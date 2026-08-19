/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback } from 'react';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import type { WorkbenchSection } from '../../workbench/workbenchSections';
import {
  currentWhatChangedEntry,
  markCurrentWhatChangedContentSeen
} from './whatChangedCatalog';

export type WhatChangedTourProps = {
  onDismiss: () => void;
  onOpenSection?: (section: WorkbenchSection) => void;
};

export function WhatChangedTour({ onDismiss, onOpenSection }: WhatChangedTourProps) {
  const { t } = useLocalization();
  const dismiss = useCallback(() => {
    markCurrentWhatChangedContentSeen();
    onDismiss();
  }, [onDismiss]);
  const dialogRef = useModalDialog<HTMLDivElement>({ onClose: dismiss });

  const openSection = (section: WorkbenchSection) => {
    markCurrentWhatChangedContentSeen();
    onOpenSection?.(section);
    onDismiss();
  };

  return (
    <div
      className="km-modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          dismiss();
        }
      }}
    >
      <div
        aria-labelledby="what-changed-title"
        aria-modal="true"
        className="km-modal what-changed-tour"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <h2 id="what-changed-title">{t(currentWhatChangedEntry.titleKey)}</h2>
        <p>{t('whatChanged.intro')}</p>
        <ol className="what-changed-list">
          {currentWhatChangedEntry.highlights.map((highlight) => (
            <li key={highlight.titleKey}>
              <h3>{t(highlight.titleKey)}</h3>
              <p>{t(highlight.bodyKey)}</p>
              {highlight.actionSection && onOpenSection ? (
                <button onClick={() => openSection(highlight.actionSection!)} type="button">
                  {t('whatChanged.openFeature')}
                </button>
              ) : null}
            </li>
          ))}
        </ol>
        <div className="km-modal-actions">
          <button onClick={dismiss} type="button">
            {t('whatChanged.done')}
          </button>
        </div>
      </div>
    </div>
  );
}
