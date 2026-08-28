/* SPDX-License-Identifier: GPL-3.0-only */

import {
  useLayoutEffect,
  useMemo,
  useRef,
  useSyncExternalStore
} from 'react';
import {
  kmRecipeMaximumOperations,
  kmRecipePackageSchema,
  semanticMergeContractKeys,
  semanticMergeDefaultPageSize,
  semanticMergeMaximumTargetSelectionWindow,
  type KmRecipeExportRequest,
  type KmRecipeExportResponse,
  type KmRecipePreviewResponse,
  type KmRecipeValidateResponse,
  type SemanticMergeCapabilitiesResponse,
  type SemanticMergeConflictResolution,
  type SemanticMergeFieldRef,
  type SemanticMergePreviewResponse,
  type SemanticMergeSource
} from '../../bridge/semanticMergeContracts';
import type { SemanticMergeProjectBridgeApi } from '../../bridge/semanticMergeProjectBridge';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import type {
  SemanticExploreRevision,
  SemanticExploreScope,
  SemanticExploreSourceSnapshot
} from '../../bridge/semanticExploreContracts';
import {
  kmRecipeErrorCodes,
  projectBridgeErrorCodes,
  semanticExploreErrorCodes,
  semanticMergeErrorCodes
} from '../../errorCodes';
import { useDeferredUnmountCleanup } from '../../hooks/useDeferredUnmountCleanup';
import {
  ProjectQueryEpoch,
  runIndependentProjectRead,
  runOrderedProjectOperation,
  type ProjectQueryTicket
} from '../../utils/projectAsyncPolicy';

export type SemanticMergeQueryStatus = 'idle' | 'loading' | 'ready' | 'error';
export type SemanticMergeQueryError =
  | 'concurrentModification'
  | 'cursor'
  | 'invalidQuery'
  | 'limit'
  | 'sourceUnavailable'
  | 'staleProposal'
  | 'unsupported'
  | 'generic';

export type SemanticMergeQueryState<T> = {
  data: T | null;
  error: SemanticMergeQueryError | null;
  status: SemanticMergeQueryStatus;
};

export type SemanticMergePagedState<T> = SemanticMergeQueryState<T> & {
  isAppending: boolean;
};

export type SemanticMergeControllerSnapshot = {
  capabilities: SemanticMergeQueryState<SemanticMergeCapabilitiesResponse>;
  exportRecipe: SemanticMergeQueryState<KmRecipeExportResponse>;
  isQuerying: boolean;
  mergePreview: SemanticMergePagedState<SemanticMergePreviewResponse>;
  recipePreview: SemanticMergePagedState<KmRecipePreviewResponse>;
  recipeValidation: SemanticMergeQueryState<KmRecipeValidateResponse>;
  sourceA: SemanticMergeQueryState<SemanticMergeSource>;
  sourceB: SemanticMergeQueryState<SemanticMergeSource>;
};

export type SemanticMergeController = SemanticMergeControllerSnapshot & {
  cancelAll: () => void;
  clearExport: () => void;
  clearRecipe: () => void;
  clearSource: (slot: 'a' | 'b') => void;
  ensureCapabilities: () => Promise<void>;
  exportSelectedRecipe: (request: Omit<
    KmRecipeExportRequest,
    'expectedChangeSetETag' | 'expectedRevision' | 'scope'
  >) => Promise<void>;
  loadMoreMerge: () => Promise<void>;
  loadMoreRecipe: () => Promise<void>;
  openSource: (slot: 'a' | 'b', externalRootPath: string) => Promise<void>;
  previewMerge: (
    targets: readonly SemanticMergeFieldRef[],
    resolutions: readonly SemanticMergeConflictResolution[],
    targetSearchText?: string | null
  ) => Promise<void>;
  previewRecipe: () => Promise<void>;
  validateRecipe: (content: string) => Promise<void>;
};

type Flow =
  | 'capabilities'
  | 'export'
  | 'merge'
  | 'recipePreview'
  | 'recipeValidation'
  | 'sourceA'
  | 'sourceB';

type RequestToken = ProjectQueryTicket<Flow> & { id: number };

const idleQuery = <T,>(): SemanticMergeQueryState<T> => ({
  data: null,
  error: null,
  status: 'idle'
});

const idlePage = <T,>(): SemanticMergePagedState<T> => ({
  ...idleQuery<T>(),
  isAppending: false
});

class SemanticMergeControllerStore {
  private activeRequests = new Map<number, Flow>();
  private bridge: SemanticMergeProjectBridgeApi;
  private contextKey: string | null = null;
  private expectedChangeSetETag: string | null = null;
  private freshness = new ProjectQueryEpoch<Flow>();
  private isAuthoringContextReady = false;
  private listeners = new Set<() => void>();
  private nextRequestId = 1;
  private onStaleRevision: (() => void) | null = null;
  private revision: SemanticExploreRevision | null = null;
  private scope: SemanticExploreScope | null = null;
  private snapshot: SemanticMergeControllerSnapshot = {
    capabilities: idleQuery<SemanticMergeCapabilitiesResponse>(),
    exportRecipe: idleQuery<KmRecipeExportResponse>(),
    isQuerying: false,
    mergePreview: idlePage<SemanticMergePreviewResponse>(),
    recipePreview: idlePage<KmRecipePreviewResponse>(),
    recipeValidation: idleQuery<KmRecipeValidateResponse>(),
    sourceA: idleQuery<SemanticMergeSource>(),
    sourceB: idleQuery<SemanticMergeSource>()
  };

