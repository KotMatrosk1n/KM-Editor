// SPDX-License-Identifier: GPL-3.0-only

use crate::delivery::{self, Result, RELEASES};
use fs2::FileExt;
use minisign_verify::PublicKey;
use reqwest::blocking::{Client, Response};
use serde::{Deserialize, Serialize};
use std::{
    fs::{self, OpenOptions},
    io::{BufReader, Read},
    path::{Path, PathBuf},
    time::Duration,
};

const CLIENT_VERSION: &str = env!("CARGO_PKG_VERSION");
const MAX_CATALOG_SIZE: u64 = 2 * 1024 * 1024;
const MAX_PACK_SIZE: u64 = 512 * 1024 * 1024;
const MAX_IMAGE_SIZE: u64 = 4 * 1024 * 1024;

#[derive(Deserialize, Serialize)]
struct SignedManifest {
    manifest: String,
    signature: String,
}

#[derive(Deserialize)]
struct Manifest {
    revision: u64,
    pack: String,
    tag: String,
    #[serde(rename = "sourceCommit")]
    source_commit: String,
    files: Vec<Entry>,
}

#[derive(Deserialize)]
struct Entry {
    path: String,
    offset: u64,
    size: u64,
    sha256: String,
}

#[derive(Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SyncResult {
    downloaded: usize,
}

fn cache_path() -> Result<PathBuf> {
    Ok(delivery::data_dir()?.join("Artwork"))
}

fn image_type(path: &str) -> Option<&'static str> {
    let (directory, name) = path.split_once('/')?;
    let (stem, extension) = name.rsplit_once('.')?;
    if stem.is_empty()
        || stem.len() > 120
        || !stem
            .bytes()
            .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == b'-' || c == b'_')
    {
        return None;
    }
    match (directory, extension) {
        ("gen5", "png") => Some("image/png"),
        ("ani", "gif") => Some("image/gif"),
        _ => None,
    }
}

pub fn response(request: tauri::http::Request<Vec<u8>>) -> tauri::http::Response<Vec<u8>> {
    let path = request.uri().path().trim_start_matches('/');
    if let Some(content_type) = image_type(path) {
        if let Ok(bytes) = cache_path().and_then(|root| Ok(fs::read(root.join(path))?)) {
            return tauri::http::Response::builder()
                .header("Content-Type", content_type)
                .header("Cache-Control", "no-store")
                .body(bytes)
                .expect("valid artwork response");
        }
    }
    tauri::http::Response::builder()
        .status(404)
        .body(Vec::new())
        .expect("valid artwork response")
}

fn validate_manifest(signed: &SignedManifest, key: &PublicKey) -> Result<Manifest> {
    delivery::verify_signature(signed.manifest.as_bytes(), &signed.signature, key)?;
    let manifest: Manifest = serde_json::from_str(&signed.manifest)?;
    let safe_tag = manifest.tag == "artwork"
        || manifest.tag.strip_prefix('v').is_some_and(|version| {
            let parts: Vec<_> = version.split('.').collect();
            parts.len() == 3 && parts.iter().all(|part| part.parse::<u16>().is_ok())
        });
    let pack_digest = manifest
        .pack
        .strip_prefix("artwork-")
        .and_then(|name| name.strip_suffix(".pack"));
    if !safe_tag
        || !pack_digest.is_some_and(delivery::is_digest)
        || manifest.source_commit.len() != 40
        || !manifest
            .source_commit
            .bytes()
            .all(|c| c.is_ascii_hexdigit())
        || manifest.files.is_empty()
        || manifest.files.len() > 20000
    {
        return Err("Invalid artwork manifest".into());
    }

    let mut offset = 0;
    let mut paths = std::collections::HashSet::new();
    for entry in &manifest.files {
        if image_type(&entry.path).is_none()
            || !paths.insert(&entry.path)
            || entry.offset != offset
            || entry.size == 0
            || entry.size > MAX_IMAGE_SIZE
            || !delivery::is_digest(&entry.sha256)
        {
            return Err("Invalid artwork file metadata".into());
        }
        offset += entry.size;
    }
    if offset > MAX_PACK_SIZE {
        return Err("Artwork pack is too large".into());
    }
    Ok(manifest)
}

