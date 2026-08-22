/* SPDX-License-Identifier: GPL-3.0-only */

import type { KmCommandName } from './bridge/contracts';

export const maximumPerformanceDiagnosticSamples = 256;
export const performanceDiagnosticsPreferenceStorageKey =
  'km-editor.performance-diagnostics.v1';
export const performanceDiagnosticsPreferenceVersion = 1 as const;

export type PerformanceDiagnosticOutcome = 'success' | 'failure';
export type PerformanceDiagnosticSample = Readonly<{
  command: KmCommandName;
  durationMs: number;
  outcome: PerformanceDiagnosticOutcome;
}>;

export type PerformanceDiagnosticsSnapshot = Readonly<{
  enabled: boolean;
  samples: readonly PerformanceDiagnosticSample[];
}>;

export type PerformanceDiagnosticCommandSummary = Readonly<{
  command: KmCommandName;
  failures: number;
  maximumDurationMs: number;
  medianDurationMs: number;
  p95DurationMs: number;
  sampleCount: number;
}>;

const listeners = new Set<() => void>();
let enabled = readPerformanceDiagnosticsEnabledPreference();
let samples: readonly PerformanceDiagnosticSample[] = [];
let snapshot: PerformanceDiagnosticsSnapshot = { enabled, samples };

export function setPerformanceDiagnosticsEnabled(nextEnabled: boolean) {
  if (enabled === nextEnabled) {
    if (!nextEnabled && samples.length > 0) {
      samples = [];
      publish();
    }
    return;
  }
  enabled = nextEnabled;
  if (!enabled) {
    samples = [];
  }
  writePerformanceDiagnosticsEnabledPreference(enabled);
  publish();
}

export function clearPerformanceDiagnostics() {
  if (samples.length === 0) {
    return;
  }
  samples = [];
  publish();
}

export function recordBridgePerformanceDiagnostic(
  command: KmCommandName,
  durationMs: number,
  outcome: PerformanceDiagnosticOutcome
) {
  if (!enabled || !Number.isFinite(durationMs)) {
    return;
  }
  const sample: PerformanceDiagnosticSample = Object.freeze({
    command,
    durationMs: Math.max(0, Math.round(durationMs)),
    outcome
  });
  samples = Object.freeze([...samples.slice(-(maximumPerformanceDiagnosticSamples - 1)), sample]);
  publish();
}

export function getPerformanceDiagnosticsSnapshot() {
  return snapshot;
}

export function subscribeToPerformanceDiagnostics(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function createPerformanceDiagnosticsSummary() {
  const commands = summarizePerformanceDiagnostics(samples);

  return JSON.stringify(
    {
      schemaVersion: 1,
      sessionOnly: true,
      contentBlind: true,
      sampleCount: samples.length,
      commands
    },
    null,
    2
  );
}

export function summarizePerformanceDiagnostics(
  sourceSamples: readonly PerformanceDiagnosticSample[]
): readonly PerformanceDiagnosticCommandSummary[] {
  const grouped = new Map<KmCommandName, PerformanceDiagnosticSample[]>();
  for (const sample of sourceSamples) {
    const group = grouped.get(sample.command) ?? [];
    group.push(sample);
    grouped.set(sample.command, group);
  }

  return [...grouped.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([command, commandSamples]) => {
      const durations = commandSamples.map((sample) => sample.durationMs).sort((a, b) => a - b);
      return Object.freeze({
        command,
        sampleCount: commandSamples.length,
        failures: commandSamples.filter((sample) => sample.outcome === 'failure').length,
        medianDurationMs: percentile(durations, 0.5),
        p95DurationMs: percentile(durations, 0.95),
        maximumDurationMs: durations.at(-1) ?? 0
      });
    });
}

export function formatPerformanceDiagnosticCommand(command: KmCommandName) {
  return command
    .split('.')
    .map((segment) => humanizeCommandSegment(segment))
    .join(' › ');
}

function humanizeCommandSegment(segment: string) {
  return segment
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .split(' ')
    .map((word) => commandTermLabels.get(word.toLowerCase()) ?? toTitleCase(word))
    .join(' ');
}

const commandTermLabels = new Map<string, string>([
  ['exefs', 'ExeFS'],
  ['fps', 'FPS'],
  ['iv', 'IV'],
  ['npc', 'NPC'],
  ['sv', 'Scarlet and Violet'],
  ['swsh', 'Sword and Shield'],
  ['za', 'Z-A']
]);

function toTitleCase(value: string) {
  return value.length === 0 ? value : `${value[0]!.toUpperCase()}${value.slice(1).toLowerCase()}`;
}

function percentile(values: readonly number[], fraction: number) {
  if (values.length === 0) {
    return 0;
  }
  return values[Math.min(values.length - 1, Math.ceil(values.length * fraction) - 1)];
}

function publish() {
  snapshot = Object.freeze({ enabled, samples });
  for (const listener of listeners) {
    listener();
  }
}

function readPerformanceDiagnosticsEnabledPreference() {
  if (typeof window === 'undefined') {
    return false;
  }
  try {
    const value: unknown = JSON.parse(
      window.localStorage.getItem(performanceDiagnosticsPreferenceStorageKey) ?? 'null'
    );
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
      return false;
    }
    const candidate = value as Record<string, unknown>;
    return Object.keys(candidate).length === 2 &&
      candidate.version === performanceDiagnosticsPreferenceVersion &&
      candidate.enabled === true;
  } catch {
    return false;
  }
}

function writePerformanceDiagnosticsEnabledPreference(nextEnabled: boolean) {
  if (typeof window === 'undefined') {
    return;
  }
  try {
    window.localStorage.setItem(
      performanceDiagnosticsPreferenceStorageKey,
      JSON.stringify({
        enabled: nextEnabled,
        version: performanceDiagnosticsPreferenceVersion
      })
    );
  } catch {
    // The current session still honors the preference when storage is unavailable.
  }
}
