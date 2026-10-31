# SPDX-License-Identifier: GPL-3.0-only
[CmdletBinding()]
param([string]$LlvmBin = $env:KM_LLVM_BIN)
$ErrorActionPreference = 'Stop'
$runtimeRoot = $PSScriptRoot
$sharedRoot = Join-Path $runtimeRoot '../km-native-settings'
$outputRoot = Join-Path $runtimeRoot 'out'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
function Resolve-Tool([string]$Name) {
    $suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
    if ($LlvmBin) { return Join-Path $LlvmBin ($Name + $suffix) }
    return (Get-Command ($Name + $suffix) -ErrorAction Stop).Source
}
$cpp = Resolve-Tool 'clang++'
$assembler = Resolve-Tool 'clang'
$linker = Resolve-Tool 'ld.lld'
$common = @('--target=aarch64-none-elf', '-march=armv8-a', '-ffreestanding', '-fPIC',
    '-fvisibility=hidden', '-ffunction-sections', '-fdata-sections', '-fno-stack-protector',
    '-fno-unwind-tables', '-fno-asynchronous-unwind-tables', '-Wall', '-Wextra', '-Werror',
    '-I', (Join-Path $sharedRoot 'include'))
$cppOptions = $common + @('-std=c++20', '-fno-exceptions', '-fno-rtti', '-fno-threadsafe-statics', '-O2')
$objects = @()
foreach ($source in @((Join-Path $runtimeRoot 'runtime.cpp'), (Join-Path $sharedRoot 'src/runtime_base.cpp'),
    (Join-Path $sharedRoot 'src/svc.S'), (Join-Path $sharedRoot 'src/crt0.S'))) {
    $object = Join-Path $outputRoot (([IO.Path]::GetFileNameWithoutExtension($source)) + '.o')
    if ($source.EndsWith('.cpp')) { & $cpp @cppOptions -c $source -o $object }
    else { & $assembler @common -c $source -o $object }
    if ($LASTEXITCODE -ne 0) { throw 'Custom gift runtime compilation failed.' }
    $objects += $object
}
$elf = Join-Path $outputRoot 'km-custom-gifts.elf'
& $linker -shared --init=km_runtime_initialize --build-id=sha1 --gc-sections --no-undefined -T (Join-Path $sharedRoot 'linker.ld') @objects -o $elf
if ($LASTEXITCODE -ne 0) { throw 'Custom gift runtime linking failed.' }
$repository = (Resolve-Path (Join-Path $runtimeRoot '../..')).Path
$resource = Join-Path $repository 'src/KM.SwSh/Resources/km-custom-gifts.nso'
$packer = Join-Path $sharedRoot 'tools/KM.NativeSettings.Pack/KM.NativeSettings.Pack.csproj'
& dotnet run --project $packer --configuration Release -- $elf $resource
if ($LASTEXITCODE -ne 0) { throw 'Custom gift runtime packing failed.' }
& dotnet run --project $packer --configuration Release -- --verify $elf $resource
if ($LASTEXITCODE -ne 0) { throw 'Custom gift runtime verification failed.' }
