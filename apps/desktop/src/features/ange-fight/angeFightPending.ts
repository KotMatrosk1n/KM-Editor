/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { type EditSession } from '../../bridge/contracts';
import {
  angeFightAttackSelectionSchema,
  angeFightHpSchema,
  type AngeFightAttackSelection,
  type AngeFightWorkflow
} from '../../bridge/angeFightContracts';

export type AngeFightValues = {
  attacks: AngeFightAttackSelection[];
  blueFlowerHp: number;
  redFlowerHp: number;
};

export type CanonicalAngeFightPendingState =
  | {
      kind: 'settings';
      values: AngeFightValues;
    }
  | {
      kind: 'uninstall';
    };

const settingsRecordId = 'za-ange-fight-v1-settings';
const settingsField = 'settings';
const settingsSummary = 'Stage Ange Fight HP and direct-damage values.';
const uninstallRecordId = 'za-ange-fight-v1-uninstall';
const uninstallField = 'uninstall';
const uninstallSummary = 'Stage Ange Fight uninstall to verified vanilla values.';

const canonicalSourcePaths = [
  'romfs/world/ik_data/field/field_wazagimmick_public/field_wazagimmick_public/field_wazagimmick_public.bin',
  'romfs/param_ai/data/ai/attack/attack_param/attack_param_array.bin',
  'romfs/param_ai/data/ai/bullet/bullet_param/bullet_param_array.bin'
] as const;

const settingsSchema = z.strictObject({
  blueFlowerHp: angeFightHpSchema,
  redFlowerHp: angeFightHpSchema,
  attacks: z.array(angeFightAttackSelectionSchema).length(10)
});

export function getCanonicalAngeFightPendingState(
  editSession: EditSession | null,
  workflow: AngeFightWorkflow | null
): CanonicalAngeFightPendingState | null {
  if (!workflow || editSession?.pendingEdits.length !== 1) {
    return null;
  }

  const edit = editSession.pendingEdits[0];
  if (
    edit?.domain !== 'workflow.angeFight' ||
    !hasCanonicalSources(edit.sources, workflow)
  ) {
    return null;
  }

  if (
    edit.recordId === uninstallRecordId &&
    edit.field === uninstallField &&
    edit.newValue === 'true' &&
    edit.summary === uninstallSummary
  ) {
    return { kind: 'uninstall' };
  }

  if (
    edit.recordId !== settingsRecordId ||
    edit.field !== settingsField ||
    edit.summary !== settingsSummary ||
    !edit.newValue
  ) {
    return null;
  }

  const values = decodeAngeFightPendingValues(edit.newValue, workflow);
  if (
    values === null ||
    edit.newValue !== encodeAngeFightPendingValues(values)
  ) {
    return null;
  }

  return { kind: 'settings', values };
}

export function decodeAngeFightPendingValues(
  payload: string,
  workflow: AngeFightWorkflow
): AngeFightValues | null {
  try {
    const result = settingsSchema.safeParse(JSON.parse(payload));
    if (!result.success || !hasCanonicalAttackOrder(result.data.attacks, workflow)) {
      return null;
    }

    return {
      attacks: result.data.attacks.map((attack) => ({ ...attack })),
      blueFlowerHp: result.data.blueFlowerHp,
      redFlowerHp: result.data.redFlowerHp
    };
  } catch {
    return null;
  }
}

export function encodeAngeFightPendingValues(values: AngeFightValues) {
  return JSON.stringify({
    blueFlowerHp: values.blueFlowerHp,
    redFlowerHp: values.redFlowerHp,
    attacks: values.attacks.map((attack) => ({
      attackId: attack.attackId,
      damageToPokemon: attack.damageToPokemon,
      damageToPlayer: attack.damageToPlayer
    }))
  });
}

function hasCanonicalAttackOrder(
  attacks: readonly AngeFightAttackSelection[],
  workflow: AngeFightWorkflow
) {
  return (
    workflow.attacks.length === attacks.length &&
    attacks.every(
      (attack, index) => attack.attackId === workflow.attacks[index]?.attackId
    )
  );
}

function hasCanonicalSources(
  sources: EditSession['pendingEdits'][number]['sources'],
  workflow: AngeFightWorkflow
) {
  if (workflow.sources.length !== canonicalSourcePaths.length) {
    return false;
  }

  const expected = canonicalSourcePaths
    .flatMap((relativePath, index) => {
      const source = workflow.sources[index];
      if (
        source?.relativePath !== relativePath ||
        (source.provenance.sourceLayer !== 'base' &&
          source.provenance.sourceLayer !== 'layered')
      ) {
        return [];
      }

      return source.provenance.sourceLayer === 'layered'
        ? [
            { layer: 'base' as const, relativePath },
            { layer: 'layered' as const, relativePath }
          ]
        : [{ layer: 'base' as const, relativePath }];
    })
    .sort(compareSources);

  return (
    expected.length >= canonicalSourcePaths.length &&
    sources.length === expected.length &&
    sources.every(
      (source, index) =>
        source.layer === expected[index]?.layer &&
        source.relativePath === expected[index]?.relativePath
    )
  );
}

function compareSources(
  left: EditSession['pendingEdits'][number]['sources'][number],
  right: EditSession['pendingEdits'][number]['sources'][number]
) {
  const layerOrder = { base: 0, layered: 1, pending: 2, generated: 3 };
  return (
    layerOrder[left.layer] - layerOrder[right.layer] ||
    left.relativePath.localeCompare(right.relativePath, 'en')
  );
}
