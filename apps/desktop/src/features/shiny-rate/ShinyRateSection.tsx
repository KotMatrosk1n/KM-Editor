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
  formatFileState,
  formatSourceLayer
} from '../../utils/workflowFormatters';

type ShinyRateSelection = {
  mode: ShinyRateMode;
  rollCount: number | null;
};

const baseShinyOdds = 4096;
const maximumFixedRollCount = 4091;
const minimumFixedRollCount = 1;
const shinyRateMainPath = 'exefs/main';
const shinyRateSha256RoundConstants = [
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
  0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
  0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
  0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
  0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
  0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
  0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
  0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
  0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
] as const;

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
  const stagedSelection = useMemo(
    () => getCanonicalShinyRatePendingSelection(editSession),
    [editSession]
  );
  const workflowSelection = useMemo(() => getWorkflowSelection(workflow), [workflow]);
  const cleanSelection = stagedSelection ?? workflowSelection;
  const cleanSelectionKey = getSelectionKey(cleanSelection);
  const customDenominatorBaseline = getCustomDenominatorBaseline(cleanSelection, workflow);
  const [draftSelection, setDraftSelection] = useState<ShinyRateSelection>(cleanSelection);
  const [customDenominator, setCustomDenominator] = useState(
    customDenominatorBaseline.toString()
  );

  useEffect(() => {
    setDraftSelection(cleanSelection);
  }, [cleanSelectionKey]);

  useEffect(() => {
    setCustomDenominator(customDenominatorBaseline.toString());
  }, [customDenominatorBaseline]);

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
    !isChangePlanCreating &&
    !isChangePlanApplying;
  const canStage = canEdit && isDirty;
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

    if (
      preset.targetDenominator !== null &&
      preset.targetDenominator >= (workflow?.rateRule.minimumCustomDenominator ?? 2) &&
      preset.targetDenominator <=
        (workflow?.rateRule.maximumCustomDenominator ?? baseShinyOdds)
    ) {
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
            value={workflow ? formatShinyRateStatus(workflow.installStatus) : 'Not loaded'}
          />
          <Metric
            label="Game"
            value={workflow ? formatShinyRateGame(workflow.detectedGame) : 'Not loaded'}
          />
          <Metric label="Current odds" value={workflow?.rateRule.oddsLabel ?? 'Unknown'} />
          <Metric label="Current chance" value={workflow?.rateRule.percentLabel ?? 'Unknown'} />
          <Metric label="Staged" value={hasStagedChange ? 'Yes' : 'No'} />
          <Metric
            label="Build ID"
            value={
              workflow?.buildId && workflow.buildId !== 'unknown'
                ? workflow.buildId
                : 'Unknown'
            }
          />
          <Metric
            label="Outputs"
            value={workflow ? workflow.stats.outputFileCount.toString() : '0'}
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

            <div
              aria-label="Shiny rate presets"
              className="shiny-rate-presets"
              role="group"
            >
              {workflow.presets.map((preset) => {
                const isActive = isPresetActive(preset, draftSelection);
                return (
                  <button
                    aria-pressed={isActive}
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
                    {!preset.isEnabled ? (
                      <small className="shiny-rate-preset-description">
                        {preset.description}
                      </small>
                    ) : null}
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
                    aria-invalid={customCalculation === null ? 'true' : undefined}
                    disabled={!canEdit}
                    inputMode="numeric"
                    max={workflow.rateRule.maximumCustomDenominator}
                    min={workflow.rateRule.minimumCustomDenominator}
                    onBlur={() => {
                      setCustomDenominator(
                        customCalculation?.targetDenominator.toString() ??
                          customDenominatorBaseline.toString()
                      );
                    }}
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
                aria-busy={isStaging || undefined}
                className="primary-button"
                disabled={!canStage}
                onClick={() => onStageRate(draftSelection.mode, draftSelection.rollCount)}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage Shiny Rate'}</span>
              </button>
              <button
                aria-busy={isChangePlanCreating || undefined}
                className="secondary-button"
                disabled={!canReviewPlan}
                onClick={onCreateChangePlan}
                type="button"
              >
                <ClipboardCheck aria-hidden="true" size={16} />
                <span>{isChangePlanCreating ? 'Reviewing' : 'Review'}</span>
              </button>
              <button
                aria-busy={isChangePlanApplying || undefined}
                className="primary-button"
                disabled={!canApplyPlan}
                onClick={onApplyChangePlan}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isChangePlanApplying ? 'Applying' : 'Apply'}</span>
              </button>
            </div>

            <dl className="encounter-slot-detail">
              <div>
                <dt>Detected game</dt>
                <dd>{formatShinyRateGame(workflow.detectedGame)}</dd>
              </div>
              <div>
                <dt>Build ID</dt>
                <dd>{formatShinyRateBuildId(workflow.buildId)}</dd>
              </div>
              <div>
                <dt>Function offset</dt>
                <dd>{formatShinyRateOffset(workflow.functionOffsetHex)}</dd>
              </div>
              <div>
                <dt>Compare offset</dt>
                <dd>{formatShinyRateOffset(workflow.compareOffsetHex)}</dd>
              </div>
              <div>
                <dt>Break offset</dt>
                <dd>{formatShinyRateOffset(workflow.breakOffsetHex)}</dd>
              </div>
              <div>
                <dt>Install message</dt>
                <dd>{workflow.installMessage}</dd>
              </div>
              <div>
                <dt>Staged rate</dt>
                <dd>{stagedSelection ? formatSelectionLabel(stagedSelection) : 'None'}</dd>
              </div>
            </dl>

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
    const rollCount = Number.parseInt(fixedMatch[1]!, 10);
    return rollCount >= minimumFixedRollCount && rollCount <= maximumFixedRollCount
      ? { mode: 'fixed', rollCount }
      : null;
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
        <dt>Status</dt>
        <dd>{formatShinyRateSourceStatus(source.status)}</dd>
      </div>
      <div>
        <dt>Layer</dt>
        <dd>
          {source.status === 'available'
            ? formatSourceLayer(source.provenance.sourceLayer)
            : 'Unavailable'}
        </dd>
      </div>
      <div>
        <dt>File state</dt>
        <dd>
          {source.status === 'available'
            ? formatFileState(source.provenance.fileState)
            : 'Unavailable'}
        </dd>
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

  const trimmed = value.trim();
  if (!/^\d+$/.test(trimmed)) {
    return null;
  }

  const parsed = Number(trimmed);
  if (!Number.isSafeInteger(parsed)) {
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

function getCanonicalShinyRatePendingSelection(
  editSession: EditSession | null
): ShinyRateSelection | null {
  if (editSession?.pendingEdits.length !== 1) {
    return null;
  }

  const edit = editSession.pendingEdits[0];
  if (
    edit?.domain !== 'workflow.shinyRate' ||
    edit.recordId !== 'shiny-rate' ||
    edit.field !== 'rate'
  ) {
    return null;
  }

  const selection = decodeShinyRatePendingSelection(edit.newValue);
  if (
    selection === null ||
    edit.newValue !== encodeShinyRateSelection(selection) ||
    edit.summary !== formatShinyRatePendingSummary(selection) ||
    !hasCanonicalShinyRatePendingSources(edit.sources, edit.newValue)
  ) {
    return null;
  }

  return selection;
}

function hasCanonicalShinyRatePendingSources(
  sources: EditSession['pendingEdits'][number]['sources'],
  payload: string
) {
  if (sources.length !== 2 && sources.length !== 3) {
    return false;
  }

  const baseSource = sources[0];
  const layeredSource = sources.length === 3 ? sources[1] : null;
  const pendingSource = sources.at(-1);
  return (
    baseSource?.layer === 'base' &&
    baseSource.relativePath === shinyRateMainPath &&
    (layeredSource === null ||
      (layeredSource?.layer === 'layered' &&
        layeredSource.relativePath === shinyRateMainPath)) &&
    pendingSource?.layer === 'pending' &&
    pendingSource.relativePath ===
      `pending/shiny-rate/rate/${calculateShinyRatePayloadSha256(payload)}`
  );
}

function calculateShinyRatePayloadSha256(payload: string) {
  const input = new TextEncoder().encode(payload);
  const paddedLength = Math.ceil((input.length + 9) / 64) * 64;
  const padded = new Uint8Array(paddedLength);
  padded.set(input);
  padded[input.length] = 0x80;
  const paddedView = new DataView(padded.buffer);
  const bitLength = input.length * 8;
  paddedView.setUint32(paddedLength - 8, Math.floor(bitLength / 0x100000000));
  paddedView.setUint32(paddedLength - 4, bitLength >>> 0);

  const hash = new Uint32Array([
    0x6a09e667,
    0xbb67ae85,
    0x3c6ef372,
    0xa54ff53a,
    0x510e527f,
    0x9b05688c,
    0x1f83d9ab,
    0x5be0cd19
  ]);
  const words = new Uint32Array(64);

  for (let blockOffset = 0; blockOffset < paddedLength; blockOffset += 64) {
    for (let index = 0; index < 16; index += 1) {
      words[index] = paddedView.getUint32(blockOffset + index * 4);
    }
    for (let index = 16; index < words.length; index += 1) {
      const previous15 = words[index - 15]!;
      const previous2 = words[index - 2]!;
      const sigma0 =
        rotateRight(previous15, 7) ^
        rotateRight(previous15, 18) ^
        (previous15 >>> 3);
      const sigma1 =
        rotateRight(previous2, 17) ^
        rotateRight(previous2, 19) ^
        (previous2 >>> 10);
      words[index] =
        (words[index - 16]! + sigma0 + words[index - 7]! + sigma1) >>> 0;
    }

    let a = hash[0]!;
    let b = hash[1]!;
    let c = hash[2]!;
    let d = hash[3]!;
    let e = hash[4]!;
    let f = hash[5]!;
    let g = hash[6]!;
    let h = hash[7]!;

    for (let index = 0; index < words.length; index += 1) {
      const sum1 = rotateRight(e, 6) ^ rotateRight(e, 11) ^ rotateRight(e, 25);
      const choose = (e & f) ^ (~e & g);
      const temporary1 =
        (h + sum1 + choose + shinyRateSha256RoundConstants[index]! + words[index]!) >>>
        0;
      const sum0 = rotateRight(a, 2) ^ rotateRight(a, 13) ^ rotateRight(a, 22);
      const majority = (a & b) ^ (a & c) ^ (b & c);
      const temporary2 = (sum0 + majority) >>> 0;

      h = g;
      g = f;
      f = e;
      e = (d + temporary1) >>> 0;
      d = c;
      c = b;
      b = a;
      a = (temporary1 + temporary2) >>> 0;
    }

    hash[0] = (hash[0]! + a) >>> 0;
    hash[1] = (hash[1]! + b) >>> 0;
    hash[2] = (hash[2]! + c) >>> 0;
    hash[3] = (hash[3]! + d) >>> 0;
    hash[4] = (hash[4]! + e) >>> 0;
    hash[5] = (hash[5]! + f) >>> 0;
    hash[6] = (hash[6]! + g) >>> 0;
    hash[7] = (hash[7]! + h) >>> 0;
  }

  return Array.from(hash, (word) => word.toString(16).padStart(8, '0'))
    .join('')
    .toUpperCase();
}

function rotateRight(value: number, shift: number) {
  return (value >>> shift) | (value << (32 - shift));
}

function encodeShinyRateSelection(selection: ShinyRateSelection) {
  return selection.mode === 'fixed'
    ? `fixed:${selection.rollCount}`
    : selection.mode;
}

function formatShinyRatePendingSummary(selection: ShinyRateSelection) {
  if (selection.mode === 'default') {
    return 'Stage Shiny Rate default reroll logic.';
  }

  if (selection.mode === 'always') {
    return 'Stage Shiny Rate always-shiny patch.';
  }

  return `Stage Shiny Rate fixed ${selection.rollCount} roll${selection.rollCount === 1 ? '' : 's'}.`;
}

function getCustomDenominatorBaseline(
  selection: ShinyRateSelection,
  workflow: ShinyRateWorkflow | null
) {
  if (selection.mode === 'fixed' && selection.rollCount !== null) {
    const chance = 1 - Math.pow((baseShinyOdds - 1) / baseShinyOdds, selection.rollCount);
    return Math.max(1, Math.round(1 / chance));
  }

  return workflow?.rateRule.maximumCustomDenominator ?? baseShinyOdds;
}

function formatShinyRateStatus(status: ShinyRateWorkflow['installStatus']) {
  switch (status) {
    case 'available':
      return 'Available';
    case 'blocked':
      return 'Blocked';
    case 'disabled':
      return 'Disabled';
    case 'fixed':
      return 'Fixed rolls';
    case 'always':
      return 'Always Shiny';
    case 'readOnly':
      return 'Read-only';
  }
}

function formatShinyRateGame(game: ShinyRateWorkflow['detectedGame']) {
  switch (game) {
    case 'sword':
      return 'Pokemon Sword';
    case 'shield':
      return 'Pokemon Shield';
    case null:
      return 'Unknown';
  }
}

function formatShinyRateBuildId(buildId: string) {
  return buildId === 'unknown' ? 'Unknown' : buildId;
}

function formatShinyRateOffset(offset: string) {
  return offset === 'unknown' ? 'Unknown' : offset;
}

function formatShinyRateSourceStatus(status: ShinyRateSourceRecord['status']) {
  switch (status) {
    case 'available':
      return 'Available';
    case 'missing':
      return 'Missing';
  }
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(Math.max(value, minimum), maximum);
}
