// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.ZA.Data;
using KM.ZA.Gifts;
using KM.ZA.Trades;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.StaticEncounters;

internal sealed class ZaStaticEncountersWorkflowService
{
    public const string StaticEncountersEditDomain = "workflow.staticEncounters";

    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string Move0Field = "move0Id";
    public const string Move1Field = "move1Id";
    public const string Move2Field = "move2Id";
    public const string Move3Field = "move3Id";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string FlawlessIvCountField = "flawlessIvCount";

    internal const int TalentModeRandom = 0;
    internal const int TalentModeGuaranteedPerfectCount = 1;
    internal const int TalentModeFixedValues = 2;

    private const string WorkflowLabel = "Static Encounters";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A scripted static encounter Pokemon sources.";
    private const string CategoryId = "encounterData";
    private const string CategoryLabel = "Encounter Data";
    private const string ScenarioLabel = "Scripted Pokemon";

    private static readonly IReadOnlyList<ZaStaticEncounterEditableFieldOption> GenderOptions =
    [
        new(-1, "Game default / random"),
        new(0, "Random"),
        new(1, "Male"),
        new(2, "Female"),
    ];

    private static readonly IReadOnlyList<ZaStaticEncounterEditableFieldOption> ShinyModeOptions =
    [
        new(0, "Default / not forced"),
        new(1, "Not shiny"),
        new(2, "Forced shiny"),
        new(536870911, "Game default / not forced"),
        new(1073741823, "Wild default / not forced"),
    ];

