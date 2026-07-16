// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.FashionUnlock;

public sealed class SwShFashionUnlockEditSessionService
{
    public const string FashionUnlockEditDomain = "workflow.fashionUnlock";

    private const string InstallRecordId = "fashion-unlock-v1-install";
    private const string UninstallRecordId = "fashion-unlock-v1-uninstall";
    private const string InstallField = "install";
    private const string UninstallField = "uninstall";
    private const string StageInstallSummary = "Stage Fashion Unlock install.";
    private const string StageUninstallSummary = "Stage Fashion Unlock uninstall.";
    private const string InstallReason = "Install or refresh Fashion Unlock ownership-check stubs in exefs/main.";
    private const string UninstallReason = "Uninstall Fashion Unlock from exefs/main while preserving other generated ExeFS edits.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShFashionUnlockWorkflowService fashionUnlockWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShFashionUnlockEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShFashionUnlockWorkflowService? fashionUnlockWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fashionUnlockWorkflowService = fashionUnlockWorkflowService ?? new SwShFashionUnlockWorkflowService();
    }

    internal SwShFashionUnlockEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShFashionUnlockWorkflowService? fashionUnlockWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, fashionUnlockWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    internal SwShFashionUnlockEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShFashionUnlockWorkflowService? fashionUnlockWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, fashionUnlockWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    public SwShFashionUnlockEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShFashionUnlockWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock needs its own edit session before staging.",
                expected: "A Fashion Unlock-only edit session"));
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingInstallEdit(CreatePendingSources(project, isUninstall: false))],
        };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fashion Unlock install is staged for change-plan review."));
        return new SwShFashionUnlockEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShFashionUnlockEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShFashionUnlockWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock needs its own edit session before staging uninstall.",
                expected: "A Fashion Unlock-only edit session"));
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SwShFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingUninstallEdit(CreatePendingSources(project, isUninstall: true))],
        };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fashion Unlock uninstall is staged for change-plan review."));
        return new SwShFashionUnlockEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShFashionUnlockWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage Fashion Unlock install or uninstall before validating."
                    : "Fashion Unlock requires exactly one canonical pending edit.",
                expected: "Exactly one pending Fashion Unlock install or uninstall edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (IsCanonicalInstallIdentity(edit))
            {
                ValidateCanonicalEdit(project, edit, isUninstall: false, diagnostics);
            }
            else if (IsCanonicalUninstallIdentity(edit))
            {
                ValidateCanonicalEdit(project, edit, isUninstall: true, diagnostics);
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the canonical Fashion Unlock install or uninstall record.",
                    field: edit.Field,
                    expected: $"{FashionUnlockEditDomain}/{InstallRecordId}/{InstallField} or {FashionUnlockEditDomain}/{UninstallRecordId}/{UninstallField}"));
            }
        }

        if (session.PendingEdits.Count == 1 && IsCanonicalUninstallIdentity(session.PendingEdits[0]))
        {
            CanStageUninstall(project, workflow, paths, diagnostics);
        }
        else
        {
            CanStageInstall(project, workflow, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Fashion Unlock change is valid for change-plan review."));
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

        var project = projectWorkspaceService.Open(paths);
        var edit = session.PendingEdits[0];
        var isUninstall = IsCanonicalUninstallIdentity(edit);
        var write = new PlannedFileWrite(
            SwShFashionUnlockWorkflowService.ExeFsMainPath,
            CreatePendingSources(project, isUninstall),
            File.Exists(targetPath),
            isUninstall ? UninstallReason : InstallReason);
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fashion Unlock change plan preview contains 1 target file."));

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
                "Reviewed Fashion Unlock change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Fashion Unlock change plan"));
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
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, acquireDiagnostics);
        }

        using var verifiedScope = applyScope!;
        var snapshotPlan = CreateChangePlan(verifiedScope.ApplyPaths, session);
        if (!verifiedScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan))
        {
            var staleDiagnostics = preparedPlan.Diagnostics.ToList();
            staleDiagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Fashion Unlock change plan"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, staleDiagnostics);
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
        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock prepared apply requires exactly one canonical pending edit.",
                expected: "Exactly one pending Fashion Unlock install or uninstall edit"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var edit = session.PendingEdits[0];
        var isUninstall = IsCanonicalUninstallIdentity(edit);
        if (!isUninstall && !IsCanonicalInstallIdentity(edit))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock prepared apply received a noncanonical pending edit.",
                expected: "Canonical Fashion Unlock install or uninstall edit"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        ValidateCanonicalEdit(project, edit, isUninstall, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        if (isUninstall)
        {
            ApplyPreparedUninstall(paths, writtenFiles, diagnostics);
        }
        else
        {
            ApplyPreparedInstall(paths, writtenFiles, diagnostics);
        }

        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private void ApplyPreparedInstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShFashionUnlockWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = SwShFashionUnlockWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShFashionUnlockWorkflowService.ExeFsMainPath);
        if (source is null || targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock source, vanilla base, or output target could not be resolved.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Readable reviewed source and vanilla base with writable LayeredFS target"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            EnsureVerifiedVanillaBase(
                SwShFashionUnlockMainPatcher.Analyze(baseBytes, paths.SelectedGame));
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            EnsureEditableEffectiveSource(
                SwShFashionUnlockMainPatcher.Analyze(sourceBytes, paths.SelectedGame));
            SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, sourceBytes);

            var output = SwShFashionUnlockMainPatcher.Apply(sourceBytes, paths.SelectedGame);
            WriteOutputAtomically(
                targetPath,
                output,
                roundTrip => VerifyInstalledOutput(baseBytes, roundTrip, paths.SelectedGame));
            writtenFiles.Add(new ProjectFileReference(
                ProjectFileLayer.Generated,
                SwShFashionUnlockWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Fashion Unlock changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock verified output could not be prepared: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Reviewed selected-game source, exact install action, and writable output"));
        }
    }

    private static void ApplyPreparedUninstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = SwShFashionUnlockWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShFashionUnlockWorkflowService.ExeFsMainPath);
        if (targetPath is null || basePath is null || !File.Exists(basePath) || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall could not resolve the reviewed generated and vanilla base exefs/main files.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Existing reviewed LayeredFS exefs/main and readable vanilla base"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            EnsureVerifiedVanillaBase(
                SwShFashionUnlockMainPatcher.Analyze(baseBytes, paths.SelectedGame));
            var currentBytes = File.ReadAllBytes(targetPath);
            var currentAnalysis = SwShFashionUnlockMainPatcher.Analyze(currentBytes, paths.SelectedGame);
            if (currentAnalysis.Kind != SwShFashionUnlockInstallKind.Installed)
            {
                throw new InvalidDataException(
                    "The reviewed effective main no longer contains exact Fashion Unlock ownership stubs.");
            }

            SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, currentBytes);
            var restored = SwShFashionUnlockMainPatcher.RestoreFromBase(
                currentBytes,
                baseBytes,
                paths.SelectedGame);
            VerifyUninstalledOutput(baseBytes, restored, paths.SelectedGame);
            if (SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(restored, baseBytes))
            {
                File.Delete(targetPath);
                if (File.Exists(targetPath))
                {
                    throw new IOException("Fashion Unlock uninstall target still exists after verified deletion.");
                }
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    restored,
                    roundTrip => VerifyUninstalledOutput(baseBytes, roundTrip, paths.SelectedGame));
            }

            writtenFiles.Add(new ProjectFileReference(
                ProjectFileLayer.Generated,
                SwShFashionUnlockWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Fashion Unlock from the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fashion Unlock uninstall could not prepare a verified restoration: {exception.Message}",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Exact Fashion Unlock stubs, vanilla base, and writable output"));
        }
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SwShFashionUnlockWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock install requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        AddWorkflowErrors(workflow, diagnostics);
        if (workflow.InstallStatus == "blocked"
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock cannot stage while exefs/main has an unsupported build or conflicting ownership getter bytes.",
                expected: "Verified vanilla or exact installed Fashion Unlock getter entries"));
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SwShFashionUnlockWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (!workflow.CanUninstall)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall requires exact installed ownership stubs in generated exefs/main.",
                expected: "Exact Fashion Unlock install in the configured output root"));
            return false;
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall can only restore a generated LayeredFS exefs/main.",
                file: SwShFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Fashion Unlock installed in the configured output root"));
            return false;
        }

        AddWorkflowErrors(workflow, diagnostics);
        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void AddWorkflowErrors(
        SwShFashionUnlockWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic =>
                     diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }
    }

    private static PendingEdit CreatePendingInstallEdit(IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            FashionUnlockEditDomain,
            StageInstallSummary,
            sources,
            InstallRecordId,
            InstallField,
            "true");
    }

    private static PendingEdit CreatePendingUninstallEdit(IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            FashionUnlockEditDomain,
            StageUninstallSummary,
            sources,
            UninstallRecordId,
            UninstallField,
            "true");
    }

    private static bool IsCanonicalInstallIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, InstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, InstallField, StringComparison.Ordinal);
    }

    private static bool IsCanonicalUninstallIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, UninstallField, StringComparison.Ordinal);
    }

    private void ValidateCanonicalEdit(
        OpenedProject project,
        PendingEdit edit,
        bool isUninstall,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var expectedSummary = isUninstall ? StageUninstallSummary : StageInstallSummary;
        var expectedField = isUninstall ? UninstallField : InstallField;
        if (!string.Equals(edit.Summary, expectedSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Fashion Unlock edit does not have the canonical staged summary.",
                field: expectedField,
                expected: expectedSummary));
        }

        if (!string.Equals(edit.NewValue, "true", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Fashion Unlock action payload must be exactly true.",
                field: expectedField,
                expected: "true"));
        }

        ValidateCanonicalSources(
            edit.Sources,
            CreatePendingSources(project, isUninstall),
            expectedField,
            diagnostics);
    }

    private IReadOnlyList<ProjectFileReference> CreatePendingSources(
        OpenedProject project,
        bool isUninstall)
    {
        return fashionUnlockWorkflowService
            .GetPlanSources(project)
            .Append(CreatePendingPayloadSource(isUninstall))
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectFileReference CreatePendingPayloadSource(bool isUninstall)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("true")));
        var action = isUninstall ? "uninstall" : "install";
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/fashion-unlock/{action}/{hash}");
    }

    private static void ValidateCanonicalSources(
        IReadOnlyList<ProjectFileReference> actual,
        IReadOnlyList<ProjectFileReference> expected,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (actual.Count == expected.Count && actual.SequenceEqual(expected))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pending Fashion Unlock sources do not match the canonical selected-game base, effective main, and action fingerprint.",
            field: field,
            expected: "Canonical ordered unique Fashion Unlock source references"));
    }

    private static void EnsureVerifiedVanillaBase(SwShFashionUnlockAnalysis analysis)
    {
        if (analysis.Kind != SwShFashionUnlockInstallKind.NotInstalled
            || analysis.DetectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.Equals(analysis.BuildId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Fashion Unlock requires the reviewed selected-game vanilla base exefs/main before apply or uninstall.");
        }
    }

    private static void EnsureEditableEffectiveSource(SwShFashionUnlockAnalysis analysis)
    {
        if (analysis.Kind is not (SwShFashionUnlockInstallKind.NotInstalled
            or SwShFashionUnlockInstallKind.Installed))
        {
            throw new InvalidDataException(
                "The reviewed effective exefs/main is no longer vanilla or an exact Fashion Unlock install.");
        }
    }

    private static void VerifyInstalledOutput(
        byte[] baseBytes,
        byte[] output,
        ProjectGame? selectedGame)
    {
        SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, output);
        var analysis = SwShFashionUnlockMainPatcher.Analyze(output, selectedGame);
        if (analysis.Kind != SwShFashionUnlockInstallKind.Installed
            || analysis.DetectedGame != selectedGame)
        {
            throw new InvalidDataException(
                "Patched exefs/main did not round-trip as an exact selected-game Fashion Unlock install.");
        }
    }

    private static void VerifyUninstalledOutput(
        byte[] baseBytes,
        byte[] output,
        ProjectGame? selectedGame)
    {
        SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, output);
        var analysis = SwShFashionUnlockMainPatcher.Analyze(output, selectedGame);
        if (analysis.Kind != SwShFashionUnlockInstallKind.NotInstalled
            || analysis.DetectedGame != selectedGame)
        {
            throw new InvalidDataException(
                "Restored exefs/main did not round-trip with Fashion Unlock uninstalled.");
        }
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Fashion Unlock output directory could not be resolved.");
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
                throw new IOException("Fashion Unlock temporary output did not round-trip byte-for-byte.");
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
                "Fashion Unlock apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShFashionUnlockWorkflowService.ResolveOutputPath(
            paths,
            SwShFashionUnlockWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock target must stay inside the configured output root.",
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
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var sourcePath = SwShFashionUnlockWorkflowService.ResolveSourcePath(project.Paths, entry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(sourcePath)
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
            Domain: FashionUnlockEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(string AbsolutePath);
}
