/* SPDX-License-Identifier: GPL-3.0-only */

export const userFacingFeatureVisibility = {
  namedChangeSets: false
} as const;

export type UserFacingFeature = keyof typeof userFacingFeatureVisibility;

export function isUserFacingFeatureVisible(feature: UserFacingFeature) {
  return userFacingFeatureVisibility[feature];
}
