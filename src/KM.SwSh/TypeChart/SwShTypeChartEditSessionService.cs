// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.TypeChart;

public sealed class SwShTypeChartEditSessionService
{
    public const string TypeChartEditDomain = "workflow.typeChart";

    private const string RecordId = "type-chart";
    private const string EffectivenessField = "effectiveness";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShTypeChartWorkflowService typeChartWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShTypeChartEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTypeChartWorkflowService? typeChartWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.typeChartWorkflowService = typeChartWorkflowService ?? new SwShTypeChartWorkflowService();
    }

    internal SwShTypeChartEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShTypeChartWorkflowService? typeChartWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, typeChartWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    internal SwShTypeChartEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShTypeChartWorkflowService? typeChartWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, typeChartWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    public SwShTypeChartEditResult StageChart(
        ProjectPaths paths,
        IReadOnlyList<int> values,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
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
        var sourceReferences = CreateCanonicalSources(project, payload, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShTypeChartEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingEdit(payload, sourceReferences)],
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

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = typeChartWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage Type Chart values before validating."
                    : "Type Chart expects exactly one canonical staged chart edit.",
                expected: "Exactly one pending Type Chart edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (!IsCanonicalIdentity(edit))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the canonical Type Chart field.",
                    field: edit.Field,
                    expected: $"{TypeChartEditDomain}/{RecordId}/{EffectivenessField}"));
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
        var edit = session.PendingEdits.Single();
        var values = DecodeValues(edit.NewValue, diagnostics);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var sources = CreateCanonicalSources(project, edit.NewValue ?? string.Empty, diagnostics);
        if (targetPath is null
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            SwShTypeChartWorkflowService.ExeFsMainPath,
            sources,
            File.Exists(targetPath),
            IsVanillaDisplayValues(values)
                ? "Restore the Sword/Shield type-effectiveness table from the verified vanilla base exefs/main."
                : "Update the Sword/Shield type-effectiveness table in exefs/main.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Type Chart change plan preview contains 1 target file."));

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
                "Reviewed Type Chart change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Type Chart change plan"));
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
                "Type Chart sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Type Chart change plan"));
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
                "Type Chart prepared apply requires exactly one canonical pending edit.",
                expected: $"{TypeChartEditDomain}/{RecordId}/{EffectivenessField}"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var values = DecodeValues(session.PendingEdits[0].NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        ApplyMain(paths, values, writtenFiles, diagnostics);
        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
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
        var basePath = SwShTypeChartWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShTypeChartWorkflowService.ExeFsMainPath);
        if (source is null || targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart source, vanilla base, or output target could not be resolved.",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
                expected: "Readable reviewed source and vanilla base with writable LayeredFS target"));
            return;
        }

        try
        {
            var gameOrderValues = SwShTypeChartWorkflowService.ToGameOrder(values);
            var baseBytes = File.ReadAllBytes(basePath);
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            EnsureVerifiedVanillaBase(
                SwShTypeChartMainPatcher.Analyze(baseBytes, paths.SelectedGame),
                paths.SelectedGame);
            EnsureEditableEffectiveSource(
                SwShTypeChartMainPatcher.Analyze(sourceBytes, paths.SelectedGame));
            SwShTypeChartMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, sourceBytes);

            var isRestore = gameOrderValues.SequenceEqual(SwShTypeChartMainPatcher.VanillaChartValues);
            var output = isRestore
                ? SwShTypeChartMainPatcher.RestoreFromBase(sourceBytes, baseBytes, paths.SelectedGame)
                : SwShTypeChartMainPatcher.ApplyChart(sourceBytes, gameOrderValues, paths.SelectedGame);
            VerifyOutput(baseBytes, output, gameOrderValues, paths.SelectedGame);

            if (isRestore && SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(output, baseBytes))
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                if (File.Exists(targetPath))
                {
                    throw new IOException("Type Chart restore target still exists after verified deletion.");
                }
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    output,
                    roundTrip => VerifyOutput(baseBytes, roundTrip, gameOrderValues, paths.SelectedGame));
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShTypeChartWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                isRestore
                    ? "Restored Type Chart defaults in the configured LayeredFS output root."
                    : "Applied Type Chart changes to exefs/main in the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Type Chart verified output could not be prepared: {exception.Message}",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
                expected: "Reviewed selected-game source, exact effectiveness payload, verified vanilla base, and writable output"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShTypeChartWorkflow workflow,
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
                    "Type Chart apply requires valid base paths, Pokemon Sword or Shield, and a valid output root.",
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
        catch (Exception exception) when (exception is InvalidDataException or ArgumentNullException)
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

    private static bool IsCanonicalIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, TypeChartEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, EffectivenessField, StringComparison.Ordinal);
    }

    private void ValidateCanonicalEdit(
        OpenedProject project,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var values = DecodeValues(edit.NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        var canonicalPayload = EncodeValues(values);
        if (!string.Equals(edit.NewValue, canonicalPayload, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Type Chart values are not in canonical payload format.",
                field: EffectivenessField,
                expected: canonicalPayload));
        }

        const string expectedSummary = "Stage Type Chart effectiveness table.";
        if (!string.Equals(edit.Summary, expectedSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Type Chart edit does not have the canonical staged summary.",
                field: EffectivenessField,
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
                "Pending Type Chart sources do not match the canonical base, effective, and payload references.",
                field: EffectivenessField,
                expected: "Canonical ordered unique Type Chart source references"));
        }
    }

    private IReadOnlyList<ProjectFileReference> CreateCanonicalSources(
        OpenedProject project,
        string payload,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = typeChartWorkflowService.GetPlanSources(project).ToList();
        if (!sources.Any(source => source.Layer == ProjectFileLayer.Base
            && string.Equals(source.RelativePath, SwShTypeChartWorkflowService.ExeFsMainPath, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Type Chart vanilla base source could not be resolved.",
                file: SwShTypeChartWorkflowService.ExeFsMainPath,
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
            $"pending/type-chart/effectiveness/{hash}");
    }

    private static bool IsVanillaDisplayValues(IReadOnlyList<int> displayOrderValues)
    {
        return SwShTypeChartWorkflowService
            .ToGameOrder(displayOrderValues)
            .SequenceEqual(SwShTypeChartMainPatcher.VanillaChartValues);
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

    private static void EnsureVerifiedVanillaBase(
        SwShTypeChartMainAnalysis analysis,
        ProjectGame? selectedGame)
    {
        if (analysis.Kind != SwShTypeChartMainKind.Vanilla
            || analysis.DetectedGame != selectedGame
            || analysis.DetectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.Equals(analysis.BuildId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Type Chart requires the reviewed selected-game vanilla base exefs/main before apply or restore.");
        }
    }

    private static void EnsureEditableEffectiveSource(SwShTypeChartMainAnalysis analysis)
    {
        if (analysis.Kind is not (SwShTypeChartMainKind.Vanilla or SwShTypeChartMainKind.Modified))
        {
            throw new InvalidDataException(
                "The reviewed effective exefs/main is no longer a verified Type Chart source.");
        }
    }

    private static void VerifyOutput(
        byte[] baseBytes,
        byte[] output,
        IReadOnlyList<int> expectedGameOrderValues,
        ProjectGame? selectedGame)
    {
        var analysis = SwShTypeChartMainPatcher.Analyze(output, selectedGame);
        if (analysis.Kind is not (SwShTypeChartMainKind.Vanilla or SwShTypeChartMainKind.Modified)
            || analysis.DetectedGame != selectedGame
            || !analysis.EffectivenessValues.SequenceEqual(expectedGameOrderValues))
        {
            throw new InvalidDataException(
                "Type Chart output did not round-trip with the reviewed effectiveness table.");
        }

        SwShTypeChartMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, output);
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Type Chart output directory could not be resolved.");
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
                throw new IOException("Type Chart temporary output did not round-trip byte-for-byte.");
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
            Domain: TypeChartEditDomain,
            Field: field,
            Expected: expected);
    }
}
