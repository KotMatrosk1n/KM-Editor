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
import deResource from './resources/de.json';
import esResource from './resources/es.json';
import frResource from './resources/fr.json';
import ruResource from './resources/ru.json';
import ukResource from './resources/uk.json';
import zhResource from './resources/zh.json';

export type LanguageCode = 'en' | 'es' | 'fr' | 'de' | 'ru' | 'uk' | 'zh';

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
  },
  {
    code: 'fr',
    flag: '🇫🇷'
  },
  {
    code: 'de',
    flag: '🇩🇪'
  },
  {
    code: 'ru',
    flag: '🇷🇺'
  },
  {
    code: 'uk',
    flag: '🇺🇦'
  },
  {
    code: 'zh',
    flag: '🇨🇳'
  }
] as const;

const resourcesByLanguage: Record<LanguageCode, LocalizationResource> = {
  en: enResource,
  de: deResource,
  es: esResource,
  fr: frResource,
  ru: ruResource,
  uk: ukResource,
  zh: zhResource
};

const translatableAttributes = ['aria-label', 'title', 'placeholder', 'alt'] as const;
const skippedTextTags = new Set(['SCRIPT', 'STYLE', 'CODE', 'PRE', 'TEXTAREA']);
const skippedAttributeTags = new Set(['SCRIPT', 'STYLE', 'CODE', 'PRE']);
const ignoredSelector = '[data-localization-ignore="true"]';
const textOriginals = new WeakMap<Text, string>();
const attributeOriginals = new WeakMap<Element, Map<string, string>>();
const missingLiteralWarnings = new Set<string>();
const missingKeyWarnings = new Set<string>();
const maximumRememberedLocalizationWarnings = 500;
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

