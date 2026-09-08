# SPDX-License-Identifier: GPL-3.0-only
param(
    [Parameter(Mandatory = $true)][string]$EmsdkPath,
    [Parameter(Mandatory = $true)][string]$WorkRoot
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$workspace = [IO.Path]::GetFullPath($WorkRoot)
New-Item -ItemType Directory -Force -Path $workspace | Out-Null
$emcmake = Join-Path $EmsdkPath 'upstream/emscripten/emcmake.bat'
$emcc = Join-Path $EmsdkPath 'upstream/emscripten/emcc.bat'
if (!(Test-Path -LiteralPath $emcc)) { throw 'Activate Emscripten 3.1.74 before building the decoder.' }
function Checked([scriptblock]$Action) { & $Action; if ($LASTEXITCODE -ne 0) { throw 'Audio decoder build failed.' } }
function Source([string]$Name, [string]$Url, [string]$Revision) {
    $folder = Join-Path $workspace $Name
    if (!(Test-Path -LiteralPath $folder)) {
        Checked { git clone --no-checkout $Url $folder }
        Checked { git -C $folder checkout --detach $Revision }
    }
    $actual = git -C $folder rev-parse HEAD
    if ($actual -ne $Revision -or (git -C $folder status --porcelain --untracked-files=no)) { throw "Unexpected source revision or edits in $Name." }
    return $folder
}
$stream = Source 'vgmstream' 'https://github.com/vgmstream/vgmstream.git' '71e2361042531fe767fb98300cf8c1ee95e539a0'
$opus = Source 'opus' 'https://github.com/xiph/opus.git' 'ddbe48383984d56acd9e1ab6a090c54ca6b735a6'
$build = Join-Path $workspace 'build'
$opusBuild = Join-Path $workspace 'opus-build'
$mapping = Join-Path $PSScriptRoot 'source-paths.cmake'
Checked { & $emcmake cmake -S $stream -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release -DUSE_MPEG=OFF -DUSE_FFMPEG=OFF -DUSE_G7221=OFF -DUSE_G719=OFF -DUSE_ATRAC9=OFF -DUSE_CELT=OFF -DUSE_SPEEX=OFF -DBUILD_CLI=OFF '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' "-DCMAKE_PROJECT_INCLUDE=$mapping" }
Checked { cmake --build $build --target libvgmstream --parallel 4 }
Checked { & $emcmake cmake -S $opus -B $opusBuild -G Ninja -DCMAKE_BUILD_TYPE=Release -DOPUS_BUILD_TESTING=OFF -DOPUS_BUILD_PROGRAMS=OFF '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' "-DCMAKE_PROJECT_INCLUDE=$mapping" }
Checked { cmake --build $opusBuild --target opus --parallel 4 }
$destination = Join-Path $repository 'apps/desktop/public/audio-decoder'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Checked {
    & $emcc (Join-Path $PSScriptRoot 'decoder.c') (Join-Path $PSScriptRoot 'opus_media.c') -I (Join-Path $stream 'src') -I (Join-Path $opus 'include') `
        (Join-Path $build 'src/libvgmstream.a') (Join-Path $build 'dependencies/vorbis/lib/libvorbisfile.a') `
        (Join-Path $build 'dependencies/vorbis/lib/libvorbis.a') (Join-Path $build 'dependencies/ogg/libogg.a') (Join-Path $opusBuild 'libopus.a') `
        -O3 --no-entry '-sEXPORTED_RUNTIME_METHODS=["FS"]' '-sALLOW_MEMORY_GROWTH=1' '-sMAXIMUM_MEMORY=134217728' `
        '-sSTACK_SIZE=1048576' '-sSTACK_OVERFLOW_CHECK=2' '-sENVIRONMENT=worker,node' '-sFILESYSTEM=1' -o (Join-Path $destination 'decoder.js')
}
$runtimePath = Join-Path $destination 'decoder.js'
$runtime = [IO.File]::ReadAllText($runtimePath)
# Keep the worker's unused virtual user directory under the audio namespace.
$runtime = $runtime.Replace(('"/home' + '/web_user"'), '"/audio"')
[IO.File]::WriteAllText($runtimePath, $runtime)
$notice = "Sound Studio audio decoder`n`nvgmstream r2117 (ISC), libogg 1.3.5, libvorbis 1.3.7 and libopus 1.5.2 (BSD 3 Clause).`nThe KM wrapper is GPL 3.0 only. Source revisions and build instructions are in native/km-audio-decoder.`n`n"
foreach ($license in @((Join-Path $stream 'COPYING'), (Join-Path $stream 'dependencies/ogg/COPYING'), (Join-Path $stream 'dependencies/vorbis/COPYING'), (Join-Path $opus 'COPYING'), (Join-Path $EmsdkPath 'upstream/emscripten/LICENSE'), (Join-Path $EmsdkPath 'upstream/emscripten/system/lib/libc/musl/COPYRIGHT'), (Join-Path $EmsdkPath 'upstream/emscripten/system/lib/compiler-rt/LICENSE.TXT'))) {
    $notice += (Get-Content -LiteralPath $license -Raw) + "`n`n"
}
$notice = [regex]::Replace($notice, '[ \t]+(?=\r?$)', '', [System.Text.RegularExpressions.RegexOptions]::Multiline).TrimEnd() + "`n"
[IO.File]::WriteAllText((Join-Path $destination 'NOTICE.txt'), $notice)
