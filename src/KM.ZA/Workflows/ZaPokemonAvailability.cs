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
    private readonly IReadOnlyDictionary<int, IReadOnlyList<int>>? presentFormIdsBySpecies;

    private ZaPokemonAvailability(
        IReadOnlySet<int>? presentSpeciesIds,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? presentFormIdsBySpecies)
    {
        this.presentSpeciesIds = presentSpeciesIds;
        this.presentFormIdsBySpecies = presentFormIdsBySpecies;
    }

    public static ZaPokemonAvailability Unfiltered { get; } = new(null, null);

    public bool HasKnownAvailability => presentFormIdsBySpecies is not null;

    public bool IsPresentSpeciesForm(int speciesId, int form)
    {
        return presentFormIdsBySpecies is not null
            && presentFormIdsBySpecies.TryGetValue(speciesId, out var formIds)
            && formIds.Contains(form);
    }

    public bool AllowsSpeciesForm(int speciesId, int form)
    {
        return !HasKnownAvailability || IsPresentSpeciesForm(speciesId, form);
    }

    public bool TryGetPresentFormIds(int speciesId, out IReadOnlyList<int> formIds)
    {
        if (presentFormIdsBySpecies is null)
        {
            formIds = Array.Empty<int>();
            return false;
        }

        formIds = presentFormIdsBySpecies.TryGetValue(speciesId, out var presentFormIds)
            ? presentFormIds
            : Array.Empty<int>();
        return true;
    }

    public IReadOnlyList<TOption> CreateFormOptions<TOption>(
        int speciesId,
        Func<int, TOption> createOption)
    {
        ArgumentNullException.ThrowIfNull(createOption);

        return TryGetPresentFormIds(speciesId, out var formIds)
            ? formIds.Select(createOption).ToArray()
            : Array.Empty<TOption>();
    }

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
        var formIdsBySpecies = new Dictionary<int, HashSet<int>>();
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
                if (!formIdsBySpecies.TryGetValue(species.Species, out var formIds))
                {
                    formIds = [];
                    formIdsBySpecies.Add(species.Species, formIds);
                }

                formIds.Add(species.Form);
            }
        }

        var sortedFormIdsBySpecies = formIdsBySpecies.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<int>)entry.Value.Order().ToArray());

        return new ZaPokemonAvailability(speciesIds, sortedFormIdsBySpecies);
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
