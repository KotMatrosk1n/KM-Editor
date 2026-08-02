/* SPDX-License-Identifier: GPL-3.0-only */

import { Pencil, RefreshCw } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { useLocalization } from '../localization';

export type EditorSessionBarProps = {
  activeActions?: ReactNode;
  canEdit: boolean;
  isEditing: boolean;
  isStarting: boolean;
  label: string;
  onStart: () => void | Promise<void>;
  readOnlyReason?: string | null;
};

export function EditorSessionBar({
  activeActions,
  canEdit,
  isEditing,
  isStarting,
  label,
  onStart,
  readOnlyReason
}: EditorSessionBarProps) {
  const { translateLiteral } = useLocalization();
  const labelId = useId();
  const state = !canEdit ? 'readOnly' : isEditing ? 'editing' : 'viewing';
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
  const hasActiveActions = activeActions !== undefined && activeActions !== null;

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

      {isEditing && hasActiveActions ? (
        <div className="editor-session-bar-actions">{activeActions}</div>
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
