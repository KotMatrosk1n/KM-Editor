/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import type { WorkbenchSection } from '../workbenchStore';
import {
  getWorkbenchCapabilityRegistration,
  isRegisteredWorkbenchSection
} from './capabilityRegistry';
import {
  projectGameToFamily,
  validateSemanticRecordRef,
  type JsonPrimitive,
  type SemanticRecordRef
} from './semanticContracts';

export const workbenchLocationVersion = 1 as const;
export const maximumSerializedWorkbenchLocationLength = 32 * 1024;

export type WorkbenchInspectorTab =
  | 'compare'
  | 'references'
  | 'impact'
  | 'history'
  | 'notes'
  | 'provenance';

export type WorkbenchLocation = {
  changeSetId?: string;
  entity?: SemanticRecordRef;
  game: ProjectGame | null;
  inspectorTab?: WorkbenchInspectorTab;
  projectId: string | null;
  section: WorkbenchSection;
  subcontext?: Readonly<Record<string, JsonPrimitive>>;
  version: typeof workbenchLocationVersion;
};

export type WorkbenchLocationTarget = Omit<WorkbenchLocation, 'version'> & {
  version?: typeof workbenchLocationVersion;
};

const maximumContractKeyLength = 128;
const maximumStableIdLength = 1024;
const maximumSubcontextEntries = 32;
const maximumSubcontextStringLength = 4096;
const projectGames = new Set<ProjectGame>(['sword', 'shield', 'scarlet', 'violet', 'za']);
const inspectorTabs = new Set<WorkbenchInspectorTab>([
  'compare',
  'references',
  'impact',
  'history',
  'notes',
  'provenance'
]);

export function createWorkbenchLocation(target: WorkbenchLocationTarget): WorkbenchLocation {
  if (!isRegisteredWorkbenchSection(target.section)) {
    throw new Error('Workbench location section is not registered.');
  }
  const capability = getWorkbenchCapabilityRegistration(target.section);
  if (target.game !== null && !projectGames.has(target.game)) {
    throw new Error('Workbench location game is invalid.');
  }
  if (target.inspectorTab !== undefined && !inspectorTabs.has(target.inspectorTab)) {
    throw new Error('Workbench location inspector tab is invalid.');
  }
  validateStableIdOrNull(target.projectId, 'project id');
  validateStableIdOrUndefined(target.changeSetId, 'change-set id');
  if (target.projectId && !target.game) {
    throw new Error('A project-scoped workbench location requires a game.');
  }
  if (target.changeSetId && (!target.projectId || !target.game)) {
    throw new Error('A change-set workbench location requires a project and game.');
  }
  if (target.entity) {
    if (!target.projectId) {
      throw new Error('A workbench location entity requires a project id.');
    }
    validateSemanticRecordRef(target.entity);
    if (target.entity.domain !== capability.domain) {
      throw new Error('A workbench location entity must belong to the destination domain.');
    }
    if (!target.game || projectGameToFamily(target.game) !== target.entity.gameFamily) {
      throw new Error('A workbench location entity must belong to the location game family.');
    }
  }

  const subcontext = target.subcontext
    ? canonicalizeSubcontext(target.subcontext)
    : undefined;
  return {
    ...target,
    ...(subcontext ? { subcontext } : {}),
    version: workbenchLocationVersion
  };
}

export function createSectionLocation(
  section: WorkbenchSection,
  scope: { game: ProjectGame | null; projectId: string | null }
) {
  return createWorkbenchLocation({
    game: scope.game,
    projectId: scope.projectId,
    section
  });
}

export function workbenchLocationsEqual(
  left: WorkbenchLocation,
  right: WorkbenchLocation
) {
  return serializeWorkbenchLocation(left) === serializeWorkbenchLocation(right);
}

