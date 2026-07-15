// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Raids;

public sealed class SwShRaidRewardsEditSessionService
{
    internal const string RaidRewardsEditDomain = "workflow.raidRewards";
    internal const string RaidBonusRewardsEditDomain = "workflow.raidBonusRewards";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShRaidRewardsWorkflowService raidRewardsWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShRaidRewardsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRaidRewardsWorkflowService? raidRewardsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShRaidRewardsEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRaidRewardsWorkflowService? raidRewardsWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShRaidRewardsEditResult UpdateRewardField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        return UpdateRewardFields(
            paths,
            session,
            [new SwShRaidRewardFieldUpdate(tableId, slot, field, value)]);
    }

    public SwShRaidRewardsEditResult UpdateRewardFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShRaidRewardFieldUpdate?>? updates)
    {
        return UpdateRewardFields(
            paths,
            session,
            updates,
            SwShRaidRewardWorkflowKind.Drop,
            RaidRewardsEditDomain);
    }

    public SwShRaidRewardsEditResult UpdateBonusRewardField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        return UpdateRewardFields(
            paths,
            session,
            [new SwShRaidRewardFieldUpdate(tableId, slot, field, value)],
            SwShRaidRewardWorkflowKind.Bonus,
            RaidBonusRewardsEditDomain);
    }

    public SwShRaidRewardsEditResult UpdateBonusRewardFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShRaidRewardFieldUpdate?>? updates)
    {
        return UpdateRewardFields(
            paths,
            session,
            updates,
            SwShRaidRewardWorkflowKind.Bonus,
            RaidBonusRewardsEditDomain);
    }

    private SwShRaidRewardsEditResult UpdateRewardFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShRaidRewardFieldUpdate?>? updates,
        SwShRaidRewardWorkflowKind kind,
        string editDomain)
    {
        ArgumentNullException.ThrowIfNull(paths);

        projectWorkspaceService.ClearMemoryCache();
        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = raidRewardsWorkflowService.Load(project, kind);
        var originalWorkflow = OverlayPendingEdits(loadedWorkflow, originalSession.PendingEdits, editDomain);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditRaidRewards(project, loadedWorkflow, diagnostics, editDomain))
        {
            return new SwShRaidRewardsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates is null || updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Update at least one {GetWorkflowLabel(editDomain)} field.",
                field: "updates",
                expected: $"One or more {GetWorkflowLabel(editDomain)} field updates"));
            return new SwShRaidRewardsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var workingSession = originalSession;
        var effectiveWorkflow = originalWorkflow;
        var seenUpdates = new HashSet<(string TableId, int Slot, string Field)>();
        foreach (var update in updates)
        {
            if (update is null
                || update.TableId is null
                || update.Field is null
                || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    $"{GetWorkflowLabel(editDomain)} update fields are required.",
                    field: "updates",
                    expected: "Non-null canonical raid reward update"));
                break;
            }

            if (!string.Equals(update.Field, update.Field.Trim(), StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    "Raid reward field must use canonical text without surrounding whitespace.",
                    field: "field",
                    expected: update.Field.Trim()));
                break;
            }

            if (!seenUpdates.Add((update.TableId, update.Slot, update.Field)))
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    "Raid reward batch contains the same field more than once.",
                    field: update.Field,
                    expected: "One value per raid reward field"));
                break;
            }

            var table = effectiveWorkflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, update.TableId, StringComparison.Ordinal));
            var sourceTable = loadedWorkflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, update.TableId, StringComparison.Ordinal));
            if (table is null || sourceTable is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    $"Raid reward table '{update.TableId}' is not present in the current source.",
                    field: "tableId",
                    expected: "Current signed raid reward table"));
                break;
            }

            var reward = table.Rewards.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            var sourceReward = sourceTable.Rewards.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            if (reward is null || sourceReward is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    $"Raid reward table '{table.SourceTableHash}' does not have reward slot {update.Slot}.",
                    field: "slot",
                    expected: "Existing one-based raid reward slot"));
                break;
            }

            var sourceValue = GetRewardFieldValue(sourceReward, update.Field);
            if (sourceValue is not null
                && string.Equals(sourceValue, update.Value, StringComparison.Ordinal))
            {
                workingSession = RemovePendingRaidRewardEdit(
                    workingSession,
                    editDomain,
                    SwShRaidRewardsWorkflowService.CreateRewardRecordId(table.TableId, reward.Slot),
                    update.Field);
                effectiveWorkflow = OverlayPendingEdits(
                    loadedWorkflow,
                    workingSession.PendingEdits,
                    editDomain);
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                loadedWorkflow,
                table,
                reward,
                update.Field,
                update.Value,
                editDomain,
                diagnostics);
            if (pendingEdit is null)
            {
                break;
            }

            pendingEdit = AddItemValidationSources(project, pendingEdit);
            workingSession = ReplacePendingRaidRewardEdit(workingSession, pendingEdit);
            if (GetRewardFieldValue(sourceReward, pendingEdit.Field) == pendingEdit.NewValue)
            {
                workingSession = RemovePendingRaidRewardEdit(workingSession, pendingEdit);
            }

            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, workingSession.PendingEdits, editDomain);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRaidRewardsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        ValidateLoadedSession(project, loadedWorkflow, workingSession, diagnostics, editDomain, addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRaidRewardsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShRaidRewardsEditResult(
            OverlayPendingEdits(loadedWorkflow, workingSession.PendingEdits, editDomain),
            workingSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var editDomain = GetDirectEditDomain(session);
        if (editDomain is null)
        {
            return new SwShEditSessionValidation(
                session,
                IsValid: false,
                [CreateDirectDomainOwnershipDiagnostic(session)]);
        }

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = raidRewardsWorkflowService.Load(project, GetWorkflowKind(editDomain));
        var diagnostics = new List<ValidationDiagnostic>();

        if (CanEditRaidRewards(project, workflow, diagnostics, editDomain))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, editDomain, addSuccessDiagnostic: true);
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

        var editDomain = GetDirectEditDomain(session);
        if (editDomain is null)
        {
            return new ChangePlan(
                session.Id,
                Array.Empty<PlannedFileWrite>(),
                [CreateDirectDomainOwnershipDiagnostic(session)]);
        }

        projectWorkspaceService.ClearMemoryCache();
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        var workflowLabel = GetWorkflowLabel(editDomain);
        var rewardEdits = GetRewardEdits(session, editDomain).ToArray();

        if (rewardEdits.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Create a pending {workflowLabel} edit before reviewing a change plan.",
                expected: "Pending raid reward edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShRaidRewardsWorkflowService.ResolveNestDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{workflowLabel} change plan could not resolve the source nest archive.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath, editDomain, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            dataSource.GraphEntry.RelativePath,
            rewardEdits
                .SelectMany(edit => edit.Sources)
                .Append(new ProjectFileReference(
                    GetSourceLayer(dataSource.GraphEntry),
                    dataSource.GraphEntry.RelativePath))
                .Distinct()
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            File.Exists(targetPath),
            CreatePlanReason(rewardEdits, workflowLabel));

        diagnostics.Add(CreateDiagnostic(
            editDomain,
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

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
        var editDomain = GetDirectEditDomain(session);
        if (editDomain is null)
        {
            var ownershipDiagnostic = CreateDirectDomainOwnershipDiagnostic(session);
            var rejectedPlan = new ChangePlan(
                session.Id,
                Array.Empty<PlannedFileWrite>(),
                [ownershipDiagnostic]);
            return CreateApplyResult(
                applyId,
                appliedAt,
                rejectedPlan,
                Array.Empty<ProjectFileReference>(),
                [ownershipDiagnostic]);
        }

        var workflowLabel = GetWorkflowLabel(editDomain);
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: $"Current reviewed {workflowLabel} change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShRaidRewardsWorkflowService.ResolveNestDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{workflowLabel} apply could not resolve the source nest archive.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath, editDomain, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        byte[] output;
        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));

            foreach (var editGroup in GetRewardEdits(session, editDomain)
                         .GroupBy(GetArchiveMemberFileName, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(editGroup.Key))
                {
                    diagnostics.Add(CreateDiagnostic(
                        editDomain,
                        DiagnosticSeverity.Error,
                        "Pending raid reward edit does not include a valid archive member.",
                        expected: "Known Sword/Shield raid reward member"));
                    continue;
                }

                var archive = SwShNestHoleRewardArchive.Parse(pack.GetFileByName(editGroup.Key));
                var archiveEdits = editGroup
                    .Select(edit => ToArchiveEdit(archive, edit, diagnostics))
                    .Where(edit => edit is not null)
                    .Select(edit => edit!)
                    .ToArray();

                if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
                }

                pack.SetFileByName(editGroup.Key, archive.WriteEdits(archiveEdits));
            }

            output = pack.Write();
            VerifyOutput(output, session, editDomain, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{workflowLabel} source file could not be decoded or safely edited: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield nest reward data"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{workflowLabel} source file could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield nest reward data"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (!SwShOutputRollbackScope.TryCapture(
                paths,
                currentPlan.Writes.Select(write => write.TargetRelativePath),
                out var rollbackScope,
                out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{workflowLabel} could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        using (var outputRollback = rollbackScope!)
        {
            try
            {
                WriteOutputAtomically(targetPath, output);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, dataSource.GraphEntry.RelativePath));
                outputRollback.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    $"{workflowLabel} output file could not be written: {exception.Message}",
                    file: dataSource.GraphEntry.RelativePath,
                    expected: "Writable output root"));
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics, editDomain);
            }
        }

        if (writtenFiles.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Info,
                $"Applied {workflowLabel} change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditRaidRewards(
        OpenedProject project,
        SwShRaidRewardsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics,
        string editDomain)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} edits require valid base RomFS, base ExeFS, and output root paths.",
                expected: "Editable workflow project paths"));
            return false;
        }

        if (workflow.Tables.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} edits require loaded reward tables.",
                expected: "Loaded raid reward data"));
            return false;
        }

        return true;
    }

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShRaidRewardsWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        string editDomain,
        bool addSuccessDiagnostic)
    {
        var effectiveWorkflow = workflow;
        var seen = new HashSet<(string RecordId, string Field)>();
        foreach (var edit in GetRewardEdits(session, editDomain))
        {
            if (!seen.Add((edit.RecordId ?? string.Empty, edit.Field ?? string.Empty)))
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    "A raid reward field has more than one pending value.",
                    field: edit.Field,
                    expected: "One pending value per reward field"));
                continue;
            }

            ValidatePendingEdit(project, effectiveWorkflow, edit, diagnostics, editDomain);
            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit, editDomain);
            }
        }

        if (GetRewardEdits(session, editDomain).Any()
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightArchiveWrite(project, session, editDomain, diagnostics);
        }

        if (GetRewardEdits(session, editDomain).Any()
            && addSuccessDiagnostic
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Info,
                $"Pending {GetWorkflowLabel(editDomain)} change is valid."));
        }
    }

    private static void ValidatePendingEdit(
        OpenedProject project,
        SwShRaidRewardsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics,
        string editDomain)
    {
        if (!string.Equals(edit.Domain, editDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' does not belong to {GetWorkflowLabel(editDomain)}.",
                expected: editDomain));
            return;
        }

        if (!SwShRaidRewardsWorkflowService.IsEditableField(edit.Field))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)", editDomain));
            return;
        }

        if (!SwShRaidRewardsWorkflowService.TryParseRewardRecordId(edit.RecordId, out var tableId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                "Pending raid reward edit does not target a valid reward record.",
                expected: "tableId#slot record id"));
            return;
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Pending raid reward table '{tableId}' is no longer available.",
                expected: "Current raid reward table"));
            return;
        }

        if (!table.Rewards.Any(candidate => candidate.Slot == slot))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Pending raid reward slot {slot} is no longer available.",
                expected: "Current raid reward slot"));
            return;
        }

        var reward = table.Rewards.First(candidate => candidate.Slot == slot);
        var currentSources = new List<ProjectFileReference>();
        if (string.Equals(edit.Field, SwShRaidRewardsWorkflowService.ItemIdField, StringComparison.Ordinal))
        {
            currentSources.AddRange(
                SwShRaidRewardsWorkflowService.ResolveItemDisplaySourcesForValidation(project)
                    .Select(source => new ProjectFileReference(
                        GetSourceLayer(source.GraphEntry),
                        source.GraphEntry.RelativePath)));
        }

        var archiveSources = edit.Sources
            .Where(source => string.Equals(
                source.RelativePath,
                table.Provenance.SourceFile,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasSignedTableIdentity = SwShRaidRewardsWorkflowService.TryParseTableId(
            table.TableId,
            out _,
            out _,
            out _,
            out _,
            out var isLegacy)
            && !isLegacy;
        var archiveSourceMatches = archiveSources.Length == 1
            && (hasSignedTableIdentity
                || archiveSources[0].Layer == table.Provenance.SourceLayer);
        if (!archiveSourceMatches
            || edit.Sources.Count != currentSources.Count + 1
            || currentSources.Any(source => !edit.Sources.Contains(source)))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                "The raid reward source layer changed after this edit was staged. Stage the edit again against the current source.",
                field: edit.Field,
                expected: "Pending edit staged from the current raid reward sources"));
            return;
        }

        _ = TryParseValue(workflow, table, reward, edit.Field, edit.NewValue, editDomain, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShRaidRewardsWorkflow workflow,
        SwShRaidRewardTableRecord table,
        SwShRaidRewardItemRecord reward,
        string field,
        string value,
        string editDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!SwShRaidRewardsWorkflowService.IsEditableField(normalizedField))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField, editDomain));
            return null;
        }

        var parsedValue = TryParseValue(workflow, table, reward, normalizedField, value, editDomain, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            editDomain,
            CreateSummary(table, reward, normalizedField, parsedValue.Value),
            [new ProjectFileReference(table.Provenance.SourceLayer, table.Provenance.SourceFile)],
            RecordId: SwShRaidRewardsWorkflowService.CreateRewardRecordId(table.TableId, reward.Slot),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string CreateSummary(
        SwShRaidRewardTableRecord table,
        SwShRaidRewardItemRecord reward,
        string field,
        int value)
    {
        var valueLabel = table.RewardKind == "drop" ? "drop chance" : "quantity";

        return field switch
        {
            SwShRaidRewardsWorkflowService.ItemIdField =>
                $"Set {table.RewardKindLabel} {table.SourceTableHash} slot {reward.Slot} item ID to {value}.",
            SwShRaidRewardsWorkflowService.Star1ValueField =>
                $"Set {table.RewardKindLabel} {table.SourceTableHash} slot {reward.Slot} 1-star {valueLabel} to {value}.",
            SwShRaidRewardsWorkflowService.Star2ValueField =>
                $"Set {table.RewardKindLabel} {table.SourceTableHash} slot {reward.Slot} 2-star {valueLabel} to {value}.",
            SwShRaidRewardsWorkflowService.Star3ValueField =>
                $"Set {table.RewardKindLabel} {table.SourceTableHash} slot {reward.Slot} 3-star {valueLabel} to {value}.",
            SwShRaidRewardsWorkflowService.Star4ValueField =>
                $"Set {table.RewardKindLabel} {table.SourceTableHash} slot {reward.Slot} 4-star {valueLabel} to {value}.",
            SwShRaidRewardsWorkflowService.Star5ValueField =>
                $"Set {table.RewardKindLabel} {table.SourceTableHash} slot {reward.Slot} 5-star {valueLabel} to {value}.",
            _ => $"Set {table.RewardKindLabel} {table.SourceTableHash} slot {reward.Slot} {field} to {value}.",
        };
    }

    private static int? TryParseValue(
        SwShRaidRewardsWorkflow workflow,
        SwShRaidRewardTableRecord table,
        SwShRaidRewardItemRecord reward,
        string? field,
        string? value,
        string editDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                "Raid reward edit value must be an integer.",
                field: field,
                expected: "Integer value"));
            return null;
        }

        if (!string.Equals(value, parsedValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                "Raid reward edit value must use canonical integer text without whitespace, a plus sign, or leading zeroes.",
                field: field,
                expected: parsedValue.ToString(CultureInfo.InvariantCulture)));
            return null;
        }

        var (minimum, maximum) = GetFieldRange(table, field);
        if (parsedValue < minimum || parsedValue > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Raid reward {field} must be between {minimum} and {maximum}.",
                field: field,
                expected: "Safe raid reward value"));
            return null;
        }

        if (string.Equals(field, SwShRaidRewardsWorkflowService.ItemIdField, StringComparison.Ordinal)
            && parsedValue != reward.ItemId
            && workflow.EditableFields
                .FirstOrDefault(candidate => candidate.Field == SwShRaidRewardsWorkflowService.ItemIdField)
                ?.Options is { Count: > 0 } itemOptions
            && !itemOptions.Any(option => option.Value == parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Raid reward item ID {parsedValue} is not present in the current Sword/Shield raid reward item choices.",
                field: field,
                expected: "Loaded item ID or the existing legacy item ID"));
            return null;
        }

        return parsedValue;
    }

    private static (int Minimum, int Maximum) GetFieldRange(SwShRaidRewardTableRecord table, string? field)
    {
        return field switch
        {
            SwShRaidRewardsWorkflowService.ItemIdField =>
                (SwShRaidRewardsWorkflowService.MinimumItemId, SwShRaidRewardsWorkflowService.MaximumEditableItemId),
            SwShRaidRewardsWorkflowService.Star1ValueField
                or SwShRaidRewardsWorkflowService.Star2ValueField
                or SwShRaidRewardsWorkflowService.Star3ValueField
                or SwShRaidRewardsWorkflowService.Star4ValueField
                or SwShRaidRewardsWorkflowService.Star5ValueField =>
                table.RewardKind == "drop"
                    ? (SwShRaidRewardsWorkflowService.MinimumRewardValue, SwShRaidRewardsWorkflowService.MaximumEditableDropChance)
                    : (SwShRaidRewardsWorkflowService.MinimumRewardValue, SwShRaidRewardsWorkflowService.MaximumEditableBonusQuantity),
            _ => (0, 0),
        };
    }

    private static EditSession ReplacePendingRaidRewardEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameRaidRewardEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static EditSession RemovePendingRaidRewardEdit(EditSession session, PendingEdit pendingEdit)
    {
        return RemovePendingRaidRewardEdit(
            session,
            pendingEdit.Domain,
            pendingEdit.RecordId,
            pendingEdit.Field);
    }

    private static EditSession RemovePendingRaidRewardEdit(
        EditSession session,
        string? domain,
        string? recordId,
        string? field)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, domain, StringComparison.Ordinal)
                    || !string.Equals(edit.RecordId, recordId, StringComparison.Ordinal)
                    || !string.Equals(edit.Field, field, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static bool IsSameRaidRewardEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static IEnumerable<PendingEdit> GetRewardEdits(EditSession session, string editDomain)
    {
        return session.PendingEdits.Where(edit =>
            string.Equals(edit.Domain, editDomain, StringComparison.Ordinal));
    }

    private static string? GetRewardFieldValue(SwShRaidRewardItemRecord reward, string? field)
    {
        var value = field switch
        {
            SwShRaidRewardsWorkflowService.ItemIdField => reward.ItemId,
            SwShRaidRewardsWorkflowService.Star1ValueField => GetRewardValue(reward, 0),
            SwShRaidRewardsWorkflowService.Star2ValueField => GetRewardValue(reward, 1),
            SwShRaidRewardsWorkflowService.Star3ValueField => GetRewardValue(reward, 2),
            SwShRaidRewardsWorkflowService.Star4ValueField => GetRewardValue(reward, 3),
            SwShRaidRewardsWorkflowService.Star5ValueField => GetRewardValue(reward, 4),
            _ => null,
        };

        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static long? GetRewardValue(SwShRaidRewardItemRecord reward, int index)
    {
        return (uint)index < (uint)reward.Values.Count ? reward.Values[index] : null;
    }

    private static PendingEdit AddItemValidationSources(OpenedProject project, PendingEdit pendingEdit)
    {
        if (!string.Equals(
                pendingEdit.Field,
                SwShRaidRewardsWorkflowService.ItemIdField,
                StringComparison.Ordinal))
        {
            return pendingEdit;
        }

        var itemSources = SwShRaidRewardsWorkflowService.ResolveItemDisplaySourcesForValidation(project)
            .Select(source => new ProjectFileReference(
                GetSourceLayer(source.GraphEntry),
                source.GraphEntry.RelativePath));

        return pendingEdit with
        {
            Sources = pendingEdit.Sources
                .Concat(itemSources)
                .Distinct()
                .ToArray(),
        };
    }

    private static SwShRaidRewardsWorkflow OverlayPendingEdits(
        SwShRaidRewardsWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        string editDomain)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit, editDomain);
        }

        return updatedWorkflow;
    }

    private static SwShRaidRewardsWorkflow OverlayPendingEdit(
        SwShRaidRewardsWorkflow workflow,
        PendingEdit edit,
        string editDomain)
    {
        if (!string.Equals(edit.Domain, editDomain, StringComparison.Ordinal)
            || !SwShRaidRewardsWorkflowService.IsEditableField(edit.Field)
            || !SwShRaidRewardsWorkflowService.TryParseRewardRecordId(edit.RecordId, out var tableId, out var slot))
        {
            return workflow;
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        var reward = table?.Rewards.FirstOrDefault(candidate => candidate.Slot == slot);
        if (table is null
            || reward is null
            || TryParseValue(
                workflow,
                table,
                reward,
                edit.Field,
                edit.NewValue,
                editDomain,
                new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        return workflow with
        {
            Tables = workflow.Tables
                .Select(candidate => candidate.TableId == tableId
                    ? candidate with
                    {
                        Rewards = candidate.Rewards
                            .Select(reward => OverlayReward(
                                workflow,
                                candidate,
                                reward,
                                slot,
                                edit.Field!,
                                value))
                            .ToArray(),
                    }
                    : candidate)
                .ToArray(),
        };
    }

    private static SwShRaidRewardItemRecord OverlayReward(
        SwShRaidRewardsWorkflow workflow,
        SwShRaidRewardTableRecord table,
        SwShRaidRewardItemRecord reward,
        int targetSlot,
        string field,
        int value)
    {
        if (reward.Slot != targetSlot)
        {
            return reward;
        }

        if (field == SwShRaidRewardsWorkflowService.ItemIdField)
        {
            return reward with
            {
                ItemId = value,
                ItemName = ResolveItemName(workflow, value),
            };
        }

        var values = reward.Values.ToArray();
        var valueIndex = FieldToValueIndex(field);
        if (valueIndex is null || valueIndex.Value >= values.Length)
        {
            return reward;
        }

        values[valueIndex.Value] = value;
        return reward with
        {
            Quantity = table.RewardKind == "bonus" ? values[0] : reward.Quantity,
            Weight = table.RewardKind == "drop" ? values[0] : reward.Weight,
            Values = values,
        };
    }

    private static string ResolveItemName(SwShRaidRewardsWorkflow workflow, int itemId)
    {
        var option = workflow.EditableFields
            .FirstOrDefault(field => field.Field == SwShRaidRewardsWorkflowService.ItemIdField)
            ?.Options
            .FirstOrDefault(candidate => candidate.Value == itemId);
        if (option is null)
        {
            return $"Item {itemId.ToString(CultureInfo.InvariantCulture)}";
        }

        var separator = option.Label.IndexOf(' ');
        return separator >= 0 && separator + 1 < option.Label.Length
            ? option.Label[(separator + 1)..]
            : option.Label;
    }

    private static int? FieldToValueIndex(string? field)
    {
        return field switch
        {
            SwShRaidRewardsWorkflowService.Star1ValueField => 0,
            SwShRaidRewardsWorkflowService.Star2ValueField => 1,
            SwShRaidRewardsWorkflowService.Star3ValueField => 2,
            SwShRaidRewardsWorkflowService.Star4ValueField => 3,
            SwShRaidRewardsWorkflowService.Star5ValueField => 4,
            _ => null,
        };
    }

    private static string GetArchiveMemberFileName(PendingEdit edit)
    {
        if (!SwShRaidRewardsWorkflowService.TryParseRewardRecordId(edit.RecordId, out var tableId, out _)
            || !SwShRaidRewardsWorkflowService.TryParseTableId(tableId, out var member, out _, out _))
        {
            return string.Empty;
        }

        return member.FileName;
    }

    private static SwShNestHoleRewardEdit? ToArchiveEdit(
        SwShNestHoleRewardArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShRaidRewardsWorkflowService.TryParseRewardRecordId(edit.RecordId, out var tableId, out var slot)
            || !SwShRaidRewardsWorkflowService.TryParseTableId(
                tableId,
                out var member,
                out var tableIndex,
                out var sourceTableId,
                out var sourceIdentity,
                out var isLegacy))
        {
            diagnostics.Add(CreateDiagnostic(
                edit.Domain,
                DiagnosticSeverity.Error,
                "Pending raid reward edit does not include a valid archive target.",
                expected: "Existing raid reward archive target"));
            return null;
        }

        if (isLegacy)
        {
            diagnostics.Add(CreateDiagnostic(
                edit.Domain,
                DiagnosticSeverity.Error,
                "Legacy pending raid reward identity is not signed by its source contents. Stage the edit again.",
                field: "tableId",
                expected: "Current signed raid reward table identity"));
            return null;
        }

        if ((uint)tableIndex >= (uint)archive.Tables.Count
            || archive.Tables[tableIndex].TableId != sourceTableId
            || !string.Equals(
                SwShRaidRewardsWorkflowService.CreateTableSourceIdentity(
                    member,
                    tableIndex,
                    archive.Tables[tableIndex]),
                sourceIdentity,
                StringComparison.OrdinalIgnoreCase)
            || (uint)(slot - 1) >= (uint)archive.Tables[tableIndex].Rewards.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                edit.Domain,
                DiagnosticSeverity.Error,
                "Pending raid reward edit target no longer matches the source archive.",
                expected: "Current raid reward archive target"));
            return null;
        }

        if (!TryParseArchiveValue(member, edit.Field, edit.NewValue, edit.Domain, diagnostics, out var value))
        {
            return null;
        }

        var field = ToArchiveField(edit.Field);
        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)", edit.Domain));
            return null;
        }

        return new SwShNestHoleRewardEdit(tableIndex, slot - 1, field.Value, checked((uint)value));
    }

    private static bool TryParseArchiveValue(
        RaidRewardArchiveMember member,
        string? field,
        string? value,
        string editDomain,
        ICollection<ValidationDiagnostic> diagnostics,
        out int parsedValue)
    {
        parsedValue = 0;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsedValue)
            || !string.Equals(value, parsedValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                "Pending raid reward value is not a canonical integer.",
                field: field,
                expected: "Canonical integer text"));
            return false;
        }

        var maximum = field switch
        {
            SwShRaidRewardsWorkflowService.ItemIdField => SwShRaidRewardsWorkflowService.MaximumEditableItemId,
            SwShRaidRewardsWorkflowService.Star1ValueField
                or SwShRaidRewardsWorkflowService.Star2ValueField
                or SwShRaidRewardsWorkflowService.Star3ValueField
                or SwShRaidRewardsWorkflowService.Star4ValueField
                or SwShRaidRewardsWorkflowService.Star5ValueField => member.MaximumEditableValue,
            _ => -1,
        };
        if (maximum < 0)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)", editDomain));
            return false;
        }

        if (parsedValue < 0 || parsedValue > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"Pending raid reward {field} must be between 0 and {maximum}.",
                field: field,
                expected: "Safe raid reward value"));
            return false;
        }

        return true;
    }

    private static SwShNestHoleRewardField? ToArchiveField(string? field)
    {
        return field switch
        {
            SwShRaidRewardsWorkflowService.ItemIdField => SwShNestHoleRewardField.ItemId,
            SwShRaidRewardsWorkflowService.Star1ValueField => SwShNestHoleRewardField.Star1Value,
            SwShRaidRewardsWorkflowService.Star2ValueField => SwShNestHoleRewardField.Star2Value,
            SwShRaidRewardsWorkflowService.Star3ValueField => SwShNestHoleRewardField.Star3Value,
            SwShRaidRewardsWorkflowService.Star4ValueField => SwShNestHoleRewardField.Star4Value,
            SwShRaidRewardsWorkflowService.Star5ValueField => SwShNestHoleRewardField.Star5Value,
            _ => null,
        };
    }

    private static void PreflightArchiveWrite(
        OpenedProject project,
        EditSession session,
        string editDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShRaidRewardsWorkflowService.ResolveNestDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} edit preflight could not resolve the source nest archive.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return;
        }

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(source.AbsolutePath));
            foreach (var editGroup in GetRewardEdits(session, editDomain)
                         .GroupBy(GetArchiveMemberFileName, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(editGroup.Key))
                {
                    diagnostics.Add(CreateDiagnostic(
                        editDomain,
                        DiagnosticSeverity.Error,
                        "Pending raid reward edit does not include a valid archive member.",
                        expected: "Known Sword/Shield raid reward member"));
                    return;
                }

                var archive = SwShNestHoleRewardArchive.Parse(pack.GetFileByName(editGroup.Key));
                var archiveEdits = editGroup
                    .Select(edit => ToArchiveEdit(archive, edit, diagnostics))
                    .Where(edit => edit is not null)
                    .Select(edit => edit!)
                    .ToArray();
                if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    return;
                }

                pack.SetFileByName(editGroup.Key, archive.WriteEdits(archiveEdits));
            }

            var output = pack.Write();
            VerifyOutput(output, session, editDomain, diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} edit cannot be encoded safely: {exception.Message}",
                expected: "Compatible Sword/Shield raid reward archive",
                file: source.GraphEntry.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} edit preflight could not read the source archive: {exception.Message}",
                expected: "Readable Sword/Shield raid reward archive",
                file: source.GraphEntry.RelativePath));
        }
    }

    private static void VerifyOutput(
        byte[] output,
        EditSession session,
        string editDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var pack = SwShGfPackFile.Parse(output);
        foreach (var edit in GetRewardEdits(session, editDomain))
        {
            if (!SwShRaidRewardsWorkflowService.TryParseRewardRecordId(edit.RecordId, out var tableId, out var slot)
                || !SwShRaidRewardsWorkflowService.TryParseTableId(
                    tableId,
                    out var member,
                    out var tableIndex,
                    out var tableHash))
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    "Generated raid reward output could not resolve a staged record.",
                    field: edit.Field,
                    expected: "Current raid reward record identity"));
                continue;
            }

            var archive = SwShNestHoleRewardArchive.Parse(pack.GetFileByName(member.FileName));
            if ((uint)tableIndex >= (uint)archive.Tables.Count
                || archive.Tables[tableIndex].TableId != tableHash
                || (uint)(slot - 1) >= (uint)archive.Tables[tableIndex].Rewards.Count
                || !TryParseArchiveValue(
                    member,
                    edit.Field,
                    edit.NewValue,
                    editDomain,
                    diagnostics,
                    out var expectedValue))
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    "Generated raid reward output no longer contains the staged target.",
                    field: edit.Field,
                    expected: "Verified raid reward output"));
                continue;
            }

            var reward = archive.Tables[tableIndex].Rewards[slot - 1];
            var actualValue = edit.Field switch
            {
                SwShRaidRewardsWorkflowService.ItemIdField => reward.ItemId,
                SwShRaidRewardsWorkflowService.Star1ValueField => reward.Values[0],
                SwShRaidRewardsWorkflowService.Star2ValueField => reward.Values[1],
                SwShRaidRewardsWorkflowService.Star3ValueField => reward.Values[2],
                SwShRaidRewardsWorkflowService.Star4ValueField => reward.Values[3],
                SwShRaidRewardsWorkflowService.Star5ValueField => reward.Values[4],
                _ => uint.MaxValue,
            };
            if (actualValue != (uint)expectedValue)
            {
                diagnostics.Add(CreateDiagnostic(
                    editDomain,
                    DiagnosticSeverity.Error,
                    "Generated raid reward output did not retain the staged value.",
                    field: edit.Field,
                    expected: expectedValue.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Raid reward output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Raid reward output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(tempPath, contents);
            if (!File.Exists(tempPath)
                || !File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Raid reward temporary output verification failed.");
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The original output remains untouched when temporary-file cleanup fails.
            }
        }
    }

    private static void RollbackFailedApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics,
        string editDomain)
    {
        var failures = rollbackScope.Rollback();
        writtenFiles.Clear();
        if (failures.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Info,
                $"{GetWorkflowLabel(editDomain)} apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in failures)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private static string CreatePlanReason(IReadOnlyList<PendingEdit> edits, string workflowLabel)
    {
        var canonical = new StringBuilder();
        foreach (var edit in edits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                     .ThenBy(edit => edit.NewValue, StringComparer.Ordinal))
        {
            AppendFingerprintComponent(canonical, edit.Domain);
            AppendFingerprintComponent(canonical, edit.RecordId);
            AppendFingerprintComponent(canonical, edit.Field);
            AppendFingerprintComponent(canonical, edit.NewValue);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintComponent(canonical, ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintComponent(canonical, source.RelativePath);
            }
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        var summary = edits.Count == 1
            ? $"Apply pending {workflowLabel} edit to {edits[0].RecordId}."
            : $"Apply {edits.Count} pending {workflowLabel} edits.";
        return $"{summary} Fingerprint {fingerprint}.";
    }

    private static void AppendFingerprintComponent(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length ?? -1);
        destination.Append(':');
        destination.Append(value);
        destination.Append(';');
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        string editDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
                paths,
                out var stablePaths,
                out var stableRootFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                stableRootFailure ?? "Configured output root could not be resolved safely.",
                file: targetRelativePath,
                expected: "Stable output root"));
            return null;
        }

        var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            stablePaths.OutputRootPath,
            targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                editDomain,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(editDomain)} apply target must stay inside the configured output root.",
                file: targetRelativePath,
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

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
    }

    private static string? GetDirectEditDomain(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return domains.Length == 1
            && domains[0] is RaidRewardsEditDomain or RaidBonusRewardsEditDomain
                ? domains[0]
                : null;
    }

    private static ValidationDiagnostic CreateDirectDomainOwnershipDiagnostic(EditSession session)
    {
        var supportedDomains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => domain is RaidRewardsEditDomain or RaidBonusRewardsEditDomain)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var diagnosticDomain = supportedDomains.Length == 1
            ? supportedDomains[0]
            : RaidRewardsEditDomain;
        return CreateDiagnostic(
            diagnosticDomain,
            DiagnosticSeverity.Error,
            "Raid reward edits cannot be planned directly from an empty, foreign, or mixed-domain edit session. Use the project edit-session workflow to review and apply combined changes.",
            expected: "Exactly one direct Raid Rewards or Raid Bonus Rewards domain");
    }

    private static SwShRaidRewardWorkflowKind GetWorkflowKind(string editDomain)
    {
        return string.Equals(editDomain, RaidBonusRewardsEditDomain, StringComparison.Ordinal)
            ? SwShRaidRewardWorkflowKind.Bonus
            : SwShRaidRewardWorkflowKind.Drop;
    }

    private static string GetWorkflowLabel(string editDomain)
    {
        return string.Equals(editDomain, RaidBonusRewardsEditDomain, StringComparison.Ordinal)
            ? "Raid Bonus Rewards"
            : "Raid Rewards";
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field, string editDomain)
    {
        return CreateDiagnostic(
            editDomain,
            DiagnosticSeverity.Error,
            $"Raid reward field '{field}' is not supported by the {GetWorkflowLabel(editDomain)} workflow yet.",
            field: "field",
            expected: string.Join(
                ", ",
                [
                    SwShRaidRewardsWorkflowService.ItemIdField,
                    SwShRaidRewardsWorkflowService.Star1ValueField,
                    SwShRaidRewardsWorkflowService.Star2ValueField,
                    SwShRaidRewardsWorkflowService.Star3ValueField,
                    SwShRaidRewardsWorkflowService.Star4ValueField,
                    SwShRaidRewardsWorkflowService.Star5ValueField,
                ]));
    }

    private static ValidationDiagnostic CreateDiagnostic(
        string editDomain,
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
            Field: field,
            Domain: editDomain,
            Expected: expected);
    }
}
