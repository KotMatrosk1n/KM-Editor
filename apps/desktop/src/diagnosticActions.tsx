/* SPDX-License-Identifier: GPL-3.0-only */

import { createContext, type ReactNode, useContext, useMemo } from 'react';
import type { ApiDiagnostic } from './bridge/contracts';
import { desktopErrorCodes, projectBridgeErrorCodes } from './errorCodes';
import { workbenchCapabilityRegistry } from './workbench/capabilityRegistry';
import { createSectionLocation, type WorkbenchLocation } from './workbench/workbenchLocation';
import type { WorkbenchSection } from './workbench/workbenchSections';

export type DiagnosticNavigationAction = {
  location: WorkbenchLocation;
  targetLabel: string;
};

export type DiagnosticNavigationProviderProps = {
  activeLocation: WorkbenchLocation | null;
  children: ReactNode;
  onNavigate: (location: WorkbenchLocation) => void;
};

type DiagnosticNavigationContextValue = {
  navigate: (location: WorkbenchLocation) => void;
  resolveAction: (diagnostic: ApiDiagnostic) => DiagnosticNavigationAction | null;
};

const outputCodes = new Set<string>([
  projectBridgeErrorCodes.outputCheckpointConflict,
  projectBridgeErrorCodes.outputCheckpointNotFound,
  projectBridgeErrorCodes.outputConcurrentModification,
  projectBridgeErrorCodes.outputLimitExceeded,
  projectBridgeErrorCodes.outputOwnershipUnproven,
  projectBridgeErrorCodes.outputRecoveryRequired,
  projectBridgeErrorCodes.outputRootBusy,
  projectBridgeErrorCodes.outputUnsafePath
]);
const projectCodes = new Set<string>([
  projectBridgeErrorCodes.accessDenied,
  projectBridgeErrorCodes.dataInvalid,
  projectBridgeErrorCodes.dataLayoutInvalid,
  projectBridgeErrorCodes.dataSupportUnavailable,
  projectBridgeErrorCodes.gameMismatch,
  projectBridgeErrorCodes.ioFailed,
  projectBridgeErrorCodes.projectRelocationConflict,
  projectBridgeErrorCodes.projectRelocationMismatch,
  projectBridgeErrorCodes.resourceMissing,
  projectBridgeErrorCodes.workspaceConcurrentModification,
  desktopErrorCodes.bridgeRecycleFailed,
  desktopErrorCodes.runtimeUnavailable
]);
const settingsCodes = new Set<string>([
  desktopErrorCodes.updateCheckFailed,
  desktopErrorCodes.updateCloseFailed,
  desktopErrorCodes.updateInstallFailed
]);
const capabilityByDomain = new Map(
  workbenchCapabilityRegistry.map((capability) => [capability.domain, capability] as const)
);

const defaultContext: DiagnosticNavigationContextValue = {
  navigate: () => undefined,
  resolveAction: () => null
};
const DiagnosticNavigationContext =
  createContext<DiagnosticNavigationContextValue>(defaultContext);

export function DiagnosticNavigationProvider({
  activeLocation,
  children,
  onNavigate
}: DiagnosticNavigationProviderProps) {
  const value = useMemo<DiagnosticNavigationContextValue>(
    () => ({
      navigate: onNavigate,
      resolveAction: (diagnostic) => resolveDiagnosticNavigationAction(diagnostic, activeLocation)
    }),
    [activeLocation, onNavigate]
  );
  return (
    <DiagnosticNavigationContext.Provider value={value}>
      {children}
    </DiagnosticNavigationContext.Provider>
  );
}

export function useDiagnosticNavigation() {
  return useContext(DiagnosticNavigationContext);
}

export function resolveDiagnosticNavigationAction(
  diagnostic: ApiDiagnostic,
  activeLocation: WorkbenchLocation | null
): DiagnosticNavigationAction | null {
  if (!activeLocation) {
    return null;
  }
  const destination = resolveDestination(diagnostic);
  if (!destination || destination === activeLocation.section) {
    return null;
  }
  const capability = workbenchCapabilityRegistry.find((entry) => entry.id === destination);
  if (!capability) {
    return null;
  }
  try {
    return {
      location: createSectionLocation(destination, {
        game: activeLocation.game,
        projectId: activeLocation.projectId
      }),
      targetLabel: capability.label
    };
  } catch {
    return null;
  }
}

function resolveDestination(diagnostic: ApiDiagnostic): WorkbenchSection | null {
  if (diagnostic.code && outputCodes.has(diagnostic.code)) {
    return 'changes';
  }
  if (diagnostic.code && projectCodes.has(diagnostic.code)) {
    return 'health';
  }
  if (diagnostic.code && settingsCodes.has(diagnostic.code)) {
    return 'settings';
  }
  if (!diagnostic.domain) {
    return null;
  }
  return capabilityByDomain.get(diagnostic.domain)?.id ?? null;
}
