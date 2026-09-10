/* SPDX-License-Identifier: GPL-3.0-only */
import { invoke, isTauri } from '@tauri-apps/api/core';
import { Box } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { z } from 'zod';
import { kmCommandNames, projectPathsSchema, type EditSession, type ProjectPaths } from '../../bridge/contracts';
import { sendProjectBridgeRequest } from '../../bridge/projectBridgeRequest';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import { HeaderMemoryUsage } from '../settings/ProcessMemoryPanel';
import { modelError, useModelViewport, type ModelBackground, type ModelViewOptions } from './useModelViewport';
import { ModelEditHistory, ModelHistoryContext } from './ModelEditHistory';
import { ModelWorkspaceTools, ModelParts, ModelFrameInput, ModelHistoryPanel } from './ModelWorkspaceTools';
import { ModelTextureInspector, type ModelUv } from './ModelTextureInspector';
import { ViewerColorPicker } from './ViewerColorPicker';
import { ModelLightControls, useModelLight } from './ModelLightControls';
import { ModelTextureEditor } from './ModelTextureEditor';
import { ModelMaterialEditor } from './ModelMaterialEditor';
import { loadModelProperties, stagedAssetChanges, type AssetChange, type TextureChange, type ModelProperties, type ModelTexture, type TextureRule } from './modelTextureBridge';
import './ModelViewerSection.css';

