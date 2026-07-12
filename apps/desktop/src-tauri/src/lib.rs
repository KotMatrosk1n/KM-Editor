// SPDX-License-Identifier: GPL-3.0-only

use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

#[cfg(windows)]
use std::os::windows::process::CommandExt;
use tauri::{Emitter, Manager};
use tauri_plugin_shell::ShellExt;

const BRIDGE_SIDECAR_NAME: &str = "km-tools-bridge";
const WINDOW_CLOSE_REQUESTED_EVENT: &str = "km-editor://window-close-requested";
const SUPPORT_SEARCH_PROGRESS_EVENT: &str = "km-editor://support-file-search-progress";
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

struct CloseGuardState {
    is_guarded: AtomicBool,
}

#[derive(Clone, Default)]
struct ProjectBridgeState {
    process: Arc<Mutex<Option<ProjectBridgeProcess>>>,
}

struct ProjectBridgeProcess {
    child: Child,
    stdin: ChildStdin,
    stdout: BufReader<ChildStdout>,
}

impl Drop for ProjectBridgeProcess {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
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
    tauri::async_runtime::spawn_blocking(move || {
        run_project_bridge_request(&app_handle, &bridge_state, request_json)
    })
    .await
    .map_err(|error| format!("Project bridge request task failed: {error}"))?
}

fn run_project_bridge_request(
    app_handle: &tauri::AppHandle,
    bridge_state: &ProjectBridgeState,
    request_json: String,
) -> Result<String, String> {
    let mut process_guard = bridge_state
        .process
        .lock()
        .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;

    for attempt in 0..2 {
        if process_guard.is_none() {
            *process_guard = Some(start_project_bridge_process(app_handle)?);
        }

        let request_result = process_guard
            .as_mut()
            .expect("project bridge process was initialized")
            .request(&request_json);
        match request_result {
            Ok(response) => return Ok(response),
            Err(_) if attempt == 0 => {
                *process_guard = None;
            }
            Err(error) => return Err(error),
        }
    }

    Err("Project bridge request could not be completed.".to_owned())
}

impl ProjectBridgeProcess {
    fn request(&mut self, request_json: &str) -> Result<String, String> {
        self.stdin
            .write_all(request_json.as_bytes())
            .and_then(|_| self.stdin.write_all(b"\n"))
            .and_then(|_| self.stdin.flush())
            .map_err(|error| format!("Could not send the project bridge request: {error}"))?;

        let mut response = String::new();
        let bytes_read = self
            .stdout
            .read_line(&mut response)
            .map_err(|error| format!("Could not read the project bridge response: {error}"))?;
        if bytes_read == 0 {
            return Err("Project bridge runner returned an empty response.".to_owned());
        }

        while response.ends_with(['\r', '\n']) {
            response.pop();
        }

        Ok(response)
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

    Ok(ProjectBridgeProcess {
        child,
        stdin,
        stdout: BufReader::new(stdout),
    })
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
    let process = bridge_state.process.try_lock();
    if let Ok(mut process) = process {
        *process = None;
    }
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
