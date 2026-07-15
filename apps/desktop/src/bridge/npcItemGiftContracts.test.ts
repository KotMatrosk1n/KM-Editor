/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { createNpcItemGiftWorkflowFixture } from '../testSupport/npcItemGiftTestFixtures';
import {
  npcItemGiftItemOptionRecordSchema,
  npcItemGiftRecordSchema,
  npcItemGiftSelectionSchema,
  npcItemGiftWorkflowSchema,
  stageNpcItemGiftRequestSchema
} from './npcItemGiftContracts';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

describe('NPC Item Gift contracts', () => {
  it('accepts the complete workflow and nullable fixed-quantity metadata', () => {
    const workflow = createNpcItemGiftWorkflowFixture();
    const fixedGift = workflow.npcs[0]!.gifts[0]!;
    workflow.npcs[0]!.gifts[0] = {
      ...fixedGift,
      canEditQuantity: false,
      quantityCell: null
    };

    expect(npcItemGiftWorkflowSchema.safeParse(workflow).success).toBe(true);
  });

  it.each(['future', 'Available', ''])('rejects an unknown source or gift status %j', (status) => {
    const workflow = createNpcItemGiftWorkflowFixture();
    const gift = workflow.npcs[0]!.gifts[0]!;
    const source = workflow.sources[0]!;

    expect(npcItemGiftRecordSchema.safeParse({ ...gift, status }).success).toBe(false);
    expect(
      npcItemGiftWorkflowSchema.safeParse({
        ...workflow,
        sources: [{ ...source, status }, ...workflow.sources.slice(1)]
      }).success
    ).toBe(false);
  });

  it('accepts signed legacy current and selection values while keeping vanilla strict', () => {
    const gift = createNpcItemGiftWorkflowFixture().npcs[0]!.gifts[0]!;
    expect(
      npcItemGiftRecordSchema.safeParse({
        ...gift,
        items: gift.items.map((item) => ({ ...item, itemId: -7 })),
        quantity: -12
      }).success
    ).toBe(true);
    expect(
      npcItemGiftSelectionSchema.safeParse({
        giftId: gift.giftId,
        items: [{ itemId: -7, slotId: 'item' }],
        quantity: -12
      }).success
    ).toBe(true);
    expect(npcItemGiftRecordSchema.safeParse({ ...gift, vanillaQuantity: 0 }).success).toBe(
      false
    );
    expect(
      npcItemGiftRecordSchema.safeParse({
        ...gift,
        items: gift.items.map((item) => ({ ...item, vanillaItemId: 0 }))
      }).success
    ).toBe(false);
    expect(
      npcItemGiftItemOptionRecordSchema.safeParse({
        category: 'Medicine',
        isKeyItem: false,
        itemId: 0,
        name: 'Legacy item'
      }).success
    ).toBe(false);
  });

  it('preserves long modded semantic item text without trimming or arbitrary caps', () => {
    const gift = createNpcItemGiftWorkflowFixture().npcs[0]!.gifts[0]!;
    const longName = `  ${'Modded item name '.repeat(30)}`;
    const parsedGift = npcItemGiftRecordSchema.parse({
      ...gift,
      items: gift.items.map((item) => ({
        ...item,
        itemName: longName,
        vanillaItemName: longName
      }))
    });
    const parsedOption = npcItemGiftItemOptionRecordSchema.parse({
      category: longName,
      isKeyItem: false,
      itemId: 1,
      name: longName
    });

    expect(parsedGift.items[0]?.itemName).toBe(longName);
    expect(parsedOption.name).toBe(longName);
    expect(
      npcItemGiftSelectionSchema.safeParse({
        giftId: ' gift ',
        items: [{ itemId: 1, slotId: 'item' }],
        quantity: 1
      }).success
    ).toBe(false);
  });

  it.each([1.5, 2_147_483_648, -2_147_483_649])(
    'rejects non-int32 packed values %s',
    (quantity) => {
      const gift = createNpcItemGiftWorkflowFixture().npcs[0]!.gifts[0]!;
      expect(npcItemGiftRecordSchema.safeParse({ ...gift, quantity }).success).toBe(false);
      expect(
        npcItemGiftSelectionSchema.safeParse({
          giftId: gift.giftId,
          items: [{ itemId: 1, slotId: 'item' }],
          quantity
        }).success
      ).toBe(false);
    }
  );

  it('requires nonempty selections and exact request objects', () => {
    expect(
      npcItemGiftSelectionSchema.safeParse({
        giftId: 'gift',
        items: [{ itemId: 0, slotId: 'item' }],
        quantity: 1
      }).success
    ).toBe(true);
    expect(
      stageNpcItemGiftRequestSchema.safeParse({
        gifts: [],
        paths: projectPaths,
        session: null
      }).success
    ).toBe(false);
    expect(
      stageNpcItemGiftRequestSchema.safeParse({
        extra: true,
        gifts: [{ giftId: 'gift', items: [{ itemId: 1, slotId: 'item' }], quantity: 1 }],
        paths: projectPaths,
        session: null
      }).success
    ).toBe(false);
  });
});
