// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Items;

public sealed record SwShItemProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShItemDetail(
    string Label,
    string Value);

public sealed record SwShItemDetailGroup(
    string Label,
    IReadOnlyList<SwShItemDetail> Details);

public sealed record SwShItemMetadata(
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
    int FriendshipGain3);

public sealed record SwShItemRecord(
    int ItemId,
    string Name,
    string Category,
    int BuyPrice,
    int SellPrice,
    int WattsPrice,
    int AlternatePrice,
    SwShItemMetadata Metadata,
    IReadOnlyList<int> SharedItemIds,
    IReadOnlyList<SwShItemDetailGroup> DetailGroups,
    SwShItemProvenance Provenance);

public sealed record SwShItemEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShItemEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShItemEditableFieldOption> Options);

public sealed record SwShItemsWorkflowStats(
    int TotalItemCount,
    int SourceFileCount);

public sealed record SwShItemsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShItemRecord> Items,
    IReadOnlyList<SwShItemEditableField> EditableFields,
    SwShItemsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
