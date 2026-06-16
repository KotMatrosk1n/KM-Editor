/* SPDX-License-Identifier: GPL-3.0-only */

import { type WorkflowSummary } from '../bridge/contracts';
import {
  type FairyGymBoostResultKind,
  type FairyGymBoostsWorkflow
} from '../bridge/fairyGymBoostsContracts';

export function createFairyGymBoostsWorkflowFixture(canEdit: boolean): {
  fairyGymBoostsWorkflow: FairyGymBoostsWorkflow;
  fairyGymBoostsWorkflowSummary: WorkflowSummary;
} {
  const fairyGymBoostsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Preliminary advanced editor that maps Fairy Gym quiz stat boosts to NPC trainers and answer choices.',
    diagnostics: [],
    id: 'fairyGymBoosts',
    label: 'Fairy Gym Boosts'
  };
  const fairyGymBoostsWorkflow: FairyGymBoostsWorkflow = {
    diagnostics: [],
    sources: ['bk143', 'bk144', 'bk145', 'bk171', 'bk173', 'bk174'].map((sourceId) => ({
      label: `${sourceId} quiz sequence`,
      provenance: {
        fileState: 'baseOnly',
        sourceFile: `romfs/bin/battle/waza/sequence/${sourceId}.bseq`,
        sourceLayer: 'base'
      },
      relativePath: `romfs/bin/battle/waza/sequence/${sourceId}.bseq`,
      sourceId,
      status: 'available'
    })),
    stats: {
      boostCount: 12,
      sourceFileCount: 6,
      trainerCount: 4
    },
    summary: fairyGymBoostsWorkflowSummary,
    trainers: [
      {
        boosts: [
          createBoost('annette-weakness-poison', 'bk143', 1, 'Poison type', "Do you know about Fairy type's weaknesses?", 'increase', 1, 'Attack and Sp. Atk', 1, ['atk', 'spAtk']),
          createBoost('annette-weakness-steel', 'bk143', 2, 'Steel type', "Do you know about Fairy type's weaknesses?", 'increase', 1, 'Attack and Sp. Atk', 1, ['atk', 'spAtk'])
        ],
        displayOrder: 0,
        npcName: 'Annette',
        trainerId: 113
      },
      {
        boosts: [
          createBoost('teresa-previous-trainer-annetta', 'bk144', 1, 'Annetta', "What was the previous Trainer's name?", 'decrease', 5, 'Speed', 1, ['speed']),
          createBoost('teresa-previous-trainer-annette', 'bk144', 2, 'Annette', "What was the previous Trainer's name?", 'increase', 5, 'Speed', 1, ['speed'])
        ],
        displayOrder: 1,
        npcName: 'Teresa',
        trainerId: 114
      },
      {
        boosts: [
          createBoost('theodora-breakfast-curry', 'bk145', 1, 'Curry', 'What do I eat for breakfast every morning?', 'decrease', 3, 'Defense and Sp. Def', 1, ['def', 'spDef']),
          createBoost('theodora-breakfast-omelets', 'bk145', 2, 'Omelets', 'What do I eat for breakfast every morning?', 'increase', 3, 'Defense and Sp. Def', 1, ['def', 'spDef'])
        ],
        displayOrder: 2,
        npcName: 'Theodora',
        trainerId: 115
      },
      {
        boosts: [
          createBoost('opal-nickname-magic-user', 'bk171', 1, 'The magic-user', 'Do you know my nickname?', 'decrease', 6, 'Speed', 2, ['speed']),
          createBoost('opal-nickname-wizard', 'bk171', 2, 'The wizard', 'Do you know my nickname?', 'increase', 6, 'Speed', 2, ['speed']),
          createBoost('opal-color-pink', 'bk173', 1, 'Pink', 'What is my favorite color?', 'decrease', 4, 'Defense and Sp. Def', 2, ['def', 'spDef']),
          createBoost('opal-color-purple', 'bk173', 2, 'Purple', 'What is my favorite color?', 'increase', 4, 'Defense and Sp. Def', 2, ['def', 'spDef']),
          createBoost('opal-age-sixteen', 'bk174', 1, '16 years old', 'How old am I?', 'increase', 2, 'Attack and Sp. Atk', 2, ['atk', 'spAtk']),
          createBoost('opal-age-eighty-eight', 'bk174', 2, '88 years old', 'How old am I?', 'decrease', 2, 'Attack and Sp. Atk', 2, ['atk', 'spAtk'])
        ],
        displayOrder: 3,
        npcName: 'Opal',
        trainerId: 108
      }
    ]
  };

  return { fairyGymBoostsWorkflow, fairyGymBoostsWorkflowSummary };
}

function createBoost(
  boostId: string,
  sequenceId: string,
  answerChoice: number,
  answerText: string,
  questionText: string,
  resultKind: FairyGymBoostResultKind,
    effectId: number,
  effectLabel: string,
  stageAmount: number,
  affectedStats: FairyGymBoostsWorkflow['trainers'][number]['boosts'][number]['affectedStats']
) {
  return {
    affectedStats,
    answerChoice,
    answerText,
    boostId,
    defaultResultKind: resultKind,
    effectId,
    effectLabel,
    questionText,
    resultKind,
    sequenceFile: `romfs/bin/battle/waza/sequence/${sequenceId}.bseq`,
    stageAmount
  };
}
