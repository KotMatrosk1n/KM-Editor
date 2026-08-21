/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  semanticExploreGameFamilySchema,
  semanticExploreRecordRefSchema,
  semanticExploreRevisionSchema,
  semanticExploreScopeSchema,
  semanticExploreSourceSnapshotSchema
} from './semanticExploreContracts';

export const researchLabSchemaVersion = 1 as const;
export const researchLabMaximumRegistrations = 4;
export const researchLabRequiredComparisonSources = 2;
export const researchLabRegistrationLifetimeMinutes = 30;
export const researchLabMaximumFileBytes = 64 * 1024 * 1024;
export const researchLabMaximumAggregateBytes = 512 * 1024 * 1024;
export const researchLabMaximumEntries = 200_000;
export const researchLabMaximumDirectories = 50_000;
export const researchLabMaximumTraversalDepth = 128;
export const researchLabMaximumSelectedFiles = 128;
export const researchLabMaximumRangesPerFile = 4_096;
export const researchLabMaximumAggregateRanges = 50_000;
export const researchLabMaximumPageSize = 100;
export const researchLabDefaultPageSize = 50;
export const researchLabMaximumCursorLength = 2_048;
export const researchLabMaximumByteWindowLength = 4_096;
export const researchLabResultProvisionMultiplier = 4;
export const researchLabResultCacheCeilingMultiplier = 2;
export const researchLabExpectedResultSizeBytes = 32 * 1024 * 1024;
export const researchLabMaximumResultSizeBytes =
  researchLabExpectedResultSizeBytes * researchLabResultProvisionMultiplier;
export const researchLabMaximumResultCeilingBytes =
  researchLabMaximumResultSizeBytes * researchLabResultCacheCeilingMultiplier;
export const researchLabMaximumResultCacheBytes = researchLabMaximumResultCeilingBytes;
export const researchLabMaximumRelativePathLength = 4_096;
export const researchLabMaximumAnnotationCount = 2_048;
export const researchLabMaximumAnnotationTextLength = 8_192;
export const researchLabMaximumAnnotationTags = 32;
export const researchLabExpectedSerializedAnnotationDocumentBytes = 3 * 1024 * 1024;
export const researchLabProvisionedSerializedAnnotationDocumentBytes =
  researchLabExpectedSerializedAnnotationDocumentBytes * 4;
export const researchLabMaximumSerializedAnnotationDocumentBytes =
  researchLabProvisionedSerializedAnnotationDocumentBytes * 2;
export const researchLabMaximumExtensionDescriptors = 64;
export const researchLabMaximumAccumulatedFindings = 500;

const unsafeTextPattern =
  /[\p{Cs}\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/u;
const strictUnsafeTextPattern =
  /[\p{Cs}\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/u;
const windowsReservedDeviceAlias =
  /^(?:CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|(?:COM|LPT)[1-9¹²³])(?:\.|$)/iu;
const fingerprintSchema = z.string().regex(/^[a-f0-9]{64}$/u);
const etagSchema = z.string().regex(/^[A-Fa-f0-9]{64}$/u);
const dateTimeOffsetSchema = z.string().refine(
  (value) => (
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) &&
    Number.isFinite(Date.parse(value))
  ),
  { message: 'Expected an ISO 8601 timestamp with an offset.' }
);
const contractKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u);
const extensionIdSchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$/u);
const sourceIdSchema = z.string().regex(/^source-[a-f0-9]{24}$/u);
const comparisonIdSchema = z.string().regex(/^comparison-[a-f0-9]{24}$/u);
const findingIdSchema = z.string().regex(/^finding-[a-f0-9]{24}$/u);
const annotationIdSchema = z.string().regex(/^annotation-[a-f0-9]{24}$/u);
const cursorSchema = z
  .string()
  .min(1)
  .max(researchLabMaximumCursorLength)
  .regex(/^[A-Za-z0-9_-]+$/u);
const localRootPathSchema = z
  .string()
  .min(1)
  .max(researchLabMaximumRelativePathLength)
  .refine((value) => value.trim() === value && !strictUnsafeTextPattern.test(value))
  .refine((value) => (
    /^[A-Za-z]:[\\/]/u.test(value) ||
    /^\\\\[^\\]/u.test(value) ||
    /^\/(?:$|[^/])/u.test(value)
  ));
