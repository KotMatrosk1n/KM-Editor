/* SPDX-License-Identifier: GPL-3.0-only */

import type { WorkspaceShortcutViewModel } from '../features/workbench/ShortcutOverlay';

export const maximumWorkspaceShortcuts = 64;
export const maximumWorkspaceShortcutLength = 64;

export type SafeWorkspaceShortcutCommand =
  | 'history.back'
  | 'history.forward'
  | 'shell.commandPalette'
  | 'shell.shortcuts';

export type WorkspaceShortcutDefinition = WorkspaceShortcutViewModel & {
  command: SafeWorkspaceShortcutCommand;
};

export type ResolvedWorkspaceShortcut = Omit<WorkspaceShortcutDefinition, 'chord'> & {
  chord: string | null;
};

export const defaultWorkspaceShortcutDefinitions: readonly WorkspaceShortcutDefinition[] = [
  {
    chord: 'Ctrl+K',
    command: 'shell.commandPalette',
    descriptionKey: 'workbench.commandPalette.description',
    id: 'command-palette',
    labelKey: 'workbench.commandPalette.title'
  },
  {
    chord: 'Alt+ArrowLeft',
    command: 'history.back',
    descriptionKey: 'workbench.command.back.description',
    id: 'history-back',
    labelKey: 'workbench.command.back.label'
  },
  {
    chord: 'Alt+ArrowRight',
    command: 'history.forward',
    descriptionKey: 'workbench.command.forward.description',
    id: 'history-forward',
    labelKey: 'workbench.command.forward.label'
  },
  {
    chord: 'Ctrl+/',
    command: 'shell.shortcuts',
    descriptionKey: 'workbench.command.shortcuts.description',
    id: 'shortcut-overlay',
    labelKey: 'workbench.command.shortcuts.label'
  }
];

const safeWorkspaceShortcutIds = new Set(
  defaultWorkspaceShortcutDefinitions.map((definition) => definition.id)
);

export function isSafeWorkspaceShortcutId(value: string) {
  return safeWorkspaceShortcutIds.has(value);
}

export function createWorkspaceShortcutRegistry(
  overrides: Readonly<Record<string, string | null>> = {}
): readonly ResolvedWorkspaceShortcut[] {
  if (defaultWorkspaceShortcutDefinitions.length > maximumWorkspaceShortcuts) {
    throw new Error('The workspace shortcut registry exceeds its bounded capacity.');
  }
  const seenChords = new Set<string>();
  return defaultWorkspaceShortcutDefinitions.map((definition) => {
    const override = overrides[definition.id];
    const chord = override === null
      ? null
      : normalizeWorkspaceShortcut(override ?? definition.chord);
    if (chord) {
      if (seenChords.has(chord)) {
        throw new Error(`Workspace shortcut ${chord} is assigned more than once.`);
      }
      seenChords.add(chord);
    }
    return { ...definition, chord };
  });
}

export function normalizeWorkspaceShortcut(value: string) {
  if (value.length === 0 || value.length > maximumWorkspaceShortcutLength) {
    throw new Error('A workspace shortcut must have a bounded value.');
  }
  const parts = value.split('+').map((part) => part.trim()).filter(Boolean);
  const modifiers = new Set<string>();
  let key: string | null = null;
  for (const part of parts) {
    const normalizedModifier = normalizeModifier(part);
    if (normalizedModifier) {
      if (modifiers.has(normalizedModifier)) {
        throw new Error('A shortcut cannot repeat a modifier.');
      }
      modifiers.add(normalizedModifier);
      continue;
    }
    if (key !== null) {
      throw new Error('A shortcut must contain exactly one non-modifier key.');
    }
    key = normalizeKey(part);
  }
  if (!key || modifiers.size === 0) {
    throw new Error('A workspace shortcut requires a modifier and one key.');
  }
  if (modifiers.has('Shift') && /^(?:[0-9]|\/)$/u.test(key)) {
    throw new Error('Shifted symbol shortcuts are not supported.');
  }
  return [
    ...(modifiers.has('Ctrl') ? ['Ctrl'] : []),
    ...(modifiers.has('Meta') ? ['Meta'] : []),
    ...(modifiers.has('Alt') ? ['Alt'] : []),
    ...(modifiers.has('Shift') ? ['Shift'] : []),
    key
  ].join('+');
}

export function resolveWorkspaceShortcut(
  event: KeyboardEvent,
  shortcuts: readonly ResolvedWorkspaceShortcut[]
) {
  if (event.defaultPrevented || event.isComposing) {
    return null;
  }
  const chord = eventToChord(event);
  const exactMatch = shortcuts.find((shortcut) => shortcut.chord === chord);
  if (exactMatch) {
    return exactMatch;
  }
  if (event.metaKey && !event.ctrlKey) {
    const controlEquivalent = chord.replace(/^Meta(?=\+|$)/u, 'Ctrl');
    return shortcuts.find((shortcut) => shortcut.chord === controlEquivalent) ?? null;
  }
  return null;
}

function normalizeModifier(value: string) {
  switch (value.toLocaleLowerCase()) {
    case 'ctrl':
    case 'control':
      return 'Ctrl';
    case 'cmd':
    case 'command':
    case 'meta':
      return 'Meta';
    case 'alt':
    case 'option':
      return 'Alt';
    case 'shift':
      return 'Shift';
    default:
      return null;
  }
}

function normalizeKey(value: string) {
  const trimmed = value.trim();
  switch (trimmed.toLocaleLowerCase()) {
    case 'left':
    case 'arrowleft':
      return 'ArrowLeft';
    case 'right':
    case 'arrowright':
      return 'ArrowRight';
    case 'up':
    case 'arrowup':
      return 'ArrowUp';
    case 'down':
    case 'arrowdown':
      return 'ArrowDown';
    case 'home':
      return 'Home';
    case 'end':
      return 'End';
  }
  if (!/^(?:[A-Za-z0-9/]|ArrowLeft|ArrowRight|ArrowUp|ArrowDown|Home|End)$/u.test(trimmed)) {
    throw new Error('The workspace shortcut key is not supported.');
  }
  return trimmed.length === 1 ? trimmed.toLocaleUpperCase() : trimmed;
}

function eventToChord(event: KeyboardEvent) {
  const key = event.key.length === 1 ? event.key.toLocaleUpperCase() : event.key;
  return [
    ...(event.ctrlKey ? ['Ctrl'] : []),
    ...(event.metaKey ? ['Meta'] : []),
    ...(event.altKey ? ['Alt'] : []),
    ...(event.shiftKey ? ['Shift'] : []),
    key
  ].join('+');
}
