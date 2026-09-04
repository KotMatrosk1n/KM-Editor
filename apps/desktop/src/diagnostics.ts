/* SPDX-License-Identifier: GPL-3.0-only */

import type { ApiDiagnostic } from './bridge/contracts';

type DiagnosticTranslator = (literal: string) => string;
type DiagnosticKeyTranslator = (key: string) => string;

const identityTranslator: DiagnosticTranslator = (literal) => literal;

const reviewDiagnosticLocalizationKeys: Readonly<Record<string, string>> = {
  'KM-BRIDGE-ACCESS-DENIED': 'diagnostics.review.accessDenied',
  'KM-BRIDGE-RESOURCE-MISSING': 'diagnostics.review.resourceMissing',
  'KM-BRIDGE-DATA-INVALID': 'diagnostics.review.dataInvalid',
  'KM-BRIDGE-DATA-LAYOUT-INVALID': 'diagnostics.review.dataLayoutInvalid',
  'KM-BRIDGE-SUPPORT-RUNTIME-UNAVAILABLE': 'diagnostics.review.runtimeUnavailable',
  'KM-BRIDGE-IO-FAILED': 'diagnostics.review.ioFailed',
  'KM-BRIDGE-INTERNAL-FAILURE': 'diagnostics.review.internalFailure'
};

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
  'KM-GAMEPLAY-SETTINGS-REVIEW-EXPIRED': 'gameplaySettings.diagnostic.reviewExpired',
  'KM-GAMEPLAY-SETTINGS-STATE-STALE': 'gameplaySettings.diagnostic.stateStale',
  'KM-GAMEPLAY-SETTINGS-UNAVAILABLE': 'gameplaySettings.diagnostic.unavailable',
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
  'KM-PROJECT-RELOCATION-REVIEWED': 'outputSafety.diagnostic.relocationReviewed',
  'KM-SWSH-DYNAMAX-ADVENTURES-IO-FAILED': 'routePlanner.diagnostic.ioFailed',
  'KM-SWSH-DYNAMAX-ADVENTURES-LAYOUT-UNSUPPORTED': 'routePlanner.diagnostic.layoutUnsupported',
  'KM-SWSH-DYNAMAX-ADVENTURES-PROJECT-UNSUPPORTED': 'routePlanner.diagnostic.projectUnsupported',
  'KM-SWSH-DYNAMAX-ADVENTURES-RECOVERY-REQUIRED': 'routePlanner.diagnostic.recoveryRequired',
  'KM-SWSH-DYNAMAX-ADVENTURES-SAVE-PREIMAGE-STALE': 'routePlanner.diagnostic.savePreimageStale',
  'KM-SWSH-DYNAMAX-ADVENTURES-SEED-BOUNDS-INVALID': 'routePlanner.diagnostic.seedBoundsInvalid',
  'KM-SWSH-DYNAMAX-ADVENTURES-SEED-INVALID': 'routePlanner.diagnostic.seedBoundsInvalid',
  'KM-SWSH-DYNAMAX-ADVENTURES-SEED-LIMIT-INVALID': 'routePlanner.diagnostic.seedBoundsInvalid',
  'KM-SWSH-DYNAMAX-ADVENTURES-SOURCE-UNAVAILABLE': 'routePlanner.diagnostic.sourceUnavailable',
  'KM-SWSH-DYNAMAX-ADVENTURES-SOURCE-UNSUPPORTED': 'routePlanner.diagnostic.sourceUnsupported',
  'KM-SWSH-DYNAMAX-ADVENTURES-START-SEED-INVALID': 'routePlanner.diagnostic.seedBoundsInvalid',
  'KM-SWSH-DYNAMAX-ADVENTURES-VERIFICATION-FAILED': 'routePlanner.diagnostic.verificationFailed',
  'KM-SWSH-FPS-COMPONENT-INPUT-INVALID': 'fpsPatch.diagnostic.componentInputInvalid',
  'KM-SWSH-FPS-COMPONENT-INPUT-READ-FAILED': 'fpsPatch.diagnostic.componentInputReadFailed',
  'KM-SWSH-FPS-COMPONENT-INPUT-UNAVAILABLE': 'fpsPatch.diagnostic.componentInputUnavailable',
  'KM-SWSH-FPS-MAIN-INPUT-UNAVAILABLE': 'fpsPatch.diagnostic.mainInputUnavailable',
  'KM-SWSH-FPS-MAIN-RESTORE-BLOCKED': 'fpsPatch.diagnostic.mainRestoreBlocked',
  'KM-SWSH-FPS-MANIFEST-INVALID': 'fpsPatch.diagnostic.manifestInvalid',
  'KM-SWSH-FPS-OUTPUT-ROOT-UNAVAILABLE': 'fpsPatch.diagnostic.outputRootUnavailable',
  'KM-SWSH-FPS-OWNED-OUTPUT-CHANGED': 'fpsPatch.diagnostic.ownedOutputChanged',
  'KM-SWSH-FPS-PROJECT-UNAVAILABLE': 'fpsPatch.diagnostic.projectUnavailable',
  'KM-SWSH-FPS-RESTORE-PREFLIGHT-BLOCKED': 'fpsPatch.diagnostic.restorePreflightBlocked',
  'KM-SWSH-BATTLE-CAFE-APPLIED': 'battleCafeRewards.diagnostic.applied',
  'KM-SWSH-BATTLE-CAFE-DRAFT-STAGED': 'battleCafeRewards.diagnostic.draftStaged',
  'KM-SWSH-BATTLE-CAFE-ITEM-CATALOG-UNAVAILABLE': 'battleCafeRewards.diagnostic.itemCatalogUnavailable',
  'KM-SWSH-BATTLE-CAFE-NO-CHANGES': 'battleCafeRewards.diagnostic.noChanges',
  'KM-SWSH-BATTLE-CAFE-OUTPUT-PREPARATION-FAILED': 'battleCafeRewards.diagnostic.outputPreparationFailed',
  'KM-SWSH-BATTLE-CAFE-OUTPUT-WRITE-FAILED': 'battleCafeRewards.diagnostic.outputWriteFailed',
  'KM-SWSH-BATTLE-CAFE-PROJECT-UNSUPPORTED': 'battleCafeRewards.diagnostic.projectUnsupported',
  'KM-SWSH-BATTLE-CAFE-REVIEWED-PLAN-STALE': 'battleCafeRewards.diagnostic.reviewedPlanStale',
  'KM-SWSH-BATTLE-CAFE-ROW-INVALID': 'battleCafeRewards.diagnostic.rowInvalid',
  'KM-SWSH-BATTLE-CAFE-SESSION-INVALID': 'battleCafeRewards.diagnostic.sessionInvalid',
  'KM-SWSH-BATTLE-CAFE-SOURCE-UNAVAILABLE': 'battleCafeRewards.diagnostic.sourceUnavailable',
  'KM-SWSH-BATTLE-CAFE-SOURCE-UNSUPPORTED': 'battleCafeRewards.diagnostic.sourceUnsupported',
  'KM-SWSH-BATTLE-CAFE-TARGET-RESOLUTION-FAILED': 'battleCafeRewards.diagnostic.targetResolutionFailed',
  'KM-SWSH-BATTLE-CAFE-TOTALS-INVALID': 'battleCafeRewards.diagnostic.totalsInvalid',
  'KM-ZA-FASHION-CATALOG-EDIT-SAFETY': 'fashionCatalog.diagnostics.editSafety',
  'KM-ZA-FASHION-CATALOG-REVIEWED-STATE': 'fashionCatalog.diagnostics.reviewedState',
  'KM-ZA-FASHION-CATALOG-SAFETY': 'fashionCatalog.diagnostics.safety',
  'KM-ZA-TRAINER-POOLS-APPLY-FAILED': 'trainerPools.diagnostic.applyFailed',
  'KM-ZA-TRAINER-POOLS-EDIT-SAFETY': 'trainerPools.diagnostic.editSafety',
  'KM-ZA-TRAINER-POOLS-INCOMPATIBLE': 'trainerPools.diagnostic.poolsIncompatible',
  'KM-ZA-TRAINER-POOLS-MIRROR-SHAPE-UNSUPPORTED':
    'trainerPools.diagnostic.unsupportedMirrorShape',
  'KM-ZA-TRAINER-POOLS-PLAN-STALE': 'trainerPools.diagnostic.planStale',
  'KM-ZA-TRAINER-POOLS-REVIEWED-STATE': 'trainerPools.diagnostic.reviewedState',
  'KM-ZA-TRAINER-POOLS-SAFETY': 'trainerPools.diagnostic.safety',
  'KM-ZA-TRAINER-POOLS-SELECTION-INVALID': 'trainerPools.diagnostic.selectionInvalid',
  'KM-ZA-TRAINER-POOLS-SESSION-CONFLICT': 'trainerPools.diagnostic.sessionConflict',
  'KM-ZA-TRAINER-POOLS-SOURCE-CHANGED': 'trainerPools.diagnostic.sourceChanged',
  'KM-ZA-TRAINER-POOLS-SWAP-ALREADY-STAGED':
    'trainerPools.diagnostic.swapAlreadyStaged',
  'KM-ZA-TRAINER-POOLS-VERIFICATION-FAILED':
    'trainerPools.diagnostic.verificationFailed',
  'KM-SV-HABITAT-PROJECT-UNSUPPORTED': 'habitatCoordinates.diagnostic.projectUnsupported',
  'KM-SV-HABITAT-BUILD-UNSUPPORTED': 'habitatCoordinates.diagnostic.buildUnsupported',
  'KM-SV-HABITAT-REGION-SOURCE-UNAVAILABLE': 'habitatCoordinates.diagnostic.regionSourceUnavailable',
  'KM-SV-HABITAT-REGION-SOURCE-UNSUPPORTED': 'habitatCoordinates.diagnostic.regionSourceUnsupported',
  'KM-SV-HABITAT-QUERY-INVALID': 'habitatCoordinates.diagnostic.queryInvalid',
  'KM-SV-HABITAT-EDIT-SESSION-INVALID': 'habitatCoordinates.diagnostic.editSessionInvalid',
  'KM-SV-HABITAT-ROW-BINDING-STALE': 'habitatCoordinates.diagnostic.rowBindingStale',
  'KM-SV-HABITAT-COORDINATE-UNOBSERVED': 'habitatCoordinates.diagnostic.coordinateUnobserved',
  'KM-SV-HABITAT-REVIEWED-PLAN-STALE': 'habitatCoordinates.diagnostic.reviewedPlanStale',
  'KM-SV-HABITAT-TARGET-RESOLUTION-FAILED': 'habitatCoordinates.diagnostic.targetResolutionFailed',
  'KM-SV-HABITAT-OUTPUT-PREPARATION-FAILED': 'habitatCoordinates.diagnostic.outputPreparationFailed',
  'KM-SV-SOURCE-COMPARISON-DUAL-LOOSE-DIVERGENT': 'gameModules.diagnostic.svSourceComparisonDualLooseDivergent',
  'KM-SV-HABITAT-OUTPUT-PREIMAGE-CAPTURE-FAILED': 'habitatCoordinates.diagnostic.outputPreimageCaptureFailed',
  'KM-SV-HABITAT-OUTPUT-COMMIT-FAILED': 'habitatCoordinates.diagnostic.outputCommitFailed',
  'KM-SV-HABITAT-OUTPUT-VERIFICATION-FAILED': 'habitatCoordinates.diagnostic.outputVerificationFailed',
  'KM-SV-HABITAT-OUTPUT-ROLLBACK-RESTORED': 'habitatCoordinates.diagnostic.outputRollbackRestored',
  'KM-SV-HABITAT-OUTPUT-ROLLBACK-FAILED': 'habitatCoordinates.diagnostic.outputRollbackFailed'
};

