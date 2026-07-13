// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.StaticEncounters;

internal sealed class ZaStaticEncountersEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaStaticEncountersWorkflowService staticEncountersWorkflowService;

    public ZaStaticEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaStaticEncountersWorkflowService? staticEncountersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.staticEncountersWorkflowService =
            staticEncountersWorkflowService ?? new ZaStaticEncountersWorkflowService(this.fileSource);
    }

    public ZaStaticEncountersEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int encounterIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = staticEncountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits, diagnostics);

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.StaticEncountersDomain,
                diagnostics))
        {
            return new ZaStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EncounterIndex == encounterIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter {encounterIndex} is not present in the loaded Static Encounters workflow.",
                field: "encounterIndex",
                expected: "Existing Pokemon Legends Z-A static encounter record"));
            return new ZaStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, encounter, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaStaticEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new ZaStaticEncountersEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits, diagnostics),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = staticEncountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.StaticEncountersDomain,
            diagnostics);

        var effectiveWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Info, "Pending Static Encounters change is valid."));
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
            ZaEditSessionSupport.StaticEncountersDomain,
            ZaDataPaths.EncountDataArray,
            "Static Encounters",
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
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Static Encounters change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var workflow = staticEncountersWorkflowService.Load(project);
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

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Static Encounters", outputMode)));
        }
        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounters output could not be written: {exception.Message}",
                file: $"romfs/{ZaDataPaths.EncountDataArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaStaticEncountersWorkflow workflow,
        ZaStaticEncounterEntry encounter,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        if (!encounter.SupportedFields.Contains(normalizedField, StringComparer.Ordinal))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var editableField = ZaStaticEncountersWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null || editableField.IsReadOnly)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (encounter.FieldReadOnly.TryGetValue(normalizedField, out var isReadOnly) && isReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter field '{editableField.Label}' is read-only for this encounter.",
                field: normalizedField,
                expected: "Editable Pokemon Legends Z-A static encounter field"));
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.StaticEncountersDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (!ValidateSpeciesOption(normalizedField, parsedValue.Value, editableField, diagnostics))
        {
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.StaticEncountersDomain,
            $"Set {encounter.Label} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile),
            ZaStaticEncountersWorkflowService.CreateRecordId(encounter.EncounterIndex),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaStaticEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.StaticEncountersDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Static Encounters.",
                expected: ZaEditSessionSupport.StaticEncountersDomain));
            return;
        }

        var editableField = ZaStaticEncountersWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null || editableField.IsReadOnly)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!ZaStaticEncountersWorkflowService.TryParseRecordId(edit.RecordId, out var encounterIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounters edit targets an invalid record id.",
                field: "encounterIndex",
                expected: "Existing Pokemon Legends Z-A static encounter record"));
            return;
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EncounterIndex == encounterIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounters edit targets a record that is not loaded.",
                field: "encounterIndex",
                expected: "Existing Pokemon Legends Z-A static encounter record"));
            return;
        }

        if (!encounter.SupportedFields.Contains(edit.Field ?? string.Empty, StringComparer.Ordinal))
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (encounter.FieldReadOnly.TryGetValue(edit.Field ?? string.Empty, out var isReadOnly) && isReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Static Encounters edit targets field '{editableField.Label}', which is read-only for this encounter.",
                field: edit.Field,
                expected: "Editable Pokemon Legends Z-A static encounter field"));
            return;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.StaticEncountersDomain,
            diagnostics);
        if (parsedValue is not null)
        {
            ValidateSpeciesOption(edit.Field, parsedValue.Value, editableField, diagnostics);
        }
    }

    private static bool ValidateSpeciesOption(
        string? field,
        int value,
        ZaStaticEncounterEditableField editableField,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(field, ZaStaticEncountersWorkflowService.SpeciesField, StringComparison.Ordinal))
        {
            return true;
        }

        return ZaEditSessionSupport.ValidateOptionValue(
            value,
            editableField.Options.Select(option => option.Value),
            ZaEditSessionSupport.StaticEncountersDomain,
            field,
            $"Pokemon species {value.ToString(CultureInfo.InvariantCulture)} is not available in Pokemon Legends Z-A.",
            "Pokemon marked present in Pokemon Legends Z-A Pokemon Data",
            diagnostics);
    }

    private ZaStaticEncountersWorkflow OverlayPendingEdits(
        OpenedProject project,
        ZaStaticEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        var pendingEdits = edits
            .Where(edit =>
                string.Equals(edit.Domain, ZaEditSessionSupport.StaticEncountersDomain, StringComparison.Ordinal)
                && ZaStaticEncountersWorkflowService.TryParseRecordId(edit.RecordId, out _)
                && int.TryParse(
                    edit.NewValue,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out _))
            .ToArray();

        if (pendingEdits.Length == 0)
        {
            return workflow;
        }

        try
        {
            var overlayDiagnostics = new List<ValidationDiagnostic>();
            var source = fileSource.Read(project, ZaDataPaths.EncountDataArray);
            var labels = ZaTextLabelLookup.Load(project, fileSource, overlayDiagnostics, project.Paths);
            var wildIds = staticEncountersWorkflowService.LoadWildEncounterIds(
                project,
                overlayDiagnostics,
                out _);
            var document = ZaEncounterDataDocument.Parse(source.Bytes);
            foreach (var edit in pendingEdits)
            {
                ApplyEdit(workflow, document, edit, overlayDiagnostics);
            }

            if (overlayDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                if (diagnostics is not null)
                {
                    foreach (var diagnostic in overlayDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                return workflow;
            }

            var overlaySource = source with { Bytes = document.Write() };
            var encountersByIndex = ZaStaticEncountersWorkflowService
                .LoadRecords(overlaySource, labels, wildIds)
                .ToDictionary(encounter => encounter.EncounterIndex);

            return workflow with
            {
                Encounters = workflow.Encounters
                    .Select(encounter => encountersByIndex.TryGetValue(encounter.EncounterIndex, out var updatedEncounter)
                        ? updatedEncounter
                        : encounter)
                    .ToArray(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounters pending changes could not be previewed: {exception.Message}",
                file: $"romfs/{ZaDataPaths.EncountDataArray}",
                expected: "Readable Pokemon Legends Z-A static encounter source"));
            return workflow;
        }
    }

    private static ZaStaticEncountersWorkflow OverlayPendingEdit(
        ZaStaticEncountersWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.StaticEncountersDomain, StringComparison.Ordinal)
            || !ZaStaticEncountersWorkflowService.TryParseRecordId(edit.RecordId, out var encounterIndex)
            || string.IsNullOrWhiteSpace(edit.Field)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        var editableField = ZaStaticEncountersWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            return workflow;
        }

        return workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => encounter.EncounterIndex == encounterIndex
                    ? OverlayEntry(encounter, edit.Field, value, FormatDisplayValue(value, editableField))
                    : encounter)
                .ToArray(),
        };
    }

    private static ZaStaticEncounterEntry OverlayEntry(
        ZaStaticEncounterEntry encounter,
        string field,
        int value,
        string displayValue)
    {
        var valueText = value.ToString(CultureInfo.InvariantCulture);
        var fieldValues = new Dictionary<string, string>(encounter.FieldValues, StringComparer.Ordinal)
        {
            [field] = valueText,
        };
        var fieldDisplayValues = new Dictionary<string, string>(encounter.FieldDisplayValues, StringComparer.Ordinal)
        {
            [field] = displayValue,
        };

        return field switch
        {
            ZaStaticEncountersWorkflowService.SpeciesField => encounter with
            {
                SpeciesId = value,
                Species = StripLeadingValue(displayValue),
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.FormField => encounter with
            {
                Form = value,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.LevelField => encounter with
            {
                Level = value,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.HeldItemIdField => encounter with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : StripLeadingValue(displayValue),
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.AbilityField => encounter with
            {
                Ability = value,
                AbilityLabel = displayValue,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.NatureField => encounter with
            {
                Nature = value,
                NatureLabel = displayValue,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.GenderField => encounter with
            {
                Gender = value,
                GenderLabel = displayValue,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.ShinyLockField => encounter with
            {
                ShinyLock = value,
                ShinyLockLabel = displayValue,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            ZaStaticEncountersWorkflowService.FlawlessIvCountField => OverlayIvPreset(
                encounter,
                value,
                fieldValues,
                fieldDisplayValues),
            _ when TryUpdateIvs(encounter.Ivs, field, value, out var ivs) => encounter with
            {
                Ivs = ivs,
                FlawlessIvCount = null,
                IvSummary = FormatFixedIvSummary(ivs),
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            _ when TryUpdateMove(encounter.Moves, field, value, displayValue, out var moves) => encounter with
            {
                Moves = moves,
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
            _ => encounter with
            {
                FieldValues = fieldValues,
                FieldDisplayValues = fieldDisplayValues,
            },
        };
    }

    private static ZaStaticEncounterEntry OverlayIvPreset(
        ZaStaticEncounterEntry encounter,
        int value,
        IReadOnlyDictionary<string, string> fieldValues,
        IReadOnlyDictionary<string, string> fieldDisplayValues)
    {
        return encounter with
        {
            FlawlessIvCount = value,
            IvSummary = value == 0
                ? "Random IVs"
                : value == 1
                    ? "1 guaranteed perfect IV"
                    : $"{value.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs",
            FieldValues = fieldValues,
            FieldDisplayValues = fieldDisplayValues,
        };
    }

    private static bool TryUpdateIvs(
        ZaStaticEncounterStatsRecord current,
        string field,
        int value,
        out ZaStaticEncounterStatsRecord updated)
    {
        updated = field switch
        {
            ZaStaticEncountersWorkflowService.IvHpField => current with { HP = value },
            ZaStaticEncountersWorkflowService.IvAttackField => current with { Attack = value },
            ZaStaticEncountersWorkflowService.IvDefenseField => current with { Defense = value },
            ZaStaticEncountersWorkflowService.IvSpecialAttackField => current with { SpecialAttack = value },
            ZaStaticEncountersWorkflowService.IvSpecialDefenseField => current with { SpecialDefense = value },
            ZaStaticEncountersWorkflowService.IvSpeedField => current with { Speed = value },
            _ => current,
        };

        return !ReferenceEquals(updated, current);
    }

    private static bool TryUpdateMove(
        IReadOnlyList<ZaStaticEncounterMoveRecord> current,
        string field,
        int value,
        string displayValue,
        out IReadOnlyList<ZaStaticEncounterMoveRecord> updated)
    {
        var slot = field switch
        {
            ZaStaticEncountersWorkflowService.Move0Field => 0,
            ZaStaticEncountersWorkflowService.Move1Field => 1,
            ZaStaticEncountersWorkflowService.Move2Field => 2,
            ZaStaticEncountersWorkflowService.Move3Field => 3,
            _ => -1,
        };

        if (slot < 0)
        {
            updated = current;
            return false;
        }

        updated = current
            .Select(move => move.Slot == slot
                ? move with
                {
                    MoveId = value,
                    Move = value <= ZaPokemonDataConstants.MoveAuto ? null : StripLeadingValue(displayValue),
                }
                : move)
            .ToArray();
        return true;
    }

    private static void ApplyEdit(
        ZaStaticEncountersWorkflow workflow,
        ZaEncounterDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.StaticEncountersDomain, StringComparison.Ordinal)
            || !ZaStaticEncountersWorkflowService.TryParseRecordId(edit.RecordId, out var encounterIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounters edit is not valid for apply.",
                expected: "Valid Pokemon Legends Z-A static encounter edit"));
            return;
        }

        var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EncounterIndex == encounterIndex);
        var row = encounter is null
            ? null
            : document.Entries.FirstOrDefault(candidate => candidate.SourceIndex == encounter.SourceIndex);
        if (row is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounters edit target is not present in the source encounter data array.",
                field: "encounterIndex",
                expected: "Existing source static encounter row"));
            return;
        }

        ApplyField(row, edit.Field, value);
    }

    private static void ApplyField(
        ZaPokemonDataEntry row,
        string? field,
        int value)
    {
        switch (field)
        {
            case ZaStaticEncountersWorkflowService.SpeciesField:
                row.DevNo = value;
                break;
            case ZaStaticEncountersWorkflowService.FormField:
                row.FormNo = value;
                break;
            case ZaStaticEncountersWorkflowService.LevelField:
                row.MinLevel = value;
                row.MaxLevel = value;
                break;
            case ZaStaticEncountersWorkflowService.HeldItemIdField:
                row.HoldItem = value;
                break;
            case ZaStaticEncountersWorkflowService.AbilityField:
                row.Tokusei = value;
                break;
            case ZaStaticEncountersWorkflowService.NatureField:
                row.Seikaku = value;
                break;
            case ZaStaticEncountersWorkflowService.GenderField:
                row.Sex = value;
                break;
            case ZaStaticEncountersWorkflowService.ShinyLockField:
                row.Rare = value;
                break;
            case ZaStaticEncountersWorkflowService.Move0Field:
                SetMove(row, 0, value);
                break;
            case ZaStaticEncountersWorkflowService.Move1Field:
                SetMove(row, 1, value);
                break;
            case ZaStaticEncountersWorkflowService.Move2Field:
                SetMove(row, 2, value);
                break;
            case ZaStaticEncountersWorkflowService.Move3Field:
                SetMove(row, 3, value);
                break;
            case ZaStaticEncountersWorkflowService.FlawlessIvCountField:
                SetIvPreset(row, value);
                break;
            case ZaStaticEncountersWorkflowService.IvHpField:
                SetIv(row, value, ivs => ivs with { HP = value });
                break;
            case ZaStaticEncountersWorkflowService.IvAttackField:
                SetIv(row, value, ivs => ivs with { Attack = value });
                break;
            case ZaStaticEncountersWorkflowService.IvDefenseField:
                SetIv(row, value, ivs => ivs with { Defense = value });
                break;
            case ZaStaticEncountersWorkflowService.IvSpecialAttackField:
                SetIv(row, value, ivs => ivs with { SpecialAttack = value });
                break;
            case ZaStaticEncountersWorkflowService.IvSpecialDefenseField:
                SetIv(row, value, ivs => ivs with { SpecialDefense = value });
                break;
            case ZaStaticEncountersWorkflowService.IvSpeedField:
                SetIv(row, value, ivs => ivs with { Speed = value });
                break;
        }
    }

    private static void SetMove(ZaPokemonDataEntry row, int moveIndex, int moveId)
    {
        row.WazaList = (row.WazaList ?? new ZaPokemonDataMovesRecord(0, 0, 0, 0))
            .SetMove(moveIndex, moveId);
    }

    private static void SetIvPreset(ZaPokemonDataEntry row, int value)
    {
        ZaPokemonDataIvEncoding.SetPreset(row, value);
    }

    private static void SetIv(
        ZaPokemonDataEntry row,
        int value,
        Func<ZaPokemonDataStatsRecord, ZaPokemonDataStatsRecord> update)
    {
        _ = value;
        ZaPokemonDataIvEncoding.SetFixedIvs(row, update);
    }

    private static string FormatDisplayValue(int value, ZaStaticEncounterEditableField field)
    {
        return field.Options.FirstOrDefault(option => option.Value == value)?.Label
            ?? value.ToString(CultureInfo.InvariantCulture);
    }

    private static string StripLeadingValue(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return separator > 0
            && int.TryParse(trimmed[..separator], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
            ? trimmed[(separator + 1)..]
            : trimmed;
    }

    private static string FormatFixedIvSummary(ZaStaticEncounterStatsRecord ivs)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"HP {FormatIvValue(ivs.HP)} / Atk {FormatIvValue(ivs.Attack)} / Def {FormatIvValue(ivs.Defense)} / SpA {FormatIvValue(ivs.SpecialAttack)} / SpD {FormatIvValue(ivs.SpecialDefense)} / Spe {FormatIvValue(ivs.Speed)}");
    }

    private static string FormatIvValue(int value)
    {
        return value == -1 ? "Random" : value.ToString(CultureInfo.InvariantCulture);
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Static Encounter field '{field}' is not supported by Pokemon Legends Z-A Static Encounters yet.",
            field: "field",
            expected: "Supported Pokemon Legends Z-A static encounter field");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            severity,
            message,
            ZaEditSessionSupport.StaticEncountersDomain,
            file: file,
            field: field,
            expected: expected);
    }
}
