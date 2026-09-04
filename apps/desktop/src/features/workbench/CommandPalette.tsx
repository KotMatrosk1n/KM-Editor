/* SPDX-License-Identifier: GPL-3.0-only */

import { Command, Search } from 'lucide-react';
import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState
} from 'react';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import {
  createWorkspaceEntityCommands,
  maximumWorkspaceEntityCommands,
  maximumWorkspaceEntitySearchTextLength,
  mergeWorkspaceCommandResults,
  type WorkspaceCommand,
  type WorkspaceEntityCommandSearch
} from '../../workbench/commandRegistry';

export type CommandPaletteProps = {
  commands: readonly WorkspaceCommand[];
  entitySearch?: WorkspaceEntityCommandSearch;
  isOpen: boolean;
  onCancelEntitySearch?: () => void;
  onClose: () => void;
  onExecute: (command: WorkspaceCommand) => void;
};

export function CommandPalette({
  commands,
  entitySearch,
  isOpen,
  onCancelEntitySearch,
  onClose,
  onExecute
}: CommandPaletteProps) {
  return isOpen ? (
    <OpenCommandPalette
      commands={commands}
      entitySearch={entitySearch}
      onCancelEntitySearch={onCancelEntitySearch}
      onClose={onClose}
      onExecute={onExecute}
    />
  ) : null;
}

function OpenCommandPalette({
  commands,
  entitySearch,
  onCancelEntitySearch,
  onClose,
  onExecute
}: Omit<CommandPaletteProps, 'isOpen'>) {
  const { t } = useLocalization();
  const [query, setQuery] = useCoalescedTextInputState();
  const [activeCommandId, setActiveCommandId] = useState<string | null>(null);
  const [entityCommands, setEntityCommands] = useState<readonly WorkspaceCommand[]>([]);
  const [entitySearchState, setEntitySearchState] = useState<'idle' | 'loading' | 'error'>('idle');
  usePublishCommonEditorError({
    domain: 'workbench.commandPalette',
    field: 'entitySearch',
    message: entitySearchState === 'error'
      ? t('semanticExplore.command.error')
      : null
  });
  const entityRequestGenerationRef = useRef(0);
  const activeOptionRef = useRef<HTMLButtonElement | null>(null);
  const closePalette = useCallback(() => {
    entityRequestGenerationRef.current += 1;
    onCancelEntitySearch?.();
    onClose();
  }, [onCancelEntitySearch, onClose]);
  const dialogRef = useModalDialog<HTMLDivElement>({ onClose: closePalette });
  const listboxId = useId();
  const baseCommands = useMemo(() => {
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
  const visibleCommands = useMemo(
    () => mergeWorkspaceCommandResults(baseCommands, entityCommands),
    [baseCommands, entityCommands]
  );

  useEffect(() => {
    const searchText = query.trim();
    const generation = ++entityRequestGenerationRef.current;
    setEntityCommands([]);
    setEntitySearchState('idle');
    if (!entitySearch || searchText.length < 2) {
      return;
    }

    const timeout = window.setTimeout(() => {
      setEntitySearchState('loading');
      void entitySearch({
        limit: maximumWorkspaceEntityCommands,
        searchText
      }).then(
        (result) => {
          if (entityRequestGenerationRef.current !== generation) {
            return;
          }
          try {
            if (result.searchText !== searchText) {
              throw new Error('The semantic command result does not match the requested search.');
            }
            setEntityCommands(createWorkspaceEntityCommands(result.targets));
            setEntitySearchState('idle');
          } catch {
            setEntityCommands([]);
            setEntitySearchState('error');
          }
        },
        () => {
          if (entityRequestGenerationRef.current !== generation) {
            return;
          }
          setEntityCommands([]);
          setEntitySearchState('error');
        }
      );
    }, 150);
    return () => {
      window.clearTimeout(timeout);
      onCancelEntitySearch?.();
      if (entityRequestGenerationRef.current === generation) {
        entityRequestGenerationRef.current += 1;
      }
    };
  }, [entitySearch, onCancelEntitySearch, query]);
  const enabledCommands = visibleCommands.filter((command) => command.isEnabled);
  const activeCommand = enabledCommands.find((command) => command.id === activeCommandId)
    ?? enabledCommands[0]
    ?? null;
  const activeCommandIndex = activeCommand
    ? visibleCommands.findIndex((command) => command.id === activeCommand.id)
    : -1;

  useLayoutEffect(() => {
    activeOptionRef.current?.scrollIntoView({ block: 'nearest' });
  }, [activeCommand?.id, activeCommandIndex, query]);

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
    closePalette();
  };

  return (
    <div
      className="km-workbench-overlay"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          closePalette();
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
            aria-describedby={
              entitySearchState === 'idle' ? undefined : `${listboxId}-semantic-status`
            }
            aria-expanded="true"
            aria-haspopup="listbox"
            autoComplete="off"
            maxLength={maximumWorkspaceEntitySearchTextLength}
            onChange={(event) => {
              entityRequestGenerationRef.current += 1;
              onCancelEntitySearch?.();
              setEntityCommands([]);
              setEntitySearchState('idle');
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

        <div className="km-command-results">
          {entitySearchState === 'loading' ? (
            <p
              aria-live="polite"
              className="km-command-search-state"
              id={`${listboxId}-semantic-status`}
            >
                {t('semanticExplore.command.loading')}
            </p>
          ) : null}
          {entitySearchState === 'error' ? (
            <p
              className="km-command-search-state"
              id={`${listboxId}-semantic-status`}
              role="alert"
            >
                {t('semanticExplore.command.error')}
            </p>
          ) : null}

          <div
            aria-label={t('workbench.commandPalette.resultsLabel')}
            className="km-command-list"
            id={listboxId}
            role="listbox"
          >
            {visibleCommands.length > 0 ? visibleCommands.map((command, index) => {
              const previousGroup = visibleCommands[index - 1]?.group;
              const label = command.labelKey ? t(command.labelKey) : command.label ?? command.id;
              const description = command.descriptionKey
                ? t(command.descriptionKey)
                : command.description ?? null;
              const isActive = activeCommand?.id === command.id;
              return (
                <div className="km-command-entry" key={command.id} role="presentation">
                  {previousGroup !== command.group ? (
                    <p aria-hidden="true" className="km-command-group-label">
                      {t(`workbench.command.group.${command.group}`)}
                    </p>
                  ) : null}
                  <button
                    aria-selected={isActive}
                    className="km-command-option"
                    disabled={!command.isEnabled}
                    id={commandOptionId(listboxId, index)}
                    onClick={() => execute(command)}
                    onMouseEnter={() => command.isEnabled && setActiveCommandId(command.id)}
                    ref={isActive ? activeOptionRef : undefined}
                    role="option"
                    type="button"
                  >
                    <span
                      className="km-command-option-copy"
                      data-localization-ignore={command.labelIsRawData ? 'true' : undefined}
                    >
                      <strong>{label}</strong>
                      {description ? (
                        <small
                          data-localization-ignore={
                            command.descriptionIsRawData ? 'true' : undefined
                          }
                        >
                          {description}
                        </small>
                      ) : null}
                    </span>
                    {command.shortcut ? <kbd>{command.shortcut}</kbd> : null}
                  </button>
                </div>
              );
            }) : null}
          </div>
          {visibleCommands.length === 0 ? (
            <p className="km-workbench-empty" role="status">
              {t('workbench.commandPalette.empty')}
            </p>
          ) : null}
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
