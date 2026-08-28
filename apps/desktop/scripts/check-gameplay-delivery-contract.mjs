// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

const section = read('../src/features/gameplay-settings/GameplaySettingsSection.tsx');
assert.match(
  section,
  /type GameplaySettingsDeliveryMode = 'fixed' \| 'runtime'/,
  'Gameplay Settings must retain one explicit fixed-versus-runtime delivery choice.'
);
assert.match(
  section,
  /hidden=\{deliveryMode !== 'fixed'\}[\s\S]*?hidden=\{deliveryMode !== 'runtime'\}/,
  'Only the chosen gameplay delivery workflow may be visible.'
);
assert.match(
  section,
  /<InGameSettingsPackagePanel[\s\S]*?onStateChange=\{setInGamePackageState\}/,
  'The hidden runtime workflow must remain mounted so mutual-exclusion checks stay active.'
);
assert.match(
  section,
  /inGamePackageLocksStaticEditor[\s\S]*?staticEditorLockedByPackage[\s\S]*?onStaticEditorLockChange=\{setInGamePackageLocksStaticEditor\}/,
  'The fixed editor lock must consume the package inspection capability instead of inferring ownership from a generic conflict state.'
);

const panel = read('../src/features/gameplay-settings/InGameSettingsPackagePanel.tsx');
assert.doesNotMatch(
  panel,
  /RuntimeControlCard/,
  'Runtime controls must not be presented as a duplicate set of KM inputs.'
);
assert.match(
  panel,
  /\(\['atmosphere', 'ryujinx', 'eden'\] as const\)/,
  'Native-menu installation guidance must cover every documented target boundary.'
);
assert.match(
  panel,
  /generatedTitleSourcePaths[\s\S]*?atmosphere\/contents\/\$\{titleId\}\/exefs[\s\S]*?atmosphere\/contents\/\$\{titleId\}\/romfs[\s\S]*?generatedSettingsSourcePath/,
  'Native-menu installation guidance must show the two generated title-layer source directories without shell brace shorthand.'
);
assert.match(
  panel,
  /<Emulator data folder>\/mods\/contents\/\$\{titleId\}\/KM Gameplay Settings\/exefs[\s\S]*?<Emulator data folder>\/mods\/contents\/\$\{titleId\}\/KM Gameplay Settings\/romfs[\s\S]*?<Eden data folder>\/load\/\$\{titleId\}\/KM Gameplay Settings\/exefs[\s\S]*?<Eden data folder>\/load\/\$\{titleId\}\/KM Gameplay Settings\/romfs/,
  'Native-menu installation guidance must distinguish generated sources from exact emulator title-layer destinations.'
);
assert.doesNotMatch(
  panel,
  /\{exefs,romfs\}/,
  'Copy instructions must use literal directories instead of shell brace shorthand.'
);
assert.match(
  panel,
  /<Emulated SD root>\/\$\{settingsJournalPath\}[\s\S]*?<Emulator data folder>\/sdcard\/\$\{settingsJournalPath\}[\s\S]*?<Configured emulated SD root>\/\$\{settingsJournalPath\}[\s\S]*?<Eden data folder>\/sdmc\/\$\{settingsJournalPath\}/,
  'Native-menu installation guidance must show configured and default writable journal destinations for each emulator.'
);
assert.doesNotMatch(
  panel,
  /cheatFileName|atmosphereCheatPath|ryujinxCheatPath|openFixedMode/,
  'The native-menu panel must not retain cheat-package routing or block supported emulators.'
);
assert.match(
  panel,
  /onStaticEditorLockChange\?\.\(snapshot\?\.blocksStaticEditor \?\? false\)/,
  'The native-menu panel must forward the backend-owned fixed-editor lock capability exactly.'
);
assert.doesNotMatch(
  panel,
  /packageSnapshotLocksStaticEditor/,
  'The frontend must not infer fixed-editor ownership from package state names.'
);
assert.match(
  panel,
  /snapshot\.executableInput\.compatibility === 'compatiblePreservable'[\s\S]*?state\.readyCompatible/,
  'A compatible standalone executable must receive an explicit ready-to-compose status.'
);
assert.match(
  panel,
  /preview\.composition\.strategy[\s\S]*?preview\.readDependencies\.map/,
  'Package review must surface the executable composition strategy and every bounded read-only input.'
);
assert.match(
  panel,
  /preview\.readDependenciesTruncated[\s\S]*?review\.readDependenciesTruncated/,
  'A bounded dependency review must disclose when additional inputs are omitted from display.'
);
for (const compositionFact of [
  'sourcePreserved',
  'preservesBytesOutsideOwnedRegions',
  'ownedRegionCount'
]) {
  assert.match(
    panel,
    new RegExp(`preview\\.composition\\.${compositionFact}`),
    `Package review must render the typed ${compositionFact} fact.`
  );
}

