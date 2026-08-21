/* SPDX-License-Identifier: GPL-3.0-only */

import enResource from './resources/en.json';

export const communityLocalePackSchemaVersion = 1 as const;
export const expectedCommunityLocalePackBytes = 512 * 1024;
export const provisionedCommunityLocalePackBytes = expectedCommunityLocalePackBytes * 4;
export const maximumCommunityLocalePackBytes = provisionedCommunityLocalePackBytes * 2;
export const maximumCommunityLocalePacks = 4;
export const maximumCommunityLocaleTranslationLength = 8192;

export type BuiltInLanguageCode = 'en' | 'es' | 'fr' | 'de' | 'ru' | 'uk' | 'zh';
export type CommunityLocaleId = `community:${string}`;
export type InterfaceLocale = BuiltInLanguageCode | CommunityLocaleId;

export type LocalizationResource = {
  keys: Record<string, string>;
  literals: Record<string, string>;
};

export type CommunityLocalePack = LocalizationResource & {
  direction: 'ltr';
  displayName: string;
  gameTextLanguage: BuiltInLanguageCode;
  id: string;
  localeTag: string;
  schemaVersion: typeof communityLocalePackSchemaVersion;
};

export type LocalePackValidationFailureCode =
  | 'fileTooLarge'
  | 'invalidJson'
  | 'invalidShape'
  | 'invalidMetadata'
  | 'unsupportedDirection'
  | 'missingEntries'
  | 'extraEntries'
  | 'invalidText'
  | 'placeholderMismatch';

export class LocalePackValidationError extends Error {
  readonly code: LocalePackValidationFailureCode;

  constructor(code: LocalePackValidationFailureCode) {
    super(code);
    this.name = 'LocalePackValidationError';
    this.code = code;
  }
}

const builtInLanguageCodes = new Set<BuiltInLanguageCode>([
  'en',
  'es',
  'fr',
  'de',
  'ru',
  'uk',
  'zh'
]);
const packIdPattern = /^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$/u;
const disallowedControls = /[\u0000-\u001f\u007f-\u009f]/u;
const disallowedDirectionAndZeroWidthCharacters =
  /[\u061c\u200b-\u200f\u202a-\u202e\u2060\u2066-\u2069\ufeff]/u;
const markupPattern = /<\/?[A-Za-z][^>]*>/u;
const reservedPlaceholderMarker = /KMPLACEHOLDER/u;
const placeholderPattern = /\{([A-Za-z0-9_]+)\}/gu;
const rightToLeftScripts = new Set([
  'Adlm',
  'Arab',
  'Armi',
  'Avst',
  'Chrs',
  'Elym',
  'Hatr',
  'Hebr',
  'Khar',
  'Lydi',
  'Mani',
  'Mand',
  'Mend',
  'Merc',
  'Mero',
  'Narb',
  'Nbat',
  'Nkoo',
  'Orkh',
  'Palm',
  'Phli',
  'Phlp',
  'Phnx',
  'Prti',
  'Rohg',
  'Samr',
  'Sarb',
  'Sogd',
  'Sogo',
  'Syrc',
  'Thaa'
]);
const rootFields = new Set([
  'schemaVersion',
  'id',
  'displayName',
  'localeTag',
  'direction',
  'gameTextLanguage',
  'keys',
  'literals'
]);
const englishResource: LocalizationResource = enResource;

export function toCommunityLocaleId(id: string): CommunityLocaleId {
  return `community:${id}`;
}

export function isCommunityLocaleId(value: string): value is CommunityLocaleId {
  return value.startsWith('community:') && packIdPattern.test(value.slice('community:'.length));
}

export function isBuiltInLanguageCode(value: unknown): value is BuiltInLanguageCode {
  return typeof value === 'string' && builtInLanguageCodes.has(value as BuiltInLanguageCode);
}

export function parseCommunityLocalePackText(text: string): CommunityLocalePack {
  if (new TextEncoder().encode(text).byteLength > maximumCommunityLocalePackBytes) {
    throw new LocalePackValidationError('fileTooLarge');
  }
  if (text.charCodeAt(0) === 0xfeff) {
    throw new LocalePackValidationError('invalidText');
  }

  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    throw new LocalePackValidationError('invalidJson');
  }

  return validateCommunityLocalePack(value, false);
}

export function parseCommunityLocalePackBytes(bytes: ArrayBuffer): CommunityLocalePack {
  if (bytes.byteLength > maximumCommunityLocalePackBytes) {
    throw new LocalePackValidationError('fileTooLarge');
  }

  const view = new Uint8Array(bytes);
  if (view.length >= 3 && view[0] === 0xef && view[1] === 0xbb && view[2] === 0xbf) {
    throw new LocalePackValidationError('invalidText');
  }

  let text: string;
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(view);
  } catch {
    throw new LocalePackValidationError('invalidText');
  }
  return parseCommunityLocalePackText(text);
}

