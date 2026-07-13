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
    bool HasAlphaChance);

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
    string? TableDetails = null);

public sealed record ZaEncounterEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaEncounterEditableFieldOption> Options);

public sealed record ZaEncounterEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaEncountersWorkflowStats(
    int TotalTableCount,
    int TotalSlotCount,
    int SourceFileCount);

public sealed record ZaEncountersWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaEncounterTableRecord> Tables,
    IReadOnlyList<ZaEncounterEditableField> EditableFields,
    ZaEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaEncounterSlotFieldUpdate(
    string TableId,
    int Slot,
    string Field,
    string Value);

public sealed record ZaEncountersEditResult(
    ZaEncountersWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
