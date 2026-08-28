# SPDX-License-Identifier: GPL-3.0-only
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$LlvmBin = $env:KM_LLVM_BIN
)

$ErrorActionPreference = "Stop"
$runtimeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Resolve-LlvmTool {
    param([Parameter(Mandatory = $true)][string]$Name)

    $executableName = if ($env:OS -eq "Windows_NT") { "$Name.exe" } else { $Name }
    if ($LlvmBin) {
        $candidate = Join-Path $LlvmBin $executableName
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $command = Get-Command $executableName -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "LLVM tool '$Name' was not found. Set -LlvmBin or KM_LLVM_BIN, or add LLVM tools to PATH."
}

$clang = Resolve-LlvmTool "clang"
$clangxx = Resolve-LlvmTool "clang++"
$linker = Resolve-LlvmTool "ld.lld"

$outputRoot = Join-Path $runtimeRoot "out"
$objectRoot = Join-Path $outputRoot "obj"
New-Item -ItemType Directory -Force -Path $objectRoot | Out-Null

$common = @(
    "--target=aarch64-none-elf",
    "-march=armv8-a",
    "-ffreestanding",
    "-fPIC",
    "-fvisibility=hidden",
    "-ffunction-sections",
    "-fdata-sections",
    "-fno-stack-protector",
    "-fno-unwind-tables",
    "-fno-asynchronous-unwind-tables",
    "-Wall",
    "-Wextra",
    "-Werror",
    "-I", (Join-Path $runtimeRoot "include")
)
$cpp = $common + @(
    "-std=c++20",
    "-fno-exceptions",
    "-fno-rtti",
    "-fno-threadsafe-statics",
    "-O2"
)

$objects = @()
Get-ChildItem -LiteralPath (Join-Path $runtimeRoot "src") -File | Sort-Object Name | ForEach-Object {
    $object = Join-Path $objectRoot ($_.BaseName + ".o")
    if ($_.Extension -eq ".cpp") {
        & $clangxx @cpp -c $_.FullName -o $object
    } elseif ($_.Extension -eq ".S") {
        & $clang @common -c $_.FullName -o $object
    } else {
        return
    }
    if ($LASTEXITCODE -ne 0) { throw "Guest runtime compilation failed for $($_.Name)." }
    $objects += $object
}

$elf = Join-Path $outputRoot "km-native-settings.elf"
& $linker "-shared" "--build-id=sha1" "--gc-sections" "--no-undefined" "-T" (Join-Path $runtimeRoot "linker.ld") @objects "-o" $elf
if ($LASTEXITCODE -ne 0) { throw "Guest runtime link failed." }

$repositoryRoot = Resolve-Path (Join-Path $runtimeRoot "..\..")
$packerProject = Join-Path $runtimeRoot "tools\KM.NativeSettings.Pack\KM.NativeSettings.Pack.csproj"
$embeddedRuntime = Join-Path $repositoryRoot "src\KM.Tools\Resources\km-native-settings.nso"
& dotnet run --project $packerProject --configuration $Configuration -- $elf $embeddedRuntime
if ($LASTEXITCODE -ne 0) { throw "Guest runtime NSO packing failed." }
& dotnet run --project $packerProject --configuration $Configuration -- --verify $elf $embeddedRuntime
if ($LASTEXITCODE -ne 0) { throw "Guest runtime NSO final-file verification failed." }

Write-Output $elf
Write-Output $embeddedRuntime
