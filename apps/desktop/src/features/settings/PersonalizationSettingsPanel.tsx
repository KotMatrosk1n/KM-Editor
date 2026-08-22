/* SPDX-License-Identifier: GPL-3.0-only */

import { useLocalization } from '../../localization';
import {
  useAppearancePreferences,
  type AppearanceTheme,
  type DensityPreference,
  type MotionPreference,
  type TypeScalePreference
} from './AppearancePreferencesProvider';
import { PerformanceDiagnosticsPanel } from './PerformanceDiagnosticsPanel';

export type PersonalizationSettingsPanelProps = {
  onReplayWhatChanged: () => void;
};

export function PersonalizationSettingsPanel({
  onReplayWhatChanged
}: PersonalizationSettingsPanelProps) {
  const { t } = useLocalization();
  const {
    preferences,
    setDensity,
    setMotion,
    setTheme,
    setTypeScale
  } = useAppearancePreferences();
  return (
    <div className="personalization-settings">
      <details aria-labelledby="appearance-settings-heading" className="km-settings-group" open>
        <summary>
          <h3 id="appearance-settings-heading">{t('settings.appearance.title')}</h3>
        </summary>
        <div className="km-settings-group-body">
          <p>{t('settings.appearance.description')}</p>
          <div className="km-settings-grid">
            <label>
              <span>{t('settings.appearance.theme')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setTheme(event.currentTarget.value as AppearanceTheme)}
                value={preferences.theme}
              >
                <option value="default">{t('settings.appearance.theme.default')}</option>
                <option value="highContrast">{t('settings.appearance.theme.highContrast')}</option>
                <option value="colorSafe">{t('settings.appearance.theme.colorSafe')}</option>
              </select>
            </label>
            <label>
              <span>{t('settings.appearance.motion')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setMotion(event.currentTarget.value as MotionPreference)}
                value={preferences.motion}
              >
                <option value="system">{t('settings.appearance.motion.system')}</option>
                <option value="reduce">{t('settings.appearance.motion.reduce')}</option>
              </select>
            </label>
            <label>
              <span>{t('settings.appearance.typeScale')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setTypeScale(event.currentTarget.value as TypeScalePreference)}
                value={preferences.typeScale}
              >
                <option value="default">{t('settings.appearance.typeScale.default')}</option>
                <option value="large">{t('settings.appearance.typeScale.large')}</option>
                <option value="larger">{t('settings.appearance.typeScale.larger')}</option>
              </select>
            </label>
            <label>
              <span>{t('settings.appearance.density')}</span>
              <select
                className="km-select-control"
                onChange={(event) => setDensity(event.currentTarget.value as DensityPreference)}
                value={preferences.density}
              >
                <option value="comfortable">
                  {t('settings.appearance.density.comfortable')}
                </option>
                <option value="compact">{t('settings.appearance.density.compact')}</option>
              </select>
            </label>
          </div>
        </div>
      </details>

      <PerformanceDiagnosticsPanel />

      <details aria-labelledby="what-changed-settings-heading" className="km-settings-group">
        <summary>
          <h3 id="what-changed-settings-heading">{t('settings.whatChanged.title')}</h3>
        </summary>
        <div className="km-settings-group-body">
          <p>{t('settings.whatChanged.description')}</p>
          <button onClick={onReplayWhatChanged} type="button">
            {t('settings.whatChanged.replay')}
          </button>
        </div>
      </details>
    </div>
  );
}
