/* SPDX-License-Identifier: GPL-3.0-only */
import { plaFieldHelp } from './plaFieldHelp';
import { useMemo, useState } from 'react';
import { Dna, RefreshCw, RotateCcw, Save, Search, Trash2, X } from 'lucide-react';
import type { PlaField } from '../../bridge/plaProjectBridge';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { EditorDetailGroup, EditorDetailValue, PokemonDetailsStats } from '../../components/EditorRecordDetails';
import { FieldLabel } from '../../components/FieldLabel';
import { PokemonSearchBox, PokemonTable } from '../../components/PokemonSelection';
import { PokemonYieldConfirmationModal } from '../../components/PokemonYieldConfirmationModal';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { EditorWorkspaceHeader, EditorWorkspaceSection, SectionedEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { SpeciesDataMatrix, speciesStatFields, useSpeciesFieldFinder } from '../../components/SpeciesDataMatrix';
import { Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import type { PlaEditorProps } from './PlaEditorSection';
import { PlaPokemonRows } from './PlaPokemonRows';
import { isPlaFieldValueValid, parsePlaRows } from './plaDraftValidation';
import { usePlaPokemonEditor, type PlaPokemonKind } from './usePlaPokemonEditor';

const personalGroups = [
  { label: 'Base Stats', keys: speciesStatFields.map(([key]) => key) },
  { label: 'Types', keys: ['type1', 'type2'] },
  { label: 'Identity', keys: ['catchRate', 'baseFriendship', 'growthRate', 'baseExperience'] }
];
const groupedPersonalKeys = new Set(personalGroups.flatMap(group => group.keys));
const pokemonSectionIds = {
  personal: 'speciesData', learnsets: 'learnset', evolutions: 'evolutions', compatibility: 'compatibility', tutors: 'tutors'
} satisfies Record<PlaPokemonKind, string>;

export function PlaPokemonEditor(props: PlaEditorProps) {
  const { t, translateLiteral } = useLocalization();
  const editor = usePlaPokemonEditor(props);
  const speciesFieldFinder = useSpeciesFieldFinder();
  const [compatibilitySearch, setCompatibilitySearch] = useState('');
  const [yieldAction, setYieldAction] = useState<'remove' | 'restore' | null>(null);
  const { pages, loading, staging, recordFor, valuesFor, setValue, currentKinds } = editor;
  const navigationRequest = useMemo(() => {
    const target = props.navigationTarget;
    if (target?.section !== 'pokemon') return undefined;
    const sectionId = pokemonSectionIds[target.kind as PlaPokemonKind];
    return sectionId ? { sectionId } : undefined;
  }, [props.navigationTarget]);
  const selected = recordFor('personal');
  const enabled = props.session !== null && editor.isCurrentScope;
  const busy = loading || staging || props.isBusy;
  const fieldLabel = (field: PlaField) => field.key.startsWith('tutor.') ? field.label : t(`pla.field.${field.key}`);
  const input = (field: PlaField, value: string, change: (value: string) => void, disabled: boolean, id: string, label = fieldLabel(field)) => {
    const invalid = !isPlaFieldValueValid(field, value);
    if (field.kind === 'boolean') return <input type="checkbox" id={id} aria-label={label} checked={value === '1'} disabled={disabled} onChange={event => change(event.target.checked ? '1' : '0')} />;
    if (field.choices) return <SearchableOptionInput id={id} ariaLabel={label} ariaInvalid={invalid || undefined} value={value} disabled={disabled} onChange={change}
      options={(field.choices.some(choice => choice.value === value) ? field.choices : [{ value, label: value }, ...field.choices]).map(choice =>
        ['method', 'growthRate'].includes(field.key) && field.choices?.some(item => item.value === choice.value)
          ? { ...choice, label: t(`pla.choice.${field.key}.${choice.value}`) }
          : field.key === 'method' ? { ...choice, label: t('pla.preservedMethod', { id: choice.value }) }
          : field.key === 'form' && choice.value === '65535' ? { ...choice, label: t('pla.keepForm') } : choice)} isFiniteCatalog localizeOptions={false} />;
    return <input type="text" inputMode="numeric" id={id} aria-invalid={invalid || undefined} value={value} disabled={disabled} onChange={event => change(event.target.value)} />;
  };
  const renderField = (kind: PlaPokemonKind, field: PlaField) => {
    const record = recordFor(kind);
    if (!record) return null;
    const value = valuesFor(kind, record)[field.key];
    const changed = value !== record.values[field.key], invalid = changed && !isPlaFieldValueValid(field, value);
    const readOnly = field.readOnly || record.readOnlyFields.includes(field.key);
    return <div className={`path-field editable-field-control ${readOnly ? 'editable-field-disabled' : ''} ${changed ? 'editable-field-changed' : ''} ${invalid ? 'editable-field-invalid' : ''}`} key={field.key}>
      <FieldLabel htmlFor={`pla-${kind}-${field.key}`} label={fieldLabel(field)} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />
      {input(field, value, next => setValue(kind, field.key, next), !enabled || readOnly, `pla-${kind}-${field.key}`)}
      {invalid ? <small className="editable-field-error">{t('pla.invalid')}</small> : changed || readOnly ? <small className="editable-field-status">{translateLiteral(readOnly ? 'Read-only' : 'Changed')}</small> : null}
    </div>;
  };
  const recordName = (label: string) => label.replace(/^\d+[:\s]+/u, '');
  const choiceLabel = (key: string, value: string) => key === 'growthRate' ? t('pla.choice.growthRate.' + value)
    : pages.personal?.fields.find(field => field.key === key)?.choices?.find(choice => choice.value === value)?.label ?? value;
  const records = editor.matchingRecords.map(record => {
    const [species, form] = record.id.split(':').map(Number), values = valuesFor('personal', record);
    return { ...record, personalId: species * 256 + form, speciesId: species, form, name: recordName(record.label), formLabel: record.metadata?.formName ? `${record.metadata.formName} (${form})` : String(form),
      displayId: record.id, type1: choiceLabel('type1', values.type1), type2: choiceLabel('type2', values.type2) };
  });
  const pending = new Set(records.filter(record => currentKinds.some(kind => editor.hasDraft(kind, record))
    || props.session?.pendingEdits.some(edit => edit.owner === 'pla.loose.v1' && currentKinds.some(kind => edit.recordId === `${kind}/${record.id}`)))
    .map(record => record.personalId));
  const values = selected ? valuesFor('personal', selected) : {};
  const selectedIdentity = selected?.id.split(':') ?? [];
  const rowEditor = (kind: 'learnsets' | 'evolutions') => {
    const record = recordFor(kind), page = pages[kind];
    if (!record || !page) return <p className="empty-copy">{t('pla.empty')}</p>;
    const rows = parsePlaRows(valuesFor(kind, record).entries) ?? [];
    const change = (next: string[][]) => setValue(kind, 'entries', JSON.stringify(next.map(row => row.map(value => /^\d+$/u.test(value) ? Number(value) : value))));
    return <PlaPokemonRows key={`${kind}:${record.id}`} kind={kind} rows={rows} fields={page.rowFields} enabled={enabled}
      source={props} recordId={record.id} recordLabel={recordName(record.label)} busy={busy || loading}
      onChange={change} fieldLabel={fieldLabel} renderInput={input}
      canSave={kind === 'evolutions' && enabled && !busy && editor.hasDraft(kind, record) && !editor.invalidKinds.includes(kind)}
      onSave={kind === 'evolutions' ? () => void editor.stage(['evolutions']) : undefined} />;
  };
  const refresh = <button type="button" className="secondary-button compact-button" disabled={loading || busy} onClick={editor.refresh}><RefreshCw size={16} aria-hidden="true" />{translateLiteral('Refresh')}</button>;
  return <><section className="panel wide-panel pla-editor pla-data-editor pla-pokemon-section" aria-labelledby="pla-editor-title">
    <div className="panel-heading"><Dna size={18} aria-hidden="true" /><h2 id="pla-editor-title">{translateLiteral('Pokemon')}</h2>{refresh}</div>
    <SectionedEditorWorkspace sessionKey={JSON.stringify(['pla', 'pokemon', props.paths])}
      initialSection="speciesData" activeSectionRequest={navigationRequest} sectionShortcut={speciesFieldFinder.shortcut} browserVisibleRows={3}
      browser={<PokemonTable records={records} selectedId={records.find(record => record.id === editor.selectedId)?.personalId} pendingIds={pending}
            onSelect={id => editor.setSelectedId(records.find(record => record.personalId === id)?.id ?? null)} formatName={record => record.label} resetKey={editor.query} />}
      browserControls={<PokemonSearchBox disabled={false} value={editor.query} onChange={editor.onQuery} />}
      sessionBar={<EditorSessionBar canEdit={Boolean(selected) && !loading} isEditing={props.session !== null} isStarting={props.isStarting} label="Pokemon" onStart={props.onStart}
          selection={selected ? <div className="pokemon-session-selection">
            {props.renderPokemon?.({ species: selectedIdentity[0], form: selectedIdentity[1] }, recordName(selected.label), pages.personal?.pokemonSpriteNames[selectedIdentity[0]], 'pokemon-session-sprite')}
            <strong data-localization-ignore="true">{selected.label}</strong>
          </div> : null} />}>
      <EditorWorkspaceHeader>
        {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
        {props.session ? <EditorSessionBarActions>
          <button type="button" className="primary-button" aria-busy={staging || undefined} disabled={!enabled || loading || busy || !editor.changedCount(currentKinds) || editor.invalidKinds.some(kind => kind !== 'tutors')} onClick={() => void editor.stage(currentKinds)}>
            {staging ? <RefreshCw className="button-busy-icon" size={16} aria-hidden="true" /> : <Save size={16} aria-hidden="true" />}{translateLiteral(staging ? 'Staging' : 'Stage')}
          </button>
          {editor.nextId ? <button type="button" className="secondary-button" disabled={busy} onClick={editor.reviewNext}>{t('editorDrafts.reviewNext')}</button> : null}
          <button type="button" className="secondary-button" disabled={!enabled || loading || busy || !selected} onClick={() => void editor.stage(currentKinds, true)}><RotateCcw size={16} aria-hidden="true" />{t('pla.restore')}</button>
          <button type="button" className="secondary-button" disabled={busy || !editor.changedCount(currentKinds)} onClick={() => editor.discard(currentKinds)}>{t('pla.discard')}</button>
          <button type="button" className="danger-button" disabled={busy} onClick={() => props.onCancel(() => props.setDrafts({}))}><X size={16} aria-hidden="true" />{translateLiteral('Cancel')}</button>
          <span className="draft-action-summary">{t('editorDrafts.summary.pokemon', { count: editor.outstandingIds.length })}</span>
        </EditorSessionBarActions> : null}
      </EditorWorkspaceHeader>
      {selected ? <>
        <EditorWorkspaceSection id="details" label={t('editorSections.details')}>
          <div className="pokemon-overview-identity">
            <h3 data-localization-ignore="true">{recordName(selected.label)}</h3>
            {selected.metadata?.formName ? <span data-localization-ignore="true">{selected.metadata.formName}</span> : null}
          </div>
          <div className="editor-detail-columns">
            <PokemonDetailsStats stats={speciesStatFields.map(([key]) => ({
              label: t(`pla.field.${key}`), value: values[key], number: /^\d+$/u.test(values[key]) ? Number(values[key]) : null
            }))} />
            <EditorDetailGroup label="Details">
              <dl className="editor-detail-list">
                <EditorDetailValue label="ID">{selected.id}</EditorDetailValue>
                <EditorDetailValue label="Types" dataValue>{[...new Set([choiceLabel('type1', values.type1), choiceLabel('type2', values.type2)])].join(' / ')}</EditorDetailValue>
                {pages.personal?.fields.filter(field => personalGroups[2].keys.includes(field.key)).map(field =>
                  <EditorDetailValue key={field.key} label={fieldLabel(field)}>{choiceLabel(field.key, values[field.key])}</EditorDetailValue>
                )}
              </dl>
            </EditorDetailGroup>
          </div>
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="speciesData" label={t('editorSections.speciesData')}
          modified={editor.hasDraft('personal')} invalid={editor.invalidKinds.includes('personal')}>
          <SpeciesDataMatrix finderRequest={speciesFieldFinder.request} recordName={selected.metadata?.formName
            ? `${recordName(selected.label)} (${selected.metadata.formName})` : recordName(selected.label)} recordId={selected.id}
            groups={[...personalGroups, { label: 'Other fields', keys: pages.personal?.fields
              .filter(field => !groupedPersonalKeys.has(field.key)).map(field => field.key) ?? [] }].map(group => ({
                key: group.label,
                label: translateLiteral(group.label),
                fields: (pages.personal?.fields ?? []).filter(field => group.keys.includes(field.key)).map(field => ({
                  key: field.key, label: fieldLabel(field), inputId: 'pla-personal-' + field.key, value: values[field.key],
                  changed: values[field.key] !== selected.values[field.key], control: renderField('personal', field)
                }))
              }))} />
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="learnset" label={t('pla.kind.learnsets')}
          modified={editor.hasDraft('learnsets')} invalid={editor.invalidKinds.includes('learnsets')}>
            <div className="inspector-block pla-pokemon-learnset-block">{rowEditor('learnsets')}<p className="field-note">{t('pla.notice.learnsets')}</p></div>
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="compatibility" label={t('pla.kind.compatibility')}
          modified={editor.hasDraft('compatibility')} invalid={editor.invalidKinds.includes('compatibility')}>
            <div className="inspector-block pla-pokemon-compatibility-block"><h4>{t('pla.kind.compatibility')}</h4>
              <label className="search-box compatibility-search"><Search size={16} aria-hidden="true" /><input type="search" value={compatibilitySearch} aria-label={t('pla.searchCompatibility')} placeholder={t('pla.searchCompatibility')} onChange={event => setCompatibilitySearch(event.target.value)} /></label>
              <ul className="compatibility-list">{pages.compatibility?.fields.filter(field => fieldLabel(field).toLocaleLowerCase().includes(compatibilitySearch.toLocaleLowerCase())).map(field => {
                const record = recordFor('compatibility');
                return record ? <li key={field.key}><label className="compatibility-toggle">
                  {input(field, valuesFor('compatibility', record)[field.key], value => setValue('compatibility', field.key, value), !enabled, 'pla-compatibility-' + field.key)}
                  <span data-localization-ignore="true">{fieldLabel(field)}</span><small>{field.key.slice(6)}</small>
                </label></li> : null;
              })}</ul><p className="field-note">{t('pla.notice.compatibility')}</p>
            </div>
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="evolutions" label={t('pla.kind.evolutions')}
          modified={editor.hasDraft('evolutions')} invalid={editor.invalidKinds.includes('evolutions')}>
            <div className="inspector-block pokemon-detail-panel pla-pokemon-evolutions-block">{rowEditor('evolutions')}<p className="field-note">{t('pla.notice.evolutions')}</p></div>
        </EditorWorkspaceSection>
      </> : null}
      <EditorWorkspaceSection id="tutors" label={t('pla.kind.tutors')}
        modified={editor.hasDraft('tutors')} invalid={editor.invalidKinds.includes('tutors')}>
          <div className="inspector-block pla-pokemon-tutors-block"><h4>{t('pla.kind.tutors')}</h4><p className="field-note">{t('pla.notice.tutors')}</p>
            <SearchableOptionInput ariaLabel={t('pla.kind.tutors')} value={editor.tutorId ?? ''} disabled={false} onChange={editor.setTutorId}
              options={(pages.tutors?.records ?? []).map(record => ({ value: record.id, label: record.label }))} isFiniteCatalog localizeOptions={false} />
            <div className="editable-field-grid">{pages.tutors?.fields.map(field => renderField('tutors', field))}</div>
            <div className="draft-action-row"><button type="button" className="primary-button" disabled={!enabled || loading || busy || !editor.hasDraft('tutors') || editor.invalidKinds.includes('tutors')} onClick={() => void editor.stage(['tutors'])}><Save size={16} aria-hidden="true" />{translateLiteral('Stage')}</button>
              <button type="button" className="secondary-button" disabled={!enabled || loading || busy} onClick={() => void editor.stage(['tutors'], true)}>{t('pla.restore')}</button>
              <button type="button" className="secondary-button" disabled={busy || !editor.hasDraft('tutors')} onClick={() => editor.discard(['tutors'])}>{t('pla.discard')}</button></div>
          </div>
      </EditorWorkspaceSection>
      <EditorWorkspaceSection id="bulkActions" label={t('editorSections.bulkActions')}>
        <div className="pokemon-toolbar-side pokemon-bulk-actions">
          <div className="pokemon-toolbar-metrics">
            <Metric label={translateLiteral('Loaded records')} value={String(pages.personal?.total ?? 0)} />
            <Metric label={translateLiteral('Pending changes')} value={String(pending.size)} />
          </div>
          <div className="pokemon-toolbar-actions">
            <button type="button" className="primary-button compact-button" disabled={!enabled || loading || busy || editor.dirty} onClick={() => setYieldAction('remove')}><Trash2 size={14} aria-hidden="true" />{translateLiteral('Remove EXP Yield')}</button>
            <button type="button" className="primary-button compact-button" disabled={!enabled || loading || busy || editor.dirty} onClick={() => setYieldAction('restore')}><RefreshCw size={14} aria-hidden="true" />{translateLiteral('Restore EXP Yield')}</button>
          </div>
        </div>
      </EditorWorkspaceSection>
      {!selected && !loading ? <EditorWorkspaceHeader afterBrowser><p className="empty-copy">{t('pla.empty')}</p></EditorWorkspaceHeader> : null}
    </SectionedEditorWorkspace>
  </section>{yieldAction ? <PokemonYieldConfirmationModal action={yieldAction} kind="exp" onCancel={() => setYieldAction(null)} onConfirm={() => {
    const restore = yieldAction === 'restore'; setYieldAction(null); void editor.stageExperienceYield(restore);
  }} /> : null}</>;
}
