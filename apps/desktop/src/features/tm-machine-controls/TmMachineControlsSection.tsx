/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ClipboardCheck,
  Eye,
  EyeOff,
  FileWarning,
  ListChecks,
  LockKeyhole,
  Save,
  Sparkles,
  type LucideIcon
} from 'lucide-react';
import type { EditSession } from '../../bridge/contracts';
import type {
  TmMachineControlsWorkflow
} from '../../bridge/tmMachineControlsContracts';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import {
  getEffectiveTmMachineControlPolicy,
  getTmMachineControlPendingCount,
  getTmMachineControlSource,
  isTmMachineControlTargetActive,
  type TmMachineControlId,
  type TmMachineControlPolicy,
  type TmMachineControlStagingTarget
} from './tmMachineControlsUi';

export type { TmMachineControlStagingTarget } from './tmMachineControlsUi';

type ControlCardDefinition = {
  blockedKey: string;
  control: TmMachineControlId;
  descriptionKey: string;
  icon: LucideIcon;
  options: readonly [
    { icon: LucideIcon; labelKey: string; policy: TmMachineControlPolicy; target: TmMachineControlStagingTarget },
    { icon: LucideIcon; labelKey: string; policy: TmMachineControlPolicy; target: TmMachineControlStagingTarget }
  ];
  titleKey: string;
};

const controlCards: readonly ControlCardDefinition[] = [
  {
    blockedKey: 'tmMachineControls.recipe.blocked',
    control: 'recipeAvailability',
    descriptionKey: 'tmMachineControls.recipe.description',
    icon: ListChecks,
    options: [
      {
        icon: LockKeyhole,
        labelKey: 'tmMachineControls.recipe.progression',
        policy: 'progressionGated',
        target: 'recipeProgression'
      },
      {
        icon: Sparkles,
        labelKey: 'tmMachineControls.recipe.available',
        policy: 'allAvailable',
        target: 'recipeAvailable'
      }
    ],
    titleKey: 'tmMachineControls.recipe.title'
  },
  {
    blockedKey: 'tmMachineControls.material.blocked',
    control: 'materialVisibility',
    descriptionKey: 'tmMachineControls.material.description',
    icon: Eye,
    options: [
      {
        icon: EyeOff,
        labelKey: 'tmMachineControls.material.discovery',
        policy: 'discoveryGated',
        target: 'materialDiscovery'
      },
      {
        icon: Eye,
        labelKey: 'tmMachineControls.material.visible',
        policy: 'alwaysVisible',
        target: 'materialVisible'
      }
    ],
    titleKey: 'tmMachineControls.material.title'
  }
] as const;

