/* SPDX-License-Identifier: GPL-3.0-only */

import {
  useCallback,
  useLayoutEffect,
  useMemo,
  useRef,
  useSyncExternalStore
} from 'react';
import type {
  SemanticExploreCapabilities,
  SemanticExploreChangesPage,
  SemanticExploreComparisonPage,
  SemanticExploreEntity,
  SemanticExploreImpactPage,
  SemanticExploreLayerKind,
  SemanticExploreOwnershipPage,
  SemanticExploreRecordRef,
  SemanticExploreReferencesPage,
  SemanticExploreRevision,
  SemanticExploreScope,
  SemanticExploreSearchPage,
  SemanticExploreSourceSnapshot
} from '../../bridge/semanticExploreContracts';
import {
  semanticExploreDefaultPageSize,
  semanticExploreProjectGameFamily
} from '../../bridge/semanticExploreContracts';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import type { SemanticExploreProjectBridgeApi } from '../../bridge/semanticExploreProjectBridge';
import { semanticExploreErrorCodes } from '../../errorCodes';
import { useDeferredUnmountCleanup } from '../../hooks/useDeferredUnmountCleanup';
import {
  BoundedLruCache,
  ProjectQueryEpoch,
  runIndependentProjectRead,
  runOrderedProjectOperation,
  type ProjectQueryTicket
} from '../../utils/projectAsyncPolicy';

export type SemanticQueryStatus = 'idle' | 'loading' | 'ready' | 'error';
export const semanticExploreMaximumAccumulatedResults = 500;

export type SemanticQueryError =
  | 'cursor'
  | 'externalRejected'
  | 'invalidQuery'
  | 'limit'
  | 'unsupported'
  | 'generic';

export type SemanticQueryState<T> = {
  data: T | null;
  error: SemanticQueryError | null;
  isAppending: boolean;
  status: SemanticQueryStatus;
};

export type SemanticExploreControllerSnapshot = {
  capabilities: SemanticQueryState<SemanticExploreCapabilities>;
  changes: SemanticQueryState<SemanticExploreChangesPage>;
  comparison: SemanticQueryState<SemanticExploreComparisonPage>;
  entity: SemanticQueryState<SemanticExploreEntity>;
  externalComparison: SemanticQueryState<SemanticExploreComparisonPage>;
  impact: SemanticQueryState<SemanticExploreImpactPage>;
  isQuerying: boolean;
  ownership: SemanticQueryState<SemanticExploreOwnershipPage>;
  references: SemanticQueryState<SemanticExploreReferencesPage>;
  search: SemanticQueryState<SemanticExploreSearchPage>;
  submittedChangesSpec: SemanticChangesOptions | null;
  submittedComparisonSpec: SemanticSubmittedComparisonSpec | null;
  submittedOwnershipSpec: { record?: SemanticExploreRecordRef } | null;
};

export type SemanticExploreController = SemanticExploreControllerSnapshot & {
  cancelEntityCommandSearch: () => void;
  compare: (options: SemanticCompareOptions) => Promise<void>;
  compareExternal: (options: SemanticExternalCompareOptions) => Promise<void>;
  getEntity: (record: SemanticExploreRecordRef, layer: QueryableLayer) => Promise<void>;
  getImpact: (record: SemanticExploreRecordRef, layer: QueryableLayer) => Promise<void>;
  getOwnership: (record?: SemanticExploreRecordRef) => Promise<void>;
  getReferences: (options: SemanticReferencesOptions) => Promise<void>;
  getSemanticChanges: (options: SemanticChangesOptions) => Promise<void>;
  ensureCapabilities: () => Promise<void>;
  invalidate: () => void;
  loadMoreChanges: () => Promise<void>;
  loadMoreComparison: () => Promise<void>;
  loadMoreExternalComparison: () => Promise<void>;
  loadMoreImpact: () => Promise<void>;
  loadMoreOwnership: () => Promise<void>;
  loadMoreReferences: () => Promise<void>;
  loadMoreSearch: () => Promise<void>;
  refreshCapabilities: () => Promise<void>;
  searchEntities: (options: SemanticSearchOptions) => Promise<void>;
  searchEntityCommands: (
    searchText: string,
    limit: number
  ) => Promise<SemanticExploreSearchPage>;
};
type SemanticExploreControllerActions = Omit<
  SemanticExploreController,
  keyof SemanticExploreControllerSnapshot
>;

export type QueryableLayer = Exclude<SemanticExploreLayerKind, 'comparedMod'>;

export type SemanticSearchOptions = {
  domains?: readonly string[];
  layer: QueryableLayer;
  searchText: string;
};

export type SemanticCompareOptions = {
  left: QueryableLayer;
  record?: SemanticExploreRecordRef;
  right: QueryableLayer;
};

export type SemanticReferencesOptions = {
  direction: 'incoming' | 'outgoing';
  layer: QueryableLayer;
  record: SemanticExploreRecordRef;
};

export type SemanticExternalCompareOptions = {
  externalRootPath: string;
  left: QueryableLayer;
  record?: SemanticExploreRecordRef;
};

export type SemanticChangesOptions = {
  format: 'structured' | 'canonicalText';
  from: 'base' | 'layered';
  to: 'layered' | 'pending';
};

export type SemanticSubmittedComparisonSpec =
  | ({ kind: 'internal' } & SemanticCompareOptions)
  | ({ kind: 'external' } & Omit<SemanticExternalCompareOptions, 'externalRootPath'>);

type SlotName = Exclude<
  keyof SemanticExploreControllerSnapshot,
  | 'isQuerying'
  | 'submittedChangesSpec'
  | 'submittedComparisonSpec'
  | 'submittedOwnershipSpec'
>;
type RequestChannel = SlotName | 'detached';
type RequestToken = ProjectQueryTicket<RequestChannel> & { id: number; slot: SlotName };
type DetachedRequestToken = ProjectQueryTicket<RequestChannel> & { id: number };

