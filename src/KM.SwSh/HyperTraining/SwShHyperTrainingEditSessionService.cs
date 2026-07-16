// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.HyperTraining;

public sealed class SwShHyperTrainingEditSessionService
{
    public const string HyperTrainingEditDomain = "workflow.hyperTraining";

    private const string RecordId = "hyper-training-minimum-level";
    private const string MinimumLevelField = "minimumLevel";
    private const string StageSummaryPrefix = "Stage Hyper Training minimum level Lv.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShHyperTrainingWorkflowService hyperTrainingWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShHyperTrainingEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.hyperTrainingWorkflowService = hyperTrainingWorkflowService ?? new SwShHyperTrainingWorkflowService();
    }

    internal SwShHyperTrainingEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, hyperTrainingWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    internal SwShHyperTrainingEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, hyperTrainingWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    public SwShHyperTrainingEditResult StageMinimumLevel(
        ProjectPaths paths,
        int minimumLevel,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
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

        var sources = CreatePendingSources(project, minimumLevel, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShHyperTrainingEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(minimumLevel, sources))
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

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = hyperTrainingWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage a Hyper Training minimum level before validating."
                    : "Hyper Training expects exactly one canonical staged minimum-level edit.",
                expected: "Exactly one pending Hyper Training minimum-level edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (!IsCanonicalIdentity(edit))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the canonical Hyper Training minimum-level field.",
                    field: edit.Field,
                    expected: $"{HyperTrainingEditDomain}/{RecordId}/{MinimumLevelField}"));
            }
            else
            {
                ValidateCanonicalEdit(project, edit, diagnostics);
            }
        }

        CanStage(project, workflow, diagnostics);

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
                CreatePlanSources(scriptSource.Entry, minimumLevel),
                File.Exists(scriptTargetPath),
                string.Create(CultureInfo.InvariantCulture, $"Set the Battle Tower Hyper Training script minimum level to Lv.{minimumLevel}.")),
            new(
                SwShHyperTrainingWorkflowService.ExeFsMainPath,
                CreatePlanSources(mainSource.Entry, minimumLevel),
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
                    CreatePlanSources(dialogueSource.Entry, minimumLevel),
                    File.Exists(dialogueTargetPath),
                    string.Create(CultureInfo.InvariantCulture, $"Update English Hyper Training NPC dialogue to mention Lv.{minimumLevel}.")));
            }
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Hyper Training change plan preview contains {writes.Count:N0} target file(s).")));

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        projectWorkspaceService.ClearMemoryCache();
        try
        {
            return ApplyChangePlanCore(paths, session, reviewedPlan);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Hyper Training change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Hyper Training change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        beforeAcquireApplyScope?.Invoke();
        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var applyScope,
                out var acquireDiagnostics))
        {
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                acquireDiagnostics);
        }

        using var verifiedScope = applyScope!;
        var snapshotPlan = CreateChangePlan(verifiedScope.ApplyPaths, session);
        if (!verifiedScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan))
        {
            var staleDiagnostics = preparedPlan.Diagnostics.ToList();
            staleDiagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Hyper Training change plan"));
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                staleDiagnostics);
        }

        var snapshotResult = ApplyPreparedPlan(
            verifiedScope.ApplyPaths,
            session,
            preparedPlan,
            applyId,
            appliedAt);
        return verifiedScope.Commit(snapshotResult, beforeVerifiedPromotion);
    }

    private ApplyResult ApplyPreparedPlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan preparedPlan,
        string applyId,
        DateTimeOffset appliedAt)
    {
        projectWorkspaceService.ClearMemoryCache();
        var diagnostics = preparedPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        if (session.PendingEdits.Count != 1 || !IsCanonicalIdentity(session.PendingEdits[0]))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training prepared apply requires exactly one canonical pending edit.",
                expected: $"{HyperTrainingEditDomain}/{RecordId}/{MinimumLevelField}"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var minimumLevel = ParsePendingMinimumLevel(session.PendingEdits[0].NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        ApplyScript(paths, minimumLevel, writtenFiles, diagnostics);
        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            ApplyMain(paths, minimumLevel, writtenFiles, diagnostics);
        }

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            ApplyDialogue(paths, minimumLevel, preparedPlan, writtenFiles, diagnostics);
        }

        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
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
        var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShHyperTrainingWorkflowService.ExeFsMainPath);
        if (source is null || targetPath is null || basePath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training picker source, vanilla base, or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Readable exefs/main source and writable LayeredFS target"));
            return;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var baseBytes = File.ReadAllBytes(basePath);
            SwShHyperTrainingMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, sourceBytes);
            var output = minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                ? SwShHyperTrainingMainPatcher.RestoreFromBase(
                    sourceBytes,
                    baseBytes,
                    paths.SelectedGame)
                : SwShHyperTrainingMainPatcher.ApplyMinimumLevel(
                    sourceBytes,
                    minimumLevel,
                    paths.SelectedGame);
            if (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                && SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(output, baseBytes))
            {
                File.Delete(targetPath);
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => VerifyMainOutput(roundTrip, minimumLevel, paths.SelectedGame));
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training picker runtime changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training picker runtime could not be prepared safely: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ExeFsMainPath,
                expected: "Verified vanilla base, supported effective main, and writable output"));
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
        var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShHyperTrainingWorkflowService.ScriptPath);
        if (source is null || targetPath is null || basePath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Hyper Training script source, vanilla base, or output target could not be resolved.",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Readable source and writable LayeredFS target"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            if (SwShHyperTrainingAmxPatcher.Analyze(baseBytes).Kind != SwShHyperTrainingScriptKind.NotInstalled)
            {
                throw new InvalidDataException("Hyper Training AMX base is not the verified vanilla Lv.100 script.");
            }

            var output = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(
                File.ReadAllBytes(source.AbsolutePath),
                minimumLevel);
            if (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                && output.AsSpan().SequenceEqual(baseBytes))
            {
                File.Delete(targetPath);
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => VerifyScriptOutput(roundTrip, minimumLevel));
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.ScriptPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training script changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training script could not be prepared safely: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.ScriptPath,
                expected: "Verified vanilla base, known Hyper Training AMX shape, and writable output"));
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
            var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(
                paths,
                SwShHyperTrainingWorkflowService.EnglishDialoguePath);
            byte[]? baseBytes = null;
            if (basePath is not null)
            {
                baseBytes = File.ReadAllBytes(basePath);
                if (SwShHyperTrainingDialoguePatcher.Analyze(baseBytes).Kind
                    != SwShHyperTrainingDialogueKind.NotInstalled)
                {
                    throw new InvalidDataException(
                        "English Hyper Training dialogue base is not the verified vanilla Lv.100 table.");
                }
            }

            var output = SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(
                File.ReadAllBytes(source.AbsolutePath),
                minimumLevel);
            if (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                && baseBytes is not null
                && output.AsSpan().SequenceEqual(baseBytes))
            {
                File.Delete(targetPath);
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => VerifyDialogueOutput(roundTrip, minimumLevel));
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShHyperTrainingWorkflowService.EnglishDialoguePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Hyper Training dialogue changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hyper Training dialogue could not be prepared safely: {exception.Message}",
                file: SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                expected: "Verified English Hyper Training dialogue and writable output"));
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

    private static PendingEdit CreatePendingEdit(
        int minimumLevel,
        IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            HyperTrainingEditDomain,
            CreateStageSummary(minimumLevel),
            sources,
            RecordId,
            MinimumLevelField,
            minimumLevel.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsCanonicalIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, HyperTrainingEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, MinimumLevelField, StringComparison.Ordinal);
    }

    private static void ValidateCanonicalEdit(
        OpenedProject project,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var minimumLevel = ParsePendingMinimumLevel(edit.NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        var canonicalPayload = minimumLevel.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(edit.NewValue, canonicalPayload, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Hyper Training minimum level is not in canonical integer format.",
                field: MinimumLevelField,
                expected: canonicalPayload));
        }

        var expectedSummary = CreateStageSummary(minimumLevel);
        if (!string.Equals(edit.Summary, expectedSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Hyper Training edit does not have the canonical staged summary.",
                field: MinimumLevelField,
                expected: expectedSummary));
        }

        var sourceDiagnostics = new List<ValidationDiagnostic>();
        var expectedSources = CreatePendingSources(project, minimumLevel, sourceDiagnostics);
        foreach (var diagnostic in sourceDiagnostics)
        {
            diagnostics.Add(diagnostic);
        }
        if (!edit.Sources.SequenceEqual(expectedSources))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Hyper Training sources do not match the canonical base, effective, and payload references.",
                field: MinimumLevelField,
                expected: "Canonical ordered unique Hyper Training source references"));
        }
    }

    private static string CreateStageSummary(int minimumLevel)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{StageSummaryPrefix}{minimumLevel}.");
    }

    private static IReadOnlyList<ProjectFileReference> CreatePendingSources(
        OpenedProject project,
        int minimumLevel,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = new List<ProjectFileReference>();
        foreach (var relativePath in new[]
        {
            SwShHyperTrainingWorkflowService.ScriptPath,
            SwShHyperTrainingWorkflowService.ExeFsMainPath,
        })
        {
            var source = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, relativePath);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Hyper Training source could not be resolved.",
                    file: relativePath,
                    expected: "Readable current Hyper Training source"));
                continue;
            }

            AddBaseAndEffectiveSourceReferences(source.Entry, sources);
        }

        var dialogueSource = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(
            project,
            SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        if (dialogueSource is not null)
        {
            AddBaseAndEffectiveSourceReferences(dialogueSource.Entry, sources);
        }

        sources.Add(CreatePendingPayloadSource(minimumLevel));
        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ProjectFileReference> CreatePlanSources(
        ProjectFileGraphEntry entry,
        int minimumLevel)
    {
        var sources = new List<ProjectFileReference>();
        AddBaseAndEffectiveSourceReferences(entry, sources);
        sources.Add(CreatePendingPayloadSource(minimumLevel));
        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddBaseAndEffectiveSourceReferences(
        ProjectFileGraphEntry entry,
        ICollection<ProjectFileReference> sources)
    {
        if (entry.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, entry.RelativePath));
        }

        if (entry.LayeredFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, entry.RelativePath));
        }
    }

    private static ProjectFileReference CreatePendingPayloadSource(int minimumLevel)
    {
        var payload = minimumLevel.ToString(CultureInfo.InvariantCulture);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/hyper-training/minimum-level/{hash}");
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

    private static void VerifyMainOutput(byte[] output, int minimumLevel, ProjectGame? selectedGame)
    {
        var analysis = SwShHyperTrainingMainPatcher.Analyze(output, selectedGame);
        var expectedKind = minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
            ? SwShHyperTrainingMainKind.NotInstalled
            : SwShHyperTrainingMainKind.CustomMinimumLevel;
        if (analysis.Kind != expectedKind || analysis.MinimumLevel != minimumLevel)
        {
            throw new InvalidDataException(
                "Hyper Training main output did not round-trip with the reviewed cutoff.");
        }
    }

    private static void VerifyScriptOutput(byte[] output, int minimumLevel)
    {
        if (SwShHyperTrainingAmxPatcher.ReadMinimumLevel(output) != minimumLevel)
        {
            throw new InvalidDataException(
                "Hyper Training AMX output did not round-trip with the reviewed cutoff.");
        }
    }

    private static void VerifyDialogueOutput(byte[] output, int minimumLevel)
    {
        var analysis = SwShHyperTrainingDialoguePatcher.Analyze(output);
        if (analysis.Kind == SwShHyperTrainingDialogueKind.Conflict
            || analysis.MinimumLevel != minimumLevel)
        {
            throw new InvalidDataException(
                "Hyper Training dialogue output did not round-trip with the reviewed cutoff.");
        }
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Hyper Training output directory could not be resolved.");
        Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, output);
            var roundTrip = File.ReadAllBytes(temporaryPath);
            if (!roundTrip.AsSpan().SequenceEqual(output))
            {
                throw new IOException("Hyper Training temporary output did not round-trip byte-for-byte.");
            }

            verifyRoundTrip(roundTrip);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
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
