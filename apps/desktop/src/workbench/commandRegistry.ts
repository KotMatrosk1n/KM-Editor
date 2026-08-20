/* SPDX-License-Identifier: GPL-3.0-only */

import type { CapabilityDiscoveryViewModel } from './capabilityDiscovery';
import type { WorkbenchLocation } from './workbenchLocation';
import { semanticRecordRefKey } from './semanticContracts';
import type { WorkbenchSection } from './workbenchSections';
import type {
  ResolvedWorkspaceShortcut,
  SafeWorkspaceShortcutCommand
} from './shortcutRegistry';
import type {
  WorkspaceSavedViewViewModel,
  WorkspaceTargetViewModel
} from './workspaceShellViewModels';

export const maximumWorkspaceCommands = 512;
export const maximumWorkspaceEntityCommands = 32;
export const maximumWorkspaceEntitySearchTextLength = 256;

export type WorkspaceCommandGroup =
  | 'entities'
  | 'history'
  | 'navigation'
  | 'shell'
  | 'targets'
  | 'views';

export type WorkspaceCommandAction =
  | { kind: 'back' }
  | { kind: 'forward' }
  | { kind: 'navigate'; location: WorkbenchLocation }
  | { kind: 'openInspector' }
  | { kind: 'openShortcuts' }
  | { kind: 'openView'; viewId: string };

export type WorkspaceCommand = {
  action: WorkspaceCommandAction;
  description?: string | null;
  descriptionIsRawData?: boolean;
  descriptionKey: string | null;
  group: WorkspaceCommandGroup;
  id: string;
  isEnabled: boolean;
  keywords: readonly string[];
  label: string | null;
  labelIsRawData: boolean;
  labelKey: string | null;
  shortcut: string | null;
};

export type WorkspaceEntityCommandTarget = {
  description: string | null;
  keywords: readonly string[];
  label: string;
  location: WorkbenchLocation;
};

export type WorkspaceEntityCommandSearchRequest = {
  limit: number;
  searchText: string;
};

export type WorkspaceEntityCommandSearchResult = {
  searchText: string;
  targets: readonly WorkspaceEntityCommandTarget[];
};

export type WorkspaceEntityCommandSearch = (
  request: WorkspaceEntityCommandSearchRequest
) => Promise<WorkspaceEntityCommandSearchResult>;

export function createWorkspaceCommandRegistry(options: {
  canGoBack: boolean;
  canGoForward: boolean;
  capabilities: readonly CapabilityDiscoveryViewModel[];
  createSectionLocation: (section: WorkbenchSection) => WorkbenchLocation;
  inspectorAvailable: boolean;
  pins: readonly WorkspaceTargetViewModel[];
  recents: readonly WorkspaceTargetViewModel[];
  shortcuts: readonly ResolvedWorkspaceShortcut[];
  views: readonly WorkspaceSavedViewViewModel[];
}): WorkspaceCommand[] {
  const sectionCommands: WorkspaceCommand[] = options.capabilities
    .filter((capability) => capability.status !== 'blocked')
    .map((capability) => ({
      action: {
        kind: 'navigate',
        location: options.createSectionLocation(capability.id)
      },
      descriptionKey: capability.descriptionKey,
      group: 'navigation',
      id: `navigate.${capability.id}`,
      isEnabled: true,
      keywords: [capability.id, ...capability.capabilityKinds],
      label: null,
      labelIsRawData: false,
      labelKey: capability.labelKey,
      shortcut: null
    }));

  const historyCommands: WorkspaceCommand[] = [
    {
      action: { kind: 'back' },
      descriptionKey: 'workbench.command.back.description',
      group: 'history',
      id: 'history.back',
      isEnabled: options.canGoBack,
      keywords: ['back', 'history', 'previous'],
      label: null,
      labelIsRawData: false,
      labelKey: 'workbench.command.back.label',
      shortcut: findShortcut(options.shortcuts, 'history.back')
    },
    {
      action: { kind: 'forward' },
      descriptionKey: 'workbench.command.forward.description',
      group: 'history',
      id: 'history.forward',
      isEnabled: options.canGoForward,
      keywords: ['forward', 'history', 'next'],
      label: null,
      labelIsRawData: false,
      labelKey: 'workbench.command.forward.label',
      shortcut: findShortcut(options.shortcuts, 'history.forward')
    }
  ];

  const targetCommands = [
    ...options.pins.map((target) => targetCommand('pin', target)),
    ...options.recents.map((target) => targetCommand('recent', target))
  ];
  const viewCommands: WorkspaceCommand[] = options.views.map((view) => ({
    action: { kind: 'openView', viewId: view.id },
    descriptionKey: null,
    group: 'views',
    id: `view.${view.id}`,
    isEnabled: true,
    keywords: ['saved', 'view'],
    label: view.name,
    labelIsRawData: true,
    labelKey: null,
    shortcut: null
  }));
  const shellCommands: WorkspaceCommand[] = [
    {
      action: { kind: 'openInspector' },
      descriptionKey: 'workbench.command.inspector.description',
      group: 'shell',
      id: 'shell.inspector',
      isEnabled: options.inspectorAvailable,
      keywords: ['details', 'inspector', 'context'],
      label: null,
      labelIsRawData: false,
      labelKey: 'workbench.command.inspector.label',
      shortcut: null
    },
    {
      action: { kind: 'openShortcuts' },
      descriptionKey: 'workbench.command.shortcuts.description',
      group: 'shell',
      id: 'shell.shortcuts',
      isEnabled: true,
      keywords: ['keyboard', 'keys', 'shortcuts'],
      label: null,
      labelIsRawData: false,
      labelKey: 'workbench.command.shortcuts.label',
      shortcut: findShortcut(options.shortcuts, 'shell.shortcuts')
    }
  ];

  const commands = [
    ...historyCommands,
    ...sectionCommands,
    ...targetCommands,
    ...viewCommands,
    ...shellCommands
  ];
  if (commands.length > maximumWorkspaceCommands) {
    throw new Error('The workspace command registry exceeds its bounded capacity.');
  }
  if (new Set(commands.map((command) => command.id)).size !== commands.length) {
    throw new Error('Workspace command ids must be unique.');
  }
  return commands;
}

