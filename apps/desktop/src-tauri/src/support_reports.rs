// SPDX-License-Identifier: GPL-3.0-only

use std::fs::OpenOptions;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::Manager;

const SAVE_FAILED: &str = "The report could not be saved.";
const OPEN_FAILED: &str = "The report folder could not be opened.";

fn report_directory(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    app.path()
        .local_data_dir()
        .map(|root| root.join("KM Editor").join("Bug Reports"))
        .map_err(|_| SAVE_FAILED.to_owned())
}

#[tauri::command]
pub async fn save_support_report(app: tauri::AppHandle, report: String) -> Result<String, String> {
    let directory = report_directory(&app)?;
    tauri::async_runtime::spawn_blocking(move || {
        let timestamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_err(|_| SAVE_FAILED.to_owned())?
            .as_millis();
        save_report(&directory, &report, timestamp)
    })
    .await
    .map_err(|_| SAVE_FAILED.to_owned())?
}

fn save_report(directory: &Path, report: &str, timestamp: u128) -> Result<String, String> {
    if report.len() > 1_048_576 || serde_json::from_str::<serde_json::Value>(report).is_err() {
        return Err("The support report is invalid.".to_owned());
    }
    std::fs::create_dir_all(directory).map_err(|_| SAVE_FAILED.to_owned())?;
    for sequence in 1..=1000 {
        let name = format!(
            "KMEditor-bug-report-{}-{timestamp}-{sequence:03}.json",
            env!("CARGO_PKG_VERSION")
        );
        let path = directory.join(&name);
        let mut file = match OpenOptions::new().write(true).create_new(true).open(&path) {
            Ok(file) => file,
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => continue,
            Err(_) => return Err(SAVE_FAILED.to_owned()),
        };
        if file
            .write_all(report.as_bytes())
            .and_then(|_| file.sync_all())
            .is_err()
        {
            drop(file);
            let _ = std::fs::remove_file(&path);
            return Err(SAVE_FAILED.to_owned());
        }
        return Ok(name);
    }
    Err(SAVE_FAILED.to_owned())
}

fn report_path(directory: &Path, file_name: &str) -> Result<PathBuf, String> {
    if !file_name.starts_with("KMEditor-bug-report-")
        || !file_name.ends_with(".json")
        || !file_name
            .bytes()
            .all(|c| c.is_ascii_alphanumeric() || c == b'-' || c == b'.')
    {
        return Err(OPEN_FAILED.to_owned());
    }
    let path = directory.join(file_name);
    if !path.is_file() {
        return Err(OPEN_FAILED.to_owned());
    }
    Ok(path)
}

#[tauri::command(rename_all = "camelCase")]
pub async fn open_support_report(app: tauri::AppHandle, file_name: String) -> Result<(), String> {
    let directory = report_directory(&app).map_err(|_| OPEN_FAILED.to_owned())?;
    tauri::async_runtime::spawn_blocking(move || {
        let path = report_path(&directory, &file_name)?;
        #[cfg(windows)]
        let mut command = {
            let mut command = std::process::Command::new("explorer.exe");
            command.arg("/select,").arg(path);
            command
        };
        #[cfg(not(windows))]
        let mut command = {
            let _ = path;
            super::create_open_path_command(&directory)
        };
        let child = command.spawn().map_err(|_| OPEN_FAILED.to_owned())?;
        super::reap_open_path_child_in_background(child);
        Ok(())
    })
    .await
    .map_err(|_| OPEN_FAILED.to_owned())?
}
