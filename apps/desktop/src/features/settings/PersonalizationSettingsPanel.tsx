/* SPDX-License-Identifier: GPL-3.0-only */

import { Palette } from 'lucide-react';
import classicThemeIcon from '../../assets/km-logo.png';
import renegadeThemeIcon from '../../assets/renegade-logo.png';
import royalThemeIcon from '../../assets/royal-logo.png';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
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
            <div className="km-searchable-select-field km-settings-select-field">
              <label htmlFor="personalization-appearance-theme">
                {t('settings.appearance.theme')}
              </label>
              <SearchableOptionInput
                ariaLabel={t('settings.appearance.theme')}
                data-km-source-site="personalization-appearance-theme"
                disabled={false}
                id="personalization-appearance-theme"
                isFiniteCatalog
                localizeOptions={false}
                onChange={(value) => setTheme(value as AppearanceTheme)}
                options={[
                  { label: t('settings.appearance.theme.default'), value: 'default' },
                  { label: t('settings.appearance.theme.highContrast'), value: 'highContrast' },
                  { label: t('settings.appearance.theme.colorSafe'), value: 'colorSafe' }
                ]}
                value={preferences.theme}
              />
            </div>
            <div className="km-searchable-select-field km-settings-select-field">
              <label htmlFor="personalization-motion">
                {t('settings.appearance.motion')}
              </label>
              <SearchableOptionInput
                ariaLabel={t('settings.appearance.motion')}
                data-km-source-site="personalization-motion"
                disabled={false}
                id="personalization-motion"
                isFiniteCatalog
                localizeOptions={false}
                onChange={(value) => setMotion(value as MotionPreference)}
                options={[
                  { label: t('settings.appearance.motion.system'), value: 'system' },
                  { label: t('settings.appearance.motion.reduce'), value: 'reduce' }
                ]}
                value={preferences.motion}
              />
            </div>
            <div className="km-searchable-select-field km-settings-select-field">
              <label htmlFor="personalization-type-scale">
                {t('settings.appearance.typeScale')}
              </label>
              <SearchableOptionInput
                ariaLabel={t('settings.appearance.typeScale')}
                data-km-source-site="personalization-type-scale"
                disabled={false}
                id="personalization-type-scale"
                isFiniteCatalog
                localizeOptions={false}
                onChange={(value) => setTypeScale(value as TypeScalePreference)}
                options={[
                  { label: t('settings.appearance.typeScale.default'), value: 'default' },
                  { label: t('settings.appearance.typeScale.large'), value: 'large' },
                  { label: t('settings.appearance.typeScale.larger'), value: 'larger' }
                ]}
                value={preferences.typeScale}
              />
            </div>
            <div className="km-searchable-select-field km-settings-select-field">
              <label htmlFor="personalization-density">
                {t('settings.appearance.density')}
              </label>
              <SearchableOptionInput
                ariaLabel={t('settings.appearance.density')}
                data-km-source-site="personalization-density"
                disabled={false}
                id="personalization-density"
                isFiniteCatalog
                localizeOptions={false}
                onChange={(value) => setDensity(value as DensityPreference)}
                options={[
                  {
                    label: t('settings.appearance.density.comfortable'),
                    value: 'comfortable'
                  },
                  { label: t('settings.appearance.density.compact'), value: 'compact' }
                ]}
                value={preferences.density}
              />
            </div>
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
