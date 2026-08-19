/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import { semanticRecordRefKey } from './semanticContracts';
import {
  createWorkbenchLocation,
  parseWorkbenchLocation,
  serializeWorkbenchLocation,
  workbenchLocationsEqual,
  type WorkbenchLocation
} from './workbenchLocation';

export const maximumWorkspaceHistoryEntries = 64;
export const maximumWorkspaceRecentEntries = 64;
export const maximumWorkspaceTabs = 12;
export const workbenchLocationHashPrefix = '#/workbench?';

export type WorkspaceShellScope = {
  game: ProjectGame | null;
  projectId: string | null;
};

export type WorkspaceShellTab = {
  key: string;
  lastAccessRevision: number;
  location: WorkbenchLocation;
};

export type WorkspaceShellState = {
  history: readonly WorkbenchLocation[];
  historyIndex: number;
  recents: readonly WorkbenchLocation[];
  revision: number;
  scope: WorkspaceShellScope;
  tabs: readonly WorkspaceShellTab[];
};

export type WorkspaceNavigationMode =
  | 'push'
  | 'replace'
  | 'inspector'
  | 'back'
  | 'forward';

export type PendingWorkspaceNavigation = {
  expectedRevision: number;
  historyIndex: number | null;
  mode: WorkspaceNavigationMode;
  target: WorkbenchLocation;
};

export type WorkspaceNavigationCommitOptions = {
  protectedTabKeys?: ReadonlySet<string>;
  rememberRecent?: boolean;
  tabEligible?: boolean;
};

export type WorkspaceNavigationCommitResult =
  | { kind: 'committed'; state: WorkspaceShellState }
  | { kind: 'stale'; state: WorkspaceShellState }
  | { kind: 'mismatch'; state: WorkspaceShellState };

export function createWorkspaceShellState(
  initialLocation: WorkbenchLocation
): WorkspaceShellState {
  const canonicalLocation = createWorkbenchLocation(initialLocation);
  return {
    history: [canonicalLocation],
    historyIndex: 0,
    recents: [],
    revision: 0,
    scope: locationScope(canonicalLocation),
    tabs: []
  };
}

export function resetWorkspaceShellScope(
  state: WorkspaceShellState,
  initialLocation: WorkbenchLocation
): WorkspaceShellState {
  const canonicalLocation = createWorkbenchLocation(initialLocation);
  return {
    history: [canonicalLocation],
    historyIndex: 0,
    recents: [],
    revision: nextRevision(state.revision),
    scope: locationScope(canonicalLocation),
    tabs: []
  };
}

export function beginWorkspaceNavigation(
  state: WorkspaceShellState,
  target: WorkbenchLocation,
  mode: Extract<WorkspaceNavigationMode, 'push' | 'replace' | 'inspector'> = 'push'
): PendingWorkspaceNavigation {
  const canonicalTarget = createWorkbenchLocation(target);
  assertLocationInScope(state.scope, canonicalTarget);
  return {
    expectedRevision: state.revision,
    historyIndex: null,
    mode,
    target: canonicalTarget
  };
}

export function beginWorkspaceBackNavigation(
  state: WorkspaceShellState
): PendingWorkspaceNavigation | null {
  const historyIndex = state.historyIndex - 1;
  const target = state.history[historyIndex];
  return target
    ? {
        expectedRevision: state.revision,
        historyIndex,
        mode: 'back',
        target
      }
    : null;
}

export function beginWorkspaceForwardNavigation(
  state: WorkspaceShellState
): PendingWorkspaceNavigation | null {
  const historyIndex = state.historyIndex + 1;
  const target = state.history[historyIndex];
  return target
    ? {
        expectedRevision: state.revision,
        historyIndex,
        mode: 'forward',
        target
      }
    : null;
}

export function getWorkspaceBackTarget(state: WorkspaceShellState) {
  return state.history[state.historyIndex - 1] ?? null;
}

export function getWorkspaceForwardTarget(state: WorkspaceShellState) {
  return state.history[state.historyIndex + 1] ?? null;
}

// Call this only after the existing guarded navigation controller reports that
// the exact target committed. Prompts, blocked destinations, and failed lazy
// loads therefore cannot move history, recents, or the tab LRU.
export function commitWorkspaceNavigation(
  state: WorkspaceShellState,
  pending: PendingWorkspaceNavigation,
  committedLocation: WorkbenchLocation,
  options: WorkspaceNavigationCommitOptions = {}
): WorkspaceNavigationCommitResult {
  if (pending.expectedRevision !== state.revision) {
    return { kind: 'stale', state };
  }

  const canonicalCommittedLocation = createWorkbenchLocation(committedLocation);
  if (!workbenchLocationsEqual(pending.target, canonicalCommittedLocation)) {
    return { kind: 'mismatch', state };
  }
  assertLocationInScope(state.scope, canonicalCommittedLocation);
  if (!pendingBookkeepingMatchesState(state, pending)) {
    return { kind: 'mismatch', state };
  }

  const historyResult = commitHistory(state, pending, canonicalCommittedLocation);
  const revision = nextRevision(state.revision);
  const recents = options.rememberRecent === false
    ? state.recents
    : rememberRecentLocation(state.recents, canonicalCommittedLocation);
  const tabs = options.tabEligible
    ? rememberEligibleTab(
        state.tabs,
        canonicalCommittedLocation,
        revision,
        options.protectedTabKeys ?? new Set<string>()
      )
    : state.tabs;

  return {
    kind: 'committed',
    state: {
      history: historyResult.history,
      historyIndex: historyResult.historyIndex,
      recents,
      revision,
      scope: state.scope,
      tabs
    }
  };
}

