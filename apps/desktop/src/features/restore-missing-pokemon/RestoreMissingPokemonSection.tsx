/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { Bird, CheckCircle2, RefreshCw, Save, Trash2, TriangleAlert, X } from 'lucide-react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import type { RestorationProjectBridgeApi } from '../../bridge/restorationProjectBridge';
import type { RestorationPreparation, RestorationSelection, RestorationWorkflow } from '../../bridge/restorationContracts';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './RestoreMissingPokemonSection.css';
import { readRestorationSources, rememberRestorationSources } from './restorationSourceSettings';
import { RestorationSourceFolders } from './RestorationSourceFolders';
import { RestorationPreparationProgress } from './RestorationPreparationProgress';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import { projectBridgeErrorCodes } from '../../errorCodes';
import { desktopServices as defaultDesktopServices, type DesktopServices } from '../../desktopServices';

type Props = {
  workflow: RestorationWorkflow | null; paths: ProjectPaths; bridge: RestorationProjectBridgeApi; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (selections: RestorationSelection[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
  desktopServices?: DesktopServices;
};
const preparationByProject = new Map<string, RestorationPreparation>();
const preparedByProject = new Map<string, Record<string, { hash: string; removalId: string | null }>>();
type RestorationDraft = { enabled: boolean; updateAssets?: boolean; adoptExternal?: boolean };

export function RestoreMissingPokemonSection({ workflow, paths, bridge, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput, desktopServices = defaultDesktopServices }: Props) {
  const { t } = useLocalization();
  const projectKey = JSON.stringify([paths.selectedGame, paths.baseRomFsPath, paths.outputRootPath]);
  const [search, setSearch] = useState('');
  const [sources, setSources] = useState(() => readRestorationSources(projectKey, paths.scarletVioletSupportFolderPath ?? ''));
  const [preparedHashes, setPreparedHashes] = useState(() => preparedByProject.get(projectKey) ?? {});
  const [drafts, setDrafts] = useState<Record<string, RestorationDraft>>({});
  const [preparation, setPreparation] = useState<RestorationPreparation | null>(() => preparationByProject.get(projectKey) ?? null);
  const [error, setError] = useState<string | null>(null);
  const [isPreparing, setIsPreparing] = useState(false);
  const [removing, setRemoving] = useState<string | null>(null);
  const [pollRetry, setPollRetry] = useState(0);
  const selectionVersions = useRef<Record<string, number>>({});
  const preparationIntents = useRef(new Map<string, { sessionId: string; version: number; draft: RestorationDraft }>());
  const active = useRef(true); const submittedDrafts = useRef(drafts); submittedDrafts.current = drafts;
  const entries = workflow?.entries ?? [];
  const entryName = (entry: RestorationWorkflow['entries'][number]) => entry.formLabel
    ? t(`restoration.form.${entry.formLabel}`, { name: entry.name }) : entry.name;
  const pendingCount = session?.pendingEdits.filter(e => e.domain === 'workflow.restoreMissingPokemon').length ?? 0;
  const dirty = Object.keys(drafts).length > 0;
  const canEdit = isEditing && session !== null && workflow?.canEdit === true;
  const editingSession = useRef<string | null>(null); editingSession.current = canEdit ? session!.sessionId : null;
  const hashes = new Map<string, string>();
  for (const entry of entries) if (entry.definitionId && entry.preparedHash) hashes.set(entry.definitionId, entry.preparedHash);
  for (const [id, value] of Object.entries(preparedHashes))
    if ((entries.find(e => e.definitionId === id)?.assetsRemoved ?? null) === value.removalId) hashes.set(id, value.hash);
  const preparationEntry = entries.find(e => e.definitionId === preparation?.definitionId);
  const preparationVisible = preparation && !preparationEntry?.removalStaged
    && (preparation.removalId ?? null) === (preparationEntry?.assetsRemoved ?? null);
  if (preparationVisible && preparation.preparedHash) hashes.set(preparation.definitionId, preparation.preparedHash);
  for (const entry of entries) if (entry.definitionId && entry.removalStaged) hashes.delete(entry.definitionId);
  const currentHashes = useRef(hashes); currentHashes.current = hashes;
  const valid = Object.entries(drafts).every(([id, draft]) => hashes.has(id) && entries.some(entry =>
    entry.definitionId === id && (draft.adoptExternal
      ? entry.installation === 'External' && entry.updateAvailable === true
      : entry.installation !== 'External')));
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => { active.current = true; return () => { active.current = false; onDirtyStateChange(false); }; }, [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.restoreMissingPokemon', field: 'draft', message: error });
  const remember = (value: RestorationPreparation) => {
    if (preparationByProject.size >= 4 && !preparationByProject.has(projectKey)) preparationByProject.delete(preparationByProject.keys().next().value!);
    preparationByProject.set(projectKey, value); setPreparation(value);
    if (value.status !== 'preparing') {
      setError(value.status === 'failed' ? value.diagnostics[0]?.message ?? t('restoration.failed') : null);
      const intent = preparationIntents.current.get(value.id); preparationIntents.current.delete(value.id);
      if (value.status === 'prepared' && value.preparedHash && intent && editingSession.current === intent.sessionId
        && (selectionVersions.current[value.definitionId] ?? 0) === intent.version)
        setDrafts(current => ({ ...current, [value.definitionId]: intent.draft }));
    }
    if (value.preparedHash) setPreparedHashes(current => {
      const next = { ...current, [value.definitionId]: { hash: value.preparedHash!, removalId: value.removalId ?? null } };
      if (preparedByProject.size >= 4 && !preparedByProject.has(projectKey)) preparedByProject.delete(preparedByProject.keys().next().value!);
      preparedByProject.set(projectKey, next); return next;
    });
  };
  useEffect(() => {
    if (preparation?.status !== 'preparing') return;
    let canceled = false;
    const timer = window.setTimeout(async () => {
      try {
        const response = await bridge.getRestorationPreparation({ paths, preparationId: preparation.id });
        if (!canceled) {
          remember(response.preparation);
          if (response.preparation.status === 'preparing') setError(null);
        }
      } catch (failure) {
        if (canceled) return;
        if (failure instanceof ProjectBridgeError && failure.semanticCode === projectBridgeErrorCodes.restorationSessionInvalid) {
          remember({ ...preparation, status: 'failed' }); setError(failure.message);
        } else { setError(t('restoration.progress.retrying')); setPollRetry(value => value + 1); }
      }
    }, 1000);
    return () => { canceled = true; window.clearTimeout(timer); };
    // Each response starts the next poll; project changes unmount this scoped editor.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preparation, projectKey, bridge, pollRetry]);
  const donorPath = (definitionId: string) => {
    const donor = entries.find(entry => entry.definitionId === definitionId)?.donor;
    return (donor === 'LegendsZA' ? sources.donorZa : donor === 'ScarletViolet' ? sources.donor
      : donor === 'LegendsArceus' ? sources.donorPla : '').trim();
  };
  const prepare = async (definitionId: string, updateAssets = false) => {
    if (!canEdit || !session || removing || isStaging || isPreparing || preparation?.status === 'preparing' || !donorPath(definitionId)) return;
    const entry = entries.find(value => value.definitionId === definitionId);
    const adoptExternal = entry?.installation === 'External';
    if (updateAssets && (!entry?.updateAvailable || (!adoptExternal && !entry.preparedHash)
      || entry.removalStaged || entry.staged || definitionId in drafts)) return;
    const intent = { sessionId: session.sessionId, version: selectionVersions.current[definitionId] ?? 0,
      draft: updateAssets && adoptExternal ? { enabled: true, adoptExternal: true }
        : updateAssets ? { enabled: entry!.selected, updateAssets: true } : { enabled: true } };
    setIsPreparing(true); setError(null);
    try {
      const response = await bridge.prepareRestoration({ paths, definitionId, donorRomFsPath: donorPath(definitionId), compressionSupportFolderPath: sources.support.trim() || null });
      if (active.current) {
        if (editingSession.current === intent.sessionId) preparationIntents.current.set(response.preparation.id, intent);
        remember(response.preparation);
      }
    } catch { if (active.current) setError(t('restoration.failed')); }
    finally { if (active.current) setIsPreparing(false); }
  };
  const stage = async () => {
    if (!canEdit || !dirty || !valid || isStaging || removing) return;
    const submitted = { ...submittedDrafts.current }; setError(null);
    const submittedHashes = new Map(hashes);
    try {
      if (!await onStage(Object.entries(submitted).map(([definitionId, draft]) => ({ definitionId, ...draft,
        preparedHash: (!draft.updateAssets && !draft.adoptExternal ? entries.find(entry => entry.definitionId === definitionId)?.preparedHash : null) ?? hashes.get(definitionId)! })))) {
        if (active.current) setError(t('restoration.failed')); return;
      }
      if (active.current) setDrafts(current => Object.fromEntries(Object.entries(current).filter(([id, value]) =>
        submitted[id] !== value || (value.enabled || value.updateAssets) && submittedHashes.get(id) !== currentHashes.current.get(id))));
    } catch { if (active.current) setError(t('restoration.failed')); }
  };
  const removeAssets = async (definitionId: string) => {
    const hash = entries.find(entry => entry.definitionId === definitionId)?.preparedHash ?? hashes.get(definitionId);
    if (!canEdit || !session || !hash || removing || isStaging || isPreparing || preparation?.status === 'preparing') return;
    const sessionId = session.sessionId;
    const version = (selectionVersions.current[definitionId] ?? 0) + 1;
    selectionVersions.current[definitionId] = version;
    setRemoving(definitionId); setError(null);
    try {
      const accepted = await onStage([{ definitionId, preparedHash: hash, enabled: false, removeAssets: true }]);
      if (!active.current || editingSession.current !== sessionId) return;
      if (!accepted) { setError(t('restoration.failed')); return; }
      if (selectionVersions.current[definitionId] === version) setDrafts(current => {
        const next = { ...current }; delete next[definitionId]; return next;
      });
    } catch { if (active.current) setError(t('restoration.failed')); }
    finally { if (active.current) setRemoving(null); }
  };
  const cancelPreparation = async () => {
    if (!preparation) return;
    try { const response = await bridge.cancelRestorationPreparation({ paths, preparationId: preparation.id }); if (active.current) remember(response.preparation); }
    catch { if (active.current) setError(t('restoration.failed')); }
  };
  return <section className="workflow-panel wide-panel restoration-panel" aria-labelledby="restoration-title">
    <header className="workflow-panel-header"><div><h2 id="restoration-title"><Bird size={22} aria-hidden="true" /> {t('restoration.title')}</h2>
      <p>{t('restoration.subtitle')}</p></div><button className="secondary-button" type="button" onClick={onRefresh} disabled={isLoading}>
      <RefreshCw size={16} aria-hidden="true" /> {t('restoration.refresh')}</button></header>
    <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
      label={t('restoration.title')} onStart={onStartEditSession} readOnlyReason={t('restoration.readOnly')} />
    {isEditing ? <EditorSessionBarActions>
      <button className="primary-button" type="button" onClick={() => void stage()} disabled={!canEdit || !dirty || !valid || isStaging || removing !== null}>
        <Save size={16} aria-hidden="true" /> {t('restoration.stage')}</button>
      <button className="secondary-button" type="button" onClick={() => onCancelEditSession(() => { preparationIntents.current.clear(); setDrafts({}); })} disabled={isStaging}>
        <X size={16} aria-hidden="true" /> {t('restoration.cancel')}</button>
      <span>{t('restoration.drafts', { count: Object.keys(drafts).length, staged: pendingCount })}</span>
    </EditorSessionBarActions> : null}
    <div className="restoration-guidance">
      <p>{t('restoration.familyHelp')}</p>
      <p>{t('restoration.removeHelp')}</p>
      <p>{t('restoration.updateHelp')}</p>
      <p>{t('restoration.soundMemoryHelp')}</p>
    </div>
    <RestorationSourceFolders sources={sources} desktop={desktopServices} paths={paths} bridge={bridge} onChange={next => {
      setSources(next); if (!rememberRestorationSources(projectKey, next)) setError(t('restoration.sourcesSaveFailed'));
    }} />
    {preparationVisible ? <RestorationPreparationProgress preparation={preparation}
      name={entries.find(entry => entry.definitionId === preparation.definitionId)?.name
        ?? entries.find(entry => entry.speciesId === preparation.species && entry.form === 0)?.name ?? t('restoration.title')}
      onCancel={() => void cancelPreparation()} /> : null}
    <label className="restoration-search">{t('restoration.search')}<input type="search" value={search} onChange={e => setSearch(e.target.value)} /></label>
    <div className="restoration-list" tabIndex={0} role="region" aria-label={t('restoration.pokemon')}><table><thead><tr><th scope="col">{t('restoration.enabled')}</th><th scope="col">{t('restoration.pokemon')}</th>
      <th scope="col">{t('restoration.availability')}</th><th scope="col">{t('restoration.actions')}</th></tr></thead><tbody>
      {entries.filter(e => `${e.speciesId} ${entryName(e)}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase())).map(entry => {
        const id = entry.definitionId;
        const label = entryName(entry);
        const enabled = id && id in drafts ? drafts[id].enabled : entry.selected;
        const external = entry.installation === 'External';
        return <tr key={`${entry.speciesId}:${entry.form}`}><td>{external ? <span
          className={`restoration-installed-indicator ${entry.availableForAssignment ? 'is-complete' : 'needs-repair'}`}>
          {entry.availableForAssignment ? <CheckCircle2 size={20} aria-hidden="true" /> : <TriangleAlert size={20} aria-hidden="true" />}
          <strong>{t(entry.availableForAssignment ? 'restoration.alreadyInstalled' : 'restoration.installation.NeedsRepair')}</strong>
        </span> : <input type="checkbox" aria-label={`${t('restoration.enabled')}: ${label}`} checked={enabled}
          disabled={!canEdit || removing === id || !id || !hashes.has(id) || entry.support === 'NotSupported' || entry.installation === 'External'}
          title={id && !hashes.has(id) ? t('restoration.prepare') : undefined}
          onChange={event => { if (!id) return; selectionVersions.current[id] = (selectionVersions.current[id] ?? 0) + 1; const checked = event.target.checked; setDrafts(current => {
            const next = { ...current, [id]: { enabled: checked } }; if (checked === entry.selected && !isStaging) delete next[id]; return next;
          }); }} />}</td><td data-localization-ignore="true"><strong>{label}</strong><small>#{entry.speciesId}</small>
            {entry.plannedPersonalId != null ? <small>{t('restoration.plannedRow', { form: entry.form, row: entry.plannedPersonalId })}</small> : null}</td>
          <td>{external ? t('restoration.installation.External') : t(`restoration.support.${entry.support}`)}
            {external ? <small>{t(`restoration.support.${entry.support}`)}</small> : null}
            {entry.donor ? <small>{t(`restoration.source.${entry.donor}`)}</small>
            : entry.form > 0 ? <small>{t('restoration.sourceUnavailable')}</small> : null}
            {id && hashes.has(id) ? <small>{t('restoration.status.prepared')}</small> : null}
            {entry.removalStaged ? <small role="status">{t('restoration.removalStaged')}</small> : null}
            {id && (drafts[id]?.updateAssets || drafts[id]?.adoptExternal) ? <small role="status">{t('restoration.updatePrepared')}</small> : null}
            {!external ? <small>{t(`restoration.installation.${entry.installation}`)}{entry.staged ? ` · ${t('restoration.staged')}` : ''}</small> : null}
            {external && entry.staged ? <small>{t('restoration.staged')}</small> : null}</td>
          <td><div className="restoration-asset-actions">
            {external ? <p className="restoration-external-help">
              {t(entry.updateAvailable ? 'restoration.externalUpdateHelp'
                : entry.availableForAssignment ? 'restoration.externalInstalledHelp' : 'restoration.externalRepairHelp')}
            </p> : id ? (hashes.has(id) || entry.removalStaged ? <button className="danger-button" type="button"
              disabled={!canEdit || entry.removalStaged || removing !== null || isStaging || isPreparing || preparation?.status === 'preparing'}
              aria-busy={removing === id} onClick={() => void removeAssets(id)}>
              <Trash2 size={16} aria-hidden="true" />{t('restoration.removeAssets')}</button>
              : <button className="secondary-button" type="button" disabled={!canEdit || !donorPath(id) || removing !== null || isStaging || isPreparing || preparation?.status === 'preparing'}
                onClick={() => void prepare(id)}>{t('restoration.prepare')}</button>) : null}
            {id && entry.updateAvailable && (external || entry.preparedHash) && !entry.removalStaged ? <button className="secondary-button" type="button"
              disabled={!canEdit || !donorPath(id) || entry.staged || id in drafts || removing !== null || isStaging || isPreparing || preparation?.status === 'preparing'}
              onClick={() => void prepare(id, true)}><RefreshCw size={16} aria-hidden="true" />{t('restoration.updateAsset')}</button> : null}
            {id && entry.installation === 'NeedsRepair' && !entry.removalStaged ? <button className="secondary-button" type="button" disabled={!canEdit || isStaging}
              onClick={() => setDrafts(current => ({ ...current, [id]: { enabled: true } }))}>{t('restoration.repair')}</button> : null}
          </div></td></tr>;
      })}</tbody></table></div>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </section>;
}
