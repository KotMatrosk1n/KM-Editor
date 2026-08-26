/* SPDX-License-Identifier: GPL-3.0-only */

import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useSyncExternalStore
} from 'react';
import {
  expectedModulesForFamily,
  gameModuleDefaultPageSize,
  gameModuleMaximumAccumulatedRecords,
  type GameModule,
  type GameModuleCapability,
  type GameModuleLayer,
  type QueryGameModuleResponse,
  type ReadGameModuleCapabilitiesResponse
} from '../../bridge/gameModuleContracts';
import type { GameModuleProjectBridgeApi } from '../../bridge/gameModuleProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope,
  SemanticExploreSourceSnapshot
} from '../../bridge/semanticExploreContracts';
import { semanticExploreProjectGameFamily } from '../../bridge/semanticExploreContracts';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import { semanticExploreErrorCodes } from '../../errorCodes';

export type GameModuleRequestStatus = 'idle' | 'loading' | 'ready' | 'error';
export type GameModuleRequestError =
  | 'cursor'
  | 'invalidQuery'
  | 'limit'
  | 'unsupported'
  | 'generic';

export type GameModuleCapabilitiesState = {
  data: ReadGameModuleCapabilitiesResponse | null;
  error: GameModuleRequestError | null;
  status: GameModuleRequestStatus;
};

export type GameModuleQueryOptions = {
  layer: GameModuleLayer;
  module: GameModule;
};

export type GameModuleQueryState = {
  data: QueryGameModuleResponse | null;
  error: GameModuleRequestError | null;
  isAppending: boolean;
  status: GameModuleRequestStatus;
};

export type GameModuleControllerSnapshot = {
  activeQuery: GameModuleQueryOptions | null;
  capabilities: GameModuleCapabilitiesState;
  isBusy: boolean;
  result: GameModuleQueryState;
};

export type GameModuleController = GameModuleControllerSnapshot & {
  cancel: () => void;
  loadCapabilities: () => Promise<void>;
  loadMore: () => Promise<void>;
  query: (options: GameModuleQueryOptions) => Promise<void>;
  refresh: () => Promise<void>;
  refreshCapabilities: () => Promise<void>;
};

type RequestToken = { epoch: number; generation: number; id: number };

const maximumCachedQueries = 10;
const maximumCacheBytes = 16 * 1_024 * 1_024;
const textEncoder = new TextEncoder();

const idleCapabilities = (): GameModuleCapabilitiesState => ({
  data: null,
  error: null,
  status: 'idle'
});

const idleQuery = (): GameModuleQueryState => ({
  data: null,
  error: null,
  isAppending: false,
  status: 'idle'
});

class GameModuleControllerStore {
  private activeRequests = new Set<number>();
  private bridge: GameModuleProjectBridgeApi;
  private cache = new Map<string, QueryGameModuleResponse>();
  private contextKey: string | null = null;
  private epoch = 0;
  private generation = 0;
  private listeners = new Set<() => void>();
  private nextRequestId = 1;
  private onStaleRevision: (() => void) | null = null;
  private revision: SemanticExploreRevision | null = null;
  private scope: SemanticExploreScope | null = null;
  private snapshot: GameModuleControllerSnapshot = {
    activeQuery: null,
    capabilities: idleCapabilities(),
    isBusy: false,
    result: idleQuery()
  };

  public constructor(bridge: GameModuleProjectBridgeApi) {
    this.bridge = bridge;
  }

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public readonly getSnapshot = () => this.snapshot;

  public setBridge(bridge: GameModuleProjectBridgeApi) {
    if (this.bridge === bridge) return;
    this.bridge = bridge;
    this.reset();
  }

  public setContext(scope: SemanticExploreScope | null, revision: SemanticExploreRevision | null) {
    const nextKey = revision ? revisionIdentity(revision) : null;
    if (this.scope === scope && this.contextKey === nextKey) return;
    this.scope = scope;
    this.revision = revision;
    this.contextKey = nextKey;
    this.reset();
  }

  public setOnStaleRevision(callback: (() => void) | undefined) {
    this.onStaleRevision = callback ?? null;
  }

  public cancel() {
    this.epoch += 1;
    this.generation += 1;
    this.activeRequests.clear();
    this.snapshot = {
      ...this.snapshot,
      isBusy: false,
      result: this.snapshot.result.data
        ? { ...this.snapshot.result, error: null, isAppending: false, status: 'ready' }
        : idleQuery()
    };
    this.emit();
  }

