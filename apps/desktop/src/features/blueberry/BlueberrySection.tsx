/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Gift, RotateCcw, Save, X } from 'lucide-react';
import { blueberryEligibilityFields, type BlueberryEditor, type BlueberryUpdate, type BlueberryWorkflow } from '../../bridge/blueberryContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './BlueberrySection.css';

type Props = {
  editor: BlueberryEditor; workflow: BlueberryWorkflow | null; isStaging: boolean; isEditing: boolean; isEditStarting: boolean;
  onStartEditSession: () => void; onCancelEditSession: (onDiscard: () => void) => void;
  onStage: (revision: string, updates: BlueberryUpdate[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; panelOutput: WorkflowPanelOutput;
  renderPokemon?: (species: number, name: string) => ReactNode;
};
type Row = BlueberryWorkflow['rows'][number];
type Draft = { rowId: string; field: BlueberryUpdate['field']; value: string; revision: string };

export function BlueberrySection({ editor, workflow, isStaging, isEditing, isEditStarting, onStartEditSession,
  onCancelEditSession, onStage, onDirtyStateChange, panelOutput, renderPokemon }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [failed, setFailed] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [group, setGroup] = useState('all');
  const [bulkValue, setBulkValue] = useState('');
  const detailRef = useRef<HTMLElement>(null);
  const draftsRef = useRef(drafts); draftsRef.current = drafts;
  const revision = workflow?.sourceRevision ?? '';
  const entries = Object.values(drafts);
  const isTreats = editor === 'snacksworth';
  const amountField = editor === 'bbqRewards' ? 'rewardBp' : 'costBp';
  const validAmount = (value: string) => /^\d+$/u.test(value) && Number.isSafeInteger(Number(value)) && Number(value) <= 9999999;
  const draftInvalid = (draft: Draft) => draft.revision !== revision || !validAmount(draft.value)
    || (blueberryEligibilityFields.some(field => field === draft.field) && Number(draft.value) > 1);
  const invalid = entries.some(draftInvalid);
  const available = workflow?.summary.availability === 'available';
  const canEdit = available && isEditing;
  const unnamed = (row: Row) => /^\s*(?:\[~\s*\d+\])?\s*$/u.test(row.label);
  const rows = [...(workflow?.rows ?? [])].sort((a, b) => editor === 'bbqRewards'
    ? a.group - b.group || a.id.localeCompare(b.id, undefined, { numeric: true }) : Number(unnamed(a)) - Number(unnamed(b)) || Number(a.id) - Number(b.id));
  const questTypes = ['1','2','3','5','6','7','8','9','10','11','14','15','21','22','23','25','27','29','30','31','32','33','34'];
  const label = (row: Row) => editor === 'bbqRewards' && questTypes.includes(row.id.split(':')[0])
    ? t(`blueberry.quest.${row.id.split(':')[0]}`)
    : unnamed(row) ? t('blueberry.unnamedRequest', { id: row.id })
    : row.label.replace(/\[VAR [^\]]+\]/gu, '…').replace(/\\[nrc]/gu, ' ').replace(/\{([^|{}]+)\|[^{}]+\}/gu, '$1');
  const filtered = rows.filter(row => (group === 'all' || String(row.group) === group)
    && `${label(row)} ${row.id}`.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  const selected = rows.find(row => row.id === selectedId) ?? filtered[0] ?? rows[0];
  const value = (row: Row, field: BlueberryUpdate['field']) => drafts[`${row.id}/${field}`]?.value ?? String(row.values[field] ?? 0);
  const editions = ['Scarlet', 'Violet'] as const;
  const questAvailability = (row: Row, edition: typeof editions[number], vanilla = false) => {
    const enabled = (mode: 'solo' | 'group') => {
      const field = `${mode}${edition}` as BlueberryUpdate['field'];
      return vanilla ? row.vanillaValues[field] === 1 : value(row, field) === '1';
    };
    return t(enabled('solo') ? enabled('group') ? 'blueberry.quests.both' : 'blueberry.solo'
      : enabled('group') ? 'blueberry.group' : 'blueberry.quests.none');
  };
  const changed = (row: Row) => Object.entries(row.vanillaValues).some(([field, original]) => Number(value(row, field as BlueberryUpdate['field'])) !== original);
  const setValues = (updates: { row: Row; field: BlueberryUpdate['field']; value: string }[], force = false) => {
    if (!canEdit) return;
    setFailed(false);
    setDrafts(current => {
      const next = { ...current };
      for (const update of updates) {
        const key = `${update.row.id}/${update.field}`;
        if (!force && !isStaging && update.value === String(update.row.values[update.field])) delete next[key];
        else next[key] = { rowId: update.row.id, field: update.field, value: update.value, revision };
      }
      return next;
    });
  };
  const restore = (targets: Row[]) => setValues(targets.flatMap(row => Object.entries(row.vanillaValues).map(([field, original]) =>
    ({ row, field: field as BlueberryUpdate['field'], value: String(original) }))), isTreats);
  const solo = () => setValues(rows.flatMap(row => blueberryEligibilityFields.map(field => ({ row, field, value: field.startsWith('solo') ? '1' : '0' }))));
  useEffect(() => { onDirtyStateChange(entries.length > 0); }, [entries.length, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: `workflow.${editor}`, field: 'fields',
    message: invalid || bulkValue !== '' && !validAmount(bulkValue) ? t('blueberry.invalid') : failed ? t('blueberry.failed') : null });
  const stage = async () => {
    if (!canEdit || invalid || isStaging || entries.length === 0) return;
    const submitted = { ...draftsRef.current };
    const succeeded = await onStage(revision, Object.values(submitted).map(draft => ({ rowId: draft.rowId, field: draft.field, value: Number(draft.value) })));
    setFailed(!succeeded);
    if (succeeded) setDrafts(current => {
      const next = { ...current };
      for (const [key, draft] of Object.entries(submitted))
        if (next[key]?.value === draft.value && next[key]?.revision === draft.revision) delete next[key];
      return next;
    });
  };
  const title = t(`workbench.section.${editor}.label`);
  return <FocusedEditorWorkspace className="blueberry-editor">
    <section className="panel wide-panel blueberry-panel" aria-labelledby={`${editor}-heading`}>
      <div className="panel-heading"><Gift size={20} aria-hidden="true" /><h2 id={`${editor}-heading`}>{title}</h2></div>
      <p className="muted">{t(`blueberry.${editor}.help`)}</p>
      <EditorSessionBar canEdit={available && rows.length > 0} isEditing={isEditing} isStarting={isEditStarting}
        label={title} onStart={onStartEditSession} />
      {isEditing ? <EditorSessionBarActions>
        <button className="primary-button" type="button" aria-busy={isStaging || undefined}
          disabled={!canEdit || invalid || isStaging || entries.length === 0} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" />{translateLiteral(isStaging ? 'Staging' : 'Stage')}
        </button>
        <button className="danger-button" type="button" disabled={isStaging}
          onClick={() => onCancelEditSession(() => { setDrafts({}); setFailed(false); })}><X size={16} aria-hidden="true" />{translateLiteral('Cancel')}</button>
        <span className="draft-action-summary">{t('blueberry.draftCount', { count: entries.length })}</span>
      </EditorSessionBarActions> : null}
      <div className="blueberry-toolbar">
        <label className="field blueberry-search"><span>{translateLiteral('Search')}</span>
          <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={t(isTreats ? 'blueberry.snacksworth.search' : 'blueberry.search')} /></label>
        {editor === 'bbqRewards' ? <div className="field"><span>{t('blueberry.grade')}</span>
          <SearchableOptionInput value={group} onChange={setGroup} disabled={false} ariaLabel={t('blueberry.grade')} isFiniteCatalog localizeOptions={false}
            options={[{ value: 'all', label: t('blueberry.all') }, ...[0, 1, 2].map(value => ({ value: String(value), label: t(`blueberry.grade.${value}`) }))]} /></div> : null}
        <span className="muted blueberry-count">{t('blueberry.recordCount', { visible: filtered.length, total: rows.length })}</span>
      </div>
      {isEditing ? <div className="blueberry-bulk">
        {isTreats ? <button className="secondary-button" type="button" disabled={!canEdit || isStaging} onClick={solo}>{t('blueberry.allSolo')}</button>
          : <><label className="field"><span>{t('blueberry.bulkBp')}</span><input type="text" inputMode="numeric" value={bulkValue}
            disabled={!canEdit} onChange={event => setBulkValue(event.target.value)} aria-invalid={bulkValue !== '' && !validAmount(bulkValue) || undefined} /></label>
            <button className="secondary-button" type="button" disabled={!canEdit || isStaging || !validAmount(bulkValue) || filtered.length === 0}
              onClick={() => setValues(filtered.map(row => ({ row, field: amountField, value: bulkValue })))}>{t('blueberry.setFiltered', { count: filtered.length })}</button></>}
        <button className="secondary-button" type="button" disabled={!canEdit || isStaging || rows.length === 0} onClick={() => restore(rows)}>
          <RotateCcw size={16} aria-hidden="true" />{t('blueberry.restoreAll')}</button>
      </div> : null}
      <div className="blueberry-layout">
        <div className="blueberry-list" role="group" aria-label={t('blueberry.records')}>
          {filtered.map(row => <button type="button" key={row.id} className="blueberry-row" aria-pressed={selected?.id === row.id} onClick={() => {
            setSelectedId(row.id);
            const detail = detailRef.current;
            if (detail && (detail.closest('.blueberry-editor')?.clientWidth ?? 0) <= 850) detail.scrollIntoView({ block: 'start' });
          }}>
            {isTreats ? renderPokemon?.(row.species, label(row)) : null}
            <span className="blueberry-row-copy"><strong data-localization-ignore="true">{label(row)}</strong>
              {isTreats ? <span className="muted blueberry-quest-summary">{editions.map(edition => <span key={edition}>
                {t('blueberry.quests.edition', { edition: t(`blueberry.edition.${edition}`), quests: questAvailability(row, edition) })}
              </span>)}</span> : <span className="muted">{editor === 'bbqRewards' ? `${t(`blueberry.grade.${row.group}`)} · ${t(`blueberry.difficulty.${row.difficulty}`)} · ${t('blueberry.goal')}: ${row.goal} · ${row.id}` : t('blueberry.recordId', { id: row.id })}</span>}</span>
            <span className="blueberry-row-value">{isTreats ? null : <b>{value(row, amountField)} BP</b>}
              {changed(row) ? <span className="blueberry-changed">{t('blueberry.modified')}</span> : null}</span>
          </button>)}
          {filtered.length === 0 ? <p className="muted">{t('blueberry.empty')}</p> : null}
        </div>
        {selected ? <section ref={detailRef} className="blueberry-detail" aria-labelledby={`${editor}-selected`}>
          <div className="blueberry-detail-heading">{isTreats ? renderPokemon?.(selected.species, label(selected)) : null}
            <h3 id={`${editor}-selected`} data-localization-ignore="true">{label(selected)}</h3></div>
          <button type="button" className="secondary-button" disabled={!canEdit || isStaging} onClick={() => restore([selected])}>
            <RotateCcw size={16} aria-hidden="true" />{t('blueberry.restore')}</button>
          {isTreats ? <div className="blueberry-eligibility">
            {editions.map(edition => <fieldset key={edition}><legend>{t(`blueberry.edition.${edition}`)}</legend>
              {(['solo', 'group'] as const).map(mode => { const field = `${mode}${edition}` as BlueberryUpdate['field'];
                return <label className="checkbox-field" key={field}><input type="checkbox" disabled={!canEdit} checked={value(selected, field) === '1'}
                  onChange={event => setValues([{ row: selected, field, value: event.target.checked ? '1' : '0' }])} />
                  <span>{t(`blueberry.${mode}`)}</span></label>; })}
              <p className="muted blueberry-vanilla-quests">{t('blueberry.quests.vanilla', { quests: questAvailability(selected, edition, true) })}</p>
            </fieldset>)}
          </div> : <label className="field blueberry-amount"><span>{t(`blueberry.field.${amountField}`)}</span>
            <input type="text" inputMode="numeric" disabled={!canEdit} value={value(selected, amountField)}
              aria-invalid={drafts[`${selected.id}/${amountField}`] ? draftInvalid(drafts[`${selected.id}/${amountField}`]) : undefined}
              onChange={event => setValues([{ row: selected, field: amountField, value: event.target.value }])} />
            <small className="muted">{t('blueberry.range')}</small>
            <small className="muted">{t('blueberry.originalBp', { value: selected.vanillaValues[amountField] ?? 0 })}</small>
          </label>}
          {!isTreats ? <dl className="blueberry-facts"><div><dt>{t('blueberry.identity')}</dt><dd data-localization-ignore="true">{selected.id}</dd></div>
            {editor === 'bbqRewards' ? <div><dt>{t('blueberry.goal')}</dt><dd>{selected.goal}</dd></div> : null}</dl> : null}
          <p className="muted blueberry-help">{t(isTreats ? 'blueberry.snacksworth.progress' : editor === 'supportBoard' ? 'blueberry.supportBoard.priceHelp' : 'blueberry.bpHelp')}</p>
          <p className="muted blueberry-help">{t('blueberry.restoreHelp')}</p>
        </section> : null}
      </div>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
