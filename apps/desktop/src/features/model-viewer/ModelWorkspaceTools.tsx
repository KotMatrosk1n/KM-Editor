/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { useLocalization } from '../../localization';
import { useModelHistory } from './ModelEditHistory';
import type { ModelViewOptions } from './useModelViewport';

export function ModelFrameInput({ label, value, min, max, onCommit }: { label: string; value: number; min: number; max: number; onCommit: (value: number) => void }) {
  const [text, setText] = useState<string | null>(null);
  const commit = () => {
    if (text !== null && text.trim() !== '') { const number = Number(text); if (Number.isInteger(number) && number >= min && number <= max) onCommit(number); }
    setText(null);
  };
  return <label>{label}<input type="number" step="1" min={min} max={max} value={text ?? String(value)}
    onChange={event => setText(event.target.value)} onBlur={commit}
    onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); commit(); } if (event.key === 'Escape') setText(null); }} /></label>;
}

export function ModelWorkspaceTools({ ready, disabled, original, onCompare, options, onOptions, camera, stats, onStats, orientation, historyOpen, onHistory }: {
  ready: boolean; disabled: boolean; original: boolean; onCompare: () => void; options: ModelViewOptions;
  onOptions: (options: ModelViewOptions) => void; camera: (action: string) => Promise<void>; stats: boolean; onStats: (value: boolean) => void;
  orientation: { yaw: number; pitch: number };
  historyOpen: boolean; onHistory: () => void;
}) {
  const { t } = useLocalization(), history = useModelHistory();
  const [projection, setProjection] = useState(false), [modesOpen, setModesOpen] = useState(false);
  const modeButton = useRef<HTMLButtonElement>(null);
  const modeList = useRef<HTMLDivElement>(null);
  useEffect(() => { if (modesOpen) modeList.current?.querySelector<HTMLElement>('[aria-selected="true"]')?.focus(); }, [modesOpen]);
  const modes = ['studio', 'baseColor', 'normals', 'roughness', 'metallic', 'occlusion', 'masks'];
  return <><div className="model-workspace__bar" aria-label={t('modelWorkspace.toolbar')}>
    <div className="model-workspace__tool-group">
      <div className="model-workspace__orientation" aria-label={t('modelWorkspace.cameraView')}>
        {([['sideRight','X',1,0,0],['sideLeft','X',-1,0,0],['top','Y',0,1,0],['bottom','Y',0,-1,0],['front','Z',0,0,1],['back','Z',0,0,-1]] as const).map(([action,axis,x,y,z]) => {
          const cx = Math.cos(orientation.yaw), sx = Math.sin(orientation.yaw), cy = Math.cos(orientation.pitch), sy = Math.sin(orientation.pitch);
          return <span key={action} style={{ left: `${50+(x*cx-z*sx)*35}%`, top: `${50-(-x*sx*sy+y*cy-z*cx*sy)*35}%`, zIndex: Math.round(10+x*sx*cy+y*sy+z*cx*cy) }}>
            <button type="button" title={t(`modelWorkspace.${action}`)} aria-label={t(`modelWorkspace.${action}`)} disabled={!ready} data-axis={axis}
              onClick={() => { void camera(action); }}>{x+y+z > 0 ? axis : '-'}</button></span>;
        })}
      </div>
      <button type="button" title="Ctrl+Z" disabled={disabled || !history?.canUndo || original} onClick={() => history?.undo()}>{t('modelViewer.texture.undo')}</button>
      <button type="button" title="Ctrl+Shift+Z" disabled={disabled || !history?.canRedo || original} onClick={() => history?.redo()}>{t('modelWorkspace.redo')}</button>
      <button type="button" aria-pressed={historyOpen} onClick={onHistory}>{t('modelWorkspace.history')}</button>
      <button type="button" disabled={!ready || disabled} aria-pressed={original} onClick={onCompare}>{t(original ? 'modelWorkspace.showEdited' : 'modelWorkspace.showOriginal')}</button>
    </div>
    <div className="model-workspace__tool-group">
      <button ref={modeButton} type="button" disabled={!ready} aria-haspopup="listbox" aria-expanded={modesOpen} aria-controls="model-display-modes" onClick={() => setModesOpen(!modesOpen)} title={t('modelWorkspace.displayMode')}>{t(`modelWorkspace.${modes[options.display]}`)}</button>
      <label><input type="checkbox" checked={options.wireframe} disabled={!ready} onChange={event => onOptions({ ...options, wireframe: event.target.checked })} />{t('modelWorkspace.wireframe')}</label>
    </div>
    <div className="model-workspace__tool-group">
      <button type="button" disabled={!ready} aria-pressed={projection} onClick={() => { setProjection(!projection); void camera(projection ? 'perspective' : 'orthographic'); }}>{t('modelWorkspace.orthographic')}</button>
      <button type="button" disabled={!ready} onClick={() => void camera('frame')}>{t('modelViewer.frame')}</button>
      <button type="button" disabled={!ready} onClick={() => void camera('reset')}>{t('modelViewer.resetCamera')}</button>
      <button type="button" aria-pressed={stats} onClick={() => onStats(!stats)}>{t('modelWorkspace.statistics')}</button>
    </div>
  </div>{modesOpen ? <div ref={modeList} id="model-display-modes" className="model-workspace__modes" role="listbox" aria-label={t('modelWorkspace.displayMode')}
    onKeyDown={event => {
      if (event.key === 'Escape') { event.stopPropagation(); setModesOpen(false); modeButton.current?.focus(); }
      if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(event.key)) {
        event.preventDefault(); const index = event.key === 'Home' ? 0 : event.key === 'End' ? modes.length-1 : (options.display + (['ArrowLeft','ArrowUp'].includes(event.key) ? modes.length-1 : 1)) % modes.length;
        onOptions({ ...options, display: index }); modeList.current?.querySelectorAll<HTMLElement>('[role="option"]')[index]?.focus();
      }
    }}>
    {modes.map((mode, index) => <button type="button" role="option" key={mode} tabIndex={options.display === index ? 0 : -1} aria-selected={options.display === index} onClick={() => { onOptions({ ...options, display: index }); setModesOpen(false); modeButton.current?.focus(); }}>{t(`modelWorkspace.${mode}`)}</button>)}
  </div> : null}</>;
}
export function ModelHistoryPanel() {
  const { t } = useLocalization(), history = useModelHistory();
  return <section className="model-workspace__history"><h3>{t('modelWorkspace.history')}</h3><p>{t('modelWorkspace.historyHelp')}</p>
    {history?.entries.length ? <ol>{history.entries.map((label, i) => <li key={i} data-localization-ignore="true">{label}</li>)}</ol> : <p>{t('modelWorkspace.historyEmpty')}</p>}
  </section>;
}
export function ModelParts({ parts, options, onOptions, onMaterial }: {
  parts: { id: number; name: string; material: string; triangles: number }[]; options: ModelViewOptions;
  onOptions: (options: ModelViewOptions) => void; onMaterial: () => void;
}) {
  const { t } = useLocalization(), [search, setSearch] = useState('');
  const selected = parts.find(p => p.id === options.selected);
  return <section className="model-parts" aria-label={t('modelWorkspace.parts')}>
    <label htmlFor="model-part-search">{t('modelWorkspace.searchParts')}</label>
    <input id="model-part-search" type="search" value={search} onChange={event => setSearch(event.target.value)} />
    <div className="model-workspace__bar">
      <button type="button" disabled={!selected} onClick={() => onOptions({ ...options, hidden: parts.filter(p => p.id !== selected?.id).map(p => p.id) })}>{t('modelWorkspace.isolate')}</button>
      <button type="button" disabled={!options.hidden.length} onClick={() => onOptions({ ...options, hidden: [] })}>{t('modelWorkspace.showAll')}</button>
      <button type="button" disabled={!selected} onClick={onMaterial}>{t('modelWorkspace.editMaterial')}</button>
    </div>
    <p>{t('modelWorkspace.partsHelp')}</p>
    <ul>{parts.filter(p => `${p.name} ${p.material}`.toLowerCase().includes(search.toLowerCase())).map(part => <li key={part.id}>
      <input type="checkbox" checked={!options.hidden.includes(part.id)} aria-label={t('modelWorkspace.partVisible', { name: part.name })}
        onChange={event => onOptions({ ...options, hidden: event.target.checked ? options.hidden.filter(id => id !== part.id) : [...options.hidden, part.id] })} />
      <button type="button" aria-pressed={part.id === options.selected} onClick={() => onOptions({ ...options, selected: part.id })} data-localization-ignore="true">
        <strong>{part.name}</strong><small>{part.material}</small>
      </button>
    </li>)}</ul>
  </section>;
}
