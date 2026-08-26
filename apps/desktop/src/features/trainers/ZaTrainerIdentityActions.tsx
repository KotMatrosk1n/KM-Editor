/* SPDX-License-Identifier: GPL-3.0-only */

import { Languages, ShieldAlert, UserRoundCog } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useLocalization } from '../../localization';

export type ZaTrainerTextTargetViewModel = {
  kind: string;
  lineIndex: number;
  messageKey: string;
  sharedTrainerCount: number;
};

export type ZaTrainerClassPairOptionViewModel = {
  label: string;
  pairId: string;
  presentationCanaryRequired: boolean;
  usageCount: number;
};

export type ZaTrainerIdentityViewModel = {
  canReassignClass: boolean;
  classPairId: string | null;
  classReassignmentBlockedReason: string | null;
  classTextTarget: ZaTrainerTextTargetViewModel | null;
  name: string;
  nameTextTarget: ZaTrainerTextTargetViewModel | null;
  trainerClass: string;
};

export function ZaTrainerIdentityActions({
  canStageChanges,
  classPairOptions,
  isUpdating,
  onNavigateTextTarget,
  onStageClassPair,
  trainer
}: {
  canStageChanges: boolean;
  classPairOptions: readonly ZaTrainerClassPairOptionViewModel[];
  isUpdating: boolean;
  onNavigateTextTarget: (target: ZaTrainerTextTargetViewModel) => Promise<boolean>;
  onStageClassPair: (pairId: string) => Promise<boolean>;
  trainer: ZaTrainerIdentityViewModel;
}) {
  const { t } = useLocalization();
  const [selectedPairId, setSelectedPairId] = useState(trainer.classPairId ?? '');
  const [feedback, setFeedback] = useState<{ kind: 'error' | 'success'; text: string } | null>(null);
  const feedbackRef = useRef<HTMLParagraphElement | null>(null);

  useEffect(() => {
    setSelectedPairId(trainer.classPairId ?? '');
    setFeedback(null);
  }, [trainer.classPairId]);

  const selectedOption = classPairOptions.find((option) => option.pairId === selectedPairId);
  const canStage =
    canStageChanges &&
    trainer.canReassignClass &&
    selectedOption !== undefined &&
    selectedPairId !== trainer.classPairId &&
    !isUpdating;

  return (
    <section aria-labelledby="za-trainer-identity-heading" className="trainer-identity-actions">
      <div className="panel-heading compact-heading">
        <UserRoundCog aria-hidden="true" size={17} />
        <h3 id="za-trainer-identity-heading">{t('trainers.identity.title')}</h3>
      </div>
      <p className="field-hint">{t('trainers.identity.description')}</p>

      <div className="trainer-identity-action-grid">
        <div className="trainer-identity-action-card">
          <strong>{trainer.name}</strong>
          <button
            className="secondary-button"
            disabled={!trainer.nameTextTarget || isUpdating}
            onClick={async () => {
              if (!trainer.nameTextTarget) return;
              setFeedback(null);
              const opened = await onNavigateTextTarget(trainer.nameTextTarget);
              if (!opened) setFeedback({ kind: 'error', text: t('trainers.identity.navigationFailure') });
            }}
            type="button"
          >
            <Languages aria-hidden="true" size={16} />
            {t('trainers.identity.editName')}
          </button>
          {trainer.nameTextTarget ? (
            trainer.nameTextTarget.sharedTrainerCount > 1 ? (
              <p className="field-warning">
                {t('trainers.identity.sharedName', {
                  count: trainer.nameTextTarget.sharedTrainerCount
                })}
              </p>
            ) : null
          ) : (
            <p className="field-hint">{t('trainers.identity.generatedName')}</p>
          )}
        </div>

        <div className="trainer-identity-action-card">
          <strong>{trainer.trainerClass}</strong>
          <button
            className="secondary-button"
            disabled={!trainer.classTextTarget || isUpdating}
            onClick={async () => {
              if (!trainer.classTextTarget) return;
              setFeedback(null);
              const opened = await onNavigateTextTarget(trainer.classTextTarget);
              if (!opened) setFeedback({ kind: 'error', text: t('trainers.identity.navigationFailure') });
            }}
            type="button"
          >
            <Languages aria-hidden="true" size={16} />
            {trainer.classTextTarget?.kind === 'hyperspaceArchetype'
              ? t('trainers.identity.editArchetype')
              : t('trainers.identity.editClassLabel')}
          </button>
          {trainer.classTextTarget && trainer.classTextTarget.sharedTrainerCount > 1 ? (
            <p className="field-warning">
              {t('trainers.identity.sharedClass', {
                count: trainer.classTextTarget.sharedTrainerCount
              })}
            </p>
          ) : null}
        </div>
      </div>

      <div className="trainer-class-pair-editor">
        <label htmlFor="za-trainer-class-pair">{t('trainers.identity.classPair')}</label>
        <select
          className="km-select-control"
          disabled={!canStageChanges || !trainer.canReassignClass || isUpdating}
          id="za-trainer-class-pair"
          onChange={(event) => {
            setSelectedPairId(event.target.value);
            setFeedback(null);
          }}
          value={selectedPairId}
        >
          {classPairOptions.map((option) => (
            <option key={option.pairId} value={option.pairId}>
              {option.label} · {t('trainers.identity.classUsage', { count: option.usageCount })}
            </option>
          ))}
        </select>
        <button
          className="primary-button"
          disabled={!canStage}
          onClick={async () => {
            setFeedback(null);
            const didSucceed = await onStageClassPair(selectedPairId);
            setFeedback({
              kind: didSucceed ? 'success' : 'error',
              text: didSucceed
                ? t('trainers.identity.classStageSuccess')
                : t('trainers.identity.classStageFailure')
            });
            window.requestAnimationFrame(() => feedbackRef.current?.focus());
          }}
          type="button"
        >
          {isUpdating ? t('trainers.identity.classStaging') : t('trainers.identity.classStage')}
        </button>
        {!canStageChanges && trainer.canReassignClass ? (
          <p className="field-hint">{t('trainers.identity.editSessionRequired')}</p>
        ) : !trainer.canReassignClass ? (
          <p className="field-hint">
            {trainer.classReassignmentBlockedReason === 'hyperspaceArchetype'
              ? t('trainers.identity.classUnavailableHyperspace')
              : trainer.classReassignmentBlockedReason === 'unresolvedClassPair'
                ? t('trainers.identity.classUnavailableUnresolved')
                : t('trainers.identity.classUnavailable')}
          </p>
        ) : selectedOption?.presentationCanaryRequired === true ? (
          <p className="field-warning">
            <ShieldAlert aria-hidden="true" size={15} />
            {t('trainers.identity.presentationCanary')}
          </p>
        ) : null}
      </div>

      <p className="field-hint">{t('trainers.identity.appearanceGated')}</p>
      {feedback ? (
        <p
          className={`inline-feedback ${feedback.kind}`}
          ref={feedbackRef}
          role={feedback.kind === 'error' ? 'alert' : 'status'}
          tabIndex={-1}
        >
          {feedback.text}
        </p>
      ) : null}
    </section>
  );
}