  public async loadCapabilities() {
    if (this.snapshot.capabilities.status === 'loading') return;
    const token = this.beginCapabilities();
    try {
      const context = this.requireContext();
      const response = await this.bridge.getGameModuleCapabilities({ scope: context.scope });
      if (!this.isCurrent(token)) return;
      assertCapabilityResponse(response, context.revision);
      this.snapshot = {
        ...this.snapshot,
        capabilities: { data: response, error: null, status: 'ready' }
      };
      this.emit();
    } catch (error) {
      this.failCapabilities(token, error);
    } finally {
      this.finish(token);
    }
  }

  public async refreshCapabilities() {
    this.supersedeRequests();
    this.cache.clear();
    this.snapshot = {
      activeQuery: null,
      capabilities: idleCapabilities(),
      isBusy: false,
      result: idleQuery()
    };
    this.emit();
    await this.loadCapabilities();
  }

  public async query(options: GameModuleQueryOptions) {
    this.supersedeRequests();
    const capability = this.requireQueryableCapability(options);
    this.snapshot = { ...this.snapshot, activeQuery: options };
    const cached = this.readCache(options);
    if (cached) {
      assertQueryCapability(cached.capability, capability);
      this.snapshot = {
        ...this.snapshot,
        activeQuery: options,
        isBusy: false,
        result: { data: cached, error: null, isAppending: false, status: 'ready' }
      };
      this.emit();
      return;
    }
    await this.runQuery(options, false);
  }

  public async refresh() {
    if (!this.snapshot.activeQuery) return;
    await this.runQuery(this.snapshot.activeQuery, false);
  }

  public async loadMore() {
    if (!this.snapshot.activeQuery) return;
    await this.runQuery(this.snapshot.activeQuery, true);
  }

  private async runQuery(options: GameModuleQueryOptions, append: boolean) {
    const previous = append ? this.snapshot.result.data : null;
    const cursor = previous?.nextCursor ?? undefined;
    if (
      append &&
      (!cursor || (previous?.records.length ?? 0) >= gameModuleMaximumAccumulatedRecords)
    ) {
      return;
    }
    const capability = this.requireQueryableCapability(options);
    const expectedSnapshot = this.requireCapabilitySnapshot(options.layer);
    const limit = append
      ? Math.min(
          gameModuleDefaultPageSize,
          gameModuleMaximumAccumulatedRecords - (previous?.records.length ?? 0)
        )
      : gameModuleDefaultPageSize;
    const token = this.beginQuery(options, append);
    try {
      const context = this.requireContext();
      const response = await this.bridge.queryGameModule({
        ...(cursor ? { cursor } : {}),
        expectedRevision: context.revision,
        layer: options.layer,
        limit,
        module: options.module,
        scope: context.scope
      });
      if (!this.isCurrent(token)) return;
      assertQueryResponse(
        response,
        options,
        context.revision,
        capability,
        expectedSnapshot,
        limit,
        previous?.records.length ?? 0
      );
      if (append && previous && response.queryFingerprint !== previous.queryFingerprint) {
        throw new Error('The game module continuation belongs to another query.');
      }
      const data = append && previous ? mergePages(previous, response) : response;
      this.writeCache(options, data);
      this.snapshot = {
        ...this.snapshot,
        activeQuery: options,
        result: { data, error: null, isAppending: false, status: 'ready' }
      };
      this.emit();
    } catch (error) {
      this.failQuery(token, previous, error);
    } finally {
      this.finish(token);
    }
  }

  private requireContext() {
    if (!this.scope || !this.revision) {
      throw new Error('Game modules require an exact semantic project revision.');
    }
    assertRevisionScope(this.revision, this.scope);
    return { revision: this.revision, scope: this.scope };
  }

  private requireQueryableCapability(options: GameModuleQueryOptions) {
    const capability = this.snapshot.capabilities.data?.capabilities.find(
      (candidate) => candidate.module === options.module
    );
    if (
      !capability ||
      !capability.canQuery ||
      capability.state === 'unavailable' ||
      !capability.supportedLayers.includes(options.layer)
    ) {
      throw new Error('This game module does not expose the requested read-only query.');
    }
    return capability;
  }

