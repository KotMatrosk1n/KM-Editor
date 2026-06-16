/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, RotateCcw, Save, Sparkles } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import {
  type ChangePlan,
  type EditSession
} from '../../bridge/contracts';
import {
  type FairyGymBoostRecord,
  type FairyGymBoostResultKind,
  type FairyGymBoostSelection,
  type FairyGymBoostsWorkflow
} from '../../bridge/fairyGymBoostsContracts';
import { DiagnosticsSection, Metric } from '../../components/workflowPanels';
import { formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';

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
  changePlan,
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onDirtyChange,
  onStageBoosts,
  workflow
}: {
  changePlan: ChangePlan | null;
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isStaging: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onDirtyChange: (isDirty: boolean) => void;
  onStageBoosts: (selections: FairyGymBoostSelection[]) => void;
  workflow: FairyGymBoostsWorkflow | null;
}) {
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
  const stagedFairyGymBoostsEdit = editSession?.pendingEdits.find(
    (edit) => edit.domain === 'workflow.fairyGymBoosts'
  );
  const stagedSelections = useMemo(
    () => decodeFairyGymBoostPendingSelections(stagedFairyGymBoostsEdit?.newValue),
    [stagedFairyGymBoostsEdit?.newValue]
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

  const [selectedTrainerId, setSelectedTrainerId] = useState<number | null>(null);
  const [drafts, setDrafts] = useState<FairyGymDrafts>(() => createDrafts(cleanSelections));

  useEffect(() => {
    setDrafts(createDrafts(cleanSelections));
  }, [cleanSelectionsKey]);

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
  const canEdit =
    workflow?.summary.availability === 'available' &&
    workflow.sources.every((source) => source.status === 'available') &&
    !isStaging &&
    !isChangePlanApplying;
  const canStage = canEdit && isDirty;
  const canRestoreVanilla = canEdit && !areSelectionsEqual(draftSelections, vanillaSelections);
  const canReviewPlan = hasStagedChange && !isDirty && !isChangePlanCreating;
  const canApplyPlan =
    hasStagedChange &&
    !isDirty &&
    changePlan !== null &&
    changePlan.canApply &&
    changePlan.writes.length > 0 &&
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

  return (
    <>
      <section aria-labelledby="fairy-gym-boosts-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Sparkles aria-hidden="true" size={18} />
          <h2 id="fairy-gym-boosts-heading">Fairy Gym Boosts</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <Metric label="Trainers" value={workflow?.stats.trainerCount.toString() ?? '0'} />
          <Metric label="Answer outcomes" value={workflow?.stats.boostCount.toString() ?? '0'} />
          <Metric
            label="Sources"
            value={workflow?.stats.sourceFileCount.toString() ?? '0'}
          />
          <Metric label="Staged" value={hasStagedChange ? 'Yes' : 'No'} />
        </div>

        {workflow ? (
          <div className="fairy-gym-editor">
            <div className="fairy-gym-trainer-tabs" role="tablist">
              {sortedTrainers.map((trainer) => {
                const isSelected = selectedTrainer?.trainerId === trainer.trainerId;
                return (
                  <button
                    aria-selected={isSelected}
                    className={
                      isSelected
                        ? 'fairy-gym-trainer-tab is-selected'
                        : 'fairy-gym-trainer-tab'
                    }
                    key={trainer.trainerId}
                    onClick={() => setSelectedTrainerId(trainer.trainerId)}
                    role="tab"
                    type="button"
                  >
                    {trainer.npcName}
                  </button>
                );
              })}
            </div>

            {selectedTrainer ? (
              <div className="fairy-gym-boost-stack">
                {selectedTrainer.boosts.map((boost) => (
                  <FairyGymBoostCard
                    boost={boost}
                    disabled={!canEdit}
                    key={boost.boostId}
                    onChange={updateBoostOutcome}
                    selection={drafts[boost.boostId] ?? boostToSelection(boost)}
                  />
                ))}
              </div>
            ) : (
              <p className="empty-copy">No Fairy Gym boost mappings are loaded.</p>
            )}

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
                disabled={!canStage}
                onClick={() => onStageBoosts(draftSelections)}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage Fairy Gym Boosts'}</span>
              </button>
              <button
                className="secondary-button"
                disabled={!canReviewPlan}
                onClick={onCreateChangePlan}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isChangePlanCreating ? 'Reviewing' : 'Review'}</span>
              </button>
              <button
                className="primary-button"
                disabled={!canApplyPlan}
                onClick={onApplyChangePlan}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isChangePlanApplying ? 'Applying' : 'Apply'}</span>
              </button>
            </div>

            <FairyGymSourceSummary sources={workflow.sources} />
          </div>
        ) : (
          <p className="empty-copy">Open Fairy Gym Boosts from Advanced Editors.</p>
        )}
      </section>

      <DiagnosticsSection diagnostics={workflow?.diagnostics ?? []} />
    </>
  );
}