export function validateCommunityLocalePack(
  value: unknown,
  enforceSerializedSize = true
): CommunityLocalePack {
  if (!isRecord(value)) {
    throw new LocalePackValidationError('invalidShape');
  }
  if (
    Object.keys(value).length !== rootFields.size ||
    Object.keys(value).some((field) => !rootFields.has(field))
  ) {
    throw new LocalePackValidationError('invalidShape');
  }
  if (enforceSerializedSize) {
    let serialized: string;
    try {
      serialized = JSON.stringify(value);
    } catch {
      throw new LocalePackValidationError('invalidShape');
    }
    if (new TextEncoder().encode(serialized).byteLength > maximumCommunityLocalePackBytes) {
      throw new LocalePackValidationError('fileTooLarge');
    }
  }

  if (
    value.schemaVersion !== communityLocalePackSchemaVersion ||
    typeof value.id !== 'string' ||
    !packIdPattern.test(value.id) ||
    typeof value.displayName !== 'string' ||
    value.displayName.length < 1 ||
    value.displayName.length > 64 ||
    value.displayName.trim() !== value.displayName ||
    typeof value.localeTag !== 'string' ||
    value.localeTag.length < 2 ||
    value.localeTag.length > 64 ||
    !isBuiltInLanguageCode(value.gameTextLanguage)
  ) {
    throw new LocalePackValidationError('invalidMetadata');
  }
  validateText(value.displayName);
  validateText(value.localeTag);
  if (canonicalizeLocaleTag(value.localeTag) !== value.localeTag) {
    throw new LocalePackValidationError('invalidMetadata');
  }
  if (value.direction !== 'ltr' || !isLeftToRightLocale(value.localeTag)) {
    throw new LocalePackValidationError('unsupportedDirection');
  }

  const keys = validateTranslationMap(value.keys, englishResource.keys);
  const literals = validateTranslationMap(value.literals, englishResource.literals);
  return {
    direction: 'ltr',
    displayName: value.displayName,
    gameTextLanguage: value.gameTextLanguage,
    id: value.id,
    keys,
    literals,
    localeTag: value.localeTag,
    schemaVersion: communityLocalePackSchemaVersion
  };
}

function validateTranslationMap(value: unknown, english: Record<string, string>) {
  if (!isRecord(value)) {
    throw new LocalePackValidationError('invalidShape');
  }
  const expectedEntries = Object.keys(english);
  const receivedEntries = Object.keys(value);
  if (expectedEntries.some((entry) => !Object.hasOwn(value, entry))) {
    throw new LocalePackValidationError('missingEntries');
  }
  if (receivedEntries.some((entry) => !Object.hasOwn(english, entry))) {
    throw new LocalePackValidationError('extraEntries');
  }

  const validated: Record<string, string> = {};
  for (const entry of expectedEntries) {
    const translated = value[entry];
    if (typeof translated !== 'string') {
      throw new LocalePackValidationError('invalidText');
    }
    validateText(translated);
    if (!placeholderMultisetsEqual(english[entry], translated)) {
      throw new LocalePackValidationError('placeholderMismatch');
    }
    validated[entry] = translated;
  }
  return validated;
}

function validateText(value: string) {
  if (
    value.trim().length === 0 ||
    value.length > maximumCommunityLocaleTranslationLength ||
    value.normalize('NFC') !== value ||
    disallowedControls.test(value) ||
    disallowedDirectionAndZeroWidthCharacters.test(value) ||
    markupPattern.test(value) ||
    reservedPlaceholderMarker.test(value)
  ) {
    throw new LocalePackValidationError('invalidText');
  }
}

function placeholderMultisetsEqual(left: string, right: string) {
  const leftPlaceholders = extractPlaceholderMultiset(left);
  const rightPlaceholders = extractPlaceholderMultiset(right);
  if (leftPlaceholders.size !== rightPlaceholders.size) {
    return false;
  }
  for (const [placeholder, count] of leftPlaceholders) {
    if (rightPlaceholders.get(placeholder) !== count) {
      return false;
    }
  }
  return true;
}

function extractPlaceholderMultiset(value: string) {
  const placeholders = new Map<string, number>();
  for (const match of value.matchAll(placeholderPattern)) {
    const placeholder = match[1];
    placeholders.set(placeholder, (placeholders.get(placeholder) ?? 0) + 1);
  }
  return placeholders;
}

function canonicalizeLocaleTag(value: string) {
  try {
    return Intl.getCanonicalLocales(value)[0] ?? '';
  } catch {
    return '';
  }
}

function isLeftToRightLocale(value: string) {
  try {
    const locale = new Intl.Locale(value);
    const textDirection = (
      locale as Intl.Locale & { textInfo?: { direction?: string } }
    ).textInfo?.direction;
    if (textDirection) {
      return textDirection === 'ltr';
    }
    const script = locale.maximize().script;
    return !script || !rightToLeftScripts.has(script);
  } catch {
    return false;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
