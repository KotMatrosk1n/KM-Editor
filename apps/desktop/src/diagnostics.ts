/* SPDX-License-Identifier: GPL-3.0-only */

import type { ApiDiagnostic } from './bridge/contracts';

type DiagnosticTranslator = (literal: string) => string;
type DiagnosticKeyTranslator = (key: string) => string;

const identityTranslator: DiagnosticTranslator = (literal) => literal;

const diagnosticLocalizationKeys: Readonly<Record<string, string>> = {
  'KM-BRIDGE-ACCESS-DENIED': 'outputSafety.diagnostic.accessDenied',
  'KM-BRIDGE-EMPTY-REQUEST': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-DATA-INVALID': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-DATA-LAYOUT-INVALID': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-GAME-MISMATCH': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-INTERNAL-FAILURE': 'outputSafety.diagnostic.requestFailed',
  'KM-BRIDGE-INVALID-JSON': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-IO-FAILED': 'outputSafety.diagnostic.ioFailed',
  'KM-BRIDGE-MISSING-COMMAND': 'outputSafety.diagnostic.requestFailed',
  'KM-BRIDGE-REQUEST-TOO-LARGE': 'outputSafety.diagnostic.limitExceeded',
  'KM-BRIDGE-RESOURCE-MISSING': 'outputSafety.diagnostic.resourceMissing',
  'KM-BRIDGE-RESPONSE-CONTRACT-INVALID': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-RESPONSE-JSON-INVALID': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-RESPONSE-PAYLOAD-MISSING': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-RESPONSE-REQUEST-ID-MISMATCH': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-RESPONSE-REQUEST-ID-MISSING': 'outputSafety.diagnostic.dataInvalid',
  'KM-BRIDGE-SUPPORT-RUNTIME-UNAVAILABLE': 'outputSafety.diagnostic.runtimeUnavailable',
  'KM-BRIDGE-TRANSPORT-FAILED': 'outputSafety.diagnostic.requestFailed',
  'KM-BRIDGE-UNEXPECTED': 'outputSafety.diagnostic.requestFailed',
  'KM-BRIDGE-UNSUPPORTED-COMMAND': 'outputSafety.diagnostic.requestFailed',
  'KM-SEMANTIC-EXTERNAL-OVERLAY-REJECTED': 'semanticExplore.query.error.externalRejected',
  'KM-SEMANTIC-EXTERNAL-SNAPSHOT-UNAVAILABLE': 'semanticExplore.query.error.externalRejected',
  'KM-SEMANTIC-INVALID-CURSOR': 'semanticExplore.query.error.cursor',
  'KM-SEMANTIC-INVALID-QUERY': 'semanticExplore.query.error.invalidQuery',
  'KM-SEMANTIC-LIMIT-EXCEEDED': 'semanticExplore.query.error.limit',
  'KM-SEMANTIC-STALE-REVISION': 'semanticExplore.query.error.generic',
  'KM-SEMANTIC-UNSUPPORTED': 'semanticExplore.query.error.unsupported',
  'KM-DESKTOP-FILE-PICKER-FAILED': 'outputSafety.error.relocationPath',
  'KM-DESKTOP-FOLDER-PICKER-FAILED': 'outputSafety.error.relocationPath',
  'KM-DESKTOP-PATH-PICKER-FAILED': 'outputSafety.error.relocationPath',
  'KM-DESKTOP-RUNTIME-UNAVAILABLE': 'outputSafety.diagnostic.runtimeUnavailable',
  'KM-OUTPUT-BACKUP-INVALID': 'outputSafety.diagnostic.recoveryVerificationFailed',
  'KM-OUTPUT-CHECKPOINT-CONFLICT': 'outputSafety.diagnostic.checkpointConflict',
  'KM-OUTPUT-CHECKPOINT-NOT-FOUND': 'outputSafety.diagnostic.checkpointNotFound',
  'KM-OUTPUT-CLEANUP-NOTHING-SAFE': 'outputSafety.diagnostic.cleanupNothingSafe',
  'KM-OUTPUT-COMMIT-FAILED': 'outputSafety.diagnostic.transactionFailed',
  'KM-OUTPUT-CONCURRENT-MODIFICATION': 'outputSafety.diagnostic.concurrentModification',
  'KM-OUTPUT-FINALIZATION-FAILED': 'outputSafety.diagnostic.transactionFailed',
  'KM-OUTPUT-FOREIGN-DATA-PRESENT': 'outputSafety.diagnostic.foreignData',
  'KM-OUTPUT-HISTORY-TRUNCATED': 'outputSafety.diagnostic.displayTruncated',
  'KM-OUTPUT-INTEGRITY-STALE': 'outputSafety.diagnostic.integrityStale',
  'KM-OUTPUT-LIMIT-EXCEEDED': 'outputSafety.diagnostic.limitExceeded',
  'KM-OUTPUT-OWNERSHIP-UNPROVEN': 'outputSafety.diagnostic.ownershipUnproven',
  'KM-OUTPUT-POSTIMAGE-CHANGED': 'outputSafety.diagnostic.outputChanged',
  'KM-OUTPUT-RECOVERY-FINALIZED': 'outputSafety.diagnostic.recoveryFinalized',
  'KM-OUTPUT-RECOVERY-MANUAL-REQUIRED': 'outputSafety.diagnostic.recoveryManual',
  'KM-OUTPUT-RECOVERY-METADATA-UNAVAILABLE': 'outputSafety.diagnostic.recoveryMetadataUnavailable',
  'KM-OUTPUT-RECOVERY-PENDING': 'outputSafety.diagnostic.recoveryPending',
  'KM-OUTPUT-RECOVERY-REQUIRED': 'outputSafety.diagnostic.recoveryRequired',
  'KM-OUTPUT-RECOVERY-ROLLED-BACK': 'outputSafety.diagnostic.recoveryRolledBack',
  'KM-OUTPUT-RECOVERY-STATUS-UNAVAILABLE': 'outputSafety.diagnostic.recoveryStatusUnavailable',
  'KM-OUTPUT-ROLLBACK-FAILED': 'outputSafety.diagnostic.recoveryVerificationFailed',
  'KM-OUTPUT-ROLLBACK-TARGET-CHANGED': 'outputSafety.diagnostic.outputChanged',
  'KM-OUTPUT-ROLLBACK-VERIFICATION-FAILED': 'outputSafety.diagnostic.recoveryVerificationFailed',
  'KM-OUTPUT-ROOT-BUSY': 'outputSafety.diagnostic.rootBusy',
  'KM-OUTPUT-STARTUP-RECOVERY': 'outputSafety.diagnostic.startupRecovery',
  'KM-OUTPUT-SUPPORT-REPORT-REDACTED': 'outputSafety.diagnostic.supportRedacted',
  'KM-OUTPUT-UNKNOWN-TARGET-STATE': 'outputSafety.diagnostic.unknownTarget',
  'KM-OUTPUT-UNSAFE-PATH': 'outputSafety.diagnostic.unsafePath',
  'KM-PROJECT-OUTPUT-MISSING': 'outputSafety.diagnostic.outputRootMissing',
  'KM-PROJECT-OUTPUT-NOT-CONFIGURED': 'outputSafety.diagnostic.outputRootNotConfigured',
  'KM-PROJECT-RELOCATION-CONFLICT': 'outputSafety.diagnostic.relocationConflict',
  'KM-PROJECT-RELOCATION-MISMATCH': 'outputSafety.diagnostic.relocationMismatch',
  'KM-PROJECT-RELOCATION-REVIEWED': 'outputSafety.diagnostic.relocationReviewed'
};

