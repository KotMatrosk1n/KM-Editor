/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useState } from 'react';
import { z } from 'zod';
import { useLocalization } from '../../localization';

export type ModelLight = [number, number, number, number];
export const defaultModelLight: ModelLight = [-34, 48, 4, 1];
const storageKey = 'km-editor.model-viewer.light';
const schema = z.tuple([z.number().min(-180).max(180), z.number().min(-90).max(90), z.number().min(2).max(10), z.number().min(0).max(4)]);
export function useModelLight() {
  const [light, setLight] = useState<ModelLight>(() => {
    try { return schema.parse(JSON.parse(localStorage.getItem(storageKey) ?? 'null')); }
    catch { return [...defaultModelLight]; }
  });
  useEffect(() => { try { localStorage.setItem(storageKey, JSON.stringify(light)); } catch { /* Session controls remain available. */ } }, [light]);
  return [light, setLight] as const;
}
export function ModelLightControls({ light, onChange }: { light: ModelLight; onChange: (light: ModelLight) => void }) {
  const { t } = useLocalization();
  const change = (index: number, value: number) => { const next: ModelLight = [...light]; next[index] = value; onChange(next); };
  return <details className="model-viewer__light" open>
    <summary>{t('modelViewer.light')}</summary>
    <p>{t('modelViewer.lightHelp')}</p>
    <div className="model-viewer__light-position" role="slider" tabIndex={0} aria-label={t('modelViewer.moveLight')}
      aria-valuemin={-180} aria-valuemax={180} aria-valuenow={light[0]} aria-valuetext={`${light[0]}°, ${light[1]}°`}
      onPointerDown={event => {
        event.currentTarget.setPointerCapture(event.pointerId); event.currentTarget.focus();
        const rect = event.currentTarget.getBoundingClientRect();
        onChange([Math.round(Math.max(-180, Math.min(180, (event.clientX - rect.left) / rect.width * 360 - 180))),
          Math.round(Math.max(-90, Math.min(90, 90 - (event.clientY - rect.top) / rect.height * 180))), light[2], light[3]]);
      }}
      onPointerMove={event => {
        if (!event.currentTarget.hasPointerCapture(event.pointerId)) return;
        const rect = event.currentTarget.getBoundingClientRect();
        onChange([Math.round(Math.max(-180, Math.min(180, (event.clientX - rect.left) / rect.width * 360 - 180))),
          Math.round(Math.max(-90, Math.min(90, 90 - (event.clientY - rect.top) / rect.height * 180))), light[2], light[3]]);
      }}
      onKeyDown={event => {
        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault(); const step = event.shiftKey ? 15 : 3;
        onChange([Math.max(-180, Math.min(180, light[0] + (event.key === 'ArrowRight' ? step : event.key === 'ArrowLeft' ? -step : 0))),
          Math.max(-90, Math.min(90, light[1] + (event.key === 'ArrowUp' ? step : event.key === 'ArrowDown' ? -step : 0))), light[2], light[3]]);
      }}><span style={{ left: `${(light[0] + 180) / 3.6}%`, top: `${(90 - light[1]) / 1.8}%` }} /></div>
    {(['lightHorizontal', 'lightVertical', 'lightDistance', 'lightBrightness'] as const).map((key, i) => <label key={key}>
      <span>{t(`modelViewer.${key}`)} <output>{i < 2 ? `${light[i]}°` : i === 2 ? `${light[i]}×` : `${Math.round(light[i] * 100)}%`}</output></span>
      <input type="range" aria-label={t(`modelViewer.${key}`)} min={[-180, -90, 2, 0][i]} max={[180, 90, 10, 4][i]}
        step={i < 2 ? 1 : .1} value={light[i]} onChange={event => change(i, Number(event.target.value))} />
    </label>)}
    <button type="button" onClick={() => onChange([...defaultModelLight])}>{t('modelViewer.resetLight')}</button>
  </details>;
}
