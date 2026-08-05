/* SPDX-License-Identifier: GPL-3.0-only */

import { AlertTriangle, Binary, ShieldAlert } from 'lucide-react';
import {
  type MoveRecord,
  type ScriptedBossAction,
  type ScriptedBossProfile
} from '../../bridge/contracts';
import { useLocalization } from '../../localization/LocalizationProvider';

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
  moveId: number
) {
  return profiles.flatMap((profile) =>
    profile.actions
      .filter((action) => action.moveId === moveId)
      .map((action) => ({ action, profile }))
  );
}

export function getScriptedBossMoveSearchValues(
  profiles: ScriptedBossProfile[],
  moveId: number
) {
  return getScriptedBossOwners(profiles, moveId).flatMap(({ action, profile }) => [
    profile.key,
    profile.name,
    profile.speciesId.toString(),
    action.kind
  ]);
}

export function ScriptedBossEncounterActions({
  profile
}: {
  profile: ScriptedBossProfile | null;
}) {
  const { t } = useLocalization();

  return (
    <section className="za-scripted-boss-actions" role="note">
      <div className="za-scripted-boss-actions-heading">
        <AlertTriangle aria-hidden="true" size={18} />
        <div>
          <h4>{t('za.encounters.bossActions.heading')}</h4>
          <p>{t('za.encounters.bossActions.warning')}</p>
        </div>
      </div>

      {profile ? (
        <>
          <div className="za-scripted-boss-profile-summary">
            <div>
              <strong>{profile.name}</strong>
              <span>{t('za.encounters.bossActions.scope.base')}</span>
            </div>
            <span>
              {t('za.encounters.bossActions.actionCount', {
                count: profile.actions.length
              })}
            </span>
          </div>
          <p className="za-scripted-boss-pool-help">
            {t('za.encounters.bossActions.poolHelp')}
          </p>
          <ul className="za-scripted-boss-action-list">
            {profile.actions.map((action) => (
              <li key={action.key}>
                <div>
                  <strong>{formatScriptedBossActionName(action, t)}</strong>
                  <span>{formatScriptedBossActionKind(action, t)}</span>
                </div>
                {action.moveId !== null && action.runtimeMoveId !== null ? (
                  <code>
                    {t('za.encounters.bossActions.ids', {
                      moveId: action.moveId,
                      runtimeId: action.runtimeMoveId
                    })}
                  </code>
                ) : (
                  <code>{t('za.encounters.bossActions.noRuntimeRow')}</code>
                )}
              </li>
            ))}
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
  const owners = getScriptedBossOwners(profiles, move.moveId);
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

function formatScriptedBossActionName(
  action: ScriptedBossAction,
  t: (key: string, params?: Record<string, string | number>) => string
) {
  switch (action.key) {
    case 'scripted-mechanic:volcanic-eruption':
      return t('za.encounters.bossActions.mechanic.volcanicEruption');
    case 'scripted-mechanic:clone-sequence':
      return t('za.encounters.bossActions.mechanic.cloneSequence');
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
      return t('za.encounters.bossActions.kind.battleMove');
    case 'movement-helper':
      return t('za.encounters.bossActions.kind.movementHelper');
    case 'scripted-mechanic':
      return t('za.encounters.bossActions.kind.scriptedMechanic');
  }
}