function pendingBookkeepingMatchesState(
  state: WorkspaceShellState,
  pending: PendingWorkspaceNavigation
) {
  if (pending.mode !== 'back' && pending.mode !== 'forward') {
    return pending.historyIndex === null;
  }
  const expectedIndex = pending.mode === 'back'
    ? state.historyIndex - 1
    : state.historyIndex + 1;
  const indexedLocation = pending.historyIndex === null
    ? undefined
    : state.history[pending.historyIndex];
  return (
    pending.historyIndex === expectedIndex &&
    indexedLocation !== undefined &&
    workbenchLocationsEqual(indexedLocation, pending.target)
  );
}

export function closeWorkspaceTab(
  state: WorkspaceShellState,
  tabKey: string,
  protectedTabKeys: ReadonlySet<string> = new Set<string>()
) {
  if (protectedTabKeys.has(tabKey) || !state.tabs.some((tab) => tab.key === tabKey)) {
    return state;
  }
  return {
    ...state,
    revision: nextRevision(state.revision),
    tabs: state.tabs.filter((tab) => tab.key !== tabKey)
  };
}

export function workspaceTabKey(location: WorkbenchLocation) {
  if (!location.entity) {
    return null;
  }
  return `${location.section}:${semanticRecordRefKey(location.entity)}`;
}

export function serializeWorkbenchLocationHash(location: WorkbenchLocation) {
  return `${workbenchLocationHashPrefix}${serializeWorkbenchLocation(location)}`;
}

export function parseWorkbenchLocationHash(value: string) {
  if (!value.startsWith(workbenchLocationHashPrefix)) {
    return null;
  }
  return parseWorkbenchLocation(value.slice(workbenchLocationHashPrefix.length));
}

function commitHistory(
  state: WorkspaceShellState,
  pending: PendingWorkspaceNavigation,
  committedLocation: WorkbenchLocation
) {
  if (pending.mode === 'back' || pending.mode === 'forward') {
    const indexedLocation = pending.historyIndex === null
      ? undefined
      : state.history[pending.historyIndex];
    if (
      pending.historyIndex === null ||
      !indexedLocation ||
      !workbenchLocationsEqual(indexedLocation, committedLocation)
    ) {
      return { history: state.history, historyIndex: state.historyIndex };
    }
    return { history: state.history, historyIndex: pending.historyIndex };
  }

  if (pending.mode === 'replace' || pending.mode === 'inspector') {
    const history = state.history.slice();
    history[state.historyIndex] = committedLocation;
    return { history, historyIndex: state.historyIndex };
  }

  const currentLocation = state.history[state.historyIndex];
  if (currentLocation && workbenchLocationsEqual(currentLocation, committedLocation)) {
    return { history: state.history, historyIndex: state.historyIndex };
  }

  const history = [
    ...state.history.slice(0, state.historyIndex + 1),
    committedLocation
  ];
  if (history.length > maximumWorkspaceHistoryEntries) {
    history.splice(0, history.length - maximumWorkspaceHistoryEntries);
  }
  return { history, historyIndex: history.length - 1 };
}

function rememberRecentLocation(
  recents: readonly WorkbenchLocation[],
  location: WorkbenchLocation
) {
  const normalizedLocation = withoutInspector(location);
  const normalizedKey = serializeWorkbenchLocation(normalizedLocation);
  return [
    normalizedLocation,
    ...recents.filter(
      (candidate) => serializeWorkbenchLocation(withoutInspector(candidate)) !== normalizedKey
    )
  ].slice(0, maximumWorkspaceRecentEntries);
}

function rememberEligibleTab(
  tabs: readonly WorkspaceShellTab[],
  location: WorkbenchLocation,
  revision: number,
  protectedTabKeys: ReadonlySet<string>
) {
  const key = workspaceTabKey(location);
  if (!key) {
    return tabs;
  }

  const existingIndex = tabs.findIndex((tab) => tab.key === key);
  const nextTab = { key, lastAccessRevision: revision, location: withoutInspector(location) };
  const nextTabs = existingIndex >= 0
    ? tabs.map((tab, index) => (index === existingIndex ? nextTab : tab))
    : [...tabs, nextTab];
  if (nextTabs.length <= maximumWorkspaceTabs) {
    return nextTabs;
  }

  const evictionCandidate = nextTabs
    .filter((tab) => tab.key !== key && !protectedTabKeys.has(tab.key))
    .sort((left, right) => left.lastAccessRevision - right.lastAccessRevision)[0];
  if (!evictionCandidate) {
    return tabs;
  }
  return nextTabs.filter((tab) => tab.key !== evictionCandidate.key);
}

function withoutInspector(location: WorkbenchLocation) {
  const { inspectorTab: _inspectorTab, ...target } = location;
  void _inspectorTab;
  return createWorkbenchLocation(target);
}

function locationScope(location: WorkbenchLocation): WorkspaceShellScope {
  return { game: location.game, projectId: location.projectId };
}

function assertLocationInScope(scope: WorkspaceShellScope, location: WorkbenchLocation) {
  if (scope.game !== location.game || scope.projectId !== location.projectId) {
    throw new Error('Workspace navigation must be reset before changing project scope.');
  }
}

function nextRevision(revision: number) {
  return revision === Number.MAX_SAFE_INTEGER ? 0 : revision + 1;
}
