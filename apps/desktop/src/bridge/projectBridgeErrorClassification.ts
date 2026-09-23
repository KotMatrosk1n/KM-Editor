/* SPDX-License-Identifier: GPL-3.0-only */

import {
  gameplaySettingsErrorCodes,
  mergeWorkspaceErrorCodes,
  guidedDesignErrorCodes,
  inGameSettingsPackageErrorCodes,
  kmRecipeErrorCodes,
  projectBridgeErrorCodes,
  researchLabErrorCodes,
  semanticExploreErrorCodes,
  semanticMergeErrorCodes,
  swshDynamaxAdventuresErrorCodes,
  swshPlacementErrorCodes,
  type KmErrorCode
} from '../errorCodes';
import { ProjectBridgeError } from './projectBridgeError';

// These codes describe anticipated capability boundaries, stale reviews, and
// safety guards. They remain visible to the caller, but they are not evidence
// that the bridge itself failed unexpectedly.
const expectedProjectBridgeRejectionCodes = new Set<KmErrorCode>([
  projectBridgeErrorCodes.zaBehaviorSelectionInvalid,
  projectBridgeErrorCodes.zaBehaviorValueInvalid,
  projectBridgeErrorCodes.zaBehaviorSessionInvalid,
  projectBridgeErrorCodes.zaBehaviorPlanStale,
  projectBridgeErrorCodes.zaPokemonSizeSelectionInvalid,
  projectBridgeErrorCodes.zaPokemonSizeBindingInvalid,
  projectBridgeErrorCodes.zaPokemonSizeSessionConflict,
  projectBridgeErrorCodes.zaPokemonSizePlanStale,
  projectBridgeErrorCodes.workspaceDraftInvalid,
  projectBridgeErrorCodes.workspacePersonalStateInvalid,
  projectBridgeErrorCodes.changeSetInvalid,
  projectBridgeErrorCodes.editSessionContractInvalid,
  projectBridgeErrorCodes.outputScopeMismatch,
  projectBridgeErrorCodes.outputReviewExpired,
  projectBridgeErrorCodes.outputOwnershipConflict,
  projectBridgeErrorCodes.outputCheckpointAlreadyCurrent,
  projectBridgeErrorCodes.outputPreimageChanged,
  projectBridgeErrorCodes.outputReviewStateUnverifiable,
  projectBridgeErrorCodes.outputStateRevisionChanged,
  projectBridgeErrorCodes.outputMetadataUnavailable,

  ...Object.values(gameplaySettingsErrorCodes),
  ...Object.values(mergeWorkspaceErrorCodes).filter(code => code !== mergeWorkspaceErrorCodes.exportFailed && code !== mergeWorkspaceErrorCodes.outputRecovery),
  ...Object.values(guidedDesignErrorCodes),
  ...Object.values(inGameSettingsPackageErrorCodes),
  ...Object.values(kmRecipeErrorCodes),
  ...Object.values(semanticMergeErrorCodes),
  ...Object.values(semanticExploreErrorCodes),
  ...Object.values(researchLabErrorCodes),
  swshDynamaxAdventuresErrorCodes.seedInvalid,
  swshDynamaxAdventuresErrorCodes.seedLimitInvalid,
  swshDynamaxAdventuresErrorCodes.startSeedInvalid,
  swshPlacementErrorCodes.catalogStale,
  projectBridgeErrorCodes.gameMismatch,
  projectBridgeErrorCodes.outputCheckpointConflict,
  projectBridgeErrorCodes.outputCheckpointNotFound,
  projectBridgeErrorCodes.outputConcurrentModification,
  projectBridgeErrorCodes.outputLimitExceeded,
  projectBridgeErrorCodes.outputOwnershipUnproven,
  projectBridgeErrorCodes.outputRecoveryRequired,
  projectBridgeErrorCodes.outputRootBusy,
  projectBridgeErrorCodes.outputUnsafePath,
  projectBridgeErrorCodes.projectRelocationConflict,
  projectBridgeErrorCodes.projectRelocationMismatch,
  projectBridgeErrorCodes.workspaceConcurrentModification
]);

export function isExpectedProjectBridgeRejection(error: unknown) {
  return error instanceof ProjectBridgeError &&
    error.semanticCode !== null &&
    expectedProjectBridgeRejectionCodes.has(error.semanticCode);
}
