/* SPDX-License-Identifier: GPL-3.0-only */

import { type ReactNode } from 'react';
import { ContextHelp } from './ContextHelp';

export type FieldLabelProps = {
  adornment?: ReactNode;
  help?: ReactNode;
  htmlFor: string;
  label: string;
};

export function FieldLabel({ adornment, help, htmlFor, label }: FieldLabelProps) {
  return (
    <span className="editable-field-label-row">
      <label htmlFor={htmlFor}>{label}</label>
      {help ? <ContextHelp label={label}>{help}</ContextHelp> : null}
      {adornment}
    </span>
  );
}
