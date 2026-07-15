// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Shops;

public sealed class SwShShopsEditSessionService
{
    private const int NoneItemId = 0;
    private const string ShopsEditDomain = "workflow.shops";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShShopsWorkflowService shopsWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShShopsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShShopsWorkflowService? shopsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.shopsWorkflowService = shopsWorkflowService ?? new SwShShopsWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShShopsEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShShopsWorkflowService? shopsWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.shopsWorkflowService = shopsWorkflowService ?? new SwShShopsWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
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

        projectWorkspaceService.ClearMemoryCache();
        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = shopsWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(loadedWorkflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditShops(project, loadedWorkflow, diagnostics))
        {
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (shopId is null || field is null || value is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop ID, field, and value are required.",
                field: shopId is null ? "shopId" : field is null ? "field" : "value",
                expected: "Non-null canonical Shops update input"));
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (!string.Equals(field, field.Trim(), StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop field must use canonical text without surrounding whitespace.",
                field: "field",
                expected: field.Trim()));
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var sourceShop = ResolveShop(loadedWorkflow, shopId, diagnostics, field);
        var effectiveShop = ResolveShop(originalWorkflow, shopId, diagnostics, field);
        if (sourceShop is null || effectiveShop is null)
        {
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var isAdd = string.Equals(field, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal);
        var isSetInventory = string.Equals(field, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal);
        var inventoryItem = effectiveShop.Inventory.FirstOrDefault(item => item.Slot == slot);
        if (isAdd && (slot < 1 || slot > effectiveShop.Inventory.Count + 1))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{effectiveShop.Name}' can add inventory at slots 1 through {effectiveShop.Inventory.Count + 1}.",
                field: "slot",
                expected: "Safe shop insert slot"));
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (!isAdd && !isSetInventory && inventoryItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{effectiveShop.Name}' does not have inventory slot {slot}.",
                field: "slot",
                expected: "Existing shop inventory slot"));
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(
            loadedWorkflow,
            sourceShop,
            effectiveShop,
            slot,
            inventoryItem,
            field,
            value,
            diagnostics);
        if (pendingEdit is null)
        {
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var updatedSession = ReplacePendingShopEdit(originalSession, pendingEdit, loadedWorkflow, sourceShop);
        var updatedWorkflow = OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits);
        var updatedShop = ResolveEquivalentShop(updatedWorkflow, sourceShop);
        if (updatedShop is not null
            && updatedShop.Inventory.Select(item => item.ItemId)
                .SequenceEqual(sourceShop.Inventory.Select(item => item.ItemId)))
        {
            updatedSession = RemovePendingShopEdits(updatedSession, loadedWorkflow, sourceShop);
            updatedWorkflow = OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits);
        }

        ValidateLoadedSession(
            project,
            loadedWorkflow,
            updatedSession,
            diagnostics,
            addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShShopsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShShopsEditResult(
            updatedWorkflow,
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = shopsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (CanEditShops(project, workflow, diagnostics))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);
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

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = shopsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        if (CanEditShops(project, workflow, diagnostics))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);
        }

        var shopEdits = GetShopEdits(session).ToArray();
        if (shopEdits.Length == 0)
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

        var shopDataSource = SwShShopsWorkflowService.ResolveShopDataSource(project);
        if (shopDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops change plan could not resolve the source shop table.",
                expected: SwShShopsWorkflowService.ShopDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, shopDataSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            shopDataSource.GraphEntry.RelativePath,
            shopEdits
                .SelectMany(edit => edit.Sources)
                .Append(new ProjectFileReference(
                    GetSourceLayer(shopDataSource.GraphEntry),
                    shopDataSource.GraphEntry.RelativePath))
                .Distinct()
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            File.Exists(targetPath),
            CreatePlanReason(shopEdits));

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, [write], diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        projectWorkspaceService.ClearMemoryCache();
        try
        {
            return ApplyChangePlanCore(paths, session, reviewedPlan);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Shops change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShShopsWorkflowService.ResolveShopDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops apply could not resolve the source shop table.",
                expected: SwShShopsWorkflowService.ShopDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        byte[] output;
        try
        {
            var shopData = SwShShopDataFile.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = GetShopEdits(session)
                .Select(edit => ToShopInventoryEdit(shopData, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            output = shopData.WriteEdits(edits);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops source file could not be decoded or safely edited: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield shop_data.bin"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops source file could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield shop_data.bin"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (!SwShOutputRollbackScope.TryCapture(
                paths,
                currentPlan.Writes.Select(write => write.TargetRelativePath),
                out var rollbackScope,
                out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        using (var outputRollback = rollbackScope!)
        {
            try
            {
                WriteOutputAtomically(targetPath, output);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, source.GraphEntry.RelativePath));
                outputRollback.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Shops output file could not be written: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Writable output root"));
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
        }

        if (writtenFiles.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Shops change plan to the configured LayeredFS output root."));
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

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShShopsWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var shopEdits = GetShopEdits(session).ToArray();
        var effectiveWorkflow = workflow;
        var touchedShopIds = new HashSet<string>(StringComparer.Ordinal);
        var seenEdits = new HashSet<(string RecordId, string Field)>();

        foreach (var edit in shopEdits)
        {
            var errorsBefore = CountErrors(diagnostics);
            var sourceShop = ValidatePendingEdit(
                workflow,
                effectiveWorkflow,
                edit,
                diagnostics);
            if (sourceShop is not null
                && edit.Field is not null
                && !string.Equals(edit.Field, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal)
                && !string.Equals(edit.Field, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal)
                && SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out _, out var editSlot)
                && !seenEdits.Add((
                    SwShShopsWorkflowService.CreateInventoryRecordId(sourceShop.ShopId, editSlot),
                    edit.Field)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Shop '{sourceShop.Name}' has more than one pending '{edit.Field}' edit for the same slot.",
                    field: edit.Field,
                    expected: "One pending edit per shop slot and action"));
            }

            if (CountErrors(diagnostics) == errorsBefore && sourceShop is not null)
            {
                touchedShopIds.Add(sourceShop.ShopId);
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        foreach (var shopId in touchedShopIds)
        {
            var sourceShop = workflow.Shops.Single(shop => shop.ShopId == shopId);
            var effectiveShop = ResolveEquivalentShop(effectiveWorkflow, sourceShop);
            if (effectiveShop is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending shop edits no longer resolve to exactly one source inventory.",
                    field: "shopId",
                    expected: "Current physical shop inventory"));
                continue;
            }

            if (effectiveShop.Inventory.Select(item => item.ItemId)
                .SequenceEqual(sourceShop.Inventory.Select(item => item.ItemId)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edits for '{sourceShop.Name}' do not change its source inventory.",
                    field: SwShShopsWorkflowService.SetInventoryField,
                    expected: "A non-no-op shop inventory change"));
                continue;
            }

            ValidateItemSemantics(workflow, sourceShop, effectiveShop, diagnostics);
        }

        if (shopEdits.Length > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightShopWrite(project, shopEdits, diagnostics);
        }

        if (addSuccessDiagnostic
            && shopEdits.Length > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending shop change is valid."));
        }
    }

    private static SwShShopRecord? ValidatePendingEdit(
        SwShShopsWorkflow sourceWorkflow,
        SwShShopsWorkflow effectiveWorkflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShShopsWorkflowService.IsEditableField(edit.Field)
            || edit.Field is null
            || !string.Equals(edit.Field, edit.Field.Trim(), StringComparison.Ordinal))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        if (!SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shopId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit targets an invalid inventory slot.",
                field: "slot",
                expected: "Shop inventory slot"));
            return null;
        }

        var sourceShop = ResolveShop(sourceWorkflow, shopId, diagnostics, edit.Field);
        if (sourceShop is null)
        {
            return null;
        }

        var effectiveShop = ResolveEquivalentShop(effectiveWorkflow, sourceShop);
        if (effectiveShop is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit no longer targets the current physical source inventory.",
                field: "shopId",
                expected: "Current physical shop inventory"));
            return null;
        }

        var isSetInventory = string.Equals(
            edit.Field,
            SwShShopsWorkflowService.SetInventoryField,
            StringComparison.Ordinal);
        var isAdd = string.Equals(edit.Field, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal);
        if (isSetInventory)
        {
            if (slot != 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending shop set-inventory edit must use the canonical inventory record slot.",
                    field: "slot",
                    expected: "Slot 1"));
            }

            TryParseItemIdList(edit.NewValue, diagnostics);
        }
        else if (isAdd)
        {
            if (slot < 1 || slot > effectiveShop.Inventory.Count + 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending shop add edit targets an insert slot outside the inventory.",
                    field: "slot",
                    expected: "Safe shop insert slot"));
            }

            TryParseItemId(edit.NewValue, diagnostics);
        }
        else
        {
            if (effectiveShop.Inventory.All(item => item.Slot != slot))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending shop edit targets a slot that is not loaded.",
                    field: "slot",
                    expected: "Existing shop inventory slot"));
            }

            TryParseItemId(edit.NewValue, diagnostics);
        }

        var expectedSources = CreateExpectedSources(sourceWorkflow, sourceShop, edit.Field, edit.NewValue, slot);
        var signedRecord = !SwShShopsWorkflowService.TryParseShopId(
                shopId,
                out _,
                out _,
                out _,
                out _,
                out _,
                out var isLegacy)
            ? false
            : !isLegacy;
        if (!SourcesMatchCurrent(edit.Sources, expectedSources, sourceShop, signedRecord))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The shop or item source layer changed after this edit was staged. Stage the edit again against the current sources.",
                field: edit.Field,
                expected: "Pending edit staged from the current Shops sources"));
        }

        return sourceShop;
    }

    private static PendingEdit? CreatePendingEdit(
        SwShShopsWorkflow sourceWorkflow,
        SwShShopRecord sourceShop,
        SwShShopRecord effectiveShop,
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

            return CreateSetInventoryPendingEdit(
                sourceWorkflow,
                sourceShop,
                itemIds,
                $"Set {effectiveShop.Name} inventory order to {itemIds.Count} item{(itemIds.Count == 1 ? string.Empty : "s")}.",
                slot: 1);
        }

        var isRemove = string.Equals(field, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal);
        var isAdd = string.Equals(field, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal);
        var itemId = isRemove ? inventoryItem?.ItemId : TryParseItemId(value, diagnostics);
        if (itemId is null)
        {
            if (isRemove)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shop remove edit must target an existing inventory item.",
                    field: "slot",
                    expected: "Existing shop inventory slot"));
            }

            return null;
        }

        if (isAdd || isRemove)
        {
            var finalItemIds = effectiveShop.Inventory.Select(item => item.ItemId).ToList();
            if (isAdd)
            {
                finalItemIds.Insert(slot - 1, itemId.Value);
            }
            else
            {
                finalItemIds.RemoveAt(slot - 1);
            }

            var structuralSummary = isAdd
                ? $"Add item ID {itemId.Value} to {effectiveShop.Name} slot {slot}."
                : $"Remove {effectiveShop.Name} slot {slot}{FormatInventoryItemSuffix(inventoryItem)}.";
            return CreateSetInventoryPendingEdit(
                sourceWorkflow,
                sourceShop,
                finalItemIds,
                structuralSummary,
                slot: 1);
        }

        var summary = $"Set {effectiveShop.Name} slot {slot} item ID to {itemId.Value}.";

        var canonicalValue = itemId.Value.ToString(CultureInfo.InvariantCulture);

        return new PendingEdit(
            ShopsEditDomain,
            summary,
            CreateExpectedSources(sourceWorkflow, sourceShop, field, canonicalValue, slot),
            RecordId: SwShShopsWorkflowService.CreateInventoryRecordId(sourceShop.ShopId, slot),
            Field: field,
            NewValue: canonicalValue);
    }

    private static PendingEdit CreateSetInventoryPendingEdit(
        SwShShopsWorkflow sourceWorkflow,
        SwShShopRecord sourceShop,
        IReadOnlyList<int> itemIds,
        string summary,
        int slot)
    {
        var canonicalValue = FormatItemIdList(itemIds);
        return new PendingEdit(
            ShopsEditDomain,
            summary,
            CreateExpectedSources(
                sourceWorkflow,
                sourceShop,
                SwShShopsWorkflowService.SetInventoryField,
                canonicalValue,
                slot),
            RecordId: SwShShopsWorkflowService.CreateInventoryRecordId(sourceShop.ShopId, slot),
            Field: SwShShopsWorkflowService.SetInventoryField,
            NewValue: canonicalValue);
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

        var canonical = itemId.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(value, canonical, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop item ID must use canonical integer text without whitespace or leading zeroes.",
                field: SwShShopsWorkflowService.ItemIdField,
                expected: canonical));
            return null;
        }

        return itemId;
    }

    private static IReadOnlyList<int>? TryParseItemIdList(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (value is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop inventory item list is required.",
                field: SwShShopsWorkflowService.ItemIdField,
                expected: "Canonical comma-separated shop item IDs or an empty string"));
            return null;
        }

        if (value.Length == 0)
        {
            return Array.Empty<int>();
        }

        var itemIds = new List<int>();
        foreach (var part in value.Split(',', StringSplitOptions.None))
        {
            if (part.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shop inventory item list contains an empty item ID.",
                    field: SwShShopsWorkflowService.ItemIdField,
                    expected: "Comma-separated shop item IDs"));
                return null;
            }

            var itemId = TryParseItemId(part, diagnostics);
            if (itemId is null)
            {
                return null;
            }

            if (itemId.Value != NoneItemId)
            {
                itemIds.Add(itemId.Value);
            }
        }

        var canonical = FormatItemIdList(itemIds);
        if (!string.Equals(value, canonical, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop inventory item list must use canonical comma-separated integer text.",
                field: SwShShopsWorkflowService.ItemIdField,
                expected: canonical));
            return null;
        }

        return itemIds;
    }

    private static string FormatItemIdList(IReadOnlyList<int> itemIds)
    {
        return string.Join(
            ",",
            itemIds.Select(itemId => itemId.ToString(CultureInfo.InvariantCulture)));
    }

    private static SwShShopRecord? ResolveShop(
        SwShShopsWorkflow workflow,
        string? shopId,
        ICollection<ValidationDiagnostic>? diagnostics,
        string? field)
    {
        if (!SwShShopsWorkflowService.TryParseShopId(
                shopId,
                out var kind,
                out var sourceIndex,
                out var hash,
                out var inventoryIndex,
                out var sourceIdentity,
                out var isLegacy))
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop ID is not a supported signed or legacy Shops record ID.",
                field: "shopId",
                expected: "Signed physical shop ID or unique legacy shop ID"));
            return null;
        }

        var candidates = workflow.Shops
            .Where(shop => string.Equals(
                    shop.Kind,
                    kind == SwShShopKind.Single ? "Single" : "Multi",
                    StringComparison.Ordinal)
                && string.Equals(shop.SourceHash, $"0x{hash:X16}", StringComparison.OrdinalIgnoreCase)
                && shop.InventoryIndex == inventoryIndex + 1)
            .ToArray();

        if (isLegacy)
        {
            if (candidates.Length == 1)
            {
                return candidates[0];
            }

            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                candidates.Length == 0
                    ? $"Legacy shop '{shopId}' is not present in the loaded Shops workflow."
                    : $"Legacy shop '{shopId}' is ambiguous because multiple physical shops share its hash.",
                field: "shopId",
                expected: "Exactly one physical shop inventory"));
            return null;
        }

        var indexedCandidates = candidates
            .Where(shop => shop.SourceIndex == sourceIndex)
            .ToArray();
        if (indexedCandidates.Length == 1
            && string.Equals(
                indexedCandidates[0].SourceIdentity,
                sourceIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            return indexedCandidates[0];
        }

        diagnostics?.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            indexedCandidates.Length == 1
                ? "The staged shop source record changed. Stage the edit again against the current source identity."
                : "Signed shop ID does not target exactly one current physical shop inventory.",
            field: field ?? "shopId",
            expected: "Current signed physical shop identity"));
        return null;
    }

    private static SwShShopRecord? ResolveEquivalentShop(
        SwShShopsWorkflow workflow,
        SwShShopRecord sourceShop)
    {
        return workflow.Shops.SingleOrDefault(shop => IsSamePhysicalInventory(shop, sourceShop));
    }

    private static bool IsSamePhysicalInventory(SwShShopRecord first, SwShShopRecord second)
    {
        return first.SourceIndex == second.SourceIndex
            && first.InventoryIndex == second.InventoryIndex
            && string.Equals(first.Kind, second.Kind, StringComparison.Ordinal)
            && string.Equals(first.SourceHash, second.SourceHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.SourceIdentity, second.SourceIdentity, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ProjectFileReference> CreateExpectedSources(
        SwShShopsWorkflow workflow,
        SwShShopRecord sourceShop,
        string field,
        string? value,
        int slot)
    {
        var sources = new List<ProjectFileReference>
        {
            new(sourceShop.Provenance.SourceLayer, sourceShop.Provenance.SourceFile),
        };

        var sourceIds = sourceShop.Inventory.Select(item => item.ItemId).ToList();
        var candidateIds = sourceIds.ToList();
        var scratchDiagnostics = new List<ValidationDiagnostic>();
        if (field == SwShShopsWorkflowService.SetInventoryField)
        {
            candidateIds = TryParseItemIdList(value, scratchDiagnostics)?.ToList() ?? candidateIds;
        }
        else if (field == SwShShopsWorkflowService.AddItemField)
        {
            var itemId = TryParseItemId(value, scratchDiagnostics);
            if (itemId is not null && slot >= 1 && slot <= candidateIds.Count + 1)
            {
                candidateIds.Insert(slot - 1, itemId.Value);
            }
        }
        else if (field == SwShShopsWorkflowService.ItemIdField)
        {
            var itemId = TryParseItemId(value, scratchDiagnostics);
            if (itemId is not null && slot >= 1 && slot <= candidateIds.Count)
            {
                candidateIds[slot - 1] = itemId.Value;
            }
        }

        if (IntroducesItemIds(sourceIds, candidateIds)
            && workflow.ItemSemanticSource is { } itemSource)
        {
            sources.Add(itemSource);
        }

        return sources.Distinct().ToArray();
    }

    private static bool IntroducesItemIds(IReadOnlyList<int> sourceIds, IReadOnlyList<int> candidateIds)
    {
        var sourceCounts = sourceIds
            .Where(itemId => itemId != NoneItemId)
            .GroupBy(itemId => itemId)
            .ToDictionary(group => group.Key, group => group.Count());
        return candidateIds
            .Where(itemId => itemId != NoneItemId)
            .GroupBy(itemId => itemId)
            .Any(group => !sourceCounts.TryGetValue(group.Key, out var sourceCount)
                || group.Count() > sourceCount);
    }

    private static bool SourcesMatchCurrent(
        IReadOnlyList<ProjectFileReference> stagedSources,
        IReadOnlyList<ProjectFileReference> expectedSources,
        SwShShopRecord sourceShop,
        bool signedRecord)
    {
        if (signedRecord)
        {
            return stagedSources.Count == expectedSources.Count
                && expectedSources.All(stagedSources.Contains);
        }

        var currentShopSource = new ProjectFileReference(
            sourceShop.Provenance.SourceLayer,
            sourceShop.Provenance.SourceFile);
        return stagedSources.Contains(currentShopSource)
            && stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    sourceShop.Provenance.SourceFile,
                    StringComparison.OrdinalIgnoreCase))
                .All(source => source.Layer == sourceShop.Provenance.SourceLayer)
            && expectedSources.All(expected => stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    expected.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                .Any(source => source.Layer == expected.Layer));
    }

    private static void ValidateItemSemantics(
        SwShShopsWorkflow workflow,
        SwShShopRecord sourceShop,
        SwShShopRecord effectiveShop,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sourceCounts = sourceShop.Inventory
            .Where(item => item.ItemId != NoneItemId)
            .GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var group in effectiveShop.Inventory
                     .Where(item => item.ItemId != NoneItemId)
                     .GroupBy(item => item.ItemId))
        {
            if (workflow.HasItemSemanticData && workflow.ValidItemIds.Contains(group.Key))
            {
                continue;
            }

            sourceCounts.TryGetValue(group.Key, out var sourceCount);
            if (group.Count() > sourceCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    workflow.HasItemSemanticData
                        ? $"Item ID {group.Key} is not present in the loaded Sword/Shield item data and cannot be newly introduced."
                        : $"Item ID {group.Key} cannot be newly introduced while valid Sword/Shield item metadata is unavailable.",
                    field: SwShShopsWorkflowService.ItemIdField,
                    expected: "Known item ID, or preservation/reordering of an existing legacy item ID"));
            }
        }
    }

    private static void PreflightShopWrite(
        OpenedProject project,
        IReadOnlyList<PendingEdit> shopEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShShopsWorkflowService.ResolveShopDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shops edit preflight could not resolve the source shop table.",
                expected: SwShShopsWorkflowService.ShopDataPath));
            return;
        }

        try
        {
            var shopData = SwShShopDataFile.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = shopEdits
                .Select(edit => ToShopInventoryEdit(shopData, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();
            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                _ = shopData.WriteEdits(edits);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops edit cannot be written safely: {exception.Message}",
                expected: "Safely editable Sword/Shield shop_data.bin",
                file: source.GraphEntry.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops edit preflight could not read the source table: {exception.Message}",
                expected: "Readable Sword/Shield shop_data.bin",
                file: source.GraphEntry.RelativePath));
        }
    }

    private static EditSession ReplacePendingShopEdit(
        EditSession session,
        PendingEdit pendingEdit,
        SwShShopsWorkflow sourceWorkflow,
        SwShShopRecord sourceShop)
    {
        if (string.Equals(pendingEdit.Field, SwShShopsWorkflowService.SetInventoryField, StringComparison.Ordinal)
            && SwShShopsWorkflowService.TryParseInventoryRecordId(pendingEdit.RecordId, out _, out _))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !IsShopEditForSource(edit, sourceWorkflow, sourceShop))
                    .Append(pendingEdit)
                    .ToArray(),
            };
        }

        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameShopEdit(edit, pendingEdit, sourceWorkflow, sourceShop))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameShopEdit(
        PendingEdit candidate,
        PendingEdit pendingEdit,
        SwShShopsWorkflow sourceWorkflow,
        SwShShopRecord sourceShop)
    {
        return IsShopEditForSource(candidate, sourceWorkflow, sourceShop)
            && SwShShopsWorkflowService.TryParseInventoryRecordId(candidate.RecordId, out _, out var candidateSlot)
            && SwShShopsWorkflowService.TryParseInventoryRecordId(pendingEdit.RecordId, out _, out var pendingSlot)
            && candidateSlot == pendingSlot
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static EditSession RemovePendingShopEdits(
        EditSession session,
        SwShShopsWorkflow sourceWorkflow,
        SwShShopRecord sourceShop)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !IsShopEditForSource(edit, sourceWorkflow, sourceShop))
                .ToArray(),
        };
    }

    private static bool IsShopEditForSource(
        PendingEdit edit,
        SwShShopsWorkflow sourceWorkflow,
        SwShShopRecord sourceShop)
    {
        if (!IsShopEdit(edit)
            || !SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shopId, out _))
        {
            return false;
        }

        var candidate = ResolveShop(sourceWorkflow, shopId, diagnostics: null, edit.Field);
        return candidate is not null && IsSamePhysicalInventory(candidate, sourceShop);
    }

    private static IEnumerable<PendingEdit> GetShopEdits(EditSession session)
    {
        return session.PendingEdits.Where(IsShopEdit);
    }

    private static bool IsShopEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ShopsEditDomain, StringComparison.Ordinal);
    }

    private static int CountErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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

        var targetShop = ResolveShop(workflow, shopId, diagnostics: null, edit.Field);
        if (targetShop is null)
        {
            return workflow;
        }

        var itemOption = ResolveItemOption(workflow, itemId);
        var shops = workflow.Shops
            .Select(shop => IsSamePhysicalInventory(shop, targetShop)
                ? OverlayShopInventoryItem(shop, slot, itemId, itemIds, itemOption, workflow, edit.Field!)
                : shop)
            .ToArray();
        return workflow with
        {
            Shops = shops,
            Stats = workflow.Stats with
            {
                TotalShopCount = shops.Length,
                TotalInventoryItemCount = shops.Sum(shop => shop.Inventory.Count),
            },
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
                SetShopInventoryItems(itemIds ?? Array.Empty<int>(), workflow, shop),
            var currentField when string.Equals(currentField, SwShShopsWorkflowService.AddItemField, StringComparison.Ordinal) =>
                InsertShopInventoryItem(shop.Inventory, slot, itemId, itemOption, shop.GlobalPriceField),
            var currentField when string.Equals(currentField, SwShShopsWorkflowService.RemoveItemField, StringComparison.Ordinal) =>
                RemoveShopInventoryItem(shop.Inventory, slot),
            _ => shop.Inventory
                .Select(item => item.Slot == slot
                    ? item with
                    {
                        ItemId = itemId,
                        ItemName = itemOption?.ItemName ?? $"Item {itemId}",
                        Price = ResolveOptionPrice(itemOption, shop.GlobalPriceField),
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
        SwShShopEditableFieldOption? itemOption,
        string? globalPriceField)
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
                ResolveOptionPrice(itemOption, globalPriceField),
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
        SwShShopsWorkflow workflow,
        SwShShopRecord shop)
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
                    ResolveOptionPrice(itemOption, shop.GlobalPriceField),
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

    private static int ResolveOptionPrice(
        SwShShopEditableFieldOption? option,
        string? globalPriceField)
    {
        if (option is null)
        {
            return 0;
        }

        return globalPriceField is not null
            && option.Prices.TryGetValue(globalPriceField, out var price)
                ? price
                : option.Price;
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

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
                paths,
                out var stablePaths,
                out var stableRootFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                stableRootFailure ?? "Configured output root could not be resolved safely.",
                file: targetRelativePath,
                expected: "Stable output root"));
            return null;
        }

        var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            stablePaths.OutputRootPath,
            targetRelativePath);
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
        SwShShopDataFile shopData,
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
            || !SwShShopsWorkflowService.TryParseShopId(
                shopId,
                out var kind,
                out var parsedSourceIndex,
                out var hash,
                out var inventoryIndex,
                out var sourceIdentity,
                out var isLegacy))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit does not include a valid shop inventory target.",
                field: "shopId",
                expected: "Existing shop inventory target"));
            return null;
        }

        var matchingSourceIndexes = kind == SwShShopKind.Single
            ? shopData.SingleShops
                .Select((shop, index) => (shop, index))
                .Where(entry => entry.shop.Hash == hash && inventoryIndex == 0)
                .Select(entry => entry.index)
                .ToArray()
            : shopData.MultiShops
                .Select((shop, index) => (shop, index))
                .Where(entry => entry.shop.Hash == hash
                    && inventoryIndex >= 0
                    && inventoryIndex < entry.shop.Inventories.Count)
                .Select(entry => entry.index)
                .ToArray();
        var sourceIndex = parsedSourceIndex;
        if (isLegacy)
        {
            if (matchingSourceIndexes.Length != 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    matchingSourceIndexes.Length == 0
                        ? "Legacy pending shop edit no longer targets a loaded shop."
                        : "Legacy pending shop edit is ambiguous because multiple physical shops share its hash.",
                    field: "shopId",
                    expected: "Exactly one physical shop source"));
                return null;
            }

            sourceIndex = matchingSourceIndexes[0];
        }
        else if (!matchingSourceIndexes.Contains(sourceIndex)
            || !SourceIdentityMatches(shopData, kind, sourceIndex, hash, sourceIdentity!))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The staged shop source record changed. Stage the edit again against the current physical shop.",
                field: "shopId",
                expected: "Pending edit signed by the current shop source identity"));
            return null;
        }

        return new SwShShopInventoryEdit(
            Kind: kind,
            Hash: hash,
            InventoryIndex: inventoryIndex,
            Slot: slot - 1,
            ItemId: itemId.Value,
            Action: action,
            Items: itemIds,
            ShopIndex: sourceIndex);
    }

    private static bool SourceIdentityMatches(
        SwShShopDataFile shopData,
        SwShShopKind kind,
        int sourceIndex,
        ulong hash,
        string sourceIdentity)
    {
        if (kind == SwShShopKind.Single)
        {
            if ((uint)sourceIndex >= (uint)shopData.SingleShops.Count)
            {
                return false;
            }

            var shop = shopData.SingleShops[sourceIndex];
            return shop.Hash == hash
                && string.Equals(
                    SwShShopsWorkflowService.CreateSourceIdentity(kind, sourceIndex, hash, [shop.Inventory]),
                    sourceIdentity,
                    StringComparison.OrdinalIgnoreCase);
        }

        if ((uint)sourceIndex >= (uint)shopData.MultiShops.Count)
        {
            return false;
        }

        var multiShop = shopData.MultiShops[sourceIndex];
        return multiShop.Hash == hash
            && string.Equals(
                SwShShopsWorkflowService.CreateSourceIdentity(kind, sourceIndex, hash, multiShop.Inventories),
                sourceIdentity,
                StringComparison.OrdinalIgnoreCase);
    }

    private void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Shops output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Shops output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(tempPath, contents);
            if (!File.Exists(tempPath)
                || !File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Shops temporary output verification failed.");
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The original output remains untouched when temporary-file cleanup fails.
            }
        }
    }

    private static void RollbackFailedApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rollbackFailures = rollbackScope.Rollback();
        writtenFiles.Clear();
        if (rollbackFailures.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Shops apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shops rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private static string CreatePlanReason(IReadOnlyList<PendingEdit> pendingEdits)
    {
        var fingerprint = ComputePendingEditFingerprint(pendingEdits);
        var summary = pendingEdits.Count == 1
            ? $"Apply pending Shops edit to {pendingEdits[0].RecordId}."
            : $"Apply {pendingEdits.Count} pending Shops edits.";
        return $"{summary} Fingerprint {fingerprint}.";
    }

    private static string ComputePendingEditFingerprint(IReadOnlyList<PendingEdit> edits)
    {
        var canonical = new StringBuilder();
        foreach (var edit in edits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                     .ThenBy(edit => edit.NewValue, StringComparer.Ordinal))
        {
            AppendFingerprintComponent(canonical, edit.Domain);
            AppendFingerprintComponent(canonical, edit.RecordId);
            AppendFingerprintComponent(canonical, edit.Field);
            AppendFingerprintComponent(canonical, edit.NewValue);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintComponent(
                    canonical,
                    ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintComponent(canonical, source.RelativePath);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendFingerprintComponent(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length ?? -1);
        destination.Append(':');
        destination.Append(value);
        destination.Append('|');
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
