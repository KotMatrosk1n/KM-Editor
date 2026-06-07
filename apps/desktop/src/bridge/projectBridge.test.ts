/* SPDX-License-Identifier: GPL-3.0-only */

import { ProjectBridgeError, createProjectBridge } from './projectBridge';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: null
};

const editableProjectPaths = {
  ...projectPaths,
  outputRootPath: 'output'
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
              }
            ]
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
                  context: 'Intro',
                  dialogueId: 'intro.lab.greeting',
                  label: 'Lab greeting',
                  preview: 'Welcome to the lab.',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/kmeditor/text.dialogue.readmodel.json',
                    sourceLayer: 'base'
                  },
                  textId: 10
                }
              ],
              entries: [
                {
                  label: 'Greeting',
                  language: 'en',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/kmeditor/text.dialogue.readmodel.json',
                    sourceLayer: 'base'
                  },
                  textId: 10,
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
              stats: {
                sourceFileCount: 1,
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
                  battleType: 'Single',
                  location: 'Route 1',
                  name: 'Avery',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/kmeditor/trainers.readmodel.json',
                    sourceLayer: 'base'
                  },
                  team: [
                    {
                      heldItem: null,
                      level: 12,
                      moves: ['Scratch', 'Growl'],
                      slot: 1,
                      species: 'Grookey'
                    }
                  ],
                  trainerClass: 'Pokemon Trainer',
                  trainerId: 10
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
                  area: 'Grass',
                  encounterType: 'Overworld',
                  gameVersion: 'Sword',
                  location: 'Route 1',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/kmeditor/encounters.wild.readmodel.json',
                    sourceLayer: 'base'
                  },
                  slots: [
                    {
                      levelMax: 5,
                      levelMin: 3,
                      slot: 1,
                      species: 'Skwovet',
                      timeOfDay: null,
                      weather: 'Any',
                      weight: 35
                    }
                  ],
                  tableId: 'route_1_grass_sword'
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
                  denId: 'den_001',
                  gameVersion: 'Sword',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/kmeditor/raid.rewards.readmodel.json',
                    sourceLayer: 'base'
                  },
                  rank: 5,
                  rewards: [
                    {
                      itemId: 1,
                      itemName: 'Exp. Candy L',
                      quantity: 2,
                      slot: 1,
                      weight: 40
                    }
                  ],
                  tableId: 'den_001_rank_5_sword'
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
              objects: [
                {
                  label: 'Hidden Potion',
                  map: 'Route 1',
                  objectId: 'route_1_hidden_potion',
                  objectType: 'HiddenItem',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/kmeditor/placement.readmodel.json',
                    sourceLayer: 'base'
                  },
                  rotationY: 90,
                  scriptId: 'script_hidden_item_001',
                  x: 10.5,
                  y: 0,
                  z: -4.25
                }
              ],
              stats: {
                sourceFileCount: 1,
                totalObjectCount: 1
              },
              summary: {
                availability: 'readOnly',
                description:
                  'Placed objects, map coordinates, script links, and source provenance.',
                diagnostics: [],
                id: 'placement',
                label: 'Placement'
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
                maximumValue: 999999,
                minimumValue: 0,
                valueKind: 'integer'
              }
            ],
            items: [
              {
                buyPrice: 300,
                category: 'Medicine',
                itemId: 1,
                name: 'Potion',
                provenance: {
                  fileState: 'baseOnly',
                  sourceFile: 'romfs/kmeditor/items.readmodel.json',
                  sourceLayer: 'base'
                },
                sellPrice: 150
              }
            ],
            stats: {
              sourceFileCount: 1,
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
    const text = await bridge.loadTextWorkflow({ paths: projectPaths });
    const trainers = await bridge.loadTrainersWorkflow({ paths: projectPaths });
    const shops = await bridge.loadShopsWorkflow({ paths: projectPaths });
    const encounters = await bridge.loadEncountersWorkflow({ paths: projectPaths });
    const raidRewards = await bridge.loadRaidRewardsWorkflow({ paths: projectPaths });
    const placement = await bridge.loadPlacementWorkflow({ paths: projectPaths });

    expect(workflows.workflows[0]?.id).toBe('items');
    expect(workflows.workflows[1]?.id).toBe('text');
    expect(workflows.workflows[2]?.id).toBe('trainers');
    expect(workflows.workflows[3]?.id).toBe('shops');
    expect(workflows.workflows[4]?.id).toBe('encounters');
    expect(workflows.workflows[5]?.id).toBe('raidRewards');
    expect(workflows.workflows[6]?.id).toBe('placement');
    expect(items.workflow.editableFields).toHaveLength(2);
    expect(items.workflow.items[0]?.name).toBe('Potion');
    expect(text.workflow.entries[0]?.label).toBe('Greeting');
    expect(trainers.workflow.trainers[0]?.name).toBe('Avery');
    expect(shops.workflow.shops[0]?.name).toBe('Route 1 Mart');
    expect(encounters.workflow.tables[0]?.slots[0]?.species).toBe('Skwovet');
    expect(raidRewards.workflow.tables[0]?.rewards[0]?.itemName).toBe('Exp. Candy L');
    expect(placement.workflow.objects[0]?.label).toBe('Hidden Potion');
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
              relativePath: 'romfs/kmeditor/items.readmodel.json'
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
                  maximumValue: 999999,
                  minimumValue: 0,
                  valueKind: 'integer'
                }
              ],
              items: [
                {
                  buyPrice: 450,
                  category: 'Medicine',
                  itemId: 1,
                  name: 'Potion',
                  provenance: {
                    fileState: 'baseOnly',
                    sourceFile: 'romfs/kmeditor/items.readmodel.json',
                    sourceLayer: 'base'
                  },
                  sellPrice: 150
                }
              ],
              stats: {
                sourceFileCount: 1,
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
                      relativePath: 'romfs/kmeditor/items.readmodel.json'
                    }
                  ],
                  targetRelativePath: 'romfs/kmeditor/items.readmodel.json'
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
                  message: 'Applied Items change plan to the configured output root.',
                  severity: 'info'
                }
              ],
              writtenFiles: ['romfs/kmeditor/items.readmodel.json']
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
      'romfs/kmeditor/items.readmodel.json'
    );
    expect(apply.applyResult.writtenFiles).toEqual(['romfs/kmeditor/items.readmodel.json']);
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