  public constructor(bridge: SemanticMergeProjectBridgeApi) {
    this.bridge = bridge;
  }

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public readonly getSnapshot = () => this.snapshot;

  public setBridge(bridge: SemanticMergeProjectBridgeApi) {
    if (bridge === this.bridge) return;
    this.bridge = bridge;
    this.reset();
  }

  public setOnStaleRevision(callback: (() => void) | undefined) {
    this.onStaleRevision = callback ?? null;
  }

  public setAuthoringContextReady(isReady: boolean) {
    if (isReady === this.isAuthoringContextReady) return;
    this.isAuthoringContextReady = isReady;
    if (!isReady) this.resetAuthoringFlows();
  }

  public setContext(options: {
    authoringContextRevision: string | null;
    expectedChangeSetETag: string | null;
    revision: SemanticExploreRevision | null;
    scope: SemanticExploreScope | null;
  }) {
    const nextKey = contextIdentity(options);
    if (nextKey === this.contextKey) return;
    this.contextKey = nextKey;
    this.expectedChangeSetETag = options.expectedChangeSetETag;
    this.revision = options.revision;
    this.scope = options.scope;
    this.reset();
  }

  public cancelAll = () => {
    this.freshness.invalidateAll();
    this.activeRequests.clear();
    this.snapshot = {
      ...this.snapshot,
      capabilities: settleAfterCancel(this.snapshot.capabilities),
      exportRecipe: settleAfterCancel(this.snapshot.exportRecipe),
      isQuerying: false,
      mergePreview: {
        ...settleAfterCancel(this.snapshot.mergePreview),
        isAppending: false
      },
      recipePreview: {
        ...settleAfterCancel(this.snapshot.recipePreview),
        isAppending: false
      },
      recipeValidation: settleAfterCancel(this.snapshot.recipeValidation),
      sourceA: settleAfterCancel(this.snapshot.sourceA),
      sourceB: settleAfterCancel(this.snapshot.sourceB)
    };
    this.emit();
  };

  public clearExport = () => {
    this.supersede('export');
    this.snapshot = { ...this.snapshot, exportRecipe: idleQuery<KmRecipeExportResponse>() };
    this.emit();
  };

  public clearRecipe = () => {
    this.supersede('recipeValidation');
    this.supersede('recipePreview');
    this.snapshot = {
      ...this.snapshot,
      recipePreview: idlePage<KmRecipePreviewResponse>(),
      recipeValidation: idleQuery<KmRecipeValidateResponse>()
    };
    this.emit();
  };

  public clearSource = (slot: 'a' | 'b') => {
    this.supersede(slot === 'a' ? 'sourceA' : 'sourceB');
    this.invalidateMerge();
    this.snapshot = {
      ...this.snapshot,
      [slot === 'a' ? 'sourceA' : 'sourceB']: idleQuery<SemanticMergeSource>()
    };
    this.emit();
  };

  public ensureCapabilities = async () => {
    if (
      this.snapshot.capabilities.status === 'loading' ||
      this.snapshot.capabilities.status === 'ready'
    ) return;
    const token = this.begin('capabilities');
    this.snapshot = {
      ...this.snapshot,
      capabilities: { data: null, error: null, status: 'loading' }
    };
    this.emit();
    try {
      const scope = this.requireScope();
      const request = { scope };
      const response = await runIndependentProjectRead(
        'getSemanticMergeCapabilities',
        this.bridge,
        request,
        () => this.bridge.getSemanticMergeCapabilities(request)
      );
      if (!this.isCurrent(token)) return;
      assertCapabilitiesResponse(response, scope, this.revision);
      this.snapshot = {
        ...this.snapshot,
        capabilities: { data: response, error: null, status: 'ready' }
      };
      this.emit();
    } catch (error) {
      this.failQuery(token, 'capabilities', error);
    } finally {
      this.finish(token);
    }
  };

  public openSource = async (slot: 'a' | 'b', externalRootPath: string) => {
    const flow = slot === 'a' ? 'sourceA' : 'sourceB';
    this.supersede(flow);
    this.invalidateMerge();
    const token = this.begin(flow, false);
    this.snapshot = {
      ...this.snapshot,
      [slot === 'a' ? 'sourceA' : 'sourceB']: {
        data: null,
        error: null,
        status: 'loading'
      }
    };
    this.emit();
    try {
      const context = this.requireRevisionContext();
      const request = {
        expectedRevision: context.revision,
        externalRootPath,
        scope: context.scope
      };
      const response = await runOrderedProjectOperation(
        'openSemanticMergeSource',
        this.bridge,
        (bridge) => bridge.openSemanticMergeSource(request)
      );
      if (!this.isCurrent(token)) return;
      assertSourceResponse(response.revision, response.source, context.revision);
      const other = slot === 'a' ? this.snapshot.sourceB.data : this.snapshot.sourceA.data;
      if (other?.instanceId === response.source.instanceId) {
        throw new Error('Semantic merge sources must be distinct.');
      }
      this.snapshot = {
        ...this.snapshot,
        [slot === 'a' ? 'sourceA' : 'sourceB']: {
          data: response.source,
          error: null,
          status: 'ready'
        }
      };
      this.emit();
    } catch (error) {
      this.failQuery(token, slot === 'a' ? 'sourceA' : 'sourceB', error);
    } finally {
      this.finish(token);
    }
  };

