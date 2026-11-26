/* SPDX-License-Identifier: GPL-3.0-only */
import { plaFieldHelp } from './plaFieldHelp';
import { plaAreaLabel } from './plaLabels';
import { useState, type ReactNode } from 'react';
import { ContextHelp } from '../../components/ContextHelp';
import { EditorDetailGroup, EditorDetailValue, EditorFieldGroup } from '../../components/EditorRecordDetails';
import { ItemBehavior, ItemDataLayout } from '../items/ItemEditorWorkspace';
import { formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';
import { isPlaFieldValueValid, resolvePlaField } from './plaDraftValidation';
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
  const isItems = kind === 'items';
  const [itemSectionRequest, setItemSectionRequest] = useState<{ sectionId: string; focus: boolean }>();
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
  const name = (record: PlaRecord) => isItems ? record.label.replace(new RegExp('^' + record.id + ': '), '')
    : kind === 'ranks' ? t('pla.rankLabel', { rank: record.id })
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
    { title: t('pla.kind.crafting'), fields: fields.filter(field => /^(ingredient|quantity)\d+$/u.test(field.key)) }
  ] : kind === 'research' ? [
    { title: t('pla.group.thresholds'), fields: fields.filter(field => field.key.startsWith('threshold')) },
    { title: t('pla.group.points'), fields: fields.filter(field => !field.key.startsWith('threshold')) }
  ] : kind === 'drops' ? [
    { title: t('pla.group.regularDrop'), fields: fields.filter(field => field.key.startsWith('regular')) },
    { title: t('pla.group.rareDrop'), fields: fields.filter(field => field.key.startsWith('rare')) }
  ] : [{ title: t(`pla.kind.${kind}`), fields }];
  const selectedGroup = selected && isShop ? groups.get(selected.metadata.inventory) ?? [] : [];
  const selectedValues = selected ? valuesFor(selected) : {};
  const invalidField = (field: PlaField) => Boolean(selected &&
    selectedValues[field.key] !== selected.values[field.key] &&
    !isPlaFieldValueValid(resolvePlaField(field, selectedValues), selectedValues[field.key]));
  const itemSource = selected?.metadata;
  const ingredientSlots = fields.flatMap(field => {
    const match = /^ingredient(\d+)$/u.exec(field.key);
    if (!match) return [];
    const quantity = fields.find(candidate => candidate.key === 'quantity' + match[1]);
    return quantity ? [{ index: match[1], item: field, quantity }] : [];
  });
  const renderEconomyField = (field: PlaField, inTable = false) => {
    if (!selected) return null;
    const readOnly = field.readOnly || selected.readOnlyFields.includes(field.key);
    const invalid = invalidField(field);
    const changed = selectedValues[field.key] !== selected.values[field.key];
    return <div className={`path-field editable-field-control ${readOnly ? 'editable-field-disabled' : ''} ${invalid ? 'editable-field-invalid' : changed ? 'editable-field-changed' : ''}`} key={field.key}>
      <div className={inTable ? 'pla-matrix-field-label' : undefined}>
        <FieldLabel htmlFor={`pla-field-${field.key}`} label={fieldLabel(field)} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />
      </div>
      {renderInput(field)}
      {readOnly ? <small className="editable-field-status">{translateLiteral('Read-only')}</small>
        : invalid ? <small className="editable-field-error">{t('pla.invalid')}</small>
        : changed ? <small className="editable-field-status">{translateLiteral('Changed')}</small> : null}
    </div>;
  };
  return <section className={`panel wide-panel pla-editor pla-economy-editor pla-economy-${kind}`} aria-labelledby="pla-editor-title">
    <div className="panel-heading"><Icon size={18} aria-hidden="true" /><h2 id="pla-editor-title">{t(workbenchSectionLocalization[section].label)}</h2>{refresh}</div>
    <SectionedEditorWorkspace sessionKey={sessionKey + ':' + kind} initialSection="data" compactFields
      sectionTransitionDuration={250}
      activeSectionRequest={isItems ? itemSectionRequest : undefined}
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
        <div className="items-row items-row-heading pla-economy-row" role="row">{['ID', 'Name', isShop ? 'Inventory' : isItems ? 'Price' : 'Summary', 'Status'].map(column => <span role="columnheader" key={column}>{translateLiteral(column)}</span>)}</div>
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
          {isItems ? <div className="item-editor-columns">
            <EditorDetailGroup label={translateLiteral('Item')} headingValue={<ContextHelp label={translateLiteral('Items')}>{t('pla.notice.items')}</ContextHelp>}>
              <dl className="editor-detail-list">
                <EditorDetailValue label={translateLiteral('ID')} dataValue>{selected.id}</EditorDetailValue>
                <EditorDetailValue label={translateLiteral('Name')} dataValue>{name(selected)}</EditorDetailValue>
                <EditorDetailValue label={translateLiteral('Shared row')}>{translateLiteral('No')}</EditorDetailValue>
              </dl>
            </EditorDetailGroup>
            {itemSource?.sourceFile ? <EditorDetailGroup label={translateLiteral('Source')}>
              <dl className="editor-detail-list">
                <EditorDetailValue label={translateLiteral('Source file')} dataValue path>{itemSource.sourceFile}</EditorDetailValue>
                <EditorDetailValue label={translateLiteral('Layer')}>{translateLiteral(formatSourceLayer(itemSource.sourceLayer))}</EditorDetailValue>
                <EditorDetailValue label={translateLiteral('File state')}>{translateLiteral(formatFileState(itemSource.fileState))}</EditorDetailValue>
              </dl>
            </EditorDetailGroup> : null}
          </div> : <>
          <EditorDetailGroup label={translateLiteral('Details')} headingValue={<ContextHelp label={name(selected)}>{t(`pla.notice.${kind}`)}</ContextHelp>}>
            <dl className="editor-detail-list">
              <EditorDetailValue label={translateLiteral('Status')}>{groupStatus(selected)}</EditorDetailValue>
              {Object.entries(selected.metadata).filter(([key]) => !['protected', 'areaName'].includes(key)).map(([key, value]) =>
                <EditorDetailValue key={key} label={t(`pla.meta.${key}`)} dataValue path={key === 'sourceFile'}>
                  {key === 'task' ? summary(selected) : key === 'area' ? plaAreaLabel(t, value)
                    : key === 'category' ? t('pla.event.category.' + value)
                    : key === 'empty' ? translateLiteral(value === '1' ? 'Yes' : 'No') : value}
                </EditorDetailValue>)}
            </dl>
          </EditorDetailGroup>
          </>}
        </EditorWorkspaceSection>
        <EditorWorkspaceSection id="data" label={isShop ? translateLiteral('Inventory') : kind === 'items' ? translateLiteral('Item Data') : t(`pla.kind.${kind}`)}
          modified={isShop ? selectedGroup.some(hasDraft) : hasDraft(selected)} invalid={fields.some(invalidField)}>
          {isShop ? <div className="pla-shop-inventory">
            <table className="editor-data-matrix" aria-label={translateLiteral('Inventory')}>
              <thead><tr><th scope="col">{translateLiteral('Slot')}</th>
                {fields.map(field => {
                  const label = fieldLabel(field);
                  const help = plaFieldHelp(t, kind, field, label);
                  return <th key={field.key} scope="col"><span className="editor-task-field-name">{label}
                    {help ? <ContextHelp label={label}>{help}</ContextHelp> : null}
                  </span></th>;
                })}
                <th scope="col">{translateLiteral('Status')}</th></tr></thead>
              <tbody>{selectedGroup.map((record, index) => <tr key={record.id} data-selected={record.id === selected.id}>
                <th scope="row"><button className="editor-record-table-select" type="button" aria-pressed={record.id === selected.id}
                  aria-label={`${translateLiteral('Slot')} ${index + 1}`} onClick={() => onSelect(record.id)}>{index + 1}</button></th>
                {fields.map(field => <td key={field.key}>
                  <div className="path-field editable-field-control">
                    <label className="pla-shop-field-label" htmlFor={`pla-shop-${record.id}-${field.key}`}>{fieldLabel(field)} {index + 1}</label>
                    {renderRecordInput(record, field)}
                  </div>
                </td>)}
                <td>{status(record)}</td>
              </tr>)}</tbody>
            </table>
          </div> : null}
          {isItems ? <ItemDataLayout fields={fields} keyFor={field => field.key} hasIssue={invalidField}
            renderField={field => renderEconomyField(field)} /> : !isShop ? (
            <div className="editor-task-groups">
              {groupFields.map(group => {
                const isIngredients = kind === 'crafting' && group.title === t('pla.kind.crafting') && ingredientSlots.length > 0;
                const groupFields = isIngredients ? group.fields.filter(field => !ingredientSlots.some(slot => slot.item === field || slot.quantity === field)) : group.fields;
                if (!isIngredients && groupFields.length === 0) return null;
                return <EditorFieldGroup className="editable-field-group" key={group.title} label={group.title}>
                  {isIngredients ? <table className="editor-data-matrix pla-recipe-matrix" aria-label={group.title}>
                    <thead><tr><th scope="col">{translateLiteral('Slot')}</th><th scope="col">{translateLiteral('Ingredient')}</th>
                      <th scope="col">{translateLiteral('Quantity')}</th></tr></thead>
                    <tbody>{ingredientSlots.map(slot => <tr key={slot.index}>
                      <th scope="row">{slot.index}</th><td>{renderEconomyField(slot.item, true)}</td>
                      <td>{renderEconomyField(slot.quantity, true)}</td>
                    </tr>)}</tbody>
                  </table> : null}
                  <div className="pokemon-instance-field-list">{groupFields.map(field => renderEconomyField(field))}</div>
                </EditorFieldGroup>;
              })}
            </div>
          ) : null}
          {selected.relatedRecords.length ? <div className="pla-related-records"><h4>{t('pla.sharedUses', { count: selected.relatedRecords.length })}</h4>
            <ul>{selected.relatedRecords.map((related, index) => <li key={`${related.kind}:${related.id}:${index}`}><span>{t(`pla.related.${related.kind}`)}</span>: <span data-localization-ignore="true">{related.label}</span></li>)}</ul>
          </div> : null}
        </EditorWorkspaceSection>
        {isItems ? <EditorWorkspaceSection id="behavior" label={translateLiteral('In-Game Behavior')}>
          <ItemBehavior groups={[{ label: 'Inventory', rows: fields.flatMap(field => {
            const value = invalidField(field) ? selected.values[field.key] : selectedValues[field.key];
            return value === undefined ? [] : [{ key: field.key, label: fieldLabel(field), value }];
          }) }]} onEdit={() => setItemSectionRequest({ sectionId: 'data', focus: true })} />
        </EditorWorkspaceSection> : null}
      </> : <p className="empty-copy">{t('pla.empty')}</p>}
    </SectionedEditorWorkspace>
  </section>;
}
