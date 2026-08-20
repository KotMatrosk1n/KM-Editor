/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import type { SemanticExploreRecordRef } from '../bridge/semanticExploreContracts';
import {
  getWorkbenchCapabilityRegistration,
  isCapabilityRegisteredForGame
} from './capabilityRegistry';
import {
  parseStableEntitySelection,
  stableWorkbenchLocationAdapters
} from './locationAdapterRegistry';
import {
  createWorkbenchLocation,
  type WorkbenchLocation
} from './workbenchLocation';

export function createSemanticExploreLocation(options: {
  game: ProjectGame;
  projectId: string;
  record: SemanticExploreRecordRef;
}): WorkbenchLocation | null {
  if (options.record.subrecordId !== null) {
    return null;
  }

  const matchingAdapters = stableWorkbenchLocationAdapters.filter((adapter) => {
    const registration = getWorkbenchCapabilityRegistration(adapter.section);
    return (
      adapter.recordKind === options.record.recordKind.key &&
      adapter.recordKindSchemaVersion === options.record.recordKind.schemaVersion &&
      registration.domain === options.record.domain &&
      isCapabilityRegisteredForGame(adapter.section, options.game)
    );
  });
  if (matchingAdapters.length !== 1) {
    return null;
  }

  try {
    const location = createWorkbenchLocation({
      entity: options.record,
      game: options.game,
      projectId: options.projectId,
      section: matchingAdapters[0]!.section
    });
    const selection = parseStableEntitySelection(location);
    return selection?.section === matchingAdapters[0]!.section
      ? location
      : null;
  } catch {
    return null;
  }
}
