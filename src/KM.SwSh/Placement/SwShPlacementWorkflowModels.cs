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
    string ArchiveMember,
    int ZoneIndex,
    int ObjectIndex,
    int? ChanceIndex,
    uint? ItemId,
    string ItemName,
    string ItemHash,
    int Quantity,
    int? Chance,
    double X,
    double Y,
    double Z,
    double RotationY,
    string? ScriptId,
    SwShPlacementProvenance Provenance);

public sealed record SwShPlacementEditableField(
    string Field,
    string Label,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    IReadOnlyList<SwShPlacementEditableFieldOption> Options)
{
    public SwShPlacementEditableField(
        string Field,
        string Label,
        string ValueKind,
        double MinimumValue,
        double MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShPlacementEditableFieldOption>())
    {
    }
}

public sealed record SwShPlacementEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShPlacementWorkflowStats(
    int TotalObjectCount,
    int TotalAreaCount,
    int SourceFileCount);

public sealed record SwShPlacementWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShPlacedObjectRecord> Objects,
    IReadOnlyList<SwShPlacementEditableField> EditableFields,
    SwShPlacementWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
