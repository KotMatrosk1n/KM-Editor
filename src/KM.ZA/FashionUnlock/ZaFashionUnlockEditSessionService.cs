// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Output;
using KM.Core.Semantics;
using KM.ZA.ExeFs;
using KM.ZA.Workflows;

namespace KM.ZA.FashionUnlock;

public sealed class ZaFashionUnlockEditSessionService
{
    public const string FashionUnlockEditDomain = "workflow.fashionUnlock";

    private const string InstallRecordId = "fashion-unlock-v1-install";
    private const string UninstallRecordId = "fashion-unlock-v1-uninstall";
    private const string InstallField = "install";
    private const string UninstallField = "uninstall";
    private const string StageInstallSummary = "Stage Fashion Unlock install.";
    private const string StageUninstallSummary = "Stage Fashion Unlock uninstall.";
    private const string InstallReason = "Install or refresh Fashion Unlock wardrobe access instructions in exefs/main.";
    private const string UninstallReason = "Uninstall Fashion Unlock from exefs/main while preserving other generated ExeFS edits.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaFashionUnlockWorkflowService fashionUnlockWorkflowService;

    public ZaFashionUnlockEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaFashionUnlockWorkflowService? fashionUnlockWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fashionUnlockWorkflowService = fashionUnlockWorkflowService ?? new ZaFashionUnlockWorkflowService();
    }

    public ZaFashionUnlockEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaFashionUnlockWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new ZaFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock needs its own edit session before staging.",
                expected: "A Fashion Unlock-only edit session"));
            return new ZaFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new ZaFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingInstallEdit(CreatePendingSources(project, isUninstall: false))],
        };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fashion Unlock install is staged for change-plan review."));
        return new ZaFashionUnlockEditResult(workflow, updatedSession, diagnostics);
    }

    public ZaFashionUnlockEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaFashionUnlockWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new ZaFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(edit.Domain, FashionUnlockEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock needs its own edit session before staging uninstall.",
                expected: "A Fashion Unlock-only edit session"));
            return new ZaFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new ZaFashionUnlockEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingUninstallEdit(CreatePendingSources(project, isUninstall: true))],
        };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fashion Unlock uninstall is staged for change-plan review."));
        return new ZaFashionUnlockEditResult(workflow, updatedSession, diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fashionUnlockWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaFashionUnlockWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new ZaEditSessionValidation(session, IsValid: false, diagnostics);
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

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        return ZaChangePlanSourceGuard.Capture(paths, session,
            () => CreateChangePlanCore(paths, session, outputMode), outputMode);
    }

    private ChangePlan CreateChangePlanCore(ProjectPaths paths, EditSession session, ZaOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (outputMode != ZaOutputMode.Standalone)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error,
                "Fashion Unlock edits exefs/main. Use standalone LayeredFS output for this editor.",
                expected: "Standalone LayeredFS output mode"));
        }
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
        var writes = new List<PlannedFileWrite>
        {
            new(
                ZaFashionUnlockWorkflowService.ExeFsMainPath,
                CreatePendingSources(project, isUninstall),
                File.Exists(targetPath),
                isUninstall ? UninstallReason : InstallReason),
        };
        var salon = ZaFashionUnlockSalon.Read(project);
        if (isUninstall ? salon.HasUnlockedStyles : !salon.IsUnlocked)
        {
            var sources = new List<ProjectFileReference>
            {
                new(ProjectFileLayer.Base, $"romfs/{ZaFashionUnlockSalon.VirtualPath}"),
            };
            var effective = new ProjectFileReference(salon.Effective.SourceLayer, salon.Effective.RelativePath);
            if (!sources.Contains(effective))
                sources.Add(effective);
            var target = ZaWorkflowFileSource.CreatePlannedWrite(paths, ZaFashionUnlockSalon.VirtualPath, sources);
            writes.Add(new PlannedFileWrite(target.TargetRelativePath, target.Sources, target.ReplacesExistingOutput,
                isUninstall ? "Restore the five original quest hairstyle conditions in the Hair Salon."
                    : "Unlock five quest hairstyles in both Hair Salon lineups."));
            var descriptor = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
            writes.Add(new PlannedFileWrite(descriptor.TargetRelativePath, descriptor.Sources,
                descriptor.ReplacesExistingOutput, "Update the Trinity descriptor for the Hair Salon override."));
        }
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Fashion Unlock change plan preview contains {writes.Count} target file(s)."));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        OutputApplyResult? transaction = null;
        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error,
                "Reviewed Fashion Unlock change plan is stale. Review the change plan again before applying."));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var context = new ZaOutputApplyContext(
            OutputReviewFingerprint.FromChangePlan(currentPlan),
            new OwnershipOwnerId("workflow.za.output"),
            [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, FashionUnlockEditDomain)])
        {
            HistoryDetails = OutputHistoryDetails.Capture(session.PendingEdits),
        };
        var executableContext = context with { OwnerId = new OwnershipOwnerId("workflow.za.fashion-unlock") };
        try
        {
            var uninstall = IsCanonicalUninstallIdentity(session.PendingEdits.Single());
            transaction = ZaWorkflowFileSource.ApplyStandaloneMixedBatch(
                paths,
                () =>
                {
                    projectWorkspaceService.ClearMemoryCache();
                    var project = projectWorkspaceService.Open(paths);
                    var source = ZaExeFsMainFileResolver.ResolveEffective(project)
                        ?? throw new FileNotFoundException("Fashion Unlock effective exefs/main is no longer available.");
                    var basePath = ZaExeFsMainFileResolver.ResolveBasePath(paths)
                        ?? throw new FileNotFoundException("Fashion Unlock base exefs/main is no longer available.");
                    var baseBytes = File.ReadAllBytes(basePath);
                    var effectiveBytes = File.ReadAllBytes(source.AbsolutePath);
                    EnsureVerifiedVanillaBase(ZaFashionUnlockMainPatcher.Analyze(baseBytes, paths.SelectedGame));
                    EnsureEditableEffectiveSource(ZaFashionUnlockMainPatcher.Analyze(effectiveBytes, paths.SelectedGame));
                    ZaFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseBytes, effectiveBytes);
                    var output = uninstall
                        ? ZaFashionUnlockMainPatcher.RestoreFromBase(effectiveBytes, baseBytes, paths.SelectedGame)
                        : ZaFashionUnlockMainPatcher.Apply(effectiveBytes, paths.SelectedGame);
                    byte[]? outputBytes = uninstall && ZaExeFsMainComparison.IsSemanticallyEquivalentToBase(output, baseBytes)
                        ? null : output;
                    var salon = ZaFashionUnlockSalon.Read(project);
                    var salonBytes = salon.CreateOutput(uninstall);
                    var deleteSalon = uninstall && salonBytes is not null && salon.CanDeleteOutput(paths, salonBytes);
                    IReadOnlyList<string> salonDeletes = deleteSalon ? [ZaFashionUnlockSalon.VirtualPath] : [];
                    var descriptorPreview = deleteSalon
                        ? ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(paths, [], salonDeletes)
                        : null;
                    var deleteDescriptor = descriptorPreview is not null
                        && ZaWorkflowFileSource.CanDeleteStandaloneDescriptor(paths, descriptorPreview, [], salonDeletes);
                    return new ZaStandaloneMixedBatch(
                        salonBytes is not null && !deleteSalon ? [new(ZaFashionUnlockSalon.VirtualPath, salonBytes)] : [],
                        salonDeletes,
                        [new ZaStandaloneOutputMutation(ZaFashionUnlockWorkflowService.ExeFsMainPath,
                            outputBytes, DeleteFallbackBytes: outputBytes is null ? baseBytes : null,
                            ApplyContext: executableContext)],
                        descriptorPreview,
                        deleteDescriptor);
                },
                applyContext: context,
                revalidateReviewedState: () => ChangePlanReview.Matches(reviewedPlan, CreateChangePlan(paths, session, outputMode)));
            writtenFiles.AddRange(currentPlan.Writes.Select(write =>
                new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath)));
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Info, uninstall
                ? "Restored the wardrobe and Hair Salon conditions managed by Fashion Unlock."
                : "Applied Fashion Unlock changes to the configured LayeredFS output root."));
        }
        catch (ZaOutputApplyNotCommittedException exception)
        {
            transaction = exception.Result;
            writtenFiles.Clear();
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error,
                $"Fashion Unlock output could not be committed atomically: {exception.Message}"));
        }
        catch (Exception exception) when (exception is InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException
            || exception is IOException ioException && !ZaEditSessionSupport.IsOutputSafetyException(ioException))
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error,
                $"Fashion Unlock output could not be prepared: {exception.Message}"));
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics, transaction);
    }

    private static bool CanStageInstall(
        OpenedProject project,
        ZaFashionUnlockWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != ZaWorkflowAvailability.Available)
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
                "Fashion Unlock cannot stage while exefs/main has an unsupported build or conflicting wardrobe instructions.",
                expected: "Verified vanilla or exact installed Fashion Unlock wardrobe instructions"));
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        ZaFashionUnlockWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != ZaWorkflowAvailability.Available)
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
                "Fashion Unlock uninstall requires exact installed wardrobe instructions in generated exefs/main.",
                expected: "Exact Fashion Unlock install in the configured output root"));
            return false;
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fashion Unlock uninstall can only restore a generated LayeredFS exefs/main.",
                file: ZaFashionUnlockWorkflowService.ExeFsMainPath,
                expected: "Fashion Unlock installed in the configured output root"));
            return false;
        }

        AddWorkflowErrors(workflow, diagnostics);
        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void AddWorkflowErrors(
        ZaFashionUnlockWorkflow workflow,
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
        if (actual.SequenceEqual(expected))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pending Fashion Unlock sources do not match the canonical selected-game base, effective main, and action fingerprint.",
            field: field,
            expected: "Canonical ordered unique Fashion Unlock source references"));
    }

    private static void EnsureVerifiedVanillaBase(ZaFashionUnlockAnalysis analysis)
    {
        if (analysis.Kind != ZaFashionUnlockInstallKind.NotInstalled
            || analysis.DetectedGame != ProjectGame.ZA
            || string.Equals(analysis.BuildId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Fashion Unlock requires the reviewed selected-game vanilla base exefs/main before apply or uninstall.");
        }
    }

    private static void EnsureEditableEffectiveSource(ZaFashionUnlockAnalysis analysis)
    {
        if (analysis.Kind is not (ZaFashionUnlockInstallKind.NotInstalled
            or ZaFashionUnlockInstallKind.Installed))
        {
            throw new InvalidDataException(
                "The reviewed effective exefs/main is no longer vanilla or an exact Fashion Unlock install.");
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

        var targetPath = ZaFashionUnlockWorkflowService.ResolveOutputPath(
            paths,
            ZaFashionUnlockWorkflowService.ExeFsMainPath);
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
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        OutputApplyResult? transaction = null)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics, transaction);
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

}
