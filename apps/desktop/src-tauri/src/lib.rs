// SPDX-License-Identifier: GPL-3.0-only

use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

#[cfg(windows)]
use std::os::windows::process::CommandExt;
use tauri::{Emitter, Manager};
use tauri_plugin_shell::ShellExt;

const BRIDGE_SIDECAR_NAME: &str = "km-tools-bridge";
const MAX_PROJECT_BRIDGE_IN_FLIGHT_REQUESTS: usize = 8;
const PROJECT_BRIDGE_RECYCLED_ERROR: &str =
    "Project bridge request was canceled because the bridge was recycled.";
const WINDOW_CLOSE_REQUESTED_EVENT: &str = "km-editor://window-close-requested";
const SUPPORT_SEARCH_PROGRESS_EVENT: &str = "km-editor://support-file-search-progress";
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

struct CloseGuardState {
    is_guarded: AtomicBool,
}

#[derive(Clone)]
struct ProjectBridgeState {
    process: Arc<Mutex<Option<Arc<ProjectBridgeProcess>>>>,
    generation: Arc<AtomicUsize>,
    in_flight_requests: Arc<AtomicUsize>,
    maximum_in_flight_requests: usize,
}

impl Default for ProjectBridgeState {
    fn default() -> Self {
        Self::with_request_limit(MAX_PROJECT_BRIDGE_IN_FLIGHT_REQUESTS)
    }
}

impl ProjectBridgeState {
    fn with_request_limit(maximum_in_flight_requests: usize) -> Self {
        assert!(maximum_in_flight_requests > 0);
        Self {
            process: Arc::new(Mutex::new(None)),
            generation: Arc::new(AtomicUsize::new(0)),
            in_flight_requests: Arc::new(AtomicUsize::new(0)),
            maximum_in_flight_requests,
        }
    }

    fn try_acquire_request_permit(&self) -> Result<ProjectBridgeRequestPermit, String> {
        self.in_flight_requests
            .fetch_update(Ordering::AcqRel, Ordering::Acquire, |current| {
                (current < self.maximum_in_flight_requests).then_some(current + 1)
            })
            .map_err(|_| {
                "Project bridge request capacity is full. Wait for the current editor operation to finish and retry."
                    .to_owned()
            })?;
        Ok(ProjectBridgeRequestPermit {
            in_flight_requests: self.in_flight_requests.clone(),
        })
    }
}

struct ProjectBridgeRequestPermit {
    in_flight_requests: Arc<AtomicUsize>,
}

impl Drop for ProjectBridgeRequestPermit {
    fn drop(&mut self) {
        self.in_flight_requests.fetch_sub(1, Ordering::AcqRel);
    }
}

struct ProjectBridgeProcess {
    child: Mutex<Option<Child>>,
    io: Mutex<ProjectBridgeIo>,
}

struct ProjectBridgeIo {
    stdin: ChildStdin,
    stdout: BufReader<ChildStdout>,
}

