/* SPDX-License-Identifier: GPL-3.0-only */

import type { ReactNode } from 'react';
import { ArrowLeft } from 'lucide-react';
import { EditorDetailGroup, EditorDetailValue, EditorFieldGroup } from '../../components/EditorRecordDetails';
import { useLocalization } from '../../localization';
import { itemDataGroups, type ItemBehaviorGroup } from '../../itemEditorLayout';
import './itemEditorWorkspace.css';

export function ItemDataLayout<T>({ fields, keyFor, renderField, hasIssue, machineHelp }: {
  fields: readonly T[];
  keyFor: (field: T) => string;
  renderField: (field: T) => ReactNode;
  hasIssue: (field: T) => boolean;
  machineHelp?: ReactNode;
}) {
  const { translateLiteral } = useLocalization();
  const groups = itemDataGroups(fields, keyFor);
  const renderGroup = (group: (typeof groups)[number]) => {
    const rows = <div className="item-data-rows">
      {group.rows.map(({ fields: rowFields }) => (
        <div className={rowFields.length === 2 ? 'item-effect-pair' : undefined} key={keyFor(rowFields[0])}>
          {rowFields.map(field => <div className="item-field" data-item-field={keyFor(field)} key={keyFor(field)}>
            {renderField(field)}
          </div>)}
        </div>
      ))}
    </div>;
    const title = translateLiteral(group.label);
    const invalid = group.rows.some(row => row.fields.some(hasIssue));
    return group.disclosure
      ? <details className="item-disclosure" key={group.id}>
          <summary>{title}{invalid ? <span className="editable-field-error">{translateLiteral('Invalid value')}</span> : null}</summary>
          {rows}
        </details>
      : <EditorFieldGroup className={`item-data-group item-data-${group.id}`} key={group.id} label={title} headingValue={group.id === 'machine' ? machineHelp : null}>
          {rows}
        </EditorFieldGroup>;
  };
  const prices = groups.find(group => group.id === 'prices');
  return <div className="item-data-layout">
    {prices ? renderGroup(prices) : null}
    <div className="item-editor-columns">{groups.filter(group => group.id !== 'prices' && !group.disclosure).map(renderGroup)}</div>
    {groups.filter(group => group.disclosure).map(renderGroup)}
  </div>;
}

export function ItemBehavior({ groups, onEdit }: {
  groups: readonly ItemBehaviorGroup[];
  onEdit: () => void;
}) {
  const { t, translateLiteral } = useLocalization();
  return <div className="item-behavior">
    <div className="item-editor-columns">
      {groups.filter(group => group.rows.length || group.boosts?.length || group.flags?.length).map(group => (
        <EditorDetailGroup key={group.label} label={translateLiteral(group.label)}>
          <dl className="editor-detail-list">
            {group.rows.map(row => <EditorDetailValue key={row.key} label={translateLiteral(row.label)} dataValue>
              {row.literal ? translateLiteral(String(row.value)) : row.value}
              {row.enabled !== undefined ? <span className="item-behavior-state"> · {t(row.enabled ? 'gameOptions.choice.on' : 'gameOptions.choice.off')}</span> : null}
            </EditorDetailValue>)}
            {group.flags?.map(row => <EditorDetailValue key={row.key} label={translateLiteral(row.label)} dataValue>
              {row.flags.length > 0 ? row.flags.map(flag => translateLiteral(flag)).join(', ') : translateLiteral('None')}
            </EditorDetailValue>)}
          </dl>
          {group.boosts?.length ? <dl className="item-boost-strip" aria-label={translateLiteral('Battle boost stages')}>
            {group.boosts.map(boost => <div key={boost.key}>
              <dt>{translateLiteral(boost.label)}</dt>
              <dd>{Number(boost.value) > 0 ? `+${boost.value}` : boost.value}</dd>
            </div>)}
          </dl> : null}
        </EditorDetailGroup>
      ))}
    </div>
    <button className="secondary-button compact-button" type="button" onClick={onEdit}>
      <ArrowLeft aria-hidden="true" size={15} />
      {translateLiteral('Back to Item Data')}
    </button>
  </div>;
}
