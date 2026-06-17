// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.ShinyRate;

public sealed class SwShShinyRateEditSessionService
{
    public const string ShinyRateEditDomain = "workflow.shinyRate";

    private const string RecordId = "shiny-rate";
    private const string RateField = "rate";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShShinyRateWorkflowService shinyRateWorkflowService;

    public SwShShinyRateEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShShinyRateWorkflowService? shinyRateWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.shinyRateWorkflowService = shinyRateWorkflowService ?? new SwShShinyRateWorkflowService();
    }

    public SwShShinyRateEditResult StageRate(
        ProjectPaths paths,
        string mode,
        int? rollCount,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = shinyRateWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, ShinyRateEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate needs its own edit session before staging.",
                expected: "A Shiny Rate-only edit session"));
            return new SwShShinyRateEditResult(workflow, currentSession, diagnostics);
        }

        if (!TryCreateSelection(mode, rollCount, diagnostics, out var selection)
            || !CanStage(project, workflow, diagnostics))
        {
            return new SwShShinyRateEditResult(workflow, currentSession, diagnostics);
        }

        var payload = EncodeSelection(selection);
        var source = SwShShinyRateWorkflowService.ResolveWorkflowFile(project, SwShShinyRateWorkflowService.ExeFsMainPath);
        var sourceReferences = source is null
            ? [new ProjectFileReference(ProjectFileLayer.Base, SwShShinyRateWorkflowService.ExeFsMainPath)]
            : new[] { CreateSourceReference(source.Entry) };
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, ShinyRateEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(payload, sourceReferences, selection))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Shiny Rate is staged for change-plan review."));

        return new SwShShinyRateEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = shinyRateWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Shiny Rate before validating.",
                expected: "Pending Shiny Rate selection"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate expects exactly one staged rate edit.",
                expected: "One pending Shiny Rate edit"));
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, ShinyRateEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Shiny Rate.",
                    expected: ShinyRateEditDomain));
                continue;
            }

            if (!string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Shiny Rate edit '{edit.RecordId}' is not supported.",
                    expected: "Shiny Rate selection"));
                continue;
            }

            _ = DecodeSelection(edit.NewValue, diagnostics);
            CanStage(project, workflow, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Shiny Rate change is valid for change-plan review."));
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

        var project = projectWorkspaceService.Open(paths);
        var source = SwShShinyRateWorkflowService.ResolveWorkflowFile(project, SwShShinyRateWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate source or output target could not be resolved.",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            [CreateSourceReference(source.Entry)],
            File.Exists(targetPath),
            "Update the Sword/Shield shiny reroll count in exefs/main.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Shiny Rate change plan preview contains 1 target file."));

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
                "Reviewed Shiny Rate change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Shiny Rate change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var selection = DecodeSelection(session.PendingEdits.Single().NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        ApplyMain(paths, selection, writtenFiles, diagnostics);
        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private void ApplyMain(
        ProjectPaths paths,
        ShinyRateSelection selection,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = SwShShinyRateWorkflowService.ResolveWorkflowFile(project, SwShShinyRateWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate source or output target could not be resolved.",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return;
        }

        try
        {
            var output = SwShShinyRateMainPatcher.ApplyRate(
                File.ReadAllBytes(source.AbsolutePath),
                selection.Mode,
                selection.RollCount,
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShShinyRateWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Shiny Rate changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shiny Rate could not be patched: {exception.Message}",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield 1.3.2 exefs/main with the verified shiny reroll loop"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shiny Rate output could not be written: {exception.Message}",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shiny Rate output could not be written: {exception.Message}",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShShinyRateWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (workflow.InstallStatus == "blocked")
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shiny Rate cannot stage while exefs/main has an unsupported or ambiguous shiny reroll loop.",
                    expected: "Known Sword/Shield 1.3.2 exefs/main shiny reroll loop"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool TryCreateSelection(
        string mode,
        int? rollCount,
        ICollection<ValidationDiagnostic> diagnostics,
        out ShinyRateSelection selection)
    {
        selection = new ShinyRateSelection(SwShShinyRateMode.Default, RollCount: null);
        if (!TryParseMode(mode, out var parsedMode))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shiny Rate mode '{mode}' is not supported.",
                field: RateField,
                expected: "default, fixed, or always"));
            return false;
        }

        if (parsedMode == SwShShinyRateMode.FixedRolls)
        {
            try
            {
                SwShShinyRateMainPatcher.ValidateRollCount(rollCount);
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    exception.Message,
                    field: RateField,
                    expected: string.Create(
                        CultureInfo.InvariantCulture,
                        $"{SwShShinyRateMainPatcher.MinimumFixedRollCount}-{SwShShinyRateMainPatcher.MaximumFixedRollCount} rolls")));
                return false;
            }
        }

        selection = new ShinyRateSelection(
            parsedMode,
            parsedMode == SwShShinyRateMode.FixedRolls ? rollCount : null);
        return true;
    }

    private static string EncodeSelection(ShinyRateSelection selection)
    {
        return selection.Mode switch
        {
            SwShShinyRateMode.Default => "default",
            SwShShinyRateMode.AlwaysShiny => "always",
            SwShShinyRateMode.FixedRolls => string.Create(CultureInfo.InvariantCulture, $"fixed:{selection.RollCount}"),
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
    }

    private static ShinyRateSelection DecodeSelection(string? value, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate pending edit has no rate payload.",
                field: RateField,
                expected: "default, always, or fixed:<roll count>"));
            return new ShinyRateSelection(SwShShinyRateMode.Default, RollCount: null);
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "default", StringComparison.Ordinal))
        {
            return new ShinyRateSelection(SwShShinyRateMode.Default, RollCount: null);
        }

        if (string.Equals(trimmed, "always", StringComparison.Ordinal))
        {
            return new ShinyRateSelection(SwShShinyRateMode.AlwaysShiny, RollCount: null);
        }

        const string fixedPrefix = "fixed:";
        if (trimmed.StartsWith(fixedPrefix, StringComparison.Ordinal)
            && int.TryParse(trimmed[fixedPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var rollCount))
        {
            if (TryCreateSelection("fixed", rollCount, diagnostics, out var selection))
            {
                return selection;
            }

            return new ShinyRateSelection(SwShShinyRateMode.Default, RollCount: null);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Shiny Rate pending edit payload is not supported.",
            field: RateField,
            expected: "default, always, or fixed:<roll count>"));
        return new ShinyRateSelection(SwShShinyRateMode.Default, RollCount: null);
    }

    private static PendingEdit CreatePendingEdit(
        string payload,
        IReadOnlyList<ProjectFileReference> sourceReferences,
        ShinyRateSelection selection)
    {
        return new PendingEdit(
            ShinyRateEditDomain,
            CreatePendingEditSummary(selection),
            sourceReferences,
            RecordId,
            RateField,
            payload);
    }

    private static string CreatePendingEditSummary(ShinyRateSelection selection)
    {
        return selection.Mode switch
        {
            SwShShinyRateMode.Default => "Stage Shiny Rate default reroll logic.",
            SwShShinyRateMode.AlwaysShiny => "Stage Shiny Rate always-shiny patch.",
            SwShShinyRateMode.FixedRolls => string.Create(
                CultureInfo.InvariantCulture,
                $"Stage Shiny Rate fixed {selection.RollCount} roll{(selection.RollCount == 1 ? string.Empty : "s")}."),
            _ => "Stage Shiny Rate.",
        };
    }

    private static bool TryParseMode(string mode, out SwShShinyRateMode parsedMode)
    {
        parsedMode = mode.Trim().ToLowerInvariant() switch
        {
            "default" => SwShShinyRateMode.Default,
            "fixed" => SwShShinyRateMode.FixedRolls,
            "always" => SwShShinyRateMode.AlwaysShiny,
            _ => SwShShinyRateMode.Default,
        };

        return mode.Trim().ToLowerInvariant() is "default" or "fixed" or "always";
    }

    private static ProjectFileReference CreateSourceReference(ProjectFileGraphEntry entry)
    {
        var layer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
        return new ProjectFileReference(layer, entry.RelativePath);
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShShinyRateWorkflowService.ResolveOutputPath(paths, SwShShinyRateWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate target must stay inside the configured output root.",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
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
            Domain: ShinyRateEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record ShinyRateSelection(
        SwShShinyRateMode Mode,
        int? RollCount);
}
