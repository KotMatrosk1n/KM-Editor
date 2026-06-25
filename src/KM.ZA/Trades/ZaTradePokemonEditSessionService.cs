// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Trades;

internal sealed class ZaTradePokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaTradePokemonWorkflowService tradePokemonWorkflowService;

    public ZaTradePokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaTradePokemonWorkflowService? tradePokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.tradePokemonWorkflowService = tradePokemonWorkflowService ?? new ZaTradePokemonWorkflowService(this.fileSource);
    }

    public ZaTradePokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int tradeIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = tradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits, diagnostics);

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.TradePokemonDomain,
                diagnostics))
        {
            return new ZaTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var trade = workflow.Trades.FirstOrDefault(candidate => candidate.TradeIndex == tradeIndex);
        if (trade is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon {tradeIndex} is not present in the loaded Trade Pokemon workflow.",
                ZaEditSessionSupport.TradePokemonDomain,
                field: "tradeIndex",
                expected: "Existing trade Pokemon record"));
            return new ZaTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, trade, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new ZaTradePokemonEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits, diagnostics),
            updatedSession,
            diagnostics);
    }

    public ZaTradePokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaTradePokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = tradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits, diagnostics);

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.TradePokemonDomain,
                diagnostics))
        {
            return new ZaTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Trade Pokemon batch update is missing a field or value.",
                    ZaEditSessionSupport.TradePokemonDomain,
                    field: "updates",
                    expected: "Complete trade Pokemon field update"));
                continue;
            }

            var trade = effectiveWorkflow.Trades.FirstOrDefault(candidate => candidate.TradeIndex == update.TradeIndex);
            if (trade is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trade Pokemon {update.TradeIndex} is not present in the loaded Trade Pokemon workflow.",
                    ZaEditSessionSupport.TradePokemonDomain,
                    field: "tradeIndex",
                    expected: "Existing trade Pokemon record"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                effectiveWorkflow,
                trade,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
        }

        return new ZaTradePokemonEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits, diagnostics),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = tradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.TradePokemonDomain,
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
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Trade Pokemon change is valid.",
                ZaEditSessionSupport.TradePokemonDomain));
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
            ZaEditSessionSupport.TradePokemonDomain,
            ZaDataPaths.PokemonDataArray,
            "Trade Pokemon",
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
                ZaEditSessionSupport.TradePokemonDomain,
                expected: "Current reviewed Trade Pokemon change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            var document = ZaPokemonDataDocument.Parse(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(document, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(paths, ZaDataPaths.PokemonDataArray, document.Write(), outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.PokemonDataArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Trade Pokemon", outputMode),
                ZaEditSessionSupport.TradePokemonDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon output could not be written: {exception.Message}",
                ZaEditSessionSupport.TradePokemonDomain,
                file: $"romfs/{ZaDataPaths.PokemonDataArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaTradePokemonWorkflow workflow,
        ZaTradePokemonEntry trade,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = ZaTradePokemonWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.TradePokemonDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.TradePokemonDomain,
            $"Set {trade.Label} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(trade.Provenance.SourceLayer, trade.Provenance.SourceFile),
            ZaTradePokemonWorkflowService.CreateTradeRecordId(trade.TradeIndex),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaTradePokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TradePokemonDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Trade Pokemon.",
                ZaEditSessionSupport.TradePokemonDomain,
                expected: ZaEditSessionSupport.TradePokemonDomain));
            return;
        }

        var editableField = ZaTradePokemonWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!ZaTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || workflow.Trades.All(candidate => candidate.TradeIndex != tradeIndex))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trade Pokemon edit targets a record that is not loaded.",
                ZaEditSessionSupport.TradePokemonDomain,
                field: "tradeIndex",
                expected: "Existing trade Pokemon record"));
            return;
        }

        _ = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.TradePokemonDomain,
            diagnostics);
    }

    private ZaTradePokemonWorkflow OverlayPendingEdits(
        OpenedProject project,
        ZaTradePokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        var pendingEdits = edits
            .Where(edit =>
                string.Equals(edit.Domain, ZaEditSessionSupport.TradePokemonDomain, StringComparison.Ordinal)
                && ZaTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out _)
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
            var source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            var labels = ZaTextLabelLookup.Load(project, fileSource, overlayDiagnostics, project.Paths);
            var abilityResolver = ZaTradePokemonWorkflowService.ZaTradeAbilityResolver.Load(
                project,
                fileSource,
                labels,
                overlayDiagnostics);
            var document = ZaPokemonDataDocument.Parse(source.Bytes);
            foreach (var edit in pendingEdits)
            {
                ApplyEdit(document, edit, overlayDiagnostics);
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
            var tradesByIndex = ZaTradePokemonWorkflowService
                .LoadRecords(overlaySource, labels, abilityResolver)
                .ToDictionary(trade => trade.TradeIndex);

            return workflow with
            {
                Trades = workflow.Trades
                    .Select(trade => tradesByIndex.TryGetValue(trade.TradeIndex, out var updatedTrade) ? updatedTrade : trade)
                    .ToArray(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics?.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon pending changes could not be previewed: {exception.Message}",
                ZaEditSessionSupport.TradePokemonDomain,
                file: $"romfs/{ZaDataPaths.PokemonDataArray}",
                expected: "Readable Pokemon Legends Z-A trade Pokemon source"));
            return workflow;
        }
    }

    private static ZaTradePokemonWorkflow OverlayPendingEdit(ZaTradePokemonWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TradePokemonDomain, StringComparison.Ordinal)
            || !ZaTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Trades = workflow.Trades
                .Select(trade => trade.TradeIndex == tradeIndex ? OverlayTrade(workflow, trade, edit.Field, value) : trade)
                .ToArray(),
        };
    }

    private static ZaTradePokemonEntry OverlayTrade(
        ZaTradePokemonWorkflow workflow,
        ZaTradePokemonEntry trade,
        string? field,
        int value)
    {
        return field switch
        {
            ZaTradePokemonWorkflowService.SpeciesField => trade with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, field, value, "Pokemon"),
            },
            ZaTradePokemonWorkflowService.FormField => trade with { Form = value },
            ZaTradePokemonWorkflowService.LevelField => trade with { Level = value, MaxLevel = value },
            ZaTradePokemonWorkflowService.HeldItemIdField => trade with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionLabel(workflow, field, value, "Item"),
            },
            ZaTradePokemonWorkflowService.AbilityField => trade with
            {
                Ability = value,
                AbilityLabel = GetRecordOptionLabel(trade.AbilityOptions, value, "Ability mode"),
            },
            ZaTradePokemonWorkflowService.NatureField => trade with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            ZaTradePokemonWorkflowService.GenderField => trade with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, field, value, "Gender"),
            },
            ZaTradePokemonWorkflowService.ShinyLockField => trade with
            {
                ShinyLock = value,
                ShinyLockLabel = GetOptionLabel(workflow, field, value, "Shiny mode"),
            },
            ZaTradePokemonWorkflowService.Move1IdField => OverlayMove(trade, 0, value, workflow, field),
            ZaTradePokemonWorkflowService.Move2IdField => OverlayMove(trade, 1, value, workflow, field),
            ZaTradePokemonWorkflowService.Move3IdField => OverlayMove(trade, 2, value, workflow, field),
            ZaTradePokemonWorkflowService.Move4IdField => OverlayMove(trade, 3, value, workflow, field),
            ZaTradePokemonWorkflowService.FlawlessIvCountField => OverlayIvPreset(trade, value),
            ZaTradePokemonWorkflowService.IvHpField => OverlayIvs(trade, trade.Ivs with { HP = value }),
            ZaTradePokemonWorkflowService.IvAttackField => OverlayIvs(trade, trade.Ivs with { Attack = value }),
            ZaTradePokemonWorkflowService.IvDefenseField => OverlayIvs(trade, trade.Ivs with { Defense = value }),
            ZaTradePokemonWorkflowService.IvSpecialAttackField => OverlayIvs(trade, trade.Ivs with { SpecialAttack = value }),
            ZaTradePokemonWorkflowService.IvSpecialDefenseField => OverlayIvs(trade, trade.Ivs with { SpecialDefense = value }),
            ZaTradePokemonWorkflowService.IvSpeedField => OverlayIvs(trade, trade.Ivs with { Speed = value }),
            _ => trade,
        };
    }

    private static ZaTradePokemonEntry OverlayMove(
        ZaTradePokemonEntry trade,
        int moveIndex,
        int value,
        ZaTradePokemonWorkflow workflow,
        string field)
    {
        var moves = trade.Moves.ToList();
        while (moves.Count <= moveIndex)
        {
            moves.Add(new ZaTradePokemonMoveRecord(moves.Count, 0, null, PointUps: 0));
        }

        moves[moveIndex] = moves[moveIndex] with
        {
            MoveId = value,
            Move = value == 0 ? null : GetOptionLabel(workflow, field, value, "Move"),
        };

        return trade with { Moves = moves };
    }

    private static ZaTradePokemonEntry OverlayIvPreset(ZaTradePokemonEntry trade, int value)
    {
        return trade with
        {
            FlawlessIvCount = value,
            IvSummary = value == 0
                ? "Random IVs"
                : value == 1
                    ? "1 guaranteed perfect IV"
                    : $"{value.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs",
        };
    }

    private static ZaTradePokemonEntry OverlayIvs(ZaTradePokemonEntry trade, ZaTradePokemonIvsRecord ivs)
    {
        return trade with
        {
            Ivs = ivs,
            FlawlessIvCount = null,
            IvSummary = string.Create(
                CultureInfo.InvariantCulture,
                $"Fixed IVs: HP {ivs.HP}, Atk {ivs.Attack}, Def {ivs.Defense}, SpA {ivs.SpecialAttack}, SpD {ivs.SpecialDefense}, Spe {ivs.Speed}"),
        };
    }

    private static void ApplyEdit(
        ZaPokemonDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TradePokemonDomain, StringComparison.Ordinal)
            || !ZaTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trade Pokemon edit is not valid for apply.",
                ZaEditSessionSupport.TradePokemonDomain,
                expected: "Valid trade Pokemon edit"));
            return;
        }

        var row = document.Entries
            .Where(entry => ZaTradePokemonWorkflowService.IsTradePokemonId(entry.Id))
            .ElementAtOrDefault(tradeIndex);
        if (row is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending trade Pokemon edit target is not present in the source array.",
                ZaEditSessionSupport.TradePokemonDomain,
                field: "tradeIndex",
                expected: "Existing source trade Pokemon row"));
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
            case ZaTradePokemonWorkflowService.SpeciesField:
                row.DevNo = value;
                break;
            case ZaTradePokemonWorkflowService.FormField:
                row.FormNo = value;
                break;
            case ZaTradePokemonWorkflowService.LevelField:
                row.MinLevel = value;
                row.MaxLevel = value;
                break;
            case ZaTradePokemonWorkflowService.HeldItemIdField:
                row.HoldItem = value;
                break;
            case ZaTradePokemonWorkflowService.AbilityField:
                row.Tokusei = value;
                break;
            case ZaTradePokemonWorkflowService.NatureField:
                row.Seikaku = value;
                break;
            case ZaTradePokemonWorkflowService.GenderField:
                row.Sex = value;
                break;
            case ZaTradePokemonWorkflowService.ShinyLockField:
                row.Rare = value;
                break;
            case ZaTradePokemonWorkflowService.Move1IdField:
                SetMove(row, 0, value);
                break;
            case ZaTradePokemonWorkflowService.Move2IdField:
                SetMove(row, 1, value);
                break;
            case ZaTradePokemonWorkflowService.Move3IdField:
                SetMove(row, 2, value);
                break;
            case ZaTradePokemonWorkflowService.Move4IdField:
                SetMove(row, 3, value);
                break;
            case ZaTradePokemonWorkflowService.FlawlessIvCountField:
                SetIvPreset(row, value);
                break;
            case ZaTradePokemonWorkflowService.IvHpField:
                SetIv(row, ivs => ivs with { HP = value });
                break;
            case ZaTradePokemonWorkflowService.IvAttackField:
                SetIv(row, ivs => ivs with { Attack = value });
                break;
            case ZaTradePokemonWorkflowService.IvDefenseField:
                SetIv(row, ivs => ivs with { Defense = value });
                break;
            case ZaTradePokemonWorkflowService.IvSpecialAttackField:
                SetIv(row, ivs => ivs with { SpecialAttack = value });
                break;
            case ZaTradePokemonWorkflowService.IvSpecialDefenseField:
                SetIv(row, ivs => ivs with { SpecialDefense = value });
                break;
            case ZaTradePokemonWorkflowService.IvSpeedField:
                SetIv(row, ivs => ivs with { Speed = value });
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
        if (value <= 0)
        {
            row.TalentScale = ZaTradePokemonWorkflowService.TalentModeRandom;
            row.TalentVNum = 0;
            row.TalentValue = null;
            return;
        }

        row.TalentScale = ZaTradePokemonWorkflowService.TalentModeGuaranteedPerfectCount;
        row.TalentVNum = value;
        row.TalentValue = null;
    }

    private static void SetIv(
        ZaPokemonDataEntry row,
        Func<ZaPokemonDataStatsRecord, ZaPokemonDataStatsRecord> update)
    {
        row.TalentScale = ZaTradePokemonWorkflowService.TalentModeFixedValues;
        row.TalentVNum = 0;
        row.TalentValue = update(row.TalentValue ?? ZaPokemonDataStatsRecord.Zero);
    }

    private static string GetOptionLabel(
        ZaTradePokemonWorkflow workflow,
        string? field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal));
        return GetRecordOptionLabel(options?.Options ?? [], value, fallbackPrefix);
    }

    private static string GetRecordOptionLabel(
        IReadOnlyList<ZaTradePokemonEditableFieldOption> options,
        int value,
        string fallbackPrefix)
    {
        return options.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Trade Pokemon field '{field}' is not supported by Pokemon Legends Z-A Trade Pokemon yet.",
            ZaEditSessionSupport.TradePokemonDomain,
            field: "field",
            expected: "Supported Pokemon Legends Z-A trade Pokemon field");
    }
}
