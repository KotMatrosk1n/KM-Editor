/* SPDX-License-Identifier: GPL-3.0-only */

import type { EditSession, ShopRecord } from '../../bridge/contracts';
type PendingEdit = EditSession['pendingEdits'][number];
import { createShopInventoryUpdateValue } from './shopInventoryUpdate';

export type RemovedShopInventoryRow = {
  sourceIndex: number;
  itemId: number;
  itemName: string;
  slot: number;
};

function readRows(edit: PendingEdit, shop: ShopRecord) {
  if (edit.domain !== 'workflow.shops' || edit.field !== 'setInventory' ||
      edit.recordId?.split('#')[0] !== shop.shopId || !shop.sourceInventory) return null;
  try {
    if (shop.editorFamily === 'swsh') {
      const values = edit.newValue?.trim() ? edit.newValue.split(',') : [];
      if (values.some(value => !/^\d+$/.test(value.trim()) || !Number.isSafeInteger(Number(value)) || Number(value) <= 0)) return null;
      return values.map(value => ({ itemId: Number(value), rowId: null as string | null }));
    }
    const payload = JSON.parse(edit.newValue ?? '');
    if (payload.version !== 1 || !Array.isArray(payload.rows) || payload.rows.some(
      (row: { itemId?: unknown; rowId?: unknown }) => !Number.isSafeInteger(row.itemId) ||
        Number(row.itemId) <= 0 || typeof row.rowId !== 'string' || !/^(source|new):\d+$/.test(row.rowId)
    )) return null;
    return payload.rows as Array<{ itemId: number; rowId: string | null }>;
  } catch { return null; }
}

function matchSourceRows(shop: ShopRecord, rows: Array<{ itemId: number; rowId: string | null }>) {
  const source = shop.sourceInventory!;
  if (shop.editorFamily !== 'swsh') {
    const indices = new Map(source.map((row, index) => [row.rowId, index]));
    return rows.map(row => indices.get(row.rowId) ?? -1);
  }

  // Preserve occurrence identity when the final inventory is an ordered subset.
  // Matching duplicate item IDs by count alone can turn undo into a reorder.
  let cursor = 0;
  const ordered = rows.map(row => {
    while (cursor < source.length && source[cursor]!.itemId !== row.itemId) cursor += 1;
    return cursor < source.length ? cursor++ : -1;
  });
  if (ordered.every(index => index >= 0)) return ordered;

  // Added items and explicit reorders retain their final positions.
  const occurrences = new Map<number, { indices: number[]; next: number }>();
  source.forEach((row, index) => {
    const group = occurrences.get(row.itemId) ?? { indices: [], next: 0 };
    group.indices.push(index);
    occurrences.set(row.itemId, group);
  });
  return rows.map(row => {
    const group = occurrences.get(row.itemId);
    return group ? group.indices[group.next++] ?? -1 : -1;
  });
}

export function getRemovedShopInventoryRows(edit: PendingEdit, shop: ShopRecord | undefined): RemovedShopInventoryRow[] {
  if (!shop) return [];
  const rows = readRows(edit, shop);
  if (!rows || !shop.sourceInventory) return [];
  const remaining = new Set(matchSourceRows(shop, rows));
  return shop.sourceInventory.flatMap((source, sourceIndex) => {
    if (remaining.has(sourceIndex)) return [];
    return [{ sourceIndex, itemId: source.itemId, itemName: source.itemName, slot: source.slot }];
  });
}

export function restorePendingShopInventoryRow(edit: PendingEdit, shop: ShopRecord, sourceIndex: number): PendingEdit | null | undefined {
  if (edit.association || !getRemovedShopInventoryRows(edit, shop).some(row => row.sourceIndex === sourceIndex)) return undefined;
  const rows = readRows(edit, shop)!;
  const source = shop.sourceInventory![sourceIndex]!;
  const matches = matchSourceRows(shop, rows);
  let insertionIndex = rows.length;
  for (let next = sourceIndex + 1; next < shop.sourceInventory!.length; next += 1) {
    const index = matches.indexOf(next);
    if (index >= 0) { insertionIndex = index; break; }
  }
  rows.splice(insertionIndex, 0, { itemId: source.itemId, rowId: source.rowId });
  const value = createShopInventoryUpdateValue(shop.editorFamily, shop.sourceInventory!, rows);
  return value === null ? null : { ...edit, newValue: value };
}
