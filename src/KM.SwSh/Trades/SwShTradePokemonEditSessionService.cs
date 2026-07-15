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

namespace KM.SwSh.Trades;

public sealed class SwShTradePokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShTradePokemonWorkflowService tradePokemonWorkflowService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShTradePokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTradePokemonWorkflowService? tradePokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.tradePokemonWorkflowService = tradePokemonWorkflowService ?? new SwShTradePokemonWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShTradePokemonEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTradePokemonWorkflowService? tradePokemonWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.tradePokemonWorkflowService = tradePokemonWorkflowService ?? new SwShTradePokemonWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShTradePokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int tradeIndex,
        string field,
        string value)
    {
        return UpdateFields(
            paths,
            session,
            [new SwShTradePokemonFieldUpdate(tradeIndex, field, value)]);
    }

    public SwShTradePokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShTradePokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        projectWorkspaceService.ClearMemoryCache();
        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = tradePokemonWorkflowService.Load(project);
        var originalWorkflow = OverlayPendingEdits(workflow, originalSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditTradePokemon(project, workflow, diagnostics))
        {
            return new SwShTradePokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Update at least one Trade Pokemon field.",
                field: "updates",
                expected: "One or more Trade Pokemon field updates"));
            return new SwShTradePokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var workingSession = originalSession;
        var effectiveWorkflow = originalWorkflow;
        foreach (var update in updates)
        {
            if (update is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trade Pokemon field update is missing.",
                    field: "updates",
                    expected: "Trade Pokemon field update"));
                break;
            }

            var trade = ResolveTrade(effectiveWorkflow, update.TradeIndex, diagnostics, update.Field);
            var sourceTrade = ResolveTrade(workflow, update.TradeIndex, diagnostics, update.Field);
            if (trade is null || sourceTrade is null)
            {
                break;
            }

            var pendingEdit = CreatePendingEdit(
                project,
                sourceTrade,
                trade,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                break;
            }

            workingSession = NormalizeIvEditsBeforeUpdate(workingSession, trade.TradeIndex, pendingEdit.Field!);
            var sourceValue = GetTradeFieldValue(sourceTrade, pendingEdit.Field!);
            workingSession = sourceValue == int.Parse(pendingEdit.NewValue!, CultureInfo.InvariantCulture)
                ? RemovePendingTradeField(workingSession, trade.TradeIndex, pendingEdit.Field!)
                : ReplacePendingTradeEdit(workingSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdits(workflow, workingSession.PendingEdits);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShTradePokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        ValidateLoadedSession(project, workflow, workingSession, diagnostics, addSuccessDiagnostic: false);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShTradePokemonEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShTradePokemonEditResult(
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
        var workflow = tradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (CanEditTradePokemon(project, workflow, diagnostics))
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
        var workflow = tradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        if (CanEditTradePokemon(project, workflow, diagnostics))
        {
            ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);
        }

        var tradeEdits = GetTradeEdits(session).ToArray();
        if (tradeEdits.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Trade Pokemon edit before reviewing a change plan.",
                expected: "Pending Trade Pokemon edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var tradeSource = SwShTradePokemonWorkflowService.ResolveTradePokemonDataSource(project);
        if (tradeSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon change plan could not resolve the source table.",
                expected: SwShTradePokemonWorkflowService.TradePokemonDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, tradeSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var sources = tradeEdits
            .SelectMany(edit => GetPlanSources(project, workflow, edit))
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var write = new PlannedFileWrite(
            tradeSource.GraphEntry.RelativePath,
            sources,
            File.Exists(targetPath),
            CreatePlanReason(tradeEdits));

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
                expected: "Current reviewed Trade Pokemon change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var tradeSource = SwShTradePokemonWorkflowService.ResolveTradePokemonDataSource(project);
        if (tradeSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon apply could not resolve the source table.",
                expected: SwShTradePokemonWorkflowService.TradePokemonDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, tradeSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        byte[] output;
        try
        {
            var archive = SwShTradePokemonArchive.Parse(File.ReadAllBytes(tradeSource.AbsolutePath));
            var edits = GetTradeEdits(session)
                .Select(edit => ToTradeEdit(archive, edit, diagnostics))
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
                $"Trade Pokemon source file could not be decoded or safely edited: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Sword/Shield Trade Pokemon table"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon source file could not be read: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield Trade Pokemon table"));
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
                $"Trade Pokemon could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
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
                    tradeSource.GraphEntry.RelativePath));
                outputRollback.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trade Pokemon output file could not be written: {exception.Message}",
                    file: tradeSource.GraphEntry.RelativePath,
                    expected: "Writable output root"));
                RollbackFailedApply(outputRollback, writtenFiles, diagnostics);
            }
        }

        if (writtenFiles.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Trade Pokemon change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        OpenedProject project,
        SwShTradePokemonEntry sourceTrade,
        SwShTradePokemonEntry effectiveTrade,
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
        var editableField = SwShTradePokemonWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var sourceValue = GetTradeFieldValue(sourceTrade, normalizedField);
        var parsedValue = TryParseFieldValue(editableField, value, sourceValue, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        AddLinkedPlacementWarning(normalizedField, diagnostics);

        return new PendingEdit(
            SwShTradePokemonWorkflowService.TradePokemonEditDomain,
            $"Set {effectiveTrade.Label} {editableField.Label} to {parsedValue.Value}.",
            CreateExpectedSources(project, sourceTrade, normalizedField),
            RecordId: SwShTradePokemonWorkflowService.CreateTradeRecordId(
                sourceTrade.TradeIndex,
                sourceTrade.SourceIdentity),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParseFieldValue(
        SwShTradePokemonEditableField editableField,
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

        // Preserve and permit reversion to unsupported legacy source values, but never stage new ones.
        if (sourceValue == parsedValue)
        {
            return parsedValue;
        }

        if (editableField.Field == SwShTradePokemonWorkflowService.FlawlessIvCountField
            && parsedValue is not 0 and not 3 and not 6)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon IV preset must be 0, 3, or 6.",
                field: editableField.Field,
                expected: "Supported IV preset"));
            return null;
        }

        if (IsIndividualIvField(editableField.Field))
        {
            var valid = parsedValue == SwShTradePokemonArchive.RandomIvValue
                || parsedValue is >= SwShTradePokemonArchive.MinimumFixedIvValue
                    and <= SwShTradePokemonArchive.MaximumFixedIvValue
                || (editableField.Field == SwShTradePokemonWorkflowService.IvHpField
                    && parsedValue == SwShTradePokemonArchive.ThreePerfectIvSentinel);
            if (!valid)
            {
                diagnostics.Add(CreateIvDiagnostic(editableField.Field));
                return null;
            }
        }

        if (editableField.Field == SwShTradePokemonWorkflowService.BallItemIdField
            && !SwShTradePokemonArchive.IsValidBallItemId(parsedValue))
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
                expected: "Supported Trade Pokemon field value"));
            return null;
        }

        return parsedValue;
    }

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShTradePokemonWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var tradeEdits = GetTradeEdits(session).ToArray();
        var effectiveWorkflow = workflow;
        var seenFields = new HashSet<(int TradeIndex, string Field)>();
        var ivRecords = new HashSet<int>();
        var semanticFields = new Dictionary<int, HashSet<string>>();
        var ivModes = new Dictionary<int, (bool HasPreset, bool HasIndividual)>();

        foreach (var edit in tradeEdits)
        {
            var errorsBefore = CountErrors(diagnostics);
            var resolved = ValidatePendingEdit(project, workflow, effectiveWorkflow, edit, diagnostics);
            if (resolved is not null)
            {
                var field = edit.Field ?? string.Empty;
                if (!seenFields.Add((resolved.TradeIndex, field)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Trade Pokemon {resolved.TradeIndex} has more than one pending edit for '{field}'.",
                        field: field,
                        expected: "One pending value per Trade Pokemon field"));
                }

                if (IsIndividualIvField(field)
                    || field == SwShTradePokemonWorkflowService.FlawlessIvCountField)
                {
                    ivRecords.Add(resolved.TradeIndex);
                    ivModes.TryGetValue(resolved.TradeIndex, out var modes);
                    ivModes[resolved.TradeIndex] = field == SwShTradePokemonWorkflowService.FlawlessIvCountField
                        ? modes with { HasPreset = true }
                        : modes with { HasIndividual = true };
                }

                if (IsSemanticField(field))
                {
                    if (!semanticFields.TryGetValue(resolved.TradeIndex, out var fields))
                    {
                        fields = [];
                        semanticFields.Add(resolved.TradeIndex, fields);
                    }

                    fields.Add(field);
                }
            }

            if (CountErrors(diagnostics) == errorsBefore)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        foreach (var (tradeIndex, modes) in ivModes)
        {
            if (modes.HasPreset && modes.HasIndividual)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trade Pokemon {tradeIndex} mixes an IV preset with individual IV edits.",
                    field: SwShTradePokemonWorkflowService.FlawlessIvCountField,
                    expected: "Either one IV preset or individual IV values"));
            }
        }

        ValidateFinalIvValues(effectiveWorkflow, ivRecords, diagnostics);
        ValidateSemanticValues(project, effectiveWorkflow, semanticFields, diagnostics);

        if (tradeEdits.Length > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            PreflightArchiveWrite(project, tradeEdits, diagnostics);
        }

        if (tradeEdits.Length > 0
            && addSuccessDiagnostic
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Trade Pokemon change is valid."));
        }
    }

    private static SwShTradePokemonEntry? ValidatePendingEdit(
        OpenedProject project,
        SwShTradePokemonWorkflow sourceWorkflow,
        SwShTradePokemonWorkflow effectiveWorkflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = SwShTradePokemonWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        if (!SwShTradePokemonWorkflowService.TryParseTradeRecordId(
                edit.RecordId,
                out var tradeIndex,
                out var sourceIdentity))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit targets an invalid record.",
                field: "tradeIndex",
                expected: "Trade Pokemon record"));
            return null;
        }

        var sourceTrade = ResolveTrade(sourceWorkflow, tradeIndex, diagnostics, edit.Field);
        var effectiveTrade = ResolveTrade(effectiveWorkflow, tradeIndex, diagnostics, edit.Field);
        if (sourceTrade is null || effectiveTrade is null)
        {
            return null;
        }

        if (sourceIdentity is not null
            && !string.Equals(sourceIdentity, sourceTrade.SourceIdentity, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Trade Pokemon source record identity changed after this edit was staged. Reload and stage the edit again.",
                field: edit.Field,
                expected: "The exact staged Trade Pokemon source record"));
            return null;
        }

        var expectedSources = CreateExpectedSources(project, sourceTrade, editableField.Field);
        if (!SourcesMatchCurrent(edit.Sources, expectedSources, sourceTrade, sourceIdentity is not null))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Trade Pokemon source layer or semantic lookup source changed after this edit was staged. Stage the edit again against the current source.",
                field: edit.Field,
                expected: "Pending edit staged from the current Trade Pokemon sources"));
            return null;
        }

        var sourceValue = GetTradeFieldValue(sourceTrade, editableField.Field);
        var parsedValue = TryParseFieldValue(editableField, edit.NewValue, sourceValue, diagnostics);
        if (parsedValue is not null && parsedValue != sourceValue)
        {
            ValidateOptionBackedValue(sourceWorkflow, effectiveTrade, editableField, parsedValue.Value, diagnostics);
        }

        AddLinkedPlacementWarning(edit.Field, diagnostics);
        return effectiveTrade;
    }

    private static void ValidateOptionBackedValue(
        SwShTradePokemonWorkflow workflow,
        SwShTradePokemonEntry trade,
        SwShTradePokemonEditableField editableField,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        IReadOnlyList<SwShTradePokemonEditableFieldOption> options = editableField.Field switch
        {
            SwShTradePokemonWorkflowService.AbilityField =>
                SwShTradePokemonWorkflowService.CreateAbilityOptions(
                    workflow.AbilityResolver,
                    trade.SpeciesId,
                    trade.Form),
            SwShTradePokemonWorkflowService.GenderField =>
                SwShTradePokemonWorkflowService.CreateGenderOptions(
                    workflow.AbilityResolver,
                    trade.SpeciesId,
                    trade.Form),
            _ => workflow.EditableFields.FirstOrDefault(field =>
                string.Equals(field.Field, editableField.Field, StringComparison.Ordinal))?.Options
                ?? editableField.Options,
        };

        var requiresKnownOption = editableField.Field is
            SwShTradePokemonWorkflowService.HeldItemIdField
            or SwShTradePokemonWorkflowService.BallItemIdField
            or SwShTradePokemonWorkflowService.NatureField
            or SwShTradePokemonWorkflowService.ShinyLockField
            or SwShTradePokemonWorkflowService.DynamaxLevelField
            or SwShTradePokemonWorkflowService.CanGigantamaxField
            or SwShTradePokemonWorkflowService.RequiredNatureField
            or SwShTradePokemonWorkflowService.OtGenderField
            or SwShTradePokemonWorkflowService.RelearnMove0Field
            or SwShTradePokemonWorkflowService.RelearnMove1Field
            or SwShTradePokemonWorkflowService.RelearnMove2Field
            or SwShTradePokemonWorkflowService.RelearnMove3Field;
        // Ability and gender are checked against the final species/form so batches are order independent.
        if (editableField.Field is SwShTradePokemonWorkflowService.AbilityField
            or SwShTradePokemonWorkflowService.GenderField)
        {
            return;
        }

        if (!requiresKnownOption)
        {
            return;
        }

        var canClearWithoutLookup = value == 0
            && editableField.Field is
                SwShTradePokemonWorkflowService.HeldItemIdField
                or SwShTradePokemonWorkflowService.BallItemIdField
                or SwShTradePokemonWorkflowService.RelearnMove0Field
                or SwShTradePokemonWorkflowService.RelearnMove1Field
                or SwShTradePokemonWorkflowService.RelearnMove2Field
                or SwShTradePokemonWorkflowService.RelearnMove3Field;
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
        SwShTradePokemonWorkflow workflow,
        IReadOnlySet<int> tradeIndexes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var tradeIndex in tradeIndexes)
        {
            var trade = ResolveTrade(
                workflow,
                tradeIndex,
                diagnostics,
                SwShTradePokemonWorkflowService.FlawlessIvCountField);
            if (trade is null)
            {
                continue;
            }

            var ivs = new[]
            {
                (SwShTradePokemonWorkflowService.IvHpField, trade.Ivs.HP, true),
                (SwShTradePokemonWorkflowService.IvAttackField, trade.Ivs.Attack, false),
                (SwShTradePokemonWorkflowService.IvDefenseField, trade.Ivs.Defense, false),
                (SwShTradePokemonWorkflowService.IvSpecialAttackField, trade.Ivs.SpecialAttack, false),
                (SwShTradePokemonWorkflowService.IvSpecialDefenseField, trade.Ivs.SpecialDefense, false),
                (SwShTradePokemonWorkflowService.IvSpeedField, trade.Ivs.Speed, false),
            };
            foreach (var (field, value, isHp) in ivs)
            {
                var valid = value == SwShTradePokemonArchive.RandomIvValue
                    || value is >= SwShTradePokemonArchive.MinimumFixedIvValue
                        and <= SwShTradePokemonArchive.MaximumFixedIvValue
                    || (isHp && value == SwShTradePokemonArchive.ThreePerfectIvSentinel);
                if (!valid)
                {
                    diagnostics.Add(CreateIvDiagnostic(field));
                }
            }

            if (trade.Ivs.HP == SwShTradePokemonArchive.ThreePerfectIvSentinel
                && new[]
                {
                    trade.Ivs.Attack,
                    trade.Ivs.Defense,
                    trade.Ivs.SpecialAttack,
                    trade.Ivs.SpecialDefense,
                    trade.Ivs.Speed,
                }.Any(value => value != SwShTradePokemonArchive.RandomIvValue))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{trade.Label} mixes the 3-perfect IV sentinel with individual IV values.",
                    field: SwShTradePokemonWorkflowService.FlawlessIvCountField,
                    expected: "HP -4 with all other IVs -1, or individual IV values without the -4 sentinel"));
            }
        }
    }

    private static void ValidateSemanticValues(
        OpenedProject project,
        SwShTradePokemonWorkflow workflow,
        IReadOnlyDictionary<int, HashSet<string>> semanticFields,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (semanticFields.Count == 0)
        {
            return;
        }

        IReadOnlyList<SwShPersonalRecord>? personalRecords = null;

        foreach (var (tradeIndex, fields) in semanticFields)
        {
            var trade = ResolveTrade(workflow, tradeIndex, diagnostics, SwShTradePokemonWorkflowService.SpeciesField);
            if (trade is null)
            {
                continue;
            }

            var offeredFieldsChanged = fields.Any(field => field is
                SwShTradePokemonWorkflowService.SpeciesField
                or SwShTradePokemonWorkflowService.FormField
                or SwShTradePokemonWorkflowService.AbilityField
                or SwShTradePokemonWorkflowService.GenderField
                or SwShTradePokemonWorkflowService.CanGigantamaxField);
            if (offeredFieldsChanged)
            {
                personalRecords ??= LoadPersonalRecords(project, diagnostics);
                if (personalRecords.Count == 0)
                {
                    continue;
                }

                var personal = ResolvePresentPersonalRecord(
                    personalRecords,
                    trade.SpeciesId,
                    trade.Form,
                    trade.Label,
                    SwShTradePokemonWorkflowService.SpeciesField,
                    SwShTradePokemonWorkflowService.FormField,
                    diagnostics);
                if (personal is not null)
                {
                    var identityChanged = fields.Contains(SwShTradePokemonWorkflowService.SpeciesField)
                        || fields.Contains(SwShTradePokemonWorkflowService.FormField);
                    if ((identityChanged || fields.Contains(SwShTradePokemonWorkflowService.AbilityField))
                        && !IsAvailableAbilitySlot(personal, trade.Ability))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"{trade.Label} uses an ability slot unavailable for its species and form.",
                            field: SwShTradePokemonWorkflowService.AbilityField,
                            expected: "Ability slot listed for the selected species and form"));
                    }

                    if ((identityChanged || fields.Contains(SwShTradePokemonWorkflowService.GenderField))
                        && !IsCompatibleGender(personal, trade.Gender))
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"{trade.Label} uses a gender selection unavailable for its species and form.",
                            field: SwShTradePokemonWorkflowService.GenderField,
                            expected: "Random or a gender supported by the selected species and form"));
                    }

                    if ((identityChanged || fields.Contains(SwShTradePokemonWorkflowService.CanGigantamaxField))
                        && trade.CanGigantamax)
                    {
                        if (personal.CanNotDynamax)
                        {
                            diagnostics.Add(CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                $"{trade.Species} is marked unable to Dynamax in Sword/Shield personal data.",
                                field: SwShTradePokemonWorkflowService.CanGigantamaxField,
                                expected: "Species/form permitted to Dynamax or Can Gigantamax disabled"));
                        }
                        else if (!IsGigantamaxCapableSpeciesForm(trade.SpeciesId, trade.Form))
                        {
                            diagnostics.Add(CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                $"{trade.Species} form {trade.Form} is not a Gigantamax-capable Sword/Shield species/form.",
                                field: SwShTradePokemonWorkflowService.CanGigantamaxField,
                                expected: "Gigantamax-capable species/form or Can Gigantamax disabled"));
                        }
                    }
                }
            }

            if (fields.Contains(SwShTradePokemonWorkflowService.RequiredSpeciesField)
                || fields.Contains(SwShTradePokemonWorkflowService.RequiredFormField))
            {
                if (trade.RequiredSpeciesId == 0)
                {
                    if (trade.RequiredForm != 0)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"{trade.Label} uses requested form {trade.RequiredForm} without a requested species.",
                            field: SwShTradePokemonWorkflowService.RequiredFormField,
                            expected: "Requested form 0 when requested species is None"));
                    }
                }
                else
                {
                    personalRecords ??= LoadPersonalRecords(project, diagnostics);
                    if (personalRecords.Count == 0)
                    {
                        continue;
                    }

                    _ = ResolvePresentPersonalRecord(
                        personalRecords,
                        trade.RequiredSpeciesId,
                        trade.RequiredForm,
                        $"{trade.Label} requested Pokemon",
                        SwShTradePokemonWorkflowService.RequiredSpeciesField,
                        SwShTradePokemonWorkflowService.RequiredFormField,
                        diagnostics);
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
                "Trade Pokemon species, form, ability, gender, and Gigantamax validation requires the Sword/Shield personal data table.",
                field: SwShTradePokemonWorkflowService.SpeciesField,
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
                $"Trade Pokemon semantic validation could not read personal data: {exception.Message}",
                field: SwShTradePokemonWorkflowService.SpeciesField,
                expected: "Readable Sword/Shield personal data table",
                file: source.GraphEntry.RelativePath));
            return [];
        }
    }

    private static SwShPersonalRecord? ResolvePresentPersonalRecord(
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        int speciesId,
        int form,
        string label,
        string speciesField,
        string formField,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (speciesId <= 0 || (uint)speciesId >= (uint)personalRecords.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} does not target a species available in the loaded Sword/Shield personal data.",
                field: speciesField,
                expected: "Species present in Sword/Shield personal data"));
            return null;
        }

        var basePersonal = personalRecords[speciesId];
        var formCount = Math.Max(1, basePersonal.FormCount);
        if (form < 0 || form >= formCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} uses form {form}, but species {speciesId} exposes {formCount} supported form slot(s) in personal data.",
                field: formField,
                expected: $"Form 0 through {formCount - 1}"));
            return null;
        }

        var personal = basePersonal;
        if (form > 0 && basePersonal.FormStatsIndex > 0)
        {
            var formPersonalId = basePersonal.FormStatsIndex + form - 1;
            if ((uint)formPersonalId >= (uint)personalRecords.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{label} maps to a form record outside the loaded personal table.",
                    field: formField,
                    expected: "Mapped Sword/Shield personal form record"));
                return null;
            }

            personal = personalRecords[formPersonalId];
        }

        if (!personal.IsPresentInGame)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{label} uses a species/form that is not marked present in Sword/Shield personal data.",
                field: speciesField,
                expected: "Species/form present in Sword/Shield personal data"));
            return null;
        }

        return personal;
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
        IReadOnlyList<PendingEdit> tradeEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShTradePokemonWorkflowService.ResolveTradePokemonDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon edit preflight could not resolve the source table.",
                expected: SwShTradePokemonWorkflowService.TradePokemonDataPath));
            return;
        }

        try
        {
            var archive = SwShTradePokemonArchive.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = tradeEdits
                .Select(edit => ToTradeEdit(archive, edit, diagnostics))
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
                $"Trade Pokemon edits cannot be safely encoded: {exception.Message}",
                expected: "Encodable Sword/Shield Trade Pokemon edits",
                file: source.GraphEntry.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon edit preflight could not read the source table: {exception.Message}",
                expected: "Readable Sword/Shield Trade Pokemon table",
                file: source.GraphEntry.RelativePath));
        }
    }

    private static IReadOnlyList<ProjectFileReference> CreateExpectedSources(
        OpenedProject project,
        SwShTradePokemonEntry trade,
        string field)
    {
        var sources = new List<ProjectFileReference>
        {
            new(trade.Provenance.SourceLayer, trade.Provenance.SourceFile),
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

        if (field is SwShTradePokemonWorkflowService.HeldItemIdField
            or SwShTradePokemonWorkflowService.BallItemIdField)
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
        SwShTradePokemonEntry trade,
        bool signedRecord)
    {
        if (signedRecord)
        {
            return stagedSources.Count == expectedSources.Count
                && expectedSources.All(stagedSources.Contains);
        }

        var currentTradeSource = new ProjectFileReference(
            trade.Provenance.SourceLayer,
            trade.Provenance.SourceFile);
        return stagedSources.Contains(currentTradeSource)
            && stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    trade.Provenance.SourceFile,
                    StringComparison.OrdinalIgnoreCase))
                .All(source => source.Layer == trade.Provenance.SourceLayer)
            && expectedSources.All(expected => stagedSources
                .Where(source => string.Equals(
                    source.RelativePath,
                    expected.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                .All(source => source.Layer == expected.Layer));
    }

    private static IEnumerable<ProjectFileReference> GetPlanSources(
        OpenedProject project,
        SwShTradePokemonWorkflow workflow,
        PendingEdit edit)
    {
        foreach (var source in edit.Sources)
        {
            yield return source;
        }

        if (!SwShTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || edit.Field is null)
        {
            yield break;
        }

        var trade = workflow.Trades.SingleOrDefault(candidate => candidate.TradeIndex == tradeIndex);
        if (trade is null)
        {
            yield break;
        }

        foreach (var source in CreateExpectedSources(project, trade, edit.Field))
        {
            yield return source;
        }
    }

    private static int CountErrors(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static bool IsIndividualIvField(string field)
    {
        return field is
            SwShTradePokemonWorkflowService.IvHpField
            or
            SwShTradePokemonWorkflowService.IvAttackField
            or SwShTradePokemonWorkflowService.IvDefenseField
            or SwShTradePokemonWorkflowService.IvSpeedField
            or SwShTradePokemonWorkflowService.IvSpecialAttackField
            or SwShTradePokemonWorkflowService.IvSpecialDefenseField;
    }

    private static bool IsSemanticField(string field)
    {
        return field is
            SwShTradePokemonWorkflowService.SpeciesField
            or SwShTradePokemonWorkflowService.FormField
            or SwShTradePokemonWorkflowService.AbilityField
            or SwShTradePokemonWorkflowService.GenderField
            or SwShTradePokemonWorkflowService.CanGigantamaxField
            or SwShTradePokemonWorkflowService.RequiredSpeciesField
            or SwShTradePokemonWorkflowService.RequiredFormField;
    }

    private static int? GetTradeFieldValue(SwShTradePokemonEntry trade, string field)
    {
        return field switch
        {
            SwShTradePokemonWorkflowService.SpeciesField => trade.SpeciesId,
            SwShTradePokemonWorkflowService.FormField => trade.Form,
            SwShTradePokemonWorkflowService.LevelField => trade.Level,
            SwShTradePokemonWorkflowService.HeldItemIdField => trade.HeldItemId,
            SwShTradePokemonWorkflowService.BallItemIdField => trade.BallItemId,
            SwShTradePokemonWorkflowService.Field03Field => trade.Field03,
            SwShTradePokemonWorkflowService.AbilityField => trade.Ability,
            SwShTradePokemonWorkflowService.NatureField => trade.Nature,
            SwShTradePokemonWorkflowService.GenderField => trade.Gender,
            SwShTradePokemonWorkflowService.ShinyLockField => trade.ShinyLock,
            SwShTradePokemonWorkflowService.DynamaxLevelField => trade.DynamaxLevel,
            SwShTradePokemonWorkflowService.CanGigantamaxField => trade.CanGigantamax ? 1 : 0,
            SwShTradePokemonWorkflowService.RequiredSpeciesField => trade.RequiredSpeciesId,
            SwShTradePokemonWorkflowService.RequiredFormField => trade.RequiredForm,
            SwShTradePokemonWorkflowService.RequiredNatureField => trade.RequiredNature,
            SwShTradePokemonWorkflowService.UnknownRequirementField => trade.UnknownRequirement,
            SwShTradePokemonWorkflowService.TrainerIdField => trade.TrainerId,
            SwShTradePokemonWorkflowService.OtGenderField => trade.OtGender,
            SwShTradePokemonWorkflowService.MemoryCodeField => trade.MemoryCode,
            SwShTradePokemonWorkflowService.MemoryTextVariableField => trade.MemoryTextVariable,
            SwShTradePokemonWorkflowService.MemoryFeelField => trade.MemoryFeel,
            SwShTradePokemonWorkflowService.MemoryIntensityField => trade.MemoryIntensity,
            SwShTradePokemonWorkflowService.RelearnMove0Field => trade.RelearnMoves.Single(move => move.Slot == 0).MoveId,
            SwShTradePokemonWorkflowService.RelearnMove1Field => trade.RelearnMoves.Single(move => move.Slot == 1).MoveId,
            SwShTradePokemonWorkflowService.RelearnMove2Field => trade.RelearnMoves.Single(move => move.Slot == 2).MoveId,
            SwShTradePokemonWorkflowService.RelearnMove3Field => trade.RelearnMoves.Single(move => move.Slot == 3).MoveId,
            SwShTradePokemonWorkflowService.IvHpField => trade.Ivs.HP,
            SwShTradePokemonWorkflowService.IvAttackField => trade.Ivs.Attack,
            SwShTradePokemonWorkflowService.IvDefenseField => trade.Ivs.Defense,
            SwShTradePokemonWorkflowService.IvSpeedField => trade.Ivs.Speed,
            SwShTradePokemonWorkflowService.IvSpecialAttackField => trade.Ivs.SpecialAttack,
            SwShTradePokemonWorkflowService.IvSpecialDefenseField => trade.Ivs.SpecialDefense,
            SwShTradePokemonWorkflowService.FlawlessIvCountField => trade.FlawlessIvCount,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static void AddLinkedPlacementWarning(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShTradePokemonWorkflowService.SpeciesField
            or SwShTradePokemonWorkflowService.FormField
            or SwShTradePokemonWorkflowService.RequiredSpeciesField
            or SwShTradePokemonWorkflowService.RequiredFormField
            or SwShTradePokemonWorkflowService.RequiredNatureField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Trade species, form, and requested-Pokemon edits update the trade table only; dialogue and visible placement references may need separate review.",
                field: field,
                expected: "Review linked dialogue and placement assets when changing Trade Pokemon"));
        }
    }

    private static bool CanEditTradePokemon(
        OpenedProject project,
        SwShTradePokemonWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static EditSession NormalizeIvEditsBeforeUpdate(
        EditSession session,
        int tradeIndex,
        string field)
    {
        if (field == SwShTradePokemonWorkflowService.FlawlessIvCountField)
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !IsTradeEditForRecord(edit, tradeIndex)
                        || !IsIndividualIvField(edit.Field ?? string.Empty))
                    .ToArray(),
            };
        }

        if (IsIndividualIvField(field))
        {
            return session with
            {
                PendingEdits = session.PendingEdits
                    .Where(edit => !IsTradeEditForRecord(edit, tradeIndex)
                        || edit.Field != SwShTradePokemonWorkflowService.FlawlessIvCountField)
                    .ToArray(),
            };
        }

        return session;
    }

    private static EditSession ReplacePendingTradeEdit(EditSession session, PendingEdit pendingEdit)
    {
        if (!SwShTradePokemonWorkflowService.TryParseTradeRecordId(pendingEdit.RecordId, out var tradeIndex)
            || pendingEdit.Field is null)
        {
            return session;
        }

        var withoutExisting = RemovePendingTradeField(session, tradeIndex, pendingEdit.Field);
        return withoutExisting with
        {
            PendingEdits = withoutExisting.PendingEdits.Append(pendingEdit).ToArray(),
        };
    }

    private static EditSession RemovePendingTradeField(EditSession session, int tradeIndex, string field)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !IsTradeEditForRecord(edit, tradeIndex)
                    || !string.Equals(edit.Field, field, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static bool IsTradeEditForRecord(PendingEdit edit, int tradeIndex)
    {
        return IsTradeEdit(edit)
            && SwShTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var candidateIndex)
            && candidateIndex == tradeIndex;
    }

    private static bool IsTradeEdit(PendingEdit edit)
    {
        return string.Equals(
            edit.Domain,
            SwShTradePokemonWorkflowService.TradePokemonEditDomain,
            StringComparison.Ordinal);
    }

    private static IEnumerable<PendingEdit> GetTradeEdits(EditSession session)
    {
        return session.PendingEdits.Where(IsTradeEdit);
    }

    private static SwShTradePokemonWorkflow OverlayPendingEdits(
        SwShTradePokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits.Where(IsTradeEdit))
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShTradePokemonWorkflow OverlayPendingEdit(
        SwShTradePokemonWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShTradePokemonWorkflowService.TradePokemonEditDomain, StringComparison.Ordinal)
            || !SwShTradePokemonWorkflowService.IsEditableField(edit.Field)
            || !SwShTradePokemonWorkflowService.TryParseTradeRecordId(
                edit.RecordId,
                out var tradeIndex,
                out var sourceIdentity)
            || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || (edit.Field == SwShTradePokemonWorkflowService.FlawlessIvCountField
                && value is not 0 and not 3 and not 6))
        {
            return workflow;
        }

        var trades = workflow.Trades
            .Select(trade => trade.TradeIndex == tradeIndex
                && (sourceIdentity is null
                    || string.Equals(sourceIdentity, trade.SourceIdentity, StringComparison.OrdinalIgnoreCase))
                ? OverlayTradeField(workflow, trade, edit.Field!, value)
                : trade)
            .ToArray();
        return workflow with
        {
            Trades = trades,
            Stats = workflow.Stats with
            {
                TotalTradeCount = trades.Length,
                FixedIvTradeCount = trades.Count(trade => trade.FlawlessIvCount != 0),
            },
        };
    }

    private static SwShTradePokemonEntry OverlayTradeField(
        SwShTradePokemonWorkflow workflow,
        SwShTradePokemonEntry trade,
        string field,
        int value)
    {
        var updatedTrade = field switch
        {
            SwShTradePokemonWorkflowService.SpeciesField => trade with
            {
                SpeciesId = value,
                Species = GetOptionDisplayName(workflow, field, value, "Species"),
            },
            SwShTradePokemonWorkflowService.FormField => trade with { Form = value },
            SwShTradePokemonWorkflowService.LevelField => trade with { Level = value },
            SwShTradePokemonWorkflowService.HeldItemIdField => trade with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionDisplayName(workflow, field, value, "Item"),
            },
            SwShTradePokemonWorkflowService.BallItemIdField => trade with
            {
                BallItemId = value,
                BallItem = GetOptionDisplayName(workflow, field, value, "Item"),
            },
            SwShTradePokemonWorkflowService.Field03Field => trade with { Field03 = value },
            SwShTradePokemonWorkflowService.AbilityField => trade with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability slot"),
            },
            SwShTradePokemonWorkflowService.NatureField => trade with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            SwShTradePokemonWorkflowService.GenderField => trade with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, field, value, "Gender"),
            },
            SwShTradePokemonWorkflowService.ShinyLockField => trade with
            {
                ShinyLock = value,
                ShinyLockLabel = GetOptionLabel(workflow, field, value, "Shiny lock"),
            },
            SwShTradePokemonWorkflowService.DynamaxLevelField => trade with { DynamaxLevel = value },
            SwShTradePokemonWorkflowService.CanGigantamaxField => trade with { CanGigantamax = value != 0 },
            SwShTradePokemonWorkflowService.RequiredSpeciesField => trade with
            {
                RequiredSpeciesId = value,
                RequiredSpecies = GetOptionDisplayName(workflow, field, value, value == 0 ? "None" : "Species"),
            },
            SwShTradePokemonWorkflowService.RequiredFormField => trade with { RequiredForm = value },
            SwShTradePokemonWorkflowService.RequiredNatureField => trade with
            {
                RequiredNature = value,
                RequiredNatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            SwShTradePokemonWorkflowService.UnknownRequirementField => trade with { UnknownRequirement = value },
            SwShTradePokemonWorkflowService.TrainerIdField => trade with { TrainerId = value },
            SwShTradePokemonWorkflowService.OtGenderField => trade with
            {
                OtGender = value,
                OtGenderLabel = GetOptionLabel(workflow, field, value, "OT gender"),
            },
            SwShTradePokemonWorkflowService.MemoryCodeField => trade with { MemoryCode = value },
            SwShTradePokemonWorkflowService.MemoryTextVariableField => trade with { MemoryTextVariable = value },
            SwShTradePokemonWorkflowService.MemoryFeelField => trade with { MemoryFeel = value },
            SwShTradePokemonWorkflowService.MemoryIntensityField => trade with { MemoryIntensity = value },
            SwShTradePokemonWorkflowService.RelearnMove0Field => trade with { RelearnMoves = SetRelearnMove(workflow, trade.RelearnMoves, 0, value) },
            SwShTradePokemonWorkflowService.RelearnMove1Field => trade with { RelearnMoves = SetRelearnMove(workflow, trade.RelearnMoves, 1, value) },
            SwShTradePokemonWorkflowService.RelearnMove2Field => trade with { RelearnMoves = SetRelearnMove(workflow, trade.RelearnMoves, 2, value) },
            SwShTradePokemonWorkflowService.RelearnMove3Field => trade with { RelearnMoves = SetRelearnMove(workflow, trade.RelearnMoves, 3, value) },
            SwShTradePokemonWorkflowService.IvHpField => trade with { Ivs = trade.Ivs with { HP = value } },
            SwShTradePokemonWorkflowService.IvAttackField => trade with { Ivs = trade.Ivs with { Attack = value } },
            SwShTradePokemonWorkflowService.IvDefenseField => trade with { Ivs = trade.Ivs with { Defense = value } },
            SwShTradePokemonWorkflowService.IvSpeedField => trade with { Ivs = trade.Ivs with { Speed = value } },
            SwShTradePokemonWorkflowService.IvSpecialAttackField => trade with { Ivs = trade.Ivs with { SpecialAttack = value } },
            SwShTradePokemonWorkflowService.IvSpecialDefenseField => trade with { Ivs = trade.Ivs with { SpecialDefense = value } },
            SwShTradePokemonWorkflowService.FlawlessIvCountField => trade with { Ivs = CreateIvPreset(value) },
            _ => trade,
        };

        var flawlessIvCount = GetFlawlessIvCount(updatedTrade.Ivs);
        var abilityOptions = SwShTradePokemonWorkflowService.CreateAbilityOptions(
            workflow.AbilityResolver,
            updatedTrade.SpeciesId,
            updatedTrade.Form);
        var genderOptions = SwShTradePokemonWorkflowService.CreateGenderOptions(
            workflow.AbilityResolver,
            updatedTrade.SpeciesId,
            updatedTrade.Form);
        updatedTrade = updatedTrade with
        {
            AbilityOptions = abilityOptions,
            AbilityLabel = SwShTradePokemonWorkflowService.GetOptionLabel(
                abilityOptions,
                updatedTrade.Ability,
                "Ability slot"),
            GenderOptions = genderOptions,
            GenderLabel = SwShTradePokemonWorkflowService.GetOptionLabel(
                genderOptions,
                updatedTrade.Gender,
                "Gender"),
            FlawlessIvCount = flawlessIvCount,
            IvSummary = SwShTradePokemonWorkflowService.FormatIvSummary(updatedTrade.Ivs, flawlessIvCount),
            Label = FormatTradeLabel(updatedTrade),
        };

        return updatedTrade;
    }

    private static IReadOnlyList<SwShTradePokemonMoveRecord> SetRelearnMove(
        SwShTradePokemonWorkflow workflow,
        IReadOnlyList<SwShTradePokemonMoveRecord> moves,
        int slot,
        int value)
    {
        return moves
            .Select(move => move.Slot == slot
                ? move with
                {
                    MoveId = value,
                    Move = value == 0
                        ? null
                        : GetOptionDisplayName(workflow, GetRelearnMoveField(slot), value, "Move"),
                }
                : move)
            .ToArray();
    }

    private static string GetRelearnMoveField(int slot)
    {
        return slot switch
        {
            0 => SwShTradePokemonWorkflowService.RelearnMove0Field,
            1 => SwShTradePokemonWorkflowService.RelearnMove1Field,
            2 => SwShTradePokemonWorkflowService.RelearnMove2Field,
            3 => SwShTradePokemonWorkflowService.RelearnMove3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static string FormatTradeLabel(SwShTradePokemonEntry trade)
    {
        var requested = SwShSpeciesFormLabels.FormatSpeciesFormLabel(
            trade.RequiredSpecies,
            trade.RequiredSpeciesId,
            trade.RequiredForm);
        var received = SwShSpeciesFormLabels.FormatSpeciesFormLabel(
            trade.Species,
            trade.SpeciesId,
            trade.Form);

        return $"Trade {(trade.TradeIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {requested} -> {received} Lv. {trade.Level}";
    }

    private static SwShTradePokemonIvsRecord CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShTradePokemonIvsRecord(-1, -1, -1, -1, -1, -1),
            3 => new SwShTradePokemonIvsRecord(-4, -1, -1, -1, -1, -1),
            6 => new SwShTradePokemonIvsRecord(31, 31, 31, 31, 31, 31),
            _ => throw new ArgumentOutOfRangeException(nameof(flawlessIvCount)),
        };
    }

    private static int? GetFlawlessIvCount(SwShTradePokemonIvsRecord ivs)
    {
        return SwShTradePokemonArchive.GetFlawlessIvCount(
            new SwShTradePokemonIvs(
                ivs.HP,
                ivs.Attack,
                ivs.Defense,
                ivs.Speed,
                ivs.SpecialAttack,
                ivs.SpecialDefense));
    }

    private static string GetOptionLabel(
        SwShTradePokemonWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShTradePokemonWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static string GetOptionDisplayName(
        SwShTradePokemonWorkflow workflow,
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

    private static SwShTradePokemonEdit? ToTradeEdit(
        SwShTradePokemonArchive archive,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShTradePokemonWorkflowService.TryParseTradeRecordId(
                edit.RecordId,
                out var tradeIndex,
                out var sourceIdentity)
            || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Trade Pokemon edit"));
            return null;
        }

        var matches = archive.Trades.Where(trade => trade.Index == tradeIndex).ToArray();
        var trade = matches.Length == 1 ? matches[0] : null;
        if (trade is null
            || (sourceIdentity is not null
                && !string.Equals(
                    sourceIdentity,
                    SwShTradePokemonWorkflowService.CreateSourceIdentity(trade),
                    StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit no longer resolves to exactly one matching source record.",
                field: edit.Field,
                expected: "One source Trade Pokemon matching the staged index and source identity"));
            return null;
        }

        return new SwShTradePokemonEdit(tradeIndex, field, value);
    }

    private static SwShTradePokemonField? MapField(string? field)
    {
        return field switch
        {
            SwShTradePokemonWorkflowService.SpeciesField => SwShTradePokemonField.Species,
            SwShTradePokemonWorkflowService.FormField => SwShTradePokemonField.Form,
            SwShTradePokemonWorkflowService.LevelField => SwShTradePokemonField.Level,
            SwShTradePokemonWorkflowService.HeldItemIdField => SwShTradePokemonField.HeldItem,
            SwShTradePokemonWorkflowService.BallItemIdField => SwShTradePokemonField.BallItemId,
            SwShTradePokemonWorkflowService.Field03Field => SwShTradePokemonField.Field03,
            SwShTradePokemonWorkflowService.AbilityField => SwShTradePokemonField.Ability,
            SwShTradePokemonWorkflowService.NatureField => SwShTradePokemonField.Nature,
            SwShTradePokemonWorkflowService.GenderField => SwShTradePokemonField.Gender,
            SwShTradePokemonWorkflowService.ShinyLockField => SwShTradePokemonField.ShinyLock,
            SwShTradePokemonWorkflowService.DynamaxLevelField => SwShTradePokemonField.DynamaxLevel,
            SwShTradePokemonWorkflowService.CanGigantamaxField => SwShTradePokemonField.CanGigantamax,
            SwShTradePokemonWorkflowService.RequiredSpeciesField => SwShTradePokemonField.RequiredSpecies,
            SwShTradePokemonWorkflowService.RequiredFormField => SwShTradePokemonField.RequiredForm,
            SwShTradePokemonWorkflowService.RequiredNatureField => SwShTradePokemonField.RequiredNature,
            SwShTradePokemonWorkflowService.UnknownRequirementField => SwShTradePokemonField.UnknownRequirement,
            SwShTradePokemonWorkflowService.TrainerIdField => SwShTradePokemonField.TrainerId,
            SwShTradePokemonWorkflowService.OtGenderField => SwShTradePokemonField.OtGender,
            SwShTradePokemonWorkflowService.MemoryCodeField => SwShTradePokemonField.MemoryCode,
            SwShTradePokemonWorkflowService.MemoryTextVariableField => SwShTradePokemonField.MemoryTextVariable,
            SwShTradePokemonWorkflowService.MemoryFeelField => SwShTradePokemonField.MemoryFeel,
            SwShTradePokemonWorkflowService.MemoryIntensityField => SwShTradePokemonField.MemoryIntensity,
            SwShTradePokemonWorkflowService.RelearnMove0Field => SwShTradePokemonField.RelearnMove0,
            SwShTradePokemonWorkflowService.RelearnMove1Field => SwShTradePokemonField.RelearnMove1,
            SwShTradePokemonWorkflowService.RelearnMove2Field => SwShTradePokemonField.RelearnMove2,
            SwShTradePokemonWorkflowService.RelearnMove3Field => SwShTradePokemonField.RelearnMove3,
            SwShTradePokemonWorkflowService.IvHpField => SwShTradePokemonField.IvHp,
            SwShTradePokemonWorkflowService.IvAttackField => SwShTradePokemonField.IvAttack,
            SwShTradePokemonWorkflowService.IvDefenseField => SwShTradePokemonField.IvDefense,
            SwShTradePokemonWorkflowService.IvSpeedField => SwShTradePokemonField.IvSpeed,
            SwShTradePokemonWorkflowService.IvSpecialAttackField => SwShTradePokemonField.IvSpecialAttack,
            SwShTradePokemonWorkflowService.IvSpecialDefenseField => SwShTradePokemonField.IvSpecialDefense,
            SwShTradePokemonWorkflowService.FlawlessIvCountField => SwShTradePokemonField.FlawlessIvCount,
            _ => null,
        };
    }

    private static SwShTradePokemonEntry? ResolveTrade(
        SwShTradePokemonWorkflow workflow,
        int tradeIndex,
        ICollection<ValidationDiagnostic> diagnostics,
        string? field)
    {
        var matches = workflow.Trades.Where(trade => trade.TradeIndex == tradeIndex).ToArray();
        if (matches.Length == 1)
        {
            return matches[0];
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            matches.Length == 0
                ? $"Trade Pokemon index {tradeIndex} is not present in the loaded workflow."
                : $"Trade Pokemon index {tradeIndex} is ambiguous in the loaded workflow.",
            field: field ?? "tradeIndex",
            expected: "Exactly one Trade Pokemon record"));
        return null;
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
                "Trade Pokemon apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon apply target must be relative to the output root.",
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
                "Trade Pokemon apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private void WriteAllBytesAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Trade Pokemon output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Trade Pokemon output target directory could not be resolved.");
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
                throw new IOException("Trade Pokemon temporary output verification failed.");
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
                "Trade Pokemon apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon rollback failed: {failure.Message}",
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
            ? $"Apply pending Trade Pokemon edit: {pendingEdits[0].Summary}"
            : $"Apply {pendingEdits.Count} pending Trade Pokemon edits.";
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
            "Trade Pokemon IV values must be -1 for random or 0-31 for fixed values; HP IV also accepts -4 for the 3-perfect sentinel.",
            field: field,
            expected: "Supported trade IV value");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Trade Pokemon field '{field}' is not supported by the workflow yet.",
            field: "field",
            expected: "Supported Trade Pokemon field");
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
            Domain: SwShTradePokemonWorkflowService.TradePokemonEditDomain,
            Field: field,
            Expected: expected);
    }
}
