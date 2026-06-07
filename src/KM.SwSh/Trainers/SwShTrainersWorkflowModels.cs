// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Trainers;

public sealed record SwShTrainerProvenance(
    string SourceFile,
    string TeamSourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileLayer TeamSourceLayer,
    ProjectFileGraphEntryState FileState,
    ProjectFileGraphEntryState TeamFileState);

public sealed record SwShTrainerPokemonRecord(
    int Slot,
    int SpeciesId,
    string Species,
    int Level,
    int HeldItemId,
    string? HeldItem,
    IReadOnlyList<int> MoveIds,
    IReadOnlyList<string> Moves);

public sealed record SwShTrainerRecord(
    int TrainerId,
    string Name,
    int TrainerClassId,
    string TrainerClass,
    string Location,
    int BattleTypeValue,
    string BattleType,
    IReadOnlyList<SwShTrainerPokemonRecord> Team,
    SwShTrainerProvenance Provenance);

public sealed record SwShTrainerEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue);

public sealed record SwShTrainersWorkflowStats(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record SwShTrainersWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShTrainerRecord> Trainers,
    IReadOnlyList<SwShTrainerEditableField> EditableFields,
    SwShTrainersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
