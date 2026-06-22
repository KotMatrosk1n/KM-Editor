// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Trades;

internal sealed class SvTradePokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvTradePokemonWorkflowService tradePokemonWorkflowService;

    public SvTradePokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvTradePokemonWorkflowService? tradePokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.tradePokemonWorkflowService = tradePokemonWorkflowService ?? new SvTradePokemonWorkflowService(this.fileSource);
    }

    public SvTradePokemonEditResult UpdateField(
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
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.TradePokemonDomain,
                diagnostics))
        {
            return new SvTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var trade = workflow.Trades.FirstOrDefault(candidate => candidate.TradeIndex == tradeIndex);
        if (trade is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon index {tradeIndex} is not present in the loaded workflow.",
                SvEditSessionSupport.TradePokemonDomain,
                field: "tradeIndex",
                expected: "Existing trade Pokemon record"));
            return new SvTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, trade, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvTradePokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SvTradePokemonEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits, diagnostics),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = tradePokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.TradePokemonDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Trade Pokemon change is valid.",
                SvEditSessionSupport.TradePokemonDomain));
        }

        return new SvEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Trade Pokemon edit before reviewing a change plan.",
                SvEditSessionSupport.TradePokemonDomain,
                expected: "Pending Trade Pokemon edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var touchedPaths = GetTouchedPaths(session).ToArray();
        var sources = session.PendingEdits.SelectMany(edit => edit.Sources).Distinct().ToArray();
        var writes = new List<PlannedFileWrite>();

        foreach (var virtualPath in touchedPaths)
        {
            try
            {
                var writeInfo = SvWorkflowFileSource.CreatePlannedWrite(paths, virtualPath, sources, outputMode);
                var reason = session.PendingEdits.Count == 1
                    ? $"Apply pending Trade Pokemon edit: {session.PendingEdits[0].Summary}"
                    : $"Apply {session.PendingEdits.Count} pending Trade Pokemon edits.";
                writes.Add(new PlannedFileWrite(
                    writeInfo.TargetRelativePath,
                    writeInfo.Sources,
                    writeInfo.ReplacesExistingOutput,
                    reason));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trade Pokemon change plan could not resolve the output target: {exception.Message}",
                    SvEditSessionSupport.TradePokemonDomain,
                    file: $"romfs/{virtualPath}",
                    expected: "Writable output root"));
                return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
            }
        }

        if (outputMode == SvOutputMode.Standalone)
        {
            var descriptorWriteInfo = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
            writes.Add(new PlannedFileWrite(
                descriptorWriteInfo.TargetRelativePath,
                descriptorWriteInfo.Sources,
                descriptorWriteInfo.ReplacesExistingOutput,
                "Patch Scarlet/Violet Trinity descriptor for standalone LayeredFS overrides."));
        }

        diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target files.",
            SvEditSessionSupport.TradePokemonDomain));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                SvEditSessionSupport.TradePokemonDomain,
                expected: "Current reviewed Trade Pokemon change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var tradeListSource = fileSource.Read(project, SvDataPaths.EventTradeListArray);
            var tradePokemonSource = fileSource.Read(project, SvDataPaths.EventTradePokemonArray);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, diagnostics);
            var tradeListRows = ReadTradeListRows(tradeListSource.Bytes);
            var tradePokemonRows = ReadTradePokemonRows(tradePokemonSource.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(tradeListRows, tradePokemonRows, edit, moveResolver, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var touchedPaths = GetTouchedPaths(session).ToHashSet(StringComparer.Ordinal);
            if (touchedPaths.Contains(SvDataPaths.EventTradeListArray))
            {
                SvWorkflowFileSource.Write(paths, SvDataPaths.EventTradeListArray, WriteTradeListRows(tradeListRows), outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.EventTradeListArray, outputMode));
            }

            if (touchedPaths.Contains(SvDataPaths.EventTradePokemonArray))
            {
                SvWorkflowFileSource.Write(paths, SvDataPaths.EventTradePokemonArray, WriteTradePokemonRows(tradePokemonRows), outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.EventTradePokemonArray, outputMode));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                outputMode == SvOutputMode.Standalone
                    ? "Applied Trade Pokemon change plan as standalone Scarlet/Violet output and patched the Trinity descriptor."
                    : "Applied Trade Pokemon change plan for Trinity Mod Manager. Run this output folder through Trinity Mod Manager before installing.",
                SvEditSessionSupport.TradePokemonDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon output could not be written: {exception.Message}",
                SvEditSessionSupport.TradePokemonDomain,
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SvTradePokemonWorkflow workflow,
        SvTradePokemonEntry trade,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SvTradePokemonWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (IsTradeListField(normalizedField) && trade.TradeListIndex is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade {trade.TradeIndex + 1} does not have a matching S/V trade request row.",
                SvEditSessionSupport.TradePokemonDomain,
                field: normalizedField,
                expected: "Trade request row linked to the received Pokemon label"));
            return null;
        }

        var parsedValue = SvEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            SvEditSessionSupport.TradePokemonDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        var sourceFile = IsTradeListField(normalizedField)
            ? trade.Provenance.TradeListSourceFile ?? trade.Provenance.SourceFile
            : trade.Provenance.SourceFile;

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.TradePokemonDomain,
            $"Set {trade.Label} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(trade.Provenance.SourceLayer, sourceFile),
            SvTradePokemonWorkflowService.CreateTradeRecordId(trade.TradeIndex),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SvTradePokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TradePokemonDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Trade Pokemon.",
                SvEditSessionSupport.TradePokemonDomain,
                expected: SvEditSessionSupport.TradePokemonDomain));
            return;
        }

        if (!SvTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || workflow.Trades.All(candidate => candidate.TradeIndex != tradeIndex))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit targets a record that is not loaded.",
                SvEditSessionSupport.TradePokemonDomain,
                field: "tradeIndex",
                expected: "Existing trade Pokemon record"));
            return;
        }

        var trade = workflow.Trades.First(candidate => candidate.TradeIndex == tradeIndex);
        if (IsTradeListField(edit.Field) && trade.TradeListIndex is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon request edit targets a trade without a matching request row.",
                SvEditSessionSupport.TradePokemonDomain,
                field: edit.Field,
                expected: "Trade request row linked to the received Pokemon label"));
            return;
        }

        var editableField = SvTradePokemonWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? string.Empty));
            return;
        }

        _ = SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            SvEditSessionSupport.TradePokemonDomain,
            diagnostics);
    }

    private SvTradePokemonWorkflow OverlayPendingEdits(
        OpenedProject project,
        SvTradePokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        var pendingEdits = edits
            .Where(edit =>
                string.Equals(edit.Domain, SvEditSessionSupport.TradePokemonDomain, StringComparison.Ordinal)
                && SvTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out _)
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
            var tradeListSource = fileSource.Read(project, SvDataPaths.EventTradeListArray);
            var tradePokemonSource = fileSource.Read(project, SvDataPaths.EventTradePokemonArray);
            var labels = SvTextLabelLookup.Load(project, fileSource, overlayDiagnostics);
            var abilityResolver = SvTradePokemonWorkflowService.SvTradeAbilityResolver.Load(
                project,
                fileSource,
                labels,
                overlayDiagnostics);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, overlayDiagnostics);
            var tradeListRows = ReadTradeListRows(tradeListSource.Bytes);
            var tradePokemonRows = ReadTradePokemonRows(tradePokemonSource.Bytes);
            foreach (var edit in pendingEdits)
            {
                ApplyEdit(tradeListRows, tradePokemonRows, edit, moveResolver, overlayDiagnostics);
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

            var overlayTradeListSource = tradeListSource with { Bytes = WriteTradeListRows(tradeListRows) };
            var overlayTradePokemonSource = tradePokemonSource with { Bytes = WriteTradePokemonRows(tradePokemonRows) };
            var tradesByIndex = SvTradePokemonWorkflowService
                .LoadRecords(overlayTradeListSource, overlayTradePokemonSource, labels, abilityResolver, moveResolver)
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
            diagnostics?.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon pending changes could not be previewed: {exception.Message}",
                SvEditSessionSupport.TradePokemonDomain,
                expected: "Readable S/V trade Pokemon sources"));
            return workflow;
        }
    }

    private static void ApplyEdit(
        IReadOnlyList<EventTradeListRow> tradeListRows,
        IReadOnlyList<EventTradePokemonRow> tradePokemonRows,
        PendingEdit edit,
        SvDefaultMoveResolver moveResolver,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TradePokemonDomain, StringComparison.Ordinal)
            || !SvTradePokemonWorkflowService.TryParseTradeRecordId(edit.RecordId, out var tradeIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Trade Pokemon edit is not valid for apply.",
                SvEditSessionSupport.TradePokemonDomain,
                expected: "Valid Trade Pokemon edit"));
            return;
        }

        var tradeRow = tradePokemonRows.ElementAtOrDefault(tradeIndex);
        if (tradeRow?.PokeData is not { } pokeData)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Trade Pokemon index {tradeIndex} is not present in the source event trade Pokemon array.",
                SvEditSessionSupport.TradePokemonDomain,
                field: "tradeIndex",
                expected: "Existing source trade Pokemon row"));
            return;
        }

        if (IsTradeListField(edit.Field))
        {
            var tradeListRow = tradeListRows.FirstOrDefault(row =>
                string.Equals(row.ReceivePoke, tradeRow.Label, StringComparison.Ordinal));
            if (tradeListRow is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Trade Pokemon index {tradeIndex} does not have a matching source trade request row.",
                    SvEditSessionSupport.TradePokemonDomain,
                    field: edit.Field,
                    expected: "Trade request row linked by ReceivePoke"));
                return;
            }

            ApplyTradeListField(tradeListRow, edit.Field, value);
            return;
        }

        ApplyPokemonField(pokeData, edit.Field, value, moveResolver);
    }

    private static void ApplyTradeListField(
        EventTradeListRow row,
        string? field,
        int value)
    {
        switch (field)
        {
            case SvTradePokemonWorkflowService.RequiredSpeciesField:
                row.SendPokeDevId = (global::pml.common.DevID)checked((ushort)value);
                break;
            case SvTradePokemonWorkflowService.RequiredFormField:
                row.SendPokeFormId = checked((short)value);
                break;
        }
    }

    private static void ApplyPokemonField(
        PokeDataTradeRow row,
        string? field,
        int value,
        SvDefaultMoveResolver moveResolver)
    {
        switch (field)
        {
            case SvTradePokemonWorkflowService.SpeciesField:
                row.DevId = (global::pml.common.DevID)checked((ushort)value);
                break;
            case SvTradePokemonWorkflowService.FormField:
                row.FormId = checked((short)value);
                break;
            case SvTradePokemonWorkflowService.LevelField:
                row.Level = value;
                break;
            case SvTradePokemonWorkflowService.HeldItemIdField:
                row.Item = (global::ItemID)value;
                break;
            case SvTradePokemonWorkflowService.BallItemIdField:
                row.BallId = (global::BallType)value;
                break;
            case SvTradePokemonWorkflowService.AbilityField:
                row.Tokusei = (global::TokuseiType)value;
                break;
            case SvTradePokemonWorkflowService.NatureField:
                row.Seikaku = (global::SeikakuType)value;
                break;
            case SvTradePokemonWorkflowService.GenderField:
                row.Sex = (global::SexType)value;
                break;
            case SvTradePokemonWorkflowService.ShinyLockField:
                row.RareType = (global::RareType)value;
                break;
            case SvTradePokemonWorkflowService.TeraTypeField:
                row.GemType = (global::GemType)value;
                break;
            case SvTradePokemonWorkflowService.Move1IdField:
                row.SetMove(0, value, moveResolver);
                break;
            case SvTradePokemonWorkflowService.Move2IdField:
                row.SetMove(1, value, moveResolver);
                break;
            case SvTradePokemonWorkflowService.Move3IdField:
                row.SetMove(2, value, moveResolver);
                break;
            case SvTradePokemonWorkflowService.Move4IdField:
                row.SetMove(3, value, moveResolver);
                break;
            case SvTradePokemonWorkflowService.FlawlessIvCountField:
                row.SetIvPreset(value);
                break;
            case SvTradePokemonWorkflowService.IvHpField:
                row.SetIv(ivs => ivs with { Hp = value });
                break;
            case SvTradePokemonWorkflowService.IvAttackField:
                row.SetIv(ivs => ivs with { Atk = value });
                break;
            case SvTradePokemonWorkflowService.IvDefenseField:
                row.SetIv(ivs => ivs with { Def = value });
                break;
            case SvTradePokemonWorkflowService.IvSpecialAttackField:
                row.SetIv(ivs => ivs with { SpAtk = value });
                break;
            case SvTradePokemonWorkflowService.IvSpecialDefenseField:
                row.SetIv(ivs => ivs with { SpDef = value });
                break;
            case SvTradePokemonWorkflowService.IvSpeedField:
                row.SetIv(ivs => ivs with { Agi = value });
                break;
            case SvTradePokemonWorkflowService.ScaleModeField:
                row.ScaleType = (global::SizeType)value;
                break;
            case SvTradePokemonWorkflowService.ScaleValueField:
                row.ScaleValue = checked((short)value);
                break;
            case SvTradePokemonWorkflowService.TrainerIdField:
                row.TrainerId = value;
                break;
            case SvTradePokemonWorkflowService.OtGenderField:
                row.ParentSex = (global::SexType)value;
                break;
        }
    }

    private static IReadOnlyList<EventTradeListRow> ReadTradeListRows(byte[] bytes)
    {
        var table = global::EventTradeListArray.GetRootAsEventTradeListArray(new ByteBuffer(bytes));
        var rows = new List<EventTradeListRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            rows.Add(row is { } value ? EventTradeListRow.From(value) : new EventTradeListRow());
        }

        return rows;
    }

    private static IReadOnlyList<EventTradePokemonRow> ReadTradePokemonRows(byte[] bytes)
    {
        var table = global::EventTradePokemonArray.GetRootAsEventTradePokemonArray(new ByteBuffer(bytes));
        var rows = new List<EventTradePokemonRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            rows.Add(row is { } value ? EventTradePokemonRow.From(value) : new EventTradePokemonRow());
        }

        return rows;
    }

    private static byte[] WriteTradeListRows(IReadOnlyList<EventTradeListRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::EventTradeListArray.CreateValuesVector(builder, offsets);
        var root = global::EventTradeListArray.CreateEventTradeListArray(builder, vector);
        global::EventTradeListArray.FinishEventTradeListArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] WriteTradePokemonRows(IReadOnlyList<EventTradePokemonRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::EventTradePokemonArray.CreateValuesVector(builder, offsets);
        var root = global::EventTradePokemonArray.CreateEventTradePokemonArray(builder, vector);
        global::EventTradePokemonArray.FinishEventTradePokemonArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static IEnumerable<string> GetTouchedPaths(EditSession session)
    {
        var hasTradeListEdit = session.PendingEdits.Any(edit => IsTradeListField(edit.Field));
        var hasPokemonEdit = session.PendingEdits.Any(edit => !IsTradeListField(edit.Field));
        if (hasTradeListEdit)
        {
            yield return SvDataPaths.EventTradeListArray;
        }

        if (hasPokemonEdit)
        {
            yield return SvDataPaths.EventTradePokemonArray;
        }
    }

    private static bool IsTradeListField(string? field)
    {
        return field is
            SvTradePokemonWorkflowService.RequiredSpeciesField or
            SvTradePokemonWorkflowService.RequiredFormField;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Trade Pokemon field '{field}' is not supported by Scarlet/Violet Trade Pokemon yet.",
            SvEditSessionSupport.TradePokemonDomain,
            field: "field",
            expected: "Supported S/V trade Pokemon field");
    }

    private sealed class EventTradeListRow
    {
        public string? Label { get; init; }
        public string? ReceivePoke { get; init; }
        public global::pml.common.DevID SendPokeDevId { get; set; }
        public short SendPokeFormId { get; set; }

        public static EventTradeListRow From(global::EventTradeList row)
        {
            return new EventTradeListRow
            {
                Label = row.Label,
                ReceivePoke = row.ReceivePoke,
                SendPokeDevId = row.SendPokeDevId,
                SendPokeFormId = row.SendPokeFormId,
            };
        }

        public Offset<global::EventTradeList> Write(FlatBufferBuilder builder)
        {
            var labelOffset = string.IsNullOrEmpty(Label) ? default : builder.CreateString(Label);
            var receivePokeOffset = string.IsNullOrEmpty(ReceivePoke) ? default : builder.CreateString(ReceivePoke);
            return global::EventTradeList.CreateEventTradeList(
                builder,
                labelOffset,
                receivePokeOffset,
                SendPokeDevId,
                SendPokeFormId);
        }
    }

    private sealed class EventTradePokemonRow
    {
        public string? Label { get; init; }
        public PokeDataTradeRow? PokeData { get; init; }

        public static EventTradePokemonRow From(global::EventTradePokemon row)
        {
            return new EventTradePokemonRow
            {
                Label = row.Label,
                PokeData = row.PokeData is { } pokeData ? PokeDataTradeRow.From(pokeData) : null,
            };
        }

        public Offset<global::EventTradePokemon> Write(FlatBufferBuilder builder)
        {
            var labelOffset = string.IsNullOrEmpty(Label) ? default : builder.CreateString(Label);
            var pokeDataOffset = PokeData?.Write(builder) ?? default;
            return global::EventTradePokemon.CreateEventTradePokemon(
                builder,
                labelOffset,
                pokeDataOffset);
        }
    }

    private sealed class PokeDataTradeRow
    {
        public global::pml.common.DevID DevId { get; set; }
        public short FormId { get; set; }
        public int Level { get; set; }
        public global::SexType Sex { get; set; }
        public global::TokuseiType Tokusei { get; set; }
        public global::GemType GemType { get; set; }
        public global::RareType RareType { get; set; }
        public global::SizeType ScaleType { get; set; }
        public short ScaleValue { get; set; }
        public global::SizeType WeightType { get; init; }
        public short WaightValue { get; init; }
        public global::TalentType TalentType { get; set; }
        public sbyte TalentVnum { get; set; }
        public ParamSetRow? TalentValue { get; set; }
        public ParamSetRow? EffortValue { get; init; }
        public global::ItemID Item { get; set; }
        public global::SeikakuType Seikaku { get; set; }
        public global::SeikakuType SeikakuHosei { get; init; }
        public global::WazaType WazaType { get; set; }
        public WazaSetRow?[] Waza { get; } = new WazaSetRow?[4];
        public global::BallType BallId { get; set; }
        public bool UseNickName { get; init; }
        public ulong NicknameLabel { get; init; }
        public ulong ParentNameLabel { get; init; }
        public long TrainerId { get; set; }
        public global::SexType ParentSex { get; set; }
        public global::RibbonType SetRibbon { get; init; }

        public static PokeDataTradeRow From(global::PokeDataTrade row)
        {
            var result = new PokeDataTradeRow
            {
                DevId = row.DevId,
                FormId = row.FormId,
                Level = row.Level,
                Sex = row.Sex,
                Tokusei = row.Tokusei,
                GemType = row.GemType,
                RareType = row.RareType,
                ScaleType = row.ScaleType,
                ScaleValue = row.ScaleValue,
                WeightType = row.WeightType,
                WaightValue = row.WaightValue,
                TalentType = row.TalentType,
                TalentVnum = row.TalentVnum,
                TalentValue = row.TalentValue is { } talentValue ? ParamSetRow.From(talentValue) : null,
                EffortValue = row.EffortValue is { } effortValue ? ParamSetRow.From(effortValue) : null,
                Item = row.Item,
                Seikaku = row.Seikaku,
                SeikakuHosei = row.SeikakuHosei,
                WazaType = row.WazaType,
                BallId = row.BallId,
                UseNickName = row.UseNickName,
                NicknameLabel = row.NicknameLabel,
                ParentNameLabel = row.ParentNameLabel,
                TrainerId = row.TrainerId,
                ParentSex = row.ParentSex,
                SetRibbon = row.SetRibbon,
            };

            result.Waza[0] = row.Waza1 is { } waza1 ? WazaSetRow.From(waza1) : null;
            result.Waza[1] = row.Waza2 is { } waza2 ? WazaSetRow.From(waza2) : null;
            result.Waza[2] = row.Waza3 is { } waza3 ? WazaSetRow.From(waza3) : null;
            result.Waza[3] = row.Waza4 is { } waza4 ? WazaSetRow.From(waza4) : null;
            return result;
        }

        public void SetMove(int index, int moveId, SvDefaultMoveResolver moveResolver)
        {
            if (WazaType == global::WazaType.DEFAULT)
            {
                var currentMoves = Waza
                    .Select(waza => waza is null ? 0 : (int)waza.WazaId)
                    .ToArray();
                var defaultMoves = currentMoves.Any(move => move != 0)
                    ? currentMoves
                    : moveResolver.Resolve((int)DevId, FormId, Level);

                for (var defaultIndex = 0; defaultIndex < Waza.Length; defaultIndex++)
                {
                    var defaultMove = defaultMoves.ElementAtOrDefault(defaultIndex);
                    Waza[defaultIndex] = defaultMove == 0
                        ? null
                        : new WazaSetRow((global::pml.common.WazaID)checked((ushort)defaultMove), 0);
                }

                WazaType = global::WazaType.MANUAL;
            }

            Waza[index] = moveId == 0
                ? null
                : (Waza[index] ?? new WazaSetRow((global::pml.common.WazaID)0, 0)) with
                {
                    WazaId = (global::pml.common.WazaID)checked((ushort)moveId),
                };
        }

        public void SetIvPreset(int value)
        {
            if (value <= 0)
            {
                TalentType = global::TalentType.RANDOM;
                TalentVnum = 0;
                TalentValue = null;
                return;
            }

            TalentType = global::TalentType.V_NUM;
            TalentVnum = checked((sbyte)value);
            TalentValue = null;
        }

        public void SetIv(Func<ParamSetRow, ParamSetRow> update)
        {
            TalentType = global::TalentType.VALUE;
            TalentVnum = 0;
            TalentValue = update(TalentValue ?? ParamSetRow.Zero);
        }

        public Offset<global::PokeDataTrade> Write(FlatBufferBuilder builder)
        {
            var wazaOffsets = Waza
                .Select(waza => waza?.Write(builder) ?? default)
                .ToArray();
            var talentOffset = TalentValue?.Write(builder) ?? default;
            var effortOffset = EffortValue?.Write(builder) ?? default;

            return global::PokeDataTrade.CreatePokeDataTrade(
                builder,
                DevId,
                FormId,
                Level,
                Sex,
                Tokusei,
                GemType,
                RareType,
                ScaleType,
                ScaleValue,
                WeightType,
                WaightValue,
                TalentType,
                TalentVnum,
                talentOffset,
                effortOffset,
                Item,
                Seikaku,
                SeikakuHosei,
                WazaType,
                wazaOffsets[0],
                wazaOffsets[1],
                wazaOffsets[2],
                wazaOffsets[3],
                BallId,
                UseNickName,
                NicknameLabel,
                ParentNameLabel,
                TrainerId,
                ParentSex,
                SetRibbon);
        }
    }

    private sealed record WazaSetRow(global::pml.common.WazaID WazaId, sbyte PointUp)
    {
        public static WazaSetRow From(global::WazaSet row) => new(row.WazaId, row.PointUp);

        public Offset<global::WazaSet> Write(FlatBufferBuilder builder) =>
            global::WazaSet.CreateWazaSet(builder, WazaId, PointUp);
    }

    private sealed record ParamSetRow(int Hp, int Atk, int Def, int SpAtk, int SpDef, int Agi)
    {
        public static readonly ParamSetRow Zero = new(0, 0, 0, 0, 0, 0);

        public static ParamSetRow From(global::ParamSet row) =>
            new(row.Hp, row.Atk, row.Def, row.SpAtk, row.SpDef, row.Agi);

        public Offset<global::ParamSet> Write(FlatBufferBuilder builder) =>
            global::ParamSet.CreateParamSet(builder, Hp, Atk, Def, SpAtk, SpDef, Agi);
    }
}
