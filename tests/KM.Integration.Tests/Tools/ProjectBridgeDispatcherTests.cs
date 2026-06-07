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
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Workflows;
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
            });
    }

    [Fact]
    public void DispatchLoadItemsWorkflowReturnsSanitizedItemRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
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
        var item = Assert.Single(response.Payload.Workflow.Items);
        Assert.Equal("Potion", item.Name);
        Assert.Equal("romfs/kmeditor/items.readmodel.json", item.Provenance.SourceFile);
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
                Assert.Equal(999_999, editableField.MaximumValue);
            });
    }

    [Fact]
    public void DispatchLoadTextWorkflowReturnsSanitizedDialogueRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/text.dialogue.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "language": "en",
              "entries": [
                {
                  "textId": 10,
                  "label": "Greeting",
                  "value": "Welcome to the lab."
                }
              ],
              "dialogueReferences": [
                {
                  "dialogueId": "intro.lab.greeting",
                  "label": "Lab greeting",
                  "textId": 10,
                  "context": "Intro",
                  "preview": "Welcome to the lab."
                }
              ]
            }
            """);
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
        var entry = Assert.Single(response.Payload.Workflow.Entries);
        Assert.Equal("Greeting", entry.Label);
        Assert.Equal("en", entry.Language);
        Assert.Equal(ProjectFileLayerDto.Base, entry.Provenance.SourceLayer);
        var reference = Assert.Single(response.Payload.Workflow.DialogueReferences);
        Assert.Equal("intro.lab.greeting", reference.DialogueId);
        Assert.Equal(10, reference.TextId);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadTrainersWorkflowReturnsSanitizedTrainerRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/trainers.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "trainers": [
                {
                  "trainerId": 10,
                  "name": "Avery",
                  "trainerClass": "Pokemon Trainer",
                  "location": "Route 1",
                  "battleType": "Single",
                  "team": [
                    {
                      "slot": 1,
                      "species": "Grookey",
                      "level": 12,
                      "heldItem": null,
                      "moves": ["Scratch", "Growl"]
                    }
                  ]
                }
              ]
            }
            """);
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
        Assert.Equal("Pokemon Trainer", trainer.TrainerClass);
        Assert.Equal(ProjectFileLayerDto.Base, trainer.Provenance.SourceLayer);
        var pokemon = Assert.Single(trainer.Team);
        Assert.Equal("Grookey", pokemon.Species);
        Assert.Equal(12, pokemon.Level);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadShopsWorkflowReturnsSanitizedShopRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/shops.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "shops": [
                {
                  "shopId": "route_1_mart",
                  "name": "Route 1 Mart",
                  "location": "Route 1",
                  "currency": "Money",
                  "inventory": [
                    {
                      "slot": 1,
                      "itemId": 1,
                      "itemName": "Potion",
                      "price": 300,
                      "stockLimit": null
                    }
                  ]
                }
              ]
            }
            """);
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
        var shop = Assert.Single(response.Payload.Workflow.Shops);
        Assert.Equal("route_1_mart", shop.ShopId);
        Assert.Equal("Route 1 Mart", shop.Name);
        Assert.Equal(ProjectFileLayerDto.Base, shop.Provenance.SourceLayer);
        var inventoryItem = Assert.Single(shop.Inventory);
        Assert.Equal("Potion", inventoryItem.ItemName);
        Assert.Equal(300, inventoryItem.Price);
        Assert.Null(inventoryItem.StockLimit);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadEncountersWorkflowReturnsSanitizedEncounterTables()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/encounters.wild.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "tables": [
                {
                  "tableId": "route_1_grass_sword",
                  "location": "Route 1",
                  "area": "Grass",
                  "encounterType": "Overworld",
                  "gameVersion": "Sword",
                  "slots": [
                    {
                      "slot": 1,
                      "species": "Skwovet",
                      "levelMin": 3,
                      "levelMax": 5,
                      "weight": 35,
                      "timeOfDay": null,
                      "weather": "Any"
                    }
                  ]
                }
              ]
            }
            """);
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
        var table = Assert.Single(response.Payload.Workflow.Tables);
        Assert.Equal("route_1_grass_sword", table.TableId);
        Assert.Equal("Route 1", table.Location);
        Assert.Equal(ProjectFileLayerDto.Base, table.Provenance.SourceLayer);
        var slot = Assert.Single(table.Slots);
        Assert.Equal("Skwovet", slot.Species);
        Assert.Equal(3, slot.LevelMin);
        Assert.Equal(5, slot.LevelMax);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadRaidRewardsWorkflowReturnsSanitizedRewardTables()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/raid.rewards.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "tables": [
                {
                  "tableId": "den_001_rank_5_sword",
                  "denId": "den_001",
                  "rank": 5,
                  "gameVersion": "Sword",
                  "rewards": [
                    {
                      "slot": 1,
                      "itemId": 1,
                      "itemName": "Exp. Candy L",
                      "quantity": 2,
                      "weight": 40
                    }
                  ]
                }
              ]
            }
            """);
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
        var table = Assert.Single(response.Payload.Workflow.Tables);
        Assert.Equal("den_001_rank_5_sword", table.TableId);
        Assert.Equal("den_001", table.DenId);
        Assert.Equal(5, table.Rank);
        Assert.Equal(ProjectFileLayerDto.Base, table.Provenance.SourceLayer);
        var reward = Assert.Single(table.Rewards);
        Assert.Equal("Exp. Candy L", reward.ItemName);
        Assert.Equal(2, reward.Quantity);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchLoadPlacementWorkflowReturnsSanitizedPlacedObjects()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/placement.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "objects": [
                {
                  "objectId": "route_1_hidden_potion",
                  "objectType": "HiddenItem",
                  "label": "Hidden Potion",
                  "map": "Route 1",
                  "x": 10.5,
                  "y": 0,
                  "z": -4.25,
                  "rotationY": 90,
                  "scriptId": "script_hidden_item_001"
                }
              ]
            }
            """);
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
        var placedObject = Assert.Single(response.Payload.Workflow.Objects);
        Assert.Equal("route_1_hidden_potion", placedObject.ObjectId);
        Assert.Equal("Hidden Potion", placedObject.Label);
        Assert.Equal("Route 1", placedObject.Map);
        Assert.Equal(10.5, placedObject.X);
        Assert.Equal(ProjectFileLayerDto.Base, placedObject.Provenance.SourceLayer);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
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
    public void DispatchUpdateItemFieldReturnsPendingEditSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
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
        Assert.Equal(450, Assert.Single(response.Payload.Workflow.Items).BuyPrice);
        Assert.Equal("450", Assert.Single(response.Payload.Session.PendingEdits).NewValue);
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingSellPriceSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
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
        var item = Assert.Single(response.Payload.Workflow.Items);
        Assert.Equal(300, item.BuyPrice);
        Assert.Equal(175, item.SellPrice);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("sellPrice", edit.Field);
        Assert.Equal("175", edit.NewValue);
    }

    [Fact]
    public void DispatchValidateEditSessionReturnsValidationPayload()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
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
    public void DispatchCreateChangePlanReturnsPlannedTargetFiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
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
        Assert.Equal("romfs/kmeditor/items.readmodel.json", write.TargetRelativePath);
        Assert.Equal(FileLayerDto.Base, Assert.Single(write.Sources).Layer);
    }

    [Fact]
    public void DispatchApplyChangePlanReturnsWrittenFiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
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
        Assert.Equal("romfs/kmeditor/items.readmodel.json", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "kmeditor", "items.readmodel.json");
        Assert.Contains("\"buyPrice\": 450", File.ReadAllText(outputPath));
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

