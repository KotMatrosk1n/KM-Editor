/* SPDX-License-Identifier: GPL-3.0-only */

import type { WorkbenchSection } from '../workbenchStore';
import { semanticRecordRefKey } from './semanticContracts';
import type { WorkbenchLocation } from './workbenchLocation';
import { workbenchLocationsEqual } from './workbenchLocation';

export type NavigationBlockReason = 'busy' | 'unavailable';

export type NavigationExitPrompt = {
  allowGoToChanges?: boolean;
  destination: WorkbenchLocation;
  discardPendingSession?: boolean;
  kind: 'editorSwitch';
  mode: 'confirm';
  stageOnlyDexLayout?: boolean;
};

export type WorkbenchNavigationDecision =
  | { kind: 'unchanged' }
  | { kind: 'blocked'; reason: NavigationBlockReason }
  | { clearPendingState: boolean; kind: 'commit'; location: WorkbenchLocation }
  | { kind: 'prompt'; prompt: NavigationExitPrompt };

export type WorkbenchNavigationGuardState = {
  activeEditorHasLocalDrafts: boolean;
  activeLocation: WorkbenchLocation;
  activeSectionIsEditor: boolean;
  activeSectionOwnsAdvancedEditSession: boolean;
  activeSectionOwnsDexLayoutEditSession: boolean;
  activeSectionOwnsEditSession: boolean;
  canShareEditSessionWith: (section: WorkbenchSection) => boolean;
  editSessionSection: WorkbenchSection | null;
  hasCriticalWriteOperation: boolean;
  hasEditSession: boolean;
  isDestinationAvailable: (location: WorkbenchLocation) => boolean;
  isEditSessionOperationBusy: boolean;
  pendingEditCount: number;
};

export type WorkbenchNavigationRequestOptions = {
  preserveSameSectionDraftScope?: boolean;
};

export type WorkbenchNavigationController = {
  request: (
    location: WorkbenchLocation,
    options?: WorkbenchNavigationRequestOptions
  ) => WorkbenchNavigationDecision;
};

export function createWorkbenchNavigationController(
  getGuardState: () => WorkbenchNavigationGuardState
): WorkbenchNavigationController {
  return {
    request: (location, options) =>
      evaluateWorkbenchNavigation(getGuardState(), location, options)
  };
}

