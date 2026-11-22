/* SPDX-License-Identifier: GPL-3.0-only */
import { plaFieldHelp } from './plaFieldHelp';
import { plaAreaLabel } from './plaLabels';
import type { ReactNode } from 'react';
import { Search } from 'lucide-react';
import type { PlaField, PlaKind, PlaPage, PlaRecord } from '../../bridge/plaProjectBridge';
import { EditorRecordHeading, EditorWorkspaceHeader, EditorWorkspaceSection, SectionedEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import { FieldLabel } from '../../components/FieldLabel';
import { InteractiveTableRow, VirtualTableBody } from '../../components/VirtualTable';
import { Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import { getWorkbenchCapabilityRegistration } from '../../workbench/capabilityRegistry';
import { workbenchSectionLocalization } from '../../workbench/sectionLocalization';

export type PlaEconomyKind = 'items' | 'crafting' | 'shops' | 'ginkgo' | 'drops' | 'research' | 'ranks'
  | 'outbreaks' | 'massiveOutbreaks' | 'outbreakGroups' | 'outbreakWaves' | 'outbreakDurations' | 'distortionItems' | 'weather';
export const isPlaEconomyKind = (kind: PlaKind): kind is PlaEconomyKind => ['items', 'crafting', 'shops', 'ginkgo', 'drops', 'research', 'ranks',
  'outbreaks', 'massiveOutbreaks', 'outbreakGroups', 'outbreakWaves', 'outbreakDurations', 'distortionItems', 'weather'].includes(kind);
type Props = {
  sessionKey: string;
  kind: PlaEconomyKind; page: PlaPage | null; selected: PlaRecord | null; query: string; offset: number; loading: boolean;
  controls: ReactNode; sessionBar: ReactNode; actions: ReactNode; refresh: ReactNode;
  onQuery: (query: string) => void; onOffset: (offset: number) => void; onSelect: (id: string) => void;
  valuesFor: (record: PlaRecord) => Record<string, string>; hasDraft: (record: PlaRecord) => boolean;
  pendingIds: ReadonlySet<string>; fieldLabel: (field: PlaField) => string; renderInput: (field: PlaField) => ReactNode;
  renderRecordInput: (record: PlaRecord, field: PlaField) => ReactNode;
};

export function PlaEconomyWorkspace({ sessionKey, kind, page, selected, query, offset, loading, controls, sessionBar, actions, refresh,
  onQuery, onOffset, onSelect, valuesFor, hasDraft, pendingIds, fieldLabel, renderInput, renderRecordInput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const section = kind === 'ginkgo' ? 'shops' : kind === 'drops' ? 'pokemonDrops' : kind === 'ranks' ? 'research'
    : kind === 'massiveOutbreaks' || kind === 'outbreakGroups' || kind === 'outbreakWaves' || kind === 'outbreakDurations' ? 'outbreaks'
      : kind === 'distortionItems' ? 'distortions' : kind;
  const registration = getWorkbenchCapabilityRegistration(section);
  const Icon = registration.icon;
  const isShop = kind === 'shops';
  const step = isShop ? 3 : 100;
  const records = page?.records ?? [];
  const groups = new Map<string, PlaRecord[]>();
  for (const record of records) {
    const key = isShop ? record.metadata.inventory : record.id;
    groups.set(key, [...(groups.get(key) ?? []), record]);
  }
  const browserRecords = [...groups.values()].map(group => group[0]);
  const fields = page?.fields ?? [];
  const isProtected = (record: PlaRecord) => fields.every(field => field.readOnly || record.readOnlyFields.includes(field.key));
  const status = (record: PlaRecord) => translateLiteral(hasDraft(record) ? 'Draft' : pendingIds.has(record.id) ? 'Pending changes' : isProtected(record) ? 'Read-only' : 'Editable');
  const choice = (key: string, value: string) => value === '0' && /[Ii]tem|ingredient/u.test(key) ? translateLiteral('None')
    : fields.find(field => field.key === key)?.choices?.find(option => option.value === value)?.label ?? value;
  const name = (record: PlaRecord) => kind === 'ranks' ? t('pla.rankLabel', { rank: record.id })
    : kind === 'weather' ? plaAreaLabel(t, record.id)
    : kind === 'outbreaks' || kind === 'massiveOutbreaks' ? plaAreaLabel(t, record.metadata.area)
    : kind === 'outbreakDurations' ? t('pla.event.durationLabel', { seconds: valuesFor(record).durationSeconds })
      : record.metadata.empty === '1' ? t('pla.event.emptySelection') : record.label;
  const summary = (record: PlaRecord) => {
    const values = valuesFor(record);
    if (kind === 'weather') return t('pla.weather.total', { count: Object.values(values).reduce((sum, value) => sum + (Number(value) || 0), 0) });
    if (isShop) return String(groups.get(record.metadata.inventory)?.length ?? 0);
    if (kind === 'items' || kind === 'ginkgo') return values.price;
    if (kind === 'crafting') return `${choice('resultItem', values.resultItem)} × ${values.resultQuantity}`;
    if (kind === 'research') {
      const task = [0, 1, 2, 3, 4, 5, 13, 14, 15, 18, 19].includes(Number(record.metadata.task))
        ? t(`pla.task.${record.metadata.task}`) : t('pla.unknownTask', { id: record.metadata.task });
      return [task, record.metadata.move, record.metadata.moveType].filter(Boolean).join(': ');
    }
    if (kind === 'ranks') return values.requiredPoints;
    if (values.selectionWeight !== undefined) return values.selectionWeight;
    if (values.outbreakChance !== undefined) return values.outbreakChance + '%';
    return String(record.relatedRecords.length);
  };
  const groupStatus = (record: PlaRecord) => {
    const inventory = isShop ? groups.get(record.metadata.inventory) ?? [record] : [record];
    return status(inventory.find(hasDraft) ?? inventory.find(record => pendingIds.has(record.id))
      ?? inventory.find(record => !isProtected(record)) ?? record);
  };
  const groupFields = kind === 'massiveOutbreaks' ? [
    { title: t('pla.kind.massiveOutbreaks'), fields: fields.filter(field => ['outbreakChance', 'locationsMin', 'locationsMax', 'rareOneCount', 'rareTwoCount'].includes(field.key)) },
    { title: t('pla.related.initial'), fields: fields.filter(field => field.key.startsWith('firstWave')) },
    { title: t('pla.related.followup'), fields: fields.filter(field => field.key.startsWith('secondWave')) },
    { title: t('pla.event.bonuses'), fields: fields.filter(field => ['extraBonusChance', 'starChance', 'berryChance', 'shinyRolls'].includes(field.key)) }
  ] : kind === 'crafting' ? [
    { title: t('pla.group.result'), fields: fields.filter(field => field.key.startsWith('result')) },
    ...[1, 2, 3, 4].map(slot => ({ title: t('pla.ingredientSlot', { slot }), fields: fields.filter(field => field.key === `ingredient${slot}` || field.key === `quantity${slot}`) }))
  ] : kind === 'research' ? [
    { title: t('pla.group.thresholds'), fields: fields.filter(field => field.key.startsWith('threshold')) },
    { title: t('pla.group.points'), fields: fields.filter(field => !field.key.startsWith('threshold')) }
  ] : kind === 'drops' ? [
    { title: t('pla.group.regularDrop'), fields: fields.filter(field => field.key.startsWith('regular')) },
    { title: t('pla.group.rareDrop'), fields: fields.filter(field => field.key.startsWith('rare')) }
  ] : [{ title: t(`pla.kind.${kind}`), fields }];
  const selectedGroup = selected && isShop ? groups.get(selected.metadata.inventory) ?? [] : [];
  return <section className={`panel wide-panel pla-editor pla-economy-editor pla-economy-${kind}`} aria-labelledby="pla-editor-title">
    <div className="panel-heading"><Icon size={18} aria-hidden="true" /><h2 id="pla-editor-title">{t(workbenchSectionLocalization[section].label)}</h2>{refresh}</div>
    <SectionedEditorWorkspace sessionKey={sessionKey + ':' + kind} initialSection="data" pinnedBrowser compactFields
      browserVisibleRows={4} sessionBar={<>{sessionBar}{actions}</>}
      browserControls={<>
        {controls}
    <div className="items-toolbar"><label className="search-box items-search"><Search size={18} aria-hidden="true" />
      <input type="search" aria-label={translateLiteral('Search')} placeholder={translateLiteral('Search')} value={query} onChange={event => onQuery(event.target.value)} /></label>
      <Metric label={translateLiteral('Loaded records')} value={String(page?.total ?? 0)} />
      <Metric label={translateLiteral('Pending changes')} value={String(pendingIds.size)} /></div>
      </>}
      browser={
        <div className={isShop ? 'shops-table' : 'items-table'} role="table" aria-label={t(`pla.kind.${kind}`)} aria-colcount={4} aria-rowcount={browserRecords.length + 1}>
        <div className="items-row items-row-heading pla-economy-row" role="row">{['ID', 'Name', isShop ? 'Inventory' : 'Summary', 'Status'].map(column => <span role="columnheader" key={column}>{translateLiteral(column)}</span>)}</div>
        <VirtualTableBody items={browserRecords} getKey={record => record.id} resetKey={`${kind}:${offset}:${query}`}
          renderRow={record => <InteractiveTableRow aria-selected={isShop ? selected?.metadata.inventory === record.metadata.inventory : selected?.id === record.id}
            className={`items-row pla-economy-row ${(isShop ? selected?.metadata.inventory === record.metadata.inventory : selected?.id === record.id) ? 'items-row-selected' : ''}`}
            onClick={() => onSelect(record.id)}>
            <span role="cell" data-localization-ignore="true" title={record.id}>{isShop ? record.metadata.inventory : record.id}</span>
            <span role="cell" data-localization-ignore="true" title={isShop ? t('pla.shopLabel', { id: record.metadata.inventory }) : name(record)}>{isShop ? t('pla.shopLabel', { id: record.metadata.inventory }) : name(record)}</span>
            <span role="cell" data-localization-ignore="true" title={summary(record)}>{summary(record)}</span><span role="cell">{groupStatus(record)}</span>
          </InteractiveTableRow>} />
      </div>
      }
      browserFooter={
        <nav className="text-page-bar pla-pagination" aria-label={t('text.pagination.ariaLabel')}>
      <span>{page ? `${Math.min(offset + 1, page.total)} - ${Math.min(offset + step, page.total)} / ${page.total}` : ''}</span><div>
        <button type="button" className="secondary-button compact-button" disabled={offset === 0 || loading} onClick={() => onOffset(Math.max(0, offset - step))}>{translateLiteral('Previous')}</button>
        <button type="button" className="secondary-button compact-button" disabled={!page || offset + step >= page.total || loading} onClick={() => onOffset(offset + step)}>{translateLiteral('Next')}</button>
      </div>
    </nav>
      }>
      <EditorWorkspaceHeader>
        {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
      </EditorWorkspaceHeader>
      {selected ? <>
        <EditorWorkspaceHeader afterBrowser>
          <EditorRecordHeading name={isShop ? t('pla.shopLabel', { id: selected.metadata.inventory }) : name(selected)} id={selected.id} />
        </EditorWorkspaceHeader>
        <EditorWorkspaceSection id="details" label={t('editorSections.details')}>
          <p className="field-note">{t(`pla.notice.${kind}`)}</p>
          <dl className="item-provenance-list"><div><dt>{translateLiteral('Name')}</dt><dd data-localization-ignore="true">{name(selected)}</dd></div>
            <div><dt>{translateLiteral('Status')}</dt><dd>{groupStatus(selected)}</dd></div>
            {Object.entries(selected.metadata).filter(([key]) => !['protected', 'areaName'].includes(key)).map(([key, value]) => <div key={key}><dt>{t(`pla.meta.${key}`)}</dt><dd data-localization-ignore="true">{key === 'task' ? summary(selected)
              : key === 'area' ? plaAreaLabel(t, value) : key === 'category' ? t('pla.event.category.' + value) : key === 'empty' ? translateLiteral(value === '1' ? 'Yes' : 'No') : value}</dd></div>)}
          </dl>
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="data" label={isShop ? translateLiteral('Inventory') : kind === 'items' ? translateLiteral('Item Data') : t(`pla.kind.${kind}`)} modified={hasDraft(selected)}>
          {isShop ? <div className="shop-inventory-editor-grid" role="table" aria-label={translateLiteral('Inventory')}>
            <div className="shop-inventory-editor-row shop-inventory-editor-heading pla-shop-slot-row" role="row"><span role="columnheader">{translateLiteral('Slot')}</span><span role="columnheader">{translateLiteral('Item')}</span><span role="columnheader">{translateLiteral('Status')}</span></div>
            {selectedGroup.map((record, index) => <div key={record.id} role="row" onClick={() => onSelect(record.id)}
              className={`shop-inventory-editor-row pla-shop-slot-row ${record.id === selected.id ? 'shop-inventory-editor-row-selected' : ''}`}>
              <span role="cell">{index + 1}</span><div role="cell">{fields.map(field => <div className="path-field editable-field-control" key={field.key}>
                <FieldLabel htmlFor={`pla-shop-${record.id}-${field.key}`} label={`${fieldLabel(field)} ${index + 1}`} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />{renderRecordInput(record, field)}
              </div>)}</div><span role="cell">{status(record)}</span>
            </div>)}
          </div> : null}
          {!isShop ? <div className="item-edit-form"><div className="editable-field-groups">
            {groupFields.map(group => <fieldset className="editable-field-group" key={group.title}><legend>{group.title}</legend><div className="editable-field-grid">
              {group.fields.map(field => <div className={`path-field editable-field-control ${selected.readOnlyFields.includes(field.key) ? 'editable-field-disabled' : ''} ${valuesFor(selected)[field.key] !== selected.values[field.key] ? 'editable-field-changed' : ''}`} key={field.key}>
                <FieldLabel htmlFor={`pla-field-${field.key}`} label={fieldLabel(field)} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />{renderInput(field)}
                {selected.readOnlyFields.includes(field.key) ? <small className="editable-field-status">{translateLiteral('Read-only')}</small> : null}
                {!selected.readOnlyFields.includes(field.key) && valuesFor(selected)[field.key] !== selected.values[field.key] ? <small className="editable-field-status">{translateLiteral('Changed')}</small> : null}
              </div>)}
            </div></fieldset>)}
          </div></div> : null}
          {selected.relatedRecords.length ? <div className="pla-related-records"><h4>{t('pla.sharedUses', { count: selected.relatedRecords.length })}</h4>
            <ul>{selected.relatedRecords.map((related, index) => <li key={`${related.kind}:${related.id}:${index}`}><span>{t(`pla.related.${related.kind}`)}</span>: <span data-localization-ignore="true">{related.label}</span></li>)}</ul>
          </div> : null}
        </EditorWorkspaceSection>
      </> : <p className="empty-copy">{t('pla.empty')}</p>}
    </SectionedEditorWorkspace>
  </section>;
}
