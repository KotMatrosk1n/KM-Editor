/* SPDX-License-Identifier: GPL-3.0-only */

import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState
} from 'react';
import { type ApiDiagnostic } from '../bridge/contracts';
import {
  diagnosticListFingerprint,
  mergeEditorDiagnostics,
  updateEditorDiagnosticsSource
} from './commonEditorDiagnosticsState';

type CommonEditorDiagnosticsRegistration = {
  publish: (sourceId: string, diagnostics: readonly ApiDiagnostic[]) => void;
  withdraw: (sourceId: string) => void;
};

const CommonEditorDiagnosticsRegistrationContext =
  createContext<CommonEditorDiagnosticsRegistration | null>(null);
const CommonEditorDiagnosticsSnapshotContext =
  createContext<readonly ApiDiagnostic[]>([]);
const CommonEditorDiagnosticsPublishingContext = createContext(true);

export function CommonEditorDiagnosticsPublishingScope({
  children,
  enabled
}: {
  children: ReactNode;
  enabled: boolean;
}) {
  return (
    <CommonEditorDiagnosticsPublishingContext.Provider value={enabled}>
      {children}
    </CommonEditorDiagnosticsPublishingContext.Provider>
  );
}

export function CommonEditorDiagnosticsProvider({ children }: { children: ReactNode }) {
  const [diagnosticsBySource, setDiagnosticsBySource] = useState<
    ReadonlyMap<string, readonly ApiDiagnostic[]>
  >(() => new Map());

  const publish = useCallback(
    (sourceId: string, diagnostics: readonly ApiDiagnostic[]) => {
      setDiagnosticsBySource((current) =>
        updateEditorDiagnosticsSource(current, sourceId, diagnostics)
      );
    },
    []
  );
  const withdraw = useCallback((sourceId: string) => {
    setDiagnosticsBySource((current) =>
      updateEditorDiagnosticsSource(current, sourceId, [])
    );
  }, []);

  const registration = useMemo(
    () => ({ publish, withdraw }),
    [publish, withdraw]
  );
  const diagnostics = useMemo(
    () => mergeEditorDiagnostics(...diagnosticsBySource.values()),
    [diagnosticsBySource]
  );

  return (
    <CommonEditorDiagnosticsRegistrationContext.Provider value={registration}>
      <CommonEditorDiagnosticsSnapshotContext.Provider value={diagnostics}>
        {children}
      </CommonEditorDiagnosticsSnapshotContext.Provider>
    </CommonEditorDiagnosticsRegistrationContext.Provider>
  );
}

/**
 * Mirrors diagnostics from an editor-local presentation into the common bottom
 * diagnostics without making the caller retain a stable array reference.
 */
export function usePublishCommonEditorDiagnostics(
  diagnostics: readonly ApiDiagnostic[],
  enabled = true
) {
  const registration = useContext(CommonEditorDiagnosticsRegistrationContext);
  const publishingScopeEnabled = useContext(CommonEditorDiagnosticsPublishingContext);
  const sourceId = useId();
  const latestDiagnosticsRef = useRef(diagnostics);
  latestDiagnosticsRef.current = diagnostics;
  const fingerprint = diagnosticListFingerprint(diagnostics);

  useEffect(() => {
    if (!enabled || !publishingScopeEnabled || !registration) {
      return undefined;
    }
    registration.publish(sourceId, latestDiagnosticsRef.current);
    return () => registration.withdraw(sourceId);
  }, [enabled, fingerprint, publishingScopeEnabled, registration, sourceId]);
}

export function useCommonEditorDiagnostics() {
  return useContext(CommonEditorDiagnosticsSnapshotContext);
}

export function usePublishCommonEditorError({
  domain,
  field,
  message
}: {
  domain: string;
  field?: string;
  message: string | null;
}) {
  const diagnostics = useMemo<ApiDiagnostic[]>(
    () => message === null ? [] : [{ domain, field, message, severity: 'error' }],
    [domain, field, message]
  );
  usePublishCommonEditorDiagnostics(diagnostics);
}

export function PublishCommonEditorError(props: {
  domain: string;
  field?: string;
  message: string;
}) {
  usePublishCommonEditorError(props);
  return null;
}