type SearchSpec = SemanticSearchOptions;
type CompareSpec = SemanticCompareOptions;
type ReferencesSpec = SemanticReferencesOptions;
type ImpactSpec = { layer: QueryableLayer; record: SemanticExploreRecordRef };
type OwnershipSpec = { record?: SemanticExploreRecordRef };
type ExternalSpec = SemanticExternalCompareOptions;
type ChangesSpec = SemanticChangesOptions;
type InspectorCacheSlot = 'comparison' | 'entity' | 'impact' | 'ownership' | 'references';
type InspectorCacheValue =
  | SemanticExploreComparisonPage
  | SemanticExploreEntity
  | SemanticExploreImpactPage
  | SemanticExploreOwnershipPage
  | SemanticExploreReferencesPage;

const maximumCachedInspectorQueries = 24;
const maximumInspectorCacheBytes = 32 * 1_024 * 1_024;
const inspectorCacheTextEncoder = new TextEncoder();

const idleState = <T,>(): SemanticQueryState<T> => ({
  data: null,
  error: null,
  isAppending: false,
  status: 'idle'
});

function emptySnapshot(): SemanticExploreControllerSnapshot {
  return {
    capabilities: idleState(),
    changes: idleState(),
    comparison: idleState(),
    entity: idleState(),
    externalComparison: idleState(),
    impact: idleState(),
    isQuerying: false,
    ownership: idleState(),
    references: idleState(),
    search: idleState(),
    submittedChangesSpec: null,
    submittedComparisonSpec: null,
    submittedOwnershipSpec: null
  };
}

class SemanticExploreControllerStore {
  private activeRequests = new Set<number>();
  private bridge: SemanticExploreProjectBridgeApi;
  private capabilitiesRequest: Promise<void> | null = null;
  private changesSpec: ChangesSpec | null = null;
  private compareSpec: CompareSpec | null = null;
  private detachedRequestIds = new Set<number>();
  private entityTargetKey: string | null = null;
  private externalSpec: Omit<ExternalSpec, 'externalRootPath'> | null = null;
  private externalComparedModInstanceId: string | null = null;
  private freshness = new ProjectQueryEpoch<RequestChannel>();
  private impactSpec: ImpactSpec | null = null;
  private inspectorCache = new BoundedLruCache<string, InspectorCacheValue>({
    maximumEntries: maximumCachedInspectorQueries,
    maximumWeight: maximumInspectorCacheBytes,
    weight: (value, key) => (
      inspectorCacheTextEncoder.encode(key).byteLength +
      inspectorCacheTextEncoder.encode(JSON.stringify(value)).byteLength
    )
  });
  private inspectorQueryKeys = new Map<InspectorCacheSlot, string>();
  private listeners = new Set<() => void>();
  private nextRequestId = 1;
  private ownershipSpec: OwnershipSpec | null = null;
  private referencesSpec: ReferencesSpec | null = null;
  private scope: SemanticExploreScope | null = null;
  private scopeKey: string | null = null;
  private searchSpec: SearchSpec | null = null;
  private snapshot = emptySnapshot();

  public constructor(bridge: SemanticExploreProjectBridgeApi) {
    this.bridge = bridge;
  }

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public readonly getSnapshot = () => this.snapshot;

  public setBridge(bridge: SemanticExploreProjectBridgeApi) {
    if (bridge === this.bridge) {
      return;
    }
    this.bridge = bridge;
    this.invalidate();
  }

  public setScope(scope: SemanticExploreScope | null) {
    const nextKey = scope === null ? null : JSON.stringify(scope);
    if (nextKey === this.scopeKey) {
      return;
    }
    this.scope = scope;
    this.scopeKey = nextKey;
    this.reset(false);
  }

  public invalidate() {
    this.reset(false);
  }

  public ensureCapabilities() {
    return this.snapshot.capabilities.data
      ? Promise.resolve()
      : this.refreshCapabilities();
  }

  public refreshCapabilities() {
    return this.readCapabilities();
  }

  private readCapabilities() {
    if (this.capabilitiesRequest) return this.capabilitiesRequest;
    const request = this.performCapabilitiesRead();
    this.capabilitiesRequest = request;
    const release = () => {
      if (this.capabilitiesRequest === request) {
        this.capabilitiesRequest = null;
      }
    };
    void request.then(release, release);
    return request;
  }

  private async performCapabilitiesRead() {
    const scope = this.scope;
    if (!scope) {
      return;
    }
    const token = this.begin('capabilities', false);
    try {
      const request = { scope };
      const response = await runIndependentProjectRead(
        'getCapabilities',
        this.bridge,
        request,
        () => this.bridge.getCapabilities(request)
      );
      if (!this.isCurrent(token)) {
        return;
      }
      assertRevisionScope(response.revision, scope);
      assertCapabilityResponse(response);
      const previousRevision = this.snapshot.capabilities.data?.revision ?? null;
      if (
        previousRevision &&
        semanticRevisionIdentity(previousRevision) !== semanticRevisionIdentity(response.revision)
      ) {
        this.reset(false);
      }
      this.setSlot('capabilities', {
        data: response,
        error: null,
        isAppending: false,
        status: 'ready'
      });
    } catch (error) {
      this.fail(token, 'capabilities', null, error);
    } finally {
      this.finish(token);
    }
  }

  public async searchEntities(options: SemanticSearchOptions) {
    if (this.snapshot.search.status === 'loading') return;
    this.searchSpec = normalizeSearchOptions(options);
    await this.runSearch(false);
  }

  public async loadMoreSearch() {
    if (this.snapshot.search.status === 'loading') return;
    await this.runSearch(true);
  }

