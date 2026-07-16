/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, Save, Shirt, Trash2, Wrench } from 'lucide-react';
import {
  type EditSession,
  type ProjectGame
} from '../../bridge/contracts';
import {
  type GymUniformRemovalAction,
  type GymUniformRemovalWorkflow,
  getGymUniformRemovalIpsRelativePath
} from '../../bridge/gymUniformRemovalContracts';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import {
  formatBagHookStatus,
  formatFileState,
  formatSourceLayer
} from '../../utils/workflowFormatters';
import { getCanonicalGymUniformRemovalPendingAction } from './gymUniformRemovalPending';

export function GymUniformRemovalSection({
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
  stagingAction: GymUniformRemovalAction | null;
  workflow: GymUniformRemovalWorkflow | null;
}) {
  const { translateLiteral } = useLocalization();
  const pendingAction = getCanonicalGymUniformRemovalPendingAction(editSession, workflow);
  const isInstallStaged = pendingAction === 'install';
  const isUninstallStaged = pendingAction === 'uninstall';
  const isMatchingGame =
    workflow !== null &&
    (selectedGame === 'sword' || selectedGame === 'shield') &&
    workflow.detectedGame === selectedGame;
  const hasConflictingPendingState = editSession !== null && pendingAction === null;
  const isBusy =
    stagingAction !== null || isChangePlanCreating || isChangePlanApplying;
  const canStageInstall =
    isMatchingGame &&
    !hasConflictingPendingState &&
    workflow.summary.availability === 'available' &&
    (workflow.installStatus === 'available' || workflow.installStatus === 'installed');
  const canStageUninstall =
    isMatchingGame &&
    !hasConflictingPendingState &&
    workflow.summary.availability === 'available' &&
    workflow.canUninstall &&
    (workflow.installStatus === 'installed' || workflow.installStatus === 'blocked');
  const canReviewPlan = pendingAction !== null && !isBusy;
  const canApplyPlan =
    pendingAction !== null &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isBusy;
  const installLabel = workflow?.installStatus === 'installed'
    ? 'Stage Reinstall'
    : 'Stage Install';
  const ipsRelativePath = selectedGame === 'sword' || selectedGame === 'shield'
    ? getGymUniformRemovalIpsRelativePath(selectedGame)
    : 'unknown';

  return (
    <>
      <section aria-labelledby="gym-uniform-removal-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Shirt aria-hidden="true" size={18} />
          <h2 id="gym-uniform-removal-heading">Gym Uniform Removal</h2>
        </div>
        <p className="workflow-description">
          Gym Uniform Removal keeps gym challenge and gym leader battle scripts from
          switching the player into the gym uniform.
        </p>
        <p className="workflow-description">
          KM writes a build-ID IPS patch in exefs, so Eden/Yuzu applies the handler
          override at load time while the current outfit stays on.
        </p>

        <div className="items-toolbar exefs-toolbar">
          <Metric
            label="Install"
            value={workflow ? formatBagHookStatus(workflow.installStatus) : 'Not loaded'}
          />
          <Metric
            label="IPS artifact"
            value={workflow ? formatIpsArtifactState(workflow.ipsArtifactState) : 'Not loaded'}
          />
          <Metric
            label="Patch site"
            value={workflow?.patchOffsetHex ?? 'Not loaded'}
            valueIsRaw={workflow !== null}
          />
          <Metric
            label="Reserved regions"
            value={workflow ? workflow.stats.reservedMainTextRegionCount.toString() : '0'}
          />
        </div>

        {workflow ? (
          <div className="flagwork-layout">
            <div className="flagwork-stack">
              <div
                aria-label="Gym Uniform Removal behavior summary"
                className="exefs-table iv-screen-range-table"
                role="table"
              >
                <div className="exefs-row iv-screen-range-row exefs-row-heading" role="row">
                  <span role="columnheader">Mode</span>
                  <span role="columnheader">What happens</span>
                </div>
                <div className="exefs-row iv-screen-range-row iv-screen-range-row-static" role="row">
                  <span role="cell">Not installed</span>
                  <span role="cell">Gym scripts call the normal uniform-change handler.</span>
                </div>
                <div className="exefs-row iv-screen-range-row iv-screen-range-row-static" role="row">
                  <span role="cell">Installed</span>
                  <span role="cell">
                    The IPS patch makes the handler return success, and the outfit does not change.
                  </span>
                </div>
              </div>

              <div
                aria-label="Gym Uniform Removal reserved ranges"
                className="exefs-table iv-screen-range-table"
                role="table"
              >
                <div className="exefs-row iv-screen-range-row exefs-row-heading" role="row">
                  <span role="columnheader">Region</span>
                  <span role="columnheader">Range</span>
                </div>
                {workflow.reservedRegions.map((region) => (
                  <div
                    className="exefs-row iv-screen-range-row iv-screen-range-row-static"
                    key={region.regionId}
                    role="row"
                  >
                    <span role="cell">{region.label}</span>
                    <span data-localization-ignore="true" role="cell">
                      {region.offsetLabel}
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <aside
              aria-label="Gym Uniform Removal install details"
              className="encounter-inspector"
            >
              <div className="panel-heading">
                <Shirt aria-hidden="true" size={18} />
                <h3>Install Details</h3>
              </div>

              <dl className="item-provenance-list">
                <div>
                  <dt>Install status</dt>
                  <dd>{formatBagHookStatus(workflow.installStatus)}</dd>
                </div>
                <div>
                  <dt>Game</dt>
                  <dd>{formatProjectGame(workflow.detectedGame, translateLiteral)}</dd>
                </div>
                <div>
                  <dt>Build ID</dt>
                  <dd data-localization-ignore="true">{workflow.buildId}</dd>
                </div>
                <div>
                  <dt>Patch site</dt>
                  <dd data-localization-ignore="true">{workflow.patchOffsetHex}</dd>
                </div>
                <div>
                  <dt>Main handler</dt>
                  <dd>{formatMainHandlerState(workflow.mainHandlerState)}</dd>
                </div>
                <div>
                  <dt>IPS artifact</dt>
                  <dd>{formatIpsArtifactState(workflow.ipsArtifactState)}</dd>
                </div>
                <div>
                  <dt>IPS file</dt>
                  <dd data-localization-ignore="true">{ipsRelativePath}</dd>
                </div>
                <div>
                  <dt>Source file</dt>
                  <dd data-localization-ignore="true">{workflow.provenance.sourceFile}</dd>
                </div>
                <div>
                  <dt>Layer</dt>
                  <dd>{formatSourceLayer(workflow.provenance.sourceLayer)}</dd>
                </div>
                <div>
                  <dt>File state</dt>
                  <dd>{formatFileState(workflow.provenance.fileState)}</dd>
                </div>
                <div>
                  <dt>Owned bytes</dt>
                  <dd>{workflow.stats.ownedByteCount}</dd>
                </div>
                <div>
                  <dt>Verified sources</dt>
                  <dd>{workflow.stats.sourceFileCount}</dd>
                </div>
                <div>
                  <dt>Uninstall available</dt>
                  <dd>{workflow.canUninstall ? 'Available' : 'Unavailable'}</dd>
                </div>
              </dl>

              <div className="encounter-edit-form">
                <div className="form-actions">
                  <button
                    aria-busy={stagingAction === 'install'}
                    className="primary-button"
                    disabled={!canStageInstall || isBusy || isInstallStaged}
                    onClick={onStageInstall}
                    type="button"
                  >
                    <Wrench aria-hidden="true" size={16} />
                    <span>
                      {stagingAction === 'install' ? 'Staging install' : installLabel}
                    </span>
                  </button>
                  <button
                    aria-busy={stagingAction === 'uninstall'}
                    className="danger-button"
                    disabled={!canStageUninstall || isBusy || isUninstallStaged}
                    onClick={onStageUninstall}
                    type="button"
                  >
                    <Trash2 aria-hidden="true" size={16} />
                    <span>
                      {stagingAction === 'uninstall'
                        ? 'Staging uninstall'
                        : 'Stage Uninstall'}
                    </span>
                  </button>
                  <button
                    aria-busy={isChangePlanCreating || undefined}
                    className="secondary-button"
                    disabled={!canReviewPlan}
                    onClick={onCreateChangePlan}
                    type="button"
                  >
                    <ClipboardCheck aria-hidden="true" size={16} />
                    <span>{isChangePlanCreating ? 'Reviewing' : 'Review'}</span>
                  </button>
                  <button
                    aria-busy={isChangePlanApplying || undefined}
                    className="primary-button"
                    disabled={!canApplyPlan}
                    onClick={onApplyChangePlan}
                    type="button"
                  >
                    <Save aria-hidden="true" size={16} />
                    <span>{isChangePlanApplying ? 'Applying' : 'Apply'}</span>
                  </button>
                </div>

                <dl className="encounter-slot-detail">
                  <div>
                    <dt>Install message</dt>
                    <dd>{workflow.installMessage}</dd>
                  </div>
                  <div>
                    <dt>Staged change</dt>
                    <dd>
                      {isInstallStaged
                        ? 'Install or refresh'
                        : isUninstallStaged
                          ? 'Uninstall'
                          : 'None'}
                    </dd>
                  </div>
                  <div>
                    <dt>Uninstall</dt>
                    <dd>Deletes only the exact recognized generated build-ID IPS file.</dd>
                  </div>
                </dl>
              </div>
            </aside>
          </div>
        ) : (
          <p className="empty-copy">
            Open Gym Uniform Removal from Advanced Editors to inspect the patch site.
          </p>
        )}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}

function formatProjectGame(
  game: ProjectGame | null,
  translateLiteral: (literal: string) => string
) {
  switch (game) {
    case 'sword':
      return translateLiteral('Pokemon Sword');
    case 'shield':
      return translateLiteral('Pokemon Shield');
    default:
      return translateLiteral('Unknown');
  }
}

function formatMainHandlerState(state: GymUniformRemovalWorkflow['mainHandlerState']) {
  switch (state) {
    case 'notInspected': return 'Not inspected';
    case 'vanilla': return 'Vanilla handler';
    case 'kmReturnTrue': return 'KM return-true handler';
    case 'compatibleReturnTrue': return 'Compatible return-true handler';
    case 'foreign': return 'Foreign handler bytes';
    case 'conflict': return 'Conflicting handler bytes';
    case 'unsupported': return 'Unsupported build';
    case 'gameMismatch': return 'game mismatch';
    case 'unreadable': return 'unreadable';
  }
}

function formatIpsArtifactState(state: GymUniformRemovalWorkflow['ipsArtifactState']) {
  switch (state) {
    case 'notInspected': return 'Not inspected';
    case 'notPresent': return 'Not present';
    case 'current': return 'Current KM IPS';
    case 'legacy': return 'Recognized legacy IPS';
    case 'foreign': return 'Foreign IPS';
    case 'invalid': return 'Invalid IPS';
  }
}
