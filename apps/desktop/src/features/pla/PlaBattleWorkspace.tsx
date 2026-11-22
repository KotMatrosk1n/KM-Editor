/* SPDX-License-Identifier: GPL-3.0-only */
import { plaFieldHelp } from './plaFieldHelp';
import { plaPokemonLabel } from './plaLabels';
import type { ReactNode } from 'react';
import { Search } from 'lucide-react';
import type { PlaField, PlaPage, PlaRecord } from '../../bridge/plaProjectBridge';
import { EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, SectionedEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { FieldLabel } from '../../components/FieldLabel';
import { InteractiveTableRow, VirtualTableBody } from '../../components/VirtualTable';
import { Metric } from '../../components/workflowPanels';
import { getWorkbenchCapabilityRegistration } from '../../workbench/capabilityRegistry';
import { useLocalization } from '../../localization';
import type { PlaRowClipboardBinding } from './usePlaRowClipboard';

export type PlaPokemonPreview = (values: Record<string, string>, name: string, spriteName: string | undefined, className: string) => ReactNode;
type Props = {
  sessionKey: string;
  clipboard?: PlaRowClipboardBinding;
  kind: 'trainers' | 'gifts' | 'static'; page: PlaPage | null; selected: PlaRecord | null;
  query: string; offset: number; loading: boolean; sessionBar: ReactNode; actions: ReactNode; refresh: ReactNode;
  onQuery: (query: string) => void; onOffset: (offset: number) => void; onSelect: (id: string) => void;
  valuesFor: (record: PlaRecord) => Record<string, string>; hasDraft: (record: PlaRecord) => boolean;
  pendingIds: ReadonlySet<string>; fieldLabel: (field: PlaField) => string;
  renderInput: (field: PlaField) => ReactNode; renderPokemon?: PlaPokemonPreview;
};
const groupId = (record: PlaRecord) => record.id.slice(0, record.id.lastIndexOf(':'));

export function PlaBattleWorkspace({ sessionKey, kind, page, selected, query, offset, loading, sessionBar, actions, refresh,
  onQuery, onOffset, onSelect, valuesFor, hasDraft, pendingIds, fieldLabel, renderInput, renderPokemon, clipboard }: Props) {
  const { t, translateLiteral } = useLocalization();
  const section = kind === 'gifts' ? 'giftPokemon' : kind === 'static' ? 'staticEncounters' : 'trainers';
  const registration = getWorkbenchCapabilityRegistration(section);
  const Icon = registration.icon;
  const title = translateLiteral(registration.label);
  const records = page?.records ?? [];
  const groups = new Map<string, PlaRecord[]>();
  for (const record of records) {
    const key = kind === 'trainers' ? groupId(record) : record.id;
    groups.set(key, [...(groups.get(key) ?? []), record]);
  }
  const rows = [...groups.entries()].map(([id, party]) => ({ id, party, first: party[0] }));
  const selectedGroup = selected ? groupId(selected) : null;
  const party = kind === 'trainers' ? groups.get(selectedGroup ?? '') ?? [] : [];
  const pageSize = kind === 'trainers' ? 16 : 100;
  const trainerName = (record: PlaRecord) => record.metadata.trainerName?.trim() || t('pla.unnamedTrainer');
  const pokemonName = (record: PlaRecord) => {
    const value = valuesFor(record);
    return page?.fields.find(field => field.key === 'species')?.choices?.find(choice => choice.value === value.species)?.label
      ?? record.label.slice(record.label.indexOf(']: ') + 3).replace(/^\d+\s+/u, '').replace(/ \(\d+\)$/u, '');
  };
  const pokemonLabel = (record: PlaRecord) => plaPokemonLabel(page?.fields ?? [], valuesFor(record), record.label.split(']: ').at(-1) ?? record.label);
  const isProtected = (record: PlaRecord) => record.readOnlyFields.includes('battleLevel');
  const status = (record: PlaRecord) => translateLiteral(hasDraft(record) ? 'Draft' : pendingIds.has(record.id) ? 'Pending changes' : isProtected(record) ? 'Read-only' : 'Editable');
  const source = selected ? kind === 'trainers' ? `bin/trainer/${selectedGroup}.bin`
    : `bin/pokemon/data/${kind === 'gifts' ? 'poke_add' : 'poke_event_encount'}.bin` : '';
  const fieldGroups = [
    { title: translateLiteral('Pokemon'), fields: page?.fields.filter(field => ['species', 'form', 'battleLevel', 'gender'].includes(field.key)) ?? [] },
    { title: t('pla.group.effort'), fields: page?.fields.filter(field => field.key.startsWith('effort')) ?? [] },
    { title: translateLiteral('Moves'), fields: page?.fields.filter(field => /^(move|mastered)[1-4]$/u.test(field.key)) ?? [] }
  ];
  return <section className="panel wide-panel pla-battle-editor" aria-labelledby="pla-battle-heading">
    <div className="panel-heading"><Icon size={18} aria-hidden="true" /><h2 id="pla-battle-heading">{title}</h2>{refresh}</div>
    <SectionedEditorWorkspace sessionKey={sessionKey + ':' + kind} initialSection="data" pinnedBrowser compactFields
      browserVisibleRows={4} sessionBar={<>{sessionBar}{actions}</>}
      browserControls={<>
        <div className="items-toolbar trainers-toolbar">
      <label className="search-box items-search"><Search size={18} aria-hidden="true" />
        <input aria-label={translateLiteral('Search')} placeholder={translateLiteral('Search')} type="search" value={query} onChange={event => onQuery(event.target.value)} />
      </label>
      <Metric label={translateLiteral(kind === 'trainers' ? 'Loaded trainers' : kind === 'gifts' ? 'Loaded gifts' : 'Loaded encounters')} value={String(page?.total ?? 0)} />
      <Metric label={translateLiteral('Pending changes')} value={String(pendingIds.size)} />
    </div>
      </>}
      browser={
        <div className="trainers-table" role="table" aria-label={title} aria-colcount={5} aria-rowcount={rows.length + 1}>
        <div className="trainers-row pla-battle-row trainers-row-heading" role="row">
          {(kind === 'trainers' ? ['ID', 'Name', 'Team', 'Level', 'Status'] : ['ID', 'Species', 'Level', 'Event label', 'Status'])
            .map(column => <span role="columnheader" key={column}>{translateLiteral(column)}</span>)}
        </div>
        <VirtualTableBody getKey={row => row.id} items={rows} resetKey={`${kind}:${query}:${offset}`} renderRow={(row, index) => {
          const active = kind === 'trainers' ? selectedGroup === row.id : selected?.id === row.id;
          const pending = row.party.some(record => pendingIds.has(record.id) || hasDraft(record));
          const levels = row.party.map(record => Number(valuesFor(record).battleLevel));
          const level = Math.min(...levels) === Math.max(...levels) ? String(levels[0]) : `${Math.min(...levels)} - ${Math.max(...levels)}`;
          return <InteractiveTableRow className={`trainers-row pla-battle-row ${active ? 'trainers-row-selected' : ''} ${pending ? 'trainers-row-pending' : ''} ${row.party.every(isProtected) ? 'trainers-row-read-only' : ''}`}
            aria-selected={active} onClick={() => onSelect(row.first.id)}>
            <span role="cell" title={row.id}>{offset + index + 1}</span>
            <span role="cell" data-localization-ignore="true" title={kind === 'trainers' ? row.id : pokemonLabel(row.first)}>{kind === 'trainers'
              ? <>{trainerName(row.first)}<small className="pla-record-detail">{row.first.metadata.trainerClass}</small></> : pokemonLabel(row.first)}</span>
            <span role="cell" title={kind === 'trainers' ? row.party.map(pokemonLabel).join(', ') : undefined}>{kind === 'trainers' ? <>{row.party.length}<small className="pla-record-detail">{row.party.map(pokemonLabel).join(', ')}</small></> : valuesFor(row.first).battleLevel}</span>
            <span role="cell" title={kind === 'trainers' ? level : groupId(row.first)}>{kind === 'trainers' ? level : groupId(row.first)}</span>
            <span role="cell">{status(row.party.find(hasDraft) ?? row.party.find(record => pendingIds.has(record.id)) ?? row.first)}</span>
          </InteractiveTableRow>;
        }} />
      </div>
      }
      browserFooter={
        <div className="pla-pagination">
      <button type="button" className="secondary-button compact-button" disabled={offset === 0 || loading} onClick={() => onOffset(Math.max(0, offset - pageSize))}>{translateLiteral('Previous')}</button>
      <span>{page ? `${Math.min(offset + 1, page.total)} - ${Math.min(offset + pageSize, page.total)} / ${page.total}` : ''}</span>
      <button type="button" className="secondary-button compact-button" disabled={!page || offset + pageSize >= page.total || loading} onClick={() => onOffset(offset + pageSize)}>{translateLiteral('Next')}</button>
    </div>
      }>
      <EditorWorkspaceHeader>
        {loading ? <p className="field-note" role="status">{translateLiteral('Loading')}</p> : null}
      </EditorWorkspaceHeader>
      {selected ? <>
        <EditorWorkspaceHeader afterBrowser>
          <EditorRecordHeading name={kind === 'trainers' ? trainerName(selected) : pokemonLabel(selected)} id={selected.id} />
        </EditorWorkspaceHeader>
        <EditorWorkspaceSection id="details" label={t('editorSections.details')}>
          <p className="field-note">{t(`pla.notice.${kind}`)}</p>
          {kind !== 'trainers' ? <div className="pokemon-summary-card">
            {renderPokemon?.(valuesFor(selected), pokemonName(selected), page?.pokemonSpriteNames[valuesFor(selected).species], 'pokemon-summary-sprite')}
            <div className="pokemon-summary-main"><strong className="pokemon-summary-title">{pokemonLabel(selected)}</strong>
              <span className="pokemon-summary-subtitle">{translateLiteral('Level')} {valuesFor(selected).battleLevel}</span></div>
          </div> : null}
          <dl className="item-provenance-list"><div><dt>{translateLiteral(kind === 'trainers' ? 'Name' : 'Event label')}</dt><dd>{kind === 'trainers' ? trainerName(selected) : selectedGroup}</dd></div>
            {kind === 'trainers' ? <><div><dt>{t('pla.trainerClass')}</dt><dd>{selected.metadata.trainerClass || t('pla.unresolvedTrainerClass')}</dd></div>
              <div><dt>{translateLiteral('ID')}</dt><dd>{selectedGroup}</dd></div></> : null}
            <div><dt>{translateLiteral('Data file')}</dt><dd>{source}</dd></div></dl>
          {kind === 'trainers' && !selected.metadata.trainerName?.trim() ? <p className="field-note">{t('pla.unnamedTrainerHelp')}</p> : null}
          {selected.readOnlyFields.length > 0 ? <section className="technical-tool-notice technical-tool-notice-info pla-battle-notice">
            <p>{t(isProtected(selected) ? 'pla.protectedBattle' : 'pla.fixedBattleIdentity')}</p>
          </section> : null}
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="data" label={translateLiteral(kind === 'trainers' ? 'Party' : 'Pokemon')} modified={hasDraft(selected)}>
          <div className="trainer-edit-form">
            {clipboard?.content}
            {kind === 'trainers' ? <div className="trainer-party-card-grid" aria-label={translateLiteral('Trainer party Pokemon')}>
              {party.map(record => <button type="button" key={record.id} className="trainer-party-card" aria-pressed={selected.id === record.id} {...clipboard?.bind(record.id)}
                aria-label={`${translateLiteral('Slot')} ${Number(record.id.slice(record.id.lastIndexOf(':') + 1)) + 1}: ${pokemonLabel(record)}`}
                onClick={() => onSelect(record.id)}>
                {renderPokemon?.(valuesFor(record), pokemonName(record), page?.pokemonSpriteNames[valuesFor(record).species], 'trainer-party-sprite')}
                <strong data-localization-ignore="true">{pokemonLabel(record)}</strong>
                <span>{translateLiteral('Level')} {valuesFor(record).battleLevel}{hasDraft(record) ? ` (${translateLiteral('Draft')})` : ''}</span>
              </button>)}
            </div> : null}
            <div className="trainer-party-edit-stack"><div className="editable-field-groups">
              {fieldGroups.map(group => <fieldset className="editable-field-group" key={group.title}><legend>{group.title}</legend><div className="editable-field-grid">
                {group.fields.map(field => {
                  const changed = valuesFor(selected)[field.key] !== selected.values[field.key];
                  const readOnly = field.readOnly || selected.readOnlyFields.includes(field.key);
                  return <div className={`path-field editable-field-control ${changed ? 'editable-field-changed' : ''} ${readOnly ? 'editable-field-disabled' : ''}`} key={field.key}>
                    <FieldLabel htmlFor={`pla-field-${field.key}`} label={fieldLabel(field)} help={readOnly ? t(isProtected(selected) ? 'pla.protectedBattle' : 'pla.fixedBattleIdentity') : plaFieldHelp(t, kind, field, fieldLabel(field))} />
                    {renderInput(field)}
                    {readOnly || changed ? <small className="editable-field-status">{translateLiteral(readOnly ? 'Read-only' : 'Changed')}</small> : null}
                  </div>;
                })}
              </div></fieldset>)}
            </div></div>
          </div>
        </EditorWorkspaceSection>
      </> : <p className="empty-copy">{t('pla.empty')}</p>}
    </SectionedEditorWorkspace>
  </section>;
}
