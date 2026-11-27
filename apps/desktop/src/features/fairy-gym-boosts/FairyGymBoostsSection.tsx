/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, RotateCcw, Save, Sparkles } from 'lucide-react';
import { type KeyboardEvent, type ReactNode, useEffect, useMemo, useRef, useState } from 'react';
import { type EditSession } from '../../bridge/contracts';
import {
  type FairyGymBoostRecord,
  type FairyGymBoostResultKind,
  type FairyGymBoostSelection,
  type FairyGymBoostsWorkflow
} from '../../bridge/fairyGymBoostsContracts';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { ContextHelp } from '../../components/ContextHelp';
import { EditorWorkspaceSection, RecordEditorWorkspace } from '../../components/SectionedEditorWorkspace';
import './fairyGymBoostTable.css';
import { reconcileSourceBackedDraft } from '../../components/localEditorDraftState';
import { useLocalization } from '../../localization';
import { EditorDetailGroup, EditorDetailValue } from '../../components/EditorRecordDetails';
import { formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';
import {
  getCanonicalFairyGymBoostPendingSelections,
  isSupportedFairyGymBoostOutcome
} from './fairyGymBoostsPending';

type FairyGymOutcomeOption = {
  effectId: number;
  resultKind: FairyGymBoostResultKind;
  label: string;
};

const outcomeOptions: FairyGymOutcomeOption[] = [
  { effectId: 0, resultKind: 'none', label: 'No effect' },
  { effectId: 1, resultKind: 'increase', label: '+1 Atk + Sp.Atk' },
  { effectId: 1, resultKind: 'decrease', label: '-1 Atk + Sp.Atk' },
  { effectId: 2, resultKind: 'increase', label: '+2 Atk + Sp.Atk' },
  { effectId: 2, resultKind: 'decrease', label: '-2 Atk + Sp.Atk' },
  { effectId: 3, resultKind: 'increase', label: '+1 Def + Sp.Def' },
  { effectId: 3, resultKind: 'decrease', label: '-1 Def + Sp.Def' },
  { effectId: 4, resultKind: 'increase', label: '+2 Def + Sp.Def' },
  { effectId: 4, resultKind: 'decrease', label: '-2 Def + Sp.Def' },
  { effectId: 5, resultKind: 'increase', label: '+1 Speed' },
  { effectId: 5, resultKind: 'decrease', label: '-1 Speed' },
  { effectId: 6, resultKind: 'increase', label: '+2 Speed' },
  { effectId: 6, resultKind: 'decrease', label: '-2 Speed' }
];

type FairyGymDrafts = Record<string, FairyGymBoostSelection>;

export function FairyGymBoostsSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onDirtyChange,
  onStageBoosts,
  panelOutput,
  workflow
}: {
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isStaging: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onDirtyChange: (isDirty: boolean) => void;
  onStageBoosts: (selections: FairyGymBoostSelection[]) => void;
  panelOutput: WorkflowPanelOutput;
  workflow: FairyGymBoostsWorkflow | null;
}) {
  const { translateLiteral } = useLocalization();
  const sortedTrainers = useMemo(
    () =>
      workflow?.trainers
        .slice()
        .sort((left, right) => left.displayOrder - right.displayOrder) ?? [],
    [workflow?.trainers]
  );
  const workflowSelections = useMemo(
    () => getFairyGymBoostWorkflowSelections(workflow),
    [workflow]
  );
  const fairyGymBoostsPendingEdits = editSession?.pendingEdits.filter(
    (edit) => edit.domain === 'workflow.fairyGymBoosts'
  ) ?? [];
  const stagedSelections = useMemo(
    () => getCanonicalFairyGymBoostPendingSelections(editSession, workflow),
    [editSession, workflow]
  );
  const cleanSelections = useMemo(
    () => mergeSelections(workflowSelections, stagedSelections),
    [workflowSelections, stagedSelections]
  );
  const vanillaSelections = useMemo(
    () => getFairyGymBoostVanillaSelections(workflow),
    [workflow]
  );
  const cleanSelectionsKey = encodeSelectionsKey(cleanSelections);
  const draftIdentityKey = [
    workflow?.detectedGame ?? 'unknown',
    workflow?.trainers
      .flatMap((trainer) => trainer.boosts.map((boost) => boost.boostId))
      .join(',') ?? 'none'
  ].join('|');

  const [selectedTrainerId, setSelectedTrainerId] = useState<number | null>(null);
  const cleanDrafts = createDrafts(cleanSelections);
  const [drafts, setDrafts] = useState<FairyGymDrafts>(cleanDrafts);
  const sourceDraftRef = useRef({ drafts: cleanDrafts, identityKey: draftIdentityKey });

  useEffect(() => {
    const previous = sourceDraftRef.current;
    setDrafts((current) =>
      previous.identityKey !== draftIdentityKey
        ? cleanDrafts
        : reconcileSourceBackedDraft(
            current,
            previous.drafts,
            cleanDrafts,
            (left, right) => encodeDraftsKey(left) === encodeDraftsKey(right)
          )
    );
    sourceDraftRef.current = { drafts: cleanDrafts, identityKey: draftIdentityKey };
  // The encoded source value is the stable trigger; cleanDrafts is rebuilt each render.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cleanSelectionsKey, draftIdentityKey]);

  useEffect(() => {
    if (sortedTrainers.length === 0) {
      setSelectedTrainerId(null);
      return;
    }

    setSelectedTrainerId((current) =>
      sortedTrainers.some((trainer) => trainer.trainerId === current)
        ? current
        : sortedTrainers[0].trainerId
    );
  }, [sortedTrainers]);

  const selectedTrainer =
    sortedTrainers.find((trainer) => trainer.trainerId === selectedTrainerId) ??
    sortedTrainers[0] ??
    null;
  const draftSelections = useMemo(
    () => getOrderedDraftSelections(workflow, drafts, cleanSelections),
    [workflow, drafts, cleanSelections]
  );
  const isDirty = !areSelectionsEqual(draftSelections, cleanSelections);
  const hasStagedChange = stagedSelections !== null;
  const hasInvalidPendingEdit = fairyGymBoostsPendingEdits.length > 0 && !hasStagedChange;
  const hasConflictingPendingEdit =
    editSession?.pendingEdits.some(
      (edit) => edit.domain !== 'workflow.fairyGymBoosts'
    ) ?? false;
  const isBusy = isStaging || isChangePlanCreating || isChangePlanApplying;
  const canEdit =
    workflow?.summary.availability === 'available' &&
    (workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield') &&
    workflow.sources.every((source) => source.status === 'available') &&
    workflow.trainers.every((trainer) =>
      trainer.boosts.every((boost) => boost.isAvailable)
    );
  const canStage =
    canEdit &&
    !hasInvalidPendingEdit &&
    !hasConflictingPendingEdit &&
    !isBusy &&
    isDirty;
  const canRestoreVanilla = canEdit && !areSelectionsEqual(draftSelections, vanillaSelections);
  const canReviewPlan =
    hasStagedChange &&
    !isDirty &&
    !isStaging &&
    !isChangePlanCreating &&
    !isChangePlanApplying;
  const canApplyPlan =
    hasStagedChange &&
    !isDirty &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isStaging &&
    !isChangePlanCreating &&
    !isChangePlanApplying;

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const updateBoostOutcome = (boostId: string, value: string) => {
    const outcome = parseOutcomeValue(value);
    setDrafts((current) => ({
      ...current,
      [boostId]: {
        boostId,
        effectId: outcome.effectId,
        resultKind: outcome.resultKind
      }
    }));
  };

  const handleTrainerTabKeyDown = (
    event: KeyboardEvent<HTMLButtonElement>,
    currentIndex: number
  ) => {
    let nextIndex: number | null = null;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      nextIndex = (currentIndex + 1) % sortedTrainers.length;
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      nextIndex = (currentIndex - 1 + sortedTrainers.length) % sortedTrainers.length;
    } else if (event.key === 'Home') {
      nextIndex = 0;
    } else if (event.key === 'End') {
      nextIndex = sortedTrainers.length - 1;
    }

    if (nextIndex === null || sortedTrainers.length === 0) {
      return;
    }

    event.preventDefault();
    const trainer = sortedTrainers[nextIndex];
    if (!trainer) {
      return;
    }

    setSelectedTrainerId(trainer.trainerId);
    event.currentTarget.parentElement
      ?.querySelectorAll<HTMLButtonElement>('[role="tab"]')
      .item(nextIndex)
      .focus();
  };

  return (
    <>
      <section aria-labelledby="fairy-gym-boosts-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Sparkles aria-hidden="true" size={18} />
          <h2 id="fairy-gym-boosts-heading">Fairy Gym Boosts</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <Metric
            label="Game"
            value={formatFairyGymGame(workflow?.detectedGame ?? null)}
          />
          <Metric label="Trainers" value={workflow?.stats.trainerCount.toString() ?? '0'} />
          <Metric label="Answer outcomes" value={workflow?.stats.boostCount.toString() ?? '0'} />
          <Metric
            label="Sources"
            value={workflow?.stats.sourceFileCount.toString() ?? '0'}
          />
          <Metric label="Mapped bytes" value={workflow?.stats.ownedByteCount.toString() ?? '0'} />
          <Metric
            label="Staged"
            value={hasStagedChange ? 'Yes' : hasInvalidPendingEdit ? 'Invalid' : 'No'}
          />
        </div>

        {workflow ? (
          <div className="fairy-gym-editor">
            <div className="type-chart-actions fairy-gym-actions">
              <button
                className="danger-button"
                disabled={!canRestoreVanilla}
                onClick={() => setDrafts(createDrafts(vanillaSelections))}
                type="button"
              >
                <RotateCcw aria-hidden="true" size={16} />
                <span>Restore to Vanilla</span>
              </button>
              <button
                className="primary-button"
                aria-busy={isStaging || undefined}
                disabled={!canStage}
                onClick={() => onStageBoosts(draftSelections)}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage Fairy Gym Boosts'}</span>
              </button>
              <button
                className="secondary-button"
                aria-busy={isChangePlanCreating || undefined}
                disabled={!canReviewPlan}
                onClick={onCreateChangePlan}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isChangePlanCreating ? 'Reviewing' : 'Review'}</span>
              </button>
              <button
                className="primary-button"
                aria-busy={isChangePlanApplying || undefined}
                disabled={!canApplyPlan}
                onClick={onApplyChangePlan}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isChangePlanApplying ? 'Applying' : 'Apply'}</span>
              </button>
            </div>

            <RecordEditorWorkspace id="fairyGymBoosts" className="fairy-gym-workspace" initialSection="outcomes">
              <EditorWorkspaceSection id="outcomes" label={translateLiteral('Outcomes')}>
                <div
                  aria-labelledby="fairy-gym-boosts-heading"
                  className="fairy-gym-trainer-tabs"
                  role="tablist"
                >
                  {sortedTrainers.map((trainer, trainerIndex) => {
                    const isSelected = selectedTrainer?.trainerId === trainer.trainerId;
                    return (
                      <button
                        aria-controls={isSelected ? 'fairy-gym-boost-panel' : undefined}
                        aria-selected={isSelected}
                        className={
                          isSelected
                            ? 'fairy-gym-trainer-tab is-selected'
                            : 'fairy-gym-trainer-tab'
                        }
                        key={trainer.trainerId}
                        id={`fairy-gym-tab-${trainer.trainerId}`}
                        onClick={() => setSelectedTrainerId(trainer.trainerId)}
                        onKeyDown={(event) =>
                          handleTrainerTabKeyDown(event, trainerIndex)
                        }
                        role="tab"
                        tabIndex={isSelected ? 0 : -1}
                        type="button"
                        data-localization-ignore="true"
                      >
                        {trainer.npcName}
                      </button>
                    );
                  })}
                </div>
                {selectedTrainer ? (
                  <div
                    aria-labelledby={`fairy-gym-tab-${selectedTrainer.trainerId}`}
                    className="fairy-gym-boost-panel"
                    id="fairy-gym-boost-panel"
                    role="tabpanel"
                    tabIndex={0}
                  >
                    <FairyGymBoostTable>
                      {selectedTrainer.boosts.map((boost) => (
                        <FairyGymBoostRow
                          boost={boost}
                          disabled={!canEdit}
                          key={boost.boostId}
                          onChange={updateBoostOutcome}
                          selection={drafts[boost.boostId] ?? boostToSelection(boost)}
                          translateLiteral={translateLiteral}
                        />
                      ))}
                    </FairyGymBoostTable>
                  </div>
                ) : (
                  <p className="empty-copy">No Fairy Gym boost mappings are loaded.</p>
                )}

              </EditorWorkspaceSection>
              <EditorWorkspaceSection id="source" label={translateLiteral('Source')}>
                <FairyGymSourceSummary
                  sources={workflow.sources}
                  translateLiteral={translateLiteral}
                />
              </EditorWorkspaceSection>
            </RecordEditorWorkspace>
          </div>
        ) : (
          <p className="empty-copy">Open Fairy Gym Boosts from Advanced Editors.</p>
        )}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}

export function FairyGymBoostTable({ children, showQuestion = true }: {
  children: ReactNode;
  showQuestion?: boolean;
}) {
  const { t, translateLiteral } = useLocalization();
  return (
    <table className="fairy-gym-boost-table" aria-label={translateLiteral('Boost outcomes')}>
      <thead>
        <tr>
          {showQuestion ? <th scope="col">{translateLiteral('Question')}</th> : null}
          <th scope="col">{translateLiteral('Answer')}</th>
          <th scope="col">{translateLiteral('Current effect')}</th>
          <th scope="col">
            <span className="editable-field-label-row">
              {translateLiteral('Outcome')}
              <ContextHelp label={translateLiteral('Outcome')}>{t('workflowHelp.fairyGym.outcome')}</ContextHelp>
            </span>
          </th>
        </tr>
      </thead>
      <tbody>{children}</tbody>
    </table>
  );
}

export function FairyGymBoostRow({
  boost,
  disabled,
  onChange,
  selection,
  translateLiteral,
  showQuestion = true
}: {
  boost: FairyGymBoostRecord;
  disabled: boolean;
  onChange: (boostId: string, value: string) => void;
  selection: FairyGymBoostSelection;
  translateLiteral: (literal: string) => string;
  showQuestion?: boolean;
}) {
  return (
    <tr>
      {showQuestion ? <td data-localization-ignore="true">{boost.questionText}</td> : null}
      <th scope="row">
        <strong data-localization-ignore="true">{boost.answerText}</strong>
        <small>
          {translateLiteral('Answer')} {boost.answerChoice}
          {showQuestion ? ` · ${translateLiteral(formatAnswerRole(boost.defaultResultKind))}` : null}
        </small>
      </th>
      <td>
        {boost.isAvailable
          ? translateLiteral(formatOutcomeLabel(boostToSelection(boost)))
          : translateLiteral('Unavailable')}
      </td>
      <td>
        <SearchableOptionInput
          ariaLabel={`${boost.answerText} outcome`}
          data-km-source-site="fairy-gym-outcome"
          disabled={disabled}
          id={`fairy-gym-outcome-${boost.boostId}`}
          isFiniteCatalog
          localizeOptions={false}
          onChange={(value) => onChange(boost.boostId, value)}
          options={outcomeOptions.map((option) => ({
            label: translateLiteral(option.label),
            value: selectionToValue(option)
          }))}
          value={selectionToValue(selection)}
        />
      </td>
    </tr>
  );
}

function FairyGymSourceSummary({
  sources,
  translateLiteral
}: {
  sources: FairyGymBoostsWorkflow['sources'];
  translateLiteral: (literal: string) => string;
}) {
  return (
    <div className="editor-detail-columns">
      {sources.map((source) => (
        <EditorDetailGroup key={source.sourceId} label={translateLiteral(source.label)}>
          <dl className="editor-detail-list">
            <EditorDetailValue label="Source file" path dataValue>{source.relativePath}</EditorDetailValue>
            <EditorDetailValue label="Status">{translateLiteral(formatFairyGymSourceStatus(source.status))}</EditorDetailValue>
            <EditorDetailValue label="Layer">{formatSourceLayer(source.provenance.sourceLayer)}</EditorDetailValue>
            <EditorDetailValue label="File state">{formatFileState(source.provenance.fileState)}</EditorDetailValue>
            <EditorDetailValue label="Payload offset" dataValue>{formatMappedOffset(source.payloadOffsetHex)}</EditorDetailValue>
            <EditorDetailValue label="Owned range" dataValue>{formatMappedOffset(source.ownedRangeHex)}</EditorDetailValue>
          </dl>
        </EditorDetailGroup>
      ))}
    </div>
  );
}

function getFairyGymBoostWorkflowSelections(
  workflow: FairyGymBoostsWorkflow | null
): FairyGymBoostSelection[] {
  return (
    workflow?.trainers.flatMap((trainer) => trainer.boosts.map(boostToSelection)) ?? []
  );
}

function getFairyGymBoostVanillaSelections(
  workflow: FairyGymBoostsWorkflow | null
): FairyGymBoostSelection[] {
  return (
    workflow?.trainers.flatMap((trainer) => trainer.boosts.map(boostToVanillaSelection)) ?? []
  );
}

function boostToSelection(boost: FairyGymBoostRecord): FairyGymBoostSelection {
  return {
    boostId: boost.boostId,
    effectId: isSupportedFairyGymBoostOutcome(boost.effectId, boost.resultKind) ? boost.effectId : 0,
    resultKind: isSupportedFairyGymBoostOutcome(boost.effectId, boost.resultKind)
      ? boost.resultKind
      : 'none'
  };
}

function boostToVanillaSelection(boost: FairyGymBoostRecord): FairyGymBoostSelection {
  return {
    boostId: boost.boostId,
    effectId: isSupportedFairyGymBoostOutcome(boost.defaultEffectId, boost.defaultResultKind)
      ? boost.defaultEffectId
      : 0,
    resultKind: isSupportedFairyGymBoostOutcome(boost.defaultEffectId, boost.defaultResultKind)
      ? boost.defaultResultKind
      : 'none'
  };
}

function mergeSelections(
  baseSelections: readonly FairyGymBoostSelection[],
  overrideSelections: readonly FairyGymBoostSelection[] | null
) {
  if (!overrideSelections) {
    return baseSelections;
  }

  const overrides = new Map(
    overrideSelections.map((selection) => [selection.boostId, selection])
  );
  return baseSelections.map((selection) => overrides.get(selection.boostId) ?? selection);
}

function createDrafts(selections: readonly FairyGymBoostSelection[]): FairyGymDrafts {
  return Object.fromEntries(
    selections.map((selection) => [selection.boostId, selection])
  ) as FairyGymDrafts;
}

function encodeDraftsKey(drafts: FairyGymDrafts) {
  return encodeSelectionsKey(
    Object.values(drafts).sort((left, right) => left.boostId.localeCompare(right.boostId))
  );
}

function getOrderedDraftSelections(
  workflow: FairyGymBoostsWorkflow | null,
  drafts: FairyGymDrafts,
  cleanSelections: readonly FairyGymBoostSelection[]
) {
  const fallbackById = new Map(cleanSelections.map((selection) => [selection.boostId, selection]));
  const orderedBoosts =
    workflow?.trainers.flatMap((trainer) => trainer.boosts) ??
    cleanSelections.map((selection) => ({
      boostId: selection.boostId
    }));

  return orderedBoosts.map((boost) => drafts[boost.boostId] ?? fallbackById.get(boost.boostId)!);
}

export function parseOutcomeValue(value: string): Pick<FairyGymBoostSelection, 'effectId' | 'resultKind'> {
  const [effectIdText, resultKind] = value.split(':');
  const effectId = Number.parseInt(effectIdText ?? '', 10);
  return isSupportedFairyGymBoostOutcome(effectId, resultKind)
    ? { effectId, resultKind }
    : { effectId: 0, resultKind: 'none' };
}

function selectionToValue({
  effectId,
  resultKind
}: Pick<FairyGymBoostSelection, 'effectId' | 'resultKind'>) {
  return isSupportedFairyGymBoostOutcome(effectId, resultKind)
    ? `${effectId}:${resultKind}`
    : '0:none';
}

function formatAnswerRole(resultKind: FairyGymBoostResultKind) {
  if (resultKind === 'increase') {
    return 'Right answer';
  }

  if (resultKind === 'decrease') {
    return 'Wrong answer';
  }

  return 'Neutral answer';
}

function formatOutcomeLabel(selection: FairyGymBoostSelection) {
  return getOutcomeOption(selection).label;
}

function getOutcomeOption(selection: Pick<FairyGymBoostSelection, 'effectId' | 'resultKind'>) {
  return (
    outcomeOptions.find(
      (option) =>
        option.effectId === selection.effectId && option.resultKind === selection.resultKind
    ) ?? outcomeOptions[0]!
  );
}

function areSelectionsEqual(
  left: readonly FairyGymBoostSelection[],
  right: readonly FairyGymBoostSelection[]
) {
  return (
    left.length === right.length &&
    left.every(
      (selection, index) =>
        selection.boostId === right[index]?.boostId &&
        selection.effectId === right[index]?.effectId &&
        selection.resultKind === right[index]?.resultKind
    )
  );
}

function encodeSelectionsKey(selections: readonly FairyGymBoostSelection[]) {
  return selections
    .map((selection) => `${selection.boostId}:${selection.effectId}:${selection.resultKind}`)
    .join(';');
}

function formatFairyGymGame(game: FairyGymBoostsWorkflow['detectedGame']) {
  if (game === 'sword') {
    return 'Pokemon Sword';
  }

  if (game === 'shield') {
    return 'Pokemon Shield';
  }

  return 'Unknown';
}

function formatFairyGymSourceStatus(
  status: FairyGymBoostsWorkflow['sources'][number]['status']
) {
  if (status === 'available') {
    return 'Available';
  }

  if (status === 'missing') {
    return 'Missing';
  }

  return 'Blocked';
}

function formatMappedOffset(value: string) {
  return value === 'unknown' ? 'Unknown' : value;
}
