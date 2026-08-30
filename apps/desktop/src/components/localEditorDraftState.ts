/* SPDX-License-Identifier: GPL-3.0-only */

export type DraftEquality<T> = (left: T, right: T) => boolean;

export type KeyedEditorDrafts<T> = Record<string, T>;

/**
 * Accept a refreshed source value only when the user is still displaying the
 * source value that preceded it. A locally edited value always wins until the
 * user explicitly stages, discards, or changes editor scope.
 */
export function reconcileSourceBackedDraft<T>(
  currentDraft: T,
  previousSourceDraft: T,
  nextSourceDraft: T,
  equals: DraftEquality<T>
): T {
  return equals(currentDraft, previousSourceDraft) ? nextSourceDraft : currentDraft;
}

/** Resolve one submitted draft snapshot without overwriting a newer edit. */
export function resolveSubmittedEditorDraft<T>(
  currentDraft: T,
  submittedDraft: T,
  resolvedDraft: T,
  equals: DraftEquality<T> = Object.is
): T {
  return equals(currentDraft, submittedDraft) ? resolvedDraft : currentDraft;
}

/**
 * Reconcile a collection of source-backed keyed drafts after the source
 * refreshes without overwriting local work.
 *
 * - An untouched source value advances to its refreshed value.
 * - A locally modified value is retained.
 * - A newly introduced source key is seeded into the draft collection.
 * - An untouched key removed from the source is removed from the drafts.
 * - A locally modified or locally introduced key removed from the source is
 *   retained so the user can explicitly stage or discard it.
 */
export function reconcileKeyedSourceBackedEditorDrafts<T>(
  currentDrafts: KeyedEditorDrafts<T>,
  previousSourceDrafts: KeyedEditorDrafts<T>,
  nextSourceDrafts: KeyedEditorDrafts<T>,
  equals: DraftEquality<T> = Object.is
): KeyedEditorDrafts<T> {
  const nextDrafts: KeyedEditorDrafts<T> = {};

  for (const [key, currentDraft] of Object.entries(currentDrafts)) {
    const previousSourceDraft = previousSourceDrafts[key];
    const nextSourceDraft = nextSourceDrafts[key];
    const existedInPreviousSource = Object.hasOwn(previousSourceDrafts, key);
    const existsInNextSource = Object.hasOwn(nextSourceDrafts, key);

    if (existedInPreviousSource && equals(currentDraft, previousSourceDraft)) {
      if (existsInNextSource) {
        nextDrafts[key] = nextSourceDraft;
      }
      continue;
    }

    nextDrafts[key] = currentDraft;
  }

  for (const [key, nextSourceDraft] of Object.entries(nextSourceDrafts)) {
    if (!Object.hasOwn(currentDrafts, key)) {
      nextDrafts[key] = nextSourceDraft;
    }
  }

  const currentKeys = Object.keys(currentDrafts);
  const nextKeys = Object.keys(nextDrafts);
  if (
    currentKeys.length === nextKeys.length &&
    currentKeys.every(
      (key) => Object.hasOwn(nextDrafts, key) && equals(currentDrafts[key], nextDrafts[key])
    )
  ) {
    return currentDrafts;
  }

  return nextDrafts;
}

export function areStringSetsEqual(
  left: ReadonlySet<string>,
  right: ReadonlySet<string>
) {
  return left.size === right.size && [...left].every((value) => right.has(value));
}

export function reconcileEligibleDraftSelection(
  currentSelection: ReadonlySet<string>,
  previousEligibleIds: ReadonlySet<string>,
  nextEligibleIds: ReadonlySet<string>
) {
  const nextSelection = new Set(
    [...currentSelection].filter((id) => nextEligibleIds.has(id))
  );

  for (const id of nextEligibleIds) {
    if (!previousEligibleIds.has(id)) {
      nextSelection.add(id);
    }
  }

  return nextSelection;
}

/**
 * Resolve an async draft submission only when the keyed draft still matches the
 * exact snapshot that was sent. Edits made while the request was in flight are
 * deliberately preserved.
 *
 * Pass `undefined` as `resolvedDraft` to remove the submitted draft, or pass a
 * replacement value when the successful operation leaves another local draft.
 */
export function resolveSubmittedKeyedEditorDraft<T>(
  drafts: KeyedEditorDrafts<T>,
  key: string | number,
  submittedDraft: T,
  resolvedDraft: T | undefined,
  equals: DraftEquality<T> = Object.is
): Record<string, T> {
  const normalizedKey = key.toString();
  const latestDraft = drafts[normalizedKey];
  if (latestDraft === undefined || !equals(latestDraft, submittedDraft)) {
    return drafts;
  }

  if (resolvedDraft !== undefined && equals(latestDraft, resolvedDraft)) {
    return drafts;
  }

  const nextDrafts = { ...drafts };
  if (resolvedDraft === undefined) {
    delete nextDrafts[normalizedKey];
  } else {
    nextDrafts[normalizedKey] = resolvedDraft;
  }
  return nextDrafts;
}

export function clearSubmittedKeyedEditorDraft<T>(
  drafts: KeyedEditorDrafts<T>,
  key: string | number,
  submittedDraft: T,
  equals: DraftEquality<T> = Object.is
) {
  return resolveSubmittedKeyedEditorDraft(
    drafts,
    key,
    submittedDraft,
    undefined,
    equals
  );
}
