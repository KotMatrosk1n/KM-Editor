/* SPDX-License-Identifier: GPL-3.0-only */

import { Compass, X } from 'lucide-react';
import { useId } from 'react';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import type { CapabilityDiscoveryViewModel } from '../../workbench/capabilityDiscovery';
import type { WorkbenchSection } from '../../workbench/workbenchSections';

export type CapabilityDiscoveryDialogProps = {
  capabilities: readonly CapabilityDiscoveryViewModel[];
  isOpen: boolean;
  onClose: () => void;
  onOpenCapability: (section: WorkbenchSection) => void;
};

export function CapabilityDiscoveryDialog(props: CapabilityDiscoveryDialogProps) {
  return props.isOpen ? <OpenCapabilityDiscoveryDialog {...props} /> : null;
}

function OpenCapabilityDiscoveryDialog({
  capabilities,
  onClose,
  onOpenCapability
}: CapabilityDiscoveryDialogProps) {
  const { t } = useLocalization();
  const descriptionId = useId();
  const headingId = useId();
  const dialogRef = useModalDialog<HTMLDivElement>({ onClose });
  return (
    <div
      className="km-workbench-overlay km-capability-dialog-overlay"
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <div
        aria-describedby={descriptionId}
        aria-labelledby={headingId}
        aria-modal="true"
        className="km-capability-dialog"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <header className="km-capability-dialog-heading">
          <Compass aria-hidden="true" size={20} />
          <div>
            <h2 id={headingId}>{t('workbench.capabilityDialog.title')}</h2>
            <p id={descriptionId}>{t('workbench.capabilityDialog.description')}</p>
          </div>
          <button
            aria-label={t('workbench.capabilityDialog.close')}
            className="secondary-button icon-button"
            onClick={onClose}
            title={t('workbench.capabilityDialog.close')}
            type="button"
          >
            <X aria-hidden="true" size={17} />
          </button>
        </header>
        <div className="km-capability-dialog-body">
          {capabilities.length > 0 ? (
            <ul className="km-capability-dialog-list">
              {capabilities.map((capability) => (
                <li key={capability.id}>
                  <div>
                    <strong>{t(capability.labelKey)}</strong>
                    <small>{t(capability.descriptionKey)}</small>
                  </div>
                  <div className="km-capability-dialog-action">
                    <span className={`km-capability-status is-${capability.status}`}>
                      {t(capability.statusKey)}
                    </span>
                    {capability.reason || capability.reasonKey ? (
                      <small data-localization-ignore={capability.reason ? 'true' : undefined}>
                        {capability.reason ?? t(capability.reasonKey!)}
                      </small>
                    ) : null}
                    <button
                      className="secondary-button compact-button"
                      disabled={capability.status === 'blocked'}
                      onClick={() => {
                        onOpenCapability(capability.id);
                        onClose();
                      }}
                      type="button"
                    >
                      {t('workbench.capabilityDialog.open')}
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <p className="km-workbench-empty">{t('workbench.capabilityDialog.empty')}</p>
          )}
        </div>
      </div>
    </div>
  );
}
