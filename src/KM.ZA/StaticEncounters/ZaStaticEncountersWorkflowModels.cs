// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.StaticEncounters;

public sealed record ZaStaticEncounterProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaStaticEncounterStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record ZaStaticEncounterMoveRecord(
    int Slot,
    int MoveId,
    string? Move);

public sealed record ZaStaticEncounterEntry(
    int EncounterIndex,
    int SourceIndex,
    string CategoryId,
    string CategoryLabel,
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
    ZaStaticEncounterStatsRecord Evs,
    ZaStaticEncounterStatsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    IReadOnlyList<ZaStaticEncounterMoveRecord> Moves,
    ZaStaticEncounterProvenance Provenance,
    IReadOnlyList<string> SupportedFields,
    IReadOnlyDictionary<string, string> FieldValues,
    IReadOnlyDictionary<string, string> FieldDisplayValues,
    IReadOnlyDictionary<string, bool> FieldReadOnly,
    IReadOnlyList<ZaStaticEncounterEditableFieldOption> AbilityOptions);

public sealed record ZaStaticEncounterEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaStaticEncounterEditableFieldOption> Options,
    string Group,
    bool IsReadOnly = false,
    string Description = "");

public sealed record ZaStaticEncounterEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaStaticEncountersWorkflowStats(
    int TotalEncounterCount,
    int FixedIvEncounterCount,
    int SourceFileCount,
    int PokemonDataEncounterCount);

public sealed record ZaStaticEncountersWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaStaticEncounterEntry> Encounters,
    IReadOnlyList<ZaStaticEncounterEditableField> EditableFields,
    ZaStaticEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaStaticEncountersEditResult(
    ZaStaticEncountersWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
