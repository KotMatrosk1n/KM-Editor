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

    expect(workflows.workflows[0]?.id).toBe('items');
    expect(workflows.workflows[1]?.id).toBe('text');
    expect(items.workflow.editableFields).toHaveLength(2);
    expect(items.workflow.items[0]?.name).toBe('Potion');
    expect(text.workflow.entries[0]?.label).toBe('Greeting');
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
