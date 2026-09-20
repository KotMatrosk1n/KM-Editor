/* SPDX-License-Identifier: GPL-3.0-only */

import type { PokemonEditableField, PokemonRecord } from '../../bridge/contracts';
import { useLocalization } from '../../localization/LocalizationProvider';
import './alphaSize.css';

export const alphaSizePrefix = 'alphaSize:';
export const alphaSizeNumber = (value: number) => Number(value.toPrecision(9));

export function alphaSizeManualDraft(draft: string) {
  const value = Number(draft);
  if (!Number.isFinite(Math.fround(value)) || Math.fround(value) <= 0
    || !/\.\d{3}|e/i.test(draft)) return draft;
  const [mantissa, exponent = '0'] = value.toString().split('e');
  const hundredths = Number(`${mantissa}e${Number(exponent) + 2}`);
  return (Math.round(hundredths) / 100).toString();
}

export function alphaSizeFields(pokemon: PokemonRecord | null): PokemonEditableField[] {
  return (pokemon?.alphaSizes ?? []).map(size => ({
    field: size.field, group: 'Alpha Size', label: size.genders.length > 1 ? 'Alpha scale (×)'
      : size.genders[0] === 1 ? 'Female alpha scale (×)' : 'Alpha scale (×)',
    valueKind: size.minimumScale === size.scale ? 'decimal' : 'alphaSizeRange',
    minimumValue: null, maximumValue: null, options: []
  }));
}

export function alphaSizeDraftState(draft: string, current: number) {
  const value = Number(draft.trim());
  const storedValue = Math.fround(value);
  const valid = draft.trim().length > 0 && Number.isFinite(storedValue) && storedValue > 0;
  return { error: valid ? null : 'Alpha scale must be a positive finite number.',
    isChanged: value !== current, isValid: valid, normalizedValue: valid ? value.toString() : null };
}

export function AlphaSizeHelp() {
  const { t } = useLocalization();
  return <p className="field-help">{t('pokemon.alphaSize.help')}</p>;
}

export function AlphaSizeDetails({ field, pokemon, records, draft, disabled, onRestore }: {
  field: string; pokemon: PokemonRecord; records: PokemonRecord[]; draft: string;
  disabled: boolean; onRestore: (value: string) => void;
}) {
  const { t } = useLocalization();
  const size = pokemon.alphaSizes?.find(value => value.field === field);
  if (!size) return null;
  const vanilla = alphaSizeNumber(size.vanillaScale);
  const shared = records.filter(record => size.sharedPersonalIds.includes(record.personalId)
    && record.personalId !== pokemon.personalId);
  return <div className="alpha-size-details">
    <small>{t('pokemon.alphaSize.ordinary', { value: alphaSizeNumber(size.ordinaryScale) })}</small>
    <small>{t('pokemon.alphaSize.default', { value: vanilla })}</small>
    {size.minimumScale !== size.scale ? <small>{t('pokemon.alphaSize.range', {
      minimum: alphaSizeNumber(size.minimumScale), maximum: alphaSizeNumber(size.scale)
    })}</small> : null}
    {shared.length > 0 ? <small>{t('pokemon.alphaSize.shared')}{' '}
      <span data-localization-ignore="true">{shared.map(record => `${record.name} (${record.formLabel})`).join(', ')}</span>
    </small> : null}
    <button className="secondary-button" type="button"
      disabled={disabled || (Number(draft) === vanilla && size.minimumScale === size.scale)}
      onClick={() => onRestore(size.minimumScale === size.scale ? vanilla.toString() : vanilla.toExponential(9))}>
      {t('pokemon.alphaSize.restore')}</button>
  </div>;
}
