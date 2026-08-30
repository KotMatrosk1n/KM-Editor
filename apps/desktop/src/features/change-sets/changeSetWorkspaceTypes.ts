/* SPDX-License-Identifier: GPL-3.0-only */

import type { ApiDiagnostic } from '../../bridge/contracts';

export type ChangeSetOperationState =
  | 'ready'
  | 'disabled'
  | 'stale'
  | 'conflict'
  | 'unsupported';

export type ChangeSetOperationViewModel = {
  adapterLabel: string;
  description: string | null;
  id: string;
  position: number;
  provenanceLabel: string;
  state: ChangeSetOperationState;
  targetLabel: string;
  title: string;
};

export type ChangeSetConflictViewModel = {
  id: string;
  message: string;
  targetLabel: string | null;
};

export type ChangeSetViewModel = {
  conflictCount: number;
  conflicts: readonly ChangeSetConflictViewModel[];
  dependencyIds: readonly string[];
  id: string;
  isActiveStagingTarget: boolean;
  isArchived: boolean;
  isEnabled: boolean;
  name: string;
  notes: string;
  operationCount: number;
  operations: readonly ChangeSetOperationViewModel[];
  operationsAreTruncated: boolean;
  staleOperationCount: number;
  tags: readonly string[];
  updatedAtUtc: string;
};

export type ChangeSetBuildVariantViewModel = {
  enabledChangeSetCount: number;
  enabledChangeSetIds: readonly string[];
  id: string;
  isActive: boolean;
  name: string;
  outputModeLabel: string;
  outputProfileName: string | null;
};

export type ChangeSetOutputModeViewModel = {
  id: string;
  label: string;
};

export type ChangeSetOutputProfileViewModel = {
  id: string;
  isActive: boolean;
  name: string;
};

export type ChangeSetComparisonEntryViewModel = {
  kind: 'added' | 'removed' | 'reordered' | 'changed' | 'unavailable' | 'undecodable';
  leftValue: string | null;
  operationId: string;
  ownerId: string | null;
  ownerLabel: string | null;
  rightValue: string | null;
  targetLabel: string;
};

export type ChangeSetComparisonViewModel = {
  entries: readonly ChangeSetComparisonEntryViewModel[];
  isTruncated: boolean;
  selectedChangeSetId: string;
  state: 'available' | 'unavailable';
  unavailableReason: string | null;
};

export type ChangeSetWorkspaceReadiness =
  | 'unavailable'
  | 'loading'
  | 'ready'
  | 'error';

export type ChangeSetWorkspaceBusyAction =
  | 'load'
  | 'create'
  | 'setActive'
  | 'enable'
  | 'rename'
  | 'duplicate'
  | 'reorder'
  | 'archive'
  | 'delete'
  | 'restore'
  | 'metadata'
  | 'undo'
  | 'redo'
  | 'variant'
  | 'comparison'
  | 'operations';

export type ChangeSetWorkspaceController = {
  activeStagingTargetId: string | null;
  availableOutputModes: readonly ChangeSetOutputModeViewModel[];
  availableOutputProfiles: readonly ChangeSetOutputProfileViewModel[];
  buildVariants: readonly ChangeSetBuildVariantViewModel[];
  busyAction: ChangeSetWorkspaceBusyAction | null;
  canMaterialize: boolean;
  canRedo: boolean;
  canUndo: boolean;
  changeSets: readonly ChangeSetViewModel[];
  comparison: ChangeSetComparisonViewModel | null;
  diagnostics: readonly ApiDiagnostic[];
  externalBusy: boolean;
  legacyUnsupportedOperationCount: number;
  onArchive: (changeSetId: string) => void;
  onCreate: (name: string) => Promise<boolean>;
  onCreateBuildVariant: (
    name: string,
    enabledChangeSetIds: readonly string[],
    outputMode: string | null,
    outputProfileId: string | null
  ) => Promise<boolean>;
  onDeleteBuildVariant: (variantId: string) => void;
  onDeleteSet: (changeSetId: string) => void;
  onDuplicate: (changeSetId: string) => void;
  onExport: (changeSetId: string) => void;
  onImport: (packageJson: string, enableImported: boolean) => void;
  onLoadComparison: (changeSetId: string) => void;
  onMove: (changeSetId: string, direction: 'up' | 'down') => void;
  onMoveOperation: (
    changeSetId: string,
    operationId: string,
    direction: 'up' | 'down'
  ) => void;
  onRedo: () => void;
  onRemoveOperation: (changeSetId: string, operationId: string) => void;
  onRefresh: () => void;
  onRename: (changeSetId: string, name: string) => void;
  onRestore: (changeSetId: string) => void;
  onSelectBuildVariant: (variantId: string | null) => void;
  onSetActiveStagingTarget: (changeSetId: string) => void;
  onSetEnabled: (changeSetId: string, isEnabled: boolean) => void;
  onUndo: () => void;
  onUpdateMetadata: (
    changeSetId: string,
    notes: string,
    tags: readonly string[],
    dependencyIds: readonly string[]
  ) => void;
  readiness: ChangeSetWorkspaceReadiness;
  requiredOutputProfileId: string | null;
  requiredOutputProfileName: string | null;
  redoLabel: string | null;
  selectedChangeSetId: string | null;
  setSelectedChangeSetId: (changeSetId: string | null) => void;
  onRequestOutputProfileSwitch?: (outputProfileId: string) => void;
  undoLabel: string | null;
  unassignedOperationCount: number;
};
