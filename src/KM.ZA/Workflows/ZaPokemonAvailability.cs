// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using System.Globalization;

namespace KM.ZA.Workflows;

internal sealed class ZaPokemonAvailability
{
    private readonly IReadOnlySet<int>? presentSpeciesIds;

    private ZaPokemonAvailability(IReadOnlySet<int>? presentSpeciesIds)
    {
        this.presentSpeciesIds = presentSpeciesIds;
    }

    public static ZaPokemonAvailability Unfiltered { get; } = new(null);

    public static ZaPokemonAvailability Load(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        ICollection<ValidationDiagnostic> diagnostics,
        string workflowLabel)
    {
        try
        {
            var source = fileSource.Read(project, ZaDataPaths.PersonalArray);
            return FromPersonalArray(source.Bytes);
        }
        catch (FileNotFoundException)
        {
            return Unfiltered;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"{workflowLabel} Pokemon availability could not be resolved from Pokemon Data: {exception.Message}",
                $"romfs/{ZaDataPaths.PersonalArray}"));
            return Unfiltered;
        }
    }

    public IReadOnlyList<(int Value, string Label)> CreateSpeciesOptions(
        int labelCount,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var options = new List<(int Value, string Label)>();
        if (includeNone)
        {
            options.Add((0, "0 None"));
        }

        var values = presentSpeciesIds is null
            ? EnumerateLabelSpecies(labelCount, includeNone)
            : presentSpeciesIds.Where(value => value > 0).Order();

        foreach (var value in values)
        {
            options.Add((value, $"{value.ToString(CultureInfo.InvariantCulture)} {resolveName(value)}"));
        }

        return options;
    }

    private static ZaPokemonAvailability FromPersonalArray(byte[] bytes)
    {
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        var speciesIds = new HashSet<int>();
        for (var index = 0; index < table.EntryLength; index++)
        {
            var row = table.Entry(index);
            if (row is null || !row.Value.IsPresent || row.Value.Species is not { } species)
            {
                continue;
            }

            if (species.Species > 0)
            {
                speciesIds.Add(species.Species);
            }
        }

        return new ZaPokemonAvailability(speciesIds);
    }

    private static IEnumerable<int> EnumerateLabelSpecies(
        int labelCount,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (labelCount <= firstValue)
        {
            return [];
        }

        return Enumerable.Range(firstValue, labelCount - firstValue)
            .Where(value => value > 0);
    }
}
