/* SPDX-License-Identifier: GPL-3.0-only */

export {
  LocalizationProvider,
  languageStorageKey,
  supportedLanguages,
  translateKeyForInterfaceLocale,
  translateKeyForLanguage,
  translateLiteralForInterfaceLocale,
  translateLiteralForLanguage,
  useLocalization,
  type AvailableInterfaceLanguage,
  type CommunityLocalePackUpdateOptions,
  type CommunityLocalePack,
  type InterfaceLocale,
  type LanguageCode,
  type LocalizationContextValue
} from './LocalizationProvider';

export {
  LocalePackValidationError,
  communityLocalePackSchemaVersion,
  isBuiltInLanguageCode,
  isCommunityLocaleId,
  maximumCommunityLocalePackBytes,
  maximumCommunityLocalePacks,
  parseCommunityLocalePackBytes,
  parseCommunityLocalePackText,
  toCommunityLocaleId,
  validateCommunityLocalePack,
  type LocalePackValidationFailureCode
} from './localePackContracts';
