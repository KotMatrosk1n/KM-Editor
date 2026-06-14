// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.IvScreen;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.CatchCap;

public sealed class SwShCatchCapEditSessionService
{
    public const string CatchCapEditDomain = "workflow.catchCap";

    private const string RecordId = "catch-cap-v1";
    private const string UninstallRecordId = "catch-cap-v1-uninstall";
    private const string CapsField = "caps";
    private const string UninstallField = "uninstall";

    private readonly SwShCatchCapWorkflowService catchCapWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShCatchCapEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShCatchCapWorkflowService? catchCapWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.catchCapWorkflowService = catchCapWorkflowService ?? new SwShCatchCapWorkflowService();
    }

    public SwShCatchCapEditResult StageCaps(
        ProjectPaths paths,
        IReadOnlyList<SwShCatchCapSelection> caps,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(caps);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = catchCapWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

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

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, CatchCapEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(normalizedCaps))
                .ToArray(),
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
        var project = projectWorkspaceService.Open(paths);
        var workflow = catchCapWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

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

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, CatchCapEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingUninstallEdit())
                .ToArray(),
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

        var project = projectWorkspaceService.Open(paths);
        var workflow = catchCapWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Catch Cap Editor values or uninstall before validating.",
                expected: "Pending Catch Cap values or uninstall"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, CatchCapEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Catch Cap Editor.",
                    expected: CatchCapEditDomain));
                continue;
            }

            if (IsUninstallEdit(edit))
            {
                CanStageUninstall(project, workflow, paths, diagnostics);
                continue;
            }

            if (!string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending Catch Cap edit '{edit.RecordId}' is not supported.",
                    expected: "Catch Cap values or uninstall"));
                continue;
            }

            _ = ParsePendingCaps(edit.NewValue, diagnostics);
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

        var isUninstall = IsUninstallSession(session);
        var writes = new[]
        {
            new PlannedFileWrite(
                SwShCatchCapWorkflowService.ExeFsMainPath,
                isUninstall
                    ? [
                        new ProjectFileReference(ProjectFileLayer.Generated, SwShCatchCapWorkflowService.ExeFsMainPath),
                        new ProjectFileReference(ProjectFileLayer.Base, SwShCatchCapWorkflowService.ExeFsMainPath),
                    ]
                    : [new ProjectFileReference(ProjectFileLayer.Base, SwShCatchCapWorkflowService.ExeFsMainPath)],
                File.Exists(targetPath),
                isUninstall
                    ? "Uninstall Catch Cap Editor from exefs/main while preserving Royal Candy ExeFS bytes when present."
                    : "Apply Catch Cap Editor display/runtime hook and badge cap values 0-7 to exefs/main; eight badges remains Lv.100."),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Catch Cap Editor change plan preview contains {writes.Length:N0} target file(s).")));

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
                "Reviewed Catch Cap Editor change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Catch Cap Editor change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var pendingEdit = session.PendingEdits.Single();
        if (IsUninstallEdit(pendingEdit))
        {
            ApplyUninstall(paths, currentPlan, writtenFiles, diagnostics);
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var caps = ParsePendingCaps(pendingEdit.NewValue, diagnostics);
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShCatchCapWorkflowService.ExeFsMainPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor source or output target could not be resolved.",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Readable source and writable LayeredFS target"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var output = SwShCatchCapMainPatcher.Apply(
                File.ReadAllBytes(source.AbsolutePath),
                caps,
                paths.SelectedGame);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShCatchCapWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Catch Cap Editor changes to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor source file could not be patched: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor output file could not be written: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor output file could not be written: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void ApplyUninstall(
        ProjectPaths paths,
        ChangePlan currentPlan,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        var basePath = ResolveBaseSourcePath(paths, SwShCatchCapWorkflowService.ExeFsMainPath);
        if (targetPath is null || basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor uninstall could not resolve base exefs/main for restoration.",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Readable base ExeFS main"));
            return;
        }

        if (!File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Catch Cap Editor uninstall target no longer exists. Review the change plan again before applying.",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Existing reviewed LayeredFS exefs/main"));
            return;
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var restored = SwShCatchCapMainPatcher.RestoreFromBase(
                File.ReadAllBytes(targetPath),
                baseBytes,
                paths.SelectedGame);
            if (restored.SequenceEqual(baseBytes) || !ContainsIndependentExeFsHook(restored))
            {
                File.Delete(targetPath);
            }
            else
            {
                File.WriteAllBytes(targetPath, restored);
            }

            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShCatchCapWorkflowService.ExeFsMainPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Catch Cap Editor from the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor uninstall could not restore exefs/main: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Supported Sword/Shield exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor uninstall could not update output: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Catch Cap Editor uninstall could not update output: {exception.Message}",
                file: SwShCatchCapWorkflowService.ExeFsMainPath,
                expected: "Writable output root"));
        }
    }

    private static IReadOnlyList<int> NormalizeCaps(
        SwShCatchCapWorkflow workflow,
        IReadOnlyList<SwShCatchCapSelection> selections,
        ICollection<ValidationDiagnostic> diagnostics)
    {
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

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? Array.Empty<int>()
            : normalized;
    }

    private static IReadOnlyList<int> ParsePendingCaps(string? value, ICollection<ValidationDiagnostic> diagnostics)
    {
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

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
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

    private static PendingEdit CreatePendingEdit(IReadOnlyList<int> caps)
    {
        var value = string.Join(
            ';',
            caps.Select((cap, badgeCount) => string.Create(CultureInfo.InvariantCulture, $"{badgeCount}={cap}")));
        return new PendingEdit(
            CatchCapEditDomain,
            "Stage Catch Cap Editor values for badge counts 0-7 and the display/runtime hook; eight badges remains Lv.100.",
            [new ProjectFileReference(ProjectFileLayer.Base, SwShCatchCapWorkflowService.ExeFsMainPath)],
            RecordId,
            CapsField,
            value);
    }

    private static PendingEdit CreatePendingUninstallEdit()
    {
        return new PendingEdit(
            CatchCapEditDomain,
            "Stage Catch Cap Editor uninstall.",
            [
                new ProjectFileReference(ProjectFileLayer.Generated, SwShCatchCapWorkflowService.ExeFsMainPath),
                new ProjectFileReference(ProjectFileLayer.Base, SwShCatchCapWorkflowService.ExeFsMainPath),
            ],
            UninstallRecordId,
            UninstallField,
            "true");
    }

    private static bool IsUninstallSession(EditSession session)
    {
        return session.PendingEdits.Count == 1 && IsUninstallEdit(session.PendingEdits[0]);
    }

    private static bool IsUninstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal);
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

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (targetRelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, targetRelativePath["romfs/".Length..]);
        }

        if (targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool ContainsIndependentExeFsHook(byte[] mainBytes)
    {
        return SwShIndependentExeFsHookDetector.ContainsAny(mainBytes);
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
