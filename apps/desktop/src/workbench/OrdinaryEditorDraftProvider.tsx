/* SPDX-License-Identifier: GPL-3.0-only */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useSyncExternalStore,
  type ReactNode
} from 'react';
import type { ProjectDraftAdapter, ProjectDraftRegistry } from './draftRegistry';
import {
  OrdinaryEditorDraftController,
  type OrdinaryEditorDraftControllerSnapshot
} from './ordinaryEditorDraftController';
import {
  OrdinaryEditorDraftStore,
  type OrdinaryEditorDraftReconciliation,
  type OrdinaryEditorDraftScope
} from './ordinaryEditorDraftStore';
import {
  OrdinaryEditorDraftProtectionIndex,
  type OrdinaryEditorDraftProtectionIndexSnapshot
} from './ordinaryEditorDraftProtectionIndex';

const OrdinaryEditorDraftStoreContext =
  createContext<OrdinaryEditorDraftStore | null>(null);
const OrdinaryEditorDraftProtectionIndexContext =
  createContext<OrdinaryEditorDraftProtectionIndex | null>(null);

export type OrdinaryEditorDraftProviderProps = {
  children: ReactNode;
  registry: ProjectDraftRegistry;
};

export function OrdinaryEditorDraftProvider({
  children,
  registry
}: OrdinaryEditorDraftProviderProps) {
  const store = useMemo(() => new OrdinaryEditorDraftStore(registry), [registry]);
  const protectionIndex = useMemo(
    () => new OrdinaryEditorDraftProtectionIndex(store),
    [store]
  );
  return (
    <OrdinaryEditorDraftStoreContext.Provider value={store}>
      <OrdinaryEditorDraftProtectionIndexContext.Provider value={protectionIndex}>
        {children}
      </OrdinaryEditorDraftProtectionIndexContext.Provider>
    </OrdinaryEditorDraftStoreContext.Provider>
  );
}

export type UseOrdinaryEditorDraftOptions<TDraft> = {
  adapter: ProjectDraftAdapter<TDraft>;
  debounceMilliseconds?: number;
  isClean: (payload: TDraft) => boolean;
  onHydrate?: (payload: TDraft | null) => void;
  protectionIndex?: OrdinaryEditorDraftProtectionIndex;
  scope: OrdinaryEditorDraftScope | null;
  store?: OrdinaryEditorDraftStore;
};

export type UseOrdinaryEditorDraftResult<TDraft> =
  OrdinaryEditorDraftControllerSnapshot & {
    clearDurable: () => Promise<boolean>;
    flush: () => Promise<boolean>;
    discard: () => Promise<boolean>;
    reconcile: (
      resolution: OrdinaryEditorDraftReconciliation<TDraft>
    ) => Promise<boolean>;
    reload: () => Promise<void>;
    update: (payload: TDraft) => boolean;
  };

export function createOrdinaryEditorDraftCallbackBinding<TDraft>(callbacks: {
  isClean: (payload: TDraft) => boolean;
  onHydrate?: (payload: TDraft | null) => void;
}) {
  let currentCallbacks = callbacks;
  return {
    isClean: (payload: TDraft) => currentCallbacks.isClean(payload),
    onHydrate: (payload: TDraft | null) => currentCallbacks.onHydrate?.(payload),
    update: (nextCallbacks: typeof callbacks) => {
      currentCallbacks = nextCallbacks;
    }
  };
}

