/* SPDX-License-Identifier: GPL-3.0-only */

import { convertFileSrc, invoke, isTauri } from '@tauri-apps/api/core';
import { Image } from 'lucide-react';
import { useEffect, useSyncExternalStore } from 'react';
import { useLocalization } from '../../localization';

const listeners = new Set<() => void>();
type ArtworkStatus = 'idle' | 'checking' | 'current' | 'updated' | 'failed';
let snapshot: { revision: number; status: ArtworkStatus } = { revision: 0, status: 'idle' };
let operation: Promise<void> | undefined;

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

function getSnapshot() {
  return snapshot;
}

function getRevision() {
  return snapshot.revision;
}

function publish(status: ArtworkStatus, refreshImages = false) {
  snapshot = { revision: snapshot.revision + Number(refreshImages), status };
  listeners.forEach((listener) => listener());
}

function synchronize(onlyAfterUpdate: boolean) {
  if (!usesArtworkCache || operation) return;
  publish('checking');
  operation = invoke<{ downloaded: number }>('sync_artwork', { onlyAfterUpdate })
    .then(({ downloaded }) => publish(downloaded ? 'updated' : 'current', downloaded > 0))
    .catch(() => publish('failed', true))
    .finally(() => { operation = undefined; });
}

export const usesArtworkCache = !import.meta.env.DEV && isTauri();

export function useInitialArtworkSync() {
  useEffect(() => { synchronize(true); }, []);
}

export function useArtworkRevision() {
  return useSyncExternalStore(subscribe, getRevision);
}

export function artworkUrl(path: string, revision: number) {
  if (usesArtworkCache) {
    const prefix = convertFileSrc('', 'km-artwork');
    return prefix + path.split('/').map(encodeURIComponent).join('/') + '?revision=' + revision;
  }
  return import.meta.env.BASE_URL + 'sprites/' + path;
}

export function ArtworkSettingsPanel() {
  const { t } = useLocalization();
  const { status } = useSyncExternalStore(subscribe, getSnapshot);
  if (!usesArtworkCache) return null;

  return (
    <section aria-labelledby="artwork-settings-heading" className="settings-subsection">
      <div className="settings-subsection-heading">
        <Image aria-hidden="true" size={18} />
        <div>
          <h3 id="artwork-settings-heading">{t('settings.artwork.title')}</h3>
          <p>{t('settings.artwork.description')}</p>
        </div>
      </div>
      <button className="secondary-button" type="button" disabled={status === 'checking'} onClick={() => synchronize(false)}>
        {t('settings.artwork.check')}
      </button>
      <p role="status">{status === 'idle' ? '' : t('settings.artwork.' + status)}</p>
    </section>
  );
}
