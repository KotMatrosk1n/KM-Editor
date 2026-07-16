/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, RotateCcw, Save, Swords, Trash2 } from 'lucide-react';
import { Fragment, useEffect, useMemo, useState } from 'react';
import {
  type EditSession,
  type ProjectGame,
  type TypeChartSourceRecord,
  type TypeChartWorkflow
} from '../../bridge/contracts';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import { formatBagHookStatus, formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';
import {
  getCanonicalTypeChartPendingState,
  type TypeChartEffectivenessValue
} from './typeChartPending';

export { decodeTypeChartPendingValues } from './typeChartPending';

const typeChartEffectivenessOptions: Array<{
  value: TypeChartEffectivenessValue;
  label: string;
  display: string;
  className: string;
}> = [
  { value: 0, label: 'Immune', display: '0', className: 'immune' },
  { value: 2, label: 'Not Very Effective', display: '½', className: 'not-very' },
  { value: 4, label: 'Normal', display: '', className: 'normal' },
  { value: 8, label: 'Super Effective', display: '2', className: 'super' }
];

export function TypeChartSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onDirtyChange,
  onStageChart,
  onStageUninstall,
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
  onStageChart: (values: TypeChartEffectivenessValue[]) => void;
  onStageUninstall: () => void;
  panelOutput: WorkflowPanelOutput;
  workflow: TypeChartWorkflow | null;
}) {
  const { translateLiteral } = useLocalization();
  const workflowValues = useMemo(() => getTypeChartWorkflowValues(workflow), [workflow]);
  const vanillaValues = useMemo(() => getTypeChartVanillaValues(workflow), [workflow]);
  const stagedState = useMemo(
    () => getCanonicalTypeChartPendingState(editSession, workflow?.detectedGame),
    [editSession, workflow?.detectedGame]
  );
  const stagedValues = stagedState?.kind === 'chart' ? stagedState.values : null;
  const isUninstallStaged = stagedState?.kind === 'uninstall';
  const cleanValues = stagedValues ?? workflowValues ?? createDefaultTypeChartValues();
  const cleanValuesKey = cleanValues.join(',');
  const cleanIdentityKey = [
    workflow?.detectedGame ?? 'unknown',
    workflow?.buildId ?? 'unknown',
    workflow?.chartOffsetHex ?? 'unknown',
    workflow?.source?.relativePath ?? 'none',
    workflow?.source?.provenance.sourceLayer ?? 'none',
    stagedState?.kind ?? 'none',
    editSession?.sessionId ?? 'none'
  ].join('|');
  const [draftValues, setDraftValues] =
    useState<TypeChartEffectivenessValue[]>(cleanValues);

  useEffect(() => {
    setDraftValues(cleanValues);
  }, [cleanIdentityKey, cleanValuesKey]);

  const isDirty = !areTypeChartValuesEqual(draftValues, cleanValues);
  const hasStagedChange = stagedValues !== null || isUninstallStaged;
  const supportsUninstall =
    workflow?.detectedGame === 'scarlet' ||
    workflow?.detectedGame === 'violet' ||
    workflow?.detectedGame === 'za';
  const canEdit =
    workflow?.summary.availability === 'available' &&
    workflow.installStatus !== 'blocked' &&
    workflow.source?.status === 'available' &&
    isSupportedTypeChartGame(workflow.detectedGame) &&
    !isUninstallStaged &&
    !isStaging &&
    !isChangePlanCreating &&
    !isChangePlanApplying;
  const canStage = canEdit && isDirty;
  const canStageUninstall =
    supportsUninstall &&
    !isUninstallStaged &&
    workflow?.summary.availability === 'available' &&
    workflow.installStatus === 'modified' &&
    workflow.source?.status === 'available' &&
    !isDirty &&
    !isStaging &&
    !isChangePlanCreating &&
    !isChangePlanApplying;
  const canResetToVanilla =
    canEdit &&
    vanillaValues !== null &&
    !areTypeChartValuesEqual(draftValues, vanillaValues);
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
  const canDisplayChart = isDisplayableTypeChartWorkflow(workflow);

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const updateCell = (
    attackTypeIndex: number,
    defenseTypeIndex: number,
    value: TypeChartEffectivenessValue
  ) => {
    const index = attackTypeIndex * 18 + defenseTypeIndex;
    setDraftValues((current) => {
      const next = current.slice();
      next[index] = value;
      return next;
    });
  };

  return (
    <>
      <section aria-labelledby="type-chart-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Swords aria-hidden="true" size={18} />
          <h2 id="type-chart-heading">Type Chart</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <Metric
            label="Game"
            value={formatTypeChartGame(workflow?.detectedGame ?? null)}
          />
          <Metric
            label="Status"
            value={workflow ? formatBagHookStatus(workflow.installStatus) : 'Not loaded'}
          />
          <Metric label="Offset" value={workflow?.chartOffsetHex ?? 'Unknown'} />
          <Metric
            label="Build"
            value={
              workflow?.buildId && workflow.buildId !== 'unknown'
                ? workflow.buildId
                : 'Unknown'
            }
          />
          <Metric label="Staged" value={hasStagedChange ? 'Yes' : 'No'} />
        </div>

        {workflow && canDisplayChart ? (
          <div className="type-chart-editor">
            <div className="type-chart-scroll" aria-label="Type effectiveness chart">
              <div className="type-chart-grid">
                <div className="type-chart-axis-label">
                  <span className="type-chart-axis-line">
                    <span>{translateLiteral('Defending')}</span>
                    <span aria-hidden="true">→</span>
                  </span>
                  <span className="type-chart-axis-line">
                    <span>{translateLiteral('Attacking')}</span>
                    <span aria-hidden="true">↓</span>
                  </span>
                </div>
                {workflow.types.map((type) => (
                  <TypeChartTypeBadge key={`defense-${type.typeIndex}`} type={type} />
                ))}
                {workflow.types.map((attackType) => (
                  <Fragment key={`attack-row-${attackType.typeIndex}`}>
                    <TypeChartTypeBadge isRowHeader type={attackType} />
                    {workflow.types.map((defenseType) => {
                      const index = attackType.typeIndex * 18 + defenseType.typeIndex;
                      const value = draftValues[index] ?? 4;
                      return (
                        <TypeChartCellControl
                          attackTypeLabel={attackType.label}
                          defenseTypeLabel={defenseType.label}
                          disabled={!canEdit}
                          key={`${attackType.typeIndex}-${defenseType.typeIndex}`}
                          onChange={(nextValue) =>
                            updateCell(
                              attackType.typeIndex,
                              defenseType.typeIndex,
                              nextValue
                            )
                          }
                          value={value}
                        />
                      );
                    })}
                  </Fragment>
                ))}
              </div>
            </div>

            <div className="type-chart-actions">
              <button
                className="danger-button"
                disabled={!canResetToVanilla}
                onClick={() => {
                  if (vanillaValues) {
                    setDraftValues(vanillaValues);
                  }
                }}
                title="Reset the draft chart to the vanilla type-effectiveness values."
                type="button"
              >
                <RotateCcw aria-hidden="true" size={16} />
                <span>Reset to Vanilla Chart</span>
              </button>
              <button
                className="primary-button"
                disabled={!canStage}
                aria-busy={isStaging || undefined}
                onClick={() => onStageChart(draftValues.slice())}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage Type Chart'}</span>
              </button>
              {supportsUninstall ? (
                <button
                  aria-busy={isStaging || undefined}
                  className="danger-button"
                  disabled={!canStageUninstall}
                  onClick={onStageUninstall}
                  title="Restore only the Type Chart-owned bytes from base exefs/main."
                  type="button"
                >
                  <Trash2 aria-hidden="true" size={16} />
                  <span>{isStaging ? 'Staging' : 'Stage Uninstall'}</span>
                </button>
              ) : null}
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
                <dd>{formatTypeChartGame(workflow.detectedGame)}</dd>
              </div>
              <div>
                <dt>Build ID</dt>
                <dd data-localization-ignore="true">{formatTypeChartBuildId(workflow.buildId)}</dd>
              </div>
              <div>
                <dt>Offset</dt>
                <dd data-localization-ignore="true">{formatTypeChartOffset(workflow.chartOffsetHex)}</dd>
              </div>
              <div>
                <dt>Install message</dt>
                <dd>{workflow.installMessage}</dd>
              </div>
              <div>
                <dt>Staged</dt>
                <dd>
                  {stagedState?.kind === 'chart'
                    ? 'Type Chart'
                    : stagedState?.kind === 'uninstall'
                      ? 'Stage Uninstall'
                      : 'None'}
                </dd>
              </div>
              <div>
                <dt>Output files</dt>
                <dd>{workflow.stats.outputFileCount.toLocaleString()}</dd>
              </div>
            </dl>

            <TypeChartSourceSummary source={workflow.source} />
          </div>
        ) : workflow ? (
          <p className="empty-copy">
            <strong>{translateLiteral('Unavailable')}</strong>
            <br />
            <span>{workflow.installMessage}</span>
          </p>
        ) : (
          <p className="empty-copy">
            Open Type Chart from Advanced Editors to inspect the type-effectiveness table.
          </p>
        )}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}

function TypeChartTypeBadge({
  isRowHeader = false,
  type
}: {
  isRowHeader?: boolean;
  type: TypeChartWorkflow['types'][number];
}) {
  const { translateLiteral } = useLocalization();
  const localizedTypeLabel = translateLiteral(type.label);
  const displayLabel = isRowHeader
    ? localizedTypeLabel.toLocaleUpperCase()
    : translateLiteral(type.shortLabel);

  return (
    <div
      className={
        isRowHeader ? 'type-chart-type-badge type-chart-row-badge' : 'type-chart-type-badge'
      }
      style={{ backgroundColor: type.color }}
      title={localizedTypeLabel}
    >
      {displayLabel}
    </div>
  );
}

function TypeChartCellControl({
  attackTypeLabel,
  defenseTypeLabel,
  disabled,
  onChange,
  value
}: {
  attackTypeLabel: string;
  defenseTypeLabel: string;
  disabled: boolean;
  onChange: (value: TypeChartEffectivenessValue) => void;
  value: TypeChartEffectivenessValue;
}) {
  const { translateLiteral } = useLocalization();
  const option = getTypeChartEffectivenessOption(value);
  const label = `${translateLiteral(attackTypeLabel)} ${translateLiteral('attacking')} ${translateLiteral(
    defenseTypeLabel
  )}: ${translateLiteral(option.label)}`;

  return (
    <label
      className={`type-chart-cell type-chart-cell-${option.className}`}
      title={label}
    >
      <span aria-hidden="true">{option.display}</span>
      <select
        aria-label={label}
        disabled={disabled}
        onChange={(event) => {
          const nextValue = parseTypeChartEffectivenessValue(event.target.value);
          if (nextValue !== null) {
            onChange(nextValue);
          }
        }}
        value={value}
      >
        {typeChartEffectivenessOptions.map((candidate) => (
          <option key={candidate.value} value={candidate.value}>
            {translateLiteral(candidate.label)}
          </option>
        ))}
      </select>
    </label>
  );
}

function TypeChartSourceSummary({ source }: { source: TypeChartSourceRecord | null }) {
  if (!source) {
    return null;
  }

  return (
    <dl className="type-chart-source-summary">
      <div>
        <dt>Source</dt>
        <dd data-localization-ignore="true">{source.relativePath}</dd>
      </div>
      <div>
        <dt>Status</dt>
        <dd>{formatTypeChartSourceStatus(source.status)}</dd>
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

function getTypeChartEffectivenessOption(value: TypeChartEffectivenessValue) {
  return (
    typeChartEffectivenessOptions.find((option) => option.value === value) ??
    typeChartEffectivenessOptions[2]!
  );
}

function parseTypeChartEffectivenessValue(value: string) {
  const parsed = Number.parseInt(value, 10);
  return isTypeChartEffectivenessValue(parsed) ? parsed : null;
}

function isTypeChartEffectivenessValue(
  value: number
): value is TypeChartEffectivenessValue {
  return value === 0 || value === 2 || value === 4 || value === 8;
}

function isSupportedTypeChartGame(game: ProjectGame | null) {
  return (
    game === 'sword' ||
    game === 'shield' ||
    game === 'scarlet' ||
    game === 'violet' ||
    game === 'za'
  );
}

function isDisplayableTypeChartWorkflow(workflow: TypeChartWorkflow | null) {
  return Boolean(
    workflow &&
      workflow.source?.status === 'available' &&
      isSupportedTypeChartGame(workflow.detectedGame) &&
      workflow.buildId !== 'unknown' &&
      workflow.chartOffsetHex !== 'unknown' &&
      workflow.installStatus !== 'blocked' &&
      workflow.installStatus !== 'disabled'
  );
}

function formatTypeChartGame(game: ProjectGame | null) {
  switch (game) {
    case 'sword':
      return 'Pokemon Sword';
    case 'shield':
      return 'Pokemon Shield';
    case 'scarlet':
      return 'Pokemon Scarlet';
    case 'violet':
      return 'Pokemon Violet';
    case 'za':
      return 'Pokemon Legends Z-A';
    case null:
      return 'Unknown';
  }
}

function formatTypeChartBuildId(buildId: string) {
  return buildId !== 'unknown' ? buildId : 'Unknown';
}

function formatTypeChartOffset(offset: string) {
  return offset !== 'unknown' ? offset : 'Unknown';
}

function formatTypeChartSourceStatus(status: TypeChartSourceRecord['status']) {
  switch (status) {
    case 'available':
      return 'Available';
    case 'missing':
      return 'Missing';
  }
}

function createDefaultTypeChartValues(): TypeChartEffectivenessValue[] {
  return Array.from({ length: 18 * 18 }, () => 4 as TypeChartEffectivenessValue);
}

export function getTypeChartWorkflowValues(
  workflow: TypeChartWorkflow | null
): TypeChartEffectivenessValue[] | null {
  if (!workflow) {
    return null;
  }

  const values = createDefaultTypeChartValues();
  for (const cell of workflow.cells) {
    const index = cell.attackTypeIndex * 18 + cell.defenseTypeIndex;
    if (index >= 0 && index < values.length && isTypeChartEffectivenessValue(cell.effectiveness)) {
      values[index] = cell.effectiveness;
    }
  }

  return values;
}

export function getTypeChartVanillaValues(
  workflow: TypeChartWorkflow | null
): TypeChartEffectivenessValue[] | null {
  if (!workflow) {
    return null;
  }

  const values = createDefaultTypeChartValues();
  for (const cell of workflow.cells) {
    const index = cell.attackTypeIndex * 18 + cell.defenseTypeIndex;
    if (
      index >= 0 &&
      index < values.length &&
      isTypeChartEffectivenessValue(cell.vanillaEffectiveness)
    ) {
      values[index] = cell.vanillaEffectiveness;
    }
  }

  return values;
}

function areTypeChartValuesEqual(
  left: readonly TypeChartEffectivenessValue[],
  right: readonly TypeChartEffectivenessValue[]
) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}
