// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SwSh.NpcItemGift;

public sealed record SwShNpcItemGiftProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShNpcItemGiftSourceRecord(
    string SourceId,
    string Label,
    string RelativePath,
    string Status,
    SwShNpcItemGiftProvenance Provenance);

public sealed record SwShNpcItemGiftItemOptionRecord(
    int ItemId,
    string Name,
    string Category,
    bool IsKeyItem);

public sealed record SwShNpcItemGiftItemSlotRecord(
    string SlotId,
    string Label,
    int ItemId,
    string ItemName,
    int VanillaItemId,
    string VanillaItemName,
    int ItemCell);

public sealed record SwShNpcItemGiftRecord(
    string GiftId,
    string NpcId,
    string NpcName,
    string Label,
    string Location,
    int DisplayOrder,
    string RelativePath,
    int Quantity,
    int VanillaQuantity,
    int QuantityCell,
    IReadOnlyList<SwShNpcItemGiftItemSlotRecord> Items,
    SwShNpcItemGiftProvenance Provenance);

public sealed record SwShNpcItemGiftNpcGroup(
    string NpcId,
    string NpcName,
    int DisplayOrder,
    IReadOnlyList<SwShNpcItemGiftRecord> Gifts);

public sealed record SwShNpcItemGiftWorkflowStats(
    int NpcCount,
    int GiftCount,
    int SourceFileCount,
    int ItemOptionCount);

public sealed record SwShNpcItemGiftWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShNpcItemGiftNpcGroup> Npcs,
    IReadOnlyList<SwShNpcItemGiftSourceRecord> Sources,
    IReadOnlyList<SwShNpcItemGiftItemOptionRecord> ItemOptions,
    SwShNpcItemGiftWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShNpcItemGiftSelection(
    string GiftId,
    int Quantity,
    IReadOnlyList<SwShNpcItemGiftItemSelection> Items);

public sealed record SwShNpcItemGiftItemSelection(
    string SlotId,
    int ItemId);

public sealed record SwShNpcItemGiftEditResult(
    SwShNpcItemGiftWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed record SwShNpcItemGiftDefinition(
    string GiftId,
    string NpcId,
    string NpcName,
    string Label,
    string Location,
    int DisplayOrder,
    string RelativePath,
    int QuantityCell,
    int Quantity,
    IReadOnlyList<SwShNpcItemGiftItemSlotDefinition> Items,
    ProjectGame? Game = null);

internal sealed record SwShNpcItemGiftItemSlotDefinition(
    string SlotId,
    string Label,
    int ItemCell,
    int ItemId);

internal sealed record SwShNpcItemGiftFileGroup(
    string RelativePath,
    IReadOnlyList<SwShNpcItemGiftSelectionPatch> Selections);

internal sealed record SwShNpcItemGiftSelectionPatch(
    SwShNpcItemGiftDefinition Definition,
    SwShNpcItemGiftSelection Selection);
