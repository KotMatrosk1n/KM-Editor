// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Items;

public sealed record LoadItemsWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateItemFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int ItemId,
    string Field,
    string Value);

public sealed record ItemFieldUpdateDto(
    int ItemId,
    string Field,
    string Value);

public sealed record UpdateItemFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<ItemFieldUpdateDto> Updates);

public sealed record UpdateItemFieldResponse(
    ItemsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdateItemFieldsResponse(
    ItemsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ItemProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record ItemDetailDto(
    string Label,
    string Value);

public sealed record ItemDetailGroupDto(
    string Label,
    IReadOnlyList<ItemDetailDto> Details);

public sealed record ItemMetadataDto(
    int Pouch,
    int PouchFlags,
    int FlingPower,
    int FieldUseType,
    int FieldFlags,
    bool CanUseOnPokemon,
    int ItemType,
    int SortIndex,
    int ItemSprite,
    int GroupType,
    int GroupIndex,
    int CureStatusFlags,
    int Boost0,
    int Boost1,
    int Boost2,
    int Boost3,
    int UseFlags1,
    int UseFlags2,
    int EvHp,
    int EvAttack,
    int EvDefense,
    int EvSpeed,
    int EvSpecialAttack,
    int EvSpecialDefense,
    int HealAmount,
    int PpGain,
    int FriendshipGain1,
    int FriendshipGain2,
    int FriendshipGain3,
    int? MachineSlot,
    int? MachineMoveId,
    string? MachineMoveName);

public sealed record ItemRecordDto(
    int ItemId,
    string Name,
    string Category,
    int BuyPrice,
    int SellPrice,
    int WattsPrice,
    int AlternatePrice,
    IReadOnlyDictionary<string, int?> FieldValues,
    ItemMetadataDto Metadata,
    IReadOnlyList<int> SharedItemIds,
    IReadOnlyList<ItemDetailGroupDto> DetailGroups,
    ItemProvenanceDto Provenance);

public sealed record ItemEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record ItemEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ItemEditableFieldOptionDto> Options);

public sealed record ItemsWorkflowStatsDto(
    int TotalItemCount,
    int SourceFileCount);

public sealed record ItemsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<ItemRecordDto> Items,
    IReadOnlyList<ItemEditableFieldDto> EditableFields,
    ItemsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadItemsWorkflowResponse(ItemsWorkflowDto Workflow);
