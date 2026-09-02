/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  changePlanOutputModeSchema,
  type ChangePlanOutputMode,
  type EditSession
} from '../../bridge/contracts';
import type { ChangeSetProjectBridgeApi } from '../../bridge/changeSetProjectBridge';
import type { GuidedDesignProjectBridgeApi } from '../../bridge/guidedDesignProjectBridge';
import type { SemanticMergeProjectBridgeApi } from '../../bridge/semanticMergeProjectBridge';
import type {
  GuidedDesignImportRequest,
  GuidedDesignImportResponse
} from '../../bridge/guidedDesignContracts';
import type {
  CaptureChangeSetSessionResponse,
  ChangeSetMaterialization,
  ChangeSetWorkspaceMutation,
  ChangeSetWorkspaceScope,
  ChangeSetWorkspaceSnapshot
} from '../../bridge/changeSetContracts';
import type {
  KmRecipeImportRequest,
  KmRecipeImportResponse,
  SemanticMergeImportRequest,
  SemanticMergeImportResponse
} from '../../bridge/semanticMergeContracts';
import type {
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
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
  canStartOperation?: () => boolean;
  currentSession: EditSession | null;
  enabled?: boolean;
  externalBusy?: boolean;
  guidedDesignBridge?: GuidedDesignProjectBridgeApi;
  semanticMergeBridge?: SemanticMergeProjectBridgeApi;
  onActiveStagingTargetChange: (changeSetId: string | null) => void;
  onEffectiveState: (
    effective: ChangeSetMaterialization,
    snapshot: ChangeSetWorkspaceSnapshot
  ) => void;
  onScopeBlockingOperationBusyChange?: (isBusy: boolean) => void;
  onRequestOutputProfileSwitch?: (outputProfileId: string) => void;
  scope: ChangeSetWorkspaceScope | null;
};

export type ChangeSetStagingCaptureBinding = {
  activeChangeSetId: string;
  workspaceETag: string;
};

