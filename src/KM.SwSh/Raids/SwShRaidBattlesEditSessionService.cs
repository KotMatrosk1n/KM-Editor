// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Raids;

public sealed class SwShRaidBattlesEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShRaidBattlesWorkflowService raidBattlesWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShRaidBattlesEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRaidBattlesWorkflowService? raidBattlesWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.raidBattlesWorkflowService = raidBattlesWorkflowService ?? new SwShRaidBattlesWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShRaidBattlesEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShRaidBattlesWorkflowService? raidBattlesWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.raidBattlesWorkflowService = raidBattlesWorkflowService ?? new SwShRaidBattlesWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
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
        return UpdateSlotFields(
            paths,
            session,
            [new SwShRaidBattleFieldUpdate(tableId, slot, field, value)]);
    }

    public SwShRaidBattlesEditResult UpdateSlotFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShRaidBattleFieldUpdate?>? updates)
    {
        ArgumentNullException.ThrowIfNull(paths);

        projectWorkspaceService.ClearMemoryCache();
        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = raidBattlesWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(loadedWorkflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditRaidBattles(project, loadedWorkflow, diagnostics))
        {
            return new SwShRaidBattlesEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates is null || updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Update at least one Raid Battles field.",
                field: "updates",
                expected: "One or more raid battle field updates"));
            return new SwShRaidBattlesEditResult(originalWorkflow, originalSession, diagnostics);
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
                    DiagnosticSeverity.Error,
                    "Raid battle update fields are required.",
                    field: "updates",
                    expected: "Non-null canonical raid battle update"));
                break;
            }

            if (!string.Equals(update.Field, update.Field.Trim(), StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Raid battle field must use canonical text without surrounding whitespace.",
                    field: "field",
                    expected: update.Field.Trim()));
                break;
            }

            if (!seenUpdates.Add((update.TableId, update.Slot, update.Field)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Raid battle batch contains the same field more than once.",
                    field: update.Field,
                    expected: "One value per raid battle field"));
                break;
            }

            var table = effectiveWorkflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, update.TableId, StringComparison.Ordinal));
            var sourceTable = loadedWorkflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, update.TableId, StringComparison.Ordinal));
            if (table is null || sourceTable is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Raid battle table '{update.TableId}' is not present in the current source.",
                    field: "tableId",
                    expected: "Current signed raid battle table"));
                break;
            }

            var slotRecord = table.Slots.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            var sourceSlot = sourceTable.Slots.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            if (slotRecord is null || sourceSlot is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Raid battle table '{table.DenId}' does not have slot {update.Slot}.",
                    field: "slot",
                    expected: "Existing one-based raid battle slot"));
                break;
            }

            var recordId = SwShRaidBattlesWorkflowService.CreateSlotRecordId(table.TableId, slotRecord.Slot);
            var sourceValue = GetSlotFieldValue(sourceSlot, update.Field);
            if (sourceValue is not null && string.Equals(sourceValue, update.Value, StringComparison.Ordinal))
            {
                workingSession = RemovePendingRaidBattleEdit(
                    workingSession,
                    recordId,
                    update.Field);
                effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, workingSession.PendingEdits);
                continue;
            }

            var pendingEdit = CreatePendingEdit(table, slotRecord, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                break;
            }

            pendingEdit = AddSemanticValidationSources(project, pendingEdit);
            workingSession = ReplacePendingRaidBattleEdit(workingSession, pendingEdit);
            if (GetSlotFieldValue(sourceSlot, pendingEdit.Field) == pendingEdit.NewValue)
            {
                workingSession = RemovePendingRaidBattleEdit(workingSession, pendingEdit);
            }

            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, workingSession.PendingEdits);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRaidBattlesEditResult(originalWorkflow, originalSession, diagnostics);
        }

        ValidateLoadedSession(
            project,
            loadedWorkflow,
            workingSession,
            diagnostics,
            addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRaidBattlesEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShRaidBattlesEditResult(
            OverlayPendingEdits(loadedWorkflow, workingSession.PendingEdits),
            workingSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        if (!OwnsDirectSession(session))
        {
            return new SwShEditSessionValidation(
                session,
                IsValid: false,
                [CreateDirectDomainOwnershipDiagnostic(session)]);
        }

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = raidBattlesWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (CanEditRaidBattles(project, workflow, diagnostics))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);
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

        if (!OwnsDirectSession(session))
        {
            return new ChangePlan(
                session.Id,
                Array.Empty<PlannedFileWrite>(),
                [CreateDirectDomainOwnershipDiagnostic(session)]);
        }

        projectWorkspaceService.ClearMemoryCache();
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        var raidBattleEdits = GetRaidBattleEdits(session).ToArray();

        if (raidBattleEdits.Length == 0)
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

        var targetPath = ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath, diagnostics);
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
            raidBattleEdits
                .SelectMany(edit => edit.Sources)
                .Append(new ProjectFileReference(GetSourceLayer(dataSource.GraphEntry), dataSource.GraphEntry.RelativePath))
                .Distinct()
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray(),
            File.Exists(targetPath),
            raidBattleEdits.Length == 1
                ? $"Apply pending Raid Battles edit: {raidBattleEdits[0].Summary}"
                : $"Apply {raidBattleEdits.Length} pending Raid Battles edits.");

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
        if (!OwnsDirectSession(session))
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

        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Raid Battles change plan"));
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

        byte[] output;
        try
        {
            output = CreateArchiveOutput(dataSource.AbsolutePath, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            VerifyOutput(output, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles source file could not be decoded or safely edited: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield raid battle data"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles source file could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield raid battle data"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (!SwShOutputRollbackScope.TryCapture(
                paths,
                currentPlan.Writes.Select(write => write.TargetRelativePath),
                out var rollbackScope,
                out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
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
                    DiagnosticSeverity.Error,
                    $"Raid Battles output file could not be written: {exception.Message}",
                    file: dataSource.GraphEntry.RelativePath,
                    expected: "Writable output root"));
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
        }

        if (writtenFiles.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Raid Battles change plan to the configured LayeredFS output root."));
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

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShRaidBattlesWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var effectiveWorkflow = workflow;
        var seen = new HashSet<(string RecordId, string Field)>();
        var semanticRecords = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var probabilityColumns = new HashSet<(string TableId, int ProbabilityIndex)>();

        foreach (var edit in GetRaidBattleEdits(session))
        {
            if (!seen.Add((edit.RecordId ?? string.Empty, edit.Field ?? string.Empty)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "A raid battle field has more than one pending value.",
                    field: edit.Field,
                    expected: "One pending value per raid battle field"));
                continue;
            }

            var errorsBefore = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(workflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) != errorsBefore)
            {
                continue;
            }

            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            if (edit.RecordId is { } recordId && edit.Field is { } field)
            {
                if (IsSemanticField(field))
                {
                    if (!semanticRecords.TryGetValue(recordId, out var fields))
                    {
                        fields = new HashSet<string>(StringComparer.Ordinal);
                        semanticRecords.Add(recordId, fields);
                    }

                    fields.Add(field);
                }

                var probabilityIndex = GetProbabilityIndex(field);
                if (probabilityIndex is not null
                    && SwShRaidBattlesWorkflowService.TryParseSlotRecordId(recordId, out var tableId, out _))
                {
                    probabilityColumns.Add((tableId, probabilityIndex.Value));
                }
            }
        }

        ValidateSemanticRecords(effectiveWorkflow, semanticRecords, diagnostics);
        ValidateProbabilityColumns(effectiveWorkflow, probabilityColumns, diagnostics);

        if (GetRaidBattleEdits(session).Any()
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightArchiveWrite(project, session, diagnostics);
        }

        if (GetRaidBattleEdits(session).Any()
            && addSuccessDiagnostic
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending raid battle change is valid."));
        }
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

        var table = workflow.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
        if (table is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending raid battle table '{tableId}' is no longer available or no longer matches its source contents.",
                expected: "Current signed raid battle table"));
            return;
        }

        var slotRecord = table.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (slotRecord is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending raid battle slot {slot} is no longer available.",
                expected: "Current raid battle slot"));
            return;
        }

        var canonicalRecordId = SwShRaidBattlesWorkflowService.CreateSlotRecordId(table.TableId, slot);
        if (!string.Equals(edit.RecordId, canonicalRecordId, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid battle edit does not use the canonical slot identity.",
                field: "recordId",
                expected: canonicalRecordId));
            return;
        }

        var sourceValue = GetSlotFieldValue(slotRecord, edit.Field);
        _ = TryParseValue(edit.Field, edit.NewValue, diagnostics, sourceValue);
    }

    private static void ValidateSemanticRecords(
        SwShRaidBattlesWorkflow workflow,
        IReadOnlyDictionary<string, HashSet<string>> records,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (records.Count == 0)
        {
            return;
        }

        if (workflow.PersonalRecords.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid battle species, form, ability, gender, and Gigantamax validation requires the Sword/Shield personal data table.",
                field: SwShRaidBattlesWorkflowService.SpeciesField,
                expected: SwShPersonalTable.PersonalDataRelativePath));
            return;
        }

        foreach (var (recordId, fields) in records)
        {
            if (!SwShRaidBattlesWorkflowService.TryParseSlotRecordId(recordId, out var tableId, out var slot))
            {
                continue;
            }

            var battle = workflow.Tables
                .FirstOrDefault(table => string.Equals(table.TableId, tableId, StringComparison.Ordinal))?
                .Slots.FirstOrDefault(candidate => candidate.Slot == slot);
            if (battle is null)
            {
                continue;
            }

            if (battle.SpeciesId <= 0 || (uint)battle.SpeciesId >= (uint)workflow.PersonalRecords.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Raid battle slot {slot} does not target a species available in the loaded Sword/Shield personal data.",
                    field: SwShRaidBattlesWorkflowService.SpeciesField,
                    expected: "Species present in Sword/Shield personal data"));
                continue;
            }

            var basePersonal = workflow.PersonalRecords[battle.SpeciesId];
            var formCount = Math.Max(1, basePersonal.FormCount);
            if (battle.Form < 0 || battle.Form >= formCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Raid battle slot {slot} uses form {battle.Form}, but species {battle.SpeciesId} exposes {formCount} supported form slot(s) in personal data.",
                    field: SwShRaidBattlesWorkflowService.FormField,
                    expected: $"Form 0 through {formCount - 1}"));
                continue;
            }

            var personal = workflow.AbilityResolver.ResolvePersonalRecord(battle.SpeciesId, battle.Form);
            if (personal is null || !personal.IsPresentInGame)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Raid battle slot {slot} uses a species/form that is not marked present in Sword/Shield personal data.",
                    field: SwShRaidBattlesWorkflowService.SpeciesField,
                    expected: "Species/form present in Sword/Shield personal data"));
                continue;
            }

            var identityChanged = fields.Contains(SwShRaidBattlesWorkflowService.SpeciesField)
                || fields.Contains(SwShRaidBattlesWorkflowService.FormField);
            if ((identityChanged || fields.Contains(SwShRaidBattlesWorkflowService.AbilityField))
                && !IsAvailableAbilityRoll(personal, battle.Ability))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Raid battle slot {slot} uses an ability roll unavailable for its species and form.",
                    field: SwShRaidBattlesWorkflowService.AbilityField,
                    expected: "Ability roll supported by the selected species and form"));
            }

            if ((identityChanged || fields.Contains(SwShRaidBattlesWorkflowService.GenderField))
                && !IsGenderCompatible(personal, battle.Gender))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Raid battle slot {slot} uses a gender unavailable for its species and form.",
                    field: SwShRaidBattlesWorkflowService.GenderField,
                    expected: "Gender supported by the selected species and form"));
            }

            if ((identityChanged || fields.Contains(SwShRaidBattlesWorkflowService.IsGigantamaxField))
                && battle.IsGigantamax)
            {
                if (personal.CanNotDynamax)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Raid battle slot {slot} is marked unable to Dynamax in Sword/Shield personal data.",
                        field: SwShRaidBattlesWorkflowService.IsGigantamaxField,
                        expected: "Species/form permitted to Dynamax or Gigantamax disabled"));
                }
                else if (!IsGigantamaxCapableSpeciesForm(battle.SpeciesId, battle.Form))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Raid battle slot {slot} does not use a Gigantamax-capable Sword/Shield species and form.",
                        field: SwShRaidBattlesWorkflowService.IsGigantamaxField,
                        expected: "Gigantamax-capable species/form or Gigantamax disabled"));
                }
            }
        }
    }

    private static void ValidateProbabilityColumns(
        SwShRaidBattlesWorkflow workflow,
        IReadOnlySet<(string TableId, int ProbabilityIndex)> columns,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var (tableId, probabilityIndex) in columns)
        {
            var table = workflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
            if (table is null)
            {
                continue;
            }

            var total = table.Slots.Sum(slot => (long)slot.Probabilities[probabilityIndex]);
            if (total is 0 or 100)
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid battle table {table.DisplayName} has a {probabilityIndex + 1}-star probability total of {total}; an active star rank must total 100%.",
                field: GetProbabilityField(probabilityIndex),
                expected: "0% for an unused rank or 100% across all slots"));
        }
    }

    private static bool IsAvailableAbilityRoll(SwShPersonalRecord personal, int ability)
    {
        return ability switch
        {
            0 => personal.Ability1 != 0,
            1 => personal.Ability2 != 0,
            2 => personal.HiddenAbility != 0,
            3 => personal.Ability1 != 0 || personal.Ability2 != 0,
            4 => personal.Ability1 != 0 || personal.Ability2 != 0 || personal.HiddenAbility != 0,
            _ => false,
        };
    }

    private static bool IsGenderCompatible(SwShPersonalRecord personal, int gender)
    {
        return gender switch
        {
            0 => true,
            1 => personal.GenderRatio is not 254 and not 255,
            2 => personal.GenderRatio is not 0 and not 255,
            3 => personal.GenderRatio == 255,
            _ => false,
        };
    }

    private static bool IsGigantamaxCapableSpeciesForm(int speciesId, int form)
    {
        if (speciesId is 25 or 52 && form != 0)
        {
            return false;
        }

        return SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(speciesId);
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
        ICollection<ValidationDiagnostic> diagnostics,
        string? sourceValue = null)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedValue)
            || !string.Equals(value, parsedValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid battle edit value must be a canonical integer.",
                field: field,
                expected: "Canonical integer text"));
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
        if ((parsedValue < minimum || parsedValue > maximum)
            && !string.Equals(value, sourceValue, StringComparison.Ordinal))
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

    private static EditSession RemovePendingRaidBattleEdit(
        EditSession session,
        string recordId,
        string field)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !string.Equals(
                        edit.Domain,
                        SwShRaidBattlesWorkflowService.RaidBattlesEditDomain,
                        StringComparison.Ordinal)
                    || !string.Equals(edit.RecordId, recordId, StringComparison.Ordinal)
                    || !string.Equals(edit.Field, field, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static string? GetSlotFieldValue(SwShRaidBattleSlotRecord slot, string? field)
    {
        var value = field switch
        {
            SwShRaidBattlesWorkflowService.SpeciesField => slot.SpeciesId,
            SwShRaidBattlesWorkflowService.FormField => slot.Form,
            SwShRaidBattlesWorkflowService.AbilityField => slot.Ability,
            SwShRaidBattlesWorkflowService.IsGigantamaxField => slot.IsGigantamax ? 1 : 0,
            SwShRaidBattlesWorkflowService.GenderField => slot.Gender,
            SwShRaidBattlesWorkflowService.FlawlessIvsField => slot.FlawlessIvs,
            SwShRaidBattlesWorkflowService.Star1ProbabilityField => slot.Probabilities[0],
            SwShRaidBattlesWorkflowService.Star2ProbabilityField => slot.Probabilities[1],
            SwShRaidBattlesWorkflowService.Star3ProbabilityField => slot.Probabilities[2],
            SwShRaidBattlesWorkflowService.Star4ProbabilityField => slot.Probabilities[3],
            SwShRaidBattlesWorkflowService.Star5ProbabilityField => slot.Probabilities[4],
            _ => (int?)null,
        };

        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsSemanticField(string field)
    {
        return field is
            SwShRaidBattlesWorkflowService.SpeciesField
            or SwShRaidBattlesWorkflowService.FormField
            or SwShRaidBattlesWorkflowService.AbilityField
            or SwShRaidBattlesWorkflowService.IsGigantamaxField
            or SwShRaidBattlesWorkflowService.GenderField;
    }

    private static int? GetProbabilityIndex(string? field)
    {
        return field switch
        {
            SwShRaidBattlesWorkflowService.Star1ProbabilityField => 0,
            SwShRaidBattlesWorkflowService.Star2ProbabilityField => 1,
            SwShRaidBattlesWorkflowService.Star3ProbabilityField => 2,
            SwShRaidBattlesWorkflowService.Star4ProbabilityField => 3,
            SwShRaidBattlesWorkflowService.Star5ProbabilityField => 4,
            _ => null,
        };
    }

    private static string GetProbabilityField(int probabilityIndex)
    {
        return probabilityIndex switch
        {
            0 => SwShRaidBattlesWorkflowService.Star1ProbabilityField,
            1 => SwShRaidBattlesWorkflowService.Star2ProbabilityField,
            2 => SwShRaidBattlesWorkflowService.Star3ProbabilityField,
            3 => SwShRaidBattlesWorkflowService.Star4ProbabilityField,
            4 => SwShRaidBattlesWorkflowService.Star5ProbabilityField,
            _ => throw new ArgumentOutOfRangeException(nameof(probabilityIndex)),
        };
    }

    private static PendingEdit AddSemanticValidationSources(OpenedProject project, PendingEdit pendingEdit)
    {
        if (!IsSemanticField(pendingEdit.Field ?? string.Empty))
        {
            return pendingEdit;
        }

        var personalEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(
                entry.RelativePath,
                SwShPersonalTable.PersonalDataRelativePath,
                StringComparison.OrdinalIgnoreCase));
        if (personalEntry is null)
        {
            return pendingEdit;
        }

        return pendingEdit with
        {
            Sources = pendingEdit.Sources
                .Append(new ProjectFileReference(GetSourceLayer(personalEntry), personalEntry.RelativePath))
                .Distinct()
                .ToArray(),
        };
    }

    private static EditSession RemovePendingRaidBattleEdit(EditSession session, PendingEdit pendingEdit)
    {
        return RemovePendingRaidBattleEdit(
            session,
            pendingEdit.RecordId ?? string.Empty,
            pendingEdit.Field ?? string.Empty);
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

        var updated = field switch
        {
            SwShRaidBattlesWorkflowService.SpeciesField => slotRecord with
            {
                SpeciesId = value,
                Species = GetSpeciesName(workflow, value),
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

        var formOptions = SwShRaidBattlesWorkflowService.CreateFormOptions(
            workflow.PersonalRecords,
            updated.SpeciesId,
            updated.Form);
        var abilityOptions = SwShRaidBattlesWorkflowService.CreateAbilityOptions(
            workflow.AbilityResolver,
            updated.SpeciesId,
            updated.Form);
        return updated with
        {
            FormOptions = formOptions,
            AbilityOptions = abilityOptions,
            AbilityLabel = SwShRaidBattlesWorkflowService.GetOptionLabel(
                abilityOptions,
                updated.Ability,
                "Ability roll"),
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

    private static string GetSpeciesName(SwShRaidBattlesWorkflow workflow, int speciesId)
    {
        var option = workflow.EditableFields
            .FirstOrDefault(field => field.Field == SwShRaidBattlesWorkflowService.SpeciesField)?
            .Options.FirstOrDefault(candidate => candidate.Value == speciesId);
        if (option is null)
        {
            return $"Species {speciesId.ToString(CultureInfo.InvariantCulture)}";
        }

        var prefix = speciesId.ToString(CultureInfo.InvariantCulture) + " ";
        return option.Label.StartsWith(prefix, StringComparison.Ordinal)
            ? option.Label[prefix.Length..]
            : option.Label;
    }

    private static SwShEncounterNestEdit? ToArchiveEdit(
        SwShEncounterNestArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShRaidBattlesWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || !SwShRaidBattlesWorkflowService.TryParseTableId(
                tableId,
                out var tableIndex,
                out var sourceTableId,
                out var sourceIdentity,
                out var isLegacy))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid battle edit does not include a valid archive target.",
                expected: "Existing raid battle archive target"));
            return null;
        }

        if (isLegacy)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Legacy pending raid battle identity is not signed by its source contents. Stage the edit again.",
                field: "tableId",
                expected: "Current signed raid battle table identity"));
            return null;
        }

        if ((uint)tableIndex >= (uint)archive.Tables.Count
            || archive.Tables[tableIndex].TableId != sourceTableId
            || !string.Equals(
                SwShRaidBattlesWorkflowService.CreateTableSourceIdentity(
                    tableIndex,
                    archive.Tables[tableIndex]),
                sourceIdentity,
                StringComparison.OrdinalIgnoreCase)
            || (uint)(slot - 1) >= (uint)archive.Tables[tableIndex].Entries.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending raid battle edit target no longer matches the source archive.",
                expected: "Current raid battle archive target"));
            return null;
        }

        var sourceValue = GetArchiveFieldValue(archive.Tables[tableIndex].Entries[slot - 1], edit.Field);
        if (TryParseValue(edit.Field, edit.NewValue, diagnostics, sourceValue) is not { } value)
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

    private static string? GetArchiveFieldValue(SwShEncounterNest entry, string? field)
    {
        var value = field switch
        {
            SwShRaidBattlesWorkflowService.SpeciesField => entry.Species,
            SwShRaidBattlesWorkflowService.FormField => entry.Form,
            SwShRaidBattlesWorkflowService.AbilityField => entry.Ability,
            SwShRaidBattlesWorkflowService.IsGigantamaxField => entry.IsGigantamax ? 1 : 0,
            SwShRaidBattlesWorkflowService.GenderField => entry.Gender,
            SwShRaidBattlesWorkflowService.FlawlessIvsField => entry.FlawlessIvs,
            SwShRaidBattlesWorkflowService.Star1ProbabilityField => checked((int)entry.Probabilities[0]),
            SwShRaidBattlesWorkflowService.Star2ProbabilityField => checked((int)entry.Probabilities[1]),
            SwShRaidBattlesWorkflowService.Star3ProbabilityField => checked((int)entry.Probabilities[2]),
            SwShRaidBattlesWorkflowService.Star4ProbabilityField => checked((int)entry.Probabilities[3]),
            SwShRaidBattlesWorkflowService.Star5ProbabilityField => checked((int)entry.Probabilities[4]),
            _ => (int?)null,
        };

        return value?.ToString(CultureInfo.InvariantCulture);
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

    private static byte[] CreateArchiveOutput(
        string sourcePath,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var pack = SwShGfPackFile.Parse(File.ReadAllBytes(sourcePath));
        var archive = SwShEncounterNestArchive.Parse(
            pack.GetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName));
        var archiveEdits = GetRaidBattleEdits(session)
            .Select(edit => ToArchiveEdit(archive, edit, diagnostics))
            .Where(edit => edit is not null)
            .Select(edit => edit!)
            .ToArray();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return Array.Empty<byte>();
        }

        pack.SetFileByName(
            SwShRaidBattlesWorkflowService.EncounterMemberName,
            archive.WriteEdits(archiveEdits));
        return pack.Write();
    }

    private static void PreflightArchiveWrite(
        OpenedProject project,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShRaidRewardsWorkflowService.ResolveNestDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Battles edit preflight could not resolve the source nest archive.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return;
        }

        try
        {
            var output = CreateArchiveOutput(source.AbsolutePath, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            VerifyOutput(output, session, diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles edit cannot be encoded safely: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Compatible Sword/Shield raid battle archive"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles edit preflight could not read the source archive: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield raid battle archive"));
        }
    }

    private static void VerifyOutput(
        byte[] output,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var pack = SwShGfPackFile.Parse(output);
        var archive = SwShEncounterNestArchive.Parse(
            pack.GetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName));
        foreach (var edit in GetRaidBattleEdits(session))
        {
            if (!SwShRaidBattlesWorkflowService.TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
                || !SwShRaidBattlesWorkflowService.TryParseTableId(tableId, out var tableIndex, out var sourceTableId)
                || (uint)tableIndex >= (uint)archive.Tables.Count
                || archive.Tables[tableIndex].TableId != sourceTableId
                || (uint)(slot - 1) >= (uint)archive.Tables[tableIndex].Entries.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Generated Raid Battles output no longer contains the staged target.",
                    field: edit.Field,
                    expected: "Verified raid battle output"));
                continue;
            }

            var actualValue = GetArchiveFieldValue(archive.Tables[tableIndex].Entries[slot - 1], edit.Field);
            if (!string.Equals(actualValue, edit.NewValue, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Generated Raid Battles output did not retain the staged value.",
                    field: edit.Field,
                    expected: edit.NewValue));
            }
        }
    }

    private void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Raid Battles output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Raid Battles output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(tempPath, contents);
            if (!File.Exists(tempPath)
                || !File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Raid Battles temporary output verification failed.");
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
                // The original output remains available through the rollback scope.
            }
        }
    }

    private static void RollbackFailedApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var failure in rollbackScope.Rollback())
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles output rollback failed: {failure.Message}",
                file: failure.RelativePath,
                expected: "Output restored to its pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
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

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(paths, out var stablePaths, out var failure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles output root could not be resolved safely: {failure}",
                file: targetRelativePath,
                expected: "Stable physical output root"));
            return null;
        }

        var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            stablePaths.OutputRootPath,
            targetRelativePath);
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

    private static IEnumerable<PendingEdit> GetRaidBattleEdits(EditSession session)
    {
        return session.PendingEdits.Where(edit => string.Equals(
            edit.Domain,
            SwShRaidBattlesWorkflowService.RaidBattlesEditDomain,
            StringComparison.Ordinal));
    }

    private static bool OwnsDirectSession(EditSession session)
    {
        return session.PendingEdits.Count > 0
            && session.PendingEdits.All(edit => string.Equals(
                edit.Domain,
                SwShRaidBattlesWorkflowService.RaidBattlesEditDomain,
                StringComparison.Ordinal));
    }

    private static ValidationDiagnostic CreateDirectDomainOwnershipDiagnostic(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = domains.Length == 0 ? "no pending edit domain" : string.Join(", ", domains);
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Raid Battles direct validation, planning, and apply require only Raid Battles edits; found {actual}.",
            expected: SwShRaidBattlesWorkflowService.RaidBattlesEditDomain);
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
