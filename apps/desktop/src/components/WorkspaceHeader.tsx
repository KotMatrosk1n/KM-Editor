/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ArrowLeft,
  ArrowRight,
  BookmarkPlus,
  Command,
  ExternalLink,
  MoreHorizontal,
  PanelRightOpen,
  Pin,
  PinOff,
  X
} from 'lucide-react';
import { useLocalization } from '../localization';
import { TooltipIconVisibilityControl } from './TooltipIconVisibility';

export type WorkspaceHeaderProps = {
  activeProjectStateLabel: string;
  activeSectionIsEditor: boolean;
  activeSectionLabel: string;
  activeTargetIsPinned?: boolean;
  activeWikiUrl: string | null;
  canGoBack?: boolean;
  canGoForward?: boolean;
  canSaveView?: boolean;
  hasCriticalWriteOperation: boolean;
  inspectorAvailable?: boolean;
  isEditSessionOperationBusy: boolean;
  onBack?: () => void;
  onCloseEditor: () => void;
  onForward?: () => void;
  onOpenCommandPalette?: () => void;
  onOpenMore?: () => void;
  onSaveView?: () => void;
  onOpenWiki: () => void;
  onToggleInspector?: () => void;
  onTogglePin?: () => void;
};

export function WorkspaceHeader({
  activeProjectStateLabel,
  activeSectionIsEditor,
  activeSectionLabel,
  activeTargetIsPinned = false,
  activeWikiUrl,
  canGoBack = false,
  canGoForward = false,
  canSaveView = false,
  hasCriticalWriteOperation,
  inspectorAvailable = false,
  isEditSessionOperationBusy,
  onBack,
  onCloseEditor,
  onForward,
  onOpenCommandPalette,
  onOpenMore,
  onSaveView,
  onOpenWiki,
  onToggleInspector,
  onTogglePin
}: WorkspaceHeaderProps) {
  const { t } = useLocalization();

  return (
    <header className="toolbar">
      <div className="workspace-header-leading">
        {onBack || onForward ? (
          <nav
            aria-label={t('workbench.header.historyLabel')}
            className="workspace-history-actions"
          >
            <button
              aria-label={t('workbench.header.back')}
              className="secondary-button icon-button"
              disabled={!canGoBack}
              onClick={onBack}
              title={t('workbench.header.back')}
              type="button"
            >
              <ArrowLeft aria-hidden="true" size={18} />
            </button>
            <button
              aria-label={t('workbench.header.forward')}
              className="secondary-button icon-button"
              disabled={!canGoForward}
              onClick={onForward}
              title={t('workbench.header.forward')}
              type="button"
            >
              <ArrowRight aria-hidden="true" size={18} />
            </button>
          </nav>
        ) : null}

        <div className="title-block">
          <p className="project-state">{activeProjectStateLabel}</p>
          <h1>{activeSectionLabel}</h1>
        </div>
      </div>

      <div className="toolbar-actions">
        {onOpenCommandPalette ? (
          <button
            aria-label={t('workbench.header.commandPalette')}
            className="secondary-button workspace-command-button"
            onClick={onOpenCommandPalette}
            title={t('workbench.header.commandPalette')}
            type="button"
          >
            <Command aria-hidden="true" size={17} />
            <span>{t('workbench.header.commandPalette')}</span>
            <kbd>Ctrl/Cmd K</kbd>
          </button>
        ) : null}

        {canSaveView && onSaveView ? (
          <button
            aria-label={t('workbench.header.saveView')}
            className="secondary-button icon-button"
            onClick={onSaveView}
            title={t('workbench.header.saveView')}
            type="button"
          >
            <BookmarkPlus aria-hidden="true" size={17} />
          </button>
        ) : null}

        {onTogglePin && inspectorAvailable ? (
          <button
            aria-label={t(activeTargetIsPinned ? 'workbench.header.unpin' : 'workbench.header.pin')}
            aria-pressed={activeTargetIsPinned}
            className="secondary-button icon-button"
            onClick={onTogglePin}
            title={t(activeTargetIsPinned ? 'workbench.header.unpin' : 'workbench.header.pin')}
            type="button"
          >
            {activeTargetIsPinned ? (
              <PinOff aria-hidden="true" size={17} />
            ) : (
              <Pin aria-hidden="true" size={17} />
            )}
          </button>
        ) : null}

        {onToggleInspector && inspectorAvailable ? (
          <button
            aria-label={t('workbench.header.inspector')}
            className="secondary-button icon-button"
            onClick={onToggleInspector}
            title={t('workbench.header.inspector')}
            type="button"
          >
            <PanelRightOpen aria-hidden="true" size={17} />
          </button>
        ) : null}

        {onOpenMore ? (
          <button
            aria-label={t('workbench.header.more')}
            className="secondary-button icon-button"
            onClick={onOpenMore}
            title={t('workbench.header.more')}
            type="button"
          >
            <MoreHorizontal aria-hidden="true" size={18} />
          </button>
        ) : null}

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
