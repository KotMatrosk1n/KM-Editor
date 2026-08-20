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
import { projectGameToFamily } from './semanticContracts';
import {
  createWorkbenchLocation,
  type WorkbenchLocation
} from './workbenchLocation';

const canonicalSlotPattern = /^(?:0|[1-9][0-9]{0,5})$/u;

export function createBalanceLabLocation(options: {
  game: ProjectGame;
  projectId: string;
  record: SemanticExploreRecordRef;
}): WorkbenchLocation | null {
  if (
    options.record.gameFamily !== projectGameToFamily(options.game) ||
    !isSupportedLabSubrecord(options.record)
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
    return selection?.section === matchingAdapters[0]!.section &&
      selection.subrecordId === options.record.subrecordId
      ? location
      : null;
  } catch {
    return null;
  }
}

export function parseBalanceLabSlotSubrecord(
  section: 'encounters' | 'trainers',
  subrecordId: string | null
): number | null | undefined {
  if (subrecordId === null) {
    return null;
  }
  const prefix = section === 'trainers' ? 'party-slot:' : 'slot:';
  if (!subrecordId.startsWith(prefix)) {
    return undefined;
  }
  const value = subrecordId.slice(prefix.length);
  return canonicalSlotPattern.test(value) ? Number(value) : undefined;
}

function isSupportedLabSubrecord(record: SemanticExploreRecordRef) {
  if (record.subrecordId === null) {
    return true;
  }
  if (record.domain === 'workflow.trainers' && record.recordKind.key === 'trainer') {
    return parseBalanceLabSlotSubrecord('trainers', record.subrecordId) !== undefined;
  }
  if (
    record.domain === 'workflow.encounters' &&
    record.recordKind.key === 'encounter-table'
  ) {
    return parseBalanceLabSlotSubrecord('encounters', record.subrecordId) !== undefined;
  }
  return false;
}
