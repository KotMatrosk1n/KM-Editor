/* SPDX-License-Identifier: GPL-3.0-only */

import {
  AlertTriangle,
  CheckCircle2,
  CircleAlert,
  Info,
  Route,
  Save,
  Search,
  X
} from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type {
  ApiDiagnostic,
  DynamaxAdventureRecord,
  DynamaxAdventureSaveSeedResult,
  DynamaxAdventureSeedPlan,
  DynamaxAdventureSeedSearch,
  PlanDynamaxAdventureSeedRequest,
  SearchDynamaxAdventureSeedRequest
} from '../../bridge/contracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { DiagnosticsSection } from '../../components/workflowPanels';
import { useModalDialog } from '../../components/useModalDialog';
import { desktopErrorCodes } from '../../errorCodes';
import { useLocalization, type LocalizationContextValue } from '../../localization';
import { toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';
import { parseBoundedWholeNumberDraft } from '../gameplayInputDrafts';

type PlanSeedInput = Omit<PlanDynamaxAdventureSeedRequest, 'paths'>;
type SearchSeedInput = Omit<SearchDynamaxAdventureSeedRequest, 'paths'>;

export type DynamaxAdventureSeedPlannerProps = {
  armCriticalWriteGuard: () => Promise<boolean>;
  encounters: readonly DynamaxAdventureRecord[];
  hasConfiguredSave: boolean;
  onPlanSeed: (input: PlanSeedInput) => Promise<DynamaxAdventureSeedPlan>;
  onSaveBusyChange: (isBusy: boolean) => void;
  onSearchSeeds: (input: SearchSeedInput) => Promise<DynamaxAdventureSeedSearch>;
  onWriteSaveSeed: (seed: string) => Promise<DynamaxAdventureSaveSeedResult>;
  selectedEntryIndex: number | null;
};

const maximumSeed = 0xffffffffffffffffn;
const maximumSearchLimit = 10_000n;

type SeedPlannerDraftInputs = {
  maximumResults: string;
  npcCount: number;
  requiredRowsText: string;
  searchLimit: string;
  seed: string;
};

export function DynamaxAdventureSeedPlanner({
  armCriticalWriteGuard,
  encounters,
  hasConfiguredSave,
  onPlanSeed,
  onSaveBusyChange,
  onSearchSeeds,
  onWriteSaveSeed,
  selectedEntryIndex
}: DynamaxAdventureSeedPlannerProps) {
  const { t } = useLocalization();
  const [seed, setSeed] = useState('0x0000000000000000');
  const [npcCount, setNpcCount] = useState(3);
  const [requiredRowsText, setRequiredRowsText] = useState('');
  const [searchLimit, setSearchLimit] = useState('10000');
  const [maximumResults, setMaximumResults] = useState('25');
  const [plan, setPlan] = useState<DynamaxAdventureSeedPlan | null>(null);
  const [searchResult, setSearchResult] = useState<DynamaxAdventureSeedSearch | null>(null);
  const [saveResult, setSaveResult] = useState<DynamaxAdventureSaveSeedResult | null>(null);
  const [diagnostics, setDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [busyOperation, setBusyOperation] = useState<'plan' | 'search' | 'save' | null>(null);
  const [saveSeedToConfirm, setSaveSeedToConfirm] = useState<string | null>(null);
  const draftInputsRef = useRef({
    maximumResults,
    npcCount,
    requiredRowsText,
    searchLimit,
    seed
  });
  draftInputsRef.current = {
    maximumResults,
    npcCount,
    requiredRowsText,
    searchLimit,
    seed
  };
  const operationRevisionRef = useRef(0);
  const saveGuardRevisionRef = useRef<number | null>(null);
  const isMountedRef = useRef(true);
  const armCriticalWriteGuardRef = useRef(armCriticalWriteGuard);
  armCriticalWriteGuardRef.current = armCriticalWriteGuard;
  const onSaveBusyChangeRef = useRef(onSaveBusyChange);
  onSaveBusyChangeRef.current = onSaveBusyChange;
  const rowLabelByIndex = useMemo(
    () => new Map(encounters.map((encounter) => [encounter.entryIndex, encounter.label])),
    [encounters]
  );
  const parsedRows = useMemo(
    () => parseRequiredRows(requiredRowsText, new Set(rowLabelByIndex.keys()), t),
    [requiredRowsText, rowLabelByIndex, t]
  );
  const seedError = validateSeed(seed, t);
  const searchLimitError = validateSearchLimit(searchLimit, t);
  const parsedMaximumResults = parseBoundedWholeNumberDraft(maximumResults, 1, 1_000);
  const maximumResultsError = parsedMaximumResults === null
    ? t('routePlanner.validation.maximumResults')
    : null;
  usePublishCommonEditorError({
    domain: 'workflow.dynamaxAdventures',
    field: 'seed',
    message: seedError
  });
  usePublishCommonEditorError({
    domain: 'workflow.dynamaxAdventures',
    field: 'requiredRows',
    message: parsedRows.error
  });
  usePublishCommonEditorError({
    domain: 'workflow.dynamaxAdventures',
    field: 'searchLimit',
    message: searchLimitError
  });
  usePublishCommonEditorError({
    domain: 'workflow.dynamaxAdventures',
    field: 'maximumResults',
    message: maximumResultsError
  });
  const canPlan = !busyOperation && !seedError && !parsedRows.error;
  const canSearch =
    !busyOperation &&
    !seedError &&
    !searchLimitError &&
    !parsedRows.error &&
    parsedRows.rows.length > 0 &&
    parsedMaximumResults !== null;

  const beginSaveGuard = useCallback(async (revision: number) => {
    saveGuardRevisionRef.current = revision;
    onSaveBusyChangeRef.current(true);
    try {
      return await armCriticalWriteGuardRef.current();
    } catch {
      return false;
    }
  }, []);

  const endSaveGuard = useCallback((revision?: number) => {
    if (
      saveGuardRevisionRef.current === null ||
      (revision !== undefined && saveGuardRevisionRef.current !== revision)
    ) {
      return;
    }
    saveGuardRevisionRef.current = null;
    onSaveBusyChangeRef.current(false);
  }, []);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      operationRevisionRef.current += 1;
      endSaveGuard();
    };
  }, [endSaveGuard]);

  const beginOperation = (operation: 'plan' | 'search' | 'save') => {
    const revision = operationRevisionRef.current + 1;
    operationRevisionRef.current = revision;
    setBusyOperation(operation);
    setDiagnostics([]);
    return revision;
  };

  const finishOperation = (revision: number) => {
    if (operationRevisionRef.current === revision) {
      setBusyOperation(null);
    }
  };

  const handlePlan = async (nextSeed = seed) => {
    const normalizedSeed = nextSeed.trim();
    if (validateSeed(normalizedSeed, t) || parsedRows.error) return;
    const submittedInputs = {
      npcCount,
      requiredRowsText,
      seed: nextSeed
    };
    const revision = beginOperation('plan');
    try {
      const nextPlan = await onPlanSeed({
        npcCount,
        requiredRows: parsedRows.rows,
        seed: normalizedSeed
      });
      if (
        operationRevisionRef.current !== revision ||
        !samePlanInputs(draftInputsRef.current, submittedInputs)
      ) return;
      setSeed((current) => current === submittedInputs.seed ? nextPlan.seed : current);
      setPlan(nextPlan);
      setDiagnostics(nextPlan.diagnostics);
    } catch (error) {
      if (operationRevisionRef.current !== revision) return;
      setPlan(null);
      setDiagnostics(toProjectBridgeDiagnostics(error, t('routePlanner.error.preview')));
    } finally {
      finishOperation(revision);
    }
  };

  const handleSearch = async () => {
    const maxResults = parseBoundedWholeNumberDraft(maximumResults, 1, 1_000);
    if (!canSearch || maxResults === null) return;
    const submittedInputs = {
      maximumResults,
      npcCount,
      requiredRowsText,
      searchLimit,
      seed
    };
    const revision = beginOperation('search');
    try {
      const nextSearch = await onSearchSeeds({
        limit: searchLimit.trim(),
        maxResults,
        npcCount,
        requiredRows: parsedRows.rows,
        startSeed: seed.trim()
      });
      if (
        operationRevisionRef.current !== revision ||
        !sameSearchInputs(draftInputsRef.current, submittedInputs)
      ) return;
      setSearchResult(nextSearch);
      setDiagnostics(nextSearch.diagnostics);
    } catch (error) {
      if (operationRevisionRef.current !== revision) return;
      setSearchResult(null);
      setDiagnostics(toProjectBridgeDiagnostics(error, t('routePlanner.error.search')));
    } finally {
      finishOperation(revision);
    }
  };

  const handleWriteSaveSeed = async () => {
    const seedToWrite = saveSeedToConfirm;
    if (!seedToWrite || validateSeed(seedToWrite, t)) return;
    const revision = beginOperation('save');
    try {
      const didArmCriticalWriteGuard = await beginSaveGuard(revision);
      if (
        !didArmCriticalWriteGuard ||
        !isMountedRef.current ||
        operationRevisionRef.current !== revision
      ) {
        if (
          !didArmCriticalWriteGuard &&
          isMountedRef.current &&
          operationRevisionRef.current === revision
        ) {
          setDiagnostics([{
            code: desktopErrorCodes.closeGuardUpdateFailed,
            domain: 'desktop',
            message: t('routePlanner.error.closeGuard'),
            severity: 'error'
          }]);
        }
        return;
      }
      const nextResult = await onWriteSaveSeed(seedToWrite);
      if (operationRevisionRef.current !== revision) return;
      setSaveResult(nextResult);
      setDiagnostics(nextResult.diagnostics);
      setSaveSeedToConfirm(null);
    } catch (error) {
      if (operationRevisionRef.current !== revision) return;
      setSaveResult(null);
      setDiagnostics(toProjectBridgeDiagnostics(error, t('routePlanner.error.save')));
      setSaveSeedToConfirm(null);
    } finally {
      endSaveGuard(revision);
      finishOperation(revision);
    }
  };

  const addSelectedRow = () => {
    if (selectedEntryIndex === null || !rowLabelByIndex.has(selectedEntryIndex)) return;
    const rows = new Set(parsedRows.error ? [] : parsedRows.rows);
    rows.add(selectedEntryIndex);
    setRequiredRowsText([...rows].sort((left, right) => left - right).join(', '));
  };

  return (
    <section aria-labelledby="dynamax-adventure-route-planner-heading" className="dynamax-adventure-route-planner">
      <div className="panel-heading">
        <Route aria-hidden="true" size={18} />
        <h3 id="dynamax-adventure-route-planner-heading">{t('routePlanner.title')}</h3>
      </div>
      <p className="dynamax-adventure-route-copy">
        {t('routePlanner.description')}
      </p>

      <div className="dynamax-adventure-route-controls">
        <label>
          <span>{t('routePlanner.seed')}</span>
          <input
            aria-invalid={Boolean(seedError)}
            onChange={(event) => setSeed(event.target.value)}
            spellCheck={false}
            value={seed}
          />
          {seedError ? <small className="editable-field-error">{seedError}</small> : null}
        </label>
        <label>
          <span>{t('routePlanner.npcCount')}</span>
          <select
            onChange={(event) => setNpcCount(Number(event.target.value))}
            value={npcCount}
          >
            {[0, 1, 2, 3].map((count) => (
              <option key={count} value={count}>{count}</option>
            ))}
          </select>
        </label>
        <label className="dynamax-adventure-required-rows">
          <span>{t('routePlanner.requiredRows')}</span>
          <input
            aria-invalid={Boolean(parsedRows.error)}
            onChange={(event) => setRequiredRowsText(event.target.value)}
            placeholder={t('routePlanner.requiredRowsPlaceholder')}
            value={requiredRowsText}
          />
          {parsedRows.error ? <small className="editable-field-error">{parsedRows.error}</small> : null}
        </label>
        <button
          className="secondary-button compact-button"
          disabled={
            selectedEntryIndex === null ||
            !rowLabelByIndex.has(selectedEntryIndex)
          }
          onClick={addSelectedRow}
          type="button"
        >
          {t('routePlanner.addSelectedRow')}
        </button>
      </div>

      <div className="dynamax-adventure-route-search-controls">
        <label>
          <span>{t('routePlanner.searchCount')}</span>
          <input
            aria-invalid={Boolean(searchLimitError)}
            inputMode="numeric"
            onChange={(event) => setSearchLimit(event.target.value)}
            value={searchLimit}
          />
          {searchLimitError ? <small className="editable-field-error">{searchLimitError}</small> : null}
        </label>
        <label>
          <span>{t('routePlanner.maximumResults')}</span>
          <input
            aria-invalid={Boolean(maximumResultsError)}
            inputMode="numeric"
            onChange={(event) => setMaximumResults(event.currentTarget.value)}
            pattern="[0-9]*"
            type="text"
            value={maximumResults}
          />
          {maximumResultsError ? (
            <small className="editable-field-error">{maximumResultsError}</small>
          ) : null}
        </label>
        <div className="draft-action-row">
          <button
            aria-busy={busyOperation === 'plan' || undefined}
            className="secondary-button"
            disabled={!canPlan}
            onClick={() => void handlePlan()}
            type="button"
          >
            <Route aria-hidden="true" size={16} />
            <span>
              {busyOperation === 'plan'
                ? t('routePlanner.previewing')
                : t('routePlanner.preview')}
            </span>
          </button>
          <button
            aria-busy={busyOperation === 'search' || undefined}
            className="primary-button"
            disabled={!canSearch}
            onClick={() => void handleSearch()}
            type="button"
          >
            <Search aria-hidden="true" size={16} />
            <span>
              {busyOperation === 'search'
                ? t('routePlanner.searching')
                : t('routePlanner.search')}
            </span>
          </button>
        </div>
      </div>

      {plan ? (
        <RoutePreview
          plan={plan}
          rowLabelByIndex={rowLabelByIndex}
          onWriteSeed={() => setSaveSeedToConfirm(plan.seed)}
          canWriteSave={hasConfiguredSave && busyOperation === null}
          t={t}
        />
      ) : null}

      {searchResult ? (
        <section aria-labelledby="dynamax-adventure-seed-results-heading" className="dynamax-adventure-seed-results">
          <div className="panel-heading">
            <Search aria-hidden="true" size={16} />
            <h4 id="dynamax-adventure-seed-results-heading">
              {t('routePlanner.searchResults', { count: searchResult.results.length })}
            </h4>
          </div>
          {searchResult.results.length > 0 ? (
            <ol>
              {searchResult.results.map((result) => (
                <li key={result.seed}>
                  <div>
                    <code data-localization-ignore="true">{result.seed}</code>
                    <small data-localization-ignore="true">
                      {formatPositions(result.positions, t)}
                    </small>
                  </div>
                  <div className="draft-action-row">
                    <button
                      className="secondary-button compact-button"
                      disabled={Boolean(busyOperation)}
                      onClick={() => {
                        setSeed(result.seed);
                        void handlePlan(result.seed);
                      }}
                      type="button"
                    >
                      {t('routePlanner.previewResult')}
                    </button>
                    <button
                      className="secondary-button compact-button"
                      disabled={!hasConfiguredSave || Boolean(busyOperation)}
                      onClick={() => setSaveSeedToConfirm(result.seed)}
                      type="button"
                    >
                      <Save aria-hidden="true" size={14} />
                      {t('routePlanner.writeToSave')}
                    </button>
                  </div>
                </li>
              ))}
            </ol>
          ) : (
            <p className="empty-copy">{t('routePlanner.noResults')}</p>
          )}
        </section>
      ) : null}

      {!hasConfiguredSave ? (
        <div className="dynamax-adventure-seed-warning" role="note">
          <AlertTriangle aria-hidden="true" size={16} />
          <p>{t('routePlanner.configureSave')}</p>
        </div>
      ) : null}

      {saveResult ? (
        <div
          className={`dynamax-adventure-seed-result dynamax-adventure-seed-result-${saveResult.outcome}`}
          role={
            saveResult.outcome === 'rejected' || saveResult.outcome === 'recoveryRequired'
              ? 'alert'
              : 'status'
          }
        >
          <SaveSeedResultIcon outcome={saveResult.outcome} />
          <div>
            <strong>
              {t(`routePlanner.${saveResultTitleKey(saveResult.outcome)}`)}
            </strong>
            <p>
              {t('routePlanner.saveResult', {
                checksum: saveResult.checksumsValid
                  ? t('routePlanner.checksumsVerified')
                  : t('routePlanner.checksumsUnverified'),
                newSeed: saveResult.newSeed,
                oldSeed: saveResult.oldSeed ?? t('routePlanner.unknown')
              })}
            </p>
            {saveResult.backupFilePath ? (
              <p>
                {t('routePlanner.backup')}: <span data-localization-ignore="true">{saveResult.backupFilePath}</span>
              </p>
            ) : null}
            {saveResult.recoveryArtifactStatus === 'retained' && saveResult.recoveryFilePath ? (
              <p>
                {t('routePlanner.recovery')}: <span data-localization-ignore="true">{saveResult.recoveryFilePath}</span>
              </p>
            ) : null}
            {saveResult.recoveryArtifactStatus === 'unavailable' ? (
              <p>{t('routePlanner.recoveryUnavailable')}</p>
            ) : null}
            {saveResult.outcome === 'updated' || saveResult.outcome === 'unchanged' ? (
              <p>{t('routePlanner.freshAdventure')}</p>
            ) : null}
          </div>
        </div>
      ) : null}

      <DiagnosticsSection diagnostics={diagnostics} />

      {saveSeedToConfirm ? (
        <SeedSaveConfirmation
          isSaving={busyOperation === 'save'}
          onCancel={() => {
            if (busyOperation !== 'save') setSaveSeedToConfirm(null);
          }}
          onConfirm={() => void handleWriteSaveSeed()}
          seed={saveSeedToConfirm}
          t={t}
        />
      ) : null}
    </section>
  );
}

