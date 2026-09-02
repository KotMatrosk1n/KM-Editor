/* SPDX-License-Identifier: GPL-3.0-only */

import { ArrowLeftRight, Layers3, ShieldCheck } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { EditSession } from '../../bridge/contracts';
import type {
  TrainerPoolRecord,
  TrainerPoolsWorkflow
} from '../../bridge/trainerPoolsContracts';
import {
  FocusedEditorMetrics,
  FocusedEditorWorkspace
} from '../../components/FocusedEditorWorkspace';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { DiagnosticsSection, Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';

type TrainerPoolsSectionProps = {
  editSession: EditSession | null;
  isStaging: boolean;
  onOpenChanges: () => void;
  onStageSwap: (selection: {
    destinationLogicalPoolId: string;
    destinationRawTrainerId: string;
    sourceLogicalPoolId: string;
    sourceRawTrainerId: string;
  }) => Promise<boolean>;
  workflow: TrainerPoolsWorkflow | null;
};

type PoolMemberSelection = {
  logicalPoolId: string;
  rawTrainerId: string;
};

export function TrainerPoolsSection({
  editSession,
  isStaging,
  onOpenChanges,
  onStageSwap,
  workflow
}: TrainerPoolsSectionProps) {
  const { t } = useLocalization();
  const [source, setSource] = useState<PoolMemberSelection | null>(null);
  const [destination, setDestination] = useState<PoolMemberSelection | null>(null);
  const [actionFeedback, setActionFeedback] = useState<
    { kind: 'error' | 'success'; message: string } | null
  >(null);
  const [isStagePending, setIsStagePending] = useState(false);
  usePublishCommonEditorError({
    domain: 'workflow.trainerPools',
    field: 'identitySwap',
    message: actionFeedback?.kind === 'error' ? actionFeedback.message : null
  });
  const actionFeedbackRef = useRef<HTMLDivElement | null>(null);
  const stageOperationRef = useRef<symbol | null>(null);
  const selectionRef = useRef({ destination, source });
  selectionRef.current = { destination, source };

  useEffect(() => () => {
    stageOperationRef.current = null;
  }, []);

  useEffect(() => {
    if (!workflow) {
      setSource(null);
      setDestination(null);
      return;
    }

    setSource((current) => keepValidSelection(workflow.pools, current));
    setDestination((current) => keepValidSelection(workflow.pools, current));
  }, [workflow]);

  const sourcePool = findPool(workflow?.pools ?? [], source?.logicalPoolId ?? null);
  const destinationPool = findPool(
    workflow?.pools ?? [],
    destination?.logicalPoolId ?? null
  );
  const sourceMember = findMember(sourcePool, source?.rawTrainerId ?? null);
  const destinationMember = findMember(destinationPool, destination?.rawTrainerId ?? null);
  const hasPendingSwap = editSession?.pendingEdits.some(
    (edit) => edit.domain === 'workflow.trainerPools'
  ) === true;
  const isStageLocked = isStaging || isStagePending;
  useEffect(() => {
    if (
      sourcePool &&
      destinationPool &&
      sourcePool.compatibilityGroup !== destinationPool.compatibilityGroup
    ) {
      setDestination(null);
    }
  }, [destinationPool, sourcePool]);
  const canStage =
    workflow?.canStage === true &&
    source !== null &&
    destination !== null &&
    (source.logicalPoolId !== destination.logicalPoolId ||
      source.rawTrainerId !== destination.rawTrainerId) &&
    !hasPendingSwap &&
    !isStaging &&
    !isStagePending;

  const handleStageSwap = async () => {
    if (!source || !destination || stageOperationRef.current !== null) return;
    const operationToken = Symbol('trainer-pool-stage');
    stageOperationRef.current = operationToken;
    const requestedSource = source;
    const requestedDestination = destination;
    setActionFeedback(null);
    setIsStagePending(true);
    let didSucceed = false;
    try {
      didSucceed = await onStageSwap({
        destinationLogicalPoolId: destination.logicalPoolId,
        destinationRawTrainerId: destination.rawTrainerId,
        sourceLogicalPoolId: source.logicalPoolId,
        sourceRawTrainerId: source.rawTrainerId
      });
    } catch {
      didSucceed = false;
    }
    if (stageOperationRef.current !== operationToken) return;
    stageOperationRef.current = null;
    setIsStagePending(false);
    if (
      !sameSelection(selectionRef.current.source, requestedSource) ||
      !sameSelection(selectionRef.current.destination, requestedDestination)
    ) {
      return;
    }
    setActionFeedback({
      kind: didSucceed ? 'success' : 'error',
      message: didSucceed
        ? t('trainerPools.swap.success')
        : t('trainerPools.swap.failure')
    });
    window.requestAnimationFrame(() => actionFeedbackRef.current?.focus());
  };

  const poolGroups = useMemo(() => {
    const pools = workflow?.pools ?? [];
    return {
      infinity: pools.filter((pool) => pool.kind === 'infinity'),
      story: pools.filter((pool) => pool.kind === 'story')
    };
  }, [workflow]);

  if (!workflow) {
    return (
      <FocusedEditorWorkspace className="trainer-pools-workspace">
        <section aria-labelledby="trainer-pools-heading" className="panel wide-panel">
          <div className="panel-heading">
            <ArrowLeftRight aria-hidden="true" size={18} />
            <h2 id="trainer-pools-heading">{t('trainerPools.title')}</h2>
          </div>
          <p className="empty-copy focused-editor-readable-copy">
            {t('trainerPools.empty')}
          </p>
        </section>
      </FocusedEditorWorkspace>
    );
  }

  return (
    <FocusedEditorWorkspace className="trainer-pools-workspace">
      <section aria-labelledby="trainer-pools-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ArrowLeftRight aria-hidden="true" size={18} />
          <h2 id="trainer-pools-heading">{t('trainerPools.title')}</h2>
          <span className="status-badge ready">{t('trainerPools.fixedCountBadge')}</span>
        </div>
        <p className="section-copy focused-editor-readable-copy">
          {t('trainerPools.description')}
        </p>
        <FocusedEditorMetrics>
          <Metric label={t('trainerPools.metrics.logicalPools')} value={String(workflow.stats.logicalPoolCount)} />
          <Metric label={t('trainerPools.metrics.physicalMirrors')} value={String(workflow.stats.physicalMirrorCount)} />
          <Metric label={t('trainerPools.metrics.members')} value={String(workflow.stats.memberReferenceCount)} />
          <Metric label={t('trainerPools.metrics.dormantMirrors')} value={String(workflow.stats.dormantPhysicalMirrorCount)} />
        </FocusedEditorMetrics>
      </section>

      <section aria-labelledby="trainer-pools-swap-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Layers3 aria-hidden="true" size={18} />
          <h2 id="trainer-pools-swap-heading">{t('trainerPools.swap.title')}</h2>
        </div>
        <div className="trainer-pools-selection-grid">
          <PoolSelection
            destination="source"
            disabled={isStageLocked}
            groups={poolGroups}
            label={t('trainerPools.swap.source')}
            onChange={(nextSource) => {
              if (stageOperationRef.current === null) setSource(nextSource);
            }}
            selection={source}
          />
          <div aria-hidden="true" className="trainer-pools-swap-icon">
            <ArrowLeftRight size={22} />
          </div>
          <PoolSelection
            compatibleWith={sourcePool?.compatibilityGroup ?? null}
            destination="destination"
            disabled={isStageLocked}
            groups={poolGroups}
            label={t('trainerPools.swap.destination')}
            onChange={(nextDestination) => {
              if (stageOperationRef.current === null) setDestination(nextDestination);
            }}
            selection={destination}
          />
        </div>

        <div className="trainer-pools-swap-summary" role="status">
          <ShieldCheck aria-hidden="true" size={18} />
          <span>
            {sourceMember && destinationMember
              ? t('trainerPools.swap.summary', {
                  destination: destinationMember.displayName,
                  source: sourceMember.displayName
                })
              : t('trainerPools.swap.instructions')}
          </span>
        </div>
        <p className="field-hint focused-editor-readable-copy">
          {t('trainerPools.swap.preservation')}
        </p>
        {hasPendingSwap ? (
          <p className="field-warning" role="status">
            {t('trainerPools.swap.pendingBlocked')}
          </p>
        ) : null}
        {actionFeedback ? (
          <div
            className={`inline-feedback ${actionFeedback.kind}`}
            ref={actionFeedbackRef}
            role={actionFeedback.kind === 'error' ? 'alert' : 'status'}
            tabIndex={-1}
          >
            {actionFeedback.message}
          </div>
        ) : null}
        <div className="button-row">
          <button
            aria-busy={isStageLocked || undefined}
            className="primary-button"
            disabled={!canStage}
            onClick={handleStageSwap}
            type="button"
          >
            {isStaging || isStagePending
              ? t('trainerPools.swap.staging')
              : t('trainerPools.swap.stage')}
          </button>
          {editSession && editSession.pendingEdits.length > 0 ? (
            <button
              className="secondary-button"
              disabled={isStageLocked}
              onClick={onOpenChanges}
              type="button"
            >
              {t('trainerPools.openChanges', { count: editSession.pendingEdits.length })}
            </button>
          ) : null}
        </div>
      </section>

      <DiagnosticsSection diagnostics={workflow.diagnostics} />
    </FocusedEditorWorkspace>
  );
}

function PoolSelection({
  compatibleWith,
  destination,
  disabled,
  groups,
  label,
  onChange,
  selection
}: {
  compatibleWith?: string | null;
  destination: string;
  disabled: boolean;
  groups: { infinity: TrainerPoolRecord[]; story: TrainerPoolRecord[] };
  label: string;
  onChange: (selection: PoolMemberSelection | null) => void;
  selection: PoolMemberSelection | null;
}) {
  const { t } = useLocalization();
  const pools = [...groups.story, ...groups.infinity];
  const pool = findPool(pools, selection?.logicalPoolId ?? null);
  const poolSelectId = `trainer-pools-${destination}-pool`;
  const memberSelectId = `trainer-pools-${destination}-member`;

  return (
    <fieldset className="trainer-pools-selection-card" disabled={disabled}>
      <legend>{label}</legend>
      <label htmlFor={poolSelectId}>{t('trainerPools.swap.pool')}</label>
      <select
        className="km-select-control"
        id={poolSelectId}
        onChange={(event) => {
          const nextPool = findPool(pools, event.target.value);
          onChange(
            nextPool?.members[0]
              ? {
                  logicalPoolId: nextPool.logicalPoolId,
                  rawTrainerId: nextPool.members[0].rawTrainerId
                }
              : null
          );
        }}
        value={selection?.logicalPoolId ?? ''}
      >
        <option value="">{t('trainerPools.swap.choosePool')}</option>
        {groups.story.length > 0 ? (
          <optgroup label={t('trainerPools.kind.story')}>
            {groups.story.map((candidate) => (
              <option
                disabled={
                  Boolean(compatibleWith) && candidate.compatibilityGroup !== compatibleWith
                }
                key={candidate.logicalPoolId}
                value={candidate.logicalPoolId}
              >
                {formatPoolLabel(candidate)}
                {compatibleWith && candidate.compatibilityGroup !== compatibleWith
                  ? ` · ${t('trainerPools.swap.incompatible')}`
                  : ''}
              </option>
            ))}
          </optgroup>
        ) : null}
        {groups.infinity.length > 0 ? (
          <optgroup label={t('trainerPools.kind.infinity')}>
            {groups.infinity.map((candidate) => (
              <option
                disabled={
                  Boolean(compatibleWith) && candidate.compatibilityGroup !== compatibleWith
                }
                key={candidate.logicalPoolId}
                value={candidate.logicalPoolId}
              >
                {formatPoolLabel(candidate)}
                {compatibleWith && candidate.compatibilityGroup !== compatibleWith
                  ? ` · ${t('trainerPools.swap.incompatible')}`
                  : ''}
              </option>
            ))}
          </optgroup>
        ) : null}
      </select>

      <label htmlFor={memberSelectId}>{t('trainerPools.swap.trainer')}</label>
      <select
        className="km-select-control"
        disabled={!pool}
        id={memberSelectId}
        onChange={(event) =>
          onChange(
            pool
              ? { logicalPoolId: pool.logicalPoolId, rawTrainerId: event.target.value }
              : null
          )
        }
        value={selection?.rawTrainerId ?? ''}
      >
        <option value="">{t('trainerPools.swap.chooseTrainer')}</option>
        {pool?.members.map((member) => (
          <option key={member.rawTrainerId} value={member.rawTrainerId}>
            {member.displayName} · {t('trainerPools.swap.rank', { rank: member.storedRank })} · {t('trainerPools.swap.teamSize', { count: member.teamSize })}
          </option>
        ))}
      </select>
      {pool ? (
        <p className="field-hint">
          {t('trainerPools.swap.poolDetails', {
            count: pool.memberCount,
            mirrors: pool.referencedPhysicalTableCount,
            weight: pool.totalWeight
          })}
        </p>
      ) : null}
    </fieldset>
  );
}

function keepValidSelection(
  pools: readonly TrainerPoolRecord[],
  selection: PoolMemberSelection | null
) {
  if (!selection) return null;
  const pool = findPool(pools, selection.logicalPoolId);
  return findMember(pool, selection.rawTrainerId) ? selection : null;
}

function sameSelection(
  left: PoolMemberSelection | null,
  right: PoolMemberSelection | null
) {
  return left?.logicalPoolId === right?.logicalPoolId &&
    left?.rawTrainerId === right?.rawTrainerId;
}

function findPool(pools: readonly TrainerPoolRecord[], logicalPoolId: string | null) {
  return logicalPoolId
    ? pools.find((pool) => pool.logicalPoolId === logicalPoolId) ?? null
    : null;
}

function findMember(pool: TrainerPoolRecord | null, rawTrainerId: string | null) {
  return rawTrainerId
    ? pool?.members.find((member) => member.rawTrainerId === rawTrainerId) ?? null
    : null;
}

function formatPoolLabel(pool: TrainerPoolRecord) {
  return pool.displayLabel;
}
