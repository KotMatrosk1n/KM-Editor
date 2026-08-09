/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertTriangle, Binary, CheckCircle, Lock, Pencil, ShieldAlert } from 'lucide-react';
import { type ReactNode } from 'react';
import {
  type MoveRecord,
  type ScriptedBossAction,
  type ScriptedBossProfile
} from '../../bridge/contracts';
import { useLocalization } from '../../localization/LocalizationProvider';

const heatAvailabilityStates = [
  'available',
  'unavailable',
  'context-only',
  'unverified'
] as const;

const heatAvailabilitySearchLabels = {
  available: 'available',
  'context-only': 'context only',
  unavailable: 'locked',
  unverified: 'unverified'
} as const;

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
  localize?: (key: string) => string
) {
  return getScriptedBossOwners(profiles, moveId).flatMap(({ action, profile }) => [
    profile.key,
    profile.name,
    profile.speciesId.toString(),
    action.kind,
    action.runtimeState,
    action.lockReason ?? '',
    action.heatContext ?? '',
    action.selectorActionId?.toString() ?? '',
    action.vanillaMoveId?.toString() ?? '',
    ...action.heatAvailability.flatMap((availability) => {
      const stateLabel = heatAvailabilitySearchLabels[availability.state];
      const localizedHeatLevel = localize?.(
        `za.encounters.bossActions.heat.level.${availability.heatLevel}`
      );
      const localizedState = localize?.(
        `za.encounters.bossActions.heat.state.${availability.state}`
      );
      return [
        `heat ${availability.heatLevel} ${stateLabel}`,
        `phase ${availability.heatLevel} ${stateLabel}`,
        localizedHeatLevel && localizedState
          ? `${localizedHeatLevel} ${localizedState}`
          : '',
        stateLabel,
        availability.state
      ];
    })
  ]);
}

export function ScriptedBossEncounterActions({
  profile,
  profiles,
  renderActionControl
}: {
  profile: ScriptedBossProfile | null;
  profiles: ScriptedBossProfile[];
  renderActionControl?: (action: ScriptedBossAction) => ReactNode;
}) {
  const { t } = useLocalization();
  const editableCount = profile?.actions.filter((action) => action.canEdit).length ?? 0;
  const lockedCount = (profile?.actions.length ?? 0) - editableCount;
  const hasBrokenAction = profile?.actions.some(isBrokenScriptedBossAction) ?? false;
  const hasUnavailableAction = profile?.actions.some(isUnavailableScriptedBossAction) ?? false;
  const hasPhaseAvailability =
    profile?.actions.some((action) => action.heatAvailability.length > 0) ?? false;

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
              : editableCount > 0
                ? 'za-scripted-boss-actions-editable'
                : 'za-scripted-boss-actions-locked'
      }`}
    >
      <div className="za-scripted-boss-actions-heading">
        {profile === null || hasBrokenAction || hasUnavailableAction ? (
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
              <strong>{profile.name}</strong>
              <span>
                {t(
                  profile.scope === 'base-rogue-mega'
                    ? 'za.encounters.bossActions.scope.base'
                    : 'za.encounters.bossActions.scope.verified'
                )}
              </span>
            </div>
            <div className="za-scripted-boss-profile-statuses">
              <span
                className={`za-scripted-boss-status-pill ${
                  hasBrokenAction
                    ? 'za-scripted-boss-status-broken'
                    : hasUnavailableAction
                      ? 'za-scripted-boss-status-unavailable'
                      : 'za-scripted-boss-status-working'
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
          <p className="za-scripted-boss-pool-help">
            {t('za.encounters.bossActions.poolHelp')}
          </p>
          <p className="za-scripted-boss-pool-help">
            {t('za.encounters.bossActions.scopeHelp')}
          </p>
          {hasPhaseAvailability ? (
            <div className="za-scripted-boss-heat-guide" role="note">
              <div className="za-scripted-boss-heat-guide-heading">
                <strong>{t('za.encounters.bossActions.heat.heading')}</strong>
                <span>{t('za.encounters.bossActions.heat.help')}</span>
              </div>
              <div className="za-scripted-boss-heat-legend" role="list">
                {heatAvailabilityStates.map((state) => (
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
                    {t(`za.encounters.bossActions.heat.state.${state}`)}
                  </span>
                ))}
              </div>
              <div
                aria-label={t('za.encounters.bossActions.heat.rangesLabel')}
                className="za-scripted-boss-heat-ranges"
                role="list"
              >
                {[1, 2, 3].map((heatLevel) => (
                  <span key={heatLevel} role="listitem">
                    {t(`za.encounters.bossActions.heat.range.${heatLevel}`)}
                  </span>
                ))}
              </div>
              <small>{t('za.encounters.bossActions.heat.baseGameHelp')}</small>
            </div>
          ) : (
            <p className="za-scripted-boss-pool-help" role="note">
              {t('za.encounters.bossActions.scheduleUnmapped')}
            </p>
          )}
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
              const hasHeatLock = action.heatAvailability.some(
                (availability) => availability.state === 'unavailable'
              );
              const hasUnverifiedHeat = action.heatAvailability.some(
                (availability) => availability.state === 'unverified'
              );
              const hasContextOnlyHeat = action.heatAvailability.some(
                (availability) => availability.state === 'context-only'
              );

              return (
                <li
                  className={`${action.canEdit ? 'is-editable' : 'is-locked'} ${
                    isBroken ? 'is-broken' : isUnavailable ? 'is-unavailable' : 'is-working'
                  }`}
                  key={action.key}
                >
                  <div className="za-scripted-boss-action-heading">
                    <div>
                      <strong>{formatScriptedBossActionName(action, t)}</strong>
                      <span>{formatScriptedBossActionKind(action, t)}</span>
                    </div>
                    <div className="za-scripted-boss-action-statuses">
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
                                : 'za-scripted-boss-status-working'
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

                  {action.heatAvailability.length > 0 ? (
                    <div className="za-scripted-boss-heat-availability">
                      <span className="za-scripted-boss-heat-availability-label">
                        {t('za.encounters.bossActions.heat.actionLabel')}
                      </span>
                      <div
                        aria-label={t('za.encounters.bossActions.heat.actionLabel')}
                        className="za-scripted-boss-heat-pills"
                        role="list"
                      >
                        {action.heatAvailability.map((availability) => (
                          <span
                            className={`za-scripted-boss-heat-pill is-${availability.state}`}
                            key={availability.heatLevel}
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
                              {t(
                                `za.encounters.bossActions.heat.level.${availability.heatLevel}`
                              )}
                            </span>
                            <small>
                              {t(
                                `za.encounters.bossActions.heat.state.${availability.state}`
                              )}
                            </small>
                          </span>
                        ))}
                      </div>
                      {hasHeatLock ? (
                        <small className="za-scripted-boss-heat-detail is-locked">
                          {t('za.encounters.bossActions.heat.lockedHelp')}
                        </small>
                      ) : null}
                      {hasUnverifiedHeat ? (
                        <small className="za-scripted-boss-heat-detail is-unverified">
                          {t('za.encounters.bossActions.heat.unverifiedHelp')}
                        </small>
                      ) : null}
                      {hasContextOnlyHeat ? (
                        <small className="za-scripted-boss-heat-detail is-context-only">
                          {t(
                            action.heatContext === 'after-stun'
                              ? 'za.encounters.bossActions.heat.context.after-stun'
                              : 'za.encounters.bossActions.heat.contextOnlyHelp'
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
    case 'scripted-mechanic:clone-nightmare-sequence':
      return t('za.encounters.bossActions.mechanic.cloneNightmareSequence');
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
