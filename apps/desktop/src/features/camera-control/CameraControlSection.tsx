/* SPDX-License-Identifier: GPL-3.0-only */

import { GameplaySettingsSection, type GameplaySettingsSectionProps } from '../gameplay-settings/GameplaySettingsSection';
import './CameraControlSection.css';

export function CameraControlSection(props: Omit<GameplaySettingsSectionProps, 'cameraControl'>) {
  return <div className="wide-panel camera-control"><GameplaySettingsSection {...props} cameraControl /></div>;
}
