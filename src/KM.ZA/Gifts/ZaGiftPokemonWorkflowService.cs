// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Gifts;

internal sealed class ZaGiftPokemonWorkflowService
{
    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string Move1IdField = "move1Id";
    public const string Move2IdField = "move2Id";
    public const string Move3IdField = "move3Id";
    public const string Move4IdField = "move4Id";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string FlawlessIvCountField = "flawlessIvCount";

    internal const int TalentModeGameDefaultRandom = 127;
    internal const int TalentModeFixedOrGuaranteed = 128;

    private const int TalentModeScriptDefault = -1;
    private const int TalentModeAlphaDefault = 255;
    private const int LegacyTalentModeRandom = 0;
    private const int LegacyTalentModeGuaranteedPerfectCount = 1;
    private const int LegacyTalentModeFixedValues = 2;

    private const string WorkflowLabel = "Gift Pokemon";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A scripted gift Pokemon sources.";

    private static readonly string[] GiftIdPrefixes =
    [
        "sub_addpoke",
        "addpoke",
        "main_init_poke",
        "main_poke",
        "main10400_poke",
    ];

    private static readonly IReadOnlyDictionary<string, string> StarterGameplayRowsBySceneRow =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["main_init_poke_1"] = "test_encount_init_poke_0",
            ["main_init_poke_2"] = "test_encount_init_poke_1",
            ["main_init_poke_3"] = "test_encount_init_poke_2",
        };

    private static readonly IReadOnlyList<ZaGiftPokemonEditableFieldOption> GenderOptions =
    [
        new(-1, "Game default / random"),
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<ZaGiftPokemonEditableFieldOption> ShinyModeOptions =
    [
        new(0, "Default / not forced"),
        new(1, "Not shiny"),
        new(2, "Forced shiny"),
        new(536870911, "Game default / not forced"),
        new(1073741823, "Wild default / not forced"),
    ];

    private static readonly IReadOnlyList<ZaGiftPokemonEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(1, "1 Guaranteed Perfect IV"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<ZaGiftPokemonEditableFieldOption> NatureOptions =
    [
        new(-1, "Random / game default"),
        new(0, "Default (game behavior)"),
        new(1, "Hardy (neutral)"),
        new(2, "Lonely (+Atk, -Def)"),
        new(3, "Brave (+Atk, -Spe)"),
        new(4, "Adamant (+Atk, -Sp. Atk)"),
        new(5, "Naughty (+Atk, -Sp. Def)"),
        new(6, "Bold (+Def, -Atk)"),
        new(7, "Docile (neutral)"),
        new(8, "Relaxed (+Def, -Spe)"),
        new(9, "Impish (+Def, -Sp. Atk)"),
        new(10, "Lax (+Def, -Sp. Def)"),
        new(11, "Timid (+Spe, -Atk)"),
        new(12, "Hasty (+Spe, -Def)"),
        new(13, "Serious (neutral)"),
        new(14, "Jolly (+Spe, -Sp. Atk)"),
        new(15, "Naive (+Spe, -Sp. Def)"),
        new(16, "Modest (+Sp. Atk, -Atk)"),
        new(17, "Mild (+Sp. Atk, -Def)"),
        new(18, "Quiet (+Sp. Atk, -Spe)"),
        new(19, "Bashful (neutral)"),
        new(20, "Rash (+Sp. Atk, -Sp. Def)"),
        new(21, "Calm (+Sp. Def, -Atk)"),
        new(22, "Gentle (+Sp. Def, -Def)"),
        new(23, "Sassy (+Sp. Def, -Spe)"),
        new(24, "Careful (+Sp. Def, -Sp. Atk)"),
        new(25, "Quirky (neutral)"),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaGiftPokemonWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.GiftPokemon,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaGiftPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? source = null;
        var labels = ZaTextLabelLookup.None();
        var gifts = Array.Empty<ZaGiftPokemonEntry>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            var abilityResolver = ZaGiftAbilityResolver.Load(project, fileSource, labels, diagnostics);
            source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            gifts = LoadRecords(source, labels, abilityResolver).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Gift Pokemon could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.PokemonDataArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.GiftPokemon,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaGiftPokemonWorkflow(
            summary,
            gifts,
            CreateEditableFields(labels),
            new ZaGiftPokemonWorkflowStats(
                gifts.Length,
                gifts.Count(gift => gift.IsEgg),
                gifts.Count(gift => !string.Equals(gift.IvSummary, "Random IVs", StringComparison.Ordinal)),
                source is null ? 0 : 1),
            diagnostics);
    }

    internal static ZaGiftPokemonEditableField? GetEditableField(
        ZaGiftPokemonWorkflow workflow,
        string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static string CreateGiftRecordId(int giftIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"gift:{giftIndex}");
    }

    internal static bool TryParseGiftRecordId(string? recordId, out int giftIndex)
    {
        giftIndex = 0;

        const string prefix = "gift:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out giftIndex)
            && giftIndex >= 0;
    }

    internal static bool IsGiftPokemonId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && GiftIdPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsGiftPokemonSourceId(string? id)
    {
        return IsGiftPokemonId(id)
            || (!string.IsNullOrWhiteSpace(id)
                && StarterGameplayRowsBySceneRow.Values.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    internal static string FormatGender(int value)
    {
        return GenderOptions.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"Gender {value.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string FormatNature(int value)
    {
        return NatureOptions.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"Nature {value.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string FormatShinyMode(int value)
    {
        return ShinyModeOptions.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"Shiny mode {value.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static IReadOnlyList<ZaGiftPokemonEntry> LoadRecords(
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        ZaGiftAbilityResolver abilityResolver)
    {
        var document = ZaPokemonDataDocument.Parse(source.Bytes);
        return CreateGiftSourceGroups(document)
            .Select((group, giftIndex) => ToRecord(giftIndex, group, source, labels, abilityResolver))
            .ToArray();
    }

    private static ZaGiftPokemonEntry ToRecord(
        int giftIndex,
        GiftSourceGroup group,
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        ZaGiftAbilityResolver abilityResolver)
    {
        var entry = group.DisplayEntry;
        var speciesId = entry.DevNo;
        var eventLabel = group.Label;
        var isEgg = eventLabel.Contains("egg", StringComparison.OrdinalIgnoreCase);
        var speciesName = speciesId == 0 ? "None" : labels.Pokemon(speciesId);
        var abilitySet = abilityResolver.Resolve(speciesId, entry.FormNo);
        var abilityOptions = CreateAbilityModeOptions(abilitySet);
        var moves = ReadMoves(entry, labels);
        var ivs = ReadIvs(entry);
        var flawlessIvCount = ReadFlawlessIvCount(entry);
        var heldItemId = entry.HoldItem ?? 0;

        return new ZaGiftPokemonEntry(
            giftIndex,
            entry.SourceIndex,
            CreateDisplayLabel(giftIndex, speciesName, entry.MinLevel, entry.MaxLevel, eventLabel, isEgg),
            eventLabel,
            speciesId,
            speciesName,
            entry.FormNo,
            entry.MinLevel,
            entry.MaxLevel,
            isEgg,
            heldItemId,
            heldItemId > 0 ? labels.Item(heldItemId) : null,
            entry.Tokusei,
            CreateAbilityModeLabel(entry.Tokusei, abilitySet),
            entry.Seikaku,
            FormatNature(entry.Seikaku),
            entry.Sex,
            FormatGender(entry.Sex),
            entry.Rare,
            FormatShinyMode(entry.Rare),
            moves,
            ivs,
            flawlessIvCount,
            FormatIvSummary(entry, ivs),
            entry.OyabunProbability,
            entry.OyabunAdditionalLevel,
            new ZaGiftPokemonProvenance(
                source.RelativePath,
                source.SourceLayer,
                source.FileState))
        {
            AbilityOptions = abilityOptions,
        };
    }

    internal static IReadOnlyList<ZaPokemonDataEntry> ResolveApplyTargets(
        ZaPokemonDataDocument document,
        int giftIndex)
    {
        var group = CreateGiftSourceGroups(document).ElementAtOrDefault(giftIndex);
        return group is null ? Array.Empty<ZaPokemonDataEntry>() : group.ApplyEntries;
    }

    private static IReadOnlyList<GiftSourceGroup> CreateGiftSourceGroups(ZaPokemonDataDocument document)
    {
        var entriesById = document.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return document.Entries
            .Where(entry => IsGiftPokemonId(entry.Id))
            .Select(entry => CreateGiftSourceGroup(entry, entriesById))
            .ToArray();
    }

    private static GiftSourceGroup CreateGiftSourceGroup(
        ZaPokemonDataEntry entry,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> entriesById)
    {
        if (entry.Id is { } sceneId
            && StarterGameplayRowsBySceneRow.TryGetValue(sceneId, out var gameplayId)
            && entriesById.TryGetValue(gameplayId, out var gameplayEntry))
        {
            return new GiftSourceGroup(
                gameplayEntry,
                [entry, gameplayEntry],
                $"{sceneId} + {gameplayId}");
        }

        return new GiftSourceGroup(
            entry,
            [entry],
            entry.Id ?? string.Empty);
    }

    private static IReadOnlyList<ZaGiftPokemonMoveRecord> ReadMoves(
        ZaPokemonDataEntry entry,
        ZaTextLabelLookup labels)
    {
        var moves = entry.WazaList?.Values ?? [-1, -1, -1, -1];
        return moves
            .Take(4)
            .Select((moveId, index) => new ZaGiftPokemonMoveRecord(
                index,
                moveId,
                moveId <= 0 ? null : labels.Move(moveId),
                PointUps: 0))
            .ToArray();
    }

    private static ZaGiftPokemonIvsRecord ReadIvs(ZaPokemonDataEntry entry)
    {
        if (entry.TalentValue is not { } talentValue)
        {
            return new ZaGiftPokemonIvsRecord(-1, -1, -1, -1, -1, -1);
        }

        return new ZaGiftPokemonIvsRecord(
            talentValue.HP,
            talentValue.Attack,
            talentValue.Defense,
            talentValue.SpecialAttack,
            talentValue.SpecialDefense,
            talentValue.Speed);
    }

    private static int? ReadFlawlessIvCount(ZaPokemonDataEntry entry)
    {
        var hasRandomIvValues = HasOnlyRandomIvs(entry.TalentValue);
        if (IsGuaranteedPerfectCountMode(entry, hasRandomIvValues))
        {
            return entry.TalentVNum;
        }

        return IsRandomIvMode(entry, hasRandomIvValues) ? 0 : null;
    }

    private static string FormatIvSummary(ZaPokemonDataEntry entry, ZaGiftPokemonIvsRecord ivs)
    {
        var flawlessIvCount = ReadFlawlessIvCount(entry);
        if (flawlessIvCount == 0)
        {
            return "Random IVs";
        }

        if (flawlessIvCount > 0)
        {
            return flawlessIvCount == 1
                ? "1 guaranteed perfect IV"
                : $"{flawlessIvCount.Value.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs";
        }

        return FormatFixedIvSummary(ivs);
    }

    internal static ZaPokemonDataStatsRecord CreateRandomIvStats()
    {
        return new ZaPokemonDataStatsRecord(-1, -1, -1, -1, -1, -1);
    }

    private static bool IsGuaranteedPerfectCountMode(
        ZaPokemonDataEntry entry,
        bool hasRandomIvValues)
    {
        return entry.TalentScale == LegacyTalentModeGuaranteedPerfectCount
            || (hasRandomIvValues
                && entry.TalentVNum > 0
                && (entry.TalentScale == TalentModeFixedOrGuaranteed
                    || entry.TalentScale == TalentModeScriptDefault));
    }

    private static bool IsRandomIvMode(
        ZaPokemonDataEntry entry,
        bool hasRandomIvValues)
    {
        return entry.TalentScale == LegacyTalentModeRandom
            || entry.TalentScale == TalentModeGameDefaultRandom
            || entry.TalentScale == TalentModeAlphaDefault
            || (hasRandomIvValues
                && (entry.TalentScale == TalentModeFixedOrGuaranteed
                    || entry.TalentScale == TalentModeScriptDefault
                    || entry.TalentScale == LegacyTalentModeFixedValues));
    }

    private static bool HasOnlyRandomIvs(ZaPokemonDataStatsRecord? ivs)
    {
        return ivs is null
            || (ivs.HP == -1
                && ivs.Attack == -1
                && ivs.Defense == -1
                && ivs.SpecialAttack == -1
                && ivs.SpecialDefense == -1
                && ivs.Speed == -1);
    }

    private static string FormatFixedIvSummary(ZaGiftPokemonIvsRecord ivs)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Fixed IVs: HP {FormatIvValue(ivs.HP)}, Atk {FormatIvValue(ivs.Attack)}, Def {FormatIvValue(ivs.Defense)}, SpA {FormatIvValue(ivs.SpecialAttack)}, SpD {FormatIvValue(ivs.SpecialDefense)}, Spe {FormatIvValue(ivs.Speed)}");
    }

    private static string FormatIvValue(int value)
    {
        return value == -1 ? "Random" : value.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<ZaGiftPokemonEditableField> CreateEditableFields(ZaTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateMoveOptions(labels);

        return
        [
            CreateField(SpeciesField, "Species", 0, MaximumOptionValue(speciesOptions, ushort.MaxValue), speciesOptions),
            CreateField(FormField, "Form", 0, short.MaxValue),
            CreateField(LevelField, "Level", 0, 100),
            CreateField(HeldItemIdField, "Held item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions),
            CreateField(AbilityField, "Ability mode", 0, 255, CreateAbilityModeOptions(ZaGiftAbilitySet.Empty)),
            CreateField(NatureField, "Nature", -1, 25, NatureOptions),
            CreateField(GenderField, "Gender", -1, 2, GenderOptions),
            CreateField(ShinyLockField, "Shiny mode", 0, 1073741823, ShinyModeOptions),
            CreateField(Move1IdField, "Move 1", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move2IdField, "Move 2", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move3IdField, "Move 3", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move4IdField, "Move 4", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(FlawlessIvCountField, "IV preset", 0, 6, FlawlessIvCountOptions),
            CreateField(IvHpField, "HP IV", -1, 31),
            CreateField(IvAttackField, "Attack IV", -1, 31),
            CreateField(IvDefenseField, "Defense IV", -1, 31),
            CreateField(IvSpeedField, "Speed IV", -1, 31),
            CreateField(IvSpecialAttackField, "Sp. Atk IV", -1, 31),
            CreateField(IvSpecialDefenseField, "Sp. Def IV", -1, 31),
        ];
    }

    private static IReadOnlyList<ZaGiftPokemonEditableFieldOption> CreateAbilityModeOptions(
        ZaGiftAbilitySet abilities)
    {
        return
        [
            new(0, "Random 1/2"),
            new(1, "Random 1/2/Hidden"),
            new(2, FormatAbilitySlot(abilities.Ability1, "Ability 1")),
            new(3, FormatAbilitySlot(abilities.Ability2, "Ability 2")),
            new(4, FormatAbilitySlot(abilities.HiddenAbility, "Hidden Ability")),
            new(255, "Game default / random"),
        ];
    }

    private static string CreateAbilityModeLabel(int value, ZaGiftAbilitySet abilities)
    {
        return CreateAbilityModeOptions(abilities).FirstOrDefault(option => option.Value == value)?.Label
            ?? $"Ability mode {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatAbilitySlot(string ability, string slot)
    {
        return string.Equals(ability, slot, StringComparison.Ordinal) ? slot : $"{ability} ({slot})";
    }

    private static IReadOnlyList<ZaGiftPokemonEditableFieldOption> CreateMoveOptions(
        ZaTextLabelLookup labels)
    {
        return
        [
            new(-1, "-1 Game default / none"),
            .. CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true),
        ];
    }

    private static IReadOnlyList<ZaGiftPokemonEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : Array.Empty<ZaGiftPokemonEditableFieldOption>();
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new ZaGiftPokemonEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static int MaximumOptionValue(
        IReadOnlyList<ZaGiftPokemonEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static ZaGiftPokemonEditableField CreateField(
        string field,
        string label,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<ZaGiftPokemonEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new ZaGiftPokemonEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<ZaGiftPokemonEditableFieldOption>());
    }

    private static string CreateDisplayLabel(
        int giftIndex,
        string species,
        int minLevel,
        int maxLevel,
        string eventLabel,
        bool isEgg)
    {
        var giftNumber = (giftIndex + 1).ToString(CultureInfo.InvariantCulture);
        if (isEgg)
        {
            return $"Gift {giftNumber}: {species} Egg ({eventLabel})";
        }

        var levelLabel = minLevel == 0 && maxLevel == 0
            ? "Game default level"
            : minLevel == maxLevel
                ? $"Lv. {minLevel.ToString(CultureInfo.InvariantCulture)}"
                : $"Lv. {minLevel.ToString(CultureInfo.InvariantCulture)} to {maxLevel.ToString(CultureInfo.InvariantCulture)}";
        return $"Gift {giftNumber}: {species} {levelLabel} ({eventLabel})";
    }

    internal sealed class ZaGiftAbilityResolver
    {
        private readonly IReadOnlyDictionary<(int Species, int Form), ZaGiftAbilitySet> abilitiesBySpeciesForm;

        private ZaGiftAbilityResolver(IReadOnlyDictionary<(int Species, int Form), ZaGiftAbilitySet> abilitiesBySpeciesForm)
        {
            this.abilitiesBySpeciesForm = abilitiesBySpeciesForm;
        }

        public static ZaGiftAbilityResolver Empty { get; } = new(
            new Dictionary<(int Species, int Form), ZaGiftAbilitySet>());

        public static ZaGiftAbilityResolver Load(
            OpenedProject project,
            ZaWorkflowFileSource fileSource,
            ZaTextLabelLookup labels,
            ICollection<ValidationDiagnostic> diagnostics)
        {
            try
            {
                var source = fileSource.Read(project, ZaDataPaths.PersonalArray);
                var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(source.Bytes));
                var lookup = new Dictionary<(int Species, int Form), ZaGiftAbilitySet>();
                for (var index = 0; index < table.EntryLength; index++)
                {
                    var row = table.Entry(index);
                    if (row?.Species is not { } species || species.Species == 0)
                    {
                        continue;
                    }

                    lookup.TryAdd(
                        (species.Species, species.Form),
                        new ZaGiftAbilitySet(
                            labels.Ability(row.Value.Ability1),
                            labels.Ability(row.Value.Ability2),
                            labels.Ability(row.Value.AbilityHidden)));
                }

                return new ZaGiftAbilityResolver(lookup);
            }
            catch (FileNotFoundException)
            {
                return Empty;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Gift Pokemon ability names could not be resolved from Pokemon Data: {exception.Message}",
                    $"romfs/{ZaDataPaths.PersonalArray}"));
                return Empty;
            }
        }

        public ZaGiftAbilitySet Resolve(int species, int form)
        {
            return abilitiesBySpeciesForm.TryGetValue((species, form), out var exact)
                ? exact
                : abilitiesBySpeciesForm.TryGetValue((species, 0), out var baseForm)
                    ? baseForm
                    : ZaGiftAbilitySet.Empty;
        }
    }

    internal sealed record ZaGiftAbilitySet(
        string Ability1,
        string Ability2,
        string HiddenAbility)
    {
        public static ZaGiftAbilitySet Empty { get; } = new("Ability 1", "Ability 2", "Hidden Ability");
    }

    private sealed record GiftSourceGroup(
        ZaPokemonDataEntry DisplayEntry,
        IReadOnlyList<ZaPokemonDataEntry> ApplyEntries,
        string Label);
}
