/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';

// Semantic codes are stable machine identifiers. Generated incident fingerprints live in
// errorReporting.ts and intentionally use the separate KM-INCIDENT-* namespace.
export type KmErrorCode = `KM-${Uppercase<string>}`;

export const kmErrorCodePattern = /^KM(?:-[A-Z0-9]+)+$/;

export const kmErrorCodeSchema = z
  .string()
  .regex(kmErrorCodePattern, 'Error codes must use the uppercase KM-prefixed format.');

export const projectBridgeErrorCodes = {
  emptyRequest: 'KM-BRIDGE-EMPTY-REQUEST',
  gameMismatch: 'KM-BRIDGE-GAME-MISMATCH',
  invalidJson: 'KM-BRIDGE-INVALID-JSON',
  invalidResponseContract: 'KM-BRIDGE-RESPONSE-CONTRACT-INVALID',
  invalidResponseJson: 'KM-BRIDGE-RESPONSE-JSON-INVALID',
  missingCommand: 'KM-BRIDGE-MISSING-COMMAND',
  missingPayload: 'KM-BRIDGE-RESPONSE-PAYLOAD-MISSING',
  missingRequestId: 'KM-BRIDGE-RESPONSE-REQUEST-ID-MISSING',
  requestIdMismatch: 'KM-BRIDGE-RESPONSE-REQUEST-ID-MISMATCH',
  transportFailed: 'KM-BRIDGE-TRANSPORT-FAILED',
  unsupportedCommand: 'KM-BRIDGE-UNSUPPORTED-COMMAND',
  unexpected: 'KM-BRIDGE-UNEXPECTED'
} as const satisfies Record<string, KmErrorCode>;

export const swshDynamaxAdventuresErrorCodes = {
  seedInvalid: 'KM-SWSH-DYNAMAX-ADVENTURES-SEED-INVALID',
  seedLimitInvalid: 'KM-SWSH-DYNAMAX-ADVENTURES-SEED-LIMIT-INVALID',
  startSeedInvalid: 'KM-SWSH-DYNAMAX-ADVENTURES-START-SEED-INVALID'
} as const satisfies Record<string, KmErrorCode>;

export const diagnosticErrorCodes = {
  swshDynamaxAdventuresHiddenRowChanged:
    'KM-SWSH-DYNAMAX-ADVENTURES-HIDDEN-ROW-CHANGED',
  swshDynamaxAdventuresRowApiDomainInvalid:
    'KM-SWSH-DYNAMAX-ADVENTURES-ROW-API-DOMAIN-INVALID',
  swshDynamaxAdventuresRowFormUnresolved:
    'KM-SWSH-DYNAMAX-ADVENTURES-ROW-FORM-UNRESOLVED',
  swshDynamaxAdventuresTableLayoutMismatch:
    'KM-SWSH-DYNAMAX-ADVENTURES-TABLE-LAYOUT-MISMATCH',
  swshRoyalCandyPreflightBlocked: 'KM-SWSH-ROYAL-CANDY-PREFLIGHT-BLOCKED',
  zaItemsTmLegacyNumbering: 'KM-ZA-ITEMS-TM-LEGACY-NUMBERING',
  zaItemsTmLegacyPickupLayout: 'KM-ZA-ITEMS-TM-LEGACY-PICKUP-LAYOUT'
} as const satisfies Record<string, KmErrorCode>;

export const desktopErrorCodes = {
  appExitFailed: 'KM-DESKTOP-APP-EXIT-FAILED',
  appRelaunchFailed: 'KM-DESKTOP-APP-RELAUNCH-FAILED',
  bridgeRecycleFailed: 'KM-DESKTOP-BRIDGE-RECYCLE-FAILED',
  closeRequestListenerFailed: 'KM-DESKTOP-CLOSE-REQUEST-LISTENER-FAILED',
  closeGuardUpdateFailed: 'KM-DESKTOP-CLOSE-GUARD-UPDATE-FAILED',
  directoryCreateFailed: 'KM-DESKTOP-DIRECTORY-CREATE-FAILED',
  externalUrlOpenFailed: 'KM-DESKTOP-EXTERNAL-URL-OPEN-FAILED',
  filePickerFailed: 'KM-DESKTOP-FILE-PICKER-FAILED',
  folderPickerFailed: 'KM-DESKTOP-FOLDER-PICKER-FAILED',
  pathOpenFailed: 'KM-DESKTOP-PATH-OPEN-FAILED',
  pathPickerFailed: 'KM-DESKTOP-PATH-PICKER-FAILED',
  runtimeUnavailable: 'KM-DESKTOP-RUNTIME-UNAVAILABLE',
  supportFileSearchCancelFailed: 'KM-DESKTOP-SUPPORT-FILE-SEARCH-CANCEL-FAILED',
  supportFileSearchCanceled: 'KM-DESKTOP-SUPPORT-FILE-SEARCH-CANCELED',
  supportFileSearchFailed: 'KM-DESKTOP-SUPPORT-FILE-SEARCH-FAILED',
  unexpected: 'KM-DESKTOP-UNEXPECTED',
  updateCheckFailed: 'KM-DESKTOP-UPDATE-CHECK-FAILED',
  updateCloseFailed: 'KM-DESKTOP-UPDATE-CLOSE-FAILED',
  updateInstallFailed: 'KM-DESKTOP-UPDATE-INSTALL-FAILED'
} as const satisfies Record<string, KmErrorCode>;

export const uiErrorCodes = {
  renderUnexpected: 'KM-UI-RENDER-UNEXPECTED',
  unhandled: 'KM-UI-UNHANDLED',
  unhandledPromise: 'KM-UI-PROMISE-UNHANDLED'
} as const satisfies Record<string, KmErrorCode>;

export function isKmErrorCode(value: unknown): value is KmErrorCode {
  return typeof value === 'string' && kmErrorCodePattern.test(value);
}
