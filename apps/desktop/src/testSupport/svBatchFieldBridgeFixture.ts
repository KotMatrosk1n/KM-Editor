/* SPDX-License-Identifier: GPL-3.0-only */

import {
  type EncountersWorkflow,
  type GiftPokemonWorkflow,
  type ItemsWorkflow,
  type MovesWorkflow,
  type PlacementWorkflow,
  type PokemonWorkflow,
  type TradePokemonWorkflow,
  type TrainersWorkflow
} from '../bridge/contracts';
import { type ProjectBridge } from '../bridge/projectBridge';

type SvBatchFieldFixtureMethods = Pick<
  ProjectBridge,
  | 'updateEncounterSlotFields'
  | 'updateGiftPokemonFields'
  | 'updateItemFields'
  | 'updateMoveFields'
  | 'updatePlacementObjectFields'
  | 'updatePokemonFields'
  | 'updateTradePokemonFields'
  | 'updateTrainerFields'
>;

type ItemMetadata = ItemsWorkflow['items'][number]['metadata'];
type ItemDetailGroups = ItemsWorkflow['items'][number]['detailGroups'];

type IvFieldToKey = Record<
  'ivAttack' | 'ivDefense' | 'ivHp' | 'ivSpecialAttack' | 'ivSpecialDefense' | 'ivSpeed',
  'attack' | 'defense' | 'hp' | 'specialAttack' | 'specialDefense' | 'speed'
>;

type SvBatchFieldBridgeFixtureContext = {
  createItemDetailGroups: (metadata: ItemMetadata) => ItemDetailGroups;
  encountersWorkflow: EncountersWorkflow;
  getGiftPokemonWorkflow: () => GiftPokemonWorkflow;
  getMockPokemonCompatibilityLabel: (
    workflow: PokemonWorkflow,
    personalId: number,
    field: string
  ) => string | null;
  getTradePokemonWorkflow: () => TradePokemonWorkflow;
  itemsWorkflow: ItemsWorkflow;
  ivFieldToKey: IvFieldToKey;
  movesWorkflow: MovesWorkflow;
  placementWorkflow: PlacementWorkflow;
  pokemonWorkflow: PokemonWorkflow;
  setGiftPokemonWorkflow: (workflow: GiftPokemonWorkflow) => void;
  setTradePokemonWorkflow: (workflow: TradePokemonWorkflow) => void;
  trainersWorkflow: TrainersWorkflow;
};

