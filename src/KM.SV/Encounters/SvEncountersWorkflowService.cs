// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.Encounters;
using KM.SwSh.Workflows;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Encounters;

internal sealed class SvEncountersWorkflowService
{
    private static readonly IReadOnlyList<SwShEncounterEditableField> EditableFields =
    [
        new(SwShEncountersWorkflowService.SpeciesIdField, "Species", "integer", 0, ushort.MaxValue),
        new(SwShEncountersWorkflowService.FormField, "Form", "integer", sbyte.MinValue, sbyte.MaxValue),
        new(SwShEncountersWorkflowService.ProbabilityField, "Lot weight", "integer", short.MinValue, short.MaxValue),
        new(SwShEncountersWorkflowService.LevelMinField, "Min Level", "integer", 0, 100),
        new(SwShEncountersWorkflowService.LevelMaxField, "Max Level", "integer", 0, 100),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvEncountersWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SwShEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var tables = Array.Empty<SwShEncounterTableRecord>();

        try
        {
            source = fileSource.Read(project, SvDataPaths.WildEncounterArray);
            tables = LoadTables(source).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Wild Encounters could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.WildEncounterArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SwShWorkflowIds.Encounters,
            "Wild Encounters",
            "Edit Scarlet/Violet wild encounter rows.",
            diagnostics.Count == 0 ? null : diagnostics);

        return new SwShEncountersWorkflow(
            summary,
            tables,
            EditableFields,
            new SwShEncountersWorkflowStats(
                tables.Length,
                tables.Sum(table => table.Slots.Count),
                source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SwShEncounterTableRecord> LoadTables(SvWorkflowFile source)
    {
        var table = global::EncountPokeDataArray.GetRootAsEncountPokeDataArray(new ByteBuffer(source.Bytes));
        var rows = new List<(int Index, global::EncountPokeData Data)>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add((index, row.Value));
            }
        }

        foreach (var group in rows.GroupBy(row => CreateGroupKey(row.Data), StringComparer.Ordinal))
        {
            var first = group.First().Data;
            var slots = group
                .Select((row, slotIndex) => ToSlot(slotIndex, row.Data))
                .ToArray();

            yield return new SwShEncounterTableRecord(
                group.Key,
                string.IsNullOrWhiteSpace(first.LocationName) ? "Unknown Location" : first.LocationName,
                string.IsNullOrWhiteSpace(first.Area) ? "Unknown Area" : first.Area,
                FormatEncounterType(first),
                FormatVersions(first.Versiontable),
                source.RelativePath,
                slots,
                new SwShEncounterProvenance(source.RelativePath, source.SourceLayer, source.FileState));
        }
    }

    private static SwShEncounterSlotRecord ToSlot(int slot, global::EncountPokeData row)
    {
        var speciesId = (int)row.Devid;
        return new SwShEncounterSlotRecord(
            slot,
            speciesId,
            SvLabels.Pokemon(speciesId),
            row.Formno,
            row.Minlevel,
            row.Maxlevel,
            row.Lotvalue,
            FormatTimes(row.Timetable),
            FormatBiomes(row));
    }

    private static string CreateGroupKey(global::EncountPokeData row)
    {
        var location = string.IsNullOrWhiteSpace(row.LocationName) ? "location" : row.LocationName;
        var area = string.IsNullOrWhiteSpace(row.Area) ? "area" : row.Area;
        return $"{location}:{area}";
    }

    private static string FormatEncounterType(global::EncountPokeData row)
    {
        var enable = row.Enabletable;
        if (enable is null)
        {
            return "Wild";
        }

        var parts = new List<string>();
        if (enable.Value.Land)
        {
            parts.Add("Land");
        }

        if (enable.Value.UpWater)
        {
            parts.Add("Water");
        }

        if (enable.Value.Underwater)
        {
            parts.Add("Underwater");
        }

        if (enable.Value.Air1 || enable.Value.Air2)
        {
            parts.Add("Air");
        }

        return parts.Count == 0 ? "Wild" : string.Join(", ", parts);
    }

    private static string FormatTimes(global::TimeTable? table)
    {
        if (table is null)
        {
            return "Any";
        }

        var parts = new List<string>();
        if (table.Value.Morning)
        {
            parts.Add("Morning");
        }

        if (table.Value.Noon)
        {
            parts.Add("Noon");
        }

        if (table.Value.Evening)
        {
            parts.Add("Evening");
        }

        if (table.Value.Night)
        {
            parts.Add("Night");
        }

        return parts.Count == 0 ? "Any" : string.Join(", ", parts);
    }

    private static string FormatVersions(global::VersionTable? table)
    {
        if (table is null || (table.Value.A && table.Value.B) || (!table.Value.A && !table.Value.B))
        {
            return "Scarlet/Violet";
        }

        return table.Value.A ? "Scarlet" : "Violet";
    }

    private static string FormatBiomes(global::EncountPokeData row)
    {
        var biomes = new[]
        {
            (row.Biome1, row.Lotvalue1),
            (row.Biome2, row.Lotvalue2),
            (row.Biome3, row.Lotvalue3),
            (row.Biome4, row.Lotvalue4),
        };

        var parts = biomes
            .Where(biome => biome.Item1 != global::Biome.NONE || biome.Item2 != 0)
            .Select(biome => $"{SvLabels.EnumName(biome.Item1)} {biome.Item2}")
            .ToArray();

        return parts.Length == 0 ? "Any" : string.Join(", ", parts);
    }
}
