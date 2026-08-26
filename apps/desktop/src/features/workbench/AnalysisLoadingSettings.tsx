/* SPDX-License-Identifier: GPL-3.0-only */

import { Gauge } from 'lucide-react';
import { useLocalization } from '../../localization';
import {
  analysisLoadingModes,
  type AnalysisLoadingMode
} from './analysisPreparation';

export function AnalysisLoadingSettings({
  mode,
  onChange
}: {
  mode: AnalysisLoadingMode;
  onChange: (mode: AnalysisLoadingMode) => void;
}) {
  const { t } = useLocalization();
  return (
    <section aria-labelledby="analysis-loading-settings-heading" className="settings-subsection">
      <div className="settings-subsection-heading">
        <Gauge aria-hidden="true" size={18} />
        <div>
          <h3 id="analysis-loading-settings-heading">{t('analysisLoading.title')}</h3>
          <p>{t('analysisLoading.description')}</p>
        </div>
      </div>
      <div
        aria-label={t('analysisLoading.groupLabel')}
        className="analysis-loading-options"
        role="radiogroup"
      >
        {analysisLoadingModes.map((option) => {
          const isSelected = option === mode;
          return (
            <button
              aria-checked={isSelected}
              className={`analysis-loading-option${isSelected ? ' is-selected' : ''}`}
              disabled={isSelected}
              key={option}
              onClick={() => onChange(option)}
              role="radio"
              type="button"
            >
              <span>
                <strong>{t(`analysisLoading.mode.${option}.label`)}</strong>
                {option === 'balanced' ? <small>{t('analysisLoading.recommended')}</small> : null}
              </span>
              <p>{t(`analysisLoading.mode.${option}.description`)}</p>
            </button>
          );
        })}
      </div>
    </section>
  );
}
