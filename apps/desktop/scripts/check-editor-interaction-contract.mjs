// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { dirname, delimiter, join, relative, resolve, sep } from 'node:path';
import { createRequire, Module } from 'node:module';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import {
  createSearchableOptionInteractionState,
  formatSearchableOptionValue,
  getSmartOptionMatches,
  resolveSearchableOptionCommit,
  transitionSearchableOptionInteraction
} from '../src/components/searchableOptionInputState.ts';
import {
  createLocalEditorValidationDiagnostics,
  mergeEditorDiagnostics,
  updateEditorDiagnosticsSource
} from '../src/components/commonEditorDiagnosticsState.ts';

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));
const sourceRoot = join(desktopRoot, 'src');
const require = createRequire(import.meta.url);
const desktopNodeModules = join(desktopRoot, 'node_modules');
// Compiled contract modules live under the OS temp directory, so explicitly expose
// the desktop dependency root to their CommonJS requires (notably Zod schemas).
process.env.NODE_PATH = process.env.NODE_PATH
  ? `${desktopNodeModules}${delimiter}${process.env.NODE_PATH}`
  : desktopNodeModules;
Module._initPaths();

function read(relativePath) {
  return readFileSync(join(desktopRoot, relativePath), 'utf8').replace(/\r\n?/gu, '\n');
}

