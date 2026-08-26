/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import { getWorkbenchCapabilityRegistration } from './capabilityRegistry';
import {
  createWorkbenchLocation,
  type WorkbenchLocation
} from './workbenchLocation';
import type { WorkbenchSection } from './workbenchSections';
import {
  getWorkbenchRecordIdentityRegistration,
  type RecordIdentityStability
} from './recordIdentityRegistry';
import {
  projectGameToFamily,
  type SemanticRecordRef
} from './semanticContracts';

export type StableLocationValueKind = 'integer' | 'string';
export type LocationAdapterTabPolicy = 'readOnly' | 'requiresDraftAdapter';

export type StableLocationAdapter = {
  recordKind: string;
  recordKindSchemaVersion: number;
  identityStability: RecordIdentityStability;
  section: WorkbenchSection;
  tabPolicy: LocationAdapterTabPolicy;
  valueKind: StableLocationValueKind;
};

export type StableWorkbenchSelection = {
  recordKind: string;
  section: WorkbenchSection;
  subrecordId: string | null;
  value: number | string;
};

export type CreateStableEntityLocationOptions = {
  game: ProjectGame;
  inspectorTab?: WorkbenchLocation['inspectorTab'];
  projectId: string;
  recordKind?: string;
  section: WorkbenchSection;
  subrecordId?: string | null;
  value: number | string;
};

export type WorkbenchLocationApplyHandler = (
  selection: StableWorkbenchSelection,
  location: WorkbenchLocation
) => boolean | Promise<boolean>;

export type WorkbenchLocationApplyHandlers = Partial<
  Record<WorkbenchSection, WorkbenchLocationApplyHandler>
>;

export type ApplyWorkbenchLocationResult =
  | { kind: 'applied'; selection: StableWorkbenchSelection }
  | { kind: 'rejected'; selection: StableWorkbenchSelection }
  | { kind: 'handlerMissing'; selection: StableWorkbenchSelection }
  | { kind: 'unsupported' };

const stableLocationAdapters = [
  adapter('items', 'item', 'integer'),
  adapter('pokemon', 'pokemon-personal', 'integer'),
  adapter('moves', 'move', 'integer'),
  adapter('text', 'text-entry', 'string'),
  adapter('trainers', 'trainer', 'integer'),
  adapter('shops', 'shop', 'string'),
  adapter('encounters', 'encounter-table', 'string'),
  adapter('teraRaids', 'tera-raid', 'string'),
  adapter('raidBattles', 'raid-table', 'string'),
  adapter('raidRewards', 'raid-reward-table', 'string'),
  adapter('raidBonusRewards', 'raid-bonus-reward-table', 'string'),
  adapter('placement', 'placed-object', 'string'),
  adapter('behavior', 'behavior-entry', 'string'),
  adapter('flagworkSave', 'flag', 'string', 'readOnly'),
  adapter('flagworkSave', 'save-block', 'string', 'readOnly'),
  adapter('exefsPatches', 'exefs-check', 'string', 'readOnly'),
  adapter('exefsPatches', 'exefs-patch', 'string'),
  adapter('royalCandy', 'royal-candy-workflow', 'string'),
  adapter('royalCandy', 'royal-candy-check', 'string'),
  adapter('startingItems', 'starting-item-slot', 'integer'),
  adapter('spreadsheetImport', 'import-profile', 'string')
] as const satisfies readonly StableLocationAdapter[];

const adaptersBySection = new Map<WorkbenchSection, readonly StableLocationAdapter[]>();
for (const locationAdapter of stableLocationAdapters) {
  const existing = adaptersBySection.get(locationAdapter.section) ?? [];
  if (existing.some((candidate) => candidate.recordKind === locationAdapter.recordKind)) {
    throw new Error('Stable location adapters must have unique section and record-kind pairs.');
  }
  adaptersBySection.set(locationAdapter.section, [...existing, locationAdapter]);
}

export const stableWorkbenchLocationAdapters: readonly StableLocationAdapter[] =
  stableLocationAdapters;

export function createStableEntityLocation(
  options: CreateStableEntityLocationOptions
): WorkbenchLocation {
  const locationAdapter = resolveAdapterForCreation(
    options.section,
    options.recordKind,
    options.value
  );
  const recordId = serializeStableValue(locationAdapter, options.value);
  const registration = getWorkbenchCapabilityRegistration(options.section);
  const entity: SemanticRecordRef = {
    domain: registration.domain,
    gameFamily: projectGameToFamily(options.game),
    recordId,
    recordKind: {
      key: locationAdapter.recordKind,
      schemaVersion: locationAdapter.recordKindSchemaVersion
    },
    subrecordId: options.subrecordId ?? null
  };

  return createWorkbenchLocation({
    entity,
    game: options.game,
    ...(options.inspectorTab ? { inspectorTab: options.inspectorTab } : {}),
    projectId: options.projectId,
    section: options.section
  });
}

