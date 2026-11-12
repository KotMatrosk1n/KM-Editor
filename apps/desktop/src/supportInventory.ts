/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke, isTauri } from '@tauri-apps/api/core';
import { projectPathsSchema, type ProjectPaths } from './bridge/contracts';
import { supportChangeInventorySchema } from './bridge/supportInventoryContracts';

let paths: ProjectPaths | null = null;
let generation = 0;

export function observeInventoryScope(payload: unknown): void {
  if (!payload || typeof payload !== 'object') return;
  const source = payload as Record<string, unknown>;
  const scope = source.scope && typeof source.scope === 'object' ? source.scope as Record<string, unknown> : {};
  const result = projectPathsSchema.safeParse(source.paths ?? scope.paths);
  if (result.success && JSON.stringify(result.data) !== JSON.stringify(paths)) {
    // Paths stay in memory solely to locate existing metadata. Never export them.
    paths = result.data;
    generation++;
  }
}

export async function collectSupportInventory() {
  if (!paths) return { status: 'not-configured' };
  if (!isTauri()) return { status: 'unavailable' };
  const current = generation;
  const requestId = `support-inventory-${crypto.randomUUID()}`;
  let timer: ReturnType<typeof setTimeout> | undefined;
  try {
    const result = await Promise.race([
      invoke<string>('project_bridge', { requestJson: JSON.stringify({
        command: 'support.report.build', requestId,
        payload: { scope: { projectId: 'support-report', paths: {
          ...paths, scarletVioletSupportFolderPath: paths.scarletVioletSupportFolderPath ?? null
        } }, includeChanges: true }, compactPokemonWorkflows: true
      }) }),
      new Promise<null>((resolve) => { timer = setTimeout(() => resolve(null), 2500); })
    ]);
    if (generation !== current) return { status: 'scope-changed' };
    if (result === null) return { status: 'timeout' };
    if (typeof result !== 'string' || new TextEncoder().encode(result).length > 256 * 1024) return { status: 'size-limit' };
    const response = JSON.parse(result);
    if (response?.requestId !== requestId || response.error) return { status: 'unavailable' };
    const parsed = supportChangeInventorySchema.safeParse(response.payload?.report?.changes);
    return parsed.success ? { status: 'collected', game: paths?.selectedGame, ...parsed.data } : { status: 'invalid-data' };
  } catch { return { status: 'unavailable' }; }
  finally { clearTimeout(timer); }
}
