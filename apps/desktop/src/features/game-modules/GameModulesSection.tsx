/* SPDX-License-Identifier: GPL-3.0-only */

import { ArrowLeft, Boxes, ExternalLink, RefreshCw, ShieldCheck } from 'lucide-react';
import { useEffect, useRef, useState, type RefObject } from 'react';
import type {
  GameModule,
  GameModuleCapability
} from '../../bridge/gameModuleContracts';
import {
  gameModuleMaximumAccumulatedRecords
} from '../../bridge/gameModuleContracts';
import type { SemanticExploreRecordRef } from '../../bridge/semanticExploreContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';
import { getWorkbenchSectionLabelKey } from '../../workbench/capabilityRegistry';
import type { WorkbenchSection } from '../../workbench/workbenchSections';
import {
  gameModuleDescriptionKey,
  gameModuleOwnerSections,
  gameModuleReasonKey,
  gameModuleTitleKey
} from './gameModuleCatalog';
import {
  ConfidenceBadge,
  GameModuleDiagnostics,
  GameModuleResults
} from './GameModuleResults';
import { GameModuleComparison } from './GameModuleComparison';
import type { GameModuleController } from './useGameModuleController';
import './gameModules.css';

export type GameModulesSectionProps = {
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  canOpenSection: (section: WorkbenchSection) => boolean;
  controller: GameModuleController;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
  onOpenSection: (section: WorkbenchSection) => void;
};