export function createWorkspaceEntityCommands(
  targets: readonly WorkspaceEntityCommandTarget[]
): WorkspaceCommand[] {
  if (targets.length > maximumWorkspaceEntityCommands) {
    throw new Error('The semantic entity command result exceeds its bounded capacity.');
  }

  const commands = targets.map((target) => ({
    action: { kind: 'navigate', location: target.location } as const,
    description: target.description,
    descriptionIsRawData: true,
    descriptionKey: null,
    group: 'entities' as const,
    id: semanticEntityCommandId(target),
    isEnabled: true,
    keywords: target.keywords,
    label: target.label,
    labelIsRawData: true,
    labelKey: null,
    shortcut: null
  }));
  if (new Set(commands.map((command) => command.id)).size !== commands.length) {
    throw new Error('Semantic entity command ids must be unique.');
  }
  return commands;
}

function semanticEntityCommandId(target: WorkspaceEntityCommandTarget) {
  if (!target.location.entity) {
    throw new Error('A semantic entity command requires an exact entity location.');
  }
  return `semantic-entity:${semanticRecordRefKey(target.location.entity)}`;
}

export function mergeWorkspaceCommandResults(
  commands: readonly WorkspaceCommand[],
  entityCommands: readonly WorkspaceCommand[]
) {
  if (entityCommands.length > maximumWorkspaceEntityCommands) {
    throw new Error('The semantic entity command result exceeds its bounded capacity.');
  }
  if (entityCommands.some((command) => command.group !== 'entities')) {
    throw new Error('Only semantic entity commands can extend a command search result.');
  }

  const merged = [...commands, ...entityCommands];
  if (merged.length > maximumWorkspaceCommands) {
    throw new Error('The merged workspace command result exceeds its bounded capacity.');
  }
  if (new Set(merged.map((command) => command.id)).size !== merged.length) {
    throw new Error('Merged workspace command ids must be unique.');
  }
  return merged;
}

function findShortcut(
  shortcuts: readonly ResolvedWorkspaceShortcut[],
  command: SafeWorkspaceShortcutCommand
) {
  return shortcuts.find((shortcut) => shortcut.command === command)?.chord ?? null;
}

function targetCommand(kind: 'pin' | 'recent', target: WorkspaceTargetViewModel): WorkspaceCommand {
  return {
    action: { kind: 'navigate', location: target.location },
    descriptionKey: kind === 'pin'
      ? 'workbench.command.pin.description'
      : 'workbench.command.recent.description',
    group: 'targets',
    id: `${kind}.${target.id}`,
    isEnabled: true,
    keywords: [kind, target.description ?? ''],
    label: target.label,
    labelIsRawData: target.labelIsRawData,
    labelKey: null,
    shortcut: null
  };
}
