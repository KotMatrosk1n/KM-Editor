// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using System.Globalization;

namespace KM.SwSh.Pokemon;

public enum SwShAbilityOptionMode
{
    DefaultPlusSlots,
    ZeroBasedSlots,
    Roll
}

public sealed record SwShAbilitySlotOption(int Value, string Label);

public sealed class SwShPokemonAbilityOptionResolver
{
    private readonly IReadOnlyList<SwShPersonalRecord> records;
    private readonly IReadOnlyList<string> abilityNames;

    public static SwShPokemonAbilityOptionResolver Empty { get; } = new([], []);

    private SwShPokemonAbilityOptionResolver(
        IReadOnlyList<SwShPersonalRecord> records,
        IReadOnlyList<string> abilityNames)
    {
        this.records = records;
        this.abilityNames = abilityNames;
    }

    public static SwShPokemonAbilityOptionResolver Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new SwShPokemonAbilityOptionResolver(
            LoadPersonalRecords(project),
            LoadEnglishAbilityNames(project));
    }

    public IReadOnlyList<SwShAbilitySlotOption> CreateOptions(
        int speciesId,
        int form,
        SwShAbilityOptionMode mode)
    {
        var abilities = ResolveAbilities(speciesId, form);

        return mode switch
        {
            SwShAbilityOptionMode.ZeroBasedSlots =>
            [
                new(0, FormatSlotLabel("Ability 1", abilities.Ability1)),
                new(1, FormatSlotLabel("Ability 2", abilities.Ability2)),
                new(2, FormatSlotLabel("Hidden Ability", abilities.HiddenAbility)),
            ],
            SwShAbilityOptionMode.Roll =>
            [
                new(0, FormatSlotLabel("Ability 1", abilities.Ability1)),
                new(1, FormatSlotLabel("Ability 2", abilities.Ability2)),
                new(2, FormatSlotLabel("Hidden Ability", abilities.HiddenAbility)),
                new(3, FormatMultiSlotLabel("Ability 1 or 2", abilities.Ability1, abilities.Ability2)),
                new(4, FormatMultiSlotLabel("Any Ability", abilities.Ability1, abilities.Ability2, abilities.HiddenAbility)),
            ],
            _ =>
            [
                new(0, FormatSlotLabel("Default", abilities.Ability1)),
                new(1, FormatSlotLabel("Ability 1", abilities.Ability1)),
                new(2, FormatSlotLabel("Ability 2", abilities.Ability2)),
                new(3, FormatSlotLabel("Hidden Ability", abilities.HiddenAbility)),
            ],
        };
    }

    private AbilitySet ResolveAbilities(int speciesId, int form)
    {
        if ((uint)speciesId >= (uint)records.Count)
        {
            return AbilitySet.Empty;
        }

        var record = records[speciesId];
        if (form > 0 && record.FormStatsIndex > 0)
        {
            var formPersonalId = record.FormStatsIndex + form - 1;
            if ((uint)formPersonalId < (uint)records.Count)
            {
                record = records[formPersonalId];
            }
        }

        return new AbilitySet(
            FormatAbility(record.Ability1),
            FormatAbility(record.Ability2),
            FormatAbility(record.HiddenAbility));
    }

    private string FormatAbility(int abilityId)
    {
        var name = (uint)abilityId < (uint)abilityNames.Count
            && !string.IsNullOrWhiteSpace(abilityNames[abilityId])
            ? abilityNames[abilityId]
            : abilityId == 0
                ? "None"
                : string.Create(CultureInfo.InvariantCulture, $"Ability {abilityId}");

        return string.Create(CultureInfo.InvariantCulture, $"{abilityId:000} {name}");
    }

    private static string FormatSlotLabel(string slot, string ability)
    {
        return string.Equals(ability, AbilitySet.Unknown, StringComparison.Ordinal)
            ? slot
            : $"{slot} - {ability}";
    }

    private static string FormatMultiSlotLabel(string slot, params string[] abilities)
    {
        var knownAbilities = abilities
            .Where(ability => !string.Equals(ability, AbilitySet.Unknown, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return knownAbilities.Length == 0
            ? slot
            : $"{slot} - {string.Join(" / ", knownAbilities)}";
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> LoadEnglishAbilityNames(OpenedProject project)
    {
        var source = ResolveWorkflowFile(project, SwShPokemonWorkflowService.EnglishAbilityNamePath);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed record AbilitySet(
        string Ability1,
        string Ability2,
        string HiddenAbility)
    {
        public const string Unknown = "Unknown Ability";
        public static AbilitySet Empty { get; } = new(Unknown, Unknown, Unknown);
    }

    private sealed record WorkflowFileSource(string AbsolutePath);
}
