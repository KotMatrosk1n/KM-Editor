/* SPDX-License-Identifier: GPL-3.0-only */

import {
  apiErrorSchema,
  applyChangePlanRequestSchema,
  applyChangePlanResponseSchema,
  createChangePlanRequestSchema,
  createChangePlanResponseSchema,
  createBridgeRequestSchema,
  createBridgeResponseSchema,
  kmCommandNames,
  listWorkflowsRequestSchema,
  listWorkflowsResponseSchema,
  loadEncountersWorkflowRequestSchema,
  loadEncountersWorkflowResponseSchema,
  loadExeFsPatchWorkflowRequestSchema,
  loadExeFsPatchWorkflowResponseSchema,
  loadFlagworkSaveWorkflowRequestSchema,
  loadFlagworkSaveWorkflowResponseSchema,
  loadItemsWorkflowRequestSchema,
  loadItemsWorkflowResponseSchema,
  loadMovesWorkflowRequestSchema,
  loadMovesWorkflowResponseSchema,
  loadPokemonWorkflowRequestSchema,
  loadPokemonWorkflowResponseSchema,
  loadPlacementWorkflowRequestSchema,
  loadPlacementWorkflowResponseSchema,
  loadRaidRewardsWorkflowRequestSchema,
  loadRaidRewardsWorkflowResponseSchema,
  loadRoyalCandyWorkflowRequestSchema,
  loadRoyalCandyWorkflowResponseSchema,
  loadSpreadsheetImportWorkflowRequestSchema,
  loadSpreadsheetImportWorkflowResponseSchema,
  loadShopsWorkflowRequestSchema,
  loadShopsWorkflowResponseSchema,
  previewSpreadsheetImportRequestSchema,
  previewSpreadsheetImportResponseSchema,
  loadTextWorkflowRequestSchema,
  loadTextWorkflowResponseSchema,
  loadTrainersWorkflowRequestSchema,
  loadTrainersWorkflowResponseSchema,
  openProjectRequestSchema,
  openProjectResponseSchema,
  refreshFileGraphRequestSchema,
  refreshFileGraphResponseSchema,
  startEditSessionRequestSchema,
  startEditSessionResponseSchema,
  updateItemFieldRequestSchema,
  updateItemFieldResponseSchema,
  updatePokemonFieldRequestSchema,
  updatePokemonFieldResponseSchema,
  updatePokemonEvolutionRequestSchema,
  updatePokemonEvolutionResponseSchema,
  updatePokemonLearnsetRequestSchema,
  updatePokemonLearnsetResponseSchema,
  updateMoveFieldRequestSchema,
  updateMoveFieldResponseSchema,
  updateEncounterSlotFieldRequestSchema,
  updateEncounterSlotFieldResponseSchema,
  updatePlacementObjectFieldRequestSchema,
  updatePlacementObjectFieldResponseSchema,
  updateShopInventoryItemRequestSchema,
  updateShopInventoryItemResponseSchema,
  updateTextEntryRequestSchema,
  updateTextEntryResponseSchema,
  updateTrainerFieldRequestSchema,
  updateTrainerFieldResponseSchema,
  validateEditSessionRequestSchema,
  validateEditSessionResponseSchema,
  validateProjectRequestSchema,
  validateProjectResponseSchema
} from './contracts';

const editableHealth = {
  canOpenEditableWorkflows: true,
  canOpenReadOnlyWorkflows: true,
  diagnostics: [],
  fileGraph: {
    baseFileCount: 2,
    layeredFileCount: 1,
    layeredOnlyCount: 0,
    overrideCount: 1
  },
  paths: [
    {
      diagnostics: [],
      isRequired: true,
      path: 'base-romfs',
      role: 'baseRomFs',
      status: 'valid'
    }
  ],
  state: 'editableReady'
} as const;

const fileGraph = {
  entries: [
    {
      baseFile: {
        layer: 'base',
        relativePath: 'romfs/data/items.bin'
      },
      layeredFile: {
        layer: 'layered',
        relativePath: 'romfs/data/items.bin'
      },
      relativePath: 'romfs/data/items.bin',
      state: 'layeredOverride'
    }
  ],
  summary: editableHealth.fileGraph
} as const;

