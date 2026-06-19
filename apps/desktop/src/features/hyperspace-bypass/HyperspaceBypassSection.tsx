/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, Save, Sparkle, Trash2, Wrench } from 'lucide-react';
import {
  Metric,
  WorkflowPanelOutputSections,
  type WorkflowPanelOutput
} from '../../components/workflowPanels';
import {
  type EditSession,
  type ProjectGame
} from '../../bridge/contracts';
import { type HyperspaceBypassWorkflow } from '../../bridge/hyperspaceBypassContracts';
import { formatBagHookStatus, formatFileState, formatSourceLayer } from '../../utils/workflowFormatters';

export function HyperspaceBypassSection({
  editSession,
  isChangePlanApplying,
  isChangePlanCreating,
  isStaging,
  onApplyChangePlan,
  onCreateChangePlan,
  onStageInstall,
  onStageUninstall,
  panelOutput,
  workflow
}: {
  editSession: EditSession | null;
  isChangePlanApplying: boolean;
  isChangePlanCreating: boolean;
  isStaging: boolean;
  onApplyChangePlan: () => void;
  onCreateChangePlan: () => void;
  onStageInstall: () => void;
  onStageUninstall: () => void;
  panelOutput: WorkflowPanelOutput;
  workflow: HyperspaceBypassWorkflow | null;
}) {
  const stagedEdit = editSession?.pendingEdits.find((edit) => edit.domain === 'workflow.hyperspaceBypass');
  const isInstallStaged = stagedEdit?.recordId === 'hyperspace-bypass-v1-install';
  const isUninstallStaged = stagedEdit?.recordId === 'hyperspace-bypass-v1-uninstall';
  const hasStagedChange = isInstallStaged || isUninstallStaged;
  const canStageInstall = workflow?.summary.availability === 'available' && workflow.installStatus !== 'blocked';
  const canStageUninstall = workflow?.summary.availability === 'available' && workflow.installStatus === 'installed';
  const canReviewPlan = hasStagedChange && !isChangePlanCreating;
  const canApplyPlan =
    hasStagedChange &&
    panelOutput.changePlan !== null &&
    panelOutput.changePlan.canApply &&
    panelOutput.changePlan.writes.length > 0 &&
    !isChangePlanApplying;
  const installLabel = workflow?.installStatus === 'installed' ? 'Stage Reinstall' : 'Stage Install';

  return (
    <>
      <section aria-labelledby="hyperspace-bypass-heading" className="panel wide-panel">
        <div className="panel-heading">
          <Sparkle aria-hidden="true" size={18} />
          <h2 id="hyperspace-bypass-heading">Hyperspace Bypass</h2>
        </div>
        <p className="workflow-description">
          Hyperspace Bypass makes Scarlet/Violet skip the Hoopa species and form gate for Hyperspace Hole and Hyperspace Fury.
        </p>
        <p className="workflow-description">
          KM edits only the verified runtime gate branch in exefs/main; move data, learnsets, and personal data stay untouched.
        </p>

        <div className="items-toolbar exefs-toolbar">
          <Metric label="Install" value={workflow ? formatBagHookStatus(workflow.installStatus) : 'Not loaded'} />
          <Metric label="Patch site" value={workflow?.patchOffsetHex ?? 'Not loaded'} />
          <Metric label="Reserved regions" value={workflow ? workflow.stats.reservedMainTextRegionCount.toString() : '0'} />
        </div>

        {workflow ? (
          <div className="flagwork-layout">
            <div className="flagwork-stack">
              <div className="exefs-table iv-screen-range-table" role="table" aria-label="Hyperspace Bypass behavior summary">
                <div className="exefs-row iv-screen-range-row exefs-row-heading" role="row">
                  <span role="columnheader">Mode</span>
                  <span role="columnheader">What happens</span>
                </div>
                <div className="exefs-row iv-screen-range-row" role="row">
                  <span role="cell">Not installed</span>
                  <span role="cell">The battle runtime rejects non-Hoopa and wrong-form users.</span>
                </div>
                <div className="exefs-row iv-screen-range-row" role="row">
                  <span role="cell">Installed</span>
                  <span role="cell">The checked path branches to the existing success return.</span>
                </div>
              </div>

              <div className="exefs-table iv-screen-range-table" role="table" aria-label="Hyperspace Bypass reserved ranges">
                <div className="exefs-row iv-screen-range-row exefs-row-heading" role="row">
                  <span role="columnheader">Region</span>
                  <span role="columnheader">Range</span>
                </div>
                {workflow.reservedRegions.map((region) => (
                  <div className="exefs-row iv-screen-range-row" key={region.regionId} role="row">
                    <span role="cell">{region.label}</span>
                    <span role="cell">{region.offsetLabel}</span>
                  </div>
                ))}
              </div>
            </div>

            <aside aria-label="Hyperspace Bypass install details" className="encounter-inspector">
              <div className="panel-heading">
                <Sparkle aria-hidden="true" size={18} />
                <h3>Install Details</h3>
              </div>

              <dl className="item-provenance-list">
                <div><dt>Install status</dt><dd>{formatBagHookStatus(workflow.installStatus)}</dd></div>
                <div><dt>Game</dt><dd>{formatProjectGame(workflow.detectedGame)}</dd></div>
                <div><dt>Build ID</dt><dd>{workflow.buildId}</dd></div>
                <div><dt>Patch site</dt><dd>{workflow.patchOffsetHex}</dd></div>
                <div><dt>Stub</dt><dd>{workflow.stubKind}</dd></div>
                <div><dt>Source file</dt><dd>{workflow.provenance.sourceFile}</dd></div>
                <div><dt>Layer</dt><dd>{formatSourceLayer(workflow.provenance.sourceLayer)}</dd></div>
                <div><dt>File state</dt><dd>{formatFileState(workflow.provenance.fileState)}</dd></div>
              </dl>

              <div className="encounter-edit-form">
                <div className="form-actions">
                  <button className="primary-button" disabled={!canStageInstall || isStaging} onClick={onStageInstall} type="button">
                    <Wrench aria-hidden="true" size={16} />
                    <span>{isStaging ? 'Staging' : installLabel}</span>
                  </button>
                  <button className="danger-button" disabled={!canStageUninstall || isStaging} onClick={onStageUninstall} type="button">
                    <Trash2 aria-hidden="true" size={16} />
                    <span>{isStaging ? 'Staging' : 'Stage Uninstall'}</span>
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
                  <div><dt>Uninstall</dt><dd>Restores only the Hyperspace Bypass-owned instruction and keeps other exefs/main edits.</dd></div>
                </dl>
              </div>
            </aside>
          </div>
        ) : (
          <p className="empty-copy">Open Hyperspace Bypass from Advanced Editors to inspect the runtime gate site.</p>
        )}
      </section>

      <WorkflowPanelOutputSections
        output={panelOutput}
        workflowDiagnostics={workflow?.diagnostics ?? []}
      />
    </>
  );
}

function formatProjectGame(game: ProjectGame | null) {
  switch (game) {
    case 'sword': return 'Pokemon Sword';
    case 'shield': return 'Pokemon Shield';
    case 'scarlet': return 'Pokemon Scarlet';
    case 'violet': return 'Pokemon Violet';
    default: return 'Unknown';
  }
}
