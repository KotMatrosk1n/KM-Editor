// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));
const sourceRoot = resolve(desktopRoot, 'src');
const configPath = ts.findConfigFile(desktopRoot, ts.sys.fileExists, 'tsconfig.app.json');
assert.ok(configPath, 'Desktop tsconfig.app.json was not found.');
const config = ts.readConfigFile(configPath, ts.sys.readFile);
assert.equal(config.error, undefined, 'Desktop tsconfig.app.json could not be read.');
const parsedConfig = ts.parseJsonConfigFileContent(
  config.config,
  ts.sys,
  desktopRoot,
  undefined,
  configPath
);
assert.deepEqual(
  parsedConfig.errors,
  [],
  'Desktop tsconfig.app.json produced configuration diagnostics.'
);
const program = ts.createProgram({
  options: { ...parsedConfig.options, noEmit: true },
  rootNames: parsedConfig.fileNames
});

function normalizedPath(path) {
  return resolve(path).replaceAll('\\', '/').toLocaleLowerCase();
}

const normalizedSourceRoot = `${normalizedPath(sourceRoot)}/`;
const auditedSourceFiles = program.getSourceFiles().filter((sourceFile) =>
  !sourceFile.isDeclarationFile &&
  normalizedPath(sourceFile.fileName).startsWith(normalizedSourceRoot)
);
const auditedTsxSourceFiles = auditedSourceFiles.filter((sourceFile) =>
  sourceFile.fileName.endsWith('.tsx')
);

// Exact counts turn an accidentally excluded source tree into a contract
// failure rather than silently shrinking the audit.
assert.equal(
  auditedSourceFiles.length,
  271,
  'Update the field-lock source count only after reviewing every added or removed application file.'
);
assert.equal(
  auditedTsxSourceFiles.length,
  88,
  'Update the field-lock TSX count only after reviewing every added or removed editor surface.'
);

const transientNamePattern =
  /(?:busy|loading|saving|staging|applying|previewing|processing|updating|refreshing|mutating|pending|read(?:y|iness)|error|invalid|stale|blocked|locked|conflict|status|availability|phase|busyAction|recoveryRequired)/iu;
const transientLiterals = new Set([
  'applying',
  'available',
  'blocked',
  'busy',
  'checking',
  'conflict',
  'disabled',
  'error',
  'failed',
  'failure',
  'invalid',
  'loading',
  'locked',
  'mutating',
  'not-ready',
  'pending',
  'preparing',
  'previewing',
  'processing',
  'ready',
  'recovering',
  'recovery-required',
  'refreshing',
  'saving',
  'staging',
  'stale',
  'unavailable',
  'updating'
]);
const excludedInputTypes = new Set(['button', 'hidden', 'image', 'reset', 'submit']);
const activeTransientSignalPattern =
  /(?:busy|loading|saving|staging|applying|previewing|processing|updating|refreshing|mutating|pending|read(?:y|iness)|error|invalid|stale|conflict|recovery)/iu;
function isActiveTransientSignal(signal) {
  if (/^name:/u.test(signal) && /(?:Blocked|Disabled|Locked|ReadOnly)Reason\b/iu.test(signal)) {
    return false;
  }
  return activeTransientSignalPattern.test(signal) ||
    (/^name:/u.test(signal) && /(?:blocked|locked|phase)/iu.test(signal)) ||
    /^literal:(?:blocked|locked)$/u.test(signal);
}
const knownFieldComponents = new Set([
  'DexSizeControl',
  'GiftPokemonDraftField',
  'MutationPinControl',
  'NpcItemGiftItemPicker',
  'NumberControl',
  'NumberField',
  'PokemonPersonalFieldInput',
  'SearchableOptionInput',
  'SelectControl',
  'ShopRowFieldInput',
  'TrainerDraftField',
  'TypeChartCellControl'
]);

/**
 * Only reviewed operation-bound, persisted-transaction, source-replacement,
 * or structural-capability locks belong here. Each key must match one
 * detected value control exactly; stale permits fail the gate.
 */
