// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Placement;

public sealed record SwShPlacementProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShPlacedObjectRecord(
    string ObjectId,
    string ObjectType,
    string Label,
    string Map,
    double X,
    double Y,
    double Z,
    double RotationY,
    string? ScriptId,
    SwShPlacementProvenance Provenance);

public sealed record SwShPlacementWorkflowStats(
    int TotalObjectCount,
    int SourceFileCount);

public sealed record SwShPlacementWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShPlacedObjectRecord> Objects,
    SwShPlacementWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
