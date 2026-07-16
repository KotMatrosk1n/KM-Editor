/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  type DynamaxAdventurePokemonSnapshot,
  type DynamaxAdventureRecord,
  type DynamaxAdventuresWorkflow,
  dynamaxAdventuresWorkflowSchema,
  loadDynamaxAdventuresWorkflowRequestSchema,
  previewDynamaxAdventureDefaultsRequestSchema,
  previewDynamaxAdventureDefaultsResponseSchema,
  stageDynamaxAdventureRepairRequestSchema,
  stageDynamaxAdventureRepairResponseSchema,
  stageDynamaxAdventureRestoreRequestSchema,
  stageDynamaxAdventureRestoreResponseSchema,
  updateDynamaxAdventureFieldRequestSchema,
  updateDynamaxAdventureFieldResponseSchema
} from './contracts';
import { createProjectBridge } from './projectBridge';
import { createMockProjectBridge } from '../testSupport/appTestFixtures';

const paths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: null,
  saveFilePath: null,
  scarletVioletSupportFolderPath: null,
  selectedGame: 'sword' as const
};

describe('Dynamax Adventures contracts', () => {
  it('accepts only canonical safe workflows and Sword or Shield field actions', async () => {
    const bridge = createMockProjectBridge({}, true);
    const compactResponse = await bridge.loadDynamaxAdventuresWorkflow({ paths });
    const workflow = createCanonicalDynamaxAdventuresWorkflow(compactResponse.workflow);
    const canonicalWorkflow = dynamaxAdventuresWorkflowSchema.safeParse(workflow);
    if (!canonicalWorkflow.success) {
      throw new Error(JSON.stringify(canonicalWorkflow.error.issues, null, 2));
    }
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse({
        ...workflow,
        hasLegacyBossTargetPatch: true,
        installStatus: 'repairable'
      }).success
    ).toBe(true);

    const invalidWorkflows = [
      mutateWorkflow(workflow, (draft) => {
        delete (draft as unknown as Record<string, unknown>).buildId;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft as unknown as Record<string, unknown>).installStatus;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft as unknown as Record<string, unknown>).reservedRegions;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft as unknown as Record<string, unknown>).canRestoreVanillaTable;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft as unknown as Record<string, unknown>)
          .hasLegacyBossTargetPatch;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.hasLegacyBossTargetPatch = true;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft as unknown as Record<string, unknown>)
          .usesVanillaRecoveryProjection;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft as unknown as Record<string, unknown>)
          .restoreVanillaTableMessage;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.detectedGame = 'shield';
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft.encounters[0] as unknown as Record<string, unknown>).isEditable;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft.encounters[0] as unknown as Record<string, unknown>).abilityOptions;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft.encounters[0] as unknown as Record<string, unknown>).vanillaPokemon;
      }),
      mutateWorkflow(workflow, (draft) => {
        delete (draft.encounters[0] as unknown as Record<string, unknown>)
          .layoutWritableFields;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[0]!.layoutWritableFields.push('level');
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters.pop();
        draft.stats.totalEncounterCount = draft.encounters.length;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[226]!.isEditable = true;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[226]!.layoutWritableFields = ['level'];
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[226]!.vanillaPokemon!.level += 1;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[0]!.bossTargetSpeciesId = 1;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[0]!.ivs.attack = -2;
      }),
      mutateWorkflow(workflow, (draft) => {
        const moves = draft.encounters[0]!.moves;
        [moves[0], moves[1]] = [moves[1]!, moves[0]!];
      }),
      mutateWorkflow(workflow, (draft) => {
        const moves = draft.encounters[0]!.vanillaPokemon!.moves;
        [moves[0], moves[1]] = [moves[1]!, moves[0]!];
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[0]!.entryIndex = 1;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[0]!.bossTargetOptions = [
          {
            adventureIndex: 0,
            entryIndex: 0,
            form: 0,
            isStoryProgressGated: false,
            label: 'Unsafe boss edit',
            species: 'Bulbasaur',
            speciesId: 1,
            version: 0,
            versionLabel: 'Both'
          }
        ];
      }),
      mutateWorkflow(workflow, (draft) => {
        (draft.encounters[0]!.provenance as unknown as Record<string, unknown>)
          .sourceFile = 'romfs/not-the-adventure-table.bin';
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.encounters[0]!.abilityOptions = [];
      }),
      mutateWorkflow(workflow, (draft) => {
        const encounter = draft.encounters[0]!;
        encounter.abilityOptions = encounter.abilityOptions.filter(
          (option) => option.value !== encounter.ability
        );
      }),
      mutateWorkflow(workflow, (draft) => {
        const encounter = draft.encounters[0]!;
        encounter.gigantamaxOptions = [
          {
            label: 'Mismatched state',
            value: encounter.gigantamaxState === 2 ? 1 : 2
          }
        ];
      }),
      mutateWorkflow(workflow, (draft) => {
        const encounter = draft.encounters[0]!;
        encounter.moveOptions = encounter.moveOptions.filter(
          (option) => option.value !== encounter.moves[0]!.moveId
        );
      }),
      mutateWorkflow(workflow, (draft) => {
        const encounter = draft.encounters[0]!;
        encounter.provenance.sourceLayer =
          encounter.provenance.fileState === 'baseOnly' ? 'layered' : 'base';
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.safeNormalSpeciesOptions = [];
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.stats.totalEncounterCount = 2;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.editableFields[0]!.maximumValue = 899;
      }),
      mutateWorkflow(workflow, (draft) => {
        draft.editableFields[0]!.field = 'level';
      }),
      mutateWorkflow(workflow, (draft) => {
        const field = draft.editableFields.find(
          (candidate) => candidate.field === 'guaranteedPerfectIvs'
        )!;
        field.options = [
          ...field.options,
          { label: 'Unsupported guaranteed count', value: 1 }
        ];
      })
    ];
    for (const invalidWorkflow of invalidWorkflows) {
      expect(dynamaxAdventuresWorkflowSchema.safeParse(invalidWorkflow).success).toBe(false);
    }

    const nonWritableLegacyOptions = mutateWorkflow(workflow, (draft) => {
      const encounter = draft.encounters[0]!;
      encounter.layoutWritableFields = encounter.layoutWritableFields.filter(
        (field) => field !== 'ability' && field !== 'gigantamaxState'
      );
      encounter.ability = 2;
      encounter.abilityOptions = [{ label: 'Ability 1', value: 0 }];
      encounter.gigantamaxState = 2;
      encounter.gigantamaxOptions = [{ label: 'Normal', value: 1 }];
    });
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(nonWritableLegacyOptions).success
    ).toBe(true);

    const diagnosticMismatchWorkflow = mutateWorkflow(workflow, (draft) => {
      draft.summary.availability = 'readOnly';
      draft.encounters[226]!.level += 1;
    });
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(diagnosticMismatchWorkflow).success
    ).toBe(true);
    const emptyDiagnosticWorkflow = mutateWorkflow(workflow, (draft) => {
      const diagnostic = {
        domain: 'workflow.dynamaxAdventures',
        message: 'Dynamax Adventures table could not be inspected.',
        severity: 'error' as const
      };
      draft.diagnostics = [diagnostic];
      draft.encounters = [];
      draft.installStatus = 'blocked';
      draft.safeNormalSpeciesOptions = [];
      draft.stats = {
        guaranteedPerfectIvEncounterCount: 0,
        singleCaptureCount: 0,
        sourceFileCount: 0,
        storyGatedCount: 0,
        totalEncounterCount: 0
      };
      draft.summary.availability = 'readOnly';
      draft.summary.diagnostics = [diagnostic];
    });
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(emptyDiagnosticWorkflow).success
    ).toBe(true);

    const layoutMismatchDiagnostic = {
      domain: 'workflow.dynamaxAdventures',
      file: 'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin',
      message:
        'Dynamax Adventures source table byte layout differs from the vanilla table. Restore the Adventure table from a clean dump before making new Pokemon edits.',
      severity: 'error' as const
    };
    const restoreWorkflow = mutateWorkflow(workflow, (draft) => {
      draft.canRestoreVanillaTable = true;
      draft.diagnostics = [layoutMismatchDiagnostic];
      draft.usesVanillaRecoveryProjection = true;
      draft.encounters.forEach((encounter) => {
        encounter.isEditable = false;
        encounter.layoutWritableFields = [];
        encounter.provenance.fileState = 'layeredOverride';
        encounter.provenance.sourceLayer = 'layered';
        encounter.vanillaPokemon = createSnapshot(encounter);
      });
      draft.restoreVanillaTableMessage =
        'Restore is available. Applying it removes all layered Adventure-table changes and restores the verified vanilla table.';
      draft.summary.availability = 'readOnly';
    });
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(restoreWorkflow).success
    ).toBe(true);
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(
        mutateWorkflow(restoreWorkflow, (draft) => {
          draft.hasLegacyBossTargetPatch = true;
          draft.installStatus = 'repairable';
        })
      ).success
    ).toBe(true);
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(
        mutateWorkflow(restoreWorkflow, (draft) => {
          draft.summary.availability = 'available';
        })
      ).success
    ).toBe(false);
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(
        mutateWorkflow(restoreWorkflow, (draft) => {
          draft.usesVanillaRecoveryProjection = false;
        })
      ).success
    ).toBe(false);
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(
        mutateWorkflow(restoreWorkflow, (draft) => {
          draft.encounters[0]!.isEditable = true;
          draft.encounters[0]!.layoutWritableFields = ['level'];
        })
      ).success
    ).toBe(false);
    const compatibleInvalidRowWorkflow = mutateWorkflow(
      restoreWorkflow,
      (draft) => {
        draft.diagnostics = [
          {
            domain: 'workflow.dynamaxAdventures',
            file:
              'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin',
            message:
              'Dynamax Adventures row 1 contains species outside the supported API domain.',
            severity: 'error'
          }
        ];
      }
    );
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(compatibleInvalidRowWorkflow)
        .success
    ).toBe(true);
    const blockedRecoveryProjection = mutateWorkflow(
      restoreWorkflow,
      (draft) => {
        draft.canRestoreVanillaTable = false;
        draft.installStatus = 'blocked';
        draft.diagnostics = [
          layoutMismatchDiagnostic,
          {
            domain: 'workflow.dynamaxAdventures',
            file: 'exefs/main',
            message: 'Dynamax Adventures found a non-owned executable state.',
            severity: 'error'
          }
        ];
      }
    );
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(blockedRecoveryProjection).success
    ).toBe(true);
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(
        mutateWorkflow(restoreWorkflow, (draft) => {
          draft.diagnostics = [];
        })
      ).success
    ).toBe(false);
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(
        mutateWorkflow(restoreWorkflow, (draft) => {
          draft.encounters[0]!.provenance.fileState = 'baseOnly';
          draft.encounters[0]!.provenance.sourceLayer = 'base';
        })
      ).success
    ).toBe(false);
    expect(
      dynamaxAdventuresWorkflowSchema.safeParse(
        mutateWorkflow(restoreWorkflow, (draft) => {
          draft.installStatus = 'blocked';
        })
      ).success
    ).toBe(false);

    expect(
      loadDynamaxAdventuresWorkflowRequestSchema.safeParse({ paths }).success
    ).toBe(true);
    expect(
      loadDynamaxAdventuresWorkflowRequestSchema.safeParse({
        paths: { ...paths, selectedGame: 'scarlet' }
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRepairRequestSchema.safeParse({
        paths,
        session: null
      }).success
    ).toBe(true);
    expect(
      stageDynamaxAdventureRepairRequestSchema.safeParse({
        paths: { ...paths, selectedGame: 'scarlet' },
        session: null
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreRequestSchema.safeParse({
        paths,
        session: null
      }).success
    ).toBe(true);
    const restoreRetrySession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.dynamaxAdventures',
          field: 'level',
          newValue: workflow.encounters[0]!.level.toString(),
          recordId: 'dynamaxAdventure:0',
          sources: [
            {
              layer: 'layered' as const,
              relativePath:
                'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
            }
          ],
          summary: 'Restore the vanilla Dynamax Adventures table.'
        }
      ],
      sessionId: 'dynamax-session'
    };
    expect(
      stageDynamaxAdventureRestoreRequestSchema.safeParse({
        paths,
        session: restoreRetrySession
      }).success
    ).toBe(true);
    expect(
      stageDynamaxAdventureRestoreRequestSchema.safeParse({
        paths,
        session: {
          hasPendingChanges: true,
          pendingEdits: [],
          sessionId: 'dynamax-session'
        }
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreRequestSchema.safeParse({
        paths,
        session: {
          ...restoreRetrySession,
          pendingEdits: [
            {
              ...restoreRetrySession.pendingEdits[0]!,
              summary: 'Restore one Adventure row.'
            }
          ]
        }
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreRequestSchema.safeParse({
        paths: { ...paths, selectedGame: 'scarlet' },
        session: null
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreRequestSchema.safeParse({
        paths,
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.dynamaxAdventures',
              field: 'level',
              newValue: '66',
              recordId: 'dynamaxAdventure:0',
              sources: [],
              summary: 'Set Adventure 000 level to 66.'
            }
          ],
          sessionId: 'dynamax-session'
        }
      }).success
    ).toBe(false);
    expect(
      previewDynamaxAdventureDefaultsRequestSchema.safeParse({
        entryIndex: 0,
        form: 0,
        level: 65,
        paths,
        session: null,
        species: 898
      }).success
    ).toBe(true);
    expect(
      previewDynamaxAdventureDefaultsRequestSchema.safeParse({
        entryIndex: 0,
        form: 0,
        level: 65,
        paths,
        session: null,
        species: 899
      }).success
    ).toBe(false);
    expect(
      previewDynamaxAdventureDefaultsRequestSchema.safeParse({
        entryIndex: 226,
        form: 0,
        level: 65,
        paths,
        session: null,
        species: 467
      }).success
    ).toBe(false);
    const preview = await bridge.previewDynamaxAdventureDefaults({
      entryIndex: 0,
      form: 0,
      level: 65,
      paths,
      session: null,
      species: 467
    });
    expect(previewDynamaxAdventureDefaultsResponseSchema.safeParse(preview).success).toBe(true);
    expect(
      previewDynamaxAdventureDefaultsResponseSchema.safeParse({
        ...preview,
        changes: preview.changes.filter((change) => change.field !== 'move3Id')
      }).success
    ).toBe(false);
    expect(
      previewDynamaxAdventureDefaultsResponseSchema.safeParse({
        ...preview,
        changes: [...preview.changes, preview.changes[0]]
      }).success
    ).toBe(false);
    expect(
      previewDynamaxAdventureDefaultsResponseSchema.safeParse({
        ...preview,
        abilityOptions: []
      }).success
    ).toBe(false);
    expect(
      previewDynamaxAdventureDefaultsResponseSchema.safeParse({
        ...preview,
        changes: preview.changes.map((change) =>
          change.field === 'ability' ? { ...change, value: '3' } : change
        )
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldRequestSchema.safeParse({
        entryIndex: 0,
        field: 'ivAttack',
        paths,
        session: null,
        value: '-1'
      }).success
    ).toBe(true);
    expect(
      updateDynamaxAdventureFieldRequestSchema.safeParse({
        entryIndex: 0,
        field: 'bossTargetSpecies',
        paths,
        session: null,
        value: '1'
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldRequestSchema.safeParse({
        entryIndex: 0,
        field: 'level',
        paths,
        session: null,
        value: '65.5'
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldRequestSchema.safeParse({
        entryIndex: 0,
        field: 'level',
        paths,
        session: null,
        value: '101'
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldRequestSchema.safeParse({
        entryIndex: 0,
        field: 'guaranteedPerfectIvs',
        paths,
        session: null,
        value: '1'
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldRequestSchema.safeParse({
        entryIndex: 226,
        field: 'level',
        paths,
        session: null,
        value: '65'
      }).success
    ).toBe(false);

    const pendingEdit = {
      domain: 'workflow.dynamaxAdventures',
      field: 'level',
      newValue: '66',
      recordId: 'dynamaxAdventure:0',
      sources: [
        {
          layer: 'base' as const,
          relativePath:
            'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
        }
      ],
      summary: 'Set Adventure 000 level to 66.'
    };
    const validUpdateResponse = {
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: [pendingEdit],
        sessionId: 'dynamax-session'
      },
      workflow
    };
    expect(
      updateDynamaxAdventureFieldResponseSchema.safeParse(validUpdateResponse).success
    ).toBe(true);
    const invalidOwnerSourceSets = [
      [
        {
          ...pendingEdit.sources[0]!,
          layer: 'layered' as const
        }
      ],
      [
        {
          ...pendingEdit.sources[0]!,
          relativePath: 'romfs/bin/appli/chika/data_table/not_adventure.bin'
        }
      ],
      [],
      [pendingEdit.sources[0]!, pendingEdit.sources[0]!]
    ];
    for (const sources of invalidOwnerSourceSets) {
      expect(
        updateDynamaxAdventureFieldResponseSchema.safeParse({
          ...validUpdateResponse,
          session: {
            ...validUpdateResponse.session,
            pendingEdits: [{ ...pendingEdit, sources }]
          }
        }).success
      ).toBe(false);
    }
    const validRepairResponse = {
      ...validUpdateResponse,
      session: {
        ...validUpdateResponse.session,
        pendingEdits: [
          {
            ...pendingEdit,
            newValue: workflow.encounters[0]!.level.toString(),
            summary: 'Repair Dynamax Adventures executable projection.'
          }
        ]
      }
    };
    expect(
      stageDynamaxAdventureRepairResponseSchema.safeParse(validRepairResponse).success
    ).toBe(true);
    for (const sources of invalidOwnerSourceSets) {
      expect(
        stageDynamaxAdventureRepairResponseSchema.safeParse({
          ...validRepairResponse,
          session: {
            ...validRepairResponse.session,
            pendingEdits: [
              {
                ...validRepairResponse.session.pendingEdits[0]!,
                sources
              }
            ]
          }
        }).success
      ).toBe(false);
    }
    expect(
      stageDynamaxAdventureRepairResponseSchema.safeParse(validUpdateResponse).success
    ).toBe(false);
    const repairErrorResponse = {
      ...validRepairResponse,
      diagnostics: [
        {
          domain: 'workflow.dynamaxAdventures',
          message: 'Dynamax Adventures repair is no longer available.',
          severity: 'error' as const
        }
      ],
      session: {
        hasPendingChanges: false,
        pendingEdits: [],
        sessionId: 'dynamax-session'
      }
    };
    expect(
      stageDynamaxAdventureRepairResponseSchema.safeParse(repairErrorResponse).success
    ).toBe(true);
    const restoreOwner = restoreWorkflow.encounters[0]!;
    const validRestoreResponse = {
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: [
          {
            domain: 'workflow.dynamaxAdventures',
            field: 'level',
            newValue: restoreOwner.level.toString(),
            recordId: `dynamaxAdventure:${restoreOwner.entryIndex}`,
            sources: [
              {
                layer: restoreOwner.provenance.sourceLayer,
                relativePath: restoreOwner.provenance.sourceFile
              }
            ],
            summary: 'Restore the vanilla Dynamax Adventures table.'
          }
        ],
        sessionId: 'dynamax-session'
      },
      workflow: restoreWorkflow
    };
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse(validRestoreResponse)
        .success
    ).toBe(true);
    expect(
      updateDynamaxAdventureFieldResponseSchema.safeParse(validRestoreResponse).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...validRestoreResponse,
        session: {
          ...validRestoreResponse.session,
          pendingEdits: [
            {
              ...validRestoreResponse.session.pendingEdits[0]!,
              summary: 'Restore one Adventure row.'
            }
          ]
        }
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...validRestoreResponse,
        session: {
          ...validRestoreResponse.session,
          pendingEdits: [
            {
              ...validRestoreResponse.session.pendingEdits[0]!,
              newValue: '100'
            }
          ]
        }
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...validRestoreResponse,
        session: {
          ...validRestoreResponse.session,
          pendingEdits: [
            {
              ...validRestoreResponse.session.pendingEdits[0]!,
              sources: [{ layer: 'base', relativePath: restoreOwner.provenance.sourceFile }]
            }
          ]
        }
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...validRestoreResponse,
        workflow: { ...restoreWorkflow, canRestoreVanillaTable: false }
      }).success
    ).toBe(false);
    const restoreErrorResponse = {
      diagnostics: [
        {
          domain: 'workflow.dynamaxAdventures',
          message: 'Dynamax Adventures table restore is no longer available.',
          severity: 'error' as const
        }
      ],
      session: {
        hasPendingChanges: false,
        pendingEdits: [],
        sessionId: 'dynamax-session'
      },
      workflow
    };
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse(restoreErrorResponse)
        .success
    ).toBe(true);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...restoreErrorResponse,
        session: validUpdateResponse.session
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...restoreErrorResponse,
        session: validRestoreResponse.session
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...restoreErrorResponse,
        session: {
          ...validRestoreResponse.session,
          pendingEdits: [
            {
              ...validRestoreResponse.session.pendingEdits[0]!,
              sources: [
                {
                  layer: 'base',
                  relativePath: restoreOwner.provenance.sourceFile
                }
              ]
            }
          ]
        }
      }).success
    ).toBe(false);
    expect(
      stageDynamaxAdventureRestoreResponseSchema.safeParse({
        ...restoreErrorResponse,
        session: {
          ...validRestoreResponse.session,
          pendingEdits: [
            validRestoreResponse.session.pendingEdits[0]!,
            validUpdateResponse.session.pendingEdits[0]!
          ]
        }
      }).success
    ).toBe(false);
    let capturedRepairRequest: unknown;
    const repairBridge = createProjectBridge(async (requestJson) => {
      capturedRepairRequest = JSON.parse(requestJson);
      return JSON.stringify({ error: null, payload: validRepairResponse });
    });
    await repairBridge.stageDynamaxAdventureRepair({ paths, session: null });
    expect(capturedRepairRequest).toMatchObject({
      command: 'dynamaxAdventures.repair.stage',
      payload: { paths, session: null }
    });
    let capturedRestoreRequest: unknown;
    const restoreBridge = createProjectBridge(async (requestJson) => {
      capturedRestoreRequest = JSON.parse(requestJson);
      return JSON.stringify({ error: null, payload: validRestoreResponse });
    });
    await restoreBridge.stageDynamaxAdventureRestore({ paths, session: null });
    expect(capturedRestoreRequest).toMatchObject({
      command: 'dynamaxAdventures.restore.stage',
      payload: { paths, session: null }
    });
    expect(
      updateDynamaxAdventureFieldResponseSchema.safeParse({
        ...validUpdateResponse,
        session: { ...validUpdateResponse.session, hasPendingChanges: false }
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldResponseSchema.safeParse({
        ...validUpdateResponse,
        session: {
          ...validUpdateResponse.session,
          pendingEdits: [{ ...pendingEdit, recordId: 'dynamaxAdventure:226' }]
        }
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldResponseSchema.safeParse({
        ...validUpdateResponse,
        session: {
          ...validUpdateResponse.session,
          pendingEdits: [pendingEdit, { ...pendingEdit, recordId: 'dynamaxAdventure:1' }]
        }
      }).success
    ).toBe(false);
    expect(
      updateDynamaxAdventureFieldResponseSchema.safeParse({
        ...validUpdateResponse,
        session: {
          ...validUpdateResponse.session,
          pendingEdits: [{ ...pendingEdit, newValue: '101' }]
        }
      }).success
    ).toBe(false);

    let rejectedTransportCalls = 0;
    const requestValidatingBridge = createProjectBridge(async () => {
      rejectedTransportCalls += 1;
      throw new Error('Invalid Dynamax Adventures request reached the transport.');
    });
    await expect(
      requestValidatingBridge.loadDynamaxAdventuresWorkflow({
        paths: { ...paths, selectedGame: 'scarlet' }
      })
    ).rejects.toThrow();
    await expect(
      requestValidatingBridge.previewDynamaxAdventureDefaults({
        entryIndex: 226,
        form: 0,
        level: 65,
        paths,
        session: null,
        species: 467
      })
    ).rejects.toThrow();
    await expect(
      requestValidatingBridge.updateDynamaxAdventureField({
        entryIndex: 0,
        field: 'level',
        paths,
        session: null,
        value: '101'
      })
    ).rejects.toThrow();
    await expect(
      requestValidatingBridge.stageDynamaxAdventureRepair({
        paths: { ...paths, selectedGame: 'scarlet' },
        session: null
      })
    ).rejects.toThrow();
    await expect(
      requestValidatingBridge.stageDynamaxAdventureRestore({
        paths: { ...paths, selectedGame: 'scarlet' },
        session: null
      })
    ).rejects.toThrow();
    expect(rejectedTransportCalls).toBe(0);

    const mismatchedGameWorkflow = mutateWorkflow(workflow, (draft) => {
      draft.buildId =
        'A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000';
      draft.detectedGame = 'shield';
    });
    const mismatchBridge = createProjectBridge(async () =>
      JSON.stringify({
        error: null,
        payload: { workflow: mismatchedGameWorkflow }
      })
    );
    await expect(
      mismatchBridge.loadDynamaxAdventuresWorkflow({ paths })
    ).rejects.toThrow('response detected shield');
  });
});

function mutateWorkflow<T>(workflow: T, mutate: (draft: T) => void) {
  const draft = structuredClone(workflow);
  mutate(draft);
  return draft;
}

function createCanonicalDynamaxAdventuresWorkflow(
  compactWorkflow: DynamaxAdventuresWorkflow
): DynamaxAdventuresWorkflow {
  const template = compactWorkflow.encounters[0]!;
  const encounters = Array.from({ length: 273 }, (_, entryIndex) => {
    const encounter: DynamaxAdventureRecord = {
      ...structuredClone(template),
      adventureIndex: entryIndex,
      entryIndex,
      label: `Adventure ${entryIndex.toString().padStart(3, '0')}`,
      singleCaptureFlagBlock: `0x${entryIndex.toString(16).toUpperCase().padStart(16, '0')}`,
      uiMessageId: `0x${(entryIndex + 0x1000)
        .toString(16)
        .toUpperCase()
        .padStart(16, '0')}`
    };
    if (entryIndex >= 226) {
      encounter.isEditable = false;
      encounter.layoutWritableFields = [];
      encounter.vanillaPokemon = createSnapshot(encounter);
    }
    return encounter;
  });

  return {
    ...compactWorkflow,
    encounters,
    stats: {
      guaranteedPerfectIvEncounterCount: encounters.length,
      singleCaptureCount: encounters.length,
      sourceFileCount: 1,
      storyGatedCount: 0,
      totalEncounterCount: encounters.length
    }
  };
}

function createSnapshot(
  encounter: DynamaxAdventureRecord
): DynamaxAdventurePokemonSnapshot {
  return {
    ability: encounter.ability,
    abilityLabel: encounter.abilityLabel,
    form: encounter.form,
    gigantamaxLabel: encounter.gigantamaxLabel,
    gigantamaxState: encounter.gigantamaxState,
    guaranteedPerfectIvs: encounter.guaranteedPerfectIvs,
    ivs: structuredClone(encounter.ivs),
    ivSummary: encounter.ivSummary,
    level: encounter.level,
    moves: structuredClone(encounter.moves),
    species: encounter.species,
    speciesId: encounter.speciesId
  };
}
