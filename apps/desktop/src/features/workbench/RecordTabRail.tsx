/* SPDX-License-Identifier: GPL-3.0-only */

import { X } from 'lucide-react';
import { useLayoutEffect, useRef } from 'react';
import { useLocalization } from '../../localization';
import type { WorkbenchLocation } from '../../workbench/workbenchLocation';

export type WorkspaceRecordTabViewModel = {
  hasProtectedDraft: boolean;
  key: string;
  label: string;
  labelIsRawData: boolean;
  location: WorkbenchLocation;
};

export type RecordTabRailProps = {
  activeTabKey: string | null;
  onActivate: (location: WorkbenchLocation) => void;
  onClose?: (tabKey: string) => void;
  tabs: readonly WorkspaceRecordTabViewModel[];
};

export function RecordTabRail({
  activeTabKey,
  onActivate,
  onClose,
  tabs
}: RecordTabRailProps) {
  const { t } = useLocalization();
  const railRef = useRef<HTMLDivElement | null>(null);
  const firstTabKey = tabs[0]?.key ?? null;

  useLayoutEffect(() => {
    if (firstTabKey !== null && railRef.current) {
      railRef.current.scrollLeft = 0;
    }
  }, [firstTabKey]);

  if (tabs.length === 0) {
    return null;
  }

  return (
    <div
      aria-label={t('workbench.tabs.label')}
      className="km-record-tab-rail"
      ref={railRef}
      role="tablist"
    >
      {tabs.map((tab, index) => {
        const isActive = activeTabKey === tab.key;
        return (
          <div className="km-record-tab-item" key={tab.key}>
            <button
              aria-selected={isActive}
              className="km-record-tab"
              data-localization-ignore={tab.labelIsRawData ? 'true' : undefined}
              onClick={() => onActivate(tab.location)}
              onKeyDown={(event) => {
                if (event.nativeEvent.isComposing) {
                  return;
                }
                const nextIndex = resolveTabIndex(event.key, index, tabs.length);
                if (nextIndex === null) {
                  return;
                }
                event.preventDefault();
                const nextTab = tabs[nextIndex];
                if (nextTab) {
                  onActivate(nextTab.location);
                  const tabButtons = event.currentTarget
                    .closest('[role="tablist"]')
                    ?.querySelectorAll<HTMLButtonElement>('[role="tab"]');
                  tabButtons?.[nextIndex]?.focus({ preventScroll: true });
                }
              }}
              role="tab"
              tabIndex={isActive || (activeTabKey === null && index === 0) ? 0 : -1}
              title={tab.label}
              type="button"
            >
              <span>{tab.label}</span>
              {tab.hasProtectedDraft ? (
                <span
                  aria-label={t('workbench.tabs.unsaved')}
                  className="km-record-tab-dirty"
                />
              ) : null}
            </button>
            {onClose ? (
              <button
                aria-label={t('workbench.tabs.close', { label: tab.label })}
                className="km-record-tab-close"
                disabled={tab.hasProtectedDraft}
                onClick={() => onClose(tab.key)}
                title={tab.hasProtectedDraft ? t('workbench.tabs.closeBlocked') : undefined}
                type="button"
              >
                <X aria-hidden="true" size={14} />
              </button>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

function resolveTabIndex(key: string, currentIndex: number, tabCount: number) {
  switch (key) {
    case 'ArrowLeft':
      return (currentIndex - 1 + tabCount) % tabCount;
    case 'ArrowRight':
      return (currentIndex + 1) % tabCount;
    case 'Home':
      return 0;
    case 'End':
      return tabCount - 1;
    default:
      return null;
  }
}
