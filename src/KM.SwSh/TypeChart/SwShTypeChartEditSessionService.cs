// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.TypeChart;

public sealed class SwShTypeChartEditSessionService
{
    public const string TypeChartEditDomain = "workflow.typeChart";

    private const string RecordId = "type-chart";
    private const string EffectivenessField = "effectiveness";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShTypeChartWorkflowService typeChartWorkflowService;

    public SwShTypeChartEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTypeChartWorkflowService? typeChartWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.typeChartWorkflowService = typeChartWorkflowService ?? new SwShTypeChartWorkflowService();
    }

    public SwShTypeChartEditResult StageChart(
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
            return new SwShTypeChartEditResult(workflow, currentSession, diagnostics);
        }

        if (!ValidateChartValues(values, diagnostics) || !CanStage(project, workflow, diagnostics))
        {
            return new SwShTypeChartEditResult(workflow, currentSession, diagnostics);
        }

        var payload = EncodeValues(values);
        var source = SwShTypeChartWorkflowService.ResolveWorkflowFile(project, SwShTypeChartWorkflowService.ExeFsMainPath);
        var sourceReferences = source is null
            ? [new ProjectFileReference(ProjectFileLayer.Base, SwShTypeChartWorkflowService.ExeFsMainPath)]
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

        return new SwShTypeChartEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
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
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
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

            if (!string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Type Chart edit '{edit.RecordId}' is not supported.",
                    expected: "Type Chart effectiveness table"));
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
        var source = SwShTypeChartWorkflowService.ResolveWorkflowFile(project, SwShTypeChartWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart source or output target could not be resolved.",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            SwShTypeChartWorkflowService.ExeFsMainPath,
            [CreateSourceReference(source.Entry)],
            File.Exists(targetPath),
            "Update the Sword/Shield type-effectiveness table in exefs/main.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Type Chart change plan preview contains 1 target file."));

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
                "Reviewed Type Chart change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Type Chart change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var values = DecodeValues(session.PendingEdits.Single().NewValue, diagnostics);
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
        var source = SwShTypeChartWorkflowService.ResolveWorkflowFile(project, SwShTypeChartWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart source or output target could not be resolved.",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return;
        }

        try
        {
            var output = SwShTypeChartMainPatcher.ApplyChart(
                File.ReadAllBytes(source.AbsolutePath),
                values,
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShTypeChartWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Type Chart changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart could not be patched: {exception.Message}",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield 1.3.2 exefs/main with one legal 18x18 type chart table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart output could not be written: {exception.Message}",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart output could not be written: {exception.Message}",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShTypeChartWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart apply requires valid base paths and a valid output root.",
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
                    "Type Chart cannot stage while exefs/main has an unsupported or ambiguous type chart shape.",
                    expected: "Known Sword/Shield 1.3.2 exefs/main type chart table"));
            }

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
            SwShTypeChartMainPatcher.ValidateValues(values);
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
        SwShTypeChartMainPatcher.ValidateValues(values);
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
            return SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
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
            return SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
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
            RecordId,
            EffectivenessField,
            payload);
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

        var targetPath = SwShTypeChartWorkflowService.ResolveOutputPath(paths, SwShTypeChartWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart target must stay inside the configured output root.",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
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
            Domain: TypeChartEditDomain,
            Field: field,
            Expected: expected);
    }
}
