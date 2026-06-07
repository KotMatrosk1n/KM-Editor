// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Items;

public sealed class SwShItemsEditSessionService
{
    public const string BuyPriceField = SwShItemsWorkflowService.BuyPriceField;
    public const string SellPriceField = SwShItemsWorkflowService.SellPriceField;
    public const string WattsPriceField = SwShItemsWorkflowService.WattsPriceField;
    public const string AlternatePriceField = SwShItemsWorkflowService.AlternatePriceField;

    private const string ItemsEditDomain = "workflow.items";

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

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Items change plan"));
        }

        var targetPath = ResolveOutputPath(paths, SwShItemsWorkflowService.ItemDataPath, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) || targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var itemDataSource = SwShItemsWorkflowService.ResolveItemDataSource(project);
        if (itemDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply could not resolve the source item table.",
                expected: SwShItemsWorkflowService.ItemDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var itemTable = SwShItemTable.Parse(File.ReadAllBytes(itemDataSource.AbsolutePath));
            var itemTableEdits = session.PendingEdits
                .Select(edit => ToItemTableEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = itemTable.WriteEdits(itemTableEdits);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShItemsWorkflowService.ItemDataPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Items change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items source file could not be decoded: {exception.Message}",
                expected: "Sword/Shield item.dat"));
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
        var itemField = GetEditableField(normalizedField);
        if (itemField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!TryParseItemValue(value, itemField.MaximumValue, out var itemValue))
        {
            diagnostics.Add(CreateItemValueRangeDiagnostic(itemField));
            return null;
        }

        return new PendingEdit(
            ItemsEditDomain,
            CreatePendingEditSummary(selectedItem, itemField, itemValue),
            [new ProjectFileReference(selectedItem.Provenance.SourceLayer, selectedItem.Provenance.SourceFile)],
            RecordId: selectedItem.ItemId.ToString(CultureInfo.InvariantCulture),
            Field: itemField.Field,
            NewValue: itemValue.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParsePendingEditValue(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemField = GetEditableField(edit.Field);
        if (itemField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        if (!TryParseItemValue(edit.NewValue, itemField.MaximumValue, out var itemValue))
        {
            diagnostics.Add(CreateItemValueRangeDiagnostic(itemField));
            return null;
        }

        return itemValue;
    }

    private static bool TryParseItemValue(string? value, int maximumValue, out int itemValue)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out itemValue)
            && itemValue >= 0
            && itemValue <= maximumValue;
    }

    private static ItemField? GetEditableField(string? field)
    {
        return field switch
        {
            BuyPriceField => new ItemField(
                BuyPriceField,
                "buy price",
                SwShItemsWorkflowService.MaximumBuyPrice,
                SwShItemTableField.BuyPrice,
                ActualValueMultiplier: 1),
            SellPriceField => new ItemField(
                SellPriceField,
                "sell price",
                SwShItemsWorkflowService.MaximumSellPrice,
                SwShItemTableField.BuyPrice,
                ActualValueMultiplier: 2),
            WattsPriceField => new ItemField(
                WattsPriceField,
                "Watts price",
                SwShItemsWorkflowService.MaximumWattsPrice,
                SwShItemTableField.WattsPrice,
                ActualValueMultiplier: 1),
            AlternatePriceField => new ItemField(
                AlternatePriceField,
                "alternate price",
                SwShItemsWorkflowService.MaximumAlternatePrice,
                SwShItemTableField.AlternatePrice,
                ActualValueMultiplier: 1),
            _ => null,
        };
    }

    private static EditSession ReplacePendingItemEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameItemTableFieldEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameItemTableFieldEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        var candidateField = GetEditableField(candidate.Field);
        var pendingField = GetEditableField(pendingEdit.Field);

        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && candidateField is not null
            && pendingField is not null
            && candidateField.TableField == pendingField.TableField;
    }

    private static SwShItemsWorkflow OverlayPendingEdit(
        SwShItemsWorkflow workflow,
        PendingEdit edit)
    {
        var itemField = GetEditableField(edit.Field);
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || itemField is null
            || !TryParseItemValue(edit.NewValue, itemField.MaximumValue, out var itemValue))
        {
            return workflow;
        }

        return itemField.Field switch
        {
            BuyPriceField => OverlayItem(workflow, itemId, item => item with
            {
                BuyPrice = itemValue,
                SellPrice = itemValue / 2,
            }),
            SellPriceField => OverlayItem(workflow, itemId, item => item with
            {
                BuyPrice = itemValue * itemField.ActualValueMultiplier,
                SellPrice = itemValue,
            }),
            WattsPriceField => OverlayItem(workflow, itemId, item => item with { WattsPrice = itemValue }),
            AlternatePriceField => OverlayItem(workflow, itemId, item => item with { AlternatePrice = itemValue }),
            _ => workflow,
        };
    }

    private static SwShItemsWorkflow OverlayItem(
        SwShItemsWorkflow workflow,
        int itemId,
        Func<SwShItemRecord, SwShItemRecord> update)
    {
        var items = workflow.Items
            .Select(item => item.ItemId == itemId ? update(item) : item)
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
        var targetRelativePath = SwShItemsWorkflowService.ItemDataPath;
        var targetPath = SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
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

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
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

        var targetPath = SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static SwShItemTableEdit? ToItemTableEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemField = GetEditableField(edit.Field);
        var itemValue = TryParsePendingEditValue(edit, diagnostics);

        if (itemField is null || itemValue is null)
        {
            return null;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit does not include a valid item ID.",
                field: "itemId",
                expected: "Existing item record"));
            return null;
        }

        return new SwShItemTableEdit(
            itemId,
            itemField.TableField,
            checked((uint)(itemValue.Value * itemField.ActualValueMultiplier)));
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

    private static string CreatePendingEditSummary(
        SwShItemRecord item,
        ItemField itemField,
        int itemValue)
    {
        var sharedRowSuffix = item.SharedItemIds.Count > 1
            ? $" Shared row also affects item IDs {string.Join(", ", item.SharedItemIds.Where(id => id != item.ItemId))}."
            : string.Empty;
        var derivedSuffix = itemField.Field == SellPriceField
            ? $" Stored buy price will become {itemValue * itemField.ActualValueMultiplier}."
            : string.Empty;

        return $"Set {item.Name} {itemField.DisplayName} to {itemValue}.{derivedSuffix}{sharedRowSuffix}";
    }

    private static ValidationDiagnostic CreateItemValueRangeDiagnostic(ItemField itemField)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Item {itemField.DisplayName} must be between 0 and {itemField.MaximumValue}.",
            field: itemField.Field,
            expected: $"Safe item {itemField.DisplayName}");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Item field '{field}' is not supported by the Items workflow yet.",
            field: "field",
            expected: $"{BuyPriceField}, {SellPriceField}, {WattsPriceField}, or {AlternatePriceField}");
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

    private sealed record ItemField(
        string Field,
        string DisplayName,
        int MaximumValue,
        SwShItemTableField TableField,
        int ActualValueMultiplier);
}
