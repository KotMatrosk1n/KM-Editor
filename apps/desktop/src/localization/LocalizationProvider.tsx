/* SPDX-License-Identifier: GPL-3.0-only */

import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useLayoutEffect,
  useMemo,
  useState
} from 'react';
import enResource from './resources/en.json';
import esResource from './resources/es.json';

export type LanguageCode = 'en' | 'es';

type LocalizationParams = Record<string, string | number>;
type LocalizationResource = {
  keys: Record<string, string>;
  literals: Record<string, string>;
};

type LocalizationContextValue = {
  language: LanguageCode;
  setLanguage: (language: LanguageCode) => void;
  t: (key: string, params?: LocalizationParams) => string;
  translateLiteral: (literal: string) => string;
};

export const languageStorageKey = 'km-editor.language.v1';

const localizationDebugStorageKey = 'km-editor.localization.debug';

export const supportedLanguages = [
  {
    code: 'en',
    flag: '🇺🇸'
  },
  {
    code: 'es',
    flag: '🇪🇸'
  }
] as const;

const resourcesByLanguage: Record<LanguageCode, LocalizationResource> = {
  en: enResource,
  es: esResource
};

const translatableAttributes = ['aria-label', 'title', 'placeholder', 'alt'] as const;
const skippedTextTags = new Set(['SCRIPT', 'STYLE', 'CODE', 'PRE', 'TEXTAREA']);
const skippedAttributeTags = new Set(['SCRIPT', 'STYLE', 'CODE', 'PRE']);
const ignoredSelector = '[data-localization-ignore="true"]';
const textOriginals = new WeakMap<Text, string>();
const attributeOriginals = new WeakMap<Element, Map<string, string>>();
const missingLiteralWarnings = new Set<string>();
const missingKeyWarnings = new Set<string>();
const pokemonTypeLiteralKeys = new Set([
  'Normal',
  'Fire',
  'Water',
  'Electric',
  'Grass',
  'Ice',
  'Fighting',
  'Poison',
  'Ground',
  'Flying',
  'Psychic',
  'Bug',
  'Rock',
  'Ghost',
  'Dragon',
  'Dark',
  'Steel',
  'Fairy'
]);

const defaultLocalizationContext: LocalizationContextValue = {
  language: 'en',
  setLanguage: () => undefined,
  t: (key, params) => translateKeyForLanguage('en', key, params),
  translateLiteral: (literal) => translateLiteralForLanguage('en', literal)
};

const LocalizationContext = createContext<LocalizationContextValue>(defaultLocalizationContext);

export function LocalizationProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<LanguageCode>(() => readStoredLanguage());

  const setLanguage = useCallback((nextLanguage: LanguageCode) => {
    setLanguageState(nextLanguage);
    writeStoredLanguage(nextLanguage);
  }, []);

  useLayoutEffect(() => {
    if (typeof document === 'undefined') {
      return undefined;
    }

    document.documentElement.lang = language;
    document.body?.setAttribute('data-km-language', language);

    if (!document.body) {
      return undefined;
    }

    localizeNodeAndDescendants(document.body, language);

    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === 'characterData' && mutation.target instanceof Text) {
          localizeTextNode(mutation.target, language);
          continue;
        }

        if (mutation.type === 'attributes' && mutation.target instanceof Element) {
          localizeElementAttributes(mutation.target, language);
          continue;
        }

        for (const node of mutation.addedNodes) {
          localizeNodeAndDescendants(node, language);
        }
      }
    });

    observer.observe(document.body, {
      attributeFilter: [...translatableAttributes],
      attributes: true,
      characterData: true,
      childList: true,
      subtree: true
    });

    return () => observer.disconnect();
  }, [language]);

  const value = useMemo<LocalizationContextValue>(
    () => ({
      language,
      setLanguage,
      t: (key, params) => translateKeyForLanguage(language, key, params),
      translateLiteral: (literal) => translateLiteralForLanguage(language, literal)
    }),
    [language, setLanguage]
  );

  return <LocalizationContext.Provider value={value}>{children}</LocalizationContext.Provider>;
}

export function useLocalization() {
  return useContext(LocalizationContext);
}

export function translateKeyForLanguage(
  language: LanguageCode,
  key: string,
  params?: LocalizationParams
) {
  const translated =
    resourcesByLanguage[language].keys[key] ?? resourcesByLanguage.en.keys[key] ?? key;
  warnMissingKey(language, key, translated);

  return interpolateLocalizationParams(translated, params);
}

export function translateLiteralForLanguage(language: LanguageCode, literal: string) {
  const translated = translateLiteralBodyForLanguage(language, literal);
  warnMissingLiteral(language, literal, translated);

  return translated;
}