  public previewMerge = async (
    targets: readonly SemanticMergeFieldRef[],
    resolutions: readonly SemanticMergeConflictResolution[],
    targetSearchText: string | null = null
  ) => {
    await this.runMergePreview([...targets], [...resolutions], targetSearchText, false);
  };

  public loadMoreMerge = async () => {
    const previous = this.snapshot.mergePreview.data;
    if (!previous?.nextCursor) return;
    await this.runMergePreview(
      previous.normalizedTargets,
      previous.normalizedResolutions,
      previous.normalizedTargetSearchText,
      true
    );
  };

  public validateRecipe = async (content: string) => {
    this.supersede('recipeValidation');
    this.supersede('recipePreview');
    const token = this.begin('recipeValidation', false);
    this.snapshot = {
      ...this.snapshot,
      recipePreview: idlePage<KmRecipePreviewResponse>(),
      recipeValidation: { data: null, error: null, status: 'loading' }
    };
    this.emit();
    try {
      const request = { content };
      const response = await runOrderedProjectOperation(
        'validateKmRecipe',
        this.bridge,
        (bridge) => bridge.validateKmRecipe(request)
      );
      if (!this.isCurrent(token)) return;
      this.snapshot = {
        ...this.snapshot,
        recipeValidation: { data: response, error: null, status: 'ready' }
      };
      this.emit();
    } catch (error) {
      this.failQuery(token, 'recipeValidation', error);
    } finally {
      this.finish(token);
    }
  };

  public previewRecipe = async () => {
    await this.runRecipePreview(false);
  };

  public loadMoreRecipe = async () => {
    if (!this.snapshot.recipePreview.data?.nextCursor) return;
    await this.runRecipePreview(true);
  };

  public exportSelectedRecipe = async (request: Omit<
    KmRecipeExportRequest,
    'expectedChangeSetETag' | 'expectedRevision' | 'scope'
  >) => {
    this.supersede('export');
    const token = this.begin('export', false);
    this.snapshot = {
      ...this.snapshot,
      exportRecipe: { data: null, error: null, status: 'loading' }
    };
    this.emit();
    try {
      const context = this.requireAuthoringContext(true);
      const fullRequest: KmRecipeExportRequest = {
        ...request,
        expectedChangeSetETag: context.expectedChangeSetETag,
        expectedRevision: context.revision,
        scope: context.scope
      };
      const response = await runOrderedProjectOperation(
        'exportKmRecipe',
        this.bridge,
        (bridge) => bridge.exportKmRecipe(fullRequest)
      );
      if (!this.isCurrent(token)) return;
      await assertRecipeExportResponse(response, fullRequest);
      if (!this.isCurrent(token)) return;
      this.snapshot = {
        ...this.snapshot,
        exportRecipe: { data: response, error: null, status: 'ready' }
      };
      this.emit();
    } catch (error) {
      this.failQuery(token, 'exportRecipe', error);
    } finally {
      this.finish(token);
    }
  };

  private async runMergePreview(
    targets: SemanticMergeFieldRef[],
    resolutions: SemanticMergeConflictResolution[],
    targetSearchText: string | null,
    append: boolean
  ) {
    const previous = append ? this.snapshot.mergePreview.data : null;
    if (append && !previous?.nextCursor) return;
    this.supersede('merge');
    const token = this.begin('merge', false);
    this.snapshot = {
      ...this.snapshot,
      mergePreview: {
        data: previous,
        error: null,
        isAppending: append,
        status: 'loading'
      }
    };
    this.emit();
    try {
      const context = this.requireAuthoringContext(false);
      const sourceA = this.snapshot.sourceA.data;
      const sourceB = this.snapshot.sourceB.data;
      const capabilities = this.snapshot.capabilities.data;
      if (!sourceA || !sourceB || !capabilities) {
        throw new Error('Two loaded semantic merge sources and capabilities are required.');
      }
      const requestTargets = previous?.normalizedTargets ?? targets;
      const requestResolutions = previous?.normalizedResolutions ?? resolutions;
      const requestSearch = previous?.normalizedTargetSearchText ?? targetSearchText;
      const request = {
        cursor: previous?.nextCursor ?? null,
        expectedChangeSetETag: context.expectedChangeSetETag,
        expectedRevision: context.revision,
        limit: semanticMergeDefaultPageSize,
        proposalFingerprint: previous?.proposalFingerprint ?? null,
        proposalId: previous?.proposalId ?? null,
        resolutions: requestResolutions,
        scope: context.scope,
        sourceAInstanceId: sourceA.instanceId,
        sourceBInstanceId: sourceB.instanceId,
        targetSearchText: requestSearch,
        targets: requestTargets
      };
      const response = await runOrderedProjectOperation(
        'previewSemanticMerge',
        this.bridge,
        (bridge) => bridge.previewSemanticMerge(request)
      );
      if (!this.isCurrent(token)) return;
      assertMergePreviewResponse({
        capabilities,
        expectedRevision: context.revision,
        previous,
        requestedResolutions: requestResolutions,
        requestedSearch: requestSearch,
        requestedTargets: requestTargets,
        response,
        sourceA,
        sourceB
      });
      const merged = previous ? mergeMergePages(previous, response) : response;
      assertMergeCursor(merged);
      this.snapshot = {
        ...this.snapshot,
        mergePreview: {
          data: merged,
          error: null,
          isAppending: false,
          status: 'ready'
        }
      };
      this.emit();
    } catch (error) {
      this.failQuery(token, 'mergePreview', error, append ? previous : null);
    } finally {
      this.finish(token);
    }
  }

