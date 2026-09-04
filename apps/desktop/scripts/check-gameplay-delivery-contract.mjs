// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

const section = read('../src/features/gameplay-settings/GameplaySettingsSection.tsx');
assert.match(
  section,
  /<InGameSettingsPackagePanel[\s\S]*?scope=\{stableScope\}/,
  'Gameplay Settings must open directly into the native in-game controls workflow.'
);
assert.doesNotMatch(
  section,
  /GameplaySettingsDeliveryMode|deliveryMode|previewGameplaySettingsUpdate|applyGameplaySettingsUpdate|getGameplaySettings/,
  'Gameplay Settings must not retain the retired fixed executable editor or delivery selector.'
);

const panel = read('../src/features/gameplay-settings/InGameSettingsPackagePanel.tsx');
assert.match(
  panel,
  /reviewRegionRef\.current\?\.focus\(\)[\s\S]*?aria-live="polite"[\s\S]*?ref=\{reviewRegionRef\}[\s\S]*?tabIndex=\{-1\}/,
  'The dynamically opened native-package review must announce itself and receive keyboard focus.'
);
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
  /const installationTargetSelectionBusy = busy !== null;[\s\S]*?aria-busy=\{installationTargetSelectionBusy \|\| undefined\}[\s\S]*?aria-controls="in-game-settings-installation-detail"[\s\S]*?disabled=\{installationTargetSelectionBusy\}/,
  'Installation targets must be natively disabled and expose busy state while their request context is changing.'
);
assert.match(
  panel,
  /generatedTitleSourcePaths[\s\S]*?atmosphere\/contents\/\$\{titleId\}\/exefs[\s\S]*?atmosphere\/contents\/\$\{titleId\}\/romfs[\s\S]*?generatedSettingsSourcePath/,
  'Native-menu installation guidance must show the two generated title-layer source directories without shell brace shorthand.'
);
assert.match(
  panel,
  /<Emulator data folder>\/mods\/contents\/\$\{titleId\}\/KM-Gameplay-Settings\/exefs[\s\S]*?<Emulator data folder>\/mods\/contents\/\$\{titleId\}\/KM-Gameplay-Settings\/romfs[\s\S]*?<Eden data folder>\/load\/\$\{titleId\}\/KM-Gameplay-Settings\/exefs[\s\S]*?<Eden data folder>\/load\/\$\{titleId\}\/KM-Gameplay-Settings\/romfs/,
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
assert.doesNotMatch(
  panel,
  /staticEditor|staticSettingsAreVanilla|vanillaRequired|openFixedMode/,
  'The native-menu panel must not depend on the retired fixed executable editor.'
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

const resourcesDirectory = new URL('../src/localization/resources/', import.meta.url);
const requiredKeys = [
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

const legacyStaticCompositionContracts = [
  {
    label: 'Scarlet/Violet',
    source: read('../../../src/KM.SV/RuntimeSettings/SvGameplaySettingsMainPatcher.cs'),
    recognizedKind: 'SvGameplaySettingsMainKind.Modified'
  },
  {
    label: 'Sword/Shield',
    source: read('../../../src/KM.SwSh/RuntimeSettings/SwShStaticGameplaySettingsMainPatcher.cs'),
    recognizedKind: 'SwShStaticGameplaySettingsMainKind.Configured'
  },
  {
    label: 'Pokemon Legends Z-A',
    source: read('../../../src/KM.ZA/RuntimeSettings/ZaStaticGameplaySettingsMainPatcher.cs'),
    recognizedKind: 'ZaStaticGameplaySettingsMainKind.Configured'
  }
];
for (const contract of legacyStaticCompositionContracts) {
  assert.match(
    contract.source,
    new RegExp(
      `var compositionMainBytes = current\\.Kind switch[\\s\\S]*?${contract.recognizedKind.replaceAll('.', '\\.')} => RestoreFromBase\\([\\s\\S]*?_ => throw new InvalidDataException\\(current\\.Message\\)`
    ),
    `${contract.label} must normalize only an exactly recognized legacy static output and fail closed for every other state.`
  );
  assert.match(
    contract.source,
    /var normalizedCurrent = Analyze\([\s\S]*?compositionMainBytes[\s\S]*?normalizedCurrent\.Kind != [\s\S]*?Vanilla/,
    `${contract.label} must re-analyze the preservation-aware restore before writing native runtime hooks.`
  );
  assert.match(
    contract.source,
    /(?:NsoFile\.Parse|ParseBoundedNso)\(compositionMainBytes/,
    `${contract.label} must compose the native runtime from the normalized executable rather than the legacy static image.`
  );
}

const nativeBundleFactory = read(
  '../../../src/KM.Tools/Application/NativeGameplayMenuBundleFactory.cs'
);
for (const game of ['Scarlet', 'Violet', 'Sword', 'Shield', 'ZA']) {
  assert.match(
    nativeBundleFactory,
    new RegExp(`ProjectGame\\.${game}`),
    `Native gameplay composition must keep ${game} routed through the preservation-aware runtime builders.`
  );
}
assert.match(
  nativeBundleFactory,
  /var initialSettings = ReadInitialSettings\([\s\S]*?sourceMain,[\s\S]*?composedSourceMain\)[\s\S]*?&& !initialSettings\.IsLegacyStaticOutput[\s\S]*?GameplaySettingsJournal\.CreateBootstrap\([\s\S]*?SettingsPresence,[\s\S]*?initialSettings\.Values\)/,
  'Native package creation must carry the recognized executable settings into the initial runtime journal.'
);
for (const recognizedKind of [
  'SvGameplaySettingsMainKind.Modified',
  'SwShStaticGameplaySettingsMainKind.Configured',
  'ZaStaticGameplaySettingsMainKind.Configured'
]) {
  assert.match(
    nativeBundleFactory,
    new RegExp(`${recognizedKind.replaceAll('.', '\\.')}\\)`),
    `Only ${recognizedKind} may bypass the generic Base layout envelope during legacy migration.`
  );
}
for (const analyzer of [
  'SvGameplaySettingsMainPatcher.Analyze',
  'SwShStaticGameplaySettingsMainPatcher.Analyze',
  'ZaStaticGameplaySettingsMainPatcher.Analyze'
]) {
  assert.match(
    nativeBundleFactory,
    new RegExp(analyzer.replaceAll('.', '\\.')),
    `Initial journal migration must use the strict ${analyzer} result.`
  );
}

const settingsJournal = read(
  '../../../src/KM.Core/RuntimeSettings/GameplaySettingsJournal.cs'
);
assert.match(
  settingsJournal,
  /CreateBootstrap\([\s\S]*?GameplaySettingPresence presence,[\s\S]*?GameplaySettingsValues values\)[\s\S]*?CanonicalizeValues\(normalizedPresence, values\)/,
  'The settings journal must support a canonical one-slot bootstrap with reviewed initial values.'
);

const bundleArchive = read(
  '../../../src/KM.Core/RuntimeSettings/GameplayBundleArchive.cs'
);
assert.match(
  bundleArchive,
  /GameplaySettingsJournal\.CreateBootstrap\([\s\S]*?inspection\.ActiveSnapshot\.Presence,[\s\S]*?inspection\.ActiveSnapshot\.Values\)/,
  'Bundle validation must canonicalize the actual initial settings instead of forcing vanilla.'
);

const runtimeMutableContracts = read(
  '../../../src/KM.Core/Output/OutputRuntimeMutableContracts.cs'
);
assert.match(
  runtimeMutableContracts,
  /GameplaySettingsJournal\.CreateBootstrap\([\s\S]*?inspection\.ActiveSnapshot\.Presence,[\s\S]*?inspection\.ActiveSnapshot\.Values\)/,
  'Runtime-mutable ownership must validate non-vanilla canonical bootstrap journals.'
);

const semanticNsoVerifier = read(
  '../../../src/KM.Formats/Executable/NsoRegisteredRegionCompositionVerifier.cs'
);
for (const semanticProof of [
  'NormalizeHeader(retail)',
  'retail.Text.DecompressedData',
  'retail.Ro.DecompressedData',
  'retail.Data.DecompressedData',
  'OpaqueSpansMatch'
]) {
  assert.match(
    semanticNsoVerifier,
    new RegExp(semanticProof.replaceAll('.', '\\.').replaceAll('(', '\\(').replaceAll(')', '\\)')),
    `Ledgerless executable adoption must include the whole-NSO semantic proof ${semanticProof}.`
  );
}

const strictKnownOutputContracts = [
  {
    label: 'Scarlet/Violet',
    source: read('../../../src/KM.SV/RuntimeSettings/SvKnownExecutableCompositionVerifier.cs'),
    legacyKind: 'SvGameplaySettingsMainKind.Modified',
    legacyRestore: 'SvGameplaySettingsMainPatcher.RestoreFromBase'
  },
  {
    label: 'Sword/Shield',
    source: read('../../../src/KM.SwSh/RuntimeSettings/SwShKnownExecutableCompositionVerifier.cs'),
    legacyKind: 'SwShStaticGameplaySettingsMainKind.Configured',
    legacyRestore: 'SwShStaticGameplaySettingsMainPatcher.RestoreFromBase'
  },
  {
    label: 'Pokemon Legends Z-A',
    source: read('../../../src/KM.ZA/RuntimeSettings/ZaKnownExecutableCompositionVerifier.cs'),
    legacyKind: 'ZaStaticGameplaySettingsMainKind.Configured',
    legacyRestore: 'ZaStaticGameplaySettingsMainPatcher.RestoreFromBase'
  }
];
for (const contract of strictKnownOutputContracts) {
  assert.match(
    contract.source,
    new RegExp(contract.legacyKind.replaceAll('.', '\\.')),
    `${contract.label} ledgerless migration must require the strict legacy static analyzer result.`
  );
  assert.match(
    contract.source,
    new RegExp(contract.legacyRestore.replaceAll('.', '\\.')),
    `${contract.label} ledgerless migration must use the legacy editor's preservation-aware inverse.`
  );
  assert.match(
    contract.source,
    /recognizedTransformation[\s\S]*?NsoRegisteredRegionCompositionVerifier\.SemanticallyMatches/,
    `${contract.label} must normalize at least one known KM output and prove the complete remaining executable.`
  );
  assert.doesNotMatch(
    contract.source,
    /ChangesAreConfinedToRegisteredRegions|ReservedRegionLedger\.Regions/,
    `${contract.label} must not treat registered offsets as semantic provenance.`
  );
}

const swordShieldKnownOutput = strictKnownOutputContracts[1].source;
assert.doesNotMatch(
  swordShieldKnownOutput,
  /SwShDynamaxAdventuresMainPatcher/,
  'Ledgerless Dynamax Adventures output must remain rejected because executable bytes alone cannot prove its paired archive state.'
);
for (const rejectedCompatibleKind of [
  'SwShGymUniformRemovalInstallKind.InstalledCompatible',
  'SwShNameFilterMainKind.InstalledCompatible'
]) {
  assert.doesNotMatch(
    swordShieldKnownOutput,
    new RegExp(rejectedCompatibleKind.replaceAll('.', '\\.')),
    `${rejectedCompatibleKind} must not be adopted without a current ownership ledger.`
  );
}

const zaKnownOutput = strictKnownOutputContracts[2].source;
assert.match(
  zaKnownOutput,
  /ZaDexLayoutMainPatcher\.Analyze[\s\S]*?ZaDexLayoutMainKind\.Modified[\s\S]*?ApplyRegularCount\([\s\S]*?ZaDexLayoutMainPatcher\.VanillaRegularCount/,
  'Z-A must recognize and canonically reverse the proven historical Dex Layout output.'
);

const nativeBundleProvider = read(
  '../../../src/KM.Tools/Application/NativeGameplayMenuBundleProvider.cs'
);
const nativePackageApplication = read(
  '../../../src/KM.Tools/Application/InGameSettingsPackageApplicationService.cs'
);
assert.match(
  nativeBundleProvider,
  /semanticallyVerifiedMainSource[\s\S]*?IsSemanticallyVerifiedOutput/,
  'The provider must carry one authoritative semantic source classification.'
);
assert.match(
  nativePackageApplication,
  /SemanticallyVerifiedMainSource[\s\S]*?semanticallyVerifiedSource/,
  'The application ownership gate must consume the provider semantic classification.'
);
