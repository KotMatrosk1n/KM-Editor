// SPDX-License-Identifier: GPL-3.0-only

use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::Path;
use std::ptr::{null, null_mut};
use windows_sys::Win32::Foundation::LocalFree;
use windows_sys::Win32::Security::Cryptography::{
    CryptProtectData, CryptUnprotectData, CRYPTPROTECT_UI_FORBIDDEN, CRYPT_INTEGER_BLOB,
};

// The frontend bounds JSON to 512,000 UTF-16 code units.
const MAX_JSON_BYTES: usize = 512_000 * 3;
const MAX_PROTECTED_BYTES: usize = MAX_JSON_BYTES + 16_384;

struct DpapiOutput(CRYPT_INTEGER_BLOB);

impl DpapiOutput {
    fn new() -> Self {
        Self(CRYPT_INTEGER_BLOB {
            cbData: 0,
            pbData: null_mut(),
        })
    }

    fn bytes(&self, max: usize) -> io::Result<&[u8]> {
        let len = self.0.cbData as usize;
        if self.0.pbData.is_null() || len == 0 || len > max {
            return Err(io::ErrorKind::InvalidData.into());
        }
        // DPAPI allocated and initialized this buffer. It remains owned by this output.
        Ok(unsafe { std::slice::from_raw_parts(self.0.pbData, len) })
    }
}

impl Drop for DpapiOutput {
    fn drop(&mut self) {
        if !self.0.pbData.is_null() {
            // CryptProtectData and CryptUnprotectData allocate output with LocalAlloc.
            unsafe { LocalFree(self.0.pbData.cast()) };
        }
    }
}

fn input_blob(bytes: &[u8]) -> CRYPT_INTEGER_BLOB {
    CRYPT_INTEGER_BLOB {
        cbData: bytes.len() as u32,
        pbData: bytes.as_ptr().cast_mut(),
    }
}

pub(super) fn load(path: &Path) -> io::Result<Option<String>> {
    let file = match File::open(path) {
        Ok(file) => file,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(error),
    };
    let mut protected = Vec::new();
    file.take((MAX_PROTECTED_BYTES + 1) as u64)
        .read_to_end(&mut protected)?;
    if protected.is_empty() || protected.len() > MAX_PROTECTED_BYTES {
        return Err(io::ErrorKind::InvalidData.into());
    }

    let mut output = DpapiOutput::new();
    // All optional arguments are null and the input remains valid for the call.
    let success = unsafe {
        CryptUnprotectData(
            &input_blob(&protected),
            null_mut(),
            null(),
            null(),
            null(),
            CRYPTPROTECT_UI_FORBIDDEN,
            &mut output.0,
        )
    };
    if success == 0 {
        return Err(io::Error::last_os_error());
    }
    let draft = std::str::from_utf8(output.bytes(MAX_JSON_BYTES)?)
        .map_err(|_| io::ErrorKind::InvalidData)?;
    Ok(Some(draft.to_owned()))
}

pub(super) fn save(path: &Path, draft: &str) -> io::Result<()> {
    if draft.is_empty() || draft.len() > MAX_JSON_BYTES {
        return Err(io::ErrorKind::InvalidInput.into());
    }
    let mut output = DpapiOutput::new();
    // User scope is DPAPI's default. All optional arguments are null.
    let success = unsafe {
        CryptProtectData(
            &input_blob(draft.as_bytes()),
            null(),
            null(),
            null(),
            null(),
            CRYPTPROTECT_UI_FORBIDDEN,
            &mut output.0,
        )
    };
    if success == 0 {
        return Err(io::Error::last_os_error());
    }

    let directory = path.parent().ok_or(io::ErrorKind::InvalidInput)?;
    fs::create_dir_all(directory)?;
    let mut temporary = tempfile::NamedTempFile::new_in(directory)?;
    temporary.write_all(output.bytes(MAX_PROTECTED_BYTES)?)?;
    temporary.as_file().sync_all()?;
    temporary.persist(path).map_err(|error| error.error)?;
    Ok(())
}