  private async runRecipePreview(append: boolean) {
    const previous = append ? this.snapshot.recipePreview.data : null;
    if (append && !previous?.nextCursor) return;
    this.supersede('recipePreview');
    const token = this.begin('recipePreview', false);
    this.snapshot = {
      ...this.snapshot,
      recipePreview: {
        data: previous,
        error: null,
        isAppending: append,
        status: 'loading'
      }
    };
    this.emit();
    try {
      const context = this.requireAuthoringContext(false);
      const validation = this.snapshot.recipeValidation.data;
      if (!validation) throw new Error('A validated recipe is required before preview.');
      const request = {
        cursor: previous?.nextCursor ?? null,
        expectedChangeSetETag: context.expectedChangeSetETag,
        expectedRevision: context.revision,
        limit: semanticMergeDefaultPageSize,
        proposalFingerprint: previous?.proposalFingerprint ?? null,
        proposalId: previous?.proposalId ?? null,
        recipeFingerprint: validation.recipeFingerprint,
        recipeInstanceId: validation.recipeInstanceId,
        scope: context.scope
      };
      const response = await runOrderedProjectOperation(
        'previewKmRecipe',
        this.bridge,
        (bridge) => bridge.previewKmRecipe(request)
      );
      if (!this.isCurrent(token)) return;
      assertRecipePreviewResponse(
        response,
        validation,
        context.revision,
        context.scope.paths.selectedGame,
        previous
      );
      const merged = previous ? mergeRecipePages(previous, response) : response;
      assertRecipeCursor(merged);
      this.snapshot = {
        ...this.snapshot,
        recipePreview: {
          data: merged,
          error: null,
          isAppending: false,
          status: 'ready'
        }
      };
      this.emit();
    } catch (error) {
      this.failQuery(token, 'recipePreview', error, append ? previous : null);
    } finally {
      this.finish(token);
    }
  }

  private requireScope() {
    if (!this.scope) throw new Error('A semantic project scope is required.');
    return this.scope;
  }

  private requireRevisionContext() {
    if (!this.scope || !this.revision) {
      throw new Error('An exact semantic project revision is required.');
    }
    return { revision: this.revision, scope: this.scope };
  }

  private requireAuthoringContext(requireETag: boolean) {
    if (!this.isAuthoringContextReady) {
      throw new Error('The change-set authoring context is not ready.');
    }
    const context = this.requireRevisionContext();
    if (requireETag && this.expectedChangeSetETag === null) {
      throw new Error('A non-empty change-set workspace is required.');
    }
    return {
      ...context,
      expectedChangeSetETag: this.expectedChangeSetETag as string
    };
  }

  private invalidateMerge() {
    this.supersede('merge');
    this.snapshot = {
      ...this.snapshot,
      mergePreview: idlePage<SemanticMergePreviewResponse>()
    };
  }

  private resetAuthoringFlows() {
    this.supersede('export');
    this.supersede('merge');
    this.supersede('recipePreview');
    this.snapshot = {
      ...this.snapshot,
      exportRecipe: idleQuery<KmRecipeExportResponse>(),
      mergePreview: idlePage<SemanticMergePreviewResponse>(),
      recipePreview: idlePage<KmRecipePreviewResponse>()
    };
    this.emit();
  }

  private reset() {
    this.freshness.invalidateAll();
    this.activeRequests.clear();
    this.snapshot = {
      capabilities: idleQuery<SemanticMergeCapabilitiesResponse>(),
      exportRecipe: idleQuery<KmRecipeExportResponse>(),
      isQuerying: false,
      mergePreview: idlePage<SemanticMergePreviewResponse>(),
      recipePreview: idlePage<KmRecipePreviewResponse>(),
      recipeValidation: idleQuery<KmRecipeValidateResponse>(),
      sourceA: idleQuery<SemanticMergeSource>(),
      sourceB: idleQuery<SemanticMergeSource>()
    };
    this.emit();
  }

  private begin(flow: Flow, supersede = true): RequestToken {
    if (supersede) this.supersede(flow);
    const token = {
      ...this.freshness.capture(flow),
      id: this.nextRequestId++
    };
    this.activeRequests.set(token.id, flow);
    this.updateQuerying();
    return token;
  }

