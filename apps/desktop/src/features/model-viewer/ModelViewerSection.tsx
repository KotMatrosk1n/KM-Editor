/* SPDX-License-Identifier: GPL-3.0-only */
import { invoke, isTauri } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { ScanLine } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { z } from 'zod';
import { kmCommandNames, projectPathsSchema, type ProjectPaths } from '../../bridge/contracts';
import { sendProjectBridgeRequest } from '../../bridge/projectBridgeRequest';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useLocalization } from '../../localization';
import './ModelViewerSection.css';

const catalogSchema = z.array(z.object({
  id: z.string().max(1024), species: z.number().int(), form: z.number().int(),
  gender: z.number().int(), name: z.string()
})).max(8192);
type Entry = z.infer<typeof catalogSchema>[number];
const infoSchema = z.object({ adapter: z.string(), backend: z.literal('DX12'), selection: z.literal('Auto') });

export default function ModelViewerSection({ paths }: { paths: ProjectPaths }) {
  const { t } = useLocalization();
  const [catalog, setCatalog] = useState<Entry[]>([]);
  const [selected, setSelected] = useState('');
  const [loading, setLoading] = useState(false);
  const [opening, setOpening] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [adapter, setAdapter] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const sessionRef = useRef<string | null>(null);
  const pathKey = JSON.stringify(paths);
  const supported = paths.selectedGame === 'scarlet' || paths.selectedGame === 'violet';
  useEffect(() => {
    setCatalog([]); setSelected(''); setError(null); setAdapter(null); setOpening(false);
    if (!supported || !isTauri()) return;
    const session = crypto.randomUUID();
    let active = true;
    sessionRef.current = session;
    setLoading(true);
    const subscription = listen<{ session: string; error: string | null }>('model-preview-status', event => {
      if (!active || event.payload.session !== session) return;
      setAdapter(null);
      if (event.payload.error) setError(event.payload.error);
    });
    void (async () => {
      try {
        await invoke('model_preview_activate', { session });
        if (!active) return;
        const entries = await sendProjectBridgeRequest(
          requestJson => invoke<string>('project_bridge', { requestJson }),
          kmCommandNames.modelCatalog, { paths: projectPathsSchema.parse(JSON.parse(pathKey)) }, catalogSchema
        );
        if (!active) return;
        setCatalog(entries); setSelected(entries[0]?.id ?? '');
      } catch (cause) {
        if (active) setError(errorCode(cause));
      } finally { if (active) setLoading(false); }
    })();
    return () => {
      active = false;
      if (sessionRef.current === session) sessionRef.current = null;
      void subscription.then(unlisten => unlisten()).catch(() => {});
      void invoke('model_preview_close', { session }).catch(() => {});
    };
  }, [pathKey, supported, revision]);

  const options = useMemo(() => catalog.map(entry => ({
    value: entry.id,
    label: t('modelViewer.entry', { species: entry.species, name: entry.name, form: entry.form, gender: entry.gender }),
    searchAliases: [entry.name, String(entry.species), entry.id]
  })), [catalog, t]);
  const entry = catalog.find(value => value.id === selected);
  async function open() {
    const session = sessionRef.current;
    if (!session || !entry || opening) return;
    setOpening(true); setError(null); setAdapter(null);
    try {
      const info = infoSchema.parse(await invoke('model_preview_open', {
        paths, id: entry.id, title: t('modelViewer.windowTitle', { name: entry.name }), session
      }));
      if (sessionRef.current === session) setAdapter(info.adapter);
    } catch (cause) {
      if (sessionRef.current === session && errorCode(cause) !== 'KM-MODEL-CANCELLED') setError(errorCode(cause));
    } finally { if (sessionRef.current === session) setOpening(false); }
  }
  const message = error === 'KM-MODEL-GPU-UNAVAILABLE' ? 'modelViewer.gpuError' :
    error === 'KM-MODEL-BUSY' ? 'modelViewer.busyError' : 'modelViewer.loadError';
  usePublishCommonEditorDiagnostics(error ? [{
    code: error, domain: 'workflow.modelViewer', message: t(message), severity: 'error'
  }] : []);
  return <section className="panel wide-panel model-viewer" aria-labelledby="model-viewer-title">
    <header className="model-viewer__header">
      <ScanLine aria-hidden="true" size={22} />
      <div><h2 id="model-viewer-title">{t('modelViewer.title')}</h2><p>{t('modelViewer.description')}</p></div>
    </header>
    <p className="model-viewer__note">{t('modelViewer.scope')}</p>
    {!supported ? <p role="status">{t('modelViewer.unsupportedGame')}</p> :
      !isTauri() ? <p role="status">{t('modelViewer.desktopRequired')}</p> : <>
        <div className="model-viewer__toolbar">
          <span>{t('modelViewer.models', { count: catalog.length })}</span>
          <button type="button" onClick={() => setRevision(value => value + 1)} disabled={loading || opening}>{t('modelViewer.reload')}</button>
        </div>
        <label htmlFor="model-catalog">{t('modelViewer.search')}</label>
        <SearchableOptionInput id="model-catalog" ariaLabel={t('modelViewer.search')}
          disabled={false} isFiniteCatalog localizeOptions={false} maximumVisibleOptions={100}
          options={options} value={selected} onChange={setSelected} noOptionsLabel={t('modelViewer.empty')} />
        {!loading && catalog.length === 0 ? <p role="status">{t('modelViewer.empty')}</p> : null}
        <div className="model-viewer__actions">
          <button type="button" disabled={loading || opening || !entry} onClick={() => void open()}>
            {opening ? t('modelViewer.opening') : t('modelViewer.open')}
          </button>
          <span role="status" aria-live="polite">{loading ? t('modelViewer.loading') : adapter ? t('modelViewer.adapter', { adapter }) : ''}</span>
        </div>
      </>}
    {error ? <p className="model-viewer__error" role="alert">{t(message)} <code>{error}</code></p> : null}
    <p>{t('modelViewer.controls')}</p>
  </section>;
}
function errorCode(cause: unknown): string {
  if (cause instanceof ProjectBridgeError && cause.semanticCode) return cause.semanticCode;
  if (typeof cause === 'string' && /^KM(?:-[A-Z0-9]+)+$/.test(cause)) return cause;
  if (cause && typeof cause === 'object' && 'code' in cause && typeof cause.code === 'string' &&
      /^KM(?:-[A-Z0-9]+)+$/.test(cause.code)) return cause.code;
  return 'KM-MODEL-UNSUPPORTED';
}
