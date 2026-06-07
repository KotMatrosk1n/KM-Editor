// SPDX-License-Identifier: GPL-3.0-only

use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

#[tauri::command(rename_all = "camelCase")]
fn project_bridge_once(request_json: String) -> Result<String, String> {
    let repo_root = resolve_repo_root()?;
    let mut child = Command::new("dotnet")
        .args([
            "run",
            "--project",
            "src/KM.Tools",
            "--no-restore",
            "--",
            "bridge-once",
        ])
        .current_dir(repo_root)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| format!("Could not start the project bridge runner: {error}"))?;

    let mut stdin = child
        .stdin
        .take()
        .ok_or_else(|| "Project bridge runner did not expose stdin.".to_owned())?;
    stdin
        .write_all(request_json.as_bytes())
        .and_then(|_| stdin.write_all(b"\n"))
        .map_err(|error| format!("Could not send the project bridge request: {error}"))?;

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

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        // Register shell support now so the future sidecar bridge can add a narrow command allowlist.
        .plugin(tauri_plugin_shell::init())
        .invoke_handler(tauri::generate_handler![project_bridge_once])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

fn resolve_repo_root() -> Result<PathBuf, String> {
    let manifest_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let current_dir = std::env::current_dir()
        .map_err(|error| format!("Could not inspect current directory: {error}"))?;
    let current_exe = std::env::current_exe()
        .ok()
        .and_then(|path| path.parent().map(Path::to_path_buf));

    // Tauri can launch from different working directories in dev/build flows; walk known anchors.
    [Some(manifest_dir), Some(current_dir), current_exe]
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
