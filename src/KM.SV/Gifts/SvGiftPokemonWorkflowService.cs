// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Gifts;

internal sealed class SvGiftPokemonWorkflowService
{
    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string BallItemIdField = "ballItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string Move1IdField = "move1Id";
    public const string Move2IdField = "move2Id";
    public const string Move3IdField = "move3Id";
    public const string Move4IdField = "move4Id";
    public const string TeraTypeField = "teraType";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string FlawlessIvCountField = "flawlessIvCount";
    public const string ScaleModeField = "scaleMode";
    public const string ScaleValueField = "scaleValue";

    private const string WorkflowLabel = "Gift Pokemon";
    private const string WorkflowDescription = "Edit Scarlet/Violet event gift Pokemon sources.";

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> GenderOptions =
    [
        new((int)global::SexType.DEFAULT, "Random"),
        new((int)global::SexType.MALE, "Male"),
        new((int)global::SexType.FEMALE, "Female"),
    ];

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> AbilityModeOptions =
    [
        new((int)global::TokuseiType.RANDOM_12, "Random 1/2"),
        new((int)global::TokuseiType.RANDOM_123, "Random 1/2/Hidden"),
        new((int)global::TokuseiType.SET_1, "Ability 1"),
        new((int)global::TokuseiType.SET_2, "Ability 2"),
        new((int)global::TokuseiType.SET_3, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> ShinyModeOptions =
    [
        new((int)global::RareType.DEFAULT, "Default"),
        new((int)global::RareType.NO_RARE, "Not Shiny"),
        new((int)global::RareType.RARE, "Shiny"),
    ];

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(1, "1 Guaranteed Perfect IV"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> NatureOptions =
    [
        new((int)global::SeikakuType.DEFAULT, "Default"),
        new((int)global::SeikakuType.GANBARIYA, "Hardy"),
        new((int)global::SeikakuType.SAMISIGARIYA, "Lonely"),
        new((int)global::SeikakuType.YUUKAN, "Brave"),
        new((int)global::SeikakuType.IJIPPARI, "Adamant"),
        new((int)global::SeikakuType.YANTYA, "Naughty"),
        new((int)global::SeikakuType.ZUBUTOI, "Bold"),
        new((int)global::SeikakuType.SUNAO, "Docile"),
        new((int)global::SeikakuType.NONKI, "Relaxed"),
        new((int)global::SeikakuType.WANPAKU, "Impish"),
        new((int)global::SeikakuType.NOUTENKI, "Lax"),
        new((int)global::SeikakuType.OKUBYOU, "Timid"),
        new((int)global::SeikakuType.SEKKATI, "Hasty"),
        new((int)global::SeikakuType.MAJIME, "Serious"),
        new((int)global::SeikakuType.YOUKI, "Jolly"),
        new((int)global::SeikakuType.MUJYAKI, "Naive"),
        new((int)global::SeikakuType.HIKAEME, "Modest"),
        new((int)global::SeikakuType.OTTORI, "Mild"),
        new((int)global::SeikakuType.REISEI, "Quiet"),
        new((int)global::SeikakuType.TEREYA, "Bashful"),
        new((int)global::SeikakuType.UKKARIYA, "Rash"),
        new((int)global::SeikakuType.ODAYAKA, "Calm"),
        new((int)global::SeikakuType.OTONASII, "Gentle"),
        new((int)global::SeikakuType.NAMAIKI, "Sassy"),
        new((int)global::SeikakuType.SINNTYOU, "Careful"),
        new((int)global::SeikakuType.KIMAGURE, "Quirky"),
    ];

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> TeraTypeOptions =
    [
        new((int)global::GemType.DEFAULT, "Default"),
        new((int)global::GemType.RANDOM, "Random"),
        new((int)global::GemType.NORMAL, "Normal"),
        new((int)global::GemType.KAKUTOU, "Fighting"),
        new((int)global::GemType.HIKOU, "Flying"),
        new((int)global::GemType.DOKU, "Poison"),
        new((int)global::GemType.JIMEN, "Ground"),
        new((int)global::GemType.IWA, "Rock"),
        new((int)global::GemType.MUSHI, "Bug"),
        new((int)global::GemType.GHOST, "Ghost"),
        new((int)global::GemType.HAGANE, "Steel"),
        new((int)global::GemType.HONOO, "Fire"),
        new((int)global::GemType.MIZU, "Water"),
        new((int)global::GemType.KUSA, "Grass"),
        new((int)global::GemType.DENKI, "Electric"),
        new((int)global::GemType.ESPER, "Psychic"),
        new((int)global::GemType.KOORI, "Ice"),
        new((int)global::GemType.DRAGON, "Dragon"),
        new((int)global::GemType.AKU, "Dark"),
        new((int)global::GemType.FAIRY, "Fairy"),
        new((int)global::GemType.NIJI, "Stellar"),
    ];

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> SizeModeOptions =
    [
        new((int)global::SizeType.RANDOM, "Random"),
        new((int)global::SizeType.XS, "XS"),
        new((int)global::SizeType.S, "S"),
        new((int)global::SizeType.M, "M"),
        new((int)global::SizeType.L, "L"),
        new((int)global::SizeType.XL, "XL"),
        new((int)global::SizeType.VALUE, "Fixed value"),
    ];

    private static readonly IReadOnlyList<SvGiftPokemonEditableFieldOption> BallOptions =
    [
        new((int)global::BallType.NONE, "None"),
        new((int)global::BallType.MASUTAABOORU, "Master Ball"),
        new((int)global::BallType.HAIPAABOORU, "Ultra Ball"),
        new((int)global::BallType.SUUPAABOORU, "Great Ball"),
        new((int)global::BallType.MONSUTAABOORU, "Poke Ball"),
        new((int)global::BallType.SAFARIBOORU, "Safari Ball"),
        new((int)global::BallType.NETTOBOORU, "Net Ball"),
        new((int)global::BallType.DAIBUBOORU, "Dive Ball"),
        new((int)global::BallType.NESUTOBOORU, "Nest Ball"),
        new((int)global::BallType.RIPIITOBOORU, "Repeat Ball"),
        new((int)global::BallType.TAIMAABOORU, "Timer Ball"),
        new((int)global::BallType.GOOZYASUBOORU, "Luxury Ball"),
        new((int)global::BallType.PUREMIABOORU, "Premier Ball"),
        new((int)global::BallType.DAAKUBOORU, "Dusk Ball"),
        new((int)global::BallType.HIIRUBOORU, "Heal Ball"),
        new((int)global::BallType.KUIKKUBOORU, "Quick Ball"),
        new((int)global::BallType.PURESYASUBOORU, "Cherish Ball"),
        new((int)global::BallType.SUPIIDOBOORU, "Fast Ball"),
        new((int)global::BallType.REBERUBOORU, "Level Ball"),
        new((int)global::BallType.RUAABOORU, "Lure Ball"),
        new((int)global::BallType.HEBIIBOORU, "Heavy Ball"),
        new((int)global::BallType.RABURABUBOORU, "Love Ball"),
        new((int)global::BallType.HURENDOBOORU, "Friend Ball"),
        new((int)global::BallType.MUUNBOORU, "Moon Ball"),
        new((int)global::BallType.KONPEBOORU, "Sport Ball"),
        new((int)global::BallType.DORIIMUBOORU, "Dream Ball"),
        new((int)global::BallType.URUTORABOORU, "Beast Ball"),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvGiftPokemonWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.GiftPokemon,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvGiftPokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var labels = SvTextLabelLookup.None();
        var gifts = Array.Empty<SvGiftPokemonEntry>();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
            var abilityResolver = SvGiftAbilityResolver.Load(project, fileSource, labels, diagnostics);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, diagnostics);
            source = fileSource.Read(project, SvDataPaths.EventAddPokemonArray);
            gifts = LoadRecords(source, labels, abilityResolver, moveResolver).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Gift Pokemon could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.EventAddPokemonArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.GiftPokemon,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvGiftPokemonWorkflow(
            summary,
            gifts,
            CreateEditableFields(labels),
            new SvGiftPokemonWorkflowStats(
                gifts.Length,
                gifts.Count(gift => gift.IsEgg),
                gifts.Count(gift => !string.Equals(gift.IvSummary, "Random IVs", StringComparison.Ordinal)),
                source is null ? 0 : 1),
            diagnostics);
    }

    internal static SvGiftPokemonEditableField? GetEditableField(
        SvGiftPokemonWorkflow workflow,
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

    internal static string FormatGender(global::SexType value)
    {
        return GenderOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static string FormatAbilityMode(global::TokuseiType value)
    {
        return AbilityModeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static string FormatNature(global::SeikakuType value)
    {
        return NatureOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static string FormatShinyMode(global::RareType value)
    {
        return ShinyModeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static string FormatTeraType(global::GemType value)
    {
        return TeraTypeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static string FormatScaleMode(global::SizeType value)
    {
        return SizeModeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static string FormatBall(global::BallType value)
    {
        return BallOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    internal static int ReadMoveId(global::WazaSet? move)
    {
        return move is null ? 0 : (int)move.Value.WazaId;
    }

    internal static IEnumerable<SvGiftPokemonEntry> LoadRecords(
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        SvGiftAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver)
    {
        var table = global::EventAddPokemonArray.GetRootAsEventAddPokemonArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var gift = table.Values(index);
            if (gift?.PokeData is not { } pokeData)
            {
                continue;
            }

            yield return ToRecord(index, gift.Value, pokeData, source, labels, abilityResolver, moveResolver);
        }
    }

    private static SvGiftPokemonEntry ToRecord(
        int giftIndex,
        global::EventAddPokemon gift,
        global::PokeDataFull pokeData,
        SvWorkflowFile source,
        SvTextLabelLookup labels,
        SvGiftAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver)
    {
        var speciesId = (int)pokeData.DevId;
        var eventLabel = gift.Label ?? string.Empty;
        var isEgg = eventLabel.Contains("egg", StringComparison.OrdinalIgnoreCase);
        var speciesName = speciesId == 0 ? "None" : labels.Pokemon(speciesId);
        var abilitySet = abilityResolver.Resolve(speciesId, pokeData.FormId);
        var abilityOptions = CreateAbilityModeOptions(abilitySet);
        var moves = ReadMoves(pokeData, labels, moveResolver);
        var ivs = ReadIvs(pokeData);
        var flawlessIvCount = ReadFlawlessIvCount(pokeData);

        return new SvGiftPokemonEntry(
            giftIndex,
            CreateDisplayLabel(giftIndex, speciesName, pokeData.Level, isEgg),
            eventLabel,
            speciesId,
            speciesName,
            pokeData.FormId,
            pokeData.Level,
            isEgg,
            (int)pokeData.Item,
            (int)pokeData.Item > 0 ? labels.Item((int)pokeData.Item) : null,
            (int)pokeData.BallId,
            FormatBall(pokeData.BallId),
            (int)pokeData.Tokusei,
            CreateAbilityModeLabel(pokeData.Tokusei, abilitySet),
            (int)pokeData.Seikaku,
            FormatNature(pokeData.Seikaku),
            (int)pokeData.Sex,
            FormatGender(pokeData.Sex),
            (int)pokeData.RareType,
            FormatShinyMode(pokeData.RareType),
            (int)pokeData.GemType,
            FormatTeraType(pokeData.GemType),
            (int)pokeData.ScaleType,
            FormatScaleMode(pokeData.ScaleType),
            pokeData.ScaleValue,
            moves,
            ivs,
            flawlessIvCount,
            FormatIvSummary(pokeData, ivs),
            new SvGiftPokemonProvenance(
                source.RelativePath,
                source.SourceLayer,
                source.FileState))
        {
            AbilityOptions = abilityOptions,
        };
    }

    private static IReadOnlyList<SvGiftPokemonMoveRecord> ReadMoves(
        global::PokeDataFull pokeData,
        SvTextLabelLookup labels,
        SvDefaultMoveResolver moveResolver)
    {
        var rawMoves = new[]
        {
            ToMoveRecord(0, pokeData.Waza1, labels),
            ToMoveRecord(1, pokeData.Waza2, labels),
            ToMoveRecord(2, pokeData.Waza3, labels),
            ToMoveRecord(3, pokeData.Waza4, labels),
        };

        if (pokeData.WazaType == global::WazaType.DEFAULT && rawMoves.All(move => move.MoveId == 0))
        {
            return moveResolver
                .Resolve((int)pokeData.DevId, pokeData.FormId, pokeData.Level)
                .Select((moveId, index) => new SvGiftPokemonMoveRecord(
                    index,
                    moveId,
                    moveId == 0 ? null : labels.Move(moveId),
                    PointUps: 0))
                .ToArray();
        }

        return rawMoves;
    }

    private static SvGiftPokemonMoveRecord ToMoveRecord(
        int slot,
        global::WazaSet? move,
        SvTextLabelLookup labels)
    {
        var moveId = ReadMoveId(move);
        return new SvGiftPokemonMoveRecord(
            slot,
            moveId,
            moveId == 0 ? null : labels.Move(moveId),
            move?.PointUp ?? 0);
    }

    private static SvGiftPokemonIvsRecord ReadIvs(global::PokeDataFull pokeData)
    {
        if (pokeData.TalentType != global::TalentType.VALUE || pokeData.TalentValue is not { } talentValue)
        {
            return new SvGiftPokemonIvsRecord(0, 0, 0, 0, 0, 0);
        }

        return new SvGiftPokemonIvsRecord(
            talentValue.Hp,
            talentValue.Atk,
            talentValue.Def,
            talentValue.SpAtk,
            talentValue.SpDef,
            talentValue.Agi);
    }

    private static int? ReadFlawlessIvCount(global::PokeDataFull pokeData)
    {
        return pokeData.TalentType switch
        {
            global::TalentType.RANDOM => 0,
            global::TalentType.V_NUM => pokeData.TalentVnum,
            global::TalentType.VALUE => null,
            _ => 0,
        };
    }

    private static string FormatIvSummary(global::PokeDataFull pokeData, SvGiftPokemonIvsRecord ivs)
    {
        return pokeData.TalentType switch
        {
            global::TalentType.RANDOM => "Random IVs",
            global::TalentType.V_NUM => pokeData.TalentVnum == 1
                ? "1 guaranteed perfect IV"
                : $"{pokeData.TalentVnum.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs",
            global::TalentType.VALUE => string.Create(
                CultureInfo.InvariantCulture,
                $"Fixed IVs: HP {ivs.HP}, Atk {ivs.Attack}, Def {ivs.Defense}, SpA {ivs.SpecialAttack}, SpD {ivs.SpecialDefense}, Spe {ivs.Speed}"),
            _ => SvLabels.EnumName(pokeData.TalentType),
        };
    }

    private static IReadOnlyList<SvGiftPokemonEditableField> CreateEditableFields(SvTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);

        return
        [
            CreateField(SpeciesField, "Species", 0, MaximumOptionValue(speciesOptions, ushort.MaxValue), speciesOptions),
            CreateField(FormField, "Form", 0, short.MaxValue),
            CreateField(LevelField, "Level", 0, 100),
            CreateField(HeldItemIdField, "Held item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions),
            CreateField(BallItemIdField, "Ball", 0, MaximumOptionValue(BallOptions, int.MaxValue), BallOptions),
            CreateField(AbilityField, "Ability mode", 0, 4, AbilityModeOptions),
            CreateField(NatureField, "Nature", 0, 25, NatureOptions),
            CreateField(GenderField, "Gender", 0, 2, GenderOptions),
            CreateField(ShinyLockField, "Shiny mode", 0, 2, ShinyModeOptions),
            CreateField(TeraTypeField, "Tera type", 0, 101, TeraTypeOptions),
            CreateField(Move1IdField, "Move 1", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move2IdField, "Move 2", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move3IdField, "Move 3", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move4IdField, "Move 4", 0, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(FlawlessIvCountField, "IV preset", 0, 6, FlawlessIvCountOptions),
            CreateField(IvHpField, "HP IV", 0, 31),
            CreateField(IvAttackField, "Attack IV", 0, 31),
            CreateField(IvDefenseField, "Defense IV", 0, 31),
            CreateField(IvSpeedField, "Speed IV", 0, 31),
            CreateField(IvSpecialAttackField, "Sp. Atk IV", 0, 31),
            CreateField(IvSpecialDefenseField, "Sp. Def IV", 0, 31),
            CreateField(ScaleModeField, "Scale mode", 0, 6, SizeModeOptions),
            CreateField(ScaleValueField, "Scale value", short.MinValue, short.MaxValue),
        ];
    }

    private static IReadOnlyList<SvGiftPokemonEditableFieldOption> CreateAbilityModeOptions(SvGiftAbilitySet abilities)
    {
        return
        [
            new((int)global::TokuseiType.RANDOM_12, "Random 1/2"),
            new((int)global::TokuseiType.RANDOM_123, "Random 1/2/Hidden"),
            new((int)global::TokuseiType.SET_1, FormatAbilitySlot(abilities.Ability1, "Ability 1")),
            new((int)global::TokuseiType.SET_2, FormatAbilitySlot(abilities.Ability2, "Ability 2")),
            new((int)global::TokuseiType.SET_3, FormatAbilitySlot(abilities.HiddenAbility, "Hidden Ability")),
        ];
    }

    private static string CreateAbilityModeLabel(global::TokuseiType value, SvGiftAbilitySet abilities)
    {
        return CreateAbilityModeOptions(abilities).FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatAbilitySlot(string ability, string slot)
    {
        return string.Equals(ability, slot, StringComparison.Ordinal) ? slot : $"{ability} ({slot})";
    }

    private static IReadOnlyList<SvGiftPokemonEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : Array.Empty<SvGiftPokemonEditableFieldOption>();
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SvGiftPokemonEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static int MaximumOptionValue(
        IReadOnlyList<SvGiftPokemonEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static SvGiftPokemonEditableField CreateField(
        string field,
        string label,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SvGiftPokemonEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new SvGiftPokemonEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SvGiftPokemonEditableFieldOption>());
    }

    private static string CreateDisplayLabel(
        int giftIndex,
        string species,
        int level,
        bool isEgg)
    {
        var giftNumber = (giftIndex + 1).ToString(CultureInfo.InvariantCulture);
        return isEgg
            ? $"Gift {giftNumber}: {species} Egg"
            : $"Gift {giftNumber}: {species} Lv. {level.ToString(CultureInfo.InvariantCulture)}";
    }

    internal sealed class SvGiftAbilityResolver
    {
        private readonly IReadOnlyDictionary<string, SvGiftAbilitySet> abilitiesBySpeciesForm;

        private SvGiftAbilityResolver(IReadOnlyDictionary<string, SvGiftAbilitySet> abilitiesBySpeciesForm)
        {
            this.abilitiesBySpeciesForm = abilitiesBySpeciesForm;
        }

        public static SvGiftAbilityResolver Empty { get; } = new(
            new Dictionary<string, SvGiftAbilitySet>(StringComparer.Ordinal));

        public static SvGiftAbilityResolver Load(
            OpenedProject project,
            SvWorkflowFileSource fileSource,
            SvTextLabelLookup labels,
            ICollection<ValidationDiagnostic> diagnostics)
        {
            try
            {
                var source = fileSource.Read(project, SvDataPaths.PersonalArray);
                var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(source.Bytes));
                var lookup = new Dictionary<string, SvGiftAbilitySet>(StringComparer.Ordinal);
                for (var index = 0; index < table.EntryLength; index++)
                {
                    var row = table.Entry(index);
                    if (row?.Species is not { } species || !row.Value.IsPresent)
                    {
                        continue;
                    }

                    lookup.TryAdd(
                        CreateKey(species.Species, species.Form),
                        new SvGiftAbilitySet(
                            labels.Ability(row.Value.Ability1),
                            labels.Ability(row.Value.Ability2),
                            labels.Ability(row.Value.AbilityHidden)));
                }

                return new SvGiftAbilityResolver(lookup);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"Gift Pokemon ability names could not be resolved from Pokemon Data: {exception.Message}",
                    $"romfs/{SvDataPaths.PersonalArray}"));
                return Empty;
            }
        }

        public SvGiftAbilitySet Resolve(int species, int form)
        {
            return abilitiesBySpeciesForm.TryGetValue(CreateKey(species, form), out var exact)
                ? exact
                : abilitiesBySpeciesForm.TryGetValue(CreateKey(species, 0), out var baseForm)
                    ? baseForm
                    : SvGiftAbilitySet.Empty;
        }

        private static string CreateKey(int species, int form)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{species}:{form}");
        }
    }

    internal sealed record SvGiftAbilitySet(
        string Ability1,
        string Ability2,
        string HiddenAbility)
    {
        public static SvGiftAbilitySet Empty { get; } = new("Ability 1", "Ability 2", "Hidden Ability");
    }
}