  private requireCapabilitySnapshot(layer: GameModuleLayer) {
    const snapshot = this.snapshot.capabilities.data?.snapshots.find(
      (candidate) => candidate.layer.kind === layer
    );
    if (!snapshot) {
      throw new Error('This game module does not have an exact source snapshot.');
    }
    return snapshot;
  }

  private beginCapabilities() {
    const token = this.beginRequest();
    this.snapshot = {
      ...this.snapshot,
      capabilities: { data: null, error: null, status: 'loading' },
      isBusy: true
    };
    this.emit();
    return token;
  }

  private beginQuery(options: GameModuleQueryOptions, append: boolean) {
    const token = this.beginRequest();
    this.snapshot = {
      ...this.snapshot,
      activeQuery: options,
      isBusy: true,
      result: {
        data: append ? this.snapshot.result.data : null,
        error: null,
        isAppending: append,
        status: 'loading'
      }
    };
    this.emit();
    return token;
  }

  private beginRequest(): RequestToken {
    this.supersedeRequests();
    this.generation += 1;
    const token = { epoch: this.epoch, generation: this.generation, id: this.nextRequestId++ };
    this.activeRequests.add(token.id);
    return token;
  }

  private finish(token: RequestToken) {
    if (!this.activeRequests.delete(token.id)) return;
    const isBusy = this.activeRequests.size > 0;
    if (isBusy === this.snapshot.isBusy) return;
    this.snapshot = { ...this.snapshot, isBusy };
    this.emit();
  }

  private isCurrent(token: RequestToken) {
    return token.epoch === this.epoch && token.generation === this.generation;
  }

  private failCapabilities(token: RequestToken, error: unknown) {
    if (!this.isCurrent(token)) return;
    if (isStaleError(error)) {
      this.onStaleRevision?.();
      this.reset();
      return;
    }
    this.snapshot = {
      ...this.snapshot,
      capabilities: { data: null, error: classifyError(error), status: 'error' }
    };
    this.emit();
  }

  private failQuery(
    token: RequestToken,
    retained: QueryGameModuleResponse | null,
    error: unknown
  ) {
    if (!this.isCurrent(token)) return;
    if (isStaleError(error)) {
      this.onStaleRevision?.();
      this.reset();
      return;
    }
    this.snapshot = {
      ...this.snapshot,
      result: {
        data: retained,
        error: classifyError(error),
        isAppending: false,
        status: 'error'
      }
    };
    this.emit();
  }

  private supersedeRequests() {
    this.generation += 1;
    this.activeRequests.clear();
  }

  private cacheKey(options: GameModuleQueryOptions) {
    return this.revision
      ? JSON.stringify([revisionIdentity(this.revision), options.module, options.layer])
      : null;
  }

  private readCache(options: GameModuleQueryOptions) {
    const key = this.cacheKey(options);
    if (!key) return null;
    const value = this.cache.get(key) ?? null;
    if (value) {
      this.cache.delete(key);
      this.cache.set(key, value);
    }
    return value;
  }

  private writeCache(options: GameModuleQueryOptions, response: QueryGameModuleResponse) {
    const key = this.cacheKey(options);
    if (!key || response.diagnostics.length > 0 || response.capability.state === 'unavailable') {
      return;
    }
    this.cache.delete(key);
    this.cache.set(key, response);
    while (
      this.cache.size > maximumCachedQueries ||
      cacheByteCount(this.cache) > maximumCacheBytes
    ) {
      const oldest = this.cache.keys().next().value as string | undefined;
      if (!oldest) break;
      this.cache.delete(oldest);
    }
  }

  private reset() {
    this.epoch += 1;
    this.generation += 1;
    this.activeRequests.clear();
    this.cache.clear();
    this.snapshot = {
      activeQuery: null,
      capabilities: idleCapabilities(),
      isBusy: false,
      result: idleQuery()
    };
    this.emit();
  }

  private emit() {
    for (const listener of this.listeners) listener();
  }
}

function assertCapabilityResponse(
  response: ReadGameModuleCapabilitiesResponse,
  expectedRevision: SemanticExploreRevision
) {
  assertRevision(response.revision, expectedRevision);
  const expected = expectedModulesForFamily(expectedRevision.gameFamily);
  const actual = response.capabilities.map((capability) => capability.module);
  if (
    actual.length !== expected.length ||
    expected.some((module, index) => actual[index] !== module) ||
    new Set(actual).size !== actual.length
  ) {
    throw new Error('The game module catalog is incomplete or duplicated.');
  }
  const snapshotLayers = new Set(response.snapshots.map((snapshot) => snapshot.layer.kind));
  for (const capability of response.capabilities) {
    if (
      capability.family !== expectedRevision.gameFamily ||
      capability.supportedLayers.some((layer) => !snapshotLayers.has(layer))
    ) {
      throw new Error('The game module catalog belongs to another source context.');
    }
  }
  if (response.snapshots.some((snapshot) => !snapshotMatchesRevision(snapshot, expectedRevision))) {
    throw new StaleGameModuleResponseError();
  }
}