describe('bridge contracts', () => {
  it('validates known command request envelopes', () => {
    const requestSchema = createBridgeRequestSchema(openProjectRequestSchema);

    const parsed = requestSchema.parse({
      command: kmCommandNames.openProject,
      payload: {
        paths: {
          baseExeFsPath: 'base-exefs',
          baseRomFsPath: 'base-romfs',
          outputRootPath: null,
          saveFilePath: null
        }
      },
      requestId: 'request-1'
    });

    expect(parsed.command).toBe('project.open');
  });

  it('validates success and failure response envelopes', () => {
    const responseSchema = createBridgeResponseSchema(openProjectResponseSchema);

    expect(
      responseSchema.safeParse({
        error: null,
        payload: {
          fileGraph,
          health: {
            ...editableHealth
          },
          projectId: 'project-1'
        },
        requestId: 'request-1'
      }).success
    ).toBe(true);

    expect(
      responseSchema.safeParse({
        error: {
          code: 'project.invalidPaths',
          diagnostics: [],
          message: 'Project paths are not valid.'
        },
        payload: null,
        requestId: 'request-2'
      }).success
    ).toBe(true);
  });

  it('rejects ambiguous response envelopes', () => {
    const responseSchema = createBridgeResponseSchema(openProjectResponseSchema);

    expect(
      responseSchema.safeParse({
        error: null,
        payload: null,
        requestId: 'request-3'
      }).success
    ).toBe(false);
  });

  it('validates project validate and file graph refresh envelopes', () => {
    const validateRequestSchema = createBridgeRequestSchema(validateProjectRequestSchema);
    const validateResponseSchema = createBridgeResponseSchema(validateProjectResponseSchema);
    const refreshRequestSchema = createBridgeRequestSchema(refreshFileGraphRequestSchema);
    const refreshResponseSchema = createBridgeResponseSchema(refreshFileGraphResponseSchema);

    expect(
      validateRequestSchema.safeParse({
        command: kmCommandNames.validateProject,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      validateResponseSchema.safeParse({
        payload: {
          health: editableHealth
        }
      }).success
    ).toBe(true);

    expect(
      refreshRequestSchema.safeParse({
        command: kmCommandNames.refreshFileGraph,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      refreshResponseSchema.safeParse({
        payload: {
          fileGraph
        }
      }).success
    ).toBe(true);
  });

  it('validates workflow list and workflow load envelopes', () => {
    const workflowsRequestSchema = createBridgeRequestSchema(listWorkflowsRequestSchema);
    const workflowsResponseSchema = createBridgeResponseSchema(listWorkflowsResponseSchema);
    const itemsRequestSchema = createBridgeRequestSchema(loadItemsWorkflowRequestSchema);
    const itemsResponseSchema = createBridgeResponseSchema(loadItemsWorkflowResponseSchema);
    const pokemonRequestSchema = createBridgeRequestSchema(loadPokemonWorkflowRequestSchema);
    const pokemonResponseSchema = createBridgeResponseSchema(loadPokemonWorkflowResponseSchema);
    const movesRequestSchema = createBridgeRequestSchema(loadMovesWorkflowRequestSchema);
    const movesResponseSchema = createBridgeResponseSchema(loadMovesWorkflowResponseSchema);
    const textRequestSchema = createBridgeRequestSchema(loadTextWorkflowRequestSchema);
    const textResponseSchema = createBridgeResponseSchema(loadTextWorkflowResponseSchema);
    const trainersRequestSchema = createBridgeRequestSchema(loadTrainersWorkflowRequestSchema);
    const trainersResponseSchema = createBridgeResponseSchema(loadTrainersWorkflowResponseSchema);
    const shopsRequestSchema = createBridgeRequestSchema(loadShopsWorkflowRequestSchema);
    const shopsResponseSchema = createBridgeResponseSchema(loadShopsWorkflowResponseSchema);
    const encountersRequestSchema = createBridgeRequestSchema(
      loadEncountersWorkflowRequestSchema
    );
    const encountersResponseSchema = createBridgeResponseSchema(
      loadEncountersWorkflowResponseSchema
    );
    const raidRewardsRequestSchema = createBridgeRequestSchema(
      loadRaidRewardsWorkflowRequestSchema
    );
    const raidRewardsResponseSchema = createBridgeResponseSchema(
      loadRaidRewardsWorkflowResponseSchema
    );
    const placementRequestSchema = createBridgeRequestSchema(loadPlacementWorkflowRequestSchema);
    const placementResponseSchema = createBridgeResponseSchema(
      loadPlacementWorkflowResponseSchema
    );
    const flagworkSaveRequestSchema = createBridgeRequestSchema(
      loadFlagworkSaveWorkflowRequestSchema
    );
    const flagworkSaveResponseSchema = createBridgeResponseSchema(
      loadFlagworkSaveWorkflowResponseSchema
    );
    const exeFsPatchRequestSchema = createBridgeRequestSchema(loadExeFsPatchWorkflowRequestSchema);
    const exeFsPatchResponseSchema = createBridgeResponseSchema(
      loadExeFsPatchWorkflowResponseSchema
    );
    const royalCandyRequestSchema = createBridgeRequestSchema(loadRoyalCandyWorkflowRequestSchema);
    const royalCandyResponseSchema = createBridgeResponseSchema(
      loadRoyalCandyWorkflowResponseSchema
    );
    const spreadsheetImportRequestSchema = createBridgeRequestSchema(
      loadSpreadsheetImportWorkflowRequestSchema
    );
    const spreadsheetImportResponseSchema = createBridgeResponseSchema(
      loadSpreadsheetImportWorkflowResponseSchema
    );
    const spreadsheetImportPreviewRequestSchema = createBridgeRequestSchema(
      previewSpreadsheetImportRequestSchema
    );
    const spreadsheetImportPreviewResponseSchema = createBridgeResponseSchema(
      previewSpreadsheetImportResponseSchema
    );
    const itemsWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'buyPrice',
          label: 'Buy price',
          maximumValue: 999999,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'sellPrice',
          label: 'Sell price',
          maximumValue: 499999,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'wattsPrice',
          label: 'Watts price',
          maximumValue: 999999,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'alternatePrice',
          label: 'Alternate price',
          maximumValue: 999999,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      items: [
        {
          alternatePrice: 3,
          buyPrice: 300,
          category: 'Medicine',
          itemId: 1,
          name: 'Potion',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/item/item.dat',
            sourceLayer: 'base'
          },
          sellPrice: 150,
          sharedItemIds: [1],
          wattsPrice: 15
        }
      ],
      stats: {
        sourceFileCount: 2,
        totalItemCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Item records, names, and source provenance.',
        diagnostics: [],
        id: 'items',
        label: 'Items'
      }
    } as const;
    const pokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'hp',
          group: 'Base Stats',
          label: 'HP',
          maximumValue: 255,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        }
      ],
      pokemon: [
        {
          abilities: {
            ability1: 65,
            ability2: 0,
            hiddenAbility: 34
          },
          baseExperience: 64,
          baseStats: {
            attack: 49,
            defense: 49,
            hp: 45,
            specialAttack: 65,
            specialDefense: 65,
            speed: 45,
            total: 318
          },
          catchRate: 45,
          compatibility: [
            {
              enabledCount: 1,
              entries: [
                {
                  canLearn: true,
                  label: 'TM10 Magical Leaf',
                  moveId: 345,
                  moveName: 'Magical Leaf',
                  slot: 10
                }
              ],
              groupId: 'tm',
              label: 'TMs'
            }
          ],
          dexPresence: {
            armorDexIndex: 0,
            crownDexIndex: 0,
            isInAnyDex: true,
            isPresentInGame: true,
            regionalDexIndex: 1
          },
          evolutionStage: 1,
          evolutions: [
            {
              argument: 0,
              form: 0,
              level: 16,
              method: 4,
              slot: 0,
              species: 2
            }
          ],
          form: 0,
          formLabel: 'Base',
          genderRatio: 31,
          height: 7,
          learnset: [
            {
              level: 1,
              moveId: 33,
              moveName: 'Tackle',
              slot: 0
            }
          ],
          name: 'Bulbasaur',
          personal: {
            baseFriendship: 70,
            canNotDynamax: false,
            catchRate: 45,
            color: 5,
            eggGroup1: 7,
            eggGroup2: 1,
            evYieldAttack: 0,
            evYieldDefense: 0,
            evYieldHP: 0,
            evYieldSpecialAttack: 1,
            evYieldSpecialDefense: 0,
            evYieldSpeed: 0,
            evolutionStage: 1,
            expGrowth: 4,
            form: 0,
            formCount: 1,
            formStatsIndex: 0,
            genderRatio: 31,
            hasSpriteForm: false,
            hatchedSpecies: 1,
            hatchCycles: 20,
            heldItem1: 0,
            heldItem2: 0,
            heldItem3: 0,
            isPresentInGame: true,
            isRegionalForm: false,
            localFormIndex: 0,
            modelId: 1,
            type1: 11,
            type2: 3
          },
          personalId: 1,
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/personal/personal_total.bin',
            sourceLayer: 'base'
          },
          speciesId: 1,
          type1: 'Grass',
          type2: 'Poison',
          weight: 69
        }
      ],
      stats: {
        presentPokemonCount: 1,
        sourceFileCount: 4,
        totalEvolutionCount: 1,
        totalLearnsetMoveCount: 1,
        totalPokemonCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
        diagnostics: [],
        id: 'pokemon',
        label: 'Pokemon Data'
      }
    } as const;
    const movesWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'power',
          label: 'Power',
          maximumValue: 255,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'makesContact',
          label: 'Makes contact',
          maximumValue: 1,
          minimumValue: 0,
          valueKind: 'boolean'
        }
      ],
      moves: [
        {
          accuracy: 100,
          canUseMove: true,
          category: 1,
          categoryName: 'Physical',
          critStage: 0,
          description: 'A physical attack in which the user charges and slams into the target.',
          effectSequence: 12,
          flags: [
            {
              enabled: true,
              field: 'makesContact',
              label: 'Makes Contact'
            },
            {
              enabled: true,
              field: 'protect',
              label: 'Blocked By Protect'
            }
          ],
          flinch: 0,
          hitMax: 1,
          hitMin: 1,
          inflict: 0,
          inflictName: 'None',
          inflictPercent: 0,
          maxMovePower: 90,
          moveId: 33,
          name: 'Tackle',
          power: 40,
          pp: 35,
          priority: 0,
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/waza/waza_033.bin',
            sourceLayer: 'base'
          },
          quality: 2,
          rawHealing: 0,
          rawInflictCount: 0,
          recoil: 0,
          statChanges: [
            {
              percent: 0,
              slot: 1,
              stage: 0,
              stat: 0,
              statName: 'None'
            },
            {
              percent: 30,
              slot: 2,
              stage: -1,
              stat: 1,
              statName: 'Attack'
            }
          ],
          target: 3,
          targetName: 'Opponent',
          turnMax: 0,
          turnMin: 0,
          type: 0,
          typeName: 'Normal',
          version: 1
        }
      ],
      stats: {
        activeFlagCount: 2,
        enabledMoveCount: 1,
        sourceFileCount: 4,
        totalMoveCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Move stats, target behavior, secondary effects, flags, and source provenance.',
        diagnostics: [],
        id: 'moves',
        label: 'Moves Data'
      }
    } as const;
    const textWorkflow = {
      diagnostics: [],
      dialogueReferences: [
        {
          context: 'common/story.dat',
          dialogueId: 'common/story:0',
          label: 'story #0',
          preview: 'Welcome to the lab.',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/message/English/common/story.dat',
            sourceLayer: 'base'
          },
          textId: 0
        }
      ],
      editableFields: [
        {
          field: 'value',
          label: 'Text value',
          maximumLength: 4096,
          minimumLength: 0,
          valueKind: 'multilineText'
        }
      ],
      entries: [
        {
          canEdit: true,
          editBlockedReason: null,
          label: 'story #0',
          language: 'English',
          lineIndex: 0,
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/message/English/common/story.dat',
            sourceLayer: 'base'
          },
          sourceFile: 'romfs/bin/message/English/common/story.dat',
          textId: 0,
          textKey: 'romfs/bin/message/English/common/story.dat#0',
          value: 'Welcome to the lab.'
        }
      ],
      stats: {
        dialogueReferenceCount: 1,
        sourceFileCount: 1,
        totalTextEntryCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Text entries, dialogue references, and source provenance.',
        diagnostics: [],
        id: 'text',
        label: 'Text and Dialogue Map'
      }
    } as const;
    const trainersWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'level',
          label: 'Level',
          maximumValue: 100,
          minimumValue: 1,
          valueKind: 'integer'
        }
      ],
      stats: {
        sourceFileCount: 2,
        totalPokemonCount: 1,
        totalTrainerCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Trainer parties, classes, battle types, and source provenance.',
        diagnostics: [],
        id: 'trainers',
        label: 'Trainers'
      },
      trainers: [
        {
          battleType: 'Doubles',
          battleTypeValue: 1,
          location: 'Trainer 10',
          name: 'Avery',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/trainer/trainer_data/trainer_010.bin',
            sourceLayer: 'base',
            teamFileState: 'baseOnly',
            teamSourceFile: 'romfs/bin/trainer/trainer_poke/trainer_010.bin',
            teamSourceLayer: 'base'
          },
          team: [
            {
              ability: 2,
              canDynamax: false,
              canGigantamax: true,
              dynamaxLevel: 7,
              evs: {
                attack: 20,
                defense: 30,
                hp: 10,
                specialAttack: 40,
                specialDefense: 50,
                speed: 60
              },
              form: 0,
              gender: 1,
              heldItem: 'Potion',
              heldItemId: 1,
              ivs: {
                attack: 2,
                defense: 3,
                hp: 1,
                specialAttack: 5,
                specialDefense: 6,
                speed: 4
              },
              level: 12,
              moveIds: [1, 2, 0, 0],
              moves: ['Scratch', 'Growl', 'None', 'None'],
              nature: 13,
              shiny: true,
              slot: 1,
              species: 'Grookey',
              speciesId: 810
            }
          ],
          trainerClass: 'Pokemon Trainer',
          trainerClassId: 5,
          trainerId: 10
        }
      ]
    } as const;
    const shopsWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'itemId',
          label: 'Item ID',
          maximumValue: 65535,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      shops: [
        {
          currency: 'Money',
          inventory: [
            {
              itemId: 1,
              itemName: 'Potion',
              price: 300,
              slot: 1,
              stockLimit: null
            }
          ],
          location: 'Route 1',
          name: 'Route 1 Mart',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/kmeditor/shops.readmodel.json',
            sourceLayer: 'base'
          },
          shopId: 'route_1_mart'
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalInventoryItemCount: 1,
        totalShopCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Shop inventories, prices, stock limits, and source provenance.',
        diagnostics: [],
        id: 'shops',
        label: 'Shops'
      }
    } as const;
    const encountersWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'speciesId',
          label: 'Species ID',
          maximumValue: 65535,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'probability',
          label: 'Probability',
          maximumValue: 100,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalSlotCount: 1,
        totalTableCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
        diagnostics: [],
        id: 'encounters',
        label: 'Encounters and Wild Data'
      },
      tables: [
        {
          archiveMember: 'encount_symbol_k.bin',
          area: 'Symbol',
          encounterType: 'Normal',
          gameVersion: 'Sword',
          location: 'Zone 0x1122334455667788',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
            sourceLayer: 'base'
          },
          slots: [
            {
              form: 0,
              levelMax: 8,
              levelMin: 3,
              slot: 1,
              speciesId: 1,
              species: 'Bulbasaur',
              timeOfDay: null,
              weather: 'Normal',
              weight: 35
            }
          ],
          tableId: 'sword:symbol:0:1122334455667788:0'
        }
      ]
    } as const;
    const raidRewardsWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'itemId',
          label: 'Item ID',
          maximumValue: 65535,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalRewardItemCount: 1,
        totalTableCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
        diagnostics: [],
        id: 'raidRewards',
        label: 'Raid Rewards'
      },
      tables: [
        {
          archiveMember: 'nest_hole_drop_rewards.bin',
          denId: 'table_AABBCCDD00112233',
          gameVersion: 'Sword/Shield',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
            sourceLayer: 'base'
          },
          rank: 0,
          rewardKind: 'drop',
          rewardKindLabel: 'Drop',
          rewards: [
            {
              entryId: 10,
              itemId: 3,
              itemName: 'Exp. Candy L',
              quantity: 0,
              slot: 1,
              values: [40, 30, 20, 10, 5],
              weight: 40
            }
          ],
          sourceTableHash: '0xAABBCCDD00112233',
          tableId: 'drop:0:AABBCCDD00112233',
          tableIndex: 0
        }
      ]
    } as const;
    const placementWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'itemId',
          label: 'Item ID',
          maximumValue: 65535,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      objects: [
        {
          archiveMember: 'a_test.bin',
          chance: 50,
          chanceIndex: 0,
          itemHash: '0xAABBCCDD00112233',
          itemId: 1,
          itemName: 'Potion',
          label: 'Hidden item: Potion',
          map: 'Route 1',
          objectId: 'a_test.bin|0|hiddenItem|0|0',
          objectIndex: 0,
          objectType: 'HiddenItem',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/archive/field/resident/placement.gfpak',
            sourceLayer: 'base'
          },
          quantity: 2,
          rotationY: 90,
          scriptId: 'hidden_item',
          x: 10.5,
          y: 0,
          zoneIndex: 0,
          z: -4.25
        }
      ],
      stats: {
        sourceFileCount: 3,
        totalAreaCount: 1,
        totalObjectCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Placed objects, map coordinates, item pickups, and source provenance.',
        diagnostics: [],
        id: 'placement',
        label: 'Placement'
      }
    } as const;
    const flagworkSaveWorkflow = {
      diagnostics: [],
      flags: [
        {
          category: 'system_flags',
          defaultValue: 'false',
          description: 'Flag hash 0x1122334455667788 uses save key 0x55667788.',
          flagId: 'system_flags:0000',
          hash: '0x1122334455667788',
          index: 0,
          kind: 'Flag',
          low32Key: '0x55667788',
          name: 'FE_TEST_FLAG',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/flagwork/system_flags.tbl',
            sourceLayer: 'base'
          },
          table: 'system_flags',
          valueKind: 'boolean'
        }
      ],
      saveBlocks: [
        {
          blockId: 'scene_work:0000:0xDDEEFF00',
          description: 'Save work key 0xDDEEFF00 is derived from WK_SCENE_MAIN.',
          hash: '0x99AABBCCDDEEFF00',
          key: '0xDDEEFF00',
          kind: 'Work',
          name: 'WK_SCENE_MAIN',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/flagwork/scene_work.tbl',
            sourceLayer: 'base'
          },
          valueKind: 'integer'
        }
      ],
      saveFile: {
        description: 'Save file is configured for read-only inspection.',
        fileName: 'main',
        sha256: '01020304',
        sizeBytes: 4,
        status: 'available'
      },
      stats: {
        hasSaveFile: true,
        sourceFileCount: 2,
        totalFlagCount: 1,
        totalSaveBlockCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Flagwork hash tables, save keys, and source provenance.',
        diagnostics: [],
        id: 'flagworkSave',
        label: 'Flagwork and Save Inspectors'
      }
    } as const;
    const exeFsPatchWorkflow = {
      diagnostics: [],
      checks: [
        {
          actual: 'text+0x7BC338',
          area: '.text',
          checkId: 'exefs-main-compatibility:patch-code-cave',
          expected: '12 zero bytes after text+0x7BC338',
          name: 'Patch code cave',
          notes: 'A code cave is available for small stubs.',
          offset: 'text+0x7BC338',
          patchId: 'exefs-main-compatibility',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'exefs/main',
            sourceLayer: 'base'
          },
          status: 'Pass'
        }
      ],
      patches: [
        {
          description:
            'Validates Sword/Shield ExeFS main structure, segment hashes, code-cave availability, and known patch anchors.',
          details: ['Build ID: ABAB', 'Checks: 26 total, 0 failing, 0 warnings'],
          name: 'ExeFS main compatibility',
          patchId: 'exefs-main-compatibility',
          patchKind: 'NSO signature scan',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'exefs/main',
            sourceLayer: 'base'
          },
          status: 'available',
          targetFile: 'exefs/main'
        }
      ],
      segments: [
        {
          compressedSize: '0x7DDA90',
          decompressedSize: '0x7DDA90',
          fileOffset: 'file+0x100',
          hashStatus: 'Pass',
          memoryOffset: '0x0',
          name: '.text',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'exefs/main',
            sourceLayer: 'base'
          },
          segmentId: 'text',
          sha256: 'ABCD'
        }
      ],
      stats: {
        failCount: 0,
        passCount: 24,
        sourceFileCount: 1,
        totalCheckCount: 26,
        totalPatchCount: 1,
        warningCount: 0
      },
      summary: {
        availability: 'readOnly',
        description: 'ExeFS main validation, patch anchors, segment hashes, and source provenance.',
        diagnostics: [],
        id: 'exefsPatches',
        label: 'ExeFS Patch Manager'
      }
    } as const;
    const royalCandyWorkflow = {
      checks: [
        {
          area: 'RomFS',
          checkId: 'royal-candy-preflight:item-data',
          message: 'Item data found.',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/item/item.dat',
            sourceLayer: 'base'
          },
          status: 'Pass',
          target: 'romfs/bin/pml/item/item.dat',
          workflowId: 'royal-candy-preflight'
        }
      ],
      diagnostics: [],
      outputs: [
        {
          description: 'Royal Candy item row patch.',
          outputId: 'royal-candy-unlimited:romfs/bin/pml/item/item.dat',
          outputKind: 'RomFS data',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/item/item.dat',
            sourceLayer: 'base'
          },
          relativePath: 'romfs/bin/pml/item/item.dat',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          status: 'ready',
          workflowId: 'royal-candy-unlimited'
        }
      ],
      stats: {
        failCount: 0,
        outputCount: 1,
        passCount: 1,
        sourceFileCount: 1,
        totalCheckCount: 1,
        totalStepCount: 1,
        totalWorkflowCount: 1,
        warningCount: 0
      },
      summary: {
        availability: 'readOnly',
        description: 'Royal Candy source readiness, ExeFS compatibility, and LayeredFS output preview.',
        diagnostics: [],
        id: 'royalCandy',
        label: 'Royal Candy Workflows'
      },
      workflows: [
        {
          category: 'Build',
          description: 'Prepares Royal Candy item 1128 from Rare Candy item 50.',
          itemId: 1128,
          mode: 'unlimited',
          name: 'Install Unlimited Royal Candy',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/item/item.dat',
            sourceLayer: 'base'
          },
          status: 'available',
          steps: [
            {
              description: 'Review target item and output preview.',
              label: 'Review target',
              step: 1
            }
          ],
          target: 'RomFS + ExeFS LayeredFS',
          templateItemId: 50,
          workflowId: 'royal-candy-unlimited'
        }
      ]
    } as const;
    const spreadsheetImportWorkflow = {
      diagnostics: [],
      profiles: [
        {
          columns: [
            {
              column: 1,
              description: 'Item identifier.',
              header: 'ItemId',
              isRequired: true,
              valueKind: 'integer'
            }
          ],
          description: 'Imports item price columns into the Items workflow for change-plan review.',
          name: 'Items Price CSV/TSV',
          profileId: 'items-price-csv',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/item/item.dat',
            sourceLayer: 'base'
          },
          sourceKind: 'csv/tsv',
          status: 'available',
          targetWorkflow: 'items'
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalColumnCount: 1,
        totalProfileCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'CSV and TSV import profiles that execute through backend edit sessions.',
        diagnostics: [],
        id: 'spreadsheetImport',
        label: 'Spreadsheet Import'
      }
    } as const;

    expect(
      workflowsRequestSchema.safeParse({
        command: kmCommandNames.listWorkflows,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      workflowsResponseSchema.safeParse({
        payload: {
          workflows: [
            itemsWorkflow.summary,
            pokemonWorkflow.summary,
            movesWorkflow.summary,
            textWorkflow.summary,
            trainersWorkflow.summary,
            shopsWorkflow.summary,
            encountersWorkflow.summary,
            raidRewardsWorkflow.summary,
            placementWorkflow.summary,
            flagworkSaveWorkflow.summary,
            exeFsPatchWorkflow.summary,
            royalCandyWorkflow.summary,
            spreadsheetImportWorkflow.summary
          ]
        }
      }).success
    ).toBe(true);

    expect(
      itemsRequestSchema.safeParse({
        command: kmCommandNames.loadItemsWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      itemsResponseSchema.safeParse({
        payload: {
          workflow: itemsWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      pokemonRequestSchema.safeParse({
        command: kmCommandNames.loadPokemonWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      pokemonResponseSchema.safeParse({
        payload: {
          workflow: pokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      movesRequestSchema.safeParse({
        command: kmCommandNames.loadMovesWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      movesResponseSchema.safeParse({
        payload: {
          workflow: movesWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      textRequestSchema.safeParse({
        command: kmCommandNames.loadTextWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      textResponseSchema.safeParse({
        payload: {
          workflow: textWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      trainersRequestSchema.safeParse({
        command: kmCommandNames.loadTrainersWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      trainersResponseSchema.safeParse({
        payload: {
          workflow: trainersWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      shopsRequestSchema.safeParse({
        command: kmCommandNames.loadShopsWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      shopsResponseSchema.safeParse({
        payload: {
          workflow: shopsWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      encountersRequestSchema.safeParse({
        command: kmCommandNames.loadEncountersWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      encountersResponseSchema.safeParse({
        payload: {
          workflow: encountersWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      raidRewardsRequestSchema.safeParse({
        command: kmCommandNames.loadRaidRewardsWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      raidRewardsResponseSchema.safeParse({
        payload: {
          workflow: raidRewardsWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      placementRequestSchema.safeParse({
        command: kmCommandNames.loadPlacementWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      placementResponseSchema.safeParse({
        payload: {
          workflow: placementWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      flagworkSaveRequestSchema.safeParse({
        command: kmCommandNames.loadFlagworkSaveWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      flagworkSaveResponseSchema.safeParse({
        payload: {
          workflow: flagworkSaveWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      exeFsPatchRequestSchema.safeParse({
        command: kmCommandNames.loadExeFsPatchWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      exeFsPatchResponseSchema.safeParse({
        payload: {
          workflow: exeFsPatchWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      royalCandyRequestSchema.safeParse({
        command: kmCommandNames.loadRoyalCandyWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      royalCandyResponseSchema.safeParse({
        payload: {
          workflow: royalCandyWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      spreadsheetImportRequestSchema.safeParse({
        command: kmCommandNames.loadSpreadsheetImportWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null,
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      spreadsheetImportResponseSchema.safeParse({
        payload: {
          workflow: spreadsheetImportWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      spreadsheetImportPreviewRequestSchema.safeParse({
        command: kmCommandNames.previewSpreadsheetImport,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          profileId: 'items-price-csv',
          session: null,
          sourcePath: 'items.csv'
        }
      }).success
    ).toBe(true);

    expect(
      spreadsheetImportPreviewResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          preview: {
            acceptedRowCount: 1,
            profileId: 'items-price-csv',
            rejectedRowCount: 0,
            rows: [
              {
                cells: [
                  {
                    field: 'itemId',
                    header: 'ItemId',
                    message: 'Potion',
                    status: 'accepted',
                    value: '1'
                  }
                ],
                diagnostics: [],
                recordId: '1',
                rowNumber: 2,
                status: 'accepted',
                summary: 'Potion: Buy price -> 450.'
              }
            ],
            skippedRowCount: 0,
            sourcePath: 'items.csv',
            totalRowCount: 1
          },
          session: {
            hasPendingChanges: true,
            pendingEdits: [
              {
                domain: 'workflow.items',
                field: 'buyPrice',
                newValue: '450',
                recordId: '1',
                sources: [
                  {
                    layer: 'base',
                    relativePath: 'romfs/bin/pml/item/item.dat'
                  }
                ],
                summary: 'Set Potion buy price to 450.'
              }
            ],
            sessionId: 'session-1'
          },
          workflow: spreadsheetImportWorkflow
        }
      }).success
    ).toBe(true);
  });

  it('validates edit session and Items buy price update envelopes', () => {
    const startRequestSchema = createBridgeRequestSchema(startEditSessionRequestSchema);
    const startResponseSchema = createBridgeResponseSchema(startEditSessionResponseSchema);
    const updateRequestSchema = createBridgeRequestSchema(updateItemFieldRequestSchema);
    const updateResponseSchema = createBridgeResponseSchema(updateItemFieldResponseSchema);
    const updatePokemonRequestSchema = createBridgeRequestSchema(updatePokemonFieldRequestSchema);
    const updatePokemonResponseSchema = createBridgeResponseSchema(updatePokemonFieldResponseSchema);
    const updatePokemonLearnsetBridgeRequestSchema = createBridgeRequestSchema(
      updatePokemonLearnsetRequestSchema
    );
    const updatePokemonLearnsetBridgeResponseSchema = createBridgeResponseSchema(
      updatePokemonLearnsetResponseSchema
    );
    const updatePokemonEvolutionBridgeRequestSchema = createBridgeRequestSchema(
      updatePokemonEvolutionRequestSchema
    );
    const updatePokemonEvolutionBridgeResponseSchema = createBridgeResponseSchema(
      updatePokemonEvolutionResponseSchema
    );
    const updateMoveRequestSchema = createBridgeRequestSchema(updateMoveFieldRequestSchema);
    const updateMoveResponseSchema = createBridgeResponseSchema(updateMoveFieldResponseSchema);
    const updateTextRequestSchema = createBridgeRequestSchema(updateTextEntryRequestSchema);
    const updateTextResponseSchema = createBridgeResponseSchema(updateTextEntryResponseSchema);
    const updateTrainerRequestSchema = createBridgeRequestSchema(updateTrainerFieldRequestSchema);
    const updateTrainerResponseSchema = createBridgeResponseSchema(updateTrainerFieldResponseSchema);
    const updateShopRequestSchema = createBridgeRequestSchema(
      updateShopInventoryItemRequestSchema
    );
    const updateShopResponseSchema = createBridgeResponseSchema(
      updateShopInventoryItemResponseSchema
    );
    const updateEncounterRequestSchema = createBridgeRequestSchema(
      updateEncounterSlotFieldRequestSchema
    );
    const updateEncounterResponseSchema = createBridgeResponseSchema(
      updateEncounterSlotFieldResponseSchema
    );
    const updatePlacementRequestSchema = createBridgeRequestSchema(
      updatePlacementObjectFieldRequestSchema
    );
    const updatePlacementResponseSchema = createBridgeResponseSchema(
      updatePlacementObjectFieldResponseSchema
    );
    const validateRequestSchema = createBridgeRequestSchema(validateEditSessionRequestSchema);
    const validateResponseSchema = createBridgeResponseSchema(validateEditSessionResponseSchema);
    const changePlanRequestSchema = createBridgeRequestSchema(createChangePlanRequestSchema);
    const changePlanResponseSchema = createBridgeResponseSchema(createChangePlanResponseSchema);
    const applyRequestSchema = createBridgeRequestSchema(applyChangePlanRequestSchema);
    const applyResponseSchema = createBridgeResponseSchema(applyChangePlanResponseSchema);
    const editSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.items',
          field: 'buyPrice',
          newValue: '450',
          recordId: '1',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/pml/item/item.dat'
            }
          ],
          summary: 'Set Potion buy price to 450.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const changePlan = {
      canApply: true,
      diagnostics: [
        {
          message: 'Change plan preview contains 1 target file.',
          severity: 'info'
        }
      ],
      sessionId: 'session-1',
      writes: [
        {
          reason: 'Apply pending Items edit: Set Potion buy price to 450.',
          replacesExistingOutput: false,
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/pml/item/item.dat'
            }
          ],
          targetRelativePath: 'romfs/bin/pml/item/item.dat'
        }
      ]
    } as const;
    const itemsWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'buyPrice',
          label: 'Buy price',
          maximumValue: 999999,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'sellPrice',
          label: 'Sell price',
          maximumValue: 499999,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'wattsPrice',
          label: 'Watts price',
          maximumValue: 999999,
          minimumValue: 0,
          valueKind: 'integer'
        },
        {
          field: 'alternatePrice',
          label: 'Alternate price',
          maximumValue: 999999,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      items: [
        {
          alternatePrice: 3,
          buyPrice: 450,
          category: 'Medicine',
          itemId: 1,
          name: 'Potion',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/item/item.dat',
            sourceLayer: 'base'
          },
          sellPrice: 225,
          sharedItemIds: [1],
          wattsPrice: 15
        }
      ],
      stats: {
        sourceFileCount: 2,
        totalItemCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Item records, names, and source provenance.',
        diagnostics: [],
        id: 'items',
        label: 'Items'
      }
    } as const;
    const pokemonSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.pokemon',
          field: 'hp',
          newValue: '99',
          recordId: '1',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/pml/personal/personal_total.bin'
            }
          ],
          summary: 'Set Bulbasaur hp to 99.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const pokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'hp',
          group: 'Base Stats',
          label: 'HP',
          maximumValue: 255,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        }
      ],
      pokemon: [
        {
          abilities: {
            ability1: 65,
            ability2: 0,
            hiddenAbility: 34
          },
          baseExperience: 64,
          baseStats: {
            attack: 49,
            defense: 49,
            hp: 99,
            specialAttack: 65,
            specialDefense: 65,
            speed: 45,
            total: 372
          },
          catchRate: 45,
          compatibility: [
            {
              enabledCount: 0,
              entries: [
                {
                  canLearn: false,
                  label: 'TR00 Swords Dance',
                  moveId: 14,
                  moveName: 'Swords Dance',
                  slot: 0
                }
              ],
              groupId: 'tr',
              label: 'TRs'
            }
          ],
          dexPresence: {
            armorDexIndex: 0,
            crownDexIndex: 0,
            isInAnyDex: true,
            isPresentInGame: true,
            regionalDexIndex: 1
          },
          evolutionStage: 1,
          evolutions: [],
          form: 0,
          formLabel: 'Base',
          genderRatio: 31,
          height: 7,
          learnset: [],
          name: 'Bulbasaur',
          personal: {
            baseFriendship: 70,
            canNotDynamax: false,
            catchRate: 45,
            color: 5,
            eggGroup1: 7,
            eggGroup2: 1,
            evYieldAttack: 0,
            evYieldDefense: 0,
            evYieldHP: 0,
            evYieldSpecialAttack: 1,
            evYieldSpecialDefense: 0,
            evYieldSpeed: 0,
            evolutionStage: 1,
            expGrowth: 4,
            form: 0,
            formCount: 1,
            formStatsIndex: 0,
            genderRatio: 31,
            hasSpriteForm: false,
            hatchedSpecies: 1,
            hatchCycles: 20,
            heldItem1: 0,
            heldItem2: 0,
            heldItem3: 0,
            isPresentInGame: true,
            isRegionalForm: false,
            localFormIndex: 0,
            modelId: 1,
            type1: 11,
            type2: 3
          },
          personalId: 1,
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/personal/personal_total.bin',
            sourceLayer: 'base'
          },
          speciesId: 1,
          type1: 'Grass',
          type2: 'Poison',
          weight: 69
        }
      ],
      stats: {
        presentPokemonCount: 1,
        sourceFileCount: 1,
        totalEvolutionCount: 0,
        totalLearnsetMoveCount: 0,
        totalPokemonCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
        diagnostics: [],
        id: 'pokemon',
        label: 'Pokemon Data'
      }
    } as const;
    const moveSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.moves',
          field: 'power',
          newValue: '80',
          recordId: '33',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/pml/waza/waza_033.bin'
            }
          ],
          summary: 'Set Tackle power to 80.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const movesWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'power',
          label: 'Power',
          maximumValue: 255,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      moves: [],
      stats: {
        activeFlagCount: 0,
        enabledMoveCount: 0,
        sourceFileCount: 1,
        totalMoveCount: 0
      },
      summary: {
        availability: 'available',
        description: 'Move stats, target behavior, secondary effects, flags, and source provenance.',
        diagnostics: [],
        id: 'moves',
        label: 'Moves Data'
      }
    } as const;
    const textSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.text',
          field: 'value',
          newValue: 'Hello there.',
          recordId: 'romfs/bin/message/English/common/story.dat#0',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/message/English/common/story.dat'
            }
          ],
          summary: 'Set story #0 to "Hello there.".'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const textWorkflow = {
      diagnostics: [],
      dialogueReferences: [
        {
          context: 'common/story.dat',
          dialogueId: 'common/story:0',
          label: 'story #0',
          preview: 'Hello there.',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/message/English/common/story.dat',
            sourceLayer: 'base'
          },
          textId: 0
        }
      ],
      editableFields: [
        {
          field: 'value',
          label: 'Text value',
          maximumLength: 4096,
          minimumLength: 0,
          valueKind: 'multilineText'
        }
      ],
      entries: [
        {
          canEdit: true,
          editBlockedReason: null,
          label: 'story #0',
          language: 'English',
          lineIndex: 0,
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/message/English/common/story.dat',
            sourceLayer: 'base'
          },
          sourceFile: 'romfs/bin/message/English/common/story.dat',
          textId: 0,
          textKey: 'romfs/bin/message/English/common/story.dat#0',
          value: 'Hello there.'
        }
      ],
      stats: {
        dialogueReferenceCount: 1,
        sourceFileCount: 1,
        totalTextEntryCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Text entries, dialogue references, and source provenance.',
        diagnostics: [],
        id: 'text',
        label: 'Text and Dialogue Map'
      }
    } as const;
    const trainerSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.trainers',
          field: 'level',
          newValue: '25',
          recordId: '10:1',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
            }
          ],
          summary: 'Set Avery slot 1 level to 25.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const trainersWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'level',
          label: 'Level',
          maximumValue: 100,
          minimumValue: 1,
          valueKind: 'integer'
        }
      ],
      stats: {
        sourceFileCount: 2,
        totalPokemonCount: 1,
        totalTrainerCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Trainer parties, classes, battle types, and source provenance.',
        diagnostics: [],
        id: 'trainers',
        label: 'Trainers'
      },
      trainers: [
        {
          battleType: 'Doubles',
          battleTypeValue: 1,
          location: 'Trainer 10',
          name: 'Avery',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/trainer/trainer_data/trainer_010.bin',
            sourceLayer: 'base',
            teamFileState: 'baseOnly',
            teamSourceFile: 'romfs/bin/trainer/trainer_poke/trainer_010.bin',
            teamSourceLayer: 'base'
          },
          team: [
            {
              ability: 2,
              canDynamax: false,
              canGigantamax: true,
              dynamaxLevel: 7,
              evs: {
                attack: 20,
                defense: 30,
                hp: 10,
                specialAttack: 40,
                specialDefense: 50,
                speed: 60
              },
              form: 0,
              gender: 1,
              heldItem: 'Potion',
              heldItemId: 1,
              ivs: {
                attack: 2,
                defense: 3,
                hp: 1,
                specialAttack: 5,
                specialDefense: 6,
                speed: 4
              },
              level: 25,
              moveIds: [1, 2, 0, 0],
              moves: ['Scratch', 'Growl', 'None', 'None'],
              nature: 13,
              shiny: true,
              slot: 1,
              species: 'Grookey',
              speciesId: 810
            }
          ],
          trainerClass: 'Pokemon Trainer',
          trainerClassId: 5,
          trainerId: 10
        }
      ]
    } as const;
    const shopSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.shops',
          field: 'itemId',
          newValue: '2',
          recordId: 'single:1F3FF031A3A24490#1',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/app/shop/shop_data.bin'
            }
          ],
          summary: 'Set Poke Mart slot 1 item ID to 2.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const shopsWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'itemId',
          label: 'Item ID',
          maximumValue: 65535,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      shops: [
        {
          currency: 'Money',
          inventory: [
            {
              itemId: 2,
              itemName: 'Antidote',
              price: 200,
              slot: 1,
              stockLimit: null
            }
          ],
          location: 'Poke Mart',
          name: 'Poke Mart',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/app/shop/shop_data.bin',
            sourceLayer: 'base'
          },
          shopId: 'single:1F3FF031A3A24490'
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalInventoryItemCount: 1,
        totalShopCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Shop inventories, item metadata, and source provenance.',
        diagnostics: [],
        id: 'shops',
        label: 'Shops'
      }
    } as const;
    const encounterSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.encounters',
          field: 'probability',
          newValue: '40',
          recordId: 'sword:symbol:0:1122334455667788:0#2',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
            }
          ],
          summary:
            'Set Sword Symbol Zone 0x1122334455667788 Normal slot 2 probability to 40.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const encountersWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'probability',
          label: 'Probability',
          maximumValue: 100,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalSlotCount: 1,
        totalTableCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
        diagnostics: [],
        id: 'encounters',
        label: 'Encounters and Wild Data'
      },
      tables: [
        {
          archiveMember: 'encount_symbol_k.bin',
          area: 'Symbol',
          encounterType: 'Normal',
          gameVersion: 'Sword',
          location: 'Zone 0x1122334455667788',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
            sourceLayer: 'base'
          },
          slots: [
            {
              form: 1,
              levelMax: 8,
              levelMin: 3,
              slot: 2,
              speciesId: 4,
              species: 'Charmander',
              timeOfDay: null,
              weather: 'Normal',
              weight: 40
            }
          ],
          tableId: 'sword:symbol:0:1122334455667788:0'
        }
      ]
    } as const;
    const placementSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.placement',
          field: 'quantity',
          newValue: '5',
          recordId: 'a_test.bin|0|fieldItem|0|-',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/archive/field/resident/placement.gfpak'
            }
          ],
          summary: 'Set Field item: Potion Quantity -> 5'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const placementWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'quantity',
          label: 'Quantity',
          maximumValue: 999,
          minimumValue: 0,
          valueKind: 'integer'
        }
      ],
      objects: [
        {
          archiveMember: 'a_test.bin',
          chance: null,
          chanceIndex: null,
          itemHash: '0xAABBCCDD00112233',
          itemId: 1,
          itemName: 'Potion',
          label: 'Field item: Potion',
          map: 'Route 1',
          objectId: 'a_test.bin|0|fieldItem|0|-',
          objectIndex: 0,
          objectType: 'FieldItem',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/archive/field/resident/placement.gfpak',
            sourceLayer: 'base'
          },
          quantity: 5,
          rotationY: 90,
          scriptId: 'visible_potion',
          x: 10.5,
          y: 0,
          zoneIndex: 0,
          z: -4.25
        }
      ],
      stats: {
        sourceFileCount: 3,
        totalAreaCount: 1,
        totalObjectCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Placed objects, map coordinates, item pickups, and source provenance.',
        diagnostics: [],
        id: 'placement',
        label: 'Placement'
      }
    } as const;

    expect(
      startRequestSchema.safeParse({
        command: kmCommandNames.startEditSession,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          }
        }
      }).success
    ).toBe(true);

    expect(startResponseSchema.safeParse({ payload: { session: editSession } }).success).toBe(true);

    expect(
      updateRequestSchema.safeParse({
        command: kmCommandNames.updateItemField,
        payload: {
          field: 'buyPrice',
          itemId: 1,
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          value: '450'
        }
      }).success
    ).toBe(true);

    expect(
      updateResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: editSession,
          workflow: itemsWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updatePokemonRequestSchema.safeParse({
        command: kmCommandNames.updatePokemonField,
        payload: {
          field: 'hp',
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          personalId: 1,
          session: editSession,
          value: '99'
        }
      }).success
    ).toBe(true);

    expect(
      updatePokemonResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: pokemonSession,
          workflow: pokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updatePokemonLearnsetBridgeRequestSchema.safeParse({
        command: kmCommandNames.updatePokemonLearnset,
        payload: {
          action: 'upsert',
          level: 9,
          moveId: 345,
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          personalId: 1,
          session: editSession,
          slot: 1
        }
      }).success
    ).toBe(true);

    expect(
      updatePokemonLearnsetBridgeResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: pokemonSession,
          workflow: pokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updatePokemonEvolutionBridgeRequestSchema.safeParse({
        command: kmCommandNames.updatePokemonEvolution,
        payload: {
          action: 'upsert',
          argument: 25,
          form: 1,
          level: 32,
          method: 8,
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          personalId: 1,
          session: editSession,
          slot: 0,
          species: 2
        }
      }).success
    ).toBe(true);

    expect(
      updatePokemonEvolutionBridgeResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: pokemonSession,
          workflow: pokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updateMoveRequestSchema.safeParse({
        command: kmCommandNames.updateMoveField,
        payload: {
          field: 'power',
          moveId: 33,
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          value: '80'
        }
      }).success
    ).toBe(true);

    expect(
      updateMoveResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: moveSession,
          workflow: movesWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updateTextRequestSchema.safeParse({
        command: kmCommandNames.updateTextEntry,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          textKey: 'romfs/bin/message/English/common/story.dat#0',
          value: 'Hello there.'
        }
      }).success
    ).toBe(true);

    expect(
      updateTextResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: textSession,
          workflow: textWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updateTrainerRequestSchema.safeParse({
        command: kmCommandNames.updateTrainerField,
        payload: {
          field: 'level',
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          slot: 1,
          trainerId: 10,
          value: '25'
        }
      }).success
    ).toBe(true);

    expect(
      updateTrainerResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: trainerSession,
          workflow: trainersWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updateShopRequestSchema.safeParse({
        command: kmCommandNames.updateShopInventoryItem,
        payload: {
          field: 'itemId',
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          shopId: 'single:1F3FF031A3A24490',
          slot: 1,
          value: '2'
        }
      }).success
    ).toBe(true);

    expect(
      updateShopResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: shopSession,
          workflow: shopsWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updateEncounterRequestSchema.safeParse({
        command: kmCommandNames.updateEncounterSlotField,
        payload: {
          field: 'probability',
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          slot: 2,
          tableId: 'sword:symbol:0:1122334455667788:0',
          value: '40'
        }
      }).success
    ).toBe(true);

    expect(
      updateEncounterResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: encounterSession,
          workflow: encountersWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updatePlacementRequestSchema.safeParse({
        command: kmCommandNames.updatePlacementObjectField,
        payload: {
          field: 'quantity',
          objectId: 'a_test.bin|0|fieldItem|0|-',
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          value: '5'
        }
      }).success
    ).toBe(true);

    expect(
      updatePlacementResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: placementSession,
          workflow: placementWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      validateRequestSchema.safeParse({
        command: kmCommandNames.validateEditSession,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession
        }
      }).success
    ).toBe(true);

    expect(
      validateResponseSchema.safeParse({
        payload: {
          diagnostics: [
            {
              field: 'buyPrice',
              message: 'Pending item change is valid.',
              severity: 'info'
            }
          ],
          isValid: true,
          session: editSession
        }
      }).success
    ).toBe(true);

    expect(
      changePlanRequestSchema.safeParse({
        command: kmCommandNames.createChangePlan,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession
        }
      }).success
    ).toBe(true);

    expect(
      changePlanResponseSchema.safeParse({
        payload: {
          changePlan
        }
      }).success
    ).toBe(true);

    expect(
      applyRequestSchema.safeParse({
        command: kmCommandNames.applyChangePlan,
        payload: {
          changePlan,
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession
        }
      }).success
    ).toBe(true);

    expect(
      applyResponseSchema.safeParse({
        payload: {
          applyResult: {
            applyId: 'apply-1',
            diagnostics: [
              {
                message: 'Applied Items change plan to the configured LayeredFS output root.',
                severity: 'info'
              }
            ],
            writtenFiles: ['romfs/bin/pml/item/item.dat']
          }
        }
      }).success
    ).toBe(true);
  });

  it('validates diagnostic severity strings', () => {
    const parsed = apiErrorSchema.parse({
      code: 'project.invalidPaths',
      diagnostics: [
        {
          message: 'Project paths are not valid.',
          severity: 'warning'
        }
      ],
      message: 'Project paths are not valid.'
    });

    expect(parsed.diagnostics[0]?.severity).toBe('warning');
  });
});