fn read_catalog(reader: impl Read, key: &PublicKey) -> Result<(SignedManifest, Manifest)> {
    let mut bytes = Vec::new();
    reader.take(MAX_CATALOG_SIZE + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 > MAX_CATALOG_SIZE {
        return Err("Artwork manifest is too large".into());
    }
    let signed = serde_json::from_slice(&bytes)?;
    let manifest = validate_manifest(&signed, key)?;
    Ok((signed, manifest))
}

fn latest_manifest(client: &Client, root: &Path, key: &PublicKey) -> Result<Manifest> {
    let manifest_path = root.join("manifest.json");
    let cached = fs::File::open(&manifest_path)
        .ok()
        .and_then(|file| read_catalog(file, key).ok());
    let mut newest = None;
    for channel in ["latest/download", "download/artwork"] {
        let url = format!("{RELEASES}/{channel}/artwork.json");
        let result = client
            .get(url)
            .timeout(Duration::from_secs(15))
            .send()
            .and_then(Response::error_for_status)
            .map_err(delivery::Error::from)
            .and_then(|response| read_catalog(response, key));
        if let Ok(candidate) = result {
            if newest
                .as_ref()
                .is_none_or(|current: &(SignedManifest, Manifest)| {
                    candidate.1.revision > current.1.revision
                })
            {
                newest = Some(candidate);
            }
        }
    }
    let (signed, mut manifest) = newest.ok_or("Could not download a verified artwork manifest")?;
    if let Some((_, cached_manifest)) = cached {
        if cached_manifest.revision >= manifest.revision {
            manifest = cached_manifest;
            return Ok(manifest);
        }
    }
    delivery::write_atomically(&manifest_path, &serde_json::to_vec(&signed)?)?;
    Ok(manifest)
}

fn verified(root: &Path, entry: &Entry) -> bool {
    let path = root.join(&entry.path);
    if fs::metadata(&path).map(|metadata| metadata.len()).ok() != Some(entry.size) {
        return false;
    }
    fs::read(path).is_ok_and(|bytes| delivery::sha256(&bytes) == entry.sha256)
}

fn save(root: &Path, entry: &Entry, reader: &mut impl Read) -> Result<()> {
    let mut bytes = vec![0; entry.size as usize];
    reader.read_exact(&mut bytes)?;
    if delivery::sha256(&bytes) != entry.sha256 {
        return Err(format!("Artwork verification failed for {}", entry.path).into());
    }
    delivery::write_atomically(&root.join(&entry.path), &bytes)
}

fn fetch_range(
    client: &Client,
    url: &str,
    first: &Entry,
    last: &Entry,
    size: u64,
) -> Result<Option<Response>> {
    let start = first.offset;
    let end = last.offset + last.size - 1;
    let response = client
        .get(url)
        .header("Range", format!("bytes={start}-{end}"))
        .send()?;
    let expected = format!("bytes {start}-{end}/{size}");
    if response.status() != reqwest::StatusCode::PARTIAL_CONTENT
        || response
            .headers()
            .get("Content-Range")
            .and_then(|value| value.to_str().ok())
            != Some(&expected)
    {
        return Ok(None);
    }
    Ok(Some(response))
}

fn fetch_images(client: &Client, root: &Path, manifest: &Manifest, pack: &str) -> Result<usize> {
    let needs_image: Vec<_> = manifest
        .files
        .iter()
        .map(|entry| !verified(root, entry))
        .collect();
    let last = manifest.files.last().ok_or("Empty artwork manifest")?;
    let size = last.offset + last.size;
    let mut downloaded = 0;
    let mut use_ranges = true;

    let mut start = 0;
    while let Some(next) = needs_image[start..].iter().position(|missing| *missing) {
        start += next;
        let end = needs_image[start..]
            .iter()
            .position(|missing| !missing)
            .map_or(needs_image.len(), |length| start + length);
        let group = &manifest.files[start..end];
        start = end;
        let reader = if group.len() == manifest.files.len() {
            let response = client.get(pack).send()?;
            if response.status() == reqwest::StatusCode::NOT_FOUND {
                None
            } else {
                Some(response.error_for_status()?)
            }
        } else if use_ranges {
            let response = fetch_range(client, pack, &group[0], &group[group.len() - 1], size)?;
            use_ranges = response.is_some();
            response
        } else {
            None
        };
        if let Some(response) = reader {
            let mut reader = BufReader::new(response);
            for entry in group {
                save(root, entry, &mut reader)?;
                downloaded += 1;
            }
        } else {
            for entry in group {
                let url = format!(
                    "https://raw.githubusercontent.com/KotMatrosk1n/KM-Editor/{}/apps/desktop/public/sprites/{}",
                    manifest.source_commit, entry.path
                );
                let mut response = client.get(url).send()?.error_for_status()?;
                save(root, entry, &mut response)?;
                downloaded += 1;
            }
        }
    }
    Ok(downloaded)
}

fn synchronize(only_after_update: bool) -> Result<SyncResult> {
    let root = cache_path()?;
    fs::create_dir_all(root.join("gen5"))?;
    fs::create_dir_all(root.join("ani"))?;
    let lock = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(root.join("sync.lock"))?;
    FileExt::lock_exclusive(&lock)?;
    let version_marker = root.join("client-version");
    if only_after_update
        && fs::read_to_string(&version_marker).ok().as_deref() == Some(CLIENT_VERSION)
    {
        return Ok(SyncResult::default());
    }
    let _ = rustls::crypto::ring::default_provider().install_default();
    let client = Client::builder()
        .connect_timeout(Duration::from_secs(10))
        .timeout(Duration::from_secs(180))
        .user_agent("KM Editor artwork")
        .build()?;
    let manifest = latest_manifest(&client, &root, &delivery::public_key()?)?;
    let pack = format!("{RELEASES}/download/{}/{}", manifest.tag, manifest.pack);
    let downloaded = fetch_images(&client, &root, &manifest, &pack)?;
    delivery::write_atomically(&version_marker, CLIENT_VERSION.as_bytes())?;
    Ok(SyncResult { downloaded })
}

#[tauri::command]
pub async fn sync_artwork(only_after_update: bool) -> std::result::Result<SyncResult, String> {
    tauri::async_runtime::spawn_blocking(move || synchronize(only_after_update))
        .await
        .map_err(|error| error.to_string())?
        .map_err(|error| error.to_string())
}

pub fn sync_for_installation() -> bool {
    synchronize(true).map(|_| true).unwrap_or_else(|error| {
        eprintln!("Artwork download pending: {error}");
        false
    })
}
