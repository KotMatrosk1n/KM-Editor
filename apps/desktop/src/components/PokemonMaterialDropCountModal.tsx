/* SPDX-License-Identifier: GPL-3.0-only */
import { useState } from 'react';
import { ListChecks, Package, X } from 'lucide-react';
import { useLocalization } from '../localization';
import { useModalDialog } from './useModalDialog';

export function PokemonMaterialDropCountModal({
  minimum,
  maximum,
  onCancel,
  onStage
}: {
  minimum: number;
  maximum: number;
  onCancel: () => void;
  onStage: (count: number) => void;
}) {
  const { t, translateLiteral } = useLocalization();
  const dialogRef = useModalDialog<HTMLFormElement>({ onClose: onCancel });
  const [value, setValue] = useState('');
  const count = Number(value);
  const isValid = /^\d+$/.test(value) && count >= minimum && count <= maximum;

  return (
    <div className="modal-backdrop" role="presentation">
      <form
        aria-describedby="material-drop-count-description"
        aria-labelledby="material-drop-count-heading"
        aria-modal="true"
        className="modal-panel"
        onSubmit={(event) => {
          event.preventDefault();
          if (isValid) onStage(count);
        }}
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading">
          <Package aria-hidden="true" size={18} />
          <h2 id="material-drop-count-heading">{t('pokemon.materialDrops.bulkTitle')}</h2>
        </div>
        <p className="modal-copy" id="material-drop-count-description">
          {t('pokemon.materialDrops.bulkDescription')}
        </p>
        <label className="path-field">
          <span>{t('pokemon.materialDrops.bulkRange', { minimum, maximum })}</span>
          <input
            max={maximum}
            min={minimum}
            onChange={(event) => setValue(event.currentTarget.value)}
            required
            step={1}
            type="number"
            value={value}
          />
        </label>
        <div className="modal-actions">
          <button className="secondary-button" onClick={onCancel} type="button">
            <X aria-hidden="true" size={16} />
            <span>{translateLiteral('Cancel')}</span>
          </button>
          <button className="primary-button" disabled={!isValid} type="submit">
            <ListChecks aria-hidden="true" size={16} />
            <span>{translateLiteral('Stage Changes')}</span>
          </button>
        </div>
      </form>
    </div>
  );
}
