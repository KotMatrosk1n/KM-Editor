// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Items;

public sealed record SvItemProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvItemDetail(
    string Label,
    string Value);

public sealed record SvItemDetailGroup(
    string Label,
    IReadOnlyList<SvItemDetail> Details);

public sealed record SvItemMetadata(
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

public sealed record SvItemRecord(
    int ItemId,
    string Name,
    string Category,
    int BuyPrice,
    int SellPrice,
    int WattsPrice,
    int AlternatePrice,
    SvItemMetadata Metadata,
    IReadOnlyList<int> SharedItemIds,
    IReadOnlyList<SvItemDetailGroup> DetailGroups,
    SvItemProvenance Provenance);

public sealed record SvItemEditableFieldOption(
    int Value,
    string Label);

public sealed record SvItemEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvItemEditableFieldOption> Options);

public sealed record SvItemsWorkflowStats(
    int TotalItemCount,
    int SourceFileCount);

public sealed record SvItemsWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvItemRecord> Items,
    IReadOnlyList<SvItemEditableField> EditableFields,
    SvItemsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
