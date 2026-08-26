/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectDraftAdapter } from './draftRegistry';
import {
  OrdinaryEditorDraftStore,
  type OrdinaryEditorDraftErrorCode,
  type OrdinaryEditorDraftInspection,
  type OrdinaryEditorDraftLoadResult,
  type OrdinaryEditorDraftReconciliation,
  type OrdinaryEditorDraftRevision,
  type OrdinaryEditorDraftScope
} from './ordinaryEditorDraftStore';

export type OrdinaryEditorDraftStatus =
  | 'idle'
  | 'loading'
  | 'saving'
  | 'saved'
  | 'stale'
  | 'error';

export type OrdinaryEditorDraftControllerSnapshot = {
  errorCode: OrdinaryEditorDraftErrorCode | null;
  hasDurableDraft: boolean;
  hasPendingChanges: boolean;
  staleInspection: OrdinaryEditorDraftInspection | null;
  status: OrdinaryEditorDraftStatus;
  updatedAtUtc: string | null;
};

export type OrdinaryEditorDraftControllerOptions<TDraft> = {
  adapter: ProjectDraftAdapter<TDraft>;
  debounceMilliseconds?: number;
  isClean: (payload: TDraft) => boolean;
  onHydrate?: (payload: TDraft | null) => void;
  scope: OrdinaryEditorDraftScope;
  store: OrdinaryEditorDraftStore;
};

type PendingMutation<TDraft> = {
  payload: TDraft;
};

const defaultDraftDebounceMilliseconds = 500;
const maximumDraftDebounceMilliseconds = 60_000;

export class OrdinaryEditorDraftController<TDraft> {
  private activeDrain: Promise<boolean> | null = null;
  private closing = false;
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;
  private disposePromise: Promise<boolean> | null = null;
  private disposed = false;
  private expectedRevision: OrdinaryEditorDraftRevision | null = null;
  private hasHydrated = false;
  private hydrationPromise: Promise<void> | null = null;
  private listeners = new Set<() => void>();
  private loadAbortController: AbortController | null = null;
  private loadGeneration = 0;
  private pendingMutation: PendingMutation<TDraft> | null = null;
  private snapshot: OrdinaryEditorDraftControllerSnapshot = {
    errorCode: null,
    hasDurableDraft: false,
    hasPendingChanges: false,
    staleInspection: null,
    status: 'idle',
    updatedAtUtc: null
  };

  private readonly adapter: ProjectDraftAdapter<TDraft>;
  private readonly debounceMilliseconds: number;
  private readonly isClean: (payload: TDraft) => boolean;
  private readonly onHydrate: ((payload: TDraft | null) => void) | undefined;
  private readonly scope: OrdinaryEditorDraftScope;
  private readonly store: OrdinaryEditorDraftStore;

  public constructor(options: OrdinaryEditorDraftControllerOptions<TDraft>) {
    this.adapter = options.adapter;
    this.debounceMilliseconds =
      options.debounceMilliseconds ?? defaultDraftDebounceMilliseconds;
    if (
      !Number.isSafeInteger(this.debounceMilliseconds) ||
      this.debounceMilliseconds < 0 ||
      this.debounceMilliseconds > maximumDraftDebounceMilliseconds
    ) {
      throw new RangeError(
        `debounceMilliseconds must be between 0 and ${maximumDraftDebounceMilliseconds}.`
      );
    }
    if (typeof options.isClean !== 'function') {
      throw new TypeError('isClean must be a function.');
    }
    this.isClean = options.isClean;
    this.onHydrate = options.onHydrate;
    this.scope = options.scope;
    this.store = options.store;
  }

  public readonly getSnapshot = () => this.snapshot;

  public readonly getAdapterId = () => this.adapter.adapterId;

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  public hydrate(): Promise<void> {
    return this.startHydration(false);
  }

