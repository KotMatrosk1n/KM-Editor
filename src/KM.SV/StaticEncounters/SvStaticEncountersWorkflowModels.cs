// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.StaticEncounters;

public sealed record SvStaticEncounterProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvStaticEncounterStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SvStaticEncounterMoveRecord(
    int Slot,
    int MoveId,
    string? Move);

public sealed record SvStaticEncounterEntry(
    int EncounterIndex,
    string ObjectId,
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
    int DynamaxLevel,
    bool CanGigantamax,
    SvStaticEncounterStatsRecord Evs,
    SvStaticEncounterStatsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    IReadOnlyList<SvStaticEncounterMoveRecord> Moves,
    SvStaticEncounterProvenance Provenance,
    IReadOnlyList<string> SupportedFields,
    IReadOnlyDictionary<string, string> FieldValues,
    IReadOnlyDictionary<string, string> FieldDisplayValues,
    IReadOnlyDictionary<string, bool> FieldReadOnly,
    IReadOnlyList<SvStaticEncounterEditableFieldOption> AbilityOptions);

public sealed record SvStaticEncounterEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvStaticEncounterEditableFieldOption> Options,
    string Group,
    bool IsReadOnly = false,
    string Description = "");

public sealed record SvStaticEncounterEditableFieldOption(
    int Value,
    string Label);

public sealed record SvStaticEncountersWorkflowStats(
    int TotalEncounterCount,
    int GigantamaxEncounterCount,
    int FixedIvEncounterCount,
    int SourceFileCount,
    int FixedSymbolCount,
    int CoinSymbolCount);

public sealed record SvStaticEncountersWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvStaticEncounterEntry> Encounters,
    IReadOnlyList<SvStaticEncounterEditableField> EditableFields,
    SvStaticEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvStaticEncountersEditResult(
    SvStaticEncountersWorkflow Workflow,
    KM.Core.Editing.EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
