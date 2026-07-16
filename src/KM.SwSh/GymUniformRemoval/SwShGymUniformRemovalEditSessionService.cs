// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.GymUniformRemoval;

public sealed class SwShGymUniformRemovalEditSessionService
{
    public const string GymUniformRemovalEditDomain = "workflow.gymUniformRemoval";

    private const string InstallRecordId = "gym-uniform-removal-v1-install";
    private const string UninstallRecordId = "gym-uniform-removal-v1-uninstall";
    private const string InstallField = "install";
    private const string UninstallField = "uninstall";
    private const string StageInstallSummary = "Stage Gym Uniform Removal install.";
    private const string StageUninstallSummary = "Stage Gym Uniform Removal uninstall.";
    private const string InstallReason = "Install or refresh Gym Uniform Removal's selected build-ID IPS patch in exefs.";
    private const string UninstallReason = "Remove Gym Uniform Removal's exact selected build-ID IPS patch from exefs.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShGymUniformRemovalWorkflowService gymUniformRemovalWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShGymUniformRemovalEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShGymUniformRemovalWorkflowService? gymUniformRemovalWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.gymUniformRemovalWorkflowService = gymUniformRemovalWorkflowService ?? new SwShGymUniformRemovalWorkflowService();
    }

    internal SwShGymUniformRemovalEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShGymUniformRemovalWorkflowService? gymUniformRemovalWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, gymUniformRemovalWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    internal SwShGymUniformRemovalEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShGymUniformRemovalWorkflowService? gymUniformRemovalWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, gymUniformRemovalWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    public static bool IsCanonicalUninstallSession(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.PendingEdits.Count == 1
            && IsCanonicalUninstallIdentity(session.PendingEdits[0]);
    }

    public SwShGymUniformRemovalEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = gymUniformRemovalWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShGymUniformRemovalWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal needs its own edit session before staging.",
                expected: "A Gym Uniform Removal-only edit session"));
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingInstallEdit(CreatePendingSources(project, isUninstall: false))],
        };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Gym Uniform Removal install is staged for change-plan review."));
        return new SwShGymUniformRemovalEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShGymUniformRemovalEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = gymUniformRemovalWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShGymUniformRemovalWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal needs its own edit session before staging uninstall.",
                expected: "A Gym Uniform Removal-only edit session"));
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SwShGymUniformRemovalEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingUninstallEdit(CreatePendingSources(project, isUninstall: true))],
        };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Gym Uniform Removal uninstall is staged for change-plan review."));
        return new SwShGymUniformRemovalEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = gymUniformRemovalWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShGymUniformRemovalWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage Gym Uniform Removal install or uninstall before validating."
                    : "Gym Uniform Removal requires exactly one canonical pending edit.",
                expected: "Exactly one pending Gym Uniform Removal install or uninstall edit"));
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
                    "Pending edit does not target the canonical Gym Uniform Removal install or uninstall record.",
                    field: edit.Field,
                    expected: $"{GymUniformRemovalEditDomain}/{InstallRecordId}/{InstallField} or {GymUniformRemovalEditDomain}/{UninstallRecordId}/{UninstallField}"));
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
                "Pending Gym Uniform Removal change is valid for change-plan review."));
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

        var targetRelativePath = ResolveIpsRelativePath(paths);
        var targetPath = ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var edit = session.PendingEdits[0];
        var isUninstall = IsCanonicalUninstallIdentity(edit);
        var write = new PlannedFileWrite(
            targetRelativePath,
            CreatePendingSources(project, isUninstall),
            File.Exists(targetPath),
            isUninstall ? UninstallReason : InstallReason);
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Gym Uniform Removal change plan preview contains 1 target file."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, [write], diagnostics),
            preserveExplicitSourceLayers: isUninstall);
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
                "Reviewed Gym Uniform Removal change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Gym Uniform Removal change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        beforeAcquireApplyScope?.Invoke();
        var preserveExplicitSourceLayers = IsCanonicalUninstallSession(session);
        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var applyScope,
                out var acquireDiagnostics,
                preserveExplicitSourceLayers))
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
                "Gym Uniform Removal sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Gym Uniform Removal change plan"));
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
                "Gym Uniform Removal prepared apply requires exactly one canonical pending edit.",
                expected: "Exactly one pending Gym Uniform Removal install or uninstall edit"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var edit = session.PendingEdits[0];
        var isUninstall = IsCanonicalUninstallIdentity(edit);
        if (!isUninstall && !IsCanonicalInstallIdentity(edit))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal prepared apply received a noncanonical pending edit.",
                expected: "Canonical Gym Uniform Removal install or uninstall edit"));
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
        var effectiveSource = ResolveWorkflowFile(project, SwShGymUniformRemovalWorkflowService.ExeFsMainPath);
        var basePath = SwShGymUniformRemovalWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShGymUniformRemovalWorkflowService.ExeFsMainPath);
        var targetRelativePath = ResolveIpsRelativePath(paths);
        var targetPath = ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (effectiveSource is null || basePath is null || !File.Exists(basePath) || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal base, effective main, or IPS output target could not be resolved.",
                file: SwShGymUniformRemovalWorkflowService.ExeFsMainPath,
                expected: "Readable reviewed base and effective main with writable build-ID IPS target"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            EnsureVerifiedVanillaBase(
                SwShGymUniformRemovalMainPatcher.Analyze(baseBytes, paths.SelectedGame));
            var effectiveBytes = File.ReadAllBytes(effectiveSource.AbsolutePath);
            EnsureEditableEffectiveSource(
                SwShGymUniformRemovalMainPatcher.Analyze(effectiveBytes, paths.SelectedGame));
            SwShGymUniformRemovalMainPatcher.EnsureCompatibleExecutableIdentity(
                baseBytes,
                effectiveBytes);

            var output = SwShGymUniformRemovalMainPatcher.CreateIpsPatch(
                baseBytes,
                paths.SelectedGame);
            WriteOutputAtomically(
                targetPath,
                output,
                roundTrip => VerifyInstalledIps(baseBytes, roundTrip, paths.SelectedGame));
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Gym Uniform Removal IPS changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal verified IPS output could not be prepared: {exception.Message}",
                file: targetRelativePath,
                expected: "Reviewed selected-game sources, exact install action, and writable IPS target"));
        }
    }

    private static void ApplyPreparedUninstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var basePath = SwShGymUniformRemovalWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShGymUniformRemovalWorkflowService.ExeFsMainPath);
        var targetRelativePath = ResolveIpsRelativePath(paths);
        var targetPath = ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (basePath is null || !File.Exists(basePath) || targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall could not resolve the reviewed vanilla base and generated IPS files.",
                file: targetRelativePath,
                expected: "Readable vanilla base and existing exact or recognized legacy Gym Uniform Removal IPS"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            EnsureVerifiedVanillaBase(
                SwShGymUniformRemovalMainPatcher.Analyze(baseBytes, paths.SelectedGame));
            var ipsAnalysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsArtifact(
                File.ReadAllBytes(targetPath),
                baseBytes,
                paths.SelectedGame);
            if (ipsAnalysis.Kind is not (SwShGymUniformRemovalIpsArtifactKind.Current
                or SwShGymUniformRemovalIpsArtifactKind.Legacy))
            {
                throw new InvalidDataException(ipsAnalysis.Message);
            }

            File.Delete(targetPath);
            if (File.Exists(targetPath))
            {
                throw new IOException("Gym Uniform Removal IPS target still exists after verified deletion.");
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, targetRelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Gym Uniform Removal IPS from the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gym Uniform Removal uninstall could not remove the verified IPS patch: {exception.Message}",
                file: targetRelativePath,
                expected: "Exact current or recognized legacy Gym Uniform Removal IPS"));
        }
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SwShGymUniformRemovalWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal install requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        AddWorkflowErrors(workflow, diagnostics);
        if (workflow.InstallStatus is "blocked" or "foreign")
        {
            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Gym Uniform Removal cannot stage while a main source or IPS artifact conflicts with the selected-game mapping.",
                    expected: "Verified vanilla or recognized Gym Uniform Removal handler and IPS bytes"));
            }

            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SwShGymUniformRemovalWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (!workflow.CanUninstall)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall requires an exact current or recognized legacy build-ID IPS patch.",
                expected: "Owned Gym Uniform Removal IPS in the configured output root"));
            return false;
        }

        var targetRelativePath = ResolveIpsRelativePath(paths);
        var targetPath = ResolveOutputPath(paths, targetRelativePath, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal uninstall can only remove a generated build-ID IPS patch.",
                file: targetRelativePath,
                expected: "Gym Uniform Removal IPS installed in the configured output root"));
            return false;
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void AddWorkflowErrors(
        SwShGymUniformRemovalWorkflow workflow,
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
            GymUniformRemovalEditDomain,
            StageInstallSummary,
            sources,
            InstallRecordId,
            InstallField,
            "true");
    }

    private static PendingEdit CreatePendingUninstallEdit(IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            GymUniformRemovalEditDomain,
            StageUninstallSummary,
            sources,
            UninstallRecordId,
            UninstallField,
            "true");
    }

    private static bool IsCanonicalInstallIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, InstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, InstallField, StringComparison.Ordinal);
    }

    private static bool IsCanonicalUninstallIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, GymUniformRemovalEditDomain, StringComparison.Ordinal)
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
                "Pending Gym Uniform Removal edit does not have the canonical staged summary.",
                field: expectedField,
                expected: expectedSummary));
        }

        if (!string.Equals(edit.NewValue, "true", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gym Uniform Removal action payload must be exactly true.",
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
        var ipsRelativePath = ResolveIpsRelativePath(project.Paths);
        return gymUniformRemovalWorkflowService
            .GetPlanSources(project, ipsRelativePath, isUninstall)
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
            $"pending/gym-uniform-removal/{action}/{hash}");
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
            "Pending Gym Uniform Removal sources do not match the canonical action-specific base, effective main, IPS artifact, and action fingerprint.",
            field: field,
            expected: "Canonical ordered unique Gym Uniform Removal source references"));
    }

    private static void EnsureVerifiedVanillaBase(SwShGymUniformRemovalAnalysis analysis)
    {
        if (analysis.Kind != SwShGymUniformRemovalInstallKind.NotInstalled
            || analysis.DetectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.Equals(analysis.BuildId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Gym Uniform Removal requires the reviewed selected-game vanilla base exefs/main before apply or uninstall.");
        }
    }

    private static void EnsureEditableEffectiveSource(SwShGymUniformRemovalAnalysis analysis)
    {
        if (analysis.Kind is not (SwShGymUniformRemovalInstallKind.NotInstalled
            or SwShGymUniformRemovalInstallKind.InstalledV1
            or SwShGymUniformRemovalInstallKind.InstalledCompatible))
        {
            throw new InvalidDataException(
                "The reviewed effective exefs/main no longer has vanilla or recognized Gym Uniform Removal handler bytes.");
        }
    }

    private static void VerifyInstalledIps(
        byte[] baseBytes,
        byte[] output,
        ProjectGame? selectedGame)
    {
        var analysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsArtifact(
            output,
            baseBytes,
            selectedGame);
        if (analysis.Kind != SwShGymUniformRemovalIpsArtifactKind.Current
            || analysis.DetectedGame != selectedGame)
        {
            throw new InvalidDataException(
                "Generated IPS did not round-trip as the exact selected-game Gym Uniform Removal patch.");
        }
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Gym Uniform Removal output directory could not be resolved.");
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
                throw new IOException("Gym Uniform Removal temporary IPS did not round-trip byte-for-byte.");
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

    private static string ResolveIpsRelativePath(ProjectPaths paths)
    {
        if (!SwShGymUniformRemovalWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            throw new InvalidDataException(
                "Gym Uniform Removal requires Pokemon Sword or Pokemon Shield to choose the IPS filename.");
        }

        return SwShGymUniformRemovalMainPatcher.IpsRelativePath(paths.SelectedGame!.Value);
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
                "Gym Uniform Removal apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShGymUniformRemovalWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gym Uniform Removal target must stay inside the configured output root.",
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

        var sourcePath = SwShGymUniformRemovalWorkflowService.ResolveSourcePath(project.Paths, entry);
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
            Domain: GymUniformRemovalEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(string AbsolutePath);
}
