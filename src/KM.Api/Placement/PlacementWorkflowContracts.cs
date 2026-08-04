// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Placement;

public sealed record LoadPlacementWorkflowRequest(ProjectPathsDto Paths);

public sealed record PlacementProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record PlacementFieldValueDto(
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
    IReadOnlyList<PlacementEditableFieldOptionDto>? Options = null);

public sealed record PlacedObjectRecordDto(
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
    PlacementProvenanceDto Provenance,
    string CategoryId = "",
    string CategoryLabel = "",
    IReadOnlyList<PlacementFieldValueDto>? Fields = null,
    string? PreviewText = null);

public sealed record PlacementEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    IReadOnlyList<PlacementEditableFieldOptionDto> Options,
    string Group = "",
    bool IsReadOnly = false,
    string Description = "");

public sealed record PlacementEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record PlacementCategoryDto(
    string Id,
    string Label,
    string Description,
    int ObjectCount);

public sealed record PlacementWorkflowStatsDto(
    int TotalObjectCount,
    int TotalAreaCount,
    int SourceFileCount);

public sealed record PlacementWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<PlacedObjectRecordDto> Objects,
    IReadOnlyList<PlacementEditableFieldDto> EditableFields,
    PlacementWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    IReadOnlyList<PlacementCategoryDto>? Categories = null);

public sealed record LoadPlacementWorkflowResponse(PlacementWorkflowDto Workflow);

public sealed record OpenSwShPlacementCatalogRequest(ProjectPathsDto Paths);

public sealed record SwShPlacementCatalogDto(
    string Revision,
    WorkflowSummaryDto Summary,
    IReadOnlyList<PlacementEditableFieldDto> EditableFields,
    PlacementWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    IReadOnlyList<PlacementCategoryDto> Categories);

public sealed record OpenSwShPlacementCatalogResponse(SwShPlacementCatalogDto Catalog);

public sealed record QuerySwShPlacementCatalogRequest(
    ProjectPathsDto Paths,
    string Revision,
    string? CategoryId = null,
    string? SearchText = null,
    int Offset = 0,
    int Limit = 100,
    EditSessionDto? Session = null);

public sealed record QuerySwShPlacementCatalogResponse(
    string Revision,
    IReadOnlyList<PlacedObjectRecordDto> Objects,
    int Offset,
    int Limit,
    int TotalCount);

public sealed record LoadSwShPlacementObjectRequest(
    ProjectPathsDto Paths,
    string Revision,
    string ObjectId,
    EditSessionDto? Session = null);

public sealed record LoadSwShPlacementObjectResponse(
    string Revision,
    PlacedObjectRecordDto Object,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdatePlacementObjectFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string ObjectId,
    string Field,
    string Value);

public sealed record PlacementObjectFieldUpdateDto(
    string ObjectId,
    string Field,
    string Value);

public sealed record UpdatePlacementObjectFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<PlacementObjectFieldUpdateDto> Updates);

public sealed record UpdatePlacementObjectFieldResponse(
    PlacementWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdatePlacementObjectFieldsResponse(
    PlacementWorkflowDto? Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    IReadOnlyList<PlacedObjectRecordDto>? UpdatedObjects = null);