  private supersede(flow: Flow) {
    this.freshness.supersede(flow);
    for (const [id, activeFlow] of this.activeRequests) {
      if (activeFlow === flow) this.activeRequests.delete(id);
    }
    this.updateQuerying();
  }

  private isCurrent(token: RequestToken) {
    return this.freshness.isCurrent(token) &&
      this.activeRequests.has(token.id);
  }

  private finish(token: RequestToken) {
    this.activeRequests.delete(token.id);
    this.updateQuerying(true);
  }

  private updateQuerying(emit = false) {
    const isQuerying = this.activeRequests.size > 0;
    if (isQuerying === this.snapshot.isQuerying) return;
    this.snapshot = { ...this.snapshot, isQuerying };
    if (emit) this.emit();
  }

  private failQuery(
    token: RequestToken,
    target: keyof Pick<
      SemanticMergeControllerSnapshot,
      | 'capabilities'
      | 'exportRecipe'
      | 'mergePreview'
      | 'recipePreview'
      | 'recipeValidation'
      | 'sourceA'
      | 'sourceB'
    >,
    error: unknown,
    retainedData: SemanticMergePreviewResponse | KmRecipePreviewResponse | null = null
  ) {
    if (!this.isCurrent(token)) return;
    const normalized = normalizeError(error);
    if (normalized === 'staleProposal' || isStaleRevisionError(error)) {
      this.onStaleRevision?.();
    }
    const current = this.snapshot[target];
    const next = {
      data: retainedData,
      error: normalized,
      status: 'error' as const,
      ...('isAppending' in current ? { isAppending: false } : {})
    };
    this.snapshot = { ...this.snapshot, [target]: next };
    this.emit();
  }

  private emit() {
    for (const listener of this.listeners) listener();
  }
}

function settleAfterCancel<T>(state: SemanticMergeQueryState<T>): SemanticMergeQueryState<T> {
  return state.data
    ? { data: state.data, error: null, status: 'ready' }
    : idleQuery<T>();
}

function contextIdentity(options: {
  authoringContextRevision: string | null;
  expectedChangeSetETag: string | null;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope | null;
}) {
  return JSON.stringify([
    options.scope,
    options.revision,
    options.expectedChangeSetETag,
    options.authoringContextRevision
  ]);
}

function assertCapabilitiesResponse(
  response: SemanticMergeCapabilitiesResponse,
  scope: SemanticExploreScope,
  expectedRevision: SemanticExploreRevision | null
) {
  if (!expectedRevision || !sameRevision(response.revision, expectedRevision)) {
    throw new Error('Semantic merge capabilities returned a stale project revision.');
  }
  if (response.revision.projectId !== scope.projectId) {
    throw new Error('Semantic merge capabilities returned a different project scope.');
  }
  assertSnapshotsForRevision(response.snapshots, expectedRevision);
}

function assertSourceResponse(
  revision: SemanticExploreRevision,
  source: SemanticMergeSource,
  expectedRevision: SemanticExploreRevision
) {
  if (
    !sameRevision(revision, expectedRevision) ||
    !sameRevision(source.snapshot.revision, expectedRevision) ||
    source.snapshot.layer.kind !== 'comparedMod' ||
    source.snapshot.layer.instanceId !== source.instanceId
  ) {
    throw new Error('Semantic merge source returned a stale or mismatched snapshot.');
  }
}

function assertMergePreviewResponse(options: {
  capabilities: SemanticMergeCapabilitiesResponse;
  expectedRevision: SemanticExploreRevision;
  previous: SemanticMergePreviewResponse | null;
  requestedResolutions: readonly SemanticMergeConflictResolution[];
  requestedSearch: string | null;
  requestedTargets: readonly SemanticMergeFieldRef[];
  response: SemanticMergePreviewResponse;
  sourceA: SemanticMergeSource;
  sourceB: SemanticMergeSource;
}) {
  const {
    capabilities,
    expectedRevision,
    previous,
    requestedResolutions,
    requestedSearch,
    requestedTargets,
    response,
    sourceA,
    sourceB
  } = options;
  if (!sameRevision(response.revision, expectedRevision)) {
    throw new Error('Semantic merge preview returned a stale project revision.');
  }
  assertSnapshot(response.baseSnapshot, expectedRevision, 'base', null);
  assertSnapshot(response.layeredSnapshot, expectedRevision, 'layered', null);
  assertSnapshot(response.pendingSnapshot, expectedRevision, 'pending', null);
  if (
    !sameSnapshot(response.sourceASnapshot, sourceA.snapshot) ||
    !sameSnapshot(response.sourceBSnapshot, sourceB.snapshot)
  ) {
    throw new Error('Semantic merge preview changed an opaque source snapshot.');
  }
  if (JSON.stringify(response.capabilities) !== JSON.stringify(capabilities.capabilities)) {
    throw new Error('Semantic merge preview changed the capability matrix.');
  }
  if (!sameFieldRefSet(response.normalizedTargets, requestedTargets)) {
    throw new Error('Semantic merge preview changed the exact requested targets.');
  }
  if (!sameResolutionSet(response.normalizedResolutions, requestedResolutions)) {
    throw new Error('Semantic merge preview changed the requested conflict resolutions.');
  }
  if (response.normalizedTargetSearchText !== requestedSearch) {
    throw new Error('Semantic merge preview changed the normalized target search.');
  }
  if (response.selectionRequired !== (requestedTargets.length === 0)) {
    throw new Error('Semantic merge preview returned an inconsistent selection state.');
  }
  for (const row of response.rows) {
    if (response.selectionRequired) {
      if (row.selected) throw new Error('Semantic merge discovery selected an unrequested field.');
    } else if (
      !row.selected ||
      !response.normalizedTargets.some((target) => sameFieldRef(target, row.target))
    ) {
      throw new Error('Semantic merge proposal returned a row outside its exact targets.');
    }
  }
  if (previous) assertSameMergePageIdentity(previous, response);
}

