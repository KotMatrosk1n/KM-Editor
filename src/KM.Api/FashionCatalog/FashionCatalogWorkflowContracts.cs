// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.FashionCatalog;

public enum FashionCatalogFileDto
{
    DressUpItems,
    DressUpGroups,
    HairAndMakeup,
    DressUpLineups,
    HairAndMakeupLineups,
}

public sealed record FashionCatalogRowBindingDto(
    string SourceRevision,
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision);

public sealed record DressUpItemRecordDto(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    uint ItemId,
    string ModelPart,
    uint CatalogGroupCode,
    string ModelVariant,
    uint CategoryCode,
    uint ColorVariantCode,
    string PrimaryColorLabel,
    string SecondaryColorLabel,
    uint DisplayOrder,
    uint VariantOrder);

public sealed record DressUpGroupRecordDto(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    string ModelPart,
    uint DisplayOrder,
    string DisplayLabel);

public sealed record HairAndMakeupRecordDto(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    uint ItemId,
    string ModelKey,
    uint CatalogTypeCode,
    string? ColorValue,
    string? LabelKey,
    uint DisplayOrder,
    int GroupCode,
    int VariantCode);

public sealed record FashionLineupEntryRecordDto(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    int LineupPhysicalIndex,
    int EntryPhysicalIndex,
    string LineupId,
    IReadOnlyList<string> ShopIds,
    uint ItemId);

public sealed record FashionCatalogStatsDto(
    int DressUpItemCount,
    int DressUpGroupCount,
    int HairAndMakeupCount,
    int DressUpLineupEntryCount,
    int HairAndMakeupLineupEntryCount);

public sealed record FashionCatalogWorkflowDto(
    WorkflowSummaryDto Summary,
    string SourceRevision,
    string DressUpItemsRevision,
    string DressUpGroupsRevision,
    string HairAndMakeupRevision,
    string FashionShopsRevision,
    string DressUpLineupsRevision,
    string HairAndMakeupLineupsRevision,
    IReadOnlyList<DressUpItemRecordDto> DressUpItems,
    IReadOnlyList<DressUpGroupRecordDto> DressUpGroups,
    IReadOnlyList<HairAndMakeupRecordDto> HairAndMakeup,
    IReadOnlyList<FashionLineupEntryRecordDto> DressUpLineups,
    IReadOnlyList<FashionLineupEntryRecordDto> HairAndMakeupLineups,
    FashionCatalogStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    bool CanStage);

public sealed record LoadFashionCatalogWorkflowRequest(ProjectPathsDto Paths);

public sealed record LoadFashionCatalogWorkflowResponse(FashionCatalogWorkflowDto Workflow);

public sealed record StageFashionCatalogFieldEditRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    FashionCatalogFileDto CatalogFile,
    FashionCatalogRowBindingDto Binding,
    string Field,
    string? Value,
    bool Clear = false);

public sealed record StageFashionCatalogFieldEditResponse(
    FashionCatalogWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
