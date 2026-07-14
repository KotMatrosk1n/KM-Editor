/* SPDX-License-Identifier: GPL-3.0-only */

import { GitMerge, Package, ShieldCheck, X } from 'lucide-react';
import { type ChangePlanOutputMode } from '../../bridge/contracts';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';

type TrinityOutputConfirmationModalProps = {
  isApplying: boolean;
  mode: ChangePlanOutputMode;
  onCancel: () => void;
  onConfirm: () => void;
  outputRootPath: string | null;
};

export function TrinityOutputConfirmationModal({
  isApplying,
  mode,
  onCancel,
  onConfirm,
  outputRootPath
}: TrinityOutputConfirmationModalProps) {
  const { translateLiteral } = useLocalization();
  const isStandalone = mode === 'standalone';
  const isTrinityBypass = mode === 'trinityBypass';
  const headingId = 'trinity-output-confirmation-heading';
  const title = isStandalone
    ? 'Output as Standalone?'
    : isTrinityBypass
      ? 'Output for Trinity Bypass?'
      : 'Output for Trinity Mod Manager?';
  const outputRootLabel = outputRootPath?.trim() || 'the configured Output Root';
  const OutputIcon = isStandalone ? Package : isTrinityBypass ? ShieldCheck : GitMerge;
  const dialogRef = useModalDialog<HTMLElement>({
    canClose: !isApplying,
    onClose: onCancel
  });

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby={headingId}
        aria-modal="true"
        className="modal-panel"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading">
          <OutputIcon aria-hidden="true" size={18} />
          <h2 id={headingId}>{translateLiteral(title)}</h2>
        </div>
        {isStandalone ? (
          <>
            <p className="modal-copy">
              {translateLiteral('KM Editor will write the edited files under')}{' '}
              <strong data-localization-ignore={Boolean(outputRootPath)}>
                {outputRootPath ? outputRootLabel : translateLiteral(outputRootLabel)}
              </strong>{' '}
              {translateLiteral('as standalone LayeredFS output.')}
            </p>
            <p className="modal-copy modal-copy-muted">
              {translateLiteral('This will create or replace loose files under')}{' '}
              <code>romfs/</code> {translateLiteral('and patch')}{' '}
              <span>{translateLiteral('the Trinity file index')}</span>{' '}
              {translateLiteral(
                'so the output can be installed directly without Trinity Mod Manager.'
              )}
            </p>
          </>
        ) : isTrinityBypass ? (
          <>
            <p className="modal-copy">
              {translateLiteral('KM Editor will write the edited files under')}{' '}
              <strong data-localization-ignore={Boolean(outputRootPath)}>
                {outputRootPath ? outputRootLabel : translateLiteral(outputRootLabel)}
              </strong>{' '}
              {translateLiteral('as loose LayeredFS RomFS files for Trinity Bypass.')}
            </p>
            <p className="modal-copy modal-copy-muted">
              {translateLiteral('This creates or replaces files under')} <code>romfs/</code>{' '}
              {translateLiteral('and does not patch')}{' '}
              <span>{translateLiteral('the Trinity file index')}</span>
              {translateLiteral(
                '. Use this only when Trinity Bypass is already installed for the selected game.'
              )}
            </p>
          </>
        ) : (
          <>
            <p className="modal-copy">
              {translateLiteral('KM Editor will write the edited files under')}{' '}
              <strong data-localization-ignore={Boolean(outputRootPath)}>
                {outputRootPath ? outputRootLabel : translateLiteral(outputRootLabel)}
              </strong>{' '}
              {translateLiteral('as a Trinity Mod Manager folder mod.')}
            </p>
            <p className="modal-copy modal-copy-muted">
              {translateLiteral('This writes RomFS-relative files such as')}{' '}
              <code>world/data/...</code> {translateLiteral('and does not patch')}{' '}
              <span>{translateLiteral('the Trinity file index')}</span>
              {translateLiteral(
                '. Run this folder through Trinity Mod Manager before installing it in game or in an emulator.'
              )}
            </p>
          </>
        )}
        <p className="modal-copy modal-copy-muted">
          {translateLiteral(
            'Existing output files at the planned target paths may be replaced. Base RomFS and Base ExeFS are not modified.'
          )}
        </p>
        <div className="modal-actions">
          <button className="secondary-button" disabled={isApplying} onClick={onCancel} type="button">
            <X aria-hidden="true" size={16} />
            <span>{translateLiteral('Cancel')}</span>
          </button>
          <button className="primary-button" disabled={isApplying} onClick={onConfirm} type="button">
            <OutputIcon aria-hidden="true" size={16} />
            <span>{translateLiteral(isApplying ? 'Outputting' : 'Confirm Output')}</span>
          </button>
        </div>
      </section>
    </div>
  );
}
