/* SPDX-License-Identifier: GPL-3.0-only */

import { GitMerge, Package, X } from 'lucide-react';
import { type ChangePlanOutputMode } from '../../bridge/contracts';

type SvOutputConfirmationModalProps = {
  isApplying: boolean;
  mode: ChangePlanOutputMode;
  onCancel: () => void;
  onConfirm: () => void;
  outputRootPath: string | null;
};

export function SvOutputConfirmationModal({
  isApplying,
  mode,
  onCancel,
  onConfirm,
  outputRootPath
}: SvOutputConfirmationModalProps) {
  const isStandalone = mode === 'standalone';
  const headingId = 'sv-output-confirmation-heading';
  const title = isStandalone ? 'Output as Standalone?' : 'Output for Trinity Mod Manager?';
  const outputRootLabel = outputRootPath?.trim() || 'the configured Output Root';

  return (
    <div aria-labelledby={headingId} aria-modal="true" className="modal-backdrop" role="dialog">
      <section className="modal-panel">
        <div className="panel-heading">
          {isStandalone ? (
            <Package aria-hidden="true" size={18} />
          ) : (
            <GitMerge aria-hidden="true" size={18} />
          )}
          <h2 id={headingId}>{title}</h2>
        </div>
        {isStandalone ? (
          <>
            <p className="modal-copy">
              KM Editor will write the edited Scarlet/Violet files under{' '}
              <strong>{outputRootLabel}</strong> as standalone LayeredFS output.
            </p>
            <p className="modal-copy modal-copy-muted">
              This will create or replace loose files under <code>romfs/</code> and patch{' '}
              <code>romfs/arc/data.trpfd</code> so the output can be installed directly without
              Trinity Mod Manager.
            </p>
          </>
        ) : (
          <>
            <p className="modal-copy">
              KM Editor will write the edited Scarlet/Violet files under{' '}
              <strong>{outputRootLabel}</strong> as a Trinity Mod Manager folder mod.
            </p>
            <p className="modal-copy modal-copy-muted">
              This writes RomFS-relative files such as <code>world/data/...</code> and does not
              patch <code>romfs/arc/data.trpfd</code>. Run this folder through Trinity Mod Manager
              before installing it in game or in an emulator.
            </p>
          </>
        )}
        <p className="modal-copy modal-copy-muted">
          Existing output files at the planned target paths may be replaced. Base RomFS and Base
          ExeFS are not modified.
        </p>
        <div className="modal-actions">
          <button className="secondary-button" disabled={isApplying} onClick={onCancel} type="button">
            <X aria-hidden="true" size={16} />
            <span>Cancel</span>
          </button>
          <button className="primary-button" disabled={isApplying} onClick={onConfirm} type="button">
            {isStandalone ? (
              <Package aria-hidden="true" size={16} />
            ) : (
              <GitMerge aria-hidden="true" size={16} />
            )}
            <span>{isApplying ? 'Outputting' : 'Confirm Output'}</span>
          </button>
        </div>
      </section>
    </div>
  );
}
