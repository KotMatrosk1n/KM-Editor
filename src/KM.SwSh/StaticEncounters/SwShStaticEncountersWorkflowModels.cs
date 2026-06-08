// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.StaticEncounters;

public sealed record SwShStaticEncounterProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShStaticEncounterStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShStaticEncounterMoveRecord(
    int Slot,
    int MoveId,
    string? Move);

public sealed record SwShStaticEncounterEntry(
    int EncounterIndex,
    string Label,
    string EncounterId,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int HeldItemId,
    string? HeldItem,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    int Gender,
    string GenderLabel,
    int ShinyLock,
    string ShinyLockLabel,
    int EncounterScenario,
    string EncounterScenarioLabel,
    int DynamaxLevel,
    bool CanGigantamax,
    SwShStaticEncounterStatsRecord Evs,
    SwShStaticEncounterStatsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    IReadOnlyList<SwShStaticEncounterMoveRecord> Moves,
    SwShStaticEncounterProvenance Provenance);

public sealed record SwShStaticEncounterEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShStaticEncounterEditableFieldOption> Options)
{
    public SwShStaticEncounterEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShStaticEncounterEditableFieldOption>())
    {
    }
}

public sealed record SwShStaticEncounterEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShStaticEncountersWorkflowStats(
    int TotalEncounterCount,
    int GigantamaxEncounterCount,
    int FixedIvEncounterCount,
    int SourceFileCount);

public sealed record SwShStaticEncountersWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShStaticEncounterEntry> Encounters,
    IReadOnlyList<SwShStaticEncounterEditableField> EditableFields,
    SwShStaticEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
