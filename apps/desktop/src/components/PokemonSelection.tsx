/* SPDX-License-Identifier: GPL-3.0-only */
import { Search } from 'lucide-react';
import { useLocalization } from '../localization';
import { InteractiveTableRow, VirtualTableBody } from './VirtualTable';

export type PokemonSelectionRecord = {
  personalId: number;
  speciesId: number;
  form: number;
  name: string;
  formLabel: string;
  type1: string;
  type2: string;
};

export function formatPokemonTypes(pokemon: Pick<PokemonSelectionRecord, 'type1' | 'type2'>) {
  return pokemon.type1 === pokemon.type2 ? pokemon.type1 : `${pokemon.type1} / ${pokemon.type2}`;
}

export function filterPokemonRecords<T extends PokemonSelectionRecord>(
  pokemon: T[], searchText: string, formatName: (record: T) => string
) {
  const query = searchText.trim().toLocaleLowerCase();
  if (!query) return pokemon;
  return pokemon.filter(record => [
    String(record.personalId), String(record.speciesId), record.formLabel,
    formatName(record), record.name, record.type1, record.type2, formatPokemonTypes(record)
  ].some(value => value.toLocaleLowerCase().includes(query)));
}

export function PokemonSearchBox({ disabled, value, onChange }: {
  disabled: boolean; value: string; onChange: (value: string) => void;
}) {
  const { translateLiteral } = useLocalization();
  return <label className="search-box items-search">
    <Search aria-hidden="true" size={18} />
    <input aria-label={translateLiteral('Search Pokemon')} placeholder={translateLiteral('Search Pokemon')}
      disabled={disabled} onChange={event => onChange(event.target.value)} type="search" value={value} />
  </label>;
}

export function PokemonTable<T extends PokemonSelectionRecord>({ records, selectedId, pendingIds, onSelect, formatName, resetKey }: {
  records: T[]; selectedId: number | undefined; pendingIds: ReadonlySet<number>;
  onSelect: (personalId: number) => void; formatName: (record: T) => string; resetKey?: string;
}) {
  const { translateLiteral } = useLocalization();
  return <div aria-colcount={3} aria-label={translateLiteral('Pokemon')} aria-rowcount={records.length + 1}
    className="items-table pokemon-table" role="table">
    <div className="items-row items-row-heading" role="row">
      <span role="columnheader">{translateLiteral('ID')}</span>
      <span role="columnheader">{translateLiteral('Name')}</span>
      <span role="columnheader">{translateLiteral('Types')}</span>
    </div>
    <VirtualTableBody getKey={record => record.personalId} items={records} resetKey={resetKey}
      renderRow={record => <InteractiveTableRow
        className={`items-row ${selectedId === record.personalId ? 'items-row-selected' : ''} ${pendingIds.has(record.personalId) ? 'moves-row-pending' : ''}`}
        onClick={() => onSelect(record.personalId)}>
        <span role="cell">{record.personalId}</span>
        <span data-localization-ignore="true" role="cell">{formatName(record)}</span>
        <span data-localization-ignore="true" role="cell">{formatPokemonTypes(record)}</span>
      </InteractiveTableRow>} />
  </div>;
}
