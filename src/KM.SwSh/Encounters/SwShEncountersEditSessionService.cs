// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
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
                $"Encounter table {FormatEncounterTableIdContext(tableId)} is not present in the loaded workflow.",
                field: "tableId",
                expected: "Existing encounter table"));
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var slotRecord = table.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (slotRecord is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter table {FormatEncounterTableContext(table)} does not have slot {slot}.",
                field: "slot",
                expected: "Existing encounter slot"));
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(
            workflow,
            table,
            slotRecord,
            field,
            value,
            diagnostics: diagnostics);
        if (pendingEdit is null)
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingEncounterEdit(currentSession, pendingEdit);
        var projectedWorkflow = OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits);
        ValidatePendingLevelPairs(
            loadedWorkflow,
            projectedWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        return new SwShEncountersEditResult(
            projectedWorkflow,
            updatedSession,
            diagnostics);
    }

    public SwShEncountersEditResult UpdateSlotFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShEncounterSlotFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditEncounters(project, workflow, diagnostics))
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.TableId)
                || string.IsNullOrWhiteSpace(update.Field)
                || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Encounter batch update is missing a table, field, or value.",
                    field: "updates",
                    expected: "Complete encounter slot field update"));
                continue;
            }

            var table = effectiveWorkflow.Tables.FirstOrDefault(candidate => candidate.TableId == update.TableId);
            if (table is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Encounter table {FormatEncounterTableIdContext(update.TableId)} is not present in the loaded workflow.",
                    field: "tableId",
                    expected: "Existing encounter table"));
                continue;
            }

            var slotRecord = table.Slots.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            if (slotRecord is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Encounter table {FormatEncounterTableContext(table)} does not have slot {update.Slot}.",
                    field: "slot",
                    expected: "Existing encounter slot"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                effectiveWorkflow,
                table,
                slotRecord,
                update.Field,
                update.Value,
                diagnostics: diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ReplacePendingEncounterEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
        }

        ValidatePendingLevelPairs(
            loadedWorkflow,
            effectiveWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

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
        ValidatePendingLevelPairs(workflow, workflowWithPendingEdits, session.PendingEdits, diagnostics);
        ValidateEncounterProbabilityTotals(workflow, workflowWithPendingEdits, session.PendingEdits, diagnostics);
        ValidateNoEmptyWeightedSlots(workflow, workflowWithPendingEdits, session.PendingEdits, diagnostics);
        ValidateEmptySlotForms(workflow, workflowWithPendingEdits, session.PendingEdits, diagnostics);

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

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, [write], diagnostics));
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

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var applyScope,
                out var scopeDiagnostics))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, scopeDiagnostics);
        }

        using var verifiedApply = applyScope!;
        paths = verifiedApply.ApplyPaths;
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

            foreach (var editGroup in session.PendingEdits.GroupBy(GetArchiveMemberFileName, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(editGroup.Key))
                {
                    var targets = string.Join(", ", editGroup.Select(FormatPendingEditTarget).Take(3));
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Pending encounter edits target invalid archive members: {targets}.",
                        expected: "Known Sword/Shield encounter member"));
                    continue;
                }

                var archive = SwShWildEncounterArchive.Parse(pack.GetFileByName(editGroup.Key));
                var archiveEdits = editGroup
                    .SelectMany(edit => ToArchiveEdits(archive, edit, diagnostics))
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

        return verifiedApply.Commit(
            CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics));
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
                $"Pending encounter edit targets invalid encounter record '{edit.RecordId ?? "(missing)"}'.",
                field: "slot",
                expected: "Encounter slot"));
            return;
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending encounter edit targets table {FormatEncounterTableIdContext(tableId)}, which is not loaded.",
                field: "tableId",
                expected: "Existing encounter table"));
            return;
        }

        var slotRecord = table.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (slotRecord is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending encounter edit targets {FormatEncounterTableContext(table)} slot {slot}, which is not loaded.",
                field: "slot",
                expected: "Existing encounter slot"));
            return;
        }

        var value = TryParseValue(
            edit.Field,
            edit.NewValue,
            diagnostics,
            FormatEncounterSlotContext(table, slotRecord));
        if (value is not null)
        {
            ValidateSpeciesAvailability(workflow, edit.Field, value.Value, diagnostics);
        }
    }

    private static void ValidatePendingLevelPairs(
        SwShEncountersWorkflow sourceWorkflow,
        SwShEncountersWorkflow projectedWorkflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var (sourceTable, projectedTable) in GetTouchedEncounterTablePairs(
                     sourceWorkflow,
                     projectedWorkflow,
                     edits))
        {
            var sourceLevels = GetEncounterTableLevelState(sourceTable);
            var projectedLevels = GetEncounterTableLevelState(projectedTable);
            var sourceViolation = Math.Max(0, sourceLevels.LevelMin - sourceLevels.LevelMax);
            var projectedViolation = Math.Max(0, projectedLevels.LevelMin - projectedLevels.LevelMax);
            if (projectedViolation <= sourceViolation)
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter table {FormatEncounterTableContext(projectedTable)} has minimum level {projectedLevels.LevelMin} greater than maximum level {projectedLevels.LevelMax}. Pending edits increase the invalid level gap from {sourceViolation} to {projectedViolation}.",
                field: "level",
                expected: "Min level less than or equal to max level"));
        }
    }

    private static void ValidateEncounterProbabilityTotals(
        SwShEncountersWorkflow sourceWorkflow,
        SwShEncountersWorkflow projectedWorkflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var (sourceTable, projectedTable) in GetTouchedEncounterTablePairs(
                     sourceWorkflow,
                     projectedWorkflow,
                     edits))
        {
            var sourceTotal = sourceTable.Slots.Sum(slot => slot.Weight);
            var projectedTotal = projectedTable.Slots.Sum(slot => slot.Weight);
            var sourceDeviation = Math.Abs(sourceTotal - 100);
            var projectedDeviation = Math.Abs(projectedTotal - 100);
            if (projectedDeviation <= sourceDeviation)
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounter table {FormatEncounterTableContext(projectedTable)} has probabilities totaling {projectedTotal}, but they must total 100. Pending edits increase the difference from the source total of {sourceTotal}.",
                field: SwShEncountersWorkflowService.ProbabilityField,
                expected: "Slot probabilities total exactly 100"));
        }
    }

    private static void ValidateNoEmptyWeightedSlots(
        SwShEncountersWorkflow sourceWorkflow,
        SwShEncountersWorkflow projectedWorkflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var (sourceTable, projectedTable) in GetTouchedEncounterTablePairs(
                     sourceWorkflow,
                     projectedWorkflow,
                     edits))
        {
            var sourceSlots = sourceTable.Slots.ToDictionary(slot => slot.Slot);

            foreach (var slot in projectedTable.Slots.Where(slot => slot.SpeciesId == 0 && slot.Weight > 0))
            {
                var sourceWeight = sourceSlots.TryGetValue(slot.Slot, out var sourceSlot)
                    && sourceSlot.SpeciesId == 0
                        ? sourceSlot.Weight
                        : 0;
                if (slot.Weight <= sourceWeight)
                {
                    continue;
                }

                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Encounter table {FormatEncounterTableContext(projectedTable)} slot {slot.Slot} is empty but has {slot.Weight}% probability. Pending edits increase its empty-slot probability from {sourceWeight}%.",
                    field: SwShEncountersWorkflowService.SpeciesIdField,
                    expected: "Empty encounter slots must remain at 0% probability"));
            }
        }
    }

    private static void ValidateEmptySlotForms(
        SwShEncountersWorkflow sourceWorkflow,
        SwShEncountersWorkflow projectedWorkflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var (sourceTable, projectedTable) in GetTouchedEncounterTablePairs(
                     sourceWorkflow,
                     projectedWorkflow,
                     edits))
        {
            var sourceSlots = sourceTable.Slots.ToDictionary(slot => slot.Slot);

            foreach (var slot in projectedTable.Slots.Where(slot => slot.SpeciesId == 0 && slot.Form != 0))
            {
                if (sourceSlots.TryGetValue(slot.Slot, out var sourceSlot)
                    && sourceSlot.SpeciesId == 0
                    && sourceSlot.Form != 0)
                {
                    continue;
                }

                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Encounter table {FormatEncounterTableContext(projectedTable)} slot {slot.Slot} is empty but still uses form {slot.Form}. Pending edits introduce an invalid empty-slot form.",
                    field: SwShEncountersWorkflowService.FormField,
                    expected: "Empty encounter slots must use form 0"));
            }
        }
    }

    private static IEnumerable<(SwShEncounterTableRecord Source, SwShEncounterTableRecord Projected)>
        GetTouchedEncounterTablePairs(
            SwShEncountersWorkflow sourceWorkflow,
            SwShEncountersWorkflow projectedWorkflow,
            IEnumerable<PendingEdit> edits)
    {
        var sourceTables = sourceWorkflow.Tables.ToDictionary(table => table.TableId, StringComparer.Ordinal);
        var projectedTables = projectedWorkflow.Tables.ToDictionary(table => table.TableId, StringComparer.Ordinal);
        var touchedTableIds = edits
            .Where(edit => string.Equals(edit.Domain, EncountersEditDomain, StringComparison.Ordinal))
            .Select(edit => SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out _)
                ? tableId
                : null)
            .Where(tableId => !string.IsNullOrWhiteSpace(tableId))
            .Distinct(StringComparer.Ordinal);

        foreach (var tableId in touchedTableIds)
        {
            if (sourceTables.TryGetValue(tableId!, out var sourceTable)
                && projectedTables.TryGetValue(tableId!, out var projectedTable))
            {
                yield return (sourceTable, projectedTable);
            }
        }
    }

    private static EncounterTableLevelState GetEncounterTableLevelState(SwShEncounterTableRecord table)
    {
        var firstSlot = table.Slots.FirstOrDefault();
        return new EncounterTableLevelState(firstSlot?.LevelMin ?? 0, firstSlot?.LevelMax ?? 0);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShEncountersWorkflow workflow,
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

        var parsedValue = TryParseValue(normalizedField, value, diagnostics, FormatEncounterSlotContext(table, slot));
        if (parsedValue is null)
        {
            return null;
        }

        if (!ValidateSpeciesAvailability(workflow, normalizedField, parsedValue.Value, diagnostics))
        {
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
        ICollection<ValidationDiagnostic> diagnostics,
        string? targetContext = null)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                targetContext is null
                    ? "Encounter edit value must be an integer."
                    : $"Encounter table {targetContext} {field} value must be an integer.",
                field: field,
                expected: "Integer value"));
            return null;
        }

        var (minimum, maximum) = GetFieldRange(field);
        if (parsedValue < minimum || parsedValue > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                targetContext is null
                    ? $"Encounter {field} must be between {minimum} and {maximum}."
                    : $"Encounter table {targetContext} {field} must be between {minimum} and {maximum}.",
                field: field,
                expected: "Safe encounter value"));
            return null;
        }

        return parsedValue;
    }

    private static bool ValidateSpeciesAvailability(
        SwShEncountersWorkflow workflow,
        string? field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field != SwShEncountersWorkflowService.SpeciesIdField
            || value == 0
            || workflow.PresentSpeciesIds.Count == 0
            || workflow.PresentSpeciesIds.Contains(value))
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Encounter species {value} is not marked present in the loaded Sword/Shield personal data.",
            field: SwShEncountersWorkflowService.SpeciesIdField,
            expected: "Empty or a Pokemon present in Sword/Shield"));
        return false;
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
            return string.Equals(candidateTableId, pendingTableId, StringComparison.Ordinal);
        }

        return string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal);
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

        var isTableLevelEdit = edit.Field is SwShEncountersWorkflowService.LevelMinField
            or SwShEncountersWorkflowService.LevelMaxField;

        return workflow with
        {
            Tables = workflow.Tables
                .Select(table => (isTableLevelEdit
                        ? string.Equals(table.TableId, tableId, StringComparison.Ordinal)
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

    private static string FormatEncounterTableContext(SwShEncounterTableRecord table)
    {
        return $"{table.GameVersion} {table.Location} {table.Area} {table.EncounterType}";
    }

    private static string FormatEncounterTableIdContext(string tableId)
    {
        if (!SwShEncountersWorkflowService.TryParseTableId(
                tableId,
                out var member,
                out var tableIndex,
                out var zoneId,
                out var subTableIndex))
        {
            return $"'{tableId}'";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{member.GameLabel} {member.AreaLabel} table {tableIndex} zone 0x{zoneId:X16} subtable {subTableIndex}");
    }

    private static string FormatEncounterSlotContext(
        SwShEncounterTableRecord table,
        SwShEncounterSlotRecord slot)
    {
        return $"{FormatEncounterTableContext(table)} slot {slot.Slot}";
    }

    private static string FormatPendingEditTarget(PendingEdit edit)
    {
        if (!SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot))
        {
            return edit.RecordId ?? "(missing encounter record)";
        }

        if (!SwShEncountersWorkflowService.TryParseTableId(
                tableId,
                out var member,
                out var tableIndex,
                out var zoneId,
                out var subTableIndex))
        {
            return $"{tableId} slot {slot}";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{member.GameLabel} {member.AreaLabel} table {tableIndex} zone 0x{zoneId:X16} subtable {subTableIndex} slot {slot}");
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
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShEncountersWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || !SwShEncountersWorkflowService.TryParseTableId(
                tableId,
                out _,
                out var tableIndex,
                out var zoneId,
                out var subTableIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending encounter edit target '{FormatPendingEditTarget(edit)}' is not a valid archive target.",
                expected: "Existing encounter archive target"));
            return [];
        }

        if (TryParseValue(edit.Field, edit.NewValue, diagnostics, FormatPendingEditTarget(edit)) is not { } value)
        {
            return [];
        }

        if ((uint)tableIndex >= (uint)archive.Tables.Count
            || archive.Tables[tableIndex].ZoneId != zoneId
            || (uint)subTableIndex >= (uint)archive.Tables[tableIndex].SubTables.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending encounter edit target {FormatPendingEditTarget(edit)} no longer matches the source archive.",
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
            return
            [
                new SwShWildEncounterEdit(
                    tableIndex,
                    subTableIndex,
                    SlotIndex: null,
                    field.Value,
                    value),
            ];
        }

        return
        [
            new SwShWildEncounterEdit(tableIndex, subTableIndex, slot - 1, field.Value, value)
        ];
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

    private sealed record EncounterTableLevelState(int LevelMin, int LevelMax);
}
