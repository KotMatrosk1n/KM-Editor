/* SPDX-License-Identifier: GPL-3.0-only */
import { EditorFieldGroup } from '../../components/EditorRecordDetails';
import { plaFieldHelp } from './plaFieldHelp';
import { plaAreaLabel, plaPokemonLabel } from './plaLabels';
import type { ReactNode } from 'react';
import { Search, Trees } from 'lucide-react';
import type { PlaField, PlaKind, PlaPage, PlaRecord } from '../../bridge/plaProjectBridge';
import { EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, SectionedEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { FieldLabel } from '../../components/FieldLabel';
import { InteractiveTableRow, VirtualTableBody } from '../../components/VirtualTable';
import { Metric } from '../../components/workflowPanels';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization';
import type { PlaPokemonPreview } from './PlaBattleWorkspace';
import type { PlaRowClipboardBinding } from './usePlaRowClipboard';

export type PlaWildKind = 'encounters' | 'spawns' | 'outbreakEncounters' | 'distortionEncounters' | 'distortionSpawns';
export const isPlaWildKind = (kind: PlaKind): kind is PlaWildKind => ['encounters', 'spawns', 'outbreakEncounters', 'distortionEncounters', 'distortionSpawns'].includes(kind);
type Props = {
  sessionKey: string;
  clipboard?: PlaRowClipboardBinding;
  kind: PlaWildKind; page: PlaPage | null; selected: PlaRecord | null;
  controls?: ReactNode; title?: string;
  query: string; offset: number; loading: boolean; sessionBar: ReactNode; actions: ReactNode; refresh: ReactNode;
  onQuery: (value: string) => void; onOffset: (value: number) => void; onSelect: (id: string) => void;
  onKind: (kind: PlaWildKind) => void;
  valuesFor: (record: PlaRecord) => Record<string, string>; hasDraft: (record: PlaRecord) => boolean;
  pendingIds: ReadonlySet<string>; fieldLabel: (field: PlaField) => string;
  renderInput: (field: PlaField) => ReactNode; renderPokemon?: PlaPokemonPreview;
};

export function PlaWildWorkspace({ sessionKey, kind, page, selected, query, offset, loading, sessionBar, actions, refresh, controls, title,
  onQuery, onOffset, onSelect, onKind, valuesFor, hasDraft, pendingIds, fieldLabel, renderInput, renderPokemon, clipboard }: Props) {
  const { t, translateLiteral } = useLocalization();
  const records = page?.records ?? [];
  const isSpawn = kind === 'spawns' || kind === 'distortionSpawns';
  const fields = page?.fields ?? [];
  const values = selected ? valuesFor(selected) : {};
  const profile = (record: PlaRecord) => t(record.metadata.alpha === 'protected' ? 'pla.wild.unknownProfile' : 'pla.wild.' + record.metadata.alpha);
  const name = (record: PlaRecord) => plaPokemonLabel(fields, valuesFor(record), record.metadata.pokemon ?? record.label.split(' / ').at(-1) ?? record.label);
  const status = (record: PlaRecord) => translateLiteral(hasDraft(record) ? 'Draft' : pendingIds.has(record.id) ? 'Pending changes'
    : record.readOnlyFields.length ? 'Read-only' : 'Editable');
  const groups = isSpawn ? records.map(record => ({ value: record.id, label: record.metadata.pokemon ?? '', area: record.metadata.area, table: record.metadata.table })) : page?.groups ?? [];
  const groupsFor = (record: PlaRecord) => [
    { key: 'pokemon', title: translateLiteral('Pokemon'), help: t('pla.wild.levelHelp', { min: record.metadata.defaultMin, max: record.metadata.defaultMax }),
      fields: fields.filter(field => ['species', 'form', 'wildMin', 'wildMax', 'wildWeight'].includes(field.key)) },
    { key: 'traits', title: t('pla.wild.traits'), help: t('pla.wild.traitHelp'),
      fields: fields.filter(field => field.key.startsWith('effort') || field.key === 'perfectIvs') },
    { key: 'eligibility', title: t('pla.wild.eligibility'), help: t('pla.wild.multiplierHelp'), fields: fields.filter(field => field.kind === 'decimal') },
    { key: 'counts', title: t('pla.kind.spawns'), help: t('pla.wild.spawnHelp'), fields: fields.filter(field => field.key.startsWith('spawn')) }
  ].filter(group => group.fields.length);
  const levels = (record: PlaRecord) => {
    const { wildMin, wildMax } = valuesFor(record);
    const min = wildMin === '0' ? record.metadata.defaultMin : wildMin;
    const max = wildMax === '0' ? record.metadata.defaultMax : wildMax;
    if (!min || !max) return '—';
    return min === max ? min : `${min}–${max}`;
  };
  const tabs = ['encounters', 'spawns'] as const;
  return <section className="panel wide-panel pla-wild-editor" aria-labelledby="pla-wild-heading">
    <div className="panel-heading"><Trees size={18} aria-hidden="true" /><h2 id="pla-wild-heading">{title ?? translateLiteral('Wild Encounters')}</h2>{refresh}</div>
    <SectionedEditorWorkspace sessionKey={sessionKey + ':' + kind} initialSection="data" compactFields
      browserVisibleRows={4} sessionBar={<>{sessionBar}{actions}</>}
      browserControls={<>
        <div className="items-toolbar encounters-toolbar">
      <label className="search-box items-search"><Search size={18} aria-hidden="true" /><input type="search" aria-label={translateLiteral('Search encounters')}
        placeholder={translateLiteral('Search encounters')} value={query} onChange={event => onQuery(event.target.value)} /></label>
      <Metric label={translateLiteral(isSpawn ? 'Records' : 'Loaded tables')} value={String(page?.total ?? 0)} />
      <Metric label={translateLiteral('Pending changes')} value={String(pendingIds.size)} />
    </div>
    {controls ?? <nav className="text-category-browser" aria-label={t('pla.editor')}><div className="text-category-tabs" role="tablist" aria-label={t('pla.editor')}>
      {tabs.map((value, index) => <button type="button" key={value} role="tab" aria-selected={kind === value} tabIndex={kind === value ? 0 : -1}
        className={kind === value ? 'is-active' : ''} onClick={() => onKind(value)}
        onKeyDown={event => {
          const next = event.key === 'ArrowLeft' || event.key === 'ArrowRight' ? 1 - index : event.key === 'Home' ? 0 : event.key === 'End' ? 1 : null;
          if (next === null) return;
          event.preventDefault(); onKind(tabs[next]); (event.currentTarget.parentElement?.children[next] as HTMLButtonElement | undefined)?.focus();
        }}>{t('pla.kind.' + value)}</button>)}
    </div></nav>}
      </>}
      browser={
        <div className="encounters-table" role="table" aria-label={translateLiteral('Encounter tables')} aria-colcount={2} aria-rowcount={groups.length + 1}>
        <div className="encounters-row pla-wild-row encounters-row-heading" role="row"><span role="columnheader">{translateLiteral('Location')}</span><span role="columnheader">{translateLiteral('ID')}</span></div>
        <VirtualTableBody items={groups} getKey={group => group.value} resetKey={kind + ':' + query} renderRow={group =>
          <InteractiveTableRow className={'encounters-row pla-wild-row ' + ((isSpawn ? selected?.id === group.value : offset === Number(group.value)) ? 'encounters-row-selected' : '')}
            aria-selected={isSpawn ? selected?.id === group.value : offset === Number(group.value)} onClick={() => isSpawn ? onSelect(group.value) : onOffset(Number(group.value))}>
            <span role="cell" data-localization-ignore="true">{plaAreaLabel(t, group.area ?? group.label.split('/')[0])}
              {group.area && group.label ? <small className="pla-record-detail" title={group.label}>{group.label}</small> : null}</span>
            <span role="cell" data-localization-ignore="true">{group.table ?? group.label.split('/')[1]}</span>
          </InteractiveTableRow>} />
      </div>
      }
      browserFooter={
        isSpawn ? <div className="pla-pagination">
      <button type="button" className="secondary-button" disabled={loading || offset === 0} onClick={() => onOffset(Math.max(0, offset - 100))}>{translateLiteral('Previous')}</button>
      <span>{Math.min(offset + 1, page?.total ?? 0)} / {page?.total ?? 0}</span>
      <button type="button" className="secondary-button" disabled={loading || offset + 100 >= (page?.total ?? 0)} onClick={() => onOffset(offset + 100)}>{translateLiteral('Next')}</button>
    </div> : null
      }>
      <EditorWorkspaceHeader>
        {loading ? <p className="field-note" role="status">{translateLiteral('Loading')}</p> : null}
      </EditorWorkspaceHeader>
      {selected ? <>
        <EditorWorkspaceHeader afterBrowser>
          <EditorRecordHeading name={plaAreaLabel(t, selected.metadata.area)} id={selected.metadata.table ?? selected.id} />
        </EditorWorkspaceHeader>
        <EditorWorkspaceSection id="details" label={t('editorSections.details')}>
          <ContextHelp label={translateLiteral('Encounter slots')}>{t('pla.wild.notice')}</ContextHelp>
          <dl className="item-provenance-list">
            <div><dt>{translateLiteral('Location')}</dt><dd data-localization-ignore="true">{plaAreaLabel(t, selected.metadata.area)}</dd></div>
            <div><dt>{translateLiteral('ID')}</dt><dd>{selected.id}</dd></div>
            <div><dt>{translateLiteral('Table')}</dt><dd>{selected.metadata.table}</dd></div>
            {!isSpawn ? <><div><dt>{t('pla.wild.profile')}</dt><dd>{profile(selected)}</dd></div>
              <div><dt>{t('pla.wild.shared')}</dt><dd>{selected.metadata.consumers}</dd></div></> : null}
          </dl>
          {selected.relatedRecords.length ? <details><summary>{t('pla.wild.references')} ({selected.relatedRecords.length})</summary>
            <ul className="pla-wild-references">
              {selected.relatedRecords.map(related => <li key={related.id} data-localization-ignore="true">{related.label}{related.label !== related.id ? ` (${related.id})` : ''}</li>)}
            </ul>
          </details> : null}
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="data" label={isSpawn ? t('pla.kind.spawns') : translateLiteral('Encounter slots')} modified={hasDraft(selected)}>
          <div className={isSpawn ? undefined : 'raid-slot-workspace'}>
            {!isSpawn ? <section className="pla-wild-slots">
              <div className="editor-record-context"><strong>{translateLiteral('Encounter slots')} ({records.length})</strong>{clipboard?.content}</div>
              <div className="editor-slot-browser"><table className="editor-record-table" aria-label={translateLiteral('Encounter slots')}>
                <thead><tr><th scope="col">{translateLiteral('Slot')}</th><th scope="col">{translateLiteral('Level')}</th>
                  <th scope="col" data-numeric="true">{t('pla.field.wildWeight')}</th><th scope="col">{translateLiteral('Status')}</th></tr></thead>
                <tbody>{records.map(record => <tr key={record.id} data-selected={selected.id === record.id} onClick={() => onSelect(record.id)}>
                  <th scope="row"><button type="button" className="editor-record-table-select" aria-pressed={selected.id === record.id} {...clipboard?.bind(record.id)}
                    aria-label={translateLiteral('Slot') + ' ' + record.metadata.slot + ': ' + name(record)} onClick={event => { event.stopPropagation(); onSelect(record.id); }}>
                    <span>{record.metadata.slot}</span>
                    {renderPokemon?.(valuesFor(record), name(record), page?.pokemonSpriteNames[valuesFor(record).species], 'slot-tab-sprite')}
                    <span data-localization-ignore="true">{name(record)}<small className="pla-wild-profile">{profile(record)}</small></span>
                  </button></th>
                  <td>{levels(record)}</td><td data-numeric="true">{valuesFor(record).wildWeight}</td><td>{status(record)}</td>
                </tr>)}</tbody>
              </table></div>
            </section> : null}
            <div className="pla-wild-inspector">
              {!isSpawn ? <div className="editor-record-context">
                <strong data-localization-ignore="true">{name(selected)}</strong><span className="muted">{translateLiteral('Slot')} {selected.metadata.slot}</span>
              </div> : null}
              {selected.readOnlyFields.length ? <p className="field-note">{t('pla.wild.protected')}</p> : null}
              {selected.metadata.condition === 'progression' ? <p className="field-note">{t('pla.wild.condition')}</p> : null}
              <div className="editor-task-groups">{groupsFor(selected).map(group =>
                <EditorFieldGroup className="editable-field-group" key={group.key} label={group.title} headingValue={<ContextHelp label={group.title}>{group.help}</ContextHelp>}>
                  <div className="pokemon-instance-field-list">{group.fields.map(field => <div className="editable-field-control" key={field.key}>
                    <FieldLabel htmlFor={'pla-field-' + field.key} label={fieldLabel(field)} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />
                    {renderInput(field)}
                    {field.kind === 'decimal' && values[field.key] === '-1' ? <small className="field-note">{values.species === selected.values.species && values.form === selected.values.form
                      ? t('pla.wild.inherited', { value: selected.metadata['default.' + field.key] }) : t('pla.wild.inheritedPending')}</small> : null}
                  </div>)}</div>
                </EditorFieldGroup>)}</div>
            </div>
          </div>
        </EditorWorkspaceSection>
      </> : <p className="empty-copy">{translateLiteral('No results')}</p>}
    </SectionedEditorWorkspace>
  </section>;
}
