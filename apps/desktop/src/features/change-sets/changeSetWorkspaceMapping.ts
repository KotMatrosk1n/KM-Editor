/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ChangeSetMaterialization,
  ChangeSetWorkspaceSnapshot,
  NamedChangeSet
} from '../../bridge/changeSetContracts';
import type { LocalizationContextValue } from '../../localization';
import type {
  ChangeSetBuildVariantViewModel,
  ChangeSetComparisonViewModel,
  ChangeSetOperationState,
  ChangeSetOutputModeViewModel,
  ChangeSetOutputProfileViewModel,
  ChangeSetViewModel
} from './changeSetWorkspaceTypes';

type Translate = LocalizationContextValue['t'];

export type ChangeSetWorkspaceMappedState = {
  buildVariants: readonly ChangeSetBuildVariantViewModel[];
  changeSets: readonly ChangeSetViewModel[];
  comparison: ChangeSetComparisonViewModel | null;
  legacyUnsupportedOperationCount: number;
  requiredOutputProfileId: string | null;
  requiredOutputProfileName: string | null;
  unassignedOperationCount: number;
};

export function mapChangeSetWorkspaceState(
  snapshot: ChangeSetWorkspaceSnapshot | null,
  activeChangeSetId: string | null,
  selectedComparisonId: string | null,
  outputModes: readonly ChangeSetOutputModeViewModel[],
  outputProfiles: readonly ChangeSetOutputProfileViewModel[],
  t: Translate
): ChangeSetWorkspaceMappedState {
  if (!snapshot) {
    return {
      buildVariants: [],
      changeSets: [],
      comparison: null,
      legacyUnsupportedOperationCount: 0,
      requiredOutputProfileId: null,
      requiredOutputProfileName: null,
      unassignedOperationCount: 0
    };
  }

  const summaries = new Map(
    snapshot.effective.operations.map((operation) => [operation.operationId, operation])
  );
  const selectedSetIds = new Set(snapshot.effective.selectedChangeSetIds);
  const changeSets = snapshot.document.changeSets.map((changeSet) => {
    const conflicts = snapshot.effective.conflicts.filter((conflict) => (
      conflict.changeSetIds.includes(changeSet.changeSetId)
    ));
    const operations = changeSet.operations.map((operation, position) => {
      const summary = summaries.get(operation.operationId);
      return {
        adapterLabel: t(`changeSets.operations.binding.${operation.sourceBindingKind}`),
        description: summary?.description || null,
        id: operation.operationId,
        position,
        provenanceLabel: t('changeSets.operations.captured'),
        state: mapOperationState(
          summary?.state,
          selectedSetIds.has(changeSet.changeSetId),
          changeSet.enabled,
          changeSet.archived
        ),
        targetLabel: summary?.target || formatPendingEditTarget(changeSet, position),
        title: summary?.title || operation.pendingEdit.summary
      };
    });
    return {
      conflictCount: conflicts.length,
      conflicts: conflicts.map((conflict, index) => ({
        id: `${conflict.kind}:${conflict.target ?? ''}:${index}`,
        message: conflict.message,
        targetLabel: conflict.target
      })),
      dependencyIds: changeSet.dependencyIds,
      id: changeSet.changeSetId,
      isActiveStagingTarget: changeSet.changeSetId === activeChangeSetId,
      isArchived: changeSet.archived,
      isEnabled: changeSet.enabled,
      name: changeSet.name,
      notes: changeSet.notes ?? '',
      operationCount: changeSet.operations.length,
      operations,
      operationsAreTruncated: false,
      staleOperationCount: operations.filter((operation) => operation.state === 'stale').length,
      tags: changeSet.tags,
      updatedAtUtc: changeSet.updatedAtUtc
    } satisfies ChangeSetViewModel;
  });
  const activeOutputProfileId = outputProfiles.find((profile) => profile.isActive)?.id ?? null;
  const requiredOutputProfileId = snapshot.effective.outputProfileId !== null &&
    snapshot.effective.outputProfileId !== activeOutputProfileId
    ? snapshot.effective.outputProfileId
    : null;

  return {
    buildVariants: snapshot.document.buildVariants.map((variant) => ({
      enabledChangeSetCount: variant.changeSetIds.length,
      enabledChangeSetIds: variant.changeSetIds,
      id: variant.variantId,
      isActive: variant.variantId === snapshot.document.activeBuildVariantId,
      name: variant.name,
      outputModeLabel: variant.outputMode === null
        ? t('changeSets.variants.currentMode')
        : outputModes.find((mode) => mode.id === variant.outputMode)?.label
          ?? t(`changeSets.outputMode.${variant.outputMode}`),
      outputProfileName: variant.outputProfileId === null
        ? t('changeSets.variants.currentProfile')
        : outputProfiles.find((profile) => profile.id === variant.outputProfileId)?.name ?? null
    })),
    changeSets,
    comparison: createComparison(
      snapshot.document.changeSets.find((changeSet) => (
        changeSet.changeSetId === selectedComparisonId
      )) ?? null,
      snapshot.effective,
      t
    ),
    legacyUnsupportedOperationCount: snapshot.effective.operations.filter(
      (operation) => operation.state === 'legacyUnsupported'
    ).length,
    requiredOutputProfileId,
    requiredOutputProfileName: requiredOutputProfileId === null
      ? null
      : outputProfiles.find((profile) => profile.id === requiredOutputProfileId)?.name ?? null,
    unassignedOperationCount: snapshot.effective.operations.filter(
      (operation) => operation.changeSetId === null
    ).length
  };
}