const relativePathSchema = z
  .string()
  .min(1)
  .max(researchLabMaximumRelativePathLength)
  .refine((value) => value.trim() === value && value.normalize('NFC') === value)
  .refine((value) => (
    !value.startsWith('/') &&
    !value.includes('\\') &&
    !/[":<>|?*\p{Cc}\p{Cs}\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/u.test(value) &&
    value.split('/').every((segment) => (
      segment.length > 0 &&
      segment.trim().length > 0 &&
      segment.length <= 255 &&
      segment !== '.' &&
      segment !== '..' &&
      !segment.endsWith('.') &&
      !segment.endsWith(' ') &&
      researchPortableCaseFold(segment) !== '.km' &&
      !windowsReservedDeviceAlias.test(segment)
    ))
  ));
const nonnegativeOffsetSchema = z
  .number()
  .int()
  .min(0)
  .max(researchLabMaximumFileBytes);
const fileLengthSchema = z
  .number()
  .int()
  .min(0)
  .max(researchLabMaximumFileBytes);
const annotationTextSchema = z
  .string()
  .min(1)
  .max(researchLabMaximumAnnotationTextLength)
  .refine((value) => (
    value.trim() === value &&
    value.normalize('NFC') === value &&
    !unsafeTextPattern.test(value) &&
    !containsResearchLocalPathSignature(value)
  ));
const annotationTagSchema = z
  .string()
  .min(1)
  .max(128)
  .refine((value) => (
    value.trim() === value &&
    value.normalize('NFC') === value &&
    !value.includes(',') &&
    !strictUnsafeTextPattern.test(value) &&
    !containsResearchLocalPathSignature(value)
  ));

export const researchFeatureValues = [
  'sourceComparison',
  'byteWindows',
  'semanticProjection',
  'annotations',
  'ownershipEvidence',
  'readOnlyExtensions',
  'writableExtensions'
] as const;

export const researchFeatureSchema = z.enum(researchFeatureValues);
const researchExtensionFeatureValues = [
  'sourceComparison',
  'byteWindows',
  'semanticProjection',
  'ownershipEvidence'
] as const;
const researchExtensionFeatureSchema = z.enum(researchExtensionFeatureValues);
export const researchExtensionKindSchema = z.enum(['hostRegistered', 'declarativeData']);
export const researchFileDifferenceKindSchema = z.enum(['added', 'removed', 'changed']);
export const researchRangeCoverageSchema = z.enum(['notRequested', 'complete', 'truncated']);
export const researchAnnotationTargetKindSchema = z.enum([
  'semanticRecord',
  'relativeRange',
  'finding'
]);
export const researchAnnotationMutationKindSchema = z.enum(['upsert', 'delete']);
export const researchCoverageStateSchema = z.enum(['complete', 'partial', 'unavailable']);
export const researchConfidenceSchema = z.enum(['verified', 'derived', 'unknown']);

export const researchCapabilitySchema = z
  .strictObject({
    canUse: z.boolean(),
    confidence: researchConfidenceSchema,
    coverage: researchCoverageStateSchema,
    feature: researchFeatureSchema,
    reasonCode: contractKeySchema.nullable()
  })
  .superRefine((capability, context) => {
    if (
      capability.canUse === (capability.coverage === 'unavailable') ||
      capability.coverage === 'unavailable' && capability.confidence !== 'unknown' ||
      capability.coverage === 'unavailable' && capability.reasonCode === null ||
      capability.coverage === 'complete' && capability.reasonCode !== null ||
      capability.feature === 'writableExtensions' && capability.canUse
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The research capability boundary is inconsistent.'
      });
    }
  });