export type ExpectedImportedScalarEdit = {
  domain: string;
  field: string;
  newValue: string;
  recordId: string;
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
  importGuidedDesignProposal: (
    request: GuidedDesignImportRequest
  ) => Promise<GuidedDesignImportResponse>;
  importKmRecipe: (
    request: KmRecipeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => Promise<KmRecipeImportResponse>;
  importSemanticMerge: (
    request: SemanticMergeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => Promise<SemanticMergeImportResponse>;
  isScopeBlockingOperationInFlight: () => boolean;
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
  canStartOperation = () => true,
  currentSession,
  enabled = true,
  externalBusy = false,
  guidedDesignBridge,
  semanticMergeBridge,
  onActiveStagingTargetChange,
  onEffectiveState,
  onRequestOutputProfileSwitch,
  onScopeBlockingOperationBusyChange,
  scope
}: UseChangeSetWorkspaceControllerOptions): ChangeSetWorkspaceControllerResult {
  const { t } = useLocalization();
  const [snapshot, setSnapshot] = useState<ChangeSetWorkspaceSnapshot | null>(null);
  const activeScope = enabled ? scope : null;
  const [readiness, setReadiness] = useState<ChangeSetWorkspaceReadiness>(
    activeScope ? 'loading' : 'unavailable'
  );
  const [busyAction, setBusyAction] = useState<ChangeSetWorkspaceBusyAction | null>(null);
  const [actionDiagnostics, setActionDiagnostics] = useState<
    ChangeSetWorkspaceController['diagnostics']
  >([]);
  const [selectedChangeSetId, setSelectedChangeSetId] = useState<string | null>(null);
  const [comparisonChangeSetId, setComparisonChangeSetId] = useState<string | null>(null);
  const scopeKey = activeScope ? JSON.stringify(activeScope) : null;
  const scopeRef = useRef(activeScope);
  const scopeKeyRef = useRef(scopeKey);
  const currentSessionRef = useRef(currentSession);
  const activeChangeSetIdRef = useRef(activeChangeSetId);
  const externalBusyRef = useRef(externalBusy);
  const operationAdmissionRef = useRef(canStartOperation);
  const snapshotRef = useRef(snapshot);
  const queueRef = useRef<Promise<void>>(Promise.resolve());
  const controllerActionRef = useRef<object | null>(null);
  const queuedOperationTokensRef = useRef(new Set<object>());
  const effectiveCallbackRef = useRef(onEffectiveState);
  const activeCallbackRef = useRef(onActiveStagingTargetChange);
  const scopeBlockingBusyCallbackRef = useRef(onScopeBlockingOperationBusyChange);
  scopeRef.current = activeScope;
  scopeKeyRef.current = scopeKey;
  currentSessionRef.current = currentSession;
  activeChangeSetIdRef.current = activeChangeSetId;
  externalBusyRef.current = externalBusy;
  operationAdmissionRef.current = canStartOperation;
  effectiveCallbackRef.current = onEffectiveState;
  activeCallbackRef.current = onActiveStagingTargetChange;
  scopeBlockingBusyCallbackRef.current = onScopeBlockingOperationBusyChange;

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
    operation: (requestedScopeKey: string) => Promise<T>,
    blocksScopeTransition = false
  ) => {
    if (blocksScopeTransition && !operationAdmissionRef.current()) {
      return Promise.reject(new Error(
        'The change-set action cannot start during another project operation.'
      ));
    }
    const requestedScopeKey = scopeKeyRef.current;
    const operationToken = blocksScopeTransition ? {} : null;
    if (operationToken) {
      const wasIdle = queuedOperationTokensRef.current.size === 0;
      queuedOperationTokensRef.current.add(operationToken);
      if (wasIdle) scopeBlockingBusyCallbackRef.current?.(true);
    }
    const task = queueRef.current.then(async () => {
      try {
        if (requestedScopeKey === null || requestedScopeKey !== scopeKeyRef.current) {
          throw new Error('The change-set workspace scope changed before the action started.');
        }
        setBusyAction(action);
        try {
          return await operation(requestedScopeKey);
        } catch (error) {
          if (requestedScopeKey === scopeKeyRef.current) reportError(error);
          throw error;
        } finally {
          if (requestedScopeKey === scopeKeyRef.current) setBusyAction(null);
        }
      } finally {
        if (operationToken) {
          queuedOperationTokensRef.current.delete(operationToken);
          if (queuedOperationTokensRef.current.size === 0) {
            scopeBlockingBusyCallbackRef.current?.(false);
          }
        }
      }
    });
    queueRef.current = task.then(() => undefined, () => undefined);
    return task;
  }, [reportError]);

  const readWorkspace = useCallback((sessionOverride?: EditSession | null) => {
    if (scopeKeyRef.current === null) return Promise.resolve(null);
    return enqueue(
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
      }
    );
  }, [acceptSnapshot, bridge, enqueue]);

  useEffect(() => {
    if (scopeKey === null) {
      snapshotRef.current = null;
      setSnapshot(null);
      setReadiness('unavailable');
      setBusyAction(null);
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
  }, true), [acceptSnapshot, bridge, enqueue]);

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
  }, true), [acceptSnapshot, bridge, enqueue]);

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
  const importGuidedDesignProposal = useCallback((
    request: GuidedDesignImportRequest
  ) => enqueue('operations', async (requestedScopeKey) => {
    const currentScope = scopeRef.current;
    const currentSnapshot = snapshotRef.current;
    if (externalBusyRef.current) {
      throw new Error('Guided Design cannot import during another project write.');
    }
    if (!guidedDesignBridge || !currentScope || !currentSnapshot) {
      throw new Error(
        'A loaded change-set workspace is required before importing a Guided Design proposal.'
      );
    }
    if (
      request.scope.projectId !== currentScope.projectId ||
      JSON.stringify(request.scope.paths) !== JSON.stringify(currentScope.paths) ||
      JSON.stringify(request.scope.pendingSession) !== JSON.stringify(currentSessionRef.current)
    ) {
      throw new Error('The Guided Design proposal belongs to a different project scope.');
    }
    if (request.expectedChangeSetETag !== currentSnapshot.etag) {
      throw new Error('The change-set workspace changed after the proposal preview.');
    }
    const response = await guidedDesignBridge.importGuidedDesignProposal(request);
    const priorChangeSetIds = new Set(
      currentSnapshot.document.changeSets.map((changeSet) => changeSet.changeSetId)
    );
    const responseExistingChangeSets = response.snapshot.document.changeSets.filter(
      (changeSet) => changeSet.changeSetId !== response.importedChangeSetId
    );
    if (
      response.proposalId !== request.proposalId ||
      response.proposalFingerprint !== request.proposalFingerprint ||
      response.revision.projectId !== request.expectedRevision.projectId ||
      response.revision.gameFamily !== request.expectedRevision.gameFamily ||
      response.revision.generation !== request.expectedRevision.generation ||
      response.revision.fingerprint !== request.expectedRevision.fingerprint ||
      response.snapshot.document.game !== currentScope.paths.selectedGame ||
      response.snapshot.etag === null ||
      response.snapshot.etag === currentSnapshot.etag ||
      priorChangeSetIds.has(response.importedChangeSetId) ||
      response.snapshot.document.changeSets.length !==
        currentSnapshot.document.changeSets.length + 1 ||
      JSON.stringify(responseExistingChangeSets) !==
        JSON.stringify(currentSnapshot.document.changeSets) ||
      JSON.stringify(response.snapshot.document.buildVariants) !==
        JSON.stringify(currentSnapshot.document.buildVariants) ||
      response.snapshot.document.activeChangeSetId !==
        currentSnapshot.document.activeChangeSetId ||
      response.snapshot.document.activeBuildVariantId !==
        currentSnapshot.document.activeBuildVariantId
    ) {
      throw new Error('The imported Guided Design response no longer matches its reviewed context.');
    }
    const imported = response.snapshot.document.changeSets.find(
      (changeSet) => changeSet.changeSetId === response.importedChangeSetId
    );
    if (
      !imported ||
      imported.enabled ||
      imported.archived ||
      response.snapshot.document.activeChangeSetId === response.importedChangeSetId
    ) {
      throw new Error('The imported Guided Design change set is not safely disabled.');
    }
    if (!acceptSnapshot(response.snapshot, requestedScopeKey)) {
      throw new Error('The project scope changed before the Guided Design import completed.');
    }
    setSelectedChangeSetId(response.importedChangeSetId);
    return response;
  }, true), [acceptSnapshot, enqueue, guidedDesignBridge]);

  const importSemanticMerge = useCallback((
    request: SemanticMergeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => enqueue('operations', async (requestedScopeKey) => {
    const currentScope = scopeRef.current;
    const currentSnapshot = snapshotRef.current;
    assertSemanticImportContext(
      'Semantic merge',
      request.scope,
      request.expectedChangeSetETag,
      currentScope,
      currentSessionRef.current,
      currentSnapshot,
      externalBusyRef.current,
      Boolean(semanticMergeBridge)
    );
    const response = await semanticMergeBridge!.importSemanticMerge(request);
    if (
      response.proposalId !== request.proposalId ||
      response.proposalFingerprint !== request.proposalFingerprint ||
      !sameSemanticRevision(response.revision, request.expectedRevision)
    ) {
      throw new Error('The imported semantic merge no longer matches its reviewed proposal.');
    }
    const responseSnapshot = reconstructDisabledImportSnapshot(
      currentSnapshot!,
      response.receipt
    );
    acceptDisabledImportedSet({
      acceptSnapshot,
      currentScope: currentScope!,
      currentSnapshot: currentSnapshot!,
      expectedChangeSetName: request.changeSetName,
      expectedEdits,
      expectedOwner: 'semantic-merge.v1',
      expectedTag: 'semantic-merge',
      importedChangeSetId: response.importedChangeSetId,
      requestedScopeKey,
      responseSnapshot,
      setSelectedChangeSetId,
      workflowName: 'semantic merge'
    });
    return response;
  }, true), [acceptSnapshot, enqueue, semanticMergeBridge]);

  const importKmRecipe = useCallback((
    request: KmRecipeImportRequest,
    expectedEdits: readonly ExpectedImportedScalarEdit[]
  ) => enqueue('operations', async (requestedScopeKey) => {
    const currentScope = scopeRef.current;
    const currentSnapshot = snapshotRef.current;
    assertSemanticImportContext(
      'Recipe',
      request.scope,
      request.expectedChangeSetETag,
      currentScope,
      currentSessionRef.current,
      currentSnapshot,
      externalBusyRef.current,
      Boolean(semanticMergeBridge)
    );
    const response = await semanticMergeBridge!.importKmRecipe(request);
    if (
      response.recipeInstanceId !== request.recipeInstanceId ||
      response.recipeFingerprint !== request.recipeFingerprint ||
      response.proposalId !== request.proposalId ||
      response.proposalFingerprint !== request.proposalFingerprint ||
      !sameSemanticRevision(response.revision, request.expectedRevision)
    ) {
      throw new Error('The imported recipe no longer matches its reviewed proposal.');
    }
    const responseSnapshot = reconstructDisabledImportSnapshot(
      currentSnapshot!,
      response.receipt
    );
    acceptDisabledImportedSet({
      acceptSnapshot,
      currentScope: currentScope!,
      currentSnapshot: currentSnapshot!,
      expectedChangeSetName: request.changeSetName,
      expectedEdits,
      expectedOwner: 'km-recipe.v1',
      expectedTag: 'recipe',
      importedChangeSetId: response.importedChangeSetId,
      requestedScopeKey,
      responseSnapshot,
      setSelectedChangeSetId,
      workflowName: 'recipe'
    });
    return response;
  }, true), [acceptSnapshot, enqueue, semanticMergeBridge]);

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

  const runControllerAction = useCallback(async (operation: () => Promise<unknown>) => {
    if (
      controllerActionRef.current !== null ||
      externalBusyRef.current ||
      !operationAdmissionRef.current()
    ) {
      return false;
    }
    const action = {};
    controllerActionRef.current = action;
    try {
      await operation();
      return true;
    } catch (error) {
      reportError(error);
      return false;
    } finally {
      if (controllerActionRef.current === action) {
        controllerActionRef.current = null;
      }
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
      const requestedScopeKey = scopeKeyRef.current;
      const priorIds = new Set(
        snapshotRef.current?.document.changeSets.map((set) => set.changeSetId) ?? []
      );
      const next = await mutate('create', { kind: 'createSet', name });
      if (requestedScopeKey === null || requestedScopeKey !== scopeKeyRef.current) {
        return;
      }
      const created = next.document.changeSets.find((set) => !priorIds.has(set.changeSetId));
      if (!created) {
        throw new Error('The change-set create response did not contain the new change set.');
      }
      setSelectedChangeSetId(created.changeSetId);
    }),
    onCreateBuildVariant: (name, enabledChangeSetIds, outputMode, outputProfileId) => {
      const variantId = createStableId('variant');
      return runControllerAction(async () => {
        const next = await mutate('variant', {
          kind: 'createVariant',
          variant: {
            changeSetIds: [...enabledChangeSetIds],
            createdAtUtc: new Date().toISOString(),
            name,
            outputMode: parseOutputMode(outputMode),
            outputProfileId,
            updatedAtUtc: new Date().toISOString(),
            variantId
          }
        });
        if (!next.document.buildVariants.some((variant) => variant.variantId === variantId)) {
          throw new Error('The build-variant create response did not contain the new variant.');
        }
      });
    },
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
       async (requestedScopeKey) => {
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
        if (requestedScopeKey !== scopeKeyRef.current) return response;
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
        if (externalBusyRef.current) {
          throw new Error('Change sets cannot be imported during another project write.');
        }
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
      },
      true
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
    importGuidedDesignProposal,
    importKmRecipe,
    importSemanticMerge,
    isScopeBlockingOperationInFlight: () => queuedOperationTokensRef.current.size > 0,
    materialize,
    mutateHistory,
    removeOperation,
    refresh: readWorkspace,
    snapshot
  };
}