function translateLiteralBodyForLanguage(language: LanguageCode, literal: string): string {
  if (language === 'en') {
    return literal;
  }

  const resource = resourcesByLanguage[language];
  const direct = resource.literals[literal] ?? resourcesByLanguage.en.literals[literal];
  if (direct) {
    return direct;
  }

  const clearCacheMatch = /^Clear Cache \((.+)\)$/.exec(literal);
  if (clearCacheMatch) {
    return `Borrar caché (${clearCacheMatch[1]})`;
  }

  const currentCacheSizeMatch = /^Current cache size: (.+)$/.exec(literal);
  if (currentCacheSizeMatch) {
    return `Tamaño actual de caché: ${currentCacheSizeMatch[1]}`;
  }

  const pendingChangesMatch = /^Pending changes \((\d+)\)$/.exec(literal);
  if (pendingChangesMatch) {
    return `Cambios pendientes (${pendingChangesMatch[1]})`;
  }

  const inlinePendingChangesMatch = /^Pending changes: (\d+)$/.exec(literal);
  if (inlinePendingChangesMatch) {
    return `Cambios pendientes: ${inlinePendingChangesMatch[1]}`;
  }

  const removePendingMatch = /^Remove pending change (\d+): (.+)$/.exec(literal);
  if (removePendingMatch) {
    return `Eliminar cambio pendiente ${removePendingMatch[1]}: ${removePendingMatch[2]}`;
  }

  const dumpPreviewMatch = /^Dump Importer preview accepted (\d+) rows? and rejected (\d+)\.$/.exec(literal);
  if (dumpPreviewMatch) {
    const acceptedRows = dumpPreviewMatch[1] === '1' ? 'fila aceptada' : 'filas aceptadas';
    const rejectedRows = dumpPreviewMatch[2] === '1' ? 'fila rechazada' : 'filas rechazadas';
    return `El Importador de volcados aceptó ${dumpPreviewMatch[1]} ${acceptedRows} y rechazó ${dumpPreviewMatch[2]} ${rejectedRows}.`;
  }

  const dumpParseMatch = /^Dump Importer source could not be parsed(?: at line (\d+), byte (\d+))?: (.+)$/.exec(
    literal
  );
  if (dumpParseMatch) {
    const location =
      dumpParseMatch[1] && dumpParseMatch[2]
        ? ` en la línea ${dumpParseMatch[1]}, byte ${dumpParseMatch[2]}`
        : '';
    return `No se pudo analizar el origen del Importador de volcados${location}: ${translateLiteralBodyForLanguage(
      language,
      dumpParseMatch[3]
    )}`;
  }

  const dumpReadMatch = /^Dump Importer source could not be read: (.+)$/.exec(literal);
  if (dumpReadMatch) {
    return `No se pudo leer el origen del Importador de volcados: ${dumpReadMatch[1]}`;
  }

  const dumpUnsupportedProfileMatch = /^Dump Importer profile '(.+)' is not supported\.$/.exec(literal);
  if (dumpUnsupportedProfileMatch) {
    return `El perfil '${dumpUnsupportedProfileMatch[1]}' no es compatible con el Importador de volcados.`;
  }

  const dumpDuplicateColumnMatch = /^Dump Importer source has more than one '(.+)' column\.$/.exec(literal);
  if (dumpDuplicateColumnMatch) {
    return `El origen del Importador de volcados tiene más de una columna '${dumpDuplicateColumnMatch[1]}'.`;
  }

  const importRowReviewMatch = /^Row (\d+) needs review\.$/.exec(literal);
  if (importRowReviewMatch) {
    return `La fila ${importRowReviewMatch[1]} necesita revisión.`;
  }

  const importNoChangesMatch = /^(.+) has no changed import values\.$/.exec(literal);
  if (importNoChangesMatch) {
    return `${importNoChangesMatch[1]} no tiene valores de importación cambiados.`;
  }

  const importEditSummaryMatch = /^(.+): (.+) -> (.+)\.$/.exec(literal);
  if (importEditSummaryMatch) {
    return `${importEditSummaryMatch[1]}: ${translateLiteralBodyForLanguage(language, importEditSummaryMatch[2])} -> ${importEditSummaryMatch[3]}.`;
  }

  const importRowItemIdMatch = /^Row (\d+): ItemId must be a non-negative integer\.$/.exec(literal);
  if (importRowItemIdMatch) {
    return `Fila ${importRowItemIdMatch[1]}: ItemId debe ser un número entero no negativo.`;
  }

  const importRowMissingItemMatch = /^Row (\d+): Item (\d+) is not present in the loaded Items workflow\.$/.exec(
    literal
  );
  if (importRowMissingItemMatch) {
    return `Fila ${importRowMissingItemMatch[1]}: el objeto ${importRowMissingItemMatch[2]} no está en el flujo de Objetos cargado.`;
  }

  const importRowSharedPriceMatch =
    /^Row (\d+): BuyPrice and SellPrice both changed to incompatible values, but they target the same stored item-table field\. Change one value, or keep BuyPrice equal to SellPrice multiplied by 2\.$/.exec(
      literal
    );
  if (importRowSharedPriceMatch) {
    return `Fila ${importRowSharedPriceMatch[1]}: BuyPrice y SellPrice cambiaron a valores incompatibles, pero apuntan al mismo campo almacenado de la tabla de objetos. Cambia un valor, o mantén BuyPrice igual a SellPrice multiplicado por 2.`;
  }

  const importRowRangeMatch = /^Row (\d+): (.+) value '(.+)' must be between (\d+) and (\d+)\.$/.exec(
    literal
  );
  if (importRowRangeMatch) {
    return `Fila ${importRowRangeMatch[1]}: el valor '${importRowRangeMatch[3]}' de ${translateLiteralBodyForLanguage(
      language,
      importRowRangeMatch[2]
    )} debe estar entre ${importRowRangeMatch[4]} y ${importRowRangeMatch[5]}.`;
  }

  const jsonRowObjectMatch = /^JSON row (\d+) must be an object\.$/.exec(literal);
  if (jsonRowObjectMatch) {
    return `La fila JSON ${jsonRowObjectMatch[1]} debe ser un objeto.`;
  }

  const jsonDuplicatePropertyMatch = /^JSON row (\d+) contains more than one '(.+)' property\.$/.exec(
    literal
  );
  if (jsonDuplicatePropertyMatch) {
    return `La fila JSON ${jsonDuplicatePropertyMatch[1]} contiene más de una propiedad '${jsonDuplicatePropertyMatch[2]}'.`;
  }

  const browseForMatch = /^Browse for (.+)$/.exec(literal);
  if (browseForMatch) {
    return `Buscar ${translateLiteralBodyForLanguage(language, browseForMatch[1])}`;
  }

  const selectMatch = /^Select (.+)$/.exec(literal);
  if (selectMatch) {
    return `Seleccionar ${translateLiteralBodyForLanguage(language, selectMatch[1])}`;
  }

  const slotMatch = /^Slot (\d+)$/.exec(literal);
  if (slotMatch) {
    return `Hueco ${slotMatch[1]}`;
  }

  const natureRaisesMatch = /^Nature raises (.+)\.$/.exec(literal);
  if (natureRaisesMatch) {
    return `La naturaleza sube ${translateLiteralBodyForLanguage(language, natureRaisesMatch[1])}.`;
  }

  const natureLowersMatch = /^Nature lowers (.+)\.$/.exec(literal);
  if (natureLowersMatch) {
    return `La naturaleza baja ${translateLiteralBodyForLanguage(language, natureLowersMatch[1])}.`;
  }

  const unavailableEncounterAreaMatch = /^(.+) encounters are not available for this location\.$/.exec(
    literal
  );
  if (unavailableEncounterAreaMatch) {
    return `Los encuentros de ${unavailableEncounterAreaMatch[1]} no están disponibles para esta ubicación.`;
  }

  const unavailableForLocationMatch = /^(.+) is not available for this location\.$/.exec(literal);
  if (unavailableForLocationMatch) {
    return `${unavailableForLocationMatch[1]} no está disponible para esta ubicación.`;
  }

  const copyEncounterTableMatch = /^Copy this (.+) table to (.+)\.$/.exec(literal);
  if (copyEncounterTableMatch) {
    return `Copiar esta tabla de ${copyEncounterTableMatch[1]} a ${copyEncounterTableMatch[2]}.`;
  }

  const shinyRateRollsMatch = /^Writes (\d+) PID rolls\.$/.exec(literal);
  if (shinyRateRollsMatch) {
    return `Escribe ${shinyRateRollsMatch[1]} tiradas PID.`;
  }

  const searchMatch = /^Search (.+)$/.exec(literal);
  if (searchMatch) {
    return `Buscar ${translateLiteralBodyForLanguage(language, searchMatch[1])}`;
  }

  const openMatch = /^Open (.+)$/.exec(literal);
  if (openMatch) {
    return `Abrir ${translateLiteralBodyForLanguage(language, openMatch[1])}`;
  }

  const saveMatch = /^Save (.+)$/.exec(literal);
  if (saveMatch) {
    return `Guardar ${translateLiteralBodyForLanguage(language, saveMatch[1])}`;
  }

  const stageMatch = /^Stage (.+)$/.exec(literal);
  if (stageMatch) {
    return `Preparar ${translateLiteralBodyForLanguage(language, stageMatch[1])}`;
  }

  const setMatch = /^Set (.+)$/.exec(literal);
  if (setMatch) {
    return `Configurar ${translateLiteralBodyForLanguage(language, setMatch[1])}`;
  }

  const addMatch = /^Add (.+)$/.exec(literal);
  if (addMatch) {
    return `Agregar ${translateLiteralBodyForLanguage(language, addMatch[1])}`;
  }

  const removeMatch = /^Remove (.+)$/.exec(literal);
  if (removeMatch) {
    return `Quitar ${translateLiteralBodyForLanguage(language, removeMatch[1])}`;
  }

  const moveSourceMatch = /^Move source (up|down)$/.exec(literal);
  if (moveSourceMatch) {
    return moveSourceMatch[1] === 'up' ? 'Subir fuente' : 'Bajar fuente';
  }

  const fileCountMatch = /^(\d+) files$/.exec(literal);
  if (fileCountMatch) {
    return fileCountMatch[1] === '1' ? '1 archivo' : `${fileCountMatch[1]} archivos`;
  }

  const gameDumpRequirementMatch =
    /^(Base RomFS|Base ExeFS) contains the (.+) required for (Pokemon Sword|Pokemon Shield|Pokemon Scarlet|Pokemon Violet)\.$/.exec(
      literal
    );
  if (gameDumpRequirementMatch) {
    return `${translateLiteralBodyForLanguage(
      language,
      gameDumpRequirementMatch[1]
    )} contiene ${translateLiteralBodyForLanguage(
      language,
      gameDumpRequirementMatch[2]
    )} requerido para ${translateLiteralBodyForLanguage(language, gameDumpRequirementMatch[3])}.`;
  }

  const typeList = translateSlashSeparatedTypeList(language, literal);
  if (typeList) {
    return typeList;
  }

  const updateReadyMatch = /^KM Editor v(.+) is ready to install\.$/.exec(literal);
  if (updateReadyMatch) {
    return `KM Editor v${updateReadyMatch[1]} está listo para instalar.`;
  }

  const updateAvailableMatch = /^KM Editor v(.+) is available on GitHub\.$/.exec(literal);
  if (updateAvailableMatch) {
    return `KM Editor v${updateAvailableMatch[1]} está disponible en GitHub.`;
  }

  const updateCurrentMatch = /^KM Editor v(.+) is up to date\.$/.exec(literal);
  if (updateCurrentMatch) {
    return `KM Editor v${updateCurrentMatch[1]} está actualizado.`;
  }

  const updateDownloadWithSizeMatch = /^Downloading KM Editor v(.+) \((.+)\)\.$/.exec(literal);
  if (updateDownloadWithSizeMatch) {
    return `Descargando KM Editor v${updateDownloadWithSizeMatch[1]} (${updateDownloadWithSizeMatch[2]}).`;
  }

  const updateDownloadingMatch = /^Downloading KM Editor v(.+)\.$/.exec(literal);
  if (updateDownloadingMatch) {
    return `Descargando KM Editor v${updateDownloadingMatch[1]}.`;
  }

  const updateProgressMatch = /^Downloading update \((.+) of (.+)\)\.$/.exec(literal);
  if (updateProgressMatch) {
    return `Descargando actualización (${updateProgressMatch[1]} de ${updateProgressMatch[2]}).`;
  }

  const updateProgressNoTotalMatch = /^Downloading update \((.+)\)\.$/.exec(literal);
  if (updateProgressNoTotalMatch) {
    return `Descargando actualización (${updateProgressNoTotalMatch[1]}).`;
  }

  return literal;
}

