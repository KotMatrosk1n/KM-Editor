// SPDX-License-Identifier: GPL-3.0-only

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        // Register shell support now so the future sidecar bridge can add a narrow command allowlist.
        .plugin(tauri_plugin_shell::init())
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
