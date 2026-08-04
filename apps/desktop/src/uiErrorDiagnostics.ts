/* SPDX-License-Identifier: GPL-3.0-only */

import type { ApiDiagnostic } from './bridge/contracts';
import { isStaleProjectScopeError } from './bridge/gameScopedProjectBridge';
import { ProjectBridgeError } from './bridge/projectBridgeError';
import { DesktopServiceError } from './desktopServices';
import {
  desktopErrorCodes,
  projectBridgeErrorCodes,
  swshPlacementErrorCodes,
  swshDynamaxAdventuresErrorCodes,
  type KmErrorCode
} from './errorCodes';
import { createReportableError, formatReportableErrorMessage } from './errorReporting';

const expectedBridgeErrorCodes = new Set<KmErrorCode>([
  swshDynamaxAdventuresErrorCodes.seedInvalid,
  swshDynamaxAdventuresErrorCodes.seedLimitInvalid,
  swshDynamaxAdventuresErrorCodes.startSeedInvalid,
  swshPlacementErrorCodes.catalogStale,
  projectBridgeErrorCodes.gameMismatch
]);

export function toProjectBridgeDiagnostics(
  error: unknown,
  fallbackMessage = 'Project bridge request failed.'
): ApiDiagnostic[] {
  if (isStaleProjectScopeError(error)) {
    return [];
  }

  if (error instanceof ProjectBridgeError) {
    if (error.apiError.diagnostics.length > 0) {
      return error.apiError.diagnostics;
    }

    const semanticCode = error.semanticCode ?? projectBridgeErrorCodes.unexpected;
    if (expectedBridgeErrorCodes.has(semanticCode)) {
      return [createDiagnostic(semanticCode, error.apiError.message, 'bridge')];
    }

    const report = createReportableError(error, {
      fallbackMessage: error.apiError.message,
      kind: 'bridge',
      semanticCode,
      title: 'KM Editor hit an unexpected bridge error.'
    });
    return [
      createDiagnostic(
        semanticCode,
        formatReportableErrorMessage(report, { includeSemanticCode: false }),
        'bridge'
      )
    ];
  }

  const report = createReportableError(error, {
    fallbackMessage,
    kind: 'bridge',
    semanticCode: projectBridgeErrorCodes.transportFailed,
    title: 'KM Editor hit an unexpected bridge error.'
  });
  return [
    createDiagnostic(
      projectBridgeErrorCodes.transportFailed,
      formatReportableErrorMessage(report, { includeSemanticCode: false }),
      'bridge'
    )
  ];
}

export function toDesktopErrorDiagnostics(
  error: unknown,
  fallbackMessage: string,
  fallbackCode: KmErrorCode = desktopErrorCodes.unexpected
): ApiDiagnostic[] {
  const semanticCode = error instanceof DesktopServiceError ? error.code : fallbackCode;
  const reportError =
    error instanceof DesktopServiceError
      ? error
      : new DesktopServiceError(
          semanticCode,
          combineErrorContext(fallbackMessage, error),
          error
        );
  const report = createReportableError(reportError, {
    fallbackMessage,
    kind: 'desktop',
    semanticCode,
    title: 'KM Editor hit an unexpected desktop error.'
  });
  return [
    createDiagnostic(
      semanticCode,
      formatReportableErrorMessage(report, { includeSemanticCode: false }),
      'desktop'
    )
  ];
}

function combineErrorContext(fallbackMessage: string, error: unknown) {
  const detail =
    error instanceof Error
      ? error.message.trim()
      : typeof error === 'string'
        ? error.trim()
        : '';

  return detail.length > 0 && detail !== fallbackMessage
    ? `${fallbackMessage} ${detail}`
    : fallbackMessage;
}

function createDiagnostic(code: KmErrorCode, message: string, domain: string): ApiDiagnostic {
  return {
    code,
    domain,
    message,
    severity: 'error'
  };
}
