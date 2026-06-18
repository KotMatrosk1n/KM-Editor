// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.StartingItems;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using KM.SwSh.Scripts;

namespace KM.SwSh.NpcItemGift;

public sealed class SwShNpcItemGiftWorkflowService
{
    public const string NpcItemGiftEditDomain = "workflow.npcItemGift";

    private readonly SwShBagHookWorkflowService bagHookWorkflowService;
    private readonly SwShItemsWorkflowService itemsWorkflowService;

    public SwShNpcItemGiftWorkflowService(
        SwShBagHookWorkflowService? bagHookWorkflowService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.bagHookWorkflowService = bagHookWorkflowService ?? new SwShBagHookWorkflowService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    internal static IReadOnlyList<SwShNpcItemGiftDefinition> Gifts => SwShNpcItemGiftDefinitions.All;

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShNpcItemGiftWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(project, summary, [], [], [], diagnostics);
        }

        var royalCandyInstalled = IsRoyalCandyInstalled(bagHookWorkflowService.Load(project));
        var itemOptions = LoadItemOptions(project, diagnostics, royalCandyInstalled);
        var itemLookup = itemOptions.ToDictionary(item => item.ItemId);
        var definitions = GetDefinitionsForGame(ResolveGame(project.Paths.SelectedGame));
        var sources = CreateSourceRecords(project, definitions, diagnostics);
        var provenanceByPath = sources.ToDictionary(source => source.RelativePath, source => source.Provenance, StringComparer.Ordinal);
        var sourceBytes = ReadSources(project, sources, diagnostics);
        var gifts = definitions
            .Select(definition => ToGiftRecord(definition, itemLookup, sourceBytes, provenanceByPath, diagnostics))
            .ToArray();

