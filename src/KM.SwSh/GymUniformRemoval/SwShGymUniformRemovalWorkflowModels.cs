// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SwSh.GymUniformRemoval;

public sealed record SwShGymUniformRemovalProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShGymUniformRemovalReservedRegion(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record SwShGymUniformRemovalWorkflowStats(
    int ReservedMainTextRegionCount,
    int SourceFileCount,
    int OwnedByteCount);

public sealed record SwShGymUniformRemovalWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    bool CanUninstall,
    string BuildId,
    string PatchOffsetHex,
    string MainHandlerState,
    string IpsArtifactState,
    ProjectGame? DetectedGame,
    IReadOnlyList<SwShGymUniformRemovalReservedRegion> ReservedRegions,
    SwShGymUniformRemovalProvenance Provenance,
    SwShGymUniformRemovalWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
