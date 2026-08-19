/* SPDX-License-Identifier: GPL-3.0-only */

import {
  maximumCommunityLocalePacks,
  toCommunityLocaleId,
  validateCommunityLocalePack,
  type BuiltInLanguageCode,
  type CommunityLocalePack,
  type InterfaceLocale
} from './localePackContracts';

export type AvailableInterfaceLanguage = {
  code: InterfaceLocale;
  displayName: string;
  flag?: string;
  formatLocale: string;
  gameTextLanguage: BuiltInLanguageCode;
  source: 'builtIn' | 'community';
};

export function createCommunityLocaleRegistry(values: readonly unknown[]) {
  if (values.length > maximumCommunityLocalePacks) {
    throw new Error('At most four community locale packs can be active.');
  }

  const packs = values.map((value) => validateCommunityLocalePack(value));
  const ids = new Set<string>();
  const localeTags = new Set<string>();
  for (const pack of packs) {
    if (ids.has(pack.id) || localeTags.has(pack.localeTag)) {
      throw new Error('Community locale pack identifiers and locale tags must be unique.');
    }
    ids.add(pack.id);
    localeTags.add(pack.localeTag);
  }
  return packs;
}

export function findCommunityLocalePack(
  packs: readonly CommunityLocalePack[],
  locale: InterfaceLocale
) {
  if (!locale.startsWith('community:')) {
    return undefined;
  }
  const id = locale.slice('community:'.length);
  return packs.find((pack) => pack.id === id);
}

export function toAvailableCommunityLanguage(
  pack: CommunityLocalePack
): AvailableInterfaceLanguage {
  return {
    code: toCommunityLocaleId(pack.id),
    displayName: pack.displayName,
    formatLocale: pack.localeTag,
    gameTextLanguage: pack.gameTextLanguage,
    source: 'community'
  };
}
