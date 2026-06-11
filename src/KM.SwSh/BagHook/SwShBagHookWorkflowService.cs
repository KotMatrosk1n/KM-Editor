// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.BagHook;

public sealed class SwShBagHookWorkflowService
{
    public const string BagEventScriptPath = SwShRoyalCandyWorkflowService.BagEventScriptPath;

    private readonly SwShItemsWorkflowService itemsWorkflowService;

    public SwShBagHookWorkflowService(SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Bag Hook requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShBagHookWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var itemNames = LoadItemNames(project);
        var provenance = CreateMissingProvenance(BagEventScriptPath);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                "disabled",
                "Bag Hook cannot load until the project paths validate.",
                CreateEmptySlotRecords("unavailable", itemNames, provenance),
                sourceFileCount: 0,
                diagnostics);
        }

        var entry = FindEntry(project, BagEventScriptPath);
        if (entry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag-event script is missing.",
                file: BagEventScriptPath,
                expected: "romfs/bin/script/amx/main_event_0020.amx"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Bag Hook cannot inspect slots because the Bag-event script is missing.",
                CreateEmptySlotRecords("unavailable", itemNames, provenance),
                sourceFileCount: 0,
                diagnostics);
        }

        provenance = CreateProvenance(entry);
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag-event script could not be resolved from the project graph.",
                file: entry.RelativePath,
                expected: "Readable Bag-event AMX"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Bag Hook cannot inspect slots because the Bag-event script cannot be read.",
                CreateEmptySlotRecords("unavailable", itemNames, provenance),
                sourceFileCount: 0,
                diagnostics);
        }

        try
        {
            var analysis = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(sourcePath));
            var installStatus = analysis.Kind switch
            {
                SwShBagHookInstallKind.InstalledV2 => "installed",
                SwShBagHookInstallKind.NotInstalled => summary.Availability == SwShWorkflowAvailability.Available ? "available" : "readOnly",
                SwShBagHookInstallKind.LegacySingleGrant => "legacy",
                _ => "blocked",
            };
            if (analysis.Kind == SwShBagHookInstallKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    analysis.Message,
                    file: entry.RelativePath,
                    expected: "Vanilla Bag-event no-op, Bag Hook V2, or recognized legacy Royal Candy grant"));
            }

            return CreateWorkflow(
                summary,
                installStatus,
                analysis.Message,
                analysis.Slots.Select(slot => ToSlotRecord(slot, itemNames, provenance)).ToArray(),
                sourceFileCount: 1,
                diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag-event script could not be inspected: {exception.Message}",
                file: entry.RelativePath,
                expected: "Readable Bag-event AMX"));
            return CreateWorkflow(
                summary,
                "blocked",
                "Bag Hook cannot inspect slots because the Bag-event script could not be read.",
                CreateEmptySlotRecords("unavailable", itemNames, provenance),
                sourceFileCount: 0,
                diagnostics);
        }
    }

    internal static string GetSlotOwner(int slot, int? itemId)
    {
        if (slot == SwShBagHookAmxPatcher.RoyalCandySlot)
        {
            return itemId == SwShBagHookAmxPatcher.RoyalCandyItemId
                ? "Royal Candy"
                : "Reserved for Royal Candy";
        }

        return itemId is null ? "Available for Starting Items" : "Starting Items";
    }

    internal static string GetSlotReservation(int slot)
    {
        return slot == SwShBagHookAmxPatcher.RoyalCandySlot
            ? "Royal Candy"
            : "Starting Items";
    }

    private static SwShBagHookWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        string installStatus,
        string installMessage,
        IReadOnlyList<SwShBagHookSlotRecord> slots,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShBagHookWorkflow(
            summary,
            installStatus,
            installMessage,
            slots,
            new SwShBagHookWorkflowStats(
                slots.Count,
                slots.Count(slot => slot.Status == "occupied"),
                slots.Count(slot => slot.Status == "empty"),
                slots.Count(slot => slot.IsReserved),
                sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<SwShBagHookSlotRecord> CreateEmptySlotRecords(
        string status,
        IReadOnlyDictionary<int, string> itemNames,
        SwShBagHookProvenance provenance)
    {
        return Enumerable.Range(1, SwShBagHookAmxPatcher.SlotCount)
            .Select(slot => ToSlotRecord(
                new SwShBagHookSlotState(slot, status, null, null, "Bag Hook slot is not currently editable."),
                itemNames,
                provenance))
            .ToArray();
    }

    private static SwShBagHookSlotRecord ToSlotRecord(
        SwShBagHookSlotState slot,
        IReadOnlyDictionary<int, string> itemNames,
        SwShBagHookProvenance provenance)
    {
        var itemName = slot.ItemId is not null && itemNames.TryGetValue(slot.ItemId.Value, out var name)
            ? name
            : slot.ItemId is null
                ? "None"
                : string.Create(CultureInfo.InvariantCulture, $"Item {slot.ItemId}");

        return new SwShBagHookSlotRecord(
            slot.Slot,
            slot.Status,
            IsReserved: true,
            GetSlotReservation(slot.Slot),
            slot.ItemId,
            itemName,
            slot.Quantity,
            GetSlotOwner(slot.Slot, slot.ItemId),
            slot.Notes,
            provenance);
    }

    private IReadOnlyDictionary<int, string> LoadItemNames(OpenedProject project)
    {
        try
        {
            return itemsWorkflowService.Load(project).Items.ToDictionary(item => item.ItemId, item => item.Name);
        }
        catch (InvalidDataException)
        {
            return new Dictionary<int, string>();
        }
        catch (IOException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static ProjectFileGraphEntry? FindEntry(OpenedProject project, string relativePath)
    {
        return project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShBagHookProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShBagHookProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShBagHookProvenance CreateMissingProvenance(string relativePath)
    {
        return new SwShBagHookProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.BagHook,
            "Bag Hook",
            "Install first for Royal Candy or Starting Items. Bag Hook grants nothing by itself; uninstall removes dependent Royal Candy and Starting Items output.",
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
            Domain: SwShBagHookEditSessionService.BagHookEditDomain,
            Expected: expected);
    }
}
