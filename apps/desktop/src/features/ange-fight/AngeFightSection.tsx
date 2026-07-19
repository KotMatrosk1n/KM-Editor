/* SPDX-License-Identifier: GPL-3.0-only */

import {
  ClipboardCheck,
  RotateCcw,
  Save,
  Swords,
  Trash2,
  TriangleAlert
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { type EditSession } from '../../bridge/contracts';
import {
  type AngeFightAttackRecord,
  type AngeFightAttackSelection,
  type AngeFightWorkflow
} from '../../bridge/angeFightContracts';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import {
  formatFileState,
  formatSourceLayer
} from '../../utils/workflowFormatters';
import {
  encodeAngeFightPendingValues,
  getCanonicalAngeFightPendingState,
  type AngeFightValues
} from './angeFightPending';

export {
  decodeAngeFightPendingValues,
  getCanonicalAngeFightPendingState
} from './angeFightPending';
export type { AngeFightValues } from './angeFightPending';

const int32Maximum = 2_147_483_647;

type AngeFightAttackDraft = {
  attackId: number;
  damageToPlayer: string;
  damageToPokemon: string;
};

type AngeFightDraft = {
  attacks: AngeFightAttackDraft[];
  blueFlowerHp: string;
  redFlowerHp: string;
};

export function AngeFightSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onDirtyChange,
  onStageFight,
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
  onStageFight: (
    blueFlowerHp: number,
    redFlowerHp: number,
    attacks: AngeFightAttackSelection[]
  ) => void;
  onStageUninstall: () => void;
  panelOutput: WorkflowPanelOutput;
  workflow: AngeFightWorkflow | null;
}) {
  const { translateLiteral } = useLocalization();
  const workflowValues = useMemo(() => getAngeFightWorkflowValues(workflow), [workflow]);
  const vanillaValues = useMemo(() => getAngeFightVanillaValues(workflow), [workflow]);
  const stagedState = useMemo(
    () => getCanonicalAngeFightPendingState(editSession, workflow),
    [editSession, workflow]
  );
  const stagedValues = stagedState?.kind === 'settings' ? stagedState.values : null;
  const isUninstallStaged = stagedState?.kind === 'uninstall';
  const angePendingEdits =
    editSession?.pendingEdits.filter((edit) => edit.domain === 'workflow.angeFight') ?? [];
  const hasInvalidPendingEdit = angePendingEdits.length > 0 && stagedState === null;
  const hasConflictingPendingEdit =
    editSession?.pendingEdits.some((edit) => edit.domain !== 'workflow.angeFight') ?? false;
  const cleanValues = stagedValues ?? workflowValues ?? createEmptyAngeFightValues();
  const cleanValuesKey = encodeAngeFightPendingValues(cleanValues);
  const cleanIdentityKey = [
    workflow?.sources
      .map(
        (source) =>
          `${source.id}:${source.effectiveSha256}:${source.vanillaSha256}:${source.provenance.sourceLayer}`
      )
      .join('|') ?? 'none',
    stagedState?.kind ?? 'none',
    angePendingEdits[0]?.newValue ?? 'none',
    editSession?.sessionId ?? 'none'
  ].join('||');
  const [draft, setDraft] = useState<AngeFightDraft>(() =>
    createAngeFightDraft(cleanValues)
  );

  useEffect(() => {
    setDraft(createAngeFightDraft(cleanValues));
  }, [cleanIdentityKey, cleanValuesKey]);

  const parsedDraft = useMemo(() => parseAngeFightDraft(draft), [draft]);
  const isDirty = getDraftKey(draft) !== getDraftKey(createAngeFightDraft(cleanValues));
  const hasStagedChange = stagedState !== null;
  const isBusy = isStaging || isChangePlanCreating || isChangePlanApplying;
  const canDisplayEditor = isDisplayableAngeFightWorkflow(workflow);
  const canEdit =
    canDisplayEditor &&
    workflow?.installStatus !== 'readOnly' &&
    !isUninstallStaged &&
    !hasInvalidPendingEdit &&
    !hasConflictingPendingEdit &&
    !isBusy;
  const canStage = canEdit && isDirty && parsedDraft !== null;
  const canResetToVanilla =
    canEdit &&
    vanillaValues !== null &&
    getDraftKey(draft) !== getDraftKey(createAngeFightDraft(vanillaValues));
  const canStageUninstall =
    canDisplayEditor &&
    workflow?.canUninstall === true &&
    !isUninstallStaged &&
    !hasInvalidPendingEdit &&
    !hasConflictingPendingEdit &&
    !isDirty &&
    !isBusy;
  const canReviewPlan =
    hasStagedChange &&
    !isDirty &&
    !hasInvalidPendingEdit &&
    !hasConflictingPendingEdit &&
    !isBusy;
  const canApplyPlan =
    hasStagedChange &&
    !isDirty &&
    !hasInvalidPendingEdit &&
    !hasConflictingPendingEdit &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isBusy;

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const updateFlowerHp = (flowerId: 'blue' | 'red', value: string) => {
    setDraft((current) =>
      flowerId === 'blue'
        ? { ...current, blueFlowerHp: value }
        : { ...current, redFlowerHp: value }
    );
  };

  const updateAttackDamage = (
    attackId: number,
    field: 'damageToPokemon' | 'damageToPlayer',
    value: string
  ) => {
    setDraft((current) => ({
      ...current,
      attacks: current.attacks.map((attack) =>
        attack.attackId === attackId ? { ...attack, [field]: value } : attack
      )
    }));
  };

  return (
    <>
      <section aria-labelledby="ange-fight-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Swords aria-hidden="true" size={18} />
          <h2 id="ange-fight-heading">Ange Fight</h2>
        </div>

        <div className="items-toolbar exefs-toolbar">
          <Metric label="Game" value="Pokemon Legends Z-A" />
          <Metric
            label="Status"
            value={workflow ? formatAngeFightStatus(workflow.installStatus) : 'Not loaded'}
          />
          <Metric label="Flowers" value={workflow?.stats.flowerCount.toString() ?? '0'} />
          <Metric label="Attacks" value={workflow?.stats.attackCount.toString() ?? '0'} />
          <Metric
            label="Editable values"
            value={workflow?.stats.editableValueCount.toString() ?? '0'}
          />
          <Metric
            label="Staged"
            value={
              stagedState?.kind === 'settings'
                ? 'Ange Fight'
                : isUninstallStaged
                  ? 'Uninstall'
                  : hasInvalidPendingEdit
                    ? 'Invalid'
                    : 'No'
            }
          />
        </div>

        {workflow && canDisplayEditor ? (
          <div className="ange-fight-editor">
            <div className="ange-fight-intro">
              <div>
                <strong>Direct fight values</strong>
                <p>
                  Edit both flowers and every mapped direct-damage attack used by the
                  Ange encounter.
                </p>
              </div>
              <p className="ange-fight-ember-note">
                Ember uses the normal move system and is intentionally not editable here.
              </p>
            </div>

            {hasInvalidPendingEdit ? (
              <div className="ange-fight-blocker" role="alert">
                <TriangleAlert aria-hidden="true" size={17} />
                <span>
                  Ange Fight has a non-canonical pending edit. Discard it before editing.
                </span>
              </div>
            ) : null}
            {hasConflictingPendingEdit ? (
              <div className="ange-fight-blocker" role="alert">
                <TriangleAlert aria-hidden="true" size={17} />
                <span>
                  Ange Fight needs its own edit session. Finish or discard the other
                  pending changes first.
                </span>
              </div>
            ) : null}
            {isUninstallStaged ? (
              <div className="ange-fight-staged-note" role="status">
                Ange Fight uninstall is staged. Review and apply it to restore the
                verified vanilla values.
              </div>
            ) : null}

            <section aria-labelledby="ange-flower-hp-heading" className="ange-fight-group">
              <div className="ange-fight-group-heading">
                <div>
                  <h3 id="ange-flower-hp-heading">Flower HP</h3>
                  <p>Each flower has its own independent HP value.</p>
                </div>
              </div>
              <div className="ange-fight-flower-grid">
                {workflow.flowers.map((flower) => {
                  const value =
                    flower.flowerId === 'blue'
                      ? draft.blueFlowerHp
                      : draft.redFlowerHp;
                  return (
                    <NumberField
                      disabled={!canEdit}
                      invalid={parseInt32(value, 1) === null}
                      key={flower.flowerId}
                      label={translateLiteral(flower.label)}
                      minimum={1}
                      onChange={(nextValue) =>
                        updateFlowerHp(flower.flowerId, nextValue)
                      }
                      value={value}
                      vanillaValue={flower.vanillaHp}
                    />
                  );
                })}
              </div>
            </section>

            <section aria-labelledby="ange-damage-heading" className="ange-fight-group">
              <div className="ange-fight-group-heading">
                <div>
                  <h3 id="ange-damage-heading">Attack Damage</h3>
                  <p>Pokémon damage and player damage are stored separately.</p>
                </div>
                <span className="ange-fight-count-pill">
                  <strong data-localization-ignore="true">
                    {workflow.attacks.length.toLocaleString()}
                  </strong>
                  <span>Mapped attacks</span>
                </span>
              </div>

              <div className="ange-fight-attack-grid">
                {workflow.attacks.map((attack, index) => (
                  <AngeFightAttackCard
                    attack={attack}
                    disabled={!canEdit}
                    draft={draft.attacks[index]}
                    key={attack.attackId}
                    onChange={updateAttackDamage}
                    translateLiteral={translateLiteral}
                  />
                ))}
              </div>
            </section>

            {parsedDraft === null && isDirty ? (
              <div className="ange-fight-blocker" role="alert">
                <TriangleAlert aria-hidden="true" size={17} />
                <span>
                  Fix the highlighted values before staging. HP must be at least 1;
                  damage may be 0 or higher.
                </span>
              </div>
            ) : null}

            <div className="type-chart-actions ange-fight-actions">
              <button
                className="danger-button"
                disabled={!canResetToVanilla}
                onClick={() => {
                  if (vanillaValues) {
                    setDraft(createAngeFightDraft(vanillaValues));
                  }
                }}
                title="Reset all Ange Fight HP and damage fields to verified vanilla values."
                type="button"
              >
                <RotateCcw aria-hidden="true" size={16} />
                <span>Reset to Vanilla</span>
              </button>
              <button
                aria-busy={isStaging || undefined}
                className="primary-button"
                disabled={!canStage}
                onClick={() => {
                  if (parsedDraft) {
                    onStageFight(
                      parsedDraft.blueFlowerHp,
                      parsedDraft.redFlowerHp,
                      parsedDraft.attacks
                    );
                  }
                }}
                type="button"
              >
                <Save aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage Ange Fight'}</span>
              </button>
              <button
                aria-busy={isStaging || undefined}
                className="danger-button"
                disabled={!canStageUninstall}
                onClick={onStageUninstall}
                title="Restore vanilla Ange Fight values"
                type="button"
              >
                <Trash2 aria-hidden="true" size={16} />
                <span>{isStaging ? 'Staging' : 'Stage Uninstall'}</span>
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

            <div className="ange-fight-source-grid">
              {workflow.sources.map((source) => (
                <dl className="ange-fight-source-card" key={source.id}>
                  <div className="ange-fight-source-path">
                    <dt>{translateLiteral(source.label)}</dt>
                    <dd data-localization-ignore="true">{source.relativePath}</dd>
                  </div>
                  <div>
                    <dt>Layer</dt>
                    <dd>{formatSourceLayer(source.provenance.sourceLayer)}</dd>
                  </div>
                  <div>
                    <dt>File state</dt>
                    <dd>{formatFileState(source.provenance.state)}</dd>
                  </div>
                </dl>
              ))}
            </div>
          </div>
        ) : workflow ? (
          <p className="empty-copy">
            <strong>Unavailable</strong>
            <br />
            <span>{workflow.installMessage}</span>
          </p>
        ) : (
          <p className="empty-copy">
            Open Ange Fight from Advanced Editors to inspect the encounter values.
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

function AngeFightAttackCard({
  attack,
  disabled,
  draft,
  onChange,
  translateLiteral
}: {
  attack: AngeFightAttackRecord;
  disabled: boolean;
  draft: AngeFightAttackDraft | undefined;
  onChange: (
    attackId: number,
    field: 'damageToPokemon' | 'damageToPlayer',
    value: string
  ) => void;
  translateLiteral: (literal: string) => string;
}) {
  const pokemonDamage = draft?.damageToPokemon ?? attack.damageToPokemon.toString();
  const playerDamage = draft?.damageToPlayer ?? attack.damageToPlayer.toString();

  return (
    <article className="ange-fight-attack-card">
      <div className="ange-fight-attack-heading">
        <div>
          <h4>{translateLiteral(attack.label)}</h4>
          <p>{translateLiteral(attack.usage)}</p>
        </div>
        <div className="ange-fight-attack-badges">
          {attack.sharedByMultipleActions ? <span>Shared value</span> : null}
          {attack.canRepeatHit ? <span className="is-warning">Per-hit damage</span> : null}
        </div>
      </div>

      {attack.sharedByMultipleActions ? (
        <p className="ange-fight-shared-note">
          Changing this row affects every listed use.
        </p>
      ) : null}
      {attack.canRepeatHit ? (
        <div className="ange-fight-repeat-warning">
          <TriangleAlert aria-hidden="true" size={15} />
          <span>
            {translateLiteral(
              'This attack can damage the same target repeatedly.'
            )}{' '}
            {translateLiteral('Hit interval')}:{' '}
            <strong data-localization-ignore="true">
              {formatInterval(attack.hitIntervalSeconds)} s
            </strong>
          </span>
        </div>
      ) : null}

      <div className="ange-fight-damage-grid">
        <NumberField
          disabled={disabled}
          invalid={parseInt32(pokemonDamage, 0) === null}
          label="Damage to Pokémon"
          minimum={0}
          onChange={(value) => onChange(attack.attackId, 'damageToPokemon', value)}
          value={pokemonDamage}
          vanillaValue={attack.vanillaDamageToPokemon}
        />
        <NumberField
          disabled={disabled}
          invalid={parseInt32(playerDamage, 0) === null}
          label="Damage to player"
          minimum={0}
          onChange={(value) => onChange(attack.attackId, 'damageToPlayer', value)}
          value={playerDamage}
          vanillaValue={attack.vanillaDamageToPlayer}
        />
      </div>

      <dl className="ange-fight-technical-map">
        <div>
          <dt>Bullet ID</dt>
          <dd data-localization-ignore="true">{attack.bulletId}</dd>
        </div>
        <div>
          <dt>Attack ID</dt>
          <dd data-localization-ignore="true">{attack.attackId}</dd>
        </div>
        <div>
          <dt>Hit interval</dt>
          <dd data-localization-ignore="true">
            {formatInterval(attack.hitIntervalSeconds)} s
          </dd>
        </div>
      </dl>
    </article>
  );
}

function NumberField({
  disabled,
  invalid,
  label,
  minimum,
  onChange,
  value,
  vanillaValue
}: {
  disabled: boolean;
  invalid: boolean;
  label: string;
  minimum: number;
  onChange: (value: string) => void;
  value: string;
  vanillaValue: number;
}) {
  const { translateLiteral } = useLocalization();
  const localizedLabel = translateLiteral(label);

  return (
    <label className="ange-fight-number-field">
      <span>{localizedLabel}</span>
      <input
        aria-label={localizedLabel}
        aria-invalid={invalid ? 'true' : undefined}
        disabled={disabled}
        inputMode="numeric"
        max={int32Maximum}
        min={minimum}
        onChange={(event) => onChange(event.currentTarget.value)}
        step={1}
        type="number"
        value={value}
      />
      <small>
        {invalid ? (
          minimum === 1 ? (
            'Enter a whole number from 1 to 2,147,483,647.'
          ) : (
            'Enter a whole number from 0 to 2,147,483,647.'
          )
        ) : (
          <>
            <span>Vanilla</span>:{' '}
            <span data-localization-ignore="true">
              {vanillaValue.toLocaleString()}
            </span>
          </>
        )}
      </small>
    </label>
  );
}

export function getAngeFightWorkflowValues(
  workflow: AngeFightWorkflow | null
): AngeFightValues | null {
  if (!hasCanonicalWorkflowValues(workflow)) {
    return null;
  }

  return {
    attacks: workflow.attacks.map((attack) => ({
      attackId: attack.attackId,
      damageToPokemon: attack.damageToPokemon,
      damageToPlayer: attack.damageToPlayer
    })),
    blueFlowerHp: workflow.flowers[0]!.hp,
    redFlowerHp: workflow.flowers[1]!.hp
  };
}

export function getAngeFightVanillaValues(
  workflow: AngeFightWorkflow | null
): AngeFightValues | null {
  if (!hasCanonicalWorkflowValues(workflow)) {
    return null;
  }

  return {
    attacks: workflow.attacks.map((attack) => ({
      attackId: attack.attackId,
      damageToPokemon: attack.vanillaDamageToPokemon,
      damageToPlayer: attack.vanillaDamageToPlayer
    })),
    blueFlowerHp: workflow.flowers[0]!.vanillaHp,
    redFlowerHp: workflow.flowers[1]!.vanillaHp
  };
}

function hasCanonicalWorkflowValues(
  workflow: AngeFightWorkflow | null
): workflow is AngeFightWorkflow {
  return Boolean(
    workflow &&
      workflow.flowers.length === 2 &&
      workflow.flowers[0]?.flowerId === 'blue' &&
      workflow.flowers[1]?.flowerId === 'red' &&
      workflow.attacks.length === 10
  );
}

function isDisplayableAngeFightWorkflow(workflow: AngeFightWorkflow | null) {
  return Boolean(
    hasCanonicalWorkflowValues(workflow) &&
      workflow.summary.availability === 'available' &&
      workflow.installStatus !== 'blocked' &&
      workflow.sources.length === 3
  );
}

function createEmptyAngeFightValues(): AngeFightValues {
  return {
    attacks: [],
    blueFlowerHp: 1,
    redFlowerHp: 1
  };
}

function createAngeFightDraft(values: AngeFightValues): AngeFightDraft {
  return {
    attacks: values.attacks.map((attack) => ({
      attackId: attack.attackId,
      damageToPokemon: attack.damageToPokemon.toString(),
      damageToPlayer: attack.damageToPlayer.toString()
    })),
    blueFlowerHp: values.blueFlowerHp.toString(),
    redFlowerHp: values.redFlowerHp.toString()
  };
}

function parseAngeFightDraft(draft: AngeFightDraft): AngeFightValues | null {
  const blueFlowerHp = parseInt32(draft.blueFlowerHp, 1);
  const redFlowerHp = parseInt32(draft.redFlowerHp, 1);
  if (blueFlowerHp === null || redFlowerHp === null || draft.attacks.length !== 10) {
    return null;
  }

  const attacks: AngeFightAttackSelection[] = [];
  for (const attack of draft.attacks) {
    const damageToPokemon = parseInt32(attack.damageToPokemon, 0);
    const damageToPlayer = parseInt32(attack.damageToPlayer, 0);
    if (damageToPokemon === null || damageToPlayer === null) {
      return null;
    }

    attacks.push({
      attackId: attack.attackId,
      damageToPokemon,
      damageToPlayer
    });
  }

  return { attacks, blueFlowerHp, redFlowerHp };
}

function parseInt32(value: string, minimum: 0 | 1) {
  if (!/^\d+$/.test(value)) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= minimum && parsed <= int32Maximum
    ? parsed
    : null;
}

function getDraftKey(draft: AngeFightDraft) {
  return [
    draft.blueFlowerHp,
    draft.redFlowerHp,
    ...draft.attacks.flatMap((attack) => [
      attack.attackId.toString(),
      attack.damageToPokemon,
      attack.damageToPlayer
    ])
  ].join('|');
}

function formatAngeFightStatus(status: AngeFightWorkflow['installStatus']) {
  switch (status) {
    case 'vanilla':
      return 'Vanilla';
    case 'modified':
      return 'Modified';
    case 'readOnly':
      return 'Read-only';
    case 'blocked':
      return 'Blocked';
  }
}

function formatInterval(value: number) {
  return Number.isInteger(value) ? value.toFixed(1) : value.toString();
}
