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
        var workflow = shopsWorkflowService.Load(project);
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

        var inventoryItem = selectedShop.Inventory.FirstOrDefault(item => item.Slot == slot);
        if (inventoryItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shop '{selectedShop.Name}' does not have inventory slot {slot}.",
                field: "slot",
                expected: "Existing shop inventory slot"));
            return new SwShShopsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedShop, inventoryItem, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShShopsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingShopEdit(currentSession, pendingEdit);

        return new SwShShopsEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
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

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
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
        if (shop is null || shop.Inventory.All(item => item.Slot != slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending shop edit targets a slot that is not loaded.",
                field: "slot",
                expected: "Existing shop inventory slot"));
            return;
        }

        TryParseItemId(edit.NewValue, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShShopRecord shop,
        SwShShopInventoryRecord inventoryItem,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!SwShShopsWorkflowService.IsEditableField(normalizedField))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var itemId = TryParseItemId(value, diagnostics);
        if (itemId is null)
        {
            return null;
        }

        return new PendingEdit(
            ShopsEditDomain,
            $"Set {shop.Name} slot {inventoryItem.Slot} item ID to {itemId.Value}.",
            [new ProjectFileReference(shop.Provenance.SourceLayer, shop.Provenance.SourceFile)],
            RecordId: SwShShopsWorkflowService.CreateInventoryRecordId(shop.ShopId, inventoryItem.Slot),
            Field: SwShShopsWorkflowService.ItemIdField,
            NewValue: itemId.Value.ToString(CultureInfo.InvariantCulture));
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

    private static EditSession ReplacePendingShopEdit(EditSession session, PendingEdit pendingEdit)
    {
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
            || !SwShShopsWorkflowService.TryParseInventoryRecordId(edit.RecordId, out var shopId, out var slot)
            || TryParseItemId(edit.NewValue, new List<ValidationDiagnostic>()) is not { } itemId)
        {
            return workflow;
        }

        return workflow with
        {
            Shops = workflow.Shops
                .Select(shop => shop.ShopId == shopId
                    ? shop with
                    {
                        Inventory = shop.Inventory
                            .Select(item => item.Slot == slot
                                ? item with
                                {
                                    ItemId = itemId,
                                    ItemName = $"Item {itemId}",
                                    Price = 0,
                                }
                                : item)
                            .ToArray(),
                    }
                    : shop)
                .ToArray(),
        };
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
        var itemId = TryParseItemId(edit.NewValue, diagnostics);
        if (itemId is null)
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
            itemId.Value);
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
