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
    string Species,
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
    IReadOnlyList<SwShEncounterSlotRecord> Slots,
    SwShEncounterProvenance Provenance);

public sealed record SwShEncountersWorkflowStats(
    int TotalTableCount,
    int TotalSlotCount,
    int SourceFileCount);

public sealed record SwShEncountersWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShEncounterTableRecord> Tables,
    SwShEncountersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
