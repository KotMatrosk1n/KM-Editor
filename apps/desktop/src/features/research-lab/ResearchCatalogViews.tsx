/* SPDX-License-Identifier: GPL-3.0-only */

import { Blocks, Eye, Network, Search } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import type {
  CompareResearchSourcesResponse,
  ReadResearchLabCapabilitiesResponse,
  ResearchConfidence,
  ResearchCoverageState
} from '../../bridge/researchLabContracts';
import { researchLabMaximumAccumulatedFindings } from '../../bridge/researchLabContracts';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import { useLocalization } from '../../localization';
import { TechnicalDetails } from '../workbench/AnalysisPresentation';
import { humanizeIdentifier } from '../workbench/analysisPresentationUtils';
import {
  researchConfidenceKey,
  researchCoverageKey,
  researchFeatureDescriptionKey,
  researchFeatureKey,
  researchReasonKey
} from './researchLabPresentation';

export function ResearchObservationsView({
  capabilities,
  comparison
}: {
  capabilities: ReadResearchLabCapabilitiesResponse;
  comparison: CompareResearchSourcesResponse | null;
}) {
  const { t } = useLocalization();
  const added = comparison?.items.filter((item) => item.differenceKind === 'added').length ?? 0;
  const removed = comparison?.items.filter((item) => item.differenceKind === 'removed').length ?? 0;
  const changed = comparison?.items.filter((item) => item.differenceKind === 'changed').length ?? 0;
  return (
    <section
      aria-labelledby="research-lab-tab-observations"
      className="km-research-lab-panel"
      id="research-lab-panel-observations"
      role="tabpanel"
    >
      <div className="km-research-lab-panel-heading">
        <div>
          <h3 id="research-observations-title">{t('researchLab.observations.title')}</h3>
          <p>{t('researchLab.observations.description')}</p>
        </div>
      </div>
      {comparison ? (
        <div className="km-research-lab-comparison-summary" aria-live="polite">
          <SummaryCard label={t('researchLab.observations.added')} value={added} />
          <SummaryCard label={t('researchLab.observations.removed')} value={removed} />
          <SummaryCard label={t('researchLab.observations.changed')} value={changed} />
          <SummaryCard
            label={t('researchLab.observations.loaded')}
            value={comparison.items.length}
          />
        </div>
      ) : (
        <p className="km-research-lab-empty">{t('researchLab.observations.empty')}</p>
      )}
      <ul
        aria-label={t('researchLab.observations.capabilities')}
        className="km-research-lab-capabilities"
      >
        {capabilities.capabilities.map((capability) => {
          const reasonKey = researchReasonKey(capability.reasonCode);
          return (
            <li key={capability.feature}>
              <article className="km-research-lab-card">
                <div className="km-research-lab-card-title">
                  <Eye aria-hidden="true" size={17} />
                  <div>
                    <h4>{t(researchFeatureKey(capability.feature))}</h4>
                    <p>{t(researchFeatureDescriptionKey(capability.feature))}</p>
                  </div>
                </div>
                <div className="km-research-lab-badges">
                  <ResearchBadge coverage={capability.coverage} />
                  <ResearchBadge confidence={capability.confidence} />
                </div>
                {reasonKey ? <small>{t(reasonKey)}</small> : null}
              </article>
            </li>
          );
        })}
      </ul>
      <p className="km-research-lab-help">
        {t('researchLab.observations.limits', {
          files: capabilities.limits.maximumSelectedFiles,
          window: capabilities.limits.maximumByteWindowLength
        })}
      </p>
    </section>
  );
}

