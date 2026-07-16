/* SPDX-License-Identifier: GPL-3.0-only */

import { type EditSession } from '../../bridge/contracts';
import {
  type FairyGymBoostResultKind,
  type FairyGymBoostSelection,
  type FairyGymBoostsWorkflow
} from '../../bridge/fairyGymBoostsContracts';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';

export const fairyGymBoostIds = [
  'annette-weakness-poison',
  'annette-weakness-steel',
  'teresa-previous-trainer-annetta',
  'teresa-previous-trainer-annette',
  'theodora-breakfast-curry',
  'theodora-breakfast-omelets',
  'opal-nickname-magic-user',
  'opal-nickname-wizard',
  'opal-color-pink',
  'opal-color-purple',
  'opal-age-sixteen',
  'opal-age-eighty-eight'
] as const;

const canonicalSourcePaths = [
  'romfs/bin/battle/waza/sequence/bk143.bseq',
  'romfs/bin/battle/waza/sequence/bk144.bseq',
  'romfs/bin/battle/waza/sequence/bk145.bseq',
  'romfs/bin/battle/waza/sequence/bk171.bseq',
  'romfs/bin/battle/waza/sequence/bk173.bseq',
  'romfs/bin/battle/waza/sequence/bk174.bseq'
] as const;

export function getCanonicalFairyGymBoostPendingSelections(
  editSession: EditSession | null,
  workflow: FairyGymBoostsWorkflow | null
): FairyGymBoostSelection[] | null {
  if (
    !workflow ||
    (workflow.detectedGame !== 'sword' && workflow.detectedGame !== 'shield') ||
    editSession?.sessionId.trim().length === 0 ||
    editSession?.hasPendingChanges !== true ||
    editSession?.pendingEdits.length !== 1
  ) {
    return null;
  }

  const edit = editSession.pendingEdits[0];
  const selections = decodeFairyGymBoostPendingSelections(edit?.newValue);
  if (
    edit?.domain !== 'workflow.fairyGymBoosts' ||
    edit.recordId !== 'fairy-gym-boosts' ||
    edit.field !== 'boostSelections' ||
    edit.summary !== 'Stage Fairy Gym boost outcomes.' ||
    selections === null ||
    edit.newValue !== encodeFairyGymBoostPendingSelections(selections) ||
    !hasCanonicalPendingSources(workflow, edit.sources, edit.newValue)
  ) {
    return null;
  }

  return selections;
}

export function encodeFairyGymBoostPendingSelections(
  selections: readonly FairyGymBoostSelection[]
) {
  return selections
    .map(
      (selection) =>
        `${selection.boostId}:${selection.effectId}:${selection.resultKind}`
    )
    .join(';');
}

export function decodeFairyGymBoostPendingSelections(
  value: string | null | undefined
): FairyGymBoostSelection[] | null {
  if (!value) {
    return null;
  }

  const entries = value.split(';');
  if (entries.length !== fairyGymBoostIds.length) {
    return null;
  }

  const selections: FairyGymBoostSelection[] = [];
  for (let index = 0; index < entries.length; index += 1) {
    const parts = entries[index]!.split(':');
    const [boostId, effectIdText, resultKind] = parts;
    const effectId = Number(effectIdText);
    if (
      parts.length !== 3 ||
      boostId !== fairyGymBoostIds[index] ||
      !Number.isInteger(effectId) ||
      effectIdText !== effectId.toString() ||
      !isSupportedFairyGymBoostOutcome(effectId, resultKind)
    ) {
      return null;
    }

    selections.push({ boostId, effectId, resultKind });
  }

  return selections;
}

export function isSupportedFairyGymBoostOutcome(
  effectId: number,
  resultKind: string | undefined
): resultKind is FairyGymBoostResultKind {
  if (effectId === 0) {
    return resultKind === 'none';
  }

  return (
    effectId >= 1 &&
    effectId <= 6 &&
    (resultKind === 'increase' || resultKind === 'decrease')
  );
}

function hasCanonicalPendingSources(
  workflow: FairyGymBoostsWorkflow,
  sources: EditSession['pendingEdits'][number]['sources'],
  payload: string
) {
  if (
    workflow.sources.length !== canonicalSourcePaths.length ||
    workflow.sources.some(
      (source, index) =>
        source.status !== 'available' ||
        source.relativePath !== canonicalSourcePaths[index]
    )
  ) {
    return false;
  }

  const expectedSources: EditSession['pendingEdits'][number]['sources'] = [
    ...canonicalSourcePaths.map((relativePath) => ({
      layer: 'base' as const,
      relativePath
    })),
    ...workflow.sources
      .filter((source) => source.provenance.sourceLayer === 'layered')
      .map((source) => ({
        layer: 'layered' as const,
        relativePath: source.relativePath
      })),
    {
      layer: 'pending' as const,
      relativePath: `pending/fairy-gym-boosts/selections/${calculatePendingPayloadSha256(payload)}`
    }
  ];

  return (
    sources.length === expectedSources.length &&
    sources.every(
      (source, index) =>
        source.layer === expectedSources[index]?.layer &&
        source.relativePath === expectedSources[index]?.relativePath
    )
  );
}