const apiContracts = read('../../../src/KM.Api/RuntimeSettings/InGameSettingsPackageContracts.cs');
assert.match(
  apiContracts,
  /record InGameSettingsExecutableInputAssessmentDto\([\s\S]*?SourceRelativePath,[\s\S]*?SourceSha256,[\s\S]*?SourceLengthBytes\)/,
  'The API snapshot must carry a typed executable source assessment and bounded fingerprint facts.'
);
assert.match(
  apiContracts,
  /record PreviewInGameSettingsPackageResponse\([\s\S]*?ReadDependencies,[\s\S]*?ReadDependenciesTruncated,[\s\S]*?Composition\)/,
  'The API preview must expose bounded read dependencies and executable composition facts.'
);

const bridgeJson = read('../../../src/KM.Api/Bridge/BridgeJson.cs');
for (const enumName of [
  'InGameSettingsExecutableInputSourceDto',
  'InGameSettingsExecutableCompatibilityDto',
  'InGameSettingsPackageReadDependencyRoleDto',
  'InGameSettingsExecutableCompositionStrategyDto'
]) {
  assert.match(
    bridgeJson,
    new RegExp(`JsonStringEnumConverter<${enumName}>\\(JsonNamingPolicy\\.CamelCase, false\\)`),
    `${enumName} must cross the bridge as a closed camel-case string enum.`
  );
}

const bridgeContracts = read('../src/bridge/inGameSettingsPackageContracts.ts');
assert.match(
  bridgeContracts,
  /executableInput: inGameSettingsExecutableInputAssessmentSchema/,
  'The frontend bridge must validate the executable input assessment.'
);
assert.match(
  bridgeContracts,
  /readDependencies: z[\s\S]*?inGameSettingsPackageReadDependencySchema[\s\S]*?inGameSettingsPackageMaximumReturnedReadDependencies/,
  'The frontend bridge must bound and validate preview read dependencies.'
);
assert.match(
  bridgeContracts,
  /readDependenciesTruncated: z\.boolean\(\)/,
  'The frontend bridge must validate whether the bounded dependency display is truncated.'
);
assert.match(
  bridgeContracts,
  /composition: inGameSettingsExecutableCompositionSchema/,
  'The frontend bridge must validate executable composition facts.'
);

