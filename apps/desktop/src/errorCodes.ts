/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from "zod";

// Semantic codes are stable machine identifiers. Generated incident fingerprints live in
// errorReporting.ts and intentionally use the separate KM-INCIDENT-* namespace.
export type KmErrorCode = `KM-${Uppercase<string>}`;

export const kmErrorCodePattern = /^KM(?:-[A-Z0-9]+)+$/;

export const kmErrorCodeSchema = z
  .string()
  .regex(
    kmErrorCodePattern,
    "Error codes must use the uppercase KM-prefixed format.",
  );

export const projectBridgeErrorCodes = {
  accessDenied: "KM-BRIDGE-ACCESS-DENIED",
  dataInvalid: "KM-BRIDGE-DATA-INVALID",
  dataLayoutInvalid: "KM-BRIDGE-DATA-LAYOUT-INVALID",
  dataSupportUnavailable: "KM-BRIDGE-SUPPORT-RUNTIME-UNAVAILABLE",
  emptyRequest: "KM-BRIDGE-EMPTY-REQUEST",
  gameMismatch: "KM-BRIDGE-GAME-MISMATCH",
  internalFailure: "KM-BRIDGE-INTERNAL-FAILURE",
  invalidJson: "KM-BRIDGE-INVALID-JSON",
  invalidResponseContract: "KM-BRIDGE-RESPONSE-CONTRACT-INVALID",
  invalidResponseJson: "KM-BRIDGE-RESPONSE-JSON-INVALID",
  ioFailed: "KM-BRIDGE-IO-FAILED",
  missingCommand: "KM-BRIDGE-MISSING-COMMAND",
  missingPayload: "KM-BRIDGE-RESPONSE-PAYLOAD-MISSING",
  missingRequestId: "KM-BRIDGE-RESPONSE-REQUEST-ID-MISSING",
  outputCheckpointConflict: "KM-OUTPUT-CHECKPOINT-CONFLICT",
  outputCheckpointNotFound: "KM-OUTPUT-CHECKPOINT-NOT-FOUND",
  outputConcurrentModification: "KM-OUTPUT-CONCURRENT-MODIFICATION",
  outputLimitExceeded: "KM-OUTPUT-LIMIT-EXCEEDED",
  outputOwnershipUnproven: "KM-OUTPUT-OWNERSHIP-UNPROVEN",
  outputRecoveryRequired: "KM-OUTPUT-RECOVERY-REQUIRED",
  outputRootBusy: "KM-OUTPUT-ROOT-BUSY",
  outputUnsafePath: "KM-OUTPUT-UNSAFE-PATH",
  projectOutputMigrationBlocked: "KM-PROJECT-OUTPUT-MIGRATION-BLOCKED",
  projectOutputMissing: "KM-PROJECT-OUTPUT-MISSING",
  projectOutputNotConfigured: "KM-PROJECT-OUTPUT-NOT-CONFIGURED",
  projectRelocationConflict: "KM-PROJECT-RELOCATION-CONFLICT",
  projectRelocationMismatch: "KM-PROJECT-RELOCATION-MISMATCH",
  requestTooLarge: "KM-BRIDGE-REQUEST-TOO-LARGE",
  responseTooLarge: "KM-BRIDGE-RESPONSE-TOO-LARGE",
  requestIdMismatch: "KM-BRIDGE-RESPONSE-REQUEST-ID-MISMATCH",
  resourceMissing: "KM-BRIDGE-RESOURCE-MISSING",
  transportFailed: "KM-BRIDGE-TRANSPORT-FAILED",
  unsupportedCommand: "KM-BRIDGE-UNSUPPORTED-COMMAND",
  unexpected: "KM-BRIDGE-UNEXPECTED",
  workspaceConcurrentModification: "KM-WORKSPACE-CONCURRENT-MODIFICATION",
} as const satisfies Record<string, KmErrorCode>;

export const semanticExploreErrorCodes = {
  externalOverlayRejected: "KM-SEMANTIC-EXTERNAL-OVERLAY-REJECTED",
  externalSnapshotUnavailable: "KM-SEMANTIC-EXTERNAL-SNAPSHOT-UNAVAILABLE",
  invalidCursor: "KM-SEMANTIC-INVALID-CURSOR",
  invalidQuery: "KM-SEMANTIC-INVALID-QUERY",
  limitExceeded: "KM-SEMANTIC-LIMIT-EXCEEDED",
  staleRevision: "KM-SEMANTIC-STALE-REVISION",
  unsupported: "KM-SEMANTIC-UNSUPPORTED",
} as const satisfies Record<string, KmErrorCode>;

