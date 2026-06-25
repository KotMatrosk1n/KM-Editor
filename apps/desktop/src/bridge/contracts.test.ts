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
  loadGiftPokemonWorkflowRequestSchema,
  loadGiftPokemonWorkflowResponseSchema,
  loadRentalPokemonWorkflowRequestSchema,
  loadRentalPokemonWorkflowResponseSchema,
  loadTradePokemonWorkflowRequestSchema,
  loadTradePokemonWorkflowResponseSchema,
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
  loadRaidBonusRewardsWorkflowRequestSchema,
  loadRaidBonusRewardsWorkflowResponseSchema,
  loadRoyalCandyWorkflowRequestSchema,
  loadRoyalCandyWorkflowResponseSchema,
  loadSpreadsheetImportWorkflowRequestSchema,
  loadSpreadsheetImportWorkflowResponseSchema,
  loadStaticEncountersWorkflowResponseSchema,
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
  updateGiftPokemonFieldRequestSchema,
  updateGiftPokemonFieldResponseSchema,
  updateRentalPokemonFieldRequestSchema,
  updateRentalPokemonFieldResponseSchema,
  updateTradePokemonFieldRequestSchema,
  updateTradePokemonFieldResponseSchema,
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

const itemMetadata = {
  boost0: 0,
  boost1: 0,
  boost2: 0,
  boost3: 0,
  canUseOnPokemon: true,
  cureStatusFlags: 0,
  evAttack: 0,
  evDefense: 0,
  evHp: 0,
  evSpecialAttack: 0,
  evSpecialDefense: 0,
  evSpeed: 0,
  fieldFlags: 2,
  fieldUseType: 1,
  flingPower: 30,
  friendshipGain1: 1,
  friendshipGain2: 1,
  friendshipGain3: 0,
  groupIndex: 0,
  groupType: 0,
  healAmount: 20,
  itemSprite: 12,
  itemType: 9,
  machineMoveId: null,
  machineMoveName: null,
  machineSlot: null,
  pouch: 0,
  pouchFlags: 0,
  ppGain: 0,
  sortIndex: 5,
  useFlags1: 4,
  useFlags2: 0
} as const;

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
            gameTextLanguage: 'es',
            outputRootPath: null,
            saveFilePath: null,
            selectedGame: 'shield'
          }
        },
      requestId: 'request-1'
    });

    expect(parsed.command).toBe('project.open');
    expect(parsed.payload.paths.gameTextLanguage).toBe('es');
    expect(parsed.payload.paths.selectedGame).toBe('shield');
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
    const giftPokemonRequestSchema = createBridgeRequestSchema(loadGiftPokemonWorkflowRequestSchema);
    const giftPokemonResponseSchema = createBridgeResponseSchema(
      loadGiftPokemonWorkflowResponseSchema
    );
    const tradePokemonRequestSchema = createBridgeRequestSchema(loadTradePokemonWorkflowRequestSchema);
    const tradePokemonResponseSchema = createBridgeResponseSchema(
      loadTradePokemonWorkflowResponseSchema
    );
    const rentalPokemonRequestSchema = createBridgeRequestSchema(
      loadRentalPokemonWorkflowRequestSchema
    );
    const rentalPokemonResponseSchema = createBridgeResponseSchema(
      loadRentalPokemonWorkflowResponseSchema
    );
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
    const raidBonusRewardsRequestSchema = createBridgeRequestSchema(
      loadRaidBonusRewardsWorkflowRequestSchema
    );
    const raidBonusRewardsResponseSchema = createBridgeResponseSchema(
      loadRaidBonusRewardsWorkflowResponseSchema
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
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'sellPrice',
          label: 'Sell price',
          maximumValue: 499999,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'wattsPrice',
          label: 'Watts price',
          maximumValue: 999999,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'alternatePrice',
          label: 'Alternate price',
          maximumValue: 999999,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        }
      ],
      items: [
        {
          alternatePrice: 3,
          buyPrice: 300,
          category: 'Medicine',
          detailGroups: [
            {
              details: [
                { label: 'Pouch', value: 'Medicine (0)' },
                { label: 'Machine', value: 'No machine link' }
              ],
              label: 'Inventory'
            },
            {
              details: [{ label: 'Use flags 1', value: 'Restore HP' }],
              label: 'Field Use'
            }
          ],
          itemId: 1,
          metadata: itemMetadata,
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
            ability1Label: 'Overgrow',
            ability2: 0,
            ability2Label: 'None',
            hiddenAbility: 34,
            hiddenAbilityLabel: 'Chlorophyll'
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
          genderRatioLabel: '031 Male 87.5% / Female 12.5%',
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
            },
            {
              percent: 0,
              slot: 3,
              stage: 0,
              stat: -1,
              statName: 'Unused (-1 raw)'
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
          options: [],
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
              abilityLabel: 'Ability 2',
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
              genderLabel: 'Male',
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
              natureLabel: 'Jolly',
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
    const giftPokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'ivHp',
          label: 'HP IV',
          maximumValue: 31,
          minimumValue: -4,
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'flawlessIvCount',
          label: 'IV preset',
          maximumValue: 6,
          minimumValue: 0,
          options: [
            {
              label: 'Random IVs',
              value: 0
            },
            {
              label: '3 Guaranteed Perfect IVs',
              value: 3
            },
            {
              label: '6 Perfect IVs',
              value: 6
            }
          ],
          valueKind: 'integer'
        }
      ],
      gifts: [
        {
          ability: 3,
          abilityLabel: 'Hidden Ability',
          ballItem: 'Poke Ball',
          ballItemId: 4,
          canGigantamax: true,
          dynamaxLevel: 10,
          flawlessIvCount: 0,
          form: 0,
          gender: 0,
          genderLabel: 'Random',
          giftIndex: 0,
          heldItem: null,
          heldItemId: 0,
          isEgg: false,
          ivs: {
            attack: -1,
            defense: -1,
            hp: -1,
            specialAttack: -1,
            specialDefense: -1,
            speed: -1
          },
          ivSummary: 'Random IVs',
          label: 'Gift 001: Grookey Lv. 50',
          level: 50,
          nature: 25,
          natureLabel: 'Random',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/script_event_data/add_poke.bin',
            sourceLayer: 'base'
          },
          shinyLock: 0,
          shinyLockLabel: 'Random',
          specialMove: null,
          specialMoveId: 0,
          species: 'Grookey',
          speciesId: 810
        }
      ],
      stats: {
        eggGiftCount: 0,
        fixedIvGiftCount: 0,
        sourceFileCount: 4,
        totalGiftCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
        diagnostics: [],
        id: 'giftPokemon',
        label: 'Gift Pokemon'
      }
    } as const;
    const tradePokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'ivHp',
          label: 'HP IV',
          maximumValue: 31,
          minimumValue: -4,
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'requiredSpecies',
          label: 'Requested species',
          maximumValue: 65535,
          minimumValue: 0,
          options: [
            {
              label: '133 Eevee',
              value: 133
            }
          ],
          valueKind: 'integer'
        }
      ],
      stats: {
        fixedIvTradeCount: 0,
        sourceFileCount: 4,
        totalTradeCount: 1
      },
      summary: {
        availability: 'readOnly',
        description:
          'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
        diagnostics: [],
        id: 'tradePokemon',
        label: 'Trade Pokemon'
      },
      trades: [
        {
          ability: 1,
          abilityLabel: 'Ability 1',
          ballItem: 'Poke Ball',
          ballItemId: 4,
          canGigantamax: false,
          dynamaxLevel: 0,
          field03: 0,
          flawlessIvCount: 3,
          form: 0,
          gender: 0,
          genderLabel: 'Random',
          hash0: '0x1122334455667788',
          hash1: '0x8877665544332211',
          hash2: '0x0102030405060708',
          heldItem: null,
          heldItemId: 0,
          ivs: {
            attack: -1,
            defense: -1,
            hp: -4,
            specialAttack: -1,
            specialDefense: -1,
            speed: -1
          },
          ivSummary: '3 guaranteed perfect IVs',
          label: 'Trade 001: Eevee -> Grookey Lv. 50',
          level: 50,
          memoryCode: 11,
          memoryFeel: 12,
          memoryIntensity: 13,
          memoryTextVariable: 4660,
          nature: 25,
          natureLabel: 'Random',
          otGender: 1,
          otGenderLabel: 'Female',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/script_event_data/field_trade.bin',
            sourceLayer: 'base'
          },
          relearnMoves: [
            {
              move: 'Scratch',
              moveId: 1,
              slot: 0
            }
          ],
          requiredForm: -1,
          requiredNature: 25,
          requiredNatureLabel: 'Random',
          requiredSpecies: 'Eevee',
          requiredSpeciesId: 133,
          shinyLock: 2,
          shinyLockLabel: 'Never Shiny',
          species: 'Grookey',
          speciesId: 810,
          tradeIndex: 0,
          trainerId: 123456,
          unknownRequirement: 0
        }
      ]
    } as const;
    const rentalPokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'ivHp',
          label: 'HP IV',
          maximumValue: 31,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'fixedIvPreset',
          label: 'Fixed IV preset',
          maximumValue: 31,
          minimumValue: 0,
          options: [
            {
              label: '31 IVs',
              value: 31
            }
          ],
          valueKind: 'integer'
        }
      ],
      rentals: [
        {
          ability: 1,
          abilityLabel: 'Ability 1',
          ballItem: 'Poke Ball',
          ballItemId: 4,
          evs: {
            attack: 252,
            defense: 0,
            hp: 4,
            specialAttack: 0,
            specialDefense: 0,
            speed: 252
          },
          form: 0,
          gender: 1,
          genderLabel: 'Male',
          hash1: '0x1122334455667788',
          hash2: '0x8877665544332211',
          hasPerfectIvs: true,
          heldItem: 'Potion',
          heldItemId: 1,
          ivs: {
            attack: 31,
            defense: 31,
            hp: 31,
            specialAttack: 31,
            specialDefense: 31,
            speed: 31
          },
          ivSummary: 'HP 31 / Atk 31 / Def 31 / SpA 31 / SpD 31 / Spe 31',
          label: 'Rental 001: Grookey Lv. 50',
          level: 50,
          moves: [
            {
              move: 'Scratch',
              moveId: 1,
              slot: 0
            }
          ],
          nature: 0,
          natureLabel: 'Hardy',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/script_event_data/rental.bin',
            sourceLayer: 'base'
          },
          rentalIndex: 0,
          species: 'Grookey',
          speciesId: 810,
          trainerId: 123456
        }
      ],
      stats: {
        perfectIvRentalCount: 1,
        sourceFileCount: 1,
        totalRentalCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Rental Pokemon records, fixed IVs, EVs, items, moves, and source provenance.',
        diagnostics: [],
        id: 'rentalPokemon',
        label: 'Rental Pokemon'
      }
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
          displayName: 'Drop 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1',
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
    const raidBonusRewardsWorkflow = {
      ...raidRewardsWorkflow,
      summary: {
        availability: 'readOnly',
        description: 'Raid bonus reward tables, item quantities, den usage, and source provenance.',
        diagnostics: [],
        id: 'raidBonusRewards',
        label: 'Raid Bonus Rewards'
      },
      tables: raidRewardsWorkflow.tables.map((table) => ({
        ...table,
        archiveMember: 'nest_hole_bonus_rewards.bin',
        displayName: 'Bonus 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1',
        rewardKind: 'bonus',
        rewardKindLabel: 'Bonus',
        sourceTableHash: '0x1020304050607080',
        tableId: 'bonus:0:1020304050607080'
      }))
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
          name: 'Unlimited Royal Candy',
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
          description: 'Imports supported item price dump files into the Items workflow for change-plan review.',
          name: 'Items Price Dump',
          profileId: 'items-price-csv',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/pml/item/item.dat',
            sourceLayer: 'base'
          },
          sourceKind: 'csv/tsv/json',
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
        description: 'CSV, TSV, and JSON import profiles that execute through backend edit sessions.',
        diagnostics: [],
        id: 'spreadsheetImport',
        label: 'Dump Importer'
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
            giftPokemonWorkflow.summary,
            tradePokemonWorkflow.summary,
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

    const svPokemonWorkflow = {
      ...pokemonWorkflow,
      pokemon: [
        {
          ...pokemonWorkflow.pokemon[0],
          baseExperience: -12,
          personal: {
            ...pokemonWorkflow.pokemon[0].personal,
            baseExperience: -12
          }
        }
      ]
    };

    expect(
      pokemonResponseSchema.safeParse({
        payload: {
          workflow: svPokemonWorkflow
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
      giftPokemonRequestSchema.safeParse({
        command: kmCommandNames.loadGiftPokemonWorkflow,
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
      giftPokemonResponseSchema.safeParse({
        payload: {
          workflow: giftPokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      tradePokemonRequestSchema.safeParse({
        command: kmCommandNames.loadTradePokemonWorkflow,
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
      tradePokemonResponseSchema.safeParse({
        payload: {
          workflow: tradePokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      rentalPokemonRequestSchema.safeParse({
        command: kmCommandNames.loadRentalPokemonWorkflow,
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
      rentalPokemonResponseSchema.safeParse({
        payload: {
          workflow: rentalPokemonWorkflow
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
      raidBonusRewardsRequestSchema.safeParse({
        command: kmCommandNames.loadRaidBonusRewardsWorkflow,
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
      raidBonusRewardsResponseSchema.safeParse({
        payload: {
          workflow: raidBonusRewardsWorkflow
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
      placementResponseSchema.safeParse({
        payload: {
          workflow: {
            ...placementWorkflow,
            categories: null,
            objects: placementWorkflow.objects.map((object) => ({
              ...object,
              fields: null
            }))
          }
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
    const updateGiftPokemonRequestSchema = createBridgeRequestSchema(
      updateGiftPokemonFieldRequestSchema
    );
    const updateGiftPokemonResponseSchema = createBridgeResponseSchema(
      updateGiftPokemonFieldResponseSchema
    );
    const updateTradePokemonRequestSchema = createBridgeRequestSchema(
      updateTradePokemonFieldRequestSchema
    );
    const updateTradePokemonResponseSchema = createBridgeResponseSchema(
      updateTradePokemonFieldResponseSchema
    );
    const updateRentalPokemonRequestSchema = createBridgeRequestSchema(
      updateRentalPokemonFieldRequestSchema
    );
    const updateRentalPokemonResponseSchema = createBridgeResponseSchema(
      updateRentalPokemonFieldResponseSchema
    );
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
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'sellPrice',
          label: 'Sell price',
          maximumValue: 499999,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'wattsPrice',
          label: 'Watts price',
          maximumValue: 999999,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        },
        {
          field: 'alternatePrice',
          label: 'Alternate price',
          maximumValue: 999999,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        }
      ],
      items: [
        {
          alternatePrice: 3,
          buyPrice: 450,
          category: 'Medicine',
          detailGroups: [
            {
              details: [
                { label: 'Pouch', value: 'Medicine (0)' },
                { label: 'Machine', value: 'No machine link' }
              ],
              label: 'Inventory'
            },
            {
              details: [{ label: 'Use flags 1', value: 'Restore HP' }],
              label: 'Field Use'
            }
          ],
          itemId: 1,
          metadata: itemMetadata,
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
            ability1Label: 'Overgrow',
            ability2: 0,
            ability2Label: 'None',
            hiddenAbility: 34,
            hiddenAbilityLabel: 'Chlorophyll'
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
          genderRatioLabel: '031 Male 87.5% / Female 12.5%',
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
          options: [],
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
              abilityLabel: 'Ability 2',
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
              genderLabel: 'Male',
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
              natureLabel: 'Jolly',
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
    const giftPokemonSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.giftPokemon',
          field: 'ivHp',
          newValue: '-4',
          recordId: 'gift:0',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/script_event_data/add_poke.bin'
            }
          ],
          summary: 'Set Gift 001: Grookey Lv. 50 HP IV to -4.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const giftPokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'ivHp',
          label: 'HP IV',
          maximumValue: 31,
          minimumValue: -4,
          options: [],
          valueKind: 'integer'
        }
      ],
      gifts: [
        {
          ability: 3,
          abilityLabel: 'Hidden Ability',
          ballItem: 'Poke Ball',
          ballItemId: 4,
          canGigantamax: true,
          dynamaxLevel: 10,
          flawlessIvCount: 3,
          form: 0,
          gender: 0,
          genderLabel: 'Random',
          giftIndex: 0,
          heldItem: null,
          heldItemId: 0,
          isEgg: false,
          ivs: {
            attack: -1,
            defense: -1,
            hp: -4,
            specialAttack: -1,
            specialDefense: -1,
            speed: -1
          },
          ivSummary: '3 guaranteed perfect IVs',
          label: 'Gift 001: Grookey Lv. 50',
          level: 50,
          nature: 25,
          natureLabel: 'Random',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/script_event_data/add_poke.bin',
            sourceLayer: 'base'
          },
          shinyLock: 0,
          shinyLockLabel: 'Random',
          specialMove: null,
          specialMoveId: 0,
          species: 'Grookey',
          speciesId: 810
        }
      ],
      stats: {
        eggGiftCount: 0,
        fixedIvGiftCount: 0,
        sourceFileCount: 4,
        totalGiftCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
        diagnostics: [],
        id: 'giftPokemon',
        label: 'Gift Pokemon'
      }
    } as const;
    const tradePokemonSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.tradePokemon',
          field: 'ivHp',
          newValue: '31',
          recordId: 'trade:0',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/script_event_data/field_trade.bin'
            }
          ],
          summary: 'Set Trade 001 HP IV to 31.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const tradePokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'ivHp',
          label: 'HP IV',
          maximumValue: 31,
          minimumValue: -4,
          options: [],
          valueKind: 'integer'
        }
      ],
      stats: {
        fixedIvTradeCount: 1,
        sourceFileCount: 4,
        totalTradeCount: 1
      },
      summary: {
        availability: 'available',
        description:
          'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
        diagnostics: [],
        id: 'tradePokemon',
        label: 'Trade Pokemon'
      },
      trades: [
        {
          ability: 3,
          abilityLabel: 'Hidden Ability',
          ballItem: 'Poke Ball',
          ballItemId: 4,
          canGigantamax: true,
          dynamaxLevel: 10,
          field03: 0,
          flawlessIvCount: null,
          form: 1,
          gender: 0,
          genderLabel: 'Random',
          hash0: '0x1122334455667788',
          hash1: '0x8877665544332211',
          hash2: '0x0102030405060708',
          heldItem: 'Potion',
          heldItemId: 1,
          ivs: {
            attack: 30,
            defense: 29,
            hp: 31,
            specialAttack: 27,
            specialDefense: 26,
            speed: 28
          },
          ivSummary: 'HP 31 / Atk 30 / Def 29 / SpA 27 / SpD 26 / Spe 28',
          label: 'Trade 001: Eevee -> Grookey (Form 1) Lv. 50',
          level: 50,
          memoryCode: 11,
          memoryFeel: 12,
          memoryIntensity: 13,
          memoryTextVariable: 4660,
          nature: 25,
          natureLabel: 'Random',
          otGender: 1,
          otGenderLabel: 'Female',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/script_event_data/field_trade.bin',
            sourceLayer: 'base'
          },
          relearnMoves: [
            {
              move: 'Scratch',
              moveId: 1,
              slot: 0
            }
          ],
          requiredForm: 0,
          requiredNature: 25,
          requiredNatureLabel: 'Random',
          requiredSpecies: 'Eevee',
          requiredSpeciesId: 133,
          shinyLock: 0,
          shinyLockLabel: 'Random',
          species: 'Grookey',
          speciesId: 810,
          tradeIndex: 0,
          trainerId: 123456,
          unknownRequirement: 0
        }
      ]
    } as const;
    const rentalPokemonSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.rentalPokemon',
          field: 'ivHp',
          newValue: '0',
          recordId: 'rental:0',
          sources: [
            {
              layer: 'base',
              relativePath: 'romfs/bin/script_event_data/rental.bin'
            }
          ],
          summary: 'Set Rental 001 HP IV to 0.'
        }
      ],
      sessionId: 'session-1'
    } as const;
    const rentalPokemonWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'ivHp',
          label: 'HP IV',
          maximumValue: 31,
          minimumValue: 0,
          options: [],
          valueKind: 'integer'
        }
      ],
      rentals: [
        {
          ability: 1,
          abilityLabel: 'Ability 1',
          ballItem: 'Poke Ball',
          ballItemId: 4,
          evs: {
            attack: 252,
            defense: 0,
            hp: 4,
            specialAttack: 0,
            specialDefense: 0,
            speed: 252
          },
          form: 0,
          gender: 1,
          genderLabel: 'Male',
          hash1: '0x1122334455667788',
          hash2: '0x8877665544332211',
          hasPerfectIvs: false,
          heldItem: 'Potion',
          heldItemId: 1,
          ivs: {
            attack: 31,
            defense: 31,
            hp: 0,
            specialAttack: 31,
            specialDefense: 31,
            speed: 31
          },
          ivSummary: 'HP 0 / Atk 31 / Def 31 / SpA 31 / SpD 31 / Spe 31',
          label: 'Rental 001: Grookey Lv. 50',
          level: 50,
          moves: [
            {
              move: 'Scratch',
              moveId: 1,
              slot: 0
            }
          ],
          nature: 0,
          natureLabel: 'Hardy',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/script_event_data/rental.bin',
            sourceLayer: 'base'
          },
          rentalIndex: 0,
          species: 'Grookey',
          speciesId: 810,
          trainerId: 123456
        }
      ],
      stats: {
        perfectIvRentalCount: 0,
        sourceFileCount: 1,
        totalRentalCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Rental Pokemon records, fixed IVs, EVs, items, moves, and source provenance.',
        diagnostics: [],
        id: 'rentalPokemon',
        label: 'Rental Pokemon'
      }
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
              relativePath: 'romfs/bin/appli/shop/bin/shop_data.bin'
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
            sourceFile: 'romfs/bin/appli/shop/bin/shop_data.bin',
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
      updateGiftPokemonRequestSchema.safeParse({
        command: kmCommandNames.updateGiftPokemonField,
        payload: {
          field: 'ivHp',
          giftIndex: 0,
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          value: '-4'
        }
      }).success
    ).toBe(true);

    expect(
      updateGiftPokemonResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: giftPokemonSession,
          workflow: giftPokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updateTradePokemonRequestSchema.safeParse({
        command: kmCommandNames.updateTradePokemonField,
        payload: {
          field: 'ivHp',
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          session: editSession,
          tradeIndex: 0,
          value: '31'
        }
      }).success
    ).toBe(true);

    expect(
      updateTradePokemonResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: tradePokemonSession,
          workflow: tradePokemonWorkflow
        }
      }).success
    ).toBe(true);

    expect(
      updateRentalPokemonRequestSchema.safeParse({
        command: kmCommandNames.updateRentalPokemonField,
        payload: {
          field: 'ivHp',
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output',
            saveFilePath: null
          },
          rentalIndex: 0,
          session: editSession,
          value: '0'
        }
      }).success
    ).toBe(true);

    expect(
      updateRentalPokemonResponseSchema.safeParse({
        payload: {
          diagnostics: [],
          session: rentalPokemonSession,
          workflow: rentalPokemonWorkflow
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
          outputMode: 'trinityModManager',
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
          outputMode: 'trinityBypass',
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

  it('accepts Pokemon Legends Z-A default and random sentinel values', () => {
    const provenance = {
      fileState: 'baseOnly',
      sourceFile: 'romfs/world/ik_data/field/pokemon/pokemon_data/pokemon_data_array.bin',
      sourceLayer: 'base'
    } as const;
    const randomIvs = {
      attack: -1,
      defense: -1,
      hp: -1,
      specialAttack: -1,
      specialDefense: -1,
      speed: -1
    } as const;
    const zeroStats = {
      attack: 0,
      defense: 0,
      hp: 0,
      specialAttack: 0,
      specialDefense: 0,
      speed: 0
    } as const;
    const trainerProvenance = {
      classFileState: null,
      classSourceFile: null,
      classSourceLayer: null,
      fileState: 'baseOnly',
      sourceFile: 'romfs/avalon/data/trainer/trainer_array.bin',
      sourceLayer: 'base',
      teamFileState: 'baseOnly',
      teamSourceFile: 'romfs/avalon/data/trainer/trainer_poke_array.bin',
      teamSourceLayer: 'base'
    } as const;

    expect(
      loadTrainersWorkflowResponseSchema.safeParse({
        workflow: {
          diagnostics: [],
          editableFields: [
            {
              field: 'gender',
              label: 'Gender',
              maximumValue: 2,
              minimumValue: -1,
              options: [{ label: 'Game default / random', value: -1 }],
              valueKind: 'integer'
            },
            {
              field: 'move1Id',
              label: 'Move 1',
              maximumValue: 65535,
              minimumValue: 0,
              options: [{ label: '0 None', value: 0 }],
              valueKind: 'integer'
            }
          ],
          stats: {
            sourceFileCount: 1,
            totalPokemonCount: 1,
            totalTrainerCount: 1
          },
          summary: {
            availability: 'available',
            description: 'Edit Pokemon Legends Z-A trainer data and trainer Pokemon.',
            diagnostics: [],
            id: 'trainers',
            label: 'Trainers'
          },
          trainers: [
            {
              aiFlags: 0,
              aiFlagStates: [],
              battleType: 'Trainer Battle',
              battleTypeValue: 0,
              canEditClassBall: false,
              canTerastallize: false,
              classBall: null,
              classBallId: null,
              classBallScope: 'Class file missing',
              gift: 0,
              heal: false,
              itemIds: [],
              items: [],
              location: 'tr_battle_main_001',
              money: 0,
              name: 'Dimension Rank Trainer',
              provenance: trainerProvenance,
              team: [
                {
                  ability: 255,
                  abilityLabel: 'Game default / random',
                  canDynamax: false,
                  canGigantamax: false,
                  dynamaxLevel: 0,
                  evs: zeroStats,
                  form: 0,
                  gender: -1,
                  genderLabel: 'Game default / random',
                  heldItem: null,
                  heldItemId: 0,
                  ivs: randomIvs,
                  level: 50,
                  moveIds: [33, 0, 33, 45],
                  moves: ['Tackle', 'None', 'Tackle', 'Growl'],
                  nature: -1,
                  natureLabel: 'Random / game default',
                  shiny: false,
                  slot: 0,
                  species: 'Bulbasaur',
                  speciesId: 1
                }
              ],
              teraTarget: 'Disabled',
              trainerClass: 'Pokemon Trainer',
              trainerClassId: 1,
              trainerId: 704,
              zaLastHand: false,
              zaMegaEvolution: false,
              zaRank: 26
            }
          ]
        }
      }).success
    ).toBe(true);

    expect(
      loadGiftPokemonWorkflowResponseSchema.safeParse({
        workflow: {
          diagnostics: [],
          editableFields: [],
          editorFamily: 'za',
          gifts: [
            {
              ability: 255,
              abilityLabel: 'Game default / random',
              ballItem: 'None',
              ballItemId: 0,
              canGigantamax: false,
              dynamaxLevel: 0,
              editorFamily: 'za',
              eventLabel: 'main_init_poke_1',
              flawlessIvCount: null,
              form: 0,
              gender: -1,
              genderLabel: 'Game default / random',
              giftIndex: 0,
              heldItem: null,
              heldItemId: 0,
              isEgg: false,
              ivSummary: 'Fixed IVs: HP Random',
              ivs: randomIvs,
              label: 'Gift 1: Chikorita Lv. 0',
              level: 0,
              moves: [
                {
                  move: null,
                  moveId: -1,
                  pointUps: 0,
                  slot: 0
                }
              ],
              nature: -1,
              natureLabel: 'Random / game default',
              provenance,
              shinyLock: 536870911,
              shinyLockLabel: 'Game default / not forced',
              specialMove: null,
              specialMoveId: -1,
              species: 'Chikorita',
              speciesId: 152
            }
          ],
          stats: {
            eggGiftCount: 0,
            fixedIvGiftCount: 0,
            sourceFileCount: 1,
            totalGiftCount: 1
          },
          summary: {
            availability: 'available',
            description: 'Edit Pokemon Legends Z-A gift Pokemon.',
            diagnostics: [],
            id: 'giftPokemon',
            label: 'Gift Pokemon'
          }
        }
      }).success
    ).toBe(true);

    expect(
      loadTradePokemonWorkflowResponseSchema.safeParse({
        workflow: {
          diagnostics: [],
          editableFields: [],
          editorFamily: 'za',
          stats: {
            fixedIvTradeCount: 0,
            sourceFileCount: 1,
            totalTradeCount: 1
          },
          summary: {
            availability: 'available',
            description: 'Edit Pokemon Legends Z-A trade Pokemon.',
            diagnostics: [],
            id: 'tradePokemon',
            label: 'Trade Pokemon'
          },
          trades: [
            {
              ability: 255,
              abilityLabel: 'Game default / random',
              ballItem: 'None',
              ballItemId: 0,
              canGigantamax: false,
              dynamaxLevel: 0,
              editorFamily: 'za',
              eventLabel: 'sub_tradepoke_poligon2',
              field03: 0,
              flawlessIvCount: null,
              form: 0,
              gender: -1,
              genderLabel: 'Game default / random',
              hash0: '0x0000000000000000',
              hash1: '0x0000000000000000',
              hash2: '0x0000000000000000',
              heldItem: 'Upgrade',
              heldItemId: 252,
              ivSummary: 'Fixed IVs: HP Random',
              ivs: randomIvs,
              label: 'Trade 3: Porygon Lv. 50',
              level: 50,
              memoryCode: 0,
              memoryFeel: 0,
              memoryIntensity: 0,
              memoryTextVariable: 0,
              moves: [
                {
                  move: null,
                  moveId: -1,
                  slot: 0
                }
              ],
              nature: 15,
              natureLabel: 'Naive (+Spe, -Sp. Def)',
              otGender: 0,
              otGenderLabel: 'Default',
              provenance,
              relearnMoves: [
                {
                  move: null,
                  moveId: -1,
                  slot: 0
                }
              ],
              requiredForm: 0,
              requiredNature: -1,
              requiredNatureLabel: 'Random / game default',
              requiredSpecies: 'Script linked',
              requiredSpeciesId: 0,
              shinyLock: 536870911,
              shinyLockLabel: 'Game default / not forced',
              species: 'Porygon',
              speciesId: 137,
              tradeIndex: 2,
              trainerId: 0,
              unknownRequirement: 0
            }
          ]
        }
      }).success
    ).toBe(true);

    expect(
      loadStaticEncountersWorkflowResponseSchema.safeParse({
        workflow: {
          diagnostics: [],
          editableFields: [],
          editorFamily: 'za',
          encounters: [
            {
              ability: 255,
              abilityLabel: 'Game default / random',
              canGigantamax: false,
              categoryId: 'encounterData',
              categoryLabel: 'Encounter Data',
              dynamaxLevel: 0,
              editorFamily: 'za',
              encounterId: 'ect_boss_0359_01',
              encounterIndex: 0,
              encounterScenario: 0,
              encounterScenarioLabel: 'Scripted Pokemon',
              evs: zeroStats,
              flawlessIvCount: null,
              form: 0,
              fieldDisplayValues: {
                gender: 'Game default / random',
                move0Id: '-1 Game default / none'
              },
              fieldValues: {
                gender: '-1',
                move0Id: '-1'
              },
              gender: -1,
              genderLabel: 'Game default / random',
              heldItem: null,
              heldItemId: 0,
              ivSummary: 'Fixed IVs: HP Random',
              ivs: randomIvs,
              label: 'Static 001: Absol Lv. 25',
              level: 25,
              moves: [
                {
                  move: null,
                  moveId: -1,
                  slot: 0
                }
              ],
              nature: -1,
              natureLabel: 'Random / game default',
              provenance,
              shinyLock: 536870911,
              shinyLockLabel: 'Game default / not forced',
              species: 'Absol',
              speciesId: 359
            }
          ],
          stats: {
            fixedIvEncounterCount: 0,
            gigantamaxEncounterCount: 0,
            sourceFileCount: 1,
            totalEncounterCount: 1
          },
          summary: {
            availability: 'available',
            description: 'Edit Pokemon Legends Z-A static encounters.',
            diagnostics: [],
            id: 'staticEncounters',
            label: 'Static Encounters'
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
