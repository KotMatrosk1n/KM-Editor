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
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Gifts;

public sealed class SwShGiftPokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShGiftPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShGiftPokemonWorkflowService? giftPokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SwShGiftPokemonWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShGiftPokemonEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShGiftPokemonWorkflowService? giftPokemonWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SwShGiftPokemonWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShGiftPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex,
        string field,
        string value)
    {
        return UpdateFields(
            paths,
            session,
            [new SwShGiftPokemonFieldUpdate(giftIndex, field, value)]);
    }

    public SwShGiftPokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShGiftPokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        projectWorkspaceService.ClearMemoryCache();
        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(workflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditGiftPokemon(project, workflow, diagnostics))
        {
            return new SwShGiftPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Update at least one Gift Pokemon field.",
                field: "updates",
                expected: "One or more Gift Pokemon field updates"));
            return new SwShGiftPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var workingSession = originalSession;
        var effectiveWorkflow = originalWorkflow;
        foreach (var update in updates)
        {
            if (update is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Gift Pokemon field update is missing.",
                    field: "updates",
                    expected: "Gift Pokemon field update"));
                break;
            }

            var gift = ResolveGift(effectiveWorkflow, update.GiftIndex, diagnostics, update.Field);
            var sourceGift = ResolveGift(workflow, update.GiftIndex, diagnostics, update.Field);
            if (gift is null || sourceGift is null)
            {
                break;
            }

            var pendingEdit = CreatePendingEdit(project, sourceGift, gift, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                break;
            }

            workingSession = NormalizeIvEditsBeforeUpdate(workingSession, gift.GiftIndex, pendingEdit.Field!);
            var sourceValue = GetGiftFieldValue(sourceGift, pendingEdit.Field!);
            workingSession = sourceValue == int.Parse(pendingEdit.NewValue!, CultureInfo.InvariantCulture)
                ? RemovePendingGiftField(workingSession, gift.GiftIndex, pendingEdit.Field!)
                : ReplacePendingGiftEdit(workingSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(workflow, workingSession.PendingEdits);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShGiftPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        ValidateLoadedSession(project, workflow, workingSession, diagnostics, addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShGiftPokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShGiftPokemonEditResult(
            OverlayPendingEdits(workflow, workingSession.PendingEdits),
            workingSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (CanEditGiftPokemon(project, workflow, diagnostics))
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

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        if (CanEditGiftPokemon(project, workflow, diagnostics))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);
        }

        var giftEdits = GetGiftEdits(session).ToArray();
        if (giftEdits.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Gift Pokemon edit before reviewing a change plan.",
                expected: "Pending Gift Pokemon edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var giftSource = SwShGiftPokemonWorkflowService.ResolveGiftPokemonDataSource(project);
        if (giftSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon change plan could not resolve the source table.",
                expected: SwShGiftPokemonWorkflowService.GiftPokemonDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, giftSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var sources = giftEdits
            .SelectMany(edit => GetPlanSources(project, workflow, edit))
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var write = new PlannedFileWrite(
            giftSource.GraphEntry.RelativePath,
            sources,
            File.Exists(targetPath),
            CreatePlanReason(giftEdits));

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
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Gift Pokemon change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var giftSource = SwShGiftPokemonWorkflowService.ResolveGiftPokemonDataSource(project);
        if (giftSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon apply could not resolve the source table.",
                expected: SwShGiftPokemonWorkflowService.GiftPokemonDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, giftSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        byte[] output;
        try
        {
            var archive = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(giftSource.AbsolutePath));
            var edits = GetGiftEdits(session)
                .Select(edit => ToGiftEdit(archive, edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            output = archive.WriteEdits(edits);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon source file could not be decoded or safely edited: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Sword/Shield Gift Pokemon table"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon source file could not be read: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Gift Pokemon table"));
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
                $"Gift Pokemon could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        using (var outputRollback = rollbackScope!)
        {
            try
            {
                WriteAllBytesAtomically(targetPath, output);
                writtenFiles.Add(new ProjectFileReference(
                    ProjectFileLayer.Generated,
                    giftSource.GraphEntry.RelativePath));
                outputRollback.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Gift Pokemon output file could not be written: {exception.Message}",
                    file: giftSource.GraphEntry.RelativePath,
                    expected: "Writable output root"));
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
        }

        if (writtenFiles.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Gift Pokemon change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        OpenedProject project,
        SwShGiftPokemonEntry sourceGift,
        SwShGiftPokemonEntry effectiveGift,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is null || value is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
            return null;
        }

        var normalizedField = field.Trim();
        var editableField = SwShGiftPokemonWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var sourceValue = GetGiftFieldValue(sourceGift, normalizedField);
        var parsedValue = TryParseFieldValue(editableField, value, sourceValue, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        AddLinkedPlacementWarning(normalizedField, diagnostics);
        return new PendingEdit(
            SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
            $"Set {effectiveGift.Label} {editableField.Label} to {parsedValue.Value}.",
            CreateExpectedSources(project, sourceGift, normalizedField),
            RecordId: SwShGiftPokemonWorkflowService.CreateGiftRecordId(
                sourceGift.GiftIndex,
                sourceGift.SourceIdentity),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParseFieldValue(
        SwShGiftPokemonEditableField editableField,
        string? value,
        int? sourceValue,
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

        var canonical = parsedValue.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(value, canonical, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must use canonical integer text without whitespace, a plus sign, or leading zeroes.",
                field: editableField.Field,
                expected: canonical));
            return null;
        }

        // Unsupported legacy source values may be preserved or reverted, but never newly staged.
        if (sourceValue == parsedValue)
        {
            return parsedValue;
        }

        if (editableField.Field == SwShGiftPokemonWorkflowService.FlawlessIvCountField
            && parsedValue is not 0 and not 3 and not 6)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon IV preset must be 0, 3, or 6.",
                field: editableField.Field,
                expected: "Supported IV preset"));
            return null;
        }

        if (IsIndividualIvField(editableField.Field))
        {
            var valid = parsedValue == SwShGiftPokemonArchive.RandomIvValue
                || parsedValue is >= SwShGiftPokemonArchive.MinimumFixedIvValue
                    and <= SwShGiftPokemonArchive.MaximumFixedIvValue
                || (editableField.Field == SwShGiftPokemonWorkflowService.IvHpField
                    && parsedValue == SwShGiftPokemonArchive.ThreePerfectIvSentinel);
            if (!valid)
            {
                diagnostics.Add(CreateIvDiagnostic(editableField.Field));
                return null;
            }
        }

        if (editableField.Field == SwShGiftPokemonWorkflowService.BallItemIdField
            && !SwShGiftPokemonArchive.IsValidBallItemId(parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ball item {parsedValue.ToString(CultureInfo.InvariantCulture)} is not a supported Sword/Shield Poke Ball item ID.",
                field: editableField.Field,
                expected: "0, 1-16, 492-499, 576, or 851"));
            return null;
        }

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Gift Pokemon field value"));
            return null;
        }

        return parsedValue;
    }

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShGiftPokemonWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var giftEdits = GetGiftEdits(session).ToArray();
        var effectiveWorkflow = workflow;
        var seenFields = new HashSet<(int GiftIndex, string Field)>();
        var ivRecords = new HashSet<int>();
        var semanticFields = new Dictionary<int, HashSet<string>>();
        var ivModes = new Dictionary<int, (bool HasPreset, bool HasIndividual)>();

        foreach (var edit in giftEdits)
        {
            var errorsBefore = CountErrors(diagnostics);
            var resolved = ValidatePendingEdit(project, workflow, effectiveWorkflow, edit, diagnostics);
            if (resolved is not null)
            {
                var field = edit.Field ?? string.Empty;
                if (!seenFields.Add((resolved.GiftIndex, field)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Gift Pokemon {resolved.GiftIndex} has more than one pending edit for '{field}'.",
                        field: field,
                        expected: "One pending value per Gift Pokemon field"));
                }

                if (IsIndividualIvField(field)
                    || field == SwShGiftPokemonWorkflowService.FlawlessIvCountField)
                {
                    ivRecords.Add(resolved.GiftIndex);
                    ivModes.TryGetValue(resolved.GiftIndex, out var modes);
                    ivModes[resolved.GiftIndex] = field == SwShGiftPokemonWorkflowService.FlawlessIvCountField
                        ? modes with { HasPreset = true }
                        : modes with { HasIndividual = true };
                }

                if (IsSemanticField(field))
                {
                    if (!semanticFields.TryGetValue(resolved.GiftIndex, out var fields))
                    {
                        fields = [];
                        semanticFields.Add(resolved.GiftIndex, fields);
                    }

                    fields.Add(field);
                }
            }

            if (CountErrors(diagnostics) == errorsBefore)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        foreach (var (giftIndex, modes) in ivModes)
        {
            if (modes.HasPreset && modes.HasIndividual)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Gift Pokemon {giftIndex} mixes an IV preset with individual IV edits.",
                    field: SwShGiftPokemonWorkflowService.FlawlessIvCountField,
                    expected: "Either one IV preset or individual IV values"));
            }
        }

        ValidateFinalIvValues(effectiveWorkflow, ivRecords, diagnostics);
        ValidateSemanticValues(project, effectiveWorkflow, semanticFields, diagnostics);

        if (giftEdits.Length > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightArchiveWrite(project, giftEdits, diagnostics);
        }

        if (giftEdits.Length > 0
            && addSuccessDiagnostic
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Gift Pokemon change is valid."));
        }
    }

    private static SwShGiftPokemonEntry? ValidatePendingEdit(
        OpenedProject project,
        SwShGiftPokemonWorkflow sourceWorkflow,
        SwShGiftPokemonWorkflow effectiveWorkflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = SwShGiftPokemonWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        if (!SwShGiftPokemonWorkflowService.TryParseGiftRecordId(
                edit.RecordId,
                out var giftIndex,
                out var sourceIdentity))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit targets an invalid record.",
                field: "giftIndex",
                expected: "Gift Pokemon record"));
            return null;
        }

        var sourceGift = ResolveGift(sourceWorkflow, giftIndex, diagnostics, edit.Field);
        var effectiveGift = ResolveGift(effectiveWorkflow, giftIndex, diagnostics, edit.Field);
        if (sourceGift is null || effectiveGift is null)
        {
            return null;
        }

        if (sourceIdentity is not null
            && !string.Equals(sourceIdentity, sourceGift.SourceIdentity, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Gift Pokemon source record identity changed after this edit was staged. Reload and stage the edit again.",
                field: edit.Field,
                expected: "The exact staged Gift Pokemon source record"));
            return null;
        }

        var expectedSources = CreateExpectedSources(project, sourceGift, editableField.Field);
        if (!SourcesMatchCurrent(edit.Sources, expectedSources, sourceGift, sourceIdentity is not null))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Gift Pokemon source layer or semantic lookup source changed after this edit was staged. Stage the edit again against the current source.",
                field: edit.Field,
                expected: "Pending edit staged from the current Gift Pokemon sources"));
            return null;
        }

        var sourceValue = GetGiftFieldValue(sourceGift, editableField.Field);
        var parsedValue = TryParseFieldValue(editableField, edit.NewValue, sourceValue, diagnostics);
        if (parsedValue is not null && parsedValue != sourceValue)
        {
            ValidateOptionBackedValue(sourceWorkflow, effectiveGift, editableField, parsedValue.Value, diagnostics);
        }

        AddLinkedPlacementWarning(edit.Field, diagnostics);
        return effectiveGift;
    }

    private static void ValidateOptionBackedValue(
        SwShGiftPokemonWorkflow workflow,
        SwShGiftPokemonEntry gift,
        SwShGiftPokemonEditableField editableField,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        IReadOnlyList<SwShGiftPokemonEditableFieldOption> options = editableField.Field switch
        {
            SwShGiftPokemonWorkflowService.AbilityField =>
                SwShGiftPokemonWorkflowService.CreateAbilityOptions(
                    workflow.AbilityResolver,
                    gift.SpeciesId,
                    gift.Form),
            SwShGiftPokemonWorkflowService.GenderField =>
                SwShGiftPokemonWorkflowService.CreateGenderOptions(
                    workflow.AbilityResolver,
                    gift.SpeciesId,
                    gift.Form),
            _ => workflow.EditableFields.FirstOrDefault(field =>
                string.Equals(field.Field, editableField.Field, StringComparison.Ordinal))?.Options
                ?? editableField.Options,
        };

        var requiresKnownOption = editableField.Field is
            SwShGiftPokemonWorkflowService.HeldItemIdField
            or SwShGiftPokemonWorkflowService.NatureField
            or SwShGiftPokemonWorkflowService.ShinyLockField
            or SwShGiftPokemonWorkflowService.DynamaxLevelField
            or SwShGiftPokemonWorkflowService.CanGigantamaxField
            or SwShGiftPokemonWorkflowService.SpecialMoveIdField;
        // Ability is checked against the final species/form so multi-field updates are order independent.
        if (editableField.Field is SwShGiftPokemonWorkflowService.AbilityField
            or SwShGiftPokemonWorkflowService.GenderField)
        {
            return;
        }

        if (!requiresKnownOption)
        {
            return;
        }

        var canClearWithoutLookup = value == 0
            && editableField.Field is
                SwShGiftPokemonWorkflowService.HeldItemIdField
                or SwShGiftPokemonWorkflowService.SpecialMoveIdField;
        if (options.Count == 0 && !canClearWithoutLookup)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} cannot be changed because its Sword/Shield lookup data is unavailable.",
                field: editableField.Field,
                expected: $"Loaded {editableField.Label.ToLowerInvariant()} lookup data"));
            return;
        }

        if (options.Count > 0 && !options.Any(option => option.Value == value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} value {value.ToString(CultureInfo.InvariantCulture)} is not available in the loaded Sword/Shield lookup data.",
                field: editableField.Field,
                expected: $"A listed {editableField.Label.ToLowerInvariant()} value"));
        }
    }

    private static void ValidateFinalIvValues(
        SwShGiftPokemonWorkflow workflow,
        IReadOnlySet<int> giftIndexes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var giftIndex in giftIndexes)
        {
            var gift = ResolveGift(workflow, giftIndex, diagnostics, SwShGiftPokemonWorkflowService.FlawlessIvCountField);
            if (gift is null)
            {
                continue;
            }

            var ivs = new[]
            {
                (SwShGiftPokemonWorkflowService.IvHpField, gift.Ivs.HP, true),
                (SwShGiftPokemonWorkflowService.IvAttackField, gift.Ivs.Attack, false),
                (SwShGiftPokemonWorkflowService.IvDefenseField, gift.Ivs.Defense, false),
                (SwShGiftPokemonWorkflowService.IvSpecialAttackField, gift.Ivs.SpecialAttack, false),
                (SwShGiftPokemonWorkflowService.IvSpecialDefenseField, gift.Ivs.SpecialDefense, false),
                (SwShGiftPokemonWorkflowService.IvSpeedField, gift.Ivs.Speed, false),
            };
            foreach (var (field, value, isHp) in ivs)
            {
                var valid = value == SwShGiftPokemonArchive.RandomIvValue
                    || value is >= SwShGiftPokemonArchive.MinimumFixedIvValue
                        and <= SwShGiftPokemonArchive.MaximumFixedIvValue
                    || (isHp && value == SwShGiftPokemonArchive.ThreePerfectIvSentinel);
                if (!valid)
                {
                    diagnostics.Add(CreateIvDiagnostic(field));
                }
            }

            if (gift.Ivs.HP == SwShGiftPokemonArchive.ThreePerfectIvSentinel
                && new[]
                {
                    gift.Ivs.Attack,
                    gift.Ivs.Defense,
                    gift.Ivs.SpecialAttack,
                    gift.Ivs.SpecialDefense,
                    gift.Ivs.Speed,
                }.Any(value => value != SwShGiftPokemonArchive.RandomIvValue))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{gift.Label} mixes the 3-perfect IV sentinel with individual IV values.",
                    field: SwShGiftPokemonWorkflowService.FlawlessIvCountField,
                    expected: "HP -4 with all other IVs -1, or individual IV values without the -4 sentinel"));
            }
        }
    }

    private static void ValidateSemanticValues(
        OpenedProject project,
        SwShGiftPokemonWorkflow workflow,
        IReadOnlyDictionary<int, HashSet<string>> semanticFields,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (semanticFields.Count == 0)
        {
            return;
        }

        var personalRecords = LoadPersonalRecords(project, diagnostics);
        if (personalRecords.Count == 0)
        {
            return;
        }

        foreach (var (giftIndex, fields) in semanticFields)
        {
            var gift = ResolveGift(workflow, giftIndex, diagnostics, SwShGiftPokemonWorkflowService.SpeciesField);
            if (gift is null
                || gift.SpeciesId <= 0
                || (uint)gift.SpeciesId >= (uint)personalRecords.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Gift Pokemon {giftIndex} does not target a species available in the loaded Sword/Shield personal data.",
                    field: SwShGiftPokemonWorkflowService.SpeciesField,
                    expected: "Species present in Sword/Shield personal data"));
                continue;
            }

            var basePersonal = personalRecords[gift.SpeciesId];
            var formCount = Math.Max(1, basePersonal.FormCount);
            if (gift.Form < 0 || gift.Form >= formCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{gift.Label} uses form {gift.Form}, but species {gift.SpeciesId} exposes {formCount} supported form slot(s) in personal data.",
                    field: SwShGiftPokemonWorkflowService.FormField,
                    expected: $"Form 0 through {formCount - 1}"));
                continue;
            }

            var personal = basePersonal;
            if (gift.Form > 0 && basePersonal.FormStatsIndex > 0)
            {
                var formPersonalId = basePersonal.FormStatsIndex + gift.Form - 1;
                if ((uint)formPersonalId >= (uint)personalRecords.Count)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{gift.Label} maps to a form record outside the loaded personal table.",
                        field: SwShGiftPokemonWorkflowService.FormField,
                        expected: "Mapped Sword/Shield personal form record"));
                    continue;
                }

                personal = personalRecords[formPersonalId];
            }

            if (!personal.IsPresentInGame)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{gift.Label} uses a species/form that is not marked present in Sword/Shield personal data.",
                    field: SwShGiftPokemonWorkflowService.SpeciesField,
                    expected: "Species/form present in Sword/Shield personal data"));
            }

            var identityChanged = fields.Contains(SwShGiftPokemonWorkflowService.SpeciesField)
                || fields.Contains(SwShGiftPokemonWorkflowService.FormField);
            if ((identityChanged || fields.Contains(SwShGiftPokemonWorkflowService.AbilityField))
                && !IsAvailableAbilitySlot(personal, gift.Ability))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{gift.Label} uses an ability slot unavailable for its species and form.",
                    field: SwShGiftPokemonWorkflowService.AbilityField,
                    expected: "Ability slot listed for the selected species and form"));
            }

            if ((identityChanged || fields.Contains(SwShGiftPokemonWorkflowService.GenderField))
                && !IsCompatibleGender(personal, gift.Gender))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{gift.Label} uses a gender selection unavailable for its species and form.",
                    field: SwShGiftPokemonWorkflowService.GenderField,
                    expected: "Random or a gender supported by the selected species and form"));
            }

            if ((identityChanged || fields.Contains(SwShGiftPokemonWorkflowService.CanGigantamaxField))
                && gift.CanGigantamax)
            {
                if (personal.CanNotDynamax)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{gift.Species} is marked unable to Dynamax in Sword/Shield personal data.",
                        field: SwShGiftPokemonWorkflowService.CanGigantamaxField,
                        expected: "Species/form permitted to Dynamax or Can Gigantamax disabled"));
                }
                else if (!IsGigantamaxCapableSpeciesForm(gift.SpeciesId, gift.Form))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{gift.Species} form {gift.Form} is not a Gigantamax-capable Sword/Shield species/form.",
                        field: SwShGiftPokemonWorkflowService.CanGigantamaxField,
                        expected: "Gigantamax-capable species/form or Can Gigantamax disabled"));
                }
            }
        }
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
                "Gift Pokemon species, form, ability, gender, and Gigantamax validation requires the Sword/Shield personal data table.",
                field: SwShGiftPokemonWorkflowService.SpeciesField,
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
                $"Gift Pokemon semantic validation could not read personal data: {exception.Message}",
                field: SwShGiftPokemonWorkflowService.SpeciesField,
                expected: "Readable Sword/Shield personal data table",
                file: source.GraphEntry.RelativePath));
            return [];
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

    private static bool IsCompatibleGender(SwShPersonalRecord personal, int gender)
    {
        return gender switch
        {
            0 => true,
            1 => personal.GenderRatio is not 254 and not 255,
            2 => personal.GenderRatio != 0,
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

    private static void PreflightArchiveWrite(
        OpenedProject project,
        IReadOnlyList<PendingEdit> giftEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShGiftPokemonWorkflowService.ResolveGiftPokemonDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon edit preflight could not resolve the source table.",
                expected: SwShGiftPokemonWorkflowService.GiftPokemonDataPath));
            return;
        }

        try
        {
            var archive = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = giftEdits
                .Select(edit => ToGiftEdit(archive, edit, diagnostics))
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
                $"Gift Pokemon edit cannot be encoded safely in the source FlatBuffer: {exception.Message}",
                expected: "Materialized compatible Gift Pokemon fields",
                file: source.GraphEntry.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon edit preflight could not read the source table: {exception.Message}",
                expected: "Readable Sword/Shield Gift Pokemon table",
                file: source.GraphEntry.RelativePath));
        }
    }

    private static IReadOnlyList<ProjectFileReference> CreateExpectedSources(
        OpenedProject project,
        SwShGiftPokemonEntry gift,
        string field)
    {
        var sources = new List<ProjectFileReference>
        {
            new(gift.Provenance.SourceLayer, gift.Provenance.SourceFile),
        };

        if (IsSemanticField(field))
        {
            var personalSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
            if (personalSource is not null)
            {
                sources.Add(new ProjectFileReference(
                    GetSourceLayer(personalSource.GraphEntry),
                    personalSource.GraphEntry.RelativePath));
            }
        }

        if (field is SwShGiftPokemonWorkflowService.HeldItemIdField
            or SwShGiftPokemonWorkflowService.BallItemIdField)
        {
            var itemSource = SwShItemsWorkflowService.ResolveItemDataSource(project);
            if (itemSource is not null)
            {
                sources.Add(new ProjectFileReference(
                    GetSourceLayer(itemSource.GraphEntry),
                    itemSource.GraphEntry.RelativePath));
            }
        }

        return sources.Distinct().ToArray();
    }

    private static bool SourcesMatchCurrent(
        IReadOnlyList<ProjectFileReference> stagedSources,
        IReadOnlyList<ProjectFileReference> expectedSources,
        SwShGiftPokemonEntry gift,
        bool signedRecord)
    {
        if (signedRecord)
        {
            return stagedSources.Count == expectedSources.Count
                && expectedSources.All(stagedSources.Contains);
        }

        var currentGiftSource = new ProjectFileReference(
            gift.Provenance.SourceLayer,
            gift.Provenance.SourceFile);
        return stagedSources.Contains(currentGiftSource)
            && stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    gift.Provenance.SourceFile,
                    StringComparison.OrdinalIgnoreCase))
                .All(source => source.Layer == gift.Provenance.SourceLayer)
            && expectedSources.All(expected => stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    expected.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                .All(source => source.Layer == expected.Layer));
    }

    private static IEnumerable<ProjectFileReference> GetPlanSources(
        OpenedProject project,
        SwShGiftPokemonWorkflow workflow,
        PendingEdit edit)
    {
        foreach (var source in edit.Sources)
        {
            yield return source;
        }

        if (!SwShGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || edit.Field is null)
        {
            yield break;
        }

        var gift = workflow.Gifts.SingleOrDefault(candidate => candidate.GiftIndex == giftIndex);
        if (gift is null)
        {
            yield break;
        }

        foreach (var source in CreateExpectedSources(project, gift, edit.Field))
        {
            yield return source;
        }
    }

    private static bool CanEditGiftPokemon(
        OpenedProject project,
        SwShGiftPokemonWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic =>
                     diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static SwShGiftPokemonEntry? ResolveGift(
        SwShGiftPokemonWorkflow workflow,
        int giftIndex,
        ICollection<ValidationDiagnostic> diagnostics,
        string? field,
        bool reportMissing = true)
    {
        var matches = workflow.Gifts
            .Where(candidate => candidate.GiftIndex == giftIndex)
            .ToArray();
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (reportMissing)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                matches.Length == 0
                    ? $"Gift Pokemon index {giftIndex} is not present in the loaded workflow."
                    : $"Gift Pokemon index {giftIndex} is duplicated in the loaded workflow.",
                field: field ?? "giftIndex",
                expected: "One existing Gift Pokemon record"));
        }

        return null;
    }

    private static EditSession NormalizeIvEditsBeforeUpdate(
        EditSession session,
        int giftIndex,
        string field)
    {
        if (field == SwShGiftPokemonWorkflowService.FlawlessIvCountField)
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !IsGiftEditForRecord(edit, giftIndex)
                        || !IsIndividualIvField(edit.Field ?? string.Empty))
                    .ToArray(),
            };
        }

        if (IsIndividualIvField(field))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !IsGiftEditForRecord(edit, giftIndex)
                        || edit.Field != SwShGiftPokemonWorkflowService.FlawlessIvCountField)
                    .ToArray(),
            };
        }

        return session;
    }

    private static EditSession ReplacePendingGiftEdit(EditSession session, PendingEdit pendingEdit)
    {
        if (!SwShGiftPokemonWorkflowService.TryParseGiftRecordId(pendingEdit.RecordId, out var giftIndex)
            || pendingEdit.Field is null)
        {
            return session;
        }

        return RemovePendingGiftField(session, giftIndex, pendingEdit.Field) with
        {
            PendingEdits = RemovePendingGiftField(session, giftIndex, pendingEdit.Field)
                .PendingEdits
                .Append(pendingEdit)
                .ToArray(),
        };
    }

    private static EditSession RemovePendingGiftField(EditSession session, int giftIndex, string field)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !IsGiftEditForRecord(edit, giftIndex)
                    || !string.Equals(edit.Field, field, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static bool IsGiftEditForRecord(PendingEdit edit, int giftIndex)
    {
        return IsGiftEdit(edit)
            && SwShGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var candidateIndex)
            && candidateIndex == giftIndex;
    }

    private static bool IsGiftEdit(PendingEdit edit)
    {
        return string.Equals(
            edit.Domain,
            SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
            StringComparison.Ordinal);
    }

    private static IEnumerable<PendingEdit> GetGiftEdits(EditSession session)
    {
        return session.PendingEdits.Where(IsGiftEdit);
    }

    private static bool IsIndividualIvField(string field)
    {
        return field is
            SwShGiftPokemonWorkflowService.IvHpField
            or SwShGiftPokemonWorkflowService.IvAttackField
            or SwShGiftPokemonWorkflowService.IvDefenseField
            or SwShGiftPokemonWorkflowService.IvSpeedField
            or SwShGiftPokemonWorkflowService.IvSpecialAttackField
            or SwShGiftPokemonWorkflowService.IvSpecialDefenseField;
    }

    private static bool IsSemanticField(string field)
    {
        return field is
            SwShGiftPokemonWorkflowService.SpeciesField
            or SwShGiftPokemonWorkflowService.FormField
            or SwShGiftPokemonWorkflowService.AbilityField
            or SwShGiftPokemonWorkflowService.GenderField
            or SwShGiftPokemonWorkflowService.CanGigantamaxField;
    }

    private static int? GetGiftFieldValue(SwShGiftPokemonEntry gift, string field)
    {
        return field switch
        {
            SwShGiftPokemonWorkflowService.SpeciesField => gift.SpeciesId,
            SwShGiftPokemonWorkflowService.FormField => gift.Form,
            SwShGiftPokemonWorkflowService.LevelField => gift.Level,
            SwShGiftPokemonWorkflowService.HeldItemIdField => gift.HeldItemId,
            SwShGiftPokemonWorkflowService.BallItemIdField => gift.BallItemId,
            SwShGiftPokemonWorkflowService.AbilityField => gift.Ability,
            SwShGiftPokemonWorkflowService.NatureField => gift.Nature,
            SwShGiftPokemonWorkflowService.GenderField => gift.Gender,
            SwShGiftPokemonWorkflowService.ShinyLockField => gift.ShinyLock,
            SwShGiftPokemonWorkflowService.DynamaxLevelField => gift.DynamaxLevel,
            SwShGiftPokemonWorkflowService.CanGigantamaxField => gift.CanGigantamax ? 1 : 0,
            SwShGiftPokemonWorkflowService.SpecialMoveIdField => gift.SpecialMoveId,
            SwShGiftPokemonWorkflowService.IvHpField => gift.Ivs.HP,
            SwShGiftPokemonWorkflowService.IvAttackField => gift.Ivs.Attack,
            SwShGiftPokemonWorkflowService.IvDefenseField => gift.Ivs.Defense,
            SwShGiftPokemonWorkflowService.IvSpeedField => gift.Ivs.Speed,
            SwShGiftPokemonWorkflowService.IvSpecialAttackField => gift.Ivs.SpecialAttack,
            SwShGiftPokemonWorkflowService.IvSpecialDefenseField => gift.Ivs.SpecialDefense,
            SwShGiftPokemonWorkflowService.FlawlessIvCountField => gift.FlawlessIvCount,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static SwShGiftPokemonWorkflow OverlayPendingEdits(
        SwShGiftPokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits.Where(IsGiftEdit))
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShGiftPokemonWorkflow OverlayPendingEdit(
        SwShGiftPokemonWorkflow workflow,
        PendingEdit edit)
    {
        if (!IsGiftEdit(edit)
            || !SwShGiftPokemonWorkflowService.IsEditableField(edit.Field)
            || !SwShGiftPokemonWorkflowService.TryParseGiftRecordId(
                edit.RecordId,
                out var giftIndex,
                out var sourceIdentity)
            || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || (edit.Field == SwShGiftPokemonWorkflowService.FlawlessIvCountField
                && value is not 0 and not 3 and not 6))
        {
            return workflow;
        }

        var gifts = workflow.Gifts
            .Select(gift => gift.GiftIndex == giftIndex
                && (sourceIdentity is null
                    || string.Equals(sourceIdentity, gift.SourceIdentity, StringComparison.OrdinalIgnoreCase))
                ? OverlayGiftField(workflow, gift, edit.Field!, value)
                : gift)
            .ToArray();
        return workflow with
        {
            Gifts = gifts,
            Stats = workflow.Stats with
            {
                TotalGiftCount = gifts.Length,
                EggGiftCount = gifts.Count(gift => gift.IsEgg),
                FixedIvGiftCount = gifts.Count(gift => gift.FlawlessIvCount != 0),
            },
        };
    }

    private static SwShGiftPokemonEntry OverlayGiftField(
        SwShGiftPokemonWorkflow workflow,
        SwShGiftPokemonEntry gift,
        string field,
        int value)
    {
        var updatedGift = field switch
        {
            SwShGiftPokemonWorkflowService.SpeciesField => gift with
            {
                SpeciesId = value,
                Species = GetOptionDisplayName(workflow, field, value, "Species"),
            },
            SwShGiftPokemonWorkflowService.FormField => gift with { Form = value },
            SwShGiftPokemonWorkflowService.LevelField => gift with { Level = value },
            SwShGiftPokemonWorkflowService.HeldItemIdField => gift with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionDisplayName(workflow, field, value, "Item"),
            },
            SwShGiftPokemonWorkflowService.BallItemIdField => gift with
            {
                BallItemId = value,
                BallItem = GetOptionDisplayName(workflow, field, value, "Item"),
            },
            SwShGiftPokemonWorkflowService.AbilityField => gift with { Ability = value },
            SwShGiftPokemonWorkflowService.NatureField => gift with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            SwShGiftPokemonWorkflowService.GenderField => gift with { Gender = value },
            SwShGiftPokemonWorkflowService.ShinyLockField => gift with
            {
                ShinyLock = value,
                ShinyLockLabel = GetOptionLabel(workflow, field, value, "Shiny lock"),
            },
            SwShGiftPokemonWorkflowService.DynamaxLevelField => gift with { DynamaxLevel = value },
            SwShGiftPokemonWorkflowService.CanGigantamaxField => gift with { CanGigantamax = value != 0 },
            SwShGiftPokemonWorkflowService.SpecialMoveIdField => gift with
            {
                SpecialMoveId = value,
                SpecialMove = value == 0 ? null : GetOptionDisplayName(workflow, field, value, "Move"),
            },
            SwShGiftPokemonWorkflowService.IvHpField => gift with { Ivs = gift.Ivs with { HP = value } },
            SwShGiftPokemonWorkflowService.IvAttackField => gift with { Ivs = gift.Ivs with { Attack = value } },
            SwShGiftPokemonWorkflowService.IvDefenseField => gift with { Ivs = gift.Ivs with { Defense = value } },
            SwShGiftPokemonWorkflowService.IvSpeedField => gift with { Ivs = gift.Ivs with { Speed = value } },
            SwShGiftPokemonWorkflowService.IvSpecialAttackField => gift with { Ivs = gift.Ivs with { SpecialAttack = value } },
            SwShGiftPokemonWorkflowService.IvSpecialDefenseField => gift with { Ivs = gift.Ivs with { SpecialDefense = value } },
            SwShGiftPokemonWorkflowService.FlawlessIvCountField => gift with { Ivs = CreateIvPreset(value) },
            _ => gift,
        };

        var flawlessIvCount = GetFlawlessIvCount(updatedGift.Ivs);
        var abilityOptions = SwShGiftPokemonWorkflowService.CreateAbilityOptions(
            workflow.AbilityResolver,
            updatedGift.SpeciesId,
            updatedGift.Form);
        var genderOptions = SwShGiftPokemonWorkflowService.CreateGenderOptions(
            workflow.AbilityResolver,
            updatedGift.SpeciesId,
            updatedGift.Form);
        return updatedGift with
        {
            AbilityOptions = abilityOptions,
            AbilityLabel = SwShGiftPokemonWorkflowService.GetOptionLabel(
                abilityOptions,
                updatedGift.Ability,
                "Ability slot"),
            GenderOptions = genderOptions,
            GenderLabel = SwShGiftPokemonWorkflowService.GetOptionLabel(
                genderOptions,
                updatedGift.Gender,
                "Gender"),
            FlawlessIvCount = flawlessIvCount,
            IvSummary = SwShGiftPokemonWorkflowService.FormatIvSummary(updatedGift.Ivs, flawlessIvCount),
            Label = SwShGiftPokemonWorkflowService.FormatGiftLabel(
                updatedGift.GiftIndex,
                updatedGift.Species,
                updatedGift.SpeciesId,
                updatedGift.Form,
                updatedGift.Level,
                updatedGift.IsEgg),
        };
    }

    private static SwShGiftPokemonIvsRecord CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShGiftPokemonIvsRecord(-1, -1, -1, -1, -1, -1),
            3 => new SwShGiftPokemonIvsRecord(-4, -1, -1, -1, -1, -1),
            6 => new SwShGiftPokemonIvsRecord(31, 31, 31, 31, 31, 31),
            _ => throw new ArgumentOutOfRangeException(nameof(flawlessIvCount)),
        };
    }

    private static int? GetFlawlessIvCount(SwShGiftPokemonIvsRecord ivs)
    {
        return SwShGiftPokemonArchive.GetFlawlessIvCount(
            new SwShGiftPokemonIvs(
                ivs.HP,
                ivs.Attack,
                ivs.Defense,
                ivs.Speed,
                ivs.SpecialAttack,
                ivs.SpecialDefense));
    }

    private static string GetOptionLabel(
        SwShGiftPokemonWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];
        return SwShGiftPokemonWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static string GetOptionDisplayName(
        SwShGiftPokemonWorkflow workflow,
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

    private static SwShGiftPokemonEdit? ToGiftEdit(
        SwShGiftPokemonArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShGiftPokemonWorkflowService.TryParseGiftRecordId(
                edit.RecordId,
                out var giftIndex,
                out var sourceIdentity)
            || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Gift Pokemon edit"));
            return null;
        }

        var matches = archive.Gifts.Where(gift => gift.Index == giftIndex).ToArray();
        var gift = matches.Length == 1 ? matches[0] : null;
        if (gift is null
            || (sourceIdentity is not null
                && !string.Equals(
                    sourceIdentity,
                    SwShGiftPokemonWorkflowService.CreateSourceIdentity(gift),
                    StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit no longer resolves to exactly one matching source record.",
                field: edit.Field,
                expected: "One source Gift Pokemon matching the staged index and source identity"));
            return null;
        }

        return new SwShGiftPokemonEdit(giftIndex, field, value);
    }

    private static SwShGiftPokemonField? MapField(string? field)
    {
        return field switch
        {
            SwShGiftPokemonWorkflowService.SpeciesField => SwShGiftPokemonField.Species,
            SwShGiftPokemonWorkflowService.FormField => SwShGiftPokemonField.Form,
            SwShGiftPokemonWorkflowService.LevelField => SwShGiftPokemonField.Level,
            SwShGiftPokemonWorkflowService.HeldItemIdField => SwShGiftPokemonField.HeldItem,
            SwShGiftPokemonWorkflowService.BallItemIdField => SwShGiftPokemonField.BallItemId,
            SwShGiftPokemonWorkflowService.AbilityField => SwShGiftPokemonField.Ability,
            SwShGiftPokemonWorkflowService.NatureField => SwShGiftPokemonField.Nature,
            SwShGiftPokemonWorkflowService.GenderField => SwShGiftPokemonField.Gender,
            SwShGiftPokemonWorkflowService.ShinyLockField => SwShGiftPokemonField.ShinyLock,
            SwShGiftPokemonWorkflowService.DynamaxLevelField => SwShGiftPokemonField.DynamaxLevel,
            SwShGiftPokemonWorkflowService.CanGigantamaxField => SwShGiftPokemonField.CanGigantamax,
            SwShGiftPokemonWorkflowService.SpecialMoveIdField => SwShGiftPokemonField.SpecialMove,
            SwShGiftPokemonWorkflowService.IvHpField => SwShGiftPokemonField.IvHp,
            SwShGiftPokemonWorkflowService.IvAttackField => SwShGiftPokemonField.IvAttack,
            SwShGiftPokemonWorkflowService.IvDefenseField => SwShGiftPokemonField.IvDefense,
            SwShGiftPokemonWorkflowService.IvSpeedField => SwShGiftPokemonField.IvSpeed,
            SwShGiftPokemonWorkflowService.IvSpecialAttackField => SwShGiftPokemonField.IvSpecialAttack,
            SwShGiftPokemonWorkflowService.IvSpecialDefenseField => SwShGiftPokemonField.IvSpecialDefense,
            SwShGiftPokemonWorkflowService.FlawlessIvCountField => SwShGiftPokemonField.FlawlessIvCount,
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
                "Gift Pokemon apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
                paths,
                out var stablePaths,
                out var stableRootFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                stableRootFailure ?? "Configured output root could not be resolved safely.",
                file: targetRelativePath,
                expected: "Stable output root"));
            return null;
        }

        var targetPath = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            stablePaths.OutputRootPath,
            targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private void WriteAllBytesAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Gift Pokemon output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Gift Pokemon output target directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(temporaryPath, contents);
            if (!File.Exists(temporaryPath)
                || !File.ReadAllBytes(temporaryPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Gift Pokemon temporary output verification failed.");
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A cleanup failure does not replace the verified target.
            }
        }
    }

    private static void RollbackFailedApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> writtenFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rollbackFailures = rollbackScope.Rollback();
        writtenFiles.Clear();
        if (rollbackFailures.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Gift Pokemon apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private static string CreatePlanReason(IReadOnlyList<PendingEdit> pendingEdits)
    {
        var fingerprint = ComputePendingEditFingerprint(pendingEdits);
        var summary = pendingEdits.Count == 1
            ? $"Apply pending Gift Pokemon edit: {pendingEdits[0].Summary}"
            : $"Apply {pendingEdits.Count} pending Gift Pokemon edits.";
        return $"{summary} Fingerprint {fingerprint}.";
    }

    private static string ComputePendingEditFingerprint(IReadOnlyList<PendingEdit> edits)
    {
        var canonical = new StringBuilder();
        foreach (var edit in edits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                     .ThenBy(edit => edit.NewValue, StringComparer.Ordinal))
        {
            AppendFingerprintComponent(canonical, edit.Domain);
            AppendFingerprintComponent(canonical, edit.RecordId);
            AppendFingerprintComponent(canonical, edit.Field);
            AppendFingerprintComponent(canonical, edit.NewValue);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintComponent(canonical, ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintComponent(canonical, source.RelativePath);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendFingerprintComponent(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length ?? -1);
        destination.Append(':');
        destination.Append(value);
        destination.Append('|');
    }

    private static void AddLinkedPlacementWarning(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShGiftPokemonWorkflowService.SpeciesField
            or SwShGiftPokemonWorkflowService.FormField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Species and form edits update the gift table only; some visible overworld placements may need a separate placement review.",
                field: field,
                expected: "Review linked placement assets when changing visible gift Pokemon"));
        }
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

    private static int CountErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static ValidationDiagnostic CreateIvDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Gift Pokemon IV values must be -1 for random or 0-31 for fixed values; HP IV also accepts -4 for the 3-perfect sentinel.",
            field: field,
            expected: "Supported Gift Pokemon IV value");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Gift Pokemon field '{field}' is not supported by the workflow yet.",
            field: "field",
            expected: "Supported Gift Pokemon field");
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
            Domain: SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
            Field: field,
            Expected: expected);
    }
}
