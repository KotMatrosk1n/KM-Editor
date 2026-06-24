// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.Trainers;

internal sealed record ZaTrainerAbilitySet(string Ability1, string Ability2, string HiddenAbility)
{
    public static readonly ZaTrainerAbilitySet Empty = new("Ability 1", "Ability 2", "Hidden Ability");
}

internal sealed class ZaTrainerAbilityResolver
{
    private readonly IReadOnlyDictionary<(int Species, int Form), ZaTrainerAbilitySet> abilitiesBySpeciesForm;
    private readonly ZaTextLabelLookup labels;

    private ZaTrainerAbilityResolver(
        IReadOnlyDictionary<(int Species, int Form), ZaTrainerAbilitySet> abilitiesBySpeciesForm,
        ZaTextLabelLookup labels)
    {
        this.abilitiesBySpeciesForm = abilitiesBySpeciesForm;
        this.labels = labels;
    }

    public static ZaTrainerAbilityResolver Load(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        ZaTextLabelLookup labels,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(diagnostics);

        try
        {
            var source = fileSource.Read(project, ZaDataPaths.PersonalArray);
            var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(source.Bytes));
            var records = new Dictionary<(int Species, int Form), ZaTrainerAbilitySet>();

            for (var index = 0; index < table.EntryLength; index++)
            {
                var entry = table.Entry(index);
                var species = entry?.Species;
                if (entry is null || species is null || species.Value.Species == 0)
                {
                    continue;
                }

                records[(species.Value.Species, species.Value.Form)] = new ZaTrainerAbilitySet(
                    labels.Ability(entry.Value.Ability1),
                    labels.Ability(entry.Value.Ability2),
                    labels.Ability(entry.Value.AbilityHidden));
            }

            return new ZaTrainerAbilityResolver(records, labels);
        }
        catch (FileNotFoundException)
        {
            return new ZaTrainerAbilityResolver(
                new Dictionary<(int Species, int Form), ZaTrainerAbilitySet>(),
                labels);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Pokemon Legends Z-A trainer ability labels could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.PersonalArray}"));
            return new ZaTrainerAbilityResolver(
                new Dictionary<(int Species, int Form), ZaTrainerAbilitySet>(),
                labels);
        }
    }

    public ZaTrainerAbilitySet Resolve(int speciesId, int form)
    {
        if (abilitiesBySpeciesForm.TryGetValue((speciesId, form), out var abilities)
            || abilitiesBySpeciesForm.TryGetValue((speciesId, 0), out abilities))
        {
            return abilities;
        }

        return ZaTrainerAbilitySet.Empty;
    }

    public string FormatAbilityMode(int value, ZaTrainerAbilitySet abilities)
    {
        return ZaTrainersWorkflowService.CreateAbilityModeOptions(abilities)
            .FirstOrDefault(option => option.Value == value)?.Label
            ?? labels.Ability(value);
    }
}
