// SPDX-License-Identifier: GPL-3.0-only

import { readdirSync, readFileSync } from 'node:fs';
import { extname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

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

function checkNativeControlMarkup() {
  for (const path of sourceFiles(sourceRoot)) {
    const text = readFileSync(path, 'utf8');
    const sourceFile = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);

    function visit(node) {
      if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
        const tagName = node.tagName.getText(sourceFile);
        if (['button', 'input', 'select', 'textarea'].includes(tagName)) {
          const attributes = node.attributes.properties.filter(ts.isJsxAttribute);
          const styleAttribute = attributes.find(({ name }) => name.getText(sourceFile) === 'style');
          const location = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
          const sourceLocation = `${relative(sourceRoot, path)}:${location.line + 1}`;

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
    checkNativeControlMarkup,
    () => checkRequiredGlobalRules(rules),
    () => checkForcedColorsRules(rules),
    () => checkProxyControlRules(allRules)
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
