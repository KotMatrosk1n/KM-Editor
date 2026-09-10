/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useRef, useState } from 'react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { ViewerColorPicker } from './ViewerColorPicker';
import { useModelDraft, useModelHistory } from './ModelEditHistory';
import { loadModelTextures, stagedTextureChanges, type ModelTexture, type TextureChange, type TextureRule } from './modelTextureBridge';

type Props = {
  paths: ProjectPaths; model: string; session: EditSession | null; disabled: boolean;
  onPreview: (changes: TextureChange[]) => void; onStage: (model: string, change: TextureChange) => Promise<boolean>;
  onDirtyChange: (dirty: boolean) => void;
  onInspect?: (texture: ModelTexture, changes: TextureRule[]) => void;
  selectedMaterial?: string;
  selectionRevision?: number;
};
function TextureImage({ texture, onColor, disabled }: { texture: ModelTexture; onColor: (color: string) => void; disabled: boolean }) {
  const { t } = useLocalization(); const canvas = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const bytes = Uint8ClampedArray.from(atob(texture.pixels), c => c.charCodeAt(0));
    if (bytes.length !== texture.thumbnailWidth * texture.thumbnailHeight * 4) return;
    canvas.current?.getContext('2d')?.putImageData(new ImageData(bytes, texture.thumbnailWidth, texture.thumbnailHeight), 0, 0);
  }, [texture]);
  return <canvas ref={canvas} width={texture.thumbnailWidth} height={texture.thumbnailHeight} aria-label={t('modelViewer.texture.image')}
    onClick={event => {
      if (disabled) return;
      const area = event.currentTarget.getBoundingClientRect();
      const x = Math.min(texture.thumbnailWidth - 1, Math.max(0, Math.floor((event.clientX - area.left) / area.width * texture.thumbnailWidth)));
      const y = Math.min(texture.thumbnailHeight - 1, Math.max(0, Math.floor((event.clientY - area.top) / area.height * texture.thumbnailHeight)));
      const rgba = event.currentTarget.getContext('2d')?.getImageData(x, y, 1, 1).data;
      if (rgba && rgba[3]) onColor(`#${[...rgba.slice(0, 3)].map(value => value.toString(16).padStart(2, '0')).join('')}`);
    }} />;
}
export function ModelTextureEditor({ paths, model, session, disabled, onPreview, onStage, onDirtyChange, onInspect, selectedMaterial, selectionRevision }: Props) {
  const { t } = useLocalization();
  const [textures, setTextures] = useState<ModelTexture[]>([]);
  const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false); const [error, setError] = useState(false);
  const [state, editState, applyState] = useModelDraft({ drafts: {} as Record<string, TextureRule[]>, selected: '', from: '#ffffff', to: '#ffffff', tolerance: 15 }, 'texture');
  const { drafts, selected, from, to, tolerance } = state;
  const history = useModelHistory();
  const setFrom = (from: string) => editState(old => ({ ...old, from }), t('modelViewer.texture.from'), 'texture-from');
  const setTo = (to: string) => editState(old => ({ ...old, to }), t('modelViewer.texture.to'), 'texture-to');
  const setTolerance = (tolerance: number) => editState(old => ({ ...old, tolerance }), t('modelViewer.texture.title'), 'texture-tolerance');
  const pathKey = JSON.stringify(paths);
  useEffect(() => {
    let live = true; setLoading(true); setError(false);
    void loadModelTextures(JSON.parse(pathKey) as ProjectPaths, model).then(items => {
      if (live) { setTextures(items); const color = items[0]?.colors[0] ?? '#ffffff'; applyState({ ...state, selected: items[0]?.id ?? '', from: color, to: color }); }
    }).catch(() => { if (live) setError(true); }).finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [pathKey, model]);
  const texture = textures.find(item => item.id === selected);
  const staged = useMemo(() => stagedTextureChanges(session, textures.map(item => item.id)), [session, textures]);
  const setSelected = (value: string) => {
    if (value === selected) return;
    const color = textures.find(item => item.id === value)?.colors[0] ?? '#ffffff';
    if (from.toLowerCase() === to.toLowerCase()) { applyState({ ...state, selected: value, from: color, to: color }); return; }
    editState(old => ({ ...old, selected: value, from: color, to: color,
      drafts: from.toLowerCase() !== to.toLowerCase() ? { ...old.drafts, [selected]: pending.find(c => c.texture === selected)?.changes ?? [] } : old.drafts }), t('modelViewer.texture.select'), 'texture-selection');
  };
  const pending = useMemo(() => textures.flatMap(item => {
    const previous = staged.find(change => change.texture === item.id);
    const rules = drafts[item.id] ?? previous?.changes ?? [];
    const current = item.id === selected && rules.length < 32 && from.toLowerCase() !== to.toLowerCase() ? [...rules, { from, to, tolerance }] : rules;
    return current.length ? [{ texture: item.id, sourceHash: item.sourceHash, changes: current }] : [];
  }), [textures, drafts, staged, selected, from, to, tolerance]);
  useEffect(() => {
    const match = textures.find(item => item.materials.includes(selectedMaterial ?? ''));
    if (match) setSelected(match.id);
  }, [selectedMaterial, selectionRevision, textures]);
  const signature = (items: TextureChange[]) => JSON.stringify([...items].sort((a, b) => a.texture.localeCompare(b.texture)));
  const dirty = signature(pending) !== signature(staged);
  const selectedDirty = signature(pending.filter(item => item.texture === selected)) !== signature(staged.filter(item => item.texture === selected));
  const locked = busy || disabled || history?.locked === true;
  const colorLocked = locked || texture?.editable === false;
  const previewKey = JSON.stringify(pending);
  useEffect(() => {
    const timer = setTimeout(() => onPreview(JSON.parse(previewKey) as TextureChange[]), 700);
    return () => clearTimeout(timer);
  }, [previewKey, onPreview]);
  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange(false), [onDirtyChange]);
  usePublishCommonEditorDiagnostics(error ? [{ code: 'KM-MODEL-TEXTURE-EDIT-INVALID', domain: 'workflow.modelTextures', severity: 'error', message: t('modelViewer.texture.error') }] : []);
  const pick = (color: string) => {
    editState(old => ({ ...old, drafts: texture && from.toLowerCase() !== to.toLowerCase() ? { ...old.drafts, [selected]: pending.find(c => c.texture === selected)?.changes ?? [] } : old.drafts,
      from: color, to: color }), t('modelViewer.texture.keep'), 'texture-pick');
  };
  const keep = () => {
    if (!texture || from.toLowerCase() === to.toLowerCase()) return;
    editState(old => ({ ...old, drafts: { ...old.drafts, [selected]: pending.find(change => change.texture === selected)?.changes ?? [] }, from: to }), t('modelViewer.texture.keep'), 'texture-keep');
  };
  const stage = async () => {
    if (!texture) return; setBusy(true); setError(false); history?.lock(true);
    const change = pending.find(item => item.texture === texture.id) ?? { texture: texture.id, sourceHash: texture.sourceHash, changes: [] };
    try {
      if (await onStage(model, change)) { const next = { ...drafts }; delete next[texture.id]; applyState({ ...state, drafts: next, from: to }); history?.clear(); }
      else setError(true);
    } catch { setError(true); }
    finally { setBusy(false); history?.lock(false); }
  };
  return <section className="model-textures" aria-labelledby="model-textures-title" aria-busy={loading || busy}>
    <h3 id="model-textures-title">{t('modelViewer.texture.title')}</h3>
    <p>{t('modelViewer.texture.shared')}</p>
    {dirty ? <p role="status">{t('modelViewer.texture.dirty')}</p> : null}
    {loading ? <p role="status">{t('modelViewer.loading')}</p> : textures.length === 0 ? <p>{t('modelViewer.texture.empty')}</p> : <>
      <label htmlFor="model-texture-select">{t('modelViewer.texture.select')}</label>
      <SearchableOptionInput id="model-texture-select" ariaLabel={t('modelViewer.texture.select')} value={selected} disabled={locked}
        isFiniteCatalog localizeOptions={false} onChange={setSelected}
        options={textures.map(item => ({ value: item.id, label: item.id.split('/').at(-1) ?? item.id }))} />
      {texture ? <>
        {!texture.editable ? <p role="status">{t('modelViewer.texture.unsupported')}</p> : null}
        <p data-localization-ignore="true">{texture.materials.join(', ')}</p>
        <p>{t('modelViewer.texture.size', { width: texture.width, height: texture.height, levels: texture.mipCount })}</p>
        {onInspect ? <button type="button" disabled={locked} onClick={() => onInspect(texture, pending.find(c => c.texture === selected)?.changes ?? [])}>{t('modelWorkspace.inspectTexture')}</button> : null}
        <div className="model-textures__colors">
          <TextureImage texture={texture} onColor={pick} disabled={colorLocked} />
          <fieldset disabled={colorLocked}>
            <div className="model-textures__swatches">{texture.colors.map(color => <button key={color} type="button"
              aria-label={t('modelViewer.texture.pick', { color })} disabled={busy} onClick={() => pick(color)}><span aria-hidden="true" style={{ backgroundColor: color }} /></button>)}</div>
            <div className="model-textures__pickers">
              <label htmlFor="model-texture-from">{t('modelViewer.texture.from')}</label>
              <ViewerColorPicker id="model-texture-from" label={t('modelViewer.texture.from')} color={from} onChange={setFrom} />
              <label htmlFor="model-texture-to">{t('modelViewer.texture.to')}</label>
              <ViewerColorPicker id="model-texture-to" label={t('modelViewer.texture.to')} color={to} onChange={setTo} />
            </div>
            <label htmlFor="model-texture-tolerance">{t('modelViewer.texture.tolerance', { value: tolerance })}</label>
            <input id="model-texture-tolerance" type="range" min="0" max="100" step="1" value={tolerance} disabled={busy} onChange={event => setTolerance(Number(event.target.value))} />
            <p>{t('modelViewer.texture.toleranceHelp')}</p>
            <p>{t('modelViewer.texture.preview')}</p>
          </fieldset>
        </div>
        <div className="model-viewer__toolbar">
          <button type="button" disabled={colorLocked || from.toLowerCase() === to.toLowerCase()} onClick={keep}>{t('modelViewer.texture.keep')}</button>
          <button type="button" disabled={colorLocked} onClick={() => editState(old => ({ ...old, drafts: { ...old.drafts, [selected]: [] }, to: from }), t('modelViewer.texture.reset'), 'texture-reset')}>{t('modelViewer.texture.reset')}</button>
          <button type="button" disabled={locked || !selectedDirty} onClick={() => void stage()}>{t(busy ? 'modelViewer.texture.encoding' : 'modelViewer.texture.stage')}</button>
          <button type="button" disabled={locked || !dirty} onClick={() => editState(old => ({ ...old, drafts: {}, to: from }), t('modelViewer.texture.discard'), 'texture-discard')}>{t('modelViewer.texture.discard')}</button>
        </div>
      </> : null}
    </>}
    {error ? <p role="alert">{t('modelViewer.texture.error')} <code>KM-MODEL-TEXTURE-EDIT-INVALID</code></p> : null}
  </section>;
}
