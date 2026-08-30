// SPDX-License-Identifier: GPL-3.0-only

import type {
  HabitatCoordinateRecord,
  HabitatCoordinateChoice,
  HabitatCoordinatesQuery,
  HabitatCoordinatesWorkflow,
  HabitatRowBinding
} from '../../bridge/habitatCoordinatesContracts';
import type { EditSession } from '../../bridge/contracts';

export type HabitatCoordinateDraftValues = Record<string, string>;

export type HabitatCoordinatesLoadCommitGuard = {
  currentSessionSignature: string | null;
  currentStageGeneration: number;
  currentViewGeneration: number;
  requestedSessionSignature: string | null;
  requestedStageGeneration: number;
  requestedViewGeneration: number;
};

export type HabitatCoordinateStageEvidence = {
  binding: HabitatRowBinding;
  coordinate: HabitatCoordinateChoice;
  query: HabitatCoordinatesQuery;
  region: HabitatCoordinatesQuery['region'];
};

export type HabitatSearchSubmissionSnapshot = {
  draft: string;
  source: string;
};

export function createHabitatCoordinatesQueryKey(query: HabitatCoordinatesQuery) {
  return JSON.stringify([query.region, query.search, query.offset, query.limit]);
}

export function canCommitHabitatCoordinatesLoad({
  currentSessionSignature,
  currentStageGeneration,
  currentViewGeneration,
  requestedSessionSignature,
  requestedStageGeneration,
  requestedViewGeneration
}: HabitatCoordinatesLoadCommitGuard) {
  return (
    currentViewGeneration === requestedViewGeneration &&
    currentStageGeneration === requestedStageGeneration &&
    currentSessionSignature === requestedSessionSignature
  );
}

export function reconcileHabitatSearchDraftAfterAcceptedQuery(
  currentDraft: string,
  submitted: HabitatSearchSubmissionSnapshot,
  acceptedSource: string
) {
  const submittedBaseline =
    submitted.draft === submitted.source ? submitted.source : submitted.draft;
  return Object.is(currentDraft, submittedBaseline) ? acceptedSource : currentDraft;
}

export function habitatCoordinateStageResponseMatchesRequest(
  request: HabitatCoordinateStageEvidence,
  workflow: HabitatCoordinatesWorkflow,
  session: EditSession | null
) {
  if (
    request.region !== request.query.region ||
    workflow.page.limit !== request.query.limit ||
    workflow.page.offset !== request.query.offset ||
    workflow.page.region !== request.query.region ||
    workflow.page.search !== request.query.search
  ) {
    return false;
  }

  const hasRequestedWorkflowCoordinate = workflow.page.records.some(
    (record) =>
      habitatRowBindingsEqual(record.binding, request.binding) &&
      record.isStaged &&
      habitatCoordinatesEqual(record.stagedCoordinate, request.coordinate)
  );
  if (!hasRequestedWorkflowCoordinate || !session) {
    return false;
  }

  const requestedRecordId = createHabitatPendingEditRecordId(
    request.region,
    request.binding
  );
  const requestedValue = `${request.coordinate.x},${request.coordinate.y}`;
  return session.pendingEdits.some(
    (edit) =>
      edit.domain === 'workflow.habitatCoordinates' &&
      edit.field === 'coordinate' &&
      edit.recordId === requestedRecordId &&
      edit.newValue === requestedValue
  );
}

function createHabitatPendingEditRecordId(
  region: HabitatCoordinatesQuery['region'],
  binding: HabitatRowBinding
) {
  return [
    'v1',
    region,
    binding.outerGroupOccurrence,
    binding.rowOccurrence,
    binding.devNo,
    binding.formNo,
    binding.versionA ? 1 : 0,
    binding.versionB ? 1 : 0,
    binding.currentX,
    binding.currentY,
    binding.rowPreimageSha256,
    binding.sourceRevision
  ].join(':');
}

function habitatRowBindingsEqual(left: HabitatRowBinding, right: HabitatRowBinding) {
  return (
    left.sourceFile === right.sourceFile &&
    left.devNo === right.devNo &&
    left.formNo === right.formNo &&
    left.outerGroupOccurrence === right.outerGroupOccurrence &&
    left.rowOccurrence === right.rowOccurrence &&
    left.versionA === right.versionA &&
    left.versionB === right.versionB &&
    left.currentX === right.currentX &&
    left.currentY === right.currentY &&
    left.rowPreimageSha256 === right.rowPreimageSha256 &&
    left.sourceRevision === right.sourceRevision
  );
}

function habitatCoordinatesEqual(
  left: HabitatCoordinateChoice | null,
  right: HabitatCoordinateChoice
) {
  return left?.x === right.x && left.y === right.y;
}

export function createHabitatCoordinateDraftKey(
  region: string,
  record: HabitatCoordinateRecord
) {
  const binding = record.binding;
  return JSON.stringify([
    region,
    binding.sourceFile,
    binding.devNo,
    binding.formNo,
    binding.outerGroupOccurrence,
    binding.rowOccurrence
  ]);
}

export function setHabitatCoordinateDraftValue(
  drafts: HabitatCoordinateDraftValues,
  key: string,
  value: string,
  sourceValue: string
) {
  if (value === sourceValue) {
    return removeHabitatCoordinateDraftValue(drafts, key);
  }

  if (drafts[key] === value) {
    return drafts;
  }

  return { ...drafts, [key]: value };
}

export function clearStagedHabitatCoordinateDraftValue(
  drafts: HabitatCoordinateDraftValues,
  key: string,
  stagedValue: string
) {
  return drafts[key] === stagedValue
    ? removeHabitatCoordinateDraftValue(drafts, key)
    : drafts;
}

function removeHabitatCoordinateDraftValue(
  drafts: HabitatCoordinateDraftValues,
  key: string
) {
  if (!(key in drafts)) {
    return drafts;
  }

  const nextDrafts = { ...drafts };
  delete nextDrafts[key];
  return nextDrafts;
}
