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
    [string] $WebView2BootstrapperPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [switch] $AcceptWixEula,

    [switch] $KeepIntermediates,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $MsBuildPath
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
        throw "$Description was not found."
    }

    $item = Get-Item -LiteralPath $resolved.Path
    if ($item.PSIsContainer -or $item.Length -eq 0) {
        throw "$Description must be a nonempty file."
    }

    return $item.FullName
}

function Resolve-RequiredDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        throw "$Description was not found."
    }

    $item = Get-Item -LiteralPath $resolved.Path
    if (-not $item.PSIsContainer) {
        throw "$Description must be a directory."
    }

    return $item.FullName
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    Write-Host "==> $Description"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Resolve-MsBuild {
    param([string] $ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return Resolve-RequiredFile -Path $ExplicitPath -Description 'The requested MSBuild executable'
    }

    $command = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $vsWhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vsWhere -PathType Leaf)) {
        throw 'MSBuild was not found. Install Visual Studio 2022 Build Tools with the C++ toolchain.'
    }

    $candidates = @(
        & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe'
    )
    if ($LASTEXITCODE -ne 0 -or $candidates.Count -eq 0) {
        $candidates = @(
            & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe'
        )
    }
    if ($LASTEXITCODE -ne 0 -or $candidates.Count -eq 0) {
        throw 'Visual Studio 2022 Build Tools is installed, but MSBuild could not be located.'
    }

    return Resolve-RequiredFile -Path $candidates[0] -Description 'The Visual Studio MSBuild executable'
}

if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$') {
    throw 'Version must contain exactly three numeric components, for example 2.4.0.'
}

$versionParts = @($Version.Split('.') | ForEach-Object { [int]$_ })
if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535) {
    throw 'MSI version limits are major 0..255, minor 0..255, and build 0..65535.'
}

if (-not $AcceptWixEula) {
    throw 'WiX v7 requires explicit OSMF EULA acceptance. Review the WiX v7 terms, then invoke this driver with -AcceptWixEula to record that build-time acceptance gesture.'
}

$scriptsRoot = $PSScriptRoot
$windowsRoot = Split-Path -Parent $scriptsRoot
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $windowsRoot '..\..')).Path
$cargoTarget = Resolve-RequiredDirectory -Path $CargoTargetDirectory -Description 'The Cargo target directory'
$sidecar = Resolve-RequiredFile -Path $SidecarPath -Description 'The KM Tools sidecar'
$webView2 = Resolve-RequiredFile -Path $WebView2BootstrapperPath -Description 'The WebView2 Evergreen Bootstrapper'
$msBuild = Resolve-MsBuild -ExplicitPath $MsBuildPath

$webViewSignature = Get-AuthenticodeSignature -LiteralPath $webView2
if ($webViewSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    $null -eq $webViewSignature.SignerCertificate -or
    $webViewSignature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
    throw 'The supplied WebView2 bootstrapper must have a valid Microsoft Corporation Authenticode signature.'
}
$webViewHash = (Get-FileHash -LiteralPath $webView2 -Algorithm SHA256).Hash.ToLowerInvariant()

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    $outputItem = Get-Item -LiteralPath $outputPath
    if (-not $outputItem.PSIsContainer) {
        throw 'OutputDirectory exists and is not a directory.'
    }
} else {
    New-Item -ItemType Directory -Path $outputPath | Out-Null
}

$artifactName = "KM.Editor.Setup_${Version}_x64.exe"
$finalArtifact = Join-Path $outputPath $artifactName
if (Test-Path -LiteralPath $finalArtifact) {
    throw "The output artifact already exists: $finalArtifact"
}

$buildIdentifier = [Guid]::NewGuid().ToString('N')
$workingRoot = Join-Path $windowsRoot "obj\setup-build\$buildIdentifier"
$payloadRoot = Join-Path $workingRoot 'payload\app'
$uiOutput = Join-Path $workingRoot 'ui'
$packageOutput = Join-Path $workingRoot 'package'
$packageIntermediate = Join-Path $workingRoot 'package-obj'
$bundleOutput = Join-Path $workingRoot 'bundle'
$bundleIntermediate = Join-Path $workingRoot 'bundle-obj'
$launcherOutput = Join-Path $workingRoot 'launcher'
$launcherIntermediate = Join-Path $workingRoot 'launcher-obj'

New-Item -ItemType Directory -Path $workingRoot | Out-Null

