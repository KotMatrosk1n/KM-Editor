/* SPDX-License-Identifier: GPL-3.0-only */

import type { ReactNode } from 'react';
import { useLocalization } from '../localization';

export function AdvancedEditorDetails({ children, label = 'advancedEditor.technicalDetails' }: {
  children: ReactNode;
  label?: string;
}) {
  const { t } = useLocalization();
  return (
    <details className="advanced-editor-details">
      <summary>{t(label)}</summary>
      <div className="advanced-editor-details-content">{children}</div>
    </details>
  );
}
