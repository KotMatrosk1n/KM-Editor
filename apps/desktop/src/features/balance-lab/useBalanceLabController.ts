/* SPDX-License-Identifier: GPL-3.0-only */

import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useSyncExternalStore
} from 'react';
import {
  balanceLabDefaultPageSize,
  balanceLabMaximumAccumulatedResults,
  balanceLabMaximumContinuationStartCount,
  balanceLabMaximumFindingsPerPage,
  type BalanceLabQueryResponse,
  type BalanceLabStudy
} from '../../bridge/balanceLabContracts';
import type { BalanceLabProjectBridgeApi } from '../../bridge/balanceLabProjectBridge';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope,
  SemanticExploreSourceSnapshot
} from '../../bridge/semanticExploreContracts';
import { semanticExploreProjectGameFamily } from '../../bridge/semanticExploreContracts';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import { semanticExploreErrorCodes } from '../../errorCodes';

export type BalanceLabLayer = 'base' | 'layered' | 'pending';
export type BalanceLabQueryStatus = 'idle' | 'loading' | 'ready' | 'error';
export type BalanceLabQueryError = 'cursor' | 'invalidQuery' | 'limit' | 'unsupported' | 'generic';

export type BalanceLabQueryState = {
  data: BalanceLabQueryResponse | null;
  error: BalanceLabQueryError | null;
  isAppending: boolean;
  status: BalanceLabQueryStatus;
};

export type BalanceLabQueryOptions = {
  layer: BalanceLabLayer;
  study: BalanceLabStudy;
};

export type BalanceLabControllerSnapshot = {
  activeQuery: BalanceLabQueryOptions | null;
  isQuerying: boolean;
  result: BalanceLabQueryState;
};

export type BalanceLabController = BalanceLabControllerSnapshot & {
  cancel: () => void;
  invalidate: () => void;
  loadMore: () => Promise<void>;
  query: (options: BalanceLabQueryOptions) => Promise<void>;
  refresh: () => Promise<void>;
};

type RequestToken = { epoch: number; id: number; queryGeneration: number };
const maximumCachedQueries = 4;
const maximumCacheCharacters = 256 * 1_024;

const idleQueryState = (): BalanceLabQueryState => ({
  data: null,
  error: null,
  isAppending: false,
  status: 'idle'
});

class BalanceLabControllerStore {
  private activeRequests = new Set<number>();
  private bridge: BalanceLabProjectBridgeApi;
  private cache = new Map<string, BalanceLabQueryResponse>();
  private contextKey: string | null = null;
  private epoch = 0;
  private listeners = new Set<() => void>();
  private nextRequestId = 1;
  private onStaleRevision: (() => void) | null = null;
  private queryGeneration = 0;
  private revision: SemanticExploreRevision | null = null;
  private scope: SemanticExploreScope | null = null;
  private snapshot: BalanceLabControllerSnapshot = {
    activeQuery: null,
    isQuerying: false,
    result: idleQueryState()
  };

  public constructor(bridge: BalanceLabProjectBridgeApi) {
    this.bridge = bridge;
  }

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public readonly getSnapshot = () => this.snapshot;

  public setBridge(bridge: BalanceLabProjectBridgeApi) {
    if (bridge === this.bridge) return;
    this.bridge = bridge;
    this.invalidate();
  }

  public setOnStaleRevision(callback: (() => void) | undefined) {
    this.onStaleRevision = callback ?? null;
  }

  public setContext(scope: SemanticExploreScope | null, revision: SemanticExploreRevision | null) {
    const nextKey = revision ? revisionIdentity(revision) : null;
    if (scope === this.scope && nextKey === this.contextKey) return;
    this.scope = scope;
    this.revision = revision;
    this.contextKey = nextKey;
    this.reset();
  }

  public invalidate() {
    this.cache.clear();
    this.reset();
  }

  public cancel() {
    this.epoch += 1;
    this.queryGeneration += 1;
    this.activeRequests.clear();
    this.snapshot = {
      ...this.snapshot,
      isQuerying: false,
      result: this.snapshot.result.data
        ? { ...this.snapshot.result, error: null, isAppending: false, status: 'ready' }
        : idleQueryState()
    };
    this.emit();
  }

  public async query(options: BalanceLabQueryOptions) {
    this.supersedeRequests();
    this.snapshot = { ...this.snapshot, activeQuery: options };
    const cached = this.readCache(options);
    if (cached) {
      this.snapshot = {
        activeQuery: options,
        isQuerying: false,
        result: { data: cached, error: null, isAppending: false, status: 'ready' }
      };
      this.emit();
      return;
    }
    await this.runQuery(options, false);
  }

  public async refresh() {
    const options = this.snapshot.activeQuery;
    if (!options) return;
    await this.runQuery(options, false);
  }

  public async loadMore() {
    const options = this.snapshot.activeQuery;
    if (!options) return;
    await this.runQuery(options, true);
  }

