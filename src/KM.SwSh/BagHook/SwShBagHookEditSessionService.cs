// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.CatchCap;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.BagHook;

public sealed class SwShBagHookEditSessionService
{
    public const string BagHookEditDomain = "workflow.bagHook";

    private const string InstallRecordId = "bag-hook-v2";
    private const string InstallField = "install";
    private const string UninstallRecordId = "bag-hook-v2-uninstall";
    private const string UninstallField = "uninstall";

    private readonly SwShBagHookWorkflowService bagHookWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShBagHookEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShBagHookWorkflowService? bagHookWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.bagHookWorkflowService = bagHookWorkflowService ?? new SwShBagHookWorkflowService();
    }

    public SwShBagHookEditResult StageInstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = bagHookWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook install needs its own edit session before staging.",
                expected: "A Bag Hook-only edit session"));
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageInstall(project, workflow, diagnostics))
        {
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit())
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Bag Hook V2 install is staged for change-plan review."));

        return new SwShBagHookEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShBagHookEditResult StageUninstall(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = bagHookWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall needs its own edit session before staging.",
                expected: "A Bag Hook-only edit session"));
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        if (!CanStageUninstall(project, workflow, diagnostics))
        {
            return new SwShBagHookEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal))
                .Append(CreateUninstallPendingEdit())
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Bag Hook V2 uninstall is staged for change-plan review."));

        return new SwShBagHookEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = bagHookWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage Bag Hook install or uninstall before validating.",
                expected: "Pending Bag Hook install or uninstall"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, BagHookEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit is not a Bag Hook workflow edit.",
                    expected: BagHookEditDomain));
                continue;
            }

            if (IsInstallEdit(edit))
            {
                CanStageInstall(project, workflow, diagnostics);
            }
            else if (IsUninstallEdit(edit))
            {
                CanStageUninstall(project, workflow, diagnostics);
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit is not a Bag Hook V2 install or uninstall.",
                    field: edit.Field,
                    expected: $"{InstallRecordId} or {UninstallRecordId}"));
            }
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Bag Hook workflow edit is valid for change-plan review."));
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

        var edit = session.PendingEdits.Single();
        var project = projectWorkspaceService.Open(paths);
        var writes = IsUninstallEdit(edit)
            ? CreateUninstallWrites(project, paths, diagnostics)
            : CreateInstallWrites(paths, diagnostics);

        if (writes.Count == 0)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Bag Hook change plan preview contains {writes.Count:N0} target file(s).")));

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
                "Reviewed Bag Hook change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Bag Hook change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var isUninstall = IsUninstallEdit(session.PendingEdits.Single());
        if (isUninstall)
        {
            ApplyUninstall(paths, currentPlan, writtenFiles, diagnostics);
        }
        else
        {
            ApplyInstall(paths, writtenFiles, diagnostics);
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private void ApplyInstall(
        ProjectPaths paths,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShBagHookWorkflowService.BagEventScriptPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook source or output target could not be resolved.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Readable source and writable LayeredFS target"));
            return;
        }

        try
        {
            var output = SwShBagHookAmxPatcher.InstallEmptyHook(File.ReadAllBytes(source.AbsolutePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShBagHookWorkflowService.BagEventScriptPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Installed Bag Hook V2 to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook source file could not be patched: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Supported Sword/Shield Bag-event AMX"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Bag Hook output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }
    }

    private static void ApplyUninstall(
        ProjectPaths paths,
        ChangePlan currentPlan,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var write in currentPlan.Writes)
        {
            var targetPath = ResolveOutputPath(paths, write.TargetRelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
            }

            if (!File.Exists(targetPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Bag Hook uninstall target '{write.TargetRelativePath}' no longer exists. Review the change plan again before applying.",
                    file: write.TargetRelativePath,
                    expected: "Existing reviewed LayeredFS output file"));
                continue;
            }

            try
            {
                var changed = string.Equals(write.TargetRelativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                    ? RestoreOrDeleteExeFsMain(paths, targetPath, write.TargetRelativePath, diagnostics)
                    : DeleteOutput(targetPath);
                if (changed)
                {
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath));
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Bag Hook uninstall target '{write.TargetRelativePath}' could not be restored: {exception.Message}",
                    file: write.TargetRelativePath,
                    expected: "Supported Sword/Shield LayeredFS output"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Bag Hook uninstall target '{write.TargetRelativePath}' could not be removed: {exception.Message}",
                    file: write.TargetRelativePath,
                    expected: "Writable output root"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Bag Hook uninstall target '{write.TargetRelativePath}' could not be removed: {exception.Message}",
                    file: write.TargetRelativePath,
                    expected: "Writable output root"));
            }
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Uninstalled Bag Hook V2 and removed dependent Royal Candy and Starting Items LayeredFS output."));
        }
    }

    private static bool CanStageInstall(
        OpenedProject project,
        SwShBagHookWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook install requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus == SwShBagHookWorkflowService.InstalledStatus)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook V2 is already installed.",
                expected: "Use Royal Candy or Starting Items to claim slots"));
            return false;
        }

        if (workflow.InstallStatus != "available" && workflow.InstallStatus != SwShBagHookWorkflowService.RepairableStatus)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook V2 cannot be installed or repaired while the Bag-event script is blocked, legacy, or conflicting.",
                expected: "Vanilla Bag-event script or repairable Bag Hook V2 output"));
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
        SwShBagHookWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus is not SwShBagHookWorkflowService.InstalledStatus
            and not SwShBagHookWorkflowService.RepairableStatus)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook V2 is not installed in the configured project output.",
                expected: "Installed Bag Hook V2"));
            return false;
        }

        var targetPath = SwShBagHookWorkflowService.ResolveOutputPath(project.Paths, SwShBagHookWorkflowService.BagEventScriptPath);
        if (targetPath is null || !File.Exists(targetPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall only removes LayeredFS output, but no Bag Hook output file was found.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Installed Bag Hook V2 in the configured output root"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<PlannedFileWrite> CreateInstallWrites(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null)
        {
            return Array.Empty<PlannedFileWrite>();
        }

        return
        [
            new PlannedFileWrite(
                SwShBagHookWorkflowService.BagEventScriptPath,
                [new ProjectFileReference(ProjectFileLayer.Base, SwShBagHookWorkflowService.BagEventScriptPath)],
                File.Exists(targetPath),
                "Install Bag Hook V2 with 20 disabled startup item grant slots. The hook grants no items by itself."),
        ];
    }

    private static IReadOnlyList<PlannedFileWrite> CreateUninstallWrites(
        OpenedProject project,
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var writes = new SortedDictionary<string, PlannedFileWrite>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in project.FileGraph.Entries.Where(entry => entry.LayeredFile is not null))
        {
            if (!string.Equals(entry.RelativePath, SwShBagHookWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
                && !IsRoyalCandyDependentOutput(project, entry))
            {
                continue;
            }

            var targetPath = ResolveOutputPath(paths, entry.RelativePath, diagnostics);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            writes[entry.RelativePath] = new PlannedFileWrite(
                entry.RelativePath,
                [new ProjectFileReference(ProjectFileLayer.Layered, entry.RelativePath)],
                ReplacesExistingOutput: true,
                string.Equals(entry.RelativePath, SwShBagHookWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
                    ? "Uninstall Bag Hook V2 and remove all dependent startup item grants."
                    : "Remove Royal Candy output because Royal Candy depends on Bag Hook slot 1.");
        }

        if (writes.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall did not find any reviewed LayeredFS output targets.",
                expected: "Bag Hook or dependent Royal Candy LayeredFS output"));
        }

        return writes.Values.ToArray();
    }

    private static PendingEdit CreatePendingEdit()
    {
        return new PendingEdit(
            BagHookEditDomain,
            "Stage Bag Hook install: 20 disabled startup item grant slots.",
            [new ProjectFileReference(ProjectFileLayer.Base, SwShBagHookWorkflowService.BagEventScriptPath)],
            RecordId: InstallRecordId,
            Field: InstallField,
            NewValue: "v2-empty");
    }

    private static PendingEdit CreateUninstallPendingEdit()
    {
        return new PendingEdit(
            BagHookEditDomain,
            "Stage Bag Hook uninstall: remove Bag Hook plus dependent Royal Candy and Starting Items outputs.",
            [new ProjectFileReference(ProjectFileLayer.Layered, SwShBagHookWorkflowService.BagEventScriptPath)],
            RecordId: UninstallRecordId,
            Field: UninstallField,
            NewValue: "remove-bag-hook-and-dependents");
    }

    private static bool IsInstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, InstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, InstallField, StringComparison.Ordinal);
    }

    private static bool IsUninstallEdit(PendingEdit edit)
    {
        return string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, UninstallField, StringComparison.Ordinal);
    }

    private static string? ResolveOutputPath(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook install requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShBagHookWorkflowService.ResolveOutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
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
                "Bag Hook workflow requires a configured output root.",
                file: targetRelativePath,
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook workflow target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook workflow target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static bool DeleteOutput(string targetPath)
    {
        File.Delete(targetPath);
        return true;
    }

    private static bool RestoreOrDeleteExeFsMain(
        ProjectPaths paths,
        string targetPath,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var basePath = ResolveBaseSourcePath(paths, targetRelativePath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Bag Hook uninstall could not resolve base exefs/main for Royal Candy restoration.",
                file: targetRelativePath,
                expected: "Readable base ExeFS main"));
            return false;
        }

        var baseBytes = File.ReadAllBytes(basePath);
        var restored = SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
            File.ReadAllBytes(targetPath),
            baseBytes);
        if (restored.SequenceEqual(baseBytes) || !ContainsIndependentExeFsHook(restored))
        {
            File.Delete(targetPath);
        }
        else
        {
            File.WriteAllBytes(targetPath, restored);
        }

        return true;
    }

    private static bool ContainsIndependentExeFsHook(byte[] mainBytes)
    {
        return SwShCatchCapMainPatcher.Analyze(mainBytes).Kind == SwShCatchCapInstallKind.InstalledV1;
    }

    private static bool IsRoyalCandyDependentOutput(OpenedProject project, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is null)
        {
            return false;
        }

        if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase))
        {
            return HasRoyalCandyExeFsSignature(project, entry);
        }

        return IsKnownRoyalCandyOutputPath(entry.RelativePath);
    }

    private static bool HasRoyalCandyExeFsSignature(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = CombineGraphPath(project.Paths.OutputRootPath, entry.RelativePath);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            return SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(File.ReadAllBytes(sourcePath)).Kind
                != SwShRoyalCandyExeFsSignatureKind.NotInstalled;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static bool IsKnownRoyalCandyOutputPath(string relativePath)
    {
        return string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.LegacyShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.NestDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.PlacementPath, StringComparison.OrdinalIgnoreCase)
            || IsItemMessageOutputPath(relativePath);
    }

    private static bool IsItemMessageOutputPath(string relativePath)
    {
        return TryParseMessageCommonFile(relativePath, out _, out var fileName)
            && (string.Equals(fileName, "iteminfo.dat", StringComparison.OrdinalIgnoreCase)
                || (fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryParseMessageCommonFile(string relativePath, out string language, out string fileName)
    {
        language = string.Empty;
        fileName = string.Empty;

        var parts = relativePath.Split('/');
        if (parts.Length != 6
            || !string.Equals(parts[0], "romfs", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[1], "bin", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "message", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[4], "common", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        language = parts[3];
        fileName = parts[5];
        return true;
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

        var sourcePath = SwShBagHookWorkflowService.ResolveSourcePath(project.Paths, graphEntry);
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
            Domain: BagHookEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
