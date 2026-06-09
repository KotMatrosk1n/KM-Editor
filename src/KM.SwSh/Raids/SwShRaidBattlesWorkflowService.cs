// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Raids;

public sealed class SwShRaidBattlesWorkflowService
{
    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string AbilityField = "ability";
    public const string IsGigantamaxField = "isGigantamax";
    public const string GenderField = "gender";
    public const string FlawlessIvsField = "flawlessIvs";
    public const string Star1ProbabilityField = "star1Probability";
    public const string Star2ProbabilityField = "star2Probability";
    public const string Star3ProbabilityField = "star3Probability";
    public const string Star4ProbabilityField = "star4Probability";
    public const string Star5ProbabilityField = "star5Probability";
    public const string EncounterMemberName = "nest_hole_encount.bin";

    internal const string RaidBattlesEditDomain = "workflow.raidBattles";

    private const string MessageRootPath = "romfs/bin/message";
    private const string PreferredLanguage = "English";
    private const int MinimumValue = 0;

    private static readonly IReadOnlyList<SwShRaidBattleEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShRaidBattleEditableFieldOption> AbilityOptions =
    [
        new(0, "Ability 1"),
        new(1, "Ability 2"),
        new(2, "Hidden Ability"),
        new(3, "Ability 1 or 2"),
        new(4, "Any Ability"),
    ];

    private static readonly IReadOnlyList<SwShRaidBattleEditableFieldOption> GenderOptions =
    [
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
        new(3, "Genderless"),
    ];

