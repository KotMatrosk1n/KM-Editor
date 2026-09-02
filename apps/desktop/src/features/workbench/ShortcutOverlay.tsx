/* SPDX-License-Identifier: GPL-3.0-only */

import { Check, Keyboard, Pencil, RotateCcw, Search, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import {
  defaultWorkspaceShortcutDefinitions,
  isSafeWorkspaceShortcutId,
  normalizeWorkspaceShortcut
} from '../../workbench/shortcutRegistry';

export type WorkspaceShortcutViewModel = {
  chord: string;
  descriptionKey: string;
  id: string;
  labelKey: string;
};

export type ShortcutOverlayProps = {
  isOpen: boolean;
  onClose: () => void;
  onResetShortcut?: (id: string) => Promise<void> | void;
  onSetShortcut?: (id: string, chord: string) => Promise<void> | void;
  shortcuts: readonly WorkspaceShortcutViewModel[];
};

export function ShortcutOverlay(props: ShortcutOverlayProps) {
  return props.isOpen ? (
    <OpenShortcutOverlay
      onClose={props.onClose}
      onResetShortcut={props.onResetShortcut}
      onSetShortcut={props.onSetShortcut}
      shortcuts={props.shortcuts}
    />
  ) : null;
}

function OpenShortcutOverlay({
  onClose,
  onResetShortcut,
  onSetShortcut,
  shortcuts
}: Omit<ShortcutOverlayProps, 'isOpen'>) {
  const { t } = useLocalization();
  const [query, setQuery] = useState('');
  const [editingShortcutId, setEditingShortcutId] = useState<string | null>(null);
  const [draftChord, setDraftChord] = useState('');
  const [feedback, setFeedback] = useState<{ id: string; key: string } | null>(null);
  usePublishCommonEditorError({
    domain: 'workbench.shortcuts',
    field: feedback?.id,
    message: feedback ? t(feedback.key) : null
  });
  const [isMutationBusy, setIsMutationBusy] = useState(false);
  const mutationOperationRef = useRef<object | null>(null);
  const draftChordRef = useRef(draftChord);
  draftChordRef.current = draftChord;
  const requestClose = () => {
    if (mutationOperationRef.current === null) onClose();
  };
  const dialogRef = useModalDialog<HTMLDivElement>({
    canClose: !isMutationBusy,
    onClose: requestClose
  });
  const visibleShortcuts = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    return normalizedQuery.length === 0
      ? shortcuts
      : shortcuts.filter((shortcut) =>
          [t(shortcut.labelKey), t(shortcut.descriptionKey), shortcut.chord]
            .join(' ')
            .toLocaleLowerCase()
            .includes(normalizedQuery)
        );
  }, [query, shortcuts, t]);

  const beginEdit = (shortcut: WorkspaceShortcutViewModel) => {
    if (
      mutationOperationRef.current !== null ||
      !onSetShortcut ||
      !isSafeWorkspaceShortcutId(shortcut.id)
    ) {
      return;
    }
    setEditingShortcutId(shortcut.id);
    setDraftChord(shortcut.chord);
    setFeedback(null);
  };
  const saveShortcut = async (shortcut: WorkspaceShortcutViewModel) => {
    if (
      isMutationBusy ||
      mutationOperationRef.current !== null ||
      !onSetShortcut ||
      !isSafeWorkspaceShortcutId(shortcut.id)
    ) {
      return;
    }
    const submittedDraftChord = draftChord;
    let normalizedChord: string;
    try {
      normalizedChord = normalizeWorkspaceShortcut(submittedDraftChord);
    } catch {
      setFeedback({ id: shortcut.id, key: 'workbench.shortcuts.invalid' });
      return;
    }
    const hasConflict = shortcuts.some(
      (candidate) =>
        candidate.id !== shortcut.id &&
        normalizeComparableShortcut(candidate.chord) === normalizedChord
    );
    if (hasConflict) {
      setFeedback({ id: shortcut.id, key: 'workbench.shortcuts.conflict' });
      return;
    }
    const mutationOperation = {};
    mutationOperationRef.current = mutationOperation;
    setIsMutationBusy(true);
    try {
      await onSetShortcut(shortcut.id, normalizedChord);
      setEditingShortcutId((current) =>
        current === shortcut.id && draftChordRef.current === submittedDraftChord
          ? null
          : current
      );
      setFeedback(null);
    } catch {
      setFeedback({ id: shortcut.id, key: 'workbench.shortcuts.saveError' });
    } finally {
      if (mutationOperationRef.current === mutationOperation) {
        mutationOperationRef.current = null;
        setIsMutationBusy(false);
      }
    }
  };
  const resetShortcut = async (shortcut: WorkspaceShortcutViewModel) => {
    if (
      isMutationBusy ||
      mutationOperationRef.current !== null ||
      !onResetShortcut ||
      !isSafeWorkspaceShortcutId(shortcut.id)
    ) {
      return;
    }
    const defaultShortcut = defaultWorkspaceShortcutDefinitions.find(
      (candidate) => candidate.id === shortcut.id
    );
    const defaultChord = defaultShortcut
      ? normalizeWorkspaceShortcut(defaultShortcut.chord)
      : null;
    if (
      defaultChord &&
      shortcuts.some(
        (candidate) =>
          candidate.id !== shortcut.id &&
          normalizeComparableShortcut(candidate.chord) === defaultChord
      )
    ) {
      setFeedback({ id: shortcut.id, key: 'workbench.shortcuts.conflict' });
      return;
    }
    const submittedDraftChord = draftChordRef.current;
    const mutationOperation = {};
    mutationOperationRef.current = mutationOperation;
    setIsMutationBusy(true);
    try {
      await onResetShortcut(shortcut.id);
      setEditingShortcutId((current) =>
        current === shortcut.id && draftChordRef.current === submittedDraftChord
          ? null
          : current
      );
      setFeedback(null);
    } catch {
      setFeedback({ id: shortcut.id, key: 'workbench.shortcuts.resetError' });
    } finally {
      if (mutationOperationRef.current === mutationOperation) {
        mutationOperationRef.current = null;
        setIsMutationBusy(false);
      }
    }
  };

  return (
    <div
      className="km-workbench-overlay"
      onMouseDown={(event) => {
        if (
          mutationOperationRef.current === null &&
          !isMutationBusy &&
          event.target === event.currentTarget
        ) {
          requestClose();
        }
      }}
    >
      <div
        aria-labelledby="km-shortcut-overlay-heading"
        aria-modal="true"
        className="km-shortcut-overlay"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <header className="km-shortcut-overlay-heading">
          <Keyboard aria-hidden="true" size={18} />
          <div>
            <h2 id="km-shortcut-overlay-heading">{t('workbench.shortcuts.title')}</h2>
            <p>{t('workbench.shortcuts.description')}</p>
          </div>
          <button
            className="secondary-button"
            disabled={isMutationBusy}
            onClick={requestClose}
            type="button"
          >
            {t('workbench.shortcuts.close')}
          </button>
        </header>
        <label className="km-command-palette-search">
          <Search aria-hidden="true" size={17} />
          <span className="km-workbench-visually-hidden">
            {t('workbench.shortcuts.searchLabel')}
          </span>
          <input
            autoComplete="off"
            onChange={(event) => setQuery(event.target.value)}
            placeholder={t('workbench.shortcuts.placeholder')}
            type="search"
            value={query}
          />
        </label>
        {visibleShortcuts.length > 0 ? (
          <dl className="km-shortcut-list">
            {visibleShortcuts.map((shortcut) => (
              <div key={shortcut.id}>
                <dt>
                  <strong>{t(shortcut.labelKey)}</strong>
                  <small>{t(shortcut.descriptionKey)}</small>
                </dt>
                <dd className="km-shortcut-controls">
                  <span className="km-shortcut-current">
                    <kbd>{shortcut.chord}</kbd>
                    {onSetShortcut && isSafeWorkspaceShortcutId(shortcut.id) ? (
                      <button
                        aria-label={t('workbench.shortcuts.editLabel', {
                          label: t(shortcut.labelKey)
                        })}
                        className="secondary-button icon-button"
                        disabled={isMutationBusy}
                        onClick={() => beginEdit(shortcut)}
                        type="button"
                      >
                        <Pencil aria-hidden="true" size={14} />
                      </button>
                    ) : null}
                    {onResetShortcut && isSafeWorkspaceShortcutId(shortcut.id) ? (
                      <button
                        aria-label={t('workbench.shortcuts.resetLabel', {
                          label: t(shortcut.labelKey)
                        })}
                        className="secondary-button icon-button"
                        disabled={isMutationBusy}
                        onClick={() => void resetShortcut(shortcut)}
                        type="button"
                      >
                        <RotateCcw aria-hidden="true" size={14} />
                      </button>
                    ) : null}
                  </span>
                  {editingShortcutId === shortcut.id ? (
                    <form
                      className="km-shortcut-editor"
                      onSubmit={(event) => {
                        event.preventDefault();
                        void saveShortcut(shortcut);
                      }}
                    >
                      <label>
                        <span className="km-workbench-visually-hidden">
                          {t('workbench.shortcuts.inputLabel', {
                            label: t(shortcut.labelKey)
                          })}
                        </span>
                        <input
                          autoFocus
                          maxLength={64}
                          onChange={(event) => {
                            setDraftChord(event.target.value);
                            setFeedback(null);
                          }}
                          placeholder={t('workbench.shortcuts.inputPlaceholder')}
                          type="text"
                          value={draftChord}
                        />
                      </label>
                      <button
                        aria-label={t('workbench.shortcuts.save')}
                        className="secondary-button icon-button"
                        disabled={isMutationBusy}
                        type="submit"
                      >
                        <Check aria-hidden="true" size={14} />
                      </button>
                      <button
                        aria-label={t('workbench.shortcuts.cancel')}
                        className="secondary-button icon-button"
                        disabled={isMutationBusy}
                        onClick={() => {
                          setEditingShortcutId(null);
                          setFeedback(null);
                        }}
                        type="button"
                      >
                        <X aria-hidden="true" size={14} />
                      </button>
                    </form>
                  ) : null}
                  {feedback?.id === shortcut.id ? (
                    <small className="km-shortcut-feedback" role="alert">
                      {t(feedback.key)}
                    </small>
                  ) : null}
                </dd>
              </div>
            ))}
          </dl>
        ) : (
          <p className="km-workbench-empty">{t('workbench.shortcuts.empty')}</p>
        )}
      </div>
    </div>
  );
}

function normalizeComparableShortcut(value: string) {
  try {
    return normalizeWorkspaceShortcut(value);
  } catch {
    return value.trim().toLocaleLowerCase();
  }
}

export function useShortcutOverlayShortcut(options: {
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
        event.metaKey ||
        event.shiftKey ||
        !event.ctrlKey ||
        event.key !== '/'
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
