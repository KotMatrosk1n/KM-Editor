// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Placement;

public sealed record SvPlacementProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvPlacementFieldValue(
    string Field,
    string Label,
    string Group,
    string Value,
    string DisplayValue,
    bool IsReadOnly,
    IReadOnlyList<SvPlacementEditableFieldOption>? Options = null);

public sealed record SvPlacedObjectRecord(
    string ObjectId,
    string CategoryId,
    string CategoryLabel,
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
    IReadOnlyList<SvPlacementFieldValue> Fields,
    SvPlacementProvenance Provenance);

public sealed record SvPlacementEditableField(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    bool IsReadOnly,
    string Description,
    IReadOnlyList<SvPlacementEditableFieldOption> Options)
{
    public SvPlacementEditableField(
        string Field,
        string Label,
        string Group,
        string ValueKind,
        double MinimumValue,
        double MaximumValue,
        bool IsReadOnly = false,
        string Description = "")
        : this(
            Field,
            Label,
            Group,
            ValueKind,
            MinimumValue,
            MaximumValue,
            IsReadOnly,
            Description,
            Array.Empty<SvPlacementEditableFieldOption>())
    {
    }
}

public sealed record SvPlacementEditableFieldOption(
    int Value,
    string Label);

public sealed record SvPlacementCategory(
    string Id,
    string Label,
    string Description,
    int ObjectCount);

public sealed record SvPlacementWorkflowStats(
    int TotalObjectCount,
    int TotalAreaCount,
    int SourceFileCount);

public sealed record SvPlacementWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvPlacedObjectRecord> Objects,
    IReadOnlyList<SvPlacementEditableField> EditableFields,
    IReadOnlyList<SvPlacementCategory> Categories,
    SvPlacementWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
