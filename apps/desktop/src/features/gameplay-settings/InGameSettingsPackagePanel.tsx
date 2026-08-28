/* SPDX-License-Identifier: GPL-3.0-only */

import { Gamepad2, PackageCheck, ShieldCheck } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type {
  InGameSettingsPackageOperation,
  InGameSettingsPackageSnapshot,
  PreviewInGameSettingsPackageResponse
} from '../../bridge/inGameSettingsPackageContracts';
import type { OutputSafetyScope } from '../../bridge/outputSafetyContracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { useLocalization } from '../../localization';
import './InGameSettingsPackagePanel.css';

type InGameSettingsPackageBridge = Pick<
  ProjectBridge,
  | 'applyInGameSettingsPackage'
  | 'inspectInGameSettingsPackage'
  | 'previewInGameSettingsPackage'
>;

type InGameSettingsPackagePanelProps = {
  armCriticalWriteGuard: () => Promise<boolean>;
  bridge: InGameSettingsPackageBridge;
  canApply: boolean;
  onApplied?: (scope: OutputSafetyScope) => Promise<void> | void;
  onApplyBusyChange?: (isBusy: boolean) => void;
  onDirtyChange?: (isDirty: boolean) => void;
  onError?: (
    error: unknown,
    operation: 'apply' | 'load' | 'preview',
    scope: OutputSafetyScope
  ) => Promise<void> | void;
  onRecoveryRequired?: (scope: OutputSafetyScope) => Promise<void> | void;
  onStaticEditorLockChange?: (isLocked: boolean) => void;
  onStateChange?: (state: InGameSettingsPackageSnapshot['state'] | null) => void;
  scope: OutputSafetyScope;
  staticEditorBusy: boolean;
  staticSettingsAreVanilla: boolean;
};

type PackageBusyState = 'apply' | 'load' | 'preview' | null;
type InstallationTarget = 'atmosphere' | 'eden' | 'ryujinx';