const ivScreenReservedRegionPatterns = [
  {
    pattern: /^IV Screen legacy secondary-stats setup hook branch site$/,
    template: 'IV Screen legacy secondary-stats setup hook branch site'
  },
  {
    pattern: /^IV Screen legacy normal stats graph refresh hook branch site$/,
    template: 'IV Screen legacy normal stats graph refresh hook branch site'
  },
  {
    pattern: /^IV Screen legacy normal graph value source (\d{2})$/,
    template: 'IV Screen legacy normal graph value source {index}'
  },
  {
    pattern: /^IV Screen multi-chart HP text value source (\d{2})$/,
    template: 'IV Screen multi-chart HP text value source {index}'
  },
  {
    pattern: /^IV Screen multi-chart stat text value source (\d{2})$/,
    template: 'IV Screen multi-chart stat text value source {index}'
  },
  {
    pattern: /^IV Screen multi-chart stat source (\d{2})$/,
    template: 'IV Screen multi-chart stat source {index}'
  },
  {
    pattern: /^IV Screen yellow graph raw value site (\d{2})$/,
    template: 'IV Screen yellow graph raw value site {index}'
  },
  {
    pattern: /^IV Screen legacy X-mode sparkle value source (\d{2})$/,
    template: 'IV Screen legacy X-mode sparkle value source {index}'
  },
  {
    pattern: /^IV Screen X-mode value-pane visibility (\d{2})$/,
    template: 'IV Screen X-mode value-pane visibility {index}'
  },
  {
    pattern: /^IV Screen X-toggle numeric text pane visibility (\d{2})$/,
    template: 'IV Screen X-toggle numeric text pane visibility {index}'
  },
  {
    pattern: /^IV Screen X-toggle stats refresh call$/,
    template: 'IV Screen X-toggle stats refresh call'
  },
  {
    pattern: /^IV Screen legacy value-pane visibility flag$/,
    template: 'IV Screen legacy value-pane visibility flag'
  },
  {
    pattern: /^IV Screen legacy value-pane visibility mask$/,
    template: 'IV Screen legacy value-pane visibility mask'
  },
  {
    pattern: /^IV Screen legacy summary renderer wrapper primary call$/,
    template: 'IV Screen legacy summary renderer wrapper primary call'
  },
  {
    pattern: /^IV Screen legacy summary renderer wrapper call (\d{2})$/,
    template: 'IV Screen legacy summary renderer wrapper call {index}'
  },
  {
    pattern: /^IV Screen legacy X-toggle yellow graph bypass branch$/,
    template: 'IV Screen legacy X-toggle yellow graph bypass branch'
  },
  {
    pattern: /^IV Screen wrapper cave slot (\d{2})$/,
    template: 'IV Screen wrapper cave slot {index}'
  },
  {
    pattern: /^IV Screen marker\/version fragment (\d+)$/,
    template: 'IV Screen marker/version fragment {index}'
  }
] as const;

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

  const minimumValueMatch = /^Value must be at least (-?\d+)\.$/.exec(literal);
  if (minimumValueMatch) {
    return formatLiteralTemplate(language, 'Value must be at least {minimum}.', {
      minimum: minimumValueMatch[1]
    });
  }

  const maximumValueMatch = /^Value must be at most (-?\d+)\.$/.exec(literal);
  if (maximumValueMatch) {
    return formatLiteralTemplate(language, 'Value must be at most {maximum}.', {
      maximum: maximumValueMatch[1]
    });
  }

  const minimumDraftValueMatch = /^Minimum value is (-?\d+)\.$/.exec(literal);
  if (minimumDraftValueMatch) {
    return formatLiteralTemplate(language, 'Value must be at least {minimum}.', {
      minimum: minimumDraftValueMatch[1]
    });
  }

  const maximumDraftValueMatch = /^Maximum value is (-?\d+)\.$/.exec(literal);
  if (maximumDraftValueMatch) {
    return formatLiteralTemplate(language, 'Value must be at most {maximum}.', {
      maximum: maximumDraftValueMatch[1]
    });
  }

  const inventoryLabelMatch = /^Inventory (\d+) of (\d+)$/.exec(literal);
  if (inventoryLabelMatch) {
    return formatLiteralTemplate(language, 'Inventory {index} of {count}', {
      count: inventoryLabelMatch[2],
      index: inventoryLabelMatch[1]
    });
  }

  const badgeCountMatch = /^(\d+) (Badge|Badges)$/.exec(literal);
  if (badgeCountMatch) {
    return formatLiteralTemplate(
      language,
      badgeCountMatch[2] === 'Badge' ? '{count} Badge' : '{count} Badges',
      { count: badgeCountMatch[1] }
    );
  }

  const badgeShopNameMatch = /^(.+) \[((?:\d+) (?:Badge|Badges))\]$/.exec(literal);
  if (badgeShopNameMatch) {
    return `${translateLiteralBodyForLanguage(language, badgeShopNameMatch[1])} [${translateLiteralBodyForLanguage(
      language,
      badgeShopNameMatch[2]
    )}]`;
  }

  const resource = resourcesByLanguage[language];
  const direct = resource.literals[literal] ?? resourcesByLanguage.en.literals[literal];
  if (direct) {
    return direct;
  }

  const npcItemGiftPendingCountMatch = /^(\d+) gift (group|groups) staged$/.exec(literal);
  if (npcItemGiftPendingCountMatch) {
    return formatLiteralTemplate(
      language,
      npcItemGiftPendingCountMatch[2] === 'group'
        ? '{count} gift group staged'
        : '{count} gift groups staged',
      { count: npcItemGiftPendingCountMatch[1] }
    );
  }

  const npcItemGiftUnavailableItemMatch = /^(.+) unavailable$/.exec(literal);
  if (npcItemGiftUnavailableItemMatch) {
    return formatLiteralTemplate(language, '{item} unavailable', {
      item: npcItemGiftUnavailableItemMatch[1]
    });
  }

  const npcItemGiftUnavailableOptionMatch = /^(.+) unavailable \(#(-?\d+)\)$/.exec(literal);
  if (npcItemGiftUnavailableOptionMatch) {
    return `${formatLiteralTemplate(language, '{item} unavailable', {
      item: npcItemGiftUnavailableOptionMatch[1]
    })} (#${npcItemGiftUnavailableOptionMatch[2]})`;
  }

  const npcItemGiftFallbackItemMatch = /^Item (-?\d+)$/.exec(literal);
  if (npcItemGiftFallbackItemMatch) {
    return formatLiteralTemplate(language, 'Item {itemId}', {
      itemId: npcItemGiftFallbackItemMatch[1]
    });
  }

  const npcItemGiftGroupLabelMatch = /^(.+) NPCs$/.exec(literal);
  if (npcItemGiftGroupLabelMatch) {
    const group = translateLiteralBodyForLanguage(language, npcItemGiftGroupLabelMatch[1]);
    if (/NPCs?/i.test(group)) {
      return group;
    }

    return formatLiteralTemplate(language, '{group} NPCs', {
      group
    });
  }

  const npcItemGiftItemOptionsFailureMatch =
    /^Item options could not be (decoded|read|loaded): (.+)$/.exec(literal);
  if (npcItemGiftItemOptionsFailureMatch) {
    const template = npcItemGiftItemOptionsFailureMatch[1] === 'decoded'
      ? 'Item options could not be decoded: {error}'
      : npcItemGiftItemOptionsFailureMatch[1] === 'read'
        ? 'Item options could not be read: {error}'
        : 'Item options could not be loaded: {error}';
    return formatLiteralTemplate(language, template, {
      error: translateLiteralBodyForLanguage(language, npcItemGiftItemOptionsFailureMatch[2])
    });
  }

  const npcItemGiftMissingSourceMatch =
    /^(.+) is missing\. NPC Item Gift can show defaults, but patching needs this AMX file\.$/.exec(
      literal
    );
  if (npcItemGiftMissingSourceMatch) {
    return formatLiteralTemplate(
      language,
      '{file} is missing. NPC Item Gift can show defaults, but patching needs this AMX file.',
      { file: npcItemGiftMissingSourceMatch[1] }
    );
  }

  const npcItemGiftMappedMissingSourceMatch =
    /^(.+) is missing\. Its mapped NPC gifts are read-only until the base script is restored\.$/.exec(
      literal
    );
  if (npcItemGiftMappedMissingSourceMatch) {
    return formatLiteralTemplate(
      language,
      '{file} is missing. Its mapped NPC gifts are read-only until the base script is restored.',
      { file: npcItemGiftMappedMissingSourceMatch[1] }
    );
  }

  const npcItemGiftMappedSourceBlockerMatch =
    /^(.+) (is unreadable or not a supported 64-bit AMX script|cannot verify its mapped operands against the base script)\. Its mapped gifts are blocked\.$/.exec(
      literal
    );
  if (npcItemGiftMappedSourceBlockerMatch) {
    const template = npcItemGiftMappedSourceBlockerMatch[2].startsWith('is unreadable')
      ? '{file} is unreadable or not a supported 64-bit AMX script. Its mapped gifts are blocked.'
      : '{file} cannot verify its mapped operands against the base script. Its mapped gifts are blocked.';
    return formatLiteralTemplate(language, template, {
      file: npcItemGiftMappedSourceBlockerMatch[1]
    });
  }

  const npcItemGiftIncompatibleOperandMatch =
    /^(.+) has an incompatible mapped operand or an unverified base layout and is blocked from editing\.$/.exec(
      literal
    );
  if (npcItemGiftIncompatibleOperandMatch) {
    return formatLiteralTemplate(
      language,
      '{gift} has an incompatible mapped operand or an unverified base layout and is blocked from editing.',
      { gift: npcItemGiftIncompatibleOperandMatch[1] }
    );
  }

  const npcItemGiftCompanionMismatchMatch =
    /^(.+) has companion operands that disagree with its primary value\. Staging this gift will normalize all owned companions\.$/.exec(
      literal
    );
  if (npcItemGiftCompanionMismatchMatch) {
    return formatLiteralTemplate(
      language,
      '{gift} has companion operands that disagree with its primary value. Staging this gift will normalize all owned companions.',
      { gift: npcItemGiftCompanionMismatchMatch[1] }
    );
  }

  const npcItemGiftSourceReadMatch =
    /^(.+) could not be read: (.+)\. Known vanilla values will be shown for that script\.$/.exec(
      literal
    );
  if (npcItemGiftSourceReadMatch) {
    return formatLiteralTemplate(
      language,
      '{source} could not be read: {error}. Known vanilla values will be shown for that script.',
      {
        error: translateLiteralBodyForLanguage(language, npcItemGiftSourceReadMatch[2]),
        source: npcItemGiftSourceReadMatch[1]
      }
    );
  }

  const npcItemGiftQuantityInspectMatch =
    /^(.+) quantity could not be inspected: (.+)\. Known vanilla quantity will be shown\.$/.exec(
      literal
    );
  if (npcItemGiftQuantityInspectMatch) {
    return formatLiteralTemplate(
      language,
      '{gift} quantity could not be inspected: {error}. Known vanilla quantity will be shown.',
      {
        error: translateLiteralBodyForLanguage(language, npcItemGiftQuantityInspectMatch[2]),
        gift: npcItemGiftQuantityInspectMatch[1]
      }
    );
  }

  const npcItemGiftItemInspectMatch =
    /^(.+) item could not be inspected: (.+)\. Known vanilla item will be shown\.$/.exec(literal);
  if (npcItemGiftItemInspectMatch) {
    return formatLiteralTemplate(
      language,
      '{gift} item could not be inspected: {error}. Known vanilla item will be shown.',
      {
        error: translateLiteralBodyForLanguage(language, npcItemGiftItemInspectMatch[2]),
        gift: npcItemGiftItemInspectMatch[1]
      }
    );
  }

  const npcItemGiftUnsupportedDomainMatch =
    /^Pending edit domain '(.+)' is not supported by NPC Item Gift\.$/.exec(literal);
  if (npcItemGiftUnsupportedDomainMatch) {
    return formatLiteralTemplate(
      language,
      "Pending edit domain '{domain}' is not supported by NPC Item Gift.",
      { domain: npcItemGiftUnsupportedDomainMatch[1] }
    );
  }

  const npcItemGiftUnsupportedEditMatch =
    /^Pending NPC Item Gift edit '(.+)' is not supported\.$/.exec(literal);
  if (npcItemGiftUnsupportedEditMatch) {
    return formatLiteralTemplate(
      language,
      "Pending NPC Item Gift edit '{recordId}' is not supported.",
      { recordId: npcItemGiftUnsupportedEditMatch[1] }
    );
  }

  const npcItemGiftUnsupportedFieldMatch =
    /^Pending NPC Item Gift field '(.+)' is not supported\.$/.exec(literal);
  if (npcItemGiftUnsupportedFieldMatch) {
    return formatLiteralTemplate(
      language,
      "Pending NPC Item Gift field '{field}' is not supported.",
      { field: npcItemGiftUnsupportedFieldMatch[1] }
    );
  }

  const npcItemGiftPlanPreviewMatch =
    /^NPC Item Gift change plan preview contains ([\d,]+) target file\(s\)\.$/.exec(literal);
  if (npcItemGiftPlanPreviewMatch) {
    const count = npcItemGiftPlanPreviewMatch[1];
    return formatLiteralTemplate(
      language,
      Number(count.replaceAll(',', '')) === 1
        ? 'NPC Item Gift change plan preview contains {count} target file.'
        : 'NPC Item Gift change plan preview contains {count} target files.',
      { count }
    );
  }

  const npcItemGiftAppliedMatch = /^Applied NPC Item Gift changes to (.+)\.$/.exec(literal);
  if (npcItemGiftAppliedMatch) {
    return formatLiteralTemplate(language, 'Applied NPC Item Gift changes to {path}.', {
      path: npcItemGiftAppliedMatch[1]
    });
  }

  const npcItemGiftFileFailureMatch =
    /^NPC Item Gift (source file could not be patched|output file could not be written): (.+)$/.exec(
      literal
    );
  if (npcItemGiftFileFailureMatch) {
    const template = npcItemGiftFileFailureMatch[1].startsWith('source')
      ? 'NPC Item Gift source file could not be patched: {error}'
      : 'NPC Item Gift output file could not be written: {error}';
    return formatLiteralTemplate(language, template, {
      error: translateLiteralBodyForLanguage(language, npcItemGiftFileFailureMatch[2])
    });
  }

  const npcItemGiftVerifiedFileFailureMatch =
    /^NPC Item Gift (verified output could not be written|source file could not be patched safely|source file could not be read): (.+)$/.exec(
      literal
    );
  if (npcItemGiftVerifiedFileFailureMatch) {
    const template = npcItemGiftVerifiedFileFailureMatch[1] === 'verified output could not be written'
      ? 'NPC Item Gift verified output could not be written: {error}'
      : npcItemGiftVerifiedFileFailureMatch[1] === 'source file could not be patched safely'
        ? 'NPC Item Gift source file could not be patched safely: {error}'
        : 'NPC Item Gift source file could not be read: {error}';
    return formatLiteralTemplate(language, template, {
      error: translateLiteralBodyForLanguage(language, npcItemGiftVerifiedFileFailureMatch[2])
    });
  }

  const npcItemGiftConflictingCellMatch =
    /^NPC Item Gift has conflicting staged values for AMX cell (.+)\.$/.exec(literal);
  if (npcItemGiftConflictingCellMatch) {
    return formatLiteralTemplate(
      language,
      'NPC Item Gift has conflicting staged values for AMX cell {cell}.',
      { cell: npcItemGiftConflictingCellMatch[1] }
    );
  }

  const npcItemGiftRequiredSourceMatch =
    /^(.+) is required before NPC Item Gift can be staged\.$/.exec(literal);
  if (npcItemGiftRequiredSourceMatch) {
    return formatLiteralTemplate(
      language,
      '{file} is required before NPC Item Gift can be staged.',
      { file: npcItemGiftRequiredSourceMatch[1] }
    );
  }

  const npcItemGiftSelectionMatch =
    /^NPC Item Gift selection '(.+)' (is duplicated|is not recognized for this game)\.$/.exec(
      literal
    );
  if (npcItemGiftSelectionMatch) {
    const template = npcItemGiftSelectionMatch[2] === 'is duplicated'
      ? "NPC Item Gift selection '{giftId}' is duplicated."
      : "NPC Item Gift selection '{giftId}' is not recognized for this game.";
    return formatLiteralTemplate(language, template, { giftId: npcItemGiftSelectionMatch[1] });
  }

  const npcItemGiftSelectionGameMatch =
    /^NPC Item Gift selection '(.+)' is not recognized for (Sword|Shield)\.$/.exec(literal);
  if (npcItemGiftSelectionGameMatch) {
    return formatLiteralTemplate(
      language,
      "NPC Item Gift selection '{giftId}' is not recognized for {game}.",
      {
        game: translateLiteralBodyForLanguage(language, npcItemGiftSelectionGameMatch[2]),
        giftId: npcItemGiftSelectionGameMatch[1]
      }
    );
  }

  const npcItemGiftQuantityRangeMatch = /^(.+) quantity must be between 1 and 999\.$/.exec(literal);
  if (npcItemGiftQuantityRangeMatch) {
    return formatLiteralTemplate(language, '{gift} quantity must be between 1 and 999.', {
      gift: npcItemGiftQuantityRangeMatch[1]
    });
  }

  const npcItemGiftSlotMatch =
    /^(.+) item slot '(.+)' (is not recognized|is duplicated|is missing)\.$/.exec(literal);
  if (npcItemGiftSlotMatch) {
    const template = npcItemGiftSlotMatch[3] === 'is not recognized'
      ? "{gift} item slot '{slotId}' is not recognized."
      : npcItemGiftSlotMatch[3] === 'is duplicated'
        ? "{gift} item slot '{slotId}' is duplicated."
        : "{gift} item slot '{slotId}' is missing.";
    return formatLiteralTemplate(language, template, {
      gift: npcItemGiftSlotMatch[1],
      slotId: npcItemGiftSlotMatch[2]
    });
  }

  const npcItemGiftUnselectableItemMatch =
    /^(.+) item (-?\d+) is not selectable for this project\.$/.exec(literal);
  if (npcItemGiftUnselectableItemMatch) {
    return formatLiteralTemplate(
      language,
      '{gift} item {itemId} is not selectable for this project.',
      {
        gift: npcItemGiftUnselectableItemMatch[1],
        itemId: npcItemGiftUnselectableItemMatch[2]
      }
    );
  }

  const npcItemGiftMappedOperandStatusMatch =
    /^(.+) cannot be staged while its mapped operands are (available|repairable|damaged|missing)\.$/.exec(
      literal
    );
  if (npcItemGiftMappedOperandStatusMatch) {
    return formatLiteralTemplate(
      language,
      '{gift} cannot be staged while its mapped operands are {status}.',
      {
        gift: npcItemGiftMappedOperandStatusMatch[1],
        status: translateLiteralBodyForLanguage(language, npcItemGiftMappedOperandStatusMatch[2])
      }
    );
  }

  const npcItemGiftStatusBlockerMatch =
    /^(.+) is (available|repairable|damaged|missing)\.$/.exec(literal);
  if (npcItemGiftStatusBlockerMatch) {
    return formatLiteralTemplate(language, '{subject} is {status}.', {
      status: translateLiteralBodyForLanguage(language, npcItemGiftStatusBlockerMatch[2]),
      subject: npcItemGiftStatusBlockerMatch[1]
    });
  }

  const npcItemGiftMissingSourceRecordMatch =
    /^(.+) does not have a loaded source record\.$/.exec(literal);
  if (npcItemGiftMissingSourceRecordMatch) {
    return formatLiteralTemplate(language, '{gift} does not have a loaded source record.', {
      gift: npcItemGiftMissingSourceRecordMatch[1]
    });
  }

  const npcItemGiftFixedQuantityMatch =
    /^(.+) uses a fixed helper quantity and only its item can be edited\.$/.exec(literal);
  if (npcItemGiftFixedQuantityMatch) {
    return formatLiteralTemplate(
      language,
      '{gift} uses a fixed helper quantity and only its item can be edited.',
      { gift: npcItemGiftFixedQuantityMatch[1] }
    );
  }

  const npcItemGiftMissingSelectionsMatch = /^(.+) is missing its item selections\.$/.exec(
    literal
  );
  if (npcItemGiftMissingSelectionsMatch) {
    return formatLiteralTemplate(language, '{gift} is missing its item selections.', {
      gift: npcItemGiftMissingSelectionsMatch[1]
    });
  }


  const npcItemGiftMissingItemSelectionMatch =
    /^(.+) contains a missing item selection\.$/.exec(literal);
  if (npcItemGiftMissingItemSelectionMatch) {
    return formatLiteralTemplate(language, '{gift} contains a missing item selection.', {
      gift: npcItemGiftMissingItemSelectionMatch[1]
    });
  }

  const catchCapInstalledMessageMatch =
    /^(Catch Cap Editor hook is installed for display and runtime capture checks\.|Catch Cap Editor has a legacy display-only hook installed; stage and apply to add the runtime capture gate hook\.) Changing values edits badge counts 0-7; eight badges is fixed at Lv\.100 by the game\.(?: The installed table has stale Lv\.(\d+) metadata for eight badges; stage and apply to rewrite it to Lv\.100\.)?$/.exec(
      literal
    );
  if (catchCapInstalledMessageMatch) {
    const translatedParts = [
      translateLiteralBodyForLanguage(language, catchCapInstalledMessageMatch[1]),
      translateLiteralBodyForLanguage(
        language,
        'Changing values edits badge counts 0-7; eight badges is fixed at Lv.100 by the game.'
      )
    ];
    if (catchCapInstalledMessageMatch[2]) {
      translatedParts.push(
        formatLiteralTemplate(
          language,
          'The installed table has stale Lv.{level} metadata for eight badges; stage and apply to rewrite it to Lv.100.',
          { level: catchCapInstalledMessageMatch[2] }
        )
      );
    }

    return translatedParts.join(' ');
  }

  const catchCapGameMismatchMatch =
    /^Selected (Pokemon Sword|Pokemon Shield), but exefs\/main build ID is (Pokemon Sword|Pokemon Shield) (1\.3\.2)\. Catch Cap Editor will not patch this file because Sword and Shield use different hook sites\.$/.exec(
      literal
    );
  if (catchCapGameMismatchMatch) {
    return formatLiteralTemplate(
      language,
      'Selected {selectedGame}, but exefs/main build ID is {detectedGame}. Catch Cap Editor will not patch this file because Sword and Shield use different hook sites.',
      {
        detectedGame: `${translateLiteralBodyForLanguage(
          language,
          catchCapGameMismatchMatch[2]
        )} ${catchCapGameMismatchMatch[3]}`,
        selectedGame: translateLiteralBodyForLanguage(language, catchCapGameMismatchMatch[1])
      }
    );
  }

  const catchCapInvalidBaseMatch =
    /^Base exefs\/main is not a selected-game vanilla Catch Cap source\. (.+)$/.exec(literal);
  if (catchCapInvalidBaseMatch) {
    return formatLiteralTemplate(
      language,
      'Base exefs/main is not a selected-game vanilla Catch Cap source. {detail}',
      {
        detail: translateLiteralBodyForLanguage(language, catchCapInvalidBaseMatch[1])
      }
    );
  }

  const catchCapSegmentHashMatch =
    /^Catch Cap patching rejected (.+) because its required NSO header hash does not match the decompressed segment\.$/.exec(
      literal
    );
  if (catchCapSegmentHashMatch) {
    return formatLiteralTemplate(
      language,
      'Catch Cap patching rejected {segment} because its required NSO header hash does not match the decompressed segment.',
      { segment: catchCapSegmentHashMatch[1] }
    );
  }

  const reviewedChangePlanStaleMatch = /^Reviewed change plan is stale\. (.+)$/.exec(literal);
  if (reviewedChangePlanStaleMatch) {
    return formatLiteralTemplate(language, 'Reviewed change plan is stale. {detail}', {
      detail: translateLiteralBodyForLanguage(language, reviewedChangePlanStaleMatch[1])
    });
  }

  const changePlanSourceVerificationMatch =
    /^Change-plan source verification failed: (.+)$/.exec(literal);
  if (changePlanSourceVerificationMatch) {
    return formatLiteralTemplate(language, 'Change-plan source verification failed: {detail}', {
      detail: translateLiteralBodyForLanguage(language, changePlanSourceVerificationMatch[1])
    });
  }

  const catchCapBadgeCountFixedMatch =
    /^Catch cap for badge count (\d+) is fixed at level (\d+); the game treats eight badges as catch any level\.$/.exec(
      literal
    );
  if (catchCapBadgeCountFixedMatch) {
    return formatLiteralTemplate(
      language,
      'Catch Cap badge count {badgeCount} is fixed at level {level}; the game treats eight badges as catch any level.',
      { badgeCount: catchCapBadgeCountFixedMatch[1], level: catchCapBadgeCountFixedMatch[2] }
    );
  }

  const catchCapBadgeCountOrderMatch =
    /^Catch cap for badge count (\d+) must be level (\d+) or higher\.$/.exec(literal);
  if (catchCapBadgeCountOrderMatch) {
    return formatLiteralTemplate(
      language,
      'Catch Cap badge count {badgeCount} must be level {level} or higher.',
      { badgeCount: catchCapBadgeCountOrderMatch[1], level: catchCapBadgeCountOrderMatch[2] }
    );
  }

  const catchCapLabelFixedMatch =
    /^Catch cap for (.+) is fixed at level (\d+); the game treats eight badges as catch any level\.$/.exec(
      literal
    );
  if (catchCapLabelFixedMatch) {
    return formatLiteralTemplate(
      language,
      'Catch cap for {label} is fixed at level {level}; the game treats eight badges as catch any level.',
      {
        label: translateLiteralBodyForLanguage(language, catchCapLabelFixedMatch[1]),
        level: catchCapLabelFixedMatch[2]
      }
    );
  }

  const catchCapLabelRangeMatch =
    /^Catch cap for (.+) must be between (\d+) and (\d+)\.$/.exec(literal);
  if (catchCapLabelRangeMatch) {
    return formatLiteralTemplate(
      language,
      'Catch cap for {label} must be between {minimum} and {maximum}.',
      {
        label: translateLiteralBodyForLanguage(language, catchCapLabelRangeMatch[1]),
        maximum: catchCapLabelRangeMatch[3],
        minimum: catchCapLabelRangeMatch[2]
      }
    );
  }

  const catchCapLabelOrderMatch =
    /^Catch cap for (.+) must be the same as or higher than the previous badge level \(level (\d+)\)\.$/.exec(
      literal
    );
  if (catchCapLabelOrderMatch) {
    return formatLiteralTemplate(
      language,
      'Catch cap for {label} must be the same as or higher than the previous badge level (level {level}).',
      {
        label: translateLiteralBodyForLanguage(language, catchCapLabelOrderMatch[1]),
        level: catchCapLabelOrderMatch[2]
      }
    );
  }

  const catchCapAriaMatch = /^Catch cap for (.+)$/.exec(literal);
  if (catchCapAriaMatch) {
    return formatLiteralTemplate(language, 'Catch cap for {label}', {
      label: translateLiteralBodyForLanguage(language, catchCapAriaMatch[1])
    });
  }

  const catchCapRangeMatch = /^Use Lv\. (\d+)-(\d+)\.$/.exec(literal);
  if (catchCapRangeMatch) {
    return formatLiteralTemplate(language, 'Use Lv. {minimum}-{maximum}.', {
      maximum: catchCapRangeMatch[2],
      minimum: catchCapRangeMatch[1]
    });
  }

  const catchCapOrderMatch = /^Must be Lv\. (\d+) or higher\.$/.exec(literal);
  if (catchCapOrderMatch) {
    return formatLiteralTemplate(language, 'Must be Lv. {minimum} or higher.', {
      minimum: catchCapOrderMatch[1]
    });
  }

  const catchCapLockedSelectionMatch =
    /^Lv\. (\d+) \(locked: full badges catch any level\)$/.exec(literal);
  if (catchCapLockedSelectionMatch) {
    return formatLiteralTemplate(
      language,
      'Lv. {level} (locked: full badges catch any level)',
      { level: catchCapLockedSelectionMatch[1] }
    );
  }

  const catchCapBadgeRangeDiagnosticMatch =
    /^Catch Cap badge count (\d+) must be between (\d+) and (\d+)\.$/.exec(literal);
  if (catchCapBadgeRangeDiagnosticMatch) {
    return formatLiteralTemplate(
      language,
      'Catch Cap badge count {badgeCount} must be between {minimum} and {maximum}.',
      {
        badgeCount: catchCapBadgeRangeDiagnosticMatch[1],
        maximum: catchCapBadgeRangeDiagnosticMatch[3],
        minimum: catchCapBadgeRangeDiagnosticMatch[2]
      }
    );
  }

  const catchCapBadgeIdentityDiagnosticMatch =
    /^Catch Cap badge count (\d+) (is not available|was supplied more than once|is missing)\.$/.exec(
      literal
    );
  if (catchCapBadgeIdentityDiagnosticMatch) {
    const template = catchCapBadgeIdentityDiagnosticMatch[2] === 'is not available'
      ? 'Catch Cap badge count {badgeCount} is not available.'
      : catchCapBadgeIdentityDiagnosticMatch[2] === 'was supplied more than once'
        ? 'Catch Cap badge count {badgeCount} was supplied more than once.'
        : 'Catch Cap badge count {badgeCount} is missing.';
    return formatLiteralTemplate(language, template, {
      badgeCount: catchCapBadgeIdentityDiagnosticMatch[1]
    });
  }

  const catchCapFixedBadgeDiagnosticMatch =
    /^Catch Cap badge count (\d+) is fixed at level (\d+); the game treats eight badges as catch any level\.$/.exec(
      literal
    );
  if (catchCapFixedBadgeDiagnosticMatch) {
    return formatLiteralTemplate(
      language,
      'Catch Cap badge count {badgeCount} is fixed at level {level}; the game treats eight badges as catch any level.',
      {
        badgeCount: catchCapFixedBadgeDiagnosticMatch[1],
        level: catchCapFixedBadgeDiagnosticMatch[2]
      }
    );
  }

  const catchCapEntryDiagnosticMatch = /^Catch Cap entry '(.+)' is not valid\.$/.exec(literal);
  if (catchCapEntryDiagnosticMatch) {
    return formatLiteralTemplate(language, "Catch Cap entry '{entry}' is not valid.", {
      entry: catchCapEntryDiagnosticMatch[1]
    });
  }

  const catchCapPlanCountMatch =
    /^Catch Cap Editor change plan preview contains ([\d,]+) target file\(s\)\.$/.exec(literal);
  if (catchCapPlanCountMatch) {
    return formatLiteralTemplate(
      language,
      'Catch Cap Editor change plan preview contains {count} target file(s).',
      { count: catchCapPlanCountMatch[1] }
    );
  }

  const catchCapFailureMatch =
    /^(Catch Cap Editor source file could not be patched|Catch Cap Editor output file could not be written|Catch Cap Editor uninstall could not restore exefs\/main|Catch Cap Editor uninstall could not update output|Catch Cap Editor verified output could not be prepared|Catch Cap Editor uninstall could not prepare a verified restoration): (.+)$/.exec(
      literal
    );
  if (catchCapFailureMatch) {
    return formatLiteralTemplate(language, `${catchCapFailureMatch[1]}: {error}`, {
      error: translateLiteralBodyForLanguage(language, catchCapFailureMatch[2])
    });
  }

  const ivScreenGameMismatchMatch =
    /^Selected (Pokemon Sword|Pokemon Shield), but exefs\/main build ID is (Pokemon Sword|Pokemon Shield) (1\.3\.2)\. IV Screen will not patch this file because Sword and Shield use different Pokemon Summary hook sites\.$/.exec(
      literal
    );
  if (ivScreenGameMismatchMatch) {
    return formatLiteralTemplate(
      language,
      'Selected {selectedGame}, but exefs/main build ID is {detectedGame}. IV Screen will not patch this file because Sword and Shield use different Pokemon Summary hook sites.',
      {
        detectedGame: `${translateLiteralBodyForLanguage(
          language,
          ivScreenGameMismatchMatch[2]
        )} ${ivScreenGameMismatchMatch[3]}`,
        selectedGame: translateLiteralBodyForLanguage(language, ivScreenGameMismatchMatch[1])
      }
    );
  }

  const ivScreenInvalidBaseMatch =
    /^Base exefs\/main is not a selected-game vanilla IV Screen source\. (.+)$/.exec(literal);
  if (ivScreenInvalidBaseMatch) {
    return formatLiteralTemplate(
      language,
      'Base exefs/main is not a selected-game vanilla IV Screen source. {detail}',
      { detail: translateLiteralBodyForLanguage(language, ivScreenInvalidBaseMatch[1]) }
    );
  }

  const ivScreenSegmentHashMatch =
    /^IV Screen patching rejected (.+) because its required NSO header hash does not match the decompressed segment\.$/.exec(
      literal
    );
  if (ivScreenSegmentHashMatch) {
    return formatLiteralTemplate(
      language,
      'IV Screen patching rejected {segment} because its required NSO header hash does not match the decompressed segment.',
      { segment: ivScreenSegmentHashMatch[1] }
    );
  }

  const ivScreenReservedRegionMatch = /^(.*?)( \(Shield\))?$/.exec(literal);
  if (ivScreenReservedRegionMatch) {
    for (const { pattern, template } of ivScreenReservedRegionPatterns) {
      const labelMatch = pattern.exec(ivScreenReservedRegionMatch[1]);
      if (!labelMatch) {
        continue;
      }

      const translatedLabel = formatLiteralTemplate(
        language,
        template,
        labelMatch[1] === undefined ? undefined : { index: labelMatch[1] }
      );
      return ivScreenReservedRegionMatch[2]
        ? formatLiteralTemplate(language, '{label} (Shield)', { label: translatedLabel })
        : translatedLabel;
    }
  }

  const ivScreenPendingDomainMatch =
    /^Pending edit domain '(.+)' is not supported by IV Screen\.$/.exec(literal);
  if (ivScreenPendingDomainMatch) {
    return formatLiteralTemplate(
      language,
      "Pending edit domain '{domain}' is not supported by IV Screen.",
      { domain: ivScreenPendingDomainMatch[1] }
    );
  }

  const ivScreenPendingEditMatch =
    /^Pending IV Screen edit '(.+)' is not supported\.$/.exec(literal);
  if (ivScreenPendingEditMatch) {
    return formatLiteralTemplate(
      language,
      "Pending IV Screen edit '{recordId}' is not supported.",
      { recordId: ivScreenPendingEditMatch[1] }
    );
  }

  const ivScreenPlanCountMatch =
    /^IV Screen change plan preview contains ([\d,]+) target file\(s\)\.$/.exec(literal);
  if (ivScreenPlanCountMatch) {
    return formatLiteralTemplate(
      language,
      'IV Screen change plan preview contains {count} target file(s).',
      { count: ivScreenPlanCountMatch[1] }
    );
  }

  const ivScreenReservedSlotMatch =
    /^IV Screen reserved slot (main\.text\+0x[\dA-F]+) is not empty\.$/.exec(literal);
  if (ivScreenReservedSlotMatch) {
    return formatLiteralTemplate(language, 'IV Screen reserved slot {offset} is not empty.', {
      offset: ivScreenReservedSlotMatch[1]
    });
  }

  const ivScreenExpectedValueMatch =
    /^IV Screen expected (vanilla or owned|vanilla) (.+) at (main\.text\+0x[\dA-F]+), but found (0x[\dA-F]+)\.$/.exec(
      literal
    );
  if (ivScreenExpectedValueMatch) {
    const template = ivScreenExpectedValueMatch[1] === 'vanilla'
      ? 'IV Screen expected vanilla {site} at {offset}, but found {actual}.'
      : 'IV Screen expected vanilla or owned {site} at {offset}, but found {actual}.';
    return formatLiteralTemplate(language, template, {
      actual: ivScreenExpectedValueMatch[4],
      offset: ivScreenExpectedValueMatch[3],
      site: translateLiteralBodyForLanguage(language, ivScreenExpectedValueMatch[2])
    });
  }

  const ivScreenExpectedSiteMatch =
    /^IV Screen expected vanilla or owned (.+) at (main\.text\+0x[\dA-F]+)\.$/.exec(literal);
  if (ivScreenExpectedSiteMatch) {
    return formatLiteralTemplate(
      language,
      'IV Screen expected vanilla or owned {site} at {offset}.',
      {
        offset: ivScreenExpectedSiteMatch[2],
        site: translateLiteralBodyForLanguage(language, ivScreenExpectedSiteMatch[1])
      }
    );
  }

  const ivScreenNestedErrorPatterns = [
    {
      pattern: /^Legacy IV Screen uninstall remains available, but migration is unavailable: (.+)$/,
      template: 'Legacy IV Screen uninstall remains available, but migration is unavailable: {error}'
    },
    {
      pattern: /^Exact legacy IV Screen uninstall remains available, but migration is blocked: (.+)$/,
      template: 'Exact legacy IV Screen uninstall remains available, but migration is blocked: {error}'
    },
    {
      pattern: /^IV Screen is installed with the exact initial Sword hook layout\. Uninstall remains available, but migration is unavailable because (.+)$/,
      template: 'IV Screen is installed with the exact initial Sword hook layout. Uninstall remains available, but migration is unavailable because {error}'
    },
    {
      pattern: /^IV Screen is not installed, but installation is blocked because (.+)$/,
      template: 'IV Screen is not installed, but installation is blocked because {error}'
    },
    {
      pattern: /^IV Screen legacy migration is unavailable\. (.+)$/,
      template: 'IV Screen legacy migration is unavailable. {detail}'
    },
    {
      pattern: /^IV Screen install or legacy migration is unavailable: (.+)$/,
      template: 'IV Screen install or legacy migration is unavailable: {error}'
    }
  ] as const;
  for (const { pattern, template } of ivScreenNestedErrorPatterns) {
    const match = pattern.exec(literal);
    if (match) {
      return formatLiteralTemplate(language, template, {
        [template.endsWith('{detail}') ? 'detail' : 'error']:
          translateLiteralBodyForLanguage(language, match[1])
      });
    }
  }

  const ivScreenFailureMatch =
    /^(ExeFS main sources could not be verified for IV Screen|IV Screen install preflight could not read exefs\/main|IV Screen source file could not be patched|IV Screen output file could not be written|IV Screen uninstall could not restore exefs\/main|IV Screen uninstall could not update the output file|IV Screen verified output could not be prepared|IV Screen uninstall could not prepare a verified restoration): (.+)$/.exec(
      literal
    );
  if (ivScreenFailureMatch) {
    return formatLiteralTemplate(language, `${ivScreenFailureMatch[1]}: {error}`, {
      error: translateLiteralBodyForLanguage(language, ivScreenFailureMatch[2])
    });
  }

  const startingItemsSlotFieldMatch = /^(Item|Quantity) for Bag Hook slot (\d+)$/.exec(
    literal
  );
  if (startingItemsSlotFieldMatch) {
    return formatLiteralTemplate(
      language,
      `${startingItemsSlotFieldMatch[1]} for Bag Hook slot {slot}`,
      { slot: startingItemsSlotFieldMatch[2] }
    );
  }

  const startingItemsPendingGrantMatch = /^slot (\d+): (.+) x(\d+)$/.exec(literal);
  const startingItemsPendingGrantSegments = literal.split(', ');
  if (
    startingItemsPendingGrantSegments.length > 1 &&
    startingItemsPendingGrantSegments.every((segment) => /^slot \d+: .+ x\d+$/.test(segment))
  ) {
    return startingItemsPendingGrantSegments
      .map((segment) => translateLiteralBodyForLanguage(language, segment))
      .join(', ');
  }
  if (startingItemsPendingGrantMatch) {
    const unknownItemMatch = /^item (\d+)$/.exec(startingItemsPendingGrantMatch[2]);
    const item = unknownItemMatch
      ? formatLiteralTemplate(language, 'item {itemId}', { itemId: unknownItemMatch[1] })
      : startingItemsPendingGrantMatch[2];
    return formatLiteralTemplate(language, 'slot {slot}: {item} x{quantity}', {
      item,
      quantity: startingItemsPendingGrantMatch[3],
      slot: startingItemsPendingGrantMatch[1]
    });
  }

  const startingItemsKeyOptionMatch = /^(.+) \(#(\d+)\) \[Key\]$/.exec(literal);
  if (startingItemsKeyOptionMatch) {
    return `${startingItemsKeyOptionMatch[1]} (#${startingItemsKeyOptionMatch[2]}) [${translateLiteralBodyForLanguage(
      language,
      'Key item'
    )}]`;
  }

  const startingItemsDamagedSlotsMatch =
    /^Starting Items cannot overwrite damaged Bag Hook slot\(s\): (.+)\.$/.exec(literal);
  if (startingItemsDamagedSlotsMatch) {
    return formatLiteralTemplate(
      language,
      'Starting Items cannot overwrite damaged Bag Hook slot(s): {slots}.',
      { slots: startingItemsDamagedSlotsMatch[1] }
    );
  }

  const startingItemsInvalidActiveGrantMatch =
    /^Bag Hook slot (\d+) contains an invalid active grant \(item ([^,]+), quantity ([^)]+)\)\.$/.exec(
      literal
    );
  if (startingItemsInvalidActiveGrantMatch) {
    return formatLiteralTemplate(
      language,
      'Bag Hook slot {slot} contains an invalid active grant (item {item}, quantity {quantity}).',
      {
        item: translateLiteralBodyForLanguage(language, startingItemsInvalidActiveGrantMatch[2]),
        quantity: translateLiteralBodyForLanguage(language, startingItemsInvalidActiveGrantMatch[3]),
        slot: startingItemsInvalidActiveGrantMatch[1]
      }
    );
  }

  const startingItemsInvalidGrantMatch =
    /^Invalid grant \(item ([^,]+), quantity ([^)]+)\)$/.exec(literal);
  if (startingItemsInvalidGrantMatch) {
    return formatLiteralTemplate(language, 'Invalid grant (item {item}, quantity {quantity})', {
      item: translateLiteralBodyForLanguage(language, startingItemsInvalidGrantMatch[1]),
      quantity: translateLiteralBodyForLanguage(language, startingItemsInvalidGrantMatch[2])
    });
  }

  const bagHookInvalidEncodedGrantMatch =
    /^(Legacy one-item grant|Active slot) uses invalid encoded values \(item ([^,]+), quantity ([^)]+)\)( and cannot be overwritten safely)?\.$/.exec(
      literal
    );
  if (bagHookInvalidEncodedGrantMatch) {
    const template = bagHookInvalidEncodedGrantMatch[1] === 'Legacy one-item grant'
      ? 'Legacy one-item grant uses invalid encoded values (item {item}, quantity {quantity}).'
      : 'Active slot uses invalid encoded values (item {item}, quantity {quantity}) and cannot be overwritten safely.';
    return formatLiteralTemplate(language, template, {
      item: translateLiteralBodyForLanguage(language, bagHookInvalidEncodedGrantMatch[2]),
      quantity: translateLiteralBodyForLanguage(language, bagHookInvalidEncodedGrantMatch[3])
    });
  }

  const bagHookSlotDuplicateMatch = /^Bag Hook slot (\d+) was supplied more than once\.$/.exec(literal);
  if (bagHookSlotDuplicateMatch) {
    return formatLiteralTemplate(language, 'Bag Hook slot {slot} was supplied more than once.', {
      slot: bagHookSlotDuplicateMatch[1]
    });
  }

  const bagHookDamagedSlotMatch =
    /^Bag Hook slot (\d+) is damaged and cannot be overwritten safely\.$/.exec(literal);
  if (bagHookDamagedSlotMatch) {
    return formatLiteralTemplate(
      language,
      'Bag Hook slot {slot} is damaged and cannot be overwritten safely.',
      { slot: bagHookDamagedSlotMatch[1] }
    );
  }

  const startingItemsDuplicateSlotMatch =
    /^Starting Items slot (\d+) was supplied more than once\.$/.exec(literal);
  if (startingItemsDuplicateSlotMatch) {
    return formatLiteralTemplate(
      language,
      'Starting Items slot {slot} was supplied more than once.',
      { slot: startingItemsDuplicateSlotMatch[1] }
    );
  }

  const startingItemsRoundTripMatch =
    /^Starting Items slot (\d+) did not round-trip (with the reviewed grant|as empty)\.$/.exec(literal);
  if (startingItemsRoundTripMatch) {
    const template = startingItemsRoundTripMatch[2] === 'with the reviewed grant'
      ? 'Starting Items slot {slot} did not round-trip with the reviewed grant.'
      : 'Starting Items slot {slot} did not round-trip as empty.';
    return formatLiteralTemplate(language, template, { slot: startingItemsRoundTripMatch[1] });
  }

  const startingItemsApplyFailureMatch =
    /^(Starting Items source file could not be patched|Starting Items output file could not be written): (.+)$/.exec(
      literal
    );
  if (startingItemsApplyFailureMatch) {
    const template = startingItemsApplyFailureMatch[1].includes('patched')
      ? 'Starting Items source file could not be patched: {error}'
      : 'Starting Items output file could not be written: {error}';
    return formatLiteralTemplate(language, template, {
      error: translateLiteralBodyForLanguage(language, startingItemsApplyFailureMatch[2])
    });
  }

  const startingItemsItemOptionsLoadMatch = /^Item options could not be loaded: (.+)$/.exec(literal);
  if (startingItemsItemOptionsLoadMatch) {
    return formatLiteralTemplate(language, 'Item options could not be loaded: {error}', {
      error: translateLiteralBodyForLanguage(language, startingItemsItemOptionsLoadMatch[1])
    });
  }

  const royalCandyStartingItemsConflictMatch =
    /^Clear item 1128 from Starting Items slot\(s\) (.+) before installing or refreshing Royal Candy; KM will not delete those grants automatically\.$/.exec(
      literal
    );
  if (royalCandyStartingItemsConflictMatch) {
    return formatLiteralTemplate(
      language,
      'Clear item 1128 from Starting Items slot(s) {slots} before installing or refreshing Royal Candy; KM will not delete those grants automatically.',
      { slots: royalCandyStartingItemsConflictMatch[1] }
    );
  }

  const numericPrefixedLiteralMatch = /^(-?\d{1,5})\s+(.+)$/.exec(literal);
  if (numericPrefixedLiteralMatch) {
    const translatedSuffix = translateLiteralBodyForLanguage(language, numericPrefixedLiteralMatch[2]);
    if (translatedSuffix !== numericPrefixedLiteralMatch[2]) {
      return `${numericPrefixedLiteralMatch[1]} ${translatedSuffix}`;
    }
  }

  const editableFieldHelpMatch = /^(.+)\. Allowed range: ([^.]+)(?:\. (.+))?$/.exec(literal);
  if (editableFieldHelpMatch) {
    const translatedLabel = translateEditableFieldHelpPrefix(
      language,
      editableFieldHelpMatch[1]
    );
    const translatedTail = editableFieldHelpMatch[3]
      ? `. ${translateLiteralBodyForLanguage(language, editableFieldHelpMatch[3])}`
      : '';
    const labelSeparator = /[.!?。！？]$/u.test(translatedLabel) ? ' ' : '. ';

    return `${translatedLabel}${labelSeparator}${translateLiteralBodyForLanguage(language, 'Allowed range')}: ${
      editableFieldHelpMatch[2]
    }${translatedTail}`;
  }

  const availableOptionsMatch = /^(\d+) available option(s)?$/.exec(literal);
  if (availableOptionsMatch) {
    const count = Number(availableOptionsMatch[1]);
    const optionLabel = count === 1 ? 'available option' : 'available options';
    return `${availableOptionsMatch[1]} ${translateLiteralBodyForLanguage(language, optionLabel)}`;
  }

  const moveHitCountMatch = /^(\d+) (hit|hits)$/.exec(literal);
  if (moveHitCountMatch) {
    return formatLiteralTemplate(
      language,
      moveHitCountMatch[2] === 'hit' ? '{count} hit' : '{count} hits',
      { count: moveHitCountMatch[1] }
    );
  }

  const moveHitRangeMatch = /^(\d+)-(\d+) hits$/.exec(literal);
  if (moveHitRangeMatch) {
    return formatLiteralTemplate(language, '{min}-{max} hits', {
      max: moveHitRangeMatch[2],
      min: moveHitRangeMatch[1]
    });
  }

  const moveTurnCountMatch = /^(\d+) (turn|turns)$/.exec(literal);
  if (moveTurnCountMatch) {
    return formatLiteralTemplate(
      language,
      moveTurnCountMatch[2] === 'turn' ? '{count} turn' : '{count} turns',
      { count: moveTurnCountMatch[1] }
    );
  }

  const moveTurnRangeMatch = /^(\d+)-(\d+) turns$/.exec(literal);
  if (moveTurnRangeMatch) {
    return formatLiteralTemplate(language, '{min}-{max} turns', {
      max: moveTurnRangeMatch[2],
      min: moveTurnRangeMatch[1]
    });
  }

  const moveDrainRecoilMatch = /^(Drain|Recoil) (\d+)%$/.exec(literal);
  if (moveDrainRecoilMatch) {
    return formatLiteralTemplate(language, `${moveDrainRecoilMatch[1]} {percent}%`, {
      percent: moveDrainRecoilMatch[2]
    });
  }

  const moveHealingMatch = /^(Restore|Cost) (\d+)% HP$/.exec(literal);
  if (moveHealingMatch) {
    return formatLiteralTemplate(language, `${moveHealingMatch[1]} {percent}% HP`, {
      percent: moveHealingMatch[2]
    });
  }

  const hyphenatedOptionMatch = /^(Default|Ability 1|Ability 2|Hidden Ability) - (.+)$/.exec(
    literal
  );
  if (hyphenatedOptionMatch) {
    return `${translateLiteralBodyForLanguage(language, hyphenatedOptionMatch[1])} - ${
      hyphenatedOptionMatch[2]
    }`;
  }

  const indexedFieldMatch =
    /^(Trainer item|Move|Relearn move|Form|Item|Emerge value|Drop count) (\d+) ?(ID|key|value)?$/.exec(
      literal
    );
  if (indexedFieldMatch) {
    const suffix = indexedFieldMatch[3]
      ? ` ${translateLiteralBodyForLanguage(language, indexedFieldMatch[3])}`
      : '';
    return `${translateLiteralBodyForLanguage(language, indexedFieldMatch[1])} ${
      indexedFieldMatch[2]
    }${suffix}`;
  }

  const starFieldMatch = /^(\d)-star (probability|value|drop chance|quantity)$/.exec(literal);
  if (starFieldMatch) {
    return formatLiteralTemplate(language, '{count}-star {label}', {
      count: starFieldMatch[1],
      label: translateLiteralBodyForLanguage(language, starFieldMatch[2])
    });
  }

  const raidRewardQuantitySummaryMatch = /^Quantity (\d+(?:\/\d+)*)$/.exec(literal);
  if (raidRewardQuantitySummaryMatch) {
    return `${translateLiteralBodyForLanguage(language, 'Quantity')} ${raidRewardQuantitySummaryMatch[1]}`;
  }

  const raidRewardDropSummaryMatch = /^Drop chance (\d+(?:\/\d+)*)%$/.exec(literal);
  if (raidRewardDropSummaryMatch) {
    return `${translateLiteralBodyForLanguage(language, 'Drop chance')} ${raidRewardDropSummaryMatch[1]}%`;
  }

  const raidRewardSegments = literal.split(' / ');
  if (raidRewardSegments.length > 1) {
    const translatedSegments = raidRewardSegments.map((segment) => {
      const chanceMatch = /^(\d)-star (\d+)% chance$/.exec(segment);
      if (chanceMatch) {
        return formatLiteralTemplate(language, '{count}-star {percent}% chance', {
          count: chanceMatch[1],
          percent: chanceMatch[2]
        });
      }

      const quantityMatch = /^(\d)-star (\d+) (item|items)$/.exec(segment);
      if (quantityMatch) {
        return formatLiteralTemplate(
          language,
          quantityMatch[3] === 'item'
            ? '{count}-star {quantity} item'
            : '{count}-star {quantity} items',
          {
            count: quantityMatch[1],
            quantity: quantityMatch[2]
          }
        );
      }

      return null;
    });
    if (translatedSegments.every((segment): segment is string => segment !== null)) {
      return translatedSegments.join(' / ');
    }
  }

  const recordSummaryMatch = /^(Gift|Rental|Adventure|Static) (\d+): (.+)$/.exec(literal);
  if (recordSummaryMatch) {
    return `${translateLiteralBodyForLanguage(language, recordSummaryMatch[1])} ${
      recordSummaryMatch[2]
    }: ${recordSummaryMatch[3]}`;
  }

  const fieldItemMatch = /^Field item: (.+)$/.exec(literal);
  if (fieldItemMatch) {
    return `${translateLiteralBodyForLanguage(language, 'Field item')}: ${fieldItemMatch[1]}`;
  }

  const behaviorEntryMatch = /^(#\d+ .+) - (.+)$/.exec(literal);
  if (behaviorEntryMatch) {
    const translatedBehavior = translateLiteralBodyForLanguage(language, behaviorEntryMatch[2]);
    return translatedBehavior === behaviorEntryMatch[2]
      ? literal
      : `${behaviorEntryMatch[1]} - ${translatedBehavior}`;
  }

  const storyIndexMatch = /^story #(\d+)$/.exec(literal);
  if (storyIndexMatch) {
    return `${translateLiteralBodyForLanguage(language, 'story')} #${storyIndexMatch[1]}`;
  }

  const unknownFieldMatch = /^Unknown field (.+)$/.exec(literal);
  if (unknownFieldMatch) {
    return `${translateLiteralBodyForLanguage(language, 'Unknown field')} ${
      unknownFieldMatch[1]
    }`;
  }

  const hashUsesSaveKeyMatch = /^(Flag|Work) hash (0x[0-9A-Fa-f]+) uses save key (0x[0-9A-Fa-f]+)\.$/.exec(
    literal
  );
  if (hashUsesSaveKeyMatch) {
    return `${translateLiteralBodyForLanguage(language, hashUsesSaveKeyMatch[1])} hash ${
      hashUsesSaveKeyMatch[2]
    } ${translateLiteralBodyForLanguage(language, 'uses save key')} ${hashUsesSaveKeyMatch[3]}.`;
  }

  const saveWorkKeyDerivedMatch = /^Save work key (0x[0-9A-Fa-f]+) is derived from (.+)\.$/.exec(
    literal
  );
  if (saveWorkKeyDerivedMatch) {
    return `${translateLiteralBodyForLanguage(language, 'Save work key')} ${
      saveWorkKeyDerivedMatch[1]
    } ${translateLiteralBodyForLanguage(language, 'is derived from')} ${
      saveWorkKeyDerivedMatch[2]
    }.`;
  }

  const natureStatSummaryMatch = /^(.+) \(\+(.+)\/-(.+)\)$/.exec(literal);
  if (natureStatSummaryMatch) {
    return `${translateLiteralBodyForLanguage(language, natureStatSummaryMatch[1])} (+${translateLiteralBodyForLanguage(
      language,
      natureStatSummaryMatch[2]
    )}/-${translateLiteralBodyForLanguage(language, natureStatSummaryMatch[3])})`;
  }

  const clearCacheMatch = /^Clear Cache \((.+)\)$/.exec(literal);
  if (clearCacheMatch) {
    return formatLiteralTemplate(language, 'Clear Cache ({size})', {
      size: clearCacheMatch[1]
    });
  }

  const currentCacheSizeMatch = /^Current cache size: (.+)$/.exec(literal);
  if (currentCacheSizeMatch) {
    return formatLiteralTemplate(language, 'Current cache size: {size}', {
      size: currentCacheSizeMatch[1]
    });
  }

  const pendingChangesMatch = /^Pending changes \((\d+)\)$/.exec(literal);
  if (pendingChangesMatch) {
    return formatLiteralTemplate(language, 'Pending changes ({count})', {
      count: pendingChangesMatch[1]
    });
  }

  const inlinePendingChangesMatch = /^Pending changes: (\d+)$/.exec(literal);
  if (inlinePendingChangesMatch) {
    return formatLiteralTemplate(language, 'Pending changes: {count}', {
      count: inlinePendingChangesMatch[1]
    });
  }

  const removePendingMatch = /^Remove pending change (\d+): (.+)$/.exec(literal);
  if (removePendingMatch) {
    return formatLiteralTemplate(language, 'Remove pending change {index}: {label}', {
      index: removePendingMatch[1],
      label: removePendingMatch[2]
    });
  }

  const dumpPreviewMatch = /^Dump Importer preview accepted (\d+) rows? and rejected (\d+)\.$/.exec(literal);
  if (dumpPreviewMatch) {
    const acceptedRows =
      dumpPreviewMatch[1] === '1'
        ? translateLiteralBodyForLanguage(language, 'accepted row')
        : translateLiteralBodyForLanguage(language, 'accepted rows');
    const rejectedRows =
      dumpPreviewMatch[2] === '1'
        ? translateLiteralBodyForLanguage(language, 'rejected row')
        : translateLiteralBodyForLanguage(language, 'rejected rows');
    return formatLiteralTemplate(
      language,
      'Dump Importer preview accepted {accepted} {acceptedRows} and rejected {rejected} {rejectedRows}.',
      {
        accepted: dumpPreviewMatch[1],
        acceptedRows,
        rejected: dumpPreviewMatch[2],
        rejectedRows
      }
    );
  }

  const dumpParseMatch = /^Dump Importer source could not be parsed(?: at line (\d+), byte (\d+))?: (.+)$/.exec(
    literal
  );
  if (dumpParseMatch) {
    const message = translateLiteralBodyForLanguage(language, dumpParseMatch[3]);
    return dumpParseMatch[1] && dumpParseMatch[2]
      ? formatLiteralTemplate(
          language,
          'Dump Importer source could not be parsed at line {line}, byte {byte}: {message}',
          {
            line: dumpParseMatch[1],
            byte: dumpParseMatch[2],
            message
          }
        )
      : formatLiteralTemplate(language, 'Dump Importer source could not be parsed: {message}', {
          message
        });
  }

  const dumpReadMatch = /^Dump Importer source could not be read: (.+)$/.exec(literal);
  if (dumpReadMatch) {
    return formatLiteralTemplate(language, 'Dump Importer source could not be read: {error}', {
      error: dumpReadMatch[1]
    });
  }

  const dumpUnsupportedProfileMatch = /^Dump Importer profile '(.+)' is not supported\.$/.exec(literal);
  if (dumpUnsupportedProfileMatch) {
    return formatLiteralTemplate(language, "Dump Importer profile '{profile}' is not supported.", {
      profile: dumpUnsupportedProfileMatch[1]
    });
  }

  const dumpDuplicateColumnMatch = /^Dump Importer source has more than one '(.+)' column\.$/.exec(literal);
  if (dumpDuplicateColumnMatch) {
    return formatLiteralTemplate(
      language,
      "Dump Importer source has more than one '{column}' column.",
      {
        column: dumpDuplicateColumnMatch[1]
      }
    );
  }

  const importRowReviewMatch = /^Row (\d+) needs review\.$/.exec(literal);
  if (importRowReviewMatch) {
    return formatLiteralTemplate(language, 'Row {row} needs review.', {
      row: importRowReviewMatch[1]
    });
  }

  const importNoChangesMatch = /^(.+) has no changed import values\.$/.exec(literal);
  if (importNoChangesMatch) {
    return formatLiteralTemplate(language, '{row} has no changed import values.', {
      row: importNoChangesMatch[1]
    });
  }

  const noEntriesInMatch = /^No entries in (.+)\.$/.exec(literal);
  if (noEntriesInMatch) {
    return formatLiteralTemplate(language, 'No entries in {section}.', {
      section: translateLiteralBodyForLanguage(language, noEntriesInMatch[1])
    });
  }

  const importEditSummaryMatch = /^(.+): (.+) -> (.+)\.$/.exec(literal);
  if (importEditSummaryMatch) {
    return formatLiteralTemplate(language, '{row}: {field} -> {value}.', {
      row: importEditSummaryMatch[1],
      field: translateLiteralBodyForLanguage(language, importEditSummaryMatch[2]),
      value: importEditSummaryMatch[3]
    });
  }

  const importRowItemIdMatch = /^Row (\d+): ItemId must be a non-negative integer\.$/.exec(literal);
  if (importRowItemIdMatch) {
    return formatLiteralTemplate(language, 'Row {row}: ItemId must be a non-negative integer.', {
      row: importRowItemIdMatch[1]
    });
  }

  const importRowMissingItemMatch = /^Row (\d+): Item (\d+) is not present in the loaded Items workflow\.$/.exec(
    literal
  );
  if (importRowMissingItemMatch) {
    return formatLiteralTemplate(
      language,
      'Row {row}: Item {itemId} is not present in the loaded Items workflow.',
      {
        row: importRowMissingItemMatch[1],
        itemId: importRowMissingItemMatch[2]
      }
    );
  }

  const importRowSharedPriceMatch =
    /^Row (\d+): BuyPrice and SellPrice both changed to incompatible values, but they target the same stored item-table field\. Change one value, or keep BuyPrice equal to SellPrice multiplied by 2\.$/.exec(
      literal
    );
  if (importRowSharedPriceMatch) {
    return formatLiteralTemplate(
      language,
      'Row {row}: BuyPrice and SellPrice both changed to incompatible values, but they target the same stored item-table field. Change one value, or keep BuyPrice equal to SellPrice multiplied by 2.',
      {
        row: importRowSharedPriceMatch[1]
      }
    );
  }

  const importRowRangeMatch = /^Row (\d+): (.+) value '(.+)' must be between (\d+) and (\d+)\.$/.exec(
    literal
  );
  if (importRowRangeMatch) {
    return formatLiteralTemplate(
      language,
      "Row {row}: {field} value '{value}' must be between {minimum} and {maximum}.",
      {
        row: importRowRangeMatch[1],
        field: translateLiteralBodyForLanguage(language, importRowRangeMatch[2]),
        value: importRowRangeMatch[3],
        minimum: importRowRangeMatch[4],
        maximum: importRowRangeMatch[5]
      }
    );
  }

  const jsonRowObjectMatch = /^JSON row (\d+) must be an object\.$/.exec(literal);
  if (jsonRowObjectMatch) {
    return formatLiteralTemplate(language, 'JSON row {row} must be an object.', {
      row: jsonRowObjectMatch[1]
    });
  }

  const jsonDuplicatePropertyMatch = /^JSON row (\d+) contains more than one '(.+)' property\.$/.exec(
    literal
  );
  if (jsonDuplicatePropertyMatch) {
    return formatLiteralTemplate(
      language,
      "JSON row {row} contains more than one '{property}' property.",
      {
        row: jsonDuplicatePropertyMatch[1],
        property: jsonDuplicatePropertyMatch[2]
      }
    );
  }

  const browseForMatch = /^Browse for (.+)$/.exec(literal);
  if (browseForMatch) {
    return formatLiteralTemplate(language, 'Browse for {target}', {
      target: translateLiteralBodyForLanguage(language, browseForMatch[1])
    });
  }

  const selectMatch = /^Select (.+)$/.exec(literal);
  if (selectMatch) {
    return formatLiteralTemplate(language, 'Select {target}', {
      target: translateLiteralBodyForLanguage(language, selectMatch[1])
    });
  }

  const slotMatch = /^Slot (\d+)$/.exec(literal);
  if (slotMatch) {
    return formatLiteralTemplate(language, 'Slot {index}', {
      index: slotMatch[1]
    });
  }

  const natureRaisesMatch = /^Nature raises (.+)\.$/.exec(literal);
  if (natureRaisesMatch) {
    return formatLiteralTemplate(language, 'Nature raises {stat}.', {
      stat: translateLiteralBodyForLanguage(language, natureRaisesMatch[1])
    });
  }

  const natureLowersMatch = /^Nature lowers (.+)\.$/.exec(literal);
  if (natureLowersMatch) {
    return formatLiteralTemplate(language, 'Nature lowers {stat}.', {
      stat: translateLiteralBodyForLanguage(language, natureLowersMatch[1])
    });
  }

  const unavailableEncounterAreaMatch = /^(.+) encounters are not available for this location\.$/.exec(
    literal
  );
  if (unavailableEncounterAreaMatch) {
    return formatLiteralTemplate(
      language,
      '{area} encounters are not available for this location.',
      {
        area: unavailableEncounterAreaMatch[1]
      }
    );
  }

  const unavailableForLocationMatch = /^(.+) is not available for this location\.$/.exec(literal);
  if (unavailableForLocationMatch) {
    return formatLiteralTemplate(language, '{item} is not available for this location.', {
      item: unavailableForLocationMatch[1]
    });
  }

  const copyEncounterTableMatch = /^Copy this (.+) table to (.+)\.$/.exec(literal);
  if (copyEncounterTableMatch) {
    return formatLiteralTemplate(language, 'Copy this {table} table to {target}.', {
      table: copyEncounterTableMatch[1],
      target: copyEncounterTableMatch[2]
    });
  }

  const shinyRateRollsMatch = /^Writes (\d+) PID rolls\.$/.exec(literal);
  if (shinyRateRollsMatch) {
    return formatLiteralTemplate(language, 'Writes {count} PID rolls.', {
      count: shinyRateRollsMatch[1]
    });
  }

  const searchMatch = /^Search (.+)$/.exec(literal);
  if (searchMatch) {
    return formatLiteralTemplate(language, 'Search {target}', {
      target: translateLiteralBodyForLanguage(language, searchMatch[1])
    });
  }

  const openMatch = /^Open (.+)$/.exec(literal);
  if (openMatch) {
    return formatLiteralTemplate(language, 'Open {target}', {
      target: translateLiteralBodyForLanguage(language, openMatch[1])
    });
  }

  const saveMatch = /^Save (.+)$/.exec(literal);
  if (saveMatch) {
    return formatLiteralTemplate(language, 'Save {target}', {
      target: translateLiteralBodyForLanguage(language, saveMatch[1])
    });
  }

  const stageMatch = /^Stage (.+)$/.exec(literal);
  if (stageMatch) {
    return formatLiteralTemplate(language, 'Stage {target}', {
      target: translateLiteralBodyForLanguage(language, stageMatch[1])
    });
  }

  const setMatch = /^Set (.+)$/.exec(literal);
  if (setMatch) {
    return formatLiteralTemplate(language, 'Set {target}', {
      target: translateLiteralBodyForLanguage(language, setMatch[1])
    });
  }

  const addMatch = /^Add (.+)$/.exec(literal);
  if (addMatch) {
    return formatLiteralTemplate(language, 'Add {target}', {
      target: translateLiteralBodyForLanguage(language, addMatch[1])
    });
  }

  const removeMatch = /^Remove (.+)$/.exec(literal);
  if (removeMatch) {
    return formatLiteralTemplate(language, 'Remove {target}', {
      target: translateLiteralBodyForLanguage(language, removeMatch[1])
    });
  }

  const moveSourceMatch = /^Move source (up|down)$/.exec(literal);
  if (moveSourceMatch) {
    return moveSourceMatch[1] === 'up'
      ? formatLiteralTemplate(language, 'Move source up')
      : formatLiteralTemplate(language, 'Move source down');
  }

  const fileCountMatch = /^(\d+) files$/.exec(literal);
  if (fileCountMatch) {
    return fileCountMatch[1] === '1'
      ? formatLiteralTemplate(language, '{count} file', { count: fileCountMatch[1] })
      : formatLiteralTemplate(language, '{count} files', { count: fileCountMatch[1] });
  }

  const gameDumpRequirementMatch =
    /^(Base RomFS|Base ExeFS) contains the (.+) required for (Pokemon Sword|Pokemon Shield|Pokemon Scarlet|Pokemon Violet)\.$/.exec(
      literal
  );
  if (gameDumpRequirementMatch) {
    return formatLiteralTemplate(language, '{source} contains the {archive} required for {game}.', {
      source: translateLiteralBodyForLanguage(language, gameDumpRequirementMatch[1]),
      archive: translateLiteralBodyForLanguage(language, gameDumpRequirementMatch[2]),
      game: translateLiteralBodyForLanguage(language, gameDumpRequirementMatch[3])
    });
  }

  const titleIdMatch =
    /^(Base RomFS|Base ExeFS|Output root folder) matches selected (Pokemon Sword|Pokemon Shield|Pokemon Scarlet|Pokemon Violet) title id (0x[0-9A-Fa-f]+)\.$/.exec(
      literal
  );
  if (titleIdMatch) {
    return formatLiteralTemplate(
      language,
      `${titleIdMatch[1]} matches selected {game} title id {titleId}.`,
      {
        game: translateLiteralBodyForLanguage(language, titleIdMatch[2]),
        titleId: titleIdMatch[3]
      }
    );
  }

  const fixedIvsMatch =
    /^Fixed IVs: HP (\d+), Atk (\d+), Def (\d+), SpA (\d+), SpD (\d+), Spe (\d+)$/.exec(
      literal
  );
  if (fixedIvsMatch) {
    return formatLiteralTemplate(
      language,
      'Fixed IVs: HP {hp}, Atk {attack}, Def {defense}, SpA {specialAttack}, SpD {specialDefense}, Spe {speed}',
      {
        hp: fixedIvsMatch[1],
        attack: fixedIvsMatch[2],
        defense: fixedIvsMatch[3],
        specialAttack: fixedIvsMatch[4],
        specialDefense: fixedIvsMatch[5],
        speed: fixedIvsMatch[6]
      }
    );
  }

  const perfectIvMatch = /^(\d+) [Gg]uaranteed [Pp]erfect IVs?$/.exec(literal);
  if (perfectIvMatch) {
    return perfectIvMatch[1] === '1'
      ? formatLiteralTemplate(language, '{count} guaranteed perfect IV', {
          count: perfectIvMatch[1]
        })
      : formatLiteralTemplate(language, '{count} guaranteed perfect IVs', {
          count: perfectIvMatch[1]
        });
  }

  const tradeLabelMatch = /^Trade (\d+): (.+) -> (.+) Lv\. (\d+)$/.exec(literal);
  if (tradeLabelMatch) {
    return formatLiteralTemplate(language, 'Trade {index}: {from} -> {to} Lv. {level}', {
      index: tradeLabelMatch[1],
      from: tradeLabelMatch[2],
      to: tradeLabelMatch[3],
      level: tradeLabelMatch[4]
    });
  }

  const hiddenItemSlotMatch = /^Hidden Item Slot (\d+)$/.exec(literal);
  if (hiddenItemSlotMatch) {
    return formatLiteralTemplate(language, 'Hidden Item Slot {index}', {
      index: hiddenItemSlotMatch[1]
    });
  }

  const unusedDefaultMatch = /^Unused Default (\d+)$/.exec(literal);
  if (unusedDefaultMatch) {
    return formatLiteralTemplate(language, 'Unused Default {index}', {
      index: unusedDefaultMatch[1]
    });
  }

  const inflictFallbackMatch = /^Inflict (\d+)$/.exec(literal);
  if (inflictFallbackMatch) {
    return formatLiteralTemplate(language, 'Inflict {index}', {
      index: inflictFallbackMatch[1]
    });
  }

  const placementSlotFieldMatch = /^(Item|Emerge value|Drop count) (\d+)$/.exec(literal);
  if (placementSlotFieldMatch) {
    return `${translateLiteralBodyForLanguage(language, placementSlotFieldMatch[1])} ${placementSlotFieldMatch[2]}`;
  }

  const fallbackLabelsUnavailableMatch =
    /^(.+) are not available; numeric fallback labels will be shown\.$/.exec(literal);
  if (fallbackLabelsUnavailableMatch) {
    return formatLiteralTemplate(
      language,
      '{labels} are not available; numeric fallback labels will be shown.',
      {
        labels: translateLiteralBodyForLanguage(language, fallbackLabelsUnavailableMatch[1])
      }
    );
  }

  const tableReadDecodeMatch = /^(.+) table could not be (decoded|read): (.+)$/.exec(literal);
  if (tableReadDecodeMatch) {
    return tableReadDecodeMatch[2] === 'decoded'
      ? formatLiteralTemplate(language, '{table} table could not be decoded: {error}', {
          table: translateLiteralBodyForLanguage(language, tableReadDecodeMatch[1]),
          error: tableReadDecodeMatch[3]
        })
      : formatLiteralTemplate(language, '{table} table could not be read: {error}', {
          table: translateLiteralBodyForLanguage(language, tableReadDecodeMatch[1]),
          error: tableReadDecodeMatch[3]
        });
  }

  const workflowRequiresPathsMatch =
    /^(.+) requires valid base RomFS and base ExeFS paths before it can load\.$/.exec(literal);
  if (workflowRequiresPathsMatch) {
    return formatLiteralTemplate(
      language,
      '{workflow} requires valid base RomFS and base ExeFS paths before it can load.',
      {
        workflow: translateLiteralBodyForLanguage(language, workflowRequiresPathsMatch[1])
      }
    );
  }

  const workflowUnavailableMatch = /^(.+) is not available for this project\.$/.exec(literal);
  if (workflowUnavailableMatch) {
    return formatLiteralTemplate(language, '{workflow} is not available for this project.', {
      workflow: translateLiteralBodyForLanguage(language, workflowUnavailableMatch[1])
    });
  }

  const changePlanPreviewMatch = /^Change plan preview contains (\d+) target files?\.$/.exec(
    literal
  );
  if (changePlanPreviewMatch) {
    return changePlanPreviewMatch[1] === '1'
      ? formatLiteralTemplate(language, 'Change plan preview contains {count} target file.', {
          count: changePlanPreviewMatch[1]
        })
      : formatLiteralTemplate(language, 'Change plan preview contains {count} target files.', {
          count: changePlanPreviewMatch[1]
        });
  }

  const stagedForReviewMatch = /^(.+) (is|are) staged for change-plan review\.$/.exec(literal);
  if (stagedForReviewMatch) {
    const subject = translateLiteralBodyForLanguage(language, stagedForReviewMatch[1]);
    return stagedForReviewMatch[2] === 'is'
      ? formatLiteralTemplate(
          language,
          '{subject} is staged for change-plan review.',
          { subject }
        )
      : formatLiteralTemplate(language, '{subject} are staged for change-plan review.', {
          subject
        });
  }

  const installedStatusMatch = /^(.+) is (installed|not installed)\.$/.exec(literal);
  if (installedStatusMatch) {
    const subject = translateLiteralBodyForLanguage(language, installedStatusMatch[1]);
    return installedStatusMatch[2] === 'installed'
      ? formatLiteralTemplate(language, '{subject} is installed.', { subject })
      : formatLiteralTemplate(language, '{subject} is not installed.', { subject });
  }

  const installedOutputFilesMatch = /^(.+) installed ([\d,]+) output file\(s\)\.$/.exec(
    literal
  );
  if (installedOutputFilesMatch) {
    return formatLiteralTemplate(
      language,
      '{subject} installed {count} output file(s).',
      {
        subject: translateLiteralBodyForLanguage(language, installedOutputFilesMatch[1]),
        count: installedOutputFilesMatch[2]
      }
    );
  }

  const uninstalledOutputFilesMatch =
    /^(.+) uninstalled ([\d,]+) owned output file\(s\)\.$/.exec(literal);
  if (uninstalledOutputFilesMatch) {
    return formatLiteralTemplate(
      language,
      '{subject} uninstalled {count} owned output file(s).',
      {
        subject: translateLiteralBodyForLanguage(language, uninstalledOutputFilesMatch[1]),
        count: uninstalledOutputFilesMatch[2]
      }
    );
  }

  const selectedGameMismatchMatch =
    /^Selected (Pokemon Sword|Pokemon Shield|Pokemon Scarlet|Pokemon Violet), but (Base RomFS|Base ExeFS|Output root folder) contains (Pokemon Sword|Pokemon Shield|Pokemon Scarlet|Pokemon Violet) title id (0x[0-9A-Fa-f]+)\.$/.exec(
      literal
  );
  if (selectedGameMismatchMatch) {
    return formatLiteralTemplate(
      language,
      'Selected {selectedGame}, but {source} contains {actualGame} title id {titleId}.',
      {
        selectedGame: translateLiteralBodyForLanguage(language, selectedGameMismatchMatch[1]),
        source: translateLiteralBodyForLanguage(language, selectedGameMismatchMatch[2]),
        actualGame: translateLiteralBodyForLanguage(language, selectedGameMismatchMatch[3]),
        titleId: selectedGameMismatchMatch[4]
      }
    );
  }

  const expectedActualMatch = /^(.+): expected (.+), actual (.+)\.$/.exec(literal);
  if (expectedActualMatch) {
    return formatLiteralTemplate(language, '{subject}: expected {expected}, actual {actual}.', {
      subject: translateLiteralBodyForLanguage(language, expectedActualMatch[1]),
      expected: expectedActualMatch[2],
      actual: expectedActualMatch[3]
    });
  }

  const dataSourceErrorMatch = /^(.+) (is not supported|could not be read): (.+)$/.exec(literal);
  if (dataSourceErrorMatch) {
    return dataSourceErrorMatch[2] === 'is not supported'
      ? formatLiteralTemplate(language, '{source} is not supported: {error}', {
          source: translateLiteralBodyForLanguage(language, dataSourceErrorMatch[1]),
          error: dataSourceErrorMatch[3]
        })
      : formatLiteralTemplate(language, '{source} could not be read: {error}', {
          source: translateLiteralBodyForLanguage(language, dataSourceErrorMatch[1]),
          error: dataSourceErrorMatch[3]
        });
  }

  const namedLoadFailureMatch = /^(.+) table '(.+)' could not be loaded: (.+)$/.exec(literal);
  if (namedLoadFailureMatch) {
    return formatLiteralTemplate(language, "{table} table '{name}' could not be loaded: {error}", {
      table: translateLiteralBodyForLanguage(language, namedLoadFailureMatch[1]),
      name: namedLoadFailureMatch[2],
      error: namedLoadFailureMatch[3]
    });
  }

  const loadFailureMatch = /^(.+) could not be loaded: (.+)$/.exec(literal);
  if (loadFailureMatch) {
    return formatLiteralTemplate(language, '{subject} could not be loaded: {error}', {
      subject: translateLiteralBodyForLanguage(language, loadFailureMatch[1]),
      error: loadFailureMatch[2]
    });
  }

  const abilityResolveFailureMatch =
    /^(.+) ability names could not be resolved from Pokemon Data: (.+)$/.exec(literal);
  if (abilityResolveFailureMatch) {
    return formatLiteralTemplate(
      language,
      '{subject} ability names could not be resolved from Pokemon Data: {error}',
      {
        subject: translateLiteralBodyForLanguage(language, abilityResolveFailureMatch[1]),
        error: abilityResolveFailureMatch[2]
      }
    );
  }

  const showOptionsMatch = /^Show (.+) options$/.exec(literal);
  if (showOptionsMatch) {
    return formatLiteralTemplate(language, 'Show {target} options', {
      target: translateLiteralBodyForLanguage(language, showOptionsMatch[1])
    });
  }

  const wikiForMatch = /^Go to Wiki for (.+)$/.exec(literal);
  if (wikiForMatch) {
    return formatLiteralTemplate(language, 'Go to Wiki for {target}', {
      target: translateLiteralBodyForLanguage(language, wikiForMatch[1])
    });
  }

  const pendingCountMatch = /^(\d+) pending changes$/.exec(literal);
  if (pendingCountMatch) {
    return pendingCountMatch[1] === '1'
      ? formatLiteralTemplate(language, '{count} pending change', { count: pendingCountMatch[1] })
      : formatLiteralTemplate(language, '{count} pending changes', {
          count: pendingCountMatch[1]
        });
  }

  const progressLabelMatch = /^(.+) progress$/.exec(literal);
  if (progressLabelMatch) {
    return formatLiteralTemplate(language, '{target} progress', {
      target: translateLiteralBodyForLanguage(language, progressLabelMatch[1])
    });
  }

  const typeList = translateSlashSeparatedTypeList(language, literal);
  if (typeList) {
    return typeList;
  }

  const updateReadyMatch = /^KM Editor v(.+) is ready to install\.$/.exec(literal);
  if (updateReadyMatch) {
    return formatLiteralTemplate(language, 'KM Editor v{version} is ready to install.', {
      version: updateReadyMatch[1]
    });
  }

  const updateAvailableMatch = /^KM Editor v(.+) is available on GitHub\.$/.exec(literal);
  if (updateAvailableMatch) {
    return formatLiteralTemplate(language, 'KM Editor v{version} is available on GitHub.', {
      version: updateAvailableMatch[1]
    });
  }

  const updateCurrentMatch = /^KM Editor v(.+) is up to date\.$/.exec(literal);
  if (updateCurrentMatch) {
    return formatLiteralTemplate(language, 'KM Editor v{version} is up to date.', {
      version: updateCurrentMatch[1]
    });
  }

  const updateDownloadWithSizeMatch = /^Downloading KM Editor v(.+) \((.+)\)\.$/.exec(literal);
  if (updateDownloadWithSizeMatch) {
    return formatLiteralTemplate(language, 'Downloading KM Editor v{version} ({size}).', {
      version: updateDownloadWithSizeMatch[1],
      size: updateDownloadWithSizeMatch[2]
    });
  }

  const updateDownloadingMatch = /^Downloading KM Editor v(.+)\.$/.exec(literal);
  if (updateDownloadingMatch) {
    return formatLiteralTemplate(language, 'Downloading KM Editor v{version}.', {
      version: updateDownloadingMatch[1]
    });
  }

  const updateProgressMatch = /^Downloading update \((.+) of (.+)\)\.$/.exec(literal);
  if (updateProgressMatch) {
    return formatLiteralTemplate(language, 'Downloading update ({current} of {total}).', {
      current: updateProgressMatch[1],
      total: updateProgressMatch[2]
    });
  }

  const updateProgressNoTotalMatch = /^Downloading update \((.+)\)\.$/.exec(literal);
  if (updateProgressNoTotalMatch) {
    return formatLiteralTemplate(language, 'Downloading update ({current}).', {
      current: updateProgressNoTotalMatch[1]
    });
  }

  return literal;
}