function FairyGymBoostCard({
  boost,
  disabled,
  onChange,
  selection
}: {
  boost: FairyGymBoostRecord;
  disabled: boolean;
  onChange: (boostId: string, value: string) => void;
  selection: FairyGymBoostSelection;
}) {
  return (
    <article className="fairy-gym-boost-card">
      <div className="fairy-gym-boost-card-heading">
        <div>
          <div className="fairy-gym-answer-row">
            <span className={`fairy-gym-answer-role is-${boost.defaultResultKind}`}>
              {formatAnswerRole(boost.defaultResultKind)}
            </span>
            <span className="fairy-gym-answer-choice">Answer {boost.answerChoice}</span>
          </div>
          <h3>{boost.questionText}</h3>
          <p>{boost.answerText}</p>
        </div>
        <span className="fairy-gym-effect-pill">{formatOutcomeLabel(boostToSelection(boost))}</span>
      </div>

      <label className="fairy-gym-outcome-control">
        <span>Outcome</span>
        <select
          aria-label={`${boost.answerText} outcome`}
          disabled={disabled}
          onChange={(event) => onChange(boost.boostId, event.currentTarget.value)}
          value={selectionToValue(selection)}
        >
          {outcomeOptions.map((option) => (
            <option key={selectionToValue(option)} value={selectionToValue(option)}>
              {option.label}
            </option>
          ))}
        </select>
      </label>
    </article>
  );
}

function FairyGymSourceSummary({
  sources
}: {
  sources: FairyGymBoostsWorkflow['sources'];
}) {
  return (
    <div className="fairy-gym-source-grid">
      {sources.map((source) => (
        <dl className="fairy-gym-source-summary" key={source.sourceId}>
          <div>
            <dt>{source.label}</dt>
            <dd>{source.relativePath}</dd>
          </div>
          <div>
            <dt>Layer</dt>
            <dd>{formatSourceLayer(source.provenance.sourceLayer)}</dd>
          </div>
          <div>
            <dt>File state</dt>
            <dd>{formatFileState(source.provenance.fileState)}</dd>
          </div>
        </dl>
      ))}
    </div>
  );
}

export function decodeFairyGymBoostPendingSelections(
  value: string | null | undefined
): FairyGymBoostSelection[] | null {
  if (!value) {
    return null;
  }

  const selections: FairyGymBoostSelection[] = [];
  for (const entry of value.split(';')) {
    const [boostId, effectIdText, resultKind] = entry.split(':');
    const effectId = Number.parseInt(effectIdText ?? '', 10);
    if (
      !boostId ||
      !Number.isInteger(effectId) ||
      !isSupportedOutcome(effectId, resultKind)
    ) {
      return null;
    }

    selections.push({
      boostId,
      effectId,
      resultKind
    });
  }

  return selections.length > 0 ? selections : null;
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
    effectId: isSupportedOutcome(boost.effectId, boost.resultKind) ? boost.effectId : 0,
    resultKind: isSupportedOutcome(boost.effectId, boost.resultKind)
      ? boost.resultKind
      : 'none'
  };
}

function boostToVanillaSelection(boost: FairyGymBoostRecord): FairyGymBoostSelection {
  return {
    boostId: boost.boostId,
    effectId: isSupportedOutcome(boost.defaultEffectId, boost.defaultResultKind)
      ? boost.defaultEffectId
      : 0,
    resultKind: isSupportedOutcome(boost.defaultEffectId, boost.defaultResultKind)
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

function parseOutcomeValue(value: string): Pick<FairyGymBoostSelection, 'effectId' | 'resultKind'> {
  const [effectIdText, resultKind] = value.split(':');
  const effectId = Number.parseInt(effectIdText ?? '', 10);
  return isSupportedOutcome(effectId, resultKind)
    ? { effectId, resultKind }
    : { effectId: 0, resultKind: 'none' };
}

function selectionToValue({
  effectId,
  resultKind
}: Pick<FairyGymBoostSelection, 'effectId' | 'resultKind'>) {
  return isSupportedOutcome(effectId, resultKind) ? `${effectId}:${resultKind}` : '0:none';
}

function isSupportedOutcome(
  effectId: number,
  resultKind: string | undefined
): resultKind is FairyGymBoostResultKind {
  if (effectId === 0) {
    return resultKind === 'none';
  }

  return effectId >= 1 && effectId <= 6 && (resultKind === 'increase' || resultKind === 'decrease');
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
