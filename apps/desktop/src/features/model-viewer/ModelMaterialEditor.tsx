/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useState } from 'react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { ViewerColorPicker } from './ViewerColorPicker';
import { loadModelProperties, stagedAssetChanges, type AssetChange, type MaterialField, type ModelProperties } from './modelTextureBridge';

type Draft = { values: string[]; text: string | null };
type Props = {
  paths: ProjectPaths; model: string; session: EditSession | null; disabled: boolean;
  onDirtyChange: (dirty: boolean) => void; onPreview: (changes: AssetChange[]) => void;
  onStage: (model: string, change: AssetChange | null, restore?: boolean) => Promise<boolean>;
};
export function ModelMaterialEditor({ paths, model, session, disabled, onDirtyChange, onPreview, onStage }: Props) {
  const { t } = useLocalization();
  const [properties, setProperties] = useState<ModelProperties | null>(null);
  const [selection, setSelection] = useState(''); const [search, setSearch] = useState('');
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [busy, setBusy] = useState(false); const [error, setError] = useState(false);
  const pathKey = JSON.stringify(paths);
  useEffect(() => {
    let live = true;
    void loadModelProperties(JSON.parse(pathKey) as ProjectPaths, model).then(value => {
      if (live) { setProperties(value); setSelection(value.materials.flatMap(a => a.fields.map(f => `${a.id}|${f.material}`))[0] ?? ''); }
    }).catch(() => { if (live) setError(true); });
    return () => { live = false; };
  }, [pathKey, model]);
  const staged = useMemo(() => stagedAssetChanges(session, model), [session, model]);
  const asset = properties?.materials.find(a => selection.startsWith(a.id + '|'));
  const material = asset ? selection.slice(asset.id.length + 1) : '';
  const key = (id: string, field: MaterialField) => `${id}|${field.key}`;
  const baseline = (id: string, field: MaterialField): Draft => {
    const saved = staged.find(a => a.asset === id)?.changes.find(c => c.key === field.key);
    return { values: (saved?.values ?? field.values).map(String), text: saved?.text ?? field.text };
  };
  const current = (id: string, field: MaterialField) => drafts[key(id, field)] ?? baseline(id, field);
  const valid = (field: MaterialField, value: Draft) => value.values.every(v => v.trim() !== '' && Number.isFinite(Number(v)) && Math.abs(Number(v)) <= 1_000_000 && (field.kind !== 'int' || Number.isInteger(Number(v))));
  const invalid = properties?.materials.some(a => a.fields.some(f => drafts[key(a.id, f)] && !valid(f, current(a.id, f)))) ?? false;
  const dirty = Object.keys(drafts).length > 0;
  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange(false), [onDirtyChange]);
  const changes = properties?.materials.flatMap(a => {
    const edits = a.fields.flatMap(field => {
      const value = current(a.id, field);
      if (!field.editable || !valid(field, value)) return [];
      if (JSON.stringify(value) === JSON.stringify({ values: field.values.map(String), text: field.text })) return [];
      return [{ key: field.key, values: ['texture', 'textureIndex', 'text'].includes(field.kind) ? [] : value.values.map(Number),
        text: ['texture', 'textureIndex', 'text'].includes(field.kind) ? value.text : null }];
    });
    return edits.length ? [{ asset: a.id, sourceHash: a.sourceHash, changes: edits }] : [];
  }) ?? [];
  const previewKey = JSON.stringify([...staged.filter(a => a.restore), ...changes.filter(c => !staged.some(a => a.asset === c.asset && a.restore))]);
  useEffect(() => { const timer = setTimeout(() => onPreview(JSON.parse(previewKey) as AssetChange[]), 700); return () => clearTimeout(timer); }, [previewKey, onPreview]);
  usePublishCommonEditorDiagnostics(error ? [{ code: 'KM-MODEL-ASSET-EDIT-INVALID', domain: 'workflow.modelTextures', severity: 'error', message: t('modelEditor.error') }] : []);
  const edit = (field: MaterialField, value: Draft) => {
    if (!asset) return;
    setDrafts(previous => { const result = { ...previous }; const id = key(asset.id, field);
      if (JSON.stringify(value) === JSON.stringify(baseline(asset.id, field))) delete result[id]; else result[id] = value;
      return result;
    });
  };
  const save = async () => {
    if (!asset || invalid) return; setBusy(true); setError(false);
    try {
      if (await onStage(model, changes.find(c => c.asset === asset.id) ?? { asset: asset.id, sourceHash: asset.sourceHash, changes: [] }))
        setDrafts(previous => Object.fromEntries(Object.entries(previous).filter(([id]) => !id.startsWith(asset.id + '|'))));
      else setError(true);
    } catch { setError(true); } finally { setBusy(false); }
  };
  const fields = asset?.fields.filter(f => f.material === material && `${f.name} ${f.group}`.toLowerCase().includes(search.toLowerCase())) ?? [];
  return <section className="model-materials" aria-labelledby="model-materials-title" aria-busy={busy || !properties}>
    <h3 id="model-materials-title">{t('modelEditor.materials')}</h3>
    <p>{t('modelEditor.materialHelp')}</p>
    {error ? <p role="alert">{t('modelEditor.error')}</p> : null}
    {!properties && !error ? <p role="status">{t('modelViewer.loading')}</p> : null}
    {properties ? <>
      <label htmlFor="model-material-select">{t('modelEditor.material')}</label>
      <SearchableOptionInput id="model-material-select" ariaLabel={t('modelEditor.material')} value={selection} disabled={busy || disabled}
        isFiniteCatalog localizeOptions={false} onChange={setSelection} options={properties.materials.flatMap(a => [...new Set(a.fields.map(f => f.material))].map(name => ({ value: `${a.id}|${name}`, label: `${name} (${a.id.split('/').at(-1)})` })))} />
      <label htmlFor="model-property-search">{t('modelEditor.search')}</label>
      <input id="model-property-search" type="search" value={search} onChange={event => setSearch(event.target.value)} />
      <fieldset disabled={busy || disabled}>
        {[...new Set(fields.map(f => f.group))].map(group => <details key={group} open={group === 'colors' || !!search}>
          <summary>{t(`modelEditor.group.${group}`)} ({fields.filter(f => f.group === group).length})</summary>
          {fields.filter(f => f.group === group).map(field => {
            const value = current(asset!.id, field);
            const selectedRestore = staged.some(a => a.asset === asset?.id && a.restore);
            return <fieldset key={field.key} className="model-materials__field" disabled={!field.editable || selectedRestore}>
              <legend data-localization-ignore="true">{field.name}</legend>
              {field.options.length ? <SearchableOptionInput id={`model-property-${field.key}`} ariaLabel={field.name}
                disabled={false} isFiniteCatalog localizeOptions={false}
                value={field.kind === 'int' ? value.values[0] : value.text ?? ''}
                onChange={text => edit(field, field.kind === 'int' ? { values: [text], text: null } : { values: [], text })}
                options={field.options.map(option => ({ value: option, label: option }))} />
                : field.kind === 'text' ? <span data-localization-ignore="true">{field.text}</span> : <>
                {field.kind === 'vector' && field.name.toLowerCase().includes('color') ? <ViewerColorPicker id={`material-color-${field.key}`} label={field.name}
                  color={'#' + value.values.slice(0, 3).map(v => Math.round(Math.max(0, Math.min(1, Number(v) || 0)) * 255).toString(16).padStart(2, '0')).join('')}
                  onChange={color => edit(field, { values: [...[1, 3, 5].map(i => String(parseInt(color.slice(i, i + 2), 16) / 255)), ...value.values.slice(3)], text: null })} /> : null}
                <div className="model-materials__vector">{value.values.map((v, index) => <label key={index} data-localization-ignore="true">{value.values.length > 1 ? (field.name.toLowerCase().includes('color') ? 'RGBA' : 'XYZW')[index] : field.name}
                  <input type="number" step={field.kind === 'int' ? '1' : 'any'} aria-label={`${field.name} ${index + 1}`} value={v} onChange={event => edit(field,
                    { values: value.values.map((old, at) => at === index ? event.target.value : old), text: null })} />
                </label>)}</div>
              </>}
              {!field.editable ? <small>{t('modelEditor.readOnly')}</small> : !field.previewed ? <small>{t('modelEditor.gameOnly')}</small> : null}
            </fieldset>;
          })}
        </details>)}
      </fieldset>
      {invalid ? <p role="alert">{t('modelEditor.invalid')}</p> : null}
      <div className="model-viewer__toolbar">
        <button type="button" disabled={busy || disabled || invalid || !asset || !Object.keys(drafts).some(k => k.startsWith(asset.id + '|'))} onClick={() => void save()}>{t('modelEditor.stageMaterial')}</button>
        <button type="button" disabled={busy || disabled || !dirty} onClick={() => setDrafts({})}>{t('modelViewer.texture.discard')}</button>
      </div>
      <details><summary>{t('modelEditor.assets', { count: properties.assets.length })}</summary>
        <ul className="model-materials__assets">{properties.assets.map(a => <li key={a.id} data-localization-ignore="true" title={a.id}>{a.id.split('/').at(-1)} ({a.size.toLocaleString()} B)</li>)}</ul>
      </details>
    </> : null}
  </section>;
}
