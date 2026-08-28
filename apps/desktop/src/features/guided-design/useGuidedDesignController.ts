/* SPDX-License-Identifier: GPL-3.0-only */

import {
  useLayoutEffect,
  useMemo,
  useRef,
  useSyncExternalStore
} from 'react';
import {
  guidedDesignDefaultPageSize,
  guidedDesignMaximumAccumulatedResults,
  guidedDesignMaximumEligibleTargetWindow,
  guidedDesignMaximumFindings,
  guidedDesignMaximumMutations,
  type GuidedDesignCapabilitiesResponse,
  type GuidedDesignInput,
  type GuidedDesignPreviewResponse
} from '../../bridge/guidedDesignContracts';
import type { GuidedDesignProjectBridgeApi } from '../../bridge/guidedDesignProjectBridge';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision,
  SemanticExploreScope,
  SemanticExploreSourceSnapshot
} from '../../bridge/semanticExploreContracts';
import { semanticExploreProjectGameFamily } from '../../bridge/semanticExploreContracts';
import { guidedDesignErrorCodes, semanticExploreErrorCodes } from '../../errorCodes';
import { useDeferredUnmountCleanup } from '../../hooks/useDeferredUnmountCleanup';
import {
  ProjectQueryEpoch,
  runIndependentProjectRead,
  type ProjectQueryTicket
} from '../../utils/projectAsyncPolicy';

export type GuidedDesignQueryStatus = 'idle' | 'loading' | 'ready' | 'error';
export type GuidedDesignQueryError =
  | 'concurrentModification'
  | 'cursor'
  | 'invalidQuery'
  | 'limit'
  | 'staleProposal'
  | 'unsupported'
  | 'generic';

type QueryState<T> = {
  data: T | null;
  error: GuidedDesignQueryError | null;
  status: GuidedDesignQueryStatus;
};

export type GuidedDesignPreviewState = QueryState<GuidedDesignPreviewResponse> & {
  isAppending: boolean;
};

export type GuidedDesignControllerSnapshot = {
  activeInput: GuidedDesignInput | null;
  capabilities: QueryState<GuidedDesignCapabilitiesResponse>;
  isQuerying: boolean;
  preview: GuidedDesignPreviewState;
};

export type GuidedDesignController = GuidedDesignControllerSnapshot & {
  cancel: () => void;
  ensureCapabilities: () => Promise<void>;
  invalidate: () => void;
  loadMore: () => Promise<void>;
  previewDesign: (input: GuidedDesignInput, targetSearchText?: string | null) => Promise<void>;
  refresh: () => Promise<void>;
};

type RequestToken = ProjectQueryTicket<'query'> & { id: number };

const idleQuery = <T,>(): QueryState<T> => ({
  data: null,
  error: null,
  status: 'idle'
});

const idlePreview = (): GuidedDesignPreviewState => ({
  ...idleQuery<GuidedDesignPreviewResponse>(),
  isAppending: false
});

class GuidedDesignControllerStore {
  private activeRequests = new Set<number>();
  private activeTargetSearchText: string | null = null;
  private bridge: GuidedDesignProjectBridgeApi;
  private contextKey: string | null = null;
  private expectedChangeSetETag: string | null = null;
  private freshness = new ProjectQueryEpoch<'query'>();
  private isAuthoringContextReady = false;
  private listeners = new Set<() => void>();
  private nextRequestId = 1;
  private onStaleRevision: (() => void) | null = null;
  private revision: SemanticExploreRevision | null = null;
  private scope: SemanticExploreScope | null = null;
  private snapshot: GuidedDesignControllerSnapshot = {
    activeInput: null,
    capabilities: idleQuery<GuidedDesignCapabilitiesResponse>(),
    isQuerying: false,
    preview: idlePreview()
  };

  public constructor(bridge: GuidedDesignProjectBridgeApi) {
    this.bridge = bridge;
  }

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public readonly getSnapshot = () => this.snapshot;

  public setBridge(bridge: GuidedDesignProjectBridgeApi) {
    if (bridge === this.bridge) return;
    this.bridge = bridge;
    this.invalidate();
  }

  public setOnStaleRevision(callback: (() => void) | undefined) {
    this.onStaleRevision = callback ?? null;
  }

