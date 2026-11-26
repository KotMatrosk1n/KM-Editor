/* SPDX-License-Identifier: GPL-3.0-only */
import type { ReactNode } from 'react';
import './editorRecordTable.css';

type Column<Row> = { id: string; label: string; value: (row: Row) => ReactNode; numeric?: boolean };

export function EditorRecordTable<Row>({ label, identityLabel, rows, rowId, identity, columns, selection, onSelect, empty, disabled }: {
  label: string;
  identityLabel: string;
  rows: readonly Row[];
  rowId: (row: Row) => string;
  identity: (row: Row) => ReactNode;
  columns: readonly Column<Row>[];
  selection?: string;
  onSelect: (row: Row) => void;
  empty?: string;
  disabled?: boolean;
}) {
  return <table role="table" aria-label={label} className="editor-record-table">
    <thead><tr role="row">
      <th scope="col">{identityLabel}</th>
      {columns.map(column => <th scope="col" key={column.id} data-numeric={column.numeric}>{column.label}</th>)}
    </tr></thead>
    <tbody>
      {rows.map(row => {
        const id = rowId(row);
        return <tr key={id} data-selected={id === selection}
          onClick={event => {
            if (disabled || !(event.target instanceof Element)) return;
            if (event.target.closest('button, a, input, select, textarea, label, summary')) return;
            onSelect(row);
          }}>
          <th scope="row">
            <button className="editor-record-table-select" type="button" disabled={disabled} aria-pressed={id === selection} onClick={() => onSelect(row)}>
              {identity(row)}
            </button>
          </th>
          {columns.map(column => <td key={column.id} data-numeric={column.numeric}>{column.value(row)}</td>)}
        </tr>;
      })}
      {!rows.length && empty ? <tr><td colSpan={columns.length + 1} className="muted">{empty}</td></tr> : null}
    </tbody>
  </table>;
}