export function evaluateWorkbenchNavigation(
  state: WorkbenchNavigationGuardState,
  destination: WorkbenchLocation,
  options: WorkbenchNavigationRequestOptions = {}
): WorkbenchNavigationDecision {
  if (workbenchLocationsEqual(destination, state.activeLocation)) {
    return { kind: 'unchanged' };
  }

  const sharesDraftScope = locationsShareDraftScope(state.activeLocation, destination);
  const preservesDraftScope =
    sharesDraftScope ||
    (options.preserveSameSectionDraftScope === true &&
      locationsShareSectionDraftScope(state.activeLocation, destination));
  if (
    state.hasCriticalWriteOperation ||
    (state.isEditSessionOperationBusy && !preservesDraftScope)
  ) {
    return { kind: 'blocked', reason: 'busy' };
  }

  if (!state.isDestinationAvailable(destination)) {
    return { kind: 'blocked', reason: 'unavailable' };
  }

  const activeSection = state.activeLocation.section;
  const destinationSection = destination.section;
  if (preservesDraftScope) {
    return { clearPendingState: false, kind: 'commit', location: destination };
  }

  const isCrossingDexLayoutBoundary =
    (activeSection === 'dexLayout') !== (destinationSection === 'dexLayout');
  const isLeavingEmptyDexLayoutSession =
    isCrossingDexLayoutBoundary &&
    state.activeSectionOwnsDexLayoutEditSession &&
    state.pendingEditCount === 0 &&
    !state.activeEditorHasLocalDrafts;
  if (isLeavingEmptyDexLayoutSession) {
    return { clearPendingState: true, kind: 'commit', location: destination };
  }

  const isMovingCleanStagedSessionToChanges =
    destinationSection === 'changes' &&
    state.pendingEditCount > 0 &&
    !state.activeEditorHasLocalDrafts;
  const isMovingStagedDexLayoutToChanges =
    activeSection === 'dexLayout' && isMovingCleanStagedSessionToChanges;
  const hasDexLayoutBoundaryEdits =
    state.pendingEditCount > 0 ||
    (activeSection === 'dexLayout' && state.activeEditorHasLocalDrafts);
  if (
    isCrossingDexLayoutBoundary &&
    hasDexLayoutBoundaryEdits &&
    !isMovingStagedDexLayoutToChanges
  ) {
    return {
      kind: 'prompt',
      prompt: {
        allowGoToChanges:
          activeSection !== 'dexLayout' &&
          !state.activeSectionOwnsAdvancedEditSession &&
          !state.activeEditorHasLocalDrafts,
        destination,
        discardPendingSession:
          state.pendingEditCount > 0 || state.activeSectionOwnsDexLayoutEditSession,
        kind: 'editorSwitch',
        mode: 'confirm',
        stageOnlyDexLayout: activeSection === 'dexLayout'
      }
    };
  }

  const destinationOwnsEditSession =
    state.hasEditSession &&
    (destinationSection === state.editSessionSection ||
      state.canShareEditSessionWith(destinationSection));
  const isLeavingActiveEditSession =
    destinationSection !== 'changes' &&
    state.hasEditSession &&
    !destinationOwnsEditSession &&
    (state.activeSectionOwnsEditSession ||
      activeSection === 'changes' ||
      state.activeSectionIsEditor);
  const isLeavingAdvancedEditorForChanges =
    destinationSection === 'changes' &&
    state.activeSectionOwnsAdvancedEditSession &&
    !isMovingCleanStagedSessionToChanges;

  if (isLeavingActiveEditSession || isLeavingAdvancedEditorForChanges) {
    return {
      kind: 'prompt',
      prompt: {
        allowGoToChanges:
          !state.activeSectionOwnsAdvancedEditSession &&
          !state.activeEditorHasLocalDrafts,
        destination,
        discardPendingSession: true,
        kind: 'editorSwitch',
        mode: 'confirm',
        stageOnlyDexLayout: activeSection === 'dexLayout'
      }
    };
  }

  if (state.activeSectionIsEditor && state.activeEditorHasLocalDrafts) {
    return {
      kind: 'prompt',
      prompt: {
        destination,
        kind: 'editorSwitch',
        mode: 'confirm'
      }
    };
  }

  return { clearPendingState: false, kind: 'commit', location: destination };
}

function locationsShareDraftScope(left: WorkbenchLocation, right: WorkbenchLocation) {
  return (
    locationsShareSectionDraftScope(left, right) &&
    semanticEntityKeysEqual(left, right, left.section)
  );
}

function locationsShareSectionDraftScope(left: WorkbenchLocation, right: WorkbenchLocation) {
  return (
    left.projectId === right.projectId &&
    left.game === right.game &&
    left.changeSetId === right.changeSetId &&
    left.section === right.section
  );
}

function semanticEntityKeysEqual(
  left: WorkbenchLocation,
  right: WorkbenchLocation,
  section: WorkbenchSection
) {
  if (!left.entity || !right.entity) {
    return left.entity === right.entity;
  }

  if (section === 'trainers' || section === 'encounters') {
    return (
      left.entity.domain === right.entity.domain &&
      left.entity.gameFamily === right.entity.gameFamily &&
      left.entity.recordId === right.entity.recordId &&
      left.entity.recordKind.key === right.entity.recordKind.key &&
      left.entity.recordKind.schemaVersion === right.entity.recordKind.schemaVersion
    );
  }

  return semanticRecordRefKey(left.entity) === semanticRecordRefKey(right.entity);
}
