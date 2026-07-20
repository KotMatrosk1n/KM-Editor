// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Shops;

internal sealed class ZaShopsEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaShopsWorkflowService shopsWorkflowService;

    public ZaShopsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaShopsWorkflowService? shopsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.shopsWorkflowService = shopsWorkflowService ?? new ZaShopsWorkflowService(this.fileSource);
    }

    public ZaShopsEditResult UpdateInventoryItem(
        ProjectPaths paths,
        EditSession? session,
        string shopId,
        int slot,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = shopsWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.ShopsDomain,
                diagnostics))
        {
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        var selectedShop = workflow.Shops.FirstOrDefault(shop => shop.ShopId == shopId);
        if (selectedShop is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{shopId}' is not present in the loaded Pokemon Legends Z-A Shops workflow.",
                field: "shopId",
                expected: "Existing Z-A shop record"));
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, selectedShop, slot, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new ZaShopsEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = shopsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.ShopsDomain,
            diagnostics);

        var validationWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(validationWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                validationWorkflow = OverlayPendingEdit(validationWorkflow, edit);
            }
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Pokemon Legends Z-A Shops change is valid."));
        }

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
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
                expected: "Pending Shops edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        try
        {
            var lineupWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.ShopItemLineupArray,
                session.PendingEdits.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                outputMode);
            var lineupReason = session.PendingEdits.Count == 1
                ? $"Apply pending Shops edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Shops edits.";
            writes.Add(new PlannedFileWrite(
                lineupWriteInfo.TargetRelativePath,
                lineupWriteInfo.Sources,
                lineupWriteInfo.ReplacesExistingOutput,
                lineupReason));

            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides."));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops change plan could not resolve the output target: {exception.Message}",
                file: $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target files."));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Shops change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var shopSource = fileSource.Read(project, ZaDataPaths.ShopItemArray);
            var masterRows = ZaShopsWorkflowService.ReadShopRows(shopSource.Bytes);
            var lineupSource = fileSource.Read(project, ZaDataPaths.ShopItemLineupArray);
            var lineupRows = ZaShopsWorkflowService.ReadLineupRows(lineupSource.Bytes).ToList();

            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(masterRows, lineupRows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(
                paths,
                ZaDataPaths.ShopItemLineupArray,
                ZaShopsWorkflowService.WriteLineupRows(lineupRows),
                outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.ShopItemLineupArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Shops", outputMode)));
        }
        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops output could not be written: {exception.Message}",
                file: $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaShopsWorkflow workflow,
        ZaShopRecord shop,
        int slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var normalizedValue = value.Trim();
        var isSetInventory = string.Equals(normalizedField, ZaShopsWorkflowService.SetInventoryField, StringComparison.Ordinal);
        var isAdd = string.Equals(normalizedField, ZaShopsWorkflowService.AddItemField, StringComparison.Ordinal);
        var isRemove = string.Equals(normalizedField, ZaShopsWorkflowService.RemoveItemField, StringComparison.Ordinal);
        var inventoryItem = shop.Inventory.FirstOrDefault(item => item.Slot == slot);

        if ((isSetInventory || isAdd || isRemove) && !shop.CanEditInventoryOrder)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{shop.Name}' does not support inventory order changes.",
                field: normalizedField,
                expected: "Editable inventory shop"));
            return null;
        }

        if (isSetInventory)
        {
            if (!ValidateInventoryList(workflow, normalizedValue, diagnostics, normalizedField))
            {
                return null;
            }

            return ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.ShopsDomain,
                $"Replace {shop.Name} inventory.",
                new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile),
                CreateRecordId(shop.ShopId, slot),
                normalizedField,
                normalizedValue);
        }

        if (isAdd && (slot < 1 || slot > shop.Inventory.Count + 1))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{shop.Name}' can add inventory at slots 1 through {shop.Inventory.Count + 1}.",
                field: "slot",
                expected: "Safe shop insert slot"));
            return null;
        }

        if (isAdd)
        {
            if (ZaEditSessionSupport.TryParseInt(
                    normalizedValue,
                    ZaShopsWorkflowService.MinimumItemId,
                    ZaShopsWorkflowService.MaximumItemId,
                    normalizedField,
                    ZaEditSessionSupport.ShopsDomain,
                    diagnostics) is not { } addItemId
                || !ValidateKnownItemId(workflow, addItemId, normalizedField, diagnostics))
            {
                return null;
            }

            return ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.ShopsDomain,
                $"Add {FormatKnownItemName(workflow, addItemId)} to {shop.Name} at slot {slot}.",
                new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile),
                CreateRecordId(shop.ShopId, slot),
                normalizedField,
                normalizedValue);
        }

        if (!isAdd && !isSetInventory && inventoryItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{shop.Name}' does not have inventory slot {slot}.",
                field: "slot",
                expected: "Existing shop inventory slot"));
            return null;
        }

        if (isRemove)
        {
            return ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.ShopsDomain,
                $"Remove slot {slot} from {shop.Name}.",
                new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile),
                CreateRecordId(shop.ShopId, slot),
                normalizedField,
                string.Empty);
        }

        var editableField = GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!string.Equals(editableField.ValueKind, "text", StringComparison.Ordinal)
            && ZaEditSessionSupport.TryParseInt(
                normalizedValue,
                editableField.MinimumValue,
                editableField.MaximumValue,
                normalizedField,
                ZaEditSessionSupport.ShopsDomain,
                diagnostics) is null)
        {
            return null;
        }

        if (string.Equals(normalizedField, ZaShopsWorkflowService.ItemIdField, StringComparison.Ordinal)
            && int.TryParse(normalizedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            && !ValidateKnownItemId(workflow, itemId, normalizedField, diagnostics))
        {
            return null;
        }

        if (!FieldMatchesShop(shop, normalizedField))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop field '{normalizedField}' cannot be applied to '{shop.Name}'.",
                field: "field",
                expected: "Field supported by the selected Z-A shop"));
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.ShopsDomain,
            $"Set {shop.Name} slot {slot} {editableField.Label.ToLowerInvariant()} to {normalizedValue}.",
            new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile),
            CreateRecordId(shop.ShopId, slot),
            normalizedField,
            normalizedValue);
    }

    private static void ValidatePendingEdit(
        ZaShopsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Shops.",
                expected: ZaEditSessionSupport.ShopsDomain));
            return;
        }

        if (!TryParseRecordId(edit.RecordId, out var shopId, out var slot)
            || workflow.Shops.FirstOrDefault(shop => shop.ShopId == shopId) is not { } shop)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Legends Z-A Shops edit targets a record that is not loaded.",
                field: "recordId",
                expected: "Existing Z-A shop inventory record"));
            return;
        }

        _ = CreatePendingEdit(workflow, shop, slot, edit.Field ?? string.Empty, edit.NewValue ?? string.Empty, diagnostics);
    }

    private static ZaShopsWorkflow OverlayPendingEdits(ZaShopsWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaShopsWorkflow OverlayPendingEdit(ZaShopsWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal)
            || !TryParseRecordId(edit.RecordId, out var shopId, out var slot))
        {
            return workflow;
        }

        return workflow with
        {
            Shops = workflow.Shops
                .Select(shop => shop.ShopId == shopId ? OverlayShop(workflow, shop, slot, edit) : shop)
                .ToArray(),
        };
    }

    private static ZaShopRecord OverlayShop(
        ZaShopsWorkflow workflow,
        ZaShopRecord shop,
        int slot,
        PendingEdit edit)
    {
        if (edit.Field == ZaShopsWorkflowService.SetInventoryField && shop.CanEditInventoryOrder)
        {
            var nextItems = ParseInventoryList(edit.NewValue ?? string.Empty);
            if (nextItems is null)
            {
                return shop;
            }

            var inventoryByIndex = shop.Inventory.OrderBy(item => item.Slot).ToArray();
            var nextInventory = nextItems
                .Select((itemId, index) =>
                {
                    var source = inventoryByIndex.ElementAtOrDefault(index);
                    var overlay = OverlayInventoryItemId(workflow, source ?? CreatePlaceholderInventoryRecord(index + 1), index + 1, itemId);
                    return OverlayInventoryField(workflow, overlay, ZaShopsWorkflowService.DisplayIndexField, (index + 1).ToString(CultureInfo.InvariantCulture));
                })
                .ToArray();

            return shop with
            {
                Inventory = nextInventory,
                InventorySummary = ZaShopsWorkflowService.FormatInventorySummary(nextInventory),
            };
        }

        if (edit.Field == ZaShopsWorkflowService.AddItemField && shop.CanEditInventoryOrder)
        {
            if (!int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
            {
                return shop;
            }

            var nextInventory = shop.Inventory
                .OrderBy(item => item.Slot)
                .ToList();
            nextInventory.Insert(
                Math.Clamp(slot - 1, 0, nextInventory.Count),
                OverlayInventoryItemId(workflow, CreatePlaceholderInventoryRecord(slot), slot, itemId));
            var renumbered = nextInventory
                .Select((item, index) => OverlayInventoryField(workflow, item with { Slot = index + 1 }, ZaShopsWorkflowService.DisplayIndexField, (index + 1).ToString(CultureInfo.InvariantCulture)))
                .ToArray();
            return shop with
            {
                Inventory = renumbered,
                InventorySummary = ZaShopsWorkflowService.FormatInventorySummary(renumbered),
            };
        }

        if (edit.Field == ZaShopsWorkflowService.RemoveItemField && shop.CanEditInventoryOrder)
        {
            var nextInventory = shop.Inventory
                .Where(item => item.Slot != slot)
                .Select((item, index) => OverlayInventoryField(workflow, item with { Slot = index + 1 }, ZaShopsWorkflowService.DisplayIndexField, (index + 1).ToString(CultureInfo.InvariantCulture)))
                .ToArray();
            return shop with
            {
                Inventory = nextInventory,
                InventorySummary = ZaShopsWorkflowService.FormatInventorySummary(nextInventory),
            };
        }

        return shop with
        {
            Inventory = shop.Inventory
                .Select(item => item.Slot == slot ? OverlayInventoryField(workflow, item, edit.Field ?? string.Empty, edit.NewValue ?? string.Empty) : item)
                .ToArray(),
        };
    }

    private static ZaShopInventoryRecord OverlayInventoryField(
        ZaShopsWorkflow workflow,
        ZaShopInventoryRecord item,
        string field,
        string value)
    {
        if (field == ZaShopsWorkflowService.ItemIdField
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
        {
            return OverlayInventoryItemId(workflow, item, item.Slot, itemId);
        }

        var fieldValues = new Dictionary<string, string>(item.FieldValues, StringComparer.Ordinal)
        {
            [field] = value,
        };
        var fieldDisplayValues = new Dictionary<string, string>(item.FieldDisplayValues, StringComparer.Ordinal)
        {
            [field] = FormatFieldDisplayValue(workflow, field, value),
        };
        return item with
        {
            FieldValues = fieldValues,
            FieldDisplayValues = fieldDisplayValues,
        };
    }

    private static ZaShopInventoryRecord OverlayInventoryItemId(
        ZaShopsWorkflow workflow,
        ZaShopInventoryRecord source,
        int slot,
        int itemId)
    {
        var itemField = GetEditableField(workflow, ZaShopsWorkflowService.ItemIdField);
        var option = itemField?.Options.FirstOrDefault(candidate => candidate.Value == itemId);
        var values = new Dictionary<string, string>(source.FieldValues, StringComparer.Ordinal)
        {
            [ZaShopsWorkflowService.ItemIdField] = itemId.ToString(CultureInfo.InvariantCulture),
        };

        return source with
        {
            Slot = slot,
            ItemId = itemId,
            ItemName = option?.ItemName ?? (itemId == 0 ? "None" : $"Item {itemId.ToString(CultureInfo.InvariantCulture)}"),
            Price = option?.Price ?? 0,
            IsKnownItem = option is not null,
            FieldValues = values,
            FieldDisplayValues = new Dictionary<string, string>(source.FieldDisplayValues, StringComparer.Ordinal)
            {
                [ZaShopsWorkflowService.ItemIdField] = option?.ItemName ?? itemId.ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    private static ZaShopInventoryRecord CreatePlaceholderInventoryRecord(int slot) =>
        new(
            slot,
            0,
            "None",
            0,
            IsKnownItem: true,
            StockLimit: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ZaShopsWorkflowService.ItemIdField] = "0",
                [ZaShopsWorkflowService.DisplayIndexField] = slot.ToString(CultureInfo.InvariantCulture),
                [ZaShopsWorkflowService.ConditionKindField] = "0",
                [ZaShopsWorkflowService.ConditionComparisonField] = "0",
                [ZaShopsWorkflowService.ConditionArgumentsField] = string.Empty,
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ZaShopsWorkflowService.ConditionKindField] = ZaShopsWorkflowService.FormatConditionKind("force_condition"),
                [ZaShopsWorkflowService.ConditionArgumentsField] = ZaShopsWorkflowService.FormatConditionKind("force_condition"),
            },
            [
                ZaShopsWorkflowService.DisplayIndexField,
                ZaShopsWorkflowService.ConditionKindField,
                ZaShopsWorkflowService.ConditionComparisonField,
                ZaShopsWorkflowService.ConditionArgumentsField,
            ],
            PriceField: null,
            CanEditPrice: false);

    private static void ApplyEdit(
        IReadOnlyList<ZaShopsWorkflowService.ShopMasterRow> masterRows,
        List<ZaShopsWorkflowService.ShopLineupRow> lineupRows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseRecordId(edit.RecordId, out var shopId, out var slot)
            || !TryResolveLineupId(masterRows, shopId, out var lineupId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Legends Z-A Shops edit is not valid for apply.",
                expected: "Valid Z-A Shops edit"));
            return;
        }

        var lineup = lineupRows.FirstOrDefault(row => string.Equals(row.Name, lineupId, StringComparison.Ordinal));
        if (lineup is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Legends Z-A shop lineup '{lineupId}' is not present in the source lineup table.",
                field: "lineupId",
                expected: "Existing Z-A shop lineup"));
            return;
        }

        ApplyLineupEdit(lineup, slot, edit, diagnostics);
    }

    private static void ApplyLineupEdit(
        ZaShopsWorkflowService.ShopLineupRow lineup,
        int slot,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var inventory = lineup.Inventory
            .OrderBy(row => row.DisplayIndex)
            .ThenBy(row => row.SourceIndex)
            .ToArray();

        if (edit.Field == ZaShopsWorkflowService.SetInventoryField)
        {
            var itemIds = ParseInventoryList(edit.NewValue ?? string.Empty);
            if (itemIds is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Z-A shop inventory list is not valid for apply.",
                    field: edit.Field,
                    expected: "Comma-separated item IDs"));
                return;
            }

            lineup.Inventory.Clear();
            lineup.Inventory.AddRange(itemIds.Select((itemId, index) =>
            {
                var source = inventory.ElementAtOrDefault(index);
                var conditions = source is null
                    ? CreateDefaultConditions()
                    : CloneConditions(source.Conditions);
                return new ZaShopsWorkflowService.ShopInventoryRow(
                    source?.SourceIndex ?? index,
                    checked((uint)itemId),
                    checked((uint)(index + 1)),
                    conditions);
            }));
            return;
        }

        if (edit.Field == ZaShopsWorkflowService.AddItemField)
        {
            if (TryParseInteger(edit, ZaShopsWorkflowService.MinimumItemId, ZaShopsWorkflowService.MaximumItemId, diagnostics) is not { } itemId)
            {
                return;
            }

            var nextRows = inventory.ToList();
            nextRows.Insert(
                Math.Clamp(slot - 1, 0, nextRows.Count),
                new ZaShopsWorkflowService.ShopInventoryRow(
                    nextRows.Count == 0 ? 0 : nextRows.Max(row => row.SourceIndex) + 1,
                    checked((uint)itemId),
                    checked((uint)slot),
                    CreateDefaultConditions()));
            RewriteLineupInventory(lineup, nextRows);
            return;
        }

        if (edit.Field == ZaShopsWorkflowService.RemoveItemField)
        {
            var nextRows = inventory
                .Where((_, index) => index != slot - 1)
                .ToList();
            RewriteLineupInventory(lineup, nextRows);
            return;
        }

        var row = inventory.ElementAtOrDefault(slot - 1);
        if (row is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Legends Z-A shop lineup '{lineup.Name}' does not contain slot {slot}.",
                field: "slot",
                expected: "Existing shop row"));
            return;
        }

        ApplyField(row, edit, diagnostics);
    }

    private static void ApplyField(
        ZaShopsWorkflowService.ShopInventoryRow row,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        switch (edit.Field)
        {
            case ZaShopsWorkflowService.ItemIdField:
                if (TryParseInteger(edit, ZaShopsWorkflowService.MinimumItemId, ZaShopsWorkflowService.MaximumItemId, diagnostics) is { } itemId)
                {
                    row.ItemId = checked((uint)itemId);
                }
                break;
            case ZaShopsWorkflowService.DisplayIndexField:
                if (TryParseInteger(edit, 0, int.MaxValue, diagnostics) is { } displayIndex)
                {
                    row.DisplayIndex = checked((uint)displayIndex);
                }
                break;
            case ZaShopsWorkflowService.ConditionKindField:
                if (TryParseInteger(edit, 0, 4, diagnostics) is { } conditionKind)
                {
                    row.EnsureFirstCondition().Condition = ZaShopsWorkflowService.ConditionValueToToken(conditionKind);
                }
                break;
            case ZaShopsWorkflowService.ConditionComparisonField:
                if (TryParseInteger(edit, 0, int.MaxValue, diagnostics) is { } comparison)
                {
                    row.EnsureFirstCondition().Comparison = checked((uint)comparison);
                }
                break;
            case ZaShopsWorkflowService.ConditionArgumentsField:
                var condition = row.EnsureFirstCondition();
                condition.Arguments.Clear();
                condition.Arguments.AddRange(ParseArguments(edit.NewValue ?? string.Empty));
                break;
            default:
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? string.Empty));
                break;
        }
    }

    private static void RewriteLineupInventory(
        ZaShopsWorkflowService.ShopLineupRow lineup,
        IReadOnlyList<ZaShopsWorkflowService.ShopInventoryRow> rows)
    {
        lineup.Inventory.Clear();
        lineup.Inventory.AddRange(rows.Select((row, index) =>
        {
            row.DisplayIndex = checked((uint)(index + 1));
            return row;
        }));
    }

    private static bool TryResolveLineupId(
        IReadOnlyList<ZaShopsWorkflowService.ShopMasterRow> masterRows,
        string shopId,
        out string lineupId)
    {
        if (ZaShopsWorkflowService.TryGetLineupShopId(shopId, out lineupId))
        {
            return true;
        }

        if (!ZaShopsWorkflowService.TryGetMasterShopId(shopId, out var masterShopId))
        {
            lineupId = string.Empty;
            return false;
        }

        var master = masterRows.FirstOrDefault(row => string.Equals(row.ShopId, masterShopId, StringComparison.Ordinal));
        if (master is null)
        {
            lineupId = string.Empty;
            return false;
        }

        lineupId = master.LineupId;
        return !string.IsNullOrWhiteSpace(lineupId);
    }

    private static int? TryParseInteger(
        PendingEdit edit,
        int minimumValue,
        int maximumValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            minimumValue,
            maximumValue,
            edit.Field,
            ZaEditSessionSupport.ShopsDomain,
            diagnostics);
    }

    private static bool ValidateInventoryList(
        ZaShopsWorkflow workflow,
        string value,
        ICollection<ValidationDiagnostic> diagnostics,
        string field)
    {
        var itemIds = ParseInventoryList(value);
        if (itemIds is not null
            && itemIds.All(itemId => ValidateKnownItemId(workflow, itemId, field, diagnostics)))
        {
            return true;
        }

        if (itemIds is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop inventory value must be a comma-separated list of item IDs.",
                field: field,
                expected: "Comma-separated item IDs"));
        }

        return false;
    }

    private static bool ValidateKnownItemId(
        ZaShopsWorkflow workflow,
        int itemId,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (itemId == 0 || workflow.KnownItemIds.Contains(itemId))
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Shop item ID {itemId.ToString(CultureInfo.InvariantCulture)} is not a known Pokemon Legends Z-A item.",
            field: field,
            expected: "Known Z-A item ID from Items"));
        return false;
    }

    private static string FormatKnownItemName(ZaShopsWorkflow workflow, int itemId)
    {
        var itemField = GetEditableField(workflow, ZaShopsWorkflowService.ItemIdField);
        return itemField?.Options.FirstOrDefault(option => option.Value == itemId)?.ItemName
            ?? (itemId == 0 ? "None" : $"Item {itemId.ToString(CultureInfo.InvariantCulture)}");
    }

    private static int[]? ParseInventoryList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var values = new List<int>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue)
                || parsedValue < ZaShopsWorkflowService.MinimumItemId
                || parsedValue > ZaShopsWorkflowService.MaximumItemId)
            {
                return null;
            }

            values.Add(parsedValue);
        }

        return values.ToArray();
    }

    private static IReadOnlyList<string> ParseArguments(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
    }

    private static IReadOnlyList<ZaShopsWorkflowService.ShopConditionGroup> CreateDefaultConditions()
    {
        return
        [
            new ZaShopsWorkflowService.ShopConditionGroup(
            [
                new ZaShopsWorkflowService.ShopConditionHolder(
                [
                    new ZaShopsWorkflowService.ShopAppearCondition("force_condition", 0, []),
                ]),
            ]),
        ];
    }

    private static IReadOnlyList<ZaShopsWorkflowService.ShopConditionGroup> CloneConditions(
        IReadOnlyList<ZaShopsWorkflowService.ShopConditionGroup> conditions)
    {
        return conditions
            .Select(group => new ZaShopsWorkflowService.ShopConditionGroup(
                group.Values
                    .Select(holder => new ZaShopsWorkflowService.ShopConditionHolder(
                        holder.Values
                            .Select(condition => new ZaShopsWorkflowService.ShopAppearCondition(
                                condition.Condition,
                                condition.Comparison,
                                condition.Arguments.ToArray()))
                            .ToArray()))
                    .ToArray()))
            .ToArray();
    }

    private static bool FieldMatchesShop(ZaShopRecord shop, string field)
    {
        if (field == ZaShopsWorkflowService.ItemIdField)
        {
            return true;
        }

        return shop.Inventory.Any(item => item.SupportedFields.Contains(field, StringComparer.Ordinal));
    }

    private static ZaShopEditableField? GetEditableField(ZaShopsWorkflow workflow, string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    private static string FormatFieldDisplayValue(ZaShopsWorkflow workflow, string field, string value)
    {
        var editableField = GetEditableField(workflow, field);
        if (editableField?.Options.FirstOrDefault(option =>
                string.Equals(option.Value.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal)) is { } option)
        {
            return option.ItemName;
        }

        return value;
    }

    private static string CreateRecordId(string shopId, int slot) =>
        ZaShopsWorkflowService.CreateInventoryRecordId(shopId, slot);

    private static bool TryParseRecordId(string? recordId, out string shopId, out int slot) =>
        ZaShopsWorkflowService.TryParseInventoryRecordId(recordId, out shopId, out slot);

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Pokemon Legends Z-A Shops field '{field}' is not supported.",
            field: "field",
            expected: "Supported Z-A Shops field");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            severity,
            message,
            ZaEditSessionSupport.ShopsDomain,
            file,
            field,
            expected);
    }
}
