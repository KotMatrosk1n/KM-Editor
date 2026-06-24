// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.GameDump;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Shops;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.Formats.SwSh;
using KM.Formats.ZA;
using KM.Formats.ZA.Generated.GameData;
using KM.Integration.Tests.Tools;
using KM.Tools.Bridge;
using KM.ZA.Data;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class PokemonLegendsZABridgeTests
{
    private const ulong PokemonLegendsZATitleId = 0x0100F43008C44000;
    private const int ZaNpdmTitleIdOffset = 0x480;

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
        Assert.Single(workflow.Pokemon);
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
        Assert.Equal(33, Assert.Single(bulbasaur.Learnset).MoveId);
        Assert.Equal(1, Assert.Single(bulbasaur.Learnset).Level);
        Assert.Equal(1, Assert.Single(bulbasaur.Evolutions).Species);
        var tmGroup = bulbasaur.Compatibility.Single(group => group.GroupId == "tm");
        Assert.Contains(tmGroup.Entries, entry => entry.MoveId == 45 && entry.CanLearn);
    }

    [Fact]
    public void PokemonLegendsZAProjectListsPokemonTrainersMovesItemsAndShopsWorkflows()
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
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "moves" && workflow.Label == "Moves");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "items" && workflow.Label == "Items");
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "shops" && workflow.Label == "Shops");
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
        var dispatcher = new ProjectBridgeDispatcher();
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadGameDumpWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGameDumpWorkflow,
            new LoadGameDumpWorkflowRequest(paths),
            "request-za-game-dump-load");

        AssertSuccess(load);
        var categories = load.Payload!.Workflow.Categories;
        Assert.Equal(["pokemon", "trainers", "moves", "items", "shops"], categories.Select(category => category.Id).ToArray());
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
                    new GameDumpSelectionDto("shops", GameDumpFormatDto.Json),
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
            file => file.CategoryId == "manifest" && file.RelativePath == "manifest.json");
        Assert.Contains("Poke Ball", File.ReadAllText(Path.Combine(destinationFolder, "Items", "items.tsv")));
        Assert.Contains("Rival Aria", File.ReadAllText(Path.Combine(destinationFolder, "Trainers", "trainers.json")));
        Assert.Contains("Friendly Shop", File.ReadAllText(Path.Combine(destinationFolder, "Shops", "shops.json")));
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
        Assert.Equal(42, writtenPokemon!.Value.Level);
        Assert.Equal(45, writtenPokemon.Value.Move1!.Value.MoveId);
    }

    private static TemporaryBridgeProject CreatePokemonLegendsZAProject()
    {
        var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(PokemonLegendsZATitleId));
        temp.WriteBaseRomFsFile("arc/data.trpfd", []);
        temp.WriteBaseRomFsFile("arc/data.trpfs", []);
        var supportFolder = Directory.CreateDirectory(Path.Combine(temp.RootPath, "za-support")).FullName;
        File.WriteAllBytes(Path.Combine(supportFolder, ZaCompressionRuntime.RequiredFileName), []);
        return temp;
    }

    private static ProjectPathsDto CreatePaths(TemporaryBridgeProject temp)
    {
        return temp.Paths with
        {
            SelectedGame = ProjectGameDto.ZA,
            PokemonLegendsZASupportFolderPath = Path.Combine(temp.RootPath, "za-support"),
        };
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var npdm = new byte[ZaNpdmTitleIdOffset + sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(ZaNpdmTitleIdOffset), titleId);
        return npdm;
    }

    private static byte[] CreatePersonalArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var empty = CreatePersonal(builder, species: 0, present: false, hp: 0, zaDexOrder: 0);
        var bulbasaur = CreatePersonal(builder, species: 1, present: true, hp: 45, zaDexOrder: 25);
        var vector = ZaPersonalTable.CreateEntryVector(builder, [empty, bulbasaur]);
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
            CreateTextTable(1, (1, "Bulbasaur")));
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

    private static byte[] CreateTrainerDataArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var trainer = CreateTrainer(builder);
        var vector = ZaTrainerTable.CreateValueVector(builder, [trainer]);
        var root = ZaTrainerTable.Create(builder, vector);
        ZaTrainerTable.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<ZaTrainerRow> CreateTrainer(FlatBufferBuilder builder)
    {
        var trainerId = builder.CreateString("tr_battle_main_001");
        var pokemon = CreateTrainerPokemon(builder);

        return ZaTrainerRow.Create(
            builder,
            trainerIdOffset: trainerId,
            trainerType: 1,
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

    private static Offset<ZaTrainerPokemon> CreateTrainerPokemon(FlatBufferBuilder builder)
    {
        var move1 = ZaTrainerMove.Create(builder, 33);
        var move2 = ZaTrainerMove.Create(builder, 45);
        var ivs = ZaTrainerStats.Create(builder, hp: 31, atk: 31, def: 31, spAtk: 31, spDef: 31, agi: 31);
        var evs = ZaTrainerStats.Create(builder, hp: 4, atk: 0, def: 0, spAtk: 0, spDef: 0, agi: 0);

        return ZaTrainerPokemon.Create(
            builder,
            speciesId: 1,
            formId: 0,
            sex: 1,
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
            ZaLevelUpMoveData.Create(builder, move: 33, level: 1);
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
}
