/* SPDX-License-Identifier: GPL-3.0-only */

import { invoke, isTauri } from '@tauri-apps/api/core';
import { kmCommandNameValues, type KmCommandName } from './bridge/contracts';
import { supportDiagnosticSnapshotSchema } from './bridge/supportDiagnosticContracts';
import * as errorCodes from './errorCodes';
import { localizedDiagnosticCodes } from './diagnostics';
import tauriConfig from '../src-tauri/tauri.conf.json';
import { collectSupportInventory, observeInventoryScope } from './supportInventory';

type Observation = { id: number; requestId: string; command: KmCommandName; game: string | null; started: number; elapsedMs: number | null; outcome: 'pending' | 'success' | 'failure'; codes: string[] };
const requests: Observation[] = [];
const attempts: ReturnType<typeof supportDiagnosticSnapshotSchema.parse>['attempts'] = [];
let nextId = 0;
let omittedRequests = 0;
let backend: { version: string; commit: string | null } | null = null;
let backendStatus = 'not-observed';
const codes = new Set<string>();
const cacheSnapshots = new Map<string, Record<string, unknown>>();
const sessionCommands = new Map<KmCommandName, { success: number; failure: number }>();
const safeCount = (value: unknown) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;
// Only shipped codes may leave the process. A KM shaped string can still contain a name.
const knownCodes = new Set([...localizedDiagnosticCodes, ...Object.entries(errorCodes)
  .filter(([name]) => name.endsWith('Codes'))
  .flatMap(([, group]) => Object.values(group))]);

export function observeBridgeStart(command: KmCommandName, requestId: string, payload?: unknown): number {
  const id = ++nextId;
  if (!kmCommandNameValues.includes(command)) return id;
  observeInventoryScope(payload);
  if (requests.length === 64) {
    const completed = requests.findIndex((entry) => entry.outcome !== 'pending');
    if (completed < 0) { omittedRequests++; return id; }
    requests.splice(completed, 1);
    omittedRequests++;
  }
  const source = payload && typeof payload === 'object' ? payload as Record<string, unknown> : {};
  const scope = source.scope && typeof source.scope === 'object' ? source.scope as Record<string, unknown> : {};
  const paths = source.paths ?? scope.paths;
  const selectedGame = paths && typeof paths === 'object' ? (paths as Record<string, unknown>).selectedGame : null;
  const game = typeof selectedGame === 'string' && ['sword', 'shield', 'scarlet', 'violet', 'za', 'pla'].includes(selectedGame) ? selectedGame : null;
  requests.push({ id, requestId, command, game, started: performance.now(), elapsedMs: null, outcome: 'pending', codes: [] });
  return id;
}

export function observeBridgeFinish(id: number, failed: boolean, value: unknown): void {
  const observation = requests.find((entry) => entry.id === id);
  if (observation) {
    const counts = sessionCommands.get(observation.command) ?? { success: 0, failure: 0 };
    if (observation.outcome === 'pending') counts[failed ? 'failure' : 'success']++;
    sessionCommands.set(observation.command, counts);
    observation.elapsedMs = Math.max(0, Math.round(performance.now() - observation.started));
    observation.outcome = failed ? 'failure' : 'success';
    observation.codes = collectCodes(value);
    if (!failed && /^(?:sv|swsh|za|pla)Cache\./.test(observation.command)) {
      const status = value && typeof value === 'object' ? (value as Record<string, unknown>).status : null;
      if (status && typeof status === 'object') {
        const source = status as Record<string, unknown>;
        const settings = source.settings && typeof source.settings === 'object' ? source.settings as Record<string, unknown> : {};
        const phases = ['Minimal mode', 'Cache ready', 'Checking cache', 'Caching Trinity payloads', 'Indexing Trinity files', 'Preparing cache', 'Ready to cache'];
        cacheSnapshots.set(observation.command.split('.')[0], {
          request: id,
          phase: typeof source.phase === 'string' && phases.includes(source.phase) ? source.phase : 'unknown',
          mode: ['minimal', 'balanced', 'performance'].includes(settings.mode as string) ? settings.mode : 'unknown',
          cacheSizeBytes: safeCount(source.cacheSizeBytes), maxCacheSizeBytes: safeCount(settings.maxCacheSizeBytes),
          warmupCompleted: safeCount(source.warmupCompleted), warmupTotal: safeCount(source.warmupTotal)
        });
      }
    }
  }
}

function collectCodes(value: unknown): string[] {
  const found = new Set<string>();
  let budget = 2048;
  function visit(node: unknown, depth: number) {
    if (--budget < 0 || depth > 6 || !node || typeof node !== 'object') return;
    if (Array.isArray(node)) { for (const entry of node.slice(0, 128)) visit(entry, depth + 1); return; }
    const object = node as Record<string, unknown>;
    const code = object.code;
    if (typeof code === 'string' && knownCodes.has(code)) { found.add(code); if (codes.size < 64) codes.add(code); }
    for (const key of ['apiError', 'error', 'diagnostics', 'health', 'status', 'workflow', 'payload']) visit(object[key], depth + 1);
  }
  visit(value, 0);
  return [...found].slice(0, 32);
}

