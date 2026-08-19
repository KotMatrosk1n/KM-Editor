/* SPDX-License-Identifier: GPL-3.0-only */

import { X } from 'lucide-react';
import {
  useEffect,
  useId,
  useState,
  type ReactNode
} from 'react';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import type { WorkbenchInspectorTab } from '../../workbench/workbenchLocation';

export type AdaptiveInspectorTabViewModel = {
  content: ReactNode;
  count: number | null;
  id: WorkbenchInspectorTab;
  labelKey: string;
};

export type AdaptiveInspectorProps = {
  activeTab: WorkbenchInspectorTab | null;
  isOpen: boolean;
  onClose: () => void;
  onSelectTab: (tab: WorkbenchInspectorTab) => void;
  tabs: readonly AdaptiveInspectorTabViewModel[];
  targetLabel: string;
  targetLabelIsRawData: boolean;
};

export const adaptiveInspectorNarrowMediaQuery = '(max-width: 1100px)';

export function AdaptiveInspector(props: AdaptiveInspectorProps) {
  const isNarrow = useMediaQuery(adaptiveInspectorNarrowMediaQuery);
  if (!props.isOpen || props.tabs.length === 0) {
    return null;
  }
  return isNarrow ? <NarrowInspector {...props} /> : <WideInspector {...props} />;
}

function WideInspector(props: AdaptiveInspectorProps) {
  const headingId = useId();
  return (
    <aside aria-labelledby={headingId} className="km-adaptive-inspector">
      <InspectorBody {...props} headingId={headingId} />
    </aside>
  );
}

function NarrowInspector(props: AdaptiveInspectorProps) {
  const headingId = useId();
  const dialogRef = useModalDialog<HTMLDivElement>({ onClose: props.onClose });
  return (
    <div
      className="km-workbench-overlay km-inspector-overlay"
      onMouseDown={(event) => event.target === event.currentTarget && props.onClose()}
    >
      <div
        aria-labelledby={headingId}
        aria-modal="true"
        className="km-adaptive-inspector km-adaptive-inspector-modal"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <InspectorBody {...props} headingId={headingId} />
      </div>
    </div>
  );
}

function InspectorBody({
  activeTab,
  headingId,
  onClose,
  onSelectTab,
  tabs,
  targetLabel,
  targetLabelIsRawData
}: AdaptiveInspectorProps & { headingId: string }) {
  const { t } = useLocalization();
  const selectedTab = tabs.find((tab) => tab.id === activeTab) ?? tabs[0]!;
  const panelId = `${headingId}-panel`;
  return (
    <>
      <header className="km-inspector-heading">
        <div>
          <p>{t('workbench.inspector.eyebrow')}</p>
          <h2 id={headingId}>{t('workbench.inspector.title')}</h2>
          <small data-localization-ignore={targetLabelIsRawData ? 'true' : undefined}>
            {targetLabel}
          </small>
        </div>
        <button
          aria-label={t('workbench.inspector.close')}
          className="secondary-button icon-button"
          onClick={onClose}
          title={t('workbench.inspector.close')}
          type="button"
        >
          <X aria-hidden="true" size={17} />
        </button>
      </header>
      <div aria-label={t('workbench.inspector.tabs')} className="km-inspector-tabs" role="tablist">
        {tabs.map((tab, index) => (
          <button
            aria-controls={panelId}
            aria-selected={selectedTab.id === tab.id}
            id={`${headingId}-tab-${tab.id}`}
            key={tab.id}
            onClick={() => onSelectTab(tab.id)}
            onKeyDown={(event) => {
              if (event.nativeEvent.isComposing) {
                return;
              }
              const nextIndex = resolveInspectorTabIndex(event.key, index, tabs.length);
              if (nextIndex === null) {
                return;
              }
              event.preventDefault();
              const nextTab = tabs[nextIndex];
              if (nextTab) {
                onSelectTab(nextTab.id);
                const tabButtons = event.currentTarget.parentElement
                  ?.querySelectorAll<HTMLButtonElement>('[role="tab"]');
                tabButtons?.[nextIndex]?.focus({ preventScroll: true });
              }
            }}
            role="tab"
            tabIndex={selectedTab.id === tab.id ? 0 : -1}
            type="button"
          >
            <span>{t(tab.labelKey)}</span>
            {tab.count !== null ? <small>{tab.count}</small> : null}
          </button>
        ))}
      </div>
      <div
        aria-labelledby={`${headingId}-tab-${selectedTab.id}`}
        className="km-inspector-content"
        id={panelId}
        role="tabpanel"
      >
        {selectedTab.content}
      </div>
    </>
  );
}

function resolveInspectorTabIndex(key: string, currentIndex: number, tabCount: number) {
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

function useMediaQuery(query: string) {
  const [matches, setMatches] = useState(() =>
    typeof window === 'undefined' ? false : window.matchMedia(query).matches
  );
  useEffect(() => {
    const mediaQuery = window.matchMedia(query);
    const update = () => setMatches(mediaQuery.matches);
    update();
    mediaQuery.addEventListener('change', update);
    return () => mediaQuery.removeEventListener('change', update);
  }, [query]);
  return matches;
}
