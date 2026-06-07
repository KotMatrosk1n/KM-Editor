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
          outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: 'output'
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
    } as const;
    const textWorkflow = {
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
    } as const;
    const trainersWorkflow = {
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
    } as const;
    const shopsWorkflow = {
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
    } as const;
    const encountersWorkflow = {
      diagnostics: [],
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
    } as const;
    const raidRewardsWorkflow = {
      diagnostics: [],
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
    } as const;
    const placementWorkflow = {
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
        description: 'Placed objects, map coordinates, script links, and source provenance.',
        diagnostics: [],
        id: 'placement',
        label: 'Placement'
      }
    } as const;
    const flagworkSaveWorkflow = {
      diagnostics: [],
      flags: [
        {
          category: 'Story',
          defaultValue: 'false',
          description: 'First gym badge story flag.',
          flagId: 'story.badge_1',
          name: 'Badge 1 Obtained',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/kmeditor/flagwork.save.readmodel.json',
            sourceLayer: 'base'
          },
          valueKind: 'boolean'
        }
      ],
      saveBlocks: [
        {
          blockId: 'player.profile',
          description: 'Player profile save block.',
          length: 64,
          name: 'Player Profile',
          offset: 128,
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/kmeditor/flagwork.save.readmodel.json',
            sourceLayer: 'base'
          }
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalFlagCount: 1,
        totalSaveBlockCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Game flags, save blocks, inspector metadata, and source provenance.',
        diagnostics: [],
        id: 'flagworkSave',
        label: 'Flagwork and Save Inspectors'
      }
    } as const;
    const exeFsPatchWorkflow = {
      diagnostics: [],
      patches: [
        {
          description: 'Enable a safe ExeFS patch fixture.',
          name: 'Sample ExeFS Patch',
          patchId: 'sample_patch',
          patchKind: 'IPS',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'exefs/kmeditor/exefs.patches.readmodel.json',
            sourceLayer: 'base'
          },
          status: 'available',
          targetFile: 'exefs/main'
        }
      ],
      stats: {
        sourceFileCount: 1,
        totalPatchCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'ExeFS patch definitions, target files, statuses, and source provenance.',
        diagnostics: [],
        id: 'exefsPatches',
        label: 'ExeFS Patch Manager'
      }
    } as const;
    const royalCandyWorkflow = {
      diagnostics: [],
      stats: {
        sourceFileCount: 1,
        totalStepCount: 1,
        totalWorkflowCount: 1
      },
      summary: {
        availability: 'readOnly',
        description: 'Curated batch workflow recipes, targets, steps, and source provenance.',
        diagnostics: [],
        id: 'royalCandy',
        label: 'Royal Candy Workflows'
      },
      workflows: [
        {
          category: 'Items',
          description: 'Prepare a safe candy reward workflow fixture.',
          name: 'Candy Reward Setup',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/kmeditor/royal-candy.workflows.readmodel.json',
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
          target: 'items',
          workflowId: 'candy_reward_setup'
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
          description: 'Import item price columns from a workbook fixture.',
          name: 'Items Price Sheet',
          profileId: 'items_price_sheet',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/kmeditor/spreadsheet-import.profiles.readmodel.json',
            sourceLayer: 'base'
          },
          sourceKind: 'xlsx',
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
        description: 'Spreadsheet import profiles, target workflows, columns, and source provenance.',
        diagnostics: [],
        id: 'spreadsheetImport',
        label: 'Spreadsheet Import Tooling'
      }
    } as const;

    expect(
      workflowsRequestSchema.safeParse({
        command: kmCommandNames.listWorkflows,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null
          }
        }
      }).success
    ).toBe(true);

    expect(
      workflowsResponseSchema.safeParse({
        payload: {
          workflows: [
            itemsWorkflow.summary,
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
            outputRootPath: null
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
      textRequestSchema.safeParse({
        command: kmCommandNames.loadTextWorkflow,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
            outputRootPath: null
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
  });

  it('validates edit session and Items buy price update envelopes', () => {
    const startRequestSchema = createBridgeRequestSchema(startEditSessionRequestSchema);
    const startResponseSchema = createBridgeResponseSchema(startEditSessionResponseSchema);
    const updateRequestSchema = createBridgeRequestSchema(updateItemFieldRequestSchema);
    const updateResponseSchema = createBridgeResponseSchema(updateItemFieldResponseSchema);
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
              relativePath: 'romfs/kmeditor/items.readmodel.json'
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
              relativePath: 'romfs/kmeditor/items.readmodel.json'
            }
          ],
          targetRelativePath: 'romfs/kmeditor/items.readmodel.json'
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
    } as const;

    expect(
      startRequestSchema.safeParse({
        command: kmCommandNames.startEditSession,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output'
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
            outputRootPath: 'output'
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
      validateRequestSchema.safeParse({
        command: kmCommandNames.validateEditSession,
        payload: {
          paths: {
            baseExeFsPath: 'base-exefs',
            baseRomFsPath: 'base-romfs',
            outputRootPath: 'output'
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
            outputRootPath: 'output'
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
            outputRootPath: 'output'
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
                message: 'Applied Items change plan to the configured output root.',
                severity: 'info'
              }
            ],
            writtenFiles: ['romfs/kmeditor/items.readmodel.json']
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
