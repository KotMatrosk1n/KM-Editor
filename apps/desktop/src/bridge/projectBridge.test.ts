/* SPDX-License-Identifier: GPL-3.0-only */

import { ProjectBridgeError, createProjectBridge } from './projectBridge';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: null,
  saveFilePath: null
};

const editableProjectPaths = {
  ...projectPaths,
  outputRootPath: 'output',
  saveFilePath: null
};

const readOnlyHealth = {
  canOpenEditableWorkflows: false,
  canOpenReadOnlyWorkflows: true,
  diagnostics: [],
  fileGraph: {
    baseFileCount: 2,
    layeredFileCount: 0,
    layeredOnlyCount: 0,
    overrideCount: 0
  },
  paths: [
    {
      diagnostics: [],
      isRequired: true,
      path: 'base-romfs',
      role: 'baseRomFs',
      status: 'valid'
    },
    {
      diagnostics: [],
      isRequired: true,
      path: 'base-exefs',
      role: 'baseExeFs',
      status: 'valid'
    },
    {
      diagnostics: [],
      isRequired: false,
      path: null,
      role: 'outputRoot',
      status: 'notSet'
    }
  ],
  state: 'readOnlyReady'
} as const;

const fileGraph = {
  entries: [
    {
      baseFile: {
        layer: 'base',
        relativePath: 'romfs/data/items.bin'
      },
      layeredFile: null,
      relativePath: 'romfs/data/items.bin',
      state: 'baseOnly'
    }
  ],
  summary: readOnlyHealth.fileGraph
} as const;

