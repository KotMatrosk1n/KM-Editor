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
  loadItemsWorkflowRequestSchema,
  loadItemsWorkflowResponseSchema,
  openProjectRequestSchema,
  openProjectResponseSchema,
  refreshFileGraphRequestSchema,
  refreshFileGraphResponseSchema,
  startEditSessionRequestSchema,
  startEditSessionResponseSchema,
  updateItemBuyPriceRequestSchema,
  updateItemBuyPriceResponseSchema,
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

  it('validates workflow list and Items load envelopes', () => {
    const workflowsRequestSchema = createBridgeRequestSchema(listWorkflowsRequestSchema);
    const workflowsResponseSchema = createBridgeResponseSchema(listWorkflowsResponseSchema);
    const itemsRequestSchema = createBridgeRequestSchema(loadItemsWorkflowRequestSchema);
    const itemsResponseSchema = createBridgeResponseSchema(loadItemsWorkflowResponseSchema);
    const itemsWorkflow = {
      diagnostics: [],
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
          workflows: [itemsWorkflow.summary]
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
  });

  it('validates edit session and Items buy price update envelopes', () => {
    const startRequestSchema = createBridgeRequestSchema(startEditSessionRequestSchema);
    const startResponseSchema = createBridgeResponseSchema(startEditSessionResponseSchema);
    const updateRequestSchema = createBridgeRequestSchema(updateItemBuyPriceRequestSchema);
    const updateResponseSchema = createBridgeResponseSchema(updateItemBuyPriceResponseSchema);
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
        command: kmCommandNames.updateItemBuyPrice,
        payload: {
          buyPrice: 450,
          itemId: 1,
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
              message: 'Pending item buy price change is valid.',
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