export const researchExtensionDescriptorSchema = z
  .strictObject({
    confidence: researchConfidenceSchema,
    coverage: researchCoverageStateSchema,
    extensionId: extensionIdSchema,
    features: z.array(researchExtensionFeatureSchema).min(1).max(16),
    gameFamilies: z.array(semanticExploreGameFamilySchema).min(1).max(3),
    kind: researchExtensionKindSchema,
    reasonCode: contractKeySchema.nullable(),
    schemaVersion: z.number().int().positive()
  })
  .superRefine((extension, context) => {
    if (
      !isStrictlyOrdinalSorted(extension.features) ||
      !isStrictlyOrdinalSorted(extension.gameFamilies) ||
      extension.confidence !== (extension.coverage === 'unavailable' ? 'unknown' : 'verified') ||
      (extension.coverage === 'complete') !== (extension.reasonCode === null)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The read-only research extension descriptor is inconsistent.'
      });
    }
  });

export const researchLimitsSchema = z.strictObject({
  maximumAggregateBytes: z.literal(researchLabMaximumAggregateBytes),
  maximumAggregateRanges: z.literal(researchLabMaximumAggregateRanges),
  maximumByteWindowLength: z.literal(researchLabMaximumByteWindowLength),
  maximumCursorLength: z.literal(researchLabMaximumCursorLength),
  maximumDirectories: z.literal(researchLabMaximumDirectories),
  maximumEntries: z.literal(researchLabMaximumEntries),
  maximumFileBytes: z.literal(researchLabMaximumFileBytes),
  maximumPageSize: z.literal(researchLabMaximumPageSize),
  maximumRangesPerFile: z.literal(researchLabMaximumRangesPerFile),
  maximumRegistrations: z.literal(researchLabMaximumRegistrations),
  maximumResultCacheBytes: z.literal(researchLabMaximumResultCacheBytes),
  maximumSelectedFiles: z.literal(researchLabMaximumSelectedFiles),
  maximumTraversalDepth: z.literal(researchLabMaximumTraversalDepth),
  registrationLifetimeMinutes: z.literal(researchLabRegistrationLifetimeMinutes),
  requiredComparisonSources: z.literal(researchLabRequiredComparisonSources)
});

export const readResearchLabCapabilitiesRequestSchema = z.strictObject({
  scope: semanticExploreScopeSchema
});

export const readResearchLabCapabilitiesResponseSchema = z
  .strictObject({
    capabilities: z.array(researchCapabilitySchema).max(researchFeatureValues.length),
    extensions: z
      .array(researchExtensionDescriptorSchema)
      .max(researchLabMaximumExtensionDescriptors),
    limits: researchLimitsSchema,
    revision: semanticExploreRevisionSchema,
    snapshots: z.array(semanticExploreSourceSnapshotSchema).max(4)
  })
  .superRefine((response, context) => {
    const features = response.capabilities.map((capability) => capability.feature);
    const extensionIds = response.extensions.map((extension) => extension.extensionId);
    if (
      features.length !== researchFeatureValues.length ||
      researchFeatureValues.some((feature, index) => features[index] !== feature) ||
      response.capabilities.some((capability) => !isExactResearchCapability(capability)) ||
      new Set(features).size !== features.length ||
      !isStrictlyOrdinalSorted(extensionIds) ||
      response.snapshots.some((snapshot) => (
        researchRevisionIdentity(snapshot.revision) !== researchRevisionIdentity(response.revision)
      ))
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The Research Lab capability catalog is inconsistent.'
      });
    }
  });

export const openResearchSourceRequestSchema = z.strictObject({
  expectedRevision: semanticExploreRevisionSchema,
  replaceSourceId: sourceIdSchema.nullable(),
  rootPath: localRootPathSchema,
  scope: semanticExploreScopeSchema
});

export const openResearchSourceResponseSchema = z.strictObject({
  expiresAtUtc: dateTimeOffsetSchema,
  revision: semanticExploreRevisionSchema,
  sourceId: sourceIdSchema
});

export const closeResearchSourceRequestSchema = z.strictObject({
  expectedRevision: semanticExploreRevisionSchema,
  scope: semanticExploreScopeSchema,
  sourceId: sourceIdSchema
});

export const closeResearchSourceResponseSchema = z.strictObject({
  closed: z.boolean(),
  revision: semanticExploreRevisionSchema,
  sourceId: sourceIdSchema
});