export function TmMachineControlsSection({
  editSession,
  hasConflictingEditSession,
  isChangePlanApplying,
  isChangePlanCreating,
  onApplyChangePlan,
  onCreateChangePlan,
  onStage,
  panelOutput,
  stagingTarget,
  workflow
}: {
  editSession: EditSession | null;
  hasConflictingEditSession: boolean;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onStage: (target: TmMachineControlStagingTarget) => void;
  panelOutput: WorkflowPanelOutput;
  stagingTarget: TmMachineControlStagingTarget | null;
  workflow: TmMachineControlsWorkflow | null;
}) {
  const { t } = useLocalization();
  const pendingControlCount = getTmMachineControlPendingCount(editSession);
  const isBusy =
    stagingTarget !== null || isChangePlanCreating || isChangePlanApplying;
  const hasReviewableChanges = (editSession?.pendingEdits.length ?? 0) > 0;
  const canReviewPlan = hasReviewableChanges && !hasConflictingEditSession && !isBusy;
  const canApplyPlan =
    hasReviewableChanges &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !hasConflictingEditSession &&
    !isBusy;
  usePublishCommonEditorError({
    domain: 'workflow.tmMachineControls',
    field: 'editSession',
    message: hasConflictingEditSession ? t('tmMachineControls.sessionConflict') : null
  });

  return (
    <>
      <section aria-labelledby="tm-machine-controls-heading" className="panel wide-panel">
        <div className="panel-heading">
          <ListChecks aria-hidden="true" size={18} />
          <div>
            <h2 id="tm-machine-controls-heading">{t('tmMachineControls.title')}</h2>
            <p className="tm-machine-controls-heading-copy">
              {t('tmMachineControls.description')}
            </p>
          </div>
        </div>

        <div className="items-toolbar tm-machine-controls-metrics">
          <Metric
            label={t('tmMachineControls.metric.build')}
            value={workflow?.supportedBuild ?? t('tmMachineControls.notLoaded')}
            valueIsRaw={workflow !== null}
          />
          <Metric
            label={t('tmMachineControls.metric.recipes')}
            value={workflow?.stats.recipeCount.toString() ?? '0'}
          />
          <Metric
            label={t('tmMachineControls.metric.sources')}
            value={workflow?.stats.sourceFileCount.toString() ?? '0'}
          />
          <Metric
            label={t('tmMachineControls.metric.staged')}
            value={pendingControlCount.toString()}
          />
        </div>

        {hasConflictingEditSession ? (
          <div className="tm-machine-control-blocked tm-machine-control-session-blocked" role="alert">
            <FileWarning aria-hidden="true" size={18} />
            <p>{t('tmMachineControls.sessionConflict')}</p>
          </div>
        ) : null}

        {workflow ? (
          <div className="tm-machine-controls-grid">
            {controlCards.map((card) => {
              const state = workflow[card.control];
              const source = getTmMachineControlSource(workflow, card.control);
              const effectivePolicy = getEffectiveTmMachineControlPolicy(state);
              const CardIcon = card.icon;
              return (
                <article
                  aria-labelledby={`tm-machine-control-${card.control}-heading`}
                  className={`tm-machine-control-card tm-machine-control-card-${state.status}`}
                  key={card.control}
                >
                  <header className="tm-machine-control-card-heading">
                    <span className="tm-machine-control-icon" aria-hidden="true">
                      <CardIcon size={20} />
                    </span>
                    <div>
                      <h3 id={`tm-machine-control-${card.control}-heading`}>
                        {t(card.titleKey)}
                      </h3>
                      <p>{t(card.descriptionKey)}</p>
                    </div>
                    <span
                      className={`tm-machine-control-status tm-machine-control-status-${state.status}`}
                    >
                      {t(`tmMachineControls.status.${state.status}`)}
                    </span>
                  </header>

                  <div aria-live="polite" className="tm-machine-control-effective">
                    <span>{t('tmMachineControls.effective')}</span>
                    <strong>{t(`tmMachineControls.policy.${effectivePolicy}`)}</strong>
                    {state.stagedPolicy ? (
                      <span className="tm-machine-control-staged">
                        {t('tmMachineControls.staged')}
                      </span>
                    ) : null}
                  </div>

                  {!state.canStage ? (
                    <div className="tm-machine-control-blocked" role="alert">
                      <FileWarning aria-hidden="true" size={18} />
                      <p>{t(card.blockedKey)}</p>
                    </div>
                  ) : null}

                  <dl className="tm-machine-control-source">
                    <div>
                      <dt>{t('tmMachineControls.source.file')}</dt>
                      {source ? (
                        <dd data-localization-ignore="true">{source.sourceFile}</dd>
                      ) : (
                        <dd>{t('tmMachineControls.source.unavailable')}</dd>
                      )}
                    </div>
                    <div>
                      <dt>{t('tmMachineControls.source.layer')}</dt>
                      <dd>
                        {source
                          ? t(`tmMachineControls.layer.${source.sourceLayer}`)
                          : t('tmMachineControls.source.unavailable')}
                      </dd>
                    </div>
                    <div>
                      <dt>{t('tmMachineControls.source.match')}</dt>
                      <dd>
                        {t('tmMachineControls.source.matchValue', {
                          matching: state.matchingRecordCount,
                          total: state.totalRecordCount
                        })}
                      </dd>
                    </div>
                  </dl>

                  <div
                    aria-label={t('tmMachineControls.actions', {
                      control: t(card.titleKey)
                    })}
                    className="tm-machine-control-options"
                    role="group"
                  >
                    {card.options.map((option) => {
                      const OptionIcon = option.icon;
                      const isActive = isTmMachineControlTargetActive(state, option.policy);
                      const isStagingThis = stagingTarget === option.target;
                      return (
                        <button
                          aria-busy={isStagingThis || undefined}
                          aria-pressed={isActive}
                          className={`tm-machine-control-option ${isActive ? 'selected' : ''}`}
                          disabled={
                            !state.canStage ||
                            workflow.summary.availability !== 'available' ||
                            hasConflictingEditSession ||
                            isBusy ||
                            isActive
                          }
                          key={option.target}
                          onClick={() => onStage(option.target)}
                          type="button"
                        >
                          <OptionIcon aria-hidden="true" size={17} />
                          <span>
                            {isStagingThis
                              ? t('tmMachineControls.staging')
                              : t(option.labelKey)}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                </article>
              );
            })}
          </div>
        ) : (
          <p className="empty-copy">{t('tmMachineControls.empty')}</p>
        )}

        <div className="tm-machine-controls-review-actions">
          <button
            className="secondary-button"
            disabled={!canReviewPlan}
            onClick={onCreateChangePlan}
            type="button"
          >
            <ClipboardCheck aria-hidden="true" size={16} />
            <span>
              {isChangePlanCreating
                ? t('tmMachineControls.reviewing')
                : t('tmMachineControls.review')}
            </span>
          </button>
          <button
            className="primary-button"
            disabled={!canApplyPlan}
            onClick={onApplyChangePlan}
            type="button"
          >
            <Save aria-hidden="true" size={16} />
            <span>
              {isChangePlanApplying
                ? t('tmMachineControls.applying')
                : t('tmMachineControls.apply')}
            </span>
          </button>
          <p>{t('tmMachineControls.reviewHelp')}</p>
        </div>
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}
