// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.GameDump;
using KM.Api.Gifts;
using KM.Api.Items;
using KM.Api.ModMerger;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Shops;
using KM.Api.StaticEncounters;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.Workflows;
using KM.Formats.SwSh;
using KM.Formats.ZA;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.Formats.ZA.Generated.GameData;
using KM.Formats.ZA.Trinity;
using KM.Integration.Tests.Tools;
using KM.Tools.Bridge;
using KM.ZA.Data;
using KM.ZA.Placement;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class PokemonLegendsZABridgeTests
{
    private const ulong PokemonLegendsZATitleId = 0x0100F43008C44000;
    private const int ZaNpdmTitleIdOffset = 0x480;
    private const int ZaPokemonDataRareNotShiny = 0x1FFFFFFF;
    private const int ZaPokemonDataRareForcedShiny = 0x2FFFFFFF;
    private const int ZaPokemonDataRareDefaultShinyRoll = 0x3FFFFFFF;
    private const string ModMergerDataVirtualPath = "bin/mock/data.bin";
    private const string ModMergerDataOutputPath = "romfs/bin/mock/data.bin";
    private const string ModMergerDescriptorOutputPath = "romfs/arc/data.trpfd";

    [Fact]
    public void PokemonLegendsZAProjectLoadsPokemonData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var dispatcher = new ProjectBridgeDispatcher();

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(CreatePaths(temp)),
            "request-za-pokemon");

        AssertSuccess(pokemon);
        var workflow = pokemon.Payload!.Workflow;
        Assert.Equal("Pokemon Data", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        Assert.Equal(3, workflow.Stats.TotalPokemonCount);
        Assert.Equal(2, workflow.Stats.PresentPokemonCount);
        var bulbasaur = workflow.Pokemon.Single(row => row.PersonalId == 1);
        Assert.Equal(1, bulbasaur.SpeciesId);
        Assert.Equal("Pokemon 1", bulbasaur.Name);
        Assert.Equal("Grass", bulbasaur.Type1);
        Assert.Equal("Poison", bulbasaur.Type2);
        Assert.Equal(45, bulbasaur.BaseStats.HP);
        Assert.Equal(318, bulbasaur.BaseStats.Total);
        Assert.Equal(65, bulbasaur.Abilities.Ability1);
        Assert.Equal(34, bulbasaur.Abilities.HiddenAbility);
        Assert.Equal(25, bulbasaur.DexPresence.RegionalDexIndex);
        Assert.Equal("Z-A Dex Order", workflow.EditableFields.Single(field => field.Field == "regionalDexIndex").Label);
        var learnedMove = Assert.Single(bulbasaur.Learnset);
        Assert.Equal(33, learnedMove.MoveId);
        Assert.Equal(1, learnedMove.Level);
        Assert.Equal((int?)0x0A01, learnedMove.RawLevel);
        Assert.Equal("Lv. 1 / Mastery Lv. 10", learnedMove.LevelLabel);
        Assert.Equal(1, Assert.Single(bulbasaur.Evolutions).Species);
        var tmGroup = bulbasaur.Compatibility.Single(group => group.GroupId == "tm");
        Assert.Contains(tmGroup.Entries, entry => entry.MoveId == 45 && entry.CanLearn);
        var unavailablePokemon = workflow.Pokemon.Single(row => row.PersonalId == 3);
        Assert.Equal(3, unavailablePokemon.SpeciesId);
        Assert.False(unavailablePokemon.DexPresence.IsPresentInGame);
    }

    [Fact]
    public void PokemonLegendsZAProjectListsPokemonTrainersGiftTradeMovesItemsShopsAndModMergerWorkflows()
    {
        using var temp = CreatePokemonLegendsZAProject();
        var dispatcher = new ProjectBridgeDispatcher();

        var workflows = Dispatch<ListWorkflowsResponse>(
            dispatcher,
            KmCommandNames.ListWorkflows,
            new ListWorkflowsRequest(CreatePaths(temp)),
            "request-za-workflows");

        AssertSuccess(workflows);
        Assert.Contains(workflows.Payload!.Workflows, workflow => workflow.Id == "pokemon" && workflow.Label == "Pokemon Data");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "trainers" && workflow.Label == "Trainers");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "encounters" && workflow.Label == "Wild Encounters");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "staticEncounters" && workflow.Label == "Static Encounters");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "giftPokemon" && workflow.Label == "Gift Pokemon");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "tradePokemon" && workflow.Label == "Trade Pokemon");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "moves" && workflow.Label == "Moves");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "items" && workflow.Label == "Items");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "placement" && workflow.Label == "Placement");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "shops" && workflow.Label == "Shops");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "typeChart" && workflow.Label == "Type Chart");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "modMerger" && workflow.Label == "Mod Merger");
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsMoveData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.MoveDataArray, CreateMoveDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = new ProjectBridgeDispatcher();

        var moves = Dispatch<LoadMovesWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadMovesWorkflow,
            new LoadMovesWorkflowRequest(CreatePaths(temp)),
            "request-za-moves");

        AssertSuccess(moves);
        var workflow = moves.Payload!.Workflow;
        Assert.Equal("Moves", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Moves.Count);
        Assert.Equal(2, workflow.Stats.TotalMoveCount);
        Assert.Equal(2, workflow.Stats.EnabledMoveCount);
        var tackle = workflow.Moves.Single(move => move.MoveId == 33);
        Assert.Equal("Tackle", tackle.Name);
        Assert.Equal("Normal", tackle.TypeName);
        Assert.Equal("Physical", tackle.CategoryName);
        Assert.Equal(40, tackle.Power);
        Assert.Equal(35, tackle.PP);
        Assert.Equal("Opponent", tackle.TargetName);
        Assert.Contains(tackle.Flags, flag => flag.Field == "makesContact" && flag.Enabled);
        Assert.Contains(workflow.EditableFields, field => field.Field == "power" && field.Label == "Power");
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsItemData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = new ProjectBridgeDispatcher();

        var items = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(CreatePaths(temp)),
            "request-za-items");

        AssertSuccess(items);
        var workflow = items.Payload!.Workflow;
        Assert.Equal("Items", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        Assert.Equal(3, workflow.Items.Count);
        Assert.Equal(3, workflow.Stats.TotalItemCount);
        var pokeBall = workflow.Items.Single(item => item.ItemId == 4);
        Assert.Equal("Poke Ball", pokeBall.Name);
        Assert.Equal("Balls", pokeBall.Category);
        Assert.Equal(100, pokeBall.BuyPrice);
        Assert.Equal("999", pokeBall
            .DetailGroups
            .Single(group => group.Label == "Pokemon Legends Z-A")
            .Details
            .Single(detail => detail.Label == "Stack cap")
            .Value);
        var tm = workflow.Items.Single(item => item.ItemId == 328);
        Assert.Equal("Technical Machines", tm.Category);
        Assert.Equal(33, tm.Metadata.MachineMoveId);
        Assert.Equal("Tackle", tm.Metadata.MachineMoveName);
        Assert.Contains(workflow.EditableFields, field => field.Field == "machineMoveId" && field.Label == "TM move");
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsShopData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemArray, CreateShopDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemLineupArray, CreateShopLineupArray());
        var dispatcher = new ProjectBridgeDispatcher();

        var shops = Dispatch<LoadShopsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadShopsWorkflow,
            new LoadShopsWorkflowRequest(CreatePaths(temp)),
            "request-za-shops");

        AssertSuccess(shops);
        var workflow = shops.Payload!.Workflow;
        Assert.Equal("za", workflow.EditorFamily);
        Assert.Equal("Shops", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        var shop = Assert.Single(workflow.Shops);
        Assert.Equal("shop:a01_friendlyshop_01", shop.ShopId);
        Assert.Equal("Friendly Shop", shop.Name);
        Assert.Equal("Friendly Shop", shop.Kind);
        Assert.Equal("Money", shop.Currency);
        Assert.Equal("za", shop.EditorFamily);
        Assert.True(shop.CanEditInventoryOrder);
        Assert.EndsWith(ZaDataPaths.ShopItemLineupArray, shop.Provenance.SourceFile, StringComparison.Ordinal);
        Assert.Equal(2, shop.Inventory.Count);

        var potion = shop.Inventory.Single(item => item.ItemId == 17);
        Assert.Equal("Potion", potion.ItemName);
        Assert.Equal(150, potion.Price);
        Assert.False(potion.CanEditPrice);
        Assert.Contains("zaConditionKind", potion.SupportedFields);
        Assert.Equal("1", potion.FieldValues["zaConditionKind"]);
        Assert.Equal("30600", potion.FieldValues["zaConditionArguments"]);

        Assert.Contains(workflow.EditableFields, field => field.Field == "itemId" && field.Label == "Item");
        Assert.Contains(workflow.EditableFields, field => field.Field == "displayIndex" && field.Label == "Display order");
        Assert.Contains(workflow.EditableFields, field => field.Field == "zaConditionKind" && field.Label == "First condition");
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsTrainerData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTrainerFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(CreatePaths(temp)),
            "request-za-trainers");

        AssertSuccess(trainers);
        var workflow = trainers.Payload!.Workflow;
        Assert.Equal("Trainers", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        var trainer = Assert.Single(workflow.Trainers);
        Assert.Equal("Rival Aria", trainer.Name);
        Assert.Equal("Duelist", trainer.TrainerClass);
        Assert.Equal("tr_battle_main_001", trainer.Location);
        Assert.Equal("Mega Evolution", trainer.BattleType);
        Assert.Equal(4, trainer.Money);
        Assert.Equal(0, trainer.Gift);
        Assert.Equal(26, trainer.ZaRank);
        Assert.True(trainer.ZaMegaEvolution);
        Assert.False(trainer.ZaLastHand);
        Assert.Contains(trainer.AiFlagStates, flag => flag.Label == "Basic" && flag.Enabled);
        Assert.Contains(trainer.AiFlagStates, flag => flag.Label == "High" && flag.Enabled);

        var pokemon = Assert.Single(trainer.Team);
        Assert.Equal(0, pokemon.Slot);
        Assert.Equal(1, pokemon.SpeciesId);
        Assert.Equal("Bulbasaur", pokemon.Species);
        Assert.Equal(12, pokemon.Level);
        Assert.Equal(4, pokemon.HeldItemId);
        Assert.Equal("Poke Ball", pokemon.HeldItem);
        Assert.Equal([33, 45, 0, 0], pokemon.MoveIds);
        Assert.Equal("Tackle", pokemon.Moves[0]);
        Assert.Equal("Growl", pokemon.Moves[1]);
        Assert.Equal(2, pokemon.Ability);
        Assert.Equal("Overgrow (Ability 1)", pokemon.AbilityLabel);
        Assert.Contains(workflow.EditableFields, field => field.Field == "rank" && field.Label == "Z-A rank");
        Assert.Contains(workflow.EditableFields, field => field.Field == "megaEvolution" && field.Label == "Mega Evolution");
    }

    [Fact]
    public void PokemonLegendsZATrainerLabelsUseHashClassesAndReadableFallbackNames()
    {
        const ulong hyperspaceTrainerHash = 0xCB6F6D064E9E96A4;

        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerDataArray,
            CreateTrainerDataArray(
                trainerId: "dim_rank_02_mizu_05",
                trainerType: hyperspaceTrainerHash,
                trainerType2: hyperspaceTrainerHash));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerNames("English"),
            CreateTextTable(0, (0, "???")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerTypes("English"),
            CreateTextTable(0, (0, "Hyperspace Trainer")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerTypeKeys("English"),
            CreateKeyTable((hyperspaceTrainerHash, "MSG_TRTYPE_DIM")));
        var dispatcher = new ProjectBridgeDispatcher();

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(CreatePaths(temp)),
            "request-za-trainer-labels");

        AssertSuccess(trainers);
        var trainer = Assert.Single(trainers.Payload!.Workflow.Trainers);
        Assert.Equal("Hyperspace Trainer Rank 2 Water 5", trainer.Name);
        Assert.Equal(0, trainer.TrainerClassId);
        Assert.Equal("Hyperspace Trainer", trainer.TrainerClass);
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsGiftPokemonData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteGiftPokemonFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();

        var gifts = Dispatch<LoadGiftPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGiftPokemonWorkflow,
            new LoadGiftPokemonWorkflowRequest(CreatePaths(temp)),
            "request-za-gift-pokemon");

        AssertSuccess(gifts);
        var workflow = gifts.Payload!.Workflow;
        Assert.Equal("Gift Pokemon", workflow.Summary.Label);
        Assert.Equal("za", workflow.EditorFamily);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        Assert.Equal(1, workflow.Stats.TotalGiftCount);

        var gift = Assert.Single(workflow.Gifts);
        Assert.Equal("za", gift.EditorFamily);
        Assert.Equal(0, gift.GiftIndex);
        Assert.Equal("main_init_poke_1 + test_encount_init_poke_0", gift.EventLabel);
        Assert.Contains("Bulbasaur", gift.Label);
        Assert.Equal(1, gift.SpeciesId);
        Assert.Equal("Bulbasaur", gift.Species);
        Assert.Equal(0, gift.Form);
        Assert.Equal(5, gift.Level);
        Assert.Equal(4, gift.HeldItemId);
        Assert.Equal("Poke Ball", gift.HeldItem);
        Assert.Equal(2, gift.Ability);
        Assert.Equal("Overgrow (Ability 1)", gift.AbilityLabel);
        Assert.Equal(4, gift.Nature);
        Assert.Equal("Adamant (+Atk, -Sp. Atk)", gift.NatureLabel);
        Assert.Equal(1, gift.Gender);
        Assert.Equal("Male", gift.GenderLabel);
        Assert.Equal(ZaPokemonDataRareNotShiny, gift.ShinyLock);
        Assert.Equal("Not shiny", gift.ShinyLockLabel);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == "shinyLock").Options,
            option => option.Value == ZaPokemonDataRareForcedShiny && option.Label == "Forced shiny");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == "shinyLock").Options,
            option => option.Value == ZaPokemonDataRareDefaultShinyRoll && option.Label == "Default shiny roll");
        Assert.DoesNotContain(
            workflow.EditableFields.Single(field => field.Field == "shinyLock").Options,
            option => option.Label.Contains(ZaPokemonDataRareForcedShiny.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == "move4Id").Options,
            option => option.Value == -1 && option.Label == "-1 None");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == "move4Id").Options,
            option => option.Value == 0 && option.Label == "0 Game default / auto move");
        Assert.Equal(33, gift.Moves[0].MoveId);
        Assert.Equal("Tackle", gift.Moves[0].Move);
        Assert.Equal(45, gift.Moves[1].MoveId);
        Assert.Equal("Growl", gift.Moves[1].Move);
        Assert.Equal(31, gift.Ivs.HP);
        Assert.Equal(30, gift.Ivs.Attack);
        Assert.Null(gift.FlawlessIvCount);
        Assert.Contains("Fixed IVs", gift.IvSummary);
        Assert.EndsWith(ZaDataPaths.PokemonDataArray, gift.Provenance.SourceFile, StringComparison.Ordinal);
        Assert.Contains(gift.AbilityOptions, option => option.Value == 4 && option.Label == "Chlorophyll (Hidden Ability)");
        Assert.Contains(workflow.EditableFields, field => field.Field == "species" && field.Label == "Species");
        Assert.Contains(workflow.EditableFields, field => field.Field == "move1Id" && field.Label == "Move 1");
        Assert.Contains(workflow.EditableFields, field => field.Field == "flawlessIvCount" && field.Label == "IV preset");
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsGameDefaultGiftPokemonSentinels()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteGiftPokemonFixture(temp, gameDefaultGift: true);
        var dispatcher = new ProjectBridgeDispatcher();

        var gifts = Dispatch<LoadGiftPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGiftPokemonWorkflow,
            new LoadGiftPokemonWorkflowRequest(CreatePaths(temp)),
            "request-za-default-gift-pokemon");

        AssertSuccess(gifts);
        var gift = Assert.Single(gifts.Payload!.Workflow.Gifts);
        Assert.Contains("Game default level", gift.Label);
        Assert.Equal(0, gift.Level);
        Assert.Equal(-1, gift.Moves[0].MoveId);
        Assert.Null(gift.Moves[0].Move);
        Assert.Equal(-1, gift.SpecialMoveId);
        Assert.Equal(-1, gift.Ivs.HP);
        Assert.Equal(0, gift.FlawlessIvCount);
        Assert.Equal("Random IVs", gift.IvSummary);
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsTradePokemonData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTradePokemonFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();

        var trades = Dispatch<LoadTradePokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTradePokemonWorkflow,
            new LoadTradePokemonWorkflowRequest(CreatePaths(temp)),
            "request-za-trade-pokemon");

        AssertSuccess(trades);
        var workflow = trades.Payload!.Workflow;
        Assert.Equal("Trade Pokemon", workflow.Summary.Label);
        Assert.Equal("za", workflow.EditorFamily);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        Assert.Equal(1, workflow.Stats.TotalTradeCount);

        var trade = Assert.Single(workflow.Trades);
        Assert.Equal("za", trade.EditorFamily);
        Assert.Equal(0, trade.TradeIndex);
        Assert.Equal("sub_tradepoke_bulbasaur", trade.EventLabel);
        Assert.Contains("Bulbasaur", trade.Label);
        Assert.Equal(1, trade.SpeciesId);
        Assert.Equal("Bulbasaur", trade.Species);
        Assert.Equal("Script linked", trade.RequiredSpecies);
        Assert.Equal(5, trade.Level);
        Assert.Equal(4, trade.HeldItemId);
        Assert.Equal("Poke Ball", trade.HeldItem);
        Assert.Equal(2, trade.Ability);
        Assert.Equal("Overgrow (Ability 1)", trade.AbilityLabel);
        Assert.Equal(4, trade.Nature);
        Assert.Equal("Adamant (+Atk, -Sp. Atk)", trade.NatureLabel);
        Assert.Equal(33, trade.Moves[0].MoveId);
        Assert.Equal("Tackle", trade.Moves[0].Move);
        Assert.Equal(45, trade.RelearnMoves[1].MoveId);
        Assert.Equal("Growl", trade.RelearnMoves[1].Move);
        Assert.Null(trade.FlawlessIvCount);
        Assert.Contains("Fixed IVs", trade.IvSummary);
        Assert.EndsWith(ZaDataPaths.PokemonDataArray, trade.Provenance.SourceFile, StringComparison.Ordinal);
        Assert.Contains(workflow.EditableFields, field => field.Field == "species" && field.Label == "Species");
        Assert.Contains(workflow.EditableFields, field => field.Field == "move1Id" && field.Label == "Move 1");
        Assert.Contains(workflow.EditableFields, field => field.Field == "flawlessIvCount" && field.Label == "IV preset");
    }

    [Fact]
    public void PokemonLegendsZAGameDumpWritesImplementedCategoryFiles()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.MoveDataArray, CreateMoveDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemArray, CreateShopDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemLineupArray, CreateShopLineupArray());
        WriteTrainerFixture(temp);
        WriteGiftPokemonFixture(temp);
        WriteTradePokemonFixture(temp);
        WriteStaticEncounterFixture(temp);
        WritePlacementFixture(temp);
        temp.WriteBaseExeFsFile("main", ZaTypeChartBridgeFixtures.CreateCompatibleMain());
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadGameDumpWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGameDumpWorkflow,
            new LoadGameDumpWorkflowRequest(paths),
            "request-za-game-dump-load");

        AssertSuccess(load);
        var categories = load.Payload!.Workflow.Categories;
        Assert.Equal(["pokemon", "trainers", "staticEncounters", "giftPokemon", "tradePokemon", "moves", "items", "placement", "shops", "typeChart"], categories.Select(category => category.Id).ToArray());
        Assert.All(categories, category => Assert.True(category.IsAvailable, category.Id));

        var destinationFolder = Path.Combine(temp.RootPath, "dump");
        var run = Dispatch<RunGameDumpResponse>(
            dispatcher,
            KmCommandNames.RunGameDump,
            new RunGameDumpRequest(
                paths,
                destinationFolder,
                [
                    new GameDumpSelectionDto("items", GameDumpFormatDto.TsvAndJson),
                    new GameDumpSelectionDto("trainers", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("placement", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("staticEncounters", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("giftPokemon", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("tradePokemon", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("shops", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("typeChart", GameDumpFormatDto.Json),
                ]),
            "request-za-game-dump-run");

        AssertSuccess(run);
        Assert.True(
            run.Payload!.Result.Succeeded,
            string.Join(Environment.NewLine, run.Payload.Result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "items" && file.RelativePath == Path.Combine("Items", "items.tsv"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "items" && file.RelativePath == Path.Combine("Items", "items.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "shops" && file.RelativePath == Path.Combine("Shops", "shops.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "trainers" && file.RelativePath == Path.Combine("Trainers", "trainers.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "placement" && file.RelativePath == Path.Combine("Placement", "placement.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "staticEncounters" && file.RelativePath == Path.Combine("Static Encounters", "staticEncounters.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "giftPokemon" && file.RelativePath == Path.Combine("Gift Pokemon", "giftPokemon.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "tradePokemon" && file.RelativePath == Path.Combine("Trade Pokemon", "tradePokemon.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "typeChart" && file.RelativePath == Path.Combine("Type Chart", "typeChart.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "manifest" && file.RelativePath == "manifest.json");
        Assert.Contains("Poke Ball", File.ReadAllText(Path.Combine(destinationFolder, "Items", "items.tsv")));
        Assert.Contains("Rival Aria", File.ReadAllText(Path.Combine(destinationFolder, "Trainers", "trainers.json")));
        Assert.Contains("wild_spawn_001", File.ReadAllText(Path.Combine(destinationFolder, "Placement", "placement.json")));
        Assert.Contains("static_event_ivysaur", File.ReadAllText(Path.Combine(destinationFolder, "Static Encounters", "staticEncounters.json")));
        Assert.Contains("main_init_poke_1", File.ReadAllText(Path.Combine(destinationFolder, "Gift Pokemon", "giftPokemon.json")));
        Assert.Contains("sub_tradepoke_bulbasaur", File.ReadAllText(Path.Combine(destinationFolder, "Trade Pokemon", "tradePokemon.json")));
        Assert.Contains("Friendly Shop", File.ReadAllText(Path.Combine(destinationFolder, "Shops", "shops.json")));
        Assert.Contains("attackTypeIndex", File.ReadAllText(Path.Combine(destinationFolder, "Type Chart", "typeChart.json")));
        Assert.Contains("Pokemon Legends Z-A", File.ReadAllText(Path.Combine(destinationFolder, "manifest.json")));
    }

    [Fact]
    public void PokemonLegendsZAPokemonEditWritesStandalonePersonalTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, Session: null, PersonalId: 1, Field: "hp", Value: "99"),
            "request-za-pokemon-update");
        AssertSuccess(update);
        Assert.Equal(99, update.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).BaseStats.HP);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.PersonalArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var outputPath = Path.Combine(temp.OutputRootPath, "avalon", "data", "personal_array.bin");
        Assert.True(File.Exists(outputPath));
        var written = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(File.ReadAllBytes(outputPath)));
        var row = written.Entry(1);
        Assert.NotNull(row);
        Assert.Equal(99, row!.Value.BaseStats!.Value.Hp);
    }

    [Fact]
    public void PokemonLegendsZALearnsetEditsPreservePackedMasteryLevel()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdatePokemonLearnsetResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonLearnset,
            new UpdatePokemonLearnsetRequest(paths, Session: null, PersonalId: 1, Action: "upsert", Slot: 0, MoveId: 45, Level: 7),
            "request-za-pokemon-learnset-update");
        AssertSuccess(update);
        var updatedMove = Assert.Single(update.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Learnset);
        Assert.Equal(45, updatedMove.MoveId);
        Assert.Equal(7, updatedMove.Level);
        Assert.Equal((int?)0x0A07, updatedMove.RawLevel);
        Assert.Equal("Lv. 7 / Mastery Lv. 10", updatedMove.LevelLabel);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-learnset-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-learnset-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var outputPath = Path.Combine(temp.OutputRootPath, "avalon", "data", "personal_array.bin");
        var written = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(File.ReadAllBytes(outputPath)));
        var row = written.Entry(1);
        Assert.NotNull(row);
        var learnedMove = row!.Value.LevelupMoves(0);
        Assert.NotNull(learnedMove);
        Assert.Equal(45, learnedMove!.Value.Move);
        Assert.Equal(0x0A07, learnedMove.Value.Level);
    }

    [Fact]
    public void PokemonLegendsZAMoveEditWritesTrinityMoveTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.MoveDataArray, CreateMoveDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateMoveFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateMoveFields,
            new UpdateMoveFieldsRequest(
                paths,
                Session: null,
                [
                    new MoveFieldUpdateDto(33, "power", "90"),
                    new MoveFieldUpdateDto(33, "pp", "15"),
                ]),
            "request-za-move-update");
        AssertSuccess(update);
        var updatedMove = update.Payload!.Workflow.Moves.Single(move => move.MoveId == 33);
        Assert.Equal(90, updatedMove.Power);
        Assert.Equal(15, updatedMove.PP);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-move-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.MoveDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-move-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadMove(temp, 33);
        Assert.Equal(90, written.Power);
        Assert.Equal(15, written.Pp);
        Assert.Equal(40, ReadMove(temp, 45).Pp);
    }

    [Fact]
    public void PokemonLegendsZAItemEditWritesTrinityItemTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(328, "price", "3000"),
                    new ItemFieldUpdateDto(328, "machineMoveId", "45"),
                    new ItemFieldUpdateDto(17, "healPercentage", "50"),
                ]),
            "request-za-item-update");
        AssertSuccess(update);
        var updatedTm = update.Payload!.Workflow.Items.Single(item => item.ItemId == 328);
        Assert.Equal(3000, updatedTm.BuyPrice);
        Assert.Equal(45, updatedTm.Metadata.MachineMoveId);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-item-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.ItemDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-item-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var writtenTm = ReadItem(temp, 328);
        Assert.Equal(3000, writtenTm.Price);
        Assert.Equal(45, writtenTm.MachineWaza);
        Assert.Equal(50, ReadItem(temp, 17).HealPercentage);
    }

    [Fact]
    public void PokemonLegendsZAShopEditWritesTrinityLineupTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemArray, CreateShopDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemLineupArray, CreateShopLineupArray());
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var reorder = Dispatch<UpdateShopInventoryItemResponse>(
            dispatcher,
            KmCommandNames.UpdateShopInventoryItem,
            new UpdateShopInventoryItemRequest(
                paths,
                Session: null,
                ShopId: "shop:a01_friendlyshop_01",
                Slot: 1,
                Field: "setInventory",
                Value: "17,4"),
            "request-za-shop-reorder");
        AssertSuccess(reorder);
        Assert.Equal([17, 4], reorder.Payload!.Workflow.Shops.Single().Inventory.Select(item => item.ItemId).ToArray());

        var conditionKind = Dispatch<UpdateShopInventoryItemResponse>(
            dispatcher,
            KmCommandNames.UpdateShopInventoryItem,
            new UpdateShopInventoryItemRequest(
                paths,
                reorder.Payload.Session,
                ShopId: "shop:a01_friendlyshop_01",
                Slot: 1,
                Field: "zaConditionKind",
                Value: "1"),
            "request-za-shop-condition-kind");
        AssertSuccess(conditionKind);

        var conditionArgs = Dispatch<UpdateShopInventoryItemResponse>(
            dispatcher,
            KmCommandNames.UpdateShopInventoryItem,
            new UpdateShopInventoryItemRequest(
                paths,
                conditionKind.Payload!.Session,
                ShopId: "shop:a01_friendlyshop_01",
                Slot: 1,
                Field: "zaConditionArguments",
                Value: "99999"),
            "request-za-shop-condition-args");
        AssertSuccess(conditionArgs);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, conditionArgs.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-shop-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.ShopItemLineupArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, conditionArgs.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-shop-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadShopLineup(temp, "a01_friendlyshop_01_lineup1");
        var first = written.Inventory(0);
        var second = written.Inventory(1);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(17u, first!.Value.Item);
        Assert.Equal(1u, first.Value.DisplayIndex);
        Assert.Equal(4u, second!.Value.Item);
        var firstCondition = first.Value.Conditions(0)!.Value.Values(0)!.Value.Values(0)!.Value;
        Assert.Equal("phase_condition", firstCondition.Condition);
        Assert.Equal("99999", firstCondition.Arguments(0));
    }

    [Fact]
    public void PokemonLegendsZATrainerEditWritesTrinityTrainerTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTrainerFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateTrainerFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerFields,
            new UpdateTrainerFieldsRequest(
                paths,
                Session: null,
                [
                    new TrainerFieldUpdateDto(0, null, "rank", "25"),
                    new TrainerFieldUpdateDto(0, null, "megaEvolution", "0"),
                    new TrainerFieldUpdateDto(0, 0, "speciesId", "2"),
                    new TrainerFieldUpdateDto(0, 0, "level", "42"),
                    new TrainerFieldUpdateDto(0, 0, "move1Id", "45"),
                ]),
            "request-za-trainer-update");
        AssertSuccess(update);
        var trainer = Assert.Single(update.Payload!.Workflow.Trainers);
        Assert.Equal(25, trainer.ZaRank);
        Assert.False(trainer.ZaMegaEvolution);
        Assert.Equal("Trainer Battle", trainer.BattleType);
        var pokemon = Assert.Single(trainer.Team);
        Assert.Equal(2, pokemon.SpeciesId);
        Assert.Equal("Ivysaur", pokemon.Species);
        Assert.Equal(42, pokemon.Level);
        Assert.Equal(45, pokemon.MoveIds[0]);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-trainer-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.TrainerDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-trainer-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadTrainer(temp, 0);
        Assert.Equal(25, written.Rank);
        Assert.False(written.MegaEvolution);
        var writtenPokemon = written.Pokemon1;
        Assert.NotNull(writtenPokemon);
        Assert.Equal(2, writtenPokemon!.Value.SpeciesId);
        Assert.Equal(42, writtenPokemon!.Value.Level);
        Assert.Equal(45, writtenPokemon.Value.Move1!.Value.MoveId);
    }

    [Fact]
    public void PokemonLegendsZAGiftPokemonEditWritesTrinityPokemonDataTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteGiftPokemonFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateGiftPokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateGiftPokemonFields,
            new UpdateGiftPokemonFieldsRequest(
                paths,
                Session: null,
                [
                    new GiftPokemonFieldUpdateDto(0, "species", "2"),
                    new GiftPokemonFieldUpdateDto(0, "level", "12"),
                    new GiftPokemonFieldUpdateDto(0, "heldItemId", "17"),
                    new GiftPokemonFieldUpdateDto(0, "move1Id", "45"),
                    new GiftPokemonFieldUpdateDto(0, "move4Id", "-1"),
                    new GiftPokemonFieldUpdateDto(0, "shinyLock", ZaPokemonDataRareForcedShiny.ToString(CultureInfo.InvariantCulture)),
                    new GiftPokemonFieldUpdateDto(0, "ivHp", "31"),
                ]),
            "request-za-gift-pokemon-update");
        AssertSuccess(update);
        var gift = Assert.Single(update.Payload!.Workflow.Gifts);
        Assert.Equal(2, gift.SpeciesId);
        Assert.Equal("Ivysaur", gift.Species);
        Assert.Contains("Ivysaur", gift.Label);
        Assert.DoesNotContain("Bulbasaur", gift.Label);
        Assert.Equal(12, gift.Level);
        Assert.Equal(17, gift.HeldItemId);
        Assert.Equal(45, gift.Moves[0].MoveId);
        Assert.Equal(-1, gift.Moves[3].MoveId);
        Assert.Null(gift.Moves[3].Move);
        Assert.Equal(ZaPokemonDataRareForcedShiny, gift.ShinyLock);
        Assert.Equal("Forced shiny", gift.ShinyLockLabel);
        Assert.Equal(31, gift.Ivs.HP);
        Assert.Contains("Fixed IVs", gift.IvSummary);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-gift-pokemon-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.PokemonDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-gift-pokemon-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var writtenScene = ReadGiftPokemonData(temp, "main_init_poke_1");
        Assert.Equal(2, writtenScene.DevNo);
        Assert.Equal(12, writtenScene.MinLevel);
        Assert.Equal(12, writtenScene.MaxLevel);
        Assert.Equal(17, writtenScene.HoldItem!.Value.HoldItem);
        Assert.Equal(45, writtenScene.WazaList!.Value.Waza1);
        Assert.Equal(-1, writtenScene.WazaList!.Value.Waza4);
        Assert.Equal(ZaPokemonDataRareForcedShiny, writtenScene.Rare);
        Assert.Equal(128, writtenScene.TalentScale);
        Assert.Equal(31, writtenScene.TalentValue!.Value.Hp);

        var writtenPlayable = ReadGiftPokemonData(temp, "test_encount_init_poke_0");
        Assert.Equal(2, writtenPlayable.DevNo);
        Assert.Equal(12, writtenPlayable.MinLevel);
        Assert.Equal(12, writtenPlayable.MaxLevel);
        Assert.Equal(17, writtenPlayable.HoldItem!.Value.HoldItem);
        Assert.Equal(45, writtenPlayable.WazaList!.Value.Waza1);
        Assert.Equal(-1, writtenPlayable.WazaList!.Value.Waza4);
        Assert.Equal(ZaPokemonDataRareForcedShiny, writtenPlayable.Rare);
        Assert.Equal(128, writtenPlayable.TalentScale);
        Assert.Equal(31, writtenPlayable.TalentValue!.Value.Hp);

        var ignored = ReadGiftPokemonData(temp, "wild_ignore");
        Assert.Equal(20, ignored.MinLevel);
        Assert.Equal(20, ignored.MaxLevel);
    }

    [Fact]
    public void PokemonLegendsZATradePokemonEditWritesTrinityPokemonDataTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTradePokemonFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateTradePokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonFields,
            new UpdateTradePokemonFieldsRequest(
                paths,
                Session: null,
                [
                    new TradePokemonFieldUpdateDto(0, "species", "2"),
                    new TradePokemonFieldUpdateDto(0, "level", "18"),
                    new TradePokemonFieldUpdateDto(0, "heldItemId", "17"),
                    new TradePokemonFieldUpdateDto(0, "move1Id", "45"),
                    new TradePokemonFieldUpdateDto(0, "ivHp", "31"),
                ]),
            "request-za-trade-pokemon-update");
        AssertSuccess(update);
        var trade = Assert.Single(update.Payload!.Workflow.Trades);
        Assert.Equal(2, trade.SpeciesId);
        Assert.Equal("Ivysaur", trade.Species);
        Assert.Contains("Ivysaur", trade.Label);
        Assert.DoesNotContain("Bulbasaur", trade.Label);
        Assert.Equal(18, trade.Level);
        Assert.Equal(17, trade.HeldItemId);
        Assert.Equal(45, trade.Moves[0].MoveId);
        Assert.Equal(31, trade.Ivs.HP);
        Assert.Contains("Fixed IVs", trade.IvSummary);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-trade-pokemon-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.PokemonDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-trade-pokemon-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadGiftPokemonData(temp, "sub_tradepoke_bulbasaur");
        Assert.Equal(2, written.DevNo);
        Assert.Equal(18, written.MinLevel);
        Assert.Equal(18, written.MaxLevel);
        Assert.Equal(17, written.HoldItem!.Value.HoldItem);
        Assert.Equal(45, written.WazaList!.Value.Waza1);
        Assert.Equal(2, written.TalentScale);
        Assert.Equal(31, written.TalentValue!.Value.Hp);

        var gift = ReadGiftPokemonData(temp, "main_init_poke_1");
        Assert.Equal(0, gift.MinLevel);
        Assert.Equal(0, gift.MaxLevel);
    }

    [Fact]
    public void PokemonLegendsZALoadsSignedDefaultPokemonDataFields()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.TrainerDataArray, CreateTrainerDataArray(signedDefaults: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonDataArray, CreatePokemonDataArray(signedDefaults: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.EncountDataArray, CreatePokemonDataArray(signedDefaults: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonSpawnerDataArray, CreatePokemonSpawnerDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.MoveDataArray, CreateMoveDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonNames("English"),
            CreatePokemonNameTextTable());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerNames("English"),
            CreateTextTable(0, (0, "Rival Aria")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerTypes("English"),
            CreateTextTable(1, (1, "Duelist")));
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(paths),
            "request-za-trainers-signed-defaults");
        AssertSuccess(trainers);
        var trainerPokemon = Assert.Single(Assert.Single(trainers.Payload!.Workflow.Trainers).Team);
        Assert.Equal(-1, trainerPokemon.Gender);
        Assert.Equal("Game default / random", trainerPokemon.GenderLabel);
        Assert.Equal(33, trainerPokemon.MoveIds[0]);
        Assert.Equal("Tackle", trainerPokemon.Moves[0]);
        Assert.Equal(-1, trainerPokemon.Ivs.HP);
        Assert.Contains(trainers.Payload.Workflow.EditableFields, field => field.Field == "gender" && field.MinimumValue == -1);
        Assert.Contains(trainers.Payload.Workflow.EditableFields, field => field.Field == "move1Id" && field.MinimumValue == 0);
        Assert.Contains(trainers.Payload.Workflow.EditableFields, field => field.Field == "ivHp" && field.MinimumValue == -1);

        var gifts = Dispatch<LoadGiftPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGiftPokemonWorkflow,
            new LoadGiftPokemonWorkflowRequest(paths),
            "request-za-gifts-signed-defaults");
        AssertSuccess(gifts);
        var gift = Assert.Single(gifts.Payload!.Workflow.Gifts);
        Assert.Equal(-1, gift.Gender);
        Assert.Equal("Game default / random", gift.GenderLabel);
        Assert.Equal(-1, gift.Moves[0].MoveId);
        Assert.Null(gift.Moves[0].Move);
        Assert.Equal(-1, gift.SpecialMoveId);
        Assert.Contains(gifts.Payload.Workflow.EditableFields, field => field.Field == "gender" && field.MinimumValue == -1);
        Assert.Contains(gifts.Payload.Workflow.EditableFields, field => field.Field == "move1Id" && field.MinimumValue == -1);

        var trades = Dispatch<LoadTradePokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTradePokemonWorkflow,
            new LoadTradePokemonWorkflowRequest(paths),
            "request-za-trades-signed-defaults");
        AssertSuccess(trades);
        var trade = Assert.Single(trades.Payload!.Workflow.Trades);
        Assert.Equal(-1, trade.Gender);
        Assert.Equal("Game default / random", trade.GenderLabel);
        Assert.Equal(-1, trade.Moves[0].MoveId);
        Assert.Null(trade.Moves[0].Move);
        Assert.Contains(trades.Payload.Workflow.EditableFields, field => field.Field == "gender" && field.MinimumValue == -1);
        Assert.Contains(trades.Payload.Workflow.EditableFields, field => field.Field == "move1Id" && field.MinimumValue == -1);

        var staticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-za-static-signed-defaults");
        AssertSuccess(staticEncounters);
        var staticEncounter = Assert.Single(staticEncounters.Payload!.Workflow.Encounters);
        Assert.Equal(-1, staticEncounter.Gender);
        Assert.Equal("Game default / random", staticEncounter.GenderLabel);
        Assert.Equal(-1, staticEncounter.Moves[0].MoveId);
        Assert.Null(staticEncounter.Moves[0].Move);
        Assert.Equal("-1", staticEncounter.FieldValues["move0Id"]);
        Assert.Equal("-1 None", staticEncounter.FieldDisplayValues["move0Id"]);
        Assert.Contains(staticEncounters.Payload.Workflow.EditableFields, field => field.Field == "gender" && field.MinimumValue == -1);
        Assert.Contains(staticEncounters.Payload.Workflow.EditableFields, field => field.Field == "move0Id" && field.MinimumValue == -1);
    }

    [Fact]
    public void PokemonLegendsZANonPokemonSpeciesPickersExcludeUnavailablePokemon()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTrainerFixture(temp);
        WriteStaticEncounterFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(paths),
            "request-za-present-trainer-options");
        AssertSuccess(trainers);
        AssertZaSpeciesPickerOptions(trainers.Payload!.Workflow.EditableFields
            .Single(field => field.Field == "speciesId").Options.Select(option => option.Value));

        var encounters = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-present-wild-options");
        AssertSuccess(encounters);
        AssertZaSpeciesPickerOptions(encounters.Payload!.Workflow.EditableFields
            .Single(field => field.Field == "speciesId").Options.Select(option => option.Value));

        var staticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-za-present-static-options");
        AssertSuccess(staticEncounters);
        AssertZaSpeciesPickerOptions(staticEncounters.Payload!.Workflow.EditableFields
            .Single(field => field.Field == "species").Options.Select(option => option.Value));

        var gifts = Dispatch<LoadGiftPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGiftPokemonWorkflow,
            new LoadGiftPokemonWorkflowRequest(paths),
            "request-za-present-gift-options");
        AssertSuccess(gifts);
        AssertZaSpeciesPickerOptions(gifts.Payload!.Workflow.EditableFields
            .Single(field => field.Field == "species").Options.Select(option => option.Value));

        var trades = Dispatch<LoadTradePokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTradePokemonWorkflow,
            new LoadTradePokemonWorkflowRequest(paths),
            "request-za-present-trade-options");
        AssertSuccess(trades);
        AssertZaSpeciesPickerOptions(trades.Payload!.Workflow.EditableFields
            .Single(field => field.Field == "species").Options.Select(option => option.Value));
    }

    [Fact]
    public void PokemonLegendsZAUnavailableSpeciesUpdatesAreRejectedOutsidePokemonEditor()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTrainerFixture(temp);
        WriteStaticEncounterFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var trainerUpdate = Dispatch<UpdateTrainerFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerFields,
            new UpdateTrainerFieldsRequest(
                paths,
                Session: null,
                [new TrainerFieldUpdateDto(0, 0, "speciesId", "3")]),
            "request-za-unavailable-trainer");
        AssertSuccess(trainerUpdate);
        AssertUnavailableZaSpeciesDiagnostic(trainerUpdate.Payload!.Diagnostics);

        var giftUpdate = Dispatch<UpdateGiftPokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateGiftPokemonFields,
            new UpdateGiftPokemonFieldsRequest(
                paths,
                Session: null,
                [new GiftPokemonFieldUpdateDto(0, "species", "3")]),
            "request-za-unavailable-gift");
        AssertSuccess(giftUpdate);
        AssertUnavailableZaSpeciesDiagnostic(giftUpdate.Payload!.Diagnostics);

        var tradeUpdate = Dispatch<UpdateTradePokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonFields,
            new UpdateTradePokemonFieldsRequest(
                paths,
                Session: null,
                [new TradePokemonFieldUpdateDto(0, "species", "3")]),
            "request-za-unavailable-trade");
        AssertSuccess(tradeUpdate);
        AssertUnavailableZaSpeciesDiagnostic(tradeUpdate.Payload!.Diagnostics);

        var encounters = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-unavailable-wild-load");
        AssertSuccess(encounters);
        var table = Assert.Single(encounters.Payload!.Workflow.Tables);
        var slot = Assert.Single(table.Slots);
        var encounterUpdate = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(paths, Session: null, table.TableId, slot.Slot, "speciesId", "3"),
            "request-za-unavailable-wild");
        AssertSuccess(encounterUpdate);
        AssertUnavailableZaSpeciesDiagnostic(encounterUpdate.Payload!.Diagnostics);

        var staticEncounters = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-za-unavailable-static-load");
        AssertSuccess(staticEncounters);
        var staticEncounter = Assert.Single(staticEncounters.Payload!.Workflow.Encounters);
        var staticUpdate = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(paths, Session: null, staticEncounter.EncounterIndex, "species", "3"),
            "request-za-unavailable-static");
        AssertSuccess(staticUpdate);
        AssertUnavailableZaSpeciesDiagnostic(staticUpdate.Payload!.Diagnostics);
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersEditWritesTrinityEncountDataTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounters-load");

        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        Assert.Equal("Wild Encounters", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        var table = Assert.Single(workflow.Tables);
        Assert.Equal("Pokemon Legends ZA", table.GameVersion);
        Assert.Equal("Wild Zone 1", table.Location);
        Assert.Equal("a0102_w01", table.LocationKey);
        Assert.Equal(1, table.LocationSort);
        Assert.Equal("Spawner 1", table.TableLabel);
        Assert.Contains("Bulbasaur", table.TableDetails);
        Assert.EndsWith(ZaDataPaths.PokemonSpawnerDataArray, table.Provenance.SourceFile, StringComparison.Ordinal);
        var slot = Assert.Single(table.Slots);
        Assert.Equal(1, slot.SpeciesId);
        Assert.Equal("Bulbasaur", slot.Species);
        Assert.Equal(20, slot.LevelMin);
        Assert.Equal(20, slot.LevelMax);
        Assert.Equal(35, slot.Weight);
        Assert.Equal("Night", slot.TimeOfDay);
        Assert.Equal("Rain", slot.Weather);
        Assert.Equal("wild_ignore", slot.EncounterDataId);
        Assert.Equal("Wild", slot.EncounterKind);
        Assert.False(slot.IsAlpha);
        Assert.Contains(workflow.EditableFields, field => field.Field == "speciesId" && field.Label == "Species");
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == "probability");

        var slotUpdate = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                Session: null,
                [
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "speciesId", "2"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "levelMin", "25"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "levelMax", "30"),
                ]),
            "request-za-encounters-slot-fields");
        AssertSuccess(slotUpdate);
        var updatedSlot = Assert.Single(slotUpdate.Payload!.Workflow.Tables.Single().Slots);
        Assert.Equal(2, updatedSlot.SpeciesId);
        Assert.Equal("Ivysaur", updatedSlot.Species);
        Assert.Equal(25, updatedSlot.LevelMin);
        Assert.Equal(30, updatedSlot.LevelMax);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, slotUpdate.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.EncountDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, slotUpdate.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadEncountData(temp, "wild_ignore");
        Assert.Equal(2, written.DevNo);
        Assert.Equal(25, written.MinLevel);
        Assert.Equal(30, written.MaxLevel);
        Assert.DoesNotContain(ZaDataPaths.PokemonDataArray, apply.Payload.ApplyResult.WrittenFiles);
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersDescribeAlphaAndRawSpawnerLocations()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp, includeAlphaAndRawSpawners: true);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounters-alpha-raw-load");

        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        Assert.Equal(4, workflow.Tables.Count);

        var alphaTable = workflow.Tables.Single(table => table.Slots.Any(slot => slot.IsAlpha));
        Assert.Equal("Wild Zone 1", alphaTable.Location);
        Assert.Equal("Spawner 2", alphaTable.TableLabel);
        Assert.Contains("Alpha", alphaTable.TableDetails);
        var alphaSlot = Assert.Single(alphaTable.Slots);
        Assert.True(alphaSlot.IsAlpha);
        Assert.Equal("Alpha", alphaSlot.EncounterKind);
        Assert.Equal("wild_ignore_Alpha", alphaSlot.EncounterDataId);
        Assert.Equal(1, alphaSlot.SpeciesId);
        Assert.Equal("Bulbasaur", alphaSlot.Species);

        var rawTables = workflow.Tables.Where(table => table.LocationKey == "zdm406_v00").ToArray();
        Assert.Equal(2, rawTables.Length);
        Assert.All(rawTables, table => Assert.Equal("Dungeon ZDM406 V00", table.Location));
        Assert.Contains(rawTables, table => table.TableLabel == "Dungeon ZDM406 V00 Spawn 700");
        Assert.Contains(rawTables, table => table.TableLabel == "Dungeon ZDM406 V00 Spawn 701");
        Assert.All(rawTables, table => Assert.Equal("Wild", Assert.Single(table.Slots).EncounterKind));
    }

    [Fact]
    public void PokemonLegendsZAPlacementEditWritesSpawnerTransformTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WritePlacementFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-za-placement-load");

        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        Assert.Equal("Placement", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Objects.Count);
        Assert.Contains(workflow.Categories!, category => category.Id == "pokemonSpawners" && category.ObjectCount == 1);
        Assert.Contains(workflow.Categories!, category => category.Id == "itemBallSpawners" && category.ObjectCount == 1);
        Assert.Contains(workflow.EditableFields, field => field.Field == "point.positionX" && field.Label == "Position X");
        Assert.Contains(workflow.EditableFields, field => field.Field == "point.attachTransformEnable" && field.Label == "Attach Transform");

        var pokemonSpawner = workflow.Objects.Single(placedObject => placedObject.Label == "wild_spawn_001");
        Assert.Equal("pokemonSpawners", pokemonSpawner.CategoryId);
        Assert.Equal("Pokemon Spawner", pokemonSpawner.ObjectType);
        Assert.Equal("a0102_w01", pokemonSpawner.Map);
        Assert.Equal(1, pokemonSpawner.X);
        Assert.Equal(45, pokemonSpawner.RotationY);
        Assert.EndsWith(ZaDataPaths.PokemonSpawnerTransformArray, pokemonSpawner.Provenance.SourceFile, StringComparison.Ordinal);
        Assert.Contains(pokemonSpawner.Fields!, field => field.Field == "spawner.id" && field.DisplayValue == "za_wild_spawner_001");
        Assert.Contains(pokemonSpawner.Fields!, field => field.Field == "spawner.encounterRows" && field.DisplayValue == "1");

        var update = Dispatch<UpdatePlacementObjectFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdatePlacementObjectFields,
            new UpdatePlacementObjectFieldsRequest(
                paths,
                Session: null,
                [
                    new PlacementObjectFieldUpdateDto(pokemonSpawner.ObjectId, "point.positionX", "9.5"),
                    new PlacementObjectFieldUpdateDto(pokemonSpawner.ObjectId, "point.rotationYaw", "180"),
                    new PlacementObjectFieldUpdateDto(pokemonSpawner.ObjectId, "point.attachTransformEnable", "0"),
                ]),
            "request-za-placement-update");
        AssertSuccess(update);
        Assert.DoesNotContain(update.Payload!.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var updatedObject = update.Payload.Workflow.Objects.Single(placedObject => placedObject.ObjectId == pokemonSpawner.ObjectId);
        Assert.Equal(9.5, updatedObject.X);
        Assert.Equal(180, updatedObject.RotationY);
        Assert.Contains(updatedObject.Fields!, field => field.Field == "point.attachTransformEnable" && field.DisplayValue == "No");

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-placement-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.PokemonSpawnerTransformArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-placement-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(ZaDataPaths.PokemonSpawnerTransformArray, apply.Payload.ApplyResult.WrittenFiles);

        var written = ReadSpawnerTransformRow(temp, ZaDataPaths.PokemonSpawnerTransformArray, "wild_spawn_001");
        Assert.Equal(9.5f, written.Position.X);
        Assert.Equal(2, written.Position.Y);
        Assert.Equal(3, written.Position.Z);
        Assert.Equal(180, written.Rotation.Y);
        Assert.False(written.AttachTransformEnable);
        Assert.DoesNotContain(ZaDataPaths.ItemBallSpawnerTransformArray, apply.Payload.ApplyResult.WrittenFiles);
    }

    [Fact]
    public void PokemonLegendsZAStaticEncountersEditWritesTrinityEncountDataTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteStaticEncounterFixture(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadStaticEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(paths),
            "request-za-static-encounters-load");

        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        Assert.Equal("za", workflow.EditorFamily);
        Assert.Equal("Static Encounters", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        var encounter = Assert.Single(workflow.Encounters);
        Assert.Equal("encounterData", encounter.CategoryId);
        Assert.Equal("Encounter Data", encounter.CategoryLabel);
        Assert.Equal("static_event_ivysaur", encounter.EncounterId);
        Assert.Equal(2, encounter.SpeciesId);
        Assert.Equal("Ivysaur", encounter.Species);
        Assert.Equal(35, encounter.Level);
        Assert.Equal(17, encounter.HeldItemId);
        Assert.Equal("Potion", encounter.HeldItem);
        Assert.Contains(encounter.SupportedFields, field => field == "species");
        Assert.Contains(workflow.EditableFields, field => field.Field == "move0Id" && field.Label == "Move 1");

        var speciesUpdate = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(paths, Session: null, encounter.EncounterIndex, "species", "1"),
            "request-za-static-encounters-species");
        AssertSuccess(speciesUpdate);
        var levelUpdate = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(paths, speciesUpdate.Payload!.Session, encounter.EncounterIndex, "level", "42"),
            "request-za-static-encounters-level");
        AssertSuccess(levelUpdate);
        var moveUpdate = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(paths, levelUpdate.Payload!.Session, encounter.EncounterIndex, "move0Id", "45"),
            "request-za-static-encounters-move");
        AssertSuccess(moveUpdate);
        var updatedEncounter = Assert.Single(moveUpdate.Payload!.Workflow.Encounters);
        Assert.Equal(1, updatedEncounter.SpeciesId);
        Assert.Equal("Bulbasaur", updatedEncounter.Species);
        Assert.Contains("Bulbasaur", updatedEncounter.Label);
        Assert.DoesNotContain("Ivysaur", updatedEncounter.Label);
        Assert.Equal(42, updatedEncounter.Level);
        Assert.Equal(45, updatedEncounter.Moves[0].MoveId);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, moveUpdate.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-static-encounters-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.EncountDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, moveUpdate.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-static-encounters-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadEncountData(temp, "static_event_ivysaur");
        Assert.Equal(1, written.DevNo);
        Assert.Equal(42, written.MinLevel);
        Assert.Equal(42, written.MaxLevel);
        Assert.Equal(45, written.WazaList!.Value.Waza1);
        var wild = ReadEncountData(temp, "wild_ignore");
        Assert.Equal(1, wild.DevNo);
        Assert.Equal(20, wild.MinLevel);
    }

    [Fact]
    public void PokemonLegendsZAModMergerStagesAndAppliesTrinityRomFsMods()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile("arc/data.trpfd", CreateTrinityDescriptor([ModMergerDataVirtualPath]));
        temp.WriteBaseRomFsFile(ModMergerDataVirtualPath, [0, 0, 0, 0]);
        var firstMod = CreateZaFolderMod(temp, "first-mod", [1, 0, 0, 0]);
        var secondMod = CreateZaFolderMod(temp, "second-mod", [0, 0, 2, 0]);
        var paths = CreatePaths(temp);
        var sources = new[]
        {
            new ZaModMergerSourceDto(firstMod, IsEnabled: true),
            new ZaModMergerSourceDto(secondMod, IsEnabled: true),
        };
        var dispatcher = new ProjectBridgeDispatcher();

        var load = Dispatch<LoadZaModMergerWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadZaModMergerWorkflow,
            new LoadZaModMergerWorkflowRequest(paths, sources),
            "request-za-mod-merger-load");
        AssertSuccess(load);
        Assert.Equal("Mod Merger", load.Payload!.Workflow.Summary.Label);
        Assert.Equal(2, load.Payload.Workflow.Sources.Count);
        Assert.Equal(2, load.Payload.Workflow.Stats.SourceCount);
        Assert.Equal(1, load.Payload.Workflow.Stats.OutputFileCount);

        var stage = Dispatch<StageZaModMergeResponse>(
            dispatcher,
            KmCommandNames.StageZaModMerge,
            new StageZaModMergeRequest(paths, sources),
            "request-za-mod-merger-stage");
        AssertSuccess(stage);
        Assert.True(stage.Payload!.Preview.CanApply);
        Assert.Equal("ready", stage.Payload.Preview.Status);
        var stagedFile = Assert.Single(stage.Payload.Preview.Files);
        Assert.Equal(ModMergerDataOutputPath, stagedFile.RelativePath);
        Assert.Equal("smartMerge", stagedFile.MergeKind);

        var apply = Dispatch<ApplyZaModMergeResponse>(
            dispatcher,
            KmCommandNames.ApplyZaModMerge,
            new ApplyZaModMergeRequest(paths, sources),
            "request-za-mod-merger-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(ModMergerDataOutputPath, apply.Payload.WrittenFiles);
        Assert.Contains(ModMergerDescriptorOutputPath, apply.Payload.WrittenFiles);
        Assert.Equal([1, 0, 2, 0], ReadZaOutputBytes(temp, ModMergerDataOutputPath));
        AssertDescriptorRemovedZaModFile(temp);
    }

    private static TemporaryBridgeProject CreatePokemonLegendsZAProject()
    {
        var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(PokemonLegendsZATitleId));
        temp.WriteBaseRomFsFile("arc/data.trpfd", []);
        temp.WriteBaseRomFsFile("arc/data.trpfs", []);
        temp.EnsurePokemonLegendsZASupportFolder();
        return temp;
    }

    private static ProjectPathsDto CreatePaths(TemporaryBridgeProject temp)
    {
        return temp.Paths with
        {
            SelectedGame = ProjectGameDto.ZA,
            PokemonLegendsZASupportFolderPath = temp.PokemonLegendsZASupportFolderPath,
        };
    }

    private static string CreateZaFolderMod(TemporaryBridgeProject temp, string name, byte[] bytes)
    {
        var root = Path.Combine(temp.RootPath, name);
        var path = Path.Combine(root, "romfs", "bin", "mock", "data.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return root;
    }

    private static byte[] ReadZaOutputBytes(TemporaryBridgeProject temp, string relativePath)
    {
        return File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void AssertDescriptorRemovedZaModFile(TemporaryBridgeProject temp)
    {
        var descriptorPath = Path.Combine(temp.OutputRootPath, "romfs", "arc", "data.trpfd");
        Assert.True(File.Exists(descriptorPath));

        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(File.ReadAllBytes(descriptorPath)));
        var activeHashes = Enumerable
            .Range(0, descriptor.FileHashesLength)
            .Select(descriptor.FileHashes)
            .ToHashSet();

        Assert.DoesNotContain(ZaTrinityPathHasher.HashPath(ModMergerDataVirtualPath), activeHashes);
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var npdm = new byte[ZaNpdmTitleIdOffset + sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(ZaNpdmTitleIdOffset), titleId);
        return npdm;
    }

    private static byte[] CreateTrinityDescriptor(IReadOnlyList<string> virtualPaths)
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            virtualPaths.Select(ZaTrinityPathHasher.HashPath).ToArray());
        var fileEntries = virtualPaths
            .Select(_ => FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: 0))
            .ToArray();
        var files = FileDescriptor.CreateFilesVector(builder, fileEntries);
        var pack = PackDescriptorEntry.CreatePackDescriptorEntry(
            builder,
            file_size: 123,
            file_count: checked((ulong)virtualPaths.Count));
        var packs = FileDescriptor.CreatePacksVector(builder, [pack]);
        var root = FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreatePersonalArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var empty = CreatePersonal(builder, species: 0, present: false, hp: 0, zaDexOrder: 0);
        var bulbasaur = CreatePersonal(builder, species: 1, present: true, hp: 45, zaDexOrder: 25);
        var ivysaur = CreatePersonal(builder, species: 2, present: true, hp: 60, zaDexOrder: 26);
        var charmander = CreatePersonal(builder, species: 3, present: false, hp: 39, zaDexOrder: 0);
        var vector = ZaPersonalTable.CreateEntryVector(builder, [empty, bulbasaur, ivysaur, charmander]);
        ZaPersonalTable.Start(builder);
        ZaPersonalTable.AddEntry(builder, vector);
        var root = ZaPersonalTable.End(builder);
        ZaPersonalTable.FinishBuffer(builder, root);
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
        var vector = ZaMoveDataArray.CreateValuesVector(builder, [tackle, growl]);
        var root = ZaMoveDataArray.CreateZaMoveDataArray(builder, vector);
        ZaMoveDataArray.FinishZaMoveDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateItemDataArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var pokeBall = CreateItem(
            builder,
            itemId: 4,
            itemType: 4,
            internalName: "MONSTERBALL",
            iconName: "item_0004",
            price: 100,
            pocket: 1,
            stackCap: 999,
            sortOrder: 1);
        var potion = CreateItem(
            builder,
            itemId: 17,
            itemType: 2,
            internalName: "KIZUGUSURI",
            iconName: "item_0017",
            price: 150,
            pocket: 2,
            stackCap: 999,
            sortOrder: 1,
            healPower: 20,
            canUseInBattle: true);
        var tm = CreateItem(
            builder,
            itemId: 328,
            itemType: 5,
            internalName: "WAZAMASIN01",
            iconName: "item_0332",
            price: 1000,
            pocket: 6,
            stackCap: 1,
            sortOrder: 1,
            machineMoveId: 33,
            machineIndex: -1);
        var vector = ZaItemDataArray.CreateValuesVector(builder, [pokeBall, potion, tm]);
        var root = ZaItemDataArray.CreateZaItemDataArray(builder, vector);
        ZaItemDataArray.FinishZaItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static void WriteTrainerFixture(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(ZaDataPaths.TrainerDataArray, CreateTrainerDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.MoveDataArray, CreateMoveDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerNames("English"),
            CreateTextTable(0, (0, "Rival Aria")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerTypes("English"),
            CreateTextTable(1, (1, "Duelist")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonNames("English"),
            CreatePokemonNameTextTable());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.AbilityNames("English"),
            CreateTextTable(65, (34, "Chlorophyll"), (65, "Overgrow")));
    }

    private static void WriteGiftPokemonFixture(TemporaryBridgeProject temp, bool gameDefaultGift = false)
    {
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonDataArray, CreatePokemonDataArray(gameDefaultGift: gameDefaultGift));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.MoveDataArray, CreateMoveDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonNames("English"),
            CreatePokemonNameTextTable());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.AbilityNames("English"),
            CreateTextTable(65, (34, "Chlorophyll"), (65, "Overgrow")));
    }

    private static void WriteTradePokemonFixture(TemporaryBridgeProject temp)
    {
        WriteGiftPokemonFixture(temp);
    }

    private static void WriteWildEncounterFixture(
        TemporaryBridgeProject temp,
        bool includeAlphaAndRawSpawners = false)
    {
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonDataArray, CreatePokemonDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.EncountDataArray, CreatePokemonDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonSpawnerDataArray,
            CreatePokemonSpawnerDataArray(includeAlphaAndRawSpawners));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonNames("English"),
            CreatePokemonNameTextTable());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PlaceNames("English"),
            CreateTextTable(0, (0, "Wild Zone 1")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PlaceNameKeys("English"),
            CreateKeyTable((0, "PLACENAME_wild_a0102_w01")));
    }

    private static void WriteStaticEncounterFixture(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonDataArray, CreatePokemonDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.EncountDataArray, CreatePokemonDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonSpawnerDataArray, CreatePokemonSpawnerDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonNames("English"),
            CreatePokemonNameTextTable());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (328, "TM001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
    }

    private static void WritePlacementFixture(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonSpawnerDataArray, CreatePokemonSpawnerDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonSpawnerTransformArray,
            CreateSpawnerTransformArray(
                "wild_spawn_001",
                positionX: 1,
                positionY: 2,
                positionZ: 3,
                rotationX: 0,
                rotationY: 45,
                rotationZ: 0,
                attach: true));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemBallSpawnerTransformArray,
            CreateSpawnerTransformArray(
                "itemball_spawn_001",
                positionX: 4,
                positionY: 5,
                positionZ: 6,
                rotationX: 10,
                rotationY: 90,
                rotationZ: 20,
                attach: true));
    }

    private static byte[] CreatePokemonDataArray(bool signedDefaults = false, bool gameDefaultGift = false)
    {
        var builder = new FlatBufferBuilder(2048);
        var giftScene = CreatePokemonData(
            builder,
            "main_init_poke_1",
            level: 0,
            heldItem: 0,
            move1: -1,
            move2: -1,
            ivHp: -1,
            ivAttack: -1,
            sex: 0,
            ivDefense: -1,
            ivSpecialAttack: -1,
            ivSpecialDefense: -1,
            ivSpeed: -1,
            talentScale: 127,
            omitWazaList: true);
        var giftPlayable = CreatePokemonData(
            builder,
            "test_encount_init_poke_0",
            level: gameDefaultGift ? 0 : 5,
            heldItem: 4,
            move1: signedDefaults ? -1 : 33,
            move2: 45,
            ivHp: gameDefaultGift ? -1 : 31,
            ivAttack: gameDefaultGift ? -1 : 30,
            sex: signedDefaults ? -1 : 1,
            ivDefense: gameDefaultGift ? -1 : 29,
            ivSpecialAttack: gameDefaultGift ? -1 : 28,
            ivSpecialDefense: gameDefaultGift ? -1 : 27,
            ivSpeed: gameDefaultGift ? -1 : 26,
            talentScale: gameDefaultGift ? 127 : 2,
            omitWazaList: gameDefaultGift);
        var ignored = CreatePokemonData(
            builder,
            "wild_ignore",
            level: 20,
            heldItem: 0,
            move1: 33,
            move2: 0,
            ivHp: 1,
            ivAttack: 2);
        var trade = CreatePokemonData(
            builder,
            "sub_tradepoke_bulbasaur",
            level: 5,
            heldItem: 4,
            move1: signedDefaults ? -1 : 33,
            move2: 45,
            ivHp: 31,
            ivAttack: 30,
            sex: signedDefaults ? -1 : 1);
        var staticEncounter = CreatePokemonData(
            builder,
            "static_event_ivysaur",
            level: 35,
            heldItem: 17,
            move1: signedDefaults ? -1 : 33,
            move2: 45,
            ivHp: 10,
            ivAttack: 11,
            speciesId: 2,
            sex: signedDefaults ? -1 : 1);
        var rootVector = ZaPokemonDataDb.CreateRootVector(builder, [giftScene, ignored, trade, staticEncounter, giftPlayable]);
        var db = ZaPokemonDataDb.Create(builder, rootVector);
        var valuesVector = ZaPokemonDataDbArray.CreateValuesVector(builder, [db]);
        var root = ZaPokemonDataDbArray.Create(builder, valuesVector);
        ZaPokemonDataDbArray.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreatePokemonSpawnerDataArray(bool includeAlphaAndRawSpawners = false)
    {
        var builder = new FlatBufferBuilder(4096);
        var spawners = includeAlphaAndRawSpawners
            ? new[]
            {
                CreateSpawner(
                    builder,
                    "za_wild_spawner_001",
                    "wild_ignore",
                    "a0102_w01",
                    weight: 35,
                    appearanceObjectName: "wild_spawn_001"),
                CreateSpawner(builder, "id_spn_outzone_a0201_A459", "wild_ignore_Alpha", "a0102_w01", weight: 65),
                CreateSpawner(builder, "zdm406_v00_700", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "zdm406_v00_701", "wild_ignore", zoneId: null, weight: 100),
            }
            :
            [
                CreateSpawner(
                    builder,
                    "za_wild_spawner_001",
                    "wild_ignore",
                    "a0102_w01",
                    weight: 35,
                    appearanceObjectName: "wild_spawn_001"),
            ];
        var rootVector = PokemonSpawnerDataDB.CreateRootVector(builder, spawners);
        var db = PokemonSpawnerDataDB.CreatePokemonSpawnerDataDB(builder, rootVector);
        var valuesVector = PokemonSpawnerDataDBArray.CreateValuesVector(builder, [db]);
        var root = PokemonSpawnerDataDBArray.CreatePokemonSpawnerDataDBArray(builder, valuesVector);
        PokemonSpawnerDataDBArray.FinishPokemonSpawnerDataDBArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<PokemonSpawnerData> CreateSpawner(
        FlatBufferBuilder builder,
        string spawnerName,
        string encounterDataId,
        string? zoneId,
        int weight,
        string? appearanceObjectName = null)
    {
        var encounterId = builder.CreateString(encounterDataId);
        var encounter = EncountDataInfo.CreateEncountDataInfo(
            builder,
            encounterId,
            weight: weight,
            maxCount: 2,
            additionalLevel: 0,
            showMapIcon: 1,
            appearedTimeCondition: 4,
            appearedWeatherCondition: 2);
        var encounters = PokemonSpawnerData.CreateEncountDataInfoListVector(builder, [encounter]);
        var objectName = builder.CreateString(appearanceObjectName ?? $"{spawnerName}_object");
        var appearance = CreateSpawnerAppearance(builder, objectName, zoneId);
        var appearances = PokemonSpawnerData.CreateAppearanceSpawnerObjectInfoListVector(builder, [appearance]);
        var spawnerId = builder.CreateString(spawnerName);
        return PokemonSpawnerData.CreatePokemonSpawnerData(
            builder,
            spawnerId,
            appearanceSpawnerObjectInfoListOffset: appearances,
            encountDataInfoListOffset: encounters);
    }

    private static Offset<AppearanceSpawnerObjectInfo> CreateSpawnerAppearance(
        FlatBufferBuilder builder,
        StringOffset objectName,
        string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return AppearanceSpawnerObjectInfo.CreateAppearanceSpawnerObjectInfo(
                builder,
                objectNameOffset: objectName);
        }

        var zoneIdOffset = builder.CreateString(zoneId);
        var variationId = builder.CreateString(string.Empty);
        var zone = ZoneInfo.CreateZoneInfo(builder, zoneIdOffset, variationId);
        return AppearanceSpawnerObjectInfo.CreateAppearanceSpawnerObjectInfo(
            builder,
            objectNameOffset: objectName,
            zoneInfoOffset: zone);
    }

    private static byte[] CreateSpawnerTransformArray(
        string name,
        float positionX,
        float positionY,
        float positionZ,
        float rotationX,
        float rotationY,
        float rotationZ,
        bool attach)
    {
        var document = ZaSpawnerTransformDocument.Create(
        [
            new ZaSpawnerTransformGroup(
                0,
                [
                    new ZaSpawnerTransformRow(
                        0,
                        0,
                        name,
                        new ZaSpawnerTransformVector(positionX, positionY, positionZ),
                        new ZaSpawnerTransformVector(rotationX, rotationY, rotationZ),
                        attach),
                ]),
        ]);
        return document.Write();
    }

    private static Offset<ZaPokemonDataRow> CreatePokemonData(
        FlatBufferBuilder builder,
        string id,
        int level,
        int heldItem,
        int move1,
        int move2,
        int ivHp,
        int ivAttack,
        int speciesId = 1,
        int sex = 1,
        int ivDefense = 29,
        int ivSpecialAttack = 28,
        int ivSpecialDefense = 27,
        int ivSpeed = 26,
        int talentScale = 2,
        int talentVNum = 0,
        bool omitWazaList = false)
    {
        var idOffset = builder.CreateString(id);
        var talentValue = ZaPokemonDataTalentValue.Create(
            builder,
            hp: ivHp,
            atk: ivAttack,
            def: ivDefense,
            spAtk: ivSpecialAttack,
            spDef: ivSpecialDefense,
            agi: ivSpeed);
        Offset<ZaPokemonDataWazaList> wazaList = omitWazaList
            ? default
            : ZaPokemonDataWazaList.Create(builder, move1, move2, waza3: 0, waza4: 0);
        var holdItemOffset = ZaPokemonDataHoldItem.Create(builder, heldItem);
        return ZaPokemonDataRow.Create(
            builder,
            idOffset,
            devNo: speciesId,
            minLevel: level,
            maxLevel: level,
            sex: sex,
            formNo: 0,
            rare: ZaPokemonDataRareNotShiny,
            tokusei: 2,
            seikaku: 4,
            talentScale: talentScale,
            talentVNum: talentVNum,
            oyabunProbability: 0.25F,
            oyabunAdditionalLevel: 10,
            talentValueOffset: talentValue,
            wazaListOffset: wazaList,
            holdItemOffset: holdItemOffset);
    }

    private static byte[] CreateTrainerDataArray(
        bool signedDefaults = false,
        string trainerId = "tr_battle_main_001",
        ulong trainerType = 1,
        ulong trainerType2 = 0)
    {
        var builder = new FlatBufferBuilder(2048);
        var trainer = CreateTrainer(builder, signedDefaults, trainerId, trainerType, trainerType2);
        var vector = ZaTrainerTable.CreateValueVector(builder, [trainer]);
        var root = ZaTrainerTable.Create(builder, vector);
        ZaTrainerTable.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<ZaTrainerRow> CreateTrainer(
        FlatBufferBuilder builder,
        bool signedDefaults = false,
        string trainerIdValue = "tr_battle_main_001",
        ulong trainerType = 1,
        ulong trainerType2 = 0)
    {
        var trainerId = builder.CreateString(trainerIdValue);
        var pokemon = CreateTrainerPokemon(builder, signedDefaults);

        return ZaTrainerRow.Create(
            builder,
            trainerIdOffset: trainerId,
            trainerType: trainerType,
            trainerType2: trainerType2 == 0 ? trainerType : trainerType2,
            rank: 26,
            moneyRate: 4,
            megaEvolution: true,
            pokemon1Offset: pokemon,
            aiBasic: true,
            aiHigh: true,
            viewHorizontalAngle: 90,
            viewVerticalAngle: 30,
            viewRange: 12,
            hearingRange: 8);
    }

    private static Offset<ZaTrainerPokemon> CreateTrainerPokemon(FlatBufferBuilder builder, bool signedDefaults = false)
    {
        var move1 = ZaTrainerMove.Create(builder, 33);
        var move2 = ZaTrainerMove.Create(builder, 45);
        var ivs = signedDefaults
            ? ZaTrainerStats.Create(builder, hp: -1, atk: -1, def: -1, spAtk: -1, spDef: -1, agi: -1)
            : ZaTrainerStats.Create(builder, hp: 31, atk: 31, def: 31, spAtk: 31, spDef: 31, agi: 31);
        var evs = ZaTrainerStats.Create(builder, hp: 4, atk: 0, def: 0, spAtk: 0, spDef: 0, agi: 0);

        return ZaTrainerPokemon.Create(
            builder,
            speciesId: 1,
            formId: 0,
            sex: signedDefaults ? -1 : 1,
            item: 4,
            level: 12,
            ballId: 4,
            move1Offset: move1,
            move2Offset: move2,
            nature: 4,
            ability: 2,
            ivsOffset: ivs,
            evsOffset: evs);
    }

    private static byte[] CreateShopDataArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var shop = CreateShopData(
            builder,
            "a01_friendlyshop_01",
            "a01_friendlyshop_01_lineup1",
            "shop_friendlyshop_01",
            "msg_shop_friendly",
            2);
        var vector = ZaShopDataArray.CreateValuesVector(builder, [shop]);
        var root = ZaShopDataArray.CreateZaShopDataArray(builder, vector);
        ZaShopDataArray.FinishZaShopDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<ZaShopData> CreateShopData(
        FlatBufferBuilder builder,
        string shopId,
        string lineupId,
        string resourceLabel,
        string messageLabel,
        int shopKind)
    {
        var shopIdOffset = builder.CreateString(shopId);
        var lineupIdOffset = builder.CreateString(lineupId);
        var resourceLabelOffset = builder.CreateString(resourceLabel);
        var messageLabelOffset = builder.CreateString(messageLabel);

        ZaShopData.StartZaShopData(builder);
        ZaShopData.AddCondition(builder, -1);
        ZaShopData.AddShopKind(builder, shopKind);
        ZaShopData.AddMessageLabel(builder, messageLabelOffset);
        ZaShopData.AddResourceLabel(builder, resourceLabelOffset);
        ZaShopData.AddLineupId(builder, lineupIdOffset);
        ZaShopData.AddShopId(builder, shopIdOffset);
        return ZaShopData.EndZaShopData(builder);
    }

    private static byte[] CreateShopLineupArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var pokeBall = CreateShopInventory(builder, itemId: 4, displayIndex: 1, CreateForceShopCondition(builder));
        var potion = CreateShopInventory(builder, itemId: 17, displayIndex: 2, CreatePhaseShopCondition(builder, "30600"));
        var lineup = CreateShopLineup(builder, "a01_friendlyshop_01_lineup1", [pokeBall, potion]);
        var vector = ZaShopLineupArray.CreateValuesVector(builder, [lineup]);
        var root = ZaShopLineupArray.CreateZaShopLineupArray(builder, vector);
        ZaShopLineupArray.FinishZaShopLineupArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<ZaShopLineup> CreateShopLineup(
        FlatBufferBuilder builder,
        string name,
        Offset<ZaShopInventory>[] inventory)
    {
        var nameOffset = builder.CreateString(name);
        var inventoryVector = ZaShopLineup.CreateInventoryVector(builder, inventory);

        ZaShopLineup.StartZaShopLineup(builder);
        ZaShopLineup.AddInventory(builder, inventoryVector);
        ZaShopLineup.AddName(builder, nameOffset);
        return ZaShopLineup.EndZaShopLineup(builder);
    }

    private static Offset<ZaShopInventory> CreateShopInventory(
        FlatBufferBuilder builder,
        uint itemId,
        uint displayIndex,
        Offset<ZaShopInventoryCondition> condition)
    {
        var conditionsVector = ZaShopInventory.CreateConditionsVector(builder, [condition]);

        ZaShopInventory.StartZaShopInventory(builder);
        ZaShopInventory.AddConditions(builder, conditionsVector);
        ZaShopInventory.AddDisplayIndex(builder, displayIndex);
        ZaShopInventory.AddItem(builder, itemId);
        return ZaShopInventory.EndZaShopInventory(builder);
    }

    private static Offset<ZaShopInventoryCondition> CreateForceShopCondition(FlatBufferBuilder builder) =>
        CreateShopCondition(builder, "force_condition", 0, []);

    private static Offset<ZaShopInventoryCondition> CreatePhaseShopCondition(FlatBufferBuilder builder, string phase) =>
        CreateShopCondition(builder, "phase_condition", 5, [phase]);

    private static Offset<ZaShopInventoryCondition> CreateShopCondition(
        FlatBufferBuilder builder,
        string condition,
        uint comparison,
        string[] arguments)
    {
        var conditionOffset = builder.CreateString(condition);
        var argumentOffsets = arguments.Select(builder.CreateString).ToArray();
        var argumentsVector = ZaShopInventoryAppearCondition.CreateArgumentsVector(builder, argumentOffsets);
        ZaShopInventoryAppearCondition.StartZaShopInventoryAppearCondition(builder);
        ZaShopInventoryAppearCondition.AddArguments(builder, argumentsVector);
        ZaShopInventoryAppearCondition.AddComparison(builder, comparison);
        ZaShopInventoryAppearCondition.AddCondition(builder, conditionOffset);
        var appearCondition = ZaShopInventoryAppearCondition.EndZaShopInventoryAppearCondition(builder);

        var holderVector = ZaShopInventoryConditionHolder.CreateValuesVector(builder, [appearCondition]);
        ZaShopInventoryConditionHolder.StartZaShopInventoryConditionHolder(builder);
        ZaShopInventoryConditionHolder.AddValues(builder, holderVector);
        var holder = ZaShopInventoryConditionHolder.EndZaShopInventoryConditionHolder(builder);

        var conditionVector = ZaShopInventoryCondition.CreateValuesVector(builder, [holder]);
        ZaShopInventoryCondition.StartZaShopInventoryCondition(builder);
        ZaShopInventoryCondition.AddValues(builder, conditionVector);
        return ZaShopInventoryCondition.EndZaShopInventoryCondition(builder);
    }

    private static Offset<ZaItemData> CreateItem(
        FlatBufferBuilder builder,
        int itemId,
        int itemType,
        string internalName,
        string iconName,
        int price,
        int pocket,
        int stackCap,
        int sortOrder,
        ushort machineMoveId = 0,
        int machineIndex = 0,
        int healPower = 0,
        bool canUseInBattle = false)
    {
        var internalNameOffset = builder.CreateString(internalName);
        var iconNameOffset = builder.CreateString(iconName);
        ZaItemData.StartZaItemData(builder);
        ZaItemData.AddCanUseInBattle(builder, canUseInBattle);
        ZaItemData.AddWorkRecvPower(builder, healPower);
        ZaItemData.AddMachineIndex(builder, machineIndex);
        ZaItemData.AddMachineWaza(builder, machineMoveId);
        ZaItemData.AddSortNum(builder, sortOrder);
        ZaItemData.AddSlotMaxNum(builder, stackCap);
        ZaItemData.AddPocket(builder, pocket);
        ZaItemData.AddPrice(builder, price);
        ZaItemData.AddIconName(builder, iconNameOffset);
        ZaItemData.AddInternalName(builder, internalNameOffset);
        ZaItemData.AddItemType(builder, itemType);
        ZaItemData.AddId(builder, itemId);
        return ZaItemData.EndZaItemData(builder);
    }

    private static Offset<ZaMoveData> CreateMove(
        FlatBufferBuilder builder,
        ushort moveId,
        byte power,
        byte pp,
        bool makesContact,
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
        ZaMoveData.StartZaMoveData(builder);
        ZaMoveData.AddStatChanges(
            builder,
            ZaMoveStatChanges.CreateZaMoveStatChanges(
                builder,
                stat1,
                stat2,
                stat3,
                stat1Stage,
                stat2Stage,
                stat3Stage,
                stat1Chance,
                stat2Chance,
                stat3Chance));
        ZaMoveData.AddRawTarget(builder, 3);
        ZaMoveData.AddInflict(
            builder,
            ZaMoveInflict.CreateZaMoveInflict(builder, Condition: 0, Chance: 0, TurnMode: 0, TurnMin: 0, TurnMax: 0));
        ZaMoveData.AddPp(builder, pp);
        ZaMoveData.AddAccuracy(builder, 100);
        ZaMoveData.AddPower(builder, power);
        ZaMoveData.AddCategory(builder, category);
        ZaMoveData.AddType(builder, 0);
        ZaMoveData.AddCanUseMove(builder, true);
        ZaMoveData.AddMoveId(builder, moveId);
        ZaMoveData.AddFlagMakesContact(builder, makesContact);
        return ZaMoveData.EndZaMoveData(builder);
    }

    private static Offset<ZaPersonal> CreatePersonal(
        FlatBufferBuilder builder,
        ushort species,
        bool present,
        byte hp,
        byte zaDexOrder)
    {
        ZaPersonal.StartEvolutionsVector(builder, species == 0 ? 0 : 1);
        if (species != 0)
        {
            ZaEvolutionData.Create(builder, level: 16, condition: 4, parameter: 0, reserved3: 0, reserved4: 0, reserved5: 0, species: 1, form: 0);
        }

        var evolutions = builder.EndVector();
        var tmMoves = ZaPersonal.CreateUshortVector(builder, species == 0 ? [] : [(ushort)45]);
        var eggMoves = ZaPersonal.CreateUshortVector(builder, species == 0 ? [] : [(ushort)33]);
        var reminderMoves = ZaPersonal.CreateUshortVector(builder, species == 0 ? [] : [(ushort)36]);
        ZaPersonal.StartLevelupMovesVector(builder, species == 0 ? 0 : 1);
        if (species != 0)
        {
            ZaLevelUpMoveData.Create(builder, move: 33, level: 0x0A01);
        }

        var levelupMoves = builder.EndVector();

        ZaPersonal.Start(builder);
        ZaPersonal.AddLevelupMoves(builder, levelupMoves);
        ZaPersonal.AddReminderMoves(builder, reminderMoves);
        ZaPersonal.AddEggMoves(builder, eggMoves);
        ZaPersonal.AddTmMoves(builder, tmMoves);
        ZaPersonal.AddEvolutions(builder, evolutions);
        ZaPersonal.AddBaseStats(builder, ZaStatInfo.Create(builder, hp, atk: 49, def: 49, spa: 65, spd: 65, spe: 45));
        ZaPersonal.AddEvYield(builder, ZaStatInfo.Create(builder, hp: 0, atk: 0, def: 0, spa: 1, spd: 0, spe: 0));
        ZaPersonal.AddEvoStage(builder, 0);
        ZaPersonal.AddBaseFriendship(builder, 70);
        ZaPersonal.AddEggHatchCycles(builder, 20);
        ZaPersonal.AddEggHatch(builder, ZaEggHatchInfo.Create(builder, species, form: 0, formFlags: 0, formEverstone: 0));
        ZaPersonal.AddEggGroup2(builder, 7);
        ZaPersonal.AddEggGroup1(builder, 1);
        ZaPersonal.AddGender(builder, ZaGenderInfo.Create(builder, group: 0, ratio: 31));
        ZaPersonal.AddCatchRate(builder, 45);
        ZaPersonal.AddXpGrowth(builder, 3);
        ZaPersonal.AddAbilityHidden(builder, 34);
        ZaPersonal.AddAbility2(builder, 65);
        ZaPersonal.AddAbility1(builder, 65);
        ZaPersonal.AddType2(builder, 3);
        ZaPersonal.AddType1(builder, 11);
        ZaPersonal.AddZADexOrder(builder, zaDexOrder);
        ZaPersonal.AddIsPresent(builder, present);
        ZaPersonal.AddSpecies(builder, ZaSpeciesInfo.Create(builder, species, form: 0, model: species, color: 3, bodyType: 1, height: 7, weight: 69, reserved: 0, reserved1: 0, reserved2: 0));
        return ZaPersonal.End(builder);
    }

    private static ZaMoveData ReadMove(TemporaryBridgeProject temp, int moveId)
    {
        var outputPath = Path.Combine(temp.OutputRootPath, "avalon", "data", "waza_array.bin");
        Assert.True(File.Exists(outputPath));
        var table = ZaMoveDataArray.GetRootAsZaMoveDataArray(new ByteBuffer(File.ReadAllBytes(outputPath)));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null && row.Value.MoveId == moveId)
            {
                return row.Value;
            }
        }

        throw new InvalidOperationException($"Move {moveId} was not written.");
    }

    private static ZaItemData ReadItem(TemporaryBridgeProject temp, int itemId)
    {
        var outputPath = Path.Combine(temp.OutputRootPath, "world", "exl", "item_data", "item_data", "item_data.bin");
        Assert.True(File.Exists(outputPath));
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(File.ReadAllBytes(outputPath)));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null && row.Value.Id == itemId)
            {
                return row.Value;
            }
        }

        throw new InvalidOperationException($"Item {itemId} was not written.");
    }

    private static ZaShopLineup ReadShopLineup(TemporaryBridgeProject temp, string lineupId)
    {
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "world",
            "exl",
            "shop",
            "shop_item_lineup",
            "shop_item_lineup.bin");
        Assert.True(File.Exists(outputPath));
        var table = ZaShopLineupArray.GetRootAsZaShopLineupArray(new ByteBuffer(File.ReadAllBytes(outputPath)));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null && row.Value.Name == lineupId)
            {
                return row.Value;
            }
        }

        throw new InvalidOperationException($"Shop lineup {lineupId} was not written.");
    }

    private static ZaTrainerRow ReadTrainer(TemporaryBridgeProject temp, int trainerId)
    {
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "world",
            "ik_data",
            "trainer",
            "trdata",
            "trdata_array.bin");
        Assert.True(File.Exists(outputPath));
        var table = ZaTrainerTable.GetRootAsZaTrainerTable(new ByteBuffer(File.ReadAllBytes(outputPath)));
        var row = table.Value(trainerId);
        Assert.NotNull(row);
        return row!.Value;
    }

    private static ZaPokemonDataRow ReadGiftPokemonData(TemporaryBridgeProject temp, string id)
    {
        return ReadPokemonLikeData(temp, ZaDataPaths.PokemonDataArray, id, "PokemonData");
    }

    private static ZaPokemonDataRow ReadEncountData(TemporaryBridgeProject temp, string id)
    {
        return ReadPokemonLikeData(temp, ZaDataPaths.EncountDataArray, id, "EncountData");
    }

    private static ZaPokemonDataRow ReadPokemonLikeData(
        TemporaryBridgeProject temp,
        string relativePath,
        string id,
        string label)
    {
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(outputPath));
        var table = ZaPokemonDataDbArray.GetRootAsZaPokemonDataDbArray(new ByteBuffer(File.ReadAllBytes(outputPath)));
        for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
        {
            var group = table.Values(groupIndex);
            if (group is null)
            {
                continue;
            }

            for (var rowIndex = 0; rowIndex < group.Value.RootLength; rowIndex++)
            {
                var row = group.Value.Root(rowIndex);
                if (row is not null && row.Value.Id == id)
                {
                    return row.Value;
                }
            }
        }

        throw new InvalidOperationException($"{label} row {id} was not written.");
    }

    private static ZaSpawnerTransformRow ReadSpawnerTransformRow(
        TemporaryBridgeProject temp,
        string relativePath,
        string objectName)
    {
        var document = ZaSpawnerTransformDocument.Parse(ReadZaOutputBytes(temp, relativePath));
        var row = document.Groups
            .SelectMany(group => group.Rows)
            .SingleOrDefault(candidate => candidate.Name == objectName);

        return row ?? throw new InvalidOperationException($"Spawner transform row {objectName} was not written.");
    }

    private static byte[] CreateTextTable(int count, params (int Index, string Text)[] entries)
    {
        var values = Enumerable.Repeat(string.Empty, count + 1).ToArray();
        foreach (var entry in entries)
        {
            values[entry.Index] = entry.Text;
        }

        var lines = values
            .Select(value => new SwShGameTextLine(value, Flags: 0))
            .ToArray();
        return SwShGameTextFile.Write(lines);
    }

    private static byte[] CreateKeyTable(params (ulong Hash, string Name)[] entries)
    {
        return new SwShAhtbFile(entries
            .Select(entry => new SwShAhtbEntry(entry.Hash, entry.Name))
            .ToArray())
            .Write();
    }

    private static byte[] CreatePokemonNameTextTable()
    {
        return CreateTextTable(3, (1, "Bulbasaur"), (2, "Ivysaur"), (3, "Charmander"));
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

    private static void AssertSuccess<TPayload>(BridgeResponse<TPayload> response)
    {
        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
    }

    private static void AssertZaSpeciesPickerOptions(IEnumerable<int> optionValues)
    {
        var values = optionValues.ToArray();
        Assert.Contains(0, values);
        Assert.Contains(1, values);
        Assert.Contains(2, values);
        Assert.DoesNotContain(3, values);
    }

    private static void AssertUnavailableZaSpeciesDiagnostic(IEnumerable<ApiDiagnostic> diagnostics)
    {
        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("Pokemon species 3 is not available", StringComparison.Ordinal));
    }
}