export const compareResearchSourcesRequestSchema = z
  .strictObject({
    cursor: cursorSchema.nullable(),
    expectedRevision: semanticExploreRevisionSchema,
    limit: z.number().int().min(1).max(researchLabMaximumPageSize),
    scope: semanticExploreScopeSchema,
    selectedRelativePaths: z.array(relativePathSchema).max(researchLabMaximumSelectedFiles),
    sourceIds: z.array(sourceIdSchema).length(researchLabRequiredComparisonSources)
  })
  .superRefine((request, context) => {
    if (
      new Set(request.sourceIds).size !== request.sourceIds.length ||
      !hasDistinctRelativePaths(request.selectedRelativePaths)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The research comparison selection is duplicated.'
      });
    }
  });

export const researchSourceSnapshotSchema = z.strictObject({
  directoryCount: z.number().int().min(0).max(researchLabMaximumDirectories),
  fileCount: z.number().int().min(0).max(researchLabMaximumEntries),
  fingerprint: fingerprintSchema,
  sourceId: sourceIdSchema,
  totalBytes: z.number().int().min(0).max(researchLabMaximumAggregateBytes)
});

export const researchFileSideSchema = z
  .strictObject({
    contentSha256: fingerprintSchema.nullable(),
    exists: z.boolean(),
    length: fileLengthSchema.nullable()
  })
  .refine((side) => (
    side.exists === (side.length !== null) &&
    side.exists === (side.contentSha256 !== null)
  ), { message: 'Research file existence must match its bounded metadata.' });

export const researchByteRangeSchema = z.strictObject({
  length: z.number().int().min(1).max(researchLabMaximumFileBytes),
  offset: nonnegativeOffsetSchema
}).refine((range) => range.offset + range.length <= researchLabMaximumFileBytes, {
  message: 'The research byte range exceeds the bounded source file.'
});

export const researchOwnershipEvidenceSchema = z.strictObject({
  confidence: z.literal('unknown'),
  coverage: z.literal('unavailable'),
  ownerId: z.null(),
  reasonCode: z.literal('opaque-file-ownership-provider-unavailable')
});

export const researchFileFindingSchema = z
  .strictObject({
    differenceKind: researchFileDifferenceKindSchema,
    findingId: findingIdSchema,
    ownership: researchOwnershipEvidenceSchema,
    rangeCoverage: researchRangeCoverageSchema,
    ranges: z.array(researchByteRangeSchema).max(researchLabMaximumRangesPerFile),
    relativePath: relativePathSchema,
    sourceA: researchFileSideSchema,
    sourceB: researchFileSideSchema
  })
  .superRefine((finding, context) => {
    const sidesMatchDifference = finding.differenceKind === 'added'
      ? !finding.sourceA.exists && finding.sourceB.exists
      : finding.differenceKind === 'removed'
        ? finding.sourceA.exists && !finding.sourceB.exists
        : finding.sourceA.exists && finding.sourceB.exists;
    const orderedRanges = finding.ranges.every((range, index) => {
      if (index === 0) return true;
      const previous = finding.ranges[index - 1]!;
      return previous.offset + previous.length <= range.offset;
    });
    const maximumLength = Math.max(finding.sourceA.length ?? 0, finding.sourceB.length ?? 0);
    if (
      !sidesMatchDifference ||
      finding.differenceKind === 'changed' &&
      finding.sourceA.contentSha256 === finding.sourceB.contentSha256 ||
      !orderedRanges ||
      finding.ranges.some((range) => range.offset + range.length > maximumLength) ||
      finding.rangeCoverage === 'notRequested' && finding.ranges.length !== 0
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The research file finding is internally inconsistent.'
      });
    }
  });