export function GameModulesSection({
  canNavigateRecord,
  canOpenSection,
  controller,
  onNavigateRecord,
  onOpenSection
}: GameModulesSectionProps) {
  const { t } = useLocalization();
  const [selectedModule, setSelectedModule] = useState<GameModule | null>(null);
  const detailBackRef = useRef<HTMLButtonElement | null>(null);
  const launcherRefs = useRef<Partial<Record<GameModule, HTMLButtonElement | null>>>({});
  const previousModuleRef = useRef<GameModule | null>(null);
  const capabilityData = controller.capabilities.data;

  useEffect(() => {
    if (controller.capabilities.status === 'idle') {
      void controller.loadCapabilities();
    }
  }, [controller, controller.capabilities.status]);

  useEffect(() => {
    const previous = previousModuleRef.current;
    if (selectedModule && previous !== selectedModule) {
      detailBackRef.current?.focus({ preventScroll: true });
    } else if (!selectedModule && previous) {
      launcherRefs.current[previous]?.focus({ preventScroll: true });
    }
    previousModuleRef.current = selectedModule;
  }, [selectedModule]);

  useEffect(() => {
    if (
      selectedModule &&
      !capabilityData?.capabilities.some((capability) => capability.module === selectedModule)
    ) {
      setSelectedModule(null);
    }
  }, [capabilityData, selectedModule]);

  const selectedCapability = selectedModule
    ? capabilityData?.capabilities.find((capability) => capability.module === selectedModule) ?? null
    : null;

  if (selectedModule && selectedCapability) {
    return (
      <GameModuleDetail
        backRef={detailBackRef}
        canNavigateRecord={canNavigateRecord}
        capability={selectedCapability}
        controller={controller}
        onBack={() => {
          controller.cancel();
          setSelectedModule(null);
        }}
        onNavigateRecord={onNavigateRecord}
      />
    );
  }

  return (
    <section
      aria-busy={controller.isBusy || undefined}
      aria-labelledby="game-modules-title"
      className="km-game-modules wide-panel"
    >
      <header className="km-game-modules-heading">
        <div>
          <p>{t('gameModules.eyebrow')}</p>
          <h2 id="game-modules-title">{t('gameModules.title')}</h2>
          <span>{t('gameModules.description')}</span>
        </div>
        <button
          aria-busy={controller.isBusy || undefined}
          className="secondary-button compact-button"
          disabled={controller.isBusy}
          onClick={() => void controller.refreshCapabilities()}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={15} />
          <span>{t(controller.isBusy ? 'gameModules.loading' : 'gameModules.refresh')}</span>
        </button>
      </header>

      <p className="km-game-modules-boundary">
        <ShieldCheck aria-hidden="true" size={17} />
        <span>{t('gameModules.boundary')}</span>
      </p>

      {controller.capabilities.status === 'loading' ? <StatusPanel kind="loading" /> : null}
      {controller.capabilities.status === 'error' ? (
        <StatusPanel
          kind="error"
          onRetry={() => void controller.refreshCapabilities()}
        />
      ) : null}
      {capabilityData ? (
        <ul aria-label={t('gameModules.catalog.label')} className="km-game-module-catalog">
          {capabilityData.capabilities.map((capability) => (
            <li key={capability.module}>
              <CapabilityCard
                canOpenSection={canOpenSection}
                capability={capability}
                onOpen={() => {
                  const layer = capability.supportedLayers[0];
                  if (!capability.canQuery || !layer) return;
                  setSelectedModule(capability.module);
                  void controller.query({ layer, module: capability.module });
                }}
                onOpenSection={onOpenSection}
                registerLauncher={(node) => {
                  launcherRefs.current[capability.module] = node;
                }}
              />
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

function CapabilityCard({
  canOpenSection,
  capability,
  onOpen,
  onOpenSection,
  registerLauncher
}: {
  canOpenSection: (section: WorkbenchSection) => boolean;
  capability: GameModuleCapability;
  onOpen: () => void;
  onOpenSection: (section: WorkbenchSection) => void;
  registerLauncher: (node: HTMLButtonElement | null) => void;
}) {
  const { t } = useLocalization();
  const owners = gameModuleOwnerSections[capability.module].filter(canOpenSection);
  return (
    <article className="km-game-module-card">
      <header>
        <Boxes aria-hidden="true" size={18} />
        <div>
          <h3>{t(gameModuleTitleKey(capability.module))}</h3>
          <p>{t(gameModuleDescriptionKey(capability.module))}</p>
        </div>
      </header>
      <div className="km-game-module-card-badges">
        <span data-state={capability.state}>{t(`gameModules.state.${capability.state}`)}</span>
        <ConfidenceBadge confidence={capability.confidence} />
        <span data-maturity={capability.maturity}>
          {t(`gameModules.maturity.${capability.maturity}`)}
        </span>
      </div>
      {capability.reasonCode ? (
        <p className="km-game-module-reason">
          {t(gameModuleReasonKey(capability.reasonCode))}
        </p>
      ) : null}
      <div className="km-game-module-card-actions">
        {capability.canQuery ? (
          <button
            ref={registerLauncher}
            className="primary-button compact-button"
            onClick={onOpen}
            type="button"
          >
            {t('gameModules.catalog.explore')}
          </button>
        ) : null}
        {owners.length > 0 ? (
          <details>
            <summary>{t('gameModules.catalog.relatedEditors')}</summary>
            <div>
              {owners.map((section) => (
                <button
                  className="secondary-button compact-button"
                  key={section}
                  onClick={() => onOpenSection(section)}
                  type="button"
                >
                  <ExternalLink aria-hidden="true" size={13} />
                  <span>{t(getWorkbenchSectionLabelKey(section))}</span>
                </button>
              ))}
            </div>
          </details>
        ) : null}
      </div>
    </article>
  );
}

function GameModuleDetail({
  backRef,
  canNavigateRecord,
  capability,
  controller,
  onBack,
  onNavigateRecord
}: {
  backRef: RefObject<HTMLButtonElement | null>;
  canNavigateRecord: (record: SemanticExploreRecordRef) => boolean;
  capability: GameModuleCapability;
  controller: GameModuleController;
  onBack: () => void;
  onNavigateRecord: (record: SemanticExploreRecordRef) => void;
}) {
  const { t } = useLocalization();
  const result = controller.activeQuery?.module === capability.module
    ? controller.result.data
    : null;
  return (
    <section
      aria-busy={controller.isBusy || undefined}
      aria-labelledby="game-module-detail-title"
      className="km-game-modules wide-panel"
    >
      <button
        ref={backRef}
        className="secondary-button compact-button km-game-module-detail-back"
        onClick={onBack}
        type="button"
      >
        <ArrowLeft aria-hidden="true" size={14} />
        <span>{t('gameModules.catalog.back')}</span>
      </button>
      <header className="km-game-modules-heading">
        <div>
          <p>{t('gameModules.eyebrow')}</p>
          <h2 id="game-module-detail-title">{t(gameModuleTitleKey(capability.module))}</h2>
          <span>{t(gameModuleDescriptionKey(capability.module))}</span>
        </div>
        <button
          aria-busy={controller.isBusy || undefined}
          className="secondary-button compact-button"
          disabled={controller.isBusy}
          onClick={() => void controller.refresh()}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={15} />
          <span>{t(controller.isBusy ? 'gameModules.loading' : 'gameModules.refresh')}</span>
        </button>
      </header>
      {!result && controller.result.status === 'loading' ? <StatusPanel kind="loading" /> : null}
      {!result && controller.result.status === 'error' ? (
        <StatusPanel kind="error" onRetry={() => void controller.refresh()} />
      ) : null}
      {result ? (
        <>
          {controller.result.status === 'loading' && !controller.result.isAppending ? (
            <div className="km-game-module-status">
              <LoadingProgress className="is-compact" label={t('gameModules.loading')} />
            </div>
          ) : null}
          {controller.result.status === 'error' ? (
            <InlineError onRetry={() => void controller.refresh()} />
          ) : null}
          <GameModuleDiagnostics diagnostics={result.diagnostics} />
          {result.records.length > 0 ? (
            <GameModuleComparison
              canNavigateRecord={canNavigateRecord}
              key={result.queryFingerprint}
              onNavigateRecord={onNavigateRecord}
              response={result}
            />
          ) : null}
          {result.nextCursor ? (
            result.records.length >= gameModuleMaximumAccumulatedRecords ? (
              <p className="km-game-module-window-limit">
                {t('gameModules.results.windowLimit')}
              </p>
            ) : (
              <>
                <button
                  aria-busy={controller.result.isAppending || undefined}
                  className="secondary-button km-game-module-load-more"
                  disabled={controller.result.isAppending}
                  onClick={() => void controller.loadMore()}
                  type="button"
                >
                  {controller.result.isAppending
                    ? t('gameModules.loading')
                    : t('gameModules.results.more')}
                </button>
                {controller.result.isAppending ? (
                  <LoadingProgress
                    className="is-compact"
                    completed={result.records.length}
                    label={t('gameModules.loading')}
                    total={Math.min(
                      result.totalRecordCount,
                      gameModuleMaximumAccumulatedRecords
                    )}
                  />
                ) : null}
              </>
            )
          ) : null}
          {result.records.length === 0 ? (
            <GameModuleResults
              canNavigateRecord={canNavigateRecord}
              onNavigateRecord={onNavigateRecord}
              response={result}
            />
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function StatusPanel({
  kind,
  onRetry
}: {
  kind: 'loading' | 'error';
  onRetry?: () => void;
}) {
  const { t } = useLocalization();
  const errorMessage = kind === 'error' ? t('gameModules.error') : null;
  usePublishCommonEditorError({ domain: 'analysis.gameModules', message: errorMessage });
  if (kind === 'loading') {
    return (
      <div className="km-game-module-status">
        <LoadingProgress label={t('gameModules.loading')} />
      </div>
    );
  }
  return (
    <div
      aria-live="polite"
      className="km-game-module-status"
      role="alert"
    >
      <p>{errorMessage}</p>
      {onRetry ? <button onClick={onRetry} type="button">{t('gameModules.retry')}</button> : null}
    </div>
  );
}

function InlineError({ onRetry }: { onRetry: () => void }) {
  const { t } = useLocalization();
  const message = t('gameModules.queryError');
  usePublishCommonEditorError({ domain: 'analysis.gameModules', message });
  return (
    <div className="km-game-module-inline-error" role="alert">
      <span>{message}</span>
      <button className="secondary-button compact-button" onClick={onRetry} type="button">
        {t('gameModules.retry')}
      </button>
    </div>
  );
}