  public update(payload: TDraft) {
    if (this.closing || this.disposed) {
      return false;
    }
    this.pendingMutation = { payload };
    this.setSnapshot({
      ...this.snapshot,
      hasPendingChanges: true
    });
    if (!this.hasHydrated || this.snapshot.status === 'stale') {
      return true;
    }
    this.setSnapshot({
      ...this.snapshot,
      errorCode: null,
      status: 'saving'
    });
    this.scheduleFlush();
    return true;
  }

  public async flush() {
    return this.flushCore(false);
  }

  public async discard() {
    return this.discardCore(true);
  }

  public async clearDurable() {
    return this.discardCore(false);
  }

  private async discardCore(applyHydratedPayload: boolean) {
    this.clearDebounceTimer();
    this.pendingMutation = null;
    if (this.activeDrain) {
      await this.activeDrain;
    }
    this.pendingMutation = null;
    let result;
    try {
      result = await this.store.discard(this.scope);
    } catch {
      if (!this.disposed) {
        this.setError('storage-unavailable');
      }
      return false;
    }
    if (result.kind === 'error') {
      if (!this.disposed) {
        this.setError(result.errorCode);
      }
      return false;
    }
    this.expectedRevision = null;
    if (!this.disposed) {
      if (applyHydratedPayload && !this.applyHydratedPayload(null)) {
        return false;
      }
      this.setSnapshot({
        errorCode: null,
        hasDurableDraft: false,
        hasPendingChanges: false,
        staleInspection: null,
        status: 'saved',
        updatedAtUtc: null
      });
    }
    return true;
  }

  public async reconcile(resolution: OrdinaryEditorDraftReconciliation<TDraft>) {
    if (this.closing || this.disposed) {
      return false;
    }
    this.clearDebounceTimer();
    if (this.activeDrain) {
      await this.activeDrain;
    }
    let result;
    try {
      result = await this.store.reconcile(this.scope, this.adapter, resolution);
    } catch {
      this.setError('storage-unavailable');
      return false;
    }
    if (this.closing || this.disposed) {
      return false;
    }
    if (result.kind === 'ready') {
      this.pendingMutation = null;
      this.expectedRevision = result.revision;
      this.hasHydrated = true;
      if (!this.applyHydratedPayload(result.payload)) {
        return false;
      }
      this.setSnapshot({
        errorCode: null,
        hasDurableDraft: true,
        hasPendingChanges: this.pendingMutation !== null,
        staleInspection: null,
        status: this.pendingMutation ? 'saving' : 'saved',
        updatedAtUtc: result.updatedAtUtc
      });
      if (this.pendingMutation) {
        this.scheduleFlush(0);
      }
      return true;
    }
    if (result.kind === 'missing') {
      this.pendingMutation = null;
      this.expectedRevision = null;
      this.hasHydrated = true;
      if (!this.applyHydratedPayload(null)) {
        return false;
      }
      this.setSnapshot({
        errorCode: null,
        hasDurableDraft: false,
        hasPendingChanges: this.pendingMutation !== null,
        staleInspection: null,
        status: this.pendingMutation ? 'saving' : 'saved',
        updatedAtUtc: null
      });
      if (this.pendingMutation) {
        this.scheduleFlush(0);
      }
      return true;
    }
    if (result.kind === 'stale') {
      this.setStale(result.inspection);
      return false;
    }
    this.setError(result.errorCode);
    return false;
  }

  public dispose() {
    if (!this.disposePromise) {
      this.disposePromise = this.disposeCore().then((didDispose) => {
        if (!didDispose) {
          this.disposePromise = null;
        }
        return didDispose;
      });
    }
    return this.disposePromise;
  }

  private async disposeCore() {
    if (this.disposed) {
      return true;
    }
    this.closing = true;
    this.clearDebounceTimer();

    if (this.pendingMutation) {
      if (!this.hasHydrated) {
        await this.startHydration(true);
      } else if (this.hydrationPromise) {
        await this.hydrationPromise;
      }
      const didFlush = await this.flushCore(true);
      if (!didFlush) {
        this.closing = false;
        return false;
      }
    } else {
      this.cancelHydration();
      if (this.activeDrain) {
        const didDrain = await this.activeDrain;
        if (!didDrain) {
          this.closing = false;
          return false;
        }
      }
    }

    this.cancelHydration();
    this.disposed = true;
    this.listeners.clear();
    return true;
  }

