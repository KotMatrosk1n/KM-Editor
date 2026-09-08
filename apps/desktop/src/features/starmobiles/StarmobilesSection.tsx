/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useRef, useState } from 'react';
import { Check, LockKeyhole, RotateCcw, Save, Star, X } from 'lucide-react';
import { starmobileFieldMaximum as maximum, starmobileFields, starmobileMoveFields, starmobileTraitFields, starmobileTypeOptions, type StarmobileUpdate, type StarmobilesWorkflow } from '../../bridge/starmobilesContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
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
    return <label className="field" key={field}>
      <span>{t(`starmobiles.field.${field}`)}</span>
      <input aria-label={`${t(`starmobiles.boss.${bossNames[row.bossType] ?? 'unknown'}`)} ${t(`starmobiles.field.${field}`)}`}
        type="text" inputMode="numeric" disabled={!canEdit} title={t('starmobiles.range', { maximum: maximum(field) })}
        aria-invalid={fieldInvalid || undefined} className={draft ? 'starmobiles-input-edited' : undefined}
        value={drafts[key]?.value ?? String(source)}
        onChange={event => setValue(row, field, event.target.value)} />
    </label>;
  };

  return <FocusedEditorWorkspace className="starmobiles-editor">
    <section className="panel wide-panel starmobiles-panel" aria-labelledby="starmobiles-heading">
      <div className="panel-heading"><Star size={20} aria-hidden="true" /><h2 id="starmobiles-heading">{t('starmobiles.title')}</h2></div>
    <EditorSessionBar canEdit={available && !!selected} isEditing={isEditing} isStarting={isEditStarting}
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
    <div className="starmobiles-selector" role="group" aria-label={t('starmobiles.select')}>
      {rows.map(row => {
        const edited = entries.some(draft => draft.rowId === row.id);
        return <button type="button" key={row.id} aria-pressed={selected?.id === row.id}
          className={`starmobiles-boss starmobiles-boss-${bossNames[row.bossType] ?? 'unknown'}`}
          onClick={() => setSelectedId(row.id)}>
          <span className="starmobiles-sprite" aria-hidden="true">
            {bossSprites[row.bossType] ? <img src={`/sprites/starmobiles/${bossSprites[row.bossType]}.png`} alt="" width={94} height={94} /> : <Star size={48} />}
          </span>
          <span className="starmobiles-boss-copy"><strong>{bossLabel(row.bossType)}</strong>
            <span>{t('starmobiles.field.level')} <b>{(drafts[`${row.id}/level`]?.value ?? row.values.level) || '…'}</b></span>
          </span>
          <span className="starmobiles-boss-status">{edited ? t('starmobiles.edited') : null}{selected?.id === row.id ? <Check size={16} aria-hidden="true" /> : null}</span>
        </button>;
      })}
    </div>
    {selected ? <section className="starmobiles-detail" aria-labelledby="starmobiles-selected-heading">
      <div className="starmobiles-detail-heading">
        <h3 id="starmobiles-selected-heading">{t('starmobiles.selectedTitle', { boss: bossLabel(selected.bossType) })}</h3>
        <div className="starmobiles-actions">
        <button className="secondary-button" type="button" disabled={!canEdit || isStaging || !selected.vanillaValues}
          title={t('starmobiles.resetVanillaHelp')} onClick={resetSelectedToVanilla}>
          <RotateCcw size={16} aria-hidden="true" />{t('starmobiles.resetVanilla')}
        </button>
        </div>
      </div>
      <div className="starmobiles-traits">
        {starmobileTraitFields.map(field => {
          const source = selected.values[field];
          const draft = drafts[`${selected.id}/${field}`];
          const label = t(`starmobiles.field.${field}`);
          const options = (field === 'ability' ? workflow?.abilityOptions ?? [] : starmobileTypeOptions)
            .map(option => ({ value: option.value, label: field === 'ability'
              ? option.value === 0 ? translateLiteral('None') : option.label : translateLiteral(option.label), disabled: false }));
          const hasCatalog = options.length > 0;
          if (source !== undefined && !options.some(option => option.value === source)) options.push({ value: source, label: String(source), disabled: true });
          return <div className="field" key={field}>
            <span>{label}{field === 'type2' ? <ContextHelp label={label}>{t('starmobiles.typesHelp')}</ContextHelp> : null}</span>
            <SearchableOptionInput ariaLabel={`${bossLabel(selected.bossType)} ${label}`} isFiniteCatalog localizeOptions={false}
              disabled={!canEdit || source === undefined || !hasCatalog} options={options}
              value={draft?.value ?? (source === undefined ? '' : String(source))}
              ariaInvalid={draft ? draftInvalid(draft) : undefined} className={draft ? 'starmobiles-input-edited' : undefined}
              onChange={value => setValue(selected, field, value)} />
          </div>;
        })}
      </div>
      <div className="starmobiles-stats-heading">
        <h4>{t('starmobiles.stats')}</h4>
        <p className="muted">{t('starmobiles.statsHelp')}</p>
      </div>
      <div className="starmobiles-edit-layout">
        <div className="starmobiles-level-fields">
          {input(selected, 'level')}
          {input(selected, 'hpMultiplier')}
        </div>
        <div className="starmobiles-fields">{statFields.map(field => field === 'speed' ?
          <label className="field" key={field}>
            <span><span>{t('starmobiles.field.speed')}</span><ContextHelp label={t('starmobiles.field.speed')}>{t('starmobiles.speedHelp')}</ContextHelp></span>
            <input aria-label={`${bossLabel(selected.bossType)} ${t('starmobiles.field.speed')}`} type="text" readOnly
              value={selected.values.move1 === undefined ? '…' : selected.values.move1 & 255} />
          </label> : input(selected, field))}</div>
      </div>
      <fieldset className="starmobiles-moves">
        <legend>{t('starmobiles.moves')}</legend>
        <div className="starmobiles-move-fields">
          {(['move1', ...starmobileMoveFields] as const).map((field, index) => {
            const source = selected.values[field];
            const locked = field === 'move1' || (source !== undefined && isSignature(source));
            const draft = drafts[`${selected.id}/${field}`];
            const label = t('starmobiles.moveSlot', { slot: index + 1 });
            const options = (workflow?.moveOptions ?? []).filter(option => option.canSelect || option.value === source)
              .map(option => ({ value: option.value, label: option.value === 0 ? t('starmobiles.noMove') : option.label, disabled: !option.canSelect }));
            if (source !== undefined && !options.some(option => option.value === source)) options.push({ value: source, label: String(source), disabled: true });
            return <div className="field" key={field}>
              <span className="starmobiles-move-label">{label}{locked ? <span className="starmobiles-lock"><LockKeyhole size={14} aria-hidden="true" />{t('starmobiles.signature')}</span> : null}</span>
              <SearchableOptionInput ariaLabel={`${bossLabel(selected.bossType)} ${label}`}
                disabled={!canEdit || locked || source === undefined || !workflow?.moveOptions.length}
                ariaInvalid={draft ? draftInvalid(draft) : undefined} isFiniteCatalog localizeOptions={false}
                className={draft ? 'starmobiles-input-edited' : undefined}
                value={draft?.value ?? (source === undefined ? '' : String(source))} options={options}
                onChange={value => { if (field !== 'move1' && !locked) setValue(selected, field, value); }} />
            </div>;
          })}
        </div>
      </fieldset>
      <details className="starmobiles-details"><summary>{t('starmobiles.details')}</summary>
        <dl>
          <div><dt>{t('starmobiles.trainer')}</dt><dd data-localization-ignore="true">{selected.trainerId}</dd></div>
          <div><dt>{t('starmobiles.event')}</dt><dd data-localization-ignore="true">{selected.eventId}</dd></div>
          <div><dt>{t('starmobiles.difficulty')}</dt><dd>{selected.difficulty}</dd></div>
        </dl>
      </details>
    </section> : null}
    {workflow?.rows.length === 0 ? <p className="muted">{t('starmobiles.empty')}</p> : null}
    </section>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </FocusedEditorWorkspace>;
}