  private async runQuery(options: BalanceLabQueryOptions, append: boolean) {
    const previous = append ? this.snapshot.result.data : null;
    const cursor = previous?.nextCursor ?? undefined;
    if (
      append &&
      (!cursor || resultCount(previous) >= balanceLabMaximumContinuationStartCount)
    ) {
      return;
    }
    const limit = append
      ? Math.min(
          balanceLabDefaultPageSize,
          balanceLabMaximumAccumulatedResults -
            resultCount(previous) -
            balanceLabMaximumFindingsPerPage
        )
      : balanceLabDefaultPageSize;
    const token = this.begin(options, append);
    try {
      const context = this.requireContext();
      const response = await this.bridge.queryBalanceLab({
        ...(cursor ? { cursor } : {}),
        expectedRevision: context.revision,
        layer: options.layer,
        limit,
        scope: context.scope,
        study: options.study
      });
      if (!this.isCurrent(token)) return;
      assertBalanceLabResponse(response, options, context.revision);
      if (append && previous && response.queryFingerprint !== previous.queryFingerprint) {
        throw new Error('The Balance Lab continuation belongs to another query.');
      }
      const data = append && previous ? mergePages(previous, response) : response;
      this.writeCache(options, data);
      this.snapshot = {
        activeQuery: options,
        isQuerying: true,
        result: { data, error: null, isAppending: false, status: 'ready' }
      };
      this.emit();
    } catch (error) {
      this.fail(token, append ? previous : null, error);
    } finally {
      this.finish(token);
    }
  }

  private requireContext() {
    if (!this.scope || !this.revision) {
      throw new Error('Balance Lab requires an exact semantic project revision.');
    }
    assertRevisionScope(this.revision, this.scope);
    return { revision: this.revision, scope: this.scope };
  }

