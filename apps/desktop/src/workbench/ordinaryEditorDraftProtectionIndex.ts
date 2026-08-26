/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import type { WorkbenchSection } from '../workbenchStore';
import type {
  OrdinaryEditorDraftController,
  OrdinaryEditorDraftControllerSnapshot
} from './ordinaryEditorDraftController';
import {
  OrdinaryEditorDraftStore,
  type OrdinaryEditorDraftDiscardQuery,
  type OrdinaryEditorDraftErrorCode,
  type OrdinaryEditorDraftScope
} from './ordinaryEditorDraftStore';
import { semanticRecordRefKey, type SemanticRecordRef } from './semanticContracts';
import type { WorkspaceShellTab } from './workspaceShellController';

export type OrdinaryEditorDraftProtectionIndexStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'error';

export type OrdinaryEditorDraftProtectionIndexSnapshot = {
  dirtyEntityKeys: ReadonlySet<string>;
  durableEntityKeys: ReadonlySet<string>;
  errorCode: OrdinaryEditorDraftErrorCode | null;
  protectedEntityKeys: ReadonlySet<string>;
  revision: number;
  status: OrdinaryEditorDraftProtectionIndexStatus;
};

export type OrdinaryEditorDraftProtectionRefreshScope = {
  game: ProjectGame;
  projectId: string;
  sourceRevisionFingerprint: string;
};

export type WorkspaceTabProtectionFallback = {
  entityScopedSections: ReadonlySet<WorkbenchSection>;
  fallbackProtectedSections: ReadonlySet<WorkbenchSection>;
};

type TrackedController = {
  controller: OrdinaryEditorDraftController<unknown>;
  key: string;
  releaseRequested: boolean;
  scope: OrdinaryEditorDraftScope;
  unsubscribe: () => void;
};

const maximumTrackedDraftControllers = 256;

export class OrdinaryEditorDraftProtectionIndex {
  private durableKeys = new Set<string>();
  private listeners = new Set<() => void>();
  private loadGeneration = 0;
  private nextControllerToken = 0;
  private snapshot: OrdinaryEditorDraftProtectionIndexSnapshot = {
    dirtyEntityKeys: new Set<string>(),
    durableEntityKeys: new Set<string>(),
    errorCode: null,
    protectedEntityKeys: new Set<string>(),
    revision: 0,
    status: 'idle'
  };
  private trackedControllers = new Map<number, TrackedController>();

  public constructor(private readonly store: OrdinaryEditorDraftStore) {}

  public readonly getSnapshot = () => this.snapshot;

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  public async refresh(scope: OrdinaryEditorDraftProtectionRefreshScope) {
    const generation = ++this.loadGeneration;
    this.publish('loading', null);
    let result;
    try {
      result = await this.store.list({
        game: scope.game,
        limit: 256,
        projectId: scope.projectId,
        sourceRevisionFingerprint: scope.sourceRevisionFingerprint
      });
    } catch {
      if (generation === this.loadGeneration) {
        this.publish('error', 'storage-unavailable');
      }
      return false;
    }
    if (generation !== this.loadGeneration) {
      return false;
    }
    if (result.kind === 'error') {
      this.publish('error', result.errorCode);
      return false;
    }

    this.durableKeys = new Set(
      result.entries.map((entry) =>
        ordinaryEditorDraftProtectionKey(entry.section, entry.stableEntityKey)
      )
    );
    this.publish('ready', null);
    return true;
  }

  public clear() {
    this.loadGeneration += 1;
    this.durableKeys.clear();
    this.publish('idle', null);
  }

  public track<TDraft>(
    scope: OrdinaryEditorDraftScope,
    controller: OrdinaryEditorDraftController<TDraft>
  ) {
    if (this.trackedControllers.size >= maximumTrackedDraftControllers) {
      this.publish('error', 'storage-limit');
      return () => undefined;
    }
    const token = this.nextControllerToken;
    this.nextControllerToken =
      this.nextControllerToken === Number.MAX_SAFE_INTEGER
        ? 0
        : this.nextControllerToken + 1;
    let key: string;
    try {
      key = ordinaryEditorDraftProtectionKey(
        scope.section,
        semanticRecordRefKey(scope.entity)
      );
    } catch {
      this.publish('error', 'invalid-scope');
      return () => undefined;
    }
    const update = () => {
      this.applyControllerKnowledge(key, controller.getSnapshot());
      this.publish(this.snapshot.status, this.snapshot.errorCode);
    };
    const unsubscribe = controller.subscribe(update);
    this.trackedControllers.set(token, {
      controller: controller as OrdinaryEditorDraftController<unknown>,
      key,
      releaseRequested: false,
      scope,
      unsubscribe
    });
    update();

    return () => {
      const tracked = this.trackedControllers.get(token);
      if (!tracked) {
        return;
      }
      tracked.releaseRequested = true;
      void this.releaseTrackedController(token, tracked);
    };
  }

