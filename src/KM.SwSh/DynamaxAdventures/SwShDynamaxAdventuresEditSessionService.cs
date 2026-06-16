// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShDynamaxAdventuresWorkflowService dynamaxAdventuresWorkflowService;

    public SwShDynamaxAdventuresEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShDynamaxAdventuresWorkflowService? dynamaxAdventuresWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.dynamaxAdventuresWorkflowService = dynamaxAdventuresWorkflowService ?? new SwShDynamaxAdventuresWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShDynamaxAdventuresEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int entryIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditDynamaxAdventures(project, workflow, diagnostics))
        {
            return new SwShDynamaxAdventuresEditResult(workflow, currentSession, diagnostics);
        }

        var effectiveWorkflow = RefreshDynamicEncounterOptions(
            project,
            OverlayPendingEdits(workflow, currentSession.PendingEdits));
        var encounter = effectiveWorkflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventure entry index {entryIndex} is not present in the loaded workflow.",
                field: "entryIndex",
                expected: "Existing Dynamax Adventure record"));
            return new SwShDynamaxAdventuresEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(
            encounter,
            field,
            value,
            effectiveWorkflow.Encounters,
            effectiveWorkflow.SafeNormalSpeciesOptions,
            diagnostics);
        if (pendingEdit is null)
        {
            return new SwShDynamaxAdventuresEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingEncounterEdits(
            currentSession,
            CreateRelatedPendingEdits(encounter, pendingEdit));

        return new SwShDynamaxAdventuresEditResult(
            RefreshDynamicEncounterOptions(
                project,
                OverlayPendingEdits(workflow, updatedSession.PendingEdits)),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = dynamaxAdventuresWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditDynamaxAdventures(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var personalRecords = LoadPersonalRecords(project);
        var learnsetRecords = LoadLearnsetRecords(project);
        ValidateDynamaxAdventureCompatibility(
            OverlayPendingEdits(workflow, session.PendingEdits),
            usableMoveIds,
            personalRecords,
            learnsetRecords,
            diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Dynamax Adventures change is valid."));
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
                "Create a pending Dynamax Adventures edit before reviewing a change plan.",
                expected: "Pending Dynamax Adventures edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures change plan could not resolve the source table.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(paths, source.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply target must stay inside the configured output root.",
                file: source.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        var applyState = CreateApplyState(paths, source, session, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) || applyState is null)
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var tableRestoresVanilla = applyState.HasTableEdits && applyState.MatchesBaseArchive;
        if (applyState.HasTableEdits && !tableRestoresVanilla && !applyState.SourceLengthMatchesBase)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures source table layout differs from the vanilla table. Restore the generated Adventure table before making new Pokemon edits.",
                file: source.GraphEntry.RelativePath,
                expected: "Vanilla-length Dynamax Adventures table or a restore-to-vanilla change"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        if (applyState.HasTableEdits)
        {
            var tableWrite = new PlannedFileWrite(
                source.GraphEntry.RelativePath,
                [new ProjectFileReference(GetSourceLayer(source.GraphEntry), source.GraphEntry.RelativePath)],
                File.Exists(targetPath),
                tableRestoresVanilla
                    ? "Restore vanilla Dynamax Adventures Pokemon by removing the generated Adventure table."
                    : session.PendingEdits.Count == 1
                    ? $"Apply pending Dynamax Adventures edit: {session.PendingEdits[0].Summary}"
                    : $"Apply {session.PendingEdits.Count} pending Dynamax Adventures edits.");
            writes.Add(tableWrite);
        }

        var mainTargetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(
            paths,
            SwShExeFsPatchWorkflowService.ExeFsMainPath);
        var bossTargetRemap = applyState.BossTargetRemap;
        var shouldCleanupMain = (tableRestoresVanilla || bossTargetRemap?.IsRestore == true)
            && mainTargetPath is not null
            && File.Exists(mainTargetPath);
        var shouldPatchMain = RequiresMainPatch(session.PendingEdits) && !tableRestoresVanilla && bossTargetRemap?.IsRestore != true;
        if (shouldPatchMain || shouldCleanupMain)
        {
            var mainSource = ResolveWorkflowFile(project, SwShExeFsPatchWorkflowService.ExeFsMainPath);
            if (shouldPatchMain && mainSource is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures identity edits require exefs/main so the game's hardcoded Adventure mirrors can be updated.",
                    file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    expected: "Readable Sword/Shield exefs/main NSO"));
                return new ChangePlan(session.Id, [], diagnostics);
            }

            mainTargetPath = ResolveOutputPath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath, diagnostics);
            if (mainTargetPath is null)
            {
                return new ChangePlan(session.Id, [], diagnostics);
            }

            if (shouldCleanupMain && !BaseSourceExists(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures restore could not resolve base exefs/main for safe cleanup.",
                    file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                    expected: "Readable base ExeFS main"));
                return new ChangePlan(session.Id, [], diagnostics);
            }

            IReadOnlyList<ProjectFileReference> sources = shouldCleanupMain
                ? [
                    new ProjectFileReference(ProjectFileLayer.Generated, SwShExeFsPatchWorkflowService.ExeFsMainPath),
                    new ProjectFileReference(ProjectFileLayer.Base, SwShExeFsPatchWorkflowService.ExeFsMainPath),
                ]
                : [new ProjectFileReference(GetSourceLayer(mainSource!.GraphEntry), mainSource.GraphEntry.RelativePath)];
            writes.Add(new PlannedFileWrite(
                SwShExeFsPatchWorkflowService.ExeFsMainPath,
                sources,
                File.Exists(mainTargetPath),
                shouldCleanupMain
                    ? "Restore or remove Dynamax Adventures ExeFS mirrors because Adventure Pokemon match vanilla."
                    : bossTargetRemap is not null
                    ? $"Patch Dynamax Adventures final boss target species from {bossTargetRemap.FromSpecies.ToString(CultureInfo.InvariantCulture)} to {bossTargetRemap.ToSpecies.ToString(CultureInfo.InvariantCulture)}."
                    : "Patch Dynamax Adventures ExeFS mirrors for edited Adventure identity data."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Change plan preview contains {writes.Count:N0} target file(s).")));

        return new ChangePlan(session.Id, writes, diagnostics);
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
                expected: "Current reviewed Dynamax Adventures change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply could not resolve the source table.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var archive = SwShDynamaxAdventureArchive.Parse(sourceBytes);
            var edits = session.PendingEdits
                .Select(edit => ToDynamaxAdventureEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = archive.WriteEdits(edits);
            var finalArchive = SwShDynamaxAdventureArchive.Parse(output);
            var baseArchive = TryLoadBaseDynamaxAdventureArchive(paths);
            var tableRestoresVanilla = edits.Length > 0
                && baseArchive is not null
                && ArchiveRecordsEqual(finalArchive, baseArchive);
            var bossTargetRemap = CreateBossTargetRemap(archive, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            byte[]? patchedMain = null;
            string? mainTargetPath = null;

            if (!tableRestoresVanilla && RequiresMainPatch(session.PendingEdits) && bossTargetRemap?.IsRestore != true)
            {
                var mainSource = ResolveWorkflowFile(project, SwShExeFsPatchWorkflowService.ExeFsMainPath);
                if (mainSource is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Dynamax Adventures apply could not resolve exefs/main for Adventure mirror patching.",
                        file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                        expected: "Readable Sword/Shield exefs/main NSO"));
                    return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
                }

                mainTargetPath = ResolveOutputPath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath, diagnostics);
                if (mainTargetPath is null)
                {
                    return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
                }

                patchedMain = File.ReadAllBytes(mainSource.AbsolutePath);
                if (RequiresAdventureMirrorPatch(session.PendingEdits))
                {
                    patchedMain = SwShDynamaxAdventuresMainPatcher.Apply(
                        patchedMain,
                        finalArchive,
                        RequiresCommandValidatorPatch(session.PendingEdits));
                }

                if (bossTargetRemap is not null)
                {
                    patchedMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                        patchedMain,
                        finalArchive,
                        bossTargetRemap.FromSpecies,
                        bossTargetRemap.ToSpecies);
                }
            }

            if (tableRestoresVanilla)
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, source.GraphEntry.RelativePath));
                }
            }
            else if (edits.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllBytes(targetPath, output);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, source.GraphEntry.RelativePath));
            }

            if (tableRestoresVanilla || bossTargetRemap?.IsRestore == true)
            {
                mainTargetPath = ResolveOutputPath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath, diagnostics);
                if (mainTargetPath is not null && File.Exists(mainTargetPath))
                {
                    if (!RestoreOrDeleteMain(paths, mainTargetPath, finalArchive.Entries.Count, diagnostics))
                    {
                        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
                    }

                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShExeFsPatchWorkflowService.ExeFsMainPath));
                }
            }

            if (!tableRestoresVanilla && bossTargetRemap?.IsRestore != true && patchedMain is not null && mainTargetPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(mainTargetPath)!);
                File.WriteAllBytes(mainTargetPath, patchedMain);
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShExeFsPatchWorkflowService.ExeFsMainPath));
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                tableRestoresVanilla || bossTargetRemap?.IsRestore == true
                    ? "Restored vanilla Dynamax Adventures Pokemon and safely cleaned generated LayeredFS output."
                    : "Applied Dynamax Adventures change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change could not be applied: {exception.Message}",
                file: RequiresMainPatch(session.PendingEdits) ? SwShExeFsPatchWorkflowService.ExeFsMainPath : source.GraphEntry.RelativePath,
                expected: "Layout-preserving edit to a Sword/Shield Dynamax Adventures table and Adventure mirrors"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures output file could not be written: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShDynamaxAdventureEntry encounter,
        string field,
        string value,
        IReadOnlyList<SwShDynamaxAdventureEntry> encounters,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> safeNormalSpeciesOptions,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SwShDynamaxAdventuresWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!encounter.IsEditable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} is hidden from the safe Dynamax Adventures editor because boss, legendary, mythical, Ultra Beast, special, and unsupported rows are not live-proven safe to edit.",
                field: "entryIndex",
                expected: "A visible ordinary normal-route Dynamax Adventures row"));
            return null;
        }

        var parsedValue = TryParseFieldValue(editableField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && !CanStageSafeNormalSpeciesValue(encounter, parsedValue.Value, safeNormalSpeciesOptions))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use species {parsedValue.Value.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: normalizedField,
                expected: "A species from the safe non-duplicate normal-route replacement list"));
            return null;
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.FormField
            && !CanStageSafeNormalFormValue(encounter, parsedValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use form {parsedValue.Value.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: normalizedField,
                expected: "Form 0 or the row's original vanilla form"));
            return null;
        }

        if (normalizedField is SwShDynamaxAdventuresWorkflowService.SpeciesField or SwShDynamaxAdventuresWorkflowService.FormField)
        {
            var targetSpecies = normalizedField == SwShDynamaxAdventuresWorkflowService.SpeciesField
                ? parsedValue.Value
                : encounter.SpeciesId;
            var targetForm = normalizedField == SwShDynamaxAdventuresWorkflowService.SpeciesField
                && parsedValue.Value != encounter.SpeciesId
                    ? 0
                    : normalizedField == SwShDynamaxAdventuresWorkflowService.FormField
                        ? parsedValue.Value
                        : encounter.Form;
            if (CreatesDuplicateNormalSpeciesForm(encounters, encounter, targetSpecies, targetForm))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} cannot use duplicate normal-route species/form {targetSpecies.ToString(CultureInfo.InvariantCulture)}/{targetForm.ToString(CultureInfo.InvariantCulture)}.",
                    field: normalizedField,
                    expected: "Unique species/form identities inside the normal Dynamax Adventures pool"));
                return null;
            }
        }

        if (normalizedField == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            && parsedValue.Value == SwShDynamaxAdventureArchive.MaximumGigantamaxState
            && !SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(encounter.SpeciesId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Species} cannot use the Dynamax Adventures Gigantamax state.",
                field: normalizedField,
                expected: "Gigantamax-capable species or Normal Gigantamax state"));
            return null;
        }

        if (IsMoveField(normalizedField)
            && parsedValue.Value == 0
            && !CanStageVanillaZeroMoveSlot(encounter, normalizedField))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures requires all four move slots to be populated.",
                field: normalizedField,
                expected: "A usable nonzero move"));
            return null;
        }

        if (IsMoveField(normalizedField)
            && parsedValue.Value != 0
            && encounter.MoveOptions.Count > 0
            && !encounter.MoveOptions.Any(option => option.Value == parsedValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use move {parsedValue.Value.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: normalizedField,
                expected: "A usable move compatible with the row's current species/form and level"));
            return null;
        }

        AddLinkedUsageWarning(normalizedField, diagnostics);

        return new PendingEdit(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            $"Set {encounter.Label} {editableField.Label} to {parsedValue.Value}.",
            [new ProjectFileReference(encounter.Provenance.SourceLayer, encounter.Provenance.SourceFile)],
            RecordId: SwShDynamaxAdventuresWorkflowService.CreateEncounterRecordId(encounter.EntryIndex),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Dynamax Adventures workflow.",
                expected: SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain));
            return;
        }

        var editableField = SwShDynamaxAdventuresWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit targets an invalid record.",
                field: "entryIndex",
                expected: "Dynamax Adventure record"));
            return;
        }

        var encounter = workflow.Encounters.FirstOrDefault(encounter => encounter.EntryIndex == entryIndex);
        if (encounter is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit targets a record that is not loaded.",
                field: "entryIndex",
                expected: "Existing Dynamax Adventure record"));
            return;
        }

        if (!encounter.IsEditable)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Dynamax Adventures edit targets {encounter.Label}, which is hidden from the safe editor because this row class is not live-proven safe.",
                field: "entryIndex",
                expected: "A visible ordinary normal-route Dynamax Adventures row"));
            return;
        }

        TryParseFieldValue(editableField, edit.NewValue, diagnostics);
        if (edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var species)
            && !CanStageSafeNormalSpeciesValue(encounter, species, workflow.SafeNormalSpeciesOptions))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{encounter.Label} cannot use species {species.ToString(CultureInfo.InvariantCulture)} in the safe Dynamax Adventures editor.",
                field: edit.Field,
                expected: "A species from the safe non-duplicate normal-route replacement list"));
        }

        AddLinkedUsageWarning(edit.Field, diagnostics);
    }

    private static bool CanStageSafeNormalSpeciesValue(
        SwShDynamaxAdventureEntry encounter,
        int species,
        IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> safeNormalSpeciesOptions)
    {
        return species == encounter.SpeciesId
            || safeNormalSpeciesOptions.Count == 0
            || safeNormalSpeciesOptions.Any(option => option.Value == species);
    }

    private static bool CanStageSafeNormalFormValue(
        SwShDynamaxAdventureEntry encounter,
        int form)
    {
        return form == 0
            || form == encounter.Form
            || form == encounter.VanillaPokemon?.Form;
    }

    private static bool CreatesDuplicateNormalSpeciesForm(
        IReadOnlyList<SwShDynamaxAdventureEntry> encounters,
        SwShDynamaxAdventureEntry target,
        int species,
        int form)
    {
        if (!IsNormalEncounter(target))
        {
            return false;
        }

        return encounters.Any(encounter =>
            encounter.EntryIndex != target.EntryIndex
            && IsNormalEncounter(encounter)
            && encounter.SpeciesId == species
            && encounter.Form == form);
    }

    private static void ValidateBossTargetEdits(
        SwShDynamaxAdventuresWorkflow workflow,
        IReadOnlyList<PendingEdit> pendingEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var bossTargetEdits = pendingEdits
            .Where(edit => edit.Field == SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField)
            .ToArray();
        if (bossTargetEdits.Length == 0)
        {
            return;
        }

        if (bossTargetEdits.Length > 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures can stage only one boss target-species remap at a time.",
                field: SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
                expected: "One final boss target remap"));
        }

        if (pendingEdits.Count != bossTargetEdits.Length)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures boss target-species remaps must be reviewed and applied separately from table row edits.",
                field: SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
                expected: "A boss target-only edit session"));
        }

        foreach (var edit in bossTargetEdits)
        {
            if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
                || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var targetSpecies))
            {
                continue;
            }

            var encounter = workflow.Encounters.FirstOrDefault(candidate => candidate.EntryIndex == entryIndex);
            if (encounter is not null)
            {
                ValidateBossTargetValue(encounter, targetSpecies, diagnostics);
            }
        }
    }

    private static void ValidateBossTargetValue(
        SwShDynamaxAdventureEntry encounter,
        int targetSpecies,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsBossEncounter(encounter))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures boss target-species remaps can only target boss rows.",
                field: SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
                expected: "Boss row 226 or later"));
            return;
        }

        if (targetSpecies == encounter.SpeciesId)
        {
            return;
        }

        if (encounter.BossTargetOptions.Any(option => option.SpeciesId == targetSpecies))
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"{encounter.Label} cannot target boss species {targetSpecies.ToString(CultureInfo.InvariantCulture)} with the guarded boss remap path.",
            field: SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
            expected: "A unique boss species in the same version/story bucket"));
    }

    private static void ValidateDynamaxAdventureCompatibility(
        SwShDynamaxAdventuresWorkflow workflow,
        IReadOnlySet<int> usableMoveIds,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        IReadOnlyList<SwShPokemonLearnsetRecord> learnsetRecords,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var vanillaBossSpeciesForms = GetVanillaBossSpeciesForms(workflow);
        ValidateDistinctSpeciesForms(workflow.Encounters.Where(IsNormalEncounter), "normal route", diagnostics);
        ValidateDistinctSpeciesForms(workflow.Encounters.Where(IsBossEncounter), "boss", diagnostics);

        foreach (var encounter in workflow.Encounters)
        {
            var personalRecord = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(encounter.SpeciesId, encounter.Form, personalRecords);
            var learnsetRecord = personalRecord is not null && (uint)personalRecord.PersonalId < (uint)learnsetRecords.Count
                ? learnsetRecords[personalRecord.PersonalId]
                : null;
            if (personalRecords.Count > 0 && personalRecord?.IsPresentInGame != true)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses species {encounter.SpeciesId.ToString(CultureInfo.InvariantCulture)}, which is not marked present in Sword/Shield personal data.",
                    field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                    expected: "Species present in Sword/Shield personal data"));
            }

            if (personalRecord?.CanNotDynamax == true)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses a species/form that personal data marks as unable to Dynamax.",
                    field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                    expected: "Species/form allowed to Dynamax"));
            }

            if (encounter.Level is < 1 or > 100)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} uses level {encounter.Level.ToString(CultureInfo.InvariantCulture)}. Dynamax Adventures Pokemon should stay in the normal battle level range.",
                    field: SwShDynamaxAdventuresWorkflowService.LevelField,
                    expected: "Level 1 through 100"));
            }

            if (IsNormalEncounter(encounter))
            {
                if (IntroducesNonVanillaNormalForm(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} introduces form {encounter.Form.ToString(CultureInfo.InvariantCulture)} in a normal route row. Only form 0 normal-route replacements are currently live-proven safe.",
                        field: SwShDynamaxAdventuresWorkflowService.FormField,
                        expected: "Form 0, or the row's original vanilla form"));
                }

                if (IntroducesNormalRouteIdentity(encounter)
                    && (encounter.SpeciesId <= 0
                        || encounter.SpeciesId > SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses species {encounter.SpeciesId.ToString(CultureInfo.InvariantCulture)} outside the verified Dynamax Adventures normal-route replacement range.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: $"Species 1 through {SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies.ToString(CultureInfo.InvariantCulture)}"));
                }

                if (personalRecord is not null
                    && personalRecord.Form != 0
                    && IntroducesNormalRouteIdentity(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a non-base personal form as a normal route replacement. Only base personal-form replacements are currently in the verified safe list.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Base personal form for normal route rows"));
                }

                if (vanillaBossSpeciesForms.Contains((encounter.SpeciesId, encounter.Form)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a vanilla Dynamax Adventures boss species/form in a normal route row. Live testing returned to the lobby for this category.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Non-boss species/form for normal route rows"));
                }

                if (SwShDynamaxAdventureSafetyRules.IsSpecialNormalRouteSpecies(encounter.SpeciesId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a legendary, mythical, Ultra Beast, or special boss-category species in a normal route row. Live testing returned to the lobby for this category.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Ordinary non-legendary species for normal route rows"));
                }

                if (encounter.Form != 0 && SwShDynamaxAdventureSafetyRules.IsBattleFusionSpecies(encounter.SpeciesId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses a battle-fusion form in a normal route row. Live testing returned to the lobby for this category.",
                        field: SwShDynamaxAdventuresWorkflowService.FormField,
                        expected: "Non-fusion normal route identity"));
                }
            }
            else
            {
                if (vanillaBossSpeciesForms.Count > 0 && !vanillaBossSpeciesForms.Contains((encounter.SpeciesId, encounter.Form)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} changes a boss row to a species/form that is not part of the vanilla Dynamax Adventures boss roster. Keep boss edits inside the vanilla boss pool until boss metadata is fully mapped.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Species/form from the vanilla Dynamax Adventures boss roster"));
                }

                if (encounter.VanillaPokemon is not null
                    && (encounter.SpeciesId != encounter.VanillaPokemon.SpeciesId
                        || encounter.Form != encounter.VanillaPokemon.Form))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} changes a boss identity without moving its capture flag and message metadata. Boss replacements need a dedicated metadata-copy operation before they are safe in the editor.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Vanilla boss identity until boss metadata-copy support is implemented"));
                }

                if (HasUnprovenBossRuntimeFieldChange(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} changes boss runtime fields before boss metadata-copy support has live final-boss proof.",
                        field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                        expected: "Vanilla boss runtime fields until the dedicated boss row-copy operation is implemented"));
                }
            }

            if (encounter.GigantamaxState == SwShDynamaxAdventureArchive.MaximumGigantamaxState
                && !SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(encounter.SpeciesId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{encounter.Label} is marked Gigantamax, but {encounter.Species} does not have a Gigantamax form.",
                    field: SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
                    expected: "Gigantamax-capable species or Normal Gigantamax state"));
            }

            if (encounter.Moves.Count < 4 || encounter.Moves.Any(move => move.MoveId == 0))
            {
                if (!PreservesVanillaZeroMoveSlots(encounter))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} introduces empty Dynamax Adventure move slots. Vanilla Ditto keeps its omitted slots, but edited Adventure Pokemon should not add new zero moves.",
                        field: SwShDynamaxAdventuresWorkflowService.Move0Field,
                        expected: "Four nonzero Adventure moves, or preserved vanilla zero slots"));
                }
            }

            if (usableMoveIds.Count > 0)
            {
                foreach (var move in encounter.Moves.Where(move => move.MoveId != 0 && !usableMoveIds.Contains(move.MoveId)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses move {move.MoveId.ToString(CultureInfo.InvariantCulture)} ({move.Move}), but that move is not marked usable in Sword/Shield move data.",
                        field: GetMoveField(move.Slot - 1),
                        expected: "Move with CanUseMove enabled"));
                }
            }

            if (personalRecord is not null && learnsetRecords.Count > 0)
            {
                foreach (var move in encounter.Moves.Where(move => move.MoveId != 0))
                {
                    if (SwShDynamaxAdventureSafetyRules.CanLearnMove(personalRecord, learnsetRecord, move.MoveId, encounter.Level)
                        || PreservesVanillaMoveCompatibilityException(encounter, move))
                    {
                        continue;
                    }

                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{encounter.Label} uses move {move.MoveId.ToString(CultureInfo.InvariantCulture)} ({move.Move}), but {encounter.Species} cannot normally learn it from Sword/Shield level-up, TM, TR, or tutor data.",
                        field: GetMoveField(move.Slot - 1),
                        expected: "A move compatible with that species/form, or the row's original vanilla move exception"));
                }
            }
        }
    }

    private static bool PreservesVanillaMoveCompatibilityException(
        SwShDynamaxAdventureEntry encounter,
        SwShDynamaxAdventureMoveRecord move)
    {
        var vanilla = encounter.VanillaPokemon;
        var slotIndex = move.Slot - 1;
        return vanilla is not null
            && encounter.SpeciesId == vanilla.SpeciesId
            && encounter.Form == vanilla.Form
            && encounter.Level >= vanilla.Level
            && (uint)slotIndex < (uint)vanilla.Moves.Count
            && vanilla.Moves[slotIndex].MoveId == move.MoveId;
    }

    private static bool PreservesVanillaZeroMoveSlots(SwShDynamaxAdventureEntry encounter)
    {
        if (encounter.VanillaPokemon is null
            || encounter.SpeciesId != encounter.VanillaPokemon.SpeciesId
            || encounter.Form != encounter.VanillaPokemon.Form
            || encounter.Moves.Count != encounter.VanillaPokemon.Moves.Count)
        {
            return false;
        }

        for (var index = 0; index < encounter.Moves.Count; index++)
        {
            if (encounter.Moves[index].MoveId == 0 && encounter.VanillaPokemon.Moves[index].MoveId != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanStageVanillaZeroMoveSlot(
        SwShDynamaxAdventureEntry encounter,
        string field)
    {
        var slot = GetMoveSlot(field);
        var vanilla = encounter.VanillaPokemon;
        return vanilla is not null
            && encounter.SpeciesId == vanilla.SpeciesId
            && encounter.Form == vanilla.Form
            && (uint)slot < (uint)vanilla.Moves.Count
            && vanilla.Moves[slot].MoveId == 0;
    }

    private static bool HasUnprovenBossRuntimeFieldChange(SwShDynamaxAdventureEntry encounter)
    {
        var vanilla = encounter.VanillaPokemon;
        return vanilla is not null
            && (encounter.GigantamaxState != vanilla.GigantamaxState
                || encounter.Level != vanilla.Level
                || encounter.Ability != vanilla.Ability
                || encounter.GuaranteedPerfectIvs != vanilla.GuaranteedPerfectIvs
                || encounter.Ivs != vanilla.Ivs
                || !MoveIdsEqual(encounter.Moves, vanilla.Moves));
    }

    private static bool MoveIdsEqual(
        IReadOnlyList<SwShDynamaxAdventureMoveRecord> left,
        IReadOnlyList<SwShDynamaxAdventureMoveRecord> right)
    {
        return left.Count == right.Count
            && left.Select(move => move.MoveId).SequenceEqual(right.Select(move => move.MoveId));
    }

    private static bool IntroducesNonVanillaNormalForm(SwShDynamaxAdventureEntry encounter)
    {
        return encounter.Form != 0
            && IntroducesNormalRouteIdentity(encounter);
    }

    private static bool IntroducesNormalRouteIdentity(SwShDynamaxAdventureEntry encounter)
    {
        return encounter.VanillaPokemon is null
            || encounter.SpeciesId != encounter.VanillaPokemon.SpeciesId
            || encounter.Form != encounter.VanillaPokemon.Form;
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SwShPokemonLearnsetRecord> LoadLearnsetRecords(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPokemonLearnsetTable.LearnsetDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShPokemonLearnsetTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlySet<(int Species, int Form)> GetVanillaBossSpeciesForms(SwShDynamaxAdventuresWorkflow workflow)
    {
        return workflow.Encounters
            .Where(IsBossEncounter)
            .Select(encounter => encounter.VanillaPokemon is null
                ? (encounter.SpeciesId, encounter.Form)
                : (encounter.VanillaPokemon.SpeciesId, encounter.VanillaPokemon.Form))
            .ToHashSet();
    }

    private static void ValidateDistinctSpeciesForms(
        IEnumerable<SwShDynamaxAdventureEntry> encounters,
        string poolName,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var duplicateGroup in encounters
            .GroupBy(encounter => (encounter.SpeciesId, encounter.Form))
            .Where(group => group.Count() > 1))
        {
            var rows = string.Join(
                ", ",
                duplicateGroup.Select(encounter => encounter.EntryIndex.ToString(CultureInfo.InvariantCulture)));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures {poolName} pool contains duplicate species/form {duplicateGroup.Key.SpeciesId}/{duplicateGroup.Key.Form} on rows {rows}. Live stress testing showed collapsed or duplicate pools can return to the lobby or freeze.",
                field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
                expected: "Unique species/form identities inside each Dynamax Adventures pool"));
        }
    }

    private static bool IsNormalEncounter(SwShDynamaxAdventureEntry encounter)
    {
        return SwShDynamaxAdventureSafetyRules.IsNormalEntryIndex(encounter.EntryIndex);
    }

    private static bool IsBossEncounter(SwShDynamaxAdventureEntry encounter)
    {
        return SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(encounter.EntryIndex);
    }

    private static int? TryParseFieldValue(
        SwShDynamaxAdventureEditableField editableField,
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

        if (IsIndividualIvOverrideField(editableField.Field))
        {
            parsedValue = ClampFixedIvValue(parsedValue);
        }

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Dynamax Adventures field value"));
            return null;
        }

        if (editableField.Field == SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField
            && parsedValue == 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Guaranteed perfect IVs supports Random IVs or 2-6 guaranteed perfect IVs; 1 is not representable in this table.",
                field: editableField.Field,
                expected: "0, 2, 3, 4, 5, or 6"));
            return null;
        }

        return parsedValue;
    }

    private static int ClampFixedIvValue(int value)
    {
        return Math.Clamp(
            value,
            SwShDynamaxAdventureArchive.MinimumFixedIvValue,
            SwShDynamaxAdventureArchive.MaximumFixedIvValue);
    }

    private static bool IsIndividualIvOverrideField(string field)
    {
        return field is
            SwShDynamaxAdventuresWorkflowService.IvAttackField
            or SwShDynamaxAdventuresWorkflowService.IvDefenseField
            or SwShDynamaxAdventuresWorkflowService.IvSpeedField
            or SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField
            or SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField;
    }

    private static bool IsMoveField(string field)
    {
        return field is
            SwShDynamaxAdventuresWorkflowService.Move0Field
            or SwShDynamaxAdventuresWorkflowService.Move1Field
            or SwShDynamaxAdventuresWorkflowService.Move2Field
            or SwShDynamaxAdventuresWorkflowService.Move3Field;
    }

    private static int GetMoveSlot(string field)
    {
        return field switch
        {
            SwShDynamaxAdventuresWorkflowService.Move0Field => 0,
            SwShDynamaxAdventuresWorkflowService.Move1Field => 1,
            SwShDynamaxAdventuresWorkflowService.Move2Field => 2,
            SwShDynamaxAdventuresWorkflowService.Move3Field => 3,
            _ => -1,
        };
    }

    private static void AddLinkedUsageWarning(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShDynamaxAdventuresWorkflowService.SpeciesField
            or SwShDynamaxAdventuresWorkflowService.FormField
            or SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            or SwShDynamaxAdventuresWorkflowService.Move0Field
            or SwShDynamaxAdventuresWorkflowService.Move1Field
            or SwShDynamaxAdventuresWorkflowService.Move2Field
            or SwShDynamaxAdventuresWorkflowService.Move3Field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Dynamax Adventures Pokemon edits only affect routes that select the edited row; identity edits also require the matching ExeFS mirror patch.",
                field: field,
                expected: "Generated Adventure table, matching route seed, and ExeFS mirrors for identity edits"));
        }
    }

    private static bool CanEditDynamaxAdventures(
        OpenedProject project,
        SwShDynamaxAdventuresWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<PendingEdit> CreateRelatedPendingEdits(
        SwShDynamaxAdventureEntry encounter,
        PendingEdit pendingEdit)
    {
        if (pendingEdit.Field != SwShDynamaxAdventuresWorkflowService.SpeciesField)
        {
            return [pendingEdit];
        }

        var relatedEdits = new List<PendingEdit> { pendingEdit };
        if (!int.TryParse(pendingEdit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var speciesId))
        {
            return relatedEdits;
        }

        if (encounter.Form != 0 && speciesId != encounter.SpeciesId)
        {
            relatedEdits.Add(new PendingEdit(
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
                $"Set {encounter.Label} Form to 0 because normal Adventure replacements are only live-proven safe as base forms.",
                pendingEdit.Sources,
                RecordId: pendingEdit.RecordId,
                Field: SwShDynamaxAdventuresWorkflowService.FormField,
                NewValue: "0"));
        }

        if (encounter.GigantamaxState == SwShDynamaxAdventureArchive.MaximumGigantamaxState
            && !SwShDynamaxAdventuresWorkflowService.IsGigantamaxCapableSpecies(speciesId))
        {
            var resetGigantamax = new PendingEdit(
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
                $"Set {encounter.Label} Gigantamax state to 1 because the selected species cannot Gigantamax.",
                pendingEdit.Sources,
                RecordId: pendingEdit.RecordId,
                Field: SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
                NewValue: "1");

            relatedEdits.Add(resetGigantamax);
        }

        return relatedEdits;
    }

    private static EditSession ReplacePendingEncounterEdits(
        EditSession session,
        IReadOnlyList<PendingEdit> pendingEdits)
    {
        var updatedPendingEdits = session.PendingEdits
            .Where(edit => pendingEdits.All(pendingEdit => !IsSameEncounterEdit(edit, pendingEdit)))
            .Concat(pendingEdits)
            .ToArray();

        return session with { PendingEdits = updatedPendingEdits };
    }

    private static bool IsSameEncounterEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShDynamaxAdventuresWorkflow OverlayPendingEdits(
        SwShDynamaxAdventuresWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShDynamaxAdventuresWorkflow RefreshDynamicEncounterOptions(
        OpenedProject project,
        SwShDynamaxAdventuresWorkflow workflow)
    {
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);
        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var personalRecords = LoadPersonalRecords(project);
        var learnsetRecords = LoadLearnsetRecords(project);

        return workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => RefreshEncounterOptions(
                    workflow,
                    encounter,
                    abilityResolver,
                    usableMoveIds,
                    personalRecords,
                    learnsetRecords))
                .ToArray(),
        };
    }

    private static SwShDynamaxAdventureEntry RefreshEncounterOptions(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        SwShPokemonAbilityOptionResolver abilityResolver,
        IReadOnlySet<int> usableMoveIds,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        IReadOnlyList<SwShPokemonLearnsetRecord> learnsetRecords)
    {
        var abilityOptions = CreateAbilityOptions(abilityResolver, encounter.SpeciesId, encounter.Form);
        return encounter with
        {
            AbilityLabel = abilityOptions.FirstOrDefault(option => option.Value == encounter.Ability)?.Label
                ?? GetOptionLabel(workflow, SwShDynamaxAdventuresWorkflowService.AbilityField, encounter.Ability, "Ability roll"),
            AbilityOptions = abilityOptions,
            MoveOptions = CreateMoveOptions(workflow, encounter, usableMoveIds, personalRecords, learnsetRecords),
        };
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateAbilityOptions(
        SwShPokemonAbilityOptionResolver abilityResolver,
        int speciesId,
        int form)
    {
        return abilityResolver
            .CreateOptions(speciesId, form, SwShAbilityOptionMode.ZeroBasedSlots)
            .Select(option => new SwShDynamaxAdventureEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> CreateMoveOptions(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        IReadOnlySet<int> usableMoveIds,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        IReadOnlyList<SwShPokemonLearnsetRecord> learnsetRecords)
    {
        if (usableMoveIds.Count == 0 || personalRecords.Count == 0 || learnsetRecords.Count == 0)
        {
            return encounter.MoveOptions;
        }

        var personal = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(
            encounter.SpeciesId,
            encounter.Form,
            personalRecords);
        if (personal is null)
        {
            return encounter.MoveOptions;
        }

        var learnset = (uint)personal.PersonalId < (uint)learnsetRecords.Count
            ? learnsetRecords[personal.PersonalId]
            : null;
        var moveIds = new SortedSet<int>();
        foreach (var moveId in usableMoveIds)
        {
            if (SwShDynamaxAdventureSafetyRules.CanLearnMove(personal, learnset, moveId, encounter.Level))
            {
                moveIds.Add(moveId);
            }
        }

        foreach (var move in encounter.Moves)
        {
            moveIds.Add(move.MoveId);
        }

        var vanilla = encounter.VanillaPokemon;
        if (vanilla is not null
            && vanilla.SpeciesId == encounter.SpeciesId
            && vanilla.Form == encounter.Form
            && encounter.Level >= vanilla.Level)
        {
            foreach (var move in vanilla.Moves)
            {
                moveIds.Add(move.MoveId);
            }
        }

        return moveIds
            .Select(moveId => new SwShDynamaxAdventureEditableFieldOption(
                moveId,
                GetMoveOptionLabel(workflow, encounter, moveId)))
            .ToArray();
    }

    private static string GetMoveOptionLabel(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        int moveId)
    {
        var globalMoveOption = workflow.EditableFields
            .FirstOrDefault(field => field.Field == SwShDynamaxAdventuresWorkflowService.Move0Field)
            ?.Options
            .FirstOrDefault(option => option.Value == moveId);
        if (globalMoveOption is not null)
        {
            return globalMoveOption.Label;
        }

        var currentMove = encounter.Moves.FirstOrDefault(move => move.MoveId == moveId);
        if (currentMove is not null && !string.IsNullOrWhiteSpace(currentMove.Move))
        {
            var prefix = moveId.ToString("000", CultureInfo.InvariantCulture);
            return currentMove.Move.StartsWith(prefix, StringComparison.Ordinal)
                ? currentMove.Move
                : $"{prefix} {currentMove.Move}";
        }

        return moveId == 0
            ? "000 None"
            : $"{moveId.ToString("000", CultureInfo.InvariantCulture)} Move {moveId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static SwShDynamaxAdventuresWorkflow OverlayPendingEdit(
        SwShDynamaxAdventuresWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain, StringComparison.Ordinal)
            || !SwShDynamaxAdventuresWorkflowService.IsEditableField(edit.Field)
            || !SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Encounters = workflow.Encounters
                .Select(encounter => encounter.EntryIndex == entryIndex
                    ? OverlayEncounterField(workflow, encounter, edit.Field!, value)
                    : encounter)
                .ToArray(),
        };
    }

    private static SwShDynamaxAdventureEntry OverlayEncounterField(
        SwShDynamaxAdventuresWorkflow workflow,
        SwShDynamaxAdventureEntry encounter,
        string field,
        int value)
    {
        var updatedEncounter = field switch
        {
            SwShDynamaxAdventuresWorkflowService.SpeciesField => encounter with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, field, value, "Species"),
            },
            SwShDynamaxAdventuresWorkflowService.FormField => encounter with { Form = value },
            SwShDynamaxAdventuresWorkflowService.LevelField => encounter with { Level = value },
            SwShDynamaxAdventuresWorkflowService.BallItemIdField => encounter with
            {
                BallItemId = value,
                BallItem = GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShDynamaxAdventuresWorkflowService.AbilityField => encounter with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability roll"),
            },
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField => encounter with
            {
                GigantamaxState = value,
                GigantamaxLabel = GetOptionLabel(workflow, field, value, "Gigantamax"),
            },
            SwShDynamaxAdventuresWorkflowService.VersionField => encounter with
            {
                Version = value,
                VersionLabel = GetOptionLabel(workflow, field, value, "Version"),
            },
            SwShDynamaxAdventuresWorkflowService.ShinyRollField => encounter with
            {
                ShinyRoll = value,
                ShinyRollLabel = GetOptionLabel(workflow, field, value, "Shiny roll"),
            },
            SwShDynamaxAdventuresWorkflowService.Move0Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 0, value) },
            SwShDynamaxAdventuresWorkflowService.Move1Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 1, value) },
            SwShDynamaxAdventuresWorkflowService.Move2Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 2, value) },
            SwShDynamaxAdventuresWorkflowService.Move3Field => encounter with { Moves = SetMove(workflow, encounter.Moves, 3, value) },
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField => encounter with
            {
                GuaranteedPerfectIvs = value,
                Ivs = encounter.Ivs with { Hp = value == 0 ? SwShDynamaxAdventureArchive.RandomIvValue : -value },
            },
            SwShDynamaxAdventuresWorkflowService.IvAttackField => encounter with { Ivs = encounter.Ivs with { Attack = value } },
            SwShDynamaxAdventuresWorkflowService.IvDefenseField => encounter with { Ivs = encounter.Ivs with { Defense = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpeedField => encounter with { Ivs = encounter.Ivs with { Speed = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField => encounter with { Ivs = encounter.Ivs with { SpecialAttack = value } },
            SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField => encounter with { Ivs = encounter.Ivs with { SpecialDefense = value } },
            SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField => encounter with { IsSingleCapture = value != 0 },
            SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField => encounter with { IsStoryProgressGated = value != 0 },
            SwShDynamaxAdventuresWorkflowService.OtGenderField => encounter with
            {
                OtGender = value,
                OtGenderLabel = GetOptionLabel(workflow, field, value, "OT gender"),
            },
            _ => encounter,
        };

        updatedEncounter = updatedEncounter with
        {
            IvSummary = FormatIvSummary(updatedEncounter.Ivs, updatedEncounter.GuaranteedPerfectIvs),
            Label = FormatEncounterLabel(updatedEncounter),
        };

        return updatedEncounter;
    }

    private static IReadOnlyList<SwShDynamaxAdventureMoveRecord> SetMove(
        SwShDynamaxAdventuresWorkflow workflow,
        IReadOnlyList<SwShDynamaxAdventureMoveRecord> moves,
        int slot,
        int value)
    {
        return moves
            .Select(move => move.Slot == slot + 1
                ? move with
                {
                    MoveId = value,
                    Move = GetOptionLabel(workflow, GetMoveField(slot), value, "Move"),
                }
                : move)
            .ToArray();
    }

    private static string GetMoveField(int slot)
    {
        return slot switch
        {
            0 => SwShDynamaxAdventuresWorkflowService.Move0Field,
            1 => SwShDynamaxAdventuresWorkflowService.Move1Field,
            2 => SwShDynamaxAdventuresWorkflowService.Move2Field,
            3 => SwShDynamaxAdventuresWorkflowService.Move3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static string FormatEncounterLabel(SwShDynamaxAdventureEntry encounter)
    {
        var speciesLabel = SwShSpeciesFormLabels.FormatSpeciesFormLabel(
            encounter.Species,
            encounter.SpeciesId,
            encounter.Form);
        var versionSuffix = string.Equals(encounter.VersionLabel, "Both", StringComparison.Ordinal)
            ? string.Empty
            : $" [{encounter.VersionLabel}]";
        return $"{encounter.EntryIndex.ToString("000", CultureInfo.InvariantCulture)} / {encounter.AdventureIndex.ToString("000", CultureInfo.InvariantCulture)} - {speciesLabel}{versionSuffix}";
    }

    private static string FormatIvSummary(SwShDynamaxAdventureIvsRecord ivs, int guaranteedPerfectIvs)
    {
        return string.Join(
            ", ",
            [
                guaranteedPerfectIvs > 0
                    ? $"{guaranteedPerfectIvs.ToString(CultureInfo.InvariantCulture)} guaranteed perfect"
                    : "random HP",
                $"Atk {FormatIvValue(ivs.Attack)}",
                $"Def {FormatIvValue(ivs.Defense)}",
                $"SpA {FormatIvValue(ivs.SpecialAttack)}",
                $"SpD {FormatIvValue(ivs.SpecialDefense)}",
                $"Spe {FormatIvValue(ivs.Speed)}",
            ]);
    }

    private static string FormatIvValue(int value)
    {
        return value == SwShDynamaxAdventureArchive.RandomIvValue
            ? "random"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetOptionLabel(
        SwShDynamaxAdventuresWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShDynamaxAdventuresWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static DynamaxAdventureApplyState? CreateApplyState(
        ProjectPaths paths,
        SwShDynamaxAdventuresWorkflowService.WorkflowFileSource source,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var archive = SwShDynamaxAdventureArchive.Parse(sourceBytes);
            var edits = session.PendingEdits
                .Select(edit => ToDynamaxAdventureEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return null;
            }

            var output = archive.WriteEdits(edits);
            var finalArchive = SwShDynamaxAdventureArchive.Parse(output);
            var baseBytes = TryLoadBaseDynamaxAdventureBytes(paths);
            var baseArchive = baseBytes is null
                ? null
                : SwShDynamaxAdventureArchive.Parse(baseBytes);
            var bossTargetRemap = CreateBossTargetRemap(archive, session, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return null;
            }

            return new DynamaxAdventureApplyState(
                finalArchive,
                baseArchive is not null && ArchiveRecordsEqual(finalArchive, baseArchive),
                baseBytes is null || sourceBytes.Length == baseBytes.Length,
                HasTableEdits: edits.Length > 0,
                bossTargetRemap);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change plan could not decode the source table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Dynamax Adventures table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change plan could not read the source table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable source table"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures change plan could not read the source table: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable source table"));
        }

        return null;
    }

    private static BossTargetRemap? CreateBossTargetRemap(
        SwShDynamaxAdventureArchive archive,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var edit = session.PendingEdits.SingleOrDefault(edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField);
        if (edit is null)
        {
            return null;
        }

        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var targetSpecies))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures boss target edit does not include a valid target or value.",
                field: SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
                expected: "Valid boss target remap"));
            return null;
        }

        var sourceBoss = archive.Entries.FirstOrDefault(row => row.EntryIndex == entryIndex);
        if (sourceBoss is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures boss target row {entryIndex.ToString(CultureInfo.InvariantCulture)} is not present in the source table.",
                field: SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField,
                expected: "Existing boss row"));
            return null;
        }

        return new BossTargetRemap(
            entryIndex,
            sourceBoss.Species,
            targetSpecies,
            targetSpecies == sourceBoss.Species);
    }

    private static SwShDynamaxAdventureEdit? ToDynamaxAdventureEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.Field == SwShDynamaxAdventuresWorkflowService.BossTargetSpeciesField)
        {
            return null;
        }

        if (!SwShDynamaxAdventuresWorkflowService.TryParseEncounterRecordId(edit.RecordId, out var entryIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Dynamax Adventures edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Dynamax Adventures edit"));
            return null;
        }

        return new SwShDynamaxAdventureEdit(entryIndex, field, value);
    }

    private static SwShDynamaxAdventureField? MapField(string? field)
    {
        return field switch
        {
            SwShDynamaxAdventuresWorkflowService.SpeciesField => SwShDynamaxAdventureField.Species,
            SwShDynamaxAdventuresWorkflowService.FormField => SwShDynamaxAdventureField.Form,
            SwShDynamaxAdventuresWorkflowService.LevelField => SwShDynamaxAdventureField.Level,
            SwShDynamaxAdventuresWorkflowService.BallItemIdField => SwShDynamaxAdventureField.BallItemId,
            SwShDynamaxAdventuresWorkflowService.AbilityField => SwShDynamaxAdventureField.Ability,
            SwShDynamaxAdventuresWorkflowService.GigantamaxStateField => SwShDynamaxAdventureField.GigantamaxState,
            SwShDynamaxAdventuresWorkflowService.VersionField => SwShDynamaxAdventureField.Version,
            SwShDynamaxAdventuresWorkflowService.ShinyRollField => SwShDynamaxAdventureField.ShinyRoll,
            SwShDynamaxAdventuresWorkflowService.Move0Field => SwShDynamaxAdventureField.Move0,
            SwShDynamaxAdventuresWorkflowService.Move1Field => SwShDynamaxAdventureField.Move1,
            SwShDynamaxAdventuresWorkflowService.Move2Field => SwShDynamaxAdventureField.Move2,
            SwShDynamaxAdventuresWorkflowService.Move3Field => SwShDynamaxAdventureField.Move3,
            SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField => SwShDynamaxAdventureField.GuaranteedPerfectIvs,
            SwShDynamaxAdventuresWorkflowService.IvAttackField => SwShDynamaxAdventureField.IvAttack,
            SwShDynamaxAdventuresWorkflowService.IvDefenseField => SwShDynamaxAdventureField.IvDefense,
            SwShDynamaxAdventuresWorkflowService.IvSpeedField => SwShDynamaxAdventureField.IvSpeed,
            SwShDynamaxAdventuresWorkflowService.IvSpecialAttackField => SwShDynamaxAdventureField.IvSpecialAttack,
            SwShDynamaxAdventuresWorkflowService.IvSpecialDefenseField => SwShDynamaxAdventureField.IvSpecialDefense,
            SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField => SwShDynamaxAdventureField.IsSingleCapture,
            SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField => SwShDynamaxAdventureField.IsStoryProgressGated,
            SwShDynamaxAdventuresWorkflowService.OtGenderField => SwShDynamaxAdventureField.OtGender,
            _ => null,
        };
    }

    private static bool RequiresMainPatch(IEnumerable<PendingEdit> edits)
    {
        return RequiresAdventureMirrorPatch(edits);
    }

    private static bool RequiresAdventureMirrorPatch(IEnumerable<PendingEdit> edits)
    {
        return edits.Any(edit => edit.Field is
            SwShDynamaxAdventuresWorkflowService.SpeciesField
            or SwShDynamaxAdventuresWorkflowService.FormField
            or SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            or SwShDynamaxAdventuresWorkflowService.ShinyRollField
            or SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField);
    }

    private static bool RequiresCommandValidatorPatch(IEnumerable<PendingEdit> edits)
    {
        return edits.Any(edit => edit.Field is
            SwShDynamaxAdventuresWorkflowService.SpeciesField
            or SwShDynamaxAdventuresWorkflowService.FormField
            or SwShDynamaxAdventuresWorkflowService.GigantamaxStateField);
    }

    private static bool RestoreOrDeleteMain(
        ProjectPaths paths,
        string targetPath,
        int entryCount,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var basePath = ResolveBaseSourcePath(paths, SwShExeFsPatchWorkflowService.ExeFsMainPath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures restore could not resolve base exefs/main for safe cleanup.",
                file: SwShExeFsPatchWorkflowService.ExeFsMainPath,
                expected: "Readable base ExeFS main"));
            return false;
        }

        var baseBytes = File.ReadAllBytes(basePath);
        var restored = SwShDynamaxAdventuresMainPatcher.RestoreFromBase(
            File.ReadAllBytes(targetPath),
            baseBytes,
            entryCount);
        if (restored.SequenceEqual(baseBytes))
        {
            File.Delete(targetPath);
        }
        else
        {
            File.WriteAllBytes(targetPath, restored);
        }

        return true;
    }

    private static bool ArchiveRecordsEqual(
        SwShDynamaxAdventureArchive left,
        SwShDynamaxAdventureArchive right)
    {
        if (left.Entries.Count != right.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Entries.Count; index++)
        {
            if (!RecordsEqual(left.Entries[index], right.Entries[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RecordsEqual(
        SwShDynamaxAdventureRecord left,
        SwShDynamaxAdventureRecord right)
    {
        return left.EntryIndex == right.EntryIndex
            && left.IsSingleCapture == right.IsSingleCapture
            && left.SingleCaptureFlagBlock == right.SingleCaptureFlagBlock
            && left.Field02 == right.Field02
            && left.Form == right.Form
            && left.GigantamaxState == right.GigantamaxState
            && left.BallItemId == right.BallItemId
            && left.AdventureIndex == right.AdventureIndex
            && left.Level == right.Level
            && left.Species == right.Species
            && left.UiMessageId == right.UiMessageId
            && left.OtGender == right.OtGender
            && left.Version == right.Version
            && left.ShinyRoll == right.ShinyRoll
            && left.Ivs == right.Ivs
            && left.Ability == right.Ability
            && left.IsStoryProgressGated == right.IsStoryProgressGated
            && left.Moves.SequenceEqual(right.Moves);
    }

    private static SwShDynamaxAdventureArchive? TryLoadBaseDynamaxAdventureArchive(ProjectPaths paths)
    {
        var baseBytes = TryLoadBaseDynamaxAdventureBytes(paths);
        return baseBytes is not null
            ? SwShDynamaxAdventureArchive.Parse(baseBytes)
            : null;
    }

    private static byte[]? TryLoadBaseDynamaxAdventureBytes(ProjectPaths paths)
    {
        var basePath = ResolveBaseSourcePath(paths, SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        return basePath is not null && File.Exists(basePath)
            ? File.ReadAllBytes(basePath)
            : null;
    }

    private static bool BaseSourceExists(ProjectPaths paths, string targetRelativePath)
    {
        var basePath = ResolveBaseSourcePath(paths, targetRelativePath);
        return basePath is not null && File.Exists(basePath);
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
                "Dynamax Adventures apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShDynamaxAdventuresWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (targetRelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, targetRelativePath["romfs/".Length..]);
        }

        if (targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed record WorkflowFileSource(ProjectFileGraphEntry GraphEntry, string AbsolutePath);

    private sealed record DynamaxAdventureApplyState(
        SwShDynamaxAdventureArchive FinalArchive,
        bool MatchesBaseArchive,
        bool SourceLengthMatchesBase,
        bool HasTableEdits,
        BossTargetRemap? BossTargetRemap);

    private sealed record BossTargetRemap(
        int EntryIndex,
        int FromSpecies,
        int ToSpecies,
        bool IsRestore);

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
            $"Dynamax Adventures field '{field}' is not supported by the workflow.",
            field: "field",
            expected: "Supported Dynamax Adventures Pokemon field");
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
            Domain: SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            Field: field,
            Expected: expected);
    }
}
