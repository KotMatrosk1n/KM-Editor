// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

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
        var slotRecord = table?.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (table is null || slotRecord is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter edit targets a table or slot that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A encounter table slot"));
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, table, slotRecord, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var candidateWorkflow = OverlayPendingEdit(workflow, pendingEdit);
        if (AffectsSharedLevelRange(pendingEdit.Field))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateFinalSharedLevelRanges(candidateWorkflow, [slotRecord.PokemonDataSourceIndex], diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount)
            {
                return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
            }
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
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
            var slot = table?.Slots.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            if (table is null || slot is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Encounter edit targets a table or slot that is not loaded.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: "slot",
                    expected: "Existing Pokemon Legends Z-A encounter table slot"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
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

        var finalRangeErrorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ValidateFinalSharedLevelRanges(effectiveWorkflow, sharedLevelRangeSources, diagnostics);
        if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > finalRangeErrorCount)
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        return new ZaEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
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

        var effectiveWorkflow = workflow;
        var sharedLevelRangeSources = new HashSet<int>();
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
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

        ValidateFinalSharedLevelRanges(effectiveWorkflow, sharedLevelRangeSources, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Wild Encounters change is valid.",
                ZaEditSessionSupport.EncountersDomain));
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

        var validation = Validate(paths, session);
        return ZaEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            ZaEditSessionSupport.EncountersDomain,
            ZaDataPaths.EncountDataArray,
            "Wild Encounters",
            validation.Diagnostics,
            outputMode);
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
            var workflow = encountersWorkflowService.Load(project);
            var source = fileSource.Read(project, ZaDataPaths.EncountDataArray);
            var document = ZaEncounterDataDocument.Parse(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(workflow, document, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(paths, ZaDataPaths.EncountDataArray, document.Write(), outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.EncountDataArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Wild Encounters", outputMode),
                ZaEditSessionSupport.EncountersDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Wild Encounters output could not be written: {exception.Message}",
                ZaEditSessionSupport.EncountersDomain,
                file: $"romfs/{ZaDataPaths.EncountDataArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaEncountersWorkflow workflow,
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = ZaEncountersWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (slot.PokemonDataSourceIndex < 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter slot is missing its linked encounter data row and cannot be edited.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Encounter slot linked to Encount Data"));
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (!ValidateSpeciesOption(normalizedField, parsedValue.Value, editableField, diagnostics))
        {
            return null;
        }

        if (!ValidateSharedAlphaChance(
                workflow,
                slot.PokemonDataSourceIndex,
                normalizedField,
                parsedValue.Value,
                diagnostics))
        {
            return null;
        }

        if (!ValidateSharedAlphaLevelBonus(
                workflow,
                slot.PokemonDataSourceIndex,
                normalizedField,
                diagnostics))
        {
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.EncountersDomain,
            CreateSummary(table, slot, editableField, parsedValue.Value),
            new ProjectFileReference(slot.PokemonProvenance.SourceLayer, slot.PokemonProvenance.SourceFile),
            ZaEncountersWorkflowService.CreatePokemonDataRecordId(slot.PokemonDataSourceIndex),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
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

        var editableField = ZaEncountersWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets an encounter data row that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A encounter data row"));
            return;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is not null)
        {
            ValidateSpeciesOption(edit.Field, parsedValue.Value, editableField, diagnostics);
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

    private static bool ValidateSpeciesOption(
        string? field,
        int value,
        ZaEncounterEditableField editableField,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(field, ZaEncountersWorkflowService.SpeciesIdField, StringComparison.Ordinal))
        {
            return true;
        }

        return ZaEditSessionSupport.ValidateOptionValue(
            value,
            editableField.Options.Select(option => option.Value),
            ZaEditSessionSupport.EncountersDomain,
            field,
            $"Pokemon species {value.ToString(CultureInfo.InvariantCulture)} is not available in Pokemon Legends Z-A.",
            "Pokemon marked present in Pokemon Legends Z-A Pokemon Data",
            diagnostics);
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
        if ((hasStructuralAlphaReference || hasGuaranteedAlphaChance) && value != 100)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Guaranteed Alpha encounter rows must keep their shared Alpha chance at 100 percent.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "100 for a structural _Alpha reference or an existing 100-percent encounter row"));
            return false;
        }

        if (!hasStructuralAlphaReference && !hasGuaranteedAlphaChance && value > 99)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Ordinary encounter rows must keep their shared Alpha chance between 0 and 99 percent.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "Whole-number percent from 0 through 99 for an ordinary encounter row"));
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
            || !TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

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

    private static ZaEncounterSlotRecord OverlaySlot(
        ZaEncountersWorkflow workflow,
        ZaEncounterSlotRecord slot,
        string? field,
        int value)
    {
        return field switch
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
            _ => slot,
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

        var row = document.Entries.FirstOrDefault(candidate => candidate.SourceIndex == sourceIndex);
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
        ZaPokemonDataEntry row,
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
        }
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Encounter field '{field}' is not supported by Pokemon Legends Z-A Wild Encounters yet.",
            ZaEditSessionSupport.EncountersDomain,
            field: "field",
            expected: "speciesId, form, levelMin, levelMax, alphaChancePercent, or alphaLevelBonus");
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
            _ => $"Set {table.Location} slot {slot.Slot} {field.Label.ToLowerInvariant()} to {value}.",
        };
    }
}