function samePlanInputs(
  current: SeedPlannerDraftInputs,
  submitted: Pick<SeedPlannerDraftInputs, 'npcCount' | 'requiredRowsText' | 'seed'>
) {
  return (
    current.npcCount === submitted.npcCount &&
    current.requiredRowsText === submitted.requiredRowsText &&
    current.seed === submitted.seed
  );
}

function sameSearchInputs(
  current: SeedPlannerDraftInputs,
  submitted: SeedPlannerDraftInputs
) {
  return (
    samePlanInputs(current, submitted) &&
    current.maximumResults === submitted.maximumResults &&
    current.searchLimit === submitted.searchLimit
  );
}

function SaveSeedResultIcon({
  outcome
}: {
  outcome: DynamaxAdventureSaveSeedResult['outcome'];
}) {
  const Icon = outcome === 'updated'
    ? CheckCircle2
    : outcome === 'unchanged'
      ? Info
      : CircleAlert;
  return <Icon aria-hidden="true" size={16} />;
}

function saveResultTitleKey(
  outcome: DynamaxAdventureSaveSeedResult['outcome']
) {
  switch (outcome) {
    case 'rejected':
      return 'saveRejected';
    case 'unchanged':
      return 'saveUnchanged';
    case 'updated':
      return 'saveUpdated';
    case 'recovered':
      return 'saveRecovered';
    case 'recoveryRequired':
      return 'saveRecoveryRequired';
  }
}

