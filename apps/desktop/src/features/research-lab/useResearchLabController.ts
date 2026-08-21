/* SPDX-License-Identifier: GPL-3.0-only */

import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useSyncExternalStore
} from 'react';
import { ProjectBridgeError } from '../../bridge/projectBridgeError';
import {
  researchAnnotationTargetIdentity,
  researchBase64ByteLength,
  researchLabDefaultPageSize,
  researchLabMaximumAccumulatedFindings,
  researchLabMaximumAggregateRanges,
  researchLabRegistrationLifetimeMinutes,
  researchPortableCaseFold,
  researchRevisionIdentity,
  type CompareResearchSourcesResponse,
  type MutateResearchAnnotationsResponse,
  type OpenResearchSourceResponse,
  type ReadResearchAnnotationsResponse,
  type ReadResearchByteWindowResponse,
  type ReadResearchLabCapabilitiesResponse,
  type ResearchAnnotation,
  type ResearchAnnotationDraft,
  type ResearchCapability,
  type ResearchFileFinding
} from '../../bridge/researchLabContracts';
import type { ResearchLabProjectBridgeApi } from '../../bridge/researchLabProjectBridge';
import type {
  SemanticExploreRevision,
  SemanticExploreScope
} from '../../bridge/semanticExploreContracts';
import { semanticExploreProjectGameFamily } from '../../bridge/semanticExploreContracts';
import {
  projectBridgeErrorCodes,
  researchLabErrorCodes,
  semanticExploreErrorCodes
} from '../../errorCodes';

export type ResearchLabStatus = 'idle' | 'loading' | 'ready' | 'error';
export type ResearchLabError =
  | 'comparisonStale'
  | 'conflict'
  | 'cursor'
  | 'invalidQuery'
  | 'limit'
  | 'sourceExpired'
  | 'sourceRejected'
  | 'unsupported'
  | 'generic';

export type ResearchCapabilitiesState = {
  data: ReadResearchLabCapabilitiesResponse | null;
  error: ResearchLabError | null;
  status: ResearchLabStatus;
};

export type ResearchSourceState = {
  data: OpenResearchSourceResponse | null;
  error: ResearchLabError | null;
  status: ResearchLabStatus;
};

export type ResearchComparisonState = {
  data: CompareResearchSourcesResponse | null;
  error: ResearchLabError | null;
  isAppending: boolean;
  selectedRelativePaths: readonly string[];
  status: ResearchLabStatus;
};

export type ResearchByteWindowState = {
  data: ReadResearchByteWindowResponse | null;
  error: ResearchLabError | null;
  findingId: string | null;
  status: ResearchLabStatus;
};

export type ResearchAnnotationsState = {
  data: ReadResearchAnnotationsResponse | null;
  error: ResearchLabError | null;
  isSaving: boolean;
  status: ResearchLabStatus;
};

export type ResearchLabControllerSnapshot = {
  annotations: ResearchAnnotationsState;
  byteWindow: ResearchByteWindowState;
  capabilities: ResearchCapabilitiesState;
  comparison: ResearchComparisonState;
  isBusy: boolean;
  sources: readonly [ResearchSourceState, ResearchSourceState];
};

export type ResearchLabController = ResearchLabControllerSnapshot & {
  cancel: () => void;
  clearByteWindow: () => void;
  clearSource: (slot: 0 | 1) => Promise<void>;
  compare: (selectedRelativePaths?: readonly string[]) => Promise<void>;
  deleteAnnotation: (annotationId: string) => Promise<void>;
  expireSources: () => void;
  loadAnnotations: () => Promise<void>;
  loadByteWindow: (
    finding: ResearchFileFinding,
    offset: number,
    length: number
  ) => Promise<void>;
  loadCapabilities: () => Promise<void>;
  loadMore: () => Promise<void>;
  openSource: (slot: 0 | 1, rootPath: string) => Promise<void>;
  refreshAnnotations: () => Promise<void>;
  refreshCapabilities: () => Promise<void>;
  upsertAnnotation: (draft: ResearchAnnotationDraft) => Promise<void>;
};

type RequestChannel =
  | 'annotations'
  | 'byteWindow'
  | 'capabilities'
  | 'comparison'
  | 'source0'
  | 'source1';
type RequestToken = {
  channel: RequestChannel;
  epoch: number;
  generation: number;
  id: number;
};

const requestChannels: readonly RequestChannel[] = [
  'annotations',
  'byteWindow',
  'capabilities',
  'comparison',
  'source0',
  'source1'
];

const idleCapabilities = (): ResearchCapabilitiesState => ({
  data: null,
  error: null,
  status: 'idle'
});
const idleSource = (): ResearchSourceState => ({ data: null, error: null, status: 'idle' });
const idleComparison = (): ResearchComparisonState => ({
  data: null,
  error: null,
  isAppending: false,
  selectedRelativePaths: [],
  status: 'idle'
});
const idleByteWindow = (): ResearchByteWindowState => ({
  data: null,
  error: null,
  findingId: null,
  status: 'idle'
});
const idleAnnotations = (): ResearchAnnotationsState => ({
  data: null,
  error: null,
  isSaving: false,
  status: 'idle'
});

class ResearchLabControllerStore {
  private activeRequests = new Set<number>();
  private bridge: ResearchLabProjectBridgeApi;
  private comparisonCursors = new Set<string>();
  private contextKey: string | null = null;
  private disposalTimer: ReturnType<typeof setTimeout> | null = null;
  private epoch = 0;
  private generations = new Map<RequestChannel, number>(
    requestChannels.map((channel) => [channel, 0])
  );
  private listeners = new Set<() => void>();
  private nextRequestId = 1;
  private onStaleRevision: (() => void) | null = null;
  private revokedSourceIds = new Set<string>();
  private revision: SemanticExploreRevision | null = null;
  private scope: SemanticExploreScope | null = null;
  private snapshot: ResearchLabControllerSnapshot = {
    annotations: idleAnnotations(),
    byteWindow: idleByteWindow(),
    capabilities: idleCapabilities(),
    comparison: idleComparison(),
    isBusy: false,
    sources: [idleSource(), idleSource()]
  };