  private startHydration(allowClosing: boolean): Promise<void> {
    if (this.disposed || (this.closing && !allowClosing)) {
      return Promise.resolve();
    }
    this.cancelHydration();
    const generation = ++this.loadGeneration;
    const abortController = new AbortController();
    this.loadAbortController = abortController;
    this.setSnapshot({
      ...this.snapshot,
      errorCode: null,
      staleInspection: null,
      status: 'loading'
    });

    const hydrationPromise = this.store
      .load(this.scope, this.adapter, abortController.signal)
      .then((result) => this.handleHydrationResult(generation, result))
      .catch(() => {
        if (generation === this.loadGeneration && !abortController.signal.aborted) {
          this.setError('storage-unavailable');
        }
      })
      .finally(() => {
        if (this.loadAbortController === abortController) {
          this.loadAbortController = null;
        }
        if (this.hydrationPromise === hydrationPromise) {
          this.hydrationPromise = null;
        }
      });
    this.hydrationPromise = hydrationPromise;
    return hydrationPromise;
  }

  private handleHydrationResult(
    generation: number,
    result: OrdinaryEditorDraftLoadResult<TDraft>
  ) {
    if (generation !== this.loadGeneration || this.disposed || result.kind === 'cancelled') {
      return;
    }
    this.hasHydrated = true;
    if (result.kind === 'ready') {
      this.expectedRevision = result.revision;
      if (!this.pendingMutation && !this.applyHydratedPayload(result.payload)) {
        return;
      }
      this.setSnapshot({
        errorCode: null,
        hasDurableDraft: true,
        hasPendingChanges: this.pendingMutation !== null,
        staleInspection: null,
        status: this.pendingMutation ? 'saving' : 'saved',
        updatedAtUtc: result.updatedAtUtc
      });
      if (this.pendingMutation) {
        this.scheduleFlush(0);
      }
      return;
    }
    if (result.kind === 'missing') {
      this.expectedRevision = null;
      if (!this.pendingMutation && !this.applyHydratedPayload(null)) {
        return;
      }
      this.setSnapshot({
        errorCode: null,
        hasDurableDraft: false,
        hasPendingChanges: this.pendingMutation !== null,
        staleInspection: null,
        status: this.pendingMutation ? 'saving' : 'saved',
        updatedAtUtc: null
      });
      if (this.pendingMutation) {
        this.scheduleFlush(0);
      }
      return;
    }
    if (result.kind === 'stale') {
      this.setStale(result.inspection);
      return;
    }
    this.setError(result.errorCode);
  }

  private async flushCore(allowClosing: boolean): Promise<boolean> {
    if (this.disposed || (this.closing && !allowClosing)) {
      return false;
    }
    this.clearDebounceTimer();
    if (!this.hasHydrated) {
      if (!this.hydrationPromise) {
        await this.startHydration(allowClosing);
      } else {
        await this.hydrationPromise;
      }
    }
    if (
      this.disposed ||
      (this.closing && !allowClosing) ||
      this.snapshot.status === 'stale'
    ) {
      return false;
    }
    if (this.snapshot.status === 'error') {
      if (
        this.snapshot.errorCode !== 'storage-unavailable' ||
        !this.pendingMutation
      ) {
        return false;
      }
      this.setSnapshot({
        ...this.snapshot,
        errorCode: null,
        status: 'saving'
      });
    }
    if (this.activeDrain) {
      return this.activeDrain;
    }
    if (!this.pendingMutation) {
      return true;
    }

    const activeDrain = this.drainPendingMutations();
    this.activeDrain = activeDrain;
    try {
      return await activeDrain;
    } finally {
      if (this.activeDrain === activeDrain) {
        this.activeDrain = null;
      }
    }
  }

