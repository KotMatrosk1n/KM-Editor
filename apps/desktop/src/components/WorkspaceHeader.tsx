/* SPDX-License-Identifier: GPL-3.0-only */

import { ExternalLink, X } from 'lucide-react';
import { TooltipIconVisibilityControl } from './TooltipIconVisibility';

export type WorkspaceHeaderProps = {
  activeProjectStateLabel: string;
  activeSectionIsEditor: boolean;
  activeSectionLabel: string;
  activeWikiUrl: string | null;
  hasCriticalWriteOperation: boolean;
  isEditSessionOperationBusy: boolean;
  onCloseEditor: () => void;
  onOpenWiki: () => void;
};

export function WorkspaceHeader({
  activeProjectStateLabel,
  activeSectionIsEditor,
  activeSectionLabel,
  activeWikiUrl,
  hasCriticalWriteOperation,
  isEditSessionOperationBusy,
  onCloseEditor,
  onOpenWiki
}: WorkspaceHeaderProps) {
  return (
    <header className="toolbar">
      <div className="title-block">
        <p className="project-state">{activeProjectStateLabel}</p>
        <h1>{activeSectionLabel}</h1>
      </div>

      <div className="toolbar-actions">
        {activeSectionIsEditor ? <TooltipIconVisibilityControl /> : null}
        {activeWikiUrl ? (
          <button
            aria-label={`Go to Wiki for ${activeSectionLabel}`}
            className="secondary-button toolbar-wiki-button"
            onClick={onOpenWiki}
            title="Open wiki page"
            type="button"
          >
            <ExternalLink aria-hidden="true" size={16} />
            <span>Go to Wiki</span>
          </button>
        ) : null}

        {activeSectionIsEditor ? (
          <button
            aria-label="Close Editor"
            className="secondary-button icon-button"
            disabled={isEditSessionOperationBusy || hasCriticalWriteOperation}
            onClick={onCloseEditor}
            title="Close editor"
            type="button"
          >
            <X aria-hidden="true" size={18} />
          </button>
        ) : null}
      </div>
    </header>
  );
}