function mapOperationState(
  state: ChangeSetMaterialization['operations'][number]['state'] | undefined,
  isSelected: boolean,
  isEnabled: boolean,
  isArchived: boolean
): ChangeSetOperationState {
  if (isArchived || !isEnabled || !isSelected) return 'disabled';
  switch (state) {
    case 'fresh':
    case 'sessionLocal':
      return 'ready';
    case 'stale':
      return 'stale';
    case 'conflict':
      return 'conflict';
    case 'legacyUnsupported':
    case undefined:
      return 'unsupported';
  }
}

function formatPendingEditTarget(changeSet: NamedChangeSet, operationIndex: number) {
  const edit = changeSet.operations[operationIndex]!.pendingEdit;
  return [edit.domain, edit.recordId, edit.field].filter(Boolean).join(' / ') || edit.summary;
}

function createComparison(
  selectedSet: NamedChangeSet | null,
  effective: ChangeSetMaterialization,
  t: Translate
): ChangeSetComparisonViewModel | null {
  if (!selectedSet) return null;
  const selectedOperationIds = new Set(
    selectedSet.operations.map((operation) => operation.operationId)
  );
  const effectiveById = new Map(
    effective.operations.map((operation) => [operation.operationId, operation])
  );
  const effectiveSelectedOperations = effective.operations.filter((operation) => (
    operation.changeSetId === selectedSet.changeSetId
  ));
  const effectiveSelectedPositions = new Map(
    effectiveSelectedOperations.map((operation, index) => [operation.operationId, index])
  );
  const entries: ChangeSetComparisonViewModel['entries'][number][] = [];

  selectedSet.operations.forEach((operation, index) => {
    const summary = effectiveById.get(operation.operationId);
    const targetLabel = summary?.target || formatPendingEditTarget(selectedSet, index);
    if (!summary) {
      entries.push({
        kind: 'removed',
        leftValue: operation.pendingEdit.summary,
        ownerLabel: selectedSet.name,
        rightValue: null,
        targetLabel
      });
      return;
    }
    if (summary.state === 'legacyUnsupported') {
      entries.push({
        kind: 'undecodable',
        leftValue: operation.pendingEdit.summary,
        ownerLabel: summary.changeSetName,
        rightValue: summary.description || summary.title,
        targetLabel
      });
      return;
    }
    if (summary.state === 'stale' || summary.state === 'conflict') {
      entries.push({
        kind: 'unavailable',
        leftValue: operation.pendingEdit.summary,
        ownerLabel: summary.changeSetName,
        rightValue: summary.description || summary.title,
        targetLabel
      });
      return;
    }
    if (effectiveSelectedPositions.get(operation.operationId) !== index) {
      entries.push({
        kind: 'reordered',
        leftValue: t('changeSets.comparison.position', { position: index + 1 }),
        ownerLabel: summary.changeSetName,
        rightValue: t('changeSets.comparison.position', {
          position: (effectiveSelectedPositions.get(operation.operationId) ?? -1) + 1
        }),
        targetLabel
      });
    }
  });

  for (const operation of effective.operations) {
    if (selectedOperationIds.has(operation.operationId)) continue;
    entries.push({
      kind: 'added',
      leftValue: null,
      ownerLabel: operation.changeSetName,
      rightValue: operation.description || operation.title,
      targetLabel: operation.target
    });
  }

  return {
    entries,
    isTruncated: false,
    selectedChangeSetId: selectedSet.changeSetId,
    state: 'available',
    unavailableReason: null
  };
}
