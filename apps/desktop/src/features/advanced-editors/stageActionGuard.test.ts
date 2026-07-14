/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { canStageAdvancedEditorAction, type StageActionState } from './stageActionGuard';

describe('advanced editor stage action guard', () => {
  const readyState: StageActionState = {
    isAllowed: true,
    isChangePlanApplying: false,
    isChangePlanCreating: false,
    isCurrent: false,
    isStaging: false
  };

  it('allows a new valid stage action while idle', () => {
    expect(canStageAdvancedEditorAction(readyState)).toBe(true);
  });

  it.each([
    ['the identical change is already staged', { isCurrent: true }],
    ['staging is in progress', { isStaging: true }],
    ['plan creation is in progress', { isChangePlanCreating: true }],
    ['plan application is in progress', { isChangePlanApplying: true }],
    ['the workflow disallows the action', { isAllowed: false }]
  ] as const)('blocks staging when %s', (_label, override) => {
    expect(canStageAdvancedEditorAction({ ...readyState, ...override })).toBe(false);
  });
});
