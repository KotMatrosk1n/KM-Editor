# SPDX-License-Identifier: GPL-3.0-only

$ErrorActionPreference = 'Stop'

$previousTauriConfig = $env:TAURI_CONFIG
$env:TAURI_CONFIG = '{"bundle":{"externalBin":[]}}'

try {
    if ($env:OS -ne 'Windows_NT') {
        cargo test --locked --manifest-path apps/desktop/src-tauri/Cargo.toml --lib
        $exitCode = $LASTEXITCODE
    }
    else {
        $vsWherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (-not (Test-Path -LiteralPath $vsWherePath)) {
            throw 'Visual Studio Installer vswhere.exe was not found. Install Visual Studio Build Tools with Desktop development with C++.'
        }

        $visualStudioPath = & $vsWherePath `
            -latest `
            -products Microsoft.VisualStudio.Product.BuildTools `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath
        if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
            $visualStudioPath = & $vsWherePath `
                -latest `
                -products '*' `
                -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                -property installationPath
        }
        if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
            throw 'Visual Studio Build Tools with the x64 C++ toolchain were not found.'
        }

        $vsDevCmdPath = Join-Path $visualStudioPath.Trim() 'Common7\Tools\VsDevCmd.bat'
        if (-not (Test-Path -LiteralPath $vsDevCmdPath)) {
            throw 'Visual Studio Build Tools did not contain Common7\Tools\VsDevCmd.bat.'
        }

        $command = "call `"$vsDevCmdPath`" -arch=x64 -host_arch=x64 >nul && cargo test --locked --manifest-path apps\desktop\src-tauri\Cargo.toml --lib"
        & $env:ComSpec /d /s /c $command
        $exitCode = $LASTEXITCODE
    }
}
finally {
    $env:TAURI_CONFIG = $previousTauriConfig
}

exit $exitCode
