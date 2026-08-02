/* SPDX-License-Identifier: GPL-3.0-only */

import { isKmErrorCode, type KmErrorCode, uiErrorCodes } from './errorCodes';

export type ReportableErrorKind =
  | 'bridge'
  | 'desktop'
  | 'render'
  | 'unhandled'
  | 'unhandledRejection';

export type KmIncidentFingerprint = `KM-INCIDENT-${Uppercase<string>}`;

export type ReportableError = {
  command?: string;
  incidentFingerprint: KmIncidentFingerprint;
  message: string;
  requestId?: string;
  responseRequestId?: string;
  semanticCode?: KmErrorCode;
  title: string;
};

const githubIssuesUrl = 'https://github.com/KotMatrosk1n/KM-Editor/issues';

const incidentFingerprintPrefixes = {
  bridge: 'KM-INCIDENT-BRIDGE',
  desktop: 'KM-INCIDENT-DESKTOP',
  render: 'KM-INCIDENT-UI-RENDER',
  unhandled: 'KM-INCIDENT-UI-UNHANDLED',
  unhandledRejection: 'KM-INCIDENT-UI-PROMISE'
} as const satisfies Record<ReportableErrorKind, string>;

const defaultSemanticCodes: Partial<Record<ReportableErrorKind, KmErrorCode>> = {
  render: uiErrorCodes.renderUnexpected,
  unhandled: uiErrorCodes.unhandled,
  unhandledRejection: uiErrorCodes.unhandledPromise
};

const reportedGlobalIncidentFingerprints = new Set<string>();
const maximumRememberedGlobalIncidentFingerprints = 100;
let uninstallGlobalErrorHandlers: (() => void) | null = null;

export function createReportableError(
  error: unknown,
  {
    command,
    fallbackMessage = 'KM Editor hit an unexpected error.',
    kind,
    requestId,
    seed,
    semanticCode,
    title = 'KM Editor hit a critical error.'
  }: {
    command?: string;
    fallbackMessage?: string;
    kind: ReportableErrorKind;
    requestId?: string;
    seed?: string;
    semanticCode?: KmErrorCode;
    title?: string;
  }
): ReportableError {
  const resolvedCommand = command ?? readErrorContext(error, 'command');
  const resolvedRequestId = requestId ?? readErrorContext(error, 'requestId');
  const resolvedResponseRequestId = readErrorContext(error, 'responseRequestId');
  const primaryMessage = redactReportableContext(
    sanitizeReportableErrorText(toUnknownErrorMessage(error, fallbackMessage)),
    resolvedRequestId,
    resolvedResponseRequestId
  );
  const cause = readErrorCause(error);
  const causeMessage = redactReportableContext(
    sanitizeReportableErrorText(toUnknownCauseMessage(cause)),
    resolvedRequestId,
    resolvedResponseRequestId
  );
  const message =
    causeMessage.length > 0 && !primaryMessage.includes(causeMessage)
      ? `${primaryMessage}\n\nDetails: ${limitText(causeMessage, maximumCauseDetailLength)}`
      : primaryMessage;
  const requestedSemanticCode = semanticCode ?? readSemanticCode(error);
  const resolvedSemanticCode = isKmErrorCode(requestedSemanticCode)
    ? requestedSemanticCode
    : defaultSemanticCodes[kind];
  const hashInput = [
    kind,
    resolvedSemanticCode ?? '',
    resolvedCommand ?? '',
    redactReportableContext(seed ?? '', resolvedRequestId, resolvedResponseRequestId),
    message,
    redactReportableContext(
      sanitizeReportableErrorText(error instanceof Error ? error.stack ?? '' : ''),
      resolvedRequestId,
      resolvedResponseRequestId
    ),
    redactReportableContext(
      sanitizeReportableErrorText(toUnknownCauseFingerprint(cause)),
      resolvedRequestId,
      resolvedResponseRequestId
    )
  ].join('|');
  const incidentFingerprint = `${incidentFingerprintPrefixes[kind]}-${createShortHash(
    hashInput
  )}` as KmIncidentFingerprint;

  return {
    ...(resolvedCommand ? { command: resolvedCommand } : {}),
    incidentFingerprint,
    message,
    ...(resolvedRequestId ? { requestId: resolvedRequestId } : {}),
    ...(resolvedResponseRequestId && resolvedResponseRequestId !== resolvedRequestId
      ? { responseRequestId: resolvedResponseRequestId }
      : {}),
    ...(resolvedSemanticCode ? { semanticCode: resolvedSemanticCode } : {}),
    title
  };
}

export function formatReportableErrorMessage(
  report: ReportableError,
  { includeSemanticCode = true }: { includeSemanticCode?: boolean } = {}
) {
  return [
    report.title,
    '',
    ...(includeSemanticCode && report.semanticCode ? [`Error code: ${report.semanticCode}`] : []),
    `Incident fingerprint: ${report.incidentFingerprint}`,
    ...(report.command ? [`Command: ${report.command}`] : []),
    ...(report.requestId ? [`Request ID: ${report.requestId}`] : []),
    ...(report.responseRequestId
      ? [`Response request ID: ${report.responseRequestId}`]
      : []),
    '',
    'What to do:',
    'Take a screenshot of this message and report it in GitHub Issues.',
    githubIssuesUrl,
    '',
    'What happened:',
    report.message
  ].join('\n');
}