export function InGameSettingsPackagePanel({
  armCriticalWriteGuard,
  bridge,
  canApply,
  onApplied,
  onApplyBusyChange,
  onDirtyChange,
  onError,
  onRecoveryRequired,
  onStaticEditorLockChange,
  onStateChange,
  scope,
  staticEditorBusy,
  staticSettingsAreVanilla
}: InGameSettingsPackagePanelProps) {
  const { t } = useLocalization();
  const [snapshot, setSnapshot] = useState<InGameSettingsPackageSnapshot | null>(null);
  const [preview, setPreview] = useState<PreviewInGameSettingsPackageResponse | null>(null);
  const [reviewAcknowledged, setReviewAcknowledged] = useState(false);
  const [busy, setBusy] = useState<PackageBusyState>(null);
  const [messageKey, setMessageKey] = useState<string | null>(null);
  const [recoveryRequired, setRecoveryRequired] = useState(false);
  const [installationTarget, setInstallationTarget] =
    useState<InstallationTarget>('atmosphere');
  const requestGenerationRef = useRef(0);
  const applyGenerationRef = useRef(0);
  const isMountedRef = useRef(true);
  const applyBusyReportedRef = useRef(false);
  const staticSettingsAreVanillaRef = useRef(staticSettingsAreVanilla);
  staticSettingsAreVanillaRef.current = staticSettingsAreVanilla;

  const scopeKey = useMemo(() => JSON.stringify(scope), [scope]);
  const scopeKeyRef = useRef(scopeKey);
  scopeKeyRef.current = scopeKey;

  const reportApplyBusy = useCallback(
    (isBusy: boolean) => {
      if (applyBusyReportedRef.current === isBusy) return;
      applyBusyReportedRef.current = isBusy;
      try {
        onApplyBusyChange?.(isBusy);
      } catch {
        // A host notification cannot change the package transaction state.
      }
    },
    [onApplyBusyChange]
  );

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      requestGenerationRef.current += 1;
      applyGenerationRef.current += 1;
      reportApplyBusy(false);
    };
  }, [reportApplyBusy]);

  useEffect(() => {
    onDirtyChange?.(preview !== null);
    return () => onDirtyChange?.(false);
  }, [onDirtyChange, preview]);

  useEffect(() => {
    onStateChange?.(snapshot?.state ?? null);
    return () => onStateChange?.(null);
  }, [onStateChange, snapshot?.state]);

  useEffect(() => {
    onStaticEditorLockChange?.(snapshot?.blocksStaticEditor ?? false);
    return () => onStaticEditorLockChange?.(false);
  }, [onStaticEditorLockChange, snapshot]);

  const reportError = useCallback(
    async (error: unknown, operation: 'apply' | 'load' | 'preview') => {
      setMessageKey('gameplaySettings.inGamePackage.error');
      try {
        await onError?.(error, operation, scope);
      } catch {
        // The panel remains fail-closed even when a host notification fails.
      }
    },
    [onError, scope]
  );

  const inspect = useCallback(
    async (showBusy = true): Promise<InGameSettingsPackageSnapshot | null> => {
      const generation = ++requestGenerationRef.current;
      const requestScopeKey = scopeKey;
      if (showBusy) {
        setBusy('load');
      }
      setPreview(null);
      setReviewAcknowledged(false);
      setRecoveryRequired(false);
      setMessageKey(null);
      try {
        const response = await bridge.inspectInGameSettingsPackage({ scope });
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          scopeKeyRef.current !== requestScopeKey
        ) {
          return null;
        }
        setSnapshot(response.snapshot);
        return response.snapshot;
      } catch (error) {
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          scopeKeyRef.current !== requestScopeKey
        ) {
          return null;
        }
        setSnapshot(null);
        await reportError(error, 'load');
        return null;
      } finally {
        if (
          showBusy &&
          isMountedRef.current &&
          requestGenerationRef.current === generation &&
          scopeKeyRef.current === requestScopeKey
        ) {
          setBusy(null);
        }
      }
    },
    [bridge, reportError, scope, scopeKey]
  );

  useEffect(() => {
    requestGenerationRef.current += 1;
    applyGenerationRef.current += 1;
    reportApplyBusy(false);
    setSnapshot(null);
    setPreview(null);
    setReviewAcknowledged(false);
    setRecoveryRequired(false);
    setMessageKey(null);
    void inspect();
  }, [inspect, reportApplyBusy, scopeKey]);

  useEffect(() => {
    if (!preview || preview.operation !== 'install' || staticSettingsAreVanilla) return;
    setPreview(null);
    setReviewAcknowledged(false);
    setMessageKey('gameplaySettings.inGamePackage.vanillaRequired');
  }, [preview, staticSettingsAreVanilla]);

  useEffect(() => {
    if (!preview) return;
    let timer: number | null = null;
    let disposed = false;
    const expiresAt = Date.parse(preview.expiresAtUtc);
    const checkExpiry = () => {
      if (disposed) return;
      const remaining = expiresAt - Date.now();
      if (!Number.isFinite(remaining) || remaining <= 0) {
        setPreview(null);
        setReviewAcknowledged(false);
        setMessageKey('gameplaySettings.inGamePackage.reviewExpired');
        return;
      }
      timer = window.setTimeout(checkExpiry, Math.min(remaining, 60_000));
    };
    checkExpiry();
    return () => {
      disposed = true;
      if (timer !== null) window.clearTimeout(timer);
    };
  }, [preview]);

  const reviewOperation = useCallback(
    async (operation: InGameSettingsPackageOperation) => {
      if (
        !snapshot ||
        busy !== null ||
        staticEditorBusy ||
        !canApply ||
        recoveryRequired ||
        !canReviewOperation(snapshot, operation) ||
        (operation === 'install' && !staticSettingsAreVanilla)
      ) {
        return;
      }

      const generation = ++requestGenerationRef.current;
      const requestScopeKey = scopeKey;
      setBusy('preview');
      setPreview(null);
      setReviewAcknowledged(false);
      setMessageKey(null);
      try {
        const response = await bridge.previewInGameSettingsPackage({
          expectedRevision: snapshot.revision,
          operation,
          scope
        });
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          scopeKeyRef.current !== requestScopeKey
        ) {
          return;
        }
        if (operation === 'install' && !staticSettingsAreVanillaRef.current) {
          setMessageKey('gameplaySettings.inGamePackage.vanillaRequired');
          return;
        }
        setPreview(response);
      } catch (error) {
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          scopeKeyRef.current !== requestScopeKey
        ) {
          return;
        }
        await reportError(error, 'preview');
      } finally {
        if (
          isMountedRef.current &&
          requestGenerationRef.current === generation &&
          scopeKeyRef.current === requestScopeKey
        ) {
          setBusy(null);
        }
      }
    },
    [
      bridge,
      busy,
      canApply,
      recoveryRequired,
      reportError,
      scope,
      scopeKey,
      snapshot,
      staticEditorBusy,
      staticSettingsAreVanilla
    ]
  );

  const applyReviewedOperation = useCallback(async () => {
    if (
      !preview ||
      !reviewAcknowledged ||
      busy !== null ||
      staticEditorBusy ||
      !canApply ||
      recoveryRequired ||
      isPackageReviewExpired(preview) ||
      (preview.operation === 'install' && !staticSettingsAreVanilla)
    ) {
      return;
    }

    const generation = ++applyGenerationRef.current;
    const requestScopeKey = scopeKey;
    setBusy('apply');
    setMessageKey(null);
    reportApplyBusy(true);
    try {
      const armed = await tryArmInGameSettingsPackageWriteGuard(armCriticalWriteGuard);
      if (
        !armed ||
        !isMountedRef.current ||
        applyGenerationRef.current !== generation ||
        scopeKeyRef.current !== requestScopeKey
      ) {
        if (
          isMountedRef.current &&
          applyGenerationRef.current === generation &&
          scopeKeyRef.current === requestScopeKey
        ) {
          setMessageKey('gameplaySettings.inGamePackage.guardFailed');
        }
        return;
      }
      if (
        isPackageReviewExpired(preview) ||
        (preview.operation === 'install' && !staticSettingsAreVanillaRef.current)
      ) {
        setPreview(null);
        setReviewAcknowledged(false);
        setMessageKey(
          preview.operation === 'install'
            ? 'gameplaySettings.inGamePackage.vanillaRequired'
            : 'gameplaySettings.inGamePackage.reviewExpired'
        );
        return;
      }

      const response = await bridge.applyInGameSettingsPackage({
        reviewId: preview.reviewId,
        scope
      });
      if (
        !isMountedRef.current ||
        applyGenerationRef.current !== generation ||
        scopeKeyRef.current !== requestScopeKey
      ) {
        return;
      }

      setPreview(null);
      setReviewAcknowledged(false);
      if (response.outcome === 'committed') {
        if (response.snapshot) {
          setSnapshot(response.snapshot);
        } else {
          await inspect(false);
        }
        if (
          !isMountedRef.current ||
          applyGenerationRef.current !== generation ||
          scopeKeyRef.current !== requestScopeKey
        ) {
          return;
        }
        setMessageKey(
          `gameplaySettings.inGamePackage.${preview.operation}Committed`
        );
        try {
          await onApplied?.(scope);
        } catch (error) {
          try {
            await onError?.(error, 'load', scope);
          } catch {
            // The committed receipt remains authoritative.
          }
          setMessageKey(
            `gameplaySettings.inGamePackage.${preview.operation}Committed`
          );
        }
      } else if (response.outcome === 'rolledBack') {
        await inspect(false);
        if (
          isMountedRef.current &&
          applyGenerationRef.current === generation &&
          scopeKeyRef.current === requestScopeKey
        ) {
          setMessageKey('gameplaySettings.inGamePackage.rolledBack');
        }
      } else {
        setSnapshot(null);
        setRecoveryRequired(true);
        setMessageKey('gameplaySettings.inGamePackage.recoveryRequired');
        try {
          await onRecoveryRequired?.(scope);
        } catch (error) {
          try {
            await onError?.(error, 'apply', scope);
          } catch {
            // Recovery remains required even when host notification fails.
          }
          setMessageKey('gameplaySettings.inGamePackage.recoveryRequired');
        }
      }
    } catch (error) {
      if (
        !isMountedRef.current ||
        applyGenerationRef.current !== generation ||
        scopeKeyRef.current !== requestScopeKey
      ) {
        return;
      }
      setPreview(null);
      setReviewAcknowledged(false);
      await reportError(error, 'apply');
    } finally {
      reportApplyBusy(false);
      if (
        isMountedRef.current &&
        applyGenerationRef.current === generation &&
        scopeKeyRef.current === requestScopeKey
      ) {
        setBusy(null);
      }
    }
  }, [
    armCriticalWriteGuard,
    bridge,
    busy,
    canApply,
    inspect,
    onApplied,
    onRecoveryRequired,
    preview,
    recoveryRequired,
    reportApplyBusy,
    reportError,
    reviewAcknowledged,
    scope,
    scopeKey,
    staticEditorBusy,
    staticSettingsAreVanilla
  ]);

  const installBlockedByStaticSettings =
    snapshot?.state === 'notInstalled' && !staticSettingsAreVanilla;
  const actionsDisabled = busy !== null || staticEditorBusy || !canApply || recoveryRequired;
  const compatibilityPackage = snapshot?.availablePackage ?? snapshot?.installedPackage ?? null;
  const titleId = compatibilityPackage?.titleId ?? '<TITLE_ID>';
  const generatedTitleSourcePaths = [
    `<Output Root>/atmosphere/contents/${titleId}/exefs`,
    `<Output Root>/atmosphere/contents/${titleId}/romfs`
  ];
  const settingsJournalPath = `config/km-editor/gameplay-settings/${titleId}/settings.bin`;
  const generatedSettingsSourcePath = `<Output Root>/${settingsJournalPath}`;
  const installationPaths: Record<
    InstallationTarget,
    {
      defaultSettingsDestination?: string;
      settingsDestination: string;
      titleDestinations: readonly [string, string];
    }
  > = {
    atmosphere: {
      settingsDestination: `<Console SD card root>/${settingsJournalPath}`,
      titleDestinations: [
        `<Console SD card root>/atmosphere/contents/${titleId}/exefs`,
        `<Console SD card root>/atmosphere/contents/${titleId}/romfs`
      ]
    },
    ryujinx: {
      settingsDestination: `<Emulated SD root>/${settingsJournalPath}`,
      defaultSettingsDestination:
        `<Emulator data folder>/sdcard/${settingsJournalPath}`,
      titleDestinations: [
        `<Emulator data folder>/mods/contents/${titleId}/KM Gameplay Settings/exefs`,
        `<Emulator data folder>/mods/contents/${titleId}/KM Gameplay Settings/romfs`
      ]
    },
    eden: {
      settingsDestination: `<Configured emulated SD root>/${settingsJournalPath}`,
      defaultSettingsDestination: `<Eden data folder>/sdmc/${settingsJournalPath}`,
      titleDestinations: [
        `<Eden data folder>/load/${titleId}/KM Gameplay Settings/exefs`,
        `<Eden data folder>/load/${titleId}/KM Gameplay Settings/romfs`
      ]
    }
  };
  const selectedInstallationPaths = installationPaths[installationTarget];
  const stateTitleKey = snapshot ? packageStateTitleMessageKey(snapshot) : null;
  const stateDescriptionKey = snapshot
    ? packageStateDescriptionMessageKey(snapshot)
    : null;
  const showExecutableInputAssessment = Boolean(
    snapshot &&
      (snapshot.executableInput.source === 'standaloneOutput' ||
        snapshot.executableInput.compatibility !== 'absent')
  );

  return (
    <section
      aria-labelledby="in-game-settings-package-title"
      className="in-game-settings-package"
    >
      <header className="in-game-settings-package__header">
        <div className="in-game-settings-package__heading">
          <Gamepad2 aria-hidden="true" size={20} />
          <div>
            <div className="in-game-settings-package__title-row">
              <h3 id="in-game-settings-package-title">
                {t('gameplaySettings.inGamePackage.title')}
              </h3>
              <span className="gameplay-settings__beta-badge">
                {t('gameplaySettings.betaBadge')}
              </span>
            </div>
            <p>{t('gameplaySettings.inGamePackage.description')}</p>
          </div>
        </div>
        <button
          className="secondary-button"
          disabled={busy !== null || preview !== null || staticEditorBusy}
          onClick={() => void inspect()}
          type="button"
        >
          {busy === 'load'
            ? t('gameplaySettings.inGamePackage.checking')
            : t('gameplaySettings.refresh')}
        </button>
      </header>

      <div className="in-game-settings-package__instructions" role="note">
        <ShieldCheck aria-hidden="true" size={20} />
        <div>
          <strong>{t('gameplaySettings.inGamePackage.howToTitle')}</strong>
          <p>{t('gameplaySettings.inGamePackage.howToDescription')}</p>
          <p>{t('gameplaySettings.inGamePackage.availableControls')}</p>
          <p>{t('gameplaySettings.inGamePackage.sharedToggleWarning')}</p>
          <p>{t('gameplaySettings.inGamePackage.hardwareValidationPending')}</p>
        </div>
      </div>

      <section
        aria-labelledby="in-game-settings-installation-title"
        className="in-game-settings-package__installation"
      >
        <div className="in-game-settings-package__installation-heading">
          <h4 id="in-game-settings-installation-title">
            {t('gameplaySettings.inGamePackage.installationTitle')}
          </h4>
          <p>{t('gameplaySettings.inGamePackage.installationDescription')}</p>
        </div>
        <div
          aria-label={t('gameplaySettings.inGamePackage.installationTitle')}
          className="in-game-settings-package__target-options"
          role="group"
        >
          {(['atmosphere', 'ryujinx', 'eden'] as const).map((target) => (
            <button
              aria-pressed={installationTarget === target}
              className="in-game-settings-package__target-option"
              key={target}
              onClick={() => setInstallationTarget(target)}
              type="button"
            >
              <span>{t(`gameplaySettings.inGamePackage.target.${target}`)}</span>
              <small
                className={`in-game-settings-package__support in-game-settings-package__support--${target === 'atmosphere' ? 'supported' : 'manual'}`}
              >
                {t(`gameplaySettings.inGamePackage.target.${target}Status`)}
              </small>
            </button>
          ))}
        </div>
        <div
          aria-label={t(`gameplaySettings.inGamePackage.target.${installationTarget}`)}
          className="in-game-settings-package__installation-detail"
          id="in-game-settings-installation-detail"
          role="region"
        >
          <p>
            {t(`gameplaySettings.inGamePackage.target.${installationTarget}Description`)}
          </p>
          <div className="in-game-settings-package__path">
            <span>{t('gameplaySettings.inGamePackage.target.sourcePathLabel')}</span>
            {generatedTitleSourcePaths.map((path) => (
              <code key={path}>{path}</code>
            ))}
            <span>{t('gameplaySettings.inGamePackage.target.settingsPathLabel')}</span>
            <code>{generatedSettingsSourcePath}</code>
            <span>{t('gameplaySettings.inGamePackage.target.destinationPathLabel')}</span>
            {selectedInstallationPaths.titleDestinations.map((path) => (
              <code key={path}>{path}</code>
            ))}
            <span>
              {t('gameplaySettings.inGamePackage.target.destinationSettingsPathLabel')}
            </span>
            <code>{selectedInstallationPaths.settingsDestination}</code>
            {selectedInstallationPaths.defaultSettingsDestination ? (
              <>
                <span>
                  {t('gameplaySettings.inGamePackage.target.defaultSettingsPathLabel')}
                </span>
                <code>{selectedInstallationPaths.defaultSettingsDestination}</code>
              </>
            ) : null}
          </div>
        </div>
      </section>

      <details className="in-game-settings-package__contents">
        <summary>{t('gameplaySettings.inGamePackage.contentsTitle')}</summary>
        <div>
          <p>{t('gameplaySettings.inGamePackage.contentsRuntime')}</p>
          <p>{t('gameplaySettings.inGamePackage.contentsToggles')}</p>
          <p>{t('gameplaySettings.inGamePackage.contentsMetadata')}</p>
          <p>{t('gameplaySettings.inGamePackage.contentsNoDll')}</p>
        </div>
      </details>

      <div className="in-game-settings-package__status" aria-live="polite">
        <div>
          <span>{t('gameplaySettings.inGamePackage.statusLabel')}</span>
          <strong>
            {busy === 'load' && !snapshot
              ? t('gameplaySettings.inGamePackage.checking')
              : snapshot
                ? t(stateTitleKey!)
                : t('gameplaySettings.inGamePackage.state.unavailable.title')}
          </strong>
        </div>
        {snapshot?.availablePackage ? (
          <div>
            <span>{t('gameplaySettings.inGamePackage.availableVersion')}</span>
            <strong>{formatPackageVersion(snapshot.availablePackage.packageVersion)}</strong>
          </div>
        ) : null}
        {snapshot?.installedPackage ? (
          <div>
            <span>{t('gameplaySettings.inGamePackage.installedVersion')}</span>
            <strong>{formatPackageVersion(snapshot.installedPackage.packageVersion)}</strong>
          </div>
        ) : null}
        {compatibilityPackage ? (
          <>
            <div>
              <span>{t('gameplaySettings.inGamePackage.supportedGameVersion')}</span>
              <strong>{compatibilityPackage.supportedGameVersion}</strong>
            </div>
            <div>
              <span>{t('gameplaySettings.inGamePackage.buildId')}</span>
              <strong>
                <code
                  aria-label={`${t('gameplaySettings.inGamePackage.buildId')} ${compatibilityPackage.buildId}`}
                  title={compatibilityPackage.buildId}
                >
                  {compatibilityPackage.buildId.slice(0, 16)}
                </code>
              </strong>
            </div>
          </>
        ) : null}
      </div>

      {snapshot ? (
        <div
          className={`in-game-settings-package__state in-game-settings-package__state--${isPackageStateActionable(snapshot) ? 'ready' : 'blocked'}`}
          role="status"
        >
          <PackageCheck aria-hidden="true" size={20} />
          <div>
            <strong>
              {t(stateTitleKey!)}
            </strong>
            <p>{t(stateDescriptionKey!)}</p>
            {snapshot.detail ? <p>{snapshot.detail}</p> : null}
          </div>
        </div>
      ) : null}

      {snapshot && showExecutableInputAssessment ? (
        <div
          className={`in-game-settings-package__state in-game-settings-package__state--${isExecutableInputCompatible(snapshot) ? 'ready' : 'blocked'}`}
          role="status"
        >
          <ShieldCheck aria-hidden="true" size={20} />
          <div>
            <strong>
              {t(
                `gameplaySettings.inGamePackage.executableInput.${snapshot.executableInput.compatibility}.title`
              )}
            </strong>
            <p>
              {t(
                `gameplaySettings.inGamePackage.executableInput.${snapshot.executableInput.compatibility}.description`
              )}
            </p>
            <p>{t(executableInputReasonMessageKey(snapshot.executableInput.reasonCode))}</p>
            {snapshot.executableInput.sourceRelativePath ? (
              <p>
                <strong>
                  {t('gameplaySettings.inGamePackage.executableInput.sourcePath')}
                </strong>{' '}
                <code>{snapshot.executableInput.sourceRelativePath}</code>
              </p>
            ) : null}
            {snapshot.executableInput.sourceSha256 &&
            snapshot.executableInput.sourceLengthBytes !== null ? (
              <p>
                <strong>
                  {t('gameplaySettings.inGamePackage.executableInput.fingerprint')}
                </strong>{' '}
                <code title={snapshot.executableInput.sourceSha256}>
                  {snapshot.executableInput.sourceSha256.slice(0, 16)}
                </code>{' '}
                <span>
                  {t('gameplaySettings.inGamePackage.executableInput.length', {
                    count: snapshot.executableInput.sourceLengthBytes
                  })}
                </span>
              </p>
            ) : null}
          </div>
        </div>
      ) : null}

      {installBlockedByStaticSettings ? (
        <div className="in-game-settings-package__vanilla-guidance" role="alert">
          <strong>{t('gameplaySettings.inGamePackage.vanillaTitle')}</strong>
          <p>{t('gameplaySettings.inGamePackage.vanillaDescription')}</p>
        </div>
      ) : null}

      {staticEditorBusy && snapshot && isPackageStateActionable(snapshot) ? (
        <p className="in-game-settings-package__message" role="status">
          {t('gameplaySettings.inGamePackage.staticDraftPending')}
        </p>
      ) : null}

      {messageKey ? (
        <p className="in-game-settings-package__message" role="status">
          {t(messageKey)}
        </p>
      ) : null}

      {snapshot && !preview ? (
        <div className="in-game-settings-package__actions">
          {snapshot.state === 'notInstalled' ? (
            <button
              className="primary-button"
              disabled={
                actionsDisabled ||
                !staticSettingsAreVanilla ||
                !isExecutableInputCompatible(snapshot)
              }
              onClick={() => void reviewOperation('install')}
              type="button"
            >
              {busy === 'preview'
                ? t('gameplaySettings.inGamePackage.reviewing')
                : t('gameplaySettings.inGamePackage.reviewInstall')}
            </button>
          ) : null}
          {snapshot.state === 'upgradeAvailable' ? (
            <button
              className="primary-button"
              disabled={actionsDisabled}
              onClick={() => void reviewOperation('upgrade')}
              type="button"
            >
              {busy === 'preview'
                ? t('gameplaySettings.inGamePackage.reviewing')
                : t('gameplaySettings.inGamePackage.reviewUpgrade')}
            </button>
          ) : null}
          {snapshot.state === 'installed' ||
          snapshot.state === 'upgradeAvailable' ||
          snapshot.state === 'coexistenceConflict' ? (
            <button
              className="secondary-button"
              disabled={actionsDisabled}
              onClick={() => void reviewOperation('remove')}
              type="button"
            >
              {busy === 'preview'
                ? t('gameplaySettings.inGamePackage.reviewing')
                : t('gameplaySettings.inGamePackage.reviewRemove')}
            </button>
          ) : null}
        </div>
      ) : null}

      {preview ? (
        <div
          aria-labelledby="in-game-settings-package-review-title"
          className="in-game-settings-package__review"
          role="region"
        >
          <div>
            <h4 id="in-game-settings-package-review-title">
              {t(`gameplaySettings.inGamePackage.review.${preview.operation}.title`)}
            </h4>
            <p>{t(`gameplaySettings.inGamePackage.review.${preview.operation}.description`)}</p>
          </div>
          {preview.composition ? (
            <section className="in-game-settings-package__composition">
              <h5>{t('gameplaySettings.inGamePackage.review.compositionTitle')}</h5>
              <p>
                {t(
                  `gameplaySettings.inGamePackage.review.composition.${preview.composition.strategy}`,
                  { count: preview.composition.ownedRegionCount }
                )}
              </p>
              <dl>
                <div>
                  <dt>{t('gameplaySettings.inGamePackage.review.destination')}</dt>
                  <dd>
                    <code>{preview.composition.destinationRelativePath}</code>
                  </dd>
                </div>
                <div>
                  <dt>{t('gameplaySettings.inGamePackage.review.sourceHandling')}</dt>
                  <dd>
                    {t(
                      preview.composition.sourcePreserved
                        ? 'gameplaySettings.inGamePackage.review.sourcePreserved'
                        : 'gameplaySettings.inGamePackage.review.sourceNotPreserved'
                    )}
                  </dd>
                </div>
                <div>
                  <dt>{t('gameplaySettings.inGamePackage.review.unownedBytes')}</dt>
                  <dd>
                    {t(
                      preview.composition.preservesBytesOutsideOwnedRegions
                        ? 'gameplaySettings.inGamePackage.review.unownedBytesPreserved'
                        : 'gameplaySettings.inGamePackage.review.unownedBytesNotPreserved'
                    )}
                  </dd>
                </div>
              </dl>
            </section>
          ) : null}
          {preview.readDependencies.length > 0 ? (
            <section className="in-game-settings-package__read-dependencies">
              <h5>{t('gameplaySettings.inGamePackage.review.readDependenciesTitle')}</h5>
              <p>{t('gameplaySettings.inGamePackage.review.readDependenciesDescription')}</p>
              <ul className="in-game-settings-package__targets">
                {preview.readDependencies.map((dependency) => (
                  <li key={`${dependency.role}:${dependency.relativePath}`}>
                    <div>
                      <span>
                        {t(
                          `gameplaySettings.inGamePackage.review.readDependency.${dependency.role}`
                        )}
                      </span>
                      <small>
                        {t(
                          dependency.preserved
                            ? 'gameplaySettings.inGamePackage.review.readDependency.preserved'
                            : 'gameplaySettings.inGamePackage.review.readDependency.notPreserved'
                        )}
                      </small>
                    </div>
                    <div>
                      <code>{dependency.relativePath}</code>
                      {dependency.exists &&
                      dependency.sha256 &&
                      dependency.lengthBytes !== null ? (
                        <code title={dependency.sha256}>
                          {dependency.sha256.slice(0, 16)} · {dependency.lengthBytes}
                        </code>
                      ) : (
                        <small>
                          {t(
                            'gameplaySettings.inGamePackage.review.readDependency.expectedMissing'
                          )}
                        </small>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
              {preview.readDependenciesTruncated ? (
                <p>{t('gameplaySettings.inGamePackage.review.readDependenciesTruncated')}</p>
              ) : null}
            </section>
          ) : null}
          <ul className="in-game-settings-package__targets">
            {preview.targets.map((target) => (
              <li key={`${target.operation}:${target.relativePath}`}>
                <span>
                  {t(`gameplaySettings.inGamePackage.target.${target.operation}`)}
                </span>
                <code>{target.relativePath}</code>
              </li>
            ))}
          </ul>
          {preview.targetsTruncated ? (
            <p>{t('gameplaySettings.inGamePackage.targetsTruncated')}</p>
          ) : null}
          <label className="in-game-settings-package__confirmation">
            <input
              checked={reviewAcknowledged}
              disabled={busy !== null}
              onChange={(event) => setReviewAcknowledged(event.currentTarget.checked)}
              type="checkbox"
            />
            <span>
              {t(
                preview.operation === 'install' &&
                  preview.composition?.strategy === 'compatibleStandalone'
                  ? 'gameplaySettings.inGamePackage.confirm.installCompatible'
                  : `gameplaySettings.inGamePackage.confirm.${preview.operation}`
              )}
            </span>
          </label>
          <div className="in-game-settings-package__actions">
            <button
              className="secondary-button"
              disabled={busy !== null}
              onClick={() => {
                setPreview(null);
                setReviewAcknowledged(false);
              }}
              type="button"
            >
              {t('gameplaySettings.cancelReview')}
            </button>
            <button
              className={preview.operation === 'remove' ? 'danger-button' : 'primary-button'}
              disabled={
                busy !== null ||
                staticEditorBusy ||
                !canApply ||
                !reviewAcknowledged ||
                recoveryRequired ||
                isPackageReviewExpired(preview) ||
                (preview.operation === 'install' && !staticSettingsAreVanilla)
              }
              onClick={() => void applyReviewedOperation()}
              type="button"
            >
              {busy === 'apply'
                ? t('gameplaySettings.inGamePackage.applying')
                : t(`gameplaySettings.inGamePackage.apply.${preview.operation}`)}
            </button>
          </div>
        </div>
      ) : null}
    </section>
  );
}

function canReviewOperation(
  snapshot: InGameSettingsPackageSnapshot,
  operation: InGameSettingsPackageOperation
) {
  switch (operation) {
    case 'install':
      return (
        snapshot.state === 'notInstalled' &&
        snapshot.packageAvailable &&
        isExecutableInputCompatible(snapshot)
      );
    case 'upgrade':
      return snapshot.state === 'upgradeAvailable' && snapshot.packageAvailable;
    case 'remove':
      return (
        snapshot.state === 'installed' ||
        snapshot.state === 'upgradeAvailable' ||
        snapshot.state === 'coexistenceConflict'
      );
  }
}

function isPackageStateActionable(snapshot: InGameSettingsPackageSnapshot) {
  return (
    (snapshot.state === 'notInstalled' && isExecutableInputCompatible(snapshot)) ||
    snapshot.state === 'installed' ||
    snapshot.state === 'upgradeAvailable'
  );
}

function packageStateTitleMessageKey(snapshot: InGameSettingsPackageSnapshot) {
  if (
    snapshot.state === 'notInstalled' &&
    snapshot.executableInput.compatibility === 'compatiblePreservable'
  ) {
    return 'gameplaySettings.inGamePackage.state.readyCompatible.title';
  }
  return `gameplaySettings.inGamePackage.state.${snapshot.state}.title`;
}

function packageStateDescriptionMessageKey(snapshot: InGameSettingsPackageSnapshot) {
  if (
    snapshot.state === 'notInstalled' &&
    snapshot.executableInput.compatibility === 'compatiblePreservable'
  ) {
    return 'gameplaySettings.inGamePackage.state.readyCompatible.description';
  }
  return `gameplaySettings.inGamePackage.state.${snapshot.state}.description`;
}

function isExecutableInputCompatible(snapshot: InGameSettingsPackageSnapshot) {
  return (
    snapshot.executableInput.compatibility === 'absent' ||
    snapshot.executableInput.compatibility === 'retailEquivalent' ||
    snapshot.executableInput.compatibility === 'compatiblePreservable'
  );
}

const executableInputReasonMessageKeys: Readonly<Record<string, string>> = {
  'base-source-selected':
    'gameplaySettings.inGamePackage.executableInput.reason.baseSourceSelected',
  'no-standalone-output':
    'gameplaySettings.inGamePackage.executableInput.reason.noStandaloneOutput',
  'owned-regions-conflict':
    'gameplaySettings.inGamePackage.executableInput.reason.ownedRegionsConflict',
  'standalone-compatible':
    'gameplaySettings.inGamePackage.executableInput.reason.standaloneCompatible',
  'standalone-matches-base':
    'gameplaySettings.inGamePackage.executableInput.reason.standaloneMatchesBase',
  'source-review-unavailable':
    'gameplaySettings.inGamePackage.executableInput.reason.sourceReviewUnavailable',
  'unsupported-base-input':
    'gameplaySettings.inGamePackage.executableInput.reason.unsupportedBaseInput',
  'runtime-slot-occupied':
    'gameplaySettings.inGamePackage.executableInput.reason.runtimeSlotOccupied',
  'standalone-output-not-ledger-owned':
    'gameplaySettings.inGamePackage.executableInput.reason.standaloneOutputNotLedgerOwned',
  'verified-native-region-conflict':
    'gameplaySettings.inGamePackage.executableInput.reason.verifiedNativeRegionConflict',
  'ledger-owned-preservable-output':
    'gameplaySettings.inGamePackage.executableInput.reason.ledgerOwnedPreservableOutput',
  'unreadable-or-ambiguous':
    'gameplaySettings.inGamePackage.executableInput.reason.unreadableOrAmbiguous',
  'unsupported-build':
    'gameplaySettings.inGamePackage.executableInput.reason.unsupportedBuild'
};

function executableInputReasonMessageKey(reasonCode: string) {
  return (
    executableInputReasonMessageKeys[reasonCode] ??
    'gameplaySettings.inGamePackage.executableInput.reason.other'
  );
}

function isPackageReviewExpired(
  preview: Pick<PreviewInGameSettingsPackageResponse, 'expiresAtUtc'>,
  now = Date.now()
) {
  const expiresAt = Date.parse(preview.expiresAtUtc);
  return !Number.isFinite(expiresAt) || expiresAt <= now;
}

function formatPackageVersion(version: { major: number; minor: number; patch: number }) {
  return `${version.major}.${version.minor}.${version.patch}`;
}

async function tryArmInGameSettingsPackageWriteGuard(
  armCriticalWriteGuard: () => Promise<boolean>
) {
  try {
    return await armCriticalWriteGuard();
  } catch {
    return false;
  }
}
