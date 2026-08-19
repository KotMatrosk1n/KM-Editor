/* SPDX-License-Identifier: GPL-3.0-only */

import { z, type ZodTypeAny } from 'zod';
import { projectBridgeErrorCodes, type KmErrorCode } from '../errorCodes';
import {
  createBridgeResponseSchema,
  type ApiError,
  type KmCommandName
} from './contracts';
import { ProjectBridgeError } from './projectBridgeError';
import { recordBridgePerformanceDiagnostic } from '../performanceDiagnostics';

export type ProjectBridgeTransport = (requestJson: string) => Promise<string>;

const maximumProjectBridgeRequestBytes = 16 * 1024 * 1024;

export async function sendProjectBridgeRequest<TPayloadSchema extends ZodTypeAny>(
  transport: ProjectBridgeTransport,
  command: KmCommandName,
  payload: unknown,
  payloadSchema: TPayloadSchema
): Promise<z.infer<TPayloadSchema>> {
  const startedAt = performance.now();
  try {
    const response = await sendProjectBridgeRequestInner(transport, command, payload, payloadSchema);
    recordBridgePerformanceDiagnostic(command, performance.now() - startedAt, 'success');
    return response;
  } catch (error) {
    recordBridgePerformanceDiagnostic(command, performance.now() - startedAt, 'failure');
    throw error;
  }
}

async function sendProjectBridgeRequestInner<TPayloadSchema extends ZodTypeAny>(
  transport: ProjectBridgeTransport,
  command: KmCommandName,
  payload: unknown,
  payloadSchema: TPayloadSchema
): Promise<z.infer<TPayloadSchema>> {
  const requestId = createRequestId(command);
  const requestJson = JSON.stringify({ command, payload, requestId });
  if (
    requestJson.length > maximumProjectBridgeRequestBytes ||
    new TextEncoder().encode(requestJson).byteLength > maximumProjectBridgeRequestBytes
  ) {
    throw new ProjectBridgeError(
      {
        code: projectBridgeErrorCodes.requestTooLarge,
        diagnostics: [],
        message: 'The project bridge request exceeds the supported size limit.'
      },
      {
        command,
        requestId,
        responseRequestId: null
      }
    );
  }

  let responseJson: string;
  try {
    responseJson = await transport(requestJson);
  } catch (error) {
    throw createProjectBridgeProtocolError(
      projectBridgeErrorCodes.transportFailed,
      'The project bridge request could not be sent or completed.',
      command,
      requestId,
      null,
      error
    );
  }

  let responseValue: unknown;
  try {
    responseValue = JSON.parse(responseJson);
  } catch (error) {
    throw createProjectBridgeProtocolError(
      projectBridgeErrorCodes.invalidResponseJson,
      'The project bridge returned invalid JSON.',
      command,
      requestId,
      null,
      error
    );
  }

  const responseResult = createBridgeResponseSchema(payloadSchema).safeParse(responseValue);
  if (!responseResult.success) {
    throw createProjectBridgeProtocolError(
      projectBridgeErrorCodes.invalidResponseContract,
      'The project bridge returned a response that does not match the expected contract.',
      command,
      requestId,
      readResponseRequestId(responseValue),
      responseResult.error
    );
  }

  const response = responseResult.data;
  const responseRequestId = response.requestId ?? null;
  if (responseRequestId === null) {
    // Request-size rejection happens before the backend parses the envelope, so it
    // cannot safely recover the request ID. This one deterministic pre-envelope
    // failure is still attributable to the serialized request sent by this call.
    if (response.error?.code === projectBridgeErrorCodes.requestTooLarge) {
      throw new ProjectBridgeError(response.error, {
        command,
        requestId,
        responseRequestId
      });
    }

    throw createProjectBridgeProtocolError(
      projectBridgeErrorCodes.missingRequestId,
      'The project bridge response did not include its request ID.',
      command,
      requestId,
      responseRequestId
    );
  }

  if (responseRequestId !== requestId) {
    throw createProjectBridgeProtocolError(
      projectBridgeErrorCodes.requestIdMismatch,
      'The project bridge response did not match the active request.',
      command,
      requestId,
      responseRequestId
    );
  }

  if (response.error) {
    throw new ProjectBridgeError(response.error, {
      command,
      requestId,
      responseRequestId
    });
  }

  if (response.payload === null || response.payload === undefined) {
    throw createProjectBridgeProtocolError(
      projectBridgeErrorCodes.missingPayload,
      'The project bridge response did not include a payload.',
      command,
      requestId,
      responseRequestId
    );
  }

  return response.payload;
}

function createRequestId(command: KmCommandName) {
  const randomValue = globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2);
  const commandSegment = command.replace(/[^a-zA-Z0-9]+/g, '-').toUpperCase();
  return `KM-REQUEST-${commandSegment}-${randomValue.toUpperCase()}`;
}

function createProjectBridgeProtocolError(
  code: KmErrorCode,
  message: string,
  command: KmCommandName,
  requestId: string,
  responseRequestId: string | null,
  cause?: unknown
) {
  const apiError: ApiError = {
    code,
    diagnostics: [],
    message
  };

  return new ProjectBridgeError(apiError, {
    cause,
    command,
    requestId,
    responseRequestId
  });
}

function readResponseRequestId(value: unknown) {
  if (typeof value !== 'object' || value === null || !('requestId' in value)) {
    return null;
  }

  const requestId = value.requestId;
  return typeof requestId === 'string' ? requestId : null;
}
