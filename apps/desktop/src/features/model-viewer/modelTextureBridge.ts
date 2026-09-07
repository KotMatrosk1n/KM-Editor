/* SPDX-License-Identifier: GPL-3.0-only */
import { invoke } from '@tauri-apps/api/core';
import { z } from 'zod';
import { editSessionSchema, kmCommandNames, type EditSession, type ProjectPaths } from '../../bridge/contracts';
import { sendProjectBridgeRequest } from '../../bridge/projectBridgeRequest';

const hex = z.string().regex(/^#[0-9a-f]{6}$/i);
export const textureRuleSchema = z.object({ from: hex, to: hex, tolerance: z.number().int().min(0).max(100) });
export type TextureRule = z.infer<typeof textureRuleSchema>;
export type TextureChange = { texture: string; sourceHash: string; changes: TextureRule[] };
export type MaterialChange = { key: string; values: number[]; text?: string | null };
export type AssetChange = { asset: string; sourceHash: string; changes: MaterialChange[]; restore?: boolean };
const materialFieldSchema = z.object({
  key: z.string(), material: z.string(), group: z.string(), name: z.string(), kind: z.string(),
  values: z.array(z.number()).max(4), text: z.string().nullable(), options: z.array(z.string()).max(256), editable: z.boolean(), previewed: z.boolean()
});
export const modelPropertiesSchema = z.object({
  assets: z.array(z.object({ id: z.string(), size: z.number(), sourceHash: z.string(), archive: z.string().nullable() })).max(2048),
  materials: z.array(z.object({ id: z.string(), sourceHash: z.string(), fields: z.array(materialFieldSchema).max(16384) })).max(256)
});
export type ModelProperties = z.infer<typeof modelPropertiesSchema>;
export type MaterialField = z.infer<typeof materialFieldSchema>;
export const modelTexturesSchema = z.array(z.object({
  editable: z.boolean().default(true),
  id: z.string().max(1024), materials: z.array(z.string()).max(256), sourceHash: z.string().regex(/^[0-9a-f]{64}$/i),
  width: z.number().int().min(1).max(4096), height: z.number().int().min(1).max(4096), mipCount: z.number().int().min(1).max(13), format: z.string(),
  thumbnailWidth: z.number().int().min(1).max(256), thumbnailHeight: z.number().int().min(1).max(256),
  pixels: z.string().max(350000), colors: z.array(hex).max(16)
})).max(128);
export type ModelTexture = z.infer<typeof modelTexturesSchema>[number];
const transport = (requestJson: string) => invoke<string>('project_bridge', { requestJson });
export function loadModelTextures(paths: ProjectPaths, id: string) {
  return sendProjectBridgeRequest(transport, kmCommandNames.modelTextures, { paths, id }, modelTexturesSchema);
}
export function loadTextureImage(paths: ProjectPaths, id: string, texture: ModelTexture, changes: TextureRule[]) {
  return sendProjectBridgeRequest(transport, kmCommandNames.modelTextures,
    { paths, id, texture: texture.id, sourceHash: texture.sourceHash, changes },
    z.object({ width: z.number().int().min(1).max(4096), height: z.number().int().min(1).max(4096), sourceHash: z.string(), pixels: z.string().max(90_000_000) }));
}
export function loadModelProperties(paths: ProjectPaths, id: string) {
  return sendProjectBridgeRequest(transport, kmCommandNames.modelProperties, { paths, id }, modelPropertiesSchema);
}
export function stageModelAsset(paths: ProjectPaths, id: string, change: AssetChange | null, session: EditSession | null, restoreVanilla = false) {
  return sendProjectBridgeRequest(transport, kmCommandNames.modelAssetStage, { paths, id, change, session, restoreVanilla }, z.object({ session: editSessionSchema }));
}
export function stagedAssetChanges(session: EditSession | null, model: string): AssetChange[] {
  return (session?.pendingEdits ?? []).flatMap(edit => {
    if (edit.domain !== 'workflow.modelTextures') return [];
    try {
      const value = z.object({
        Model: z.string().max(1024), Kind: z.enum(['material', 'restore']), Texture: z.string().max(1024),
        SourceHash: z.string().regex(/^[0-9a-f]{64}$/i),
        MaterialChanges: z.array(z.object({ Key: z.string().max(128), Values: z.array(z.number()).max(4), Text: z.string().max(4096).nullish() })).max(512).nullish()
      }).parse(JSON.parse(edit.newValue ?? ''));
      if (value.Model !== model || value.Texture !== edit.recordId) return [];
      return [{ asset: value.Texture, sourceHash: value.SourceHash, restore: value.Kind === 'restore',
        changes: (value.MaterialChanges ?? []).map(c => ({ key: c.Key, values: c.Values, text: c.Text })) }];
    } catch { return []; }
  });
}
export function stageModelTexture(paths: ProjectPaths, id: string, change: TextureChange, session: EditSession | null) {
  return sendProjectBridgeRequest(transport, kmCommandNames.modelTextureStage, { paths, id, change, session }, z.object({ session: editSessionSchema }));
}
export function stagedTextureChanges(session: EditSession | null, ids: string[]): TextureChange[] {
  return (session?.pendingEdits ?? []).flatMap(edit => {
    if (edit.domain !== 'workflow.modelTextures' || !edit.recordId || !ids.includes(edit.recordId)) return [];
    try {
      const value = JSON.parse(edit.newValue ?? '');
      if (value.Kind && value.Kind !== 'texture') return [];
      const changes = z.array(z.object({ From: hex, To: hex, Tolerance: z.number().int().min(0).max(100) })).max(32).parse(value.Changes);
      return [{ texture: edit.recordId, sourceHash: z.string().regex(/^[0-9a-f]{64}$/i).parse(value.SourceHash),
        changes: changes.map(rule => ({ from: rule.From, to: rule.To, tolerance: rule.Tolerance })) }];
    } catch { return []; }
  });
}
