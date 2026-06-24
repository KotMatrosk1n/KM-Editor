// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Placement;

public sealed record ZaPlacementProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaPlacementFieldValue(
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
    IReadOnlyList<ZaPlacementEditableFieldOption>? Options = null);

public sealed record ZaPlacedObjectRecord(
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
    IReadOnlyList<ZaPlacementFieldValue> Fields,
    ZaPlacementProvenance Provenance);

public sealed record ZaPlacementEditableField(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    bool IsReadOnly,
    string Description,
    IReadOnlyList<ZaPlacementEditableFieldOption> Options);

public sealed record ZaPlacementEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaPlacementCategory(
    string Id,
    string Label,
    string Description,
    int ObjectCount);

public sealed record ZaPlacementWorkflowStats(
    int TotalObjectCount,
    int TotalAreaCount,
    int SourceFileCount);

public sealed record ZaPlacementWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaPlacedObjectRecord> Objects,
    IReadOnlyList<ZaPlacementEditableField> EditableFields,
    IReadOnlyList<ZaPlacementCategory> Categories,
    ZaPlacementWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaPlacementEditResult(
    ZaPlacementWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaPlacementObjectFieldUpdate(
    string ObjectId,
    string Field,
    string Value);
