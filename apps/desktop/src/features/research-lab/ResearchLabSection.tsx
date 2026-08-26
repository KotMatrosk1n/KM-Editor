/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Blocks,
  Eye,
  FileDiff,
  MessageSquareText,
  Network,
  RefreshCw,
  ShieldCheck
} from 'lucide-react';
import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode
} from 'react';
import {
  researchRevisionIdentity,
  type ResearchAnnotationTarget
} from '../../bridge/researchLabContracts';
import type {
  SemanticExploreRecordRef,
  SemanticExploreRevision
} from '../../bridge/semanticExploreContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';
import { ResearchAnnotationsView } from './ResearchAnnotationsView';
import {
  ResearchExtensionsView,
  ResearchObservationsView,
  ResearchOwnershipView
} from './ResearchCatalogViews';
import { ResearchComparisonView } from './ResearchComparisonView';
import { researchErrorKey } from './researchLabPresentation';
import type { ResearchLabController } from './useResearchLabController';

type ResearchLabView =
  | 'comparison'
  | 'observations'
  | 'annotations'
  | 'ownership'
  | 'extensions';

const researchLabViews: readonly ResearchLabView[] = [
  'comparison',
  'observations',
  'annotations',
  'ownership',
  'extensions'
];

const viewIcons: Readonly<Record<ResearchLabView, ReactNode>> = {
  annotations: <MessageSquareText aria-hidden="true" size={15} />,
  comparison: <FileDiff aria-hidden="true" size={15} />,
  extensions: <Blocks aria-hidden="true" size={15} />,
  observations: <Eye aria-hidden="true" size={15} />,
  ownership: <Network aria-hidden="true" size={15} />
};

