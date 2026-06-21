// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Workflows;

namespace KM.SV.TypeChart;

public sealed class SvTypeChartEditSessionService
{
    public const string TypeChartEditDomain = "workflow.typeChart";

    private const string ChartRecordId = "sv-type-chart";
    private const string UninstallRecordId = "sv-type-chart-v1-uninstall";
    private const string EffectivenessField = "effectiveness";
    private const string UninstallField = "uninstall";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvTypeChartWorkflowService typeChartWorkflowService;

    public SvTypeChartEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvTypeChartWorkflowService? typeChartWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.typeChartWorkflowService = typeChartWorkflowService ?? new SvTypeChartWorkflowService();
    }

    public SvTypeChartEditResult StageChart(
        ProjectPaths paths,
        IReadOnlyList<int> values,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = typeChartWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, TypeChartEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart needs its own edit session before staging.",
                expected: "A Type Chart-only edit session"));
            return new SvTypeChartEditResult(workflow, currentSession, diagnostics);
        }

        if (!ValidateChartValues(values, diagnostics) || !CanStage(project, workflow, diagnostics))
        {
            return new SvTypeChartEditResult(workflow, currentSession, diagnostics);
        }

        var payload = EncodeValues(values);
        var source = SvTypeChartWorkflowService.ResolveWorkflowFile(project, SvTypeChartWorkflowService.ExeFsMainPath);
        var sourceReferences = source is null
            ? [new ProjectFileReference(ProjectFileLayer.Base, SvTypeChartWorkflowService.ExeFsMainPath)]
            : new[] { CreateSourceReference(source.Entry) };
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, TypeChartEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(payload, sourceReferences))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Type Chart effectiveness values are staged for change-plan review."));

        return new SvTypeChartEditResult(workflow, updatedSession, diagnostics);
    }

    public SvTypeChartEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = typeChartWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, TypeChartEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart needs its own edit session before staging uninstall.",
                expected: "A Type Chart-only edit session"));
            return new SvTypeChartEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SvTypeChartEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, TypeChartEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingUninstallEdit())
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Type Chart uninstall is staged for change-plan review."));

        return new SvTypeChartEditResult(workflow, updatedSession, diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = typeChartWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Type Chart values before validating.",
                expected: "Pending Type Chart effectiveness values"));
            return new SvEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart expects exactly one staged chart edit.",
                expected: "One pending Type Chart edit"));
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, TypeChartEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Type Chart.",
                    expected: TypeChartEditDomain));
                continue;
            }

            if (IsUninstallEdit(edit))
            {
                CanStageUninstall(project, workflow, paths, diagnostics);
                continue;
            }

            if (!string.Equals(edit.RecordId, ChartRecordId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Type Chart edit '{edit.RecordId}' is not supported.",
                    expected: "Type Chart effectiveness table or uninstall"));
                continue;
            }

            _ = DecodeValues(edit.NewValue, diagnostics);
            CanStage(project, workflow, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Type Chart change is valid for change-plan review."));
        }

        return new SvEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (outputMode == SvOutputMode.TrinityModManager)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart edits exefs/main, which is outside Trinity Mod Manager RomFS output. Use standalone LayeredFS output for this editor.",
                expected: "Standalone LayeredFS output mode"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart output target could not be resolved.",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Writable LayeredFS target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var isUninstall = IsUninstallSession(session);
        var project = projectWorkspaceService.Open(paths);
        var source = SvTypeChartWorkflowService.ResolveWorkflowFile(project, SvTypeChartWorkflowService.ExeFsMainPath);
        if (!isUninstall && source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart source could not be resolved.",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            SvTypeChartWorkflowService.ExeFsMainPath,
            isUninstall
                ? [
                    new ProjectFileReference(ProjectFileLayer.Generated, SvTypeChartWorkflowService.ExeFsMainPath),
                    new ProjectFileReference(ProjectFileLayer.Base, SvTypeChartWorkflowService.ExeFsMainPath),
                ]
                : [CreateSourceReference(source!.Entry)],
            File.Exists(targetPath),
            isUninstall
                ? "Remove Scarlet/Violet Type Chart output from exefs/main while preserving other generated ExeFS edits."
                : "Update the Scarlet/Violet type-effectiveness table in exefs/main.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Type Chart change plan preview contains 1 target file."));

        return new ChangePlan(session.Id, [write], diagnostics);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Type Chart change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Type Chart change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var pendingEdit = session.PendingEdits.Single();
        if (IsUninstallEdit(pendingEdit))
        {
            ApplyUninstall(paths, writtenFiles, diagnostics);
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var values = DecodeValues(pendingEdit.NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        ApplyMain(paths, values, writtenFiles, diagnostics);
        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private void ApplyMain(
        ProjectPaths paths,
        IReadOnlyList<int> values,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = SvTypeChartWorkflowService.ResolveWorkflowFile(project, SvTypeChartWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart source or output target could not be resolved.",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return;
        }

        try
        {
            var gameOrderValues = SvTypeChartWorkflowService.ToGameOrder(values);
            var output = SvTypeChartMainPatcher.ApplyChart(
                File.ReadAllBytes(source.AbsolutePath),
                gameOrderValues,
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SvTypeChartWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Type Chart changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart could not be patched: {exception.Message}",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Supported Scarlet/Violet exefs/main with one legal 18x18 type chart table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart output could not be written: {exception.Message}",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart output could not be written: {exception.Message}",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private static void ApplyUninstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = ResolveBaseSourcePath(paths, SvTypeChartWorkflowService.ExeFsMainPath);
        if (targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart uninstall could not resolve base exefs/main for restoration.",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Readable base ExeFS main"));
            return;
        }

        if (!File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart uninstall target no longer exists. Review the change plan again before applying.",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Existing reviewed LayeredFS exefs/main"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var restored = SvTypeChartMainPatcher.RestoreFromBase(
                File.ReadAllBytes(targetPath),
                baseBytes,
                paths.SelectedGame);
            if (restored.SequenceEqual(baseBytes))
            {
                File.Delete(targetPath);
            }
            else
            {
                File.WriteAllBytes(targetPath, restored);
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SvTypeChartWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Type Chart changes from the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart uninstall could not restore exefs/main: {exception.Message}",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Supported Scarlet/Violet exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart uninstall could not update output: {exception.Message}",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart uninstall could not update output: {exception.Message}",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SvTypeChartWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SvWorkflowFileSource.IsScarletViolet(project.Paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart requires a Scarlet or Violet project.",
                expected: "Scarlet/Violet project"));
            return false;
        }

        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SvWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart apply requires valid base paths and a valid output root.",
                expected: "Editable Scarlet/Violet project paths"));
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
                    "Type Chart cannot stage while exefs/main has an unsupported or ambiguous type chart shape.",
                    expected: "Known Scarlet/Violet exefs/main type chart table"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SvTypeChartWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SvWorkflowFileSource.IsScarletViolet(project.Paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart uninstall requires a Scarlet or Violet project.",
                expected: "Scarlet/Violet project"));
            return false;
        }

        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SvWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart uninstall requires valid base paths and a valid output root.",
                expected: "Editable Scarlet/Violet project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (!string.Equals(workflow.InstallStatus, "modified", StringComparison.Ordinal))
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Type Chart is not modified in the current project output.",
                    expected: "Modified Type Chart values in the configured output root"));
            }

            return false;
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart uninstall can only restore a generated exefs/main file.",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Modified Type Chart values in the configured output root"));
            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool ValidateChartValues(
        IReadOnlyList<int> values,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            SvTypeChartMainPatcher.ValidateValues(values);
            return true;
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                field: EffectivenessField,
                expected: "324 values, each one of 0, 2, 4, or 8"));
            return false;
        }
    }

    private static string EncodeValues(IReadOnlyList<int> values)
    {
        SvTypeChartMainPatcher.ValidateValues(values);
        return Convert.ToHexString(values.Select(value => checked((byte)value)).ToArray());
    }

    private static int[] DecodeValues(string? value, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart pending edit has no effectiveness payload.",
                field: EffectivenessField,
                expected: "Hex-encoded 18x18 effectiveness table"));
            return SvTypeChartMainPatcher.VanillaChartValues.ToArray();
        }

        try
        {
            var bytes = Convert.FromHexString(value);
            var values = bytes.Select(effectiveness => (int)effectiveness).ToArray();
            ValidateChartValues(values, diagnostics);
            return values;
        }
        catch (FormatException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart pending edit payload is not valid hex.",
                field: EffectivenessField,
                expected: "Hex-encoded 18x18 effectiveness table"));
            return SvTypeChartMainPatcher.VanillaChartValues.ToArray();
        }
    }

    private static PendingEdit CreatePendingEdit(
        string payload,
        IReadOnlyList<ProjectFileReference> sourceReferences)
    {
        return new PendingEdit(
            TypeChartEditDomain,
            "Stage Type Chart effectiveness table.",
            sourceReferences,
            ChartRecordId,
            EffectivenessField,
            payload);
    }

    private static PendingEdit CreatePendingUninstallEdit()
    {
        return new PendingEdit(
            TypeChartEditDomain,
            "Stage Type Chart uninstall.",
            [
                new ProjectFileReference(ProjectFileLayer.Generated, SvTypeChartWorkflowService.ExeFsMainPath),
                new ProjectFileReference(ProjectFileLayer.Base, SvTypeChartWorkflowService.ExeFsMainPath),
            ],
            UninstallRecordId,
            UninstallField,
            "true");
    }

    private static bool IsUninstallSession(EditSession session)
    {
        return session.PendingEdits.Count == 1 && IsUninstallEdit(session.PendingEdits[0]);
    }

    private static bool IsUninstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal);
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
                "Type Chart apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SvTypeChartWorkflowService.ResolveOutputPath(paths, SvTypeChartWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart target must stay inside the configured output root.",
                file: SvTypeChartWorkflowService.ExeFsMainPath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
            Domain: TypeChartEditDomain,
            Field: field,
            Expected: expected);
    }
}
