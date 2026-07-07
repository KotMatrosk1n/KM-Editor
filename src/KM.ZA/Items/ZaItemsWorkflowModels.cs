// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Items;

public sealed record ZaItemProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaItemDetail(
    string Label,
    string Value);

public sealed record ZaItemDetailGroup(
    string Label,
    IReadOnlyList<ZaItemDetail> Details);

public sealed record ZaItemMetadata(
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

public sealed record ZaItemRecord(
    int ItemId,
    string Name,
    string Category,
    int BuyPrice,
    int SellPrice,
    int WattsPrice,
    int AlternatePrice,
    IReadOnlyDictionary<string, int?> FieldValues,
    ZaItemMetadata Metadata,
    IReadOnlyList<int> SharedItemIds,
    IReadOnlyList<ZaItemDetailGroup> DetailGroups,
    ZaItemProvenance Provenance);

public sealed record ZaItemEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaItemEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaItemEditableFieldOption> Options);

public sealed record ZaItemsWorkflowStats(
    int TotalItemCount,
    int SourceFileCount);

public sealed record ZaItemsWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaItemRecord> Items,
    IReadOnlyList<ZaItemEditableField> EditableFields,
    ZaItemsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