function RoutePreview({
  canWriteSave,
  onWriteSeed,
  plan,
  rowLabelByIndex,
  t
}: {
  canWriteSave: boolean;
  onWriteSeed: () => void;
  plan: DynamaxAdventureSeedPlan;
  rowLabelByIndex: ReadonlyMap<number, string>;
  t: LocalizationContextValue['t'];
}) {
  return (
    <section aria-labelledby="dynamax-adventure-route-preview-heading" className="dynamax-adventure-route-preview">
      <div className="panel-heading">
        <Route aria-hidden="true" size={16} />
        <h4 id="dynamax-adventure-route-preview-heading">{t('routePlanner.routePreview')}</h4>
      </div>
      <div className="dynamax-adventure-route-preview-heading">
        <code data-localization-ignore="true">{plan.seed}</code>
        <button
          className="secondary-button compact-button"
          disabled={!canWriteSave}
          onClick={onWriteSeed}
          type="button"
        >
          <Save aria-hidden="true" size={14} />
          {t('routePlanner.writeToSave')}
        </button>
      </div>
      <div className="dynamax-adventure-route-columns">
        <RouteSlotList
          label={t('routePlanner.rentals')}
          rowLabelByIndex={rowLabelByIndex}
          t={t}
          templates={plan.rentals}
        />
        <RouteSlotList
          label={t('routePlanner.encounters')}
          rowLabelByIndex={rowLabelByIndex}
          t={t}
          templates={plan.encounters}
        />
      </div>
      {plan.requiredRowPositions.length > 0 ? (
        <p>
          {t('routePlanner.requiredRowsSummary', {
            positions: formatPositions(plan.requiredRowPositions, t)
          })}
        </p>
      ) : null}
    </section>
  );
}