  public async searchEntityCommands(searchText: string, limit: number) {
    const expectedFreshness = this.freshness.capture('detached');
    if (!this.snapshot.capabilities.data) {
      await this.ensureCapabilities();
    }
    if (!this.freshness.isCurrent(expectedFreshness)) {
      throw new Error('The semantic command query is stale.');
    }
    const normalizedSearch = searchText.trim();
    const token = this.startDetachedRequest();
    try {
      const { capabilities, scope } = this.requireQueryContext();
      const request = {
        expectedRevision: capabilities.revision,
        layer: preferredLayer(capabilities),
        limit,
        scope,
        searchText: normalizedSearch
      };
      const response = await runIndependentProjectRead(
        'search',
        this.bridge,
        request,
        () => this.bridge.search(request)
      );
      if (!this.isDetachedCurrent(token)) {
        throw new Error('The semantic command query is stale.');
      }
      assertResponseRevision(response.revision, capabilities.revision);
      if (response.items.some((item) => (
        item.snapshot.layer.kind !== preferredLayer(capabilities) ||
        !snapshotMatchesRevision(item.snapshot, capabilities.revision) ||
        item.record.gameFamily !== capabilities.revision.gameFamily
      ))) {
        throw new Error('The semantic command result belongs to another source layer.');
      }
      return response;
    } finally {
      this.finishDetachedRequest(token);
    }
  }

  public cancelEntityCommandSearch() {
    this.freshness.supersede('detached');
    for (const requestId of this.detachedRequestIds) {
      this.activeRequests.delete(requestId);
    }
    this.detachedRequestIds.clear();
    this.updateQuerying();
  }

  public async getEntity(record: SemanticExploreRecordRef, layer: QueryableLayer) {
    const targetKey = semanticEntityTargetKey(record, layer);
    if (targetKey !== this.entityTargetKey) {
      this.entityTargetKey = targetKey;
      this.clearEntityDependentQueries();
    }
    const context = this.requireQueryContext();
    const cacheKey = this.inspectorCacheKey(
      'entity',
      targetKey,
      context.capabilities.revision
    );
    const validate = (response: SemanticExploreEntity) => {
      assertResponseRevision(response.revision, context.capabilities.revision);
      if (!semanticRecordsEqual(response.entity.record, record)) {
        throw new Error('The semantic entity response belongs to another record.');
      }
      if (response.entity.snapshot.layer.kind !== layer) {
        throw new Error('The semantic entity response belongs to another source layer.');
      }
      if (!snapshotMatchesRevision(response.entity.snapshot, context.capabilities.revision)) {
        throw new Error('The semantic entity response belongs to another source revision.');
      }
    };
    if (this.restoreInspectorQuery('entity', cacheKey, validate)) return;

    const token = this.begin('entity', false);
    try {
      const request = {
        expectedRevision: context.capabilities.revision,
        layer,
        record,
        scope: context.scope
      };
      const response = await runIndependentProjectRead(
        'getEntity',
        this.bridge,
        request,
        () => this.bridge.getEntity(request)
      );
      if (!this.isCurrent(token)) {
        return;
      }
      validate(response);
      this.writeInspectorCache(cacheKey, response);
      this.setSlot('entity', {
        data: response,
        error: null,
        isAppending: false,
        status: 'ready'
      });
    } catch (error) {
      this.fail(token, 'entity', null, error);
    } finally {
      this.finish(token);
    }
  }

  public async compare(options: SemanticCompareOptions) {
    if (this.snapshot.comparison.status === 'loading') return;
    this.externalSpec = null;
    this.externalComparedModInstanceId = null;
    this.clearSlot('externalComparison');
    this.compareSpec = options;
    this.snapshot = {
      ...this.snapshot,
      submittedComparisonSpec: { kind: 'internal', ...options }
    };
    await this.runComparison(false);
  }

  public async loadMoreComparison() {
    if (this.snapshot.comparison.status === 'loading') return;
    await this.runComparison(true);
  }

  public async getReferences(options: SemanticReferencesOptions) {
    if (this.snapshot.references.status === 'loading') return;
    this.referencesSpec = options;
    await this.runReferences(false);
  }

  public async loadMoreReferences() {
    if (this.snapshot.references.status === 'loading') return;
    await this.runReferences(true);
  }

  public async getImpact(record: SemanticExploreRecordRef, layer: QueryableLayer) {
    if (this.snapshot.impact.status === 'loading') return;
    this.impactSpec = { layer, record };
    await this.runImpact(false);
  }

  public async loadMoreImpact() {
    if (this.snapshot.impact.status === 'loading') return;
    await this.runImpact(true);
  }

  public async getOwnership(record?: SemanticExploreRecordRef) {
    if (this.snapshot.ownership.status === 'loading') return;
    this.ownershipSpec = { ...(record ? { record } : {}) };
    this.snapshot = {
      ...this.snapshot,
      submittedOwnershipSpec: { ...(record ? { record } : {}) }
    };
    await this.runOwnership(false);
  }

  public async loadMoreOwnership() {
    if (this.snapshot.ownership.status === 'loading') return;
    await this.runOwnership(true);
  }

  public compareExternal(options: SemanticExternalCompareOptions) {
    if (this.snapshot.externalComparison.status === 'loading') return Promise.resolve();
    this.compareSpec = null;
    this.clearSlot('comparison');
    this.externalSpec = { left: options.left, ...(options.record ? { record: options.record } : {}) };
    this.snapshot = {
      ...this.snapshot,
      submittedComparisonSpec: {
        kind: 'external',
        left: options.left,
        ...(options.record ? { record: options.record } : {})
      }
    };
    this.externalComparedModInstanceId = null;
    return this.runInitialExternalComparison(options.externalRootPath);
  }

  public async loadMoreExternalComparison() {
    if (this.snapshot.externalComparison.status === 'loading') return;
    await this.runExternalComparison(true);
  }

