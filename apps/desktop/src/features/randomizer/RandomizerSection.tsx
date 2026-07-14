/* SPDX-License-Identifier: GPL-3.0-only */

import {
  Activity,
  ArrowUp,
  CheckCircle,
  ChevronDown,
  ClipboardCheck,
  Copy,
  Dna,
  ListChecks,
  MapPin,
  Package,
  RefreshCw,
  RotateCcw,
  Save,
  Settings as SettingsIcon,
  ShieldCheck,
  Shuffle,
  Zap,
  type LucideIcon
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  type ApiDiagnostic,
  type ApplyRandomizerResponse,
  type ApplyResult,
  type ImportRandomizerSeedResponse,
  type RandomizerConfig,
  type RandomizerOptions,
  type RestoreRandomizerResponse
} from '../../bridge/contracts';
import { ApplyResultSection, DiagnosticsSection, Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization/LocalizationProvider';
import { RandomizerConfirmationModal } from './RandomizerConfirmationModal';
import { RandomizerRestoreResultSection } from './RandomizerRestoreResultSection';
import './RandomizerSection.css';

type RandomizerOptionKey = keyof RandomizerOptions;
type RandomizerOperation = 'applySeed' | 'randomize' | 'restore';

const defaultRandomizerOptions: RandomizerOptions = {
  ability1: true,
  ability2: true,
  allowSameType: false,
  compatibilityMachines: true,
  compatibilityRecords: true,
  compatibilityTutors: true,
  hiddenAbility: true,
  learnsetBanFixedDamageMoves: true,
  learnsetExpandTo25: false,
  learnsetRequireDamagingMove: true,
  learnsetStabFirst: true,
  randomizeGiftEncounters: false,
  randomizePokemonAbilities: false,
  randomizePokemonCatchRates: false,
  randomizePokemonCompatibility: false,
  randomizePokemonEvolutions: false,
  randomizePokemonHeldItems: false,
  randomizePokemonLearnsets: false,
  randomizePokemonStats: false,
  randomizePokemonTypes: false,
  randomizeWildEncounters: false,
  randomizeRaidBonusRewards: false,
  randomizeRaidRewards: false,
  randomizeStaticEncounters: false,
  randomizeTypeChart: false,
  shufflePokemonStats: true,
  statAttack: true,
  statDefense: true,
  statHp: true,
  statSpecialAttack: true,
  statSpecialDefense: true,
  statSpeed: true,
  typeChartNoImmunities: false,
  typeChartOneImmunityPerType: false,
  typePrimary: true,
  typeSecondary: true
};

const randomizerCategoryDefinitions: Array<{
  enabledKey: RandomizerOptionKey;
  enabledLabel: string;
  fields: Array<{ help: string; key: RandomizerOptionKey; label: string }>;
  help: string;
  icon: LucideIcon;
  id: string;
  label: string;
}> = [
  {
    enabledKey: 'randomizePokemonStats',
    enabledLabel: 'Randomize Base Stats',
    fields: [
      {
        help: 'Shuffle the selected base stat values within each Pokemon instead of generating a fresh stat spread.',
        key: 'shufflePokemonStats',
        label: 'Shuffle stat values'
      },
      { help: 'Include base HP when base stats are randomized.', key: 'statHp', label: 'HP' },
      {
        help: 'Include base Attack when base stats are randomized.',
        key: 'statAttack',
        label: 'Attack'
      },
      {
        help: 'Include base Defense when base stats are randomized.',
        key: 'statDefense',
        label: 'Defense'
      },
      {
        help: 'Include base Special Attack when base stats are randomized.',
        key: 'statSpecialAttack',
        label: 'Sp. Attack'
      },
      {
        help: 'Include base Special Defense when base stats are randomized.',
        key: 'statSpecialDefense',
        label: 'Sp. Defense'
      },
      { help: 'Include base Speed when base stats are randomized.', key: 'statSpeed', label: 'Speed' }
    ],
    help: 'Randomizes Pokemon base stat values while keeping values within safe personal-data limits.',
    icon: Activity,
    id: 'stats',
    label: 'Stats'
  },
  {
    enabledKey: 'randomizePokemonTypes',
    enabledLabel: 'Randomize Types',
    fields: [
      {
        help: 'Randomize each Pokemon primary type. When both type slots are selected, KM can also change whether a Pokemon is mono-type or dual-type.',
        key: 'typePrimary',
        label: 'Primary type'
      },
      {
        help: 'Randomize each Pokemon secondary type. When both type slots are selected, KM can also change whether a Pokemon is mono-type or dual-type.',
        key: 'typeSecondary',
        label: 'Secondary type'
      },
      {
        help: 'For single-slot type rerolls, allow the selected type slot to match the other slot. When both type slots are selected, KM rolls mono-type versus dual-type shape separately.',
        key: 'allowSameType',
        label: 'Allow duplicate types'
      }
    ],
    help: 'Randomizes Pokemon personal-data types and can change Pokemon between mono-type and dual-type when both type slots are selected.',
    icon: Dna,
    id: 'types',
    label: 'Types'
  },
  {
    enabledKey: 'randomizePokemonAbilities',
    enabledLabel: 'Randomize Abilities',
    fields: [
      { help: 'Randomize ability slot 1 for each Pokemon.', key: 'ability1', label: 'Ability 1' },
      { help: 'Randomize ability slot 2 for each Pokemon.', key: 'ability2', label: 'Ability 2' },
      {
        help: 'Randomize the hidden ability slot for each Pokemon.',
        key: 'hiddenAbility',
        label: 'Hidden ability'
      }
    ],
    help: 'Randomizes Pokemon personal-data ability slots using valid loaded ability IDs.',
    icon: CheckCircle,
    id: 'abilities',
    label: 'Abilities'
  },
  {
    enabledKey: 'randomizePokemonHeldItems',
    enabledLabel: 'Randomize Held Items',
    fields: [],
    help: 'Randomizes each Pokemon common, uncommon, and rare held item slots using safe item candidates.',
    icon: Package,
    id: 'heldItems',
    label: 'Held Items'
  },
  {
    enabledKey: 'randomizePokemonCatchRates',
    enabledLabel: 'Randomize Catch Rates',
    fields: [],
    help: 'Randomizes Pokemon catch rates between 1 and 255 in personal data.',
    icon: SettingsIcon,
    id: 'misc',
    label: 'Catch Rates'
  },
  {
    enabledKey: 'randomizePokemonLearnsets',
    enabledLabel: 'Randomize Learnsets',
    fields: [
      {
        help: 'Prefer a same-type attack bonus move in the first learnset slot when one is available.',
        key: 'learnsetStabFirst',
        label: 'STAB first move'
      },
      {
        help: 'Expand randomized learnsets with fewer than 25 moves to 25 move slots spread from Lv. 1 to Lv. 75.',
        key: 'learnsetExpandTo25',
        label: 'Expand learnsets to 25 moves'
      },
      {
        help: 'Exclude fixed-damage moves such as Sonic Boom and Dragon Rage from randomized learnsets.',
        key: 'learnsetBanFixedDamageMoves',
        label: 'Ban fixed-damage moves'
      },
      {
        help: 'Make sure each randomized learnset contains at least one damaging move when possible.',
        key: 'learnsetRequireDamagingMove',
        label: 'Require damaging move'
      }
    ],
    help: 'Randomizes Pokemon level-up learnset moves using legal, usable moves from the loaded move table.',
    icon: Zap,
    id: 'learnsets',
    label: 'Learnsets'
  },
  {
    enabledKey: 'randomizePokemonCompatibility',
    enabledLabel: 'Randomize Move Compatibility',
    fields: [
      { help: 'Randomize TM learn compatibility for each Pokemon.', key: 'compatibilityMachines', label: 'TMs' },
      { help: 'Randomize TR learn compatibility for each Pokemon.', key: 'compatibilityRecords', label: 'TRs' },
      {
        help: 'Randomize move tutor compatibility, including type tutors and Armor tutor moves.',
        key: 'compatibilityTutors',
        label: 'Tutors'
      }
    ],
    help: 'Randomizes whether each Pokemon can learn TM, TR, and tutor moves.',
    icon: ListChecks,
    id: 'compatibility',
    label: 'Moves'
  },
  {
    enabledKey: 'randomizePokemonEvolutions',
    enabledLabel: 'Randomize Evolutions',
    fields: [],
    help: 'Randomizes evolution target species/forms while keeping existing evolution methods, arguments, and levels.',
    icon: ArrowUp,
    id: 'evolutions',
    label: 'Evolutions'
  }
];

const randomizerEncounterOptions: Array<{
  help: string;
  key: RandomizerOptionKey;
  label: string;
}> = [
  {
    help: 'Randomize wild encounter Pokemon/form slots and encounter percentages for symbol, hidden, fishing, shaking tree, and weather tables where they exist.',
    key: 'randomizeWildEncounters',
    label: 'Randomize Wild Encounters'
  },
  {
    help: 'Randomize species/forms for fixed overworld or scripted static encounters.',
    key: 'randomizeStaticEncounters',
    label: 'Randomize Static Encounters'
  },
  {
    help: 'Randomize species/forms for Pokemon received as gifts from scripts or event data.',
    key: 'randomizeGiftEncounters',
    label: 'Randomize Gift Encounters'
  },
  {
    help: 'Randomize raid drop rewards with safe item candidates; Royal Candy item 1128 is excluded when Royal Candy is installed.',
    key: 'randomizeRaidRewards',
    label: 'Randomize Raid Rewards'
  },
  {
    help: 'Randomize raid bonus rewards with safe item candidates; Royal Candy item 1128 is excluded when Royal Candy is installed.',
    key: 'randomizeRaidBonusRewards',
    label: 'Randomize Raid Bonus Rewards'
  }
];

const randomizerTypeChartOptions: Array<{
  help: string;
  key: RandomizerOptionKey;
  label: string;
}> = [
  {
    help: 'Prevent randomized Type Chart values from creating immunities and replace vanilla immunities with non-immune effectiveness values.',
    key: 'typeChartNoImmunities',
    label: 'No immunities'
  },
  {
    help: 'Allow immunities, but limit each attacking type to at most one defending type that is immune to it.',
    key: 'typeChartOneImmunityPerType',
    label: 'No more than one immunity per type'
  }
];

const randomizerChildKeysByParent: Partial<Record<RandomizerOptionKey, RandomizerOptionKey[]>> = {
  randomizePokemonAbilities: ['ability1', 'ability2', 'hiddenAbility'],
  randomizePokemonCompatibility: [
    'compatibilityMachines',
    'compatibilityRecords',
    'compatibilityTutors'
  ],
  randomizePokemonLearnsets: [
    'learnsetStabFirst',
    'learnsetExpandTo25',
    'learnsetBanFixedDamageMoves',
    'learnsetRequireDamagingMove'
  ],
  randomizePokemonStats: [
    'shufflePokemonStats',
    'statHp',
    'statAttack',
    'statDefense',
    'statSpecialAttack',
    'statSpecialDefense',
    'statSpeed'
  ],
  randomizePokemonTypes: ['typePrimary', 'typeSecondary', 'allowSameType'],
  randomizeTypeChart: ['typeChartNoImmunities', 'typeChartOneImmunityPerType']
};

function createEffectiveRandomizerOptions(options: RandomizerOptions): RandomizerOptions {
  const effective = { ...options };

  for (const [parentKey, childKeys] of Object.entries(randomizerChildKeysByParent) as Array<
    [RandomizerOptionKey, RandomizerOptionKey[]]
  >) {
    if (!effective[parentKey]) {
      for (const childKey of childKeys) {
        effective[childKey] = false;
      }
    }
  }

  if (effective.typeChartNoImmunities && effective.typeChartOneImmunityPerType) {
    effective.typeChartOneImmunityPerType = false;
  }

  return effective;
}

export function RandomizerSection({
  canApply,
  isApplying,
  onApplyRandomizer,
  onImportSeed,
  onRestoreRandomizer
}: {
  canApply: boolean;
  isApplying: boolean;
  onApplyRandomizer: (
    config: RandomizerConfig,
    operation?: 'randomize' | 'applySeed'
  ) => Promise<ApplyRandomizerResponse>;
  onImportSeed: (seed: string) => Promise<ImportRandomizerSeedResponse>;
  onRestoreRandomizer: () => Promise<RestoreRandomizerResponse>;
}) {
  const { t, translateLiteral } = useLocalization();
  const [userSeed, setUserSeed] = useState('');
  const [options, setOptions] = useState<RandomizerOptions>(defaultRandomizerOptions);
  const [rollSeed, setRollSeed] = useState<string | null>(null);
  const [outputHash, setOutputHash] = useState<string | null>(null);
  const [expandedCategoryIds, setExpandedCategoryIds] = useState<Set<string>>(
    () => new Set(['stats'])
  );
  const [seedOutput, setSeedOutput] = useState('');
  const [diagnostics, setDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [applyResult, setApplyResult] = useState<ApplyResult | null>(null);
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const [isSeedConfirmOpen, setIsSeedConfirmOpen] = useState(false);
  const [isRestoreConfirmOpen, setIsRestoreConfirmOpen] = useState(false);
  const [importSeedText, setImportSeedText] = useState('');
  const [isImporting, setIsImporting] = useState(false);
  const [isRestoring, setIsRestoring] = useState(false);
  const [lastOperation, setLastOperation] = useState<RandomizerOperation | null>(null);
  const [copySeedStatus, setCopySeedStatus] = useState<'idle' | 'copied' | 'failed'>('idle');
  const copySeedStatusTimerRef = useRef<number | null>(null);
  const selectedOptionCount = useMemo(
    () => Object.entries(options).filter(([key, value]) => key.startsWith('randomize') && value).length,
    [options]
  );
  const pokemonCategoryCount = useMemo(
    () => randomizerCategoryDefinitions.filter((category) => options[category.enabledKey]).length,
    [options]
  );
  const encounterOptionCount = useMemo(
    () => randomizerEncounterOptions.filter((option) => options[option.key]).length,
    [options]
  );
  const battleMechanicOptionCount = options.randomizeTypeChart ? 1 : 0;
  const isConfigurationLocked = isApplying || isImporting || isRestoring;
  const canRandomize = canApply && selectedOptionCount > 0 && !isConfigurationLocked;
  const canApplySharedSeed =
    canApply && !isConfigurationLocked && Boolean(importSeedText.trim());
  const canRestoreVanillaValues = canApply && !isConfigurationLocked;
  const canCopySeed = Boolean(seedOutput.trim()) && !isConfigurationLocked;
  const hasImportedReplay = Boolean(rollSeed && outputHash);
  const hasOperationErrors =
    diagnostics.some((diagnostic) => diagnostic.severity === 'error') ||
    Boolean(
      applyResult?.diagnostics.some((diagnostic) => diagnostic.severity === 'error')
    );
  const restoreNeedsAttention =
    diagnostics.some((diagnostic) => diagnostic.severity !== 'info') ||
    Boolean(
      applyResult?.diagnostics.some((diagnostic) => diagnostic.severity !== 'info')
    );
  const restoreHasNoChanges =
    lastOperation === 'restore' &&
    applyResult !== null &&
    applyResult.writtenFiles.length === 0 &&
    !restoreNeedsAttention;
  const replaySeedStatus =
    hasImportedReplay || (lastOperation === 'applySeed' && applyResult !== null)
      ? 'Imported'
      : 'New roll';
  let operationStatus = 'Ready';
  if (isRestoring) {
    operationStatus = 'Restoring';
  } else if (isImporting || (isApplying && lastOperation === 'applySeed')) {
    operationStatus = 'Applying Seed';
  } else if (isApplying) {
    operationStatus = 'Randomizing';
  } else if (lastOperation === 'restore') {
    operationStatus = restoreNeedsAttention
      ? 'Restore needs attention'
      : restoreHasNoChanges
        ? 'No changes'
        : 'Restore Complete';
  } else if (lastOperation !== null) {
    operationStatus = hasOperationErrors ? 'Randomizer needs attention' : 'Randomizer Complete';
  }

  useEffect(() => {
    setCopySeedStatus('idle');
  }, [seedOutput]);

  useEffect(
    () => () => {
      if (copySeedStatusTimerRef.current !== null) {
        window.clearTimeout(copySeedStatusTimerRef.current);
      }
    },
    []
  );

  const clearImportedReplay = () => {
    setRollSeed(null);
    setOutputHash(null);
    setSeedOutput('');
    setDiagnostics([]);
    setApplyResult(null);
    setLastOperation(null);
  };

  const beginOperation = () => {
    setSeedOutput('');
    setDiagnostics([]);
    setApplyResult(null);
    setLastOperation(null);
  };

  const handleOperationFailure = (operation: RandomizerOperation, error: unknown) => {
    setSeedOutput('');
    setApplyResult(null);
    setDiagnostics([createOperationFailureDiagnostic(operation, error)]);
    setLastOperation(operation);
  };

  const handleUserSeedChange = (value: string) => {
    setUserSeed(value.slice(0, 20));
    clearImportedReplay();
  };

  const handleToggleCategory = (categoryId: string) => {
    setExpandedCategoryIds((current) => {
      const next = new Set(current);
      if (next.has(categoryId)) {
        next.delete(categoryId);
      } else {
        next.add(categoryId);
      }

      return next;
    });
  };

  const handleToggleOption = (key: RandomizerOptionKey) => {
    setOptions((current) => {
      const next = {
        ...current,
        [key]: !current[key]
      };

      if (key === 'typeChartNoImmunities' && next.typeChartNoImmunities) {
        next.typeChartOneImmunityPerType = false;
      }

      if (key === 'typeChartOneImmunityPerType' && next.typeChartOneImmunityPerType) {
        next.typeChartNoImmunities = false;
      }

      return next;
    });
    clearImportedReplay();
  };

  const handleResetOptions = () => {
    setOptions(defaultRandomizerOptions);
    setExpandedCategoryIds(new Set(['stats']));
    clearImportedReplay();
  };

  const handleCopySeed = async () => {
    const seed = seedOutput.trim();
    if (!seed) {
      return;
    }

    if (copySeedStatusTimerRef.current !== null) {
      window.clearTimeout(copySeedStatusTimerRef.current);
    }

    try {
      await writeTextToClipboard(seed);
      setCopySeedStatus('copied');
    } catch {
      setCopySeedStatus('failed');
    }

    copySeedStatusTimerRef.current = window.setTimeout(() => {
      setCopySeedStatus('idle');
      copySeedStatusTimerRef.current = null;
    }, 1800);
  };

  const handleConfirmImportSeed = async () => {
    const seed = importSeedText.trim();
    if (!seed) {
      return;
    }

    setIsSeedConfirmOpen(false);
    beginOperation();
    setIsImporting(true);
    try {
      const importResponse = await onImportSeed(seed);
      const importDiagnostics = importResponse.config
        ? importResponse.diagnostics
        : ensureErrorDiagnostic(importResponse.diagnostics, 'Randomizer needs attention');
      setDiagnostics(importDiagnostics);
      setLastOperation('applySeed');

      if (importResponse.config) {
        const replayConfig = {
          ...importResponse.config,
          options: createEffectiveRandomizerOptions(importResponse.config.options)
        };
        setUserSeed(replayConfig.userSeed);
        setOptions(replayConfig.options);
        setRollSeed(replayConfig.rollSeed ?? null);
        setOutputHash(replayConfig.outputHash ?? null);

        if (importDiagnostics.some((diagnostic) => diagnostic.severity === 'error')) {
          return;
        }

        const applyResponse = await onApplyRandomizer(replayConfig, 'applySeed');
        const hasErrors = applyResponse.applyResult.diagnostics.some(
          (diagnostic) => diagnostic.severity === 'error'
        );

        setApplyResult(applyResponse.applyResult);
        setDiagnostics(applyResponse.applyResult.diagnostics);
        setLastOperation('applySeed');
        if (!hasErrors) {
          setSeedOutput(applyResponse.seed);
          setRollSeed(null);
          setOutputHash(null);
        }
      }
    } catch (error) {
      handleOperationFailure('applySeed', error);
    } finally {
      setIsImporting(false);
    }
  };

  const handleConfirmRestoreVanillaValues = async () => {
    setIsRestoreConfirmOpen(false);
    beginOperation();
    setIsRestoring(true);
    try {
      const response = await onRestoreRandomizer();
      setApplyResult(response.applyResult);
      setDiagnostics(response.applyResult.diagnostics);
      setLastOperation('restore');
      setRollSeed(null);
      setOutputHash(null);
    } catch (error) {
      handleOperationFailure('restore', error);
    } finally {
      setIsRestoring(false);
    }
  };

  const handleConfirmRandomize = async () => {
    setIsConfirmOpen(false);
    beginOperation();
    try {
      const response = await onApplyRandomizer({
        options: createEffectiveRandomizerOptions(options),
        outputHash,
        rollSeed,
        userSeed
      });
      const hasErrors = response.applyResult.diagnostics.some(
        (diagnostic) => diagnostic.severity === 'error'
      );

      setApplyResult(response.applyResult);
      setDiagnostics(response.applyResult.diagnostics);
      setLastOperation('randomize');
      if (!hasErrors) {
        setSeedOutput(response.seed);
        setRollSeed(null);
        setOutputHash(null);
      }
    } catch (error) {
      handleOperationFailure('randomize', error);
    }
  };

  return (
    <>
      <section
        aria-busy={isConfigurationLocked || undefined}
        aria-labelledby="randomizer-heading"
        className="panel wide-panel randomizer-panel"
      >
        <div className="panel-heading">
          <Shuffle aria-hidden="true" size={18} />
          <h2 id="randomizer-heading">Randomizer</h2>
        </div>

        <div className="randomizer-seed-row">
          <label
            className="path-field"
            title={t('randomizer.seed.baseHelp')}
          >
            <span>Base Seed</span>
            <input
              aria-describedby="randomizer-base-seed-help"
              aria-label="Base Seed"
              disabled={isConfigurationLocked}
              maxLength={20}
              onChange={(event) => handleUserSeedChange(event.currentTarget.value)}
              placeholder="Optional, 20 characters max"
              title={t('randomizer.seed.baseHelp')}
              value={userSeed}
            />
            <small className="randomizer-help-copy" id="randomizer-base-seed-help">
              {t('randomizer.seed.baseHelp')}
            </small>
          </label>
        </div>

        <div aria-atomic="true" aria-live="polite" className="randomizer-metrics">
          <Metric label="Selected categories" value={selectedOptionCount.toString()} />
          <Metric label="Pokemon categories" value={pokemonCategoryCount.toString()} />
          <Metric label="Encounters and rewards" value={encounterOptionCount.toString()} />
          <Metric label="Battle mechanics" value={battleMechanicOptionCount.toString()} />
          <Metric label="Replay seed" value={replaySeedStatus} />
          <Metric label="Status" value={operationStatus} />
        </div>

        <div className="randomizer-category-bar" aria-label="Pokemon randomizer categories">
          {randomizerCategoryDefinitions.map((category) => {
            const Icon = category.icon;
            const isExpanded = expandedCategoryIds.has(category.id);
            const isEnabled = options[category.enabledKey];

            return (
              <button
                aria-expanded={isExpanded}
                className={`randomizer-category-button ${
                  isExpanded ? 'randomizer-category-expanded' : ''
                } ${isEnabled ? 'randomizer-category-enabled' : ''}`}
                key={category.id}
                onClick={() => handleToggleCategory(category.id)}
                title={category.help}
                type="button"
              >
                <Icon aria-hidden="true" size={16} />
                <span>{category.label}</span>
                <ChevronDown aria-hidden="true" size={16} />
              </button>
            );
          })}
        </div>
      </section>

      {randomizerCategoryDefinitions
        .filter((category) => expandedCategoryIds.has(category.id))
        .map((category) => {
          const Icon = category.icon;
          const categoryEnabled = options[category.enabledKey];

          return (
            <section
              aria-labelledby={`randomizer-${category.id}-heading`}
              className="panel randomizer-option-panel"
              key={category.id}
            >
              <div className="panel-heading">
                <Icon aria-hidden="true" size={18} />
                <h2 id={`randomizer-${category.id}-heading`}>{category.label}</h2>
              </div>
              <div className="randomizer-option-grid">
                <label className="randomizer-checkbox" title={category.help}>
                  <input
                    checked={categoryEnabled}
                    disabled={isConfigurationLocked}
                    onChange={() => handleToggleOption(category.enabledKey)}
                    title={category.help}
                    type="checkbox"
                  />
                  <span>{category.enabledLabel}</span>
                </label>
                {category.fields.map((field) => {
                  const isFieldChecked = categoryEnabled && options[field.key];

                  return (
                    <label className="randomizer-checkbox" key={field.key} title={field.help}>
                      <input
                        checked={isFieldChecked}
                        disabled={isConfigurationLocked || !categoryEnabled}
                        onChange={() => handleToggleOption(field.key)}
                        title={field.help}
                        type="checkbox"
                      />
                      <span>{field.label}</span>
                    </label>
                  );
                })}
              </div>
            </section>
          );
        })}

      <section aria-labelledby="randomizer-encounters-heading" className="panel randomizer-option-panel">
        <div className="panel-heading">
          <MapPin aria-hidden="true" size={18} />
          <h2 id="randomizer-encounters-heading">Encounters and Rewards</h2>
        </div>
        <div className="randomizer-option-grid">
          {randomizerEncounterOptions.map((option) => (
            <label className="randomizer-checkbox" key={option.key} title={option.help}>
              <input
                checked={options[option.key]}
                disabled={isConfigurationLocked}
                onChange={() => handleToggleOption(option.key)}
                title={option.help}
                type="checkbox"
              />
              <span>{option.label}</span>
            </label>
          ))}
        </div>
      </section>

      <section aria-labelledby="randomizer-type-chart-heading" className="panel randomizer-option-panel">
        <div className="panel-heading">
          <ShieldCheck aria-hidden="true" size={18} />
          <h2 id="randomizer-type-chart-heading">Type Chart</h2>
        </div>
        <div className="randomizer-option-grid">
          <label
            className="randomizer-checkbox"
            title="Randomize the Sword/Shield type-effectiveness table in exefs/main."
          >
            <input
              checked={options.randomizeTypeChart}
              disabled={isConfigurationLocked}
              onChange={() => handleToggleOption('randomizeTypeChart')}
              title="Randomize the Sword/Shield type-effectiveness table in exefs/main."
              type="checkbox"
            />
            <span>Randomize Type Chart</span>
          </label>
          {randomizerTypeChartOptions.map((option) => {
            const isChecked = options.randomizeTypeChart && options[option.key];

            return (
              <label className="randomizer-checkbox" key={option.key} title={option.help}>
                <input
                  checked={isChecked}
                  disabled={isConfigurationLocked || !options.randomizeTypeChart}
                  onChange={() => handleToggleOption(option.key)}
                  title={option.help}
                  type="checkbox"
                />
                <span>{option.label}</span>
              </label>
            );
          })}
        </div>
      </section>

      <section
        aria-busy={isConfigurationLocked || undefined}
        aria-labelledby="randomizer-apply-heading"
        className="panel wide-panel randomizer-apply-panel"
      >
        <div className="panel-heading">
          <Save aria-hidden="true" size={18} />
          <h2 id="randomizer-apply-heading">Apply Randomizer</h2>
        </div>
        <div className="randomizer-action-row">
          <button
            className="secondary-button"
            disabled={isConfigurationLocked}
            onClick={handleResetOptions}
            title={t('randomizer.reset.help')}
            type="button"
          >
            <RotateCcw aria-hidden="true" size={16} />
            <span>Reset Selections</span>
          </button>
          <button
            className="primary-button"
            disabled={!canRandomize}
            onClick={() => setIsConfirmOpen(true)}
            title={
              canRandomize
                ? 'Randomize the selected data and write it to Output Root.'
                : 'Select at least one option and validate editable project paths first.'
            }
            type="button"
          >
            <Shuffle aria-hidden="true" size={16} />
            <span>{isApplying ? 'Randomizing' : 'Randomize'}</span>
          </button>
        </div>
        <div className="randomizer-output-seed">
          <div className="randomizer-output-seed-heading">
            <span>
              <span>{translateLiteral('Generated')}</span>{' '}
              <span>{translateLiteral('Randomizer Seed')}</span>
            </span>
            <button
              className="secondary-button"
              disabled={!canCopySeed}
              onClick={handleCopySeed}
              title={
                canCopySeed
                  ? 'Copy the generated Randomizer Seed to the system clipboard.'
                  : 'Randomize or apply a shared seed before copying.'
              }
              type="button"
            >
              <Copy aria-hidden="true" size={16} />
              <span>
                {copySeedStatus === 'copied'
                  ? 'Copied'
                  : copySeedStatus === 'failed'
                  ? 'Copy Failed'
                  : 'Copy Seed'}
              </span>
            </button>
          </div>
          <p className="randomizer-help-copy" id="randomizer-generated-seed-help">
            {t('randomizer.seed.generatedHelp')}
          </p>
          <textarea
            aria-describedby="randomizer-generated-seed-help"
            aria-label="Randomizer Seed"
            readOnly
            rows={4}
            title={t('randomizer.seed.generatedHelp')}
            value={seedOutput}
          />
        </div>
        <div className="randomizer-shared-seed-row">
          <label
            className="path-field"
            title={t('randomizer.seed.sharedHelp')}
          >
            <span>Shared Randomization Seed</span>
            <textarea
              aria-describedby="randomizer-shared-seed-help"
              aria-label="Shared Randomization Seed"
              disabled={isConfigurationLocked}
              onChange={(event) => setImportSeedText(event.currentTarget.value)}
              rows={3}
              title={t('randomizer.seed.sharedHelp')}
              value={importSeedText}
            />
            <small className="randomizer-help-copy" id="randomizer-shared-seed-help">
              {t('randomizer.seed.sharedHelp')}
            </small>
          </label>
          <button
            className="purple-button"
            disabled={!canApplySharedSeed}
            onClick={() => setIsSeedConfirmOpen(true)}
            title={
              canApplySharedSeed
                ? 'Apply the pasted shared seed directly to Output Root using its saved replay data.'
                : 'Paste a shared seed and validate editable project paths before applying it.'
            }
            type="button"
          >
            <ClipboardCheck aria-hidden="true" size={16} />
            <span>{isImporting ? 'Applying Seed' : 'Apply Randomization Seed'}</span>
          </button>
        </div>
      </section>

      {applyResult && lastOperation !== 'restore' ? (
        <ApplyResultSection applyResult={applyResult} />
      ) : null}

      <section
        aria-busy={isRestoring || undefined}
        aria-labelledby="randomizer-recovery-heading"
        className="panel wide-panel randomizer-apply-panel randomizer-recovery-panel"
      >
        <div className="panel-heading">
          <RefreshCw aria-hidden="true" size={18} />
          <h2 id="randomizer-recovery-heading">
            {translateLiteral('Restore Vanilla Values')}
          </h2>
        </div>
        <p className="randomizer-recovery-copy">{t('randomizer.recovery.description')}</p>
        <div className="randomizer-action-row">
          <button
            className="danger-button"
            disabled={!canRestoreVanillaValues}
            onClick={() => setIsRestoreConfirmOpen(true)}
            title={
              canRestoreVanillaValues
                ? t('randomizer.recovery.description')
                : translateLiteral(
                    'Validate editable project paths before restoring tracked Randomizer output files.'
                  )
            }
            type="button"
          >
            <RefreshCw aria-hidden="true" size={16} />
            <span>{isRestoring ? 'Restoring' : 'Restore Vanilla Values'}</span>
          </button>
        </div>
      </section>

      {applyResult && lastOperation === 'restore' ? (
        <RandomizerRestoreResultSection applyResult={applyResult} />
      ) : null}
      {diagnostics.length > 0 ? (
        <DiagnosticsSection diagnostics={diagnostics} scrollAfterEntries={5} />
      ) : null}

      {isConfirmOpen ? (
        <RandomizerConfirmationModal
          isBusy={isConfigurationLocked}
          kind="randomize"
          onCancel={() => setIsConfirmOpen(false)}
          onConfirm={() => void handleConfirmRandomize()}
        />
      ) : null}

      {isSeedConfirmOpen ? (
        <RandomizerConfirmationModal
          isBusy={isConfigurationLocked}
          kind="applySeed"
          onCancel={() => setIsSeedConfirmOpen(false)}
          onConfirm={() => void handleConfirmImportSeed()}
        />
      ) : null}

      {isRestoreConfirmOpen ? (
        <RandomizerConfirmationModal
          isBusy={isConfigurationLocked}
          kind="restore"
          onCancel={() => setIsRestoreConfirmOpen(false)}
          onConfirm={() => void handleConfirmRestoreVanillaValues()}
        />
      ) : null}
    </>
  );
}

function createOperationFailureDiagnostic(
  operation: RandomizerOperation,
  error: unknown
): ApiDiagnostic {
  const fallbackMessage =
    operation === 'restore' ? 'Restore needs attention' : 'Randomizer needs attention';
  return {
    message: error instanceof Error && error.message.trim() ? error.message : fallbackMessage,
    severity: 'error'
  };
}

function ensureErrorDiagnostic(
  diagnostics: ApiDiagnostic[],
  fallbackMessage: string
): ApiDiagnostic[] {
  return diagnostics.some((diagnostic) => diagnostic.severity === 'error')
    ? diagnostics
    : [...diagnostics, { message: fallbackMessage, severity: 'error' }];
}

async function writeTextToClipboard(text: string): Promise<void> {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }

  const textArea = document.createElement('textarea');
  textArea.value = text;
  textArea.setAttribute('readonly', '');
  textArea.style.position = 'fixed';
  textArea.style.top = '-1000px';
  textArea.style.opacity = '0';
  document.body.append(textArea);
  textArea.select();

  try {
    if (!document.execCommand('copy')) {
      throw new Error('Clipboard copy command failed.');
    }
  } finally {
    textArea.remove();
  }
}