export function installGlobalErrorHandlers() {
  if (uninstallGlobalErrorHandlers !== null) {
    return uninstallGlobalErrorHandlers;
  }

  const handleError = (event: ErrorEvent) => {
    showGlobalReportableError(
      event.error ?? event.message,
      'unhandled',
      'KM Editor hit an unexpected app error.'
    );
  };

  const handleUnhandledRejection = (event: PromiseRejectionEvent) => {
    showGlobalReportableError(
      event.reason,
      'unhandledRejection',
      'KM Editor hit an unexpected background error.'
    );
  };

  window.addEventListener('error', handleError);
  window.addEventListener('unhandledrejection', handleUnhandledRejection);

  uninstallGlobalErrorHandlers = () => {
    window.removeEventListener('error', handleError);
    window.removeEventListener('unhandledrejection', handleUnhandledRejection);
    uninstallGlobalErrorHandlers = null;
  };

  return uninstallGlobalErrorHandlers;
}

function showGlobalReportableError(
  error: unknown,
  kind: ReportableErrorKind,
  fallbackMessage: string
) {
  const report = createReportableError(error, {
    fallbackMessage,
    kind
  });

  if (reportedGlobalIncidentFingerprints.has(report.incidentFingerprint)) {
    return;
  }

  rememberBoundedValue(
    reportedGlobalIncidentFingerprints,
    report.incidentFingerprint,
    maximumRememberedGlobalIncidentFingerprints
  );
  window.alert(formatReportableErrorMessage(report));
}

function rememberBoundedValue(values: Set<string>, value: string, maximumSize: number) {
  if (values.size >= maximumSize) {
    const oldestValue = values.values().next().value;
    if (oldestValue !== undefined) {
      values.delete(oldestValue);
    }
  }

  values.add(value);
}

function toUnknownErrorMessage(error: unknown, fallbackMessage: string) {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  if (typeof error === 'string' && error.trim().length > 0) {
    return error;
  }

  return fallbackMessage;
}

const maximumCauseDetailLength = 1_200;

function readErrorCause(error: unknown) {
  if (typeof error !== 'object' || error === null || !('cause' in error)) {
    return undefined;
  }

  return error.cause;
}

function toUnknownCauseMessage(cause: unknown) {
  if (cause instanceof Error && cause.message.trim().length > 0) {
    return cause.message;
  }

  if (typeof cause === 'string' && cause.trim().length > 0) {
    return cause;
  }

  return '';
}

function toUnknownCauseFingerprint(cause: unknown) {
  if (cause instanceof Error) {
    return [cause.name, cause.message, cause.stack ?? ''].join('|');
  }

  return typeof cause === 'string' ? cause : '';
}

function readSemanticCode(error: unknown): KmErrorCode | undefined {
  if (typeof error !== 'object' || error === null) {
    return undefined;
  }

  if ('semanticCode' in error && isKmErrorCode(error.semanticCode)) {
    return error.semanticCode;
  }

  if ('code' in error && isKmErrorCode(error.code)) {
    return error.code;
  }

  return undefined;
}

function readErrorContext(
  error: unknown,
  property: 'command' | 'requestId' | 'responseRequestId'
) {
  if (typeof error !== 'object' || error === null || !(property in error)) {
    return undefined;
  }

  const value = (error as Record<string, unknown>)[property];
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

export function sanitizeReportableErrorText(value: string) {
  return value
    .replace(
      /(["'])(?:file:\/\/\/?[^"'\r\n]+|[A-Za-z]:[\\/][^"'\r\n]*|\\\\[^"'\r\n]+)\1/gi,
      (_match, quote: string) => `${quote}[local path]${quote}`
    )
    // Unquoted native errors can contain paths with spaces. Once an absolute path starts,
    // redact the rest of that line; there is no reliable delimiter between the final path
    // segment and arbitrary native prose, and privacy is more important than that suffix.
    .replace(/\bfile:\/\/\/?[^\r\n"'<>]+/gi, '[local path]')
    .replace(/\b[A-Za-z]:[\\/][^\r\n"'<>|]*/g, '[local path]')
    .replace(/\\\\[^\\/\r\n"'<>|]+[\\/][^\r\n"'<>|]*/g, '[local path]');
}

function redactReportableContext(
  value: string,
  requestId: string | undefined,
  responseRequestId: string | undefined
) {
  return redactExactValue(
    redactExactValue(value, requestId, '[request ID]'),
    responseRequestId,
    '[response request ID]'
  );
}

function redactExactValue(value: string, sensitiveValue: string | undefined, replacement: string) {
  return sensitiveValue ? value.replaceAll(sensitiveValue, replacement) : value;
}

function limitText(value: string, maximumLength: number) {
  return value.length <= maximumLength
    ? value
    : `${value.slice(0, maximumLength - 1).trimEnd()}…`;
}

function createShortHash(value: string) {
  let hash = 0x811c9dc5;

  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }

  return (hash >>> 0).toString(36).toUpperCase().padStart(7, '0');
}
