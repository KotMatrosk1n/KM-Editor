/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useId, useMemo, useRef, useState } from 'react';
import type { ApiDiagnostic, EditSession, ZaBehaviorWorkflow } from '../../bridge/contracts';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import './behavior.css';

type Changes = Record<string, string>;
const cityProfiles: Record<string, string> = {
  aggressive: 'wp_oz_warlike', gentle: 'wp_oz_gentle', timid: 'wp_oz_cowardice', sluggish: 'wp_oz_torpor'
};
function cityProfile(tags: readonly string[]) {
  const overrides = tags.filter(tag => Object.values(cityProfiles).includes(tag));
  return overrides.length === 0 ? 'default' : overrides.length === 1
    ? Object.keys(cityProfiles).find(key => cityProfiles[key] === overrides[0])! : 'custom';
}
type Props = {
  workflow: ZaBehaviorWorkflow;
  editSession: EditSession | null;
  searchText: string;
  selectedEntryId: string | null;
  isBehaviorUpdating: boolean;
  isEditStarting: boolean;
  onStartEditSession: () => void;
  onCancelEditSession: (onDiscard: () => void) => void;
  onSearchChange: (value: string) => void;
  onSelectEntry: (value: string) => void;
  onDraftDirtyChange: (dirty: boolean) => void;
  onUpdateBehaviorEntryFields: (id: string, changes: { field: string; value: string }[]) => Promise<boolean>;
};

