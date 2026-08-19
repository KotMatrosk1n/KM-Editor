/* SPDX-License-Identifier: GPL-3.0-only */

import { Command, Search } from 'lucide-react';
import {
  useEffect,
  useId,
  useMemo,
  useState
} from 'react';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import type { WorkspaceCommand } from '../../workbench/commandRegistry';

export type CommandPaletteProps = {
  commands: readonly WorkspaceCommand[];
  isOpen: boolean;
  onClose: () => void;
  onExecute: (command: WorkspaceCommand) => void;
};

export function CommandPalette({
  commands,
  isOpen,
  onClose,
  onExecute
}: CommandPaletteProps) {
  return isOpen ? (
    <OpenCommandPalette commands={commands} onClose={onClose} onExecute={onExecute} />
  ) : null;
}

function OpenCommandPalette({
  commands,
  onClose,
  onExecute
}: Omit<CommandPaletteProps, 'isOpen'>) {
  const { t } = useLocalization();
  const [query, setQuery] = useState('');
  const [activeCommandId, setActiveCommandId] = useState<string | null>(null);
  const dialogRef = useModalDialog<HTMLDivElement>({ onClose });
  const listboxId = useId();
  const visibleCommands = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    if (!normalizedQuery) {
      return commands;
    }
    return commands.filter((command) => {
      const label = command.labelKey ? t(command.labelKey) : command.label ?? '';
      const description = command.descriptionKey ? t(command.descriptionKey) : '';
      return [label, description, command.id, ...command.keywords]
        .join(' ')
        .toLocaleLowerCase()
        .includes(normalizedQuery);
    });
  }, [commands, query, t]);
  const enabledCommands = visibleCommands.filter((command) => command.isEnabled);
  const activeCommand = enabledCommands.find((command) => command.id === activeCommandId)
    ?? enabledCommands[0]
    ?? null;
  const activeCommandIndex = activeCommand
    ? visibleCommands.findIndex((command) => command.id === activeCommand.id)
    : -1;

  const moveActiveCommand = (offset: number) => {
    if (enabledCommands.length === 0) {
      return;
    }
    const currentIndex = activeCommand
      ? enabledCommands.findIndex((command) => command.id === activeCommand.id)
      : -1;
    const nextIndex = (currentIndex + offset + enabledCommands.length) % enabledCommands.length;
    setActiveCommandId(enabledCommands[nextIndex]!.id);
  };
  const execute = (command: WorkspaceCommand) => {
    if (!command.isEnabled) {
      return;
    }
    onExecute(command);
    onClose();
  };

  return (
    <div
      className="km-workbench-overlay"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        aria-labelledby={`${listboxId}-heading`}
        aria-modal="true"
        className="km-command-palette"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <header className="km-command-palette-heading">
          <Command aria-hidden="true" size={18} />
          <h2 id={`${listboxId}-heading`}>{t('workbench.commandPalette.title')}</h2>
          <kbd>Ctrl/Cmd K</kbd>
        </header>
        <label className="km-command-palette-search">
          <Search aria-hidden="true" size={17} />
          <span className="km-workbench-visually-hidden">
            {t('workbench.commandPalette.searchLabel')}
          </span>
          <input
            aria-activedescendant={
              activeCommandIndex >= 0
                ? commandOptionId(listboxId, activeCommandIndex)
                : undefined
            }
            aria-autocomplete="list"
            aria-controls={listboxId}
            aria-expanded="true"
            autoComplete="off"
            onChange={(event) => {
              setQuery(event.target.value);
              setActiveCommandId(null);
            }}
            onKeyDown={(event) => {
              if (event.nativeEvent.isComposing) {
                return;
              }
              if (event.key === 'ArrowDown') {
                event.preventDefault();
                moveActiveCommand(1);
              } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                moveActiveCommand(-1);
              } else if (event.key === 'Home') {
                event.preventDefault();
                setActiveCommandId(enabledCommands[0]?.id ?? null);
              } else if (event.key === 'End') {
                event.preventDefault();
                setActiveCommandId(enabledCommands.at(-1)?.id ?? null);
              } else if (event.key === 'Enter' && activeCommand) {
                event.preventDefault();
                execute(activeCommand);
              }
            }}
            placeholder={t('workbench.commandPalette.placeholder')}
            role="combobox"
            type="search"
            value={query}
          />
        </label>

        <div aria-label={t('workbench.commandPalette.resultsLabel')} className="km-command-list" id={listboxId} role="listbox">
          {visibleCommands.length > 0 ? visibleCommands.map((command, index) => {
            const previousGroup = visibleCommands[index - 1]?.group;
            const label = command.labelKey ? t(command.labelKey) : command.label ?? command.id;
            return (
              <div className="km-command-entry" key={command.id}>
                {previousGroup !== command.group ? (
                  <p className="km-command-group-label">
                    {t(`workbench.command.group.${command.group}`)}
                  </p>
                ) : null}
                <button
                  aria-selected={activeCommand?.id === command.id}
                  className="km-command-option"
                  disabled={!command.isEnabled}
                  id={commandOptionId(listboxId, index)}
                  onClick={() => execute(command)}
                  onMouseEnter={() => command.isEnabled && setActiveCommandId(command.id)}
                  role="option"
                  type="button"
                >
                  <span
                    className="km-command-option-copy"
                    data-localization-ignore={command.labelIsRawData ? 'true' : undefined}
                  >
                    <strong>{label}</strong>
                    {command.descriptionKey ? <small>{t(command.descriptionKey)}</small> : null}
                  </span>
                  {command.shortcut ? <kbd>{command.shortcut}</kbd> : null}
                </button>
              </div>
            );
          }) : (
            <p className="km-workbench-empty">{t('workbench.commandPalette.empty')}</p>
          )}
        </div>
      </div>
    </div>
  );
}

export function useCommandPaletteShortcut(options: {
  disabled?: boolean;
  onOpen: () => void;
}) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (
        options.disabled ||
        event.defaultPrevented ||
        event.isComposing ||
        event.altKey ||
        event.shiftKey ||
        event.key.toLocaleLowerCase() !== 'k' ||
        (!event.ctrlKey && !event.metaKey)
      ) {
        return;
      }
      event.preventDefault();
      options.onOpen();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [options.disabled, options.onOpen]);
}

function commandOptionId(listboxId: string, commandIndex: number) {
  return `${listboxId}-option-${commandIndex}`;
}
