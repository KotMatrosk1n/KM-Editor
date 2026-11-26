/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useRef, useState } from 'react';
import { LockKeyhole, RotateCcw, Save, Star, X } from 'lucide-react';
import { starmobileFieldMaximum as maximum, starmobileFields, starmobileMoveFields, starmobileTraitFields, starmobileTypeOptions, type StarmobileUpdate, type StarmobilesWorkflow } from '../../bridge/starmobilesContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { EditorRecordTable } from '../../components/EditorRecordTable';
import { EditorDetailGroup, EditorDetailValue } from '../../components/EditorRecordDetails';
import { ContextHelp } from '../../components/ContextHelp';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './StarmobilesSection.css';

type Props = {
  workflow: StarmobilesWorkflow | null;
  isStaging: boolean;
  isEditing: boolean;
  isEditStarting: boolean;
  onStartEditSession: () => void;
  onCancelEditSession: (onDiscard: () => void) => void;
  onStage: (revision: string, updates: StarmobileUpdate[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void;
  panelOutput: WorkflowPanelOutput;
};
type Draft = { rowId: string; field: StarmobileUpdate['field']; value: string; revision: string };
const bossNames = ['fire', 'dark', 'fairy', 'fighting', 'poison'];
const bossOrder = [1, 0, 4, 2, 3];
const bossSprites = ['schedar', 'segin', 'ruchbah', 'caph', 'navi'];
const statFields = ['hp', 'attack', 'defense', 'specialAttack', 'specialDefense', 'speed'] as const;
const isSignature = (value: number) => value >= 896 && value <= 900;

export function StarmobilesSection({ workflow, isStaging, isEditing, isEditStarting, onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, panelOutput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [failed, setFailed] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const draftsRef = useRef(drafts);
  draftsRef.current = drafts;
  const revision = workflow?.sourceRevision ?? '';
  const entries = Object.values(drafts);
  const draftInvalid = (draft: Draft) => {
    if (!/^\d+$/u.test(draft.value) || draft.revision !== revision) return true;
    const value = Number(draft.value);
    if (draft.field === 'type1' || draft.field === 'type2') return value < 0 || value > 17;
    if (draft.field === 'ability') return !workflow?.abilityOptions.some(option => option.value === value);
    if (!draft.field.startsWith('move')) return value < 1 || value > maximum(draft.field);
    const source = workflow?.rows.find(row => row.id === draft.rowId)?.values[draft.field];
    return source === undefined || isSignature(source) || isSignature(value)
      || !workflow?.moveOptions.some(option => option.canSelect && option.value === value);
  };
  const invalidMoveOrder = (workflow?.rows ?? []).some(row => {
    if (!entries.some(draft => draft.rowId === row.id && draft.field.startsWith('move'))) return false;
    let empty = false;
    return (['move1', ...starmobileMoveFields] as const).some(field => {
      const value = Number(drafts[`${row.id}/${field}`]?.value ?? row.values[field] ?? 0);
      if (value === 0) empty = true;
      return value > 0 && empty;
    });
  });
  const invalid = entries.some(draftInvalid) || invalidMoveOrder;
  const available = workflow?.summary.availability === 'available';
  const canEdit = available && isEditing;
  const rows = [...(workflow?.rows ?? [])].sort((a, b) => bossOrder.indexOf(a.bossType) - bossOrder.indexOf(b.bossType));
  const selected = rows.find(row => row.id === selectedId) ?? rows[0];
  const bossLabel = (bossType: number) => t(`starmobiles.boss.${bossNames[bossType] ?? 'unknown'}`);
  const setValue = (row: StarmobilesWorkflow['rows'][number], field: StarmobileUpdate['field'], value: string) => {
    if (!canEdit) return;
    setFailed(false);
    setDrafts(current => {
      const next = { ...current };
      if (value === String(row.values[field])) delete next[`${row.id}/${field}`];
      else next[`${row.id}/${field}`] = { rowId: row.id, field, value, revision };
      return next;
    });
  };
  const resetSelectedToVanilla = () => {
    if (!selected?.vanillaValues || !canEdit || isStaging) return;
    const row = selected;
    const vanilla = selected.vanillaValues;
    setFailed(false);
    setDrafts(current => {
      const next = { ...current };
      for (const field of [...starmobileFields, ...starmobileMoveFields, ...starmobileTraitFields]) {
        const source = row.values[field];
        const value = vanilla[field];
        if (source === undefined || value === undefined || (field.startsWith('move') && isSignature(source))) continue;
        const key = `${row.id}/${field}`;
        if (value === source) delete next[key];
        else next[key] = { rowId: row.id, field, value: String(value), revision };
      }
      return next;
    });
  };
  useEffect(() => { onDirtyStateChange(entries.length > 0); }, [drafts, onDirtyStateChange, entries.length]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.starmobiles', field: 'fields',
    message: invalidMoveOrder ? t('starmobiles.moveOrder') : invalid ? t('starmobiles.invalid') : failed ? t('starmobiles.failed') : null });

  const stage = async () => {
    if (!canEdit || invalid || isStaging || entries.length === 0) return;
    const submitted = { ...draftsRef.current };
    const succeeded = await onStage(revision, Object.values(submitted).map(draft => ({
      rowId: draft.rowId, field: draft.field, value: Number(draft.value)
    })));
    setFailed(!succeeded);
    if (succeeded) setDrafts(current => {
      const next = { ...current };
      for (const [key, draft] of Object.entries(submitted)) {
        if (next[key]?.value === draft.value && next[key]?.revision === draft.revision) delete next[key];
      }
      return next;
    });
  };

  const input = (row: NonNullable<StarmobilesWorkflow>['rows'][number], field: StarmobileUpdate['field']) => {
    const source = row.values[field];
    if (source === undefined) return null;
    const key = `${row.id}/${field}`;
    const draft = drafts[key];
    const fieldInvalid = draft !== undefined && draftInvalid(draft);
    return <label className="starmobiles-field-row" key={field}>
      <span>{t(`starmobiles.field.${field}`)}</span>
      <input aria-label={`${t(`starmobiles.boss.${bossNames[row.bossType] ?? 'unknown'}`)} ${t(`starmobiles.field.${field}`)}`}
        type="text" inputMode="numeric" disabled={!canEdit} title={t('starmobiles.range', { maximum: maximum(field) })}
        aria-invalid={fieldInvalid || undefined} className={draft ? 'starmobiles-input-edited' : undefined}
        value={drafts[key]?.value ?? String(source)}
        onChange={event => setValue(row, field, event.target.value)} />
      {statFields.some(stat => stat === field) ? <span className="muted">{row.vanillaValues?.[field] ?? '—'}</span> : null}
    </label>;
  };

  const typeLabel = (value: string | number | undefined) => value === undefined ? '—'
    : translateLiteral(starmobileTypeOptions.find(option => option.value === Number(value))?.label ?? String(value));
  const moveInvalid = invalidMoveOrder || entries.some(draft => draft.field.startsWith('move') && draftInvalid(draft));
  const localError = invalidMoveOrder ? t('starmobiles.moveOrder') : invalid ? t('starmobiles.invalid') : failed ? t('starmobiles.failed') : null;
  return <FocusedEditorWorkspace className="starmobiles-editor">
    <section className="panel wide-panel starmobiles-panel" aria-labelledby="starmobiles-heading">
      <div className="panel-heading"><Star size={20} aria-hidden="true" /><h2 id="starmobiles-heading">{t('starmobiles.title')}</h2></div>
    <RecordEditorWorkspace id="starmobiles" className="starmobiles-layout" initialSection="battle"
      browserVisibleRows={5}
      sessionBar={<><EditorSessionBar canEdit={available && !!selected} isEditing={isEditing} isStarting={isEditStarting}
        label={t('starmobiles.title')} onStart={onStartEditSession} />
      {isEditing ? <EditorSessionBarActions>
      <button className="primary-button" type="button" aria-busy={isStaging || undefined}
        disabled={!canEdit || invalid || isStaging || entries.length === 0} onClick={() => void stage()}>
        {isStaging ? <RotateCcw className="button-busy-icon" size={16} aria-hidden="true" /> : <Save size={16} aria-hidden="true" />}
        <span>{translateLiteral(isStaging ? 'Staging' : 'Stage')}</span>
      </button>
      <button className="danger-button" type="button" disabled={isStaging}
        onClick={() => onCancelEditSession(() => { setDrafts({}); setFailed(false); })}>
        <X size={16} aria-hidden="true" /><span>{translateLiteral('Cancel')}</span>
      </button>
      <span className="draft-action-summary">{t('starmobiles.draftCount', { count: entries.length })}</span>
    </EditorSessionBarActions> : null}
      </>}
      browser={<EditorRecordTable label={t('starmobiles.select')} identityLabel={t('starmobiles.title')}
        rows={rows} rowId={row => row.id} selection={selected?.id} onSelect={row => setSelectedId(row.id)}
        empty={t('starmobiles.empty')} identity={row => <>
          <span className="starmobiles-sprite" aria-hidden="true">
            {bossSprites[row.bossType] ? <img src={`/sprites/starmobiles/${bossSprites[row.bossType]}.png`} alt="" width={32} height={32} /> : <Star size={20} />}
          </span>
          <span>{bossLabel(row.bossType)}</span>
        </>} columns={[
          { id: 'level', label: t('starmobiles.field.level'), numeric: true, value: row => drafts[`${row.id}/level`]?.value ?? row.values.level ?? '—' },
          { id: 'types', label: translateLiteral('Types'), value: row => {
            const primary = drafts[`${row.id}/type1`]?.value ?? row.values.type1;
            const secondary = drafts[`${row.id}/type2`]?.value ?? row.values.type2;
            return secondary === undefined || Number(primary) === Number(secondary)
              ? typeLabel(primary) : `${typeLabel(primary)} / ${typeLabel(secondary)}`;
          } },
          { id: 'state', label: translateLiteral('State'), value: row => entries.some(draft => draft.rowId === row.id)
            ? <span className="starmobiles-draft">{t('starmobiles.edited')}</span> : '—' },
        ]} />}
      browserFooter={localError ? <p className="editor-task-status validation-message" role="alert">{localError}</p> : undefined}>
    {selected ? <>
      <EditorWorkspaceHeader afterBrowser>
        <div className="editor-record-context">
          <EditorRecordHeading name={t('starmobiles.selectedTitle', { boss: bossLabel(selected.bossType) })} />
          {isEditing ? <button className="secondary-button compact-button" type="button" disabled={!canEdit || isStaging || !selected.vanillaValues}
            title={t('starmobiles.resetVanillaHelp')} onClick={resetSelectedToVanilla}>
            <RotateCcw size={16} aria-hidden="true" />{t('starmobiles.resetVanilla')}
          </button> : null}
        </div>
      </EditorWorkspaceHeader>
      <EditorWorkspaceSection id="battle" label={translateLiteral('Battle Setup')}
        invalid={entries.some(draft => (['level', 'hpMultiplier', ...starmobileTraitFields] as const).some(field => field === draft.field) && draftInvalid(draft))}>
        <div className="editor-task-groups">
          <EditorDetailGroup label={translateLiteral('Scaling')}>
            <div className="editor-task-list">{input(selected, 'level')}{input(selected, 'hpMultiplier')}</div>
          </EditorDetailGroup>
          <EditorDetailGroup label={translateLiteral('Traits')}>
            <div className="editor-task-list">
        {starmobileTraitFields.map(field => {
          const source = selected.values[field];
          const draft = drafts[`${selected.id}/${field}`];
          const label = t(`starmobiles.field.${field}`);
          const options = (field === 'ability' ? workflow?.abilityOptions ?? [] : starmobileTypeOptions)
            .map(option => ({ value: option.value, label: field === 'ability'
              ? option.value === 0 ? translateLiteral('None') : option.label : translateLiteral(option.label), disabled: false }));
          const hasCatalog = options.length > 0;
          if (source !== undefined && !options.some(option => option.value === source)) options.push({ value: source, label: String(source), disabled: true });
          if (source === undefined) return null;
          return <div key={field}>
            <span className="editor-task-field-name">{label}{field === 'type2' ? <ContextHelp label={label}>{t('starmobiles.typesHelp')}</ContextHelp> : null}</span>
            <SearchableOptionInput ariaLabel={`${bossLabel(selected.bossType)} ${label}`} isFiniteCatalog localizeOptions={false}
              disabled={!canEdit || source === undefined || !hasCatalog} options={options}
              value={draft?.value ?? (source === undefined ? '' : String(source))}
              ariaInvalid={draft ? draftInvalid(draft) : undefined} className={draft ? 'starmobiles-input-edited' : undefined}
              onChange={value => setValue(selected, field, value)} />
          </div>;
        })}
            </div>
          </EditorDetailGroup>
        </div>
      </EditorWorkspaceSection>
      <EditorWorkspaceSection id="stats" label={t('starmobiles.stats')}
        invalid={entries.some(draft => statFields.some(field => field === draft.field) && draftInvalid(draft))}>
        <EditorDetailGroup label={t('starmobiles.stats')}
          headingValue={<ContextHelp label={t('starmobiles.stats')}>{t('starmobiles.statsHelp')}</ContextHelp>}>
          <div className="editor-task-list starmobiles-stat-grid">
            <div className="editor-task-column-head"><span>{translateLiteral('Stat')}</span><span>{translateLiteral('Value')}</span><span>{translateLiteral('Original')}</span></div>
            {statFields.map(field => field === 'speed' ?
              <div key={field}>
                <span className="editor-task-field-name">{t('starmobiles.field.speed')}
                  <ContextHelp label={t('starmobiles.field.speed')}>{t('starmobiles.speedHelp')}</ContextHelp>
                </span>
                <output aria-label={`${bossLabel(selected.bossType)} ${t('starmobiles.field.speed')}`}>
                  {selected.values.move1 === undefined ? '—' : selected.values.move1 & 255}
                </output>
                <span className="muted">{selected.vanillaValues?.move1 === undefined ? '—' : selected.vanillaValues.move1 & 255}</span>
              </div> : input(selected, field))}
          </div>
        </EditorDetailGroup>
      </EditorWorkspaceSection>
      <EditorWorkspaceSection id="moves" label={t('starmobiles.moves')} invalid={moveInvalid}>
        <EditorDetailGroup label={t('starmobiles.moves')}>
          <div className="editor-task-list starmobiles-move-grid">
          {(['move1', ...starmobileMoveFields] as const).map((field, index) => {
            const source = selected.values[field];
            const locked = field === 'move1' || (source !== undefined && isSignature(source));
            const draft = drafts[`${selected.id}/${field}`];
            const label = t('starmobiles.moveSlot', { slot: index + 1 });
            const options = (workflow?.moveOptions ?? []).filter(option => option.canSelect || option.value === source)
              .map(option => ({ value: option.value, label: option.value === 0 ? t('starmobiles.noMove') : option.label, disabled: !option.canSelect }));
            if (source !== undefined && !options.some(option => option.value === source)) options.push({ value: source, label: String(source), disabled: true });
            return <div key={field}>
              <span>{label}</span>
              <SearchableOptionInput ariaLabel={`${bossLabel(selected.bossType)} ${label}`}
                disabled={!canEdit || locked || source === undefined || !workflow?.moveOptions.length}
                ariaInvalid={draft ? draftInvalid(draft) : undefined} isFiniteCatalog localizeOptions={false}
                className={draft ? 'starmobiles-input-edited' : undefined}
                value={draft?.value ?? (source === undefined ? '' : String(source))} options={options}
                onChange={value => { if (field !== 'move1' && !locked) setValue(selected, field, value); }} />
              {locked ? <span className="starmobiles-lock"><LockKeyhole size={14} aria-hidden="true" />{t('starmobiles.signature')}</span> : <span />}
            </div>;
          })}
          </div>
        </EditorDetailGroup>
      </EditorWorkspaceSection>
      <EditorWorkspaceSection id="details" label={t('starmobiles.details')}>
        <EditorDetailGroup label={translateLiteral('Identity')}>
          <dl className="editor-detail-list">
            <EditorDetailValue label={t('starmobiles.trainer')} dataValue path>{selected.trainerId}</EditorDetailValue>
            <EditorDetailValue label={t('starmobiles.event')} dataValue path>{selected.eventId}</EditorDetailValue>
            <EditorDetailValue label={t('starmobiles.difficulty')}>{selected.difficulty}</EditorDetailValue>
          </dl>
        </EditorDetailGroup>
      </EditorWorkspaceSection>
    </> : null}
    </RecordEditorWorkspace>
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