const permittedTransientFieldLocks = new Map([
  [
    'src/features/gameplay-settings/InGameSettingsPackagePanel.tsx#input:id="in-game-settings-review-confirmation"[disabled]',
    'The reviewed-package acknowledgement is owned by the exact apply transaction once the write begins.'
  ],
  [
    'src/features/output-safety/ProjectRelocationPanel.tsx#input:id={`project-relocation-${entry.field}`}[disabled]',
    'Relocation paths stay editable through review and lock only while the reviewed filesystem move is applying.'
  ],
  [
    'src/features/npc-item-gift/NpcItemGiftSection.tsx#NpcItemGiftCard:key={gift.giftId}=>src/features/npc-item-gift/NpcItemGiftSection.tsx#NpcItemGiftItemPicker:inputId={itemInputId}[disabled]=>src/features/npc-item-gift/NpcItemGiftSection.tsx#SearchableOptionInput:id={inputId}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'A persisted NPC gift transaction owns the picker until it is applied or discarded. The picker remains editable while local text is incomplete.'
  ],
  [
    'src/features/npc-item-gift/NpcItemGiftSection.tsx#NpcItemGiftCard:key={gift.giftId}=>src/features/npc-item-gift/NpcItemGiftSection.tsx#input:id={`npc-item-gift-${gift.giftId}-amount`}[disabled]',
    'A persisted NPC gift transaction owns the quantity until it is applied or discarded. Invalid local quantity text remains repairable before staging.'
  ],
  [
    'src/features/shiny-rate/ShinyRateSection.tsx#input:id="shiny-rate-custom-denominator"[disabled]',
    'The custom denominator is structurally read-only when the exact game build or installed package state cannot support a shiny-rate write.'
  ],
  [
    'src/features/change-sets/ChangeSetWorkspacePanel.tsx#input:id="change-set-import-file"[disabled]',
    'The hidden file picker is structurally unavailable until the workspace is ready and is serialized while an import or authoring transaction owns the controller.'
  ],
  [
    'src/features/change-sets/ChangeSetWorkspacePanel.tsx#ChangeSetList[1]=>src/features/change-sets/ChangeSetWorkspacePanel.tsx#input:id={`change-set-enabled-${changeSet.id}`}[disabled]',
    'Change-set enablement is a serialized workspace mutation and locks only while another workspace mutation owns the controller.'
  ],
  [
    'src/features/type-chart/TypeChartSection.tsx#TypeChartCellControl:id={`type-chart-${attackType.typeIndex}-${defenseType.typeIndex}`}=>src/features/type-chart/TypeChartSection.tsx#SearchableOptionInput:id={id}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Type Chart cells are structurally read-only for unavailable sources, blocked package states, unsupported games, or a persisted staged uninstall.'
  ],
  [
    'src/features/ange-fight/AngeFightSection.tsx#NumberField:inputId={`ange-fight-${flower.flowerId}-flower-hp`}=>src/features/ange-fight/AngeFightSection.tsx#input:id={inputId}[disabled]',
    'Ange Fight flower HP is structurally read-only for an unavailable workflow, a read-only install, or a persisted staged uninstall.'
  ],
  [
    'src/features/ange-fight/AngeFightSection.tsx#AngeFightAttackCard:key={attack.attackId}=>src/features/ange-fight/AngeFightSection.tsx#NumberField:inputId={`ange-fight-${attack.attackId}-pokemon-damage`}[disabled]=>src/features/ange-fight/AngeFightSection.tsx#input:id={inputId}[disabled]',
    'Ange Fight Pokemon damage is structurally read-only for an unavailable workflow, a read-only install, or a persisted staged uninstall.'
  ],
  [
    'src/features/ange-fight/AngeFightSection.tsx#AngeFightAttackCard:key={attack.attackId}=>src/features/ange-fight/AngeFightSection.tsx#NumberField:inputId={`ange-fight-${attack.attackId}-player-damage`}[disabled]=>src/features/ange-fight/AngeFightSection.tsx#input:id={inputId}[disabled]',
    'Ange Fight player damage is structurally read-only for an unavailable workflow, a read-only install, or a persisted staged uninstall.'
  ],
  [
    'src/App.tsx#SvModMergerSection[1]=>src/App.tsx#input:id={`mod-merger-source-enabled-${index}`}[disabled]',
    'The Scarlet and Violet merger source list is owned while its exact load, stage, or apply transaction is running.'
  ],
  [
    'src/App.tsx#SvModMergerSection[2]=>src/App.tsx#input:id={`mod-merger-source-enabled-${index}`}[disabled]',
    'The Z-A merger source list is owned while its exact load, stage, or apply transaction is running.'
  ],
  [
    'src/App.tsx#SettingsSection[1]=>src/App.tsx#SearchableOptionInput:id="settings-cache-limit"[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Cache-limit changes are serialized with cache refresh and settings writes so competing disk-cache mutations cannot race.'
  ],
  [
    'src/App.tsx#SelectedItemPanel[1]=>src/App.tsx#input:id={`item-tm-compatibility-${pokemon.personalId}`}[disabled]',
    'A Technical Machine compatibility matrix is structurally bound to its committed move identity and stays read-only until a changed move mapping or protected Pokemon transaction is reloaded.'
  ],
  [
    "src/App.tsx#SelectedTextPanel[1]=>src/App.tsx#textarea:aria-label={valueField?.label ?? 'Text value'}[disabled]",
    'The text value is bound to the selected paged source record and locks while a query is replacing that record so input cannot land on a different entry.'
  ],
  [
    'src/App.tsx#SelectedPlacementPanel[1]=>src/App.tsx#SearchableOptionInput:id={`placement-field-${field.field}`}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Remote placement fields are bound to an exact loaded record and lock only while that page or record snapshot is being replaced.'
  ],
  [
    'src/App.tsx#SelectedPlacementPanel[1]=>src/App.tsx#input:id={`placement-field-${field.field}`}[disabled]',
    'Remote placement fields are bound to an exact loaded record and lock only while that page or record snapshot is being replaced.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#input:id={`pokemon-learnset-${pokemon.personalId}-${move.slot}-level`}[disabled]',
    'Learnset row values lock only while a paste or structural reorder owns the slot identities that those drafts address.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#SearchableOptionInput:id={`pokemon-learnset-${pokemon.personalId}-${move.slot}-move`}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Learnset row values lock only while a paste or structural reorder owns the slot identities that those drafts address.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#input:id={`pokemon-learnset-${pokemon.personalId}-${move.slot}-move`}[disabled]',
    'Learnset row values lock only while a paste or structural reorder owns the slot identities that those drafts address.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#SearchableOptionInput:id="pokemon-evolution-method"[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Evolution row values lock only while a structural move or removal owns their slot identities.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#SearchableOptionInput:id="pokemon-evolution-argument"[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Evolution row values lock only while a structural move or removal owns their slot identities.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#input:id="pokemon-evolution-argument"[disabled]',
    'Evolution row values lock only while a structural move or removal owns their slot identities.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#SearchableOptionInput:id="pokemon-evolution-species"[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Evolution row values lock only while a structural move or removal owns their slot identities.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#input:id="pokemon-evolution-species"[disabled]',
    'Evolution row values lock only while a structural move or removal owns their slot identities.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#SearchableOptionInput:id="pokemon-evolution-form"[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Evolution row values lock only while a structural move or removal owns their slot identities.'
  ],
  [
    'src/App.tsx#SelectedPokemonPanel[1]=>src/App.tsx#input:id="pokemon-evolution-level"[disabled]',
    'Evolution row values lock only while a structural move or removal owns their slot identities.'
  ],
  [
    'src/App.tsx#SelectedTrainerPanel[1]=>src/App.tsx#TrainerDraftField:key={field.field}[disabled]=>src/App.tsx#SearchableOptionInput:id={inputId}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'The selected party-slot fields lock only while a paste owns that exact trainer and slot.'
  ],
  [
    'src/App.tsx#SelectedTrainerPanel[1]=>src/App.tsx#TrainerDraftField:key={field.field}[disabled]=>src/App.tsx#input:id={inputId}[disabled]',
    'The selected party-slot fields lock only while a paste owns that exact trainer and slot.'
  ],
  [
    'src/App.tsx#SelectedEncounterPanel[1]=>src/App.tsx#TrainerDraftField:key={field.field}[disabled]=>src/App.tsx#SearchableOptionInput:id={inputId}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Encounter slot fields lock only while a paste owns their exact destination table and shared placement records.'
  ],
  [
    'src/App.tsx#SelectedEncounterPanel[1]=>src/App.tsx#TrainerDraftField:key={field.field}[disabled]=>src/App.tsx#input:id={inputId}[disabled]',
    'Encounter slot fields lock only while a paste owns their exact destination table and shared placement records.'
  ],
  [
    'src/features/game-dump/GameDumpSection.tsx#input:id="game-dump-destination-folder"[disabled]',
    'The Game Dump destination belongs to the active generation transaction and cannot change while files are being written.'
  ],
  [
    'src/features/game-dump/GameDumpSection.tsx#input:id={categoryInputId}[disabled]',
    'Game Dump category selection is frozen while the active generation transaction owns the submitted category snapshot.'
  ],
  [
    'src/features/game-dump/GameDumpSection.tsx#SearchableOptionInput:id={formatInputId}=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Game Dump output formats are frozen while the active generation transaction owns the submitted category snapshot.'
  ],
  [
    'src/features/game-dump/GameDumpSection.tsx#SearchableOptionInput:id={languageInputId}=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Game Dump language choices are frozen while the active generation transaction owns the submitted category snapshot.'
  ],
  [
    'src/App.tsx#HealthSection[1]=>src/App.tsx#input:id={inputId}[disabled]',
    'Project paths stay fixed while a project-scoped write or transition owns the current bridge and filesystem scope.'
  ],
  [
    'src/App.tsx#TrainerPoolsSection[1]=>src/features/trainer-pools/TrainerPoolsSection.tsx#PoolSelection[1][disabled]=>src/features/trainer-pools/TrainerPoolsSection.tsx#fieldset[1][disabled]',
    'Trainer pool membership is frozen while the exact staged pool snapshot is being committed.'
  ],
  [
    'src/App.tsx#TrainerPoolsSection[1]=>src/features/trainer-pools/TrainerPoolsSection.tsx#PoolSelection[2][disabled]=>src/features/trainer-pools/TrainerPoolsSection.tsx#fieldset[1][disabled]',
    'Trainer pool membership is frozen while the exact staged pool snapshot is being committed.'
  ],
  [
    'src/App.tsx#TrainerPoolsSection[1]=>src/features/trainer-pools/TrainerPoolsSection.tsx#PoolSelection[1][disabled]=>src/features/trainer-pools/TrainerPoolsSection.tsx#SearchableOptionInput:id={poolSelectId}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Trainer pool selection is frozen while the exact staged pool snapshot is being committed.'
  ],
  [
    'src/App.tsx#TrainerPoolsSection[1]=>src/features/trainer-pools/TrainerPoolsSection.tsx#PoolSelection[1][disabled]=>src/features/trainer-pools/TrainerPoolsSection.tsx#SearchableOptionInput:id={memberSelectId}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Trainer pool membership selection is frozen while the exact staged pool snapshot is being committed.'
  ],
  [
    'src/App.tsx#TrainerPoolsSection[1]=>src/features/trainer-pools/TrainerPoolsSection.tsx#PoolSelection[2][disabled]=>src/features/trainer-pools/TrainerPoolsSection.tsx#SearchableOptionInput:id={poolSelectId}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Trainer pool selection is frozen while the exact staged pool snapshot is being committed.'
  ],
  [
    'src/App.tsx#TrainerPoolsSection[1]=>src/features/trainer-pools/TrainerPoolsSection.tsx#PoolSelection[2][disabled]=>src/features/trainer-pools/TrainerPoolsSection.tsx#SearchableOptionInput:id={memberSelectId}[disabled]=>src/components/SearchableOptionInput.tsx#input:id={inputId}[disabled]',
    'Trainer pool membership selection is frozen while the exact staged pool snapshot is being committed.'
  ],
  [
    'src/App.tsx#RandomizerSection[1]=>src/features/randomizer/RandomizerSection.tsx#input:id="randomizer-base-seed"[disabled]',
    'Randomizer configuration is immutable while preview or apply owns the submitted seed and options.'
  ],
  [
    'src/App.tsx#RandomizerSection[1]=>src/features/randomizer/RandomizerSection.tsx#input:id={`randomizer-${category.id}-enabled`}[disabled]',
    'Randomizer category choices are immutable while preview or apply owns the submitted options.'
  ],
  [
    'src/App.tsx#RandomizerSection[1]=>src/features/randomizer/RandomizerSection.tsx#input:id={inputId}[disabled]',
    'Randomizer type-chart constraints are immutable while preview or apply owns the submitted options.'
  ],
  [
    'src/App.tsx#RandomizerSection[1]=>src/features/randomizer/RandomizerSection.tsx#input:id="randomizer-type-chart-enabled"[disabled]',
    'Randomizer type-chart selection is immutable while preview or apply owns the submitted options.'
  ],
  [
    'src/App.tsx#RandomizerSection[1]=>src/features/randomizer/RandomizerSection.tsx#textarea:id="randomizer-shared-seed"[disabled]',
    'Randomizer shared-seed import is immutable while preview or apply owns the submitted options.'
  ],
  [
    'src/App.tsx#ModMergerSection[1]=>src/App.tsx#ModMergerFileList[1][disabled]=>src/App.tsx#input[57][disabled]',
    'Mod Merger file selection is frozen while its source load, stage, or apply transaction owns the plan.'
  ],
  [
    'src/App.tsx#ModMergerSection[1]=>src/App.tsx#ModMergerFileList[2][disabled]=>src/App.tsx#input[57][disabled]',
    'Mod Merger file selection is frozen while its source load, stage, or apply transaction owns the plan.'
  ]
]);

