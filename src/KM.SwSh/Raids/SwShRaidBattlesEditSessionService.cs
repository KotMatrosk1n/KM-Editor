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

public sealed class SwShRaidBattlesEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShRaidBattlesWorkflowService raidBattlesWorkflowService;

    public SwShRaidBattlesEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRaidBattlesWorkflowService? raidBattlesWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.raidBattlesWorkflowService = raidBattlesWorkflowService ?? new SwShRaidBattlesWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShRaidBattlesEditResult UpdateSlotField(
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
        var loadedWorkflow = raidBattlesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditRaidBattles(project, workflow, diagnostics))
        {
            return new SwShRaidBattlesEditResult(workflow, currentSession, diagnostics);
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid battle table '{tableId}' is not present in the loaded workflow.",
                field: "tableId",
                expected: "Existing raid battle table"));
            return new SwShRaidBattlesEditResult(workflow, currentSession, diagnostics);
        }

        var slotRecord = table.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (slotRecord is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid battle table '{table.DenId}' does not have slot {slot}.",
                field: "slot",
                expected: "Existing raid battle slot"));
            return new SwShRaidBattlesEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(table, slotRecord, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShRaidBattlesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingRaidBattleEdit(currentSession, pendingEdit);

        return new SwShRaidBattlesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = raidBattlesWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditRaidBattles(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending raid battle change is valid."));
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
                "Create a pending Raid Battles edit before reviewing a change plan.",
                expected: "Pending raid battle edit"));
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
                "Raid Battles change plan could not resolve the source nest archive.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = SwShRaidRewardsWorkflowService.ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Battles apply target must stay inside the configured output root.",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            dataSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(dataSource.GraphEntry), dataSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Raid Battles edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Raid Battles edits.");

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
                expected: "Current reviewed Raid Battles change plan"));
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
                "Raid Battles apply could not resolve the source nest archive.",
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
            var archive = SwShEncounterNestArchive.Parse(pack.GetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName));
            var archiveEdits = session.PendingEdits
                .Select(edit => ToArchiveEdit(archive, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            pack.SetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName, archive.WriteEdits(archiveEdits));

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, pack.Write());
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, dataSource.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Raid Battles change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles source file could not be decoded: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield raid battle data"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles output file could not be written: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles output file could not be written: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditRaidBattles(
        OpenedProject project,
        SwShRaidBattlesWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Battles edits require valid base RomFS, base ExeFS, and output root paths.",
                expected: "Editable workflow project paths"));
            return false;
        }

        if (workflow.Tables.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Battles edits require loaded battle tables.",
                expected: "Loaded raid battle data"));
            return false;
        }

        return true;
    }

    private static void ValidatePendingEdit(
        SwShRaidBattlesWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShRaidBattlesWorkflowService.RaidBattlesEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' does not belong to Raid Battles.",
                expected: SwShRaidBattlesWorkflowService.RaidBattlesEditDomain));
            return;
        }

        if (!SwShRaidBattlesWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid battle edit does not target a valid slot record.",
                expected: "tableId#slot record id"));
            return;
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending raid battle table '{tableId}' is no longer available.",
                expected: "Current raid battle table"));
            return;
        }

        if (!table.Slots.Any(candidate => candidate.Slot == slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending raid battle slot {slot} is no longer available.",
                expected: "Current raid battle slot"));
            return;
        }

        _ = TryParseValue(edit.Field, edit.NewValue, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShRaidBattleTableRecord table,
        SwShRaidBattleSlotRecord slotRecord,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!SwShRaidBattlesWorkflowService.IsEditableField(normalizedField))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = TryParseValue(normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            SwShRaidBattlesWorkflowService.RaidBattlesEditDomain,
            CreateSummary(table, slotRecord, normalizedField, parsedValue.Value),
            [new ProjectFileReference(table.Provenance.SourceLayer, table.Provenance.SourceFile)],
            RecordId: SwShRaidBattlesWorkflowService.CreateSlotRecordId(table.TableId, slotRecord.Slot),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string CreateSummary(
        SwShRaidBattleTableRecord table,
        SwShRaidBattleSlotRecord slotRecord,
        string field,
        int value)
    {
        return field switch
        {
            SwShRaidBattlesWorkflowService.SpeciesField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} species to {value}.",
            SwShRaidBattlesWorkflowService.FormField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} form to {value}.",
            SwShRaidBattlesWorkflowService.AbilityField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} ability roll to {value}.",
            SwShRaidBattlesWorkflowService.IsGigantamaxField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} Gigantamax flag to {value}.",
            SwShRaidBattlesWorkflowService.GenderField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} gender to {value}.",
            SwShRaidBattlesWorkflowService.FlawlessIvsField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} guaranteed perfect IVs to {value}.",
            SwShRaidBattlesWorkflowService.Star1ProbabilityField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} 1-star probability to {value}%.",
            SwShRaidBattlesWorkflowService.Star2ProbabilityField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} 2-star probability to {value}%.",
            SwShRaidBattlesWorkflowService.Star3ProbabilityField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} 3-star probability to {value}%.",
            SwShRaidBattlesWorkflowService.Star4ProbabilityField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} 4-star probability to {value}%.",
            SwShRaidBattlesWorkflowService.Star5ProbabilityField =>
                $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} 5-star probability to {value}%.",
            _ => $"Set Raid Battles {table.SourceTableHash} slot {slotRecord.Slot} {field} to {value}.",
        };
    }

    private static int? TryParseValue(
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid battle edit value must be an integer.",
                field: field,
                expected: "Integer value"));
            return null;
        }

        var editableField = SwShRaidBattlesWorkflowService.GetEditableField(field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
            return null;
        }

        var minimum = editableField.MinimumValue ?? 0;
        var maximum = editableField.MaximumValue ?? 0;
        if (parsedValue < minimum || parsedValue > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid battle {editableField.Label} must be between {minimum} and {maximum}.",
                field: field,
                expected: $"Safe Raid Battles {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        return parsedValue;
    }

    private static EditSession ReplacePendingRaidBattleEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameRaidBattleEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameRaidBattleEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShRaidBattlesWorkflow OverlayPendingEdits(
        SwShRaidBattlesWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShRaidBattlesWorkflow OverlayPendingEdit(SwShRaidBattlesWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShRaidBattlesWorkflowService.RaidBattlesEditDomain, StringComparison.Ordinal)
            || !SwShRaidBattlesWorkflowService.IsEditableField(edit.Field)
            || !SwShRaidBattlesWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot))
        {
            return workflow;
        }

        if (TryParseValue(edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        return workflow with
        {
            Tables = workflow.Tables
                .Select(table => table.TableId == tableId
                    ? table with
                    {
                        Slots = table.Slots
                            .Select(slotRecord => OverlaySlot(workflow, slotRecord, slot, edit.Field!, value))
                            .ToArray(),
                    }
                    : table)
                .ToArray(),
        };
    }

    private static SwShRaidBattleSlotRecord OverlaySlot(
        SwShRaidBattlesWorkflow workflow,
        SwShRaidBattleSlotRecord slotRecord,
        int targetSlot,
        string field,
        int value)
    {
        if (slotRecord.Slot != targetSlot)
        {
            return slotRecord;
        }

        return field switch
        {
            SwShRaidBattlesWorkflowService.SpeciesField => slotRecord with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, SwShRaidBattlesWorkflowService.SpeciesField, value, "Species"),
            },
            SwShRaidBattlesWorkflowService.FormField => slotRecord with { Form = value },
            SwShRaidBattlesWorkflowService.AbilityField => slotRecord with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, SwShRaidBattlesWorkflowService.AbilityField, value, "Ability roll"),
            },
            SwShRaidBattlesWorkflowService.IsGigantamaxField => slotRecord with { IsGigantamax = value != 0 },
            SwShRaidBattlesWorkflowService.GenderField => slotRecord with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, SwShRaidBattlesWorkflowService.GenderField, value, "Gender"),
            },
            SwShRaidBattlesWorkflowService.FlawlessIvsField => slotRecord with { FlawlessIvs = value },
            SwShRaidBattlesWorkflowService.Star1ProbabilityField => OverlayProbability(slotRecord, probabilityIndex: 0, value),
            SwShRaidBattlesWorkflowService.Star2ProbabilityField => OverlayProbability(slotRecord, probabilityIndex: 1, value),
            SwShRaidBattlesWorkflowService.Star3ProbabilityField => OverlayProbability(slotRecord, probabilityIndex: 2, value),
            SwShRaidBattlesWorkflowService.Star4ProbabilityField => OverlayProbability(slotRecord, probabilityIndex: 3, value),
            SwShRaidBattlesWorkflowService.Star5ProbabilityField => OverlayProbability(slotRecord, probabilityIndex: 4, value),
            _ => slotRecord,
        };
    }

    private static SwShRaidBattleSlotRecord OverlayProbability(
        SwShRaidBattleSlotRecord slotRecord,
        int probabilityIndex,
        int value)
    {
        var probabilities = slotRecord.Probabilities.ToArray();
        if ((uint)probabilityIndex >= (uint)probabilities.Length)
        {
            return slotRecord;
        }

        probabilities[probabilityIndex] = value;
        return slotRecord with
        {
            Probabilities = probabilities,
            ProbabilitySummary = string.Join(
                " / ",
                probabilities.Select((probability, index) =>
                    $"{index + 1}-star {probability.ToString(CultureInfo.InvariantCulture)}%")),
        };
    }

    private static string GetOptionLabel(
        SwShRaidBattlesWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(candidate => candidate.Field == field)?.Options
            ?? Array.Empty<SwShRaidBattleEditableFieldOption>();

        return SwShRaidBattlesWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static SwShEncounterNestEdit? ToArchiveEdit(
        SwShEncounterNestArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShRaidBattlesWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || !SwShRaidBattlesWorkflowService.TryParseTableId(tableId, out var tableIndex, out var sourceTableId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid battle edit does not include a valid archive target.",
                expected: "Existing raid battle archive target"));
            return null;
        }

        if ((uint)tableIndex >= (uint)archive.Tables.Count
            || archive.Tables[tableIndex].TableId != sourceTableId
            || (uint)(slot - 1) >= (uint)archive.Tables[tableIndex].Entries.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid battle edit target no longer matches the source archive.",
                expected: "Current raid battle archive target"));
            return null;
        }

        if (TryParseValue(edit.Field, edit.NewValue, diagnostics) is not { } value)
        {
            return null;
        }

        var field = ToArchiveField(edit.Field);
        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        return new SwShEncounterNestEdit(tableIndex, slot - 1, field.Value, value);
    }

    private static SwShEncounterNestField? ToArchiveField(string? field)
    {
        return field switch
        {
            SwShRaidBattlesWorkflowService.SpeciesField => SwShEncounterNestField.Species,
            SwShRaidBattlesWorkflowService.FormField => SwShEncounterNestField.Form,
            SwShRaidBattlesWorkflowService.AbilityField => SwShEncounterNestField.Ability,
            SwShRaidBattlesWorkflowService.IsGigantamaxField => SwShEncounterNestField.IsGigantamax,
            SwShRaidBattlesWorkflowService.GenderField => SwShEncounterNestField.Gender,
            SwShRaidBattlesWorkflowService.FlawlessIvsField => SwShEncounterNestField.FlawlessIvs,
            SwShRaidBattlesWorkflowService.Star1ProbabilityField => SwShEncounterNestField.Star1Probability,
            SwShRaidBattlesWorkflowService.Star2ProbabilityField => SwShEncounterNestField.Star2Probability,
            SwShRaidBattlesWorkflowService.Star3ProbabilityField => SwShEncounterNestField.Star3Probability,
            SwShRaidBattlesWorkflowService.Star4ProbabilityField => SwShEncounterNestField.Star4Probability,
            SwShRaidBattlesWorkflowService.Star5ProbabilityField => SwShEncounterNestField.Star5Probability,
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
                "Raid Battles apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Battles apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShRaidRewardsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Battles apply target must stay inside the configured output root.",
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
            $"Raid Battles field '{field}' is not supported by the Raid Battles workflow yet.",
            field: "field",
            expected: string.Join(
                ", ",
                [
                    SwShRaidBattlesWorkflowService.SpeciesField,
                    SwShRaidBattlesWorkflowService.FormField,
                    SwShRaidBattlesWorkflowService.AbilityField,
                    SwShRaidBattlesWorkflowService.IsGigantamaxField,
                    SwShRaidBattlesWorkflowService.GenderField,
                    SwShRaidBattlesWorkflowService.FlawlessIvsField,
                    SwShRaidBattlesWorkflowService.Star1ProbabilityField,
                    SwShRaidBattlesWorkflowService.Star2ProbabilityField,
                    SwShRaidBattlesWorkflowService.Star3ProbabilityField,
                    SwShRaidBattlesWorkflowService.Star4ProbabilityField,
                    SwShRaidBattlesWorkflowService.Star5ProbabilityField,
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
            Domain: SwShRaidBattlesWorkflowService.RaidBattlesEditDomain,
            Expected: expected);
    }
}
