/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke } from '@tauri-apps/api/core';
import { z, type ZodTypeAny } from 'zod';
import {
  type ApiError,
  type KmCommandName,
  type OpenProjectRequest,
  type OpenProjectResponse,
  type RefreshFileGraphRequest,
  type RefreshFileGraphResponse,
  type ValidateProjectRequest,
  type ValidateProjectResponse,
  createBridgeResponseSchema,
  kmCommandNames,
  openProjectResponseSchema,
  refreshFileGraphResponseSchema,
  validateProjectResponseSchema
} from './contracts';

export type ProjectBridge = {
  openProject: (request: OpenProjectRequest) => Promise<OpenProjectResponse>;
  refreshFileGraph: (request: RefreshFileGraphRequest) => Promise<RefreshFileGraphResponse>;
  validateProject: (request: ValidateProjectRequest) => Promise<ValidateProjectResponse>;
};

export type ProjectBridgeTransport = (requestJson: string) => Promise<string>;

export class ProjectBridgeError extends Error {
  public readonly apiError: ApiError;

  public constructor(apiError: ApiError) {
    super(apiError.message);
    this.name = 'ProjectBridgeError';
    this.apiError = apiError;
  }
}

const tauriProjectBridgeTransport: ProjectBridgeTransport = (requestJson) => {
  if (!hasTauriRuntime()) {
    return Promise.reject(new Error('Project bridge is only available in the desktop app.'));
  }

  return invoke<string>('project_bridge_once', { requestJson });
};

export function createProjectBridge(
  transport: ProjectBridgeTransport = tauriProjectBridgeTransport
): ProjectBridge {
  return {
    openProject: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.openProject,
        request,
        openProjectResponseSchema
      ),
    refreshFileGraph: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.refreshFileGraph,
        request,
        refreshFileGraphResponseSchema
      ),
    validateProject: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.validateProject,
        request,
        validateProjectResponseSchema
      )
  };
}

export const projectBridge = createProjectBridge();

async function sendProjectBridgeRequest<TPayloadSchema extends ZodTypeAny>(
  transport: ProjectBridgeTransport,
  command: KmCommandName,
  payload: unknown,
  payloadSchema: TPayloadSchema
): Promise<z.infer<TPayloadSchema>> {
  const requestId = createRequestId(command);
  const responseJson = await transport(
    JSON.stringify({
      command,
      payload,
      requestId
    })
  );
  const response = createBridgeResponseSchema(payloadSchema).parse(JSON.parse(responseJson));

  if (response.error) {
    throw new ProjectBridgeError(response.error);
  }

  if (response.payload === null || response.payload === undefined) {
    throw new Error('Project bridge response did not include a payload.');
  }

  return response.payload;
}

function createRequestId(command: KmCommandName) {
  const randomValue = globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2);

  return `${command}:${randomValue}`;
}

function hasTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}