export const guidedDesignErrorCodes = {
  staleProposal: "KM-GUIDED-DESIGN-STALE-PROPOSAL",
} as const satisfies Record<string, KmErrorCode>;

export const semanticMergeErrorCodes = {
  staleProposal: "KM-SEMANTIC-MERGE-STALE-PROPOSAL",
} as const satisfies Record<string, KmErrorCode>;

export const researchLabErrorCodes = {
  comparisonStale: "KM-RESEARCH-COMPARISON-STALE",
  sourceExpired: "KM-RESEARCH-SOURCE-EXPIRED",
  sourceRejected: "KM-RESEARCH-SOURCE-REJECTED",
} as const satisfies Record<string, KmErrorCode>;

export const kmRecipeErrorCodes = {
  staleProposal: "KM-RECIPE-STALE-PROPOSAL",
} as const satisfies Record<string, KmErrorCode>;

export const swshDynamaxAdventuresErrorCodes = {
  ioFailed: "KM-SWSH-DYNAMAX-ADVENTURES-IO-FAILED",
  layoutUnsupported: "KM-SWSH-DYNAMAX-ADVENTURES-LAYOUT-UNSUPPORTED",
  projectUnsupported: "KM-SWSH-DYNAMAX-ADVENTURES-PROJECT-UNSUPPORTED",
  recoveryRequired: "KM-SWSH-DYNAMAX-ADVENTURES-RECOVERY-REQUIRED",
  savePreimageStale: "KM-SWSH-DYNAMAX-ADVENTURES-SAVE-PREIMAGE-STALE",
  seedBoundsInvalid: "KM-SWSH-DYNAMAX-ADVENTURES-SEED-BOUNDS-INVALID",
  seedInvalid: "KM-SWSH-DYNAMAX-ADVENTURES-SEED-INVALID",
  seedLimitInvalid: "KM-SWSH-DYNAMAX-ADVENTURES-SEED-LIMIT-INVALID",
  sourceUnavailable: "KM-SWSH-DYNAMAX-ADVENTURES-SOURCE-UNAVAILABLE",
  sourceUnsupported: "KM-SWSH-DYNAMAX-ADVENTURES-SOURCE-UNSUPPORTED",
  startSeedInvalid: "KM-SWSH-DYNAMAX-ADVENTURES-START-SEED-INVALID",
  verificationFailed: "KM-SWSH-DYNAMAX-ADVENTURES-VERIFICATION-FAILED",
} as const satisfies Record<string, KmErrorCode>;

export const gameplaySettingsErrorCodes = {
  reviewExpired: "KM-GAMEPLAY-SETTINGS-REVIEW-EXPIRED",
  stateStale: "KM-GAMEPLAY-SETTINGS-STATE-STALE",
  unavailable: "KM-GAMEPLAY-SETTINGS-UNAVAILABLE",
} as const satisfies Record<string, KmErrorCode>;

export const inGameSettingsPackageErrorCodes = {
  reviewExpired: "KM-IN-GAME-SETTINGS-PACKAGE-REVIEW-EXPIRED",
  stateStale: "KM-IN-GAME-SETTINGS-PACKAGE-STATE-STALE",
  unavailable: "KM-IN-GAME-SETTINGS-PACKAGE-UNAVAILABLE",
} as const satisfies Record<string, KmErrorCode>;

