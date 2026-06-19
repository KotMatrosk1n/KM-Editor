/* SPDX-License-Identifier: GPL-3.0-only */

import { z, type ZodTypeAny } from 'zod';
import {
  createBridgeResponseSchema,
  type KmCommandName
} from './contracts';
import { ProjectBridgeError } from './projectBridgeError';

export type ProjectBridgeTransport = (requestJson: string) => Promise<string>;

export async function sendProjectBridgeRequest<TPayloadSchema extends ZodTypeAny>(
  transport: ProjectBridgeTransport,
  command: KmCommandName,
  payload: unknown,
  payloadSchema: TPayloadSchema
): Promise<z.infer<TPayloadSchema>> {
  const requestId = createRequestId(command);
  const responseJson = await transport(JSON.stringify({ command, payload, requestId }));
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