  public constructor(bridge: ResearchLabProjectBridgeApi) {
    this.bridge = bridge;
  }

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public readonly getSnapshot = () => this.snapshot;

  public setBridge(bridge: ResearchLabProjectBridgeApi) {
    if (this.bridge === bridge) return;
    this.releaseRegisteredSources();
    this.bridge = bridge;
    this.reset();
  }

  public setContext(scope: SemanticExploreScope | null, revision: SemanticExploreRevision | null) {
    const nextKey = revision ? researchRevisionIdentity(revision) : null;
    if (this.scope === scope && this.contextKey === nextKey) return;
    this.releaseRegisteredSources();
    this.scope = scope;
    this.revision = revision;
    this.contextKey = nextKey;
    this.reset();
  }

  public setOnStaleRevision(callback: (() => void) | undefined) {
    this.onStaleRevision = callback ?? null;
  }

  public cancelScheduledDispose() {
    if (this.disposalTimer === null) return;
    clearTimeout(this.disposalTimer);
    this.disposalTimer = null;
  }

  public scheduleDispose() {
    this.cancel();
    this.cancelScheduledDispose();
    this.disposalTimer = setTimeout(() => {
      this.disposalTimer = null;
      this.releaseRegisteredSources();
    }, 0);
  }

  public cancel() {
    this.epoch += 1;
    for (const channel of requestChannels) this.advance(channel);
    this.activeRequests.clear();
    this.snapshot = {
      ...this.snapshot,
      annotations: retainAnnotationsAfterCancel(this.snapshot.annotations),
      byteWindow: idleByteWindow(),
      capabilities: retainCapabilitiesAfterCancel(this.snapshot.capabilities),
      comparison: retainComparisonAfterCancel(this.snapshot.comparison),
      isBusy: false,
      sources: this.snapshot.sources.map(retainSourceAfterCancel) as [
        ResearchSourceState,
        ResearchSourceState
      ]
    };
    this.emit();
  }

