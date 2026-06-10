// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Shops;

public sealed class SwShShopsEditSessionService
{
    private const int NoneItemId = 0;
    private const string ShopsEditDomain = "workflow.shops";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShShopsWorkflowService shopsWorkflowService;

    public SwShShopsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShShopsWorkflowService? shopsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.shopsWorkflowService = shopsWorkflowService ?? new SwShShopsWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShShopsEditResult UpdateInventoryItem(
        ProjectPaths paths,
        EditSession? session,
        string shopId,
        int slot,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(shopId);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = shopsWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditShops(project, workflow, diagnostics))
        {
            return new SwShShopsEditResult(workflow, currentSession, diagnostics);
        }

        var selectedShop = workflow.Shops.FirstOrDefault(shop => shop.ShopId == shopId);
        if (selectedShop is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{shopId}' is not present in the loaded Shops workflow.",
                field: "shopId",
                expected: "Existing shop record"));
            return new SwShShopsEditResult(workflow, currentSession, diagnostics);
        }

        var normalizedField = field.Trim();
        var isAdd = string.Equals(normalizedField, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal);
        var isSetInventory = string.Equals(normalizedField, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal);
        var inventoryItem = selectedShop.Inventory.FirstOrDefault(item => item.Slot == slot);
        if (isAdd && (slot < 1 || slot > selectedShop.Inventory.Count + 1))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{selectedShop.Name}' can add inventory at slots 1 through {selectedShop.Inventory.Count + 1}.",
                field: "slot",
                expected: "Safe shop insert slot"));
            return new SwShShopsEditResult(workflow, currentSession, diagnostics);
        }

        if (!isAdd && !isSetInventory && inventoryItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{selectedShop.Name}' does not have inventory slot {slot}.",
                field: "slot",
                expected: "Existing shop inventory slot"));
            return new SwShShopsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedShop, slot, inventoryItem, normalizedField, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShShopsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingShopEdit(currentSession, pendingEdit);

        return new SwShShopsEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = shopsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditShops(project, workflow, diagnostics);

        var validationWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(validationWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                validationWorkflow = OverlayPendingEdits(validationWorkflow, [edit]);
            }
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending shop change is valid."));
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Shops edit before reviewing a change plan.",
                expected: "Pending shop edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var shopDataSource = SwShShopsWorkflowService.ResolveShopDataSource(project);
        if (shopDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops change plan could not resolve the source shop table.",
                expected: SwShShopsWorkflowService.ShopDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = SwShShopsWorkflowService.ResolveOutputPath(paths, shopDataSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops apply target must stay inside the configured output root.",
                file: shopDataSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            shopDataSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(shopDataSource.GraphEntry), shopDataSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Shops edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Shops edits.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

        return new ChangePlan(session.Id, [write], diagnostics);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Shops change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var shopDataSource = SwShShopsWorkflowService.ResolveShopDataSource(project);
        if (shopDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops apply could not resolve the source shop table.",
                expected: SwShShopsWorkflowService.ShopDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, shopDataSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var shopData = SwShShopDataFile.Parse(File.ReadAllBytes(shopDataSource.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToShopInventoryEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = shopData.WriteEdits(edits);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, shopDataSource.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Shops change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops source file could not be decoded: {exception.Message}",
                file: shopDataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield shop_data.bin"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops output file could not be written: {exception.Message}",
                file: shopDataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops output file could not be written: {exception.Message}",
                file: shopDataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditShops(
        OpenedProject project,
        SwShShopsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void ValidatePendingEdit(
        SwShShopsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ShopsEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Shops workflow.",
                expected: ShopsEditDomain));
            return;
        }

        if (!SwShShopsWorkflowService.IsEditableField(edit.Field))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shopId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit targets an invalid inventory slot.",
                field: "slot",
                expected: "Shop inventory slot"));
            return;
        }

        var shop = workflow.Shops.FirstOrDefault(candidate => candidate.ShopId == shopId);
        if (shop is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit targets a shop that is not loaded.",
                field: "shopId",
                expected: "Existing shop record"));
            return;
        }

        if (string.Equals(edit.Field, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal))
        {
            TryParseItemIdList(edit.NewValue, diagnostics);
            return;
        }

        if (string.Equals(edit.Field, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal))
        {
            if (slot > shop.Inventory.Count + 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending shop add edit targets an insert slot outside the inventory.",
                    field: "slot",
                    expected: "Safe shop insert slot"));
                return;
            }

            TryParseItemId(edit.NewValue, diagnostics);
            return;
        }

        if (shop.Inventory.All(item => item.Slot != slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit targets a slot that is not loaded.",
                field: "slot",
                expected: "Existing shop inventory slot"));
            return;
        }

        if (!string.Equals(edit.Field, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal))
        {
            TryParseItemId(edit.NewValue, diagnostics);
        }
    }

    private static PendingEdit? CreatePendingEdit(
        SwShShopRecord shop,
        int slot,
        SwShShopInventoryRecord? inventoryItem,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShShopsWorkflowService.IsEditableField(field))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field));
            return null;
        }

        if (string.Equals(field, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal))
        {
            var itemIds = TryParseItemIdList(value, diagnostics);
            if (itemIds is null)
            {
                return null;
            }

            return new PendingEdit(
                ShopsEditDomain,
                $"Set {shop.Name} inventory order to {itemIds.Count} item{(itemIds.Count == 1 ? string.Empty : "s")}.",
                [new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile)],
                RecordId: SwShShopsWorkflowService.CreateInventoryRecordId(shop.ShopId, 1),
                Field: field,
                NewValue: FormatItemIdList(itemIds));
        }

        var isRemove = string.Equals(field, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal);
        var itemId = isRemove
            ? inventoryItem?.ItemId ?? 0
            : TryParseItemId(value, diagnostics);
        if (itemId is null)
        {
            return null;
        }

        var summary = field switch
        {
            var currentField when string.Equals(currentField, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal) =>
                $"Add item ID {itemId.Value} to {shop.Name} slot {slot}.",
            var currentField when string.Equals(currentField, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal) =>
                $"Remove {shop.Name} slot {slot}{FormatInventoryItemSuffix(inventoryItem)}.",
            _ => $"Set {shop.Name} slot {slot} item ID to {itemId.Value}.",
        };

        return new PendingEdit(
            ShopsEditDomain,
            summary,
            [new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile)],
            RecordId: SwShShopsWorkflowService.CreateInventoryRecordId(shop.ShopId, slot),
            Field: field,
            NewValue: itemId.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatInventoryItemSuffix(SwShShopInventoryRecord? inventoryItem)
    {
        return inventoryItem is null
            ? string.Empty
            : $" ({inventoryItem.ItemName})";
    }

    private static int? TryParseItemId(string? value, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || itemId < SwShShopsWorkflowService.MinimumItemId
            || itemId > SwShShopsWorkflowService.MaximumItemId)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop item ID must be between {SwShShopsWorkflowService.MinimumItemId} and {SwShShopsWorkflowService.MaximumItemId}.",
                field: SwShShopsWorkflowService.ItemIdField,
                expected: "Safe shop item ID"));
            return null;
        }

        return itemId;
    }

    private static IReadOnlyList<int>? TryParseItemIdList(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<int>();
        }

        var itemIds = new List<int>();
        foreach (var part in value.Split(',', StringSplitOptions.None))
        {
            var trimmedPart = part.Trim();
            if (trimmedPart.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shop inventory item list contains an empty item ID.",
                    field: SwShShopsWorkflowService.ItemIdField,
                    expected: "Comma-separated shop item IDs"));
                return null;
            }

            var itemId = TryParseItemId(trimmedPart, diagnostics);
            if (itemId is null)
            {
                return null;
            }

            if (itemId.Value != NoneItemId)
            {
                itemIds.Add(itemId.Value);
            }
        }

        return itemIds;
    }

    private static string FormatItemIdList(IReadOnlyList<int> itemIds)
    {
        return string.Join(
            ",",
            itemIds.Select(itemId => itemId.ToString(CultureInfo.InvariantCulture)));
    }

    private static EditSession ReplacePendingShopEdit(EditSession session, PendingEdit pendingEdit)
    {
        if (string.Equals(pendingEdit.Field, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal)
            && SwShShopsWorkflowService.TryParseInventoryRecordId(pendingEdit.RecordId, out var pendingShopId, out _))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit =>
                        !string.Equals(edit.Domain, pendingEdit.Domain, StringComparison.Ordinal)
                        || !SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shopId, out _)
                        || !string.Equals(shopId, pendingShopId, StringComparison.Ordinal))
                    .Append(pendingEdit)
                    .ToArray(),
            };
        }

        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameShopEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameShopEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShShopsWorkflow OverlayPendingEdits(
        SwShShopsWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShShopsWorkflow OverlayPendingEdit(SwShShopsWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ShopsEditDomain, StringComparison.Ordinal)
            || !SwShShopsWorkflowService.IsEditableField(edit.Field)
            || !SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shopId, out var slot))
        {
            return workflow;
        }

        IReadOnlyList<int>? itemIds = null;
        var itemId = 0;
        if (string.Equals(edit.Field, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal))
        {
            itemIds = TryParseItemIdList(edit.NewValue, new List<ValidationDiagnostic>());
            if (itemIds is null)
            {
                return workflow;
            }
        }
        else if (!string.Equals(edit.Field, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal))
        {
            var parsedItemId = TryParseItemId(edit.NewValue, new List<ValidationDiagnostic>());
            if (parsedItemId is null)
            {
                return workflow;
            }

            itemId = parsedItemId.Value;
        }

        var itemOption = ResolveItemOption(workflow, itemId);
        return workflow with
        {
            Shops = workflow.Shops
                .Select(shop => shop.ShopId == shopId
                    ? OverlayShopInventoryItem(shop, slot, itemId, itemIds, itemOption, workflow, edit.Field!)
                    : shop)
                .ToArray(),
        };
    }

    private static SwShShopRecord OverlayShopInventoryItem(
        SwShShopRecord shop,
        int slot,
        int itemId,
        IReadOnlyList<int>? itemIds,
        SwShShopEditableFieldOption? itemOption,
        SwShShopsWorkflow workflow,
        string field)
    {
        var inventory = field switch
        {
            var currentField when string.Equals(currentField, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal) =>
                SetShopInventoryItems(itemIds ?? Array.Empty<int>(), workflow),
            var currentField when string.Equals(currentField, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal) =>
                InsertShopInventoryItem(shop.Inventory, slot, itemId, itemOption),
            var currentField when string.Equals(currentField, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal) =>
                RemoveShopInventoryItem(shop.Inventory, slot),
            _ => shop.Inventory
                .Select(item => item.Slot == slot
                    ? item with
                    {
                        ItemId = itemId,
                        ItemName = itemOption?.ItemName ?? $"Item {itemId}",
                        Price = itemOption?.Price ?? 0,
                        IsKnownItem = itemOption is not null,
                    }
                    : item)
                .ToArray(),
        };

        return shop with
        {
            Inventory = inventory,
            InventorySummary = SwShShopsWorkflowService.FormatInventorySummary(inventory),
        };
    }

    private static SwShShopInventoryRecord[] InsertShopInventoryItem(
        IReadOnlyList<SwShShopInventoryRecord> inventory,
        int slot,
        int itemId,
        SwShShopEditableFieldOption? itemOption)
    {
        if (slot < 1 || slot > inventory.Count + 1)
        {
            return inventory.ToArray();
        }

        var records = inventory.ToList();
        records.Insert(
            slot - 1,
            new SwShShopInventoryRecord(
                slot,
                itemId,
                itemOption?.ItemName ?? $"Item {itemId}",
                itemOption?.Price ?? 0,
                IsKnownItem: itemOption is not null,
                StockLimit: null));

        return RenumberInventory(records);
    }

    private static SwShShopInventoryRecord[] RemoveShopInventoryItem(
        IReadOnlyList<SwShShopInventoryRecord> inventory,
        int slot)
    {
        if (slot < 1 || slot > inventory.Count)
        {
            return inventory.ToArray();
        }

        var records = inventory
            .Where(item => item.Slot != slot)
            .ToList();
        return RenumberInventory(records);
    }

    private static SwShShopInventoryRecord[] SetShopInventoryItems(
        IReadOnlyList<int> itemIds,
        SwShShopsWorkflow workflow)
    {
        return itemIds
            .Where(itemId => itemId != NoneItemId)
            .Select((itemId, index) =>
            {
                var itemOption = ResolveItemOption(workflow, itemId);
                return new SwShShopInventoryRecord(
                    index + 1,
                    itemId,
                    itemOption?.ItemName ?? $"Item {itemId}",
                    itemOption?.Price ?? 0,
                    IsKnownItem: itemOption is not null,
                    StockLimit: null);
            })
            .ToArray();
    }

    private static SwShShopInventoryRecord[] RenumberInventory(
        IReadOnlyList<SwShShopInventoryRecord> inventory)
    {
        return inventory
            .Select((item, index) => item with { Slot = index + 1 })
            .ToArray();
    }

    private static SwShShopEditableFieldOption? ResolveItemOption(SwShShopsWorkflow workflow, int itemId)
    {
        return workflow.EditableFields
            .FirstOrDefault(field => string.Equals(field.Field, SwShShopsWorkflowService.ItemIdField, StringComparison.Ordinal))
            ?.Options
            .FirstOrDefault(option => option.Value == itemId);
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShShopsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static SwShShopInventoryEdit? ToShopInventoryEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var action = edit.Field switch
        {
            SwShShopsWorkflowService.AddItemField => SwShShopInventoryEditAction.Add,
            SwShShopsWorkflowService.RemoveItemField => SwShShopInventoryEditAction.Remove,
            SwShShopsWorkflowService.SetInventoryField => SwShShopInventoryEditAction.Set,
            _ => SwShShopInventoryEditAction.Replace,
        };
        var itemIds = action == SwShShopInventoryEditAction.Set
            ? TryParseItemIdList(edit.NewValue, diagnostics)
            : null;
        var itemId = action == SwShShopInventoryEditAction.Remove || action == SwShShopInventoryEditAction.Set
            ? 0
            : TryParseItemId(edit.NewValue, diagnostics);
        if (itemId is null || (action == SwShShopInventoryEditAction.Set && itemIds is null))
        {
            return null;
        }

        if (!SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shopId, out var slot)
            || !SwShShopsWorkflowService.TryParseShopId(shopId, out var kind, out var hash, out var inventoryIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit does not include a valid shop inventory target.",
                field: "shopId",
                expected: "Existing shop inventory target"));
            return null;
        }

        return new SwShShopInventoryEdit(
            kind,
            hash,
            inventoryIndex,
            Slot: slot - 1,
            itemId.Value,
            action,
            itemIds);
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        if (!reviewedPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedTargets = reviewedPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentTargets = currentPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return reviewedTargets.SequenceEqual(currentTargets, StringComparer.Ordinal);
    }

    private static ApplyResult CreateApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Shop field '{field}' is not supported by the Shops workflow yet.",
            field: "field",
            expected: SwShShopsWorkflowService.ItemIdField);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: ShopsEditDomain,
            Field: field,
            Expected: expected);
    }
}