export const compareResearchSourcesResponseSchema = z
  .strictObject({
    comparisonFingerprint: fingerprintSchema,
    comparisonId: comparisonIdSchema,
    items: z.array(researchFileFindingSchema).max(researchLabMaximumPageSize),
    nextCursor: cursorSchema.nullable(),
    queryFingerprint: fingerprintSchema,
    revision: semanticExploreRevisionSchema,
    semanticProjection: researchCapabilitySchema,
    sources: z.array(researchSourceSnapshotSchema).length(researchLabRequiredComparisonSources)
  })
  .superRefine((response, context) => {
    const sourceIds = response.sources.map((source) => source.sourceId);
    const findingIds = response.items.map((item) => item.findingId);
    const relativePaths = response.items.map((item) => item.relativePath);
    if (
      response.semanticProjection.feature !== 'semanticProjection' ||
      response.sources.reduce((total, source) => total + source.totalBytes, 0) >
        researchLabMaximumAggregateBytes ||
      response.sources.reduce((total, source) => total + source.fileCount, 0) >
        researchLabMaximumEntries ||
      response.sources.reduce((total, source) => total + source.directoryCount, 0) >
        researchLabMaximumDirectories ||
      response.items.reduce((total, item) => total + item.ranges.length, 0) >
        researchLabMaximumAggregateRanges ||
      new Set(sourceIds).size !== sourceIds.length ||
      new Set(findingIds).size !== findingIds.length ||
      !hasDistinctRelativePaths(relativePaths) ||
      response.nextCursor !== null && response.items.length === 0
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The research comparison page is internally inconsistent.'
      });
    }
  });

export const readResearchByteWindowRequestSchema = z.strictObject({
  comparisonId: comparisonIdSchema,
  expectedComparisonFingerprint: fingerprintSchema,
  expectedRevision: semanticExploreRevisionSchema,
  length: z.number().int().min(1).max(researchLabMaximumByteWindowLength),
  offset: nonnegativeOffsetSchema,
  relativePath: relativePathSchema,
  scope: semanticExploreScopeSchema
});

const base64Schema = z.string().refine((value) => {
  if (value.length > Math.ceil(researchLabMaximumByteWindowLength / 3) * 4) return false;
  if (value === '') return true;
  return /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u.test(value);
});

export const researchByteWindowSideSchema = z
  .strictObject({
    bytesBase64: base64Schema.nullable(),
    exists: z.boolean(),
    fileLength: fileLengthSchema.nullable(),
    windowSha256: fingerprintSchema.nullable()
  })
  .refine((side) => (
    side.exists === (side.fileLength !== null) &&
    side.exists === (side.bytesBase64 !== null) &&
    side.exists === (side.windowSha256 !== null)
  ), { message: 'Research byte-window existence must match its bounded payload.' });

export const readResearchByteWindowResponseSchema = z
  .strictObject({
    comparisonFingerprint: fingerprintSchema,
    offset: nonnegativeOffsetSchema,
    relativePath: relativePathSchema,
    requestedLength: z.number().int().min(1).max(researchLabMaximumByteWindowLength),
    revision: semanticExploreRevisionSchema,
    sourceA: researchByteWindowSideSchema,
    sourceB: researchByteWindowSideSchema
  })
  .superRefine((response, context) => {
    for (const side of [response.sourceA, response.sourceB]) {
      if (!side.exists) continue;
      const byteLength = researchBase64ByteLength(side.bytesBase64!);
      const expectedLength = Math.min(
        response.requestedLength,
        Math.max(side.fileLength! - response.offset, 0)
      );
      if (byteLength !== expectedLength) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'The research byte window exceeds the requested source range.'
        });
      }
    }
  });

export const researchRelativeRangeRefSchema = z.strictObject({
  comparisonFingerprint: fingerprintSchema,
  length: z.number().int().min(1).max(researchLabMaximumFileBytes),
  offset: nonnegativeOffsetSchema,
  relativePath: relativePathSchema
}).refine((range) => range.offset + range.length <= researchLabMaximumFileBytes, {
  message: 'The annotation range exceeds the bounded source file.'
});

export const researchFindingRefSchema = z.strictObject({
  comparisonFingerprint: fingerprintSchema,
  findingId: findingIdSchema,
  relativePath: relativePathSchema
});

