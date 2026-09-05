/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { type ApiDiagnostic } from '../../bridge/contracts';
import {
  type ApplyOutputCleanupResponse,
  type BuildSupportReportResponse,
  type ListOutputCheckpointsResponse,
  type ListOutputHistoryResponse,
  type OutputCheckpoint,
  type OutputRecoveryStatus,
  type OutputSafetyScope,
  type PreviewOutputCheckpointRestoreResponse,
  type PreviewOutputCleanupResponse,
  type RestoreOutputCheckpointResponse,
  type ScanOutputIntegrityResponse
} from '../../bridge/outputSafetyContracts';
import { type ProjectBridge } from '../../bridge/projectBridge';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import { projectBridgeErrorCodes } from '../../errorCodes';
import { useLocalization } from '../../localization';
import { toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';

export type OutputSafetyReadiness =
  | 'unavailable'
  | 'checking'
  | 'ready'
  | 'blocked'
  | 'error';

export type OutputSafetyAction =
  | 'refresh'
  | 'reconcile'
  | 'scan'
  | 'cleanupPreview'
  | 'cleanupApply'
  | 'loadActivity'
  | 'checkpointCreate'
  | 'checkpointRestorePreview'
  | 'checkpointRestore'
  | 'checkpointDelete'
  | 'supportReport';

export type OutputSafetyController = {
  actionDiagnostics: ApiDiagnostic[];
  applyCleanup: () => Promise<void>;
  buildSupportReport: () => Promise<void>;
  busyAction: OutputSafetyAction | null;
  canApply: boolean;
  canMutate: boolean;
  checkpointRestorePreview: PreviewOutputCheckpointRestoreResponse | null;
  checkpoints: ListOutputCheckpointsResponse | null;
  cleanupPreview: PreviewOutputCleanupResponse | null;
  cleanupResult: ApplyOutputCleanupResponse | null;
  createCheckpoint: (label: string | null) => Promise<boolean>;
  deleteCheckpoint: (checkpoint: OutputCheckpoint) => Promise<void>;
  history: ListOutputHistoryResponse | null;
  integrity: ScanOutputIntegrityResponse | null;
  isAvailable: boolean;
  loadActivity: () => Promise<void>;
  loadHistory: () => Promise<void>;
  loadMoreHistory: () => Promise<void>;
  notifyOutputFailure: (error: unknown) => Promise<OutputRecoveryStatus | null>;
  notifyOutputMutation: () => Promise<OutputRecoveryStatus | null>;
  previewCleanup: () => Promise<void>;
  previewCheckpointRestore: (checkpoint: OutputCheckpoint) => Promise<void>;
  readiness: OutputSafetyReadiness;
  reconcileRecovery: () => Promise<void>;
  recoveryStatus: OutputRecoveryStatus | null;
  refreshRecovery: () => Promise<void>;
  restoreCheckpoint: () => Promise<void>;
  restoreResult: RestoreOutputCheckpointResponse | null;
  scanIntegrity: () => Promise<void>;
  supportReport: BuildSupportReportResponse | null;
};

type UseOutputSafetyControllerOptions = {
  armCriticalWriteGuard: () => Promise<boolean>;
  bridge: ProjectBridge;
  externalMutationBusy: boolean;
  onMutationBusyChange: (isBusy: boolean) => void;
  scope: OutputSafetyScope | null;
};

export function useOutputSafetyController({
  armCriticalWriteGuard,
  bridge,
  externalMutationBusy,
  onMutationBusyChange,
  scope
}: UseOutputSafetyControllerOptions): OutputSafetyController {
  const { t } = useLocalization();
  const scopeKey = useMemo(() => (scope ? JSON.stringify(scope) : null), [scope]);
  const scopeKeyRef = useRef(scopeKey);
  scopeKeyRef.current = scopeKey;
  const scopeRef = useRef(scope);
  scopeRef.current = scope;
  const externalMutationBusyRef = useRef(externalMutationBusy);
  externalMutationBusyRef.current = externalMutationBusy;
  const generationRef = useRef(0);
  const recoveryRequestRef = useRef(0);
  const safetyEpochRef = useRef(0);
  const [readiness, setReadiness] = useState<OutputSafetyReadiness>('unavailable');
  const [recoveryStatus, setRecoveryStatus] = useState<OutputRecoveryStatus | null>(null);
  const [integrity, setIntegrity] = useState<ScanOutputIntegrityResponse | null>(null);
  const [cleanupPreview, setCleanupPreview] = useState<PreviewOutputCleanupResponse | null>(null);
  const [cleanupResult, setCleanupResult] = useState<ApplyOutputCleanupResponse | null>(null);
  const [history, setHistory] = useState<ListOutputHistoryResponse | null>(null);
  const [checkpoints, setCheckpoints] = useState<ListOutputCheckpointsResponse | null>(null);
  const [checkpointRestorePreview, setCheckpointRestorePreview] =
    useState<PreviewOutputCheckpointRestoreResponse | null>(null);
  const [restoreResult, setRestoreResult] = useState<RestoreOutputCheckpointResponse | null>(null);
  const [supportReport, setSupportReport] = useState<BuildSupportReportResponse | null>(null);
  const [actionDiagnostics, setActionDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [busyAction, setBusyAction] = useState<OutputSafetyAction | null>(null);
  const busyActionRef = useRef<OutputSafetyAction | null>(null);
  const mutationBusyRef = useRef(false);
  const armCriticalWriteGuardRef = useRef(armCriticalWriteGuard);
  armCriticalWriteGuardRef.current = armCriticalWriteGuard;
  const onMutationBusyChangeRef = useRef(onMutationBusyChange);
  onMutationBusyChangeRef.current = onMutationBusyChange;

  const setMutationBusy = useCallback((isBusy: boolean) => {
    if (mutationBusyRef.current === isBusy) return;
    mutationBusyRef.current = isBusy;
    onMutationBusyChangeRef.current(isBusy);
  }, []);

  useEffect(() => () => {
    generationRef.current += 1;
    recoveryRequestRef.current += 1;
    safetyEpochRef.current += 1;
    busyActionRef.current = null;
    setMutationBusy(false);
  }, [setMutationBusy]);

  const beginAction = useCallback((action: OutputSafetyAction) => {
    if (busyActionRef.current !== null || (
      externalMutationBusyRef.current && outputSafetyMutationActions.has(action)
    )) {
      return false;
    }
    busyActionRef.current = action;
    setBusyAction(action);
    if (outputSafetyMutationActions.has(action)) {
      setMutationBusy(true);
    }
    return true;
  }, [setMutationBusy]);

  const endAction = useCallback((action: OutputSafetyAction) => {
    if (busyActionRef.current === action) {
      busyActionRef.current = null;
      setBusyAction(null);
      if (outputSafetyMutationActions.has(action)) {
        setMutationBusy(false);
      }
    }
  }, [setMutationBusy]);

  const isCurrent = useCallback((generation: number, requestScopeKey: string) => {
    return generationRef.current === generation && scopeKeyRef.current === requestScopeKey;
  }, []);

  const commitRecovery = useCallback((status: OutputRecoveryStatus) => {
    setRecoveryStatus(status);
    setReadiness(
      status.requiresRecovery || status.pendingReconciliationCount > 0
        ? 'blocked'
        : 'ready'
    );
  }, []);

  const refreshRecovery = useCallback(async (preserveActionDiagnostics = false) => {
    if (!scope || !scopeKey) {
      return;
    }

    if (!beginAction('refresh')) {
      return;
    }

    const generation = generationRef.current;
    const safetyEpoch = safetyEpochRef.current + 1;
    safetyEpochRef.current = safetyEpoch;
    const requestId = recoveryRequestRef.current + 1;
    recoveryRequestRef.current = requestId;
    setReadiness((current) => current === 'ready' || current === 'blocked' ? current : 'checking');
    if (!preserveActionDiagnostics) {
      setActionDiagnostics([]);
    }
    try {
      const response = await bridge.getOutputRecoveryStatus({ scope });
      if (isCurrent(generation, scopeKey)
          && recoveryRequestRef.current === requestId
          && safetyEpochRef.current === safetyEpoch) {
        commitRecovery(response.status);
      }
    } catch (error) {
      if (isCurrent(generation, scopeKey)
          && recoveryRequestRef.current === requestId
          && safetyEpochRef.current === safetyEpoch) {
        const diagnostics = toProjectBridgeDiagnostics(
          error,
          t('outputSafety.error.recoveryCheck')
        );
        if (diagnostics.length > 0) {
          setActionDiagnostics((current) => preserveActionDiagnostics
            ? [...current, ...diagnostics]
            : diagnostics);
          setReadiness('error');
        }
      }
    } finally {
      if (isCurrent(generation, scopeKey) && recoveryRequestRef.current === requestId) {
        endAction('refresh');
      }
    }
  }, [beginAction, bridge, commitRecovery, endAction, isCurrent, scope, scopeKey, t]);

  useEffect(() => {
    generationRef.current += 1;
    recoveryRequestRef.current += 1;
    safetyEpochRef.current += 1;
    setRecoveryStatus(null);
    setIntegrity(null);
    setCleanupPreview(null);
    setCleanupResult(null);
    setHistory(null);
    setCheckpoints(null);
    setCheckpointRestorePreview(null);
    setRestoreResult(null);
    setSupportReport(null);
    setActionDiagnostics([]);
    busyActionRef.current = null;
    setMutationBusy(false);
    setBusyAction(null);
    setReadiness(scope ? 'checking' : 'unavailable');
    if (scope) {
      void refreshRecovery();
    }
  }, [refreshRecovery, scopeKey, setMutationBusy]);

  const runScopedAction = useCallback(async <T,>(
    action: OutputSafetyAction,
    operation: (activeScope: OutputSafetyScope) => Promise<T>,
    commit: (result: T) => void | Promise<void>,
    fallbackMessage: string
  ) => {
    const activeScope = scopeRef.current;
    if (!activeScope || !scopeKey || !beginAction(action)) {
      return false;
    }

    const generation = generationRef.current;
    const isMutation = outputSafetyMutationActions.has(action);
    setActionDiagnostics([]);
    if (isMutation) {
      let didArmGuard = false;
      try {
        didArmGuard = await armCriticalWriteGuardRef.current();
      } catch {
        didArmGuard = false;
      }
      if (
        !didArmGuard ||
        !isCurrent(generation, scopeKey) ||
        busyActionRef.current !== action
      ) {
        if (isCurrent(generation, scopeKey) && busyActionRef.current === action) {
          endAction(action);
        }
        return false;
      }
    }
    const safetyEpoch = isMutation
      ? safetyEpochRef.current + 1
      : safetyEpochRef.current;
    safetyEpochRef.current = safetyEpoch;
    if (isMutation) {
      setReadiness('checking');
      setSupportReport(null);
    }
    let shouldRefreshReadiness = false;
    try {
      const result = await operation(activeScope);
      if (isCurrent(generation, scopeKey) && safetyEpochRef.current === safetyEpoch) {
        await commit(result);
        return true;
      }
      return false;
    } catch (error) {
      if (isCurrent(generation, scopeKey) && safetyEpochRef.current === safetyEpoch) {
        setActionDiagnostics(toProjectBridgeDiagnostics(error, fallbackMessage));
        const ambiguousMutationFailure = outputSafetyMutationActions.has(action);
        if (ambiguousMutationFailure || invalidatesOutputSafetyReadiness(error)) {
          safetyEpochRef.current += 1;
          setReadiness('checking');
          shouldRefreshReadiness = true;
          if (ambiguousMutationFailure) {
            setIntegrity(null);
            setCleanupPreview(null);
            setHistory(null);
            setCheckpoints(null);
            setCheckpointRestorePreview(null);
          }
        }
      }
      return false;
    } finally {
      if (isCurrent(generation, scopeKey)) {
        endAction(action);
        if (shouldRefreshReadiness) {
          await refreshRecovery(true);
        }
      }
    }
  }, [beginAction, endAction, isCurrent, refreshRecovery, scopeKey]);

  const reconcileRecovery = useCallback(async () => {
    if (
      !recoveryStatus ||
      recoveryStatus.requiresRecovery ||
      recoveryStatus.pendingReconciliationCount === 0
    ) {
      return;
    }
    await runScopedAction('reconcile', async (activeScope) => {
      const response = await bridge.reconcileOutputRecovery({
        expectedRevision: recoveryStatus.revision,
        scope: activeScope
      });
      const [nextHistory, nextCheckpoints] = await Promise.all([
        bridge.listOutputHistory({ cursor: null, limit: 20, scope: activeScope }),
        bridge.listOutputCheckpoints({ scope: activeScope })
      ]);
      return { nextCheckpoints, nextHistory, response };
    }, ({ nextCheckpoints, nextHistory, response }) => {
      commitRecovery(response.status);
      setIntegrity(null);
      setCleanupPreview(null);
      setCheckpointRestorePreview(null);
      setHistory(nextHistory);
      setCheckpoints(nextCheckpoints);
    }, t('outputSafety.error.reconcile'));
  }, [bridge, commitRecovery, recoveryStatus, runScopedAction, t]);

  const scanIntegrity = useCallback(async () => {
    await runScopedAction('scan', (activeScope) =>
      bridge.scanOutputIntegrity({ scope: activeScope }), (response) => {
      setIntegrity(response);
      setCleanupPreview(null);
      setCleanupResult(null);
    }, t('outputSafety.error.integrityScan'));
  }, [bridge, runScopedAction, t]);

  const previewCleanup = useCallback(async () => {
    if (!integrity) {
      return;
    }
    const targetIds = integrity.entries
      .filter((entry) => entry.cleanupEligible)
      .map((entry) => entry.targetId);
    if (targetIds.length === 0) {
      setCleanupPreview(null);
      return;
    }
    await runScopedAction('cleanupPreview', (activeScope) =>
      bridge.previewOutputCleanup({
        integrityRevision: integrity.revision,
        scanId: integrity.scanId,
        scope: activeScope,
        targetIds
      }), (response) => {
      setCleanupPreview(response);
      setCleanupResult(null);
    }, t('outputSafety.error.cleanupPreview'));
  }, [bridge, integrity, runScopedAction, t]);

  const loadHistory = useCallback(async () => {
    await runScopedAction('loadActivity', activeScope =>
      bridge.listOutputHistory({ cursor: null, limit: 20, scope: activeScope }),
      setHistory, t('outputSafety.error.activityLoad'));
  }, [bridge, runScopedAction, t]);

  const loadActivity = useCallback(async () => {
    await runScopedAction('loadActivity', (activeScope) => Promise.all([
        bridge.listOutputHistory({ cursor: null, limit: 20, scope: activeScope }),
        bridge.listOutputCheckpoints({ scope: activeScope })
      ]), ([nextHistory, nextCheckpoints]) => {
      setHistory(nextHistory);
      setCheckpoints(nextCheckpoints);
    }, t('outputSafety.error.activityLoad'));
  }, [bridge, runScopedAction, t]);

  const loadMoreHistory = useCallback(async () => {
    if (!history?.nextCursor) {
      return;
    }
    const cursor = history.nextCursor;
    await runScopedAction('loadActivity', (activeScope) =>
      bridge.listOutputHistory({ cursor, limit: 20, scope: activeScope }), (response) => {
      setHistory((current) => {
        if (!current || current.nextCursor !== cursor) {
          return current;
        }
        const knownIds = new Set(current.receipts.map((receipt) => receipt.transactionId));
        return {
          nextCursor: response.nextCursor,
          receipts: [
            ...current.receipts,
            ...response.receipts.filter((receipt) => !knownIds.has(receipt.transactionId))
          ],
          truncated: response.truncated
        };
      });
    }, t('outputSafety.error.activityLoad'));
  }, [bridge, history, runScopedAction, t]);

  const notifyOutputMutation = useCallback(async () => {
    const activeScope = scopeRef.current;
    if (!activeScope || !scopeKey) {
      return null;
    }
    const generation = generationRef.current;
    const safetyEpoch = safetyEpochRef.current + 1;
    safetyEpochRef.current = safetyEpoch;
    setReadiness('checking');
    setIntegrity(null);
    setCleanupPreview(null);
    setHistory(null);
    setCheckpoints(null);
    setCheckpointRestorePreview(null);
    setSupportReport(null);
    setActionDiagnostics([]);
    const results = await Promise.allSettled([
      bridge.getOutputRecoveryStatus({ scope: activeScope }),
      bridge.listOutputHistory({ cursor: null, limit: 20, scope: activeScope }),
      bridge.listOutputCheckpoints({ scope: activeScope })
    ]);
    if (!isCurrent(generation, scopeKey) || safetyEpochRef.current !== safetyEpoch) {
      return null;
    }
    const [recoveryResult, historyResult, checkpointResult] = results;
    let refreshedRecovery: OutputRecoveryStatus | null = null;
    if (recoveryResult.status === 'fulfilled') {
      commitRecovery(recoveryResult.value.status);
      refreshedRecovery = recoveryResult.value.status;
    } else {
      setReadiness('error');
      setActionDiagnostics(toProjectBridgeDiagnostics(
        recoveryResult.reason,
        t('outputSafety.error.recoveryRefresh')
      ));
    }
    if (historyResult.status === 'fulfilled') {
      setHistory(historyResult.value);
    }
    if (checkpointResult.status === 'fulfilled') {
      setCheckpoints(checkpointResult.value);
    }
    return refreshedRecovery;
  }, [bridge, commitRecovery, isCurrent, scopeKey, t]);

  const notifyOutputFailure = useCallback(async (_error: unknown) => {
    const activeScope = scopeRef.current;
    if (!activeScope || !scopeKey) {
      return null;
    }
    const generation = generationRef.current;
    const safetyEpoch = safetyEpochRef.current + 1;
    safetyEpochRef.current = safetyEpoch;
    const requestId = recoveryRequestRef.current + 1;
    recoveryRequestRef.current = requestId;
    setReadiness('checking');
    setIntegrity(null);
    setCleanupPreview(null);
    setHistory(null);
    setCheckpoints(null);
    setCheckpointRestorePreview(null);
    setSupportReport(null);
    setActionDiagnostics([]);
    try {
      const response = await bridge.getOutputRecoveryStatus({ scope: activeScope });
      if (isCurrent(generation, scopeKey)
          && recoveryRequestRef.current === requestId
          && safetyEpochRef.current === safetyEpoch) {
        commitRecovery(response.status);
        return response.status;
      }
    } catch (error) {
      if (isCurrent(generation, scopeKey)
          && recoveryRequestRef.current === requestId
          && safetyEpochRef.current === safetyEpoch) {
        setActionDiagnostics(toProjectBridgeDiagnostics(
          error,
          t('outputSafety.error.recoveryRefresh')
        ));
        setReadiness('error');
      }
    }
    return null;
  }, [bridge, commitRecovery, isCurrent, scopeKey, t]);

  const applyCleanup = useCallback(async () => {
    if (!cleanupPreview) {
      return;
    }
    await runScopedAction('cleanupApply', (activeScope) =>
      bridge.applyOutputCleanup({
        expectedRevision: cleanupPreview.expectedRevision,
        planId: cleanupPreview.planId,
        scope: activeScope
      }), async (response) => {
      setCleanupResult(response);
      setCleanupPreview(null);
      await notifyOutputMutation();
    }, t('outputSafety.error.cleanupApply'));
  }, [bridge, cleanupPreview, notifyOutputMutation, runScopedAction, t]);

  const createCheckpoint = useCallback(async (label: string | null) => {
    if (!checkpoints) {
      return false;
    }
    return await runScopedAction('checkpointCreate', (activeScope) =>
      bridge.createOutputCheckpoint({
        expectedOutputRevision: checkpoints.outputRevision,
        label: label && label.trim().length > 0 ? label.trim() : null,
        scope: activeScope
      }), async (response) => {
      setCheckpoints((current) => current ? {
        checkpoints: response.checkpoints,
        outputRevision: response.outputRevision,
        revision: response.revision
      } : current);
      await notifyOutputMutation();
    }, t('outputSafety.error.checkpointCreate'));
  }, [bridge, checkpoints, notifyOutputMutation, runScopedAction, t]);

  const previewCheckpointRestore = useCallback(async (checkpoint: OutputCheckpoint) => {
    await runScopedAction('checkpointRestorePreview', (activeScope) =>
      bridge.previewOutputCheckpointRestore({
        checkpointId: checkpoint.checkpointId,
        manifestFingerprint: checkpoint.manifestFingerprint,
        scope: activeScope
      }), (response) => {
      setCheckpointRestorePreview(response);
      setRestoreResult(null);
    }, t('outputSafety.error.checkpointPreview'));
  }, [bridge, runScopedAction, t]);

  const restoreCheckpoint = useCallback(async () => {
    if (!checkpointRestorePreview?.canRestore) {
      return;
    }
    await runScopedAction('checkpointRestore', (activeScope) =>
      bridge.restoreOutputCheckpoint({
        planId: checkpointRestorePreview.planId,
        scope: activeScope
      }), async (response) => {
      setRestoreResult(response);
      setCheckpointRestorePreview(null);
      await notifyOutputMutation();
    }, t('outputSafety.error.checkpointRestore'));
  }, [bridge, checkpointRestorePreview, notifyOutputMutation, runScopedAction, t]);

  const deleteCheckpoint = useCallback(async (checkpoint: OutputCheckpoint) => {
    if (!checkpoints) {
      return;
    }
    await runScopedAction('checkpointDelete', (activeScope) =>
      bridge.deleteOutputCheckpoint({
        checkpointId: checkpoint.checkpointId,
        expectedRevision: checkpoints.revision,
        manifestFingerprint: checkpoint.manifestFingerprint,
        scope: activeScope
      }), async (response) => {
      setCheckpoints((current) => current ? {
        checkpoints: response.deleted
          ? current.checkpoints.filter((entry) => entry.checkpointId !== checkpoint.checkpointId)
          : current.checkpoints,
        outputRevision: current.outputRevision,
        revision: response.revision
      } : current);
      await notifyOutputMutation();
    }, t('outputSafety.error.checkpointDelete'));
  }, [bridge, checkpoints, notifyOutputMutation, runScopedAction, t]);

  const buildSupportReport = useCallback(async () => {
    await runScopedAction(
      'supportReport',
      (activeScope) => bridge.buildSupportReport({ scope: activeScope }),
      setSupportReport,
      t('outputSafety.error.supportReport')
    );
  }, [bridge, runScopedAction, t]);

  return {
    actionDiagnostics,
    applyCleanup,
    buildSupportReport,
    busyAction,
    canApply: readiness === 'ready' && busyAction === null && !externalMutationBusy,
    canMutate: busyAction === null && !externalMutationBusy,
    checkpointRestorePreview,
    checkpoints,
    cleanupPreview,
    cleanupResult,
    createCheckpoint,
    deleteCheckpoint,
    history,
    integrity,
    isAvailable: scope !== null,
    loadActivity,
    loadHistory,
    loadMoreHistory,
    notifyOutputFailure,
    notifyOutputMutation,
    previewCleanup,
    previewCheckpointRestore,
    readiness,
    reconcileRecovery,
    recoveryStatus,
    refreshRecovery,
    restoreCheckpoint,
    restoreResult,
    scanIntegrity,
    supportReport
  };
}

const readinessInvalidatingCodes = new Set<string>([
  projectBridgeErrorCodes.outputCheckpointConflict,
  projectBridgeErrorCodes.outputCheckpointNotFound,
  projectBridgeErrorCodes.outputConcurrentModification,
  projectBridgeErrorCodes.outputOwnershipUnproven,
  projectBridgeErrorCodes.outputRecoveryRequired,
  projectBridgeErrorCodes.outputRootBusy
]);

const outputSafetyMutationActions = new Set<OutputSafetyAction>([
  'reconcile',
  'cleanupApply',
  'checkpointCreate',
  'checkpointRestore',
  'checkpointDelete'
]);

function invalidatesOutputSafetyReadiness(error: unknown) {
  if (!(error instanceof ProjectBridgeError)) {
    return false;
  }
  return (
    (error.semanticCode !== null && readinessInvalidatingCodes.has(error.semanticCode)) ||
    error.apiError.diagnostics.some(
      (diagnostic) => diagnostic.code && readinessInvalidatingCodes.has(diagnostic.code)
    )
  );
}
