// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SwSh.IvScreen;

public sealed record SwShIvScreenProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShIvScreenReservedRegion(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record SwShIvScreenWorkflowStats(
    int ReservedMainTextRegionCount,
    int SourceFileCount);

public sealed record SwShIvScreenWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string Marker,
    string BuildId,
    ProjectGame? DetectedGame,
    string PrimaryValueSourceOffsetHex,
    string XToggleRefreshOffsetHex,
    string RawIvGetterOffsetHex,
    string HyperTrainingWrapperOffsetHex,
    bool CanUninstall,
    IReadOnlyList<SwShIvScreenReservedRegion> ReservedRegions,
    SwShIvScreenProvenance Provenance,
    SwShIvScreenWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
