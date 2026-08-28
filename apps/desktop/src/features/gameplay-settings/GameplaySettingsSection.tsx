/* SPDX-License-Identifier: GPL-3.0-only */

import { FlaskConical, ShieldAlert } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type {
  ApplyGameplaySettingsUpdateResponse,
  GameplaySettingsCapability,
  GameplaySettingsSnapshot,
  GameplaySettingsState,
  GameplaySettingsValues,
  PreviewGameplaySettingsUpdateResponse
} from '../../bridge/gameplaySettingsContracts';
import {
  gameplaySettingsExperienceRateStepBasisPoints,
  gameplaySettingsMaximumExperienceRateBasisPoints
} from '../../bridge/gameplaySettingsContracts';
import type { InGameSettingsPackageState } from '../../bridge/inGameSettingsPackageContracts';
import type { OutputSafetyScope } from '../../bridge/outputSafetyContracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { useLocalization } from '../../localization';
import './GameplaySettingsSection.css';
import { InGameSettingsPackagePanel } from './InGameSettingsPackagePanel';

type GameplaySettingsBridge = Pick<
  ProjectBridge,
  | 'applyGameplaySettingsUpdate'
  | 'applyInGameSettingsPackage'
  | 'getGameplaySettings'
  | 'inspectInGameSettingsPackage'
  | 'previewGameplaySettingsUpdate'
  | 'previewInGameSettingsPackage'
>;

type GameplaySettingsSectionProps = {
  armCriticalWriteGuard: () => Promise<boolean>;
  bridge: GameplaySettingsBridge;
  canApply?: boolean;
  onApplied?: (scope: OutputSafetyScope) => Promise<void> | void;
  onApplyBusyChange?: (isBusy: boolean) => void;
  onDirtyChange?: (isDirty: boolean) => void;
  onError?: (
    error: unknown,
    operation: 'apply' | 'load' | 'preview',
    scope: OutputSafetyScope
  ) => Promise<void> | void;
  onOpenProjectSetup?: () => void;
  onRecoveryRequired?: (scope: OutputSafetyScope) => Promise<void> | void;
  scope: OutputSafetyScope | null;
};

type LoadState = {
  detail?: string | null;
  snapshot: GameplaySettingsSnapshot | null;
  state: GameplaySettingsState;
};

type GameplaySettingsLoadMode = 'applyRefresh' | 'standalone';
type GameplaySettingsDeliveryMode = 'fixed' | 'runtime';

const rateChoices = Array.from(
  {
    length:
      gameplaySettingsMaximumExperienceRateBasisPoints /
        gameplaySettingsExperienceRateStepBasisPoints +
      1
  },
  (_, index) => index * gameplaySettingsExperienceRateStepBasisPoints
);

const fallbackValues: GameplaySettingsValues = {
  experienceRateBasisPoints: 10_000,
  experienceShareEnabled: true,
  levelCap: 100,
  levelCapEnabled: false
};

export const gameplaySettingsGuardFailureMessageKey = 'gameplaySettings.error';

