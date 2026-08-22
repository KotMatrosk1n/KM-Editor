// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Items;
using KM.ZA.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KM.ZA.Shops;

internal sealed class ZaShopsEditSessionService
{
    private static readonly JsonSerializerOptions InventoryJsonOptions = new(JsonSerializerDefaults.Web);

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
        string value,
        string? rowId = null)
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
        var loadedShop = loadedWorkflow.Shops.FirstOrDefault(shop => shop.ShopId == shopId);
        if (selectedShop is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{shopId}' is not present in the loaded Pokemon Legends Z-A Shops workflow.",
                field: "shopId",
                expected: "Existing Z-A shop record"));
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        var normalizedField = field.Trim();
        var isIncomingStructuredInventory =
            string.Equals(normalizedField, ZaShopsWorkflowService.SetInventoryField, StringComparison.Ordinal)
            && ParseInventoryUpdate(value.Trim()) is { IsStructured: true };
        if (IsStructuralInventoryField(normalizedField)
            && !isIncomingStructuredInventory
            && HasPendingStructuredInventoryForShop(currentSession.PendingEdits, shopId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A structured inventory update already owns this shop's additions, removals, item IDs, and order. Restage the complete row-aware inventory instead of adding a positional structural edit.",
                field: normalizedField,
                expected: "Version 1 row inventory with unique stable row identities"));
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        if (rowId is null
            && !IsStructuralInventoryField(normalizedField)
            && HasPendingStructuralEditForShop(currentSession.PendingEdits, shopId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "This positional shop field edit is ambiguous after an inventory reorder, addition, or removal. Reload or send the stable row identity before editing the field.",
                field: "rowId",
                expected: "Stable Z-A shop row identity"));
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(
            workflow,
            selectedShop,
            loadedShop,
            slot,
            rowId,
            field,
            value,
            diagnostics);
        if (pendingEdit is null)
        {
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        if (IsStructuredInventoryEdit(pendingEdit)
            && HasUnsafeLegacyPositionalMix(currentSession.PendingEdits, pendingEdit))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "This inventory reorder cannot be combined with an older positional shop field edit. Cancel or restage the shop changes so every row has a stable identity.",
                field: ZaShopsWorkflowService.SetInventoryField,
                expected: "Stable row identities for every non-structural shop edit"));
            return new ZaShopsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingShopEdit(currentSession, pendingEdit);
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
        ValidateLegacyPositionalMix(session.PendingEdits, diagnostics);

        var validationWorkflow = workflow;
        foreach (var edit in OrderPendingEdits(session.PendingEdits))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(validationWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                validationWorkflow = OverlayPendingEdit(validationWorkflow, edit);
            }
        }

        ValidateTouchedShopDisplayOrder(validationWorkflow, session.PendingEdits, diagnostics);

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
            var project = projectWorkspaceService.Open(paths);
            var shopSource = fileSource.Read(project, ZaDataPaths.ShopItemArray);
            var lineupSource = fileSource.Read(project, ZaDataPaths.ShopItemLineupArray);
            var effectiveWorkflow = OverlayPendingEdits(
                shopsWorkflowService.Load(project),
                session.PendingEdits);
            var referencesTestTechnicalMachine = ReferencesTestTechnicalMachine(effectiveWorkflow);
            ZaWorkflowFile? itemSource = null;
            ZaTestTechnicalMachineProvisioningResult? itemProvisioning = null;
            if (referencesTestTechnicalMachine)
            {
                itemSource = fileSource.Read(project, ZaDataPaths.ItemDataArray);
                itemProvisioning = ZaTestTechnicalMachineProvisioner.Provision(
                    itemSource.Bytes,
                    out _);
                if (!itemProvisioning.IsAvailable)
                {
                    throw new InvalidDataException(
                        itemProvisioning.UnavailableReason
                            ?? "TM162 Bug Buzz could not be provisioned safely.");
                }
            }

            var planSources = session.PendingEdits
                .SelectMany(edit => edit.Sources)
                .Append(ZaWorkflowFileSource.CreateReference(shopSource))
                .Append(ZaWorkflowFileSource.CreateReference(lineupSource))
                .ToList();
            if (itemSource is not null)
            {
                planSources.Add(ZaWorkflowFileSource.CreateReference(itemSource));
            }

            var distinctPlanSources = planSources.Distinct().ToArray();
            var lineupWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.ShopItemLineupArray,
                distinctPlanSources,
                outputMode);
            var lineupReason = session.PendingEdits.Count == 1
                ? $"Apply pending Shops edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Shops edits.";
            writes.Add(new PlannedFileWrite(
                lineupWriteInfo.TargetRelativePath,
                lineupWriteInfo.Sources,
                lineupWriteInfo.ReplacesExistingOutput,
                lineupReason,
                CreatePlanSourceFingerprint(
                    paths,
                    outputMode,
                    shopSource,
                    lineupSource,
                    itemSource,
                    itemProvisioning?.Added == true,
                    session.PendingEdits)));

            if (itemSource is not null && itemProvisioning?.Added == true)
            {
                var itemWriteInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    ZaDataPaths.ItemDataArray,
                    [ZaWorkflowFileSource.CreateReference(itemSource)],
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    itemWriteInfo.TargetRelativePath,
                    itemWriteInfo.Sources,
                    itemWriteInfo.ReplacesExistingOutput,
                    "Provision the owned TM162 Bug Buzz item before the reviewed shop references it.",
                    CreatePlanSourceFingerprint(
                        paths,
                        outputMode,
                        shopSource,
                        lineupSource,
                        itemSource,
                        provisionsTestTechnicalMachine: true,
                        session.PendingEdits)));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                var descriptorPreview = ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                    paths,
                    CreatePlannedVirtualPaths(itemProvisioning?.Added == true));
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides.",
                    CreateDescriptorPlanFingerprint(paths, descriptorPreview)));
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

        var planBecameStale = false;
        var wroteItemData = false;
        try
        {
            ZaWorkflowFileSource.ApplyHybridMixedBatch(
                paths,
                outputMode,
                isolateTrinityModManagerRomFs: false,
                () =>
                {
                    var lockedPlan = CreateChangePlan(paths, session, outputMode);
                    if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, lockedPlan))
                    {
                        currentPlan = lockedPlan;
                        diagnostics.Clear();
                        diagnostics.AddRange(lockedPlan.Diagnostics);
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "Reviewed change plan became stale before the Shops output lock was acquired. Review the change plan again before applying.",
                            expected: "Current reviewed Shops source and pending changes"));
                        planBecameStale = true;
                        throw new InvalidDataException("The reviewed Shops change plan became stale.");
                    }

                    currentPlan = lockedPlan;
                    diagnostics.Clear();
                    diagnostics.AddRange(lockedPlan.Diagnostics);
                    var project = projectWorkspaceService.Open(paths);
                    var shopSource = fileSource.Read(project, ZaDataPaths.ShopItemArray);
                    var lineupSource = fileSource.Read(project, ZaDataPaths.ShopItemLineupArray);
                    var effectiveWorkflow = OverlayPendingEdits(
                        shopsWorkflowService.Load(project),
                        session.PendingEdits);
                    var referencesTestTechnicalMachine = ReferencesTestTechnicalMachine(effectiveWorkflow);
                    ZaWorkflowFile? itemSource = null;
                    ZaTestTechnicalMachineProvisioningResult? itemProvisioning = null;
                    byte[]? provisionedItemBytes = null;
                    if (referencesTestTechnicalMachine)
                    {
                        itemSource = fileSource.Read(project, ZaDataPaths.ItemDataArray);
                        itemProvisioning = ZaTestTechnicalMachineProvisioner.Provision(
                            itemSource.Bytes,
                            out provisionedItemBytes);
                        if (!itemProvisioning.IsAvailable)
                        {
                            diagnostics.Add(CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                itemProvisioning.UnavailableReason
                                    ?? "TM162 Bug Buzz could not be provisioned safely.",
                                file: $"romfs/{ZaDataPaths.ItemDataArray}",
                                expected: "Supported unique 160-TM source with unclaimed item 2222"));
                            throw new InvalidDataException("TM162 Bug Buzz could not be provisioned safely.");
                        }
                    }

                    if (!PlanSourcesMatch(
                            paths,
                            lockedPlan,
                            outputMode,
                            shopSource,
                            lineupSource,
                            itemSource,
                            itemProvisioning?.Added == true,
                            session.PendingEdits))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "Shops source data changed while the output was being prepared. Review the change plan again before applying.",
                            expected: "The exact reviewed shop master and lineup data"));
                        planBecameStale = true;
                        throw new InvalidDataException("The reviewed Shops sources changed during output preparation.");
                    }

                    var masterRows = ZaShopsWorkflowService.ReadShopRows(shopSource.Bytes);
                    var lineupRows = ZaShopsWorkflowService.ReadLineupRows(lineupSource.Bytes).ToList();
                    foreach (var edit in OrderPendingEdits(session.PendingEdits))
                    {
                        ApplyEdit(masterRows, lineupRows, edit, diagnostics);
                    }

                    ValidateTouchedLineupDisplayOrder(masterRows, lineupRows, session.PendingEdits, diagnostics);
                    if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        throw new InvalidDataException("A staged Shops edit failed final validation under the output lock.");
                    }

                    var outputReferencesTestTechnicalMachine =
                        effectiveWorkflow.OwnedTestTechnicalMachineAvailable
                        && lineupRows
                            .SelectMany(row => row.Inventory)
                            .Any(row => row.ItemId == ZaTechnicalMachineCatalog.TestTechnicalMachineItemId);
                    if (outputReferencesTestTechnicalMachine != referencesTestTechnicalMachine)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "The reviewed TM162 shop reference changed while output was being prepared. Review the change plan again before applying.",
                            expected: "The exact reviewed shop inventory and item provisioning requirement"));
                        planBecameStale = true;
                        throw new InvalidDataException("The reviewed TM162 shop reference changed during output preparation.");
                    }

                    var dataWrites = new List<ZaWorkflowFileWrite>
                    {
                        new(
                            ZaDataPaths.ShopItemLineupArray,
                            ZaShopsWorkflowService.WriteLineupRows(lineupRows)),
                    };
                    if (itemProvisioning?.Added == true && provisionedItemBytes is not null)
                    {
                        dataWrites.Add(new ZaWorkflowFileWrite(
                            ZaDataPaths.ItemDataArray,
                            provisionedItemBytes));
                        wroteItemData = true;
                    }

                    var reviewedDescriptorBytes = outputMode == ZaOutputMode.Standalone
                        ? ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                            paths,
                            dataWrites.Select(write => write.VirtualPath))
                        : null;
                    if (reviewedDescriptorBytes is not null
                        && !DescriptorPlanMatches(paths, lockedPlan, reviewedDescriptorBytes))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "The standalone descriptor changed while Shops output was being prepared. Review the change plan again before applying.",
                            expected: "The exact reviewed standalone descriptor"));
                        planBecameStale = true;
                        throw new InvalidDataException("The reviewed standalone descriptor changed during Shops output preparation.");
                    }

                    return new ZaStandaloneMixedBatch(
                        dataWrites,
                        Array.Empty<string>(),
                        Array.Empty<ZaStandaloneOutputMutation>(),
                        reviewedDescriptorBytes);
                },
                revalidateReviewedState: () =>
                    ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                        reviewedPlan,
                        CreateChangePlan(paths, session, outputMode)));

            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.ShopItemLineupArray, outputMode));
            if (wroteItemData)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.ItemDataArray,
                    outputMode));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Shops", outputMode)));
        }
        catch (Exception exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
        {
            if (!planBecameStale)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Shops output could not be written: {exception.Message}",
                    file: $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                    expected: "Readable reviewed sources and a writable output root"));
            }
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaShopsWorkflow workflow,
        ZaShopRecord shop,
        ZaShopRecord? sourceIdentityShop,
        int slot,
        string? requestedRowId,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var normalizedValue = value.Trim();
        var isSetInventory = string.Equals(normalizedField, ZaShopsWorkflowService.SetInventoryField, StringComparison.Ordinal);
        var isAdd = string.Equals(normalizedField, ZaShopsWorkflowService.AddItemField, StringComparison.Ordinal);
        var isRemove = string.Equals(normalizedField, ZaShopsWorkflowService.RemoveItemField, StringComparison.Ordinal);
        if (requestedRowId is not null && !ZaShopsWorkflowService.IsValidRowId(requestedRowId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop row identity '{requestedRowId}' is not valid.",
                field: "rowId",
                expected: "Existing Z-A shop row identity"));
            return null;
        }

        var identityShop = requestedRowId is null ? sourceIdentityShop ?? shop : shop;
        var inventoryItem = requestedRowId is not null
            ? shop.Inventory.FirstOrDefault(item => item.RowId == requestedRowId)
            : identityShop.Inventory.FirstOrDefault(item => item.Slot == slot);

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
            if (!ValidateInventoryUpdate(
                    workflow,
                    normalizedValue,
                    sourceIdentityShop ?? shop,
                    diagnostics,
                    normalizedField))
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

        if (inventoryItem is not null)
        {
            slot = inventoryItem.Slot;
        }

        if (isRemove)
        {
            return ZaEditSessionSupport.CreatePendingEdit(
                ZaEditSessionSupport.ShopsDomain,
                $"Remove slot {slot} from {shop.Name}.",
                new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile),
                CreateRecordId(shop.ShopId, slot, inventoryItem!.RowId),
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

        if (string.Equals(normalizedField, ZaShopsWorkflowService.DisplayIndexField, StringComparison.Ordinal)
            && int.TryParse(normalizedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var displayIndex)
            && (displayIndex < 1 || displayIndex > shop.Inventory.Count))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop display order must be between 1 and {shop.Inventory.Count.ToString(CultureInfo.InvariantCulture)}.",
                field: normalizedField,
                expected: "One-based position within this shop"));
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
            CreateRecordId(shop.ShopId, slot, inventoryItem!.RowId),
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

        if (!TryParseRecordRowId(edit.RecordId, out var shopId, out var slot, out var rowId)
            || workflow.Shops.FirstOrDefault(shop => shop.ShopId == shopId) is not { } shop)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Legends Z-A Shops edit targets a record that is not loaded.",
                field: "recordId",
                expected: "Existing Z-A shop inventory record"));
            return;
        }

        if (rowId is not null)
        {
            var target = shop.Inventory.FirstOrDefault(item => item.RowId == rowId);
            if (target is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Pokemon Legends Z-A Shops edit targets a source row that is not loaded.",
                    field: "recordId",
                    expected: "Existing Z-A shop source row"));
                return;
            }

            slot = target.Slot;
        }

        _ = CreatePendingEdit(
            workflow,
            shop,
            sourceIdentityShop: null,
            slot,
            rowId,
            edit.Field ?? string.Empty,
            edit.NewValue ?? string.Empty,
            diagnostics);
    }

    private static EditSession ReplacePendingShopEdit(EditSession session, PendingEdit pendingEdit)
    {
        var includedRowIds = string.Equals(
                pendingEdit.Field,
                ZaShopsWorkflowService.SetInventoryField,
                StringComparison.Ordinal)
            && ParseInventoryUpdate(pendingEdit.NewValue ?? string.Empty) is { IsStructured: true } inventoryUpdate
                ? inventoryUpdate.Rows.Select(row => row.RowId!).ToHashSet(StringComparer.Ordinal)
                : null;

        var pendingEdits = session.PendingEdits
            .Where(edit => !ShouldReplaceOrPrunePendingEdit(edit, pendingEdit, includedRowIds))
            .Append(pendingEdit)
            .ToArray();
        return session with { PendingEdits = pendingEdits };
    }

    private static bool ShouldReplaceOrPrunePendingEdit(
        PendingEdit candidate,
        PendingEdit pendingEdit,
        IReadOnlySet<string>? includedRowIds)
    {
        if (!string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            || !TryParseRecordRowId(pendingEdit.RecordId, out var pendingShopId, out _, out var pendingRowId)
            || !TryParseRecordRowId(candidate.RecordId, out var candidateShopId, out _, out var candidateRowId)
            || !string.Equals(candidateShopId, pendingShopId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(pendingEdit.Field, ZaShopsWorkflowService.SetInventoryField, StringComparison.Ordinal))
        {
            if (string.Equals(candidate.Field, ZaShopsWorkflowService.SetInventoryField, StringComparison.Ordinal))
            {
                return true;
            }

            if (includedRowIds is not null)
            {
                if (IsStructuralInventoryField(candidate.Field))
                {
                    return true;
                }

                if (candidateRowId is not null
                    && (!includedRowIds.Contains(candidateRowId)
                        || string.Equals(candidate.Field, ZaShopsWorkflowService.ItemIdField, StringComparison.Ordinal)
                        || string.Equals(candidate.Field, ZaShopsWorkflowService.DisplayIndexField, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        if (pendingRowId is not null
            && candidateRowId is not null
            && string.Equals(candidateRowId, pendingRowId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static ZaShopsWorkflow OverlayPendingEdits(ZaShopsWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in OrderPendingEdits(edits))
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaShopsWorkflow OverlayPendingEdit(ZaShopsWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal)
            || !TryParseRecordRowId(edit.RecordId, out var shopId, out var slot, out var rowId))
        {
            return workflow;
        }

        return workflow with
        {
            Shops = workflow.Shops
                .Select(shop => shop.ShopId == shopId ? OverlayShop(workflow, shop, slot, rowId, edit) : shop)
                .ToArray(),
        };
    }

    private static ZaShopRecord OverlayShop(
        ZaShopsWorkflow workflow,
        ZaShopRecord shop,
        int slot,
        string? rowId,
        PendingEdit edit)
    {
        if (edit.Field == ZaShopsWorkflowService.SetInventoryField && shop.CanEditInventoryOrder)
        {
            var update = ParseInventoryUpdate(edit.NewValue ?? string.Empty);
            if (update is null)
            {
                return shop;
            }

            if (update.IsStructured)
            {
                return OverlayStructuredInventoryUpdate(workflow, shop, update);
            }

            var inventoryByIndex = shop.Inventory.OrderBy(item => item.Slot).ToArray();
            var nextInventory = update.Rows
                .Select((row, index) =>
                {
                    var source = inventoryByIndex.ElementAtOrDefault(index);
                    var overlay = OverlayInventoryItemId(
                        workflow,
                        source ?? CreatePlaceholderInventoryRecord(index + 1),
                        index + 1,
                        row.ItemId);
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
                .Where(item => !IsTargetInventoryItem(item, slot, rowId))
                .Select((item, index) => OverlayInventoryField(workflow, item with { Slot = index + 1 }, ZaShopsWorkflowService.DisplayIndexField, (index + 1).ToString(CultureInfo.InvariantCulture)))
                .ToArray();
            return shop with
            {
                Inventory = nextInventory,
                InventorySummary = ZaShopsWorkflowService.FormatInventorySummary(nextInventory),
            };
        }

        var updatedInventory = shop.Inventory
            .Select(item => IsTargetInventoryItem(item, slot, rowId)
                ? OverlayInventoryField(workflow, item, edit.Field ?? string.Empty, edit.NewValue ?? string.Empty)
                : item)
            .ToArray();
        if (edit.Field == ZaShopsWorkflowService.DisplayIndexField)
        {
            updatedInventory = updatedInventory
                .OrderBy(GetInventoryDisplayIndex)
                .ThenBy(item => item.SourceIndex)
                .Select((item, index) => item with { Slot = index + 1 })
                .ToArray();
        }

        return shop with
        {
            Inventory = updatedInventory,
            InventorySummary = ZaShopsWorkflowService.FormatInventorySummary(updatedInventory),
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

    private static ZaShopRecord OverlayStructuredInventoryUpdate(
        ZaShopsWorkflow workflow,
        ZaShopRecord shop,
        InventoryUpdate update)
    {
        var currentByRowId = shop.Inventory.ToDictionary(item => item.RowId, StringComparer.Ordinal);
        var nextInventory = new List<ZaShopInventoryRecord>(update.Rows.Count);
        for (var index = 0; index < update.Rows.Count; index++)
        {
            var row = update.Rows[index];
            if (row.RowId is null)
            {
                return shop;
            }

            ZaShopInventoryRecord source;
            if (!currentByRowId.TryGetValue(row.RowId, out source!))
            {
                if (!row.RowId.StartsWith(ZaShopsWorkflowService.NewRowIdPrefix, StringComparison.Ordinal))
                {
                    return shop;
                }

                source = CreatePlaceholderInventoryRecord(index + 1, row.RowId);
            }

            var overlay = OverlayInventoryItemId(workflow, source, index + 1, row.ItemId);
            overlay = OverlayInventoryField(
                workflow,
                overlay,
                ZaShopsWorkflowService.DisplayIndexField,
                (index + 1).ToString(CultureInfo.InvariantCulture));
            nextInventory.Add(overlay);
        }

        return shop with
        {
            Inventory = nextInventory,
            InventorySummary = ZaShopsWorkflowService.FormatInventorySummary(nextInventory),
        };
    }

    private static bool IsTargetInventoryItem(
        ZaShopInventoryRecord item,
        int slot,
        string? rowId) =>
        rowId is not null
            ? string.Equals(item.RowId, rowId, StringComparison.Ordinal)
            : item.Slot == slot;

    private static uint GetInventoryDisplayIndex(ZaShopInventoryRecord item)
    {
        return item.FieldValues.TryGetValue(ZaShopsWorkflowService.DisplayIndexField, out var value)
            && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : uint.MaxValue;
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

    private static ZaShopInventoryRecord CreatePlaceholderInventoryRecord(int slot, string? rowId = null) =>
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
            CanEditPrice: false,
            SourceIndex: -1,
            RowId: rowId ?? string.Create(CultureInfo.InvariantCulture, $"{ZaShopsWorkflowService.NewRowIdPrefix}{slot}"));

    private static void ApplyEdit(
        IReadOnlyList<ZaShopsWorkflowService.ShopMasterRow> masterRows,
        List<ZaShopsWorkflowService.ShopLineupRow> lineupRows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseRecordRowId(edit.RecordId, out var shopId, out var slot, out var rowId)
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

        ApplyLineupEdit(lineup, slot, rowId, edit, diagnostics);
    }

    private static void ApplyLineupEdit(
        ZaShopsWorkflowService.ShopLineupRow lineup,
        int slot,
        string? rowId,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var inventory = lineup.Inventory
            .OrderBy(row => row.DisplayIndex)
            .ThenBy(row => row.SourceIndex)
            .ToArray();

        if (edit.Field == ZaShopsWorkflowService.SetInventoryField)
        {
            var update = ParseInventoryUpdate(edit.NewValue ?? string.Empty);
            if (update is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Z-A shop inventory value is not valid for apply.",
                    field: edit.Field,
                    expected: "Version 1 row inventory or comma-separated item IDs"));
                return;
            }

            if (update.IsStructured)
            {
                ApplyStructuredInventoryUpdate(lineup, inventory, update, diagnostics);
                return;
            }

            lineup.Inventory.Clear();
            lineup.Inventory.AddRange(update.Rows.Select((inventoryRow, index) =>
            {
                var source = inventory.ElementAtOrDefault(index);
                var conditions = source is null
                    ? CreateDefaultConditions()
                    : CloneConditions(source.Conditions);
                return new ZaShopsWorkflowService.ShopInventoryRow(
                    source?.SourceIndex ?? index,
                    checked((uint)inventoryRow.ItemId),
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
                    CreateDefaultConditions(),
                    string.Create(CultureInfo.InvariantCulture, $"{ZaShopsWorkflowService.NewRowIdPrefix}{slot}")));
            RewriteLineupInventory(lineup, nextRows);
            return;
        }

        if (edit.Field == ZaShopsWorkflowService.RemoveItemField)
        {
            var nextRows = inventory
                .Where((candidate, index) => rowId is not null
                    ? !string.Equals(candidate.RowId, rowId, StringComparison.Ordinal)
                    : index != slot - 1)
                .ToList();
            RewriteLineupInventory(lineup, nextRows);
            return;
        }

        var row = rowId is not null
            ? lineup.Inventory.FirstOrDefault(candidate => string.Equals(candidate.RowId, rowId, StringComparison.Ordinal))
            : inventory.ElementAtOrDefault(slot - 1);
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
                if (TryParseInteger(edit, 1, int.MaxValue, diagnostics) is { } displayIndex)
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

    private static void ApplyStructuredInventoryUpdate(
        ZaShopsWorkflowService.ShopLineupRow lineup,
        IReadOnlyList<ZaShopsWorkflowService.ShopInventoryRow> currentDisplayRows,
        InventoryUpdate update,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var currentByRowId = currentDisplayRows.ToDictionary(row => row.RowId, StringComparer.Ordinal);
        var nextSourceIndex = lineup.Inventory.Select(row => row.SourceIndex).DefaultIfEmpty(-1).Max() + 1;
        var desiredRows = new List<ZaShopsWorkflowService.ShopInventoryRow>(update.Rows.Count);
        foreach (var (inventoryRow, index) in update.Rows.Select((row, index) => (row, index)))
        {
            if (inventoryRow.RowId is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending Z-A shop row identity is missing.",
                    field: ZaShopsWorkflowService.SetInventoryField,
                    expected: "Version 1 row inventory"));
                return;
            }

            ZaShopsWorkflowService.ShopInventoryRow source;
            if (currentByRowId.TryGetValue(inventoryRow.RowId, out var currentRow))
            {
                source = currentRow;
            }
            else if (inventoryRow.RowId.StartsWith(ZaShopsWorkflowService.NewRowIdPrefix, StringComparison.Ordinal))
            {
                source = new ZaShopsWorkflowService.ShopInventoryRow(
                    nextSourceIndex++,
                    0,
                    0,
                    CreateDefaultConditions(),
                    inventoryRow.RowId);
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Z-A shop row '{inventoryRow.RowId}' is not present in lineup '{lineup.Name}'.",
                    field: ZaShopsWorkflowService.SetInventoryField,
                    expected: "Rows from the selected Z-A shop"));
                return;
            }

            desiredRows.Add(new ZaShopsWorkflowService.ShopInventoryRow(
                source.SourceIndex,
                checked((uint)inventoryRow.ItemId),
                checked((uint)(index + 1)),
                CloneConditions(source.Conditions),
                inventoryRow.RowId));
        }

        var desiredByRowId = desiredRows.ToDictionary(row => row.RowId, StringComparer.Ordinal);
        var existingRowIds = currentByRowId.Keys.ToHashSet(StringComparer.Ordinal);
        var rebuiltRows = lineup.Inventory
            .Where(row => desiredByRowId.ContainsKey(row.RowId))
            .Select(row => desiredByRowId[row.RowId])
            .Concat(desiredRows.Where(row => !existingRowIds.Contains(row.RowId)))
            .ToArray();

        lineup.Inventory.Clear();
        lineup.Inventory.AddRange(rebuiltRows);
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

    private static bool ValidateInventoryUpdate(
        ZaShopsWorkflow workflow,
        string value,
        ZaShopRecord shop,
        ICollection<ValidationDiagnostic> diagnostics,
        string field)
    {
        var update = ParseInventoryUpdate(value);
        if (update is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shop inventory value must be version 1 row data or a comma-separated list of item IDs.",
                field: field,
                expected: "Version 1 row inventory or comma-separated item IDs"));
            return false;
        }

        if (!update.Rows.All(row => ValidateKnownItemId(workflow, row.ItemId, field, diagnostics)))
        {
            return false;
        }

        if (!update.IsStructured)
        {
            return true;
        }

        var availableRowIds = shop.Inventory.Select(item => item.RowId).ToHashSet(StringComparer.Ordinal);
        var unknownSourceRow = update.Rows.FirstOrDefault(row =>
            row.RowId?.StartsWith(ZaShopsWorkflowService.SourceRowIdPrefix, StringComparison.Ordinal) == true
            && !availableRowIds.Contains(row.RowId));
        if (unknownSourceRow is null)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Shop row '{unknownSourceRow.RowId}' is not present in '{shop.Name}'.",
            field: field,
            expected: "Rows from the selected Z-A shop"));
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

    private static InventoryUpdate? ParseInventoryUpdate(string value)
    {
        if (!value.TrimStart().StartsWith('{'))
        {
            var itemIds = ParseInventoryList(value);
            return itemIds is null
                ? null
                : new InventoryUpdate(
                    IsStructured: false,
                    itemIds.Select(itemId => new InventoryUpdateRow(null, itemId)).ToArray());
        }

        try
        {
            var payload = JsonSerializer.Deserialize<StructuredInventoryPayload>(value, InventoryJsonOptions);
            if (payload is not { Version: 1, UpdateOrder: not null, Rows: not null })
            {
                return null;
            }

            var rowIds = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<InventoryUpdateRow>(payload.Rows.Length);
            foreach (var row in payload.Rows)
            {
                if (!ZaShopsWorkflowService.IsValidRowId(row.RowId)
                    || !rowIds.Add(row.RowId!)
                    || row.ItemId < ZaShopsWorkflowService.MinimumItemId
                    || row.ItemId > ZaShopsWorkflowService.MaximumItemId)
                {
                    return null;
                }

                rows.Add(new InventoryUpdateRow(row.RowId, row.ItemId));
            }

            return new InventoryUpdate(IsStructured: true, rows);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static IReadOnlyList<PendingEdit> OrderPendingEdits(IEnumerable<PendingEdit> edits)
    {
        return edits
            .Select((edit, index) => new { Edit = edit, Index = index })
            .OrderBy(entry => IsStructuredInventoryEdit(entry.Edit) ? 0 : 1)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Edit)
            .ToArray();
    }

    private static bool IsStructuredInventoryEdit(PendingEdit edit) =>
        string.Equals(edit.Field, ZaShopsWorkflowService.SetInventoryField, StringComparison.Ordinal)
        && ParseInventoryUpdate(edit.NewValue ?? string.Empty) is { IsStructured: true };

    private static bool IsStructuralInventoryField(string? field) =>
        string.Equals(field, ZaShopsWorkflowService.SetInventoryField, StringComparison.Ordinal)
        || string.Equals(field, ZaShopsWorkflowService.AddItemField, StringComparison.Ordinal)
        || string.Equals(field, ZaShopsWorkflowService.RemoveItemField, StringComparison.Ordinal);

    private static bool HasPendingStructuralEditForShop(
        IEnumerable<PendingEdit> edits,
        string shopId) =>
        edits.Any(edit =>
            string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal)
            && IsStructuralInventoryField(edit.Field)
            && TryParseRecordRowId(edit.RecordId, out var candidateShopId, out _, out _)
            && string.Equals(candidateShopId, shopId, StringComparison.Ordinal));

    private static bool HasPendingStructuredInventoryForShop(
        IEnumerable<PendingEdit> edits,
        string shopId) =>
        edits.Any(edit =>
            string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal)
            && IsStructuredInventoryEdit(edit)
            && TryParseRecordRowId(edit.RecordId, out var candidateShopId, out _, out _)
            && string.Equals(candidateShopId, shopId, StringComparison.Ordinal));

    private static bool HasUnsafeLegacyPositionalMix(
        IEnumerable<PendingEdit> existingEdits,
        PendingEdit structuredInventoryEdit)
    {
        if (!TryParseRecordRowId(
                structuredInventoryEdit.RecordId,
                out var structuredShopId,
                out _,
                out _))
        {
            return false;
        }

        return existingEdits.Any(edit =>
            string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal)
            && TryParseRecordRowId(edit.RecordId, out var candidateShopId, out _, out var candidateRowId)
            && string.Equals(candidateShopId, structuredShopId, StringComparison.Ordinal)
            && candidateRowId is null
            && !IsStructuralInventoryField(edit.Field));
    }

    private static void ValidateLegacyPositionalMix(
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var shopEdits = edits
            .Where(edit => string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal))
            .Select(edit => TryParseRecordRowId(edit.RecordId, out var shopId, out _, out var rowId)
                ? new { Edit = edit, ShopId = shopId, RowId = rowId }
                : null)
            .Where(entry => entry is not null)
            .ToArray();
        foreach (var group in shopEdits.GroupBy(entry => entry!.ShopId, StringComparer.Ordinal))
        {
            if (!group.Any(entry => IsStructuredInventoryEdit(entry!.Edit)))
            {
                continue;
            }

            if (group.Any(entry =>
                    IsStructuralInventoryField(entry!.Edit.Field)
                    && !IsStructuredInventoryEdit(entry.Edit)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Shop '{group.Key}' mixes a structured inventory update with a positional add, remove, or replacement. Restage one complete row-aware inventory update.",
                    field: ZaShopsWorkflowService.SetInventoryField,
                    expected: "One version 1 row inventory update with unique stable row identities"));
            }

            if (group.Any(entry => entry!.RowId is null && !IsStructuralInventoryField(entry.Edit.Field)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Shop '{group.Key}' mixes a structured inventory update with an older positional field edit. Cancel or restage that shop so every row has a stable identity.",
                    field: ZaShopsWorkflowService.SetInventoryField,
                    expected: "Stable row identities for every non-structural shop edit"));
            }
        }
    }

    private sealed record InventoryUpdate(
        bool IsStructured,
        IReadOnlyList<InventoryUpdateRow> Rows);

    private sealed record InventoryUpdateRow(string? RowId, int ItemId);

    private sealed record StructuredInventoryPayload(
        int Version,
        bool? UpdateOrder,
        StructuredInventoryPayloadRow[]? Rows);

    private sealed record StructuredInventoryPayloadRow(string? RowId, int ItemId);

    private static void ValidateTouchedShopDisplayOrder(
        ZaShopsWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var touchedShopIds = edits
            .Where(edit => string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal))
            .Select(edit => TryParseRecordId(edit.RecordId, out var shopId, out _) ? shopId : null)
            .Where(shopId => shopId is not null)
            .Distinct(StringComparer.Ordinal)
            .Cast<string>();

        foreach (var shopId in touchedShopIds)
        {
            var shop = workflow.Shops.FirstOrDefault(candidate => string.Equals(candidate.ShopId, shopId, StringComparison.Ordinal));
            if (shop is null)
            {
                continue;
            }

            var values = new List<uint>(shop.Inventory.Count);
            var hasInvalidValue = false;
            foreach (var item in shop.Inventory)
            {
                if (!item.FieldValues.TryGetValue(ZaShopsWorkflowService.DisplayIndexField, out var text)
                    || !uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    hasInvalidValue = true;
                    break;
                }

                values.Add(value);
            }

            if (hasInvalidValue)
            {
                diagnostics.Add(CreateDisplayOrderDiagnostic(shop.Name, shop.Inventory.Count, "an invalid value"));
                continue;
            }

            ValidateDisplayOrderValues(shop.Name, values, diagnostics);
        }
    }

    private static void ValidateTouchedLineupDisplayOrder(
        IReadOnlyList<ZaShopsWorkflowService.ShopMasterRow> masterRows,
        IReadOnlyList<ZaShopsWorkflowService.ShopLineupRow> lineupRows,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var touchedLineupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edit in edits.Where(edit =>
                     string.Equals(edit.Domain, ZaEditSessionSupport.ShopsDomain, StringComparison.Ordinal)))
        {
            if (TryParseRecordId(edit.RecordId, out var shopId, out _)
                && TryResolveLineupId(masterRows, shopId, out var lineupId))
            {
                touchedLineupIds.Add(lineupId);
            }
        }

        foreach (var lineupId in touchedLineupIds)
        {
            var lineup = lineupRows.FirstOrDefault(row => string.Equals(row.Name, lineupId, StringComparison.Ordinal));
            if (lineup is not null)
            {
                ValidateDisplayOrderValues(
                    lineup.Name,
                    lineup.Inventory.Select(row => row.DisplayIndex).ToArray(),
                    diagnostics);
            }
        }
    }

    private static void ValidateDisplayOrderValues(
        string shopName,
        IReadOnlyList<uint> values,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var expected = Enumerable.Range(1, values.Count).Select(value => checked((uint)value)).ToArray();
        var sorted = values.Order().ToArray();
        if (sorted.SequenceEqual(expected))
        {
            return;
        }

        var duplicates = values
            .GroupBy(value => value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order()
            .ToArray();
        var outOfRange = values
            .Where(value => value < 1 || value > values.Count)
            .Distinct()
            .Order()
            .ToArray();
        var missing = expected.Except(values).Order().ToArray();
        var details = new List<string>(3);
        if (duplicates.Length > 0)
        {
            details.Add($"duplicate position(s) {string.Join(", ", duplicates)}");
        }

        if (outOfRange.Length > 0)
        {
            details.Add($"out-of-range position(s) {string.Join(", ", outOfRange)}");
        }

        if (missing.Length > 0)
        {
            details.Add($"missing position(s) {string.Join(", ", missing)}");
        }

        diagnostics.Add(CreateDisplayOrderDiagnostic(shopName, values.Count, string.Join("; ", details)));
    }

    private static ValidationDiagnostic CreateDisplayOrderDiagnostic(
        string shopName,
        int inventoryCount,
        string detail) =>
        CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Shop '{shopName}' display order must contain every position from 1 through {inventoryCount.ToString(CultureInfo.InvariantCulture)} exactly once; found {detail}.",
            field: ZaShopsWorkflowService.DisplayIndexField,
            expected: $"Unique permutation of 1 through {inventoryCount.ToString(CultureInfo.InvariantCulture)}");

    private static bool DescriptorPlanMatches(
        ProjectPaths paths,
        ChangePlan plan,
        byte[] descriptorPreview)
    {
        var targetRelativePath = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths).TargetRelativePath;
        var write = plan.Writes.FirstOrDefault(candidate => string.Equals(
            candidate.TargetRelativePath,
            targetRelativePath,
            StringComparison.Ordinal));
        return write is not null
            && string.Equals(
                write.SourceFingerprint,
                CreateDescriptorPlanFingerprint(paths, descriptorPreview),
                StringComparison.Ordinal);
    }

    private static bool PlanSourcesMatch(
        ProjectPaths paths,
        ChangePlan plan,
        ZaOutputMode outputMode,
        ZaWorkflowFile shopSource,
        ZaWorkflowFile lineupSource,
        ZaWorkflowFile? itemSource,
        bool provisionsTestTechnicalMachine,
        IEnumerable<PendingEdit> edits)
    {
        var targetRelativePath = ZaWorkflowFileSource.CreatePlannedWrite(
            paths,
            ZaDataPaths.ShopItemLineupArray,
            Array.Empty<ProjectFileReference>(),
            outputMode).TargetRelativePath;
        var write = plan.Writes.FirstOrDefault(candidate => string.Equals(
            candidate.TargetRelativePath,
            targetRelativePath,
            StringComparison.Ordinal));
        return write is not null
            && string.Equals(
                write.SourceFingerprint,
                CreatePlanSourceFingerprint(
                    paths,
                    outputMode,
                    shopSource,
                    lineupSource,
                    itemSource,
                    provisionsTestTechnicalMachine,
                    edits),
                StringComparison.Ordinal);
    }

    private static string CreatePlanSourceFingerprint(
        ProjectPaths paths,
        ZaOutputMode outputMode,
        ZaWorkflowFile shopSource,
        ZaWorkflowFile lineupSource,
        ZaWorkflowFile? itemSource,
        bool provisionsTestTechnicalMachine,
        IEnumerable<PendingEdit> edits)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Shops.Source.v2");
        AppendFingerprintValue(hash, outputMode.ToString());
        AppendFingerprintSource(hash, ZaDataPaths.ShopItemArray, shopSource);
        AppendFingerprintSource(hash, ZaDataPaths.ShopItemLineupArray, lineupSource);
        AppendFingerprintValue(hash, provisionsTestTechnicalMachine ? "ProvisionTM162" : "NoTM162Provision");
        if (itemSource is not null)
        {
            AppendFingerprintSource(hash, ZaDataPaths.ItemDataArray, itemSource);
        }

        AppendFingerprintTarget(hash, paths, ZaDataPaths.ShopItemLineupArray, outputMode);
        if (provisionsTestTechnicalMachine)
        {
            AppendFingerprintTarget(hash, paths, ZaDataPaths.ItemDataArray, outputMode);
        }

        foreach (var (edit, index) in OrderPendingEdits(edits).Select((edit, index) => (edit, index)))
        {
            AppendFingerprintValue(hash, index.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintValue(hash, edit.Domain);
            AppendFingerprintValue(hash, edit.RecordId);
            AppendFingerprintValue(hash, edit.Field);
            AppendFingerprintValue(hash, edit.NewValue);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintValue(hash, source.Layer.ToString());
                AppendFingerprintValue(hash, source.RelativePath);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool ReferencesTestTechnicalMachine(ZaShopsWorkflow workflow) =>
        workflow.OwnedTestTechnicalMachineAvailable
        && workflow.Shops
            .SelectMany(shop => shop.Inventory)
            .Any(item => item.ItemId == ZaTechnicalMachineCatalog.TestTechnicalMachineItemId);

    private static IReadOnlyList<string> CreatePlannedVirtualPaths(
        bool provisionsTestTechnicalMachine) =>
        provisionsTestTechnicalMachine
            ? [ZaDataPaths.ShopItemLineupArray, ZaDataPaths.ItemDataArray]
            : [ZaDataPaths.ShopItemLineupArray];

    private static string CreateDescriptorPlanFingerprint(
        ProjectPaths paths,
        byte[] descriptorPreview)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Shops.Descriptor.v1");
        AppendFingerprintBytes(hash, descriptorPreview);
        AppendFingerprintTarget(
            hash,
            paths,
            ZaWorkflowFileSource.DescriptorVirtualPath,
            ZaOutputMode.Standalone);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendFingerprintSource(
        IncrementalHash hash,
        string virtualPath,
        ZaWorkflowFile source)
    {
        AppendFingerprintValue(hash, virtualPath.Replace('\\', '/'));
        AppendFingerprintValue(hash, source.SourceLayer.ToString());
        AppendFingerprintValue(hash, source.RelativePath.Replace('\\', '/'));
        AppendFingerprintValue(hash, source.Origin.ToString());
        AppendFingerprintBytes(hash, source.Bytes);
    }

    private static void AppendFingerprintTarget(
        IncrementalHash hash,
        ProjectPaths paths,
        string virtualPath,
        ZaOutputMode outputMode)
    {
        var targetPath = ZaWorkflowFileSource.ResolveOutputPath(paths, virtualPath, outputMode);
        AppendFingerprintValue(hash, Path.GetFullPath(targetPath));
        if (File.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "File");
            AppendFingerprintBytes(hash, File.ReadAllBytes(targetPath));
        }
        else if (Directory.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "Directory");
        }
        else
        {
            AppendFingerprintValue(hash, "Missing");
        }
    }

    private static void AppendFingerprintValue(IncrementalHash hash, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "<null>");
        AppendFingerprintBytes(hash, bytes);
    }

    private static void AppendFingerprintBytes(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
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

    private static string CreateRecordId(string shopId, int slot, string rowId) =>
        ZaShopsWorkflowService.CreateInventoryRecordId(shopId, slot, rowId);

    private static bool TryParseRecordId(string? recordId, out string shopId, out int slot) =>
        ZaShopsWorkflowService.TryParseInventoryRecordId(recordId, out shopId, out slot);

    private static bool TryParseRecordRowId(
        string? recordId,
        out string shopId,
        out int slot,
        out string? rowId) =>
        ZaShopsWorkflowService.TryParseInventoryRecordRowId(recordId, out shopId, out slot, out rowId);

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
