// SPDX-License-Identifier: GPL-3.0-only

// Prevents additional console window on Windows in release, DO NOT REMOVE!!
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    km_editor_desktop_lib::run();
}
