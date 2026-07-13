/* SPDX-License-Identifier: GPL-3.0-only */

export type ShopInventoryUpdateRow = {
  itemId: number;
  rowId: string | null;
};

type StructuredShopInventoryUpdate = {
  rows: Array<{ itemId: number; rowId: string }>;
  updateOrder: boolean;
  version: 1;
};

export function createShopInventoryUpdateValue(
  editorFamily: string,
  originalRows: readonly ShopInventoryUpdateRow[],
  finalRows: readonly ShopInventoryUpdateRow[]
) {
  const includedFinalRows = finalRows.filter((row) => row.itemId !== 0);

  if (editorFamily !== 'sv') {
    const finalItemIds = includedFinalRows.map((row) => row.itemId);
    const originalItemIds = originalRows.map((row) => row.itemId);
    return areArraysEqual(finalItemIds, originalItemIds) ? null : finalItemIds.join(',');
  }

  if (
    originalRows.some((row) => !isSvShopRowId(row.rowId)) ||
    includedFinalRows.some((row) => !isSvShopRowId(row.rowId))
  ) {
    return null;
  }

  const originalRowIds = originalRows.map((row) => row.rowId as string);
  const finalRowIds = includedFinalRows.map((row) => row.rowId as string);
  const hasItemChanges = !areArraysEqual(
    includedFinalRows.map((row) => `${row.rowId}:${row.itemId}`),
    originalRows.map((row) => `${row.rowId}:${row.itemId}`)
  );
  if (!hasItemChanges) {
    return null;
  }

  const payload: StructuredShopInventoryUpdate = {
    rows: includedFinalRows.map((row) => ({
      itemId: row.itemId,
      rowId: row.rowId as string
    })),
    updateOrder: !areArraysEqual(finalRowIds, originalRowIds),
    version: 1
  };
  return JSON.stringify(payload);
}

export function parseShopInventoryUpdateItemIds(value: string | null | undefined) {
  const text = value?.trim() ?? '';
  if (text.length === 0) {
    return [];
  }

  if (text.startsWith('{')) {
    try {
      const payload = JSON.parse(text) as Partial<StructuredShopInventoryUpdate>;
      if (payload.version !== 1 || !Array.isArray(payload.rows)) {
        return [];
      }

      const itemIds: number[] = [];
      for (const row of payload.rows) {
        if (!row || !Number.isInteger(row.itemId) || row.itemId < 0) {
          return [];
        }

        itemIds.push(row.itemId);
      }

      return itemIds;
    } catch {
      return [];
    }
  }

  return text
    .split(',')
    .map((part) => Number.parseInt(part, 10))
    .filter((itemId) => Number.isFinite(itemId));
}

export function getNextShopInventoryDraftId(
  rows: readonly Pick<ShopInventoryUpdateRow, 'rowId'>[]
) {
  let highestDraftId = 0;
  for (const row of rows) {
    const match = /^new:(\d+)$/.exec(row.rowId ?? '');
    if (match) {
      highestDraftId = Math.max(highestDraftId, Number.parseInt(match[1]!, 10));
    }
  }

  return highestDraftId + 1;
}

function isSvShopRowId(rowId: string | null): rowId is string {
  return rowId !== null && /^(?:source|new):\d+$/.test(rowId);
}

function areArraysEqual<T>(left: readonly T[], right: readonly T[]) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}
