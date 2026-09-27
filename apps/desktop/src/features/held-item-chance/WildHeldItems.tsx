/* SPDX-License-Identifier: GPL-3.0-only */
import { createContext, useContext, useEffect, useState } from 'react';
import { type EditSession, type PokemonRecord, type PokemonWorkflow } from '../../bridge/contracts';
import { getHeldItemPendingRates, type HeldItemChanceWorkflow } from '../../bridge/heldItemChanceContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { parseEditableIntegerDraft } from '../../editableFieldHelpers';
import { useLocalization } from '../../localization';
import './HeldItemChanceSection.css';

export type WildHeldItemUpdate = { personalId: number; field: string; value: string };
export const WildHeldItemsActions = createContext<{
  isStaging: boolean; onStage: (updates: WildHeldItemUpdate[]) => Promise<boolean>;
} | null>(null);
const fields = ['heldItem1', 'heldItem2', 'heldItem3'] as const;

export function effectiveHeldItems(row: PokemonRecord, session: EditSession | null): number[] {
  return fields.map(field => {
    const edit = session?.pendingEdits.find(edit => edit.domain === 'workflow.pokemon' &&
      edit.recordId === String(row.personalId) && edit.field === field);
    return edit?.newValue !== undefined && edit?.newValue !== null && /^\d+$/.test(edit.newValue)
      ? Number(edit.newValue) : row.personal[field];
  });
}

export function WildHeldItems({ workflow, chances, session, speciesId, form, recordId, onDirtyStateChange }: {
  workflow: PokemonWorkflow | null; chances: HeldItemChanceWorkflow | null; session: EditSession | null;
  speciesId: number | null; form: number | null; recordId?: string | null; onDirtyStateChange: (dirty: boolean) => void;
}) {
  const { t } = useLocalization();
  const actions = useContext(WildHeldItemsActions);
  const [drafts, setDrafts] = useState<Record<number, string[]>>({});
  const [failed, setFailed] = useState(false);
  const pendingIdentity = (field: string, fallback: number | null) => {
    const edit = recordId ? session?.pendingEdits.find(edit => edit.domain === 'workflow.encounters'
      && edit.recordId === recordId && edit.field === field) : undefined;
    return edit?.newValue != null && /^\d+$/.test(edit.newValue) ? Number(edit.newValue) : fallback;
  };
  const row = workflow?.pokemon.find(value => value.speciesId === pendingIdentity('speciesId', speciesId)
    && value.form === pendingIdentity('form', form));
  const options = workflow?.editableFields.find(field => field.field === 'heldItem1')?.options ?? [];
  const rates = getHeldItemPendingRates(session) ?? (chances?.rates.length === 6 ? chances.rates : null);
  const clean = row ? effectiveHeldItems(row, session) : [];
  const desired = row ? drafts[row.personalId] ?? clean.map(String) : [];
  const parse = (value: string) => {
    const id = parseEditableIntegerDraft(value, options);
    return id !== null && options.some(option => option.value === id) ? id : null;
  };
  const changes: WildHeldItemUpdate[] = [];
  let invalid = false;
  for (const [key, values] of Object.entries(drafts)) {
    const target = workflow?.pokemon.find(value => value.personalId === Number(key));
    if (!target) { invalid = true; continue; }
    const current = effectiveHeldItems(target, session);
    values.forEach((value, index) => {
      const id = parse(value);
      if (id === null) invalid = true;
      else if (id !== current[index]) changes.push({ personalId: target.personalId, field: fields[index], value: String(id) });
    });
  }
  const dirty = invalid || changes.length > 0;
  const canEdit = session !== null && workflow?.summary.availability === 'available' && actions !== null;
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => () => onDirtyStateChange(false), [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.pokemon', field: 'heldItem1',
    message: invalid ? t('heldItemChance.wildInvalid') : failed ? t('heldItemChance.wildFailed') : null });
  const stage = async () => {
    if (!canEdit || !dirty || invalid || actions.isStaging) return;
    const submitted = drafts;
    setFailed(false);
    try {
      if (!await actions.onStage(changes)) { setFailed(true); return; }
      setDrafts(previous => Object.fromEntries(Object.entries(previous).filter(([key, value]) => value !== submitted[Number(key)])));
    } catch { setFailed(true); }
  };
  const first = desired[0] === undefined ? null : parse(desired[0]);
  const second = desired[1] === undefined ? null : parse(desired[1]);
  return <section className="wild-held-items" aria-label={t('heldItemChance.wildTitle')}>
    <h3>{t('heldItemChance.wildTitle')}</h3>
    {row ? <>
      <p>{t('heldItemChance.wildShared', { pokemon: row.formLabel ? `${row.name} (${row.formLabel})` : row.name })}</p>
      <p>{t('heldItemChance.wildDraftScope')}</p>
      <div className="held-item-slot-grid">
        {fields.map((field, index) => <div key={`${row.personalId}:${field}`} className="held-item-rate-field">
          <label htmlFor={`wild-held-item-${index}`}>{t('heldItemChance.slot', { number: index + 1 })}</label>
          <span>{rates ? t('heldItemChance.wildOdds', { normal: rates[index], boosted: rates[index + 3] }) : t('heldItemChance.wildUnknown')}</span>
          <SearchableOptionInput id={`wild-held-item-${index}`} ariaLabel={t('heldItemChance.slot', { number: index + 1 })}
            disabled={!canEdit} ariaInvalid={parse(desired[index]) === null} isFiniteCatalog options={options}
            value={desired[index]} onChange={value => {
              const next = [...desired]; next[index] = value;
              setDrafts(previous => ({ ...previous, [row.personalId]: next })); setFailed(false);
            }} />
        </div>)}
      </div>
      {first !== null && first === second ? <p role="status">{t(first === 0 ? 'heldItemChance.wildEqualNone' : 'heldItemChance.wildEqualItem')}</p> : null}
      <p>{t('heldItemChance.boostedHelp')}</p>
    </> : <p>{t('heldItemChance.wildMissing')}</p>}
    {session ? <div className="held-item-actions">
      <button type="button" className="primary-button" disabled={!canEdit || !dirty || invalid || actions?.isStaging}
        aria-busy={actions?.isStaging || undefined} onClick={() => void stage()}>{t('heldItemChance.wildStage')}</button>
      <button type="button" className="secondary-button" disabled={!dirty || actions?.isStaging}
        onClick={() => { setDrafts({}); setFailed(false); }}>{t('heldItemChance.wildDiscard')}</button>
      <span>{t(dirty ? 'heldItemChance.draft' : 'heldItemChance.clean')}</span>
    </div> : null}
  </section>;
}