export function serializeWorkbenchLocation(location: WorkbenchLocation) {
  const canonicalLocation = createWorkbenchLocation(location);
  const parameters = new URLSearchParams();
  parameters.set('v', String(canonicalLocation.version));
  parameters.set('section', canonicalLocation.section);
  if (canonicalLocation.projectId) {
    parameters.set('project', canonicalLocation.projectId);
  }
  if (canonicalLocation.game) {
    parameters.set('game', canonicalLocation.game);
  }
  if (canonicalLocation.changeSetId) {
    parameters.set('changeSet', canonicalLocation.changeSetId);
  }
  if (canonicalLocation.inspectorTab) {
    parameters.set('inspector', canonicalLocation.inspectorTab);
  }
  if (canonicalLocation.entity) {
    parameters.set('entity', JSON.stringify(canonicalSemanticRecord(canonicalLocation.entity)));
  }
  if (canonicalLocation.subcontext && Object.keys(canonicalLocation.subcontext).length > 0) {
    parameters.set('context', JSON.stringify(canonicalLocation.subcontext));
  }

  const serializedLocation = parameters.toString();
  if (serializedLocation.length > maximumSerializedWorkbenchLocationLength) {
    throw new Error('Workbench location exceeds the supported serialized length.');
  }
  return serializedLocation;
}

export function parseWorkbenchLocation(value: string): WorkbenchLocation | null {
  if (value.length === 0 || value.length > maximumSerializedWorkbenchLocationLength) {
    return null;
  }

  try {
    const query = extractLocationQuery(value);
    const parameters = new URLSearchParams(query);
    if (hasDuplicateRecognizedParameter(parameters)) {
      return null;
    }
    if (parameters.get('v') !== String(workbenchLocationVersion)) {
      return null;
    }

    const section = parameters.get('section');
    const gameValue = parameters.get('game');
    const inspectorValue = parameters.get('inspector');
    if (
      !section ||
      !isRegisteredWorkbenchSection(section) ||
      (gameValue !== null && !projectGames.has(gameValue as ProjectGame)) ||
      (inspectorValue !== null && !inspectorTabs.has(inspectorValue as WorkbenchInspectorTab))
    ) {
      return null;
    }

    const game = gameValue as ProjectGame | null;
    const entity = parseSemanticRecordRef(parameters.get('entity'));
    if (parameters.has('entity') && !entity) {
      return null;
    }
    if (entity && (!game || projectGameToFamily(game) !== entity.gameFamily)) {
      return null;
    }

    const subcontext = parseSubcontext(parameters.get('context'));
    if (parameters.has('context') && !subcontext) {
      return null;
    }

    const changeSetValue = parameters.get('changeSet');
    const projectValue = parameters.get('project');
    if (
      (parameters.has('changeSet') && !changeSetValue) ||
      (parameters.has('project') && !projectValue) ||
      (changeSetValue !== null && changeSetValue.trim() !== changeSetValue) ||
      (projectValue !== null && projectValue.trim() !== projectValue)
    ) {
      return null;
    }
    const changeSetId = changeSetValue || undefined;
    const projectId = projectValue || null;

    return createWorkbenchLocation({
      ...(changeSetId ? { changeSetId } : {}),
      ...(entity ? { entity } : {}),
      game,
      ...(inspectorValue ? { inspectorTab: inspectorValue as WorkbenchInspectorTab } : {}),
      projectId,
      section,
      ...(subcontext ? { subcontext } : {})
    });
  } catch {
    return null;
  }
}

function extractLocationQuery(value: string) {
  const questionMarkIndex = value.indexOf('?');
  return (questionMarkIndex >= 0 ? value.slice(questionMarkIndex + 1) : value).replace(/^\?/, '');
}

function hasDuplicateRecognizedParameter(parameters: URLSearchParams) {
  const recognizedParameters = [
    'v',
    'section',
    'project',
    'game',
    'changeSet',
    'inspector',
    'entity',
    'context'
  ] as const;
  return recognizedParameters.some((name) => parameters.getAll(name).length > 1);
}

