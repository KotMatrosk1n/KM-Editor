/* SPDX-License-Identifier: GPL-3.0-only */

export type StageActionState = {
  isAllowed: boolean;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isCurrent: boolean;
  isStaging: boolean;
};

export function canStageAdvancedEditorAction(state: StageActionState) {
  return (
    state.isAllowed &&
    !state.isCurrent &&
    !state.isStaging &&
    !state.isChangePlanCreating &&
    !state.isChangePlanApplying
  );
}