function getFunctionLike(sourceFile, name) {
  let result = null;
  const visit = (node) => {
    if (
      (ts.isFunctionDeclaration(node) && node.name?.text === name) ||
      (ts.isVariableDeclaration(node) &&
        ts.isIdentifier(node.name) &&
        node.name.text === name &&
        node.initializer &&
        (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer)))
    ) {
      result = ts.isVariableDeclaration(node) ? node.initializer : node;
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  assert.ok(result, `Missing interaction contract function: ${name}`);
  return result;
}

function getOperationFunctionLike(sourceFile, name) {
  let result = null;
  const visit = (node) => {
    if (ts.isFunctionDeclaration(node) && node.name?.text === name) {
      result = node;
      return;
    }
    if (
      ts.isVariableDeclaration(node) &&
      ts.isIdentifier(node.name) &&
      node.name.text === name &&
      node.initializer
    ) {
      if (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer)) {
        result = node.initializer;
        return;
      }
      if (ts.isCallExpression(node.initializer)) {
        const callback = node.initializer.arguments[0];
        if (callback && (ts.isArrowFunction(callback) || ts.isFunctionExpression(callback))) {
          result = callback;
          return;
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  assert.ok(result, `Missing operation contract function: ${name}`);
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

function calledIdentifier(node, name) {
  return descendants(
    node,
    (candidate) =>
      ts.isCallExpression(candidate) &&
      ts.isIdentifier(candidate.expression) &&
      candidate.expression.text === name
  );
}

function assertOperationOwnedLockContract(
  sourceFile,
  { functionName, operationRef, releaseCalls }
) {
  const operation = getOperationFunctionLike(sourceFile, functionName);
  const assignments = descendants(
    operation,
    (candidate) =>
      ts.isBinaryExpression(candidate) &&
      candidate.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
      candidate.left.getText(sourceFile) === `${operationRef}.current`
  );
  const reservations = assignments.filter(
    (assignment) => assignment.right.getText(sourceFile) === 'operationToken'
  );
  const releases = assignments.filter(
    (assignment) => assignment.right.kind === ts.SyntaxKind.NullKeyword
  );
  assert.equal(
    reservations.length,
    1,
    `${functionName} must synchronously reserve its operation ref exactly once.`
  );
  assert.equal(
    releases.length,
    1,
    `${functionName} must release its operation ref exactly once.`
  );

  const guards = descendants(
    operation,
    (candidate) =>
      ts.isBinaryExpression(candidate) &&
      candidate.operatorToken.kind === ts.SyntaxKind.ExclamationEqualsEqualsToken &&
      candidate.left.getText(sourceFile) === `${operationRef}.current` &&
      candidate.right.kind === ts.SyntaxKind.NullKeyword
  );
  assert.equal(guards.length, 1, `${functionName} must reject an already-owned operation.`);
  assert.ok(
    guards[0].getStart(sourceFile) < reservations[0].getStart(sourceFile),
    `${functionName} must reject reentry before reserving the operation.`
  );
  const firstAwait = descendants(operation, ts.isAwaitExpression).sort(
    (left, right) => left.getStart(sourceFile) - right.getStart(sourceFile)
  )[0];
  assert.ok(firstAwait, `${functionName} must remain an asynchronous operation.`);
  assert.ok(
    reservations[0].getStart(sourceFile) < firstAwait.getStart(sourceFile),
    `${functionName} must reserve synchronously before its first await.`
  );

  const ownerBranches = descendants(
    operation,
    (candidate) =>
      ts.isIfStatement(candidate) &&
      candidate.expression.getText(sourceFile) ===
        `${operationRef}.current === operationToken`
  );
  assert.equal(
    ownerBranches.length,
    1,
    `${functionName} must release state only from the operation-token owner branch.`
  );
  const ownerBranch = ownerBranches[0];
  assert.ok(
    releases[0].getStart(sourceFile) >= ownerBranch.thenStatement.getStart(sourceFile) &&
      releases[0].getEnd() <= ownerBranch.thenStatement.getEnd(),
    `${functionName} must clear its operation ref inside the owner branch.`
  );
  const enclosingTry = descendants(operation, ts.isTryStatement).find(
    (candidate) =>
      candidate.finallyBlock &&
      ownerBranch.getStart(sourceFile) >= candidate.finallyBlock.getStart(sourceFile) &&
      ownerBranch.getEnd() <= candidate.finallyBlock.getEnd()
  );
  assert.ok(
    enclosingTry,
    `${functionName} must put the operation-token owner release in finally.`
  );

  for (const { argument, setter } of releaseCalls) {
    const matchingCalls = calledIdentifier(operation, setter).filter((call) =>
      argument.test(call.arguments[0]?.getText(sourceFile) ?? '')
    );
    const ownedMatchingCalls = calledIdentifier(ownerBranch.thenStatement, setter).filter(
      (call) => argument.test(call.arguments[0]?.getText(sourceFile) ?? '')
    );
    assert.equal(
      matchingCalls.length,
      1,
      `${functionName} must have exactly one ${setter} release.`
    );
    assert.equal(
      ownedMatchingCalls.length,
      1,
      `${functionName} must keep its ${setter} release inside the owner branch.`
    );
  }
}

function sourceMutationResultBinding(call) {
  let candidate = call;
  while (
    candidate.parent &&
    (ts.isAwaitExpression(candidate.parent) ||
      ts.isParenthesizedExpression(candidate.parent) ||
      ts.isAsExpression(candidate.parent) ||
      ts.isNonNullExpression(candidate.parent) ||
      ts.isSatisfiesExpression(candidate.parent))
  ) {
    candidate = candidate.parent;
  }
  return candidate.parent &&
    ts.isVariableDeclaration(candidate.parent) &&
    ts.isIdentifier(candidate.parent.name)
    ? candidate.parent
    : null;
}

function reservationUnavailableBranch(functionNode, resultName, sourceFile) {
  return descendants(
    functionNode,
    (candidate) => {
      if (!ts.isIfStatement(candidate) || !ts.isBinaryExpression(candidate.expression)) {
        return false;
      }
      const { left, operatorToken, right } = candidate.expression;
      if (
        operatorToken.kind !== ts.SyntaxKind.EqualsEqualsEqualsToken ||
        !ts.isStringLiteral(right) ||
        right.text !== 'reservation-unavailable' ||
        !ts.isPropertyAccessExpression(left) ||
        !ts.isIdentifier(left.expression) ||
        left.expression.text !== resultName ||
        left.name.text !== 'kind'
      ) {
        return false;
      }
      return candidate.getSourceFile() === sourceFile;
    }
  )[0] ?? null;
}

function enclosingFunction(node) {
  let candidate = node.parent;
  while (candidate && !ts.isFunctionLike(candidate)) {
    candidate = candidate.parent;
  }
  return candidate ?? null;
}

function ruleBody(css, selector) {
  const marker = `${selector} {`;
  const start = css.indexOf(marker);
  assert.notEqual(start, -1, `Missing editor interaction CSS rule: ${selector}`);
  const bodyStart = start + marker.length;
  const end = css.indexOf('}', bodyStart);
  assert.notEqual(end, -1, `Unterminated editor interaction CSS rule: ${selector}`);
  return css.slice(bodyStart, end);
}

function compileRuntimeModules(relativeRootNames) {
  const outputRoot = mkdtempSync(
    join(desktopRoot, 'node_modules', '.km-editor-interaction-contract-')
  );
  try {
    const rootNames = relativeRootNames.map((path) => join(sourceRoot, path));
    const program = ts.createProgram({
      rootNames,
      options: {
        esModuleInterop: true,
        forceConsistentCasingInFileNames: true,
        ignoreDeprecations: '6.0',
        lib: ['lib.es2022.d.ts', 'lib.dom.d.ts'],
        module: ts.ModuleKind.CommonJS,
        moduleResolution: ts.ModuleResolutionKind.Node10,
        noEmitOnError: true,
        outDir: outputRoot,
        rootDir: sourceRoot,
        skipLibCheck: true,
        strict: true,
        target: ts.ScriptTarget.ES2022
      }
    });
    const diagnostics = ts.getPreEmitDiagnostics(program).filter(
      (diagnostic) => diagnostic.category === ts.DiagnosticCategory.Error
    );
    assert.deepEqual(
      diagnostics.map((diagnostic) => {
        const message = ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n');
        if (!diagnostic.file || diagnostic.start === undefined) return message;
        const position = diagnostic.file.getLineAndCharacterOfPosition(diagnostic.start);
        return `${relative(desktopRoot, diagnostic.file.fileName)}:${position.line + 1}:${position.character + 1}: ${message}`;
      }),
      [],
      'Editor interaction runtime modules must compile without TypeScript errors.'
    );
    const emit = program.emit();
    assert.equal(
      emit.emitSkipped,
      false,
      'Editor interaction runtime module emission was skipped.'
    );
    return outputRoot;
  } catch (error) {
    removeRuntimeModules(outputRoot);
    throw error;
  }
}

function removeRuntimeModules(outputRoot) {
  const resolvedOutput = resolve(outputRoot);
  const resolvedTemporaryRoot = resolve(join(desktopRoot, 'node_modules'));
  assert.ok(
    resolvedOutput.startsWith(`${resolvedTemporaryRoot}${sep}`) &&
      dirname(resolvedOutput) === resolvedTemporaryRoot &&
      resolvedOutput.includes('.km-editor-interaction-contract-'),
    `Refusing to remove unexpected interaction contract path: ${resolvedOutput}`
  );
  rmSync(resolvedOutput, { force: true, recursive: true });
}

function checkSearchableOptionContract() {
  const options = [
    { label: 'Bulbasaur', value: 1 },
    { label: 'Charmander', value: 4 },
    { label: 'Pikachu', value: 25 },
    { label: 'Pichu', value: 172 }
  ];

  assert.equal(resolveSearchableOptionCommit('  pikachu  ', options), '25');
  assert.equal(resolveSearchableOptionCommit('004', options), '4');
  assert.equal(resolveSearchableOptionCommit('Bulb', options), '1');
  assert.equal(resolveSearchableOptionCommit('Pi', options), null);
  assert.equal(resolveSearchableOptionCommit('not-an-option', options), null);
  assert.equal(resolveSearchableOptionCommit('', options), null);
  assert.equal(resolveSearchableOptionCommit('', options, 'None'), '');
  assert.equal(resolveSearchableOptionCommit('none', options, 'None'), '');
  assert.equal(
    resolveSearchableOptionCommit('Shared name', [
      { label: 'Shared name', value: 10 },
      { inputLabel: 'Shared name', label: 'Different detail', value: 11 }
    ], undefined, true),
    null,
    'An exact visible label shared by distinct values must not commit an arbitrary option.'
  );
  assert.equal(
    resolveSearchableOptionCommit('Shared name', [
      { label: 'Shared name', value: 10 },
      { inputLabel: 'Shared name', label: 'Different detail', value: 10 }
    ], undefined, true),
    '10',
    'Duplicate visible labels remain unambiguous when every match has the same semantic value.'
  );
  assert.equal(
    resolveSearchableOptionCommit('10', [
      { label: '10', value: 12 },
      { label: 'Different detail', value: 10 }
    ], undefined, true),
    '10',
    'An exact semantic value must take precedence over a conflicting visible label.'
  );
  assert.equal(
    resolveSearchableOptionCommit('999', options),
    '999',
    'A valid raw integer must remain editable when an option catalog is incomplete.'
  );
  assert.equal(formatSearchableOptionValue('25', options), 'Pikachu');
  assert.deepEqual(
    getSmartOptionMatches('Pi', options).map((option) => option.value),
    [25, 172]
  );
  assert.equal(
    getSmartOptionMatches(
      '',
      Array.from({ length: 101 }, (_, value) => ({ label: `Option ${value}`, value }))
    ).length,
    101,
    'An empty query must preserve the complete option catalog.'
  );
  assert.equal(
    getSmartOptionMatches(
      'Option',
      Array.from({ length: 101 }, (_, value) => ({ label: `Option ${value}`, value }))
    ).length,
    100,
    'A non-empty searchable option query must retain its result cap.'
  );
  assert.deepEqual(
    getSmartOptionMatches('2', [
      { inputLabel: '1/2', label: 'Not Very Effective', value: 2 },
      { inputLabel: '2', label: 'Super Effective', value: 8 }
    ]).map((option) => option.value),
    [2, 8],
    'Numeric searches must include visible input labels as well as stored numeric values.'
  );

  const applyInteraction = (event) => {
    const result = transitionSearchableOptionInteraction(interactionState, event);
    interactionState = result.state;
    if (result.sourceCommit !== null) {
      sourceCalls.push(result.sourceCommit.value);
    }
    return result;
  };
  const interactionOptions = [
    ...options,
    { label: 'Altaria', value: 334 }
  ];
  const sourceCalls = [];
  let interactionState = createSearchableOptionInteractionState('Pikachu');
  applyInteraction({ formattedValue: 'Pikachu', type: 'focus' });
  applyInteraction({ query: 'A', type: 'input' });
  assert.equal(interactionState.query, 'A');
  assert.deepEqual(sourceCalls, [], 'The first query character must stay local.');
  applyInteraction({ query: 'Al', type: 'input' });
  assert.equal(interactionState.query, 'Al');
  assert.deepEqual(sourceCalls, [], 'A partial multi-character query must stay local.');
  applyInteraction({ query: 'Altaria', type: 'input' });
  applyInteraction({
    committedValue: '25',
    formattedValue: 'Pikachu',
    options: interactionOptions,
    type: 'commit'
  });
  assert.deepEqual(sourceCalls, ['334']);
  assert.deepEqual(interactionState, {
    hasUserQuery: false,
    isOpen: false,
    query: 'Altaria'
  });

  sourceCalls.length = 0;
  interactionState = createSearchableOptionInteractionState('Pikachu');
  applyInteraction({ formattedValue: 'Pikachu', type: 'focus' });
  applyInteraction({ query: 'Pi', type: 'input' });
  const ambiguousCommit = applyInteraction({
    committedValue: '25',
    formattedValue: 'Pikachu',
    options: interactionOptions,
    type: 'commit'
  });
  assert.equal(ambiguousCommit.sourceCommit, null);
  assert.deepEqual(
    sourceCalls,
    [],
    'An ambiguous commit must restore the source value without invoking its onChange path.'
  );
  assert.equal(interactionState.query, 'Pikachu');

  applyInteraction({ formattedValue: 'Pikachu', type: 'focus' });
  applyInteraction({ query: '', type: 'input' });
  const requiredBlankCommit = applyInteraction({
    committedValue: '25',
    formattedValue: 'Pikachu',
    options: interactionOptions,
    type: 'commit'
  });
  assert.equal(requiredBlankCommit.sourceCommit, null);
  assert.deepEqual(
    sourceCalls,
    [],
    'A required blank must not be sent to the source editor.'
  );
  assert.equal(interactionState.query, 'Pikachu');

  applyInteraction({ formattedValue: 'Pikachu', type: 'focus' });
  applyInteraction({ query: '', type: 'input' });
  applyInteraction({
    committedValue: '25',
    emptyOptionLabel: 'None',
    formattedValue: 'Pikachu',
    options: interactionOptions,
    type: 'commit'
  });
  assert.deepEqual(sourceCalls, ['']);
  assert.equal(interactionState.query, 'None');
}

function checkLocalValidationDiagnosticsContract() {
  const simultaneousBlankDiagnostics = createLocalEditorValidationDiagnostics(
    'workflow.trainers',
    [
      { field: 'evAttack', message: 'Attack EV is required.' },
      { field: 'evDefense', message: 'Defense EV is required.' }
    ]
  );
  assert.deepEqual(
    simultaneousBlankDiagnostics.map(({ field, severity }) => ({ field, severity })),
    [
      { field: 'evAttack', severity: 'error' },
      { field: 'evDefense', severity: 'error' }
    ],
    'Two simultaneous blank numeric drafts must produce two independent bottom diagnostics.'
  );

  let diagnosticsBySource = new Map();
  diagnosticsBySource = updateEditorDiagnosticsSource(
    diagnosticsBySource,
    'trainer-fields',
    simultaneousBlankDiagnostics
  );
  assert.equal(mergeEditorDiagnostics(...diagnosticsBySource.values()).length, 2);
  diagnosticsBySource = updateEditorDiagnosticsSource(
    diagnosticsBySource,
    'trainer-fields',
    []
  );
  assert.equal(diagnosticsBySource.has('trainer-fields'), false);
  assert.deepEqual(
    mergeEditorDiagnostics(...diagnosticsBySource.values()),
    [],
    'Withdrawing a corrected validation source must clear every diagnostic it published.'
  );
}

function checkProjectRelocationDraftContract({ reconcileRelocationCandidatePaths }) {
  const createPaths = (prefix, selectedGame = 'za') => ({
    baseExeFsPath: `${prefix}-exefs`,
    baseRomFsPath: `${prefix}-romfs`,
    gameTextLanguage: 'en',
    outputRootPath: `${prefix}-output`,
    pokemonLegendsZASupportFolderPath: `${prefix}-support`,
    saveFilePath: `${prefix}-save`,
    scarletVioletSupportFolderPath: null,
    selectedGame
  });
  const previousSource = { paths: createPaths('old'), projectId: 'project-a' };
  const refreshedSource = { paths: createPaths('new'), projectId: 'project-a' };

  assert.deepEqual(
    reconcileRelocationCandidatePaths(
      { ...previousSource.paths },
      previousSource,
      refreshedSource
    ),
    refreshedSource.paths,
    'A clean relocation draft must follow every refreshed source path.'
  );
  assert.deepEqual(
    reconcileRelocationCandidatePaths(
      { ...previousSource.paths, outputRootPath: 'typed-output' },
      previousSource,
      refreshedSource
    ),
    { ...refreshedSource.paths, outputRootPath: 'typed-output' },
    'A same-scope refresh must preserve only locally edited relocation paths.'
  );
  const switchedProject = { paths: createPaths('other'), projectId: 'project-b' };
  assert.deepEqual(
    reconcileRelocationCandidatePaths(
      { ...previousSource.paths, outputRootPath: 'typed-output' },
      previousSource,
      switchedProject
    ),
    switchedProject.paths,
    'A project switch must never carry paths from the prior project.'
  );
  const switchedGame = {
    paths: createPaths('scarlet', 'scarlet'),
    projectId: previousSource.projectId
  };
  assert.deepEqual(
    reconcileRelocationCandidatePaths(
      { ...previousSource.paths, outputRootPath: 'typed-output' },
      previousSource,
      switchedGame
    ),
    switchedGame.paths,
    'A game switch must never carry paths from the prior game.'
  );
}

async function checkCompiledRuntimeContracts(outputRoot) {
  const { calculatePayloadBytesSha256 } = require(
    join(outputRoot, 'utils', 'pendingPayloadHash.js')
  );
  const {
    createTrainerPartyClipboardFieldValues,
    createTrainerPartyClipboardRowFromFieldValues,
    resolveRowClipboardAdapterRegistration,
    rowClipboardEditorSchemas,
    rowClipboardProfileIds
  } = require(join(outputRoot, 'authoring', 'rowClipboardAdapters.js'));
  const { createScopedRowClipboardController } = require(
    join(outputRoot, 'authoring', 'rowClipboardScopedController.js')
  );
  const {
    readRowClipboardEnvelopeFromSystemClipboard,
    writeRowClipboardEnvelopeToSystemClipboard
  } = require(join(outputRoot, 'authoring', 'rowClipboardSystemClipboard.js'));
  checkProjectRelocationDraftContract(
    require(join(
      outputRoot,
      'features',
      'output-safety',
      'projectRelocationDraftState.js'
    ))
  );

  assert.equal(
    calculatePayloadBytesSha256(new Uint8Array()),
    'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855',
    'Provider-independent row clipboard hashing must preserve the SHA-256 empty vector.'
  );
  assert.equal(
    calculatePayloadBytesSha256(new TextEncoder().encode('abc')),
    'BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD',
    'Provider-independent row clipboard hashing must preserve the SHA-256 abc vector.'
  );
  assert.equal(
    calculatePayloadBytesSha256(
      new TextEncoder().encode(
        'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq'
      )
    ),
    '248D6A61D20638B8E5C026930C3E6039A33CE45964FF2167F6ECEDD419DB06C1',
    'Provider-independent row clipboard hashing must preserve a multi-block SHA-256 vector.'
  );

  const originalWindowDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'window');
  const originalNavigatorDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  try {
    const invocations = [];
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {
        __TAURI_INTERNALS__: {
          invoke: async (command, payload) => {
            invocations.push({ command, payload });
            return command === 'plugin:clipboard-manager|read_text'
              ? JSON.stringify({ source: 'tauri-native' })
              : undefined;
          }
        }
      }
    });
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: Object.defineProperty({}, 'clipboard', {
        get() {
          throw Object.assign(new Error('Web clipboard must not be resolved in Tauri.'), {
            name: 'SecurityError'
          });
        }
      })
    });

    const nativeWrite = await writeRowClipboardEnvelopeToSystemClipboard({
      source: 'tauri-native'
    });
    assert.deepEqual(nativeWrite, { kind: 'success', value: undefined });
    let invalidatedPreview = false;
    const nativeRead = await readRowClipboardEnvelopeFromSystemClipboard({
      importEnvelope: async (candidate) => candidate,
      invalidatePreview: () => {
        invalidatedPreview = true;
      }
    });
    assert.deepEqual(nativeRead, {
      kind: 'success',
      value: { source: 'tauri-native' }
    });
    assert.equal(invalidatedPreview, true);
    assert.deepEqual(
      invocations.map(({ command }) => command),
      ['plugin:clipboard-manager|write_text', 'plugin:clipboard-manager|read_text'],
      'Tauri row clipboard operations must use the native clipboard plugin.'
    );

    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {}
    });
    const guardedBrowserWrite = await writeRowClipboardEnvelopeToSystemClipboard({
      source: 'guarded-browser'
    });
    assert.deepEqual(
      guardedBrowserWrite,
      {
        feedbackKey: 'rowClipboard.feedback.clipboardUnavailable',
        kind: 'failure',
        reason: 'clipboard-unavailable'
      },
      'A throwing Web Clipboard getter must become controlled unavailable feedback.'
    );
  } finally {
    if (originalWindowDescriptor) {
      Object.defineProperty(globalThis, 'window', originalWindowDescriptor);
    } else {
      delete globalThis.window;
    }
    if (originalNavigatorDescriptor) {
      Object.defineProperty(globalThis, 'navigator', originalNavigatorDescriptor);
    } else {
      delete globalThis.navigator;
    }
  }

  const trainerWithAbsentMoves = {
    ability: 0,
    canDynamax: null,
    canGigantamax: null,
    dynamaxLevel: null,
    evs: { attack: 0, defense: 0, hp: 0, specialAttack: 0, specialDefense: 0, speed: 0 },
    form: 0,
    gender: 0,
    heldItemId: 0,
    ivs: { attack: 0, defense: 0, hp: 0, specialAttack: 0, specialDefense: 0, speed: 0 },
    level: 10,
    moveIds: [],
    nature: 1,
    shiny: false,
    slot: 0,
    speciesId: 25,
    teraType: null
  };
  const trainerFieldValues = createTrainerPartyClipboardFieldValues(
    [
      { field: 'level' },
      { field: 'move1Id' },
      { field: 'move2Id' },
      { field: 'move3Id' },
      { field: 'move4Id' }
    ],
    (field) => field === 'level' ? 10 : null,
    { move1Id: '   ' }
  );
  assert.deepEqual(
    trainerFieldValues,
    { level: '10' },
    'Populated fields must copy while unchanged null/blank move fields stay omitted.'
  );
  const absentMoveRow = createTrainerPartyClipboardRowFromFieldValues(
    'za',
    0,
    trainerWithAbsentMoves,
    trainerFieldValues
  );
  const absentMoveValues = Object.fromEntries(
    absentMoveRow.values.map(({ fieldKey, value }) => [fieldKey, value])
  );
  for (const fieldKey of ['move1Id', 'move2Id', 'move3Id', 'move4Id']) {
    assert.deepEqual(
      absentMoveValues[fieldKey],
      { kind: 'signedInteger', value: '0' },
      `${fieldKey} must retain the canonical zero when an unchanged absent move is omitted.`
    );
  }
  const draftedTrainerFieldValues = createTrainerPartyClipboardFieldValues(
    [{ field: 'level' }, { field: 'move1Id' }, { field: 'move2Id' }],
    (field) => field === 'level' ? 10 : null,
    { level: '12', move1Id: '33' }
  );
  assert.deepEqual(
    draftedTrainerFieldValues,
    { level: '12', move1Id: '33' },
    'Explicit populated and absent-field drafts must remain copyable overlays.'
  );
  const draftedMoveRow = createTrainerPartyClipboardRowFromFieldValues(
    'za',
    0,
    trainerWithAbsentMoves,
    draftedTrainerFieldValues
  );
  const draftedMoveValues = Object.fromEntries(
    draftedMoveRow.values.map(({ fieldKey, value }) => [fieldKey, value])
  );
  assert.deepEqual(draftedMoveValues.level, { kind: 'signedInteger', value: '12' });
  assert.deepEqual(draftedMoveValues.move1Id, { kind: 'signedInteger', value: '33' });
  assert.deepEqual(draftedMoveValues.move2Id, { kind: 'signedInteger', value: '0' });

  const gameProfiles = [
    ['sword', rowClipboardProfileIds.swordShield],
    ['shield', rowClipboardProfileIds.swordShield],
    ['scarlet', rowClipboardProfileIds.scarletViolet],
    ['violet', rowClipboardProfileIds.scarletViolet],
    ['za', rowClipboardProfileIds.za]
  ];
  for (const [schemaName, editor] of Object.entries(rowClipboardEditorSchemas)) {
    for (const [game, profileId] of gameProfiles) {
      const scope = { game, profileId, projectId: `contract-${schemaName}-${game}` };
      const registration = resolveRowClipboardAdapterRegistration(editor, scope);
      assert.ok(registration.games.includes(game));
      assert.ok(registration.profileIds?.includes(profileId));
      const controller = createScopedRowClipboardController(editor, scope);
      assert.deepEqual(
        controller.getSnapshot().scope,
        scope,
        `${schemaName} must construct with exactly one adapter for ${game}.`
      );
      const bridgeScope = {
        ...scope,
        gameFamily:
          game === 'sword' || game === 'shield'
            ? 'swordShield'
            : game === 'scarlet' || game === 'violet'
              ? 'scarletViolet'
              : 'legendsZA'
      };
      assert.deepEqual(
        createScopedRowClipboardController(editor, bridgeScope).getSnapshot().scope,
        scope,
        `${schemaName} must project the validated bridge scope DTO for ${game}.`
      );

      const fieldPolicy = registration.fieldPolicies[0];
      assert.ok(fieldPolicy, `${schemaName} must expose at least one copyable field.`);
      const valueKind = fieldPolicy.valueKinds[0];
      assert.ok(valueKind, `${schemaName}.${fieldPolicy.fieldKey} must allow a value kind.`);
      const sampleValue = valueKind === 'boolean'
        ? { kind: valueKind, value: true }
        : valueKind === 'dependencyReference'
          ? {
              kind: valueKind,
              value: { form: null, id: 'contract-dependency', kind: 'contract' }
            }
          : { kind: valueKind, value: valueKind === 'string' ? 'contract' : '1' };
      const sourceIdentity = {
        key: `source-${schemaName}-${game}`,
        kind: editor.rowKind
      };
      const envelope = await controller.copy({
        dependencies: [],
        editor,
        producerVersion: 'interaction-contract',
        rows: [
          {
            sourceIdentity,
            values: [{ fieldKey: fieldPolicy.fieldKey, value: sampleValue }]
          }
        ],
        source: {
          logicalIdentity: sourceIdentity,
          projectRevision: 'source-revision-1'
        }
      });
      const receivingController = createScopedRowClipboardController(editor, scope);
      const importedEnvelope = await receivingController.importEnvelope(envelope);
      assert.equal(importedEnvelope.checksum, envelope.checksum);
      const reusableClipboard = {
        readText: async () => JSON.stringify(envelope),
        writeText: async () => undefined
      };
      const firstPasteRead = await readRowClipboardEnvelopeFromSystemClipboard(
        createScopedRowClipboardController(editor, scope),
        reusableClipboard
      );
      const secondPasteRead = await readRowClipboardEnvelopeFromSystemClipboard(
        createScopedRowClipboardController(editor, scope),
        reusableClipboard
      );
      assert.equal(firstPasteRead.kind, 'success');
      assert.equal(secondPasteRead.kind, 'success');
      assert.equal(
        firstPasteRead.kind === 'success' ? firstPasteRead.value.checksum : null,
        envelope.checksum
      );
      assert.equal(
        secondPasteRead.kind === 'success' ? secondPasteRead.value.checksum : null,
        envelope.checksum,
        `${schemaName} ${game} clipboard data must remain reusable across repeated pastes.`
      );
      const targetIdentity = {
        key: `target-${schemaName}-${game}`,
        kind: editor.rowKind
      };
      const preview = receivingController.bindPreview({
        mode: registration.pasteModes[0],
        targetIdentity,
        targetRevision: 'target-revision-1'
      });
      const authorization = receivingController.requireFreshPreview(
        preview,
        'target-revision-1'
      );
      assert.equal(authorization.envelope.checksum, envelope.checksum);
      assert.equal(authorization.operationCount, 1);
      assert.deepEqual(authorization.targetIdentity, targetIdentity);
      receivingController.completePreview(preview);
      assert.equal(
        receivingController.getSnapshot().preview,
        null,
        `${schemaName} must complete a scoped ${game} copy/import/preview round trip.`
      );
    }
  }
  for (const editor of Object.values(rowClipboardEditorSchemas)) {
    assert.throws(
      () =>
        createScopedRowClipboardController(editor, {
          game: 'za',
          profileId: 'wrong-profile',
          projectId: 'contract-wrong-profile'
        }),
      (error) => error?.code === 'adapter-unavailable'
    );
  }
}

