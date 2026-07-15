// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.Items;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.StartingItems;

public sealed class SwShStartingItemsWorkflowService
{
    public const string NoBlockerKind = "none";
    public const string BagHookMissingBlockerKind = "bagHookMissing";
    public const string BagHookDamagedBlockerKind = "bagHookDamaged";
    public const string ItemMetadataUnavailableBlockerKind = "itemMetadataUnavailable";

    private readonly SwShBagHookWorkflowService bagHookWorkflowService;
    private readonly SwShItemsWorkflowService itemsWorkflowService;

    public SwShStartingItemsWorkflowService(
        SwShBagHookWorkflowService? bagHookWorkflowService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.bagHookWorkflowService = bagHookWorkflowService ?? new SwShBagHookWorkflowService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!ProjectGameMetadata.IsSwordShield(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Starting Items is only available for Pokemon Sword and Pokemon Shield projects.",
                    expected: "Pokemon Sword or Pokemon Shield"));
        }

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Starting Items requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShStartingItemsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (!ProjectGameMetadata.IsSwordShield(project.Paths.SelectedGame))
        {
            return new SwShStartingItemsWorkflow(
                summary,
                "blocked",
                "Starting Items is unavailable for this project game.",
                NoBlockerKind,
                Array.Empty<SwShStartingItemGrantRecord>(),
                Array.Empty<SwShStartingItemOptionRecord>(),
                new SwShStartingItemsWorkflowStats(0, 0, 0, 0),
                diagnostics);
        }

        var bagHook = bagHookWorkflowService.Load(project);
        diagnostics.AddRange(bagHook.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var royalCandyInstalled = SwShRoyalCandyCleanup.HasInstalledOwnershipMarker(project);
        var catalog = LoadItemCatalog(project, royalCandyInstalled, diagnostics);
        var itemLookup = catalog.Options.ToDictionary(item => item.ItemId);
        var grants = bagHook.Slots
            .Where(slot => slot.Slot is >= SwShBagHookAmxPatcher.FirstStartingItemSlot and <= SwShBagHookAmxPatcher.LastStartingItemSlot)
            .Select(slot => ToGrant(slot, itemLookup, diagnostics))
            .ToArray();

        var bagHookInstalled = IsBagHookInstalledForSlotWrites(bagHook.InstallStatus);
        var damagedSlots = grants
            .Where(grant => grant.Status is not ("empty" or "occupied"))
            .Select(grant => grant.Slot)
            .ToArray();
        var hasCompleteSlotBank = grants.Length ==
            SwShBagHookAmxPatcher.LastStartingItemSlot - SwShBagHookAmxPatcher.FirstStartingItemSlot + 1;

        string blockerKind;
        string installStatus;
        string installMessage;
        if (!bagHookInstalled)
        {
            blockerKind = bagHook.InstallStatus is "available" or "readOnly"
                ? BagHookMissingBlockerKind
                : BagHookDamagedBlockerKind;
            installStatus = "blocked";
            installMessage = blockerKind == BagHookMissingBlockerKind
                ? "Install Bag Hook before adding Starting Items."
                : "Repair the damaged or incompatible Bag Hook before editing Starting Items.";
            diagnostics.Add(CreateDiagnostic(
                blockerKind == BagHookMissingBlockerKind ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                blockerKind == BagHookMissingBlockerKind
                    ? "Starting Items requires Bag Hook V2 before it can write startup item grants."
                    : "Starting Items cannot trust the current Bag Hook slot bank.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Installed, readable Bag Hook V2"));
        }
        else if (!hasCompleteSlotBank || damagedSlots.Length > 0)
        {
            blockerKind = BagHookDamagedBlockerKind;
            installStatus = "blocked";
            installMessage = "Repair the damaged Bag Hook slots before editing Starting Items.";
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                hasCompleteSlotBank
                    ? $"Starting Items cannot overwrite damaged Bag Hook slot(s): {string.Join(", ", damagedSlots)}."
                    : "Starting Items cannot trust the Bag Hook slot bank because slots 2-20 are incomplete.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Readable empty or occupied Bag Hook slots 2-20"));
        }
        else if (!catalog.MetadataAvailable)
        {
            blockerKind = ItemMetadataUnavailableBlockerKind;
            installStatus = "blocked";
            installMessage = "Repair the item metadata source before editing Starting Items.";
        }
        else
        {
            blockerKind = NoBlockerKind;
            installStatus = summary.Availability == SwShWorkflowAvailability.Available ? "available" : "readOnly";
            installMessage = "Starting Items can claim Bag Hook slots 2-20. Slot 1 is never used because it is reserved for Royal Candy.";
        }

        var consumedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in catalog.Sources)
        {
            AddResolvedPhysicalSource(project, source, consumedSourcePaths);
        }
        var bagSource = ResolveEffectiveReference(project, SwShBagHookWorkflowService.BagEventScriptPath);
        if (bagSource is not null)
        {
            AddResolvedPhysicalSource(project, bagSource, consumedSourcePaths);
        }
        foreach (var markerSource in SwShRoyalCandyCleanup.GetOwnershipMarkerSources(project))
        {
            AddResolvedPhysicalSource(project, markerSource, consumedSourcePaths);
        }

        return new SwShStartingItemsWorkflow(
            summary,
            installStatus,
            installMessage,
            blockerKind,
            grants,
            catalog.Options,
            new SwShStartingItemsWorkflowStats(
                grants.Length,
                grants.Count(grant => grant.Status == "occupied"),
                catalog.Options.Count,
                consumedSourcePaths.Count),
            diagnostics);
    }

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var sources = new List<ProjectFileReference>();
        AddEffectiveSource(project, SwShBagHookWorkflowService.BagEventScriptPath, sources);
        sources.AddRange(ResolveItemSemanticSources(project));
        sources.AddRange(SwShRoyalCandyCleanup.GetOwnershipMarkerSources(project));
        return sources
            .DistinctBy(source => (source.Layer, source.RelativePath), ProjectFileReferenceKeyComparer.Instance)
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    internal bool HasInstalledRoyalCandy(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return SwShRoyalCandyCleanup.HasInstalledOwnershipMarker(project);
    }

    private StartingItemCatalog LoadItemCatalog(
        OpenedProject project,
        bool royalCandyInstalled,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = ResolveItemSemanticSources(project);
        try
        {
            var workflow = itemsWorkflowService.Load(project);
            foreach (var diagnostic in workflow.Diagnostics)
            {
                diagnostics.Add(diagnostic with { Domain = SwShStartingItemsEditSessionService.StartingItemsEditDomain });
            }

            var hasMetadataError = workflow.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (workflow.Items.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Starting Items could not load any item records from item.dat.",
                    file: SwShItemsWorkflowService.ItemDataPath,
                    expected: "Readable Sword/Shield item metadata"));
                hasMetadataError = true;
            }

            var options = workflow.Items
                .Where(item => item.ItemId > 0 && !string.Equals(item.Name, "None", StringComparison.OrdinalIgnoreCase))
                .Where(item => !royalCandyInstalled || !IsReservedRoyalCandyStartingItem(item.ItemId, item.Name))
                .Select(item => new SwShStartingItemOptionRecord(
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
                    "Starting Items did not find any eligible item records in item.dat.",
                    file: SwShItemsWorkflowService.ItemDataPath,
                    expected: "At least one item with a positive item id"));
                hasMetadataError = true;
            }

            return new StartingItemCatalog(options, sources, !hasMetadataError);
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item options could not be loaded: {exception.Message}",
                file: SwShItemsWorkflowService.ItemDataPath,
                expected: "Readable Sword/Shield item metadata"));
            return new StartingItemCatalog(Array.Empty<SwShStartingItemOptionRecord>(), sources, MetadataAvailable: false);
        }
    }

    private static IReadOnlyList<ProjectFileReference> ResolveItemSemanticSources(OpenedProject project)
    {
        var sources = new List<ProjectFileReference>();
        AddEffectiveSource(project, SwShItemsWorkflowService.ItemDataPath, sources);
        AddCommonTextSource(project, "itemname.dat", sources);
        AddCommonTextSource(project, "wazaname.dat", sources);
        return sources
            .DistinctBy(source => (source.Layer, source.RelativePath), ProjectFileReferenceKeyComparer.Instance)
            .ToArray();
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
            && AddEffectiveSource(project, SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.English, fileName), sources))
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

    private static void AddResolvedPhysicalSource(
        OpenedProject project,
        ProjectFileReference source,
        ISet<string> resolvedPaths)
    {
        string? rootPath;
        string relativePath;
        switch (source.Layer)
        {
            case ProjectFileLayer.Base when source.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase):
                rootPath = project.Paths.BaseRomFsPath;
                relativePath = source.RelativePath["romfs/".Length..];
                break;
            case ProjectFileLayer.Base when source.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase):
                rootPath = project.Paths.BaseExeFsPath;
                relativePath = source.RelativePath["exefs/".Length..];
                break;
            case ProjectFileLayer.Layered or ProjectFileLayer.Generated:
                rootPath = project.Paths.OutputRootPath;
                relativePath = source.RelativePath;
                break;
            default:
                return;
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(absolutePath))
        {
            resolvedPaths.Add(absolutePath);
        }
    }

    internal static bool IsReservedRoyalCandyStartingItem(int itemId, string? itemName = null)
    {
        return itemId == SwShBagHookAmxPatcher.RoyalCandyItemId || IsExpCandyXlName(itemName);
    }

    private static bool IsExpCandyXlName(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return false;
        }

        var normalized = new string(
            itemName
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        return normalized is "EXPCANDYXL" or "EXPERIENCECANDYXL";
    }

    private static SwShStartingItemGrantRecord ToGrant(
        SwShBagHookSlotRecord slot,
        IReadOnlyDictionary<int, SwShStartingItemOptionRecord> itemLookup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var malformedActiveGrant = slot.Status is "occupied" or "conflict"
            && (slot.ItemId is not null || slot.Quantity is not null)
            && (slot.ItemId is null or <= 0 || slot.Quantity is null or < 1 or > 999);
        if (malformedActiveGrant)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Bag Hook slot {slot.Slot} contains an invalid active grant (item {slot.ItemId?.ToString(CultureInfo.InvariantCulture) ?? "missing"}, quantity {slot.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "missing"})."),
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Item id greater than 0 and quantity 1-999"));
        }

        var safeItemId = slot.ItemId is > 0 ? slot.ItemId : null;
        var safeQuantity = slot.Quantity is >= 1 and <= 999 ? slot.Quantity.Value : 1;
        var item = safeItemId is not null && itemLookup.TryGetValue(safeItemId.Value, out var option)
            ? option
            : null;
        var itemName = malformedActiveGrant
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Invalid grant (item {slot.ItemId?.ToString(CultureInfo.InvariantCulture) ?? "missing"}, quantity {slot.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "missing"})")
            : item?.Name ?? slot.ItemName;

        return new SwShStartingItemGrantRecord(
            slot.Slot,
            safeItemId,
            itemName,
            safeQuantity,
            item?.IsKeyItem ?? false,
            malformedActiveGrant ? "conflict" : slot.Status,
            slot.Owner,
            new SwShStartingItemsProvenance(
                slot.Provenance.SourceFile,
                slot.Provenance.SourceLayer,
                slot.Provenance.FileState));
    }

    private static bool IsBagHookInstalledForSlotWrites(string installStatus)
    {
        return installStatus is SwShBagHookWorkflowService.InstalledStatus
            or SwShBagHookWorkflowService.RepairableStatus;
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.StartingItems,
            "Starting Items",
            "Requires Bag Hook and uses only slots 2-20. Clear selected slots and apply to remove Starting Items without touching Royal Candy.",
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
            Domain: SwShStartingItemsEditSessionService.StartingItemsEditDomain,
            Expected: expected);
    }

    private sealed record StartingItemCatalog(
        IReadOnlyList<SwShStartingItemOptionRecord> Options,
        IReadOnlyList<ProjectFileReference> Sources,
        bool MetadataAvailable);

    private sealed class ProjectFileReferenceKeyComparer : IEqualityComparer<(ProjectFileLayer Layer, string RelativePath)>
    {
        public static ProjectFileReferenceKeyComparer Instance { get; } = new();

        public bool Equals(
            (ProjectFileLayer Layer, string RelativePath) x,
            (ProjectFileLayer Layer, string RelativePath) y)
        {
            return x.Layer == y.Layer
                && string.Equals(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((ProjectFileLayer Layer, string RelativePath) obj)
        {
            return HashCode.Combine(obj.Layer, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RelativePath));
        }
    }
}