export function ResearchOwnershipView({
  capabilities,
  comparison,
  isBusy,
  isLoadingMore,
  onLoadMore
}: {
  capabilities: ReadResearchLabCapabilitiesResponse;
  comparison: CompareResearchSourcesResponse | null;
  isBusy: boolean;
  isLoadingMore: boolean;
  onLoadMore: () => void;
}) {
  const { t, translateLiteral } = useLocalization();
  const capability = capabilities.capabilities.find(
    (candidate) => candidate.feature === 'ownershipEvidence'
  );
  const capabilityReasonKey = researchReasonKey(capability?.reasonCode ?? null);
  const [query, setQuery] = useCoalescedTextInputState();
  const [selectedFindingId, setSelectedFindingId] = useState<string | null>(null);
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const matches = useMemo(() => normalizedQuery
    ? (comparison?.items ?? []).filter((item) => (
      item.relativePath.toLocaleLowerCase().includes(normalizedQuery)
    )).slice(0, 10)
    : [], [comparison?.items, normalizedQuery]);
  const selectedItem = comparison?.items.find((item) => (
    item.findingId === selectedFindingId
  )) ?? null;
  useEffect(() => {
    if (selectedFindingId && !comparison?.items.some((item) => (
      item.findingId === selectedFindingId
    ))) setSelectedFindingId(null);
  }, [comparison?.items, selectedFindingId]);
  return (
    <section
      aria-labelledby="research-lab-tab-ownership"
      className="km-research-lab-panel"
      id="research-lab-panel-ownership"
      role="tabpanel"
    >
      <div className="km-research-lab-panel-heading">
        <div>
          <h3 id="research-ownership-title">{t('researchLab.ownership.title')}</h3>
          <p>{t('researchLab.ownership.description')}</p>
        </div>
      </div>
      {!capability?.canUse ? (
        <div className="km-research-lab-inline-status" role="status">
          <strong>{t('researchLab.ownership.unavailable')}</strong>
          <p>{t(capabilityReasonKey ?? 'researchLab.reason.unavailable')}</p>
        </div>
      ) : !comparison ? (
        <p className="km-research-lab-empty">{t('researchLab.ownership.empty')}</p>
      ) : (
        <>
          <label className="km-research-lab-ownership-search">
            <span>{t('researchLab.ownership.findFile')}</span>
            <span className="km-research-lab-filter-input">
              <Search aria-hidden="true" size={15} />
              <input
                onChange={(event) => setQuery(event.currentTarget.value)}
                placeholder={t('researchLab.ownership.findFilePlaceholder')}
                type="search"
                value={query}
              />
            </span>
          </label>
          <p aria-live="polite" className="km-research-lab-result-count">
            {t('researchLab.comparison.loaded', { count: comparison.items.length })}
          </p>
          {normalizedQuery ? (
            matches.length > 0 ? (
              <ul className="km-research-lab-file-matches">
                {matches.map((item) => (
                  <li key={item.findingId}>
                    <span data-localization-ignore="true">
                      <strong>{item.relativePath}</strong>
                      {item.ownership.ownerId ? (
                        <small>{humanizeIdentifier(item.ownership.ownerId)}</small>
                      ) : null}
                    </span>
                    <button
                      aria-label={`${translateLiteral('Open')}: ${item.relativePath}`}
                      className="secondary-button compact-button"
                      onClick={() => {
                        setSelectedFindingId(item.findingId);
                        setQuery('');
                      }}
                      type="button"
                    >
                      {translateLiteral('Open')}
                    </button>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="km-research-lab-empty">
                {t('analysisPresentation.controls.noMatches')}
              </p>
            )
          ) : null}
          {selectedItem ? (
            <ul className="km-research-lab-ownership">
              {[selectedItem].map((item) => {
            const reasonKey = researchReasonKey(item.ownership.reasonCode);
            return (
              <li key={item.findingId}>
                <article className="km-research-lab-card">
                  <div className="km-research-lab-card-title">
                    <Network aria-hidden="true" size={17} />
                    <div>
                      <h4 data-localization-ignore="true">{item.relativePath}</h4>
                      <p>{t('researchLab.ownership.fileEvidence')}</p>
                    </div>
                  </div>
                  <div className="km-research-lab-badges">
                    <ResearchBadge coverage={item.ownership.coverage} />
                    <ResearchBadge confidence={item.ownership.confidence} />
                  </div>
                  {item.ownership.ownerId ? (
                    <>
                      <p>
                        <span>{t('researchLab.ownership.owner')}</span>{' '}
                        <span data-localization-ignore="true">
                          {humanizeIdentifier(item.ownership.ownerId)}
                        </span>
                      </p>
                      <TechnicalDetails summary={translateLiteral('Technical details')}>
                        <code>{item.ownership.ownerId}</code>
                      </TechnicalDetails>
                    </>
                  ) : null}
                  {reasonKey ? <small>{t(reasonKey)}</small> : null}
                </article>
              </li>
            );
              })}
            </ul>
          ) : (
            <p className="km-research-lab-empty">{t('researchLab.ownership.searchToView')}</p>
          )}
          {comparison.nextCursor ? (
            comparison.items.length >= researchLabMaximumAccumulatedFindings ? (
              <p className="km-research-lab-help">
                {t('researchLab.comparison.windowLimit', {
                  count: researchLabMaximumAccumulatedFindings
                })}
              </p>
            ) : (
              <button
                aria-busy={isLoadingMore || undefined}
                className="secondary-button compact-button"
                disabled={isBusy}
                onClick={onLoadMore}
                type="button"
              >
                {t(isLoadingMore
                  ? 'researchLab.comparison.loadingMore'
                  : 'researchLab.comparison.more')}
              </button>
            )
          ) : null}
        </>
      )}
    </section>
  );
}

export function ResearchExtensionsView({
  capabilities
}: {
  capabilities: ReadResearchLabCapabilitiesResponse;
}) {
  const { t } = useLocalization();
  const writable = capabilities.capabilities.find(
    (capability) => capability.feature === 'writableExtensions'
  );
  return (
    <section
      aria-labelledby="research-lab-tab-extensions"
      className="km-research-lab-panel"
      id="research-lab-panel-extensions"
      role="tabpanel"
    >
      <div className="km-research-lab-panel-heading">
        <div>
          <h3 id="research-extensions-title">{t('researchLab.extensions.title')}</h3>
          <p>{t('researchLab.extensions.description')}</p>
        </div>
      </div>
      <div className="km-research-lab-inline-status">
        <strong>{t('researchLab.extensions.readOnlyBoundary')}</strong>
        <p>{t('researchLab.extensions.noLoading')}</p>
      </div>
      {capabilities.extensions.length === 0 ? (
        <p className="km-research-lab-empty">{t('researchLab.extensions.empty')}</p>
      ) : (
        <ul className="km-research-lab-extension-list">
          {capabilities.extensions.map((extension) => {
            const reasonKey = researchReasonKey(extension.reasonCode);
            return (
              <li key={extension.extensionId}>
                <article className="km-research-lab-card">
                  <div className="km-research-lab-card-title">
                    <Blocks aria-hidden="true" size={17} />
                    <div>
                      <h4 data-localization-ignore="true">{extension.extensionId}</h4>
                      <p>{t(`researchLab.extensions.kind.${extension.kind}`)}</p>
                    </div>
                  </div>
                  <div className="km-research-lab-badges">
                    <ResearchBadge coverage={extension.coverage} />
                    <ResearchBadge confidence={extension.confidence} />
                  </div>
                  <p>
                    {t('researchLab.extensions.features')}{' '}
                    {extension.features.map((feature) => t(researchFeatureKey(feature))).join(', ')}
                  </p>
                  <p>
                    {t('researchLab.extensions.games')}{' '}
                    {extension.gameFamilies.map((family) => (
                      t(`researchLab.gameFamily.${family}`)
                    )).join(', ')}
                  </p>
                  {reasonKey ? <small>{t(reasonKey)}</small> : null}
                </article>
              </li>
            );
          })}
        </ul>
      )}
      {writable ? (
        <div className="km-research-lab-inline-status" role="status">
          <strong>{t(researchFeatureKey(writable.feature))}</strong>
          <p>{t(researchReasonKey(writable.reasonCode) ?? 'researchLab.reason.unavailable')}</p>
        </div>
      ) : null}
    </section>
  );
}

export function ResearchBadge({
  confidence,
  coverage
}: {
  confidence?: ResearchConfidence;
  coverage?: ResearchCoverageState;
}) {
  const { t } = useLocalization();
  if (coverage) {
    return (
      <span className="km-research-lab-badge" data-state={coverage}>
        {t(researchCoverageKey(coverage))}
      </span>
    );
  }
  return (
    <span className="km-research-lab-badge" data-confidence={confidence}>
      {t(researchConfidenceKey(confidence!))}
    </span>
  );
}

function SummaryCard({ label, value }: { label: string; value: number }) {
  return (
    <div className="km-research-lab-card">
      <strong>{value.toLocaleString()}</strong>
      <span>{label}</span>
    </div>
  );
}