function checkStaticWiringContract() {
  const app = read('src/App.tsx');
  const appSource = ts.createSourceFile('App.tsx', app, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const rowClipboardCanonical = read('src/authoring/rowClipboardCanonical.ts');
  const rowClipboardSystemClipboard = read(
    'src/authoring/rowClipboardSystemClipboard.ts'
  );
  const trainerClipboardValues = getFunctionLike(
    appSource,
    'createTrainerPokemonSlotClipboardValues'
  ).getText(appSource);
  assert.match(
    trainerClipboardValues,
    /return createTrainerPartyClipboardFieldValues\(/u,
    'Trainer row copy must use the tested null/blank omission helper.'
  );
  assert.match(
    rowClipboardCanonical,
    /return calculatePayloadBytesSha256\(bytes\);/u,
    'Row clipboard checksums must use the provider-independent byte hash.'
  );
  assert.doesNotMatch(
    rowClipboardCanonical,
    /(?:globalThis\.)?crypto\.subtle/u,
    'Row clipboard checksums must not depend on native WebCrypto provider registration.'
  );
  assert.match(
    rowClipboardSystemClipboard,
    /from '@tauri-apps\/plugin-clipboard-manager';/u,
    'Packaged row clipboard operations must import the Tauri clipboard plugin.'
  );
  assert.match(
    rowClipboardSystemClipboard,
    /if \(hasTauriRuntime\(\)\) \{\s*return tauriSystemClipboard;/u,
    'Tauri row clipboard operations must select the native adapter before Web Clipboard.'
  );
  assert.match(
    rowClipboardSystemClipboard,
    /try \{\s*if \(typeof navigator === 'undefined'\)[\s\S]*?const clipboard = navigator\.clipboard;[\s\S]*?\} catch \{\s*return null;/u,
    'Browser clipboard resolution must contain getter failures.'
  );
  assert.doesNotMatch(
    app,
    /\browClipboardAdapterRegistrations\b/u,
    'App must not construct a controller from the multi-scope clipboard catalog.'
  );

  const clipboardHandlers = new Map([
    ['handleCopyTrainerPartyClipboard', { operation: 'copy', schemaName: 'trainerParty' }],
    ['handlePasteTrainerPartyClipboard', { operation: 'paste', schemaName: 'trainerParty' }],
    ['handleCopyPokemonLearnsetClipboard', { operation: 'copy', schemaName: 'pokemonLearnset' }],
    ['handlePastePokemonLearnsetClipboard', { operation: 'paste', schemaName: 'pokemonLearnset' }],
    ['handleCopyEncounterClipboard', { operation: 'copy', schemaName: 'encounterSlot' }],
    ['handlePasteEncounterClipboard', { operation: 'paste', schemaName: 'encounterSlot' }]
  ]);
  let scopedControllerCalls = 0;
  for (const [handlerName, { operation, schemaName }] of clipboardHandlers) {
    const handler = getFunctionLike(appSource, handlerName);
    const scopedCalls = calledIdentifier(handler, 'createScopedRowClipboardController');
    assert.equal(scopedCalls.length, 1, `${handlerName} must construct one scoped controller.`);
    assert.match(
      scopedCalls[0].arguments[0]?.getText(appSource) ?? '',
      new RegExp(`^rowClipboardEditorSchemas\\.${schemaName}$`, 'u'),
      `${handlerName} must bind the ${schemaName} adapter.`
    );
    scopedControllerCalls += scopedCalls.length;
    const controllerBinding = sourceMutationResultBinding(scopedCalls[0]);
    assert.ok(
      controllerBinding && ts.isIdentifier(controllerBinding.name),
      `${handlerName} must bind its scoped controller to a local identifier.`
    );
    const controllerName = controllerBinding.name.text;
    if (operation === 'copy') {
      const copyReceivers = descendants(
        handler,
        (candidate) =>
          ts.isCallExpression(candidate) &&
          ts.isPropertyAccessExpression(candidate.expression) &&
          ts.isIdentifier(candidate.expression.expression) &&
          candidate.expression.expression.text === controllerName &&
          candidate.expression.name.text === 'copy'
      );
      assert.equal(
        copyReceivers.length,
        1,
        `${handlerName} must execute copy on the one scoped controller it constructed.`
      );
    } else {
      const readCalls = calledIdentifier(
        handler,
        'readRowClipboardEnvelopeFromSystemClipboard'
      );
      assert.equal(
        readCalls.length,
        1,
        `${handlerName} must import exactly one system clipboard envelope.`
      );
      assert.equal(
        readCalls[0].arguments[0]?.getText(appSource),
        controllerName,
        `${handlerName} must validate the pasted envelope with the scoped controller it constructed.`
      );
      assert.equal(
        calledIdentifier(handler, 'runEditSessionMutation').length,
        0,
        `${handlerName} must not mutate the edit session before the explicit Stage action.`
      );
      const backendPasteCalls = descendants(
        handler,
        (candidate) =>
          ts.isCallExpression(candidate) &&
          ts.isPropertyAccessExpression(candidate.expression) &&
          ['previewRowClipboardPaste', 'stageRowClipboardPaste'].includes(
            candidate.expression.name.text
          )
      );
      assert.equal(
        backendPasteCalls.length,
        0,
        `${handlerName} must only import validated clipboard rows for local drafts.`
      );
      assert.match(
        handler.getText(appSource),
        /rows:\s*readResult\.value\.rows/u,
        `${handlerName} must return imported rows to the editor draft reducer.`
      );
    }

    const catches = descendants(handler, ts.isCatchClause);
    assert.equal(catches.length, 1, `${handlerName} must have one diagnostic catch boundary.`);
    assert.equal(catches[0].variableDeclaration?.name.getText(appSource), 'error');
    assert.equal(calledIdentifier(catches[0].block, 'setBridgeDiagnostics').length, 1);
    assert.equal(calledIdentifier(catches[0].block, 'toBridgeDiagnostics').length, 1);
    assert.match(
      catches[0].block.getText(appSource),
      /setBridgeDiagnostics\(toBridgeDiagnostics\(error\)\)/u,
      `${handlerName} must retain unexpected clipboard diagnostics.`
    );
  }
  assert.equal(scopedControllerCalls, 6);

  for (const operationContract of [
    {
      functionName: 'runSelectedPokemonStructuralMutation',
      operationRef: 'pokemonStructureMutationOperationRef',
      releaseCalls: [
        { argument: /^\(currentKind\)\s*=>/u, setter: 'setPokemonStructureMutationPending' }
      ]
    },
    {
      functionName: 'handleCopyLearnsetClipboardRow',
      operationRef: 'learnsetClipboardOperationRef',
      releaseCalls: [{ argument: /^false$/u, setter: 'setIsLearnsetClipboardBusy' }]
    },
    {
      functionName: 'handlePasteLearnsetClipboardRow',
      operationRef: 'learnsetClipboardOperationRef',
      releaseCalls: [
        { argument: /^false$/u, setter: 'setIsLearnsetPasteApplying' },
        { argument: /^false$/u, setter: 'setIsLearnsetClipboardBusy' }
      ]
    },
    {
      functionName: 'copyPartySlot',
      operationRef: 'partySlotClipboardOperationRef',
      releaseCalls: [{ argument: /^false$/u, setter: 'setIsPartySlotClipboardBusy' }]
    },
    {
      functionName: 'pastePartySlot',
      operationRef: 'partySlotClipboardOperationRef',
      releaseCalls: [{ argument: /^false$/u, setter: 'setIsPartySlotClipboardBusy' }]
    },
    {
      functionName: 'handleCopyEncounterClipboardSlot',
      operationRef: 'encounterClipboardOperationRef',
      releaseCalls: [{ argument: /^false$/u, setter: 'setIsEncounterClipboardBusy' }]
    },
    {
      functionName: 'handlePasteEncounterClipboardSlot',
      operationRef: 'encounterClipboardOperationRef',
      releaseCalls: [{ argument: /^false$/u, setter: 'setIsEncounterClipboardBusy' }]
    }
  ]) {
    assertOperationOwnedLockContract(appSource, operationContract);
  }

  for (const [functionName, draftSetter] of [
    ['handlePasteLearnsetClipboardRow', 'setLearnsetDraftsByPokemonId'],
    ['pastePartySlot', 'setPokemonDraftsByTrainerSlot'],
    ['handlePasteEncounterClipboardSlot', 'setDraftsBySlotKey']
  ]) {
    const handler = getFunctionLike(appSource, functionName);
    assert.equal(
      calledIdentifier(handler, 'runSessionLocalEditorSourceMutation').length,
      0,
      `${functionName} must not stage or reserve a source mutation during Paste.`
    );
    assert.ok(
      calledIdentifier(handler, draftSetter).length > 0,
      `${functionName} must write the imported values into local drafts.`
    );
  }

  const ordinarySourceMutationCallInventory = {
    ItemsSection: { onUpdateItemFields: 1 },
    SelectedItemPanel: {
      onStageItemDrafts: 1,
      onStageItemVanilla: 1,
      onUpdateItemFields: 1
    },
    PokemonSection: { onUpdatePokemonField: 4 },
    SelectedPokemonPanel: {
      onPasteLearnsetRow: 1,
      onSwapPokemonDexPlacement: 1,
      onUpdatePokemonEvolution: 5,
      onUpdatePokemonFields: 1,
      onUpdatePokemonLearnset: 5
    },
    ZaPokemonDexPlacementEditor: { onStageSwap: 1 },
    SelectedMovePanel: { onStageMoveVanilla: 1, onUpdateMoveFields: 1 },
    SelectedTextPanel: { onUpdateTextEntry: 1 },
    TrainersSection: { onUpdateTrainerFields: 1 },
    SelectedTrainerPanel: {
      onPasteTrainerPartyClipboard: 1,
      onUpdateTrainerField: 1,
      onUpdateTrainerFields: 2
    }
  };
  const explicitImmediateActionInventory = {
    ItemsSection: { onUpdateItemFields: 1 },
    PokemonSection: { onUpdatePokemonField: 4 },
    SelectedPokemonPanel: { onPasteLearnsetRow: 1 },
    SelectedTrainerPanel: { onPasteTrainerPartyClipboard: 1 },
    TrainersSection: { onUpdateTrainerFields: 1 },
    ZaPokemonDexPlacementEditor: { onStageSwap: 1 }
  };
  const actualImmediateActions = {};
  for (const [componentName, expectedCounts] of Object.entries(
    ordinarySourceMutationCallInventory
  )) {
    const component = getFunctionLike(appSource, componentName);
    const actualCounts = {};
    const sourceMutationCalls = descendants(
      component,
      (candidate) =>
        ts.isCallExpression(candidate) &&
        ts.isIdentifier(candidate.expression) &&
        /^on(?:Paste|Stage|Swap|Update)[A-Z]/u.test(candidate.expression.text)
    );
    for (const call of sourceMutationCalls) {
      const callName = call.expression.text;
      actualCounts[callName] = (actualCounts[callName] ?? 0) + 1;

      let ancestor = call.parent;
      let isSessionLocalStageMutation = false;
      while (ancestor && ancestor !== component) {
        if (
          ts.isCallExpression(ancestor) &&
          ts.isIdentifier(ancestor.expression) &&
          [
            'runSessionLocalEditorSourceMutation',
            'runSelectedPokemonStructuralMutation',
            'runSelectedPokemonSourceMutation',
            'runSelectedTrainerSourceMutation'
          ].includes(ancestor.expression.text)
        ) {
          isSessionLocalStageMutation = true;
          break;
        }
        ancestor = ancestor.parent;
      }
      if (!isSessionLocalStageMutation) {
        actualImmediateActions[componentName] ??= {};
        actualImmediateActions[componentName][callName] =
          (actualImmediateActions[componentName][callName] ?? 0) + 1;
      }
    }
    assert.deepEqual(
      actualCounts,
      expectedCounts,
      `${componentName} source mutations changed without an interaction-contract classification.`
    );
  }
  assert.deepEqual(
    actualImmediateActions,
    explicitImmediateActionInventory,
    'Every ordinary-editor source mutation must use the session-local Stage reservation unless it is an explicitly inventoried immediate action.'
  );

  const searchableInputSource = ts.createSourceFile(
    'SearchableOptionInput.tsx',
    read('src/components/SearchableOptionInput.tsx'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const searchableInput = getFunctionLike(searchableInputSource, 'SearchableOptionInput');
  const inputChange = getFunctionLike(
    ts.createSourceFile(
      'SearchableOptionInput.tsx',
      searchableInput.getText(searchableInputSource),
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TSX
    ),
    'handleInputChange'
  );
  assert.equal(
    calledIdentifier(inputChange, 'onChange').length,
    0,
    'Typing a searchable query must remain local until an explicit commit.'
  );
  assert.equal(
    calledIdentifier(inputChange, 'transitionSearchableOptionInteraction').length,
    1,
    'Every typed searchable query must pass through the tested interaction reducer.'
  );
  assert.match(searchableInput.getText(searchableInputSource), /onBlur=\{commitTypedOption\}/u);
  assert.match(
    searchableInput.getText(searchableInputSource),
    /result\.sourceCommit !== null[\s\S]*?onChange\(result\.sourceCommit\.value\)/u,
    'Searchable option source updates must be gated by the reducer commit result.'
  );
  assert.match(
    searchableInput.getText(searchableInputSource),
    /event\.key === 'Escape'[\s\S]*?restoreCommittedValue\(\)/u
  );
  assert.match(
    searchableInput.getText(searchableInputSource),
    /event\.key === 'Enter' &&\s*!event\.nativeEvent\.isComposing/u,
    'Searchable option Enter must not commit while an IME composition is active.'
  );
  assert.match(
    searchableInput.getText(searchableInputSource),
    /const noOptionsStatus =[\s\S]*?className="searchable-option-empty" role="status"[\s\S]*?className="searchable-option-listbox"[\s\S]*?id=\{listboxId\}[\s\S]*?role="listbox"[\s\S]*?\{optionRows\}\s*<\/div>\s*\{noOptionsStatus\}/u,
    'A KM option control must render empty-result status as a sibling of its valid listbox structure.'
  );

  const battleCafeSource = ts.createSourceFile(
    'BattleCafeRewardsSection.tsx',
    read('src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const battleCafeItemPicker = getFunctionLike(battleCafeSource, 'BattleCafeItemPicker');
  const battleCafeSearchableControls = descendants(
    battleCafeItemPicker,
    (candidate) =>
      ts.isJsxSelfClosingElement(candidate) &&
      candidate.tagName.getText(battleCafeSource) === 'SearchableOptionInput'
  );
  assert.equal(
    battleCafeSearchableControls.length,
    1,
    'Battle Cafe item selection must delegate keyboard, Escape, and IME behavior to one shared KM combobox.'
  );
  assert.match(
    battleCafeSearchableControls[0].getText(battleCafeSource),
    /isFiniteCatalog[\s\S]*?options=\{options\}[\s\S]*?value=\{value\.toString\(\)\}/u,
    'Battle Cafe item selection must use a finite semantic catalog rather than bespoke query state.'
  );
  assert.match(
    battleCafeSearchableControls[0].getText(battleCafeSource),
    /noOptionsLabel=\{t\('battleCafeRewards\.row\.noItems'\)\}/u,
    'Battle Cafe item search must retain its localized no-results feedback.'
  );

  const semanticExploreSource = read(
    'src/features/semantic-explore/SemanticExploreSection.tsx'
  );
  assert.match(
    semanticExploreSource,
    /const resolvedTo = layers\.includes\(to\)[\s\S]*?data-km-source-site="semantic-explore-changes-to"\s*disabled=\{resolvedTo === ''\}\s*id="semantic-explore-changes-to"[\s\S]*?value=\{resolvedTo\}/u,
    'Semantic Changes must show a disabled blank destination instead of a stale unavailable layer.'
  );

  const localValidationPublisher = getFunctionLike(
    appSource,
    'PublishedLocalEditorValidationDiagnostics'
  );
  assert.equal(
    calledIdentifier(
      localValidationPublisher,
      'createLocalEditorValidationDiagnostics'
    ).length,
    1,
    'App validation publishing must use the runtime-tested multi-field diagnostic builder.'
  );
  assert.equal(
    calledIdentifier(localValidationPublisher, 'usePublishCommonEditorDiagnostics').length,
    1,
    'App validation publishing must register the complete field diagnostic list at the bottom.'
  );

  const commonEditorDiagnostics = read('src/components/CommonEditorDiagnostics.tsx');
  assert.match(
    commonEditorDiagnostics,
    /if \(!enabled \|\| !publishingScopeEnabled \|\| !registration\)/u,
    'Common diagnostics publishing must stop when its visible-editor scope is disabled.'
  );
  assert.match(
    commonEditorDiagnostics,
    /return \(\) => registration\.withdraw\(sourceId\);/u,
    'Disabling a diagnostics scope must withdraw diagnostics it previously published.'
  );
  assert.match(
    app,
    /<div className="km-retained-workbench" hidden=\{activeSection !== 'workbench'\}>\s*<CommonEditorDiagnosticsPublishingScope enabled=\{activeSection === 'workbench'\}>\s*<WorkbenchHomeSection/u,
    'The retained Workbench must publish diagnostics only while it is the visible editor.'
  );

  const hyperTraining = getFunctionLike(appSource, 'HyperTrainingSection');
  const hyperTrainingCutoffInput = descendants(
    hyperTraining,
    (candidate) =>
      ts.isJsxSelfClosingElement(candidate) &&
      candidate.tagName.getText(appSource) === 'input' &&
      candidate.attributes.properties.some(
        (property) =>
          ts.isJsxAttribute(property) &&
          property.name.getText(appSource) === 'id' &&
          ts.isStringLiteral(property.initializer) &&
          property.initializer.text === 'hyper-training-cutoff'
      )
  );
  assert.equal(hyperTrainingCutoffInput.length, 1, 'Hyper Training must expose one cutoff input.');
  const cutoffAttributes = new Map(
    hyperTrainingCutoffInput[0].attributes.properties
      .filter(ts.isJsxAttribute)
      .map((attribute) => [attribute.name.getText(appSource), attribute.initializer])
  );
  assert.equal(cutoffAttributes.get('type')?.getText(appSource), '"text"');
  assert.equal(cutoffAttributes.get('inputMode')?.getText(appSource), '"numeric"');
  assert.equal(
    cutoffAttributes.get('onChange')?.getText(appSource),
    '{(event) => setLevelInput(event.target.value)}',
    'Hyper Training typing must preserve raw partial text without parsing or clamping.'
  );
  assert.equal(
    cutoffAttributes.has('onBlur'),
    false,
    'Hyper Training must not silently rewrite an invalid partial draft on blur.'
  );
  assert.equal(
    calledIdentifier(hyperTraining, 'parseHyperTrainingLevelInput').length,
    1,
    'Hyper Training validation must parse the raw draft only as derived state.'
  );

  const sessionLocalSourceMutationCalls = descendants(
    appSource,
    (candidate) =>
      ts.isCallExpression(candidate) &&
      ts.isIdentifier(candidate.expression) &&
      candidate.expression.text === 'runSessionLocalEditorSourceMutation'
  );
  assert.ok(
    sessionLocalSourceMutationCalls.length >= 9,
    'Every Item, Pokemon, Move, Text, and Trainer staged write must reserve its session-local snapshot.'
  );
  for (const call of sessionLocalSourceMutationCalls) {
    const options = call.arguments[0];
    const line = appSource.getLineAndCharacterOfPosition(call.getStart(appSource)).line + 1;
    assert.ok(
      options &&
        ts.isObjectLiteralExpression(options) &&
        ['binding', 'didMutate', 'mutation', 'reduceLatestPayload'].every((name) =>
          options.properties.some(
            (property) =>
              (ts.isPropertyAssignment(property) ||
                ts.isShorthandPropertyAssignment(property)) &&
              property.name.getText(appSource) === name
          )
        ),
      'Every delayed Stage mutation must reserve its submitted snapshot and reduce the latest local payload after success.'
    );
    const resultBinding = sourceMutationResultBinding(call);
    assert.ok(
      resultBinding,
      `App.tsx:${line} must retain the Stage-mutation result before deciding whether to continue.`
    );
    const resultName = resultBinding.name.text;
    const handler = enclosingFunction(resultBinding);
    assert.ok(
      handler,
      `App.tsx:${line} Stage mutation must be owned by an explicit handler boundary.`
    );
    const unavailableBranch = reservationUnavailableBranch(
      handler,
      resultName,
      appSource
    );
    assert.ok(
      unavailableBranch,
      `App.tsx:${line} must explicitly branch on ${resultName}.kind === 'reservation-unavailable'.`
    );
    assert.match(
      unavailableBranch.thenStatement.getText(appSource),
      /sessionLocalDraftMutationBusyMessage/u,
      `App.tsx:${line} must explain a concurrent Stage reservation without reporting a storage failure.`
    );
    assert.ok(
      descendants(
        unavailableBranch.thenStatement,
        (candidate) =>
          ts.isCallExpression(candidate) &&
          ts.isIdentifier(candidate.expression) &&
          /^set[A-Z]\w*(?:Diagnostics|Error|Feedback)$/u.test(candidate.expression.text)
      ).length >= 1,
      `App.tsx:${line} must publish visible feedback when a concurrent Stage reservation is refused.`
    );
    assert.ok(
      descendants(unavailableBranch.thenStatement, ts.isReturnStatement).length >= 1,
      `App.tsx:${line} must stop the refused Stage action after showing its error.`
    );
  }

  const sourceMutationRunnerSource = ts.createSourceFile(
    'sessionLocalEditorSourceMutation.ts',
    read('src/components/sessionLocalEditorSourceMutation.ts'),
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TS
  );
  const sourceMutationRunner = getFunctionLike(
    sourceMutationRunnerSource,
    'runSessionLocalEditorSourceMutation'
  );
  const sourceMutationRunnerText = sourceMutationRunner.getText(sourceMutationRunnerSource);
  assert.match(
    sourceMutationRunnerText,
    /reserveDraftSourceMutation\(\)[\s\S]*?await options\.mutation\(\)[\s\S]*?commitDraftSourceMutation\(/u,
    'Stage must reserve the local snapshot before the backend write and commit only after success.'
  );
  assert.match(
    sourceMutationRunnerText,
    /!options\.didMutate\(result\)[\s\S]*?cancelDraftSourceMutation\(reservation\)/u,
    'A backend response that made no source change must release its Stage reservation.'
  );
  assert.match(
    sourceMutationRunnerText,
    /catch \(error\) \{\s*options\.binding\.cancelDraftSourceMutation\(reservation\);\s*throw error;/u,
    'A thrown Stage write must release its reservation before propagating the failure.'
  );
  assert.doesNotMatch(
    sourceMutationRunnerText,
    /isOrdinaryEditorDraftBindingReady|\.flush\(|\.save\(|\.update\(/u,
    'The Stage runner must not depend on durable draft readiness or persistence.'
  );

  const sessionLocalBindingHook = getFunctionLike(
    appSource,
    'useSessionLocalEditorDraftBinding'
  );
  const sessionLocalBindingHookText = sessionLocalBindingHook.getText(appSource);
  assert.match(
    sessionLocalBindingHookText,
    /latestPayloadRef = useRef\(payload\);\s*latestPayloadRef\.current = payload;/u,
    'The session-local binding must observe the newest in-memory payload while Stage is in flight.'
  );
  assert.match(
    sessionLocalBindingHookText,
    /const latestPayload = latestPayloadRef\.current;[\s\S]*?nextPayload = reduceLatestPayload\(latestPayload\);[\s\S]*?applyHydratedPayload\(nextPayload\)/u,
    'Stage completion must reduce the latest payload so typing performed during Stage is preserved.'
  );
  assert.match(
    sessionLocalBindingHookText,
    /mutationReservationRef\.current !== null[\s\S]*?return null/u,
    'One record must refuse overlapping Stage writes instead of racing their snapshots.'
  );
  assert.doesNotMatch(
    sessionLocalBindingHookText,
    /useOrdinaryEditorDraft|projectDraftRegistry|\.flush\(|\.reload\(|\.update\(/u,
    'Session-local ordinary bindings must not autosave, hydrate, or wait on draft storage.'
  );

  assert.equal(
    [...app.matchAll(/useSessionLocalEditorDraftBinding\(/gu)].length,
    5,
    'Items, Pokemon, Moves, Text, and Trainers must each use one session-local binding.'
  );
  assert.equal(
    [...app.matchAll(/useOrdinaryEditorDraft\(/gu)].length,
    0,
    'Ordinary editors must never mount the durable autosave hook.'
  );
  assert.doesNotMatch(
    app,
    /\bdraft\.update\s*\(|\bOrdinaryEditorDraftStatus\b|<OrdinaryEditorDraftProvider\b/u,
    'Ordinary editors must not autosave per keystroke or render durable draft lifecycle UI.'
  );
  for (const section of ['items', 'pokemon', 'moves', 'text', 'trainers']) {
    assert.equal(
      [...app.matchAll(new RegExp(`useRegisterEditorDraftDirty\\(\\s*['"]${section}['"]`, 'gu'))].length,
      1,
      `${section} must register its session-local dirty state exactly once.`
    );
  }

  const pokemonPanelText = getFunctionLike(
    appSource,
    'SelectedPokemonPanel'
  ).getText(appSource);
  assert.doesNotMatch(
    pokemonPanelText,
    /isPokemonDraft(?:Editable|Ready)/u,
    'Pokemon copy, paste, and fields must not be locked by a durable draft lifecycle.'
  );
  const trainerPanelText = getFunctionLike(
    appSource,
    'SelectedTrainerPanel'
  ).getText(appSource);
  assert.doesNotMatch(
    trainerPanelText,
    /isTrainerDraft(?:Editable|Ready)/u,
    'Trainer copy, paste, and fields must not be locked by a durable draft lifecycle.'
  );
  const openTrainerPartyMenu = getFunctionLike(appSource, 'openPartySlotContextMenu');
  assert.equal(
    calledIdentifier(openTrainerPartyMenu, 'onSelectSlot').length,
    0,
    'Opening a Trainer party menu must not select the target and collapse the inspector; paste owns target selection after it succeeds.'
  );
  const pasteTrainerPartySlot = getFunctionLike(appSource, 'pastePartySlot');
  assert.equal(
    calledIdentifier(pasteTrainerPartySlot, 'onSelectSlot').length,
    1,
    'A successful Trainer party paste must select its target exactly once.'
  );
  assert.match(
    app,
    /const bottomDiagnostics = useMemo\([\s\S]*?deduplicateDiagnostics\(\[[\s\S]*?\.\.\.bridgeDiagnostics,[\s\S]*?\.\.\.editValidationDiagnostics/u,
    'Bottom diagnostics must combine bridge and local edit-validation diagnostics.'
  );
  assert.doesNotMatch(
    app,
    /getOrdinaryEditorDraftDiagnostics/u,
    'Bottom diagnostics must not include removed ordinary draft storage diagnostics.'
  );
  assert.match(
    app,
    /<CommonBottomDiagnosticsSection diagnostics=\{bottomDiagnostics\} \/>/u,
    'The shared bottom diagnostics panel must always receive the combined diagnostic set.'
  );
  assert.match(
    app,
    /void projectDraftRegistry\.load\([\s\S]*?advancedAuthoringProjectDraftAdapter/u,
    'Advanced Authoring must retain its explicit project draft restore path.'
  );
  assert.match(
    app,
    /await projectDraftRegistry\.save\([\s\S]*?advancedAuthoringProjectDraftAdapter/u,
    'Advanced Authoring must retain its explicit project draft save path.'
  );

  const projectRelocation = read(
    'src/features/output-safety/ProjectRelocationPanel.tsx'
  );
  assert.match(
    projectRelocation,
    /const previousSource = previousSourceRef\.current;\s*previousSourceRef\.current = source;\s*setCandidatePaths\(\(current\) =>\s*reconcileRelocationCandidatePaths\(\s*current,\s*previousSource,\s*source/u,
    'Relocation reconciliation must capture the previous source before scheduling its functional state update.'
  );
  assert.doesNotMatch(
    projectRelocation,
    /setCandidatePaths\(\(current\) =>[\s\S]{0,180}?previousSourceRef\.current/u,
    'A deferred relocation updater must not read a ref that already points at the next source.'
  );
  const styles = read('src/styles.css');
  const fullSpanRule = styles.match(
    /(\.trainer-inspector > \.panel-heading,[\s\S]*?\.trainer-inspector > \.empty-copy) \{([\s\S]*?)\}/u
  );
  assert.ok(fullSpanRule, 'Missing trainer inspector full-span selector group.');
  for (const selector of [
    '.trainer-inspector > .pokemon-summary-grid',
    '.trainer-inspector > .technical-tool-notice',
    '.trainer-inspector > .trainer-identity-actions'
  ]) {
    assert.ok(fullSpanRule[1].includes(selector), `${selector} must remain full-span.`);
  }
  assert.match(fullSpanRule[2], /grid-column:\s*1 \/ -1;/u);

  const sessionTitleRule = ruleBody(styles, '.editor-session-bar-title');
  for (const declaration of [
    'display: inline-flex;',
    'align-items: center;',
    'min-width: 0;',
    'max-width: 100%;'
  ]) {
    assert.ok(sessionTitleRule.includes(declaration), `Session bar title must retain ${declaration}`);
  }
  assert.ok(
    ruleBody(styles, '.editor-session-bar-summary').includes('align-items: center;'),
    'Session bar status and title must retain shared cross-axis alignment.'
  );
  assert.match(
    styles,
    /@container km-focused-editor \(max-width: 56rem\)[\s\S]*?\.trainer-identity-action-grid \{\s*grid-template-columns: minmax\(0, 1fr\);/u,
    'Trainer identity actions must collapse to one column at the focused-editor breakpoint.'
  );
}

checkSearchableOptionContract();
checkLocalValidationDiagnosticsContract();
checkStaticWiringContract();

const runtimeOutput = compileRuntimeModules([
  'authoring/rowClipboardAdapters.ts',
  'authoring/rowClipboardScopedController.ts',
  'authoring/rowClipboardSystemClipboard.ts',
  'features/output-safety/projectRelocationDraftState.ts'
]);
try {
  await checkCompiledRuntimeContracts(runtimeOutput);
} finally {
  removeRuntimeModules(runtimeOutput);
}
