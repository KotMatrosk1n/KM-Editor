/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ResearchAnnotationTargetKind,
  ResearchConfidence,
  ResearchCoverageState,
  ResearchFeature,
  ResearchFileDifferenceKind,
  ResearchRangeCoverage
} from '../../bridge/researchLabContracts';
import type { ResearchLabError } from './useResearchLabController';

export function researchFeatureKey(feature: ResearchFeature) {
  return `researchLab.feature.${feature}`;
}

export function researchFeatureDescriptionKey(feature: ResearchFeature) {
  return `researchLab.feature.${feature}.description`;
}

export function researchCoverageKey(coverage: ResearchCoverageState) {
  return `researchLab.coverage.${coverage}`;
}

export function researchConfidenceKey(confidence: ResearchConfidence) {
  return `researchLab.confidence.${confidence}`;
}

export function researchDifferenceKey(kind: ResearchFileDifferenceKind) {
  return `researchLab.difference.${kind}`;
}

export function researchRangeCoverageKey(coverage: ResearchRangeCoverage) {
  return `researchLab.rangeCoverage.${coverage}`;
}

export function researchTargetKindKey(kind: ResearchAnnotationTargetKind) {
  return `researchLab.annotations.target.${kind}`;
}

export function researchErrorKey(error: ResearchLabError | null) {
  return `researchLab.error.${error ?? 'generic'}`;
}

const reasonKeys: Readonly<Record<string, string>> = {
  'comparison-target-creation-only': 'researchLab.reason.comparisonTargetCreationOnly',
  'host-registered-descriptors-only': 'researchLab.reason.hostRegisteredDescriptorsOnly',
  'opaque-file-ownership-provider-unavailable':
    'researchLab.reason.opaqueFileOwnershipUnavailable',
  'selected-dump-semantic-provider-unavailable':
    'researchLab.reason.selectedDumpSemanticProviderUnavailable',
  'writable-extensions-not-supported': 'researchLab.reason.writableExtensionsNotSupported'
};

export function researchReasonKey(reasonCode: string | null) {
  return reasonCode
    ? reasonKeys[reasonCode] ?? 'researchLab.reason.unavailable'
    : null;
}