function assertSemanticImportContext(
  workflowName: string,
  requestScope: SemanticExploreScope,
  expectedETag: string | null,
  currentScope: ChangeSetWorkspaceScope | null,
  currentSession: EditSession | null,
  currentSnapshot: ChangeSetWorkspaceSnapshot | null,
  externalBusy: boolean,
  hasBridge: boolean
) {
  if (externalBusy) {
    throw new Error(`${workflowName} cannot import during another project write.`);
  }
  if (!hasBridge || !currentScope || !currentSnapshot) {
    throw new Error(
      `A loaded change-set workspace is required before importing a ${workflowName.toLowerCase()}.`
    );
  }
  if (
    requestScope.projectId !== currentScope.projectId ||
    JSON.stringify(requestScope.paths) !== JSON.stringify(currentScope.paths) ||
    JSON.stringify(requestScope.pendingSession ?? null) !== JSON.stringify(currentSession)
  ) {
    throw new Error(`The ${workflowName.toLowerCase()} belongs to a different project scope.`);
  }
  if (expectedETag !== currentSnapshot.etag) {
    throw new Error('The change-set workspace changed after the reviewed preview.');
  }
}

function acceptDisabledImportedSet(options: {
  acceptSnapshot: (snapshot: ChangeSetWorkspaceSnapshot, scopeKey: string) => boolean;
  currentScope: ChangeSetWorkspaceScope;
  currentSnapshot: ChangeSetWorkspaceSnapshot;
  expectedChangeSetName: string;
  expectedEdits: readonly ExpectedImportedScalarEdit[];
  expectedOwner: 'semantic-merge.v1' | 'km-recipe.v1';
  expectedTag: 'semantic-merge' | 'recipe';
  importedChangeSetId: string;
  requestedScopeKey: string;
  responseSnapshot: ChangeSetWorkspaceSnapshot;
  setSelectedChangeSetId: (changeSetId: string) => void;
  workflowName: string;
}) {
  const {
    acceptSnapshot,
    currentScope,
    currentSnapshot,
    expectedChangeSetName,
    expectedEdits,
    expectedOwner,
    expectedTag,
    importedChangeSetId,
    requestedScopeKey,
    responseSnapshot,
    setSelectedChangeSetId,
    workflowName
  } = options;
  const priorChangeSetIds = new Set(
    currentSnapshot.document.changeSets.map((changeSet) => changeSet.changeSetId)
  );
  const responseExistingChangeSets = responseSnapshot.document.changeSets.filter(
    (changeSet) => changeSet.changeSetId !== importedChangeSetId
  );
  if (
    responseSnapshot.document.game !== currentScope.paths.selectedGame ||
    responseSnapshot.etag === null ||
    responseSnapshot.etag === currentSnapshot.etag ||
    priorChangeSetIds.has(importedChangeSetId) ||
    responseSnapshot.document.changeSets.length !==
      currentSnapshot.document.changeSets.length + 1 ||
    JSON.stringify(responseExistingChangeSets) !==
      JSON.stringify(currentSnapshot.document.changeSets) ||
    JSON.stringify(responseSnapshot.document.buildVariants) !==
      JSON.stringify(currentSnapshot.document.buildVariants) ||
    responseSnapshot.document.activeChangeSetId !==
      currentSnapshot.document.activeChangeSetId ||
    responseSnapshot.document.activeBuildVariantId !==
      currentSnapshot.document.activeBuildVariantId
  ) {
    throw new Error(
      `The imported ${workflowName} response changed unrelated workspace state.`
    );
  }
  const imported = responseSnapshot.document.changeSets.find(
    (changeSet) => changeSet.changeSetId === importedChangeSetId
  );
  const importedEditKeys = imported?.operations.map((operation) => JSON.stringify([
    operation.pendingEdit.domain,
    operation.pendingEdit.recordId,
    operation.pendingEdit.field,
    operation.pendingEdit.newValue
  ])).sort() ?? [];
  const expectedEditKeys = expectedEdits.map((edit) => JSON.stringify([
    edit.domain,
    edit.recordId,
    edit.field,
    edit.newValue
  ])).sort();
  if (
    !imported ||
    imported.enabled ||
    imported.archived ||
    responseSnapshot.document.activeChangeSetId === importedChangeSetId ||
    imported.name !== expectedChangeSetName ||
    containsPrivatePathSignature(imported.name) ||
    imported.notes !== null ||
    imported.dependencyIds.length !== 0 ||
    JSON.stringify(imported.tags) !== JSON.stringify([expectedTag]) ||
    expectedEdits.length < 1 ||
    new Set(expectedEditKeys).size !== expectedEditKeys.length ||
    JSON.stringify(importedEditKeys) !== JSON.stringify(expectedEditKeys) ||
    imported.operations.some((operation) => (
      operation.sourceBindingKind !== 'reviewedPlan' ||
      operation.ownedTargets.length === 0 ||
      operation.pendingEdit.owner !== expectedOwner ||
      containsPrivatePathSignature(operation.pendingEdit.summary)
    ))
  ) {
    throw new Error(`The imported ${workflowName} change set is not safely disabled.`);
  }
  if (!acceptSnapshot(responseSnapshot, requestedScopeKey)) {
    throw new Error(`The project scope changed before the ${workflowName} import completed.`);
  }
  setSelectedChangeSetId(importedChangeSetId);
}

