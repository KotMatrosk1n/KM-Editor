// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.BagHook;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.DynamaxAdventures;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.FashionUnlock;
using KM.Api.Flagwork;
using KM.Api.GymUniformRemoval;
using KM.Api.Items;
using KM.Api.IvScreen;
using KM.Api.ModMerger;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Rentals;
using KM.Api.RoyalCandy;
using KM.Api.Shops;
using KM.Api.SpreadsheetImport;
using KM.Api.StaticEncounters;
using KM.Api.StartingItems;
using KM.Api.Text;
using KM.Api.Trades;
using KM.Api.Trainers;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.ExeFs;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.IvScreen;
using KM.SwSh.Raids;
using KM.SwSh.RoyalCandy;
using KM.Tools.Bridge;
using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class ProjectBridgeDispatcherTests
{
    private const ulong RoyalCandyDyniteOreTraderShopHash = 0xF49C86F8683842BF;
    private const string GymUniformRemovalSwordIpsPath = "exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips";
    private const int RoyalCandyItemId = 1128;

    [Fact]
    public void DispatchOpenProjectReturnsProjectHealthAndFileGraph()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile("romfs/data/items.bin", "layered-items");

        var requestJson = SerializeRequest(
            KmCommandNames.OpenProject,
            new OpenProjectRequest(temp.Paths),
            requestId: "request-open");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<OpenProjectResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-open", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.False(string.IsNullOrWhiteSpace(response.Payload.ProjectId));
        Assert.Equal(ProjectHealthStateDto.EditableReady, response.Payload.Health.State);
        Assert.Equal(2, response.Payload.FileGraph.Summary.BaseFileCount);
        Assert.Equal(1, response.Payload.FileGraph.Summary.OverrideCount);
        Assert.Contains(
            response.Payload.FileGraph.Entries,
            entry => entry.RelativePath == "romfs/data/items.bin"
                && entry.State == ProjectFileGraphEntryStateDto.LayeredOverride);
    }

    [Fact]
    public void DispatchValidateProjectReturnsValidationPayloadForMissingPaths()
    {
        using var temp = TemporaryBridgeProject.Create();
        var missingPaths = temp.Paths with { BaseRomFsPath = Path.Combine(temp.RootPath, "missing-romfs") };
        var requestJson = SerializeRequest(
            KmCommandNames.ValidateProject,
            new ValidateProjectRequest(missingPaths),
            requestId: "request-validate");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<ValidateProjectResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-validate", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(ProjectHealthStateDto.NeedsPaths, response.Payload.Health.State);
        Assert.Contains(
            response.Payload.Health.Paths,
            path => path.Role == ProjectPathRoleDto.BaseRomFs && path.Status == ProjectPathStatusDto.Missing);
    }

    [Fact]
    public void DispatchRefreshFileGraphReturnsBaseGraphWhenOutputRootIsNotConfigured()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var paths = temp.Paths with { OutputRootPath = null };
        var requestJson = SerializeRequest(
            KmCommandNames.RefreshFileGraph,
            new RefreshFileGraphRequest(paths),
            requestId: "request-refresh");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<RefreshFileGraphResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-refresh", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.FileGraph.Summary.BaseFileCount);
        Assert.Equal(0, response.Payload.FileGraph.Summary.LayeredFileCount);
    }

    [Fact]
    public void DispatchListWorkflowsReturnsItemsWorkflowAvailability()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.ListWorkflows,
            new ListWorkflowsRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-workflows");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<ListWorkflowsResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-workflows", response.RequestId);
        var workflows = response.Payload?.Workflows ?? [];
        Assert.Equal(
            [
                "items",
                "pokemon",
                "moves",
                "text",
                "trainers",
                "giftPokemon",
                "tradePokemon",
                "staticEncounters",
                "rentalPokemon",
                "dynamaxAdventures",
                "shops",
                "encounters",
                "raidBattles",
                "raidRewards",
                "raidBonusRewards",
                "placement",
                "behavior",
                "flagworkSave",
                "bagHook",
                "catchCap",
                "hyperTraining",
                "shinyRate",
                "typeChart",
                "fairyGymBoosts",
                "fashionUnlock",
                "gymUniformRemoval",
                "ivScreen",
                "royalCandy",
                "startingItems",
                "npcItemGift",
                "spreadsheetImport",
                "modMerger",
            ],
            workflows.Select(workflow => workflow.Id).ToArray());

        var items = workflows.Single(workflow => workflow.Id == "items");
        Assert.Equal(WorkflowAvailabilityDto.Disabled, items.Availability);
        Assert.Contains(
            items.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.items.dependencies");

        var bagHook = workflows.Single(workflow => workflow.Id == "bagHook");
        Assert.Equal(WorkflowAvailabilityDto.ReadOnly, bagHook.Availability);
        Assert.DoesNotContain(
            bagHook.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchLoadItemsWorkflowReturnsRealItemRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-items");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadItemsWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-items", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(3, response.Payload.Workflow.Items.Count);
        var item = response.Payload.Workflow.Items[1];
        Assert.Equal("Potion", item.Name);
        Assert.Equal(300, item.BuyPrice);
        Assert.Equal(150, item.SellPrice);
        Assert.Equal(15, item.WattsPrice);
        Assert.Equal(3, item.AlternatePrice);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Sprite" && detail.Value == "12");
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Field Use").Details,
            detail => detail.Label == "Use flags 1 (decoded)" && detail.Value == "Restore HP");
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Pokemon Effects").Details,
            detail => detail.Label == "Heal" && detail.Value == "20 HP");
        Assert.Equal("romfs/bin/pml/item/item.dat", item.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, item.Provenance.SourceLayer);
        Assert.Equal(0, item.Metadata.Pouch);
        Assert.True(item.Metadata.CanUseOnPokemon);
        Assert.Equal(20, item.Metadata.HealAmount);
        Assert.Contains(
            response.Payload.Workflow.EditableFields,
            editableField => editableField.Field == "buyPrice" && editableField.MaximumValue == 999_999);
        Assert.Contains(
            response.Payload.Workflow.EditableFields,
            editableField => editableField.Field == "sellPrice" && editableField.MaximumValue == 499_999);
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "pouch").Options,
            option => option.Value == 0 && option.Label == "Medicine");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "fieldUseType").Options,
            option => option.Value == 1 && option.Label == "Medicine");
    }

    [Fact]
    public void DispatchLoadItemsWorkflowReturnsMachineMoveLinkage()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseMachineItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-items-machine");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadItemsWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        var item = response.Payload.Workflow.Items[1];
        Assert.Equal(10, item.Metadata.MachineSlot);
        Assert.Equal(345, item.Metadata.MachineMoveId);
        Assert.Equal("Magical Leaf", item.Metadata.MachineMoveName);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Machine" && detail.Value == "TM10 (slot 10) -> Magical Leaf (345)");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "machineMoveId").Options,
            option => option.Value == 345 && option.Label == "345 Magical Leaf");
    }

    [Fact]
    public void DispatchLoadPokemonWorkflowReturnsRealPokemonRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPokemonBridgeFixtures.WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-pokemon");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadPokemonWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-pokemon", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Pokemon.Count);
        Assert.Equal(1, response.Payload.Workflow.Stats.PresentPokemonCount);
        Assert.Equal(1, response.Payload.Workflow.Stats.TotalEvolutionCount);
        Assert.Equal(2, response.Payload.Workflow.Stats.TotalLearnsetMoveCount);
        var pokemon = response.Payload.Workflow.Pokemon[1];
        Assert.Equal(1, pokemon.PersonalId);
        Assert.Equal("Bulbasaur", pokemon.Name);
        Assert.Equal("Grass", pokemon.Type1);
        Assert.Equal("Poison", pokemon.Type2);
        Assert.Equal(318, pokemon.BaseStats.Total);
        Assert.Equal(65, pokemon.Abilities.Ability1);
        Assert.Equal(11, pokemon.Personal.Type1);
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "hp" && field.Group == "Base Stats");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "ability1").Options,
            option => option.Value == 65 && option.Label == "065 Overgrow");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "hatchedSpecies").Options,
            option => option.Value == 1 && option.Label == "001 Bulbasaur");
        Assert.Contains(
            response.Payload.Workflow.EvolutionMethodOptions,
            option => option.Value == 4
                && option.Label == "004 Level Up"
                && option.ArgumentKind == "level");
        Assert.Contains(
            response.Payload.Workflow.EvolutionMethodOptions.Single(option => option.Value == 8).ArgumentOptions,
            option => option.Value == 1 && option.Label == "001 Potion");
        var tmGroup = pokemon.Compatibility.Single(group => group.GroupId == "tm");
        Assert.Equal(1, tmGroup.EnabledCount);
        var tm10 = tmGroup.Entries.Single(entry => entry.Slot == 10);
        Assert.Equal("TM10 (Magical Leaf)", tm10.Label);
        Assert.True(tm10.CanLearn);
        Assert.Equal("romfs/bin/pml/personal/personal_total.bin", pokemon.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, pokemon.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryStateDto.BaseOnly, pokemon.Provenance.FileState);
        var evolution = Assert.Single(pokemon.Evolutions);
        Assert.Equal(0, evolution.Slot);
        Assert.Equal("Level Up", evolution.MethodName);
        Assert.Equal("level", evolution.ArgumentKind);
        Assert.Equal("Level", evolution.ArgumentLabel);
        Assert.Equal("None", evolution.ArgumentValue);
        Assert.Equal(2, evolution.Species);
        Assert.Equal(16, evolution.Level);
        Assert.Collection(
            pokemon.Learnset,
            move =>
            {
                Assert.Equal(0, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal("Tackle", move.MoveName);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(45, move.MoveId);
                Assert.Equal("Growl", move.MoveName);
            });
    }

    [Fact]
    public void DispatchUpdatePokemonFieldReturnsPendingPokemonSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPokemonBridgeFixtures.WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(
                temp.Paths,
                Session: null,
                PersonalId: 1,
                Field: "hp",
                Value: "99"),
            requestId: "request-pokemon-update");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdatePokemonFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-pokemon-update", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(99, response.Payload.Workflow.Pokemon[1].BaseStats.HP);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("hp", edit.Field);
        Assert.Equal("99", edit.NewValue);
    }

    [Fact]
    public void DispatchUpdatePokemonLearnsetReturnsPendingPokemonSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPokemonBridgeFixtures.WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdatePokemonLearnset,
            new UpdatePokemonLearnsetRequest(
                temp.Paths,
                Session: null,
                PersonalId: 1,
                Action: "upsert",
                Slot: 1,
                MoveId: 345,
                Level: 9),
            requestId: "request-pokemon-learnset-update");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdatePokemonLearnsetResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-pokemon-learnset-update", response.RequestId);
        Assert.NotNull(response.Payload);
        var updatedMove = response.Payload.Workflow.Pokemon[1].Learnset[1];
        Assert.Equal(1, updatedMove.Slot);
        Assert.Equal(345, updatedMove.MoveId);
        Assert.Equal(9, updatedMove.Level);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("learnset:upsert:1", edit.Field);
        Assert.Equal("345:9", edit.NewValue);
    }

    [Fact]
    public void DispatchUpdatePokemonEvolutionReturnsPendingPokemonSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPokemonBridgeFixtures.WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(
                temp.Paths,
                Session: null,
                PersonalId: 1,
                Action: "upsert",
                Slot: 0,
                Method: 8,
                Argument: 25,
                Species: 2,
                Form: 1,
                Level: 32),
            requestId: "request-pokemon-evolution-update");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdatePokemonEvolutionResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-pokemon-evolution-update", response.RequestId);
        Assert.NotNull(response.Payload);
        var updatedEvolution = Assert.Single(response.Payload.Workflow.Pokemon[1].Evolutions);
        Assert.Equal(0, updatedEvolution.Slot);
        Assert.Equal(8, updatedEvolution.Method);
        Assert.Equal(25, updatedEvolution.Argument);
        Assert.Equal(2, updatedEvolution.Species);
        Assert.Equal(1, updatedEvolution.Form);
        Assert.Equal(32, updatedEvolution.Level);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("evolution:upsert:0", edit.Field);
        Assert.Equal("8:25:2:1:32", edit.NewValue);
    }

    [Fact]
    public void DispatchApplyPokemonChangePlanWritesPersonalData()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPokemonBridgeFixtures.WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var updateJson = SerializeRequest(
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(
                temp.Paths,
                Session: null,
                PersonalId: 1,
                Field: "canNotDynamax",
                Value: "true"),
            requestId: "request-pokemon-update");
        var update = DeserializeResponse<UpdatePokemonFieldResponse>(dispatcher.Dispatch(updateJson)).Payload!;
        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, update.Session),
            requestId: "request-pokemon-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson)).Payload!.ChangePlan;

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, update.Session, plan),
            requestId: "request-pokemon-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson)).Payload!.ApplyResult;

        Assert.Contains("romfs/bin/pml/personal/personal_total.bin", apply.WrittenFiles);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs/bin/pml/personal/personal_total.bin".Replace('/', Path.DirectorySeparatorChar));
        var output = SwShPersonalTable.Parse(File.ReadAllBytes(outputPath));
        Assert.True(output.Records[1].CanNotDynamax);
    }

    [Fact]
    public void DispatchApplyChangePlanAllowsNormalEditorsToShareOneSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPokemonBridgeFixtures.WriteBasePokemonData(temp);
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();

        var pokemonUpdateJson = SerializeRequest(
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(
                temp.Paths,
                Session: null,
                PersonalId: 1,
                Field: "canNotDynamax",
                Value: "true"),
            requestId: "request-normal-session-pokemon");
        var pokemonUpdate = DeserializeResponse<UpdatePokemonFieldResponse>(
            dispatcher.Dispatch(pokemonUpdateJson)).Payload!;
        var itemUpdateJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(
                temp.Paths,
                pokemonUpdate.Session,
                ItemId: 1,
                Field: "buyPrice",
                Value: "650"),
            requestId: "request-normal-session-item");
        var itemUpdate = DeserializeResponse<UpdateItemFieldResponse>(
            dispatcher.Dispatch(itemUpdateJson)).Payload!;

        Assert.Collection(
            itemUpdate.Session.PendingEdits,
            edit => Assert.Equal("workflow.pokemon", edit.Domain),
            edit => Assert.Equal("workflow.items", edit.Domain));

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, itemUpdate.Session),
            requestId: "request-normal-session-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(
            dispatcher.Dispatch(planJson)).Payload!.ChangePlan;
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "romfs/bin/pml/personal/personal_total.bin");
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "romfs/bin/pml/item/item.dat");

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, itemUpdate.Session, plan),
            requestId: "request-normal-session-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(
            dispatcher.Dispatch(applyJson)).Payload!.ApplyResult;

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains("romfs/bin/pml/personal/personal_total.bin", apply.WrittenFiles);
        Assert.Contains("romfs/bin/pml/item/item.dat", apply.WrittenFiles);

        var personalPath = Path.Combine(
            temp.OutputRootPath,
            "romfs/bin/pml/personal/personal_total.bin".Replace('/', Path.DirectorySeparatorChar));
        var itemPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        Assert.True(SwShPersonalTable.Parse(File.ReadAllBytes(personalPath)).Records[1].CanNotDynamax);
        Assert.Equal(650u, SwShItemTable.Parse(File.ReadAllBytes(itemPath)).Records[1].BuyPrice);
    }

    [Fact]
    public void DispatchLoadStaticEncountersWorkflowReturnsRealStaticEncounterRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteStaticEncounterBridgeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadStaticEncountersWorkflow,
            new LoadStaticEncountersWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-static-encounters");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadStaticEncountersWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-static-encounters", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Encounters.Count);
        var encounter = response.Payload.Workflow.Encounters[0];
        Assert.Equal(0, encounter.EncounterIndex);
        Assert.Equal("Grookey", encounter.Species);
        Assert.Equal(50, encounter.Level);
        Assert.Equal("Hidden Ability", encounter.AbilityLabel);
        Assert.Equal("Never Shiny", encounter.ShinyLockLabel);
        Assert.Equal("Calyrex", encounter.EncounterScenarioLabel);
        Assert.Equal("0x0102030405060708", encounter.EncounterId);
        Assert.Equal("romfs/bin/script_event_data/event_encount_data.bin", encounter.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, encounter.Provenance.SourceLayer);
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "ivHp");
    }

    [Fact]
    public void DispatchApplyStaticEncounterChangePlanWritesStaticTable()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteStaticEncounterBridgeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(
                temp.Paths,
                Session: null,
                EncounterIndex: 0,
                Field: "ivAttack",
                Value: "12"),
            requestId: "request-static-encounter-update");
        var update = DeserializeResponse<UpdateStaticEncounterFieldResponse>(dispatcher.Dispatch(updateJson)).Payload!;
        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, update.Session),
            requestId: "request-static-encounter-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson)).Payload!.ChangePlan;

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, update.Session, plan),
            requestId: "request-static-encounter-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson)).Payload!.ApplyResult;

        Assert.Contains("romfs/bin/script_event_data/event_encount_data.bin", apply.WrittenFiles);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs/bin/script_event_data/event_encount_data.bin".Replace('/', Path.DirectorySeparatorChar));
        var output = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(12, output.Encounters[0].Ivs.Attack);
    }

    [Fact]
    public void DispatchLoadTradePokemonWorkflowReturnsRealTradeRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTradePokemonBridgeFixtures.WriteBaseTradePokemon(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadTradePokemonWorkflow,
            new LoadTradePokemonWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-trade-pokemon");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadTradePokemonWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-trade-pokemon", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Trades.Count);
        var trade = response.Payload.Workflow.Trades[0];
        Assert.Equal(0, trade.TradeIndex);
        Assert.Equal("Grookey", trade.Species);
        Assert.Equal("Pikachu", trade.RequiredSpecies);
        Assert.Equal("Hidden Ability", trade.AbilityLabel);
        Assert.Equal("0x1122334455667788", trade.Hash0);
        Assert.Equal("romfs/bin/script_event_data/field_trade.bin", trade.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, trade.Provenance.SourceLayer);
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "ivHp");
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "requiredSpecies");
    }

    [Fact]
    public void DispatchApplyTradePokemonChangePlanWritesTradeTable()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTradePokemonBridgeFixtures.WriteBaseTradePokemon(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateTradePokemonField,
            new UpdateTradePokemonFieldRequest(
                temp.Paths,
                Session: null,
                TradeIndex: 0,
                Field: "ivAttack",
                Value: "12"),
            requestId: "request-trade-pokemon-update");
        var update = DeserializeResponse<UpdateTradePokemonFieldResponse>(dispatcher.Dispatch(updateJson)).Payload!;
        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, update.Session),
            requestId: "request-trade-pokemon-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson)).Payload!.ChangePlan;

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, update.Session, plan),
            requestId: "request-trade-pokemon-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson)).Payload!.ApplyResult;

        Assert.Contains("romfs/bin/script_event_data/field_trade.bin", apply.WrittenFiles);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs/bin/script_event_data/field_trade.bin".Replace('/', Path.DirectorySeparatorChar));
        var output = SwShTradePokemonArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(12, output.Trades[0].Ivs.Attack);
    }

    [Fact]
    public void DispatchLoadRentalPokemonWorkflowReturnsRealRentalRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShRentalPokemonBridgeFixtures.WriteBaseRentalPokemon(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadRentalPokemonWorkflow,
            new LoadRentalPokemonWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-rental-pokemon");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadRentalPokemonWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-rental-pokemon", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Rentals.Count);
        var rental = response.Payload.Workflow.Rentals[0];
        Assert.Equal(0, rental.RentalIndex);
        Assert.Equal("Eevee", rental.Species);
        Assert.Equal("Hidden Ability", rental.AbilityLabel);
        Assert.Equal("0x1122334455667788", rental.Hash1);
        Assert.Equal("romfs/bin/script_event_data/rental.bin", rental.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, rental.Provenance.SourceLayer);
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "ivHp");
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "fixedIvPreset");
    }

    [Fact]
    public void DispatchApplyRentalPokemonChangePlanWritesRentalTable()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShRentalPokemonBridgeFixtures.WriteBaseRentalPokemon(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateRentalPokemonField,
            new UpdateRentalPokemonFieldRequest(
                temp.Paths,
                Session: null,
                RentalIndex: 0,
                Field: "ivAttack",
                Value: "12"),
            requestId: "request-rental-pokemon-update");
        var update = DeserializeResponse<UpdateRentalPokemonFieldResponse>(dispatcher.Dispatch(updateJson)).Payload!;
        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, update.Session),
            requestId: "request-rental-pokemon-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson)).Payload!.ChangePlan;

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, update.Session, plan),
            requestId: "request-rental-pokemon-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson)).Payload!.ApplyResult;

        Assert.Contains("romfs/bin/script_event_data/rental.bin", apply.WrittenFiles);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs/bin/script_event_data/rental.bin".Replace('/', Path.DirectorySeparatorChar));
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(12, output.Rentals[0].Ivs.Attack);
    }

    [Fact]
    public void DispatchLoadDynamaxAdventuresWorkflowReturnsRealAdventureRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureBridgeFixtures.WriteBasePersonalData(temp);
        SwShDynamaxAdventureBridgeFixtures.WriteBaseMoveLegalityData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadDynamaxAdventuresWorkflow,
            new LoadDynamaxAdventuresWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-dynamax-adventures");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadDynamaxAdventuresWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-dynamax-adventures", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Encounters.Count);
        var encounter = response.Payload.Workflow.Encounters[0];
        Assert.Equal(0, encounter.EntryIndex);
        Assert.Equal("Eevee", encounter.Species);
        Assert.StartsWith("Ability 2", encounter.AbilityLabel, StringComparison.Ordinal);
        Assert.Equal(4, encounter.GuaranteedPerfectIvs);
        Assert.Equal("0x1122334455667788", encounter.SingleCaptureFlagBlock);
        Assert.Equal("romfs/bin/appli/chika/data_table/underground_exploration_poke.bin", encounter.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, encounter.Provenance.SourceLayer);
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "guaranteedPerfectIvs");
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "ivAttack");
        var pikachu = response.Payload.Workflow.Encounters.Single(row => row.EntryIndex == 1);
        Assert.Contains(pikachu.MoveOptions, option => option.Value == 85 && option.Label == "085 Thunderbolt");
        Assert.Contains(pikachu.MoveOptions, option => option.Value == 3);
        Assert.DoesNotContain(pikachu.MoveOptions, option => option.Value == 10);
    }

    [Fact]
    public void DispatchLoadDynamaxAdventuresWorkflowMarksBossRowsUnsafe()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteBossTargetDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadDynamaxAdventuresWorkflow,
            new LoadDynamaxAdventuresWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-dynamax-boss-targets");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadDynamaxAdventuresWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-dynamax-boss-targets", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.DoesNotContain(response.Payload.Workflow.EditableFields, field => field.Field == "bossTargetSpecies");
        var normal = response.Payload.Workflow.Encounters.Single(row => row.EntryIndex == 0);
        Assert.True(normal.IsEditable);
        Assert.Empty(normal.BossTargetOptions);
        var articuno = response.Payload.Workflow.Encounters.Single(row => row.EntryIndex == 226);
        Assert.False(articuno.IsEditable);
        Assert.Equal(144, articuno.BossTargetSpeciesId);
        Assert.Equal("Articuno", articuno.BossTargetSpecies);
        var option = Assert.Single(articuno.BossTargetOptions);
        Assert.Equal(227, option.EntryIndex);
        Assert.Equal(1004, option.AdventureIndex);
        Assert.Equal(150, option.SpeciesId);
        Assert.Equal("Mewtwo", option.Species);
        Assert.Equal("Mewtwo", response.Payload.Workflow.Encounters.Single(row => row.EntryIndex == 227).Species);
    }

    [Fact]
    public void DispatchDynamaxAdventureBossTargetRemapIsRejected()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteBossTargetDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateDynamaxAdventureBossTargetCompatibleNso(entryCount: 230));
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var dispatcher = new ProjectBridgeDispatcher();
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateDynamaxAdventureField,
            new UpdateDynamaxAdventureFieldRequest(
                temp.Paths,
                Session: null,
                EntryIndex: 226,
                Field: "bossTargetSpecies",
                Value: "150"),
            requestId: "request-dynamax-boss-target-update");
        var updateResponse = DeserializeResponse<UpdateDynamaxAdventureFieldResponse>(dispatcher.Dispatch(updateJson));
        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Empty(updateResponse.Payload.Session.PendingEdits);
        Assert.Contains(
            updateResponse.Payload.Diagnostics,
            diagnostic =>
                diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("is not supported", StringComparison.Ordinal));
        Assert.False(File.Exists(outputMainPath));
    }

    [Fact]
    public void DispatchDynamaxAdventureSeedPlanUsesActiveAdventureRows()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureBridgeFixtures.WriteBasePersonalData(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureBridgeFixtures.CreateArchive().WriteEdits(
            [
                new(1, SwShDynamaxAdventureField.Species, 467),
            ]));
        var requestJson = SerializeRequest(
            KmCommandNames.PlanDynamaxAdventureSeed,
            new PlanDynamaxAdventureSeedRequest(temp.Paths, "0x0", NpcCount: 0, RequiredRows: [1]),
            requestId: "request-dynamax-seed-plan");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<PlanDynamaxAdventureSeedResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-dynamax-seed-plan", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal("0x0000000000000000", response.Payload.Plan.Seed);
        Assert.Contains(response.Payload.Plan.Rentals.Concat(response.Payload.Plan.Encounters), template =>
            template.Row == 1
            && template.Species == 467);
        Assert.Contains(response.Payload.Plan.RequiredRowPositions, position => position.Row == 1);
    }

    [Fact]
    public void DispatchDynamaxAdventureSeedSearchReturnsMatchingSeeds()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureBridgeFixtures.WriteBasePersonalData(temp);
        var requestJson = SerializeRequest(
            KmCommandNames.SearchDynamaxAdventureSeed,
            new SearchDynamaxAdventureSeedRequest(
                temp.Paths,
                RequiredRows: [0],
                NpcCount: 0,
                StartSeed: "0",
                Limit: "1",
                MaxResults: 1),
            requestId: "request-dynamax-seed-search");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<SearchDynamaxAdventureSeedResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-dynamax-seed-search", response.RequestId);
        Assert.NotNull(response.Payload);
        var result = Assert.Single(response.Payload.Search.Results);
        Assert.Equal("0x0000000000000000", result.Seed);
        Assert.Contains(result.Positions, position => position.Row == 0);
    }

    [Fact]
    public void DispatchDynamaxAdventureSeedPlanWarnsForBossRows()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteSeedPlanningDynamaxAdventures(temp, rowCount: 230);
        SwShDynamaxAdventureBridgeFixtures.WriteBasePersonalData(temp, count: 400);
        var requestJson = SerializeRequest(
            KmCommandNames.PlanDynamaxAdventureSeed,
            new PlanDynamaxAdventureSeedRequest(
                temp.Paths,
                Seed: "0",
                NpcCount: 0,
                RequiredRows: [226]),
            requestId: "request-dynamax-seed-plan-boss-row");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<PlanDynamaxAdventureSeedResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.Empty(response.Payload.Plan.RequiredRowPositions);
        Assert.Contains(response.Payload.Plan.Diagnostics, diagnostic =>
            diagnostic.Severity == ApiDiagnosticSeverity.Warning
            && diagnostic.Message.Contains("cannot select boss row(s) 226", StringComparison.Ordinal));
    }

    [Fact]
    public void DispatchDynamaxAdventureSeedSearchRejectsBossRows()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteSeedPlanningDynamaxAdventures(temp, rowCount: 230);
        SwShDynamaxAdventureBridgeFixtures.WriteBasePersonalData(temp, count: 400);
        var requestJson = SerializeRequest(
            KmCommandNames.SearchDynamaxAdventureSeed,
            new SearchDynamaxAdventureSeedRequest(
                temp.Paths,
                RequiredRows: [226],
                NpcCount: 0,
                StartSeed: "0",
                Limit: "100",
                MaxResults: 1),
            requestId: "request-dynamax-seed-search-boss-row");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<SearchDynamaxAdventureSeedResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.Empty(response.Payload.Search.Results);
        Assert.Contains(response.Payload.Search.Diagnostics, diagnostic =>
            diagnostic.Severity == ApiDiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot select boss row(s) 226", StringComparison.Ordinal));
    }

    [Fact]
    public void DispatchSetDynamaxAdventureSaveSeedRejectsInvalidSeed()
    {
        using var temp = TemporaryBridgeProject.Create();
        var requestJson = SerializeRequest(
            KmCommandNames.SetDynamaxAdventureSaveSeed,
            new SetDynamaxAdventureSaveSeedRequest(temp.Paths, Seed: "not-a-seed"),
            requestId: "request-dynamax-save-seed-invalid");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<SetDynamaxAdventureSaveSeedResponse>(responseJson);

        Assert.NotNull(response.Error);
        Assert.Equal("dynamaxAdventures.seed.invalid", response.Error.Code);
        Assert.Null(response.Payload);
    }

    [Fact]
    public void DispatchSetDynamaxAdventureSaveSeedReturnsMissingSaveDiagnostic()
    {
        using var temp = TemporaryBridgeProject.Create();
        var requestJson = SerializeRequest(
            KmCommandNames.SetDynamaxAdventureSaveSeed,
            new SetDynamaxAdventureSaveSeedRequest(temp.Paths, Seed: "0x1234"),
            requestId: "request-dynamax-save-seed-missing");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<SetDynamaxAdventureSaveSeedResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-dynamax-save-seed-missing", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.False(response.Payload.Result.WasChanged);
        Assert.Contains(response.Payload.Result.Diagnostics, diagnostic =>
            diagnostic.Severity == ApiDiagnosticSeverity.Error
            && diagnostic.Message.Contains("save file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DispatchApplyDynamaxAdventureChangePlanWritesAdventureTable()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShDynamaxAdventureBridgeFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateDynamaxAdventureField,
            new UpdateDynamaxAdventureFieldRequest(
                temp.Paths,
                Session: null,
                EntryIndex: 0,
                Field: "guaranteedPerfectIvs",
                Value: "6"),
            requestId: "request-dynamax-adventure-update");
        var update = DeserializeResponse<UpdateDynamaxAdventureFieldResponse>(dispatcher.Dispatch(updateJson)).Payload!;
        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, update.Session),
            requestId: "request-dynamax-adventure-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson)).Payload!.ChangePlan;

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, update.Session, plan),
            requestId: "request-dynamax-adventure-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson)).Payload!.ApplyResult;

        Assert.Contains("romfs/bin/appli/chika/data_table/underground_exploration_poke.bin", apply.WrittenFiles);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs/bin/appli/chika/data_table/underground_exploration_poke.bin".Replace('/', Path.DirectorySeparatorChar));
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(-6, output.Entries[0].Ivs.Hp);
        Assert.Equal(0x1122334455667788UL, output.Entries[0].SingleCaptureFlagBlock);
    }

    [Fact]
    public void DispatchLoadMovesWorkflowReturnsRealMoveRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShMoveBridgeFixtures.WriteBaseMoves(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadMovesWorkflow,
            new LoadMovesWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-moves");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadMovesWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-moves", response.RequestId);
        Assert.NotNull(response.Payload);
        var move = Assert.Single(response.Payload.Workflow.Moves);
        Assert.Equal(33, move.MoveId);
        Assert.Equal("Tackle", move.Name);
        Assert.Equal("Normal", move.TypeName);
        Assert.Equal("Physical", move.CategoryName);
        Assert.Equal(40, move.Power);
        Assert.Equal(100, move.Accuracy);
        Assert.Equal(35, move.PP);
        Assert.Equal("Opponent", move.TargetName);
        Assert.Equal("Paralyze", move.InflictName);
        Assert.Equal(-25, move.Recoil);
        Assert.Contains(move.Flags, flag => flag.Field == "makesContact" && flag.Enabled);
        Assert.Contains(move.StatChanges, stat => stat.StatName == "Attack" && stat.Stage == -1);
        Assert.Equal(1, response.Payload.Workflow.Stats.TotalMoveCount);
        Assert.Equal(3, response.Payload.Workflow.Stats.ActiveFlagCount);
        Assert.Equal("romfs/bin/pml/waza/waza_033.bin", move.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, move.Provenance.SourceLayer);
    }

    [Fact]
    public void DispatchUpdateMoveFieldReturnsPendingMoveSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShMoveBridgeFixtures.WriteBaseMoves(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateMoveField,
            new UpdateMoveFieldRequest(
                temp.Paths,
                Session: null,
                MoveId: 33,
                Field: "power",
                Value: "80"),
            requestId: "request-move-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateMoveFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        Assert.Equal(80, Assert.Single(response.Payload.Workflow.Moves).Power);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("workflow.moves", edit.Domain);
        Assert.Equal("power", edit.Field);
        Assert.Equal("80", edit.NewValue);
    }

    [Fact]
    public void DispatchApplyMoveChangePlanWritesMoveData()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShMoveBridgeFixtures.WriteBaseMoves(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var sessionResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateMoveField,
            new UpdateMoveFieldRequest(temp.Paths, Session: null, MoveId: 33, Field: "power", Value: "80"),
            requestId: "request-move-edit"));
        var sessionResponse = DeserializeResponse<UpdateMoveFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var validationResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-move-validate"));
        var validationResponse = DeserializeResponse<ValidateEditSessionResponse>(validationResponseJson);
        Assert.NotNull(validationResponse.Payload);
        var planResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-move-change-plan"));
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(planResponseJson);
        Assert.NotNull(planResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, sessionResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-move-apply");

        var responseJson = dispatcher.Dispatch(requestJson);
        var response = DeserializeResponse<ApplyChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.True(validationResponse.Payload.IsValid);
        Assert.NotNull(response.Payload);
        Assert.Equal("romfs/bin/pml/waza/waza_033.bin", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "waza", "waza_033.bin");
        var output = SwShMoveDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(80, output.Record.Core.Power);
        Assert.DoesNotContain(
            response.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchLoadTextWorkflowReturnsRealMessageRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTextBridgeFixtures.WriteBaseText(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-text");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadTextWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-text", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Entries.Count);
        var entry = response.Payload.Workflow.Entries[0];
        Assert.Equal("story #0", entry.Label);
        Assert.Equal("English", entry.Language);
        Assert.Equal("romfs/bin/message/English/common/story.dat", entry.SourceFile);
        Assert.Equal(0, entry.LineIndex);
        Assert.True(entry.CanEdit);
        Assert.Equal(ProjectFileLayerDto.Base, entry.Provenance.SourceLayer);
        Assert.Equal(2, response.Payload.Workflow.DialogueReferences.Count);
        var reference = response.Payload.Workflow.DialogueReferences[0];
        Assert.Equal("common/story:0", reference.DialogueId);
        Assert.Equal(0, reference.TextId);
        var editableField = Assert.Single(response.Payload.Workflow.EditableFields);
        Assert.Equal("value", editableField.Field);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadTrainersWorkflowReturnsRealTrainerRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTrainerBridgeFixtures.WriteBaseTrainers(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-trainers");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadTrainersWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-trainers", response.RequestId);
        Assert.NotNull(response.Payload);
        var trainer = Assert.Single(response.Payload.Workflow.Trainers);
        Assert.Equal("Avery", trainer.Name);
        Assert.Equal(5, trainer.TrainerClassId);
        Assert.Equal("Pokemon Trainer", trainer.TrainerClass);
        Assert.Equal(1, trainer.BattleTypeValue);
        Assert.Equal("Doubles", trainer.BattleType);
        Assert.Equal([1, 2, 0, 0], trainer.ItemIds);
        Assert.Equal(["Potion", "Antidote", "None", "None"], trainer.Items);
        Assert.Equal(0x4D, trainer.AiFlags);
        Assert.True(trainer.Heal);
        Assert.Equal(24, trainer.Money);
        Assert.Equal(7, trainer.Gift);
        Assert.Equal(4, trainer.ClassBallId);
        Assert.Equal("4 Poke Ball", trainer.ClassBall);
        Assert.True(trainer.CanEditClassBall);
        Assert.Equal("Unique trainer class: Avery", trainer.ClassBallScope);
        Assert.Equal(ProjectFileLayerDto.Base, trainer.Provenance.SourceLayer);
        Assert.Equal(ProjectFileLayerDto.Base, trainer.Provenance.TeamSourceLayer);
        Assert.Equal(ProjectFileLayerDto.Base, trainer.Provenance.ClassSourceLayer);
        var pokemon = Assert.Single(trainer.Team);
        Assert.Equal(810, pokemon.SpeciesId);
        Assert.Equal("Grookey", pokemon.Species);
        Assert.Equal(12, pokemon.Level);
        Assert.Equal(0, pokemon.Form);
        Assert.Equal(1, pokemon.HeldItemId);
        Assert.Equal("Potion", pokemon.HeldItem);
        Assert.Equal([1, 2, 0, 0], pokemon.MoveIds);
        Assert.Equal(1, pokemon.Gender);
        Assert.Equal(2, pokemon.Ability);
        Assert.Equal(13, pokemon.Nature);
        Assert.Equal(10, pokemon.Evs.HP);
        Assert.Equal(1, pokemon.Ivs.HP);
        Assert.Equal(2, pokemon.Ivs.Attack);
        Assert.Equal(5, pokemon.Ivs.SpecialAttack);
        Assert.True(pokemon.Shiny);
        Assert.False(pokemon.CanDynamax);
        Assert.Equal(38, response.Payload.Workflow.EditableFields.Count);
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "trainerClassId").Options,
            option => option.Value == 5 && option.Label == "005 Pokemon Trainer");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "classBallId").Options,
            option => option.Value == 4 && option.Label == "4 Poke Ball");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "battleType").Options,
            option => option.Value == 1 && option.Label == "1 Doubles");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "trainerItem1Id").Options,
            option => option.Value == 2 && option.Label == "002 Antidote");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "speciesId").Options,
            option => option.Value == 810 && option.Label == "810 Grookey");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "heldItemId").Options,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "move1Id").Options,
            option => option.Value == 1 && option.Label == "001 Scratch");
        Assert.Equal(3, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchTrainerUpdateValidatePlanAndApplyWritesLayeredOutput()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTrainerBridgeFixtures.WriteBaseTrainers(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateTrainerField,
            new UpdateTrainerFieldRequest(
                temp.Paths,
                Session: null,
                TrainerId: 10,
                Slot: 1,
                Field: "level",
                Value: "25"),
            requestId: "request-trainer-update");

        var updateResponse = DeserializeResponse<UpdateTrainerFieldResponse>(dispatcher.Dispatch(updateJson));
        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Equal(25, Assert.Single(updateResponse.Payload.Workflow.Trainers).Team[0].Level);
        Assert.Single(updateResponse.Payload.Session.PendingEdits);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-trainer-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-trainer-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.Equal("romfs/bin/trainer/trainer_poke/trainer_010.bin", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, updateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-trainer-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/trainer/trainer_poke/trainer_010.bin", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "trainer", "trainer_poke", "trainer_010.bin");
        var output = SwShTrainerTeamFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(25, output.Records[0].Level);
    }

    [Fact]
    public void DispatchShopUpdateValidatePlanAndApplyWritesLayeredOutput()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShShopBridgeFixtures.WriteBaseShops(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var shopId = $"single:{SwShShopBridgeFixtures.SingleShopHash:X16}";
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateShopInventoryItem,
            new UpdateShopInventoryItemRequest(
                temp.Paths,
                Session: null,
                shopId,
                Slot: 1,
                Field: "itemId",
                Value: "2"),
            requestId: "request-shop-update");

        var updateResponse = DeserializeResponse<UpdateShopInventoryItemResponse>(dispatcher.Dispatch(updateJson));
        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Equal(2, updateResponse.Payload.Workflow.Shops[0].Inventory[0].ItemId);
        Assert.Single(updateResponse.Payload.Session.PendingEdits);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-shop-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-shop-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.Equal("romfs/bin/appli/shop/bin/shop_data.bin", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, updateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-shop-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/appli/shop/bin/shop_data.bin", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "appli", "shop", "bin", "shop_data.bin");
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(2, output.SingleShops[0].Inventory.Items[0]);
    }

    [Fact]
    public void DispatchLoadShopsWorkflowReturnsRealShopRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShShopBridgeFixtures.WriteBaseShops(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadShopsWorkflow,
            new LoadShopsWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-shops");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadShopsWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-shops", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Shops.Count);
        var shop = response.Payload.Workflow.Shops[0];
        Assert.Equal($"single:{SwShShopBridgeFixtures.SingleShopHash:X16}", shop.ShopId);
        Assert.Equal("Single", shop.Kind);
        Assert.Equal("Inventory", shop.InventoryLabel);
        Assert.Equal(1, shop.InventoryIndex);
        Assert.Equal(1, shop.InventoryCount);
        Assert.Equal($"0x{SwShShopBridgeFixtures.SingleShopHash:X16}", shop.SourceHash);
        Assert.Equal("Potion, Antidote", shop.InventorySummary);
        Assert.Equal("Poke Mart", shop.Location);
        Assert.Equal(ProjectFileLayerDto.Base, shop.Provenance.SourceLayer);
        var inventoryItem = shop.Inventory[0];
        Assert.Equal("Potion", inventoryItem.ItemName);
        Assert.Equal(300, inventoryItem.Price);
        Assert.True(inventoryItem.IsKnownItem);
        Assert.Null(inventoryItem.StockLimit);
        Assert.Equal(1, response.Payload.Workflow.EditableFields.Count);
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single().Options,
            option => option.Value == 2
                && option.Label == "0002 Antidote (Medicine)"
                && option.ItemName == "Antidote"
                && option.Price == 200);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadEncountersWorkflowReturnsRealEncounterTables()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShEncounterBridgeFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-encounters");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadEncountersWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-encounters", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Tables.Count);
        var table = response.Payload.Workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        Assert.StartsWith("sword:symbol:0:", table.TableId, StringComparison.Ordinal);
        Assert.Equal($"Zone 0x{SwShEncounterBridgeFixtures.ZoneId:X16}", table.Location);
        Assert.Equal("Symbol", table.Area);
        Assert.Equal("Normal", table.EncounterType);
        Assert.Equal(ProjectFileLayerDto.Base, table.Provenance.SourceLayer);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", table.Provenance.SourceFile);
        var slot = table.Slots[0];
        Assert.Equal(1, slot.SpeciesId);
        Assert.Equal("Bulbasaur", slot.Species);
        Assert.Equal(3, slot.LevelMin);
        Assert.Equal(8, slot.LevelMax);
        Assert.Equal(5, response.Payload.Workflow.EditableFields.Count);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchEncounterUpdateValidatePlanAndApplyWritesLayeredOutput()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShEncounterBridgeFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var loadJson = SerializeRequest(
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(temp.Paths),
            requestId: "request-encounter-load");
        var loadResponse = DeserializeResponse<LoadEncountersWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.NotNull(loadResponse.Payload);
        var table = loadResponse.Payload.Workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var updateJson = SerializeRequest(
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                temp.Paths,
                Session: null,
                table.TableId,
                Slot: 1,
                Field: "probability",
                Value: "40"),
            requestId: "request-encounter-update");

        var updateResponse = DeserializeResponse<UpdateEncounterSlotFieldResponse>(dispatcher.Dispatch(updateJson));
        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Single(updateResponse.Payload.Session.PendingEdits);

        var secondUpdateJson = SerializeRequest(
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                temp.Paths,
                updateResponse.Payload.Session,
                table.TableId,
                Slot: 2,
                Field: "probability",
                Value: "60"),
            requestId: "request-encounter-update-2");

        var secondUpdateResponse = DeserializeResponse<UpdateEncounterSlotFieldResponse>(dispatcher.Dispatch(secondUpdateJson));
        Assert.Null(secondUpdateResponse.Error);
        Assert.NotNull(secondUpdateResponse.Payload);
        var updatedTable = secondUpdateResponse.Payload.Workflow.Tables.First(candidate => candidate.TableId == table.TableId);
        Assert.Equal(40, updatedTable.Slots[0].Weight);
        Assert.Equal(60, updatedTable.Slots[1].Weight);
        Assert.Equal(2, secondUpdateResponse.Payload.Session.PendingEdits.Count);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, secondUpdateResponse.Payload.Session),
            requestId: "request-encounter-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, secondUpdateResponse.Payload.Session),
            requestId: "request-encounter-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, secondUpdateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-encounter-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "archive", "field", "resident", "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal(40, outputArchive.Tables[0].SubTables[0].Slots[0].Probability);
        Assert.Equal(60, outputArchive.Tables[0].SubTables[0].Slots[1].Probability);
    }

    [Fact]
    public void DispatchLoadRaidBattlesWorkflowReturnsRealBattleSlots()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShRaidBattleBridgeFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadRaidBattlesWorkflow,
            new LoadRaidBattlesWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-raid-battles");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadRaidBattlesWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-raid-battles", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Single(response.Payload.Workflow.Tables);
        var table = response.Payload.Workflow.Tables[0];
        Assert.Equal("0xAABBCCDD00112233", table.SourceTableHash);
        Assert.Equal(ProjectFileLayerDto.Base, table.Provenance.SourceLayer);
        var slot = table.Slots[0];
        Assert.Equal("Eevee", slot.Species);
        Assert.Equal("Any Ability", slot.AbilityLabel);
        Assert.True(slot.IsGigantamax);
        Assert.Equal(4, slot.FlawlessIvs);
        Assert.Equal([100, 20, 30, 40, 50], slot.Probabilities);
        Assert.True(slot.DropRewardLink.IsMatched);
        Assert.Equal("Drop", slot.DropRewardLink.RewardKindLabel);
        Assert.Equal("0xAABBCCDD00112233", slot.DropRewardLink.SourceTableHash);
        Assert.Contains("Exp. Candy L", slot.DropRewardLink.Preview, StringComparison.Ordinal);
        Assert.True(slot.BonusRewardLink.IsMatched);
        Assert.Equal("Bonus", slot.BonusRewardLink.RewardKindLabel);
        Assert.Contains("Armorite Ore", slot.BonusRewardLink.Preview, StringComparison.Ordinal);
        Assert.False(table.Slots[1].BonusRewardLink.IsMatched);
        Assert.Contains(
            response.Payload.Workflow.EditableFields.Single(field => field.Field == "flawlessIvs").Options,
            option => option.Value == 6 && option.Label == "6 Guaranteed Perfect IVs");
        Assert.Equal(2, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchRaidBattleEditValidatePlanAndApplyWritesNestDataPack()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShRaidBattleBridgeFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();

        var loadJson = SerializeRequest(
            KmCommandNames.LoadRaidBattlesWorkflow,
            new LoadRaidBattlesWorkflowRequest(temp.Paths),
            requestId: "request-raid-battle-load");
        var loadResponse = DeserializeResponse<LoadRaidBattlesWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.NotNull(loadResponse.Payload);
        var table = Assert.Single(loadResponse.Payload.Workflow.Tables);
        var startJson = SerializeRequest(
            KmCommandNames.StartEditSession,
            new StartEditSessionRequest(temp.Paths),
            requestId: "request-raid-battle-start");
        var startResponse = DeserializeResponse<StartEditSessionResponse>(dispatcher.Dispatch(startJson));
        Assert.NotNull(startResponse.Payload);

        var updateJson = SerializeRequest(
            KmCommandNames.UpdateRaidBattleSlotField,
            new UpdateRaidBattleSlotFieldRequest(
                temp.Paths,
                startResponse.Payload.Session,
                table.TableId,
                Slot: 2,
                Field: "flawlessIvs",
                Value: "6"),
            requestId: "request-raid-battle-update");
        var updateResponse = DeserializeResponse<UpdateRaidBattleSlotFieldResponse>(dispatcher.Dispatch(updateJson));

        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Equal(6, updateResponse.Payload.Workflow.Tables.Single(candidate => candidate.TableId == table.TableId).Slots[1].FlawlessIvs);
        Assert.Equal("workflow.raidBattles", Assert.Single(updateResponse.Payload.Session.PendingEdits).Domain);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-raid-battle-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-raid-battle-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.NotNull(planResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, updateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-raid-battle-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "archive", "field", "resident", "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShEncounterNestArchive.Parse(outputPack.GetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName));
        Assert.Equal(6, outputArchive.Tables[0].Entries[1].FlawlessIvs);
    }

    [Fact]
    public void DispatchLoadRaidRewardsWorkflowReturnsRealRewardTables()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShRaidRewardBridgeFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadRaidRewardsWorkflow,
            new LoadRaidRewardsWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-raid-rewards");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadRaidRewardsWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-raid-rewards", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Single(response.Payload.Workflow.Tables);
        var table = response.Payload.Workflow.Tables.Single(table => table.RewardKind == "drop");
        Assert.Equal("Drop 000", table.DisplayName);
        Assert.Equal("nest_hole_drop_rewards.bin", table.ArchiveMember);
        Assert.Equal("0xAABBCCDD00112233", table.SourceTableHash);
        Assert.Equal(ProjectFileLayerDto.Base, table.Provenance.SourceLayer);
        var reward = table.Rewards[0];
        Assert.Equal("Exp. Candy L", reward.ItemName);
        Assert.Equal([40, 30, 20, 10, 5], reward.Values);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadRaidBonusRewardsWorkflowReturnsRealBonusRewardTables()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShRaidRewardBridgeFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadRaidBonusRewardsWorkflow,
            new LoadRaidBonusRewardsWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-raid-bonus-rewards");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadRaidBonusRewardsWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-raid-bonus-rewards", response.RequestId);
        Assert.NotNull(response.Payload);
        var table = Assert.Single(response.Payload.Workflow.Tables);
        Assert.Equal("bonus", table.RewardKind);
        Assert.Equal("Bonus 000", table.DisplayName);
        Assert.Equal("nest_hole_bonus_rewards.bin", table.ArchiveMember);
        Assert.Equal("0x1020304050607080", table.SourceTableHash);
        var reward = table.Rewards[0];
        Assert.Equal("Armorite Ore", reward.ItemName);
        Assert.Equal([1, 2, 3, 4, 5], reward.Values);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchRaidRewardEditValidatePlanAndApplyWritesNestDataPack()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShRaidRewardBridgeFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();

        var loadJson = SerializeRequest(
            KmCommandNames.LoadRaidRewardsWorkflow,
            new LoadRaidRewardsWorkflowRequest(temp.Paths),
            requestId: "request-raid-load");
        var loadResponse = DeserializeResponse<LoadRaidRewardsWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.NotNull(loadResponse.Payload);
        var dropTable = loadResponse.Payload.Workflow.Tables.Single(table => table.RewardKind == "drop");
        var startJson = SerializeRequest(
            KmCommandNames.StartEditSession,
            new StartEditSessionRequest(temp.Paths),
            requestId: "request-raid-start");
        var startResponse = DeserializeResponse<StartEditSessionResponse>(dispatcher.Dispatch(startJson));
        Assert.NotNull(startResponse.Payload);

        var updateJson = SerializeRequest(
            KmCommandNames.UpdateRaidRewardField,
            new UpdateRaidRewardFieldRequest(
                temp.Paths,
                startResponse.Payload.Session,
                dropTable.TableId,
                Slot: 2,
                Field: "star5Value",
                Value: "77"),
            requestId: "request-raid-update");
        var updateResponse = DeserializeResponse<UpdateRaidRewardFieldResponse>(dispatcher.Dispatch(updateJson));

        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Equal(77, updateResponse.Payload.Workflow.Tables.Single(table => table.TableId == dropTable.TableId).Rewards[1].Values[4]);
        Assert.Equal("workflow.raidRewards", Assert.Single(updateResponse.Payload.Session.PendingEdits).Domain);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-raid-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-raid-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.NotNull(planResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, updateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-raid-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "archive", "field", "resident", "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShNestHoleRewardArchive.Parse(outputPack.GetFileByName("nest_hole_drop_rewards.bin"));
        Assert.Equal(77u, outputArchive.Tables[0].Rewards[1].Values[4]);
    }

    [Fact]
    public void DispatchLoadPlacementWorkflowReturnsRealPlacedObjects()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPlacementBridgeFixtures.WriteBasePlacement(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-placement");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadPlacementWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-placement", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Objects.Count);
        var category = Assert.Single(response.Payload.Workflow.Categories!);
        Assert.Equal("items", category.Id);
        Assert.Equal("Items", category.Label);
        Assert.Equal(2, category.ObjectCount);
        var placedObject = response.Payload.Workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        Assert.Equal($"{SwShPlacementBridgeFixtures.AreaMember}|0|fieldItem|0|-", placedObject.ObjectId);
        Assert.Equal("Field item: Potion", placedObject.Label);
        Assert.Equal("Route 1", placedObject.Map);
        Assert.Equal(SwShPlacementBridgeFixtures.AreaMember, placedObject.ArchiveMember);
        Assert.Equal("items", placedObject.CategoryId);
        Assert.Equal("Items", placedObject.CategoryLabel);
        Assert.Equal(1u, placedObject.ItemId);
        Assert.Equal("Potion", placedObject.ItemName);
        Assert.Equal("0xAABBCCDD00112233", placedObject.ItemHash);
        Assert.Equal(1, placedObject.Quantity);
        Assert.Equal(10.5, placedObject.X);
        Assert.Equal(ProjectFileLayerDto.Base, placedObject.Provenance.SourceLayer);
        Assert.Contains(placedObject.Fields!, field => field.Field == "itemId" && !field.IsReadOnly);
        Assert.Contains(placedObject.Fields!, field => field.Field == "fieldItem.hash" && field.IsReadOnly);
        Assert.Equal(1, response.Payload.Workflow.Stats.TotalAreaCount);
        Assert.Equal(3, response.Payload.Workflow.Stats.SourceFileCount);
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "itemId");
    }

    [Fact]
    public void DispatchPlacementEditValidatePlanAndApplyWritesPlacementPack()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShPlacementBridgeFixtures.WriteBasePlacement(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();

        var loadJson = SerializeRequest(
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(temp.Paths),
            requestId: "request-placement-load");
        var loadResponse = DeserializeResponse<LoadPlacementWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.NotNull(loadResponse.Payload);
        var fieldItem = loadResponse.Payload.Workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        var startJson = SerializeRequest(
            KmCommandNames.StartEditSession,
            new StartEditSessionRequest(temp.Paths),
            requestId: "request-placement-start");
        var startResponse = DeserializeResponse<StartEditSessionResponse>(dispatcher.Dispatch(startJson));
        Assert.NotNull(startResponse.Payload);

        var updateJson = SerializeRequest(
            KmCommandNames.UpdatePlacementObjectField,
            new UpdatePlacementObjectFieldRequest(
                temp.Paths,
                startResponse.Payload.Session,
                fieldItem.ObjectId,
                Field: "quantity",
                Value: "5"),
            requestId: "request-placement-update");
        var updateResponse = DeserializeResponse<UpdatePlacementObjectFieldResponse>(dispatcher.Dispatch(updateJson));

        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Equal(5, updateResponse.Payload.Workflow.Objects.Single(placedObject => placedObject.ObjectId == fieldItem.ObjectId).Quantity);
        Assert.Equal("workflow.placement", Assert.Single(updateResponse.Payload.Session.PendingEdits).Domain);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-placement-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-placement-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.NotNull(planResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/placement.gfpak", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, updateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-placement-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/placement.gfpak", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "archive", "field", "resident", "placement.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShPlacementZoneArchive.Parse(outputPack.GetFileByName(SwShPlacementBridgeFixtures.AreaMember));
        Assert.Equal(5, outputArchive.Zones[0].FieldItems[0].Quantity);
    }

    [Fact]
    public void DispatchLoadFlagworkSaveWorkflowReturnsRealFlagworkRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShFlagworkBridgeFixtures.WriteBaseFlagwork(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadFlagworkSaveWorkflow,
            new LoadFlagworkSaveWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-flagwork-save");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadFlagworkSaveWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-flagwork-save", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.Workflow.Flags.Count);
        var flag = response.Payload.Workflow.Flags.Single(flag => flag.Name == "FE_TEST_FLAG");
        Assert.Equal("system_flags:0000", flag.FlagId);
        Assert.Equal("system_flags", flag.Table);
        Assert.Equal("Flag", flag.Kind);
        Assert.Equal("0x1122334455667788", flag.Hash);
        Assert.Equal("0x55667788", flag.Low32Key);
        Assert.Equal(ProjectFileLayerDto.Base, flag.Provenance.SourceLayer);
        var saveBlock = response.Payload.Workflow.SaveBlocks.Single(block => block.Name == "WK_SCENE_MAIN");
        Assert.Equal("scene_work:0000:0xDDEEFF00", saveBlock.BlockId);
        Assert.Equal("0xDDEEFF00", saveBlock.Key);
        Assert.Equal("Work", saveBlock.Kind);
        Assert.Equal(2, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadExeFsPatchWorkflowReturnsExeFsMainCompatibilityRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        var requestJson = SerializeRequest(
            KmCommandNames.LoadExeFsPatchWorkflow,
            new LoadExeFsPatchWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-exefs-patches");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadExeFsPatchWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-exefs-patches", response.RequestId);
        Assert.NotNull(response.Payload);
        var patch = Assert.Single(response.Payload.Workflow.Patches);
        Assert.Equal("exefs-main-compatibility", patch.PatchId);
        Assert.Equal("available", patch.Status);
        Assert.Equal("exefs/main", patch.TargetFile);
        Assert.Contains(patch.Details, detail => detail.StartsWith("Build ID:", StringComparison.Ordinal));
        Assert.Equal(3, response.Payload.Workflow.Segments.Count);
        Assert.Contains(
            response.Payload.Workflow.Checks,
            check => check.Name == "Patch code cave" && check.Status == "Pass");
        Assert.Contains(
            response.Payload.Workflow.Checks,
            check => check.Name == "Royal Candy immediate scan" && check.Status == "Info");
        Assert.Equal(ProjectFileLayerDto.Base, patch.Provenance.SourceLayer);
        Assert.Equal("exefs/main", patch.Provenance.SourceFile);
        Assert.Equal(26, response.Payload.Workflow.Stats.TotalCheckCount);
        Assert.Equal(24, response.Payload.Workflow.Stats.PassCount);
        Assert.Equal(0, response.Payload.Workflow.Stats.FailCount);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchExeFsStageValidatePlanAndApplyWritesLayeredMain()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        var baseMainPath = Path.Combine(temp.BaseExeFsPath, "main");
        var baseMainBytes = File.ReadAllBytes(baseMainPath);
        var dispatcher = new ProjectBridgeDispatcher();
        var stageJson = SerializeRequest(
            KmCommandNames.StageExeFsPatch,
            new StageExeFsPatchRequest(
                temp.Paths,
                PatchId: SwShExeFsPatchWorkflowService.MainPatchId,
                Session: null),
            requestId: "request-exefs-stage");

        var stageResponse = DeserializeResponse<StageExeFsPatchResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("workflow.exefsPatches", stageResponse.Payload.Session.PendingEdits[0].Domain);
        Assert.Equal(SwShExeFsPatchWorkflowService.MainPatchId, stageResponse.Payload.Session.PendingEdits[0].RecordId);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-exefs-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-exefs-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        var write = Assert.Single(planResponse.Payload.ChangePlan.Writes);
        Assert.Equal(SwShExeFsPatchWorkflowService.ExeFsMainPath, write.TargetRelativePath);
        Assert.False(write.ReplacesExistingOutput);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-exefs-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShExeFsPatchWorkflowService.ExeFsMainPath);
        Assert.Equal(baseMainBytes, File.ReadAllBytes(baseMainPath));

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputMainBytes = File.ReadAllBytes(outputMainPath);
        Assert.NotEqual(baseMainBytes, outputMainBytes);
        var outputNso = SwShNsoFile.Parse(outputMainBytes);
        var outputText = outputNso.Text.DecompressedData;
        Assert.Equal(EncodeCmpImmediate(register: 9, immediate: 3), ReadInstruction(outputText, 0x007BC1BC));
        Assert.Equal(EncodeCmpImmediate(register: 9, immediate: 3), ReadInstruction(outputText, 0x007BC1C4));
        Assert.NotEqual(0x2A0003E2u, ReadInstruction(outputText, 0x007B1F20));
        Assert.Contains(EncodeCmpImmediate(register: 22, immediate: 1128), ReadAlignedInstructions(outputText));
        Assert.Equal(SwShNsoFile.ComputeHash(outputText), outputNso.Text.Hash);
    }

    [Fact]
    public void DispatchTypeChartStageValidatePlanAndApplyWritesLayeredMain()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateTypeChartCompatibleNso());
        var dispatcher = new ProjectBridgeDispatcher();
        var loadJson = SerializeRequest(
            KmCommandNames.LoadTypeChartWorkflow,
            new LoadTypeChartWorkflowRequest(temp.Paths),
            requestId: "request-type-chart-load");
        var loadResponse = DeserializeResponse<LoadTypeChartWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(loadResponse.Error);
        Assert.NotNull(loadResponse.Payload);
        Assert.NotEqual("blocked", loadResponse.Payload.Workflow.InstallStatus);
        Assert.Equal(324, loadResponse.Payload.Workflow.Cells.Count);

        var values = Enumerable.Repeat(4, 18 * 18).ToArray();
        values[0] = 0;
        values[(1 * 18) + 4] = 2;
        var stageJson = SerializeRequest(
            KmCommandNames.StageTypeChart,
            new StageTypeChartRequest(temp.Paths, Session: null, Values: values),
            requestId: "request-type-chart-stage");
        var stageResponse = DeserializeResponse<StageTypeChartResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("workflow.typeChart", stageResponse.Payload.Session.PendingEdits[0].Domain);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-type-chart-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-type-chart-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        var write = Assert.Single(planResponse.Payload.ChangePlan.Writes);
        Assert.Equal("exefs/main", write.TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-type-chart-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));
        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(applyResponse.Payload.ApplyResult.WrittenFiles, relativePath => relativePath == "exefs/main");

        var reloadResponse = DeserializeResponse<LoadTypeChartWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(reloadResponse.Error);
        Assert.NotNull(reloadResponse.Payload);
        Assert.Equal("modified", reloadResponse.Payload.Workflow.InstallStatus);
        Assert.Equal(0, reloadResponse.Payload.Workflow.Cells.Single(cell => cell.AttackTypeIndex == 0 && cell.DefenseTypeIndex == 0).Effectiveness);
        Assert.Equal(2, reloadResponse.Payload.Workflow.Cells.Single(cell => cell.AttackTypeIndex == 1 && cell.DefenseTypeIndex == 4).Effectiveness);
    }

    [Fact]
    public void DispatchIvScreenStageValidatePlanAndApplyWritesLayeredMain()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
        var dispatcher = new ProjectBridgeDispatcher();

        var loadJson = SerializeRequest(
            KmCommandNames.LoadIvScreenWorkflow,
            new LoadIvScreenWorkflowRequest(temp.Paths),
            requestId: "request-iv-screen-load");
        var loadResponse = DeserializeResponse<LoadIvScreenWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(loadResponse.Error);
        Assert.NotNull(loadResponse.Payload);
        Assert.Equal("available", loadResponse.Payload.Workflow.InstallStatus);

        var stageJson = SerializeRequest(
            KmCommandNames.StageIvScreenInstall,
            new StageIvScreenInstallRequest(temp.Paths, Session: null),
            requestId: "request-iv-screen-stage");
        var stageResponse = DeserializeResponse<StageIvScreenInstallResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("workflow.ivScreen", stageResponse.Payload.Session.PendingEdits[0].Domain);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-iv-screen-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-iv-screen-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        var write = Assert.Single(planResponse.Payload.ChangePlan.Writes);
        Assert.Equal(SwShIvScreenWorkflowService.ExeFsMainPath, write.TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-iv-screen-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShIvScreenWorkflowService.ExeFsMainPath);

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputText = SwShNsoFile.Parse(File.ReadAllBytes(outputMainPath)).Text.DecompressedData;
        Assert.Equal(0x94001F27u, ReadInstruction(outputText, 0x0137F634));
        Assert.Equal(0x9400023Eu, ReadInstruction(outputText, 0x0138F268));
        Assert.Equal(0x97CFA48Eu, ReadInstruction(outputText, 0x0138FBE8));
        Assert.NotEqual(0x97CFC347u, ReadInstruction(outputText, 0x0138A2B4));
        Assert.NotEqual(0x97CFC229u, ReadInstruction(outputText, 0x0138A3CC));
        Assert.NotEqual(0x97CFC1EDu, ReadInstruction(outputText, 0x0138A47C));
        Assert.Equal(0x39592688u, ReadInstruction(outputText, 0x013912F4));
        Assert.Equal(0x34000068u, ReadInstruction(outputText, 0x013912F8));
        Assert.Equal(0x2A1F03E1u, ReadInstruction(outputText, 0x01391304));
        Assert.Equal(0x97FFDCBEu, ReadInstruction(outputText, 0x01392EA8));
        Assert.NotEqual(0x97CFBD40u, ReadInstruction(outputText, 0x0138AA50));
        Assert.Equal(0x2A1F03E8u, ReadInstruction(outputText, 0x0138AC88));
        Assert.Equal(0x2A1F03E0u, ReadInstruction(outputText, 0x0138AE28));
        Assert.Equal(0x2A1F03E8u, ReadInstruction(outputText, 0x0138AEAC));
        Assert.Equal(0xD503201Fu, ReadInstruction(outputText, 0x0138AEB0));
        Assert.Equal(0xD503201Fu, ReadInstruction(outputText, 0x0138AEB4));
        Assert.Equal(0x52800028u, ReadInstruction(outputText, 0x0138B230));
        Assert.Equal(0xAA1303E0u, ReadInstruction(outputText, 0x0138B3AC));
        Assert.NotEqual(0x97FFE9B0u, ReadInstruction(outputText, 0x0138B3B0));
        Assert.NotEqual(0x2A1F03E1u, ReadInstruction(outputText, 0x0138B3B4));
        Assert.Equal(0x39592408u, ReadInstruction(outputText, 0x0138B1FC));
        Assert.Equal(0x52000108u, ReadInstruction(outputText, 0x0138B200));
        Assert.Equal(0x340000A8u, ReadInstruction(outputText, 0x0139FB60));

        var reloadResponse = DeserializeResponse<LoadIvScreenWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(reloadResponse.Error);
        Assert.NotNull(reloadResponse.Payload);
        Assert.Equal("installed", reloadResponse.Payload.Workflow.InstallStatus);
    }

    [Fact]
    public void DispatchFashionUnlockStageValidatePlanAndApplyWritesLayeredMain()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
        var dispatcher = new ProjectBridgeDispatcher();

        var loadJson = SerializeRequest(
            KmCommandNames.LoadFashionUnlockWorkflow,
            new LoadFashionUnlockWorkflowRequest(temp.Paths),
            requestId: "request-fashion-unlock-load");
        var loadResponse = DeserializeResponse<LoadFashionUnlockWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(loadResponse.Error);
        Assert.NotNull(loadResponse.Payload);
        Assert.Equal("available", loadResponse.Payload.Workflow.InstallStatus);
        Assert.Equal("main.text+0x0143A2B0", loadResponse.Payload.Workflow.DirectGetterOffsetHex);
        Assert.Equal("main.text+0x0143A300", loadResponse.Payload.Workflow.MappedGetterOffsetHex);

        var stageJson = SerializeRequest(
            KmCommandNames.StageFashionUnlockInstall,
            new StageFashionUnlockInstallRequest(temp.Paths, Session: null),
            requestId: "request-fashion-unlock-stage");
        var stageResponse = DeserializeResponse<StageFashionUnlockInstallResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("workflow.fashionUnlock", stageResponse.Payload.Session.PendingEdits[0].Domain);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-fashion-unlock-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-fashion-unlock-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        var write = Assert.Single(planResponse.Payload.ChangePlan.Writes);
        Assert.Equal("exefs/main", write.TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-fashion-unlock-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == "exefs/main");

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputText = SwShNsoFile.Parse(File.ReadAllBytes(outputMainPath)).Text.DecompressedData;
        Assert.Equal(0x52800020u, ReadInstruction(outputText, 0x0143A2B0));
        Assert.Equal(0xD65F03C0u, ReadInstruction(outputText, 0x0143A2B4));
        Assert.Equal(0x52800020u, ReadInstruction(outputText, 0x0143A300));
        Assert.Equal(0xD65F03C0u, ReadInstruction(outputText, 0x0143A304));

        var reloadResponse = DeserializeResponse<LoadFashionUnlockWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(reloadResponse.Error);
        Assert.NotNull(reloadResponse.Payload);
        Assert.Equal("installed", reloadResponse.Payload.Workflow.InstallStatus);
    }

    [Fact]
    public void DispatchGymUniformRemovalStageValidatePlanAndApplyWritesLayeredIps()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
        var dispatcher = new ProjectBridgeDispatcher();

        var loadJson = SerializeRequest(
            KmCommandNames.LoadGymUniformRemovalWorkflow,
            new LoadGymUniformRemovalWorkflowRequest(temp.Paths),
            requestId: "request-gym-uniform-load");
        var loadResponse = DeserializeResponse<LoadGymUniformRemovalWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(loadResponse.Error);
        Assert.NotNull(loadResponse.Payload);
        Assert.Equal("available", loadResponse.Payload.Workflow.InstallStatus);
        Assert.Equal("main.text+0x01472600", loadResponse.Payload.Workflow.PatchOffsetHex);

        var stageJson = SerializeRequest(
            KmCommandNames.StageGymUniformRemovalInstall,
            new StageGymUniformRemovalInstallRequest(temp.Paths, Session: null),
            requestId: "request-gym-uniform-stage");
        var stageResponse = DeserializeResponse<StageGymUniformRemovalInstallResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("workflow.gymUniformRemoval", stageResponse.Payload.Session.PendingEdits[0].Domain);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-gym-uniform-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-gym-uniform-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        var write = Assert.Single(planResponse.Payload.ChangePlan.Writes);
        Assert.Equal(GymUniformRemovalSwordIpsPath, write.TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-gym-uniform-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == GymUniformRemovalSwordIpsPath);

        var outputIpsPath = Path.Combine(temp.OutputRootPath, "exefs", "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips");
        Assert.Equal(
            Convert.FromHexString("4950533332014726000008E0030032C0035FD645454F46"),
            File.ReadAllBytes(outputIpsPath));

        var reloadResponse = DeserializeResponse<LoadGymUniformRemovalWorkflowResponse>(dispatcher.Dispatch(loadJson));
        Assert.Null(reloadResponse.Error);
        Assert.NotNull(reloadResponse.Payload);
        Assert.Equal("installed", reloadResponse.Payload.Workflow.InstallStatus);
    }

    [Fact]
    public void DispatchLoadRoyalCandyWorkflowReturnsRealPreflightAndOutputs()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..],
            CreateRoyalCandyItemTable());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..],
            [0x01]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            CreateRoyalCandyShopData());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.NestDataPath["romfs/".Length..],
            [0x03]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.PlacementPath["romfs/".Length..],
            [0x04]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.BagEventScriptPath["romfs/".Length..],
            CreateRoyalCandyBagEventScript());
        temp.WriteBaseRomFsFile("bin/message/English/common/iteminfo.dat", [0x06]);
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", [0x07]);
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
        InstallEmptyBagHook(temp);
        var requestJson = SerializeRequest(
            KmCommandNames.LoadRoyalCandyWorkflow,
            new LoadRoyalCandyWorkflowRequest(temp.Paths),
            requestId: "request-royal-candy");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadRoyalCandyWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-royal-candy", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(3, response.Payload.Workflow.Workflows.Count);
        var workflow = response.Payload.Workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        Assert.Equal("Unlimited Royal Candy", workflow.Name);
        Assert.Equal("available", workflow.Status);
        Assert.Equal(1128, workflow.ItemId);
        Assert.Equal(ProjectFileLayerDto.Base, workflow.Provenance.SourceLayer);
        Assert.Contains(
            response.Payload.Workflow.Checks,
            check => check.CheckId.EndsWith(":item-data-stride", StringComparison.Ordinal)
                && check.Status == "Pass"
                && check.Message.Contains("1,129 item id", StringComparison.Ordinal));
        Assert.Contains(
            response.Payload.Workflow.Checks,
            check => check.CheckId.EndsWith(":game-flavor", StringComparison.Ordinal)
                && check.Status == "Pass"
                && check.Message.Contains("Pokemon Sword", StringComparison.Ordinal));
        Assert.Contains(
            response.Payload.Workflow.Checks,
            check => check.CheckId.Contains("patch-code-cave", StringComparison.Ordinal)
                && check.Status == "Pass");
        Assert.Contains(
            response.Payload.Workflow.Outputs,
            output => output.WorkflowId == workflow.WorkflowId
                && output.RelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath
                && output.Status == "ready");
        Assert.True(response.Payload.Workflow.Stats.TotalCheckCount >= 40);
        Assert.Equal(0, response.Payload.Workflow.Stats.FailCount);
        Assert.True(response.Payload.Workflow.Stats.SourceFileCount >= 10);
    }

    [Fact]
    public void DispatchRoyalCandyStageValidatePlanAndApplyWritesLayeredOutputs()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteRoyalCandyApplyInputs(temp);
        temp.WriteOutputFile(
            SwShRoyalCandyWorkflowService.ItemHashPath,
            new SwShItemHashTable(
            [
                new SwShItemHashEntry(50, 0xAABBCCDD00112233),
                new SwShItemHashEntry(1128, 0xAABBCCDD00112800),
            ]).Write());
        var baseItemPath = Path.Combine(temp.BaseRomFsPath, "bin", "pml", "item", "item.dat");
        var baseItemBytes = File.ReadAllBytes(baseItemPath);
        var dispatcher = new ProjectBridgeDispatcher();
        var stageJson = SerializeRequest(
            KmCommandNames.StageRoyalCandyWorkflow,
            new StageRoyalCandyWorkflowRequest(
                temp.Paths,
                WorkflowId: "royal-candy-unlimited",
                Session: null),
            requestId: "request-royal-candy-stage");

        var stageResponse = DeserializeResponse<StageRoyalCandyWorkflowResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("workflow.royalCandy", stageResponse.Payload.Session.PendingEdits[0].Domain);
        Assert.Equal("royal-candy-unlimited", stageResponse.Payload.Session.PendingEdits[0].RecordId);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-royal-candy-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-royal-candy-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ItemPath);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ItemHashPath);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ShopDataPath);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/itemname.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/itemname_classified.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/itemname_acc.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/itemname_acc_classified.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/itemname_plural.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/itemname_plural_classified.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/iteminfo.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.BagEventScriptPath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-royal-candy-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ItemPath);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.BagEventScriptPath);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ShopDataPath);
        Assert.Equal(baseItemBytes, File.ReadAllBytes(baseItemPath));

        var outputItemPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var baseItemBytesForAssert = File.ReadAllBytes(Path.Combine(temp.BaseRomFsPath, "bin", "pml", "item", "item.dat"));
        var outputItemTable = SwShItemTable.Parse(File.ReadAllBytes(outputItemPath));
        var royalCandy = outputItemTable.Records.Single(record => record.ItemId == 1128);
        Assert.Equal(baseItemBytesForAssert.Length + 0x30, File.ReadAllBytes(outputItemPath).Length);
        Assert.Equal(1129, royalCandy.RawRowIndex);
        Assert.Equal(1u, royalCandy.BuyPrice);
        Assert.Equal(0u, royalCandy.WattsPrice);
        Assert.Equal(SwShItemPouch.KeyItems, royalCandy.Pouch);
        Assert.True(royalCandy.CanUseOnPokemon);
        Assert.Equal(9, royalCandy.ItemType);

        AssertRoyalCandyTextRow(temp, "itemname.dat", "Royal Candy");
        AssertRoyalCandyTextRow(temp, "itemname_classified.dat", "Royal Candy");
        AssertRoyalCandyTextRow(temp, "itemname_acc.dat", "Royal Candy");
        AssertRoyalCandyTextRow(temp, "itemname_acc_classified.dat", "Royal Candy");
        AssertRoyalCandyTextRow(temp, "itemname_plural.dat", "Royal Candies");
        AssertRoyalCandyTextRow(temp, "itemname_plural_classified.dat", "Royal Candies");

        var outputInfo = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            "iteminfo.dat")));
        Assert.Contains("strange energy", outputInfo.Lines[1128].Text, StringComparison.Ordinal);

        var baseHashBytes = File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "pml",
            "item",
            "item_hash_to_index.dat"));
        var outputHashBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "pml",
            "item",
            "item_hash_to_index.dat"));
        Assert.Equal(baseHashBytes, outputHashBytes);
        var outputHashTable = SwShItemHashTable.Parse(outputHashBytes);
        Assert.Contains(outputHashTable.Entries, entry => entry.ItemId == 1128);

        var outputShopData = ReadRoyalCandyOutputShopData(temp);
        var dyniteShop = outputShopData.SingleShops.Single(shop => shop.Hash == RoyalCandyDyniteOreTraderShopHash);
        Assert.Equal([1127, 1129, 1606], dyniteShop.Inventory.Items);

        var outputBagScript = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script",
            "amx",
            "main_event_0020.amx"));
        Assert.NotEqual([0x05], outputBagScript);
        Assert.True(outputBagScript.Length > 0x38);

        var outputExeFsMain = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));
        Assert.NotEqual(SwShExeFsBridgeFixtures.CreateCompatibleNso(), outputExeFsMain);
    }

    [Fact]
    public void DispatchRoyalCandyStoryLimitsStagesSelectedCapsAndAppliesDynamicExeFsHook()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteRoyalCandyApplyInputs(temp);
        var baseMainPath = Path.Combine(temp.BaseExeFsPath, "main");
        var baseMainBytes = File.ReadAllBytes(baseMainPath);
        var dispatcher = new ProjectBridgeDispatcher();
        var stageJson = SerializeRequest(
            KmCommandNames.StageRoyalCandyWorkflow,
            new StageRoyalCandyWorkflowRequest(
                temp.Paths,
                WorkflowId: "royal-candy-story-limits",
                Session: null,
                LevelCaps:
                [
                    new RoyalCandyLevelCapSelectionDto(0, 12),
                    new RoyalCandyLevelCapSelectionDto(1, 16),
                ]),
            requestId: "request-royal-candy-story-limits-stage");

        var stageResponse = DeserializeResponse<StageRoyalCandyWorkflowResponse>(dispatcher.Dispatch(stageJson));

        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("royal-candy-story-limits", stageResponse.Payload.Session.PendingEdits[0].RecordId);
        Assert.StartsWith("storyLimits|0=12;1=16;2=20", stageResponse.Payload.Session.PendingEdits[0].NewValue, StringComparison.Ordinal);
        Assert.Contains(";24=90", stageResponse.Payload.Session.PendingEdits[0].NewValue, StringComparison.Ordinal);
        Assert.Equal(25, stageResponse.Payload.Workflow.Workflows.Single(workflow => workflow.WorkflowId == "royal-candy-story-limits").LevelCaps.Count);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-royal-candy-story-limits-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));

        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-royal-candy-story-limits-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ShopDataPath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-royal-candy-story-limits-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ShopDataPath);
        Assert.Equal(baseMainBytes, File.ReadAllBytes(baseMainPath));

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputMainBytes = File.ReadAllBytes(outputMainPath);
        Assert.NotEqual(baseMainBytes, outputMainBytes);
        var outputNso = SwShNsoFile.Parse(outputMainBytes);
        var outputText = outputNso.Text.DecompressedData;
        Assert.Equal(EncodeCmpImmediate(register: 9, immediate: 3), ReadInstruction(outputText, 0x007BC1BC));
        Assert.Equal(EncodeCmpImmediate(register: 9, immediate: 3), ReadInstruction(outputText, 0x007BC1C4));
        Assert.NotEqual(0x54000321u, ReadInstruction(outputText, 0x007BB208));
        Assert.NotEqual(0x54000141u, ReadInstruction(outputText, 0x007BB3C4));
        Assert.NotEqual(0x1A963316u, ReadInstruction(outputText, 0x007BAF3C));
        Assert.NotEqual(0x2A0003E2u, ReadInstruction(outputText, 0x007B1F20));
        Assert.Contains(EncodeCmpImmediate(register: 20, immediate: 1128), ReadAlignedInstructions(outputText));
        Assert.Contains(EncodeCmpImmediate(register: 19, immediate: 1128), ReadAlignedInstructions(outputText));
        Assert.Contains(EncodeMovzImmediate32(register: 0, immediate: 12), ReadAlignedInstructions(outputText));
        Assert.Equal(SwShNsoFile.ComputeHash(outputText), outputNso.Text.Hash);

        var outputShopData = ReadRoyalCandyOutputShopData(temp);
        var dyniteShop = outputShopData.SingleShops.Single(shop => shop.Hash == RoyalCandyDyniteOreTraderShopHash);
        Assert.Equal([1127, 1129, 1606], dyniteShop.Inventory.Items);
    }

    [Fact]
    public void DispatchRoyalCandyApplyPreservesStartingItemsBagHookSlots()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteRoyalCandyApplyInputs(temp);
        var dispatcher = new ProjectBridgeDispatcher();

        var stageStartingItemsJson = SerializeRequest(
            KmCommandNames.StageStartingItems,
            new StageStartingItemsRequest(
                temp.Paths,
                Session: null,
                Grants:
                [
                    new StartingItemGrantSelectionDto(2, 50, 3),
                ]),
            requestId: "request-starting-items-stage");
        var stageStartingItemsResponse = DeserializeResponse<StageStartingItemsResponse>(dispatcher.Dispatch(stageStartingItemsJson));
        Assert.Null(stageStartingItemsResponse.Error);
        Assert.NotNull(stageStartingItemsResponse.Payload);
        Assert.DoesNotContain(
            stageStartingItemsResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var planStartingItemsJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageStartingItemsResponse.Payload.Session),
            requestId: "request-starting-items-plan");
        var planStartingItemsResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planStartingItemsJson));
        Assert.Null(planStartingItemsResponse.Error);
        Assert.NotNull(planStartingItemsResponse.Payload);
        Assert.True(planStartingItemsResponse.Payload.ChangePlan.CanApply);

        var applyStartingItemsJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageStartingItemsResponse.Payload.Session,
                planStartingItemsResponse.Payload.ChangePlan),
            requestId: "request-starting-items-apply");
        var applyStartingItemsResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyStartingItemsJson));
        Assert.Null(applyStartingItemsResponse.Error);
        Assert.NotNull(applyStartingItemsResponse.Payload);
        Assert.DoesNotContain(
            applyStartingItemsResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var stageRoyalCandyJson = SerializeRequest(
            KmCommandNames.StageRoyalCandyWorkflow,
            new StageRoyalCandyWorkflowRequest(
                temp.Paths,
                WorkflowId: "royal-candy-unlimited",
                Session: null),
            requestId: "request-royal-candy-preserve-stage");
        var stageRoyalCandyResponse = DeserializeResponse<StageRoyalCandyWorkflowResponse>(dispatcher.Dispatch(stageRoyalCandyJson));
        Assert.Null(stageRoyalCandyResponse.Error);
        Assert.NotNull(stageRoyalCandyResponse.Payload);
        Assert.DoesNotContain(
            stageRoyalCandyResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var planRoyalCandyJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageRoyalCandyResponse.Payload.Session),
            requestId: "request-royal-candy-preserve-plan");
        var planRoyalCandyResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planRoyalCandyJson));
        Assert.Null(planRoyalCandyResponse.Error);
        Assert.NotNull(planRoyalCandyResponse.Payload);
        Assert.True(planRoyalCandyResponse.Payload.ChangePlan.CanApply);

        var applyRoyalCandyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageRoyalCandyResponse.Payload.Session,
                planRoyalCandyResponse.Payload.ChangePlan),
            requestId: "request-royal-candy-preserve-apply");
        var applyRoyalCandyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyRoyalCandyJson));
        Assert.Null(applyRoyalCandyResponse.Error);
        Assert.NotNull(applyRoyalCandyResponse.Payload);
        Assert.DoesNotContain(
            applyRoyalCandyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var loadBagHookJson = SerializeRequest(
            KmCommandNames.LoadBagHookWorkflow,
            new LoadBagHookWorkflowRequest(temp.Paths),
            requestId: "request-bag-hook-preserve-load");
        var loadBagHookResponse = DeserializeResponse<LoadBagHookWorkflowResponse>(dispatcher.Dispatch(loadBagHookJson));
        Assert.Null(loadBagHookResponse.Error);
        Assert.NotNull(loadBagHookResponse.Payload);

        var slot1 = loadBagHookResponse.Payload.Workflow.Slots.Single(slot => slot.Slot == 1);
        var slot2 = loadBagHookResponse.Payload.Workflow.Slots.Single(slot => slot.Slot == 2);
        var slot3 = loadBagHookResponse.Payload.Workflow.Slots.Single(slot => slot.Slot == 3);
        Assert.Equal("occupied", slot1.Status);
        Assert.Equal(1128, slot1.ItemId);
        Assert.Equal(1, slot1.Quantity);
        Assert.Equal("Royal Candy", slot1.Owner);
        Assert.Equal("occupied", slot2.Status);
        Assert.Equal(50, slot2.ItemId);
        Assert.Equal(3, slot2.Quantity);
        Assert.Equal("Starting Items", slot2.Owner);
        Assert.Equal("empty", slot3.Status);
        Assert.Null(slot3.ItemId);
        Assert.Null(slot3.Quantity);
    }

    [Fact]
    public void DispatchRoyalCandyCleanupStagesAndDeletesReviewedOutputs()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteRoyalCandyApplyInputs(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        ApplyRoyalCandyUnlimited(temp, dispatcher);
        File.Delete(Path.Combine(temp.BaseRomFsPath, "bin", "pml", "item", "item_hash_to_index.dat"));
        File.Delete(Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item_hash_to_index.dat"));

        var stageJson = SerializeRequest(
            KmCommandNames.StageRoyalCandyWorkflow,
            new StageRoyalCandyWorkflowRequest(
                temp.Paths,
                WorkflowId: "royal-candy-uninstall",
                Session: null),
            requestId: "request-royal-candy-cleanup-stage");

        var stageResponse = DeserializeResponse<StageRoyalCandyWorkflowResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.Single(stageResponse.Payload.Session.PendingEdits);
        Assert.Equal("royal-candy-uninstall", stageResponse.Payload.Session.PendingEdits[0].RecordId);
        Assert.Contains(
            stageResponse.Payload.Workflow.Diagnostics,
            diagnostic => diagnostic.Message.Contains("preflight is blocked", StringComparison.Ordinal));
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-royal-candy-cleanup-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));

        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.BagEventScriptPath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-royal-candy-cleanup-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.BagEventScriptPath);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ShopDataPath);
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "exefs", "main")));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin")));
        Assert.True(File.Exists(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script",
            "amx",
            "main_event_0020.amx")));
        var loadBagHookJson = SerializeRequest(
            KmCommandNames.LoadBagHookWorkflow,
            new LoadBagHookWorkflowRequest(temp.Paths),
            requestId: "request-royal-candy-cleanup-bag-hook-load");
        var loadBagHookResponse = DeserializeResponse<LoadBagHookWorkflowResponse>(dispatcher.Dispatch(loadBagHookJson));
        Assert.Null(loadBagHookResponse.Error);
        Assert.NotNull(loadBagHookResponse.Payload);
        Assert.Null(loadBagHookResponse.Payload.Workflow.Slots.Single(slot => slot.Slot == 1).ItemId);
        Assert.True(File.Exists(Path.Combine(temp.BaseExeFsPath, "main")));
        Assert.True(File.Exists(Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "script",
            "amx",
            "main_event_0020.amx")));
    }

    [Fact]
    public void DispatchRoyalCandyCleanupRestoresOnlyRoyalCandyShopEntries()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteRoyalCandyApplyInputs(temp);
        var dispatcher = new ProjectBridgeDispatcher();
        ApplyRoyalCandyUnlimited(temp, dispatcher);
        var shopPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
        var userEditedShopData = ReadRoyalCandyOutputShopData(temp).WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SwShShopBridgeFixtures.SingleShopHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 999),
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                RoyalCandyDyniteOreTraderShopHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 0,
                SwShShopInventoryEditAction.Set,
                [1127, 1129, 1606, RoyalCandyItemId]),
        ]);
        File.WriteAllBytes(shopPath, userEditedShopData);

        var stageJson = SerializeRequest(
            KmCommandNames.StageRoyalCandyWorkflow,
            new StageRoyalCandyWorkflowRequest(
                temp.Paths,
                WorkflowId: "royal-candy-uninstall",
                Session: null),
            requestId: "request-royal-candy-shop-cleanup-stage");
        var stageResponse = DeserializeResponse<StageRoyalCandyWorkflowResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.DoesNotContain(
            stageResponse.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            stageResponse.Payload.Workflow.Outputs,
            output => output.RelativePath == SwShRoyalCandyWorkflowService.ShopDataPath);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-royal-candy-shop-cleanup-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.True(planResponse.Payload.ChangePlan.CanApply);
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ShopDataPath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                temp.Paths,
                stageResponse.Payload.Session,
                planResponse.Payload.ChangePlan),
            requestId: "request-royal-candy-shop-cleanup-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));
        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.DoesNotContain(
            applyResponse.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains(
            applyResponse.Payload.ApplyResult.WrittenFiles,
            relativePath => relativePath == SwShRoyalCandyWorkflowService.ShopDataPath);

        var restoredShopData = ReadRoyalCandyOutputShopData(temp);
        var dyniteShop = restoredShopData.SingleShops.Single(shop => shop.Hash == RoyalCandyDyniteOreTraderShopHash);
        Assert.Equal([1127, RoyalCandyItemId, 1129, 1606, RoyalCandyItemId], dyniteShop.Inventory.Items);
        var userEditedShop = restoredShopData.SingleShops.Single(shop => shop.Hash == SwShShopBridgeFixtures.SingleShopHash);
        Assert.Equal([999, 18, 19], userEditedShop.Inventory.Items);
    }

    [Fact]
    public void DispatchRoyalCandyCleanupIgnoresShopOnlyExpCandyRemoval()
    {
        using var temp = TemporaryBridgeProject.Create();
        WriteRoyalCandyApplyInputs(temp);
        temp.WriteOutputFile(
            SwShRoyalCandyWorkflowService.ShopDataPath,
            new SwShShopDataFile(
            [
                new SwShSingleShopRecord(
                    SwShShopBridgeFixtures.SingleShopHash,
                    new SwShShopInventory([17, 18, 19])),
                new SwShSingleShopRecord(
                    RoyalCandyDyniteOreTraderShopHash,
                    new SwShShopInventory([1127, 1129, 1606])),
            ],
            Array.Empty<SwShMultiShopRecord>())
            .Write());

        var dispatcher = new ProjectBridgeDispatcher();
        var stageJson = SerializeRequest(
            KmCommandNames.StageRoyalCandyWorkflow,
            new StageRoyalCandyWorkflowRequest(
                temp.Paths,
                WorkflowId: "royal-candy-uninstall",
                Session: null),
            requestId: "request-royal-candy-shop-only-stage");
        var stageResponse = DeserializeResponse<StageRoyalCandyWorkflowResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);

        var cleanupWorkflow = stageResponse.Payload.Workflow.Workflows.Single(workflow => workflow.WorkflowId == "royal-candy-uninstall");
        Assert.Equal("blocked", cleanupWorkflow.Status);
        Assert.DoesNotContain(
            stageResponse.Payload.Workflow.Outputs,
            output => output.WorkflowId == "royal-candy-uninstall"
                && output.RelativePath == SwShRoyalCandyWorkflowService.ShopDataPath);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stageResponse.Payload.Session),
            requestId: "request-royal-candy-shop-only-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.False(planResponse.Payload.ChangePlan.CanApply);
        Assert.Empty(planResponse.Payload.ChangePlan.Writes);
    }

    [Fact]
    public void DispatchLoadSpreadsheetImportWorkflowReturnsGeneratedImportProfiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadSpreadsheetImportWorkflow,
            new LoadSpreadsheetImportWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-spreadsheet-import");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadSpreadsheetImportWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-spreadsheet-import", response.RequestId);
        Assert.NotNull(response.Payload);
        var profile = Assert.Single(response.Payload.Workflow.Profiles);
        Assert.Equal("items-price-csv", profile.ProfileId);
        Assert.Equal("Items Price CSV/TSV", profile.Name);
        Assert.Equal("csv/tsv", profile.SourceKind);
        Assert.Equal("items", profile.TargetWorkflow);
        Assert.Equal(ProjectFileLayerDto.Base, profile.Provenance.SourceLayer);
        Assert.Equal("romfs/bin/pml/item/item.dat", profile.Provenance.SourceFile);
        Assert.Equal(5, profile.Columns.Count);
        Assert.Equal("ItemId", profile.Columns[0].Header);
        Assert.True(profile.Columns[0].IsRequired);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchPreviewSpreadsheetImportReturnsItemsEditSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.csv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId,BuyPrice,WattsPrice
            1,450,21
            """);
        var requestJson = SerializeRequest(
            KmCommandNames.PreviewSpreadsheetImport,
            new PreviewSpreadsheetImportRequest(
                temp.Paths,
                ProfileId: "items-price-csv",
                SourcePath: sourcePath,
                Session: null),
            requestId: "request-spreadsheet-preview");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<PreviewSpreadsheetImportResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-spreadsheet-preview", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(1, response.Payload.Preview.AcceptedRowCount);
        Assert.Equal(0, response.Payload.Preview.RejectedRowCount);
        Assert.Equal(2, response.Payload.Session.PendingEdits.Count);
        Assert.All(response.Payload.Session.PendingEdits, edit => Assert.Equal("workflow.items", edit.Domain));
        Assert.Contains(
            response.Payload.Session.PendingEdits,
            edit => edit.Field == "buyPrice" && edit.NewValue == "450");
        Assert.DoesNotContain(
            response.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchModMergerStagesAndAppliesMatchingAndOneSidedRomFsFiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        const string relativePath = "romfs/bin/test.bin";
        const string directory1OnlyPath = "romfs/bin/dir1-only.bin";
        const string directory2OnlyPath = "romfs/bin/dir2-only.bin";
        var modDirectory1 = Directory.CreateDirectory(Path.Combine(temp.RootPath, "mod-1")).FullName;
        var modDirectory2 = Directory.CreateDirectory(Path.Combine(temp.RootPath, "mod-2")).FullName;
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0]);
        WriteBinaryFile(modDirectory1, relativePath, [1, 0, 0]);
        WriteBinaryFile(modDirectory2, relativePath, [0, 2, 0]);
        WriteBinaryFile(modDirectory1, directory1OnlyPath, [9]);
        WriteBinaryFile(modDirectory2, directory2OnlyPath, [8, 8]);
        var dispatcher = new ProjectBridgeDispatcher();

        var loadResponse = DeserializeResponse<LoadModMergerWorkflowResponse>(
            dispatcher.Dispatch(SerializeRequest(
                KmCommandNames.LoadModMergerWorkflow,
                new LoadModMergerWorkflowRequest(temp.Paths, modDirectory1, modDirectory2),
                requestId: "request-mod-merger-load")));
        Assert.Null(loadResponse.Error);
        Assert.NotNull(loadResponse.Payload);
        Assert.Equal(1, loadResponse.Payload.Workflow.Stats.MatchingFileCount);

        var stageResponse = DeserializeResponse<StageModMergeResponse>(
            dispatcher.Dispatch(SerializeRequest(
                KmCommandNames.StageModMerge,
                new StageModMergeRequest(
                    temp.Paths,
                    modDirectory1,
                    modDirectory2,
                    [relativePath, directory1OnlyPath],
                    [relativePath, directory2OnlyPath],
                    []),
                requestId: "request-mod-merger-stage")));
        Assert.Null(stageResponse.Error);
        Assert.NotNull(stageResponse.Payload);
        Assert.True(stageResponse.Payload.Preview.CanApply);
        Assert.Empty(stageResponse.Payload.Preview.Conflicts);
        Assert.Equal(3, stageResponse.Payload.Preview.Files.Count);

        var applyResponse = DeserializeResponse<ApplyModMergeResponse>(
            dispatcher.Dispatch(SerializeRequest(
                KmCommandNames.ApplyModMerge,
                new ApplyModMergeRequest(
                    temp.Paths,
                    modDirectory1,
                    modDirectory2,
                    [relativePath, directory1OnlyPath],
                    [relativePath, directory2OnlyPath],
                    []),
                requestId: "request-mod-merger-apply")));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal([directory1OnlyPath, directory2OnlyPath, relativePath], applyResponse.Payload.WrittenFiles);
        Assert.Equal(
            [1, 2, 0],
            File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "romfs", "bin", "test.bin")));
        Assert.Equal(
            [9],
            File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "romfs", "bin", "dir1-only.bin")));
        Assert.Equal(
            [8, 8],
            File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "romfs", "bin", "dir2-only.bin")));
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingEditSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateItemFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        Assert.Equal(450, response.Payload.Workflow.Items[1].BuyPrice);
        Assert.Equal("450", Assert.Single(response.Payload.Session.PendingEdits).NewValue);
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingSellPriceSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(
                temp.Paths,
                Session: null,
                ItemId: 1,
                Field: "sellPrice",
                Value: "175"),
            requestId: "request-items-sell-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateItemFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        var item = response.Payload.Workflow.Items[1];
        Assert.Equal(350, item.BuyPrice);
        Assert.Equal(175, item.SellPrice);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("sellPrice", edit.Field);
        Assert.Equal("175", edit.NewValue);
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingMetadataSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(
                temp.Paths,
                Session: null,
                ItemId: 1,
                Field: "healAmount",
                Value: "254"),
            requestId: "request-items-metadata-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateItemFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        var item = response.Payload.Workflow.Items[1];
        Assert.Equal(254, item.Metadata.HealAmount);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Pokemon Effects").Details,
            detail => detail.Label == "Heal" && detail.Value == "Half HP");
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("healAmount", edit.Field);
        Assert.Equal("254", edit.NewValue);
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingMachineMoveSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseMachineItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(
                temp.Paths,
                Session: null,
                ItemId: 1,
                Field: "machineMoveId",
                Value: "85"),
            requestId: "request-items-machine-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateItemFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        var item = response.Payload.Workflow.Items[1];
        Assert.Equal(85, item.Metadata.MachineMoveId);
        Assert.Equal("Thunderbolt", item.Metadata.MachineMoveName);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Machine" && detail.Value == "TM10 (slot 10) -> Thunderbolt (85)");
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("machineMoveId", edit.Field);
        Assert.Equal("85", edit.NewValue);
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingNamedBehaviorSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(
                temp.Paths,
                Session: null,
                ItemId: 1,
                Field: "attackBoost",
                Value: "6"),
            requestId: "request-items-behavior-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateItemFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        var item = response.Payload.Workflow.Items[1];
        Assert.Equal(0x60, item.Metadata.Boost0);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Battle").Details,
            detail => detail.Label == "Battle boosts" && detail.Value.StartsWith("Atk +6 stages", StringComparison.Ordinal));
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("attackBoost", edit.Field);
        Assert.Equal("6", edit.NewValue);
    }

    [Fact]
    public void DispatchUpdateTextEntryReturnsPendingTextSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTextBridgeFixtures.WriteBaseText(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(
                temp.Paths,
                Session: null,
                TextKey: "romfs/bin/message/English/common/story.dat#0",
                Value: "Hello there."),
            requestId: "request-text-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateTextEntryResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        Assert.Equal("Hello there.", response.Payload.Workflow.Entries[0].Value);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("workflow.text", edit.Domain);
        Assert.Equal("value", edit.Field);
        Assert.Equal("Hello there.", edit.NewValue);
    }

    [Fact]
    public void DispatchValidateEditSessionReturnsValidationPayload()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sessionResponseJson = new ProjectBridgeDispatcher().Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-session-validate");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<ValidateEditSessionResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.IsValid);
        Assert.Contains(response.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Info);
    }

    [Fact]
    public void DispatchValidateTextEditSessionRoutesToTextWorkflow()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTextBridgeFixtures.WriteBaseText(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sessionResponseJson = new ProjectBridgeDispatcher().Dispatch(SerializeRequest(
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(
                temp.Paths,
                Session: null,
                TextKey: "romfs/bin/message/English/common/story.dat#0",
                Value: "Hello there."),
            requestId: "request-text-edit"));
        var sessionResponse = DeserializeResponse<UpdateTextEntryResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-text-session-validate");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<ValidateEditSessionResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.IsValid);
        Assert.Contains(
            response.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Info
                && diagnostic.Message.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DispatchCreateChangePlanReturnsPlannedTargetFiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sessionResponseJson = new ProjectBridgeDispatcher().Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-change-plan");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<CreateChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-change-plan", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.ChangePlan.CanApply);
        var write = Assert.Single(response.Payload.ChangePlan.Writes);
        Assert.Equal("romfs/bin/pml/item/item.dat", write.TargetRelativePath);
        Assert.Equal(FileLayerDto.Base, Assert.Single(write.Sources).Layer);
    }

    [Fact]
    public void DispatchCreateTextChangePlanReturnsMessageTableTarget()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTextBridgeFixtures.WriteBaseText(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sessionResponseJson = new ProjectBridgeDispatcher().Dispatch(SerializeRequest(
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(
                temp.Paths,
                Session: null,
                TextKey: "romfs/bin/message/English/common/story.dat#0",
                Value: "Hello there."),
            requestId: "request-text-edit"));
        var sessionResponse = DeserializeResponse<UpdateTextEntryResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-text-change-plan");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<CreateChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.ChangePlan.CanApply);
        var write = Assert.Single(response.Payload.ChangePlan.Writes);
        Assert.Equal("romfs/bin/message/English/common/story.dat", write.TargetRelativePath);
        Assert.Equal(FileLayerDto.Base, Assert.Single(write.Sources).Layer);
    }

    [Fact]
    public void DispatchApplyChangePlanReturnsWrittenFiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var sessionResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var planResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-change-plan"));
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(planResponseJson);
        Assert.NotNull(planResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, sessionResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-change-plan-apply");

        var responseJson = dispatcher.Dispatch(requestJson);
        var response = DeserializeResponse<ApplyChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-change-plan-apply", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal("romfs/bin/pml/item/item.dat", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(450u, item.BuyPrice);
        Assert.DoesNotContain(
            response.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchApplyChangePlanWritesItemMetadata()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var sessionResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "healAmount", Value: "254"),
            requestId: "request-items-metadata-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var planResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-change-plan"));
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(planResponseJson);
        Assert.NotNull(planResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, sessionResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-change-plan-apply");

        var responseJson = dispatcher.Dispatch(requestJson);
        var response = DeserializeResponse<ApplyChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.Equal("romfs/bin/pml/item/item.dat", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(254, item.HealAmount);
        Assert.Equal(300u, item.BuyPrice);
        Assert.DoesNotContain(
            response.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchApplyChangePlanWritesItemMachineMove()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseMachineItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var sessionResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "machineMoveId", Value: "85"),
            requestId: "request-items-machine-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var planResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-change-plan"));
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(planResponseJson);
        Assert.NotNull(planResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, sessionResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-change-plan-apply");

        var responseJson = dispatcher.Dispatch(requestJson);
        var response = DeserializeResponse<ApplyChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.Equal("romfs/bin/pml/item/item.dat", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(10, item.MachineSlot);
        Assert.Equal((ushort)85, item.MachineMoveId);
        Assert.DoesNotContain(
            response.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchApplyChangePlanWritesItemNamedBehaviorFields()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var firstSessionJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "cureBurn", Value: "1"),
            requestId: "request-items-behavior-edit-1"));
        var firstSession = DeserializeResponse<UpdateItemFieldResponse>(firstSessionJson);
        Assert.NotNull(firstSession.Payload);
        var secondSessionJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(
                temp.Paths,
                firstSession.Payload.Session,
                ItemId: 1,
                Field: "attackBoost",
                Value: "6"),
            requestId: "request-items-behavior-edit-2"));
        var secondSession = DeserializeResponse<UpdateItemFieldResponse>(secondSessionJson);
        Assert.NotNull(secondSession.Payload);
        var planResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, secondSession.Payload.Session),
            requestId: "request-change-plan"));
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(planResponseJson);
        Assert.NotNull(planResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, secondSession.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-change-plan-apply");

        var responseJson = dispatcher.Dispatch(requestJson);
        var response = DeserializeResponse<ApplyChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.Equal("romfs/bin/pml/item/item.dat", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(0x04, item.CureStatusFlags);
        Assert.Equal(0x60, item.Boost0);
        Assert.DoesNotContain(
            response.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchApplyTextChangePlanWritesMessageTable()
    {
        using var temp = TemporaryBridgeProject.Create();
        SwShTextBridgeFixtures.WriteBaseText(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var sessionResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(
                temp.Paths,
                Session: null,
                TextKey: "romfs/bin/message/English/common/story.dat#0",
                Value: "Hello there."),
            requestId: "request-text-edit"));
        var sessionResponse = DeserializeResponse<UpdateTextEntryResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var planResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-text-change-plan"));
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(planResponseJson);
        Assert.NotNull(planResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, sessionResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-text-change-plan-apply");

        var responseJson = dispatcher.Dispatch(requestJson);
        var response = DeserializeResponse<ApplyChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.Equal("romfs/bin/message/English/common/story.dat", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            "story.dat");
        var output = SwShGameTextFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal("Hello there.", output.Lines[0].Text);
        Assert.Equal("Second line.", output.Lines[1].Text);
        Assert.DoesNotContain(
            response.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchUnsupportedCommandReturnsBridgeError()
    {
        var requestJson = JsonSerializer.Serialize(
            new BridgeRequest<object>("project.unsupported", new { }, RequestId: "request-unsupported"),
            BridgeJson.SerializerOptions);

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<object>(responseJson);

        Assert.Null(response.Payload);
        Assert.NotNull(response.Error);
        Assert.Equal("bridge.unsupportedCommand", response.Error.Code);
        Assert.Equal("request-unsupported", response.RequestId);
    }

    [Fact]
    public void DispatchSwordShieldOnlyCommandForScarletVioletProjectReturnsGameMismatch()
    {
        using var temp = TemporaryBridgeProject.Create();
        var requestJson = SerializeRequest(
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(temp.Paths with { SelectedGame = ProjectGameDto.Scarlet }),
            requestId: "request-swsh-only");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<object>(responseJson);

        Assert.Null(response.Payload);
        Assert.NotNull(response.Error);
        Assert.Equal("bridge.gameMismatch", response.Error.Code);
        Assert.Contains("Sword/Shield", response.Error.Message);
        Assert.Equal("request-swsh-only", response.RequestId);
    }

    [Fact]
    public void DispatchScarletVioletOnlyCommandForSwordShieldProjectReturnsGameMismatch()
    {
        using var temp = TemporaryBridgeProject.Create();
        var requestJson = SerializeRequest(
            KmCommandNames.LoadSvModMergerWorkflow,
            new LoadSvModMergerWorkflowRequest(temp.Paths with { SelectedGame = ProjectGameDto.Sword }, []),
            requestId: "request-sv-only");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<object>(responseJson);

        Assert.Null(response.Payload);
        Assert.NotNull(response.Error);
        Assert.Equal("bridge.gameMismatch", response.Error.Code);
        Assert.Contains("Scarlet/Violet", response.Error.Message);
        Assert.Equal("request-sv-only", response.RequestId);
    }

    private static string SerializeRequest<TPayload>(string command, TPayload payload, string requestId)
    {
        return JsonSerializer.Serialize(
            new BridgeRequest<TPayload>(command, payload, RequestId: requestId),
            BridgeJson.SerializerOptions);
    }

    private static void WriteBinaryFile(string rootPath, string relativePath, byte[] contents)
    {
        var path = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, 8), titleId);
        return data;
    }

    private static uint ReadInstruction(byte[] text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, 4));
    }

    private static IEnumerable<uint> ReadAlignedInstructions(byte[] text)
    {
        for (var offset = 0; offset <= text.Length - 4; offset += 4)
        {
            yield return ReadInstruction(text, offset);
        }
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeMovzImmediate32(int register, int immediate)
    {
        return (uint)(0x52800000 | ((immediate & 0xFFFF) << 5) | (register & 0x1F));
    }

    private static void WriteStaticEncounterBridgeFixture(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/script_event_data/event_encount_data.bin",
            CreateStaticEncounterTable(new SwShStaticEncounterStats(31, 30, 29, 27, 26, 28)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateIndexedTextTable(810, (25, "Pikachu"), (810, "Grookey")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateIndexedTextTable(4, (1, "Potion"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedTextTable(75, (1, "Scratch"), (2, "Growl"), (22, "Vine Whip"), (75, "Razor Leaf")));
    }

    private static byte[] CreateStaticEncounterTable(SwShStaticEncounterStats firstEncounterIvs)
    {
        return new SwShStaticEncounterArchive(
        [
            new SwShStaticEncounterRecord(
                0,
                0x1122334455667788,
                0x8877665544332211,
                new SwShStaticEncounterStats(1, 2, 3, 4, 5, 6),
                1,
                10,
                0,
                0x0102030405060708,
                0,
                true,
                1,
                50,
                17,
                810,
                2,
                25,
                1,
                firstEncounterIvs,
                3,
                [1, 2, 22, 75]),
            new SwShStaticEncounterRecord(
                1,
                0,
                0,
                new SwShStaticEncounterStats(0, 0, 0, 0, 0, 0),
                0,
                0,
                0,
                0x1111111111111111,
                0,
                false,
                0,
                25,
                0,
                25,
                0,
                0,
                0,
                new SwShStaticEncounterStats(-1, -1, -1, -1, -1, -1),
                0,
                [0, 0, 0, 0]),
        ]).Write();
    }

    private static byte[] CreateIndexedTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(_ => new SwShGameTextLine(string.Empty, Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }

    private static void WriteRoyalCandyApplyInputs(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..],
            CreateRoyalCandyItemTable());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..],
            CreateRoyalCandyHashTable());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            CreateRoyalCandyShopData());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.NestDataPath["romfs/".Length..],
            [0x03]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.PlacementPath["romfs/".Length..],
            [0x04]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.BagEventScriptPath["romfs/".Length..],
            CreateRoyalCandyBagEventScript());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "Rare Candy" : $"Item {itemId}"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname_classified.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "Rare Candy" : $"Classified Item {itemId}"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname_acc.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "Rare Candy" : $"Accessible Item {itemId}"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname_acc_classified.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "Rare Candy" : $"Accessible Classified Item {itemId}"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname_plural.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "Rare Candies" : $"Items {itemId}"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname_plural_classified.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "Rare Candies" : $"Classified Items {itemId}"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/iteminfo.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "A candy that raises level." : $"Info {itemId}"));
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
        InstallEmptyBagHook(temp);
    }

    private static void InstallEmptyBagHook(TemporaryBridgeProject temp)
    {
        var dispatcher = new ProjectBridgeDispatcher();
        var stageJson = SerializeRequest(
            KmCommandNames.StageBagHookInstall,
            new StageBagHookInstallRequest(temp.Paths, Session: null),
            requestId: "request-bag-hook-install-stage");
        var stage = DeserializeResponse<StageBagHookInstallResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stage.Error);
        Assert.NotNull(stage.Payload);
        Assert.DoesNotContain(stage.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stage.Payload.Session),
            requestId: "request-bag-hook-install-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(plan.Error);
        Assert.NotNull(plan.Payload);
        Assert.True(plan.Payload.ChangePlan.CanApply);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, stage.Payload.Session, plan.Payload.ChangePlan),
            requestId: "request-bag-hook-install-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));
        Assert.Null(apply.Error);
        Assert.NotNull(apply.Payload);
        Assert.DoesNotContain(apply.Payload.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    private static void ApplyRoyalCandyUnlimited(
        TemporaryBridgeProject temp,
        ProjectBridgeDispatcher dispatcher)
    {
        var stageJson = SerializeRequest(
            KmCommandNames.StageRoyalCandyWorkflow,
            new StageRoyalCandyWorkflowRequest(
                temp.Paths,
                WorkflowId: "royal-candy-unlimited",
                Session: null),
            requestId: "request-royal-candy-helper-stage");
        var stage = DeserializeResponse<StageRoyalCandyWorkflowResponse>(dispatcher.Dispatch(stageJson));
        Assert.Null(stage.Error);
        Assert.NotNull(stage.Payload);
        Assert.DoesNotContain(stage.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, stage.Payload.Session),
            requestId: "request-royal-candy-helper-plan");
        var plan = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(plan.Error);
        Assert.NotNull(plan.Payload);
        Assert.True(plan.Payload.ChangePlan.CanApply);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, stage.Payload.Session, plan.Payload.ChangePlan),
            requestId: "request-royal-candy-helper-apply");
        var apply = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));
        Assert.Null(apply.Error);
        Assert.NotNull(apply.Payload);
        Assert.DoesNotContain(apply.Payload.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    private static byte[] CreateRoyalCandyItemTable()
    {
        const int itemCount = 1129;
        const int rowSize = 0x30;
        const int headerSize = 0x44;
        const int entryTableOffset = 0x44;
        const int rowsStartOffset = 0x40;

        var rowsStart = headerSize + (itemCount * sizeof(ushort));
        var data = new byte[rowsStart + (itemCount * rowSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(data, itemCount);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), itemCount);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(rowsStartOffset), rowsStart);

        for (var itemId = 0; itemId < itemCount; itemId++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(entryTableOffset + (itemId * sizeof(ushort))),
                checked((ushort)itemId));
            var rowOffset = rowsStart + (itemId * rowSize);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset), itemId == 1128 ? 1u : 10u);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset + 0x04), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset + 0x08), 1u);
            data[rowOffset + 0x11] = (byte)SwShItemPouch.Items;
        }

        var rareCandyOffset = rowsStart + (50 * rowSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rareCandyOffset), 300u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rareCandyOffset + 0x04), 15u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rareCandyOffset + 0x08), 3u);
        data[rareCandyOffset + 0x11] = (byte)SwShItemPouch.Medicine;
        data[rareCandyOffset + 0x13] = 1;
        data[rareCandyOffset + 0x15] = 1;
        data[rareCandyOffset + 0x16] = 1;
        data[rareCandyOffset + 0x18] = 4;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(rareCandyOffset + 0x1A), 50);
        data[rareCandyOffset + 0x1F] = 0x04;

        return data;
    }

    private static byte[] CreateRoyalCandyHashTable()
    {
        var data = new byte[sizeof(int) + (3 * 0x10)];
        BinaryPrimitives.WriteInt32LittleEndian(data, 3);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(sizeof(int)), 0xAABBCCDD00112233);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(sizeof(int) + sizeof(ulong)), 50);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(sizeof(int) + 0x20), 0xAABBCCDD00112800);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(sizeof(int) + 0x20 + sizeof(ulong)), 1128);
        return data;
    }

    private static byte[] CreateRoyalCandyShopData()
    {
        return new SwShShopDataFile(
            [
                new SwShSingleShopRecord(
                    SwShShopBridgeFixtures.SingleShopHash,
                    new SwShShopInventory([17, 18, 19])),
                new SwShSingleShopRecord(
                    RoyalCandyDyniteOreTraderShopHash,
                    new SwShShopInventory([1127, RoyalCandyItemId, 1129, 1606])),
            ],
            Array.Empty<SwShMultiShopRecord>())
            .Write();
    }

    private static SwShShopDataFile ReadRoyalCandyOutputShopData(TemporaryBridgeProject temp)
    {
        return SwShShopDataFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin")));
    }

    private static byte[] CreateRoyalCandyTextTable(Func<int, string> getLine)
    {
        return SwShGameTextFile.Write(
            Enumerable.Range(0, 1129)
                .Select(itemId => new SwShGameTextLine(getLine(itemId), Flags: 0))
                .ToArray());
    }

    private static void AssertRoyalCandyTextRow(TemporaryBridgeProject temp, string fileName, string expectedText)
    {
        var outputText = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            fileName)));
        Assert.Equal(expectedText, outputText.Lines[1128].Text);
    }

    private static byte[] CreateRoyalCandyBagEventScript()
    {
        const ushort pawnMagic64 = 0xF1E1;
        const short pawnFlagCompact = 0x0004;
        const short defSize = 12;
        const int cellSize = 8;
        const int nativeCount = 77;
        const int natives = 0x38;
        const int libraries = natives + nativeCount * defSize;
        const int cod = libraries;
        const int codeCellCount = 5022;
        const uint duplicatedNativeHash = 0x0473BE4E;

        var prefix = new byte[cod];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 70 * defSize + 8), duplicatedNativeHash);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 76 * defSize + 8), duplicatedNativeHash);

        var cells = new ulong[codeCellCount];
        cells[3686] = 135;
        cells[3687] = 70;
        cells[3688] = 8;
        cells[4991] = 46;
        cells[4992] = 89;
        cells[4993] = 48;
        cells[5020] = 49;
        cells[5021] = unchecked((ulong)((4991 - 5020) * cellSize));

        var compactCode = CompactAmxCells(cells);
        var data = new byte[cod + compactCode.Length];
        Array.Copy(prefix, data, prefix.Length);
        Array.Copy(compactCode, 0, data, cod, compactCode.Length);

        var dat = cod + codeCellCount * cellSize;
        WriteAmxHeaderFields(
            data,
            size: data.Length,
            magic: pawnMagic64,
            flags: pawnFlagCompact,
            defSize: defSize,
            cod: cod,
            dat: dat,
            hea: dat,
            stp: dat,
            publics: natives,
            natives: natives,
            libraries: libraries,
            nameTable: libraries);
        return data;
    }

    private static byte[] CompactAmxCells(IEnumerable<ulong> cells)
    {
        var compact = new List<byte>();
        foreach (var cell in cells)
        {
            var value = unchecked((long)cell);
            var chunks = new List<byte>();
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
    }

    private static void WriteAmxHeaderFields(
        byte[] data,
        int size,
        ushort magic,
        short flags,
        short defSize,
        int cod,
        int dat,
        int hea,
        int stp,
        int publics,
        int natives,
        int libraries,
        int nameTable)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), size);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), magic);
        data[0x06] = 11;
        data[0x07] = 11;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x08), flags);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x0A), defSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), hea);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), stp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), publics);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), natives);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), nameTable);
    }

    private static BridgeResponse<TPayload> DeserializeResponse<TPayload>(string responseJson)
    {
        var response = JsonSerializer.Deserialize<BridgeResponse<TPayload>>(
            responseJson,
            BridgeJson.SerializerOptions);

        Assert.NotNull(response);

        return response;
    }
}

