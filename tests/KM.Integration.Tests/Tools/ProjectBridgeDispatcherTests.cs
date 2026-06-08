// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.DynamaxAdventures;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.Flagwork;
using KM.Api.Items;
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
using KM.Api.Text;
using KM.Api.Trades;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using KM.SwSh.Raids;
using KM.SwSh.RoyalCandy;
using KM.Tools.Bridge;
using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class ProjectBridgeDispatcherTests
{
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
        Assert.Collection(
            workflows,
            workflow =>
            {
                Assert.Equal("items", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("pokemon", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("moves", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("text", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("trainers", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("giftPokemon", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("tradePokemon", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("staticEncounters", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("rentalPokemon", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("dynamaxAdventures", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("shops", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("encounters", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("raidBattles", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("raidRewards", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("placement", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("flagworkSave", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("exefsPatches", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("royalCandy", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("spreadsheetImport", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            });
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
            detail => detail.Label == "Use flags 1" && detail.Value == "Restore HP");
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
            option => option.Value == 2 && option.Label == "002 Ivysaur");
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
        Assert.Equal("TM10 Magical Leaf", tm10.Label);
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
        Assert.Equal("Ability 2", encounter.AbilityLabel);
        Assert.Equal(4, encounter.GuaranteedPerfectIvs);
        Assert.Equal("0x1122334455667788", encounter.SingleCaptureFlagBlock);
        Assert.Equal("romfs/bin/appli/chika/data_table/underground_exploration_poke.bin", encounter.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, encounter.Provenance.SourceLayer);
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "guaranteedPerfectIvs");
        Assert.Contains(response.Payload.Workflow.EditableFields, field => field.Field == "ivAttack");
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
        Assert.Equal("romfs/bin/app/shop/shop_data.bin", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, updateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-shop-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/app/shop/shop_data.bin", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "app", "shop", "shop_data.bin");
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
                Slot: 2,
                Field: "probability",
                Value: "40"),
            requestId: "request-encounter-update");

        var updateResponse = DeserializeResponse<UpdateEncounterSlotFieldResponse>(dispatcher.Dispatch(updateJson));
        Assert.Null(updateResponse.Error);
        Assert.NotNull(updateResponse.Payload);
        Assert.Equal(40, updateResponse.Payload.Workflow.Tables.First(candidate => candidate.TableId == table.TableId).Slots[1].Weight);
        Assert.Single(updateResponse.Payload.Session.PendingEdits);

        var validateJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-encounter-validate");
        var validateResponse = DeserializeResponse<ValidateEditSessionResponse>(dispatcher.Dispatch(validateJson));
        Assert.Null(validateResponse.Error);
        Assert.NotNull(validateResponse.Payload);
        Assert.True(validateResponse.Payload.IsValid);

        var planJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, updateResponse.Payload.Session),
            requestId: "request-encounter-plan");
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(dispatcher.Dispatch(planJson));
        Assert.Null(planResponse.Error);
        Assert.NotNull(planResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(planResponse.Payload.ChangePlan.Writes).TargetRelativePath);

        var applyJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, updateResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-encounter-apply");
        var applyResponse = DeserializeResponse<ApplyChangePlanResponse>(dispatcher.Dispatch(applyJson));

        Assert.Null(applyResponse.Error);
        Assert.NotNull(applyResponse.Payload);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(applyResponse.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "archive", "field", "resident", "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal(40, outputArchive.Tables[0].SubTables[0].Slots[1].Probability);
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
            option => option.Value == 6 && option.Label == "6 Perfect IVs");
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
        Assert.Equal(2, response.Payload.Workflow.Tables.Count);
        var table = response.Payload.Workflow.Tables.Single(table => table.RewardKind == "drop");
        Assert.Equal("nest_hole_drop_rewards.bin", table.ArchiveMember);
        Assert.Equal("0xAABBCCDD00112233", table.SourceTableHash);
        Assert.Equal(ProjectFileLayerDto.Base, table.Provenance.SourceLayer);
        var reward = table.Rewards[0];
        Assert.Equal("Exp. Candy L", reward.ItemName);
        Assert.Equal([40, 30, 20, 10, 5], reward.Values);
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
        var placedObject = response.Payload.Workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        Assert.Equal($"{SwShPlacementBridgeFixtures.AreaMember}|0|fieldItem|0|-", placedObject.ObjectId);
        Assert.Equal("Field item: Potion", placedObject.Label);
        Assert.Equal("Route 1", placedObject.Map);
        Assert.Equal(SwShPlacementBridgeFixtures.AreaMember, placedObject.ArchiveMember);
        Assert.Equal(1u, placedObject.ItemId);
        Assert.Equal("Potion", placedObject.ItemName);
        Assert.Equal("0xAABBCCDD00112233", placedObject.ItemHash);
        Assert.Equal(1, placedObject.Quantity);
        Assert.Equal(10.5, placedObject.X);
        Assert.Equal(ProjectFileLayerDto.Base, placedObject.Provenance.SourceLayer);
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
    public void DispatchLoadRoyalCandyWorkflowReturnsRealPreflightAndOutputs()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..],
            new byte[(1128 + 1) * 0x30]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..],
            [0x01]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            [0x02]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.NestDataPath["romfs/".Length..],
            [0x03]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.PlacementPath["romfs/".Length..],
            [0x04]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.BagEventScriptPath["romfs/".Length..],
            [0x05]);
        temp.WriteBaseRomFsFile("bin/message/English/common/iteminfo.dat", [0x06]);
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", [0x07]);
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
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
        Assert.Equal("Install Unlimited Royal Candy", workflow.Name);
        Assert.Equal("available", workflow.Status);
        Assert.Equal(1128, workflow.ItemId);
        Assert.Equal(ProjectFileLayerDto.Base, workflow.Provenance.SourceLayer);
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
            write => write.TargetRelativePath == "romfs/bin/message/English/common/itemname.dat");
        Assert.Contains(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == "romfs/bin/message/English/common/iteminfo.dat");
        Assert.DoesNotContain(
            planResponse.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);

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
        Assert.Equal(baseItemBytes, File.ReadAllBytes(baseItemPath));

        var outputItemPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var outputItemTable = SwShItemTable.Parse(File.ReadAllBytes(outputItemPath));
        var royalCandy = outputItemTable.Records.Single(record => record.ItemId == 1128);
        Assert.Equal(300u, royalCandy.BuyPrice);
        Assert.Equal(15u, royalCandy.WattsPrice);
        Assert.Equal(SwShItemPouch.Medicine, royalCandy.Pouch);

        var outputNames = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            "itemname.dat")));
        Assert.Equal("Royal Candy", outputNames.Lines[1128].Text);

        var outputInfo = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            "iteminfo.dat")));
        Assert.Contains("strange energy", outputInfo.Lines[1128].Text, StringComparison.Ordinal);

        var outputHashTable = SwShItemHashTable.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "pml",
            "item",
            "item_hash_to_index.dat")));
        Assert.Contains(outputHashTable.Entries, entry => entry.ItemId == 1128);
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
            detail => detail.Label == "Battle boosts" && detail.Value.StartsWith("Atk 6", StringComparison.Ordinal));
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

    private static string SerializeRequest<TPayload>(string command, TPayload payload, string requestId)
    {
        return JsonSerializer.Serialize(
            new BridgeRequest<TPayload>(command, payload, RequestId: requestId),
            BridgeJson.SerializerOptions);
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
            new SwShItemHashTable(
            [
                new SwShItemHashEntry(50, 0xAABBCCDD00112233),
                new SwShItemHashEntry(1128, 0xAABBCCDD00112800),
            ]).Write());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            [0x02]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.NestDataPath["romfs/".Length..],
            [0x03]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.PlacementPath["romfs/".Length..],
            [0x04]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.BagEventScriptPath["romfs/".Length..],
            [0x05]);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "Rare Candy" : $"Item {itemId}"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/iteminfo.dat",
            CreateRoyalCandyTextTable(itemId => itemId == 50 ? "A candy that raises level." : $"Info {itemId}"));
        temp.WriteBaseExeFsFile("main", SwShExeFsBridgeFixtures.CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
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

        return data;
    }

    private static byte[] CreateRoyalCandyTextTable(Func<int, string> getLine)
    {
        return SwShGameTextFile.Write(
            Enumerable.Range(0, 1129)
                .Select(itemId => new SwShGameTextLine(getLine(itemId), Flags: 0))
                .ToArray());
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

