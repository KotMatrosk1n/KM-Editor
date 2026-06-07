// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Text.Json;

namespace KM.SwSh.Items;

public sealed class SwShItemsEditSessionService
{
    public const string BuyPriceField = SwShItemsWorkflowService.BuyPriceField;
    public const string SellPriceField = SwShItemsWorkflowService.SellPriceField;

    private const string ItemsEditDomain = "workflow.items";

    private static readonly JsonSerializerOptions WriteModelJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShItemsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShItemsEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = itemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditItems(project, workflow, diagnostics))
        {
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var selectedItem = workflow.Items.FirstOrDefault(item => item.ItemId == itemId);
        if (selectedItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {itemId} is not present in the loaded Items workflow.",
                field: "itemId",
                expected: "Existing item record"));
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedItem, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingItemEdit(currentSession, pendingEdit);

        return new SwShItemsEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = itemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditItems(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending item change is valid."));
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

        // Plan review reuses edit-session validation so invalid pending edits cannot produce write targets.
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Items edit before reviewing a change plan.",
                expected: "Pending item edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = session.PendingEdits.Count == 0
            ? Array.Empty<PlannedFileWrite>()
            : [CreatePlannedWrite(paths, session.PendingEdits)];

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Length} target file{(writes.Length == 1 ? string.Empty : "s")}."));

        return new ChangePlan(session.Id, writes, diagnostics);
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

        // The reviewed plan is treated as an approval token only; target paths still come from the current backend plan.
        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Items change plan"));
        }

        var targetPath = ResolveOutputPath(paths.OutputRootPath, SwShItemsWorkflowService.ItemsReadModelPath, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) || targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = OverlayPendingEdits(itemsWorkflowService.Load(project), session.PendingEdits);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var stream = File.Create(targetPath);
            JsonSerializer.Serialize(stream, ToWriteModel(workflow), WriteModelJsonOptions);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShItemsWorkflowService.ItemsReadModelPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Items change plan to the configured output root."));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items output file could not be written: {exception.Message}",
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items output file could not be written: {exception.Message}",
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditItems(
        OpenedProject project,
        SwShItemsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items edit sessions require valid base paths and a valid output root.",
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
        SwShItemsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ItemsEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Items workflow.",
                expected: ItemsEditDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || workflow.Items.All(item => item.ItemId != itemId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit targets a record that is not loaded.",
                field: "itemId",
                expected: "Existing item record"));
            return;
        }

        if (TryParsePendingEditValue(edit, diagnostics) is null)
        {
            return;
        }
    }

    private static PendingEdit? CreatePendingEdit(
        SwShItemRecord selectedItem,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();

        var priceField = GetPriceField(normalizedField);
        if (priceField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!TryParseItemPrice(value, priceField.MaximumValue, out var itemPrice))
        {
            diagnostics.Add(CreateItemPriceRangeDiagnostic(priceField));
            return null;
        }

        return new PendingEdit(
            ItemsEditDomain,
            $"Set {selectedItem.Name} {priceField.DisplayName} to {itemPrice}.",
            [new ProjectFileReference(selectedItem.Provenance.SourceLayer, selectedItem.Provenance.SourceFile)],
            RecordId: selectedItem.ItemId.ToString(CultureInfo.InvariantCulture),
            Field: priceField.Field,
            NewValue: itemPrice.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParsePendingEditValue(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var priceField = GetPriceField(edit.Field);
        if (priceField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        if (!TryParseItemPrice(edit.NewValue, priceField.MaximumValue, out var itemPrice))
        {
            diagnostics.Add(CreateItemPriceRangeDiagnostic(priceField));
            return null;
        }

        return itemPrice;
    }

    private static bool TryParseItemPrice(string? value, int maximumValue, out int itemPrice)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out itemPrice)
            && itemPrice >= 0
            && itemPrice <= maximumValue;
    }

    private static ItemPriceField? GetPriceField(string? field)
    {
        return field switch
        {
            BuyPriceField => new ItemPriceField(BuyPriceField, "buy price", SwShItemsWorkflowService.MaximumBuyPrice),
            SellPriceField => new ItemPriceField(SellPriceField, "sell price", SwShItemsWorkflowService.MaximumSellPrice),
            _ => null,
        };
    }

    private static EditSession ReplacePendingItemEdit(EditSession session, PendingEdit pendingEdit)
    {
        // Keep one draft per item field so repeated inspector saves update the pending change.
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameItemFieldEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameItemFieldEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShItemsWorkflow OverlayPendingEdit(
        SwShItemsWorkflow workflow,
        PendingEdit edit)
    {
        var priceField = GetPriceField(edit.Field);
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || priceField is null
            || !TryParseItemPrice(edit.NewValue, priceField.MaximumValue, out var itemPrice))
        {
            return workflow;
        }

        return priceField.Field switch
        {
            BuyPriceField => OverlayPendingBuyPrice(workflow, itemId, itemPrice),
            SellPriceField => OverlayPendingSellPrice(workflow, itemId, itemPrice),
            _ => workflow,
        };
    }

    private static SwShItemsWorkflow OverlayPendingBuyPrice(
        SwShItemsWorkflow workflow,
        int itemId,
        int buyPrice)
    {
        var items = workflow.Items
            .Select(item => item.ItemId == itemId ? item with { BuyPrice = buyPrice } : item)
            .ToArray();

        return workflow with { Items = items };
    }

    private static SwShItemsWorkflow OverlayPendingSellPrice(
        SwShItemsWorkflow workflow,
        int itemId,
        int sellPrice)
    {
        var items = workflow.Items
            .Select(item => item.ItemId == itemId ? item with { SellPrice = sellPrice } : item)
            .ToArray();

        return workflow with { Items = items };
    }

    private static SwShItemsWorkflow OverlayPendingEdits(
        SwShItemsWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static PlannedFileWrite CreatePlannedWrite(ProjectPaths paths, IReadOnlyList<PendingEdit> edits)
    {
        var targetRelativePath = SwShItemsWorkflowService.ItemsReadModelPath;
        var targetPath = CombineGraphPath(paths.OutputRootPath, targetRelativePath);
        var sources = edits
            .SelectMany(edit => edit.Sources)
            .Distinct()
            .ToArray();
        var reason = edits.Count == 1
            ? $"Apply pending Items edit: {edits[0].Summary}"
            : $"Apply {edits.Count} pending Items edits: {string.Join(" ", edits.Select(edit => edit.Summary))}";

        return new PlannedFileWrite(
            targetRelativePath,
            sources,
            !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath),
            reason);
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

    private static string? ResolveOutputPath(
        string? outputRootPath,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(outputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply target must be relative to the output root.",
                expected: "Relative output target"));
            return null;
        }

        var outputRoot = Path.GetFullPath(outputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRootWithSeparator = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(outputRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
            return null;
        }

        return targetPath;
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

    private static ItemsWriteModel ToWriteModel(SwShItemsWorkflow workflow)
    {
        return new ItemsWriteModel(
            SchemaVersion: 1,
            workflow.Items
                .OrderBy(item => item.ItemId)
                .Select(item => new ItemsWriteModelRecord(
                    item.ItemId,
                    item.Name,
                    item.Category,
                    item.BuyPrice,
                    item.SellPrice))
                .ToArray());
    }

    private static ValidationDiagnostic CreateItemPriceRangeDiagnostic(ItemPriceField priceField)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Item {priceField.DisplayName} must be between 0 and {priceField.MaximumValue}.",
            field: priceField.Field,
            expected: $"Safe item {priceField.DisplayName}");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Item field '{field}' is not supported by the Items workflow yet.",
            field: "field",
            expected: $"{BuyPriceField} or {SellPriceField}");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            Domain: ItemsEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record ItemsWriteModel(
        int SchemaVersion,
        IReadOnlyList<ItemsWriteModelRecord> Items);

    private sealed record ItemsWriteModelRecord(
        int ItemId,
        string Name,
        string Category,
        int BuyPrice,
        int SellPrice);

    private sealed record ItemPriceField(
        string Field,
        string DisplayName,
        int MaximumValue);
}
