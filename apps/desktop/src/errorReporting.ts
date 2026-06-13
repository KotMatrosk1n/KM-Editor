/* SPDX-License-Identifier: GPL-3.0-only */

export type ReportableErrorKind =
  | 'bridge'
  | 'desktop'
  | 'render'
  | 'unhandled'
  | 'unhandledRejection';

export type ReportableError = {
  code: string;
  message: string;
  title: string;
};

const githubIssuesUrl = 'https://github.com/KotMatrosk1n/KM-Editor/issues';

const errorKindPrefixes = {
  bridge: 'KM-BRIDGE',
  desktop: 'KM-DESKTOP',
  render: 'KM-UI-RENDER',
  unhandled: 'KM-UI-UNHANDLED',
  unhandledRejection: 'KM-UI-PROMISE'
} as const satisfies Record<ReportableErrorKind, string>;

const reportedGlobalErrorCodes = new Set<string>();

export function createReportableError(
  error: unknown,
  {
    fallbackMessage = 'KM Editor hit an unexpected error.',
    kind,
    seed,
    title = 'KM Editor hit a critical error.'
  }: {
    fallbackMessage?: string;
    kind: ReportableErrorKind;
    seed?: string;
    title?: string;
  }
): ReportableError {
  const message = toUnknownErrorMessage(error, fallbackMessage);
  const hashInput = [
    kind,
    seed ?? '',
    message,
    error instanceof Error ? error.stack ?? '' : ''
  ].join('|');

  return {
    code: `${errorKindPrefixes[kind]}-${createShortHash(hashInput)}`,
    message,
    title
  };
}

export function formatReportableErrorMessage(report: ReportableError) {
  return [
    report.title,
    '',
    `Error code: ${report.code}`,
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
  window.addEventListener('error', (event) => {
    showGlobalReportableError(
      event.error ?? event.message,
      'unhandled',
      'KM Editor hit an unexpected app error.'
    );
  });

  window.addEventListener('unhandledrejection', (event) => {
    showGlobalReportableError(
      event.reason,
      'unhandledRejection',
      'KM Editor hit an unexpected background error.'
    );
  });
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

  if (reportedGlobalErrorCodes.has(report.code)) {
    return;
  }

  reportedGlobalErrorCodes.add(report.code);
  window.alert(formatReportableErrorMessage(report));
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

function createShortHash(value: string) {
  let hash = 0x811c9dc5;

  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }

  return (hash >>> 0).toString(36).toUpperCase().padStart(6, '0').slice(0, 6);
}