function assertSameMergePageIdentity(
  previous: SemanticMergePreviewResponse,
  response: SemanticMergePreviewResponse
) {
  const stableKeys: (keyof SemanticMergePreviewResponse)[] = [
    'revision',
    'queryFingerprint',
    'baseSnapshot',
    'layeredSnapshot',
    'pendingSnapshot',
    'sourceASnapshot',
    'sourceBSnapshot',
    'capabilities',
    'normalizedTargets',
    'normalizedResolutions',
    'normalizedTargetSearchText',
    'targetWindowCapped',
    'totalMatchingTargetCount',
    'authoringContextFingerprint',
    'proposalId',
    'proposalFingerprint',
    'canImport',
    'selectionRequired',
    'totalRowCount',
    'totalConflictCount',
    'totalMutationCount',
    'diagnostics'
  ];
  for (const key of stableKeys) {
    if (JSON.stringify(previous[key]) !== JSON.stringify(response[key])) {
      throw new Error(`Semantic merge continuation changed ${String(key)}.`);
    }
  }
}

function mergeMergePages(
  previous: SemanticMergePreviewResponse,
  response: SemanticMergePreviewResponse
): SemanticMergePreviewResponse {
  if (response.rows.length === 0 && response.nextCursor !== null) {
    throw new Error('Semantic merge continuation made no progress.');
  }
  const rows = [...previous.rows, ...response.rows];
  const conflictIds = rows.flatMap((row) => row.conflicts.map((conflict) => conflict.conflictId));
  if (
    new Set(rows.map((row) => row.rowId)).size !== rows.length ||
    new Set(rows.map((row) => semanticMergeContractKeys.fieldRefKey(row.target))).size !==
      rows.length ||
    new Set(conflictIds).size !== conflictIds.length ||
    rows.length > previous.totalRowCount ||
    rows.length > semanticMergeMaximumTargetSelectionWindow
  ) {
    throw new Error('Semantic merge continuation exceeded its bounded unique result set.');
  }
  return { ...response, rows };
}

function assertMergeCursor(response: SemanticMergePreviewResponse) {
  const rowsRemain = response.rows.length < response.totalRowCount;
  if (response.rows.length === 0 && response.nextCursor !== null) {
    throw new Error('Semantic merge page advertised continuation without progress.');
  }
  if ((response.nextCursor !== null) !== rowsRemain) {
    throw new Error('Semantic merge continuation did not match its advertised row total.');
  }
  if (
    !rowsRemain &&
    response.rows.reduce((sum, row) => sum + row.conflicts.length, 0) !==
      response.totalConflictCount
  ) {
    throw new Error('Semantic merge conflict totals changed across the complete review.');
  }
  if (!rowsRemain) {
    const exactSelectedResolutions = response.rows.flatMap((row) => (
      row.conflicts.flatMap((conflict) => conflict.selectedChoice === null
        ? []
        : [{ choice: conflict.selectedChoice, conflictId: conflict.conflictId }])
    ));
    if (!sameResolutionSet(exactSelectedResolutions, response.normalizedResolutions)) {
      throw new Error('Semantic merge resolutions do not match the complete focused review.');
    }
    const effectiveMutations = response.rows.filter((row) => (
      row.state === 'autoMerged' &&
      row.resultValue !== null &&
      row.pendingValue !== null &&
      !sameScalar(row.resultValue, row.pendingValue)
    )).length;
    if (effectiveMutations !== response.totalMutationCount) {
      throw new Error('Semantic merge mutation totals do not match the complete effective diff.');
    }
  }
}

