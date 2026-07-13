/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  createShopInventoryUpdateValue,
  getNextShopInventoryDraftId,
  parseShopInventoryUpdateItemIds,
  type ShopInventoryUpdateRow
} from './shopInventoryUpdate';

describe('shop inventory updates', () => {
  const originalRows: ShopInventoryUpdateRow[] = [
    { itemId: 1, rowId: 'source:0' },
    { itemId: 2, rowId: 'source:1' }
  ];

  it('creates an identity-preserving S/V payload for an item-only replacement', () => {
    const value = createShopInventoryUpdateValue('sv', originalRows, [
      { itemId: 4, rowId: 'source:0' },
      originalRows[1]!
    ]);

    expect(JSON.parse(value!)).toEqual({
      rows: [
        { itemId: 4, rowId: 'source:0' },
        { itemId: 2, rowId: 'source:1' }
      ],
      updateOrder: false,
      version: 1
    });
  });

  it('detects an S/V reorder when duplicate item IDs make positional comparison ambiguous', () => {
    const duplicateRows: ShopInventoryUpdateRow[] = [
      { itemId: 1, rowId: 'source:0' },
      { itemId: 1, rowId: 'source:1' }
    ];
    const value = createShopInventoryUpdateValue('sv', duplicateRows, [
      duplicateRows[1]!,
      duplicateRows[0]!
    ]);

    expect(JSON.parse(value!)).toEqual({
      rows: [
        { itemId: 1, rowId: 'source:1' },
        { itemId: 1, rowId: 'source:0' }
      ],
      updateOrder: true,
      version: 1
    });
  });

  it('does not stage unchanged S/V inventory rows', () => {
    expect(createShopInventoryUpdateValue('sv', originalRows, [...originalRows])).toBeNull();
  });

  it('keeps legacy CSV payloads for other editor families', () => {
    expect(
      createShopInventoryUpdateValue('swsh', originalRows, [
        originalRows[1]!,
        originalRows[0]!
      ])
    ).toBe('2,1');
  });

  it('formats item IDs from structured and legacy payloads', () => {
    expect(
      parseShopInventoryUpdateItemIds(
        '{"version":1,"updateOrder":true,"rows":[{"rowId":"source:1","itemId":2},{"rowId":"new:1","itemId":4}]}'
      )
    ).toEqual([2, 4]);
    expect(parseShopInventoryUpdateItemIds('2,4')).toEqual([2, 4]);
  });

  it('continues new row identities above rows returned by an earlier Save', () => {
    expect(
      getNextShopInventoryDraftId([
        { rowId: 'source:0' },
        { rowId: 'new:2' },
        { rowId: 'new:7' }
      ])
    ).toBe(8);
  });
});