impl Drop for ProjectBridgeProcess {
    fn drop(&mut self) {
        let child = match self.child.get_mut() {
            Ok(child) => child,
            Err(poisoned) => poisoned.into_inner(),
        };
        terminate_project_bridge_child(child);
    }
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct SupportSearchProgress {
    current_root: String,
    current_path: String,
    searched_directories: u64,
    searched_files: u64,
}

#[tauri::command(rename_all = "camelCase")]
async fn project_bridge(
    app_handle: tauri::AppHandle,
    bridge_state: tauri::State<'_, ProjectBridgeState>,
    request_json: String,
) -> Result<String, String> {
    let bridge_state = bridge_state.inner().clone();
    let request_permit = bridge_state.try_acquire_request_permit()?;
    let request_generation = bridge_state.generation.load(Ordering::Acquire);
    tauri::async_runtime::spawn_blocking(move || {
        let _request_permit = request_permit;
        run_project_bridge_request(&app_handle, &bridge_state, request_generation, request_json)
    })
    .await
    .map_err(|error| format!("Project bridge request task failed: {error}"))?
}

#[tauri::command]
async fn recycle_project_bridge(
    bridge_state: tauri::State<'_, ProjectBridgeState>,
) -> Result<(), String> {
    let bridge_state = bridge_state.inner().clone();
    tauri::async_runtime::spawn_blocking(move || recycle_project_bridge_process(&bridge_state))
        .await
        .map_err(|error| format!("Project bridge recycle task failed: {error}"))?
}

fn run_project_bridge_request(
    app_handle: &tauri::AppHandle,
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_json: String,
) -> Result<String, String> {
    run_project_bridge_request_with(bridge_state, request_generation, &request_json, || {
        start_project_bridge_process(app_handle)
    })
}

fn run_project_bridge_request_with<F>(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_json: &str,
    mut start_process: F,
) -> Result<String, String>
where
    F: FnMut() -> Result<ProjectBridgeProcess, String>,
{
    for attempt in 0..2 {
        let process = get_or_start_project_bridge_process(
            bridge_state,
            request_generation,
            &mut start_process,
        )?;
        let request_result = process.request(bridge_state, request_generation, request_json);
        match request_result {
            Ok(response) => return Ok(response),
            Err(error) => {
                remove_failed_project_bridge_process(bridge_state, &process)?;
                ensure_project_bridge_request_is_current(bridge_state, request_generation)?;
                if attempt > 0 {
                    return Err(error);
                }
            }
        }
    }

    Err("Project bridge request could not be completed.".to_owned())
}

fn get_or_start_project_bridge_process(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    start_process: &mut impl FnMut() -> Result<ProjectBridgeProcess, String>,
) -> Result<Arc<ProjectBridgeProcess>, String> {
    let mut process = bridge_state
        .process
        .lock()
        .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
    ensure_project_bridge_request_is_current(bridge_state, request_generation)?;

    if let Some(process) = process.as_ref() {
        return Ok(process.clone());
    }

    let started = Arc::new(start_process()?);
    *process = Some(started.clone());
    Ok(started)
}

fn remove_failed_project_bridge_process(
    bridge_state: &ProjectBridgeState,
    failed_process: &Arc<ProjectBridgeProcess>,
) -> Result<(), String> {
    let removed = {
        let mut current = bridge_state
            .process
            .lock()
            .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
        if current
            .as_ref()
            .is_some_and(|process| Arc::ptr_eq(process, failed_process))
        {
            current.take()
        } else {
            None
        }
    };

    if let Some(process) = removed {
        process.terminate()?;
    }
    Ok(())
}

impl ProjectBridgeProcess {
    fn new(child: Child, stdin: ChildStdin, stdout: ChildStdout) -> Self {
        Self {
            child: Mutex::new(Some(child)),
            io: Mutex::new(ProjectBridgeIo {
                stdin,
                stdout: BufReader::new(stdout),
            }),
        }
    }

    fn request(
        &self,
        bridge_state: &ProjectBridgeState,
        request_generation: usize,
        request_json: &str,
    ) -> Result<String, String> {
        let mut io = self
            .io
            .lock()
            .map_err(|_| "Project bridge I/O lock was poisoned.".to_owned())?;
        ensure_project_bridge_request_is_current(bridge_state, request_generation)?;
        io.stdin
            .write_all(request_json.as_bytes())
            .map_err(|error| format!("Could not send the project bridge request: {error}"))?;
        io.stdin
            .write_all(b"\n")
            .and_then(|_| io.stdin.flush())
            .map_err(|error| format!("Could not send the project bridge request: {error}"))?;

        let mut response = String::new();
        let bytes_read = io
            .stdout
            .read_line(&mut response)
            .map_err(|error| format!("Could not read the project bridge response: {error}"))?;
        if bytes_read == 0 {
            return Err("Project bridge runner returned an empty response.".to_owned());
        }

        while response.ends_with(['\r', '\n']) {
            response.pop();
        }

        ensure_project_bridge_request_is_current(bridge_state, request_generation)?;
        Ok(response)
    }