export const researchAnnotationTargetSchema = z
  .strictObject({
    finding: researchFindingRefSchema.nullable(),
    kind: researchAnnotationTargetKindSchema,
    relativeRange: researchRelativeRangeRefSchema.nullable(),
    revision: semanticExploreRevisionSchema,
    semanticRecord: semanticExploreRecordRefSchema.nullable(),
    semanticSnapshot: semanticExploreSourceSnapshotSchema.nullable()
  })
  .superRefine((target, context) => {
    const isSemantic = target.kind === 'semanticRecord';
    const isRange = target.kind === 'relativeRange';
    const isFinding = target.kind === 'finding';
    if (
      isSemantic !== (target.semanticRecord !== null) ||
      isSemantic !== (target.semanticSnapshot !== null) ||
      isRange !== (target.relativeRange !== null) ||
      isFinding !== (target.finding !== null) ||
      isSemantic && (
        target.semanticRecord!.gameFamily !== target.revision.gameFamily ||
        researchRevisionIdentity(target.semanticSnapshot!.revision) !==
          researchRevisionIdentity(target.revision) ||
        !isResearchPrivateIdentifier(target.semanticRecord!.recordId) ||
        target.semanticRecord!.subrecordId !== null &&
          !isResearchPrivateIdentifier(target.semanticRecord!.subrecordId) ||
        target.semanticSnapshot!.layer.instanceId !== null &&
          !isResearchPrivateIdentifier(target.semanticSnapshot!.layer.instanceId)
      )
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The research annotation target is inconsistent or contains a private path.'
      });
    }
  });

export const researchAnnotationSchema = z.strictObject({
  annotationId: annotationIdSchema,
  createdAtUtc: dateTimeOffsetSchema,
  tags: z.array(annotationTagSchema).max(researchLabMaximumAnnotationTags),
  target: researchAnnotationTargetSchema,
  text: annotationTextSchema,
  updatedAtUtc: dateTimeOffsetSchema
}).superRefine((annotation, context) => {
  if (
    new Set(annotation.tags.map(researchPortableCaseFold)).size !==
      annotation.tags.length ||
    Date.parse(annotation.updatedAtUtc) < Date.parse(annotation.createdAtUtc)
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The research annotation is duplicated or has invalid timestamps.'
    });
  }
});

export const researchAnnotationDocumentSchema = z
  .strictObject({
    annotations: z.array(researchAnnotationSchema).max(researchLabMaximumAnnotationCount),
    schemaVersion: z.literal(researchLabSchemaVersion),
    updatedAtUtc: dateTimeOffsetSchema
  })
  .superRefine((document, context) => {
    const ids = document.annotations.map((annotation) => annotation.annotationId);
    if (
      new Set(ids).size !== ids.length ||
      document.annotations.some((annotation) => (
        Date.parse(annotation.updatedAtUtc) > Date.parse(document.updatedAtUtc)
      ))
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The research annotation document is inconsistent.'
      });
    }
  });

export const readResearchAnnotationsRequestSchema = z.strictObject({
  expectedRevision: semanticExploreRevisionSchema,
  scope: semanticExploreScopeSchema
});

export const readResearchAnnotationsResponseSchema = z
  .strictObject({
    document: researchAnnotationDocumentSchema.nullable(),
    etag: etagSchema.nullable(),
    exists: z.boolean(),
    revision: semanticExploreRevisionSchema
  })
  .refine((response) => (
    response.exists === (response.document !== null) &&
    response.exists === (response.etag !== null)
  ), { message: 'Research annotation existence must match document and ETag presence.' });

export const researchAnnotationDraftSchema = z.strictObject({
  annotationId: annotationIdSchema.nullable(),
  tags: z.array(annotationTagSchema).max(researchLabMaximumAnnotationTags),
  target: researchAnnotationTargetSchema,
  text: annotationTextSchema
}).superRefine((draft, context) => {
  if (new Set(draft.tags.map(researchPortableCaseFold)).size !== draft.tags.length) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Research annotation tags must be unique.'
    });
  }
});

export const researchAnnotationMutationSchema = z
  .strictObject({
    annotationId: annotationIdSchema.nullable(),
    kind: researchAnnotationMutationKindSchema,
    upsert: researchAnnotationDraftSchema.nullable()
  })
  .refine((mutation) => (
    mutation.kind === 'upsert'
      ? mutation.annotationId === null && mutation.upsert !== null
      : mutation.annotationId !== null && mutation.upsert === null
  ), { message: 'The research annotation mutation shape is inconsistent.' });

export const mutateResearchAnnotationsRequestSchema = z.strictObject({
  expectedETag: etagSchema.nullable(),
  expectedRevision: semanticExploreRevisionSchema,
  mutation: researchAnnotationMutationSchema,
  scope: semanticExploreScopeSchema
});

