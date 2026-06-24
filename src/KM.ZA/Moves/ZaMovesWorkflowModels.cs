// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Moves;

public sealed record ZaMoveProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaMoveStatChangeRecord(
    int Slot,
    int Stat,
    string StatName,
    int Stage,
    int Percent);

public sealed record ZaMoveFlagRecord(
    string Field,
    string Label,
    bool Enabled);

public sealed record ZaMoveEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaMoveEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaMoveEditableFieldOption> Options);

public sealed record ZaMoveRecord(
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
    IReadOnlyList<ZaMoveStatChangeRecord> StatChanges,
    IReadOnlyList<ZaMoveFlagRecord> Flags,
    ZaMoveProvenance Provenance);

public sealed record ZaMovesWorkflowStats(
    int TotalMoveCount,
    int EnabledMoveCount,
    int SourceFileCount,
    int ActiveFlagCount);

public sealed record ZaMovesWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaMoveRecord> Moves,
    IReadOnlyList<ZaMoveEditableField> EditableFields,
    ZaMovesWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
