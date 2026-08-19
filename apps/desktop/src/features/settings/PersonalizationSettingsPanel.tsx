/* SPDX-License-Identifier: GPL-3.0-only */

import { useState, type ChangeEvent } from 'react';
import {
  LocalePackValidationError,
  maximumCommunityLocalePackBytes,
  maximumCommunityLocalePacks,
  parseCommunityLocalePackBytes,
  type CommunityLocalePack,
  type InterfaceLocale,
  type LocalePackValidationFailureCode
} from '../../localization/localePackContracts';
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
  communityLocalePacks: readonly CommunityLocalePack[];
  hasIgnoredPersistedLocalePacks?: boolean;
  isLocalePackBusy?: boolean;
  onInstallLocalePack: (pack: CommunityLocalePack) => void | Promise<void>;
  onRemoveLocalePack: (packId: string) => void | Promise<void>;
  onReplayWhatChanged: () => void;
};

type LocaleManagerStatus =
  | { kind: 'idle' }
  | { kind: 'installed'; name: string }
  | { kind: 'removed'; name: string }
  | { code?: LocalePackValidationFailureCode | 'limit'; kind: 'error' };

export function PersonalizationSettingsPanel({
  communityLocalePacks,
  hasIgnoredPersistedLocalePacks = false,
  isLocalePackBusy = false,
  onInstallLocalePack,
  onRemoveLocalePack,
  onReplayWhatChanged
}: PersonalizationSettingsPanelProps) {
  const {
    availableLanguages,
    interfaceLocale,
    setLanguage,
    t
  } = useLocalization();
  const {
    preferences,
    setDensity,
    setMotion,
    setTheme,
    setTypeScale
  } = useAppearancePreferences();
  const [localeStatus, setLocaleStatus] = useState<LocaleManagerStatus>({ kind: 'idle' });

  const installLocalePack = async (event: ChangeEvent<HTMLInputElement>) => {
    const input = event.currentTarget;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    if (file.size > maximumCommunityLocalePackBytes) {
      setLocaleStatus({ code: 'fileTooLarge', kind: 'error' });
      return;
    }
    try {
      const pack = parseCommunityLocalePackBytes(await file.arrayBuffer());
      const replacesExisting = communityLocalePacks.some(
        (installed) => installed.id === pack.id || installed.localeTag === pack.localeTag
      );
      if (!replacesExisting && communityLocalePacks.length >= maximumCommunityLocalePacks) {
        setLocaleStatus({ code: 'limit', kind: 'error' });
        return;
      }
      await onInstallLocalePack(pack);
      setLocaleStatus({ kind: 'installed', name: pack.displayName });
    } catch (error) {
      setLocaleStatus({
        ...(error instanceof LocalePackValidationError ? { code: error.code } : {}),
        kind: 'error'
      });
    }
  };

  const removeLocalePack = async (pack: CommunityLocalePack) => {
    try {
      const removedActiveLocale = interfaceLocale === `community:${pack.id}`;
      await onRemoveLocalePack(pack.id);
      if (removedActiveLocale) {
        setLanguage('en');
      }
      setLocaleStatus({ kind: 'removed', name: pack.displayName });
    } catch {
      setLocaleStatus({ kind: 'error' });
    }
  };

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

      {hasIgnoredPersistedLocalePacks ? (
        <p className="km-settings-warning" role="status">
          {t('settings.localePacks.persistedIgnored')}
        </p>
      ) : null}

      <details aria-labelledby="interface-language-heading" className="km-settings-group">
        <summary>
          <h3 id="interface-language-heading">{t('settings.localePacks.title')}</h3>
        </summary>
        <div className="km-settings-group-body">
          <p>{t('settings.localePacks.description')}</p>
          <label className="km-settings-field">
            <span>{t('settings.localePacks.interfaceLanguage')}</span>
            <select
              disabled={isLocalePackBusy}
              onChange={(event) => setLanguage(event.currentTarget.value as InterfaceLocale)}
              value={interfaceLocale}
            >
              {availableLanguages.map((locale) => (
                <option key={locale.code} value={locale.code}>
                  {locale.flag ? `${locale.flag} ` : ''}
                  {locale.displayName}
                </option>
              ))}
            </select>
          </label>
          <label className="km-file-input">
            <span>{t('settings.localePacks.install')}</span>
            <input
              accept="application/json,.json"
              disabled={isLocalePackBusy}
              onChange={(event) => void installLocalePack(event)}
              type="file"
            />
          </label>
          <p className="km-settings-note">
            {t('settings.localePacks.limit', { count: maximumCommunityLocalePacks })}
          </p>
          {communityLocalePacks.length > 0 ? (
            <ul className="locale-pack-list">
              {communityLocalePacks.map((pack) => (
                <li key={pack.id}>
                  <span data-localization-ignore="true">
                    <strong>{pack.displayName}</strong> <small>{pack.localeTag}</small>
                  </span>
                  <button
                    disabled={isLocalePackBusy}
                    onClick={() => void removeLocalePack(pack)}
                    type="button"
                  >
                    {t('settings.localePacks.remove')}
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p>{t('settings.localePacks.noneInstalled')}</p>
          )}
          <p aria-live="polite" className="km-settings-status">
            {formatLocaleManagerStatus(localeStatus, t)}
          </p>
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

function formatLocaleManagerStatus(
  status: LocaleManagerStatus,
  t: (key: string, params?: Record<string, string | number>) => string
) {
  if (status.kind === 'installed') {
    return t('settings.localePacks.installed', { name: status.name });
  }
  if (status.kind === 'removed') {
    return t('settings.localePacks.removed', { name: status.name });
  }
  if (status.kind === 'error') {
    return t(
      status.code
        ? `settings.localePacks.error.${status.code}`
        : 'settings.localePacks.error.generic'
    );
  }
  return '';
}
