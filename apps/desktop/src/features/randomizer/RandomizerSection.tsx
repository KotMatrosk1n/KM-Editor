/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardCheck, Copy, RefreshCw, RotateCcw, Save, Shuffle } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  type ApiDiagnostic,
  type ApplyRandomizerResponse,
  type ApplyResult,
  type ImportRandomizerSeedResponse,
  type RandomizerConfig,
  type RandomizerOptions,
  type TrainerRandomizerOptions,
  type RestoreRandomizerResponse
} from '../../bridge/contracts';
import { ApplyResultSection, DiagnosticsSection, Metric } from '../../components/workflowPanels';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { FieldLabel } from '../../components/FieldLabel';
import { HoverTooltip } from '../../components/HoverTooltip';
import { useLocalization } from '../../localization/LocalizationProvider';
import { toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';
import { RandomizerConfirmationModal } from './RandomizerConfirmationModal';
import { RandomizerRestoreResultSection } from './RandomizerRestoreResultSection';
import { createEffectiveRandomizerOptions, defaultRandomizerOptions, defaultTrainerOptions, type RandomizerOptionKey } from './randomizerOptions';
import { RandomizerSettings } from './RandomizerSettings';
import './RandomizerSection.css';

type RandomizerOperation = 'applySeed' | 'randomize' | 'restore';

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
  const [trainerLevelDraft, setTrainerLevelDraft] = useState('100');
  const [rollSeed, setRollSeed] = useState<string | null>(null);
  const [outputHash, setOutputHash] = useState<string | null>(null);
  const [importedReplayConfig, setImportedReplayConfig] = useState<RandomizerConfig | null>(null);
  const [categoryId, setCategoryId] = useState('stats');
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
  usePublishCommonEditorError({
    domain: 'workflow.randomizer',
    field: 'seedClipboard',
    message: copySeedStatus === 'failed'
      ? translateLiteral('The Randomizer seed could not be copied.')
      : null
  });
  const copySeedStatusTimerRef = useRef<number | null>(null);
  const draftRevisionRef = useRef(0);
  const activeOperationRef = useRef<object | null>(null);
  const selectedOptionCount = useMemo(
    () => Object.entries(options).filter(([key, value]) => key.startsWith('randomize') && value).length,
    [options]
  );
  const isConfigurationLocked =
    activeOperationRef.current !== null || isApplying || isImporting || isRestoring;
  const trainerLevelValid = /^\d+$/.test(trainerLevelDraft) && Number(trainerLevelDraft) >= 25 && Number(trainerLevelDraft) <= 1000;
  const canRandomize = canApply && selectedOptionCount > 0 && !isConfigurationLocked && (!options.randomizeTrainers || trainerLevelValid);
  const canApplySharedSeed =
    canApply && !isConfigurationLocked && Boolean(importSeedText.trim());
  const canRestoreVanillaValues = canApply && !isConfigurationLocked;
  const canCopySeed = Boolean(seedOutput.trim()) && !isConfigurationLocked;
  const hasImportedReplay = importedReplayConfig !== null;
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
      activeOperationRef.current = null;
      if (copySeedStatusTimerRef.current !== null) {
        window.clearTimeout(copySeedStatusTimerRef.current);
      }
    },
    []
  );

  const clearImportedReplay = () => {
    setImportedReplayConfig(null);
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
    const failureDiagnostics = createOperationFailureDiagnostics(operation, error);
    if (failureDiagnostics.length === 0) {
      return;
    }

    setSeedOutput('');
    setApplyResult(null);
    setDiagnostics(failureDiagnostics);
    setLastOperation(operation);
  };

  const handleUserSeedChange = (value: string) => {
    if (activeOperationRef.current !== null) {
      return;
    }
    draftRevisionRef.current += 1;
    setUserSeed(value.slice(0, 20));
    clearImportedReplay();
  };

  const handleToggleOption = (key: RandomizerOptionKey) => {
    if (activeOperationRef.current !== null) {
      return;
    }
    draftRevisionRef.current += 1;
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

  const handleTrainerChange = (patch: Partial<TrainerRandomizerOptions>) => {
    if (activeOperationRef.current !== null) return;
    draftRevisionRef.current += 1;
    setOptions(current => ({ ...current, trainers: { ...(current.trainers ?? defaultTrainerOptions), ...patch } }));
    clearImportedReplay();
  };
  const handleTrainerLevelChange = (value: string) => {
    if (activeOperationRef.current !== null) return;
    setTrainerLevelDraft(value);
    handleTrainerChange(/^\d+$/.test(value) && Number(value) >= 25 && Number(value) <= 1000 ? { levelPercent: Number(value) } : {});
  };

  const handleResetOptions = () => {
    if (activeOperationRef.current !== null) {
      return;
    }
    draftRevisionRef.current += 1;
    setOptions(defaultRandomizerOptions);
    setTrainerLevelDraft('100');
    setCategoryId('stats');
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
    if (!seed || activeOperationRef.current !== null) {
      return;
    }
    const operation = {};
    activeOperationRef.current = operation;
    const submittedDraftRevision = draftRevisionRef.current;
    const isCurrentOperation = () =>
      activeOperationRef.current === operation &&
      draftRevisionRef.current === submittedDraftRevision;

    setIsSeedConfirmOpen(false);
    beginOperation();
    setIsImporting(true);
    try {
      const importResponse = await onImportSeed(seed);
      if (!isCurrentOperation()) {
        return;
      }
      const importDiagnostics = importResponse.config
        ? importResponse.diagnostics
        : ensureErrorDiagnostic(importResponse.diagnostics, 'Randomizer needs attention');
      setDiagnostics(importDiagnostics);
      setLastOperation('applySeed');

      if (importResponse.config) {
        const replayConfig = importResponse.config;
        setImportedReplayConfig(replayConfig);
        setUserSeed(replayConfig.userSeed);
        setOptions(replayConfig.options);
        setTrainerLevelDraft(String(replayConfig.options.trainers?.levelPercent ?? 100));
        setRollSeed(replayConfig.rollSeed ?? null);
        setOutputHash(replayConfig.outputHash ?? null);

        if (importDiagnostics.some((diagnostic) => diagnostic.severity === 'error')) {
          return;
        }

        const applyResponse = await onApplyRandomizer(replayConfig, 'applySeed');
        if (!isCurrentOperation()) {
          return;
        }
        const hasErrors = applyResponse.applyResult.diagnostics.some(
          (diagnostic) => diagnostic.severity === 'error'
        );

        setApplyResult(applyResponse.applyResult);
        setDiagnostics(applyResponse.applyResult.diagnostics);
        setLastOperation('applySeed');
        if (!hasErrors) {
          setSeedOutput(applyResponse.seed);
          setImportedReplayConfig(null);
          setRollSeed(null);
          setOutputHash(null);
        }
      }
    } catch (error) {
      if (isCurrentOperation()) {
        handleOperationFailure('applySeed', error);
      }
    } finally {
      if (activeOperationRef.current === operation) {
        activeOperationRef.current = null;
        setIsImporting(false);
      }
    }
  };

  const handleConfirmRestoreVanillaValues = async () => {
    if (activeOperationRef.current !== null) {
      return;
    }
    const operation = {};
    activeOperationRef.current = operation;

    setIsRestoreConfirmOpen(false);
    beginOperation();
    setIsRestoring(true);
    try {
      const response = await onRestoreRandomizer();
      if (activeOperationRef.current !== operation) {
        return;
      }
      setApplyResult(response.applyResult);
      setDiagnostics(response.applyResult.diagnostics);
      setLastOperation('restore');
      setImportedReplayConfig(null);
      setRollSeed(null);
      setOutputHash(null);
    } catch (error) {
      if (activeOperationRef.current === operation) {
        handleOperationFailure('restore', error);
      }
    } finally {
      if (activeOperationRef.current === operation) {
        activeOperationRef.current = null;
        setIsRestoring(false);
      }
    }
  };

  const handleConfirmRandomize = async () => {
    if (!canRandomize || activeOperationRef.current !== null) {
      return;
    }
    const operation = {};
    activeOperationRef.current = operation;
    const submittedDraftRevision = draftRevisionRef.current;
    const isCurrentOperation = () =>
      activeOperationRef.current === operation &&
      draftRevisionRef.current === submittedDraftRevision;
    const submittedConfig: RandomizerConfig = importedReplayConfig ?? {
      algorithmVersion: 2,
      options: createEffectiveRandomizerOptions(options),
      outputHash,
      rollSeed,
      userSeed
    };

    setIsConfirmOpen(false);
    beginOperation();
    try {
      const response = await onApplyRandomizer(submittedConfig);
      if (!isCurrentOperation()) {
        return;
      }
      const hasErrors = response.applyResult.diagnostics.some(
        (diagnostic) => diagnostic.severity === 'error'
      );

      setApplyResult(response.applyResult);
      setDiagnostics(response.applyResult.diagnostics);
      setLastOperation('randomize');
      if (!hasErrors) {
        setSeedOutput(response.seed);
        setImportedReplayConfig(null);
        setRollSeed(null);
        setOutputHash(null);
      }
    } catch (error) {
      if (isCurrentOperation()) {
        handleOperationFailure('randomize', error);
      }
    } finally {
      if (activeOperationRef.current === operation) {
        activeOperationRef.current = null;
      }
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

        <div className="randomizer-configuration">
          <div className="randomizer-seed-row">
            <div className="path-field">
              <FieldLabel
                help={t('randomizer.seed.baseHelp')}
                htmlFor="randomizer-base-seed"
                label="Base Seed"
              />
              <HoverTooltip content={t('randomizer.seed.baseHelp')}>
                <input
                  aria-label="Base Seed"
                  disabled={isConfigurationLocked}
                  id="randomizer-base-seed"
                  maxLength={20}
                  onChange={(event) => handleUserSeedChange(event.currentTarget.value)}
                  placeholder="Optional, 20 characters max"
                  value={userSeed}
                />
              </HoverTooltip>
            </div>
          </div>

          <div aria-atomic="true" aria-live="polite" className="randomizer-metrics">
            <Metric label="Selected categories" value={selectedOptionCount.toString()} />
            <Metric label="Replay seed" value={replaySeedStatus} />
            <Metric label="Status" value={operationStatus} />
          </div>
        </div>

      </section>

      <RandomizerSettings options={options} locked={isConfigurationLocked}
        categoryId={categoryId} onSelectCategory={setCategoryId}
        onToggle={handleToggleOption} onTrainerChange={handleTrainerChange}
        levelDraft={trainerLevelDraft} levelValid={trainerLevelValid} onLevelChange={handleTrainerLevelChange} />

      <section
        aria-busy={isConfigurationLocked || undefined}
        aria-labelledby="randomizer-apply-heading"
        className="panel wide-panel randomizer-apply-panel"
      >
        <div className="panel-heading">
          <Save aria-hidden="true" size={18} />
          <h2 id="randomizer-apply-heading">{translateLiteral('Share and Replay Seeds')}</h2>
        </div>
        <div className="randomizer-output-seed">
          <div className="randomizer-output-seed-heading">
            <FieldLabel
              help={t('randomizer.seed.generatedHelp')}
              htmlFor="randomizer-generated-seed"
              label={`${translateLiteral('Generated')} ${translateLiteral('Randomizer Seed')}`}
            />
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
          <HoverTooltip content={t('randomizer.seed.generatedHelp')}>
            <textarea
              aria-label="Randomizer Seed"
              id="randomizer-generated-seed"
              readOnly
              rows={4}
              value={seedOutput}
            />
          </HoverTooltip>
        </div>
        <div className="randomizer-shared-seed-row">
          <div className="path-field">
            <FieldLabel
              help={t('randomizer.seed.sharedHelp')}
              htmlFor="randomizer-shared-seed"
              label="Shared Randomization Seed"
            />
            <HoverTooltip content={t('randomizer.seed.sharedHelp')}>
              <textarea
                aria-label="Shared Randomization Seed"
                disabled={isConfigurationLocked}
                id="randomizer-shared-seed"
                onChange={(event) => {
                  if (activeOperationRef.current !== null) {
                    return;
                  }
                  draftRevisionRef.current += 1;
                  setImportSeedText(event.currentTarget.value);
                }}
                rows={3}
                value={importSeedText}
              />
            </HoverTooltip>
          </div>
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

function createOperationFailureDiagnostics(
  operation: RandomizerOperation,
  error: unknown
): ApiDiagnostic[] {
  const fallbackMessage =
    operation === 'restore' ? 'Restore needs attention' : 'Randomizer needs attention';
  return toProjectBridgeDiagnostics(error, fallbackMessage);
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
