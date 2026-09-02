/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ChevronLeft,
  ChevronRight,
  FileWarning,
  MapPinned,
  Search
} from 'lucide-react';
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent
} from 'react';
import type { EditSession } from '../../bridge/contracts';
import type {
  HabitatCoordinateChoice,
  HabitatCoordinateRecord,
  HabitatCoordinatesQuery,
  HabitatCoordinatesWorkflow,
  HabitatRowBinding
} from '../../bridge/habitatCoordinatesContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import {
  getNextOutstandingEditorDraftKey,
  reconcileSourceBackedDraft
} from '../../components/localEditorDraftState';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import {
  clearStagedHabitatCoordinateDraftValue,
  createHabitatCoordinateDraftKey,
  createHabitatCoordinatesQueryKey,
  reconcileHabitatSearchDraftAfterAcceptedQuery,
  setHabitatCoordinateDraftValue
} from './habitatCoordinateDraftState';
import './HabitatCoordinatesSection.css';

const regionOrder = ['paldea', 'kitakami', 'blueberry'] as const;
type HabitatRegion = (typeof regionOrder)[number];

export type HabitatCoordinateStageInput = {
  binding: HabitatRowBinding;
  coordinate: HabitatCoordinateChoice;
  query: HabitatCoordinatesQuery;
  region: HabitatCoordinatesQuery['region'];
};

type HabitatCoordinatesSectionProps = {
  editSession: EditSession | null;
  isLoading: boolean;
  isStaging: boolean;
  onDirtyStateChange?: (isDirty: boolean) => void;
  onLoadQuery: (query: HabitatCoordinatesQuery) => Promise<boolean>;
  onOpenChanges: () => void;
  onStageCoordinate: (
    input: HabitatCoordinateStageInput,
    isSubmittedDraftCurrent: () => boolean
  ) => Promise<boolean>;
  panelOutput: WorkflowPanelOutput;
  workflow: HabitatCoordinatesWorkflow | null;
};

type HabitatCoordinateDraftReviewTarget = {
  query: HabitatCoordinatesQuery;
  rowKey: string;
  value: string;
};

