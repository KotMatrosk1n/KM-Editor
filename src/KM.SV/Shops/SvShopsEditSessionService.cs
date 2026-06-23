// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Shops;

internal sealed class SvShopsEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvShopsWorkflowService shopsWorkflowService;

    public SvShopsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvShopsWorkflowService? shopsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.shopsWorkflowService = shopsWorkflowService ?? new SvShopsWorkflowService(this.fileSource);
    }

    public SvShopsEditResult UpdateInventoryItem(
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

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.ShopsDomain,
                diagnostics))
        {
            return new SvShopsEditResult(workflow, currentSession, diagnostics);
        }

        var selectedShop = workflow.Shops.FirstOrDefault(shop => shop.ShopId == shopId);
        if (selectedShop is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{shopId}' is not present in the loaded S/V Shops workflow.",
                field: "shopId",
                expected: "Existing S/V shop record"));
            return new SvShopsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, selectedShop, slot, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvShopsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SvShopsEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = shopsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.ShopsDomain,
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
                "Pending S/V Shops change is valid."));
        }

        return new SvEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending S/V Shops edit before reviewing a change plan.",
                expected: "Pending S/V Shops edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        try
        {
            foreach (var virtualPath in GetTouchedVirtualPaths(session).Order(StringComparer.Ordinal))
            {
                var writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    virtualPath,
                    GetSourcesForVirtualPath(session, virtualPath),
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    writeInfo.TargetRelativePath,
                    writeInfo.Sources,
                    writeInfo.ReplacesExistingOutput,
                    $"Apply pending S/V Shops edits to {virtualPath}."));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                var descriptorWriteInfo = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Scarlet/Violet Trinity descriptor for standalone LayeredFS overrides."));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V Shops change plan could not resolve the output target: {exception.Message}",
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
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed S/V Shops change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var friendlySource = fileSource.Read(project, SvDataPaths.FriendlyShopLineupDataArray);
            var friendlyRows = SvShopsWorkflowService.ReadFriendlyRows(friendlySource.Bytes).ToList();
            var tmSource = fileSource.Read(project, SvDataPaths.ShopWazaMachineDataArray);
            var tmRows = SvShopsWorkflowService.ReadTechnicalMachineRows(tmSource.Bytes).ToList();

            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(friendlyRows, tmRows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var touchedPaths = GetTouchedVirtualPaths(session).ToHashSet(StringComparer.Ordinal);
            if (touchedPaths.Contains(SvDataPaths.FriendlyShopLineupDataArray))
            {
                SvWorkflowFileSource.Write(
                    paths,
                    SvDataPaths.FriendlyShopLineupDataArray,
                    SvShopsWorkflowService.WriteFriendlyRows(friendlyRows),
                    outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.FriendlyShopLineupDataArray, outputMode));
            }

            if (touchedPaths.Contains(SvDataPaths.ShopWazaMachineDataArray))
            {
                SvWorkflowFileSource.Write(
                    paths,
                    SvDataPaths.ShopWazaMachineDataArray,
                    SvShopsWorkflowService.WriteTechnicalMachineRows(tmRows),
                    outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.ShopWazaMachineDataArray, outputMode));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                SvEditSessionSupport.CreateApplyOutputMessage("S/V Shops", outputMode)));
        }
        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V Shops output could not be written: {exception.Message}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SvShopsWorkflow workflow,
        SvShopRecord shop,
        int slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var normalizedValue = value.Trim();
        var isSetInventory = string.Equals(normalizedField, SvShopsWorkflowService.SetInventoryField, StringComparison.Ordinal);
        var isAdd = string.Equals(normalizedField, SvShopsWorkflowService.AddItemField, StringComparison.Ordinal);
        var isRemove = string.Equals(normalizedField, SvShopsWorkflowService.RemoveItemField, StringComparison.Ordinal);
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
            if (!ValidateInventoryList(normalizedValue, diagnostics, normalizedField))
            {
                return null;
            }

            return SvEditSessionSupport.CreatePendingEdit(
                SvEditSessionSupport.ShopsDomain,
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
            return SvEditSessionSupport.CreatePendingEdit(
                SvEditSessionSupport.ShopsDomain,
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

        var isTextField = string.Equals(editableField.ValueKind, "text", StringComparison.Ordinal);
        if (!isTextField)
        {
            if (SvEditSessionSupport.TryParseInt(
                    normalizedValue,
                    editableField.MinimumValue,
                    editableField.MaximumValue,
                    normalizedField,
                    SvEditSessionSupport.ShopsDomain,
                    diagnostics) is null)
            {
                return null;
            }
        }

        if (!FieldMatchesShop(shop, normalizedField))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop field '{normalizedField}' cannot be applied to '{shop.Name}'.",
                field: "field",
                expected: "Field supported by the selected S/V shop"));
            return null;
        }

        var displayValue = isTextField ? normalizedValue : normalizedValue;
        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.ShopsDomain,
            $"Set {shop.Name} slot {slot} {editableField.Label.ToLowerInvariant()} to {displayValue}.",
            new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile),
            CreateRecordId(shop.ShopId, slot),
            normalizedField,
            normalizedValue);
    }

    private static void ValidatePendingEdit(
        SvShopsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.ShopsDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Shops.",
                expected: SvEditSessionSupport.ShopsDomain));
            return;
        }

        if (!TryParseRecordId(edit.RecordId, out var shopId, out var slot)
            || workflow.Shops.FirstOrDefault(shop => shop.ShopId == shopId) is not { } shop)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending S/V Shops edit targets a record that is not loaded.",
                field: "recordId",
                expected: "Existing S/V shop inventory record"));
            return;
        }

        _ = CreatePendingEdit(workflow, shop, slot, edit.Field ?? string.Empty, edit.NewValue ?? string.Empty, diagnostics);
    }

    private static SvShopsWorkflow OverlayPendingEdits(SvShopsWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SvShopsWorkflow OverlayPendingEdit(SvShopsWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.ShopsDomain, StringComparison.Ordinal)
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

    private static SvShopRecord OverlayShop(
        SvShopsWorkflow workflow,
        SvShopRecord shop,
        int slot,
        PendingEdit edit)
    {
        if (edit.Field == SvShopsWorkflowService.SetInventoryField && shop.CanEditInventoryOrder)
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
                    return OverlayInventoryItemId(workflow, source ?? CreatePlaceholderInventoryRecord(index + 1), index + 1, itemId);
                })
                .ToArray();

            return shop with
            {
                Inventory = nextInventory,
                InventorySummary = SvShopsWorkflowService.FormatInventorySummary(nextInventory),
            };
        }

        if (edit.Field == SvShopsWorkflowService.RemoveItemField && shop.CanEditInventoryOrder)
        {
            var nextInventory = shop.Inventory
                .Where(item => item.Slot != slot)
                .Select((item, index) => item with { Slot = index + 1 })
                .ToArray();
            return shop with
            {
                Inventory = nextInventory,
                InventorySummary = SvShopsWorkflowService.FormatInventorySummary(nextInventory),
            };
        }

        if (edit.Field == SvShopsWorkflowService.AddItemField && shop.CanEditInventoryOrder
            && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var addedItemId))
        {
            var nextInventory = shop.Inventory.ToList();
            var insertIndex = Math.Clamp(slot - 1, 0, nextInventory.Count);
            nextInventory.Insert(insertIndex, OverlayInventoryItemId(
                workflow,
                CreatePlaceholderInventoryRecord(slot),
                slot,
                addedItemId));
            var reindexed = nextInventory.Select((item, index) => item with { Slot = index + 1 }).ToArray();
            return shop with
            {
                Inventory = reindexed,
                InventorySummary = SvShopsWorkflowService.FormatInventorySummary(reindexed),
            };
        }

        var overlayedInventory = shop.Inventory
            .Select(item => item.Slot == slot ? OverlayInventoryField(workflow, item, edit.Field, edit.NewValue ?? string.Empty) : item)
            .ToArray();

        return shop with
        {
            Inventory = overlayedInventory,
            InventorySummary = SvShopsWorkflowService.FormatInventorySummary(overlayedInventory),
        };
    }

    private static SvShopInventoryRecord OverlayInventoryField(
        SvShopsWorkflow workflow,
        SvShopInventoryRecord item,
        string? field,
        string value)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return item;
        }

        var next = item;
        if (field == SvShopsWorkflowService.ItemIdField
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
        {
            next = OverlayInventoryItemId(workflow, item, item.Slot, itemId);
        }
        else if (field == SvShopsWorkflowService.LpCostField
            && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var price))
        {
            next = item with { Price = price };
        }

        if (!item.SupportedFields.Contains(field, StringComparer.Ordinal))
        {
            return next;
        }

        var fieldValues = new Dictionary<string, string>(next.FieldValues, StringComparer.Ordinal)
        {
            [field] = value,
        };
        var fieldDisplayValues = new Dictionary<string, string>(next.FieldDisplayValues, StringComparer.Ordinal)
        {
            [field] = FormatFieldDisplayValue(workflow, field, value),
        };

        return next with
        {
            FieldValues = fieldValues,
            FieldDisplayValues = fieldDisplayValues,
        };
    }

    private static SvShopInventoryRecord OverlayInventoryItemId(
        SvShopsWorkflow workflow,
        SvShopInventoryRecord source,
        int slot,
        int itemId)
    {
        var itemField = GetEditableField(workflow, SvShopsWorkflowService.ItemIdField);
        var option = itemField?.Options.FirstOrDefault(candidate => candidate.Value == itemId);
        var values = new Dictionary<string, string>(source.FieldValues, StringComparer.Ordinal)
        {
            [SvShopsWorkflowService.ItemIdField] = itemId.ToString(CultureInfo.InvariantCulture),
        };

        return source with
        {
            Slot = slot,
            ItemId = itemId,
            ItemName = option?.ItemName ?? (itemId == 0 ? "None" : $"Item {itemId.ToString(CultureInfo.InvariantCulture)}"),
            Price = source.PriceField == SvShopsWorkflowService.LpCostField
                ? source.Price
                : option?.Price ?? 0,
            IsKnownItem = option is not null,
            FieldValues = values,
            FieldDisplayValues = new Dictionary<string, string>(source.FieldDisplayValues, StringComparer.Ordinal)
            {
                [SvShopsWorkflowService.ItemIdField] = option?.ItemName ?? itemId.ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    private static SvShopInventoryRecord CreatePlaceholderInventoryRecord(int slot) =>
        new(
            slot,
            0,
            "None",
            0,
            IsKnownItem: true,
            StockLimit: null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SvShopsWorkflowService.ItemIdField] = "0",
                [SvShopsWorkflowService.SortOrderField] = (slot - 1).ToString(CultureInfo.InvariantCulture),
                [SvShopsWorkflowService.ConditionKindField] = ((int)CondEnum.NONE).ToString(CultureInfo.InvariantCulture),
                [SvShopsWorkflowService.ConditionValueField] = string.Empty,
                [SvShopsWorkflowService.GymBadgeCountField] = "0",
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SvShopsWorkflowService.ConditionKindField] = "None",
            },
            [
                SvShopsWorkflowService.SortOrderField,
                SvShopsWorkflowService.ConditionKindField,
                SvShopsWorkflowService.ConditionValueField,
                SvShopsWorkflowService.GymBadgeCountField,
            ],
            PriceField: null,
            CanEditPrice: true);

    private static void ApplyEdit(
        List<SvShopsWorkflowService.FriendlyShopRow> friendlyRows,
        List<SvShopsWorkflowService.TechnicalMachineRow> tmRows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseRecordId(edit.RecordId, out var shopId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending S/V Shops edit is not valid for apply.",
                expected: "Valid S/V Shops edit"));
            return;
        }

        if (SvShopsWorkflowService.IsFriendlyShopId(shopId, out var lineupId))
        {
            ApplyFriendlyEdit(friendlyRows, lineupId, slot, edit, diagnostics);
            return;
        }

        if (SvShopsWorkflowService.IsTechnicalMachineShopId(shopId, out var region))
        {
            ApplyTechnicalMachineEdit(tmRows, region, slot, edit, diagnostics);
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Pending S/V Shops edit targets unknown shop '{shopId}'.",
            field: "shopId",
            expected: "Known S/V shop"));
    }

    private static void ApplyFriendlyEdit(
        List<SvShopsWorkflowService.FriendlyShopRow> rows,
        string lineupId,
        int slot,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var group = rows
            .Where(row => string.Equals(row.LineupId, lineupId, StringComparison.Ordinal))
            .OrderBy(row => row.SortNum)
            .ThenBy(row => row.SourceIndex)
            .ToArray();

        if (edit.Field == SvShopsWorkflowService.SetInventoryField)
        {
            var itemIds = ParseInventoryList(edit.NewValue ?? string.Empty);
            if (itemIds is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending S/V shop inventory list is not valid for apply.",
                    field: edit.Field,
                    expected: "Comma-separated item IDs"));
                return;
            }

            rows.RemoveAll(row => string.Equals(row.LineupId, lineupId, StringComparison.Ordinal));
            var nextRows = itemIds.Select((itemId, index) =>
            {
                var source = group.ElementAtOrDefault(index);
                return new SvShopsWorkflowService.FriendlyShopRow(
                    source?.SourceIndex ?? index,
                    lineupId,
                    index,
                    itemId,
                    source?.ConditionKind ?? CondEnum.NONE,
                    source?.ConditionValue ?? string.Empty,
                    source?.GymBadgeNum ?? 0);
            });
            rows.AddRange(nextRows);
            return;
        }

        var row = group.ElementAtOrDefault(slot - 1);
        if (row is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V shop lineup '{lineupId}' does not contain slot {slot}.",
                field: "slot",
                expected: "Existing friendly shop row"));
            return;
        }

        var updatedRow = row;
        switch (edit.Field)
        {
            case SvShopsWorkflowService.ItemIdField:
                if (TryParseInteger(edit, 0, ushort.MaxValue, diagnostics) is { } itemId)
                {
                    updatedRow = row with { ItemId = itemId };
                }
                break;
            case SvShopsWorkflowService.SortOrderField:
                if (TryParseInteger(edit, 0, int.MaxValue, diagnostics) is { } sortOrder)
                {
                    updatedRow = row with { SortNum = sortOrder };
                }
                break;
            case SvShopsWorkflowService.ConditionKindField:
                if (TryParseInteger(edit, 0, 3, diagnostics) is { } conditionKind)
                {
                    updatedRow = row with { ConditionKind = (CondEnum)conditionKind };
                }
                break;
            case SvShopsWorkflowService.ConditionValueField:
                updatedRow = row with { ConditionValue = edit.NewValue ?? string.Empty };
                break;
            case SvShopsWorkflowService.GymBadgeCountField:
                if (TryParseInteger(edit, 0, 8, diagnostics) is { } badgeCount)
                {
                    updatedRow = row with { GymBadgeNum = badgeCount };
                }
                break;
            default:
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? string.Empty));
                return;
        }

        ReplaceFriendlyRow(rows, row, updatedRow);
    }

    private static void ApplyTechnicalMachineEdit(
        List<SvShopsWorkflowService.TechnicalMachineRow> rows,
        AddRegion region,
        int slot,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var group = rows
            .Where(row => row.Region == region)
            .OrderBy(row => row.WazaItemId)
            .ThenBy(row => row.SourceIndex)
            .ToArray();
        var row = group.ElementAtOrDefault(slot - 1);
        if (row is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V TM Machine region '{SvShopsWorkflowService.FormatRegion(region)}' does not contain slot {slot}.",
                field: "slot",
                expected: "Existing TM Machine row"));
            return;
        }

        var updatedRow = row;
        switch (edit.Field)
        {
            case SvShopsWorkflowService.ItemIdField:
                if (TryParseInteger(edit, 0, ushort.MaxValue, diagnostics) is { } itemId)
                {
                    updatedRow = row with { WazaItemId = itemId };
                }
                break;
            case SvShopsWorkflowService.MoveIdField:
                if (TryParseInteger(edit, 0, ushort.MaxValue, diagnostics) is { } moveId)
                {
                    updatedRow = row with { MoveId = moveId };
                }
                break;
            case SvShopsWorkflowService.LpCostField:
                if (TryParseInteger(edit, 0, int.MaxValue, diagnostics) is { } lpCost)
                {
                    updatedRow = row with { LpCost = lpCost };
                }
                break;
            case SvShopsWorkflowService.ConditionKindField:
                if (TryParseInteger(edit, 0, 3, diagnostics) is { } conditionKind)
                {
                    updatedRow = row with { ConditionKind = (CondEnum)conditionKind };
                }
                break;
            case SvShopsWorkflowService.ConditionValueField:
                updatedRow = row with { ConditionValue = edit.NewValue ?? string.Empty };
                break;
            case SvShopsWorkflowService.Material1ItemIdField:
                updatedRow = ApplyMaterialItem(row, edit, 1, diagnostics);
                break;
            case SvShopsWorkflowService.Material1CountField:
                updatedRow = ApplyMaterialCount(row, edit, 1, diagnostics);
                break;
            case SvShopsWorkflowService.Material1DevNoField:
                updatedRow = ApplyMaterialDevNo(row, edit, 1, diagnostics);
                break;
            case SvShopsWorkflowService.Material2ItemIdField:
                updatedRow = ApplyMaterialItem(row, edit, 2, diagnostics);
                break;
            case SvShopsWorkflowService.Material2CountField:
                updatedRow = ApplyMaterialCount(row, edit, 2, diagnostics);
                break;
            case SvShopsWorkflowService.Material2DevNoField:
                updatedRow = ApplyMaterialDevNo(row, edit, 2, diagnostics);
                break;
            case SvShopsWorkflowService.Material3ItemIdField:
                updatedRow = ApplyMaterialItem(row, edit, 3, diagnostics);
                break;
            case SvShopsWorkflowService.Material3CountField:
                updatedRow = ApplyMaterialCount(row, edit, 3, diagnostics);
                break;
            case SvShopsWorkflowService.Material3DevNoField:
                updatedRow = ApplyMaterialDevNo(row, edit, 3, diagnostics);
                break;
            case SvShopsWorkflowService.RegionField:
                if (TryParseInteger(edit, 0, 3, diagnostics) is { } regionValue)
                {
                    updatedRow = row with { Region = (AddRegion)regionValue };
                }
                break;
            default:
                diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? string.Empty));
                return;
        }

        ReplaceTechnicalMachineRow(rows, row, updatedRow);
    }

    private static SvShopsWorkflowService.TechnicalMachineRow ApplyMaterialItem(
        SvShopsWorkflowService.TechnicalMachineRow row,
        PendingEdit edit,
        int materialSlot,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (TryParseInteger(edit, 0, ushort.MaxValue, diagnostics) is not { } value)
        {
            return row;
        }

        return materialSlot switch
        {
            1 => row with { Material1ItemId = value },
            2 => row with { Material2ItemId = value },
            3 => row with { Material3ItemId = value },
            _ => row,
        };
    }

    private static SvShopsWorkflowService.TechnicalMachineRow ApplyMaterialCount(
        SvShopsWorkflowService.TechnicalMachineRow row,
        PendingEdit edit,
        int materialSlot,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (TryParseInteger(edit, 0, int.MaxValue, diagnostics) is not { } value)
        {
            return row;
        }

        return materialSlot switch
        {
            1 => row with { Material1Count = value },
            2 => row with { Material2Count = value },
            3 => row with { Material3Count = value },
            _ => row,
        };
    }

    private static SvShopsWorkflowService.TechnicalMachineRow ApplyMaterialDevNo(
        SvShopsWorkflowService.TechnicalMachineRow row,
        PendingEdit edit,
        int materialSlot,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (TryParseInteger(edit, 0, int.MaxValue, diagnostics) is not { } value)
        {
            return row;
        }

        return materialSlot switch
        {
            1 => row with { Material1DevNo = value },
            2 => row with { Material2DevNo = value },
            3 => row with { Material3DevNo = value },
            _ => row,
        };
    }

    private static int? TryParseInteger(
        PendingEdit edit,
        int minimumValue,
        int maximumValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            minimumValue,
            maximumValue,
            edit.Field,
            SvEditSessionSupport.ShopsDomain,
            diagnostics);
    }

    private static bool ValidateInventoryList(
        string value,
        ICollection<ValidationDiagnostic> diagnostics,
        string field)
    {
        if (ParseInventoryList(value) is not null)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Shop inventory value must be a comma-separated list of item IDs.",
            field: field,
            expected: "Comma-separated item IDs"));
        return false;
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
                || parsedValue < 0
                || parsedValue > ushort.MaxValue)
            {
                return null;
            }

            values.Add(parsedValue);
        }

        return values.ToArray();
    }

    private static bool FieldMatchesShop(SvShopRecord shop, string field)
    {
        if (field == SvShopsWorkflowService.ItemIdField)
        {
            return true;
        }

        if (SvShopsWorkflowService.IsFriendlyShopId(shop.ShopId, out _))
        {
            return SvShopsWorkflowService.IsFriendlyRowField(field);
        }

        return SvShopsWorkflowService.IsTechnicalMachineShopId(shop.ShopId, out _)
            && SvShopsWorkflowService.IsTechnicalMachineRowField(field);
    }

    private static SvShopEditableField? GetEditableField(SvShopsWorkflow workflow, string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    private static string FormatFieldDisplayValue(SvShopsWorkflow workflow, string field, string value)
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
        SvShopsWorkflowService.CreateInventoryRecordId(shopId, slot);

    private static bool TryParseRecordId(string? recordId, out string shopId, out int slot) =>
        SvShopsWorkflowService.TryParseInventoryRecordId(recordId, out shopId, out slot);

    private static IReadOnlyList<string> GetTouchedVirtualPaths(EditSession session)
    {
        return session.PendingEdits
            .Select(edit =>
            {
                if (!TryParseRecordId(edit.RecordId, out var shopId, out _))
                {
                    return null;
                }

                return SvShopsWorkflowService.IsFriendlyShopId(shopId, out _)
                    ? SvDataPaths.FriendlyShopLineupDataArray
                    : SvShopsWorkflowService.IsTechnicalMachineShopId(shopId, out _)
                        ? SvDataPaths.ShopWazaMachineDataArray
                        : null;
            })
            .Where(path => path is not null)
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyList<ProjectFileReference> GetSourcesForVirtualPath(
        EditSession session,
        string virtualPath)
    {
        return session.PendingEdits
            .Where(edit =>
            {
                if (!TryParseRecordId(edit.RecordId, out var shopId, out _))
                {
                    return false;
                }

                return virtualPath == SvDataPaths.FriendlyShopLineupDataArray
                    ? SvShopsWorkflowService.IsFriendlyShopId(shopId, out _)
                    : SvShopsWorkflowService.IsTechnicalMachineShopId(shopId, out _);
            })
            .SelectMany(edit => edit.Sources)
            .Distinct()
            .ToArray();
    }

    private static void ReplaceFriendlyRow(
        List<SvShopsWorkflowService.FriendlyShopRow> rows,
        SvShopsWorkflowService.FriendlyShopRow original,
        SvShopsWorkflowService.FriendlyShopRow replacement)
    {
        var index = rows.IndexOf(original);
        if (index >= 0)
        {
            rows[index] = replacement;
        }
    }

    private static void ReplaceTechnicalMachineRow(
        List<SvShopsWorkflowService.TechnicalMachineRow> rows,
        SvShopsWorkflowService.TechnicalMachineRow original,
        SvShopsWorkflowService.TechnicalMachineRow replacement)
    {
        var index = rows.IndexOf(original);
        if (index >= 0)
        {
            rows[index] = replacement;
        }
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"S/V Shops field '{field}' is not supported.",
            field: "field",
            expected: "Supported S/V Shops field");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            severity,
            message,
            SvEditSessionSupport.ShopsDomain,
            field,
            expected);
    }
}