export function formatDiagnosticMessage(
  diagnostic: ApiDiagnostic,
  translateLiteral: DiagnosticTranslator = identityTranslator,
  translateKey?: DiagnosticKeyTranslator
) {
  const localizedCodeMessage = localizedDiagnosticCodeMessage(diagnostic, translateKey);
  const message = formatDiagnosticSummary(diagnostic, translateLiteral, translateKey);

  if (isChangePlanReviewDiagnostic(diagnostic)) {
    return message;
  }

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
  const message = normalizeSentence(
    localizedDiagnosticCodeMessage(diagnostic, translateKey) ??
      translateLiteral(diagnostic.message)
  );
  if (!isChangePlanReviewDiagnostic(diagnostic)) {
    return message;
  }

  // Review failures need their safe resource and corrective context in the
  // visible summary; grouping and a collapsed technical panel must not hide it.
  const details = [
    formatDomainDetail(diagnostic.domain, translateLiteral),
    formatLabeledDetail('File', diagnostic.file, translateLiteral),
    formatReviewExpectedDetail(diagnostic, translateLiteral)
  ].filter((detail): detail is string => detail !== null);
  return details.length === 0 ? message : `${message} ${details.join(' ')}`;
}

function formatReviewExpectedDetail(
  diagnostic: ApiDiagnostic,
  translateLiteral: DiagnosticTranslator
) {
  const expected = diagnostic.expected?.trim();
  if (!expected) return null;

  // The safe file classifier composes recovery, operation and selected-copy
  // sentences. Translate those stable literals without losing their context.
  const translated = diagnostic.field === 'changePlanSourceFingerprint'
    ? translateLiteral(expected)
    : expected.split(/(?<=\.)\s+/u).map(translateLiteral).join(' ');
  return `${translateLiteral('Expected')}: ${normalizeSentence(translated)}`;
}