export function formatDiagnosticMessage(
  diagnostic: ApiDiagnostic,
  translateLiteral: DiagnosticTranslator = identityTranslator,
  translateKey?: DiagnosticKeyTranslator
) {
  const localizedCodeMessage = localizedDiagnosticCodeMessage(diagnostic, translateKey);
  const message = formatDiagnosticSummary(diagnostic, translateLiteral, translateKey);

  // Stable output and relocation codes intentionally replace raw backend prose in normal UI.
  // The code itself remains visible in Diagnostics for technical support.
  if (localizedCodeMessage !== null) {
    return message;
  }

  if (diagnostic.severity === 'info') {
    return message;
  }

  const valueDetails = [
    formatLabeledDetail('File', diagnostic.file, translateLiteral),
    formatLabeledDetail('Field', formatFieldName(diagnostic.field), translateLiteral),
    formatLabeledDetail('Expected', diagnostic.expected, translateLiteral)
  ].filter((detail): detail is string => detail !== null);

  if (valueDetails.length === 0) {
    return message;
  }

  const details = [
    formatDomainDetail(diagnostic.domain, translateLiteral),
    ...valueDetails
  ].filter((detail): detail is string => detail !== null);

  return `${message} ${details.join(' ')}`;
}

export function formatDiagnosticSummary(
  diagnostic: ApiDiagnostic,
  translateLiteral: DiagnosticTranslator = identityTranslator,
  translateKey?: DiagnosticKeyTranslator
) {
  return normalizeSentence(
    localizedDiagnosticCodeMessage(diagnostic, translateKey) ??
      translateLiteral(diagnostic.message)
  );
}

