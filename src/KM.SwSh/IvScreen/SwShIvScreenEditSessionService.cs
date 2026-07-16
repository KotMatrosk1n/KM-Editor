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

namespace KM.SwSh.IvScreen;

public sealed class SwShIvScreenEditSessionService
{
    public const string IvScreenEditDomain = "workflow.ivScreen";

    private const string InstallRecordId = "iv-screen-v1-install";
    private const string UninstallRecordId = "iv-screen-v1-uninstall";
    private const string InstallField = "install";
    private const string UninstallField = "uninstall";
    private const string StageInstallSummary = "Stage IV Screen install or refresh.";
    private const string StageUninstallSummary = "Stage IV Screen uninstall.";
    private const string InstallReason = "Install or refresh IV Screen's reviewed Pokemon Summary raw-IV display graph in exefs/main.";
    private const string UninstallReason = "Uninstall the exact recognized IV Screen graph from exefs/main while preserving unrelated supported ExeFS edits.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShIvScreenWorkflowService ivScreenWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShIvScreenEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShIvScreenWorkflowService? ivScreenWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.ivScreenWorkflowService = ivScreenWorkflowService ?? new SwShIvScreenWorkflowService();
    }

    internal SwShIvScreenEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShIvScreenWorkflowService? ivScreenWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, ivScreenWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    internal SwShIvScreenEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShIvScreenWorkflowService? ivScreenWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, ivScreenWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    public SwShIvScreenEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = ivScreenWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShIvScreenWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShIvScreenEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, IvScreenEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen needs its own edit session before staging.",
                expected: "An IV Screen-only edit session"));
            return new SwShIvScreenEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SwShIvScreenEditResult(workflow, currentSession, diagnostics);
        }

        const string payload = "true";
        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingInstallEdit(CreatePendingSources(project, payload, isUninstall: false))],
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "IV Screen install is staged for change-plan review."));
        return new SwShIvScreenEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShIvScreenEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = ivScreenWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShIvScreenWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShIvScreenEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, IvScreenEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen needs its own edit session before staging uninstall.",
                expected: "An IV Screen-only edit session"));
            return new SwShIvScreenEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SwShIvScreenEditResult(workflow, currentSession, diagnostics);
        }

        const string payload = "true";
        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingUninstallEdit(CreatePendingSources(project, payload, isUninstall: true))],
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "IV Screen uninstall is staged for change-plan review."));
        return new SwShIvScreenEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = ivScreenWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShIvScreenWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage IV Screen install or uninstall before validating."
                    : "IV Screen requires exactly one canonical pending edit.",
                expected: "Exactly one pending IV Screen install or uninstall edit"));
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
                    "Pending edit does not target the canonical IV Screen install or uninstall record.",
                    field: edit.Field,
                    expected: $"{IvScreenEditDomain}/{InstallRecordId}/{InstallField} or {IvScreenEditDomain}/{UninstallRecordId}/{UninstallField}"));
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
                "Pending IV Screen change is valid for change-plan review."));
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
        var pendingEdit = session.PendingEdits[0];
        var isUninstall = IsCanonicalUninstallIdentity(pendingEdit);
        var sources = CreatePendingSources(project, pendingEdit.NewValue ?? string.Empty, isUninstall);
        var writes = new[]
        {
            new PlannedFileWrite(
                SwShIvScreenWorkflowService.ExeFsMainPath,
                sources,
                File.Exists(targetPath),
                isUninstall ? UninstallReason : InstallReason),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(
                CultureInfo.InvariantCulture,
                $"IV Screen change plan preview contains {writes.Length:N0} target file(s).")));

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
                "Reviewed IV Screen change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed IV Screen change plan"));
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
                "IV Screen sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed IV Screen change plan"));
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
                "IV Screen prepared apply requires exactly one canonical pending edit.",
                expected: "Exactly one pending IV Screen install or uninstall edit"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var edit = session.PendingEdits[0];
        if (IsCanonicalInstallIdentity(edit))
        {
            ApplyPreparedInstall(paths, writtenFiles, diagnostics);
        }
        else if (IsCanonicalUninstallIdentity(edit))
        {
            ApplyPreparedUninstall(paths, writtenFiles, diagnostics);
        }
        else
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen prepared apply received a noncanonical pending edit.",
                expected: "Canonical IV Screen install or uninstall edit"));
        }

        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private void ApplyPreparedInstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShIvScreenWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = SwShIvScreenWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShIvScreenWorkflowService.ExeFsMainPath);
        if (source is null || targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen source, vanilla base, or output target could not be resolved.",
                file: SwShIvScreenWorkflowService.ExeFsMainPath,
                expected: "Readable reviewed source and vanilla base with writable LayeredFS target"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var baseAnalysis = SwShIvScreenMainPatcher.Analyze(baseBytes, paths.SelectedGame);
            EnsureVerifiedVanillaBase(baseAnalysis);
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var sourceAnalysis = SwShIvScreenMainPatcher.Analyze(sourceBytes, paths.SelectedGame);
            EnsureEditableEffectiveSource(sourceAnalysis);
            SwShIvScreenMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, sourceBytes);

            var output = SwShIvScreenMainPatcher.Apply(sourceBytes, paths.SelectedGame);
            WriteOutputAtomically(
                targetPath,
                output,
                roundTrip => VerifyInstalledOutput(baseAnalysis, roundTrip, paths.SelectedGame));
            writtenFiles.Add(new ProjectFileReference(
                ProjectFileLayer.Generated,
                SwShIvScreenWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied IV Screen changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"IV Screen verified output could not be prepared: {exception.Message}",
                file: SwShIvScreenWorkflowService.ExeFsMainPath,
                expected: "Reviewed selected-game source, exact install action, and writable output"));
        }
    }

    private static void ApplyPreparedUninstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = SwShIvScreenWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShIvScreenWorkflowService.ExeFsMainPath);
        if (targetPath is null || basePath is null || !File.Exists(basePath) || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen uninstall could not resolve the reviewed generated and vanilla base exefs/main files.",
                file: SwShIvScreenWorkflowService.ExeFsMainPath,
                expected: "Existing reviewed LayeredFS exefs/main and readable vanilla base"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var baseAnalysis = SwShIvScreenMainPatcher.Analyze(baseBytes, paths.SelectedGame);
            EnsureVerifiedVanillaBase(baseAnalysis);
            var currentBytes = File.ReadAllBytes(targetPath);
            var currentAnalysis = SwShIvScreenMainPatcher.Analyze(currentBytes, paths.SelectedGame);
            if (currentAnalysis.Kind is not (SwShIvScreenInstallKind.InstalledV1 or SwShIvScreenInstallKind.InstalledLegacyV1))
            {
                throw new InvalidDataException("The reviewed effective main no longer contains an exact recognized IV Screen graph.");
            }

            var restored = SwShIvScreenMainPatcher.RestoreFromBase(
                currentBytes,
                baseBytes,
                paths.SelectedGame);
            VerifyUninstalledOutput(baseAnalysis, restored, paths.SelectedGame);
            if (SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(restored, baseBytes))
            {
                File.Delete(targetPath);
                if (File.Exists(targetPath))
                {
                    throw new IOException("IV Screen uninstall target still exists after verified deletion.");
                }
            }
            else
            {
                WriteOutputAtomically(
                    targetPath,
                    restored,
                    roundTrip => VerifyUninstalledOutput(baseAnalysis, roundTrip, paths.SelectedGame));
            }

            writtenFiles.Add(new ProjectFileReference(
                ProjectFileLayer.Generated,
                SwShIvScreenWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled IV Screen from the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"IV Screen uninstall could not prepare a verified restoration: {exception.Message}",
                file: SwShIvScreenWorkflowService.ExeFsMainPath,
                expected: "Exact recognized IV Screen graph, vanilla base, and writable output"));
        }
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SwShIvScreenWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen install requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus is "blocked" or "foreign")
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                workflow.CanUninstall
                    ? $"IV Screen legacy migration is unavailable. {workflow.InstallMessage}"
                    : "IV Screen cannot stage while exefs/main has a foreign or conflicting Pokemon Summary hook graph.",
                expected: workflow.CanUninstall
                    ? "Exact current IV Screen dependency anchors for migration"
                    : "Vanilla or exact recognized IV Screen graph"));
            return false;
        }

        AddWorkflowErrors(workflow, diagnostics);
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            var source = ResolveWorkflowFile(project, SwShIvScreenWorkflowService.ExeFsMainPath);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "IV Screen install preflight could not resolve the effective exefs/main.",
                    file: SwShIvScreenWorkflowService.ExeFsMainPath,
                    expected: "Readable effective selected-game exefs/main"));
            }
            else
            {
                try
                {
                    var preflightError = SwShIvScreenMainPatcher.GetApplyPreflightError(
                        File.ReadAllBytes(source.AbsolutePath),
                        project.Paths.SelectedGame);
                    if (preflightError is not null)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"IV Screen install or legacy migration is unavailable: {preflightError}",
                            file: SwShIvScreenWorkflowService.ExeFsMainPath,
                            expected: "Exact current IV Screen dependency anchors"));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"IV Screen install preflight could not read exefs/main: {exception.Message}",
                        file: SwShIvScreenWorkflowService.ExeFsMainPath,
                        expected: "Readable effective selected-game exefs/main"));
                }
            }
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SwShIvScreenWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen uninstall requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (!workflow.CanUninstall)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen uninstall requires an exact current or exact supported legacy install.",
                expected: "Exact recognized IV Screen graph"));
            return false;
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen uninstall can only remove a generated LayeredFS exefs/main.",
                file: SwShIvScreenWorkflowService.ExeFsMainPath,
                expected: "IV Screen installed in the configured output root"));
            return false;
        }

        AddWorkflowErrors(workflow, diagnostics);
        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void AddWorkflowErrors(
        SwShIvScreenWorkflow workflow,
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
            IvScreenEditDomain,
            StageInstallSummary,
            sources,
            InstallRecordId,
            InstallField,
            "true");
    }

    private static PendingEdit CreatePendingUninstallEdit(IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            IvScreenEditDomain,
            StageUninstallSummary,
            sources,
            UninstallRecordId,
            UninstallField,
            "true");
    }

    private static bool IsCanonicalInstallIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, IvScreenEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, InstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, InstallField, StringComparison.Ordinal);
    }

    private static bool IsCanonicalUninstallIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, IvScreenEditDomain, StringComparison.Ordinal)
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
                "Pending IV Screen edit does not have the canonical staged summary.",
                field: expectedField,
                expected: expectedSummary));
        }

        if (!string.Equals(edit.NewValue, "true", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending IV Screen action payload must be exactly true.",
                field: expectedField,
                expected: "true"));
        }

        ValidateCanonicalSources(
            edit.Sources,
            CreatePendingSources(project, "true", isUninstall),
            expectedField,
            diagnostics);
    }

    private IReadOnlyList<ProjectFileReference> CreatePendingSources(
        OpenedProject project,
        string payload,
        bool isUninstall)
    {
        return ivScreenWorkflowService
            .GetPlanSources(project)
            .Append(CreatePendingPayloadSource(payload, isUninstall))
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectFileReference CreatePendingPayloadSource(string payload, bool isUninstall)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var action = isUninstall ? "uninstall" : "install";
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/iv-screen/{action}/{hash}");
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
            "Pending IV Screen sources do not match the canonical selected-game base, effective main, and action fingerprint.",
            field: field,
            expected: "Canonical ordered unique IV Screen source references"));
    }

    private static void EnsureVerifiedVanillaBase(SwShIvScreenAnalysis analysis)
    {
        if (analysis.Kind != SwShIvScreenInstallKind.NotInstalled
            || analysis.DetectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.Equals(analysis.BuildId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "IV Screen requires the reviewed selected-game vanilla base exefs/main before apply or uninstall.");
        }
    }

    private static void EnsureEditableEffectiveSource(SwShIvScreenAnalysis analysis)
    {
        if (analysis.Kind is not (SwShIvScreenInstallKind.NotInstalled
            or SwShIvScreenInstallKind.InstalledV1
            or SwShIvScreenInstallKind.InstalledLegacyV1))
        {
            throw new InvalidDataException(
                "The reviewed effective exefs/main is no longer vanilla or an exact recognized IV Screen source.");
        }
    }

    private static void VerifyInstalledOutput(
        SwShIvScreenAnalysis baseAnalysis,
        byte[] output,
        ProjectGame? selectedGame)
    {
        var outputAnalysis = SwShIvScreenMainPatcher.Analyze(output, selectedGame);
        if (outputAnalysis.Kind != SwShIvScreenInstallKind.InstalledV1)
        {
            throw new InvalidDataException("Patched exefs/main did not round-trip as an exact current IV Screen install.");
        }

        VerifyExecutableIdentity(baseAnalysis, outputAnalysis, selectedGame);
    }

    private static void VerifyUninstalledOutput(
        SwShIvScreenAnalysis baseAnalysis,
        byte[] output,
        ProjectGame? selectedGame)
    {
        var outputAnalysis = SwShIvScreenMainPatcher.Analyze(output, selectedGame);
        if (outputAnalysis.Kind is not (SwShIvScreenInstallKind.NotInstalled
            or SwShIvScreenInstallKind.NotInstalledDependencyConflict))
        {
            throw new InvalidDataException("Restored exefs/main did not round-trip with IV Screen uninstalled.");
        }

        VerifyExecutableIdentity(baseAnalysis, outputAnalysis, selectedGame);
    }

    private static void VerifyExecutableIdentity(
        SwShIvScreenAnalysis baseAnalysis,
        SwShIvScreenAnalysis outputAnalysis,
        ProjectGame? selectedGame)
    {
        if (!string.Equals(baseAnalysis.BuildId, outputAnalysis.BuildId, StringComparison.Ordinal)
            || baseAnalysis.DetectedGame != outputAnalysis.DetectedGame
            || outputAnalysis.DetectedGame != selectedGame
            || !string.Equals(
                baseAnalysis.PrimaryValueSourceOffsetHex,
                outputAnalysis.PrimaryValueSourceOffsetHex,
                StringComparison.Ordinal)
            || !string.Equals(
                baseAnalysis.XToggleRefreshOffsetHex,
                outputAnalysis.XToggleRefreshOffsetHex,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("IV Screen output executable identity changed during apply verification.");
        }
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("IV Screen output directory could not be resolved.");
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
                throw new IOException("IV Screen temporary output did not round-trip byte-for-byte.");
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
                "IV Screen apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShIvScreenWorkflowService.ResolveOutputPath(
            paths,
            SwShIvScreenWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "IV Screen target must stay inside the configured output root.",
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

        var sourcePath = SwShIvScreenWorkflowService.ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
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
            Domain: IvScreenEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
