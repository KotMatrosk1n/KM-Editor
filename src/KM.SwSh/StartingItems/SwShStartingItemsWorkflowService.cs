// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.StartingItems;

public sealed class SwShStartingItemsWorkflowService
{
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
        var bagHook = bagHookWorkflowService.Load(project);
        diagnostics.AddRange(bagHook.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var royalCandyInstalled = IsRoyalCandyInstalled(bagHook);
        var itemOptions = LoadItemOptions(project, diagnostics, royalCandyInstalled);
        var itemLookup = itemOptions.ToDictionary(item => item.ItemId);

        var bagHookInstalled = IsBagHookInstalledForSlotWrites(bagHook.InstallStatus);
        var installStatus = bagHookInstalled
            ? summary.Availability == SwShWorkflowAvailability.Available ? "available" : "readOnly"
            : "blocked";
        var installMessage = bagHookInstalled
            ? "Starting Items can claim Bag Hook slots 2-20. Slot 1 is never used because it is reserved for Royal Candy."
            : "Install Bag Hook before adding Starting Items.";

        if (!bagHookInstalled)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Starting Items requires Bag Hook V2 before it can write startup item grants.",
                expected: "Installed Bag Hook V2"));
        }

        var grants = bagHook.Slots
            .Where(slot => slot.Slot is >= SwShBagHookAmxPatcher.FirstStartingItemSlot and <= SwShBagHookAmxPatcher.LastStartingItemSlot)
            .Select(slot => ToGrant(slot, itemLookup))
            .ToArray();

        return new SwShStartingItemsWorkflow(
            summary,
            installStatus,
            installMessage,
            grants,
            itemOptions,
            new SwShStartingItemsWorkflowStats(
                grants.Length,
                grants.Count(grant => grant.Status == "occupied"),
                itemOptions.Count,
                SourceFileCount: 2),
            diagnostics);
    }

    private static bool IsBagHookInstalledForSlotWrites(string installStatus)
    {
        return installStatus is SwShBagHookWorkflowService.InstalledStatus
            or SwShBagHookWorkflowService.RepairableStatus;
    }

    internal IReadOnlyDictionary<int, SwShStartingItemOptionRecord> LoadItemOptionLookup(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var bagHook = bagHookWorkflowService.Load(project);
        return LoadItemOptions(project, diagnostics, IsRoyalCandyInstalled(bagHook))
            .ToDictionary(item => item.ItemId);
    }

    internal bool HasInstalledRoyalCandy(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return IsRoyalCandyInstalled(bagHookWorkflowService.Load(project));
    }

    private IReadOnlyList<SwShStartingItemOptionRecord> LoadItemOptions(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        bool royalCandyInstalled)
    {
        try
        {
            return itemsWorkflowService.Load(project).Items
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

        return Array.Empty<SwShStartingItemOptionRecord>();
    }

    internal static bool IsReservedRoyalCandyStartingItem(int itemId, string? itemName = null)
    {
        if (itemId == SwShBagHookAmxPatcher.RoyalCandyItemId)
        {
            return true;
        }

        return IsExpCandyXlName(itemName);
    }

    private static bool IsRoyalCandyInstalled(SwShBagHookWorkflow bagHook)
    {
        var slot1 = bagHook.Slots.FirstOrDefault(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
        return slot1?.Status == "occupied"
            && slot1.ItemId == SwShBagHookAmxPatcher.RoyalCandyItemId;
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
        IReadOnlyDictionary<int, SwShStartingItemOptionRecord> itemLookup)
    {
        var item = slot.ItemId is not null && itemLookup.TryGetValue(slot.ItemId.Value, out var option)
            ? option
            : null;

        return new SwShStartingItemGrantRecord(
            slot.Slot,
            slot.ItemId,
            item?.Name ?? slot.ItemName,
            slot.Quantity ?? 1,
            item?.IsKeyItem ?? false,
            slot.Status,
            slot.Owner,
            new SwShStartingItemsProvenance(
                slot.Provenance.SourceFile,
                slot.Provenance.SourceLayer,
                slot.Provenance.FileState));
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
}