    private static readonly IReadOnlyList<ZaStaticEncounterEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(1, "1 Guaranteed Perfect IV"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<ZaStaticEncounterEditableFieldOption> AbilityModeOptions =
    [
        new(0, "Random 1/2"),
        new(1, "Random 1/2/Hidden"),
        new(2, "Ability 1"),
        new(3, "Ability 2"),
        new(4, "Hidden Ability"),
        new(255, "Game default / random"),
    ];

    private static readonly IReadOnlyList<ZaStaticEncounterEditableFieldOption> NatureOptions =
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

    private static readonly IReadOnlyList<string> SupportedFields =
    [
        SpeciesField,
        FormField,
        LevelField,
        HeldItemIdField,
        AbilityField,
        NatureField,
        GenderField,
        ShinyLockField,
        Move0Field,
        Move1Field,
        Move2Field,
        Move3Field,
        FlawlessIvCountField,
        IvHpField,
        IvAttackField,
        IvDefenseField,
        IvSpecialAttackField,
        IvSpecialDefenseField,
        IvSpeedField,
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaStaticEncountersWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.StaticEncounters,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaStaticEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? encounterSource = null;
        var labels = ZaTextLabelLookup.None();
        var encounters = Array.Empty<ZaStaticEncounterEntry>();
        var sourceFileCount = 0;

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            encounterSource = fileSource.Read(project, ZaDataPaths.EncountDataArray);
            var wildIds = LoadWildEncounterIds(project, diagnostics, out var usedSpawnerSource);
            sourceFileCount = usedSpawnerSource ? 2 : 1;
            encounters = LoadRecords(encounterSource, labels, wildIds).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Static Encounters could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.EncountDataArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.StaticEncounters,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaStaticEncountersWorkflow(
            summary,
            encounters,
            CreateEditableFields(labels),
            new ZaStaticEncountersWorkflowStats(
                encounters.Length,
                encounters.Count(encounter => encounter.FlawlessIvCount is not null and not 0),
                encounterSource is null ? 0 : sourceFileCount,
                encounters.Length),
            diagnostics);
    }

    internal static ZaStaticEncounterEditableField? GetEditableField(
        ZaStaticEncountersWorkflow workflow,
        string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static string CreateRecordId(int encounterIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"static:{encounterIndex}");
    }

    internal static bool TryParseRecordId(string? recordId, out int encounterIndex)
    {
        encounterIndex = -1;

        const string prefix = "static:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out encounterIndex)
            && encounterIndex >= 0;
    }

    internal static IReadOnlyList<ZaStaticEncounterEntry> LoadRecords(
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        IReadOnlySet<string> wildEncounterIds)
    {
        var document = ZaPokemonDataDocument.Parse(source.Bytes);
        return document.Entries
            .Where(entry => IsStaticPokemonId(entry.Id, wildEncounterIds))
            .Select((entry, encounterIndex) => ToRecord(encounterIndex, entry, source, labels))
            .ToArray();
    }

    internal static bool IsStaticPokemonId(
        string? id,
        IReadOnlySet<string> wildEncounterIds)
    {
        return !string.IsNullOrWhiteSpace(id)
            && !ZaGiftPokemonWorkflowService.IsGiftPokemonId(id)
            && !ZaTradePokemonWorkflowService.IsTradePokemonId(id)
            && !wildEncounterIds.Contains(id);
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

    internal static string FormatAbilityMode(int value)
    {
        return AbilityModeOptions.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"Ability mode {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private IReadOnlySet<string> LoadWildEncounterIds(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        out bool usedSpawnerSource)
    {
        usedSpawnerSource = false;
        try
        {
            var source = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
            usedSpawnerSource = true;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var table = PokemonSpawnerDataDBArray.GetRootAsPokemonSpawnerDataDBArray(new ByteBuffer(source.Bytes));
            for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
            {
                var db = table.Values(groupIndex);
                if (db is null)
                {
                    continue;
                }

                for (var spawnerIndex = 0; spawnerIndex < db.Value.RootLength; spawnerIndex++)
                {
                    var spawner = db.Value.Root(spawnerIndex);
                    if (spawner is null)
                    {
                        continue;
                    }

                    for (var slot = 0; slot < spawner.Value.EncountDataInfoListLength; slot++)
                    {
                        var encounter = spawner.Value.EncountDataInfoList(slot);
                        if (!string.IsNullOrWhiteSpace(encounter?.EncountDataId))
                        {
                            ids.Add(encounter.Value.EncountDataId!);
                        }
                    }
                }
            }

            return ids;
        }
        catch (FileNotFoundException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Static Encounters could not read Wild Encounter spawner links: {exception.Message}",
                $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}"));
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static ZaStaticEncounterEntry ToRecord(
        int encounterIndex,
        ZaPokemonDataEntry entry,
        ZaWorkflowFile source,
        ZaTextLabelLookup labels)
    {
        var speciesId = entry.DevNo;
        var speciesName = speciesId == 0 ? "None" : labels.Pokemon(speciesId);
        var moves = ReadMoves(entry, labels);
        var ivs = ReadIvs(entry);
        var flawlessIvCount = ReadFlawlessIvCount(entry);
        var heldItemId = entry.HoldItem ?? 0;
        var fieldValues = CreateFieldValues(entry);
        var displayValues = CreateFieldDisplayValues(entry, labels);

        return new ZaStaticEncounterEntry(
            encounterIndex,
            entry.SourceIndex,
            CategoryId,
            CategoryLabel,
            CreateDisplayLabel(encounterIndex, speciesId, speciesName, entry.FormNo, entry.MinLevel, entry.Id ?? string.Empty, moves),
            entry.Id ?? string.Empty,
            speciesId,
            speciesName,
            entry.FormNo,
            entry.MinLevel,
            heldItemId,
            heldItemId > 0 ? labels.Item(heldItemId) : null,
            entry.Tokusei,
            FormatAbilityMode(entry.Tokusei),
            entry.Seikaku,
            FormatNature(entry.Seikaku),
            entry.Sex,
            FormatGender(entry.Sex),
            entry.Rare,
            FormatShinyMode(entry.Rare),
            EncounterScenario: 0,
            ScenarioLabel,
            new ZaStaticEncounterStatsRecord(0, 0, 0, 0, 0, 0),
            ivs,
            flawlessIvCount,
            FormatIvSummary(entry, ivs),
            moves,
            new ZaStaticEncounterProvenance(
                source.RelativePath,
                source.SourceLayer,
                source.FileState),
            SupportedFields,
            fieldValues,
            displayValues,
            SupportedFields.ToDictionary(field => field, _ => false, StringComparer.Ordinal),
            AbilityModeOptions);
    }

    private static IReadOnlyList<ZaStaticEncounterMoveRecord> ReadMoves(
        ZaPokemonDataEntry entry,
        ZaTextLabelLookup labels)
    {
        var moves = entry.WazaList?.Values ?? [0, 0, 0, 0];
        return moves
            .Take(4)
            .Select((moveId, index) => new ZaStaticEncounterMoveRecord(
                index,
                moveId,
                moveId <= 0 ? null : labels.Move(moveId)))
            .ToArray();
    }

    private static ZaStaticEncounterStatsRecord ReadIvs(ZaPokemonDataEntry entry)
    {
        if (entry.TalentValue is not { } talentValue)
        {
            return new ZaStaticEncounterStatsRecord(0, 0, 0, 0, 0, 0);
        }

        return new ZaStaticEncounterStatsRecord(
            talentValue.HP,
            talentValue.Attack,
            talentValue.Defense,
            talentValue.SpecialAttack,
            talentValue.SpecialDefense,
            talentValue.Speed);
    }

    private static int? ReadFlawlessIvCount(ZaPokemonDataEntry entry)
    {
        return entry.TalentScale switch
        {
            TalentModeRandom => 0,
            TalentModeGuaranteedPerfectCount => entry.TalentVNum,
            TalentModeFixedValues => null,
            _ => entry.TalentValue is null ? 0 : null,
        };
    }

    private static string FormatIvSummary(ZaPokemonDataEntry entry, ZaStaticEncounterStatsRecord ivs)
    {
        return entry.TalentScale switch
        {
            TalentModeRandom => "Random IVs",
            TalentModeGuaranteedPerfectCount => entry.TalentVNum == 1
                ? "1 guaranteed perfect IV"
                : $"{entry.TalentVNum.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs",
            TalentModeFixedValues => FormatFixedIvSummary(ivs),
            _ => entry.TalentValue is null
                ? $"Talent mode {entry.TalentScale.ToString(CultureInfo.InvariantCulture)}"
                : FormatFixedIvSummary(ivs),
        };
    }

    private static string FormatFixedIvSummary(ZaStaticEncounterStatsRecord ivs)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"HP {FormatIvValue(ivs.HP)} / Atk {FormatIvValue(ivs.Attack)} / Def {FormatIvValue(ivs.Defense)} / SpA {FormatIvValue(ivs.SpecialAttack)} / SpD {FormatIvValue(ivs.SpecialDefense)} / Spe {FormatIvValue(ivs.Speed)}");
    }

    private static string FormatIvValue(int value)
    {
        return value == -1 ? "Random" : value.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, string> CreateFieldValues(ZaPokemonDataEntry entry)
    {
        var moves = entry.WazaList?.Values ?? [0, 0, 0, 0];
        var ivs = ReadIvs(entry);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SpeciesField] = entry.DevNo.ToString(CultureInfo.InvariantCulture),
            [FormField] = entry.FormNo.ToString(CultureInfo.InvariantCulture),
            [LevelField] = entry.MinLevel.ToString(CultureInfo.InvariantCulture),
            [HeldItemIdField] = (entry.HoldItem ?? 0).ToString(CultureInfo.InvariantCulture),
            [AbilityField] = entry.Tokusei.ToString(CultureInfo.InvariantCulture),
            [NatureField] = entry.Seikaku.ToString(CultureInfo.InvariantCulture),
            [GenderField] = entry.Sex.ToString(CultureInfo.InvariantCulture),
            [ShinyLockField] = entry.Rare.ToString(CultureInfo.InvariantCulture),
            [Move0Field] = moves.ElementAtOrDefault(0).ToString(CultureInfo.InvariantCulture),
            [Move1Field] = moves.ElementAtOrDefault(1).ToString(CultureInfo.InvariantCulture),
            [Move2Field] = moves.ElementAtOrDefault(2).ToString(CultureInfo.InvariantCulture),
            [Move3Field] = moves.ElementAtOrDefault(3).ToString(CultureInfo.InvariantCulture),
            [FlawlessIvCountField] = (ReadFlawlessIvCount(entry) ?? 0).ToString(CultureInfo.InvariantCulture),
            [IvHpField] = ivs.HP.ToString(CultureInfo.InvariantCulture),
            [IvAttackField] = ivs.Attack.ToString(CultureInfo.InvariantCulture),
            [IvDefenseField] = ivs.Defense.ToString(CultureInfo.InvariantCulture),
            [IvSpecialAttackField] = ivs.SpecialAttack.ToString(CultureInfo.InvariantCulture),
            [IvSpecialDefenseField] = ivs.SpecialDefense.ToString(CultureInfo.InvariantCulture),
            [IvSpeedField] = ivs.Speed.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static IReadOnlyDictionary<string, string> CreateFieldDisplayValues(
        ZaPokemonDataEntry entry,
        ZaTextLabelLookup labels)
    {
        var moves = entry.WazaList?.Values ?? [0, 0, 0, 0];
        var heldItemId = entry.HoldItem ?? 0;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SpeciesField] = FormatOption(entry.DevNo, entry.DevNo == 0 ? "None" : labels.Pokemon(entry.DevNo)),
            [FormField] = entry.FormNo.ToString(CultureInfo.InvariantCulture),
            [LevelField] = entry.MinLevel.ToString(CultureInfo.InvariantCulture),
            [HeldItemIdField] = FormatOption(heldItemId, heldItemId == 0 ? "None" : labels.Item(heldItemId)),
            [AbilityField] = FormatAbilityMode(entry.Tokusei),
            [NatureField] = FormatNature(entry.Seikaku),
            [GenderField] = FormatGender(entry.Sex),
            [ShinyLockField] = FormatShinyMode(entry.Rare),
            [Move0Field] = FormatMove(moves.ElementAtOrDefault(0), labels),
            [Move1Field] = FormatMove(moves.ElementAtOrDefault(1), labels),
            [Move2Field] = FormatMove(moves.ElementAtOrDefault(2), labels),
            [Move3Field] = FormatMove(moves.ElementAtOrDefault(3), labels),
            [FlawlessIvCountField] = FlawlessIvCountOptions
                .FirstOrDefault(option => option.Value == (ReadFlawlessIvCount(entry) ?? 0))?.Label ?? "Fixed IVs",
            [IvHpField] = ReadIvs(entry).HP.ToString(CultureInfo.InvariantCulture),
            [IvAttackField] = ReadIvs(entry).Attack.ToString(CultureInfo.InvariantCulture),
            [IvDefenseField] = ReadIvs(entry).Defense.ToString(CultureInfo.InvariantCulture),
            [IvSpecialAttackField] = ReadIvs(entry).SpecialAttack.ToString(CultureInfo.InvariantCulture),
            [IvSpecialDefenseField] = ReadIvs(entry).SpecialDefense.ToString(CultureInfo.InvariantCulture),
            [IvSpeedField] = ReadIvs(entry).Speed.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string FormatMove(int moveId, ZaTextLabelLookup labels)
    {
        return FormatOption(moveId, moveId < 0 ? "Game default / none" : moveId == 0 ? "None" : labels.Move(moveId));
    }

    private static string FormatOption(int value, string label)
    {
        return $"{value.ToString(CultureInfo.InvariantCulture)} {label}";
    }

    private static IReadOnlyList<ZaStaticEncounterEditableField> CreateEditableFields(ZaTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateMoveOptions(labels);

        return
        [
            CreateField(SpeciesField, "Species", "Pokemon", 0, MaximumOptionValue(speciesOptions, ushort.MaxValue), speciesOptions),
            CreateField(FormField, "Form", "Pokemon", 0, short.MaxValue),
            CreateField(LevelField, "Level", "Pokemon", 0, 100),
            CreateField(HeldItemIdField, "Held item", "Pokemon", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions),
            CreateField(AbilityField, "Ability mode", "Pokemon", 0, 255, AbilityModeOptions),
            CreateField(NatureField, "Nature", "Pokemon", -1, 25, NatureOptions),
            CreateField(GenderField, "Gender", "Pokemon", -1, 2, GenderOptions),
            CreateField(ShinyLockField, "Shiny mode", "Pokemon", 0, 1073741823, ShinyModeOptions),
            CreateField(Move0Field, "Move 1", "Moves", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move1Field, "Move 2", "Moves", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move2Field, "Move 3", "Moves", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(Move3Field, "Move 4", "Moves", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            CreateField(FlawlessIvCountField, "IV preset", "Stats", 0, 6, FlawlessIvCountOptions),
            CreateField(IvHpField, "HP IV", "Stats", -1, 31),
            CreateField(IvAttackField, "Attack IV", "Stats", -1, 31),
            CreateField(IvDefenseField, "Defense IV", "Stats", -1, 31),
            CreateField(IvSpecialAttackField, "Sp. Atk IV", "Stats", -1, 31),
            CreateField(IvSpecialDefenseField, "Sp. Def IV", "Stats", -1, 31),
            CreateField(IvSpeedField, "Speed IV", "Stats", -1, 31),
        ];
    }

    private static IReadOnlyList<ZaStaticEncounterEditableFieldOption> CreateIndexedOptions(
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
                return new ZaStaticEncounterEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaStaticEncounterEditableFieldOption> CreateMoveOptions(
        ZaTextLabelLookup labels)
    {
        return
        [
            new(-1, "-1 Game default / none"),
            .. CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true),
        ];
    }

    private static int MaximumOptionValue(
        IReadOnlyList<ZaStaticEncounterEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static ZaStaticEncounterEditableField CreateField(
        string field,
        string label,
        string group,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<ZaStaticEncounterEditableFieldOption>? options = null)
    {
        return new ZaStaticEncounterEditableField(
            field,
            label,
            "integer",
            minimumValue,
            maximumValue,
            options ?? [],
            group);
    }

    private static string CreateDisplayLabel(
        int encounterIndex,
        int speciesId,
        string species,
        int form,
        int level,
        string eventLabel,
        IReadOnlyList<ZaStaticEncounterMoveRecord> moves)
    {
        var speciesLabel = ZaLabels.PokemonWithForm(speciesId, form, species);
        var moveText = string.Join(", ", moves
            .Where(move => move.MoveId > 0 && !string.IsNullOrWhiteSpace(move.Move))
            .Take(2)
            .Select(move => move.Move));
        var prefix = $"Static {(encounterIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {speciesLabel} Lv. {level.ToString(CultureInfo.InvariantCulture)}";
        return moveText.Length == 0
            ? $"{prefix} | {eventLabel}"
            : $"{prefix} | {eventLabel} | {moveText}";
    }
}
