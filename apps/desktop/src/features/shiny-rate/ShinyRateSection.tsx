/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, Save, Sparkle } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { type EditSession } from '../../bridge/contracts';
import {
  type ShinyRateMode,
  type ShinyRateSourceRecord,
  type ShinyRateWorkflow
} from '../../bridge/shinyRateContracts';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import {
  formatBagHookStatus,
  formatFileState,
  formatSourceLayer
} from '../../utils/workflowFormatters';

type ShinyRateSelection = {
  mode: ShinyRateMode;
  rollCount: number | null;
};

const baseShinyOdds = 4096;

export function ShinyRateSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onDirtyChange,
  onStageRate,
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
  onStageRate: (mode: ShinyRateMode, rollCount: number | null) => void;
  panelOutput: WorkflowPanelOutput;
  workflow: ShinyRateWorkflow | null;
}) {
  const stagedShinyRateEdit = editSession?.pendingEdits.find(
    (edit) => edit.domain === 'workflow.shinyRate'
  );
  const stagedSelection = useMemo(
    () => decodeShinyRatePendingSelection(stagedShinyRateEdit?.newValue),
    [stagedShinyRateEdit?.newValue]
  );
  const workflowSelection = useMemo(() => getWorkflowSelection(workflow), [workflow]);
  const cleanSelection = stagedSelection ?? workflowSelection;
  const cleanSelectionKey = getSelectionKey(cleanSelection);
  const [draftSelection, setDraftSelection] = useState<ShinyRateSelection>(cleanSelection);
  const [customDenominator, setCustomDenominator] = useState(
    workflow?.rateRule.oddsDenominator?.toString() ?? '4096'
  );

  useEffect(() => {
    setDraftSelection(cleanSelection);
  }, [cleanSelectionKey]);

  useEffect(() => {
    if (workflow?.rateRule.oddsDenominator) {
      setCustomDenominator(workflow.rateRule.oddsDenominator.toString());
    }
  }, [workflow?.rateRule.oddsDenominator]);

  const customCalculation = useMemo(
    () => calculateCustomRate(customDenominator, workflow),
    [customDenominator, workflow]
  );
  const isDirty = getSelectionKey(draftSelection) !== cleanSelectionKey;
  const hasStagedChange = stagedSelection !== null;
  const canEdit =
    workflow?.summary.availability === 'available' &&
    workflow.installStatus !== 'blocked' &&
    !isStaging &&
    !isChangePlanApplying;
  const canStage = canEdit && isDirty;
  const canReviewPlan = hasStagedChange && !isDirty && !isChangePlanCreating;
  const canApplyPlan =
    hasStagedChange &&
    !isDirty &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isChangePlanApplying;

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const applyPreset = (preset: ShinyRateWorkflow['presets'][number]) => {
    if (!preset.isEnabled || !canEdit) {
      return;
    }

    if (preset.mode === 'default') {
      setDraftSelection({ mode: 'default', rollCount: null });
    } else if (preset.mode === 'fixed' && preset.rollCount !== null) {
      setDraftSelection({ mode: 'fixed', rollCount: preset.rollCount });
    } else if (preset.mode === 'always') {
      setDraftSelection({ mode: 'always', rollCount: null });
    }

    if (preset.targetDenominator !== null) {
      setCustomDenominator(preset.targetDenominator.toString());
    }
  };

  const applyCustom = () => {
    if (!canEdit || customCalculation === null) {
      return;
    }

    setDraftSelection({
      mode: 'fixed',
      rollCount: customCalculation.rollCount
    });
    setCustomDenominator(customCalculation.targetDenominator.toString());
  };

  return (
    <>
      <section aria-labelledby="shiny-rate-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Sparkle aria-hidden="true" size={18} />
          <h2 id="shiny-rate-heading">Shiny Rate</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <Metric
            label="Status"
            value={workflow ? formatBagHookStatus(workflow.installStatus) : 'Not loaded'}
          />
          <Metric label="Current odds" value={workflow?.rateRule.oddsLabel ?? 'Unknown'} />
          <Metric label="Current chance" value={workflow?.rateRule.percentLabel ?? 'Unknown'} />
          <Metric label="Staged" value={hasStagedChange ? 'Yes' : 'No'} />
          <Metric label="Offset" value={workflow?.compareOffsetHex ?? 'Unknown'} />
          <Metric
            label="Build"
            value={
              workflow?.buildId && workflow.buildId !== 'unknown'
                ? workflow.buildId.slice(0, 12)
                : 'Unknown'
            }
          />
        </div>

        {workflow ? (
          <div className="shiny-rate-editor">
            <div className="shiny-rate-current">
              <div>
                <span>Draft</span>
                <strong>{formatSelectionLabel(draftSelection)}</strong>
              </div>
              <div>
                <span>Runtime</span>
                <strong>{workflow.rateRule.runtimeSummary}</strong>
              </div>
            </div>

            <div className="shiny-rate-presets" aria-label="Shiny rate presets">
              {workflow.presets.map((preset) => {
                const isActive = isPresetActive(preset, draftSelection);
                return (
                  <button
                    className={`shiny-rate-preset ${isActive ? 'selected' : ''}`}
                    disabled={!preset.isEnabled || !canEdit}
                    key={preset.presetId}
                    onClick={() => applyPreset(preset)}
                    title={preset.description}
                    type="button"
                  >
                    <span>{preset.label}</span>
                    <strong>{preset.oddsLabel}</strong>
                    <small>{preset.percentLabel}</small>
                  </button>
                );
              })}
            </div>

            <div className="shiny-rate-custom">
              <label>
                <span>Custom</span>
                <span className="shiny-rate-fraction">
                  1/
                  <input
                    aria-label="Custom shiny odds denominator"
                    disabled={!canEdit}
                    inputMode="numeric"
                    max={workflow.rateRule.maximumCustomDenominator}
                    min={workflow.rateRule.minimumCustomDenominator}
                    onChange={(event) => setCustomDenominator(event.target.value)}
                    step={1}
                    type="number"
                    value={customDenominator}
                  />
                </span>
              </label>
              <div className="shiny-rate-custom-result">
                <span>{customCalculation?.summary ?? 'Enter odds from 1/2 to 1/4096.'}</span>
                <button
                  className="secondary-button"
                  disabled={!canEdit || customCalculation === null}
                  onClick={applyCustom}
                  type="button"
                >
                  Use Custom
                </button>
              </div>
            </div>

            <div className="type-chart-actions">
              <button
                className="primary-button"
                disabled={!canStage}
                onClick={() => onStageRate(draftSelection.mode, draftSelection.rollCount)}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage Shiny Rate'}</span>
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

            <ShinyRateSourceSummary source={workflow.source} />
          </div>
        ) : (
          <p className="empty-copy">Open Shiny Rate from Advanced Editors to inspect the reroll loop.</p>
        )}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}

export function decodeShinyRatePendingSelection(
  value: string | null | undefined
): ShinyRateSelection | null {
  if (!value) {
    return null;
  }

  if (value === 'default') {
    return { mode: 'default', rollCount: null };
  }

  if (value === 'always') {
    return { mode: 'always', rollCount: null };
  }

  const fixedMatch = /^fixed:(\d+)$/.exec(value);
  if (fixedMatch) {
    return { mode: 'fixed', rollCount: Number.parseInt(fixedMatch[1]!, 10) };
  }

  return null;
}

export function formatShinyRatePendingValue(value: string | null | undefined) {
  const selection = decodeShinyRatePendingSelection(value);
  return selection ? formatSelectionLabel(selection) : 'Unknown';
}

function ShinyRateSourceSummary({ source }: { source: ShinyRateSourceRecord | null }) {
  if (!source) {
    return null;
  }

  return (
    <dl className="type-chart-source-summary">
      <div>
        <dt>Source</dt>
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
  );
}

function getWorkflowSelection(workflow: ShinyRateWorkflow | null): ShinyRateSelection {
  if (!workflow) {
    return { mode: 'default', rollCount: null };
  }

  if (workflow.rateRule.mode === 'fixed' && workflow.rateRule.rollCount !== null) {
    return { mode: 'fixed', rollCount: workflow.rateRule.rollCount };
  }

  if (workflow.rateRule.mode === 'always') {
    return { mode: 'always', rollCount: null };
  }

  return { mode: 'default', rollCount: null };
}

function getSelectionKey(selection: ShinyRateSelection) {
  return `${selection.mode}:${selection.rollCount ?? ''}`;
}

function isPresetActive(
  preset: ShinyRateWorkflow['presets'][number],
  selection: ShinyRateSelection
) {
  if (!preset.isEnabled) {
    return false;
  }

  if (preset.mode === 'default') {
    return selection.mode === 'default';
  }

  if (preset.mode === 'fixed') {
    return selection.mode === 'fixed' && selection.rollCount === preset.rollCount;
  }

  return selection.mode === preset.mode;
}

function calculateCustomRate(
  value: string,
  workflow: ShinyRateWorkflow | null
): {
  actualDenominator: number;
  chancePercent: string;
  rollCount: number;
  summary: string;
  targetDenominator: number;
} | null {
  if (!workflow) {
    return null;
  }

  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed)) {
    return null;
  }

  const targetDenominator = clamp(
    parsed,
    workflow.rateRule.minimumCustomDenominator,
    workflow.rateRule.maximumCustomDenominator
  );
  const targetChance = 1 / targetDenominator;
  const rollCount = clamp(
    Math.ceil(Math.log(1 - targetChance) / Math.log((baseShinyOdds - 1) / baseShinyOdds)),
    workflow.rateRule.minimumRollCount,
    workflow.rateRule.maximumRollCount
  );
  const chance = 1 - Math.pow((baseShinyOdds - 1) / baseShinyOdds, rollCount);
  const actualDenominator = Math.max(1, Math.round(1 / chance));
  const chancePercent = `${(chance * 100).toFixed(3)}%`;
  const clampedCopy =
    targetDenominator === parsed
      ? ''
      : `Closest input is 1/${targetDenominator.toLocaleString()}. `;

  return {
    actualDenominator,
    chancePercent,
    rollCount,
    summary: `${clampedCopy}${rollCount} roll${rollCount === 1 ? '' : 's'} gives about 1/${actualDenominator.toLocaleString()} (${chancePercent}).`,
    targetDenominator
  };
}

function formatSelectionLabel(selection: ShinyRateSelection) {
  if (selection.mode === 'default') {
    return 'Default';
  }

  if (selection.mode === 'always') {
    return 'Always Shiny';
  }

  return `${selection.rollCount ?? 1} roll${selection.rollCount === 1 ? '' : 's'}`;
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(Math.max(value, minimum), maximum);
}
