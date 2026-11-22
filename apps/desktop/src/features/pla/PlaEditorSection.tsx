/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { plaAreaQuery } from './plaLabels';
import { RefreshCw, RotateCcw, Save, X } from 'lucide-react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import type { PlaField, PlaKind, PlaPage, PlaRecord, StagePlaRequest } from '../../bridge/plaProjectBridge';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { getWorkbenchCapabilityRegistration } from '../../workbench/capabilityRegistry';
import { workbenchSectionLocalization } from '../../workbench/sectionLocalization';
import { PlaBattleWorkspace, type PlaPokemonPreview } from './PlaBattleWorkspace';
import { PlaDataWorkspace } from './PlaDataWorkspace';
import { PlaEconomyWorkspace, isPlaEconomyKind } from './PlaEconomyWorkspace';
import { PlaWildWorkspace, isPlaWildKind } from './PlaWildWorkspace';
import { PlaRulesWorkspace } from './PlaRulesWorkspace';
import { PlaTypeChartWorkspace } from './PlaTypeChartWorkspace';
import { PlaPokemonEditor } from './PlaPokemonEditor';
import { isPlaFieldValueValid, resolvePlaField, updatePlaDependentValues } from './plaDraftValidation';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import { plaLocationLoadOptions, type PlaNavigationTarget } from '../../workbench/plaLocations';
import { usePlaRowClipboard } from './usePlaRowClipboard';
import './plaEditor.css';

export type PlaDrafts = Record<string, { revision: string; values: Record<string, string>; baseline: Record<string, string> }>;
export type PlaEditorProps = {
  section: 'pokemon' | 'moves' | 'text' | 'trainers' | 'giftPokemon' | 'staticEncounters' | 'items' | 'crafting' | 'shops' | 'pokemonDrops' | 'research' | 'encounters' | 'outbreaks' | 'distortions' | 'weather' | 'gameplayRules' | 'typeChart'; paths: ProjectPaths; session: EditSession | null; bridge: ProjectBridge;
  drafts: PlaDrafts; setDrafts: Dispatch<SetStateAction<PlaDrafts>>;
  isStarting: boolean; isBusy: boolean; onStart: () => void;
  onStage: (request: Omit<StagePlaRequest, 'paths' | 'session'>) => Promise<boolean>;
  onCancel: (discard: () => void) => void; onDirtyChange: (dirty: boolean) => void;
  renderPokemon?: PlaPokemonPreview;
  beginLoad?: () => () => void; cacheRevision?: number;
  navigationTarget?: PlaNavigationTarget | null;
  onNavigateRecord?: (kind: PlaKind, id: string, label: string, commit: () => void) => void;
};

export function PlaEditorSection(props: PlaEditorProps) {
  return <FocusedEditorWorkspace>{props.section === 'pokemon' ? <PlaPokemonEditor {...props} /> : <PlaRecordEditor {...props} section={props.section} />}</FocusedEditorWorkspace>;
}

