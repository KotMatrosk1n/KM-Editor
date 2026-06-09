// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Raids;

public sealed class SwShRaidRewardsEditSessionService
{
    private const string RaidRewardsEditDomain = "workflow.raidRewards";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShRaidRewardsWorkflowService raidRewardsWorkflowService;

    public SwShRaidRewardsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRaidRewardsWorkflowService? raidRewardsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
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
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tableId);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = raidRewardsWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditRaidRewards(project, workflow, diagnostics))
        {
            return new SwShRaidRewardsEditResult(workflow, currentSession, diagnostics);
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid reward table '{tableId}' is not present in the loaded workflow.",
                field: "tableId",
                expected: "Existing raid reward table"));
            return new SwShRaidRewardsEditResult(workflow, currentSession, diagnostics);
        }

        var reward = table.Rewards.FirstOrDefault(candidate => candidate.Slot == slot);
        if (reward is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid reward table '{table.DenId}' does not have reward slot {slot}.",
                field: "slot",
                expected: "Existing raid reward slot"));
            return new SwShRaidRewardsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(table, reward, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShRaidRewardsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingRaidRewardEdit(currentSession, pendingEdit);

        return new SwShRaidRewardsEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = raidRewardsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditRaidRewards(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending raid reward change is valid."));
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

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Raid Rewards edit before reviewing a change plan.",
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
                DiagnosticSeverity.Error,
                "Raid Rewards change plan could not resolve the source nest archive.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = SwShRaidRewardsWorkflowService.ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards apply target must stay inside the configured output root.",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            dataSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(dataSource.GraphEntry), dataSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Raid Rewards edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Raid Rewards edits.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

        return new ChangePlan(session.Id, [write], diagnostics);
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
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Raid Rewards change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShRaidRewardsWorkflowService.ResolveNestDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards apply could not resolve the source nest archive.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));

            foreach (var editGroup in session.PendingEdits.GroupBy(GetArchiveMemberFileName, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(editGroup.Key))
                {
                    diagnostics.Add(CreateDiagnostic(
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

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, pack.Write());
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, dataSource.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Raid Rewards change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards source file could not be decoded: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield nest reward data"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards output file could not be written: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards output file could not be written: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditRaidRewards(
        OpenedProject project,
        SwShRaidRewardsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards edits require valid base RomFS, base ExeFS, and output root paths.",
                expected: "Editable workflow project paths"));
            return false;
        }

        if (workflow.Tables.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards edits require loaded reward tables.",
                expected: "Loaded raid reward data"));
            return false;
        }

        return true;
    }

    private static void ValidatePendingEdit(
        SwShRaidRewardsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, RaidRewardsEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' does not belong to Raid Rewards.",
                expected: RaidRewardsEditDomain));
            return;
        }

        if (!SwShRaidRewardsWorkflowService.TryParseRewardRecordId(edit.RecordId, out var tableId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid reward edit does not target a valid reward record.",
                expected: "tableId#slot record id"));
            return;
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending raid reward table '{tableId}' is no longer available.",
                expected: "Current raid reward table"));
            return;
        }

        if (!table.Rewards.Any(candidate => candidate.Slot == slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending raid reward slot {slot} is no longer available.",
                expected: "Current raid reward slot"));
            return;
        }

        _ = TryParseValue(table, edit.Field, edit.NewValue, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShRaidRewardTableRecord table,
        SwShRaidRewardItemRecord reward,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!SwShRaidRewardsWorkflowService.IsEditableField(normalizedField))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = TryParseValue(table, normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            RaidRewardsEditDomain,
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
        SwShRaidRewardTableRecord table,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid reward edit value must be an integer.",
                field: field,
                expected: "Integer value"));
            return null;
        }

        var (minimum, maximum) = GetFieldRange(table, field);
        if (parsedValue < minimum || parsedValue > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid reward {field} must be between {minimum} and {maximum}.",
                field: field,
                expected: "Safe raid reward value"));
            return null;
        }

        return parsedValue;
    }

    private static (int Minimum, int Maximum) GetFieldRange(SwShRaidRewardTableRecord table, string? field)
    {
        return field switch
        {
            SwShRaidRewardsWorkflowService.ItemIdField =>
                (SwShRaidRewardsWorkflowService.MinimumItemId, SwShRaidRewardsWorkflowService.MaximumItemId),
            SwShRaidRewardsWorkflowService.Star1ValueField
                or SwShRaidRewardsWorkflowService.Star2ValueField
                or SwShRaidRewardsWorkflowService.Star3ValueField
                or SwShRaidRewardsWorkflowService.Star4ValueField
                or SwShRaidRewardsWorkflowService.Star5ValueField =>
                table.RewardKind == "drop"
                    ? (SwShRaidRewardsWorkflowService.MinimumRewardValue, 100)
                    : (SwShRaidRewardsWorkflowService.MinimumRewardValue, SwShRaidRewardsWorkflowService.MaximumRewardValue),
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

    private static bool IsSameRaidRewardEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShRaidRewardsWorkflow OverlayPendingEdits(
        SwShRaidRewardsWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShRaidRewardsWorkflow OverlayPendingEdit(SwShRaidRewardsWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, RaidRewardsEditDomain, StringComparison.Ordinal)
            || !SwShRaidRewardsWorkflowService.IsEditableField(edit.Field)
            || !SwShRaidRewardsWorkflowService.TryParseRewardRecordId(edit.RecordId, out var tableId, out var slot))
        {
            return workflow;
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null || TryParseValue(table, edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
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
                            .Select(reward => OverlayReward(candidate, reward, slot, edit.Field!, value))
                            .ToArray(),
                    }
                    : candidate)
                .ToArray(),
        };
    }

    private static SwShRaidRewardItemRecord OverlayReward(
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
                ItemName = $"Item {value}",
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
                out var sourceTableId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid reward edit does not include a valid archive target.",
                expected: "Existing raid reward archive target"));
            return null;
        }

        if ((uint)tableIndex >= (uint)archive.Tables.Count
            || archive.Tables[tableIndex].TableId != sourceTableId
            || (uint)(slot - 1) >= (uint)archive.Tables[tableIndex].Rewards.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid reward edit target no longer matches the source archive.",
                expected: "Current raid reward archive target"));
            return null;
        }

        var workflowTable = new SwShRaidRewardTableRecord(
            tableId,
            $"table_{sourceTableId:X16}",
            Rank: 0,
            GameVersion: "Sword/Shield",
            member.Key,
            member.Label,
            member.FileName,
            tableIndex,
            $"0x{sourceTableId:X16}",
            Array.Empty<SwShRaidRewardItemRecord>(),
            new SwShRaidRewardProvenance(string.Empty, ProjectFileLayer.Base, ProjectFileGraphEntryState.BaseOnly));
        if (TryParseValue(workflowTable, edit.Field, edit.NewValue, diagnostics) is not { } value)
        {
            return null;
        }

        var field = ToArchiveField(edit.Field);
        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        return new SwShNestHoleRewardEdit(tableIndex, slot - 1, field.Value, checked((uint)value));
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

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShRaidRewardsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
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

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Raid reward field '{field}' is not supported by the Raid Rewards workflow yet.",
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
            Domain: RaidRewardsEditDomain,
            Expected: expected);
    }
}
