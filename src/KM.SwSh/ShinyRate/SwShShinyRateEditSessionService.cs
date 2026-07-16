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

namespace KM.SwSh.ShinyRate;

public sealed class SwShShinyRateEditSessionService
{
    public const string ShinyRateEditDomain = "workflow.shinyRate";

    private const string RecordId = "shiny-rate";
    private const string RateField = "rate";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShShinyRateWorkflowService shinyRateWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShShinyRateEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShShinyRateWorkflowService? shinyRateWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.shinyRateWorkflowService = shinyRateWorkflowService ?? new SwShShinyRateWorkflowService();
    }

    internal SwShShinyRateEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShShinyRateWorkflowService? shinyRateWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, shinyRateWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    internal SwShShinyRateEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShShinyRateWorkflowService? shinyRateWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, shinyRateWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    public SwShShinyRateEditResult StageRate(
        ProjectPaths paths,
        string? mode,
        int? rollCount,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
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
        var sources = CreateCanonicalSources(project, payload, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShShinyRateEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingEdit(payload, sources, selection)],
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

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = shinyRateWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage Shiny Rate before validating."
                    : "Shiny Rate expects exactly one canonical staged rate edit.",
                expected: "Exactly one pending Shiny Rate edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (!IsCanonicalIdentity(edit))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the canonical Shiny Rate field.",
                    field: edit.Field,
                    expected: $"{ShinyRateEditDomain}/{RecordId}/{RateField}"));
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
        var edit = session.PendingEdits.Single();
        var selection = DecodeSelection(edit.NewValue, diagnostics);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var sources = CreateCanonicalSources(project, edit.NewValue ?? string.Empty, diagnostics);
        if (selection is null
            || targetPath is null
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            SwShShinyRateWorkflowService.ExeFsMainPath,
            sources,
            File.Exists(targetPath),
            CreatePlanReason(selection));

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Shiny Rate change plan preview contains 1 target file."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, [write], diagnostics));
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
                "Reviewed Shiny Rate change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Shiny Rate change plan"));
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
                "Shiny Rate sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Shiny Rate change plan"));
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
                "Shiny Rate prepared apply requires exactly one canonical pending edit.",
                expected: $"{ShinyRateEditDomain}/{RecordId}/{RateField}"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var selection = DecodeSelection(session.PendingEdits[0].NewValue, diagnostics);
        if (selection is null || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        ApplyMain(paths, selection, writtenFiles, diagnostics);
        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private void ApplyMain(
        ProjectPaths paths,
        ShinyRateSelection selection,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = SwShShinyRateWorkflowService.ResolveWorkflowFile(
            project,
            SwShShinyRateWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = SwShShinyRateWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShShinyRateWorkflowService.ExeFsMainPath);
        if (source is null || targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate source, vanilla base, or output target could not be resolved.",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Readable reviewed source and vanilla base with writable LayeredFS target"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var baseAnalysis = SwShShinyRateMainPatcher.Analyze(baseBytes, paths.SelectedGame);
            var sourceAnalysis = SwShShinyRateMainPatcher.Analyze(sourceBytes, paths.SelectedGame);
            EnsureVerifiedVanillaBase(baseAnalysis, paths.SelectedGame);
            EnsureEditableEffectiveSource(sourceAnalysis);
            SwShShinyRateMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, sourceBytes);

            var output = selection.Mode == SwShShinyRateMode.Default
                ? SwShShinyRateMainPatcher.RestoreFromBase(sourceBytes, baseBytes, paths.SelectedGame)
                : SwShShinyRateMainPatcher.ApplyRate(
                    sourceBytes,
                    selection.Mode,
                    selection.RollCount,
                    paths.SelectedGame);
            VerifyOutput(baseBytes, output, selection, paths.SelectedGame);

            if (selection.Mode == SwShShinyRateMode.Default
                && SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(output, baseBytes))
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                if (File.Exists(targetPath))
                {
                    throw new IOException("Shiny Rate restore target still exists after verified deletion.");
                }
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => VerifyOutput(baseBytes, roundTrip, selection, paths.SelectedGame));
            }

            writtenFiles.Add(new ProjectFileReference(
                ProjectFileLayer.Generated,
                SwShShinyRateWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                selection.Mode == SwShShinyRateMode.Default
                    ? "Restored Shiny Rate default logic in the configured LayeredFS output root."
                    : "Applied Shiny Rate changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shiny Rate verified output could not be prepared: {exception.Message}",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Reviewed selected-game source, exact rate payload, verified vanilla base, and writable output"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShShinyRateWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            if (!diagnostics.Contains(diagnostic))
            {
                diagnostics.Add(diagnostic);
            }
        }

        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shiny Rate apply requires valid base paths, Pokemon Sword or Shield, and a valid output root.",
                    expected: "Editable Pokemon Sword or Pokemon Shield project paths"));
            }

            return false;
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
        string? mode,
        int? rollCount,
        ICollection<ValidationDiagnostic> diagnostics,
        out ShinyRateSelection selection)
    {
        selection = new ShinyRateSelection(SwShShinyRateMode.Default, RollCount: null);
        if (!TryParseMode(mode, out var parsedMode))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.IsNullOrWhiteSpace(mode)
                    ? "Shiny Rate mode is required."
                    : $"Shiny Rate mode '{mode}' is not supported.",
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

    private static ShinyRateSelection? DecodeSelection(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.Equals(value, "default", StringComparison.Ordinal))
        {
            return new ShinyRateSelection(SwShShinyRateMode.Default, RollCount: null);
        }

        if (string.Equals(value, "always", StringComparison.Ordinal))
        {
            return new ShinyRateSelection(SwShShinyRateMode.AlwaysShiny, RollCount: null);
        }

        const string fixedPrefix = "fixed:";
        if (value is not null
            && value.StartsWith(fixedPrefix, StringComparison.Ordinal)
            && int.TryParse(value[fixedPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var rollCount))
        {
            if (TryCreateSelection("fixed", rollCount, diagnostics, out var selection))
            {
                return selection;
            }

            return null;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            string.IsNullOrWhiteSpace(value)
                ? "Shiny Rate pending edit has no rate payload."
                : "Shiny Rate pending edit payload is not supported.",
            field: RateField,
            expected: "default, always, or fixed:<roll count>"));
        return null;
    }

    private PendingEdit CreatePendingEdit(
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

    private static bool IsCanonicalIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ShinyRateEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, RateField, StringComparison.Ordinal);
    }

    private void ValidateCanonicalEdit(
        OpenedProject project,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selection = DecodeSelection(edit.NewValue, diagnostics);
        if (selection is null)
        {
            return;
        }

        var canonicalPayload = EncodeSelection(selection);
        if (!string.Equals(edit.NewValue, canonicalPayload, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Shiny Rate selection is not in canonical payload format.",
                field: RateField,
                expected: canonicalPayload));
        }

        var expectedSummary = CreatePendingEditSummary(selection);
        if (!string.Equals(edit.Summary, expectedSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Shiny Rate edit does not have the canonical staged summary.",
                field: RateField,
                expected: expectedSummary));
        }

        var sourceDiagnostics = new List<ValidationDiagnostic>();
        var expectedSources = CreateCanonicalSources(project, canonicalPayload, sourceDiagnostics);
        foreach (var diagnostic in sourceDiagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        if (!edit.Sources.SequenceEqual(expectedSources))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Shiny Rate sources do not match the canonical base, effective, and payload references.",
                field: RateField,
                expected: "Canonical ordered unique Shiny Rate source references"));
        }
    }

    private IReadOnlyList<ProjectFileReference> CreateCanonicalSources(
        OpenedProject project,
        string payload,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = shinyRateWorkflowService.GetPlanSources(project).ToList();
        if (!sources.Any(source => source.Layer == ProjectFileLayer.Base
            && string.Equals(source.RelativePath, SwShShinyRateWorkflowService.ExeFsMainPath, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shiny Rate vanilla base source could not be resolved.",
                file: SwShShinyRateWorkflowService.ExeFsMainPath,
                expected: "Readable selected-game base exefs/main"));
        }

        sources.Add(CreatePendingPayloadSource(payload));
        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectFileReference CreatePendingPayloadSource(string payload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/shiny-rate/rate/{hash}");
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

    private static string CreatePlanReason(ShinyRateSelection selection)
    {
        return selection.Mode switch
        {
            SwShShinyRateMode.Default => "Restore the game's runtime-dependent shiny reroll logic in exefs/main.",
            SwShShinyRateMode.AlwaysShiny => "Apply the always-shiny reroll-loop control bytes to exefs/main.",
            SwShShinyRateMode.FixedRolls => string.Create(
                CultureInfo.InvariantCulture,
                $"Set the global shiny PID roll count to {selection.RollCount} in exefs/main."),
            _ => "Update the Sword/Shield shiny reroll logic in exefs/main.",
        };
    }

    private static bool TryParseMode(string? mode, out SwShShinyRateMode parsedMode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();
        parsedMode = normalized switch
        {
            "default" => SwShShinyRateMode.Default,
            "fixed" => SwShShinyRateMode.FixedRolls,
            "always" => SwShShinyRateMode.AlwaysShiny,
            _ => SwShShinyRateMode.Default,
        };

        return normalized is "default" or "fixed" or "always";
    }

    private static void EnsureVerifiedVanillaBase(
        SwShShinyRateMainAnalysis analysis,
        ProjectGame? selectedGame)
    {
        if (analysis.Kind != SwShShinyRateMainKind.Default
            || analysis.DetectedGame != selectedGame
            || analysis.DetectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.Equals(analysis.BuildId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Shiny Rate requires the reviewed selected-game vanilla base exefs/main before apply or restore.");
        }
    }

    private static void EnsureEditableEffectiveSource(SwShShinyRateMainAnalysis analysis)
    {
        if (analysis.Kind is not (SwShShinyRateMainKind.Default
            or SwShShinyRateMainKind.FixedRolls
            or SwShShinyRateMainKind.AlwaysShiny))
        {
            throw new InvalidDataException(
                "The reviewed effective exefs/main is no longer a verified Shiny Rate source.");
        }
    }

    private static void VerifyOutput(
        byte[] baseBytes,
        byte[] output,
        ShinyRateSelection selection,
        ProjectGame? selectedGame)
    {
        var analysis = SwShShinyRateMainPatcher.Analyze(output, selectedGame);
        var verified = selection.Mode switch
        {
            SwShShinyRateMode.Default => analysis.Kind == SwShShinyRateMainKind.Default,
            SwShShinyRateMode.FixedRolls => analysis.Kind == SwShShinyRateMainKind.FixedRolls
                && analysis.RollCount == selection.RollCount,
            SwShShinyRateMode.AlwaysShiny => analysis.Kind == SwShShinyRateMainKind.AlwaysShiny,
            _ => false,
        };
        if (!verified || analysis.DetectedGame != selectedGame)
        {
            throw new InvalidDataException(
                "Shiny Rate output did not round-trip with the reviewed rate selection.");
        }

        SwShShinyRateMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, output);
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Shiny Rate output directory could not be resolved.");
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
                throw new IOException("Shiny Rate temporary output did not round-trip byte-for-byte.");
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

        var targetPath = SwShShinyRateWorkflowService.ResolveOutputPath(
            paths,
            SwShShinyRateWorkflowService.ExeFsMainPath);
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
