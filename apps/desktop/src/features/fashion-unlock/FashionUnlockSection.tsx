/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, Save, Shirt, Trash2, Wrench } from 'lucide-react';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import {
  type EditSession,
  type ProjectGame
} from '../../bridge/contracts';
import { type FashionUnlockAction, type FashionUnlockWorkflow } from '../../bridge/fashionUnlockContracts';
import { useLocalization } from '../../localization';
import { formatBagHookStatus, formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';
import { getCanonicalFashionUnlockPendingAction } from './fashionUnlockPending';

export function FashionUnlockSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  onApplyChangePlan,
  onCreateChangePlan,
  onStageInstall,
  onStageUninstall,
  panelOutput,
  selectedGame,
  stagingAction,
  workflow
}: {
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onStageInstall: () => void;
  onStageUninstall: () => void;
  panelOutput: WorkflowPanelOutput;
  selectedGame: ProjectGame | null;
  stagingAction: FashionUnlockAction | null;
  workflow: FashionUnlockWorkflow | null;
}) {
  const { translateLiteral } = useLocalization();
  const pendingAction = getCanonicalFashionUnlockPendingAction(editSession, workflow);
  const isInstallStaged = pendingAction === 'install';
  const isUninstallStaged = pendingAction === 'uninstall';
  const isScarletViolet = workflow?.editorFamily === 'sv';
  const expectedFamily = selectedGame === 'scarlet' || selectedGame === 'violet'
    ? 'sv'
    : selectedGame === 'sword' || selectedGame === 'shield'
      ? 'swsh'
      : null;
  const isMatchingGame =
    workflow !== null &&
    expectedFamily !== null &&
    workflow.editorFamily === expectedFamily &&
    workflow.detectedGame === selectedGame;
  const hasConflictingPendingState = editSession !== null && pendingAction === null;
  const isBusy =
    stagingAction !== null || isChangePlanCreating || isChangePlanApplying;
  const hasStagedChange = pendingAction !== null;
  const canStageInstall =
    isMatchingGame &&
    !hasConflictingPendingState &&
    workflow?.summary.availability === 'available' &&
    (workflow.installStatus === 'available' || workflow.installStatus === 'installed');
  const canStageUninstall =
    isMatchingGame &&
    !hasConflictingPendingState &&
    workflow?.summary.availability === 'available' &&
    workflow.installStatus === 'installed' &&
    workflow.canUninstall;
  const canReviewPlan = hasStagedChange && !isBusy;
  const canApplyPlan =
    hasStagedChange &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isBusy;
  const installLabel = workflow?.installStatus === 'installed' ? 'Stage Reinstall' : 'Stage Install';
  const primaryOffsetLabel = isScarletViolet ? 'Ownership check' : 'Direct getter';
  const primaryOffsetValue = isScarletViolet
    ? workflow?.ownershipCheckOffsetHex
    : workflow?.directGetterOffsetHex;

  return (
    <>
      <section aria-labelledby="fashion-unlock-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Shirt aria-hidden="true" size={18} />
          <h2 id="fashion-unlock-heading">Fashion Unlock</h2>
        </div>
        <p className="workflow-description">
          {isScarletViolet
            ? 'Fashion Unlock makes Scarlet/Violet dress-up ownership checks return unlocked at runtime, without editing the save file.'
            : 'Fashion Unlock makes Sword/Shield fashion ownership checks return unlocked at runtime, without importing a PKHeX save block.'}
        </p>
        <p className="workflow-description">
          {isScarletViolet
            ? 'KM edits only the verified dress-up ownership check bytes in exefs/main; acquired clothing data in the save file stays untouched.'
            : 'KM edits only the verified ownership getter bytes in exefs/main; gender is not selected because the check result is forced after the save has already loaded.'}
        </p>

        <div className="items-toolbar exefs-toolbar">
          <Metric label="Install" value={workflow ? formatBagHookStatus(workflow.installStatus) : 'Not loaded'} />
          <Metric
            label={primaryOffsetLabel}
            value={primaryOffsetValue ?? 'Not loaded'}
            valueIsRaw={primaryOffsetValue !== undefined}
          />
          <Metric label="Reserved regions" value={workflow ? workflow.stats.reservedMainTextRegionCount.toString() : '0'} />
        </div>

        {workflow ? (
          <div className="flagwork-layout">
            <div className="flagwork-stack">
              <div className="exefs-table iv-screen-range-table" role="table" aria-label="Fashion Unlock behavior summary">
                <div className="exefs-row iv-screen-range-row exefs-row-heading" role="row">
                  <span role="columnheader">Mode</span>
                  <span role="columnheader">What happens</span>
                </div>
                <div className="exefs-row iv-screen-range-row iv-screen-range-row-static" role="row">
                  <span role="cell">Not installed</span>
                  <span role="cell">{isScarletViolet ? 'Dress-up ownership comes from the player save.' : 'Fashion ownership comes from the player save.'}</span>
                </div>
                <div className="exefs-row iv-screen-range-row iv-screen-range-row-static" role="row">
                  <span role="cell">Installed</span>
                  <span role="cell">
                    {isScarletViolet
                      ? 'The dress-up ownership check returns unlocked.'
                      : 'Direct and mapped ownership getters return unlocked.'}
                  </span>
                </div>
              </div>

              <div className="exefs-table iv-screen-range-table" role="table" aria-label="Fashion Unlock reserved ranges">
                <div className="exefs-row iv-screen-range-row exefs-row-heading" role="row">
                  <span role="columnheader">Region</span>
                  <span role="columnheader">Range</span>
                </div>
                {workflow.reservedRegions.map((region) => (
                  <div className="exefs-row iv-screen-range-row iv-screen-range-row-static" key={region.regionId} role="row">
                    <span role="cell">{region.label}</span>
                    <span data-localization-ignore="true" role="cell">{region.offsetLabel}</span>
                  </div>
                ))}
              </div>
            </div>

            <aside aria-label="Fashion Unlock install details" className="encounter-inspector">
              <div className="panel-heading">
                <Shirt aria-hidden="true" size={18} />
                <h3>Install Details</h3>
              </div>

              <dl className="item-provenance-list">
                <div><dt>Install status</dt><dd>{formatBagHookStatus(workflow.installStatus)}</dd></div>
                <div><dt>Game</dt><dd>{formatProjectGame(workflow.detectedGame, translateLiteral)}</dd></div>
                <div><dt>Build ID</dt><dd data-localization-ignore="true">{workflow.buildId}</dd></div>
                {isScarletViolet ? (
                  <div><dt>Ownership check</dt><dd data-localization-ignore="true">{workflow.ownershipCheckOffsetHex}</dd></div>
                ) : (
                  <>
                    <div><dt>Direct getter</dt><dd data-localization-ignore="true">{workflow.directGetterOffsetHex}</dd></div>
                    <div><dt>Mapped getter</dt><dd data-localization-ignore="true">{workflow.mappedGetterOffsetHex}</dd></div>
                  </>
                )}
                <div><dt>Stub</dt><dd>{workflow.stubKind}</dd></div>
                <div><dt>Source file</dt><dd data-localization-ignore="true">{workflow.provenance.sourceFile}</dd></div>
                <div><dt>Layer</dt><dd>{formatSourceLayer(workflow.provenance.sourceLayer)}</dd></div>
                <div><dt>File state</dt><dd>{formatFileState(workflow.provenance.fileState)}</dd></div>
              </dl>

              <div className="encounter-edit-form">
                <div className="form-actions">
                  <button
                    className="primary-button"
                    aria-busy={stagingAction === 'install'}
                    disabled={!canStageInstall || isBusy || isInstallStaged}
                    onClick={onStageInstall}
                    type="button"
                  >
                    <Wrench aria-hidden="true" size={16} />
                    <span>{stagingAction === 'install' ? 'Staging install' : installLabel}</span>
                  </button>
                  <button
                    className="danger-button"
                    aria-busy={stagingAction === 'uninstall'}
                    disabled={!canStageUninstall || isBusy || isUninstallStaged}
                    onClick={onStageUninstall}
                    type="button"
                  >
                    <Trash2 aria-hidden="true" size={16} />
                    <span>{stagingAction === 'uninstall' ? 'Staging uninstall' : 'Stage Uninstall'}</span>
                  </button>
                  <button className="secondary-button" disabled={!canReviewPlan} onClick={onCreateChangePlan} type="button">
                    <ClipboardCheck aria-hidden="true" size={16} />
                    <span>{isChangePlanCreating ? 'Reviewing' : 'Review'}</span>
                  </button>
                  <button className="primary-button" disabled={!canApplyPlan} onClick={onApplyChangePlan} type="button">
                    <Save aria-hidden="true" size={16} />
                    <span>{isChangePlanApplying ? 'Applying' : 'Apply'}</span>
                  </button>
                </div>

                <dl className="encounter-slot-detail">
                  <div><dt>Install message</dt><dd>{workflow.installMessage}</dd></div>
                  <div>
                    <dt>Staged change</dt>
                    <dd>{isInstallStaged ? 'Install or refresh' : isUninstallStaged ? 'Uninstall' : 'None'}</dd>
                  </div>
                  <div>
                    <dt>Uninstall</dt>
                    <dd>
                      {isScarletViolet
                        ? 'Restores only the Fashion Unlock-owned dress-up ownership bytes and keeps other exefs/main edits.'
                        : 'Restores only Fashion Unlock-owned bytes and keeps other exefs/main edits.'}
                    </dd>
                  </div>
                </dl>
              </div>
            </aside>
          </div>
        ) : (
          <p className="empty-copy">Open Fashion Unlock from Advanced Editors to inspect ownership getter sites.</p>
        )}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}

function formatProjectGame(game: ProjectGame | null, translateLiteral: (literal: string) => string) {
  switch (game) {
    case 'sword': return translateLiteral('Pokemon Sword');
    case 'shield': return translateLiteral('Pokemon Shield');
    case 'scarlet': return translateLiteral('Pokemon Scarlet');
    case 'violet': return translateLiteral('Pokemon Violet');
    default: return translateLiteral('Unknown');
  }
}