  public async loadCapabilities() {
    if (this.snapshot.capabilities.status === 'loading') return;
    const token = this.begin('capabilities');
    this.snapshot = {
      ...this.snapshot,
      capabilities: { data: null, error: null, status: 'loading' }
    };
    this.emit();
    try {
      const context = this.requireContext();
      const response = await this.bridge.getResearchLabCapabilities({ scope: context.scope });
      if (!this.isCurrent(token)) return;
      assertCapabilitiesResponse(response, context.revision);
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
    this.invalidateChannel('capabilities');
    this.invalidateResearchSources();
    this.snapshot = {
      ...this.snapshot,
      capabilities: idleCapabilities()
    };
    this.emit();
    await this.loadCapabilities();
  }

  public async openSource(slot: 0 | 1, rootPath: string) {
    this.requireCapability('sourceComparison');
    const channel = slot === 0 ? 'source0' : 'source1';
    const retained = this.snapshot.sources[slot];
    const token = this.begin(channel);
    const nextSources = [...this.snapshot.sources] as [ResearchSourceState, ResearchSourceState];
    nextSources[slot] = { data: retained.data, error: null, status: 'loading' };
    this.invalidateComparison();
    this.snapshot = { ...this.snapshot, sources: nextSources };
    this.emit();
    let acceptedResponse: OpenResearchSourceResponse | null = null;
    let requestContext: {
      revision: SemanticExploreRevision;
      scope: SemanticExploreScope;
    } | null = null;
    try {
      const context = this.requireContext();
      requestContext = context;
      const requestStartedAt = Date.now();
      const response = await this.bridge.openResearchSource({
        expectedRevision: context.revision,
        replaceSourceId: retained.data?.sourceId ?? null,
        rootPath,
        scope: context.scope
      });
      acceptedResponse = response;
      if (retained.data !== null) {
        this.revokedSourceIds.add(retained.data.sourceId);
      }
      const responseReceivedAt = Date.now();
      if (!this.isCurrent(token)) {
        this.releaseSource(response.sourceId, context.scope, context.revision);
        const current = this.snapshot.sources[slot];
        if (
          retained.data !== null &&
          current.data?.sourceId === retained.data.sourceId
        ) {
          const staleSources = [...this.snapshot.sources] as [
            ResearchSourceState,
            ResearchSourceState
          ];
          staleSources[slot] = { data: null, error: 'generic', status: 'error' };
          this.snapshot = { ...this.snapshot, sources: staleSources };
          this.emit();
        }
        return;
      }
      assertRevision(response.revision, context.revision);
      assertSourceExpiration(response.expiresAtUtc, requestStartedAt, responseReceivedAt);
      const other = this.snapshot.sources[slot === 0 ? 1 : 0].data;
      if (other?.sourceId === response.sourceId) {
        throw new Error('Research comparison sources must have distinct physical identities.');
      }
      const updated = [...this.snapshot.sources] as [ResearchSourceState, ResearchSourceState];
      updated[slot] = { data: response, error: null, status: 'ready' };
      this.snapshot = { ...this.snapshot, sources: updated };
      this.emit();
    } catch (error) {
      if (acceptedResponse && requestContext) {
        this.releaseSource(
          acceptedResponse.sourceId,
          requestContext.scope,
          requestContext.revision
        );
      }
      const classified = classifyError(error);
      if (
        acceptedResponse === null &&
        retained.data !== null &&
        !this.isCurrent(token) &&
        !canSafelyRetainSourceAfterFailure(classified)
      ) {
        this.revokedSourceIds.add(retained.data.sourceId);
        this.clearRevokedSource(slot, retained.data.sourceId, classified);
      }
      this.failSource(token, slot, acceptedResponse ? idleSource() : retained, error);
    } finally {
      this.finish(token);
    }
  }

  public async clearSource(slot: 0 | 1) {
    const retained = this.snapshot.sources[slot];
    if (!retained.data) {
      const sources = [...this.snapshot.sources] as [ResearchSourceState, ResearchSourceState];
      sources[slot] = idleSource();
      this.snapshot = { ...this.snapshot, sources };
      this.emit();
      return;
    }
    const channel = slot === 0 ? 'source0' : 'source1';
    const token = this.begin(channel);
    this.invalidateComparison();
    const sources = [...this.snapshot.sources] as [ResearchSourceState, ResearchSourceState];
    sources[slot] = { data: retained.data, error: null, status: 'loading' };
    this.snapshot = { ...this.snapshot, sources };
    this.emit();
    try {
      const context = this.requireContext();
      const response = await this.bridge.closeResearchSource({
        expectedRevision: context.revision,
        scope: context.scope,
        sourceId: retained.data.sourceId
      });
      this.revokedSourceIds.add(retained.data.sourceId);
      assertRevision(response.revision, context.revision);
      if (response.sourceId !== retained.data.sourceId) {
        throw new Error('The closed research source did not match the exact request.');
      }
      if (!this.isCurrent(token)) {
        const current = this.snapshot.sources[slot];
        if (
          current.data?.sourceId === retained.data.sourceId
        ) {
          const staleSources = [...this.snapshot.sources] as [
            ResearchSourceState,
            ResearchSourceState
          ];
          staleSources[slot] = idleSource();
          this.snapshot = { ...this.snapshot, sources: staleSources };
          this.emit();
        }
        return;
      }
      const updated = [...this.snapshot.sources] as [ResearchSourceState, ResearchSourceState];
      updated[slot] = idleSource();
      this.snapshot = { ...this.snapshot, sources: updated };
      this.emit();
    } catch (error) {
      if (!this.isCurrent(token)) {
        this.revokedSourceIds.add(retained.data.sourceId);
        this.clearRevokedSource(slot, retained.data.sourceId, classifyError(error));
      }
      this.failSource(token, slot, retained, error);
    } finally {
      this.finish(token);
    }
  }

  public expireSources() {
    const now = Date.now();
    const expiredSlots = this.snapshot.sources.map((source) => (
      source.data !== null && Date.parse(source.data.expiresAtUtc) <= now
    ));
    if (!expiredSlots.some(Boolean)) return;
    expiredSlots.forEach((expired, slot) => {
      if (expired) this.invalidateChannel(slot === 0 ? 'source0' : 'source1');
    });
    this.invalidateComparison();
    const sources = this.snapshot.sources.map((source, slot) => (
      expiredSlots[slot]
        ? { data: null, error: 'sourceExpired' as const, status: 'error' as const }
        : source
    )) as [ResearchSourceState, ResearchSourceState];
    this.snapshot = {
      ...this.snapshot,
      comparison: { ...idleComparison(), error: 'sourceExpired', status: 'error' },
      sources
    };
    this.emit();
  }

  public async compare(selectedRelativePaths: readonly string[] = []) {
    this.requireCapability('sourceComparison');
    this.requireSourceIds();
    this.comparisonCursors.clear();
    this.invalidateChannel('comparison');
    this.invalidateChannel('byteWindow');
    this.snapshot = {
      ...this.snapshot,
      byteWindow: idleByteWindow(),
      comparison: {
        data: null,
        error: null,
        isAppending: false,
        selectedRelativePaths: [...selectedRelativePaths],
        status: 'loading'
      }
    };
    this.emit();
    await this.runComparison(false);
  }

  public async loadMore() {
    const current = this.snapshot.comparison.data;
    if (
      !current?.nextCursor ||
      current.items.length + researchLabDefaultPageSize >
        researchLabMaximumAccumulatedFindings ||
      this.snapshot.comparison.isAppending
    ) return;
    await this.runComparison(true);
  }

  private async runComparison(append: boolean) {
    this.requireCapability('sourceComparison');
    const expectedSemanticProjection = this.requireCapabilityDescriptor('semanticProjection');
    const sourceIds = this.requireSourceIds();
    const retained = append ? this.snapshot.comparison.data : null;
    const cursor = retained?.nextCursor ?? null;
    const selectedRelativePaths = this.snapshot.comparison.selectedRelativePaths;
    const limit = researchLabDefaultPageSize;
    const token = this.begin('comparison');
    this.snapshot = {
      ...this.snapshot,
      comparison: {
        ...this.snapshot.comparison,
        data: retained,
        error: null,
        isAppending: append,
        status: 'loading'
      }
    };
    this.emit();
    try {
      const context = this.requireContext();
      const response = await this.bridge.compareResearchSources({
        cursor,
        expectedRevision: context.revision,
        limit,
        scope: context.scope,
        selectedRelativePaths: [...selectedRelativePaths],
        sourceIds
      });
      if (!this.isCurrent(token)) return;
      assertComparisonResponse({
        cursor,
        expectedRevision: context.revision,
        limit,
        response,
        seenCursors: this.comparisonCursors,
        retained,
        expectedSemanticProjection,
        selectedRelativePaths,
        sourceIds
      });
      const data = retained ? mergeComparisonPages(retained, response) : response;
      if (cursor !== null) this.comparisonCursors.add(cursor);
      this.snapshot = {
        ...this.snapshot,
        comparison: {
          data,
          error: null,
          isAppending: false,
          selectedRelativePaths,
          status: 'ready'
        }
      };
      this.emit();
    } catch (error) {
      this.failComparison(token, retained, error);
    } finally {
      this.finish(token);
    }
  }

  public async loadByteWindow(
    finding: ResearchFileFinding,
    offset: number,
    length: number
  ) {
    this.requireCapability('byteWindows');
    const comparison = this.requireComparison();
    const storedFinding = comparison.items.find(
      (candidate) => candidate.findingId === finding.findingId
    );
    if (!storedFinding) {
      throw new Error('The byte-window finding is outside the current comparison.');
    }
    const token = this.begin('byteWindow');
    this.snapshot = {
      ...this.snapshot,
      byteWindow: { data: null, error: null, findingId: finding.findingId, status: 'loading' }
    };
    this.emit();
    try {
      const context = this.requireContext();
      const response = await this.bridge.readResearchByteWindow({
        comparisonId: comparison.comparisonId,
        expectedComparisonFingerprint: comparison.comparisonFingerprint,
        expectedRevision: context.revision,
        length,
        offset,
        relativePath: storedFinding.relativePath,
        scope: context.scope
      });
      if (!this.isCurrent(token)) return;
      assertByteWindowResponse(response, {
        comparisonFingerprint: comparison.comparisonFingerprint,
        finding: storedFinding,
        length,
        offset,
        relativePath: storedFinding.relativePath,
        revision: context.revision
      });
      await assertByteWindowHashes(response);
      if (!this.isCurrent(token)) return;
      this.snapshot = {
        ...this.snapshot,
        byteWindow: {
          data: response,
          error: null,
          findingId: finding.findingId,
          status: 'ready'
        }
      };
      this.emit();
    } catch (error) {
      this.failByteWindow(token, finding.findingId, error);
    } finally {
      this.finish(token);
    }
  }

  public clearByteWindow() {
    this.invalidateChannel('byteWindow');
    this.snapshot = { ...this.snapshot, byteWindow: idleByteWindow() };
    this.emit();
  }

  public async loadAnnotations() {
    if (this.snapshot.annotations.status === 'loading') return;
    this.requireCapability('annotations');
    const token = this.begin('annotations');
    this.snapshot = {
      ...this.snapshot,
      annotations: { data: null, error: null, isSaving: false, status: 'loading' }
    };
    this.emit();
    try {
      const context = this.requireContext();
      const response = await this.bridge.readResearchAnnotations({
        expectedRevision: context.revision,
        scope: context.scope
      });
      if (!this.isCurrent(token)) return;
      assertAnnotationsResponse(response, context.revision);
      this.snapshot = {
        ...this.snapshot,
        annotations: { data: response, error: null, isSaving: false, status: 'ready' }
      };
      this.emit();
    } catch (error) {
      this.failAnnotations(token, null, false, error);
    } finally {
      this.finish(token);
    }
  }

  public async refreshAnnotations() {
    this.invalidateChannel('annotations');
    this.snapshot = { ...this.snapshot, annotations: idleAnnotations() };
    this.emit();
    await this.loadAnnotations();
  }

  public async upsertAnnotation(draft: ResearchAnnotationDraft) {
    try {
      const current = this.requireAnnotations();
      const context = this.requireContext();
      assertTargetRevision(draft.target, context.revision);
      if (draft.annotationId !== null) {
        const existing = current.document?.annotations.find((annotation) => (
          annotation.annotationId === draft.annotationId
        ));
        if (
          !existing ||
          researchAnnotationTargetIdentity(existing.target) !==
            researchAnnotationTargetIdentity(draft.target)
        ) {
          throw new Error('The annotation edit no longer matches its exact private target.');
        }
      }
    } catch (error) {
      this.rejectAnnotationPrecondition(error);
      return;
    }
    await this.mutateAnnotation({
      expectedAnnotationId: draft.annotationId,
      mutation: { annotationId: null, kind: 'upsert', upsert: draft }
    });
  }

  public async deleteAnnotation(annotationId: string) {
    try {
      const current = this.requireAnnotations();
      if (!current.document?.annotations.some((annotation) => (
        annotation.annotationId === annotationId
      ))) {
        throw new Error('The annotation no longer exists in the current private document.');
      }
    } catch (error) {
      this.rejectAnnotationPrecondition(error);
      return;
    }
    await this.mutateAnnotation({
      expectedAnnotationId: annotationId,
      mutation: { annotationId, kind: 'delete', upsert: null }
    });
  }

  private async mutateAnnotation(options: {
    expectedAnnotationId: string | null;
    mutation:
      | { annotationId: null; kind: 'upsert'; upsert: ResearchAnnotationDraft }
      | { annotationId: string; kind: 'delete'; upsert: null };
  }) {
    const retained = this.requireAnnotations();
    const token = this.begin('annotations');
    this.snapshot = {
      ...this.snapshot,
      annotations: {
        data: retained,
        error: null,
        isSaving: true,
        status: 'loading'
      }
    };
    this.emit();
    try {
      const context = this.requireContext();
      const response = await this.bridge.mutateResearchAnnotations({
        expectedETag: retained.etag,
        expectedRevision: context.revision,
        mutation: options.mutation,
        scope: context.scope
      });
      if (!this.isCurrent(token)) return;
      assertAnnotationMutationResponse({
        expectedAnnotationId: options.expectedAnnotationId,
        previous: retained,
        request: options.mutation,
        response,
        revision: context.revision
      });
      this.snapshot = {
        ...this.snapshot,
        annotations: {
          data: {
            document: response.document,
            etag: response.etag,
            exists: true,
            revision: response.revision
          },
          error: null,
          isSaving: false,
          status: 'ready'
        }
      };
      this.emit();
    } catch (error) {
      this.failAnnotations(token, retained, true, error);
    } finally {
      this.finish(token);
    }
  }

  private requireContext() {
    if (!this.scope || !this.revision) {
      throw new Error('Research Lab requires an exact semantic project revision.');
    }
    if (
      this.revision.projectId !== this.scope.projectId ||
      this.scope.paths.selectedGame === null ||
      this.revision.gameFamily !== semanticExploreProjectGameFamily(this.scope.paths.selectedGame)
    ) {
      throw new StaleProjectRevisionResponseError();
    }
    return { revision: this.revision, scope: this.scope };
  }

  private requireCapability(feature: ReadResearchLabCapabilitiesResponse['capabilities'][number]['feature']) {
    const capability = this.requireCapabilityDescriptor(feature);
    if (!capability?.canUse || capability.coverage === 'unavailable') {
      throw new Error('This research capability is unavailable for the current project revision.');
    }
    return capability;
  }

  private requireCapabilityDescriptor(
    feature: ReadResearchLabCapabilitiesResponse['capabilities'][number]['feature']
  ) {
    const capability = this.snapshot.capabilities.data?.capabilities.find(
      (candidate) => candidate.feature === feature
    );
    if (!capability) {
      throw new Error('The Research Lab capability catalog is not loaded.');
    }
    return capability;
  }

  private requireSourceIds(): [string, string] {
    const sourceA = this.snapshot.sources[0].data?.sourceId;
    const sourceB = this.snapshot.sources[1].data?.sourceId;
    if (!sourceA || !sourceB || sourceA === sourceB) {
      throw new Error('Research comparison requires two distinct registered sources.');
    }
    return [sourceA, sourceB];
  }

  private requireComparison() {
    const comparison = this.snapshot.comparison.data;
    if (!comparison) throw new Error('A current research comparison is required.');
    return comparison;
  }

  private requireAnnotations() {
    const annotations = this.snapshot.annotations.data;
    if (!annotations || this.snapshot.annotations.status !== 'ready') {
      throw new Error('The current private annotation document must be loaded first.');
    }
    return annotations;
  }

  private begin(channel: RequestChannel): RequestToken {
    this.advance(channel);
    const token = {
      channel,
      epoch: this.epoch,
      generation: this.generations.get(channel)!,
      id: this.nextRequestId++
    };
    this.activeRequests.add(token.id);
    this.updateBusy();
    return token;
  }

  private finish(token: RequestToken) {
    if (!this.activeRequests.delete(token.id)) return;
    this.updateBusy();
  }

  private isCurrent(token: RequestToken) {
    return token.epoch === this.epoch &&
      token.generation === this.generations.get(token.channel);
  }

  private advance(channel: RequestChannel) {
    this.generations.set(channel, (this.generations.get(channel) ?? 0) + 1);
  }

  private invalidateChannel(channel: RequestChannel) {
    this.advance(channel);
  }

  private invalidateResearchSources() {
    this.releaseRegisteredSources();
    this.invalidateChannel('source0');
    this.invalidateChannel('source1');
    this.invalidateComparison();
    this.snapshot = { ...this.snapshot, sources: [idleSource(), idleSource()] };
  }

  private invalidateComparison() {
    this.comparisonCursors.clear();
    this.invalidateChannel('comparison');
    this.invalidateChannel('byteWindow');
    this.snapshot = {
      ...this.snapshot,
      byteWindow: idleByteWindow(),
      comparison: idleComparison()
    };
  }

  private failCapabilities(token: RequestToken, error: unknown) {
    if (!this.isCurrent(token)) return;
    if (this.handleStale(error)) return;
    this.snapshot = {
      ...this.snapshot,
      capabilities: { data: null, error: classifyError(error), status: 'error' }
    };
    this.emit();
  }

  private failSource(
    token: RequestToken,
    slot: 0 | 1,
    retained: ResearchSourceState,
    error: unknown
  ) {
    if (!this.isCurrent(token)) return;
    if (this.handleStale(error)) return;
    const classified = classifyError(error);
    const canRetain = retained.data !== null &&
      !this.revokedSourceIds.has(retained.data.sourceId) &&
      canSafelyRetainSourceAfterFailure(classified);
    const sources = [...this.snapshot.sources] as [ResearchSourceState, ResearchSourceState];
    sources[slot] = canRetain
      ? { data: retained.data, error: classified, status: 'ready' }
      : { data: null, error: classified, status: 'error' };
    this.snapshot = { ...this.snapshot, sources };
    this.emit();
  }

  private clearRevokedSource(
    slot: 0 | 1,
    sourceId: string,
    error: ResearchLabError
  ) {
    if (this.snapshot.sources[slot].data?.sourceId !== sourceId) return;
    const sources = [...this.snapshot.sources] as [ResearchSourceState, ResearchSourceState];
    sources[slot] = { data: null, error, status: 'error' };
    this.snapshot = { ...this.snapshot, sources };
    this.emit();
  }

  private failComparison(
    token: RequestToken,
    retained: CompareResearchSourcesResponse | null,
    error: unknown
  ) {
    if (!this.isCurrent(token)) return;
    if (this.handleStale(error)) return;
    const classified = classifyError(error);
    if (classified === 'sourceExpired') {
      this.snapshot = {
        ...this.snapshot,
        byteWindow: idleByteWindow(),
        comparison: { ...idleComparison(), error: classified, status: 'error' },
        sources: [
          { data: null, error: classified, status: 'error' },
          { data: null, error: classified, status: 'error' }
        ]
      };
    } else if (classified === 'comparisonStale') {
      this.snapshot = {
        ...this.snapshot,
        byteWindow: idleByteWindow(),
        comparison: { ...idleComparison(), error: classified, status: 'error' }
      };
    } else {
      this.snapshot = {
        ...this.snapshot,
        comparison: {
          ...this.snapshot.comparison,
          data: retained,
          error: classified,
          isAppending: false,
          status: 'error'
        }
      };
    }
    this.emit();
  }

  private failByteWindow(token: RequestToken, findingId: string, error: unknown) {
    if (!this.isCurrent(token)) return;
    if (this.handleStale(error)) return;
    const classified = classifyError(error);
    this.snapshot = classified === 'sourceExpired'
      ? {
          ...this.snapshot,
          byteWindow: idleByteWindow(),
          comparison: {
            ...idleComparison(),
            error: classified,
            status: 'error'
          },
          sources: [
            { data: null, error: classified, status: 'error' },
            { data: null, error: classified, status: 'error' }
          ]
        }
      : classified === 'comparisonStale'
        ? {
            ...this.snapshot,
            byteWindow: idleByteWindow(),
            comparison: {
              ...idleComparison(),
              error: classified,
              status: 'error'
            }
          }
      : {
          ...this.snapshot,
          byteWindow: {
            data: null,
            error: classified,
            findingId,
            status: 'error'
          }
        };
    this.emit();
  }

  private failAnnotations(
    token: RequestToken,
    retained: ReadResearchAnnotationsResponse | null,
    isSaving: boolean,
    error: unknown
  ) {
    if (!this.isCurrent(token)) return;
    if (this.handleStale(error)) return;
    const classified = classifyError(error);
    if (classified === 'sourceExpired' || classified === 'comparisonStale') {
      this.invalidateComparison();
    }
    this.snapshot = {
      ...this.snapshot,
      ...(classified === 'sourceExpired'
        ? {
            comparison: { ...idleComparison(), error: classified, status: 'error' as const },
            sources: [
              { data: null, error: classified, status: 'error' as const },
              { data: null, error: classified, status: 'error' as const }
            ] as [ResearchSourceState, ResearchSourceState]
          }
        : classified === 'comparisonStale'
          ? { comparison: { ...idleComparison(), error: classified, status: 'error' as const } }
          : {}),
      annotations: {
        data: retained,
        error: classified,
        isSaving: false,
        status: 'error'
      }
    };
    if (isSaving && classified === 'conflict') {
      this.invalidateChannel('annotations');
    }
    this.emit();
  }

  private rejectAnnotationPrecondition(error: unknown) {
    if (this.handleStale(error)) return;
    this.snapshot = {
      ...this.snapshot,
      annotations: {
        data: this.snapshot.annotations.data,
        error: classifyError(error),
        isSaving: false,
        status: 'error'
      }
    };
    this.emit();
  }

  private handleStale(error: unknown) {
    if (!isStaleError(error)) return false;
    this.releaseRegisteredSources();
    this.onStaleRevision?.();
    this.reset();
    return true;
  }

  private updateBusy() {
    const isBusy = this.activeRequests.size > 0;
    if (isBusy === this.snapshot.isBusy) return;
    this.snapshot = { ...this.snapshot, isBusy };
    this.emit();
  }

  private reset() {
    this.epoch += 1;
    this.comparisonCursors.clear();
    this.revokedSourceIds.clear();
    for (const channel of requestChannels) this.advance(channel);
    this.activeRequests.clear();
    this.snapshot = {
      annotations: idleAnnotations(),
      byteWindow: idleByteWindow(),
      capabilities: idleCapabilities(),
      comparison: idleComparison(),
      isBusy: false,
      sources: [idleSource(), idleSource()]
    };
    this.emit();
  }

  private releaseRegisteredSources() {
    if (!this.scope || !this.revision) return;
    const sourceIds = this.snapshot.sources.flatMap((source) => (
      source.data ? [source.data.sourceId] : []
    ));
    for (const sourceId of new Set(sourceIds)) {
      this.releaseSource(sourceId, this.scope, this.revision);
    }
  }

  private releaseSource(
    sourceId: string,
    scope: SemanticExploreScope,
    revision: SemanticExploreRevision
  ) {
    void this.bridge.closeResearchSource({
      expectedRevision: revision,
      scope,
      sourceId
    }).catch(() => undefined);
  }

  private emit() {
    for (const listener of this.listeners) listener();
  }
}

function assertCapabilitiesResponse(
  response: ReadResearchLabCapabilitiesResponse,
  revision: SemanticExploreRevision
) {
  assertRevision(response.revision, revision);
  if (response.snapshots.some((snapshot) => (
    researchRevisionIdentity(snapshot.revision) !== researchRevisionIdentity(revision)
  ))) {
    throw new StaleProjectRevisionResponseError();
  }
}

function assertComparisonResponse(options: {
  cursor: string | null;
  expectedRevision: SemanticExploreRevision;
  limit: number;
  response: CompareResearchSourcesResponse;
  retained: CompareResearchSourcesResponse | null;
  seenCursors: ReadonlySet<string>;
  expectedSemanticProjection: ResearchCapability;
  selectedRelativePaths: readonly string[];
  sourceIds: readonly [string, string];
}) {
  const {
    cursor,
    expectedRevision,
    limit,
    response,
    retained,
    seenCursors,
    expectedSemanticProjection,
    selectedRelativePaths,
    sourceIds
  } = options;
  assertRevision(response.revision, expectedRevision);
  if (
    response.items.length > limit ||
    response.items.reduce((count, item) => count + item.ranges.length, 0) >
      researchLabMaximumAggregateRanges ||
    JSON.stringify(response.semanticProjection) !== JSON.stringify(expectedSemanticProjection) ||
    response.sources.length !== sourceIds.length ||
    response.sources.some((source, index) => source.sourceId !== sourceIds[index]) ||
    cursor !== null && response.items.length === 0 ||
    response.nextCursor !== null && response.items.length !== limit ||
    response.nextCursor !== null && response.nextCursor === cursor ||
    response.nextCursor !== null && seenCursors.has(response.nextCursor) ||
    response.items.some((item) => {
      const selected = selectedRelativePaths.some((path) => (
        sameRelativePath(path, item.relativePath)
      ));
      return selected
        ? item.rangeCoverage === 'notRequested'
        : item.rangeCoverage !== 'notRequested';
    })
  ) {
    throw new Error('The research comparison page did not match its exact request.');
  }
  if (retained && (
    retained.queryFingerprint !== response.queryFingerprint ||
    retained.comparisonId !== response.comparisonId ||
    retained.comparisonFingerprint !== response.comparisonFingerprint ||
    JSON.stringify(retained.sources) !== JSON.stringify(response.sources) ||
    JSON.stringify(retained.semanticProjection) !== JSON.stringify(response.semanticProjection)
  )) {
    throw new StaleComparisonResponseError();
  }
}

function mergeComparisonPages(
  previous: CompareResearchSourcesResponse,
  next: CompareResearchSourcesResponse
): CompareResearchSourcesResponse {
  const findingIds = new Set(previous.items.map((item) => item.findingId));
  const relativePaths = new Set(previous.items.map((item) => relativePathIdentity(item.relativePath)));
  for (const item of next.items) {
    if (
      !findingIds.add(item.findingId) ||
      !relativePaths.add(relativePathIdentity(item.relativePath))
    ) {
      throw new Error('The research comparison continuation repeated a finding.');
    }
  }
  const items = [...previous.items, ...next.items];
  if (
    items.length > researchLabMaximumAccumulatedFindings ||
    items.reduce((count, item) => count + item.ranges.length, 0) >
      researchLabMaximumAggregateRanges
  ) {
    throw new Error('The research comparison exceeds the bounded frontend window.');
  }
  return { ...next, items };
}

function assertByteWindowResponse(
  response: ReadResearchByteWindowResponse,
  expected: {
    comparisonFingerprint: string;
    finding: ResearchFileFinding;
    length: number;
    offset: number;
    relativePath: string;
    revision: SemanticExploreRevision;
  }
) {
  assertRevision(response.revision, expected.revision);
  if (
    response.comparisonFingerprint !== expected.comparisonFingerprint ||
    response.relativePath !== expected.relativePath ||
    response.offset !== expected.offset ||
    response.requestedLength !== expected.length ||
    response.sourceA.exists !== expected.finding.sourceA.exists ||
    response.sourceA.fileLength !== expected.finding.sourceA.length ||
    response.sourceB.exists !== expected.finding.sourceB.exists ||
    response.sourceB.fileLength !== expected.finding.sourceB.length
  ) {
    throw new StaleComparisonResponseError();
  }
  for (const side of [response.sourceA, response.sourceB]) {
    if (!side.exists) continue;
    const expectedByteLength = Math.min(
      expected.length,
      Math.max(side.fileLength! - expected.offset, 0)
    );
    if (researchBase64ByteLength(side.bytesBase64!) !== expectedByteLength) {
      throw new Error('The research byte window is incomplete for its reviewed source range.');
    }
  }
}

async function assertByteWindowHashes(response: ReadResearchByteWindowResponse) {
  for (const side of [response.sourceA, response.sourceB]) {
    if (!side.exists) continue;
    const binary = atob(side.bytesBase64!);
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    const actual = Array.from(new Uint8Array(digest), (value) => (
      value.toString(16).padStart(2, '0')
    )).join('');
    if (actual !== side.windowSha256) {
      throw new Error('The research byte window hash does not match its bounded payload.');
    }
  }
}

function assertAnnotationsResponse(
  response: ReadResearchAnnotationsResponse,
  revision: SemanticExploreRevision
) {
  assertRevision(response.revision, revision);
}

function assertAnnotationMutationResponse(options: {
  expectedAnnotationId: string | null;
  previous: ReadResearchAnnotationsResponse;
  request:
    | { annotationId: null; kind: 'upsert'; upsert: ResearchAnnotationDraft }
    | { annotationId: string; kind: 'delete'; upsert: null };
  response: MutateResearchAnnotationsResponse;
  revision: SemanticExploreRevision;
}) {
  const { expectedAnnotationId, previous, request, response, revision } = options;
  assertRevision(response.revision, revision);
  if (
    response.etag === previous.etag ||
    Date.parse(response.document.updatedAtUtc) > Date.parse(response.writtenAtUtc)
  ) {
    throw new Error('The private annotation receipt is inconsistent.');
  }
  const before = previous.document?.annotations ?? [];
  const after = response.document.annotations;
  if (request.kind === 'delete') {
    if (
      after.length !== before.length - 1 ||
      after.some((annotation) => annotation.annotationId === request.annotationId) ||
      !unchangedAnnotationsMatch(before, after, request.annotationId)
    ) {
      throw new Error('The private annotation delete receipt changed another entry.');
    }
    return;
  }
  const expectedCount = before.length + (expectedAnnotationId === null ? 1 : 0);
  const candidates = after.filter((annotation) => (
    expectedAnnotationId === null
      ? !before.some((old) => old.annotationId === annotation.annotationId)
      : annotation.annotationId === expectedAnnotationId
  ));
  const candidate = candidates[0];
  const previousCandidate = expectedAnnotationId === null
    ? null
    : before.find((annotation) => annotation.annotationId === expectedAnnotationId) ?? null;
  if (
    after.length !== expectedCount ||
    candidates.length !== 1 ||
    !annotationMatchesDraft(candidate!, request.upsert) ||
    !hasExpectedMutationTimestamps(
      candidate!,
      previousCandidate,
      response.document.updatedAtUtc
    ) ||
    !unchangedAnnotationsMatch(before, after, expectedAnnotationId)
  ) {
    throw new Error('The private annotation upsert receipt changed an unexpected entry.');
  }
}

function hasExpectedMutationTimestamps(
  annotation: ResearchAnnotation,
  previous: ResearchAnnotation | null,
  documentUpdatedAtUtc: string
) {
  return annotation.updatedAtUtc === documentUpdatedAtUtc &&
    annotation.createdAtUtc === (previous?.createdAtUtc ?? documentUpdatedAtUtc);
}

function annotationMatchesDraft(annotation: ResearchAnnotation, draft: ResearchAnnotationDraft) {
  return researchAnnotationTargetIdentity(annotation.target) ===
      researchAnnotationTargetIdentity(draft.target) &&
    annotation.text === draft.text &&
    JSON.stringify(annotation.tags) === JSON.stringify(draft.tags);
}

function unchangedAnnotationsMatch(
  before: readonly ResearchAnnotation[],
  after: readonly ResearchAnnotation[],
  excludedId: string | null
) {
  const remainingBefore = before.filter((annotation) => annotation.annotationId !== excludedId);
  return remainingBefore.every((annotation) => {
    const current = after.find((candidate) => candidate.annotationId === annotation.annotationId);
    return current !== undefined && JSON.stringify(current) === JSON.stringify(annotation);
  });
}

function assertTargetRevision(
  target: ResearchAnnotationDraft['target'],
  revision: SemanticExploreRevision
) {
  if (researchRevisionIdentity(target.revision) !== researchRevisionIdentity(revision)) {
    throw new StaleProjectRevisionResponseError();
  }
}

function assertRevision(actual: SemanticExploreRevision, expected: SemanticExploreRevision) {
  if (researchRevisionIdentity(actual) !== researchRevisionIdentity(expected)) {
    throw new StaleProjectRevisionResponseError();
  }
}

function sameRelativePath(left: string, right: string) {
  return relativePathIdentity(left) === relativePathIdentity(right);
}

function relativePathIdentity(value: string) {
  return researchPortableCaseFold(value.normalize('NFC'));
}

function assertSourceExpiration(
  expiresAtUtc: string,
  requestStartedAt: number,
  responseReceivedAt: number
) {
  const expiration = Date.parse(expiresAtUtc);
  const lifetime = researchLabRegistrationLifetimeMinutes * 60_000;
  const serializationTolerance = 5_000;
  if (
    expiration <= responseReceivedAt ||
    expiration < requestStartedAt + lifetime - serializationTolerance ||
    expiration > responseReceivedAt + lifetime + serializationTolerance
  ) {
    throw new Error('The research source registration lifetime is inconsistent.');
  }
}

class StaleProjectRevisionResponseError extends Error {}
class StaleComparisonResponseError extends Error {}

function semanticErrorCode(error: unknown) {
  return error instanceof ProjectBridgeError && error.semanticCode
    ? String(error.semanticCode)
    : null;
}

function isStaleError(error: unknown) {
  const code = semanticErrorCode(error);
  return error instanceof StaleProjectRevisionResponseError ||
    code === semanticExploreErrorCodes.staleRevision;
}

function classifyError(error: unknown): ResearchLabError {
  if (error instanceof StaleComparisonResponseError) return 'comparisonStale';
  switch (semanticErrorCode(error)) {
    case projectBridgeErrorCodes.workspaceConcurrentModification:
      return 'conflict';
    case semanticExploreErrorCodes.invalidCursor:
      return 'cursor';
    case semanticExploreErrorCodes.invalidQuery:
      return 'invalidQuery';
    case semanticExploreErrorCodes.limitExceeded:
      return 'limit';
    case semanticExploreErrorCodes.unsupported:
      return 'unsupported';
    case researchLabErrorCodes.sourceExpired:
    case semanticExploreErrorCodes.externalSnapshotUnavailable:
      return 'sourceExpired';
    case researchLabErrorCodes.comparisonStale:
      return 'comparisonStale';
    case researchLabErrorCodes.sourceRejected:
    case semanticExploreErrorCodes.externalOverlayRejected:
      return 'sourceRejected';
    default:
      return 'generic';
  }
}

function canSafelyRetainSourceAfterFailure(error: ResearchLabError) {
  return error === 'invalidQuery' || error === 'limit' || error === 'sourceRejected';
}

function retainCapabilitiesAfterCancel(state: ResearchCapabilitiesState): ResearchCapabilitiesState {
  return state.data
    ? { data: state.data, error: null, status: 'ready' }
    : idleCapabilities();
}

function retainSourceAfterCancel(state: ResearchSourceState): ResearchSourceState {
  return state.data ? { data: state.data, error: null, status: 'ready' } : idleSource();
}

function retainComparisonAfterCancel(state: ResearchComparisonState): ResearchComparisonState {
  return state.data
    ? { ...state, error: null, isAppending: false, status: 'ready' }
    : idleComparison();
}

function retainAnnotationsAfterCancel(state: ResearchAnnotationsState): ResearchAnnotationsState {
  return state.data
    ? { data: state.data, error: null, isSaving: false, status: 'ready' }
    : idleAnnotations();
}

export function useResearchLabController(options: {
  bridge: ResearchLabProjectBridgeApi;
  onStaleRevision?: () => void;
  revision: SemanticExploreRevision | null;
  scope: SemanticExploreScope | null;
}): ResearchLabController {
  const storeRef = useRef<ResearchLabControllerStore | null>(null);
  if (storeRef.current === null) {
    storeRef.current = new ResearchLabControllerStore(options.bridge);
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
  useEffect(() => {
    store.cancelScheduledDispose();
    return () => store.scheduleDispose();
  }, [store]);
  return useMemo(() => ({
    ...snapshot,
    cancel: () => store.cancel(),
    clearByteWindow: () => store.clearByteWindow(),
    clearSource: (slot: 0 | 1) => store.clearSource(slot),
    compare: (paths?: readonly string[]) => store.compare(paths),
    deleteAnnotation: (annotationId: string) => store.deleteAnnotation(annotationId),
    expireSources: () => store.expireSources(),
    loadAnnotations: () => store.loadAnnotations(),
    loadByteWindow: (finding: ResearchFileFinding, offset: number, length: number) => (
      store.loadByteWindow(finding, offset, length)
    ),
    loadCapabilities: () => store.loadCapabilities(),
    loadMore: () => store.loadMore(),
    openSource: (slot: 0 | 1, rootPath: string) => store.openSource(slot, rootPath),
    refreshAnnotations: () => store.refreshAnnotations(),
    refreshCapabilities: () => store.refreshCapabilities(),
    upsertAnnotation: (draft: ResearchAnnotationDraft) => store.upsertAnnotation(draft)
  }), [snapshot, store]);
}