export function HabitatCoordinatesSection({
  editSession,
  isLoading,
  isStaging,
  onDirtyStateChange,
  onLoadQuery,
  onOpenChanges,
  onStageCoordinate,
  panelOutput,
  workflow
}: HabitatCoordinatesSectionProps) {
  const { t } = useLocalization();
  const page = workflow?.page ?? null;
  const initialSearchSource = page?.search ?? '';
  const [searchDraft, setSearchDraft] = useCoalescedTextInputState(initialSearchSource);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [coordinateDrafts, setCoordinateDrafts] = useState<Record<string, string>>({});
  const [draftReviewTargets, setDraftReviewTargets] = useState<
    Record<string, HabitatCoordinateDraftReviewTarget>
  >({});
  const [confirmedUnavailableDraftKeys, setConfirmedUnavailableDraftKeys] = useState<
    ReadonlySet<string>
  >(() => new Set());
  const [feedback, setFeedback] = useState<'success' | 'error' | null>(null);
  const feedbackRef = useRef<HTMLDivElement | null>(null);
  const pendingReviewTargetRef = useRef<{
    draftKey: string;
    queryKey: string;
    rowKey: string;
  } | null>(null);
  const preservedReviewSearchRef = useRef<{
    queryKey: string;
    value: string;
  } | null>(null);
  const reviewLoadPendingRef = useRef(false);
  const reviewFocusPendingRef = useRef(false);
  const searchDraftRef = useRef(searchDraft);
  const searchSourceRef = useRef(initialSearchSource);
  const coordinateDraftsRef = useRef(coordinateDrafts);
  const draftReviewTargetsRef = useRef(draftReviewTargets);
  searchDraftRef.current = searchDraft;
  coordinateDraftsRef.current = coordinateDrafts;
  draftReviewTargetsRef.current = draftReviewTargets;
  const regionTabRefs = useRef<Record<HabitatRegion, HTMLButtonElement | null>>({
    blueberry: null,
    kitakami: null,
    paldea: null
  });
  const selectedRegion = workflow?.regions.find((region) => region.region === page?.region) ?? null;
  const selectedRecord = page?.records.find((record) => rowKey(record) === selectedKey) ?? null;
  const pendingCount = editSession?.pendingEdits.filter(
    (edit) => edit.domain === 'workflow.habitatCoordinates'
  ).length ?? 0;
  const coordinateOptions = selectedRegion?.coordinateChoices ?? [];
  const coordinateSet = useMemo(
    () => new Set(coordinateOptions.map(coordinateKey)),
    [coordinateOptions]
  );
  const effectiveCoordinate = selectedRecord?.stagedCoordinate ?? (
    selectedRecord ? { x: selectedRecord.x, y: selectedRecord.y } : null
  );
  const coordinateDraftKey = selectedRecord && page
    ? createHabitatCoordinateDraftKey(page.region, selectedRecord)
    : null;
  const sourceCoordinateValue = effectiveCoordinate ? coordinateKey(effectiveCoordinate) : '';
  const coordinateDraft = coordinateDraftKey
    ? coordinateDrafts[coordinateDraftKey] ?? sourceCoordinateValue
    : '';
  const outstandingDraftKeys = Object.keys(coordinateDrafts);
  const outstandingDraftCount = outstandingDraftKeys.length;
  const unavailableDraftKeys = outstandingDraftKeys.filter(
    (draftKey) =>
      !draftReviewTargets[draftKey] || confirmedUnavailableDraftKeys.has(draftKey)
  );
  const unavailableDraftKeySet = new Set(unavailableDraftKeys);
  const reviewableDraftKeys = outstandingDraftKeys.filter(
    (draftKey) => !unavailableDraftKeySet.has(draftKey)
  );
  const nextDraftKey = getNextOutstandingEditorDraftKey(
    reviewableDraftKeys,
    coordinateDraftKey
  );
  const nextDraftTarget = nextDraftKey ? draftReviewTargets[nextDraftKey] ?? null : null;
  const coordinateDraftContextRef = useRef({
    key: coordinateDraftKey,
    value: coordinateDraft
  });
  coordinateDraftContextRef.current = {
    key: coordinateDraftKey,
    value: coordinateDraft
  };
  const nextOffset = page ? page.offset + page.records.length : 0;
  const canGoNext = page !== null && nextOffset < page.totalMatches;
  const canGoPrevious = page !== null && page.offset > 0;
  const selectedDraft = parseCoordinate(coordinateDraft);
  const canStage =
    selectedRegion?.canStage === true &&
    selectedRecord !== null &&
    selectedDraft !== null &&
    coordinateSet.has(coordinateDraft) &&
    effectiveCoordinate !== null &&
    coordinateKey(selectedDraft) !== coordinateKey(effectiveCoordinate) &&
    !isLoading &&
    !isStaging;
  usePublishCommonEditorError({
    domain: 'workflow.habitatCoordinates',
    field: 'coordinate',
    message: feedback === 'error'
      ? t('habitatCoordinates.feedback.error')
      : null
  });

  useEffect(() => {
    if (page) {
      const pageQueryKey = createHabitatCoordinatesQueryKey(queryFromPage(page, {}));
      const preservedReviewSearch = preservedReviewSearchRef.current;
      if (preservedReviewSearch?.queryKey === pageQueryKey) {
        preservedReviewSearchRef.current = null;
        searchSourceRef.current = page.search;
        searchDraftRef.current = preservedReviewSearch.value;
        setSearchDraft(preservedReviewSearch.value);
        return;
      }

      const previousSource = searchSourceRef.current;
      searchSourceRef.current = page.search;
      const nextDraft = reconcileSourceBackedDraft(
        searchDraftRef.current,
        previousSource,
        page.search,
        Object.is
      );
      searchDraftRef.current = nextDraft;
      setSearchDraft(nextDraft);
    }
  }, [page?.region, page?.search]);

  useEffect(() => {
    const pendingTarget = pendingReviewTargetRef.current;
    if (
      pendingTarget &&
      page &&
      createHabitatCoordinatesQueryKey(queryFromPage(page, {})) === pendingTarget.queryKey
    ) {
      pendingReviewTargetRef.current = null;
      const matchingRecordExists = page.records.some(
        (record) => rowKey(record) === pendingTarget.rowKey
      );
      const currentTarget = draftReviewTargetsRef.current[pendingTarget.draftKey];
      const isPendingDraftCurrent =
        Object.hasOwn(coordinateDraftsRef.current, pendingTarget.draftKey) &&
        currentTarget !== undefined &&
        createHabitatCoordinatesQueryKey(currentTarget.query) === pendingTarget.queryKey &&
        currentTarget.rowKey === pendingTarget.rowKey;
      if (matchingRecordExists && isPendingDraftCurrent) {
        setConfirmedUnavailableDraftKeys((currentKeys) =>
          removeSetValue(currentKeys, pendingTarget.draftKey)
        );
        reviewFocusPendingRef.current = true;
        setSelectedKey(pendingTarget.rowKey);
        return;
      }
      if (isPendingDraftCurrent) {
        setConfirmedUnavailableDraftKeys((currentKeys) =>
          addSetValue(currentKeys, pendingTarget.draftKey)
        );
      }
    }

    if (selectedKey && page?.records.some((record) => rowKey(record) === selectedKey)) {
      return;
    }
    setSelectedKey(page?.records[0] ? rowKey(page.records[0]) : null);
  }, [page?.limit, page?.offset, page?.records, page?.region, page?.search, selectedKey]);

  useEffect(() => {
    setFeedback(null);
    if (!reviewFocusPendingRef.current) {
      return;
    }

    reviewFocusPendingRef.current = false;
    const frame = requestAnimationFrame(() => {
      document.getElementById('habitat-coordinate-value')?.focus({ preventScroll: true });
    });
    return () => cancelAnimationFrame(frame);
  }, [coordinateDraftKey]);

  useEffect(() => {
    onDirtyStateChange?.(Object.keys(coordinateDrafts).length > 0);
  }, [coordinateDrafts, onDirtyStateChange]);

  useEffect(() => {
    setConfirmedUnavailableDraftKeys((currentKeys) => {
      const nextKeys = new Set(
        [...currentKeys].filter((draftKey) => Object.hasOwn(coordinateDrafts, draftKey))
      );
      return setsEqual(currentKeys, nextKeys) ? currentKeys : nextKeys;
    });
  }, [coordinateDrafts]);

  useEffect(() => {
    if (!page) {
      return;
    }
    const visibleRowKeys = new Set(page.records.map(rowKey));
    setConfirmedUnavailableDraftKeys((currentKeys) => {
      const nextKeys = new Set(
        [...currentKeys].filter((draftKey) => {
          const target = draftReviewTargetsRef.current[draftKey];
          return !(
            target &&
            target.query.region === page.region &&
            visibleRowKeys.has(target.rowKey)
          );
        })
      );
      return setsEqual(currentKeys, nextKeys) ? currentKeys : nextKeys;
    });
  }, [page?.records, page?.region]);

  useEffect(() => () => onDirtyStateChange?.(false), [onDirtyStateChange]);

  useEffect(() => {
    if (feedback) {
      feedbackRef.current?.focus();
    }
  }, [feedback]);

  const loadQuery = async (
    query: HabitatCoordinatesQuery,
    options: { preserveSearchDraft?: boolean } = {}
  ) => {
    setFeedback(null);
    const submittedSearch = {
      draft: searchDraftRef.current,
      source: searchSourceRef.current
    };
    const queryKey = createHabitatCoordinatesQueryKey(query);
    const shouldPreserveSearchAcrossLoad =
      options.preserveSearchDraft === true &&
      (
        !page ||
        page.region !== query.region ||
        page.search !== query.search
      );
    if (shouldPreserveSearchAcrossLoad) {
      preservedReviewSearchRef.current = {
        queryKey,
        value: submittedSearch.draft
      };
    }
    const didLoad = await onLoadQuery(query);
    if (!didLoad) {
      if (preservedReviewSearchRef.current?.queryKey === queryKey) {
        preservedReviewSearchRef.current = null;
      }
      return false;
    }

    searchSourceRef.current = query.search;
    if (!options.preserveSearchDraft) {
      const nextDraft = reconcileHabitatSearchDraftAfterAcceptedQuery(
        searchDraftRef.current,
        submittedSearch,
        query.search
      );
      searchDraftRef.current = nextDraft;
      setSearchDraft(nextDraft);
    }
    return true;
  };

  const loadRegion = (region: HabitatRegion) => {
    if (!workflow || isLoading) {
      return;
    }
    void loadQuery({
      limit: page?.limit ?? 50,
      offset: 0,
      region,
      search: ''
    });
  };

  const handleRegionKeyDown = (
    event: KeyboardEvent<HTMLButtonElement>,
    region: HabitatRegion
  ) => {
    if (!workflow || isLoading) {
      return;
    }
    const currentIndex = regionOrder.indexOf(region);
    let targetIndex: number | null = null;
    switch (event.key) {
      case 'ArrowLeft':
        targetIndex = (currentIndex - 1 + regionOrder.length) % regionOrder.length;
        break;
      case 'ArrowRight':
        targetIndex = (currentIndex + 1) % regionOrder.length;
        break;
      case 'Home':
        targetIndex = 0;
        break;
      case 'End':
        targetIndex = regionOrder.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    if (targetIndex === null) {
      return;
    }
    const targetRegion = regionOrder[targetIndex];
    regionTabRefs.current[targetRegion]?.focus();
    loadRegion(targetRegion);
  };

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    if (!page) {
      return;
    }
    void loadQuery(queryFromPage(page, {
      offset: 0,
      search: searchDraftRef.current.trim().slice(0, 80)
    }));
  };

  const stage = async () => {
    if (!canStage || !page || !selectedRecord || !selectedDraft || !coordinateDraftKey) {
      return;
    }
    const stagedDraftKey = coordinateDraftKey;
    const stagedDraftValue = coordinateDraft;
    const isSubmittedDraftCurrent = () =>
      coordinateDraftContextRef.current.key === stagedDraftKey &&
      coordinateDraftContextRef.current.value === stagedDraftValue;
    const succeeded = await onStageCoordinate(
      {
        binding: selectedRecord.binding,
        coordinate: selectedDraft,
        query: queryFromPage(page, {}),
        region: page.region
      },
      isSubmittedDraftCurrent
    );
    if (isSubmittedDraftCurrent()) {
      setFeedback(succeeded ? 'success' : 'error');
    }
    if (succeeded) {
      setCoordinateDrafts((currentDrafts) =>
        clearStagedHabitatCoordinateDraftValue(
          currentDrafts,
          stagedDraftKey,
          stagedDraftValue
        )
      );
      setDraftReviewTargets((currentTargets) =>
        currentTargets[stagedDraftKey]?.value === stagedDraftValue
          ? removeDraftReviewTarget(currentTargets, stagedDraftKey)
          : currentTargets
      );
      setConfirmedUnavailableDraftKeys((currentKeys) =>
        removeSetValue(currentKeys, stagedDraftKey)
      );
    }
  };

  const updateCoordinateDraft = (value: string) => {
    if (!coordinateDraftKey || !page || !selectedRecord) {
      return;
    }
    setFeedback(null);
    setCoordinateDrafts((currentDrafts) =>
      setHabitatCoordinateDraftValue(
        currentDrafts,
        coordinateDraftKey,
        value,
        sourceCoordinateValue
      )
    );
    setDraftReviewTargets((currentTargets) =>
      value === sourceCoordinateValue
        ? removeDraftReviewTarget(currentTargets, coordinateDraftKey)
        : {
            ...currentTargets,
            [coordinateDraftKey]: {
              query: queryFromPage(page, {}),
              rowKey: rowKey(selectedRecord),
              value
            }
          }
    );
    setConfirmedUnavailableDraftKeys((currentKeys) =>
      removeSetValue(currentKeys, coordinateDraftKey)
    );
  };

  const reviewNextDraft = async () => {
    if (
      !nextDraftKey ||
      !nextDraftTarget ||
      isLoading ||
      isStaging ||
      reviewLoadPendingRef.current
    ) {
      return;
    }

    const targetQueryKey = createHabitatCoordinatesQueryKey(nextDraftTarget.query);
    const currentQueryKey = page
      ? createHabitatCoordinatesQueryKey(queryFromPage(page, {}))
      : null;
    if (
      currentQueryKey === targetQueryKey &&
      page?.records.some((record) => rowKey(record) === nextDraftTarget.rowKey)
    ) {
      reviewFocusPendingRef.current = true;
      setSelectedKey(nextDraftTarget.rowKey);
      return;
    }

    reviewLoadPendingRef.current = true;
    try {
      pendingReviewTargetRef.current = {
        draftKey: nextDraftKey,
        queryKey: targetQueryKey,
        rowKey: nextDraftTarget.rowKey
      };
      const didLoad = await loadQuery(nextDraftTarget.query, {
        preserveSearchDraft: true
      });
      if (
        !didLoad &&
        pendingReviewTargetRef.current?.draftKey === nextDraftKey
      ) {
        pendingReviewTargetRef.current = null;
      }
    } finally {
      reviewLoadPendingRef.current = false;
    }
  };

  const discardUnavailableDrafts = () => {
    const capturedUnavailableDraftKeys = [...unavailableDraftKeys];
    if (
      capturedUnavailableDraftKeys.length === 0 ||
      !window.confirm(
        t('editorDrafts.confirmDiscardUnavailable.habitatCoordinates', {
          count: capturedUnavailableDraftKeys.length
        })
      )
    ) {
      return;
    }

    const capturedUnavailableDraftKeySet = new Set(capturedUnavailableDraftKeys);
    if (
      pendingReviewTargetRef.current &&
      capturedUnavailableDraftKeySet.has(pendingReviewTargetRef.current.draftKey)
    ) {
      pendingReviewTargetRef.current = null;
    }
    setCoordinateDrafts((currentDrafts) =>
      removeRecordKeys(currentDrafts, capturedUnavailableDraftKeySet)
    );
    setDraftReviewTargets((currentTargets) =>
      removeRecordKeys(currentTargets, capturedUnavailableDraftKeySet)
    );
    setConfirmedUnavailableDraftKeys((currentKeys) =>
      removeSetValues(currentKeys, capturedUnavailableDraftKeySet)
    );
  };

  return (
    <div className="habitat-coordinates-workspace">
      <section aria-labelledby="habitat-coordinates-heading" className="panel wide-panel">
        <div className="panel-heading">
          <MapPinned aria-hidden="true" size={18} />
          <div>
            <h2 id="habitat-coordinates-heading">{t('habitatCoordinates.title')}</h2>
            <p>{t('habitatCoordinates.description')}</p>
          </div>
        </div>

        <div className="habitat-coordinates-metrics">
          <Metric
            label={t('habitatCoordinates.metric.build')}
            value={workflow?.supportedBuild ?? t('habitatCoordinates.notLoaded')}
            valueIsRaw={workflow !== null}
          />
          <Metric
            label={t('habitatCoordinates.metric.regions')}
            value={workflow ? `${workflow.stats.readyRegionCount}/${workflow.stats.regionCount}` : '0/3'}
            valueIsRaw
          />
          <Metric
            label={t('habitatCoordinates.metric.rows')}
            value={workflow?.stats.totalRowCount.toString() ?? '0'}
            valueIsRaw
          />
          <Metric
            label={t('habitatCoordinates.metric.staged')}
            value={pendingCount.toString()}
            valueIsRaw
          />
        </div>

        <div
          aria-label={t('habitatCoordinates.regions')}
          aria-orientation="horizontal"
          className="habitat-region-tabs"
          role="tablist"
        >
          {regionOrder.map((region) => {
            const state = workflow?.regions.find((candidate) => candidate.region === region);
            const selected = page?.region === region;
            return (
              <button
                aria-controls="habitat-coordinate-list"
                aria-selected={selected}
                className="secondary-button habitat-region-tab"
                disabled={!workflow || isLoading}
                id={`habitat-region-tab-${region}`}
                key={region}
                onClick={() => loadRegion(region)}
                onKeyDown={(event) => handleRegionKeyDown(event, region)}
                ref={(element) => {
                  regionTabRefs.current[region] = element;
                }}
                role="tab"
                tabIndex={selected ? 0 : -1}
                type="button"
              >
                <span>{t(`habitatCoordinates.region.${region}`)}</span>
                {state && !state.canStage ? (
                  <>
                    <FileWarning aria-hidden="true" size={15} />
                    <span className="sr-only">
                      {t(`habitatCoordinates.regionBlocked.${region}`)}
                    </span>
                  </>
                ) : null}
              </button>
            );
          })}
        </div>

        {selectedRegion && !selectedRegion.canStage ? (
          <div className="habitat-region-blocked" role="alert">
            <FileWarning aria-hidden="true" size={18} />
            <p>{t(`habitatCoordinates.regionBlocked.${selectedRegion.region}`)}</p>
          </div>
        ) : null}

        <div className="habitat-coordinate-cards">
          <section aria-labelledby="habitat-coordinate-list-heading" className="habitat-coordinate-card">
            <div className="habitat-coordinate-card-heading">
              <h3 id="habitat-coordinate-list-heading">{t('habitatCoordinates.list.title')}</h3>
              <span data-localization-ignore="true">{page?.totalMatches ?? 0}</span>
            </div>

            <form
              aria-busy={isLoading}
              className="habitat-coordinate-search"
              onSubmit={submitSearch}
              role="search"
            >
              <label className="sr-only" htmlFor="habitat-coordinate-search">
                {t('habitatCoordinates.search.label')}
              </label>
              <div className="search-box habitat-coordinate-search-field">
                <Search aria-hidden="true" size={16} />
                <input
                  disabled={!workflow}
                  id="habitat-coordinate-search"
                  maxLength={80}
                  onChange={(event) => {
                    searchDraftRef.current = event.target.value;
                    setSearchDraft(event.target.value);
                  }}
                  placeholder={t('habitatCoordinates.search.placeholder')}
                  type="search"
                  value={searchDraft}
                />
              </div>
              <button
                className="secondary-button habitat-coordinate-search-action"
                disabled={!workflow || isLoading}
                type="submit"
              >
                <span>{t('habitatCoordinates.search.action')}</span>
              </button>
            </form>

            <div
              aria-busy={isLoading}
              aria-labelledby={page ? `habitat-region-tab-${page.region}` : undefined}
              className="habitat-coordinate-list"
              id="habitat-coordinate-list"
              role="tabpanel"
            >
              {page?.records.map((record) => {
                const selected = rowKey(record) === selectedKey;
                const effective = record.stagedCoordinate ?? { x: record.x, y: record.y };
                const formName = record.formName ?? t(
                  record.binding.formNo === 0
                    ? 'habitatCoordinates.row.form.standard'
                    : 'habitatCoordinates.row.form.number',
                  { number: record.binding.formNo }
                );
                return (
                  <button
                    aria-pressed={selected}
                    className="habitat-coordinate-row"
                    disabled={isLoading}
                    key={rowKey(record)}
                    onClick={() => setSelectedKey(rowKey(record))}
                    type="button"
                  >
                    <span className="habitat-coordinate-row-identity">
                      <strong data-localization-ignore="true">{record.speciesName}</strong>
                      <small>
                        <span data-localization-ignore={record.formName ? 'true' : undefined}>
                          {formName}
                        </span>
                        <span aria-hidden="true"> · </span>
                        <span className="sr-only">, </span>
                        <span>
                          {t('habitatCoordinates.row.pokedexNumber', {
                            number: record.binding.devNo
                          })}
                        </span>
                      </small>
                    </span>
                    <span className="habitat-coordinate-row-meta">
                      <span className="habitat-coordinate-row-cell">
                        <small>
                          {t('habitatCoordinates.row.cell', {
                            region: t(`habitatCoordinates.region.${page.region}`)
                          })}
                        </small>
                        <span>
                          {t('habitatCoordinates.row.coordinate', {
                            x: effective.x,
                            y: effective.y
                          })}
                        </span>
                      </span>
                      {record.isStaged ? <strong>{t('habitatCoordinates.staged')}</strong> : null}
                    </span>
                    <span className="sr-only">
                      {t('habitatCoordinates.editor.version')}:{' '}
                      {record.binding.versionA
                        ? t('habitatCoordinates.version.scarlet')
                        : null}
                      {record.binding.versionA && record.binding.versionB ? ', ' : null}
                      {record.binding.versionB
                        ? t('habitatCoordinates.version.violet')
                        : null}
                      {'. '}
                      {t('habitatCoordinates.editor.occurrence')}:{' '}
                      {t('habitatCoordinates.editor.occurrenceValue', {
                        group: record.binding.outerGroupOccurrence + 1,
                        row: record.binding.rowOccurrence + 1
                      })}
                    </span>
                  </button>
                );
              })}
              {page && page.records.length === 0 ? (
                <p className="empty-copy">{t('habitatCoordinates.list.empty')}</p>
              ) : null}
            </div>

            <div className="habitat-coordinate-pagination">
              <button
                className="secondary-button compact-button"
                disabled={!canGoPrevious || isLoading || !page}
                onClick={() => page && void loadQuery(queryFromPage(page, {
                  offset: Math.max(0, page.offset - page.limit)
                }))}
                type="button"
              >
                <ChevronLeft aria-hidden="true" size={16} />
                <span>{t('habitatCoordinates.page.previous')}</span>
              </button>
              <span aria-atomic="true" aria-live="polite">
                {t('habitatCoordinates.page.range', {
                  end: page ? Math.min(page.totalMatches, page.offset + page.records.length) : 0,
                  start: page && page.totalMatches > 0 ? page.offset + 1 : 0,
                  total: page?.totalMatches ?? 0
                })}
              </span>
              <button
                className="secondary-button compact-button"
                disabled={!canGoNext || isLoading || !page}
                onClick={() => page && void loadQuery(queryFromPage(page, { offset: nextOffset }))}
                type="button"
              >
                <span>{t('habitatCoordinates.page.next')}</span>
                <ChevronRight aria-hidden="true" size={16} />
              </button>
            </div>
          </section>

          <section aria-labelledby="habitat-coordinate-editor-heading" className="habitat-coordinate-card">
            <div className="habitat-coordinate-card-heading">
              <h3 id="habitat-coordinate-editor-heading">{t('habitatCoordinates.editor.title')}</h3>
            </div>
            {selectedRecord && selectedRegion ? (
              <div className="habitat-coordinate-editor">
                <dl>
                  <div>
                    <dt>{t('habitatCoordinates.editor.pokemon')}</dt>
                    <dd data-localization-ignore="true">{selectedRecord.speciesName}</dd>
                  </div>
                  <div>
                    <dt>{t('habitatCoordinates.editor.form')}</dt>
                    <dd data-localization-ignore={selectedRecord.formName ? 'true' : undefined}>
                      {selectedRecord.formName ?? t(
                        selectedRecord.binding.formNo === 0
                          ? 'habitatCoordinates.row.form.standard'
                          : 'habitatCoordinates.row.form.number',
                        { number: selectedRecord.binding.formNo }
                      )}
                    </dd>
                  </div>
                  <div>
                    <dt>{t('habitatCoordinates.editor.identity')}</dt>
                    <dd>
                      {t('habitatCoordinates.editor.identityValue', {
                        form: selectedRecord.binding.formNo,
                        species: selectedRecord.binding.devNo
                      })}
                    </dd>
                  </div>
                  <div>
                    <dt>{t('habitatCoordinates.editor.version')}</dt>
                    <dd>
                      {selectedRecord.binding.versionA
                        ? t('habitatCoordinates.version.scarlet')
                        : null}
                      {selectedRecord.binding.versionA && selectedRecord.binding.versionB ? ' · ' : null}
                      {selectedRecord.binding.versionB
                        ? t('habitatCoordinates.version.violet')
                        : null}
                    </dd>
                  </div>
                  <div>
                    <dt>{t('habitatCoordinates.editor.occurrence')}</dt>
                    <dd>
                      {t('habitatCoordinates.editor.occurrenceValue', {
                        group: selectedRecord.binding.outerGroupOccurrence + 1,
                        row: selectedRecord.binding.rowOccurrence + 1
                      })}
                    </dd>
                  </div>
                  <div>
                    <dt>{t('habitatCoordinates.editor.source')}</dt>
                    <dd data-localization-ignore="true">{selectedRegion.sourceFile}</dd>
                  </div>
                </dl>

                <label className="field-label" htmlFor="habitat-coordinate-value">
                  {t('habitatCoordinates.editor.coordinate')}
                </label>
                <select
                  aria-describedby="habitat-coordinate-observed-hint"
                  className="km-select-control"
                  disabled={!selectedRegion.canStage}
                  id="habitat-coordinate-value"
                  onChange={(event) => updateCoordinateDraft(event.target.value)}
                  value={coordinateDraft}
                >
                  {coordinateOptions.map((coordinate) => (
                    <option key={coordinateKey(coordinate)} value={coordinateKey(coordinate)}>
                      {t('habitatCoordinates.editor.coordinateOption', {
                        x: coordinate.x,
                        y: coordinate.y
                      })}
                    </option>
                  ))}
                </select>
                <p className="field-hint" id="habitat-coordinate-observed-hint">
                  {t('habitatCoordinates.editor.observedOnly', { count: coordinateOptions.length })}
                </p>

                <p aria-live="polite" className="field-hint">
                  {t('habitatCoordinates.editor.draftSummary', {
                    count: outstandingDraftCount
                  })}{'; '}
                  {t('editorDrafts.summary.unavailable', {
                    count: unavailableDraftKeys.length
                  })}
                </p>
                <div className="button-row habitat-coordinate-actions">
                  <button
                    className="primary-button"
                    aria-busy={isStaging}
                    disabled={!canStage}
                    onClick={() => void stage()}
                    type="button"
                  >
                    {isStaging
                      ? t('habitatCoordinates.editor.staging')
                      : t('habitatCoordinates.editor.stage')}
                  </button>
                  {nextDraftTarget ? (
                    <button
                      className="secondary-button"
                      disabled={isLoading || isStaging}
                      onClick={() => void reviewNextDraft()}
                      type="button"
                    >
                      {t('habitatCoordinates.editor.reviewNextDraft')}
                    </button>
                  ) : null}
                  {unavailableDraftKeys.length > 0 ? (
                    <button
                      className="danger-button"
                      disabled={isLoading || isStaging}
                      onClick={discardUnavailableDrafts}
                      type="button"
                    >
                      {t('editorDrafts.discardUnavailable')}
                    </button>
                  ) : null}
                  {pendingCount > 0 ? (
                    <button className="secondary-button" onClick={onOpenChanges} type="button">
                      {t('habitatCoordinates.editor.review', { count: pendingCount })}
                    </button>
                  ) : null}
                </div>
              </div>
            ) : (
              <>
                <p className="empty-copy">{t('habitatCoordinates.editor.noSelection')}</p>
                <p aria-live="polite" className="field-hint">
                  {t('habitatCoordinates.editor.draftSummary', {
                    count: outstandingDraftCount
                  })}{'; '}
                  {t('editorDrafts.summary.unavailable', {
                    count: unavailableDraftKeys.length
                  })}
                </p>
                {nextDraftTarget || unavailableDraftKeys.length > 0 ? (
                  <div className="button-row habitat-coordinate-actions">
                    {nextDraftTarget ? (
                      <button
                        className="secondary-button"
                        disabled={isLoading || isStaging}
                        onClick={() => void reviewNextDraft()}
                        type="button"
                      >
                        {t('habitatCoordinates.editor.reviewNextDraft')}
                      </button>
                    ) : null}
                    {unavailableDraftKeys.length > 0 ? (
                      <button
                        className="danger-button"
                        disabled={isLoading || isStaging}
                        onClick={discardUnavailableDrafts}
                        type="button"
                      >
                        {t('editorDrafts.discardUnavailable')}
                      </button>
                    ) : null}
                  </div>
                ) : null}
              </>
            )}
          </section>
        </div>

        {feedback ? (
          <div
            className={`action-feedback ${feedback}`}
            ref={feedbackRef}
            role={feedback === 'error' ? 'alert' : 'status'}
            tabIndex={-1}
          >
            {t(`habitatCoordinates.feedback.${feedback}`)}
          </div>
        ) : null}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </div>
  );
}

