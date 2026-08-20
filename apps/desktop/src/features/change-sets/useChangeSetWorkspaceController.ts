/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  changePlanOutputModeSchema,
  type ChangePlanOutputMode,
  type EditSession
} from '../../bridge/contracts';
import type { ChangeSetProjectBridgeApi } from '../../bridge/changeSetProjectBridge';
import type {
  CaptureChangeSetSessionResponse,
  ChangeSetMaterialization,
  ChangeSetWorkspaceMutation,
  ChangeSetWorkspaceScope,
  ChangeSetWorkspaceSnapshot
} from '../../bridge/changeSetContracts';
import { useLocalization } from '../../localization';
import { toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';
import { mapChangeSetWorkspaceState } from './changeSetWorkspaceMapping';
import type {
  ChangeSetOutputModeViewModel,
  ChangeSetOutputProfileViewModel,
  ChangeSetWorkspaceBusyAction,
  ChangeSetWorkspaceController,
  ChangeSetWorkspaceReadiness
} from './changeSetWorkspaceTypes';

export type UseChangeSetWorkspaceControllerOptions = {
  activeChangeSetId: string | null;
  availableOutputModes?: readonly (ChangeSetOutputModeViewModel & {
    id: ChangePlanOutputMode;
  })[];
  availableOutputProfiles?: readonly ChangeSetOutputProfileViewModel[];
  bridge: ChangeSetProjectBridgeApi;
  currentSession: EditSession | null;
  externalBusy?: boolean;
  onActiveStagingTargetChange: (changeSetId: string | null) => void;
  onEffectiveState: (
    effective: ChangeSetMaterialization,
    snapshot: ChangeSetWorkspaceSnapshot
  ) => void;
  onRequestOutputProfileSwitch?: (outputProfileId: string) => void;
  scope: ChangeSetWorkspaceScope | null;
};

export type ChangeSetStagingCaptureBinding = {
  activeChangeSetId: string;
  workspaceETag: string;
};

export type ChangeSetWorkspaceControllerResult = {
  captureStagedSession: (
    previousSession: EditSession | null,
    stagedSession: EditSession,
    binding: ChangeSetStagingCaptureBinding
  ) => Promise<CaptureChangeSetSessionResponse>;
  controller: ChangeSetWorkspaceController;
  effective: ChangeSetMaterialization | null;
  materialize: (buildVariantId?: string | null) => Promise<ChangeSetMaterialization>;
  mutateHistory: (
    direction: 'redo' | 'undo',
    expectedETag?: string
  ) => Promise<ChangeSetWorkspaceSnapshot>;
  removeOperation: (
    changeSetId: string,
    operationId: string
  ) => Promise<ChangeSetWorkspaceSnapshot>;
  refresh: (
    sessionOverride?: EditSession | null
  ) => Promise<ChangeSetWorkspaceSnapshot | null>;
  snapshot: ChangeSetWorkspaceSnapshot | null;
};

export function useChangeSetWorkspaceController({
  activeChangeSetId,
  availableOutputModes = [],
  availableOutputProfiles = [],
  bridge,
  currentSession,
  externalBusy = false,
  onActiveStagingTargetChange,
  onEffectiveState,
  onRequestOutputProfileSwitch,
  scope
}: UseChangeSetWorkspaceControllerOptions): ChangeSetWorkspaceControllerResult {
  const { t } = useLocalization();
  const [snapshot, setSnapshot] = useState<ChangeSetWorkspaceSnapshot | null>(null);
  const [readiness, setReadiness] = useState<ChangeSetWorkspaceReadiness>(
    scope ? 'loading' : 'unavailable'
  );
  const [busyAction, setBusyAction] = useState<ChangeSetWorkspaceBusyAction | null>(null);
  const [actionDiagnostics, setActionDiagnostics] = useState<
    ChangeSetWorkspaceController['diagnostics']
  >([]);
  const [selectedChangeSetId, setSelectedChangeSetId] = useState<string | null>(null);
  const [comparisonChangeSetId, setComparisonChangeSetId] = useState<string | null>(null);
  const scopeKey = scope ? JSON.stringify(scope) : null;
  const scopeRef = useRef(scope);
  const scopeKeyRef = useRef(scopeKey);
  const currentSessionRef = useRef(currentSession);
  const activeChangeSetIdRef = useRef(activeChangeSetId);
  const snapshotRef = useRef(snapshot);
  const queueRef = useRef<Promise<void>>(Promise.resolve());
  const effectiveCallbackRef = useRef(onEffectiveState);
  const activeCallbackRef = useRef(onActiveStagingTargetChange);
  scopeRef.current = scope;
  scopeKeyRef.current = scopeKey;
  currentSessionRef.current = currentSession;
  activeChangeSetIdRef.current = activeChangeSetId;
  effectiveCallbackRef.current = onEffectiveState;
  activeCallbackRef.current = onActiveStagingTargetChange;

  const acceptSnapshot = useCallback((
    nextSnapshot: ChangeSetWorkspaceSnapshot,
    requestedScopeKey: string | null
  ) => {
    if (requestedScopeKey === null || requestedScopeKey !== scopeKeyRef.current) return false;
    snapshotRef.current = nextSnapshot;
    setSnapshot(nextSnapshot);
    setReadiness('ready');
    setActionDiagnostics([]);
    setSelectedChangeSetId((selectedId) => (
      selectedId && nextSnapshot.document.changeSets.some((set) => set.changeSetId === selectedId)
        ? selectedId
        : nextSnapshot.document.changeSets.find((set) => !set.archived)?.changeSetId
          ?? nextSnapshot.document.changeSets[0]?.changeSetId
          ?? null
    ));
    activeCallbackRef.current(nextSnapshot.document.activeChangeSetId);
    effectiveCallbackRef.current(nextSnapshot.effective, nextSnapshot);
    return true;
  }, []);

  const reportError = useCallback((error: unknown) => {
    setActionDiagnostics(toProjectBridgeDiagnostics(error, t('changeSets.requestError')));
  }, [t]);

  const enqueue = useCallback(<T,>(
    action: ChangeSetWorkspaceBusyAction,
    operation: (requestedScopeKey: string) => Promise<T>
  ) => {
    const requestedScopeKey = scopeKeyRef.current;
    const task = queueRef.current.then(async () => {
      if (requestedScopeKey === null || requestedScopeKey !== scopeKeyRef.current) {
        throw new Error('The change-set workspace scope changed before the action started.');
      }
      setBusyAction(action);
      try {
        return await operation(requestedScopeKey);
      } catch (error) {
        reportError(error);
        throw error;
      } finally {
        if (requestedScopeKey === scopeKeyRef.current) setBusyAction(null);
      }
    });
    queueRef.current = task.then(() => undefined, () => undefined);
    return task;
  }, [reportError]);

  const readWorkspace = useCallback((sessionOverride?: EditSession | null) => enqueue(
    'load',
    async (requestedScopeKey) => {
    const currentScope = scopeRef.current;
    if (!currentScope) return null;
    const nextSnapshot = await bridge.readChangeSets({
      scope: currentScope,
      session: sessionOverride === undefined ? currentSessionRef.current : sessionOverride
    });
    acceptSnapshot(nextSnapshot, requestedScopeKey);
    return nextSnapshot;
  }), [acceptSnapshot, bridge, enqueue]);

  useEffect(() => {
    if (scopeKey === null) {
      snapshotRef.current = null;
      setSnapshot(null);
      setReadiness('unavailable');
      setActionDiagnostics([]);
      setSelectedChangeSetId(null);
      setComparisonChangeSetId(null);
      return;
    }
    snapshotRef.current = null;
    setSnapshot(null);
    setReadiness('loading');
    setSelectedChangeSetId(null);
    setComparisonChangeSetId(null);
    void readWorkspace().catch(() => {
      if (scopeKeyRef.current === scopeKey) setReadiness('error');
    });
  }, [readWorkspace, scopeKey]);

  const mutate = useCallback((
    action: ChangeSetWorkspaceBusyAction,
    mutation: ChangeSetWorkspaceMutation,
    requiredETag?: string
  ) => enqueue(action, async (requestedScopeKey) => {
    const currentScope = scopeRef.current;
    if (!currentScope) throw new Error('A project is required to mutate change sets.');
    const currentETag = snapshotRef.current?.etag ?? null;
    if (requiredETag !== undefined && currentETag !== requiredETag) {
      throw new Error('The change-set history changed before the queued action started.');
    }
    const nextSnapshot = await bridge.mutateChangeSets({
      expectedETag: currentETag,
      mutation,
      scope: currentScope,
      session: currentSessionRef.current
    });
    acceptSnapshot(nextSnapshot, requestedScopeKey);
    return nextSnapshot;
  }), [acceptSnapshot, bridge, enqueue]);

  const captureStagedSession = useCallback((
    previousSession: EditSession | null,
    stagedSession: EditSession,
    binding: ChangeSetStagingCaptureBinding
  ) => enqueue('operations', async (requestedScopeKey) => {
    const currentScope = scopeRef.current;
    const currentSnapshot = snapshotRef.current;
    if (
      !currentScope ||
      !currentSnapshot?.etag ||
      !binding.activeChangeSetId ||
      !binding.workspaceETag ||
      currentSnapshot.etag !== binding.workspaceETag ||
      activeChangeSetIdRef.current !== binding.activeChangeSetId ||
      currentSnapshot.document.activeChangeSetId !== binding.activeChangeSetId
    ) {
      throw new Error(
        'The active change set or workspace history changed while the edit was being staged.'
      );
    }
    const response = await bridge.captureChangeSetSession({
      changeSetId: binding.activeChangeSetId,
      expectedETag: binding.workspaceETag,
      previousSession,
      scope: currentScope,
      stagedSession
    });
    acceptSnapshot(response.snapshot, requestedScopeKey);
    return response;
  }), [acceptSnapshot, bridge, enqueue]);

  const materialize = useCallback((buildVariantId: string | null = null) => (
    enqueue('variant', async (requestedScopeKey) => {
      const currentScope = scopeRef.current;
      const currentSnapshot = snapshotRef.current;
      if (!currentScope || !currentSnapshot?.etag) {
        throw new Error('A loaded change-set workspace is required before materializing.');
      }
      const effective = await bridge.materializeChangeSets({
        buildVariantId,
        expectedETag: currentSnapshot.etag,
        scope: currentScope,
        session: currentSessionRef.current
      });
      acceptSnapshot({ ...currentSnapshot, effective }, requestedScopeKey);
      return effective;
    })
  ), [acceptSnapshot, bridge, enqueue]);

  const mutateHistory = useCallback((direction: 'redo' | 'undo', expectedETag?: string) => (
    mutate(direction, { kind: direction }, expectedETag)
  ), [mutate]);
  const removeOperation = useCallback((changeSetId: string, operationId: string) => (
    mutate('operations', { changeSetId, kind: 'removeOperation', operationId })
  ), [mutate]);

  const mapped = useMemo(() => mapChangeSetWorkspaceState(
    snapshot,
    activeChangeSetId,
    comparisonChangeSetId,
    availableOutputModes,
    availableOutputProfiles,
    t
  ), [
    activeChangeSetId,
    availableOutputModes,
    availableOutputProfiles,
    comparisonChangeSetId,
    snapshot,
    t
  ]);

  const runControllerAction = useCallback((operation: () => Promise<unknown>) => {
    try {
      void operation().catch(() => undefined);
    } catch (error) {
      reportError(error);
    }
  }, [reportError]);
  const updateSet = useCallback((
    changeSetId: string,
    action: ChangeSetWorkspaceBusyAction,
    update: (set: NonNullable<typeof snapshotRef.current>['document']['changeSets'][number]) => {
      archived: boolean;
      dependencyIds: string[];
      enabled: boolean;
      name: string;
      notes: string | null;
      tags: string[];
    }
  ) => {
    const changeSet = snapshotRef.current?.document.changeSets.find(
      (candidate) => candidate.changeSetId === changeSetId
    );
    if (!changeSet) return Promise.reject(new Error('The change set is no longer available.'));
    return mutate(action, {
      changeSetId,
      kind: 'updateSet',
      metadata: update(changeSet)
    });
  }, [mutate]);

  const controller = useMemo<ChangeSetWorkspaceController>(() => ({
    activeStagingTargetId: activeChangeSetId,
    availableOutputModes,
    availableOutputProfiles,
    buildVariants: mapped.buildVariants,
    busyAction,
    canMaterialize: snapshot?.effective.canMaterialize ?? false,
    canRedo: snapshot?.canRedo ?? false,
    canUndo: snapshot?.canUndo ?? false,
    changeSets: mapped.changeSets,
    comparison: mapped.comparison,
    diagnostics: [...(snapshot?.effective.diagnostics ?? []), ...actionDiagnostics],
    externalBusy,
    legacyUnsupportedOperationCount: mapped.legacyUnsupportedOperationCount,
    onArchive: (changeSetId) => runControllerAction(() => updateSet(
      changeSetId,
      'archive',
      (set) => ({ ...toMetadata(set), archived: true })
    )),
    onCreate: (name) => runControllerAction(async () => {
      const priorIds = new Set(
        snapshotRef.current?.document.changeSets.map((set) => set.changeSetId) ?? []
      );
      const next = await mutate('create', { kind: 'createSet', name });
      const created = next.document.changeSets.find((set) => !priorIds.has(set.changeSetId));
      if (created) setSelectedChangeSetId(created.changeSetId);
    }),
    onCreateBuildVariant: (name, enabledChangeSetIds, outputMode, outputProfileId) => (
      runControllerAction(() => mutate('variant', {
        kind: 'createVariant',
        variant: {
          changeSetIds: [...enabledChangeSetIds],
          createdAtUtc: new Date().toISOString(),
          name,
          outputMode: parseOutputMode(outputMode),
          outputProfileId,
          updatedAtUtc: new Date().toISOString(),
          variantId: createStableId('variant')
        }
      }))
    ),
    onDeleteBuildVariant: (variantId) => runControllerAction(() => mutate(
      'variant',
      { kind: 'deleteVariant', variantId }
    )),
    onDeleteSet: (changeSetId) => runControllerAction(() => mutate(
      'delete',
      { changeSetId, kind: 'deleteSet' }
    )),
    onDuplicate: (changeSetId) => runControllerAction(() => {
      const source = snapshotRef.current?.document.changeSets.find(
        (set) => set.changeSetId === changeSetId
      );
      if (!source) return Promise.reject(new Error('The change set is no longer available.'));
      return mutate('duplicate', {
        changeSetId,
        kind: 'duplicateSet',
        name: t('changeSets.copyName', { name: source.name }).slice(0, 128).trim()
      });
    }),
    onExport: (changeSetId) => runControllerAction(() => enqueue(
      'operations',
      async () => {
        const currentScope = scopeRef.current;
        const currentSnapshot = snapshotRef.current;
        if (!currentScope || !currentSnapshot?.etag) {
          throw new Error('A loaded change-set workspace is required before exporting.');
        }
        const response = await bridge.exportChangeSets({
          changeSetIds: [changeSetId],
          expectedETag: currentSnapshot.etag,
          scope: currentScope
        });
        setActionDiagnostics(response.diagnostics);
        if (response.available && response.packageJson) {
          const name = currentSnapshot.document.changeSets.find(
            (set) => set.changeSetId === changeSetId
          )?.name ?? 'change-set';
          downloadPackage(response.packageJson, name);
        }
        return response;
      }
    )),
    onImport: (packageJson, enableImported) => runControllerAction(() => enqueue(
      'operations',
      async (requestedScopeKey) => {
        const currentScope = scopeRef.current;
        if (!currentScope) throw new Error('A project is required before importing.');
        const response = await bridge.importChangeSets({
          enableImported,
          expectedETag: snapshotRef.current?.etag ?? null,
          packageJson,
          scope: currentScope,
          session: currentSessionRef.current
        });
        acceptSnapshot(response.snapshot, requestedScopeKey);
        return response;
      }
    )),
    onLoadComparison: setComparisonChangeSetId,
    onMove: (changeSetId, direction) => runControllerAction(() => {
      const sets = snapshotRef.current?.document.changeSets;
      if (!sets) return Promise.reject(new Error('The change-set order is unavailable.'));
      const source = sets.find((set) => set.changeSetId === changeSetId);
      if (!source) return Promise.reject(new Error('The change set is no longer available.'));
      const peers = sets.filter((set) => set.archived === source.archived);
      const peerIndex = peers.findIndex((set) => set.changeSetId === changeSetId);
      const other = peers[peerIndex + (direction === 'up' ? -1 : 1)];
      if (!other) return Promise.resolve(snapshotRef.current!);
      const orderedIds = sets.map((set) => set.changeSetId);
      const sourceIndex = orderedIds.indexOf(changeSetId);
      const otherIndex = orderedIds.indexOf(other.changeSetId);
      [orderedIds[sourceIndex], orderedIds[otherIndex]] = [
        orderedIds[otherIndex]!,
        orderedIds[sourceIndex]!
      ];
      return mutate('reorder', { kind: 'reorderSets', orderedIds });
    }),
    onMoveOperation: (changeSetId, operationId, direction) => runControllerAction(() => {
      const operations = snapshotRef.current?.document.changeSets.find(
        (set) => set.changeSetId === changeSetId
      )?.operations;
      if (!operations) return Promise.reject(new Error('The operation order is unavailable.'));
      const orderedIds = operations.map((operation) => operation.operationId);
      const index = orderedIds.indexOf(operationId);
      const destination = index + (direction === 'up' ? -1 : 1);
      if (index < 0 || destination < 0 || destination >= orderedIds.length) {
        return Promise.resolve(snapshotRef.current!);
      }
      [orderedIds[index], orderedIds[destination]] = [
        orderedIds[destination]!,
        orderedIds[index]!
      ];
      return mutate('operations', { changeSetId, kind: 'reorderOperations', orderedIds });
    }),
    onRedo: () => runControllerAction(() => mutateHistory('redo')),
    onRefresh: () => runControllerAction(readWorkspace),
    onRemoveOperation: (changeSetId, operationId) => runControllerAction(() => mutate(
      'operations',
      { changeSetId, kind: 'removeOperation', operationId }
    )),
    onRename: (changeSetId, name) => runControllerAction(() => updateSet(
      changeSetId,
      'rename',
      (set) => ({ ...toMetadata(set), name })
    )),
    onRequestOutputProfileSwitch,
    onRestore: (changeSetId) => runControllerAction(() => updateSet(
      changeSetId,
      'restore',
      (set) => ({ ...toMetadata(set), archived: false })
    )),
    onSelectBuildVariant: (variantId) => runControllerAction(() => mutate(
      'variant',
      { kind: 'setActiveVariant', variantId }
    )),
    onSetActiveStagingTarget: (changeSetId) => runControllerAction(() => mutate(
      'setActive',
      { changeSetId, kind: 'setActiveSet' }
    )),
    onSetEnabled: (changeSetId, enabled) => runControllerAction(() => updateSet(
      changeSetId,
      'enable',
      (set) => ({ ...toMetadata(set), enabled })
    )),
    onUndo: () => runControllerAction(() => mutateHistory('undo')),
    onUpdateMetadata: (changeSetId, notes, tags, dependencyIds) => runControllerAction(() => (
      updateSet(changeSetId, 'metadata', (set) => ({
        ...toMetadata(set),
        dependencyIds: [...dependencyIds],
        notes: notes || null,
        tags: [...tags]
      }))
    )),
    readiness,
    redoLabel: localizeHistoryLabel(snapshot?.redoLabel, t),
    requiredOutputProfileId: mapped.requiredOutputProfileId,
    requiredOutputProfileName: mapped.requiredOutputProfileName,
    selectedChangeSetId,
    setSelectedChangeSetId,
    undoLabel: localizeHistoryLabel(snapshot?.undoLabel, t),
    unassignedOperationCount: currentSession?.pendingEdits.filter(
      (edit) => edit.association === null
    ).length ?? 0
  }), [
    acceptSnapshot,
    actionDiagnostics,
    activeChangeSetId,
    availableOutputModes,
    availableOutputProfiles,
    bridge,
    busyAction,
    currentSession,
    enqueue,
    externalBusy,
    mapped,
    mutate,
    mutateHistory,
    onRequestOutputProfileSwitch,
    readiness,
    readWorkspace,
    runControllerAction,
    selectedChangeSetId,
    snapshot,
    t,
    updateSet
  ]);

  return {
    captureStagedSession,
    controller,
    effective: snapshot?.effective ?? null,
    materialize,
    mutateHistory,
    removeOperation,
    refresh: readWorkspace,
    snapshot
  };
}

function toMetadata(set: ChangeSetWorkspaceSnapshot['document']['changeSets'][number]) {
  return {
    archived: set.archived,
    dependencyIds: [...set.dependencyIds],
    enabled: set.enabled,
    name: set.name,
    notes: set.notes,
    tags: [...set.tags]
  };
}

function parseOutputMode(value: string | null) {
  if (value === null) return null;
  return changePlanOutputModeSchema.parse(value);
}

function createStableId(prefix: string) {
  return `${prefix}-${crypto.randomUUID()}`;
}

function downloadPackage(packageJson: string, displayName: string) {
  const safeName = displayName
    .normalize('NFKD')
    .replace(/[^A-Za-z0-9._-]+/gu, '-')
    .replace(/^-+|-+$/gu, '')
    .slice(0, 80) || 'change-set';
  const url = URL.createObjectURL(new Blob([packageJson], { type: 'application/json' }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `${safeName}.km-change-set.json`;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

const historyLabelKeys: Readonly<Record<string, string>> = {
  'Change staging target': 'changeSets.history.setActiveSet',
  'Create build variant': 'changeSets.history.createVariant',
  'Create change set': 'changeSets.history.createSet',
  'Delete build variant': 'changeSets.history.deleteVariant',
  'Delete change set': 'changeSets.history.deleteSet',
  'Duplicate change set': 'changeSets.history.duplicateSet',
  'Remove staged edit': 'changeSets.history.removeOperation',
  'Reorder change sets': 'changeSets.history.reorderSets',
  'Reorder staged edits': 'changeSets.history.reorderOperations',
  'Select build variant': 'changeSets.history.setActiveVariant',
  'Update build variant': 'changeSets.history.updateVariant',
  'Update change set': 'changeSets.history.updateSet',
  'Update change sets': 'changeSets.history.updateSet'
};

function localizeHistoryLabel(
  label: string | null | undefined,
  t: ReturnType<typeof useLocalization>['t']
) {
  return label ? t(historyLabelKeys[label] ?? 'changeSets.history.updateSet') : null;
}
