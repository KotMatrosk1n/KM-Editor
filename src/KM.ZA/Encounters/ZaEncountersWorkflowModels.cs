// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Encounters;

public sealed record ZaEncounterProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaBossBattleContext(
    string Key,
    string Label,
    int Rank);

public sealed record ZaEncounterSlotRecord(
    int Slot,
    int PokemonDataSourceIndex,
    string? EncounterRecordId,
    string EncounterDataId,
    int SpeciesId,
    string Species,
    int Form,
    int LevelMin,
    int LevelMax,
    int Weight,
    string? TimeOfDay,
    string Weather,
    bool IsAlpha,
    string EncounterKind,
    ZaEncounterProvenance PokemonProvenance,
    bool? ContributesToWildZoneCompletion,
    int? AlphaChancePercent,
    int? AlphaLevelBonus,
    bool HasAlphaChance)
{
    public IReadOnlyList<ZaEncounterEditableFieldOption> FormOptions { get; init; } =
        Array.Empty<ZaEncounterEditableFieldOption>();

    public int SlotMaxCount { get; init; }

    public bool CanEditWeight { get; init; }

    public bool CanEditSlotMaxCount { get; init; }

    public int? AppearanceMinCount { get; init; }

    public int? AppearanceMaxCount { get; init; }

    public int AppearanceObjectCount { get; init; }

    public bool CanEditAppearanceCounts { get; init; }
}

public sealed record ZaEncounterTableRecord(
    string TableId,
    string Location,
    string Area,
    string EncounterType,
    string GameVersion,
    string ArchiveMember,
    IReadOnlyList<ZaEncounterSlotRecord> Slots,
    ZaEncounterProvenance Provenance,
    string? LocationKey = null,
    int? LocationSort = null,
    string? TableLabel = null,
    string? TableDetails = null,
    string? LocationDetails = null)
{
    public string? SpawnerCategory { get; init; }

    public string? RawSpawnerId { get; init; }

    public bool IsPostgame { get; init; }

    public string? BossBattleContextKey { get; init; }

    public string? BossBattleContextLabel { get; init; }

    public int? BossBattleContextRank { get; init; }

    public string? BossBattleWaveLabel { get; init; }

    public int? BossBattleWaveRank { get; init; }

    public IReadOnlyList<ZaBossBattleContext>? BossBattleContexts { get; init; }
}

public sealed record ZaEncounterEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaEncounterEditableFieldOption> Options);

public sealed record ZaEncounterEditableFieldOption(
    int Value,
    string Label)
{
    public IReadOnlyList<ZaEncounterEditableFieldOption>? FormOptions { get; init; }
}

public sealed record ZaEncountersWorkflowStats(
    int TotalTableCount,
    int TotalSlotCount,
    int SourceFileCount);

public sealed record ZaEncountersWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaEncounterTableRecord> Tables,
    IReadOnlyList<ZaEncounterEditableField> EditableFields,
    ZaEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    internal ZaPokemonAvailability PokemonAvailability { get; init; } =
        ZaPokemonAvailability.Unfiltered;
}

public sealed record ZaEncounterSlotFieldUpdate(
    string TableId,
    int Slot,
    string Field,
    string Value);

public sealed record ZaEncountersEditResult(
    ZaEncountersWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
