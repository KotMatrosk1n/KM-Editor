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

namespace KM.SwSh.StaticEncounters;

public sealed class SwShStaticEncountersEditSessionService
{
    private const int MaximumPokemonEvTotal = 510;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShStaticEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShStaticEncountersWorkflowService? staticEncountersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.staticEncountersWorkflowService = staticEncountersWorkflowService ?? new SwShStaticEncountersWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShStaticEncountersEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShStaticEncountersWorkflowService? staticEncountersWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.staticEncountersWorkflowService = staticEncountersWorkflowService ?? new SwShStaticEncountersWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShStaticEncountersEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int encounterIndex,
        string field,
        string value)
    {
        return UpdateFields(
            paths,
            session,
            [new SwShStaticEncounterFieldUpdate(encounterIndex, field, value)]);
    }

    public SwShStaticEncountersEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShStaticEncounterFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = staticEncountersWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(workflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditStaticEncounters(project, workflow, diagnostics))
        {
            return new SwShStaticEncountersEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Update at least one Static Encounter field.",
                field: "updates",
                expected: "One or more Static Encounter field updates"));
            return new SwShStaticEncountersEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var workingSession = originalSession;
        var effectiveWorkflow = originalWorkflow;
        foreach (var update in updates)
        {
            if (update is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Static Encounter field update is missing.",
                    field: "updates",
                    expected: "Static Encounter field update"));
                break;
            }

            var expectedEncounterId = ParseExpectedEncounterId(update.ExpectedEncounterId, diagnostics);
            if (!string.IsNullOrWhiteSpace(update.ExpectedEncounterId) && expectedEncounterId is null)
            {
                break;
            }

            var encounter = ResolveEncounter(
                effectiveWorkflow,
                update.EncounterIndex,
                expectedEncounterId,
                diagnostics,
                update.Field);
            if (encounter is null)
            {
                break;
            }

            var pendingEdit = CreatePendingEdit(effectiveWorkflow, encounter, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                break;
            }

            pendingEdit = AddIdentityValidationSource(project, pendingEdit);
            workingSession = ReplacePendingStaticEncounterEdit(workingSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(workflow, workingSession.PendingEdits);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShStaticEncountersEditResult(originalWorkflow, originalSession, diagnostics);
        }

        ValidateLoadedSession(project, workflow, workingSession, diagnostics, addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShStaticEncountersEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShStaticEncountersEditResult(
            OverlayPendingEdits(workflow, workingSession.PendingEdits),
            workingSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = staticEncountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditStaticEncounters(project, workflow, diagnostics);
        ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);

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
                "Create a pending Static Encounter edit before reviewing a change plan.",
                expected: "Pending static encounter edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShStaticEncountersWorkflowService.ResolveStaticEncounterDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter change plan could not resolve the source table.",
                expected: SwShStaticEncountersWorkflowService.StaticEncounterDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShStaticEncountersWorkflowService.ResolveOutputPath(paths, source.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter apply target must stay inside the configured output root.",
                file: source.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            source.GraphEntry.RelativePath,
            session.PendingEdits
                .SelectMany(edit => edit.Sources)
                .Distinct()
                .ToArray(),
            File.Exists(targetPath),
            CreatePlanReason(session.PendingEdits));

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

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Static Encounter change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShStaticEncountersWorkflowService.ResolveStaticEncounterDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter apply could not resolve the source table.",
                expected: SwShStaticEncountersWorkflowService.StaticEncounterDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var archive = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToStaticEncounterEdit(archive, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = archive.WriteEdits(edits);
            WriteOutputAtomically(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, source.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Static Encounter change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter source file could not be decoded or safely edited: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield static encounter table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter change could not be encoded safely: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Supported Sword/Shield static encounter field values"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShStaticEncountersWorkflow workflow,
        SwShStaticEncounterEntry encounter,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var normalizedField = field.Trim();
        var editableField = SwShStaticEncountersWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = TryParseFieldValue(editableField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        AddAdvancedFieldWarnings(normalizedField, diagnostics);

        return new PendingEdit(
            SwShStaticEncountersWorkflowService.StaticEncountersEditDomain,
            $"Set {encounter.Label} {editableField.Label} to {parsedValue.Value}.",
            [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
            RecordId: SwShStaticEncountersWorkflowService.CreateEncounterRecordId(
                encounter.EncounterIndex,
                encounter.EncounterKey),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        OpenedProject project,
        SwShStaticEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShStaticEncountersWorkflowService.StaticEncountersEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Static Encounters workflow.",
                expected: SwShStaticEncountersWorkflowService.StaticEncountersEditDomain));
            return;
        }

        var editableField = SwShStaticEncountersWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(
                edit.RecordId,
                out var encounterIndex,
                out var encounterId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit targets an invalid record.",
                field: "encounterIndex",
                expected: "Static encounter record"));
            return;
        }

        var encounter = ResolveEncounter(workflow, encounterIndex, encounterId, diagnostics, edit.Field);
        if (encounter is null)
        {
            return;
        }

        var currentSources = new List<ProjectFileReference>
        {
            new(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile),
        };
        if (edit.Field is SwShStaticEncountersWorkflowService.SpeciesField
            or SwShStaticEncountersWorkflowService.FormField
            or SwShStaticEncountersWorkflowService.AbilityField
            or SwShStaticEncountersWorkflowService.CanGigantamaxField)
        {
            var personalSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
            if (personalSource is not null)
            {
                currentSources.Add(new ProjectFileReference(
                    GetSourceLayer(personalSource.GraphEntry),
                    personalSource.GraphEntry.RelativePath));
            }
        }

        if (edit.Sources.Count != currentSources.Count
            || currentSources.Any(source => !edit.Sources.Contains(source)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Static Encounter source layer changed after this edit was staged. Stage the edit again against the current source.",
                field: edit.Field,
                expected: "Pending edit staged from the current Static Encounter source"));
            return;
        }

        var parsedValue = TryParseFieldValue(editableField, edit.NewValue, diagnostics);
        if (parsedValue is not null)
        {
            ValidateOptionBackedValue(workflow, encounter, editableField, parsedValue.Value, diagnostics);
        }

        AddAdvancedFieldWarnings(edit.Field, diagnostics);
    }

    private static int? TryParseFieldValue(
        SwShStaticEncounterEditableField editableField,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be an integer value.",
                field: editableField.Field,
                expected: "Integer value"));
            return null;
        }

        if (!string.Equals(
                value,
                parsedValue.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must use canonical integer text without whitespace, a plus sign, or leading zeroes.",
                field: editableField.Field,
                expected: parsedValue.ToString(CultureInfo.InvariantCulture)));
            return null;
        }

        if (editableField.Field == SwShStaticEncountersWorkflowService.FlawlessIvCountField
            && parsedValue is not 0 and not 3 and not 6)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter IV preset must be 0, 3, or 6.",
                field: editableField.Field,
                expected: "Supported IV preset"));
            return null;
        }

        if (IsIndividualIvField(editableField.Field))
        {
            var isSupportedIv = parsedValue == SwShStaticEncounterArchive.RandomIvValue
                || parsedValue is >= SwShStaticEncounterArchive.MinimumFixedIvValue
                    and <= SwShStaticEncounterArchive.MaximumFixedIvValue
                || (editableField.Field == SwShStaticEncountersWorkflowService.IvHpField
                    && parsedValue == SwShStaticEncounterArchive.ThreePerfectIvSentinel);
            if (!isSupportedIv)
            {
                diagnostics.Add(CreateIvDiagnostic(editableField.Field));
                return null;
            }
        }

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Static Encounter field value"));
            return null;
        }

        return parsedValue;
    }

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShStaticEncountersWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var effectiveWorkflow = workflow;
        var evRecords = new HashSet<(int Index, ulong EncounterId)>();
        var ivRecords = new HashSet<(int Index, ulong EncounterId)>();
        var moveRecords = new HashSet<(int Index, ulong EncounterId)>();
        var identityRecords = new HashSet<(int Index, ulong EncounterId)>();
        var abilityRecords = new HashSet<(int Index, ulong EncounterId)>();
        var gigantamaxRecords = new HashSet<(int Index, ulong EncounterId)>();
        var seenFields = new HashSet<(int Index, ulong EncounterId, string Field)>();

        foreach (var edit in session.PendingEdits)
        {
            var errorsBefore = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(project, effectiveWorkflow, edit, diagnostics);

            if (SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(
                    edit.RecordId,
                    out var encounterIndex,
                    out var encounterId))
            {
                var encounter = ResolveEncounter(
                    effectiveWorkflow,
                    encounterIndex,
                    encounterId,
                    diagnostics,
                    edit.Field,
                    reportMissing: false);
                if (encounter is not null)
                {
                    var identity = (encounter.EncounterIndex, encounter.EncounterKey);
                    var field = edit.Field ?? string.Empty;
                    if (!seenFields.Add((identity.EncounterIndex, identity.EncounterKey, field)))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"Static encounter {encounter.EncounterIndex} has more than one pending edit for '{field}'.",
                            field: field,
                            expected: "One pending value per Static Encounter field"));
                    }

                    if (IsEvField(field))
                    {
                        evRecords.Add(identity);
                    }

                    if (IsIndividualIvField(field)
                        || field == SwShStaticEncountersWorkflowService.FlawlessIvCountField)
                    {
                        ivRecords.Add(identity);
                    }

                    if (IsMoveField(field))
                    {
                        moveRecords.Add(identity);
                    }

                    if (field is SwShStaticEncountersWorkflowService.SpeciesField
                        or SwShStaticEncountersWorkflowService.FormField)
                    {
                        identityRecords.Add(identity);
                        abilityRecords.Add(identity);
                        gigantamaxRecords.Add(identity);
                    }

                    if (field == SwShStaticEncountersWorkflowService.AbilityField)
                    {
                        abilityRecords.Add(identity);
                    }

                    if (field == SwShStaticEncountersWorkflowService.CanGigantamaxField)
                    {
                        gigantamaxRecords.Add(identity);
                    }
                }
            }

            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorsBefore)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        ValidateFinalEncounterInvariants(effectiveWorkflow, evRecords, ivRecords, moveRecords, diagnostics);
        ValidateSpeciesFormsAndAbilities(
            project,
            effectiveWorkflow,
            identityRecords,
            abilityRecords,
            diagnostics);
        ValidateGigantamaxCapability(effectiveWorkflow, gigantamaxRecords, diagnostics);

        if (session.PendingEdits.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightArchiveWrite(project, session, diagnostics);
        }

        if (session.PendingEdits.Count > 0
            && addSuccessDiagnostic
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Static Encounter change is valid."));
        }
    }

    private static void ValidateOptionBackedValue(
        SwShStaticEncountersWorkflow workflow,
        SwShStaticEncounterEntry encounter,
        SwShStaticEncounterEditableField editableField,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        IReadOnlyList<SwShStaticEncounterEditableFieldOption> options = editableField.Field switch
        {
            SwShStaticEncountersWorkflowService.AbilityField =>
                SwShStaticEncountersWorkflowService.CreateAbilityOptions(
                    workflow.AbilityResolver,
                    encounter.SpeciesId,
                    encounter.Form),
            _ => editableField.Options,
        };

        var requiresKnownOption = editableField.Field is
            SwShStaticEncountersWorkflowService.SpeciesField
            or SwShStaticEncountersWorkflowService.HeldItemIdField
            or SwShStaticEncountersWorkflowService.AbilityField
            or SwShStaticEncountersWorkflowService.Move0Field
            or SwShStaticEncountersWorkflowService.Move1Field
            or SwShStaticEncountersWorkflowService.Move2Field
            or SwShStaticEncountersWorkflowService.Move3Field;
        if (requiresKnownOption
            && options.Count > 0
            && !options.Any(option => option.Value == value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} value {value.ToString(CultureInfo.InvariantCulture)} is not available in the loaded Sword/Shield lookup data.",
                field: editableField.Field,
                expected: $"A listed {editableField.Label.ToLowerInvariant()} value"));
        }
    }

    private static void ValidateFinalEncounterInvariants(
        SwShStaticEncountersWorkflow workflow,
        IReadOnlySet<(int Index, ulong EncounterId)> evRecords,
        IReadOnlySet<(int Index, ulong EncounterId)> ivRecords,
        IReadOnlySet<(int Index, ulong EncounterId)> moveRecords,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var identity in evRecords.Concat(ivRecords).Concat(moveRecords).Distinct())
        {
            var encounter = ResolveEncounter(workflow, identity.Index, identity.EncounterId, diagnostics, field: null);
            if (encounter is null)
            {
                continue;
            }

            if (evRecords.Contains(identity))
            {
                var evTotal = encounter.Evs.HP
                    + encounter.Evs.Attack
                    + encounter.Evs.Defense
                    + encounter.Evs.SpecialAttack
                    + encounter.Evs.SpecialDefense
                    + encounter.Evs.Speed;
                if (evTotal > MaximumPokemonEvTotal)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} has {evTotal} total EVs; a Pokemon may use at most {MaximumPokemonEvTotal}.",
                        field: "evs",
                        expected: $"Combined EV total of {MaximumPokemonEvTotal} or less"));
                }
            }

            if (ivRecords.Contains(identity))
            {
                ValidateFinalIvValues(encounter, diagnostics);
            }

            if (moveRecords.Contains(identity)
                && encounter.Moves.Count > 1
                && encounter.Moves[0].MoveId == 0
                && encounter.Moves.Skip(1).Any(move => move.MoveId != 0))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} cannot use later move slots while Move 1 is empty.",
                    field: SwShStaticEncountersWorkflowService.Move0Field,
                    expected: "Move 1 populated before later move slots, or all move slots empty"));
            }
        }
    }

    private static void ValidateFinalIvValues(
        SwShStaticEncounterEntry encounter,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var ivs = new[]
        {
            (SwShStaticEncountersWorkflowService.IvHpField, encounter.Ivs.HP, true),
            (SwShStaticEncountersWorkflowService.IvAttackField, encounter.Ivs.Attack, false),
            (SwShStaticEncountersWorkflowService.IvDefenseField, encounter.Ivs.Defense, false),
            (SwShStaticEncountersWorkflowService.IvSpecialAttackField, encounter.Ivs.SpecialAttack, false),
            (SwShStaticEncountersWorkflowService.IvSpecialDefenseField, encounter.Ivs.SpecialDefense, false),
            (SwShStaticEncountersWorkflowService.IvSpeedField, encounter.Ivs.Speed, false),
        };
        foreach (var (field, value, isHp) in ivs)
        {
            var valid = value == SwShStaticEncounterArchive.RandomIvValue
                || value is >= SwShStaticEncounterArchive.MinimumFixedIvValue
                    and <= SwShStaticEncounterArchive.MaximumFixedIvValue
                || (isHp && value == SwShStaticEncounterArchive.ThreePerfectIvSentinel);
            if (!valid)
            {
                diagnostics.Add(CreateIvDiagnostic(field));
            }
        }

        if (encounter.Ivs.HP == SwShStaticEncounterArchive.ThreePerfectIvSentinel
            && new[]
            {
                encounter.Ivs.Attack,
                encounter.Ivs.Defense,
                encounter.Ivs.SpecialAttack,
                encounter.Ivs.SpecialDefense,
                encounter.Ivs.Speed,
            }.Any(value => value != SwShStaticEncounterArchive.RandomIvValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} mixes the 3-perfect IV sentinel with individual IV values.",
                field: SwShStaticEncountersWorkflowService.FlawlessIvCountField,
                expected: "HP -4 with all other IVs -1, or individual IV values without the -4 sentinel"));
        }
    }

    private static void ValidateSpeciesFormsAndAbilities(
        OpenedProject project,
        SwShStaticEncountersWorkflow workflow,
        IReadOnlySet<(int Index, ulong EncounterId)> identityRecords,
        IReadOnlySet<(int Index, ulong EncounterId)> abilityRecords,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (identityRecords.Count == 0 && abilityRecords.Count == 0)
        {
            return;
        }

        var personalRecords = LoadPersonalRecords(project, diagnostics);
        if (personalRecords.Count == 0)
        {
            return;
        }

        foreach (var identity in identityRecords.Concat(abilityRecords).Distinct())
        {
            var encounter = ResolveEncounter(workflow, identity.Index, identity.EncounterId, diagnostics, field: null);
            if (encounter is null
                || encounter.SpeciesId <= 0
                || (uint)encounter.SpeciesId >= (uint)personalRecords.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Static encounter {identity.Index} does not target a species available in the loaded Sword/Shield personal data.",
                    field: SwShStaticEncountersWorkflowService.SpeciesField,
                    expected: "Species present in Sword/Shield personal data"));
                continue;
            }

            var basePersonal = personalRecords[encounter.SpeciesId];
            var formCount = Math.Max(1, basePersonal.FormCount);
            if (encounter.Form < 0 || encounter.Form >= formCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses form {encounter.Form}, but species {encounter.SpeciesId} exposes {formCount} supported form slot(s) in personal data.",
                    field: SwShStaticEncountersWorkflowService.FormField,
                    expected: $"Form 0 through {formCount - 1}"));
                continue;
            }

            var personal = basePersonal;
            if (encounter.Form > 0 && basePersonal.FormStatsIndex > 0)
            {
                var formPersonalId = basePersonal.FormStatsIndex + encounter.Form - 1;
                if ((uint)formPersonalId >= (uint)personalRecords.Count)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} maps to a form record outside the loaded personal table.",
                        field: SwShStaticEncountersWorkflowService.FormField,
                        expected: "Mapped Sword/Shield personal form record"));
                    continue;
                }

                personal = personalRecords[formPersonalId];
            }

            if (identityRecords.Contains(identity) && !personal.IsPresentInGame)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses a species/form that is not marked present in Sword/Shield personal data.",
                    field: SwShStaticEncountersWorkflowService.SpeciesField,
                    expected: "Species/form present in Sword/Shield personal data"));
            }

            if (abilityRecords.Contains(identity)
                && !IsAvailableAbilitySlot(personal, encounter.Ability))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses an ability slot unavailable for its species and form.",
                    field: SwShStaticEncountersWorkflowService.AbilityField,
                    expected: "Ability slot listed for the selected species and form"));
            }
        }
    }

    private static bool IsAvailableAbilitySlot(SwShPersonalRecord personal, int ability)
    {
        return ability switch
        {
            0 or 1 => personal.Ability1 != 0,
            2 => personal.Ability2 != 0,
            3 => personal.HiddenAbility != 0,
            _ => false,
        };
    }

    private static void ValidateGigantamaxCapability(
        SwShStaticEncountersWorkflow workflow,
        IReadOnlySet<(int Index, ulong EncounterId)> records,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var identity in records)
        {
            var encounter = ResolveEncounter(workflow, identity.Index, identity.EncounterId, diagnostics, field: null);
            if (encounter is null || !encounter.CanGigantamax)
            {
                continue;
            }

            var personal = workflow.AbilityResolver.ResolvePersonalRecord(encounter.SpeciesId, encounter.Form);
            if (personal?.CanNotDynamax == true)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Species} is marked unable to Dynamax in Sword/Shield personal data.",
                    field: SwShStaticEncountersWorkflowService.CanGigantamaxField,
                    expected: "Species/form permitted to Dynamax or Can Gigantamax disabled"));
            }
            else if (!IsGigantamaxCapableSpeciesForm(encounter.SpeciesId, encounter.Form))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Species} form {encounter.Form} is not a Gigantamax-capable Sword/Shield species/form.",
                    field: SwShStaticEncountersWorkflowService.CanGigantamaxField,
                    expected: "Gigantamax-capable species/form or Can Gigantamax disabled"));
            }
        }
    }

    private static bool IsGigantamaxCapableSpeciesForm(int speciesId, int form)
    {
        if (speciesId is 25 or 52 && form != 0)
        {
            return false;
        }

        return SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(speciesId);
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter species and form validation requires the Sword/Shield personal data table.",
                field: SwShStaticEncountersWorkflowService.SpeciesField,
                expected: SwShPokemonWorkflowService.PersonalDataPath));
            return [];
        }

        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter species and form validation could not read personal data: {exception.Message}",
                field: SwShStaticEncountersWorkflowService.SpeciesField,
                expected: "Readable Sword/Shield personal data table",
                file: source.GraphEntry.RelativePath));
            return [];
        }
    }

    private static PendingEdit AddIdentityValidationSource(OpenedProject project, PendingEdit pendingEdit)
    {
        if (pendingEdit.Field is not SwShStaticEncountersWorkflowService.SpeciesField
            and not SwShStaticEncountersWorkflowService.FormField
            and not SwShStaticEncountersWorkflowService.AbilityField
            and not SwShStaticEncountersWorkflowService.CanGigantamaxField)
        {
            return pendingEdit;
        }

        var personalSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        return personalSource is null
            ? pendingEdit
            : pendingEdit with
            {
                Sources = pendingEdit.Sources
                    .Append(new ProjectFileReference(
                        GetSourceLayer(personalSource.GraphEntry),
                        personalSource.GraphEntry.RelativePath))
                    .Distinct()
                    .ToArray(),
            };
    }

    private static void PreflightArchiveWrite(
        OpenedProject project,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShStaticEncountersWorkflowService.ResolveStaticEncounterDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter edit preflight could not resolve the source table.",
                expected: SwShStaticEncountersWorkflowService.StaticEncounterDataPath));
            return;
        }

        try
        {
            var archive = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToStaticEncounterEdit(archive, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            _ = archive.WriteEdits(edits);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter edit cannot be encoded safely in the source FlatBuffer: {exception.Message}",
                expected: "Materialized compatible Static Encounter fields",
                file: source.GraphEntry.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Static Encounter edit preflight could not read the source table: {exception.Message}",
                expected: "Readable Sword/Shield static encounter table",
                file: source.GraphEntry.RelativePath));
        }
    }

    private static ulong? ParseExpectedEncounterId(
        string? expectedEncounterId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expectedEncounterId))
        {
            return null;
        }

        var text = expectedEncounterId.Trim();
        var isHex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (isHex)
        {
            text = text[2..];
        }

        if (ulong.TryParse(
                text,
                isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var encounterId))
        {
            return encounterId;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Expected encounter ID '{expectedEncounterId}' is not valid.",
            field: "expectedEncounterId",
            expected: "Unsigned decimal ID or 0x-prefixed hexadecimal ID"));
        return null;
    }

    private static SwShStaticEncounterEntry? ResolveEncounter(
        SwShStaticEncountersWorkflow workflow,
        int encounterIndex,
        ulong? expectedEncounterId,
        ICollection<ValidationDiagnostic> diagnostics,
        string? field,
        bool reportMissing = true)
    {
        if (expectedEncounterId is null)
        {
            var indexMatches = workflow.Encounters
                .Where(candidate => candidate.EncounterIndex == encounterIndex)
                .ToArray();
            if (indexMatches.Length == 1)
            {
                return indexMatches[0];
            }

            if (reportMissing)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    indexMatches.Length == 0
                        ? $"Static encounter index {encounterIndex} is not present in the loaded workflow."
                        : $"Static encounter index {encounterIndex} is duplicated in the loaded workflow.",
                    field: field ?? "encounterIndex",
                    expected: "One existing static encounter record"));
            }

            return null;
        }

        var indexed = workflow.Encounters
            .Where(candidate => candidate.EncounterIndex == encounterIndex)
            .ToArray();
        var identityMatches = workflow.Encounters
            .Where(candidate => candidate.EncounterKey == expectedEncounterId.Value)
            .ToArray();
        if (indexed.Length == 1
            && indexed[0].EncounterKey == expectedEncounterId.Value
            && identityMatches.Length == 1)
        {
            return indexed[0];
        }

        if (reportMissing)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                identityMatches.Length > 1
                    ? $"Static encounter ID 0x{expectedEncounterId.Value:X16} is duplicated and cannot be targeted safely."
                    : $"Static encounter index {encounterIndex} and ID 0x{expectedEncounterId.Value:X16} no longer identify the same row. Reload and stage the edit again.",
                field: field ?? "encounterIndex",
                expected: "The exact staged Static Encounter index and encounter ID pair"));
        }

        return null;
    }

    private static bool IsIndividualIvField(string field)
    {
        return field is
            SwShStaticEncountersWorkflowService.IvHpField
            or SwShStaticEncountersWorkflowService.IvAttackField
            or SwShStaticEncountersWorkflowService.IvDefenseField
            or SwShStaticEncountersWorkflowService.IvSpeedField
            or SwShStaticEncountersWorkflowService.IvSpecialAttackField
            or SwShStaticEncountersWorkflowService.IvSpecialDefenseField;
    }

    private static bool IsEvField(string field)
    {
        return field is
            SwShStaticEncountersWorkflowService.EvHpField
            or SwShStaticEncountersWorkflowService.EvAttackField
            or SwShStaticEncountersWorkflowService.EvDefenseField
            or SwShStaticEncountersWorkflowService.EvSpecialAttackField
            or SwShStaticEncountersWorkflowService.EvSpecialDefenseField
            or SwShStaticEncountersWorkflowService.EvSpeedField;
    }

    private static bool IsMoveField(string field)
    {
        return field is
            SwShStaticEncountersWorkflowService.Move0Field
            or SwShStaticEncountersWorkflowService.Move1Field
            or SwShStaticEncountersWorkflowService.Move2Field
            or SwShStaticEncountersWorkflowService.Move3Field;
    }

    private static void AddAdvancedFieldWarnings(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShStaticEncountersWorkflowService.SpeciesField or SwShStaticEncountersWorkflowService.FormField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Species and form edits update the static encounter table only; visible placement, model, or script references may need a separate review.",
                field: field,
                expected: "Review linked placement assets when changing visible static Pokemon"));
        }

        if (field == SwShStaticEncountersWorkflowService.EncounterScenarioField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Scenario edits should stay aligned with encounters designed to use that story or battle rule.",
                field: field,
                expected: "Use a scenario compatible with the encounter setup"));
        }
    }

    private static bool CanEditStaticEncounters(
        OpenedProject project,
        SwShStaticEncountersWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static EditSession ReplacePendingStaticEncounterEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsConflictingStaticEncounterEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsConflictingStaticEncounterEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        if (!string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            || !TargetsSameEncounter(candidate.RecordId, pendingEdit.RecordId))
        {
            return false;
        }

        return string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal)
            || (candidate.Field == SwShStaticEncountersWorkflowService.FlawlessIvCountField
                && IsIndividualIvField(pendingEdit.Field ?? string.Empty))
            || (pendingEdit.Field == SwShStaticEncountersWorkflowService.FlawlessIvCountField
                && IsIndividualIvField(candidate.Field ?? string.Empty));
    }

    private static bool TargetsSameEncounter(string? firstRecordId, string? secondRecordId)
    {
        if (string.Equals(firstRecordId, secondRecordId, StringComparison.Ordinal))
        {
            return true;
        }

        return SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(firstRecordId, out var firstIndex, out var firstId)
            && SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(secondRecordId, out var secondIndex, out var secondId)
            && (firstId is not null && secondId is not null
                ? firstId == secondId
                : firstIndex == secondIndex);
    }

    private static SwShStaticEncountersWorkflow OverlayPendingEdits(
        SwShStaticEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShStaticEncountersWorkflow OverlayPendingEdit(
        SwShStaticEncountersWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShStaticEncountersWorkflowService.StaticEncountersEditDomain, StringComparison.Ordinal)
            || !SwShStaticEncountersWorkflowService.IsEditableField(edit.Field)
            || !SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(
                edit.RecordId,
                out var encounterIndex,
                out var encounterId)
            || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }


        var target = ResolveEncounter(
            workflow,
            encounterIndex,
            encounterId,
            new List<ValidationDiagnostic>(),
            edit.Field,
            reportMissing: false);
        if (target is null)
        {
            return workflow;
        }

        var updated = workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => encounter.EncounterIndex == target.EncounterIndex
                    && encounter.EncounterKey == target.EncounterKey
                    ? OverlayEncounterField(workflow, encounter, edit.Field!, value)
                    : encounter)
                .ToArray(),
        };

        return SwShStaticEncountersWorkflowService.RecalculateStats(updated);
    }

    private static SwShStaticEncounterEntry OverlayEncounterField(
        SwShStaticEncountersWorkflow workflow,
        SwShStaticEncounterEntry encounter,
        string field,
        int value)
    {
        var updatedEncounter = field switch
        {
            SwShStaticEncountersWorkflowService.SpeciesField => encounter with
            {
                SpeciesId = value,
                Species = GetOptionDisplayName(workflow, field, value, "Species"),
            },
            SwShStaticEncountersWorkflowService.FormField => encounter with { Form = value },
            SwShStaticEncountersWorkflowService.LevelField => encounter with { Level = value },
            SwShStaticEncountersWorkflowService.HeldItemIdField => encounter with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionDisplayName(workflow, field, value, "Item"),
            },
            SwShStaticEncountersWorkflowService.AbilityField => encounter with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability slot"),
            },
            SwShStaticEncountersWorkflowService.NatureField => encounter with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            SwShStaticEncountersWorkflowService.GenderField => encounter with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, field, value, "Gender"),
            },
            SwShStaticEncountersWorkflowService.ShinyLockField => encounter with
            {
                ShinyLock = value,
                ShinyLockLabel = GetOptionLabel(workflow, field, value, "Shiny lock"),
            },
            SwShStaticEncountersWorkflowService.EncounterScenarioField => encounter with
            {
                EncounterScenario = value,
                EncounterScenarioLabel = GetOptionLabel(workflow, field, value, "Scenario"),
            },
            SwShStaticEncountersWorkflowService.DynamaxLevelField => encounter with { DynamaxLevel = value },
            SwShStaticEncountersWorkflowService.CanGigantamaxField => encounter with { CanGigantamax = value != 0 },
            SwShStaticEncountersWorkflowService.Move0Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 0, value) },
            SwShStaticEncountersWorkflowService.Move1Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 1, value) },
            SwShStaticEncountersWorkflowService.Move2Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 2, value) },
            SwShStaticEncountersWorkflowService.Move3Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 3, value) },
            SwShStaticEncountersWorkflowService.EvHpField => encounter with { Evs = encounter.Evs with { HP = value } },
            SwShStaticEncountersWorkflowService.EvAttackField => encounter with { Evs = encounter.Evs with { Attack = value } },
            SwShStaticEncountersWorkflowService.EvDefenseField => encounter with { Evs = encounter.Evs with { Defense = value } },
            SwShStaticEncountersWorkflowService.EvSpeedField => encounter with { Evs = encounter.Evs with { Speed = value } },
            SwShStaticEncountersWorkflowService.EvSpecialAttackField => encounter with { Evs = encounter.Evs with { SpecialAttack = value } },
            SwShStaticEncountersWorkflowService.EvSpecialDefenseField => encounter with { Evs = encounter.Evs with { SpecialDefense = value } },
            SwShStaticEncountersWorkflowService.IvHpField => encounter with { Ivs = encounter.Ivs with { HP = value } },
            SwShStaticEncountersWorkflowService.IvAttackField => encounter with { Ivs = encounter.Ivs with { Attack = value } },
            SwShStaticEncountersWorkflowService.IvDefenseField => encounter with { Ivs = encounter.Ivs with { Defense = value } },
            SwShStaticEncountersWorkflowService.IvSpeedField => encounter with { Ivs = encounter.Ivs with { Speed = value } },
            SwShStaticEncountersWorkflowService.IvSpecialAttackField => encounter with { Ivs = encounter.Ivs with { SpecialAttack = value } },
            SwShStaticEncountersWorkflowService.IvSpecialDefenseField => encounter with { Ivs = encounter.Ivs with { SpecialDefense = value } },
            SwShStaticEncountersWorkflowService.FlawlessIvCountField => encounter with { Ivs = CreateIvPreset(value) },
            _ => encounter,
        };

        var flawlessIvCount = GetFlawlessIvCount(updatedEncounter.Ivs);
        var abilityOptions = SwShStaticEncountersWorkflowService.CreateAbilityOptions(
            workflow.AbilityResolver,
            updatedEncounter.SpeciesId,
            updatedEncounter.Form);
        var genderOptions = SwShStaticEncountersWorkflowService.CreateGenderOptions(
            workflow.AbilityResolver,
            updatedEncounter.SpeciesId,
            updatedEncounter.Form);
        updatedEncounter = updatedEncounter with
        {
            AbilityOptions = abilityOptions,
            AbilityLabel = SwShStaticEncountersWorkflowService.GetOptionLabel(
                abilityOptions,
                updatedEncounter.Ability,
                "Ability slot"),
            GenderOptions = genderOptions,
            GenderLabel = SwShStaticEncountersWorkflowService.GetOptionLabel(
                genderOptions,
                updatedEncounter.Gender,
                "Gender"),
            FlawlessIvCount = flawlessIvCount,
            IvSummary = SwShStaticEncountersWorkflowService.FormatIvSummary(updatedEncounter.Ivs, flawlessIvCount),
            Label = SwShStaticEncountersWorkflowService.FormatEncounterLabel(
                updatedEncounter.EncounterIndex,
                updatedEncounter.Species,
                updatedEncounter.SpeciesId,
                updatedEncounter.Form,
                updatedEncounter.Level,
                updatedEncounter.EncounterScenarioLabel,
                updatedEncounter.Moves),
        };

        return updatedEncounter;
    }

    private static IReadOnlyList<SwShStaticEncounterMoveRecord> SetMove(
        SwShStaticEncountersWorkflow workflow,
        IReadOnlyList<SwShStaticEncounterMoveRecord> moves,
        int slot,
        int moveId)
    {
        return moves
            .Select(move => move.Slot == slot
                ? move with
                {
                    MoveId = moveId,
                    Move = moveId == 0 ? null : GetOptionDisplayName(workflow, GetMoveField(slot), moveId, "Move"),
                }
                : move)
            .ToArray();
    }

    private static string GetMoveField(int slot)
    {
        return slot switch
        {
            0 => SwShStaticEncountersWorkflowService.Move0Field,
            1 => SwShStaticEncountersWorkflowService.Move1Field,
            2 => SwShStaticEncountersWorkflowService.Move2Field,
            3 => SwShStaticEncountersWorkflowService.Move3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static SwShStaticEncounterStatsRecord CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShStaticEncounterStatsRecord(-1, -1, -1, -1, -1, -1),
            3 => new SwShStaticEncounterStatsRecord(-4, -1, -1, -1, -1, -1),
            6 => new SwShStaticEncounterStatsRecord(31, 31, 31, 31, 31, 31),
            _ => throw new ArgumentOutOfRangeException(nameof(flawlessIvCount)),
        };
    }

    private static int? GetFlawlessIvCount(SwShStaticEncounterStatsRecord ivs)
    {
        return SwShStaticEncounterArchive.GetFlawlessIvCount(
            new SwShStaticEncounterStats(
                ivs.HP,
                ivs.Attack,
                ivs.Defense,
                ivs.SpecialAttack,
                ivs.SpecialDefense,
                ivs.Speed));
    }

    private static string GetOptionLabel(
        SwShStaticEncountersWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShStaticEncountersWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static string GetOptionDisplayName(
        SwShStaticEncountersWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var label = GetOptionLabel(workflow, field, value, fallbackPrefix);
        var prefix = $"{value.ToString("000", CultureInfo.InvariantCulture)} ";

        return label.StartsWith(prefix, StringComparison.Ordinal)
            ? label[prefix.Length..]
            : label;
    }

    private static SwShStaticEncounterEdit? ToStaticEncounterEdit(
        SwShStaticEncounterArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(
                edit.RecordId,
                out var encounterIndex,
                out var encounterId)
            || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Static Encounter edit"));
            return null;
        }

        SwShStaticEncounterRecord? encounter;
        if (encounterId is null)
        {
            encounter = archive.Encounters.Count(candidate => candidate.Index == encounterIndex) == 1
                ? archive.Encounters.First(candidate => candidate.Index == encounterIndex)
                : null;
        }
        else
        {
            var indexed = archive.Encounters
                .Where(candidate => candidate.Index == encounterIndex)
                .ToArray();
            var identityMatches = archive.Encounters
                .Where(candidate => candidate.EncounterId == encounterId.Value)
                .ToArray();
            if (indexed.Length == 1
                && indexed[0].EncounterId == encounterId.Value
                && identityMatches.Length == 1)
            {
                encounter = indexed[0];
            }
            else
            {
                encounter = null;
            }
        }

        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Static Encounter edit no longer resolves to exactly one source record.",
                field: edit.Field,
                expected: "One source encounter matching the staged index and encounter ID"));
            return null;
        }

        return new SwShStaticEncounterEdit(encounter.Index, field, value);
    }

    private static SwShStaticEncounterField? MapField(string? field)
    {
        return field switch
        {
            SwShStaticEncountersWorkflowService.SpeciesField => SwShStaticEncounterField.Species,
            SwShStaticEncountersWorkflowService.FormField => SwShStaticEncounterField.Form,
            SwShStaticEncountersWorkflowService.LevelField => SwShStaticEncounterField.Level,
            SwShStaticEncountersWorkflowService.HeldItemIdField => SwShStaticEncounterField.HeldItem,
            SwShStaticEncountersWorkflowService.AbilityField => SwShStaticEncounterField.Ability,
            SwShStaticEncountersWorkflowService.NatureField => SwShStaticEncounterField.Nature,
            SwShStaticEncountersWorkflowService.GenderField => SwShStaticEncounterField.Gender,
            SwShStaticEncountersWorkflowService.ShinyLockField => SwShStaticEncounterField.ShinyLock,
            SwShStaticEncountersWorkflowService.EncounterScenarioField => SwShStaticEncounterField.EncounterScenario,
            SwShStaticEncountersWorkflowService.DynamaxLevelField => SwShStaticEncounterField.DynamaxLevel,
            SwShStaticEncountersWorkflowService.CanGigantamaxField => SwShStaticEncounterField.CanGigantamax,
            SwShStaticEncountersWorkflowService.Move0Field => SwShStaticEncounterField.Move0,
            SwShStaticEncountersWorkflowService.Move1Field => SwShStaticEncounterField.Move1,
            SwShStaticEncountersWorkflowService.Move2Field => SwShStaticEncounterField.Move2,
            SwShStaticEncountersWorkflowService.Move3Field => SwShStaticEncounterField.Move3,
            SwShStaticEncountersWorkflowService.EvHpField => SwShStaticEncounterField.EvHp,
            SwShStaticEncountersWorkflowService.EvAttackField => SwShStaticEncounterField.EvAttack,
            SwShStaticEncountersWorkflowService.EvDefenseField => SwShStaticEncounterField.EvDefense,
            SwShStaticEncountersWorkflowService.EvSpeedField => SwShStaticEncounterField.EvSpeed,
            SwShStaticEncountersWorkflowService.EvSpecialAttackField => SwShStaticEncounterField.EvSpecialAttack,
            SwShStaticEncountersWorkflowService.EvSpecialDefenseField => SwShStaticEncounterField.EvSpecialDefense,
            SwShStaticEncountersWorkflowService.IvHpField => SwShStaticEncounterField.IvHp,
            SwShStaticEncountersWorkflowService.IvAttackField => SwShStaticEncounterField.IvAttack,
            SwShStaticEncountersWorkflowService.IvDefenseField => SwShStaticEncounterField.IvDefense,
            SwShStaticEncountersWorkflowService.IvSpeedField => SwShStaticEncounterField.IvSpeed,
            SwShStaticEncountersWorkflowService.IvSpecialAttackField => SwShStaticEncounterField.IvSpecialAttack,
            SwShStaticEncountersWorkflowService.IvSpecialDefenseField => SwShStaticEncounterField.IvSpecialDefense,
            SwShStaticEncountersWorkflowService.FlawlessIvCountField => SwShStaticEncounterField.FlawlessIvCount,
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
                "Static Encounter apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShStaticEncountersWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static Encounter apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Static Encounter output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Static Encounter output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(tempPath, contents);
            if (!File.Exists(tempPath)
                || !File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Static Encounter temporary output verification failed.");
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

    private static string CreatePlanReason(IReadOnlyList<PendingEdit> pendingEdits)
    {
        return pendingEdits.Count == 1
            ? $"Apply pending Static Encounter edit: {pendingEdits[0].Summary}"
            : $"Apply {pendingEdits.Count} pending Static Encounter edits: {string.Join(" ", pendingEdits.Select(edit => edit.Summary))}";
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

    private static ValidationDiagnostic CreateIvDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Static Encounter IV values must be -1 for random or 0-31 for fixed values; HP IV also accepts -4 for the 3-perfect sentinel.",
            field: field,
            expected: "Supported static encounter IV value");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Static Encounter field '{field}' is not supported by the workflow yet.",
            field: "field",
            expected: "Supported Static Encounter field");
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
            Domain: SwShStaticEncountersWorkflowService.StaticEncountersEditDomain,
            Field: field,
            Expected: expected);
    }
}
