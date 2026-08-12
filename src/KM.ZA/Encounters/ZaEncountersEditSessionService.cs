// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Moves;
using KM.ZA.ScriptedBosses;
using KM.ZA.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.ZA.Encounters;

internal sealed class ZaEncountersEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaEncountersWorkflowService encountersWorkflowService;

    public ZaEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaEncountersWorkflowService? encountersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.encountersWorkflowService = encountersWorkflowService ?? new ZaEncountersWorkflowService(this.fileSource);
    }

    public ZaEncountersEditResult UpdateSlotField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.EncountersDomain,
                diagnostics))
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        if (table is not null
            && slot == ZaEncounterPlayerPartnerCatalog.EditSlot
            && table.PlayerPartner is { } playerPartner)
        {
            var pendingPartnerEdit = CreatePlayerPartnerPendingEdit(
                table,
                playerPartner,
                field,
                value,
                diagnostics);
            if (pendingPartnerEdit is null)
            {
                return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
            }

            var projectedPartnerWorkflow = OverlayPendingEdit(workflow, pendingPartnerEdit);
            var updatedPartnerSession = ZaEditSessionSupport.ReplacePendingEdit(
                currentSession,
                pendingPartnerEdit);
            ValidateFinalPlayerPartnerSpeciesForm(workflow, projectedPartnerWorkflow, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
            }

            return new ZaEncountersEditResult(
                OverlayPendingEdits(loadedWorkflow, updatedPartnerSession.PendingEdits),
                updatedPartnerSession,
                diagnostics);
        }

        var slotRecord = table?.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (table is null || slotRecord is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter edit targets a table, slot, or special battle Pokemon that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A encounter table slot or its verified player partner"));
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(project, workflow, table, slotRecord, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var candidateWorkflow = OverlayPendingEdit(workflow, pendingEdit);
        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        var pairErrorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ValidateFinalSpeciesFormPairs(loadedWorkflow, candidateWorkflow, diagnostics);
        if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > pairErrorCount)
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        if (AffectsSharedLevelRange(pendingEdit.Field))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateFinalSharedLevelRanges(candidateWorkflow, [slotRecord.PokemonDataSourceIndex], diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount)
            {
                return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
            }
        }

        if (AffectsSpawnerData(pendingEdit.Field))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateFinalSpawnerCounts(candidateWorkflow, updatedSession.PendingEdits, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount)
            {
                return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
            }
        }

        AppendOutzoneBehaviorWarnings(loadedWorkflow, candidateWorkflow, diagnostics);
        return new ZaEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEncountersEditResult UpdateSlotFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaEncounterSlotFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.EncountersDomain,
                diagnostics))
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        var sharedLevelRangeSources = new HashSet<int>();
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.TableId)
                || string.IsNullOrWhiteSpace(update.Field)
                || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Encounter batch update is missing a table, field, or value.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: "updates",
                    expected: "Complete Pokemon Legends Z-A encounter slot field update"));
                continue;
            }

            var table = effectiveWorkflow.Tables.FirstOrDefault(candidate => candidate.TableId == update.TableId);
            if (table is not null
                && update.Slot == ZaEncounterPlayerPartnerCatalog.EditSlot
                && table.PlayerPartner is { } playerPartner)
            {
                var pendingPartnerEdit = CreatePlayerPartnerPendingEdit(
                    table,
                    playerPartner,
                    update.Field,
                    update.Value,
                    diagnostics);
                if (pendingPartnerEdit is null)
                {
                    continue;
                }

                updatedSession = ZaEditSessionSupport.ReplacePendingEdit(
                    updatedSession,
                    pendingPartnerEdit);
                effectiveWorkflow = OverlayPendingEdit(
                    effectiveWorkflow,
                    pendingPartnerEdit);
                continue;
            }

            var slot = table?.Slots.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            if (table is null || slot is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Encounter edit targets a table, slot, or special battle Pokemon that is not loaded.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: "slot",
                    expected: "Existing Pokemon Legends Z-A encounter table slot or its verified player partner"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                project,
                effectiveWorkflow,
                table,
                slot,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
            if (AffectsSharedLevelRange(pendingEdit.Field))
            {
                sharedLevelRangeSources.Add(slot.PokemonDataSourceIndex);
            }

        }

        ValidateFinalSpeciesFormPairs(loadedWorkflow, effectiveWorkflow, diagnostics);
        ValidateFinalPlayerPartnerSpeciesForm(loadedWorkflow, effectiveWorkflow, diagnostics);
        ValidateFinalSharedLevelRanges(effectiveWorkflow, sharedLevelRangeSources, diagnostics);
        ValidateFinalSpawnerCounts(effectiveWorkflow, updatedSession.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        AppendOutzoneBehaviorWarnings(loadedWorkflow, effectiveWorkflow, diagnostics);
        return new ZaEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEncountersEditResult StageSlotVanilla(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var currentWorkflow = OverlayPendingEdits(
            loadedWorkflow,
            currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                loadedWorkflow.Summary,
                loadedWorkflow.Diagnostics,
                ZaEditSessionSupport.EncountersDomain,
                diagnostics))
        {
            return new ZaEncountersEditResult(
                currentWorkflow,
                currentSession,
                diagnostics);
        }

        var targetTable = loadedWorkflow.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
        if (targetTable is not null
            && slot == ZaEncounterPlayerPartnerCatalog.EditSlot
            && targetTable.PlayerPartner is not null)
        {
            return StagePlayerPartnerVanilla(
                project,
                loadedWorkflow,
                currentWorkflow,
                currentSession,
                targetTable,
                diagnostics);
        }

        var targetSlot = targetTable?.Slots.FirstOrDefault(candidate =>
            candidate.Slot == slot);
        if (targetTable is null || targetSlot is null)
        {
            diagnostics.Add(CreateVanillaRestoreDiagnostic(
                "Vanilla restore targets an encounter table, slot, or special battle Pokemon that is not loaded.",
                "Existing Pokemon Legends Z-A encounter table slot or its verified player partner"));
            return new ZaEncountersEditResult(
                currentWorkflow,
                currentSession,
                diagnostics);
        }

        if (!ZaEncounterVanillaRestoreCatalog.TryCreate(
                project,
                fileSource,
                out var catalog,
                out var catalogBlockedReason))
        {
            diagnostics.Add(CreateVanillaRestoreDiagnostic(
                catalogBlockedReason,
                "Exact matching source coordinates and identities in readable verified vanilla files"));
            return new ZaEncountersEditResult(
                currentWorkflow,
                currentSession,
                diagnostics);
        }

        if (!catalog!.TryResolve(
                loadedWorkflow,
                targetTable,
                targetSlot,
                out var restore,
                out var slotBlockedReason))
        {
            diagnostics.Add(CreateVanillaRestoreDiagnostic(
                slotBlockedReason,
                "Exact matching source coordinates and identities in readable verified vanilla files"));
            return new ZaEncountersEditResult(
                currentWorkflow,
                currentSession,
                diagnostics);
        }

        var retainedEdits = currentSession.PendingEdits
            .Where(edit => !TargetsSelectedOwnership(
                loadedWorkflow,
                targetTable,
                targetSlot,
                edit))
            .ToArray();
        var removedEditCount = currentSession.PendingEdits.Count - retainedEdits.Length;
        var updatedSession = currentSession with { PendingEdits = retainedEdits };
        var effectiveWorkflow = OverlayPendingEdits(
            loadedWorkflow,
            updatedSession.PendingEdits);
        var stagedFieldCount = 0;
        var sharedLevelRangeChanged = false;

        foreach (var fieldValue in restore!.Fields.Where(field => field.RequiresWrite))
        {
            var effectiveTable = effectiveWorkflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, targetTable.TableId, StringComparison.Ordinal));
            var effectiveSlot = effectiveTable?.Slots.FirstOrDefault(candidate =>
                candidate.Slot == targetSlot.Slot);
            if (effectiveTable is null || effectiveSlot is null)
            {
                diagnostics.Add(CreateVanillaRestoreDiagnostic(
                    "Vanilla restore target changed while its fields were being staged.",
                    "Stable selected encounter table and slot"));
                return new ZaEncountersEditResult(
                    currentWorkflow,
                    currentSession,
                    diagnostics);
            }

            var pendingEdit = CreatePendingEdit(
                project,
                effectiveWorkflow,
                effectiveTable,
                effectiveSlot,
                fieldValue.Field,
                fieldValue.Value.ToString(CultureInfo.InvariantCulture),
                diagnostics,
                allowVerifiedVanillaSharedValue: true);
            if (pendingEdit is null)
            {
                return new ZaEncountersEditResult(
                    currentWorkflow,
                    currentSession,
                    diagnostics);
            }

            pendingEdit = pendingEdit with
            {
                Sources = pendingEdit.Sources
                    .Append(restore.EncounterBaseSource)
                    .Append(restore.SpawnerBaseSource)
                    .Distinct()
                    .ToArray(),
            };
            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(
                updatedSession,
                pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(
                effectiveWorkflow,
                pendingEdit);
            stagedFieldCount++;
            sharedLevelRangeChanged |= AffectsSharedLevelRange(fieldValue.Field);
        }

        ValidateFinalSpeciesFormPairs(
            loadedWorkflow,
            effectiveWorkflow,
            diagnostics,
            catalog,
            restore.Fields.Any(field =>
                field.RequiresWrite
                && field.Field is
                    ZaEncountersWorkflowService.SpeciesIdField
                    or ZaEncountersWorkflowService.FormField)
                ? new HashSet<int> { targetSlot.PokemonDataSourceIndex }
                : null);
        if (sharedLevelRangeChanged)
        {
            ValidateFinalSharedLevelRanges(
                effectiveWorkflow,
                [targetSlot.PokemonDataSourceIndex],
                diagnostics);
        }

        ValidateFinalSpawnerCounts(
            effectiveWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        if (diagnostics.Any(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaEncountersEditResult(
                currentWorkflow,
                currentSession,
                diagnostics);
        }

        AppendOutzoneBehaviorWarnings(
            loadedWorkflow,
            effectiveWorkflow,
            diagnostics);
        var message = stagedFieldCount > 0
            ? $"Staged {stagedFieldCount.ToString(CultureInfo.InvariantCulture)} verified vanilla "
                + "field values for the selected encounter."
            : removedEditCount > 0
                ? "The selected encounter already matches verified vanilla values. "
                    + "Its pending edits were cleared."
                : "The selected encounter already matches verified vanilla values.";
        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            message,
            ZaEditSessionSupport.EncountersDomain));
        return new ZaEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    private ZaEncountersEditResult StagePlayerPartnerVanilla(
        OpenedProject project,
        ZaEncountersWorkflow loadedWorkflow,
        ZaEncountersWorkflow currentWorkflow,
        EditSession currentSession,
        ZaEncounterTableRecord targetTable,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hasCurrentRow = TryReadPlayerPartnerRow(
            project,
            readBase: false,
            out _,
            out var currentRow,
            out var currentBlockedReason);
        var hasBaseRow = TryReadPlayerPartnerRow(
            project,
            readBase: true,
            out var baseSource,
            out var baseRow,
            out var baseBlockedReason);
        if (!hasCurrentRow || !hasBaseRow)
        {
            diagnostics.Add(CreateVanillaRestoreDiagnostic(
                !string.IsNullOrWhiteSpace(currentBlockedReason)
                    ? currentBlockedReason
                    : baseBlockedReason,
                "Exact temporary partner identity in readable effective and verified base PokemonData files"));
            return new ZaEncountersEditResult(currentWorkflow, currentSession, diagnostics.ToArray());
        }

        if (baseRow.MinLevel != baseRow.MaxLevel
            || baseRow.WazaList is null
            || baseRow.TalentValue is null)
        {
            diagnostics.Add(CreateVanillaRestoreDiagnostic(
                "The verified base temporary partner row does not have its expected fixed level, move list, and IV storage.",
                "Materialized fixed level, four-move list, and six IV values in verified base PokemonData"));
            return new ZaEncountersEditResult(currentWorkflow, currentSession, diagnostics.ToArray());
        }

        var retainedEdits = currentSession.PendingEdits
            .Where(edit => !ZaEncounterPlayerPartnerCatalog.IsRecordId(edit.RecordId))
            .ToArray();
        var removedEditCount = currentSession.PendingEdits.Count - retainedEdits.Length;
        var updatedSession = currentSession with { PendingEdits = retainedEdits };
        var effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, retainedEdits);
        var baseReference = new ProjectFileReference(
            ProjectFileLayer.Base,
            baseSource.RelativePath);
        var restoreMarker = new ProjectFileReference(
            ProjectFileLayer.Base,
            ZaEncounterPlayerPartnerCatalog.VanillaRestoreMarker);
        var restoreFields = CreatePlayerPartnerRestoreFields(currentRow, baseRow);
        var stagedFieldCount = 0;
        foreach (var restoreField in restoreFields.Where(field => field.RequiresWrite))
        {
            var effectiveTable = effectiveWorkflow.Tables.FirstOrDefault(table =>
                string.Equals(table.TableId, targetTable.TableId, StringComparison.Ordinal));
            var effectivePartner = effectiveTable?.PlayerPartner;
            if (effectiveTable is null || effectivePartner is null)
            {
                diagnostics.Add(CreateVanillaRestoreDiagnostic(
                    "The temporary partner target changed while its vanilla fields were being staged.",
                    "Stable verified Absol battle and temporary partner identity"));
                return new ZaEncountersEditResult(currentWorkflow, currentSession, diagnostics.ToArray());
            }

            var pendingEdit = CreatePlayerPartnerPendingEdit(
                effectiveTable,
                effectivePartner,
                restoreField.Field,
                restoreField.Value.ToString(CultureInfo.InvariantCulture),
                diagnostics,
                allowVerifiedVanillaValue: true);
            if (pendingEdit is null)
            {
                return new ZaEncountersEditResult(currentWorkflow, currentSession, diagnostics.ToArray());
            }

            pendingEdit = pendingEdit with
            {
                Sources = pendingEdit.Sources
                    .Append(baseReference)
                    .Append(restoreMarker)
                    .Distinct()
                    .ToArray(),
            };
            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
            stagedFieldCount++;
        }

        ValidateFinalPlayerPartnerSpeciesForm(loadedWorkflow, effectiveWorkflow, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaEncountersEditResult(currentWorkflow, currentSession, diagnostics.ToArray());
        }

        var message = stagedFieldCount > 0
            ? $"Staged {stagedFieldCount.ToString(CultureInfo.InvariantCulture)} verified vanilla field values for AZ's temporary Lucario."
            : removedEditCount > 0
                ? "AZ's temporary Lucario already matches verified vanilla values. Its pending edits were cleared."
                : "AZ's temporary Lucario already matches verified vanilla values.";
        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            message,
            ZaEditSessionSupport.EncountersDomain));
        return new ZaEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics.ToArray());
    }

    private bool TryReadPlayerPartnerRow(
        OpenedProject project,
        bool readBase,
        out ZaWorkflowFile source,
        out ZaPokemonDataEntry row,
        out string blockedReason)
    {
        try
        {
            source = readBase
                ? fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray)
                : fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            var document = ZaPokemonDataDocument.Parse(source.Bytes);
            return ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
                document,
                out row,
                out blockedReason);
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            source = null!;
            row = null!;
            blockedReason = $"{(readBase ? "Verified base" : "Effective")} PokemonData could not be read: {exception.Message}";
            return false;
        }
    }

    private static IReadOnlyList<ZaEncounterVanillaFieldValue> CreatePlayerPartnerRestoreFields(
        ZaPokemonDataEntry currentRow,
        ZaPokemonDataEntry baseRow)
    {
        var currentMoves = currentRow.WazaList?.Values.Take(4).ToArray() ?? [0, 0, 0, 0];
        var baseMoves = baseRow.WazaList!.Values.Take(4).ToArray();
        var currentIvs = currentRow.TalentValue;
        var baseIvs = baseRow.TalentValue!;
        return
        [
            RestoreField(ZaEncountersWorkflowService.SpeciesIdField, currentRow.DevNo, baseRow.DevNo),
            RestoreField(ZaEncountersWorkflowService.FormField, currentRow.FormNo, baseRow.FormNo),
            new ZaEncounterVanillaFieldValue(
                ZaEncountersWorkflowService.PlayerPartnerLevelField,
                baseRow.MinLevel,
                currentRow.MinLevel != baseRow.MinLevel || currentRow.MaxLevel != baseRow.MaxLevel),
            RestoreField(ZaEncountersWorkflowService.HeldItemIdField, currentRow.HoldItem ?? 0, baseRow.HoldItem ?? 0),
            RestoreField(ZaEncountersWorkflowService.AbilityField, currentRow.Tokusei, baseRow.Tokusei),
            RestoreField(ZaEncountersWorkflowService.NatureField, currentRow.Seikaku, baseRow.Seikaku),
            RestoreField(ZaEncountersWorkflowService.GenderField, currentRow.Sex, baseRow.Sex),
            RestoreField(ZaEncountersWorkflowService.ShinyModeField, currentRow.Rare, baseRow.Rare),
            RestoreField(ZaEncountersWorkflowService.Move1IdField, currentMoves[0], baseMoves[0]),
            RestoreField(ZaEncountersWorkflowService.Move2IdField, currentMoves[1], baseMoves[1]),
            RestoreField(ZaEncountersWorkflowService.Move3IdField, currentMoves[2], baseMoves[2]),
            RestoreField(ZaEncountersWorkflowService.Move4IdField, currentMoves[3], baseMoves[3]),
            RestoreField(ZaEncountersWorkflowService.IvHpField, currentIvs?.HP ?? -1, baseIvs.HP),
            RestoreField(ZaEncountersWorkflowService.IvAttackField, currentIvs?.Attack ?? -1, baseIvs.Attack),
            RestoreField(ZaEncountersWorkflowService.IvDefenseField, currentIvs?.Defense ?? -1, baseIvs.Defense),
            RestoreField(ZaEncountersWorkflowService.IvSpecialAttackField, currentIvs?.SpecialAttack ?? -1, baseIvs.SpecialAttack),
            RestoreField(ZaEncountersWorkflowService.IvSpecialDefenseField, currentIvs?.SpecialDefense ?? -1, baseIvs.SpecialDefense),
            RestoreField(ZaEncountersWorkflowService.IvSpeedField, currentIvs?.Speed ?? -1, baseIvs.Speed),
            RestoreField(ZaEncountersWorkflowService.VanillaTalentScaleField, currentRow.TalentScale, baseRow.TalentScale),
            RestoreField(ZaEncountersWorkflowService.VanillaTalentVCountField, currentRow.TalentVNum, baseRow.TalentVNum),
        ];
    }

    private static ZaEncounterVanillaFieldValue RestoreField(
        string field,
        int currentValue,
        int baseValue)
    {
        return new ZaEncounterVanillaFieldValue(field, baseValue, currentValue != baseValue);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = encountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);

        ZaEncounterVanillaRestoreCatalog? vanillaRestoreCatalog = null;
        if (session.PendingEdits.Any(HasVanillaRestoreSourceMarker))
        {
            ZaEncounterVanillaRestoreCatalog.TryCreate(
                project,
                fileSource,
                out vanillaRestoreCatalog,
                out _);
        }
        ZaPokemonDataEntry? playerPartnerBaseRow = null;
        if (session.PendingEdits.Any(edit =>
                ZaEncounterPlayerPartnerCatalog.IsRecordId(edit.RecordId)
                && HasPlayerPartnerVanillaRestoreSourceMarker(edit)))
        {
            _ = TryReadPlayerPartnerRow(
                project,
                readBase: true,
                out _,
                out playerPartnerBaseRow,
                out _);
        }
        var verifiedVanillaSpeciesFormSources = session.PendingEdits
            .Where(edit =>
                edit.Field is
                    ZaEncountersWorkflowService.SpeciesIdField
                    or ZaEncountersWorkflowService.FormField
                && HasVanillaRestoreSourceMarker(edit)
                && vanillaRestoreCatalog is not null
                && vanillaRestoreCatalog.HasRestoreSourceMarker(edit)
                && vanillaRestoreCatalog.TryValidatePendingRestore(workflow, edit))
            .Select(edit => ZaEncountersWorkflowService.TryParsePokemonDataRecordId(
                edit.RecordId,
                out var sourceIndex)
                    ? sourceIndex
                    : -1)
            .Where(sourceIndex => sourceIndex >= 0)
            .ToHashSet();

        var effectiveWorkflow = workflow;
        var sharedLevelRangeSources = new HashSet<int>();
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(
                effectiveWorkflow,
                edit,
                diagnostics,
                vanillaRestoreCatalog,
                playerPartnerBaseRow);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
                if (AffectsSharedLevelRange(edit.Field)
                    && TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex))
                {
                    sharedLevelRangeSources.Add(sourceIndex);
                }
            }
        }

        ValidateFinalSpeciesFormPairs(
            workflow,
            effectiveWorkflow,
            diagnostics,
            vanillaRestoreCatalog,
            verifiedVanillaSpeciesFormSources);
        ValidateFinalPlayerPartnerSpeciesForm(workflow, effectiveWorkflow, diagnostics);
        ValidateFinalSharedLevelRanges(effectiveWorkflow, sharedLevelRangeSources, diagnostics);
        ValidateFinalSpawnerCounts(effectiveWorkflow, session.PendingEdits, diagnostics);

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            AppendOutzoneBehaviorWarnings(workflow, effectiveWorkflow, diagnostics);
            if (session.PendingEdits.Count > 0)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    "Pending Wild Encounters change is valid.",
                    ZaEditSessionSupport.EncountersDomain));
            }
        }

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var diagnostics = Validate(paths, session).Diagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Wild Encounters edit before reviewing a change plan.",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Pending Wild Encounters edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var normalizationState = PrepareSpecialSpawnNormalization(project, session);
            AppendSpecialSpawnNormalizationErrors(
                normalizationState.Result,
                diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
            }

            var semanticSources = normalizationState.SemanticSources;
            var bossActionSemanticSources = session.PendingEdits.Any(edit =>
                    AffectsBossActionData(edit.Field))
                ? CreateBossActionPlanSemanticSources(project)
                : Array.Empty<PlanFingerprintSource>();
            var playerPartnerSemanticSources = session.PendingEdits.Any(IsPlayerPartnerEdit)
                ? CreatePlayerPartnerPlanSemanticSources(project)
                : Array.Empty<PlanFingerprintSource>();
            var plannedVirtualPaths = session.PendingEdits
                .Select(GetSourcePathForEdit)
                .Append(normalizationState.Result.HasChanges
                    ? ZaDataPaths.PokemonSpawnerDataArray
                    : null)
                .Where(path => path is not null)
                .Select(path => path!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var writes = plannedVirtualPaths
                .Select(virtualPath =>
                {
                    var plannedSemanticSources = string.Equals(
                            virtualPath,
                            ZaDataPaths.BossMoveSelectorArray,
                            StringComparison.Ordinal)
                        ? bossActionSemanticSources
                        : string.Equals(
                            virtualPath,
                            ZaDataPaths.PokemonDataArray,
                            StringComparison.Ordinal)
                            ? playerPartnerSemanticSources
                            : semanticSources;
                    var semanticSourceReferences = plannedSemanticSources
                        .Where(source => source.Layer is not null)
                        .Select(source => new ProjectFileReference(
                            source.Layer!.Value,
                            source.SourceIdentity))
                        .ToArray();
                    var plannedEdits = session.PendingEdits
                        .Where(edit => string.Equals(
                            GetSourcePathForEdit(edit),
                            virtualPath,
                            StringComparison.Ordinal))
                        .OrderBy(edit => edit.RecordId, StringComparer.Ordinal)
                        .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                        .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
                        .ThenBy(edit => edit.Summary, StringComparer.Ordinal)
                        .ToArray();
                    var sources = plannedEdits
                        .SelectMany(edit => edit.Sources)
                        .Concat(semanticSourceReferences)
                        .Distinct()
                        .OrderBy(source => source.Layer)
                        .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                        .ToArray();
                    var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                        paths,
                        virtualPath,
                        sources,
                        outputMode);
                    var editCount = plannedEdits.Length;
                    var specialSpawnReason = CreateSpecialSpawnPlanReason(
                        normalizationState.Result);
                    var reason = editCount switch
                    {
                        0 => specialSpawnReason,
                        1 => $"Apply pending Wild Encounters edit: {plannedEdits[0].Summary} "
                            + $"Change set SHA-256 {CreatePlanChangeSetFingerprint(plannedEdits)}.",
                        _ => $"Apply {editCount.ToString(CultureInfo.InvariantCulture)} pending Wild Encounters edits: "
                            + $"change set SHA-256 {CreatePlanChangeSetFingerprint(plannedEdits)}.",
                    };
                    if (string.Equals(
                            virtualPath,
                            ZaDataPaths.PokemonSpawnerDataArray,
                            StringComparison.Ordinal)
                        && normalizationState.Result.HasChanges
                        && editCount > 0)
                    {
                        reason += $" {specialSpawnReason}";
                    }

                    return new PlannedFileWrite(
                        writeInfo.TargetRelativePath,
                        writeInfo.Sources,
                        writeInfo.ReplacesExistingOutput,
                        reason,
                        CreatePlanSourceFingerprint(
                            paths,
                            virtualPath,
                            outputMode,
                            plannedSemanticSources));
                })
                .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
                .ToList();

            AppendSpecialSpawnNormalizationPlanDiagnostics(
                normalizationState.Result,
                diagnostics);

            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides.",
                    CreatePlanSourceFingerprint(
                        paths,
                        ZaWorkflowFileSource.DescriptorVirtualPath,
                        ZaOutputMode.Standalone,
                        [
                            new PlanFingerprintSource(
                                ZaWorkflowFileSource.DescriptorVirtualPath,
                                ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                                    paths,
                                    plannedVirtualPaths),
                                "DescriptorPreview",
                                ZaWorkflowFileSource.DescriptorVirtualPath),
                        ])));
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Change plan preview contains {writes.Count.ToString(CultureInfo.InvariantCulture)} target files.",
                ZaEditSessionSupport.EncountersDomain));

            return new ChangePlan(session.Id, writes, diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException
                or InvalidDataException
                or OverflowException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Wild Encounters change plan could not resolve the output target: {exception.Message}",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
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
        IDisposable outputLock;
        try
        {
            outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        }
        catch (Exception exception)
        {
            var lockDiagnostics = new[]
            {
                ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Wild Encounters output is busy or unavailable: {exception.Message}",
                    ZaEditSessionSupport.EncountersDomain,
                    expected: "Exclusive access to the selected output root"),
            };
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                reviewedPlan,
                Array.Empty<ProjectFileReference>(),
                lockDiagnostics);
        }

        using var acquiredOutputLock = outputLock;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Current reviewed Wild Encounters change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var normalizationState = PrepareSpecialSpawnNormalization(project, session);
            var workflow = normalizationState.CurrentWorkflow;
            var writesPlayerPartnerData = session.PendingEdits.Any(IsPlayerPartnerEdit);
            var writesEncounterData = session.PendingEdits.Any(edit =>
                !IsPlayerPartnerEdit(edit)
                && AffectsSharedPokemonData(edit.Field));
            var writesSpawnerData = session.PendingEdits.Any(edit => AffectsSpawnerData(edit.Field))
                || normalizationState.Result.HasChanges;
            var writesBossActionData = session.PendingEdits.Any(edit =>
                AffectsBossActionData(edit.Field));
            var encounterSource = normalizationState.EncounterSource;
            var spawnerSource = normalizationState.SpawnerSource;
            var capturedSemanticSources = normalizationState.SemanticSources;
            var bossActionSemanticSources = writesBossActionData
                ? CreateBossActionPlanSemanticSources(project)
                : Array.Empty<PlanFingerprintSource>();
            var playerPartnerSemanticSources = writesPlayerPartnerData
                ? CreatePlayerPartnerPlanSemanticSources(project)
                : Array.Empty<PlanFingerprintSource>();
            if ((writesEncounterData && !CapturedSourcesMatchPlan(
                    paths,
                    currentPlan,
                    ZaDataPaths.EncountDataArray,
                    outputMode,
                    capturedSemanticSources))
                || (writesSpawnerData && !CapturedSourcesMatchPlan(
                    paths,
                    currentPlan,
                    ZaDataPaths.PokemonSpawnerDataArray,
                    outputMode,
                    capturedSemanticSources))
                || (writesBossActionData && !CapturedSourcesMatchPlan(
                    paths,
                    currentPlan,
                    ZaDataPaths.BossMoveSelectorArray,
                    outputMode,
                    bossActionSemanticSources))
                || (writesPlayerPartnerData && !CapturedSourcesMatchPlan(
                    paths,
                    currentPlan,
                    ZaDataPaths.PokemonDataArray,
                    outputMode,
                    playerPartnerSemanticSources)))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Wild Encounters source or destination changed after review. Review the change plan again before applying.",
                    ZaEditSessionSupport.EncountersDomain,
                    expected: "The exact reviewed Wild Encounters source and output target"));
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            byte[]? reviewedStandaloneDescriptorBytes = null;
            if (outputMode == ZaOutputMode.Standalone)
            {
                var plannedVirtualPaths = session.PendingEdits
                    .Select(GetSourcePathForEdit)
                    .Append(normalizationState.Result.HasChanges
                        ? ZaDataPaths.PokemonSpawnerDataArray
                        : null)
                    .Where(path => path is not null)
                    .Select(path => path!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                reviewedStandaloneDescriptorBytes =
                    ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                        paths,
                        plannedVirtualPaths);
                if (!CapturedSourcesMatchPlan(
                        paths,
                        currentPlan,
                        ZaWorkflowFileSource.DescriptorVirtualPath,
                        ZaOutputMode.Standalone,
                        [
                            new PlanFingerprintSource(
                                ZaWorkflowFileSource.DescriptorVirtualPath,
                                reviewedStandaloneDescriptorBytes,
                                "DescriptorPreview",
                                ZaWorkflowFileSource.DescriptorVirtualPath),
                        ]))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "The standalone Trinity descriptor changed after review. "
                        + "Review the change plan again before applying.",
                        ZaEditSessionSupport.EncountersDomain,
                        expected: "The exact reviewed standalone Trinity descriptor"));
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }
            }

            var encounterDocument = writesEncounterData
                ? ZaEncounterDataDocument.Parse(encounterSource.Bytes)
                : null;
            var spawnerDocument = writesSpawnerData
                ? normalizationState.SpawnerDocument
                : null;
            var bossActionSourceBytes = writesBossActionData
                ? bossActionSemanticSources.Single(source => string.Equals(
                    source.VirtualPath,
                    ZaDataPaths.BossMoveSelectorArray,
                    StringComparison.Ordinal)).Bytes
                : null;
            var bossActionDocument = bossActionSourceBytes is not null
                ? ZaBossMoveSelectorDocument.Parse(bossActionSourceBytes)
                : null;
            var playerPartnerSourceBytes = writesPlayerPartnerData
                ? playerPartnerSemanticSources.Single(source => string.Equals(
                    source.VirtualPath,
                    ZaDataPaths.PokemonDataArray,
                    StringComparison.Ordinal)).Bytes
                : null;
            var playerPartnerDocument = playerPartnerSourceBytes is not null
                ? ZaPokemonDataDocument.Parse(playerPartnerSourceBytes)
                : null;
            foreach (var edit in session.PendingEdits)
            {
                if (IsPlayerPartnerEdit(edit))
                {
                    ApplyPlayerPartnerEdit(playerPartnerDocument!, edit, diagnostics);
                }
                else if (AffectsBossActionData(edit.Field))
                {
                    ApplyBossActionEdit(workflow, bossActionDocument!, edit, diagnostics);
                }
                else if (AffectsSpawnerData(edit.Field))
                {
                    ApplySpawnerEdit(workflow, spawnerDocument!, edit, diagnostics);
                }
                else
                {
                    ApplyEdit(workflow, encounterDocument!, edit, diagnostics);
                }
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var outputWrites = new List<ZaWorkflowFileWrite>();
            if (encounterDocument is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.EncountDataArray,
                    encounterDocument.Write()));
            }

            if (playerPartnerDocument is not null && playerPartnerSourceBytes is not null)
            {
                var playerPartnerBytes = playerPartnerDocument.Write();
                if (!VerifyPlayerPartnerOutput(
                        playerPartnerSourceBytes,
                        playerPartnerBytes,
                        normalizationState.EffectiveWorkflow,
                        diagnostics))
                {
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }

                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.PokemonDataArray,
                    playerPartnerBytes));
            }

            if (bossActionDocument is not null && bossActionSourceBytes is not null)
            {
                var bossActionBytes = bossActionDocument.Write();
                if (!VerifyBossActionOutput(
                        workflow,
                        bossActionSourceBytes,
                        bossActionBytes,
                        session.PendingEdits,
                        diagnostics))
                {
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }

                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.BossMoveSelectorArray,
                    bossActionBytes));
            }

            if (spawnerDocument is not null)
            {
                var spawnerBytes = spawnerDocument.Write();
                var verificationDocument = ZaPokemonSpawnerDataDocument.Parse(spawnerBytes);
                var verification = ZaSpecialSpawnNormalizer.Reconcile(
                    normalizationState.CurrentWorkflow,
                    normalizationState.EffectiveWorkflow,
                    verificationDocument,
                    normalizationState.BaseSpawnerDocument,
                    normalizationState.BaseEncounterDocument);
                if (verification.HasChanges || verification.Errors.Count > 0)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Special placement normalization did not converge to the reviewed output state.",
                        ZaEditSessionSupport.EncountersDomain,
                        file: $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}",
                        expected: "Stable reconciled special placement behavior"));
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }

                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.PokemonSpawnerDataArray,
                    spawnerBytes));
            }

            ZaWorkflowFileSource.WriteBatch(
                paths,
                outputWrites,
                outputMode,
                reviewedStandaloneDescriptorBytes);
            if (encounterDocument is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.EncountDataArray, outputMode));
            }

            if (playerPartnerDocument is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.PokemonDataArray,
                    outputMode));
            }

            if (spawnerDocument is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.PokemonSpawnerDataArray, outputMode));
            }

            if (bossActionDocument is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.BossMoveSelectorArray,
                    outputMode));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Wild Encounters", outputMode),
                ZaEditSessionSupport.EncountersDomain));
            AppendSpecialSpawnNormalizationApplyDiagnostics(
                normalizationState.Result,
                diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Wild Encounters output could not be written: {exception.Message}",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static string CreatePlanChangeSetFingerprint(
        IReadOnlyList<PendingEdit> edits)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Encounters.ChangeSet.v1");
        AppendFingerprintValue(hash, edits.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var edit in edits
            .OrderBy(candidate => candidate.Domain, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.RecordId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Field, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.NewValue, StringComparer.Ordinal))
        {
            AppendFingerprintValue(hash, edit.Domain);
            AppendFingerprintValue(hash, edit.RecordId);
            AppendFingerprintValue(hash, edit.Field);
            AppendFingerprintValue(hash, edit.NewValue);
            var sources = edit.Sources
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray();
            AppendFingerprintValue(hash, sources.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var source in sources)
            {
                AppendFingerprintValue(
                    hash,
                    ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintValue(hash, source.RelativePath);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string CreatePlanSourceFingerprint(
        ProjectPaths paths,
        string virtualPath,
        ZaOutputMode outputMode,
        IReadOnlyList<PlanFingerprintSource> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Encounters.Source.v2");
        AppendFingerprintValue(hash, virtualPath.Replace('\\', '/'));
        AppendFingerprintValue(hash, outputMode.ToString());
        AppendFingerprintValue(
            hash,
            NormalizeFingerprintPath(
                ZaWorkflowFileSource.ResolveOutputPath(paths, virtualPath, outputMode)));
        foreach (var source in sources.OrderBy(
                     source => source.VirtualPath,
                     StringComparer.Ordinal))
        {
            AppendFingerprintValue(hash, source.VirtualPath.Replace('\\', '/'));
            AppendFingerprintValue(hash, source.SourceKind);
            AppendFingerprintValue(hash, source.SourceIdentity.Replace('\\', '/'));
            AppendFingerprintBytes(hash, source.Bytes);
        }

        var targetPath = ZaWorkflowFileSource.ResolveOutputPath(paths, virtualPath, outputMode);
        if (File.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "TargetFile");
            AppendFingerprintBytes(hash, File.ReadAllBytes(targetPath));
        }
        else if (Directory.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "TargetDirectory");
        }
        else
        {
            AppendFingerprintValue(hash, "TargetMissing");
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool CapturedSourcesMatchPlan(
        ProjectPaths paths,
        ChangePlan plan,
        string virtualPath,
        ZaOutputMode outputMode,
        IReadOnlyList<PlanFingerprintSource> sources)
    {
        var targetRelativePath = ZaWorkflowFileSource.CreatePlannedWrite(
            paths,
            virtualPath,
            Array.Empty<ProjectFileReference>(),
            outputMode).TargetRelativePath;
        var plannedWrite = plan.Writes.FirstOrDefault(write =>
            string.Equals(
                write.TargetRelativePath,
                targetRelativePath,
                StringComparison.Ordinal));
        return plannedWrite is not null
            && string.Equals(
                plannedWrite.SourceFingerprint,
                CreatePlanSourceFingerprint(
                    paths,
                    virtualPath,
                    outputMode,
                    sources),
                StringComparison.Ordinal);
    }

    private PreparedSpecialSpawnNormalization PrepareSpecialSpawnNormalization(
        OpenedProject project,
        EditSession session)
    {
        var currentWorkflow = encountersWorkflowService.Load(project);
        var effectiveWorkflow = OverlayPendingEdits(
            currentWorkflow,
            session.PendingEdits);
        var encounterSource = fileSource.Read(project, ZaDataPaths.EncountDataArray);
        var spawnerSource = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
        var baseEncounterSource = fileSource.ReadBase(project, ZaDataPaths.EncountDataArray);
        var baseSpawnerSource = fileSource.ReadBase(project, ZaDataPaths.PokemonSpawnerDataArray);
        var spawnerDocument = ZaPokemonSpawnerDataDocument.Parse(spawnerSource.Bytes);
        var baseSpawnerDocument = ZaPokemonSpawnerDataDocument.Parse(baseSpawnerSource.Bytes);
        var baseEncounterDocument = ZaEncounterDataDocument.Parse(baseEncounterSource.Bytes);
        var result = ZaSpecialSpawnNormalizer.Reconcile(
            currentWorkflow,
            effectiveWorkflow,
            spawnerDocument,
            baseSpawnerDocument,
            baseEncounterDocument);
        return new PreparedSpecialSpawnNormalization(
            currentWorkflow,
            effectiveWorkflow,
            encounterSource,
            spawnerSource,
            spawnerDocument,
            baseSpawnerDocument,
            baseEncounterDocument,
            result,
            CreatePlanSemanticSources(
                encounterSource,
                spawnerSource,
                baseEncounterSource,
                baseSpawnerSource));
    }

    private static void AppendSpecialSpawnNormalizationErrors(
        ZaSpecialSpawnNormalizationResult result,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var error in result.Errors)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"A special placement could not be reconciled safely. {error}",
                ZaEditSessionSupport.EncountersDomain,
                file: $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}",
                expected: "Verified native special placement or byte-preserving ordinary-spawn conversion"));
        }
    }

    private static void AppendSpecialSpawnNormalizationPlanDiagnostics(
        ZaSpecialSpawnNormalizationResult result,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (result.NormalizedCount > 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"The reviewed Wild Encounters output will remove incompatible special spawn behavior from "
                + $"{FormatPlacementCount(result.NormalizedCount)} so they use ordinary spawn behavior.",
                ZaEditSessionSupport.EncountersDomain,
                file: $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}"));
        }

        if (result.RestoredCount > 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"The reviewed Wild Encounters output will restore special spawn behavior for "
                + $"{FormatPlacementCount(result.RestoredCount)} returned to a compatible species.",
                ZaEditSessionSupport.EncountersDomain,
                file: $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}"));
        }
    }

    private static void AppendSpecialSpawnNormalizationApplyDiagnostics(
        ZaSpecialSpawnNormalizationResult result,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (result.NormalizedCount > 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Converted {FormatPlacementCount(result.NormalizedCount)} to ordinary spawns "
                + "while preserving their encounter links, conditions, counts, and positions.",
                ZaEditSessionSupport.EncountersDomain,
                file: $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}"));
        }

        if (result.RestoredCount > 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Restored special spawn behavior for {FormatPlacementCount(result.RestoredCount)} "
                + "returned to a compatible species.",
                ZaEditSessionSupport.EncountersDomain,
                file: $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}"));
        }
    }

    private static string FormatPlacementCount(int count)
    {
        return count == 1
            ? "1 special placement"
            : $"{count.ToString(CultureInfo.InvariantCulture)} special placements";
    }

    private static string CreateSpecialSpawnPlanReason(
        ZaSpecialSpawnNormalizationResult result)
    {
        if (result.NormalizedCount > 0 && result.RestoredCount > 0)
        {
            return $"Convert {FormatPlacementCount(result.NormalizedCount)} to ordinary spawns "
                + $"and restore special spawn behavior for {FormatPlacementCount(result.RestoredCount)}.";
        }

        if (result.NormalizedCount > 0)
        {
            return $"Remove incompatible special spawn behavior from "
                + $"{FormatPlacementCount(result.NormalizedCount)}.";
        }

        if (result.RestoredCount > 0)
        {
            return $"Restore special spawn behavior for {FormatPlacementCount(result.RestoredCount)} "
                + "returned to a compatible species.";
        }

        return "Preserve the reviewed Wild Encounters spawner output.";
    }

    private static IReadOnlyList<PlanFingerprintSource> CreatePlanSemanticSources(
        ZaWorkflowFile encounterSource,
        ZaWorkflowFile spawnerSource,
        ZaWorkflowFile? baseEncounterSource = null,
        ZaWorkflowFile? baseSpawnerSource = null)
    {
        var sources = new List<PlanFingerprintSource>
        {
            new PlanFingerprintSource(
                ZaDataPaths.EncountDataArray,
                encounterSource.Bytes,
                encounterSource.SourceLayer.ToString(),
                encounterSource.RelativePath,
                encounterSource.SourceLayer),
            new PlanFingerprintSource(
                ZaDataPaths.PokemonSpawnerDataArray,
                spawnerSource.Bytes,
                spawnerSource.SourceLayer.ToString(),
                spawnerSource.RelativePath,
                spawnerSource.SourceLayer),
        };
        if (baseEncounterSource is not null && baseSpawnerSource is not null)
        {
            sources.Add(new PlanFingerprintSource(
                $"base:{ZaDataPaths.EncountDataArray}",
                baseEncounterSource.Bytes,
                $"Base:{baseEncounterSource.Origin}",
                baseEncounterSource.RelativePath,
                ProjectFileLayer.Base));
            sources.Add(new PlanFingerprintSource(
                $"base:{ZaDataPaths.PokemonSpawnerDataArray}",
                baseSpawnerSource.Bytes,
                $"Base:{baseSpawnerSource.Origin}",
                baseSpawnerSource.RelativePath,
                ProjectFileLayer.Base));
        }

        return sources;
    }

    private IReadOnlyList<PlanFingerprintSource> CreateBossActionPlanSemanticSources(
        OpenedProject project)
    {
        var selectorSource = fileSource.Read(project, ZaDataPaths.BossMoveSelectorArray);
        var baseSelectorSource = fileSource.ReadBase(project, ZaDataPaths.BossMoveSelectorArray);
        var battleSource = fileSource.Read(project, ZaDataPaths.BattleMoveParameterArray);
        var timingSource = fileSource.Read(project, ZaDataPaths.MoveTimingParameterArray);

        _ = ZaBossMoveSelectorDocument.Parse(selectorSource.Bytes);
        _ = ZaBossMoveSelectorDocument.Parse(baseSelectorSource.Bytes);
        _ = ZaRuntimeMoveData.ReadBattle(battleSource.Bytes);
        _ = ZaRuntimeMoveData.ReadTiming(timingSource.Bytes);

        return
        [
            new PlanFingerprintSource(
                ZaDataPaths.BossMoveSelectorArray,
                selectorSource.Bytes,
                selectorSource.SourceLayer.ToString(),
                selectorSource.RelativePath,
                selectorSource.SourceLayer),
            new PlanFingerprintSource(
                $"base:{ZaDataPaths.BossMoveSelectorArray}",
                baseSelectorSource.Bytes,
                $"Base:{baseSelectorSource.Origin}",
                baseSelectorSource.RelativePath,
                ProjectFileLayer.Base),
            new PlanFingerprintSource(
                ZaDataPaths.BattleMoveParameterArray,
                battleSource.Bytes,
                battleSource.SourceLayer.ToString(),
                battleSource.RelativePath,
                battleSource.SourceLayer),
            new PlanFingerprintSource(
                ZaDataPaths.MoveTimingParameterArray,
                timingSource.Bytes,
                timingSource.SourceLayer.ToString(),
                timingSource.RelativePath,
                timingSource.SourceLayer),
        ];
    }

    private IReadOnlyList<PlanFingerprintSource> CreatePlayerPartnerPlanSemanticSources(
        OpenedProject project)
    {
        var source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
        var baseSource = fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray);
        var document = ZaPokemonDataDocument.Parse(source.Bytes);
        var baseDocument = ZaPokemonDataDocument.Parse(baseSource.Bytes);
        var hasSourceRow = ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
            document,
            out _,
            out var sourceBlockedReason);
        var hasBaseRow = ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
            baseDocument,
            out _,
            out var baseBlockedReason);
        if (!hasSourceRow || !hasBaseRow)
        {
            throw new InvalidDataException(
                "AZ's temporary Lucario cannot be fingerprinted safely. "
                    + (!string.IsNullOrWhiteSpace(sourceBlockedReason)
                        ? sourceBlockedReason
                        : baseBlockedReason));
        }

        return
        [
            new PlanFingerprintSource(
                ZaDataPaths.PokemonDataArray,
                source.Bytes,
                source.SourceLayer.ToString(),
                source.RelativePath,
                source.SourceLayer),
            new PlanFingerprintSource(
                $"base:{ZaDataPaths.PokemonDataArray}",
                baseSource.Bytes,
                $"Base:{baseSource.Origin}",
                baseSource.RelativePath,
                ProjectFileLayer.Base),
        ];
    }

    private sealed record PlanFingerprintSource(
        string VirtualPath,
        byte[] Bytes,
        string SourceKind,
        string SourceIdentity,
        ProjectFileLayer? Layer = null);

    private static string NormalizeFingerprintPath(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static void AppendFingerprintValue(
        IncrementalHash hash,
        string? value)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, -1);
            hash.AppendData(lengthBytes);
            return;
        }

        var valueBytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, valueBytes.Length);
        hash.AppendData(lengthBytes);
        hash.AppendData(valueBytes);
    }

    private static void AppendFingerprintBytes(
        IncrementalHash hash,
        byte[] value)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, value.LongLength);
        hash.AppendData(lengthBytes);
        hash.AppendData(value);
    }

    private PendingEdit? CreatePendingEdit(
        OpenedProject project,
        ZaEncountersWorkflow workflow,
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics,
        bool allowVerifiedVanillaSharedValue = false)
    {
        var normalizedField = field.Trim();
        if (ZaScriptedBossActionCatalog.TryParseEditField(
                normalizedField,
                out var selectorActionId))
        {
            return CreateBossActionPendingEdit(
                project,
                workflow,
                table,
                slot,
                selectorActionId,
                normalizedField,
                value,
                diagnostics);
        }

        var editableField = ResolveEditableField(
            workflow,
            normalizedField,
            allowVerifiedVanillaSharedValue);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (AffectsSharedPokemonData(normalizedField) && slot.PokemonDataSourceIndex < 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter slot is missing its linked encounter data row and cannot be edited.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Encounter slot linked to Encount Data"));
            return null;
        }

        if (AffectsSpawnerData(normalizedField)
            && !ValidateSpawnerFieldEditability(slot, normalizedField, diagnostics))
        {
            return null;
        }

        if (!allowVerifiedVanillaSharedValue
            && IsStrengthenField(normalizedField)
            && !ValidateStrengthenFieldEditability(slot, normalizedField, diagnostics))
        {
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            allowVerifiedVanillaSharedValue ? null : editableField.MinimumValue,
            allowVerifiedVanillaSharedValue ? null : editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (!allowVerifiedVanillaSharedValue
            && !ValidateOptionValue(editableField, parsedValue.Value, diagnostics))
        {
            return null;
        }

        if (!allowVerifiedVanillaSharedValue
            && AffectsSharedPokemonData(normalizedField)
            && !ValidateSharedAlphaChance(
                workflow,
                slot.PokemonDataSourceIndex,
                normalizedField,
                parsedValue.Value,
                diagnostics))
        {
            return null;
        }

        if (!allowVerifiedVanillaSharedValue
            && AffectsSharedPokemonData(normalizedField)
            && !ValidateSharedAlphaLevelBonus(
                workflow,
                slot.PokemonDataSourceIndex,
                normalizedField,
                diagnostics))
        {
            return null;
        }

        var sourceProvenance = AffectsSpawnerData(normalizedField)
            ? table.Provenance
            : slot.PokemonProvenance;
        var recordId = AffectsAppearanceCounts(normalizedField)
            ? ZaEncountersWorkflowService.CreateAppearanceRecordId(table.TableId)
            : AffectsSpawnerSlot(normalizedField)
                ? ZaEncountersWorkflowService.CreateSlotRecordId(table.TableId, slot.Slot)
                : ZaEncountersWorkflowService.CreatePokemonDataRecordId(slot.PokemonDataSourceIndex);

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.EncountersDomain,
            CreateSummary(table, slot, editableField, parsedValue.Value),
            new ProjectFileReference(sourceProvenance.SourceLayer, sourceProvenance.SourceFile),
            recordId,
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static PendingEdit? CreatePlayerPartnerPendingEdit(
        ZaEncounterTableRecord table,
        ZaEncounterPlayerPartnerRecord partner,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics,
        bool allowVerifiedVanillaValue = false)
    {
        var normalizedField = field.Trim();
        if (!ZaEncounterPlayerPartnerCatalog.IsTargetTable(table)
            || partner.Slot != ZaEncounterPlayerPartnerCatalog.EditSlot
            || partner.PokemonDataSourceIndex != ZaEncounterPlayerPartnerCatalog.PokemonDataSourceIndex
            || !string.Equals(
                partner.PokemonDataId,
                ZaEncounterPlayerPartnerCatalog.PokemonDataId,
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "The selected temporary partner no longer has its verified Absol battle identity.",
                normalizedField,
                "Exact Absol story battle and vsmega_init_rukario source identity"));
            return null;
        }

        var editableField = ResolvePlayerPartnerEditableField(
            partner,
            normalizedField,
            allowVerifiedVanillaValue);
        if (editableField is null)
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                $"Temporary partner field '{normalizedField}' is not supported.",
                normalizedField,
                "Supported AZ's Lucario field"));
            return null;
        }

        if (!allowVerifiedVanillaValue
            && string.Equals(
                normalizedField,
                ZaEncountersWorkflowService.PlayerPartnerLevelField,
                StringComparison.Ordinal)
            && !partner.CanEditLevel)
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "The temporary partner level is read-only because its source row does not currently store one fixed level from 1 through 100.",
                normalizedField,
                "Matching minimum and maximum levels from 1 through 100"));
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            allowVerifiedVanillaValue ? null : editableField.MinimumValue,
            allowVerifiedVanillaValue ? null : editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is null
            || !allowVerifiedVanillaValue
                && !ValidateOptionValue(editableField, parsedValue.Value, diagnostics))
        {
            return null;
        }

        if (!allowVerifiedVanillaValue
            && normalizedField is (ZaEncountersWorkflowService.SpeciesIdField
                or ZaEncountersWorkflowService.FormField
                or ZaEncountersWorkflowService.HeldItemIdField))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "AZ's temporary partner is scripted as Lucario holding Lucarionite. Changing its species, form, or held item may disrupt the battle's scripted Mega Evolution behavior.",
                ZaEditSessionSupport.EncountersDomain,
                partner.Provenance.SourceFile,
                normalizedField,
                "Keep Lucario form 0 with Lucarionite unless the replacement has been tested in the Absol battle"));
        }

        var optionLabel = editableField.Options.FirstOrDefault(option =>
            option.Value == parsedValue.Value)?.Label;
        var displayValue = optionLabel
            ?? parsedValue.Value.ToString(CultureInfo.InvariantCulture);
        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.EncountersDomain,
            $"Set AZ's temporary Lucario {editableField.Label} to {displayValue} for the Absol battle.",
            new ProjectFileReference(
                partner.Provenance.SourceLayer,
                partner.Provenance.SourceFile),
            ZaEncounterPlayerPartnerCatalog.RecordId,
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static ZaEncounterEditableField? ResolvePlayerPartnerEditableField(
        ZaEncounterPlayerPartnerRecord partner,
        string? field,
        bool allowVerifiedVanillaValue)
    {
        var editableField = partner.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
        if (editableField is not null || !allowVerifiedVanillaValue)
        {
            return editableField;
        }

        return field switch
        {
            ZaEncountersWorkflowService.VanillaTalentScaleField =>
                new ZaEncounterEditableField(
                    field,
                    "Talent scale",
                    "integer",
                    null,
                    null,
                    Array.Empty<ZaEncounterEditableFieldOption>()),
            ZaEncountersWorkflowService.VanillaTalentVCountField =>
                new ZaEncounterEditableField(
                    field,
                    "Talent V count",
                    "integer",
                    null,
                    null,
                    Array.Empty<ZaEncounterEditableFieldOption>()),
            _ => null,
        };
    }

    private PendingEdit? CreateBossActionPendingEdit(
        OpenedProject project,
        ZaEncountersWorkflow workflow,
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        int selectorActionId,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsPrimaryBossController(table.RawSpawnerId))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Boss action edits must be staged from the primary scripted boss controller encounter.",
                field,
                "Selected primary btl_spn_boss_* controller"));
            return null;
        }

        var profile = ZaScriptedBossActionCatalog.FindProfile(
            workflow.ScriptedBosses,
            table.RawSpawnerId,
            slot.SpeciesId,
            slot.Form);
        var action = profile?.Actions.FirstOrDefault(candidate =>
            candidate.SelectorActionId == selectorActionId);
        if (profile is null || action is null)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "The selected scripted boss controller does not own this boss action selector.",
                field,
                "Selector action owned by the selected primary controller"));
            return null;
        }

        if (!action.CanEdit
            || !string.Equals(
                action.Kind,
                ZaScriptedBossActionCatalog.BattleMoveKind,
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                CreateBossActionLockMessage(action),
                field,
                "Verified data-driven battle move selector"));
            return null;
        }

        if (!TryResolveEditableBossActionOwners(
                workflow,
                selectorActionId,
                out _,
                out var variant)
            || action.Variant != variant)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "The shared boss action selector does not have one verified editable move variant.",
                field,
                "All selector owners must be editable battle actions with the same move variant"));
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            minimumValue: 0,
            maximumValue: ZaScriptedBossActionCatalog.MaximumBaseMoveId,
            field: field,
            domain: ZaEditSessionSupport.EncountersDomain,
            diagnostics: diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        var option = workflow.ScriptedBossMoveOptions.FirstOrDefault(candidate =>
            candidate.MoveId == parsedValue.Value
            && candidate.Variant == variant);
        if (option is null)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "The requested move is not a verified working replacement for this selector variant.",
                field,
                "Move with one battle row and a matching timing row for the selector variant"));
            return null;
        }

        ZaWorkflowFile selectorSource;
        try
        {
            selectorSource = fileSource.Read(project, ZaDataPaths.BossMoveSelectorArray);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                $"Boss action selector data could not be captured for staging: {exception.Message}",
                field,
                "Readable effective boss move selector data"));
            return null;
        }

        var affectedProfiles = workflow.ScriptedBosses
            .Where(candidate => candidate.Actions.Any(candidateAction =>
                candidateAction.SelectorActionId == selectorActionId
                && candidateAction.Variant == variant))
            .Select(candidate => candidate.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var impact = affectedProfiles.Length switch
        {
            0 => profile.Name,
            1 => affectedProfiles[0],
            _ => string.Join(", ", affectedProfiles),
        };
        var summary = $"Set scripted boss selector action {selectorActionId.ToString(CultureInfo.InvariantCulture)} "
            + $"from {action.Name} to {option.Name}. Affected profiles: {impact}.";

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.EncountersDomain,
            summary,
            new ProjectFileReference(
                selectorSource.SourceLayer,
                selectorSource.RelativePath),
            ZaScriptedBossActionCatalog.CreateRecordId(selectorActionId),
            field,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsPrimaryBossController(string? rawSpawnerId)
    {
        if (string.IsNullOrWhiteSpace(rawSpawnerId)
            || !rawSpawnerId.StartsWith(
                "btl_spn_boss_",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !rawSpawnerId
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.StartsWith(
                "follower",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveEditableBossActionOwners(
        ZaEncountersWorkflow workflow,
        int selectorActionId,
        out ZaScriptedBossActionRecord[] ownedActions,
        out int variant)
    {
        ownedActions = workflow.ScriptedBosses
            .SelectMany(profile => profile.Actions)
            .Where(action => action.SelectorActionId == selectorActionId)
            .ToArray();
        variant = default;
        if (ownedActions.Length == 0
            || ownedActions[0].Variant is not { } sharedVariant
            || ownedActions.Any(action =>
                !action.CanEdit
                || !string.Equals(
                    action.Kind,
                    ZaScriptedBossActionCatalog.BattleMoveKind,
                    StringComparison.Ordinal)
                || action.Variant != sharedVariant))
        {
            return false;
        }

        variant = sharedVariant;
        return true;
    }

    private static string CreateBossActionLockMessage(
        ZaScriptedBossActionRecord action)
    {
        return action.LockReason switch
        {
            ZaScriptedBossActionCatalog.ControllerScriptLockReason =>
                "This boss action is hard-coded in the controller script and cannot be edited.",
            ZaScriptedBossActionCatalog.TimingChoreographyLockReason =>
                "This timing or movement helper is fixed by the controller choreography and cannot be edited.",
            ZaScriptedBossActionCatalog.SelectorUnavailableLockReason =>
                "This boss action selector could not be verified against the base game and is locked.",
            ZaScriptedBossActionCatalog.RuntimeCatalogUnavailableLockReason =>
                "Working move replacement data could not be verified, so this selector is locked.",
            _ => "This boss action is locked and cannot be edited.",
        };
    }

    private static ValidationDiagnostic CreateBossActionDiagnostic(
        string message,
        string? field,
        string expected)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.EncountersDomain,
            file: $"romfs/{ZaDataPaths.BossMoveSelectorArray}",
            field: field,
            expected: expected);
    }

    private static ValidationDiagnostic CreatePlayerPartnerDiagnostic(
        string message,
        string? field,
        string expected)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.EncountersDomain,
            file: $"romfs/{ZaDataPaths.PokemonDataArray}",
            field: field,
            expected: expected);
    }

    private static void ValidatePendingEdit(
        ZaEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics,
        ZaEncounterVanillaRestoreCatalog? vanillaRestoreCatalog = null,
        ZaPokemonDataEntry? playerPartnerBaseRow = null)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Wild Encounters.",
                ZaEditSessionSupport.EncountersDomain,
                expected: ZaEditSessionSupport.EncountersDomain));
            return;
        }

        if (ZaEncounterPlayerPartnerCatalog.IsRecordId(edit.RecordId))
        {
            ValidatePlayerPartnerPendingEdit(
                workflow,
                edit,
                playerPartnerBaseRow,
                diagnostics);
            return;
        }

        if (ZaScriptedBossActionCatalog.TryParseEditField(
                edit.Field,
                out var selectorActionId))
        {
            ValidateBossActionPendingEdit(
                workflow,
                edit,
                selectorActionId,
                diagnostics);
            return;
        }

        var hasVanillaRestoreMarker = HasVanillaRestoreSourceMarker(edit);
        var isVerifiedVanillaValue = hasVanillaRestoreMarker
            && vanillaRestoreCatalog is not null
            && vanillaRestoreCatalog.HasRestoreSourceMarker(edit)
            && vanillaRestoreCatalog.TryValidatePendingRestore(workflow, edit);
        if (hasVanillaRestoreMarker && !isVerifiedVanillaValue)
        {
            diagnostics.Add(CreateVanillaRestoreDiagnostic(
                "The pending vanilla restore no longer matches the verified base files. "
                    + "Stage the selected encounter restore again before review.",
                "Current exact source identities and values from the verified vanilla encounter files"));
            return;
        }

        var editableField = ResolveEditableField(
            workflow,
            edit.Field,
            isVerifiedVanillaValue);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        var sourceIndex = -1;
        ZaEncounterSlotRecord? slot = null;
        if (AffectsSharedPokemonData(edit.Field)
            && !TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out sourceIndex))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets an encounter data row that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A encounter data row"));
            return;
        }

        if (AffectsSpawnerSlot(edit.Field)
            && !TryResolveSpawnerSlot(workflow, edit.RecordId, out _, out slot))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets a spawner slot that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A spawner slot"));
            return;
        }

        if (AffectsAppearanceCounts(edit.Field))
        {
            if (!TryResolveAppearanceTable(workflow, edit.RecordId, out var table))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending encounter edit targets a spawner appearance that is not loaded.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: "slot",
                    expected: "Existing Pokemon Legends Z-A spawner appearance"));
                return;
            }

            slot = table.Slots.FirstOrDefault();
            if (slot is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending encounter edit targets a spawner appearance that has no encounter slots.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: edit.Field,
                    expected: "Existing Pokemon Legends Z-A spawner appearance"));
                return;
            }
        }

        if (slot is not null
            && AffectsSpawnerData(edit.Field)
            && !ValidateSpawnerFieldEditability(slot, edit.Field, diagnostics))
        {
            return;
        }

        if (!isVerifiedVanillaValue
            && IsStrengthenField(edit.Field)
            && !ValidateStrengthenPendingEditability(
                workflow,
                sourceIndex,
                edit.Field,
                diagnostics))
        {
            return;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            isVerifiedVanillaValue ? null : editableField.MinimumValue,
            isVerifiedVanillaValue ? null : editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is not null)
        {
            if (!isVerifiedVanillaValue
                && !ValidateOptionValue(editableField, parsedValue.Value, diagnostics))
            {
                return;
            }

            if (AffectsSharedPokemonData(edit.Field) && !isVerifiedVanillaValue)
            {
                ValidateSharedAlphaChance(
                    workflow,
                    sourceIndex,
                    edit.Field,
                    parsedValue.Value,
                    diagnostics);
                ValidateSharedAlphaLevelBonus(
                    workflow,
                    sourceIndex,
                    edit.Field,
                    diagnostics);
            }
        }
    }

    private static void ValidateBossActionPendingEdit(
        ZaEncountersWorkflow workflow,
        PendingEdit edit,
        int selectorActionId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!ZaScriptedBossActionCatalog.TryParseRecordId(
                edit.RecordId,
                out var recordSelectorActionId)
            || recordSelectorActionId != selectorActionId)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Pending boss action edit has a mismatched or missing selector record ID.",
                edit.Field,
                ZaScriptedBossActionCatalog.CreateRecordId(selectorActionId)));
            return;
        }

        if (!TryResolveEditableBossActionOwners(
                workflow,
                selectorActionId,
                out var actions,
                out var variant))
        {
            if (actions.Length == 0)
            {
                diagnostics.Add(CreateBossActionDiagnostic(
                    "Pending boss action edit targets a selector that is not owned by a verified scripted boss controller.",
                    edit.Field,
                    "Known selector action from a verified scripted boss profile"));
            }
            else if (actions.FirstOrDefault(action =>
                    !action.CanEdit
                    || !string.Equals(
                        action.Kind,
                        ZaScriptedBossActionCatalog.BattleMoveKind,
                        StringComparison.Ordinal)) is { } lockedAction)
            {
                diagnostics.Add(CreateBossActionDiagnostic(
                    CreateBossActionLockMessage(lockedAction),
                    edit.Field,
                    "Verified data-driven battle move selector"));
            }
            else
            {
                diagnostics.Add(CreateBossActionDiagnostic(
                    "Pending boss action edit targets a shared selector without one verified move variant.",
                    edit.Field,
                    "All selector owners must use the same non-null move variant"));
            }

            return;
        }

        var moveId = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            minimumValue: 0,
            maximumValue: ZaScriptedBossActionCatalog.MaximumBaseMoveId,
            field: edit.Field,
            domain: ZaEditSessionSupport.EncountersDomain,
            diagnostics: diagnostics);
        if (moveId is null)
        {
            return;
        }

        if (!workflow.ScriptedBossMoveOptions.Any(option =>
                option.MoveId == moveId.Value
                && option.Variant == variant))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Pending boss action edit no longer selects a verified working replacement for its selector variant.",
                edit.Field,
                "Move with one battle row and a matching timing row for the selector variant"));
            return;
        }

        if (!HasSourceReference(edit, ZaDataPaths.BossMoveSelectorArray))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Pending boss action edit is missing its selector source provenance.",
                edit.Field,
                $"Source reference for romfs/{ZaDataPaths.BossMoveSelectorArray}"));
        }
    }

    private static void ValidatePlayerPartnerPendingEdit(
        ZaEncountersWorkflow workflow,
        PendingEdit edit,
        ZaPokemonDataEntry? baseRow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var matches = workflow.Tables
            .Where(ZaEncounterPlayerPartnerCatalog.IsTargetTable)
            .Select(table => table.PlayerPartner)
            .Where(partner => partner is not null)
            .Cast<ZaEncounterPlayerPartnerRecord>()
            .ToArray();
        if (matches.Length != 1
            || matches[0].PokemonDataSourceIndex != ZaEncounterPlayerPartnerCatalog.PokemonDataSourceIndex
            || !string.Equals(
                matches[0].PokemonDataId,
                ZaEncounterPlayerPartnerCatalog.PokemonDataId,
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "Pending temporary partner edit no longer targets one verified Absol battle PokemonData row.",
                edit.Field,
                "One exact vsmega_init_rukario row attached to the base Absol story battle"));
            return;
        }

        if (!HasSourceReference(edit, ZaDataPaths.PokemonDataArray))
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "Pending temporary partner edit is missing its PokemonData source provenance.",
                edit.Field,
                $"Source reference for romfs/{ZaDataPaths.PokemonDataArray}"));
            return;
        }

        var hasVanillaMarker = HasPlayerPartnerVanillaRestoreSourceMarker(edit);
        var isVerifiedVanillaValue = hasVanillaMarker
            && baseRow is not null
            && TryReadPlayerPartnerField(baseRow, edit.Field, out var expectedValue)
            && int.TryParse(
                edit.NewValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var stagedValue)
            && stagedValue == expectedValue;
        if (hasVanillaMarker && !isVerifiedVanillaValue)
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "The pending temporary partner restore no longer matches the exact verified base PokemonData row. Stage the restore again before review.",
                edit.Field,
                "Current exact vanilla field value from vsmega_init_rukario at source index 772"));
            return;
        }

        var partner = matches[0];
        var editableField = ResolvePlayerPartnerEditableField(
            partner,
            edit.Field,
            isVerifiedVanillaValue);
        if (editableField is null)
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                $"Temporary partner field '{edit.Field ?? "(missing)"}' is not supported.",
                edit.Field,
                "Supported AZ's Lucario field"));
            return;
        }

        if (!isVerifiedVanillaValue
            && string.Equals(
                edit.Field,
                ZaEncountersWorkflowService.PlayerPartnerLevelField,
                StringComparison.Ordinal)
            && !partner.CanEditLevel)
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "The pending temporary partner level no longer targets one fixed source level from 1 through 100.",
                edit.Field,
                "Matching minimum and maximum levels from 1 through 100"));
            return;
        }

        var value = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            isVerifiedVanillaValue ? null : editableField.MinimumValue,
            isVerifiedVanillaValue ? null : editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (value is not null && !isVerifiedVanillaValue)
        {
            _ = ValidateOptionValue(editableField, value.Value, diagnostics);
        }
    }

    private static bool TryReadPlayerPartnerField(
        ZaPokemonDataEntry row,
        string? field,
        out int value)
    {
        var moves = row.WazaList?.Values.Take(4).ToArray() ?? [0, 0, 0, 0];
        var ivs = row.TalentValue;
        switch (field)
        {
            case ZaEncountersWorkflowService.SpeciesIdField:
                value = row.DevNo;
                return true;
            case ZaEncountersWorkflowService.FormField:
                value = row.FormNo;
                return true;
            case ZaEncountersWorkflowService.PlayerPartnerLevelField when row.MinLevel == row.MaxLevel:
                value = row.MinLevel;
                return true;
            case ZaEncountersWorkflowService.HeldItemIdField:
                value = row.HoldItem ?? 0;
                return true;
            case ZaEncountersWorkflowService.AbilityField:
                value = row.Tokusei;
                return true;
            case ZaEncountersWorkflowService.NatureField:
                value = row.Seikaku;
                return true;
            case ZaEncountersWorkflowService.GenderField:
                value = row.Sex;
                return true;
            case ZaEncountersWorkflowService.ShinyModeField:
                value = row.Rare;
                return true;
            case ZaEncountersWorkflowService.Move1IdField:
                value = moves[0];
                return true;
            case ZaEncountersWorkflowService.Move2IdField:
                value = moves[1];
                return true;
            case ZaEncountersWorkflowService.Move3IdField:
                value = moves[2];
                return true;
            case ZaEncountersWorkflowService.Move4IdField:
                value = moves[3];
                return true;
            case ZaEncountersWorkflowService.IvHpField when ivs is not null:
                value = ivs.HP;
                return true;
            case ZaEncountersWorkflowService.IvAttackField when ivs is not null:
                value = ivs.Attack;
                return true;
            case ZaEncountersWorkflowService.IvDefenseField when ivs is not null:
                value = ivs.Defense;
                return true;
            case ZaEncountersWorkflowService.IvSpecialAttackField when ivs is not null:
                value = ivs.SpecialAttack;
                return true;
            case ZaEncountersWorkflowService.IvSpecialDefenseField when ivs is not null:
                value = ivs.SpecialDefense;
                return true;
            case ZaEncountersWorkflowService.IvSpeedField when ivs is not null:
                value = ivs.Speed;
                return true;
            case ZaEncountersWorkflowService.VanillaTalentScaleField:
                value = row.TalentScale;
                return true;
            case ZaEncountersWorkflowService.VanillaTalentVCountField:
                value = row.TalentVNum;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool ValidateOptionValue(
        ZaEncounterEditableField field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field.Options.Count == 0
            || field.Options.Any(option => option.Value == value))
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Encounter field '{field.Label}' must use one of its supported options.",
            ZaEditSessionSupport.EncountersDomain,
            field: field.Field,
            expected: field.Options.Count <= 12
                ? string.Join(
                    ", ",
                    field.Options.Select(option => option.Value.ToString(CultureInfo.InvariantCulture)))
                : $"One of {field.Options.Count.ToString(CultureInfo.InvariantCulture)} supported values"));
        return false;
    }

    private static ZaEncounterEditableField? ResolveEditableField(
        ZaEncountersWorkflow workflow,
        string? field,
        bool allowVerifiedVanillaSharedValue)
    {
        var editableField = ZaEncountersWorkflowService.GetEditableField(workflow, field);
        if (editableField is not null || !allowVerifiedVanillaSharedValue)
        {
            return editableField;
        }

        return field switch
        {
            ZaEncountersWorkflowService.VanillaTalentScaleField =>
                new ZaEncounterEditableField(
                    field,
                    "Talent scale",
                    "integer",
                    null,
                    null,
                    Array.Empty<ZaEncounterEditableFieldOption>()),
            ZaEncountersWorkflowService.VanillaTalentVCountField =>
                new ZaEncounterEditableField(
                    field,
                    "Talent V count",
                    "integer",
                    null,
                    null,
                    Array.Empty<ZaEncounterEditableFieldOption>()),
            _ => null,
        };
    }

    private static bool ValidateSharedAlphaChance(
        ZaEncountersWorkflow workflow,
        int sourceIndex,
        string? field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(field, ZaEncountersWorkflowService.AlphaChancePercentField, StringComparison.Ordinal))
        {
            return true;
        }

        var linkedSlots = workflow.Tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.PokemonDataSourceIndex == sourceIndex)
            .ToArray();
        if (linkedSlots.Any(slot => slot.AlphaChancePercent is null))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shared Alpha chance is read-only because the source encounter row does not contain a whole-number percentage from 0 through 100.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "Preserve the source value or restore a whole-number shared Alpha chance before editing"));
            return false;
        }

        var hasStructuralAlphaReference = linkedSlots.Any(slot => slot.IsAlpha);
        var hasOrdinaryReference = linkedSlots.Any(slot => !slot.IsAlpha);
        if (hasStructuralAlphaReference && hasOrdinaryReference)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shared Alpha chance cannot be edited because this encounter row is linked by both structural _Alpha and ordinary references.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "Encounter row linked only by structural _Alpha references or only by ordinary references"));
            return false;
        }

        var hasGuaranteedAlphaChance = linkedSlots.Any(slot => slot.AlphaChancePercent == 100);
        if (hasGuaranteedAlphaChance && value != 100)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Guaranteed Alpha encounter rows must keep their shared Alpha chance at 100 percent.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "100 for an existing 100-percent encounter row"));
            return false;
        }

        if (!hasGuaranteedAlphaChance && value > 99)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Non-guaranteed encounter rows must keep their shared Alpha chance between 0 and 99 percent.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "Whole-number percent from 0 through 99 for a non-guaranteed encounter row"));
            return false;
        }

        return true;
    }

    private static bool AffectsSharedLevelRange(string? field)
    {
        return field is ZaEncountersWorkflowService.LevelMaxField
            or ZaEncountersWorkflowService.LevelMinField
            or ZaEncountersWorkflowService.AlphaChancePercentField
            or ZaEncountersWorkflowService.AlphaLevelBonusField;
    }

    private static bool AffectsSharedPokemonData(string? field)
    {
        return field is ZaEncountersWorkflowService.SpeciesIdField
            or ZaEncountersWorkflowService.FormField
            or ZaEncountersWorkflowService.LevelMinField
            or ZaEncountersWorkflowService.LevelMaxField
            or ZaEncountersWorkflowService.AlphaChancePercentField
            or ZaEncountersWorkflowService.AlphaLevelBonusField
            or ZaEncountersWorkflowService.HeldItemIdField
            or ZaEncountersWorkflowService.AbilityField
            or ZaEncountersWorkflowService.NatureField
            or ZaEncountersWorkflowService.GenderField
            or ZaEncountersWorkflowService.ShinyModeField
            or ZaEncountersWorkflowService.Move1IdField
            or ZaEncountersWorkflowService.Move2IdField
            or ZaEncountersWorkflowService.Move3IdField
            or ZaEncountersWorkflowService.Move4IdField
            or ZaEncountersWorkflowService.FlawlessIvCountField
            or ZaEncountersWorkflowService.IvHpField
            or ZaEncountersWorkflowService.IvAttackField
            or ZaEncountersWorkflowService.IvDefenseField
            or ZaEncountersWorkflowService.IvSpecialAttackField
            or ZaEncountersWorkflowService.IvSpecialDefenseField
            or ZaEncountersWorkflowService.IvSpeedField
            or ZaEncountersWorkflowService.StrengthenHpField
            or ZaEncountersWorkflowService.StrengthenAttackField
            or ZaEncountersWorkflowService.StrengthenDefenseField
            or ZaEncountersWorkflowService.StrengthenSpecialAttackField
            or ZaEncountersWorkflowService.StrengthenSpecialDefenseField
            or ZaEncountersWorkflowService.StrengthenSpeedField
            or ZaEncountersWorkflowService.VanillaTalentScaleField
            or ZaEncountersWorkflowService.VanillaTalentVCountField;
    }

    private static bool IsStrengthenField(string? field)
    {
        return field is ZaEncountersWorkflowService.StrengthenHpField
            or ZaEncountersWorkflowService.StrengthenAttackField
            or ZaEncountersWorkflowService.StrengthenDefenseField
            or ZaEncountersWorkflowService.StrengthenSpecialAttackField
            or ZaEncountersWorkflowService.StrengthenSpecialDefenseField
            or ZaEncountersWorkflowService.StrengthenSpeedField;
    }

    private static bool AffectsSpawnerSlot(string? field)
    {
        return field is ZaEncountersWorkflowService.WeightField
            or ZaEncountersWorkflowService.SlotMaxCountField;
    }

    private static bool AffectsAppearanceCounts(string? field)
    {
        return field is ZaEncountersWorkflowService.AppearanceMinCountField
            or ZaEncountersWorkflowService.AppearanceMaxCountField;
    }

    private static bool AffectsSpawnerData(string? field)
    {
        return AffectsSpawnerSlot(field) || AffectsAppearanceCounts(field);
    }

    private static bool AffectsBossActionData(string? field)
    {
        return ZaScriptedBossActionCatalog.TryParseEditField(field, out _);
    }

    private static bool TargetsSelectedOwnership(
        ZaEncountersWorkflow workflow,
        ZaEncounterTableRecord targetTable,
        ZaEncounterSlotRecord targetSlot,
        PendingEdit edit)
    {
        if (!string.Equals(
                edit.Domain,
                ZaEditSessionSupport.EncountersDomain,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (AffectsSharedPokemonData(edit.Field))
        {
            return string.Equals(
                    edit.RecordId,
                    ZaEncountersWorkflowService.CreatePokemonDataRecordId(
                        targetSlot.PokemonDataSourceIndex),
                    StringComparison.Ordinal)
                || TryResolvePokemonDataSourceIndex(
                    workflow,
                    edit.RecordId,
                    out var sourceIndex)
                && sourceIndex == targetSlot.PokemonDataSourceIndex;
        }

        if (AffectsSpawnerSlot(edit.Field))
        {
            return string.Equals(
                    edit.RecordId,
                    ZaEncountersWorkflowService.CreateSlotRecordId(
                        targetTable.TableId,
                        targetSlot.Slot),
                    StringComparison.Ordinal)
                || TryResolveSpawnerSlot(
                    workflow,
                    edit.RecordId,
                    out var slotTable,
                    out var slot)
                && string.Equals(
                    slotTable.TableId,
                    targetTable.TableId,
                    StringComparison.Ordinal)
                && slot.Slot == targetSlot.Slot;
        }

        return AffectsAppearanceCounts(edit.Field)
            && (string.Equals(
                    edit.RecordId,
                    ZaEncountersWorkflowService.CreateAppearanceRecordId(
                        targetTable.TableId),
                    StringComparison.Ordinal)
                || TryResolveAppearanceTable(
                    workflow,
                    edit.RecordId,
                    out var appearanceTable)
                && string.Equals(
                    appearanceTable.TableId,
                    targetTable.TableId,
                    StringComparison.Ordinal));
    }

    private static bool HasVanillaRestoreSourceMarker(PendingEdit edit)
    {
        return HasBaseSource(edit, ZaDataPaths.EncountDataArray)
            && HasBaseSource(edit, ZaDataPaths.PokemonSpawnerDataArray);
    }

    private static bool HasPlayerPartnerVanillaRestoreSourceMarker(PendingEdit edit)
    {
        return edit.Sources.Any(source =>
            source.Layer == ProjectFileLayer.Base
            && string.Equals(
                source.RelativePath,
                ZaEncounterPlayerPartnerCatalog.VanillaRestoreMarker,
                StringComparison.Ordinal));
    }

    private static bool HasBaseSource(PendingEdit edit, string virtualPath)
    {
        var relativePath = $"romfs/{virtualPath}";
        return edit.Sources.Any(source =>
            source.Layer == ProjectFileLayer.Base
            && (string.Equals(
                    source.RelativePath,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    source.RelativePath,
                    virtualPath,
                    StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasSourceReference(PendingEdit edit, string virtualPath)
    {
        var relativePath = $"romfs/{virtualPath}";
        return edit.Sources.Any(source =>
            string.Equals(
                source.RelativePath.Replace('\\', '/'),
                relativePath,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                source.RelativePath.Replace('\\', '/'),
                virtualPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidateSpawnerFieldEditability(
        ZaEncounterSlotRecord slot,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        string? message = field switch
        {
            ZaEncountersWorkflowService.WeightField when !slot.CanEditWeight =>
                "Spawn weight is read-only because its source FlatBuffer scalar is omitted.",
            ZaEncountersWorkflowService.SlotMaxCountField when !slot.CanEditSlotMaxCount =>
                "Slot maximum count is read-only because its source FlatBuffer scalar is omitted.",
            ZaEncountersWorkflowService.AppearanceMinCountField
                or ZaEncountersWorkflowService.AppearanceMaxCountField
                when slot.AppearanceObjectCount == 0
                    || slot.AppearanceMinCount is null
                    || slot.AppearanceMaxCount is null =>
                "Overall encounter counts are read-only because this spawner has missing or mixed appearance count values.",
            ZaEncountersWorkflowService.AppearanceMinCountField
                when !slot.CanEditAppearanceMinCount =>
                "Overall minimum count is read-only because at least one source FlatBuffer scalar is omitted.",
            ZaEncountersWorkflowService.AppearanceMaxCountField
                when !slot.CanEditAppearanceMaxCount =>
                "Overall maximum count is read-only because at least one source FlatBuffer scalar is omitted.",
            _ => null,
        };
        if (message is null)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Materialized source scalar storage for the requested spawner field"));
        return false;
    }

    private static bool ValidateStrengthenFieldEditability(
        ZaEncounterSlotRecord slot,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (slot.CanEditStrengthenValues)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Strengthen multipliers are read-only because the source encounter row does not contain six non-negative, runtime-representable values.",
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Materialized StrengthenValue data with HP from 0 through 65535 and the other stats from 0 through 255"));
        return false;
    }

    private static bool ValidateStrengthenPendingEditability(
        ZaEncountersWorkflow workflow,
        int sourceIndex,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editablePlacement = workflow.Tables
            .SelectMany(table => table.Slots)
            .Any(slot =>
                slot.PokemonDataSourceIndex == sourceIndex
                && slot.CanEditStrengthenValues);
        if (editablePlacement)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pending strengthen edit no longer targets a placed, materialized, runtime-representable StrengthenValue row.",
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Loaded encounter placement with six non-negative strengthen controls"));
        return false;
    }

    private static string GetSourcePathForField(string? field)
    {
        if (AffectsBossActionData(field))
        {
            return ZaDataPaths.BossMoveSelectorArray;
        }

        return AffectsSpawnerData(field)
            ? ZaDataPaths.PokemonSpawnerDataArray
            : ZaDataPaths.EncountDataArray;
    }

    private static bool IsPlayerPartnerEdit(PendingEdit edit)
    {
        return string.Equals(
                edit.Domain,
                ZaEditSessionSupport.EncountersDomain,
                StringComparison.Ordinal)
            && ZaEncounterPlayerPartnerCatalog.IsRecordId(edit.RecordId);
    }

    private static string GetSourcePathForEdit(PendingEdit edit)
    {
        return IsPlayerPartnerEdit(edit)
            ? ZaDataPaths.PokemonDataArray
            : GetSourcePathForField(edit.Field);
    }

    private static bool ValidateSharedAlphaLevelBonus(
        ZaEncountersWorkflow workflow,
        int sourceIndex,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(field, ZaEncountersWorkflowService.AlphaLevelBonusField, StringComparison.Ordinal))
        {
            return true;
        }

        var hasUnsupportedBonus = workflow.Tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.PokemonDataSourceIndex == sourceIndex)
            .Any(slot => slot.AlphaLevelBonus is null);
        if (!hasUnsupportedBonus)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Shared Alpha level bonus is read-only because the source encounter row is outside the supported range from 0 through 100.",
            ZaEditSessionSupport.EncountersDomain,
            field: ZaEncountersWorkflowService.AlphaLevelBonusField,
            expected: "Preserve the unsupported source value"));
        return false;
    }

    private static void ValidateFinalSharedLevelRanges(
        ZaEncountersWorkflow workflow,
        IEnumerable<int> sourceIndexes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var slotsBySourceIndex = workflow.Tables
            .SelectMany(table => table.Slots)
            .GroupBy(slot => slot.PokemonDataSourceIndex)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var sourceIndex in sourceIndexes.Distinct())
        {
            if (!slotsBySourceIndex.TryGetValue(sourceIndex, out var slot))
            {
                continue;
            }

            if (slot.LevelMin > slot.LevelMax)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Shared encounter level range is invalid: minimum {slot.LevelMin.ToString(CultureInfo.InvariantCulture)} "
                    + $"is greater than maximum {slot.LevelMax.ToString(CultureInfo.InvariantCulture)} for every linked placement.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.LevelMinField,
                    expected: "Shared minimum level less than or equal to shared maximum level after all batch updates"));
                continue;
            }

            if (!slot.HasAlphaChance)
            {
                continue;
            }

            if (slot.AlphaLevelBonus is not int alphaLevelBonus)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shared Alpha level range cannot be changed while its source Alpha level bonus is outside the supported range from 0 through 100.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AlphaLevelBonusField,
                    expected: "Preserve the unsupported source bonus or disable Alpha chance before changing the shared Alpha level range"));
                continue;
            }

            var alphaLevelMaximum = (long)slot.LevelMax + alphaLevelBonus;
            if (alphaLevelMaximum <= 100)
            {
                continue;
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shared Alpha level range is invalid: base maximum {slot.LevelMax.ToString(CultureInfo.InvariantCulture)} "
                + $"plus bonus {alphaLevelBonus.ToString(CultureInfo.InvariantCulture)} would produce level {alphaLevelMaximum.ToString(CultureInfo.InvariantCulture)} "
                + "for every linked placement.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaLevelBonusField,
                expected: "When shared Alpha chance is above 0 percent, base maximum level plus Alpha level bonus must be at most 100"));
        }
    }

    private static void ValidateFinalSpawnerCounts(
        ZaEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targets = edits
            .Where(edit => AffectsSpawnerData(edit.Field))
            .Select(edit => TryResolveSpawnerTableId(workflow, edit.RecordId, out var tableId)
                ? (TableId: tableId, edit.Field, edit.RecordId)
                : (TableId: string.Empty, edit.Field, edit.RecordId))
            .Where(target => !string.IsNullOrWhiteSpace(target.TableId))
            .GroupBy(target => target.TableId, StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var table = workflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, target.Key, StringComparison.Ordinal));
            if (table is null)
            {
                continue;
            }

            var validatesSlotCounts = target.Any(candidate =>
                candidate.Field is ZaEncountersWorkflowService.SlotMaxCountField);
            var validatesAppearanceCounts = target.Any(candidate =>
                candidate.Field is ZaEncountersWorkflowService.AppearanceMinCountField
                    or ZaEncountersWorkflowService.AppearanceMaxCountField);
            var validatesCounts = validatesSlotCounts || validatesAppearanceCounts;
            var validatesWeights = target.Any(candidate =>
                candidate.Field is ZaEncountersWorkflowService.WeightField);
            var changedSlots = target
                .Where(candidate =>
                    candidate.Field is ZaEncountersWorkflowService.WeightField
                        or ZaEncountersWorkflowService.SlotMaxCountField)
                .Select(candidate => TryResolveSpawnerSlot(
                    workflow,
                    candidate.RecordId,
                    out _,
                    out var slot)
                        ? slot
                        : null)
                .OfType<ZaEncounterSlotRecord>()
                .DistinctBy(slot => slot.Slot)
                .ToArray();

            if (validatesWeights && table.Slots.All(slot => slot.Weight == 0))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Every slot in this spawner has weight 0, so no weighted candidate may be selectable.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.WeightField,
                    expected: "At least one positive slot weight when the spawner should remain active"));
            }

            if (validatesSlotCounts)
            {
                var highSlotCount = table.Slots
                    .Where(slot => slot.SlotMaxCount > 6)
                    .Select(slot => slot.SlotMaxCount)
                    .DefaultIfEmpty()
                    .Max();
                if (highSlotCount > 6)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Slot maximum count {highSlotCount.ToString(CultureInfo.InvariantCulture)} exceeds the highest slot count observed in vanilla data (6). "
                        + "KM Editor will preserve the requested raw value.",
                        ZaEditSessionSupport.EncountersDomain,
                        field: ZaEncountersWorkflowService.SlotMaxCountField,
                    expected: "Counts through 6 match the vanilla-observed range"));
                }
            }

            foreach (var slot in changedSlots)
            {
                var displayedSlot = slot.Slot + 1;
                if (slot.Weight == 0 && slot.SlotMaxCount > 0)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Slot {displayedSlot.ToString(CultureInfo.InvariantCulture)} has weight 0 but a positive maximum count, "
                        + "so it normally will not be selected.",
                        ZaEditSessionSupport.EncountersDomain,
                        field: ZaEncountersWorkflowService.WeightField,
                        expected: "Positive weight when a slot should contribute spawns"));
                }

                if (slot.Weight > 0 && slot.SlotMaxCount == 0)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Slot {displayedSlot.ToString(CultureInfo.InvariantCulture)} has a positive weight but maximum count 0, "
                        + "so it may not contribute a spawn.",
                        ZaEditSessionSupport.EncountersDomain,
                        field: ZaEncountersWorkflowService.SlotMaxCountField,
                        expected: "Positive slot maximum count when a weighted slot should contribute spawns"));
                }
            }

            var firstSlot = table.Slots.FirstOrDefault();
            if (!validatesCounts
                || firstSlot?.AppearanceMinCount is not int minimum
                || firstSlot.AppearanceMaxCount is not int maximum)
            {
                continue;
            }

            if (minimum > maximum)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Overall encounter count range is invalid: minimum {minimum.ToString(CultureInfo.InvariantCulture)} "
                    + $"is greater than maximum {maximum.ToString(CultureInfo.InvariantCulture)}.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AppearanceMinCountField,
                    expected: "Overall minimum count less than or equal to overall maximum count"));
            }

            if (validatesAppearanceCounts && (minimum > 6 || maximum > 6))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Overall encounter count range {minimum.ToString(CultureInfo.InvariantCulture)} through "
                    + $"{maximum.ToString(CultureInfo.InvariantCulture)} exceeds the highest per-appearance count observed in vanilla data (6). "
                    + "KM Editor will preserve the requested raw values.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AppearanceMaxCountField,
                    expected: "Counts through 6 match the vanilla-observed range"));
            }

            if (maximum == 0)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Overall maximum count is 0, so this spawner may not create any Pokemon.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AppearanceMaxCountField,
                    expected: "Positive overall maximum count when the spawner should remain active"));
            }

            var capacityCandidates = validatesAppearanceCounts
                ? table.Slots
                : changedSlots;
            var slotAboveOverallMaximum = capacityCandidates
                .Where(slot => slot.SlotMaxCount > maximum)
                .OrderByDescending(slot => slot.SlotMaxCount)
                .FirstOrDefault();
            if (slotAboveOverallMaximum is not null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Slot {(slotAboveOverallMaximum.Slot + 1).ToString(CultureInfo.InvariantCulture)} maximum count "
                    + $"{slotAboveOverallMaximum.SlotMaxCount.ToString(CultureInfo.InvariantCulture)} is above the overall maximum "
                    + $"{maximum.ToString(CultureInfo.InvariantCulture)}. The overall population cap is reached first.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.SlotMaxCountField,
                    expected: "Slot maximum count no greater than the overall maximum count when both caps should be reachable"));
            }

            var totalSlotCapacity = table.Slots.Sum(slot => (long)slot.SlotMaxCount);
            if (totalSlotCapacity < minimum)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Combined slot capacity {totalSlotCapacity.ToString(CultureInfo.InvariantCulture)} "
                    + $"is below overall minimum count {minimum.ToString(CultureInfo.InvariantCulture)}. "
                    + "KM Editor will preserve the requested raw values.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.SlotMaxCountField,
                    expected: "Combined slot maximum counts at least as large as the overall minimum count"));
            }
        }
    }

    private static void ValidateFinalSpeciesFormPairs(
        ZaEncountersWorkflow sourceWorkflow,
        ZaEncountersWorkflow projectedWorkflow,
        ICollection<ValidationDiagnostic> diagnostics,
        ZaEncounterVanillaRestoreCatalog? vanillaRestoreCatalog = null,
        IReadOnlySet<int>? verifiedVanillaSourceIndexes = null)
    {
        var projectedSlotsBySourceIndex = projectedWorkflow.Tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.PokemonDataSourceIndex >= 0)
            .GroupBy(slot => slot.PokemonDataSourceIndex)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var sourceGroup in sourceWorkflow.Tables
                     .SelectMany(table => table.Slots)
                     .Where(slot => slot.PokemonDataSourceIndex >= 0)
                     .GroupBy(slot => slot.PokemonDataSourceIndex))
        {
            var sourceSlot = sourceGroup.First();
            if (!projectedSlotsBySourceIndex.TryGetValue(
                    sourceSlot.PokemonDataSourceIndex,
                    out var projectedSlot))
            {
                continue;
            }

            if (verifiedVanillaSourceIndexes?.Contains(
                    sourceSlot.PokemonDataSourceIndex) == true
                && vanillaRestoreCatalog?.IsVerifiedBaseSpeciesForm(
                    sourceSlot.PokemonDataSourceIndex,
                    projectedSlot.SpeciesId,
                    projectedSlot.Form) == true)
            {
                continue;
            }

            ZaSpeciesFormPairValidation.ValidateChangedPair(
                sourceWorkflow.PokemonAvailability,
                sourceSlot.SpeciesId,
                sourceSlot.Form,
                projectedSlot.SpeciesId,
                projectedSlot.Form,
                ZaEditSessionSupport.EncountersDomain,
                $"Encounter data row '{sourceSlot.EncounterDataId}'",
                diagnostics,
                sourceSlot.PokemonProvenance.SourceFile,
                ZaEncountersWorkflowService.FormField);
        }
    }

    private static void ValidateFinalPlayerPartnerSpeciesForm(
        ZaEncountersWorkflow sourceWorkflow,
        ZaEncountersWorkflow projectedWorkflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = sourceWorkflow.Tables
            .Where(ZaEncounterPlayerPartnerCatalog.IsTargetTable)
            .Select(table => table.PlayerPartner)
            .FirstOrDefault(partner => partner is not null);
        var projected = projectedWorkflow.Tables
            .Where(ZaEncounterPlayerPartnerCatalog.IsTargetTable)
            .Select(table => table.PlayerPartner)
            .FirstOrDefault(partner => partner is not null);
        if (source is null || projected is null)
        {
            return;
        }

        ZaSpeciesFormPairValidation.ValidateChangedPair(
            sourceWorkflow.PokemonAvailability,
            source.SpeciesId,
            source.Form,
            projected.SpeciesId,
            projected.Form,
            ZaEditSessionSupport.EncountersDomain,
            "AZ's temporary Lucario",
            diagnostics,
            source.Provenance.SourceFile,
            ZaEncountersWorkflowService.FormField);
        if (projected.Level != projected.LevelMax
            || projected.Level is < 1 or > 100)
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "AZ's temporary partner must keep one fixed level from 1 through 100.",
                ZaEncountersWorkflowService.PlayerPartnerLevelField,
                "Matching minimum and maximum levels from 1 through 100"));
        }
    }

    private static void AppendOutzoneBehaviorWarnings(
        ZaEncountersWorkflow sourceWorkflow,
        ZaEncountersWorkflow projectedWorkflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var projectedSlotsBySourceIndex = projectedWorkflow.Tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.PokemonDataSourceIndex >= 0)
            .GroupBy(slot => slot.PokemonDataSourceIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var sourceConsumersBySourceIndex = sourceWorkflow.Tables
            .SelectMany(table => table.Slots
                .Where(slot => slot.PokemonDataSourceIndex >= 0)
                .Select(slot => (Table: table, Slot: slot)))
            .GroupBy(consumer => consumer.Slot.PokemonDataSourceIndex);

        foreach (var sourceConsumers in sourceConsumersBySourceIndex)
        {
            var sourceSlot = sourceConsumers.First().Slot;
            if (!projectedSlotsBySourceIndex.TryGetValue(
                    sourceSlot.PokemonDataSourceIndex,
                    out var projectedSlot)
                || sourceSlot.SpeciesId == projectedSlot.SpeciesId
                    && sourceSlot.Form == projectedSlot.Form
                || projectedSlot.SpeciesId == 0 && projectedSlot.Form == 0)
            {
                continue;
            }

            var cityConsumers = sourceConsumers
                .Where(consumer => consumer.Table.LocationKey?.StartsWith(
                    "outzone_",
                    StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
            if (cityConsumers.Length == 0)
            {
                continue;
            }

            var availability = sourceWorkflow.OutzoneAvailability;
            if (availability.HasKnownAvailability
                && availability.IsObserved(projectedSlot.SpeciesId, projectedSlot.Form))
            {
                continue;
            }

            var linkedPlacements = string.Join(
                ", ",
                cityConsumers
                    .Select(consumer =>
                    {
                        var location = string.IsNullOrWhiteSpace(consumer.Table.Location)
                            ? consumer.Table.TableId
                            : consumer.Table.Location;
                        var identity = string.IsNullOrWhiteSpace(consumer.Table.RawSpawnerId)
                            ? consumer.Table.TableId
                            : consumer.Table.RawSpawnerId;
                        return $"{location} [{identity}]";
                    })
                    .Distinct(StringComparer.Ordinal));
            var message = availability.HasKnownAvailability
                ? $"{projectedSlot.Species} (species {projectedSlot.SpeciesId}, "
                    + $"form {projectedSlot.Form}) is not used by any immutable base Lumiose "
                    + "City encounter outside Wild Zones. The game may not initialize its "
                    + "usual awareness or attack behavior for the linked placements: "
                    + $"{linkedPlacements}. KM Editor will preserve the requested species and form."
                : "City behavior compatibility could not be compared with immutable base "
                    + $"encounters for {projectedSlot.Species} (species "
                    + $"{projectedSlot.SpeciesId}, form {projectedSlot.Form}) at the linked "
                    + $"placements: {linkedPlacements}. KM Editor will preserve the requested "
                    + "species and form.";
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Warning,
                message,
                ZaEditSessionSupport.EncountersDomain,
                sourceSlot.PokemonProvenance.SourceFile,
                sourceSlot.SpeciesId != projectedSlot.SpeciesId
                    ? ZaEncountersWorkflowService.SpeciesIdField
                    : ZaEncountersWorkflowService.FormField,
                "An intentional custom city encounter; the edit remains allowed"));
        }
    }

    private static ZaEncountersWorkflow OverlayPendingEdits(
        ZaEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaEncountersWorkflow OverlayPendingEdit(ZaEncountersWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        if (ZaEncounterPlayerPartnerCatalog.IsRecordId(edit.RecordId))
        {
            return workflow with
            {
                Tables = workflow.Tables
                    .Select(table => table.PlayerPartner is { } partner
                        && ZaEncounterPlayerPartnerCatalog.IsTargetTable(table)
                            ? table with
                            {
                                PlayerPartner = OverlayPlayerPartner(
                                    workflow,
                                    partner,
                                    edit.Field,
                                    value),
                            }
                            : table)
                    .ToArray(),
            };
        }

        if (ZaScriptedBossActionCatalog.TryParseEditField(
                edit.Field,
                out var selectorActionId)
            && ZaScriptedBossActionCatalog.TryParseRecordId(
                edit.RecordId,
                out var recordSelectorActionId)
            && selectorActionId == recordSelectorActionId)
        {
            if (!TryResolveEditableBossActionOwners(
                    workflow,
                    selectorActionId,
                    out _,
                    out var variant))
            {
                return workflow;
            }

            var option = workflow.ScriptedBossMoveOptions.FirstOrDefault(candidate =>
                candidate.MoveId == value
                && candidate.Variant == variant);
            if (option is null)
            {
                return workflow;
            }

            return workflow with
            {
                ScriptedBosses = workflow.ScriptedBosses
                    .Select(profile => profile with
                    {
                        Actions = profile.Actions
                            .Select(action => action.SelectorActionId == selectorActionId
                                ? action with
                                {
                                    MoveId = option.MoveId,
                                    RuntimeMoveId = option.RuntimeMoveId,
                                    Variant = option.Variant,
                                    Name = option.Name,
                                    RuntimeState = ZaScriptedBossActionCatalog.WorkingRuntimeState,
                                }
                                : action)
                            .ToArray(),
                    })
                    .ToArray(),
            };
        }

        if (AffectsSharedPokemonData(edit.Field)
            && TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex))
        {
            return workflow with
            {
                Tables = workflow.Tables
                    .Select(table => table with
                    {
                        Slots = table.Slots
                            .Select(row => row.PokemonDataSourceIndex == sourceIndex
                                ? OverlaySlot(workflow, row, edit.Field, value)
                                : row)
                            .ToArray(),
                    })
                    .ToArray(),
            };
        }

        if (AffectsSpawnerSlot(edit.Field)
            && TryResolveSpawnerSlot(workflow, edit.RecordId, out var targetTable, out var targetSlot))
        {
            return workflow with
            {
                Tables = workflow.Tables
                    .Select(table => string.Equals(table.TableId, targetTable.TableId, StringComparison.Ordinal)
                        ? table with
                        {
                            Slots = table.Slots
                                .Select(slot => slot.Slot == targetSlot.Slot
                                    ? OverlaySlot(workflow, slot, edit.Field, value)
                                    : slot)
                                .ToArray(),
                        }
                        : table)
                    .ToArray(),
            };
        }

        if (!AffectsAppearanceCounts(edit.Field)
            || !TryResolveAppearanceTable(workflow, edit.RecordId, out var appearanceTable))
        {
            return workflow;
        }

        return workflow with
        {
            Tables = workflow.Tables
                .Select(table => string.Equals(table.TableId, appearanceTable.TableId, StringComparison.Ordinal)
                    ? table with
                    {
                        Slots = table.Slots
                            .Select(slot => OverlaySlot(workflow, slot, edit.Field, value))
                            .ToArray(),
                    }
                    : table)
                .ToArray(),
        };
    }

    private static ZaEncounterPlayerPartnerRecord OverlayPlayerPartner(
        ZaEncountersWorkflow workflow,
        ZaEncounterPlayerPartnerRecord partner,
        string? field,
        int value)
    {
        var updated = field switch
        {
            ZaEncountersWorkflowService.SpeciesIdField => partner with
            {
                SpeciesId = value,
                Species = ZaEncountersWorkflowService.FormatEncounterSpeciesLabel(
                    value,
                    partner.Form,
                    ResolveSpeciesName(workflow, value)),
            },
            ZaEncountersWorkflowService.FormField => partner with
            {
                Form = value,
                Species = ZaEncountersWorkflowService.FormatEncounterSpeciesLabel(
                    partner.SpeciesId,
                    value,
                    ResolveSpeciesName(workflow, partner.SpeciesId)),
            },
            ZaEncountersWorkflowService.PlayerPartnerLevelField => partner with
            {
                Level = value,
                LevelMax = value,
                CanEditLevel = value is >= 1 and <= 100,
            },
            ZaEncountersWorkflowService.HeldItemIdField => partner with { HeldItemId = value },
            ZaEncountersWorkflowService.AbilityField => partner with { Ability = value },
            ZaEncountersWorkflowService.NatureField => partner with { Nature = value },
            ZaEncountersWorkflowService.GenderField => partner with { Gender = value },
            ZaEncountersWorkflowService.ShinyModeField => partner with { ShinyMode = value },
            ZaEncountersWorkflowService.Move1IdField => OverlayPlayerPartnerMove(partner, 0, value),
            ZaEncountersWorkflowService.Move2IdField => OverlayPlayerPartnerMove(partner, 1, value),
            ZaEncountersWorkflowService.Move3IdField => OverlayPlayerPartnerMove(partner, 2, value),
            ZaEncountersWorkflowService.Move4IdField => OverlayPlayerPartnerMove(partner, 3, value),
            ZaEncountersWorkflowService.FlawlessIvCountField => partner with
            {
                FlawlessIvCount = value,
                IvHp = -1,
                IvAttack = -1,
                IvDefense = -1,
                IvSpecialAttack = -1,
                IvSpecialDefense = -1,
                IvSpeed = -1,
                TalentScale = value == 0
                    ? ZaPokemonDataIvEncoding.GameDefaultRandomMode
                    : ZaPokemonDataIvEncoding.FixedOrGuaranteedMode,
                TalentVCount = value,
            },
            ZaEncountersWorkflowService.IvHpField => OverlayPlayerPartnerIv(
                partner with { IvHp = value }),
            ZaEncountersWorkflowService.IvAttackField => OverlayPlayerPartnerIv(
                partner with { IvAttack = value }),
            ZaEncountersWorkflowService.IvDefenseField => OverlayPlayerPartnerIv(
                partner with { IvDefense = value }),
            ZaEncountersWorkflowService.IvSpecialAttackField => OverlayPlayerPartnerIv(
                partner with { IvSpecialAttack = value }),
            ZaEncountersWorkflowService.IvSpecialDefenseField => OverlayPlayerPartnerIv(
                partner with { IvSpecialDefense = value }),
            ZaEncountersWorkflowService.IvSpeedField => OverlayPlayerPartnerIv(
                partner with { IvSpeed = value }),
            ZaEncountersWorkflowService.VanillaTalentScaleField => partner with { TalentScale = value },
            ZaEncountersWorkflowService.VanillaTalentVCountField => partner with { TalentVCount = value },
            _ => partner,
        };

        return field is ZaEncountersWorkflowService.SpeciesIdField
            or ZaEncountersWorkflowService.FormField
                ? updated with
                {
                    FormOptions = ZaEncountersWorkflowService.CreateFormOptions(
                        updated.SpeciesId,
                        ResolveSpeciesName(workflow, updated.SpeciesId),
                        workflow.PokemonAvailability),
                }
                : updated;
    }

    private static ZaEncounterPlayerPartnerRecord OverlayPlayerPartnerMove(
        ZaEncounterPlayerPartnerRecord partner,
        int moveIndex,
        int moveId)
    {
        var moves = partner.MoveIds.Take(4).ToArray();
        if (moves.Length < 4)
        {
            Array.Resize(ref moves, 4);
        }

        moves[moveIndex] = moveId;
        return partner with
        {
            MoveIds = moves,
            HasExplicitMoves = moves.Any(move => move != ZaPokemonDataConstants.MoveAuto),
        };
    }

    private static ZaEncounterPlayerPartnerRecord OverlayPlayerPartnerIv(
        ZaEncounterPlayerPartnerRecord partner)
    {
        return partner with
        {
            FlawlessIvCount = null,
            TalentScale = ZaPokemonDataIvEncoding.FixedOrGuaranteedMode,
            TalentVCount = 0,
        };
    }

    private static ZaEncounterSlotRecord OverlaySlot(
        ZaEncountersWorkflow workflow,
        ZaEncounterSlotRecord slot,
        string? field,
        int value)
    {
        var updatedSlot = field switch
        {
            ZaEncountersWorkflowService.SpeciesIdField => slot with
            {
                SpeciesId = value,
                Species = ZaEncountersWorkflowService.FormatEncounterSpeciesLabel(
                    value,
                    slot.Form,
                    ResolveSpeciesName(workflow, value)),
            },
            ZaEncountersWorkflowService.FormField => slot with
            {
                Form = value,
                Species = ZaEncountersWorkflowService.FormatEncounterSpeciesLabel(
                    slot.SpeciesId,
                    value,
                    ResolveSpeciesName(workflow, slot.SpeciesId)),
            },
            ZaEncountersWorkflowService.LevelMinField => slot with { LevelMin = value },
            ZaEncountersWorkflowService.LevelMaxField => slot with { LevelMax = value },
            ZaEncountersWorkflowService.AlphaChancePercentField => slot with
            {
                AlphaChancePercent = value,
                HasAlphaChance = value > 0,
                EncounterKind = value switch
                {
                    100 => "Guaranteed Alpha",
                    > 0 => "Alpha Chance",
                    _ => "Wild",
                },
            },
            ZaEncountersWorkflowService.AlphaLevelBonusField => slot with { AlphaLevelBonus = value },
            ZaEncountersWorkflowService.HeldItemIdField => slot with { HeldItemId = value },
            ZaEncountersWorkflowService.AbilityField => slot with { Ability = value },
            ZaEncountersWorkflowService.NatureField => slot with { Nature = value },
            ZaEncountersWorkflowService.GenderField => slot with { Gender = value },
            ZaEncountersWorkflowService.ShinyModeField => slot with { ShinyMode = value },
            ZaEncountersWorkflowService.Move1IdField => OverlayMove(slot, 0, value),
            ZaEncountersWorkflowService.Move2IdField => OverlayMove(slot, 1, value),
            ZaEncountersWorkflowService.Move3IdField => OverlayMove(slot, 2, value),
            ZaEncountersWorkflowService.Move4IdField => OverlayMove(slot, 3, value),
            ZaEncountersWorkflowService.FlawlessIvCountField => slot with
            {
                FlawlessIvCount = value,
                IvHp = -1,
                IvAttack = -1,
                IvDefense = -1,
                IvSpecialAttack = -1,
                IvSpecialDefense = -1,
                IvSpeed = -1,
                TalentScale = value == 0
                    ? ZaPokemonDataIvEncoding.GameDefaultRandomMode
                    : ZaPokemonDataIvEncoding.FixedOrGuaranteedMode,
                TalentVCount = value,
            },
            ZaEncountersWorkflowService.IvHpField => OverlayIv(slot, value, iv => slot with { IvHp = iv }),
            ZaEncountersWorkflowService.IvAttackField => OverlayIv(slot, value, iv => slot with { IvAttack = iv }),
            ZaEncountersWorkflowService.IvDefenseField => OverlayIv(slot, value, iv => slot with { IvDefense = iv }),
            ZaEncountersWorkflowService.IvSpecialAttackField => OverlayIv(slot, value, iv => slot with { IvSpecialAttack = iv }),
            ZaEncountersWorkflowService.IvSpecialDefenseField => OverlayIv(slot, value, iv => slot with { IvSpecialDefense = iv }),
            ZaEncountersWorkflowService.IvSpeedField => OverlayIv(slot, value, iv => slot with { IvSpeed = iv }),
            ZaEncountersWorkflowService.StrengthenHpField => OverlayStrengthen(slot with { StrengthenHp = value }),
            ZaEncountersWorkflowService.StrengthenAttackField => OverlayStrengthen(slot with { StrengthenAttack = value }),
            ZaEncountersWorkflowService.StrengthenDefenseField => OverlayStrengthen(slot with { StrengthenDefense = value }),
            ZaEncountersWorkflowService.StrengthenSpecialAttackField => OverlayStrengthen(slot with { StrengthenSpecialAttack = value }),
            ZaEncountersWorkflowService.StrengthenSpecialDefenseField => OverlayStrengthen(slot with { StrengthenSpecialDefense = value }),
            ZaEncountersWorkflowService.StrengthenSpeedField => OverlayStrengthen(slot with { StrengthenSpeed = value }),
            ZaEncountersWorkflowService.VanillaTalentScaleField => slot with { TalentScale = value },
            ZaEncountersWorkflowService.VanillaTalentVCountField => slot with { TalentVCount = value },
            ZaEncountersWorkflowService.WeightField => slot with { Weight = value },
            ZaEncountersWorkflowService.SlotMaxCountField => slot with { SlotMaxCount = value },
            ZaEncountersWorkflowService.AppearanceMinCountField => slot with { AppearanceMinCount = value },
            ZaEncountersWorkflowService.AppearanceMaxCountField => slot with { AppearanceMaxCount = value },
            _ => slot,
        };

        return field is ZaEncountersWorkflowService.SpeciesIdField
            or ZaEncountersWorkflowService.FormField
            ? updatedSlot with
            {
                FormOptions = ZaEncountersWorkflowService.CreateFormOptions(
                    updatedSlot.SpeciesId,
                    ResolveSpeciesName(workflow, updatedSlot.SpeciesId),
                    workflow.PokemonAvailability),
            }
            : updatedSlot;
    }

    private static ZaEncounterSlotRecord OverlayMove(
        ZaEncounterSlotRecord slot,
        int moveIndex,
        int moveId)
    {
        var moves = (slot.MoveIds ?? [0, 0, 0, 0]).Take(4).ToArray();
        if (moves.Length < 4)
        {
            Array.Resize(ref moves, 4);
        }

        moves[moveIndex] = moveId;
        return slot with
        {
            MoveIds = moves,
            HasExplicitMoves = moves.Any(move => move != ZaPokemonDataConstants.MoveAuto),
        };
    }

    private static ZaEncounterSlotRecord OverlayIv(
        ZaEncounterSlotRecord slot,
        int value,
        Func<int, ZaEncounterSlotRecord> update)
    {
        return update(value) with
        {
            FlawlessIvCount = null,
            TalentScale = ZaPokemonDataIvEncoding.FixedOrGuaranteedMode,
            TalentVCount = 0,
        };
    }

    private static ZaEncounterSlotRecord OverlayStrengthen(ZaEncounterSlotRecord slot)
    {
        return slot with
        {
            StrengthenValueSummary = ZaEncountersWorkflowService.FormatStrengthenValues(
                slot.StrengthenHp,
                slot.StrengthenAttack,
                slot.StrengthenDefense,
                slot.StrengthenSpecialAttack,
                slot.StrengthenSpecialDefense,
                slot.StrengthenSpeed),
        };
    }

    private static string ResolveSpeciesName(ZaEncountersWorkflow workflow, int speciesId)
    {
        if (speciesId == 0)
        {
            return "Empty";
        }

        var speciesField = workflow.EditableFields.FirstOrDefault(field =>
            string.Equals(field.Field, ZaEncountersWorkflowService.SpeciesIdField, StringComparison.Ordinal));
        var option = speciesField?.Options.FirstOrDefault(candidate => candidate.Value == speciesId);
        if (option is null)
        {
            return ZaLabels.Pokemon(speciesId);
        }

        var prefix = speciesId.ToString(CultureInfo.InvariantCulture);
        return option.Label.StartsWith(prefix, StringComparison.Ordinal)
            ? option.Label[prefix.Length..].Trim()
            : option.Label;
    }

    private static void ApplySpawnerEdit(
        ZaEncountersWorkflow workflow,
        ZaPokemonSpawnerDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !AffectsSpawnerData(edit.Field)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending spawner edit is not valid for apply.",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Valid Pokemon Legends Z-A spawner edit"));
            return;
        }

        if (AffectsSpawnerSlot(edit.Field))
        {
            if (!TryResolveSpawnerSlot(workflow, edit.RecordId, out var table, out var slot)
                || !ZaEncountersWorkflowService.TryParseTableId(
                    table.TableId,
                    out var groupIndex,
                    out var spawnerIndex))
            {
                diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
                return;
            }

            var entry = document.Entries.FirstOrDefault(candidate =>
                candidate.GroupIndex == groupIndex && candidate.SpawnerIndex == spawnerIndex);
            var sourceSlot = entry?.EncountDataInfoList.FirstOrDefault(candidate =>
                candidate is not null && candidate.SlotIndex == slot.Slot);
            if (sourceSlot is null)
            {
                diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
                return;
            }

            bool changed;
            string? error;
            switch (edit.Field)
            {
                case ZaEncountersWorkflowService.WeightField:
                    if (sourceSlot.Weight == value)
                    {
                        return;
                    }

                    changed = document.TrySetSlotWeight(
                        groupIndex,
                        spawnerIndex,
                        slot.Slot,
                        value,
                        out error);
                    break;
                case ZaEncountersWorkflowService.SlotMaxCountField:
                    if (sourceSlot.MaxCount == value)
                    {
                        return;
                    }

                    changed = document.TrySetSlotMaxCount(
                        groupIndex,
                        spawnerIndex,
                        slot.Slot,
                        value,
                        out error);
                    break;
                default:
                    diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                    return;
            }

            if (!changed)
            {
                diagnostics.Add(CreateSpawnerPatchDiagnostic(edit.Field, error));
            }

            return;
        }

        if (!TryResolveAppearanceTable(workflow, edit.RecordId, out var appearanceTable)
            || !ZaEncountersWorkflowService.TryParseTableId(
                appearanceTable.TableId,
                out var appearanceGroupIndex,
                out var appearanceSpawnerIndex))
        {
            diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
            return;
        }

        var spawnerEntry = document.Entries.FirstOrDefault(candidate =>
            candidate.GroupIndex == appearanceGroupIndex
            && candidate.SpawnerIndex == appearanceSpawnerIndex);
        if (spawnerEntry is null || spawnerEntry.AppearanceSpawnerObjectInfoList.Count == 0)
        {
            diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
            return;
        }

        foreach (var appearance in spawnerEntry.AppearanceSpawnerObjectInfoList)
        {
            if (appearance?.AppearanceInfo is null)
            {
                diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
                return;
            }

            bool changed;
            string? error;
            switch (edit.Field)
            {
                case ZaEncountersWorkflowService.AppearanceMinCountField:
                    if (appearance.AppearanceInfo.MinCount == value)
                    {
                        continue;
                    }

                    changed = document.TrySetAppearanceMinCount(
                        appearanceGroupIndex,
                        appearanceSpawnerIndex,
                        appearance.AppearanceIndex,
                        value,
                        out error);
                    break;
                case ZaEncountersWorkflowService.AppearanceMaxCountField:
                    if (appearance.AppearanceInfo.MaxCount == value)
                    {
                        continue;
                    }

                    changed = document.TrySetAppearanceMaxCount(
                        appearanceGroupIndex,
                        appearanceSpawnerIndex,
                        appearance.AppearanceIndex,
                        value,
                        out error);
                    break;
                default:
                    diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                    return;
            }

            if (!changed)
            {
                diagnostics.Add(CreateSpawnerPatchDiagnostic(edit.Field, error));
                return;
            }
        }
    }

    private static ValidationDiagnostic CreateMissingSpawnerTargetDiagnostic(string? field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pending encounter edit target is not present in the source spawner data array.",
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Existing spawner slot or appearance object");
    }

    private static ValidationDiagnostic CreateSpawnerPatchDiagnostic(string? field, string? error)
    {
        var detail = string.IsNullOrWhiteSpace(error)
            ? "The source scalar is not safely editable."
            : error.Trim();
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Spawner field '{field}' could not be changed. {detail}",
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Materialized 32-bit spawner scalar in the source data");
    }

    private static void ApplyBossActionEdit(
        ZaEncountersWorkflow workflow,
        ZaBossMoveSelectorDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!ZaScriptedBossActionCatalog.TryParseEditField(
                edit.Field,
                out var selectorActionId)
            || !ZaScriptedBossActionCatalog.TryParseRecordId(
                edit.RecordId,
                out var recordSelectorActionId)
            || selectorActionId != recordSelectorActionId
            || !int.TryParse(
                edit.NewValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var moveId)
            || moveId is < 0 or > ZaScriptedBossActionCatalog.MaximumBaseMoveId)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Pending boss action edit is not valid for apply.",
                edit.Field,
                "Matching selector action record and verified base move ID"));
            return;
        }

        if (!TryResolveEditableBossActionOwners(
                workflow,
                selectorActionId,
                out var ownedActions,
                out var variant))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Pending boss action replacement is no longer verified or editable.",
                edit.Field,
                "Owned data-driven selector whose owners share one editable move variant"));
            return;
        }

        var option = workflow.ScriptedBossMoveOptions.FirstOrDefault(candidate =>
            candidate.MoveId == moveId
            && candidate.Variant == variant);
        if (option is null)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Pending boss action replacement is no longer verified for its selector variant.",
                edit.Field,
                "Working move replacement for the selector's verified variant"));
            return;
        }

        var runtimeMoveId = ZaScriptedBossActionCatalog.ToRuntimeMoveId(moveId, variant);
        if (option.RuntimeMoveId != runtimeMoveId)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Pending boss action replacement no longer maps to the reviewed runtime move ID.",
                edit.Field,
                runtimeMoveId.ToString(CultureInfo.InvariantCulture)));
            return;
        }

        if (!document.TryGetRow(selectorActionId, out var sourceRow)
            || ownedActions.Any(action => action.RuntimeMoveId != sourceRow.RuntimeMoveId))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Boss action selector ownership changed after the workflow was loaded.",
                edit.Field,
                "Selector row matching the reviewed controller action"));
            return;
        }

        if (!document.TrySetRuntimeMoveId(
                selectorActionId,
                runtimeMoveId,
                out var error))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                string.IsNullOrWhiteSpace(error)
                    ? "Boss action selector could not be updated safely."
                    : $"Boss action selector could not be updated safely. {error}",
                edit.Field,
                "Exclusive materialized move ID storage for the selector action"));
        }
    }

    private static bool VerifyBossActionOutput(
        ZaEncountersWorkflow workflow,
        byte[] originalBytes,
        byte[] outputBytes,
        IEnumerable<PendingEdit> pendingEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var expectedEdits = pendingEdits
            .Where(edit => AffectsBossActionData(edit.Field))
            .Select(edit =>
            {
                var hasActionId = ZaScriptedBossActionCatalog.TryParseEditField(
                    edit.Field,
                    out var actionId);
                var hasMoveId = int.TryParse(
                    edit.NewValue,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var moveId)
                    && moveId is >= 0 and <= ZaScriptedBossActionCatalog.MaximumBaseMoveId;
                var variant = default(int);
                var hasVariant = hasActionId
                    && TryResolveEditableBossActionOwners(
                        workflow,
                        actionId,
                        out _,
                        out variant);
                var hasOption = hasMoveId
                    && hasVariant
                    && workflow.ScriptedBossMoveOptions.Any(option =>
                        option.MoveId == moveId
                        && option.Variant == variant);
                return (
                    hasActionId,
                    actionId,
                    hasMoveId,
                    moveId,
                    hasVariant,
                    variant,
                    hasOption,
                    edit.Field);
            })
            .ToArray();
        if (expectedEdits.Length == 0
            || expectedEdits.Any(edit =>
                !edit.hasActionId
                || !edit.hasMoveId
                || !edit.hasVariant
                || !edit.hasOption))
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Boss action output verification did not receive a complete selector change set.",
                field: null,
                expected: "Complete reviewed boss action edits with verified move variants"));
            return false;
        }

        var conflictingEdit = expectedEdits
            .GroupBy(edit => edit.actionId)
            .FirstOrDefault(group => group
                .Select(edit => (edit.moveId, edit.variant))
                .Distinct()
                .Skip(1)
                .Any());
        if (conflictingEdit is not null)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Boss action output contains conflicting values for one selector action.",
                conflictingEdit.First().Field,
                "One final move assignment per selector action"));
            return false;
        }

        var expectedRuntimeMoveIds = expectedEdits
            .GroupBy(edit => edit.actionId)
            .ToDictionary(
                group => group.Key,
                group => ZaScriptedBossActionCatalog.ToRuntimeMoveId(
                    group.First().moveId,
                    group.First().variant));
        var original = ZaBossMoveSelectorDocument.Parse(originalBytes);
        var output = ZaBossMoveSelectorDocument.Parse(outputBytes);
        if (originalBytes.Length != outputBytes.Length
            || original.Rows.Count != output.Rows.Count)
        {
            diagnostics.Add(CreateBossActionDiagnostic(
                "Boss action selector output changed the source table shape.",
                field: null,
                expected: "Byte-preserving selector table with unchanged row count"));
            return false;
        }

        for (var index = 0; index < original.Rows.Count; index++)
        {
            var before = original.Rows[index];
            var after = output.Rows[index];
            if (before.GroupIndex != after.GroupIndex
                || before.RowIndex != after.RowIndex
                || before.ActionId != after.ActionId
                || before.LotteryType != after.LotteryType)
            {
                diagnostics.Add(CreateBossActionDiagnostic(
                    "Boss action selector output changed fixed controller selector identity or ordering.",
                    field: null,
                    expected: "Unchanged selector action IDs, lottery types, and row ordering"));
                return false;
            }

            var expectedRuntimeMoveId = expectedRuntimeMoveIds.GetValueOrDefault(
                before.ActionId,
                before.RuntimeMoveId);
            if (after.RuntimeMoveId != expectedRuntimeMoveId)
            {
                diagnostics.Add(CreateBossActionDiagnostic(
                    $"Boss action selector {before.ActionId.ToString(CultureInfo.InvariantCulture)} "
                        + "did not serialize the reviewed move assignment.",
                    ZaScriptedBossActionCatalog.CreateEditField(before.ActionId),
                    expectedRuntimeMoveId.ToString(CultureInfo.InvariantCulture)));
                return false;
            }
        }

        var allowedChangedBytePositions = new HashSet<int>();
        foreach (var selectorActionId in expectedRuntimeMoveIds.Keys)
        {
            if (!original.TryGetRow(selectorActionId, out var row)
                || row.MoveIdPosition is not { } moveIdPosition)
            {
                diagnostics.Add(CreateBossActionDiagnostic(
                    "Boss action selector output target is missing materialized move ID storage.",
                    ZaScriptedBossActionCatalog.CreateEditField(selectorActionId),
                    "Verified selector move ID field"));
                return false;
            }

            for (var offset = 0; offset < sizeof(int); offset++)
            {
                allowedChangedBytePositions.Add(moveIdPosition + offset);
            }
        }

        for (var index = 0; index < originalBytes.Length; index++)
        {
            if (originalBytes[index] != outputBytes[index]
                && !allowedChangedBytePositions.Contains(index))
            {
                diagnostics.Add(CreateBossActionDiagnostic(
                    "Boss action selector output changed bytes outside the reviewed move ID fields.",
                    field: null,
                    expected: "Only reviewed 32-bit selector move ID cells may change"));
                return false;
            }
        }

        return true;
    }

    private static void ApplyEdit(
        ZaEncountersWorkflow workflow,
        ZaEncounterDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit is not valid for apply.",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Valid Pokemon Legends Z-A encounter edit"));
            return;
        }

        var row = document.Entries
            .OfType<ZaEncounterDataEntry>()
            .FirstOrDefault(candidate => candidate.SourceIndex == sourceIndex);
        if (row is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit target is not present in the source encounter data array.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing linked encounter data row"));
            return;
        }

        ApplyField(row, edit.Field, value);
    }

    private static void ApplyPlayerPartnerEdit(
        ZaPokemonDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsPlayerPartnerEdit(edit)
            || !int.TryParse(
                edit.NewValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value))
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "Pending temporary partner edit is not valid for apply.",
                edit.Field,
                "Exact vsmega_init_rukario source identity and supported integer field"));
            return;
        }

        if (!ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
                document,
                out var row,
                out var blockedReason))
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                "Pending temporary partner edit target is no longer exact. " + blockedReason,
                edit.Field,
                "Unique vsmega_init_rukario row at source index 772"));
            return;
        }

        if (!ApplyPlayerPartnerField(row, edit.Field, value))
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                $"Temporary partner field '{edit.Field ?? "(missing)"}' is not supported for apply.",
                edit.Field,
                "Supported AZ's Lucario field"));
        }
    }

    private static bool ApplyPlayerPartnerField(
        ZaPokemonDataEntry row,
        string? field,
        int value)
    {
        switch (field)
        {
            case ZaEncountersWorkflowService.SpeciesIdField:
                row.DevNo = value;
                return true;
            case ZaEncountersWorkflowService.FormField:
                row.FormNo = value;
                return true;
            case ZaEncountersWorkflowService.PlayerPartnerLevelField:
                row.MinLevel = value;
                row.MaxLevel = value;
                return true;
            case ZaEncountersWorkflowService.HeldItemIdField:
                row.HoldItem = value == 0 ? null : value;
                return true;
            case ZaEncountersWorkflowService.AbilityField:
                row.Tokusei = value;
                return true;
            case ZaEncountersWorkflowService.NatureField:
                row.Seikaku = value;
                return true;
            case ZaEncountersWorkflowService.GenderField:
                row.Sex = value;
                return true;
            case ZaEncountersWorkflowService.ShinyModeField:
                row.Rare = value;
                return true;
            case ZaEncountersWorkflowService.Move1IdField:
                SetMove(row, 0, value);
                return true;
            case ZaEncountersWorkflowService.Move2IdField:
                SetMove(row, 1, value);
                return true;
            case ZaEncountersWorkflowService.Move3IdField:
                SetMove(row, 2, value);
                return true;
            case ZaEncountersWorkflowService.Move4IdField:
                SetMove(row, 3, value);
                return true;
            case ZaEncountersWorkflowService.FlawlessIvCountField:
                ZaPokemonDataIvEncoding.SetPreset(row, value);
                return true;
            case ZaEncountersWorkflowService.IvHpField:
                SetIv(row, ivs => ivs with { HP = value });
                return true;
            case ZaEncountersWorkflowService.IvAttackField:
                SetIv(row, ivs => ivs with { Attack = value });
                return true;
            case ZaEncountersWorkflowService.IvDefenseField:
                SetIv(row, ivs => ivs with { Defense = value });
                return true;
            case ZaEncountersWorkflowService.IvSpecialAttackField:
                SetIv(row, ivs => ivs with { SpecialAttack = value });
                return true;
            case ZaEncountersWorkflowService.IvSpecialDefenseField:
                SetIv(row, ivs => ivs with { SpecialDefense = value });
                return true;
            case ZaEncountersWorkflowService.IvSpeedField:
                SetIv(row, ivs => ivs with { Speed = value });
                return true;
            case ZaEncountersWorkflowService.VanillaTalentScaleField:
                row.TalentScale = value;
                return true;
            case ZaEncountersWorkflowService.VanillaTalentVCountField:
                row.TalentVNum = value;
                return true;
            default:
                return false;
        }
    }

    private static bool VerifyPlayerPartnerOutput(
        byte[] sourceBytes,
        byte[] outputBytes,
        ZaEncountersWorkflow effectiveWorkflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var sourceDocument = ZaPokemonDataDocument.Parse(sourceBytes);
            var outputDocument = ZaPokemonDataDocument.Parse(outputBytes);
            var hasSourceRow = ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
                sourceDocument,
                out var sourceRow,
                out var sourceBlockedReason);
            var hasOutputRow = ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
                outputDocument,
                out var outputRow,
                out var outputBlockedReason);
            if (!hasSourceRow || !hasOutputRow)
            {
                diagnostics.Add(CreatePlayerPartnerDiagnostic(
                    "Temporary partner output lost its exact PokemonData identity. "
                        + (!string.IsNullOrWhiteSpace(outputBlockedReason)
                            ? outputBlockedReason
                            : sourceBlockedReason),
                    field: null,
                    "Unique vsmega_init_rukario row at source index 772"));
                return false;
            }

            var expectedMatches = effectiveWorkflow.Tables
                .Where(ZaEncounterPlayerPartnerCatalog.IsTargetTable)
                .Select(table => table.PlayerPartner)
                .Where(partner => partner is not null)
                .Cast<ZaEncounterPlayerPartnerRecord>()
                .ToArray();
            if (expectedMatches.Length != 1
                || !PlayerPartnerRowMatches(outputRow, sourceRow, expectedMatches[0]))
            {
                diagnostics.Add(CreatePlayerPartnerDiagnostic(
                    "Temporary partner output does not match the reviewed AZ's Lucario values or changed unexposed source fields.",
                    field: null,
                    "Reviewed partner fields with all unexposed source values preserved"));
                return false;
            }

            if (sourceDocument.Groups.Count != outputDocument.Groups.Count)
            {
                diagnostics.Add(CreatePlayerPartnerDiagnostic(
                    "Temporary partner output changed the PokemonData group count.",
                    field: null,
                    "Unchanged PokemonData structure outside source index 772"));
                return false;
            }

            for (var groupIndex = 0; groupIndex < sourceDocument.Groups.Count; groupIndex++)
            {
                var sourceRows = sourceDocument.Groups[groupIndex].Rows;
                var outputRows = outputDocument.Groups[groupIndex].Rows;
                if (sourceRows.Count != outputRows.Count)
                {
                    diagnostics.Add(CreatePlayerPartnerDiagnostic(
                        "Temporary partner output changed a PokemonData group shape.",
                        field: null,
                        "Unchanged PokemonData structure outside source index 772"));
                    return false;
                }

                for (var rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
                {
                    var before = sourceRows[rowIndex];
                    var after = outputRows[rowIndex];
                    if (before?.SourceIndex == ZaEncounterPlayerPartnerCatalog.PokemonDataSourceIndex)
                    {
                        continue;
                    }

                    if (!PokemonDataRowsEqual(before, after))
                    {
                        diagnostics.Add(CreatePlayerPartnerDiagnostic(
                            "Temporary partner output changed an unrelated PokemonData row.",
                            field: null,
                            "Only vsmega_init_rukario at source index 772 may change"));
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
            or ArgumentException
            or OverflowException)
        {
            diagnostics.Add(CreatePlayerPartnerDiagnostic(
                $"Temporary partner output could not be verified: {exception.Message}",
                field: null,
                "Readable PokemonData output with one exact reviewed row change"));
            return false;
        }
    }

    private static bool PlayerPartnerRowMatches(
        ZaPokemonDataEntry output,
        ZaPokemonDataEntry source,
        ZaEncounterPlayerPartnerRecord expected)
    {
        var outputMoves = output.WazaList?.Values.Take(4).ToArray() ?? [0, 0, 0, 0];
        return output.SourceIndex == ZaEncounterPlayerPartnerCatalog.PokemonDataSourceIndex
            && string.Equals(output.Id, ZaEncounterPlayerPartnerCatalog.PokemonDataId, StringComparison.Ordinal)
            && output.DevNo == expected.SpeciesId
            && output.FormNo == expected.Form
            && output.MinLevel == expected.Level
            && output.MaxLevel == expected.LevelMax
            && output.Sex == expected.Gender
            && output.Rare == expected.ShinyMode
            && output.Tokusei == expected.Ability
            && output.Seikaku == expected.Nature
            && output.TalentScale == expected.TalentScale
            && output.TalentVNum == expected.TalentVCount
            && output.TalentValue == new ZaPokemonDataStatsRecord(
                expected.IvHp,
                expected.IvAttack,
                expected.IvDefense,
                expected.IvSpecialAttack,
                expected.IvSpecialDefense,
                expected.IvSpeed)
            && outputMoves.SequenceEqual(expected.MoveIds.Take(4))
            && (output.WazaList is not null) == expected.HasExplicitMoves
            && (output.HoldItem ?? 0) == expected.HeldItemId
            && output.OyabunProbability.Equals(source.OyabunProbability)
            && output.OyabunAdditionalLevel == source.OyabunAdditionalLevel
            && ActivationConditionsEqual(output.ActivationConditions, source.ActivationConditions);
    }

    private static bool PokemonDataRowsEqual(
        ZaPokemonDataEntry? left,
        ZaPokemonDataEntry? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SourceIndex == right.SourceIndex
            && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.DevNo == right.DevNo
            && left.MinLevel == right.MinLevel
            && left.MaxLevel == right.MaxLevel
            && left.Sex == right.Sex
            && left.FormNo == right.FormNo
            && left.Rare == right.Rare
            && left.Tokusei == right.Tokusei
            && left.Seikaku == right.Seikaku
            && left.TalentScale == right.TalentScale
            && left.TalentVNum == right.TalentVNum
            && left.OyabunProbability.Equals(right.OyabunProbability)
            && left.OyabunAdditionalLevel == right.OyabunAdditionalLevel
            && left.TalentValue == right.TalentValue
            && left.WazaList == right.WazaList
            && left.HoldItem == right.HoldItem
            && ActivationConditionsEqual(left.ActivationConditions, right.ActivationConditions);
    }

    private static bool ActivationConditionsEqual(
        IReadOnlyList<ZaPokemonDataActivationConditionRecord> left,
        IReadOnlyList<ZaPokemonDataActivationConditionRecord> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var conditionIndex = 0; conditionIndex < left.Count; conditionIndex++)
        {
            var leftElements = left[conditionIndex].Elements;
            var rightElements = right[conditionIndex].Elements;
            if (leftElements.Count != rightElements.Count)
            {
                return false;
            }

            for (var elementIndex = 0; elementIndex < leftElements.Count; elementIndex++)
            {
                var leftParams = leftElements[elementIndex].Params;
                var rightParams = rightElements[elementIndex].Params;
                if (leftParams.Count != rightParams.Count)
                {
                    return false;
                }

                for (var paramIndex = 0; paramIndex < leftParams.Count; paramIndex++)
                {
                    var leftParam = leftParams[paramIndex];
                    var rightParam = rightParams[paramIndex];
                    if (!string.Equals(leftParam.Condition, rightParam.Condition, StringComparison.Ordinal)
                        || leftParam.Op != rightParam.Op
                        || !leftParam.Params.SequenceEqual(rightParam.Params, StringComparer.Ordinal))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool TryResolveSpawnerSlot(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out ZaEncounterTableRecord table,
        out ZaEncounterSlotRecord slot)
    {
        table = null!;
        slot = null!;
        if (!ZaEncountersWorkflowService.TryParseSlotRecordId(recordId, out var tableId, out var slotIndex))
        {
            return false;
        }

        var resolvedTable = workflow.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
        var resolvedSlot = resolvedTable?.Slots.FirstOrDefault(candidate => candidate.Slot == slotIndex);
        if (resolvedTable is null || resolvedSlot is null)
        {
            return false;
        }

        table = resolvedTable;
        slot = resolvedSlot;
        return true;
    }

    private static bool TryResolveAppearanceTable(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out ZaEncounterTableRecord table)
    {
        table = null!;
        if (!ZaEncountersWorkflowService.TryParseAppearanceRecordId(recordId, out var tableId))
        {
            return false;
        }

        var resolvedTable = workflow.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
        if (resolvedTable is null)
        {
            return false;
        }

        table = resolvedTable;
        return true;
    }

    private static bool TryResolveSpawnerTableId(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out string tableId)
    {
        if (TryResolveSpawnerSlot(workflow, recordId, out var slotTable, out _))
        {
            tableId = slotTable.TableId;
            return true;
        }

        if (TryResolveAppearanceTable(workflow, recordId, out var appearanceTable))
        {
            tableId = appearanceTable.TableId;
            return true;
        }

        tableId = string.Empty;
        return false;
    }

    private static bool TryResolvePokemonDataSourceIndex(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out int sourceIndex)
    {
        if (ZaEncountersWorkflowService.TryParsePokemonDataRecordId(recordId, out sourceIndex))
        {
            var resolvedSourceIndex = sourceIndex;
            return workflow.Tables
                .SelectMany(table => table.Slots)
                .Any(slot => slot.PokemonDataSourceIndex == resolvedSourceIndex);
        }

        if (ZaEncountersWorkflowService.TryParseSlotRecordId(recordId, out var tableId, out var slot))
        {
            sourceIndex = workflow.Tables
                .FirstOrDefault(candidate => string.Equals(candidate.TableId, tableId, StringComparison.Ordinal))
                ?.Slots
                .FirstOrDefault(candidate => candidate.Slot == slot)
                ?.PokemonDataSourceIndex ?? -1;
            return sourceIndex >= 0;
        }

        sourceIndex = -1;
        return false;
    }

    private static void ApplyField(
        ZaEncounterDataEntry row,
        string? field,
        int value)
    {
        switch (field)
        {
            case ZaEncountersWorkflowService.SpeciesIdField:
                row.DevNo = value;
                break;
            case ZaEncountersWorkflowService.FormField:
                row.FormNo = value;
                break;
            case ZaEncountersWorkflowService.LevelMinField:
                row.MinLevel = value;
                break;
            case ZaEncountersWorkflowService.LevelMaxField:
                row.MaxLevel = value;
                break;
            case ZaEncountersWorkflowService.AlphaChancePercentField:
                row.OyabunProbability = value;
                break;
            case ZaEncountersWorkflowService.AlphaLevelBonusField:
                row.OyabunAdditionalLevel = value;
                break;
            case ZaEncountersWorkflowService.HeldItemIdField:
                row.HoldItem = value == 0 ? null : value;
                break;
            case ZaEncountersWorkflowService.AbilityField:
                row.Tokusei = value;
                break;
            case ZaEncountersWorkflowService.NatureField:
                row.Seikaku = value;
                break;
            case ZaEncountersWorkflowService.GenderField:
                row.Sex = value;
                break;
            case ZaEncountersWorkflowService.ShinyModeField:
                row.Rare = value;
                break;
            case ZaEncountersWorkflowService.Move1IdField:
                SetMove(row, 0, value);
                break;
            case ZaEncountersWorkflowService.Move2IdField:
                SetMove(row, 1, value);
                break;
            case ZaEncountersWorkflowService.Move3IdField:
                SetMove(row, 2, value);
                break;
            case ZaEncountersWorkflowService.Move4IdField:
                SetMove(row, 3, value);
                break;
            case ZaEncountersWorkflowService.FlawlessIvCountField:
                ZaPokemonDataIvEncoding.SetPreset(row, value);
                break;
            case ZaEncountersWorkflowService.IvHpField:
                SetIv(row, ivs => ivs with { HP = value });
                break;
            case ZaEncountersWorkflowService.IvAttackField:
                SetIv(row, ivs => ivs with { Attack = value });
                break;
            case ZaEncountersWorkflowService.IvDefenseField:
                SetIv(row, ivs => ivs with { Defense = value });
                break;
            case ZaEncountersWorkflowService.IvSpecialAttackField:
                SetIv(row, ivs => ivs with { SpecialAttack = value });
                break;
            case ZaEncountersWorkflowService.IvSpecialDefenseField:
                SetIv(row, ivs => ivs with { SpecialDefense = value });
                break;
            case ZaEncountersWorkflowService.IvSpeedField:
                SetIv(row, ivs => ivs with { Speed = value });
                break;
            case ZaEncountersWorkflowService.StrengthenHpField:
                SetStrengthen(row, stats => stats with { HP = value });
                break;
            case ZaEncountersWorkflowService.StrengthenAttackField:
                SetStrengthen(row, stats => stats with { Attack = value });
                break;
            case ZaEncountersWorkflowService.StrengthenDefenseField:
                SetStrengthen(row, stats => stats with { Defense = value });
                break;
            case ZaEncountersWorkflowService.StrengthenSpecialAttackField:
                SetStrengthen(row, stats => stats with { SpecialAttack = value });
                break;
            case ZaEncountersWorkflowService.StrengthenSpecialDefenseField:
                SetStrengthen(row, stats => stats with { SpecialDefense = value });
                break;
            case ZaEncountersWorkflowService.StrengthenSpeedField:
                SetStrengthen(row, stats => stats with { Speed = value });
                break;
            case ZaEncountersWorkflowService.VanillaTalentScaleField:
                row.TalentScale = value;
                break;
            case ZaEncountersWorkflowService.VanillaTalentVCountField:
                row.TalentVNum = value;
                break;
        }
    }

    private static void SetMove(ZaPokemonDataEntry row, int moveIndex, int moveId)
    {
        var moves = (row.WazaList ?? new ZaPokemonDataMovesRecord(0, 0, 0, 0))
            .SetMove(moveIndex, moveId);
        row.WazaList = moves.Values.All(move => move == ZaPokemonDataConstants.MoveAuto)
            ? null
            : moves;
    }

    private static void SetIv(
        ZaPokemonDataEntry row,
        Func<ZaPokemonDataStatsRecord, ZaPokemonDataStatsRecord> update)
    {
        ZaPokemonDataIvEncoding.SetFixedIvs(row, update);
    }

    private static void SetStrengthen(
        ZaEncounterDataEntry row,
        Func<ZaPokemonDataStatsRecord, ZaPokemonDataStatsRecord> update)
    {
        row.StrengthenValue = update(row.StrengthenValue
            ?? throw new InvalidDataException(
                "Encounter StrengthenValue storage disappeared before the reviewed edit was applied."));
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Encounter field '{field}' is not supported by Pokemon Legends Z-A Wild Encounters yet.",
            ZaEditSessionSupport.EncountersDomain,
            field: "field",
            expected: "A supported shared Pokemon, spawner slot, or spawner population field");
    }

    private static ValidationDiagnostic CreateVanillaRestoreDiagnostic(
        string message,
        string expected)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.EncountersDomain,
            field: "slot",
            expected: expected);
    }

    private static string CreateSummary(
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        ZaEncounterEditableField field,
        int value)
    {
        return field.Field switch
        {
            ZaEncountersWorkflowService.SpeciesIdField =>
                $"Set {table.Location} slot {slot.Slot} species ID to {value}.",
            ZaEncountersWorkflowService.FormField =>
                $"Set {table.Location} slot {slot.Slot} form to {value}.",
            ZaEncountersWorkflowService.LevelMinField =>
                $"Set {table.Location} slot {slot.Slot} minimum level to {value}.",
            ZaEncountersWorkflowService.LevelMaxField =>
                $"Set {table.Location} slot {slot.Slot} maximum level to {value}.",
            ZaEncountersWorkflowService.AlphaChancePercentField =>
                $"Set the shared Alpha chance to {value} percent for every placement linked to {slot.EncounterRecordId}.",
            ZaEncountersWorkflowService.AlphaLevelBonusField =>
                $"Set the shared Alpha level bonus to +{value} for every placement linked to {slot.EncounterRecordId}.",
            ZaEncountersWorkflowService.StrengthenHpField
                or ZaEncountersWorkflowService.StrengthenAttackField
                or ZaEncountersWorkflowService.StrengthenDefenseField
                or ZaEncountersWorkflowService.StrengthenSpecialAttackField
                or ZaEncountersWorkflowService.StrengthenSpecialDefenseField
                or ZaEncountersWorkflowService.StrengthenSpeedField =>
                value == ZaEncountersWorkflowService.MinimumStrengthenValue
                    ? $"Disable the shared {field.Label.ToLowerInvariant()} override for every placement linked to {slot.EncounterRecordId}."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Set the shared {field.Label.ToLowerInvariant()} to {value / 10m:0.0}x (stored {value}) for every placement linked to {slot.EncounterRecordId}."),
            ZaEncountersWorkflowService.WeightField =>
                $"Set {table.Location} slot {slot.Slot + 1} weight to {value}.",
            ZaEncountersWorkflowService.SlotMaxCountField =>
                $"Set {table.Location} slot {slot.Slot + 1} maximum count to {value}.",
            ZaEncountersWorkflowService.AppearanceMinCountField =>
                $"Set {table.Location} overall minimum count to {value} for every appearance object.",
            ZaEncountersWorkflowService.AppearanceMaxCountField =>
                $"Set {table.Location} overall maximum count to {value} for every appearance object.",
            _ => $"Set {table.Location} slot {slot.Slot + 1} {field.Label.ToLowerInvariant()} to {value}.",
        };
    }

    private sealed record PreparedSpecialSpawnNormalization(
        ZaEncountersWorkflow CurrentWorkflow,
        ZaEncountersWorkflow EffectiveWorkflow,
        ZaWorkflowFile EncounterSource,
        ZaWorkflowFile SpawnerSource,
        ZaPokemonSpawnerDataDocument SpawnerDocument,
        ZaPokemonSpawnerDataDocument BaseSpawnerDocument,
        ZaEncounterDataDocument BaseEncounterDocument,
        ZaSpecialSpawnNormalizationResult Result,
        IReadOnlyList<PlanFingerprintSource> SemanticSources);
}
