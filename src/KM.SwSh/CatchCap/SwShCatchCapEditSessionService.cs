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

namespace KM.SwSh.CatchCap;

public sealed class SwShCatchCapEditSessionService
{
    public const string CatchCapEditDomain = "workflow.catchCap";

    private const string RecordId = "catch-cap-v1";
    private const string UninstallRecordId = "catch-cap-v1-uninstall";
    private const string CapsField = "caps";
    private const string UninstallField = "uninstall";
    private const string StageCapsSummary = "Stage Catch Cap Editor values for badge counts 0-7 and the display/runtime hook; eight badges remains Lv.100.";
    private const string StageUninstallSummary = "Stage Catch Cap Editor uninstall.";
    private const string ApplyCapsReason = "Apply Catch Cap Editor display/runtime hook and reviewed badge cap values 0-7 to exefs/main; eight badges remains Lv.100.";
    private const string UninstallReason = "Uninstall Catch Cap Editor from exefs/main while preserving unrelated supported ExeFS edits.";

    private readonly SwShCatchCapWorkflowService catchCapWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShCatchCapEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShCatchCapWorkflowService? catchCapWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.catchCapWorkflowService = catchCapWorkflowService ?? new SwShCatchCapWorkflowService();
    }

    internal SwShCatchCapEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShCatchCapWorkflowService? catchCapWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, catchCapWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    internal SwShCatchCapEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShCatchCapWorkflowService? catchCapWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, catchCapWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    public SwShCatchCapEditResult StageCaps(
        ProjectPaths paths,
        IReadOnlyList<SwShCatchCapSelection> caps,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = catchCapWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShCatchCapWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShCatchCapEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, CatchCapEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor needs its own edit session before staging.",
                expected: "A Catch Cap-only edit session"));
            return new SwShCatchCapEditResult(workflow, currentSession, diagnostics);
        }

        var normalizedCaps = NormalizeCaps(workflow, caps, diagnostics);
        if (!CanStage(project, workflow, diagnostics) || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShCatchCapEditResult(workflow, currentSession, diagnostics);
        }

        var payload = SerializeCaps(normalizedCaps);
        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingEdit(payload, CreatePendingSources(project, payload, isUninstall: false))],
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Catch Cap Editor values are staged for change-plan review."));

        return new SwShCatchCapEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShCatchCapEditResult StageUninstall(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = catchCapWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShCatchCapWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShCatchCapEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, CatchCapEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor needs its own edit session before staging uninstall.",
                expected: "A Catch Cap-only edit session"));
            return new SwShCatchCapEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, paths, diagnostics))
        {
            return new SwShCatchCapEditResult(workflow, currentSession, diagnostics);
        }

        const string payload = "true";
        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingUninstallEdit(CreatePendingSources(project, payload, isUninstall: true))],
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Catch Cap Editor uninstall is staged for change-plan review."));

        return new SwShCatchCapEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = catchCapWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShCatchCapWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.AddRange(workflow.Summary.Diagnostics);
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage Catch Cap Editor values or uninstall before validating."
                    : "Catch Cap Editor requires exactly one canonical pending edit.",
                expected: "Exactly one pending Catch Cap values or uninstall edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (IsCanonicalUninstallIdentity(edit))
            {
                ValidateCanonicalUninstallEdit(project, edit, diagnostics);
            }
            else if (IsCanonicalCapsIdentity(edit))
            {
                ValidateCanonicalCapsEdit(project, edit, diagnostics);
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the canonical Catch Cap values or uninstall record.",
                    field: edit.Field,
                    expected: $"{CatchCapEditDomain}/{RecordId}/{CapsField} or {CatchCapEditDomain}/{UninstallRecordId}/{UninstallField}"));
            }
        }

        if (session.PendingEdits.Count == 1 && IsCanonicalUninstallIdentity(session.PendingEdits[0]))
        {
            CanStageUninstall(project, workflow, paths, diagnostics);
        }
        else
        {
            CanStage(project, workflow, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Catch Cap Editor change is valid for change-plan review."));
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
        var payload = pendingEdit.NewValue ?? string.Empty;
        var sources = CreatePendingSources(project, payload, isUninstall);
        var writes = new[]
        {
            new PlannedFileWrite(
                SwShCatchCapWorkflowService.ExeFsMainPath,
                sources,
                File.Exists(targetPath),
                isUninstall ? UninstallReason : ApplyCapsReason),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Catch Cap Editor change plan preview contains {writes.Length:N0} target file(s).")));

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
                "Reviewed Catch Cap Editor change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Catch Cap Editor change plan"));
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
                "Catch Cap Editor sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Catch Cap Editor change plan"));
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
        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor prepared apply requires exactly one canonical pending edit.",
                expected: "Exactly one pending Catch Cap values or uninstall edit"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var pendingEdit = session.PendingEdits[0];
        if (IsCanonicalUninstallIdentity(pendingEdit))
        {
            ApplyPreparedUninstall(paths, writtenFiles, diagnostics);
        }
        else if (IsCanonicalCapsIdentity(pendingEdit))
        {
            ApplyPreparedCaps(paths, pendingEdit, writtenFiles, diagnostics);
        }
        else
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor prepared apply received a noncanonical pending edit.",
                expected: "Canonical Catch Cap values or uninstall edit"));
        }

        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private void ApplyPreparedCaps(
        ProjectPaths paths,
        PendingEdit pendingEdit,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var caps = ParsePendingCaps(pendingEdit.NewValue, diagnostics);
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShCatchCapWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = SwShCatchCapWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShCatchCapWorkflowService.ExeFsMainPath);
        if (source is null || targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor source, vanilla base, or output target could not be resolved.",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Readable reviewed source and vanilla base with writable LayeredFS target"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var baseAnalysis = SwShCatchCapMainPatcher.Analyze(baseBytes, paths.SelectedGame);
            EnsureVerifiedVanillaBase(baseAnalysis);
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var sourceAnalysis = SwShCatchCapMainPatcher.Analyze(sourceBytes, paths.SelectedGame);
            EnsureEditableEffectiveSource(sourceAnalysis);

            var output = SwShCatchCapMainPatcher.Apply(sourceBytes, caps, paths.SelectedGame);
            WriteOutputAtomically(
                targetPath,
                output,
                roundTrip => VerifyInstalledOutput(baseAnalysis, caps, roundTrip, paths.SelectedGame));
            writtenFiles.Add(new ProjectFileReference(
                ProjectFileLayer.Generated,
                SwShCatchCapWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Catch Cap Editor changes to the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor verified output could not be prepared: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Reviewed selected-game source, exact badge payload, and writable output"));
        }
    }

    private static void ApplyPreparedUninstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = SwShCatchCapWorkflowService.ResolveBaseSourcePath(
            paths,
            SwShCatchCapWorkflowService.ExeFsMainPath);
        if (targetPath is null || basePath is null || !File.Exists(basePath) || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor uninstall could not resolve the reviewed generated and vanilla base exefs/main files.",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Existing reviewed LayeredFS exefs/main and readable vanilla base"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var baseAnalysis = SwShCatchCapMainPatcher.Analyze(baseBytes, paths.SelectedGame);
            EnsureVerifiedVanillaBase(baseAnalysis);
            var currentBytes = File.ReadAllBytes(targetPath);
            var currentAnalysis = SwShCatchCapMainPatcher.Analyze(currentBytes, paths.SelectedGame);
            if (currentAnalysis.Kind != SwShCatchCapInstallKind.InstalledV1)
            {
                throw new InvalidDataException("The reviewed effective main no longer contains an exact KM Catch Cap Hook.");
            }

            var restored = SwShCatchCapMainPatcher.RestoreFromBase(
                currentBytes,
                baseBytes,
                paths.SelectedGame);
            VerifyUninstalledOutput(baseAnalysis, restored, paths.SelectedGame);
            if (SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(restored, baseBytes))
            {
                File.Delete(targetPath);
                if (File.Exists(targetPath))
                {
                    throw new IOException("Catch Cap Editor uninstall target still exists after verified deletion.");
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
                SwShCatchCapWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Catch Cap Editor from the configured LayeredFS output root."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor uninstall could not prepare a verified restoration: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Exact installed KM Catch Cap Hook, vanilla base, and writable output"));
        }
    }

    private static IReadOnlyList<int> NormalizeCaps(
        SwShCatchCapWorkflow workflow,
        IReadOnlyList<SwShCatchCapSelection> selections,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var initialErrorCount = CountErrors(diagnostics);
        var requested = new Dictionary<int, int>();
        foreach (var selection in selections)
        {
            if (selection.BadgeCount is < 0 or >= SwShCatchCapMainPatcher.CapCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch Cap badge count {selection.BadgeCount} is not available.",
                    field: CapsField,
                    expected: "Badge counts 0-8"));
                continue;
            }

            if (requested.ContainsKey(selection.BadgeCount))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch Cap badge count {selection.BadgeCount} was supplied more than once.",
                    field: CapsField,
                    expected: "One cap value per badge count"));
                continue;
            }

            requested.Add(selection.BadgeCount, selection.LevelCap);
        }

        var definitions = workflow.Caps.OrderBy(cap => cap.BadgeCount).ToArray();
        var normalized = new List<int>(SwShCatchCapMainPatcher.CapCount);
        foreach (var definition in definitions)
        {
            var levelCap = requested.TryGetValue(definition.BadgeCount, out var requestedCap)
                ? requestedCap
                : definition.LevelCap;

            if (definition.BadgeCount == SwShCatchCapMainPatcher.FinalBadgeCount
                && levelCap != SwShCatchCapMainPatcher.FinalBadgeCap)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch cap for {definition.Label} is fixed at level {SwShCatchCapMainPatcher.FinalBadgeCap}; the game treats eight badges as catch any level.",
                    field: CapsField,
                    expected: "Level 100 for eight badges"));
            }
            else if (levelCap < definition.MinimumLevelCap || levelCap > definition.MaximumLevelCap)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch cap for {definition.Label} must be between {definition.MinimumLevelCap} and {definition.MaximumLevelCap}.",
                    field: CapsField,
                    expected: "Level cap between 1 and 100"));
            }

            normalized.Add(levelCap);
        }

        ValidateCapOrder(normalized, definitions.Select(definition => definition.Label).ToArray(), diagnostics);

        return CountErrors(diagnostics) > initialErrorCount
            ? Array.Empty<int>()
            : normalized;
    }

    private static IReadOnlyList<int> ParsePendingCaps(string? value, ICollection<ValidationDiagnostic> diagnostics)
    {
        var initialErrorCount = CountErrors(diagnostics);
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap pending edit has no cap payload.",
                field: CapsField,
                expected: "badge=level;..."));
            return Array.Empty<int>();
        }

        var values = new int[SwShCatchCapMainPatcher.CapCount];
        var seen = new bool[SwShCatchCapMainPatcher.CapCount];
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2
                || !int.TryParse(keyValue[0], NumberStyles.None, CultureInfo.InvariantCulture, out var badgeCount)
                || !int.TryParse(keyValue[1], NumberStyles.None, CultureInfo.InvariantCulture, out var levelCap)
                || badgeCount is < 0 or >= SwShCatchCapMainPatcher.CapCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch Cap entry '{part}' is not valid.",
                    field: CapsField,
                    expected: "badge=level"));
                continue;
            }

            if (seen[badgeCount])
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch Cap badge count {badgeCount} appears more than once in the pending payload.",
                    field: CapsField,
                    expected: "Unique badge counts 0-8 in ascending order"));
                continue;
            }

            values[badgeCount] = levelCap;
            seen[badgeCount] = true;
        }

        for (var index = 0; index < seen.Length; index++)
        {
            if (!seen[index])
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch Cap badge count {index} is missing.",
                    field: CapsField,
                    expected: "Nine badge caps"));
            }

            if (seen[index] &&
                (values[index] is < SwShCatchCapMainPatcher.MinimumCap or > SwShCatchCapMainPatcher.MaximumCap))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch Cap badge count {index} must be between {SwShCatchCapMainPatcher.MinimumCap} and {SwShCatchCapMainPatcher.MaximumCap}.",
                    field: CapsField,
                    expected: "Level cap between 1 and 100"));
            }

            if (seen[index]
                && index == SwShCatchCapMainPatcher.FinalBadgeCount
                && values[index] != SwShCatchCapMainPatcher.FinalBadgeCap)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Catch Cap badge count {SwShCatchCapMainPatcher.FinalBadgeCount} is fixed at level {SwShCatchCapMainPatcher.FinalBadgeCap}; the game treats eight badges as catch any level.",
                    field: CapsField,
                    expected: "Level 100 for eight badges"));
            }
        }

        if (seen.All(isSeen => isSeen))
        {
            ValidateCapOrder(values, labels: null, diagnostics);
        }

        return CountErrors(diagnostics) > initialErrorCount
            ? Array.Empty<int>()
            : values;
    }

    private static void ValidateCapOrder(
        IReadOnlyList<int> caps,
        IReadOnlyList<string>? labels,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (caps.Count != SwShCatchCapMainPatcher.CapCount)
        {
            return;
        }

        for (var index = 1; index < caps.Count; index++)
        {
            if (caps[index] >= caps[index - 1])
            {
                continue;
            }

            var label = labels is not null && index < labels.Count
                ? labels[index]
                : string.Create(CultureInfo.InvariantCulture, $"badge count {index}");
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch cap for {label} must be the same as or higher than the previous badge level (level {caps[index - 1]}).",
                field: CapsField,
                expected: "Each badge level must be the same or higher than the previous badge level"));
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShCatchCapWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus is "blocked" or "foreign")
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor cannot stage while exefs/main has a foreign or conflicting catch-cap patch.",
                expected: "Vanilla catch-cap formula tail or installed KM Catch Cap Hook"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool CanStageUninstall(
        OpenedProject project,
        SwShCatchCapWorkflow workflow,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor uninstall requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (!string.Equals(workflow.InstallStatus, "installed", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor is not installed in the current project output.",
                expected: "Installed KM Catch Cap Hook"));
            return false;
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor uninstall can only remove a generated LayeredFS exefs/main.",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Catch Cap installed in the configured output root"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static PendingEdit CreatePendingEdit(
        string payload,
        IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            CatchCapEditDomain,
            StageCapsSummary,
            sources,
            RecordId,
            CapsField,
            payload);
    }

    private static PendingEdit CreatePendingUninstallEdit(IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            CatchCapEditDomain,
            StageUninstallSummary,
            sources,
            UninstallRecordId,
            UninstallField,
            "true");
    }

    private static bool IsCanonicalCapsIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, CatchCapEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, CapsField, StringComparison.Ordinal);
    }

    private static bool IsCanonicalUninstallIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, CatchCapEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, UninstallField, StringComparison.Ordinal);
    }

    private void ValidateCanonicalCapsEdit(
        OpenedProject project,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Summary, StageCapsSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Catch Cap values edit does not have the canonical staged summary.",
                field: CapsField,
                expected: StageCapsSummary));
        }

        var caps = ParsePendingCaps(edit.NewValue, diagnostics);
        var canonicalPayload = SerializeCaps(caps);
        if (caps.Count == SwShCatchCapMainPatcher.CapCount
            && !string.Equals(edit.NewValue, canonicalPayload, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Catch Cap values are not in canonical ordered badge format.",
                field: CapsField,
                expected: "Exactly one ordered badge=level entry for badge counts 0-8"));
        }

        ValidateCanonicalSources(
            edit.Sources,
            CreatePendingSources(project, edit.NewValue ?? string.Empty, isUninstall: false),
            CapsField,
            diagnostics);
    }

    private void ValidateCanonicalUninstallEdit(
        OpenedProject project,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Summary, StageUninstallSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Catch Cap uninstall edit does not have the canonical staged summary.",
                field: UninstallField,
                expected: StageUninstallSummary));
        }

        if (!string.Equals(edit.NewValue, "true", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Catch Cap uninstall payload must be exactly true.",
                field: UninstallField,
                expected: "true"));
        }

        ValidateCanonicalSources(
            edit.Sources,
            CreatePendingSources(project, "true", isUninstall: true),
            UninstallField,
            diagnostics);
    }

    private IReadOnlyList<ProjectFileReference> CreatePendingSources(
        OpenedProject project,
        string payload,
        bool isUninstall)
    {
        return catchCapWorkflowService
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
        var action = isUninstall ? "uninstall" : "caps";
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/catch-cap/{action}/{hash}");
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
            "Pending Catch Cap edit sources do not match the canonical selected-game base, effective main, and payload fingerprint.",
            field: field,
            expected: "Canonical ordered unique Catch Cap source references"));
    }

    private static string SerializeCaps(IReadOnlyList<int> caps)
    {
        return string.Join(
            ';',
            caps.Select((cap, badgeCount) => string.Create(
                CultureInfo.InvariantCulture,
                $"{badgeCount}={cap}")));
    }

    private static void EnsureVerifiedVanillaBase(SwShCatchCapAnalysis analysis)
    {
        if (analysis.Kind != SwShCatchCapInstallKind.NotInstalled
            || analysis.DetectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.Equals(analysis.BuildId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Catch Cap Editor requires the reviewed selected-game vanilla base exefs/main before apply or uninstall.");
        }
    }

    private static void EnsureEditableEffectiveSource(SwShCatchCapAnalysis analysis)
    {
        if (analysis.Kind is not (SwShCatchCapInstallKind.NotInstalled or SwShCatchCapInstallKind.InstalledV1))
        {
            throw new InvalidDataException(
                "The reviewed effective exefs/main is no longer a vanilla or exact KM Catch Cap source.");
        }
    }

    private static void VerifyInstalledOutput(
        SwShCatchCapAnalysis baseAnalysis,
        IReadOnlyList<int> expectedCaps,
        byte[] output,
        ProjectGame? selectedGame)
    {
        var outputAnalysis = SwShCatchCapMainPatcher.Analyze(output, selectedGame);
        if (outputAnalysis.Kind != SwShCatchCapInstallKind.InstalledV1)
        {
            throw new InvalidDataException("Patched exefs/main did not round-trip as an exact KM Catch Cap Hook.");
        }

        if (!outputAnalysis.Caps.SequenceEqual(expectedCaps.Select(cap => checked((byte)cap))))
        {
            throw new InvalidDataException("Patched exefs/main did not round-trip with the reviewed badge cap values.");
        }

        VerifyExecutableIdentity(baseAnalysis, outputAnalysis, selectedGame);
    }

    private static void VerifyUninstalledOutput(
        SwShCatchCapAnalysis baseAnalysis,
        byte[] output,
        ProjectGame? selectedGame)
    {
        var outputAnalysis = SwShCatchCapMainPatcher.Analyze(output, selectedGame);
        if (outputAnalysis.Kind != SwShCatchCapInstallKind.NotInstalled)
        {
            throw new InvalidDataException("Restored exefs/main did not round-trip with Catch Cap Editor uninstalled.");
        }

        VerifyExecutableIdentity(baseAnalysis, outputAnalysis, selectedGame);
    }

    private static void VerifyExecutableIdentity(
        SwShCatchCapAnalysis baseAnalysis,
        SwShCatchCapAnalysis outputAnalysis,
        ProjectGame? selectedGame)
    {
        if (!string.Equals(baseAnalysis.BuildId, outputAnalysis.BuildId, StringComparison.Ordinal)
            || baseAnalysis.DetectedGame != outputAnalysis.DetectedGame
            || outputAnalysis.DetectedGame != selectedGame
            || !string.Equals(
                baseAnalysis.DisplayHookOffsetHex,
                outputAnalysis.DisplayHookOffsetHex,
                StringComparison.Ordinal)
            || !string.Equals(
                baseAnalysis.RuntimeHookOffsetHex,
                outputAnalysis.RuntimeHookOffsetHex,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Catch Cap Editor output executable identity changed during apply verification.");
        }
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Catch Cap Editor output directory could not be resolved.");
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
                throw new IOException("Catch Cap Editor temporary output did not round-trip byte-for-byte.");
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

    private static int CountErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string? ResolveOutputPath(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShCatchCapWorkflowService.ResolveOutputPath(paths, SwShCatchCapWorkflowService.ExeFsMainPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor target must stay inside the configured output root.",
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

        var sourcePath = SwShCatchCapWorkflowService.ResolveSourcePath(project.Paths, graphEntry);
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
            Domain: CatchCapEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