function assertQueryResponse(
  response: QueryGameModuleResponse,
  options: GameModuleQueryOptions,
  expectedRevision: SemanticExploreRevision,
  expectedCapability: GameModuleCapability,
  expectedSnapshot: SemanticExploreSourceSnapshot,
  requestedLimit: number,
  previousRecordCount: number
) {
  assertRevision(response.revision, expectedRevision);
  if (
    response.capability.module !== options.module ||
    response.snapshot.layer.kind !== options.layer ||
    !snapshotMatchesRevision(response.snapshot, expectedRevision) ||
    snapshotIdentity(response.snapshot) !== snapshotIdentity(expectedSnapshot)
  ) {
    throw new StaleGameModuleResponseError();
  }
  assertQueryCapability(response.capability, expectedCapability);
  const loadedRecordCount = previousRecordCount + response.records.length;
  if (
    response.records.length > requestedLimit ||
    loadedRecordCount > response.totalRecordCount ||
    (loadedRecordCount < response.totalRecordCount) !== (response.nextCursor !== null) ||
    (response.nextCursor !== null && response.records.length === 0)
  ) {
    throw new Error('The game module page did not make bounded cursor progress.');
  }
  const recordKeys = new Set<string>();
  const factIds = new Set<string>();
  const records: SemanticExploreRecordRef[] = [];
  for (const [index, record] of response.records.entries()) {
    const key = record.recordId;
    if (recordKeys.has(key)) throw new Error('The game module page contains duplicate records.');
    recordKeys.add(key);
    if (
      record.coverage !== response.capability.state ||
      record.confidence !== response.capability.confidence ||
      record.sortOrder !== previousRecordCount + index ||
      record.facts.some((fact) => (
        fact.providerId !== response.capability.providerId ||
        fact.value.kind === 'null' && fact.confidence !== 'unknown'
      ))
    ) {
      throw new Error('The game module page contains inconsistent provider truth.');
    }
    for (const fact of record.facts) {
      if (factIds.has(fact.factId)) {
        throw new Error('The game module page contains duplicate facts.');
      }
      factIds.add(fact.factId);
    }
    if (record.target) records.push(record.target);
    for (const fact of record.facts) records.push(...fact.evidence);
  }
  if (records.some((record) => record.gameFamily !== expectedRevision.gameFamily)) {
    throw new Error('The game module query returned a record from another game family.');
  }
}

function assertQueryCapability(
  actual: GameModuleCapability,
  expected: GameModuleCapability
) {
  if (
    actual.module !== expected.module ||
    actual.family !== expected.family ||
    actual.maturity !== expected.maturity ||
    actual.providerId !== expected.providerId ||
    actual.state !== expected.state ||
    actual.confidence !== expected.confidence ||
    actual.canQuery !== expected.canQuery ||
    actual.reasonCode !== expected.reasonCode ||
    actual.supportedLayers.length !== expected.supportedLayers.length ||
    actual.supportedLayers.some((layer) => !expected.supportedLayers.includes(layer))
  ) {
    throw new Error('The game module capability changed during its query.');
  }
}

function mergePages(
  previous: QueryGameModuleResponse,
  next: QueryGameModuleResponse
): QueryGameModuleResponse {
  if (previous.totalRecordCount !== next.totalRecordCount) {
    throw new Error('The game module result count changed between continuation pages.');
  }
  const seen = new Set(
    previous.records.map((record) => record.recordId)
  );
  const seenFactIds = new Set(
    previous.records.flatMap((record) => record.facts.map((fact) => fact.factId))
  );
  for (const record of next.records) {
    if (seen.has(record.recordId)) {
      throw new Error('The game module continuation repeated a record.');
    }
    seen.add(record.recordId);
    for (const fact of record.facts) {
      if (seenFactIds.has(fact.factId)) {
        throw new Error('The game module continuation repeated a fact.');
      }
      seenFactIds.add(fact.factId);
    }
  }
  const records = [...previous.records, ...next.records];
  if (records.length > gameModuleMaximumAccumulatedRecords) {
    throw new Error('The game module result exceeds the bounded frontend window.');
  }
  return {
    ...next,
    diagnostics: distinctDiagnostics([...previous.diagnostics, ...next.diagnostics]),
    records
  };
}