export function useOrdinaryEditorDraft<TDraft>(
  options: UseOrdinaryEditorDraftOptions<TDraft>
): UseOrdinaryEditorDraftResult<TDraft> {
  const providedStore = useContext(OrdinaryEditorDraftStoreContext);
  const providedProtectionIndex = useContext(
    OrdinaryEditorDraftProtectionIndexContext
  );
  const store = options.store ?? providedStore;
  const protectionIndex = options.protectionIndex ?? providedProtectionIndex;
  if (!store) {
    throw new Error(
      'useOrdinaryEditorDraft requires an OrdinaryEditorDraftProvider or an explicit store.'
    );
  }

  const scopeIdentity = options.scope ? renderScopeIdentity(options.scope) : null;
  const controllerCallbacks = useMemo(
    () => createOrdinaryEditorDraftCallbackBinding({
      isClean: options.isClean,
      onHydrate: options.onHydrate
    }),
    [options.adapter, scopeIdentity, store]
  );
  controllerCallbacks.update({
    isClean: options.isClean,
    onHydrate: options.onHydrate
  });

  const controller = useMemo(
    () =>
      options.scope
        ? new OrdinaryEditorDraftController({
            adapter: options.adapter,
            debounceMilliseconds: options.debounceMilliseconds,
            isClean: controllerCallbacks.isClean,
            onHydrate: controllerCallbacks.onHydrate,
            scope: options.scope,
            store
          })
        : null,
    [
      options.adapter,
      options.debounceMilliseconds,
      controllerCallbacks,
      scopeIdentity,
      store
    ]
  );

  useEffect(() => {
    if (!controller || !options.scope) {
      return;
    }
    const stopTracking = protectionIndex?.track(options.scope, controller);
    void controller.hydrate();
    return () => {
      if (stopTracking) {
        stopTracking();
      } else {
        void controller.dispose();
      }
    };
  }, [controller, protectionIndex]);

  const snapshot = useSyncExternalStore(
    controller?.subscribe ?? emptySubscribe,
    controller?.getSnapshot ?? getIdleSnapshot,
    controller?.getSnapshot ?? getIdleSnapshot
  );
  const discard = useCallback(
    () => controller?.discard() ?? Promise.resolve(true),
    [controller]
  );
  const clearDurable = useCallback(
    () => controller?.clearDurable() ?? Promise.resolve(true),
    [controller]
  );
  const flush = useCallback(
    () => controller?.flush() ?? Promise.resolve(true),
    [controller]
  );
  const reconcile = useCallback(
    (resolution: OrdinaryEditorDraftReconciliation<TDraft>) =>
      controller?.reconcile(resolution) ?? Promise.resolve(false),
    [controller]
  );
  const reload = useCallback(
    () => controller?.hydrate() ?? Promise.resolve(),
    [controller]
  );
  const update = useCallback(
    (payload: TDraft) => controller?.update(payload) ?? false,
    [controller]
  );

  return useMemo(
    () => ({
      ...snapshot,
      clearDurable,
      discard,
      flush,
      reconcile,
      reload,
      update
    }),
    [clearDurable, discard, flush, reconcile, reload, snapshot, update]
  );
}

const idleSnapshot: OrdinaryEditorDraftControllerSnapshot = {
  errorCode: null,
  hasDurableDraft: false,
  hasPendingChanges: false,
  staleInspection: null,
  status: 'idle',
  updatedAtUtc: null
};

function emptySubscribe() {
  return () => undefined;
}

function getIdleSnapshot() {
  return idleSnapshot;
}

export function useOrdinaryEditorDraftProtectionIndex() {
  const index = useContext(OrdinaryEditorDraftProtectionIndexContext);
  if (!index) {
    throw new Error(
      'useOrdinaryEditorDraftProtectionIndex requires an OrdinaryEditorDraftProvider.'
    );
  }
  return index;
}

export function useOrdinaryEditorDraftProtectionSnapshot(): OrdinaryEditorDraftProtectionIndexSnapshot {
  const index = useOrdinaryEditorDraftProtectionIndex();
  return useSyncExternalStore(
    index.subscribe,
    index.getSnapshot,
    index.getSnapshot
  );
}

function renderScopeIdentity(scope: OrdinaryEditorDraftScope) {
  return JSON.stringify({
    domain: scope.domain,
    entity: scope.entity,
    game: scope.game,
    projectId: scope.projectId,
    section: scope.section,
    sourceRevisionFingerprint: scope.sourceRevisionFingerprint
  });
}
