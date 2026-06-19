// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Encounters;

public sealed class SwShEncountersEditSessionService
{
    private const string EncountersEditDomain = "workflow.encounters";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShEncountersWorkflowService encountersWorkflowService;

    public SwShEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShEncountersWorkflowService? encountersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.encountersWorkflowService = encountersWorkflowService ?? new SwShEncountersWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShEncountersEditResult UpdateSlotField(
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
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditEncounters(project, workflow, diagnostics))
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter table '{tableId}' is not present in the loaded workflow.",
                field: "tableId",
                expected: "Existing encounter table"));
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var slotRecord = table.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (slotRecord is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter table '{table.Location}' does not have slot {slot}.",
                field: "slot",
                expected: "Existing encounter slot"));
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(table, slotRecord, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingEncounterEdit(currentSession, pendingEdit);

        return new SwShEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = encountersWorkflowService.Load(project);
        var workflowWithPendingEdits = OverlayPendingEdits(workflow, session.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditEncounters(project, workflow, diagnostics);
        ValidatePendingLevelPairs(workflow, session.PendingEdits, diagnostics);
        ValidateEncounterProbabilityTotals(workflowWithPendingEdits, session.PendingEdits, diagnostics);
        ValidateNoEmptyWeightedSlots(workflowWithPendingEdits, session.PendingEdits, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending encounter change is valid."));
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
                "Create a pending Encounters edit before reviewing a change plan.",
                expected: "Pending encounter edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShEncountersWorkflowService.ResolveWildDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounters change plan could not resolve the source encounter archive.",
                expected: SwShEncountersWorkflowService.WildDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = SwShEncountersWorkflowService.ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounters apply target must stay inside the configured output root.",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            dataSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(dataSource.GraphEntry), dataSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Encounters edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Encounters edits.");

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
                expected: "Current reviewed Encounters change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShEncountersWorkflowService.ResolveWildDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounters apply could not resolve the source encounter archive.",
                expected: SwShEncountersWorkflowService.WildDataPath));
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
            var workflow = encountersWorkflowService.Load(project);

            foreach (var editGroup in session.PendingEdits.GroupBy(GetArchiveMemberFileName, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(editGroup.Key))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending encounter edit does not include a valid archive member.",
                        expected: "Known Sword/Shield encounter member"));
                    continue;
                }

                var archive = SwShWildEncounterArchive.Parse(pack.GetFileByName(editGroup.Key));
                var availableSubTableIndexes = CreateAvailableSubTableIndexLookup(workflow, editGroup.Key);
                var archiveEdits = editGroup
                    .SelectMany(edit => ToArchiveEdits(archive, edit, availableSubTableIndexes, diagnostics))
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
                "Applied Encounters change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters source file could not be decoded: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield data_table.gfpak"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters output file could not be written: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters output file could not be written: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditEncounters(
        OpenedProject project,
        SwShEncountersWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounters edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void ValidatePendingEdit(
        SwShEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, EncountersEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Encounters workflow.",
                expected: EncountersEditDomain));
            return;
        }

        if (!SwShEncountersWorkflowService.IsEditableField(edit.Field))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets an invalid slot.",
                field: "slot",
                expected: "Encounter slot"));
            return;
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null || table.Slots.All(candidate => candidate.Slot != slot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets a slot that is not loaded.",
                field: "slot",
                expected: "Existing encounter slot"));
            return;
        }

        _ = TryParseValue(edit.Field, edit.NewValue, diagnostics);
    }

    private static void ValidatePendingLevelPairs(
        SwShEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var levels = workflow.Tables.ToDictionary(
            table => table.TableId,
            table =>
            {
                var firstSlot = table.Slots.FirstOrDefault();
                return new LevelPair(firstSlot?.LevelMin ?? 0, firstSlot?.LevelMax ?? 0);
            },
            StringComparer.Ordinal);

        foreach (var edit in edits.Where(edit => string.Equals(edit.Domain, EncountersEditDomain, StringComparison.Ordinal)))
        {
            if (!SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out _)
                || !levels.TryGetValue(tableId, out var current)
                || TryParseValue(edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
            {
                continue;
            }

            if (edit.Field is SwShEncountersWorkflowService.LevelMinField or SwShEncountersWorkflowService.LevelMaxField)
            {
                foreach (var targetTableId in levels.Keys
                    .Where(candidateTableId => IsSameEncounterZoneTable(candidateTableId, tableId))
                    .ToArray())
                {
                    var targetCurrent = levels[targetTableId];
                    levels[targetTableId] = edit.Field switch
                    {
                        SwShEncountersWorkflowService.LevelMinField => targetCurrent with { LevelMin = value },
                        SwShEncountersWorkflowService.LevelMaxField => targetCurrent with { LevelMax = value },
                        _ => targetCurrent,
                    };
                }

                continue;
            }

            levels[tableId] = current;
        }

        foreach (var pair in levels.Where(pair => pair.Value.LevelMin > pair.Value.LevelMax))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter table '{pair.Key}' has a minimum level greater than its maximum level.",
                field: "level",
                expected: "Min level less than or equal to max level"));
        }
    }

    private static void ValidateEncounterProbabilityTotals(
        SwShEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var touchedTableIds = edits
            .Where(edit => string.Equals(edit.Domain, EncountersEditDomain, StringComparison.Ordinal))
            .Select(edit => SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out _)
                ? tableId
                : null)
            .Where(tableId => !string.IsNullOrWhiteSpace(tableId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var tableId in touchedTableIds)
        {
            var table = workflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
            if (table is null)
            {
                continue;
            }

            var totalProbability = table.Slots.Sum(slot => slot.Weight);
            if (totalProbability == 100)
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter table '{table.Location}' {table.Area} {table.EncounterType} probabilities total {totalProbability}, but must total 100.",
                field: SwShEncountersWorkflowService.ProbabilityField,
                expected: "Slot probabilities total exactly 100"));
        }
    }

    private static void ValidateNoEmptyWeightedSlots(
        SwShEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var touchedTableIds = edits
            .Where(edit => string.Equals(edit.Domain, EncountersEditDomain, StringComparison.Ordinal))
            .Select(edit => SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out _)
                ? tableId
                : null)
            .Where(tableId => !string.IsNullOrWhiteSpace(tableId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var tableId in touchedTableIds)
        {
            var table = workflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
            if (table is null)
            {
                continue;
            }

            foreach (var slot in table.Slots.Where(slot => slot.SpeciesId == 0 && slot.Weight > 0))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Encounter table '{table.Location}' {table.Area} {table.EncounterType} slot {slot.Slot} is empty but has {slot.Weight}% probability.",
                    field: SwShEncountersWorkflowService.SpeciesIdField,
                    expected: "Empty encounter slots must remain at 0% probability"));
            }
        }
    }

    private static PendingEdit? CreatePendingEdit(
        SwShEncounterTableRecord table,
        SwShEncounterSlotRecord slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!SwShEncountersWorkflowService.IsEditableField(normalizedField))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = TryParseValue(normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (normalizedField == SwShEncountersWorkflowService.LevelMinField && parsedValue.Value > slot.LevelMax)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter minimum level cannot be greater than the current maximum level.",
                field: normalizedField,
                expected: "Min level less than or equal to max level"));
            return null;
        }

        if (normalizedField == SwShEncountersWorkflowService.LevelMaxField && parsedValue.Value < slot.LevelMin)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter maximum level cannot be less than the current minimum level.",
                field: normalizedField,
                expected: "Max level greater than or equal to min level"));
            return null;
        }

        return new PendingEdit(
            EncountersEditDomain,
            CreateSummary(table, slot, normalizedField, parsedValue.Value),
            [new ProjectFileReference(table.Provenance.SourceLayer, table.Provenance.SourceFile)],
            RecordId: SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, slot.Slot),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string CreateSummary(
        SwShEncounterTableRecord table,
        SwShEncounterSlotRecord slot,
        string field,
        int value)
    {
        return field switch
        {
            SwShEncountersWorkflowService.SpeciesIdField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} {table.EncounterType} slot {slot.Slot} species ID to {value}.",
            SwShEncountersWorkflowService.FormField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} {table.EncounterType} slot {slot.Slot} form to {value}.",
            SwShEncountersWorkflowService.ProbabilityField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} {table.EncounterType} slot {slot.Slot} probability to {value}.",
            SwShEncountersWorkflowService.LevelMinField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} minimum level to {value}.",
            SwShEncountersWorkflowService.LevelMaxField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} maximum level to {value}.",
            _ => $"Set {table.Location} encounter {field} to {value}.",
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
                "Encounter edit value must be an integer.",
                field: field,
                expected: "Integer value"));
            return null;
        }

        var (minimum, maximum) = GetFieldRange(field);
        if (parsedValue < minimum || parsedValue > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter {field} must be between {minimum} and {maximum}.",
                field: field,
                expected: "Safe encounter value"));
            return null;
        }

        return parsedValue;
    }

    private static (int Minimum, int Maximum) GetFieldRange(string? field)
    {
        return field switch
        {
            SwShEncountersWorkflowService.SpeciesIdField =>
                (SwShEncountersWorkflowService.MinimumSpeciesId, SwShEncountersWorkflowService.MaximumSpeciesId),
            SwShEncountersWorkflowService.FormField =>
                (SwShEncountersWorkflowService.MinimumForm, SwShEncountersWorkflowService.MaximumForm),
            SwShEncountersWorkflowService.ProbabilityField =>
                (SwShEncountersWorkflowService.MinimumProbability, SwShEncountersWorkflowService.MaximumProbability),
            SwShEncountersWorkflowService.LevelMinField =>
                (SwShEncountersWorkflowService.MinimumLevel, SwShEncountersWorkflowService.MaximumLevel),
            SwShEncountersWorkflowService.LevelMaxField =>
                (SwShEncountersWorkflowService.MinimumLevel, SwShEncountersWorkflowService.MaximumLevel),
            _ => (0, 0),
        };
    }

    private static EditSession ReplacePendingEncounterEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameEncounterEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameEncounterEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        if (!string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            || !string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal))
        {
            return false;
        }

        if (candidate.Field is SwShEncountersWorkflowService.LevelMinField or SwShEncountersWorkflowService.LevelMaxField
            && SwShEncountersWorkflowService.TryParseSlotRecordId(candidate.RecordId, out var candidateTableId, out _)
            && SwShEncountersWorkflowService.TryParseSlotRecordId(pendingEdit.RecordId, out var pendingTableId, out _))
        {
            return IsSameEncounterZoneTable(candidateTableId, pendingTableId);
        }

        return string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal);
    }

    private static bool IsSameEncounterZoneTable(string leftTableId, string rightTableId)
    {
        return SwShEncountersWorkflowService.TryParseTableId(
                leftTableId,
                out var leftMember,
                out var leftTableIndex,
                out var leftZoneId,
                out _)
            && SwShEncountersWorkflowService.TryParseTableId(
                rightTableId,
                out var rightMember,
                out var rightTableIndex,
                out var rightZoneId,
                out _)
            && string.Equals(leftMember.FileName, rightMember.FileName, StringComparison.Ordinal)
            && leftTableIndex == rightTableIndex
            && leftZoneId == rightZoneId;
    }

    private static SwShEncountersWorkflow OverlayPendingEdits(
        SwShEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShEncountersWorkflow OverlayPendingEdit(SwShEncountersWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, EncountersEditDomain, StringComparison.Ordinal)
            || !SwShEncountersWorkflowService.IsEditableField(edit.Field)
            || !SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || TryParseValue(edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        var isZoneLevelEdit = edit.Field is SwShEncountersWorkflowService.LevelMinField
            or SwShEncountersWorkflowService.LevelMaxField;

        return workflow with
        {
            Tables = workflow.Tables
                .Select(table => (isZoneLevelEdit
                        ? IsSameEncounterZoneTable(table.TableId, tableId)
                        : table.TableId == tableId)
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

    private static SwShEncounterSlotRecord OverlaySlot(
        SwShEncountersWorkflow workflow,
        SwShEncounterSlotRecord slotRecord,
        int targetSlot,
        string field,
        int value)
    {
        return field switch
        {
            SwShEncountersWorkflowService.SpeciesIdField when slotRecord.Slot == targetSlot =>
                slotRecord with
                {
                    SpeciesId = value,
                    Species = SwShEncountersWorkflowService.FormatEncounterSpeciesLabel(
                        value,
                        slotRecord.Form,
                        ResolveSpeciesName(workflow, value)),
                },
            SwShEncountersWorkflowService.FormField when slotRecord.Slot == targetSlot =>
                slotRecord with
                {
                    Form = value,
                    Species = SwShEncountersWorkflowService.FormatEncounterSpeciesLabel(
                        slotRecord.SpeciesId,
                        value,
                        ResolveSpeciesName(workflow, slotRecord.SpeciesId)),
                },
            SwShEncountersWorkflowService.ProbabilityField when slotRecord.Slot == targetSlot =>
                slotRecord with { Weight = value },
            SwShEncountersWorkflowService.LevelMinField =>
                slotRecord with { LevelMin = value },
            SwShEncountersWorkflowService.LevelMaxField =>
                slotRecord with { LevelMax = value },
            _ => slotRecord,
        };
    }

    private static string ResolveSpeciesName(SwShEncountersWorkflow workflow, int speciesId)
    {
        if (speciesId == 0)
        {
            return "Empty";
        }

        var speciesField = workflow.EditableFields.FirstOrDefault(field =>
            string.Equals(field.Field, SwShEncountersWorkflowService.SpeciesIdField, StringComparison.Ordinal));
        var option = speciesField?.Options.FirstOrDefault(candidate => candidate.Value == speciesId);
        if (option is null)
        {
            return $"Species {speciesId}";
        }

        var prefix = speciesId.ToString("000", CultureInfo.InvariantCulture);
        return option.Label.StartsWith(prefix, StringComparison.Ordinal)
            ? option.Label[prefix.Length..].Trim()
            : option.Label;
    }

    private static string GetArchiveMemberFileName(PendingEdit edit)
    {
        if (!SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out _)
            || !SwShEncountersWorkflowService.TryParseTableId(tableId, out var member, out _, out _, out _))
        {
            return string.Empty;
        }

        return member.FileName;
    }

    private static IReadOnlyList<SwShWildEncounterEdit> ToArchiveEdits(
        SwShWildEncounterArchive archive,
        PendingEdit edit,
        IReadOnlyDictionary<int, IReadOnlySet<int>> availableSubTableIndexes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || !SwShEncountersWorkflowService.TryParseTableId(
                tableId,
                out _,
                out var tableIndex,
                out var zoneId,
                out var subTableIndex)
            || TryParseValue(edit.Field, edit.NewValue, diagnostics) is not { } value)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit does not include a valid archive target.",
                expected: "Existing encounter archive target"));
            return [];
        }

        if ((uint)tableIndex >= (uint)archive.Tables.Count
            || archive.Tables[tableIndex].ZoneId != zoneId
            || (uint)subTableIndex >= (uint)archive.Tables[tableIndex].SubTables.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit target no longer matches the source archive.",
                expected: "Current encounter archive target"));
            return [];
        }

        var field = ToArchiveField(edit.Field);
        if (field is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return [];
        }

        if (field is SwShWildEncounterField.LevelMin or SwShWildEncounterField.LevelMax)
        {
            if (!availableSubTableIndexes.TryGetValue(tableIndex, out var targetSubTableIndexes)
                || targetSubTableIndexes.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending encounter level edit no longer matches an editable vanilla encounter section.",
                    expected: "Vanilla-available encounter subtables"));
                return [];
            }

            return targetSubTableIndexes
                .Order()
                .Select(targetSubTableIndex => new SwShWildEncounterEdit(
                    tableIndex,
                    targetSubTableIndex,
                    SlotIndex: null,
                    field.Value,
                    value))
                .ToArray();
        }

        return
        [
            new SwShWildEncounterEdit(tableIndex, subTableIndex, slot - 1, field.Value, value)
        ];
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<int>> CreateAvailableSubTableIndexLookup(
        SwShEncountersWorkflow workflow,
        string archiveMember)
    {
        var lookup = new Dictionary<int, HashSet<int>>();
        foreach (var table in workflow.Tables.Where(table => string.Equals(table.ArchiveMember, archiveMember, StringComparison.Ordinal)))
        {
            if (!SwShEncountersWorkflowService.TryParseTableId(
                    table.TableId,
                    out _,
                    out var tableIndex,
                    out _,
                    out var subTableIndex))
            {
                continue;
            }

            if (!lookup.TryGetValue(tableIndex, out var subTableIndexes))
            {
                subTableIndexes = [];
                lookup.Add(tableIndex, subTableIndexes);
            }

            subTableIndexes.Add(subTableIndex);
        }

        return lookup.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<int>)pair.Value);
    }

    private static SwShWildEncounterField? ToArchiveField(string? field)
    {
        return field switch
        {
            SwShEncountersWorkflowService.SpeciesIdField => SwShWildEncounterField.SpeciesId,
            SwShEncountersWorkflowService.FormField => SwShWildEncounterField.Form,
            SwShEncountersWorkflowService.ProbabilityField => SwShWildEncounterField.Probability,
            SwShEncountersWorkflowService.LevelMinField => SwShWildEncounterField.LevelMin,
            SwShEncountersWorkflowService.LevelMaxField => SwShWildEncounterField.LevelMax,
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
                "Encounters apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounters apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShEncountersWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounters apply target must stay inside the configured output root.",
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
            $"Encounter field '{field}' is not supported by the Encounters workflow yet.",
            field: "field",
            expected: string.Join(
                ", ",
                [
                    SwShEncountersWorkflowService.SpeciesIdField,
                    SwShEncountersWorkflowService.FormField,
                    SwShEncountersWorkflowService.ProbabilityField,
                    SwShEncountersWorkflowService.LevelMinField,
                    SwShEncountersWorkflowService.LevelMaxField,
                ]));
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: EncountersEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record LevelPair(int LevelMin, int LevelMax);
}
