// SPDX-License-Identifier: GPL-3.0-only

import { readdirSync, readFileSync } from 'node:fs';
import { extname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import {
  formatSearchableOptionValue,
  getSmartOptionMatches,
  resolveSearchableOptionCommit,
  transitionSearchableOptionInteraction
} from '../src/components/searchableOptionInputState.ts';

const stylesPath = fileURLToPath(new URL('../src/styles.css', import.meta.url));
const sourceRoot = fileURLToPath(new URL('../src/', import.meta.url));

const visibleInputTypes = new Set([
  'button',
  'checkbox',
  'color',
  'date',
  'datetime',
  'datetime-local',
  'email',
  'file',
  'hidden',
  'month',
  'number',
  'password',
  'radio',
  'range',
  'reset',
  'search',
  'submit',
  'tel',
  'text',
  'time',
  'url',
  'week'
]);

function normalize(value) {
  return value.replace(/\/\*[\s\S]*?\*\//gu, '').replace(/\s+/gu, ' ').trim();
}

function findClosingBrace(css, openingBrace) {
  let depth = 1;
  let quote = '';

  for (let index = openingBrace + 1; index < css.length; index += 1) {
    const character = css[index];
    if (quote !== '') {
      if (character === '\\') index += 1;
      else if (character === quote) quote = '';
      continue;
    }

    if (character === '\'' || character === '"') quote = character;
    else if (character === '{') depth += 1;
    else if (character === '}') {
      depth -= 1;
      if (depth === 0) return index;
    }
  }

  throw new Error('CSS contract audit found an unclosed block.');
}

function readRules(css, context = [], start = 0, end = css.length) {
  const rules = [];
  let cursor = start;

  while (cursor < end) {
    while (cursor < end && /[\s;]/u.test(css[cursor] ?? '')) cursor += 1;
    if (cursor >= end) break;

    let quote = '';
    let boundary = cursor;
    for (; boundary < end; boundary += 1) {
      const character = css[boundary];
      if (quote !== '') {
        if (character === '\\') boundary += 1;
        else if (character === quote) quote = '';
        continue;
      }

      if (character === '\'' || character === '"') quote = character;
      else if (character === '{' || character === ';') break;
    }

    if (boundary >= end) break;
    if (css[boundary] === ';') {
      cursor = boundary + 1;
      continue;
    }

    const header = normalize(css.slice(cursor, boundary));
    const closingBrace = findClosingBrace(css, boundary);
    if (header.startsWith('@')) {
      if (/^@(container|layer|media|scope|supports)\b/u.test(header)) {
        rules.push(...readRules(css, [...context, header], boundary + 1, closingBrace));
      }
    } else if (header !== '') {
      rules.push({
        body: normalize(css.slice(boundary + 1, closingBrace)),
        context,
        selector: header
      });
    }

    cursor = closingBrace + 1;
  }

  return rules;
}

function cssFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return cssFiles(path);
    return entry.isFile() && entry.name.endsWith('.css') ? [path] : [];
  });
}

function sourceFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return sourceFiles(path);
    return entry.isFile() && ['.jsx', '.tsx'].includes(extname(entry.name)) ? [path] : [];
  });
}

function isForcedColorsRule(rule) {
  return rule.context.some((entry) => (
    /^@media\b/u.test(entry) && /\(\s*forced-colors\s*:\s*active\s*\)/u.test(entry)
  ));
}

function selectorContainsElement(selector, element) {
  const pattern = new RegExp(
    `(?:^|[\\s,>+~(:])${element}(?=$|[\\s,>+~.#:\\[\\])])`,
    'u'
  );
  return pattern.test(selector);
}

function isVisibleControlSelector(selector) {
  return ['button', 'input', 'select', 'textarea'].some((element) => (
    selectorContainsElement(selector, element)
  ));
}

function splitSelectorBranches(selector) {
  const branches = [];
  let bracketDepth = 0;
  let parenthesisDepth = 0;
  let start = 0;

  for (let index = 0; index < selector.length; index += 1) {
    const character = selector[index];
    if (character === '[') bracketDepth += 1;
    else if (character === ']') bracketDepth -= 1;
    else if (character === '(') parenthesisDepth += 1;
    else if (character === ')') parenthesisDepth -= 1;
    else if (character === ',' && bracketDepth === 0 && parenthesisDepth === 0) {
      branches.push(selector.slice(start, index).trim());
      start = index + 1;
    }
  }

  branches.push(selector.slice(start).trim());
  return branches;
}

function finalCompoundSelector(branch) {
  let bracketDepth = 0;
  let parenthesisDepth = 0;
  let start = 0;

  for (let index = 0; index < branch.length; index += 1) {
    const character = branch[index];
    if (character === '[') bracketDepth += 1;
    else if (character === ']') bracketDepth -= 1;
    else if (character === '(') parenthesisDepth += 1;
    else if (character === ')') parenthesisDepth -= 1;
    else if (bracketDepth === 0 && parenthesisDepth === 0 && /[\s>+~]/u.test(character ?? '')) {
      while (index + 1 < branch.length && /[\s>+~]/u.test(branch[index + 1] ?? '')) index += 1;
      start = index + 1;
    }
  }

  return branch.slice(start).trim();
}