    fn terminate(&self) -> Result<(), String> {
        let mut child = self
            .child
            .lock()
            .map_err(|_| "Project bridge child lock was poisoned.".to_owned())?;
        terminate_project_bridge_child(&mut child);
        Ok(())
    }
}

fn ensure_project_bridge_request_is_current(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
) -> Result<(), String> {
    if bridge_state.generation.load(Ordering::Acquire) == request_generation {
        Ok(())
    } else {
        Err(PROJECT_BRIDGE_RECYCLED_ERROR.to_owned())
    }
}

fn terminate_project_bridge_child(child: &mut Option<Child>) {
    if let Some(mut child) = child.take() {
        let _ = child.kill();
        let _ = child.wait();
    }
}

fn start_project_bridge_process(
    app_handle: &tauri::AppHandle,
) -> Result<ProjectBridgeProcess, String> {
    let mut command = resolve_project_bridge_command(app_handle, "bridge")?;
    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);
    let mut child = command
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|error| format!("Could not start the project bridge runner: {error}"))?;
    let Some(stdin) = child.stdin.take() else {
        let _ = child.kill();
        let _ = child.wait();
        return Err("Project bridge runner did not expose stdin.".to_owned());
    };
    let Some(stdout) = child.stdout.take() else {
        let _ = child.kill();
        let _ = child.wait();
        return Err("Project bridge runner did not expose stdout.".to_owned());
    };

    Ok(ProjectBridgeProcess::new(child, stdin, stdout))
}

fn resolve_project_bridge_command(
    app_handle: &tauri::AppHandle,
    bridge_mode: &str,
) -> Result<Command, String> {
    if let Some(command) = resolve_bundled_bridge_command(app_handle, bridge_mode)? {
        return Ok(command);
    }

    resolve_dev_bridge_command(bridge_mode)
}

fn resolve_bundled_bridge_command(
    app_handle: &tauri::AppHandle,
    bridge_mode: &str,
) -> Result<Option<Command>, String> {
    let sidecar_command = app_handle
        .shell()
        .sidecar(BRIDGE_SIDECAR_NAME)
        .map_err(|error| format!("Could not resolve the bundled project bridge sidecar: {error}"))?
        .arg(bridge_mode);
    let command: Command = sidecar_command.into();
    let program_path = Path::new(command.get_program());

    if program_path.is_file() {
        Ok(Some(command))
    } else {
        Ok(None)
    }
}

fn resolve_dev_bridge_command(bridge_mode: &str) -> Result<Command, String> {
    let repo_root = resolve_repo_root()?;
    let mut command = Command::new("dotnet");
    command
        .args([
            "run",
            "--project",
            "src/KM.Tools",
            "--no-restore",
            "--",
            bridge_mode,
        ])
        .current_dir(repo_root);

    Ok(command)
}

#[tauri::command(rename_all = "camelCase")]
fn open_path(path: String) -> Result<(), String> {
    let trimmed_path = path.trim();

    if trimmed_path.is_empty() {
        return Err("No folder path was provided.".to_owned());
    }

    let path = PathBuf::from(trimmed_path);

    if !path.is_dir() {
        return Err("The folder does not exist.".to_owned());
    }

    let mut command = create_open_path_command(&path);
    command
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("Could not open the folder: {error}"))
}

#[tauri::command(rename_all = "camelCase")]
fn create_directory(path: String) -> Result<(), String> {
    let trimmed_path = path.trim();

    if trimmed_path.is_empty() {
        return Err("No folder path was provided.".to_owned());
    }

    let path = PathBuf::from(trimmed_path);

    if path.exists() {
        return if path.is_dir() {
            Err("The output root folder already exists.".to_owned())
        } else {
            Err("A file already exists at the output root path.".to_owned())
        };
    }

    std::fs::create_dir(&path).map_err(|error| format!("Could not create the folder: {error}"))
}

#[tauri::command(rename_all = "camelCase")]
async fn find_support_file_folder(app_handle: tauri::AppHandle) -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || find_support_file_blocking(&app_handle))
        .await
        .map_err(|error| format!("S/V support file search task failed: {error}"))?
}

