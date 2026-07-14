/* SPDX-License-Identifier: GPL-3.0-only */

import {
  AlertTriangle,
  ClipboardCheck,
  RefreshCw,
  Shuffle,
  X,
  type LucideIcon
} from 'lucide-react';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization/LocalizationProvider';

type RandomizerConfirmationKind = 'applySeed' | 'randomize' | 'restore';

type RandomizerConfirmationDefinition = {
  busyLabel: string;
  confirmClassName: string;
  confirmLabel: string;
  description: string | null;
  heading: string;
  icon: LucideIcon;
};

const confirmationDefinitions: Record<
  RandomizerConfirmationKind,
  RandomizerConfirmationDefinition
> = {
  applySeed: {
    busyLabel: 'Applying Seed',
    confirmClassName: 'purple-button',
    confirmLabel: 'Confirm Apply Seed',
    description:
      'KM Editor will read the pasted seed, select the options stored inside it, and immediately write that exact randomized output to Output Root. Existing output files for those data domains may be replaced.',
    heading: 'Apply Shared Randomization Seed?',
    icon: ClipboardCheck
  },
  randomize: {
    busyLabel: 'Randomizing',
    confirmClassName: 'primary-button',
    confirmLabel: 'Confirm Randomize',
    description:
      'KM Editor will write randomized output files for the selected categories to Output Root. Existing output files for those data domains may be replaced.',
    heading: 'Randomize Selected Data?',
    icon: Shuffle
  },
  restore: {
    busyLabel: 'Restoring',
    confirmClassName: 'danger-button',
    confirmLabel: 'Confirm Restore',
    description: null,
    heading: 'Restore Vanilla Values?',
    icon: RefreshCw
  }
};

export function RandomizerConfirmationModal({
  isBusy,
  kind,
  onCancel,
  onConfirm
}: {
  isBusy: boolean;
  kind: RandomizerConfirmationKind;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const { t, translateLiteral } = useLocalization();
  const definition = confirmationDefinitions[kind];
  const Icon = definition.icon;
  const headingId = `randomizer-${kind}-confirm-heading`;
  const descriptionId = `randomizer-${kind}-confirm-description`;
  const description = definition.description
    ? translateLiteral(definition.description)
    : t('randomizer.recovery.confirm');
  const dialogRef = useModalDialog<HTMLElement>({
    canClose: !isBusy,
    onClose: onCancel
  });

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-describedby={descriptionId}
        aria-labelledby={headingId}
        aria-modal="true"
        className="modal-panel"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading">
          <AlertTriangle aria-hidden="true" size={18} />
          <h2 id={headingId}>{translateLiteral(definition.heading)}</h2>
        </div>
        <p className="modal-copy" id={descriptionId}>
          {description}
        </p>
        <div className="modal-actions">
          <button
            className="secondary-button"
            disabled={isBusy}
            onClick={onCancel}
            type="button"
          >
            <X aria-hidden="true" size={16} />
            <span>{translateLiteral('Cancel')}</span>
          </button>
          <button
            className={definition.confirmClassName}
            disabled={isBusy}
            onClick={onConfirm}
            type="button"
          >
            <Icon aria-hidden="true" size={16} />
            <span>
              {translateLiteral(isBusy ? definition.busyLabel : definition.confirmLabel)}
            </span>
          </button>
        </div>
      </section>
    </div>
  );
}
