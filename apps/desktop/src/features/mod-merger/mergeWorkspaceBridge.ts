// SPDX-License-Identifier: GPL-3.0-only
import { invoke } from '@tauri-apps/api/core';
import { z } from 'zod';
import { kmCommandNames } from '../../bridge/contracts';
import { sendProjectBridgeRequest } from '../../bridge/projectBridgeRequest';

export type MergeSource = { id: string; path: string; game: string | null; layout: string | null;
  packageIds?: string[] | null; packageScanToken?: string | null; packageCount?: number };
export type MergeRequest = {
  mode: 'basic' | 'advanced'; game: string | null; outputMode: 'standalone' | 'trinity' | 'bypass'; outputRoot: string;
  sources: MergeSource[]; choices: { conflictId: string; sourceId: string }[];
  baseRomFs: string | null; baseExeFs: string | null; supportFolder: string | null; reviewToken?: string | null;
};
const issue = z.object({ code: z.string(), severity: z.enum(['error', 'warning', 'info']), message: z.string(), file: z.string().nullable() });
export const mergeResultSchema = z.object({
  projectId: z.string().nullable().optional(),
  game: z.string().nullable(), mode: z.string(), outputMode: z.string(), reviewToken: z.string(), canExport: z.boolean(),
  sources: z.array(z.object({ id: z.string(), name: z.string(), game: z.string().nullable(), layout: z.string(), fileCount: z.number(), evidence: z.array(z.string()), parentId: z.string().nullable().optional() })),
  files: z.array(z.object({ path: z.string(), kind: z.string(), status: z.string(), conflictCount: z.number(), size: z.number() })),
  conflicts: z.array(z.object({ id: z.string(), file: z.string(), label: z.string(), kind: z.string(), original: z.string().nullable(), resolution: z.string().nullable(),
    values: z.array(z.object({ sourceId: z.string(), sourceName: z.string(), value: z.string() })) })),
  issues: z.array(issue), writtenFiles: z.array(z.string())
});
export type MergeResult = z.infer<typeof mergeResultSchema>;
export const mergePackageCatalogSchema = z.object({
  sourceId: z.string(), name: z.string(), scanToken: z.string(), requiresSelection: z.boolean(),
  packages: z.array(z.object({ id: z.string(), name: z.string(), root: z.string(), game: z.string().nullable(), layout: z.string(),
    fileCount: z.number(), size: z.number(), required: z.boolean(), recommended: z.boolean(), group: z.string().nullable(),
    requires: z.array(z.string()), description: z.string().nullable(), evidence: z.array(z.string()), files: z.array(z.string()), overlapCount: z.number() })),
  groups: z.array(z.object({ id: z.string(), name: z.string(), required: z.boolean() })),
  documents: z.array(z.object({ path: z.string(), text: z.string(), truncated: z.boolean() }))
});
export type MergePackageCatalog = z.infer<typeof mergePackageCatalogSchema>;
export type MergePackage = MergePackageCatalog['packages'][number];
export function scanMergePackages(source: MergeSource): Promise<MergePackageCatalog> {
  return sendProjectBridgeRequest(json => invoke<string>('project_bridge', { requestJson: json }),
    kmCommandNames.scanMergePackages, { source: sourcePayload(source) }, mergePackageCatalogSchema);
}
function sourcePayload({ packageCount: _count, ...source }: MergeSource) { return source; }
export function runMerge(request: MergeRequest, exportFiles = false): Promise<MergeResult> {
  return sendProjectBridgeRequest(json => invoke<string>('project_bridge', { requestJson: json }),
    exportFiles ? kmCommandNames.exportMergeWorkspace : kmCommandNames.analyzeMergeWorkspace,
    { ...request, sources: request.sources.map(sourcePayload) }, mergeResultSchema);
}