function RouteSlotList({
  label,
  rowLabelByIndex,
  templates,
  t
}: {
  label: string;
  rowLabelByIndex: ReadonlyMap<number, string>;
  templates: DynamaxAdventureSeedPlan['rentals'];
  t: LocalizationContextValue['t'];
}) {
  return (
    <section>
      <h5>{label}</h5>
      <ol>
        {templates.map((template, index) => (
          <li key={`${template.row}:${index}`}>
            <span>{index + 1}</span>
            <div>
              <strong>
                {rowLabelByIndex.get(template.row) ??
                  t('routePlanner.fallbackSpecies', {
                    form: template.form,
                    species: template.species
                  })}
              </strong>
              <small>
                {t('routePlanner.rowSummary', {
                  boss: template.isBoss ? ` | ${t('routePlanner.boss')}` : '',
                  row: template.row
                })}
              </small>
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}

function SeedSaveConfirmation({
  isSaving,
  onCancel,
  onConfirm,
  seed,
  t
}: {
  isSaving: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  seed: string;
  t: LocalizationContextValue['t'];
}) {
  const dialogRef = useModalDialog({ canClose: !isSaving, onClose: onCancel });
  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby="dynamax-adventure-save-confirmation-heading"
        aria-modal="true"
        className="modal-panel dynamax-adventure-save-confirmation"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading">
          <Save aria-hidden="true" size={18} />
          <h2 id="dynamax-adventure-save-confirmation-heading">
            {t('routePlanner.confirmTitle')}
          </h2>
          <button
            aria-label={t('routePlanner.close')}
            className="icon-button"
            disabled={isSaving}
            onClick={onCancel}
            type="button"
          >
            <X aria-hidden="true" size={16} />
          </button>
        </div>
        <p>
          {t('routePlanner.confirmDescription')}
        </p>
        <code data-localization-ignore="true">{seed}</code>
        <p>{t('routePlanner.confirmFresh')}</p>
        <div className="modal-actions">
          <button className="secondary-button" disabled={isSaving} onClick={onCancel} type="button">
            {t('routePlanner.cancel')}
          </button>
          <button
            aria-busy={isSaving || undefined}
            className="primary-button"
            disabled={isSaving}
            onClick={onConfirm}
            type="button"
          >
            <Save aria-hidden="true" size={16} />
            <span>
              {isSaving
                ? t('routePlanner.writing')
                : t('routePlanner.writeVerify')}
            </span>
          </button>
        </div>
      </section>
    </div>
  );
}

function parseRequiredRows(
  value: string,
  availableRows: ReadonlySet<number>,
  t: LocalizationContextValue['t']
) {
  const trimmed = value.trim();
  if (!trimmed) return { error: null, rows: [] as number[] };
  const tokens = trimmed.split(/[\s,;]+/u).filter(Boolean);
  const rows: number[] = [];
  const seen = new Set<number>();
  for (const token of tokens) {
    if (!/^(?:0|[1-9][0-9]*)$/u.test(token)) {
      return {
        error: t('routePlanner.validation.invalidRow', { row: token }),
        rows: [] as number[]
      };
    }
    const row = Number(token);
    if (!Number.isSafeInteger(row) || !availableRows.has(row)) {
      return {
        error: t('routePlanner.validation.missingRow', { row: token }),
        rows: [] as number[]
      };
    }
    if (!seen.has(row)) {
      seen.add(row);
      rows.push(row);
    }
  }
  return { error: null, rows };
}

function validateSeed(value: string, t: LocalizationContextValue['t']) {
  const trimmed = value.trim();
  if (!/^(?:0[xX][0-9A-Fa-f]{1,16}|[0-9]{1,20})$/u.test(trimmed)) {
    return t('routePlanner.validation.seedFormat');
  }
  try {
    const parsed = BigInt(trimmed);
    return parsed < 0n || parsed > maximumSeed
      ? t('routePlanner.validation.seedRange')
      : null;
  } catch {
    return t('routePlanner.validation.seedRange');
  }
}

function validateSearchLimit(value: string, t: LocalizationContextValue['t']) {
  const seedError = validateSeed(value, t);
  if (seedError) return seedError;
  return BigInt(value.trim()) > maximumSearchLimit
    ? t('routePlanner.validation.searchMax')
    : null;
}

function formatPositions(
  positions: readonly { kind: 'rental' | 'encounter'; row: number; slot: number }[],
  t: LocalizationContextValue['t']
) {
  return positions
    .map((position) =>
      t('routePlanner.position', {
        kind:
          position.kind === 'rental'
            ? t('routePlanner.rental')
            : t('routePlanner.encounter'),
        row: position.row,
        slot: position.slot + 1
      })
    )
    .join(', ');
}
