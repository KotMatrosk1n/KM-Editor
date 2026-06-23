// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Raids;

internal sealed class SvTeraRaidsWorkflowService
{
    public const string VersionField = "version";
    public const string DifficultyField = "difficulty";
    public const string DeliveryGroupIdField = "deliveryGroupId";
    public const string SpawnRateField = "spawnRate";
    public const string CaptureRateField = "captureRate";
    public const string CaptureLevelField = "captureLevel";
    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string BallItemIdField = "ballItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string TeraTypeField = "teraType";
    public const string MoveModeField = "moveMode";
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
    public const string ScaleModeField = "scaleMode";
    public const string ScaleValueField = "scaleValue";
    public const string HeightModeField = "heightMode";
    public const string HeightValueField = "heightValue";
    public const string WeightModeField = "weightMode";
    public const string WeightValueField = "weightValue";
    public const string HpMultiplierField = "hpMultiplier";
    public const string ShieldTriggerHpField = "shieldTriggerHp";
    public const string ShieldTriggerTimeField = "shieldTriggerTime";
    public const string DoubleActionHpField = "doubleActionHp";
    public const string DoubleActionTimeField = "doubleActionTime";
    public const string DoubleActionRateField = "doubleActionRate";
    public const string FixedRewardTableField = "fixedRewardTable";
    public const string LotteryRewardTableField = "lotteryRewardTable";
    public const string FixedCategoryField = "fixedCategory";
    public const string FixedSubjectField = "fixedSubject";
    public const string FixedItemIdField = "fixedItemId";
    public const string FixedCountField = "fixedCount";
    public const string LotteryCategoryField = "lotteryCategory";
    public const string LotteryItemIdField = "lotteryItemId";
    public const string LotteryCountField = "lotteryCount";
    public const string LotteryRateField = "lotteryRate";
    public const string LotteryRareFlagField = "lotteryRareFlag";

    private const string WorkflowLabel = "Tera Raids";
    private const string WorkflowDescription = "Edit Scarlet/Violet Tera raid Pokemon, stars, Tera types, boss settings, rewards, and source provenance.";
    private const string FixedRewardKind = "fixed";
    private const string LotteryRewardKind = "lottery";

