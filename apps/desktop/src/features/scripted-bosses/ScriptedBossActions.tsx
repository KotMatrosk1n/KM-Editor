/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertTriangle, Binary, CheckCircle, Lock, Pencil, ShieldAlert } from 'lucide-react';
import { type ReactNode } from 'react';
import {
  type MoveRecord,
  type ScriptedBossAction,
  type ScriptedBossAffectedScope,
  type ScriptedBossMoveOption,
  type ScriptedBossProfile,
  type ScriptedEncounterMoveOwnership
} from '../../bridge/contracts';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization/LocalizationProvider';

const phaseAvailabilityStates = [
  'available',
  'unavailable',
  'context-only',
  'unverified'
] as const;

const phaseAvailabilitySearchLabels = {
  available: 'available',
  'context-only': 'context only',
  unavailable: 'locked',
  unverified: 'unverified'
} as const;

type Localize = (
  key: string,
  params?: Record<string, string | number>
) => string;

type ScriptedBossPhase = ScriptedBossProfile['phaseModel']['phases'][number];

export function findScriptedBossProfile(
  profiles: ScriptedBossProfile[],
  speciesId: number,
  form: number,
  lineageKey: string | null = null
) {
  const normalizedLineageKey = lineageKey?.trim().toLocaleLowerCase() ?? '';
  if (normalizedLineageKey.length > 0) {
    return profiles.find(
      (profile) => profile.lineageKey.toLocaleLowerCase() === normalizedLineageKey
    ) ?? null;
  }

  return profiles.find(
    (profile) => profile.speciesId === speciesId && profile.form === form
  ) ?? null;
}

export function getScriptedBossOwners(
  profiles: ScriptedBossProfile[],
  moveId: number,
  variant?: number
) {
  return profiles.flatMap((profile) =>
    profile.actions
      .filter(
        (action) =>
          action.moveId === moveId && (variant === undefined || action.variant === variant)
      )
      .map((action) => ({ action, profile }))
  );
}

export function getScriptedBossActionOwners(
  profiles: ScriptedBossProfile[],
  selectorActionId: number
) {
  return profiles.filter((profile) =>
    profile.actions.some((action) => action.selectorActionId === selectorActionId)
  );
}

export function getScriptedBossMoveSearchValues(
  profiles: ScriptedBossProfile[],
  moveId: number,
  localize?: Localize
) {
  return getScriptedBossOwners(profiles, moveId).flatMap(({ action, profile }) => {
    const phasesByKey = new Map(
      profile.phaseModel.phases.map((phase) => [phase.key, phase])
    );
    const variantLabel = action.variant === null
      ? ''
      : formatScriptedBossRuntimeVariantLabel(action.variant, localize);
    return [
      profile.key,
      profile.name,
      profile.speciesId.toString(),
      profile.phaseModel.state,
      profile.phaseModel.kind,
      action.kind,
      action.runtimeState,
      action.compatibilityState,
      action.compatibilityReason ?? '',
      action.lockReason ?? '',
      action.phaseContext ?? '',
      action.selectorActionId?.toString() ?? '',
      action.vanillaMoveId?.toString() ?? '',
      action.variant?.toString() ?? '',
      variantLabel,
      ...profile.phaseModel.phases.flatMap((phase) => [
        phase.key,
        phase.stageName,
        `stage ${phase.stage}`,
        `phase ${phase.hpPhase}`,
        `${phase.minimumHpPercent} ${phase.maximumHpPercent}`
      ]),
      ...action.phaseAvailability.flatMap((availability) => {
        const stateLabel = phaseAvailabilitySearchLabels[availability.state];
        const phase = phasesByKey.get(availability.phaseKey);
        const localizedState = localize?.(
          `za.encounters.bossActions.phase.state.${availability.state}`
        );
        return [
          availability.phaseKey,
          phase?.stageName ?? '',
          phase ? `stage ${phase.stage}` : '',
          phase ? `phase ${phase.hpPhase}` : '',
          `${stateLabel}`,
          localizedState ?? '',
          availability.state
        ];
      })
    ];
  });
}

