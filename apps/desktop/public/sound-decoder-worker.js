// SPDX-License-Identifier: GPL-3.0-only
// One worker owns one selected recording. Terminating it cancels extraction/decoding and frees its heap.
let ready, inputSize = 0, cursor = 0, input, info, wave = false;
var Module = {
  noInitialRun: true,
  locateFile: file => new URL(`audio-decoder/${file}`, self.location.href).href,
  print: () => {}, printErr: () => {},
  onRuntimeInitialized: () => ready(),
  onAbort: () => postMessage({ type: 'error' })
};
const initialized = new Promise(resolve => { ready = resolve; });
importScripts('audio-decoder/decoder.js');
self.onmessage = async ({ data }) => {
  try {
    await initialized;
    if (data.type === 'init') {
      if (!Number.isSafeInteger(data.size) || data.size < 12 || data.size > 64 * 1024 * 1024) throw Error();
      input = new Uint8Array(data.size); inputSize = data.size; cursor = 0; wave = data.wave === true;
    } else if (data.type === 'input') {
      const raw = atob(data.data); if (!input || cursor + raw.length > inputSize) throw Error();
      for (let i = 0; i < raw.length; i++) input[cursor++] = raw.charCodeAt(i);
    } else if (data.type === 'open') {
      if (!input || cursor !== inputSize) throw Error();
      Module.FS.writeFile(wave ? '/selected.wav' : '/selected.wem', input, { canOwn: true }); input = null;
      if (!Module._km_audio_open(wave ? 1 : 0)) throw Error();
      info = { channels: Module._km_audio_info(0), rate: Module._km_audio_info(1), frames: Module._km_audio_info(2),
        loopStart: Module._km_audio_info(3), loopEnd: Module._km_audio_info(4) };
      if (!Number.isSafeInteger(info.loopStart) || !Number.isSafeInteger(info.loopEnd) ||
          info.loopStart < 0 || info.loopEnd <= info.loopStart || info.loopEnd > info.frames) {
        info.loopStart = 0; info.loopEnd = 0;
      }
      cursor = 0; postMessage({ type: 'ready', info });
    } else if (data.type === 'render') {
      if (!info || !Number.isSafeInteger(data.start) || data.start < 0 || data.start > info.frames || !Number.isSafeInteger(data.count) || data.count < 1 || data.count > 16384) throw Error();
      if (cursor !== data.start) Module._km_audio_seek(data.start);
      const count = Module._km_audio_render(data.count); if (count < 0) throw Error();
      cursor = data.start + count;
      const channels = Math.min(info.channels, 2);
      const output = Array.from({ length: channels }, () => new Float32Array(count));
      const pcm = Module.HEAPF32.subarray(Module._km_audio_buffer() / 4, Module._km_audio_buffer() / 4 + count * info.channels);
      // Unknown spatial layouts are folded by alternating channels, with unity total gain per side.
      // This preserves every channel for auditioning without claiming a spatial event reconstruction.
      for (let frame = 0; frame < count; frame++) for (let ch = 0; ch < info.channels; ch++) {
        const side = ch % channels; const value = pcm[frame * info.channels + ch];
        output[side][frame] += (Number.isFinite(value) ? value : 0) / Math.ceil((info.channels - side) / channels);
      }
      postMessage({ type: 'pcm', id: data.id, output, count }, output.map(channel => channel.buffer));
    }
  } catch { postMessage({ type: 'error' }); }
};
