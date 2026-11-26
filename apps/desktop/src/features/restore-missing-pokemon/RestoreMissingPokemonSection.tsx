/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { Bird, CheckCircle2, RefreshCw, Save, Trash2, TriangleAlert, X } from 'lucide-react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import type { RestorationProjectBridgeApi } from '../../bridge/restorationProjectBridge';
import type { RestorationPreparation, RestorationSelection, RestorationWorkflow } from '../../bridge/restorationContracts';
import { ContextHelp } from '../../components/ContextHelp';
import { EditorRecordTable } from '../../components/EditorRecordTable';
import { EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
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
  onStage: (selections: RestorationSelection[], checkIntegrity?: boolean) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
  desktopServices?: DesktopServices;
};
const preparationByProject = new Map<string, RestorationPreparation>();
const preparedByProject = new Map<string, Record<string, { hash: string; removalId: string | null }>>();
type RestorationDraft = { enabled: boolean; updateAssets?: boolean; adoptExternal?: boolean };

export function RestoreMissingPokemonSection({ workflow, paths, bridge, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput, desktopServices = defaultDesktopServices }: Props) {
  const { t, translateLiteral } = useLocalization();
  const projectKey = JSON.stringify([paths.selectedGame, paths.baseRomFsPath, paths.outputRootPath]);
  const [search, setSearch] = useState('');
  const [selectedKey, setSelectedKey] = useState<string>();
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
  const entryName = (entry: RestorationWorkflow['entries'][number]) => {
    if (!entry.formLabel) return entry.name;
    const key = `restoration.form.${entry.formLabel}`;
    const translated = t(key, { name: entry.name });
    return translated === key ? `${entry.name} (${entry.formLabel})` : translated;
  };
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
  const canStage = canEdit && (dirty || workflow?.canRepairPokedex === true) && valid && !isStaging && removing === null;
  const stage = async () => {
    if (!canStage) return;
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
  const recordKey = (entry: RestorationWorkflow['entries'][number]) => `${entry.speciesId}:${entry.form}`;
  const term = search.trim().toLocaleLowerCase();
  const visibleEntries = entries.filter(entry => `${entry.speciesId} ${entryName(entry)}`.toLocaleLowerCase().includes(term));
  const selected = visibleEntries.find(entry => recordKey(entry) === selectedKey) ?? visibleEntries[0];
  const renderSelected = (entry: RestorationWorkflow['entries'][number]) => {
    const id = entry.definitionId;
    const label = entryName(entry);
    const enabled = id && id in drafts ? drafts[id].enabled : entry.selected;
    const external = entry.installation === 'External';
    return <div className="editor-task-groups">
      <div className="editor-detail-group">
        <h3>{t('restoration.availability')}</h3>
        <div className="restoration-record-status">
          <div>{external ? t('restoration.installation.External') : t(`restoration.support.${entry.support}`)}
            {external ? <small>{t(`restoration.support.${entry.support}`)}</small> : null}
            {entry.donor ? <small>{t(`restoration.source.${entry.donor}`)}</small>
            : entry.form > 0 ? <small>{t('restoration.sourceUnavailable')}</small> : null}
            {id && hashes.has(id) ? <small>{t('restoration.status.prepared')}</small> : null}
            {entry.removalStaged ? <small role="status">{t('restoration.removalStaged')}</small> : null}
            {id && (drafts[id]?.updateAssets || drafts[id]?.adoptExternal) ? <small role="status">{t('restoration.updatePrepared')}</small> : null}
            {!external ? <small>{t(`restoration.installation.${entry.installation}`)}{entry.staged ? ` · ${t('restoration.staged')}` : ''}</small> : null}
            {external && entry.staged ? <small>{t('restoration.staged')}</small> : null}</div>
          {entry.plannedPersonalId != null ?
            <p>{t('restoration.plannedRow', { form: entry.form, row: entry.plannedPersonalId })}</p> : null}
        </div>
      </div>
      <div className="editor-detail-group">
        <h3>{t('restoration.actions')}</h3>
        <div className="restoration-record-actions">
          <div className="editor-task-field-name">{external ? <span
          className={`restoration-installed-indicator ${entry.availableForAssignment ? 'is-complete' : 'needs-repair'}`}>
          {entry.availableForAssignment ? <CheckCircle2 size={20} aria-hidden="true" /> : <TriangleAlert size={20} aria-hidden="true" />}
          <strong>{t(entry.availableForAssignment ? 'restoration.alreadyInstalled' : 'restoration.installation.NeedsRepair')}</strong>
        </span> : <label className="editor-task-field-name"><input type="checkbox" aria-label={`${t('restoration.enabled')}: ${label}`} checked={enabled}
          disabled={!canEdit || removing === id || !id || !hashes.has(id) || entry.support === 'NotSupported' || entry.installation === 'External'}
          title={id && !hashes.has(id) ? t('restoration.prepare') : undefined}
          onChange={event => { if (!id) return; selectionVersions.current[id] = (selectionVersions.current[id] ?? 0) + 1; const checked = event.target.checked; setDrafts(current => {
            const next = { ...current, [id]: { enabled: checked } }; if (checked === entry.selected && !isStaging) delete next[id]; return next;
          }); }} />{t('restoration.enabled')}</label>}</div>
          <div className="restoration-asset-actions">
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
          </div>
        </div>
      </div>
    </div>;
  };
  return <section className="workflow-panel wide-panel restoration-panel" aria-labelledby="restoration-title">
    <header className="workflow-panel-header"><div><h2 id="restoration-title"><Bird size={22} aria-hidden="true" /> {t('restoration.title')}</h2>
      <p>{t('restoration.subtitle')}</p></div>
      <ContextHelp label={t('restoration.title')}>
        <p>{t('restoration.familyHelp')}</p>
        <p>{t('restoration.removeHelp')}</p>
        <p>{t('restoration.updateHelp')}</p>
        <p>{t('restoration.soundMemoryHelp')}</p>
        {workflow?.canRepairPokedex ? <p>{t('restoration.pokedexRepairHelp')}</p> : null}
      </ContextHelp><button className="secondary-button" type="button" onClick={onRefresh} disabled={isLoading}>
      <RefreshCw size={16} aria-hidden="true" /> {t('restoration.refresh')}</button></header>
    <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
      label={t('restoration.title')} onStart={onStartEditSession} readOnlyReason={t('restoration.readOnly')} />
    {isEditing ? <EditorSessionBarActions>
      <button className="primary-button" type="button" onClick={() => void stage()} disabled={!canStage}>
        <Save size={16} aria-hidden="true" /> {t('restoration.stage')}</button>
      <button className="secondary-button" type="button"
        disabled={!canEdit || isStaging || removing !== null}
        onClick={() => { setError(null); void onStage([], true); }}>
        {t('restoration.checkIntegrity')}</button>
      <button className="secondary-button" type="button" onClick={() => onCancelEditSession(() => { preparationIntents.current.clear(); setDrafts({}); })} disabled={isStaging}>
        <X size={16} aria-hidden="true" /> {t('restoration.cancel')}</button>
      <span>{t('restoration.drafts', { count: Object.keys(drafts).length, staged: pendingCount })}</span>
    </EditorSessionBarActions> : null}
    {workflow?.repairs.length ? <div className="restoration-integrity" aria-live="polite">
      <h3>{t('restoration.integrityRepairs')}</h3>
      <dl>{workflow.repairs.map(repair => <div key={repair.id}>
        <dt>{t('restoration.integrityRepair.' + repair.id)}</dt>
        <dd><span data-repair-state={repair.state}>{t('restoration.integrityState.' + repair.state)}</span>
          {repair.detail ? <details><summary>{t('restoration.integrityDetails')}</summary><p>{repair.detail}</p></details> : null}
        </dd>
      </div>)}</dl>
    </div> : null}
    {preparationVisible ? <RestorationPreparationProgress preparation={preparation}
      name={preparationEntry ? entryName(preparationEntry) : t('restoration.title')}
      onCancel={() => void cancelPreparation()} /> : null}
    <RecordEditorWorkspace id="restoreMissingPokemon" className="restoration-workspace" initialSection="catalog"
      browserControls={<div className="editor-record-context">
        <label className="restoration-search">{t('restoration.search')}
          <input type="search" value={search} onChange={event => setSearch(event.currentTarget.value)} />
        </label>
        <strong>{t('restoration.pokemon')} <span>{visibleEntries.length} / {entries.length}</span></strong>
      </div>}
      browser={<EditorRecordTable label={t('restoration.pokemon')} identityLabel={t('restoration.pokemon')}
        rows={visibleEntries} rowId={recordKey} selection={selected ? recordKey(selected) : undefined}
        onSelect={entry => setSelectedKey(recordKey(entry))}
        identity={entry => <><span className="editor-record-id">#{entry.speciesId}</span>{entryName(entry)}</>}
        columns={[
          { id: 'status', label: t('restoration.availability'), value: entry => t(`restoration.installation.${entry.installation}`) },
          { id: 'donor', label: translateLiteral('Source'), value: entry => entry.donor ? t(`restoration.source.${entry.donor}`) : '—' },
          { id: 'enabled', label: t('restoration.enabled'), value: entry => {
            const draft = entry.definitionId ? drafts[entry.definitionId] : undefined;
            return (draft?.enabled ?? entry.selected) ? '✓' : '—';
          } }
        ]} />}>
      {selected ? <EditorWorkspaceHeader afterBrowser><EditorRecordHeading name={entryName(selected)} id={selected.speciesId} /></EditorWorkspaceHeader> : null}
      <EditorWorkspaceSection id="catalog" label={t('restoration.pokemon')}
        modified={dirty}>{selected ? renderSelected(selected) : <p className="muted">{translateLiteral('No matching records.')}</p>}</EditorWorkspaceSection>
      <EditorWorkspaceSection id="sources" label={translateLiteral('Source Folders')}>
    <RestorationSourceFolders sources={sources} desktop={desktopServices} paths={paths} bridge={bridge} onChange={next => {
      setSources(next); if (!rememberRestorationSources(projectKey, next)) setError(t('restoration.sourcesSaveFailed'));
    }} />
      </EditorWorkspaceSection>
    </RecordEditorWorkspace>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </section>;
}
