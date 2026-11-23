// SPDX-License-Identifier: GPL-3.0-only

use base64::{engine::general_purpose::STANDARD, Engine};
use minisign_verify::{PublicKey, Signature};
use sha2::{Digest, Sha256};
use std::{
    io::Write,
    path::{Path, PathBuf},
};

pub type Error = Box<dyn std::error::Error + Send + Sync>;
pub type Result<T> = std::result::Result<T, Error>;

pub const RELEASES: &str = "https://github.com/KotMatrosk1n/KM-Editor/releases";

pub fn data_dir() -> Result<PathBuf> {
    Ok(dirs::data_local_dir()
        .ok_or("Unable to locate local application data")?
        .join("app.kmeditor.desktop"))
}

pub fn public_key() -> Result<PublicKey> {
    let config: serde_json::Value = serde_json::from_str(include_str!("../tauri.conf.json"))?;
    let key = config["plugins"]["updater"]["pubkey"]
        .as_str()
        .ok_or("Missing updater key")?;
    Ok(PublicKey::decode(&String::from_utf8(
        STANDARD.decode(key)?,
    )?)?)
}

pub fn verify_signature(bytes: &[u8], signature: &str, key: &PublicKey) -> Result<()> {
    let signature = Signature::decode(&String::from_utf8(STANDARD.decode(signature.trim())?)?)?;
    key.verify(bytes, &signature, true)?;
    Ok(())
}

pub fn sha256(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
}

pub fn is_digest(digest: &str) -> bool {
    digest.len() == 64
        && digest
            .bytes()
            .all(|c| c.is_ascii_digit() || (b'a'..=b'f').contains(&c))
}

pub fn write_atomically(path: &Path, bytes: &[u8]) -> Result<()> {
    let parent = path.parent().ok_or("Missing output directory")?;
    let mut file = tempfile::NamedTempFile::new_in(parent)?;
    file.write_all(bytes)?;
    file.as_file().sync_all()?;
    file.persist(path)?;
    Ok(())
}