const catalogSchema = z.array(z.object({
  id: z.string().max(1024), species: z.number().int(), form: z.number().int(), gender: z.number().int(), name: z.string(),
  category: z.enum(['pokemon', 'trainers', 'npcs', 'objects', 'environment', 'other']).default('pokemon'), shiny: z.boolean().default(false)
})).max(8192);
type Entry = z.infer<typeof catalogSchema>[number];
const backgroundKey = 'km-editor.model-viewer.background';
const defaultBackgroundColor = '#343b44';
const backgroundSchema = z.object({ color: z.string().regex(/^#[0-9a-f]{6}$/i), grid: z.boolean() });
function readBackground(): ModelBackground {
  try { return backgroundSchema.parse(JSON.parse(localStorage.getItem(backgroundKey) ?? 'null')); }
  catch { return { color: defaultBackgroundColor, grid: false }; }
}
export default function ModelViewerSection({ paths, session, disabled, onStage, onStageAsset, onDirtyChange }: {
  paths: ProjectPaths; session: EditSession | null; disabled: boolean;
  onStage: (model: string, change: TextureChange) => Promise<boolean>;
  onStageAsset: (model: string, change: AssetChange | null, restore?: boolean) => Promise<boolean>;
  onDirtyChange: (section: 'modelViewer', dirty: boolean) => void;
}) {
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
  const [light, setLight] = useModelLight();
  const [textureChanges, setTextureChanges] = useState<TextureChange[]>([]);
  const [textureDirty, setTextureDirty] = useState(false);
  const [materialDirty, setMaterialDirty] = useState(false);
  const [editing, setEditing] = useState(false);
  const [assetChanges, setAssetChanges] = useState<AssetChange[] | null>(null);
  const [restoreRevision, setRestoreRevision] = useState(0);
  const [restoring, setRestoring] = useState(false);
  const [restoreError, setRestoreError] = useState(false);
  const [history] = useState(() => new ModelEditHistory());
  const [tab, setTab] = useState('materials');
  const [original, setOriginal] = useState(false);
  const [properties, setProperties] = useState<ModelProperties | null>(null);
  const [options, setOptions] = useState<ModelViewOptions>({ display: 0, wireframe: false, hidden: [], selected: null });
  const [selectionRevision, setSelectionRevision] = useState(0);
  const selectPart = (part: number | null) => {
    setOptions(old => ({ ...old, selected: part }));
    setSelectionRevision(value => value + 1);
  };
  const [statistics, setStatistics] = useState(false);
  const [inGame, setInGame] = useState(true);
  const [image, setImage] = useState<{ texture: ModelTexture; changes: TextureRule[] } | null>(null);
  const [uv, setUv] = useState<ModelUv[]>([]);
  const uvRequest = useRef(0);
  const [range, setRange] = useState([0, 0]);
  useEffect(() => { history.clear(); if (!editing) { setOriginal(false); setImage(null); uvRequest.current++; } }, [editing, history]);
  useEffect(() => { setImage(current => current ? { ...current, changes: textureChanges.find(c => c.texture === current.texture.id)?.changes ?? [] } : null); }, [textureChanges]);
  const dirty = textureDirty || materialDirty;
  const pendingRestore = stagedAssetChanges(session, selected).some(change => change.restore);
  const dirtyChange = useCallback((value: boolean) => setTextureDirty(value), []);
  const materialDirtyChange = useCallback((value: boolean) => setMaterialDirty(value), []);
  const dialog = useModalDialog<HTMLElement>({ enabled: editing, canClose: !dirty && !restoring, onClose: () => setEditing(false) });
  useEffect(() => { onDirtyChange('modelViewer', dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange('modelViewer', false), [onDirtyChange]);
  useEffect(() => {
    try { localStorage.setItem(backgroundKey, JSON.stringify(background)); } catch { /* Session controls remain usable when storage is unavailable. */ }
  }, [background]);
  const pathKey = JSON.stringify(paths);
  const supported = ['sword', 'shield', 'scarlet', 'violet', 'za'].some(game => game === paths.selectedGame);
  const vanillaAssets = useMemo(() => properties?.assets.map(a => ({ asset: a.id, sourceHash: a.sourceHash, changes: [], restore: true })) ?? [], [properties]);
  const viewer = useModelViewport(paths, selected, animation, revision, !!image, background, original ? [] : textureChanges,
    original ? vanillaAssets : assetChanges ?? stagedAssetChanges(session, selected), light,
    editing ? { ...options, statistics, inGame } : { display: 0, wireframe: false, hidden: [], selected: null, statistics: false, inGame: true },
    selectPart, redo => { if (!original && !disabled && !restoring) { if (redo) history.redo(); else history.undo(); } });
  useEffect(() => {
    history.clear(); setOriginal(false); setProperties(null); setImage(null); setUv([]); uvRequest.current++;
    setOptions({ display: 0, wireframe: false, hidden: [], selected: null });
  }, [selected, pathKey, restoreRevision, history]);
  useEffect(() => {
    if (!selected || !editing) return;
    let live = true;
    void loadModelProperties(paths, selected).then(value => { if (live) setProperties(value); }).catch(() => { if (live) setProperties(null); });
    return () => { live = false; };
  }, [selected, pathKey, editing, restoreRevision]);
  useEffect(() => {
    if (!editing) return;
    const shortcut = (event: KeyboardEvent) => {
      if ((!event.ctrlKey && !event.metaKey) || !['z','y'].includes(event.key.toLowerCase()) || disabled || restoring || original) return;
      if (event.target instanceof Element && event.target.closest('input, textarea, [contenteditable="true"]')) return;
      event.preventDefault(); if (event.shiftKey || event.key.toLowerCase() === 'y') history.redo(); else history.undo();
    };
    window.addEventListener('keydown', shortcut);
    return () => window.removeEventListener('keydown', shortcut);
  }, [editing, history, disabled, restoring, original]);
  const inspectTexture = async (texture: ModelTexture, changes: TextureRule[]) => {
    const request = ++uvRequest.current;
    setImage({ texture, changes }); setUv([]);
    try {
      const parts = viewer.info?.parts.filter(p => texture.materials.includes(p.material)) ?? [];
      const values: ModelUv[] = [];
      for (const part of parts) { if (request !== uvRequest.current) return; values.push(await viewer.inspect(part.id)); }
      if (request === uvRequest.current) setUv(values);
    } catch { if (request === uvRequest.current) setUv([]); }
  };
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
  useEffect(() => { setLoop(viewer.info?.looped ?? false); setSpeed('1'); setRange([0, viewer.info?.frames ? viewer.info.frames - 1 : 0]); }, [viewer.info?.clip]);
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
  usePublishCommonEditorDiagnostics(restoreError ? [{ code: 'KM-MODEL-ASSET-EDIT-INVALID', domain: 'workflow.modelTextures', message: t('modelEditor.error'), severity: 'error' }] : error ? [{ code: error, domain: 'workflow.modelViewer', message: t(message), severity: 'error' }] :
    (viewer.info?.warnings ?? []).map(warning => ({ code: 'KM-MODEL-PARTIAL', domain: 'workflow.modelViewer', message: t(`modelViewer.warning.${warning}`), severity: 'warning' })));
  const select = (id: string) => { setTextureChanges([]); setAssetChanges(null); setSelected(id); setAnimation(null); };
  const restore = async () => {
    if (history.locked) return;
    history.lock(true);
    setRestoring(true); setRestoreError(false);
    try {
      if (await onStageAsset(selected, null, true)) { setTextureChanges([]); setAssetChanges(null); setRestoreRevision(value => value + 1); }
      else setRestoreError(true);
    } catch { setRestoreError(true); } finally { setRestoring(false); history.lock(false); }
  };
  return <ModelHistoryContext.Provider value={history}><section ref={dialog} className={`panel wide-panel model-viewer${editing ? ' model-viewer--editing' : ''}`} role={editing ? 'dialog' : undefined} aria-modal={editing || undefined} aria-labelledby="model-viewer-title" tabIndex={editing ? -1 : undefined}>
    <div data-editor-portal-host className="editor-portal-host" />
    <header className="model-viewer__header">
      <div className="model-viewer__header-status"><Box aria-hidden="true" size={22} />{editing ? <HeaderMemoryUsage /> : null}</div>
      <div className="model-viewer__heading"><h2 id="model-viewer-title">{t(editing ? 'modelEditor.title' : 'modelViewer.title')}</h2><p>{editing ? entry?.name : t('modelViewer.description')}</p></div>
      {editing ? <button type="button" disabled={dirty || restoring} onClick={() => setEditing(false)}>{t('modelEditor.close')}</button> : null}
    </header>
    <p className="model-viewer__note">{t('modelViewer.scope')}</p>
    {!supported ? <p role="status">{t('modelViewer.unsupportedGame')}</p> : !isTauri() ? <p role="status">{t('modelViewer.desktopRequired')}</p> :
      <div className="model-viewer__workspace">
        <aside className="model-viewer__browser" aria-label={t('modelViewer.models', { count: catalog.length })}>
          <div className="model-viewer__toolbar"><strong>{t('modelViewer.models', { count: catalog.length })}</strong>
            <button type="button" onClick={() => { setTextureChanges([]); setAssetChanges(null); setRevision(value => value + 1); }} disabled={loading || dirty || disabled}>{t('modelViewer.reload')}</button>
          </div>
          <label htmlFor="model-category">{t('modelViewer.category')}</label>
          <SearchableOptionInput id="model-category" ariaLabel={t('modelViewer.category')} disabled={false} isFiniteCatalog localizeOptions={false}
            value={category} onChange={setCategory} options={[{ value: 'all', label: t('modelViewer.all') }, ...categories.map(value => ({ value, label: t(`modelViewer.category.${value}`) }))]} />
          <label htmlFor="model-search">{t('modelViewer.search')}</label>
          <input id="model-search" type="search" value={query} onChange={event => setQuery(event.target.value)} />
          <div className="model-viewer__catalog" aria-busy={loading}>
            <div className="model-viewer__catalog-content">
            {loading ? <LoadingProgress label={t('modelViewer.loading')} /> : groups.length === 0 ? <p role="status">{t('modelViewer.empty')}</p> :
              groups.map(group => <details key={group[0].id} open={group.some(item => item.id === selected) || undefined}>
                <summary data-localization-ignore="true">{group[0].species > 0 ? `#${group[0].species} ` : ''}{group[0].name} <span>({group.length})</span></summary>
                {group.map(item => <button key={item.id} type="button" disabled={dirty || disabled} aria-pressed={item.id === selected} onClick={() => select(item.id)} title={item.id}>
                  {item.species > 0 ? t('modelViewer.variant', { form: item.form, gender: item.gender }) : item.name}
                  {item.shiny ? ` · ${t('modelViewer.shiny')}` : ''}
                </button>)}
              </details>)}
            </div>
          </div>
        </aside>
        <div className="model-viewer__stage">
          {editing ? <div className="model-workspace__top"><ModelWorkspaceTools ready={!!viewer.info && !!properties} disabled={disabled || restoring}
            original={original} onCompare={() => { void viewer.playback('pause'); setOriginal(!original); }} options={options} onOptions={setOptions}
            camera={viewer.camera} stats={statistics} onStats={setStatistics} inGame={inGame} onLighting={setInGame} orientation={viewer.stats} historyOpen={tab === 'history'} onHistory={() => setTab(tab === 'history' ? 'materials' : 'history')} />
            {original ? <p className="model-workspace__comparison" role="status">{t('modelWorkspace.comparing')}</p> : null}
          </div> : null}
          <div className="model-viewer__properties model-viewer__properties--primary">
          <div className="model-viewer__toolbar">
            <strong data-localization-ignore="true">{entry?.name ?? t('modelViewer.selectModel')}</strong>
            {!editing ? <button type="button" className="primary-button model-viewer__edit" disabled={!selected || disabled} onClick={() => setEditing(true)}>{t('modelEditor.edit')}</button> : null}
            <button type="button" disabled={!viewer.info} onClick={() => void viewer.camera('reset')}>{t('modelViewer.resetCamera')}</button>
            {viewer.loading && !editing ? <button type="button" disabled={dirty} onClick={() => select('')}>{t('modelViewer.cancel')}</button> : null}
          </div>
          <div className="model-viewer__background">
            <label htmlFor="model-background-style">{t('modelViewer.backgroundStyle')}</label>
            <SearchableOptionInput id="model-background-style" ariaLabel={t('modelViewer.backgroundStyle')} disabled={false} isFiniteCatalog localizeOptions={false}
              value={background.grid ? 'grid' : 'solid'} onChange={value => setBackground(current => ({ ...current, grid: value === 'grid' }))}
              options={[{ value: 'solid', label: t('modelViewer.backgroundSolid') }, { value: 'grid', label: t('modelViewer.backgroundGrid') }]} />
            <label htmlFor="model-background-color">{t('modelViewer.backgroundColor')}</label>
            <ViewerColorPicker color={background.color} onChange={color => setBackground(current => ({ ...current, color }))} />
            <button type="button" disabled={background.color.toLowerCase() === defaultBackgroundColor}
              onClick={() => setBackground(current => ({ ...current, color: defaultBackgroundColor }))}>{t('modelViewer.resetBackground')}</button>
          </div>
          </div>
          {editing && image ? <ModelTextureInspector paths={paths} model={selected} texture={image.texture} changes={image.changes} uv={uv} onClose={() => setImage(null)} /> : null}
          <div ref={viewer.viewport} className="model-viewer__viewport" tabIndex={0} role="region" aria-label={t('modelViewer.viewport')}
            style={{ backgroundColor: background.color }}
            onFocus={() => { if (viewer.info) void viewer.camera('focus'); }} onContextMenu={event => event.preventDefault()}>
            <p role="status">{viewer.loading ? t('modelViewer.opening') : !selected ? t('modelViewer.selectModel') : error ? t(message) : ''}</p>
          </div>
          <p className="model-viewer__controls">{t('modelViewer.controls')}</p>
          {editing && statistics && viewer.info ? <div className="model-workspace__statistics">
            <span>{viewer.stats.fps.toFixed(0)} FPS</span>
            <span>{t('modelWorkspace.triangles', { count: viewer.info.parts.reduce((sum, part) => sum + part.triangles, 0) })}</span>
            <span>{t('modelWorkspace.textureMemory', { value: (viewer.info.textureBytes / 1048576).toFixed(1) })}</span>
            <span>{t('modelWorkspace.renderTime', { value: viewer.frameMs.toFixed(1) })}</span>
            <span>{t('modelWorkspace.textureDimensions')} <span data-localization-ignore="true">{[...new Set(viewer.info.textures.map(size => size.join(' × ')))].join(', ')}</span></span>
          </div> : null}
          <span role="status">{viewer.info ? t('modelViewer.adapter', { adapter: viewer.info.adapter }) : ''}</span>
          {entry ? <small className="model-viewer__identifier" data-localization-ignore="true">{entry.id}</small> : null}
          {viewer.info ? <details className="model-viewer__animation-group" open={!editing || !!viewer.info.clip}>
            <summary>{t('modelViewer.animation')}</summary>
            <div className="model-viewer__animation">
            <label htmlFor="model-animation">{t('modelViewer.animation')}</label>
            <SearchableOptionInput id="model-animation" ariaLabel={t('modelViewer.animation')} disabled={false} isFiniteCatalog localizeOptions={false} portalMenu={!editing}
              value={viewer.info.clip ?? 'rest'} onChange={setAnimation} options={[{ value: 'rest', label: t('modelViewer.rest') }, ...viewer.info.clips.map(id => ({ value: id, label: id }))]} />
            {viewer.info.clips.length === 0 ? <p>{t('modelViewer.noAnimations')}</p> : null}
            <div className="model-viewer__toolbar">
              <button type="button" disabled={!viewer.info.clip} onClick={() => void viewer.playback(viewer.playing ? 'pause' : 'play')}>{t(viewer.playing ? 'modelViewer.pause' : 'modelViewer.play')}</button>
              <button type="button" disabled={!viewer.info.clip} onClick={() => void viewer.playback('restart')}>{t('modelViewer.restart')}</button>
              <label><input type="checkbox" checked={loop} disabled={!viewer.info.clip} onChange={event => { setLoop(event.target.checked); void viewer.playback('loop', event.target.checked ? 1 : 0); }} />{t('modelViewer.loop')}</label>
              <span>{!loop ? t('modelViewer.once') : ''}</span>
              <label htmlFor="model-speed">{t('modelViewer.speed')}</label>
              <SearchableOptionInput id="model-speed" ariaLabel={t('modelViewer.speed')} disabled={false} isFiniteCatalog localizeOptions={false} portalMenu={!editing}
                value={speed} onChange={value => { setSpeed(value); void viewer.playback('speed', Number(value)); }} options={['0.25', '0.5', '1', '1.5', '2', '4'].map(value => ({ value, label: `${value}x` }))} />
            </div>
            <label htmlFor="model-seek">{t('modelViewer.position')} {viewer.position.toFixed(2)} / {viewer.info.duration.toFixed(2)} s</label>
            <input id="model-seek" type="range" min="0" max={viewer.info.duration || 1} step="0.01" value={viewer.position} disabled={!viewer.info.clip} onChange={event => void viewer.playback('seek', Number(event.target.value))} />
            {editing && viewer.info.clip ? <div className="model-workspace__bar model-workspace__frames">
              <button type="button" onClick={() => void viewer.playback('previousFrame')}>{t('modelWorkspace.previousFrame')}</button>
              <ModelFrameInput label={t('modelWorkspace.frame')} min={0} max={viewer.info.frames-1} value={Math.round(viewer.position*viewer.info.frameRate)}
                onCommit={value => { void viewer.playback('pause'); void viewer.playback('seek', value/viewer.info!.frameRate); }} />
              <button type="button" onClick={() => void viewer.playback('nextFrame')}>{t('modelWorkspace.nextFrame')}</button>
              {['rangeStart','rangeEnd'].map((key, i) => <ModelFrameInput key={key} label={t(`modelWorkspace.${key}`)} min={i ? range[0] : 0} max={i ? viewer.info!.frames-1 : range[1]} value={range[i]}
                onCommit={value => { setRange(old => old.map((v,index) => index === i ? value : v)); void viewer.playback(key, value/viewer.info!.frameRate); }} />)}
            </div> : null}
            {viewer.info.warnings.map(warning => <p key={warning} role="status">{t(`modelViewer.warning.${warning}`)}</p>)}
          </div></details> : null}
          <div className="model-viewer__properties model-viewer__properties--secondary" hidden={!editing}>
          {editing ? <nav className="model-workspace__tabs" aria-label={t('modelWorkspace.inspector')}>
            {['parts','materials','textures','scene'].map(value => <button type="button" key={value} aria-pressed={tab === value} onClick={() => setTab(value)}>{t(`modelWorkspace.${value}`)}</button>)}
          </nav> : null}
          {editing && tab === 'history' ? <ModelHistoryPanel /> : null}
          <div hidden={editing && tab !== 'parts'}>{editing ? <ModelParts parts={viewer.info?.parts ?? []} options={options} onOptions={setOptions} onSelect={selectPart}
            onMaterial={() => { setSelectionRevision(value => value + 1); setTab('materials'); }} /> : null}</div>
          <div hidden={editing && tab !== 'scene'}>
            {editing && !inGame ? <ModelLightControls light={light} onChange={setLight} /> : editing ? <p className="model-workspace__lighting-help">{t('modelWorkspace.inGameHelp')}</p> : null}
            {editing ? <div className="model-workspace__scene-background">
              <h3>{t('modelViewer.backgroundStyle')}</h3>
              <label><input type="checkbox" checked={background.grid} onChange={event => setBackground(old => ({ ...old, grid: event.target.checked }))} />{t('modelViewer.backgroundGrid')}</label>
              <ViewerColorPicker id="model-scene-background" label={t('modelViewer.backgroundColor')} color={background.color} onChange={color => setBackground(old => ({ ...old, color }))} />
              <button type="button" disabled={background.color === defaultBackgroundColor} onClick={() => setBackground(old => ({ ...old, color: defaultBackgroundColor }))}>{t('modelViewer.resetBackground')}</button>
            </div> : null}
          </div>
          {selected ? <div hidden={!editing}>
            {editing ? <>
              <details className="model-workspace__restore"><summary>{t('modelEditor.restore')}</summary><p>{t('modelEditor.restoreHelp')}</p>
              <button type="button" disabled={disabled || restoring} onClick={() => void restore()}>{t(restoring ? 'modelEditor.restoring' : 'modelEditor.restore')}</button>
              </details>
              {restoreError ? <p role="alert">{t('modelEditor.error')}</p> : null}
              {dirty ? <p role="status">{t('modelEditor.dirty')}</p> : null}
              {pendingRestore ? <p role="status">{t('modelEditor.pendingRestore')}</p> : null}
              <div hidden={tab !== 'materials'}><ModelMaterialEditor key={`${selected}/${restoreRevision}`} paths={paths} model={selected} session={session} disabled={disabled || restoring || pendingRestore || original}
                selectedMaterial={viewer.info?.parts.find(p => p.id === options.selected)?.material}
                selectionRevision={selectionRevision}
                onSelectMaterial={material => selectPart(viewer.info?.parts.find(p => p.material === material)?.id ?? null)}
                onDirtyChange={materialDirtyChange} onStage={onStageAsset} onPreview={setAssetChanges} /></div>
            </> : null}
            <div hidden={editing && tab !== 'textures'}><ModelTextureEditor key={`${selected}/${restoreRevision}`} paths={paths} model={selected} session={session} disabled={disabled || restoring || pendingRestore || original}
              selectedMaterial={viewer.info?.parts.find(p => p.id === options.selected)?.material} onInspect={(texture, changes) => void inspectTexture(texture, changes)}
              selectionRevision={selectionRevision}
              onPreview={setTextureChanges} onStage={onStage} onDirtyChange={dirtyChange} /></div>
          </div> : null}
          </div>
        </div>
      </div>}
    {error ? <p className="model-viewer__error" role="alert">{t(message)} <code>{error}</code></p> : null}
  </section></ModelHistoryContext.Provider>;
}