export function getScriptedBossMoveCompatibility(
  option: ScriptedBossMoveOption,
  selectorActionId: number
): {
  reason: ScriptedBossAction['compatibilityReason'];
  state: ScriptedBossAction['compatibilityState'];
} {
  const compatibility = option.selectorCompatibilities.find(
    (candidate) => candidate.selectorActionId === selectorActionId
  );
  return compatibility ?? {
    reason: null,
    state: option.defaultCompatibilityState
  };
}

export function ScriptedBossEncounterActions({
  moveOwnership,
  profile,
  profiles,
  renderActionControl
}: {
  moveOwnership?: ScriptedEncounterMoveOwnership | null;
  profile: ScriptedBossProfile | null;
  profiles: ScriptedBossProfile[];
  renderActionControl?: (action: ScriptedBossAction) => ReactNode;
}) {
  const { t } = useLocalization();
  const editableCount = profile?.actions.filter((action) => action.canEdit).length ?? 0;
  const lockedCount = (profile?.actions.length ?? 0) - editableCount;
  const hasBrokenAction = profile?.actions.some(isBrokenScriptedBossAction) ?? false;
  const hasUnavailableAction = profile?.actions.some(isUnavailableScriptedBossAction) ?? false;
  const hasKnownIncompatibleAction = profile?.actions.some(
    (action) => action.compatibilityState === 'known-incompatible'
  ) ?? false;
  const phaseGroups = profile === null ? [] : groupScriptedBossPhases(profile);
  const hasVerifiedPhaseModel = profile?.phaseModel.state === 'verified';
  const hasSingleStageHpBands = Boolean(
    profile?.phaseModel.kind === 'hp-bands' && phaseGroups.length === 1
  );
  const profileName = profile?.scope === 'verified-scripted-follower'
    ? t('za.encounters.bossActions.followerProfileName', { pokemon: profile.name })
    : profile?.name;

  return (
    <section
      aria-label={t('za.encounters.bossActions.heading')}
      className={`za-scripted-boss-actions ${
        profile === null
          ? 'za-scripted-boss-actions-unverified'
          : hasBrokenAction
            ? 'za-scripted-boss-actions-broken'
            : hasUnavailableAction
              ? 'za-scripted-boss-actions-unavailable'
              : hasKnownIncompatibleAction
                ? 'za-scripted-boss-actions-incompatible'
                : editableCount > 0
                  ? 'za-scripted-boss-actions-editable'
                  : 'za-scripted-boss-actions-locked'
      }`}
    >
      <div className="za-scripted-boss-actions-heading">
        {profile === null || hasBrokenAction || hasUnavailableAction || hasKnownIncompatibleAction ? (
          <AlertTriangle aria-hidden="true" size={18} />
        ) : editableCount > 0 ? (
          <Pencil aria-hidden="true" size={18} />
        ) : (
          <Lock aria-hidden="true" size={18} />
        )}
        <div>
          <h4>{t('za.encounters.bossActions.heading')}</h4>
          <p>
            {t(
              profile === null
                ? 'za.encounters.bossActions.status.unverifiedHelp'
                : hasBrokenAction
                  ? 'za.encounters.bossActions.status.brokenHelp'
                  : hasUnavailableAction
                    ? 'za.encounters.bossActions.status.unavailableHelp'
                    : hasKnownIncompatibleAction
                      ? 'za.encounters.bossActions.compatibility.profileUnsafeHelp'
                      : editableCount > 0
                        ? 'za.encounters.bossActions.status.editableHelp'
                        : 'za.encounters.bossActions.status.lockedHelp'
            )}
          </p>
        </div>
      </div>

      {profile ? (
        <>
          <div className="za-scripted-boss-profile-summary">
            <div>
              <strong>{profileName}</strong>
              <span className="editable-field-label-row">
                <span>{t(
                  profile.scope === 'base-rogue-mega'
                    ? 'za.encounters.bossActions.scope.base'
                    : profile.scope === 'verified-scripted-follower'
                      ? 'za.encounters.bossActions.scope.follower'
                    : 'za.encounters.bossActions.scope.verified'
                )}</span>
                <ContextHelp label={t('za.encounters.bossActions.heading')}>
                  {t('za.encounters.bossActions.poolHelp')}
                  <br />
                  <br />
                  {t('za.encounters.bossActions.scopeHelp')}
                </ContextHelp>
              </span>
            </div>
            <div className="za-scripted-boss-profile-statuses">
              <span
                className={`za-scripted-boss-status-pill ${
                  hasBrokenAction
                    ? 'za-scripted-boss-status-broken'
                    : hasUnavailableAction
                      ? 'za-scripted-boss-status-unavailable'
                      : 'za-scripted-boss-status-runtime-present'
                }`}
              >
                {hasBrokenAction || hasUnavailableAction ? (
                  <AlertTriangle aria-hidden="true" size={13} />
                ) : (
                  <CheckCircle aria-hidden="true" size={13} />
                )}
                {t(
                  hasBrokenAction
                    ? 'za.encounters.bossActions.status.broken'
                    : hasUnavailableAction
                      ? 'za.encounters.bossActions.status.unavailable'
                      : 'za.encounters.bossActions.status.working'
                )}
              </span>
              {hasKnownIncompatibleAction ? (
                <span className="za-scripted-boss-status-pill za-scripted-boss-compatibility-known-incompatible">
                  <ShieldAlert aria-hidden="true" size={13} />
                  {t('za.encounters.bossActions.compatibility.known-incompatible.label')}
                </span>
              ) : null}
              <span
                className={`za-scripted-boss-status-pill ${
                  editableCount > 0
                    ? 'za-scripted-boss-status-editable'
                    : 'za-scripted-boss-status-locked'
                }`}
              >
                {editableCount > 0 ? (
                  <Pencil aria-hidden="true" size={13} />
                ) : (
                  <Lock aria-hidden="true" size={13} />
                )}
                {t(
                  editableCount > 0
                    ? 'za.encounters.bossActions.status.editable'
                    : 'za.encounters.bossActions.status.locked'
                )}
              </span>
            </div>
            <span>
              {t('za.encounters.bossActions.editCounts', {
                editableCount,
                lockedCount
              })}
            </span>
          </div>
          {hasVerifiedPhaseModel ? (
            <div className="za-scripted-boss-phase-guide" role="note">
              <div className="za-scripted-boss-phase-guide-heading">
                <span className="editable-field-label-row">
                  <strong>{t('za.encounters.bossActions.phase.heading')}</strong>
                  <ContextHelp label={t('za.encounters.bossActions.phase.heading')}>
                  {t(
                    `za.encounters.bossActions.phase.help.${profile.phaseModel.kind}`
                  )}
                  </ContextHelp>
                </span>
              </div>
              <div className="za-scripted-boss-phase-legend" role="list">
                {phaseAvailabilityStates.map((state) => (
                  <span className={`is-${state}`} key={state} role="listitem">
                    {state === 'available' ? (
                      <CheckCircle aria-hidden="true" size={12} />
                    ) : state === 'unavailable' ? (
                      <Lock aria-hidden="true" size={12} />
                    ) : state === 'context-only' ? (
                      <Binary aria-hidden="true" size={12} />
                    ) : (
                      <AlertTriangle aria-hidden="true" size={12} />
                    )}
                    {t(`za.encounters.bossActions.phase.state.${state}`)}
                  </span>
                ))}
              </div>
              <div
                aria-label={t('za.encounters.bossActions.phase.rangesLabel')}
                className={`za-scripted-boss-phase-stages ${
                  hasSingleStageHpBands ? 'is-single-stage' : 'is-multi-stage'
                }`}
                role="list"
              >
                {phaseGroups.map((group) => (
                  <section key={group.stage} role="listitem">
                    {!hasSingleStageHpBands ? (
                      <strong>
                        {t('za.encounters.bossActions.phase.stageLabel', {
                          name: group.stageName,
                          stage: group.stage
                        })}
                      </strong>
                    ) : null}
                    <div role="list">
                      {group.phases.map((phase) => (
                        <span key={phase.key} role="listitem">
                          {hasSingleStageHpBands || group.phases.length > 1 ? (
                            <strong>
                              {t('za.encounters.bossActions.phase.hpPhaseLabel', {
                                phase: phase.hpPhase
                              })}
                            </strong>
                          ) : null}
                          <small>
                            {t('za.encounters.bossActions.phase.hpRange', {
                              maximum: phase.maximumHpPercent,
                              minimum: phase.minimumHpPercent
                            })}
                          </small>
                        </span>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
              {profile.phaseModel.kind === 'hp-bands' ? (
                <small>{t('za.encounters.bossActions.phase.hpBandsHelp')}</small>
              ) : null}
            </div>
          ) : (
            <div
              className={`za-scripted-boss-phase-model-status is-${profile.phaseModel.state}`}
              role="note"
            >
              {profile.phaseModel.state === 'verified-none' ? (
                <CheckCircle aria-hidden="true" size={16} />
              ) : (
                <AlertTriangle aria-hidden="true" size={16} />
              )}
              <div>
                <strong>
                  {t(
                    `za.encounters.bossActions.phase.model.${profile.phaseModel.state}.label`
                  )}
                </strong>
                <span>
                  {t(
                    `za.encounters.bossActions.phase.model.${profile.phaseModel.state}.help`
                  )}
                </span>
              </div>
            </div>
          )}
          {moveOwnership ? (
            <div className="za-scripted-move-ownership-notice" role="note">
              <ShieldAlert aria-hidden="true" size={18} />
              <div>
                <strong>
                  {t(
                    moveOwnership.authority === 'shared-primary-controller'
                      ? 'za.encounters.bossActions.ownership.sharedPrimary.title'
                      : 'za.encounters.bossActions.ownership.follower.title'
                  )}
                </strong>
                <p>
                  {t(
                    moveOwnership.authority === 'shared-primary-controller'
                      ? 'za.encounters.bossActions.ownership.sharedPrimary.help'
                      : 'za.encounters.bossActions.ownership.follower.help'
                  )}
                </p>
              </div>
            </div>
          ) : null}
          {editableCount > 0 ? (
            <p className="za-scripted-boss-replacement-caveat">
              {t('za.encounters.bossActions.replacementCaveat')}
            </p>
          ) : null}
          <ul className="za-scripted-boss-action-list">
            {profile.actions.map((action) => {
              const sharedOwners = action.selectorActionId === null
                ? []
                : getScriptedBossActionOwners(profiles, action.selectorActionId);
              const isBroken = isBrokenScriptedBossAction(action);
              const isUnavailable = isUnavailableScriptedBossAction(action);
              const hasPhaseLock = action.phaseAvailability.some(
                (availability) => availability.state === 'unavailable'
              );
              const hasUnverifiedPhase = action.phaseAvailability.some(
                (availability) => availability.state === 'unverified'
              );
              const hasContextOnlyPhase = action.phaseAvailability.some(
                (availability) => availability.state === 'context-only'
              );
              const contextOnlyPhaseLabels = action.phaseAvailability
                .filter((availability) => availability.state === 'context-only')
                .map((availability) => {
                  const phase = profile.phaseModel.phases.find(
                    (candidate) => candidate.key === availability.phaseKey
                  );
                  return phase
                    ? formatScriptedBossActionPhaseLabel(profile, phase, t)
                    : availability.phaseKey;
                })
                .join(', ');

              return (
                <li
                  className={`${action.canEdit ? 'is-editable' : 'is-locked'} ${
                    isBroken ? 'is-broken' : isUnavailable ? 'is-unavailable' : 'is-runtime-present'
                  } ${
                    action.variant === null
                      ? ''
                      : `is-runtime-variant-${formatScriptedBossRuntimeVariantKey(action.variant)}`
                  }`}
                  key={action.key}
                >
                  <div className="za-scripted-boss-action-heading">
                    <div>
                      <strong>{formatScriptedBossActionName(action, t)}</strong>
                      <span>{formatScriptedBossActionKind(action, t)}</span>
                    </div>
                    <div className="za-scripted-boss-action-statuses">
                      {action.variant !== null ? (
                        <span
                          aria-label={t('za.encounters.bossActions.variant.ariaLabel', {
                            variant: formatScriptedBossRuntimeVariantLabel(action.variant, t)
                          })}
                          className={`za-scripted-boss-variant-pill is-${formatScriptedBossRuntimeVariantKey(
                            action.variant
                          )}`}
                        >
                          {formatScriptedBossRuntimeVariantLabel(action.variant, t)}
                        </span>
                      ) : null}
                      <span
                        className={`za-scripted-boss-status-pill ${
                          action.canEdit
                            ? 'za-scripted-boss-status-editable'
                            : 'za-scripted-boss-status-locked'
                        }`}
                      >
                        {action.canEdit ? (
                          <Pencil aria-hidden="true" size={12} />
                        ) : (
                          <Lock aria-hidden="true" size={12} />
                        )}
                        {t(
                          action.canEdit
                            ? 'za.encounters.bossActions.status.editable'
                            : 'za.encounters.bossActions.status.locked'
                        )}
                      </span>
                      {action.runtimeState !== 'not-applicable' ? (
                        <span
                          className={`za-scripted-boss-status-pill ${
                            isBroken
                              ? 'za-scripted-boss-status-broken'
                              : isUnavailable
                                ? 'za-scripted-boss-status-unavailable'
                                : 'za-scripted-boss-status-runtime-present'
                          }`}
                        >
                          {isBroken || isUnavailable ? (
                            <AlertTriangle aria-hidden="true" size={12} />
                          ) : (
                            <CheckCircle aria-hidden="true" size={12} />
                          )}
                          {t(
                            isBroken
                              ? 'za.encounters.bossActions.status.broken'
                              : isUnavailable
                                ? 'za.encounters.bossActions.status.unavailable'
                                : 'za.encounters.bossActions.status.working'
                          )}
                        </span>
                      ) : null}
                      {action.compatibilityState !== 'not-applicable' ? (
                        <>
                          <span
                            className={`za-scripted-boss-status-pill za-scripted-boss-compatibility-${action.compatibilityState}`}
                          >
                            {action.compatibilityState === 'base-verified' ||
                            action.compatibilityState === 'gameplay-tested' ? (
                              <CheckCircle aria-hidden="true" size={12} />
                            ) : action.compatibilityState === 'known-incompatible' ? (
                              <ShieldAlert aria-hidden="true" size={12} />
                            ) : (
                              <AlertTriangle aria-hidden="true" size={12} />
                            )}
                            {t(
                              `za.encounters.bossActions.compatibility.${action.compatibilityState}.label`
                            )}
                          </span>
                          <ContextHelp
                            label={t(
                              `za.encounters.bossActions.compatibility.${action.compatibilityState}.label`
                            )}
                          >
                            {t(
                              action.compatibilityReason
                                ? `za.encounters.bossActions.compatibility.reason.${action.compatibilityReason}`
                                : `za.encounters.bossActions.compatibility.${action.compatibilityState}.help`
                            )}
                          </ContextHelp>
                        </>
                      ) : null}
                    </div>
                  </div>

                  {action.moveId !== null && action.runtimeMoveId !== null ? (
                    <code>
                      {t('za.encounters.bossActions.ids', {
                        moveId: action.moveId,
                        runtimeId: action.runtimeMoveId
                      })}
                    </code>
                  ) : (
                    <code>
                      {t(
                        action.runtimeMoveId !== null
                          ? 'za.encounters.bossActions.invalidReference'
                          : action.selectorActionId === null
                            ? 'za.encounters.bossActions.noRuntimeRow'
                            : 'za.encounters.bossActions.unavailableReference',
                        action.runtimeMoveId !== null
                          ? { runtimeId: action.runtimeMoveId }
                          : undefined
                      )}
                    </code>
                  )}

                  {action.vanillaMoveId !== null && action.moveId !== action.vanillaMoveId ? (
                    <small className="za-scripted-boss-action-detail">
                      {t('za.encounters.bossActions.changedFromVanilla', {
                        moveId: action.vanillaMoveId
                      })}
                    </small>
                  ) : null}

                  {sharedOwners.length > 1 ? (
                    <small className="za-scripted-boss-action-detail">
                      {t('za.encounters.bossActions.sharedBy', {
                        count: sharedOwners.length,
                        owners: sharedOwners.map((owner) => owner.name).join(', ')
                      })}
                    </small>
                  ) : null}

                  {action.affectedScopes.length > 0 ? (
                    <small className="za-scripted-boss-action-detail za-scripted-boss-affected-scope">
                      {t('za.encounters.bossActions.ownership.affectedScope', {
                        scope: action.affectedScopes
                          .map((scope) => formatScriptedBossAffectedScope(scope, t))
                          .join('; ')
                      })}
                    </small>
                  ) : null}

                  {action.phaseAvailability.length > 0 ? (
                    <div className="za-scripted-boss-phase-availability">
                      <span className="za-scripted-boss-phase-availability-label">
                        {t('za.encounters.bossActions.phase.actionLabel')}
                      </span>
                      <div
                        aria-label={t('za.encounters.bossActions.phase.actionLabel')}
                        className="za-scripted-boss-phase-pills"
                        role="list"
                      >
                        {action.phaseAvailability.map((availability) => {
                          const phase = profile.phaseModel.phases.find(
                            (candidate) => candidate.key === availability.phaseKey
                          );
                          return (
                            <span
                              className={`za-scripted-boss-phase-pill is-${availability.state}`}
                              key={availability.phaseKey}
                              role="listitem"
                            >
                              {availability.state === 'available' ? (
                                <CheckCircle aria-hidden="true" size={12} />
                              ) : availability.state === 'unavailable' ? (
                                <Lock aria-hidden="true" size={12} />
                              ) : availability.state === 'context-only' ? (
                                <Binary aria-hidden="true" size={12} />
                              ) : (
                                <AlertTriangle aria-hidden="true" size={12} />
                              )}
                              <span>
                                {phase
                                  ? formatScriptedBossActionPhaseLabel(profile, phase, t)
                                  : availability.phaseKey}
                              </span>
                              <small>
                                {t(
                                  `za.encounters.bossActions.phase.state.${availability.state}`
                                )}
                              </small>
                            </span>
                          );
                        })}
                      </div>
                      {hasPhaseLock ? (
                        <small className="za-scripted-boss-phase-detail is-locked">
                          {t('za.encounters.bossActions.phase.lockedHelp')}
                        </small>
                      ) : null}
                      {hasUnverifiedPhase ? (
                        <small className="za-scripted-boss-phase-detail is-unverified">
                          {t('za.encounters.bossActions.phase.unverifiedHelp')}
                        </small>
                      ) : null}
                      {hasContextOnlyPhase ? (
                        <small className="za-scripted-boss-phase-detail is-context-only">
                          {t(
                            action.phaseContext === 'after-stun'
                              ? 'za.encounters.bossActions.phase.context.after-stun'
                              : action.phaseContext === 'bomb-rock-deployed'
                                ? 'za.encounters.bossActions.phase.context.bomb-rock-deployed'
                              : 'za.encounters.bossActions.phase.contextOnlyHelp',
                            { phases: contextOnlyPhaseLabels }
                          )}
                        </small>
                      ) : null}
                    </div>
                  ) : null}

                  {!action.canEdit && action.lockReason ? (
                    <small className="za-scripted-boss-action-lock-reason">
                      {t(`za.encounters.bossActions.lockReason.${action.lockReason}`)}
                    </small>
                  ) : null}

                  {renderActionControl?.(action)}
                </li>
              );
            })}
          </ul>
        </>
      ) : (
        <p className="za-scripted-boss-unmapped">
          {t('za.encounters.bossActions.unmapped')}
        </p>
      )}
    </section>
  );
}

export function ScriptedBossMoveOwnership({
  move,
  profiles
}: {
  move: MoveRecord;
  profiles: ScriptedBossProfile[];
}) {
  const { t } = useLocalization();
  const owners = getScriptedBossOwners(profiles, move.moveId, 2);
  const battleVariantPresent = move.runtimeVariants.some(
    (variant) => variant.variant === 2
  );
  const timingProfileIds = Array.from(
    new Set(
      move.timingRows
        .filter((timing) => timing.variant === 2)
        .map((timing) => timing.timingMoveId)
    )
  ).sort((left, right) => left - right);

  return (
    <section className="move-scripted-boss-ownership" role="note">
      <div className="move-scripted-boss-ownership-heading">
        <ShieldAlert aria-hidden="true" size={18} />
        <div>
          <h4>{t('moves.bossOwners.heading')}</h4>
          <p>{t('moves.bossOwners.help')}</p>
        </div>
      </div>

      <dl className="move-scripted-boss-technical-grid">
        <div>
          <dt>{t('moves.bossOwners.baseMoveId')}</dt>
          <dd>{move.moveId}</dd>
        </div>
        <div>
          <dt>{t('moves.bossOwners.runtimeMoveId')}</dt>
          <dd>{2000 + move.moveId}</dd>
        </div>
        <div>
          <dt>{t('moves.bossOwners.battleRow')}</dt>
          <dd>
            {battleVariantPresent
              ? t('moves.bossOwners.present')
              : t('moves.bossOwners.absent')}
          </dd>
        </div>
        <div>
          <dt>{t('moves.bossOwners.timingProfiles')}</dt>
          <dd>
            {timingProfileIds.length > 0
              ? timingProfileIds.join(', ')
              : t('moves.bossOwners.noneValue')}
          </dd>
        </div>
      </dl>

      {owners.length > 0 ? (
        <ul className="move-scripted-boss-owner-list">
          {owners.map(({ action, profile }) => (
            <li key={`${profile.key}:${action.key}`}>
              <Binary aria-hidden="true" size={15} />
              <div>
                <strong>{profile.name}</strong>
                <span>{formatScriptedBossActionKind(action, t)}</span>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <p className="move-scripted-boss-owner-empty">
          {t('moves.bossOwners.none')}
        </p>
      )}
    </section>
  );
}

export function ScriptedBossMoveControllerAvailability({
  move,
  profiles
}: {
  move: MoveRecord;
  profiles: ScriptedBossProfile[];
}) {
  const { t } = useLocalization();
  const owners = getScriptedBossOwners(profiles, move.moveId, 2);

  return (
    <section className="move-player-damage-controller-availability" role="note">
      <div className="move-player-damage-controller-availability-heading">
        <ShieldAlert aria-hidden="true" size={17} />
        <div>
          <h4>{t('moves.playerDamage.controllerAvailability.heading')}</h4>
          <p>{t('moves.playerDamage.controllerAvailability.help')}</p>
        </div>
      </div>
      {owners.length > 0 ? (
        <ScriptedBossMoveOwnerAvailabilityList owners={owners} />
      ) : (
        <p className="move-scripted-boss-owner-empty">
          {t('moves.playerDamage.controllerAvailability.noOwner')}
        </p>
      )}
    </section>
  );
}

function ScriptedBossMoveOwnerAvailabilityList({
  owners
}: {
  owners: ReturnType<typeof getScriptedBossOwners>;
}) {
  const { t } = useLocalization();

  return (
    <ul className="move-scripted-boss-owner-list has-availability">
      {owners.map(({ action, profile }) => {
        const phasesByKey = new Map(
          profile.phaseModel.phases.map((phase) => [phase.key, phase])
        );
        return (
          <li key={`${profile.key}:${action.key}`}>
            <Binary aria-hidden="true" size={15} />
            <div className="move-scripted-boss-owner-body">
              <div className="move-scripted-boss-owner-heading">
                <strong>{profile.name}</strong>
                <span>{formatScriptedBossActionKind(action, t)}</span>
              </div>
              {profile.phaseModel.state === 'verified' ? (
                <div
                  aria-label={t('moves.bossOwners.controllerScheduleLabel', {
                    boss: profile.name
                  })}
                  className="move-scripted-boss-owner-phases"
                  role="list"
                >
                  {action.phaseAvailability.map((availability) => {
                    const phase = phasesByKey.get(availability.phaseKey);
                    if (!phase) {
                      return null;
                    }
                    return (
                      <span
                        className={`is-${availability.state}`}
                        key={availability.phaseKey}
                        role="listitem"
                      >
                        <strong>
                          {formatScriptedBossActionPhaseLabel(profile, phase, t)}
                        </strong>
                        <small>
                          {t(
                            `za.encounters.bossActions.phase.state.${availability.state}`
                          )}
                        </small>
                      </span>
                    );
                  })}
                </div>
              ) : (
                <span className={`move-scripted-boss-owner-model is-${profile.phaseModel.state}`}>
                  {t(`moves.bossOwners.phaseModel.${profile.phaseModel.state}`)}
                </span>
              )}
              {action.phaseContext ? (
                <small className="move-scripted-boss-owner-context">
                  {t(
                    action.phaseContext === 'bomb-rock-deployed'
                      ? 'moves.bossOwners.bombRockDeployedContext'
                      : 'moves.bossOwners.afterStunContext'
                  )}
                </small>
              ) : null}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

function groupScriptedBossPhases(profile: ScriptedBossProfile) {
  const groups = new Map<
    number,
    { phases: ScriptedBossPhase[]; stage: number; stageName: string }
  >();
  for (const phase of profile.phaseModel.phases) {
    const group = groups.get(phase.stage);
    if (group) {
      group.phases.push(phase);
    } else {
      groups.set(phase.stage, {
        phases: [phase],
        stage: phase.stage,
        stageName: phase.stageName
      });
    }
  }

  return [...groups.values()].sort((left, right) => left.stage - right.stage);
}

function formatScriptedBossActionPhaseLabel(
  profile: ScriptedBossProfile,
  phase: ScriptedBossPhase,
  t: Localize
) {
  const stagePhaseCount = profile.phaseModel.phases.filter(
    (candidate) => candidate.stage === phase.stage
  ).length;
  if (profile.phaseModel.kind === 'hp-bands') {
    return t('za.encounters.bossActions.phase.hpPhaseLabel', {
      phase: phase.hpPhase
    });
  }

  return stagePhaseCount > 1
    ? t('za.encounters.bossActions.phase.actionStageAndHpPhase', {
        name: phase.stageName,
        phase: phase.hpPhase,
        stage: phase.stage
      })
    : t('za.encounters.bossActions.phase.stageLabel', {
        name: phase.stageName,
        stage: phase.stage
      });
}

function formatScriptedBossAffectedScope(
  scope: ScriptedBossAffectedScope,
  t: Localize
) {
  switch (scope.key) {
    case 'beedrill-battle-kakuna-and-beedrill-followers':
      return t('za.encounters.bossActions.ownership.scope.beedrillSharedFollowers');
    case 'beedrill-battle-kakuna-followers':
      return t('za.encounters.bossActions.ownership.scope.kakunaFollowers');
    case 'beedrill-battle-beedrill-followers':
      return t('za.encounters.bossActions.ownership.scope.beedrillFollowers');
    case 'banette-primary-and-clone-controllers':
      return t('za.encounters.bossActions.ownership.scope.banettePrimaryAndClones');
    default:
      return scope.label;
  }
}

function formatScriptedBossRuntimeVariantKey(variant: number) {
  switch (variant) {
    case 0:
      return 'normal';
    case 1:
      return 'plus';
    case 2:
      return 'boss';
    default:
      return 'unknown';
  }
}

function formatScriptedBossRuntimeVariantLabel(
  variant: number,
  localize?: Localize
) {
  const key = formatScriptedBossRuntimeVariantKey(variant);
  if (localize) {
    return localize(
      key === 'unknown'
        ? 'moves.runtimeVariant.unknown'
        : `moves.runtimeVariant.${key}`,
      { variant }
    );
  }

  switch (key) {
    case 'normal':
      return 'Normal Move';
    case 'plus':
      return 'Plus Move';
    case 'boss':
      return 'Boss Move';
    default:
      return `Variant ${variant}`;
  }
}

function isBrokenScriptedBossAction(action: ScriptedBossAction) {
  return [
    'missing-battle',
    'missing-timing',
    'missing-battle-and-timing',
    'invalid-reference'
  ].includes(action.runtimeState);
}

function isUnavailableScriptedBossAction(action: ScriptedBossAction) {
  return action.runtimeState === 'unavailable';
}

function formatScriptedBossActionName(
  action: ScriptedBossAction,
  t: (key: string, params?: Record<string, string | number>) => string
) {
  switch (action.key) {
    case 'scripted-mechanic:volcanic-eruption':
      return t('za.encounters.bossActions.mechanic.volcanicEruption');
    case 'scripted-mechanic:clone-sequence':
      return t('za.encounters.bossActions.mechanic.cloneSequence');
    case 'scripted-mechanic:darkrai-nightmare-sequence':
      return t('za.encounters.bossActions.mechanic.darkraiNightmareSequence');
    case 'scripted-mechanic:darkrai-clone-sequence':
      return t('za.encounters.bossActions.mechanic.darkraiCloneSequence');
    default:
      return action.name;
  }
}

function formatScriptedBossActionKind(
  action: ScriptedBossAction,
  t: (key: string) => string
) {
  switch (action.kind) {
    case 'battle-move':
      switch (action.variant) {
        case 0:
          return t('za.encounters.bossActions.kind.normalMove');
        case 1:
          return t('za.encounters.bossActions.kind.plusMove');
        case 2:
          return t('za.encounters.bossActions.kind.bossMove');
        default:
          return t('za.encounters.bossActions.kind.battleMove');
      }
    case 'movement-helper':
      return t('za.encounters.bossActions.kind.movementHelper');
    case 'scripted-mechanic':
      return t('za.encounters.bossActions.kind.scriptedMechanic');
  }
}
