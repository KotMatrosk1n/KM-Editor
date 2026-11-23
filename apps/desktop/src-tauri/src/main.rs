// SPDX-License-Identifier: GPL-3.0-only

// Prevents additional console window on Windows in release, DO NOT REMOVE!!
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    if std::env::args_os().nth(1).as_deref() == Some(std::ffi::OsStr::new("--sync-artwork")) {
        std::process::exit(if km_editor_desktop_lib::sync_artwork_for_installation() {
            0
        } else {
            1
        });
    }
    km_editor_desktop_lib::run();
}