function distinctDiagnostics(
  diagnostics: QueryGameModuleResponse['diagnostics']
): QueryGameModuleResponse['diagnostics'] {
  const seen = new Set<string>();
  return diagnostics.filter((diagnostic) => {
    const key = JSON.stringify([
      diagnostic.code,
      diagnostic.domain,
      diagnostic.field,
      diagnostic.message,
      diagnostic.severity
    ]);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  }).slice(0, 100);
}

function cacheByteCount(cache: ReadonlyMap<string, QueryGameModuleResponse>) {
  let count = 0;
  for (const [key, value] of cache) {
    count += textEncoder.encode(key).byteLength;
    count += textEncoder.encode(JSON.stringify(value)).byteLength;
  }
  return count;
}

function assertRevisionScope(revision: SemanticExploreRevision, scope: SemanticExploreScope) {
  if (
    revision.projectId !== scope.projectId ||
    scope.paths.selectedGame === null ||
    revision.gameFamily !== semanticExploreProjectGameFamily(scope.paths.selectedGame)
  ) {
    throw new StaleGameModuleResponseError();
  }
}

function assertRevision(actual: SemanticExploreRevision, expected: SemanticExploreRevision) {
  if (revisionIdentity(actual) !== revisionIdentity(expected)) {
    throw new StaleGameModuleResponseError();
  }
}

function revisionIdentity(revision: SemanticExploreRevision) {
  return JSON.stringify([
    revision.projectId,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
}

function snapshotMatchesRevision(
  snapshot: SemanticExploreSourceSnapshot,
  revision: SemanticExploreRevision
) {
  return revisionIdentity(snapshot.revision) === revisionIdentity(revision);
}

function snapshotIdentity(snapshot: SemanticExploreSourceSnapshot) {
  return JSON.stringify([
    snapshot.layer.kind,
    snapshot.layer.instanceId,
    revisionIdentity(snapshot.revision),
    snapshot.fingerprint
  ]);
}

class StaleGameModuleResponseError extends Error {}

function semanticErrorCode(error: unknown) {
  return error instanceof ProjectBridgeError && error.semanticCode
    ? String(error.semanticCode)
    : null;
}

function isStaleError(error: unknown) {
  return error instanceof StaleGameModuleResponseError ||
    semanticErrorCode(error) === semanticExploreErrorCodes.staleRevision;
}

function classifyError(error: unknown): GameModuleRequestError {
  switch (semanticErrorCode(error)) {
    case semanticExploreErrorCodes.invalidCursor:
      return 'cursor';
    case semanticExploreErrorCodes.invalidQuery:
      return 'invalidQuery';
    case semanticExploreErrorCodes.limitExceeded:
      return 'limit';
    case semanticExploreErrorCodes.unsupported:
      return 'unsupported';
    default:
      return 'generic';
  }
}

export function useGameModuleController(options: {
  bridge: GameModuleProjectBridgeApi;
  onStaleRevision?: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope | null;
}): GameModuleController {
  const storeRef = useRef<GameModuleControllerStore | null>(null);
  if (storeRef.current === null) {
    storeRef.current = new GameModuleControllerStore(options.bridge);
  }
  const store = storeRef.current;
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
  useLayoutEffect(() => store.setBridge(options.bridge), [options.bridge, store]);
  useLayoutEffect(
    () => store.setContext(options.scope, options.revision),
    [options.revision, options.scope, store]
  );
  useLayoutEffect(
    () => store.setOnStaleRevision(options.onStaleRevision),
    [options.onStaleRevision, store]
  );
  useEffect(() => () => store.cancel(), [store]);
  return useMemo(() => ({
    ...snapshot,
    cancel: () => store.cancel(),
    loadCapabilities: () => store.loadCapabilities(),
    loadMore: () => store.loadMore(),
    query: (value: GameModuleQueryOptions) => store.query(value),
    refresh: () => store.refresh(),
    refreshCapabilities: () => store.refreshCapabilities()
  }), [snapshot, store]);
}
