/* SPDX-License-Identifier: GPL-3.0-only */

import {
  gameplaySettingsErrorCodes,
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
  ...Object.values(gameplaySettingsErrorCodes),
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
