# SPDX-License-Identifier: GPL-3.0-only

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Setup,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [string[]] $BaseInstaller,
    [string] $ZstdPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = 'KotMatrosk1n/KM-Editor'
$releaseDirectory = Split-Path -Parent $Setup
$targetSize = (Get-Item -LiteralPath $Setup).Length
$targetHash = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
$deltas = [ordered]@{}
if ($targetSize -gt 512MB) { return $deltas }
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null

if (-not $ZstdPath) {
    $archive = Join-Path $WorkDirectory 'zstd-v1.5.7-win64.zip'
    Invoke-WebRequest -Uri "https://github.com/facebook/zstd/releases/download/v1.5.7/zstd-v1.5.7-win64.zip" -OutFile $archive
    $expectedHash = 'acb4e8111511749dc7a3ebedca9b04190e37a17afeb73f55d4425dbf0b90fad9'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'The Zstandard binary archive failed its pinned SHA-256 check.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $WorkDirectory -Force
    $ZstdPath = (Get-ChildItem -LiteralPath $WorkDirectory -Filter zstd.exe -File -Recurse | Select-Object -First 1).FullName
}

if (-not $BaseInstaller) {
    $releaseList = gh release list --repo $repository --exclude-drafts --exclude-pre-releases --limit 15 --json tagName
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Could not list earlier releases. Publishing the full setup without deltas.'
        return $deltas
    }
    $tags = $releaseList | ConvertFrom-Json | ForEach-Object { $_.tagName } |
        Where-Object { $_ -match '^v\d+\.\d+\.\d+$' -and [version]$_.Substring(1) -lt [version]$Version } |
        Sort-Object { [version]$_.Substring(1) } -Descending |
        Select-Object -First 3
    $BaseInstaller = foreach ($tag in $tags) {
        $metadata = gh release download $tag --repo $repository --pattern latest.json --output -
        if ($LASTEXITCODE -ne 0) { continue }
        try {
            $previous = $metadata | ConvertFrom-Json -AsHashtable
        } catch {
            Write-Warning "Could not read updater metadata for $tag."
            continue
        }
        if (-not $previous -or -not $previous.Contains('setup_deltas')) { continue }
        $name = "KM.Editor.Setup_$($tag.Substring(1))_x64.exe"
        $localBase = Join-Path $WorkDirectory $name
        gh release download $tag --repo $repository --pattern $name --dir $WorkDirectory --clobber | Out-Host
        if ($LASTEXITCODE -eq 0) {
            $localBase
        } else {
            Write-Warning "No compatible full setup found for $tag."
        }
    }
}

foreach ($base in $BaseInstaller) {
    $baseFile = Get-Item -LiteralPath $base
    if ($baseFile.Name -notmatch '^KM\.Editor\.Setup_(\d+\.\d+\.\d+)_x64\.exe$') {
        throw "Unexpected base setup name $($baseFile.Name)."
    }
    $baseVersion = $Matches[1]
    if ([version]$baseVersion -ge [version]$Version -or $baseFile.Length -gt 512MB) { continue }
    $name = "KM.Editor.Setup_$($Version)_from_$($baseVersion)_x64.zst"
    $patch = Join-Path $WorkDirectory $name
    $reconstructed = Join-Path $WorkDirectory 'reconstructed.exe'

    & $ZstdPath -5 -T1 -q -f "--patch-from=$($baseFile.FullName)" $Setup -o $patch
    if ($LASTEXITCODE -ne 0) { throw "Could not build the setup delta from $baseVersion." }
    $patchSize = (Get-Item -LiteralPath $patch).Length
    if ($patchSize -ge $targetSize * 0.85) {
        Write-Host "Skipping delta from $baseVersion because it saves less than 15%."
        continue
    }
    & $ZstdPath -d -q -f --long=29 "--patch-from=$($baseFile.FullName)" $patch -o $reconstructed
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $reconstructed -Algorithm SHA256).Hash -ne $targetHash) {
        throw "Setup delta from $baseVersion did not reproduce the full setup."
    }

    $deltas[$baseVersion] = [ordered]@{
        format = 'zstd-patch-v1'
        url = "https://github.com/$repository/releases/download/v$Version/$name"
        base_size = $baseFile.Length
        base_sha256 = (Get-FileHash -LiteralPath $baseFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        patch_size = $patchSize
        patch_sha256 = (Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash.ToLowerInvariant()
        target_size = $targetSize
        target_sha256 = $targetHash
    }
    Copy-Item -LiteralPath $patch -Destination (Join-Path $releaseDirectory $name)
    Write-Host "Verified setup delta from $baseVersion ($patchSize bytes)."
}
return $deltas
