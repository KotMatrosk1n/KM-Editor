// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.Flagwork;
using KM.Api.Items;
using KM.Api.Placement;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.RoyalCandy;
using KM.Api.Shops;
using KM.Api.SpreadsheetImport;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.Formats.SwSh;
using KM.Tools.Bridge;
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
        Assert.Equal("romfs/bin/pml/item/item.dat", item.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, item.Provenance.SourceLayer);
        Assert.Collection(
            response.Payload.Workflow.EditableFields,
            editableField =>
            {
                Assert.Equal("buyPrice", editableField.Field);
                Assert.Equal(999_999, editableField.MaximumValue);
            },
            editableField =>
            {
                Assert.Equal("sellPrice", editableField.Field);
                Assert.Equal(499_999, editableField.MaximumValue);
            },
            editableField =>
            {
                Assert.Equal("wattsPrice", editableField.Field);
                Assert.Equal(999_999, editableField.MaximumValue);
            },
            editableField =>
            {
                Assert.Equal("alternatePrice", editableField.Field);
                Assert.Equal(999_999, editableField.MaximumValue);
            });
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
        Assert.Equal(ProjectFileLayerDto.Base, trainer.Provenance.SourceLayer);
        Assert.Equal(ProjectFileLayerDto.Base, trainer.Provenance.TeamSourceLayer);
        var pokemon = Assert.Single(trainer.Team);
        Assert.Equal(810, pokemon.SpeciesId);
        Assert.Equal("Grookey", pokemon.Species);
        Assert.Equal(12, pokemon.Level);
        Assert.Equal(1, pokemon.HeldItemId);
        Assert.Equal("Potion", pokemon.HeldItem);
        Assert.Equal([1, 2, 0, 0], pokemon.MoveIds);
        Assert.Equal(9, response.Payload.Workflow.EditableFields.Count);
        Assert.Equal(2, response.Payload.Workflow.Stats.SourceFileCount);
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
        Assert.Equal("Poke Mart", shop.Location);
        Assert.Equal(ProjectFileLayerDto.Base, shop.Provenance.SourceLayer);
        var inventoryItem = shop.Inventory[0];
        Assert.Equal("Potion", inventoryItem.ItemName);
        Assert.Equal(300, inventoryItem.Price);
        Assert.Null(inventoryItem.StockLimit);
        Assert.Equal(1, response.Payload.Workflow.EditableFields.Count);
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
    public void DispatchLoadFlagworkSaveWorkflowReturnsSanitizedInspectorRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/flagwork.save.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "flags": [
                {
                  "flagId": "story.badge_1",
                  "name": "Badge 1 Obtained",
                  "category": "Story",
                  "valueKind": "boolean",
                  "defaultValue": "false",
                  "description": "First gym badge story flag."
                }
              ],
              "saveBlocks": [
                {
                  "blockId": "player.profile",
                  "name": "Player Profile",
                  "offset": 128,
                  "length": 64,
                  "description": "Player profile save block."
                }
              ]
            }
            """);
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
        var flag = Assert.Single(response.Payload.Workflow.Flags);
        Assert.Equal("story.badge_1", flag.FlagId);
        Assert.Equal("Badge 1 Obtained", flag.Name);
        Assert.Equal(ProjectFileLayerDto.Base, flag.Provenance.SourceLayer);
        var saveBlock = Assert.Single(response.Payload.Workflow.SaveBlocks);
        Assert.Equal("player.profile", saveBlock.BlockId);
        Assert.Equal(128, saveBlock.Offset);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadExeFsPatchWorkflowReturnsSanitizedPatchRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile(
            "kmeditor/exefs.patches.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "patches": [
                {
                  "patchId": "sample_patch",
                  "name": "Sample ExeFS Patch",
                  "targetFile": "exefs/main",
                  "patchKind": "IPS",
                  "status": "available",
                  "description": "Enable a safe ExeFS patch fixture."
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
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
        Assert.Equal("sample_patch", patch.PatchId);
        Assert.Equal("exefs/main", patch.TargetFile);
        Assert.Equal(ProjectFileLayerDto.Base, patch.Provenance.SourceLayer);
        Assert.Equal("exefs/kmeditor/exefs.patches.readmodel.json", patch.Provenance.SourceFile);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadRoyalCandyWorkflowReturnsSanitizedWorkflowRecipes()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/royal-candy.workflows.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "workflows": [
                {
                  "workflowId": "candy_reward_setup",
                  "name": "Candy Reward Setup",
                  "category": "Items",
                  "target": "items",
                  "status": "available",
                  "description": "Prepare a safe candy reward workflow fixture.",
                  "steps": [
                    {
                      "step": 1,
                      "label": "Review target",
                      "description": "Review target item and output preview."
                    }
                  ]
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadRoyalCandyWorkflow,
            new LoadRoyalCandyWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-royal-candy");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadRoyalCandyWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-royal-candy", response.RequestId);
        Assert.NotNull(response.Payload);
        var workflow = Assert.Single(response.Payload.Workflow.Workflows);
        Assert.Equal("candy_reward_setup", workflow.WorkflowId);
        Assert.Equal("Candy Reward Setup", workflow.Name);
        Assert.Equal(ProjectFileLayerDto.Base, workflow.Provenance.SourceLayer);
        var step = Assert.Single(workflow.Steps);
        Assert.Equal(1, step.Step);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadSpreadsheetImportWorkflowReturnsSanitizedImportProfiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/spreadsheet-import.profiles.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "profiles": [
                {
                  "profileId": "items_price_sheet",
                  "name": "Items Price Sheet",
                  "sourceKind": "xlsx",
                  "targetWorkflow": "items",
                  "status": "available",
                  "description": "Import item price columns from a workbook fixture.",
                  "columns": [
                    {
                      "column": 1,
                      "header": "ItemId",
                      "valueKind": "integer",
                      "isRequired": true,
                      "description": "Item identifier."
                    }
                  ]
                }
              ]
            }
            """);
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
        Assert.Equal("items_price_sheet", profile.ProfileId);
        Assert.Equal("Items Price Sheet", profile.Name);
        Assert.Equal("xlsx", profile.SourceKind);
        Assert.Equal("items", profile.TargetWorkflow);
        Assert.Equal(ProjectFileLayerDto.Base, profile.Provenance.SourceLayer);
        var column = Assert.Single(profile.Columns);
        Assert.Equal("ItemId", column.Header);
        Assert.True(column.IsRequired);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
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

    private static BridgeResponse<TPayload> DeserializeResponse<TPayload>(string responseJson)
    {
        var response = JsonSerializer.Deserialize<BridgeResponse<TPayload>>(
            responseJson,
            BridgeJson.SerializerOptions);

        Assert.NotNull(response);

        return response;
    }
}