function resolveSymbol(checker, symbol) {
  if (!symbol) return null;
  return symbol.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(symbol) : symbol;
}

function sameSymbol(left, right) {
  if (!left || !right) return false;
  if (left === right) return true;
  if (left.id !== undefined && right.id !== undefined && left.id === right.id) return true;
  const rightDeclarations = new Set(right.declarations ?? []);
  return (left.declarations ?? []).some((declaration) => rightDeclarations.has(declaration));
}

function attributeMap(node, sourceFile) {
  return new Map(
    node.attributes.properties
      .filter(ts.isJsxAttribute)
      .map((attribute) => [attribute.name.getText(sourceFile), attribute])
  );
}

function attributeExpression(attribute) {
  return attribute?.initializer && ts.isJsxExpression(attribute.initializer)
    ? attribute.initializer.expression ?? null
    : null;
}

function getStaticAttributeText(attribute) {
  return attribute?.initializer && ts.isStringLiteral(attribute.initializer)
    ? attribute.initializer.text.toLocaleLowerCase()
    : null;
}

function getFunctionComponentSymbol(checker, functionLike) {
  if (ts.isFunctionDeclaration(functionLike) && functionLike.name) {
    return resolveSymbol(checker, checker.getSymbolAtLocation(functionLike.name));
  }
  if (
    (ts.isArrowFunction(functionLike) || ts.isFunctionExpression(functionLike)) &&
    ts.isVariableDeclaration(functionLike.parent) &&
    ts.isIdentifier(functionLike.parent.name)
  ) {
    return resolveSymbol(checker, checker.getSymbolAtLocation(functionLike.parent.name));
  }
  let owner = functionLike;
  while (
    ts.isCallExpression(owner.parent) ||
    ts.isParenthesizedExpression(owner.parent) ||
    ts.isAsExpression(owner.parent) ||
    ts.isSatisfiesExpression(owner.parent)
  ) {
    owner = owner.parent;
  }
  if (ts.isVariableDeclaration(owner.parent) && ts.isIdentifier(owner.parent.name)) {
    return resolveSymbol(checker, checker.getSymbolAtLocation(owner.parent.name));
  }
  return null;
}