export function ZaBehaviorSection(props: Props) {
  const { t, translateLiteral } = useLocalization();
  const profileId = useId();
  const cityProfileId = useId();
  const cityMovementId = useId();
  const { workflow, editSession, selectedEntryId, onDraftDirtyChange } = props;
  const [drafts, setDrafts] = useState<Record<string, Changes>>({});
  const submission = useRef<{ id: string; snapshot: Changes } | null>(null);
  const resources = useMemo(() => workflow.resources.map(resource => {
    const pending = (editSession?.pendingEdits ?? []).filter(e => e.domain === 'workflow.behavior' && e.recordId === resource.entryId);
    const restore = pending.some(e => e.field === 'restoreVanilla');
    const fields = { ...(restore ? resource.vanillaFields : resource.fields) };
    let profile = restore ? resource.vanillaProfile : resource.profile;
    let cityRoaming = resource.cityRoaming;
    const cityEdit = editSession?.pendingEdits.find(e => e.domain === 'workflow.behavior' && e.field === 'cityRoaming' &&
      e.recordId?.slice(0, 4) === resource.entryId.slice(0, 4));
    switch (cityEdit?.newValue) {
      case 'true': cityRoaming = 'enabled'; break;
      case 'false': cityRoaming = 'disabled'; break;
      case 'off': cityRoaming = 'native-off'; break;
      case 'ground': case 'hover': cityRoaming = cityEdit.newValue; break;
      case 'default': cityRoaming = resource.cityRoaming === 'native-off' ? 'native' : 'unsupported'; break;
    }
    const tags = restore ? resource.vanillaTags : resource.tags;
    let outsideProfile = cityProfile(tags);
    const initialization = pending.some(e => e.field === 'initialize');
    if (initialization && !tags.some(tag => /^(Warlike|Gentle(?:[1-9]|10)?|Cowardice|Torpor(?:[1-9]|10)?)$/.test(tag))) profile = 'aggressive';
    for (const edit of pending) {
      if (edit.field === 'profile' && edit.newValue) profile = edit.newValue;
      else if (edit.field === 'cityProfile' && edit.newValue) outsideProfile = edit.newValue;
      else if (edit.field && Object.hasOwn(fields, edit.field) && edit.newValue != null) fields[edit.field] = edit.newValue;
    }
    return { ...resource, fields, profile, cityRoaming, cityProfile: outsideProfile, isInitialized: initialization || (tags.includes('State_usually') && tags.includes('WildTeam')) };
  }), [workflow.resources, editSession]);
  const entry = resources.find(r => r.entryId === selectedEntryId) ?? null;
  const movement = entry?.cityRoaming;
  const nativeMovement = movement === 'native' || movement === 'native-off';
  const movementChoice = movement === 'enabled' ? 'true' : movement === 'disabled' ? 'false'
    : movement === 'native-off' ? 'off' : movement === 'ground' || movement === 'hover' ? movement : 'default';
  const current = entry ? { ...entry.fields, profile: entry.profile, cityProfile: entry.cityProfile, cityRoaming: movementChoice } : {} as Changes;
  const cityDraft = entry ? Object.entries(drafts).find(([id, changes]) =>
    id.slice(0, 4) === entry.entryId.slice(0, 4) && Object.hasOwn(changes, 'cityRoaming'))?.[1].cityRoaming : undefined;
  const draft: Changes = entry ? { ...drafts[entry.entryId], ...(cityDraft === undefined ? {} : { cityRoaming: cityDraft }) } : {};
  const values = { ...current, ...draft };
  const canEdit = workflow.summary.availability === 'available';
  const editable = canEdit && editSession !== null;
  const dirtyCount = Object.keys(drafts).length;
  useEffect(() => { onDraftDirtyChange(dirtyCount > 0); }, [dirtyCount, onDraftDirtyChange]);
  useEffect(() => () => onDraftDirtyChange(false), [onDraftDirtyChange]);
  const invalid = workflow.fields.filter(f => Object.hasOwn(draft, f.field) &&
    (!/^[+]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?$/i.test(draft[f.field].trim()) ||
      !Number.isFinite(Math.fround(Number(draft[f.field]))) || Number(draft[f.field]) < f.minimum));
  const diagnostics = useMemo<ApiDiagnostic[]>(() => [...workflow.diagnostics,
    ...invalid.map(f => ({ code: 'KM-ZA-BEHAVIOR-VALUE-INVALID', severity: 'error' as const,
      message: t('behavior.za.invalid', { field: t(`behavior.za.${f.field}`) }),
      domain: 'workflow.behavior', field: f.field, file: entry?.sourceFile ?? null, expected: null }))],
  [workflow.diagnostics, invalid.map(f => f.field).join('|'), entry?.sourceFile, t]);
  usePublishCommonEditorDiagnostics(diagnostics);
  const filtered = resources.filter(r => `${r.speciesName} ${r.speciesId} ${r.entryId}`
    .toLocaleLowerCase().includes(props.searchText.trim().toLocaleLowerCase()));
  const change = (field: string, value: string) => {
    if (!entry) return;
    setDrafts(previous => {
      const result = { ...previous };
      if (field === 'cityRoaming') for (const [id, changes] of Object.entries(result)) {
        if (id.slice(0, 4) !== entry.entryId.slice(0, 4)) continue;
        const other = { ...changes }; delete other.cityRoaming;
        if (Object.keys(other).length) result[id] = other; else delete result[id];
      }
      const next = { ...(result[entry.entryId] ?? {}), [field]: value };
      const submitting = submission.current;
      if (current[field] === value && !(submitting && Object.hasOwn(submitting.snapshot, field) &&
        (submitting.id === entry.entryId || field === 'cityRoaming' && submitting.id.slice(0, 4) === entry.entryId.slice(0, 4)))) delete next[field];
      result[entry.entryId] = next;
      if (!Object.keys(next).length) delete result[entry.entryId];
      return result;
    });
  };
  const stage = async (snapshot: Changes) => {
    if (!entry || submission.current) return;
    const id = entry.entryId;
    submission.current = { id, snapshot };
    try {
      if (await props.onUpdateBehaviorEntryFields(id, Object.entries(snapshot).map(([field, value]) => ({ field, value })))) {
        setDrafts(previous => {
          const remaining = { ...previous[id] };
          for (const [field, value] of Object.entries(snapshot)) if (remaining[field] === value) delete remaining[field];
          const result = { ...previous, [id]: remaining };
          if (!Object.keys(remaining).length) delete result[id];
          if (snapshot.cityRoaming !== undefined) for (const [otherId, changes] of Object.entries(result)) {
            if (otherId.slice(0, 4) !== id.slice(0, 4) || changes.cityRoaming !== snapshot.cityRoaming) continue;
            const other = { ...changes }; delete other.cityRoaming;
            if (Object.keys(other).length) result[otherId] = other; else delete result[otherId];
          }
          return result;
        });
      }
    } finally { submission.current = null; }
  };
  const renderNumber = (field: ZaBehaviorWorkflow['fields'][number]) => {
    const value = values[field.field] ?? '';
    const outside = Number(value) < field.stockMinimum || Number(value) > field.stockMaximum;
    return <label className="za-behavior-field" key={field.field}>
      <span>{t(`behavior.za.${field.field}`)}</span>
      <input type="number" inputMode="decimal" min={field.minimum} max={field.maximum} step="any"
        aria-label={t(`behavior.za.${field.field}`)} aria-invalid={invalid.includes(field) || undefined}
        disabled={!editable} value={value} onChange={e => change(field.field, e.target.value)} />
      <small>{t('behavior.za.stock', { minimum: field.stockMinimum, maximum: field.stockMaximum })}</small>
      {outside ? <small className="warning-copy">{t('behavior.za.outside')}</small> : null}
    </label>;
  };
  return <section className="panel wide-panel za-behavior-section" aria-labelledby="za-behavior-heading">
    <div className="panel-heading"><h2 id="za-behavior-heading">{translateLiteral('Behavior')}</h2></div>
    <p>{t('behavior.za.description')}</p>
    <EditorSessionBar canEdit={canEdit && entry !== null} isEditing={editSession !== null}
      isStarting={props.isEditStarting} label="Behavior" onStart={props.onStartEditSession} />
    {editSession ? <EditorSessionBarActions><button type="button" className="secondary-button"
      disabled={props.isBehaviorUpdating} onClick={() => props.onCancelEditSession(() => setDrafts({}))}>
      {translateLiteral('Cancel')}</button></EditorSessionBarActions> : null}
    <div className="za-behavior-layout">
      <aside className="za-behavior-browser">
        <input type="search" aria-label={t('behavior.za.search')} placeholder={t('behavior.za.search')}
          value={props.searchText} onChange={e => props.onSearchChange(e.target.value)} />
        <small>{t('behavior.za.count', { count: filtered.length })}</small>
        <div className="za-behavior-list" role="list" aria-label={t('behavior.za.resources')}>
          {filtered.map(r => <div role="listitem" key={r.entryId}><button type="button"
            className={`za-behavior-entry${r.entryId === selectedEntryId ? ' selected' : ''}`}
            aria-pressed={r.entryId === selectedEntryId} onClick={() => props.onSelectEntry(r.entryId)}>
            <strong data-localization-ignore="true">{r.speciesName}</strong>
            <small>{t('behavior.za.identity', { form: r.form, variant: r.gender })}</small>
            {!r.isInitialized ? <small>{t('behavior.za.incomplete')}</small> : null}
            {drafts[r.entryId] ? <small>{t('behavior.za.draft')}</small> : null}
          </button></div>)}
          {!filtered.length ? <p>{t('behavior.za.empty')}</p> : null}
        </div>
      </aside>
      {entry ? <div className="za-behavior-detail">
        <h3 data-localization-ignore="true">{entry.speciesName}</h3>
        <p>{t('behavior.za.scope')}</p>
        <div className="za-behavior-field"><label htmlFor={profileId}>{t('behavior.za.profile')}</label>
          <SearchableOptionInput id={profileId} ariaLabel={t('behavior.za.profile')} disabled={!editable} value={values.profile}
            isFiniteCatalog localizeOptions={false} onChange={value => change('profile', value)}
            options={[
              ...(entry.profile === 'custom' ? [{ value: 'custom', label: t('behavior.za.custom') }] : []),
              ...workflow.profiles.map(p => ({ value: p.value, label: t(`behavior.za.profile.${p.value}`) }))
            ]} />
        </div>
        <p className="field-help">{t('behavior.za.profileHelp')}</p>
        <div className="za-behavior-field"><label htmlFor={cityProfileId}>{t('behavior.za.cityProfile')}</label>
          <SearchableOptionInput id={cityProfileId} ariaLabel={t('behavior.za.cityProfile')} disabled={!editable} value={values.cityProfile}
            isFiniteCatalog localizeOptions={false} onChange={value => change('cityProfile', value)}
            options={[
              ...(entry.cityProfile === 'custom' ? [{ value: 'custom', label: t('behavior.za.custom') }] : []),
              { value: 'default', label: t('behavior.za.cityProfileDefault') },
              ...Object.keys(cityProfiles).map(value => ({ value, label: t(value === 'gentle' ? 'behavior.za.cityProfileGentle' : `behavior.za.profile.${value}`) }))
            ]} />
          <small>{t('behavior.za.cityProfileHelp')}</small>
        </div>
        <div className="za-behavior-fields">{workflow.fields.slice(2).map(renderNumber)}</div>
        <p className="field-help">{t('behavior.za.units')}</p>
        <details><summary>{t('behavior.za.advanced')}</summary>
          <div className="za-behavior-fields">{workflow.fields.slice(0, 2).map(renderNumber)}</div>
          <p className="field-help">{t('behavior.za.angles')}</p>
        </details>
        {!entry.isInitialized ? <div className="za-behavior-initialization">
          <p>{t('behavior.za.initializeHelp')}</p>
          <button type="button" className="secondary-button" disabled={!editable || draft.initialize === 'true'}
            onClick={() => change('initialize', 'true')}>{t(draft.initialize === 'true' ? 'behavior.za.initializeSelected' : 'behavior.za.initialize')}</button>
        </div> : null}
        <p className="field-help">{t('behavior.za.roaming')}</p>
        {entry.cityRoaming === 'enabled' || entry.cityRoaming === 'disabled' ? <div className="za-behavior-field">
          <label className="za-behavior-city-roaming"><input type="checkbox" checked={values.cityRoaming === 'true'} disabled={!editable}
            onChange={e => change('cityRoaming', String(e.target.checked))} /> {t('behavior.za.cityRoaming')}</label>
          <small>{t('behavior.za.cityRoamingHelp')}</small>
        </div> : entry.cityRoaming === 'unavailable' ? <p className="field-help">
          {t('behavior.za.cityRoamingUnavailable')}
        </p> : <div className="za-behavior-field">
          <label htmlFor={cityMovementId}>{t('behavior.za.cityMovement')}</label>
          <SearchableOptionInput id={cityMovementId} ariaLabel={t('behavior.za.cityMovement')} disabled={!editable} value={values.cityRoaming}
            isFiniteCatalog localizeOptions={false} onChange={value => change('cityRoaming', value)}
            options={[
              { value: 'default', label: t(nativeMovement ? 'behavior.za.cityRoamingNative' : 'behavior.za.cityMovementDefault') },
              ...(nativeMovement ? [{ value: 'off', label: t('behavior.za.cityMovementOff') }] : [
                { value: 'ground', label: t('behavior.za.cityMovementGround') },
                { value: 'hover', label: t('behavior.za.cityMovementHover') }
              ])
            ]} />
          <small>{t(nativeMovement ? 'behavior.za.cityMovementNativeHelp' : 'behavior.za.cityMovementHelp')}</small>
        </div>}
        <div className="za-behavior-actions">
          <button type="button" className="primary-button" disabled={!editable || props.isBehaviorUpdating || !Object.keys(draft).length || invalid.length > 0}
            onClick={() => void stage({ ...draft })}>{t('behavior.za.stage')}</button>
          <button type="button" className="secondary-button" disabled={!editable || props.isBehaviorUpdating || Object.keys(draft).length > 0}
            onClick={() => void stage({ restoreVanilla: 'true' })}>{t('behavior.za.restore')}</button>
          <button type="button" className="secondary-button" disabled={!Object.keys(draft).length || props.isBehaviorUpdating}
            onClick={() => setDrafts(previous => {
              const next = { ...previous }; delete next[entry.entryId];
              for (const [id, changes] of Object.entries(next)) if (id.slice(0, 4) === entry.entryId.slice(0, 4)) {
                const other = { ...changes }; delete other.cityRoaming;
                if (Object.keys(other).length) next[id] = other; else delete next[id];
              }
              return next;
            })}>{t('behavior.za.discard')}</button>
        </div>
        <details><summary>{t('behavior.za.details')}</summary>
          <p data-localization-ignore="true">{entry.sourceFile}</p>
          <p data-localization-ignore="true">{entry.tags.join(', ')}</p>
        </details>
      </div> : <p>{t('behavior.za.empty')}</p>}
    </div>
  </section>;
}
