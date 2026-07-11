// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Moves;

internal sealed class ZaMovesEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaMovesWorkflowService movesWorkflowService;

    public ZaMovesEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaMovesWorkflowService? movesWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.movesWorkflowService = movesWorkflowService ?? new ZaMovesWorkflowService(this.fileSource);
    }

    public ZaMovesEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int moveId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.MovesDomain,
                diagnostics))
        {
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var move = workflow.Moves.FirstOrDefault(candidate => candidate.MoveId == moveId);
        if (move is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the loaded Moves workflow.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing Z-A move record"));
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(move, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new ZaMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaMovesEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaMoveFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.MovesDomain,
                diagnostics))
        {
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Move batch update is missing a field or value.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "updates",
                    expected: "Complete move field update"));
                continue;
            }

            var move = effectiveWorkflow.Moves.FirstOrDefault(candidate => candidate.MoveId == update.MoveId);
            if (move is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {update.MoveId} is not present in the loaded Moves workflow.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "moveId",
                    expected: "Existing Z-A move record"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(move, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
        }

        ValidatePendingPairs(loadedWorkflow, updatedSession.PendingEdits, diagnostics);

        return new ZaMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = movesWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.MovesDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        ValidatePendingPairs(workflow, session.PendingEdits, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Moves change is valid.",
                ZaEditSessionSupport.MovesDomain));
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
            ZaEditSessionSupport.MovesDomain,
            ZaDataPaths.MoveDataArray,
            "Moves",
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
                ZaEditSessionSupport.MovesDomain,
                expected: "Current reviewed Moves change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.MoveDataArray);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(paths, ZaDataPaths.MoveDataArray, WriteRows(rows), outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.MoveDataArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Moves", outputMode),
                ZaEditSessionSupport.MovesDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Moves output could not be written: {exception.Message}",
                ZaEditSessionSupport.MovesDomain,
                file: $"romfs/{ZaDataPaths.MoveDataArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaMoveRecord move,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var parsedValue = TryParseEditableValue(normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (!ValidateImmediatePairs(move, normalizedField, parsedValue.Value, diagnostics))
        {
            return null;
        }

        var editableField = ZaMovesWorkflowService.GetEditableField(normalizedField)!;
        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.MovesDomain,
            $"Set {move.Name} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(move.Provenance.SourceLayer, move.Provenance.SourceFile),
            move.MoveId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaMovesWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Moves.",
                ZaEditSessionSupport.MovesDomain,
                expected: ZaEditSessionSupport.MovesDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || workflow.Moves.All(move => move.MoveId != moveId))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending move edit targets a record that is not loaded.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing Z-A move record"));
            return;
        }

        _ = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
    }

    private static void ValidatePendingPairs(
        ZaMovesWorkflow workflow,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editedMoveIds = edits
            .Where(edit => string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal))
            .Select(edit => int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                ? moveId
                : (int?)null)
            .Where(moveId => moveId is not null)
            .Select(moveId => moveId!.Value)
            .Distinct()
            .ToHashSet();

        if (editedMoveIds.Count == 0)
        {
            return;
        }

        var overlaidWorkflow = OverlayPendingEdits(workflow, edits);
        foreach (var move in overlaidWorkflow.Moves.Where(move => editedMoveIds.Contains(move.MoveId)))
        {
            if (move.HitMin > move.HitMax)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {move.MoveId} has a minimum hit count greater than its maximum hit count.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "hit",
                    expected: "Minimum hits less than or equal to maximum hits"));
            }

            if (move.TurnMin > move.TurnMax)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {move.MoveId} has a minimum inflict turn count greater than its maximum inflict turn count.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "turn",
                    expected: "Minimum turns less than or equal to maximum turns"));
            }
        }
    }

    private static bool ValidateImmediatePairs(
        ZaMoveRecord selectedMove,
        string field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hitMin = field == ZaMovesWorkflowService.HitMinField ? value : selectedMove.HitMin;
        var hitMax = field == ZaMovesWorkflowService.HitMaxField ? value : selectedMove.HitMax;
        var turnMin = field == ZaMovesWorkflowService.TurnMinField ? value : selectedMove.TurnMin;
        var turnMax = field == ZaMovesWorkflowService.TurnMaxField ? value : selectedMove.TurnMax;

        if (hitMin > hitMax)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Move minimum hits cannot be greater than the current maximum hits.",
                ZaEditSessionSupport.MovesDomain,
                field: ZaMovesWorkflowService.HitMinField,
                expected: "Minimum hits less than or equal to maximum hits"));
            return false;
        }

        if (turnMin > turnMax)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Move minimum inflict turns cannot be greater than the current maximum inflict turns.",
                ZaEditSessionSupport.MovesDomain,
                field: ZaMovesWorkflowService.TurnMinField,
                expected: "Minimum turns less than or equal to maximum turns"));
            return false;
        }

        return true;
    }

    private static int? TryParseEditableValue(
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = ZaMovesWorkflowService.GetEditableField(field);
        if (editableField is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move field '{field ?? "(missing)"}' is not supported by Pokemon Legends Z-A Moves yet.",
                ZaEditSessionSupport.MovesDomain,
                field: "field",
                expected: "Supported Z-A move field"));
            return null;
        }

        var parsedValue = editableField.ValueKind == "boolean"
            ? TryParseBooleanValue(value, out var booleanValue) ? booleanValue : (int?)null
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue)
                ? integerValue
                : (int?)null;

        if (parsedValue is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be a valid {editableField.ValueKind} value.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        return parsedValue.Value;
    }

    private static bool TryParseBooleanValue(string? value, out int parsedValue)
    {
        parsedValue = 0;
        if (string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = 1;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static ZaMovesWorkflow OverlayPendingEdits(
        ZaMovesWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaMovesWorkflow OverlayPendingEdit(ZaMovesWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || TryParseEditableValue(edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        return workflow with
        {
            Moves = workflow.Moves
                .Select(move => move.MoveId == moveId ? OverlayMoveField(move, edit.Field!, value) : move)
                .ToArray(),
        };
    }

    private static ZaMoveRecord OverlayMoveField(ZaMoveRecord move, string field, int value)
    {
        return field switch
        {
            ZaMovesWorkflowService.CanUseMoveField => move with { CanUseMove = value != 0 },
            ZaMovesWorkflowService.TypeField => move with { Type = value, TypeName = ZaMovesWorkflowService.FormatType(value) },
            ZaMovesWorkflowService.QualityField => move with { Quality = value },
            ZaMovesWorkflowService.CategoryField => move with { Category = value, CategoryName = ZaMovesWorkflowService.FormatCategory(value) },
            ZaMovesWorkflowService.PowerField => move with { Power = value },
            ZaMovesWorkflowService.AccuracyField => move with { Accuracy = value },
            ZaMovesWorkflowService.PpField => move with { PP = value },
            ZaMovesWorkflowService.PriorityField => move with { Priority = value },
            ZaMovesWorkflowService.CritStageField => move with { CritStage = value },
            ZaMovesWorkflowService.TargetField => move with { Target = value, TargetName = ZaMovesWorkflowService.FormatTarget(value) },
            ZaMovesWorkflowService.HitMinField => move with { HitMin = value },
            ZaMovesWorkflowService.HitMaxField => move with { HitMax = value },
            ZaMovesWorkflowService.TurnMinField => move with { TurnMin = value },
            ZaMovesWorkflowService.TurnMaxField => move with { TurnMax = value },
            ZaMovesWorkflowService.InflictField => move with { Inflict = value, InflictName = ZaMovesWorkflowService.FormatInflict(value) },
            ZaMovesWorkflowService.InflictPercentField => move with { InflictPercent = value },
            ZaMovesWorkflowService.RawInflictCountField => move with { RawInflictCount = value },
            ZaMovesWorkflowService.FlinchField => move with { Flinch = value },
            ZaMovesWorkflowService.EffectSequenceField => move with { EffectSequence = value },
            ZaMovesWorkflowService.RecoilField => move with { Recoil = value },
            ZaMovesWorkflowService.RawHealingField => move with { RawHealing = value },
            ZaMovesWorkflowService.Stat1Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            ZaMovesWorkflowService.Stat1StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Stage = value }) },
            ZaMovesWorkflowService.Stat1PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Percent = value }) },
            ZaMovesWorkflowService.Stat2Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            ZaMovesWorkflowService.Stat2StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Stage = value }) },
            ZaMovesWorkflowService.Stat2PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Percent = value }) },
            ZaMovesWorkflowService.Stat3Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            ZaMovesWorkflowService.Stat3StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Stage = value }) },
            ZaMovesWorkflowService.Stat3PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Percent = value }) },
            _ when ZaMovesWorkflowService.IsEditableFlagField(field) => move with { Flags = OverlayFlag(move.Flags, field, value != 0) },
            _ => move,
        };
    }

    private static IReadOnlyList<ZaMoveStatChangeRecord> OverlayStatChange(
        IReadOnlyList<ZaMoveStatChangeRecord> statChanges,
        int slot,
        Func<ZaMoveStatChangeRecord, ZaMoveStatChangeRecord> update)
    {
        var updated = statChanges.ToList();
        var index = updated.FindIndex(stat => stat.Slot == slot);
        if (index < 0)
        {
            updated.Add(update(new ZaMoveStatChangeRecord(slot, Stat: 0, "None", Stage: 0, Percent: 0)));
        }
        else
        {
            updated[index] = update(updated[index]);
        }

        return updated.OrderBy(stat => stat.Slot).ToArray();
    }

    private static IReadOnlyList<ZaMoveFlagRecord> OverlayFlag(
        IReadOnlyList<ZaMoveFlagRecord> flags,
        string field,
        bool enabled)
    {
        return flags
            .Select(flag => string.Equals(flag.Field, field, StringComparison.Ordinal)
                ? flag with { Enabled = enabled }
                : flag)
            .ToArray();
    }

    private static void ApplyEdit(
        IReadOnlyList<MoveRow> rows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || TryParseEditableValue(edit.Field, edit.NewValue, diagnostics) is not { } value)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending move edit is not valid for apply.",
                ZaEditSessionSupport.MovesDomain,
                expected: "Valid Z-A move edit"));
            return;
        }

        var row = rows.FirstOrDefault(candidate => candidate.MoveId == moveId);
        if (row is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the source move array.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing move source row"));
            return;
        }

        ApplyField(row, edit.Field, value);
    }

    private static void ApplyField(MoveRow row, string? field, int value)
    {
        switch (field)
        {
            case ZaMovesWorkflowService.CanUseMoveField:
                row.CanUseMove = value != 0;
                break;
            case ZaMovesWorkflowService.TypeField:
                row.Type = checked((byte)value);
                break;
            case ZaMovesWorkflowService.QualityField:
                row.Quality = checked((byte)value);
                break;
            case ZaMovesWorkflowService.CategoryField:
                row.Category = checked((byte)value);
                break;
            case ZaMovesWorkflowService.PowerField:
                row.Power = checked((byte)value);
                break;
            case ZaMovesWorkflowService.AccuracyField:
                row.Accuracy = checked((byte)value);
                break;
            case ZaMovesWorkflowService.PpField:
                row.PP = checked((byte)value);
                break;
            case ZaMovesWorkflowService.PriorityField:
                row.Priority = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.CritStageField:
                row.CritStage = checked((byte)value);
                break;
            case ZaMovesWorkflowService.TargetField:
                row.RawTarget = checked((byte)value);
                break;
            case ZaMovesWorkflowService.HitMinField:
                row.HitMin = checked((byte)value);
                break;
            case ZaMovesWorkflowService.HitMaxField:
                row.HitMax = checked((byte)value);
                break;
            case ZaMovesWorkflowService.TurnMinField:
                row.Inflict.TurnMin = checked((byte)value);
                break;
            case ZaMovesWorkflowService.TurnMaxField:
                row.Inflict.TurnMax = checked((byte)value);
                break;
            case ZaMovesWorkflowService.InflictField:
                row.Inflict.Condition = checked((ushort)value);
                break;
            case ZaMovesWorkflowService.InflictPercentField:
                row.Inflict.Chance = checked((byte)value);
                break;
            case ZaMovesWorkflowService.RawInflictCountField:
                row.Inflict.TurnMode = checked((byte)value);
                break;
            case ZaMovesWorkflowService.FlinchField:
                row.Flinch = checked((byte)value);
                break;
            case ZaMovesWorkflowService.EffectSequenceField:
                row.EffectSequence = checked((ushort)value);
                break;
            case ZaMovesWorkflowService.RecoilField:
                row.Recoil = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.RawHealingField:
                row.RawHealing = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat1Field:
                row.StatChanges.Stat1 = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat1StageField:
                row.StatChanges.Stat1Stage = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat1PercentField:
                row.StatChanges.Stat1Chance = checked((byte)value);
                break;
            case ZaMovesWorkflowService.Stat2Field:
                row.StatChanges.Stat2 = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat2StageField:
                row.StatChanges.Stat2Stage = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat2PercentField:
                row.StatChanges.Stat2Chance = checked((byte)value);
                break;
            case ZaMovesWorkflowService.Stat3Field:
                row.StatChanges.Stat3 = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat3StageField:
                row.StatChanges.Stat3Stage = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat3PercentField:
                row.StatChanges.Stat3Chance = checked((byte)value);
                break;
            default:
                ApplyFlag(row, field, value != 0);
                break;
        }
    }

    private static void ApplyFlag(MoveRow row, string? field, bool value)
    {
        switch (field)
        {
            case ZaMovesWorkflowService.MakesContactField:
                row.FlagMakesContact = value;
                break;
            case ZaMovesWorkflowService.ChargeField:
                row.FlagCharge = value;
                break;
            case ZaMovesWorkflowService.RechargeField:
                row.FlagRecharge = value;
                break;
            case ZaMovesWorkflowService.ProtectField:
                row.FlagProtect = value;
                break;
            case ZaMovesWorkflowService.ReflectableField:
                row.FlagReflectable = value;
                break;
            case ZaMovesWorkflowService.SnatchField:
                row.FlagSnatch = value;
                break;
            case ZaMovesWorkflowService.MirrorField:
                row.FlagMirror = value;
                break;
            case ZaMovesWorkflowService.PunchField:
                row.FlagPunch = value;
                break;
            case ZaMovesWorkflowService.SoundField:
                row.FlagSound = value;
                break;
            case ZaMovesWorkflowService.DanceField:
                row.FlagDance = value;
                break;
            case ZaMovesWorkflowService.GravityField:
                row.FlagGravity = value;
                break;
            case ZaMovesWorkflowService.DefrostField:
                row.FlagDefrost = value;
                break;
            case ZaMovesWorkflowService.DistanceTripleField:
                row.FlagDistanceTriple = value;
                break;
            case ZaMovesWorkflowService.HealField:
                row.FlagHeal = value;
                break;
            case ZaMovesWorkflowService.IgnoreSubstituteField:
                row.FlagIgnoreSubstitute = value;
                break;
            case ZaMovesWorkflowService.FailSkyBattleField:
                row.FlagFailSkyBattle = value;
                break;
            case ZaMovesWorkflowService.AnimateAllyField:
                row.FlagAnimateAlly = value;
                break;
            case ZaMovesWorkflowService.MetronomeField:
                row.FlagMetronome = value;
                break;
            case ZaMovesWorkflowService.FailEncoreField:
                row.FlagFailEncore = value;
                break;
            case ZaMovesWorkflowService.FailMeFirstField:
                row.FlagFailMeFirst = value;
                break;
            case ZaMovesWorkflowService.FutureAttackField:
                row.FlagFutureAttack = value;
                break;
            case ZaMovesWorkflowService.PressureField:
                row.FlagPressure = value;
                break;
            case ZaMovesWorkflowService.ComboField:
                row.FlagCombo = value;
                break;
            case ZaMovesWorkflowService.NoSleepTalkField:
                row.FlagNoSleepTalk = value;
                break;
            case ZaMovesWorkflowService.NoAssistField:
                row.FlagNoAssist = value;
                break;
            case ZaMovesWorkflowService.FailCopycatField:
                row.FlagFailCopycat = value;
                break;
            case ZaMovesWorkflowService.FailMimicField:
                row.FlagFailMimic = value;
                break;
            case ZaMovesWorkflowService.FailInstructField:
                row.FlagFailInstruct = value;
                break;
            case ZaMovesWorkflowService.PowderField:
                row.FlagPowder = value;
                break;
            case ZaMovesWorkflowService.BiteField:
                row.FlagBite = value;
                break;
            case ZaMovesWorkflowService.BulletField:
                row.FlagBullet = value;
                break;
            case ZaMovesWorkflowService.NoMultiHitField:
                row.FlagNoMultiHit = value;
                break;
            case ZaMovesWorkflowService.NoEffectivenessField:
                row.FlagNoEffectiveness = value;
                break;
            case ZaMovesWorkflowService.SheerForceField:
                row.FlagSheerForce = value;
                break;
            case ZaMovesWorkflowService.SlicingField:
                row.FlagSlicing = value;
                break;
            case ZaMovesWorkflowService.WindField:
                row.FlagWind = value;
                break;
            case ZaMovesWorkflowService.CantUseTwiceField:
                row.FlagCantUseTwice = value;
                break;
        }
    }

    private static IReadOnlyList<MoveRow> ReadRows(byte[] bytes)
    {
        var table = ZaMoveDataArray.GetRootAsZaMoveDataArray(new ByteBuffer(bytes));
        var rows = new List<MoveRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(MoveRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<MoveRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = ZaMoveDataArray.CreateValuesVector(builder, offsets);
        var root = ZaMoveDataArray.CreateZaMoveDataArray(builder, vector);
        ZaMoveDataArray.FinishZaMoveDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private sealed class MoveRow
    {
        public ushort MoveId { get; init; }
        public bool CanUseMove { get; set; }
        public byte Type { get; set; }
        public byte Quality { get; set; }
        public byte Category { get; set; }
        public byte Power { get; set; }
        public byte Accuracy { get; set; }
        public byte PP { get; set; }
        public sbyte Priority { get; set; }
        public byte HitMax { get; set; }
        public byte HitMin { get; set; }
        public InflictRow Inflict { get; } = new();
        public byte CritStage { get; set; }
        public byte Flinch { get; set; }
        public ushort EffectSequence { get; set; }
        public sbyte Recoil { get; set; }
        public sbyte RawHealing { get; set; }
        public byte RawTarget { get; set; }
        public StatChangesRow StatChanges { get; } = new();
        public sbyte Affinity { get; init; }
        public bool Unknown20 { get; init; }
        public bool FlagMakesContact { get; set; }
        public bool FlagCharge { get; set; }
        public bool FlagRecharge { get; set; }
        public bool FlagProtect { get; set; }
        public bool FlagReflectable { get; set; }
        public bool FlagSnatch { get; set; }
        public bool FlagMirror { get; set; }
        public bool FlagPunch { get; set; }
        public bool FlagSound { get; set; }
        public bool FlagDance { get; set; }
        public bool FlagGravity { get; set; }
        public bool FlagDefrost { get; set; }
        public bool FlagDistanceTriple { get; set; }
        public bool FlagHeal { get; set; }
        public bool FlagIgnoreSubstitute { get; set; }
        public bool FlagFailSkyBattle { get; set; }
        public bool FlagAnimateAlly { get; set; }
        public bool FlagMetronome { get; set; }
        public bool FlagFailEncore { get; set; }
        public bool FlagFailMeFirst { get; set; }
        public bool FlagFutureAttack { get; set; }
        public bool FlagPressure { get; set; }
        public bool FlagCombo { get; set; }
        public bool FlagNoSleepTalk { get; set; }
        public bool FlagNoAssist { get; set; }
        public bool FlagFailCopycat { get; set; }
        public bool FlagFailMimic { get; set; }
        public bool FlagFailInstruct { get; set; }
        public bool FlagPowder { get; set; }
        public bool FlagBite { get; set; }
        public bool FlagBullet { get; set; }
        public bool FlagNoMultiHit { get; set; }
        public bool FlagNoEffectiveness { get; set; }
        public bool FlagSheerForce { get; set; }
        public bool FlagSlicing { get; set; }
        public bool FlagWind { get; set; }
        public bool Unknown56 { get; init; }
        public bool Unknown57 { get; init; }
        public bool Unknown58 { get; init; }
        public bool Unknown59 { get; init; }
        public bool Unknown60 { get; init; }
        public bool Unused61 { get; init; }
        public bool Unused62 { get; init; }
        public bool Unused63 { get; init; }
        public bool Unused64 { get; init; }
        public bool Unused65 { get; init; }
        public bool Unused66 { get; init; }
        public bool Unused67 { get; init; }
        public bool Unused68 { get; init; }
        public bool Unused69 { get; init; }
        public bool Unused70 { get; init; }
        public bool FlagCantUseTwice { get; set; }

        public static MoveRow From(ZaMoveData row)
        {
            var result = new MoveRow
            {
                MoveId = row.MoveId,
                CanUseMove = row.CanUseMove,
                Type = row.Type,
                Quality = row.Quality,
                Category = row.Category,
                Power = row.Power,
                Accuracy = row.Accuracy,
                PP = row.Pp,
                Priority = row.Priority,
                HitMax = row.HitMax,
                HitMin = row.HitMin,
                CritStage = row.CritStage,
                Flinch = row.Flinch,
                EffectSequence = row.EffectSequence,
                Recoil = row.Recoil,
                RawHealing = row.RawHealing,
                RawTarget = row.RawTarget,
                Affinity = row.Affinity,
                Unknown20 = row.Unknown20,
                FlagMakesContact = row.FlagMakesContact,
                FlagCharge = row.FlagCharge,
                FlagRecharge = row.FlagRecharge,
                FlagProtect = row.FlagProtect,
                FlagReflectable = row.FlagReflectable,
                FlagSnatch = row.FlagSnatch,
                FlagMirror = row.FlagMirror,
                FlagPunch = row.FlagPunch,
                FlagSound = row.FlagSound,
                FlagDance = row.FlagDance,
                FlagGravity = row.FlagGravity,
                FlagDefrost = row.FlagDefrost,
                FlagDistanceTriple = row.FlagDistanceTriple,
                FlagHeal = row.FlagHeal,
                FlagIgnoreSubstitute = row.FlagIgnoreSubstitute,
                FlagFailSkyBattle = row.FlagFailSkyBattle,
                FlagAnimateAlly = row.FlagAnimateAlly,
                FlagMetronome = row.FlagMetronome,
                FlagFailEncore = row.FlagFailEncore,
                FlagFailMeFirst = row.FlagFailMeFirst,
                FlagFutureAttack = row.FlagFutureAttack,
                FlagPressure = row.FlagPressure,
                FlagCombo = row.FlagCombo,
                FlagNoSleepTalk = row.FlagNoSleepTalk,
                FlagNoAssist = row.FlagNoAssist,
                FlagFailCopycat = row.FlagFailCopycat,
                FlagFailMimic = row.FlagFailMimic,
                FlagFailInstruct = row.FlagFailInstruct,
                FlagPowder = row.FlagPowder,
                FlagBite = row.FlagBite,
                FlagBullet = row.FlagBullet,
                FlagNoMultiHit = row.FlagNoMultiHit,
                FlagNoEffectiveness = row.FlagNoEffectiveness,
                FlagSheerForce = row.FlagSheerForce,
                FlagSlicing = row.FlagSlicing,
                FlagWind = row.FlagWind,
                Unknown56 = row.Unknown56,
                Unknown57 = row.Unknown57,
                Unknown58 = row.Unknown58,
                Unknown59 = row.Unknown59,
                Unknown60 = row.Unknown60,
                Unused61 = row.Unused61,
                Unused62 = row.Unused62,
                Unused63 = row.Unused63,
                Unused64 = row.Unused64,
                Unused65 = row.Unused65,
                Unused66 = row.Unused66,
                Unused67 = row.Unused67,
                Unused68 = row.Unused68,
                Unused69 = row.Unused69,
                Unused70 = row.Unused70,
                FlagCantUseTwice = row.FlagCantUseTwice,
            };

            if (row.Inflict is { } inflict)
            {
                result.Inflict.CopyFrom(inflict);
            }

            if (row.StatChanges is { } statChanges)
            {
                result.StatChanges.CopyFrom(statChanges);
            }

            return result;
        }

        public Offset<ZaMoveData> Write(FlatBufferBuilder builder)
        {
            ZaMoveData.StartZaMoveData(builder);
            ZaMoveData.AddFlagCantUseTwice(builder, FlagCantUseTwice);
            ZaMoveData.AddUnused70(builder, Unused70);
            ZaMoveData.AddUnused69(builder, Unused69);
            ZaMoveData.AddUnused68(builder, Unused68);
            ZaMoveData.AddUnused67(builder, Unused67);
            ZaMoveData.AddUnused66(builder, Unused66);
            ZaMoveData.AddUnused65(builder, Unused65);
            ZaMoveData.AddUnused64(builder, Unused64);
            ZaMoveData.AddUnused63(builder, Unused63);
            ZaMoveData.AddUnused62(builder, Unused62);
            ZaMoveData.AddUnused61(builder, Unused61);
            ZaMoveData.AddUnknown60(builder, Unknown60);
            ZaMoveData.AddUnknown59(builder, Unknown59);
            ZaMoveData.AddUnknown58(builder, Unknown58);
            ZaMoveData.AddUnknown57(builder, Unknown57);
            ZaMoveData.AddUnknown56(builder, Unknown56);
            ZaMoveData.AddFlagWind(builder, FlagWind);
            ZaMoveData.AddFlagSlicing(builder, FlagSlicing);
            ZaMoveData.AddFlagSheerForce(builder, FlagSheerForce);
            ZaMoveData.AddFlagNoEffectiveness(builder, FlagNoEffectiveness);
            ZaMoveData.AddFlagNoMultiHit(builder, FlagNoMultiHit);
            ZaMoveData.AddFlagBullet(builder, FlagBullet);
            ZaMoveData.AddFlagBite(builder, FlagBite);
            ZaMoveData.AddFlagPowder(builder, FlagPowder);
            ZaMoveData.AddFlagFailInstruct(builder, FlagFailInstruct);
            ZaMoveData.AddFlagFailMimic(builder, FlagFailMimic);
            ZaMoveData.AddFlagFailCopycat(builder, FlagFailCopycat);
            ZaMoveData.AddFlagNoAssist(builder, FlagNoAssist);
            ZaMoveData.AddFlagNoSleepTalk(builder, FlagNoSleepTalk);
            ZaMoveData.AddFlagCombo(builder, FlagCombo);
            ZaMoveData.AddFlagPressure(builder, FlagPressure);
            ZaMoveData.AddFlagFutureAttack(builder, FlagFutureAttack);
            ZaMoveData.AddFlagFailMeFirst(builder, FlagFailMeFirst);
            ZaMoveData.AddFlagFailEncore(builder, FlagFailEncore);
            ZaMoveData.AddFlagMetronome(builder, FlagMetronome);
            ZaMoveData.AddFlagAnimateAlly(builder, FlagAnimateAlly);
            ZaMoveData.AddFlagFailSkyBattle(builder, FlagFailSkyBattle);
            ZaMoveData.AddFlagIgnoreSubstitute(builder, FlagIgnoreSubstitute);
            ZaMoveData.AddFlagHeal(builder, FlagHeal);
            ZaMoveData.AddFlagDistanceTriple(builder, FlagDistanceTriple);
            ZaMoveData.AddFlagDefrost(builder, FlagDefrost);
            ZaMoveData.AddFlagGravity(builder, FlagGravity);
            ZaMoveData.AddFlagDance(builder, FlagDance);
            ZaMoveData.AddFlagSound(builder, FlagSound);
            ZaMoveData.AddFlagPunch(builder, FlagPunch);
            ZaMoveData.AddFlagMirror(builder, FlagMirror);
            ZaMoveData.AddFlagSnatch(builder, FlagSnatch);
            ZaMoveData.AddFlagReflectable(builder, FlagReflectable);
            ZaMoveData.AddFlagProtect(builder, FlagProtect);
            ZaMoveData.AddFlagRecharge(builder, FlagRecharge);
            ZaMoveData.AddFlagCharge(builder, FlagCharge);
            ZaMoveData.AddFlagMakesContact(builder, FlagMakesContact);
            ZaMoveData.AddUnknown20(builder, Unknown20);
            ZaMoveData.AddAffinity(builder, Affinity);
            ZaMoveData.AddStatChanges(builder, StatChanges.Write(builder));
            ZaMoveData.AddRawTarget(builder, RawTarget);
            ZaMoveData.AddRawHealing(builder, RawHealing);
            ZaMoveData.AddRecoil(builder, Recoil);
            ZaMoveData.AddEffectSequence(builder, EffectSequence);
            ZaMoveData.AddFlinch(builder, Flinch);
            ZaMoveData.AddCritStage(builder, CritStage);
            ZaMoveData.AddInflict(builder, Inflict.Write(builder));
            ZaMoveData.AddHitMin(builder, HitMin);
            ZaMoveData.AddHitMax(builder, HitMax);
            ZaMoveData.AddPriority(builder, Priority);
            ZaMoveData.AddPp(builder, PP);
            ZaMoveData.AddAccuracy(builder, Accuracy);
            ZaMoveData.AddPower(builder, Power);
            ZaMoveData.AddCategory(builder, Category);
            ZaMoveData.AddQuality(builder, Quality);
            ZaMoveData.AddType(builder, Type);
            ZaMoveData.AddCanUseMove(builder, CanUseMove);
            ZaMoveData.AddMoveId(builder, MoveId);
            return ZaMoveData.EndZaMoveData(builder);
        }
    }

    private sealed class InflictRow
    {
        public ushort Condition { get; set; }
        public byte Chance { get; set; }
        public byte TurnMode { get; set; }
        public byte TurnMin { get; set; }
        public byte TurnMax { get; set; }

        public void CopyFrom(ZaMoveInflict row)
        {
            Condition = row.Condition;
            Chance = row.Chance;
            TurnMode = row.TurnMode;
            TurnMin = row.TurnMin;
            TurnMax = row.TurnMax;
        }

        public Offset<ZaMoveInflict> Write(FlatBufferBuilder builder) =>
            ZaMoveInflict.CreateZaMoveInflict(builder, Condition, Chance, TurnMode, TurnMin, TurnMax);
    }

    private sealed class StatChangesRow
    {
        public sbyte Stat1 { get; set; }
        public sbyte Stat2 { get; set; }
        public sbyte Stat3 { get; set; }
        public sbyte Stat1Stage { get; set; }
        public sbyte Stat2Stage { get; set; }
        public sbyte Stat3Stage { get; set; }
        public byte Stat1Chance { get; set; }
        public byte Stat2Chance { get; set; }
        public byte Stat3Chance { get; set; }

        public void CopyFrom(ZaMoveStatChanges row)
        {
            Stat1 = row.Stat1;
            Stat2 = row.Stat2;
            Stat3 = row.Stat3;
            Stat1Stage = row.Stat1Stage;
            Stat2Stage = row.Stat2Stage;
            Stat3Stage = row.Stat3Stage;
            Stat1Chance = row.Stat1Chance;
            Stat2Chance = row.Stat2Chance;
            Stat3Chance = row.Stat3Chance;
        }

        public Offset<ZaMoveStatChanges> Write(FlatBufferBuilder builder) =>
            ZaMoveStatChanges.CreateZaMoveStatChanges(
                builder,
                Stat1,
                Stat1Stage,
                Stat1Chance,
                Stat2,
                Stat2Stage,
                Stat2Chance,
                Stat3,
                Stat3Stage,
                Stat3Chance);
    }
}
