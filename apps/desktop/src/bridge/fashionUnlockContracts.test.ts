/* SPDX-License-Identifier: GPL-3.0-only */

import {
  fashionUnlockWorkflowSchema,
  loadFashionUnlockWorkflowRequestSchema,
  stageFashionUnlockInstallRequestSchema,
  stageFashionUnlockUninstallRequestSchema
} from './fashionUnlockContracts';
import {
  createSvFashionUnlockWorkflow,
  createSwShFashionUnlockWorkflow
} from '../testSupport/fashionUnlockTestFixtures';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

describe('Fashion Unlock bridge contracts', () => {
  it.each(['sword', 'shield'] as const)(
    'accepts canonical %s getter ownership and installed states',
    (game) => {
      expect(fashionUnlockWorkflowSchema.parse(createSwShFashionUnlockWorkflow(game)))
        .toMatchObject({ detectedGame: game, editorFamily: 'swsh' });
      expect(fashionUnlockWorkflowSchema.parse(createSwShFashionUnlockWorkflow(game, true)))
        .toMatchObject({ canUninstall: true, installStatus: 'installed' });
    }
  );

  it.each(['scarlet', 'violet'] as const)(
    'accepts canonical %s dress-up ownership and installed states',
    (game) => {
      expect(fashionUnlockWorkflowSchema.parse(createSvFashionUnlockWorkflow(game)))
        .toMatchObject({ detectedGame: game, editorFamily: 'sv' });
      expect(fashionUnlockWorkflowSchema.parse(createSvFashionUnlockWorkflow(game, true)))
        .toMatchObject({ canUninstall: true, installStatus: 'installed' });
    }
  );

  it('accepts legitimate disabled and blocked Sword/Shield states with selected-game ranges', () => {
    const disabled = {
      ...createSwShFashionUnlockWorkflow(),
      buildId: 'unknown' as const,
      canUninstall: false,
      detectedGame: null,
      directGetterOffsetHex: 'unknown',
      installMessage: 'Fashion Unlock cannot load until project paths validate.',
      installStatus: 'disabled' as const,
      mappedGetterOffsetHex: 'unknown',
      provenance: {
        fileState: 'baseOnly' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'generated' as const
      },
      stats: { ownedByteCount: 16, reservedMainTextRegionCount: 2, sourceFileCount: 0 },
      stubKind: 'not inspected' as const,
      summary: {
        ...createSwShFashionUnlockWorkflow().summary,
        availability: 'disabled' as const
      }
    };
    expect(fashionUnlockWorkflowSchema.parse(disabled).installStatus).toBe('disabled');
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...disabled,
      provenance: { ...disabled.provenance, sourceLayer: 'base' }
    })).toThrow(/missing generated provenance/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...disabled,
      provenance: {
        fileState: 'layeredOverride',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      }
    })).toThrow(/missing generated provenance/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...disabled,
      installStatus: 'blocked',
      stats: { ...disabled.stats, sourceFileCount: 1 },
      summary: { ...disabled.summary, availability: 'available' }
    })).toThrow(/generated.*verified source/i);

    const blocked = {
      ...disabled,
      buildId: 'F'.repeat(40),
      installMessage: 'Unsupported build.',
      installStatus: 'blocked' as const,
      provenance: {
        fileState: 'baseOnly' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'base' as const
      },
      stats: { ...disabled.stats, sourceFileCount: 0 },
      stubKind: 'unsupported' as const,
      summary: { ...disabled.summary, availability: 'available' as const }
    };
    expect(fashionUnlockWorkflowSchema.parse(blocked).installStatus).toBe('blocked');

    expect(() => fashionUnlockWorkflowSchema.parse({
      ...blocked,
      stats: { ...blocked.stats, sourceFileCount: 1 }
    })).toThrow(/source verification/i);
    const layeredBlocked = {
      ...blocked,
      provenance: {
        fileState: 'layeredOverride' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'layered' as const
      },
      stats: { ...blocked.stats, sourceFileCount: 1 }
    };
    expect(fashionUnlockWorkflowSchema.parse(layeredBlocked).stats.sourceFileCount).toBe(1);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...layeredBlocked,
      stats: { ...layeredBlocked.stats, sourceFileCount: 2 }
    })).toThrow(/source verification/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...layeredBlocked,
      provenance: { ...layeredBlocked.provenance, fileState: 'layeredOnly' }
    })).toThrow(/source verification/i);
  });

  it('rejects wrong build, offset, family field, region, and statistic mappings', () => {
    const swsh = createSwShFashionUnlockWorkflow();
    expect(() => fashionUnlockWorkflowSchema.parse({ ...swsh, buildId: 'F'.repeat(40) }))
      .toThrow(/build and getter offsets/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      directGetterOffsetHex: 'main.text+0x0143A2E0'
    })).toThrow(/build and getter offsets/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      ownershipCheckOffsetHex: 'main.text+0x00EAE95C'
    })).toThrow();
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      reservedRegions: swsh.reservedRegions.slice(0, 1)
    })).toThrow(/owned ranges/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      stats: { ...swsh.stats, ownedByteCount: 8 }
    })).toThrow(/owned ranges/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      provenance: {
        fileState: 'layeredOnly',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      },
      stats: { ...swsh.stats, sourceFileCount: 2 }
    })).toThrow(/verified base override/i);
  });

  it('requires game-mismatch ranges to belong to the selected opposite title', () => {
    const detectedShield = createSwShFashionUnlockWorkflow('shield');
    const selectedSwordRanges = createSwShFashionUnlockWorkflow('sword').reservedRegions;
    const mismatch = {
      ...detectedShield,
      installMessage: 'Selected Pokemon Sword, but exefs/main is Pokemon Shield.',
      installStatus: 'blocked' as const,
      reservedRegions: selectedSwordRanges,
      stats: { ...detectedShield.stats, sourceFileCount: 0 },
      stubKind: 'game mismatch' as const
    };

    expect(fashionUnlockWorkflowSchema.parse(mismatch).reservedRegions).toEqual(
      selectedSwordRanges
    );
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...mismatch,
      reservedRegions: detectedShield.reservedRegions
    })).toThrow(/owned ranges/i);
  });

  it('rejects impossible provenance, status, uninstall, and summary truth', () => {
    const swsh = createSwShFashionUnlockWorkflow();
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      provenance: { ...swsh.provenance, sourceLayer: 'pending' }
    })).toThrow(/provenance/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      installStatus: 'installed',
      stubKind: 'return-true ownership stubs'
    })).toThrow(/removable layered override/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      installStatus: 'disabled'
    })).toThrow(/availability/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      summary: { ...swsh.summary, id: 'typeChart' }
    })).toThrow(/identity/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      stats: { ...swsh.stats, sourceFileCount: 0 }
    })).toThrow(/source count/i);

    const installedSv = createSvFashionUnlockWorkflow('scarlet', true);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...installedSv,
      canUninstall: false
    })).toThrow(/uninstall capability/i);

    const readOnlyInstalledSv = {
      ...installedSv,
      canUninstall: false,
      provenance: {
        fileState: 'baseOnly' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'base' as const
      },
      summary: { ...installedSv.summary, availability: 'readOnly' as const }
    };
    expect(fashionUnlockWorkflowSchema.parse(readOnlyInstalledSv).canUninstall).toBe(false);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...readOnlyInstalledSv,
      canUninstall: true
    })).toThrow(/uninstall capability/i);

    const layeredOnlyInstalledSv = {
      ...installedSv,
      canUninstall: false,
      provenance: { ...installedSv.provenance, fileState: 'layeredOnly' as const }
    };
    expect(fashionUnlockWorkflowSchema.parse(layeredOnlyInstalledSv).canUninstall).toBe(false);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...layeredOnlyInstalledSv,
      canUninstall: true
    })).toThrow(/uninstall capability/i);
  });

  it('enforces disabled and read-only provenance across both editor families', () => {
    const sv = createSvFashionUnlockWorkflow();
    const disabledSv = {
      ...sv,
      buildId: 'unknown' as const,
      canUninstall: false,
      detectedGame: null,
      installMessage: 'Fashion Unlock cannot load until project paths validate.',
      installStatus: 'disabled' as const,
      ownershipCheckOffsetHex: 'unknown',
      provenance: {
        fileState: 'baseOnly' as const,
        sourceFile: 'exefs/main' as const,
        sourceLayer: 'generated' as const
      },
      stats: { ...sv.stats, sourceFileCount: 0 },
      stubKind: 'not inspected' as const,
      summary: { ...sv.summary, availability: 'disabled' as const }
    };
    expect(fashionUnlockWorkflowSchema.parse(disabledSv).installStatus).toBe('disabled');
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...disabledSv,
      provenance: { ...disabledSv.provenance, sourceLayer: 'base' }
    })).toThrow(/missing generated provenance/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...disabledSv,
      provenance: {
        fileState: 'layeredOverride',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      }
    })).toThrow(/missing generated provenance/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...disabledSv,
      installStatus: 'blocked',
      stats: { ...disabledSv.stats, sourceFileCount: 1 },
      summary: { ...disabledSv.summary, availability: 'available' }
    })).toThrow(/generated.*verified source/i);

    const swsh = createSwShFashionUnlockWorkflow();
    const readOnlySwSh = {
      ...swsh,
      installStatus: 'readOnly' as const,
      summary: { ...swsh.summary, availability: 'readOnly' as const }
    };
    expect(fashionUnlockWorkflowSchema.parse(readOnlySwSh).installStatus).toBe('readOnly');
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...readOnlySwSh,
      provenance: {
        fileState: 'layeredOverride',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      },
      stats: { ...readOnlySwSh.stats, sourceFileCount: 2 }
    })).toThrow(/LayeredFS source/i);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...readOnlySwSh,
      installStatus: 'installed',
      stubKind: 'return-true ownership stubs'
    })).toThrow(/editable removable layered override/i);

    const readOnlyLayeredSv = createSvFashionUnlockWorkflow('scarlet', true);
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...readOnlyLayeredSv,
      canUninstall: false,
      summary: { ...readOnlyLayeredSv.summary, availability: 'readOnly' }
    })).toThrow(/LayeredFS source/i);
  });

  it('rejects nonblocked workflows without a detected canonical identity', () => {
    const swsh = createSwShFashionUnlockWorkflow();
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...swsh,
      buildId: 'unknown',
      detectedGame: null,
      directGetterOffsetHex: 'unknown',
      mappedGetterOffsetHex: 'unknown'
    })).toThrow(/detected supported game/i);

    const sv = createSvFashionUnlockWorkflow();
    expect(() => fashionUnlockWorkflowSchema.parse({
      ...sv,
      buildId: 'unknown',
      detectedGame: null,
      ownershipCheckOffsetHex: 'unknown'
    })).toThrow(/detected supported game/i);
  });

  it.each([
    [loadFashionUnlockWorkflowRequestSchema, {}],
    [stageFashionUnlockInstallRequestSchema, { session: null }],
    [stageFashionUnlockUninstallRequestSchema, { session: null }]
  ] as const)('rejects Pokemon Legends Z-A requests', (schema, extra) => {
    expect(() => schema.parse({
      paths: { ...projectPaths, selectedGame: 'za' },
      ...extra
    })).toThrow(/requires Sword, Shield, Scarlet, or Violet/i);
  });
});
