// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SwSh.FashionUnlock;

public sealed record SwShFashionUnlockProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShFashionUnlockReservedRegion(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record SwShFashionUnlockWorkflowStats(
    int ReservedMainTextRegionCount,
    int SourceFileCount,
    int OwnedByteCount);

public sealed record SwShFashionUnlockWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    bool CanUninstall,
    string BuildId,
    string DirectGetterOffsetHex,
    string MappedGetterOffsetHex,
    string StubKind,
    ProjectGame? DetectedGame,
    IReadOnlyList<SwShFashionUnlockReservedRegion> ReservedRegions,
    SwShFashionUnlockProvenance Provenance,
    SwShFashionUnlockWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShFashionUnlockEditResult(
    SwShFashionUnlockWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
