/* SPDX-License-Identifier: GPL-3.0-only */

import { ProjectBridgeError, createProjectBridge } from './projectBridge';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: null
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