function reconstructDisabledImportSnapshot(
  currentSnapshot: ChangeSetWorkspaceSnapshot,
  receipt: {
    canRedo: boolean;
    canUndo: boolean;
    document: ChangeSetWorkspaceSnapshot['document'];
    etag: string;
    redoLabel: string | null;
    undoLabel: string | null;
  }
): ChangeSetWorkspaceSnapshot {
  const currentSession = currentSnapshot.effective.session;
  const currentBinding = currentSession?.authoringBinding;
  const session = currentSession && currentBinding
    ? {
        ...currentSession,
        authoringBinding: {
          ...currentBinding,
          workspaceETag: receipt.etag
        }
      }
    : currentSession;
  return {
    canRedo: receipt.canRedo,
    canUndo: receipt.canUndo,
    document: receipt.document,
    effective: {
      ...currentSnapshot.effective,
      session
    },
    etag: receipt.etag,
    redoLabel: receipt.redoLabel,
    undoLabel: receipt.undoLabel
  };
}

function containsPrivatePathSignature(value: string) {
  let candidate = value;
  for (let depth = 0; depth <= 3; depth += 1) {
    if (
      candidate.includes('/') ||
      candidate.includes('\\') ||
      /(?:^|[^A-Za-z0-9])[A-Za-z]:/u.test(candidate) ||
      /(?:^|[^A-Za-z0-9])file:/iu.test(candidate) ||
      /(?:^|[^A-Za-z0-9])~/u.test(candidate)
    ) return true;
    if (depth === 3 || !candidate.includes('%')) break;
    try {
      const decoded = decodeURIComponent(candidate);
      if (decoded === candidate) break;
      candidate = decoded;
    } catch {
      return true;
    }
  }
  return false;
}

function sameSemanticRevision(
  left: SemanticExploreRevision,
  right: SemanticExploreRevision
) {
  return left.projectId === right.projectId &&
    left.gameFamily === right.gameFamily &&
    left.generation === right.generation &&
    left.fingerprint === right.fingerprint;
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