    private static readonly IReadOnlyList<RaidEnemySourceDefinition> RaidEnemySources =
    [
        new("paldea-1", "Paldea", 1, SvDataPaths.TeraRaidEnemyPaldea1),
        new("paldea-2", "Paldea", 2, SvDataPaths.TeraRaidEnemyPaldea2),
        new("paldea-3", "Paldea", 3, SvDataPaths.TeraRaidEnemyPaldea3),
        new("paldea-4", "Paldea", 4, SvDataPaths.TeraRaidEnemyPaldea4),
        new("paldea-5", "Paldea", 5, SvDataPaths.TeraRaidEnemyPaldea5),
        new("paldea-6", "Paldea", 6, SvDataPaths.TeraRaidEnemyPaldea6),
        new("kitakami-1", "Kitakami", 1, SvDataPaths.TeraRaidEnemyKitakami1),
        new("kitakami-2", "Kitakami", 2, SvDataPaths.TeraRaidEnemyKitakami2),
        new("kitakami-3", "Kitakami", 3, SvDataPaths.TeraRaidEnemyKitakami3),
        new("kitakami-4", "Kitakami", 4, SvDataPaths.TeraRaidEnemyKitakami4),
        new("kitakami-5", "Kitakami", 5, SvDataPaths.TeraRaidEnemyKitakami5),
        new("kitakami-6", "Kitakami", 6, SvDataPaths.TeraRaidEnemyKitakami6),
        new("blueberry-1", "Blueberry", 1, SvDataPaths.TeraRaidEnemyBlueberry1),
        new("blueberry-2", "Blueberry", 2, SvDataPaths.TeraRaidEnemyBlueberry2),
        new("blueberry-3", "Blueberry", 3, SvDataPaths.TeraRaidEnemyBlueberry3),
        new("blueberry-4", "Blueberry", 4, SvDataPaths.TeraRaidEnemyBlueberry4),
        new("blueberry-5", "Blueberry", 5, SvDataPaths.TeraRaidEnemyBlueberry5),
        new("blueberry-6", "Blueberry", 6, SvDataPaths.TeraRaidEnemyBlueberry6),
        new("delivery", "Event Delivery", null, SvDataPaths.TeraRaidEnemyDelivery),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> VersionOptions =
    [
        new((int)global::RaidRomType.BOTH, "Scarlet/Violet"),
        new((int)global::RaidRomType.TYPE_A, "Scarlet"),
        new((int)global::RaidRomType.TYPE_B, "Violet"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> GenderOptions =
    [
        new((int)global::SexType.DEFAULT, "Random"),
        new((int)global::SexType.MALE, "Male"),
        new((int)global::SexType.FEMALE, "Female"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> AbilityModeOptions =
    [
        new((int)global::TokuseiType.RANDOM_12, "Random 1/2"),
        new((int)global::TokuseiType.RANDOM_123, "Random 1/2/Hidden"),
        new((int)global::TokuseiType.SET_1, "Ability 1"),
        new((int)global::TokuseiType.SET_2, "Ability 2"),
        new((int)global::TokuseiType.SET_3, "Hidden Ability"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> ShinyModeOptions =
    [
        new((int)global::RareType.DEFAULT, "Default"),
        new((int)global::RareType.NO_RARE, "Not Shiny"),
        new((int)global::RareType.RARE, "Shiny"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(1, "1 Guaranteed Perfect IV"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> NatureOptions =
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

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> TeraTypeOptions =
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

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> SizeModeOptions =
    [
        new((int)global::SizeType.RANDOM, "Random"),
        new((int)global::SizeType.XS, "XS"),
        new((int)global::SizeType.S, "S"),
        new((int)global::SizeType.M, "M"),
        new((int)global::SizeType.L, "L"),
        new((int)global::SizeType.XL, "XL"),
        new((int)global::SizeType.VALUE, "Fixed value"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> BallOptions =
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

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> MoveModeOptions =
    [
        new((int)global::WazaType.DEFAULT, "Default"),
        new((int)global::WazaType.MANUAL, "Manual"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> RewardCategoryOptions =
    [
        new((int)global::RaidRewardItemCategoryType.ITEM, "Item"),
        new((int)global::RaidRewardItemCategoryType.POKE, "Pokemon material"),
        new((int)global::RaidRewardItemCategoryType.GEM, "Tera shard"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> RewardSubjectOptions =
    [
        new((int)global::RaidRewardItemSubjectType.ALL, "All players"),
        new((int)global::RaidRewardItemSubjectType.HOST, "Host"),
        new((int)global::RaidRewardItemSubjectType.CLIENT, "Guest"),
        new((int)global::RaidRewardItemSubjectType.ONCE, "Once"),
    ];

    private static readonly IReadOnlyList<SvTeraRaidEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvTeraRaidsWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.TeraRaids,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvTeraRaidsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        var labels = SvTextLabelLookup.None();
        RaidDataSet? dataSet = null;

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
            var abilityResolver = SvTeraRaidAbilityResolver.Load(project, fileSource, labels, diagnostics);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, diagnostics);
            dataSet = LoadDataSet(project, diagnostics);
            return CreateWorkflow(project, labels, abilityResolver, moveResolver, dataSet, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Tera Raids could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.TeraRaidEnemyPaldea1}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.TeraRaids,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvTeraRaidsWorkflow(
            summary,
            Array.Empty<SvTeraRaidEntry>(),
            Array.Empty<SvTeraRaidRewardTableRecord>(),
            Array.Empty<SvTeraRaidRewardTableRecord>(),
            CreateEditableFields(labels, Array.Empty<SvTeraRaidRewardTableRecord>(), Array.Empty<SvTeraRaidRewardTableRecord>()),
            new SvTeraRaidsWorkflowStats(0, 0, 0, 0),
            diagnostics);
    }

    internal static SvTeraRaidEditableField? GetEditableField(
        SvTeraRaidsWorkflow workflow,
        string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static bool TryParseRecordId(string? recordId, out TeraRaidRecordKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return false;
        }

        var parts = recordId.Split(':');
        if (parts is ["raid", var sourceKey, var indexText]
            && int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var raidIndex)
            && raidIndex >= 0)
        {
            key = new TeraRaidRecordKey("raid", sourceKey, raidIndex, null);
            return true;
        }

        if (parts is [FixedRewardKind or LotteryRewardKind, var tableText, var slotText]
            && int.TryParse(tableText, NumberStyles.None, CultureInfo.InvariantCulture, out var tableIndex)
            && int.TryParse(slotText, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
            && tableIndex >= 0
            && slot >= 0)
        {
            key = new TeraRaidRecordKey(parts[0], string.Empty, tableIndex, slot);
            return true;
        }

        return false;
    }

    internal static string CreateRaidRecordId(string sourceKey, int entryIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"raid:{sourceKey}:{entryIndex}");

    internal static string CreateRewardRecordId(string rewardKind, int tableIndex, int slot) =>
        string.Create(CultureInfo.InvariantCulture, $"{rewardKind}:{tableIndex}:{slot}");

    internal static bool IsRaidField(string field)
    {
        return field is
            VersionField or
            DifficultyField or
            DeliveryGroupIdField or
            SpawnRateField or
            CaptureRateField or
            CaptureLevelField or
            SpeciesField or
            FormField or
            LevelField or
            HeldItemIdField or
            BallItemIdField or
            AbilityField or
            NatureField or
            GenderField or
            ShinyLockField or
            TeraTypeField or
            MoveModeField or
            Move1IdField or
            Move2IdField or
            Move3IdField or
            Move4IdField or
            IvHpField or
            IvAttackField or
            IvDefenseField or
            IvSpeedField or
            IvSpecialAttackField or
            IvSpecialDefenseField or
            FlawlessIvCountField or
            ScaleModeField or
            ScaleValueField or
            HeightModeField or
            HeightValueField or
            WeightModeField or
            WeightValueField or
            HpMultiplierField or
            ShieldTriggerHpField or
            ShieldTriggerTimeField or
            DoubleActionHpField or
            DoubleActionTimeField or
            DoubleActionRateField or
            FixedRewardTableField or
            LotteryRewardTableField;
    }

    internal static bool IsFixedRewardField(string field)
    {
        return field is FixedCategoryField or FixedSubjectField or FixedItemIdField or FixedCountField;
    }

    internal static bool IsLotteryRewardField(string field)
    {
        return field is LotteryCategoryField or LotteryItemIdField or LotteryCountField or LotteryRateField or LotteryRareFlagField;
    }

    internal RaidDataSet LoadDataSet(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var raidSources = new List<RaidEnemySourceRows>();
        foreach (var definition in RaidEnemySources)
        {
            try
            {
                var source = fileSource.Read(project, definition.VirtualPath);
                raidSources.Add(new RaidEnemySourceRows(definition, source, ReadRaidRows(source.Bytes)));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"Tera raid source could not be loaded: {exception.Message}",
                    $"romfs/{definition.VirtualPath}"));
            }
        }

        var fixedRewardSource = ReadRequired(project, SvDataPaths.TeraRaidFixedRewardItemArray, diagnostics, "fixed reward");
        var lotteryRewardSource = ReadRequired(project, SvDataPaths.TeraRaidLotteryRewardItemArray, diagnostics, "lottery reward");
        return new RaidDataSet(
            raidSources,
            fixedRewardSource,
            ReadFixedRewardRows(fixedRewardSource.Bytes),
            lotteryRewardSource,
            ReadLotteryRewardRows(lotteryRewardSource.Bytes));
    }

    internal SvTeraRaidsWorkflow CreateWorkflow(
        OpenedProject project,
        SvTextLabelLookup labels,
        SvTeraRaidAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver,
        RaidDataSet dataSet,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var fixedTables = BuildFixedRewardTables(dataSet.FixedRewardSource, dataSet.FixedRewards, labels).ToArray();
        var lotteryTables = BuildLotteryRewardTables(dataSet.LotteryRewardSource, dataSet.LotteryRewards, labels).ToArray();
        var fixedByHash = fixedTables.ToDictionary(table => table.TableHash, StringComparer.OrdinalIgnoreCase);
        var lotteryByHash = lotteryTables.ToDictionary(table => table.TableHash, StringComparer.OrdinalIgnoreCase);
        var raids = dataSet.RaidSources
            .SelectMany(source => BuildRaidEntries(
                source,
                labels,
                abilityResolver,
                moveResolver,
                fixedByHash,
                lotteryByHash,
                project.Paths.SelectedGame))
            .ToArray();

        var allDiagnostics = diagnostics.ToArray();
        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.TeraRaids,
            WorkflowLabel,
            WorkflowDescription,
            allDiagnostics.Length == 0 ? null : allDiagnostics);

        return new SvTeraRaidsWorkflow(
            summary,
            raids,
            fixedTables,
            lotteryTables,
            CreateEditableFields(labels, fixedTables, lotteryTables),
            new SvTeraRaidsWorkflowStats(
                raids.Length,
                fixedTables.Length + lotteryTables.Length,
                fixedTables.Sum(table => table.RewardItemCount) + lotteryTables.Sum(table => table.RewardItemCount),
                dataSet.SourceFileCount),
            allDiagnostics);
    }

    internal static IReadOnlyList<RaidEnemySourceDefinition> EnemySourceDefinitions => RaidEnemySources;

    internal static IReadOnlyList<RaidEnemyRow> ReadRaidRows(byte[] bytes)
    {
        var table = global::RaidEnemyTable01Array.GetRootAsRaidEnemyTable01Array(new ByteBuffer(bytes));
        var rows = new List<RaidEnemyRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var wrapper = table.Values(index);
            rows.Add(new RaidEnemyRow(wrapper?.RaidEnemyInfo is { } info ? RaidEnemyInfoRow.From(info) : null));
        }

        return rows;
    }

    internal static byte[] WriteRaidRows(IReadOnlyList<RaidEnemyRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::RaidEnemyTable01Array.CreateValuesVector(builder, offsets);
        var root = global::RaidEnemyTable01Array.CreateRaidEnemyTable01Array(builder, vector);
        global::RaidEnemyTable01Array.FinishRaidEnemyTable01ArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    internal static IReadOnlyList<FixedRewardTableRow> ReadFixedRewardRows(byte[] bytes)
    {
        var table = global::RaidFixedRewardItemArray.GetRootAsRaidFixedRewardItemArray(new ByteBuffer(bytes));
        var rows = new List<FixedRewardTableRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            rows.Add(row is { } value ? FixedRewardTableRow.From(value) : FixedRewardTableRow.Empty);
        }

        return rows;
    }

    internal static byte[] WriteFixedRewardRows(IReadOnlyList<FixedRewardTableRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::RaidFixedRewardItemArray.CreateValuesVector(builder, offsets);
        var root = global::RaidFixedRewardItemArray.CreateRaidFixedRewardItemArray(builder, vector);
        global::RaidFixedRewardItemArray.FinishRaidFixedRewardItemArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    internal static IReadOnlyList<LotteryRewardTableRow> ReadLotteryRewardRows(byte[] bytes)
    {
        var table = global::RaidLotteryRewardItemArray.GetRootAsRaidLotteryRewardItemArray(new ByteBuffer(bytes));
        var rows = new List<LotteryRewardTableRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            rows.Add(row is { } value ? LotteryRewardTableRow.From(value) : LotteryRewardTableRow.Empty);
        }

        return rows;
    }

    internal static byte[] WriteLotteryRewardRows(IReadOnlyList<LotteryRewardTableRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::RaidLotteryRewardItemArray.CreateValuesVector(builder, offsets);
        var root = global::RaidLotteryRewardItemArray.CreateRaidLotteryRewardItemArray(builder, vector);
        global::RaidLotteryRewardItemArray.FinishRaidLotteryRewardItemArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    internal static void ApplyRaidField(
        RaidEnemyInfoRow row,
        string? field,
        int value,
        SvDefaultMoveResolver moveResolver)
    {
        switch (field)
        {
            case VersionField:
                row.RomVer = (global::RaidRomType)value;
                break;
            case DifficultyField:
                row.Difficulty = value;
                break;
            case DeliveryGroupIdField:
                row.DeliveryGroupID = checked((sbyte)value);
                break;
            case SpawnRateField:
                row.Rate = checked((sbyte)value);
                break;
            case CaptureRateField:
                row.CaptureRate = checked((sbyte)value);
                break;
            case CaptureLevelField:
                row.CaptureLv = checked((sbyte)value);
                break;
            default:
                var pokeData = row.EnsureBossPokePara();
                ApplyPokeDataField(pokeData, field, value, moveResolver);
                if (field is HeightModeField or HeightValueField or WeightModeField or WeightValueField)
                {
                    ApplySizeField(row.EnsureBossPokeSize(), field, value);
                }

                if (field is HpMultiplierField or ShieldTriggerHpField or ShieldTriggerTimeField or DoubleActionHpField or DoubleActionTimeField or DoubleActionRateField)
                {
                    ApplyBossField(row.EnsureBossDesc(), field, value);
                }

                break;
        }
    }

    internal static void ApplyFixedRewardField(FixedRewardInfoRow row, string? field, int value)
    {
        switch (field)
        {
            case FixedCategoryField:
                row.Category = (global::RaidRewardItemCategoryType)value;
                break;
            case FixedSubjectField:
                row.SubjectType = (global::RaidRewardItemSubjectType)value;
                break;
            case FixedItemIdField:
                row.ItemID = (global::ItemID)value;
                break;
            case FixedCountField:
                row.Num = checked((sbyte)value);
                break;
        }
    }

    internal static void ApplyLotteryRewardField(LotteryRewardInfoRow row, string? field, int value)
    {
        switch (field)
        {
            case LotteryCategoryField:
                row.Category = (global::RaidRewardItemCategoryType)value;
                break;
            case LotteryItemIdField:
                row.ItemID = (global::ItemID)value;
                break;
            case LotteryCountField:
                row.Num = checked((sbyte)value);
                break;
            case LotteryRateField:
                row.Rate = value;
                break;
            case LotteryRareFlagField:
                row.RareItemFlag = value != 0;
                break;
        }
    }

    private SvWorkflowFile ReadRequired(
        OpenedProject project,
        string virtualPath,
        ICollection<ValidationDiagnostic> diagnostics,
        string label)
    {
        try
        {
            return fileSource.Read(project, virtualPath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Tera raid {label} table could not be loaded: {exception.Message}",
                $"romfs/{virtualPath}"));
            throw;
        }
    }

    private static IEnumerable<SvTeraRaidEntry> BuildRaidEntries(
        RaidEnemySourceRows sourceRows,
        SvTextLabelLookup labels,
        SvTeraRaidAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver,
        IReadOnlyDictionary<string, SvTeraRaidRewardTableRecord> fixedRewardTables,
        IReadOnlyDictionary<string, SvTeraRaidRewardTableRecord> lotteryRewardTables,
        ProjectGame? selectedGame)
    {
        for (var index = 0; index < sourceRows.Rows.Count; index++)
        {
            var info = sourceRows.Rows[index].Info;
            if (info is null || !IsAvailableForSelectedGame(info.RomVer, selectedGame))
            {
                continue;
            }

            yield return ToRaidEntry(
                sourceRows.Definition,
                sourceRows.Source,
                index,
                info,
                labels,
                abilityResolver,
                moveResolver,
                fixedRewardTables,
                lotteryRewardTables);
        }
    }

    private static SvTeraRaidEntry ToRaidEntry(
        RaidEnemySourceDefinition definition,
        SvWorkflowFile source,
        int entryIndex,
        RaidEnemyInfoRow info,
        SvTextLabelLookup labels,
        SvTeraRaidAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver,
        IReadOnlyDictionary<string, SvTeraRaidRewardTableRecord> fixedRewardTables,
        IReadOnlyDictionary<string, SvTeraRaidRewardTableRecord> lotteryRewardTables)
    {
        var pokeData = info.BossPokePara ?? PokeDataBattleRow.Empty;
        var sizeData = info.BossPokeSize ?? RaidBossSizeRow.Empty;
        var bossData = info.BossDesc ?? RaidBossDataRow.Empty;
        var speciesId = (int)pokeData.DevId;
        var species = speciesId == 0 ? "None" : labels.Pokemon(speciesId);
        var fixedHash = FormatHash(info.DropTableFix);
        var lotteryHash = FormatHash(info.DropTableRandom);
        fixedRewardTables.TryGetValue(fixedHash, out var fixedRewards);
        lotteryRewardTables.TryGetValue(lotteryHash, out var lotteryRewards);
        var abilitySet = abilityResolver.Resolve(speciesId, pokeData.FormId);

        return new SvTeraRaidEntry(
            CreateRaidRecordId(definition.SourceKey, entryIndex),
            definition.Region,
            definition.StarRank,
            definition.StarRank is null ? "Event" : $"{definition.StarRank.Value.ToString(CultureInfo.InvariantCulture)} Star",
            entryIndex,
            info.No,
            (int)info.RomVer,
            FormatVersion(info.RomVer),
            info.DeliveryGroupID,
            info.Difficulty,
            info.Rate,
            info.CaptureRate,
            info.CaptureLv,
            speciesId,
            species,
            pokeData.FormId,
            pokeData.Level,
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
            (int)pokeData.WazaType,
            FormatMoveMode(pokeData.WazaType),
            ReadMoves(pokeData, labels, moveResolver),
            ReadIvs(pokeData),
            ReadFlawlessIvCount(pokeData),
            FormatIvSummary(pokeData, ReadIvs(pokeData)),
            (int)sizeData.ScaleType,
            FormatScaleMode(sizeData.ScaleType),
            sizeData.ScaleValue,
            (int)sizeData.HeightType,
            FormatScaleMode(sizeData.HeightType),
            sizeData.HeigntValue,
            (int)sizeData.WeightType,
            FormatScaleMode(sizeData.WeightType),
            sizeData.WaightValue,
            bossData.HpCoef,
            bossData.PowerChargeTrigerHp,
            bossData.PowerChargeTrigerTime,
            bossData.DoubleActionTrigerHp,
            bossData.DoubleActionTrigerTime,
            bossData.DoubleActionRate,
            fixedHash,
            lotteryHash,
            fixedRewards?.Preview ?? "No matching fixed rewards",
            lotteryRewards?.Preview ?? "No matching lottery rewards",
            new SvTeraRaidProvenance(source.RelativePath, source.SourceLayer, source.FileState))
        {
            AbilityOptions = CreateAbilityModeOptions(abilitySet),
        };
    }

    private static bool IsAvailableForSelectedGame(global::RaidRomType version, ProjectGame? selectedGame)
    {
        return selectedGame switch
        {
            ProjectGame.Scarlet => version is global::RaidRomType.BOTH or global::RaidRomType.TYPE_A,
            ProjectGame.Violet => version is global::RaidRomType.BOTH or global::RaidRomType.TYPE_B,
            _ => true,
        };
    }

    private static IEnumerable<SvTeraRaidRewardTableRecord> BuildFixedRewardTables(
        SvWorkflowFile source,
        IReadOnlyList<FixedRewardTableRow> rows,
        SvTextLabelLookup labels)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var tableHash = FormatHash(row.TableName);
            var rewards = row.Rewards
                .Select((reward, slot) => ToFixedRewardRecord(source, index, tableHash, slot, reward, labels))
                .Where(reward => reward is not null)
                .Cast<SvTeraRaidRewardItemRecord>()
                .ToArray();
            yield return new SvTeraRaidRewardTableRecord(
                CreateRewardRecordId(FixedRewardKind, index, 0),
                FixedRewardKind,
                "Fixed rewards",
                index,
                tableHash,
                rewards.Length,
                CreateRewardPreview(rewards),
                rewards,
                new SvTeraRaidProvenance(source.RelativePath, source.SourceLayer, source.FileState));
        }
    }

    private static IEnumerable<SvTeraRaidRewardTableRecord> BuildLotteryRewardTables(
        SvWorkflowFile source,
        IReadOnlyList<LotteryRewardTableRow> rows,
        SvTextLabelLookup labels)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var tableHash = FormatHash(row.TableName);
            var rewards = row.Rewards
                .Select((reward, slot) => ToLotteryRewardRecord(source, index, tableHash, slot, reward, labels))
                .Where(reward => reward is not null)
                .Cast<SvTeraRaidRewardItemRecord>()
                .ToArray();
            yield return new SvTeraRaidRewardTableRecord(
                CreateRewardRecordId(LotteryRewardKind, index, 0),
                LotteryRewardKind,
                "Lottery rewards",
                index,
                tableHash,
                rewards.Length,
                CreateRewardPreview(rewards),
                rewards,
                new SvTeraRaidProvenance(source.RelativePath, source.SourceLayer, source.FileState));
        }
    }

    private static SvTeraRaidRewardItemRecord? ToFixedRewardRecord(
        SvWorkflowFile source,
        int tableIndex,
        string tableHash,
        int slot,
        FixedRewardInfoRow? reward,
        SvTextLabelLookup labels)
    {
        if (reward is null)
        {
            return null;
        }

        return new SvTeraRaidRewardItemRecord(
            CreateRewardRecordId(FixedRewardKind, tableIndex, slot),
            FixedRewardKind,
            "Fixed",
            tableIndex,
            tableHash,
            slot,
            (int)reward.Category,
            FormatRewardCategory(reward.Category),
            (int)reward.SubjectType,
            FormatRewardSubject(reward.SubjectType),
            (int)reward.ItemID,
            FormatRewardItem(reward.ItemID, labels),
            reward.Num,
            null,
            null,
            new SvTeraRaidProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static SvTeraRaidRewardItemRecord? ToLotteryRewardRecord(
        SvWorkflowFile source,
        int tableIndex,
        string tableHash,
        int slot,
        LotteryRewardInfoRow? reward,
        SvTextLabelLookup labels)
    {
        if (reward is null)
        {
            return null;
        }

        return new SvTeraRaidRewardItemRecord(
            CreateRewardRecordId(LotteryRewardKind, tableIndex, slot),
            LotteryRewardKind,
            "Lottery",
            tableIndex,
            tableHash,
            slot,
            (int)reward.Category,
            FormatRewardCategory(reward.Category),
            null,
            null,
            (int)reward.ItemID,
            FormatRewardItem(reward.ItemID, labels),
            reward.Num,
            reward.Rate,
            reward.RareItemFlag,
            new SvTeraRaidProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static string CreateRewardPreview(IReadOnlyList<SvTeraRaidRewardItemRecord> rewards)
    {
        if (rewards.Count == 0)
        {
            return "No rewards";
        }

        return string.Join(
            ", ",
            rewards
                .Take(3)
                .Select(reward => $"{reward.Count.ToString(CultureInfo.InvariantCulture)} {reward.ItemName}"))
            + (rewards.Count > 3 ? $" +{(rewards.Count - 3).ToString(CultureInfo.InvariantCulture)} more" : string.Empty);
    }

    private static IReadOnlyList<SvTeraRaidMoveRecord> ReadMoves(
        PokeDataBattleRow pokeData,
        SvTextLabelLookup labels,
        SvDefaultMoveResolver moveResolver)
    {
        var rawMoves = new[]
        {
            ToMoveRecord(0, pokeData.Waza[0], labels),
            ToMoveRecord(1, pokeData.Waza[1], labels),
            ToMoveRecord(2, pokeData.Waza[2], labels),
            ToMoveRecord(3, pokeData.Waza[3], labels),
        };

        if (pokeData.WazaType == global::WazaType.DEFAULT && rawMoves.All(move => move.MoveId == 0))
        {
            return moveResolver
                .Resolve((int)pokeData.DevId, pokeData.FormId, pokeData.Level)
                .Select((moveId, index) => new SvTeraRaidMoveRecord(
                    index,
                    moveId,
                    moveId == 0 ? null : labels.Move(moveId),
                    PointUps: 0))
                .ToArray();
        }

        return rawMoves;
    }

    private static SvTeraRaidMoveRecord ToMoveRecord(int slot, WazaSetRow? move, SvTextLabelLookup labels)
    {
        var moveId = move is null ? 0 : (int)move.WazaId;
        return new SvTeraRaidMoveRecord(slot, moveId, moveId == 0 ? null : labels.Move(moveId), move?.PointUp ?? 0);
    }

    private static SvTeraRaidIvsRecord ReadIvs(PokeDataBattleRow pokeData)
    {
        if (pokeData.TalentType != global::TalentType.VALUE || pokeData.TalentValue is not { } talentValue)
        {
            return new SvTeraRaidIvsRecord(0, 0, 0, 0, 0, 0);
        }

        return new SvTeraRaidIvsRecord(
            talentValue.Hp,
            talentValue.Atk,
            talentValue.Def,
            talentValue.SpAtk,
            talentValue.SpDef,
            talentValue.Agi);
    }

    private static int? ReadFlawlessIvCount(PokeDataBattleRow pokeData)
    {
        return pokeData.TalentType switch
        {
            global::TalentType.RANDOM => 0,
            global::TalentType.V_NUM => pokeData.TalentVnum,
            global::TalentType.VALUE => null,
            _ => 0,
        };
    }

    private static string FormatIvSummary(PokeDataBattleRow pokeData, SvTeraRaidIvsRecord ivs)
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

    private static IReadOnlyList<SvTeraRaidEditableField> CreateEditableFields(
        SvTextLabelLookup labels,
        IReadOnlyList<SvTeraRaidRewardTableRecord> fixedRewardTables,
        IReadOnlyList<SvTeraRaidRewardTableRecord> lotteryRewardTables)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        var fixedRewardOptions = fixedRewardTables
            .Select(table => new SvTeraRaidEditableFieldOption(
                table.TableIndex,
                $"{table.TableIndex.ToString(CultureInfo.InvariantCulture)} {table.TableHash} {table.Preview}"))
            .ToArray();
        var lotteryRewardOptions = lotteryRewardTables
            .Select(table => new SvTeraRaidEditableFieldOption(
                table.TableIndex,
                $"{table.TableIndex.ToString(CultureInfo.InvariantCulture)} {table.TableHash} {table.Preview}"))
            .ToArray();

        return
        [
            CreateField(VersionField, "Game", 0, 2, VersionOptions),
            CreateField(DifficultyField, "Difficulty", 0, 10),
            CreateField(DeliveryGroupIdField, "Delivery group", sbyte.MinValue, sbyte.MaxValue),
            CreateField(SpawnRateField, "Spawn weight", sbyte.MinValue, sbyte.MaxValue),
            CreateField(CaptureRateField, "Capture rate", sbyte.MinValue, sbyte.MaxValue),
            CreateField(CaptureLevelField, "Capture level", 0, 100),
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
            CreateField(MoveModeField, "Move mode", 0, 1, MoveModeOptions),
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
            CreateField(HeightModeField, "Height mode", 0, 6, SizeModeOptions),
            CreateField(HeightValueField, "Height value", short.MinValue, short.MaxValue),
            CreateField(WeightModeField, "Weight mode", 0, 6, SizeModeOptions),
            CreateField(WeightValueField, "Weight value", short.MinValue, short.MaxValue),
            CreateField(HpMultiplierField, "HP multiplier", 0, short.MaxValue),
            CreateField(ShieldTriggerHpField, "Shield HP trigger", sbyte.MinValue, sbyte.MaxValue),
            CreateField(ShieldTriggerTimeField, "Shield time trigger", sbyte.MinValue, sbyte.MaxValue),
            CreateField(DoubleActionHpField, "Double action HP", sbyte.MinValue, sbyte.MaxValue),
            CreateField(DoubleActionTimeField, "Double action time", sbyte.MinValue, sbyte.MaxValue),
            CreateField(DoubleActionRateField, "Double action rate", sbyte.MinValue, sbyte.MaxValue),
            CreateField(FixedRewardTableField, "Fixed rewards", 0, Math.Max(0, fixedRewardOptions.Length - 1), fixedRewardOptions),
            CreateField(LotteryRewardTableField, "Lottery rewards", 0, Math.Max(0, lotteryRewardOptions.Length - 1), lotteryRewardOptions),
            CreateField(FixedCategoryField, "Fixed category", 0, 2, RewardCategoryOptions),
            CreateField(FixedSubjectField, "Fixed subject", 0, 3, RewardSubjectOptions),
            CreateField(FixedItemIdField, "Fixed item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions),
            CreateField(FixedCountField, "Fixed count", sbyte.MinValue, sbyte.MaxValue),
            CreateField(LotteryCategoryField, "Lottery category", 0, 2, RewardCategoryOptions),
            CreateField(LotteryItemIdField, "Lottery item", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions),
            CreateField(LotteryCountField, "Lottery count", sbyte.MinValue, sbyte.MaxValue),
            CreateField(LotteryRateField, "Lottery rate", 0, int.MaxValue),
            CreateField(LotteryRareFlagField, "Rare item", 0, 1, BooleanOptions),
        ];
    }

    private static void ApplyPokeDataField(
        PokeDataBattleRow row,
        string? field,
        int value,
        SvDefaultMoveResolver moveResolver)
    {
        switch (field)
        {
            case SpeciesField:
                row.DevId = (global::pml.common.DevID)checked((ushort)value);
                break;
            case FormField:
                row.FormId = checked((short)value);
                break;
            case LevelField:
                row.Level = value;
                break;
            case HeldItemIdField:
                row.Item = (global::ItemID)value;
                break;
            case BallItemIdField:
                row.BallId = (global::BallType)value;
                break;
            case AbilityField:
                row.Tokusei = (global::TokuseiType)value;
                break;
            case NatureField:
                row.Seikaku = (global::SeikakuType)value;
                break;
            case GenderField:
                row.Sex = (global::SexType)value;
                break;
            case ShinyLockField:
                row.RareType = (global::RareType)value;
                break;
            case TeraTypeField:
                row.GemType = (global::GemType)value;
                break;
            case MoveModeField:
                row.WazaType = (global::WazaType)value;
                break;
            case Move1IdField:
                row.SetMove(0, value, moveResolver);
                break;
            case Move2IdField:
                row.SetMove(1, value, moveResolver);
                break;
            case Move3IdField:
                row.SetMove(2, value, moveResolver);
                break;
            case Move4IdField:
                row.SetMove(3, value, moveResolver);
                break;
            case FlawlessIvCountField:
                row.SetIvPreset(value);
                break;
            case IvHpField:
                row.SetIv(ivs => ivs with { Hp = value });
                break;
            case IvAttackField:
                row.SetIv(ivs => ivs with { Atk = value });
                break;
            case IvDefenseField:
                row.SetIv(ivs => ivs with { Def = value });
                break;
            case IvSpecialAttackField:
                row.SetIv(ivs => ivs with { SpAtk = value });
                break;
            case IvSpecialDefenseField:
                row.SetIv(ivs => ivs with { SpDef = value });
                break;
            case IvSpeedField:
                row.SetIv(ivs => ivs with { Agi = value });
                break;
            case ScaleModeField:
                row.ScaleType = (global::SizeType)value;
                break;
            case ScaleValueField:
                row.ScaleValue = checked((short)value);
                break;
        }
    }

    private static void ApplySizeField(RaidBossSizeRow row, string? field, int value)
    {
        switch (field)
        {
            case HeightModeField:
                row.HeightType = (global::SizeType)value;
                break;
            case HeightValueField:
                row.HeigntValue = checked((short)value);
                break;
            case WeightModeField:
                row.WeightType = (global::SizeType)value;
                break;
            case WeightValueField:
                row.WaightValue = checked((short)value);
                break;
        }
    }

    private static void ApplyBossField(RaidBossDataRow row, string? field, int value)
    {
        switch (field)
        {
            case HpMultiplierField:
                row.HpCoef = checked((short)value);
                break;
            case ShieldTriggerHpField:
                row.PowerChargeTrigerHp = checked((sbyte)value);
                break;
            case ShieldTriggerTimeField:
                row.PowerChargeTrigerTime = checked((sbyte)value);
                break;
            case DoubleActionHpField:
                row.DoubleActionTrigerHp = checked((sbyte)value);
                break;
            case DoubleActionTimeField:
                row.DoubleActionTrigerTime = checked((sbyte)value);
                break;
            case DoubleActionRateField:
                row.DoubleActionRate = checked((sbyte)value);
                break;
        }
    }

    private static string FormatVersion(global::RaidRomType value)
    {
        return VersionOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatGender(global::SexType value)
    {
        return GenderOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatAbilityMode(global::TokuseiType value)
    {
        return AbilityModeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatNature(global::SeikakuType value)
    {
        return NatureOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatShinyMode(global::RareType value)
    {
        return ShinyModeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatTeraType(global::GemType value)
    {
        return TeraTypeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatScaleMode(global::SizeType value)
    {
        return SizeModeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatMoveMode(global::WazaType value)
    {
        return MoveModeOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatBall(global::BallType value)
    {
        return BallOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatRewardCategory(global::RaidRewardItemCategoryType value)
    {
        return RewardCategoryOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatRewardSubject(global::RaidRewardItemSubjectType value)
    {
        return RewardSubjectOptions.FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? SvLabels.EnumName(value);
    }

    private static string FormatRewardItem(global::ItemID itemId, SvTextLabelLookup labels)
    {
        var value = (int)itemId;
        return value == 0 ? "None" : labels.Item(value);
    }

    internal static string FormatHash(ulong value) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{value:X16}");

    private static IReadOnlyList<SvTeraRaidEditableFieldOption> CreateAbilityModeOptions(SvTeraRaidAbilitySet abilities)
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

    private static string CreateAbilityModeLabel(global::TokuseiType value, SvTeraRaidAbilitySet abilities)
    {
        return CreateAbilityModeOptions(abilities).FirstOrDefault(option => option.Value == (int)value)?.Label
            ?? FormatAbilityMode(value);
    }

    private static string FormatAbilitySlot(string ability, string slot)
    {
        return string.Equals(ability, slot, StringComparison.Ordinal) ? slot : $"{ability} ({slot})";
    }

    private static IReadOnlyList<SvTeraRaidEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : Array.Empty<SvTeraRaidEditableFieldOption>();
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SvTeraRaidEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static int MaximumOptionValue(
        IReadOnlyList<SvTeraRaidEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static SvTeraRaidEditableField CreateField(
        string field,
        string label,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SvTeraRaidEditableFieldOption>? options = null,
        string valueKind = "integer")
    {
        return new SvTeraRaidEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? Array.Empty<SvTeraRaidEditableFieldOption>());
    }

    public sealed record RaidEnemySourceDefinition(
        string SourceKey,
        string Region,
        int? StarRank,
        string VirtualPath);

    internal sealed record RaidEnemySourceRows(
        RaidEnemySourceDefinition Definition,
        SvWorkflowFile Source,
        IReadOnlyList<RaidEnemyRow> Rows);

    internal sealed record RaidDataSet(
        IReadOnlyList<RaidEnemySourceRows> RaidSources,
        SvWorkflowFile FixedRewardSource,
        IReadOnlyList<FixedRewardTableRow> FixedRewards,
        SvWorkflowFile LotteryRewardSource,
        IReadOnlyList<LotteryRewardTableRow> LotteryRewards)
    {
        public int SourceFileCount => RaidSources.Count + 2;
    }

    internal readonly record struct TeraRaidRecordKey(
        string Kind,
        string SourceKey,
        int Index,
        int? Slot);

    internal sealed class SvTeraRaidAbilityResolver
    {
        private readonly IReadOnlyDictionary<string, SvTeraRaidAbilitySet> abilitiesBySpeciesForm;

        private SvTeraRaidAbilityResolver(IReadOnlyDictionary<string, SvTeraRaidAbilitySet> abilitiesBySpeciesForm)
        {
            this.abilitiesBySpeciesForm = abilitiesBySpeciesForm;
        }

        public static SvTeraRaidAbilityResolver Empty { get; } = new(
            new Dictionary<string, SvTeraRaidAbilitySet>(StringComparer.Ordinal));

        public static SvTeraRaidAbilityResolver Load(
            OpenedProject project,
            SvWorkflowFileSource fileSource,
            SvTextLabelLookup labels,
            ICollection<ValidationDiagnostic> diagnostics)
        {
            try
            {
                var source = fileSource.Read(project, SvDataPaths.PersonalArray);
                var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(source.Bytes));
                var lookup = new Dictionary<string, SvTeraRaidAbilitySet>(StringComparer.Ordinal);
                for (var index = 0; index < table.EntryLength; index++)
                {
                    var row = table.Entry(index);
                    if (row?.Species is not { } species || !row.Value.IsPresent)
                    {
                        continue;
                    }

                    lookup.TryAdd(
                        CreateKey(species.Species, species.Form),
                        new SvTeraRaidAbilitySet(
                            labels.Ability(row.Value.Ability1),
                            labels.Ability(row.Value.Ability2),
                            labels.Ability(row.Value.AbilityHidden)));
                }

                return new SvTeraRaidAbilityResolver(lookup);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"Tera raid ability names could not be resolved from Pokemon Data: {exception.Message}",
                    $"romfs/{SvDataPaths.PersonalArray}"));
                return Empty;
            }
        }

        public SvTeraRaidAbilitySet Resolve(int species, int form)
        {
            return abilitiesBySpeciesForm.TryGetValue(CreateKey(species, form), out var exact)
                ? exact
                : abilitiesBySpeciesForm.TryGetValue(CreateKey(species, 0), out var baseForm)
                    ? baseForm
                    : SvTeraRaidAbilitySet.Empty;
        }

        private static string CreateKey(int species, int form)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{species}:{form}");
        }
    }

    internal sealed record SvTeraRaidAbilitySet(
        string Ability1,
        string Ability2,
        string HiddenAbility)
    {
        public static SvTeraRaidAbilitySet Empty { get; } =
            new("Ability 1", "Ability 2", "Hidden Ability");
    }

    internal sealed class RaidEnemyRow
    {
        public RaidEnemyRow(RaidEnemyInfoRow? info)
        {
            Info = info;
        }

        public RaidEnemyInfoRow? Info { get; set; }

        public Offset<global::RaidEnemyTable01> Write(FlatBufferBuilder builder)
        {
            var infoOffset = Info?.Write(builder) ?? default;
            return global::RaidEnemyTable01.CreateRaidEnemyTable01(builder, infoOffset);
        }
    }

    internal sealed class RaidEnemyInfoRow
    {
        public global::RaidRomType RomVer { get; set; }
        public int No { get; init; }
        public sbyte DeliveryGroupID { get; set; }
        public int Difficulty { get; set; }
        public sbyte Rate { get; set; }
        public ulong DropTableFix { get; set; }
        public ulong DropTableRandom { get; set; }
        public sbyte CaptureRate { get; set; }
        public sbyte CaptureLv { get; set; }
        public PokeDataBattleRow? BossPokePara { get; set; }
        public RaidBossSizeRow? BossPokeSize { get; set; }
        public RaidBossDataRow? BossDesc { get; set; }
        public RaidTimeRow? RaidTimeData { get; init; }

        public static RaidEnemyInfoRow From(global::RaidEnemyInfo row)
        {
            return new RaidEnemyInfoRow
            {
                RomVer = row.RomVer,
                No = row.No,
                DeliveryGroupID = row.DeliveryGroupID,
                Difficulty = row.Difficulty,
                Rate = row.Rate,
                DropTableFix = row.DropTableFix,
                DropTableRandom = row.DropTableRandom,
                CaptureRate = row.CaptureRate,
                CaptureLv = row.CaptureLv,
                BossPokePara = row.BossPokePara is { } pokeData ? PokeDataBattleRow.From(pokeData) : null,
                BossPokeSize = row.BossPokeSize is { } sizeData ? RaidBossSizeRow.From(sizeData) : null,
                BossDesc = row.BossDesc is { } bossData ? RaidBossDataRow.From(bossData) : null,
                RaidTimeData = row.RaidTimeData is { } timeData ? RaidTimeRow.From(timeData) : null,
            };
        }

        public PokeDataBattleRow EnsureBossPokePara()
        {
            BossPokePara ??= PokeDataBattleRow.Empty;
            return BossPokePara;
        }

        public RaidBossSizeRow EnsureBossPokeSize()
        {
            BossPokeSize ??= RaidBossSizeRow.Empty;
            return BossPokeSize;
        }

        public RaidBossDataRow EnsureBossDesc()
        {
            BossDesc ??= RaidBossDataRow.Empty;
            return BossDesc;
        }

        public Offset<global::RaidEnemyInfo> Write(FlatBufferBuilder builder)
        {
            var pokeDataOffset = BossPokePara?.Write(builder) ?? default;
            var sizeOffset = BossPokeSize?.Write(builder) ?? default;
            var bossOffset = BossDesc?.Write(builder) ?? default;
            var timeOffset = RaidTimeData?.Write(builder) ?? default;

            return global::RaidEnemyInfo.CreateRaidEnemyInfo(
                builder,
                RomVer,
                No,
                DeliveryGroupID,
                Difficulty,
                Rate,
                DropTableFix,
                DropTableRandom,
                CaptureRate,
                CaptureLv,
                pokeDataOffset,
                sizeOffset,
                bossOffset,
                timeOffset);
        }
    }

    internal sealed class PokeDataBattleRow
    {
        public static PokeDataBattleRow Empty => new();

        public global::pml.common.DevID DevId { get; set; }
        public short FormId { get; set; }
        public global::SexType Sex { get; set; }
        public global::ItemID Item { get; set; }
        public int Level { get; set; }
        public global::BallType BallId { get; set; }
        public global::WazaType WazaType { get; set; }
        public WazaSetRow?[] Waza { get; } = new WazaSetRow?[4];
        public global::GemType GemType { get; set; }
        public global::SeikakuType Seikaku { get; set; }
        public global::TokuseiType Tokusei { get; set; }
        public global::TalentType TalentType { get; set; }
        public ParamSetRow? TalentValue { get; set; }
        public sbyte TalentVnum { get; set; }
        public ParamSetRow? EffortValue { get; init; }
        public global::RareType RareType { get; set; }
        public global::SizeType ScaleType { get; set; }
        public short ScaleValue { get; set; }

        public static PokeDataBattleRow From(global::PokeDataBattle row)
        {
            var result = new PokeDataBattleRow
            {
                DevId = row.DevId,
                FormId = row.FormId,
                Sex = row.Sex,
                Item = row.Item,
                Level = row.Level,
                BallId = row.BallId,
                WazaType = row.WazaType,
                GemType = row.GemType,
                Seikaku = row.Seikaku,
                Tokusei = row.Tokusei,
                TalentType = row.TalentType,
                TalentValue = row.TalentValue is { } talentValue ? ParamSetRow.From(talentValue) : null,
                TalentVnum = row.TalentVnum,
                EffortValue = row.EffortValue is { } effortValue ? ParamSetRow.From(effortValue) : null,
                RareType = row.RareType,
                ScaleType = row.ScaleType,
                ScaleValue = row.ScaleValue,
            };

            result.Waza[0] = row.Waza1 is { } waza1 ? WazaSetRow.From(waza1) : null;
            result.Waza[1] = row.Waza2 is { } waza2 ? WazaSetRow.From(waza2) : null;
            result.Waza[2] = row.Waza3 is { } waza3 ? WazaSetRow.From(waza3) : null;
            result.Waza[3] = row.Waza4 is { } waza4 ? WazaSetRow.From(waza4) : null;
            return result;
        }

        public void SetMove(int index, int moveId, SvDefaultMoveResolver moveResolver)
        {
            if (WazaType == global::WazaType.DEFAULT)
            {
                var currentMoves = Waza
                    .Select(waza => waza is null ? 0 : (int)waza.WazaId)
                    .ToArray();
                var defaultMoves = currentMoves.Any(move => move != 0)
                    ? currentMoves
                    : moveResolver.Resolve((int)DevId, FormId, Level);

                for (var defaultIndex = 0; defaultIndex < Waza.Length; defaultIndex++)
                {
                    var defaultMove = defaultMoves.ElementAtOrDefault(defaultIndex);
                    Waza[defaultIndex] = defaultMove == 0
                        ? null
                        : new WazaSetRow((global::pml.common.WazaID)checked((ushort)defaultMove), 0);
                }

                WazaType = global::WazaType.MANUAL;
            }

            Waza[index] = moveId == 0
                ? null
                : (Waza[index] ?? new WazaSetRow((global::pml.common.WazaID)0, 0)) with
                {
                    WazaId = (global::pml.common.WazaID)checked((ushort)moveId),
                };
        }

        public void SetIvPreset(int value)
        {
            if (value <= 0)
            {
                TalentType = global::TalentType.RANDOM;
                TalentVnum = 0;
                TalentValue = null;
                return;
            }

            TalentType = global::TalentType.V_NUM;
            TalentVnum = checked((sbyte)value);
            TalentValue = null;
        }

        public void SetIv(Func<ParamSetRow, ParamSetRow> update)
        {
            TalentType = global::TalentType.VALUE;
            TalentVnum = 0;
            TalentValue = update(TalentValue ?? ParamSetRow.Zero);
        }

        public Offset<global::PokeDataBattle> Write(FlatBufferBuilder builder)
        {
            var wazaOffsets = Waza.Select(waza => waza?.Write(builder) ?? default).ToArray();
            var talentOffset = TalentValue?.Write(builder) ?? default;
            var effortOffset = EffortValue?.Write(builder) ?? default;
            return global::PokeDataBattle.CreatePokeDataBattle(
                builder,
                DevId,
                FormId,
                Sex,
                Item,
                Level,
                BallId,
                WazaType,
                wazaOffsets[0],
                wazaOffsets[1],
                wazaOffsets[2],
                wazaOffsets[3],
                GemType,
                Seikaku,
                Tokusei,
                TalentType,
                talentOffset,
                TalentVnum,
                effortOffset,
                RareType,
                ScaleType,
                ScaleValue);
        }
    }

    internal sealed class RaidBossSizeRow
    {
        public static RaidBossSizeRow Empty => new();

        public global::SizeType HeightType { get; set; }
        public short HeigntValue { get; set; }
        public global::SizeType WeightType { get; set; }
        public short WaightValue { get; set; }
        public global::SizeType ScaleType { get; set; }
        public short ScaleValue { get; set; }

        public static RaidBossSizeRow From(global::RaidBossSizeData row)
        {
            return new RaidBossSizeRow
            {
                HeightType = row.HeightType,
                HeigntValue = row.HeigntValue,
                WeightType = row.WeightType,
                WaightValue = row.WaightValue,
                ScaleType = row.ScaleType,
                ScaleValue = row.ScaleValue,
            };
        }

        public Offset<global::RaidBossSizeData> Write(FlatBufferBuilder builder) =>
            global::RaidBossSizeData.CreateRaidBossSizeData(
                builder,
                HeightType,
                HeigntValue,
                WeightType,
                WaightValue,
                ScaleType,
                ScaleValue);
    }

    internal sealed class RaidBossDataRow
    {
        public static RaidBossDataRow Empty => new();

        public short HpCoef { get; set; }
        public sbyte PowerChargeTrigerHp { get; set; }
        public sbyte PowerChargeTrigerTime { get; set; }
        public short PowerChargeLimitTime { get; init; }
        public sbyte PowerChargeCancelDamage { get; init; }
        public short PowerChargePenaltyTime { get; init; }
        public global::pml.common.WazaID PowerChargePenaltyAction { get; init; }
        public sbyte PowerChargeDamageRate { get; init; }
        public sbyte PowerChargeGemDamageRate { get; init; }
        public sbyte PowerChargeChangeGemDamageRate { get; init; }
        public RaidBossExtraRow?[] ExtraActions { get; } = new RaidBossExtraRow?[6];
        public sbyte DoubleActionTrigerHp { get; set; }
        public sbyte DoubleActionTrigerTime { get; set; }
        public sbyte DoubleActionRate { get; set; }

        public static RaidBossDataRow From(global::RaidBossData row)
        {
            var result = new RaidBossDataRow
            {
                HpCoef = row.HpCoef,
                PowerChargeTrigerHp = row.PowerChargeTrigerHp,
                PowerChargeTrigerTime = row.PowerChargeTrigerTime,
                PowerChargeLimitTime = row.PowerChargeLimitTime,
                PowerChargeCancelDamage = row.PowerChargeCancelDamage,
                PowerChargePenaltyTime = row.PowerChargePenaltyTime,
                PowerChargePenaltyAction = row.PowerChargePenaltyAction,
                PowerChargeDamageRate = row.PowerChargeDamageRate,
                PowerChargeGemDamageRate = row.PowerChargeGemDamageRate,
                PowerChargeChangeGemDamageRate = row.PowerChargeChangeGemDamageRate,
                DoubleActionTrigerHp = row.DoubleActionTrigerHp,
                DoubleActionTrigerTime = row.DoubleActionTrigerTime,
                DoubleActionRate = row.DoubleActionRate,
            };

            result.ExtraActions[0] = row.ExtraAction1 is { } action1 ? RaidBossExtraRow.From(action1) : null;
            result.ExtraActions[1] = row.ExtraAction2 is { } action2 ? RaidBossExtraRow.From(action2) : null;
            result.ExtraActions[2] = row.ExtraAction3 is { } action3 ? RaidBossExtraRow.From(action3) : null;
            result.ExtraActions[3] = row.ExtraAction4 is { } action4 ? RaidBossExtraRow.From(action4) : null;
            result.ExtraActions[4] = row.ExtraAction5 is { } action5 ? RaidBossExtraRow.From(action5) : null;
            result.ExtraActions[5] = row.ExtraAction6 is { } action6 ? RaidBossExtraRow.From(action6) : null;
            return result;
        }

        public Offset<global::RaidBossData> Write(FlatBufferBuilder builder)
        {
            var extraOffsets = ExtraActions.Select(extra => extra?.Write(builder) ?? default).ToArray();
            return global::RaidBossData.CreateRaidBossData(
                builder,
                HpCoef,
                PowerChargeTrigerHp,
                PowerChargeTrigerTime,
                PowerChargeLimitTime,
                PowerChargeCancelDamage,
                PowerChargePenaltyTime,
                PowerChargePenaltyAction,
                PowerChargeDamageRate,
                PowerChargeGemDamageRate,
                PowerChargeChangeGemDamageRate,
                extraOffsets[0],
                extraOffsets[1],
                extraOffsets[2],
                extraOffsets[3],
                extraOffsets[4],
                extraOffsets[5],
                DoubleActionTrigerHp,
                DoubleActionTrigerTime,
                DoubleActionRate);
        }
    }

    internal sealed record RaidBossExtraRow(
        global::RaidBossExtraTimingType Timming,
        global::RaidBossExtraActType Action,
        short Value,
        global::pml.common.WazaID Wazano)
    {
        public static RaidBossExtraRow From(global::RaidBossExtraData row) =>
            new(row.Timming, row.Action, row.Value, row.Wazano);

        public Offset<global::RaidBossExtraData> Write(FlatBufferBuilder builder) =>
            global::RaidBossExtraData.CreateRaidBossExtraData(builder, Timming, Action, Value, Wazano);
    }

    internal sealed record RaidTimeRow(
        bool IsActive,
        int GameLimit,
        int ClientLimit,
        int CommandLimit,
        int PokeReviveTime,
        int AiIntervalTime,
        int AiIntervalRand)
    {
        public static RaidTimeRow From(global::RaidTimeData row) =>
            new(row.IsActive, row.GameLimit, row.ClientLimit, row.CommandLimit, row.PokeReviveTime, row.AiIntervalTime, row.AiIntervalRand);

        public Offset<global::RaidTimeData> Write(FlatBufferBuilder builder) =>
            global::RaidTimeData.CreateRaidTimeData(builder, IsActive, GameLimit, ClientLimit, CommandLimit, PokeReviveTime, AiIntervalTime, AiIntervalRand);
    }

    internal sealed record WazaSetRow(global::pml.common.WazaID WazaId, sbyte PointUp)
    {
        public static WazaSetRow From(global::WazaSet row) => new(row.WazaId, row.PointUp);

        public Offset<global::WazaSet> Write(FlatBufferBuilder builder) =>
            global::WazaSet.CreateWazaSet(builder, WazaId, PointUp);
    }

    internal sealed record ParamSetRow(int Hp, int Atk, int Def, int SpAtk, int SpDef, int Agi)
    {
        public static readonly ParamSetRow Zero = new(0, 0, 0, 0, 0, 0);

        public static ParamSetRow From(global::ParamSet row) =>
            new(row.Hp, row.Atk, row.Def, row.SpAtk, row.SpDef, row.Agi);

        public Offset<global::ParamSet> Write(FlatBufferBuilder builder) =>
            global::ParamSet.CreateParamSet(builder, Hp, Atk, Def, SpAtk, SpDef, Agi);
    }

    internal sealed class FixedRewardTableRow
    {
        public static FixedRewardTableRow Empty => new();

        public ulong TableName { get; init; }
        public FixedRewardInfoRow?[] Rewards { get; } = new FixedRewardInfoRow?[15];

        public static FixedRewardTableRow From(global::RaidFixedRewardItem row)
        {
            var result = new FixedRewardTableRow { TableName = row.TableName };
            result.Rewards[0] = row.RewardItem00 is { } item00 ? FixedRewardInfoRow.From(item00) : null;
            result.Rewards[1] = row.RewardItem01 is { } item01 ? FixedRewardInfoRow.From(item01) : null;
            result.Rewards[2] = row.RewardItem02 is { } item02 ? FixedRewardInfoRow.From(item02) : null;
            result.Rewards[3] = row.RewardItem03 is { } item03 ? FixedRewardInfoRow.From(item03) : null;
            result.Rewards[4] = row.RewardItem04 is { } item04 ? FixedRewardInfoRow.From(item04) : null;
            result.Rewards[5] = row.RewardItem05 is { } item05 ? FixedRewardInfoRow.From(item05) : null;
            result.Rewards[6] = row.RewardItem06 is { } item06 ? FixedRewardInfoRow.From(item06) : null;
            result.Rewards[7] = row.RewardItem07 is { } item07 ? FixedRewardInfoRow.From(item07) : null;
            result.Rewards[8] = row.RewardItem08 is { } item08 ? FixedRewardInfoRow.From(item08) : null;
            result.Rewards[9] = row.RewardItem09 is { } item09 ? FixedRewardInfoRow.From(item09) : null;
            result.Rewards[10] = row.RewardItem10 is { } item10 ? FixedRewardInfoRow.From(item10) : null;
            result.Rewards[11] = row.RewardItem11 is { } item11 ? FixedRewardInfoRow.From(item11) : null;
            result.Rewards[12] = row.RewardItem12 is { } item12 ? FixedRewardInfoRow.From(item12) : null;
            result.Rewards[13] = row.RewardItem13 is { } item13 ? FixedRewardInfoRow.From(item13) : null;
            result.Rewards[14] = row.RewardItem14 is { } item14 ? FixedRewardInfoRow.From(item14) : null;
            return result;
        }

        public FixedRewardInfoRow EnsureReward(int slot)
        {
            Rewards[slot] ??= FixedRewardInfoRow.Empty;
            return Rewards[slot]!;
        }

        public Offset<global::RaidFixedRewardItem> Write(FlatBufferBuilder builder)
        {
            var offsets = Rewards.Select(reward => reward?.Write(builder) ?? default).ToArray();
            return global::RaidFixedRewardItem.CreateRaidFixedRewardItem(
                builder,
                TableName,
                offsets[0],
                offsets[1],
                offsets[2],
                offsets[3],
                offsets[4],
                offsets[5],
                offsets[6],
                offsets[7],
                offsets[8],
                offsets[9],
                offsets[10],
                offsets[11],
                offsets[12],
                offsets[13],
                offsets[14]);
        }
    }

    internal sealed class FixedRewardInfoRow
    {
        public static FixedRewardInfoRow Empty => new();

        public global::RaidRewardItemCategoryType Category { get; set; }
        public global::RaidRewardItemSubjectType SubjectType { get; set; }
        public global::ItemID ItemID { get; set; }
        public sbyte Num { get; set; }

        public static FixedRewardInfoRow From(global::RaidFixedRewardItemInfo row) =>
            new()
            {
                Category = row.Category,
                SubjectType = row.SubjectType,
                ItemID = row.ItemID,
                Num = row.Num,
            };

        public Offset<global::RaidFixedRewardItemInfo> Write(FlatBufferBuilder builder) =>
            global::RaidFixedRewardItemInfo.CreateRaidFixedRewardItemInfo(builder, Category, SubjectType, ItemID, Num);
    }

    internal sealed class LotteryRewardTableRow
    {
        public static LotteryRewardTableRow Empty => new();

        public ulong TableName { get; init; }
        public LotteryRewardInfoRow?[] Rewards { get; } = new LotteryRewardInfoRow?[30];

        public static LotteryRewardTableRow From(global::RaidLotteryRewardItem row)
        {
            var result = new LotteryRewardTableRow { TableName = row.TableName };
            result.Rewards[0] = row.RewardItem00 is { } item00 ? LotteryRewardInfoRow.From(item00) : null;
            result.Rewards[1] = row.RewardItem01 is { } item01 ? LotteryRewardInfoRow.From(item01) : null;
            result.Rewards[2] = row.RewardItem02 is { } item02 ? LotteryRewardInfoRow.From(item02) : null;
            result.Rewards[3] = row.RewardItem03 is { } item03 ? LotteryRewardInfoRow.From(item03) : null;
            result.Rewards[4] = row.RewardItem04 is { } item04 ? LotteryRewardInfoRow.From(item04) : null;
            result.Rewards[5] = row.RewardItem05 is { } item05 ? LotteryRewardInfoRow.From(item05) : null;
            result.Rewards[6] = row.RewardItem06 is { } item06 ? LotteryRewardInfoRow.From(item06) : null;
            result.Rewards[7] = row.RewardItem07 is { } item07 ? LotteryRewardInfoRow.From(item07) : null;
            result.Rewards[8] = row.RewardItem08 is { } item08 ? LotteryRewardInfoRow.From(item08) : null;
            result.Rewards[9] = row.RewardItem09 is { } item09 ? LotteryRewardInfoRow.From(item09) : null;
            result.Rewards[10] = row.RewardItem10 is { } item10 ? LotteryRewardInfoRow.From(item10) : null;
            result.Rewards[11] = row.RewardItem11 is { } item11 ? LotteryRewardInfoRow.From(item11) : null;
            result.Rewards[12] = row.RewardItem12 is { } item12 ? LotteryRewardInfoRow.From(item12) : null;
            result.Rewards[13] = row.RewardItem13 is { } item13 ? LotteryRewardInfoRow.From(item13) : null;
            result.Rewards[14] = row.RewardItem14 is { } item14 ? LotteryRewardInfoRow.From(item14) : null;
            result.Rewards[15] = row.RewardItem15 is { } item15 ? LotteryRewardInfoRow.From(item15) : null;
            result.Rewards[16] = row.RewardItem16 is { } item16 ? LotteryRewardInfoRow.From(item16) : null;
            result.Rewards[17] = row.RewardItem17 is { } item17 ? LotteryRewardInfoRow.From(item17) : null;
            result.Rewards[18] = row.RewardItem18 is { } item18 ? LotteryRewardInfoRow.From(item18) : null;
            result.Rewards[19] = row.RewardItem19 is { } item19 ? LotteryRewardInfoRow.From(item19) : null;
            result.Rewards[20] = row.RewardItem20 is { } item20 ? LotteryRewardInfoRow.From(item20) : null;
            result.Rewards[21] = row.RewardItem21 is { } item21 ? LotteryRewardInfoRow.From(item21) : null;
            result.Rewards[22] = row.RewardItem22 is { } item22 ? LotteryRewardInfoRow.From(item22) : null;
            result.Rewards[23] = row.RewardItem23 is { } item23 ? LotteryRewardInfoRow.From(item23) : null;
            result.Rewards[24] = row.RewardItem24 is { } item24 ? LotteryRewardInfoRow.From(item24) : null;
            result.Rewards[25] = row.RewardItem25 is { } item25 ? LotteryRewardInfoRow.From(item25) : null;
            result.Rewards[26] = row.RewardItem26 is { } item26 ? LotteryRewardInfoRow.From(item26) : null;
            result.Rewards[27] = row.RewardItem27 is { } item27 ? LotteryRewardInfoRow.From(item27) : null;
            result.Rewards[28] = row.RewardItem28 is { } item28 ? LotteryRewardInfoRow.From(item28) : null;
            result.Rewards[29] = row.RewardItem29 is { } item29 ? LotteryRewardInfoRow.From(item29) : null;
            return result;
        }

        public LotteryRewardInfoRow EnsureReward(int slot)
        {
            Rewards[slot] ??= LotteryRewardInfoRow.Empty;
            return Rewards[slot]!;
        }

        public Offset<global::RaidLotteryRewardItem> Write(FlatBufferBuilder builder)
        {
            var offsets = Rewards.Select(reward => reward?.Write(builder) ?? default).ToArray();
            return global::RaidLotteryRewardItem.CreateRaidLotteryRewardItem(
                builder,
                TableName,
                offsets[0],
                offsets[1],
                offsets[2],
                offsets[3],
                offsets[4],
                offsets[5],
                offsets[6],
                offsets[7],
                offsets[8],
                offsets[9],
                offsets[10],
                offsets[11],
                offsets[12],
                offsets[13],
                offsets[14],
                offsets[15],
                offsets[16],
                offsets[17],
                offsets[18],
                offsets[19],
                offsets[20],
                offsets[21],
                offsets[22],
                offsets[23],
                offsets[24],
                offsets[25],
                offsets[26],
                offsets[27],
                offsets[28],
                offsets[29]);
        }
    }

    internal sealed class LotteryRewardInfoRow
    {
        public static LotteryRewardInfoRow Empty => new();

        public global::RaidRewardItemCategoryType Category { get; set; }
        public global::ItemID ItemID { get; set; }
        public sbyte Num { get; set; }
        public int Rate { get; set; }
        public bool RareItemFlag { get; set; }

        public static LotteryRewardInfoRow From(global::RaidLotteryRewardItemInfo row) =>
            new()
            {
                Category = row.Category,
                ItemID = row.ItemID,
                Num = row.Num,
                Rate = row.Rate,
                RareItemFlag = row.RareItemFlag,
            };

        public Offset<global::RaidLotteryRewardItemInfo> Write(FlatBufferBuilder builder) =>
            global::RaidLotteryRewardItemInfo.CreateRaidLotteryRewardItemInfo(builder, Category, ItemID, Num, Rate, RareItemFlag);
    }
}
