/* SPDX-License-Identifier: GPL-3.0-only */

import type { ApiDiagnostic } from './bridge/contracts';
import { isExpectedProjectBridgeRejection } from './bridge/projectBridgeErrorClassification';
import { isStaleProjectScopeError } from './bridge/gameScopedProjectBridge';
import { ProjectBridgeError } from './bridge/projectBridgeError';
import { DesktopServiceError } from './desktopServices';
import {
  desktopErrorCodes,
  isKmErrorCode,
  projectBridgeErrorCodes,
  type KmErrorCode
} from './errorCodes';
import {
  createReportableError,
  formatReportableErrorMessage,
  sanitizeReportableErrorText
} from './errorReporting';

const maximumBackendDiagnosticSourceUnits = 16 * 1024;
const maximumBackendDiagnosticTextLength = 4_000;

export function toProjectBridgeDiagnostics(
  error: unknown,
  fallbackMessage = 'Project bridge request failed.'
): ApiDiagnostic[] {
  if (isStaleProjectScopeError(error)) {
    return [];
  }

  if (error instanceof ProjectBridgeError) {
    if (error.apiError.diagnostics.length > 0) {
      if (error.apiError.code === projectBridgeErrorCodes.unexpected) {
        return createUnexpectedBridgeDiagnostics(error);
      }

      return error.apiError.diagnostics;
    }

    const semanticCode = error.semanticCode ?? projectBridgeErrorCodes.unexpected;
    if (isExpectedProjectBridgeRejection(error)) {
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

function createUnexpectedBridgeDiagnostics(error: ProjectBridgeError): ApiDiagnostic[] {
  const [primaryDiagnostic, ...remainingDiagnostics] = error.apiError.diagnostics;
  if (!primaryDiagnostic) {
    return [];
  }

  const sanitizedPrimaryDiagnostic = sanitizeBackendDiagnostic(primaryDiagnostic, error);
  const semanticCode = isKmErrorCode(sanitizedPrimaryDiagnostic.code)
    ? sanitizedPrimaryDiagnostic.code
    : error.semanticCode ?? projectBridgeErrorCodes.unexpected;
  const report = createReportableError(error, {
    fallbackMessage: error.apiError.message,
    kind: 'bridge',
    seed: createBackendDiagnosticSeed(sanitizedPrimaryDiagnostic, error),
    semanticCode,
    title: 'KM Editor hit an unexpected bridge error.'
  });
  const reportMessage = formatReportableErrorMessage(report, { includeSemanticCode: false });
  const backendMessage = sanitizedPrimaryDiagnostic.message.trim();

  return [
    {
      ...sanitizedPrimaryDiagnostic,
      message:
        backendMessage.length > 0 && !reportMessage.includes(backendMessage)
          ? `${reportMessage}\n\nBackend diagnostic:\n${backendMessage}`
          : reportMessage
    },
    ...remainingDiagnostics.map((diagnostic) => sanitizeBackendDiagnostic(diagnostic, error))
  ];
}

function sanitizeBackendDiagnostic(
  diagnostic: ApiDiagnostic,
  error: ProjectBridgeError
): ApiDiagnostic {
  return {
    ...diagnostic,
    domain: sanitizeOptionalBackendDiagnosticText(diagnostic.domain, error),
    expected: sanitizeOptionalBackendDiagnosticText(diagnostic.expected, error),
    field: sanitizeOptionalBackendDiagnosticText(diagnostic.field, error),
    file: sanitizeOptionalBackendDiagnosticText(diagnostic.file, error),
    message: sanitizeBackendDiagnosticText(diagnostic.message, error)
  };
}

function createBackendDiagnosticSeed(
  diagnostic: ApiDiagnostic,
  error: ProjectBridgeError
) {
  return JSON.stringify([
    diagnostic.code ?? null,
    diagnostic.severity,
    sanitizeOptionalBackendDiagnosticText(diagnostic.message, error),
    sanitizeOptionalBackendDiagnosticText(diagnostic.domain, error),
    sanitizeOptionalBackendDiagnosticText(diagnostic.file, error),
    sanitizeOptionalBackendDiagnosticText(diagnostic.field, error),
    sanitizeOptionalBackendDiagnosticText(diagnostic.expected, error)
  ]);
}

function sanitizeOptionalBackendDiagnosticText(
  value: string | null | undefined,
  error: ProjectBridgeError
) {
  return value === null || value === undefined
    ? null
    : sanitizeBackendDiagnosticText(value, error);
}

function sanitizeBackendDiagnosticText(value: string, error: ProjectBridgeError) {
  let sanitized = sanitizeReportableErrorText(
    value.slice(0, maximumBackendDiagnosticSourceUnits)
  );
  if (error.requestId && error.requestId.length <= 4_096) {
    sanitized = sanitized.replaceAll(error.requestId, '[request ID]');
  }
  if (error.responseRequestId && error.responseRequestId.length <= 4_096) {
    sanitized = sanitized.replaceAll(error.responseRequestId, '[response request ID]');
  }

  return sanitized.length <= maximumBackendDiagnosticTextLength
    ? sanitized
    : `${sanitized.slice(0, maximumBackendDiagnosticTextLength - 3).trimEnd()}...`;
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
