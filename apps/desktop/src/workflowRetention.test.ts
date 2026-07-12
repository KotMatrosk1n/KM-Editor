/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type { EditSession } from './bridge/contracts';
import {
  WorkflowLoadGeneration,
  createLoadedWorkflowEvictionState,
  createWorkflowRetentionSizeHint,
  estimateWorkflowRetentionUnits,
  getEditSessionOwnerSections,
  selectWorkflowSectionsToRefresh,
  selectWorkflowSectionsToEvict,
  touchWorkflowRecency
} from './workflowRetention';

describe('workflow retention', () => {
  it('keeps active, dirty, session-owned, and most-recent workflows while evicting older payloads', () => {
    const entries = [
      { cost: 10, section: 'items' as const },
      { cost: 10, section: 'moves' as const },
      { cost: 80, section: 'pokemon' as const },
      { cost: 10, section: 'text' as const },
      { cost: 10, section: 'trainers' as const }
    ];
    const protectedSections = new Set(['pokemon', 'items'] as const);

    expect(
      selectWorkflowSectionsToEvict(
        entries,
        ['items', 'moves', 'pokemon', 'text', 'trainers'],
        protectedSections,
        { maxCount: 4, maxUnits: 110, minimumRecent: 1 }
      )
    ).toEqual(['moves']);
  });

  it('allows protected workflows to exceed the normal budget instead of losing pending work', () => {
    expect(
      selectWorkflowSectionsToEvict(
        [
          { cost: 100, section: 'items' },
          { cost: 100, section: 'pokemon' }
        ],
        ['items', 'pokemon'],
        new Set(['items', 'pokemon']),
        { maxCount: 1, maxUnits: 1, minimumRecent: 0 }
      )
    ).toEqual([]);
  });

  it('maps every pending edit owner without clearing the edit session', () => {
    const session = {
      hasPendingChanges: true,
      pendingEdits: [
        { domain: 'workflow.items' },
        { domain: 'workflow.pokemon' },
        { domain: 'workflow.exefs' },
        { domain: 'workflow.svModMerger' }
      ],
      sessionId: 'session'
    } as EditSession;

    expect([...getEditSessionOwnerSections(session, null)]).toEqual([
      'items',
      'pokemon',
      'exefsPatches',
      'modMerger'
    ]);
  });

  it('creates a null-only eviction patch and leaves unrelated state out of the patch', () => {
    expect(createLoadedWorkflowEvictionState(['items', 'pokemon', 'spreadsheetImport', 'modMerger'])).toEqual({
      itemsWorkflow: null,
      pokemonWorkflow: null,
      spreadsheetImportPreview: null,
      spreadsheetImportWorkflow: null
    });
  });

  it('uses sampled structure rather than serialized text length for size awareness', () => {
    const small = { records: [{ id: 1, label: 'One' }] };
    const large = { records: Array.from({ length: 100 }, (_, id) => ({ id, label: `Row ${id}` })) };

    expect(estimateWorkflowRetentionUnits(large)).toBeGreaterThan(
      estimateWorkflowRetentionUnits(small)
    );
  });

  it('accounts for ancillary collection counts without materializing their entries', () => {
    const small = { resolutions: createWorkflowRetentionSizeHint(2), selected: new Set(['a']) };
    const large = {
      resolutions: createWorkflowRetentionSizeHint(2_000),
      selected: new Set(Array.from({ length: 100 }, (_, index) => index))
    };

    expect(estimateWorkflowRetentionUnits(large)).toBeGreaterThan(
      estimateWorkflowRetentionUnits(small)
    );
  });

  it('samples a fixed number of rows instead of traversing a full workflow array', () => {
    let rowReads = 0;
    const rows = new Proxy(
      Array.from({ length: 10_000 }, (_, id) => ({ id, label: `Row ${id}` })),
      {
        get(target, property, receiver) {
          if (typeof property === 'string' && /^\d+$/.test(property)) {
            rowReads += 1;
          }
          return Reflect.get(target, property, receiver);
        }
      }
    );

    estimateWorkflowRetentionUnits({ rows });

    expect(rowReads).toBeLessThanOrEqual(4);
  });

  it('moves a touched workflow to the most-recent end without duplicating it', () => {
    expect(touchWorkflowRecency(['items', 'pokemon', 'moves'], 'pokemon')).toEqual([
      'items',
      'moves',
      'pokemon'
    ]);
  });

  it('rejects superseded and invalidated workflow responses', () => {
    const generation = new WorkflowLoadGeneration();
    const first = generation.begin('pokemon');
    const second = generation.begin('pokemon');

    expect(generation.getActiveSections()).toEqual(['pokemon']);
    expect(generation.canCommit('pokemon', first)).toBe(false);
    expect(generation.finish('pokemon', first)).toBe('superseded');
    expect(generation.canCommit('pokemon', second)).toBe(true);
    generation.invalidate('pokemon');
    expect(generation.getActiveSections()).toEqual([]);
    expect(generation.canCommit('pokemon', second)).toBe(false);
    expect(generation.finish('pokemon', second)).toBe('invalidated');
  });

  it('refreshes only preferred and recent loaded workflows after apply', () => {
    const entries = [
      { cost: 1, section: 'items' as const },
      { cost: 1, section: 'moves' as const },
      { cost: 1, section: 'pokemon' as const },
      { cost: 1, section: 'trainers' as const },
      { cost: 1, section: 'modMerger' as const }
    ];

    expect(
      [...selectWorkflowSectionsToRefresh(
        entries,
        ['items', 'moves', 'pokemon', 'trainers', 'modMerger'],
        new Set(['moves']),
        2
      )]
    ).toEqual(['moves', 'trainers', 'pokemon']);

    expect(
      [...selectWorkflowSectionsToRefresh(
        [
          { cost: 1, section: 'staticEncounters' },
          { cost: 1, section: 'placement' },
          { cost: 1, section: 'items' }
        ],
        ['placement', 'items', 'staticEncounters'],
        new Set(['staticEncounters']),
        0
      )]
    ).toEqual(['staticEncounters', 'placement']);

    expect(
      [...selectWorkflowSectionsToRefresh(
        [
          { cost: 1, section: 'text' },
          { cost: 1, section: 'placement' }
        ],
        ['placement', 'text'],
        new Set(['text']),
        0
      )]
    ).toEqual(['text', 'placement']);

    expect(
      [...selectWorkflowSectionsToRefresh(
        [
          { cost: 1, section: 'royalCandy' },
          { cost: 1, section: 'placement' }
        ],
        ['placement', 'royalCandy'],
        new Set(['royalCandy']),
        0
      )]
    ).toEqual(['royalCandy', 'placement']);
  });
});