        return CreateWorkflow(project, summary, gifts, sources, itemOptions, diagnostics);
    }

    internal static IReadOnlyList<SwShNpcItemGiftDefinition> GetDefinitionsForGame(ProjectGame game)
    {
        return Gifts
            .Where(gift => gift.Game is null || gift.Game == game)
            .OrderBy(gift => gift.DisplayOrder)
            .ThenBy(gift => gift.GiftId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static SwShNpcItemGiftDefinition? FindGift(string giftId, ProjectGame game)
    {
        return GetDefinitionsForGame(game)
            .FirstOrDefault(gift => string.Equals(gift.GiftId, giftId, StringComparison.Ordinal));
    }

    internal static ProjectGame ResolveGame(ProjectGame? game)
    {
        return game == ProjectGame.Shield ? ProjectGame.Shield : ProjectGame.Sword;
    }

    internal static SwShHyperTrainingWorkflowService.WorkflowFileSource? ResolveWorkflowFile(
        OpenedProject project,
        string relativePath)
    {
        return SwShTypeChartWorkflowService.ResolveWorkflowFile(project, relativePath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShTypeChartWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    private static SwShNpcItemGiftWorkflow CreateWorkflow(
        OpenedProject project,
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShNpcItemGiftRecord> gifts,
        IReadOnlyList<SwShNpcItemGiftSourceRecord> sources,
        IReadOnlyList<SwShNpcItemGiftItemOptionRecord> itemOptions,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var definitions = GetDefinitionsForGame(ResolveGame(project.Paths.SelectedGame));
        if (gifts.Count == 0 && summary.Availability != SwShWorkflowAvailability.Disabled)
        {
            gifts = definitions
                .Select(definition => ToDefaultGiftRecord(definition, new Dictionary<int, SwShNpcItemGiftItemOptionRecord>(), CreateDefaultProvenance(definition.RelativePath)))
                .ToArray();
        }

        var npcs = gifts
            .GroupBy(gift => gift.NpcId, StringComparer.Ordinal)
            .Select(group => new SwShNpcItemGiftNpcGroup(
                group.Key,
                group.First().NpcName,
                group.Min(gift => gift.DisplayOrder),
                group.OrderBy(gift => gift.DisplayOrder).ThenBy(gift => gift.Label, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SwShNpcItemGiftWorkflow(
            summary,
            npcs,
            sources,
            itemOptions,
            new SwShNpcItemGiftWorkflowStats(
                npcs.Length,
                gifts.Count,
                sources.Count(source => source.Status == "available"),
                itemOptions.Count),
            diagnostics);
    }

    private IReadOnlyList<SwShNpcItemGiftItemOptionRecord> LoadItemOptions(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        bool royalCandyInstalled)
    {
        try
        {
            return itemsWorkflowService.Load(project).Items
                .Where(IsSelectableNpcGiftItem)
                .Where(item => !royalCandyInstalled || !SwShStartingItemsWorkflowService.IsReservedRoyalCandyStartingItem(item.ItemId, item.Name))
                .Select(item => new SwShNpcItemGiftItemOptionRecord(
                    item.ItemId,
                    item.Name,
                    item.Category,
                    item.Metadata.Pouch == (int)SwShItemPouch.KeyItems))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ItemId)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item options could not be decoded: {exception.Message}",
                file: SwShItemsWorkflowService.ItemDataPath,
                expected: "Readable item table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item options could not be read: {exception.Message}",
                file: SwShItemsWorkflowService.ItemDataPath,
                expected: "Readable item table"));
        }

        return Array.Empty<SwShNpcItemGiftItemOptionRecord>();
    }

    private static bool IsSelectableNpcGiftItem(SwShItemRecord item)
    {
        if (item.ItemId <= 0)
        {
            return false;
        }

        var name = item.Name.Trim();
        if (name.Length == 0
            || string.Equals(name, "None", StringComparison.OrdinalIgnoreCase)
            || IsGeneratedItemName(name)
            || name.Contains("???", StringComparison.Ordinal)
            || name.Contains("Dummy", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsGeneratedItemName(string name)
    {
        const string Prefix = "Item ";
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = name[Prefix.Length..].Trim();
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private static IReadOnlyList<SwShNpcItemGiftSourceRecord> CreateSourceRecords(
        OpenedProject project,
        IReadOnlyList<SwShNpcItemGiftDefinition> definitions,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return definitions
            .Select(definition => definition.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(relativePath => CreateSourceRecord(project, relativePath, diagnostics))
            .ToArray();
    }

    private static SwShNpcItemGiftSourceRecord CreateSourceRecord(
        OpenedProject project,
        string relativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, relativePath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{Path.GetFileName(relativePath)} is missing. NPC Item Gift can show defaults, but patching needs this AMX file.",
                file: relativePath,
                expected: "Sword/Shield AMX script file"));

            return new SwShNpcItemGiftSourceRecord(
                CreateSourceId(relativePath),
                Path.GetFileName(relativePath),
                relativePath,
                "missing",
                new SwShNpcItemGiftProvenance(
                    relativePath,
                    ProjectFileLayer.Generated,
                    ProjectFileGraphEntryState.BaseOnly));
        }

        return new SwShNpcItemGiftSourceRecord(
            CreateSourceId(relativePath),
            Path.GetFileName(relativePath),
            source.Entry.RelativePath,
            "available",
            CreateProvenance(source.Entry));
    }

    private static IReadOnlyDictionary<string, byte[]> ReadSources(
        OpenedProject project,
        IReadOnlyList<SwShNpcItemGiftSourceRecord> sources,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var sourceRecord in sources.Where(source => source.Status == "available"))
        {
            var source = ResolveWorkflowFile(project, sourceRecord.RelativePath);
            if (source is null)
            {
                continue;
            }

            try
            {
                result[sourceRecord.RelativePath] = File.ReadAllBytes(source.AbsolutePath);
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"{sourceRecord.Label} could not be read: {exception.Message}. Known vanilla values will be shown for that script.",
                    file: sourceRecord.RelativePath,
                    expected: "Readable AMX script file"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"{sourceRecord.Label} could not be read: {exception.Message}. Known vanilla values will be shown for that script.",
                    file: sourceRecord.RelativePath,
                    expected: "Readable AMX script file"));
            }
        }

        return result;
    }

    private static SwShNpcItemGiftRecord ToGiftRecord(
        SwShNpcItemGiftDefinition definition,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup,
        IReadOnlyDictionary<string, byte[]> sourceBytes,
        IReadOnlyDictionary<string, SwShNpcItemGiftProvenance> provenanceByPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var provenance = provenanceByPath.TryGetValue(definition.RelativePath, out var sourceProvenance)
            ? sourceProvenance
            : CreateDefaultProvenance(definition.RelativePath);
        if (!sourceBytes.TryGetValue(definition.RelativePath, out var data))
        {
            return ToDefaultGiftRecord(definition, itemLookup, provenance);
        }

        var quantity = definition.Quantity;
        try
        {
            quantity = SwShAmxCellPatcher.ReadCodeCellInt(data, definition.QuantityCell);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{definition.Label} quantity could not be inspected: {exception.Message}. Known vanilla quantity will be shown.",
                file: definition.RelativePath,
                expected: "Readable AMX quantity cell"));
        }

        var items = definition.Items
            .Select(slot => ToItemSlotRecord(slot, itemLookup, data, definition.RelativePath, definition.Label, diagnostics))
            .ToArray();

        return new SwShNpcItemGiftRecord(
            definition.GiftId,
            definition.NpcId,
            definition.NpcName,
            definition.Label,
            definition.Location,
            definition.DisplayOrder,
            definition.RelativePath,
            quantity,
            definition.Quantity,
            definition.QuantityCell,
            items,
            provenance);
    }

    private static SwShNpcItemGiftRecord ToDefaultGiftRecord(
        SwShNpcItemGiftDefinition definition,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup,
        SwShNpcItemGiftProvenance provenance)
    {
        return new SwShNpcItemGiftRecord(
            definition.GiftId,
            definition.NpcId,
            definition.NpcName,
            definition.Label,
            definition.Location,
            definition.DisplayOrder,
            definition.RelativePath,
            definition.Quantity,
            definition.Quantity,
            definition.QuantityCell,
            definition.Items
                .Select(slot => ToDefaultItemSlotRecord(slot, itemLookup))
                .ToArray(),
            provenance);
    }

    private static SwShNpcItemGiftItemSlotRecord ToItemSlotRecord(
        SwShNpcItemGiftItemSlotDefinition definition,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup,
        byte[] data,
        string relativePath,
        string giftLabel,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemId = definition.ItemId;
        try
        {
            itemId = SwShAmxCellPatcher.ReadCodeCellInt(data, definition.ItemCell);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{giftLabel} item could not be inspected: {exception.Message}. Known vanilla item will be shown.",
                file: relativePath,
                expected: "Readable AMX item cell"));
        }

        var itemName = itemLookup.TryGetValue(itemId, out var currentItem)
            ? currentItem.Name
            : $"Item {itemId}";
        var vanillaName = itemLookup.TryGetValue(definition.ItemId, out var vanillaItem)
            ? vanillaItem.Name
            : definition.Label;

        return new SwShNpcItemGiftItemSlotRecord(
            definition.SlotId,
            definition.Label,
            itemId,
            itemName,
            definition.ItemId,
            vanillaName,
            definition.ItemCell);
    }

    private static SwShNpcItemGiftItemSlotRecord ToDefaultItemSlotRecord(
        SwShNpcItemGiftItemSlotDefinition definition,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup)
    {
        var vanillaName = itemLookup.TryGetValue(definition.ItemId, out var vanillaItem)
            ? vanillaItem.Name
            : definition.Label;

        return new SwShNpcItemGiftItemSlotRecord(
            definition.SlotId,
            definition.Label,
            definition.ItemId,
            vanillaName,
            definition.ItemId,
            vanillaName,
            definition.ItemCell);
    }

    private static bool IsRoyalCandyInstalled(SwShBagHookWorkflow bagHook)
    {
        var slot1 = bagHook.Slots.FirstOrDefault(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
        return slot1?.Status == "occupied"
            && slot1.ItemId == SwShBagHookAmxPatcher.RoyalCandyItemId;
    }

    private static SwShNpcItemGiftProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShNpcItemGiftProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShNpcItemGiftProvenance CreateDefaultProvenance(string relativePath)
    {
        return new SwShNpcItemGiftProvenance(relativePath, ProjectFileLayer.Generated, ProjectFileGraphEntryState.BaseOnly);
    }

    private static string CreateSourceId(string relativePath)
    {
        return Path.GetFileNameWithoutExtension(relativePath);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.NpcItemGift,
            "NPC Item Gift",
            "Advanced editor for fixed NPC, trainer, story, and DLC item gifts.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: NpcItemGiftEditDomain,
            Expected: expected);
    }
}
