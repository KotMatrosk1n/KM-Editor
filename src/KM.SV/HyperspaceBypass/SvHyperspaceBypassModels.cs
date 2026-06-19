// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Workflows;

namespace KM.SV.HyperspaceBypass;

public sealed record SvHyperspaceBypassProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvHyperspaceBypassReservedRegion(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record SvHyperspaceBypassWorkflowStats(
    int ReservedMainTextRegionCount,
    int SourceFileCount);

public sealed record SvHyperspaceBypassWorkflow(
    SvWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string PatchOffsetHex,
    string StubKind,
    ProjectGame? DetectedGame,
    IReadOnlyList<SvHyperspaceBypassReservedRegion> ReservedRegions,
    SvHyperspaceBypassProvenance Provenance,
    SvHyperspaceBypassWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvHyperspaceBypassEditResult(
    SvHyperspaceBypassWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
