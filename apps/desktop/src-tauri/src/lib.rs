// SPDX-License-Identifier: GPL-3.0-only

use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};

#[cfg(windows)]
use std::os::windows::process::CommandExt;
use tauri::{Emitter, Manager};
use tauri_plugin_shell::ShellExt;

const BRIDGE_SIDECAR_NAME: &str = "km-tools-bridge";
const WINDOW_CLOSE_REQUESTED_EVENT: &str = "km-editor://window-close-requested";
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

struct CloseGuardState {
    is_guarded: AtomicBool,
}

#[tauri::command(rename_all = "camelCase")]
async fn project_bridge_once(
    app_handle: tauri::AppHandle,
    request_json: String,
) -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || run_project_bridge_once(&app_handle, request_json))
        .await
        .map_err(|error| format!("Project bridge request task failed: {error}"))?
}

fn run_project_bridge_once(
    app_handle: &tauri::AppHandle,
    request_json: String,
) -> Result<String, String> {
    let mut command = resolve_project_bridge_command(app_handle)?;
    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);

    let mut child = command
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| format!("Could not start the project bridge runner: {error}"))?;

    let Some(mut stdin) = child.stdin.take() else {
        let _ = child.kill();
        let _ = child.wait();
        return Err("Project bridge runner did not expose stdin.".to_owned());
    };

    let write_result = stdin
        .write_all(request_json.as_bytes())
        .and_then(|_| stdin.write_all(b"\n"));
    drop(stdin);

    if let Err(error) = write_result {
        let _ = child.kill();
        let _ = child.wait();
        return Err(format!("Could not send the project bridge request: {error}"));
    }

    let output = child
        .wait_with_output()
        .map_err(|error| format!("Could not read the project bridge response: {error}"))?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);

        return Err(format!(
            "Project bridge runner exited with code {:?}: {}",
            output.status.code(),
            stderr.trim()
        ));
    }

    let stdout = String::from_utf8(output.stdout)
        .map_err(|error| format!("Project bridge response was not UTF-8: {error}"))?;

    // The backend bridge protocol is line-delimited so future multi-request transport can reuse it.
    stdout
        .lines()
        .next()
        .map(str::to_owned)
        .ok_or_else(|| "Project bridge runner returned an empty response.".to_owned())
}

fn resolve_project_bridge_command(app_handle: &tauri::AppHandle) -> Result<Command, String> {
    if let Some(command) = resolve_bundled_bridge_command(app_handle)? {
        return Ok(command);
    }

    resolve_dev_bridge_command()
}

fn resolve_bundled_bridge_command(
    app_handle: &tauri::AppHandle,
) -> Result<Option<Command>, String> {
    let sidecar_command = app_handle
        .shell()
        .sidecar(BRIDGE_SIDECAR_NAME)
        .map_err(|error| format!("Could not resolve the bundled project bridge sidecar: {error}"))?
        .arg("bridge-once");
    let command: Command = sidecar_command.into();
    let program_path = Path::new(command.get_program());

    if program_path.is_file() {
        Ok(Some(command))
    } else {
        Ok(None)
    }
}

fn resolve_dev_bridge_command() -> Result<Command, String> {
    let repo_root = resolve_repo_root()?;
    let mut command = Command::new("dotnet");
    command
        .args([
            "run",
            "--project",
            "src/KM.Tools",
            "--no-restore",
            "--",
            "bridge-once",
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

#[tauri::command]
fn set_close_guard_enabled(state: tauri::State<'_, CloseGuardState>, enabled: bool) {
    state.is_guarded.store(enabled, Ordering::SeqCst);
}

#[tauri::command]
fn exit_app(app_handle: tauri::AppHandle) {
    app_handle.exit(0);
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
        // Register shell support now so the future sidecar bridge can add a narrow command allowlist.
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            project_bridge_once,
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
