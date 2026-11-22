// SPDX-License-Identifier: GPL-3.0-only
import { invoke } from '@tauri-apps/api/core';
import { z } from 'zod';

const legacyStorageKey = 'km-editor.mod-merger.draft.v1';
const maxDraftLength = 512_000;
const game = z.enum(['sword', 'shield', 'scarlet', 'violet', 'za']);
const path = z.string().max(4096);
const schema = z.object({
  mode: z.enum(['basic', 'advanced']), game: game.or(z.literal('')),
  outputMode: z.enum(['standalone', 'trinity', 'bypass']), outputRoot: path,
  baseRomFs: path, baseExeFs: path, supportFolder: path,
  sources: z.array(z.object({ id: z.string().max(80), path, game: game.nullable(),
    layout: z.enum(['standalone', 'trinity', 'bypass', 'independent']).nullable(),
    packageIds: z.array(z.string().max(80)).max(64).nullable().optional(), packageScanToken: z.string().max(80).nullable().optional(),
    packageCount: z.number().int().min(1).max(64).optional() })).max(64),
  choices: z.record(z.string().max(80), z.string().max(80))
});
export type MergeDraft = z.infer<typeof schema>;
export type MergeDraftState = { draft: MergeDraft | null; available: boolean };

let pending: Promise<unknown> = Promise.resolve();

function inOrder<T>(operation: () => Promise<T>): Promise<T> {
  const result = pending.then(operation, operation);
  pending = result;
  return result;
}

function parseDraft(stored: string | null): MergeDraft | null {
  if (!stored || stored.length > maxDraftLength) return null;
  try {
    const parsed = schema.safeParse(JSON.parse(stored));
    return parsed.success ? parsed.data : null;
  } catch { return null; }
}

function readLegacyDraft(): string | null {
  try { return localStorage.getItem(legacyStorageKey); }
  catch { return null; }
}

function removeLegacyDraft(): boolean {
  try {
    if (localStorage.getItem(legacyStorageKey) !== null) localStorage.removeItem(legacyStorageKey);
    return true;
  } catch { return false; }
}

export function readMergeDraft(): Promise<MergeDraftState> {
  return inOrder(async () => {
    const legacy = readLegacyDraft();
    const legacyDraft = parseDraft(legacy);
    let available = true;
    try {
      const stored = await invoke<string | null>('load_mod_merge_draft');
      const draft = parseDraft(stored);
      if (draft) return { draft, available: legacy === null || removeLegacyDraft() };
      available = stored === null;
    } catch { available = false; }

    if (legacyDraft) {
      // Keep the recoverable draft until Windows confirms the protected write.
      return { draft: legacyDraft, available: await writeDraft(legacyDraft) };
    }
    return { draft: null, available: (legacy === null || removeLegacyDraft()) && available };
  });
}

export function saveMergeDraft(draft: unknown): Promise<boolean> {
  return inOrder(() => writeDraft(draft));
}

async function writeDraft(draft: unknown): Promise<boolean> {
  try {
    const parsed = schema.safeParse(draft);
    if (!parsed.success) return false;
    const stored = JSON.stringify(parsed.data);
    if (stored.length > maxDraftLength) return false;
    await invoke('save_mod_merge_draft', { draft: stored });
    return readLegacyDraft() === null || removeLegacyDraft();
  } catch { return false; }
}