export const swshBattleCafeRewardsDiagnosticCodes = {
  applied: "KM-SWSH-BATTLE-CAFE-APPLIED",
  draftStaged: "KM-SWSH-BATTLE-CAFE-DRAFT-STAGED",
  itemCatalogUnavailable: "KM-SWSH-BATTLE-CAFE-ITEM-CATALOG-UNAVAILABLE",
  noChanges: "KM-SWSH-BATTLE-CAFE-NO-CHANGES",
  outputPreparationFailed: "KM-SWSH-BATTLE-CAFE-OUTPUT-PREPARATION-FAILED",
  outputWriteFailed: "KM-SWSH-BATTLE-CAFE-OUTPUT-WRITE-FAILED",
  projectUnsupported: "KM-SWSH-BATTLE-CAFE-PROJECT-UNSUPPORTED",
  reviewedPlanStale: "KM-SWSH-BATTLE-CAFE-REVIEWED-PLAN-STALE",
  rowInvalid: "KM-SWSH-BATTLE-CAFE-ROW-INVALID",
  sessionInvalid: "KM-SWSH-BATTLE-CAFE-SESSION-INVALID",
  sourceUnavailable: "KM-SWSH-BATTLE-CAFE-SOURCE-UNAVAILABLE",
  sourceUnsupported: "KM-SWSH-BATTLE-CAFE-SOURCE-UNSUPPORTED",
  targetResolutionFailed: "KM-SWSH-BATTLE-CAFE-TARGET-RESOLUTION-FAILED",
  totalsInvalid: "KM-SWSH-BATTLE-CAFE-TOTALS-INVALID",
} as const satisfies Record<string, KmErrorCode>;

export const rowClipboardDiagnosticCodes = {
  adapterUnsupported: "KM-ROW-CLIPBOARD-ADAPTER-UNSUPPORTED",
  batchRejected: "KM-ROW-CLIPBOARD-BATCH-REJECTED",
  envelopeInvalid: "KM-ROW-CLIPBOARD-ENVELOPE-INVALID",
  modeUnavailable: "KM-ROW-CLIPBOARD-MODE-UNAVAILABLE",
  operationLimit: "KM-ROW-CLIPBOARD-OPERATION-LIMIT",
  previewMismatch: "KM-ROW-CLIPBOARD-PREVIEW-MISMATCH",
  previewRequired: "KM-ROW-CLIPBOARD-PREVIEW-REQUIRED",
  scopeMismatch: "KM-ROW-CLIPBOARD-SCOPE-MISMATCH",
  sourceStale: "KM-ROW-CLIPBOARD-SOURCE-STALE",
  targetInvalid: "KM-ROW-CLIPBOARD-TARGET-INVALID",
  targetStale: "KM-ROW-CLIPBOARD-TARGET-STALE",
} as const satisfies Record<string, KmErrorCode>;

export const swshPlacementErrorCodes = {
  catalogStale: "KM-SWSH-PLACEMENT-CATALOG-STALE",
} as const satisfies Record<string, KmErrorCode>;

export const zaFashionCatalogDiagnosticCodes = {
  editSafety: "KM-ZA-FASHION-CATALOG-EDIT-SAFETY",
  reviewedState: "KM-ZA-FASHION-CATALOG-REVIEWED-STATE",
  safety: "KM-ZA-FASHION-CATALOG-SAFETY",
} as const satisfies Record<string, KmErrorCode>;

export const zaTrainerPoolsDiagnosticCodes = {
  applyFailed: "KM-ZA-TRAINER-POOLS-APPLY-FAILED",
  editSafety: "KM-ZA-TRAINER-POOLS-EDIT-SAFETY",
  poolsIncompatible: "KM-ZA-TRAINER-POOLS-INCOMPATIBLE",
  planStale: "KM-ZA-TRAINER-POOLS-PLAN-STALE",
  reviewedState: "KM-ZA-TRAINER-POOLS-REVIEWED-STATE",
  safety: "KM-ZA-TRAINER-POOLS-SAFETY",
  selectionInvalid: "KM-ZA-TRAINER-POOLS-SELECTION-INVALID",
  sessionConflict: "KM-ZA-TRAINER-POOLS-SESSION-CONFLICT",
  sourceChanged: "KM-ZA-TRAINER-POOLS-SOURCE-CHANGED",
  swapAlreadyStaged: "KM-ZA-TRAINER-POOLS-SWAP-ALREADY-STAGED",
  unsupportedMirrorShape: "KM-ZA-TRAINER-POOLS-MIRROR-SHAPE-UNSUPPORTED",
  verificationFailed: "KM-ZA-TRAINER-POOLS-VERIFICATION-FAILED",
} as const satisfies Record<string, KmErrorCode>;

