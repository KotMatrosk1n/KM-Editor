/* SPDX-License-Identifier: GPL-3.0-only */

import { isKmErrorCode, type KmErrorCode } from '../errorCodes';
import { type ApiError, type KmCommandName } from './contracts';

export type ProjectBridgeErrorContext = {
  cause?: unknown;
  command?: KmCommandName | null;
  requestId?: string | null;
  responseRequestId?: string | null;
};

export class ProjectBridgeError extends Error {
  public readonly apiError: ApiError;
  public readonly command: KmCommandName | null;
  public readonly requestId: string | null;
  public readonly responseRequestId: string | null;
  public readonly semanticCode: KmErrorCode | null;

  constructor(apiError: ApiError, context: ProjectBridgeErrorContext = {}) {
    super(apiError.message, { cause: context.cause });
    this.name = 'ProjectBridgeError';
    this.semanticCode = isKmErrorCode(apiError.code) ? apiError.code : null;
    this.apiError = this.semanticCode
      ? {
          ...apiError,
          diagnostics: apiError.diagnostics.map((diagnostic) => ({
            ...diagnostic,
            code: diagnostic.code ?? this.semanticCode
          }))
        }
      : apiError;
    this.command = context.command ?? null;
    this.requestId = context.requestId ?? null;
    this.responseRequestId = context.responseRequestId ?? null;
  }
}
