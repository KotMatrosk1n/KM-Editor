/* SPDX-License-Identifier: GPL-3.0-only */

import { FlaskConical } from 'lucide-react';
import { useSyncExternalStore } from 'react';
import { useLocalization } from '../../localization';

export const betaEditorsStorageKey = 'km-editor.beta-editors.enabled';
const preferenceEvent = 'km-editor:beta-editors-changed';
let fallbackPreference = false;
let useSessionPreference = false;

function readPreference() {
  if (useSessionPreference) return fallbackPreference;
  try {
    return localStorage.getItem(betaEditorsStorageKey) === 'true';
  } catch {
    return fallbackPreference;
  }
}

function subscribe(listener: () => void) {
  window.addEventListener('storage', listener);
  window.addEventListener(preferenceEvent, listener);
  return () => {
    window.removeEventListener('storage', listener);
    window.removeEventListener(preferenceEvent, listener);
  };
}

export function useBetaEditorsEnabled() {
  return useSyncExternalStore(subscribe, readPreference, () => false);
}

function setPreference(enabled: boolean) {
  fallbackPreference = enabled;
  try {
    localStorage.setItem(betaEditorsStorageKey, String(enabled));
    useSessionPreference = false;
  } catch {
    useSessionPreference = true;
    // Keep the choice for this session when preference storage is unavailable.
  }
  window.dispatchEvent(new Event(preferenceEvent));
}

export function BetaEditorsSettings() {
  const enabled = useBetaEditorsEnabled();
  const { t } = useLocalization();
  return (
    <section className="km-settings-group" aria-labelledby="beta-editors-heading">
      <div className="panel-heading">
        <FlaskConical aria-hidden="true" size={18} />
        <h3 id="beta-editors-heading">{t('settings.tabs.betaEditors')}</h3>
      </div>
      <p className="beta-editors-warning" id="beta-editors-warning" role="note">
        {t('settings.betaEditors.warning')}
      </p>
      <label className="checkbox-field">
        <input aria-describedby="beta-editors-warning" checked={enabled}
          className="km-choice-control" onChange={event => setPreference(event.target.checked)} type="checkbox" />
        <span>{t('settings.betaEditors.show')}</span>
      </label>
      <p className="field-note">{t('settings.betaEditors.persistence')}</p>
    </section>
  );
}