export const zaTrainerIdentityDiagnosticCodes = {
  classPairUnchanged: "KM-ZA-TRAINER-IDENTITY-CLASS-PAIR-UNCHANGED",
  classPairUnverified: "KM-ZA-TRAINER-IDENTITY-CLASS-PAIR-UNVERIFIED",
  pendingEditInvalid: "KM-ZA-TRAINER-IDENTITY-PENDING-EDIT-INVALID",
  planStale: "KM-ZA-TRAINER-IDENTITY-PLAN-STALE",
  reassignmentBlocked: "KM-ZA-TRAINER-IDENTITY-REASSIGNMENT-BLOCKED",
} as const satisfies Record<string, KmErrorCode>;

export const diagnosticErrorCodes = {
  swshDynamaxAdventuresHiddenRowChanged:
    "KM-SWSH-DYNAMAX-ADVENTURES-HIDDEN-ROW-CHANGED",
  swshDynamaxAdventuresRowApiDomainInvalid:
    "KM-SWSH-DYNAMAX-ADVENTURES-ROW-API-DOMAIN-INVALID",
  swshDynamaxAdventuresRowFormUnresolved:
    "KM-SWSH-DYNAMAX-ADVENTURES-ROW-FORM-UNRESOLVED",
  swshDynamaxAdventuresTableLayoutMismatch:
    "KM-SWSH-DYNAMAX-ADVENTURES-TABLE-LAYOUT-MISMATCH",
  swshRoyalCandyPreflightBlocked: "KM-SWSH-ROYAL-CANDY-PREFLIGHT-BLOCKED",
  zaItemsTmLegacyNumbering: "KM-ZA-ITEMS-TM-LEGACY-NUMBERING",
  zaItemsTmLegacyPickupLayout: "KM-ZA-ITEMS-TM-LEGACY-PICKUP-LAYOUT",
} as const satisfies Record<string, KmErrorCode>;

export const desktopErrorCodes = {
  appExitFailed: "KM-DESKTOP-APP-EXIT-FAILED",
  appRelaunchFailed: "KM-DESKTOP-APP-RELAUNCH-FAILED",
  bridgeRecycleFailed: "KM-DESKTOP-BRIDGE-RECYCLE-FAILED",
  closeRequestListenerFailed: "KM-DESKTOP-CLOSE-REQUEST-LISTENER-FAILED",
  closeGuardUpdateFailed: "KM-DESKTOP-CLOSE-GUARD-UPDATE-FAILED",
  directoryCreateFailed: "KM-DESKTOP-DIRECTORY-CREATE-FAILED",
  externalUrlOpenFailed: "KM-DESKTOP-EXTERNAL-URL-OPEN-FAILED",
  filePickerFailed: "KM-DESKTOP-FILE-PICKER-FAILED",
  folderPickerFailed: "KM-DESKTOP-FOLDER-PICKER-FAILED",
  pathOpenFailed: "KM-DESKTOP-PATH-OPEN-FAILED",
  pathPickerFailed: "KM-DESKTOP-PATH-PICKER-FAILED",
  runtimeUnavailable: "KM-DESKTOP-RUNTIME-UNAVAILABLE",
  supportFileSearchCancelFailed: "KM-DESKTOP-SUPPORT-FILE-SEARCH-CANCEL-FAILED",
  supportFileSearchCanceled: "KM-DESKTOP-SUPPORT-FILE-SEARCH-CANCELED",
  supportFileSearchFailed: "KM-DESKTOP-SUPPORT-FILE-SEARCH-FAILED",
  unexpected: "KM-DESKTOP-UNEXPECTED",
  updateCheckFailed: "KM-DESKTOP-UPDATE-CHECK-FAILED",
  updateCloseFailed: "KM-DESKTOP-UPDATE-CLOSE-FAILED",
  updateInstallFailed: "KM-DESKTOP-UPDATE-INSTALL-FAILED",
} as const satisfies Record<string, KmErrorCode>;

export const uiErrorCodes = {
  renderUnexpected: "KM-UI-RENDER-UNEXPECTED",
  unhandled: "KM-UI-UNHANDLED",
  unhandledPromise: "KM-UI-PROMISE-UNHANDLED",
} as const satisfies Record<string, KmErrorCode>;

export function isKmErrorCode(value: unknown): value is KmErrorCode {
  return typeof value === "string" && kmErrorCodePattern.test(value);
}
