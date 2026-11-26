/* SPDX-License-Identifier: GPL-3.0-only */

import { Camera, Cctv, FlaskConical, Pill, ShieldAlert } from 'lucide-react';
import { useMemo, useState } from 'react';
import { CameraHookRequirement, type CameraHookStatus } from '../camera-control/CameraHookRequirement';
import type { OutputSafetyScope } from '../../bridge/outputSafetyContracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { ContextHelp } from '../../components/ContextHelp';
import { useLocalization } from '../../localization';
import './GameplaySettingsSection.css';
import { InGameSettingsPackagePanel, type GameplaySettingsDestinations } from './InGameSettingsPackagePanel';

type GameplaySettingsBridge = Pick<
  ProjectBridge,
  | 'applyInGameSettingsPackage'
  | 'inspectInGameSettingsPackage'
  | 'previewInGameSettingsPackage'
>;

export type GameplaySettingsSectionProps = GameplaySettingsDestinations & {
  cameraControl?: boolean;
  battleItems?: boolean;
  cameraHook?: boolean;
  onOpenCameraHook?: () => void;
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

export function GameplaySettingsSection({
  cameraControl = false,
  battleItems = false,
  cameraHook = false,
  onOpenCameraHook,
  armCriticalWriteGuard,
  destinations,
  onRememberDestination,
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
  const HeadingIcon = cameraHook ? Cctv : battleItems ? Pill : cameraControl ? Camera : FlaskConical;
  const [hookStatus, setHookStatus] = useState<CameraHookStatus | null>(null);
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

  const sharedOutput = !cameraHook && (stableScope?.paths.selectedGame === 'sword' || stableScope?.paths.selectedGame === 'shield');
  const hookReady = hookStatus?.scopeKey === JSON.stringify(stableScope) && hookStatus.ready;

  return (
    <section
      aria-labelledby="gameplay-settings-title"
      className="panel wide-panel gameplay-settings"
    >
      <header className="gameplay-settings__header">
        <div className="gameplay-settings__heading">
          <HeadingIcon aria-hidden="true" size={20} />
          <div>
            <div className="gameplay-settings__title-row">
              <h2 id="gameplay-settings-title">{t(cameraHook ? 'cameraHook.title' : battleItems ? 'battleItems.title' : cameraControl ? 'cameraControl.title' : 'gameplaySettings.title')}</h2>
              {!cameraHook ? <span className="gameplay-settings__beta-badge">{t('gameplaySettings.betaBadge')}</span> : null}
            </div>

          </div>
        </div>
        {cameraHook ? <ContextHelp label={t('cameraHook.title')}>{t('cameraHook.instructions')}</ContextHelp> : null}
      </header>

      {cameraControl || battleItems ? <details className="advanced-editor-details camera-control__overview">
        <summary>{t(cameraControl ? 'cameraControl.title' : 'battleItems.title')}</summary>
        <div className="advanced-editor-details-content">
          <p>{t(cameraControl ? 'cameraControl.instructions' : 'battleItems.instructions')}</p>
          <div className="camera-control__choices">
            <div><h3>{t('cameraControl.yes')}</h3><p>{t(cameraControl ? 'cameraControl.yesDescription' : 'battleItems.yesDescription')}</p></div>
            <div><h3>{t('cameraControl.no')}</h3><p>{t(cameraControl ? 'cameraControl.noDescription' : 'battleItems.noDescription')}</p></div>
          </div>
          <p>{t(cameraControl ? 'cameraControl.scenes' : 'battleItems.scope')}</p>
          <p className="field-note">{t(cameraControl ? 'cameraControl.sharedPackage' : 'battleItems.optionsHelp')}</p>
        </div>
      </details> : null}

      {!cameraHook ? <div className="gameplay-settings__beta-notice" role="note">
        <ShieldAlert aria-hidden="true" size={20} />
        <div>
          <p>{t('gameplaySettings.destination.beta')}</p>
        </div>
      </div> : null}

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
      ) : (
        <>
        {sharedOutput && !battleItems ? <CameraHookRequirement key={JSON.stringify(stableScope)} scope={stableScope} bridge={bridge}
          domain={battleItems ? 'workflow.battleItems' : cameraControl ? 'workflow.cameraControl' : 'workflow.gameplaySettings'}
          destinations={destinations} onStatus={setHookStatus} onOpenCameraHook={onOpenCameraHook} /> : null}
        <InGameSettingsPackagePanel
          cameraControl={cameraControl}
          battleItems={battleItems}
          purpose={cameraHook ? 'hook' : sharedOutput ? 'output' : 'combined'}
          canPrepare={!sharedOutput || battleItems || Boolean(hookReady)}
          requiredCameraHook={sharedOutput && !battleItems && hookReady ? hookStatus?.requirement : undefined}
          armCriticalWriteGuard={armCriticalWriteGuard}
          destinations={destinations}
          onRememberDestination={onRememberDestination}
          bridge={bridge}
          canApply={canApply}
          onApplied={onApplied}
          onApplyBusyChange={onApplyBusyChange}
          onDirtyChange={onDirtyChange}
          onError={onError}
          onRecoveryRequired={onRecoveryRequired}
          scope={stableScope}
        />
        </>
      )}
    </section>
  );
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
