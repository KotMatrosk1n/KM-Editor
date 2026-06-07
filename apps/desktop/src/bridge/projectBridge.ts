/* SPDX-License-Identifier: GPL-3.0-only */

import {
  type ApiDiagnostic,
  type OpenProjectRequest,
  type OpenProjectResponse,
  type ProjectFileGraph,
  type ProjectHealth,
  type ProjectPathValidation,
  type RefreshFileGraphRequest,
  type RefreshFileGraphResponse,
  type ValidateProjectRequest,
  type ValidateProjectResponse
} from './contracts';

export type ProjectBridge = {
  openProject: (request: OpenProjectRequest) => Promise<OpenProjectResponse>;
  refreshFileGraph: (request: RefreshFileGraphRequest) => Promise<RefreshFileGraphResponse>;
  validateProject: (request: ValidateProjectRequest) => Promise<ValidateProjectResponse>;
};

const emptyFileGraph: ProjectFileGraph = {
  entries: [],
  summary: {
    baseFileCount: 0,
    layeredFileCount: 0,
    layeredOnlyCount: 0,
    overrideCount: 0
  }
};

export const projectBridge: ProjectBridge = {
  async openProject(request) {
    const health = buildShellHealth(request);

    return {
      fileGraph: emptyFileGraph,
      health,
      projectId: 'shell-project'
    };
  },

  async refreshFileGraph() {
    return {
      fileGraph: emptyFileGraph
    };
  },

  async validateProject(request) {
    return {
      health: buildShellHealth(request)
    };
  }
};

function buildShellHealth(request: OpenProjectRequest): ProjectHealth {
  const { baseExeFsPath, baseRomFsPath, outputRootPath } = request.paths;
  const baseRomFs = validateRequiredPath('baseRomFs', 'Base RomFS', baseRomFsPath);
  const baseExeFs = validateRequiredPath('baseExeFs', 'Base ExeFS', baseExeFsPath);
  const outputRoot = validateOutputPath(outputRootPath, baseRomFsPath, baseExeFsPath);
  const paths = [baseRomFs, baseExeFs, outputRoot];
  const diagnostics = paths.flatMap((path) => path.diagnostics);
  const basePathsValid = baseRomFs.status === 'valid' && baseExeFs.status === 'valid';
  const hasErrors = diagnostics.some((diagnostic) => diagnostic.severity === 'error');
  const state = !basePathsValid
    ? 'needsPaths'
    : hasErrors
      ? 'blocked'
      : outputRoot.status === 'valid'
        ? 'editableReady'
        : 'readOnlyReady';

  return {
    canOpenEditableWorkflows: state === 'editableReady',
    canOpenReadOnlyWorkflows: state === 'readOnlyReady' || state === 'editableReady',
    diagnostics,
    fileGraph: emptyFileGraph.summary,
    paths,
    state
  };
}

function validateRequiredPath(
  role: 'baseRomFs' | 'baseExeFs',
  label: string,
  path: string | null
): ProjectPathValidation {
  if (!path) {
    return {
      diagnostics: [
        diagnostic('error', `${label} path is required.`, label, 'Existing directory')
      ],
      isRequired: true,
      path,
      role,
      status: 'notSet'
    };
  }

  return {
    diagnostics: [],
    isRequired: true,
    path,
    role,
    status: 'valid'
  };
}

function validateOutputPath(
  outputRootPath: string | null,
  baseRomFsPath: string | null,
  baseExeFsPath: string | null
): ProjectPathValidation {
  if (!outputRootPath) {
    return {
      diagnostics: [
        diagnostic(
          'warning',
          'Output root is not configured; write actions are disabled.',
          'Output Root',
          'Existing directory before applying output'
        )
      ],
      isRequired: false,
      path: outputRootPath,
      role: 'outputRoot',
      status: 'notSet'
    };
  }

  // The preview bridge cannot inspect the file system yet, but it can enforce the visible safety boundary.
  if (pathsMatch(outputRootPath, baseRomFsPath) || pathsMatch(outputRootPath, baseExeFsPath)) {
    return {
      diagnostics: [
        diagnostic(
          'error',
          'Output root must not match base RomFS or base ExeFS.',
          'Output Root',
          'Separate LayeredFS output directory'
        )
      ],
      isRequired: false,
      path: outputRootPath,
      role: 'outputRoot',
      status: 'unsafe'
    };
  }

  return {
    diagnostics: [],
    isRequired: false,
    path: outputRootPath,
    role: 'outputRoot',
    status: 'valid'
  };
}

function diagnostic(
  severity: ApiDiagnostic['severity'],
  message: string,
  field: string,
  expected: string
): ApiDiagnostic {
  return {
    domain: 'project',
    expected,
    field,
    message,
    severity
  };
}

function pathsMatch(firstPath: string | null, secondPath: string | null) {
  return firstPath !== null && secondPath !== null && firstPath.trim() === secondPath.trim();
}
