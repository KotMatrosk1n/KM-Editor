/* SPDX-License-Identifier: GPL-3.0-only */
import { invoke, isTauri } from '@tauri-apps/api/core';
import { Box } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { z } from 'zod';
import { kmCommandNames, projectPathsSchema, type ProjectPaths } from '../../bridge/contracts';
import { sendProjectBridgeRequest } from '../../bridge/projectBridgeRequest';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';
import { modelError, useModelViewport, type ModelBackground } from './useModelViewport';
import { ViewerColorPicker } from './ViewerColorPicker';
import './ModelViewerSection.css';

const catalogSchema = z.array(z.object({
  id: z.string().max(1024), species: z.number().int(), form: z.number().int(), gender: z.number().int(), name: z.string(),
  category: z.enum(['pokemon', 'trainers', 'npcs', 'objects', 'environment', 'other']).default('pokemon'), shiny: z.boolean().default(false)
})).max(8192);
type Entry = z.infer<typeof catalogSchema>[number];
const backgroundKey = 'km-editor.model-viewer.background';
const backgroundSchema = z.object({ color: z.string().regex(/^#[0-9a-f]{6}$/i), grid: z.boolean() });
function readBackground(): ModelBackground {
  try { return backgroundSchema.parse(JSON.parse(localStorage.getItem(backgroundKey) ?? 'null')); }
  catch { return { color: '#343b44', grid: false }; }
}
export default function ModelViewerSection({ paths }: { paths: ProjectPaths }) {
  const { t } = useLocalization();
  const [catalog, setCatalog] = useState<Entry[]>([]);
  const [selected, setSelected] = useState('');
  const [animation, setAnimation] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('all');
  const [loop, setLoop] = useState(false);
  const [speed, setSpeed] = useState('1');
  const [background, setBackground] = useState(readBackground);
  useEffect(() => {
    try { localStorage.setItem(backgroundKey, JSON.stringify(background)); } catch { /* Session controls remain usable when storage is unavailable. */ }
  }, [background]);
  const pathKey = JSON.stringify(paths);
  const supported = ['sword', 'shield', 'scarlet', 'violet', 'za'].some(game => game === paths.selectedGame);
  const viewer = useModelViewport(paths, selected, animation, revision, false, background);
  const error = catalogError ?? viewer.error;
  useEffect(() => {
    setCatalog([]); setSelected(''); setAnimation(null); setCatalogError(null); setLoading(false);
    if (!supported || !isTauri()) return;
    let active = true; setLoading(true);
    void (async () => {
      try {
        const entries = await sendProjectBridgeRequest(
          requestJson => invoke<string>('project_bridge', { requestJson }),
          kmCommandNames.modelCatalog, { paths: projectPathsSchema.parse(JSON.parse(pathKey)) }, catalogSchema
        );
        if (active) setCatalog(entries);
      } catch (cause) { if (active) setCatalogError(modelError(cause)); }
      finally { if (active) setLoading(false); }
    })();
    return () => { active = false; };
  }, [pathKey, supported, revision]);
  useEffect(() => { setQuery(''); setCategory('all'); }, [pathKey]);
  useEffect(() => { setLoop(viewer.info?.looped ?? false); setSpeed('1'); }, [viewer.info]);
  const categories = useMemo(() => [...new Set(catalog.map(entry => entry.category))], [catalog]);
  const groups = useMemo(() => {
    const results = new Map<string, Entry[]>();
    const search = query.trim().toLocaleLowerCase();
    for (const entry of catalog) {
      if (category !== 'all' && entry.category !== category) continue;
      if (search && !`${entry.name} ${entry.species || ''} ${entry.id}`.toLocaleLowerCase().includes(search)) continue;
      const key = entry.species > 0 ? `${entry.category}:${entry.species}` : entry.id;
      const group = results.get(key) ?? []; group.push(entry); results.set(key, group);
    }
    return [...results.values()];
  }, [catalog, category, query]);
  const entry = catalog.find(value => value.id === selected);
  const message = error === 'KM-MODEL-GPU-UNAVAILABLE' ? 'modelViewer.gpuError' : error === 'KM-MODEL-BUSY' ? 'modelViewer.busyError' : 'modelViewer.loadError';
  usePublishCommonEditorDiagnostics(error ? [{ code: error, domain: 'workflow.modelViewer', message: t(message), severity: 'error' }] :
    (viewer.info?.warnings ?? []).map(warning => ({ code: 'KM-MODEL-PARTIAL', domain: 'workflow.modelViewer', message: t(`modelViewer.warning.${warning}`), severity: 'warning' })));
  const select = (id: string) => { setSelected(id); setAnimation(null); };
  return <section className="panel wide-panel model-viewer" aria-labelledby="model-viewer-title">
    <header className="model-viewer__header"><Box aria-hidden="true" size={22} />
      <div><h2 id="model-viewer-title">{t('modelViewer.title')}</h2><p>{t('modelViewer.description')}</p></div>
    </header>
    <p className="model-viewer__note">{t('modelViewer.scope')}</p>
    {!supported ? <p role="status">{t('modelViewer.unsupportedGame')}</p> : !isTauri() ? <p role="status">{t('modelViewer.desktopRequired')}</p> :
      <div className="model-viewer__workspace">
        <aside className="model-viewer__browser" aria-label={t('modelViewer.models', { count: catalog.length })}>
          <div className="model-viewer__toolbar"><strong>{t('modelViewer.models', { count: catalog.length })}</strong>
            <button type="button" onClick={() => setRevision(value => value + 1)} disabled={loading}>{t('modelViewer.reload')}</button>
          </div>
          <label htmlFor="model-category">{t('modelViewer.category')}</label>
          <SearchableOptionInput id="model-category" ariaLabel={t('modelViewer.category')} disabled={false} isFiniteCatalog localizeOptions={false}
            value={category} onChange={setCategory} options={[{ value: 'all', label: t('modelViewer.all') }, ...categories.map(value => ({ value, label: t(`modelViewer.category.${value}`) }))]} />
          <label htmlFor="model-search">{t('modelViewer.search')}</label>
          <input id="model-search" type="search" value={query} onChange={event => setQuery(event.target.value)} />
          <div className="model-viewer__catalog" aria-busy={loading}>
            <div className="model-viewer__catalog-content">
            {loading ? <p role="status">{t('modelViewer.loading')}</p> : groups.length === 0 ? <p role="status">{t('modelViewer.empty')}</p> :
              groups.map(group => <details key={group[0].id} open={group.some(item => item.id === selected) || undefined}>
                <summary data-localization-ignore="true">{group[0].species > 0 ? `#${group[0].species} ` : ''}{group[0].name} <span>({group.length})</span></summary>
                {group.map(item => <button key={item.id} type="button" aria-pressed={item.id === selected} onClick={() => select(item.id)} title={item.id}>
                  {item.species > 0 ? t('modelViewer.variant', { form: item.form, gender: item.gender }) : item.name}
                  {item.shiny ? ` · ${t('modelViewer.shiny')}` : ''}
                </button>)}
              </details>)}
            </div>
          </div>
        </aside>
        <div className="model-viewer__stage">
          <div className="model-viewer__toolbar">
            <strong data-localization-ignore="true">{entry?.name ?? t('modelViewer.selectModel')}</strong>
            <button type="button" disabled={!viewer.info} onClick={() => void viewer.camera('reset')}>{t('modelViewer.resetCamera')}</button>
            <button type="button" disabled={!viewer.info} onClick={() => void viewer.camera('frame')}>{t('modelViewer.frame')}</button>
            {viewer.loading ? <button type="button" onClick={() => setSelected('')}>{t('modelViewer.cancel')}</button> : null}
          </div>
          <div className="model-viewer__background">
            <label htmlFor="model-background-style">{t('modelViewer.backgroundStyle')}</label>
            <SearchableOptionInput id="model-background-style" ariaLabel={t('modelViewer.backgroundStyle')} disabled={false} isFiniteCatalog localizeOptions={false}
              value={background.grid ? 'grid' : 'solid'} onChange={value => setBackground(current => ({ ...current, grid: value === 'grid' }))}
              options={[{ value: 'solid', label: t('modelViewer.backgroundSolid') }, { value: 'grid', label: t('modelViewer.backgroundGrid') }]} />
            <label htmlFor="model-background-color">{t('modelViewer.backgroundColor')}</label>
            <ViewerColorPicker color={background.color} onChange={color => setBackground(current => ({ ...current, color }))} />
          </div>
          <div ref={viewer.viewport} className="model-viewer__viewport" tabIndex={0} role="region" aria-label={t('modelViewer.viewport')}
            style={{ backgroundColor: background.color }}
            onFocus={() => { if (viewer.info) void viewer.camera('focus'); }} onContextMenu={event => event.preventDefault()}>
            <p role="status">{viewer.loading ? t('modelViewer.opening') : !selected ? t('modelViewer.selectModel') : error ? t(message) : ''}</p>
          </div>
          <p className="model-viewer__controls">{t('modelViewer.controls')}</p>
          <span role="status">{viewer.info ? t('modelViewer.adapter', { adapter: viewer.info.adapter }) : ''}</span>
          {entry ? <small className="model-viewer__identifier" data-localization-ignore="true">{entry.id}</small> : null}
          {viewer.info ? <div className="model-viewer__animation">
            <label htmlFor="model-animation">{t('modelViewer.animation')}</label>
            <SearchableOptionInput id="model-animation" ariaLabel={t('modelViewer.animation')} disabled={false} isFiniteCatalog localizeOptions={false}
              value={viewer.info.clip ?? 'rest'} onChange={setAnimation} options={[{ value: 'rest', label: t('modelViewer.rest') }, ...viewer.info.clips.map(id => ({ value: id, label: id }))]} />
            {viewer.info.clips.length === 0 ? <p>{t('modelViewer.noAnimations')}</p> : null}
            <div className="model-viewer__toolbar">
              <button type="button" disabled={!viewer.info.clip} onClick={() => void viewer.playback(viewer.playing ? 'pause' : 'play')}>{t(viewer.playing ? 'modelViewer.pause' : 'modelViewer.play')}</button>
              <button type="button" disabled={!viewer.info.clip} onClick={() => void viewer.playback('restart')}>{t('modelViewer.restart')}</button>
              <label><input type="checkbox" checked={loop} disabled={!viewer.info.clip} onChange={event => { setLoop(event.target.checked); void viewer.playback('loop', event.target.checked ? 1 : 0); }} />{t('modelViewer.loop')}</label>
              <span>{!loop ? t('modelViewer.once') : ''}</span>
              <label htmlFor="model-speed">{t('modelViewer.speed')}</label>
              <SearchableOptionInput id="model-speed" ariaLabel={t('modelViewer.speed')} disabled={false} isFiniteCatalog localizeOptions={false}
                value={speed} onChange={value => { setSpeed(value); void viewer.playback('speed', Number(value)); }} options={['0.25', '0.5', '1', '1.5', '2', '4'].map(value => ({ value, label: `${value}x` }))} />
            </div>
            <label htmlFor="model-seek">{t('modelViewer.position')} {viewer.position.toFixed(2)} / {viewer.info.duration.toFixed(2)} s</label>
            <input id="model-seek" type="range" min="0" max={viewer.info.duration || 1} step="0.01" value={viewer.position} disabled={!viewer.info.clip} onChange={event => void viewer.playback('seek', Number(event.target.value))} />
            {viewer.info.warnings.map(warning => <p key={warning} role="status">{t(`modelViewer.warning.${warning}`)}</p>)}
          </div> : null}
        </div>
      </div>}
    {error ? <p className="model-viewer__error" role="alert">{t(message)} <code>{error}</code></p> : null}
  </section>;
}
