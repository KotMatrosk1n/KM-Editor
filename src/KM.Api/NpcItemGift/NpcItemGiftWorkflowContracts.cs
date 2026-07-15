// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.NpcItemGift;

public sealed record LoadNpcItemGiftWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageNpcItemGiftRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<NpcItemGiftSelectionDto> Gifts);

public sealed record NpcItemGiftSelectionDto(
    string GiftId,
    int Quantity,
    IReadOnlyList<NpcItemGiftItemSelectionDto> Items);

public sealed record NpcItemGiftItemSelectionDto(
    string SlotId,
    int ItemId);

public sealed record NpcItemGiftProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record NpcItemGiftSourceRecordDto(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    NpcItemGiftProvenanceDto Provenance);

public sealed record NpcItemGiftItemOptionRecordDto(
    int ItemId,
    string Name,
    string Category,
    bool IsKeyItem);

public sealed record NpcItemGiftItemSlotRecordDto(
    string SlotId,
    string Label,
    int ItemId,
    string ItemName,
    int VanillaItemId,
    string VanillaItemName,
    int ItemCell);

public sealed record NpcItemGiftRecordDto(
    string GiftId,
    string NpcId,
    string NpcName,
    string Label,
    string Location,
    int DisplayOrder,
    string RelativePath,
    string Status,
    int Quantity,
    int VanillaQuantity,
    int? QuantityCell,
    bool CanEditQuantity,
    IReadOnlyList<NpcItemGiftItemSlotRecordDto> Items,
    NpcItemGiftProvenanceDto Provenance);

public sealed record NpcItemGiftNpcGroupDto(
    string NpcId,
    string NpcName,
    int DisplayOrder,
    IReadOnlyList<NpcItemGiftRecordDto> Gifts);

public sealed record NpcItemGiftWorkflowStatsDto(
    int NpcCount,
    int GiftCount,
    int SourceFileCount,
    int ItemOptionCount);

public sealed record NpcItemGiftWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<NpcItemGiftNpcGroupDto> Npcs,
    IReadOnlyList<NpcItemGiftSourceRecordDto> Sources,
    IReadOnlyList<NpcItemGiftItemOptionRecordDto> ItemOptions,
    NpcItemGiftWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadNpcItemGiftWorkflowResponse(NpcItemGiftWorkflowDto Workflow);

public sealed record StageNpcItemGiftResponse(
    NpcItemGiftWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
