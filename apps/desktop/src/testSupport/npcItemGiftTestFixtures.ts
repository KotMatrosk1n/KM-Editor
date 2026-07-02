/* SPDX-License-Identifier: GPL-3.0-only */
import { type WorkflowSummary } from '../bridge/contracts';
import { type NpcItemGiftWorkflow } from '../bridge/npcItemGiftContracts';
import { type ProjectBridge } from '../bridge/projectBridge';

export function createNpcItemGiftWorkflowFixture(canEdit = true): NpcItemGiftWorkflow {
  const summary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Advanced editor for fixed NPC item gifts.',
    diagnostics: [],
    id: 'npcItemGift',
    label: 'NPC Item Gift'
  };

  return {
    diagnostics: [],
    itemOptions: [
      { category: 'Items', isKeyItem: false, itemId: 4, name: 'Poke Ball' },
      { category: 'Items', isKeyItem: false, itemId: 5, name: 'Rare Candy' },
      { category: 'Items', isKeyItem: false, itemId: 17, name: 'Potion' },
      { category: 'Items', isKeyItem: false, itemId: 28, name: 'Revive' },
      { category: 'Items', isKeyItem: false, itemId: 29, name: 'Max Revive' },
      { category: 'Key Items', isKeyItem: true, itemId: 1074, name: 'Endorsement' }
    ],
    npcs: [
      {
        displayOrder: 10,
        gifts: [
          {
            displayOrder: 10,
            giftId: 'mum-postwick-poke-ball',
            items: [
              {
                itemCell: 5119,
                itemId: 4,
                itemName: 'Poke Ball',
                label: 'Poke Ball',
                slotId: 'item',
                vanillaItemId: 4,
                vanillaItemName: 'Poke Ball'
              }
            ],
            label: 'Mum (Postwick)',
            location: 'Postwick',
            npcId: 'mum',
            npcName: 'Mum',
            provenance: {
              fileState: 'baseOnly',
              sourceFile: 'romfs/bin/script/amx/main_event_0180.amx',
              sourceLayer: 'base'
            },
            quantity: 5,
            quantityCell: 5118,
            relativePath: 'romfs/bin/script/amx/main_event_0180.amx',
            vanillaQuantity: 5
          }
        ],
        npcId: 'mum',
        npcName: 'Mum'
      },
      {
        displayOrder: 30,
        gifts: [
          {
            displayOrder: 30,
            giftId: 'leon-route-2-poke-ball',
            items: [
              {
                itemCell: 6120,
                itemId: 4,
                itemName: 'Poke Ball',
                label: 'Poke Ball',
                slotId: 'item',
                vanillaItemId: 4,
                vanillaItemName: 'Poke Ball'
              }
            ],
            label: 'Leon (Route 2)',
            location: 'Route 2',
            npcId: 'leon',
            npcName: 'Leon',
            provenance: {
              fileState: 'baseOnly',
              sourceFile: 'romfs/bin/script/amx/main_event_0250.amx',
              sourceLayer: 'base'
            },
            quantity: 20,
            quantityCell: 6119,
            relativePath: 'romfs/bin/script/amx/main_event_0250.amx',
            vanillaQuantity: 20
          }
        ],
        npcId: 'leon',
        npcName: 'Leon'
      },
      {
        displayOrder: 210,
        gifts: [
          {
            displayOrder: 210,
            giftId: 'sonia-stow-on-side-revive',
            items: [
              {
                itemCell: 5247,
                itemId: 28,
                itemName: 'Revive',
                label: 'Revive',
                slotId: 'item',
                vanillaItemId: 28,
                vanillaItemName: 'Revive'
              }
            ],
            label: 'Sonia (Stow-on-Side)',
            location: 'Stow-on-Side',
            npcId: 'sonia',
            npcName: 'Sonia',
            provenance: {
              fileState: 'baseOnly',
              sourceFile: 'romfs/bin/script/amx/main_event_1110.amx',
              sourceLayer: 'base'
            },
            quantity: 2,
            quantityCell: 5246,
            relativePath: 'romfs/bin/script/amx/main_event_1110.amx',
            vanillaQuantity: 2
          },
          {
            displayOrder: 270,
            giftId: 'sonia-slumbering-weald-max-revive',
            items: [
              {
                itemCell: 6776,
                itemId: 29,
                itemName: 'Max Revive',
                label: 'Max Revive',
                slotId: 'item',
                vanillaItemId: 29,
                vanillaItemName: 'Max Revive'
              }
            ],
            label: 'Sonia (Slumbering Weald)',
            location: 'Slumbering Weald',
            npcId: 'sonia',
            npcName: 'Sonia',
            provenance: {
              fileState: 'baseOnly',
              sourceFile: 'romfs/bin/script/amx/main_event_1820.amx',
              sourceLayer: 'base'
            },
            quantity: 3,
            quantityCell: 6775,
            relativePath: 'romfs/bin/script/amx/main_event_1820.amx',
            vanillaQuantity: 3
          }
        ],
        npcId: 'sonia',
        npcName: 'Sonia'
      }
    ],
    sources: [
      {
        label: 'main_event_0180.amx',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script/amx/main_event_0180.amx',
          sourceLayer: 'base'
        },
        relativePath: 'romfs/bin/script/amx/main_event_0180.amx',
        sourceId: 'main_event_0180',
        status: 'available'
      },
      {
        label: 'main_event_1110.amx',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script/amx/main_event_1110.amx',
          sourceLayer: 'base'
        },
        relativePath: 'romfs/bin/script/amx/main_event_1110.amx',
        sourceId: 'main_event_1110',
        status: 'available'
      },
      {
        label: 'main_event_1820.amx',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script/amx/main_event_1820.amx',
          sourceLayer: 'base'
        },
        relativePath: 'romfs/bin/script/amx/main_event_1820.amx',
        sourceId: 'main_event_1820',
        status: 'available'
      }
    ],
    stats: {
      giftCount: 4,
      itemOptionCount: 6,
      npcCount: 3,
      sourceFileCount: 3
    },
    summary
  };
}

export function createNpcItemGiftBridgeFixture(
  workflow: NpcItemGiftWorkflow
): Pick<ProjectBridge, 'loadNpcItemGiftWorkflow' | 'stageNpcItemGift'> {
  return {
    loadNpcItemGiftWorkflow: () => Promise.resolve({ workflow }),
    stageNpcItemGift: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'NPC Item Gift changes are staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.npcItemGift',
              field: 'gifts',
              newValue: request.gifts
                .map(
                  (gift) =>
                    `${gift.giftId}|${gift.quantity}|${gift.items
                      .map((item) => `${item.slotId}=${item.itemId}`)
                      .join(',')}`
                )
                .join(';'),
              recordId: 'npc-item-gift',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script/amx/main_event_0180.amx'
                }
              ],
              summary: 'Stage NPC Item Gift changes.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-npc-item-gift'
        },
        workflow
      })
  };
}