export function ResearchLabSection({
  canNavigateRecord,
  controller,
  onNavigateRecord,
  onPickSource,
  revision
}: {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  controller: ResearchLabController;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onPickSource: (slot: 0 | 1) => Promise<string | null>;
  revision: SemanticExploreRevision;
}) {
  const { t } = useLocalization();
  const [activeView, setActiveView] = useState<ResearchLabView>('comparison');
  const [draftTarget, setDraftTarget] = useState<ResearchAnnotationTarget | null>(null);
  const tabRefs = useRef<Partial<Record<ResearchLabView, HTMLButtonElement | null>>>({});
  const capabilities = controller.capabilities.data;
  const revisionIdentity = researchRevisionIdentity(revision);
  const sourceExpirationIdentity = controller.sources
    .map((source) => source.data?.expiresAtUtc ?? null)
    .join('|');
  const clearDraftTarget = useCallback(() => setDraftTarget(null), []);

  useEffect(() => clearDraftTarget(), [clearDraftTarget, revisionIdentity]);
  useEffect(() => {
    const expirations = controller.sources
      .map((source) => source.data ? Date.parse(source.data.expiresAtUtc) : Number.NaN)
      .filter(Number.isFinite);
    if (expirations.length === 0) return;
    const timeout = window.setTimeout(
      controller.expireSources,
      Math.max(0, Math.min(...expirations) - Date.now())
    );
    return () => window.clearTimeout(timeout);
  }, [controller.expireSources, sourceExpirationIdentity]);

  useEffect(() => {
    if (activeView !== 'comparison') controller.clearByteWindow();
    if (activeView === 'annotations' && controller.annotations.status === 'idle') {
      void controller.loadAnnotations();
    }
  }, [activeView, controller.annotations.status]);

  const selectView = (view: ResearchLabView, focus = false) => {
    setActiveView(view);
    if (focus) requestAnimationFrame(() => tabRefs.current[view]?.focus());
  };
  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const currentIndex = researchLabViews.indexOf(activeView);
    let nextIndex: number | null = null;
    if (event.key === 'ArrowRight') nextIndex = (currentIndex + 1) % researchLabViews.length;
    if (event.key === 'ArrowLeft') {
      nextIndex = (currentIndex - 1 + researchLabViews.length) % researchLabViews.length;
    }
    if (event.key === 'Home') nextIndex = 0;
    if (event.key === 'End') nextIndex = researchLabViews.length - 1;
    if (nextIndex === null) return;
    event.preventDefault();
    selectView(researchLabViews[nextIndex]!, true);
  };
  const createAnnotation = (target: ResearchAnnotationTarget) => {
    setDraftTarget(target);
    selectView('annotations', true);
  };

  return (
    <section
      aria-busy={controller.isBusy || undefined}
      aria-labelledby="research-lab-title"
      className="km-research-lab wide-panel"
    >
      <header className="km-research-lab-heading">
        <div>
          <p>{t('researchLab.eyebrow')}</p>
          <h2 id="research-lab-title">{t('researchLab.title')}</h2>
          <span>{t('researchLab.description')}</span>
        </div>
        <button
          aria-busy={controller.isBusy || undefined}
          className="secondary-button compact-button"
          disabled={controller.isBusy}
          onClick={() => void controller.refreshCapabilities()}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={14} />
          <span>{t(controller.isBusy ? 'researchLab.loading' : 'researchLab.refresh')}</span>
        </button>
      </header>

      <p className="km-research-lab-boundary">
        <ShieldCheck aria-hidden="true" size={17} />
        <span>{t('researchLab.boundary')}</span>
      </p>

      {controller.capabilities.status === 'loading' ? (
        <Status compact={Boolean(capabilities)} messageKey="researchLab.loading" />
      ) : null}
      {controller.capabilities.error ? (
        <div className="km-research-lab-status" role="alert">
          <p>{t(researchErrorKey(controller.capabilities.error))}</p>
          <button onClick={() => void controller.refreshCapabilities()} type="button">
            {t('researchLab.retry')}
          </button>
        </div>
      ) : null}

      {capabilities ? (
        <>
          <div
            aria-label={t('researchLab.views.label')}
            className="km-research-lab-view-tabs"
            role="tablist"
          >
            {researchLabViews.map((view) => (
              <button
                aria-controls={`research-lab-panel-${view}`}
                aria-selected={activeView === view}
                id={`research-lab-tab-${view}`}
                key={view}
                onClick={() => selectView(view)}
                onKeyDown={handleTabKeyDown}
                ref={(node) => {
                  tabRefs.current[view] = node;
                }}
                role="tab"
                tabIndex={activeView === view ? 0 : -1}
                type="button"
              >
                {viewIcons[view]}
                <span>{t(`researchLab.views.${view}`)}</span>
              </button>
            ))}
          </div>

          <div hidden={activeView !== 'comparison'}>
            <ResearchComparisonView
              controller={controller}
              onCreateAnnotation={createAnnotation}
              onPickSource={onPickSource}
              revision={revision}
            />
          </div>
          <div hidden={activeView !== 'observations'}>
            <ResearchObservationsView
              capabilities={capabilities}
              comparison={controller.comparison.data}
            />
          </div>
          <div hidden={activeView !== 'annotations'}>
            <ResearchAnnotationsView
              canNavigateRecord={canNavigateRecord}
              controller={controller}
              draftTarget={draftTarget}
              onClearDraftTarget={clearDraftTarget}
              onNavigateRecord={onNavigateRecord}
              revision={revision}
            />
          </div>
          <div hidden={activeView !== 'ownership'}>
            <ResearchOwnershipView
              capabilities={capabilities}
              comparison={controller.comparison.data}
              isBusy={controller.isBusy}
              isLoadingMore={controller.comparison.isAppending}
              onLoadMore={() => void controller.loadMore()}
            />
          </div>
          <div hidden={activeView !== 'extensions'}>
            <ResearchExtensionsView capabilities={capabilities} />
          </div>
        </>
      ) : null}
    </section>
  );
}

function Status({ compact = false, messageKey }: { compact?: boolean; messageKey: string }) {
  const { t } = useLocalization();
  return (
    <div className="km-research-lab-status">
      <LoadingProgress className={compact ? 'is-compact' : undefined} label={t(messageKey)} />
    </div>
  );
}