export function createSvBatchFieldBridgeFixtureMethods(
  context: SvBatchFieldBridgeFixtureContext
): SvBatchFieldFixtureMethods {
  return {
    updateEncounterSlotFields: (request) => {
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.encountersWorkflow;

      for (const update of request.updates) {
        pendingEdits.push({
          domain: 'workflow.encounters',
          field: update.field,
          newValue: update.value,
          recordId: `${update.tableId}#${update.slot}`,
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
            }
          ],
          summary: `Set Sword Symbol Zone 0x1122334455667788 Normal slot ${update.slot} probability to ${update.value}.`
        });

        workflow = {
          ...workflow,
          tables: workflow.tables.map((table) =>
            table.tableId === update.tableId
              ? {
                  ...table,
                  slots: table.slots.map((slot) => {
                    if (slot.slot !== update.slot) {
                      return slot;
                    }

                    const value = Number.parseInt(update.value, 10);
                    switch (update.field) {
                      case 'levelMin':
                        return { ...slot, levelMin: value };
                      case 'levelMax':
                        return { ...slot, levelMax: value };
                      case 'species':
                        return { ...slot, speciesId: value };
                      case 'probability':
                      case 'weight':
                        return { ...slot, weight: value };
                      default:
                        return slot;
                    }
                  })
                }
              : table
          )
        };
      }

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    },
    updateGiftPokemonFields: (request) => {
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.getGiftPokemonWorkflow();

      for (const update of request.updates) {
        const value = Number.parseInt(update.value, 10);
        const ivKey = context.ivFieldToKey[update.field as keyof IvFieldToKey] ?? null;
        pendingEdits.push({
          domain: 'workflow.giftPokemon',
          field: update.field,
          newValue: update.value,
          recordId: `gift:${update.giftIndex}`,
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/script_event_data/add_poke.bin'
            }
          ],
          summary: `Set Gift 001 ${update.field} to ${update.value}.`
        });

        workflow = {
          ...workflow,
          gifts: workflow.gifts.map((gift) =>
            gift.giftIndex === update.giftIndex
              ? {
                  ...gift,
                  ivs: ivKey ? { ...gift.ivs, [ivKey]: value } : gift.ivs,
                  ivSummary: ivKey
                    ? `HP ${ivKey === 'hp' ? value : gift.ivs.hp} / Atk ${
                        ivKey === 'attack' ? value : gift.ivs.attack
                      } / Def ${ivKey === 'defense' ? value : gift.ivs.defense} / SpA ${
                        ivKey === 'specialAttack' ? value : gift.ivs.specialAttack
                      } / SpD ${
                        ivKey === 'specialDefense' ? value : gift.ivs.specialDefense
                      } / Spe ${ivKey === 'speed' ? value : gift.ivs.speed}`
                    : gift.ivSummary,
                  level: update.field === 'level' ? value : gift.level,
                  shinyLock: update.field === 'shinyLock' ? value : gift.shinyLock,
                  shinyLockLabel: update.field === 'shinyLock' ? 'Random' : gift.shinyLockLabel
                }
              : gift
          )
        };
      }

      context.setGiftPokemonWorkflow(workflow);
      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    },
    updateItemFields: (request) => {
      const fieldLabels: Record<string, string> = {
        alternatePrice: 'alternate price',
        buyPrice: 'buy price',
        canUseOnPokemon: 'can use on Pokemon',
        evAttack: 'Attack EV gain',
        healAmount: 'heal amount',
        pouch: 'pouch',
        sellPrice: 'sell price',
        wattsPrice: 'Watts price'
      };
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.itemsWorkflow;

      for (const update of request.updates) {
        const fieldLabel = fieldLabels[update.field] ?? update.field;
        pendingEdits.push({
          domain: 'workflow.items',
          field: update.field,
          newValue: update.value,
          recordId: update.itemId.toString(),
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/pml/item/item.dat'
            }
          ],
          summary: `Set Potion ${fieldLabel} to ${update.value}.`
        });

        workflow = {
          ...workflow,
          items: workflow.items.map((item) => {
            if (item.itemId !== update.itemId) {
              return item;
            }

            const value = Number.parseInt(update.value, 10);
            switch (update.field) {
              case 'sellPrice':
                return { ...item, buyPrice: value * 2, sellPrice: value };
              case 'wattsPrice':
                return { ...item, wattsPrice: value };
              case 'alternatePrice':
                return { ...item, alternatePrice: value };
              case 'pouch': {
                const metadata = { ...item.metadata, pouch: value };
                return {
                  ...item,
                  category: value === 4 ? 'Items' : item.category,
                  detailGroups: context.createItemDetailGroups(metadata),
                  metadata
                };
              }
              case 'healAmount': {
                const metadata = { ...item.metadata, healAmount: value };
                return {
                  ...item,
                  detailGroups: context.createItemDetailGroups(metadata),
                  metadata
                };
              }
              case 'evAttack':
                return { ...item, metadata: { ...item.metadata, evAttack: value } };
              case 'canUseOnPokemon':
                return {
                  ...item,
                  metadata: { ...item.metadata, canUseOnPokemon: value !== 0 }
                };
              default:
                return { ...item, buyPrice: value, sellPrice: Math.floor(value / 2) };
            }
          })
        };
      }

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    },
    updateMoveFields: (request) => {
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.movesWorkflow;

      for (const update of request.updates) {
        pendingEdits.push({
          domain: 'workflow.moves',
          field: update.field,
          newValue: update.value,
          recordId: update.moveId.toString(),
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/pml/waza/waza_033.bin'
            }
          ],
          summary:
            update.field === 'makesContact'
              ? `Set Tackle makes contact to ${update.value === '1' ? 'enabled' : 'disabled'}.`
              : `Set Tackle ${update.field} to ${update.value}.`
        });

        workflow = {
          ...workflow,
          moves: workflow.moves.map((move) => {
            if (move.moveId !== update.moveId) {
              return move;
            }

            const value = Number.parseInt(update.value, 10);
            if (update.field === 'makesContact') {
              return {
                ...move,
                flags: move.flags.map((flag) =>
                  flag.field === 'makesContact' ? { ...flag, enabled: value !== 0 } : flag
                )
              };
            }

            return update.field === 'power' ? { ...move, power: value } : move;
          })
        };
      }

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    },
    updatePlacementObjectFields: (request) => {
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.placementWorkflow;

      for (const update of request.updates) {
        pendingEdits.push({
          domain: 'workflow.placement',
          field: update.field,
          newValue: update.value,
          recordId: update.objectId,
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/archive/field/resident/placement.gfpak'
            }
          ],
          summary: `Set Field item: Potion ${update.field} to ${update.value}.`
        });

        workflow = {
          ...workflow,
          objects: workflow.objects.map((placedObject) =>
            placedObject.objectId === update.objectId
              ? {
                  ...placedObject,
                  fields: placedObject.fields?.map((field) =>
                    field.field === update.field
                      ? { ...field, displayValue: update.value, value: update.value }
                      : field
                  ),
                  itemId:
                    update.field === 'itemId'
                      ? Number.parseInt(update.value, 10)
                      : placedObject.itemId,
                  quantity:
                    update.field === 'quantity'
                      ? Number.parseInt(update.value, 10)
                      : placedObject.quantity
                }
              : placedObject
          )
        };
      }

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    },
    updatePokemonFields: (request) => {
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.pokemonWorkflow;

      for (const update of request.updates) {
        pendingEdits.push({
          domain: 'workflow.pokemon',
          field: update.field,
          newValue: update.value,
          recordId: update.personalId.toString(),
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/pml/personal/personal_total.bin'
            }
          ],
          summary:
            update.field.startsWith('compatibility:')
              ? `${update.value === '1' ? 'Enable' : 'Disable'} Bulbasaur ${
                  context.getMockPokemonCompatibilityLabel(
                    workflow,
                    update.personalId,
                    update.field
                  ) ?? update.field
                } compatibility.`
              : update.field === 'canNotDynamax'
              ? `Set Bulbasaur cannot dynamax to ${update.value === '1' ? 'enabled' : 'disabled'}.`
              : `Set Bulbasaur ${update.field} to ${update.value}.`
        });

        workflow = {
          ...workflow,
          pokemon: workflow.pokemon.map((pokemon) => {
            if (pokemon.personalId !== update.personalId) {
              return pokemon;
            }

            const value = Number.parseInt(update.value, 10);
            if (update.field === 'hp') {
              const baseStats = { ...pokemon.baseStats, hp: value };
              return {
                ...pokemon,
                baseStats: {
                  ...baseStats,
                  total:
                    baseStats.hp +
                    baseStats.attack +
                    baseStats.defense +
                    baseStats.specialAttack +
                    baseStats.specialDefense +
                    baseStats.speed
                }
              };
            }

            if (update.field === 'canNotDynamax') {
              return {
                ...pokemon,
                personal: { ...pokemon.personal, canNotDynamax: value !== 0 }
              };
            }

            if (update.field.startsWith('compatibility:')) {
              const [, groupId, slotText] = update.field.split(':');
              const slot = Number.parseInt(slotText ?? '', 10);
              const compatibility = pokemon.compatibility.map((group) => {
                if (group.groupId !== groupId) {
                  return group;
                }

                const entries = group.entries.map((entry) =>
                  entry.slot === slot ? { ...entry, canLearn: value !== 0 } : entry
                );

                return {
                  ...group,
                  enabledCount: entries.filter((entry) => entry.canLearn).length,
                  entries
                };
              });

              return { ...pokemon, compatibility };
            }

            return pokemon;
          })
        };
      }

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    },
    updateTradePokemonFields: (request) => {
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.getTradePokemonWorkflow();

      for (const update of request.updates) {
        const value = Number.parseInt(update.value, 10);
        const ivKey = context.ivFieldToKey[update.field as keyof IvFieldToKey] ?? null;
        pendingEdits.push({
          domain: 'workflow.tradePokemon',
          field: update.field,
          newValue: update.value,
          recordId: `trade:${update.tradeIndex}`,
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/script_event_data/field_trade.bin'
            }
          ],
          summary: `Set Trade 001 ${update.field} to ${update.value}.`
        });

        workflow = {
          ...workflow,
          trades: workflow.trades.map((trade) =>
            trade.tradeIndex === update.tradeIndex
              ? {
                  ...trade,
                  ivs: ivKey ? { ...trade.ivs, [ivKey]: value } : trade.ivs,
                  ivSummary: ivKey
                    ? `HP ${ivKey === 'hp' ? value : trade.ivs.hp} / Atk ${
                        ivKey === 'attack' ? value : trade.ivs.attack
                      } / Def ${ivKey === 'defense' ? value : trade.ivs.defense} / SpA ${
                        ivKey === 'specialAttack' ? value : trade.ivs.specialAttack
                      } / SpD ${
                        ivKey === 'specialDefense' ? value : trade.ivs.specialDefense
                      } / Spe ${ivKey === 'speed' ? value : trade.ivs.speed}`
                    : trade.ivSummary,
                  level: update.field === 'level' ? value : trade.level,
                  shinyLock: update.field === 'shinyLock' ? value : trade.shinyLock,
                  shinyLockLabel: update.field === 'shinyLock' ? 'Random' : trade.shinyLockLabel
                }
              : trade
          )
        };
      }

      context.setTradePokemonWorkflow(workflow);
      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    },
    updateTrainerFields: (request) => {
      const pendingEdits = [...(request.session?.pendingEdits ?? [])];
      let workflow = context.trainersWorkflow;

      for (const update of request.updates) {
        pendingEdits.push({
          domain: 'workflow.trainers',
          field: update.field,
          newValue: update.value,
          recordId: update.slot === null ? update.trainerId.toString() : `${update.trainerId}:${update.slot}`,
          sources: [
            {
              layer: 'base' as const,
              relativePath: update.slot === null
                ? 'romfs/bin/trainer/trainer_data/trainer_010.bin'
                : 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
            }
          ],
          summary:
            update.slot === null
              ? `Set Avery ${update.field} to ${update.value}.`
              : `Set Avery slot ${update.slot} level to ${update.value}.`
        });

        workflow = {
          ...workflow,
          trainers: workflow.trainers.map((trainer) =>
            trainer.trainerId === update.trainerId
              ? {
                  ...trainer,
                  team: trainer.team.map((pokemon) =>
                    pokemon.slot === update.slot
                      ? { ...pokemon, level: Number.parseInt(update.value, 10) }
                      : pokemon
                  )
                }
              : trainer
          )
        };
      }

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow
      });
    }
  };
}
