// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Scripts;
using KM.SwSh.StartingItems;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;

namespace KM.SwSh.NpcItemGift;

public sealed class SwShNpcItemGiftWorkflowService
{
    public const string NpcItemGiftEditDomain = "workflow.npcItemGift";

    private readonly SwShItemsWorkflowService itemsWorkflowService;

    public SwShNpcItemGiftWorkflowService(
        SwShBagHookWorkflowService? bagHookWorkflowService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        _ = bagHookWorkflowService;
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    internal static IReadOnlyList<SwShNpcItemGiftDefinition> Gifts => SwShNpcItemGiftDefinitions.All;

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift only supports Pokemon Sword and Pokemon Shield projects.",
                    expected: "Pokemon Sword or Pokemon Shield project"));
        }

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
            return CreateWorkflow(summary, [], [], [], diagnostics);
        }

        var definitions = GetDefinitionsForGame(ResolveGame(project.Paths.SelectedGame));
        var royalCandyInstalled = ResolveRoyalCandyOwnership(project, diagnostics);
        var itemOptions = LoadItemOptions(project, diagnostics, royalCandyInstalled);
        var itemLookup = itemOptions.ToDictionary(item => item.ItemId);
        var analyses = definitions
            .GroupBy(definition => definition.RelativePath, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => AnalyzeSource(project, group.Key, group.ToArray(), itemLookup, diagnostics))
            .ToArray();
        var giftsById = analyses
            .SelectMany(analysis => analysis.Gifts)
            .ToDictionary(gift => gift.GiftId, StringComparer.Ordinal);
        var gifts = definitions
            .Where(definition => giftsById.ContainsKey(definition.GiftId))
            .Select(definition => giftsById[definition.GiftId])
            .ToArray();
        var sources = analyses.Select(analysis => analysis.Source).ToArray();

        return CreateWorkflow(summary, gifts, sources, itemOptions, diagnostics);
    }

    internal static IReadOnlyList<SwShNpcItemGiftDefinition> GetDefinitionsForGame(ProjectGame game)
    {
        if (!IsSupportedGame(game))
        {
            return [];
        }

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

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game == ProjectGame.Sword || game == ProjectGame.Shield;
    }

    internal static ProjectGame ResolveGame(ProjectGame? game)
    {
        return game == ProjectGame.Sword || game == ProjectGame.Shield
            ? game.Value
            : throw new ArgumentOutOfRangeException(nameof(game), game, "NPC Item Gift requires Pokemon Sword or Pokemon Shield.");
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

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(
        OpenedProject project,
        IEnumerable<string> changedRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(changedRelativePaths);

        var sources = new List<ProjectFileReference>();
        foreach (var relativePath in changedRelativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddEffectiveSource(project, relativePath, sources);
            AddBaseSource(project, relativePath, sources);
        }

        AddEffectiveSource(project, SwShItemsWorkflowService.ItemDataPath, sources);
        AddCommonTextSource(project, "itemname.dat", sources);
        AddCommonTextSource(project, "wazaname.dat", sources);
        sources.AddRange(SwShRoyalCandyCleanup.GetOwnershipMarkerSources(project));

        return sources
            .DistinctBy(source => (source.Layer, source.RelativePath.ToUpperInvariant()))
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static SwShNpcItemGiftWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShNpcItemGiftRecord> gifts,
        IReadOnlyList<SwShNpcItemGiftSourceRecord> sources,
        IReadOnlyList<SwShNpcItemGiftItemOptionRecord> itemOptions,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var npcs = gifts
            .GroupBy(gift => gift.NpcId, StringComparer.Ordinal)
            .Select(group => new SwShNpcItemGiftNpcGroup(
                group.Key,
                group.First().NpcName,
                group.Min(gift => gift.DisplayOrder),
                group.OrderBy(gift => gift.DisplayOrder)
                    .ThenBy(gift => gift.Label, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
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
                sources.Count(source => source.Status is "available" or "repairable"),
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
            var itemWorkflow = itemsWorkflowService.Load(project);
            foreach (var diagnostic in itemWorkflow.Diagnostics)
            {
                diagnostics.Add(diagnostic with { Domain = NpcItemGiftEditDomain });
            }

            var options = itemWorkflow.Items
                .Where(IsSelectableNpcGiftItem)
                .Where(item => !royalCandyInstalled
                    || !SwShStartingItemsWorkflowService.IsReservedRoyalCandyStartingItem(item.ItemId, item.Name))
                .Select(item => new SwShNpcItemGiftItemOptionRecord(
                    item.ItemId,
                    item.Name,
                    item.Category,
                    item.Metadata.Pouch == (int)SwShItemPouch.KeyItems))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ItemId)
                .ToArray();
            if (options.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift could not load any selectable item metadata.",
                    file: SwShItemsWorkflowService.ItemDataPath,
                    expected: "At least one selectable Sword/Shield item"));
            }

            return options;
        }
        catch (Exception exception) when (exception is InvalidDataException
            or OverflowException
            or IOException
            or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item options could not be loaded: {exception.Message}",
                file: SwShItemsWorkflowService.ItemDataPath,
                expected: "Readable item table"));
            return [];
        }
    }

    private static bool IsSelectableNpcGiftItem(SwShItemRecord item)
    {
        if (item.ItemId <= 0)
        {
            return false;
        }

        var name = item.Name.Trim();
        return name.Length > 0
            && !string.Equals(name, "None", StringComparison.OrdinalIgnoreCase)
            && !IsGeneratedItemName(name)
            && !name.Contains("???", StringComparison.Ordinal)
            && !name.Contains("Dummy", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);
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

    private static bool ResolveRoyalCandyOwnership(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            return SwShRoyalCandyCleanup.HasInstalledOwnershipMarker(project);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Royal Candy ownership markers could not be inspected. Item 1128 is withheld to avoid an ownership conflict.",
                expected: "Readable Royal Candy ownership marker sources"));
            return true;
        }
    }

    private static SourceAnalysis AnalyzeSource(
        OpenedProject project,
        string relativePath,
        IReadOnlyList<SwShNpcItemGiftDefinition> definitions,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, relativePath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{Path.GetFileName(relativePath)} is missing. Its mapped NPC gifts are read-only until the base script is restored.",
                file: relativePath,
                expected: "Sword/Shield AMX script file"));
            var missingProvenance = CreateDefaultProvenance(relativePath);
            return new SourceAnalysis(
                new SwShNpcItemGiftSourceRecord(
                    CreateSourceId(relativePath),
                    Path.GetFileName(relativePath),
                    relativePath,
                    "missing",
                    missingProvenance),
                definitions.Select(definition => ToDefaultGiftRecord(
                    definition,
                    itemLookup,
                    missingProvenance,
                    "missing")).ToArray());
        }

        var provenance = CreateProvenance(source.Entry);
        SwShAmxCellPatcher.SwShAmxCodeCellReader? effectiveReader = null;
        SwShAmxCellPatcher.SwShAmxCodeCellReader? baseReader = null;
        var effectiveReadable = TryOpenCodeCellReader(source.AbsolutePath, out effectiveReader);
        var basePath = ResolveBaseSourcePath(project.Paths, relativePath);
        var effectiveIsBase = basePath is not null
            && string.Equals(
                Path.GetFullPath(source.AbsolutePath),
                Path.GetFullPath(basePath),
                StringComparison.OrdinalIgnoreCase);
        var baseReadable = effectiveIsBase
            ? effectiveReadable
            : basePath is not null && TryOpenCodeCellReader(basePath, out baseReader);
        if (effectiveIsBase)
        {
            baseReader = effectiveReader;
        }

        if (!effectiveReadable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{Path.GetFileName(relativePath)} is unreadable or not a supported 64-bit AMX script. Its mapped gifts are blocked.",
                file: relativePath,
                expected: "Readable Sword/Shield 64-bit AMX script"));
        }
        else if (!baseReadable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{Path.GetFileName(relativePath)} cannot verify its mapped operands against the base script. Its mapped gifts are blocked.",
                file: relativePath,
                expected: "Readable vanilla base AMX script"));
        }

        var gifts = effectiveReader is null
            ? definitions.Select(definition => ToDefaultGiftRecord(
                definition,
                itemLookup,
                provenance,
                "damaged")).ToArray()
            : definitions.Select(definition => AnalyzeGift(
                definition,
                baseReader,
                effectiveReader,
                itemLookup,
                provenance,
                diagnostics)).ToArray();
        var sourceStatus = gifts.Any(gift => gift.Status == "damaged")
            ? "damaged"
            : gifts.Any(gift => gift.Status == "repairable")
                ? "repairable"
                : "available";

        return new SourceAnalysis(
            new SwShNpcItemGiftSourceRecord(
                CreateSourceId(relativePath),
                Path.GetFileName(relativePath),
                source.Entry.RelativePath,
                sourceStatus,
                provenance),
            gifts);
    }

    private static SwShNpcItemGiftRecord AnalyzeGift(
        SwShNpcItemGiftDefinition definition,
        SwShAmxCellPatcher.SwShAmxCodeCellReader? baseReader,
        SwShAmxCellPatcher.SwShAmxCodeCellReader effectiveReader,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup,
        SwShNpcItemGiftProvenance provenance,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var damaged = baseReader is null;
        var companionMismatch = false;
        var quantity = definition.Quantity;

        if (definition.CanEditQuantity)
        {
            if (definition.QuantityCell is not int quantityCell)
            {
                damaged = true;
            }
            else
            {
                damaged |= !BaseOperandMatches(baseReader, quantityCell, definition.Quantity);
                if (effectiveReader.TryReadPackedInt(quantityCell, out var currentQuantity))
                {
                    quantity = currentQuantity;
                    damaged |= currentQuantity <= 0;
                }
                else
                {
                    damaged = true;
                }

                foreach (var companionCell in definition.CompanionQuantityCells)
                {
                    damaged |= !BaseOperandMatches(baseReader, companionCell, definition.Quantity);
                    if (!effectiveReader.TryReadPackedInt(companionCell, out var companionQuantity))
                    {
                        damaged = true;
                    }
                    else if (companionQuantity != quantity)
                    {
                        companionMismatch = true;
                    }
                }
            }
        }
        else if (definition.QuantityCell is not null || definition.CompanionQuantityCells.Count != 0)
        {
            damaged = true;
        }

        var items = new List<SwShNpcItemGiftItemSlotRecord>(definition.Items.Count);
        foreach (var slot in definition.Items)
        {
            damaged |= !BaseOperandMatches(baseReader, slot.ItemCell, slot.ItemId);
            var itemId = slot.ItemId;
            if (effectiveReader.TryReadPackedInt(slot.ItemCell, out var currentItemId))
            {
                itemId = currentItemId;
                damaged |= currentItemId < 0;
            }
            else
            {
                damaged = true;
            }

            foreach (var companionCell in slot.CompanionItemCells)
            {
                damaged |= !BaseOperandMatches(baseReader, companionCell, slot.ItemId);
                if (!effectiveReader.TryReadPackedInt(companionCell, out var companionItemId))
                {
                    damaged = true;
                }
                else if (companionItemId != itemId)
                {
                    companionMismatch = true;
                }
            }

            items.Add(CreateItemSlotRecord(slot, itemId, itemLookup));
        }

        var status = damaged
            ? "damaged"
            : companionMismatch
                ? "repairable"
                : "available";
        if (status == "damaged")
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{definition.Label} has an incompatible mapped operand or an unverified base layout and is blocked from editing.",
                file: definition.RelativePath,
                expected: "Vanilla base packed operands and readable effective packed operands"));
        }
        else if (status == "repairable")
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{definition.Label} has companion operands that disagree with its primary value. Staging this gift will normalize all owned companions.",
                file: definition.RelativePath,
                expected: "Matching primary and companion packed operands"));
        }

        return new SwShNpcItemGiftRecord(
            definition.GiftId,
            definition.NpcId,
            definition.NpcName,
            definition.Label,
            definition.Location,
            definition.DisplayOrder,
            definition.RelativePath,
            status,
            quantity,
            definition.Quantity,
            definition.QuantityCell,
            definition.CanEditQuantity,
            items,
            provenance);
    }

    private static SwShNpcItemGiftRecord ToDefaultGiftRecord(
        SwShNpcItemGiftDefinition definition,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup,
        SwShNpcItemGiftProvenance provenance,
        string status)
    {
        return new SwShNpcItemGiftRecord(
            definition.GiftId,
            definition.NpcId,
            definition.NpcName,
            definition.Label,
            definition.Location,
            definition.DisplayOrder,
            definition.RelativePath,
            status,
            definition.Quantity,
            definition.Quantity,
            definition.QuantityCell,
            definition.CanEditQuantity,
            definition.Items.Select(slot => CreateItemSlotRecord(slot, slot.ItemId, itemLookup)).ToArray(),
            provenance);
    }

    private static SwShNpcItemGiftItemSlotRecord CreateItemSlotRecord(
        SwShNpcItemGiftItemSlotDefinition definition,
        int itemId,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemLookup)
    {
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

    private static bool BaseOperandMatches(
        SwShAmxCellPatcher.SwShAmxCodeCellReader? baseReader,
        int cell,
        int expected)
    {
        return baseReader is not null
            && baseReader.TryReadPackedInt(cell, out var actual)
            && actual == expected;
    }

    private static bool TryOpenCodeCellReader(
        string absolutePath,
        out SwShAmxCellPatcher.SwShAmxCodeCellReader? reader)
    {
        try
        {
            reader = SwShAmxCellPatcher.OpenCodeCellReader(File.ReadAllBytes(absolutePath));
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
            or OverflowException
            or IOException
            or UnauthorizedAccessException)
        {
            reader = null;
            return false;
        }
    }

    private static void AddCommonTextSource(
        OpenedProject project,
        string fileName,
        ICollection<ProjectFileReference> sources)
    {
        var language = SwShGameTextLanguage.Resolve(project.Paths);
        if (AddEffectiveSource(project, SwShGameTextLanguage.CommonMessagePath(language, fileName), sources))
        {
            return;
        }

        if (!string.Equals(language, SwShGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
            && AddEffectiveSource(
                project,
                SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.English, fileName),
                sources))
        {
            return;
        }

        var fallback = project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith($"/common/{fileName}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(entry => ResolveEffectiveReference(project, entry.RelativePath) is not null);
        if (fallback is not null)
        {
            AddEffectiveSource(project, fallback.RelativePath, sources);
        }
    }

    private static bool AddEffectiveSource(
        OpenedProject project,
        string relativePath,
        ICollection<ProjectFileReference> sources)
    {
        var source = ResolveEffectiveReference(project, relativePath);
        if (source is null)
        {
            return false;
        }

        sources.Add(source);
        return true;
    }

    private static bool AddBaseSource(
        OpenedProject project,
        string relativePath,
        ICollection<ProjectFileReference> sources)
    {
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry?.BaseFile is null)
        {
            return false;
        }

        sources.Add(entry.BaseFile);
        return true;
    }

    private static ProjectFileReference? ResolveEffectiveReference(OpenedProject project, string relativePath)
    {
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var absolutePath = SwShBagHookWorkflowService.ResolveSourcePath(project.Paths, entry);
        if (absolutePath is null || !File.Exists(absolutePath))
        {
            return null;
        }

        return new ProjectFileReference(
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.RelativePath);
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string relativePath)
    {
        string? rootPath;
        string pathInsideRoot;
        if (relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            rootPath = paths.BaseRomFsPath;
            pathInsideRoot = relativePath["romfs/".Length..];
        }
        else if (relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            rootPath = paths.BaseExeFsPath;
            pathInsideRoot = relativePath["exefs/".Length..];
        }
        else
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        try
        {
            var fullRoot = Path.GetFullPath(rootPath);
            var candidate = Path.GetFullPath(Path.Combine(
                fullRoot,
                pathInsideRoot.Replace('/', Path.DirectorySeparatorChar)));
            return PathContainment.IsWithinRoot(Path.GetRelativePath(fullRoot, candidate))
                ? candidate
                : null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static SwShNpcItemGiftProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        return new SwShNpcItemGiftProvenance(
            entry.RelativePath,
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.State);
    }

    private static SwShNpcItemGiftProvenance CreateDefaultProvenance(string relativePath)
    {
        return new SwShNpcItemGiftProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
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

    private sealed record SourceAnalysis(
        SwShNpcItemGiftSourceRecord Source,
        IReadOnlyList<SwShNpcItemGiftRecord> Gifts);
}