  public async getSemanticChanges(options: SemanticChangesOptions) {
    if (this.snapshot.changes.status === 'loading') return;
    this.changesSpec = options;
    this.snapshot = { ...this.snapshot, submittedChangesSpec: options };
    await this.runChanges(false);
  }

  public async loadMoreChanges() {
    if (this.snapshot.changes.status === 'loading') return;
    await this.runChanges(true);
  }

  private async runSearch(append: boolean) {
    const spec = this.searchSpec;
    if (!spec) return;
    const previous = this.snapshot.search.data;
    const cursor = append ? previous?.nextCursor : undefined;
    if (append && (!cursor || pageItemCount(previous) >= semanticExploreMaximumAccumulatedResults)) return;
    const token = this.begin('search', append);
    try {
      const context = this.requireQueryContext();
      const request = {
        ...(cursor ? { cursor } : {}),
        ...(spec.domains ? { domains: [...spec.domains] } : {}),
        expectedRevision: context.capabilities.revision,
        layer: spec.layer,
        limit: semanticExploreDefaultPageSize,
        scope: context.scope,
        searchText: spec.searchText
      };
      const response = await runIndependentProjectRead(
        'search',
        this.bridge,
        request,
        () => this.bridge.search(request)
      );
      if (response.items.some((item) => (
        item.snapshot.layer.kind !== spec.layer ||
        !snapshotMatchesRevision(item.snapshot, context.capabilities.revision) ||
        item.record.gameFamily !== context.capabilities.revision.gameFamily
      ))) {
        throw new Error('The semantic search result belongs to another source layer.');
      }
      this.completePage(token, 'search', response, previous, append, mergeItemPages);
    } catch (error) {
      this.fail(token, 'search', append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private async runComparison(append: boolean) {
    const spec = this.compareSpec;
    if (!spec) return;
    const previous = this.snapshot.comparison.data;
    const cursor = append ? previous?.nextCursor : undefined;
    if (append && (!cursor || pageItemCount(previous) >= semanticExploreMaximumAccumulatedResults)) return;
    const context = this.requireQueryContext();
    const cacheKey = append
      ? null
      : this.inspectorCacheKey('comparison', [
          spec.left,
          spec.right,
          spec.record ? semanticRecordIdentity(spec.record) : null
        ], context.capabilities.revision);
    const validate = (response: SemanticExploreComparisonPage) => {
      assertComparisonResponse(response, spec.left, spec.right, context.capabilities.revision);
      if (spec.record && response.items.some(
        (item) => !semanticRecordsEqual(item.record, spec.record!)
      )) {
        throw new Error('The semantic comparison result belongs to another record.');
      }
    };
    if (cacheKey && this.restoreInspectorQuery('comparison', cacheKey, validate)) return;

    const token = this.begin('comparison', append);
    try {
      const request = {
        ...(cursor ? { cursor } : {}),
        expectedRevision: context.capabilities.revision,
        left: spec.left,
        limit: semanticExploreDefaultPageSize,
        ...(spec.record ? { record: spec.record } : {}),
        right: spec.right,
        scope: context.scope
      };
      const response = await runIndependentProjectRead(
        'compare',
        this.bridge,
        request,
        () => this.bridge.compare(request)
      );
      validate(response);
      const completed = this.completePage(
        token,
        'comparison',
        response,
        previous,
        append,
        mergeItemPages
      );
      if (completed && cacheKey) this.writeInspectorCache(cacheKey, response);
    } catch (error) {
      this.fail(token, 'comparison', append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private async runReferences(append: boolean) {
    const spec = this.referencesSpec;
    if (!spec) return;
    const previous = this.snapshot.references.data;
    const cursor = append ? previous?.nextCursor : undefined;
    if (append && (!cursor || pageItemCount(previous) >= semanticExploreMaximumAccumulatedResults)) return;
    const context = this.requireQueryContext();
    const cacheKey = append
      ? null
      : this.inspectorCacheKey('references', [
          spec.direction,
          spec.layer,
          semanticRecordIdentity(spec.record)
        ], context.capabilities.revision);
    const validate = (response: SemanticExploreReferencesPage) => {
      assertResponseRevision(response.revision, context.capabilities.revision);
      if (response.items.some((item) => (
        item.snapshot.layer.kind !== spec.layer ||
        !snapshotMatchesRevision(item.snapshot, context.capabilities.revision) ||
        item.source.gameFamily !== context.capabilities.revision.gameFamily ||
        item.target.gameFamily !== context.capabilities.revision.gameFamily ||
        !semanticRecordsEqual(
          spec.direction === 'incoming' ? item.target : item.source,
          spec.record
        )
      ))) {
        throw new Error('The semantic reference result belongs to another query target.');
      }
    };
    if (cacheKey && this.restoreInspectorQuery('references', cacheKey, validate)) return;

    const token = this.begin('references', append);
    try {
      const request = {
        ...(cursor ? { cursor } : {}),
        direction: spec.direction,
        expectedRevision: context.capabilities.revision,
        layer: spec.layer,
        limit: semanticExploreDefaultPageSize,
        record: spec.record,
        scope: context.scope
      };
      const response = await runIndependentProjectRead(
        'getReferences',
        this.bridge,
        request,
        () => this.bridge.getReferences(request)
      );
      validate(response);
      const completed = this.completePage(
        token,
        'references',
        response,
        previous,
        append,
        mergeItemPages
      );
      if (completed && cacheKey) this.writeInspectorCache(cacheKey, response);
    } catch (error) {
      this.fail(token, 'references', append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private async runImpact(append: boolean) {
    const spec = this.impactSpec;
    if (!spec) return;
    const previous = this.snapshot.impact.data;
    const cursor = append ? previous?.nextCursor : undefined;
    if (append && (!cursor || pageItemCount(previous) >= semanticExploreMaximumAccumulatedResults)) return;
    const context = this.requireQueryContext();
    const cacheKey = append
      ? null
      : this.inspectorCacheKey('impact', [
          spec.layer,
          semanticRecordIdentity(spec.record)
        ], context.capabilities.revision);
    const validate = (response: SemanticExploreImpactPage) => {
      assertResponseRevision(response.revision, context.capabilities.revision);
    };
    if (cacheKey && this.restoreInspectorQuery('impact', cacheKey, validate)) return;

    const token = this.begin('impact', append);
    try {
      const request = {
        ...(cursor ? { cursor } : {}),
        expectedRevision: context.capabilities.revision,
        layer: spec.layer,
        limit: semanticExploreDefaultPageSize,
        record: spec.record,
        scope: context.scope
      };
      const response = await runIndependentProjectRead(
        'getImpact',
        this.bridge,
        request,
        () => this.bridge.getImpact(request)
      );
      validate(response);
      const completed = this.completePage(
        token,
        'impact',
        response,
        previous,
        append,
        mergeItemPages
      );
      if (completed && cacheKey) this.writeInspectorCache(cacheKey, response);
    } catch (error) {
      this.fail(token, 'impact', append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private async runOwnership(append: boolean) {
    const spec = this.ownershipSpec;
    if (!spec) return;
    const previous = this.snapshot.ownership.data;
    const cursor = append ? previous?.nextCursor : undefined;
    if (append && (!cursor || ownershipItemCount(previous) >= semanticExploreMaximumAccumulatedResults)) return;
    const context = this.requireQueryContext();
    const cacheKey = append
      ? null
      : this.inspectorCacheKey(
          'ownership',
          spec.record ? semanticRecordIdentity(spec.record) : null,
          context.capabilities.revision
        );
    const validate = (response: SemanticExploreOwnershipPage) => {
      assertResponseRevision(response.revision, context.capabilities.revision);
      if (response.nodes.some((node) => (
        node.record !== null && node.record.gameFamily !== context.capabilities.revision.gameFamily
      ))) {
        throw new Error('The semantic ownership result belongs to another game family.');
      }
    };
    if (cacheKey && this.restoreInspectorQuery('ownership', cacheKey, validate)) return;

    const token = this.begin('ownership', append);
    try {
      const request = {
        ...(cursor ? { cursor } : {}),
        expectedRevision: context.capabilities.revision,
        limit: semanticExploreDefaultPageSize,
        ...(spec.record ? { record: spec.record } : {}),
        scope: context.scope
      };
      const response = await runIndependentProjectRead(
        'getOwnership',
        this.bridge,
        request,
        () => this.bridge.getOwnership(request)
      );
      validate(response);
      const completed = this.completePage(
        token,
        'ownership',
        response,
        previous,
        append,
        mergeOwnershipPages
      );
      if (completed && cacheKey) this.writeInspectorCache(cacheKey, response);
    } catch (error) {
      this.fail(token, 'ownership', append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private async runInitialExternalComparison(externalRootPath: string) {
    const spec = this.externalSpec;
    if (!spec) return;
    const token = this.begin('externalComparison', false);
    try {
      const context = this.requireQueryContext();
      const request = {
        expectedRevision: context.capabilities.revision,
        externalRootPath,
        left: spec.left,
        limit: semanticExploreDefaultPageSize,
        ...(spec.record ? { record: spec.record } : {}),
        scope: context.scope
      };
      const responsePromise = runOrderedProjectOperation(
        'compareExternal',
        this.bridge,
        (bridge) => bridge.compareExternal(request)
      );
      externalRootPath = '';
      const response = await responsePromise;
      if (!this.isCurrent(token)) {
        return;
      }
      assertComparisonResponse(
        response,
        spec.left,
        'comparedMod',
        context.capabilities.revision
      );
      if (spec.record && response.items.some(
        (item) => !semanticRecordsEqual(item.record, spec.record!)
      )) {
        throw new Error('The external comparison result belongs to another record.');
      }
      this.completePage(
        token,
        'externalComparison',
        response,
        null,
        false,
        mergeItemPages
      );
      this.externalComparedModInstanceId = response.rightSnapshot.layer.instanceId;
      if (response.nextCursor === null || this.externalComparedModInstanceId === null) {
        this.externalSpec = null;
        this.externalComparedModInstanceId = null;
      }
    } catch (error) {
      if (!this.isCurrent(token)) {
        return;
      }
      this.externalSpec = null;
      this.externalComparedModInstanceId = null;
      this.fail(token, 'externalComparison', null, error);
    } finally {
      this.finish(token);
    }
  }

  private async runExternalComparison(append: boolean) {
    const spec = this.externalSpec;
    const comparedModInstanceId = this.externalComparedModInstanceId;
    if (!spec || !comparedModInstanceId || !append) return;
    const previous = this.snapshot.externalComparison.data;
    const cursor = previous?.nextCursor;
    if (
      !previous ||
      !cursor ||
      pageItemCount(previous) >= semanticExploreMaximumAccumulatedResults
    ) return;
    const token = this.begin('externalComparison', true);
    try {
      const context = this.requireQueryContext();
      const request = {
        comparedModInstanceId,
        cursor,
        expectedRevision: context.capabilities.revision,
        left: spec.left,
        limit: semanticExploreDefaultPageSize,
        ...(spec.record ? { record: spec.record } : {}),
        scope: context.scope
      };
      const response = await runOrderedProjectOperation(
        'compareExternal',
        this.bridge,
        (bridge) => bridge.compareExternal(request)
      );
      if (!this.isCurrent(token)) {
        return;
      }
      assertComparisonResponse(
        response,
        spec.left,
        'comparedMod',
        context.capabilities.revision
      );
      if (spec.record && response.items.some(
        (item) => !semanticRecordsEqual(item.record, spec.record!)
      )) {
        throw new Error('The external comparison result belongs to another record.');
      }
      if (response.rightSnapshot.layer.instanceId !== comparedModInstanceId) {
        throw new Error('The external comparison continuation belongs to another source.');
      }
      this.completePage(
        token,
        'externalComparison',
        response,
        previous,
        true,
        mergeItemPages
      );
      if (response.nextCursor === null) {
        this.externalSpec = null;
        this.externalComparedModInstanceId = null;
      }
    } catch (error) {
      if (!this.isCurrent(token)) {
        return;
      }
      this.externalSpec = null;
      this.externalComparedModInstanceId = null;
      this.fail(token, 'externalComparison', append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private async runChanges(append: boolean) {
    const spec = this.changesSpec;
    if (!spec) return;
    const previous = this.snapshot.changes.data;
    const cursor = append ? previous?.nextCursor : undefined;
    if (append && (!cursor || pageItemCount(previous) >= semanticExploreMaximumAccumulatedResults)) return;
    const token = this.begin('changes', append);
    try {
      const context = this.requireQueryContext();
      const request = {
        ...(cursor ? { cursor } : {}),
        expectedRevision: context.capabilities.revision,
        format: spec.format,
        from: spec.from,
        limit: semanticExploreDefaultPageSize,
        scope: context.scope,
        to: spec.to
      };
      const response = await runIndependentProjectRead(
        'getSemanticChanges',
        this.bridge,
        request,
        () => this.bridge.getSemanticChanges(request)
      );
      if (response.items.some(
        (item) => item.record.gameFamily !== context.capabilities.revision.gameFamily
      )) {
        throw new Error('The semantic change result belongs to another game family.');
      }
      this.completePage(token, 'changes', response, previous, append, mergeItemPages);
    } catch (error) {
      this.fail(token, 'changes', append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private completePage<TPage extends CommonPage>(
    token: RequestToken,
    slot: SlotName,
    response: TPage,
    previous: TPage | null,
    append: boolean,
    merge: (previous: TPage, next: TPage) => TPage
  ) {
    if (!this.isCurrent(token)) return false;
    const capabilities = this.snapshot.capabilities.data;
    if (!capabilities) throw new Error('Semantic capabilities are unavailable.');
    assertResponseRevision(response.revision, capabilities.revision);
    if (append && previous && response.queryFingerprint !== previous.queryFingerprint) {
      throw new Error('The semantic continuation belongs to another query.');
    }
    this.setSlot(slot, {
      data: append && previous ? merge(previous, response) : response,
      error: null,
      isAppending: false,
      status: 'ready'
    });
    return true;
  }

  private inspectorCacheKey(
    slot: InspectorCacheSlot,
    queryIdentity: unknown,
    revision: SemanticExploreRevision
  ) {
    return JSON.stringify([
      revision.projectId,
      revision.gameFamily,
      revision.generation,
      revision.fingerprint,
      slot,
      queryIdentity
    ]);
  }

  private restoreInspectorQuery<T extends InspectorCacheValue>(
    slot: InspectorCacheSlot,
    key: string,
    validate: (value: T) => void
  ) {
    const current = this.snapshot[slot] as SemanticQueryState<T>;
    const isSameQuery = this.inspectorQueryKeys.get(slot) === key;
    if (isSameQuery && (current.status === 'loading' || current.status === 'ready')) {
      return true;
    }
    this.inspectorQueryKeys.set(slot, key);
    if (isSameQuery && current.status === 'error') return false;

    const cached = this.inspectorCache.get(key) as T | undefined;
    if (!cached) return false;
    try {
      validate(cached);
    } catch {
      this.inspectorCache.delete(key);
      return false;
    }
    this.freshness.supersede(slot);
    this.setSlot(slot, {
      data: cached,
      error: null,
      isAppending: false,
      status: 'ready'
    });
    return true;
  }

  private writeInspectorCache(key: string, value: InspectorCacheValue) {
    this.inspectorCache.set(key, value);
  }

  private requireQueryContext() {
    const scope = this.scope;
    const capabilities = this.snapshot.capabilities.data;
    if (!scope || !capabilities) {
      throw new Error('Semantic project capabilities are not ready.');
    }
    return { capabilities, scope };
  }

  private begin(slot: SlotName, append: boolean): RequestToken {
    const freshness = this.freshness.supersede(slot);
    const token = {
      ...freshness,
      id: this.nextRequestId++,
      slot
    };
    this.activeRequests.add(token.id);
    const current = this.snapshot[slot] as SemanticQueryState<unknown>;
    this.setSlot(slot, {
      data: append ? current.data : null,
      error: null,
      isAppending: append,
      status: 'loading'
    });
    this.updateQuerying();
    return token;
  }

  private startDetachedRequest(): DetachedRequestToken {
    const token = {
      ...this.freshness.capture('detached'),
      id: this.nextRequestId++
    };
    this.activeRequests.add(token.id);
    this.detachedRequestIds.add(token.id);
    this.updateQuerying();
    return token;
  }

  private finishDetachedRequest(token: DetachedRequestToken) {
    this.detachedRequestIds.delete(token.id);
    if (this.activeRequests.delete(token.id)) {
      this.updateQuerying();
    }
  }

  private isDetachedCurrent(token: DetachedRequestToken) {
    return this.freshness.isCurrent(token);
  }

  private finish(token: RequestToken) {
    if (this.activeRequests.delete(token.id)) {
      this.updateQuerying();
    }
  }

  private isCurrent(token: RequestToken) {
    return this.freshness.isCurrent(token);
  }

  private fail<T>(
    token: RequestToken,
    slot: SlotName,
    retainedData: T | null = null,
    error?: unknown
  ) {
    if (!this.isCurrent(token)) return;
    if (semanticErrorCode(error) === semanticExploreErrorCodes.staleRevision) {
      this.invalidate();
      return;
    }
    this.setSlot(slot, {
      data: retainedData,
      error: classifySemanticQueryError(error),
      isAppending: false,
      status: 'error'
    });
  }

  private setSlot<T>(slot: SlotName, state: SemanticQueryState<T>) {
    this.snapshot = { ...this.snapshot, [slot]: state };
    this.emit();
  }

  private clearSlot(slot: SlotName) {
    this.freshness.supersede(slot);
    this.setSlot(slot, idleState());
  }

  private clearEntityDependentQueries() {
    this.compareSpec = null;
    this.referencesSpec = null;
    this.impactSpec = null;
    this.ownershipSpec = null;
    this.clearSlot('comparison');
    this.clearSlot('references');
    this.clearSlot('impact');
    this.clearSlot('ownership');
  }

  private updateQuerying() {
    const isQuerying = this.activeRequests.size > 0;
    if (this.snapshot.isQuerying !== isQuerying) {
      this.snapshot = { ...this.snapshot, isQuerying };
      this.emit();
    }
  }

  private reset(clearScope: boolean) {
    this.freshness.invalidateAll();
    this.activeRequests.clear();
    this.capabilitiesRequest = null;
    this.detachedRequestIds.clear();
    this.entityTargetKey = null;
    this.searchSpec = null;
    this.compareSpec = null;
    this.referencesSpec = null;
    this.impactSpec = null;
    this.inspectorCache.clear();
    this.inspectorQueryKeys.clear();
    this.ownershipSpec = null;
    this.externalSpec = null;
    this.externalComparedModInstanceId = null;
    this.changesSpec = null;
    if (clearScope) {
      this.scope = null;
      this.scopeKey = null;
    }
    this.snapshot = emptySnapshot();
    this.emit();
  }

  private emit() {
    for (const listener of this.listeners) listener();
  }
}

type CommonPage = {
  nextCursor: string | null;
  queryFingerprint: string;
  revision: SemanticExploreRevision;
};

function mergeItemPages<TPage extends CommonPage & { items: readonly unknown[] }>(
  previous: TPage,
  next: TPage
): TPage {
  const items = [...previous.items, ...next.items];
  if (items.length > semanticExploreMaximumAccumulatedResults) {
    throw new Error('The semantic query exceeded the bounded frontend result window.');
  }
  return { ...next, items };
}

function mergeOwnershipPages(
  previous: SemanticExploreOwnershipPage,
  next: SemanticExploreOwnershipPage
): SemanticExploreOwnershipPage {
  const conflicts = distinctBy(
    [...previous.conflicts, ...next.conflicts],
    (conflict) => conflict.conflictId
  );
  const edges = distinctBy(
    [...previous.edges, ...next.edges],
    (edge) => `${edge.sourceNodeId}:${edge.kind}:${edge.targetNodeId}`
  );
  const nodes = distinctBy([...previous.nodes, ...next.nodes], (node) => node.nodeId);
  if (
    conflicts.length > semanticExploreMaximumAccumulatedResults ||
    edges.length > semanticExploreMaximumAccumulatedResults ||
    nodes.length > semanticExploreMaximumAccumulatedResults
  ) {
    throw new Error('The semantic ownership query exceeded the bounded frontend result window.');
  }
  return {
    ...next,
    conflicts,
    edges,
    nodes
  };
}

function distinctBy<T>(values: readonly T[], key: (value: T) => string) {
  const seen = new Set<string>();
  return values.filter((value) => {
    const valueKey = key(value);
    if (seen.has(valueKey)) return false;
    seen.add(valueKey);
    return true;
  });
}

function pageItemCount(page: { items: readonly unknown[] } | null | undefined) {
  return page?.items.length ?? 0;
}

function ownershipItemCount(page: SemanticExploreOwnershipPage | null | undefined) {
  return page ? Math.max(page.nodes.length, page.edges.length, page.conflicts.length) : 0;
}

function normalizeSearchOptions(options: SemanticSearchOptions): SemanticSearchOptions {
  const searchText = options.searchText.trim();
  if (searchText.length === 0) {
    throw new Error('Semantic search text cannot be empty.');
  }
  return {
    ...(options.domains && options.domains.length > 0
      ? { domains: [...new Set(options.domains)] }
      : {}),
    layer: options.layer,
    searchText
  };
}

function preferredLayer(capabilities: SemanticExploreCapabilities): QueryableLayer {
  if (capabilities.snapshots.some((snapshot) => snapshot.layer.kind === 'pending')) {
    return 'pending';
  }
  if (capabilities.snapshots.some((snapshot) => snapshot.layer.kind === 'layered')) {
    return 'layered';
  }
  return 'base';
}

function assertRevisionScope(revision: SemanticExploreRevision, scope: SemanticExploreScope) {
  if (
    revision.projectId !== scope.projectId ||
    scope.paths.selectedGame === null ||
    revision.gameFamily !== semanticExploreProjectGameFamily(scope.paths.selectedGame)
  ) {
    throw new Error('Semantic capabilities belong to another project.');
  }
}

function assertCapabilityResponse(capabilities: SemanticExploreCapabilities) {
  const layerKinds = new Set<SemanticExploreLayerKind>();
  for (const snapshot of capabilities.snapshots) {
    if (
      snapshot.layer.kind === 'comparedMod' ||
      layerKinds.has(snapshot.layer.kind) ||
      !snapshotMatchesRevision(snapshot, capabilities.revision)
    ) {
      throw new Error('Semantic capabilities contain an invalid source snapshot.');
    }
    layerKinds.add(snapshot.layer.kind);
  }
  for (const provider of capabilities.providers) {
    const coverageDomains = new Set(provider.coverage.domains);
    if (
      provider.coverage.providerId !== provider.providerId ||
      coverageDomains.size !== provider.domains.length ||
      provider.domains.some((domain) => !coverageDomains.has(domain))
    ) {
      throw new Error('Semantic capabilities contain inconsistent provider coverage.');
    }
  }
}

function assertResponseRevision(
  response: SemanticExploreRevision,
  expected: SemanticExploreRevision
) {
  if (
    response.projectId !== expected.projectId ||
    response.gameFamily !== expected.gameFamily ||
    response.generation !== expected.generation ||
    response.fingerprint !== expected.fingerprint
  ) {
    throw new Error('The semantic response belongs to a stale project revision.');
  }
}

function semanticRecordsEqual(
  left: SemanticExploreRecordRef,
  right: SemanticExploreRecordRef
) {
  return left.gameFamily === right.gameFamily &&
    left.domain === right.domain &&
    left.recordKind.key === right.recordKind.key &&
    left.recordKind.schemaVersion === right.recordKind.schemaVersion &&
    left.recordId === right.recordId &&
    left.subrecordId === right.subrecordId;
}

function semanticRecordIdentity(record: SemanticExploreRecordRef) {
  return JSON.stringify([
    record.gameFamily,
    record.domain,
    record.recordKind.key,
    record.recordKind.schemaVersion,
    record.recordId,
    record.subrecordId
  ]);
}

function semanticRevisionIdentity(revision: SemanticExploreRevision) {
  return JSON.stringify([
    revision.projectId,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
}

function semanticEntityTargetKey(record: SemanticExploreRecordRef, layer: QueryableLayer) {
  return JSON.stringify([
    layer,
    record.gameFamily,
    record.domain,
    record.recordKind.key,
    record.recordKind.schemaVersion,
    record.recordId,
    record.subrecordId
  ]);
}

function assertComparisonResponse(
  response: SemanticExploreComparisonPage,
  left: QueryableLayer,
  right: QueryableLayer | 'comparedMod',
  expectedRevision: SemanticExploreRevision
) {
  if (
    response.leftSnapshot.layer.kind !== left ||
    response.rightSnapshot.layer.kind !== right ||
    !snapshotMatchesRevision(response.leftSnapshot, expectedRevision) ||
    !snapshotMatchesRevision(response.rightSnapshot, expectedRevision) ||
    response.items.some(
      (item) => item.record.gameFamily !== expectedRevision.gameFamily
    )
  ) {
    throw new Error('The semantic comparison response belongs to another source pair.');
  }
}

function snapshotMatchesRevision(
  snapshot: SemanticExploreSourceSnapshot,
  revision: SemanticExploreRevision
) {
  try {
    assertResponseRevision(snapshot.revision, revision);
    return true;
  } catch {
    return false;
  }
}

function semanticErrorCode(error: unknown) {
  return error instanceof ProjectBridgeError && error.semanticCode
    ? String(error.semanticCode)
    : null;
}

function classifySemanticQueryError(error: unknown): SemanticQueryError {
  switch (semanticErrorCode(error)) {
    case semanticExploreErrorCodes.invalidCursor:
      return 'cursor';
    case semanticExploreErrorCodes.invalidQuery:
      return 'invalidQuery';
    case semanticExploreErrorCodes.externalOverlayRejected:
    case semanticExploreErrorCodes.externalSnapshotUnavailable:
      return 'externalRejected';
    case semanticExploreErrorCodes.limitExceeded:
      return 'limit';
    case semanticExploreErrorCodes.unsupported:
      return 'unsupported';
    default:
      return 'generic';
  }
}

export function useSemanticExploreController(options: {
  bridge: SemanticExploreProjectBridgeApi;
  scope: SemanticExploreScope | null;
}): SemanticExploreController {
  const storeRef = useRef<SemanticExploreControllerStore | null>(null);
  if (storeRef.current === null) {
    storeRef.current = new SemanticExploreControllerStore(options.bridge);
  }
  const store = storeRef.current;
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);

  useLayoutEffect(() => store.setBridge(options.bridge), [options.bridge, store]);
  useLayoutEffect(() => store.setScope(options.scope), [options.scope, store]);
  useDeferredUnmountCleanup(() => store.invalidate());

  const invalidate = useCallback(() => store.invalidate(), [store]);
  const actions = useMemo<SemanticExploreControllerActions>(() => ({
    cancelEntityCommandSearch: () => store.cancelEntityCommandSearch(),
    compare: (value: SemanticCompareOptions) => store.compare(value),
    compareExternal: (value: SemanticExternalCompareOptions) => store.compareExternal(value),
    getEntity: (record: SemanticExploreRecordRef, layer: QueryableLayer) =>
      store.getEntity(record, layer),
    getImpact: (record: SemanticExploreRecordRef, layer: QueryableLayer) =>
      store.getImpact(record, layer),
    getOwnership: (record?: SemanticExploreRecordRef) => store.getOwnership(record),
    getReferences: (value: SemanticReferencesOptions) => store.getReferences(value),
    getSemanticChanges: (value: SemanticChangesOptions) => store.getSemanticChanges(value),
    ensureCapabilities: () => store.ensureCapabilities(),
    invalidate,
    loadMoreChanges: () => store.loadMoreChanges(),
    loadMoreComparison: () => store.loadMoreComparison(),
    loadMoreExternalComparison: () => store.loadMoreExternalComparison(),
    loadMoreImpact: () => store.loadMoreImpact(),
    loadMoreOwnership: () => store.loadMoreOwnership(),
    loadMoreReferences: () => store.loadMoreReferences(),
    loadMoreSearch: () => store.loadMoreSearch(),
    refreshCapabilities: () => store.refreshCapabilities(),
    searchEntities: (value: SemanticSearchOptions) => store.searchEntities(value),
    searchEntityCommands: (searchText: string, limit: number) =>
      store.searchEntityCommands(searchText, limit)
  }), [invalidate, store]);
  return useMemo(() => ({ ...snapshot, ...actions }), [actions, snapshot]);
}
