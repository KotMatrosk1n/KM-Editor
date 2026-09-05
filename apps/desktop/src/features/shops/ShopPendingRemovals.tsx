/* SPDX-License-Identifier: GPL-3.0-only */

import { Trash2 } from 'lucide-react';
import type { EditSession, ShopsWorkflow } from '../../bridge/contracts';
type PendingEdit = EditSession['pendingEdits'][number];
import { useLocalization } from '../../localization';
import { getRemovedShopInventoryRows } from './shopPendingInventory';

export function ShopPendingRemovals({ edit, workflow, disabled, onRemove }: {
  edit: PendingEdit;
  workflow: ShopsWorkflow | null;
  disabled: boolean;
  onRemove: (sourceIndex: number) => void;
}) {
  const { t } = useLocalization();
  const shop = workflow?.shops.find(row => row.shopId === edit.recordId?.split('#')[0]);
  const removed = getRemovedShopInventoryRows(edit, shop);
  if (removed.length === 0) return null;
  return <section className="shop-pending-removals" aria-label={t('shops.pending.removedTitle')}>
    <h4>{t('shops.pending.removedTitle')}</h4>
    <ul>{removed.map(row => <li key={row.sourceIndex}>
      <span data-localization-ignore="true">{shop?.editorFamily === 'swsh' ? row.itemName : t('shops.pending.removedItem', { item: row.itemName, slot: row.slot })}</span>
      <button className="danger-button icon-button" disabled={disabled || Boolean(edit.association)}
        type="button" onClick={() => onRemove(row.sourceIndex)}
        aria-label={t('shops.pending.undoRemoval', { item: row.itemName })}
        title={t('shops.pending.undoRemoval', { item: row.itemName })}>
        <Trash2 aria-hidden="true" size={16} />
      </button>
    </li>)}</ul>
  </section>;
}
