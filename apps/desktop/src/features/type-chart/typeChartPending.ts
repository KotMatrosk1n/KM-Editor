/* SPDX-License-Identifier: GPL-3.0-only */

import {
  type EditSession,
  type ProjectGame,
  type TypeChartWorkflow
} from '../../bridge/contracts';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';

export type TypeChartEffectivenessValue =
  TypeChartWorkflow['cells'][number]['effectiveness'];

export type CanonicalTypeChartPendingState =
  | {
      kind: 'chart';
      values: TypeChartEffectivenessValue[];
    }
  | {
      kind: 'uninstall';
    };

const typeChartMainPath = 'exefs/main';
const typeChartValueCount = 18 * 18;

const chartRecordIdByGame: Record<ProjectGame, string> = {
  scarlet: 'sv-type-chart',
  shield: 'type-chart',
  sword: 'type-chart',
  violet: 'sv-type-chart',
  za: 'za-type-chart'
};

const uninstallRecordIdByGame: Partial<Record<ProjectGame, string>> = {
  scarlet: 'sv-type-chart-v1-uninstall',
  violet: 'sv-type-chart-v1-uninstall',
  za: 'za-type-chart-v1-uninstall'
};

export function getCanonicalTypeChartPendingState(
  editSession: EditSession | null,
  detectedGame: ProjectGame | null | undefined
): CanonicalTypeChartPendingState | null {
  if (!detectedGame || editSession?.pendingEdits.length !== 1) {
    return null;
  }

  const edit = editSession.pendingEdits[0];
  if (edit?.domain !== 'workflow.typeChart') {
    return null;
  }

  if (edit.recordId === chartRecordIdByGame[detectedGame]) {
    const values = decodeTypeChartPendingValues(edit.newValue);
    if (
      edit.field !== 'effectiveness' ||
      values === null ||
      edit.newValue !== encodeTypeChartPendingValues(values) ||
      edit.summary !== 'Stage Type Chart effectiveness table.' ||
      !hasCanonicalChartSources(detectedGame, edit.sources, edit.newValue)
    ) {
      return null;
    }

    return { kind: 'chart', values };
  }

  const uninstallRecordId = uninstallRecordIdByGame[detectedGame];
  if (
    uninstallRecordId &&
    edit.recordId === uninstallRecordId &&
    edit.field === 'uninstall' &&
    edit.newValue === 'true' &&
    edit.summary === 'Stage Type Chart uninstall.' &&
    hasCanonicalUninstallSources(edit.sources)
  ) {
    return { kind: 'uninstall' };
  }

  return null;
}

export function encodeTypeChartPendingValues(
  values: readonly TypeChartEffectivenessValue[]
) {
  return values
    .map((value) => value.toString(16).padStart(2, '0'))
    .join('')
    .toUpperCase();
}

export function decodeTypeChartPendingValues(
  value: string | null | undefined
): TypeChartEffectivenessValue[] | null {
  if (!value || value.length !== typeChartValueCount * 2) {
    return null;
  }

  const values: TypeChartEffectivenessValue[] = [];
  for (let index = 0; index < value.length; index += 2) {
    const parsed = Number.parseInt(value.slice(index, index + 2), 16);
    if (!isTypeChartEffectivenessValue(parsed)) {
      return null;
    }

    values.push(parsed);
  }

  return values.length === typeChartValueCount ? values : null;
}

export const calculateTypeChartPayloadSha256 = calculatePendingPayloadSha256;

function hasCanonicalChartSources(
  detectedGame: ProjectGame,
  sources: EditSession['pendingEdits'][number]['sources'],
  payload: string
) {
  if (detectedGame === 'sword' || detectedGame === 'shield') {
    if (sources.length !== 2 && sources.length !== 3) {
      return false;
    }

    const baseSource = sources[0];
    const layeredSource = sources.length === 3 ? sources[1] : null;
    const pendingSource = sources.at(-1);
    return (
      baseSource?.layer === 'base' &&
      baseSource.relativePath === typeChartMainPath &&
      (layeredSource === null ||
        (layeredSource?.layer === 'layered' &&
          layeredSource.relativePath === typeChartMainPath)) &&
      pendingSource?.layer === 'pending' &&
      pendingSource.relativePath ===
        `pending/type-chart/effectiveness/${calculateTypeChartPayloadSha256(payload)}`
    );
  }

  return (
    sources.length === 1 &&
    (sources[0]?.layer === 'base' || sources[0]?.layer === 'layered') &&
    sources[0].relativePath === typeChartMainPath
  );
}

function hasCanonicalUninstallSources(
  sources: EditSession['pendingEdits'][number]['sources']
) {
  return (
    sources.length === 2 &&
    sources[0]?.layer === 'generated' &&
    sources[0].relativePath === typeChartMainPath &&
    sources[1]?.layer === 'base' &&
    sources[1].relativePath === typeChartMainPath
  );
}

function isTypeChartEffectivenessValue(
  value: number
): value is TypeChartEffectivenessValue {
  return value === 0 || value === 2 || value === 4 || value === 8;
}