function localizedDiagnosticCodeMessage(
  diagnostic: ApiDiagnostic,
  translateKey?: DiagnosticKeyTranslator
) {
  return translateKey && diagnostic.code
    ? translateDiagnosticCode(diagnostic.code, translateKey)
    : null;
}

function translateDiagnosticCode(code: string, translateKey: DiagnosticKeyTranslator) {
  const exactKey = diagnosticLocalizationKeys[code];
  if (exactKey) {
    return translateKey(exactKey);
  }
  if (code.startsWith('KM-OUTPUT-')) {
    return translateKey('outputSafety.diagnostic.genericOutput');
  }
  if (code.startsWith('KM-PROJECT-RELOCATION-')) {
    return translateKey('outputSafety.diagnostic.genericRelocation');
  }
  if (code.startsWith('KM-BRIDGE-') || code.startsWith('KM-DESKTOP-')) {
    return translateKey('outputSafety.diagnostic.requestFailed');
  }
  return null;
}

function formatDomainDetail(
  domain: string | null | undefined,
  translateLiteral: DiagnosticTranslator
) {
  if (!domain) {
    return null;
  }

  const readableDomain = formatDomainName(domain);

  return readableDomain
    ? `${translateLiteral('Area')}: ${translateLiteral(readableDomain)}.`
    : null;
}

function formatLabeledDetail(
  label: string,
  value: string | null | undefined,
  translateLiteral: DiagnosticTranslator
) {
  if (!value) {
    return null;
  }

  const trimmed = value.trim();

  return trimmed.length > 0
    ? `${translateLiteral(label)}: ${normalizeSentence(
        translateDiagnosticDetail(label, trimmed, translateLiteral)
      )}`
    : null;
}

function translateDiagnosticDetail(
  label: string,
  value: string,
  translateLiteral: DiagnosticTranslator
) {
  if (label === 'Field' || label === 'Expected') {
    return translateLiteral(value);
  }

  return value;
}

function formatDomainName(domain: string) {
  const trimmed = domain.trim();

  if (trimmed.length === 0) {
    return '';
  }

  const withoutPrefix = trimmed
    .replace(/^workflow[._-]/i, '')
    .replace(/^project[._-]/i, 'project ')
    .replace(/^desktop[._-]/i, 'desktop ')
    .replace(/^bridge[._-]/i, 'bridge ');

  return humanizeIdentifier(withoutPrefix);
}

function formatFieldName(field: string | null | undefined) {
  if (!field) {
    return null;
  }

  const trimmed = field.trim();

  if (trimmed.length === 0) {
    return null;
  }

  return humanizeIdentifier(trimmed);
}

function humanizeIdentifier(value: string) {
  const spaced = value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[._/-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();

  if (spaced.length === 0) {
    return value;
  }

  return spaced
    .split(' ')
    .map((part) => {
      if (/^[A-Z0-9]+$/.test(part)) {
        return part;
      }

      return part.charAt(0).toUpperCase() + part.slice(1);
    })
    .join(' ');
}

function normalizeSentence(value: string) {
  const trimmed = value.trim();

  if (trimmed.length === 0 || /[.!?]$/.test(trimmed)) {
    return trimmed;
  }

  return `${trimmed}.`;
}