function translateSlashSeparatedTypeList(language: LanguageCode, literal: string): string | null {
  if (!literal.includes('/')) {
    return null;
  }

  const resource = resourcesByLanguage[language];
  const parts = literal.split('/').map((part) => part.trim());
  if (parts.length < 2 || parts.some((part) => part.length === 0)) {
    return null;
  }

  if (parts.some((part) => !pokemonTypeLiteralKeys.has(part))) {
    return null;
  }

  const translatedParts = parts.map((part) => resource.literals[part] ?? null);
  return translatedParts.every((part): part is string => part !== null)
    ? translatedParts.join(' / ')
    : null;
}

function localizeNodeAndDescendants(node: Node, language: LanguageCode) {
  if (node instanceof Text) {
    localizeTextNode(node, language);
    return;
  }

  if (!(node instanceof Element) || shouldSkipElement(node, 'text')) {
    return;
  }

  localizeElementAttributes(node, language);

  for (const child of Array.from(node.childNodes)) {
    localizeNodeAndDescendants(child, language);
  }
}

function localizeTextNode(node: Text, language: LanguageCode) {
  const parent = node.parentElement;
  if (!parent || shouldSkipElement(parent, 'text')) {
    return;
  }

  const currentValue = node.data;
  if (currentValue.trim().length === 0) {
    return;
  }

  const existingOriginal = textOriginals.get(node);
  const original =
    existingOriginal === undefined ||
    !isKnownLocalizedValue(currentValue, existingOriginal)
      ? currentValue
      : existingOriginal;
  textOriginals.set(node, original);

  const translated = translateTextPreservingWhitespace(original, language);
  if (node.data !== translated) {
    node.data = translated;
  }
}

