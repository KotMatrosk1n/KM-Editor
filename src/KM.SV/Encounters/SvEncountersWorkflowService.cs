// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Encounters;

internal sealed class SvEncountersWorkflowService
{
    private const string WorkflowLabel = "Wild Encounters";
    private const string WorkflowDescription = "Edit Scarlet/Violet wild encounter rows.";
    public const string SpeciesIdField = "speciesId";
    public const string FormField = "form";
    public const string ProbabilityField = "probability";
    public const string LevelMinField = "levelMin";
    public const string LevelMaxField = "levelMax";

    private static readonly IReadOnlyList<SvEncounterEditableField> BaseEditableFields =
    [
        new(SvEncountersWorkflowService.SpeciesIdField, "Species", "integer", 0, ushort.MaxValue),
        new(SvEncountersWorkflowService.FormField, "Form", "integer", sbyte.MinValue, sbyte.MaxValue),
        new(SvEncountersWorkflowService.ProbabilityField, "Lot weight", "integer", short.MinValue, short.MaxValue),
        new(SvEncountersWorkflowService.LevelMinField, "Min Level", "integer", 0, 100),
        new(SvEncountersWorkflowService.LevelMaxField, "Max Level", "integer", 0, 100),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvEncountersWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Encounters,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var tables = Array.Empty<SvEncounterTableRecord>();
        var labels = SvTextLabelLookup.None();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            source = fileSource.Read(project, SvDataPaths.WildEncounterArray);
            tables = LoadTables(source, labels, project.Paths.SelectedGame).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Wild Encounters could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.WildEncounterArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Encounters,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvEncountersWorkflow(
            summary,
            tables,
            CreateEditableFields(labels),
            new SvEncountersWorkflowStats(
                tables.Length,
                tables.Sum(table => table.Slots.Count),
                source is null ? 0 : 1),
            diagnostics);
    }

    private static IEnumerable<SvEncounterTableRecord> LoadTables(
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        ProjectGame? selectedGame)
    {
        var table = global::EncountPokeDataArray.GetRootAsEncountPokeDataArray(new ByteBuffer(source.Bytes));
        var rows = new List<(int Index, global::EncountPokeData Data)>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null && IsAvailableForSelectedGame(row.Value.Versiontable, selectedGame))
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

            yield return new SvEncounterTableRecord(
                group.Key,
                SvEncounterGrouping.FormatLocation(first, labels),
                SvEncounterGrouping.FormatDisplayArea(first, labels),
                SvEncounterGrouping.FormatEncounterType(first),
                SvEncounterGrouping.FormatVersions(first.Versiontable),
                source.RelativePath,
                slots,
                new SvEncounterProvenance(source.RelativePath, source.SourceLayer, source.FileState));
        }
    }

    private static bool IsAvailableForSelectedGame(global::VersionTable? version, ProjectGame? selectedGame)
    {
        if (selectedGame is not ProjectGame.Scarlet and not ProjectGame.Violet)
        {
            return true;
        }

        if (version is null || (version.Value.A && version.Value.B) || (!version.Value.A && !version.Value.B))
        {
            return true;
        }

        return selectedGame == ProjectGame.Scarlet ? version.Value.A : version.Value.B;
    }

    internal static string FormatEncounterSpeciesLabel(
        int speciesId,
        int form,
        SvTextLabelLookup labels)
    {
        return FormatEncounterSpeciesLabel(speciesId, form, labels.Pokemon(speciesId));
    }

    internal static string FormatEncounterSpeciesLabel(
        int speciesId,
        int form,
        string speciesName)
    {
        if (speciesId == 0)
        {
            return "Empty";
        }

        return form == 0
            ? speciesName
            : string.Create(CultureInfo.InvariantCulture, $"{speciesName} (Form {form})");
    }

    private static SvEncounterSlotRecord ToSlot(
        int slot,
        global::EncountPokeData row,
        SvTextLabelLookup labels)
    {
        var speciesId = (int)row.Devid;
        return new SvEncounterSlotRecord(
            slot,
            speciesId,
            FormatEncounterSpeciesLabel(speciesId, row.Formno, labels),
            row.Formno,
            row.Minlevel,
            row.Maxlevel,
            row.Lotvalue,
            SvEncounterGrouping.FormatTimes(row.Timetable),
            SvEncounterGrouping.FormatSlotContext(row, labels));
    }

    private static IReadOnlyList<SvEncounterEditableField> CreateEditableFields(SvTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        return BaseEditableFields
            .Select(field => field.Field == SvEncountersWorkflowService.SpeciesIdField
                ? field with
                {
                    MaximumValue = speciesOptions.Count > 0 ? speciesOptions.Max(option => option.Value) : field.MaximumValue,
                    Options = speciesOptions,
                }
                : field)
            .ToArray();
    }

    private static IReadOnlyList<SvEncounterEditableFieldOption> CreateIndexedOptions(
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
                return new SvEncounterEditableFieldOption(
                    value,
                    $"{value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }
}
