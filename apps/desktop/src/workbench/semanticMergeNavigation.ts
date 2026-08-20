/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import type { SemanticExploreRecordRef } from '../bridge/semanticExploreContracts';
import { createSemanticExploreLocation } from './semanticExploreNavigation';
import type { WorkbenchLocation } from './workbenchLocation';

const supportedRecords = new Set([
  'workflow.items:item',
  'workflow.pokemon:pokemon-personal',
  'workflow.moves:move'
]);

export function createSemanticMergeLocation(options: {
  game: ProjectGame;
  projectId: string;
  record: SemanticExploreRecordRef;
}): WorkbenchLocation | null {
  if (
    (options.game !== 'sword' && options.game !== 'shield') ||
    options.record.gameFamily !== 'swordShield' ||
    options.record.recordKind.schemaVersion !== 1 ||
    options.record.subrecordId !== null ||
    !supportedRecords.has(`${options.record.domain}:${options.record.recordKind.key}`)
  ) {
    return null;
  }

  return createSemanticExploreLocation(options);
}