function localizeElementAttributes(element: Element, language: LanguageCode) {
  if (shouldSkipElement(element, 'attribute')) {
    return;
  }

  let originals = attributeOriginals.get(element);
  if (!originals) {
    originals = new Map<string, string>();
    attributeOriginals.set(element, originals);
  }

  for (const attribute of translatableAttributes) {
    const currentValue = element.getAttribute(attribute);
    if (!currentValue || currentValue.trim().length === 0) {
      continue;
    }

    const existingOriginal = originals.get(attribute);
    const original =
      existingOriginal === undefined ||
      !isKnownLocalizedValue(currentValue, existingOriginal)
        ? currentValue
        : existingOriginal;
    originals.set(attribute, original);

    const translated = translateTextPreservingWhitespace(original, language);
    if (currentValue !== translated) {
      element.setAttribute(attribute, translated);
    }
  }
}

function translateTextPreservingWhitespace(value: string, language: LanguageCode) {
  const match = /^(\s*)([\s\S]*?)(\s*)$/.exec(value);
  if (!match) {
    return translateLiteralForLanguage(language, value);
  }

  const [, leading, body, trailing] = match;
  return `${leading}${translateLiteralForLanguage(language, body)}${trailing}`;
}

function isKnownLocalizedValue(value: string, original: string) {
  if (value === original) {
    return true;
  }

  return supportedLanguages.some(
    (language) => value === translateTextPreservingWhitespace(original, language.code)
  );
}

