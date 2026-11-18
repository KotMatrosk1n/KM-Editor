// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Workflows;

namespace KM.ZA.FashionUnlock;

public sealed record ZaFashionUnlockProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaFashionUnlockReservedRegion(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record ZaFashionUnlockWorkflowStats(
    int ReservedMainTextRegionCount,
    int SourceFileCount,
    int OwnedByteCount);

public sealed record ZaFashionUnlockWorkflow(
    ZaWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    bool CanUninstall,
    string BuildId,
    string OwnershipCheckOffsetHex,
    string StubKind,
    ProjectGame? DetectedGame,
    IReadOnlyList<ZaFashionUnlockReservedRegion> ReservedRegions,
    ZaFashionUnlockProvenance Provenance,
    ZaFashionUnlockWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaFashionUnlockEditResult(
    ZaFashionUnlockWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
