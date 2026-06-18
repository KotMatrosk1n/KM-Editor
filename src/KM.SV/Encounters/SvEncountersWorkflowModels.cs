// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Encounters;

public sealed record SvEncounterProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvEncounterSlotRecord(
    int Slot,
    int SpeciesId,
    string Species,
    int Form,
    int LevelMin,
    int LevelMax,
    int Weight,
    string? TimeOfDay,
    string Weather);

public sealed record SvEncounterTableRecord(
    string TableId,
    string Location,
    string Area,
    string EncounterType,
    string GameVersion,
    string ArchiveMember,
    IReadOnlyList<SvEncounterSlotRecord> Slots,
    SvEncounterProvenance Provenance);

public sealed record SvEncounterEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvEncounterEditableFieldOption> Options)
{
    public SvEncounterEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SvEncounterEditableFieldOption>())
    {
    }
}

public sealed record SvEncounterEditableFieldOption(
    int Value,
    string Label);

public sealed record SvEncountersWorkflowStats(
    int TotalTableCount,
    int TotalSlotCount,
    int SourceFileCount);

public sealed record SvEncountersWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvEncounterTableRecord> Tables,
    IReadOnlyList<SvEncounterEditableField> EditableFields,
    SvEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