function shouldSkipElement(element: Element, mode: 'attribute' | 'text') {
  if (element.closest(ignoredSelector)) {
    return true;
  }

  return mode === 'text'
    ? skippedTextTags.has(element.tagName)
    : skippedAttributeTags.has(element.tagName);
}

function interpolateLocalizationParams(value: string, params?: LocalizationParams) {
  if (!params) {
    return value;
  }

  return value.replace(/\{([A-Za-z0-9_]+)\}/g, (match, key: string) =>
    params[key] === undefined ? match : String(params[key])
  );
}

function readStoredLanguage(): LanguageCode {
  if (typeof window === 'undefined') {
    return 'en';
  }

  try {
    const value = window.localStorage.getItem(languageStorageKey);
    return isLanguageCode(value) ? value : 'en';
  } catch {
    return 'en';
  }
}

function writeStoredLanguage(language: LanguageCode) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(languageStorageKey, language);
  } catch {
    // localStorage may be disabled in constrained webviews. The in-memory choice still applies.
  }
}

function isLanguageCode(value: string | null): value is LanguageCode {
  return value === 'en' || value === 'es';
}

function warnMissingKey(language: LanguageCode, key: string, translated: string) {
  if (language === 'en' || translated !== key || !isLocalizationDebugEnabled()) {
    return;
  }

  const warningKey = `${language}:${key}`;
  if (!missingKeyWarnings.has(warningKey)) {
    missingKeyWarnings.add(warningKey);
    console.warn(`Missing ${language} localization key: ${key}`);
  }
}

function warnMissingLiteral(language: LanguageCode, literal: string, translated: string) {
  if (language === 'en' || translated !== literal || !isLocalizationDebugEnabled()) {
    return;
  }

  const warningKey = `${language}:${literal}`;
  if (!missingLiteralWarnings.has(warningKey)) {
    missingLiteralWarnings.add(warningKey);
    console.warn(`Missing ${language} localization literal: ${literal}`);
  }
}

function isLocalizationDebugEnabled() {
  if (!import.meta.env.DEV || typeof window === 'undefined') {
    return false;
  }

  try {
    return window.localStorage.getItem(localizationDebugStorageKey) === 'true';
  } catch {
    return false;
  }
}