  public setAuthoringContextReady(isReady: boolean) {
    if (isReady === this.isAuthoringContextReady) return;
    this.isAuthoringContextReady = isReady;
    if (!isReady) {
      this.freshness.supersede('query');
      this.activeRequests.clear();
      this.snapshot = {
        ...this.snapshot,
        activeInput: null,
        isQuerying: false,
        preview: idlePreview()
      };
      this.activeTargetSearchText = null;
      this.emit();
    }
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

  public invalidate() {
    this.reset();
  }

  public cancel() {
    this.freshness.invalidateAll();
    this.activeRequests.clear();
    this.snapshot = {
      ...this.snapshot,
      isQuerying: false,
      capabilities: this.snapshot.capabilities.data
        ? { ...this.snapshot.capabilities, error: null, status: 'ready' }
        : idleQuery<GuidedDesignCapabilitiesResponse>(),
      preview: this.snapshot.preview.data
        ? { ...this.snapshot.preview, error: null, isAppending: false, status: 'ready' }
        : idlePreview()
    };
    this.emit();
  }

  public async ensureCapabilities() {
    if (
      this.snapshot.capabilities.status === 'loading' ||
      this.snapshot.capabilities.status === 'ready'
    ) {
      return;
    }
    const token = this.beginCapabilities();
    try {
      const scope = this.requireScope();
      const request = { scope };
      const response = await runIndependentProjectRead(
        'getGuidedDesignCapabilities',
        this.bridge,
        request,
        () => this.bridge.getGuidedDesignCapabilities(request)
      );
      if (!this.isCurrent(token)) return;
      assertCapabilitiesResponse(response, scope, this.revision);
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

  public async previewDesign(input: GuidedDesignInput, targetSearchText: string | null = null) {
    this.supersedeRequests();
    this.activeTargetSearchText = targetSearchText;
    this.snapshot = { ...this.snapshot, activeInput: input };
    await this.runPreview(input, false, targetSearchText);
  }

  public async refresh() {
    const input = this.snapshot.preview.data?.normalizedInput ?? this.snapshot.activeInput;
    if (!input) return;
    await this.runPreview(
      input,
      false,
      this.snapshot.preview.data?.normalizedTargetSearchText ?? this.activeTargetSearchText
    );
  }

  public async loadMore() {
    const input = this.snapshot.preview.data?.normalizedInput ?? this.snapshot.activeInput;
    if (!input) return;
    await this.runPreview(
      input,
      true,
      this.snapshot.preview.data?.normalizedTargetSearchText ?? null
    );
  }

  private async runPreview(
    input: GuidedDesignInput,
    append: boolean,
    targetSearchText: string | null
  ) {
    const previous = append ? this.snapshot.preview.data : null;
    if (
      append &&
      (!previous?.nextCursor || resultCount(previous) >= resultLimit(previous))
    ) {
      return;
    }
    const token = this.beginPreview(input, append);
    try {
      const context = this.requirePreviewContext();
      const requestedInput = previous?.normalizedInput ?? input;
      const requestedTargetSearchText =
        previous?.normalizedTargetSearchText ?? targetSearchText;
      const request = {
        cursor: previous?.nextCursor ?? null,
        expectedChangeSetETag: context.expectedChangeSetETag,
        expectedRevision: context.revision,
        input: requestedInput,
        layer: 'layered' as const,
        limit: Math.min(
          guidedDesignDefaultPageSize,
          resultLimit(previous) - resultCount(previous)
        ),
        proposalFingerprint: previous?.proposalFingerprint ?? null,
        proposalId: previous?.proposalId ?? null,
        scope: context.scope,
        targetSearchText: requestedTargetSearchText
      };
      const response = await runIndependentProjectRead(
        'previewGuidedDesign',
        this.bridge,
        request,
        () => this.bridge.previewGuidedDesign(request)
      );
      if (!this.isCurrent(token)) return;
      assertPreviewResponse(
        response,
        context.revision,
        context.snapshot,
        context.capabilities,
        requestedInput,
        requestedTargetSearchText
      );
      const data = previous ? mergePreviewPages(previous, response) : response;
      assertAccumulatedPreview(data);
      this.snapshot = {
        ...this.snapshot,
        activeInput: data.normalizedInput,
        preview: { data, error: null, isAppending: false, status: 'ready' }
      };
      this.activeTargetSearchText = data.normalizedTargetSearchText;
      this.emit();
    } catch (error) {
      this.failPreview(token, previous, error);
    } finally {
      this.finish(token);
    }
  }

  private requireScope() {
    if (!this.scope) throw new Error('Guided Design requires an open project scope.');
    return this.scope;
  }

  private requirePreviewContext() {
    const scope = this.requireScope();
    if (!this.isAuthoringContextReady) {
      throw new Error('Guided Design requires a settled change-set workspace.');
    }
    const capabilities = this.snapshot.capabilities.data;
    if (!capabilities) {
      throw new Error('Guided Design capabilities must be loaded before preview.');
    }
    const revision = this.revision ?? capabilities.revision;
    assertRevisionScope(revision, scope);
    assertRevision(capabilities.revision, revision);
    const layeredSnapshots = capabilities.snapshots.filter(
      (candidate) => candidate.layer.kind === 'layered'
    );
    const snapshot = layeredSnapshots[0];
    if (
      layeredSnapshots.length !== 1 ||
      !snapshot ||
      !snapshotMatchesRevision(snapshot, revision)
    ) {
      throw new Error('Guided Design requires its exact layered source snapshot.');
    }
    return {
      capabilities: capabilities.capabilities,
      expectedChangeSetETag: this.expectedChangeSetETag,
      revision,
      scope,
      snapshot
    };
  }

  private beginCapabilities(): RequestToken {
    this.supersedeRequests();
    const token = this.createToken();
    this.snapshot = {
      ...this.snapshot,
      isQuerying: true,
      capabilities: { data: null, error: null, status: 'loading' },
      preview: idlePreview()
    };
    this.activeTargetSearchText = null;
    this.emit();
    return token;
  }

  private beginPreview(input: GuidedDesignInput, append: boolean): RequestToken {
    this.supersedeRequests();
    const token = this.createToken();
    this.snapshot = {
      ...this.snapshot,
      activeInput: input,
      isQuerying: true,
      preview: {
        data: append ? this.snapshot.preview.data : null,
        error: null,
        isAppending: append,
        status: 'loading'
      }
    };
    this.emit();
    return token;
  }

  private createToken(): RequestToken {
    const token = { ...this.freshness.capture('query'), id: this.nextRequestId++ };
    this.activeRequests.add(token.id);
    return token;
  }

  private finish(token: RequestToken) {
    if (!this.activeRequests.delete(token.id)) return;
    const isQuerying = this.activeRequests.size > 0;
    if (isQuerying !== this.snapshot.isQuerying) {
      this.snapshot = { ...this.snapshot, isQuerying };
      this.emit();
    }
  }

  private isCurrent(token: RequestToken) {
    return this.freshness.isCurrent(token);
  }

  private failCapabilities(token: RequestToken, error: unknown) {
    if (!this.isCurrent(token)) return;
    if (semanticErrorCode(error) === semanticExploreErrorCodes.staleRevision) {
      this.onStaleRevision?.();
      this.invalidate();
      return;
    }
    this.snapshot = {
      ...this.snapshot,
      capabilities: { data: null, error: classifyQueryError(error), status: 'error' }
    };
    this.emit();
  }

  private failPreview(
    token: RequestToken,
    retained: GuidedDesignPreviewResponse | null,
    error: unknown
  ) {
    if (!this.isCurrent(token)) return;
    if (semanticErrorCode(error) === semanticExploreErrorCodes.staleRevision) {
      this.onStaleRevision?.();
      this.invalidate();
      return;
    }
    this.snapshot = {
      ...this.snapshot,
      preview: {
        data: retained,
        error: classifyQueryError(error),
        isAppending: false,
        status: 'error'
      }
    };
    this.emit();
  }

  private supersedeRequests() {
    this.freshness.supersede('query');
    this.activeRequests.clear();
  }

  private reset() {
    this.freshness.invalidateAll();
    this.activeRequests.clear();
    this.activeTargetSearchText = null;
    this.snapshot = {
      activeInput: null,
      capabilities: idleQuery<GuidedDesignCapabilitiesResponse>(),
      isQuerying: false,
      preview: idlePreview()
    };
    this.emit();
  }

  private emit() {
    for (const listener of this.listeners) listener();
  }
}

function assertCapabilitiesResponse(
  response: GuidedDesignCapabilitiesResponse,
  scope: SemanticExploreScope,
  expectedRevision: SemanticExploreRevision | null
) {
  assertRevisionScope(response.revision, scope);
  if (expectedRevision) assertRevision(response.revision, expectedRevision);
  const layeredSnapshots = response.snapshots.filter(
    (snapshot) => snapshot.layer.kind === 'layered'
  );
  const layeredSnapshot = layeredSnapshots[0];
  if (
    layeredSnapshots.length !== 1 ||
    !layeredSnapshot ||
    !snapshotMatchesRevision(layeredSnapshot, response.revision)
  ) {
    throw new Error('Guided Design did not return its exact layered source snapshot.');
  }
}

function assertPreviewResponse(
  response: GuidedDesignPreviewResponse,
  expectedRevision: SemanticExploreRevision,
  expectedSnapshot: SemanticExploreSourceSnapshot,
  expectedCapabilities: GuidedDesignCapabilitiesResponse['capabilities'],
  requestedInput: GuidedDesignInput,
  requestedTargetSearchText: string | null
) {
  assertRevision(response.revision, expectedRevision);
  if (
    response.snapshot.layer.kind !== 'layered' ||
    !snapshotMatchesRevision(response.snapshot, expectedRevision) ||
    snapshotIdentity(response.snapshot) !== snapshotIdentity(expectedSnapshot)
  ) {
    throw new Error('Guided Design returned another source layer.');
  }
  if (response.seed !== response.normalizedInput.seed) {
    throw new Error('Guided Design returned conflicting normalized seeds.');
  }
  if (JSON.stringify(response.capabilities) !== JSON.stringify(expectedCapabilities)) {
    throw new Error('Guided Design capability coverage changed before preview completed.');
  }
  assertNormalizedInput(response.normalizedInput, requestedInput);
  if (response.normalizedTargetSearchText !== requestedTargetSearchText) {
    throw new Error('Guided Design changed the requested target search.');
  }
  if (
    response.mutations.length > response.totalMutationCount ||
    response.findings.length > response.totalFindingCount
  ) {
    throw new Error('Guided Design returned inconsistent bounded result totals.');
  }
  const records: SemanticExploreRecordRef[] = [...response.affectedRecords];
  for (const option of response.eligibleTargets) records.push(option.record);
  for (const mutation of response.mutations) {
    records.push(mutation.record);
    if (mutation.pinRecord) records.push(mutation.pinRecord);
  }
  for (const finding of response.findings) {
    if (finding.record) records.push(finding.record);
    records.push(...finding.relatedRecords);
  }
  for (const target of response.normalizedInput.targets) records.push(target);
  for (const pin of response.normalizedInput.pins) records.push(pin.record);
  if (records.some((record) => record.gameFamily !== expectedRevision.gameFamily)) {
    throw new Error('Guided Design returned a record from another game family.');
  }
  if (!distinct(response.mutations, (mutation) => mutation.mutationId)) {
    throw new Error('Guided Design returned duplicate mutations.');
  }
  if (!distinct(response.findings, (finding) => finding.findingId)) {
    throw new Error('Guided Design returned duplicate findings.');
  }
  if (!distinct(response.eligibleTargets, (option) => recordIdentity(option.record))) {
    throw new Error('Guided Design returned duplicate eligible targets.');
  }
}

function assertNormalizedInput(
  normalized: GuidedDesignInput,
  requested: GuidedDesignInput
) {
  const scalarFields = [
    'archetype',
    'delta',
    'kind',
    'maximumValue',
    'minimumValue',
    'multiplierBasisPoints',
    'rounding'
  ] as const;
  if (scalarFields.some((field) => normalized[field] !== requested[field])) {
    throw new Error('Guided Design changed a requested proposal constraint.');
  }
  if (
    requested.seed !== null
      ? normalized.seed !== requested.seed
      : requested.kind !== 'pokemonBaseStatShuffle' && normalized.seed !== null
  ) {
    throw new Error('Guided Design changed a requested seed.');
  }
  if (
    requested.fieldKeys.length > 0 &&
    !sameStringSet(normalized.fieldKeys, requested.fieldKeys)
  ) {
    throw new Error('Guided Design changed the requested field constraints.');
  }
  if (!sameStringSet(
    normalized.targets.map(recordIdentity),
    requested.targets.map(recordIdentity)
  )) {
    throw new Error('Guided Design changed the exact requested targets.');
  }
  if (!sameStringSet(
    normalized.pins.map((pin) => JSON.stringify([
      recordIdentity(pin.record),
      pin.fieldKey,
      pin.canonicalValue
    ])),
    requested.pins.map((pin) => JSON.stringify([
      recordIdentity(pin.record),
      pin.fieldKey,
      pin.canonicalValue
    ]))
  )) {
    throw new Error('Guided Design changed the exact requested pins.');
  }
}

function sameStringSet(left: readonly string[], right: readonly string[]) {
  if (left.length !== right.length) return false;
  const sortedLeft = [...left].sort();
  const sortedRight = [...right].sort();
  return sortedLeft.every((value, index) => value === sortedRight[index]);
}

function mergePreviewPages(
  previous: GuidedDesignPreviewResponse,
  next: GuidedDesignPreviewResponse
): GuidedDesignPreviewResponse {
  const stableFields = [
    'authoringContextFingerprint',
    'eligibleTargetWindowCapped',
    'proposalFingerprint',
    'proposalId',
    'queryFingerprint',
    'seed',
    'selectionRequired',
    'totalEligibleTargetCount',
    'totalFindingCount',
    'totalMutationCount'
  ] as const;
  if (stableFields.some((field) => previous[field] !== next[field])) {
    throw new Error('Guided Design continuation identity changed between pages.');
  }
  if (
    JSON.stringify(previous.normalizedInput) !== JSON.stringify(next.normalizedInput) ||
    previous.normalizedTargetSearchText !== next.normalizedTargetSearchText ||
    JSON.stringify(previous.exports) !== JSON.stringify(next.exports) ||
    JSON.stringify(previous.affectedRecords) !== JSON.stringify(next.affectedRecords) ||
    JSON.stringify(previous.capabilities) !== JSON.stringify(next.capabilities) ||
    revisionIdentity(previous.revision) !== revisionIdentity(next.revision) ||
    snapshotIdentity(previous.snapshot) !== snapshotIdentity(next.snapshot) ||
    previous.canImport !== next.canImport
  ) {
    throw new Error('Guided Design continuation changed its normalized proposal.');
  }
  const nextPageResultCount = next.selectionRequired
    ? next.eligibleTargets.length
    : next.mutations.length + next.findings.length;
  if (next.nextCursor !== null && nextPageResultCount === 0) {
    throw new Error('Guided Design continuation did not advance its result window.');
  }
  const mutations = distinctBy(
    [...previous.mutations, ...next.mutations],
    (mutation) => mutation.mutationId
  );
  const findings = distinctBy(
    [...previous.findings, ...next.findings],
    (finding) => finding.findingId
  );
  const eligibleTargets = next.selectionRequired
    ? distinctBy(
        [...previous.eligibleTargets, ...next.eligibleTargets],
        (option) => recordIdentity(option.record)
      )
    : [];
  if (
    mutations.length !== previous.mutations.length + next.mutations.length ||
    findings.length !== previous.findings.length + next.findings.length ||
    eligibleTargets.length !== previous.eligibleTargets.length + next.eligibleTargets.length
  ) {
    throw new Error('Guided Design continuation repeated a previously loaded result.');
  }
  if (
    mutations.length > guidedDesignMaximumMutations ||
    findings.length > guidedDesignMaximumFindings ||
    mutations.length + findings.length > guidedDesignMaximumAccumulatedResults ||
    eligibleTargets.length > guidedDesignMaximumEligibleTargetWindow
  ) {
    throw new Error('Guided Design exceeded the bounded frontend result window.');
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
    eligibleTargets,
    mutations
  };
}

function assertAccumulatedPreview(response: GuidedDesignPreviewResponse) {
  if (response.selectionRequired) {
    const exposedTargetCount = Math.min(
      response.totalEligibleTargetCount,
      guidedDesignMaximumEligibleTargetWindow
    );
    const hasRemainingTargets = response.eligibleTargets.length < exposedTargetCount;
    if (
      response.eligibleTargets.length > exposedTargetCount ||
      (response.nextCursor !== null) !== hasRemainingTargets ||
      (hasRemainingTargets && response.eligibleTargets.length === 0)
    ) {
      throw new Error('Guided Design returned an incomplete eligible-target window.');
    }
    return;
  }
  const hasRemainingResults =
    response.mutations.length < response.totalMutationCount ||
    response.findings.length < response.totalFindingCount;
  if (
    response.mutations.length > response.totalMutationCount ||
    response.findings.length > response.totalFindingCount ||
    (response.nextCursor !== null) !== hasRemainingResults ||
    (hasRemainingResults && response.mutations.length + response.findings.length === 0)
  ) {
    throw new Error('Guided Design returned an incomplete proposal result window.');
  }
}

function resultCount(response: GuidedDesignPreviewResponse | null | undefined) {
  return response
    ? response.selectionRequired
      ? response.eligibleTargets.length
      : response.mutations.length + response.findings.length
    : 0;
}

function resultLimit(response: GuidedDesignPreviewResponse | null | undefined) {
  return response?.selectionRequired
    ? guidedDesignMaximumEligibleTargetWindow
    : guidedDesignMaximumAccumulatedResults;
}

function distinct<T>(values: readonly T[], key: (value: T) => string) {
  return new Set(values.map(key)).size === values.length;
}

function distinctBy<T>(values: readonly T[], key: (value: T) => string) {
  const seen = new Set<string>();
  return values.filter((value) => {
    const identity = key(value);
    if (seen.has(identity)) return false;
    seen.add(identity);
    return true;
  });
}

function recordIdentity(record: SemanticExploreRecordRef) {
  return JSON.stringify([
    record.gameFamily,
    record.domain,
    record.recordKind.key,
    record.recordKind.schemaVersion,
    record.recordId,
    record.subrecordId
  ]);
}

function contextIdentity(options: {
  authoringContextRevision: string | null;
  expectedChangeSetETag: string | null;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope | null;
}) {
  if (!options.scope) return null;
  return JSON.stringify([
    options.scope,
    options.revision ? revisionIdentity(options.revision) : null,
    options.expectedChangeSetETag,
    options.authoringContextRevision
  ]);
}

function revisionIdentity(revision: SemanticExploreRevision) {
  return JSON.stringify([
    revision.projectId,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
}

function snapshotIdentity(snapshot: SemanticExploreSourceSnapshot) {
  return JSON.stringify([
    snapshot.fingerprint,
    snapshot.layer.kind,
    snapshot.layer.instanceId,
    revisionIdentity(snapshot.revision)
  ]);
}

function assertRevisionScope(revision: SemanticExploreRevision, scope: SemanticExploreScope) {
  if (
    revision.projectId !== scope.projectId ||
    scope.paths.selectedGame === null ||
    revision.gameFamily !== semanticExploreProjectGameFamily(scope.paths.selectedGame)
  ) {
    throw new Error('Guided Design revision belongs to another project.');
  }
}

function assertRevision(actual: SemanticExploreRevision, expected: SemanticExploreRevision) {
  if (revisionIdentity(actual) !== revisionIdentity(expected)) {
    throw new Error('Guided Design returned a stale project revision.');
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

function classifyQueryError(error: unknown): GuidedDesignQueryError {
  switch (semanticErrorCode(error)) {
    case guidedDesignErrorCodes.staleProposal:
      return 'staleProposal';
    case 'KM-WORKSPACE-CONCURRENT-MODIFICATION':
      return 'concurrentModification';
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

export function useGuidedDesignController(options: {
  authoringContextRevision: string | null;
  bridge: GuidedDesignProjectBridgeApi;
  expectedChangeSetETag: string | null;
  isAuthoringContextReady: boolean;
  onStaleRevision?: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope | null;
}): GuidedDesignController {
  const storeRef = useRef<GuidedDesignControllerStore | null>(null);
  if (storeRef.current === null) {
    storeRef.current = new GuidedDesignControllerStore(options.bridge);
  }
  const store = storeRef.current;
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
  useLayoutEffect(() => store.setBridge(options.bridge), [options.bridge, store]);
  useLayoutEffect(
    () => store.setOnStaleRevision(options.onStaleRevision),
    [options.onStaleRevision, store]
  );
  useLayoutEffect(
    () => store.setAuthoringContextReady(options.isAuthoringContextReady),
    [options.isAuthoringContextReady, store]
  );
  useLayoutEffect(
    () => store.setContext({
      authoringContextRevision: options.authoringContextRevision,
      expectedChangeSetETag: options.expectedChangeSetETag,
      revision: options.revision,
      scope: options.scope
    }),
    [
      options.authoringContextRevision,
      options.expectedChangeSetETag,
      options.revision,
      options.scope,
      store
    ]
  );
  useDeferredUnmountCleanup(() => store.cancel());
  const actions = useMemo(() => ({
    cancel: () => store.cancel(),
    ensureCapabilities: () => store.ensureCapabilities(),
    invalidate: () => store.invalidate(),
    loadMore: () => store.loadMore(),
    previewDesign: (input: GuidedDesignInput, targetSearchText?: string | null) => (
      store.previewDesign(input, targetSearchText)
    ),
    refresh: () => store.refresh()
  }), [store]);
  return useMemo(() => ({ ...snapshot, ...actions }), [actions, snapshot]);
}
