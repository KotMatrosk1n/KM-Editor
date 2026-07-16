/* SPDX-License-Identifier: GPL-3.0-only */

import {
  type EditSession,
  type ProjectGame,
  type TypeChartWorkflow
} from '../../bridge/contracts';

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
const typeChartSha256RoundConstants = [
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
  0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
  0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
  0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
  0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
  0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
  0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
  0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
  0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
] as const;

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

export function calculateTypeChartPayloadSha256(payload: string) {
  const input = new TextEncoder().encode(payload);
  const paddedLength = Math.ceil((input.length + 9) / 64) * 64;
  const padded = new Uint8Array(paddedLength);
  padded.set(input);
  padded[input.length] = 0x80;
  const paddedView = new DataView(padded.buffer);
  const bitLength = input.length * 8;
  paddedView.setUint32(paddedLength - 8, Math.floor(bitLength / 0x100000000));
  paddedView.setUint32(paddedLength - 4, bitLength >>> 0);

  const hash = new Uint32Array([
    0x6a09e667,
    0xbb67ae85,
    0x3c6ef372,
    0xa54ff53a,
    0x510e527f,
    0x9b05688c,
    0x1f83d9ab,
    0x5be0cd19
  ]);
  const words = new Uint32Array(64);

  for (let blockOffset = 0; blockOffset < paddedLength; blockOffset += 64) {
    for (let index = 0; index < 16; index += 1) {
      words[index] = paddedView.getUint32(blockOffset + index * 4);
    }
    for (let index = 16; index < words.length; index += 1) {
      const previous15 = words[index - 15]!;
      const previous2 = words[index - 2]!;
      const sigma0 =
        rotateRight(previous15, 7) ^
        rotateRight(previous15, 18) ^
        (previous15 >>> 3);
      const sigma1 =
        rotateRight(previous2, 17) ^
        rotateRight(previous2, 19) ^
        (previous2 >>> 10);
      words[index] =
        (words[index - 16]! + sigma0 + words[index - 7]! + sigma1) >>> 0;
    }

    let a = hash[0]!;
    let b = hash[1]!;
    let c = hash[2]!;
    let d = hash[3]!;
    let e = hash[4]!;
    let f = hash[5]!;
    let g = hash[6]!;
    let h = hash[7]!;

    for (let index = 0; index < words.length; index += 1) {
      const sum1 = rotateRight(e, 6) ^ rotateRight(e, 11) ^ rotateRight(e, 25);
      const choose = (e & f) ^ (~e & g);
      const temporary1 =
        (h + sum1 + choose + typeChartSha256RoundConstants[index]! + words[index]!) >>>
        0;
      const sum0 = rotateRight(a, 2) ^ rotateRight(a, 13) ^ rotateRight(a, 22);
      const majority = (a & b) ^ (a & c) ^ (b & c);
      const temporary2 = (sum0 + majority) >>> 0;

      h = g;
      g = f;
      f = e;
      e = (d + temporary1) >>> 0;
      d = c;
      c = b;
      b = a;
      a = (temporary1 + temporary2) >>> 0;
    }

    hash[0] = (hash[0]! + a) >>> 0;
    hash[1] = (hash[1]! + b) >>> 0;
    hash[2] = (hash[2]! + c) >>> 0;
    hash[3] = (hash[3]! + d) >>> 0;
    hash[4] = (hash[4]! + e) >>> 0;
    hash[5] = (hash[5]! + f) >>> 0;
    hash[6] = (hash[6]! + g) >>> 0;
    hash[7] = (hash[7]! + h) >>> 0;
  }

  return Array.from(hash, (word) => word.toString(16).padStart(8, '0'))
    .join('')
    .toUpperCase();
}

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

function rotateRight(value: number, shift: number) {
  return (value >>> shift) | (value << (32 - shift));
}
