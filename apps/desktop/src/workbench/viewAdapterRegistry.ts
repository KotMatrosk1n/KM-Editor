/* SPDX-License-Identifier: GPL-3.0-only */

import type { JsonValue } from './semanticContracts';
import type { WorkbenchSection } from './workbenchSections';

export const maximumWorkspaceViewSearchLength = 512;
export const maximumWorkspaceViewAdapters = 32;

export type SearchOnlyWorkspaceViewState = {
  searchText: string;
};

export type SearchOnlyWorkspaceViewPayload = {
  searchText: string;
};

export type WorkspaceViewAdapter = {
  adapterId: string;
  adapterSchemaVersion: 1;
  section: WorkbenchSection;
};

export type CapturedWorkspaceView = {
  adapterId: string;
  adapterSchemaVersion: number;
  payload: JsonValue;
};

const searchOnlyViewAdapters = [
  searchAdapter('items'),
  searchAdapter('pokemon'),
  searchAdapter('moves'),
  searchAdapter('text'),
  searchAdapter('trainers'),
  searchAdapter('shops'),
  searchAdapter('encounters'),
  searchAdapter('teraRaids'),
  searchAdapter('raidBattles'),
  searchAdapter('raidRewards'),
  searchAdapter('raidBonusRewards'),
  searchAdapter('placement'),
  searchAdapter('behavior'),
  searchAdapter('flagworkSave'),
  searchAdapter('exefsPatches'),
  searchAdapter('royalCandy')
] as const satisfies readonly WorkspaceViewAdapter[];

const adapterBySection = new Map<WorkbenchSection, WorkspaceViewAdapter>();
const adapterById = new Map<string, WorkspaceViewAdapter>();
if (searchOnlyViewAdapters.length > maximumWorkspaceViewAdapters) {
  throw new Error('The workspace view adapter registry exceeds its bounded capacity.');
}
for (const adapter of searchOnlyViewAdapters) {
  if (adapterBySection.has(adapter.section) || adapterById.has(adapter.adapterId)) {
    throw new Error('Workspace view adapters must have unique section and adapter ids.');
  }
  adapterBySection.set(adapter.section, adapter);
  adapterById.set(adapter.adapterId, adapter);
}

export const workspaceViewAdapters: readonly WorkspaceViewAdapter[] =
  searchOnlyViewAdapters;

export function getWorkspaceViewAdapter(section: WorkbenchSection) {
  return adapterBySection.get(section) ?? null;
}

export function hasWorkspaceViewAdapter(section: WorkbenchSection) {
  return adapterBySection.has(section);
}

export function captureWorkspaceView(
  section: WorkbenchSection,
  state: SearchOnlyWorkspaceViewState
): CapturedWorkspaceView | null {
  const adapter = getWorkspaceViewAdapter(section);
  if (!adapter || !isValidSearchText(state.searchText)) {
    return null;
  }
  return {
    adapterId: adapter.adapterId,
    adapterSchemaVersion: adapter.adapterSchemaVersion,
    payload: { searchText: state.searchText }
  };
}

export function validateWorkspaceViewPayload(
  adapterId: string,
  adapterSchemaVersion: number,
  payload: unknown
): payload is SearchOnlyWorkspaceViewPayload {
  const adapter = adapterById.get(adapterId);
  return Boolean(
    adapter &&
      adapter.adapterSchemaVersion === adapterSchemaVersion &&
      isPlainObject(payload) &&
      Object.keys(payload).length === 1 &&
      isValidSearchText(payload.searchText)
  );
}

export function applyWorkspaceView(
  section: WorkbenchSection,
  captured: CapturedWorkspaceView
): SearchOnlyWorkspaceViewState | null {
  const adapter = getWorkspaceViewAdapter(section);
  if (
    !adapter ||
    adapter.adapterId !== captured.adapterId ||
    adapter.adapterSchemaVersion !== captured.adapterSchemaVersion ||
    !validateWorkspaceViewPayload(
      captured.adapterId,
      captured.adapterSchemaVersion,
      captured.payload
    )
  ) {
    return null;
  }
  return { searchText: captured.payload.searchText };
}

function searchAdapter(section: WorkbenchSection): WorkspaceViewAdapter {
  const segment = section
    .replace(/([a-z0-9])([A-Z])/gu, '$1-$2')
    .toLowerCase();
  return {
    adapterId: `workspace-view.${segment}.search`,
    adapterSchemaVersion: 1,
    section
  };
}

function isValidSearchText(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= maximumWorkspaceViewSearchLength &&
    !/[\u0000-\u001f\u007f-\u009f]/u.test(value)
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
