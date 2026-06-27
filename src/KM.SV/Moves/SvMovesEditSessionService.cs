// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Moves;

internal sealed class SvMovesEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvMovesWorkflowService movesWorkflowService;

    public SvMovesEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvMovesWorkflowService? movesWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.movesWorkflowService = movesWorkflowService ?? new SvMovesWorkflowService(this.fileSource);
    }

    public SvMovesEditResult UpdateField(
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

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.MovesDomain,
                diagnostics))
        {
            return new SvMovesEditResult(workflow, currentSession, diagnostics);
        }

        var move = workflow.Moves.FirstOrDefault(candidate => candidate.MoveId == moveId);
        if (move is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the loaded Moves workflow.",
                SvEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing S/V move record"));
            return new SvMovesEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(move, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvMovesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SvMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvMovesEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvMoveFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.MovesDomain,
                diagnostics))
        {
            return new SvMovesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Move batch update is missing a field or value.",
                    SvEditSessionSupport.MovesDomain,
                    field: "updates",
                    expected: "Complete move field update"));
                continue;
            }

            var move = effectiveWorkflow.Moves.FirstOrDefault(candidate => candidate.MoveId == update.MoveId);
            if (move is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {update.MoveId} is not present in the loaded Moves workflow.",
                    SvEditSessionSupport.MovesDomain,
                    field: "moveId",
                    expected: "Existing S/V move record"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(move, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = SvEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
        }

        ValidatePendingPairs(loadedWorkflow, updatedSession.PendingEdits, diagnostics);

        return new SvMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = movesWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.MovesDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        ValidatePendingPairs(workflow, session.PendingEdits, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Moves change is valid.",
                SvEditSessionSupport.MovesDomain));
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
        return SvEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            SvEditSessionSupport.MovesDomain,
            SvDataPaths.MoveDataArray,
            "Moves",
            validation.Diagnostics,
            outputMode);
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
                SvEditSessionSupport.MovesDomain,
                expected: "Current reviewed Moves change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, SvDataPaths.MoveDataArray);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            SvWorkflowFileSource.Write(paths, SvDataPaths.MoveDataArray, WriteRows(rows), outputMode);
            writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.MoveDataArray, outputMode));
            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                SvEditSessionSupport.CreateApplyOutputMessage("Moves", outputMode),
                SvEditSessionSupport.MovesDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Moves output could not be written: {exception.Message}",
                SvEditSessionSupport.MovesDomain,
                file: $"romfs/{SvDataPaths.MoveDataArray}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SvMoveRecord move,
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

        var editableField = SvMovesWorkflowService.GetEditableField(normalizedField)!;
        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.MovesDomain,
            $"Set {move.Name} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(move.Provenance.SourceLayer, move.Provenance.SourceFile),
            move.MoveId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SvMovesWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.MovesDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Moves.",
                SvEditSessionSupport.MovesDomain,
                expected: SvEditSessionSupport.MovesDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || workflow.Moves.All(move => move.MoveId != moveId))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending move edit targets a record that is not loaded.",
                SvEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing S/V move record"));
            return;
        }

        _ = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
    }

    private static void ValidatePendingPairs(
        SvMovesWorkflow workflow,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editedMoveIds = edits
            .Where(edit => string.Equals(edit.Domain, SvEditSessionSupport.MovesDomain, StringComparison.Ordinal))
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
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {move.MoveId} has a minimum hit count greater than its maximum hit count.",
                    SvEditSessionSupport.MovesDomain,
                    field: "hit",
                    expected: "Minimum hits less than or equal to maximum hits"));
            }

            if (move.TurnMin > move.TurnMax)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {move.MoveId} has a minimum inflict turn count greater than its maximum inflict turn count.",
                    SvEditSessionSupport.MovesDomain,
                    field: "turn",
                    expected: "Minimum turns less than or equal to maximum turns"));
            }
        }
    }

    private static bool ValidateImmediatePairs(
        SvMoveRecord selectedMove,
        string field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hitMin = field == SvMovesWorkflowService.HitMinField ? value : selectedMove.HitMin;
        var hitMax = field == SvMovesWorkflowService.HitMaxField ? value : selectedMove.HitMax;
        var turnMin = field == SvMovesWorkflowService.TurnMinField ? value : selectedMove.TurnMin;
        var turnMax = field == SvMovesWorkflowService.TurnMaxField ? value : selectedMove.TurnMax;

        if (hitMin > hitMax)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Move minimum hits cannot be greater than the current maximum hits.",
                SvEditSessionSupport.MovesDomain,
                field: SvMovesWorkflowService.HitMinField,
                expected: "Minimum hits less than or equal to maximum hits"));
            return false;
        }

        if (turnMin > turnMax)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Move minimum inflict turns cannot be greater than the current maximum inflict turns.",
                SvEditSessionSupport.MovesDomain,
                field: SvMovesWorkflowService.TurnMinField,
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
        var editableField = SvMovesWorkflowService.GetEditableField(field);
        if (editableField is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move field '{field ?? "(missing)"}' is not supported by Scarlet/Violet Moves yet.",
                SvEditSessionSupport.MovesDomain,
                field: "field",
                expected: "Supported S/V move field"));
            return null;
        }

        var parsedValue = editableField.ValueKind == "boolean"
            ? TryParseBooleanValue(value, out var booleanValue) ? booleanValue : (int?)null
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue)
                ? integerValue
                : (int?)null;

        if (parsedValue is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be a valid {editableField.ValueKind} value.",
                SvEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                SvEditSessionSupport.MovesDomain,
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

    private static SvMovesWorkflow OverlayPendingEdits(
        SvMovesWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SvMovesWorkflow OverlayPendingEdit(SvMovesWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.MovesDomain, StringComparison.Ordinal)
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

    private static SvMoveRecord OverlayMoveField(SvMoveRecord move, string field, int value)
    {
        return field switch
        {
            SvMovesWorkflowService.CanUseMoveField => move with { CanUseMove = value != 0 },
            SvMovesWorkflowService.TypeField => move with { Type = value, TypeName = SvMovesWorkflowService.FormatType(value) },
            SvMovesWorkflowService.QualityField => move with { Quality = value },
            SvMovesWorkflowService.CategoryField => move with { Category = value, CategoryName = SvMovesWorkflowService.FormatCategory(value) },
            SvMovesWorkflowService.PowerField => move with { Power = value },
            SvMovesWorkflowService.AccuracyField => move with { Accuracy = value },
            SvMovesWorkflowService.PpField => move with { PP = value },
            SvMovesWorkflowService.PriorityField => move with { Priority = value },
            SvMovesWorkflowService.CritStageField => move with { CritStage = value },
            SvMovesWorkflowService.TargetField => move with { Target = value, TargetName = SvMovesWorkflowService.FormatTarget(value) },
            SvMovesWorkflowService.HitMinField => move with { HitMin = value },
            SvMovesWorkflowService.HitMaxField => move with { HitMax = value },
            SvMovesWorkflowService.TurnMinField => move with { TurnMin = value },
            SvMovesWorkflowService.TurnMaxField => move with { TurnMax = value },
            SvMovesWorkflowService.InflictField => move with { Inflict = value, InflictName = SvMovesWorkflowService.FormatInflict(value) },
            SvMovesWorkflowService.InflictPercentField => move with { InflictPercent = value },
            SvMovesWorkflowService.RawInflictCountField => move with { RawInflictCount = value },
            SvMovesWorkflowService.FlinchField => move with { Flinch = value },
            SvMovesWorkflowService.EffectSequenceField => move with { EffectSequence = value },
            SvMovesWorkflowService.RecoilField => move with { Recoil = value },
            SvMovesWorkflowService.RawHealingField => move with { RawHealing = value },
            SvMovesWorkflowService.Stat1Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Stat = value, StatName = SvMovesWorkflowService.FormatStat(value) }) },
            SvMovesWorkflowService.Stat1StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Stage = value }) },
            SvMovesWorkflowService.Stat1PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Percent = value }) },
            SvMovesWorkflowService.Stat2Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Stat = value, StatName = SvMovesWorkflowService.FormatStat(value) }) },
            SvMovesWorkflowService.Stat2StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Stage = value }) },
            SvMovesWorkflowService.Stat2PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Percent = value }) },
            SvMovesWorkflowService.Stat3Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Stat = value, StatName = SvMovesWorkflowService.FormatStat(value) }) },
            SvMovesWorkflowService.Stat3StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Stage = value }) },
            SvMovesWorkflowService.Stat3PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Percent = value }) },
            _ when SvMovesWorkflowService.IsEditableFlagField(field) => move with { Flags = OverlayFlag(move.Flags, field, value != 0) },
            _ => move,
        };
    }

    private static IReadOnlyList<SvMoveStatChangeRecord> OverlayStatChange(
        IReadOnlyList<SvMoveStatChangeRecord> statChanges,
        int slot,
        Func<SvMoveStatChangeRecord, SvMoveStatChangeRecord> update)
    {
        var updated = statChanges.ToList();
        var index = updated.FindIndex(stat => stat.Slot == slot);
        if (index < 0)
        {
            updated.Add(update(new SvMoveStatChangeRecord(slot, Stat: 0, "None", Stage: 0, Percent: 0)));
        }
        else
        {
            updated[index] = update(updated[index]);
        }

        return updated.OrderBy(stat => stat.Slot).ToArray();
    }

    private static IReadOnlyList<SvMoveFlagRecord> OverlayFlag(
        IReadOnlyList<SvMoveFlagRecord> flags,
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
        if (!string.Equals(edit.Domain, SvEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || TryParseEditableValue(edit.Field, edit.NewValue, diagnostics) is not { } value)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending move edit is not valid for apply.",
                SvEditSessionSupport.MovesDomain,
                expected: "Valid S/V move edit"));
            return;
        }

        var row = rows.FirstOrDefault(candidate => candidate.MoveId == moveId);
        if (row is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the source move array.",
                SvEditSessionSupport.MovesDomain,
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
            case SvMovesWorkflowService.CanUseMoveField:
                row.CanUseMove = value != 0;
                break;
            case SvMovesWorkflowService.TypeField:
                row.Type = checked((byte)value);
                break;
            case SvMovesWorkflowService.QualityField:
                row.Quality = checked((byte)value);
                break;
            case SvMovesWorkflowService.CategoryField:
                row.Category = checked((byte)value);
                break;
            case SvMovesWorkflowService.PowerField:
                row.Power = checked((byte)value);
                break;
            case SvMovesWorkflowService.AccuracyField:
                row.Accuracy = checked((byte)value);
                break;
            case SvMovesWorkflowService.PpField:
                row.PP = checked((byte)value);
                break;
            case SvMovesWorkflowService.PriorityField:
                row.Priority = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.CritStageField:
                row.CritStage = checked((byte)value);
                break;
            case SvMovesWorkflowService.TargetField:
                row.RawTarget = checked((byte)value);
                break;
            case SvMovesWorkflowService.HitMinField:
                row.HitMin = checked((byte)value);
                break;
            case SvMovesWorkflowService.HitMaxField:
                row.HitMax = checked((byte)value);
                break;
            case SvMovesWorkflowService.TurnMinField:
                row.Inflict.TurnMin = checked((byte)value);
                break;
            case SvMovesWorkflowService.TurnMaxField:
                row.Inflict.TurnMax = checked((byte)value);
                break;
            case SvMovesWorkflowService.InflictField:
                row.Inflict.Condition = checked((ushort)value);
                break;
            case SvMovesWorkflowService.InflictPercentField:
                row.Inflict.Chance = checked((byte)value);
                break;
            case SvMovesWorkflowService.RawInflictCountField:
                row.Inflict.TurnMode = checked((byte)value);
                break;
            case SvMovesWorkflowService.FlinchField:
                row.Flinch = checked((byte)value);
                break;
            case SvMovesWorkflowService.EffectSequenceField:
                row.EffectSequence = checked((ushort)value);
                break;
            case SvMovesWorkflowService.RecoilField:
                row.Recoil = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.RawHealingField:
                row.RawHealing = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.Stat1Field:
                row.StatChanges.Stat1 = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.Stat1StageField:
                row.StatChanges.Stat1Stage = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.Stat1PercentField:
                row.StatChanges.Stat1Chance = checked((byte)value);
                break;
            case SvMovesWorkflowService.Stat2Field:
                row.StatChanges.Stat2 = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.Stat2StageField:
                row.StatChanges.Stat2Stage = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.Stat2PercentField:
                row.StatChanges.Stat2Chance = checked((byte)value);
                break;
            case SvMovesWorkflowService.Stat3Field:
                row.StatChanges.Stat3 = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.Stat3StageField:
                row.StatChanges.Stat3Stage = checked((sbyte)value);
                break;
            case SvMovesWorkflowService.Stat3PercentField:
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
            case SvMovesWorkflowService.MakesContactField:
                row.FlagMakesContact = value;
                break;
            case SvMovesWorkflowService.ChargeField:
                row.FlagCharge = value;
                break;
            case SvMovesWorkflowService.RechargeField:
                row.FlagRecharge = value;
                break;
            case SvMovesWorkflowService.ProtectField:
                row.FlagProtect = value;
                break;
            case SvMovesWorkflowService.ReflectableField:
                row.FlagReflectable = value;
                break;
            case SvMovesWorkflowService.SnatchField:
                row.FlagSnatch = value;
                break;
            case SvMovesWorkflowService.MirrorField:
                row.FlagMirror = value;
                break;
            case SvMovesWorkflowService.PunchField:
                row.FlagPunch = value;
                break;
            case SvMovesWorkflowService.SoundField:
                row.FlagSound = value;
                break;
            case SvMovesWorkflowService.DanceField:
                row.FlagDance = value;
                break;
            case SvMovesWorkflowService.GravityField:
                row.FlagGravity = value;
                break;
            case SvMovesWorkflowService.DefrostField:
                row.FlagDefrost = value;
                break;
            case SvMovesWorkflowService.DistanceTripleField:
                row.FlagDistanceTriple = value;
                break;
            case SvMovesWorkflowService.HealField:
                row.FlagHeal = value;
                break;
            case SvMovesWorkflowService.IgnoreSubstituteField:
                row.FlagIgnoreSubstitute = value;
                break;
            case SvMovesWorkflowService.FailSkyBattleField:
                row.FlagFailSkyBattle = value;
                break;
            case SvMovesWorkflowService.AnimateAllyField:
                row.FlagAnimateAlly = value;
                break;
            case SvMovesWorkflowService.MetronomeField:
                row.FlagMetronome = value;
                break;
            case SvMovesWorkflowService.FailEncoreField:
                row.FlagFailEncore = value;
                break;
            case SvMovesWorkflowService.FailMeFirstField:
                row.FlagFailMeFirst = value;
                break;
            case SvMovesWorkflowService.FutureAttackField:
                row.FlagFutureAttack = value;
                break;
            case SvMovesWorkflowService.PressureField:
                row.FlagPressure = value;
                break;
            case SvMovesWorkflowService.ComboField:
                row.FlagCombo = value;
                break;
            case SvMovesWorkflowService.NoSleepTalkField:
                row.FlagNoSleepTalk = value;
                break;
            case SvMovesWorkflowService.NoAssistField:
                row.FlagNoAssist = value;
                break;
            case SvMovesWorkflowService.FailCopycatField:
                row.FlagFailCopycat = value;
                break;
            case SvMovesWorkflowService.FailMimicField:
                row.FlagFailMimic = value;
                break;
            case SvMovesWorkflowService.FailInstructField:
                row.FlagFailInstruct = value;
                break;
            case SvMovesWorkflowService.PowderField:
                row.FlagPowder = value;
                break;
            case SvMovesWorkflowService.BiteField:
                row.FlagBite = value;
                break;
            case SvMovesWorkflowService.BulletField:
                row.FlagBullet = value;
                break;
            case SvMovesWorkflowService.NoMultiHitField:
                row.FlagNoMultiHit = value;
                break;
            case SvMovesWorkflowService.NoEffectivenessField:
                row.FlagNoEffectiveness = value;
                break;
            case SvMovesWorkflowService.SheerForceField:
                row.FlagSheerForce = value;
                break;
            case SvMovesWorkflowService.SlicingField:
                row.FlagSlicing = value;
                break;
            case SvMovesWorkflowService.WindField:
                row.FlagWind = value;
                break;
            case SvMovesWorkflowService.CantUseTwiceField:
                row.FlagCantUseTwice = value;
                break;
        }
    }

    private static IReadOnlyList<MoveRow> ReadRows(byte[] bytes)
    {
        var table = global::SvMoveDataArray.GetRootAsSvMoveDataArray(new ByteBuffer(bytes));
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
        var vector = global::SvMoveDataArray.CreateValuesVector(builder, offsets);
        var root = global::SvMoveDataArray.CreateSvMoveDataArray(builder, vector);
        global::SvMoveDataArray.FinishSvMoveDataArrayBuffer(builder, root);
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

        public static MoveRow From(global::SvMoveData row)
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

        public Offset<global::SvMoveData> Write(FlatBufferBuilder builder)
        {
            global::SvMoveData.StartSvMoveData(builder);
            global::SvMoveData.AddFlagCantUseTwice(builder, FlagCantUseTwice);
            global::SvMoveData.AddUnused70(builder, Unused70);
            global::SvMoveData.AddUnused69(builder, Unused69);
            global::SvMoveData.AddUnused68(builder, Unused68);
            global::SvMoveData.AddUnused67(builder, Unused67);
            global::SvMoveData.AddUnused66(builder, Unused66);
            global::SvMoveData.AddUnused65(builder, Unused65);
            global::SvMoveData.AddUnused64(builder, Unused64);
            global::SvMoveData.AddUnused63(builder, Unused63);
            global::SvMoveData.AddUnused62(builder, Unused62);
            global::SvMoveData.AddUnused61(builder, Unused61);
            global::SvMoveData.AddUnknown60(builder, Unknown60);
            global::SvMoveData.AddUnknown59(builder, Unknown59);
            global::SvMoveData.AddUnknown58(builder, Unknown58);
            global::SvMoveData.AddUnknown57(builder, Unknown57);
            global::SvMoveData.AddUnknown56(builder, Unknown56);
            global::SvMoveData.AddFlagWind(builder, FlagWind);
            global::SvMoveData.AddFlagSlicing(builder, FlagSlicing);
            global::SvMoveData.AddFlagSheerForce(builder, FlagSheerForce);
            global::SvMoveData.AddFlagNoEffectiveness(builder, FlagNoEffectiveness);
            global::SvMoveData.AddFlagNoMultiHit(builder, FlagNoMultiHit);
            global::SvMoveData.AddFlagBullet(builder, FlagBullet);
            global::SvMoveData.AddFlagBite(builder, FlagBite);
            global::SvMoveData.AddFlagPowder(builder, FlagPowder);
            global::SvMoveData.AddFlagFailInstruct(builder, FlagFailInstruct);
            global::SvMoveData.AddFlagFailMimic(builder, FlagFailMimic);
            global::SvMoveData.AddFlagFailCopycat(builder, FlagFailCopycat);
            global::SvMoveData.AddFlagNoAssist(builder, FlagNoAssist);
            global::SvMoveData.AddFlagNoSleepTalk(builder, FlagNoSleepTalk);
            global::SvMoveData.AddFlagCombo(builder, FlagCombo);
            global::SvMoveData.AddFlagPressure(builder, FlagPressure);
            global::SvMoveData.AddFlagFutureAttack(builder, FlagFutureAttack);
            global::SvMoveData.AddFlagFailMeFirst(builder, FlagFailMeFirst);
            global::SvMoveData.AddFlagFailEncore(builder, FlagFailEncore);
            global::SvMoveData.AddFlagMetronome(builder, FlagMetronome);
            global::SvMoveData.AddFlagAnimateAlly(builder, FlagAnimateAlly);
            global::SvMoveData.AddFlagFailSkyBattle(builder, FlagFailSkyBattle);
            global::SvMoveData.AddFlagIgnoreSubstitute(builder, FlagIgnoreSubstitute);
            global::SvMoveData.AddFlagHeal(builder, FlagHeal);
            global::SvMoveData.AddFlagDistanceTriple(builder, FlagDistanceTriple);
            global::SvMoveData.AddFlagDefrost(builder, FlagDefrost);
            global::SvMoveData.AddFlagGravity(builder, FlagGravity);
            global::SvMoveData.AddFlagDance(builder, FlagDance);
            global::SvMoveData.AddFlagSound(builder, FlagSound);
            global::SvMoveData.AddFlagPunch(builder, FlagPunch);
            global::SvMoveData.AddFlagMirror(builder, FlagMirror);
            global::SvMoveData.AddFlagSnatch(builder, FlagSnatch);
            global::SvMoveData.AddFlagReflectable(builder, FlagReflectable);
            global::SvMoveData.AddFlagProtect(builder, FlagProtect);
            global::SvMoveData.AddFlagRecharge(builder, FlagRecharge);
            global::SvMoveData.AddFlagCharge(builder, FlagCharge);
            global::SvMoveData.AddFlagMakesContact(builder, FlagMakesContact);
            global::SvMoveData.AddAffinity(builder, Affinity);
            global::SvMoveData.AddStatChanges(builder, StatChanges.Write(builder));
            global::SvMoveData.AddRawTarget(builder, RawTarget);
            global::SvMoveData.AddRawHealing(builder, RawHealing);
            global::SvMoveData.AddRecoil(builder, Recoil);
            global::SvMoveData.AddEffectSequence(builder, EffectSequence);
            global::SvMoveData.AddFlinch(builder, Flinch);
            global::SvMoveData.AddCritStage(builder, CritStage);
            global::SvMoveData.AddInflict(builder, Inflict.Write(builder));
            global::SvMoveData.AddHitMin(builder, HitMin);
            global::SvMoveData.AddHitMax(builder, HitMax);
            global::SvMoveData.AddPriority(builder, Priority);
            global::SvMoveData.AddPp(builder, PP);
            global::SvMoveData.AddAccuracy(builder, Accuracy);
            global::SvMoveData.AddPower(builder, Power);
            global::SvMoveData.AddCategory(builder, Category);
            global::SvMoveData.AddQuality(builder, Quality);
            global::SvMoveData.AddType(builder, Type);
            global::SvMoveData.AddCanUseMove(builder, CanUseMove);
            global::SvMoveData.AddMoveId(builder, MoveId);
            return global::SvMoveData.EndSvMoveData(builder);
        }
    }

    private sealed class InflictRow
    {
        public ushort Condition { get; set; }
        public byte Chance { get; set; }
        public byte TurnMode { get; set; }
        public byte TurnMin { get; set; }
        public byte TurnMax { get; set; }

        public void CopyFrom(global::SvMoveInflict row)
        {
            Condition = row.Condition;
            Chance = row.Chance;
            TurnMode = row.TurnMode;
            TurnMin = row.TurnMin;
            TurnMax = row.TurnMax;
        }

        public Offset<global::SvMoveInflict> Write(FlatBufferBuilder builder) =>
            global::SvMoveInflict.CreateSvMoveInflict(builder, Condition, Chance, TurnMode, TurnMin, TurnMax);
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

        public void CopyFrom(global::SvMoveStatChanges row)
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

        public Offset<global::SvMoveStatChanges> Write(FlatBufferBuilder builder) =>
            global::SvMoveStatChanges.CreateSvMoveStatChanges(
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
