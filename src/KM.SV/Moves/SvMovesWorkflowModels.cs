// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Moves;

public sealed record SvMoveProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvMoveStatChangeRecord(
    int Slot,
    int Stat,
    string StatName,
    int Stage,
    int Percent);

public sealed record SvMoveFlagRecord(
    string Field,
    string Label,
    bool Enabled);

public sealed record SvMoveEditableFieldOption(
    int Value,
    string Label);

public sealed record SvMoveEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvMoveEditableFieldOption> Options);

public sealed record SvMoveRecord(
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
    IReadOnlyList<SvMoveStatChangeRecord> StatChanges,
    IReadOnlyList<SvMoveFlagRecord> Flags,
    SvMoveProvenance Provenance);

public sealed record SvMovesWorkflowStats(
    int TotalMoveCount,
    int EnabledMoveCount,
    int SourceFileCount,
    int ActiveFlagCount);

public sealed record SvMovesWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvMoveRecord> Moves,
    IReadOnlyList<SvMoveEditableField> EditableFields,
    SvMovesWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
