// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Trainers;

public sealed record SwShTrainerProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShTrainerPokemonRecord(
    int Slot,
    string Species,
    int Level,
    string? HeldItem,
    IReadOnlyList<string> Moves);

public sealed record SwShTrainerRecord(
    int TrainerId,
    string Name,
    string TrainerClass,
    string Location,
    string BattleType,
    IReadOnlyList<SwShTrainerPokemonRecord> Team,
    SwShTrainerProvenance Provenance);

public sealed record SwShTrainersWorkflowStats(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record SwShTrainersWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShTrainerRecord> Trainers,
    SwShTrainersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
