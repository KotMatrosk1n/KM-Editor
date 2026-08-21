/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Binary,
  FileDiff,
  FolderOpen,
  MessageSquarePlus,
  Search,
  X
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  researchLabDefaultPageSize,
  researchLabMaximumAccumulatedFindings,
  researchLabMaximumByteWindowLength,
  researchLabMaximumSelectedFiles,
  researchRevisionIdentity,
  type ResearchAnnotationTarget,
  type ResearchByteWindowSide,
  type ResearchFileFinding
} from '../../bridge/researchLabContracts';
import type { SemanticExploreRevision } from '../../bridge/semanticExploreContracts';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';
import {
  researchDifferenceKey,
  researchErrorKey,
  researchRangeCoverageKey
} from './researchLabPresentation';
import type { ResearchLabController } from './useResearchLabController';

export function ResearchComparisonView({
  controller,
  onCreateAnnotation,
  onPickSource,
  revision
}: {
  controller: ResearchLabController;
  onCreateAnnotation: (target: ResearchAnnotationTarget) => void;
  onPickSource: (slot: 0 | 1) => Promise<string | null>;
  revision: SemanticExploreRevision;
}) {
  const { t } = useLocalization();
  const [selectedPaths, setSelectedPaths] = useState<Set<string>>(new Set());
  const [resultFilter, setResultFilter] = useState('');
  const [differenceFilter, setDifferenceFilter] = useState<
    'all' | ResearchFileFinding['differenceKind']
  >('all');
  const [resultOrder, setResultOrder] = useState<'path' | 'difference' | 'largest'>('path');
  const pickerGenerationRef = useRef<[number, number]>([0, 0]);
  const isMountedRef = useRef(true);
  const sourceIdentity = [
    researchRevisionIdentity(revision),
    ...controller.sources.map((source) => (
      `${source.data?.sourceId ?? 'none'}:${source.status}`
    ))
  ].join('|');

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      pickerGenerationRef.current[0] += 1;
      pickerGenerationRef.current[1] += 1;
    };
  }, []);
  useEffect(() => {
    setSelectedPaths(new Set());
    controller.clearByteWindow();
    pickerGenerationRef.current[0] += 1;
    pickerGenerationRef.current[1] += 1;
  }, [sourceIdentity]);

  const pickSource = async (slot: 0 | 1) => {
    const generation = ++pickerGenerationRef.current[slot];
    const rootPath = await onPickSource(slot);
    if (
      rootPath === null ||
      !isMountedRef.current ||
      pickerGenerationRef.current[slot] !== generation
    ) return;
    await controller.openSource(slot, rootPath);
  };
  const data = controller.comparison.data;
  const committedPaths = controller.comparison.selectedRelativePaths;
  const selectionMatchesCommitted = samePathSelection(selectedPaths, committedPaths);
  const canCompare = controller.sources.every((source) => (
    source.status === 'ready' &&
    source.data !== null &&
    Date.parse(source.data.expiresAtUtc) > Date.now()
  ));
  const isWindowCapped = Boolean(
    data?.nextCursor &&
    data.items.length + researchLabDefaultPageSize > researchLabMaximumAccumulatedFindings
  );
  const visibleFindings = useMemo(() => {
    const normalizedFilter = resultFilter.trim().toLocaleLowerCase();
    return [...(data?.items ?? [])]
      .filter((finding) => (
        (differenceFilter === 'all' || finding.differenceKind === differenceFilter) &&
        (!normalizedFilter || finding.relativePath.toLocaleLowerCase().includes(normalizedFilter))
      ))
      .sort((left, right) => {
        if (resultOrder === 'difference') {
          return left.differenceKind.localeCompare(right.differenceKind) ||
            left.relativePath.localeCompare(right.relativePath);
        }
        if (resultOrder === 'largest') {
          const leftSize = Math.max(left.sourceA.length ?? 0, left.sourceB.length ?? 0);
          const rightSize = Math.max(right.sourceA.length ?? 0, right.sourceB.length ?? 0);
          return rightSize - leftSize || left.relativePath.localeCompare(right.relativePath);
        }
        return left.relativePath.localeCompare(right.relativePath);
      });
  }, [data?.items, differenceFilter, resultFilter, resultOrder]);
  useEffect(() => {
    if (
      differenceFilter !== 'all' &&
      !data?.items.some((finding) => finding.differenceKind === differenceFilter)
    ) setDifferenceFilter('all');
  }, [data?.items, differenceFilter]);
  const selectVisible = () => setSelectedPaths((current) => {
    const next = new Set(current);
    for (const finding of visibleFindings) {
      if (next.size >= researchLabMaximumSelectedFiles) break;
      next.add(finding.relativePath);
    }
    return next;
  });

  return (
    <section
      aria-labelledby="research-lab-tab-comparison"
      className="km-research-lab-panel"
      id="research-lab-panel-comparison"
      role="tabpanel"
    >
      <div className="km-research-lab-panel-heading">
        <div>
          <h3 id="research-comparison-title">{t('researchLab.comparison.title')}</h3>
          <p>{t('researchLab.comparison.description')}</p>
        </div>
        {controller.isBusy ? (
          <button
            className="secondary-button compact-button"
            onClick={controller.cancel}
            type="button"
          >
            <X aria-hidden="true" size={14} />
            <span>{t('researchLab.stopWaiting')}</span>
          </button>
        ) : null}
      </div>

      <p className="km-research-lab-help">{t('researchLab.cancellationHelp')}</p>
      <div className="km-research-lab-source-grid">
        {([0, 1] as const).map((slot) => (
          <SourceCard
            key={slot}
            controller={controller}
            onPick={() => void pickSource(slot)}
            slot={slot}
          />
        ))}
      </div>

      <div className="km-research-lab-toolbar">
        <button
          className="primary-button compact-button"
          disabled={!canCompare || controller.isBusy}
          onClick={() => void controller.compare([...selectedPaths])}
          type="button"
        >
          <FileDiff aria-hidden="true" size={15} />
          <span>{t(data
            ? 'researchLab.comparison.refresh'
            : 'researchLab.comparison.run')}</span>
        </button>
        <span role="status">
          {t('researchLab.comparison.selected', {
            count: selectedPaths.size,
            maximum: researchLabMaximumSelectedFiles
          })}
        </span>
        <button
          className="secondary-button compact-button"
          disabled={controller.isBusy || visibleFindings.length === 0 || (
            selectedPaths.size >= researchLabMaximumSelectedFiles
          )}
          onClick={selectVisible}
          type="button"
        >
          {t('analysisPresentation.controls.selectVisible')}
        </button>
        <button
          className="secondary-button compact-button"
          disabled={controller.isBusy || selectedPaths.size === 0}
          onClick={() => setSelectedPaths(new Set())}
          type="button"
        >
          {t('researchLab.comparison.clearSelection')}
        </button>
      </div>

      {controller.comparison.status === 'loading' && !data ? (
        <Status messageKey="researchLab.comparison.loading" />
      ) : null}
      {controller.comparison.status === 'loading' && data && !controller.comparison.isAppending ? (
        <Status compact messageKey="researchLab.comparison.loading" />
      ) : null}
      {controller.comparison.error ? (
        <Status error messageKey={researchErrorKey(controller.comparison.error)} />
      ) : null}
      {data ? (
        <div className="km-research-lab-query-summary">
          <p>
            {committedPaths.length > 0
              ? t('researchLab.comparison.committedSelected', { count: committedPaths.length })
              : t('researchLab.comparison.committedAll')}
          </p>
          {!selectionMatchesCommitted ? (
            <p role="status">{t('researchLab.comparison.rerunRequired')}</p>
          ) : null}
        </div>
      ) : null}
      {data ? (
        <>
          <p aria-live="polite" className="km-research-lab-result-count">
            {t('researchLab.comparison.loaded', { count: data.items.length })}
          </p>
          {data.items.length > 0 ? (
            <div className="km-research-lab-result-controls">
              <label>
                <span>{t('analysisPresentation.controls.filter')}</span>
                <span className="km-research-lab-filter-input">
                  <Search aria-hidden="true" size={15} />
                  <input
                    onChange={(event) => setResultFilter(event.currentTarget.value)}
                    type="search"
                    value={resultFilter}
                  />
                </span>
              </label>
              <label>
                <span>{t('analysisPresentation.controls.resultType')}</span>
                <select
                  className="km-select-control"
                  onChange={(event) => setDifferenceFilter(
                    event.currentTarget.value as typeof differenceFilter
                  )}
                  value={differenceFilter}
                >
                  <option value="all">{t('analysisPresentation.controls.allResults')}</option>
                  <option value="added">{t(researchDifferenceKey('added'))}</option>
                  <option value="removed">{t(researchDifferenceKey('removed'))}</option>
                  <option value="changed">{t(researchDifferenceKey('changed'))}</option>
                </select>
              </label>
              <label>
                <span>{t('analysisPresentation.controls.sort')}</span>
                <select
                  className="km-select-control"
                  onChange={(event) => setResultOrder(event.currentTarget.value as typeof resultOrder)}
                  value={resultOrder}
                >
                  <option value="path">{t('analysisPresentation.controls.path')}</option>
                  <option value="difference">{t('analysisPresentation.controls.resultType')}</option>
                  <option value="largest">{t('analysisPresentation.controls.largestFirst')}</option>
                </select>
              </label>
            </div>
          ) : null}
          {visibleFindings.length === 0 ? (
            <p className="km-research-lab-empty">{t(data.items.length === 0
              ? 'researchLab.comparison.empty'
              : 'analysisPresentation.controls.noMatches')}</p>
          ) : (
            <ol aria-label={t('researchLab.comparison.results')} className="km-research-lab-results">
              {visibleFindings.map((finding) => (
                <FindingCard
                  checked={selectedPaths.has(finding.relativePath)}
                  controller={controller}
                  disabled={
                    !selectedPaths.has(finding.relativePath) &&
                    selectedPaths.size >= researchLabMaximumSelectedFiles
                  }
                  finding={finding}
                  key={finding.findingId}
                  onAnnotateFinding={() => onCreateAnnotation({
                    finding: {
                      comparisonFingerprint: data.comparisonFingerprint,
                      findingId: finding.findingId,
                      relativePath: finding.relativePath
                    },
                    kind: 'finding',
                    relativeRange: null,
                    revision,
                    semanticRecord: null,
                    semanticSnapshot: null
                  })}
                  onAnnotateRange={(offset, length) => onCreateAnnotation({
                    finding: null,
                    kind: 'relativeRange',
                    relativeRange: {
                      comparisonFingerprint: data.comparisonFingerprint,
                      length,
                      offset,
                      relativePath: finding.relativePath
                    },
                    revision,
                    semanticRecord: null,
                    semanticSnapshot: null
                  })}
                  onToggle={(checked) => setSelectedPaths((current) => {
                    const next = new Set(current);
                    if (checked) next.add(finding.relativePath);
                    else next.delete(finding.relativePath);
                    return next;
                  })}
                />
              ))}
            </ol>
          )}
          {data.nextCursor && !isWindowCapped ? (
            <button
              className="secondary-button compact-button"
              disabled={controller.isBusy || !selectionMatchesCommitted}
              onClick={() => void controller.loadMore()}
              type="button"
            >
              {controller.comparison.isAppending
                ? t('researchLab.comparison.loadingMore')
                : t('researchLab.comparison.more')}
            </button>
          ) : null}
          {controller.comparison.isAppending ? (
            <LoadingProgress
              className="is-compact"
              label={t('researchLab.comparison.loadingMore')}
            />
          ) : null}
          {isWindowCapped ? (
            <p className="km-research-lab-inline-status" role="status">
              {t('researchLab.comparison.windowLimit', {
                count: researchLabMaximumAccumulatedFindings
              })}
            </p>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function samePathSelection(
  draft: ReadonlySet<string>,
  committed: readonly string[]
) {
  return draft.size === committed.length && committed.every((path) => draft.has(path));
}

function SourceCard({
  controller,
  onPick,
  slot
}: {
  controller: ResearchLabController;
  onPick: () => void;
  slot: 0 | 1;
}) {
  const { t } = useLocalization();
  const source = controller.sources[slot];
  const labelKey = slot === 0 ? 'researchLab.source.a' : 'researchLab.source.b';
  return (
    <article className="km-research-lab-source">
      <strong>{t(labelKey)}</strong>
      <span>{t(source.data
        ? 'researchLab.source.registered'
        : 'researchLab.source.private')}</span>
      {source.error ? (
        <span role="alert">{t(researchErrorKey(source.error))}</span>
      ) : null}
      {source.status === 'loading' ? (
        <LoadingProgress className="is-compact" label={t('semanticExplore.loading')} />
      ) : null}
      <div className="km-research-lab-source-actions">
        <button
          className="secondary-button compact-button"
          disabled={source.status === 'loading'}
          onClick={onPick}
          type="button"
        >
          <FolderOpen aria-hidden="true" size={14} />
          <span>{t(source.data ? 'researchLab.source.replace' : 'researchLab.source.choose')}</span>
        </button>
        {source.data ? (
          <button
            className="secondary-button compact-button"
            disabled={source.status === 'loading'}
            onClick={() => void controller.clearSource(slot)}
            type="button"
          >
            {t('researchLab.source.clear')}
          </button>
        ) : null}
      </div>
    </article>
  );
}

function FindingCard({
  checked,
  controller,
  disabled,
  finding,
  onAnnotateFinding,
  onAnnotateRange,
  onToggle
}: {
  checked: boolean;
  controller: ResearchLabController;
  disabled: boolean;
  finding: ResearchFileFinding;
  onAnnotateFinding: () => void;
  onAnnotateRange: (offset: number, length: number) => void;
  onToggle: (checked: boolean) => void;
}) {
  const { t } = useLocalization();
  const window = controller.byteWindow.findingId === finding.findingId
    ? controller.byteWindow
    : null;
  const firstRange = finding.ranges[0] ?? null;
  const offset = firstRange?.offset ?? 0;
  const maximumLength = Math.max(finding.sourceA.length ?? 0, finding.sourceB.length ?? 0);
  const windowLength = Math.min(
    researchLabMaximumByteWindowLength,
    firstRange?.length ?? Math.max(0, maximumLength - offset)
  );
  return (
    <li>
      <article className="km-research-lab-card">
        <header>
          <div className="km-research-lab-card-title">
            <FileDiff aria-hidden="true" size={17} />
            <div>
              <h4 data-localization-ignore="true">{finding.relativePath}</h4>
              <p>{t(researchDifferenceKey(finding.differenceKind))}</p>
            </div>
          </div>
          <label>
            <input
              aria-label={`${t('researchLab.comparison.includeRanges')}: ${finding.relativePath}`}
              checked={checked}
              className="km-choice-control"
              disabled={disabled}
              onChange={(event) => onToggle(event.target.checked)}
              type="checkbox"
            />
            <span>{t('researchLab.comparison.includeRanges')}</span>
          </label>
        </header>
        <dl className="km-research-lab-facts">
          <div>
            <dt>{t('researchLab.comparison.sourceABytes')}</dt>
            <dd>{formatLength(finding.sourceA.length, t('researchLab.comparison.missing'))}</dd>
          </div>
          <div>
            <dt>{t('researchLab.comparison.sourceBBytes')}</dt>
            <dd>{formatLength(finding.sourceB.length, t('researchLab.comparison.missing'))}</dd>
          </div>
          <div>
            <dt>{t('researchLab.comparison.rangeCoverage')}</dt>
            <dd>{t(researchRangeCoverageKey(finding.rangeCoverage))}</dd>
          </div>
          <div>
            <dt>{t('researchLab.comparison.rangeCount')}</dt>
            <dd>{finding.ranges.length.toLocaleString()}</dd>
          </div>
        </dl>
        <div className="km-research-lab-card-actions">
          <button
            aria-label={`${t('researchLab.byteWindow.open')}: ${finding.relativePath}`}
            className="secondary-button compact-button"
            disabled={windowLength < 1 || controller.isBusy}
            onClick={() => void controller.loadByteWindow(finding, offset, windowLength)}
            type="button"
          >
            <Binary aria-hidden="true" size={14} />
            <span>{t('researchLab.byteWindow.open')}</span>
          </button>
          <button
            aria-label={`${t('researchLab.annotations.addFinding')}: ${finding.relativePath}`}
            className="secondary-button compact-button"
            onClick={onAnnotateFinding}
            type="button"
          >
            <MessageSquarePlus aria-hidden="true" size={14} />
            <span>{t('researchLab.annotations.addFinding')}</span>
          </button>
          {firstRange ? (
            <button
              aria-label={`${t('researchLab.annotations.addRange')}: ${finding.relativePath}`}
              className="secondary-button compact-button"
              onClick={() => onAnnotateRange(firstRange.offset, firstRange.length)}
              type="button"
            >
              {t('researchLab.annotations.addRange')}
            </button>
          ) : null}
        </div>
        {window?.status === 'loading' ? (
          <Status messageKey="researchLab.byteWindow.loading" />
        ) : null}
        {window?.error ? <Status error messageKey={researchErrorKey(window.error)} /> : null}
        {window?.data ? (
          <ByteWindow
            onClose={controller.clearByteWindow}
            offset={window.data.offset}
            sourceA={window.data.sourceA}
            sourceB={window.data.sourceB}
          />
        ) : null}
      </article>
    </li>
  );
}

function ByteWindow({
  offset,
  onClose,
  sourceA,
  sourceB
}: {
  offset: number;
  onClose: () => void;
  sourceA: ResearchByteWindowSide;
  sourceB: ResearchByteWindowSide;
}) {
  const { t } = useLocalization();
  return (
    <section aria-label={t('researchLab.byteWindow.title')}>
      <div className="km-research-lab-panel-heading">
        <div>
          <h4>{t('researchLab.byteWindow.title')}</h4>
          <p>{t('researchLab.byteWindow.ephemeral')}</p>
        </div>
        <button className="secondary-button compact-button" onClick={onClose} type="button">
          {t('researchLab.byteWindow.close')}
        </button>
      </div>
      <div className="km-research-lab-source-grid">
        <ByteSide label={t('researchLab.source.a')} offset={offset} side={sourceA} />
        <ByteSide label={t('researchLab.source.b')} offset={offset} side={sourceB} />
      </div>
    </section>
  );
}

function ByteSide({
  label,
  offset,
  side
}: {
  label: string;
  offset: number;
  side: ResearchByteWindowSide;
}) {
  const { t } = useLocalization();
  return (
    <div>
      <strong>{label}</strong>
      {side.exists ? (
        <pre
          aria-label={`${t('researchLab.byteWindow.title')}: ${label}`}
          className="km-research-lab-byte-window"
          data-localization-ignore="true"
          tabIndex={0}
        >
          {formatHexWindow(side.bytesBase64!, offset)}
        </pre>
      ) : (
        <p>{t('researchLab.comparison.missing')}</p>
      )}
    </div>
  );
}

function Status({
  compact = false,
  error = false,
  messageKey
}: {
  compact?: boolean;
  error?: boolean;
  messageKey: string;
}) {
  const { t } = useLocalization();
  if (!error) {
    return (
      <div className="km-research-lab-inline-status">
        <LoadingProgress className={compact ? 'is-compact' : undefined} label={t(messageKey)} />
      </div>
    );
  }
  return (
    <div
      aria-live="polite"
      className="km-research-lab-inline-status"
      role="alert"
    >
      <span>{t(messageKey)}</span>
    </div>
  );
}

function formatLength(length: number | null, missing: string) {
  return length === null ? missing : length.toLocaleString();
}

function formatHexWindow(base64: string, initialOffset: number) {
  const binary = atob(base64);
  const bytes = Array.from(binary, (character) => character.charCodeAt(0));
  if (bytes.length === 0) return '';
  const lines: string[] = [];
  for (let index = 0; index < bytes.length; index += 16) {
    const slice = bytes.slice(index, index + 16);
    const address = (initialOffset + index).toString(16).padStart(8, '0');
    const hex = slice.map((value) => value.toString(16).padStart(2, '0')).join(' ');
    const text = slice.map((value) => value >= 32 && value <= 126
      ? String.fromCharCode(value)
      : '.').join('');
    lines.push(`${address}  ${hex.padEnd(47, ' ')}  ${text}`);
  }
  return lines.join('\n');
}
