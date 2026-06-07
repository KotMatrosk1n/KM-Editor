// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Items;

public sealed class SwShItemsEditSessionService
{
    public const string BuyPriceField = "buyPrice";

    private const string ItemsEditDomain = "workflow.items";
    private const int MaximumBuyPrice = 999_999;

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
}
