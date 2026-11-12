// SPDX-License-Identifier: GPL-3.0-only

fn main() {
    let revision = std::process::Command::new("git").args(["rev-parse", "HEAD"]).output()
        .ok().filter(|result| result.status.success())
        .and_then(|result| String::from_utf8(result.stdout).ok())
        .map(|value| value.trim().to_owned())
        .filter(|value| value.len() == 40 && value.bytes().all(|c| c.is_ascii_hexdigit()))
        .unwrap_or_else(|| "unknown".to_owned());
    println!("cargo:rustc-env=KM_BUILD_COMMIT={revision}");
    let modified = std::process::Command::new("git")
        .args(["status", "--porcelain", "--untracked-files=normal"]).output()
        .ok().filter(|result| result.status.success())
        .map(|result| if result.stdout.is_empty() { "false" } else { "true" })
        .unwrap_or("unknown");
    println!("cargo:rustc-env=KM_BUILD_MODIFIED={modified}");
    // Build identity must be refreshed even when only the frontend or Git state changed.
    // Cargo reruns a build script when a declared input does not exist.
    println!("cargo:rerun-if-changed=km-build-identity-always-refresh");
    tauri_build::build()
}
