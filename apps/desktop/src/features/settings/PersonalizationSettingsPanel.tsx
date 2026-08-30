/* SPDX-License-Identifier: GPL-3.0-only */

import { Palette } from 'lucide-react';
import classicThemeIcon from '../../assets/km-logo.png';
import renegadeThemeIcon from '../../assets/renegade-logo.png';
import royalThemeIcon from '../../assets/royal-logo.png';
import { useLocalization } from '../../localization';
import {
  useAppearancePreferences,
  type AppearanceTheme,
  type DensityPreference,
  type MotionPreference,
  type TypeScalePreference,
  type VisualTheme
} from './AppearancePreferencesProvider';
import { PerformanceDiagnosticsPanel } from './PerformanceDiagnosticsPanel';

const visualThemeOptions = ['classic', 'renegade', 'royal'] as const satisfies readonly VisualTheme[];

const visualThemeIcons: Record<VisualTheme, string> = {
  classic: classicThemeIcon,
  renegade: renegadeThemeIcon,
  royal: royalThemeIcon
};

export function ThemeSettingsPanel() {
  const { t } = useLocalization();
  const { setVisualTheme, visualTheme } = useAppearancePreferences();

  return (
    <div className="theme-settings">
      <section aria-labelledby="theme-settings-heading" className="settings-subsection">
        <div className="settings-subsection-heading">
          <Palette aria-hidden="true" size={18} />
          <div>
            <h3 id="theme-settings-heading">{t('settings.themes.title')}</h3>
            <p>{t('settings.themes.description')}</p>
          </div>
        </div>

        <div
          aria-label={t('settings.themes.groupLabel')}
          className="visual-theme-options"
          role="radiogroup"
        >
          {visualThemeOptions.map((theme) => {
            const isSelected = visualTheme === theme;
            return (
              <button
                aria-checked={isSelected}
                className={`visual-theme-option${isSelected ? ' visual-theme-option-selected' : ''}`}
                id={`settings-visual-theme-${theme}`}
                key={theme}
                onClick={() => setVisualTheme(theme)}
                onKeyDown={(event) => {
                  if (
                    !['Home', 'End', 'ArrowRight', 'ArrowLeft', 'ArrowDown', 'ArrowUp'].includes(
                      event.key
                    )
                  ) {
                    return;
                  }
                  event.preventDefault();
                  const currentIndex = visualThemeOptions.indexOf(theme);
                  const nextIndex = event.key === 'Home'
                    ? 0
                    : event.key === 'End'
                      ? visualThemeOptions.length - 1
                      : event.key === 'ArrowRight' || event.key === 'ArrowDown'
                        ? (currentIndex + 1) % visualThemeOptions.length
                        : (currentIndex - 1 + visualThemeOptions.length) %
                          visualThemeOptions.length;
                  const nextTheme = visualThemeOptions[nextIndex] ?? theme;
                  setVisualTheme(nextTheme);
                  window.requestAnimationFrame(() => {
                    document.getElementById(`settings-visual-theme-${nextTheme}`)?.focus();
                  });
                }}
                role="radio"
                tabIndex={isSelected ? 0 : -1}
                type="button"
              >
                <span
                  aria-hidden="true"
                  className={`visual-theme-option-preview visual-theme-option-preview-${theme}`}
                >
                  <span className="visual-theme-option-preview-surface" />
                  <span className="visual-theme-option-preview-accent" />
                  <span className="visual-theme-option-preview-emblem" />
                </span>
                <span className="visual-theme-option-copy">
                  <span className="visual-theme-option-heading">
                    <img
                      alt=""
                      aria-hidden="true"
                      className="visual-theme-option-icon"
                      src={visualThemeIcons[theme]}
                    />
                    <strong>{t(`settings.themes.${theme}`)}</strong>
                  </span>
                  <span>{t(`settings.themes.${theme}.description`)}</span>
                </span>
                {isSelected ? (
                  <small className="visual-theme-option-selected-label">
                    {t('settings.language.selected')}
                  </small>
                ) : null}
              </button>
            );
          })}
        </div>

        <p className="visual-theme-live-note">{t('settings.themes.liveNote')}</p>
      </section>
    </div>
  );
}

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