function localizedDiagnosticCodeMessage(
  diagnostic: ApiDiagnostic,
  translateKey?: DiagnosticKeyTranslator
) {
  if (translateKey && isChangePlanReviewDiagnostic(diagnostic)) {
    const key = diagnostic.field === 'changePlanSourceFingerprint'
      ? 'diagnostics.review.sourceFingerprintInvalid'
      : reviewDiagnosticLocalizationKeys[diagnostic.code!];
    return translateKey(key!);
  }
  return translateKey && diagnostic.code
    ? translateDiagnosticCode(diagnostic.code, translateKey)
    : null;
}

function isChangePlanReviewDiagnostic(diagnostic: ApiDiagnostic) {
  return (diagnostic.field === 'changePlanReview' ||
    (diagnostic.field === 'changePlanSourceFingerprint' &&
      diagnostic.code === 'KM-BRIDGE-DATA-INVALID')) &&
    diagnostic.code !== null && diagnostic.code !== undefined &&
    Object.hasOwn(reviewDiagnosticLocalizationKeys, diagnostic.code);
}

function translateDiagnosticCode(code: string, translateKey: DiagnosticKeyTranslator) {
  const key = getDiagnosticLocalizationKey(code);
  return key ? translateKey(key) : null;
}

export function getDiagnosticLocalizationKey(code: string) {
  const exactKey = diagnosticLocalizationKeys[code];
  if (exactKey) return exactKey;
  if (code.startsWith('KM-OUTPUT-')) {
    return 'outputSafety.diagnostic.genericOutput';
  }
  if (code.startsWith('KM-PROJECT-RELOCATION-')) {
    return 'outputSafety.diagnostic.genericRelocation';
  }
  if (code.startsWith('KM-BRIDGE-') || code.startsWith('KM-DESKTOP-')) {
    return 'outputSafety.diagnostic.requestFailed';
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
