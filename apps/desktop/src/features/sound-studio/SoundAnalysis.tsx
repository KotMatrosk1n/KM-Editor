// SPDX-License-Identifier: GPL-3.0-only
import { useEffect, useRef, useState } from 'react';
import { useLocalization } from '../../localization';
import type { SoundPlayer } from './SoundPlayer';

export function SoundAnalysis({ player, enabled, muted }: { player: SoundPlayer | null; enabled: boolean; muted: boolean }) {
  const { t } = useLocalization(); const canvas = useRef<HTMLCanvasElement>(null);
  const [levels, setLevels] = useState<{ peak: number; rms: number; hold: number; clip: boolean }[]>([]);
  useEffect(() => {
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const values = new Float32Array(2048); const bins = new Float32Array(1024);
    const bars = new Float32Array(40); const trails = new Float32Array(40);
    const holds = [0, 0], clipUntil = [0, 0], holdUntil = [0, 0];
    let frame = 0, last = 0, meterTime = 0;
    const draw = (time: number) => {
      frame = requestAnimationFrame(draw); if (time - last < 33) return;
      const elapsed = Math.min(0.25, (time - last) / 1000); last = time;
      try {
        const active = player?.playing && !muted;
        const next = Array.from({ length: Math.min(player?.info?.channels ?? 2, 2) }, (_, channel) => {
          let peak = 0, square = 0;
          if (active && player?.meters[channel]) {
            player.meters[channel].getFloatTimeDomainData(values);
            for (const sample of values) { peak = Math.max(peak, Math.abs(sample)); square += sample * sample; }
          }
          if (peak >= 1) clipUntil[channel] = time + 1000;
          if (peak >= holds[channel]) { holds[channel] = peak; holdUntil[channel] = time + 700; }
          else if (time > holdUntil[channel]) holds[channel] = Math.max(peak, holds[channel] - elapsed * 0.8);
          if (muted) { holds[channel] = 0; clipUntil[channel] = 0; }
          return { peak, rms: Math.sqrt(square / values.length), hold: holds[channel], clip: time < clipUntil[channel] };
        });
        if (time - meterTime > 100) { setLevels(next); meterTime = time; }
        const target = canvas.current; const context = target?.getContext('2d'); if (!target || !context) return;
        const width = target.width, height = target.height; context.clearRect(0, 0, width, height);
        if (!enabled || reduced.matches) { bars.fill(0); trails.fill(0); return; }
        const accent = getComputedStyle(target).getPropertyValue('--color-accent-bright').trim() || '#dfbd70';
        if (active && player?.spectrum) player.spectrum.getFloatFrequencyData(bins); else bins.fill(-Infinity);
        const rate = player?.context?.sampleRate ?? 48000;
        for (let i = 0; i < bars.length; i++) {
          const low = Math.max(1, Math.floor(35 * (Math.min(20000, rate / 2) / 35) ** (i / bars.length) / (rate / 2048)));
          const high = Math.min(bins.length, Math.max(low + 1, Math.ceil(35 * (Math.min(20000, rate / 2) / 35) ** ((i + 1) / bars.length) / (rate / 2048))));
          let magnitude = -100; for (let bin = low; bin < high; bin++) magnitude = Math.max(magnitude, bins[bin]);
          const value = Math.max(0, Math.min(1, (magnitude + 80) / 80));
          bars[i] = Math.max(value, bars[i] - elapsed * 1.8); trails[i] = Math.max(bars[i], trails[i] - elapsed * 0.35);
          const x = i * width / bars.length + 2, barWidth = width / bars.length - 5;
          const gradient = context.createLinearGradient(0, height, 0, 0); gradient.addColorStop(0, '#554b30'); gradient.addColorStop(1, accent);
          context.fillStyle = gradient; context.shadowColor = accent; context.shadowBlur = 5;
          context.fillRect(x, height - bars[i] * (height - 12), barWidth, bars[i] * (height - 12));
          context.shadowBlur = 0; context.fillStyle = accent;
          if (trails[i] > 0.005) context.fillRect(x, height - trails[i] * (height - 12), barWidth, 2);
        }
      } catch { /* Optional analysis cannot interrupt the audio graph. */ }
    };
    frame = requestAnimationFrame(draw);
    return () => { cancelAnimationFrame(frame); };
  }, [player, enabled, muted]);
  const db = (value: number) => value > 0.00001 ? `${(20 * Math.log10(value)).toFixed(1)} dBFS` : t('soundStudio.silence');
  const percent = (value: number) => value <= 0 ? 0 : Math.max(0, Math.min(100, (20 * Math.log10(value) + 60) / 60 * 100));
  return <div className="sound-analysis">
    {enabled ? <div className="sound-analysis__spectrum"><canvas ref={canvas} width={960} height={220} aria-label={t('soundStudio.spectrum')} />
      <span className="sound-analysis__axis" aria-hidden="true">35 Hz <span>1 kHz</span> 20 kHz</span></div> : null}
    <div className="sound-analysis__meters" aria-label={t('soundStudio.levelMeter')}>
      {levels.map((level, i) => <div key={i} className="sound-analysis__channel">
        <span>{levels.length === 1 ? t('soundStudio.mono') : t(i === 0 ? 'soundStudio.left' : 'soundStudio.right')}</span>
        <div className="sound-analysis__track"><meter min={0} max={100} value={percent(level.peak)} aria-label={t('soundStudio.peak')} aria-valuetext={db(level.peak)} />
          <span className="sound-analysis__rms" style={{ width: `${percent(level.rms)}%` }} /><span className="sound-analysis__hold" style={{ left: `${percent(level.hold)}%` }} /></div>
        <span className={level.clip ? 'sound-analysis__clip' : ''}>{level.clip ? t('soundStudio.clipping') : db(level.peak)}</span>
        <small>{t('soundStudio.rms')} {db(level.rms)}</small>
      </div>)}
    </div>
  </div>;
}
