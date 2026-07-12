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
    SwShPlacementProvenance Provenance,
    string CategoryId = "",
    string CategoryLabel = "",
    IReadOnlyList<SwShPlacementFieldValue>? Fields = null,
    bool ItemUsesHashStorage = false,
    bool ItemUsesDirectIdStorage = false);

public sealed record SwShPlacementFieldValue(
    string Field,
    string Label,
    string Group,
    string Value,
    string DisplayValue,
    bool IsReadOnly,
    string ValueKind = "text",
    double MinimumValue = 0,
    double MaximumValue = 0,
    string Description = "",
    IReadOnlyList<SwShPlacementEditableFieldOption>? Options = null);

public sealed record SwShPlacementEditableField(
    string Field,
    string Label,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    IReadOnlyList<SwShPlacementEditableFieldOption> Options,
    string Group = "",
    bool IsReadOnly = false,
    string Description = "")
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

    public SwShPlacementEditableField(
        string Field,
        string Label,
        string ValueKind,
        double MinimumValue,
        double MaximumValue,
        string Group,
        string Description = "")
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShPlacementEditableFieldOption>(), Group, false, Description)
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

public sealed record SwShPlacementCategory(
    string Id,
    string Label,
    string Description,
    int ObjectCount);

public sealed record SwShPlacementWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShPlacedObjectRecord> Objects,
    IReadOnlyList<SwShPlacementEditableField> EditableFields,
    SwShPlacementWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    IReadOnlyList<SwShPlacementCategory> Categories);
