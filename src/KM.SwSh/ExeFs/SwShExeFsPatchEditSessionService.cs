// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.ExeFs;

public sealed class SwShExeFsPatchEditSessionService
{
    public const string ExeFsPatchEditDomain = "workflow.exefsPatches";

    private const string PatchField = "patchId";

    private readonly SwShExeFsPatchWorkflowService exeFsPatchWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShExeFsPatchEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShExeFsPatchWorkflowService? exeFsPatchWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.exeFsPatchWorkflowService = exeFsPatchWorkflowService ?? new SwShExeFsPatchWorkflowService();
    }

    public void ClearMemoryCache()
    {
        exeFsPatchWorkflowService.ClearMemoryCache();
    }

    public SwShExeFsPatchEditResult StagePatch(
        ProjectPaths paths,
        string patchId,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(patchId);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = exeFsPatchWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, ExeFsPatchEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS patches need their own edit session before staging.",
                expected: "An ExeFS-only edit session"));
            return new SwShExeFsPatchEditResult(workflow, currentSession, diagnostics);
        }

        var selectedPatch = GetPatch(workflow, patchId, diagnostics);
        if (selectedPatch is null || !CanStage(project, workflow, selectedPatch, diagnostics))
        {
            return new SwShExeFsPatchEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedPatch);
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, ExeFsPatchEditDomain, StringComparison.Ordinal))
                .Append(pendingEdit)
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"ExeFS patch '{selectedPatch.Name}' is staged for change-plan review."));

        return new SwShExeFsPatchEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = exeFsPatchWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage an ExeFS patch before validating."
                    : "ExeFS Patch Manager requires exactly one pending patch.",
                expected: "Exactly one pending ExeFS patch"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(project, workflow, edit, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending ExeFS patch is valid for change-plan review."));
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
        var workflow = exeFsPatchWorkflowService.Load(project);
        var edit = session.PendingEdits.Single();
        var selectedPatch = GetPatch(workflow, edit.RecordId ?? string.Empty, diagnostics);
        if (selectedPatch is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, selectedPatch.TargetFile, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new[]
        {
            new PlannedFileWrite(
                selectedPatch.TargetFile,
                CreateChangePlanSources(selectedPatch),
                File.Exists(targetPath),
                $"Apply ExeFS patch '{selectedPatch.Name}': Exp Candy fixed-amount bypass, allowed-consumable routing, Royal Candy virtual inventory, infinite use, and UI routing."),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(
                CultureInfo.InvariantCulture,
                $"ExeFS change plan preview contains {writes.Length:N0} target file(s).")));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
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

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed ExeFS change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed ExeFS change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShExeFsPatchWorkflowService.ExeFsMainPath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS patch source could not be resolved.",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source file"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var selectedGame = paths.SelectedGame
                ?? SwShExeFsRoyalCandyMainPatcher.DetectSupportedGame(NsoFile.Parse(sourceBytes).BuildId)
                ?? throw new InvalidDataException("ExeFS patch apply requires a supported Sword or Shield executable build.");
            var output = SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(
                sourceBytes,
                selectedGame);
            SwShExeFsRoyalCandyMainPatcher.VerifyBasePatchOutput(
                sourceBytes,
                output,
                selectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShExeFsPatchWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied ExeFS patch to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS source file could not be patched: {exception.Message}",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS output file could not be written: {exception.Message}",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS output file could not be written: {exception.Message}",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanStage(
        OpenedProject project,
        SwShExeFsPatchWorkflow workflow,
        SwShExeFsPatchRecord selectedPatch,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS patch apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (!string.Equals(selectedPatch.PatchId, SwShExeFsPatchWorkflowService.MainPatchId, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS patch '{selectedPatch.Name}' cannot be applied yet.",
                field: PatchField,
                expected: SwShExeFsPatchWorkflowService.MainPatchId));
            return false;
        }

        if (selectedPatch.Status == "blocked" || selectedPatch.Status == "readOnly")
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS patch '{selectedPatch.Name}' is not ready to stage.",
                expected: "Available or warning patch status"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void ValidatePendingEdit(
        OpenedProject project,
        SwShExeFsPatchWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ExeFsPatchEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by ExeFS patches.",
                expected: ExeFsPatchEditDomain));
            return;
        }

        if (!string.Equals(edit.Field, PatchField, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending ExeFS patch field is not canonical.",
                field: edit.Field,
                expected: PatchField));
            return;
        }

        var selectedPatch = GetPatch(workflow, edit.RecordId ?? string.Empty, diagnostics);
        if (selectedPatch is null)
        {
            return;
        }

        var canonicalSummary = CreatePendingEdit(selectedPatch).Summary;
        if (!string.Equals(edit.Summary, canonicalSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending ExeFS patch summary is not canonical.",
                expected: canonicalSummary));
            return;
        }

        if (!string.Equals(edit.NewValue, selectedPatch.TargetFile, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending ExeFS patch target is not canonical.",
                field: PatchField,
                expected: selectedPatch.TargetFile));
            return;
        }

        if (edit.Sources is null
            || edit.Sources.Count != 1
            || edit.Sources[0].Layer != selectedPatch.Provenance.SourceLayer
            || !string.Equals(
                edit.Sources[0].RelativePath,
                selectedPatch.Provenance.SourceFile,
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending ExeFS patch source is not canonical.",
                file: selectedPatch.TargetFile,
                expected: $"One {selectedPatch.Provenance.SourceLayer} source at {selectedPatch.Provenance.SourceFile}"));
            return;
        }

        CanStage(project, workflow, selectedPatch, diagnostics);
    }

    private static SwShExeFsPatchRecord? GetPatch(
        SwShExeFsPatchWorkflow workflow,
        string patchId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selectedPatch = workflow.Patches.FirstOrDefault(candidate =>
            string.Equals(candidate.PatchId, patchId, StringComparison.Ordinal));
        if (selectedPatch is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS patch '{patchId}' is not available.",
                field: PatchField,
                expected: SwShExeFsPatchWorkflowService.MainPatchId));
            return null;
        }

        return selectedPatch;
    }

    private static PendingEdit CreatePendingEdit(SwShExeFsPatchRecord patch)
    {
        return new PendingEdit(
            ExeFsPatchEditDomain,
            $"Stage ExeFS patch: {patch.Name}.",
            [new ProjectFileReference(patch.Provenance.SourceLayer, patch.Provenance.SourceFile)],
            RecordId: patch.PatchId,
            Field: PatchField,
            NewValue: patch.TargetFile);
    }

    private static IReadOnlyList<ProjectFileReference> CreateChangePlanSources(SwShExeFsPatchRecord patch)
    {
        if (patch.Provenance.SourceLayer == ProjectFileLayer.Layered)
        {
            return
            [
                new ProjectFileReference(ProjectFileLayer.Base, patch.Provenance.SourceFile),
                new ProjectFileReference(ProjectFileLayer.Layered, patch.Provenance.SourceFile),
            ];
        }

        return [new ProjectFileReference(ProjectFileLayer.Base, patch.Provenance.SourceFile)];
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
                "ExeFS patch apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS patch target must be relative to the output root.",
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS patch target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
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

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
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
            Domain: ExeFsPatchEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