fn find_support_file_blocking(app_handle: &tauri::AppHandle) -> Result<Option<String>, String> {
    let mut searched_directories = 0_u64;
    let mut searched_files = 0_u64;
    let mut last_emit = Instant::now()
        .checked_sub(Duration::from_secs(1))
        .unwrap_or_else(Instant::now);

    for root in enumerate_filesystem_roots() {
        let root_label = root.display().to_string();
        let mut stack = vec![root.clone()];

        while let Some(directory) = stack.pop() {
            searched_directories = searched_directories.saturating_add(1);
            if last_emit.elapsed() >= Duration::from_millis(200) {
                emit_support_search_progress(
                    app_handle,
                    &root_label,
                    &directory,
                    searched_directories,
                    searched_files,
                );
                last_emit = Instant::now();
            }

            let Ok(entries) = std::fs::read_dir(&directory) else {
                continue;
            };

            for entry in entries.flatten() {
                let path = entry.path();
                let Ok(file_type) = entry.file_type() else {
                    continue;
                };

                if file_type.is_dir() {
                    stack.push(path);
                    continue;
                }

                if !file_type.is_file() {
                    continue;
                }

                searched_files = searched_files.saturating_add(1);
                if is_required_support_file(&entry.file_name().to_string_lossy()) {
                    emit_support_search_progress(
                        app_handle,
                        &root_label,
                        &path,
                        searched_directories,
                        searched_files,
                    );
                    return Ok(path.parent().map(|parent| parent.display().to_string()));
                }
            }
        }
    }

    Ok(None)
}

fn emit_support_search_progress(
    app_handle: &tauri::AppHandle,
    current_root: &str,
    current_path: &Path,
    searched_directories: u64,
    searched_files: u64,
) {
    let _ = app_handle.emit(
        SUPPORT_SEARCH_PROGRESS_EVENT,
        SupportSearchProgress {
            current_root: current_root.to_owned(),
            current_path: current_path.display().to_string(),
            searched_directories,
            searched_files,
        },
    );
}

fn is_required_support_file(file_name: &str) -> bool {
    file_name.eq_ignore_ascii_case(&required_support_file_name())
}

fn required_support_file_name() -> String {
    ["oo2", "core", "_8_", "win", "64", ".dll"].concat()
}

#[cfg(windows)]
fn enumerate_filesystem_roots() -> Vec<PathBuf> {
    ('A'..='Z')
        .map(|drive| PathBuf::from(format!("{drive}:\\")))
        .filter(|path| path.exists())
        .collect()
}

#[cfg(not(windows))]
fn enumerate_filesystem_roots() -> Vec<PathBuf> {
    vec![PathBuf::from("/")]
}

#[tauri::command]
fn set_close_guard_enabled(state: tauri::State<'_, CloseGuardState>, enabled: bool) {
    state.is_guarded.store(enabled, Ordering::SeqCst);
}

#[tauri::command]
fn exit_app(app_handle: tauri::AppHandle) {
    shutdown_project_bridge(&app_handle);
    app_handle.exit(0);
}

fn shutdown_project_bridge(app_handle: &tauri::AppHandle) {
    let bridge_state = app_handle.state::<ProjectBridgeState>();
    if let Ok(Some(process)) = detach_project_bridge_process(&bridge_state) {
        let _ = process.terminate();
    }
}

fn recycle_project_bridge_process(bridge_state: &ProjectBridgeState) -> Result<(), String> {
    if let Some(process) = detach_project_bridge_process(bridge_state)? {
        process.terminate()?;
    }
    Ok(())
}

fn detach_project_bridge_process(
    bridge_state: &ProjectBridgeState,
) -> Result<Option<Arc<ProjectBridgeProcess>>, String> {
    let mut process = bridge_state
        .process
        .lock()
        .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
    bridge_state.generation.fetch_add(1, Ordering::AcqRel);
    Ok(process.take())
}

#[cfg(windows)]
fn create_open_path_command(path: &Path) -> Command {
    let mut command = Command::new("explorer.exe");
    command.arg(path);
    command
}

#[cfg(target_os = "macos")]
fn create_open_path_command(path: &Path) -> Command {
    let mut command = Command::new("open");
    command.arg(path);
    command
}

