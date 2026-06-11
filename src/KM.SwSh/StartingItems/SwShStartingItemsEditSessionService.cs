// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.BagHook;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.StartingItems;

public sealed class SwShStartingItemsEditSessionService
{
    public const string StartingItemsEditDomain = "workflow.startingItems";

    private const string RecordId = "starting-items";
    private const string GrantsField = "grants";

    private readonly SwShStartingItemsWorkflowService startingItemsWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShStartingItemsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShStartingItemsWorkflowService? startingItemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.startingItemsWorkflowService = startingItemsWorkflowService ?? new SwShStartingItemsWorkflowService();
    }

    public SwShStartingItemsEditResult StageGrants(
        ProjectPaths paths,
        IReadOnlyList<SwShStartingItemGrantSelection> grants,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(grants);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = startingItemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, StartingItemsEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items needs its own edit session before staging.",
                expected: "A Starting Items-only edit session"));
            return new SwShStartingItemsEditResult(workflow, currentSession, diagnostics);
        }

        var normalized = NormalizeGrants(project, grants, diagnostics);
        if (!CanStage(project, workflow, diagnostics) || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShStartingItemsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, StartingItemsEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(normalized))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Starting Items grants are staged for change-plan review."));

        return new SwShStartingItemsEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = startingItemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Starting Items grants before validating.",
                expected: "Pending Starting Items grants"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, StartingItemsEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Starting Items.",
                    expected: StartingItemsEditDomain));
                continue;
            }

            _ = ParsePendingGrants(edit.NewValue, diagnostics);
            CanStage(project, workflow, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Starting Items grants are valid for change-plan review."));
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
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new[]
        {
            new PlannedFileWrite(
                SwShBagHookWorkflowService.BagEventScriptPath,
                [new ProjectFileReference(ProjectFileLayer.Layered, SwShBagHookWorkflowService.BagEventScriptPath)],
                File.Exists(targetPath),
                "Update Bag Hook slots 2-20 with reviewed Starting Items grants. Slot 1 remains reserved for Royal Candy."),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Starting Items change plan preview contains {writes.Length:N0} target file(s).")));

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
                "Reviewed Starting Items change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Starting Items change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var grants = ParsePendingGrants(session.PendingEdits.Single().NewValue, diagnostics);
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShBagHookWorkflowService.BagEventScriptPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items source or output target could not be resolved.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Installed Bag Hook V2 and writable LayeredFS target"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var patches = Enumerable.Range(SwShBagHookAmxPatcher.FirstStartingItemSlot, SwShBagHookAmxPatcher.LastStartingItemSlot - SwShBagHookAmxPatcher.FirstStartingItemSlot + 1)
                .Select(slot => grants.TryGetValue(slot, out var grant)
                    ? new SwShBagHookSlotPatch(slot, grant.ItemId, grant.ItemId is null ? null : grant.Quantity)
                    : new SwShBagHookSlotPatch(slot, null, null))
                .ToArray();
            var output = SwShBagHookAmxPatcher.ApplySlotPatches(File.ReadAllBytes(source.AbsolutePath), patches);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShBagHookWorkflowService.BagEventScriptPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Starting Items grants to Bag Hook slots 2-20 in the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Starting Items source file could not be patched: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Installed Bag Hook V2"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Starting Items output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Starting Items output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private IReadOnlyList<SwShStartingItemGrantSelection> NormalizeGrants(
        OpenedProject project,
        IReadOnlyList<SwShStartingItemGrantSelection> grants,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemLookup = startingItemsWorkflowService.LoadItemOptionLookup(project, diagnostics);
        var normalized = new List<SwShStartingItemGrantSelection>();
        var seenSlots = new HashSet<int>();
        foreach (var grant in grants)
        {
            if (grant.Slot is < SwShBagHookAmxPatcher.FirstStartingItemSlot or > SwShBagHookAmxPatcher.LastStartingItemSlot)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items slot {grant.Slot} is not available.",
                    field: GrantsField,
                    expected: "Bag Hook slots 2-20 only"));
                continue;
            }

            if (!seenSlots.Add(grant.Slot))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items slot {grant.Slot} was supplied more than once.",
                    field: GrantsField,
                    expected: "One item per slot"));
                continue;
            }

            if (grant.ItemId is null || grant.ItemId == 0)
            {
                normalized.Add(new SwShStartingItemGrantSelection(grant.Slot, null, 1));
                continue;
            }

            if (!itemLookup.TryGetValue(grant.ItemId.Value, out var item))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting item id {grant.ItemId} is not available.",
                    field: GrantsField,
                    expected: "Known item id"));
                continue;
            }

            var quantity = item.IsKeyItem ? 1 : grant.Quantity;
            if (quantity is < 1 or > 999)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting item '{item.Name}' quantity must be between 1 and 999.",
                    field: GrantsField,
                    expected: "Quantity 1-999"));
                continue;
            }

            normalized.Add(new SwShStartingItemGrantSelection(grant.Slot, grant.ItemId, quantity));
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? Array.Empty<SwShStartingItemGrantSelection>()
            : normalized.OrderBy(grant => grant.Slot).ToArray();
    }

    private static IReadOnlyDictionary<int, SwShStartingItemGrantSelection> ParsePendingGrants(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var grants = new Dictionary<int, SwShStartingItemGrantSelection>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return grants;
        }

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = part.Split(':', StringSplitOptions.TrimEntries);
            if (fields.Length != 3
                || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items entry '{part}' is not valid.",
                    field: GrantsField,
                    expected: "slot:itemId:quantity"));
                continue;
            }

            if (slot is < SwShBagHookAmxPatcher.FirstStartingItemSlot or > SwShBagHookAmxPatcher.LastStartingItemSlot)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items slot {slot} is not available.",
                    field: GrantsField,
                    expected: "Bag Hook slots 2-20 only"));
                continue;
            }

            grants[slot] = new SwShStartingItemGrantSelection(slot, itemId == 0 ? null : itemId, quantity);
        }

        return grants;
    }

    private static bool CanStage(
        OpenedProject project,
        SwShStartingItemsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus != "available")
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items requires installed Bag Hook V2 before staging grants.",
                expected: "Installed Bag Hook V2"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static PendingEdit CreatePendingEdit(IReadOnlyList<SwShStartingItemGrantSelection> grants)
    {
        var value = string.Join(
            ';',
            grants
                .Where(grant => grant.ItemId is not null)
                .Select(grant => string.Create(CultureInfo.InvariantCulture, $"{grant.Slot}:{grant.ItemId}:{grant.Quantity}")));
        return new PendingEdit(
            StartingItemsEditDomain,
            "Stage Starting Items grants in Bag Hook slots 2-20.",
            [new ProjectFileReference(ProjectFileLayer.Layered, SwShBagHookWorkflowService.BagEventScriptPath)],
            RecordId,
            GrantsField,
            value);
    }

    private static string? ResolveOutputPath(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShBagHookWorkflowService.ResolveOutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
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

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = SwShBagHookWorkflowService.ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: StartingItemsEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