function coordinateKey(coordinate: HabitatCoordinateChoice): string {
  return `${coordinate.x},${coordinate.y}`;
}

function parseCoordinate(value: string): HabitatCoordinateChoice | null {
  const match = /^(-?\d+),(-?\d+)$/u.exec(value);
  if (!match) {
    return null;
  }
  const x = Number(match[1]);
  const y = Number(match[2]);
  return Number.isSafeInteger(x) && Number.isSafeInteger(y) ? { x, y } : null;
}

function removeDraftReviewTarget(
  targets: Record<string, HabitatCoordinateDraftReviewTarget>,
  key: string
) {
  if (!(key in targets)) {
    return targets;
  }

  const nextTargets = { ...targets };
  delete nextTargets[key];
  return nextTargets;
}

function removeRecordKeys<T>(records: Record<string, T>, keys: ReadonlySet<string>) {
  const nextRecords = Object.fromEntries(
    Object.entries(records).filter(([key]) => !keys.has(key))
  ) as Record<string, T>;
  return Object.keys(nextRecords).length === Object.keys(records).length
    ? records
    : nextRecords;
}

function addSetValue(values: ReadonlySet<string>, value: string) {
  if (values.has(value)) {
    return values;
  }
  const nextValues = new Set(values);
  nextValues.add(value);
  return nextValues;
}

function removeSetValue(values: ReadonlySet<string>, value: string) {
  if (!values.has(value)) {
    return values;
  }
  const nextValues = new Set(values);
  nextValues.delete(value);
  return nextValues;
}

function removeSetValues(values: ReadonlySet<string>, removedValues: ReadonlySet<string>) {
  const nextValues = new Set([...values].filter((value) => !removedValues.has(value)));
  return setsEqual(values, nextValues) ? values : nextValues;
}

function setsEqual(left: ReadonlySet<string>, right: ReadonlySet<string>) {
  return left.size === right.size && [...left].every((value) => right.has(value));
}

function rowKey(record: HabitatCoordinateRecord): string {
  const binding = record.binding;
  return JSON.stringify([
    binding.sourceFile,
    binding.devNo,
    binding.formNo,
    binding.outerGroupOccurrence,
    binding.rowOccurrence
  ]);
}

function queryFromPage(
  page: HabitatCoordinatesWorkflow['page'],
  overrides: Partial<HabitatCoordinatesQuery>
): HabitatCoordinatesQuery {
  return {
    limit: overrides.limit ?? page.limit,
    offset: overrides.offset ?? page.offset,
    region: overrides.region ?? page.region,
    search: overrides.search ?? page.search
  };
}
