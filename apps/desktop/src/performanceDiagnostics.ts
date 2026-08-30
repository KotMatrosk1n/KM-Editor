/* SPDX-License-Identifier: GPL-3.0-only */

import type { KmCommandName } from './bridge/contracts';

export const maximumPerformanceDiagnosticSamples = 256;
export const performanceDiagnosticsPreferenceStorageKey =
  'km-editor.performance-diagnostics.v1';
export const performanceDiagnosticsPreferenceVersion = 1 as const;

export type PerformanceDiagnosticOutcome =
  | 'success'
  | 'expected-rejection'
  | 'unexpected-failure';
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
  expectedRejections: number;
  maximumDurationMs: number;
  medianDurationMs: number;
  p95DurationMs: number;
  sampleCount: number;
  successes: number;
  unexpectedFailures: number;
}>;

const listeners = new Set<() => void>();
let enabled = readPerformanceDiagnosticsEnabledPreference();
let samples: readonly PerformanceDiagnosticSample[] = [];
let snapshot: PerformanceDiagnosticsSnapshot = { enabled, samples };
const samplesByAssociatedResult = new WeakMap<object, PerformanceDiagnosticSample>();

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
  outcome: PerformanceDiagnosticOutcome,
  associatedResult?: unknown
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
  if (isWeakMapKey(associatedResult)) {
    samplesByAssociatedResult.set(associatedResult, sample);
  }
  publish();
}

export function reclassifyBridgePerformanceDiagnostic(
  associatedResult: unknown,
  outcome: PerformanceDiagnosticOutcome
) {
  if (!isWeakMapKey(associatedResult)) {
    return;
  }
  const currentSample = samplesByAssociatedResult.get(associatedResult);
  if (!currentSample || currentSample.outcome === outcome) {
    return;
  }
  const sampleIndex = samples.indexOf(currentSample);
  if (sampleIndex < 0) {
    samplesByAssociatedResult.delete(associatedResult);
    return;
  }
  const replacement = Object.freeze({ ...currentSample, outcome });
  samples = Object.freeze(samples.map((sample, index) =>
    index === sampleIndex ? replacement : sample
  ));
  samplesByAssociatedResult.set(associatedResult, replacement);
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
  const outcomeCounts = summarizePerformanceDiagnosticOutcomes(samples);

  return JSON.stringify(
    {
      schemaVersion: 2,
      sessionOnly: true,
      contentBlind: true,
      sampleCount: samples.length,
      outcomeDefinitions: {
        success: 'Command returned a valid response.',
        expectedRejection: 'Command was canceled, superseded, unsupported, or safely rejected by a known guard.',
        unexpectedFailure: 'Command failed outside the known rejection and cancellation catalog.'
      },
      outcomeCounts,
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
        successes: countOutcome(commandSamples, 'success'),
        expectedRejections: countOutcome(commandSamples, 'expected-rejection'),
        unexpectedFailures: countOutcome(commandSamples, 'unexpected-failure'),
        medianDurationMs: percentile(durations, 0.5),
        p95DurationMs: percentile(durations, 0.95),
        maximumDurationMs: durations.at(-1) ?? 0
      });
    });
}

function summarizePerformanceDiagnosticOutcomes(
  sourceSamples: readonly PerformanceDiagnosticSample[]
) {
  return Object.freeze({
    successes: countOutcome(sourceSamples, 'success'),
    expectedRejections: countOutcome(sourceSamples, 'expected-rejection'),
    unexpectedFailures: countOutcome(sourceSamples, 'unexpected-failure')
  });
}

function countOutcome(
  sourceSamples: readonly PerformanceDiagnosticSample[],
  outcome: PerformanceDiagnosticOutcome
) {
  return sourceSamples.filter((sample) => sample.outcome === outcome).length;
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

function isWeakMapKey(value: unknown): value is object {
  return (typeof value === 'object' && value !== null) || typeof value === 'function';
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