function assertRecipePreviewResponse(
  response: KmRecipePreviewResponse,
  validation: KmRecipeValidateResponse,
  expectedRevision: SemanticExploreRevision,
  expectedGame: KmRecipeValidateResponse['game'] | null,
  previous: KmRecipePreviewResponse | null
) {
  if (
    !sameRevision(response.revision, expectedRevision) ||
    expectedGame === null ||
    validation.game !== expectedGame ||
    projectGameFamily(validation.game) !== expectedRevision.gameFamily ||
    response.totalCompatibilityCount !== validation.totalOperationCount ||
    response.recipeInstanceId !== validation.recipeInstanceId ||
    response.recipeFingerprint !== validation.recipeFingerprint ||
    JSON.stringify(response.metadata) !== JSON.stringify(validation.metadata)
  ) {
    throw new Error('Recipe preview changed its validated recipe or project revision.');
  }
  assertSnapshot(response.baseSnapshot, expectedRevision, 'base', null);
  assertSnapshot(response.layeredSnapshot, expectedRevision, 'layered', null);
  assertSnapshot(response.pendingSnapshot, expectedRevision, 'pending', null);
  if (previous) {
    const stableKeys: (keyof KmRecipePreviewResponse)[] = [
      'revision',
      'queryFingerprint',
      'baseSnapshot',
      'layeredSnapshot',
      'pendingSnapshot',
      'metadata',
      'recipeInstanceId',
      'recipeFingerprint',
      'authoringContextFingerprint',
      'proposalId',
      'proposalFingerprint',
      'canImport',
      'totalCompatibilityCount',
      'totalMutationCount',
      'diagnostics'
    ];
    for (const key of stableKeys) {
      if (JSON.stringify(previous[key]) !== JSON.stringify(response[key])) {
        throw new Error(`Recipe continuation changed ${String(key)}.`);
      }
    }
  }
}

function projectGameFamily(game: KmRecipeValidateResponse['game']) {
  switch (game) {
    case 'sword':
    case 'shield':
      return 'swordShield' as const;
    case 'scarlet':
    case 'violet':
      return 'scarletViolet' as const;
    case 'za':
      return 'legendsZA' as const;
  }
}

function mergeRecipePages(
  previous: KmRecipePreviewResponse,
  response: KmRecipePreviewResponse
): KmRecipePreviewResponse {
  if (response.compatibility.length === 0 && response.nextCursor !== null) {
    throw new Error('Recipe continuation made no progress.');
  }
  const compatibility = [...previous.compatibility, ...response.compatibility];
  if (
    new Set(compatibility.map((row) => row.rowId)).size !== compatibility.length ||
    new Set(compatibility.map((row) => (
      semanticMergeContractKeys.fieldRefKey(row.target)
    ))).size !== compatibility.length ||
    compatibility.length > previous.totalCompatibilityCount ||
    compatibility.length > kmRecipeMaximumOperations
  ) {
    throw new Error('Recipe continuation exceeded its bounded unique result set.');
  }
  return { ...response, compatibility };
}

function assertRecipeCursor(response: KmRecipePreviewResponse) {
  const rowsRemain = response.compatibility.length < response.totalCompatibilityCount;
  if (response.compatibility.length === 0 && response.nextCursor !== null) {
    throw new Error('Recipe page advertised continuation without progress.');
  }
  if ((response.nextCursor !== null) !== rowsRemain) {
    throw new Error('Recipe continuation did not match its advertised row total.');
  }
  if (
    !rowsRemain &&
    response.compatibility.filter((row) => row.state === 'compatible').length !==
      response.totalMutationCount
  ) {
    throw new Error('Recipe mutation totals do not match the complete compatibility review.');
  }
}

async function assertRecipeExportResponse(
  response: KmRecipeExportResponse,
  request: KmRecipeExportRequest
) {
  if (
    !sameRevision(response.revision, request.expectedRevision) ||
    response.selectedChangeSetCount !== request.selectedChangeSetIds.length
  ) {
    throw new Error('Recipe export changed its exact revision or selected change-set closure.');
  }
  const recipe = kmRecipePackageSchema.parse(JSON.parse(response.artifact.content) as unknown);
  const operationCount = recipe.steps.reduce((sum, step) => sum + step.operations.length, 0);
  if (
    recipe.game !== request.scope.paths.selectedGame ||
    recipe.metadata.name !== request.name ||
    recipe.metadata.notes !== request.notes ||
    recipe.metadata.seed !== request.seed ||
    operationCount !== response.totalOperationCount ||
    recipe.steps.length !== response.selectedChangeSetCount
  ) {
    throw new Error('Recipe export artifact changed its reviewed metadata or operation count.');
  }
  const digest = await crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(response.artifact.content)
  );
  const digestHex = [...new Uint8Array(digest)]
    .map((value) => value.toString(16).padStart(2, '0'))
    .join('');
  if (
    digestHex !== response.artifact.sha256 ||
    digestHex !== response.recipeFingerprint
  ) {
    throw new Error('Recipe export content did not match its backend digest.');
  }
}

function assertSnapshotsForRevision(
  snapshots: readonly SemanticExploreSourceSnapshot[],
  revision: SemanticExploreRevision
) {
  const exactLayers = new Set<string>();
  for (const snapshot of snapshots) {
    if (!sameRevision(snapshot.revision, revision) || snapshot.layer.instanceId !== null) {
      throw new Error('Semantic merge capabilities returned a mismatched source snapshot.');
    }
    exactLayers.add(snapshot.layer.kind);
  }
  if (!exactLayers.has('base') || !exactLayers.has('layered') || !exactLayers.has('pending')) {
    throw new Error('Semantic merge capabilities omitted a required source layer.');
  }
}