export const mutateResearchAnnotationsResponseSchema = z.strictObject({
  document: researchAnnotationDocumentSchema,
  etag: etagSchema,
  revision: semanticExploreRevisionSchema,
  writtenAtUtc: dateTimeOffsetSchema
});

export type ResearchFeature = z.infer<typeof researchFeatureSchema>;
export type ResearchExtensionKind = z.infer<typeof researchExtensionKindSchema>;
export type ResearchFileDifferenceKind = z.infer<typeof researchFileDifferenceKindSchema>;
export type ResearchRangeCoverage = z.infer<typeof researchRangeCoverageSchema>;
export type ResearchAnnotationTargetKind = z.infer<typeof researchAnnotationTargetKindSchema>;
export type ResearchAnnotationMutationKind = z.infer<typeof researchAnnotationMutationKindSchema>;
export type ResearchCoverageState = z.infer<typeof researchCoverageStateSchema>;
export type ResearchConfidence = z.infer<typeof researchConfidenceSchema>;
export type ResearchCapability = z.infer<typeof researchCapabilitySchema>;
export type ResearchExtensionDescriptor = z.infer<typeof researchExtensionDescriptorSchema>;
export type ResearchLimits = z.infer<typeof researchLimitsSchema>;
export type ReadResearchLabCapabilitiesRequest = z.infer<
  typeof readResearchLabCapabilitiesRequestSchema
>;
export type ReadResearchLabCapabilitiesResponse = z.infer<
  typeof readResearchLabCapabilitiesResponseSchema
>;
export type OpenResearchSourceRequest = z.infer<typeof openResearchSourceRequestSchema>;
export type OpenResearchSourceResponse = z.infer<typeof openResearchSourceResponseSchema>;
export type CloseResearchSourceRequest = z.infer<typeof closeResearchSourceRequestSchema>;
export type CloseResearchSourceResponse = z.infer<typeof closeResearchSourceResponseSchema>;
export type CompareResearchSourcesRequest = z.infer<typeof compareResearchSourcesRequestSchema>;
export type ResearchSourceSnapshot = z.infer<typeof researchSourceSnapshotSchema>;
export type ResearchFileSide = z.infer<typeof researchFileSideSchema>;
export type ResearchByteRange = z.infer<typeof researchByteRangeSchema>;
export type ResearchOwnershipEvidence = z.infer<typeof researchOwnershipEvidenceSchema>;
export type ResearchFileFinding = z.infer<typeof researchFileFindingSchema>;
export type CompareResearchSourcesResponse = z.infer<
  typeof compareResearchSourcesResponseSchema
>;
export type ReadResearchByteWindowRequest = z.infer<typeof readResearchByteWindowRequestSchema>;
export type ResearchByteWindowSide = z.infer<typeof researchByteWindowSideSchema>;
export type ReadResearchByteWindowResponse = z.infer<
  typeof readResearchByteWindowResponseSchema
>;
export type ResearchRelativeRangeRef = z.infer<typeof researchRelativeRangeRefSchema>;
export type ResearchFindingRef = z.infer<typeof researchFindingRefSchema>;
export type ResearchAnnotationTarget = z.infer<typeof researchAnnotationTargetSchema>;
export type ResearchAnnotation = z.infer<typeof researchAnnotationSchema>;
export type ResearchAnnotationDocument = z.infer<typeof researchAnnotationDocumentSchema>;
export type ReadResearchAnnotationsRequest = z.infer<typeof readResearchAnnotationsRequestSchema>;
export type ReadResearchAnnotationsResponse = z.infer<
  typeof readResearchAnnotationsResponseSchema
>;
export type ResearchAnnotationDraft = z.infer<typeof researchAnnotationDraftSchema>;
export type ResearchAnnotationMutation = z.infer<typeof researchAnnotationMutationSchema>;
export type MutateResearchAnnotationsRequest = z.infer<
  typeof mutateResearchAnnotationsRequestSchema
>;
export type MutateResearchAnnotationsResponse = z.infer<
  typeof mutateResearchAnnotationsResponseSchema
>;

