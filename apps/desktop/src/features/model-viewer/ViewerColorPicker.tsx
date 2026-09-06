/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useLocalization } from '../../localization';

function hsv(color: string): [number, number, number] {
  const [r, g, b] = [1, 3, 5].map(i => parseInt(color.slice(i, i + 2), 16) / 255);
  const max = Math.max(r, g, b), min = Math.min(r, g, b), delta = max - min;
  const hue = delta === 0 ? 0 : max === r ? ((g - b) / delta + 6) % 6 : max === g ? (b - r) / delta + 2 : (r - g) / delta + 4;
  return [hue * 60, max === 0 ? 0 : delta / max, max];
}
function hex(h: number, s: number, v: number) {
  const channel = (n: number) => {
    const k = (n + h / 60) % 6;
    return Math.round(255 * (v - v * s * Math.max(0, Math.min(k, 4 - k, 1)))).toString(16).padStart(2, '0');
  };
  return `#${channel(5)}${channel(3)}${channel(1)}`;
}
function ColorValue({ label, value, onChange, numeric = false }: { label: string; value: string; onChange: (value: string) => void; numeric?: boolean }) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);
  return <label data-localization-ignore="true">{label}<input aria-label={numeric ? `RGB ${label}` : label}
    type={numeric ? 'number' : 'text'} min={numeric ? 0 : undefined} max={numeric ? 255 : undefined}
    maxLength={numeric ? undefined : 7} value={draft} spellCheck={false}
    onChange={event => { setDraft(event.target.value); onChange(event.target.value); }} onBlur={() => setDraft(value)} /></label>;
}
export function ViewerColorPicker({ color, onChange }: { color: string; onChange: (color: string) => void }) {
  const { t } = useLocalization();
  const [open, setOpen] = useState(false);
  const [hue, setHue] = useState(() => hsv(color)[0]);
  const wrapper = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const palette = useRef<HTMLDivElement>(null);
  const [, saturation, value] = hsv(color);
  useEffect(() => { const [h, s] = hsv(color); if (s > 0) setHue(h); }, [color]);
  useLayoutEffect(() => {
    if (!open || !palette.current || !trigger.current) return;
    const panel = palette.current, button = trigger.current;
    const position = () => {
      const rect = button.getBoundingClientRect();
      panel.style.left = `${Math.max(12, Math.min(rect.left, innerWidth - panel.offsetWidth - 12))}px`;
      panel.style.top = `${Math.max(12, Math.min(rect.bottom + 8, innerHeight - panel.offsetHeight - 12))}px`;
    };
    position();
    window.addEventListener('resize', position);
    window.addEventListener('scroll', position, true);
    return () => { window.removeEventListener('resize', position); window.removeEventListener('scroll', position, true); };
  }, [open]);
  useEffect(() => {
    if (!open) return;
    palette.current?.focus({ preventScroll: true });
    const outside = (event: PointerEvent) => { if (!wrapper.current?.contains(event.target as Node)) setOpen(false); };
    document.addEventListener('pointerdown', outside);
    return () => document.removeEventListener('pointerdown', outside);
  }, [open]);
  const close = () => { setOpen(false); trigger.current?.focus(); };
  return <div className="model-viewer__color-picker" ref={wrapper} onBlur={event => {
    if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget as Node)) setOpen(false);
  }}>
    <button ref={trigger} id="model-background-color" type="button" className="model-viewer__swatch"
      aria-label={t('modelViewer.backgroundColor')} aria-haspopup="dialog" aria-expanded={open}
      onClick={() => setOpen(current => !current)}><span style={{ backgroundColor: color }} /></button>
    {open ? <div ref={palette} className="model-viewer__palette" role="dialog" tabIndex={-1}
      aria-label={t('modelViewer.backgroundColor')} onKeyDown={event => {
        if (event.key === 'Escape') { event.stopPropagation(); close(); }
      }}>
      <div className="model-viewer__color-plane" role="slider" tabIndex={0} aria-label={t('modelViewer.saturationBrightness')}
        aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(saturation * 100)}
        aria-valuetext={`${Math.round(saturation * 100)}%, ${Math.round(value * 100)}%`}
        style={{ backgroundColor: `hsl(${hue} 100% 50%)` }}
        onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); event.currentTarget.focus();
          const rect = event.currentTarget.getBoundingClientRect(); onChange(hex(hue, Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)), Math.max(0, Math.min(1, 1 - (event.clientY - rect.top) / rect.height)))); }}
        onPointerMove={event => { if (!event.currentTarget.hasPointerCapture(event.pointerId)) return;
          const rect = event.currentTarget.getBoundingClientRect(); onChange(hex(hue, Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)), Math.max(0, Math.min(1, 1 - (event.clientY - rect.top) / rect.height)))); }}
        onKeyDown={event => {
          if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
          event.preventDefault(); const step = event.shiftKey ? .1 : .01;
          onChange(hex(hue, Math.max(0, Math.min(1, saturation + (event.key === 'ArrowRight' ? step : event.key === 'ArrowLeft' ? -step : 0))),
            Math.max(0, Math.min(1, value + (event.key === 'ArrowUp' ? step : event.key === 'ArrowDown' ? -step : 0)))));
        }}><span style={{ left: `${saturation * 100}%`, top: `${(1 - value) * 100}%` }} /></div>
      <label>{t('modelViewer.hue')}<input type="range" min="0" max="359" value={hue} aria-label={t('modelViewer.hue')}
        onChange={event => { const next = Number(event.target.value); setHue(next); onChange(hex(next, saturation, value)); }} /></label>
      <div className="model-viewer__rgb">{['R', 'G', 'B'].map((channel, index) => <ColorValue key={channel} label={channel} numeric
        value={String(parseInt(color.slice(1 + index * 2, 3 + index * 2), 16))} onChange={raw => {
          if (/^\d{1,3}$/.test(raw) && Number(raw) <= 255) onChange(color.slice(0, 1 + index * 2) + Number(raw).toString(16).padStart(2, '0') + color.slice(3 + index * 2));
        }} />)}</div>
      <ColorValue label="HEX" value={color.toUpperCase()} onChange={raw => {
        if (/^#?[0-9a-f]{6}$/i.test(raw)) onChange(`#${raw.replace('#', '').toLowerCase()}`);
      }} />
    </div> : null}
  </div>;
}