function assertSnapshot(
  snapshot: SemanticExploreSourceSnapshot,
  revision: SemanticExploreRevision,
  kind: SemanticExploreSourceSnapshot['layer']['kind'],
  instanceId: string | null
) {
  if (
    !sameRevision(snapshot.revision, revision) ||
    snapshot.layer.kind !== kind ||
    snapshot.layer.instanceId !== instanceId
  ) {
    throw new Error(`Semantic merge returned a mismatched ${kind} snapshot.`);
  }
}

function sameRevision(left: SemanticExploreRevision, right: SemanticExploreRevision) {
  return semanticMergeContractKeys.exactRevisionKey(left) ===
    semanticMergeContractKeys.exactRevisionKey(right);
}

function sameSnapshot(
  left: SemanticExploreSourceSnapshot,
  right: SemanticExploreSourceSnapshot
) {
  return semanticMergeContractKeys.exactSnapshotKey(left) ===
    semanticMergeContractKeys.exactSnapshotKey(right);
}

function sameScalar(
  left: NonNullable<SemanticMergePreviewResponse['rows'][number]['resultValue']>,
  right: NonNullable<SemanticMergePreviewResponse['rows'][number]['pendingValue']>
) {
  return left.kind === right.kind && left.canonicalValue === right.canonicalValue;
}

function sameFieldRef(left: SemanticMergeFieldRef, right: SemanticMergeFieldRef) {
  return semanticMergeContractKeys.fieldRefKey(left) ===
    semanticMergeContractKeys.fieldRefKey(right);
}

function sameFieldRefSet(
  left: readonly SemanticMergeFieldRef[],
  right: readonly SemanticMergeFieldRef[]
) {
  return JSON.stringify(left.map(semanticMergeContractKeys.fieldRefKey).sort()) ===
    JSON.stringify(right.map(semanticMergeContractKeys.fieldRefKey).sort());
}

function sameResolutionSet(
  left: readonly SemanticMergeConflictResolution[],
  right: readonly SemanticMergeConflictResolution[]
) {
  const key = (resolution: SemanticMergeConflictResolution) => JSON.stringify([
    resolution.conflictId,
    resolution.choice
  ]);
  return JSON.stringify(left.map(key).sort()) === JSON.stringify(right.map(key).sort());
}

function isStaleRevisionError(error: unknown) {
  return error instanceof ProjectBridgeError &&
    error.semanticCode === semanticExploreErrorCodes.staleRevision;
}

function normalizeError(error: unknown): SemanticMergeQueryError {
  if (!(error instanceof ProjectBridgeError)) return 'generic';
  switch (error.semanticCode) {
    case projectBridgeErrorCodes.workspaceConcurrentModification:
      return 'concurrentModification';
    case semanticExploreErrorCodes.invalidCursor:
      return 'cursor';
    case semanticExploreErrorCodes.invalidQuery:
      return 'invalidQuery';
    case semanticExploreErrorCodes.limitExceeded:
      return 'limit';
    case semanticExploreErrorCodes.externalSnapshotUnavailable:
      return 'sourceUnavailable';
    case semanticMergeErrorCodes.staleProposal:
    case kmRecipeErrorCodes.staleProposal:
    case semanticExploreErrorCodes.staleRevision:
      return 'staleProposal';
    case semanticExploreErrorCodes.unsupported:
      return 'unsupported';
    default:
      return 'generic';
  }
}

export function useSemanticMergeController(options: {
  authoringContextRevision: string | null;
  bridge: SemanticMergeProjectBridgeApi;
  expectedChangeSetETag: string | null;
  isAuthoringContextReady: boolean;
  onStaleRevision?: () => void;
  revision: SemanticExploreRevision;
  scope: SemanticExploreScope;
}): SemanticMergeController {
  const storeRef = useRef<SemanticMergeControllerStore | null>(null);
  if (!storeRef.current) storeRef.current = new SemanticMergeControllerStore(options.bridge);
  const store = storeRef.current;
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);

  useLayoutEffect(() => {
    store.setBridge(options.bridge);
    store.setOnStaleRevision(options.onStaleRevision);
    store.setContext({
      authoringContextRevision: options.authoringContextRevision,
      expectedChangeSetETag: options.expectedChangeSetETag,
      revision: options.revision,
      scope: options.scope
    });
    store.setAuthoringContextReady(options.isAuthoringContextReady);
  }, [
    options.authoringContextRevision,
    options.bridge,
    options.expectedChangeSetETag,
    options.isAuthoringContextReady,
    options.onStaleRevision,
    options.revision,
    options.scope,
    store
  ]);

  useDeferredUnmountCleanup(() => store.cancelAll());

  return useMemo(() => ({
    ...snapshot,
    cancelAll: store.cancelAll,
    clearExport: store.clearExport,
    clearRecipe: store.clearRecipe,
    clearSource: store.clearSource,
    ensureCapabilities: store.ensureCapabilities,
    exportSelectedRecipe: store.exportSelectedRecipe,
    loadMoreMerge: store.loadMoreMerge,
    loadMoreRecipe: store.loadMoreRecipe,
    openSource: store.openSource,
    previewMerge: store.previewMerge,
    previewRecipe: store.previewRecipe,
    validateRecipe: store.validateRecipe
  }), [snapshot, store]);
}
