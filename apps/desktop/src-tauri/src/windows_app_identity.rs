// SPDX-License-Identifier: GPL-3.0-only

const MAX_APP_USER_MODEL_ID_CHARACTERS: usize = 128;

#[link(name = "shell32")]
extern "system" {
    fn SetCurrentProcessExplicitAppUserModelID(app_id: *const u16) -> i32;
}

pub fn set_current_process(app_id: &str) -> Result<(), String> {
    validate(app_id)?;

    let mut wide = app_id.encode_utf16().collect::<Vec<_>>();
    wide.push(0);

    // SAFETY: `wide` is a live, null-terminated UTF-16 buffer for the duration of the call.
    // The Shell API reads the string and does not retain the pointer.
    let result = unsafe { SetCurrentProcessExplicitAppUserModelID(wide.as_ptr()) };
    if result >= 0 {
        Ok(())
    } else {
        Err(format!(
            "The application taskbar identity could not be established (HRESULT 0x{:08X}).",
            result as u32
        ))
    }
}

fn validate(app_id: &str) -> Result<(), String> {
    if app_id.is_empty() {
        return Err("The application taskbar identity is empty.".to_owned());
    }
    if app_id.encode_utf16().count() > MAX_APP_USER_MODEL_ID_CHARACTERS {
        return Err(format!(
            "The application taskbar identity exceeds {MAX_APP_USER_MODEL_ID_CHARACTERS} characters."
        ));
    }
    if app_id.chars().any(char::is_whitespace) {
        return Err("The application taskbar identity contains whitespace.".to_owned());
    }
    if app_id.contains('\0') {
        return Err("The application taskbar identity contains a null character.".to_owned());
    }
    if !app_id.contains('.') {
        return Err(
            "The application taskbar identity must contain company and product sections."
                .to_owned(),
        );
    }

    Ok(())
}
