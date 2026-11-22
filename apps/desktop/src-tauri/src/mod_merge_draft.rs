// SPDX-License-Identifier: GPL-3.0-only

#[cfg(windows)]
mod store;

const UNAVAILABLE: &str = "Mod Merger draft storage is unavailable.";

#[cfg(windows)]
fn draft_path(app: &tauri::AppHandle) -> Result<std::path::PathBuf, String> {
    use tauri::Manager;

    let root = match std::env::var_os("KM_EDITOR_APP_DATA_ROOT").filter(|value| !value.is_empty()) {
        Some(root) => std::path::PathBuf::from(root),
        None => app
            .path()
            .local_data_dir()
            .map_err(|_| UNAVAILABLE.to_owned())?
            .join("KM Editor"),
    };
    if !root.is_absolute() {
        return Err(UNAVAILABLE.to_owned());
    }
    Ok(root.join("Drafts").join("mod-merger.v2.dpapi"))
}

#[tauri::command]
pub async fn load_mod_merge_draft(app: tauri::AppHandle) -> Result<Option<String>, String> {
    #[cfg(windows)]
    {
        let path = draft_path(&app)?;
        tauri::async_runtime::spawn_blocking(move || {
            store::load(&path).map_err(|_| UNAVAILABLE.to_owned())
        })
        .await
        .map_err(|_| UNAVAILABLE.to_owned())?
    }
    #[cfg(not(windows))]
    {
        let _ = app;
        Err(UNAVAILABLE.to_owned())
    }
}

#[tauri::command]
pub async fn save_mod_merge_draft(app: tauri::AppHandle, draft: String) -> Result<(), String> {
    #[cfg(windows)]
    {
        let path = draft_path(&app)?;
        tauri::async_runtime::spawn_blocking(move || {
            store::save(&path, &draft).map_err(|_| UNAVAILABLE.to_owned())
        })
        .await
        .map_err(|_| UNAVAILABLE.to_owned())?
    }
    #[cfg(not(windows))]
    {
        let _ = (app, draft);
        Err(UNAVAILABLE.to_owned())
    }
}
