// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Moves;

public sealed record SwShMoveProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShMoveStatChangeRecord(
    int Slot,
    int Stat,
    string StatName,
    int Stage,
    int Percent);

public sealed record SwShMoveFlagRecord(
    string Field,
    string Label,
    bool Enabled);

public sealed record SwShMoveEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShMoveEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShMoveEditableFieldOption> Options);

public sealed record SwShMoveRecord(
    int MoveId,
    string Name,
    string? Description,
    uint Version,
    bool CanUseMove,
    int Type,
    string TypeName,
    int Quality,
    int Category,
    string CategoryName,
    int Power,
    int Accuracy,
    int PP,
    int Priority,
    int CritStage,
    int MaxMovePower,
    int Target,
    string TargetName,
    int HitMin,
    int HitMax,
    int TurnMin,
    int TurnMax,
    int Inflict,
    string InflictName,
    int InflictPercent,
    int RawInflictCount,
    int Flinch,
    int EffectSequence,
    int Recoil,
    int RawHealing,
    IReadOnlyList<SwShMoveStatChangeRecord> StatChanges,
    IReadOnlyList<SwShMoveFlagRecord> Flags,
    SwShMoveProvenance Provenance);

public sealed record SwShMovesWorkflowStats(
    int TotalMoveCount,
    int EnabledMoveCount,
    int SourceFileCount,
    int ActiveFlagCount);

public sealed record SwShMovesWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShMoveRecord> Moves,
    IReadOnlyList<SwShMoveEditableField> EditableFields,
    SwShMovesWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