  public isEntityProtected(section: WorkbenchSection, entity: SemanticRecordRef) {
    return this.snapshot.protectedEntityKeys.has(
      ordinaryEditorDraftProtectionKey(section, semanticRecordRefKey(entity))
    );
  }

  public getProtectedTabKeys(
    tabs: readonly WorkspaceShellTab[],
    fallback: WorkspaceTabProtectionFallback
  ) {
    return new Set(
      tabs
        .filter((tab) => {
          if (this.snapshot.protectedEntityKeys.has(tab.key)) {
            return true;
          }
          return (
            !fallback.entityScopedSections.has(tab.location.section) &&
            fallback.fallbackProtectedSections.has(tab.location.section)
          );
        })
        .map((tab) => tab.key)
    );
  }

  public async flushTracked() {
    const entries = [...this.trackedControllers.entries()];
    const results = await Promise.allSettled(
      entries.map(async ([token, tracked]) =>
        tracked.releaseRequested
          ? this.releaseTrackedController(token, tracked)
          : tracked.controller.flush()
      )
    );
    const didFlush = results.every(
      (result) => result.status === 'fulfilled' && result.value
    );
    if (!didFlush) {
      this.publish('error', 'storage-unavailable');
    }
    return didFlush;
  }

  public async discardMatching(query: OrdinaryEditorDraftDiscardQuery) {
    const tracked = [...this.trackedControllers.values()].filter(
      (entry) =>
        entry.scope.projectId === query.projectId &&
        entry.scope.game === query.game &&
        (query.section === undefined || entry.scope.section === query.section) &&
        (!query.adapterIds || query.adapterIds.has(entry.controller.getAdapterId()))
    );
    const controllerResults = await Promise.allSettled(
      tracked.map((entry) => entry.controller.discard())
    );
    if (
      controllerResults.some(
        (result) => result.status !== 'fulfilled' || !result.value
      )
    ) {
      this.publish('error', 'storage-unavailable');
      return false;
    }
    const result = await this.store.discardMatching(query);
    if (result.kind === 'error') {
      this.publish('error', result.errorCode);
      return false;
    }
    for (const entry of result.deletedEntries) {
      this.durableKeys.delete(
        ordinaryEditorDraftProtectionKey(entry.section, entry.stableEntityKey)
      );
    }
    this.publish('ready', null);
    return true;
  }

  private applyControllerKnowledge(
    key: string,
    state: OrdinaryEditorDraftControllerSnapshot
  ) {
    if (state.hasDurableDraft) {
      this.durableKeys.add(key);
    } else if (state.status === 'saved') {
      this.durableKeys.delete(key);
    }
  }

  private async releaseTrackedController(
    token: number,
    tracked: TrackedController
  ) {
    const didDispose = await tracked.controller.dispose();
    if (!didDispose || this.trackedControllers.get(token) !== tracked) {
      if (!didDispose) {
        this.publish('error', 'storage-unavailable');
      }
      return didDispose;
    }
    tracked.unsubscribe();
    this.trackedControllers.delete(token);
    this.publish(this.snapshot.status, this.snapshot.errorCode);
    return true;
  }

  private publish(
    status: OrdinaryEditorDraftProtectionIndexStatus,
    errorCode: OrdinaryEditorDraftErrorCode | null
  ) {
    const protectedEntityKeys = new Set(this.durableKeys);
    const dirtyEntityKeys = new Set<string>();
    for (const tracked of this.trackedControllers.values()) {
      const state = tracked.controller.getSnapshot();
      if (state.hasPendingChanges) {
        dirtyEntityKeys.add(tracked.key);
      }
      if (state.hasDurableDraft || state.hasPendingChanges) {
        protectedEntityKeys.add(tracked.key);
      }
    }
    this.snapshot = {
      dirtyEntityKeys,
      durableEntityKeys: new Set(this.durableKeys),
      errorCode,
      protectedEntityKeys,
      revision:
        this.snapshot.revision === Number.MAX_SAFE_INTEGER
          ? 0
          : this.snapshot.revision + 1,
      status
    };
    for (const listener of this.listeners) {
      listener();
    }
  }
}

export function ordinaryEditorDraftProtectionKey(
  section: WorkbenchSection,
  stableEntityKey: string
) {
  return `${section}:${stableEntityKey}`;
}