function isFieldSelector(selector) {
  return splitSelectorBranches(selector).some((branch) => {
    const subject = finalCompoundSelector(branch);
    if (selectorContainsElement(subject, 'select') || selectorContainsElement(subject, 'textarea')) {
      return true;
    }
    if (!selectorContainsElement(subject, 'input')) return false;

    const withoutNonFieldInputs = subject.replace(
      /input\s*\[\s*type\s*=\s*(['"]?)(?:button|hidden|image|reset|submit)\1\s*\]/gu,
      ''
    );
    return selectorContainsElement(withoutNonFieldInputs, 'input');
  });
}

function declarations(body) {
  return [...body.matchAll(/(?:^|;)\s*([\w-]+)\s*:\s*([^;}]*)/gu)].map((match) => ({
    property: (match[1] ?? '').toLowerCase(),
    value: normalize(match[2] ?? '')
  }));
}

function hasHardcodedMidGray(value) {
  if (/\b(?:darkgray|darkgrey|dimgray|dimgrey|gainsboro|gray|grey|lightgray|lightgrey|silver)\b/iu.test(value)) {
    return true;
  }

  for (const match of value.matchAll(/#([\da-f]{3,4}|[\da-f]{6}|[\da-f]{8})(?![\da-f])/giu)) {
    const literal = match[1] ?? '';
    const channels = literal.length <= 4
      ? [...literal.slice(0, 3)].map((channel) => Number.parseInt(channel.repeat(2), 16))
      : [literal.slice(0, 2), literal.slice(2, 4), literal.slice(4, 6)].map((channel) => (
          Number.parseInt(channel, 16)
        ));
    if (channels[0] === channels[1] && channels[1] === channels[2] && ![0, 255].includes(channels[0])) {
      return true;
    }
  }

  for (const match of value.matchAll(/rgba?\(\s*(\d+)\s*[, ]\s*(\d+)\s*[, ]\s*(\d+)/giu)) {
    const channels = [match[1], match[2], match[3]].map(Number);
    if (channels[0] === channels[1] && channels[1] === channels[2] && ![0, 255].includes(channels[0])) {
      return true;
    }
  }

  return /hsla?\([^)]*\b0(?:\.0+)?%\s+([1-9]\d?(?:\.\d+)?)%/iu.test(value);
}

function hasHardcodedColorLiteral(value) {
  return /#(?:[\da-f]{3,4}|[\da-f]{6}|[\da-f]{8})(?![\da-f])/iu.test(value)
    || /\b(?:rgb|rgba|hsl|hsla)\(\s*[+-.]?\d/iu.test(value)
    || /\b(?:aqua|black|blue|brown|cyan|fuchsia|green|lime|magenta|maroon|navy|olive|orange|pink|purple|red|teal|white|yellow)\b/iu.test(value)
    || hasHardcodedMidGray(value);
}

function isColorBearingProperty(property) {
  return property === 'box-shadow'
    || property === 'color'
    || property === 'fill'
    || property === 'stroke'
    || property === 'text-shadow'
    || property.endsWith('-color')
    || property.startsWith('background')
    || property.startsWith('border')
    || property.startsWith('outline');
}

function requireRule(rules, name, selectorTokens, declarationTokens, options = {}) {
  const forcedColors = options.forcedColors ?? false;
  const matchingRules = rules.filter((rule) => (
    isForcedColorsRule(rule) === forcedColors
      && selectorTokens.every((token) => rule.selector.includes(token))
  ));

  if (matchingRules.length === 0) {
    throw new Error(
      `KM control theme contract is missing ${name}. Expected selector tokens: ${selectorTokens.join(', ')}`
    );
  }

  const combinedBody = matchingRules.map(({ body }) => body).join('; ');
  const missingDeclarations = declarationTokens.filter((token) => !combinedBody.includes(token));
  if (missingDeclarations.length > 0) {
    throw new Error(
      `KM control theme contract ${name} is missing declarations: ${missingDeclarations.join(', ')}`
    );
  }
}

function checkCssSafety() {
  const violations = [];
  for (const path of cssFiles(sourceRoot)) {
    const rules = readRules(readFileSync(path, 'utf8'));
    for (const rule of rules) {
      if (!isVisibleControlSelector(rule.selector) || isForcedColorsRule(rule)) continue;

      for (const declaration of declarations(rule.body)) {
        if (
          ['appearance', '-moz-appearance', '-webkit-appearance'].includes(declaration.property)
          && declaration.value !== 'none'
          && !(declaration.value === 'textfield' && rule.selector.includes("input[type='number']"))
        ) {
          violations.push(
            `${relative(sourceRoot, path)} restores a native control appearance outside forced-colors: ${rule.selector}`
          );
        }

        if (declaration.property === 'background' && isFieldSelector(rule.selector)) {
          violations.push(
            `${relative(sourceRoot, path)} uses background shorthand on a field and can erase a KM affordance: ${rule.selector}`
          );
        }

        if (
          declaration.property === 'background-image'
          && declaration.value === 'none'
          && selectorContainsElement(rule.selector, 'select')
          && !rule.selector.includes('select[multiple]')
          && !rule.selector.includes("select[size]:not([size='1'])")
        ) {
          violations.push(
            `${relative(sourceRoot, path)} removes the shared select arrow outside the multiple/select-size exception: ${rule.selector}`
          );
        }

        if (
          isFieldSelector(rule.selector)
          && isColorBearingProperty(declaration.property)
          && hasHardcodedColorLiteral(declaration.value)
        ) {
          violations.push(
            `${relative(sourceRoot, path)} hardcodes a color on a field instead of using KM theme tokens: ${rule.selector}`
          );
        } else if (
          ['background', 'background-color', 'border', 'border-color', 'box-shadow', 'color', 'fill', 'outline-color', 'stroke'].includes(declaration.property)
          && hasHardcodedMidGray(declaration.value)
        ) {
          violations.push(
            `${relative(sourceRoot, path)} hardcodes a gray platform-like color on a visible control: ${rule.selector}`
          );
        }

        if (/\b(?:ButtonFace|ButtonText|Canvas|CanvasText|Field|FieldText)\b/u.test(declaration.value)) {
          violations.push(
            `${relative(sourceRoot, path)} uses a platform color outside forced-colors: ${rule.selector}`
          );
        }
      }
    }
  }

  if (violations.length > 0) {
    throw new Error(`KM control CSS safety audit failed:\n- ${violations.join('\n- ')}`);
  }
}

function literalInputTypes(expression) {
  if (ts.isStringLiteral(expression)) return new Set([expression.text]);
  if (ts.isParenthesizedExpression(expression)) return literalInputTypes(expression.expression);
  if (ts.isConditionalExpression(expression)) {
    const whenTrue = literalInputTypes(expression.whenTrue);
    const whenFalse = literalInputTypes(expression.whenFalse);
    if (whenTrue === undefined || whenFalse === undefined) return undefined;
    return new Set([...whenTrue, ...whenFalse]);
  }
  return undefined;
}

function findFunctionLike(sourceFile, name) {
  let result;
  const visit = (node) => {
    if (
      (ts.isFunctionDeclaration(node) && node.name?.text === name)
      || (
        ts.isVariableDeclaration(node)
        && ts.isIdentifier(node.name)
        && node.name.text === name
        && node.initializer
        && (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer))
      )
    ) {
      result = ts.isVariableDeclaration(node) ? node.initializer : node;
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  if (result === undefined) {
    throw new Error(`KM control theme contract could not find ${name}.`);
  }
  return result;
}

function descendants(node, predicate) {
  const matches = [];
  const visit = (candidate) => {
    if (predicate(candidate)) matches.push(candidate);
    ts.forEachChild(candidate, visit);
  };
  visit(node);
  return matches;
}

function hasTruthyJsxAttribute(node, sourceFile, attributeName) {
  const attribute = node.attributes.properties.find(
    (candidate) =>
      ts.isJsxAttribute(candidate)
      && candidate.name.getText(sourceFile) === attributeName
  );
  if (!attribute || !ts.isJsxAttribute(attribute)) {
    return false;
  }
  if (attribute.initializer === undefined) {
    return true;
  }
  return (
    ts.isJsxExpression(attribute.initializer)
    && attribute.initializer.expression?.kind === ts.SyntaxKind.TrueKeyword
  );
}

function findJsxAttribute(node, sourceFile, attributeName) {
  return node.attributes.properties.find(
    (candidate) =>
      ts.isJsxAttribute(candidate)
      && candidate.name.getText(sourceFile) === attributeName
  );
}

function checkMovesCustomSelectMarkup() {
  const appPath = join(sourceRoot, 'App.tsx');
  const appSource = ts.createSourceFile(
    appPath,
    readFileSync(appPath, 'utf8'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const selectedMovePanel = findFunctionLike(appSource, 'SelectedMovePanel');
  const selectedMovePanelSource = selectedMovePanel.getText(appSource);
  const nativeSelects = descendants(
    selectedMovePanel,
    (node) =>
      (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node))
      && node.tagName.getText(appSource) === 'select'
  );
  if (nativeSelects.length !== 0) {
    throw new Error('The Moves editor must use KM listboxes instead of native select popups.');
  }

  for (const id of ['move-runtime-variant', 'move-timing-profile', 'move-timing-occurrence']) {
    const matchingControl = descendants(
      selectedMovePanel,
      (node) =>
        ts.isJsxSelfClosingElement(node)
        && node.tagName.getText(appSource) === 'SearchableOptionInput'
        && node.getText(appSource).includes(`id="${id}"`)
    );
    if (
      matchingControl.length !== 1
      || !hasTruthyJsxAttribute(matchingControl[0], appSource, 'isFiniteCatalog')
    ) {
      throw new Error(`${id} must use one finite-catalog KM searchable option control.`);
    }
  }

  if (selectedMovePanelSource.includes('useSearchableBooleanInput')) {
    throw new Error('Move boolean fields must not require a scoped KM selector opt-in.');
  }

  const draftFieldNode = findFunctionLike(appSource, 'GiftPokemonDraftField');
  const draftField = draftFieldNode.getText(appSource);
  const booleanControls = descendants(
    draftFieldNode,
    (node) =>
      ts.isJsxSelfClosingElement(node)
      && node.tagName.getText(appSource) === 'SearchableOptionInput'
      && node.getText(appSource).includes('options={searchableBooleanOptions}')
  );
  const nativeBooleanControls = descendants(
    draftFieldNode,
    (node) =>
      (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node))
      && node.tagName.getText(appSource) === 'select'
  );
  if (
    !draftField.includes("field.valueKind === 'boolean' ?")
    || booleanControls.length !== 1
    || nativeBooleanControls.length !== 0
    || !hasTruthyJsxAttribute(booleanControls[0], appSource, 'isFiniteCatalog')
  ) {
    throw new Error('Every shared Pokemon boolean path must use finite-catalog KM Yes/No options.');
  }
}

function checkFiniteSearchableOptionContract() {
  const options = [
    { label: 'Yes', value: 1 },
    { label: 'No', value: 0 }
  ];
  if (resolveSearchableOptionCommit('1', options, undefined, true) !== '1') {
    throw new Error('Finite KM catalogs must accept an exact catalog value.');
  }
  if (resolveSearchableOptionCommit('Yes', options, undefined, true) !== '1') {
    throw new Error('Finite KM catalogs must accept an exact catalog label.');
  }
  if (resolveSearchableOptionCommit('2', options, undefined, true) !== null) {
    throw new Error('Finite KM catalogs must reject an unknown numeric value.');
  }
  if (resolveSearchableOptionCommit('2', options) !== '2') {
    throw new Error('Open KM catalogs must preserve their existing raw numeric fallback.');
  }

  const stringOptions = [
    { label: 'Fixed rewards', value: 'fixed' },
    { label: 'Lottery rewards', value: 'lottery' }
  ];
  if (resolveSearchableOptionCommit('lottery', stringOptions, undefined, true) !== 'lottery') {
    throw new Error('Finite KM catalogs must accept an exact string catalog value.');
  }
  if (
    resolveSearchableOptionCommit('Fixed rewards', stringOptions, undefined, true) !== 'fixed'
  ) {
    throw new Error('Finite KM catalogs must resolve a string-valued option by its exact label.');
  }
  if (resolveSearchableOptionCommit('unknown', stringOptions, undefined, true) !== null) {
    throw new Error('Finite KM catalogs must reject an unknown string value.');
  }
  if (
    resolveSearchableOptionCommit('Shared name', [
      { label: 'Shared name', value: 10 },
      { inputLabel: 'Shared name', label: 'Different detail', value: 11 }
    ], undefined, true) !== null
  ) {
    throw new Error('KM catalogs must not commit a visible label shared by distinct values.');
  }
  if (
    resolveSearchableOptionCommit('Shared name', [
      { label: 'Shared name', value: 10 },
      { inputLabel: 'Shared name', label: 'Different detail', value: 10 }
    ], undefined, true) !== '10'
  ) {
    throw new Error('A duplicated KM label remains valid when it identifies one semantic value.');
  }
  if (
    resolveSearchableOptionCommit('10', [
      { label: '10', value: 12 },
      { label: 'Different detail', value: 10 }
    ], undefined, true) !== '10'
  ) {
    throw new Error('An exact KM semantic value must take precedence over a visible label.');
  }

  const compactOption = { inputLabel: '2×', label: 'Super effective', value: 2 };
  if (resolveSearchableOptionCommit('2×', [compactOption], undefined, true) !== '2') {
    throw new Error('Finite KM catalogs must resolve an exact compact input label.');
  }
  if (formatSearchableOptionValue('2', [compactOption]) !== '2×') {
    throw new Error('KM catalogs must display a declared compact input label for their value.');
  }
  const aliasedOption = {
    label: 'Moomoo Milk',
    searchAliases: ['Medicine'],
    value: 33
  };
  if (getSmartOptionMatches('medicine', [aliasedOption])[0] !== aliasedOption) {
    throw new Error('KM catalogs must include declared aliases in option search.');
  }
  if (
    resolveSearchableOptionCommit('medicine', [
      aliasedOption,
      { label: 'Potion', searchAliases: ['Medicine'], value: 17 }
    ], undefined, true) !== null
  ) {
    throw new Error('KM catalogs must not commit an alias shared by multiple options.');
  }
  if (
    getSmartOptionMatches('moom med', [
      aliasedOption,
      { label: 'Potion', searchAliases: ['Medicine'], value: 17 }
    ])[0] !== aliasedOption
  ) {
    throw new Error('KM catalog search must apply every query token across labels and aliases.');
  }

  const rejectedCommit = transitionSearchableOptionInteraction(
    { hasUserQuery: true, isOpen: true, query: '2' },
    {
      committedValue: '0',
      formattedValue: 'No',
      isFiniteCatalog: true,
      options,
      type: 'commit'
    }
  );
  if (
    rejectedCommit.sourceCommit !== null
    || rejectedCommit.state.hasUserQuery
    || rejectedCommit.state.isOpen
    || rejectedCommit.state.query !== 'No'
  ) {
    throw new Error('A rejected finite-catalog value must restore the committed KM selection.');
  }
}

function checkSearchableOptionComponentContract() {
  const component = readFileSync(join(sourceRoot, 'components', 'SearchableOptionInput.tsx'), 'utf8');
  const requirements = [
    [
      /const selectOption = [\s\S]*?if \(option\.disabled\) \{[\s\S]*?return;/u,
      'Disabled KM options must reject direct selection.'
    ],
    [
      /if \(!item \|\| !isEnabledMenuItem\(item\)\) \{[\s\S]*?return;/u,
      'Disabled KM options must reject menu selection.'
    ],
    [
      /const enabledOptions = localizedOptions\.filter\(\(option\) => !option\.disabled\);/u,
      'Typed finite-catalog commits must exclude disabled KM options.'
    ],
    [
      /nextEnabledMenuItemIndex\([\s\S]*?isEnabledMenuItem\(items\[candidateIndex\]\)/u,
      'KM option keyboard navigation must skip disabled options.'
    ],
    [
      /aria-disabled=\{isDisabled \|\| undefined\}[\s\S]*?disabled=\{isDisabled\}/u,
      'Disabled KM options must expose both semantic and native disabled state.'
    ],
    [
      /item\.option\.groupLabel[\s\S]*?<small>\{item\.option\.groupLabel\}<\/small>/u,
      'Grouped KM options must render their group label.'
    ],
    [
      /localizationIgnore \?\? \(localizeOptions \? undefined : 'true'\)/u,
      'Prelocalized or dynamic KM option labels must opt out of observer translation.'
    ],
    [
      /onMouseDown=\{\(event\) => \{\s*event\.preventDefault\(\);\s*\}\}\s*onClick=\{\(\) => \{\s*selectMenuItem\(index\);/u,
      'KM option rows must preserve input focus on pointer down and activate on semantic click.'
    ],
    [
      /className="searchable-option-toggle"[\s\S]*?onMouseDown=\{\(event\) => \{\s*event\.preventDefault\(\);\s*\}\}\s*onClick=\{\(\) => \{/u,
      'The KM option toggle must activate on semantic click, not pointer down alone.'
    ],
    [
      /name=\{name\}[\s\S]*?onClick=\{\(\) => \{\s*if \(!isOpen\)[\s\S]*?\{ formattedValue, type: 'focus' \}/u,
      'A focused KM option input must reopen its menu on semantic click.'
    ],
    [
      /const inputTooltipText = hasUserQuery\s*\? undefined\s*:\s*tooltipContent \?\? \(formattedValue \|\| undefined\);/u,
      'KM option inputs must preserve an editor-provided field tooltip when idle.'
    ],
    [
      /maximumVisibleOptions\?: number;[\s\S]*?const visibleMatches = matches\.slice\(0, maximumVisibleOptions\);[\s\S]*?if \(!hasUserQuery\)[\s\S]*?visibleMatches\[visibleMatches\.length - 1\] = selectedOption;/u,
      'Large KM catalogs must cap rendered rows without losing the committed selection.'
    ],
    [
      /portalMenu = true[\s\S]*?position: 'fixed'[\s\S]*?createPortal\(menu, portalHost\)/u,
      'KM option menus must use viewport-aware portal placement by default while retaining the portalMenu opt-out.'
    ],
    [
      /data-value=\{item\.kind === 'empty' \? '' : item\.option\.value\.toString\(\)\}/u,
      'KM option rows must expose semantic values independently from rich display text.'
    ],
    [
      /data-value=\{value\}/u,
      'KM comboboxes must expose their committed semantic value independently from display text.'
    ]
  ];

  for (const [pattern, message] of requirements) {
    if (!pattern.test(component)) {
      throw new Error(message);
    }
  }
}

function checkMigratedSearchableOptionContracts() {
  const appPath = join(sourceRoot, 'App.tsx');
  const appSource = ts.createSourceFile(
    appPath,
    readFileSync(appPath, 'utf8'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const expectedTooltips = new Map([
    ['trainer-draft-field-boolean-option', 'localizedFieldHoverText'],
    ['gift-pokemon-draft-boolean-option', 'localizedFieldHoverText'],
    ['shop-row-field-text-option', 'localizedFieldHoverText'],
    ['behavior-field-option', 'localizedBehaviorFieldHover'],
    ['pokemon-personal-field-boolean-option', 'localizedHoverText']
  ]);
  const matchedTooltips = new Set();

  for (const node of descendants(
    appSource,
    (candidate) =>
      ts.isJsxSelfClosingElement(candidate)
      && candidate.tagName.getText(appSource) === 'SearchableOptionInput'
  )) {
    const sourceSiteAttribute = findJsxAttribute(node, appSource, 'data-km-source-site');
    if (
      !sourceSiteAttribute
      || sourceSiteAttribute.initializer === undefined
      || !ts.isStringLiteral(sourceSiteAttribute.initializer)
    ) continue;

    const sourceSite = sourceSiteAttribute.initializer.text;
    const expectedTooltip = expectedTooltips.get(sourceSite);
    if (expectedTooltip === undefined) continue;

    const tooltipAttribute = findJsxAttribute(node, appSource, 'tooltipContent');
    const tooltipExpression = tooltipAttribute?.initializer;
    if (
      tooltipAttribute === undefined
      || tooltipExpression === undefined
      || !ts.isJsxExpression(tooltipExpression)
      || tooltipExpression.expression?.getText(appSource) !== expectedTooltip
    ) {
      throw new Error(`${sourceSite} must preserve its contextual field tooltip after KM selector migration.`);
    }
    matchedTooltips.add(sourceSite);
  }

  const missingTooltipSites = [...expectedTooltips.keys()].filter(
    (sourceSite) => !matchedTooltips.has(sourceSite)
  );
  if (missingTooltipSites.length > 0) {
    throw new Error(
      `KM selector tooltip coverage is missing source sites: ${missingTooltipSites.join(', ')}`
    );
  }

  const npcPath = join(sourceRoot, 'features', 'npc-item-gift', 'NpcItemGiftSection.tsx');
  const npcSource = ts.createSourceFile(
    npcPath,
    readFileSync(npcPath, 'utf8'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const npcPicker = findFunctionLike(npcSource, 'NpcItemGiftItemPicker');
  const npcPickerText = npcPicker.getText(npcSource);
  const npcControls = descendants(
    npcPicker,
    (node) =>
      ts.isJsxSelfClosingElement(node)
      && node.tagName.getText(npcSource) === 'SearchableOptionInput'
  );
  if (
    npcControls.length !== 1
    || !hasTruthyJsxAttribute(npcControls[0], npcSource, 'isFiniteCatalog')
    || !npcPickerText.includes('data-km-source-site="npc-item-gift-item-picker"')
    || !npcPickerText.includes('localizeOptions={false}')
    || !npcPickerText.includes('disabled: option.isUnavailable')
    || !npcPickerText.includes('searchAliases: [option.name, option.category]')
  ) {
    throw new Error(
      'NPC Item Gift must use one finite-catalog KM selector with disabled and searchable option metadata.'
    );
  }

  const battleCafePath = join(
    sourceRoot,
    'features',
    'battle-cafe-rewards',
    'BattleCafeRewardsSection.tsx'
  );
  const battleCafeSource = ts.createSourceFile(
    battleCafePath,
    readFileSync(battleCafePath, 'utf8'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const battleCafePicker = findFunctionLike(
    battleCafeSource,
    'BattleCafeItemPicker'
  );
  const battleCafePickerText = battleCafePicker.getText(battleCafeSource);
  const battleCafeControls = descendants(
    battleCafePicker,
    (node) =>
      ts.isJsxSelfClosingElement(node)
      && node.tagName.getText(battleCafeSource) === 'SearchableOptionInput'
  );
  if (
    battleCafeControls.length !== 1
    || !hasTruthyJsxAttribute(battleCafeControls[0], battleCafeSource, 'isFiniteCatalog')
    || !battleCafePickerText.includes('data-km-source-site="battle-cafe-reward-item"')
    || !battleCafePickerText.includes('localizeOptions={false}')
    || !battleCafePickerText.includes('searchAliases: [option.category]')
    || !battleCafePickerText.includes('groupLabel: `${option.category} · #${option.itemId}`')
  ) {
    throw new Error(
      'Battle Cafe items must use one finite-catalog KM selector with searchable category and semantic item metadata.'
    );
  }

  const typeChartPath = join(sourceRoot, 'features', 'type-chart', 'TypeChartSection.tsx');
  const typeChartSource = ts.createSourceFile(
    typeChartPath,
    readFileSync(typeChartPath, 'utf8'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const typeChartCell = findFunctionLike(typeChartSource, 'TypeChartCellControl').getText(
    typeChartSource
  );
  if (
    !typeChartCell.includes('inputLabel: candidate.display')
    || typeChartCell.includes('<span aria-hidden="true">{option.display}</span>')
  ) {
    throw new Error('Type Chart cells must paint each compact effectiveness value exactly once.');
  }

  const fashionPath = join(
    sourceRoot,
    'features',
    'fashion-catalog',
    'FashionCatalogSection.tsx'
  );
  const fashionSource = ts.createSourceFile(
    fashionPath,
    readFileSync(fashionPath, 'utf8'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const fashionSection = findFunctionLike(fashionSource, 'FashionCatalogSection');
  const fashionSectionText = fashionSection.getText(fashionSource);
  const fashionValueControls = descendants(
    fashionSection,
    (node) =>
      ts.isJsxSelfClosingElement(node)
      && node.tagName.getText(fashionSource) === 'SearchableOptionInput'
      && node.getText(fashionSource).includes(
        'data-km-source-site="fashion-catalog-value"'
      )
  );
  if (fashionValueControls.length !== 1) {
    throw new Error('Fashion Catalog must expose exactly one KM value selector.');
  }
  const fashionValueControl = fashionValueControls[0];
  const maximumVisibleOptions = findJsxAttribute(
    fashionValueControl,
    fashionSource,
    'maximumVisibleOptions'
  );
  const fashionOptions = findJsxAttribute(fashionValueControl, fashionSource, 'options');
  if (
    maximumVisibleOptions?.initializer?.getText(fashionSource) !== '{optionRenderLimit}'
    || !fashionOptions?.initializer?.getText(fashionSource).startsWith('{options.map(')
    || fashionSectionText.includes('fashion-catalog-option-search')
    || fashionSectionText.includes('optionWindow')
  ) {
    throw new Error(
      'Fashion Catalog must search the complete option catalog in one capped KM selector.'
    );
  }
}

function checkSearchableOptionAffordanceRules(rules) {
  for (const [name, selector] of [
    ['trainer party selector arrow clearance', '.trainer-party-header .searchable-option-input > input'],
    ['shop inventory selector arrow clearance', '.shop-inventory-header .searchable-option-input > input'],
    ['fairy gym selector arrow clearance', '.fairy-gym-outcome-control .searchable-option-input > input'],
    ['encounter selector arrow clearance', '.encounter-slot-header .searchable-option-input > input']
  ]) {
    requireRule(rules, name, [selector], ['padding: 0 34px 0 10px']);
  }
  requireRule(
    rules,
    'fashion catalog selector arrow clearance',
    ['.fashion-catalog-field-editor .searchable-option-input > input'],
    ['padding-inline-end: 34px']
  );
  if (rules.some((rule) => rule.selector === '.fairy-gym-outcome-control span')) {
    throw new Error('Fairy Gym field-label styling must not leak into KM option-row spans.');
  }
  requireRule(
    rules,
    'fairy gym field label scope',
    ['.fairy-gym-outcome-control > .editable-field-label-row > label'],
    ['color: var(--color-text-muted)', 'text-transform: uppercase']
  );
  for (const selector of [
    '.game-dump-format-field span',
    '.learnset-row span, .learnset-row strong',
    '.exefs-row span',
    '.npc-item-gift-field small',
    '.npc-item-gift-field select, .npc-item-gift-field input'
  ]) {
    if (rules.some((rule) => rule.selector === selector)) {
      throw new Error(`${selector} must not leak legacy cell styling into KM option rows.`);
    }
  }
  requireRule(
    rules,
    'game dump field label scope',
    ['.game-dump-format-field > .editable-field-label-row > label'],
    ['font-size: 0.72rem', 'text-transform: uppercase']
  );
  requireRule(
    rules,
    'learnset direct-cell scope',
    ['.learnset-row > span', '.learnset-row > strong'],
    ['overflow: hidden', 'white-space: nowrap']
  );
  requireRule(
    rules,
    'ExeFS direct-cell scope',
    ['.exefs-row > span'],
    ['overflow: hidden', 'white-space: nowrap']
  );
  requireRule(
    rules,
    'NPC Item Gift direct-input scope',
    ['.npc-item-gift-field > input'],
    ['padding: 0 10px']
  );
  requireRule(
    rules,
    'NPC Item Gift direct-hint scope',
    ['.npc-item-gift-field > small'],
    ['font-size: 0.6875rem']
  );
  if (rules.some((rule) => rule.selector.includes('.km-settings-group button'))) {
    throw new Error('Settings action sizing must not leak into KM toggle and option-row buttons.');
  }
  requireRule(
    rules,
    'settings selector input sizing',
    ['.km-settings-grid .searchable-option-input > input'],
    ['min-height: var(--km-control-min-height)']
  );
}

function checkNativeControlMarkup() {
  for (const path of sourceFiles(sourceRoot)) {
    const text = readFileSync(path, 'utf8');
    const sourceFile = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);

    function visit(node) {
      if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
        const tagName = node.tagName.getText(sourceFile);
        if (tagName === 'SearchableOptionInput') {
          let ancestor = node.parent;
          while (ancestor !== undefined) {
            if (
              ts.isJsxElement(ancestor)
              && ancestor.openingElement.tagName.getText(sourceFile) === 'label'
            ) {
              const location = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
              throw new Error(
                `${relative(sourceRoot, path)}:${location.line + 1} nests a compound KM option control inside a wrapping label. Use an explicit label/htmlFor association.`
              );
            }
            ancestor = ancestor.parent;
          }
        }
        if (['button', 'input', 'select', 'textarea'].includes(tagName)) {
          const attributes = node.attributes.properties.filter(ts.isJsxAttribute);
          const styleAttribute = attributes.find(({ name }) => name.getText(sourceFile) === 'style');
          const location = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
          const sourceLocation = `${relative(sourceRoot, path)}:${location.line + 1}`;

          if (tagName === 'select') {
            throw new Error(
              `${sourceLocation} uses a native select. Use the shared KM option control.`
            );
          }

          if (styleAttribute !== undefined) {
            throw new Error(
              `${sourceLocation} uses inline style on <${tagName}>. Put control styling in the shared KM CSS contract.`
            );
          }

          if (tagName === 'input') {
            const typeAttribute = attributes.find(({ name }) => name.getText(sourceFile) === 'type');
            let inputTypes = new Set(['text']);
            if (typeAttribute?.initializer !== undefined) {
              if (ts.isStringLiteral(typeAttribute.initializer)) {
                inputTypes = new Set([typeAttribute.initializer.text]);
              } else if (
                ts.isJsxExpression(typeAttribute.initializer)
                && typeAttribute.initializer.expression !== undefined
              ) {
                inputTypes = literalInputTypes(typeAttribute.initializer.expression);
              } else {
                inputTypes = undefined;
              }
            }

            if (inputTypes === undefined) {
              throw new Error(
                `${sourceLocation} has a dynamic input type. Use a statically auditable literal union and add its KM theme policy first.`
              );
            }

            for (const inputType of inputTypes) {
              if (!visibleInputTypes.has(inputType)) {
                throw new Error(
                  `${sourceLocation} introduces input type "${inputType}" without an inventoried KM theme policy.`
                );
              }
            }
          }
        }
      }
      ts.forEachChild(node, visit);
    }

    visit(sourceFile);
  }
}

function checkRequiredGlobalRules(rules) {
  requireRule(
    rules,
    'shared theme compatibility tokens',
    [':root'],
    [
      '--color-text-secondary: var(--color-text-muted)',
      '--color-muted: var(--color-text-muted)',
      '--color-primary: var(--color-accent-bright)',
      '--font-mono: ui-monospace'
    ]
  );
  requireRule(
    rules,
    'all-button baseline',
    [':where(', 'button', "input[type='button']", "input[type='reset']", "input[type='submit']"],
    [
      'color: var(--color-text-soft)',
      'background-color: var(--color-control)',
      'background-image: linear-gradient(',
      'border: 1px solid var(--color-control-border)',
      'min-height: var(--km-control-min-height)',
      'cursor: pointer',
      'appearance: none'
    ]
  );
  const plainAllButtonRule = rules.find((rule) => (
    !isForcedColorsRule(rule)
    && ["input[type='button']", "input[type='reset']", "input[type='submit']"].every((token) => (
      rule.selector.includes(token)
    ))
    && /(?:^|[,(])\s*button\s*(?=,|\))/u.test(rule.selector)
  ));
  if (plainAllButtonRule === undefined) {
    throw new Error('KM control theme contract requires a low-specificity baseline for every button, including classed buttons.');
  }
  requireRule(
    rules,
    'interactive editor row overflow alignment',
    ['.interactive-table-row'],
    ['justify-content: start']
  );
  if (!rules.some((rule) => (
    !isForcedColorsRule(rule)
    && rule.selector === '.interactive-table-row'
    && rule.body.includes('justify-content: start')
  ))) {
    throw new Error(
      'KM control theme contract requires a shared interactive-table-row rule so ordinary editor row classes cannot restore centered overflow.'
    );
  }
  requireRule(
    rules,
    'text, select, and textarea baseline',
    ["input:not([type='button'])", 'select', 'textarea'],
    [
      'color: var(--color-text)',
      'background-color: var(--color-control)',
      'border: 1px solid var(--color-control-border)',
      'min-height: var(--km-control-min-height)'
    ]
  );
  requireRule(
    rules,
    'custom select arrow',
    [':where(select)'],
    [
      'appearance: none',
      'background-image:',
      'background-repeat: no-repeat',
      'background-position:',
      'padding-inline-end:'
    ]
  );
  requireRule(rules, 'textarea sizing and resizer policy', [':where(textarea)'], ['resize: vertical']);
  requireRule(
    rules,
    'themed textarea resizer',
    ['textarea', '::-webkit-resizer'],
    ['background-color: var(--color-control)', 'background-image:']
  );
  requireRule(
    rules,
    'editable control hover state',
    ["input:not([type='button'])", 'select', 'textarea', ':hover:not(:disabled, [readonly])'],
    ['background-color: var(--color-control-hover)', 'border-color: var(--color-accent-bright)']
  );
  requireRule(
    rules,
    'editable control focus state',
    ["input:not([type='button'])", 'select', 'textarea', ':focus'],
    ['border-color: var(--color-focus)', 'box-shadow: 0 0 0 3px']
  );
  requireRule(
    rules,
    'editable control disabled state',
    ["input:not([type='button'])", 'select', 'textarea', '):disabled'],
    ['color: var(--color-text-muted)', 'background-color: color-mix(in srgb, var(--color-control)', 'cursor: not-allowed']
  );
  requireRule(
    rules,
    'read-only text control state',
    [':where(input[readonly]:not(:disabled), textarea[readonly]:not(:disabled))'],
    ['background-color: var(--color-surface-muted)', 'border-style: dashed', 'cursor: default']
  );
  requireRule(
    rules,
    'choice control baseline',
    [":where(input[type='checkbox'], input[type='radio'])"],
    ['background-color: var(--color-control)', 'border: 1px solid var(--color-control-border)', 'appearance: none']
  );
  requireRule(
    rules,
    'checked choice state',
    [":where(input[type='checkbox'], input[type='radio']):checked"],
    ['background-color: var(--color-accent)', 'border-color: var(--color-accent-bright)']
  );
  requireRule(
    rules,
    'disabled choice state',
    [":where(input[type='checkbox'], input[type='radio']):disabled"],
    ['background-color: color-mix(in srgb, var(--color-control)', 'cursor: not-allowed', 'opacity: 0.58']
  );
  requireRule(
    rules,
    'range baseline',
    [":where(input[type='range'])"],
    ['appearance: none', 'background-color: transparent', 'cursor: pointer']
  );
  requireRule(
    rules,
    'WebKit range track',
    ['::-webkit-slider-runnable-track'],
    ['background-color: var(--color-control-hover)', 'border: 1px solid var(--color-control-border)', 'border-radius:']
  );
  requireRule(
    rules,
    'Mozilla range track',
    ['::-moz-range-track'],
    ['background-color: var(--color-control-hover)', 'border: 1px solid var(--color-control-border)', 'border-radius:']
  );
  requireRule(
    rules,
    'WebKit range thumb',
    ['::-webkit-slider-thumb'],
    ['appearance: none', 'background-color: var(--color-accent-bright)', 'border:', 'border-radius: 50%']
  );
  requireRule(
    rules,
    'Mozilla range thumb',
    ['::-moz-range-thumb'],
    ['background-color: var(--color-accent-bright)', 'border:', 'border-radius: 50%']
  );
  requireRule(
    rules,
    'disabled range state',
    [":where(input[type='range']):disabled"],
    ['cursor: not-allowed', 'opacity: 0.58']
  );
  requireRule(
    rules,
    'search field appearance policy',
    [":where(input[type='search'])"],
    ['appearance: none', '-webkit-appearance: none']
  );
  requireRule(
    rules,
    'search decoration suppression',
    [":where(input[type='search'])", '::-webkit-search-decoration'],
    ['display: none', 'appearance: none', '-webkit-appearance: none']
  );
  requireRule(
    rules,
    'themed search cancel control',
    [":where(input[type='search'])", '::-webkit-search-cancel-button'],
    ['background-color: var(--color-text-muted)', 'cursor: pointer', 'appearance: none', '-webkit-mask-image:']
  );
  requireRule(rules, 'number field policy', ["input[type='number']"], ['appearance: textfield']);
  requireRule(
    rules,
    'number spinner suppression',
    ["input[type='number']::-webkit-inner-spin-button", "input[type='number']::-webkit-outer-spin-button"],
    ['appearance: none', 'margin: 0']
  );
  requireRule(
    rules,
    'autofill theme',
    [':-webkit-autofill'],
    ['-webkit-text-fill-color: var(--color-text)', 'caret-color: var(--color-text)', 'var(--color-control) inset']
  );
  requireRule(
    rules,
    'file control baseline',
    [":where(input[type='file'])"],
    ['color: var(--color-text-soft)', 'background-color: var(--color-control)', 'border: 1px solid var(--color-control-border)']
  );
  requireRule(
    rules,
    'file selector button baseline',
    [":where(input[type='file'])", '::file-selector-button'],
    ['color: var(--color-text-soft)', 'background-color: var(--color-control-hover)', 'cursor: pointer']
  );
  requireRule(
    rules,
    'color control baseline',
    [":where(input[type='color'])"],
    ['background-color: var(--color-control)', 'border: 1px solid var(--color-control-border)', 'cursor: pointer']
  );
  requireRule(
    rules,
    'file and color disabled state',
    [":where(input[type='color'], input[type='file']):disabled"],
    ['background-color: color-mix(in srgb, var(--color-control)', 'cursor: not-allowed', 'opacity: 0.58']
  );
  requireRule(
    rules,
    'button hover state',
    ['button', "input[type='button']", ':hover:not(:disabled'],
    ['border-color: var(--color-accent-bright)']
  );
  requireRule(
    rules,
    'global control focus-visible state',
    [':where(a, button, input, select, textarea, summary, [tabindex]):focus-visible'],
    ['outline: 3px solid var(--color-focus)', 'outline-offset: 2px']
  );
  requireRule(rules, 'button disabled state', ['button', '):disabled,'], ['cursor: not-allowed', 'opacity: 0.58']);
}

function checkForcedColorsRules(rules) {
  requireRule(
    rules,
    'forced-colors control palette',
    [':is(button, input, select, textarea)'],
    ['color: ButtonText', 'background: ButtonFace', 'border-color: ButtonText'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors button restoration',
    [":is(button, input[type='button'], input[type='reset'], input[type='submit'])"],
    ['appearance: auto', '-webkit-appearance: auto'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors select restoration',
    ['select'],
    ['background-image: none', 'appearance: auto'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors choice restoration',
    ["input:is([type='checkbox'], [type='radio'])"],
    ['accent-color: Highlight', 'appearance: auto'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors range restoration',
    ["input[type='range']"],
    ['appearance: auto'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors search field restoration',
    ["input[type='search']"],
    ['appearance: auto', '-webkit-appearance: auto'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors search restoration',
    ["input[type='search']::-webkit-search-decoration", "input[type='search']::-webkit-search-cancel-button"],
    ['all: revert', 'forced-color-adjust: auto'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors number restoration',
    ["input[type='number']"],
    ['appearance: auto'],
    { forcedColors: true }
  );
  requireRule(
    rules,
    'forced-colors textarea resizer restoration',
    ['textarea::-webkit-resizer'],
    ['all: revert', 'forced-color-adjust: auto'],
    { forcedColors: true }
  );
}

function checkProxyControlRules(rules) {
  requireRule(
    rules,
    'disabled change-set proxy switch',
    ['.change-set-enabled-toggle input:disabled + span'],
    [
      'background: color-mix(in srgb, var(--color-control) 76%, var(--color-surface-muted))',
      'border-color: var(--color-border)',
      'cursor: not-allowed',
      'opacity: 0.58'
    ]
  );
  requireRule(
    rules,
    'disabled change-set proxy switch thumb',
    ['.change-set-enabled-toggle input:disabled + span::after'],
    ['background: var(--color-text-muted)']
  );
  requireRule(
    rules,
    'disabled type-chart proxy field',
    ['.type-chart-cell-disabled'],
    [
      'border-color: var(--color-border)',
      'box-shadow: inset 0 0 0 999px color-mix(in srgb, var(--color-surface-muted) 58%, transparent)',
      'cursor: not-allowed',
      'filter: saturate(0.45)',
      'opacity: 0.62'
    ]
  );
}

export function checkControlTheme() {
  const rules = readRules(readFileSync(stylesPath, 'utf8'));
  const allRules = cssFiles(sourceRoot).flatMap((path) => readRules(readFileSync(path, 'utf8')));
  const checks = [
    checkCssSafety,
    checkFiniteSearchableOptionContract,
    checkSearchableOptionComponentContract,
    checkMigratedSearchableOptionContracts,
    checkMovesCustomSelectMarkup,
    checkNativeControlMarkup,
    () => checkRequiredGlobalRules(rules),
    () => checkForcedColorsRules(rules),
    () => checkProxyControlRules(allRules),
    () => checkSearchableOptionAffordanceRules(allRules)
  ];
  const failures = [];
  for (const check of checks) {
    try {
      check();
    } catch (error) {
      failures.push(error instanceof Error ? error.message : String(error));
    }
  }

  if (failures.length > 0) {
    throw new Error(`KM control theme contract failed:\n\n${failures.join('\n\n')}`);
  }
}

const invokedPath = process.argv[1];
if (invokedPath !== undefined && fileURLToPath(import.meta.url) === invokedPath) {
  checkControlTheme();
  console.log('KM control theme contract passed.');
}