function createAnalyzer(analysisProgram, analyzedFiles, pathRoot) {
  const checker = analysisProgram.getTypeChecker();
  const sourceFiles = [...analyzedFiles];
  const sourceFileSet = new Set(sourceFiles);

  const renderedComponentSymbols = new Set();
  for (const sourceFile of sourceFiles.filter((candidate) => candidate.fileName.endsWith('.tsx'))) {
    const visit = (node) => {
      if (ts.isJsxSelfClosingElement(node) || ts.isJsxOpeningElement(node)) {
        const tagName = node.tagName.getText(sourceFile);
        if (!['fieldset', 'input', 'select', 'textarea'].includes(tagName)) {
          const symbol = resolveSymbol(checker, checker.getSymbolAtLocation(node.tagName));
          if (symbol) renderedComponentSymbols.add(symbol);
        }
      }
      ts.forEachChild(node, visit);
    };
    visit(sourceFile);
  }

  function isLockPropName(propName) {
    return (
      propName === 'disabled' ||
      propName === 'readOnly' ||
      transientNamePattern.test(propName) ||
      /^(?:can|cannot)(?:Edit|Mutate|Relocate|Select|Toggle|Update)/u.test(propName) ||
      /(?:Editable|Enabled|Interactive|Mutable)$/iu.test(propName) ||
      /(?:Blocked|Disabled|Locked|ReadOnly)Reason$/u.test(propName)
    );
  }

  function isBooleanLikeType(type) {
    if (type.flags & (ts.TypeFlags.Boolean | ts.TypeFlags.BooleanLiteral)) return true;
    return type.isUnion?.() && type.types.some((member) =>
      Boolean(member.flags & (ts.TypeFlags.Boolean | ts.TypeFlags.BooleanLiteral))
    );
  }

  function shouldFollowLockSymbol(symbol, symbolText) {
    if (!symbol) return false;
    if (transientNamePattern.test(symbolText) || isLockPropName(symbolText)) return true;
    const type = checker.getTypeOfSymbol(symbol);
    return isBooleanLikeType(type) || type.getCallSignatures().length > 0;
  }

  function componentPropsOwner(node, visitedSymbols = new Set()) {
    if (
      ts.isParenthesizedExpression(node) ||
      ts.isAsExpression(node) ||
      ts.isSatisfiesExpression(node) ||
      ts.isNonNullExpression(node)
    ) {
      return componentPropsOwner(node.expression, visitedSymbols);
    }
    if (!ts.isIdentifier(node)) return null;
    const symbol = resolveSymbol(checker, checker.getSymbolAtLocation(node));
    if (!symbol || visitedSymbols.has(symbol)) return null;
    const nextVisited = new Set(visitedSymbols);
    nextVisited.add(symbol);
    for (const declaration of symbol.declarations ?? []) {
      if (ts.isParameter(declaration)) {
        const componentSymbol = getFunctionComponentSymbol(checker, declaration.parent);
        if (componentSymbol && renderedComponentSymbols.has(componentSymbol)) {
          return componentSymbol;
        }
      }
      if (ts.isVariableDeclaration(declaration) && declaration.initializer) {
        const componentSymbol = componentPropsOwner(declaration.initializer, nextVisited);
        if (componentSymbol) return componentSymbol;
      }
    }
    return null;
  }

  function componentPropReference(node) {
    if (ts.isPropertyAccessExpression(node)) {
      const componentSymbol = componentPropsOwner(node.expression);
      if (componentSymbol) return { componentSymbol, propName: node.name.text };
    }
    if (
      ts.isElementAccessExpression(node) &&
      node.argumentExpression &&
      ts.isStringLiteralLike(node.argumentExpression)
    ) {
      const componentSymbol = componentPropsOwner(node.expression);
      if (componentSymbol) return { componentSymbol, propName: node.argumentExpression.text };
    }

    if (!ts.isIdentifier(node)) return null;
    const symbol = checker.getSymbolAtLocation(node);
    for (const declaration of symbol?.declarations ?? []) {
      if (!ts.isBindingElement(declaration) || !ts.isObjectBindingPattern(declaration.parent)) {
        continue;
      }
      const propName = declaration.propertyName?.getText() ?? declaration.name.getText();
      const bindingOwner = declaration.parent.parent;
      if (ts.isParameter(bindingOwner)) {
        const componentSymbol = getFunctionComponentSymbol(checker, bindingOwner.parent);
        if (componentSymbol && renderedComponentSymbols.has(componentSymbol)) {
          return { componentSymbol, propName };
        }
      }
      if (ts.isVariableDeclaration(bindingOwner) && bindingOwner.initializer) {
        const componentSymbol = componentPropsOwner(bindingOwner.initializer);
        if (componentSymbol) return { componentSymbol, propName };
      }
    }
    return null;
  }

  function collectMutationSignals(rootNode) {
    const signals = new Set();
    const visitedDeclarations = new Set();

    const visitDeclaration = (declaration) => {
      if (visitedDeclarations.has(declaration)) return;
      if (!sourceFileSet.has(declaration.getSourceFile())) return;
      visitedDeclarations.add(declaration);
      if (ts.isVariableDeclaration(declaration) && declaration.initializer) {
        visit(declaration.initializer);
        return;
      }
      if (
        (ts.isFunctionDeclaration(declaration) ||
          ts.isMethodDeclaration(declaration) ||
          ts.isFunctionExpression(declaration) ||
          ts.isArrowFunction(declaration)) &&
        declaration.body
      ) {
        visit(declaration.body);
        return;
      }
      if (ts.isBindingElement(declaration) && declaration.initializer) {
        visit(declaration.initializer);
      }
    };

    const visit = (candidate) => {
      if (ts.isIdentifier(candidate)) {
        if (transientNamePattern.test(candidate.text)) signals.add(`name:${candidate.text}`);
        const symbol = resolveSymbol(checker, checker.getSymbolAtLocation(candidate));
        if (shouldFollowLockSymbol(symbol, candidate.text)) {
          for (const declaration of symbol?.declarations ?? []) visitDeclaration(declaration);
        }
        return;
      }

      if (ts.isPropertyAccessExpression(candidate)) {
        if (transientNamePattern.test(candidate.name.text)) {
          signals.add(`name:${candidate.getText(candidate.getSourceFile())}`);
        }
        const symbol = resolveSymbol(checker, checker.getSymbolAtLocation(candidate.name));
        if (shouldFollowLockSymbol(symbol, candidate.name.text)) {
          for (const declaration of symbol?.declarations ?? []) visitDeclaration(declaration);
        }
      }

      if (
        ts.isStringLiteralLike(candidate) &&
        transientLiterals.has(candidate.text.trim().toLocaleLowerCase())
      ) {
        signals.add(`literal:${candidate.text.trim().toLocaleLowerCase()}`);
      }
      ts.forEachChild(candidate, visit);
    };

    visit(rootNode);
    return [...signals].sort();
  }

  function isMutableControl(node, attributes, sourceFile) {
    const tagName = node.tagName.getText(sourceFile);
    if (tagName === 'input') {
      const inputType = getStaticAttributeText(attributes.get('type'));
      return !inputType || !excludedInputTypes.has(inputType);
    }
    if (tagName === 'select' || tagName === 'textarea') return true;
    if (tagName === 'fieldset') {
      let hasMutableDescendant = false;
      const findDescendant = (candidate) => {
        if (
          candidate !== node &&
          (ts.isJsxSelfClosingElement(candidate) || ts.isJsxOpeningElement(candidate))
        ) {
          const descendantAttributes = attributeMap(candidate, sourceFile);
          if (isMutableControl(candidate, descendantAttributes, sourceFile)) {
            hasMutableDescendant = true;
            return;
          }
        }
        if (!hasMutableDescendant) ts.forEachChild(candidate, findDescendant);
      };
      const fieldsetElement = ts.isJsxOpeningElement(node) && ts.isJsxElement(node.parent)
        ? node.parent
        : node;
      ts.forEachChild(fieldsetElement, findDescendant);
      return hasMutableDescendant;
    }
    if (!knownFieldComponents.has(tagName)) return false;
    const hasValue = [
      'checked',
      'currentValue',
      'defaultChecked',
      'defaultValue',
      'draftValue',
      'value'
    ].some((attributeName) => attributes.has(attributeName));
    const hasMutationHandler = [...attributes.keys()].some((attributeName) =>
      /^on(?:Change|DraftChange|Input|Select|Toggle|Update|ValueChange)$/u.test(attributeName)
    );
    return hasValue || hasMutationHandler;
  }

  function collectComponentPropReferences(rootNode) {
    const references = new Map();
    const visitedDeclarations = new Set();
    const visit = (candidate) => {
      const reference = componentPropReference(candidate);
      if (reference) {
        references.set(
          `${reference.componentSymbol.id ?? reference.componentSymbol.name}:${reference.propName}`,
          reference
        );
      }
      if (ts.isIdentifier(candidate) || ts.isPropertyAccessExpression(candidate)) {
        const symbolLocation = ts.isPropertyAccessExpression(candidate)
          ? candidate.name
          : candidate;
        const symbol = resolveSymbol(checker, checker.getSymbolAtLocation(symbolLocation));
        if (shouldFollowLockSymbol(symbol, candidate.getText(candidate.getSourceFile()))) {
          for (const declaration of symbol?.declarations ?? []) {
            if (visitedDeclarations.has(declaration)) continue;
            if (!sourceFileSet.has(declaration.getSourceFile())) continue;
            visitedDeclarations.add(declaration);
            if (ts.isVariableDeclaration(declaration) && declaration.initializer) {
              visit(declaration.initializer);
            } else if (
              (ts.isFunctionDeclaration(declaration) ||
                ts.isMethodDeclaration(declaration) ||
                ts.isFunctionExpression(declaration) ||
                ts.isArrowFunction(declaration)) &&
              declaration.body
            ) {
              visit(declaration.body);
            }
          }
        }
      }
      ts.forEachChild(candidate, visit);
    };
    visit(rootNode);
    return [...references.values()];
  }

  function enclosingComponentSymbol(node) {
    let current = node.parent;
    while (current) {
      if (
        ts.isFunctionDeclaration(current) ||
        ts.isFunctionExpression(current) ||
        ts.isArrowFunction(current)
      ) {
        const symbol = getFunctionComponentSymbol(checker, current);
        if (symbol) return symbol;
      }
      current = current.parent;
    }
    return null;
  }

  const jsxOrdinals = new Map();
  for (const sourceFile of sourceFiles.filter((candidate) => candidate.fileName.endsWith('.tsx'))) {
    const tagCounts = new Map();
    const visit = (node) => {
      if (ts.isJsxSelfClosingElement(node) || ts.isJsxOpeningElement(node)) {
        const tagName = node.tagName.getText(sourceFile);
        const ordinal = (tagCounts.get(tagName) ?? 0) + 1;
        tagCounts.set(tagName, ordinal);
        jsxOrdinals.set(node, ordinal);
      }
      ts.forEachChild(node, visit);
    };
    visit(sourceFile);
  }

  // Resolve which exact props ultimately own disabled/readOnly on a mutable
  // field. Call sites are audited independently, so two components with the
  // same display name and two instances of one component never share signals.
  const componentLockTargets = new Map();
  const mappedTargetNodes = new Set();
  let componentTargetAdded = true;
  while (componentTargetAdded) {
    componentTargetAdded = false;
    for (const sourceFile of sourceFiles.filter((candidate) => candidate.fileName.endsWith('.tsx'))) {
      const relativePath = relative(pathRoot, sourceFile.fileName).replaceAll('\\', '/');
      const visit = (node) => {
        if (ts.isJsxSelfClosingElement(node) || ts.isJsxOpeningElement(node)) {
          const attributes = attributeMap(node, sourceFile);
          const renderedComponentSymbol = resolveSymbol(
            checker,
            checker.getSymbolAtLocation(node.tagName)
          );
          if (
            isMutableControl(node, attributes, sourceFile) ||
            componentLockTargets.has(renderedComponentSymbol)
          ) {
            for (const lockAttribute of ['disabled', 'readOnly']) {
              const expression = attributeExpression(attributes.get(lockAttribute));
              if (!expression) continue;
              for (const reference of collectComponentPropReferences(expression)) {
                if (!isLockPropName(reference.propName)) continue;
                const props = componentLockTargets.get(reference.componentSymbol) ?? new Map();
                const targets = props.get(reference.propName) ?? new Map();
                mappedTargetNodes.add(node);
                const targetIdentity =
                  `${controlIdentity(relativePath, node, attributes, sourceFile)}[${lockAttribute}]`;
                if (!targets.has(targetIdentity)) componentTargetAdded = true;
                targets.set(targetIdentity, {
                  componentSymbol: renderedComponentSymbol,
                  expression: expression.getText(sourceFile),
                  identity: targetIdentity,
                  lockAttribute,
                  signals: collectMutationSignals(expression)
                });
                props.set(reference.propName, targets);
                componentLockTargets.set(reference.componentSymbol, props);
              }
            }
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(sourceFile);
    }
  }

  const expandComponentTarget = (target, visited = new Set()) => {
    if (!target.componentSymbol || visited.has(target.componentSymbol)) return [target];
    const nestedTargets = componentLockTargets
      .get(target.componentSymbol)
      ?.get(target.lockAttribute);
    if (!nestedTargets || nestedTargets.size === 0) return [target];
    const nextVisited = new Set(visited);
    nextVisited.add(target.componentSymbol);
    return [...nestedTargets.values()].flatMap((nestedTarget) =>
      expandComponentTarget(nestedTarget, nextVisited).map((terminalTarget) => ({
        ...terminalTarget,
        expression: `${target.expression} => ${terminalTarget.expression}`,
        identity: `${target.identity}=>${terminalTarget.identity}`,
        signals: [...new Set([...target.signals, ...terminalTarget.signals])].sort()
      }))
    );
  };

  function controlIdentity(relativePath, node, attributes, sourceFile) {
    const tagName = node.tagName.getText(sourceFile);
    for (const attributeName of ['id', 'inputId', 'name', 'aria-label', 'key']) {
      const initializer = attributes.get(attributeName)?.initializer;
      if (initializer) {
        return `${relativePath}#${tagName}:${attributeName}=${initializer.getText(sourceFile)}`;
      }
    }
    return `${relativePath}#${tagName}[${jsxOrdinals.get(node) ?? 0}]`;
  }

  const locksByIdentity = new Map();
  const addLock = (lock) => {
    const existing = locksByIdentity.get(lock.identity);
    if (!existing) {
      locksByIdentity.set(lock.identity, lock);
      return;
    }
    existing.expression = [...new Set([existing.expression, lock.expression])].join(' || ');
    existing.lockAttribute = [...new Set([existing.lockAttribute, lock.lockAttribute])].join('+');
    existing.signals = [...new Set([...existing.signals, ...lock.signals])].sort();
  };
  for (const sourceFile of sourceFiles.filter((candidate) => candidate.fileName.endsWith('.tsx'))) {
    const relativePath = relative(pathRoot, sourceFile.fileName).replaceAll('\\', '/');
    const visit = (node) => {
      if (ts.isJsxSelfClosingElement(node) || ts.isJsxOpeningElement(node)) {
        const nodeAttributes = attributeMap(node, sourceFile);
        if (mappedTargetNodes.has(node)) {
          ts.forEachChild(node, visit);
          return;
        }
        const attributes = nodeAttributes;
        const lockAttributes = new Set();
        const componentSymbol = resolveSymbol(
          checker,
          checker.getSymbolAtLocation(node.tagName)
        );
        const componentTargets = componentLockTargets.get(componentSymbol) ?? new Map();
        if (isMutableControl(node, attributes, sourceFile) && componentTargets.size === 0) {
          lockAttributes.add('disabled');
          lockAttributes.add('readOnly');
        }
        if (lockAttributes.size > 0) {
          for (const lockAttribute of lockAttributes) {
            const lockExpression = attributeExpression(attributes.get(lockAttribute));
            if (!lockExpression) continue;
            const ownerComponent = enclosingComponentSymbol(node);
            if (
              ownerComponent &&
              collectComponentPropReferences(lockExpression).some(
                (reference) =>
                  sameSymbol(reference.componentSymbol, ownerComponent) &&
                  isLockPropName(reference.propName)
              )
            ) {
              continue;
            }
            const signals = collectMutationSignals(lockExpression);
            if (signals.length > 0) {
              addLock({
                expression: lockExpression.getText(sourceFile),
                identity: `${controlIdentity(
                  relativePath,
                  node,
                  attributes,
                  sourceFile
                )}[${lockAttribute}]`,
                lockAttribute,
                signals
              });
            }
          }
        }
        for (const [propName, targets] of componentTargets) {
          const lockExpression = attributeExpression(attributes.get(propName));
          if (!lockExpression) continue;
          const signals = collectMutationSignals(lockExpression);
          if (signals.length === 0) continue;
          const callerIdentity = controlIdentity(relativePath, node, attributes, sourceFile);
          for (const initialTarget of targets.values()) {
            for (const target of expandComponentTarget(initialTarget)) {
              addLock({
                expression: `${lockExpression.getText(sourceFile)} => ${target.expression}`,
                identity: `${callerIdentity}=>${target.identity}`,
                lockAttribute: propName,
                signals: [...new Set([...signals, ...target.signals])].sort()
              });
            }
          }
        }
      }
      ts.forEachChild(node, visit);
    };
    visit(sourceFile);
  }
  return [...locksByIdentity.values()];
}

function runAnalyzerSelfTest() {
  const contractPath = resolve(desktopRoot, '__field_lock_contract__.tsx');
  const contractText = `
    const isBusy = true;
    const isLoading = true;
    const isSaving = true;
    const isApplying = true;
    const isPreviewing = true;
    const isProcessing = true;
    const isRefreshing = true;
    const isUpdating = true;
    const isStaging = true;
    const isMutating = true;
    const isReady = false;
    const hasError = true;
    const isInvalid = true;
    const isStale = true;
    const isBlocked = true;
    const isLocked = true;
    const state = {
      availability: 'blocked',
      busyAction: 'saving',
      phase: 'staging',
      recoveryRequired: true,
      status: 'loading'
    };
    const alias = isLoading;
    function helper(status: string) { return status === 'pending'; }
    const arrow = (phase: string) => phase === 'conflict';
    class Policy { locked(availability: string) { return availability === 'unavailable'; } }
    const policy = new Policy();
    namespace First {
      export function DraftField({ disabled }: { disabled: boolean }) {
        return <input disabled={disabled} id="first-component" defaultValue="draft" />;
      }
    }
    namespace Second {
      export function DraftField({ disabled }: { disabled: boolean }) {
        return <input disabled={disabled} id="second-component" defaultValue="draft" />;
      }
    }
    function AliasedDraftField(props: { editable: boolean }) {
      const localProps = props;
      const { editable: canChange } = localProps;
      const effectiveReadOnly = !canChange;
      return <textarea defaultValue="draft" id="aliased-component" readOnly={effectiveReadOnly} />;
    }
    function NestedDraftField({ locked }: { locked: boolean }) {
      return <First.DraftField disabled={locked} />;
    }
    declare function wrap<T>(value: T): T;
    const WrappedDraftField = wrap(({ disabled }: { disabled: boolean }) =>
      <input defaultValue="draft" disabled={disabled} id="wrapped-component" />
    );
    const contract = <>
      <input disabled={isBusy} id="direct" defaultValue="draft" />
      <input disabled={alias} id="alias" defaultValue="draft" />
      <input disabled={helper('pending')} id="helper" defaultValue="draft" />
      <input disabled={arrow('conflict')} id="arrow" defaultValue="draft" />
      <input disabled={policy.locked('unavailable')} id="method" defaultValue="draft" />
      <input disabled={state.status === 'loading'} id="status" />
      <input disabled={state.availability === 'blocked'} id="availability" />
      <input disabled={state.phase === 'staging'} id="phase" />
      <input disabled={state.busyAction === 'saving'} id="busy-action" />
      <input disabled={state.recoveryRequired} id="recovery-required" />
      <input disabled={'error' === 'error'} id="literal" />
      <input disabled={isStaging} id="staging" />
      <input disabled={isMutating} id="mutating" />
      <input disabled={!isReady} id="ready" />
      <input disabled={hasError} id="error" />
      <input disabled={isInvalid} id="invalid" />
      <input disabled={isStale} id="stale" />
      <input disabled={isBlocked} id="blocked" />
      <input disabled={isLocked} id="locked" />
      <input disabled={isPreviewing} id="previewing" />
      <textarea readOnly={isRefreshing} id="readonly" defaultValue="draft" />
      <fieldset disabled={isApplying} id="fieldset"><input defaultValue="draft" /></fieldset>
      <input disabled={isProcessing} id="uncontrolled" defaultValue="draft" />
      <First.DraftField disabled={isSaving} />
      <Second.DraftField disabled={false} />
      <AliasedDraftField editable={!isSaving} />
      <NestedDraftField locked={isStaging} />
      <WrappedDraftField disabled={isUpdating} />
      ${[...transientLiterals].map((literal) =>
        `<input disabled={'${literal}' === '${literal}'} id="literal-${literal}" />`
      ).join('\n')}
      <button disabled={isPreviewing} type="button">Not a field</button>
      <input id="static-readonly" readOnly value="display" />
      <input id="literal-readonly" readOnly={true} value="display" />
      <input disabled={isUpdating} id="controlled" onChange={() => {}} value="draft" />
    </>;
  `;
  const compilerOptions = {
    jsx: ts.JsxEmit.ReactJSX,
    noLib: true,
    target: ts.ScriptTarget.ES2022
  };
  const sourceFile = ts.createSourceFile(
    contractPath,
    contractText,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const baseHost = ts.createCompilerHost(compilerOptions);
  const isContractPath = (path) => normalizedPath(path) === normalizedPath(contractPath);
  const host = {
    ...baseHost,
    fileExists: (path) => isContractPath(path) || baseHost.fileExists(path),
    getSourceFile: (path, languageVersion) =>
      isContractPath(path) ? sourceFile : baseHost.getSourceFile(path, languageVersion),
    readFile: (path) => isContractPath(path) ? contractText : baseHost.readFile(path)
  };
  const contractProgram = ts.createProgram({
    host,
    options: compilerOptions,
    rootNames: [contractPath]
  });
  const analyzedSourceFile = contractProgram.getSourceFile(contractPath);
  assert.ok(analyzedSourceFile, 'Field-lock self-test source was not loaded.');
  const locks = createAnalyzer(contractProgram, [analyzedSourceFile], desktopRoot);
  if (process.env.KM_DEBUG_FIELD_LOCKS === '1') console.error(locks);
  const identities = new Set(locks.map((lock) => lock.identity));
  for (const id of [
    'alias',
    'availability',
    'arrow',
    'busy-action',
    'blocked',
    'controlled',
    'direct',
    'fieldset',
    'helper',
    'error',
    'invalid',
    'locked',
    'method',
    'literal',
    'mutating',
    'phase',
    'previewing',
    'recovery-required',
    'ready',
    'readonly',
    'status',
    'staging',
    'stale',
    'uncontrolled'
  ]) {
    assert.ok(
      [...identities].some((identity) => identity.includes(`id=\"${id}\"`)),
      `Field-lock self-test did not detect ${id}.`
    );
  }
  assert.ok(
    [...identities].some((identity) => identity.includes('#First.DraftField')),
    'Field-lock self-test did not detect the exact custom-component prop.'
  );
  for (const componentName of ['AliasedDraftField', 'NestedDraftField', 'WrappedDraftField']) {
    assert.ok(
      [...identities].some((identity) => identity.includes(`#${componentName}`)),
      `Field-lock self-test did not trace ${componentName} to its native field.`
    );
  }
  assert.ok(
    ![...identities].some((identity) => identity.includes('#Second.DraftField')),
    'Field-lock self-test conflated same-named component symbols.'
  );
  for (const id of ['literal-readonly', 'static-readonly']) {
    assert.ok(
      ![...identities].some((identity) => identity.includes(`id=\"${id}\"`)),
      `Field-lock self-test incorrectly detected ${id}.`
    );
  }
  for (const literal of transientLiterals) {
    assert.ok(
      [...identities].some((identity) => identity.includes(`id="literal-${literal}"`)),
      `Field-lock self-test did not detect the ${literal} lifecycle literal.`
    );
  }
  assert.ok(
    !locks.some((lock) => lock.identity.includes('#button')),
    'Field-lock self-test must ignore operation buttons.'
  );
  assert.deepEqual(
    [...locks.reduce((counts, lock) => {
      counts.set(lock.identity, (counts.get(lock.identity) ?? 0) + 1);
      return counts;
    }, new Map()).entries()].filter(([, count]) => count !== 1),
    [],
    'Field-lock self-test permits must remain one-to-one with concrete controls.'
  );
}

runAnalyzerSelfTest();

const detectedLocks = createAnalyzer(program, auditedSourceFiles, desktopRoot);
// Availability, unsupported-build, fixed-field, and row-shape conditions are
// structural capabilities, not transient operation state. They are still
// discovered by the analyzer above, but only lifecycle signals can enter the
// exact permit contract below.
const lifecycleLocks = detectedLocks.filter((lock) =>
  lock.signals.some(isActiveTransientSignal)
);
if (process.env.KM_LIST_FIELD_LOCKS === '1') {
  console.log(JSON.stringify(lifecycleLocks.map((lock) => ({
    identity: lock.identity,
    expression: lock.expression,
    signals: lock.signals.filter(isActiveTransientSignal)
  })), null, 2));
  process.exit(0);
}
const detectedIdentityCounts = new Map();
for (const lock of lifecycleLocks) {
  detectedIdentityCounts.set(lock.identity, (detectedIdentityCounts.get(lock.identity) ?? 0) + 1);
}
assert.deepEqual(
  [...detectedIdentityCounts.entries()].filter(([, count]) => count !== 1),
  [],
  'Each permitted field lock must resolve to one concrete disabled or read-only attribute.'
);

const unclassifiedLocks = lifecycleLocks.filter(
  (lock) => !permittedTransientFieldLocks.has(lock.identity)
);
if (unclassifiedLocks.length > 0) {
  assert.fail(
    'Editable fields have unclassified transient locks:\n' +
      unclassifiedLocks.map((lock) =>
        `${lock.identity} <- ${lock.signals.join(', ')} through ${lock.lockAttribute}={${lock.expression}}`
      ).join('\n')
  );
}
assert.deepEqual(
  [...permittedTransientFieldLocks.keys()].sort(),
  lifecycleLocks.map((lock) => lock.identity).sort(),
  'Every permitted transient field lock must identify exactly one real control.'
);
