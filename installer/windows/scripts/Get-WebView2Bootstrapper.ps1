# SPDX-License-Identifier: GPL-3.0-only

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DestinationFile
)

$ErrorActionPreference = 'Stop'
$downloadUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
$destinationPath = [System.IO.Path]::GetFullPath($DestinationFile)
$destinationDirectory = Split-Path -Parent $destinationPath
$temporaryPath = "$destinationPath.download"

if ([string]::IsNullOrWhiteSpace($destinationDirectory)) {
    throw 'The WebView2 bootstrapper destination must include a parent directory.'
}

if (Test-Path -LiteralPath $destinationPath) {
    throw 'The WebView2 bootstrapper destination already exists.'
}

if (Test-Path -LiteralPath $temporaryPath) {
    throw 'The temporary WebView2 bootstrapper destination already exists.'
}

New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

try {
    Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $temporaryPath

    $downloadedFile = Get-Item -LiteralPath $temporaryPath
    if ($downloadedFile.PSIsContainer -or $downloadedFile.Length -eq 0) {
        throw 'The WebView2 bootstrapper download was empty.'
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $temporaryPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "The WebView2 bootstrapper Authenticode signature is not valid: $($signature.Status)."
    }

    if ($null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw 'The WebView2 bootstrapper is not signed by Microsoft Corporation.'
    }

    Move-Item -LiteralPath $temporaryPath -Destination $destinationPath
    $sha256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
    Write-Output "Verified and staged the Microsoft-signed WebView2 Evergreen Bootstrapper (SHA-256 $sha256)."
} finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
