/* SPDX-License-Identifier: GPL-3.0-only */

import type { PokemonCompatibilityGroup } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import { compatibilityBulkValues } from './compatibilityDrafts';
import './CompatibilityActions.css';

export function CompatibilityActions({ group, disabled, onChange }: {
  group: PokemonCompatibilityGroup | null;
  disabled: boolean;
  onChange: (values: Record<string, string>) => void;
}) {
  const { t } = useLocalization();
  const vanillaUnavailable = !group || compatibilityBulkValues(group, 'vanilla') === null;
  return <div className="compatibility-actions" role="group" aria-label={t('pokemon.compatibility.actions')}>
    {(['enable', 'disable', 'vanilla'] as const).map(action => <button type="button" key={action}
      className={action === 'enable' ? 'primary-button' : action === 'disable' ? 'danger-button' : 'secondary-button'}
      disabled={disabled || !group || (action === 'vanilla' && vanillaUnavailable)}
      title={action === 'vanilla' && vanillaUnavailable ? t('pokemon.compatibility.vanillaUnavailable') : undefined}
      onClick={() => {
        if (disabled || !group) return;
        const values = compatibilityBulkValues(group, action);
        if (values) onChange(values);
      }}>{t(`pokemon.compatibility.${action}`)}</button>)}
  </div>;
}