export function observeDiagnosticCode(code: unknown): void { collectCodes({ code }); }

export function observeBridgeResponse(value: unknown): void {
  collectCodes(value);
  const result = supportDiagnosticSnapshotSchema.safeParse(value && typeof value === 'object'
    ? (value as Record<string, unknown>).supportDiagnostics : undefined);
  if (!result.success) { if (!backend) backendStatus = 'unavailable-or-incompatible'; return; }
  backendStatus = 'available';
  backend = { version: result.data.backendVersion, commit: result.data.backendCommit };
  for (const entry of result.data.attempts) {
    if (!attempts.some((prior) => JSON.stringify(prior) === JSON.stringify(entry))) attempts.push(entry);
  }
  while (attempts.length > 8) {
    const success = attempts.findIndex((entry) => entry.outcome === 'success');
    attempts.splice(success < 0 ? 0 : success, 1);
  }
}

function safeNative(value: unknown) {
  if (!value || typeof value !== 'object') return null;
  const source = value as Record<string, unknown>;
  const version = typeof source.version === 'string' && /^\d+(?:\.\d+){1,3}$/.test(source.version) ? source.version : null;
  const commit = typeof source.commit === 'string' && /^[a-f0-9]{40}$/.test(source.commit) ? source.commit : null;
  const phases = new Set(['request-started', 'request-scheduled', 'permit-wait-started', 'permit-acquired', 'execution-started', 'process-admission-wait-started', 'process-admission-acquired', 'process-io-wait-started', 'process-io-acquired', 'response-read-finished', 'execution-finished', 'request-finished', 'response-restarted', 'transport-step']);
  const nativeRequests = Array.isArray(source.requests) ? source.requests.slice(-64).flatMap((raw: unknown) => {
    if (!raw || typeof raw !== 'object') return [];
    const entry = raw as Record<string, unknown>;
    const local = requests.find((request) => request.requestId === entry.requestId);
    if (!local) return [];
    return [{ request: local.id, phase: typeof entry.phase === 'string' && phases.has(entry.phase) ? entry.phase : 'unknown',
      phaseAtElapsedMs: safeCount(entry.elapsedMs), queueMs: safeCount(entry.queueMs), executionMs: safeCount(entry.executionMs), attempt: safeCount(entry.attempt) }];
  }) : [];
  return { version, commit, modified: source.modified === 'true' ? true : source.modified === 'false' ? false : null,
    requests: nativeRequests, requestsAvailable: Array.isArray(source.requests) };
}

export async function createBugReport(): Promise<string> {
  // This command reads an in-memory native snapshot and bypasses the managed queue.
  // The local report remains usable if the native layer is absent or unresponsive.
  let timer: ReturnType<typeof setTimeout> | undefined;
  let native = null;
  const inventoryPromise = collectSupportInventory();
  try {
    native = safeNative(await Promise.race([
      invoke<unknown>('get_support_diagnostics'),
      new Promise<null>((resolve) => { timer = setTimeout(() => resolve(null), 1000); })
    ]));
  } catch { /* Explicitly unavailable below. Never include exception text. */ }
  finally { clearTimeout(timer); }
  const inventory = await inventoryPromise;
  const report = {
    schemaVersion: 2, privacy: 'allowlisted-session-data',
    frontend: { version: tauriConfig.version, commit: __KM_BUILD_COMMIT__, modified: __KM_BUILD_MODIFIED__ },
    native: native ?? { status: 'unavailable' },
    backend: { status: backendStatus, ...backend },
    diagnosticCodes: [...codes], outputAttempts: attempts,
    caches: Object.fromEntries(cacheSnapshots),
    inventory, omittedRequests,
    sessionCommands: [...sessionCommands].sort(([a], [b]) => a.localeCompare(b)).map(([command, counts]) => ({ command, ...counts })),
    requests: requests.map(({ id, command, game, started, elapsedMs, outcome, codes: requestCodes }) => ({
      request: id, command, game, elapsedMs: elapsedMs ?? Math.max(0, Math.round(performance.now() - started)), outcome, codes: requestCodes
    }))
  };
  return JSON.stringify(report, null, 2);
}

export async function saveBugReport(report: string): Promise<string | null> {
  if (isTauri()) {
    return await invoke<string>('save_support_report', { report });
  }
  const url = URL.createObjectURL(new Blob([report], { type: 'application/json' }));
  try {
    const link = document.createElement('a');
    link.href = url;
    link.download = 'KMEditor-bug-report.json';
    link.click();
  } finally { setTimeout(() => URL.revokeObjectURL(url), 1000); }
  return null;
}

export async function openBugReport(fileName: string): Promise<void> {
  await invoke('open_support_report', { fileName });
}
