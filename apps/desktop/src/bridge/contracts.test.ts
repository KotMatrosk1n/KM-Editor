/* SPDX-License-Identifier: GPL-3.0-only */

import {
  apiErrorSchema,
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
