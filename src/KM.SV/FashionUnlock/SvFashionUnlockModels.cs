// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Workflows;

namespace KM.SV.FashionUnlock;

public sealed record SvFashionUnlockProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvFashionUnlockReservedRegion(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record SvFashionUnlockWorkflowStats(
    int ReservedMainTextRegionCount,
    int SourceFileCount);

public sealed record SvFashionUnlockWorkflow(
    SvWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string OwnershipCheckOffsetHex,
    string StubKind,
    ProjectGame? DetectedGame,
    IReadOnlyList<SvFashionUnlockReservedRegion> ReservedRegions,
    SvFashionUnlockProvenance Provenance,
    SvFashionUnlockWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvFashionUnlockEditResult(
    SvFashionUnlockWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
