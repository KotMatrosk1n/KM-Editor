/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useId, useMemo, useRef, useState } from 'react';
import type { ApiDiagnostic, EditSession, ZaBehaviorWorkflow } from '../../bridge/contracts';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import './behavior.css';

type Changes = Record<string, string>;
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
  const { workflow, editSession, selectedEntryId, onDraftDirtyChange } = props;
  const [drafts, setDrafts] = useState<Record<string, Changes>>({});
  const submission = useRef<{ id: string; snapshot: Changes } | null>(null);
  const resources = useMemo(() => workflow.resources.map(resource => {
    const pending = (editSession?.pendingEdits ?? []).filter(e => e.domain === 'workflow.behavior' && e.recordId === resource.entryId);
    const restore = pending.some(e => e.field === 'restoreVanilla');
    const fields = { ...(restore ? resource.vanillaFields : resource.fields) };
    let profile = restore ? resource.vanillaProfile : resource.profile;
    const tags = restore ? resource.vanillaTags : resource.tags;
    const initialization = pending.some(e => e.field === 'initialize');
    if (initialization && !tags.some(tag => /^(Warlike|Gentle(?:[1-9]|10)?|Cowardice|Torpor(?:[1-9]|10)?)$/.test(tag))) profile = 'aggressive';
    for (const edit of pending) {
      if (edit.field === 'profile' && edit.newValue) profile = edit.newValue;
      else if (edit.field && Object.hasOwn(fields, edit.field) && edit.newValue != null) fields[edit.field] = edit.newValue;
    }
    return { ...resource, fields, profile, isInitialized: initialization || (tags.includes('State_usually') && tags.includes('WildTeam')) };
  }), [workflow.resources, editSession]);
  const entry = resources.find(r => r.entryId === selectedEntryId) ?? null;
  const current = entry ? { ...entry.fields, profile: entry.profile } : {} as Changes;
  const draft = entry ? drafts[entry.entryId] ?? {} : {};
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
      const next = { ...(previous[entry.entryId] ?? {}), [field]: value };
      if (current[field] === value && !(submission.current?.id === entry.entryId &&
        Object.hasOwn(submission.current.snapshot, field))) delete next[field];
      const result = { ...previous, [entry.entryId]: next };
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
        <div className="za-behavior-actions">
          <button type="button" className="primary-button" disabled={!editable || props.isBehaviorUpdating || !Object.keys(draft).length || invalid.length > 0}
            onClick={() => void stage({ ...draft })}>{t('behavior.za.stage')}</button>
          <button type="button" className="secondary-button" disabled={!editable || props.isBehaviorUpdating || Object.keys(draft).length > 0}
            onClick={() => void stage({ restoreVanilla: 'true' })}>{t('behavior.za.restore')}</button>
          <button type="button" className="secondary-button" disabled={!Object.keys(draft).length || props.isBehaviorUpdating}
            onClick={() => setDrafts(previous => { const next = { ...previous }; delete next[entry.entryId]; return next; })}>{t('behavior.za.discard')}</button>
        </div>
        <details><summary>{t('behavior.za.details')}</summary>
          <p data-localization-ignore="true">{entry.sourceFile}</p>
          <p data-localization-ignore="true">{entry.tags.join(', ')}</p>
        </details>
      </div> : <p>{t('behavior.za.empty')}</p>}
    </div>
  </section>;
}