    private static readonly IReadOnlyList<SwShRaidBattleEditableFieldOption> FlawlessIvOptions =
    [
        new(0, "Random IVs"),
        new(1, "1 Guaranteed Perfect IV"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SwShRaidBattleEditableFieldOption> FormOptions =
    [
        new(0, "Base"),
        ..Enumerable.Range(1, 31).Select(value => new SwShRaidBattleEditableFieldOption(value, $"Form {value}")),
    ];

    private static readonly IReadOnlyList<SwShRaidBattleEditableField> BaseEditableFields =
    [
        CreateField(SpeciesField, "Species", "integer", MinimumValue, SwShEncounterNestArchive.MaximumSpeciesId),
        CreateField(FormField, "Form", "integer", MinimumValue, 31, FormOptions),
        CreateField(AbilityField, "Ability roll", "integer", MinimumValue, SwShEncounterNestArchive.MaximumAbility, AbilityOptions),
        CreateField(IsGigantamaxField, "Gigantamax", "boolean", MinimumValue, 1, BooleanOptions),
        CreateField(GenderField, "Gender", "integer", MinimumValue, SwShEncounterNestArchive.MaximumGender, GenderOptions),
        CreateField(FlawlessIvsField, "Guaranteed perfect IVs", "integer", MinimumValue, SwShEncounterNestArchive.MaximumFlawlessIvs, FlawlessIvOptions),
        CreateField(Star1ProbabilityField, "1-star probability", "integer", MinimumValue, SwShEncounterNestArchive.MaximumProbability),
        CreateField(Star2ProbabilityField, "2-star probability", "integer", MinimumValue, SwShEncounterNestArchive.MaximumProbability),
        CreateField(Star3ProbabilityField, "3-star probability", "integer", MinimumValue, SwShEncounterNestArchive.MaximumProbability),
        CreateField(Star4ProbabilityField, "4-star probability", "integer", MinimumValue, SwShEncounterNestArchive.MaximumProbability),
        CreateField(Star5ProbabilityField, "5-star probability", "integer", MinimumValue, SwShEncounterNestArchive.MaximumProbability),
    ];

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Raid Battles requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShRaidBattlesWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var dataSource = SwShRaidRewardsWorkflowService.ResolveNestDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Raid Battles data is not available for this project.",
                expected: SwShRaidRewardsWorkflowService.NestDataPath));
            return CreateWorkflow(summary, [], sourceFileCount: 0, CreateEmptyLookupTables(), diagnostics);
        }

        var lookupTables = LoadLookupTables(project, diagnostics);

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
            if (!pack.TryGetFileByName(EncounterMemberName, out var memberData))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Raid Battles source does not contain member '{EncounterMemberName}'.",
                    file: dataSource.GraphEntry.RelativePath,
                    expected: EncounterMemberName));
                return CreateWorkflow(summary, [], sourceFileCount: 1 + lookupTables.SourceFileCount, lookupTables, diagnostics);
            }

            var archive = SwShEncounterNestArchive.Parse(memberData);
            var provenance = CreateProvenance(dataSource.GraphEntry);
            var rewardLinks = LoadRewardLinks(project);
            var tables = FlattenArchive(archive, provenance, lookupTables, rewardLinks);

            return CreateWorkflow(summary, tables, sourceFileCount: 1 + lookupTables.SourceFileCount, lookupTables, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles source is not supported: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: $"Sword/Shield data_table.gfpak with {EncounterMemberName}"));
            return CreateWorkflow(summary, [], sourceFileCount: 1 + lookupTables.SourceFileCount, lookupTables, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(summary, [], sourceFileCount: 1 + lookupTables.SourceFileCount, lookupTables, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Battles source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(summary, [], sourceFileCount: 1 + lookupTables.SourceFileCount, lookupTables, diagnostics);
        }
    }

    internal static SwShRaidBattleEditableField? GetEditableField(string? field)
    {
        return BaseEditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static bool IsEditableField(string? field)
    {
        return GetEditableField(field) is not null;
    }

    internal static string CreateTableId(int tableIndex, ulong sourceTableId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"raid:{tableIndex}:{sourceTableId:X16}");
    }

    internal static bool TryParseTableId(string? tableId, out int tableIndex, out ulong sourceTableId)
    {
        tableIndex = -1;
        sourceTableId = 0;

        var parts = tableId?.Split(':') ?? [];
        return parts.Length == 3
            && string.Equals(parts[0], "raid", StringComparison.Ordinal)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out tableIndex)
            && tableIndex >= 0
            && ulong.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out sourceTableId);
    }

    internal static string CreateSlotRecordId(string tableId, int slot)
    {
        return $"{tableId}#{slot.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static bool TryParseSlotRecordId(string? recordId, out string tableId, out int slot)
    {
        tableId = string.Empty;
        slot = 0;

        var separatorIndex = recordId?.LastIndexOf('#') ?? -1;
        if (separatorIndex <= 0 || separatorIndex >= recordId!.Length - 1)
        {
            return false;
        }

        tableId = recordId[..separatorIndex];
        return int.TryParse(recordId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 1;
    }

    internal static string FormatHash(ulong value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{value:X16}");
    }

    internal static string GetOptionLabel(
        IReadOnlyList<SwShRaidBattleEditableFieldOption> options,
        int value,
        string fallbackPrefix)
    {
        return options.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static SwShRaidBattlesWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShRaidBattleTableRecord> tables,
        int sourceFileCount,
        RaidBattleLookupTables lookupTables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShRaidBattlesWorkflow(
            summary,
            tables,
            CreateEditableFields(lookupTables),
            new SwShRaidBattlesWorkflowStats(
                tables.Count,
                tables.Sum(table => table.Slots.Count),
                tables.Sum(table => table.Slots.Count(slot => slot.IsGigantamax)),
                sourceFileCount),
            diagnostics);
    }

    private static RaidBattleLookupTables CreateEmptyLookupTables()
    {
        return new RaidBattleLookupTables([], SwShPokemonAbilityOptionResolver.Empty, SourceFileCount: 0);
    }

    private static IReadOnlyList<SwShRaidBattleEditableField> CreateEditableFields(RaidBattleLookupTables lookupTables)
    {
        var speciesOptions = CreateIndexedOptions(lookupTables.SpeciesNames, "Species");

        return BaseEditableFields
            .Select(field => field.Field == SpeciesField
                ? field with { Options = speciesOptions }
                : field)
            .ToArray();
    }

    private static IReadOnlyList<SwShRaidBattleEditableFieldOption> CreateIndexedOptions(
        IReadOnlyList<string> names,
        string fallbackPrefix)
    {
        return names
            .Select((name, index) => new SwShRaidBattleEditableFieldOption(
                index,
                string.IsNullOrWhiteSpace(name)
                    ? $"{index.ToString("000", CultureInfo.InvariantCulture)} {fallbackPrefix} {index}"
                    : $"{index.ToString("000", CultureInfo.InvariantCulture)} {name}"))
            .ToArray();
    }

    private static IReadOnlyList<SwShRaidBattleTableRecord> FlattenArchive(
        SwShEncounterNestArchive archive,
        SwShRaidBattleProvenance provenance,
        RaidBattleLookupTables lookupTables,
        IReadOnlyDictionary<(string RewardKind, string SourceTableHash), SwShRaidBattleRewardLinkRecord> rewardLinks)
    {
        return archive.Tables
            .Select((table, tableIndex) => new SwShRaidBattleTableRecord(
                CreateTableId(tableIndex, table.TableId),
                $"table_{table.TableId:X16}",
                tableIndex,
                FormatGameVersion(table.GameVersion),
                FormatHash(table.TableId),
                table.Entries
                    .Select((entry, entryIndex) => ToSlotRecord(entry, entryIndex, lookupTables, rewardLinks))
                    .ToArray(),
                provenance))
            .ToArray();
    }

    private static SwShRaidBattleSlotRecord ToSlotRecord(
        SwShEncounterNest entry,
        int entryIndex,
        RaidBattleLookupTables lookupTables,
        IReadOnlyDictionary<(string RewardKind, string SourceTableHash), SwShRaidBattleRewardLinkRecord> rewardLinks)
    {
        var probabilities = PadProbabilities(entry.Probabilities, length: 5);
        var dropTableHash = FormatHash(entry.DropTableId);
        var bonusTableHash = FormatHash(entry.BonusTableId);
        return new SwShRaidBattleSlotRecord(
            Slot: entryIndex + 1,
            entry.EntryIndex,
            entry.Species,
            GetIndexedName(entry.Species, lookupTables.SpeciesNames, "Species"),
            entry.Form,
            entry.Ability,
            GetAbilityOptionLabel(lookupTables, entry.Species, entry.Form, entry.Ability),
            entry.IsGigantamax,
            entry.Gender,
            GetOptionLabel(GenderOptions, entry.Gender, "Gender"),
            entry.FlawlessIvs,
            probabilities,
            FormatProbabilitySummary(probabilities),
            FormatHash(entry.LevelTableId),
            dropTableHash,
            bonusTableHash,
            CreateRewardLink("drop", dropTableHash, rewardLinks),
            CreateRewardLink("bonus", bonusTableHash, rewardLinks))
        {
            AbilityOptions = CreateAbilityOptions(lookupTables, entry.Species, entry.Form),
        };
    }

    private static IReadOnlyDictionary<(string RewardKind, string SourceTableHash), SwShRaidBattleRewardLinkRecord> LoadRewardLinks(
        OpenedProject project)
    {
        var rewardWorkflow = new SwShRaidRewardsWorkflowService().Load(project);

        return rewardWorkflow.Tables
            .Select(table => new SwShRaidBattleRewardLinkRecord(
                table.RewardKind,
                table.RewardKindLabel,
                table.TableId,
                table.SourceTableHash,
                IsMatched: true,
                table.Rewards.Count,
                FormatRewardPreview(table.Rewards)))
            .GroupBy(link => (link.RewardKind, link.SourceTableHash))
            .ToDictionary(group => group.Key, group => group.First());
    }

    private static SwShRaidBattleRewardLinkRecord CreateRewardLink(
        string rewardKind,
        string sourceTableHash,
        IReadOnlyDictionary<(string RewardKind, string SourceTableHash), SwShRaidBattleRewardLinkRecord> rewardLinks)
    {
        if (rewardLinks.TryGetValue((rewardKind, sourceTableHash), out var link))
        {
            return link;
        }

        var member = SwShRaidRewardsWorkflowService.KnownArchiveMembers.First(candidate =>
            string.Equals(candidate.Key, rewardKind, StringComparison.Ordinal));

        return new SwShRaidBattleRewardLinkRecord(
            rewardKind,
            member.Label,
            TableId: string.Empty,
            sourceTableHash,
            IsMatched: false,
            RewardItemCount: 0,
            Preview: sourceTableHash == FormatHash(0)
                ? $"No {member.Label.ToLowerInvariant()} table linked"
                : $"No loaded {member.Label.ToLowerInvariant()} table matches this hash");
    }

    private static string FormatRewardPreview(IReadOnlyList<SwShRaidRewardItemRecord> rewards)
    {
        if (rewards.Count == 0)
        {
            return "No reward rows";
        }

        var previewItems = rewards
            .Take(3)
            .Select(reward => reward.ItemName)
            .ToArray();
        var suffix = rewards.Count > previewItems.Length
            ? $", +{(rewards.Count - previewItems.Length).ToString(CultureInfo.InvariantCulture)} more"
            : string.Empty;

        var label = rewards.Count == 1 ? "reward" : "rewards";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{rewards.Count} {label}: {string.Join(", ", previewItems)}{suffix}");
    }

    private static int[] PadProbabilities(IReadOnlyList<uint> values, int length)
    {
        var padded = new int[length];
        for (var index = 0; index < padded.Length && index < values.Count; index++)
        {
            padded[index] = checked((int)values[index]);
        }

        return padded;
    }

    private static string FormatProbabilitySummary(IReadOnlyList<int> probabilities)
    {
        return string.Join(
            " / ",
            probabilities.Select((probability, index) =>
                $"{index + 1}-star {probability.ToString(CultureInfo.InvariantCulture)}%"));
    }

    private static string FormatGameVersion(int gameVersion)
    {
        return gameVersion switch
        {
            1 => "Sword",
            2 => "Shield",
            _ => $"Version {gameVersion.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string GetIndexedName(int id, IReadOnlyList<string> names, string fallbackPrefix)
    {
        return (uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id])
            ? names[id]
            : $"{fallbackPrefix} {id.ToString(CultureInfo.InvariantCulture)}";
    }

    private static RaidBattleLookupTables LoadLookupTables(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var messageRoot = ResolveLanguageMessageRoot(project, diagnostics);
        var speciesNames = LoadMessageTable(project, messageRoot, "monsname.dat", diagnostics);
        var abilityResolver = SwShPokemonAbilityOptionResolver.Load(project);

        return new RaidBattleLookupTables(
            speciesNames,
            abilityResolver,
            SourceFileCount: speciesNames.Length > 0 ? 1 : 0);
    }

    private static IReadOnlyList<SwShRaidBattleEditableFieldOption> CreateAbilityOptions(
        RaidBattleLookupTables lookupTables,
        int speciesId,
        int form)
    {
        return lookupTables.AbilityResolver.CreateOptions(speciesId, form, SwShAbilityOptionMode.Roll)
            .Select(option => new SwShRaidBattleEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static string GetAbilityOptionLabel(
        RaidBattleLookupTables lookupTables,
        int speciesId,
        int form,
        int value)
    {
        return CreateAbilityOptions(lookupTables, speciesId, form)
            .FirstOrDefault(option => option.Value == value)?.Label
            ?? GetOptionLabel(AbilityOptions, value, "Ability roll");
    }

    private static string? ResolveLanguageMessageRoot(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var languages = project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
            .Select(entry => GetLanguage(entry.RelativePath))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (languages.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Raid Battles lookup text is not available; numeric fallback labels will be shown.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/monsname.dat"));
            return null;
        }

        var language = languages.Contains(PreferredLanguage, StringComparer.OrdinalIgnoreCase)
            ? PreferredLanguage
            : languages[0];

        if (!string.Equals(language, PreferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"English Raid Battles lookup text was not found; using '{language}' lookup tables instead.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/monsname.dat"));
        }

        return $"{MessageRootPath}/{language}/common";
    }

    private static string[] LoadMessageTable(
        OpenedProject project,
        string? messageRoot,
        string fileName,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (messageRoot is null)
        {
            return [];
        }

        var relativePath = $"{messageRoot}/{fileName}";
        var source = ResolveWorkflowFile(project, relativePath);
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
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Raid Battles lookup table '{relativePath}' could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Raid Battles lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Raid Battles lookup table '{relativePath}' could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
    }

    private static string? GetLanguage(string relativePath)
    {
        if (!relativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var languageStart = MessageRootPath.Length + 1;
        var nextSeparator = relativePath.IndexOf('/', languageStart);

        return nextSeparator < 0
            ? null
            : relativePath[languageStart..nextSeparator];
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
            ? new WorkflowFileSource(graphEntry, sourcePath)
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

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShRaidBattleProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShRaidBattleProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShRaidBattleEditableField CreateField(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SwShRaidBattleEditableFieldOption>? options = null)
    {
        return new SwShRaidBattleEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SwShRaidBattleEditableFieldOption>());
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.RaidBattles,
            "Raid Battles",
            "Raid Pokemon slots, star probabilities, ability rolls, guaranteed perfect IVs, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Field: field,
            Domain: RaidBattlesEditDomain,
            Expected: expected);
    }

    private sealed record RaidBattleLookupTables(
        IReadOnlyList<string> SpeciesNames,
        SwShPokemonAbilityOptionResolver AbilityResolver,
        int SourceFileCount);
}
