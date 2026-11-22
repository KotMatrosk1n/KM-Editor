/* SPDX-License-Identifier: GPL-3.0-only */
import { RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { useEffect, useMemo, useRef, useState } from 'react';
import { RefreshCw, RotateCcw, Save, TreeDeciduous, X } from 'lucide-react';
import { type EditSession } from '../../bridge/contracts';
import { treeFieldKey, treeValues, type ShakingTreesWorkflow, type TreeUpdate } from '../../bridge/shakingTreesContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import { parseTreeNumber, readTreeSettings, treeShakeOdds, validateTreeDraft } from './treeDraft';
import './ShakingTreesSection.css';

type Props = {
  workflow: ShakingTreesWorkflow | null; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (updates: TreeUpdate[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};
type Tab = 'drops' | 'encounters' | 'rules';

export function ShakingTreesSection({ workflow, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t, translateLiteral, formatLocale } = useLocalization();
  const [tab, setTab] = useState<Tab>('drops');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useCoalescedTextInputState('');
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const draftRef = useRef(drafts); draftRef.current = drafts;
  const active = useRef(true);
  const [failed, setFailed] = useState(false);
  const clean = useMemo(() => treeValues(workflow, session), [workflow, session]);
  useEffect(() => {
    setDrafts(current => {
      const next = Object.fromEntries(Object.entries(current).filter(([key, value]) => value !== clean.values[key]));
      return Object.keys(next).length === Object.keys(current).length ? current : next;
    });
  }, [clean.values]);
  const values = { ...clean.values, ...drafts };
  const dirtyKeys = Object.keys(drafts).filter(key => drafts[key] !== clean.values[key]);
  const dirty = dirtyKeys.length > 0;
  const valid = validateTreeDraft(workflow, values) && !clean.invalidPending && dirtyKeys.every(key => key in clean.values);
  const canEdit = workflow?.canEdit === true && isEditing;
  const settings = readTreeSettings(values);
  const odds = useMemo(() => settings ? treeShakeOdds(settings) : [],
    // Only the seven numeric settings affect the odds.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [JSON.stringify(settings)]);
  const selected = workflow?.trees.find(row => row.id === selectedId) ?? workflow?.trees[0];
  const area = workflow?.areas.find(row => row.id === selected?.areaId);
  const numbered = new Map<string, number>();
  const areaCounts = new Map<string, number>();
  for (const tree of workflow?.trees ?? []) {
    const number = (areaCounts.get(tree.areaId) ?? 0) + 1;
    areaCounts.set(tree.areaId, number); numbered.set(tree.id, number);
  }
  const treeName = (tree: NonNullable<typeof selected>) => `${translateLiteral(tree.area)} · ${t('shakingTrees.treeNumber', { number: numbered.get(tree.id) ?? 1 })}`;
  const get = (id: string, field: string) => values[treeFieldKey(id, field)] ?? '';
  const itemNames = useMemo(() => new Map(workflow?.items.map(item => [item.value, item.label.toLocaleLowerCase()])), [workflow?.items]);
  const query = search.trim().toLocaleLowerCase();
  const filtered = workflow?.trees.filter(row => `${treeName(row)} ${row.label}`.toLocaleLowerCase().includes(query)
    || row.drops.some((_, slot) => {
      const itemId = parseTreeNumber(get(row.id, `item.${slot}`));
      const chance = parseTreeNumber(get(row.id, `percent.${slot}`));
      return itemId !== null && chance !== null && chance > 0 && (itemNames.get(itemId)?.includes(query) ?? false);
    })) ?? [];
  const updateDrafts = (updates: Record<string, string>) => {
    setDrafts(current => {
      const next = { ...current, ...updates };
      for (const [key, value] of Object.entries(updates))
        if (!isStaging && value === clean.values[key]) delete next[key];
      return next;
    }); setFailed(false);
  };
  const change = (id: string, field: string, value: string) => updateDrafts({ [treeFieldKey(id, field)]: value });
  const changePokemon = (slot: number, value: string) => {
    if (!area) return;
    const [species, form] = value.split(':');
    updateDrafts({ [treeFieldKey(area.id, `species.${slot}`)]: species ?? '',
      [treeFieldKey(area.id, `form.${slot}`)]: form ?? '' });
  };
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => { active.current = true; return () => { active.current = false; onDirtyStateChange(false); }; }, [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.shakingTrees', field: 'draft',
    message: clean.invalidPending ? t('shakingTrees.diagnostic.SessionInvalid') : workflow && dirty && !valid
      ? t('shakingTrees.invalid') : failed ? t('shakingTrees.failed') : null });

  const stage = async () => {
    if (!canEdit || !dirty || !valid || isStaging) return;
    const submitted = { ...draftRef.current };
    const updates = dirtyKeys.map(key => {
      const separator = key.lastIndexOf('|');
      return { recordId: key.slice(0, separator), field: key.slice(separator + 1), value: Number(submitted[key]) };
    });
    setFailed(false);
    try {
      const accepted = await onStage(updates);
      if (!active.current) return;
      if (!accepted) { setFailed(true); return; }
      // A response acknowledges only the exact submitted fields. Newer typing survives.
      setDrafts(current => Object.fromEntries(Object.entries(current).filter(([key, value]) => value !== submitted[key])));
    } catch { if (active.current) setFailed(true); }
  };
  const numberInput = (id: string, field: string, label: string, max = 100, min = 0) => {
    const value = get(id, field); const parsed = parseTreeNumber(value);
    return <label className="tree-number-field"><span>{label}</span>
      <input type="text" inputMode="numeric" value={value} disabled={!canEdit} aria-label={label}
        aria-invalid={parsed === null || parsed < min || parsed > max || undefined}
        onChange={event => change(id, field, event.target.value)} />
    </label>;
  };
  const total = (id: string, count: number) => Array.from({ length: count }, (_, slot) => parseTreeNumber(get(id, `percent.${slot}`)))
    .reduce<number | null>((sum, value) => sum === null || value === null ? null : sum + value, 0);
  const formatPercent = (value: number) => `${(value * 100).toLocaleString(formatLocale, { maximumFractionDigits: 6 })}%`;
  const pendingCount = session?.pendingEdits.filter(edit => edit.domain === 'workflow.shakingTrees').length ?? 0;
  const restoreRules = () => updateDrafts(Object.fromEntries(Object.entries({
    initialRiskMin: 21, initialRiskMax: 79, riskStepMin: 8, riskStepMax: 14, loseAllRolls: 10, dropMin: 1, dropMax: 3
  }).map(([field, value]) => [treeFieldKey('settings', field), String(value)])));

  return <FocusedEditorWorkspace className="shaking-trees-editor">
    <section className="panel wide-panel" aria-labelledby="shaking-trees-heading">
      <div className="panel-heading tree-heading"><TreeDeciduous size={22} aria-hidden="true" />
        <div><h2 id="shaking-trees-heading">{t('shakingTrees.title')}</h2><p>{t('shakingTrees.subtitle')}</p></div>
        <button className="secondary-button compact-button" type="button" onClick={onRefresh} disabled={isLoading || isStaging} aria-busy={isLoading || undefined}>
          <RefreshCw size={16} aria-hidden="true" /><span>{translateLiteral('Refresh')}</span>
        </button>
      </div>
      <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
        label={t('shakingTrees.title')} onStart={onStartEditSession} readOnlyReason={t('shakingTrees.readOnly')} />
      {isEditing ? <EditorSessionBarActions>
        <button className="primary-button" type="button" disabled={!canEdit || !dirty || !valid || isStaging}
          aria-busy={isStaging || undefined} onClick={() => void stage()}><Save size={16} aria-hidden="true" />
          <span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span></button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDrafts({}); setFailed(false); })}><X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span></button>
        <span className="draft-action-summary">{t('shakingTrees.draftStatus', { count: dirtyKeys.length, staged: pendingCount })}</span>
      </EditorSessionBarActions> : null}
      <div className="tree-summary"><span>{t('shakingTrees.treeCount', { count: workflow?.trees.length ?? 0 })}</span>
        <span>{t('shakingTrees.areaCount', { count: workflow?.areas.length ?? 0 })}</span><span>{t('shakingTrees.outputScope')}</span></div>
      <div className="tree-tabs" role="tablist" aria-label={t('shakingTrees.title')}>
        {(['drops', 'encounters', 'rules'] as const).map(value => <button key={value} type="button" role="tab" id={`tree-tab-${value}`}
          aria-selected={tab === value} aria-controls="tree-panel" onClick={() => setTab(value)}
          className={`secondary-button${tab === value ? ' tree-tab-active' : ''}`}>{t(`shakingTrees.tab.${value}`)}</button>)}
      </div>
      <div id="tree-panel" role="tabpanel" aria-labelledby={`tree-tab-${tab}`}>
      {tab !== 'rules' ? <RecordEditorWorkspace id="shakingTrees" className="tree-layout" browser={
        <aside className="tree-catalog">
          <label className="tree-search"><span>{t('shakingTrees.search')}</span><input type="search" value={search}
            aria-label={t('shakingTrees.search')} onChange={event => setSearch(event.target.value)} /></label>
          <div className="tree-list" aria-label={t('shakingTrees.selectTree')}>
            {filtered.map(tree => <button type="button" key={tree.id} aria-pressed={selected?.id === tree.id}
              className={selected?.id === tree.id ? 'tree-selected' : ''} onClick={() => setSelectedId(tree.id)}>
              <span>{treeName(tree)}</span>{dirtyKeys.some(key => key.startsWith(tree.id + '|')) ? <span className="tree-draft-dot" aria-label={t('shakingTrees.changed')}>●</span> : null}
            </button>)}
            {filtered.length === 0 ? <p>{t('shakingTrees.noResults')}</p> : null}
          </div>
        </aside>
      }>
        <div className="tree-detail">
        {selected && area ? <>
          <h3>{treeName(selected)}</h3>
          {tab === 'drops' ? <>
            <p>{t('shakingTrees.dropsHelp')}</p>
            <div className="tree-total" role="status">{t('shakingTrees.total', { value: total(selected.id, selected.drops.length) ?? '…' })}</div>
            <div className="tree-row-grid">
            {selected.drops.map((_, slot) => {
              const item = get(selected.id, `item.${slot}`);
              const p = selected.drops.reduce((sum, _, index) => sum + (get(selected.id, `item.${index}`) === item
                ? parseTreeNumber(get(selected.id, `percent.${index}`)) ?? 0 : 0), 0) / 100;
              const atLeastOne = settings ? 1 - Array.from({ length: settings.dropMax - settings.dropMin + 1 }, (_, index) => Math.pow(1 - p, index + settings.dropMin))
                .reduce((sum, value) => sum + value, 0) / (settings.dropMax - settings.dropMin + 1) : null;
              return <article className="tree-drop-row" key={slot}>
                <div className="tree-item-field"><label htmlFor={`tree-item-${slot}`}>{t('shakingTrees.itemSlot', { number: slot + 1 })}</label>
                  <SearchableOptionInput id={`tree-item-${slot}`} ariaLabel={t('shakingTrees.itemSlot', { number: slot + 1 })} disabled={!canEdit}
                    isFiniteCatalog localizeOptions={false} options={workflow?.items ?? []} value={get(selected.id, `item.${slot}`)}
                    onChange={value => change(selected.id, `item.${slot}`, value)} /></div>
                {numberInput(selected.id, `percent.${slot}`, t('shakingTrees.chanceSlot', { number: slot + 1 }))}
                <p className="tree-drop-metric">{t('shakingTrees.atLeastOne', { value: atLeastOne === null || p > 1 ? '…' : formatPercent(atLeastOne) })}</p>
              </article>;
            })}
            </div><p className="tree-note">{t('shakingTrees.slotCountHelp')}</p>
          </> : <>
            <p>{t('shakingTrees.areaHelp', { count: areaCounts.get(area.id) ?? 0 })}</p>
            <div className="tree-levels">{numberInput(area.id, 'levelMin', t('shakingTrees.levelMin'), 100, 1)}
              {numberInput(area.id, 'levelMax', t('shakingTrees.levelMax'), 100, 1)}</div>
            <p className="tree-note">{t('shakingTrees.levelHelp')}</p>
            <div className="tree-total" role="status">{t('shakingTrees.total', { value: total(area.id, area.slots.length) ?? '…' })}</div>
            <div className="tree-row-grid">{area.slots.map((_, slot) => <article className="tree-drop-row" key={slot}>
              <div className="tree-item-field"><label htmlFor={`tree-pokemon-${slot}`}>{t('shakingTrees.pokemonSlot', { number: slot + 1 })}</label>
                <SearchableOptionInput id={`tree-pokemon-${slot}`} ariaLabel={t('shakingTrees.pokemonSlot', { number: slot + 1 })}
                  disabled={!canEdit} isFiniteCatalog localizeOptions={false} value={`${get(area.id, `species.${slot}`)}:${get(area.id, `form.${slot}`)}`}
                  options={[{ value: '0:0', label: translateLiteral('None') }, ...(workflow?.pokemon ?? []).map(row => ({ value: `${row.species}:${row.form}`,
                    label: row.form === 0 ? row.label : `${row.label} (${t('shakingTrees.form', { number: row.form })})` }))]}
                  onChange={value => changePokemon(slot, value)} /></div>
              {numberInput(area.id, `percent.${slot}`, t('shakingTrees.chanceSlot', { number: slot + 1 }))}
            </article>)}</div>
          </>}
        </> : <p>{t('shakingTrees.empty')}</p>}
        </div>
      </RecordEditorWorkspace> : <div className="tree-rules">
        <div className="tree-rules-controls">
          <h3>{t('shakingTrees.globalRules')}</h3><p>{t('shakingTrees.riskHelp')}</p>
          <div className="tree-rules-grid">
            {numberInput('settings', 'initialRiskMin', t('shakingTrees.initialRiskMin'), 99)}
            {numberInput('settings', 'initialRiskMax', t('shakingTrees.initialRiskMax'), 99)}
            {numberInput('settings', 'riskStepMin', t('shakingTrees.riskStepMin'), 99, 1)}
            {numberInput('settings', 'riskStepMax', t('shakingTrees.riskStepMax'), 99, 1)}
          </div>
          <h3>{t('shakingTrees.quantity')}</h3><p>{t('shakingTrees.quantityHelp')}</p>
          <div className="tree-rules-grid">{numberInput('settings', 'dropMin', t('shakingTrees.dropMin'), 3, 1)}
            {numberInput('settings', 'dropMax', t('shakingTrees.dropMax'), 3, 1)}</div>
          <h3>{t('shakingTrees.loss')}</h3>
          {numberInput('settings', 'loseAllRolls', t('shakingTrees.loseAllRolls'), 101)}
          <p>{t('shakingTrees.lossHelp', { value: settings ? formatPercent(settings.loseAllRolls / 101) : '…' })}</p>
          <button className="secondary-button" type="button" disabled={!canEdit} onClick={restoreRules}><RotateCcw size={16} aria-hidden="true" /><span>{t('shakingTrees.restoreRules')}</span></button>
        </div>
        <div className="tree-probability"><h3>{t('shakingTrees.odds')}</h3><p>{t('shakingTrees.oddsHelp')}</p>
          <div className="tree-odds-scroll"><table><thead><tr><th scope="col">{t('shakingTrees.shake')}</th>
            <th scope="col">{t('shakingTrees.encounterChance')}</th><th scope="col">{t('shakingTrees.survival')}</th></tr></thead>
            <tbody>{odds.map(row => <tr key={row.shake}><th scope="row">{row.shake}</th><td>{formatPercent(row.encounter)}</td><td>{formatPercent(row.survival)}</td></tr>)}</tbody></table></div>
          <h3>{t('shakingTrees.rustling')}</h3><p>{t('shakingTrees.rustlingHelp')}</p>
          <h3>{t('shakingTrees.refresh')}</h3><p>{t('shakingTrees.refreshHelp')}</p>
          <h3>{t('shakingTrees.giant')}</h3><p>{t('shakingTrees.giantHelp')}</p>
        </div>
      </div>}
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