#[cfg(all(unix, not(target_os = "macos")))]
fn create_open_path_command(path: &Path) -> Command {
    let mut command = Command::new("xdg-open");
    command.arg(path);
    command
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(CloseGuardState {
            is_guarded: AtomicBool::new(false),
        })
        .manage(ProjectBridgeState::default())
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_process::init())
        .setup(|app| {
            #[cfg(desktop)]
            app.handle()
                .plugin(tauri_plugin_updater::Builder::new().build())?;

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            project_bridge,
            recycle_project_bridge,
            create_directory,
            find_support_file_folder,
            open_path,
            set_close_guard_enabled,
            exit_app
        ])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                let app_handle = window.app_handle();
                let close_guard = app_handle.state::<CloseGuardState>();

                if close_guard.is_guarded.load(Ordering::SeqCst) {
                    api.prevent_close();
                    let _ = window.emit(WINDOW_CLOSE_REQUESTED_EVENT, ());
                } else {
                    shutdown_project_bridge(&app_handle);
                    app_handle.exit(0);
                }
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{mpsc, TryLockError};
    use std::thread;

    #[test]
    fn project_bridge_request_admission_is_bounded_and_reusable() {
        let state = ProjectBridgeState::with_request_limit(2);
        let first = state.try_acquire_request_permit().unwrap();
        let second = state.try_acquire_request_permit().unwrap();

        let error = match state.try_acquire_request_permit() {
            Ok(_) => panic!("request admission exceeded its configured bound"),
            Err(error) => error,
        };
        assert!(error.contains("capacity is full"));

        drop(first);
        let replacement = state.try_acquire_request_permit().unwrap();
        drop(replacement);
        drop(second);
        assert_eq!(state.in_flight_requests.load(Ordering::Acquire), 0);
    }

    #[test]
    fn recycling_an_idle_bridge_keeps_lazy_start_state() {
        let state = ProjectBridgeState::default();
        let generation = state.generation.load(Ordering::Acquire);

        recycle_project_bridge_process(&state).unwrap();

        assert!(state.process.lock().unwrap().is_none());
        assert_eq!(state.generation.load(Ordering::Acquire), generation + 1);
    }

    #[test]
    fn stale_bridge_generation_is_rejected_before_io() {
        let state = ProjectBridgeState::default();
        let process = Arc::new(start_responding_test_process().unwrap());
        *state.process.lock().unwrap() = Some(process.clone());
        let stale_generation = state.generation.load(Ordering::Acquire);
        state.generation.fetch_add(1, Ordering::AcqRel);

        let error = process
            .request(&state, stale_generation, "stale request")
            .unwrap_err();
        assert!(error.contains("bridge was recycled"));
        let child_is_running = process
            .child
            .lock()
            .unwrap()
            .as_mut()
            .unwrap()
            .try_wait()
            .unwrap()
            .is_none();
        assert!(child_is_running, "stale request reached bridge I/O");

        recycle_project_bridge_process(&state).unwrap();
    }

    #[test]
    fn recycling_a_nonresponding_bridge_cancels_stale_work_and_allows_lazy_restart() {
        let state = ProjectBridgeState::default();
        let nonresponding_process = Arc::new(start_nonresponding_test_process().unwrap());
        *state.process.lock().unwrap() = Some(nonresponding_process.clone());
        let stale_generation = state.generation.load(Ordering::Acquire);
        let process_start_count = Arc::new(AtomicUsize::new(0));

        let (request_result_sender, request_result_receiver) = mpsc::channel();
        let request_state = state.clone();
        let request_start_count = process_start_count.clone();
        let request_thread = thread::spawn(move || {
            let result = run_project_bridge_request_with(
                &request_state,
                stale_generation,
                "stale request",
                || {
                    request_start_count.fetch_add(1, Ordering::AcqRel);
                    start_responding_test_process()
                },
            );
            let _ = request_result_sender.send(result);
        });

        wait_until_request_holds_io(&nonresponding_process);

        let (recycle_result_sender, recycle_result_receiver) = mpsc::channel();
        let recycle_state = state.clone();
        let recycle_thread = thread::spawn(move || {
            let _ = recycle_result_sender.send(recycle_project_bridge_process(&recycle_state));
        });

        recycle_result_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("recycle waited on the blocked project bridge read")
            .unwrap();
        let stale_error = request_result_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("recycle did not unblock the stale project bridge read")
            .unwrap_err();
        assert!(stale_error.contains("bridge was recycled"));
        request_thread.join().unwrap();
        recycle_thread.join().unwrap();
        assert_eq!(process_start_count.load(Ordering::Acquire), 0);
        assert!(state.process.lock().unwrap().is_none());

        let fresh_generation = state.generation.load(Ordering::Acquire);
        let fresh_response =
            run_project_bridge_request_with(&state, fresh_generation, "fresh request", || {
                process_start_count.fetch_add(1, Ordering::AcqRel);
                start_responding_test_process()
            })
            .unwrap();
        assert_eq!(fresh_response, "ok");
        assert_eq!(process_start_count.load(Ordering::Acquire), 1);
        assert!(state.process.lock().unwrap().is_some());

        recycle_project_bridge_process(&state).unwrap();
    }

    fn wait_until_request_holds_io(process: &Arc<ProjectBridgeProcess>) {
        let deadline = Instant::now() + Duration::from_secs(5);
        loop {
            match process.io.try_lock() {
                Err(TryLockError::WouldBlock) => return,
                Err(TryLockError::Poisoned(_)) => panic!("project bridge I/O lock was poisoned"),
                Ok(guard) => drop(guard),
            }

            assert!(
                Instant::now() < deadline,
                "request did not begin project bridge I/O"
            );
            thread::sleep(Duration::from_millis(10));
        }
    }

    fn start_nonresponding_test_process() -> Result<ProjectBridgeProcess, String> {
        #[cfg(windows)]
        let mut command = {
            let mut command = Command::new("powershell.exe");
            command.args([
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$null = [Console]::In.ReadLine(); Start-Sleep -Seconds 30",
            ]);
            command.creation_flags(CREATE_NO_WINDOW);
            command
        };
        #[cfg(unix)]
        let mut command = {
            let mut command = Command::new("sh");
            command.args(["-c", "IFS= read -r line; IFS= read -r blocked"]);
            command
        };

        spawn_test_project_bridge(&mut command)
    }

    fn start_responding_test_process() -> Result<ProjectBridgeProcess, String> {
        #[cfg(windows)]
        let mut command = {
            let mut command = Command::new("cmd.exe");
            command.args(["/d", "/s", "/c", "set /p line= & echo ok"]);
            command.creation_flags(CREATE_NO_WINDOW);
            command
        };
        #[cfg(unix)]
        let mut command = {
            let mut command = Command::new("sh");
            command.args(["-c", "IFS= read -r line; printf 'ok\\n'"]);
            command
        };

        spawn_test_project_bridge(&mut command)
    }

    fn spawn_test_project_bridge(command: &mut Command) -> Result<ProjectBridgeProcess, String> {
        let mut child = command
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .map_err(|error| format!("Could not start test bridge child: {error}"))?;
        let stdin = child
            .stdin
            .take()
            .ok_or_else(|| "Test bridge child did not expose stdin.".to_owned())?;
        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| "Test bridge child did not expose stdout.".to_owned())?;
        Ok(ProjectBridgeProcess::new(child, stdin, stdout))
    }
}

fn resolve_repo_root() -> Result<PathBuf, String> {
    #[cfg(debug_assertions)]
    let manifest_dir = Some(PathBuf::from(env!("CARGO_MANIFEST_DIR")));
    #[cfg(not(debug_assertions))]
    let manifest_dir: Option<PathBuf> = None;
    let current_dir = std::env::current_dir()
        .map_err(|error| format!("Could not inspect current directory: {error}"))?;
    let current_exe = std::env::current_exe()
        .ok()
        .and_then(|path| path.parent().map(Path::to_path_buf));

    // Tauri can launch from different working directories in dev/build flows; walk known anchors.
    [manifest_dir, Some(current_dir), current_exe]
        .into_iter()
        .flatten()
        .find_map(find_repo_root)
        .ok_or_else(|| {
            "Could not locate the repository root for the project bridge runner.".to_owned()
        })
}

fn find_repo_root(start_path: PathBuf) -> Option<PathBuf> {
    start_path
        .ancestors()
        .find(|path| path.join("KM.Editor.slnx").is_file())
        .map(Path::to_path_buf)
}