export function GameplaySettingsSection({
  armCriticalWriteGuard,
  bridge,
  canApply = true,
  onApplied,
  onApplyBusyChange,
  onDirtyChange,
  onError,
  onOpenProjectSetup,
  onRecoveryRequired,
  scope
}: GameplaySettingsSectionProps) {
  const { t } = useLocalization();
  const [loadState, setLoadState] = useState<LoadState | null>(null);
  const [draft, setDraft] = useState<GameplaySettingsValues | null>(null);
  const [preview, setPreview] = useState<PreviewGameplaySettingsUpdateResponse | null>(null);
  const [busy, setBusy] = useState<'apply' | 'load' | 'preview' | null>(null);
  const [messageKey, setMessageKey] = useState<string | null>(null);
  const [betaAcknowledged, setBetaAcknowledged] = useState(false);
  const [deliveryMode, setDeliveryMode] =
    useState<GameplaySettingsDeliveryMode>('runtime');
  const [inGamePackageDirty, setInGamePackageDirty] = useState(false);
  const [inGamePackageState, setInGamePackageState] =
    useState<InGameSettingsPackageState | null>(null);
  const [inGamePackageLocksStaticEditor, setInGamePackageLocksStaticEditor] =
    useState(false);
  const actionGenerationRef = useRef(0);
  const draftGenerationRef = useRef(0);
  const isMountedRef = useRef(true);
  const loadRequestGenerationRef = useRef(0);
  const previewRequestGenerationRef = useRef(0);
  const armCriticalWriteGuardRef = useRef(armCriticalWriteGuard);
  armCriticalWriteGuardRef.current = armCriticalWriteGuard;
  const onApplyBusyChangeRef = useRef(onApplyBusyChange);
  onApplyBusyChangeRef.current = onApplyBusyChange;
  const applyGuardControllerRef = useRef<GameplaySettingsApplyGuardController | null>(null);
  const focusReviewOnCompletionRef = useRef(false);
  const deliveryModeChosenRef = useRef(false);
  const reviewRegionRef = useRef<HTMLDivElement | null>(null);
  if (applyGuardControllerRef.current === null) {
    applyGuardControllerRef.current = createGameplaySettingsApplyGuardController(
      () => armCriticalWriteGuardRef.current,
      () => onApplyBusyChangeRef.current
    );
  }

  const stableScope = useMemo<OutputSafetyScope | null>(
    () => (hasGameplaySettingsOutputScope(scope) ? copyGameplaySettingsScope(scope) : null),
    [
      scope?.paths.baseExeFsPath,
      scope?.paths.baseRomFsPath,
      scope?.paths.gameTextLanguage,
      scope?.paths.outputRootPath,
      scope?.paths.pokemonLegendsZASupportFolderPath,
      scope?.paths.saveFilePath,
      scope?.paths.scarletVioletSupportFolderPath,
      scope?.paths.selectedGame,
      scope?.projectId
    ]
  );
  const scopeKey = useMemo(
    () => (stableScope ? gameplaySettingsScopeKey(stableScope) : 'project-setup-required'),
    [stableScope]
  );
  const scopeKeyRef = useRef(scopeKey);
  scopeKeyRef.current = scopeKey;

  const beginApplyGuard = useCallback(async (generation: number) => {
    return applyGuardControllerRef.current!.begin(generation);
  }, []);
  const endApplyGuard = useCallback((generation?: number) => {
    applyGuardControllerRef.current!.end(generation);
  }, []);
  const isCurrentApply = useCallback(
    (generation: number, requestScopeKey: string) =>
      isMountedRef.current &&
      actionGenerationRef.current === generation &&
      scopeKeyRef.current === requestScopeKey,
    []
  );

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      actionGenerationRef.current += 1;
      draftGenerationRef.current += 1;
      loadRequestGenerationRef.current += 1;
      previewRequestGenerationRef.current += 1;
      endApplyGuard();
    };
  }, [endApplyGuard]);

  useEffect(() => {
    actionGenerationRef.current += 1;
    draftGenerationRef.current += 1;
    loadRequestGenerationRef.current += 1;
    previewRequestGenerationRef.current += 1;
    endApplyGuard();
    setBetaAcknowledged(false);
    setDeliveryMode('runtime');
    deliveryModeChosenRef.current = false;
    setInGamePackageDirty(false);
    setInGamePackageState(null);
    setInGamePackageLocksStaticEditor(false);
  }, [endApplyGuard, scopeKey]);

  useEffect(() => {
    if (
      !deliveryModeChosenRef.current &&
      (inGamePackageState === 'installed' ||
        inGamePackageState === 'upgradeAvailable' ||
        inGamePackageState === 'coexistenceConflict')
    ) {
      setDeliveryMode('runtime');
    }
  }, [inGamePackageState]);

  const reportError = useCallback(
    async (error: unknown, operation: 'apply' | 'load' | 'preview') => {
      setMessageKey('gameplaySettings.error');
      if (stableScope) {
        await onError?.(error, operation, stableScope);
      }
    },
    [onError, stableScope]
  );

  const load = useCallback(
    async (mode: GameplaySettingsLoadMode = 'standalone'): Promise<boolean> => {
      const requestScope = stableScope;
      const requestGeneration = ++loadRequestGenerationRef.current;
      draftGenerationRef.current += 1;
      previewRequestGenerationRef.current += 1;
      if (!requestScope) {
        setBusy(null);
        setLoadState(null);
        setDraft(null);
        setPreview(null);
        setMessageKey(null);
        return false;
      }

      const ownsBusy = gameplaySettingsLoadOwnsBusy(mode);
      const requestScopeKey = scopeKey;
      if (ownsBusy) {
        setBusy('load');
      }
      setMessageKey(null);
      setPreview(null);
      setLoadState(null);
      setDraft(null);
      try {
        const response = await bridge.getGameplaySettings({ scope: requestScope });
        if (
          scopeKeyRef.current !== requestScopeKey ||
          loadRequestGenerationRef.current !== requestGeneration
        ) {
          return false;
        }
        setLoadState(response);
        setDraft(response.snapshot?.values ?? null);
        return true;
      } catch (error) {
        if (
          scopeKeyRef.current !== requestScopeKey ||
          loadRequestGenerationRef.current !== requestGeneration
        ) {
          return false;
        }
        await reportError(error, 'load');
        return false;
      } finally {
        if (
          ownsBusy &&
          scopeKeyRef.current === requestScopeKey &&
          loadRequestGenerationRef.current === requestGeneration
        ) {
          setBusy(null);
        }
      }
    },
    [bridge, reportError, scopeKey, stableScope]
  );

  useEffect(() => {
    void load();
  }, [load]);

  const snapshot = loadState?.snapshot ?? null;
  const shownValues = draft ?? snapshot?.values ?? fallbackValues;
  const hasChanges = useMemo(
    () => snapshot !== null && draft !== null && !sameValues(snapshot.values, draft),
    [draft, snapshot]
  );
  const hasProtectedDraft = hasChanges || preview !== null;
  const hasProtectedEditorState = hasProtectedDraft || inGamePackageDirty;
  useEffect(() => {
    onDirtyChange?.(hasProtectedEditorState);
    return () => onDirtyChange?.(false);
  }, [hasProtectedEditorState, onDirtyChange]);
  const editorIsReady = Boolean(
    stableScope && loadState && isEditableState(loadState.state) && snapshot && draft
  );
  const staticEditorLockedByPackage = Boolean(
    stableScope && inGamePackageLocksStaticEditor
  );
  const unavailableReason = stableScope
    ? loadState?.state ?? (busy === 'load' ? 'loading' : 'not-loaded')
    : 'project-setup-required';
  const experienceShareCapability = capabilityForDisplay(
    snapshot?.experienceShareCapability,
    snapshot?.hasExperienceShare,
    unavailableReason
  );
  const experienceRateCapability = capabilityForDisplay(
    snapshot?.experienceRateCapability,
    snapshot?.hasExperienceRate,
    unavailableReason
  );
  const levelCapCapability = capabilityForDisplay(
    snapshot?.levelCapCapability,
    snapshot?.hasLevelCap,
    unavailableReason
  );
  const canEditExperienceShare =
    editorIsReady && !staticEditorLockedByPackage && experienceShareCapability.available;
  const canEditExperienceRate =
    editorIsReady && !staticEditorLockedByPackage && experienceRateCapability.available;
  const canEditLevelCap =
    editorIsReady && !staticEditorLockedByPackage && levelCapCapability.available;
  const staticSettingsAreVanilla = Boolean(
    editorIsReady &&
      snapshot &&
      draft &&
      sameValues(snapshot.values, fallbackValues) &&
      sameValues(draft, fallbackValues) &&
      preview === null
  );

  useEffect(() => {
    if (!staticEditorLockedByPackage) return;
    draftGenerationRef.current += 1;
    previewRequestGenerationRef.current += 1;
    setDraft(snapshot?.values ?? null);
    setPreview(null);
    setBetaAcknowledged(false);
  }, [snapshot, staticEditorLockedByPackage]);

  const updateDraft = useCallback((next: GameplaySettingsValues) => {
    draftGenerationRef.current += 1;
    previewRequestGenerationRef.current += 1;
    setDraft(next);
    setPreview(null);
    setMessageKey(null);
  }, []);

  const vanillaDraft = useMemo(
    () =>
      draft
        ? getAvailableVanillaGameplaySettingsValues(draft, {
            experienceRate: experienceRateCapability.available,
            experienceShare: experienceShareCapability.available,
            levelCap: levelCapCapability.available
          })
        : null,
    [
      draft,
      experienceRateCapability.available,
      experienceShareCapability.available,
      levelCapCapability.available
    ]
  );
  const canSetVanillaValues = Boolean(
    editorIsReady && vanillaDraft && draft && !sameValues(vanillaDraft, draft)
  );
  const setVanillaValues = useCallback(() => {
    if (!vanillaDraft || !editorIsReady) return;
    updateDraft(vanillaDraft);
  }, [editorIsReady, updateDraft, vanillaDraft]);

  const review = useCallback(async () => {
    const requestScope = stableScope;
    if (
      !requestScope ||
      !snapshot ||
      !draft ||
      !hasChanges ||
      !betaAcknowledged ||
      !canApply ||
      staticEditorLockedByPackage
    ) return;
    const requestScopeKey = scopeKey;
    const requestGeneration = ++previewRequestGenerationRef.current;
    const requestDraftGeneration = draftGenerationRef.current;
    setBusy('preview');
    setMessageKey(null);
    try {
      const response = await bridge.previewGameplaySettingsUpdate({
        expectedGeneration: snapshot.generation,
        experienceRateBasisPoints:
          draft.experienceRateBasisPoints === snapshot.values.experienceRateBasisPoints
            ? undefined
            : draft.experienceRateBasisPoints,
        experienceShareEnabled:
          draft.experienceShareEnabled === snapshot.values.experienceShareEnabled
            ? undefined
            : draft.experienceShareEnabled,
        levelCap: draft.levelCap === snapshot.values.levelCap ? undefined : draft.levelCap,
        levelCapEnabled:
          draft.levelCapEnabled === snapshot.values.levelCapEnabled
            ? undefined
            : draft.levelCapEnabled,
        scope: requestScope
      });
      if (
        scopeKeyRef.current !== requestScopeKey ||
        previewRequestGenerationRef.current !== requestGeneration ||
        draftGenerationRef.current !== requestDraftGeneration
      ) {
        return;
      }
      setPreview(response);
    } catch (error) {
      if (
        scopeKeyRef.current !== requestScopeKey ||
        previewRequestGenerationRef.current !== requestGeneration ||
        draftGenerationRef.current !== requestDraftGeneration
      ) {
        return;
      }
      await reportError(error, 'preview');
    } finally {
      if (
        scopeKeyRef.current === requestScopeKey &&
        previewRequestGenerationRef.current === requestGeneration &&
        draftGenerationRef.current === requestDraftGeneration
      ) {
        setBusy(null);
      }
    }
  }, [
    betaAcknowledged,
    bridge,
    canApply,
    draft,
    hasChanges,
    reportError,
    scopeKey,
    snapshot,
    stableScope,
    staticEditorLockedByPackage
  ]);

  useEffect(() => {
    if (!preview) return;
    let timer: number | null = null;
    let disposed = false;
    const expiresAt = Date.parse(preview.expiresAtUtc);
    const expireReview = () => {
      if (disposed) return;
      setPreview(null);
      setMessageKey('gameplaySettings.diagnostic.reviewExpired');
    };
    const scheduleExpiryCheck = () => {
      const remaining = expiresAt - Date.now();
      if (!Number.isFinite(remaining) || remaining <= 0) {
        expireReview();
        return;
      }
      timer = window.setTimeout(scheduleExpiryCheck, Math.min(remaining, 60_000));
    };

    scheduleExpiryCheck();
    return () => {
      disposed = true;
      if (timer !== null) {
        window.clearTimeout(timer);
      }
    };
  }, [preview]);

  useEffect(() => {
    if (!preview || !focusReviewOnCompletionRef.current) return;
    focusReviewOnCompletionRef.current = false;
    reviewRegionRef.current?.focus();
  }, [preview]);

  const apply = useCallback(async () => {
    const requestScope = stableScope;
    if (
      !requestScope ||
      !preview ||
      !canApply ||
      !betaAcknowledged ||
      staticEditorLockedByPackage
    ) return;
    if (isGameplaySettingsPreviewExpired(preview)) {
      setPreview(null);
      setMessageKey('gameplaySettings.diagnostic.reviewExpired');
      return;
    }
    const requestScopeKey = scopeKey;
    actionGenerationRef.current += 1;
    const generation = actionGenerationRef.current;
    setBusy('apply');
    setMessageKey(null);
    try {
      const didArmCriticalWriteGuard = await beginApplyGuard(generation);
      if (
        !didArmCriticalWriteGuard ||
        !applyGuardControllerRef.current!.isActive(generation) ||
        !isCurrentApply(generation, requestScopeKey)
      ) {
        if (isCurrentApply(generation, requestScopeKey)) {
          setMessageKey(gameplaySettingsGuardFailureMessageKey);
        }
        return;
      }
      if (isGameplaySettingsPreviewExpired(preview)) {
        setPreview(null);
        setMessageKey('gameplaySettings.diagnostic.reviewExpired');
        return;
      }

      const response = await bridge.applyGameplaySettingsUpdate({
        reviewId: preview.reviewId,
        scope: requestScope
      });
      if (!isCurrentApply(generation, requestScopeKey)) return;
      setPreview(null);
      const disposition = getGameplaySettingsApplyDisposition(response);
      if (disposition === 'committed') {
        if (response.snapshot) {
          draftGenerationRef.current += 1;
          previewRequestGenerationRef.current += 1;
          setLoadState({ state: 'ready', snapshot: response.snapshot });
          setDraft(response.snapshot.values);
        } else {
          try {
            await load('applyRefresh');
          } catch {
            // The durable commit is authoritative even if refreshing its display fails.
          }
        }
        if (!isCurrentApply(generation, requestScopeKey)) return;
        setMessageKey('gameplaySettings.applied');
        try {
          await onApplied?.(requestScope);
        } catch (error) {
          if (isCurrentApply(generation, requestScopeKey)) {
            try {
              await onError?.(error, 'load', requestScope);
            } catch {
              // A failed follow-up notification cannot change the committed outcome.
            }
            setMessageKey('gameplaySettings.applied');
          }
        }
      } else if (disposition === 'rolledBack') {
        const didReload = await load('applyRefresh');
        if (didReload && isCurrentApply(generation, requestScopeKey)) {
          setMessageKey('gameplaySettings.rolledBack');
        }
      } else {
        draftGenerationRef.current += 1;
        loadRequestGenerationRef.current += 1;
        previewRequestGenerationRef.current += 1;
        setLoadState({ state: 'conflict', snapshot: null });
        setDraft(null);
        setBetaAcknowledged(false);
        setMessageKey('gameplaySettings.recoveryRequired');
        try {
          await onRecoveryRequired?.(requestScope);
        } catch (error) {
          if (isCurrentApply(generation, requestScopeKey)) {
            await reportError(error, 'apply');
            setMessageKey('gameplaySettings.recoveryRequired');
          }
        }
      }
    } catch (error) {
      if (!isCurrentApply(generation, requestScopeKey)) return;
      setPreview(null);
      await reportError(error, 'apply');
    } finally {
      endApplyGuard(generation);
      if (isCurrentApply(generation, requestScopeKey)) {
        setBusy(null);
      }
    }
  }, [
    beginApplyGuard,
    betaAcknowledged,
    bridge,
    canApply,
    endApplyGuard,
    isCurrentApply,
    load,
    onApplied,
    onError,
    onRecoveryRequired,
    preview,
    reportError,
    scopeKey,
    stableScope,
    staticEditorLockedByPackage
  ]);

  const reset = useCallback(() => {
    if (!snapshot) return;
    draftGenerationRef.current += 1;
    previewRequestGenerationRef.current += 1;
    setDraft(snapshot.values);
    setPreview(null);
    setMessageKey(null);
  }, [snapshot]);

  return (
    <section
      aria-labelledby="gameplay-settings-title"
      className="panel wide-panel gameplay-settings"
    >
      <header className="gameplay-settings__header">
        <div className="gameplay-settings__heading">
          <FlaskConical aria-hidden="true" size={20} />
          <div>
            <div className="gameplay-settings__title-row">
              <h2 id="gameplay-settings-title">{t('gameplaySettings.title')}</h2>
              <span className="gameplay-settings__beta-badge">{t('gameplaySettings.betaBadge')}</span>
            </div>
            <p>{t('gameplaySettings.description')}</p>
          </div>
        </div>
        {stableScope && deliveryMode === 'fixed' ? (
          <button
            className="secondary-button"
            disabled={busy !== null || hasProtectedDraft}
            onClick={() => void load()}
            type="button"
          >
            {busy === 'load'
              ? t('gameplaySettings.loading')
              : t('gameplaySettings.refresh')}
          </button>
        ) : null}
      </header>

      <div className="gameplay-settings__beta-notice" role="note">
        <ShieldAlert aria-hidden="true" size={20} />
        <div>
          <strong>{t('gameplaySettings.betaNoticeTitle')}</strong>
          <p>{t('gameplaySettings.betaNoticeDescription')}</p>
        </div>
      </div>

      {!stableScope ? (
        <div className="gameplay-settings__setup" role="status">
          <div>
            <h3>{t('gameplaySettings.projectSetupTitle')}</h3>
            <p>{t('gameplaySettings.projectSetupDescription')}</p>
          </div>
          <button className="primary-button" onClick={onOpenProjectSetup} type="button">
            {t('gameplaySettings.openProjectSetup')}
          </button>
        </div>
      ) : null}

      {messageKey ? (
        <p className="gameplay-settings__message" role="status" aria-live="polite">
          {t(messageKey)}
        </p>
      ) : null}

      {busy === 'load' && loadState === null ? (
        <p className="gameplay-settings__empty" role="status">
          {t('gameplaySettings.loading')}
        </p>
      ) : null}

      {loadState && !isEditableState(loadState.state) ? (
        <div className="gameplay-settings__empty" role="status">
          <h3>{t(`gameplaySettings.state.${loadState.state}.title`)}</h3>
          <p>{t(`gameplaySettings.state.${loadState.state}.description`)}</p>
          {loadState.detail ? <p>{loadState.detail}</p> : null}
        </div>
      ) : null}

      {snapshot ? (
        <div
          className="gameplay-settings__identity"
          aria-label={t('gameplaySettings.executableProfile')}
        >
          <strong>{t('gameplaySettings.executableProfile')}</strong>
          <span>{snapshot.executableProfileId}</span>
          <span>{t('gameplaySettings.titleId', { value: snapshot.titleId })}</span>
          <span>
            {t('gameplaySettings.supportedGameVersion', {
              version: snapshot.supportedGameVersion
            })}
          </span>
          <span>{t('gameplaySettings.generation', { generation: snapshot.generation })}</span>
        </div>
      ) : null}

      {stableScope ? (
        <section
          aria-labelledby="gameplay-settings-delivery-title"
          className="gameplay-settings__delivery"
        >
          <div className="gameplay-settings__delivery-heading">
            <h3 id="gameplay-settings-delivery-title">
              {t('gameplaySettings.delivery.title')}
            </h3>
            <p>{t('gameplaySettings.delivery.description')}</p>
          </div>
          <div
            aria-label={t('gameplaySettings.delivery.title')}
            className="gameplay-settings__delivery-options"
            role="group"
          >
            <button
              aria-pressed={deliveryMode === 'fixed'}
              className="gameplay-settings__delivery-option"
              onClick={() => {
                deliveryModeChosenRef.current = true;
                setDeliveryMode('fixed');
              }}
              type="button"
            >
              <strong>{t('gameplaySettings.delivery.fixedTitle')}</strong>
              <span>{t('gameplaySettings.delivery.fixedDescription')}</span>
            </button>
            <button
              aria-pressed={deliveryMode === 'runtime'}
              className="gameplay-settings__delivery-option"
              onClick={() => {
                deliveryModeChosenRef.current = true;
                setDeliveryMode('runtime');
              }}
              type="button"
            >
              <strong>{t('gameplaySettings.delivery.runtimeTitle')}</strong>
              <span>{t('gameplaySettings.delivery.runtimeDescription')}</span>
            </button>
          </div>
        </section>
      ) : null}

      <div
        className="gameplay-settings__mode-content"
        hidden={deliveryMode !== 'fixed'}
      >
      {staticEditorLockedByPackage ? (
        <div className="gameplay-settings__warning" role="status">
          <strong>
            {t(
              inGamePackageState === 'installed' || inGamePackageState === 'upgradeAvailable'
                ? 'gameplaySettings.inGamePackage.staticLockInstalledTitle'
                : inGamePackageState === null
                  ? 'gameplaySettings.inGamePackage.staticLockCheckingTitle'
                  : 'gameplaySettings.inGamePackage.staticLockAttentionTitle'
            )}
          </strong>
          <p>
            {t(
              inGamePackageState === 'installed' || inGamePackageState === 'upgradeAvailable'
                ? 'gameplaySettings.inGamePackage.staticLockInstalledDescription'
                : inGamePackageState === null
                  ? 'gameplaySettings.inGamePackage.staticLockCheckingDescription'
                  : 'gameplaySettings.inGamePackage.staticLockAttentionDescription'
            )}
          </p>
        </div>
      ) : null}

      <div className="gameplay-settings__controls">
        <div
          className={`gameplay-settings__control-card${canEditExperienceShare ? '' : ' gameplay-settings__control-card--unavailable'}`}
        >
          <label className="gameplay-settings__toggle">
            <input
              checked={shownValues.experienceShareEnabled}
              disabled={busy !== null || !canEditExperienceShare}
              onChange={(event) =>
                updateDraft({
                  ...shownValues,
                  experienceShareEnabled: event.currentTarget.checked
                })
              }
              type="checkbox"
            />
            <span>
              <strong>{t('gameplaySettings.experienceShare')}</strong>
              <small>{t('gameplaySettings.experienceShareHelp')}</small>
            </span>
          </label>
          <CapabilityDetails capability={experienceShareCapability} t={t} />
        </div>

        <div
          className={`gameplay-settings__control-card${canEditExperienceRate ? '' : ' gameplay-settings__control-card--unavailable'}`}
        >
          <label className="gameplay-settings__field">
            <span>
              <strong>{t('gameplaySettings.experienceRate')}</strong>
              <small>{t('gameplaySettings.experienceRateHelp')}</small>
            </span>
            <select
              disabled={busy !== null || !canEditExperienceRate}
              onChange={(event) =>
                updateDraft({
                  ...shownValues,
                  experienceRateBasisPoints: Number(event.currentTarget.value)
                })
              }
              value={shownValues.experienceRateBasisPoints}
            >
              {rateChoices.map((value) => (
                <option key={value} value={value}>
                  {t('gameplaySettings.percent', { value: value / 100 })}
                </option>
              ))}
            </select>
          </label>
          <CapabilityDetails capability={experienceRateCapability} t={t} />
        </div>

        <div
          className={`gameplay-settings__control-card${canEditLevelCap ? '' : ' gameplay-settings__control-card--unavailable'}`}
        >
          <div className="gameplay-settings__cap">
            <label className="gameplay-settings__toggle">
              <input
                checked={shownValues.levelCapEnabled}
                disabled={busy !== null || !canEditLevelCap}
                onChange={(event) =>
                  updateDraft({
                    ...shownValues,
                    levelCap: event.currentTarget.checked ? shownValues.levelCap : 100,
                    levelCapEnabled: event.currentTarget.checked
                  })
                }
                type="checkbox"
              />
              <span>
                <strong>{t('gameplaySettings.levelCap')}</strong>
                <small>{t('gameplaySettings.levelCapHelp')}</small>
              </span>
            </label>
            <label className="gameplay-settings__field gameplay-settings__field--compact">
              <span>{t('gameplaySettings.level')}</span>
              <select
                disabled={busy !== null || !canEditLevelCap || !shownValues.levelCapEnabled}
                onChange={(event) =>
                  updateDraft({ ...shownValues, levelCap: Number(event.currentTarget.value) })
                }
                value={shownValues.levelCap}
              >
                {Array.from({ length: 100 }, (_, index) => index + 1).map((level) => (
                  <option key={level} value={level}>
                    {level}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <CapabilityDetails capability={levelCapCapability} t={t} />
        </div>
      </div>

      <label className="gameplay-settings__acknowledgement">
        <input
          checked={betaAcknowledged}
          disabled={!editorIsReady || busy !== null || staticEditorLockedByPackage}
          onChange={(event) => {
            const isAcknowledged = event.currentTarget.checked;
            setBetaAcknowledged(isAcknowledged);
            if (!isAcknowledged) {
              setPreview(null);
            }
          }}
          type="checkbox"
        />
        <span>{t('gameplaySettings.betaAcknowledgement')}</span>
      </label>

      <div className="gameplay-settings__actions">
        <button
          className="secondary-button"
          disabled={busy !== null || staticEditorLockedByPackage || !canSetVanillaValues}
          onClick={setVanillaValues}
          type="button"
        >
          {t('gameplaySettings.setVanillaValues')}
        </button>
        <button
          className="secondary-button"
          disabled={busy !== null || !hasChanges}
          onClick={reset}
          type="button"
        >
          {t('gameplaySettings.resetDraft')}
        </button>
        <button
          className="primary-button"
          disabled={
            busy !== null ||
            !canApply ||
            !hasChanges ||
            !betaAcknowledged ||
            !editorIsReady ||
            staticEditorLockedByPackage
          }
          onClick={(event) => {
            focusReviewOnCompletionRef.current = event.detail === 0;
            void review();
          }}
          type="button"
        >
          {busy === 'preview'
            ? t('gameplaySettings.reviewing')
            : t('gameplaySettings.review')}
        </button>
      </div>

      <p aria-atomic="true" aria-live="polite" className="sr-only" role="status">
        {preview ? t('gameplaySettings.reviewReady') : ''}
      </p>

      {preview ? (
        <div
          aria-labelledby="gameplay-settings-review-title"
          className="gameplay-settings__review"
          ref={reviewRegionRef}
          role="region"
          tabIndex={-1}
        >
          <h3 id="gameplay-settings-review-title">{t('gameplaySettings.reviewTitle')}</h3>
          <p>{t('gameplaySettings.reviewDescription')}</p>
          <dl>
            {changedRows(preview.before.values, preview.after.values).map((row) => (
              <div key={row.key}>
                <dt>{t(`gameplaySettings.${row.key}`)}</dt>
                <dd>
                  {formatValue(row.key, row.before, t)}
                  <span aria-hidden="true"> {'>'} </span>
                  <span className="sr-only">{t('gameplaySettings.changesTo')}</span>
                  {formatValue(row.key, row.after, t)}
                </dd>
              </div>
            ))}
          </dl>
          <div className="gameplay-settings__actions">
            <button
              className="secondary-button"
              disabled={busy !== null}
              onClick={() => setPreview(null)}
              type="button"
            >
              {t('gameplaySettings.cancelReview')}
            </button>
            <button
              className="primary-button"
              disabled={
                busy !== null ||
                !canApply ||
                !betaAcknowledged ||
                staticEditorLockedByPackage ||
                isGameplaySettingsPreviewExpired(preview)
              }
              onClick={() => void apply()}
              type="button"
            >
              {busy === 'apply'
                ? t('gameplaySettings.applying')
                : t('gameplaySettings.apply')}
            </button>
          </div>
        </div>
      ) : null}
      </div>

      {stableScope ? (
        <div
          className="gameplay-settings__mode-content"
          hidden={deliveryMode !== 'runtime'}
        >
          <InGameSettingsPackagePanel
            armCriticalWriteGuard={armCriticalWriteGuard}
            bridge={bridge}
            canApply={canApply}
            onApplied={onApplied}
            onApplyBusyChange={onApplyBusyChange}
            onDirtyChange={setInGamePackageDirty}
            onError={onError}
            onRecoveryRequired={onRecoveryRequired}
            onStaticEditorLockChange={setInGamePackageLocksStaticEditor}
            onStateChange={setInGamePackageState}
            scope={stableScope}
            staticEditorBusy={busy !== null || hasProtectedDraft}
            staticSettingsAreVanilla={staticSettingsAreVanilla}
          />
        </div>
      ) : null}
    </section>
  );
}

function CapabilityDetails({
  capability,
  t
}: {
  capability: GameplaySettingsCapability;
  t: (key: string, values?: Record<string, string | number>) => string;
}) {
  return (
    <div className="gameplay-settings__capability">
      <span
        className={`gameplay-settings__availability gameplay-settings__availability--${capability.available ? 'available' : 'unavailable'}`}
      >
        {t(
          capability.available
            ? 'gameplaySettings.capabilityAvailable'
            : 'gameplaySettings.capabilityUnavailable'
        )}
      </span>
      {capability.scopeCode.trim() ? (
        <p>
          <strong>{t('gameplaySettings.capabilityScope')}</strong>
          <span>{t(capabilityScopeMessageKey(capability.scopeCode))}</span>
        </p>
      ) : null}
      {!capability.available ? (
        <p>
          <strong>{t('gameplaySettings.capabilityReason')}</strong>
          <span>{t(capabilityReasonMessageKey(capability.reasonCode))}</span>
        </p>
      ) : null}
    </div>
  );
}

function capabilityScopeMessageKey(scopeCode: string) {
  switch (scopeCode) {
    case 'sv-exp-share-normal-battle-nonparticipants':
      return 'gameplaySettings.capabilityScope.svExperienceShare';
    case 'sv-exp-rate-normal-battle-calculator':
      return 'gameplaySettings.capabilityScope.svExperienceRate';
    case 'swsh-exp-share-battle-catch-decision':
      return 'gameplaySettings.capabilityScope.swshExperienceShare';
    case 'swsh-exp-rate-battle-catch-final-award':
      return 'gameplaySettings.capabilityScope.swshExperienceRate';
    case 'za-exp-share-two-battle-award-builders':
      return 'gameplaySettings.capabilityScope.zaExperienceShare';
    case 'za-exp-rate-two-battle-award-paths':
      return 'gameplaySettings.capabilityScope.zaExperienceRate';
    case 'sv-level-cap-normal-battle-award':
      return 'gameplaySettings.capabilityScope.svLevelCap';
    case 'swsh-level-cap-battle-catch-final-award':
      return 'gameplaySettings.capabilityScope.swshLevelCap';
    case 'za-level-cap-two-battle-award-paths':
      return 'gameplaySettings.capabilityScope.zaLevelCap';
    case 'comprehensive-level-cap-unavailable':
      return 'gameplaySettings.capabilityScope.levelCapUnavailable';
    default:
      return 'gameplaySettings.capabilityScope.generic';
  }
}

function capabilityReasonMessageKey(reasonCode: string) {
  switch (reasonCode) {
    case 'unavailable-incomplete-recipient-and-source-contracts':
      return 'gameplaySettings.capabilityReason.incompleteContracts';
    case 'project-setup-required':
      return 'gameplaySettings.capabilityReason.projectSetup';
    case 'loading':
      return 'gameplaySettings.capabilityReason.loading';
    default:
      return 'gameplaySettings.capabilityReason.generic';
  }
}

function capabilityForDisplay(
  capability: GameplaySettingsCapability | undefined,
  legacyAvailable: boolean | undefined,
  fallbackReason: string
): GameplaySettingsCapability {
  if (!capability) {
    return { available: false, reasonCode: fallbackReason, scopeCode: '' };
  }
  if (capability.available && legacyAvailable === false) {
    return { ...capability, available: false, reasonCode: 'profile-field-unavailable' };
  }
  return capability;
}

export async function tryArmGameplaySettingsWriteGuard(
  armCriticalWriteGuard: () => Promise<boolean>
): Promise<boolean> {
  try {
    return await armCriticalWriteGuard();
  } catch {
    return false;
  }
}

export function gameplaySettingsLoadOwnsBusy(mode: GameplaySettingsLoadMode): boolean {
  return mode === 'standalone';
}

export function getGameplaySettingsApplyDisposition(
  response: Pick<ApplyGameplaySettingsUpdateResponse, 'outcome'>
): ApplyGameplaySettingsUpdateResponse['outcome'] {
  return response.outcome;
}

export type GameplaySettingsApplyGuardController = {
  begin: (generation: number) => Promise<boolean>;
  end: (generation?: number) => void;
  isActive: (generation: number) => boolean;
};

export function createGameplaySettingsApplyGuardController(
  readArmCriticalWriteGuard: () => () => Promise<boolean>,
  readOnApplyBusyChange: () => ((isBusy: boolean) => void) | undefined
): GameplaySettingsApplyGuardController {
  let activeGeneration: number | null = null;
  return {
    async begin(generation) {
      activeGeneration = generation;
      readOnApplyBusyChange()?.(true);
      return tryArmGameplaySettingsWriteGuard(readArmCriticalWriteGuard());
    },
    end(generation) {
      if (
        activeGeneration === null ||
        (generation !== undefined && activeGeneration !== generation)
      ) {
        return;
      }
      activeGeneration = null;
      readOnApplyBusyChange()?.(false);
    },
    isActive(generation) {
      return activeGeneration === generation;
    }
  };
}

export function hasGameplaySettingsOutputScope(
  scope: OutputSafetyScope | null
): scope is OutputSafetyScope {
  return Boolean(
    scope?.paths.baseExeFsPath?.trim() &&
      scope.paths.outputRootPath?.trim() &&
      scope.paths.selectedGame
  );
}

export function shouldRenderGameplaySettings(
  _hideWhenUnavailable: boolean,
  _state: GameplaySettingsState | null
) {
  return true;
}

export function copyGameplaySettingsScope(scope: OutputSafetyScope): OutputSafetyScope {
  return {
    paths: {
      baseExeFsPath: scope.paths.baseExeFsPath,
      baseRomFsPath: scope.paths.baseRomFsPath,
      gameTextLanguage: scope.paths.gameTextLanguage,
      outputRootPath: scope.paths.outputRootPath,
      pokemonLegendsZASupportFolderPath: scope.paths.pokemonLegendsZASupportFolderPath,
      saveFilePath: scope.paths.saveFilePath,
      scarletVioletSupportFolderPath: scope.paths.scarletVioletSupportFolderPath,
      selectedGame: scope.paths.selectedGame
    },
    projectId: scope.projectId
  };
}

export function gameplaySettingsScopeKey(scope: OutputSafetyScope) {
  return JSON.stringify(copyGameplaySettingsScope(scope));
}

export function isGameplaySettingsPreviewExpired(
  preview: Pick<PreviewGameplaySettingsUpdateResponse, 'expiresAtUtc'>,
  now = Date.now()
) {
  const expiresAt = Date.parse(preview.expiresAtUtc);
  return !Number.isFinite(expiresAt) || expiresAt <= now;
}

export function getAvailableVanillaGameplaySettingsValues(
  current: GameplaySettingsValues,
  availability: {
    experienceRate: boolean;
    experienceShare: boolean;
    levelCap: boolean;
  }
): GameplaySettingsValues {
  return {
    experienceRateBasisPoints: availability.experienceRate
      ? 10_000
      : current.experienceRateBasisPoints,
    experienceShareEnabled: availability.experienceShare
      ? true
      : current.experienceShareEnabled,
    levelCap: availability.levelCap ? 100 : current.levelCap,
    levelCapEnabled: availability.levelCap ? false : current.levelCapEnabled
  };
}

function isEditableState(state: GameplaySettingsState) {
  return state === 'ready';
}

function sameValues(left: GameplaySettingsValues, right: GameplaySettingsValues) {
  return (
    left.experienceShareEnabled === right.experienceShareEnabled &&
    left.experienceRateBasisPoints === right.experienceRateBasisPoints &&
    left.levelCapEnabled === right.levelCapEnabled &&
    left.levelCap === right.levelCap
  );
}

type ChangedRow = {
  after: boolean | number;
  before: boolean | number;
  key: 'experienceRate' | 'experienceShare' | 'levelCap';
};

function changedRows(before: GameplaySettingsValues, after: GameplaySettingsValues): ChangedRow[] {
  const rows: ChangedRow[] = [];
  if (before.experienceShareEnabled !== after.experienceShareEnabled) {
    rows.push({
      key: 'experienceShare',
      before: before.experienceShareEnabled,
      after: after.experienceShareEnabled
    });
  }
  if (before.experienceRateBasisPoints !== after.experienceRateBasisPoints) {
    rows.push({
      key: 'experienceRate',
      before: before.experienceRateBasisPoints,
      after: after.experienceRateBasisPoints
    });
  }
  if (before.levelCapEnabled !== after.levelCapEnabled || before.levelCap !== after.levelCap) {
    rows.push({
      key: 'levelCap',
      before: before.levelCapEnabled ? before.levelCap : false,
      after: after.levelCapEnabled ? after.levelCap : false
    });
  }
  return rows;
}

function formatValue(
  key: ChangedRow['key'],
  value: boolean | number,
  t: (key: string, values?: Record<string, string | number>) => string
) {
  if (typeof value === 'boolean') {
    return t(value ? 'gameplaySettings.on' : 'gameplaySettings.off');
  }
  if (key === 'experienceRate') {
    return t('gameplaySettings.percent', { value: value / 100 });
  }
  return String(value);
}