describe('projectBridge', () => {
  it('sends typed project open requests through the configured transport', async () => {
    let capturedRequest: unknown;
    const bridge = createProjectBridge(async (requestJson) => {
      capturedRequest = JSON.parse(requestJson);

      return JSON.stringify({
        error: null,
        payload: {
          fileGraph,
          health: readOnlyHealth,
          projectId: 'project-1'
        },
        requestId: 'response-1'
      });
    });

    const response = await bridge.openProject({ paths: projectPaths });

    expect(capturedRequest).toMatchObject({
      command: 'project.open',
      payload: {
        paths: projectPaths
      }
    });
    expect(response.projectId).toBe('project-1');
    expect(response.health.state).toBe('readOnlyReady');
  });

  it('uses backend response validation for project validation responses', async () => {
    const bridge = createProjectBridge(async () =>
      JSON.stringify({
        error: null,
        payload: {
          health: readOnlyHealth
        }
      })
    );

    const response = await bridge.validateProject({ paths: projectPaths });

    expect(response.health.canOpenReadOnlyWorkflows).toBe(true);
  });

  it('loads workflow summaries and Items workflow payloads', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      if (request.command === 'workflow.list') {
        return JSON.stringify({
          error: null,
          payload: {
            workflows: [
              {
                availability: 'readOnly',
                description: 'Item records, names, and source provenance.',
                diagnostics: [],
                id: 'items',
                label: 'Items'
              },
              {
                availability: 'readOnly',
                description:
                  'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
                diagnostics: [],
                id: 'pokemon',
                label: 'Pokemon Data'
              },
              {
                availability: 'readOnly',
                description:
                  'Move stats, target behavior, secondary effects, flags, and source provenance.',
                diagnostics: [],
                id: 'moves',
                label: 'Moves Data'
              },
              {
                availability: 'readOnly',
                description: 'Text entries, dialogue references, and source provenance.',
                diagnostics: [],
                id: 'text',
                label: 'Text and Dialogue Map'
              },
              {
                availability: 'readOnly',
                description: 'Trainer parties, classes, battle types, and source provenance.',
                diagnostics: [],
                id: 'trainers',
                label: 'Trainers'
              },
              {
                availability: 'readOnly',
                description:
                  'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
                diagnostics: [],
                id: 'giftPokemon',
                label: 'Gift Pokemon'
              },
              {
                availability: 'readOnly',
                description:
                  'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
                diagnostics: [],
                id: 'tradePokemon',
                label: 'Trade Pokemon'
              },
              {
                availability: 'readOnly',
                description: 'Shop inventories, prices, stock limits, and source provenance.',
                diagnostics: [],
                id: 'shops',
                label: 'Shops'
              },
              {
                availability: 'readOnly',
                description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
                diagnostics: [],
                id: 'encounters',
                label: 'Encounters and Wild Data'
              },
              {
                availability: 'readOnly',
                description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
                diagnostics: [],
                id: 'raidRewards',
                label: 'Raid Rewards'
              },
              {
                availability: 'readOnly',
                description: 'Placed objects, map coordinates, script links, and source provenance.',
                diagnostics: [],
                id: 'placement',
                label: 'Placement'
              },
              {
                availability: 'readOnly',
                description: 'Game flags, save blocks, inspector metadata, and source provenance.',
                diagnostics: [],
                id: 'flagworkSave',
                label: 'Flagwork and Save Inspectors'
              },
              {
                availability: 'readOnly',
                description:
                  'ExeFS main validation, patch anchors, segment hashes, and source provenance.',
                diagnostics: [],
                id: 'exefsPatches',
                label: 'ExeFS Patch Manager'
              },
              {
                availability: 'readOnly',
                description:
                  'Royal Candy source readiness, ExeFS compatibility, and LayeredFS output preview.',
                diagnostics: [],
                id: 'royalCandy',
                label: 'Royal Candy Workflows'
              },
              {
                availability: 'readOnly',
                description:
                  'Spreadsheet import profiles, target workflows, columns, and source provenance.',
                diagnostics: [],
                id: 'spreadsheetImport',
                label: 'Spreadsheet Import'
              }
            ]
          }
        });
      }

      if (request.command === 'pokemon.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
                description:
                  'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
                diagnostics: [],
                id: 'pokemon',
                label: 'Pokemon Data'
              }
            }
          }
        });
      }

      if (request.command === 'moves.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
              moves: [
                {
                  accuracy: 100,
                  canUseMove: true,
                  category: 1,
                  categoryName: 'Physical',
                  critStage: 0,
                  description:
                    'A physical attack in which the user charges and slams into the target.',
                  effectSequence: 12,
                  flags: [
                    {
                      enabled: true,
                      field: 'makesContact',
                      label: 'Makes Contact'
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
                      percent: 30,
                      slot: 1,
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
                activeFlagCount: 1,
                enabledMoveCount: 1,
                sourceFileCount: 4,
                totalMoveCount: 1
              },
              summary: {
                availability: 'readOnly',
                description:
                  'Move stats, target behavior, secondary effects, flags, and source provenance.',
                diagnostics: [],
                id: 'moves',
                label: 'Moves Data'
              }
            }
          }
        });
      }

      if (request.command === 'text.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
            }
          }
        });
      }

      if (request.command === 'trainers.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
            }
          }
        });
      }

      if (request.command === 'giftPokemon.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
                  ability: 1,
                  abilityLabel: 'Ability 1',
                  ballItem: 'Poke Ball',
                  ballItemId: 4,
                  canGigantamax: false,
                  dynamaxLevel: 0,
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
                  label: 'Gift 001: Bulbasaur Lv. 5 Form 0',
                  level: 5,
                  nature: 25,
                  natureLabel: 'Random',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/bin/script_event_data/add_poke.bin',
                    sourceLayer: 'base'
                  },
                  shinyLock: 1,
                  shinyLockLabel: 'Never Shiny',
                  specialMove: null,
                  specialMoveId: 0,
                  species: 'Bulbasaur',
                  speciesId: 1
                }
              ],
              stats: {
                eggGiftCount: 0,
                fixedIvGiftCount: 0,
                sourceFileCount: 1,
                totalGiftCount: 1
              },
              summary: {
                availability: 'readOnly',
                description:
                  'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
                diagnostics: [],
                id: 'giftPokemon',
                label: 'Gift Pokemon'
              }
            }
          }
        });
      }

      if (request.command === 'tradePokemon.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
                fixedIvTradeCount: 0,
                sourceFileCount: 1,
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
                  requiredForm: 0,
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
            }
          }
        });
      }

      if (request.command === 'shops.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
            }
          }
        });
      }

      if (request.command === 'encounters.load') {
        return JSON.stringify({
          error: null,
          payload: {
              workflow: {
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
                availability: 'readOnly',
                description:
                  'Encounter tables, wild slots, levels, weather, and source provenance.',
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
              }
          }
        });
      }

      if (request.command === 'raidRewards.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
                description:
                  'Raid reward tables, den ranks, item quantities, and source provenance.',
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
            }
          }
        });
      }

      if (request.command === 'placement.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
                description:
                  'Placed objects, map coordinates, item pickups, and source provenance.',
                diagnostics: [],
                id: 'placement',
                label: 'Placement'
              }
            }
          }
        });
      }

      if (request.command === 'flagworkSave.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
            }
          }
        });
      }

      if (request.command === 'exefsPatches.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
              diagnostics: [],
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
                description:
                  'ExeFS main validation, patch anchors, segment hashes, and source provenance.',
                diagnostics: [],
                id: 'exefsPatches',
                label: 'ExeFS Patch Manager'
              }
            }
          }
        });
      }

      if (request.command === 'royalCandy.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
                description:
                  'Royal Candy source readiness, ExeFS compatibility, and LayeredFS output preview.',
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
            }
          }
        });
      }

      if (request.command === 'spreadsheetImport.load') {
        return JSON.stringify({
          error: null,
          payload: {
            workflow: {
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
                  description: 'Import item price columns from a workbook fixture.',
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
                description:
                  'CSV and TSV import profiles that execute through backend edit sessions.',
                diagnostics: [],
                id: 'spreadsheetImport',
                label: 'Spreadsheet Import'
              }
            }
          }
        });
      }

      return JSON.stringify({
        error: null,
        payload: {
          workflow: {
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
          }
        }
      });
    });

    const workflows = await bridge.listWorkflows({ paths: projectPaths });
    const items = await bridge.loadItemsWorkflow({ paths: projectPaths });
    const pokemon = await bridge.loadPokemonWorkflow({ paths: projectPaths });
    const moves = await bridge.loadMovesWorkflow({ paths: projectPaths });
    const text = await bridge.loadTextWorkflow({ paths: projectPaths });
    const trainers = await bridge.loadTrainersWorkflow({ paths: projectPaths });
    const giftPokemon = await bridge.loadGiftPokemonWorkflow({ paths: projectPaths });
    const tradePokemon = await bridge.loadTradePokemonWorkflow({ paths: projectPaths });
    const shops = await bridge.loadShopsWorkflow({ paths: projectPaths });
    const encounters = await bridge.loadEncountersWorkflow({ paths: projectPaths });
    const raidRewards = await bridge.loadRaidRewardsWorkflow({ paths: projectPaths });
    const placement = await bridge.loadPlacementWorkflow({ paths: projectPaths });
    const flagworkSave = await bridge.loadFlagworkSaveWorkflow({ paths: projectPaths });
    const exeFsPatches = await bridge.loadExeFsPatchWorkflow({ paths: projectPaths });
    const royalCandy = await bridge.loadRoyalCandyWorkflow({ paths: projectPaths });
    const spreadsheetImport = await bridge.loadSpreadsheetImportWorkflow({ paths: projectPaths });

    expect(workflows.workflows[0]?.id).toBe('items');
    expect(workflows.workflows[1]?.id).toBe('pokemon');
    expect(workflows.workflows[2]?.id).toBe('moves');
    expect(workflows.workflows[3]?.id).toBe('text');
    expect(workflows.workflows[4]?.id).toBe('trainers');
    expect(workflows.workflows[5]?.id).toBe('giftPokemon');
    expect(workflows.workflows[6]?.id).toBe('tradePokemon');
    expect(workflows.workflows[7]?.id).toBe('shops');
    expect(workflows.workflows[8]?.id).toBe('encounters');
    expect(workflows.workflows[9]?.id).toBe('raidRewards');
    expect(workflows.workflows[10]?.id).toBe('placement');
    expect(workflows.workflows[11]?.id).toBe('flagworkSave');
    expect(workflows.workflows[12]?.id).toBe('exefsPatches');
    expect(workflows.workflows[13]?.id).toBe('royalCandy');
    expect(workflows.workflows[14]?.id).toBe('spreadsheetImport');
    expect(items.workflow.editableFields).toHaveLength(4);
    expect(items.workflow.items[0]?.name).toBe('Potion');
    expect(pokemon.workflow.editableFields[0]?.field).toBe('hp');
    expect(pokemon.workflow.pokemon[0]?.name).toBe('Bulbasaur');
    expect(pokemon.workflow.pokemon[0]?.learnset[0]?.moveName).toBe('Tackle');
    expect(moves.workflow.moves[0]?.name).toBe('Tackle');
    expect(moves.workflow.moves[0]?.statChanges[0]?.statName).toBe('Attack');
    expect(text.workflow.editableFields[0]?.field).toBe('value');
    expect(text.workflow.entries[0]?.label).toBe('story #0');
    expect(trainers.workflow.trainers[0]?.name).toBe('Avery');
    expect(giftPokemon.workflow.gifts[0]?.ivSummary).toBe('3 guaranteed perfect IVs');
    expect(tradePokemon.workflow.trades[0]?.hash0).toBe('0x1122334455667788');
    expect(shops.workflow.editableFields[0]?.field).toBe('itemId');
    expect(shops.workflow.shops[0]?.name).toBe('Route 1 Mart');
    expect(encounters.workflow.editableFields[0]?.field).toBe('probability');
    expect(encounters.workflow.tables[0]?.slots[0]?.species).toBe('Bulbasaur');
    expect(raidRewards.workflow.tables[0]?.rewards[0]?.itemName).toBe('Exp. Candy L');
    expect(placement.workflow.objects[0]?.label).toBe('Hidden item: Potion');
    expect(flagworkSave.workflow.flags[0]?.name).toBe('FE_TEST_FLAG');
    expect(flagworkSave.workflow.saveBlocks[0]?.key).toBe('0xDDEEFF00');
    expect(flagworkSave.workflow.saveFile?.fileName).toBe('main');
    expect(exeFsPatches.workflow.patches[0]?.targetFile).toBe('exefs/main');
    expect(royalCandy.workflow.workflows[0]?.name).toBe('Install Unlimited Royal Candy');
    expect(royalCandy.workflow.outputs[0]?.relativePath).toBe('romfs/bin/pml/item/item.dat');
    expect(spreadsheetImport.workflow.profiles[0]?.name).toBe('Items Price CSV/TSV');
  });

  it('sends Spreadsheet Import previews through the configured transport', async () => {
    let capturedRequest: unknown;
    const spreadsheetImportWorkflow = {
      diagnostics: [],
      profiles: [
        {
          columns: [
            {
              column: 1,
              description: 'Existing item ID.',
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
        availability: 'available',
        description: 'CSV and TSV import profiles that execute through backend edit sessions.',
        diagnostics: [],
        id: 'spreadsheetImport',
        label: 'Spreadsheet Import'
      }
    } as const;
    const bridge = createProjectBridge(async (requestJson) => {
      capturedRequest = JSON.parse(requestJson);

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [
            {
              message: 'Spreadsheet import preview accepted 1 row and rejected 0.',
              severity: 'info'
            }
          ],
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
      });
    });

    const response = await bridge.previewSpreadsheetImport({
      paths: editableProjectPaths,
      profileId: 'items-price-csv',
      session: null,
      sourcePath: 'items.csv'
    });

    expect(capturedRequest).toMatchObject({
      command: 'spreadsheetImport.preview',
      payload: {
        profileId: 'items-price-csv',
        sourcePath: 'items.csv'
      }
    });
    expect(response.preview.acceptedRowCount).toBe(1);
    expect(response.session.pendingEdits[0]?.domain).toBe('workflow.items');
  });

  it('sends Placement object updates through the configured transport', async () => {
    let capturedRequest: unknown;
    const bridge = createProjectBridge(async (requestJson) => {
      capturedRequest = JSON.parse(requestJson);

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
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
          },
          workflow: {
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
          }
        }
      });
    });

    const response = await bridge.updatePlacementObjectField({
      field: 'quantity',
      objectId: 'a_test.bin|0|fieldItem|0|-',
      paths: editableProjectPaths,
      session: null,
      value: '5'
    });

    expect(capturedRequest).toMatchObject({
      command: 'placement.object.update',
      payload: {
        field: 'quantity',
        objectId: 'a_test.bin|0|fieldItem|0|-',
        value: '5'
      }
    });
    expect(response.workflow.objects[0]?.quantity).toBe(5);
  });

  it('starts, updates, and validates an Items edit session', async () => {
    const commands: string[] = [];
    const session = {
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
    };
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };
      commands.push(request.command);

      if (request.command === 'editSession.start') {
        return JSON.stringify({
          error: null,
          payload: {
            session: {
              hasPendingChanges: false,
              pendingEdits: [],
              sessionId: 'session-1'
            }
          }
        });
      }

      if (request.command === 'items.field.update') {
        return JSON.stringify({
          error: null,
          payload: {
            diagnostics: [],
            session,
            workflow: {
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
            }
          }
        });
      }

      if (request.command === 'changePlan.create') {
        return JSON.stringify({
          error: null,
          payload: {
            changePlan: {
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
            }
          }
        });
      }

      if (request.command === 'changePlan.apply') {
        return JSON.stringify({
          error: null,
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
        });
      }

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [
            {
              field: 'buyPrice',
              message: 'Pending item change is valid.',
              severity: 'info'
            }
          ],
          isValid: true,
          session
        }
      });
    });

    const started = await bridge.startEditSession({ paths: editableProjectPaths });
    const updated = await bridge.updateItemField({
      field: 'buyPrice',
      itemId: 1,
      paths: editableProjectPaths,
      session: started.session,
      value: '450'
    });
    const validation = await bridge.validateEditSession({
      paths: editableProjectPaths,
      session: updated.session
    });
    const plan = await bridge.createChangePlan({
      paths: editableProjectPaths,
      session: updated.session
    });
    const apply = await bridge.applyChangePlan({
      changePlan: plan.changePlan,
      paths: editableProjectPaths,
      session: updated.session
    });

    expect(commands).toEqual([
      'editSession.start',
      'items.field.update',
      'editSession.validate',
      'changePlan.create',
      'changePlan.apply'
    ]);
    expect(updated.workflow.items[0]?.buyPrice).toBe(450);
    expect(validation.isValid).toBe(true);
    expect(plan.changePlan.writes[0]?.targetRelativePath).toBe(
      'romfs/bin/pml/item/item.dat'
    );
    expect(apply.applyResult.writtenFiles).toEqual(['romfs/bin/pml/item/item.dat']);
  });

  it('runs pokemon field update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('pokemon.field.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
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
          },
          workflow: {
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
            pokemon: [],
            stats: {
              presentPokemonCount: 0,
              sourceFileCount: 1,
              totalEvolutionCount: 0,
              totalLearnsetMoveCount: 0,
              totalPokemonCount: 0
            },
            summary: {
              availability: 'available',
              description:
                'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
              diagnostics: [],
              id: 'pokemon',
              label: 'Pokemon Data'
            }
          }
        }
      });
    });

    const updated = await bridge.updatePokemonField({
      field: 'hp',
      paths: editableProjectPaths,
      personalId: 1,
      session: null,
      value: '99'
    });

    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.pokemon');
    expect(updated.session.pendingEdits[0]?.field).toBe('hp');
  });

  it('runs pokemon learnset update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('pokemon.learnset.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
            hasPendingChanges: true,
            pendingEdits: [
              {
                domain: 'workflow.pokemon',
                field: 'learnset:upsert:1',
                newValue: '345:9',
                recordId: '1',
                sources: [
                  {
                    layer: 'base',
                    relativePath: 'romfs/bin/pml/waza_oboe/wazaoboe_total.bin'
                  }
                ],
                summary: 'Set Bulbasaur learnset slot 1 to Lv. 9 Magical Leaf.'
              }
            ],
            sessionId: 'session-1'
          },
          workflow: {
            diagnostics: [],
            editableFields: [],
            pokemon: [],
            stats: {
              presentPokemonCount: 0,
              sourceFileCount: 1,
              totalEvolutionCount: 0,
              totalLearnsetMoveCount: 0,
              totalPokemonCount: 0
            },
            summary: {
              availability: 'available',
              description:
                'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
              diagnostics: [],
              id: 'pokemon',
              label: 'Pokemon Data'
            }
          }
        }
      });
    });

    const updated = await bridge.updatePokemonLearnset({
      action: 'upsert',
      level: 9,
      moveId: 345,
      paths: editableProjectPaths,
      personalId: 1,
      session: null,
      slot: 1
    });

    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.pokemon');
    expect(updated.session.pendingEdits[0]?.field).toBe('learnset:upsert:1');
  });

  it('runs pokemon evolution update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('pokemon.evolution.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
            hasPendingChanges: true,
            pendingEdits: [
              {
                domain: 'workflow.pokemon',
                field: 'evolution:upsert:0',
                newValue: '8:25:2:1:32',
                recordId: '1',
                sources: [
                  {
                    layer: 'base',
                    relativePath: 'romfs/bin/pml/evolution/evo_001.bin'
                  }
                ],
                summary: 'Set Bulbasaur evolution slot 0 to species 2 at level 32.'
              }
            ],
            sessionId: 'session-1'
          },
          workflow: {
            diagnostics: [],
            editableFields: [],
            pokemon: [],
            stats: {
              presentPokemonCount: 0,
              sourceFileCount: 1,
              totalEvolutionCount: 0,
              totalLearnsetMoveCount: 0,
              totalPokemonCount: 0
            },
            summary: {
              availability: 'available',
              description:
                'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
              diagnostics: [],
              id: 'pokemon',
              label: 'Pokemon Data'
            }
          }
        }
      });
    });

    const updated = await bridge.updatePokemonEvolution({
      action: 'upsert',
      argument: 25,
      form: 1,
      level: 32,
      method: 8,
      paths: editableProjectPaths,
      personalId: 1,
      session: null,
      slot: 0,
      species: 2
    });

    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.pokemon');
    expect(updated.session.pendingEdits[0]?.field).toBe('evolution:upsert:0');
  });

  it('runs gift Pokemon field update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('giftPokemon.field.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
            hasPendingChanges: true,
            pendingEdits: [
              {
                domain: 'workflow.giftPokemon',
                field: 'ivHp',
                newValue: '31',
                recordId: 'gift:0',
                sources: [
                  {
                    layer: 'base',
                    relativePath: 'romfs/bin/script_event_data/add_poke.bin'
                  }
                ],
                summary: 'Set Gift 001 HP IV to 31.'
              }
            ],
            sessionId: 'session-1'
          },
          workflow: {
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
                ability: 1,
                abilityLabel: 'Ability 1',
                ballItem: 'Poke Ball',
                ballItemId: 4,
                canGigantamax: false,
                dynamaxLevel: 0,
                flawlessIvCount: null,
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
                  hp: 31,
                  specialAttack: -1,
                  specialDefense: -1,
                  speed: -1
                },
                ivSummary: 'HP 31 / Atk -1 / Def -1 / SpA -1 / SpD -1 / Spe -1',
                label: 'Gift 001: Bulbasaur Lv. 5 Form 0',
                level: 5,
                nature: 25,
                natureLabel: 'Random',
                provenance: {
                  fileState: 'baseOnly',
                  sourceFile: 'romfs/bin/script_event_data/add_poke.bin',
                  sourceLayer: 'base'
                },
                shinyLock: 1,
                shinyLockLabel: 'Never Shiny',
                specialMove: null,
                specialMoveId: 0,
                species: 'Bulbasaur',
                speciesId: 1
              }
            ],
            stats: {
              eggGiftCount: 0,
              fixedIvGiftCount: 1,
              sourceFileCount: 1,
              totalGiftCount: 1
            },
            summary: {
              availability: 'available',
              description:
                'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
              diagnostics: [],
              id: 'giftPokemon',
              label: 'Gift Pokemon'
            }
          }
        }
      });
    });

    const updated = await bridge.updateGiftPokemonField({
      field: 'ivHp',
      giftIndex: 0,
      paths: editableProjectPaths,
      session: null,
      value: '31'
    });

    expect(updated.workflow.gifts[0]?.ivs.hp).toBe(31);
    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.giftPokemon');
  });

  it('runs trade Pokemon field update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('tradePokemon.field.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
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
          },
          workflow: {
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
              sourceFileCount: 1,
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
                ability: 1,
                abilityLabel: 'Ability 1',
                ballItem: 'Poke Ball',
                ballItemId: 4,
                canGigantamax: false,
                dynamaxLevel: 0,
                field03: 0,
                flawlessIvCount: null,
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
                  hp: 31,
                  specialAttack: -1,
                  specialDefense: -1,
                  speed: -1
                },
                ivSummary: 'HP 31 / Atk -1 / Def -1 / SpA -1 / SpD -1 / Spe -1',
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
                requiredForm: 0,
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
          }
        }
      });
    });

    const updated = await bridge.updateTradePokemonField({
      field: 'ivHp',
      paths: editableProjectPaths,
      session: null,
      tradeIndex: 0,
      value: '31'
    });

    expect(updated.workflow.trades[0]?.ivs.hp).toBe(31);
    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.tradePokemon');
  });

  it('runs move field update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('moves.field.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
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
          },
          workflow: {
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
              description:
                'Move stats, target behavior, secondary effects, flags, and source provenance.',
              diagnostics: [],
              id: 'moves',
              label: 'Moves Data'
            }
          }
        }
      });
    });

    const updated = await bridge.updateMoveField({
      field: 'power',
      moveId: 33,
      paths: editableProjectPaths,
      session: null,
      value: '80'
    });

    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.moves');
    expect(updated.session.pendingEdits[0]?.newValue).toBe('80');
  });

  it('runs trainer field update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('trainers.field.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
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
          },
          workflow: {
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
          }
        }
      });
    });

    const updated = await bridge.updateTrainerField({
      field: 'level',
      paths: editableProjectPaths,
      session: null,
      slot: 1,
      trainerId: 10,
      value: '25'
    });

    expect(updated.workflow.trainers[0]?.team[0]?.level).toBe(25);
    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.trainers');
  });

  it('runs shop inventory update command', async () => {
    const bridge = createProjectBridge(async (requestJson) => {
      const request = JSON.parse(requestJson) as { command: string };

      expect(request.command).toBe('shops.inventory.update');

      return JSON.stringify({
        error: null,
        payload: {
          diagnostics: [],
          session: {
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
          },
          workflow: {
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
          }
        }
      });
    });

    const updated = await bridge.updateShopInventoryItem({
      field: 'itemId',
      paths: editableProjectPaths,
      session: null,
      shopId: 'single:1F3FF031A3A24490',
      slot: 1,
      value: '2'
    });

    expect(updated.workflow.shops[0]?.inventory[0]?.itemId).toBe(2);
    expect(updated.session.pendingEdits[0]?.domain).toBe('workflow.shops');
  });

  it('turns bridge error envelopes into project bridge errors', async () => {
    const bridge = createProjectBridge(async () =>
      JSON.stringify({
        error: {
          code: 'bridge.unsupportedCommand',
          diagnostics: [],
          message: 'Bridge command is not supported.'
        },
        payload: null
      })
    );

    await expect(bridge.refreshFileGraph({ paths: projectPaths })).rejects.toBeInstanceOf(
      ProjectBridgeError
    );
  });
});
