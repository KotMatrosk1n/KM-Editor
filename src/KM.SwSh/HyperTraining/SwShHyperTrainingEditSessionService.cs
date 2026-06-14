// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.HyperTraining;

public sealed class SwShHyperTrainingEditSessionService
{
    public const string HyperTrainingEditDomain = "workflow.hyperTraining";

    private const string RecordId = "hyper-training-minimum-level";
    private const string MinimumLevelField = "minimumLevel";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShHyperTrainingWorkflowService hyperTrainingWorkflowService;

    public SwShHyperTrainingEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.hyperTrainingWorkflowService = hyperTrainingWorkflowService ?? new SwShHyperTrainingWorkflowService();
    }

    public SwShHyperTrainingEditResult StageMinimumLevel(
        ProjectPaths paths,
        int minimumLevel,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperTrainingWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training needs its own edit session before staging.",
                expected: "A Hyper Training-only edit session"));
            return new SwShHyperTrainingEditResult(workflow, currentSession, diagnostics);
        }

        if (!ValidateMinimumLevel(minimumLevel, diagnostics) || !CanStage(project, workflow, diagnostics))
        {
            return new SwShHyperTrainingEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(minimumLevel))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Hyper Training minimum level Lv.{minimumLevel} is staged for change-plan review.")));

        return new SwShHyperTrainingEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperTrainingWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage a Hyper Training minimum level before validating.",
                expected: "Pending Hyper Training minimum level"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training expects exactly one staged minimum-level edit.",
                expected: "One pending Hyper Training edit"));
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Hyper Training.",
                    expected: HyperTrainingEditDomain));
                continue;
            }

            if (!string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Hyper Training edit '{edit.RecordId}' is not supported.",
                    expected: "Hyper Training minimum level"));
                continue;
            }

            _ = ParsePendingMinimumLevel(edit.NewValue, diagnostics);
            CanStage(project, workflow, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Hyper Training change is valid for change-plan review."));
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
        var pendingEdit = session.PendingEdits.Single();
        var minimumLevel = ParsePendingMinimumLevel(pendingEdit.NewValue, diagnostics);
        var scriptSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ScriptPath);
        var scriptTargetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ScriptPath, diagnostics);
        var mainSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ExeFsMainPath);
        var mainTargetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ExeFsMainPath, diagnostics);
        if (scriptSource is null || scriptTargetPath is null || mainSource is null || mainTargetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training source or output target could not be resolved.",
                file: scriptSource is null ? SwShHyperTrainingWorkflowService.ScriptPath : SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Readable script/main sources and writable LayeredFS targets"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>
        {
            new(
                SwShHyperTrainingWorkflowService.ScriptPath,
                [CreateSourceReference(scriptSource.Entry)],
                File.Exists(scriptTargetPath),
                string.Create(CultureInfo.InvariantCulture, $"Set the Battle Tower Hyper Training script minimum level to Lv.{minimumLevel}.")),
            new(
                SwShHyperTrainingWorkflowService.ExeFsMainPath,
                [CreateSourceReference(mainSource.Entry)],
                File.Exists(mainTargetPath),
                string.Create(CultureInfo.InvariantCulture, $"Update the Hyper Training party/box picker cutoff checks to Lv.{minimumLevel} in exefs/main.")),
        };

        var dialogueSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        if (dialogueSource is not null)
        {
            var dialogueTargetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.EnglishDialoguePath, diagnostics);
            if (dialogueTargetPath is not null)
            {
                writes.Add(new PlannedFileWrite(
                    SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                    [CreateSourceReference(dialogueSource.Entry)],
                    File.Exists(dialogueTargetPath),
                    string.Create(CultureInfo.InvariantCulture, $"Update English Hyper Training NPC dialogue to mention Lv.{minimumLevel}.")));
            }
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Hyper Training change plan preview contains {writes.Count:N0} target file(s).")));

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
                "Reviewed Hyper Training change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Hyper Training change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var minimumLevel = ParsePendingMinimumLevel(session.PendingEdits.Single().NewValue, diagnostics);
        ApplyScript(paths, minimumLevel, writtenFiles, diagnostics);
        ApplyMain(paths, minimumLevel, writtenFiles, diagnostics);
        ApplyDialogue(paths, minimumLevel, currentPlan, writtenFiles, diagnostics);

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private void ApplyMain(
        ProjectPaths paths,
        int minimumLevel,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ExeFsMainPath, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training picker source or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return;
        }

        try
        {
            var output = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(
                File.ReadAllBytes(source.AbsolutePath),
                minimumLevel,
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training picker runtime changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training picker runtime could not be patched: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield 1.3.2 exefs/main with known Hyper Training picker checks"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training picker runtime output could not be written: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training picker runtime output could not be written: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private void ApplyScript(
        ProjectPaths paths,
        int minimumLevel,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.ScriptPath);
        var targetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.ScriptPath, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training script source or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Readable source and writable LayeredFS target"));
            return;
        }

        try
        {
            var output = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(
                File.ReadAllBytes(source.AbsolutePath),
                minimumLevel);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.ScriptPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training script changes to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training script could not be patched: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Dedicated Battle Tower hyper_training.amx with the known level check"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training script output could not be written: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training script output could not be written: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Writable output root"));
        }
    }

    private void ApplyDialogue(
        ProjectPaths paths,
        int minimumLevel,
        ChangePlan currentPlan,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!currentPlan.Writes.Any(write => string.Equals(
                write.TargetRelativePath,
                SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        var targetPath = ResolveOutputPath(paths, SwShHyperTrainingWorkflowService.EnglishDialoguePath, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training dialogue source or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                expected: "Readable source and writable LayeredFS target"));
            return;
        }

        try
        {
            var output = SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(
                File.ReadAllBytes(source.AbsolutePath),
                minimumLevel);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.EnglishDialoguePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training dialogue changes to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training dialogue could not be patched: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                expected: "Sword/Shield encrypted text table with Hyper Training dialogue"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training dialogue output could not be written: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training dialogue output could not be written: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShHyperTrainingWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training apply requires valid base paths and a valid output root.",
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
                    "Hyper Training cannot stage while the script or picker runtime has an unsupported level-check shape.",
                    expected: "Known Hyper Training AMX level check and selected-game exefs/main picker checks"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool ValidateMinimumLevel(int minimumLevel, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (minimumLevel is >= SwShHyperTrainingAmxPatcher.MinimumAllowedLevel and <= SwShHyperTrainingAmxPatcher.MaximumAllowedLevel)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Hyper Training minimum level must be between {SwShHyperTrainingAmxPatcher.MinimumAllowedLevel} and {SwShHyperTrainingAmxPatcher.MaximumAllowedLevel}."),
            field: MinimumLevelField,
            expected: "Integer level 1-100"));
        return false;
    }

    private static int ParsePendingMinimumLevel(string? value, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minimumLevel))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training pending edit has no valid minimum-level payload.",
                field: MinimumLevelField,
                expected: "Integer level 1-100"));
            return SwShHyperTrainingAmxPatcher.VanillaMinimumLevel;
        }

        ValidateMinimumLevel(minimumLevel, diagnostics);
        return minimumLevel;
    }

    private static PendingEdit CreatePendingEdit(int minimumLevel)
    {
        return new PendingEdit(
            HyperTrainingEditDomain,
            string.Create(CultureInfo.InvariantCulture, $"Stage Hyper Training minimum level Lv.{minimumLevel}."),
            [
                new ProjectFileReference(ProjectFileLayer.Base, SwShHyperTrainingWorkflowService.ScriptPath),
                new ProjectFileReference(ProjectFileLayer.Base, SwShHyperTrainingWorkflowService.EnglishDialoguePath),
                new ProjectFileReference(ProjectFileLayer.Base, SwShHyperTrainingWorkflowService.ExeFsMainPath),
            ],
            RecordId,
            MinimumLevelField,
            minimumLevel.ToString(CultureInfo.InvariantCulture));
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
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training target must stay inside the configured output root.",
                file: targetRelativePath,
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
            Domain: HyperTrainingEditDomain,
            Field: field,
            Expected: expected);
    }
}
