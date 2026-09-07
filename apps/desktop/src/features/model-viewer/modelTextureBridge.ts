/* SPDX-License-Identifier: GPL-3.0-only */
import { invoke } from '@tauri-apps/api/core';
import { z } from 'zod';
import { editSessionSchema, kmCommandNames, type EditSession, type ProjectPaths } from '../../bridge/contracts';
import { sendProjectBridgeRequest } from '../../bridge/projectBridgeRequest';

const hex = z.string().regex(/^#[0-9a-f]{6}$/i);
export const textureRuleSchema = z.object({ from: hex, to: hex, tolerance: z.number().int().min(0).max(100) });
export type TextureRule = z.infer<typeof textureRuleSchema>;
export type TextureChange = { texture: string; sourceHash: string; changes: TextureRule[] };
export const modelTexturesSchema = z.array(z.object({
  editable: z.boolean().default(true),
  id: z.string().max(1024), materials: z.array(z.string()).max(256), sourceHash: z.string().regex(/^[0-9a-f]{64}$/i),
  width: z.number().int().min(1).max(4096), height: z.number().int().min(1).max(4096), mipCount: z.number().int().min(1).max(13), format: z.string(),
  thumbnailWidth: z.number().int().min(1).max(256), thumbnailHeight: z.number().int().min(1).max(256),
  pixels: z.string().max(350000), colors: z.array(hex).max(16)
})).max(32);
export type ModelTexture = z.infer<typeof modelTexturesSchema>[number];
const transport = (requestJson: string) => invoke<string>('project_bridge', { requestJson });
export function loadModelTextures(paths: ProjectPaths, id: string) {
  return sendProjectBridgeRequest(transport, kmCommandNames.modelTextures, { paths, id }, modelTexturesSchema);
}
export function stageModelTexture(paths: ProjectPaths, id: string, change: TextureChange, session: EditSession | null) {
  return sendProjectBridgeRequest(transport, kmCommandNames.modelTextureStage, { paths, id, change, session }, z.object({ session: editSessionSchema }));
}
export function stagedTextureChanges(session: EditSession | null, ids: string[]): TextureChange[] {
  return (session?.pendingEdits ?? []).flatMap(edit => {
    if (edit.domain !== 'workflow.modelTextures' || !edit.recordId || !ids.includes(edit.recordId)) return [];
    try {
      const value = JSON.parse(edit.newValue ?? '');
      const changes = z.array(z.object({ From: hex, To: hex, Tolerance: z.number().int().min(0).max(100) })).max(32).parse(value.Changes);
      return [{ texture: edit.recordId, sourceHash: z.string().regex(/^[0-9a-f]{64}$/i).parse(value.SourceHash),
        changes: changes.map(rule => ({ from: rule.From, to: rule.To, tolerance: rule.Tolerance })) }];
    } catch { return []; }
  });
}
