/* SPDX-License-Identifier: GPL-3.0-only */
import { useSyncExternalStore } from 'react';
import { Box, Grid2X2, Grid3X3, Square } from 'lucide-react';
import { useLocalization } from '../../localization';

const key = 'km-editor.model-viewer.resolution';
const choices = [4, 2, 1] as const;
type Resolution = typeof choices[number];
const listeners = new Set<() => void>();
function read(): Resolution {
  try { const value = Number(localStorage.getItem(key)); return value === 4 || value === 2 ? value : 1; }
  catch { return 1; }
}
let current = read();
function notify() { for (const listener of listeners) listener(); }
function subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
window.addEventListener('storage', event => { if (event.key === key || event.key === null) { current = read(); notify(); } });
function change(value: Resolution) {
  current = value;
  try { localStorage.setItem(key, String(value)); } catch { /* The setting still applies for this session. */ }
  notify();
}
export function useModelResolution() { return useSyncExternalStore(subscribe, () => current); }
export function ModelResolutionSettings() {
  const resolution = useModelResolution();
  const { t } = useLocalization();
  return <section aria-labelledby="model-resolution-heading" className="settings-subsection">
    <div className="settings-subsection-heading">
      <Box aria-hidden="true" size={18} />
      <div><h3 id="model-resolution-heading">{t('modelResolution.title')}</h3><p>{t('modelResolution.description')}</p></div>
    </div>
    <div className="analysis-loading-options" role="radiogroup" aria-label={t('modelResolution.title')}>
      {choices.map((value, index) => {
        const Icon = { 4: Square, 2: Grid2X2, 1: Grid3X3 }[value];
        return <button key={value} type="button" role="radio" aria-checked={resolution === value}
        tabIndex={resolution === value ? 0 : -1} className={`analysis-loading-option${resolution === value ? ' is-selected' : ''}`}
        onClick={() => change(value)} onKeyDown={event => {
          const direction = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;
          if (!direction && event.key !== 'Home' && event.key !== 'End') return;
          event.preventDefault();
          const next = event.key === 'Home' ? 0 : event.key === 'End' ? 2 : (index + direction + choices.length) % choices.length;
          change(choices[next]); event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="radio"]')[next]?.focus();
        }}>
        <span><Icon aria-hidden="true" className="settings-mode-icon" size={20} /><strong>{t(`modelResolution.option.${value}`)}</strong></span>
        <p>{t(`modelResolution.detail.${value}`)}</p>
      </button>;
      })}
    </div>
  </section>;
}