const styles = read('../src/features/gameplay-settings/GameplaySettingsSection.css');
assert.match(
  styles,
  /\.gameplay-settings__mode-content\[hidden\] \{\s*display: none;/,
  'Hidden gameplay workflows must remain visually and interactively absent.'
);

const resourcesDirectory = new URL('../src/localization/resources/', import.meta.url);
const requiredKeys = [
  'gameplaySettings.delivery.title',
  'gameplaySettings.delivery.fixedTitle',
  'gameplaySettings.delivery.fixedDescription',
  'gameplaySettings.delivery.runtimeTitle',
  'gameplaySettings.delivery.runtimeDescription',
  'gameplaySettings.inGamePackage.availableControls',
  'gameplaySettings.inGamePackage.installationTitle',
  'gameplaySettings.inGamePackage.installationDescription',
  'gameplaySettings.inGamePackage.target.atmosphereStatus',
  'gameplaySettings.inGamePackage.target.ryujinxStatus',
  'gameplaySettings.inGamePackage.target.edenStatus',
  'gameplaySettings.inGamePackage.target.atmosphereDescription',
  'gameplaySettings.inGamePackage.target.ryujinxDescription',
  'gameplaySettings.inGamePackage.target.edenDescription',
  'gameplaySettings.inGamePackage.target.atmospherePathLabel',
  'gameplaySettings.inGamePackage.target.sourcePathLabel',
  'gameplaySettings.inGamePackage.target.settingsPathLabel',
  'gameplaySettings.inGamePackage.target.destinationPathLabel',
  'gameplaySettings.inGamePackage.target.destinationSettingsPathLabel',
  'gameplaySettings.inGamePackage.target.defaultSettingsPathLabel',
  'gameplaySettings.inGamePackage.contentsNoDll',
  'gameplaySettings.inGamePackage.state.readyCompatible.title',
  'gameplaySettings.inGamePackage.state.readyCompatible.description',
  'gameplaySettings.inGamePackage.executableInput.compatiblePreservable.title',
  'gameplaySettings.inGamePackage.executableInput.compatiblePreservable.description',
  'gameplaySettings.inGamePackage.executableInput.incompatibleOwnedRegion.title',
  'gameplaySettings.inGamePackage.executableInput.unreadableOrAmbiguous.description',
  'gameplaySettings.inGamePackage.executableInput.reason.sourceReviewUnavailable',
  'gameplaySettings.inGamePackage.executableInput.reason.unsupportedBaseInput',
  'gameplaySettings.inGamePackage.executableInput.reason.runtimeSlotOccupied',
  'gameplaySettings.inGamePackage.executableInput.reason.standaloneOutputNotLedgerOwned',
  'gameplaySettings.inGamePackage.executableInput.reason.verifiedNativeRegionConflict',
  'gameplaySettings.inGamePackage.executableInput.reason.ledgerOwnedPreservableOutput',
  'gameplaySettings.inGamePackage.executableInput.reason.other',
  'gameplaySettings.inGamePackage.review.compositionTitle',
  'gameplaySettings.inGamePackage.review.composition.compatibleStandalone',
  'gameplaySettings.inGamePackage.review.sourcePreserved',
  'gameplaySettings.inGamePackage.review.unownedBytesPreserved',
  'gameplaySettings.inGamePackage.review.readDependenciesTitle',
  'gameplaySettings.inGamePackage.review.readDependency.executableCompositionSource',
  'gameplaySettings.inGamePackage.review.readDependency.expectedMissing',
  'gameplaySettings.inGamePackage.review.readDependenciesTruncated',
  'gameplaySettings.inGamePackage.confirm.installCompatible'
];
for (const fileName of readdirSync(resourcesDirectory).filter((name) => name.endsWith('.json'))) {
  const resource = JSON.parse(read(`../src/localization/resources/${fileName}`)).keys;
  for (const key of requiredKeys) {
    assert.equal(
      typeof resource[key],
      'string',
      `${fileName} must provide localized gameplay delivery copy for ${key}.`
    );
    assert.ok(resource[key].trim(), `${fileName} must not leave ${key} blank.`);
  }
}

const english = JSON.parse(read('../src/localization/resources/en.json')).keys;
assert.equal(
  english['gameplaySettings.inGamePackage.target.ryujinxStatus'],
  'Manual copy',
  'Ryujinx must not be described as a managed KM installation.'
);
assert.equal(
  english['gameplaySettings.inGamePackage.target.edenStatus'],
  'Manual copy',
  'Eden must expose the verified native ExeFS and RomFS installation path.'
);
for (const key of Object.keys(english).filter((key) =>
  key.startsWith('gameplaySettings.inGamePackage.')
  || key.startsWith('gameplaySettings.delivery.runtime')
)) {
  assert.doesNotMatch(
    english[key],
    /cheat package|cheat definition|Manage Cheats|toggles\.txt|build-ID \.txt/i,
    `Native-menu copy must not retain the retired cheat delivery model in ${key}.`
  );
}
assert.match(
  english['gameplaySettings.inGamePackage.howToDescription'],
  /existing game-owned menu[\s\S]*No cheat manager or external overlay/i,
  'Public copy must state that controls use a game-owned menu without an external manager.'
);
