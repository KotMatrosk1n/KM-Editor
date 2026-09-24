// SPDX-License-Identifier: GPL-3.0-only
import { z } from 'zod';

const storageKey = 'km-editor.mod-merger.draft.v1';
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

export function readMergeDraft(): MergeDraft | null {
  try {
    const stored = localStorage.getItem(storageKey);
    if (!stored || stored.length > 512_000) return null;
    const parsed = schema.safeParse(JSON.parse(stored));
    return parsed.success ? parsed.data : null;
  } catch { return null; }
}

export function saveMergeDraft(draft: unknown): boolean {
  try {
    const parsed = schema.safeParse(draft);
    if (!parsed.success) return false;
    const stored = JSON.stringify(parsed.data);
    if (stored.length > 512_000) return false;
    localStorage.setItem(storageKey, stored);
    return true;
  } catch { return false; }
}