function parseSemanticRecordRef(value: string | null): SemanticRecordRef | undefined {
  if (value === null || value.length > maximumSerializedWorkbenchLocationLength) {
    return undefined;
  }

  const candidate = JSON.parse(value) as unknown;
  if (!isPlainObject(candidate) || !isPlainObject(candidate.recordKind)) {
    return undefined;
  }

  const record: SemanticRecordRef = {
    domain: typeof candidate.domain === 'string' ? candidate.domain : '',
    gameFamily:
      candidate.gameFamily === 'swordShield' ||
      candidate.gameFamily === 'scarletViolet' ||
      candidate.gameFamily === 'legendsZA'
        ? candidate.gameFamily
        : 'swordShield',
    recordId: typeof candidate.recordId === 'string' ? candidate.recordId : '',
    recordKind: {
      key: typeof candidate.recordKind.key === 'string' ? candidate.recordKind.key : '',
      schemaVersion:
        typeof candidate.recordKind.schemaVersion === 'number'
          ? candidate.recordKind.schemaVersion
          : 0
    },
    subrecordId:
      candidate.subrecordId === null || typeof candidate.subrecordId === 'string'
        ? candidate.subrecordId
        : null
  };

  try {
    validateSemanticRecordRef(record);
  } catch {
    return undefined;
  }
  if (candidate.subrecordId !== record.subrecordId || candidate.gameFamily !== record.gameFamily) {
    return undefined;
  }
  return record;
}

function parseSubcontext(value: string | null) {
  if (value === null || value.length > maximumSerializedWorkbenchLocationLength) {
    return undefined;
  }

  const candidate = JSON.parse(value) as unknown;
  if (!isPlainObject(candidate)) {
    return undefined;
  }

  try {
    return canonicalizeSubcontext(candidate);
  } catch {
    return undefined;
  }
}

function canonicalizeSubcontext(value: Readonly<Record<string, unknown>>) {
  const entries = Object.entries(value).sort(([left], [right]) =>
    left < right ? -1 : left > right ? 1 : 0
  );
  if (entries.length > maximumSubcontextEntries) {
    throw new Error('Workbench location subcontext has too many entries.');
  }

  for (const [key, entryValue] of entries) {
    validateContractKey(key, 'subcontext key');
    if (!isJsonPrimitive(entryValue)) {
      throw new Error('Workbench location subcontext values must be JSON primitives.');
    }
    if (typeof entryValue === 'string' && entryValue.length > maximumSubcontextStringLength) {
      throw new Error('Workbench location subcontext strings are too long.');
    }
  }

  return Object.fromEntries(entries) as Readonly<Record<string, JsonPrimitive>>;
}

function canonicalSemanticRecord(record: SemanticRecordRef): SemanticRecordRef {
  return {
    domain: record.domain,
    gameFamily: record.gameFamily,
    recordId: record.recordId,
    recordKind: {
      key: record.recordKind.key,
      schemaVersion: record.recordKind.schemaVersion
    },
    subrecordId: record.subrecordId
  };
}

function validateContractKey(value: string, label: string) {
  if (
    value.length === 0 ||
    value.length > maximumContractKeyLength ||
    !/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u.test(value)
  ) {
    throw new Error(`${label} is not a valid semantic contract key.`);
  }
}

function validateStableIdOrNull(value: string | null, label: string) {
  if (value !== null) {
    validateStableId(value, label);
  }
}

function validateStableIdOrUndefined(value: string | undefined, label: string) {
  if (value !== undefined) {
    validateStableId(value, label);
  }
}

function validateStableId(value: string, label: string) {
  if (
    value.length === 0 ||
    value.length > maximumStableIdLength ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f-\u009f]/u.test(value)
  ) {
    throw new Error(`${label} is not a valid bounded semantic id.`);
  }
}

function isJsonPrimitive(value: unknown): value is JsonPrimitive {
  return (
    value === null ||
    typeof value === 'boolean' ||
    typeof value === 'string' ||
    (typeof value === 'number' && Number.isFinite(value))
  );
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    Object.getPrototypeOf(value) === Object.prototype
  );
}
