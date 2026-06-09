// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Trades;

public sealed class SwShTradePokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShTradePokemonWorkflowService TradePokemonWorkflowService;

    public SwShTradePokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShTradePokemonWorkflowService? TradePokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.TradePokemonWorkflowService = TradePokemonWorkflowService ?? new SwShTradePokemonWorkflowService();
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
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = TradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditTradePokemon(project, workflow, diagnostics))
        {
            return new SwShTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var effectiveWorkflow = OverlayPendingEdits(workflow, currentSession.PendingEdits);
        var trade = effectiveWorkflow.Trades.FirstOrDefault(candidate => candidate.TradeIndex == tradeIndex);
        if (trade is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon index {tradeIndex} is not present in the loaded workflow.",
                field: "tradeIndex",
                expected: "Existing Trade Pokemon record"));
            return new SwShTradePokemonEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(trade, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShTradePokemonEditResult(effectiveWorkflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingTradeEdit(currentSession, pendingEdit);

        return new SwShTradePokemonEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = TradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditTradePokemon(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Trade Pokemon change is valid."));
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
                "Create a pending Trade Pokemon edit before reviewing a change plan.",
                expected: "Pending Trade Pokemon edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var tradeSource = SwShTradePokemonWorkflowService.ResolveTradePokemonDataSource(project);
        if (tradeSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon change plan could not resolve the source table.",
                expected: SwShTradePokemonWorkflowService.TradePokemonDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShTradePokemonWorkflowService.ResolveOutputPath(paths, tradeSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Trade Pokemon apply target must stay inside the configured output root.",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            tradeSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(tradeSource.GraphEntry), tradeSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Trade Pokemon edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Trade Pokemon edits.");

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
                expected: "Current reviewed Trade Pokemon change plan"));
        }

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

        try
        {
            var archive = SwShTradePokemonArchive.Parse(File.ReadAllBytes(tradeSource.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToTradeEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = archive.WriteEdits(edits);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, tradeSource.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Trade Pokemon change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon source file could not be decoded: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Sword/Shield Trade Pokemon table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon output file could not be written: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon output file could not be written: {exception.Message}",
                file: tradeSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShTradePokemonEntry trade,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SwShTradePokemonWorkflowService.GetEditableField(normalizedField);
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

        AddLinkedPlacementWarning(normalizedField, diagnostics);

        return new PendingEdit(
            SwShTradePokemonWorkflowService.TradePokemonEditDomain,
            $"Set {trade.Label} {editableField.Label} to {parsedValue.Value}.",
            [new ProjectFileReference(trade.Provenance.SourceLayer, trade.Provenance.SourceFile)],
            RecordId: SwShTradePokemonWorkflowService.CreateTradeRecordId(trade.TradeIndex),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShTradePokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShTradePokemonWorkflowService.TradePokemonEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Trade Pokemon workflow.",
                expected: SwShTradePokemonWorkflowService.TradePokemonEditDomain));
            return;
        }

        var editableField = SwShTradePokemonWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit targets an invalid record.",
                field: "tradeIndex",
                expected: "Trade Pokemon record"));
            return;
        }

        if (workflow.Trades.All(trade => trade.TradeIndex != tradeIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit targets a record that is not loaded.",
                field: "tradeIndex",
                expected: "Existing Trade Pokemon record"));
            return;
        }

        TryParseFieldValue(editableField, edit.NewValue, diagnostics);
        AddLinkedPlacementWarning(edit.Field, diagnostics);
    }

    private static int? TryParseFieldValue(
        SwShTradePokemonEditableField editableField,
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
            parsedValue = ClampFixedIvValue(parsedValue);
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

    private static int ClampFixedIvValue(int value)
    {
        return Math.Clamp(
            value,
            SwShTradePokemonArchive.MinimumFixedIvValue,
            SwShTradePokemonArchive.MaximumFixedIvValue);
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

    private static EditSession ReplacePendingTradeEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameTradeEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameTradeEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShTradePokemonWorkflow OverlayPendingEdits(
        SwShTradePokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
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
            || !SwShTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Trades = workflow.Trades
                .Select(trade => trade.TradeIndex == tradeIndex
                    ? OverlayTradeField(workflow, trade, edit.Field!, value)
                    : trade)
                .ToArray(),
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
                Species = GetOptionLabel(workflow, field, value, "Species"),
            },
            SwShTradePokemonWorkflowService.FormField => trade with { Form = value },
            SwShTradePokemonWorkflowService.LevelField => trade with { Level = value },
            SwShTradePokemonWorkflowService.HeldItemIdField => trade with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShTradePokemonWorkflowService.BallItemIdField => trade with
            {
                BallItemId = value,
                BallItem = GetOptionLabel(workflow, field, value, "Item"),
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
                RequiredSpecies = GetOptionLabel(workflow, field, value, "Species"),
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
        updatedTrade = updatedTrade with
        {
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
                        : GetOptionLabel(workflow, GetRelearnMoveField(slot), value, "Move"),
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

    private static SwShTradePokemonEdit? ToTradeEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Trade Pokemon edit"));
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

        var targetPath = SwShTradePokemonWorkflowService.ResolveOutputPath(paths, targetRelativePath);
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
