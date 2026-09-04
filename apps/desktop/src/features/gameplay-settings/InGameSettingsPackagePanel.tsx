/* SPDX-License-Identifier: GPL-3.0-only */

import { Gamepad2, PackageCheck, ShieldCheck } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type {
  InGameSettingsInstallationTarget,
  InGameSettingsPackageOperation,
  InGameSettingsPackageSnapshot,
  PreviewInGameSettingsPackageResponse
} from '../../bridge/inGameSettingsPackageContracts';
import type { OutputSafetyScope } from '../../bridge/outputSafetyContracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
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
  scope: OutputSafetyScope;
};

type PackageBusyState = 'apply' | 'load' | 'preview' | null;

export function InGameSettingsPackagePanel({
  armCriticalWriteGuard,
  bridge,
  canApply,
  onApplied,
  onApplyBusyChange,
  onDirtyChange,
  onError,
  onRecoveryRequired,
  scope
}: InGameSettingsPackagePanelProps) {
  const { t } = useLocalization();
  const [snapshot, setSnapshot] = useState<InGameSettingsPackageSnapshot | null>(null);
  const [preview, setPreview] = useState<PreviewInGameSettingsPackageResponse | null>(null);
  const [reviewAcknowledged, setReviewAcknowledged] = useState(false);
  const [busy, setBusy] = useState<PackageBusyState>(null);
  const [messageKey, setMessageKey] = useState<string | null>(null);
  const [recoveryRequired, setRecoveryRequired] = useState(false);
  const errorMessage = messageKey && !messageKey.endsWith('Committed')
    ? t(messageKey)
    : null;
  usePublishCommonEditorError({
    domain: 'workflow.gameplaySettings',
    field: 'inGamePackage',
    message: errorMessage
  });
  const [installationTarget, setInstallationTarget] =
    useState<InGameSettingsInstallationTarget>('atmosphere');
  const requestGenerationRef = useRef(0);
  const requestOperationRef = useRef<object | null>(null);
  const applyGenerationRef = useRef(0);
  const applyInFlightGenerationRef = useRef<number | null>(null);
  const isMountedRef = useRef(true);
  const applyBusyReportedRef = useRef(false);
  const reviewRegionRef = useRef<HTMLDivElement | null>(null);

  const scopeKey = useMemo(() => JSON.stringify(scope), [scope]);
  const requestContextKey = `${scopeKey}:${installationTarget}`;
  const requestContextKeyRef = useRef(requestContextKey);
  requestContextKeyRef.current = requestContextKey;

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
      requestOperationRef.current = null;
      applyGenerationRef.current += 1;
      applyInFlightGenerationRef.current = null;
      reportApplyBusy(false);
    };
  }, [reportApplyBusy]);

  useEffect(() => {
    onDirtyChange?.(preview !== null);
    return () => onDirtyChange?.(false);
  }, [onDirtyChange, preview]);

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

  const selectInstallationTarget = useCallback(
    (target: InGameSettingsInstallationTarget) => {
      if (
        target === installationTarget ||
        busy === 'apply' ||
        requestOperationRef.current !== null ||
        applyInFlightGenerationRef.current !== null
      ) return;
      requestGenerationRef.current += 1;
      setInstallationTarget(target);
      setSnapshot(null);
      setPreview(null);
      setReviewAcknowledged(false);
      setRecoveryRequired(false);
      setMessageKey(null);
      setBusy(null);
    },
    [busy, installationTarget]
  );

  const inspect = useCallback(
    async (showBusy = true): Promise<InGameSettingsPackageSnapshot | null> => {
      if (
        requestOperationRef.current !== null ||
        (showBusy && applyInFlightGenerationRef.current !== null)
      ) {
        return null;
      }
      const operation = {};
      requestOperationRef.current = operation;
      const generation = ++requestGenerationRef.current;
      const requestedContextKey = requestContextKey;
      if (showBusy) {
        setBusy('load');
      }
      setPreview(null);
      setReviewAcknowledged(false);
      setRecoveryRequired(false);
      setMessageKey(null);
      try {
        const response = await bridge.inspectInGameSettingsPackage({
          installationTarget,
          scope
        });
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          requestContextKeyRef.current !== requestedContextKey
        ) {
          return null;
        }
        setSnapshot(response.snapshot);
        return response.snapshot;
      } catch (error) {
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          requestContextKeyRef.current !== requestedContextKey
        ) {
          return null;
        }
        setSnapshot(null);
        await reportError(error, 'load');
        return null;
      } finally {
        if (requestOperationRef.current === operation) {
          requestOperationRef.current = null;
        }
        if (
          showBusy &&
          isMountedRef.current &&
          requestGenerationRef.current === generation &&
          requestContextKeyRef.current === requestedContextKey
        ) {
          setBusy(null);
        }
      }
    },
    [bridge, installationTarget, reportError, requestContextKey, scope]
  );
  const inspectRef = useRef(inspect);
  inspectRef.current = inspect;

  useEffect(() => {
    if (applyInFlightGenerationRef.current !== null) {
      return;
    }
    requestGenerationRef.current += 1;
    requestOperationRef.current = null;
    applyGenerationRef.current += 1;
    applyInFlightGenerationRef.current = null;
    reportApplyBusy(false);
    setSnapshot(null);
    setPreview(null);
    setReviewAcknowledged(false);
    setRecoveryRequired(false);
    setMessageKey(null);
    void inspect();
  }, [inspect, reportApplyBusy, requestContextKey]);

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

  useEffect(() => {
    if (!preview) return;
    const animationFrame = window.requestAnimationFrame(() => {
      reviewRegionRef.current?.focus();
    });
    return () => window.cancelAnimationFrame(animationFrame);
  }, [preview]);

  const reviewOperation = useCallback(
    async (operation: InGameSettingsPackageOperation) => {
      if (
        !snapshot ||
        busy !== null ||
        requestOperationRef.current !== null ||
        applyInFlightGenerationRef.current !== null ||
        !canApply ||
        recoveryRequired ||
        !canReviewOperation(snapshot, operation)
      ) {
        return;
      }

      const requestOperation = {};
      requestOperationRef.current = requestOperation;
      const generation = ++requestGenerationRef.current;
      const requestedContextKey = requestContextKey;
      setBusy('preview');
      setPreview(null);
      setReviewAcknowledged(false);
      setMessageKey(null);
      try {
        const response = await bridge.previewInGameSettingsPackage({
          expectedRevision: snapshot.revision,
          installationTarget,
          operation,
          scope
        });
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          requestContextKeyRef.current !== requestedContextKey
        ) {
          return;
        }
        setPreview(response);
      } catch (error) {
        if (
          !isMountedRef.current ||
          requestGenerationRef.current !== generation ||
          requestContextKeyRef.current !== requestedContextKey
        ) {
          return;
        }
        await reportError(error, 'preview');
      } finally {
        if (requestOperationRef.current === requestOperation) {
          requestOperationRef.current = null;
        }
        if (
          isMountedRef.current &&
          requestGenerationRef.current === generation &&
          requestContextKeyRef.current === requestedContextKey
        ) {
          setBusy(null);
        }
      }
    },
    [
      bridge,
      busy,
      canApply,
      installationTarget,
      recoveryRequired,
      reportError,
      requestContextKey,
      scope,
      snapshot
    ]
  );

  const applyReviewedOperation = useCallback(async () => {
    if (
      !preview ||
      !reviewAcknowledged ||
      busy !== null ||
      applyInFlightGenerationRef.current !== null ||
      !canApply ||
      recoveryRequired ||
      isPackageReviewExpired(preview)
    ) {
      return;
    }

    const generation = ++applyGenerationRef.current;
    applyInFlightGenerationRef.current = generation;
    const requestedContextKey = requestContextKey;
    setBusy('apply');
    setMessageKey(null);
    reportApplyBusy(true);
    try {
      const armed = await tryArmInGameSettingsPackageWriteGuard(armCriticalWriteGuard);
      if (
        !armed ||
        !isMountedRef.current ||
        applyGenerationRef.current !== generation ||
        requestContextKeyRef.current !== requestedContextKey
      ) {
        if (
          isMountedRef.current &&
          applyGenerationRef.current === generation &&
          requestContextKeyRef.current === requestedContextKey
        ) {
          setMessageKey('gameplaySettings.inGamePackage.guardFailed');
        }
        return;
      }
      if (isPackageReviewExpired(preview)) {
        setPreview(null);
        setReviewAcknowledged(false);
        setMessageKey('gameplaySettings.inGamePackage.reviewExpired');
        return;
      }

      const response = await bridge.applyInGameSettingsPackage({
        installationTarget,
        reviewId: preview.reviewId,
        scope
      });
      if (
        !isMountedRef.current ||
        applyGenerationRef.current !== generation ||
        requestContextKeyRef.current !== requestedContextKey
      ) {
        return;
      }

      setPreview(null);
      setReviewAcknowledged(false);
      if (response.outcome === 'committed') {
        const committedOperation = packageReviewPresentationOperation(preview);
        if (response.snapshot) {
          setSnapshot(response.snapshot);
        } else {
          await inspect(false);
        }
        if (
          !isMountedRef.current ||
          applyGenerationRef.current !== generation ||
          requestContextKeyRef.current !== requestedContextKey
        ) {
          return;
        }
        setMessageKey(
          `gameplaySettings.inGamePackage.${committedOperation}Committed`
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
            `gameplaySettings.inGamePackage.${committedOperation}Committed`
          );
        }
      } else if (response.outcome === 'rolledBack') {
        await inspect(false);
        if (
          isMountedRef.current &&
          applyGenerationRef.current === generation &&
          requestContextKeyRef.current === requestedContextKey
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
        requestContextKeyRef.current !== requestedContextKey
      ) {
        return;
      }
      setPreview(null);
      setReviewAcknowledged(false);
      await reportError(error, 'apply');
    } finally {
      if (applyInFlightGenerationRef.current === generation) {
        applyInFlightGenerationRef.current = null;
        reportApplyBusy(false);
      }
      const contextChanged = requestContextKeyRef.current !== requestedContextKey;
      if (
        isMountedRef.current &&
        applyGenerationRef.current === generation &&
        !contextChanged
      ) {
        setBusy(null);
      } else if (isMountedRef.current && contextChanged) {
        requestGenerationRef.current += 1;
        setBusy(null);
        setSnapshot(null);
        setPreview(null);
        setReviewAcknowledged(false);
        setRecoveryRequired(false);
        setMessageKey(null);
        void inspectRef.current();
      }
    }
  }, [
    armCriticalWriteGuard,
    bridge,
    busy,
    canApply,
    installationTarget,
    inspect,
    onApplied,
    onRecoveryRequired,
    preview,
    recoveryRequired,
    reportApplyBusy,
    reportError,
    requestContextKey,
    reviewAcknowledged,
    scope
  ]);

  const actionsDisabled = busy !== null || !canApply || recoveryRequired;
  const compatibilityPackage = snapshot?.availablePackage ?? snapshot?.installedPackage ?? null;
  const titleId = compatibilityPackage?.titleId ?? '<TITLE_ID>';
  const generatedTitleRoots: Record<InGameSettingsInstallationTarget, string> = {
    atmosphere: `atmosphere/contents/${titleId}`,
    ryujinx: `mods/contents/${titleId}/KM-Gameplay-Settings`,
    eden: `load/${titleId}/KM-Gameplay-Settings`
  };
  const generatedTitleRoot = generatedTitleRoots[installationTarget];
  const generatedTitleSourcePaths = [
    `<Output Root>/${generatedTitleRoot}/exefs`,
    `<Output Root>/${generatedTitleRoot}/romfs`
  ];
  const settingsJournalPath = `config/km-editor/gameplay-settings/${titleId}/settings.bin`;
  const generatedSettingsSourcePath = `<Output Root>/${settingsJournalPath}`;
  const installationPaths: Record<
    InGameSettingsInstallationTarget,
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
        `<Emulator data folder>/mods/contents/${titleId}/KM-Gameplay-Settings/exefs`,
        `<Emulator data folder>/mods/contents/${titleId}/KM-Gameplay-Settings/romfs`
      ]
    },
    eden: {
      settingsDestination: `<Configured emulated SD root>/${settingsJournalPath}`,
      defaultSettingsDestination: `<Eden data folder>/sdmc/${settingsJournalPath}`,
      titleDestinations: [
        `<Eden data folder>/load/${titleId}/KM-Gameplay-Settings/exefs`,
        `<Eden data folder>/load/${titleId}/KM-Gameplay-Settings/romfs`
      ]
    }
  };
  const selectedInstallationPaths = installationPaths[installationTarget];
  const stateTitleKey = snapshot ? packageStateTitleMessageKey(snapshot) : null;
  const stateDescriptionKey = snapshot
    ? packageStateDescriptionMessageKey(snapshot)
    : null;
  const refreshRequired = snapshot ? isPackageRefreshRequired(snapshot) : false;
  const reviewPresentationOperation = preview
    ? packageReviewPresentationOperation(preview)
    : null;
  const showExecutableInputAssessment = Boolean(
    snapshot &&
      (snapshot.executableInput.source === 'standaloneOutput' ||
        snapshot.executableInput.compatibility !== 'absent')
  );
  const installationTargetSelectionBusy = busy !== null;

  return (
    <section
      aria-busy={busy !== null || undefined}
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
          disabled={busy !== null || preview !== null}
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
          aria-busy={installationTargetSelectionBusy || undefined}
          aria-label={t('gameplaySettings.inGamePackage.installationTitle')}
          className="in-game-settings-package__target-options"
          role="group"
        >
          {(['atmosphere', 'ryujinx', 'eden'] as const).map((target) => (
            <button
              aria-controls="in-game-settings-installation-detail"
              aria-pressed={installationTarget === target}
              className="in-game-settings-package__target-option"
              disabled={installationTargetSelectionBusy}
              key={target}
              onClick={() => selectInstallationTarget(target)}
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
                actionsDisabled || !isExecutableInputCompatible(snapshot)
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
                : t(
                    refreshRequired
                      ? 'gameplaySettings.inGamePackage.reviewRefresh'
                      : 'gameplaySettings.inGamePackage.reviewUpgrade'
                  )}
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
          aria-live="polite"
          className="in-game-settings-package__review"
          ref={reviewRegionRef}
          role="region"
          tabIndex={-1}
        >
          <div>
            <h4 id="in-game-settings-package-review-title">
              {t(`gameplaySettings.inGamePackage.review.${reviewPresentationOperation}.title`)}
            </h4>
            <p>
              {t(
                `gameplaySettings.inGamePackage.review.${reviewPresentationOperation}.description`
              )}
            </p>
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
              id="in-game-settings-review-confirmation"
              onChange={(event) => setReviewAcknowledged(event.currentTarget.checked)}
              type="checkbox"
            />
            <span>
              {t(
                preview.operation === 'install' &&
                  preview.composition?.strategy === 'compatibleStandalone'
                  ? 'gameplaySettings.inGamePackage.confirm.installCompatible'
                  : `gameplaySettings.inGamePackage.confirm.${reviewPresentationOperation}`
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
                !canApply ||
                !reviewAcknowledged ||
                recoveryRequired ||
                isPackageReviewExpired(preview)
              }
              onClick={() => void applyReviewedOperation()}
              type="button"
            >
              {busy === 'apply'
                ? t('gameplaySettings.inGamePackage.applying')
                : t(
                    `gameplaySettings.inGamePackage.apply.${reviewPresentationOperation}`
                  )}
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
  if (isPackageRefreshRequired(snapshot)) {
    return 'gameplaySettings.inGamePackage.state.refreshRequired.title';
  }
  if (snapshot.state === 'unavailable') {
    switch (snapshot.executableInput.compatibility) {
      case 'ownershipUnverified':
        return 'gameplaySettings.inGamePackage.state.ownershipUnverified.title';
      case 'incompatibleOwnedRegion':
        return 'gameplaySettings.inGamePackage.state.executableConflict.title';
      case 'unreadableOrAmbiguous':
        return 'gameplaySettings.inGamePackage.state.executableReviewFailed.title';
    }
  }
  if (
    snapshot.state === 'notInstalled' &&
    snapshot.executableInput.compatibility === 'compatiblePreservable'
  ) {
    return 'gameplaySettings.inGamePackage.state.readyCompatible.title';
  }
  return `gameplaySettings.inGamePackage.state.${snapshot.state}.title`;
}

function packageStateDescriptionMessageKey(snapshot: InGameSettingsPackageSnapshot) {
  if (isPackageRefreshRequired(snapshot)) {
    return 'gameplaySettings.inGamePackage.state.refreshRequired.description';
  }
  if (snapshot.state === 'unavailable') {
    switch (snapshot.executableInput.compatibility) {
      case 'ownershipUnverified':
        return 'gameplaySettings.inGamePackage.state.ownershipUnverified.description';
      case 'incompatibleOwnedRegion':
        return 'gameplaySettings.inGamePackage.state.executableConflict.description';
      case 'unreadableOrAmbiguous':
        return 'gameplaySettings.inGamePackage.state.executableReviewFailed.description';
    }
  }
  if (
    snapshot.state === 'notInstalled' &&
    snapshot.executableInput.compatibility === 'compatiblePreservable'
  ) {
    return 'gameplaySettings.inGamePackage.state.readyCompatible.description';
  }
  return `gameplaySettings.inGamePackage.state.${snapshot.state}.description`;
}

function isPackageRefreshRequired(snapshot: InGameSettingsPackageSnapshot) {
  return (
    snapshot.state === 'upgradeAvailable' &&
    snapshot.installedPackage !== null &&
    snapshot.availablePackage !== null &&
    packageVersionsEqual(
      snapshot.installedPackage.packageVersion,
      snapshot.availablePackage.packageVersion
    ) &&
    snapshot.installedPackage.bundleId !== snapshot.availablePackage.bundleId
  );
}

function packageReviewPresentationOperation(
  preview: PreviewInGameSettingsPackageResponse
): InGameSettingsPackageOperation | 'refresh' {
  return preview.operation === 'upgrade' && isPackageRefreshRequired(preview.before)
    ? 'refresh'
    : preview.operation;
}

function packageVersionsEqual(
  left: { major: number; minor: number; patch: number },
  right: { major: number; minor: number; patch: number }
) {
  return (
    left.major === right.major &&
    left.minor === right.minor &&
    left.patch === right.patch
  );
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
  'source-inventory-inconsistent':
    'gameplaySettings.inGamePackage.executableInput.reason.sourceInventoryInconsistent',
  'source-ledger-stale-or-inconsistent':
    'gameplaySettings.inGamePackage.executableInput.reason.sourceLedgerStaleOrInconsistent',
  'source-ledger-preservation-contract-invalid':
    'gameplaySettings.inGamePackage.executableInput.reason.sourceLedgerPreservationContractInvalid',
  'standalone-output-not-ledger-owned':
    'gameplaySettings.inGamePackage.executableInput.reason.standaloneOutputNotLedgerOwned',
  'recognized-km-output-without-ledger':
    'gameplaySettings.inGamePackage.executableInput.reason.registeredKmExeFsOutput',
  'registered-km-exefs-output':
    'gameplaySettings.inGamePackage.executableInput.reason.registeredKmExeFsOutput',
  'registered-compatible-exefs-output':
    'gameplaySettings.inGamePackage.executableInput.reason.registeredKmExeFsOutput',
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
