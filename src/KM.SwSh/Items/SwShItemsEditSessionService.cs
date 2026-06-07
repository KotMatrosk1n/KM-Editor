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
    public const string BuyPriceField = "buyPrice";

    private const string ItemsEditDomain = "workflow.items";
    private const int MaximumBuyPrice = 999_999;

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

    public SwShItemsEditResult UpdateBuyPrice(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        int buyPrice)
    {
        ArgumentNullException.ThrowIfNull(paths);

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

        if (!IsBuyPriceInRange(buyPrice))
        {
            diagnostics.Add(CreateBuyPriceRangeDiagnostic());
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = new PendingEdit(
            ItemsEditDomain,
            $"Set {selectedItem.Name} buy price to {buyPrice}.",
            [new ProjectFileReference(selectedItem.Provenance.SourceLayer, selectedItem.Provenance.SourceFile)],
            RecordId: selectedItem.ItemId.ToString(CultureInfo.InvariantCulture),
            Field: BuyPriceField,
            NewValue: buyPrice.ToString(CultureInfo.InvariantCulture));
        var updatedSession = currentSession.WithPendingEdit(pendingEdit);

        // This is a preview overlay only; apply/change-plan support is deliberately outside this branch.
        return new SwShItemsEditResult(
            OverlayPendingBuyPrice(workflow, itemId, buyPrice),
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
                "Pending item buy price change is valid.",
                field: BuyPriceField));
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

        var writes = session.PendingEdits
            .Select(edit => CreatePlannedWrite(paths, edit))
            .ToArray();

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

        if (!string.Equals(edit.Field, BuyPriceField, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending item field '{edit.Field ?? "(missing)"}' is not supported yet.",
                expected: BuyPriceField));
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

        if (!int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var buyPrice)
            || !IsBuyPriceInRange(buyPrice))
        {
            diagnostics.Add(CreateBuyPriceRangeDiagnostic());
        }
    }

    private static bool IsBuyPriceInRange(int buyPrice)
    {
        return buyPrice is >= 0 and <= MaximumBuyPrice;
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

    private static SwShItemsWorkflow OverlayPendingEdits(
        SwShItemsWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            if (string.Equals(edit.Field, BuyPriceField, StringComparison.Ordinal)
                && int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var buyPrice))
            {
                updatedWorkflow = OverlayPendingBuyPrice(updatedWorkflow, itemId, buyPrice);
            }
        }

        return updatedWorkflow;
    }

    private static PlannedFileWrite CreatePlannedWrite(ProjectPaths paths, PendingEdit edit)
    {
        var targetRelativePath = SwShItemsWorkflowService.ItemsReadModelPath;
        var targetPath = CombineGraphPath(paths.OutputRootPath, targetRelativePath);

        return new PlannedFileWrite(
            targetRelativePath,
            edit.Sources,
            !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath),
            $"Apply pending Items edit: {edit.Summary}");
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

    private static ValidationDiagnostic CreateBuyPriceRangeDiagnostic()
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Item buy price must be between 0 and {MaximumBuyPrice}.",
            field: BuyPriceField,
            expected: "Safe item buy price");
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
}
