// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Trades;

internal sealed class ZaTradePokemonWorkflowService
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

    private const string WorkflowLabel = "Trade Pokemon";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A received trade Pokemon payloads; trade requests are handled by event scripts.";

    private static readonly string[] TradeIdPrefixes =
    [
        "sub_tradepoke",
    ];

    private static readonly IReadOnlyList<ZaTradePokemonEditableFieldOption> GenderOptions =
    [
        new(-1, "Game default / random"),
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<ZaTradePokemonEditableFieldOption> ShinyModeOptions =
    [
        new(ZaPokemonDataConstants.RareNotShiny, ZaPokemonDataConstants.RareNotShinyLabel),
        new(ZaPokemonDataConstants.RareForcedShiny, ZaPokemonDataConstants.RareForcedShinyLabel),
        new(ZaPokemonDataConstants.RareDefaultShinyRoll, ZaPokemonDataConstants.RareDefaultShinyRollLabel),
    ];

    private static readonly IReadOnlyList<ZaTradePokemonEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(1, "1 Guaranteed Perfect IV"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<ZaTradePokemonEditableFieldOption> NatureOptions =
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

    public ZaTradePokemonWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.TradePokemon,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaTradePokemonWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? source = null;
        var labels = ZaTextLabelLookup.None();
        var pokemonAvailability = ZaPokemonAvailability.Unfiltered;
        var trades = Array.Empty<ZaTradePokemonEntry>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            pokemonAvailability = ZaPokemonAvailability.Load(project, fileSource, diagnostics, WorkflowLabel);
            var abilityResolver = ZaTradeAbilityResolver.Load(project, fileSource, labels, diagnostics);
            source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            trades = LoadRecords(source, labels, abilityResolver)
                .Select(trade => WithFormOptions(trade, pokemonAvailability))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Trade Pokemon could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.PokemonDataArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.TradePokemon,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaTradePokemonWorkflow(
            summary,
            trades,
            CreateEditableFields(labels, pokemonAvailability),
            new ZaTradePokemonWorkflowStats(
                trades.Length,
                trades.Count(trade => !string.Equals(trade.IvSummary, "Random IVs", StringComparison.Ordinal)),
                source is null ? 0 : 1),
            diagnostics)
        {
            PokemonAvailability = pokemonAvailability,
        };
    }

    internal static ZaTradePokemonEditableField? GetEditableField(
        ZaTradePokemonWorkflow workflow,
        string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static string CreateTradeRecordId(int tradeIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"trade:{tradeIndex}");
    }

    internal static bool TryParseTradeRecordId(string? recordId, out int tradeIndex)
    {
        tradeIndex = 0;

        const string prefix = "trade:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out tradeIndex)
            && tradeIndex >= 0;
    }

    internal static bool IsTradePokemonId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && TradeIdPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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

    internal static IReadOnlyList<ZaTradePokemonEntry> LoadRecords(
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        ZaTradeAbilityResolver abilityResolver)
    {
        var document = ZaPokemonDataDocument.Parse(source.Bytes);
        return document.Entries
            .Where(entry => IsTradePokemonId(entry.Id))
            .Select((entry, tradeIndex) => ToRecord(tradeIndex, entry, source, labels, abilityResolver))
            .ToArray();
    }

    internal static ZaTradePokemonEntry WithFormOptions(
        ZaTradePokemonEntry trade,
        ZaPokemonAvailability pokemonAvailability)
    {
        return trade with
        {
            FormOptions = pokemonAvailability.CreateFormOptions(
                trade.SpeciesId,
                form => new ZaTradePokemonEditableFieldOption(
                    form,
                    ZaLabels.PokemonFormLabel(trade.SpeciesId, form, trade.Species))),
        };
    }

    private static ZaTradePokemonEntry ToRecord(
        int tradeIndex,
        ZaPokemonDataEntry entry,
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        ZaTradeAbilityResolver abilityResolver)
    {
        var speciesId = entry.DevNo;
        var eventLabel = entry.Id ?? string.Empty;
        var speciesName = speciesId == 0 ? "None" : labels.Pokemon(speciesId);
        var abilitySet = abilityResolver.Resolve(speciesId, entry.FormNo);
        var abilityOptions = CreateAbilityModeOptions(abilitySet);
        var moves = ReadMoves(entry, labels);
        var ivs = ReadIvs(entry);
        var flawlessIvCount = ReadFlawlessIvCount(entry);
        var heldItemId = entry.HoldItem ?? 0;

        return new ZaTradePokemonEntry(
            tradeIndex,
            entry.SourceIndex,
            CreateDisplayLabel(tradeIndex, speciesName, entry.MinLevel, entry.MaxLevel, eventLabel),
            eventLabel,
            speciesId,
            speciesName,
            entry.FormNo,
            entry.MinLevel,
            entry.MaxLevel,
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
            new ZaTradePokemonProvenance(
                source.RelativePath,
                source.SourceLayer,
                source.FileState))
        {
            AbilityOptions = abilityOptions,
        };
    }

    private static IReadOnlyList<ZaTradePokemonMoveRecord> ReadMoves(
        ZaPokemonDataEntry entry,
        ZaTextLabelLookup labels)
    {
        var moves = entry.WazaList?.Values ?? [0, 0, 0, 0];
        return moves
            .Take(4)
            .Select((moveId, index) => new ZaTradePokemonMoveRecord(
                index,
                moveId,
                moveId <= 0 ? null : labels.Move(moveId),
                PointUps: 0))
            .ToArray();
    }

    private static ZaTradePokemonIvsRecord ReadIvs(ZaPokemonDataEntry entry)
    {
        if (entry.TalentValue is not { } talentValue)
        {
            return new ZaTradePokemonIvsRecord(-1, -1, -1, -1, -1, -1);
        }

        return new ZaTradePokemonIvsRecord(
            talentValue.HP,
            talentValue.Attack,
            talentValue.Defense,
            talentValue.SpecialAttack,
            talentValue.SpecialDefense,
            talentValue.Speed);
    }

    private static int? ReadFlawlessIvCount(ZaPokemonDataEntry entry)
    {
        return ZaPokemonDataIvEncoding.ReadFlawlessIvCount(entry);
    }

    private static string FormatIvSummary(ZaPokemonDataEntry entry, ZaTradePokemonIvsRecord ivs)
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

    private static string FormatFixedIvSummary(ZaTradePokemonIvsRecord ivs)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Fixed IVs: HP {FormatIvValue(ivs.HP)}, Atk {FormatIvValue(ivs.Attack)}, Def {FormatIvValue(ivs.Defense)}, SpA {FormatIvValue(ivs.SpecialAttack)}, SpD {FormatIvValue(ivs.SpecialDefense)}, Spe {FormatIvValue(ivs.Speed)}");
    }

    private static string FormatIvValue(int value)
    {
        return value == -1 ? "Random" : value.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<ZaTradePokemonEditableField> CreateEditableFields(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        var speciesOptions = CreateSpeciesOptions(labels, pokemonAvailability);
        var speciesMaximumValue = Math.Max(labels.PokemonNameCount - 1, MaximumOptionValue(speciesOptions, 0));
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateMoveOptions(labels);

        return
        [
            CreateField(SpeciesField, "Species", 0, speciesMaximumValue, speciesOptions),
            CreateField(FormField, "Form", 0, short.MaxValue),
            CreateField(LevelField, "Level", 0, 100),
            CreateField(HeldItemIdField, "Held item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions),
            CreateField(AbilityField, "Ability mode", 0, 255, CreateAbilityModeOptions(ZaTradeAbilitySet.Empty)),
            CreateField(NatureField, "Nature", -1, 25, NatureOptions),
            CreateField(GenderField, "Gender", -1, 2, GenderOptions),
            CreateField(
                ShinyLockField,
                "Shiny mode",
                ZaPokemonDataConstants.RareNotShiny,
                ZaPokemonDataConstants.RareDefaultShinyRoll,
                ShinyModeOptions),
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

    private static IReadOnlyList<ZaTradePokemonEditableFieldOption> CreateAbilityModeOptions(
        ZaTradeAbilitySet abilities)
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

    private static string CreateAbilityModeLabel(int value, ZaTradeAbilitySet abilities)
    {
        return CreateAbilityModeOptions(abilities).FirstOrDefault(option => option.Value == value)?.Label
            ?? $"Ability mode {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatAbilitySlot(string ability, string slot)
    {
        return string.Equals(ability, slot, StringComparison.Ordinal) ? slot : $"{ability} ({slot})";
    }

    private static IReadOnlyList<ZaTradePokemonEditableFieldOption> CreateMoveOptions(
        ZaTextLabelLookup labels)
    {
        return
        [
            new(ZaPokemonDataConstants.MoveNone, FormatOption(ZaPokemonDataConstants.MoveNone, ZaPokemonDataConstants.MoveNoneLabel)),
            new(ZaPokemonDataConstants.MoveAuto, FormatOption(ZaPokemonDataConstants.MoveAuto, ZaPokemonDataConstants.MoveAutoLabel)),
            .. CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: false),
        ];
    }

    private static IReadOnlyList<ZaTradePokemonEditableFieldOption> CreateSpeciesOptions(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        return pokemonAvailability
            .CreateSpeciesOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true)
            .Select(option => new ZaTradePokemonEditableFieldOption(option.Value, option.Label)
            {
                FormOptions = CreateSpeciesFormOptions(
                    option.Value,
                    labels,
                    pokemonAvailability),
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaTradePokemonEditableFieldOption>? CreateSpeciesFormOptions(
        int speciesId,
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        if (speciesId == 0)
        {
            return [new ZaTradePokemonEditableFieldOption(0, ZaLabels.PokemonFormLabel(0, 0, "None"))];
        }

        if (!pokemonAvailability.HasKnownAvailability)
        {
            return null;
        }

        return pokemonAvailability.CreateFormOptions(
            speciesId,
            form => new ZaTradePokemonEditableFieldOption(
                form,
                ZaLabels.PokemonFormLabel(speciesId, form, labels.Pokemon(speciesId))));
    }

    private static string FormatOption(int value, string label)
    {
        return $"{value.ToString(CultureInfo.InvariantCulture)} {label}";
    }

    private static IReadOnlyList<ZaTradePokemonEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : Array.Empty<ZaTradePokemonEditableFieldOption>();
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new ZaTradePokemonEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static int MaximumOptionValue(
        IReadOnlyList<ZaTradePokemonEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static ZaTradePokemonEditableField CreateField(
        string field,
        string label,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<ZaTradePokemonEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new ZaTradePokemonEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<ZaTradePokemonEditableFieldOption>());
    }

    private static string CreateDisplayLabel(
        int tradeIndex,
        string species,
        int minLevel,
        int maxLevel,
        string eventLabel)
    {
        var tradeNumber = (tradeIndex + 1).ToString(CultureInfo.InvariantCulture);
        var levelLabel = minLevel == maxLevel
            ? $"Lv. {minLevel.ToString(CultureInfo.InvariantCulture)}"
            : $"Lv. {minLevel.ToString(CultureInfo.InvariantCulture)} to {maxLevel.ToString(CultureInfo.InvariantCulture)}";
        return $"Trade {tradeNumber}: {species} {levelLabel} ({eventLabel})";
    }

    internal sealed class ZaTradeAbilityResolver
    {
        private readonly IReadOnlyDictionary<(int Species, int Form), ZaTradeAbilitySet> abilitiesBySpeciesForm;

        private ZaTradeAbilityResolver(IReadOnlyDictionary<(int Species, int Form), ZaTradeAbilitySet> abilitiesBySpeciesForm)
        {
            this.abilitiesBySpeciesForm = abilitiesBySpeciesForm;
        }

        public static ZaTradeAbilityResolver Empty { get; } = new(
            new Dictionary<(int Species, int Form), ZaTradeAbilitySet>());

        public static ZaTradeAbilityResolver Load(
            OpenedProject project,
            ZaWorkflowFileSource fileSource,
            ZaTextLabelLookup labels,
            ICollection<ValidationDiagnostic> diagnostics)
        {
            try
            {
                var source = fileSource.Read(project, ZaDataPaths.PersonalArray);
                var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(source.Bytes));
                var lookup = new Dictionary<(int Species, int Form), ZaTradeAbilitySet>();
                for (var index = 0; index < table.EntryLength; index++)
                {
                    var row = table.Entry(index);
                    if (row?.Species is not { } species || species.Species == 0)
                    {
                        continue;
                    }

                    lookup.TryAdd(
                        (species.Species, species.Form),
                        new ZaTradeAbilitySet(
                            labels.Ability(row.Value.Ability1),
                            labels.Ability(row.Value.Ability2),
                            labels.Ability(row.Value.AbilityHidden)));
                }

                return new ZaTradeAbilityResolver(lookup);
            }
            catch (FileNotFoundException)
            {
                return Empty;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Trade Pokemon ability names could not be resolved from Pokemon Data: {exception.Message}",
                    $"romfs/{ZaDataPaths.PersonalArray}"));
                return Empty;
            }
        }

        public ZaTradeAbilitySet Resolve(int species, int form)
        {
            return abilitiesBySpeciesForm.TryGetValue((species, form), out var exact)
                ? exact
                : abilitiesBySpeciesForm.TryGetValue((species, 0), out var baseForm)
                    ? baseForm
                    : ZaTradeAbilitySet.Empty;
        }
    }

    internal sealed record ZaTradeAbilitySet(
        string Ability1,
        string Ability2,
        string HiddenAbility)
    {
        public static ZaTradeAbilitySet Empty { get; } = new("Ability 1", "Ability 2", "Hidden Ability");
    }
}