function translateEditableFieldHelpPrefix(language: LanguageCode, prefix: string) {
  let separatorIndex = prefix.indexOf('. ');
  while (separatorIndex >= 0) {
    const label = prefix.slice(0, separatorIndex);
    const help = `${prefix.slice(separatorIndex + 2)}.`;
    const translatedHelp =
      resourcesByLanguage[language].literals[help] ??
      resourcesByLanguage.en.literals[help];

    if (translatedHelp) {
      return `${translateLiteralBodyForLanguage(language, label)}. ${translatedHelp}`;
    }

    separatorIndex = prefix.indexOf('. ', separatorIndex + 2);
  }

  return translateLiteralBodyForLanguage(language, prefix);
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

function formatLiteralTemplate(
  language: LanguageCode,
  template: string,
  params?: LocalizationParams
) {
  const translated =
    resourcesByLanguage[language].literals[template] ??
    resourcesByLanguage.en.literals[template] ??
    template;

  return interpolateLocalizationParams(translated, params);
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
  return (
    value === 'en' ||
    value === 'es' ||
    value === 'fr' ||
    value === 'de' ||
    value === 'ru' ||
    value === 'uk' ||
    value === 'zh'
  );
}

function warnMissingKey(language: LanguageCode, key: string, translated: string) {
  if (language === 'en' || translated !== key || !isLocalizationDebugEnabled()) {
    return;
  }

  const warningKey = `${language}:${key}`;
  if (!missingKeyWarnings.has(warningKey)) {
    rememberLocalizationWarning(missingKeyWarnings, warningKey);
    console.warn(`Missing ${language} localization key: ${key}`);
  }
}

function warnMissingLiteral(language: LanguageCode, literal: string, translated: string) {
  if (language === 'en' || translated !== literal || !isLocalizationDebugEnabled()) {
    return;
  }

  const warningKey = `${language}:${literal}`;
  if (!missingLiteralWarnings.has(warningKey)) {
    rememberLocalizationWarning(missingLiteralWarnings, warningKey);
    console.warn(`Missing ${language} localization literal: ${literal}`);
  }
}

function rememberLocalizationWarning(warnings: Set<string>, warning: string) {
  if (warnings.size >= maximumRememberedLocalizationWarnings) {
    const oldestWarning = warnings.values().next().value;
    if (oldestWarning !== undefined) {
      warnings.delete(oldestWarning);
    }
  }

  warnings.add(warning);
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
