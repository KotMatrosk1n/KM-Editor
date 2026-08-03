/* SPDX-License-Identifier: GPL-3.0-only */

import { Pencil, RefreshCw } from 'lucide-react';
import {
  createContext,
  useContext,
  useId,
  useMemo,
  useState,
  type Dispatch,
  type ReactNode,
  type SetStateAction
} from 'react';
import { createPortal } from 'react-dom';
import { useLocalization } from '../localization';

type EditorSessionActionsContextValue = {
  actionHost: HTMLDivElement | null;
  setActionHost: Dispatch<SetStateAction<HTMLDivElement | null>>;
};

const EditorSessionActionsContext = createContext<EditorSessionActionsContextValue | null>(null);

export function EditorSessionActionsProvider({ children }: { children: ReactNode }) {
  const [actionHost, setActionHost] = useState<HTMLDivElement | null>(null);
  const value = useMemo(
    () => ({ actionHost, setActionHost }),
    [actionHost]
  );

  return (
    <EditorSessionActionsContext.Provider value={value}>
      {children}
    </EditorSessionActionsContext.Provider>
  );
}

export function EditorSessionBarActions({
  children
}: {
  children: ReactNode;
}) {
  const context = useContext(EditorSessionActionsContext);

  if (!context) {
    throw new Error('Editor session actions must be rendered inside their provider.');
  }

  return context.actionHost
    ? createPortal(
        <div className="editor-session-action-group">
          {children}
        </div>,
        context.actionHost
      )
    : null;
}

export type EditorSessionBarProps = {
  canEdit: boolean;
  isEditing: boolean;
  isStarting: boolean;
  label: string;
  onStart: () => void | Promise<void>;
  readOnlyReason?: string | null;
};

export function EditorSessionBar({
  canEdit,
  isEditing,
  isStarting,
  label,
  onStart,
  readOnlyReason
}: EditorSessionBarProps) {
  const { translateLiteral } = useLocalization();
  const actionContext = useContext(EditorSessionActionsContext);
  const labelId = useId();
  if (!actionContext) {
    throw new Error('Editor session bars must be rendered inside their actions provider.');
  }

  const state = isEditing ? 'editing' : !canEdit ? 'readOnly' : 'viewing';
  const statusLabel = state === 'editing' ? 'Editing' : state === 'viewing' ? 'Viewing' : 'Read-only';
  const statusClassName =
    state === 'editing'
      ? 'status-ready'
      : state === 'viewing'
        ? 'status-pill-info'
        : 'status-blocked';
  const explanation =
    state === 'readOnly'
      ? readOnlyReason?.trim() || 'Editing is unavailable.'
      : state === 'editing'
        ? 'Change controls are enabled. Stage changes when ready; files are not written until Review and Apply.'
        : 'Start editing to enable change controls. Nothing is written until Review and Apply.';
  return (
    <section
      aria-labelledby={labelId}
      className="editor-session-bar"
      data-state={state}
    >
      <div className="editor-session-bar-summary">
        <span
          aria-live="polite"
          className={`status-pill ${statusClassName}`}
          role="status"
        >
          {translateLiteral(statusLabel)}
        </span>
        <div className="editor-session-bar-copy">
          <strong id={labelId}>{translateLiteral(label)}</strong>
          <p>{translateLiteral(explanation)}</p>
        </div>
      </div>

      {isEditing ? (
        <div
          aria-label={`${translateLiteral(label)} ${translateLiteral('Actions')}`}
          className="editor-session-bar-actions"
          ref={actionContext.setActionHost}
          role="group"
        />
      ) : state === 'viewing' ? (
        <div className="editor-session-bar-actions">
          <button
            aria-busy={isStarting || undefined}
            className="primary-button compact-button"
            disabled={isStarting}
            onClick={() => void onStart()}
            type="button"
          >
            {isStarting ? (
              <RefreshCw aria-hidden="true" className="button-busy-icon" size={16} />
            ) : (
              <Pencil aria-hidden="true" size={16} />
            )}
            <span>{translateLiteral(isStarting ? 'Starting' : 'Start Editing')}</span>
          </button>
        </div>
      ) : null}
    </section>
  );
}
