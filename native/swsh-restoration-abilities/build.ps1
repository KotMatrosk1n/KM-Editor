# SPDX-License-Identifier: GPL-3.0-only
[CmdletBinding()]
param([string]$OutputRoot, [string]$LlvmBin = $env:KM_LLVM_BIN)
$ErrorActionPreference = 'Stop'
$runtimeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sharedRoot = Join-Path $runtimeRoot '../km-native-settings'
if (!$OutputRoot) { $OutputRoot = Join-Path $runtimeRoot 'out' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$objectRoot = Join-Path $OutputRoot 'obj'
New-Item -ItemType Directory -Force -Path $objectRoot | Out-Null
function Resolve-LlvmTool([string]$Name) {
    if ($LlvmBin) {
        $candidate = Join-Path $LlvmBin "$Name.exe"
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return (Get-Command $Name -ErrorAction Stop).Source
}
$clang = Resolve-LlvmTool 'clang'
$clangxx = Resolve-LlvmTool 'clang++'
$linker = Resolve-LlvmTool 'ld.lld'
$common = @('--target=aarch64-none-elf', '-march=armv8-a', '-ffreestanding',
    '-fPIC', '-fvisibility=hidden', '-ffunction-sections', '-fdata-sections',
    '-fno-stack-protector', '-fno-unwind-tables', '-fno-asynchronous-unwind-tables',
    '-Wall', '-Wextra', '-Werror', '-I', (Join-Path $runtimeRoot 'include'),
    '-I', (Join-Path $sharedRoot 'include'))
$cpp = $common + @('-std=c++20', '-fno-exceptions', '-fno-rtti',
    '-fno-threadsafe-statics', '-O2')
$sources = @((Join-Path $sharedRoot 'src/crt0.S'),
    (Join-Path $sharedRoot 'src/svc.S'), (Join-Path $sharedRoot 'src/runtime_base.cpp'))
$sources += Get-ChildItem -LiteralPath (Join-Path $runtimeRoot 'src') -File |
    Sort-Object Name | Select-Object -ExpandProperty FullName
$objects = @()
foreach ($source in $sources) {
    $object = Join-Path $objectRoot ([IO.Path]::GetFileNameWithoutExtension($source) + '.o')
    if ([IO.Path]::GetExtension($source) -eq '.cpp') {
        & $clangxx @cpp -c $source -o $object
    } else { & $clang @common -c $source -o $object }
    if ($LASTEXITCODE -ne 0) { throw "Restoration runtime compilation failed: $source" }
    $objects += $object
}
$elf = Join-Path $OutputRoot 'swsh-restoration-abilities.elf'
$nso = Join-Path $OutputRoot 'swsh-restoration-abilities.nso'
& $linker '-shared' '--init=km_runtime_initialize' '--build-id=sha1' '--gc-sections' `
    '--no-undefined' '-T' (Join-Path $sharedRoot 'linker.ld') @objects '-o' $elf
if ($LASTEXITCODE -ne 0) { throw 'Restoration runtime link failed.' }
$packer = Join-Path $sharedRoot 'tools/KM.NativeSettings.Pack/KM.NativeSettings.Pack.csproj'
& dotnet run --project $packer --configuration Release -- $elf $nso
if ($LASTEXITCODE -ne 0) { throw 'Restoration runtime packing failed.' }
& dotnet run --project $packer --configuration Release -- --verify $elf $nso
if ($LASTEXITCODE -ne 0) { throw 'Restoration runtime final-file verification failed.' }
Write-Output $nso