  private begin(options: BalanceLabQueryOptions, append: boolean): RequestToken {
    this.supersedeRequests();
    this.queryGeneration += 1;
    const token = { epoch: this.epoch, id: this.nextRequestId++, queryGeneration: this.queryGeneration };
    this.activeRequests.add(token.id);
    this.snapshot = {
      activeQuery: options,
      isQuerying: true,
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

  private finish(token: RequestToken) {
    if (this.activeRequests.delete(token.id)) {
      this.updateQuerying();
    }
  }

  private isCurrent(token: RequestToken) {
    return token.epoch === this.epoch && token.queryGeneration === this.queryGeneration;
  }

  private fail(token: RequestToken, retained: BalanceLabQueryResponse | null, error: unknown) {
    if (!this.isCurrent(token)) return;
    if (semanticErrorCode(error) === semanticExploreErrorCodes.staleRevision) {
      this.onStaleRevision?.();
      this.invalidate();
      return;
    }
    this.snapshot = {
      ...this.snapshot,
      result: {
        data: retained,
        error: classifyQueryError(error),
        isAppending: false,
        status: 'error'
      }
    };
    this.emit();
  }

  private updateQuerying() {
    const isQuerying = this.activeRequests.size > 0;
    if (isQuerying === this.snapshot.isQuerying) return;
    this.snapshot = { ...this.snapshot, isQuerying };
    this.emit();
  }

  private supersedeRequests() {
    this.queryGeneration += 1;
    this.activeRequests.clear();
  }

  private cacheKey(options: BalanceLabQueryOptions) {
    return this.revision
      ? JSON.stringify([revisionIdentity(this.revision), options.study, options.layer])
      : null;
  }

  private readCache(options: BalanceLabQueryOptions) {
    const key = this.cacheKey(options);
    if (!key) return null;
    const value = this.cache.get(key) ?? null;
    if (value) {
      this.cache.delete(key);
      this.cache.set(key, value);
    }
    return value;
  }

  private writeCache(options: BalanceLabQueryOptions, value: BalanceLabQueryResponse) {
    const key = this.cacheKey(options);
    const capability = value.capabilities.find((item) => item.study === options.study);
    if (!key || capability?.state === 'unavailable' || value.diagnostics.length > 0) return;
    this.cache.delete(key);
    this.cache.set(key, value);
    while (
      this.cache.size > maximumCachedQueries ||
      cacheCharacterCount(this.cache) > maximumCacheCharacters
    ) {
      const oldest = this.cache.keys().next().value as string | undefined;
      if (!oldest) break;
      this.cache.delete(oldest);
    }
  }

  private reset() {
    this.epoch += 1;
    this.queryGeneration += 1;
    this.activeRequests.clear();
    this.snapshot = { activeQuery: null, isQuerying: false, result: idleQueryState() };
    this.emit();
  }

  private emit() {
    for (const listener of this.listeners) listener();
  }
}

function mergePages(
  previous: BalanceLabQueryResponse,
  next: BalanceLabQueryResponse
): BalanceLabQueryResponse {
  const points = distinctBy([...previous.points, ...next.points], (point) => point.pointId);
  const findings = distinctBy(
    [...previous.findings, ...next.findings],
    (finding) => finding.findingId
  );
  if (
    points.length + findings.length > balanceLabMaximumAccumulatedResults
  ) {
    throw new Error('Balance Lab exceeded the bounded frontend result window.');
  }
  return {
    ...next,
    diagnostics: distinctBy(
      [...previous.diagnostics, ...next.diagnostics],
      (diagnostic) => JSON.stringify([
        diagnostic.code,
        diagnostic.domain,
        diagnostic.field,
        diagnostic.message,
        diagnostic.severity
      ])
    ).slice(0, 512),
    findings,
    points
  };
}

function assertBalanceLabResponse(
  response: BalanceLabQueryResponse,
  options: BalanceLabQueryOptions,
  expectedRevision: SemanticExploreRevision
) {
  assertRevision(response.revision, expectedRevision);
  if (
    response.snapshot.layer.kind !== options.layer ||
    !snapshotMatchesRevision(response.snapshot, expectedRevision)
  ) {
    throw new Error('Balance Lab returned another source layer.');
  }
  const capabilityStudies = new Set<BalanceLabStudy>();
  for (const capability of response.capabilities) {
    if (capabilityStudies.has(capability.study)) {
      throw new Error('Balance Lab returned duplicate study capabilities.');
    }
    capabilityStudies.add(capability.study);
  }
  if (!capabilityStudies.has(options.study)) {
    throw new Error('Balance Lab omitted the requested study capability.');
  }
  const records: SemanticExploreRecordRef[] = [];
  for (const point of response.points) {
    records.push(point.record);
    for (const fact of point.facts) records.push(...fact.evidence);
  }
  for (const finding of response.findings) {
    records.push(finding.record, ...finding.relatedRecords);
    for (const fact of finding.facts) records.push(...fact.evidence);
  }
  if (records.some((record) => record.gameFamily !== expectedRevision.gameFamily)) {
    throw new Error('Balance Lab returned a record from another game family.');
  }
}

function resultCount(response: BalanceLabQueryResponse | null | undefined) {
  return response ? response.points.length + response.findings.length : 0;
}

function cacheCharacterCount(cache: ReadonlyMap<string, BalanceLabQueryResponse>) {
  let count = 0;
  for (const [key, value] of cache) {
    count += key.length + JSON.stringify(value).length;
  }
  return count;
}

function distinctBy<T>(values: readonly T[], key: (value: T) => string) {
  const seen = new Set<string>();
  return values.filter((value) => {
    const id = key(value);
    if (seen.has(id)) return false;
    seen.add(id);
    return true;
  });
}

function revisionIdentity(revision: SemanticExploreRevision) {
  return JSON.stringify([
    revision.projectId,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
}

function assertRevisionScope(revision: SemanticExploreRevision, scope: SemanticExploreScope) {
  if (
    revision.projectId !== scope.projectId ||
    scope.paths.selectedGame === null ||
    revision.gameFamily !== semanticExploreProjectGameFamily(scope.paths.selectedGame)
  ) {
    throw new Error('Balance Lab revision belongs to another project.');
  }
}

function assertRevision(actual: SemanticExploreRevision, expected: SemanticExploreRevision) {
  if (revisionIdentity(actual) !== revisionIdentity(expected)) {
    throw new Error('Balance Lab returned a stale project revision.');
  }
}

function snapshotMatchesRevision(
  snapshot: SemanticExploreSourceSnapshot,
  expected: SemanticExploreRevision
) {
  try {
    assertRevision(snapshot.revision, expected);
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

function classifyQueryError(error: unknown): BalanceLabQueryError {
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

export function useBalanceLabController(options: {
  bridge: BalanceLabProjectBridgeApi;
  onStaleRevision?: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope | null;
}): BalanceLabController {
  const storeRef = useRef<BalanceLabControllerStore | null>(null);
  if (storeRef.current === null) {
    storeRef.current = new BalanceLabControllerStore(options.bridge);
  }
  const store = storeRef.current;
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
  useLayoutEffect(() => store.setBridge(options.bridge), [options.bridge, store]);
  useLayoutEffect(
    () => store.setOnStaleRevision(options.onStaleRevision),
    [options.onStaleRevision, store]
  );
  useLayoutEffect(
    () => store.setContext(options.scope, options.revision),
    [options.revision, options.scope, store]
  );
  useEffect(() => () => store.cancel(), [store]);
  const actions = useMemo(() => ({
    cancel: () => store.cancel(),
    invalidate: () => store.invalidate(),
    loadMore: () => store.loadMore(),
    query: (value: BalanceLabQueryOptions) => store.query(value),
    refresh: () => store.refresh()
  }), [store]);
  const invalidate = useCallback(actions.invalidate, [actions.invalidate]);
  return useMemo(() => ({ ...snapshot, ...actions, invalidate }), [actions, invalidate, snapshot]);
}
