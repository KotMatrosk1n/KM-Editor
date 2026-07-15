// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.FashionUnlock;
using KM.Api.GameDump;
using KM.Api.Gifts;
using KM.Api.HyperspaceBypass;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Shops;
using KM.Api.SpreadsheetImport;
using KM.Api.StaticEncounters;
using KM.Api.SvCache;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.Workflows;
using KM.Core.Projects;
using KM.Formats.Pokemon;
using KM.SV.Data;
using KM.SV.EvolutionItems;
using KM.SV.Workflows;
using KM.SV.Trainers;
using KM.Integration.Tests.Tools;
using KM.Formats.SV.Placement;
using KM.Formats.SwSh;
using KM.Tools.Bridge;
using Trinity = KM.Formats.SV.Generated.TrinityScene;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class ScarletVioletBridgeTests
{
    private const ulong ScarletTitleId = 0x0100A3D008C5C000;
    private const ulong VioletTitleId = 0x01008F6008C5E000;
    private const ulong TeraRaidFixedRewardTableHash = 0x1111222233334444;
    private const ulong TeraRaidLotteryRewardTableHash = 0x5555666677778888;

    private static readonly string[] TeraRaidEnemyPaths =
    [
        SvDataPaths.TeraRaidEnemyPaldea1,
        SvDataPaths.TeraRaidEnemyPaldea2,
        SvDataPaths.TeraRaidEnemyPaldea3,
        SvDataPaths.TeraRaidEnemyPaldea4,
        SvDataPaths.TeraRaidEnemyPaldea5,
        SvDataPaths.TeraRaidEnemyPaldea6,
        SvDataPaths.TeraRaidEnemyKitakami1,
        SvDataPaths.TeraRaidEnemyKitakami2,
        SvDataPaths.TeraRaidEnemyKitakami3,
        SvDataPaths.TeraRaidEnemyKitakami4,
        SvDataPaths.TeraRaidEnemyKitakami5,
        SvDataPaths.TeraRaidEnemyKitakami6,
        SvDataPaths.TeraRaidEnemyBlueberry1,
        SvDataPaths.TeraRaidEnemyBlueberry2,
        SvDataPaths.TeraRaidEnemyBlueberry3,
        SvDataPaths.TeraRaidEnemyBlueberry4,
        SvDataPaths.TeraRaidEnemyBlueberry5,
        SvDataPaths.TeraRaidEnemyBlueberry6,
        SvDataPaths.TeraRaidEnemyDelivery,
    ];

    private static readonly string[] VisibleItemScenePaths =
    [
        SvDataPaths.VisibleItemScenePaldeaScarlet,
        SvDataPaths.VisibleItemScenePaldeaViolet,
        SvDataPaths.VisibleItemSceneKitakamiScarlet,
        SvDataPaths.VisibleItemSceneKitakamiViolet,
        SvDataPaths.VisibleItemSceneBlueberryScarlet,
        SvDataPaths.VisibleItemSceneBlueberryViolet,
    ];

    private static readonly Lazy<ScarletFixtureSet> ScarletFixtures = new(
        CreateScarletFixtureSet,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IEnumerable<object[]> RepresentativeScarletVioletGame()
    {
        yield return [ProjectGameDto.Scarlet, ScarletTitleId];
    }

    public static IEnumerable<object[]> RepresentativeScarletVioletGameAndConvertedUseItemMethods()
    {
        foreach (var method in new[] { 17, 18, 42 })
        {
            yield return [ProjectGameDto.Scarlet, ScarletTitleId, method];
        }
    }

    public static IEnumerable<object[]> ScarletVioletBuilds()
    {
        yield return [ProjectGameDto.Scarlet, ScarletTitleId];
        yield return [ProjectGameDto.Violet, VioletTitleId];
    }

    [Fact]
    public void ScarletVioletCacheBridgeCommandsReturnStatusAndSettings()
    {
        using var temp = CreateScarletVioletProject(ScarletTitleId);
        var paths = temp.Paths with { SelectedGame = ProjectGameDto.Scarlet };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var initial = Dispatch<SvCacheStatusResponse>(
            dispatcher,
            KmCommandNames.GetSvCacheStatus,
            new GetSvCacheStatusRequest(paths),
            "request-sv-cache-status");

        AssertSuccess(initial);
        Assert.Equal(SvCacheModeDto.Balanced, initial.Payload!.Status.Settings.Mode);
        Assert.True(initial.Payload.Status.WarmupTotal > 0);
        foreach (var scenePath in VisibleItemScenePaths)
        {
            Assert.Contains(scenePath, SvCacheManager.WarmupVirtualPaths);
        }

        var updated = Dispatch<SvCacheStatusResponse>(
            dispatcher,
            KmCommandNames.UpdateSvCacheSettings,
            new UpdateSvCacheSettingsRequest(
                SvCacheModeDto.Performance,
                2L * 1024 * 1024 * 1024,
                paths),
            "request-sv-cache-settings");

        AssertSuccess(updated);
        Assert.Equal(SvCacheModeDto.Performance, updated.Payload!.Status.Settings.Mode);
        Assert.Equal(2L * 1024 * 1024 * 1024, updated.Payload.Status.Settings.MaxCacheSizeBytes);

        var cleared = Dispatch<SvCacheStatusResponse>(
            dispatcher,
            KmCommandNames.ClearSvCache,
            new ClearSvCacheRequest(paths),
            "request-sv-cache-clear");

        AssertSuccess(cleared);
        Assert.Equal(SvCacheModeDto.Performance, cleared.Payload!.Status.Settings.Mode);
    }

    [Fact]
    public void ScarletVioletCacheBridgeCommandsRejectSwordProjectPaths()
    {
        using var temp = CreateScarletVioletProject(ScarletTitleId);
        var paths = temp.Paths with { SelectedGame = ProjectGameDto.Sword };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var response = Dispatch<SvCacheStatusResponse>(
            dispatcher,
            KmCommandNames.GetSvCacheStatus,
            new GetSvCacheStatusRequest(paths),
            "request-sv-cache-mismatch");

        Assert.NotNull(response.Error);
        Assert.Equal("bridge.gameMismatch", response.Error.Code);
    }

    [Fact]
    public void ScarletVioletTeraBehaviorSummaryDoesNotTreatEveryGemTypeAsTarget()
    {
        var gymLeaderTeam = new[]
        {
            CreateSvTrainerPokemon(slot: 0, "Wattrel", global::GemType.DENKI),
            CreateSvTrainerPokemon(slot: 1, "Bellibolt", global::GemType.DENKI),
            CreateSvTrainerPokemon(slot: 2, "Luxio", global::GemType.DENKI),
            CreateSvTrainerPokemon(slot: 3, "Mismagius", global::GemType.DENKI),
        };

        Assert.Equal(
            "Enabled; target is battle controlled. Fixed Tera types: Slot 1: Wattrel (Electric); Slot 2: Bellibolt (Electric); Slot 3: Luxio (Electric); Slot 4: Mismagius (Electric).",
            SvTrainersWorkflowService.FormatTeraTarget(canTerastallize: true, gymLeaderTeam));

        var singleFixedSlotTeam = new[]
        {
            CreateSvTrainerPokemon(slot: 5, "Sylveon", global::GemType.FAIRY),
        };

        Assert.Equal(
            "Enabled; only fixed Tera type is Slot 6: Sylveon (Fairy).",
            SvTrainersWorkflowService.FormatTeraTarget(canTerastallize: true, singleFixedSlotTeam));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletProjectRoutesHiddenBridgeEditorsAndAppliesLooseOutputs(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var itemsSession = UpdateItem(dispatcher, paths, itemId: 1, field: "buyPrice", value: "777");
        Apply(dispatcher, paths, itemsSession);
        Assert.Equal(777, ReadItemPrice(temp, itemId: 1));

        var movesSession = UpdateMove(dispatcher, paths, moveId: 33, field: "power", value: "50");
        movesSession = UpdateMove(dispatcher, paths, movesSession, moveId: 33, field: "punch", value: "1");
        Apply(dispatcher, paths, movesSession);
        var move = ReadMove(temp, moveId: 33);
        Assert.Equal(50, move.Power);
        Assert.True(move.FlagPunch);

        var pokemonSession = UpdatePokemonField(dispatcher, paths, personalId: 1, field: "hp", value: "46");
        pokemonSession = UpdatePokemonLearnset(dispatcher, paths, pokemonSession, personalId: 1, slot: 0, moveId: 45, level: 7);
        pokemonSession = UpdatePokemonEvolution(dispatcher, paths, pokemonSession, personalId: 1, slot: 0, level: 18);
        Apply(dispatcher, paths, pokemonSession);
        var personal = ReadPersonal(temp, personalId: 1);
        Assert.Equal(46, personal.BaseStats!.Value.Hp);
        Assert.Equal(45, personal.LevelupMoves(0)!.Value.Move);
        Assert.Equal(7, personal.LevelupMoves(0)!.Value.Level);
        Assert.Equal(18, personal.Evolutions(0)!.Value.Level);

        var trainersSession = UpdateTrainer(dispatcher, paths, trainerId: 0, slot: 0, field: "level", value: "12");
        Apply(dispatcher, paths, trainersSession);
        Assert.Equal(12, ReadTrainerPokemonLevel(temp, trainerId: 0, slot: 0));
        var teraSession = UpdateTrainer(dispatcher, paths, trainerId: 0, slot: 0, field: "teraType", value: ((int)global::GemType.FAIRY).ToString());
        Apply(dispatcher, paths, teraSession);
        Assert.Equal(global::GemType.FAIRY, ReadTrainerPokemonTeraType(temp, trainerId: 0, slot: 0));

        var shopItemSession = UpdateShop(dispatcher, paths, "lineup:shop_00_lineup", slot: 1, field: "itemId", value: "4");
        Apply(dispatcher, paths, shopItemSession);
        Assert.Equal(4, ReadFriendlyShopItemId(temp, "shop_00_lineup", slot: 1));

        var tmShopSession = UpdateShop(dispatcher, paths, "tm:1", slot: 1, field: "lpCost", value: "900");
        tmShopSession = UpdateShop(dispatcher, paths, tmShopSession, "tm:1", slot: 1, field: "material1Count", value: "3");
        Apply(dispatcher, paths, tmShopSession);
        var tmRow = ReadTechnicalMachineRow(temp, AddRegion.TITAN, slot: 1);
        Assert.Equal(900, tmRow.LpCost);
        Assert.Equal(3, tmRow.Material1Count);

        var encountersWorkflow = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-sv-encounters-load");
        AssertSuccess(encountersWorkflow);
        var encounterTableId = Assert.Single(encountersWorkflow.Payload!.Workflow.Tables).TableId;
        var encountersSession = UpdateEncounter(dispatcher, paths, encounterTableId, slot: 0, field: "levelMin", value: "9");
        Apply(dispatcher, paths, encountersSession);
        Assert.Equal(9, ReadEncounterMinLevel(temp, index: 0));
        AssertDescriptorMarksLayeredOutputs(temp);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletShopEditsStayOnTheirSourceRows(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.ShopWazaMachineDataArray,
            CreateTechnicalMachineShopDataArray(includeSecondRow: true));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var session = UpdateShop(
            dispatcher,
            paths,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "sortOrder",
            value: "2");
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "conditionKind",
            value: ((int)CondEnum.SCENARIO).ToString(CultureInfo.InvariantCulture));
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "tm:1",
            slot: 1,
            field: "region",
            value: ((int)AddRegion.SUDACHI1).ToString(CultureInfo.InvariantCulture));
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "tm:1",
            slot: 1,
            field: "lpCost",
            value: "901");
        Assert.All(session.PendingEdits, edit => Assert.Contains("#row:source:", edit.RecordId, StringComparison.Ordinal));

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session),
            "request-sv-shop-source-row-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan),
            "request-sv-shop-source-row-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var friendlyRows = KM.SV.Shops.SvShopsWorkflowService
            .ReadFriendlyRows(ReadSvOutput(temp, SvDataPaths.FriendlyShopLineupDataArray))
            .OrderBy(row => row.SourceIndex)
            .ToArray();
        Assert.Equal(2, friendlyRows[0].SortNum);
        Assert.Equal(CondEnum.SCENARIO, friendlyRows[0].ConditionKind);
        Assert.Equal(1, friendlyRows[1].SortNum);
        Assert.Equal(CondEnum.GYMBADGENUM, friendlyRows[1].ConditionKind);

        var tmRows = KM.SV.Shops.SvShopsWorkflowService
            .ReadTechnicalMachineRows(ReadSvOutput(temp, SvDataPaths.ShopWazaMachineDataArray))
            .OrderBy(row => row.SourceIndex)
            .ToArray();
        Assert.Equal(AddRegion.SUDACHI1, tmRows[0].Region);
        Assert.Equal(901, tmRows[0].LpCost);
        Assert.Equal(AddRegion.TITAN, tmRows[1].Region);
        Assert.Equal(1_200, tmRows[1].LpCost);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletShopInventoryKeepsRowMetadata(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.FriendlyShopLineupDataArray,
            CreateFriendlyShopLineupDataArray(firstSort: 10, secondSort: 30, includeUnrelatedRow: true));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        const string removedSourceInventoryValue = """
            {"version":1,"updateOrder":true,"rows":[{"rowId":"source:1","itemId":2}]}
            """;
        const string inventoryValue = """
            {"version":1,"updateOrder":false,"rows":[{"rowId":"source:0","itemId":4},{"rowId":"source:1","itemId":4}]}
            """;

        var session = UpdateShop(
            dispatcher,
            paths,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "setInventory",
            value: removedSourceInventoryValue);
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "setInventory",
            value: inventoryValue);
        Apply(dispatcher, paths, session);

        var rows = KM.SV.Shops.SvShopsWorkflowService
            .ReadFriendlyRows(ReadSvOutput(temp, SvDataPaths.FriendlyShopLineupDataArray))
            .ToArray();
        Assert.Equal(["shop_00_lineup", "shop_00_lineup", "shop_99_lineup"], rows.Select(row => row.LineupId));
        Assert.Equal(4, rows[0].ItemId);
        Assert.Equal(10, rows[0].SortNum);
        Assert.Equal(CondEnum.NONE, rows[0].ConditionKind);
        Assert.Equal(4, rows[1].ItemId);
        Assert.Equal(30, rows[1].SortNum);
        Assert.Equal(CondEnum.GYMBADGENUM, rows[1].ConditionKind);
        Assert.Equal(1, rows[1].GymBadgeNum);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletShopInventoryRemainsStableAcrossRepeatedSaves(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        const string firstInventoryValue = """
            {"version":1,"updateOrder":true,"rows":[{"rowId":"source:1","itemId":2},{"rowId":"new:1","itemId":4},{"rowId":"source:0","itemId":1}]}
            """;
        const string finalInventoryValue = """
            {"version":1,"updateOrder":false,"rows":[{"rowId":"new:1","itemId":4},{"rowId":"source:1","itemId":2}]}
            """;

        var session = UpdateShop(
            dispatcher,
            paths,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "setInventory",
            value: firstInventoryValue);
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "conditionKind",
            value: ((int)CondEnum.SCENARIO).ToString(CultureInfo.InvariantCulture),
            rowId: "source:0");
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 2,
            field: "conditionKind",
            value: ((int)CondEnum.GYMBADGENUM).ToString(CultureInfo.InvariantCulture),
            rowId: "new:1");
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 3,
            field: "gymBadgeCount",
            value: "5",
            rowId: "source:1");
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "setInventory",
            value: finalInventoryValue);
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "gymBadgeCount",
            value: "6",
            rowId: "source:1");

        Assert.Equal(3, session.PendingEdits.Count);
        Assert.Single(session.PendingEdits, edit => edit.Field == "setInventory" && edit.NewValue == finalInventoryValue);
        Assert.Single(session.PendingEdits, edit => edit.RecordId?.Contains("#row:new:1", StringComparison.Ordinal) == true);
        Assert.Single(session.PendingEdits, edit =>
            edit.RecordId?.Contains("#row:source:1", StringComparison.Ordinal) == true
            && edit.Field == "gymBadgeCount"
            && edit.NewValue == "6");
        Assert.DoesNotContain(session.PendingEdits, edit => edit.RecordId?.Contains("#row:source:0", StringComparison.Ordinal) == true);

        Apply(dispatcher, paths, session);
        var rows = KM.SV.Shops.SvShopsWorkflowService
            .ReadFriendlyRows(ReadSvOutput(temp, SvDataPaths.FriendlyShopLineupDataArray))
            .Where(row => row.LineupId == "shop_00_lineup")
            .OrderBy(row => row.SortNum)
            .ThenBy(row => row.SourceIndex)
            .ToArray();
        Assert.Equal([4, 2], rows.Select(row => row.ItemId));
        Assert.Equal([0, 1], rows.Select(row => row.SortNum));
        Assert.Equal(CondEnum.GYMBADGENUM, rows[0].ConditionKind);
        Assert.Equal(CondEnum.GYMBADGENUM, rows[1].ConditionKind);
        Assert.Equal(6, rows[1].GymBadgeNum);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletLegacyShopInventoryKeepsEditOrder(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var session = UpdateShop(
            dispatcher,
            paths,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "itemId",
            value: "4");
        session = UpdateShop(
            dispatcher,
            paths,
            session,
            "lineup:shop_00_lineup",
            slot: 1,
            field: "setInventory",
            value: "1,2");
        Apply(dispatcher, paths, session);

        Assert.Equal(1, ReadFriendlyShopItemId(temp, "shop_00_lineup", slot: 1));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletChangePlansCanOutputForTrinityModManager(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        var session = UpdateItem(dispatcher, paths, itemId: 1, field: "buyPrice", value: "888");

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-trinity-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        Assert.Equal(SvDataPaths.ItemDataArray, write.TargetRelativePath);
        Assert.DoesNotContain(plan.Payload.ChangePlan.Writes, candidate =>
            string.Equals(candidate.TargetRelativePath, "romfs/arc/data.trpfd", StringComparison.Ordinal));

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-trinity-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal([SvDataPaths.ItemDataArray], apply.Payload.ApplyResult.WrittenFiles);
        Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, SvDataPaths.ItemDataArray.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "arc", "data.trpfd")));

        var loaded = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-sv-trinity-load");
        AssertSuccess(loaded);
        Assert.Equal(888, loaded.Payload!.Workflow.Items.Single(item => item.ItemId == 1).BuyPrice);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletShopOutputsRespectAllRomFsOutputModes(
        ProjectGameDto game,
        ulong titleId)
    {
        foreach (var outputMode in new[]
        {
            ChangePlanOutputModeDto.Standalone,
            ChangePlanOutputModeDto.TrinityBypass,
            ChangePlanOutputModeDto.TrinityModManager,
        })
        {
            using var temp = CreateScarletVioletProject(titleId);
            WriteScarletFixtures(temp);
            var paths = temp.Paths with { SelectedGame = game };
            var dispatcher = CreateDispatcherWithSvCache(temp);
            var session = UpdateShop(dispatcher, paths, "lineup:shop_00_lineup", slot: 1, field: "itemId", value: "4");

            var plan = Dispatch<CreateChangePlanResponse>(
                dispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(paths, session, outputMode),
                $"request-sv-shop-{outputMode}-plan");
            AssertSuccess(plan);
            Assert.True(plan.Payload!.ChangePlan.CanApply);

            var dataTarget = outputMode == ChangePlanOutputModeDto.TrinityModManager
                ? SvDataPaths.FriendlyShopLineupDataArray
                : $"romfs/{SvDataPaths.FriendlyShopLineupDataArray}";
            Assert.Contains(plan.Payload.ChangePlan.Writes, write =>
                string.Equals(write.TargetRelativePath, dataTarget, StringComparison.Ordinal));
            if (outputMode == ChangePlanOutputModeDto.Standalone)
            {
                Assert.Contains(plan.Payload.ChangePlan.Writes, write =>
                    string.Equals(write.TargetRelativePath, "romfs/arc/data.trpfd", StringComparison.Ordinal));
            }
            else
            {
                Assert.DoesNotContain(plan.Payload.ChangePlan.Writes, write =>
                    string.Equals(write.TargetRelativePath, "romfs/arc/data.trpfd", StringComparison.Ordinal));
            }

            var apply = Dispatch<ApplyChangePlanResponse>(
                dispatcher,
                KmCommandNames.ApplyChangePlan,
                new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan, outputMode),
                $"request-sv-shop-{outputMode}-apply");
            AssertSuccess(apply);
            Assert.DoesNotContain(
                apply.Payload!.ApplyResult.Diagnostics,
                diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
            Assert.Contains(dataTarget, apply.Payload.ApplyResult.WrittenFiles);
            if (outputMode == ChangePlanOutputModeDto.Standalone)
            {
                Assert.Contains("romfs/arc/data.trpfd", apply.Payload.ApplyResult.WrittenFiles);
                Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "arc", "data.trpfd")));
            }
            else
            {
                Assert.DoesNotContain("romfs/arc/data.trpfd", apply.Payload.ApplyResult.WrittenFiles);
                Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "arc", "data.trpfd")));
            }

            Assert.Equal(4, ReadFriendlyShopItemId(temp, "shop_00_lineup", slot: 1));
        }
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletChangePlansCanOutputForTrinityBypass(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        var session = UpdateItem(dispatcher, paths, itemId: 1, field: "buyPrice", value: "889");

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session, ChangePlanOutputModeDto.TrinityBypass),
            "request-sv-trinity-bypass-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        var bypassRelativePath = $"romfs/{SvDataPaths.ItemDataArray}";
        Assert.Equal(bypassRelativePath, write.TargetRelativePath);
        Assert.DoesNotContain(plan.Payload.ChangePlan.Writes, candidate =>
            string.Equals(candidate.TargetRelativePath, "romfs/arc/data.trpfd", StringComparison.Ordinal));

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityBypass),
            "request-sv-trinity-bypass-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal([bypassRelativePath], apply.Payload.ApplyResult.WrittenFiles);
        Assert.True(File.Exists(Path.Combine(
            temp.OutputRootPath,
            bypassRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "arc", "data.trpfd")));

        var loaded = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-sv-trinity-bypass-load");
        AssertSuccess(loaded);
        Assert.Equal(889, loaded.Payload!.Workflow.Items.Single(item => item.ItemId == 1).BuyPrice);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletGiftPokemonLoadsStagesAndOutputsForTrinityModManager(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadGiftPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGiftPokemonWorkflow,
            new LoadGiftPokemonWorkflowRequest(paths),
            "request-sv-gift-load");
        AssertSuccess(loaded);
        Assert.Equal("sv", loaded.Payload!.Workflow.EditorFamily);
        var gift = Assert.Single(loaded.Payload.Workflow.Gifts);
        Assert.Equal(0, gift.GiftIndex);
        Assert.Equal("Bulbasaur", gift.Species);
        Assert.Equal("Normal", gift.TeraTypeLabel);
        Assert.Equal(33, gift.Moves[0].MoveId);
        Assert.Equal("Tackle", gift.Moves[0].Move);

        var session = UpdateGiftPokemon(dispatcher, paths, gift.GiftIndex, "move1Id", "45");
        session = UpdateGiftPokemon(
            dispatcher,
            paths,
            session,
            gift.GiftIndex,
            "teraType",
            ((int)global::GemType.FAIRY).ToString(CultureInfo.InvariantCulture));

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-gift-trinity-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        Assert.Equal(SvDataPaths.EventAddPokemonArray, write.TargetRelativePath);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-gift-trinity-apply");
        AssertSuccess(apply);
        Assert.Equal([SvDataPaths.EventAddPokemonArray], apply.Payload!.ApplyResult.WrittenFiles);
        Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, SvDataPaths.EventAddPokemonArray.Replace('/', Path.DirectorySeparatorChar))));

        var output = ReadGiftPokemon(temp, giftIndex: 0);
        Assert.True(output.PokedexRegistration);
        Assert.NotNull(output.PokeData);
        Assert.Equal(global::WazaType.MANUAL, output.PokeData!.Value.WazaType);
        Assert.Equal((global::pml.common.WazaID)45, output.PokeData.Value.Waza1!.Value.WazaId);
        Assert.Equal(global::GemType.FAIRY, output.PokeData.Value.GemType);
        Assert.Equal(50, output.PokeData.Value.Friendship);
        Assert.Equal((sbyte)5, output.PokeData.Value.WazaConfirmLevel);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletGiftPokemonPendingSpeciesEditsRefreshPreviewLabelsAndDerivedFields(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadGiftPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGiftPokemonWorkflow,
            new LoadGiftPokemonWorkflowRequest(paths),
            "request-sv-gift-refresh-load");
        AssertSuccess(loaded);
        var gift = Assert.Single(loaded.Payload!.Workflow.Gifts);
        Assert.Equal("Bulbasaur", gift.Species);
        Assert.Equal("Overgrow (Ability 1)", gift.AbilityLabel);

        var updated = Dispatch<UpdateGiftPokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateGiftPokemonField,
            new UpdateGiftPokemonFieldRequest(paths, Session: null, gift.GiftIndex, "species", "4"),
            "request-sv-gift-refresh-species");
        AssertSuccess(updated);

        var updatedGift = Assert.Single(updated.Payload!.Workflow.Gifts);
        Assert.Equal(4, updatedGift.SpeciesId);
        Assert.Equal("Charmander", updatedGift.Species);
        Assert.Equal("Gift 1: Charmander Lv. 5", updatedGift.Label);
        Assert.Equal("Blaze (Ability 1)", updatedGift.AbilityLabel);
        Assert.Contains(
            updatedGift.AbilityOptions,
            option => option.Value == (int)global::TokuseiType.SET_1 && option.Label == "Blaze (Ability 1)");

        var updatedAbility = Dispatch<UpdateGiftPokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateGiftPokemonField,
            new UpdateGiftPokemonFieldRequest(
                paths,
                updated.Payload.Session,
                gift.GiftIndex,
                "ability",
                ((int)global::TokuseiType.SET_3).ToString(CultureInfo.InvariantCulture)),
            "request-sv-gift-refresh-ability");
        AssertSuccess(updatedAbility);

        var updatedAbilityGift = Assert.Single(updatedAbility.Payload!.Workflow.Gifts);
        Assert.Equal(4, updatedAbilityGift.SpeciesId);
        Assert.Equal("Charmander", updatedAbilityGift.Species);
        Assert.Equal("Solar Power (Hidden Ability)", updatedAbilityGift.AbilityLabel);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTradePokemonLoadsStagesAndOutputsForTrinityModManager(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadTradePokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTradePokemonWorkflow,
            new LoadTradePokemonWorkflowRequest(paths),
            "request-sv-trade-load");
        AssertSuccess(loaded);
        Assert.Equal("sv", loaded.Payload!.Workflow.EditorFamily);
        var trade = Assert.Single(loaded.Payload.Workflow.Trades);
        Assert.Equal(0, trade.TradeIndex);
        Assert.Equal("Bulbasaur", trade.Species);
        Assert.Equal("Ivysaur", trade.RequiredSpecies);
        Assert.Equal(-1, trade.RequiredForm);
        Assert.Equal("Normal", trade.TeraTypeLabel);
        Assert.Equal("Fixed value", trade.ScaleModeLabel);
        Assert.Equal(123, trade.ScaleValue);
        Assert.Equal(33, trade.Moves[0].MoveId);
        Assert.Equal("Tackle", trade.Moves[0].Move);
        Assert.Contains(loaded.Payload.Workflow.EditableFields, field => field.Field == "move1Id");
        Assert.Contains(loaded.Payload.Workflow.EditableFields, field => field.Field == "requiredSpecies");
        Assert.Contains(
            loaded.Payload.Workflow.EditableFields,
            field => field.Field == "requiredForm" && field.MinimumValue == short.MinValue);
        Assert.DoesNotContain(loaded.Payload.Workflow.EditableFields, field => field.Field == "relearnMove0");

        var session = UpdateTradePokemon(dispatcher, paths, trade.TradeIndex, "move1Id", "45");
        session = UpdateTradePokemon(
            dispatcher,
            paths,
            session,
            trade.TradeIndex,
            "teraType",
            ((int)global::GemType.FAIRY).ToString(CultureInfo.InvariantCulture));
        session = UpdateTradePokemon(dispatcher, paths, session, trade.TradeIndex, "requiredSpecies", "4");

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-trade-trinity-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Equal(2, plan.Payload.ChangePlan.Writes.Count);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == SvDataPaths.EventTradeListArray);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == SvDataPaths.EventTradePokemonArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-trade-trinity-apply");
        AssertSuccess(apply);
        Assert.Equal(
            [SvDataPaths.EventTradeListArray, SvDataPaths.EventTradePokemonArray],
            apply.Payload!.ApplyResult.WrittenFiles);
        Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, SvDataPaths.EventTradeListArray.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, SvDataPaths.EventTradePokemonArray.Replace('/', Path.DirectorySeparatorChar))));

        var outputList = ReadTradeList(temp, index: 0);
        Assert.Equal("test_trade_bulbasaur", outputList.ReceivePoke);
        Assert.Equal((global::pml.common.DevID)4, outputList.SendPokeDevId);
        Assert.Equal(-1, outputList.SendPokeFormId);

        var outputPokemon = ReadTradePokemon(temp, tradeIndex: 0);
        Assert.Equal("test_trade_bulbasaur", outputPokemon.Label);
        Assert.NotNull(outputPokemon.PokeData);
        Assert.Equal(global::WazaType.MANUAL, outputPokemon.PokeData!.Value.WazaType);
        Assert.Equal((global::pml.common.WazaID)45, outputPokemon.PokeData.Value.Waza1!.Value.WazaId);
        Assert.Equal(global::GemType.FAIRY, outputPokemon.PokeData.Value.GemType);
        Assert.Equal(123, outputPokemon.PokeData.Value.ScaleValue);
        Assert.Equal(123456, outputPokemon.PokeData.Value.TrainerId);
        Assert.Equal(global::SexType.FEMALE, outputPokemon.PokeData.Value.ParentSex);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTradePokemonPendingSpeciesEditsRefreshPreviewLabelsAndDerivedFields(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadTradePokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTradePokemonWorkflow,
            new LoadTradePokemonWorkflowRequest(paths),
            "request-sv-trade-refresh-load");
        AssertSuccess(loaded);
        var trade = Assert.Single(loaded.Payload!.Workflow.Trades);
        Assert.Equal("Bulbasaur", trade.Species);
        Assert.Equal("Ivysaur", trade.RequiredSpecies);
        Assert.Equal("Overgrow (Ability 1)", trade.AbilityLabel);

        var updatedSpecies = Dispatch<UpdateTradePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonField,
            new UpdateTradePokemonFieldRequest(paths, Session: null, trade.TradeIndex, "species", "4"),
            "request-sv-trade-refresh-species");
        AssertSuccess(updatedSpecies);

        var speciesTrade = Assert.Single(updatedSpecies.Payload!.Workflow.Trades);
        Assert.Equal(4, speciesTrade.SpeciesId);
        Assert.Equal("Charmander", speciesTrade.Species);
        Assert.Equal("Trade 1: Ivysaur -> Charmander Lv. 15", speciesTrade.Label);
        Assert.Equal("Blaze (Ability 1)", speciesTrade.AbilityLabel);
        Assert.Contains(
            speciesTrade.AbilityOptions,
            option => option.Value == (int)global::TokuseiType.SET_1 && option.Label == "Blaze (Ability 1)");

        var updatedRequest = Dispatch<UpdateTradePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonField,
            new UpdateTradePokemonFieldRequest(
                paths,
                updatedSpecies.Payload.Session,
                trade.TradeIndex,
                "requiredSpecies",
                "1"),
            "request-sv-trade-refresh-request");
        AssertSuccess(updatedRequest);

        var requestedTrade = Assert.Single(updatedRequest.Payload!.Workflow.Trades);
        Assert.Equal(4, requestedTrade.SpeciesId);
        Assert.Equal("Charmander", requestedTrade.Species);
        Assert.Equal(1, requestedTrade.RequiredSpeciesId);
        Assert.Equal("Bulbasaur", requestedTrade.RequiredSpecies);
        Assert.Equal("Trade 1: Bulbasaur -> Charmander Lv. 15", requestedTrade.Label);

        var updatedAbility = Dispatch<UpdateTradePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonField,
            new UpdateTradePokemonFieldRequest(
                paths,
                updatedRequest.Payload.Session,
                trade.TradeIndex,
                "ability",
                ((int)global::TokuseiType.SET_3).ToString(CultureInfo.InvariantCulture)),
            "request-sv-trade-refresh-ability");
        AssertSuccess(updatedAbility);

        var updatedAbilityTrade = Assert.Single(updatedAbility.Payload!.Workflow.Trades);
        Assert.Equal(4, updatedAbilityTrade.SpeciesId);
        Assert.Equal("Charmander", updatedAbilityTrade.Species);
        Assert.Equal("Bulbasaur", updatedAbilityTrade.RequiredSpecies);
        Assert.Equal("Solar Power (Hidden Ability)", updatedAbilityTrade.AbilityLabel);
    }

    [Theory]
    [MemberData(nameof(ScarletVioletBuilds))]
    public void ScarletVioletHyperspaceBypassStagesStandaloneMainAndRejectsTrinityOutput(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseExeFsFile("main", SvHyperspaceBypassBridgeFixtures.CreateCompatibleMain(game));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var load = Dispatch<LoadHyperspaceBypassWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadHyperspaceBypassWorkflow,
            new LoadHyperspaceBypassWorkflowRequest(paths),
            "request-sv-hyperspace-load");
        AssertSuccess(load);
        Assert.Equal("available", load.Payload!.Workflow.InstallStatus);
        Assert.Equal(game, load.Payload.Workflow.DetectedGame);
        Assert.Equal("main.text+0x02873A50", load.Payload.Workflow.PatchOffsetHex);
        Assert.Contains(load.Payload.Workflow.ReservedRegions, region => region.RegionId == "hyperspace-hoopa-runtime-gate");

        var stage = Dispatch<StageHyperspaceBypassInstallResponse>(
            dispatcher,
            KmCommandNames.StageHyperspaceBypassInstall,
            new StageHyperspaceBypassInstallRequest(paths, Session: null),
            "request-sv-hyperspace-stage");
        AssertSuccess(stage);
        Assert.Single(stage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.hyperspaceBypass", stage.Payload.Session.PendingEdits[0].Domain);
        Assert.DoesNotContain(stage.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validation = Dispatch<ValidateEditSessionResponse>(
            dispatcher,
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(paths, stage.Payload.Session),
            "request-sv-hyperspace-validate");
        AssertSuccess(validation);
        Assert.True(validation.Payload!.IsValid);

        foreach (var outputMode in new[]
        {
            ChangePlanOutputModeDto.TrinityModManager,
            ChangePlanOutputModeDto.TrinityBypass,
        })
        {
            var romFsPlan = Dispatch<CreateChangePlanResponse>(
                dispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(paths, stage.Payload.Session, outputMode),
                $"request-sv-hyperspace-{outputMode}-plan");
            AssertSuccess(romFsPlan);
            Assert.False(romFsPlan.Payload!.ChangePlan.CanApply);
            Assert.Empty(romFsPlan.Payload.ChangePlan.Writes);
            Assert.Contains(
                romFsPlan.Payload.ChangePlan.Diagnostics,
                diagnostic => diagnostic.Message.Contains("outside Scarlet/Violet RomFS output modes", StringComparison.Ordinal));
        }

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, stage.Payload.Session),
            "request-sv-hyperspace-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        Assert.Equal("exefs/main", write.TargetRelativePath);

        var baseMainPath = Path.Combine(temp.BaseExeFsPath, "main");
        var baseMainBytes = File.ReadAllBytes(baseMainPath);
        Assert.Equal(SvHyperspaceBypassBridgeFixtures.VanillaSpeciesCompare, SvHyperspaceBypassBridgeFixtures.ReadPatchInstruction(baseMainBytes));

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, stage.Payload.Session, plan.Payload.ChangePlan),
            "request-sv-hyperspace-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains("exefs/main", apply.Payload.ApplyResult.WrittenFiles);
        Assert.Equal(baseMainBytes, File.ReadAllBytes(baseMainPath));

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputMainBytes = File.ReadAllBytes(outputMainPath);
        Assert.Equal(SvHyperspaceBypassBridgeFixtures.BypassBranch, SvHyperspaceBypassBridgeFixtures.ReadPatchInstruction(outputMainBytes));

        var installed = Dispatch<LoadHyperspaceBypassWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadHyperspaceBypassWorkflow,
            new LoadHyperspaceBypassWorkflowRequest(paths),
            "request-sv-hyperspace-installed-load");
        AssertSuccess(installed);
        Assert.Equal("installed", installed.Payload!.Workflow.InstallStatus);
        Assert.Equal(ProjectFileLayerDto.Layered, installed.Payload.Workflow.Provenance.SourceLayer);

        var uninstallStage = Dispatch<StageHyperspaceBypassUninstallResponse>(
            dispatcher,
            KmCommandNames.StageHyperspaceBypassUninstall,
            new StageHyperspaceBypassUninstallRequest(paths, Session: null),
            "request-sv-hyperspace-uninstall-stage");
        AssertSuccess(uninstallStage);
        Assert.Single(uninstallStage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.hyperspaceBypass", uninstallStage.Payload.Session.PendingEdits[0].Domain);

        var uninstallPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, uninstallStage.Payload.Session),
            "request-sv-hyperspace-uninstall-plan");
        AssertSuccess(uninstallPlan);
        Assert.True(uninstallPlan.Payload!.ChangePlan.CanApply);

        var uninstallApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, uninstallStage.Payload.Session, uninstallPlan.Payload.ChangePlan),
            "request-sv-hyperspace-uninstall-apply");
        AssertSuccess(uninstallApply);
        Assert.DoesNotContain(uninstallApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.False(File.Exists(outputMainPath));
    }

    [Theory]
    [MemberData(nameof(ScarletVioletBuilds))]
    public void ScarletVioletFashionUnlockStagesStandaloneMainAndRejectsTrinityOutput(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseExeFsFile("main", SvFashionUnlockBridgeFixtures.CreateCompatibleMain(game));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var load = Dispatch<LoadFashionUnlockWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadFashionUnlockWorkflow,
            new LoadFashionUnlockWorkflowRequest(paths),
            "request-sv-fashion-load");
        AssertSuccess(load);
        Assert.Equal("available", load.Payload!.Workflow.InstallStatus);
        Assert.Equal("sv", load.Payload.Workflow.EditorFamily);
        Assert.Equal(game, load.Payload.Workflow.DetectedGame);
        Assert.Equal("main.text+0x00EAE95C", load.Payload.Workflow.OwnershipCheckOffsetHex);
        Assert.Equal(string.Empty, load.Payload.Workflow.DirectGetterOffsetHex);
        Assert.Equal(string.Empty, load.Payload.Workflow.MappedGetterOffsetHex);
        Assert.Contains(load.Payload.Workflow.ReservedRegions, region => region.RegionId == "fashion-unlock-sv-dressup-ownership-check");

        var stage = Dispatch<StageFashionUnlockInstallResponse>(
            dispatcher,
            KmCommandNames.StageFashionUnlockInstall,
            new StageFashionUnlockInstallRequest(paths, Session: null),
            "request-sv-fashion-stage");
        AssertSuccess(stage);
        Assert.Single(stage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.fashionUnlock", stage.Payload.Session.PendingEdits[0].Domain);
        Assert.DoesNotContain(stage.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validation = Dispatch<ValidateEditSessionResponse>(
            dispatcher,
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(paths, stage.Payload.Session),
            "request-sv-fashion-validate");
        AssertSuccess(validation);
        Assert.True(validation.Payload!.IsValid);

        foreach (var outputMode in new[]
        {
            ChangePlanOutputModeDto.TrinityModManager,
            ChangePlanOutputModeDto.TrinityBypass,
        })
        {
            var romFsPlan = Dispatch<CreateChangePlanResponse>(
                dispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(paths, stage.Payload.Session, outputMode),
                $"request-sv-fashion-{outputMode}-plan");
            AssertSuccess(romFsPlan);
            Assert.False(romFsPlan.Payload!.ChangePlan.CanApply);
            Assert.Empty(romFsPlan.Payload.ChangePlan.Writes);
            Assert.Contains(
                romFsPlan.Payload.ChangePlan.Diagnostics,
                diagnostic => diagnostic.Message.Contains("outside Scarlet/Violet RomFS output modes", StringComparison.Ordinal));
        }

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, stage.Payload.Session),
            "request-sv-fashion-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        Assert.Equal("exefs/main", write.TargetRelativePath);

        var baseMainPath = Path.Combine(temp.BaseExeFsPath, "main");
        var baseMainBytes = File.ReadAllBytes(baseMainPath);
        Assert.Equal(
            (SvFashionUnlockBridgeFixtures.VanillaOwnershipCheckEntryFirst, SvFashionUnlockBridgeFixtures.VanillaOwnershipCheckEntrySecond),
            SvFashionUnlockBridgeFixtures.ReadPatchInstructions(baseMainBytes));

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, stage.Payload.Session, plan.Payload.ChangePlan),
            "request-sv-fashion-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains("exefs/main", apply.Payload.ApplyResult.WrittenFiles);
        Assert.Equal(baseMainBytes, File.ReadAllBytes(baseMainPath));

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputMainBytes = File.ReadAllBytes(outputMainPath);
        Assert.Equal(
            (SvFashionUnlockBridgeFixtures.ReturnTrueFirst, SvFashionUnlockBridgeFixtures.ReturnTrueSecond),
            SvFashionUnlockBridgeFixtures.ReadPatchInstructions(outputMainBytes));

        var installed = Dispatch<LoadFashionUnlockWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadFashionUnlockWorkflow,
            new LoadFashionUnlockWorkflowRequest(paths),
            "request-sv-fashion-installed-load");
        AssertSuccess(installed);
        Assert.Equal("installed", installed.Payload!.Workflow.InstallStatus);
        Assert.Equal(ProjectFileLayerDto.Layered, installed.Payload.Workflow.Provenance.SourceLayer);

        var uninstallStage = Dispatch<StageFashionUnlockUninstallResponse>(
            dispatcher,
            KmCommandNames.StageFashionUnlockUninstall,
            new StageFashionUnlockUninstallRequest(paths, Session: null),
            "request-sv-fashion-uninstall-stage");
        AssertSuccess(uninstallStage);
        Assert.Single(uninstallStage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.fashionUnlock", uninstallStage.Payload.Session.PendingEdits[0].Domain);

        var uninstallPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, uninstallStage.Payload.Session),
            "request-sv-fashion-uninstall-plan");
        AssertSuccess(uninstallPlan);
        Assert.True(uninstallPlan.Payload!.ChangePlan.CanApply);

        var uninstallApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, uninstallStage.Payload.Session, uninstallPlan.Payload.ChangePlan),
            "request-sv-fashion-uninstall-apply");
        AssertSuccess(uninstallApply);
        Assert.DoesNotContain(uninstallApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.False(File.Exists(outputMainPath));
    }

    [Theory]
    [MemberData(nameof(ScarletVioletBuilds))]
    public void ScarletVioletFashionUnlockUninstallPreservesOtherExeFsEdits(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseExeFsFile("main", SvFashionUnlockBridgeFixtures.CreateCompatibleMain(game));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var staleFashionStage = Dispatch<StageFashionUnlockInstallResponse>(
            dispatcher,
            KmCommandNames.StageFashionUnlockInstall,
            new StageFashionUnlockInstallRequest(paths, Session: null),
            "request-sv-fashion-stale-source-stage");
        AssertSuccess(staleFashionStage);
        AssertMainSource(Assert.Single(staleFashionStage.Payload!.Session.PendingEdits).Sources, FileLayerDto.Base);
        var staleFashionPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, staleFashionStage.Payload.Session),
            "request-sv-fashion-stale-source-plan");
        AssertSuccess(staleFashionPlan);
        AssertMainSource(Assert.Single(staleFashionPlan.Payload!.ChangePlan.Writes).Sources, FileLayerDto.Base);

        var hyperspaceStage = Dispatch<StageHyperspaceBypassInstallResponse>(
            dispatcher,
            KmCommandNames.StageHyperspaceBypassInstall,
            new StageHyperspaceBypassInstallRequest(paths, Session: null),
            "request-sv-hyperspace-before-fashion-stage");
        AssertSuccess(hyperspaceStage);
        AssertMainSource(Assert.Single(hyperspaceStage.Payload!.Session.PendingEdits).Sources, FileLayerDto.Base);
        var hyperspacePlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, hyperspaceStage.Payload!.Session),
            "request-sv-hyperspace-before-fashion-plan");
        AssertSuccess(hyperspacePlan);
        AssertMainSource(Assert.Single(hyperspacePlan.Payload!.ChangePlan.Writes).Sources, FileLayerDto.Base);
        var hyperspaceApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, hyperspaceStage.Payload.Session, hyperspacePlan.Payload!.ChangePlan),
            "request-sv-hyperspace-before-fashion-apply");
        AssertSuccess(hyperspaceApply);
        Assert.DoesNotContain(hyperspaceApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var refreshedHyperspacePlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, hyperspaceStage.Payload.Session),
            "request-sv-hyperspace-refreshed-source-plan");
        AssertSuccess(refreshedHyperspacePlan);
        AssertMainSource(Assert.Single(refreshedHyperspacePlan.Payload!.ChangePlan.Writes).Sources, FileLayerDto.Layered);
        var staleHyperspaceApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, hyperspaceStage.Payload.Session, hyperspacePlan.Payload.ChangePlan),
            "request-sv-hyperspace-stale-source-apply");
        AssertSuccess(staleHyperspaceApply);
        Assert.Empty(staleHyperspaceApply.Payload!.ApplyResult.WrittenFiles);
        Assert.Contains(
            staleHyperspaceApply.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));

        var refreshedFashionPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, staleFashionStage.Payload.Session),
            "request-sv-fashion-refreshed-source-plan");
        AssertSuccess(refreshedFashionPlan);
        AssertMainSource(Assert.Single(refreshedFashionPlan.Payload!.ChangePlan.Writes).Sources, FileLayerDto.Layered);
        var staleFashionApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, staleFashionStage.Payload.Session, staleFashionPlan.Payload.ChangePlan),
            "request-sv-fashion-stale-source-apply");
        AssertSuccess(staleFashionApply);
        Assert.Empty(staleFashionApply.Payload!.ApplyResult.WrittenFiles);
        Assert.Contains(
            staleFashionApply.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));

        var fashionStage = Dispatch<StageFashionUnlockInstallResponse>(
            dispatcher,
            KmCommandNames.StageFashionUnlockInstall,
            new StageFashionUnlockInstallRequest(paths, Session: null),
            "request-sv-fashion-after-hyperspace-stage");
        AssertSuccess(fashionStage);
        AssertMainSource(Assert.Single(fashionStage.Payload!.Session.PendingEdits).Sources, FileLayerDto.Layered);
        var fashionPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, fashionStage.Payload!.Session),
            "request-sv-fashion-after-hyperspace-plan");
        AssertSuccess(fashionPlan);
        AssertMainSource(Assert.Single(fashionPlan.Payload!.ChangePlan.Writes).Sources, FileLayerDto.Layered);
        var fashionApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, fashionStage.Payload.Session, fashionPlan.Payload!.ChangePlan),
            "request-sv-fashion-after-hyperspace-apply");
        AssertSuccess(fashionApply);
        Assert.DoesNotContain(fashionApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var installedOutput = File.ReadAllBytes(outputMainPath);
        Assert.Equal(SvHyperspaceBypassBridgeFixtures.BypassBranch, SvHyperspaceBypassBridgeFixtures.ReadPatchInstruction(installedOutput));
        Assert.Equal(
            (SvFashionUnlockBridgeFixtures.ReturnTrueFirst, SvFashionUnlockBridgeFixtures.ReturnTrueSecond),
            SvFashionUnlockBridgeFixtures.ReadPatchInstructions(installedOutput));

        var hyperspaceRefreshStage = Dispatch<StageHyperspaceBypassInstallResponse>(
            dispatcher,
            KmCommandNames.StageHyperspaceBypassInstall,
            new StageHyperspaceBypassInstallRequest(paths, Session: null),
            "request-sv-hyperspace-refresh-source-stage");
        AssertSuccess(hyperspaceRefreshStage);
        AssertMainSource(Assert.Single(hyperspaceRefreshStage.Payload!.Session.PendingEdits).Sources, FileLayerDto.Layered);
        var hyperspaceRefreshPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, hyperspaceRefreshStage.Payload.Session),
            "request-sv-hyperspace-refresh-source-plan");
        AssertSuccess(hyperspaceRefreshPlan);
        AssertMainSource(Assert.Single(hyperspaceRefreshPlan.Payload!.ChangePlan.Writes).Sources, FileLayerDto.Layered);

        var uninstallStage = Dispatch<StageFashionUnlockUninstallResponse>(
            dispatcher,
            KmCommandNames.StageFashionUnlockUninstall,
            new StageFashionUnlockUninstallRequest(paths, Session: null),
            "request-sv-fashion-preserve-uninstall-stage");
        AssertSuccess(uninstallStage);
        var uninstallPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, uninstallStage.Payload!.Session),
            "request-sv-fashion-preserve-uninstall-plan");
        AssertSuccess(uninstallPlan);
        var uninstallApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, uninstallStage.Payload.Session, uninstallPlan.Payload!.ChangePlan),
            "request-sv-fashion-preserve-uninstall-apply");
        AssertSuccess(uninstallApply);
        Assert.DoesNotContain(uninstallApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var preservedOutput = File.ReadAllBytes(outputMainPath);
        Assert.Equal(SvHyperspaceBypassBridgeFixtures.BypassBranch, SvHyperspaceBypassBridgeFixtures.ReadPatchInstruction(preservedOutput));
        Assert.Equal(
            (SvFashionUnlockBridgeFixtures.VanillaOwnershipCheckEntryFirst, SvFashionUnlockBridgeFixtures.VanillaOwnershipCheckEntrySecond),
            SvFashionUnlockBridgeFixtures.ReadPatchInstructions(preservedOutput));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletNormalEditorsCanShareOnePendingEditSession(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var session = UpdatePokemonField(dispatcher, paths, personalId: 1, field: "hp", value: "47");
        session = UpdateItem(dispatcher, paths, session, itemId: 1, field: "buyPrice", value: "999");

        Assert.Collection(
            session.PendingEdits,
            edit => Assert.Equal("workflow.pokemon", edit.Domain),
            edit => Assert.Equal("workflow.items", edit.Domain));

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session),
            "request-sv-normal-mixed-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == $"romfs/{SvDataPaths.PersonalArray}");
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == $"romfs/{SvDataPaths.ItemDataArray}");
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == "romfs/arc/data.trpfd");

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan),
            "request-sv-normal-mixed-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains($"romfs/{SvDataPaths.PersonalArray}", apply.Payload.ApplyResult.WrittenFiles);
        Assert.Contains($"romfs/{SvDataPaths.ItemDataArray}", apply.Payload.ApplyResult.WrittenFiles);
        Assert.Contains("romfs/arc/data.trpfd", apply.Payload.ApplyResult.WrittenFiles);

        Assert.Equal(47, ReadPersonal(temp, personalId: 1).BaseStats!.Value.Hp);
        Assert.Equal(999, ReadItemPrice(temp, itemId: 1));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletBatchFieldCommandsSharePendingEditSession(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var itemBatch = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(1, "buyPrice", "777"),
                    new ItemFieldUpdateDto(1, "healAmount", "25"),
                ]),
            "request-sv-item-fields-update");
        AssertSuccess(itemBatch);
        var session = itemBatch.Payload!.Session;
        var item = itemBatch.Payload.Workflow.Items.Single(entry => entry.ItemId == 1);
        Assert.Equal(777, item.BuyPrice);
        Assert.Equal(25, item.Metadata.HealAmount);

        var pokemonBatch = Dispatch<UpdatePokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonFields,
            new UpdatePokemonFieldsRequest(
                paths,
                session,
                [
                    new PokemonFieldUpdateDto(1, "hp", "48"),
                    new PokemonFieldUpdateDto(1, "attack", "49"),
                ]),
            "request-sv-pokemon-fields-update");
        AssertSuccess(pokemonBatch);
        session = pokemonBatch.Payload!.Session;
        var pokemon = pokemonBatch.Payload.Workflow.Pokemon.Single(entry => entry.PersonalId == 1);
        Assert.Equal(48, pokemon.BaseStats.HP);
        Assert.Equal(49, pokemon.BaseStats.Attack);

        var moveBatch = Dispatch<UpdateMoveFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateMoveFields,
            new UpdateMoveFieldsRequest(
                paths,
                session,
                [
                    new MoveFieldUpdateDto(33, "power", "50"),
                    new MoveFieldUpdateDto(33, "accuracy", "90"),
                ]),
            "request-sv-move-fields-update");
        AssertSuccess(moveBatch);
        session = moveBatch.Payload!.Session;
        var move = moveBatch.Payload.Workflow.Moves.Single(entry => entry.MoveId == 33);
        Assert.Equal(50, move.Power);
        Assert.Equal(90, move.Accuracy);

        var trainerBatch = Dispatch<UpdateTrainerFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerFields,
            new UpdateTrainerFieldsRequest(
                paths,
                session,
                [
                    new TrainerFieldUpdateDto(0, 0, "level", "12"),
                    new TrainerFieldUpdateDto(0, 0, "teraType", ((int)global::GemType.FAIRY).ToString(CultureInfo.InvariantCulture)),
                ]),
            "request-sv-trainer-fields-update");
        AssertSuccess(trainerBatch);
        session = trainerBatch.Payload!.Session;
        var trainerPokemon = trainerBatch.Payload.Workflow.Trainers.Single(entry => entry.TrainerId == 0).Team.Single(entry => entry.Slot == 0);
        Assert.Equal(12, trainerPokemon.Level);
        Assert.Equal((int)global::GemType.FAIRY, trainerPokemon.TeraType);

        var encounters = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-sv-encounter-fields-load");
        AssertSuccess(encounters);
        var encounterTableId = Assert.Single(encounters.Payload!.Workflow.Tables).TableId;
        var encounterBatch = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                session,
                [
                    new EncounterSlotFieldUpdateDto(encounterTableId, 0, "levelMin", "9"),
                    new EncounterSlotFieldUpdateDto(encounterTableId, 0, "levelMax", "20"),
                ]),
            "request-sv-encounter-slot-fields-update");
        AssertSuccess(encounterBatch);
        session = encounterBatch.Payload!.Session;
        var encounterSlot = Assert.Single(encounterBatch.Payload.Workflow.Tables).Slots.Single(entry => entry.Slot == 0);
        Assert.Equal(9, encounterSlot.LevelMin);
        Assert.Equal(20, encounterSlot.LevelMax);

        var giftBatch = Dispatch<UpdateGiftPokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateGiftPokemonFields,
            new UpdateGiftPokemonFieldsRequest(
                paths,
                session,
                [
                    new GiftPokemonFieldUpdateDto(0, "species", "4"),
                    new GiftPokemonFieldUpdateDto(0, "shinyLock", "0"),
                ]),
            "request-sv-gift-pokemon-fields-update");
        AssertSuccess(giftBatch);
        session = giftBatch.Payload!.Session;
        var gift = Assert.Single(giftBatch.Payload.Workflow.Gifts);
        Assert.Equal(4, gift.SpeciesId);
        Assert.Equal(0, gift.ShinyLock);

        var tradeBatch = Dispatch<UpdateTradePokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonFields,
            new UpdateTradePokemonFieldsRequest(
                paths,
                session,
                [
                    new TradePokemonFieldUpdateDto(0, "species", "4"),
                    new TradePokemonFieldUpdateDto(0, "shinyLock", "0"),
                ]),
            "request-sv-trade-pokemon-fields-update");
        AssertSuccess(tradeBatch);
        session = tradeBatch.Payload!.Session;
        var trade = Assert.Single(tradeBatch.Payload.Workflow.Trades);
        Assert.Equal(4, trade.SpeciesId);
        Assert.Equal(0, trade.ShinyLock);

        var staticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-sv-static-encounter-fields-load");
        AssertSuccess(staticEncounters);
        var fixedSymbol = staticEncounters.Payload!.Workflow.Encounters.Single(entry => entry.CategoryId == "fixedSymbols");
        var staticSpecies = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(
                paths,
                session,
                fixedSymbol.EncounterIndex,
                "species",
                "4"),
            "request-sv-static-encounter-species-update");
        AssertSuccess(staticSpecies);
        session = staticSpecies.Payload!.Session;
        var staticLevel = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(
                paths,
                session,
                fixedSymbol.EncounterIndex,
                "level",
                "20"),
            "request-sv-static-encounter-level-update");
        AssertSuccess(staticLevel);
        session = staticLevel.Payload!.Session;
        var stagedFixedSymbol = staticLevel.Payload.Workflow.Encounters.Single(entry => entry.EncounterIndex == fixedSymbol.EncounterIndex);
        Assert.Equal(4, stagedFixedSymbol.SpeciesId);
        Assert.Equal(20, stagedFixedSymbol.Level);

        var placement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-fields-load");
        AssertSuccess(placement);
        var hiddenItem = placement.Payload!.Workflow.Objects.First(entry => entry.CategoryId == "hiddenItems");
        var placementBatch = Dispatch<UpdatePlacementObjectFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdatePlacementObjectFields,
            new UpdatePlacementObjectFieldsRequest(
                paths,
                session,
                [
                    new PlacementObjectFieldUpdateDto(hiddenItem.ObjectId, "hidden.item1.itemId", "4"),
                    new PlacementObjectFieldUpdateDto(hiddenItem.ObjectId, "hidden.item1.chance", "80"),
                ]),
            "request-sv-placement-object-fields-update");
        AssertSuccess(placementBatch);
        session = placementBatch.Payload!.Session;
        Assert.NotNull(placementBatch.Payload.Workflow);
        var stagedHiddenItem = placementBatch.Payload.Workflow.Objects.Single(entry => entry.ObjectId == hiddenItem.ObjectId);
        Assert.Equal("4", stagedHiddenItem.Fields!.Single(field => field.Field == "hidden.item1.itemId").Value);
        Assert.Equal("80", stagedHiddenItem.Fields!.Single(field => field.Field == "hidden.item1.chance").Value);

        var expectedDomains = new[]
        {
            "workflow.items",
            "workflow.pokemon",
            "workflow.moves",
            "workflow.trainers",
            "workflow.encounters",
            "workflow.giftPokemon",
            "workflow.tradePokemon",
            "workflow.staticEncounters",
            "workflow.placement",
        };
        foreach (var domain in expectedDomains)
        {
            Assert.Contains(session.PendingEdits, edit => edit.Domain == domain);
        }
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTrainerEditorAllowsClearingAndFillingExposedPartySlots(
        ProjectGameDto game,
        ulong titleId)
    {
        using (var temp = CreateScarletVioletProject(titleId))
        {
            WriteScarletFixtures(temp);
            var dispatcher = CreateDispatcherWithSvCache(temp);
            var paths = temp.Paths with { SelectedGame = game };

            var clear = Dispatch<UpdateTrainerFieldResponse>(
                dispatcher,
                KmCommandNames.UpdateTrainerField,
                new UpdateTrainerFieldRequest(paths, Session: null, TrainerId: 0, Slot: 0, Field: "speciesId", Value: "0"),
                "request-sv-trainer-clear-slot");
            AssertSuccess(clear);
            var emptySlot = clear.Payload!.Workflow.Trainers.Single().Team.Single(pokemon => pokemon.Slot == 0);
            Assert.Equal(0, emptySlot.SpeciesId);
            Assert.Equal("None", emptySlot.Species);
            Assert.Equal([0, 0, 0, 0], emptySlot.MoveIds);
            Assert.Equal(0, emptySlot.HeldItemId);
            Assert.Equal(0, emptySlot.Evs.HP + emptySlot.Evs.Attack + emptySlot.Evs.Defense + emptySlot.Evs.SpecialAttack + emptySlot.Evs.SpecialDefense + emptySlot.Evs.Speed);
            Assert.Equal(0, emptySlot.Ivs.HP + emptySlot.Ivs.Attack + emptySlot.Ivs.Defense + emptySlot.Ivs.SpecialAttack + emptySlot.Ivs.SpecialDefense + emptySlot.Ivs.Speed);

            var clearSession = clear.Payload.Session;
            Apply(dispatcher, paths, clearSession);

            Assert.Null(ReadOptionalTrainerPokemon(temp, trainerId: 0, slot: 0));
        }

        using (var temp = CreateScarletVioletProject(titleId))
        {
            WriteScarletFixtures(temp);
            var dispatcher = CreateDispatcherWithSvCache(temp);
            var paths = temp.Paths with { SelectedGame = game };

            var fillSession = UpdateTrainer(dispatcher, paths, trainerId: 0, slot: 1, field: "speciesId", value: "2");
            Apply(dispatcher, paths, fillSession);

            var pokemon = ReadOptionalTrainerPokemon(temp, trainerId: 0, slot: 1);
            Assert.NotNull(pokemon);
            Assert.Equal((global::pml.common.DevID)2, pokemon.Value.DevId);
        }
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTrainerEditorRejectsPartySlotGaps(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var response = Dispatch<UpdateTrainerFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerField,
            new UpdateTrainerFieldRequest(paths, Session: null, TrainerId: 0, Slot: 2, Field: "speciesId", Value: "2"),
            "request-sv-trainer-gap");

        AssertSuccess(response);
        Assert.False(response.Payload!.Session.HasPendingChanges);
        Assert.Contains(
            response.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("filled in order", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTrainerEditorRejectsEditingEmptyPartySlotDetails(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var response = Dispatch<UpdateTrainerFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerField,
            new UpdateTrainerFieldRequest(paths, Session: null, TrainerId: 0, Slot: 1, Field: "move1Id", Value: "33"),
            "request-sv-trainer-empty-slot-detail");

        AssertSuccess(response);
        Assert.False(response.Payload!.Session.HasPendingChanges);
        Assert.Contains(
            response.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("slot is empty", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletProjectExposesBasicEditorWorkflows(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };

        var response = Dispatch<ListWorkflowsResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.ListWorkflows,
            new ListWorkflowsRequest(paths),
            "request-sv-workflows");

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.Equal(
            ["items", "moves", "text", "pokemon", "trainers", "encounters", "teraRaids", "staticEncounters", "shops", "giftPokemon", "tradePokemon", "placement", "typeChart", "fashionUnlock", "hyperspaceBypass", "spreadsheetImport", "modMerger"],
            response.Payload.Workflows.Select(workflow => workflow.Id).ToArray());
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Pokemon Data");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Items");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Moves");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Text and Dialogue Map");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Trainers");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Wild Encounters");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Tera Raids");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Static Encounters");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Shops");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Gift Pokemon");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Trade Pokemon");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Placement");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Type Chart");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Fashion Unlock");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Hyperspace Bypass");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Dump Importer");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "S/V Mod Merger");
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletProjectLoadsAndPreviewsDumpImporter(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadSpreadsheetImportWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadSpreadsheetImportWorkflow,
            new LoadSpreadsheetImportWorkflowRequest(paths),
            "request-sv-dump-import-load");

        AssertSuccess(loaded);
        var profile = Assert.Single(loaded.Payload!.Workflow.Profiles);
        Assert.Equal("items-price-csv", profile.ProfileId);
        Assert.Equal("items", profile.TargetWorkflow);
        Assert.Equal($"romfs/{SvDataPaths.ItemDataArray}", profile.Provenance.SourceFile);
        Assert.Equal(4, profile.Columns.Count);

        var sourcePath = Path.Combine(temp.RootPath, "sv-items.tsv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId	SellPrice	WattsPrice
            1	90	7
            """);

        var preview = Dispatch<PreviewSpreadsheetImportResponse>(
            dispatcher,
            KmCommandNames.PreviewSpreadsheetImport,
            new PreviewSpreadsheetImportRequest(paths, "items-price-csv", sourcePath, Session: null),
            "request-sv-dump-import-preview");

        AssertSuccess(preview);
        Assert.Equal(1, preview.Payload!.Preview.AcceptedRowCount);
        Assert.Equal(0, preview.Payload.Preview.RejectedRowCount);
        Assert.Contains(
            preview.Payload.Session.PendingEdits,
            edit => edit.Domain == "workflow.items"
                && edit.Field == "buyPrice"
                && edit.NewValue == "180");
        Assert.Contains(
            preview.Payload.Session.PendingEdits,
            edit => edit.Domain == "workflow.items"
                && edit.Field == "wattsPrice"
                && edit.NewValue == "7");
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletDumpImporterRejectsConflictingStoredPriceColumns(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        var sourcePath = Path.Combine(temp.RootPath, "sv-conflicting-items.csv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId,BuyPrice,SellPrice
            1,300,80
            """);

        var preview = Dispatch<PreviewSpreadsheetImportResponse>(
            dispatcher,
            KmCommandNames.PreviewSpreadsheetImport,
            new PreviewSpreadsheetImportRequest(paths, "items-price-csv", sourcePath, Session: null),
            "request-sv-dump-import-conflict");

        AssertSuccess(preview);
        Assert.Equal(0, preview.Payload!.Preview.AcceptedRowCount);
        Assert.Equal(1, preview.Payload.Preview.RejectedRowCount);
        Assert.Empty(preview.Payload.Session.PendingEdits);
        Assert.Contains(
            preview.Payload.Preview.Rows.Single().Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("BuyPrice and SellPrice", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletProjectLoadsAndStagesTeraRaidsWorkflow(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadTeraRaidsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTeraRaidsWorkflow,
            new LoadTeraRaidsWorkflowRequest(paths),
            "request-sv-tera-raids-load");

        AssertSuccess(loaded);
        Assert.DoesNotContain(
            loaded.Payload!.Workflow.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var raid = Assert.Single(loaded.Payload.Workflow.Raids);
        Assert.Equal("Paldea", raid.Region);
        Assert.Equal(5, raid.StarRank);
        Assert.Equal("5 Star", raid.StarLabel);
        Assert.Equal(25, raid.RaidNo);
        Assert.Equal(1, raid.SpeciesId);
        Assert.Equal("Bulbasaur", raid.Species);
        Assert.Equal(30, raid.Level);
        Assert.Equal((int)global::GemType.KUSA, raid.TeraType);
        Assert.Equal("Grass", raid.TeraTypeLabel);
        Assert.Equal("0x1111222233334444", raid.FixedRewardTableHash);
        Assert.Equal("0x5555666677778888", raid.LotteryRewardTableHash);
        var fixedReward = Assert.Single(Assert.Single(loaded.Payload.Workflow.FixedRewardTables).Rewards);
        Assert.Equal(1, fixedReward.ItemId);
        Assert.Equal(2, fixedReward.Count);
        var lotteryReward = Assert.Single(Assert.Single(loaded.Payload.Workflow.LotteryRewardTables).Rewards);
        Assert.Equal(2, lotteryReward.ItemId);
        Assert.Equal(25, lotteryReward.Rate);

        var updatedRaid = Dispatch<UpdateTeraRaidFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTeraRaidField,
            new UpdateTeraRaidFieldRequest(paths, Session: null, raid.RecordId, "species", "4"),
            "request-sv-tera-raid-species-update");
        AssertSuccess(updatedRaid);
        var session = updatedRaid.Payload!.Session;
        Assert.Contains(session.PendingEdits, edit =>
            edit.Domain == "workflow.teraRaids" && edit.RecordId == raid.RecordId && edit.Field == "species" && edit.NewValue == "4");
        Assert.Equal(4, Assert.Single(updatedRaid.Payload.Workflow.Raids).SpeciesId);

        var updatedReward = Dispatch<UpdateTeraRaidFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTeraRaidField,
            new UpdateTeraRaidFieldRequest(paths, session, fixedReward.RecordId, "fixedCount", "3"),
            "request-sv-tera-raid-reward-update");
        AssertSuccess(updatedReward);
        session = updatedReward.Payload!.Session;
        Assert.Contains(session.PendingEdits, edit =>
            edit.Domain == "workflow.teraRaids" && edit.RecordId == fixedReward.RecordId && edit.Field == "fixedCount" && edit.NewValue == "3");
        Assert.Equal(3, Assert.Single(Assert.Single(updatedReward.Payload.Workflow.FixedRewardTables).Rewards).Count);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session),
            "request-sv-tera-raids-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write =>
            write.TargetRelativePath == $"romfs/{SvDataPaths.TeraRaidEnemyPaldea5}");
        Assert.Contains(plan.Payload.ChangePlan.Writes, write =>
            write.TargetRelativePath == $"romfs/{SvDataPaths.TeraRaidFixedRewardItemArray}");

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan),
            "request-sv-tera-raids-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal((global::pml.common.DevID)4, ReadTeraRaidBossSpecies(temp));
        Assert.Equal(3, ReadTeraRaidFixedRewardCount(temp));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPokemonCompatibilityRemovesTheIntendedMoves(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(
                eggMoves: [33, 45, 349],
                reminderMoves: [349, 45, 33]));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var session = UpdatePokemonField(
            dispatcher,
            paths,
            personalId: 1,
            field: "compatibility:egg:0",
            value: "0");
        session = UpdatePokemonField(dispatcher, paths, session, personalId: 1, field: "hp", value: "46");
        session = UpdatePokemonField(
            dispatcher,
            paths,
            session,
            personalId: 1,
            field: "compatibility:reminder:0",
            value: "0");
        session = UpdatePokemonField(
            dispatcher,
            paths,
            session,
            personalId: 1,
            field: "compatibility:egg:1",
            value: "0");
        session = UpdatePokemonField(
            dispatcher,
            paths,
            session,
            personalId: 1,
            field: "compatibility:reminder:1",
            value: "0");
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session),
            "request-sv-compatibility-removal-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan),
            "request-sv-compatibility-removal-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadPersonal(temp, personalId: 1);
        var eggMoves = Enumerable.Range(0, written.EggMovesLength).Select(written.EggMoves).ToArray();
        var reminderMoves = Enumerable.Range(0, written.ReminderMovesLength).Select(written.ReminderMoves).ToArray();
        Assert.Equal(new ushort[] { 349 }, eggMoves);
        Assert.Equal(new ushort[] { 33 }, reminderMoves);
        Assert.Equal(46, written.BaseStats!.Value.Hp);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletProjectLoadsEnglishMessageLabels(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-pokemon-labels");
        AssertSuccess(pokemon);
        Assert.DoesNotContain(pokemon.Payload!.Workflow.Pokemon, row => row.PersonalId == 0);
        var bulbasaur = pokemon.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1);
        Assert.Equal("Bulbasaur", bulbasaur.Name);
        Assert.Equal("Overgrow", bulbasaur.Abilities.Ability1Label);
        Assert.Equal(16, bulbasaur.BaseExperience);
        Assert.Equal("Tackle", Assert.Single(bulbasaur.Learnset).MoveName);
        var tmGroup = bulbasaur.Compatibility.Single(group => group.GroupId == "tm");
        Assert.Equal(1, tmGroup.EnabledCount);
        Assert.Equal(3, tmGroup.Entries.Count);
        var tm001Entry = tmGroup.Entries.Single(entry => entry.MoveId == 36);
        var tm002Entry = tmGroup.Entries.Single(entry => entry.MoveId == 45);
        var tm100Entry = tmGroup.Entries.Single(entry => entry.MoveId == 349);
        Assert.Equal("TM001 Take Down", tm001Entry.Label);
        Assert.True(tm001Entry.CanLearn);
        Assert.Equal("TM002 Growl", tm002Entry.Label);
        Assert.False(tm002Entry.CanLearn);
        Assert.Equal("TM100 Dragon Dance", tm100Entry.Label);
        Assert.False(tm100Entry.CanLearn);
        Assert.DoesNotContain(tmGroup.Entries, entry => entry.Label.Contains("Tackle", StringComparison.Ordinal));

        var pokemonFields = pokemon.Payload.Workflow.EditableFields;
        Assert.Contains(
            pokemonFields.Single(field => field.Field == "type1").Options,
            option => option.Value == 11 && option.Label == "Grass");
        Assert.Contains(
            pokemonFields.Single(field => field.Field == "ability1").Options,
            option => option.Value == 65 && option.Label.Contains("Overgrow", StringComparison.Ordinal));
        Assert.Contains(
            pokemonFields.Single(field => field.Field == "genderRatio").Options,
            option => option.Value == 31 && option.Label.Contains("87.5% male", StringComparison.Ordinal));
        Assert.Contains(
            pokemonFields.Single(field => field.Field == "expGrowth").Options,
            option => option.Value == 3 && option.Label == "Medium Slow");
        Assert.Contains(
            pokemonFields.Single(field => field.Field == "evolutionStage").Options,
            option => option.Value == 3 && option.Label.Contains("Final", StringComparison.Ordinal));
        Assert.Contains(
            pokemonFields.Single(field => field.Field == "eggGroup1").Options,
            option => option.Value == 7 && option.Label == "Grass");
        Assert.Contains(
            pokemonFields.Single(field => field.Field == "color").Options,
            option => option.Value == 5 && option.Label == "Brown");
        var levelUpEvolution = pokemon.Payload.Workflow.EvolutionMethodOptions.Single(option => option.Value == 4);
        Assert.Equal("level", levelUpEvolution.ArgumentKind);
        Assert.Contains("Level Up", levelUpEvolution.Label, StringComparison.Ordinal);
        Assert.Contains(
            pokemon.Payload.Workflow.EvolutionMethodOptions,
            option => option.Value == 50 && option.Label.Contains("Walk 1000 Steps", StringComparison.Ordinal));
        Assert.Contains(
            pokemon.Payload.Workflow.EvolutionMethodOptions.Single(option => option.Value == 50).ArgumentOptions,
            option => option.Value == 1000 && option.Label.Contains("1000 steps", StringComparison.Ordinal));
        Assert.Contains(
            pokemon.Payload.Workflow.EvolutionMethodOptions.Single(option => option.Value == 54).ArgumentOptions,
            option => option.Value == 999 && option.Label.Contains("999 coins", StringComparison.Ordinal));
        Assert.Contains(
            pokemon.Payload.Workflow.EvolutionMethodOptions.Single(option => option.Value == 61).ArgumentOptions,
            option => option.Value == 1 && option.Label.Contains("Hisuian Sliggoo", StringComparison.Ordinal));

        var moves = Dispatch<LoadMovesWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadMovesWorkflow,
            new LoadMovesWorkflowRequest(paths),
            "request-sv-move-labels");
        AssertSuccess(moves);
        var tackle = moves.Payload!.Workflow.Moves.Single(row => row.MoveId == 33);
        Assert.Equal("Tackle", tackle.Name);
        Assert.True(tackle.CanUseMove);
        Assert.Equal("Normal", tackle.TypeName);
        Assert.Equal("Physical", tackle.CategoryName);
        Assert.Equal("Opponent", tackle.TargetName);
        Assert.Equal(40, tackle.Power);
        Assert.Equal(35, tackle.PP);
        Assert.Equal("None", tackle.InflictName);
        Assert.Contains(tackle.Flags, flag => flag.Field == "makesContact" && flag.Enabled);
        Assert.Contains(
            moves.Payload.Workflow.EditableFields.Single(field => field.Field == "type").Options,
            option => option.Value == 0 && option.Label == "0 Normal");
        Assert.Contains(
            moves.Payload.Workflow.EditableFields.Single(field => field.Field == "category").Options,
            option => option.Value == 1 && option.Label == "1 Physical");
        Assert.Contains(
            moves.Payload.Workflow.EditableFields.Single(field => field.Field == "stat2").Options,
            option => option.Value == -1 && option.Label == "-1 Unused");
        var growl = moves.Payload.Workflow.Moves.Single(row => row.MoveId == 45);
        Assert.Contains(
            growl.StatChanges,
            change => change.Slot == 1 && change.Stat == 2 && change.StatName == "Defense" && change.Stage == -1);
        Assert.Contains(
            growl.StatChanges,
            change => change.Slot == 2 && change.Stat == -1 && change.StatName == "Unused (-1 raw)");

        var items = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-sv-item-labels");
        AssertSuccess(items);
        var masterBall = items.Payload!.Workflow.Items.Single(item => item.ItemId == 1);
        var tm001 = items.Payload.Workflow.Items.Single(item => item.ItemId == 2);
        var legacyMoveItem = items.Payload.Workflow.Items.Single(item => item.ItemId == 3);
        var tm002 = items.Payload.Workflow.Items.Single(item => item.ItemId == 4);
        var tm100 = items.Payload.Workflow.Items.Single(item => item.ItemId == 5);
        Assert.Equal("Master Ball", masterBall.Name);
        Assert.False(masterBall.Metadata.CanUseOnPokemon);
        Assert.Equal("Poke Balls", masterBall.Category);
        Assert.True(tm001.Metadata.CanUseOnPokemon);
        Assert.Equal("TMs", tm001.Category);
        Assert.Equal(1, tm001.Metadata.MachineSlot);
        Assert.Equal(36, tm001.Metadata.MachineMoveId);
        Assert.Equal("Take Down", tm001.Metadata.MachineMoveName);
        Assert.Null(legacyMoveItem.Metadata.MachineSlot);
        Assert.Equal(2, tm002.Metadata.MachineSlot);
        Assert.Equal(45, tm002.Metadata.MachineMoveId);
        Assert.Equal(100, tm100.Metadata.MachineSlot);
        Assert.Equal(349, tm100.Metadata.MachineMoveId);
        Assert.Equal("Dragon Dance", tm100.Metadata.MachineMoveName);
        var itemFields = items.Payload.Workflow.EditableFields;
        Assert.Contains(
            itemFields.Single(field => field.Field == "pouch").Options,
            option => option.Value == (int)global::FieldPocket.FPOCKET_BALL && option.Label == "Poke Balls");
        Assert.Contains(
            itemFields.Single(field => field.Field == "fieldUseType").Options,
            option => option.Value == (int)global::FieldFunctionType.FIELDFUNC_WAZA && option.Label == "Teach Move");
        Assert.Contains(
            itemFields.Single(field => field.Field == "itemType").Options,
            option => option.Value == (int)global::ItemType.ITEMTYPE_BALL && option.Label == "Poke Ball");
        Assert.Contains(
            itemFields.Single(field => field.Field == "groupType").Options,
            option => option.Value == (int)global::ItemGroup.ITEMGROUP_BALL && option.Label == "Poke Ball");
        Assert.Contains(
            itemFields.Single(field => field.Field == "machineMoveId").Options,
            option => option.Value == 36 && option.Label.Contains("Take Down", StringComparison.Ordinal));
        Assert.Contains(
            itemFields.Single(field => field.Field == "groupIndex").Options,
            option => option.Value == 1 && option.Label.Contains("TM001 Take Down", StringComparison.Ordinal));
        Assert.Contains(
            itemFields.Single(field => field.Field == "groupIndex").Options,
            option => option.Value == 2 && option.Label.Contains("TM002 Growl", StringComparison.Ordinal));

        var shops = Dispatch<LoadShopsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadShopsWorkflow,
            new LoadShopsWorkflowRequest(paths),
            "request-sv-shop-labels");
        AssertSuccess(shops);
        Assert.Equal("sv", shops.Payload!.Workflow.EditorFamily);
        Assert.Equal(2, shops.Payload.Workflow.Stats.TotalShopCount);
        Assert.Contains("Scarlet/Violet", shops.Payload.Workflow.Summary.Description, StringComparison.Ordinal);
        var pokeMart = shops.Payload.Workflow.Shops.Single(shop => shop.ShopId == "lineup:shop_00_lineup");
        Assert.Equal("Poke Mart", pokeMart.Name);
        Assert.True(pokeMart.CanEditInventoryOrder);
        Assert.Equal("buyPrice", pokeMart.GlobalPriceField);
        Assert.Equal("Master Ball", pokeMart.Inventory[0].ItemName);
        Assert.Contains("sortOrder", pokeMart.Inventory[0].SupportedFields);
        Assert.Null(pokeMart.Inventory[0].PriceField);
        var tmMachine = shops.Payload.Workflow.Shops.Single(shop => shop.ShopId == "tm:1");
        Assert.Equal("TM Machine [Paldea]", tmMachine.Name);
        Assert.False(tmMachine.CanEditInventoryOrder);
        Assert.Equal("LP", tmMachine.Currency);
        Assert.Null(tmMachine.GlobalPriceField);
        var tmEntry = Assert.Single(tmMachine.Inventory);
        Assert.Equal("TM001", tmEntry.ItemName);
        Assert.Equal("Take Down", tmEntry.FieldDisplayValues["moveId"]);
        Assert.Equal("lpCost", tmEntry.PriceField);
        Assert.Contains(
            shops.Payload.Workflow.EditableFields.Single(field => field.Field == "material1ItemId").Options,
            option => option.Value == 1 && option.Label.Contains("Master Ball", StringComparison.Ordinal));

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(paths),
            "request-sv-trainer-labels");
        AssertSuccess(trainers);
        var trainer = Assert.Single(trainers.Payload!.Workflow.Trainers);
        Assert.Equal("Test Trainer", trainer.Name);
        Assert.Equal("Pokemon Trainer", trainer.TrainerClass);
        Assert.Equal(6, trainer.Team.Count);
        var trainerPokemon = Assert.Single(trainer.Team, entry => entry.SpeciesId > 0);
        Assert.Equal("Bulbasaur", trainerPokemon.Species);
        Assert.Equal(new[] { "Tackle", "None", "None", "None" }, trainerPokemon.Moves);
        Assert.Equal("Random", trainerPokemon.GenderLabel);
        Assert.Equal("Random 1/2", trainerPokemon.AbilityLabel);
        Assert.Equal("Default (game behavior)", trainerPokemon.NatureLabel);
        Assert.NotNull(trainerPokemon.BaseStats);
        Assert.Equal(45, trainerPokemon.BaseStats!.HP);
        Assert.Equal(49, trainerPokemon.BaseStats.Attack);
        Assert.Contains(
            trainerPokemon.AbilityOptions,
            option => option.Value == (int)global::TokuseiType.SET_1 && option.Label == "Overgrow (Ability 1)");
        Assert.Equal((int)global::GemType.NORMAL, trainerPokemon.TeraType);
        Assert.Equal("Normal", trainerPokemon.TeraTypeLabel);
        Assert.False(trainer.CanTerastallize);
        Assert.Equal("Disabled", trainer.TeraTarget);
        Assert.Contains(
            trainer.AiFlagStates,
            flag => flag.Label == "Basic" && flag.Description.Contains("baseline move selection", StringComparison.Ordinal));

        var trainerFields = trainers.Payload.Workflow.EditableFields;
        Assert.Contains(
            trainerFields.Single(field => field.Field == "speciesId").Options,
            option => option.Value == 1 && option.Label.Contains("Bulbasaur", StringComparison.Ordinal));
        Assert.Contains(
            trainerFields.Single(field => field.Field == "heldItemId").Options,
            option => option.Value == 1 && option.Label.Contains("Master Ball", StringComparison.Ordinal));
        Assert.Contains(
            trainerFields.Single(field => field.Field == "move1Id").Options,
            option => option.Value == 33 && option.Label.Contains("Tackle", StringComparison.Ordinal));
        Assert.Contains(
            trainerFields.Single(field => field.Field == "gender").Options,
            option => option.Value == 1 && option.Label == "Male");
        Assert.Contains(
            trainerFields.Single(field => field.Field == "ability").Options,
            option => option.Value == 4 && option.Label == "Hidden Ability");
        Assert.Contains(
            trainerFields.Single(field => field.Field == "nature").Options,
            option => option.Value == 4 && option.Label == "Adamant (+Atk, -Sp. Atk)");
        Assert.Contains(
            trainerFields.Single(field => field.Field == "shiny").Options,
            option => option.Value == 1 && option.Label == "Forced shiny");
        Assert.Contains(
            trainerFields.Single(field => field.Field == "shiny").Options,
            option => option.Value == 0 && option.Label == "Default / not forced");
        Assert.Contains(
            trainerFields.Single(field => field.Field == "teraType").Options,
            option => option.Value == (int)global::GemType.FAIRY && option.Label == "Fairy");
        Assert.Contains(
            trainerFields.Single(field => field.Field == "teraType").Options,
            option => option.Value == (int)global::GemType.NIJI && option.Label == "Stellar");

        var encounters = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-sv-encounter-labels");
        AssertSuccess(encounters);
        Assert.Contains(
            encounters.Payload!.Workflow.EditableFields.Single(field => field.Field == "speciesId").Options,
            option => option.Value == 1 && option.Label.Contains("Bulbasaur", StringComparison.Ordinal));
        var encounterTable = Assert.Single(encounters.Payload.Workflow.Tables);
        Assert.Equal("South Province (Area Two), South Province (Area Four)", encounterTable.Location);
        Assert.Equal("South Province (Area Two), South Province (Area Four)", encounterTable.Area);

        var placement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-labels");
        AssertSuccess(placement);
        Assert.Equal(
            "Emerge value 1",
            placement.Payload!.Workflow.EditableFields.Single(field => field.Field == "hidden.item1.chance").Label);
        Assert.Equal(
            int.MaxValue,
            placement.Payload.Workflow.EditableFields.Single(field => field.Field == "hidden.item1.chance").MaximumValue);

        var staticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-sv-static-encounter-labels");
        AssertSuccess(staticEncounters);
        Assert.Equal("sv", staticEncounters.Payload!.Workflow.EditorFamily);
        Assert.Equal(1, staticEncounters.Payload.Workflow.Stats.FixedSymbolCount);
        Assert.Equal(1, staticEncounters.Payload.Workflow.Stats.CoinSymbolCount);
        Assert.Contains(
            staticEncounters.Payload.Workflow.EditableFields.Single(field => field.Field == "shinyLock").Options,
            option => option.Value == (int)global::RareType.NO_RARE && option.Label == "1 Not Shiny");
        Assert.Contains(
            staticEncounters.Payload.Workflow.EditableFields.Single(field => field.Field == "shinyLock").Options,
            option => option.Value == (int)global::RareType.RARE && option.Label == "2 Shiny");

        var fixedSymbol = staticEncounters.Payload.Workflow.Encounters.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.Equal("Bulbasaur", fixedSymbol.Species);
        Assert.Equal("1 Bulbasaur", fixedSymbol.FieldDisplayValues["species"]);
        Assert.Equal("Not Shiny", fixedSymbol.FieldDisplayValues["shinyLock"]);
        Assert.Equal("33 Tackle", fixedSymbol.FieldDisplayValues["move0Id"]);
        Assert.True(fixedSymbol.FieldReadOnly["alcremieSweet"]);
        Assert.Equal("Overgrow (Ability 1)", fixedSymbol.AbilityLabel);
        Assert.Contains(
            fixedSymbol.AbilityOptions,
            option => option.Value == (int)global::TokuseiType.SET_1 && option.Label == "2 Overgrow (Ability 1)");
        Assert.Contains(
            fixedSymbol.AbilityOptions,
            option => option.Value == (int)global::TokuseiType.SET_3 && option.Label == "4 Chlorophyll (Hidden Ability)");

        var coinSymbol = staticEncounters.Payload.Workflow.Encounters.Single(entry => entry.CategoryId == "coinSymbols");
        Assert.Equal("Bulbasaur", coinSymbol.Species);
        Assert.Equal("1 Bulbasaur", coinSymbol.FieldDisplayValues["species"]);
        Assert.Equal("Shiny", coinSymbol.FieldDisplayValues["shinyLock"]);
        Assert.Equal("33 Tackle", coinSymbol.FieldDisplayValues["move0Id"]);
        Assert.Equal("Chlorophyll (Hidden Ability)", coinSymbol.AbilityLabel);
        Assert.Contains(
            coinSymbol.AbilityOptions,
            option => option.Value == (int)global::TokuseiType.SET_3 && option.Label == "4 Chlorophyll (Hidden Ability)");
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletUngroupedTechnicalMachinesUseReadableLabelsAndRemainEditable(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteSvOutput(temp, SvDataPaths.ItemDataArray, CreateUngroupedTechnicalMachineItemDataArray());
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishItemNames,
            CreateTextTable(2176, (2175, "TM115")));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishMoveNames,
            CreateTextTable(407, (45, "Growl"), (406, "Dragon Pulse")));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var items = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-sv-ungrouped-tm-items");

        AssertSuccess(items);
        var tm115 = Assert.Single(items.Payload!.Workflow.Items);
        Assert.Equal(2175, tm115.ItemId);
        Assert.Equal("TMs", tm115.Category);
        Assert.Equal(115, tm115.Metadata.MachineSlot);
        Assert.Equal(406, tm115.Metadata.MachineMoveId);
        Assert.Equal("Dragon Pulse", tm115.Metadata.MachineMoveName);
        var details = tm115.DetailGroups.Single(group => group.Label == "Scarlet/Violet").Details;
        Assert.Equal("TM", details.Single(detail => detail.Label == "Item type").Value);
        Assert.Equal("TMs", details.Single(detail => detail.Label == "Field pocket").Value);
        Assert.Equal("Teach Move", details.Single(detail => detail.Label == "Field function").Value);
        Assert.Equal("None", details.Single(detail => detail.Label == "Group").Value);

        var update = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(2175, "machineMoveId", "45")]),
            "request-sv-ungrouped-tm-update");

        AssertSuccess(update);
        Assert.DoesNotContain(update.Payload!.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var updatedTm = Assert.Single(update.Payload.Workflow.Items);
        Assert.Equal(45, updatedTm.Metadata.MachineMoveId);
        Assert.Equal("Growl", updatedTm.Metadata.MachineMoveName);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletItemEditWritesEvolutionItemFlag(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var items = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-sv-item-evolution-flag-load");

        AssertSuccess(items);
        var evolutionItemField = items.Payload!.Workflow.EditableFields.Single(field => field.Field == "evolutionItem");
        Assert.Equal("Evolution item", evolutionItemField.Label);
        Assert.Equal("boolean", evolutionItemField.ValueKind);
        var convertedItem = items.Payload.Workflow.Items.Single(item => item.ItemId == 3);
        Assert.Equal(0, convertedItem.FieldValues["evolutionItem"]);

        var update = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(3, "evolutionItem", "1")]),
            "request-sv-item-evolution-flag-update");

        AssertSuccess(update);
        Assert.DoesNotContain(update.Payload!.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var updatedItem = update.Payload.Workflow.Items.Single(item => item.ItemId == 3);
        Assert.Equal(1, updatedItem.FieldValues["evolutionItem"]);
        Assert.Equal((int)global::FieldFunctionType.FIELDFUNC_EVOLUTION, updatedItem.FieldValues["fieldUseType"]);
        Assert.Equal(1, updatedItem.FieldValues["canUseOnPokemon"]);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session),
            "request-sv-item-evolution-flag-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.EndsWith(
            SvDataPaths.EvolutionItemConversionArray,
            plan.Payload.ChangePlan.Writes[0].TargetRelativePath,
            StringComparison.Ordinal);
        Assert.Contains(
            plan.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath.EndsWith(SvDataPaths.ItemDataArray, StringComparison.Ordinal));

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan),
            "request-sv-item-evolution-flag-apply");

        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var writtenItem = ReadItem(temp, itemId: 3);
        Assert.Equal(1, writtenItem.WorkEvolutional);
        Assert.Equal(global::FieldFunctionType.FIELDFUNC_EVOLUTION, writtenItem.FieldFunctionType);
        Assert.Equal(global::WorkType.WORKTYPE_EffectPokemon, writtenItem.WorkType);
        Assert.True(writtenItem.SetToPoke);
        Assert.Equal((global::pml.common.WazaID)33, writtenItem.MachineWaza);
        Assert.Equal(global::ItemType.ITEMTYPE_NORMAL, writtenItem.ItemType);
        Assert.Equal(global::FieldPocket.FPOCKET_OTHER, writtenItem.FieldPocket);

        var writtenConversions = EvolutionItemConversionTable.Read(
            ReadSvOutput(temp, SvDataPaths.EvolutionItemConversionArray));
        Assert.Contains(writtenConversions, row => row.ParameterId == 17 && row.ItemId == 3);
        Assert.Equal(2, writtenConversions.Count(row => row.ParameterId == 119));
        Assert.Contains(writtenConversions, row => row.ParameterId == 119 && row.ItemId == 0);
        Assert.Contains(writtenConversions, row => row.ParameterId == 119 && row.ItemId == 2482);

        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-item-evolution-flag-reload");
        AssertSuccess(reloaded);
        Assert.Contains(
            reloaded.Payload!.Workflow.EvolutionMethodOptions.Single(option => option.Value == 8).ArgumentOptions,
            option => option.Value == 3 && option.Label.Contains("Legacy Move Record", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletMixedItemAndPokemonEvolutionUsesOneAllocatedMapping(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        var evolutionUpdate = Dispatch<UpdatePokemonEvolutionResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(
                paths,
                Session: null,
                PersonalId: 1,
                Action: "add",
                Slot: null,
                Method: 8,
                Argument: 3,
                Species: 2,
                Form: 0,
                Level: 0),
            "request-sv-mixed-pokemon-evolution-update");
        AssertSuccess(evolutionUpdate);
        var itemUpdate = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                evolutionUpdate.Payload!.Session,
                [new ItemFieldUpdateDto(3, "evolutionItem", "1")]),
            "request-sv-mixed-evolution-item-update");
        AssertSuccess(itemUpdate);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(
                paths,
                itemUpdate.Payload!.Session,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-mixed-evolution-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == SvDataPaths.ItemDataArray);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == SvDataPaths.PersonalArray);
        Assert.Contains(
            plan.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SvDataPaths.EvolutionItemConversionArray);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                itemUpdate.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-mixed-evolution-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var conversions = EvolutionItemConversionTable.Read(
            ReadSvOutput(temp, SvDataPaths.EvolutionItemConversionArray));
        Assert.Contains(conversions, row => row.ParameterId == 17 && row.ItemId == 3);
        var written = global::personal_table.GetRootAspersonal_table(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.PersonalArray)));
        Assert.Contains(
            Enumerable.Range(0, written.Entry(1)!.Value.EvolutionsLength)
                .Select(index => written.Entry(1)!.Value.Evolutions(index)!.Value),
            evolution => evolution.Condition == 8 && evolution.Parameter == 17 && evolution.Species == 2);
        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-mixed-evolution-reload");
        AssertSuccess(reloaded);
        var reloadedEvolution = reloaded.Payload!.Workflow.Pokemon
            .Single(row => row.PersonalId == 1)
            .Evolutions
            .Single(evolution => evolution.Method == 8 && evolution.Species == 2 && evolution.Argument == 3);
        Assert.Equal("Legacy Move Record", reloadedEvolution.ArgumentValue);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletEvolutionItemAllocationPreservesDuplicate119AndUsesApprovedRows(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.EvolutionItemConversionArray,
            EvolutionItemConversionTable.Write(
                EvolutionItemConversionTable.Read(CreateEvolutionItemConversionArray())
                    .Select(row => row.ParameterId == 19 ? row with { ItemId = 0 } : row)
                    .ToArray()));
        var paths = temp.Paths with { SelectedGame = game };
        var project = new ProjectWorkspaceService().Open(ProjectBridgeMapper.ToCore(paths));
        var state = SvEvolutionItemConversionState.Load(project, new SvWorkflowFileSource());
        int[] expectedParameters =
        [
            17, 18, 42, 43, 44, 45, 46, 47, 48, 90, 91,
            .. Enumerable.Range(53, 17),
        ];

        Assert.True(state.TryDecode(119, out var metalAlloyId));
        Assert.Equal(2482, metalAlloyId);
        for (var index = 0; index < expectedParameters.Length; index++)
        {
            Assert.Equal(expectedParameters[index], state.Encode(3000 + index));
        }

        Assert.Throws<InvalidDataException>(() => state.Encode(4000));
        var written = EvolutionItemConversionTable.Read(state.Write());
        Assert.Equal(
            EvolutionItemConversionTable.Read(CreateEvolutionItemConversionArray()).Select(row => row.ParameterId),
            written.Select(row => row.ParameterId));
        Assert.Equal(2, written.Count(row => row.ParameterId == 119));
        Assert.Contains(written, row => row.ParameterId == 119 && row.ItemId == 0);
        Assert.Contains(written, row => row.ParameterId == 119 && row.ItemId == 2482);
        Assert.All(
            written.Where(row => row.ParameterId is 11 or 12 or 13 or 14),
            row => Assert.Equal(0, row.ItemId));
        Assert.Contains(written, row => row.ParameterId == 19 && row.ItemId == 0);
        Assert.Contains(written, row => row.ParameterId == 50 && row.ItemId == 327);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletEvolutionItemAllocationProtectsActivePersonalParameters(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 17));
        var paths = temp.Paths with { SelectedGame = game };
        var project = new ProjectWorkspaceService().Open(ProjectBridgeMapper.ToCore(paths));
        var state = SvEvolutionItemConversionState.Load(project, new SvWorkflowFileSource());

        Assert.Equal(18, state.Encode(3000));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletEvolutionItemAllocationFailsClosedWithoutReadablePersonalData(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        File.Delete(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            SvDataPaths.PersonalArray.Replace('/', Path.DirectorySeparatorChar)));
        var paths = temp.Paths with { SelectedGame = game };
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(1, "evolutionItem", "1")]),
            "request-sv-missing-personal-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            "request-sv-missing-personal-evolution-item-plan");

        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("readable active Pokemon personal data", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletEvolutionItemConversionRejectsExistingFieldUseFunctions(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(2, "evolutionItem", "1")]),
            "request-sv-conflicting-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            "request-sv-conflicting-evolution-item-plan");

        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("field-use function", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletDisablingEvolutionItemRetainsItsAllocatedMapping(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };

        var enable = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(3, "evolutionItem", "1"),
                    new ItemFieldUpdateDto(3, "fieldUseType", "0"),
                ]),
            "request-sv-enable-evolution-item");
        AssertSuccess(enable);
        var enablePlan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, enable.Payload!.Session),
            "request-sv-enable-evolution-item-plan");
        AssertSuccess(enablePlan);
        var enableApply = Dispatch<ApplyChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, enable.Payload.Session, enablePlan.Payload!.ChangePlan),
            "request-sv-enable-evolution-item-apply");
        AssertSuccess(enableApply);
        var enabledItem = ReadItem(temp, 3);
        Assert.Equal(global::WorkType.WORKTYPE_EffectPokemon, enabledItem.WorkType);
        Assert.True(enabledItem.SetToPoke);

        var disable = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(3, "evolutionItem", "0")]),
            "request-sv-disable-evolution-item");
        AssertSuccess(disable);
        var disablePlan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, disable.Payload!.Session),
            "request-sv-disable-evolution-item-plan");
        AssertSuccess(disablePlan);
        Assert.DoesNotContain(
            disablePlan.Payload!.ChangePlan.Writes,
            write => write.TargetRelativePath.EndsWith(SvDataPaths.EvolutionItemConversionArray, StringComparison.Ordinal));
        var disableApply = Dispatch<ApplyChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, disable.Payload.Session, disablePlan.Payload.ChangePlan),
            "request-sv-disable-evolution-item-apply");
        AssertSuccess(disableApply);

        var writtenItem = ReadItem(temp, 3);
        Assert.Equal(0, writtenItem.WorkEvolutional);
        Assert.Equal(global::FieldFunctionType.FIELDFUNC_NONE, writtenItem.FieldFunctionType);
        Assert.Equal(global::WorkType.WORKTYPE_OTHER, writtenItem.WorkType);
        Assert.False(writtenItem.SetToPoke);
        Assert.Contains(
            EvolutionItemConversionTable.Read(ReadSvOutput(temp, SvDataPaths.EvolutionItemConversionArray)),
            row => row.ParameterId == 17 && row.ItemId == 3);
        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-disable-evolution-item-reload");
        AssertSuccess(reloaded);
        Assert.DoesNotContain(
            reloaded.Payload!.Workflow.EvolutionMethodOptions.Single(option => option.Value == 8).ArgumentOptions,
            option => option.Value == 3);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletEvolutionItemCapacityFailurePlansNoWrites(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var approvedParameters = new HashSet<int>(
            new[] { 17, 18, 42, 43, 44, 45, 46, 47, 48, 90, 91 }
                .Concat(Enumerable.Range(53, 17)));
        var occupiedRows = EvolutionItemConversionTable.Read(CreateEvolutionItemConversionArray())
            .Select((row, index) => approvedParameters.Contains(row.ParameterId)
                ? row with { ItemId = 5000 + index }
                : row)
            .ToArray();
        WriteSvOutput(
            temp,
            SvDataPaths.EvolutionItemConversionArray,
            EvolutionItemConversionTable.Write(occupiedRows));
        var paths = temp.Paths with { SelectedGame = game };
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(3, "evolutionItem", "1")]),
            "request-sv-exhausted-evolution-item-update");
        AssertSuccess(update);

        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            "request-sv-exhausted-evolution-item-plan");
        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("No approved evolution item conversion slot", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletMalformedEvolutionItemTablePlansNoWrites(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var replacedBlank119 = false;
        var conflictingRows = EvolutionItemConversionTable.Read(CreateEvolutionItemConversionArray())
            .Select(row =>
            {
                if (!replacedBlank119 && row.ParameterId == 119 && row.ItemId == 0)
                {
                    replacedBlank119 = true;
                    return row with { ItemId = 5000 };
                }

                return row;
            })
            .ToArray();
        WriteSvOutput(
            temp,
            SvDataPaths.EvolutionItemConversionArray,
            EvolutionItemConversionTable.Write(conflictingRows));
        var paths = temp.Paths with { SelectedGame = game };
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(3, "evolutionItem", "1")]),
            "request-sv-malformed-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            "request-sv-malformed-evolution-item-plan");

        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("conflicting item assignments", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ScarletVioletBuilds))]
    public void ScarletVioletEvolutionItemsUseEligibleItemIds(
        ProjectGameDto game,
        ulong titleId)
    {
        foreach (var (storedArgument, expectedItemId, expectedName) in new[]
                 {
                     (3, 82, "Fire Stone"),
                     (4, 83, "Thunder Stone"),
                 })
        {
            using var vanillaTemp = CreateScarletVioletProject(titleId);
            vanillaTemp.WriteBaseRomFsFile(
                SvDataPaths.PersonalArray,
                CreatePersonalArray(evolutionCondition: 8, evolutionParameter: (ushort)storedArgument));
            vanillaTemp.WriteBaseRomFsFile(
                SvDataPaths.ItemDataArray,
                CreateItemDataArray(fireStoneEvolutionItem: true));
            vanillaTemp.WriteBaseRomFsFile(
                SvDataPaths.EnglishItemNames,
                CreateTextTable(expectedItemId + 1, (expectedItemId, expectedName)));
            var vanillaPaths = vanillaTemp.Paths with { SelectedGame = game };
            var vanillaDispatcher = CreateDispatcherWithSvCache(vanillaTemp);

            var vanillaPokemon = Dispatch<LoadPokemonWorkflowResponse>(
                vanillaDispatcher,
                KmCommandNames.LoadPokemonWorkflow,
                new LoadPokemonWorkflowRequest(vanillaPaths),
                $"request-sv-pokemon-vanilla-evolution-item-{storedArgument}");

            AssertSuccess(vanillaPokemon);
            var vanillaEvolution = Assert.Single(
                vanillaPokemon.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
            Assert.Equal(expectedItemId, vanillaEvolution.Argument);
            Assert.Equal(expectedName, vanillaEvolution.ArgumentValue);

            var addCustomEvolution = Dispatch<UpdatePokemonEvolutionResponse>(
                vanillaDispatcher,
                KmCommandNames.UpdatePokemonEvolution,
                new UpdatePokemonEvolutionRequest(
                    vanillaPaths,
                    Session: null,
                    PersonalId: 1,
                    Action: "add",
                    Slot: null,
                    Method: 8,
                    Argument: 248,
                    Species: 2,
                    Form: 0,
                    Level: 0),
                $"request-sv-pokemon-add-before-vanilla-evolution-item-{storedArgument}");
            AssertSuccess(addCustomEvolution);

            var moveCustomEvolution = Dispatch<UpdatePokemonEvolutionResponse>(
                vanillaDispatcher,
                KmCommandNames.UpdatePokemonEvolution,
                new UpdatePokemonEvolutionRequest(
                    vanillaPaths,
                    addCustomEvolution.Payload!.Session,
                    PersonalId: 1,
                    Action: "moveUp",
                    Slot: 1,
                    Method: null,
                    Argument: null,
                    Species: null,
                    Form: null,
                    Level: null),
                $"request-sv-pokemon-move-before-vanilla-evolution-item-{storedArgument}");
            AssertSuccess(moveCustomEvolution);
            var reorderedEvolutions = moveCustomEvolution.Payload!.Workflow.Pokemon
                .Single(row => row.PersonalId == 1)
                .Evolutions;
            Assert.Equal(248, reorderedEvolutions.Single(row => row.Slot == 0).Argument);
            Assert.Equal(expectedItemId, reorderedEvolutions.Single(row => row.Slot == 1).Argument);

            var reorderedPlan = Dispatch<CreateChangePlanResponse>(
                vanillaDispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(vanillaPaths, moveCustomEvolution.Payload.Session),
                $"request-sv-pokemon-reordered-vanilla-evolution-plan-{storedArgument}");
            AssertSuccess(reorderedPlan);
            var reorderedApply = Dispatch<ApplyChangePlanResponse>(
                vanillaDispatcher,
                KmCommandNames.ApplyChangePlan,
                new ApplyChangePlanRequest(
                    vanillaPaths,
                    moveCustomEvolution.Payload.Session,
                    reorderedPlan.Payload!.ChangePlan),
                $"request-sv-pokemon-reordered-vanilla-evolution-apply-{storedArgument}");
            AssertSuccess(reorderedApply);

            var writtenConversions = EvolutionItemConversionTable.Read(
                ReadSvOutput(vanillaTemp, SvDataPaths.EvolutionItemConversionArray));
            var twistedSpoonParameter = writtenConversions.Single(row => row.ItemId == 248).ParameterId;
            var writtenPersonal = global::personal_table.GetRootAspersonal_table(
                new ByteBuffer(ReadSvOutput(vanillaTemp, SvDataPaths.PersonalArray)));
            Assert.Equal(twistedSpoonParameter, writtenPersonal.Entry(1)!.Value.Evolutions(0)!.Value.Parameter);
            Assert.Equal(storedArgument, writtenPersonal.Entry(1)!.Value.Evolutions(1)!.Value.Parameter);

            var reloadedPokemon = Dispatch<LoadPokemonWorkflowResponse>(
                CreateDispatcherWithSvCache(vanillaTemp),
                KmCommandNames.LoadPokemonWorkflow,
                new LoadPokemonWorkflowRequest(vanillaPaths),
                $"request-sv-pokemon-reordered-vanilla-evolution-reload-{storedArgument}");
            AssertSuccess(reloadedPokemon);
            var reloadedEvolutions = reloadedPokemon.Payload!.Workflow.Pokemon
                .Single(row => row.PersonalId == 1)
                .Evolutions;
            Assert.Equal(248, reloadedEvolutions.Single(row => row.Slot == 0).Argument);
            Assert.Equal(expectedItemId, reloadedEvolutions.Single(row => row.Slot == 1).Argument);
        }

        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseRomFsFile(
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 3));
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 86));
        WriteSvOutput(
            temp,
            SvDataPaths.ItemDataArray,
            CreateItemDataArray(
                masterBallEvolutionItem: true,
                ultraBallEvolutionItem: true,
                includeTinyMushroom: true,
                includeMaliciousArmor: true));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishItemNames,
            CreateTextTable(
                2483,
                (1, "Master Ball"),
                (2, "Ultra Ball"),
                (81, "Moon Stone"),
                (86, "Tiny Mushroom"),
                (1861, "Malicious Armor"),
                (2482, "Metal Alloy")));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-pokemon-evolution-items");

        AssertSuccess(pokemon);
        var workflow = pokemon.Payload!.Workflow;
        var bulbasaur = workflow.Pokemon.Single(row => row.PersonalId == 1);
        var evolution = Assert.Single(bulbasaur.Evolutions);
        Assert.Equal(1861, evolution.Argument);
        Assert.Equal("Malicious Armor", evolution.ArgumentValue);

        var useItem = workflow.EvolutionMethodOptions.Single(option => option.Value == 8);
        Assert.Contains(useItem.ArgumentOptions, option => option.Value == 1 && option.Label == "1 Master Ball");
        Assert.Contains(useItem.ArgumentOptions, option => option.Value == 2 && option.Label == "2 Ultra Ball");
        Assert.Contains(useItem.ArgumentOptions, option => option.Value == 1861 && option.Label == "1861 Malicious Armor");
        Assert.DoesNotContain(useItem.ArgumentOptions, option => option.Value == 81);
        Assert.DoesNotContain(useItem.ArgumentOptions, option => option.Value == 86);
        Assert.DoesNotContain(useItem.ArgumentOptions, option => option.Value == 2482);

        var tradeHeldItem = workflow.EvolutionMethodOptions.Single(option => option.Value == 6);
        Assert.Contains(tradeHeldItem.ArgumentOptions, option => option.Value == 2 && option.Label == "2 Ultra Ball");
        Assert.Contains(tradeHeldItem.ArgumentOptions, option => option.Value == 86 && option.Label == "86 Tiny Mushroom");

        var update = Dispatch<UpdatePokemonEvolutionResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(
                paths,
                Session: null,
                PersonalId: 1,
                Action: "add",
                Slot: null,
                Method: 8,
                Argument: 86,
                Species: 2,
                Form: 0,
                Level: 0),
            "request-sv-pokemon-custom-evolution-item-label");

        AssertSuccess(update);
        var updatedEvolutions = update.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions;
        Assert.Equal(2, updatedEvolutions.Count);
        var updatedEvolution = updatedEvolutions.Single(row => row.Slot == 1);
        Assert.Equal(86, updatedEvolution.Argument);
        Assert.Equal("86 Tiny Mushroom", updatedEvolution.ArgumentValue);
    }

    [Theory]
    [MemberData(nameof(ScarletVioletBuilds))]
    public void ScarletVioletHeldItemEvolutionsUseItemIds(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 19, evolutionParameter: 2));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishItemNames,
            CreateTextTable(
                2483,
                (2, "Ultra Ball"),
                (81, "Moon Stone"),
                (2482, "Metal Alloy")));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-pokemon-held-item-evolution");

        AssertSuccess(pokemon);
        var workflow = pokemon.Payload!.Workflow;
        var bulbasaur = workflow.Pokemon.Single(row => row.PersonalId == 1);
        var evolution = Assert.Single(bulbasaur.Evolutions);
        Assert.Equal(19, evolution.Method);
        Assert.Equal(81, evolution.Argument);
        Assert.Equal("Moon Stone", evolution.ArgumentValue);

        var heldItemDay = workflow.EvolutionMethodOptions.Single(option => option.Value == 19);
        Assert.Contains(heldItemDay.ArgumentOptions, option => option.Value == 2 && option.Label == "2 Ultra Ball");
        Assert.DoesNotContain(heldItemDay.ArgumentOptions, option => option.Value == 2 && option.Label == "2 Moon Stone");

        var heldItemNight = workflow.EvolutionMethodOptions.Single(option => option.Value == 20);
        Assert.Contains(heldItemNight.ArgumentOptions, option => option.Value == 2 && option.Label == "2 Ultra Ball");

        var useItem = workflow.EvolutionMethodOptions.Single(option => option.Value == 8);
        Assert.Contains(useItem.ArgumentOptions, option => option.Value == 81 && option.Label == "81 Moon Stone");
        Assert.DoesNotContain(useItem.ArgumentOptions, option => option.Value == 2 && option.Label == "2 Moon Stone");

        var update = Dispatch<UpdatePokemonEvolutionResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(
                paths,
                Session: null,
                PersonalId: 1,
                Action: "upsert",
                Slot: 0,
                Method: 20,
                Argument: 2,
                Species: 2,
                Form: 0,
                Level: 24),
            "request-sv-pokemon-held-item-evolution-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-pokemon-held-item-evolution-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Equal(
            SvDataPaths.EvolutionItemConversionArray,
            plan.Payload.ChangePlan.Writes[0].TargetRelativePath);
        Assert.Contains(
            plan.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SvDataPaths.EvolutionItemConversionArray);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                update.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-pokemon-held-item-evolution-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal(SvDataPaths.EvolutionItemConversionArray, apply.Payload.ApplyResult.WrittenFiles[0]);

        var writtenConversions = EvolutionItemConversionTable.Read(
            ReadSvOutput(temp, SvDataPaths.EvolutionItemConversionArray));
        var ultraBallParameter = writtenConversions.Single(row => row.ItemId == 2).ParameterId;
        var writtenPersonal = global::personal_table.GetRootAspersonal_table(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.PersonalArray)));
        var writtenEvolution = writtenPersonal.Entry(1)!.Value.Evolutions(0)!.Value;
        Assert.Equal(20, writtenEvolution.Condition);
        Assert.Equal(ultraBallParameter, writtenEvolution.Parameter);
        Assert.Equal(2, writtenEvolution.Species);
        Assert.Equal(24, writtenEvolution.Level);

        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-pokemon-held-item-evolution-reload");
        AssertSuccess(reloaded);
        var reloadedEvolution = Assert.Single(
            reloaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
        Assert.Equal(2, reloadedEvolution.Argument);
        Assert.Equal("Ultra Ball", reloadedEvolution.ArgumentValue);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletReservedParameter50DisplaysHistoricalRazorFang(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 20, evolutionParameter: 50));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishItemNames,
            CreateTextTable(328, (50, "Rare Candy"), (327, "Razor Fang")));
        var paths = temp.Paths with { SelectedGame = game };

        var response = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-reserved-evolution-parameter-50");

        AssertSuccess(response);
        var evolution = Assert.Single(
            response.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
        Assert.Equal(327, evolution.Argument);
        Assert.Equal("Razor Fang", evolution.ArgumentValue);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGameAndConvertedUseItemMethods))]
    public void ScarletVioletConversionBackedUseItemMethodsRoundTripThroughConversionTable(
        ProjectGameDto game,
        ulong titleId,
        int method)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: (ushort)method, evolutionParameter: 3));
        WriteSvOutput(
            temp,
            SvDataPaths.ItemDataArray,
            CreateItemDataArray(fireStoneEvolutionItem: true));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishItemNames,
            CreateTextTable(83, (82, "Fire Stone")));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            $"request-sv-converted-use-item-{method}-load");
        AssertSuccess(loaded);
        var loadedEvolution = Assert.Single(
            loaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
        Assert.Equal(method, loadedEvolution.Method);
        Assert.Equal(82, loadedEvolution.Argument);
        Assert.Equal("Fire Stone", loadedEvolution.ArgumentValue);

        var update = Dispatch<UpdatePokemonEvolutionResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(
                paths,
                Session: null,
                PersonalId: 1,
                Action: "upsert",
                Slot: 0,
                Method: method,
                Argument: 82,
                Species: 2,
                Form: 0,
                Level: 0),
            $"request-sv-converted-use-item-{method}-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            $"request-sv-converted-use-item-{method}-plan");
        AssertSuccess(plan);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                update.Payload.Session,
                plan.Payload!.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            $"request-sv-converted-use-item-{method}-apply");
        AssertSuccess(apply);

        var written = global::personal_table.GetRootAspersonal_table(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.PersonalArray)));
        Assert.Equal(3, written.Entry(1)!.Value.Evolutions(0)!.Value.Parameter);
        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            $"request-sv-converted-use-item-{method}-reload");
        AssertSuccess(reloaded);
        Assert.Equal(
            82,
            Assert.Single(reloaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions).Argument);
    }

    [Theory]
    [MemberData(nameof(ScarletVioletBuilds))]
    public void ScarletVioletLegacyRawEvolutionItemArgumentsAreMigrated(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseRomFsFile(
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 3));
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 248));
        WriteSvOutput(
            temp,
            SvDataPaths.ItemDataArray,
            CreateItemDataArray(includeTwistedSpoon: true));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishItemNames,
            CreateTextTable(249, (248, "Twisted Spoon")));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var update = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, Session: null, PersonalId: 1, Field: "hp", Value: "99"),
            "request-sv-pokemon-legacy-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-pokemon-legacy-evolution-item-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(
            plan.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SvDataPaths.EvolutionItemConversionArray);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                update.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-pokemon-legacy-evolution-item-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var writtenConversions = EvolutionItemConversionTable.Read(
            ReadSvOutput(temp, SvDataPaths.EvolutionItemConversionArray));
        var twistedSpoonParameter = writtenConversions.Single(row => row.ItemId == 248).ParameterId;
        var writtenPersonal = global::personal_table.GetRootAspersonal_table(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.PersonalArray)));
        var writtenEntry = writtenPersonal.Entry(1)!.Value;
        Assert.Equal(99, writtenEntry.BaseStats!.Value.Hp);
        Assert.Equal(twistedSpoonParameter, writtenEntry.Evolutions(0)!.Value.Parameter);
        Assert.Equal(1, writtenEntry.PaldeaDex!.Value.Index);

        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-pokemon-legacy-evolution-item-reload");
        AssertSuccess(reloaded);
        var evolution = Assert.Single(
            reloaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
        Assert.Equal(248, evolution.Argument);
        Assert.Equal("Twisted Spoon", evolution.ArgumentValue);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletEvolutionConditionArgumentsUseNamedValues(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteSvOutput(
            temp,
            SvDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 31, evolutionParameter: 1));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-pokemon-evolution-conditions");

        AssertSuccess(pokemon);
        var workflow = pokemon.Payload!.Workflow;
        var bulbasaur = workflow.Pokemon.Single(row => row.PersonalId == 1);
        var evolution = Assert.Single(bulbasaur.Evolutions);
        Assert.Equal(31, evolution.Method);
        Assert.Equal("Level Up In Rain", evolution.MethodName);
        Assert.Equal("Hisuian rain rule", evolution.ArgumentValue);

        var pancham = workflow.EvolutionMethodOptions.Single(option => option.Value == 30);
        Assert.Equal("none", pancham.ArgumentKind);
        Assert.Contains("Dark-Type Teammate", pancham.Label, StringComparison.Ordinal);
        Assert.Empty(pancham.ArgumentOptions);

        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 31).ArgumentOptions,
            option => option.Value == 1 && option.Label == "Hisuian rain rule");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 36).ArgumentOptions,
            option => option.Value == 50 && option.Label == "Solgaleo branch");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 36).ArgumentOptions,
            option => option.Value == 51 && option.Label == "Lunala branch");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 43).ArgumentOptions,
            option => option.Value == 3 && option.Label == "3 critical hits");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 44).ArgumentOptions,
            option => option.Value == 49 && option.Label == "49 HP lost");
    }

    [Fact]
    public void ScarletVioletProjectLoadsAncientPowerMoveEffects()
    {
        using var temp = CreateScarletVioletProject(ScarletTitleId);
        WriteSvOutput(temp, SvDataPaths.MoveDataArray, CreateAncientPowerMoveDataArray());
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishMoveNames,
            CreateTextTable(247, (246, "Ancient Power")));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishMoveDescriptions,
            CreateTextTable(247, (246, "The user attacks with a prehistoric power.")));
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var moves = Dispatch<LoadMovesWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadMovesWorkflow,
            new LoadMovesWorkflowRequest(temp.Paths with { SelectedGame = ProjectGameDto.Scarlet }),
            "request-sv-ancient-power");

        AssertSuccess(moves);
        var workflow = moves.Payload!.Workflow;
        var ancientPower = Assert.Single(workflow.Moves);
        Assert.Equal("Ancient Power", ancientPower.Name);
        Assert.Equal("The user attacks with a prehistoric power.", ancientPower.Description);
        Assert.Equal("Rock", ancientPower.TypeName);
        Assert.Equal("Special", ancientPower.CategoryName);
        Assert.Equal(60, ancientPower.Power);
        Assert.Equal(100, ancientPower.Accuracy);
        Assert.Equal(5, ancientPower.PP);
        Assert.Contains(
            ancientPower.StatChanges,
            change => change.Slot == 1
                && change.Stat == 9
                && change.StatName == "All Stats"
                && change.Stage == 1
                && change.Percent == 10);
        Assert.Contains(
            ancientPower.StatChanges,
            change => change.Slot == 2
                && change.Stat == 0
                && change.Stage == 0
                && change.Percent == 0);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == "stat1").Options,
            option => option.Value == 9 && option.Label == "9 All Stats");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == "stat1").Options,
            option => option.Value == 8 && option.Label == "8 Critical Hit Rate");
        Assert.Empty(workflow.EditableFields.Single(field => field.Field == "rawHealing").Options);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTextEditorLoadsAllMessageFiles(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        temp.WriteBaseRomFsFile(
            "message/dat/English/script/common_0025.dat",
            CreateTextTable(2, (0, "Script line"), (1, "Second script line")));
        var paths = temp.Paths with { SelectedGame = game, GameTextLanguage = "en" };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var response = Dispatch<LoadTextWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(paths),
            "request-sv-text-load");

        AssertSuccess(response);
        var workflow = response.Payload!.Workflow;
        Assert.Equal("text", workflow.Summary.Id);
        Assert.Contains(workflow.Entries, entry =>
            entry.SourceFile == "romfs/message/dat/English/common/itemname.dat"
            && entry.Value == "Master Ball");
        Assert.Contains(workflow.Entries, entry =>
            entry.SourceFile == "romfs/message/dat/English/script/common_0025.dat"
            && entry.Value == "Script line");
        Assert.Equal(workflow.Stats.TotalTextEntryCount, workflow.Entries.Count);
        Assert.Equal(workflow.Stats.DialogueReferenceCount, workflow.DialogueReferences.Count);
        Assert.Equal(
            workflow.Stats.SourceFileCount,
            workflow.Entries.Select(entry => entry.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(workflow.Stats.SourceFileCount >= 8);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTextEditorSupportsBoundedSearchQueries(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        const string scriptPath = "message/dat/English/script/common_0025.dat";
        temp.WriteBaseRomFsFile(
            scriptPath,
            CreateTextTable(2, (0, "Alpha script line"), (1, "Second script line")));
        var paths = temp.Paths with { SelectedGame = game, GameTextLanguage = "en" };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        var query = new TextWorkflowQueryDto("Second script", 0, 1);

        var load = Dispatch<LoadTextWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(paths, query),
            "request-sv-text-query-load");

        AssertSuccess(load);
        var entry = Assert.Single(load.Payload!.Workflow.Entries);
        Assert.Equal($"romfs/{scriptPath}", entry.SourceFile);
        Assert.Equal(1, entry.LineIndex);
        Assert.Equal("Second script line", entry.Value);
        Assert.Equal(1, load.Payload.Workflow.Stats.TotalTextEntryCount);

        var update = Dispatch<UpdateTextEntryResponse>(
            dispatcher,
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(paths, Session: null, entry.TextKey, "Renamed script line", Query: query),
            "request-sv-text-query-update");

        AssertSuccess(update);
        var updatedEntry = Assert.Single(update.Payload!.Workflow.Entries);
        Assert.Equal(entry.TextKey, updatedEntry.TextKey);
        Assert.Equal("Renamed script line", updatedEntry.Value);
        Assert.Contains(update.Payload.Session.PendingEdits, edit =>
            edit.Domain == "workflow.text" && edit.RecordId == entry.TextKey && edit.NewValue == "Renamed script line");
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletTextEditorStagesAndAppliesMessageEdits(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        const string scriptPath = "message/dat/English/script/common_0025.dat";
        temp.WriteBaseRomFsFile(scriptPath, CreateTextTable(1, (0, "[VAR 0100] Original script line")));
        var paths = temp.Paths with { SelectedGame = game, GameTextLanguage = "en" };
        var dispatcher = CreateDispatcherWithSvCache(temp);
        var load = Dispatch<LoadTextWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(paths),
            "request-sv-text-edit-load");
        AssertSuccess(load);
        var entry = load.Payload!.Workflow.Entries.Single(entry =>
            entry.SourceFile == $"romfs/{scriptPath}" && entry.LineIndex == 0);
        Assert.True(entry.CanEdit);
        Assert.Null(entry.EditBlockedReason);

        var update = Dispatch<UpdateTextEntryResponse>(
            dispatcher,
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(paths, Session: null, entry.TextKey, "[VAR 0100] Updated script line"),
            "request-sv-text-update");
        AssertSuccess(update);
        Assert.Contains(update.Payload!.Session.PendingEdits, edit =>
            edit.Domain == "workflow.text" && edit.RecordId == entry.TextKey && edit.NewValue == "[VAR 0100] Updated script line");

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-text-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        Assert.Equal(scriptPath, write.TargetRelativePath);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-text-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal([scriptPath], apply.Payload.ApplyResult.WrittenFiles);

        var outputText = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            scriptPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal("[VAR 0100] Updated script line", Assert.Single(outputText.Lines).Text);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletGameDumpWritesWorkflowParityCategoryFiles(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        temp.WriteBaseRomFsFile(
            "message/dat/English/script/common_0025.dat",
            CreateTextTable(1, (0, "Game dump S/V script line")));
        var paths = temp.Paths with { SelectedGame = game, GameTextLanguage = "en" };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var load = Dispatch<LoadGameDumpWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGameDumpWorkflow,
            new LoadGameDumpWorkflowRequest(paths),
            "request-sv-game-dump-load");

        AssertSuccess(load);
        Assert.Equal(
            ["items", "pokemon", "moves", "text", "trainers", "encounters", "teraRaids", "staticEncounters", "giftPokemon", "tradePokemon", "placement", "shops", "typeChart"],
            load.Payload!.Workflow.Categories.Select(category => category.Id).ToArray());
        Assert.Contains(load.Payload.Workflow.Categories, category => category.Id == "text" && category.IsAvailable);
        Assert.Contains(load.Payload.Workflow.Categories, category => category.Id == "teraRaids" && category.IsAvailable);
        Assert.Contains(load.Payload.Workflow.Categories, category => category.Id == "staticEncounters" && category.IsAvailable);
        Assert.Contains(load.Payload.Workflow.Categories, category => category.Id == "shops" && category.IsAvailable);

        var destinationFolder = Path.Combine(temp.RootPath, "dump");
        var run = Dispatch<RunGameDumpResponse>(
            dispatcher,
            KmCommandNames.RunGameDump,
            new RunGameDumpRequest(
                paths,
                destinationFolder,
                [
                    new GameDumpSelectionDto("text", GameDumpFormatDto.TxtAndJson),
                    new GameDumpSelectionDto("teraRaids", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("staticEncounters", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("shops", GameDumpFormatDto.Json),
                ]),
            "request-sv-game-dump-run");

        AssertSuccess(run);
        Assert.True(
            run.Payload!.Result.Succeeded,
            string.Join(Environment.NewLine, run.Payload.Result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "text" && file.RelativePath == Path.Combine("Text", "text.txt"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "text" && file.RelativePath == Path.Combine("Text", "text.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "teraRaids" && file.RelativePath == Path.Combine("Tera Raids", "teraRaids.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "staticEncounters" && file.RelativePath == Path.Combine("Static Encounters", "staticEncounters.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "shops" && file.RelativePath == Path.Combine("Shops", "shops.json"));
        Assert.Contains("Game dump S/V script line", File.ReadAllText(Path.Combine(destinationFolder, "Text", "text.txt")));
        Assert.Contains("Bulbasaur", File.ReadAllText(Path.Combine(destinationFolder, "Tera Raids", "teraRaids.json")));
        Assert.Contains("eventBattle", File.ReadAllText(Path.Combine(destinationFolder, "Static Encounters", "staticEncounters.json")));
        Assert.Contains("Master Ball", File.ReadAllText(Path.Combine(destinationFolder, "Shops", "shops.json")));
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPokemonLearnsetsDisplayEvolutionSentinelAndPreserveRawLevel(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(temp, SvDataPaths.PersonalArray, CreatePersonalArrayWithLevelupMoves((33, 253), (45, 1)));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var loaded = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-sv-pokemon-learnset-evolution-load");
        AssertSuccess(loaded);
        var bulbasaur = loaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1);
        var evolutionMove = bulbasaur.Learnset[0];
        Assert.Equal(0, evolutionMove.Level);
        Assert.Equal(253, evolutionMove.RawLevel);
        Assert.Equal("Evolution", evolutionMove.LevelLabel);

        var staged = Dispatch<UpdatePokemonLearnsetResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonLearnset,
            new UpdatePokemonLearnsetRequest(paths, null, 1, "upsert", 0, 45, 0),
            "request-sv-pokemon-learnset-evolution-stage");
        AssertSuccess(staged);
        var edit = Assert.Single(staged.Payload!.Session.PendingEdits);
        Assert.Equal("45|253", edit.NewValue);
        Assert.Contains("Evolution", edit.Summary, StringComparison.Ordinal);

        Apply(dispatcher, paths, staged.Payload.Session);
        var personal = ReadPersonal(temp, personalId: 1);
        Assert.Equal(45, personal.LevelupMoves(0)!.Value.Move);
        Assert.Equal(253, personal.LevelupMoves(0)!.Value.Level);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPlacementAlcremieSweetOnlyEditsAlcremieFixedSymbols(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var staticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-sv-static-encounter-alcremie-sweet-load");
        AssertSuccess(staticEncounters);
        var fixedSymbol = staticEncounters.Payload!.Workflow.Encounters.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.True(fixedSymbol.FieldReadOnly["alcremieSweet"]);

        var stagedSpecies = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(
                paths,
                Session: null,
                fixedSymbol.EncounterIndex,
                Field: "species",
                Value: ((int)global::pml.common.DevID.DEV_MAHOIPPU).ToString(CultureInfo.InvariantCulture)),
            "request-sv-static-encounter-alcremie-sweet-unlock");
        AssertSuccess(stagedSpecies);
        var stagedFixedSymbol = stagedSpecies.Payload!.Workflow.Encounters.Single(entry => entry.EncounterIndex == fixedSymbol.EncounterIndex);
        Assert.False(stagedFixedSymbol.FieldReadOnly["alcremieSweet"]);

        WriteSvOutput(
            temp,
            SvDataPaths.FixedSymbolTableArray,
            CreateFixedSymbolTableArray(global::pml.common.DevID.DEV_MAHOIPPU));
        var alcremieStaticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-sv-static-encounter-alcremie-sweet-alcremie-load");
        AssertSuccess(alcremieStaticEncounters);
        var alcremieFixedSymbol = alcremieStaticEncounters.Payload!.Workflow.Encounters.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.False(alcremieFixedSymbol.FieldReadOnly["alcremieSweet"]);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPlacementFixedSymbolsResolveDefaultMovesBeforeManualMoveEdits(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var personalBytes = CreatePersonalArrayWithLevelupMoves((33, 253), (45, 3), (36, 5), (349, 7));
        var personalTable = global::personal_table.GetRootAspersonal_table(new ByteBuffer(personalBytes));
        Assert.Equal(4, personalTable.Entry(1)!.Value.LevelupMovesLength);
        WriteSvOutput(temp, SvDataPaths.PersonalArray, personalBytes);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var staticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-sv-static-encounter-default-moves");
        AssertSuccess(staticEncounters);
        var fixedSymbol = staticEncounters.Payload!.Workflow.Encounters.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.Equal("33 Tackle", fixedSymbol.FieldDisplayValues["move0Id"]);
        Assert.Equal("45 Growl", fixedSymbol.FieldDisplayValues["move1Id"]);
        Assert.Equal("36 Take Down", fixedSymbol.FieldDisplayValues["move2Id"]);
        Assert.Equal("None", fixedSymbol.FieldDisplayValues["move3Id"]);

        var session = UpdateStaticEncounter(dispatcher, paths, fixedSymbol.EncounterIndex, field: "move0Id", value: "349");
        Apply(dispatcher, paths, session);

        var output = FixedSymbolTableArray.GetRootAsFixedSymbolTableArray(new ByteBuffer(ReadSvOutput(temp, SvDataPaths.FixedSymbolTableArray)));
        var outputPokeData = output.Values(0)!.Value.PokeDataSymbol!.Value;
        Assert.Equal(global::WazaType.MANUAL, outputPokeData.WazaType);
        Assert.Equal((global::pml.common.WazaID)349, outputPokeData.Waza1!.Value.WazaId);
        Assert.Equal((global::pml.common.WazaID)45, outputPokeData.Waza2!.Value.WazaId);
        Assert.Equal((global::pml.common.WazaID)36, outputPokeData.Waza3!.Value.WazaId);
        Assert.Equal((global::pml.common.WazaID)0, outputPokeData.Waza4!.Value.WazaId);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPlacementHiddenItemsStageAndApplyItemSlots(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(
            temp,
            SvDataPaths.HiddenItemDataTableSu1Array,
            CreateHiddenItemDataTableArray(tableId: "kitakami_hidden", firstItemId: 4, firstEmergePercent: 80, firstDropCount: 3));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var placement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-hidden-items-load");
        AssertSuccess(placement);
        Assert.NotNull(placement.Payload!.Workflow.Categories);
        Assert.Equal(2, placement.Payload.Workflow.Categories.Single(category => category.Id == "hiddenItems").ObjectCount);
        var hiddenItem = placement.Payload.Workflow.Objects.Single(entry =>
            entry.CategoryId == "hiddenItems"
            && string.Equals(entry.Map, "Hidden Items - Paldea", StringComparison.Ordinal));
        Assert.Equal("1 Master Ball", hiddenItem.Fields!.Single(field => field.Field == "hidden.item1.itemId").DisplayValue);
        Assert.Equal("200", hiddenItem.Fields!.Single(field => field.Field == "hidden.item1.chance").DisplayValue);
        Assert.Equal("1", hiddenItem.Fields!.Single(field => field.Field == "hidden.item1.count").DisplayValue);

        var session = UpdatePlacement(dispatcher, paths, hiddenItem.ObjectId, field: "hidden.item1.itemId", value: "5");
        session = UpdatePlacement(dispatcher, paths, session, hiddenItem.ObjectId, field: "hidden.item1.chance", value: "175");
        session = UpdatePlacement(dispatcher, paths, session, hiddenItem.ObjectId, field: "hidden.item1.count", value: "4");
        Apply(dispatcher, paths, session);

        var paldeaOutput = HiddenItemDataTableArray.GetRootAsHiddenItemDataTableArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.HiddenItemDataTableArray)));
        var editedSlot = paldeaOutput.Values(0)!.Value.Item(0)!.Value;
        Assert.Equal(5, editedSlot.ItemId);
        Assert.Equal(175, editedSlot.EmergePercent);
        Assert.Equal(4, editedSlot.DropCount);
        var untouchedSlot = paldeaOutput.Values(0)!.Value.Item(1)!.Value;
        Assert.Equal(2, untouchedSlot.ItemId);
        Assert.Equal(50, untouchedSlot.EmergePercent);
        Assert.Equal(2, untouchedSlot.DropCount);

        var kitakamiOutput = HiddenItemDataTableArray.GetRootAsHiddenItemDataTableArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.HiddenItemDataTableSu1Array)));
        var kitakamiSlot = kitakamiOutput.Values(0)!.Value.Item(0)!.Value;
        Assert.Equal(4, kitakamiSlot.ItemId);
        Assert.Equal(80, kitakamiSlot.EmergePercent);
        Assert.Equal(3, kitakamiSlot.DropCount);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPlacementLoadsVisibleItemScenePoints(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var placement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-visible-items-load");

        AssertSuccess(placement);
        Assert.NotNull(placement.Payload!.Workflow.Categories);
        Assert.Equal(1, placement.Payload.Workflow.Categories.Single(category => category.Id == "visibleItems").ObjectCount);
        var visibleItem = Assert.Single(
            placement.Payload.Workflow.Objects,
            entry => entry.CategoryId == "visibleItems");
        Assert.Equal("VisibleItemScenePoint", visibleItem.ObjectType);
        Assert.Equal("Visible Items", visibleItem.CategoryLabel);
        Assert.Equal("Visible Items - Paldea", visibleItem.Map);
        Assert.Equal((uint)5, visibleItem.ItemId);
        Assert.Equal("TM100", visibleItem.ItemName);
        Assert.Equal("5", visibleItem.ItemHash);
        Assert.Equal(3, visibleItem.Quantity);
        Assert.Equal(12.5, visibleItem.X);
        Assert.Equal(20, visibleItem.Y);
        Assert.Equal(-7.25, visibleItem.Z);
        Assert.Equal(1.5, visibleItem.RotationY);
        Assert.Equal("itemball_test_1", visibleItem.ScriptId);

        var visibleFields = visibleItem.Fields;
        Assert.NotNull(visibleFields);
        Assert.Equal("itemball_test_1", visibleFields!.Single(field => field.Field == "point.name").DisplayValue);
        Assert.Equal("event_category_item", visibleFields.Single(field => field.Field == "visible.pointType").DisplayValue);
        Assert.Equal("5 TM100", visibleFields.Single(field => field.Field == "visible.itemId").DisplayValue);
        Assert.Equal("3", visibleFields.Single(field => field.Field == "visible.quantity").DisplayValue);
        Assert.True(visibleFields.Single(field => field.Field == "point.name").IsReadOnly);
        Assert.True(visibleFields.Single(field => field.Field == "visible.pointType").IsReadOnly);
        Assert.False(visibleFields.Single(field => field.Field == "visible.itemId").IsReadOnly);
        Assert.False(visibleFields.Single(field => field.Field == "visible.quantity").IsReadOnly);
        Assert.True(visibleFields.Single(field => field.Field == "point.positionX").IsReadOnly);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPlacementVisibleItemsStageAndApplySceneItemFields(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var placement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-visible-items-apply-load");

        AssertSuccess(placement);
        var visibleItem = Assert.Single(
            placement.Payload!.Workflow.Objects,
            entry => entry.CategoryId == "visibleItems");

        var session = UpdatePlacement(dispatcher, paths, visibleItem.ObjectId, field: "visible.itemId", value: "2");
        session = UpdatePlacement(dispatcher, paths, session, visibleItem.ObjectId, field: "visible.quantity", value: "7");
        Apply(dispatcher, paths, session);

        var scenePath = game == ProjectGameDto.Violet
            ? SvDataPaths.VisibleItemScenePaldeaViolet
            : SvDataPaths.VisibleItemScenePaldeaScarlet;
        var points = KM.SV.Placement.SvVisibleItemSceneReader.Read(ReadSvOutput(temp, scenePath), scenePath);
        var editedPoint = Assert.Single(points);
        Assert.Equal(2, editedPoint.ItemId);
        Assert.Equal(7, editedPoint.Quantity);
        Assert.Equal(12.5f, editedPoint.X);
        Assert.Equal(20, editedPoint.Y);
        Assert.Equal(-7.25f, editedPoint.Z);
    }

    [Theory]
    [MemberData(nameof(RepresentativeScarletVioletGame))]
    public void ScarletVioletPokemonBaseExperienceAndYieldButtonsUsePersonalTableSemantics(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        temp.WriteBaseRomFsFile(SvDataPaths.PersonalArray, CreatePersonalArray());
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var baseExpSession = UpdatePokemonField(dispatcher, paths, personalId: 1, field: "baseExperience", value: "64");
        Apply(dispatcher, paths, baseExpSession);
        Assert.Equal(48, ReadPersonal(temp, personalId: 1).ExpAddend);

        var removeExpSession = UpdatePokemonField(dispatcher, paths, personalId: 0, field: "expYieldAll", value: "remove");
        Apply(dispatcher, paths, removeExpSession);
        Assert.Equal(-16, ReadPersonal(temp, personalId: 1).ExpAddend);

        var restoreExpSession = UpdatePokemonField(dispatcher, paths, personalId: 0, field: "expYieldAll", value: "restore");
        Apply(dispatcher, paths, restoreExpSession);
        Assert.Equal(0, ReadPersonal(temp, personalId: 1).ExpAddend);

        var removeEvSession = UpdatePokemonField(dispatcher, paths, personalId: 0, field: "evYieldAll", value: "remove");
        Apply(dispatcher, paths, removeEvSession);
        Assert.Equal(0, ReadPersonal(temp, personalId: 1).EvYield!.Value.Spa);

        var restoreEvSession = UpdatePokemonField(dispatcher, paths, personalId: 0, field: "evYieldAll", value: "restore");
        Apply(dispatcher, paths, restoreEvSession);
        Assert.Equal(1, ReadPersonal(temp, personalId: 1).EvYield!.Value.Spa);

        var combinedSession = UpdatePokemonField(dispatcher, paths, personalId: 0, field: "expYieldAll", value: "remove");
        combinedSession = UpdatePokemonField(
            dispatcher,
            paths,
            combinedSession,
            personalId: 0,
            field: "evYieldAll",
            value: "remove");
        Assert.Contains(combinedSession.PendingEdits, edit => edit.Field == "expYieldAll");
        Assert.Contains(combinedSession.PendingEdits, edit => edit.Field == "evYieldAll");
        Apply(dispatcher, paths, combinedSession);
        Assert.Equal(-16, ReadPersonal(temp, personalId: 1).ExpAddend);
        Assert.Equal(0, ReadPersonal(temp, personalId: 1).EvYield!.Value.Spa);
    }

    [Theory]
    [InlineData(ProjectGameDto.Scarlet, ScarletTitleId, "Scarlet", "Violet")]
    [InlineData(ProjectGameDto.Violet, VioletTitleId, "Violet", "Scarlet")]
    public void ScarletVioletWildEncountersHideOppositeVersionTables(
        ProjectGameDto game,
        ulong titleId,
        string expectedVersion,
        string hiddenVersion)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(temp, SvDataPaths.WildEncounterArray, CreateVersionedEncounterArray());
        var paths = temp.Paths with { SelectedGame = game };

        var encounters = Dispatch<LoadEncountersWorkflowResponse>(
            CreateDispatcherWithSvCache(temp),
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-sv-versioned-encounters");

        AssertSuccess(encounters);
        Assert.Contains(encounters.Payload!.Workflow.Tables, table => table.GameVersion == "Scarlet/Violet");
        Assert.Contains(encounters.Payload.Workflow.Tables, table => table.GameVersion == expectedVersion);
        Assert.DoesNotContain(encounters.Payload.Workflow.Tables, table => table.GameVersion == hiddenVersion);
    }

    [Fact]
    public void ScarletVioletWildEncountersKeepFormLabelsVisible()
    {
        using var temp = CreateScarletVioletProject(ScarletTitleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(temp, SvDataPaths.WildEncounterArray, CreateEncounterArray(form: 1));
        var paths = temp.Paths with { SelectedGame = ProjectGameDto.Scarlet };
        var dispatcher = CreateDispatcherWithSvCache(temp);

        var encounters = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-sv-encounter-form-labels");

        AssertSuccess(encounters);
        Assert.True(
            encounters.Payload!.Workflow.Tables.Count > 0,
            string.Join("; ", encounters.Payload.Workflow.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var table = Assert.Single(encounters.Payload.Workflow.Tables);
        Assert.Equal("Bulbasaur (Form 1)", table.Slots[0].Species);

        var response = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                table.TableId,
                0,
                "form",
                "0"),
            "request-sv-encounter-form-overlay");

        AssertSuccess(response);
        var updatedTable = Assert.Single(response.Payload!.Workflow.Tables);
        Assert.Equal("Bulbasaur", updatedTable.Slots[0].Species);
    }

    private static TemporaryBridgeProject CreateScarletVioletProject(ulong titleId)
    {
        var temp = TemporaryBridgeProject.Create();
        temp.EnsureScarletVioletSupportFolder();
        temp.WriteBaseRomFsFile(
            "arc/data.trpfd",
            CreateTrinityDescriptor(
                [
                    SvDataPaths.ItemDataArray,
                    SvDataPaths.EvolutionItemConversionArray,
                    SvDataPaths.MoveDataArray,
                    SvDataPaths.PersonalArray,
                    SvDataPaths.TrainerDataArray,
                    SvDataPaths.WildEncounterArray,
                    SvDataPaths.EventAddPokemonArray,
                    SvDataPaths.EventTradeListArray,
                    SvDataPaths.EventTradePokemonArray,
                    SvDataPaths.FriendlyShopLineupDataArray,
                    SvDataPaths.ShopWazaMachineDataArray,
                    .. VisibleItemScenePaths,
                    .. TeraRaidEnemyPaths,
                    SvDataPaths.TeraRaidFixedRewardItemArray,
                    SvDataPaths.TeraRaidLotteryRewardItemArray,
                ]));
        temp.WriteBaseRomFsFile("arc/data.trpfs", "storage");
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(titleId));
        temp.WriteBaseRomFsFile(SvDataPaths.EvolutionItemConversionArray, CreateEvolutionItemConversionArray());
        return temp;
    }

    private static byte[] CreateEvolutionItemConversionArray()
    {
        var rows = new List<EvolutionItemConversion>
        {
            new(80, 1), new(81, 2), new(82, 3), new(83, 4), new(84, 5), new(85, 6),
            new(107, 7), new(108, 8), new(110, 9), new(1779, 10), new(0, 11), new(0, 12),
            new(0, 13), new(0, 14), new(229, 15), new(236, 16), new(0, 17), new(0, 18),
            new(280, 19), new(289, 20), new(290, 21), new(291, 22), new(292, 23), new(293, 24),
            new(294, 25), new(298, 26), new(299, 27), new(300, 28), new(301, 29), new(302, 30),
            new(303, 31), new(304, 32), new(305, 33), new(306, 34), new(307, 35), new(308, 36),
            new(309, 37), new(310, 38), new(311, 39), new(312, 40), new(313, 41), new(0, 42),
            new(0, 43), new(0, 44), new(0, 45), new(0, 46), new(0, 47), new(0, 48),
            new(326, 49), new(327, 50), new(644, 51), new(849, 52), new(0, 53), new(0, 54),
            new(0, 55), new(0, 56), new(0, 57), new(0, 58), new(0, 59), new(0, 60),
            new(0, 61), new(0, 62), new(0, 63), new(0, 64), new(0, 65), new(0, 66),
            new(0, 67), new(0, 68), new(0, 69), new(1103, 70), new(1104, 71), new(1109, 72),
            new(1110, 73), new(1111, 74), new(1112, 75), new(1113, 76), new(1114, 77), new(1115, 78),
            new(1116, 79), new(1117, 80), new(1253, 81), new(1254, 82), new(1582, 83), new(1592, 84),
            new(2344, 85), new(1861, 86), new(2345, 87), new(1857, 88), new(1858, 89), new(0, 90),
            new(0, 91), new(218, 92), new(109, 93), new(2403, 94), new(2404, 95), new(2402, 96),
            new(537, 111), new(325, 112), new(252, 113), new(324, 114), new(322, 115), new(323, 116),
            new(321, 117), new(235, 118), new(0, 119), new(2482, 119),
        };

        return EvolutionItemConversionTable.Write(rows);
    }

    private static ProjectBridgeDispatcher CreateDispatcherWithSvCache(TemporaryBridgeProject temp)
    {
        return new ProjectBridgeDispatcher(
            svWorkflowService: new SvWorkflowService(
                cacheManager: new SvCacheManager(Path.Combine(temp.RootPath, "sv-cache"))));
    }

    private static void WriteScarletFixtures(TemporaryBridgeProject temp)
    {
        foreach (var (relativePath, contents) in ScarletFixtures.Value.OutputFiles)
        {
            WriteSvOutput(temp, relativePath, contents);
        }

        foreach (var (relativePath, contents) in ScarletFixtures.Value.BaseFiles)
        {
            temp.WriteBaseRomFsFile(relativePath, contents);
        }
    }

    private static ScarletFixtureSet CreateScarletFixtureSet()
    {
        var outputFiles = new Dictionary<string, byte[]>
        {
            [SvDataPaths.ItemDataArray] = CreateItemDataArray(),
            [SvDataPaths.MoveDataArray] = CreateMoveDataArray(),
            [SvDataPaths.PersonalArray] = CreatePersonalArray(),
            [SvDataPaths.TrainerDataArray] = CreateTrainerDataArray(),
            [SvDataPaths.WildEncounterArray] = CreateEncounterArray(),
            [SvDataPaths.EventAddPokemonArray] = CreateEventAddPokemonArray(),
            [SvDataPaths.EventTradeListArray] = CreateEventTradeListArray(),
            [SvDataPaths.EventTradePokemonArray] = CreateEventTradePokemonArray(),
            [SvDataPaths.FriendlyShopLineupDataArray] = CreateFriendlyShopLineupDataArray(),
            [SvDataPaths.ShopWazaMachineDataArray] = CreateTechnicalMachineShopDataArray(),
            [SvDataPaths.FixedSymbolTableArray] = CreateFixedSymbolTableArray(),
            [SvDataPaths.EventBattlePokemonArray] = CreateEventBattlePokemonArray(),
            [SvDataPaths.VisibleItemScenePaldeaScarlet] = CreateVisibleItemScene(includeItem: true),
            [SvDataPaths.VisibleItemScenePaldeaViolet] = CreateVisibleItemScene(includeItem: true),
            [SvDataPaths.VisibleItemSceneKitakamiScarlet] = CreateVisibleItemScene(includeItem: false),
            [SvDataPaths.VisibleItemSceneKitakamiViolet] = CreateVisibleItemScene(includeItem: false),
            [SvDataPaths.VisibleItemSceneBlueberryScarlet] = CreateVisibleItemScene(includeItem: false),
            [SvDataPaths.VisibleItemSceneBlueberryViolet] = CreateVisibleItemScene(includeItem: false),
            [SvDataPaths.HiddenItemDataTableArray] = CreateHiddenItemDataTableArray(),
            [SvDataPaths.RummagingItemDataTableArray] = CreateRummagingItemDataTableArray(),
            [SvDataPaths.TeraRaidFixedRewardItemArray] = CreateTeraRaidFixedRewardItemArray(),
            [SvDataPaths.TeraRaidLotteryRewardItemArray] = CreateTeraRaidLotteryRewardItemArray(),
        };

        var emptyRaidArray = CreateEmptyTeraRaidEnemyArray();
        foreach (var path in TeraRaidEnemyPaths)
        {
            outputFiles[path] = path == SvDataPaths.TeraRaidEnemyPaldea5
                ? CreateTeraRaidEnemyArray()
                : emptyRaidArray;
        }

        var baseFiles = new Dictionary<string, byte[]>
        {
            [SvDataPaths.EnglishPokemonNames] = CreateTextTable(5, (1, "Bulbasaur"), (2, "Ivysaur"), (4, "Charmander")),
            [SvDataPaths.EnglishItemNames] = CreateTextTable(6, (1, "Master Ball"), (2, "TM001"), (3, "Legacy Move Record"), (4, "TM002"), (5, "TM100")),
            [SvDataPaths.EnglishMoveNames] = CreateTextTable(350, (33, "Tackle"), (36, "Take Down"), (45, "Growl"), (349, "Dragon Dance")),
            [SvDataPaths.EnglishMoveDescriptions] = CreateTextTable(350, (33, "A physical attack."), (45, "Lowers Defense.")),
            [SvDataPaths.EnglishAbilityNames] = CreateTextTable(95, (34, "Chlorophyll"), (65, "Overgrow"), (66, "Blaze"), (94, "Solar Power")),
            [SvDataPaths.EnglishPlaceNames] = CreateTextTable(2, (0, "South Province (Area Two)"), (1, "South Province (Area Four)")),
            [SvDataPaths.EnglishPlaceNameKeys] = CreateKeyTable("PLACENAME_a_w04_01", "PLACENAME_a_w05_01"),
            [SvDataPaths.EnglishTrainerNames] = CreateTextTable(1, (0, "Test Trainer")),
            [SvDataPaths.EnglishTrainerNameKeys] = CreateKeyTable("TRNAME_TEST"),
            [SvDataPaths.EnglishTrainerTypes] = CreateTextTable(1, (0, "Pokemon Trainer")),
            [SvDataPaths.EnglishTrainerTypeKeys] = CreateKeyTable("MSG_TRTYPE_TEST"),
        };

        return new ScarletFixtureSet(outputFiles, baseFiles);
    }

    private static EditSessionDto UpdateItem(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int itemId,
        string field,
        string value)
    {
        return UpdateItem(dispatcher, paths, session: null, itemId, field, value);
    }

    private static EditSessionDto UpdateItem(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        int itemId,
        string field,
        string value)
    {
        var response = Dispatch<UpdateItemFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(paths, session, itemId, field, value),
            "request-sv-item-update");

        AssertSuccess(response);
        Assert.Contains(response.Payload!.Session.PendingEdits, edit =>
            edit.Domain == "workflow.items" && edit.NewValue == value);
        return response.Payload.Session;
    }

    private static EditSessionDto UpdateMove(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int moveId,
        string field,
        string value)
    {
        return UpdateMove(dispatcher, paths, session: null, moveId, field, value);
    }

    private static EditSessionDto UpdateMove(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        int moveId,
        string field,
        string value)
    {
        var response = Dispatch<UpdateMoveFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateMoveField,
            new UpdateMoveFieldRequest(paths, session, moveId, field, value),
            "request-sv-move-update");

        AssertSuccess(response);
        return response.Payload!.Session;
    }

    private static EditSessionDto UpdatePokemonField(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int personalId,
        string field,
        string value)
    {
        return UpdatePokemonField(dispatcher, paths, session: null, personalId, field, value);
    }

    private static EditSessionDto UpdatePokemonField(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        int personalId,
        string field,
        string value)
    {
        var response = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, session, personalId, field, value),
            "request-sv-pokemon-field-update");

        AssertSuccess(response);
        return response.Payload!.Session;
    }

    private static EditSessionDto UpdatePokemonLearnset(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto session,
        int personalId,
        int slot,
        int moveId,
        int level)
    {
        var response = Dispatch<UpdatePokemonLearnsetResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonLearnset,
            new UpdatePokemonLearnsetRequest(paths, session, personalId, "upsert", slot, moveId, level),
            "request-sv-pokemon-learnset-update");

        AssertSuccess(response);
        return response.Payload!.Session;
    }

    private static EditSessionDto UpdatePokemonEvolution(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto session,
        int personalId,
        int slot,
        int level)
    {
        var response = Dispatch<UpdatePokemonEvolutionResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(
                paths,
                session,
                personalId,
                "upsert",
                slot,
                Method: 4,
                Argument: 0,
                Species: 2,
                Form: 0,
                level),
            "request-sv-pokemon-evolution-update");

        AssertSuccess(response);
        return response.Payload!.Session;
    }

    private static EditSessionDto UpdateTrainer(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int trainerId,
        int slot,
        string field,
        string value)
    {
        var response = Dispatch<UpdateTrainerFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerField,
            new UpdateTrainerFieldRequest(paths, Session: null, trainerId, slot, field, value),
            "request-sv-trainer-update");

        AssertSuccess(response);
        return response.Payload!.Session;
    }

    private static EditSessionDto UpdateShop(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        string shopId,
        int slot,
        string field,
        string value)
    {
        return UpdateShop(dispatcher, paths, session: null, shopId, slot, field, value);
    }

    private static EditSessionDto UpdateShop(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        string shopId,
        int slot,
        string field,
        string value,
        string? rowId = null)
    {
        var response = Dispatch<UpdateShopInventoryItemResponse>(
            dispatcher,
            KmCommandNames.UpdateShopInventoryItem,
            new UpdateShopInventoryItemRequest(paths, session, shopId, slot, field, value)
            {
                RowId = rowId,
            },
            "request-sv-shop-update");

        AssertSuccess(response);
        Assert.Contains(response.Payload!.Session.PendingEdits, edit =>
            edit.Domain == "workflow.shops" && edit.NewValue == value);
        return response.Payload.Session;
    }

    private static EditSessionDto UpdateEncounter(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        string tableId,
        int slot,
        string field,
        string value)
    {
        var response = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(paths, Session: null, tableId, slot, field, value),
            "request-sv-encounter-update");

        AssertSuccess(response);
        return response.Payload!.Session;
    }

    private static EditSessionDto UpdateGiftPokemon(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int giftIndex,
        string field,
        string value)
    {
        return UpdateGiftPokemon(dispatcher, paths, session: null, giftIndex, field, value);
    }

    private static EditSessionDto UpdateGiftPokemon(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        int giftIndex,
        string field,
        string value)
    {
        var response = Dispatch<UpdateGiftPokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateGiftPokemonField,
            new UpdateGiftPokemonFieldRequest(paths, session, giftIndex, field, value),
            "request-sv-gift-update");

        AssertSuccess(response);
        Assert.Contains(response.Payload!.Session.PendingEdits, edit =>
            edit.Domain == "workflow.giftPokemon" && edit.NewValue == value);
        return response.Payload.Session;
    }

    private static EditSessionDto UpdateTradePokemon(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int tradeIndex,
        string field,
        string value)
    {
        return UpdateTradePokemon(dispatcher, paths, session: null, tradeIndex, field, value);
    }

    private static EditSessionDto UpdateTradePokemon(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        int tradeIndex,
        string field,
        string value)
    {
        var response = Dispatch<UpdateTradePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonField,
            new UpdateTradePokemonFieldRequest(paths, session, tradeIndex, field, value),
            "request-sv-trade-update");

        AssertSuccess(response);
        Assert.Contains(response.Payload!.Session.PendingEdits, edit =>
            edit.Domain == "workflow.tradePokemon" && edit.NewValue == value);
        return response.Payload.Session;
    }

    private static EditSessionDto UpdatePlacement(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        string objectId,
        string field,
        string value)
    {
        return UpdatePlacement(dispatcher, paths, session: null, objectId, field, value);
    }

    private static EditSessionDto UpdatePlacement(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        string objectId,
        string field,
        string value)
    {
        var response = Dispatch<UpdatePlacementObjectFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePlacementObjectField,
            new UpdatePlacementObjectFieldRequest(paths, session, objectId, field, value),
            "request-sv-placement-update");

        AssertSuccess(response);
        return response.Payload!.Session;
    }

    private static EditSessionDto UpdateStaticEncounter(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int encounterIndex,
        string field,
        string value)
    {
        return UpdateStaticEncounter(dispatcher, paths, session: null, encounterIndex, field, value);
    }

    private static EditSessionDto UpdateStaticEncounter(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        EditSessionDto? session,
        int encounterIndex,
        string field,
        string value)
    {
        var response = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(paths, session, encounterIndex, field, value),
            "request-sv-static-encounter-update");

        AssertSuccess(response);
        Assert.Contains(response.Payload!.Session.PendingEdits, edit =>
            edit.Domain == "workflow.staticEncounters" && edit.NewValue == value);
        return response.Payload.Session;
    }

    private static void Apply(ProjectBridgeDispatcher dispatcher, ProjectPathsDto paths, EditSessionDto session)
    {
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session),
            "request-sv-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Equal(2, plan.Payload.ChangePlan.Writes.Count);
        Assert.Contains(
            plan.Payload.ChangePlan.Writes,
            write => string.Equals(write.TargetRelativePath, "romfs/arc/data.trpfd", StringComparison.Ordinal));

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, session, plan.Payload.ChangePlan),
            "request-sv-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal(2, apply.Payload.ApplyResult.WrittenFiles.Count);
        Assert.Contains("romfs/arc/data.trpfd", apply.Payload.ApplyResult.WrittenFiles);
    }

    private static void AssertDescriptorMarksLayeredOutputs(TemporaryBridgeProject temp)
    {
        var descriptorPath = Path.Combine(temp.OutputRootPath, "romfs", "arc", "data.trpfd");
        Assert.True(File.Exists(descriptorPath));

        var descriptor = KM.Formats.SV.Trinity.FileDescriptor.GetRootAsFileDescriptor(
            new ByteBuffer(File.ReadAllBytes(descriptorPath)));
        var activeHashes = Enumerable
            .Range(0, descriptor.FileHashesLength)
            .Select(descriptor.FileHashes)
            .ToHashSet();

        foreach (var path in new[]
        {
            SvDataPaths.ItemDataArray,
            SvDataPaths.MoveDataArray,
            SvDataPaths.PersonalArray,
            SvDataPaths.TrainerDataArray,
            SvDataPaths.WildEncounterArray,
            SvDataPaths.EventAddPokemonArray,
            SvDataPaths.EventTradeListArray,
            SvDataPaths.EventTradePokemonArray,
            SvDataPaths.FriendlyShopLineupDataArray,
            SvDataPaths.ShopWazaMachineDataArray,
        })
        {
            Assert.DoesNotContain(KM.Formats.SV.SvTrinityPathHasher.HashPath(path), activeHashes);
        }
    }

    private static int ReadItemPrice(TemporaryBridgeProject temp, int itemId)
    {
        return ReadItem(temp, itemId).Price;
    }

    private static global::ItemData ReadItem(TemporaryBridgeProject temp, int itemId)
    {
        var table = global::ItemDataArray.GetRootAsItemDataArray(new ByteBuffer(ReadSvOutput(temp, SvDataPaths.ItemDataArray)));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is not null && item.Value.Id == itemId)
            {
                return item.Value;
            }
        }

        throw new InvalidDataException($"Item {itemId} was not written.");
    }

    private static global::SvMoveData ReadMove(TemporaryBridgeProject temp, int moveId)
    {
        var table = global::SvMoveDataArray.GetRootAsSvMoveDataArray(new ByteBuffer(ReadSvOutput(temp, SvDataPaths.MoveDataArray)));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var move = table.Values(index);
            if (move is not null && move.Value.MoveId == moveId)
            {
                return move.Value;
            }
        }

        throw new InvalidDataException($"Move {moveId} was not written.");
    }

    private static global::personal ReadPersonal(TemporaryBridgeProject temp, int personalId)
    {
        var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(ReadSvOutput(temp, SvDataPaths.PersonalArray)));
        var personal = table.Entry(personalId);
        Assert.NotNull(personal);
        return personal.Value;
    }

    private static int ReadTrainerPokemonLevel(TemporaryBridgeProject temp, int trainerId, int slot)
    {
        return ReadTrainerPokemon(temp, trainerId, slot).Level;
    }

    private static global::GemType ReadTrainerPokemonTeraType(TemporaryBridgeProject temp, int trainerId, int slot)
    {
        return ReadTrainerPokemon(temp, trainerId, slot).GemType;
    }

    private static SvTrainerPokemonRecord CreateSvTrainerPokemon(
        int slot,
        string species,
        global::GemType teraType)
    {
        var stats = new SvTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0);
        return new SvTrainerPokemonRecord(
            slot,
            SpeciesId: slot + 1,
            species,
            Form: 0,
            Level: 50,
            HeldItemId: 0,
            HeldItem: null,
            MoveIds: [0, 0, 0, 0],
            Moves: ["None", "None", "None", "None"],
            Gender: 0,
            GenderLabel: "Random",
            Ability: 0,
            AbilityLabel: "Random 1/2",
            Nature: 0,
            NatureLabel: "Default (game behavior)",
            stats,
            stats,
            Shiny: false,
            TeraType: (int)teraType,
            TeraTypeLabel: SvTrainersWorkflowService.FormatTeraType(teraType));
    }

    private static global::PokeDataBattle ReadTrainerPokemon(TemporaryBridgeProject temp, int trainerId, int slot)
    {
        var pokemon = ReadOptionalTrainerPokemon(temp, trainerId, slot);
        Assert.NotNull(pokemon);
        return pokemon.Value;
    }

    private static global::PokeDataBattle? ReadOptionalTrainerPokemon(TemporaryBridgeProject temp, int trainerId, int slot)
    {
        var table = global::trainer.TrdataMainArray.GetRootAsTrdataMainArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.TrainerDataArray)));
        var trainer = table.Values(trainerId);
        Assert.NotNull(trainer);
        return slot switch
        {
            0 => trainer.Value.Poke1,
            1 => trainer.Value.Poke2,
            2 => trainer.Value.Poke3,
            3 => trainer.Value.Poke4,
            4 => trainer.Value.Poke5,
            5 => trainer.Value.Poke6,
            _ => null,
        };
    }

    private static int ReadEncounterMinLevel(TemporaryBridgeProject temp, int index)
    {
        var table = global::EncountPokeDataArray.GetRootAsEncountPokeDataArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.WildEncounterArray)));
        var encounter = table.Values(index);
        Assert.NotNull(encounter);
        return encounter.Value.Minlevel;
    }

    private static global::EventAddPokemon ReadGiftPokemon(TemporaryBridgeProject temp, int giftIndex)
    {
        var table = global::EventAddPokemonArray.GetRootAsEventAddPokemonArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.EventAddPokemonArray)));
        var gift = table.Values(giftIndex);
        Assert.NotNull(gift);
        return gift.Value;
    }

    private static global::EventTradeList ReadTradeList(TemporaryBridgeProject temp, int index)
    {
        var table = global::EventTradeListArray.GetRootAsEventTradeListArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.EventTradeListArray)));
        var trade = table.Values(index);
        Assert.NotNull(trade);
        return trade.Value;
    }

    private static global::EventTradePokemon ReadTradePokemon(TemporaryBridgeProject temp, int tradeIndex)
    {
        var table = global::EventTradePokemonArray.GetRootAsEventTradePokemonArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.EventTradePokemonArray)));
        var trade = table.Values(tradeIndex);
        Assert.NotNull(trade);
        return trade.Value;
    }

    private static int ReadFriendlyShopItemId(TemporaryBridgeProject temp, string lineupId, int slot)
    {
        var rows = KM.SV.Shops.SvShopsWorkflowService
            .ReadFriendlyRows(ReadSvOutput(temp, SvDataPaths.FriendlyShopLineupDataArray))
            .Where(row => string.Equals(row.LineupId, lineupId, StringComparison.Ordinal))
            .OrderBy(row => row.SortNum)
            .ThenBy(row => row.SourceIndex)
            .ToArray();

        return rows[slot - 1].ItemId;
    }

    private static KM.SV.Shops.SvShopsWorkflowService.TechnicalMachineRow ReadTechnicalMachineRow(
        TemporaryBridgeProject temp,
        AddRegion region,
        int slot)
    {
        var rows = KM.SV.Shops.SvShopsWorkflowService
            .ReadTechnicalMachineRows(ReadSvOutput(temp, SvDataPaths.ShopWazaMachineDataArray))
            .Where(row => row.Region == region)
            .OrderBy(row => row.WazaItemId)
            .ThenBy(row => row.SourceIndex)
            .ToArray();

        return rows[slot - 1];
    }

    private static global::pml.common.DevID ReadTeraRaidBossSpecies(TemporaryBridgeProject temp)
    {
        var table = global::RaidEnemyTable01Array.GetRootAsRaidEnemyTable01Array(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.TeraRaidEnemyPaldea5)));
        var row = table.Values(0);
        Assert.NotNull(row);
        var info = row.Value.RaidEnemyInfo;
        Assert.NotNull(info);
        var boss = info.Value.BossPokePara;
        Assert.NotNull(boss);
        return boss.Value.DevId;
    }

    private static int ReadTeraRaidFixedRewardCount(TemporaryBridgeProject temp)
    {
        var table = global::RaidFixedRewardItemArray.GetRootAsRaidFixedRewardItemArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.TeraRaidFixedRewardItemArray)));
        var row = table.Values(0);
        Assert.NotNull(row);
        var reward = row.Value.RewardItem00;
        Assert.NotNull(reward);
        return reward.Value.Num;
    }

    private static byte[] CreateItemDataArray(
        bool masterBallEvolutionItem = false,
        bool ultraBallEvolutionItem = false,
        bool includeTinyMushroom = false,
        bool fireStoneEvolutionItem = false,
        bool includeMaliciousArmor = false,
        bool includeTwistedSpoon = false)
    {
        var builder = new FlatBufferBuilder(1024);
        var icon = builder.CreateString("item_0001");
        var masterBall = global::ItemData.CreateItemData(
            builder,
            Id: 1,
            IconNameOffset: icon,
            Price: 100,
            BP: 2,
            ThrowPower: 10,
            SortNum: 1,
            ItemGroup: global::ItemGroup.ITEMGROUP_BALL,
            GroupID: 1,
            FieldPocket: global::FieldPocket.FPOCKET_BALL,
            BattleFunctionType: global::BattleFunctionType.BTLFUNC_BALL,
            WorkEvolutional: masterBallEvolutionItem ? 1 : 0,
            SetToPoke: true);
        var tmIcon = builder.CreateString("item_tm_001");
        var tm001 = global::ItemData.CreateItemData(
            builder,
            Id: 2,
            ItemType: global::ItemType.ITEMTYPE_WAZA,
            IconNameOffset: tmIcon,
            Price: 800,
            MachineWaza: (global::pml.common.WazaID)36,
            SortNum: 2,
            ItemGroup: global::ItemGroup.ITEMGROUP_WAZA_MACHINE,
            GroupID: 1,
            FieldPocket: global::FieldPocket.FPOCKET_WAZA,
            FieldFunctionType: global::FieldFunctionType.FIELDFUNC_WAZA,
            WorkEvolutional: ultraBallEvolutionItem ? 1 : 0,
            SetToPoke: true);
        var legacyMoveIcon = builder.CreateString("item_legacy_move");
        var legacyMoveItem = global::ItemData.CreateItemData(
            builder,
            Id: 3,
            ItemType: global::ItemType.ITEMTYPE_NORMAL,
            IconNameOffset: legacyMoveIcon,
            MachineWaza: (global::pml.common.WazaID)33,
            SortNum: 3,
            GroupID: 1230,
            FieldPocket: global::FieldPocket.FPOCKET_OTHER,
            FieldFunctionType: global::FieldFunctionType.FIELDFUNC_NONE);
        var tm002Icon = builder.CreateString("item_tm_002");
        var tm002 = global::ItemData.CreateItemData(
            builder,
            Id: 4,
            ItemType: global::ItemType.ITEMTYPE_WAZA,
            IconNameOffset: tm002Icon,
            Price: 800,
            MachineWaza: (global::pml.common.WazaID)45,
            SortNum: 4,
            ItemGroup: global::ItemGroup.ITEMGROUP_WAZA_MACHINE,
            GroupID: 2,
            FieldPocket: global::FieldPocket.FPOCKET_WAZA,
            FieldFunctionType: global::FieldFunctionType.FIELDFUNC_WAZA,
            SetToPoke: true);
        var tm100Icon = builder.CreateString("item_tm_100");
        var tm100 = global::ItemData.CreateItemData(
            builder,
            Id: 5,
            ItemType: global::ItemType.ITEMTYPE_WAZA,
            IconNameOffset: tm100Icon,
            Price: 800,
            MachineWaza: (global::pml.common.WazaID)349,
            SortNum: 100,
            ItemGroup: global::ItemGroup.ITEMGROUP_WAZA_MACHINE,
            GroupID: 0,
            FieldPocket: global::FieldPocket.FPOCKET_WAZA,
            FieldFunctionType: global::FieldFunctionType.FIELDFUNC_WAZA,
            SetToPoke: true);
        var rows = new List<Offset<global::ItemData>> { masterBall, tm001, legacyMoveItem, tm002, tm100 };
        if (includeTinyMushroom)
        {
            var tinyMushroomIcon = builder.CreateString("item_0086");
            rows.Add(global::ItemData.CreateItemData(
                builder,
                Id: 86,
                ItemType: global::ItemType.ITEMTYPE_NORMAL,
                IconNameOffset: tinyMushroomIcon,
                Price: 500,
                SortNum: 86,
                ItemGroup: global::ItemGroup.ITEMGROUP_NONE,
                FieldPocket: global::FieldPocket.FPOCKET_OTHER,
                FieldFunctionType: global::FieldFunctionType.FIELDFUNC_NONE));
        }

        if (fireStoneEvolutionItem)
        {
            var fireStoneIcon = builder.CreateString("item_0082");
            rows.Add(global::ItemData.CreateItemData(
                builder,
                Id: 82,
                ItemType: global::ItemType.ITEMTYPE_NORMAL,
                IconNameOffset: fireStoneIcon,
                Price: 3000,
                SortNum: 82,
                ItemGroup: global::ItemGroup.ITEMGROUP_NONE,
                FieldPocket: global::FieldPocket.FPOCKET_OTHER,
                FieldFunctionType: global::FieldFunctionType.FIELDFUNC_NONE,
                WorkEvolutional: 1,
                SetToPoke: true));
        }

        if (includeMaliciousArmor)
        {
            var maliciousArmorIcon = builder.CreateString("item_1861");
            rows.Add(global::ItemData.CreateItemData(
                builder,
                Id: 1861,
                ItemType: global::ItemType.ITEMTYPE_NORMAL,
                IconNameOffset: maliciousArmorIcon,
                Price: 3000,
                SortNum: 1861,
                ItemGroup: global::ItemGroup.ITEMGROUP_NONE,
                FieldPocket: global::FieldPocket.FPOCKET_OTHER,
                FieldFunctionType: global::FieldFunctionType.FIELDFUNC_NONE,
                WorkEvolutional: 1,
                SetToPoke: true));
        }

        if (includeTwistedSpoon)
        {
            var twistedSpoonIcon = builder.CreateString("item_0248");
            rows.Add(global::ItemData.CreateItemData(
                builder,
                Id: 248,
                ItemType: global::ItemType.ITEMTYPE_NORMAL,
                IconNameOffset: twistedSpoonIcon,
                Price: 3000,
                SortNum: 248,
                ItemGroup: global::ItemGroup.ITEMGROUP_NONE,
                FieldPocket: global::FieldPocket.FPOCKET_OTHER,
                FieldFunctionType: global::FieldFunctionType.FIELDFUNC_NONE,
                WorkEvolutional: 1,
                SetToPoke: true));
        }

        var vector = global::ItemDataArray.CreateValuesVector(builder, rows.ToArray());
        var root = global::ItemDataArray.CreateItemDataArray(builder, vector);
        global::ItemDataArray.FinishItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateUngroupedTechnicalMachineItemDataArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var icon = builder.CreateString("item_2175");
        var tm115 = global::ItemData.CreateItemData(
            builder,
            Id: 2175,
            ItemType: global::ItemType.ITEMTYPE_WAZA,
            IconNameOffset: icon,
            Price: 32000,
            BP: 40,
            MachineWaza: (global::pml.common.WazaID)406,
            SortNum: 115,
            ItemGroup: global::ItemGroup.ITEMGROUP_NONE,
            GroupID: 0,
            FieldPocket: global::FieldPocket.FPOCKET_WAZA,
            FieldFunctionType: global::FieldFunctionType.FIELDFUNC_WAZA,
            SetToPoke: true);
        var vector = global::ItemDataArray.CreateValuesVector(builder, [tm115]);
        var root = global::ItemDataArray.CreateItemDataArray(builder, vector);
        global::ItemDataArray.FinishItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateFriendlyShopLineupDataArray(
        int firstSort = 0,
        int secondSort = 1,
        bool includeUnrelatedRow = false)
    {
        var builder = new FlatBufferBuilder(1024);
        var lineupId = builder.CreateString("shop_00_lineup");
        var rows = new List<Offset<global::LineupData>>
        {
            global::LineupData.CreateLineupData(
                builder,
                lineupId,
                sortnum: firstSort,
                item: (ItemID)1,
                itemCondkind: CondEnum.NONE,
                itemCondvalueOffset: default,
                gymBadgeNum: 0),
            global::LineupData.CreateLineupData(
                builder,
                lineupId,
                sortnum: secondSort,
                item: (ItemID)2,
                itemCondkind: CondEnum.GYMBADGENUM,
                itemCondvalueOffset: default,
                gymBadgeNum: 1),
        };

        if (includeUnrelatedRow)
        {
            var unrelatedLineupId = builder.CreateString("shop_99_lineup");
            rows.Add(global::LineupData.CreateLineupData(
                builder,
                unrelatedLineupId,
                sortnum: 7,
                item: (ItemID)1,
                itemCondkind: CondEnum.SCENARIO,
                itemCondvalueOffset: default,
                gymBadgeNum: 0));
        }

        var vector = global::LineupDataArray.CreateValuesVector(builder, rows.ToArray());
        var root = global::LineupDataArray.CreateLineupDataArray(builder, vector);
        global::LineupDataArray.FinishLineupDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateTechnicalMachineShopDataArray(bool includeSecondRow = false)
    {
        var builder = new FlatBufferBuilder(1024);
        var firstRow = global::ShopWazamachineData.CreateShopWazamachineData(
            builder,
            wazaNo: 36,
            wazaItemID: (ItemID)2,
            lp: 800,
            cond: CondEnum.NONE,
            condValueOffset: default,
            item01: (ItemID)1,
            itemNum01: 1,
            devNo01: 1,
            addRegion: AddRegion.TITAN);
        var secondRow = global::ShopWazamachineData.CreateShopWazamachineData(
            builder,
            wazaNo: 45,
            wazaItemID: (ItemID)4,
            lp: 1_200,
            cond: CondEnum.SCENARIO,
            condValueOffset: default,
            item01: (ItemID)1,
            itemNum01: 2,
            devNo01: 2,
            addRegion: AddRegion.TITAN);
        var rows = includeSecondRow ? new[] { firstRow, secondRow } : [firstRow];
        var vector = global::ShopWazamachineDataArray.CreateValuesVector(builder, rows);
        var root = global::ShopWazamachineDataArray.CreateShopWazamachineDataArray(builder, vector);
        global::ShopWazamachineDataArray.FinishShopWazamachineDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateMoveDataArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var tackle = CreateMove(builder, moveId: 33, power: 40, pp: 35, makesContact: true);
        var growl = CreateMove(
            builder,
            moveId: 45,
            power: 0,
            pp: 40,
            makesContact: false,
            category: 0,
            stat1: 2,
            stat1Stage: -1,
            stat1Chance: 100,
            stat2: -1);
        var vector = global::SvMoveDataArray.CreateValuesVector(builder, [tackle, growl]);
        var root = global::SvMoveDataArray.CreateSvMoveDataArray(builder, vector);
        global::SvMoveDataArray.FinishSvMoveDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateAncientPowerMoveDataArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var ancientPower = CreateMove(
            builder,
            moveId: 246,
            power: 60,
            pp: 5,
            makesContact: false,
            type: 5,
            category: 2,
            stat1: 9,
            stat1Stage: 1,
            stat1Chance: 10);
        var vector = global::SvMoveDataArray.CreateValuesVector(builder, [ancientPower]);
        var root = global::SvMoveDataArray.CreateSvMoveDataArray(builder, vector);
        global::SvMoveDataArray.FinishSvMoveDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<global::SvMoveData> CreateMove(
        FlatBufferBuilder builder,
        ushort moveId,
        byte power,
        byte pp,
        bool makesContact,
        byte type = 0,
        byte category = 1,
        sbyte stat1 = 0,
        sbyte stat2 = 0,
        sbyte stat3 = 0,
        sbyte stat1Stage = 0,
        sbyte stat2Stage = 0,
        sbyte stat3Stage = 0,
        byte stat1Chance = 0,
        byte stat2Chance = 0,
        byte stat3Chance = 0)
    {
        global::SvMoveData.StartSvMoveData(builder);
        global::SvMoveData.AddStatChanges(
            builder,
            global::SvMoveStatChanges.CreateSvMoveStatChanges(
                builder,
                stat1,
                stat1Stage,
                stat1Chance,
                stat2,
                stat2Stage,
                stat2Chance,
                stat3,
                stat3Stage,
                stat3Chance));
        global::SvMoveData.AddRawTarget(builder, 3);
        global::SvMoveData.AddInflict(
            builder,
            global::SvMoveInflict.CreateSvMoveInflict(builder, Condition: 0, Chance: 0, TurnMode: 0, TurnMin: 0, TurnMax: 0));
        global::SvMoveData.AddPp(builder, pp);
        global::SvMoveData.AddAccuracy(builder, 100);
        global::SvMoveData.AddPower(builder, power);
        global::SvMoveData.AddCategory(builder, category);
        global::SvMoveData.AddType(builder, type);
        global::SvMoveData.AddCanUseMove(builder, true);
        global::SvMoveData.AddMoveId(builder, moveId);
        global::SvMoveData.AddFlagMakesContact(builder, makesContact);
        return global::SvMoveData.EndSvMoveData(builder);
    }

    private static byte[] CreatePersonalArray(
        ushort evolutionCondition = 4,
        ushort evolutionParameter = 0,
        ushort evolutionSpecies = 2,
        byte evolutionForm = 0,
        IReadOnlyList<ushort>? eggMoves = null,
        IReadOnlyList<ushort>? reminderMoves = null)
    {
        var builder = new FlatBufferBuilder(2048);
        var empty = CreatePersonal(builder, species: 0, hp: 0, level: 0, evolutionLevel: 0);
        var bulbasaur = CreatePersonal(
            builder,
            species: 1,
            hp: 45,
            level: 1,
            evolutionLevel: 16,
            evolutionCondition: evolutionCondition,
            evolutionParameter: evolutionParameter,
            evolutionSpecies: evolutionSpecies,
            evolutionForm: evolutionForm,
            eggMoves: eggMoves,
            reminderMoves: reminderMoves);
        var charmander = CreatePersonal(
            builder,
            species: 4,
            hp: 39,
            level: 1,
            evolutionLevel: 16,
            learnedMoves: [(Move: (ushort)33, Level: 1)],
            ability1: 66,
            ability2: 66,
            hiddenAbility: 94,
            evolutionCondition: evolutionCondition,
            evolutionParameter: evolutionParameter,
            evolutionSpecies: evolutionSpecies,
            evolutionForm: evolutionForm);
        var vector = global::personal_table.CreateEntryVector(builder, [empty, bulbasaur, charmander]);
        var root = global::personal_table.Createpersonal_table(builder, vector);
        global::personal_table.Finishpersonal_tableBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreatePersonalArrayWithLevelupMoves(params (ushort Move, ushort Level)[] levelupMoves)
    {
        var builder = new FlatBufferBuilder(2048);
        var empty = CreatePersonal(builder, species: 0, hp: 0, level: 0, evolutionLevel: 0);
        var bulbasaur = CreatePersonal(
            builder,
            species: 1,
            hp: 45,
            level: 1,
            evolutionLevel: 16,
            levelupMoves);
        var vector = global::personal_table.CreateEntryVector(builder, [empty, bulbasaur]);
        var root = global::personal_table.Createpersonal_table(builder, vector);
        global::personal_table.Finishpersonal_tableBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<global::personal> CreatePersonal(
        FlatBufferBuilder builder,
        ushort species,
        byte hp,
        ushort level,
        ushort evolutionLevel,
        IReadOnlyList<(ushort Move, ushort Level)>? learnedMoves = null,
        ushort ability1 = 65,
        ushort ability2 = 65,
        ushort hiddenAbility = 34,
        ushort evolutionCondition = 4,
        ushort evolutionParameter = 0,
        ushort evolutionSpecies = 2,
        byte evolutionForm = 0,
        IReadOnlyList<ushort>? eggMoves = null,
        IReadOnlyList<ushort>? reminderMoves = null)
    {
        var tmMoves = global::personal.CreateTmMovesVector(builder, species == 0 ? [] : [(ushort)36]);
        var eggMoveVector = global::personal.CreateEggMovesVector(builder, eggMoves?.ToArray() ?? []);
        var reminderMoveVector = global::personal.CreateReminderMovesVector(builder, reminderMoves?.ToArray() ?? []);

        IReadOnlyList<(ushort Move, ushort Level)> moves = species == 0
            ? Array.Empty<(ushort Move, ushort Level)>()
            : learnedMoves is { Count: > 0 }
                ? learnedMoves
                : [(Move: (ushort)33, Level: level)];
        global::personal.StartLevelupMovesVector(builder, moves.Count);
        for (var index = moves.Count - 1; index >= 0; index--)
        {
            var learnedMove = moves[index];
            global::levelup_move_data.Createlevelup_move_data(builder, Move: learnedMove.Move, Level: learnedMove.Level);
        }

        var levelupMoves = builder.EndVector();

        global::personal.StartEvolutionsVector(builder, species == 0 ? 0 : 1);
        if (species != 0)
        {
            global::evo_data.Createevo_data(
                builder,
                Level: evolutionLevel,
                Condition: evolutionCondition,
                Parameter: evolutionParameter,
                Reserved3: 0,
                Reserved4: 0,
                Reserved5: 0,
                Species: evolutionSpecies,
                Form: evolutionForm);
        }

        var evolutions = builder.EndVector();

        global::personal.Startpersonal(builder);
        global::personal.AddLevelupMoves(builder, levelupMoves);
        global::personal.AddReminderMoves(builder, reminderMoveVector);
        global::personal.AddEggMoves(builder, eggMoveVector);
        global::personal.AddTmMoves(builder, tmMoves);
        global::personal.AddEvolutions(builder, evolutions);
        global::personal.AddBaseStats(builder, global::stat_info.Createstat_info(builder, hp, 49, 49, 65, 65, 45));
        global::personal.AddEvYield(builder, global::stat_info.Createstat_info(builder, 0, 0, 0, 1, 0, 0));
        global::personal.AddEggHatch(builder, global::egg_hatch_info.Createegg_hatch_info(builder, species, 0, 0, 0));
        global::personal.AddGender(builder, global::gender_info.Creategender_info(builder, 0, 31));
        global::personal.AddCatchRate(builder, 45);
        global::personal.AddXpGrowth(builder, 3);
        global::personal.AddAbilityHidden(builder, hiddenAbility);
        global::personal.AddAbility2(builder, ability2);
        global::personal.AddAbility1(builder, ability1);
        global::personal.AddType2(builder, 3);
        global::personal.AddType1(builder, 11);
        global::personal.AddPaldeaDex(builder, global::dex_data.Createdex_data(builder, species, 0));
        global::personal.AddIsPresent(builder, species != 0);
        global::personal.AddSpecies(builder, global::species_info.Createspecies_info(builder, species, 0, species, 5, 1, 7, 69, 0, 0, 0));
        return global::personal.Endpersonal(builder);
    }

    private static byte[] CreateTrainerDataArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var move = global::WazaSet.CreateWazaSet(builder, (global::pml.common.WazaID)33, pointUp: 0);
        var ivs = global::ParamSet.CreateParamSet(builder, 31, 31, 31, 31, 31, 31);
        var evs = global::ParamSet.CreateParamSet(builder, 0, 0, 0, 0, 0, 0);
        var pokemon = global::PokeDataBattle.CreatePokeDataBattle(
            builder,
            devId: (global::pml.common.DevID)1,
            level: 5,
            waza1Offset: move,
            gemType: global::GemType.NORMAL,
            talentValueOffset: ivs,
            effortValueOffset: evs);
        var trainerId = builder.CreateString("tr_0000");
        var trainerName = builder.CreateString("TRNAME_TEST");
        var trainerType = builder.CreateString("MSG_TRTYPE_TEST");
        var trainer = global::trainer.TrdataMain.CreateTrdataMain(
            builder,
            tridOffset: trainerId,
            trNameLabelOffset: trainerName,
            trainerTypeOffset: trainerType,
            moneyRate: 1,
            poke1Offset: pokemon,
            aiBasic: true);
        var vector = global::trainer.TrdataMainArray.CreateValuesVector(builder, [trainer]);
        var root = global::trainer.TrdataMainArray.CreateTrdataMainArray(builder, vector);
        global::trainer.TrdataMainArray.FinishTrdataMainArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateEncounterArray(sbyte form = 0)
    {
        var builder = new FlatBufferBuilder(2048);
        var encounter = CreateEncounter(
            builder,
            areaText: "4,5",
            species: 1,
            form,
            minLevel: 5,
            maxLevel: 12,
            lotValue: 40,
            scarlet: true,
            violet: true);

        var vector = global::EncountPokeDataArray.CreateValuesVector(builder, [encounter]);
        var root = global::EncountPokeDataArray.CreateEncountPokeDataArray(builder, vector);
        global::EncountPokeDataArray.FinishEncountPokeDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateEventAddPokemonArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var label = builder.CreateString("test_gift_bulbasaur");
        var pokemon = global::PokeDataFull.CreatePokeDataFull(
            builder,
            devId: (global::pml.common.DevID)1,
            item: global::ItemID.ITEMID_NONE,
            level: 5,
            sex: global::SexType.DEFAULT,
            seikaku: global::SeikakuType.DEFAULT,
            tokusei: global::TokuseiType.SET_1,
            rareType: global::RareType.NO_RARE,
            talentType: global::TalentType.V_NUM,
            talentVnum: 3,
            friendship: 50,
            wazaType: global::WazaType.DEFAULT,
            ballId: global::BallType.MONSUTAABOORU,
            gemType: global::GemType.NORMAL,
            wazaConfirmLevel: 5);
        var gift = CreateEventAddPokemonWithRealLayout(
            builder,
            label,
            pokemon,
            pokedexRegistration: true);
        var vector = global::EventAddPokemonArray.CreateValuesVector(builder, [gift]);
        var root = global::EventAddPokemonArray.CreateEventAddPokemonArray(builder, vector);
        global::EventAddPokemonArray.FinishEventAddPokemonArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<global::EventAddPokemon> CreateEventAddPokemonWithRealLayout(
        FlatBufferBuilder builder,
        StringOffset labelOffset,
        Offset<global::PokeDataFull> pokeDataOffset,
        bool pokedexRegistration)
    {
        builder.StartTable(3);
        builder.AddBool(2, pokedexRegistration, false);
        builder.AddOffset(1, pokeDataOffset.Value, 0);
        builder.AddOffset(0, labelOffset.Value, 0);
        return new Offset<global::EventAddPokemon>(builder.EndTable());
    }

    private static byte[] CreateEventTradeListArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var label = builder.CreateString("test_trade_request");
        var receivePoke = builder.CreateString("test_trade_bulbasaur");
        var trade = global::EventTradeList.CreateEventTradeList(
            builder,
            label,
            receivePoke,
            (global::pml.common.DevID)2,
            sendPokeFormId: -1);
        var vector = global::EventTradeListArray.CreateValuesVector(builder, [trade]);
        var root = global::EventTradeListArray.CreateEventTradeListArray(builder, vector);
        global::EventTradeListArray.FinishEventTradeListArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateEventTradePokemonArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var label = builder.CreateString("test_trade_bulbasaur");
        var pokemon = global::PokeDataTrade.CreatePokeDataTrade(
            builder,
            devId: (global::pml.common.DevID)1,
            level: 15,
            sex: global::SexType.DEFAULT,
            tokusei: global::TokuseiType.SET_1,
            gemType: global::GemType.NORMAL,
            rareType: global::RareType.NO_RARE,
            scaleType: global::SizeType.VALUE,
            scaleValue: 123,
            talentType: global::TalentType.V_NUM,
            talentVnum: 2,
            item: (global::ItemID)1,
            seikaku: global::SeikakuType.DEFAULT,
            wazaType: global::WazaType.DEFAULT,
            ballId: global::BallType.MONSUTAABOORU,
            trainerId: 123456,
            parentSex: global::SexType.FEMALE);
        var trade = global::EventTradePokemon.CreateEventTradePokemon(builder, label, pokemon);
        var vector = global::EventTradePokemonArray.CreateValuesVector(builder, [trade]);
        var root = global::EventTradePokemonArray.CreateEventTradePokemonArray(builder, vector);
        global::EventTradePokemonArray.FinishEventTradePokemonArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateFixedSymbolTableArray(global::pml.common.DevID species = global::pml.common.DevID.DEV_HUSIGIDANE)
    {
        var builder = new FlatBufferBuilder(2048);
        var tableKey = builder.CreateString("ai_area01_01");
        var pokeData = global::PokeDataSymbol.CreatePokeDataSymbol(
            builder,
            devId: species,
            level: 5,
            rareType: global::RareType.NO_RARE,
            tokuseiIndex: global::TokuseiType.SET_1);
        var fixedSymbol = FixedSymbolTable.CreateFixedSymbolTable(
            builder,
            tableKeyOffset: tableKey,
            pokeDataSymbolOffset: pokeData);

        var vector = FixedSymbolTableArray.CreateValuesVector(builder, [fixedSymbol]);
        var root = FixedSymbolTableArray.CreateFixedSymbolTableArray(builder, vector);
        FixedSymbolTableArray.FinishFixedSymbolTableArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateEventBattlePokemonArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var label = builder.CreateString("1055_multi_01");
        var pokeData = global::PokeDataEventBattle.CreatePokeDataEventBattle(
            builder,
            devId: (global::pml.common.DevID)1,
            level: 10,
            rareType: global::RareType.RARE,
            tokusei: global::TokuseiType.SET_3,
            item: (global::ItemID)1,
            dropItem: (global::ItemID)2,
            dropItemNum: 1);
        var coinSymbol = EventBattlePokemon.CreateEventBattlePokemon(
            builder,
            labelOffset: label,
            pokeDataOffset: pokeData);

        var vector = EventBattlePokemonArray.CreateValuesVector(builder, [coinSymbol]);
        var root = EventBattlePokemonArray.CreateEventBattlePokemonArray(builder, vector);
        EventBattlePokemonArray.FinishEventBattlePokemonArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateHiddenItemDataTableArray(
        string tableId = "1001",
        int firstItemId = 1,
        int firstEmergePercent = 200,
        int firstDropCount = 1)
    {
        var builder = new FlatBufferBuilder(2048);
        var tableIdOffset = builder.CreateString(tableId);
        var item1 = HiddenItemDataTableInfo.CreateHiddenItemDataTableInfo(
            builder,
            itemId: firstItemId,
            emergePercent: firstEmergePercent,
            dropCount: firstDropCount);
        var item2 = HiddenItemDataTableInfo.CreateHiddenItemDataTableInfo(
            builder,
            itemId: 2,
            emergePercent: 50,
            dropCount: 2);
        var hiddenItem = HiddenItemDataTable.CreateHiddenItemDataTable(builder, tableIdOffset, [item1, item2]);

        var vector = HiddenItemDataTableArray.CreateValuesVector(builder, [hiddenItem]);
        var root = HiddenItemDataTableArray.CreateHiddenItemDataTableArray(builder, vector);
        HiddenItemDataTableArray.FinishHiddenItemDataTableArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateRummagingItemDataTableArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var rummaging = RummagingItemDataTable.CreateRummagingItemDataTable(
            builder,
            category: RummagingCategory.Bush,
            pattern: RummagingPattern.Normal,
            item00: 1,
            item01: 2);

        var vector = RummagingItemDataTableArray.CreateValuesVector(builder, [rummaging]);
        var root = RummagingItemDataTableArray.CreateRummagingItemDataTableArray(builder, vector);
        RummagingItemDataTableArray.FinishRummagingItemDataTableArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static void WriteTeraRaidFixtures(TemporaryBridgeProject temp)
    {
        var emptyRaidArray = CreateEmptyTeraRaidEnemyArray();
        foreach (var path in TeraRaidEnemyPaths)
        {
            WriteSvOutput(
                temp,
                path,
                path == SvDataPaths.TeraRaidEnemyPaldea5 ? CreateTeraRaidEnemyArray() : emptyRaidArray);
        }

        WriteSvOutput(temp, SvDataPaths.TeraRaidFixedRewardItemArray, CreateTeraRaidFixedRewardItemArray());
        WriteSvOutput(temp, SvDataPaths.TeraRaidLotteryRewardItemArray, CreateTeraRaidLotteryRewardItemArray());
    }

    private static byte[] CreateEmptyTeraRaidEnemyArray()
    {
        var builder = new FlatBufferBuilder(128);
        var vector = global::RaidEnemyTable01Array.CreateValuesVector(builder, []);
        var root = global::RaidEnemyTable01Array.CreateRaidEnemyTable01Array(builder, vector);
        global::RaidEnemyTable01Array.FinishRaidEnemyTable01ArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateTeraRaidEnemyArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var move = global::WazaSet.CreateWazaSet(builder, (global::pml.common.WazaID)33, pointUp: 0);
        var ivs = global::ParamSet.CreateParamSet(builder, 31, 31, 31, 31, 31, 31);
        var bossPokemon = global::PokeDataBattle.CreatePokeDataBattle(
            builder,
            devId: (global::pml.common.DevID)1,
            level: 30,
            item: global::ItemID.ITEMID_MASUTAABOORU,
            ballId: global::BallType.MONSUTAABOORU,
            wazaType: global::WazaType.MANUAL,
            waza1Offset: move,
            gemType: global::GemType.KUSA,
            tokusei: global::TokuseiType.SET_1,
            talentType: global::TalentType.VALUE,
            talentValueOffset: ivs,
            rareType: global::RareType.NO_RARE);
        var size = global::RaidBossSizeData.CreateRaidBossSizeData(
            builder,
            scaleType: global::SizeType.VALUE,
            scaleValue: 128);
        var boss = global::RaidBossData.CreateRaidBossData(
            builder,
            hpCoef: 20,
            powerChargeTrigerHp: 80,
            powerChargeTrigerTime: 60,
            doubleActionTrigerHp: 50,
            doubleActionTrigerTime: 30,
            doubleActionRate: 10);
        var raidInfo = global::RaidEnemyInfo.CreateRaidEnemyInfo(
            builder,
            romVer: global::RaidRomType.BOTH,
            no: 25,
            difficulty: 5,
            rate: 10,
            dropTableFix: TeraRaidFixedRewardTableHash,
            dropTableRandom: TeraRaidLotteryRewardTableHash,
            captureRate: 45,
            captureLv: 30,
            bossPokeParaOffset: bossPokemon,
            bossPokeSizeOffset: size,
            bossDescOffset: boss);
        var raid = global::RaidEnemyTable05.CreateRaidEnemyTable05(builder, raidInfo);
        var vector = global::RaidEnemyTable05Array.CreateValuesVector(builder, [raid]);
        var root = global::RaidEnemyTable05Array.CreateRaidEnemyTable05Array(builder, vector);
        global::RaidEnemyTable05Array.FinishRaidEnemyTable05ArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateTeraRaidFixedRewardItemArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var reward = global::RaidFixedRewardItemInfo.CreateRaidFixedRewardItemInfo(
            builder,
            category: global::RaidRewardItemCategoryType.ITEM,
            subject_type: global::RaidRewardItemSubjectType.ALL,
            itemID: global::ItemID.ITEMID_MASUTAABOORU,
            num: 2);
        var table = global::RaidFixedRewardItem.CreateRaidFixedRewardItem(
            builder,
            table_name: TeraRaidFixedRewardTableHash,
            reward_item_00Offset: reward);
        var vector = global::RaidFixedRewardItemArray.CreateValuesVector(builder, [table]);
        var root = global::RaidFixedRewardItemArray.CreateRaidFixedRewardItemArray(builder, vector);
        global::RaidFixedRewardItemArray.FinishRaidFixedRewardItemArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateTeraRaidLotteryRewardItemArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var reward = global::RaidLotteryRewardItemInfo.CreateRaidLotteryRewardItemInfo(
            builder,
            category: global::RaidRewardItemCategoryType.ITEM,
            itemID: global::ItemID.ITEMID_HAIPAABOORU,
            num: 1,
            rate: 25,
            rare_item_flag: true);
        var table = global::RaidLotteryRewardItem.CreateRaidLotteryRewardItem(
            builder,
            table_name: TeraRaidLotteryRewardTableHash,
            reward_item_00Offset: reward);
        var vector = global::RaidLotteryRewardItemArray.CreateValuesVector(builder, [table]);
        var root = global::RaidLotteryRewardItemArray.CreateRaidLotteryRewardItemArray(builder, vector);
        global::RaidLotteryRewardItemArray.FinishRaidLotteryRewardItemArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateVersionedEncounterArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var both = CreateEncounter(builder, areaText: "4,5", species: 1, form: 0, minLevel: 5, maxLevel: 12, lotValue: 40, scarlet: true, violet: true);
        var scarlet = CreateEncounter(builder, areaText: "4,5", species: 1, form: 0, minLevel: 6, maxLevel: 13, lotValue: 30, scarlet: true, violet: false);
        var violet = CreateEncounter(builder, areaText: "4,5", species: 1, form: 0, minLevel: 7, maxLevel: 14, lotValue: 20, scarlet: false, violet: true);

        var vector = global::EncountPokeDataArray.CreateValuesVector(builder, [both, scarlet, violet]);
        var root = global::EncountPokeDataArray.CreateEncountPokeDataArray(builder, vector);
        global::EncountPokeDataArray.FinishEncountPokeDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<global::EncountPokeData> CreateEncounter(
        FlatBufferBuilder builder,
        string areaText,
        ushort species,
        sbyte form,
        short minLevel,
        short maxLevel,
        short lotValue,
        bool scarlet,
        bool violet)
    {
        var area = builder.CreateString(areaText);
        var time = global::TimeTable.CreateTimeTable(builder, morning: true, noon: true, evening: true, night: true);
        var version = global::VersionTable.CreateVersionTable(builder, A: scarlet, B: violet);

        global::EncountPokeData.StartEncountPokeData(builder);
        global::EncountPokeData.AddVersiontable(builder, version);
        global::EncountPokeData.AddTimetable(builder, time);
        global::EncountPokeData.AddArea(builder, area);
        global::EncountPokeData.AddLotvalue(builder, lotValue);
        global::EncountPokeData.AddMaxlevel(builder, maxLevel);
        global::EncountPokeData.AddMinlevel(builder, minLevel);
        global::EncountPokeData.AddDevid(builder, (global::pml.common.DevID)species);
        global::EncountPokeData.AddFormno(builder, form);
        global::EncountPokeData.AddEnabletable(builder, global::EnableTable.CreateEnableTable(builder, true, false, false, false, false));
        global::EncountPokeData.AddBringItem(builder, global::BringItem.CreateBringItem(builder, (global::ItemID)0, BringRate: 0));
        return global::EncountPokeData.EndEncountPokeData(builder);
    }

    private static byte[] CreateVisibleItemScene(bool includeItem)
    {
        var entries = includeItem
            ? [CreateVisibleItemSceneEntry()]
            : Array.Empty<Trinity.TrinitySceneObjectTemplateEntryT>();

        return new Trinity.TrinitySceneObjectTemplateT
        {
            ObjectTemplateName = "world_item",
            ObjectTemplateExtra = string.Empty,
            Objects = [.. entries],
            Field05 = [],
        }.SerializeToBinary();
    }

    private static Trinity.TrinitySceneObjectTemplateEntryT CreateVisibleItemSceneEntry()
    {
        var sceneObject = new Trinity.TrinitySceneObjectT
        {
            ObjectName = "itemball_test_1",
            ObjectPosition = new Trinity.TrinitySceneObjectPositionT
            {
                Field00 = PackedVec3f(1, 1, 1),
                Field01 = PackedVec3f(0, 1.5f, 0),
                Field02 = PackedVec3f(12.5f, 20, -7.25f),
            },
            Field04 = string.Empty,
            Field07 = string.Empty,
            Field08 = [],
        }.SerializeToBinary();

        var templateData = SerializeObjectTemplateData(new Trinity.TrinitySceneObjectTemplateDataT
        {
            ObjectTemplateName = "itemball_test_1",
            ObjectTemplateExtra = string.Empty,
            ObjectTemplatePath = "obj_template/parts/event/event_item_ball_/event_item_ball.trsot",
            Type = "trinity_SceneObject",
            Data = [.. sceneObject],
        });

        var itemInfo = new Trinity.TrinityPropertySheetObjectT
        {
            Fields =
            [
                PropertySheetField("itemNo", UInt64Property(5)),
                PropertySheetField("num", UInt64Property(3)),
            ],
        };

        var propertySheet = new Trinity.TrinityPropertySheetT
        {
            Name = "event_category_item",
            Extra = string.Empty,
            Properties =
            [
                new Trinity.TrinityPropertySheetObjectT
                {
                    Fields =
                    [
                        PropertySheetField("itemInfo", ObjectProperty(itemInfo)),
                    ],
                },
            ],
        }.SerializeToBinary();

        return new Trinity.TrinitySceneObjectTemplateEntryT
        {
            Type = "trinity_ObjectTemplate",
            Data = [.. templateData],
            SubObjects =
            [
                new Trinity.TrinitySceneObjectTemplateEntryT
                {
                    Type = "trinity_PropertySheet",
                    Data = [.. propertySheet],
                    SubObjects = [],
                },
            ],
        };
    }

    private static byte[] SerializeObjectTemplateData(Trinity.TrinitySceneObjectTemplateDataT data)
    {
        var builder = new FlatBufferBuilder(1024);
        var root = Trinity.TrinitySceneObjectTemplateData.Pack(builder, data);
        builder.Finish(root.Value);
        return builder.SizedByteArray();
    }

    private static Trinity.TrinityPropertySheetFieldT PropertySheetField(
        string name,
        Trinity.TrinityPropertySheetValueUnion data)
    {
        return new Trinity.TrinityPropertySheetFieldT
        {
            Name = name,
            Data = data,
        };
    }

    private static Trinity.TrinityPropertySheetValueUnion UInt64Property(ulong value)
    {
        return Trinity.TrinityPropertySheetValueUnion.FromTrinityPropertySheetField1(
            new Trinity.TrinityPropertySheetField1T
            {
                Value = value,
            });
    }

    private static Trinity.TrinityPropertySheetValueUnion ObjectProperty(Trinity.TrinityPropertySheetObjectT value)
    {
        return Trinity.TrinityPropertySheetValueUnion.FromTrinityPropertySheetObject(value);
    }

    private static Trinity.PackedVec3fT PackedVec3f(float x, float y, float z)
    {
        return new Trinity.PackedVec3fT
        {
            X = x,
            Y = y,
            Z = z,
        };
    }

    private static void WriteSvOutput(TemporaryBridgeProject temp, string relativePath, byte[] contents)
    {
        temp.WriteOutputFile(Path.Combine("romfs", relativePath.Replace('/', Path.DirectorySeparatorChar)), contents);
    }

    private static byte[] ReadSvOutput(TemporaryBridgeProject temp, string relativePath)
    {
        var loosePath = Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loosePath))
        {
            return File.ReadAllBytes(loosePath);
        }

        return File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, 8), titleId);
        return data;
    }

    private static byte[] CreateTextTable(int count, params (int Index, string Text)[] entries)
    {
        var lines = Enumerable
            .Range(0, count)
            .Select(_ => new SwShGameTextLine(string.Empty, Flags: 0))
            .ToArray();

        foreach (var entry in entries)
        {
            lines[entry.Index] = new SwShGameTextLine(entry.Text, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }

    private static byte[] CreateKeyTable(params string[] keys)
    {
        return new SwShAhtbFile(
            keys.Select((key, index) => new SwShAhtbEntry((ulong)index, key)).ToArray())
            .Write();
    }

    private static byte[] CreateTrinityDescriptor(IReadOnlyList<string> virtualPaths)
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = KM.Formats.SV.Trinity.FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = KM.Formats.SV.Trinity.FileDescriptor.CreateFileHashesVector(
            builder,
            virtualPaths.Select(KM.Formats.SV.SvTrinityPathHasher.HashPath).ToArray());
        var fileEntries = virtualPaths
            .Select(_ => KM.Formats.SV.Trinity.FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: 0))
            .ToArray();
        var files = KM.Formats.SV.Trinity.FileDescriptor.CreateFilesVector(builder, fileEntries);
        var pack = KM.Formats.SV.Trinity.PackDescriptorEntry.CreatePackDescriptorEntry(
            builder,
            file_size: 123,
            file_count: checked((ulong)virtualPaths.Count));
        var packs = KM.Formats.SV.Trinity.FileDescriptor.CreatePacksVector(builder, [pack]);
        var root = KM.Formats.SV.Trinity.FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        KM.Formats.SV.Trinity.FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static BridgeResponse<TPayload> Dispatch<TPayload>(
        ProjectBridgeDispatcher dispatcher,
        string command,
        object payload,
        string requestId)
    {
        var requestJson = JsonSerializer.Serialize(
            new BridgeRequest<object>(command, payload, requestId),
            BridgeJson.SerializerOptions);
        var responseJson = dispatcher.Dispatch(requestJson);
        var response = JsonSerializer.Deserialize<BridgeResponse<TPayload>>(responseJson, BridgeJson.SerializerOptions);
        Assert.NotNull(response);
        return response;
    }

    private static void AssertMainSource(
        IReadOnlyList<FileProvenanceDto> sources,
        FileLayerDto expectedLayer)
    {
        var source = Assert.Single(sources);
        Assert.Equal(expectedLayer, source.Layer);
        Assert.Equal("exefs/main", source.RelativePath);
    }

    private sealed record ScarletFixtureSet(
        IReadOnlyDictionary<string, byte[]> OutputFiles,
        IReadOnlyDictionary<string, byte[]> BaseFiles);

    private static void AssertSuccess<TPayload>(BridgeResponse<TPayload> response)
    {
        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
    }
}