try {
& (Join-Path $scriptsRoot 'Stage-KmPayload.ps1') `
    -CargoTargetDirectory $cargoTarget `
    -SidecarPath $sidecar `
    -Destination $payloadRoot

$uiProject = Join-Path $windowsRoot 'KM.Setup.UI\KM.Setup.UI.csproj'
Invoke-Checked `
    -FilePath 'dotnet' `
    -Description 'Publishing the self-contained KM setup UI' `
    -ArgumentList (@(
        'publish', $uiProject,
        '--nologo',
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'true',
        '--output', $uiOutput,
        '-p:AcceptEula=wix7'
    ))

$packageProject = Join-Path $windowsRoot 'KM.Setup.Package\KM.Setup.Package.wixproj'
$packageArguments = @(
    'build', $packageProject,
    '--nologo',
    '-c', $Configuration,
    '-p:AcceptEula=wix7',
    "-p:KmVersion=$Version",
    "-p:KmPayloadDir=$payloadRoot",
    "-p:OutputPath=$packageOutput\",
    "-p:BaseIntermediateOutputPath=$packageIntermediate\"
)
Invoke-Checked `
    -FilePath 'dotnet' `
    -Description 'Building the KM Editor MSI payload' `
    -ArgumentList $packageArguments

$msiPath = Resolve-RequiredFile `
    -Path (Join-Path $packageOutput 'KM.Editor.msi') `
    -Description 'The built KM Editor MSI'

$bundleProject = Join-Path $windowsRoot 'KM.Setup.Bundle\KM.Setup.Bundle.wixproj'
$bundleArguments = @(
    'build', $bundleProject,
    '--nologo',
    '-c', $Configuration,
    '-p:AcceptEula=wix7',
    '-p:BuildProjectReferences=false',
    '-p:RestoreRecursive=false',
    "-p:KmVersion=$Version",
    "-p:KmMsiPath=$msiPath",
    "-p:KmBootstrapperApplicationSourceDir=$uiOutput",
    "-p:WebView2BootstrapperPath=$webView2",
    "-p:WebView2BootstrapperHash=$webViewHash",
    "-p:OutputPath=$bundleOutput\",
    "-p:BaseIntermediateOutputPath=$bundleIntermediate\"
)
Invoke-Checked `
    -FilePath 'dotnet' `
    -Description 'Building the KM Editor Burn bundle' `
    -ArgumentList $bundleArguments

$bundlePath = Resolve-RequiredFile `
    -Path (Join-Path $bundleOutput 'KM.Editor.Setup.exe') `
    -Description 'The built KM Editor Burn bundle'

$launcherProject = Join-Path $windowsRoot 'KM.Setup.Launcher\KM.Setup.Launcher.vcxproj'
Invoke-Checked `
    -FilePath $msBuild `
    -Description 'Building the hash-pinned KM Editor setup launcher' `
    -ArgumentList @(
        $launcherProject,
        '/nologo',
        '/m',
        "/p:Configuration=$Configuration",
        '/p:Platform=x64',
        "/p:KmInnerBundlePath=$bundlePath",
        "/p:KmVersion=$Version",
        "/p:OutDir=$launcherOutput\",
        "/p:IntDir=$launcherIntermediate\"
    )

$launcherPath = Resolve-RequiredFile `
    -Path (Join-Path $launcherOutput 'KM Editor Setup.exe') `
    -Description 'The built KM Editor setup launcher'

Copy-Item -LiteralPath $launcherPath -Destination $finalArtifact
$finalHash = (Get-FileHash -LiteralPath $finalArtifact -Algorithm SHA256).Hash.ToLowerInvariant()
$receipt = [ordered]@{
    version = $Version
    file = $artifactName
    sha256 = $finalHash
    webView2BootstrapperSha256 = $webViewHash
    authenticodeRequired = $false
    legacyNsisMigrationEnabled = $true
    architecture = 'x64'
}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$finalArtifact.build.json" -Encoding utf8

Write-Output $finalArtifact
} finally {
    if (-not $KeepIntermediates -and (Test-Path -LiteralPath $workingRoot)) {
        $workingParent = [IO.Path]::GetFullPath((Join-Path $windowsRoot 'obj\setup-build'))
        $resolvedWorkingRoot = [IO.Path]::GetFullPath($workingRoot)
        $workingPrefix = $workingParent.TrimEnd('\') + '\'
        if (-not $resolvedWorkingRoot.StartsWith($workingPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path -Leaf $resolvedWorkingRoot) -ne $buildIdentifier) {
            throw 'Refusing to clean an installer working directory outside the exact GUID-scoped setup-build root.'
        }

        Remove-Item -LiteralPath $resolvedWorkingRoot -Recurse -Force
    }
}