export function researchRevisionIdentity(
  revision: z.infer<typeof semanticExploreRevisionSchema>
) {
  return JSON.stringify([
    revision.projectId,
    revision.gameFamily,
    revision.generation,
    revision.fingerprint
  ]);
}

export function researchAnnotationTargetIdentity(target: ResearchAnnotationTarget) {
  switch (target.kind) {
    case 'semanticRecord':
      return JSON.stringify([
        target.kind,
        researchRevisionIdentity(target.revision),
        target.semanticSnapshot?.layer.kind,
        target.semanticSnapshot?.layer.instanceId,
        target.semanticSnapshot?.fingerprint,
        target.semanticRecord?.gameFamily,
        target.semanticRecord?.domain,
        target.semanticRecord?.recordKind.key,
        target.semanticRecord?.recordKind.schemaVersion,
        target.semanticRecord?.recordId,
        target.semanticRecord?.subrecordId
      ]);
    case 'relativeRange':
      return JSON.stringify([
        target.kind,
        researchRevisionIdentity(target.revision),
        target.relativeRange?.comparisonFingerprint,
        target.relativeRange?.relativePath,
        target.relativeRange?.offset,
        target.relativeRange?.length
      ]);
    case 'finding':
      return JSON.stringify([
        target.kind,
        researchRevisionIdentity(target.revision),
        target.finding?.comparisonFingerprint,
        target.finding?.findingId,
        target.finding?.relativePath
      ]);
  }
}

export function containsResearchLocalPathSignature(value: string) {
  let candidate = value;
  for (let depth = 0; depth <= 3; depth += 1) {
    if (
      candidate.includes('\\') ||
      /(?:^|[^A-Za-z0-9])[A-Za-z]:[^\s]/u.test(candidate) ||
      /(?:^|[^A-Za-z0-9])file:/iu.test(candidate) ||
      /(?:^|[^A-Za-z0-9])\/[^\s/]+\/\S/u.test(candidate) ||
      candidate.startsWith('~')
    ) return true;
    if (depth === 3 || !candidate.includes('%')) break;
    try {
      const decoded = decodeURIComponent(candidate);
      if (decoded === candidate) return true;
      candidate = decoded;
    } catch {
      return true;
    }
  }
  return false;
}

function hasDistinctRelativePaths(paths: readonly string[]) {
  const identities = paths.map((path) => researchPortableCaseFold(path.normalize('NFC')));
  return new Set(identities).size === identities.length;
}

function isResearchPrivateIdentifier(value: string) {
  return value.length > 0 &&
    value.length <= 1_024 &&
    value.trim() === value &&
    value.normalize('NFC') === value &&
    !strictUnsafeTextPattern.test(value) &&
    !containsResearchLocalPathSignature(value);
}

export function researchBase64ByteLength(value: string) {
  if (!value) return 0;
  const padding = value.endsWith('==') ? 2 : value.endsWith('=') ? 1 : 0;
  return value.length / 4 * 3 - padding;
}

export function researchPortableCaseFold(value: string) {
  return value.replace(/[A-Z]/gu, (character) => (
    String.fromCharCode(character.charCodeAt(0) + 32)
  ));
}

function isStrictlyOrdinalSorted(values: readonly string[]) {
  return values.every((value, index) => index === 0 || values[index - 1]! < value);
}

function isExactResearchCapability(capability: z.infer<typeof researchCapabilitySchema>) {
  const expected = {
    annotations: [true, 'verified', 'partial', 'comparison-target-creation-only'],
    byteWindows: [true, 'verified', 'complete', null],
    ownershipEvidence: [false, 'unknown', 'unavailable',
      'opaque-file-ownership-provider-unavailable'],
    readOnlyExtensions: [true, 'verified', 'partial', 'host-registered-descriptors-only'],
    semanticProjection: [false, 'unknown', 'unavailable',
      'selected-dump-semantic-provider-unavailable'],
    sourceComparison: [true, 'verified', 'complete', null],
    writableExtensions: [false, 'unknown', 'unavailable',
      'writable-extensions-not-supported']
  } as const;
  const boundary = expected[capability.feature];
  return capability.canUse === boundary[0] &&
    capability.confidence === boundary[1] &&
    capability.coverage === boundary[2] &&
    capability.reasonCode === boundary[3];
}
