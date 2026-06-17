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
    private const string WorkflowLabel = "Wild Encounters";
    private const string WorkflowDescription = "Edit Scarlet/Violet wild encounter rows.";

    private static readonly IReadOnlyList<SwShEncounterEditableField> BaseEditableFields =
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

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SwShWorkflowIds.Encounters,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SwShEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var tables = Array.Empty<SwShEncounterTableRecord>();
        var labels = SvTextLabelLookup.None();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
            source = fileSource.Read(project, SvDataPaths.WildEncounterArray);
            tables = LoadTables(source, labels).ToArray();
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
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SwShEncountersWorkflow(
            summary,
            tables,
            CreateEditableFields(labels),
            new SwShEncountersWorkflowStats(
                tables.Length,
                tables.Sum(table => table.Slots.Count),
                source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SwShEncounterTableRecord> LoadTables(
        SvWorkflowFile source,
        SvTextLabelLookup labels)
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

        foreach (var group in rows.GroupBy(row => SvEncounterGrouping.CreateGroupKey(row.Data), StringComparer.Ordinal))
        {
            var first = group.First().Data;
            var slots = group
                .Select((row, slotIndex) => ToSlot(slotIndex, row.Data, labels))
                .ToArray();

            yield return new SwShEncounterTableRecord(
                group.Key,
                SvEncounterGrouping.FormatLocation(first, labels),
                SvEncounterGrouping.FormatDisplayArea(first, labels),
                SvEncounterGrouping.FormatEncounterType(first),
                SvEncounterGrouping.FormatVersions(first.Versiontable),
                source.RelativePath,
                slots,
                new SwShEncounterProvenance(source.RelativePath, source.SourceLayer, source.FileState));
        }
    }

    private static SwShEncounterSlotRecord ToSlot(
        int slot,
        global::EncountPokeData row,
        SvTextLabelLookup labels)
    {
        var speciesId = (int)row.Devid;
        return new SwShEncounterSlotRecord(
            slot,
            speciesId,
            labels.Pokemon(speciesId),
            row.Formno,
            row.Minlevel,
            row.Maxlevel,
            row.Lotvalue,
            SvEncounterGrouping.FormatTimes(row.Timetable),
            SvEncounterGrouping.FormatBiomes(row));
    }

    private static IReadOnlyList<SwShEncounterEditableField> CreateEditableFields(SvTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        return BaseEditableFields
            .Select(field => field.Field == SwShEncountersWorkflowService.SpeciesIdField
                ? field with
                {
                    MaximumValue = speciesOptions.Count > 0 ? speciesOptions.Max(option => option.Value) : field.MaximumValue,
                    Options = speciesOptions,
                }
                : field)
            .ToArray();
    }

    private static IReadOnlyList<SwShEncounterEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SwShEncounterEditableFieldOption(
                    value,
                    $"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }
}
