# Audio decoder

Sound Studio uses a bundled WebAssembly decoder in a dedicated worker. It contains vgmstream r2117, libogg 1.3.5, libvorbis 1.3.7 and libopus 1.5.2. Their notices and the Emscripten, musl and compiler runtime licenses ship beside the module in `apps/desktop/public/audio-decoder/NOTICE.txt`.

The vgmstream source is pinned to `71e2361042531fe767fb98300cf8c1ee95e539a0`. Its optional external codecs are disabled except Vorbis. The separate Opus wrapper accepts the packet framing used by the supported audio banks. No game assets are bundled.

Use Emscripten 3.1.74, CMake, Ninja and Git to rebuild:

```powershell
./native/km-audio-decoder/build.ps1 -EmsdkPath <sdk-folder> -WorkRoot <build-folder>
```

The script verifies source revisions, builds the libraries and replaces only the decoder module and notices in the public asset directory. The main application build includes these assets without a network download at runtime.

The selected compressed recording is limited to 64 MiB, the decoder heap to 128 MiB and each decoded block to 16,384 frames. Playback schedules up to four seconds of PCM. Cancellation terminates the worker and releases its memory; no audio cache files are written to disk. Mono and stereo retain their channels. Recordings with more channels are folded into stereo by averaging alternating channels. This auditioning mix does not reproduce spatial audio.

Only individual recordings are played. Interactive event state, random selection, music layers, effects and game timing are not reconstructed. Bank events and unresolved references remain visible in the catalog. Source files and mod output are never changed.
