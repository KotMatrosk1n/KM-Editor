// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Encounters;

public sealed record SwShEncounterProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShEncounterSlotRecord(
    int Slot,
    int SpeciesId,
    string Species,
    int Form,
    int LevelMin,
    int LevelMax,
    int Weight,
    string? TimeOfDay,
    string Weather);

public sealed record SwShEncounterTableRecord(
    string TableId,
    string Location,
    string Area,
    string EncounterType,
    string GameVersion,
    string ArchiveMember,
    IReadOnlyList<SwShEncounterSlotRecord> Slots,
    SwShEncounterProvenance Provenance);

public sealed record SwShEncounterEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShEncounterEditableFieldOption> Options)
{
    public SwShEncounterEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShEncounterEditableFieldOption>())
    {
    }
}

public sealed record SwShEncounterEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShEncountersWorkflowStats(
    int TotalTableCount,
    int TotalSlotCount,
    int SourceFileCount);

public sealed record SwShEncountersWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShEncounterTableRecord> Tables,
    IReadOnlyList<SwShEncounterEditableField> EditableFields,
    SwShEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
