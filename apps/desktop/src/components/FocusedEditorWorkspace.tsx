/* SPDX-License-Identifier: GPL-3.0-only */

import type { HTMLAttributes } from 'react';

type DivProps = HTMLAttributes<HTMLDivElement>;

function joinClassNames(requiredClassName: string, className?: string) {
  return className ? `${requiredClassName} ${className}` : requiredClassName;
}

/**
 * Full-width root for focused editors.
 *
 * The workspace shell is a multi-column grid at wider resolutions. Focused editor
 * roots must span that grid and use their own inline-size container so their
 * internal layout follows the space actually available after DPI scaling,
 * sidebars, and inspectors.
 */
export function FocusedEditorWorkspace({ className, ...props }: DivProps) {
  return (
    <div
      className={joinClassNames('focused-editor-workspace', className)}
      {...props}
    />
  );
}

/** Responsive summary metrics that fill the editor before wrapping. */
export function FocusedEditorMetrics({ className, ...props }: DivProps) {
  return (
    <div
      className={joinClassNames('focused-editor-metrics', className)}
      {...props}
    />
  );
}