export function parseStableEntitySelection(
  location: WorkbenchLocation
): StableWorkbenchSelection | null {
  if (!location.entity) {
    return null;
  }
  const candidates = adaptersBySection.get(location.section) ?? [];
  const locationAdapter = candidates.find(
    (candidate) =>
      candidate.recordKind === location.entity?.recordKind.key &&
      candidate.recordKindSchemaVersion === location.entity.recordKind.schemaVersion
  );
  if (!locationAdapter) {
    return null;
  }

  const registration = getWorkbenchCapabilityRegistration(location.section);
  if (location.entity.domain !== registration.domain) {
    return null;
  }
  const value = parseStableValue(locationAdapter, location.entity.recordId);
  return value === null
    ? null
    : {
        recordKind: locationAdapter.recordKind,
        section: locationAdapter.section,
        subrecordId: location.entity.subrecordId,
        value
      };
}

export async function applyStableWorkbenchLocation(
  location: WorkbenchLocation,
  handlers: WorkbenchLocationApplyHandlers
): Promise<ApplyWorkbenchLocationResult> {
  const selection = parseStableEntitySelection(location);
  if (!selection) {
    return { kind: 'unsupported' };
  }
  const handler = handlers[selection.section];
  if (!handler) {
    return { kind: 'handlerMissing', selection };
  }
  return (await handler(selection, location))
    ? { kind: 'applied', selection }
    : { kind: 'rejected', selection };
}

export function isStableLocationTabEligible(
  location: WorkbenchLocation,
  draftSafeSections: ReadonlySet<WorkbenchSection>
) {
  const selection = parseStableEntitySelection(location);
  if (!selection) {
    return false;
  }
  const locationAdapter = (adaptersBySection.get(selection.section) ?? []).find(
    (candidate) => candidate.recordKind === selection.recordKind
  );
  return Boolean(
    locationAdapter &&
      (locationAdapter.tabPolicy === 'readOnly' || draftSafeSections.has(selection.section))
  );
}

export function getStableLocationAdapters(section: WorkbenchSection) {
  return adaptersBySection.get(section) ?? [];
}

function adapter(
  section: WorkbenchSection,
  recordKind: string,
  valueKind: StableLocationValueKind,
  tabPolicy: LocationAdapterTabPolicy = 'requiresDraftAdapter'
): StableLocationAdapter {
  const identity = getWorkbenchRecordIdentityRegistration(section);
  if (
    identity.stability === 'notRecordScoped' ||
    identity.stability === 'operationScoped'
  ) {
    throw new Error('A stable location adapter requires a record-scoped identity registration.');
  }
  return {
    identityStability: identity.stability,
    recordKind,
    recordKindSchemaVersion: 1,
    section,
    tabPolicy,
    valueKind
  };
}

function resolveAdapterForCreation(
  section: WorkbenchSection,
  recordKind: string | undefined,
  value: number | string
) {
  const candidates = adaptersBySection.get(section) ?? [];
  const matchingCandidates = candidates.filter(
    (candidate) =>
      (recordKind === undefined || candidate.recordKind === recordKind) &&
      ((candidate.valueKind === 'integer' && typeof value === 'number') ||
        (candidate.valueKind === 'string' && typeof value === 'string'))
  );
  if (matchingCandidates.length !== 1) {
    throw new Error('A stable entity location requires one exact registered adapter.');
  }
  return matchingCandidates[0]!;
}

function serializeStableValue(locationAdapter: StableLocationAdapter, value: number | string) {
  if (locationAdapter.valueKind === 'integer') {
    if (!Number.isSafeInteger(value) || typeof value !== 'number' || value < 0) {
      throw new Error('A stable numeric location value must be a non-negative safe integer.');
    }
    return String(value);
  }
  if (typeof value !== 'string' || value.length === 0 || value.trim() !== value) {
    throw new Error('A stable string location value must be a non-empty trimmed string.');
  }
  return value;
}

function parseStableValue(locationAdapter: StableLocationAdapter, recordId: string) {
  if (locationAdapter.valueKind === 'string') {
    return recordId;
  }
  if (!/^(?:0|[1-9][0-9]*)$/u.test(recordId)) {
    return null;
  }
  const value = Number(recordId);
  return Number.isSafeInteger(value) ? value : null;
}