function PlaRecordEditor({ section, paths, session, bridge, drafts, setDrafts, isStarting, isBusy,
  onStart, onStage, onCancel, onDirtyChange, renderPokemon, beginLoad, cacheRevision, navigationTarget, onNavigateRecord }: PlaEditorProps & { section: Exclude<PlaEditorProps['section'], 'pokemon'> }) {
  const { t, translateLiteral } = useLocalization();
  const [economyTab, setEconomyTab] = useState<PlaKind | null>(null);
  const kind = section === 'giftPokemon' ? 'gifts' : section === 'staticEncounters' ? 'static'
    : section === 'gameplayRules' ? economyTab ?? 'capture'
    : section === 'distortions' ? economyTab ?? 'distortionEncounters' : section === 'outbreaks' ? economyTab ?? 'outbreaks'
    : section === 'pokemonDrops' ? 'drops' : section === 'shops' || section === 'research' || section === 'encounters' ? economyTab ?? section : section;
  const [page, setPage] = useState<PlaPage | null>(null);
  const [loadedScope, setLoadedScope] = useState<string | null>(null);
  const publishedSelection = useRef<string | null>(null);
  const [query, setQuery] = useCoalescedTextInputState();
  const [offset, setOffset] = useState(0);
  const [textFile, setTextFile] = useState<string | null>(null);
  const [messageLanguage, setMessageLanguage] = useState(() => ({ es: 'Spanish', fr: 'French', de: 'German', zh: 'Simp_Chinese' })[paths.gameTextLanguage as 'es' | 'fr' | 'de' | 'zh'] ?? 'English');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [staging, setStaging] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    if (!navigationTarget || navigationTarget.section !== section) return;
    const options = plaLocationLoadOptions(navigationTarget);
    setEconomyTab(navigationTarget.kind); setSelectedId(navigationTarget.id); setOffset(0); setQuery(navigationTarget.id);
    if (options.messageLanguage !== undefined) { setMessageLanguage(options.messageLanguage); setTextFile(options.textFile); }
  }, [navigationTarget, section]);
  const epoch = useRef(0);
  const mounted = useRef(true);
  const stageLock = useRef(false);
  const scope = JSON.stringify(paths);
  const scopeRef = useRef(scope);
  scopeRef.current = scope;
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const searchQuery = ['encounters', 'spawns', 'outbreaks', 'massiveOutbreaks', 'outbreakEncounters', 'distortionEncounters', 'distortionSpawns', 'distortionItems'].includes(kind)
    ? plaAreaQuery(t, query) : query;
  const requestKey = JSON.stringify({ paths, session: session?.pendingEdits.length ? session : null, kind, query: searchQuery, offset, textFile, messageLanguage: kind === 'text' ? messageLanguage : null, refresh, cacheRevision });
  const dirty = Object.keys(drafts).some(key => key.startsWith(section + '/'));
  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => {
    if (staging) return;
    const run = ++epoch.current;
    const finishLoad = beginLoad?.();
    let started = false;
    setLoading(true);
    const request = JSON.parse(requestKey) as { paths: ProjectPaths; session: EditSession | null; kind: PlaKind; query: string; offset: number; textFile: string | null; messageLanguage: string | null };
    const timer = window.setTimeout(() => {
      started = true;
      void bridge.loadPla({ paths: request.paths, session: request.session, kind: request.kind, query: request.kind === 'weather' ? '' : request.query, offset: request.offset, textFile: request.textFile, messageLanguage: request.messageLanguage }).then(response => {
        if (epoch.current !== run) return;
        setPage(response.page);
        setLoadedScope(JSON.stringify(request.paths));
        setSelectedId(current => response.page.records.some(record => record.id === current) ? current : response.page.records[0]?.id ?? null);
      }).catch(reason => { if (epoch.current === run) { setPage(null); setError(String(reason)); } })
        .finally(() => { finishLoad?.(); if (epoch.current === run) setLoading(false); });
    }, 180);
    return () => { window.clearTimeout(timer); if (!started) finishLoad?.(); epoch.current++; };
  }, [bridge, requestKey, beginLoad, staging]);

  const weatherRecords = page?.kind === 'weather' ? page.records.filter(record => !query || record.id.toLocaleLowerCase().includes(query.toLocaleLowerCase())
    || t('pla.area.' + record.id).toLocaleLowerCase().includes(query.toLocaleLowerCase())) : null;
  const visiblePage = loadedScope === scope && page?.kind === kind ? weatherRecords ? { ...page, records: weatherRecords, total: weatherRecords.length } : page : null;
  const selected = visiblePage?.records.find(record => record.id === selectedId) ?? visiblePage?.records[0] ?? null;
  const selectRecord = (id: string) => {
    const commit = () => { publishedSelection.current = `${scope}/${kind}/${id}`; setSelectedId(id); };
    if (onNavigateRecord) onNavigateRecord(kind, id, visiblePage?.records.find(record => record.id === id)?.label ?? id, commit);
    else commit();
  };
  useEffect(() => {
    if (!selected || loading || isBusy || !onNavigateRecord) return;
    const key = `${scope}/${kind}/${selected.id}`;
    if (publishedSelection.current === key) return;
    publishedSelection.current = key;
    onNavigateRecord(kind, selected.id, selected.label, () => undefined);
  }, [kind, selected, loading, isBusy, scope, onNavigateRecord]);
  const recordDraftKey = (record: PlaRecord) => `${section}/${kind}/${paths.gameTextLanguage ?? 'English'}/${record.id}`;
  const draftKey = selected ? recordDraftKey(selected) : '';
  const draft = drafts[draftKey];
  const values = { ...selected?.values, ...draft?.values };
  const enabled = loadedScope === scope && session !== null && selected !== null && page?.kind === kind
    && (kind !== 'text' || selected.id.startsWith(`${messageLanguage}/${textFile ?? page.textFile}:`));
  const fields = page?.fields ?? [];
  const stageRecords = !selected ? [] : kind === 'shops'
    ? visiblePage?.records.filter(record => record.metadata.inventory === selected.metadata.inventory) ?? []
    : kind === 'trainers' ? visiblePage?.records.filter(record => record.id.slice(0, record.id.lastIndexOf(':')) === selected.id.slice(0, selected.id.lastIndexOf(':'))) ?? []
    : ['encounters', 'outbreakEncounters', 'distortionEncounters'].includes(kind) ? visiblePage?.records.filter(record => record.metadata.area === selected.metadata.area && record.metadata.table === selected.metadata.table) ?? []
    : [selected];
  const stageDrafts = stageRecords.flatMap(record => drafts[recordDraftKey(record)] ? [drafts[recordDraftKey(record)]] : []);
  const invalidFields = fields.filter(field => stageDrafts.some(item => item.values[field.key] !== undefined && !isPlaFieldValueValid(resolvePlaField(field, { ...item.baseline, ...item.values }), item.values[field.key])));
  usePublishCommonEditorError({ domain: `workflow.${section}`, field: 'pla', message: error ?? (invalidFields.length ? t('pla.invalid') : null) });
  const draftPrefix = `${section}/${kind}/${paths.gameTextLanguage ?? 'English'}/`;
  const outstandingKeys = Object.keys(drafts).filter(key => key.startsWith(draftPrefix));
  const nextDraftKey = outstandingKeys.find(key => key !== draftKey);
  const hasEditableFields = stageRecords.some(record => fields.some(field => !field.readOnly && !record.readOnlyFields.includes(field.key)));
  const label = (field: PlaField) => field.key.startsWith('tutor.') ? field.label
    : field.key.startsWith('bagPrice.') ? t('pla.rules.expansion', { number: field.key.split('.')[1] })
    : /^(warding|survival|tempting)Price\./u.test(field.key) ? t('pla.rules.ownedCharms', { count: Number(field.key.split('.')[1]) - 1 })
    : t(`pla.field.${field.key}`);
  const setRecordValue = (record: PlaRecord, key: string, value: string) => {
    if (!page || !enabled) return;
    const draftKey = recordDraftKey(record);
    const revision = page.revision;
    setDrafts(current => {
      const updated = updatePlaDependentValues(fields, { ...record.values, ...current[draftKey]?.values }, key, value);
      const nextValues = Object.fromEntries(Object.entries(updated).filter(([field, value]) => value !== record.values[field]));
      const next = { ...current };
      if (Object.keys(nextValues).length === 0) delete next[draftKey];
      else next[draftKey] = { revision: current[draftKey]?.revision ?? revision, values: nextValues, baseline: current[draftKey]?.baseline ?? { ...record.values } };
      return next;
    });
    setError(null);
  };
  const setValue = (key: string, value: string) => { if (selected) setRecordValue(selected, key, value); };
  const clipboard = usePlaRowClipboard({ source: { paths, session, bridge }, kind: kind === 'trainers' ? 'trainers' : 'encounters',
    identity: kind, enabled: enabled && ['trainers', 'encounters', 'outbreakEncounters', 'distortionEncounters'].includes(kind), busy: loading || staging || isBusy,
    getRow: id => {
      const record = visiblePage?.records.find(record => record.id === id);
      return record ? { label: record.label, fields: fields.filter(field => !field.readOnly && !record.readOnlyFields.includes(field.key)),
        values: { ...record.values, ...drafts[recordDraftKey(record)]?.values } } : null;
    }, onPaste: (id, values) => {
      const record = visiblePage?.records.find(record => record.id === id);
      if (!record) return false;
      for (const [key, value] of Object.entries(values)) setRecordValue(record, key, value);
      return true;
    } });
  const stage = async (restoreBase = false) => {
    if (!page || !selected || !enabled || !hasEditableFields || loading || stageLock.current || isBusy || invalidFields.length > 0 && !restoreBase || !restoreBase && !stageDrafts.length) return;
    const submissions = stageRecords.flatMap(record => {
      const key = recordDraftKey(record), draft = drafts[key];
      if (!restoreBase && !draft) return [];
      if (restoreBase && !fields.some(field => !field.readOnly && !record.readOnlyFields.includes(field.key))) return [];
      return [{ record, key, draft }];
    });
    stageLock.current = true; setStaging(true); setError(null);
    try {
      for (const { record, key, draft } of submissions) {
        if (!mounted.current || scopeRef.current !== scope) return;
        const submitted = draft;
        const requested = restoreBase
          ? Object.fromEntries(fields.filter(field => !field.readOnly && !record.readOnlyFields.includes(field.key)).map(field => [field.key, record.values[field.key]]))
          : { ...draft.values };
        if (!restoreBase) {
          for (const field of fields.filter(field => Object.hasOwn(requested, field.key))) {
            if (field.kind === 'rows') {
              const rows = parseRows(requested[field.key]);
              if (rows.some(row => row.some(value => !/^\d+$/u.test(value)))) { setError(t('pla.invalid')); return; }
              requested[field.key] = JSON.stringify(rows.map(row => row.map(Number)));
            } else if (field.kind === 'decimal' || field.kind === 'factor' ? !/^-?(?:\d+(?:\.\d*)?|\.\d+)$/u.test(requested[field.key]) || !Number.isFinite(Number(requested[field.key]))
              : field.kind !== 'text' && !/^-?\d+$/u.test(requested[field.key])) { setError(t(field.kind === 'factor' ? 'pla.rules.invalid' : field.kind === 'decimal' ? 'pla.wild.invalid' : 'pla.invalid')); return; }
          }
        }
        const baselineIsCurrent = draft && Object.keys(draft.values).every(field => draft.baseline[field] === record.values[field]);
        const accepted = await onStage({ kind, id: record.id, values: requested, revision: restoreBase || baselineIsCurrent ? page.revision : draft.revision, restoreBase });
        if (!mounted.current || scopeRef.current !== scope) return;
        if (!accepted) { setError(t('pla.failed')); return; }
        setDrafts(current => {
          if (current[key] !== submitted) {
            const newer = current[key];
            return newer && !restoreBase ? { ...current, [key]: { ...newer, baseline: { ...newer.baseline, ...requested } } } : current;
          }
          const next = { ...current }; delete next[key]; return next;
        });
      }
    } catch (reason) { if (mounted.current && scopeRef.current === scope) setError(String(reason)); }
    finally { stageLock.current = false; if (mounted.current) { setLoading(true); setStaging(false); setRefresh(value => value + 1); } }
  };
  const rows = useMemo(() => parseRows(values.entries ?? '[]'), [values.entries]);
  const changeRows = (next: string[][]) => setValue('entries', JSON.stringify(next.map(row => row.map(value => /^\d+$/u.test(value) ? Number(value) : value))));
  const input = (unresolvedField: PlaField, value: string, change: (value: string) => void, disabled: boolean, id: string, record = selected) => {
    const field = resolvePlaField(unresolvedField, values);
    const preservedLabel = record?.values[field.key] === value ? field.key === 'species' ? record.metadata?.speciesName
      : field.key === 'form' ? record.metadata?.formName || (value === '0' ? translateLiteral('Default') : undefined)
      : field.key === 'item' && kind === 'shops' ? record.label : undefined : undefined;
    return     field.kind === 'boolean' ? <input id={id} type="checkbox" aria-label={label(field)} checked={value === '1'} disabled={disabled} onChange={event => change(event.target.checked ? '1' : '0')} />
      : field.choices ? <SearchableOptionInput id={id} ariaLabel={label(field)} value={value} disabled={disabled} onChange={change}
          options={(field.choices.some(choice => choice.value === value) ? field.choices : [{ value, label: preservedLabel ? `${preservedLabel} (${value})` : value }, ...field.choices])
            .map(choice => ['method', 'category', 'growthRate', 'gender'].includes(field.key) && field.choices?.some(item => item.value === choice.value)
              ? { ...choice, label: t(`pla.choice.${field.key}.${choice.value}`) }
              : /^move[1-4]$/u.test(field.key) && choice.value === '0' ? { ...choice, label: t('pla.unspecifiedMove') } : choice)} isFiniteCatalog localizeOptions={false} />
        : field.kind === 'text' ? <textarea id={id} value={value} disabled={disabled} rows={8} onChange={event => change(event.target.value)} />
        : <input id={id} type="text" aria-invalid={!isPlaFieldValueValid(field, value) || undefined} inputMode={field.kind === 'decimal' || field.kind === 'factor' ? 'decimal' : 'numeric'} value={value} disabled={disabled} onChange={event => change(event.target.value)} />;
  };
  const actions = session ? <EditorSessionBarActions>
        <button type="button" className="primary-button" aria-busy={staging || undefined} disabled={!enabled || !stageDrafts.length || invalidFields.length > 0 || loading || staging || isBusy} onClick={() => void stage()}>
          {staging ? <RefreshCw className="button-busy-icon" size={16} aria-hidden="true" /> : <Save size={16} aria-hidden="true" />}{translateLiteral(staging ? 'Staging' : 'Stage')}
        </button>
        {nextDraftKey ? <button type="button" className="secondary-button" disabled={loading || staging || isBusy} onClick={() => {
          const id = nextDraftKey.slice(draftPrefix.length); selectRecord(id);
          if (!page?.records.some(record => record.id === id)) { setQuery(id); setOffset(0); }
        }}>{t('editorDrafts.reviewNext')}</button> : null}
        <button type="button" className="secondary-button" disabled={!enabled || !hasEditableFields || loading || staging || isBusy} onClick={() => void stage(true)}>
          <RotateCcw size={16} aria-hidden="true" />{t('pla.restore')}
        </button>
        <button type="button" className="secondary-button" disabled={!stageDrafts.length || staging} onClick={() => setDrafts(current => { const next = { ...current }; for (const record of stageRecords) delete next[recordDraftKey(record)]; return next; })}>
          <X size={16} aria-hidden="true" />{t('pla.discard')}
        </button>
        <button type="button" className="danger-button" disabled={staging || isBusy} onClick={() => onCancel(() => setDrafts({}))}>
          <X size={16} aria-hidden="true" />{translateLiteral('Cancel')}
        </button>
        <span className="draft-action-summary">{t('pla.draftSummary', { fields: stageDrafts.reduce((sum, item) => sum + Object.keys(item.values).length, 0), records: outstandingKeys.length })}</span>
      </EditorSessionBarActions> : null;
  const refreshButton = <button type="button" className="secondary-button compact-button" disabled={loading || staging} onClick={() => setRefresh(value => value + 1)}>
    <RefreshCw size={16} aria-hidden="true" />{translateLiteral('Refresh')}
  </button>;
  if (kind === 'typeChart') return <><PlaTypeChartWorkspace page={visiblePage} rows={rows} enabled={enabled}
    onChange={changeRows} actions={actions} refresh={refreshButton} loading={loading}
    sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting}
      label={translateLiteral('Type Chart')} onStart={onStart} />} /></>;
  if (kind === 'trainers' || kind === 'gifts' || kind === 'static') {
    return <><PlaBattleWorkspace sessionKey={scope} kind={kind} page={page} selected={selected} query={query} offset={offset} loading={loading} clipboard={kind === 'trainers' ? clipboard : undefined}
      onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={selectRecord} refresh={refreshButton}
      sessionBar={<EditorSessionBar canEdit={page !== null && !loading} isEditing={session !== null} isStarting={isStarting} label={translateLiteral(getWorkbenchCapabilityRegistration(section).label)} onStart={onStart} />}
      actions={actions} fieldLabel={label} renderPokemon={renderPokemon}
      valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })}
      hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
      pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && edit.recordId?.startsWith(kind + '/')).map(edit => edit.recordId!.slice(kind.length + 1)) ?? [])}
      renderInput={unresolvedField => {
        const field = resolvePlaField(unresolvedField, values);
        const disabled = !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key));
        return field.kind === 'number' && !field.choices ? <input id={`pla-field-${field.key}`} type="number" min={field.minimum ?? undefined} max={field.maximum ?? undefined}
          value={values[field.key] ?? ''} disabled={disabled} onChange={event => setValue(field.key, event.target.value)} />
          : input(field.kind === 'boolean' ? { ...field, kind: 'number', choices: [{ value: '0', label: translateLiteral('No') }, { value: '1', label: translateLiteral('Yes') }] } : field,
            values[field.key] ?? '', value => setValue(field.key, value), disabled, `pla-field-${field.key}`);
      }} /></>;
  }
  const eventTabs: PlaKind[] = section === 'outbreaks' ? ['outbreaks', 'massiveOutbreaks', 'outbreakGroups', 'outbreakWaves', 'outbreakDurations', 'outbreakEncounters']
    : section === 'distortions' ? ['distortionEncounters', 'distortionSpawns', 'distortionItems'] : section === 'gameplayRules' ? ['capture', 'friendship', 'shiny', 'bagPrices', 'charmPrices'] : [];
  const eventControls = eventTabs.length ? <>
    <nav className="text-category-browser" aria-label={t('pla.editor')}><div className="text-category-tabs" role="tablist" aria-label={t('pla.editor')}>
      {eventTabs.map((value, index) => <button type="button" key={value} role="tab" aria-selected={kind === value} tabIndex={kind === value ? 0 : -1}
        className={kind === value ? 'is-active' : ''} onClick={() => { setEconomyTab(value); setOffset(0); setQuery(''); }}
        onKeyDown={event => {
          const next = event.key === 'ArrowRight' ? (index + 1) % eventTabs.length : event.key === 'ArrowLeft' ? (index + eventTabs.length - 1) % eventTabs.length
            : event.key === 'Home' ? 0 : event.key === 'End' ? eventTabs.length - 1 : null;
          if (next === null) return;
          event.preventDefault(); setEconomyTab(eventTabs[next]); setOffset(0); setQuery('');
          (event.currentTarget.parentElement?.children[next] as HTMLButtonElement | undefined)?.focus();
        }}>{t('pla.kind.' + value)}</button>)}
    </div></nav>{section !== 'gameplayRules' ? <p className="field-note">{t(section === 'outbreaks' ? 'pla.event.outbreakNotice' : 'pla.event.distortionNotice')}</p> : null}
  </> : undefined;
  if (kind === 'capture' || kind === 'friendship' || kind === 'shiny' || kind === 'bagPrices' || kind === 'charmPrices') return <>
    <PlaRulesWorkspace sessionKey={scope} kind={kind} page={visiblePage} controls={eventControls} loading={loading} actions={actions} refresh={refreshButton} fieldLabel={label}
      sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting}
        label={t('workbench.section.gameplayRules.label')} onStart={onStart} />}
      renderInput={field => input(field, values[field.key] ?? '', value => setValue(field.key, value), !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key)), 'pla-field-' + field.key)} />
  </>;
  if (isPlaWildKind(kind)) return <><PlaWildWorkspace sessionKey={scope} kind={kind} page={visiblePage} selected={visiblePage ? selected : null} clipboard={clipboard}
    controls={eventControls} title={t(workbenchSectionLocalization[section].label)}
    query={query} offset={offset} loading={loading} refresh={refreshButton} actions={actions} fieldLabel={label} renderPokemon={renderPokemon}
    onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={selectRecord}
    onKind={value => { setEconomyTab(value); setOffset(0); setQuery(''); }}
    sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting}
      label={t(workbenchSectionLocalization[section].label)} onStart={onStart} />}
    valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })} hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
    pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && (kind === 'spawns' || kind === 'distortionSpawns'
      ? edit.recordId?.startsWith(kind + '/') : ['encounters/', 'outbreakEncounters/', 'distortionEncounters/'].some(prefix => edit.recordId?.startsWith(prefix))))
      .map(edit => edit.recordId!.slice(edit.recordId!.indexOf('/') + 1)) ?? [])}
    renderInput={field => input(field, values[field.key] ?? '', value => setValue(field.key, value),
      !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key)), 'pla-field-' + field.key)} /></>;
  if (isPlaEconomyKind(kind)) {
    const tabs: PlaKind[] = section === 'shops' ? ['shops', 'ginkgo'] : section === 'research' ? ['research', 'ranks'] : [];
    return <><PlaEconomyWorkspace sessionKey={scope} kind={kind} page={visiblePage} selected={visiblePage ? selected : null}
      query={query} offset={offset} loading={loading} onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={selectRecord}
      refresh={refreshButton} actions={actions} fieldLabel={label}
      renderRecordInput={(record, field) => input(field, { ...record.values, ...drafts[recordDraftKey(record)]?.values }[field.key] ?? '',
        value => setRecordValue(record, field.key, value), !enabled || field.readOnly || record.readOnlyFields.includes(field.key), `pla-shop-${record.id}-${field.key}`, record)}
      sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting}
        label={t(workbenchSectionLocalization[section].label)} onStart={onStart} />}
      valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })} hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
      pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && edit.recordId?.startsWith(kind + '/')).map(edit => edit.recordId!.slice(kind.length + 1)) ?? [])}
      renderInput={field => input(field, values[field.key] ?? '', value => setValue(field.key, value), !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key)), `pla-field-${field.key}`)}
      controls={eventControls ?? (tabs.length ? <nav className="text-category-browser" aria-label={t('pla.editor')}><div className="text-category-tabs" role="tablist" aria-label={t('pla.editor')}>
        {tabs.map(value => <button type="button" role="tab" aria-selected={kind === value} tabIndex={kind === value ? 0 : -1}
          className={kind === value ? 'is-active' : ''} key={value} onClick={() => { setEconomyTab(value); setOffset(0); }}
          onKeyDown={event => {
            const index = tabs.indexOf(value);
            const next = event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length
              : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : null;
            if (next === null) return;
            event.preventDefault(); setEconomyTab(tabs[next]); setOffset(0);
            (event.currentTarget.parentElement?.children[next] as HTMLButtonElement | undefined)?.focus();
          }}>{t(`pla.kind.${value}`)}</button>)}
      </div></nav> : null)} /></>;
  }
  return <><PlaDataWorkspace sessionKey={scope} kind={kind} page={visiblePage} selected={visiblePage ? selected : null} query={query} offset={offset} loading={loading}
    onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={selectRecord} refresh={refreshButton}
    sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting} label={translateLiteral(getWorkbenchCapabilityRegistration(section).label)} onStart={onStart} />}
    actions={actions} fieldLabel={label} renderPokemon={renderPokemon}
    valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })} hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
    pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && edit.recordId?.startsWith(kind + '/')).map(edit => edit.recordId!.slice(kind.length + 1)) ?? [])}
    renderInput={field => input(field.kind === 'boolean'
      ? { ...field, kind: 'number', choices: [{ value: '0', label: translateLiteral('No') }, { value: '1', label: translateLiteral('Yes') }] } : field,
      values[field.key] ?? '', value => setValue(field.key, value), !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key)), `pla-field-${field.key}`)}
    rowEditor={null}
    controls={<>
      {kind === 'text' ? <div className="pla-controls text-toolbar">
        {kind === 'text' ? <div className="pla-control"><label htmlFor="pla-language">{t('pla.language')}</label><SearchableOptionInput id="pla-language" ariaLabel={t('pla.language')} disabled={false} value={messageLanguage}
          onChange={value => { setMessageLanguage(value); setTextFile(null); setOffset(0); }}
          options={['English', 'French', 'German', 'Italian', 'JPN', 'JPN_KANJI', 'Korean', 'Simp_Chinese', 'Spanish', 'Trad_Chinese'].map(value => ({ value, label: t(`pla.language.${value}`) }))} isFiniteCatalog localizeOptions={false} /></div> : null}
        {kind === 'text' ? <div className="pla-control"><label htmlFor="pla-message-file">{t('pla.messageFile')}</label><SearchableOptionInput id="pla-message-file" ariaLabel={t('pla.messageFile')} disabled={false} value={textFile ?? page?.textFile ?? ''}
          onChange={value => { setTextFile(value); setOffset(0); }} options={(page?.textFiles ?? []).map(value => ({ value, label: value }))} isFiniteCatalog localizeOptions={false} /></div> : null}
      </div> : null}
    </>} /></>;
}

function parseRows(value: string): string[][] {
  try {
    const rows: unknown = JSON.parse(value);
    return Array.isArray(rows) && rows.every(row => Array.isArray(row)) ? rows.map(row => row.map(String)) : [];
  } catch { return []; }
}
