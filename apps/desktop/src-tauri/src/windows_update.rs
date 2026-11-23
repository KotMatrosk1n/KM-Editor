// SPDX-License-Identifier: GPL-3.0-only

#[cfg(windows)]
mod implementation {
    use crate::delivery::{self, Result, RELEASES};
    use serde::{Deserialize, Serialize};
    use std::{
        fs,
        io::{Cursor, Read},
        time::Duration,
    };
    use tauri::{ipc::Channel, AppHandle};
    use tauri_plugin_updater::{Update, UpdaterExt};

    const MAX_SETUP_SIZE: u64 = 512 * 1024 * 1024;

    #[derive(Deserialize)]
    struct Delta {
        format: String,
        url: String,
        base_size: u64,
        base_sha256: String,
        patch_size: u64,
        patch_sha256: String,
        target_size: u64,
        target_sha256: String,
    }

    #[derive(Serialize)]
    #[serde(tag = "event", content = "data")]
    pub enum DownloadEvent {
        Started {
            #[serde(rename = "contentLength")]
            content_length: Option<u64>,
        },
        Progress {
            #[serde(rename = "chunkLength")]
            chunk_length: usize,
        },
        Finished,
    }

    fn reconstruct(base: &[u8], patch: &[u8], delta: &Delta) -> Result<Vec<u8>> {
        if delivery::sha256(patch) != delta.patch_sha256 {
            return Err("Setup delta SHA-256 mismatch".into());
        }
        let mut decoder = zstd::stream::read::Decoder::with_dictionary(Cursor::new(patch), base)?;
        decoder.window_log_max(29)?;
        let mut target = Vec::new();
        decoder
            .take(delta.target_size + 1)
            .read_to_end(&mut target)?;
        if target.len() as u64 != delta.target_size
            || delivery::sha256(&target) != delta.target_sha256
        {
            return Err("Reconstructed setup did not match the published bytes".into());
        }
        Ok(target)
    }

    async fn try_delta(update: &Update, progress: &Channel<DownloadEvent>) -> Result<Vec<u8>> {
        let delta: Delta = serde_json::from_value(
            update.raw_json["setup_deltas"][&update.current_version].clone(),
        )?;
        let expected_url = format!(
            "{RELEASES}/download/v{}/KM.Editor.Setup_{}_from_{}_x64.zst",
            update.version, update.version, update.current_version
        );
        if delta.format != "zstd-patch-v1"
            || delta.url != expected_url
            || !(1..=MAX_SETUP_SIZE).contains(&delta.base_size)
            || !(1..=MAX_SETUP_SIZE).contains(&delta.target_size)
            || delta.patch_size == 0
            || delta.patch_size >= delta.target_size
            || !delivery::is_digest(&delta.base_sha256)
            || !delivery::is_digest(&delta.patch_sha256)
            || !delivery::is_digest(&delta.target_sha256)
        {
            return Err("Invalid setup delta metadata".into());
        }
        let source = delivery::data_dir()?.join("Update").join("base.bin");
        let base_size = delta.base_size;
        let base_sha256 = delta.base_sha256.clone();
        let base = tauri::async_runtime::spawn_blocking(move || {
            let file = fs::File::open(source)?;
            if file.metadata()?.len() != base_size {
                return Err("Cached setup length mismatch".into());
            }
            let mut base = Vec::new();
            file.take(base_size + 1).read_to_end(&mut base)?;
            if base.len() as u64 != base_size || delivery::sha256(&base) != base_sha256 {
                return Err("Cached setup SHA-256 mismatch".into());
            }
            Ok::<_, delivery::Error>(base)
        })
        .await??;

        let mut response = reqwest::Client::builder()
            .connect_timeout(Duration::from_secs(12))
            .timeout(Duration::from_secs(600))
            .user_agent("KM Editor updater")
            .build()?
            .get(&delta.url)
            .send()
            .await?
            .error_for_status()?;
        let _ = progress.send(DownloadEvent::Started {
            content_length: Some(delta.patch_size),
        });
        let mut patch = Vec::new();
        while let Some(chunk) = response.chunk().await? {
            if patch.len() as u64 + chunk.len() as u64 > delta.patch_size {
                return Err("Setup delta exceeded its published length".into());
            }
            patch.extend_from_slice(&chunk);
            let _ = progress.send(DownloadEvent::Progress {
                chunk_length: chunk.len(),
            });
        }
        if patch.len() as u64 != delta.patch_size {
            return Err("Setup delta download was incomplete".into());
        }
        let signature = update.signature.clone();
        tauri::async_runtime::spawn_blocking(move || {
            let target = reconstruct(&base, &patch, &delta)?;
            delivery::verify_signature(&target, &signature, &delivery::public_key()?)?;
            Ok(target)
        })
        .await?
    }

    pub async fn install(
        app: AppHandle,
        version: String,
        progress: Channel<DownloadEvent>,
    ) -> Result<()> {
        let updater = app
            .updater_builder()
            .header("Cache-Control", "no-cache")?
            .build()?;
        // Keep the metadata request short without setting a short deadline on the setup transfer.
        let update = tokio::time::timeout(Duration::from_secs(15), updater.check())
            .await??
            .ok_or("No KM Editor update is available")?;
        if update.version != version {
            return Err("The latest version changed. Check for updates again.".into());
        }

        let bytes = match try_delta(&update, &progress).await {
            Ok(bytes) => bytes,
            Err(_) => {
                let mut started = false;
                update
                    .download(
                        |chunk_length, content_length| {
                            if !started {
                                let _ = progress.send(DownloadEvent::Started { content_length });
                                started = true;
                            }
                            let _ = progress.send(DownloadEvent::Progress { chunk_length });
                        },
                        || {},
                    )
                    .await?
            }
        };
        let _ = progress.send(DownloadEvent::Finished);
        update.install(bytes)?;
        Ok(())
    }
}

#[cfg(windows)]
#[tauri::command]
pub async fn install_windows_update(
    app: tauri::AppHandle,
    version: String,
    progress: tauri::ipc::Channel<implementation::DownloadEvent>,
) -> std::result::Result<(), String> {
    implementation::install(app, version, progress)
        .await
        .map_err(|error| error.to_string())
}

#[cfg(not(windows))]
#[tauri::command]
pub fn install_windows_update() -> std::result::Result<(), String> {
    Err("Windows setup updates are unavailable on this platform".into())
}
