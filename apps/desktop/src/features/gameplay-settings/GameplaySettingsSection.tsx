/* SPDX-License-Identifier: GPL-3.0-only */

import { FlaskConical, ShieldAlert } from 'lucide-react';
import { useMemo } from 'react';
import type { OutputSafetyScope } from '../../bridge/outputSafetyContracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { useLocalization } from '../../localization';
import './GameplaySettingsSection.css';
import { InGameSettingsPackagePanel } from './InGameSettingsPackagePanel';

type GameplaySettingsBridge = Pick<
  ProjectBridge,
  | 'applyInGameSettingsPackage'
  | 'inspectInGameSettingsPackage'
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
            <p>{t('gameplaySettings.inGamePackage.description')}</p>
          </div>
        </div>
      </header>

      <div className="gameplay-settings__beta-notice" role="note">
        <ShieldAlert aria-hidden="true" size={20} />
        <div>
          <strong>{t('gameplaySettings.betaNoticeTitle')}</strong>
          <p>{t('gameplaySettings.inGamePackage.hardwareValidationPending')}</p>
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
      ) : (
        <InGameSettingsPackagePanel
          armCriticalWriteGuard={armCriticalWriteGuard}
          bridge={bridge}
          canApply={canApply}
          onApplied={onApplied}
          onApplyBusyChange={onApplyBusyChange}
          onDirtyChange={onDirtyChange}
          onError={onError}
          onRecoveryRequired={onRecoveryRequired}
          scope={stableScope}
        />
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
