# SPDX-License-Identifier: GPL-3.0-only

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $CargoTargetDirectory,

    [Parameter(Mandatory = $true)]
    [string] $SidecarPath,

    [Parameter(Mandatory = $true)]
    [string] $Destination
)

$ErrorActionPreference = 'Stop'

function Resolve-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        throw "$Description was not found. Build the explicit input before staging setup payloads."
    }

    $item = Get-Item -LiteralPath $resolved.Path
    if (-not $item.PSIsContainer -and $item.Length -gt 0) {
        return $item.FullName
    }

    throw "$Description must be a nonempty file."
}

function Assert-X64PortableExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read
    )
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) {
            throw "$Description is not a Windows PE file."
        }

        $stream.Position = 0x3c
        $peOffset = $reader.ReadUInt32()
        if ($peOffset -gt $stream.Length - 6) {
            throw "$Description has an invalid PE header offset."
        }

        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw "$Description must be an x64 Windows PE file."
        }
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Assert-KmBinaryVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Description,

        [Parameter(Mandatory = $true)]
        [int[]] $ExpectedParts
    )

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ([string]::IsNullOrWhiteSpace($versionInfo.FileVersion) -or
        [string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
        throw "$Description must carry Windows file and product version metadata. Rebuild it from synchronized release metadata."
    }

    $expected = @($ExpectedParts[0], $ExpectedParts[1], $ExpectedParts[2], 0)
    $actualFile = @(
        $versionInfo.FileMajorPart,
        $versionInfo.FileMinorPart,
        $versionInfo.FileBuildPart,
        $versionInfo.FilePrivatePart
    )
    $actualProduct = @(
        $versionInfo.ProductMajorPart,
        $versionInfo.ProductMinorPart,
        $versionInfo.ProductBuildPart,
        $versionInfo.ProductPrivatePart
    )
    $expectedText = $expected -join '.'
    $actualFileText = $actualFile -join '.'
    $actualProductText = $actualProduct -join '.'

    if ($actualFileText -ne $expectedText -or $actualProductText -ne $expectedText) {
        throw "$Description must carry file and product version $expectedText. Found file version $actualFileText and product version $actualProductText. Rebuild it from synchronized release metadata."
    }
}

if ($Version -notmatch '^(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})$') {
    throw 'Version must be a canonical numeric three-part version.'
}

$versionParts = @($Version.Split('.') | ForEach-Object { [int]$_ })
if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535) {
    throw 'Version exceeds the Windows setup limits.'
}

$resolvedCargoTarget = Resolve-Path -LiteralPath $CargoTargetDirectory -ErrorAction SilentlyContinue
if ($null -eq $resolvedCargoTarget) {
    throw 'The Cargo target directory does not exist.'
}

$cargoTargetItem = Get-Item -LiteralPath $resolvedCargoTarget.Path
if (-not $cargoTargetItem.PSIsContainer) {
    throw 'The Cargo target path must be a directory.'
}

$mainExecutable = Resolve-RequiredFile `
    -Path (Join-Path $cargoTargetItem.FullName 'release\km-editor-desktop.exe') `
    -Description 'The unbundled KM Editor executable'
$bridgeExecutable = Resolve-RequiredFile `
    -Path $SidecarPath `
    -Description 'The KM project bridge sidecar'

Assert-X64PortableExecutable -Path $mainExecutable -Description 'The unbundled KM Editor executable'
Assert-X64PortableExecutable -Path $bridgeExecutable -Description 'The KM project bridge sidecar'
Assert-KmBinaryVersion `
    -Path $mainExecutable `
    -Description 'The unbundled KM Editor executable' `
    -ExpectedParts $versionParts
Assert-KmBinaryVersion `
    -Path $bridgeExecutable `
    -Description 'The KM project bridge sidecar' `
    -ExpectedParts $versionParts

$destinationPath = [System.IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) {
    $destinationItem = Get-Item -LiteralPath $destinationPath
    if (-not $destinationItem.PSIsContainer) {
        throw 'The payload destination exists and is not a directory.'
    }

    if (Get-ChildItem -LiteralPath $destinationPath -Force | Select-Object -First 1) {
        throw 'The payload destination must be empty so stale application files cannot enter a setup package.'
    }
} else {
    New-Item -ItemType Directory -Path $destinationPath | Out-Null
}

$stagedApplication = Join-Path $destinationPath 'km-editor-desktop.exe'
$stagedBridge = Join-Path $destinationPath 'km-tools-bridge.exe'

Copy-Item -LiteralPath $mainExecutable -Destination $stagedApplication
Copy-Item -LiteralPath $bridgeExecutable -Destination $stagedBridge

& (Join-Path $PSScriptRoot 'Set-TauriBundleType.ps1') -Path $stagedApplication

$manifest = @(
    [ordered]@{
        file = 'km-editor-desktop.exe'
        sha256 = (Get-FileHash -LiteralPath $stagedApplication -Algorithm SHA256).Hash
        size = (Get-Item -LiteralPath $stagedApplication).Length
        tauriBundleType = 'nsis'
    },
    [ordered]@{
        file = 'km-tools-bridge.exe'
        sha256 = (Get-FileHash -LiteralPath $stagedBridge -Algorithm SHA256).Hash
        size = (Get-Item -LiteralPath $stagedBridge).Length
    }
)

[ordered]@{ version = $Version; files = $manifest } |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $destinationPath 'payload-manifest.json') -Encoding utf8

Write-Output 'Staged the two explicit KM Editor setup payloads and recorded their SHA-256 hashes.'