  private async drainPendingMutations(): Promise<boolean> {
    while (this.pendingMutation) {
      const mutation = this.pendingMutation;
      this.pendingMutation = null;
      this.setSnapshot({
        ...this.snapshot,
        errorCode: null,
        hasPendingChanges: true,
        status: 'saving'
      });

      let clean: boolean;
      try {
        clean = this.isClean(mutation.payload);
      } catch {
        if (!this.pendingMutation) {
          this.pendingMutation = mutation;
        }
        this.setError('payload-invalid');
        return false;
      }

      let result;
      try {
        result = clean
          ? await this.store.delete(
              this.scope,
              this.adapter,
              this.expectedRevision
            )
          : await this.store.save(
              this.scope,
              this.adapter,
              mutation.payload,
              this.expectedRevision
            );
      } catch {
        if (!this.pendingMutation) {
          this.pendingMutation = mutation;
        }
        this.setError('storage-unavailable');
        return false;
      }
      if (result.kind === 'ready') {
        this.expectedRevision = result.revision;
        this.setSnapshot({
          errorCode: null,
          hasDurableDraft: true,
          hasPendingChanges: this.pendingMutation !== null,
          staleInspection: null,
          status: this.pendingMutation ? 'saving' : 'saved',
          updatedAtUtc: result.updatedAtUtc
        });
        continue;
      }
      if (result.kind === 'missing') {
        this.expectedRevision = null;
        this.setSnapshot({
          errorCode: null,
          hasDurableDraft: false,
          hasPendingChanges: this.pendingMutation !== null,
          staleInspection: null,
          status: this.pendingMutation ? 'saving' : 'saved',
          updatedAtUtc: null
        });
        continue;
      }
      if (!this.pendingMutation) {
        this.pendingMutation = mutation;
      }
      if (result.kind === 'stale') {
        this.setStale(result.inspection);
      } else {
        this.setError(result.errorCode);
      }
      return false;
    }
    return true;
  }

  private applyHydratedPayload(payload: TDraft | null) {
    try {
      this.onHydrate?.(payload);
      return true;
    } catch {
      this.setError('hydration-rejected');
      return false;
    }
  }

  private scheduleFlush(delay = this.debounceMilliseconds) {
    this.clearDebounceTimer();
    this.debounceTimer = setTimeout(() => {
      this.debounceTimer = null;
      void this.flushCore(false);
    }, delay);
  }

  private clearDebounceTimer() {
    if (this.debounceTimer !== null) {
      clearTimeout(this.debounceTimer);
      this.debounceTimer = null;
    }
  }

  private cancelHydration() {
    this.loadGeneration += 1;
    this.loadAbortController?.abort();
    this.loadAbortController = null;
  }

  private setStale(inspection: OrdinaryEditorDraftInspection) {
    this.setSnapshot({
      errorCode: null,
      hasDurableDraft: true,
      hasPendingChanges: this.pendingMutation !== null,
      staleInspection: inspection,
      status: 'stale',
      updatedAtUtc: inspection.updatedAtUtc
    });
  }

  private setError(errorCode: OrdinaryEditorDraftErrorCode) {
    this.setSnapshot({
      ...this.snapshot,
      errorCode,
      status: 'error'
    });
  }

  private setSnapshot(snapshot: OrdinaryEditorDraftControllerSnapshot) {
    if (
      snapshot.errorCode === this.snapshot.errorCode &&
      snapshot.hasDurableDraft === this.snapshot.hasDurableDraft &&
      snapshot.hasPendingChanges === this.snapshot.hasPendingChanges &&
      snapshot.staleInspection === this.snapshot.staleInspection &&
      snapshot.status === this.snapshot.status &&
      snapshot.updatedAtUtc === this.snapshot.updatedAtUtc
    ) {
      return;
    }
    this.snapshot = snapshot;
    for (const listener of this.listeners) {
      listener();
    }
  }
}
