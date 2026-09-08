// SPDX-License-Identifier: GPL-3.0-only
import { invoke } from '@tauri-apps/api/core';
import { z } from 'zod';
import { kmCommandNames, type ProjectPaths } from '../../bridge/contracts';
import { sendProjectBridgeRequest } from '../../bridge/projectBridgeRequest';

export const soundEntrySchema = z.object({ id: z.number().int().nonnegative(), identifier: z.string(), name: z.string(), kind: z.string(), bank: z.string(),
  codec: z.string(), channels: z.number().int(), sampleRate: z.number().int(), status: z.string(), size: z.number().nonnegative(), category: z.string() });
export type SoundEntry = z.infer<typeof soundEntrySchema>;
const statusSchema = z.object({ done: z.boolean(), completed: z.number(), total: z.number(), count: z.number(), error: z.string().nullable() });
export function soundRequest<T extends z.ZodType>(paths: ProjectPaths, token: string, action: string, schema: T, index = 0, offset = 0) {
  return sendProjectBridgeRequest(requestJson => invoke<string>('project_bridge', { requestJson }), kmCommandNames.soundStudio,
    { paths, action, token, index, offset }, schema);
}
export async function loadSoundCatalog(paths: ProjectPaths, token: string, signal: AbortSignal, progress: (completed: number, total: number) => void) {
  await soundRequest(paths, token, 'begin', z.object({ ready: z.boolean() }));
  while (!signal.aborted) {
    const status = await soundRequest(paths, token, 'status', statusSchema);
    if (signal.aborted) break;
    progress(status.completed, status.total);
    if (status.error) throw new Error(status.error);
    if (status.done) {
      const entries: SoundEntry[] = [];
      for (let page = 0; page < status.count; page += 512) {
        signal.throwIfAborted();
        entries.push(...await soundRequest(paths, token, 'page', z.array(soundEntrySchema).max(512), page));
      }
      signal.throwIfAborted(); return entries;
    }
    await new Promise<void>(resolve => { const timer = setTimeout(done, 200); function done() { clearTimeout(timer); signal.removeEventListener('abort', done); resolve(); } signal.addEventListener('abort', done, { once: true }); });
  }
  throw new DOMException('Cancelled', 'AbortError');
}
export async function loadSoundMedia(paths: ProjectPaths, token: string, entry: SoundEntry, signal: AbortSignal, worker: Worker) {
  for (let offset = 0; offset < entry.size;) {
    signal.throwIfAborted();
    const part = await soundRequest(paths, token, 'media', z.object({ data: z.string().max(710000) }), entry.id, offset);
    signal.throwIfAborted(); worker.postMessage({ type: 'input', data: part.data });
    offset += 512 * 1024;
  }
  signal.throwIfAborted(); worker.postMessage({ type: 'open' });
}
export const closeSoundCatalog = (paths: ProjectPaths, token: string) => soundRequest(paths, token, 'close', z.object({ ready: z.boolean() }));
