// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Output;

internal sealed record OutputJournalEntry(
    RelativeOutputPath Path,
    OutputMutationKind Kind,
    OutputFileState Preimage,
    OutputFileState Postimage,
    ImmutableArray<OwnedTarget> OwnershipClaims,
    string OwnershipOutputMode,
    OutputRuntimeMutableDescriptor? RuntimeMutableDescriptor,
    bool? RestoredFileDeleteEligibility,
    string? StageFileName,
    string? BackupFileName,
    OutputLegacyAdoptionDeleteAuthority? LegacyAdoptionDeleteAuthority = null);

internal sealed record OutputTransactionJournal(
    int SchemaVersion,
    OutputTransactionId TransactionId,
    OutputTransactionPhase Phase,
    ProjectId ProjectId,
    GameFamily GameFamily,
    string OutputMode,
    string SemanticReviewHash,
    ImmutableArray<OutputApplyOrigin> Origins,
    ImmutableArray<OutputJournalEntry> Entries,
    ImmutableArray<RelativeOutputPath> CreatedDirectories,
    int PublishedEntryCount,
    DateTimeOffset StartedAtUtc,
    string? OutcomeCode)
{
    public const int CurrentSchemaVersion = 3;
}

internal sealed record OutputApplyHistoryDocument(
    int SchemaVersion,
    ImmutableArray<OutputApplyReceipt> Receipts)
{
    public const int CurrentSchemaVersion = 1;
}

internal sealed record OutputCheckpointEntry(
    RelativeOutputPath Path,
    OutputFileState State,
    ImmutableArray<OwnedTarget> OwnershipClaims,
    string OutputMode,
    bool FileDeleteEligible,
    string ContentFileName);

internal sealed record OutputCheckpointManifest(
    int SchemaVersion,
    OutputCheckpointSummary Summary,
    ImmutableArray<OutputCheckpointEntry> Entries)
{
    public const int CurrentSchemaVersion = 2;
}
