/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import type { SemanticExploreRecordRef } from '../bridge/semanticExploreContracts';
import {
  getWorkbenchCapabilityRegistration,
  isCapabilityRegisteredForGame
} from './capabilityRegistry';
import { createBalanceLabLocation } from './balanceLabNavigation';
import {
  parseStableEntitySelection,
  stableWorkbenchLocationAdapters
} from './locationAdapterRegistry';
import { projectGameToFamily } from './semanticContracts';
import {
  createWorkbenchLocation,
  type WorkbenchLocation
} from './workbenchLocation';

const evolutionSlotPattern = /^evolution-slot:(0|[1-9][0-9]*)$/u;

export function createGuidedDesignLocation(options: {
  game: ProjectGame;
  projectId: string;
  record: SemanticExploreRecordRef;
}): WorkbenchLocation | null {
  if (!isGuidedDesignRecordShape(options.record)) return null;
  const existingLocation = createBalanceLabLocation(options);
  if (existingLocation) return existingLocation;
  if (
    options.record.gameFamily !== projectGameToFamily(options.game) ||
    options.record.domain !== 'workflow.pokemon' ||
    options.record.recordKind.key !== 'pokemon-personal' ||
    parseGuidedDesignEvolutionSubrecord(options.record.subrecordId) === undefined
  ) {
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
  if (matchingAdapters.length !== 1) return null;

  try {
    const location = createWorkbenchLocation({
      entity: options.record,
      game: options.game,
      projectId: options.projectId,
      section: matchingAdapters[0]!.section
    });
    const selection = parseStableEntitySelection(location);
    return selection?.section === matchingAdapters[0]!.section &&
      selection.subrecordId === options.record.subrecordId
      ? location
      : null;
  } catch {
    return null;
  }
}

function isGuidedDesignRecordShape(record: SemanticExploreRecordRef) {
  if (record.recordKind.schemaVersion !== 1) return false;
  switch (record.domain) {
    case 'workflow.items':
      return record.recordKind.key === 'item' && record.subrecordId === null;
    case 'workflow.pokemon':
      return record.recordKind.key === 'pokemon-personal' &&
        parseGuidedDesignEvolutionSubrecord(record.subrecordId) !== undefined;
    case 'workflow.trainers':
      return record.recordKind.key === 'trainer' && (
        record.subrecordId === null ||
        /^party-slot:(0|[1-9][0-9]*)$/u.test(record.subrecordId)
      );
    case 'workflow.encounters':
      return record.recordKind.key === 'encounter-table' && (
        record.subrecordId === null ||
        /^slot:(0|[1-9][0-9]*)$/u.test(record.subrecordId)
      );
    default:
      return false;
  }
}

export function parseGuidedDesignEvolutionSubrecord(
  subrecordId: string | null
): number | null | undefined {
  if (subrecordId === null) return null;
  const match = evolutionSlotPattern.exec(subrecordId);
  if (!match) return undefined;
  const slot = Number(match[1]);
  return Number.isSafeInteger(slot) ? slot : undefined;
}
