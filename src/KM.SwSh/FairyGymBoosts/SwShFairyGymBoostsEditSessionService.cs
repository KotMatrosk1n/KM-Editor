// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.FairyGymBoosts;

public sealed class SwShFairyGymBoostsEditSessionService
{
    public const string FairyGymBoostsEditDomain = SwShFairyGymBoostsWorkflowService.FairyGymBoostsEditDomain;

    private const string RecordId = "fairy-gym-boosts";
    private const string BoostSelectionsField = "boostSelections";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShFairyGymBoostsWorkflowService fairyGymBoostsWorkflowService;

    public SwShFairyGymBoostsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShFairyGymBoostsWorkflowService? fairyGymBoostsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fairyGymBoostsWorkflowService = fairyGymBoostsWorkflowService ?? new SwShFairyGymBoostsWorkflowService();
    }

    public SwShFairyGymBoostsEditResult StageBoosts(
        ProjectPaths paths,
        IReadOnlyList<SwShFairyGymBoostSelection> selections,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selections);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, FairyGymBoostsEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts needs its own edit session before staging.",
                expected: "A Fairy Gym Boosts-only edit session"));
            return new SwShFairyGymBoostsEditResult(workflow, currentSession, diagnostics);
        }

        var normalizedSelections = NormalizeSelections(selections, diagnostics);
        if (!CanStage(project, workflow, diagnostics) || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShFairyGymBoostsEditResult(workflow, currentSession, diagnostics);
        }

        var payload = EncodeSelections(normalizedSelections);
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, FairyGymBoostsEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(payload, CreateSourceReferences(project)))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fairy Gym boost outcomes are staged for change-plan review."));

        return new SwShFairyGymBoostsEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Fairy Gym boost outcomes before validating.",
                expected: "Pending Fairy Gym Boosts edit"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts expects exactly one staged boost edit.",
                expected: "One pending Fairy Gym Boosts edit"));
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, FairyGymBoostsEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Fairy Gym Boosts.",
                    expected: FairyGymBoostsEditDomain));
                continue;
            }

            if (!string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Fairy Gym Boosts edit '{edit.RecordId}' is not supported.",
                    expected: "Fairy Gym boost outcomes"));
                continue;
            }

            _ = DecodeSelections(edit.NewValue, diagnostics);
            CanStage(project, workflow, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Fairy Gym Boosts change is valid for change-plan review."));
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
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var selections = DecodeSelections(session.PendingEdits.Single().NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var fileGroups = CreateChangedFileGroups(workflow, selections).ToArray();

        if (fileGroups.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Fairy Gym Boosts has no changed answer outcomes to write."));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        foreach (var fileGroup in fileGroups)
        {
            var source = SwShFairyGymBoostsWorkflowService.ResolveWorkflowFile(project, fileGroup.RelativePath);
            var targetPath = ResolveOutputPath(paths, fileGroup.RelativePath, diagnostics);
            if (source is null || targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts source or output target could not be resolved.",
                    file: fileGroup.RelativePath,
                    expected: "Readable BSEQ source and writable LayeredFS target"));
                continue;
            }

            writes.Add(new PlannedFileWrite(
                fileGroup.RelativePath,
                [CreateSourceReference(source.Entry)],
                File.Exists(targetPath),
                "Update Fairy Gym quiz boost outcomes in the battle sequence file."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Fairy Gym Boosts change plan preview contains {writes.Count:N0} target file(s)."));

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
                "Reviewed Fairy Gym Boosts change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Fairy Gym Boosts change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var selections = DecodeSelections(session.PendingEdits.Single().NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var fileGroups = CreateChangedFileGroups(workflow, selections).ToArray();
        foreach (var fileGroup in fileGroups)
        {
            ApplyFileGroup(project, paths, fileGroup, writtenFiles, diagnostics);
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void ApplyFileGroup(
        OpenedProject project,
        ProjectPaths paths,
        FairyGymBoostFileGroup fileGroup,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShFairyGymBoostsWorkflowService.ResolveWorkflowFile(project, fileGroup.RelativePath);
        var targetPath = ResolveOutputPath(paths, fileGroup.RelativePath, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts source or output target could not be resolved.",
                file: fileGroup.RelativePath,
                expected: "Readable BSEQ source and writable LayeredFS target"));
            return;
        }

        try
        {
            var patches = fileGroup.Selections
                .Select(selection => new SwShFairyGymBoostAnswerPatch(
                    selection.Definition.AnswerChoice,
                    selection.Selection.EffectId,
                    SwShFairyGymBoostsWorkflowService.ToResultValue(selection.Selection.ResultKind)))
                .ToArray();
            var output = SwShFairyGymBoostsBseqPatcher.ApplySelections(
                File.ReadAllBytes(source.AbsolutePath),
                patches);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, fileGroup.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Applied Fairy Gym Boosts changes to {fileGroup.RelativePath}.",
                file: fileGroup.RelativePath));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fairy Gym Boosts source file could not be patched: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Supported Fairy Gym quiz BSEQ command payload"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fairy Gym Boosts output file could not be written: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fairy Gym Boosts output file could not be written: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Writable output root"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShFairyGymBoostsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        foreach (var source in workflow.Sources.Where(source => source.Status != "available"))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{source.Label} is required before Fairy Gym Boosts can be staged.",
                file: source.RelativePath,
                expected: "Available Fairy Gym quiz BSEQ file"));
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<SwShFairyGymBoostSelection> NormalizeSelections(
        IReadOnlyList<SwShFairyGymBoostSelection> selections,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var byBoostId = new Dictionary<string, SwShFairyGymBoostSelection>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            if (string.IsNullOrWhiteSpace(selection.BoostId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts selection is missing a boost id.",
                    field: BoostSelectionsField,
                    expected: "Known Fairy Gym boost id"));
                continue;
            }

            var definition = SwShFairyGymBoostsWorkflowService.FindBoost(selection.BoostId);
            if (definition is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{selection.BoostId}' is not recognized.",
                    field: BoostSelectionsField,
                    expected: "Known Fairy Gym boost id"));
                continue;
            }

            if (byBoostId.ContainsKey(selection.BoostId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{selection.BoostId}' is duplicated.",
                    field: BoostSelectionsField,
                    expected: "One selection per answer choice"));
                continue;
            }

            if (!SwShFairyGymBoostsWorkflowService.IsSupportedSelection(selection.EffectId, selection.ResultKind))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{selection.BoostId}' is not a supported outcome.",
                    field: BoostSelectionsField,
                    expected: "No effect, or effect 1-6 with boost/drop"));
                continue;
            }

            byBoostId[selection.BoostId] = selection;
        }

        foreach (var boost in SwShFairyGymBoostsWorkflowService.Boosts)
        {
            if (!byBoostId.ContainsKey(boost.BoostId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{boost.BoostId}' is missing.",
                    field: BoostSelectionsField,
                    expected: "One selection per answer choice"));
            }
        }

        return SwShFairyGymBoostsWorkflowService.Boosts
            .Select(boost => byBoostId.TryGetValue(boost.BoostId, out var selection)
                ? selection
                : SwShFairyGymBoostsWorkflowService.CreateDefaultSelection(boost))
            .ToArray();
    }

    private static string EncodeSelections(IReadOnlyList<SwShFairyGymBoostSelection> selections)
    {
        return string.Join(
            ';',
            selections.Select(selection => $"{selection.BoostId}:{selection.EffectId}:{selection.ResultKind}"));
    }

    private static IReadOnlyList<SwShFairyGymBoostSelection> DecodeSelections(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts pending edit has no outcome payload.",
                field: BoostSelectionsField,
                expected: "Encoded Fairy Gym boost selections"));
            return SwShFairyGymBoostsWorkflowService.Boosts
                .Select(SwShFairyGymBoostsWorkflowService.CreateDefaultSelection)
                .ToArray();
        }

        var selections = new List<SwShFairyGymBoostSelection>();
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var effectId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts pending edit payload is malformed.",
                    field: BoostSelectionsField,
                    expected: "boostId:effectId:resultKind entries"));
                continue;
            }

            selections.Add(new SwShFairyGymBoostSelection(parts[0], effectId, parts[2]));
        }

        return NormalizeSelections(selections, diagnostics);
    }

    private static IReadOnlyList<FairyGymBoostFileGroup> CreateChangedFileGroups(
        SwShFairyGymBoostsWorkflow workflow,
        IReadOnlyList<SwShFairyGymBoostSelection> selections)
    {
        var currentByBoostId = workflow.Trainers
            .SelectMany(trainer => trainer.Boosts)
            .ToDictionary(boost => boost.BoostId, StringComparer.Ordinal);
        var definitionsByBoostId = SwShFairyGymBoostsWorkflowService.Boosts
            .ToDictionary(boost => boost.BoostId, StringComparer.Ordinal);

        return selections
            .Where(selection =>
                currentByBoostId.TryGetValue(selection.BoostId, out var current)
                && (current.EffectId != selection.EffectId
                    || !string.Equals(current.ResultKind, selection.ResultKind, StringComparison.Ordinal)))
            .Select(selection => new FairyGymBoostSelectionPatch(
                definitionsByBoostId[selection.BoostId],
                selection))
            .GroupBy(selection => selection.Definition.SequenceFile, StringComparer.Ordinal)
            .Select(group => new FairyGymBoostFileGroup(group.Key, group.ToArray()))
            .ToArray();
    }

    private static PendingEdit CreatePendingEdit(
        string payload,
        IReadOnlyList<ProjectFileReference> sourceReferences)
    {
        return new PendingEdit(
            FairyGymBoostsEditDomain,
            "Stage Fairy Gym boost outcomes.",
            sourceReferences,
            RecordId,
            BoostSelectionsField,
            payload);
    }

    private static IReadOnlyList<ProjectFileReference> CreateSourceReferences(OpenedProject project)
    {
        return SwShFairyGymBoostsWorkflowService.Sources
            .Select(source => SwShFairyGymBoostsWorkflowService.ResolveWorkflowFile(project, source.RelativePath))
            .Where(source => source is not null)
            .Select(source => CreateSourceReference(source!.Entry))
            .ToArray();
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
                "Fairy Gym Boosts apply requires a configured output root.",
                file: targetRelativePath,
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShFairyGymBoostsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts target must stay inside the configured output root.",
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
            Domain: FairyGymBoostsEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record FairyGymBoostFileGroup(
        string RelativePath,
        IReadOnlyList<FairyGymBoostSelectionPatch> Selections);

    private sealed record FairyGymBoostSelectionPatch(
        SwShFairyGymBoostDefinition Definition,
        SwShFairyGymBoostSelection Selection);
}
