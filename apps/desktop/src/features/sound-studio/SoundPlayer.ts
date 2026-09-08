// SPDX-License-Identifier: GPL-3.0-only
export type SoundInfo = { channels: number; rate: number; frames: number; loopStart: number; loopEnd: number };
type Pending = { resolve: (value: Float32Array[]) => void; reject: () => void; timer: ReturnType<typeof setTimeout> };

/** Decoding runs in the worker; the audio device consumes at most four seconds of scheduled PCM. */
export class SoundPlayer {
  readonly worker = new Worker(new URL('/sound-decoder-worker.js', window.location.href));
  info: SoundInfo | null = null;
  context: AudioContext | null = null;
  gain: GainNode | null = null;
  spectrum: AnalyserNode | null = null;
  meters: AnalyserNode[] = [];
  playing = false;
  repeat = false;
  position = 0;
  onReady = (_info: SoundInfo) => {};
  onChange = () => {};
  onError = () => {};
  private disposed = false;
  private sequence = 0;
  private generation = 0;
  private pending = new Map<number, Pending>();
  private nodes: { node: AudioBufferSourceNode; start: number; time: number; frames: number }[] = [];
  private timer: ReturnType<typeof setInterval> | null = null;
  private filling = false;
  private nextFrame = 0;
  private nextTime = 0;
  private volume = 0.5;
  private muted = false;
  private openTimeout: ReturnType<typeof setTimeout>;
  constructor(size: number, wave = false) {
    this.openTimeout = setTimeout(() => this.fail(), 30000);
    this.worker.onmessage = ({ data }) => {
      if (this.disposed) return;
      if (data.type === 'ready') { clearTimeout(this.openTimeout); this.info = data.info as SoundInfo; this.onReady(this.info); }
      if (data.type === 'pcm') { const request = this.pending.get(data.id); this.pending.delete(data.id); if (request) { clearTimeout(request.timer); request.resolve(data.output); } }
      if (data.type === 'error') this.fail();
    };
    this.worker.onerror = () => this.fail(); this.worker.postMessage({ type: 'init', size, wave });
  }
  private fail() { if (this.disposed) return; this.dispose(); this.onError(); }
  private render(start: number, count: number) {
    return new Promise<Float32Array[]>((resolve, reject) => { const id = ++this.sequence; const timer = setTimeout(() => this.fail(), 10000); this.pending.set(id, { resolve, reject: () => reject(new Error('Audio decode failed')), timer }); this.worker.postMessage({ type: 'render', id, start, count }); });
  }
  setVolume(volume: number, muted: boolean) { this.volume = Number.isFinite(volume) ? Math.max(0, Math.min(1, volume)) : 0.5; this.muted = muted; if (this.gain) this.gain.gain.value = muted ? 0 : this.volume; }
  async play() {
    if (!this.info || this.disposed || this.playing) return;
    const generation = ++this.generation;
    if (!this.context) {
      this.context = new AudioContext(); this.gain = this.context.createGain();
      this.gain.gain.value = this.muted ? 0 : this.volume;
      // Playback remains connected even if optional analysis cannot be created.
      this.gain.connect(this.context.destination);
      try {
        this.spectrum = this.context.createAnalyser(); this.spectrum.fftSize = 2048; this.spectrum.smoothingTimeConstant = 0.65; this.gain.connect(this.spectrum);
        const splitter = this.context.createChannelSplitter(Math.min(this.info.channels, 2)); this.gain.connect(splitter);
        this.meters = Array.from({ length: Math.min(this.info.channels, 2) }, (_, i) => { const analyser = this.context!.createAnalyser(); analyser.fftSize = 2048; splitter.connect(analyser, i); return analyser; });
      } catch { this.spectrum = null; this.meters = []; }
    }
    await this.context.resume(); if (this.disposed || generation !== this.generation) return;
    if (this.position >= this.info.frames) this.position = 0;
    this.nextFrame = this.position; this.nextTime = this.context.currentTime + 0.12; this.playing = true;
    this.timer = setInterval(() => { this.tick(); }, 60); this.tick(); this.onChange();
  }
  getPosition() {
    if (!this.context || !this.info || !this.playing) return this.position;
    const now = this.context.currentTime;
    const active = this.nodes.find(item => now >= item.time && now < item.time + item.frames / this.info!.rate);
    if (active) this.position = Math.min(this.info.frames, active.start + Math.floor((now - active.time) * this.info.rate));
    return this.position;
  }
  pause() {
    this.getPosition(); this.playing = false; this.generation++;
    if (this.timer) clearInterval(this.timer); this.timer = null;
    for (const item of this.nodes) { item.node.onended = null; item.node.stop(); item.node.disconnect(); } this.nodes = [];
    this.onChange();
  }
  stop() { this.pause(); this.position = 0; this.onChange(); }
  async seek(frame: number) { const playing = this.playing; this.pause(); this.position = Math.max(0, Math.min(this.info?.frames ?? 0, Math.round(frame))); if (playing) await this.play(); this.onChange(); }
  async setRepeat(value: boolean) { this.repeat = value; if (this.playing) await this.seek(this.getPosition()); }
  private tick() {
    if (!this.playing || !this.info || !this.context) return;
    this.getPosition(); this.nodes = this.nodes.filter(item => { if (item.time + item.frames / this.info!.rate > this.context!.currentTime) return true; item.node.disconnect(); return false; });
    if (!this.repeat && this.nextFrame >= this.info.frames && this.nodes.length === 0 && !this.filling) {
      this.pause(); this.position = this.info.frames; this.onChange(); return;
    }
    if (!this.filling) void this.fill();
  }
  private async fill() {
    const generation = this.generation; this.filling = true;
    try {
      while (this.playing && this.info && this.context && generation === this.generation && this.nextTime - this.context.currentTime < 3) {
        const info = this.info;
        const end = this.repeat && info.loopEnd > info.loopStart ? info.loopEnd : info.frames;
        if (this.nextFrame >= end) { if (!this.repeat) break; this.nextFrame = info.loopEnd > info.loopStart ? info.loopStart : 0; }
        const start = this.nextFrame; const count = Math.min(16384, Math.ceil(info.rate / 4), end - start);
        const output = await this.render(start, count);
        if (this.disposed || generation !== this.generation || !this.playing) break;
        if (!output[0]?.length) throw new Error('No audio frames');
        const buffer = this.context.createBuffer(output.length, output[0].length, info.rate);
        output.forEach((channel, index) => buffer.copyToChannel(new Float32Array(channel), index));
        const node = this.context.createBufferSource(); node.buffer = buffer; node.connect(this.gain!);
        const time = Math.max(this.nextTime, this.context.currentTime + 0.015);
        node.start(time); this.nodes.push({ node, time, start, frames: output[0].length });
        this.nextTime = time + output[0].length / info.rate; this.nextFrame = start + output[0].length;
      }
    } catch { if (!this.disposed && generation === this.generation) this.fail(); }
    finally { this.filling = false; }
  }
  dispose() {
    if (this.disposed) return;
    this.pause(); this.disposed = true; this.worker.terminate();
    clearTimeout(this.openTimeout);
    for (const request of this.pending.values()) { clearTimeout(request.timer); request.reject(); } this.pending.clear();
    this.gain?.disconnect(); this.spectrum?.disconnect(); this.meters.forEach(meter => meter.disconnect());
    if (this.context) void this.context.close().catch(() => {});
    this.context = null; this.gain = null; this.spectrum = null; this.meters = [];
  }
}
