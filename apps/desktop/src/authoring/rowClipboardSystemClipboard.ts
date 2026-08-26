/* SPDX-License-Identifier: GPL-3.0-only */

import { RowClipboardController } from './rowClipboardController';
import {
  RowClipboardError,
  rowClipboardMaximumCanonicalPayloadBytes,
  type RowClipboardEnvelopeV1
} from './rowClipboardTypes';

export const rowClipboardMaximumSerializedEnvelopeBytes =
  rowClipboardMaximumCanonicalPayloadBytes + 1024;

export type RowClipboardTextClipboard = Readonly<{
  readText: () => Promise<string>;
  writeText: (value: string) => Promise<void>;
}>;

export type RowClipboardSystemClipboardFailureReason =
  | 'clipboard-denied'
  | 'clipboard-empty'
  | 'clipboard-read-failed'
  | 'clipboard-unavailable'
  | 'clipboard-write-failed'
  | 'content-checksum-invalid'
  | 'content-incompatible'
  | 'content-malformed'
  | 'content-too-large'
  | 'validation-unavailable';

export type RowClipboardSystemClipboardFeedbackKey =
  | 'rowClipboard.feedback.clipboardDenied'
  | 'rowClipboard.feedback.clipboardEmpty'
  | 'rowClipboard.feedback.clipboardReadFailed'
  | 'rowClipboard.feedback.clipboardUnavailable'
  | 'rowClipboard.feedback.clipboardWriteFailed'
  | 'rowClipboard.feedback.contentChecksumInvalid'
  | 'rowClipboard.feedback.contentIncompatible'
  | 'rowClipboard.feedback.contentMalformed'
  | 'rowClipboard.feedback.contentTooLarge'
  | 'rowClipboard.feedback.validationUnavailable';

export type RowClipboardSystemClipboardResult<T> =
  | Readonly<{ kind: 'success'; value: T }>
  | Readonly<{
      feedbackKey: RowClipboardSystemClipboardFeedbackKey;
      kind: 'failure';
      reason: RowClipboardSystemClipboardFailureReason;
    }>;

export async function writeRowClipboardEnvelopeToSystemClipboard(
  envelope: RowClipboardEnvelopeV1,
  clipboard: RowClipboardTextClipboard | null = resolveSystemClipboard()
): Promise<RowClipboardSystemClipboardResult<undefined>> {
  if (!clipboard) {
    return failure('clipboard-unavailable');
  }

  let serialized: string;
  try {
    serialized = JSON.stringify(envelope);
  } catch {
    return failure('content-malformed');
  }
  const byteCount = serializedEnvelopeByteCount(serialized);
  if (byteCount === null) {
    return failure('validation-unavailable');
  }
  if (byteCount > rowClipboardMaximumSerializedEnvelopeBytes) {
    return failure('content-too-large');
  }

  try {
    await clipboard.writeText(serialized);
    return Object.freeze({ kind: 'success' as const, value: undefined });
  } catch (error) {
    return failure(isPermissionFailure(error) ? 'clipboard-denied' : 'clipboard-write-failed');
  }
}

export async function readRowClipboardEnvelopeFromSystemClipboard(
  controller: RowClipboardController,
  clipboard: RowClipboardTextClipboard | null = resolveSystemClipboard()
): Promise<RowClipboardSystemClipboardResult<RowClipboardEnvelopeV1>> {
  controller.invalidatePreview();
  if (!clipboard) {
    return failure('clipboard-unavailable');
  }

  let serialized: string;
  try {
    serialized = await clipboard.readText();
  } catch (error) {
    return failure(isPermissionFailure(error) ? 'clipboard-denied' : 'clipboard-read-failed');
  }
  if (serialized.length > rowClipboardMaximumSerializedEnvelopeBytes) {
    return failure('content-too-large');
  }
  const byteCount = serializedEnvelopeByteCount(serialized);
  if (byteCount === null) {
    return failure('validation-unavailable');
  }
  if (byteCount > rowClipboardMaximumSerializedEnvelopeBytes) {
    return failure('content-too-large');
  }
  if (serialized.trim().length === 0) {
    return failure('clipboard-empty');
  }

  let candidate: unknown;
  try {
    candidate = JSON.parse(serialized) as unknown;
  } catch {
    return failure('content-malformed');
  }

  try {
    const envelope = await controller.importEnvelope(candidate);
    return Object.freeze({ kind: 'success' as const, value: envelope });
  } catch (error) {
    return failure(classifyValidationFailure(error));
  }
}

function resolveSystemClipboard(): RowClipboardTextClipboard | null {
  if (
    typeof navigator === 'undefined' ||
    !navigator.clipboard ||
    typeof navigator.clipboard.readText !== 'function' ||
    typeof navigator.clipboard.writeText !== 'function'
  ) {
    return null;
  }
  return navigator.clipboard;
}

function serializedEnvelopeByteCount(value: string): number | null {
  if (typeof TextEncoder === 'undefined') {
    return null;
  }
  return new TextEncoder().encode(value).byteLength;
}

function isPermissionFailure(error: unknown): boolean {
  if (error === null || typeof error !== 'object') {
    return false;
  }
  const name =
    typeof DOMException !== 'undefined' && error instanceof DOMException
      ? error.name
      : 'name' in error && typeof error.name === 'string'
        ? error.name
        : null;
  return name === 'NotAllowedError' || name === 'SecurityError';
}

function classifyValidationFailure(
  error: unknown
): RowClipboardSystemClipboardFailureReason {
  if (!(error instanceof RowClipboardError)) {
    return 'content-malformed';
  }
  switch (error.code) {
    case 'checksum-mismatch':
    case 'invalid-checksum':
      return 'content-checksum-invalid';
    case 'checksum-unavailable':
      return 'validation-unavailable';
    case 'adapter-unavailable':
    case 'operation-unavailable':
    case 'schema-incompatible':
    case 'invalid-scope':
      return 'content-incompatible';
    case 'payload-limit-exceeded':
    case 'row-limit-exceeded':
    case 'value-limit-exceeded':
      return 'content-too-large';
    default:
      return 'content-malformed';
  }
}

function failure(
  reason: RowClipboardSystemClipboardFailureReason
): RowClipboardSystemClipboardResult<never> {
  const feedbackKey: RowClipboardSystemClipboardFeedbackKey =
    reason === 'clipboard-denied'
      ? 'rowClipboard.feedback.clipboardDenied'
      : reason === 'clipboard-empty'
        ? 'rowClipboard.feedback.clipboardEmpty'
        : reason === 'clipboard-read-failed'
          ? 'rowClipboard.feedback.clipboardReadFailed'
          : reason === 'clipboard-unavailable'
            ? 'rowClipboard.feedback.clipboardUnavailable'
            : reason === 'clipboard-write-failed'
              ? 'rowClipboard.feedback.clipboardWriteFailed'
              : reason === 'content-checksum-invalid'
                ? 'rowClipboard.feedback.contentChecksumInvalid'
                : reason === 'content-incompatible'
                  ? 'rowClipboard.feedback.contentIncompatible'
                  : reason === 'content-malformed'
                    ? 'rowClipboard.feedback.contentMalformed'
                    : reason === 'content-too-large'
                      ? 'rowClipboard.feedback.contentTooLarge'
                      : 'rowClipboard.feedback.validationUnavailable';
  return Object.freeze({ feedbackKey, kind: 'failure' as const, reason });
}
