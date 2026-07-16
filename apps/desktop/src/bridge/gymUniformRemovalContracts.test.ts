/* SPDX-License-Identifier: GPL-3.0-only */

import {
  gymUniformRemovalWorkflowSchema,
  loadGymUniformRemovalWorkflowRequestSchema,
  stageGymUniformRemovalInstallRequestSchema,
  stageGymUniformRemovalUninstallRequestSchema
} from './gymUniformRemovalContracts';
import { createGymUniformRemovalWorkflow } from '../testSupport/gymUniformRemovalTestFixtures';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

describe('Gym Uniform Removal bridge contracts', () => {
  it.each(['sword', 'shield'] as const)(
    'accepts canonical %s available and installed workflows',
    (game) => {
      expect(gymUniformRemovalWorkflowSchema.parse(
        createGymUniformRemovalWorkflow(game)
      )).toMatchObject({ detectedGame: game, installStatus: 'available' });
      expect(gymUniformRemovalWorkflowSchema.parse(
        createGymUniformRemovalWorkflow(game, true)
      )).toMatchObject({ canUninstall: true, installStatus: 'installed' });
    }
  );

  it('accepts only canonical disabled truth with the selected-game active range', () => {
    const available = createGymUniformRemovalWorkflow();
    const disabled = {
      ...available,
      buildId: 'unknown' as const,
      canUninstall: false,
      detectedGame: null,
      installMessage: 'Gym Uniform Removal cannot load until project paths validate.',
      installStatus: 'disabled' as const,
      ipsArtifactState: 'notInspected' as const,
      mainHandlerState: 'notInspected' as const,
      patchOffsetHex: 'unknown',
      provenance: {
        fileState: 'baseOnly' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'generated' as const
      },
      stats: { ...available.stats, sourceFileCount: 0 },
      summary: { ...available.summary, availability: 'disabled' as const }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(disabled).installStatus).toBe('disabled');
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...disabled,
      reservedRegions: []
    })).toThrow(/disabled.*canonical/i);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...disabled,
      stats: { ownedByteCount: 0, reservedMainTextRegionCount: 0, sourceFileCount: 0 }
    })).toThrow(/disabled.*canonical/i);
  });

  it('rejects wrong build, offset, range, byte count, and legacy DTO fields', () => {
    const workflow = createGymUniformRemovalWorkflow();
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...workflow,
      buildId: 'F'.repeat(40)
    })).toThrow(/build and patch offset/i);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...workflow,
      patchOffsetHex: 'main.text+0x01472630'
    })).toThrow(/build and patch offset/i);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...workflow,
      reservedRegions: createGymUniformRemovalWorkflow('shield').reservedRegions
    })).toThrow(/active range/i);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...workflow,
      stats: { ...workflow.stats, ownedByteCount: 16 }
    })).toThrow(/owned byte count/i);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...workflow,
      stubKind: 'vanilla handler'
    })).toThrow();
  });

  it('requires game-mismatch reservations to remain selected-game scoped', () => {
    const detectedShield = createGymUniformRemovalWorkflow('shield');
    const selectedSwordRegion = createGymUniformRemovalWorkflow('sword').reservedRegions;
    const mismatch = {
      ...detectedShield,
      canUninstall: false,
      installMessage: 'Selected Pokemon Sword, but base exefs/main is Pokemon Shield.',
      installStatus: 'blocked' as const,
      ipsArtifactState: 'notInspected' as const,
      mainHandlerState: 'gameMismatch' as const,
      reservedRegions: selectedSwordRegion,
      stats: { ...detectedShield.stats, sourceFileCount: 0 }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(mismatch).reservedRegions).toEqual(
      selectedSwordRegion
    );
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...mismatch,
      reservedRegions: detectedShield.reservedRegions
    })).toThrow(/active range/i);
  });

  it('rejects executable claims when base provenance is absent', () => {
    const workflow = createGymUniformRemovalWorkflow();
    const blocked = {
      ...workflow,
      buildId: 'unknown' as const,
      canUninstall: false,
      detectedGame: null,
      installMessage: 'Base exefs/main is missing.',
      installStatus: 'blocked' as const,
      ipsArtifactState: 'notInspected' as const,
      mainHandlerState: 'notInspected' as const,
      patchOffsetHex: 'unknown',
      provenance: {
        fileState: 'layeredOnly' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'layered' as const
      },
      stats: { ...workflow.stats, sourceFileCount: 0 }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(blocked).detectedGame).toBeNull();
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...blocked,
      buildId: workflow.buildId,
      detectedGame: 'sword',
      mainHandlerState: 'conflict',
      patchOffsetHex: workflow.patchOffsetHex
    })).toThrow(/without base provenance/i);
  });

  it('allows exact IPS uninstall while an effective main conflict blocks install', () => {
    const installed = createGymUniformRemovalWorkflow('sword', true);
    const conflict = {
      ...installed,
      installMessage: 'The effective main conflicts, but the exact IPS can be removed.',
      installStatus: 'blocked' as const,
      mainHandlerState: 'conflict' as const,
      provenance: {
        fileState: 'layeredOverride' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'layered' as const
      },
      stats: { ...installed.stats, sourceFileCount: 3 }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(conflict).canUninstall).toBe(true);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...conflict,
      canUninstall: false
    })).toThrow(/uninstall capability/i);
  });

  it('does not infer uninstall from a forged blocked artifact without verified sources', () => {
    const installed = createGymUniformRemovalWorkflow('sword', true);
    const forged = {
      ...installed,
      canUninstall: false,
      installStatus: 'blocked' as const,
      mainHandlerState: 'conflict' as const,
      stats: { ...installed.stats, sourceFileCount: 0 }
    };

    expect(() => gymUniformRemovalWorkflowSchema.parse(forged))
      .toThrow(/blocked.*source count/i);
  });

  it('accepts a verified base when the distinct layered effective main is unreadable', () => {
    const workflow = createGymUniformRemovalWorkflow();
    const blocked = {
      ...workflow,
      canUninstall: false,
      installMessage: 'The effective layered main is unreadable.',
      installStatus: 'blocked' as const,
      ipsArtifactState: 'notPresent' as const,
      mainHandlerState: 'unreadable' as const,
      provenance: {
        fileState: 'layeredOverride' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'layered' as const
      },
      stats: { ...workflow.stats, sourceFileCount: 1 }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(blocked).stats.sourceFileCount).toBe(1);
    expect(gymUniformRemovalWorkflowSchema.parse({
      ...blocked,
      stats: { ...blocked.stats, sourceFileCount: 2 }
    }).stats.sourceFileCount).toBe(2);
  });

  it('keeps exact IPS uninstall available when the optional layered main is unreadable', () => {
    const installed = createGymUniformRemovalWorkflow('sword', true);
    const blocked = {
      ...installed,
      installMessage: 'The optional layered main is unreadable, but the exact IPS is removable.',
      installStatus: 'blocked' as const,
      mainHandlerState: 'unreadable' as const,
      provenance: {
        fileState: 'layeredOverride' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'layered' as const
      },
      stats: { ...installed.stats, sourceFileCount: 2 }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(blocked)).toMatchObject({
      canUninstall: true,
      installStatus: 'blocked',
      ipsArtifactState: 'current',
      mainHandlerState: 'unreadable'
    });
    expect(gymUniformRemovalWorkflowSchema.parse({
      ...blocked,
      stats: { ...blocked.stats, sourceFileCount: 3 }
    })).toMatchObject({ canUninstall: true, stats: { sourceFileCount: 3 } });
  });

  it('counts same-file or distinct layered main and readable IPS exactly', () => {
    const installed = createGymUniformRemovalWorkflow('shield', true);
    const layered = {
      ...installed,
      provenance: {
        fileState: 'layeredOverride' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'layered' as const
      },
      stats: { ...installed.stats, sourceFileCount: 3 }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(layered).stats.sourceFileCount).toBe(3);
    expect(gymUniformRemovalWorkflowSchema.parse({
      ...layered,
      stats: { ...layered.stats, sourceFileCount: 2 }
    }).stats.sourceFileCount).toBe(2);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...layered,
      stats: { ...layered.stats, sourceFileCount: 1 }
    })).toThrow(/source count/i);
  });

  it('keeps read-only recognized IPS state non-removable', () => {
    const installed = createGymUniformRemovalWorkflow('shield', true);
    const readOnly = {
      ...installed,
      canUninstall: false,
      summary: { ...installed.summary, availability: 'readOnly' as const }
    };

    expect(gymUniformRemovalWorkflowSchema.parse(readOnly).canUninstall).toBe(false);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...readOnly,
      canUninstall: true
    })).toThrow(/uninstall capability/i);
  });

  it('rejects forged status and handler or artifact state combinations', () => {
    const available = createGymUniformRemovalWorkflow();
    const installed = createGymUniformRemovalWorkflow('sword', true);

    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...installed,
      mainHandlerState: 'unsupported'
    })).toThrow(/editable recognized main handler/i);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...available,
      mainHandlerState: 'conflict'
    })).toThrow(/editable recognized main handler/i);
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...available,
      installStatus: 'blocked'
    })).toThrow(/identify a main or IPS artifact blocker/i);

    expect(gymUniformRemovalWorkflowSchema.parse({
      ...available,
      installMessage: 'The present IPS file could not be read.',
      installStatus: 'blocked',
      ipsArtifactState: 'notInspected'
    })).toMatchObject({
      canUninstall: false,
      installStatus: 'blocked',
      ipsArtifactState: 'notInspected'
    });

    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...installed,
      buildId: 'unknown',
      canUninstall: false,
      detectedGame: null,
      installStatus: 'blocked',
      mainHandlerState: 'notInspected',
      patchOffsetHex: 'unknown',
      stats: { ...installed.stats, sourceFileCount: 0 }
    })).toThrow(/verified selected-game base identity/i);

    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...installed,
      installStatus: 'blocked',
      mainHandlerState: 'conflict'
    })).toThrow(/base-only.*vanilla/i);

    const blockedForeignInvalid = {
      ...available,
      installMessage: 'Both effective handler and IPS are non-owned.',
      installStatus: 'blocked' as const,
      ipsArtifactState: 'invalid' as const,
      mainHandlerState: 'foreign' as const,
      provenance: {
        fileState: 'layeredOverride' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'layered' as const
      },
      stats: { ...available.stats, sourceFileCount: 3 }
    };
    expect(gymUniformRemovalWorkflowSchema.parse(blockedForeignInvalid)).toMatchObject({
      installStatus: 'blocked',
      ipsArtifactState: 'invalid',
      mainHandlerState: 'foreign'
    });
    expect(() => gymUniformRemovalWorkflowSchema.parse({
      ...blockedForeignInvalid,
      installStatus: 'foreign'
    })).toThrow();
  });

  it.each([
    [loadGymUniformRemovalWorkflowRequestSchema, {}],
    [stageGymUniformRemovalInstallRequestSchema, { session: null }],
    [stageGymUniformRemovalUninstallRequestSchema, { session: null }]
  ] as const)('accepts Sword and rejects unrelated game requests', (schema, extra) => {
    expect(schema.parse({ paths: projectPaths, ...extra }).paths.selectedGame).toBe('sword');
    expect(() => schema.parse({
      paths: { ...projectPaths, selectedGame: 'scarlet' },
      ...extra
    })).toThrow(/requires Pokemon Sword or Pokemon Shield/i);
  });
});
