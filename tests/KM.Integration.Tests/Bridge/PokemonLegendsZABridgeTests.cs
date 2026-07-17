// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
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
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.Workflows;
using KM.Api.ZaCache;
using KM.Core.Projects;
using KM.Formats.Pokemon;
using KM.Formats.SwSh;
using KM.Formats.ZA;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.Formats.ZA.Generated.GameData;
using KM.Formats.ZA.Trinity;
using KM.Integration.Tests.Tools;
using KM.Tools.Bridge;
using KM.ZA.Data;
using KM.ZA.EvolutionItems;
using KM.ZA.Placement;
using KM.ZA.Workflows;
using Xunit;
using ItemBallAppearanceInfo = KM.Formats.ZA.Generated.Field.ItemBall.AppearanceInfo;
using ItemBallAppearanceSpawnerObjectInfo = KM.Formats.ZA.Generated.Field.ItemBall.AppearanceSpawnerObjectInfo;
using ItemBallSpawnerData = KM.Formats.ZA.Generated.Field.ItemBall.ItemBallSpawnerData;
using ItemBallSpawnerDataDB = KM.Formats.ZA.Generated.Field.ItemBall.ItemBallSpawnerDataDB;
using ItemBallSpawnerDataDBArray = KM.Formats.ZA.Generated.Field.ItemBall.ItemBallSpawnerDataDBArray;
using ItemBallTableInfo = KM.Formats.ZA.Generated.Field.ItemBall.TableInfo;

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
    public void PokemonLegendsZACacheBridgeCommandsReturnStatusAndSettings()
    {
        using var temp = CreatePokemonLegendsZAProject();
        var paths = CreatePaths(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var initial = Dispatch<ZaCacheStatusResponse>(
            dispatcher,
            KmCommandNames.GetZaCacheStatus,
            new GetZaCacheStatusRequest(paths),
            "request-za-cache-status");

        AssertSuccess(initial);
        Assert.Equal(ZaCacheModeDto.Balanced, initial.Payload!.Status.Settings.Mode);
        Assert.True(initial.Payload.Status.WarmupTotal > 0);
        Assert.Contains(ZaDataPaths.ShopItemArray, ZaCacheManager.WarmupVirtualPaths);

        var updated = Dispatch<ZaCacheStatusResponse>(
            dispatcher,
            KmCommandNames.UpdateZaCacheSettings,
            new UpdateZaCacheSettingsRequest(
                ZaCacheModeDto.Performance,
                2L * 1024 * 1024 * 1024,
                paths),
            "request-za-cache-settings");

        AssertSuccess(updated);
        Assert.Equal(ZaCacheModeDto.Performance, updated.Payload!.Status.Settings.Mode);
        Assert.Equal(2L * 1024 * 1024 * 1024, updated.Payload.Status.Settings.MaxCacheSizeBytes);

        var cleared = Dispatch<ZaCacheStatusResponse>(
            dispatcher,
            KmCommandNames.ClearZaCache,
            new ClearZaCacheRequest(paths),
            "request-za-cache-clear");

        AssertSuccess(cleared);
        Assert.Equal(ZaCacheModeDto.Performance, cleared.Payload!.Status.Settings.Mode);
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsPokemonData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray(includeAdditionalMachines: true));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(329, (4, "Poke Ball"), (17, "Potion"), (328, "TM001"), (329, "TM002")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        var dexOrderField = workflow.EditableFields.Single(field => field.Field == "regionalDexIndex");
        Assert.Equal("Z-A Dex Order", dexOrderField.Label);
        Assert.Equal(400, dexOrderField.MaximumValue);
        var learnedMove = Assert.Single(bulbasaur.Learnset);
        Assert.Equal(33, learnedMove.MoveId);
        Assert.Equal(1, learnedMove.Level);
        Assert.Equal((int?)0x0A01, learnedMove.RawLevel);
        Assert.Equal("Lv. 1 / Mastery Lv. 10", learnedMove.LevelLabel);
        Assert.Equal(1, Assert.Single(bulbasaur.Evolutions).Species);
        var tmGroup = bulbasaur.Compatibility.Single(group => group.GroupId == "tm");
        Assert.Equal(1, tmGroup.EnabledCount);
        Assert.Equal(2, tmGroup.Entries.Count);
        Assert.Contains(tmGroup.Entries, entry => entry.MoveId == 45 && entry.Label == "TM001 Growl" && entry.CanLearn);
        Assert.Contains(tmGroup.Entries, entry => entry.MoveId == 33 && entry.Label == "TM002 Tackle" && !entry.CanLearn);
        var unavailablePokemon = workflow.Pokemon.Single(row => row.PersonalId == 3);
        Assert.Equal(3, unavailablePokemon.SpeciesId);
        Assert.False(unavailablePokemon.DexPresence.IsPresentInGame);
    }

    [Fact]
    public void PokemonLegendsZAEvolutionItemsUseEligibleItemIds()
    {
        foreach (var (storedArgument, expectedItemId, expectedName) in new[]
                 {
                     (3, 82, "Fire Stone"),
                     (4, 83, "Thunder Stone"),
                 })
        {
            using var vanillaTemp = CreatePokemonLegendsZAProject();
            vanillaTemp.WriteBaseRomFsFile(
                ZaDataPaths.PersonalArray,
                CreatePersonalArray(evolutionCondition: 8, evolutionParameter: (ushort)storedArgument, evolutionSpecies: 2));
            vanillaTemp.WriteBaseRomFsFile(
                ZaDataPaths.ItemDataArray,
                CreateItemDataArray(includePokemonItemTypes: true));
            vanillaTemp.WriteBaseRomFsFile(
                ZaDataPaths.ItemNames("English"),
                CreateTextTable(expectedItemId + 1, (expectedItemId, expectedName)));
            var vanillaDispatcher = CreateDispatcherWithZaCache(vanillaTemp);

            var vanillaPokemon = Dispatch<LoadPokemonWorkflowResponse>(
                vanillaDispatcher,
                KmCommandNames.LoadPokemonWorkflow,
                new LoadPokemonWorkflowRequest(CreatePaths(vanillaTemp)),
                $"request-za-pokemon-vanilla-evolution-item-{storedArgument}");

            AssertSuccess(vanillaPokemon);
            var vanillaEvolution = Assert.Single(
                vanillaPokemon.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
            Assert.Equal(expectedItemId, vanillaEvolution.Argument);
            Assert.Equal(expectedName, vanillaEvolution.ArgumentValue);

            var addCustomEvolution = Dispatch<UpdatePokemonEvolutionResponse>(
                vanillaDispatcher,
                KmCommandNames.UpdatePokemonEvolution,
                new UpdatePokemonEvolutionRequest(
                    CreatePaths(vanillaTemp),
                    Session: null,
                    PersonalId: 1,
                    Action: "add",
                    Slot: null,
                    Method: 8,
                    Argument: 248,
                    Species: 2,
                    Form: 0,
                    Level: 0),
                $"request-za-pokemon-add-before-vanilla-evolution-item-{storedArgument}");
            AssertSuccess(addCustomEvolution);

            var moveCustomEvolution = Dispatch<UpdatePokemonEvolutionResponse>(
                vanillaDispatcher,
                KmCommandNames.UpdatePokemonEvolution,
                new UpdatePokemonEvolutionRequest(
                    CreatePaths(vanillaTemp),
                    addCustomEvolution.Payload!.Session,
                    PersonalId: 1,
                    Action: "moveUp",
                    Slot: 1,
                    Method: null,
                    Argument: null,
                    Species: null,
                    Form: null,
                    Level: null),
                $"request-za-pokemon-move-before-vanilla-evolution-item-{storedArgument}");
            AssertSuccess(moveCustomEvolution);
            var reorderedEvolutions = moveCustomEvolution.Payload!.Workflow.Pokemon
                .Single(row => row.PersonalId == 1)
                .Evolutions;
            Assert.Equal(248, reorderedEvolutions.Single(row => row.Slot == 0).Argument);
            Assert.Equal(expectedItemId, reorderedEvolutions.Single(row => row.Slot == 1).Argument);

            var reorderedPlan = Dispatch<CreateChangePlanResponse>(
                vanillaDispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(
                    CreatePaths(vanillaTemp),
                    moveCustomEvolution.Payload.Session,
                    ChangePlanOutputModeDto.TrinityModManager),
                $"request-za-pokemon-reordered-vanilla-evolution-plan-{storedArgument}");
            AssertSuccess(reorderedPlan);
            var reorderedApply = Dispatch<ApplyChangePlanResponse>(
                vanillaDispatcher,
                KmCommandNames.ApplyChangePlan,
                new ApplyChangePlanRequest(
                    CreatePaths(vanillaTemp),
                    moveCustomEvolution.Payload.Session,
                    reorderedPlan.Payload!.ChangePlan,
                    ChangePlanOutputModeDto.TrinityModManager),
                $"request-za-pokemon-reordered-vanilla-evolution-apply-{storedArgument}");
            AssertSuccess(reorderedApply);

            var writtenConversions = EvolutionItemConversionTable.Read(
                ReadZaOutputBytes(vanillaTemp, ZaDataPaths.EvolutionItemConversionArray));
            var twistedSpoonParameter = writtenConversions.Single(row => row.ItemId == 248).ParameterId;
            var writtenPersonal = ZaPersonalTable.GetRootAsZaPersonalTable(
                new ByteBuffer(ReadZaOutputBytes(vanillaTemp, ZaDataPaths.PersonalArray)));
            Assert.Equal(twistedSpoonParameter, writtenPersonal.Entry(1)!.Value.Evolutions(0)!.Value.Parameter);
            Assert.Equal(storedArgument, writtenPersonal.Entry(1)!.Value.Evolutions(1)!.Value.Parameter);

            var reloadedPokemon = Dispatch<LoadPokemonWorkflowResponse>(
                CreateDispatcherWithZaCache(vanillaTemp),
                KmCommandNames.LoadPokemonWorkflow,
                new LoadPokemonWorkflowRequest(CreatePaths(vanillaTemp)),
                $"request-za-pokemon-reordered-vanilla-evolution-reload-{storedArgument}");
            AssertSuccess(reloadedPokemon);
            var reloadedEvolutions = reloadedPokemon.Payload!.Workflow.Pokemon
                .Single(row => row.PersonalId == 1)
                .Evolutions;
            Assert.Equal(248, reloadedEvolutions.Single(row => row.Slot == 0).Argument);
            Assert.Equal(expectedItemId, reloadedEvolutions.Single(row => row.Slot == 1).Argument);
        }

        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 3, evolutionSpecies: 2));
        WriteZaOutput(
            temp,
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 86, evolutionSpecies: 2));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray());
        WriteZaOutput(
            temp,
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(includeCustomEvolutionItem: true));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(
                2483,
                (2, "Ultra Ball"),
                (81, "Moon Stone"),
                (86, "Tiny Mushroom"),
                (248, "Twisted Spoon"),
                (1861, "Malicious Armor"),
                (2482, "Metal Alloy")));
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(CreatePaths(temp)),
            "request-za-pokemon-evolution-items");

        AssertSuccess(pokemon);
        var workflow = pokemon.Payload!.Workflow;
        var bulbasaur = workflow.Pokemon.Single(row => row.PersonalId == 1);
        var evolution = Assert.Single(bulbasaur.Evolutions);
        Assert.Equal(1861, evolution.Argument);
        Assert.Equal("Malicious Armor", evolution.ArgumentValue);

        var useItem = workflow.EvolutionMethodOptions.Single(option => option.Value == 8);
        Assert.Contains(useItem.ArgumentOptions, option => option.Value == 2 && option.Label == "2 Ultra Ball");
        Assert.Contains(useItem.ArgumentOptions, option => option.Value == 248 && option.Label == "248 Twisted Spoon");
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
                CreatePaths(temp),
                Session: null,
                PersonalId: 1,
                Action: "add",
                Slot: null,
                Method: 8,
                Argument: 86,
                Species: 2,
                Form: 0,
                Level: 0),
            "request-za-pokemon-custom-evolution-item-label");

        AssertSuccess(update);
        var updatedEvolutions = update.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions;
        Assert.Equal(2, updatedEvolutions.Count);
        var updatedEvolution = updatedEvolutions.Single(row => row.Slot == 1);
        Assert.Equal(86, updatedEvolution.Argument);
        Assert.Equal("86 Tiny Mushroom", updatedEvolution.ArgumentValue);
    }

    [Fact]
    public void PokemonLegendsZAHeldItemEvolutionsUseItemIds()
    {
        using var temp = CreatePokemonLegendsZAProject();
        var personalArray = CreatePersonalArray(evolutionCondition: 19, evolutionParameter: 2, evolutionSpecies: 2);
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            personalArray);
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(
                2483,
                (2, "Ultra Ball"),
                (81, "Moon Stone"),
                (2482, "Metal Alloy")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-za-pokemon-held-item-evolution");

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
            new UpdatePokemonEvolutionRequest(paths, Session: null, PersonalId: 1, Action: "upsert", Slot: 0, Method: 20, Argument: 2, Species: 3, Form: 1, Level: 24),
            "request-za-pokemon-held-item-evolution-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-held-item-evolution-plan");
        AssertSuccess(plan);
        Assert.True(
            plan.Payload!.ChangePlan.CanApply,
            string.Join(Environment.NewLine, plan.Payload.ChangePlan.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(
            ZaDataPaths.EvolutionItemConversionArray,
            plan.Payload.ChangePlan.Writes[0].TargetRelativePath);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-held-item-evolution-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal(ZaDataPaths.EvolutionItemConversionArray, apply.Payload.ApplyResult.WrittenFiles[0]);

        var outputPath = Path.Combine(temp.OutputRootPath, "avalon", "data", "personal_array.bin");
        var writtenBytes = File.ReadAllBytes(outputPath);

        var written = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(writtenBytes));
        var writtenEvolution = written.Entry(1)!.Value.Evolutions(0);
        Assert.NotNull(writtenEvolution);
        Assert.Equal(20, writtenEvolution!.Value.Condition);
        var writtenConversions = EvolutionItemConversionTable.Read(
            ReadZaOutputBytes(temp, ZaDataPaths.EvolutionItemConversionArray));
        var ultraBallParameter = writtenConversions.Single(row => row.ItemId == 2).ParameterId;
        Assert.Equal(ultraBallParameter, writtenEvolution.Value.Parameter);
        Assert.Equal(3, writtenEvolution.Value.Species);
        Assert.Equal(1, writtenEvolution.Value.Form);
        Assert.Equal(24, writtenEvolution.Value.Level);
        Assert.Equal(25, written.Entry(1)!.Value.ZADexOrder);
        Assert.Equal(26, written.Entry(2)!.Value.ZADexOrder);

        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-za-pokemon-held-item-evolution-reload");
        AssertSuccess(reloaded);
        var reloadedEvolution = Assert.Single(
            reloaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
        Assert.Equal(2, reloadedEvolution.Argument);
        Assert.Equal("Ultra Ball", reloadedEvolution.ArgumentValue);

        using var resizeTemp = CreatePokemonLegendsZAProject();
        resizeTemp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(
                evolutionCondition: 19,
                evolutionParameter: 2,
                evolutionSpecies: 2,
                bulbasaurDexOrder: 173,
                ivysaurDexOrder: 400,
                bulbasaurSpeciesReserved3: 8,
                ivysaurSpeciesReserved3: 4));
        var resizePaths = CreatePaths(resizeTemp);
        var resizeDispatcher = CreateDispatcherWithZaCache(resizeTemp);
        var add = Dispatch<UpdatePokemonEvolutionResponse>(
            resizeDispatcher,
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(resizePaths, Session: null, PersonalId: 1, Action: "add", Slot: null, Method: 20, Argument: 2, Species: 3, Form: 1, Level: 24),
            "request-za-pokemon-held-item-evolution-add");
        AssertSuccess(add);
        var addPlan = Dispatch<CreateChangePlanResponse>(
            resizeDispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(resizePaths, add.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-held-item-evolution-add-plan");
        AssertSuccess(addPlan);
        Assert.True(addPlan.Payload!.ChangePlan.CanApply);
        var addApply = Dispatch<ApplyChangePlanResponse>(
            resizeDispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(resizePaths, add.Payload.Session, addPlan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-held-item-evolution-add-apply");
        AssertSuccess(addApply);
        Assert.DoesNotContain(addApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var resizedBytes = File.ReadAllBytes(Path.Combine(resizeTemp.OutputRootPath, "avalon", "data", "personal_array.bin"));
        var resized = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(resizedBytes));
        var resizedBulbasaur = resized.Entry(1)!.Value;
        var addedEvolution = resizedBulbasaur.Evolutions(1);
        Assert.NotNull(addedEvolution);
        Assert.Equal(2, resizedBulbasaur.EvolutionsLength);
        Assert.Equal(20, addedEvolution!.Value.Condition);
        var resizedConversions = EvolutionItemConversionTable.Read(
            ReadZaOutputBytes(resizeTemp, ZaDataPaths.EvolutionItemConversionArray));
        var resizedUltraBallParameter = resizedConversions.Single(row => row.ItemId == 2).ParameterId;
        Assert.Equal(resizedUltraBallParameter, addedEvolution.Value.Parameter);
        Assert.Equal(3, addedEvolution.Value.Species);
        Assert.Equal(173, resizedBulbasaur.ZADexOrder);
        Assert.Equal(400, resized.Entry(2)!.Value.ZADexOrder);
        Assert.Equal(8u, resizedBulbasaur.Species!.Value.Reserved3);
        Assert.Equal(4u, resized.Entry(2)!.Value.Species!.Value.Reserved3);
        var resizedBulbasaurOffset = FindZaPersonalTableOffset(resizedBytes, personalId: 1);
        var resizedDexLocation = ReadFlatBufferTableFieldLocation(
            resizedBytes,
            resizedBulbasaurOffset,
            fieldIndex: 2);
        var resizedType1Location = ReadFlatBufferTableFieldLocation(
            resizedBytes,
            resizedBulbasaurOffset,
            fieldIndex: 3);
        Assert.Equal(173, BinaryPrimitives.ReadUInt16LittleEndian(resizedBytes.AsSpan(resizedDexLocation, sizeof(ushort))));
        Assert.NotEqual(resizedDexLocation + sizeof(byte), resizedType1Location);
    }

    [Fact]
    public void PokemonLegendsZAReservedParameter50DisplaysHistoricalRazorFang()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 20, evolutionParameter: 50, evolutionSpecies: 2));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (50, "Rare Candy"), (327, "Razor Fang")));

        var response = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(CreatePaths(temp)),
            "request-za-reserved-evolution-parameter-50");

        AssertSuccess(response);
        var evolution = Assert.Single(
            response.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
        Assert.Equal(327, evolution.Argument);
        Assert.Equal("Razor Fang", evolution.ArgumentValue);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(42)]
    public void PokemonLegendsZAConversionBackedUseItemMethodsRoundTripThroughConversionTable(int method)
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: (ushort)method, evolutionParameter: 3, evolutionSpecies: 2));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(includePokemonItemTypes: true));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(83, (82, "Fire Stone")));
        var paths = CreatePaths(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var loaded = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            $"request-za-converted-use-item-{method}-load");
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
            $"request-za-converted-use-item-{method}-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            $"request-za-converted-use-item-{method}-plan");
        AssertSuccess(plan);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                update.Payload.Session,
                plan.Payload!.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            $"request-za-converted-use-item-{method}-apply");
        AssertSuccess(apply);

        var written = ZaPersonalTable.GetRootAsZaPersonalTable(
            new ByteBuffer(ReadZaOutputBytes(temp, ZaDataPaths.PersonalArray)));
        Assert.Equal(3, written.Entry(1)!.Value.Evolutions(0)!.Value.Parameter);
        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            $"request-za-converted-use-item-{method}-reload");
        AssertSuccess(reloaded);
        Assert.Equal(
            82,
            Assert.Single(reloaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions).Argument);
    }

    [Fact]
    public void PokemonLegendsZALegacyRawEvolutionItemArgumentsAreMigrated()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 3, evolutionSpecies: 2));
        WriteZaOutput(
            temp,
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 248, evolutionSpecies: 2));
        WriteZaOutput(
            temp,
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(includeCustomEvolutionItem: true));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(249, (248, "Twisted Spoon")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, Session: null, PersonalId: 1, Field: "hp", Value: "99"),
            "request-za-pokemon-legacy-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-legacy-evolution-item-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(
            plan.Payload.ChangePlan.Writes,
            write => write.TargetRelativePath == ZaDataPaths.EvolutionItemConversionArray);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                update.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-legacy-evolution-item-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var writtenConversions = EvolutionItemConversionTable.Read(
            ReadZaOutputBytes(temp, ZaDataPaths.EvolutionItemConversionArray));
        var twistedSpoonParameter = writtenConversions.Single(row => row.ItemId == 248).ParameterId;
        var writtenPersonal = ZaPersonalTable.GetRootAsZaPersonalTable(
            new ByteBuffer(ReadZaOutputBytes(temp, ZaDataPaths.PersonalArray)));
        var writtenEntry = writtenPersonal.Entry(1)!.Value;
        Assert.Equal(99, writtenEntry.BaseStats!.Value.Hp);
        Assert.Equal(twistedSpoonParameter, writtenEntry.Evolutions(0)!.Value.Parameter);
        Assert.Equal(25, writtenEntry.ZADexOrder);

        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-za-pokemon-legacy-evolution-item-reload");
        AssertSuccess(reloaded);
        var evolution = Assert.Single(
            reloaded.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).Evolutions);
        Assert.Equal(248, evolution.Argument);
        Assert.Equal("Twisted Spoon", evolution.ArgumentValue);
    }

    [Fact]
    public void PokemonLegendsZAEvolutionConditionArgumentsUseNamedValues()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 62, evolutionParameter: 20, evolutionSpecies: 904));
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var pokemon = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(CreatePaths(temp)),
            "request-za-pokemon-evolution-conditions");

        AssertSuccess(pokemon);
        var workflow = pokemon.Payload!.Workflow;
        var bulbasaur = workflow.Pokemon.Single(row => row.PersonalId == 1);
        var evolution = Assert.Single(bulbasaur.Evolutions);
        Assert.Equal(62, evolution.Method);
        Assert.Equal("Use Barb Barrage 20 Times", evolution.MethodName);
        Assert.Equal("20 Barb Barrage uses", evolution.ArgumentValue);

        var pancham = workflow.EvolutionMethodOptions.Single(option => option.Value == 30);
        Assert.Equal("none", pancham.ArgumentKind);
        Assert.Contains("Dark-Type Teammate", pancham.Label, StringComparison.Ordinal);
        Assert.Empty(pancham.ArgumentOptions);

        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 31).ArgumentOptions,
            option => option.Value == 1 && option.Label == "Hisuian rain rule");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 36).ArgumentOptions,
            option => option.Value == 44 && option.Label == "Solgaleo branch");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 36).ArgumentOptions,
            option => option.Value == 45 && option.Label == "Lunala branch");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 43).ArgumentOptions,
            option => option.Value == 3 && option.Label == "3 critical hits");
        Assert.Contains(
            workflow.EvolutionMethodOptions.Single(option => option.Value == 44).ArgumentOptions,
            option => option.Value == 49 && option.Label == "49 HP lost");
    }

    [Fact]
    public void PokemonLegendsZAProjectListsPokemonTrainersGiftTradeMovesItemsShopsAndModMergerWorkflows()
    {
        using var temp = CreatePokemonLegendsZAProject();
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        Assert.Contains(tackle.Flags, flag => flag.Field == "protect" && flag.Enabled);
        Assert.Contains(tackle.Flags, flag => flag.Field == "mirror" && flag.Enabled);
        Assert.Contains(tackle.Flags, flag => flag.Field == "metronome" && flag.Enabled);
        Assert.DoesNotContain(tackle.Flags, flag => flag.Field == "charge" && flag.Enabled);
        Assert.DoesNotContain(tackle.Flags, flag => flag.Field == "punch" && flag.Enabled);
        Assert.DoesNotContain(tackle.Flags, flag => flag.Field == "failEncore" && flag.Enabled);
        Assert.Contains(workflow.EditableFields, field => field.Field == "power" && field.Label == "Power");
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsAncientPowerMoveEffects()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.MoveDataArray, CreateAncientPowerMoveDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(246, (246, "Ancient Power")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveDescriptions("English"),
            CreateTextTable(246, (246, "The user attacks with a prehistoric power.")));
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var moves = Dispatch<LoadMovesWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadMovesWorkflow,
            new LoadMovesWorkflowRequest(CreatePaths(temp)),
            "request-za-ancient-power");

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

    [Fact]
    public void PokemonLegendsZAProjectLoadsItemData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray(includePokemonItemTypes: true));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(
                2564,
                (4, "Poke Ball"),
                (17, "Potion"),
                (82, "Fire Stone"),
                (328, "TM001"),
                (2563, "Meganiumite")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var items = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(CreatePaths(temp)),
            "request-za-items");

        AssertSuccess(items);
        var workflow = items.Payload!.Workflow;
        Assert.Equal("Items", workflow.Summary.Label);
        Assert.Equal(WorkflowAvailabilityDto.Available, workflow.Summary.Availability);
        Assert.Equal(5, workflow.Items.Count);
        Assert.Equal(5, workflow.Stats.TotalItemCount);
        foreach (var item in workflow.Items)
        {
            foreach (var field in workflow.EditableFields)
            {
                Assert.True(
                    item.FieldValues.ContainsKey(field.Field),
                    $"Item {item.ItemId} is missing editable field value '{field.Field}'.");
            }
        }

        var pokeBall = workflow.Items.Single(item => item.ItemId == 4);
        Assert.Equal("Poke Ball", pokeBall.Name);
        Assert.Equal("Balls", pokeBall.Category);
        Assert.Equal(100, pokeBall.BuyPrice);
        Assert.Null(pokeBall.Metadata.MachineSlot);
        Assert.Equal(4, pokeBall.FieldValues["itemType"]);
        Assert.Equal(100, pokeBall.FieldValues["price"]);
        Assert.Equal(0, pokeBall.FieldValues["megaShardPrice"]);
        Assert.Equal(0, pokeBall.FieldValues["colorfulScrewPrice"]);
        Assert.Equal(1, pokeBall.FieldValues["pocket"]);
        Assert.Equal(999, pokeBall.FieldValues["stackCap"]);
        Assert.Equal(1, pokeBall.FieldValues["sortOrder"]);
        Assert.Equal(0, pokeBall.FieldValues["canUseOnPokemon"]);
        Assert.Equal(0, pokeBall.FieldValues["evolutionItem"]);
        Assert.Equal(0, pokeBall.FieldValues["machineMoveId"]);
        Assert.Equal(-1, pokeBall.FieldValues["mintNature"]);
        var mintNatureField = workflow.EditableFields.Single(field => field.Field == "mintNature");
        Assert.Equal(-1, mintNatureField.MinimumValue);
        Assert.Equal(24, mintNatureField.MaximumValue);
        Assert.Contains(mintNatureField.Options, option => option.Value == -1 && option.Label == "-1 None");
        Assert.Contains(mintNatureField.Options, option => option.Value == 0 && option.Label == "0 Hardy");
        Assert.Equal(
            "-1 None",
            pokeBall
                .DetailGroups
                .Single(group => group.Label == "Effects")
                .Details
                .Single(detail => detail.Label == "Mint nature")
                .Value);
        var potion = workflow.Items.Single(item => item.ItemId == 17);
        Assert.True(potion.Metadata.CanUseOnPokemon);
        Assert.Equal(1, potion.FieldValues["canUseOnPokemon"]);
        Assert.Equal(0, potion.FieldValues["evolutionItem"]);
        Assert.Equal(20, potion.FieldValues["healPower"]);
        Assert.Equal(1, potion.FieldValues["canUseInBattle"]);
        var fireStone = workflow.Items.Single(item => item.ItemId == 82);
        Assert.Equal(7, fireStone.FieldValues["itemType"]);
        Assert.True(fireStone.Metadata.CanUseOnPokemon);
        Assert.Equal(1, fireStone.FieldValues["canUseOnPokemon"]);
        Assert.Equal(1, fireStone.FieldValues["evolutionItem"]);
        Assert.Equal("7 Pokemon Item", fireStone
            .DetailGroups
            .Single(group => group.Label == "Pokemon Legends Z-A")
            .Details
            .Single(detail => detail.Label == "Item type")
            .Value);
        Assert.Equal("Yes", fireStone
            .DetailGroups
            .Single(group => group.Label == "Effects")
            .Details
            .Single(detail => detail.Label == "Evolution Item")
            .Value);
        var megaStone = workflow.Items.Single(item => item.ItemId == 2563);
        Assert.Equal("Mega Stones", megaStone.Category);
        Assert.Equal("7 Pokemon Item", megaStone
            .DetailGroups
            .Single(group => group.Label == "Pokemon Legends Z-A")
            .Details
            .Single(detail => detail.Label == "Item type")
            .Value);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == "itemType").Options,
            option => option.Value == 7 && option.Label == "7 Pokemon Item");
        Assert.Equal("999", pokeBall
            .DetailGroups
            .Single(group => group.Label == "Pokemon Legends Z-A")
            .Details
            .Single(detail => detail.Label == "Stack cap")
            .Value);
        var tm = workflow.Items.Single(item => item.ItemId == 328);
        Assert.Equal("TM001 Tackle", tm.Name);
        Assert.Equal("Technical Machines", tm.Category);
        Assert.Equal(1, tm.Metadata.MachineSlot);
        Assert.Equal(33, tm.Metadata.MachineMoveId);
        Assert.Equal("Tackle", tm.Metadata.MachineMoveName);
        Assert.Equal(33, tm.FieldValues["machineMoveId"]);
        Assert.Equal(-1, tm.FieldValues["machineIndex"]);
        Assert.Contains(workflow.EditableFields, field => field.Field == "canUseOnPokemon" && field.Label == "Can use on Pokemon");
        Assert.Contains(workflow.EditableFields, field => field.Field == "evolutionItem" && field.Label == "Evolution Item");
        Assert.Contains(workflow.EditableFields, field => field.Field == "machineMoveId" && field.Label == "TM move");
    }

    [Fact]
    public void PokemonLegendsZAProjectDerivesHighNumberTechnicalMachineLabels()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateHighNumberTechnicalMachineItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(2163, (2160, "TM100"), (2161, "TM101"), (2162, "TM102")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(528, (526, "Stealth Rock"), (527, "Electroweb"), (528, "Psychic Noise")));
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemArray, CreateShopDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ShopItemLineupArray,
            CreateShopLineupArrayWithItems(2160, 2162));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var items = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-za-high-tm-items");

        AssertSuccess(items);
        var tm101 = items.Payload!.Workflow.Items.Single(item => item.ItemId == 2161);
        Assert.Equal("TM101 Electroweb", tm101.Name);
        Assert.Equal(101, tm101.Metadata.MachineSlot);
        Assert.Equal(527, tm101.Metadata.MachineMoveId);
        Assert.Equal(
            "TM101",
            tm101.DetailGroups
                .Single(group => group.Label == "TM Assignment")
                .Details
                .Single(detail => detail.Label == "TM slot")
                .Value);

        var shops = Dispatch<LoadShopsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadShopsWorkflow,
            new LoadShopsWorkflowRequest(paths),
            "request-za-high-tm-shops");

        AssertSuccess(shops);
        var itemField = shops.Payload!.Workflow.EditableFields.Single(field => field.Field == "itemId");
        Assert.Contains(
            itemField.Options,
            option => option.Value == 2161 && option.ItemName == "TM101 Electroweb");
        Assert.DoesNotContain(itemField.Options, option => option.Value == 428);
        var shop = Assert.Single(shops.Payload.Workflow.Shops);
        Assert.Collection(
            shop.Inventory,
            item => Assert.Equal("TM037 Stealth Rock", item.ItemName),
            item => Assert.Equal("TM102 Psychic Noise", item.ItemName));

        var invalidUpdate = Dispatch<UpdateShopInventoryItemResponse>(
            dispatcher,
            KmCommandNames.UpdateShopInventoryItem,
            new UpdateShopInventoryItemRequest(
                paths,
                Session: null,
                ShopId: "shop:a01_friendlyshop_01",
                Slot: 1,
                Field: "setInventory",
                Value: "2160,428,2162"),
            "request-za-high-tm-shop-invalid-insert");

        AssertSuccess(invalidUpdate);
        Assert.Contains(
            invalidUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("428", StringComparison.Ordinal)
                && diagnostic.Message.Contains("not a known Pokemon Legends Z-A item", StringComparison.Ordinal));

        var update = Dispatch<UpdateShopInventoryItemResponse>(
            dispatcher,
            KmCommandNames.UpdateShopInventoryItem,
            new UpdateShopInventoryItemRequest(
                paths,
                Session: null,
                ShopId: "shop:a01_friendlyshop_01",
                Slot: 1,
                Field: "setInventory",
                Value: "2160,2161,2162"),
            "request-za-high-tm-shop-insert");

        AssertSuccess(update);
        var updatedShop = Assert.Single(update.Payload!.Workflow.Shops);
        Assert.Collection(
            updatedShop.Inventory,
            item => Assert.Equal("TM037 Stealth Rock", item.ItemName),
            item =>
            {
                Assert.Equal(2161, item.ItemId);
                Assert.Equal("TM101 Electroweb", item.ItemName);
                Assert.True(item.IsKnownItem);
            },
            item => Assert.Equal("TM102 Psychic Noise", item.ItemName));

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-high-tm-shop-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.ShopItemLineupArray);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.ItemDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-high-tm-shop-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadShopLineup(temp, "a01_friendlyshop_01_lineup1");
        Assert.Equal(2160u, written.Inventory(0)!.Value.Item);
        Assert.Equal(2161u, written.Inventory(1)!.Value.Item);
        Assert.Equal(2162u, written.Inventory(2)!.Value.Item);
        var writtenTm101 = ReadItem(temp, 2161);
        Assert.Equal(5, writtenTm101.ItemType);
        Assert.Equal(6, writtenTm101.Pocket);
        Assert.Equal(101, writtenTm101.SortNum);
        Assert.Equal(527, writtenTm101.MachineWaza);
        Assert.Equal(100, writtenTm101.MachineIndex);
    }

    [Fact]
    public void PokemonLegendsZAProjectUsesSortOrderForTechnicalMachineNumbers()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateReorderedTechnicalMachineItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(620, (619, "TM094")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(250, (249, "Rock Smash")));
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var items = Dispatch<LoadItemsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(CreatePaths(temp)),
            "request-za-reordered-tm-items");

        AssertSuccess(items);
        var tm = Assert.Single(items.Payload!.Workflow.Items);
        Assert.Equal(619, tm.ItemId);
        Assert.Equal("TM004 Rock Smash", tm.Name);
        Assert.Equal(4, tm.Metadata.MachineSlot);
        Assert.Equal(93, tm.Metadata.GroupIndex);
        Assert.Equal(249, tm.Metadata.MachineMoveId);
        Assert.Equal(4, tm.FieldValues["sortOrder"]);
        Assert.Equal(93, tm.FieldValues["machineIndex"]);
        Assert.Equal(
            "TM004",
            tm.DetailGroups
                .Single(group => group.Label == "TM Assignment")
                .Details
                .Single(detail => detail.Label == "TM slot")
                .Value);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        var dispatcher = CreateDispatcherWithZaCache(temp);

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

        Assert.Equal(6, trainer.Team.Count);
        var pokemon = Assert.Single(trainer.Team, entry => entry.SpeciesId > 0);
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
        Assert.NotNull(pokemon.BaseStats);
        Assert.Equal(45, pokemon.BaseStats!.HP);
        Assert.Equal(49, pokemon.BaseStats.Attack);
        Assert.Contains(workflow.EditableFields, field => field.Field == "rank" && field.Label == "Z-A rank");
        Assert.Contains(workflow.EditableFields, field => field.Field == "megaEvolution" && field.Label == "Mega Evolution");
    }

    [Fact]
    public void PokemonLegendsZATrainerLabelsUseHashClassesAndRealNameFallbacks()
    {
        const ulong hyperspaceTrainerHash = 0xCB6F6D064E9E96A4;

        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerDataArray,
            CreateTrainerLabelFallbackDataArray(hyperspaceTrainerHash));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerNames("English"),
            CreateTextTable(
                9,
                (0, "Young Man"),
                (1, "Lady"),
                (2, "Audrey"),
                (3, "Venin"),
                (4, "Lyse"),
                (5, "Gwynn of"),
                (6, "Fist of Justice Gwynn"),
                (7, "Emma"),
                (8, "Lida"),
                (9, "Jean")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerNameKeys("English"),
            CreateKeyTable(
                (0, "dim_rank_02_05"),
                (0, "dim_rank_04_41"),
                (0, "rest1_01"),
                (0, "sub_010_01"),
                (0, "sub_010_02"),
                (0, "gwynn_of"),
                (0, "prefix_first"),
                (0, "detective"),
                (0, "friend_02"),
                (0, "za_rank_x_19")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerTypes("English"),
            CreateTextTable(0, (0, "Hyperspace Trainer")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerTypeKeys("English"),
            CreateKeyTable((hyperspaceTrainerHash, "MSG_TRTYPE_DIM")));
        var dispatcher = CreateDispatcherWithZaCache(temp);

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(CreatePaths(temp)),
            "request-za-trainer-labels");

        AssertSuccess(trainers);
        Assert.Equal(9, trainers.Payload!.Workflow.Trainers.Count);
        var dimensionTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "dim_rank_02_mizu_05");
        Assert.Equal("Hyperspace Trainer 5", dimensionTrainer.Name);
        Assert.Equal(0, dimensionTrainer.TrainerClassId);
        Assert.Equal("Young Man", dimensionTrainer.TrainerClass);
        var secondDimensionTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "dim_rank_04_41");
        Assert.Equal("Hyperspace Trainer 41", secondDimensionTrainer.Name);
        Assert.Equal("Lady", secondDimensionTrainer.TrainerClass);
        var eventTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "Ev_m03_0125");
        Assert.Equal("Emma", eventTrainer.Name);
        var restaurantTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "Ev_sys_rest1_01");
        Assert.Equal("Audrey", restaurantTrainer.Name);
        var subquestTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "Ev_sub_010_030");
        Assert.Equal("Lyse", subquestTrainer.Name);
        var gwynnTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "gwynn_of");
        Assert.Equal("Gwynn", gwynnTrainer.Name);
        var prefixedTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "prefix_first");
        Assert.Equal("Gwynn", prefixedTrainer.Name);
        var strongestTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "za_inf_strongest_04");
        Assert.Equal("Lida", strongestTrainer.Name);
        var rankInfinityTrainer = trainers.Payload.Workflow.Trainers.Single(trainer => trainer.Location == "za_rank_inf1_01");
        Assert.Equal("Jean", rankInfinityTrainer.Name);
    }

    [Fact]
    public void PokemonLegendsZAProjectLoadsGiftPokemonData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteGiftPokemonFixture(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        Assert.Equal("Handled by trade event", trade.RequiredSpecies);
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
    public void PokemonLegendsZATextEditorSupportsBoundedSearchQueries()
    {
        using var temp = CreatePokemonLegendsZAProject();
        const string scriptPath = "ik_message/dat/English/script/common_0025.dat";
        temp.WriteBaseRomFsFile(
            scriptPath,
            CreateTextTable(2, (0, "Alpha script line"), (1, "Second script line")));
        var paths = CreatePaths(temp) with { GameTextLanguage = "en" };
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var query = new TextWorkflowQueryDto("Second script", 0, 1);

        var load = Dispatch<LoadTextWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(paths, query),
            "request-za-text-query-load");

        AssertSuccess(load);
        var entry = Assert.Single(load.Payload!.Workflow.Entries);
        Assert.Equal($"romfs/{scriptPath}", entry.SourceFile);
        Assert.Equal(1, entry.LineIndex);
        Assert.Equal("Second script line", entry.Value);
        Assert.Equal(1, load.Payload.Workflow.Stats.TotalTextEntryCount);

        var update = Dispatch<UpdateTextEntryResponse>(
            dispatcher,
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(paths, Session: null, entry.TextKey, "Renamed Z-A script line", Query: query),
            "request-za-text-query-update");

        AssertSuccess(update);
        var updatedEntry = Assert.Single(update.Payload!.Workflow.Entries);
        Assert.Equal(entry.TextKey, updatedEntry.TextKey);
        Assert.Equal("Renamed Z-A script line", updatedEntry.Value);
        Assert.Contains(update.Payload.Session.PendingEdits, edit =>
            edit.Domain == "workflow.text" && edit.RecordId == entry.TextKey && edit.NewValue == "Renamed Z-A script line");
    }

    [Fact]
    public void PokemonLegendsZATextEditorStagesAndAppliesMessageEdits()
    {
        using var temp = CreatePokemonLegendsZAProject();
        const string scriptPath = "ik_message/dat/English/script/common_0025.dat";
        temp.WriteBaseRomFsFile(scriptPath, CreateTextTable(1, (0, "[VAR 0100] Original Z-A script line")));
        var paths = CreatePaths(temp) with { GameTextLanguage = "en" };
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var load = Dispatch<LoadTextWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(paths),
            "request-za-text-edit-load");
        AssertSuccess(load);
        var entry = load.Payload!.Workflow.Entries.Single(entry =>
            entry.SourceFile == $"romfs/{scriptPath}" && entry.LineIndex == 0);
        Assert.True(entry.CanEdit);
        Assert.Null(entry.EditBlockedReason);

        var update = Dispatch<UpdateTextEntryResponse>(
            dispatcher,
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(paths, Session: null, entry.TextKey, "[VAR 0100] Updated Z-A script line"),
            "request-za-text-update");
        AssertSuccess(update);
        Assert.Contains(update.Payload!.Session.PendingEdits, edit =>
            edit.Domain == "workflow.text"
            && edit.RecordId == entry.TextKey
            && edit.NewValue == "[VAR 0100] Updated Z-A script line");

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-text-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        Assert.Equal(scriptPath, write.TargetRelativePath);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-text-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal([scriptPath], apply.Payload.ApplyResult.WrittenFiles);

        var outputText = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            scriptPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal("[VAR 0100] Updated Z-A script line", outputText.Lines[0].Text);
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
        temp.WriteBaseRomFsFile(
            "ik_message/dat/English/script/common_0025.dat",
            CreateTextTable(1, (0, "Game dump Z-A script line")));
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemArray, CreateShopDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ShopItemLineupArray, CreateShopLineupArray());
        WriteTrainerFixture(temp);
        WriteGiftPokemonFixture(temp);
        WriteTradePokemonFixture(temp);
        WriteStaticEncounterFixture(temp);
        WritePlacementFixture(temp);
        temp.WriteBaseExeFsFile("main", ZaTypeChartBridgeFixtures.CreateCompatibleMain());
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadGameDumpWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadGameDumpWorkflow,
            new LoadGameDumpWorkflowRequest(paths),
            "request-za-game-dump-load");

        AssertSuccess(load);
        var categories = load.Payload!.Workflow.Categories;
        Assert.Equal(["pokemon", "trainers", "encounters", "staticEncounters", "giftPokemon", "tradePokemon", "moves", "text", "items", "placement", "shops", "typeChart"], categories.Select(category => category.Id).ToArray());
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
                    new GameDumpSelectionDto("encounters", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("placement", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("staticEncounters", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("giftPokemon", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("tradePokemon", GameDumpFormatDto.Json),
                    new GameDumpSelectionDto("text", GameDumpFormatDto.TxtAndJson),
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
            file => file.CategoryId == "encounters" && file.RelativePath == Path.Combine("Wild Encounters", "encounters.json"));
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
            file => file.CategoryId == "text" && file.RelativePath == Path.Combine("Text", "text.txt"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "text" && file.RelativePath == Path.Combine("Text", "text.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "typeChart" && file.RelativePath == Path.Combine("Type Chart", "typeChart.json"));
        Assert.Contains(
            run.Payload.Result.WrittenFiles,
            file => file.CategoryId == "manifest" && file.RelativePath == "manifest.json");
        Assert.Contains("Poke Ball", File.ReadAllText(Path.Combine(destinationFolder, "Items", "items.tsv")));
        Assert.Contains("Rival Aria", File.ReadAllText(Path.Combine(destinationFolder, "Trainers", "trainers.json")));
        Assert.Contains("wild_ignore", File.ReadAllText(Path.Combine(destinationFolder, "Wild Encounters", "encounters.json")));
        Assert.Contains("wild_spawn_001", File.ReadAllText(Path.Combine(destinationFolder, "Placement", "placement.json")));
        Assert.Contains("static_event_ivysaur", File.ReadAllText(Path.Combine(destinationFolder, "Static Encounters", "staticEncounters.json")));
        Assert.Contains("main_init_poke_1", File.ReadAllText(Path.Combine(destinationFolder, "Gift Pokemon", "giftPokemon.json")));
        Assert.Contains("sub_tradepoke_bulbasaur", File.ReadAllText(Path.Combine(destinationFolder, "Trade Pokemon", "tradePokemon.json")));
        Assert.Contains("Game dump Z-A script line", File.ReadAllText(Path.Combine(destinationFolder, "Text", "text.txt")));
        Assert.Contains("Friendly Shop", File.ReadAllText(Path.Combine(destinationFolder, "Shops", "shops.json")));
        Assert.Contains("attackTypeIndex", File.ReadAllText(Path.Combine(destinationFolder, "Type Chart", "typeChart.json")));
        Assert.Contains("Pokemon Legends Z-A", File.ReadAllText(Path.Combine(destinationFolder, "manifest.json")));
    }

    [Fact]
    public void PokemonLegendsZAPokemonEditWritesStandalonePersonalTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var invalidDexUpdate = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(
                paths,
                Session: null,
                PersonalId: 1,
                Field: "regionalDexIndex",
                Value: "401"),
            "request-za-pokemon-invalid-dex-order-update");
        AssertSuccess(invalidDexUpdate);
        Assert.Contains(
            invalidDexUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.False(invalidDexUpdate.Payload.Session.HasPendingChanges);

        var update = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, Session: null, PersonalId: 1, Field: "hp", Value: "99"),
            "request-za-pokemon-update");
        AssertSuccess(update);
        Assert.Equal(99, update.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).BaseStats.HP);

        var dexUpdate = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(
                paths,
                update.Payload.Session,
                PersonalId: 1,
                Field: "regionalDexIndex",
                Value: "300"),
            "request-za-pokemon-dex-order-update");
        AssertSuccess(dexUpdate);
        Assert.Equal(
            300,
            dexUpdate.Payload!.Workflow.Pokemon.Single(row => row.PersonalId == 1).DexPresence.RegionalDexIndex);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, dexUpdate.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.PersonalArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, dexUpdate.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var outputPath = Path.Combine(temp.OutputRootPath, "avalon", "data", "personal_array.bin");
        Assert.True(File.Exists(outputPath));
        var written = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(File.ReadAllBytes(outputPath)));
        var row = written.Entry(1);
        Assert.NotNull(row);
        Assert.Equal(99, row!.Value.BaseStats!.Value.Hp);
        Assert.Equal(300, row.Value.ZADexOrder);
    }

    [Fact]
    public void PokemonLegendsZALegacyByteDexOrderOutputLoadsAndRepairsFromBase()
    {
        using var temp = CreatePokemonLegendsZAProject();
        var basePersonal = CreatePersonalArray(
            bulbasaurDexOrder: 400,
            ivysaurDexOrder: 256,
            bulbasaurSpeciesReserved3: 8,
            ivysaurSpeciesReserved3: 4,
            ivysaurHasType1: false);
        var legacyPersonal = CreatePersonalArray(
            evolutionCondition: 8,
            evolutionParameter: 17,
            evolutionSpecies: 2,
            bulbasaurDexOrder: 400,
            ivysaurDexOrder: 256,
            bulbasaurSpeciesReserved3: 8,
            ivysaurSpeciesReserved3: 4,
            legacyByteDexOrderLayout: true,
            ivysaurHasType1: false,
            ivysaurHasDexOrder: false,
            bulbasaurPresent: false);
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, basePersonal);
        WriteZaOutput(temp, ZaDataPaths.PersonalArray, legacyPersonal);
        Assert.True(
            ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(legacyPersonal))
                .HasLegacyByteZADexOrderLayout);

        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);
        var loaded = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-za-legacy-dex-order-load");
        AssertSuccess(loaded);
        Assert.Contains(
            loaded.Payload!.Workflow.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("rewrite the output safely", StringComparison.Ordinal));
        Assert.Equal(
            400,
            loaded.Payload.Workflow.Pokemon.Single(row => row.PersonalId == 1).DexPresence.RegionalDexIndex);
        Assert.False(
            loaded.Payload.Workflow.Pokemon.Single(row => row.PersonalId == 1).DexPresence.IsPresentInGame);
        Assert.Equal(
            256,
            loaded.Payload.Workflow.Pokemon.Single(row => row.PersonalId == 2).DexPresence.RegionalDexIndex);

        var update = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, Session: null, PersonalId: 1, Field: "hp", Value: "99"),
            "request-za-legacy-dex-order-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-legacy-dex-order-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-legacy-dex-order-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var repairedBytes = ReadZaOutputBytes(temp, ZaDataPaths.PersonalArray);
        var repaired = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(repairedBytes));
        Assert.False(repaired.HasLegacyByteZADexOrderLayout);
        Assert.Equal(400, repaired.Entry(1)!.Value.ZADexOrder);
        Assert.Equal(256, repaired.Entry(2)!.Value.ZADexOrder);
        Assert.Equal(8u, repaired.Entry(1)!.Value.Species!.Value.Reserved3);
        Assert.Equal(4u, repaired.Entry(2)!.Value.Species!.Value.Reserved3);
        Assert.False(repaired.Entry(1)!.Value.IsPresent);
        Assert.Equal(99, repaired.Entry(1)!.Value.BaseStats!.Value.Hp);
        var repairedEvolution = repaired.Entry(1)!.Value.Evolutions(0);
        Assert.NotNull(repairedEvolution);
        Assert.Equal(8, repairedEvolution!.Value.Condition);
        Assert.Equal(17, repairedEvolution.Value.Parameter);
        Assert.Equal(2, repairedEvolution.Value.Species);
        Assert.Equal(1, repaired.Entry(2)!.Value.EvolutionsLength);

        var repairedBulbasaurOffset = FindZaPersonalTableOffset(repairedBytes, personalId: 1);
        var repairedDexLocation = ReadFlatBufferTableFieldLocation(
            repairedBytes,
            repairedBulbasaurOffset,
            fieldIndex: 2);
        var repairedType1Location = ReadFlatBufferTableFieldLocation(
            repairedBytes,
            repairedBulbasaurOffset,
            fieldIndex: 3);
        Assert.Equal(400, BinaryPrimitives.ReadUInt16LittleEndian(repairedBytes.AsSpan(repairedDexLocation, sizeof(ushort))));
        Assert.NotEqual(repairedDexLocation + sizeof(byte), repairedType1Location);
    }

    [Fact]
    public void PokemonLegendsZALegacyOutputFailsClosedWhenBaseIsAlsoMalformed()
    {
        using var temp = CreatePokemonLegendsZAProject();
        var legacyPersonal = CreatePersonalArray(
            bulbasaurDexOrder: 400,
            ivysaurDexOrder: 256,
            legacyByteDexOrderLayout: true,
            ivysaurHasType1: false,
            ivysaurHasDexOrder: false);
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, legacyPersonal);
        WriteZaOutput(temp, ZaDataPaths.PersonalArray, legacyPersonal);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var loaded = Dispatch<LoadPokemonWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-za-malformed-base-load");
        AssertSuccess(loaded);
        Assert.Empty(loaded.Payload!.Workflow.Pokemon);
        Assert.Contains(
            loaded.Payload.Workflow.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("clean base data", StringComparison.Ordinal));

        var session = new EditSessionDto(
            "za-malformed-base-session",
            HasPendingChanges: true,
            [
                new PendingEditDto(
                    "workflow.pokemon",
                    "Set Pokemon personal record 1 HP to 99.",
                    [new FileProvenanceDto(FileLayerDto.Layered, $"romfs/{ZaDataPaths.PersonalArray}")],
                    RecordId: "1",
                    Field: "hp",
                    NewValue: "99"),
            ]);
        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-malformed-base-plan");
        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void PokemonLegendsZAPokemonCompatibilityEditsWriteMoveLists()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray(includeAdditionalMachines: true));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(329, (4, "Poke Ball"), (17, "Potion"), (328, "TM001"), (329, "TM002")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (36, "Take Down"), (45, "Growl")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var tmUpdate = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, Session: null, PersonalId: 1, Field: "compatibility:tm:33", Value: "1"),
            "request-za-pokemon-tm-compatibility-update");
        AssertSuccess(tmUpdate);
        var updatedTmGroup = tmUpdate.Payload!.Workflow.Pokemon
            .Single(row => row.PersonalId == 1)
            .Compatibility
            .Single(group => group.GroupId == "tm");
        Assert.Equal(2, updatedTmGroup.EnabledCount);
        Assert.True(updatedTmGroup.Entries.Single(entry => entry.MoveId == 33).CanLearn);

        var eggUpdate = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, tmUpdate.Payload.Session, PersonalId: 1, Field: "compatibility:egg:0", Value: "0"),
            "request-za-pokemon-egg-compatibility-update");
        AssertSuccess(eggUpdate);
        var reminderUpdate = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, eggUpdate.Payload!.Session, PersonalId: 1, Field: "compatibility:reminder:0", Value: "0"),
            "request-za-pokemon-reminder-compatibility-update");
        AssertSuccess(reminderUpdate);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, reminderUpdate.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-compatibility-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, reminderUpdate.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-pokemon-compatibility-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var outputPath = Path.Combine(temp.OutputRootPath, "avalon", "data", "personal_array.bin");
        var written = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(File.ReadAllBytes(outputPath)));
        var row = written.Entry(1);
        Assert.NotNull(row);
        Assert.Equal([33, 45], row!.Value.GetTmMovesArray().Select(move => (int)move).ToArray());
        Assert.Equal([0], row.Value.GetEggMovesArray().Select(move => (int)move).ToArray());
        Assert.Equal([0], row.Value.GetReminderMovesArray().Select(move => (int)move).ToArray());
    }

    [Fact]
    public void PokemonLegendsZALearnsetEditsPreservePackedMasteryLevel()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        var baseItemData = CreateItemDataArray(includeUseGateRegressionItems: true);
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            baseItemData);
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(
                1231,
                (4, "Poke Ball"),
                (17, "Potion"),
                (82, "Fire Stone"),
                (218, "Soothe Bell"),
                (328, "TM001"),
                (1231, "Lonely Mint")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(45, (33, "Tackle"), (45, "Growl")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var derivedUpdate = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(4, "canUseOnPokemon", "1")]),
            "request-za-item-derived-update");
        AssertSuccess(derivedUpdate);
        Assert.Contains(
            derivedUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("derived from item effects", StringComparison.Ordinal));

        var update = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(4, "evolutionItem", "1"),
                    new ItemFieldUpdateDto(328, "price", "3000"),
                    new ItemFieldUpdateDto(328, "machineMoveId", "45"),
                    new ItemFieldUpdateDto(17, "healPercentage", "50"),
                ]),
            "request-za-item-update");
        AssertSuccess(update);
        var updatedTm = update.Payload!.Workflow.Items.Single(item => item.ItemId == 328);
        Assert.Equal("TM001 Growl", updatedTm.Name);
        Assert.Equal(3000, updatedTm.BuyPrice);
        Assert.Equal(45, updatedTm.Metadata.MachineMoveId);
        var updatedPokeBall = update.Payload.Workflow.Items.Single(item => item.ItemId == 4);
        Assert.True(updatedPokeBall.Metadata.CanUseOnPokemon);
        Assert.Equal(1, updatedPokeBall.FieldValues["canUseOnPokemon"]);
        Assert.Equal(1, updatedPokeBall.FieldValues["evolutionItem"]);

        var evolutionUpdate = Dispatch<UpdatePokemonEvolutionResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonEvolution,
            new UpdatePokemonEvolutionRequest(
                paths,
                update.Payload.Session,
                PersonalId: 1,
                Action: "add",
                Slot: null,
                Method: 8,
                Argument: 4,
                Species: 2,
                Form: 0,
                Level: 0),
            "request-za-item-and-evolution-update");
        AssertSuccess(evolutionUpdate);
        Assert.Contains(
            evolutionUpdate.Payload!.Workflow.Pokemon
                .Single(item => item.PersonalId == 1)
                .Evolutions,
            evolution => evolution.Method == 8 && evolution.Argument == 4 && evolution.Species == 2);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, evolutionUpdate.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-item-plan");
        AssertSuccess(plan);
        Assert.True(
            plan.Payload!.ChangePlan.CanApply,
            string.Join(Environment.NewLine, plan.Payload.ChangePlan.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.ItemDataArray);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.PersonalArray);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.EvolutionItemConversionArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, evolutionUpdate.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-item-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var writtenTm = ReadItem(temp, 328);
        Assert.Equal(3000, writtenTm.Price);
        Assert.False(writtenTm.WorkEvolutional);
        Assert.Equal(-1, writtenTm.MintNature);
        AssertItemDataEqual(
            ReadItem(baseItemData, 328),
            writtenTm,
            nameof(ZaItemData.Price),
            nameof(ZaItemData.MachineWaza));
        var writtenPersonal = ZaPersonalTable.GetRootAsZaPersonalTable(
            new ByteBuffer(ReadZaOutputBytes(temp, ZaDataPaths.PersonalArray)));
        var writtenEntry = writtenPersonal.Entry(1)!.Value;
        Assert.Equal(1, (int)writtenEntry.Species!.Value.Species);
        Assert.Equal(25, (int)writtenEntry.ZADexOrder);
        var writtenConversions = EvolutionItemConversionTable.Read(
            ReadZaOutputBytes(temp, ZaDataPaths.EvolutionItemConversionArray));
        var pokeBallParameter = writtenConversions.Single(row => row.ItemId == 4).ParameterId;
        Assert.Equal(17, pokeBallParameter);
        var writtenEvolution = Enumerable.Range(0, writtenEntry.EvolutionsLength)
            .Select(index => writtenEntry.Evolutions(index)!.Value)
            .Single(evolution => evolution.Condition == 8 && evolution.Parameter == pokeBallParameter && evolution.Species == 2);
        Assert.Equal(8, writtenEvolution.Condition);
        Assert.Equal(pokeBallParameter, writtenEvolution.Parameter);
        Assert.Equal(2, writtenEvolution.Species);
        Assert.Equal(45, writtenTm.MachineWaza);
        var writtenPokeBall = ReadItem(temp, 4);
        Assert.True(writtenPokeBall.WorkEvolutional);
        Assert.Equal(-1, writtenPokeBall.MintNature);
        Assert.Equal(4, writtenPokeBall.ItemType);
        Assert.Equal(1, writtenPokeBall.Pocket);
        AssertItemDataEqual(
            ReadItem(baseItemData, 4),
            writtenPokeBall,
            nameof(ZaItemData.WorkEvolutional));
        var writtenPotion = ReadItem(temp, 17);
        Assert.False(writtenPotion.WorkEvolutional);
        Assert.Equal(-1, writtenPotion.MintNature);
        Assert.Equal(50, writtenPotion.HealPercentage);
        AssertItemDataEqual(
            ReadItem(baseItemData, 17),
            writtenPotion,
            nameof(ZaItemData.HealPercentage));
        var writtenSootheBell = ReadItem(temp, 218);
        Assert.False(writtenSootheBell.WorkEvolutional);
        Assert.Equal(-1, writtenSootheBell.MintNature);
        AssertItemDataEqual(ReadItem(baseItemData, 218), writtenSootheBell);
        var writtenFireStone = ReadItem(temp, 82);
        Assert.True(writtenFireStone.WorkEvolutional);
        Assert.Equal(-1, writtenFireStone.MintNature);
        AssertItemDataEqual(ReadItem(baseItemData, 82), writtenFireStone);
        var writtenLonelyMint = ReadItem(temp, 1231);
        Assert.False(writtenLonelyMint.WorkEvolutional);
        Assert.Equal(1, writtenLonelyMint.MintNature);
        AssertItemDataEqual(ReadItem(baseItemData, 1231), writtenLonelyMint);

        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-za-item-evolution-reload");
        AssertSuccess(reloaded);
        var reloadedEvolution = reloaded.Payload!.Workflow.Pokemon
            .Single(item => item.PersonalId == 1)
            .Evolutions
            .Single(evolution => evolution.Method == 8 && evolution.Species == 2);
        Assert.Equal(4, reloadedEvolution.Argument);
        Assert.Equal("Poke Ball", reloadedEvolution.ArgumentValue);
        Assert.Contains(
            reloaded.Payload.Workflow.EvolutionMethodOptions.Single(option => option.Value == 8).ArgumentOptions,
            option => option.Value == 4 && option.Label == "4 Poke Ball");

        var reloadedItems = Dispatch<LoadItemsWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-za-item-use-gates-reload");
        AssertSuccess(reloadedItems);
        Assert.True(reloadedItems.Payload!.Workflow.Items.Single(item => item.ItemId == 4).Metadata.CanUseOnPokemon);
        Assert.False(reloadedItems.Payload.Workflow.Items.Single(item => item.ItemId == 218).Metadata.CanUseOnPokemon);
        Assert.True(reloadedItems.Payload.Workflow.Items.Single(item => item.ItemId == 82).Metadata.CanUseOnPokemon);
        Assert.True(reloadedItems.Payload.Workflow.Items.Single(item => item.ItemId == 1231).Metadata.CanUseOnPokemon);
    }

    [Fact]
    public void PokemonLegendsZALegacyMintSentinelOutputIsRecoveredBeforeItemApply()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(
                includeUseGateRegressionItems: true,
                extraInertSentinelRows: 40));
        WriteZaOutput(
            temp,
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(
                includeUseGateRegressionItems: true,
                noMintNature: 0,
                extraInertSentinelRows: 40));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(
                3039,
                (4, "Poke Ball"),
                (17, "Potion"),
                (82, "Fire Stone"),
                (218, "Soothe Bell"),
                (328, "TM001"),
                (1231, "Lonely Mint"),
                (3000, "Legacy Sentinel Item")));
        var paths = CreatePaths(temp);

        var loaded = Dispatch<LoadItemsWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-za-legacy-mint-sentinel-load");
        AssertSuccess(loaded);
        Assert.Contains(
            loaded.Payload!.Workflow.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("legacy KM output", StringComparison.Ordinal));
        var loadedSootheBell = loaded.Payload.Workflow.Items.Single(item => item.ItemId == 218);
        Assert.Equal(-1, loadedSootheBell.FieldValues["mintNature"]);
        Assert.False(loadedSootheBell.Metadata.CanUseOnPokemon);
        Assert.True(loaded.Payload.Workflow.Items.Single(item => item.ItemId == 82).Metadata.CanUseOnPokemon);
        Assert.True(loaded.Payload.Workflow.Items.Single(item => item.ItemId == 1231).Metadata.CanUseOnPokemon);

        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(4, "evolutionItem", "1"),
                    new ItemFieldUpdateDto(3000, "mintNature", "0"),
                ]),
            "request-za-legacy-mint-sentinel-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(
                paths,
                update.Payload!.Session,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-legacy-mint-sentinel-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var apply = Dispatch<ApplyChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                update.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-legacy-mint-sentinel-apply");
        AssertSuccess(apply);
        Assert.Contains(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Info
                && diagnostic.Message.Contains("legacy no-mint item sentinels", StringComparison.Ordinal));

        var writtenPokeBall = ReadItem(temp, 4);
        Assert.True(writtenPokeBall.WorkEvolutional);
        Assert.Equal(-1, writtenPokeBall.MintNature);
        var writtenSootheBell = ReadItem(temp, 218);
        Assert.False(writtenSootheBell.WorkEvolutional);
        Assert.Equal(-1, writtenSootheBell.MintNature);
        Assert.True(ReadItem(temp, 82).WorkEvolutional);
        Assert.Equal(-1, ReadItem(temp, 82).MintNature);
        Assert.Equal(1, ReadItem(temp, 1231).MintNature);
        Assert.Equal(0, ReadItem(temp, 3000).MintNature);
        Assert.Equal(1000, ReadItem(temp, 3000).Price);
        Assert.Equal(-1, ReadItem(temp, 3001).MintNature);

        var reloaded = Dispatch<LoadItemsWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-za-legacy-mint-sentinel-reload");
        AssertSuccess(reloaded);
        Assert.DoesNotContain(
            reloaded.Payload!.Workflow.Diagnostics,
            diagnostic => diagnostic.Message.Contains("legacy KM output", StringComparison.Ordinal));
        Assert.True(reloaded.Payload.Workflow.Items.Single(item => item.ItemId == 4).Metadata.CanUseOnPokemon);
        Assert.False(reloaded.Payload.Workflow.Items.Single(item => item.ItemId == 218).Metadata.CanUseOnPokemon);
        Assert.True(reloaded.Payload.Workflow.Items.Single(item => item.ItemId == 3000).Metadata.CanUseOnPokemon);
        Assert.False(reloaded.Payload.Workflow.Items.Single(item => item.ItemId == 3001).Metadata.CanUseOnPokemon);
    }

    [Fact]
    public void PokemonLegendsZAPartialLegacyMintSentinelPatternFailsClosed()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(extraInertSentinelRows: 40));
        WriteZaOutput(
            temp,
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(
                extraInertSentinelRows: 40,
                legacyZeroExtraRows: 36));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var paths = CreatePaths(temp);

        var loaded = Dispatch<LoadItemsWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-za-partial-legacy-mint-sentinel-load");
        AssertSuccess(loaded);
        Assert.Contains(
            loaded.Payload!.Workflow.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("partial legacy mint-sentinel pattern", StringComparison.Ordinal));

        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(4, "price", "200")]),
            "request-za-partial-legacy-mint-sentinel-update");
        AssertSuccess(update);
        Assert.False(update.Payload!.Session.HasPendingChanges);
        Assert.Contains(
            update.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void PokemonLegendsZAEvolutionItemAllocationUsesOnlyApprovedPriorityTiers()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var project = new ProjectWorkspaceService().Open(ProjectBridgeMapper.ToCore(CreatePaths(temp)));
        var state = ZaEvolutionItemConversionState.Load(project, new ZaWorkflowFileSource());
        int[] expectedParameters =
        [
            17, 18, 42, 43, 44, 45, 46, 47, 48, 90, 91, 103, 104, 105,
            94, 95, 96,
            .. Enumerable.Range(53, 17),
            .. Enumerable.Range(72, 7),
            .. Enumerable.Range(97, 5),
            106,
            9, 10, 15, 19,
            .. Enumerable.Range(26, 16),
            49, 51, 70, 71, 79, 80, 81, 82, 87, 88, 89,
        ];

        Assert.Equal(78, expectedParameters.Length);
        Assert.Equal(1, state.Encode(80));
        for (var index = 0; index < expectedParameters.Length; index++)
        {
            Assert.Equal(expectedParameters[index], state.Encode(3000 + index));
        }

        Assert.Throws<InvalidDataException>(() => state.Encode(4000));
        var written = EvolutionItemConversionTable.Read(state.Write());
        Assert.Equal(
            EvolutionItemConversionTable.Read(CreateEvolutionItemConversionArray()).Select(row => row.ParameterId),
            written.Select(row => row.ParameterId));
        for (var index = 0; index < expectedParameters.Length; index++)
        {
            Assert.Contains(
                written,
                row => row.ParameterId == expectedParameters[index] && row.ItemId == 3000 + index);
        }

        Assert.All(
            written.Where(row => row.ParameterId is 11 or 12 or 13 or 14 or 50),
            row => Assert.Equal(0, row.ItemId));
        Assert.Contains(written, row => row.ParameterId == 92 && row.ItemId == 218);
        Assert.Contains(written, row => row.ParameterId == 102 && row.ItemId == 765);
        Assert.Contains(written, row => row.ParameterId == 121 && row.ItemId == 847);
        Assert.Contains(written, row => row.ParameterId == 1691 && row.ItemId == 1691);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(328)]
    [InlineData(1231)]
    public void PokemonLegendsZAEvolutionItemConversionRejectsExistingDirectUseEffects(int itemId)
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(includeUseGateRegressionItems: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var paths = CreatePaths(temp);
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(itemId, "evolutionItem", "1")]),
            "request-za-conflicting-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            "request-za-conflicting-evolution-item-plan");

        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("direct Pokemon-use effect", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("healPower", "25")]
    [InlineData("mintNature", "0")]
    [InlineData("mintNature", "1")]
    public void PokemonLegendsZAEvolutionItemConversionRejectsPendingDirectUseEffects(
        string conflictingField,
        string conflictingValue)
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var paths = CreatePaths(temp);
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(4, "evolutionItem", "1"),
                    new ItemFieldUpdateDto(4, conflictingField, conflictingValue),
                ]),
            $"request-za-pending-{conflictingField}-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            $"request-za-pending-{conflictingField}-evolution-item-plan");

        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("direct Pokemon-use effect", StringComparison.Ordinal));
    }

    [Fact]
    public void PokemonLegendsZAEvolutionItemAllocationProtectsActivePersonalParameters()
    {
        using (var blankTierTemp = CreatePokemonLegendsZAProject())
        {
            blankTierTemp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
            blankTierTemp.WriteBaseRomFsFile(
                ZaDataPaths.PersonalArray,
                CreatePersonalArray(evolutionCondition: 8, evolutionParameter: 17, evolutionSpecies: 2));
            var project = new ProjectWorkspaceService().Open(
                ProjectBridgeMapper.ToCore(CreatePaths(blankTierTemp)));
            var state = ZaEvolutionItemConversionState.Load(project, new ZaWorkflowFileSource());
            Assert.Equal(18, state.Encode(3000));
        }

        using var reclaimTierTemp = CreatePokemonLegendsZAProject();
        reclaimTierTemp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        reclaimTierTemp.WriteBaseRomFsFile(
            ZaDataPaths.PersonalArray,
            CreatePersonalArray(evolutionCondition: 19, evolutionParameter: 9, evolutionSpecies: 2));
        var reclaimProject = new ProjectWorkspaceService().Open(
            ProjectBridgeMapper.ToCore(CreatePaths(reclaimTierTemp)));
        var reclaimState = ZaEvolutionItemConversionState.Load(reclaimProject, new ZaWorkflowFileSource());
        int[] dormantParameters =
        [
            17, 18, 42, 43, 44, 45, 46, 47, 48, 90, 91, 103, 104, 105,
            94, 95, 96,
            .. Enumerable.Range(53, 17),
            .. Enumerable.Range(72, 7),
            .. Enumerable.Range(97, 5),
            106,
        ];
        for (var index = 0; index < dormantParameters.Length; index++)
        {
            Assert.Equal(dormantParameters[index], reclaimState.Encode(3000 + index));
        }

        Assert.Equal(10, reclaimState.Encode(4000));
    }

    [Fact]
    public void PokemonLegendsZAEvolutionItemAllocationFailsClosedWithoutReadablePersonalData()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        var paths = CreatePaths(temp);
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(4, "evolutionItem", "1")]),
            "request-za-missing-personal-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            "request-za-missing-personal-evolution-item-plan");

        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("readable active Pokemon personal data", StringComparison.Ordinal));
    }

    [Fact]
    public void PokemonLegendsZAEvolutionItemBatchAllocationSortsByItemId()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(
                includeCustomEvolutionItem: true,
                customEvolutionItemsEnabled: false));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var paths = CreatePaths(temp);
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(248, "evolutionItem", "1"),
                    new ItemFieldUpdateDto(2, "evolutionItem", "1"),
                ]),
            "request-za-sorted-evolution-item-batch");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-sorted-evolution-item-batch-plan");
        AssertSuccess(plan);
        var apply = Dispatch<ApplyChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                update.Payload.Session,
                plan.Payload!.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-sorted-evolution-item-batch-apply");
        AssertSuccess(apply);

        var written = EvolutionItemConversionTable.Read(
            ReadZaOutputBytes(temp, ZaDataPaths.EvolutionItemConversionArray));
        Assert.Contains(written, row => row.ParameterId == 17 && row.ItemId == 2);
        Assert.Contains(written, row => row.ParameterId == 18 && row.ItemId == 248);
        var writtenUltraBall = ReadItem(temp, 2);
        Assert.True(writtenUltraBall.WorkEvolutional);
        Assert.Equal(-1, writtenUltraBall.MintNature);
        var writtenTwistedSpoon = ReadItem(temp, 248);
        Assert.True(writtenTwistedSpoon.WorkEvolutional);
        Assert.Equal(-1, writtenTwistedSpoon.MintNature);
        var writtenTinyMushroom = ReadItem(temp, 86);
        Assert.False(writtenTinyMushroom.WorkEvolutional);
        Assert.Equal(-1, writtenTinyMushroom.MintNature);
        var writtenMaliciousArmor = ReadItem(temp, 1861);
        Assert.False(writtenMaliciousArmor.WorkEvolutional);
        Assert.Equal(-1, writtenMaliciousArmor.MintNature);
    }

    [Fact]
    public void PokemonLegendsZADisablingEvolutionItemRetainsItsAllocatedMapping()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(5, (4, "Poke Ball")));
        var paths = CreatePaths(temp);

        var enable = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(4, "evolutionItem", "1")]),
            "request-za-enable-evolution-item");
        AssertSuccess(enable);
        var enablePlan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, enable.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-enable-evolution-item-plan");
        AssertSuccess(enablePlan);
        Assert.Equal(
            ZaDataPaths.EvolutionItemConversionArray,
            enablePlan.Payload!.ChangePlan.Writes[0].TargetRelativePath);
        var enableApply = Dispatch<ApplyChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                enable.Payload.Session,
                enablePlan.Payload!.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-enable-evolution-item-apply");
        AssertSuccess(enableApply);
        Assert.Equal(
            ZaDataPaths.EvolutionItemConversionArray,
            enableApply.Payload!.ApplyResult.WrittenFiles[0]);

        var disable = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(4, "evolutionItem", "0")]),
            "request-za-disable-evolution-item");
        AssertSuccess(disable);
        var disablePlan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, disable.Payload!.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-disable-evolution-item-plan");
        AssertSuccess(disablePlan);
        Assert.DoesNotContain(
            disablePlan.Payload!.ChangePlan.Writes,
            write => write.TargetRelativePath.EndsWith(ZaDataPaths.EvolutionItemConversionArray, StringComparison.Ordinal));
        var disableApply = Dispatch<ApplyChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                disable.Payload.Session,
                disablePlan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-disable-evolution-item-apply");
        AssertSuccess(disableApply);

        var disabledItem = ReadItem(temp, 4);
        Assert.False(disabledItem.WorkEvolutional);
        Assert.Equal(-1, disabledItem.MintNature);
        Assert.Contains(
            EvolutionItemConversionTable.Read(ReadZaOutputBytes(temp, ZaDataPaths.EvolutionItemConversionArray)),
            row => row.ParameterId == 17 && row.ItemId == 4);
        var reloaded = Dispatch<LoadPokemonWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadPokemonWorkflow,
            new LoadPokemonWorkflowRequest(paths),
            "request-za-disable-evolution-item-reload");
        AssertSuccess(reloaded);
        Assert.DoesNotContain(
            reloaded.Payload!.Workflow.EvolutionMethodOptions.Single(option => option.Value == 8).ArgumentOptions,
            option => option.Value == 4);
        var reloadedItems = Dispatch<LoadItemsWorkflowResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(paths),
            "request-za-disable-evolution-item-items-reload");
        AssertSuccess(reloadedItems);
        Assert.False(
            reloadedItems.Payload!.Workflow.Items.Single(item => item.ItemId == 4).Metadata.CanUseOnPokemon);
    }

    [Fact]
    public void PokemonLegendsZAEvolutionItemCapacityFailurePlansNoWrites()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemDataArray,
            CreateItemDataArray(includeRestoredOvalStone: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        HashSet<int> approvedParameters =
        [
            17, 18, 42, 43, 44, 45, 46, 47, 48, 90, 91, 103, 104, 105,
            94, 95, 96,
            .. Enumerable.Range(53, 17),
            .. Enumerable.Range(72, 7),
            .. Enumerable.Range(97, 5),
            106, 9, 10, 15, 19,
            .. Enumerable.Range(26, 16),
            49, 51, 70, 71, 79, 80, 81, 82, 87, 88, 89,
        ];
        var occupiedRows = EvolutionItemConversionTable.Read(CreateEvolutionItemConversionArray())
            .Select((row, index) => approvedParameters.Contains(row.ParameterId) && row.ParameterId != 9
                ? row with { ItemId = 5000 + index }
                : row)
            .ToArray();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.EvolutionItemConversionArray,
            EvolutionItemConversionTable.Write(occupiedRows));
        var paths = CreatePaths(temp);
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(4, "evolutionItem", "1")]),
            "request-za-exhausted-evolution-item-update");
        AssertSuccess(update);

        foreach (var requestId in new[] { "first", "second" })
        {
            var plan = Dispatch<CreateChangePlanResponse>(
                CreateDispatcherWithZaCache(temp),
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(paths, update.Payload!.Session),
                $"request-za-exhausted-evolution-item-plan-{requestId}");
            AssertSuccess(plan);
            Assert.False(plan.Payload!.ChangePlan.CanApply);
            Assert.Empty(plan.Payload.ChangePlan.Writes);
            Assert.Contains(
                plan.Payload.ChangePlan.Diagnostics,
                diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                    && diagnostic.Message.Contains("No approved evolution item conversion slot", StringComparison.Ordinal));
        }

        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "world", "exl", "item_data", "item_data", "item_data.bin")));
    }

    [Fact]
    public void PokemonLegendsZAMalformedEvolutionItemTablePlansNoWrites()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateItemDataArray());
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        var conflictingRows = EvolutionItemConversionTable.Read(CreateEvolutionItemConversionArray())
            .Select(row => row.ParameterId == 17 ? row with { ItemId = 5000 } : row)
            .Append(new EvolutionItemConversion(5001, 17))
            .ToArray();
        temp.WriteBaseRomFsFile(
            ZaDataPaths.EvolutionItemConversionArray,
            EvolutionItemConversionTable.Write(conflictingRows));
        var paths = CreatePaths(temp);
        var update = Dispatch<UpdateItemFieldsResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [new ItemFieldUpdateDto(4, "evolutionItem", "1")]),
            "request-za-malformed-evolution-item-update");
        AssertSuccess(update);
        var plan = Dispatch<CreateChangePlanResponse>(
            CreateDispatcherWithZaCache(temp),
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload!.Session),
            "request-za-malformed-evolution-item-plan");

        AssertSuccess(plan);
        Assert.False(plan.Payload!.ChangePlan.CanApply);
        Assert.Empty(plan.Payload.ChangePlan.Writes);
        Assert.Contains(
            plan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("conflicting item assignments", StringComparison.Ordinal));
    }

    [Fact]
    public void PokemonLegendsZAItemEditRenamesTechnicalMachineFromSortOrder()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateReorderedTechnicalMachineItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(620, (619, "TM094")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(251, (249, "Rock Smash"), (250, "Water Pulse")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(619, "machineMoveId", "250"),
                    new ItemFieldUpdateDto(619, "machineIndex", "12"),
                    new ItemFieldUpdateDto(619, "sortOrder", "44"),
                ]),
            "request-za-reordered-tm-update");

        AssertSuccess(update);
        var tm = Assert.Single(update.Payload!.Workflow.Items);
        Assert.Equal("TM044 Water Pulse", tm.Name);
        Assert.Equal(44, tm.Metadata.MachineSlot);
        Assert.Equal(12, tm.Metadata.GroupIndex);
        Assert.Equal(250, tm.Metadata.MachineMoveId);
    }


    [Fact]
    public void PokemonLegendsZAItemEditWritesMissingTechnicalMachine101Row()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemDataArray, CreateHighNumberTechnicalMachineItemDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(2163, (2160, "TM100"), (2161, "TM101"), (2162, "TM102")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.MoveNames("English"),
            CreateTextTable(528, (526, "Dragon Dance"), (527, "Electroweb"), (528, "Psychic Noise")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateItemFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateItemFields,
            new UpdateItemFieldsRequest(
                paths,
                Session: null,
                [
                    new ItemFieldUpdateDto(2161, "price", "2500"),
                ]),
            "request-za-missing-tm101-item-update");

        AssertSuccess(update);
        var tm101 = update.Payload!.Workflow.Items.Single(item => item.ItemId == 2161);
        Assert.Equal("TM101 Electroweb", tm101.Name);
        Assert.Equal(2500, tm101.BuyPrice);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-missing-tm101-item-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.ItemDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-missing-tm101-item-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var writtenTm101 = ReadItem(temp, 2161);
        Assert.Equal(5, writtenTm101.ItemType);
        Assert.Equal(2500, writtenTm101.Price);
        Assert.Equal(6, writtenTm101.Pocket);
        Assert.Equal(1, writtenTm101.SlotMaxNum);
        Assert.Equal(527, writtenTm101.MachineWaza);
        Assert.Equal(100, writtenTm101.MachineIndex);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        Assert.Equal(6, trainer.Team.Count);
        var pokemon = Assert.Single(trainer.Team, entry => entry.SpeciesId > 0);
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
    public void PokemonLegendsZATrainerEditorAllowsClearingAndFillingExposedPartySlots()
    {
        using (var temp = CreatePokemonLegendsZAProject())
        {
            WriteTrainerFixture(temp);
            var dispatcher = CreateDispatcherWithZaCache(temp);
            var paths = CreatePaths(temp);

            var clear = Dispatch<UpdateTrainerFieldsResponse>(
                dispatcher,
                KmCommandNames.UpdateTrainerFields,
                new UpdateTrainerFieldsRequest(
                    paths,
                    Session: null,
                    [new TrainerFieldUpdateDto(0, 0, "speciesId", "0")]),
                "request-za-trainer-clear-slot");
            AssertSuccess(clear);
            var emptySlot = clear.Payload!.Workflow.Trainers.Single().Team[0];
            Assert.Equal(0, emptySlot.SpeciesId);
            Assert.Equal("None", emptySlot.Species);
            Assert.Equal([0, 0, 0, 0], emptySlot.MoveIds);
            Assert.Equal(0, emptySlot.HeldItemId);
            Assert.Equal(0, emptySlot.Ability);
            Assert.Equal(0, emptySlot.Evs.HP + emptySlot.Evs.Attack + emptySlot.Evs.Defense + emptySlot.Evs.SpecialAttack + emptySlot.Evs.SpecialDefense + emptySlot.Evs.Speed);
            Assert.Equal(0, emptySlot.Ivs.HP + emptySlot.Ivs.Attack + emptySlot.Ivs.Defense + emptySlot.Ivs.SpecialAttack + emptySlot.Ivs.SpecialDefense + emptySlot.Ivs.Speed);
            var clearPlan = Dispatch<CreateChangePlanResponse>(
                dispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(paths, clear.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
                "request-za-trainer-clear-plan");
            AssertSuccess(clearPlan);
            Assert.True(clearPlan.Payload!.ChangePlan.CanApply);
            var clearApply = Dispatch<ApplyChangePlanResponse>(
                dispatcher,
                KmCommandNames.ApplyChangePlan,
                new ApplyChangePlanRequest(paths, clear.Payload.Session, clearPlan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
                "request-za-trainer-clear-apply");
            AssertSuccess(clearApply);
            Assert.DoesNotContain(clearApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
            Assert.Null(ReadTrainer(temp, 0).Pokemon1);
        }

        using (var temp = CreatePokemonLegendsZAProject())
        {
            WriteTrainerFixture(temp);
            var dispatcher = CreateDispatcherWithZaCache(temp);
            var paths = CreatePaths(temp);

            var fill = Dispatch<UpdateTrainerFieldsResponse>(
                dispatcher,
                KmCommandNames.UpdateTrainerFields,
                new UpdateTrainerFieldsRequest(
                    paths,
                    Session: null,
                    [new TrainerFieldUpdateDto(0, 1, "speciesId", "2")]),
                "request-za-trainer-fill-slot");
            AssertSuccess(fill);
            var trainer = Assert.Single(fill.Payload!.Workflow.Trainers);
            Assert.Equal(6, trainer.Team.Count);
            Assert.Equal(2, trainer.Team[1].SpeciesId);
            var fillPlan = Dispatch<CreateChangePlanResponse>(
                dispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(paths, fill.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
                "request-za-trainer-fill-plan");
            AssertSuccess(fillPlan);
            Assert.True(fillPlan.Payload!.ChangePlan.CanApply);
            var fillApply = Dispatch<ApplyChangePlanResponse>(
                dispatcher,
                KmCommandNames.ApplyChangePlan,
                new ApplyChangePlanRequest(paths, fill.Payload.Session, fillPlan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
                "request-za-trainer-fill-apply");
            AssertSuccess(fillApply);
            Assert.DoesNotContain(fillApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
            var writtenPokemon = ReadTrainer(temp, 0).Pokemon2;
            Assert.NotNull(writtenPokemon);
            Assert.Equal(2, writtenPokemon!.Value.SpeciesId);
        }
    }

    [Fact]
    public void PokemonLegendsZATrainerEditorRejectsPartySlotGaps()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTrainerFixture(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var response = Dispatch<UpdateTrainerFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerFields,
            new UpdateTrainerFieldsRequest(
                paths,
                Session: null,
                [new TrainerFieldUpdateDto(0, 2, "speciesId", "2")]),
            "request-za-trainer-gap");

        AssertSuccess(response);
        Assert.False(response.Payload!.Session.HasPendingChanges);
        Assert.Contains(
            response.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("filled in order", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PokemonLegendsZATrainerEditorRejectsEditingEmptyPartySlotDetails()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTrainerFixture(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var response = Dispatch<UpdateTrainerFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTrainerFields,
            new UpdateTrainerFieldsRequest(
                paths,
                Session: null,
                [new TrainerFieldUpdateDto(0, 1, "move1Id", "33")]),
            "request-za-trainer-empty-slot-detail");

        AssertSuccess(response);
        Assert.False(response.Payload!.Session.HasPendingChanges);
        Assert.Contains(
            response.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("slot is empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PokemonLegendsZAGiftPokemonEditWritesTrinityPokemonDataTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteGiftPokemonFixture(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        Assert.Equal(128, written.TalentScale);
        Assert.Equal(0, written.TalentVNum);
        Assert.Equal(31, written.TalentValue!.Value.Hp);
        Assert.Equal(30, written.TalentValue!.Value.Atk);

        var gift = ReadGiftPokemonData(temp, "main_init_poke_1");
        Assert.Equal(0, gift.MinLevel);
        Assert.Equal(0, gift.MaxLevel);
    }

    [Fact]
    public void PokemonLegendsZATradePokemonRandomIvPresetWritesCurrentSentinel()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteTradePokemonFixture(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var update = Dispatch<UpdateTradePokemonFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateTradePokemonFields,
            new UpdateTradePokemonFieldsRequest(
                paths,
                Session: null,
                [new TradePokemonFieldUpdateDto(0, "flawlessIvCount", "0")]),
            "request-za-trade-pokemon-random-ivs");
        AssertSuccess(update);
        var trade = Assert.Single(update.Payload!.Workflow.Trades);
        Assert.Equal(0, trade.FlawlessIvCount);
        Assert.Equal("Random IVs", trade.IvSummary);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, update.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-trade-pokemon-random-ivs-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, update.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-trade-pokemon-random-ivs-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadGiftPokemonData(temp, "sub_tradepoke_bulbasaur");
        Assert.Equal(127, written.TalentScale);
        Assert.Equal(0, written.TalentVNum);
        Assert.Equal(-1, written.TalentValue!.Value.Hp);
        Assert.Equal(-1, written.TalentValue!.Value.Atk);
        Assert.Equal(-1, written.TalentValue!.Value.Def);
        Assert.Equal(-1, written.TalentValue!.Value.SpAtk);
        Assert.Equal(-1, written.TalentValue!.Value.SpDef);
        Assert.Equal(-1, written.TalentValue!.Value.Agi);
    }

    [Fact]
    public void PokemonLegendsZALoadsSignedDefaultPokemonDataFields()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseRomFsFile(ZaDataPaths.TrainerDataArray, CreateTrainerDataArray(signedDefaults: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonDataArray, CreatePokemonDataArray(signedDefaults: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.EncountDataArray, CreateEncounterDataArray(signedDefaults: true));
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
            ZaDataPaths.TrainerNameKeys("English"),
            CreateKeyTable((0, "tr_battle_main_001")));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.TrainerTypes("English"),
            CreateTextTable(1, (1, "Duelist")));
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(paths),
            "request-za-trainers-signed-defaults");
        AssertSuccess(trainers);
        var signedDefaultsTrainer = Assert.Single(trainers.Payload!.Workflow.Trainers);
        Assert.Equal(6, signedDefaultsTrainer.Team.Count);
        var trainerPokemon = Assert.Single(signedDefaultsTrainer.Team, entry => entry.SpeciesId > 0);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        Assert.Equal("Alpha Chance", slot.EncounterKind);
        Assert.False(slot.IsAlpha);
        Assert.Equal(5, slot.AlphaChancePercent);
        Assert.Equal(10, slot.AlphaLevelBonus);
        Assert.Equal(30, slot.LevelMax + slot.AlphaLevelBonus);
        Assert.Contains(workflow.EditableFields, field => field.Field == "speciesId" && field.Label == "Species");
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == "probability");
        var alphaChanceField = Assert.Single(workflow.EditableFields, field => field.Field == "alphaChancePercent");
        Assert.Equal("Alpha Chance (%)", alphaChanceField.Label);
        Assert.Equal("integer", alphaChanceField.ValueKind);
        Assert.Equal(0, alphaChanceField.MinimumValue);
        Assert.Equal(100, alphaChanceField.MaximumValue);
        var alphaLevelBonusField = Assert.Single(workflow.EditableFields, field => field.Field == "alphaLevelBonus");
        Assert.Equal("Alpha Level Bonus", alphaLevelBonusField.Label);
        Assert.Equal("integer", alphaLevelBonusField.ValueKind);
        Assert.Equal(0, alphaLevelBonusField.MinimumValue);
        Assert.Equal(100, alphaLevelBonusField.MaximumValue);

        var invalidOrdinaryGuarantee = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                table.TableId,
                slot.Slot,
                "alphaChancePercent",
                "100"),
            "request-za-encounters-ordinary-guarantee");
        AssertSuccess(invalidOrdinaryGuarantee);
        Assert.Empty(invalidOrdinaryGuarantee.Payload!.Session.PendingEdits);
        Assert.Contains(
            invalidOrdinaryGuarantee.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("shared Alpha chance", StringComparison.OrdinalIgnoreCase));

        var invalidAlphaRange = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                Session: null,
                [
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "levelMax", "95"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "alphaLevelBonus", "6"),
                ]),
            "request-za-encounters-invalid-alpha-range");
        AssertSuccess(invalidAlphaRange);
        Assert.Empty(invalidAlphaRange.Payload!.Session.PendingEdits);
        Assert.Contains(
            invalidAlphaRange.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("every linked placement", StringComparison.Ordinal));

        var invalidBaseRange = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                Session: null,
                [
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "levelMin", "100"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "levelMax", "1"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "alphaLevelBonus", "1"),
                ]),
            "request-za-encounters-invalid-base-range");
        AssertSuccess(invalidBaseRange);
        Assert.Empty(invalidBaseRange.Payload!.Session.PendingEdits);
        Assert.Contains(
            invalidBaseRange.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("minimum 100 is greater than maximum 1", StringComparison.Ordinal));

        var orderIndependentUpdate = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                Session: null,
                [
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "alphaLevelBonus", "81"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "alphaChancePercent", "0"),
                ]),
            "request-za-encounters-order-independent-alpha-range");
        AssertSuccess(orderIndependentUpdate);
        Assert.DoesNotContain(
            orderIndependentUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var slotUpdate = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                orderIndependentUpdate.Payload.Session,
                [
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "speciesId", "2"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "levelMin", "25"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "levelMax", "30"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "alphaChancePercent", "25"),
                    new EncounterSlotFieldUpdateDto(table.TableId, slot.Slot, "alphaLevelBonus", "12"),
                ]),
            "request-za-encounters-slot-fields");
        AssertSuccess(slotUpdate);
        var updatedSlot = Assert.Single(slotUpdate.Payload!.Workflow.Tables.Single().Slots);
        Assert.Equal(2, updatedSlot.SpeciesId);
        Assert.Equal("Ivysaur", updatedSlot.Species);
        Assert.Equal(25, updatedSlot.LevelMin);
        Assert.Equal(30, updatedSlot.LevelMax);
        Assert.Equal(25, updatedSlot.AlphaChancePercent);
        Assert.Equal(12, updatedSlot.AlphaLevelBonus);
        Assert.Equal(42, updatedSlot.LevelMax + updatedSlot.AlphaLevelBonus);
        Assert.Contains(
            slotUpdate.Payload.Session.PendingEdits,
            edit => edit.Field == "alphaChancePercent"
                && edit.Summary.Contains("every placement linked", StringComparison.Ordinal));

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
        Assert.Equal(25F, written.OyabunProbability);
        Assert.Equal(12, written.OyabunAdditionalLevel);
        Assert.Equal(33, written.WazaList!.Value.Waza1);
        Assert.Equal(17, written.HoldItem!.Value.HoldItem);
        Assert.Equal(101, written.StrengthenValue!.Value.Hp);
        var drop = written.ItemDropInfoList(0);
        Assert.NotNull(drop);
        Assert.Equal("drop_table_001", drop.Value.ItemTableId);
        Assert.Equal(75U, drop.Value.DropProbability);
        Assert.Equal(2, drop.Value.DropConditionListLength);
        Assert.Equal(7, drop.Value.DropConditionList(0));
        Assert.Equal(8, drop.Value.DropConditionList(1));
        Assert.DoesNotContain(ZaDataPaths.PokemonDataArray, apply.Payload.ApplyResult.WrittenFiles);
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersSynchronizeSlotsLinkedToTheSameDataRow()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(
            temp,
            includeAlphaAndRawSpawners: true,
            includeDistinctEncounterSpawner: true);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounters-linked-load");

        AssertSuccess(load);
        var tables = load.Payload!.Workflow.Tables;
        Assert.Equal(25, tables.Count);
        var ordinaryLinkedTables = tables
            .Where(table => table.Slots.Count == 1 && table.Slots[0].EncounterDataId == "wild_ignore")
            .ToArray();
        Assert.Equal(19, ordinaryLinkedTables.Length);
        var firstTable = ordinaryLinkedTables[0];
        var firstSlot = Assert.Single(firstTable.Slots);
        var secondTable = ordinaryLinkedTables[1];
        var secondSlot = Assert.Single(secondTable.Slots);
        var encounterRecordId = Assert.IsType<string>(firstSlot.EncounterRecordId);
        Assert.Equal(encounterRecordId, secondSlot.EncounterRecordId);
        var linkedSlots = tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.EncounterRecordId == encounterRecordId)
            .ToArray();
        Assert.Equal(19, linkedSlots.Length);
        var distinctSlot = Assert.Single(
            tables.SelectMany(table => table.Slots),
            slot => slot.EncounterDataId == "static_event_ivysaur");
        Assert.NotNull(distinctSlot.EncounterRecordId);
        Assert.NotEqual(encounterRecordId, distinctSlot.EncounterRecordId);

        var firstUpdate = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                Session: null,
                [
                    new EncounterSlotFieldUpdateDto(firstTable.TableId, firstSlot.Slot, "levelMin", "25"),
                    new EncounterSlotFieldUpdateDto(firstTable.TableId, firstSlot.Slot, "levelMax", "25"),
                ]),
            "request-za-encounters-linked-first");
        AssertSuccess(firstUpdate);
        Assert.All(
            firstUpdate.Payload!.Workflow.Tables
                .SelectMany(table => table.Slots)
                .Where(slot => slot.EncounterRecordId == encounterRecordId),
            slot =>
            {
                Assert.Equal(25, slot.LevelMin);
                Assert.Equal(25, slot.LevelMax);
            });
        Assert.Equal(
            35,
            Assert.Single(
                firstUpdate.Payload.Workflow.Tables.SelectMany(table => table.Slots),
                slot => slot.EncounterRecordId == distinctSlot.EncounterRecordId).LevelMin);

        var secondUpdate = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                firstUpdate.Payload.Session,
                [
                    new EncounterSlotFieldUpdateDto(secondTable.TableId, secondSlot.Slot, "levelMin", "30"),
                    new EncounterSlotFieldUpdateDto(secondTable.TableId, secondSlot.Slot, "levelMax", "30"),
                ]),
            "request-za-encounters-linked-second");
        AssertSuccess(secondUpdate);
        Assert.Equal(2, secondUpdate.Payload!.Session.PendingEdits.Count);
        var pendingEdit = Assert.Single(
            secondUpdate.Payload.Session.PendingEdits,
            edit => edit.Field == "levelMin");
        Assert.Equal(encounterRecordId, pendingEdit.RecordId);
        Assert.Equal("30", pendingEdit.NewValue);
        Assert.All(
            secondUpdate.Payload.Workflow.Tables
                .SelectMany(table => table.Slots)
                .Where(slot => slot.EncounterRecordId == encounterRecordId),
            slot =>
            {
                Assert.Equal(30, slot.LevelMin);
                Assert.Equal(30, slot.LevelMax);
            });
        Assert.Equal(
            35,
            Assert.Single(
                secondUpdate.Payload.Workflow.Tables.SelectMany(table => table.Slots),
                slot => slot.EncounterRecordId == distinctSlot.EncounterRecordId).LevelMin);

        var sharedAlphaUpdate = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                secondUpdate.Payload.Session,
                [
                    new EncounterSlotFieldUpdateDto(secondTable.TableId, secondSlot.Slot, "alphaChancePercent", "30"),
                    new EncounterSlotFieldUpdateDto(secondTable.TableId, secondSlot.Slot, "alphaLevelBonus", "12"),
                ]),
            "request-za-encounters-linked-alpha-settings");
        AssertSuccess(sharedAlphaUpdate);
        Assert.Equal(4, sharedAlphaUpdate.Payload!.Session.PendingEdits.Count);
        Assert.All(
            sharedAlphaUpdate.Payload.Workflow.Tables
                .SelectMany(table => table.Slots)
                .Where(slot => slot.EncounterRecordId == encounterRecordId),
            slot =>
            {
                Assert.Equal(30, slot.AlphaChancePercent);
                Assert.Equal(12, slot.AlphaLevelBonus);
                Assert.Equal("Alpha Chance", slot.EncounterKind);
            });
        var unchangedDistinctSlot = Assert.Single(
            sharedAlphaUpdate.Payload.Workflow.Tables.SelectMany(table => table.Slots),
            slot => slot.EncounterRecordId == distinctSlot.EncounterRecordId);
        Assert.Null(unchangedDistinctSlot.AlphaChancePercent);
        Assert.Equal(10, unchangedDistinctSlot.AlphaLevelBonus);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, sharedAlphaUpdate.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-linked-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                sharedAlphaUpdate.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-linked-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Equal(30, ReadEncountData(temp, "wild_ignore").MinLevel);
        Assert.Equal(30, ReadEncountData(temp, "wild_ignore").MaxLevel);
        Assert.Equal(30F, ReadEncountData(temp, "wild_ignore").OyabunProbability);
        Assert.Equal(12, ReadEncountData(temp, "wild_ignore").OyabunAdditionalLevel);
        Assert.Equal(35, ReadEncountData(temp, "static_event_ivysaur").MinLevel);
        Assert.Equal(0.25F, ReadEncountData(temp, "static_event_ivysaur").OyabunProbability);
        Assert.Equal(10, ReadEncountData(temp, "static_event_ivysaur").OyabunAdditionalLevel);
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersDescribeAlphaAndRawSpawnerLocations()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp, includeAlphaAndRawSpawners: true);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounters-alpha-raw-load");

        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        Assert.Equal(24, workflow.Tables.Count);

        var alphaTables = workflow.Tables.Where(table => table.Slots.Any(slot => slot.IsAlpha)).ToArray();
        Assert.Equal(3, alphaTables.Length);
        var alphaTable = alphaTables.Single(table => table.TableLabel == "Spawner 2");
        Assert.Equal("Wild Zone 1", alphaTable.Location);
        Assert.Equal("Spawner 2", alphaTable.TableLabel);
        Assert.Contains("Alpha", alphaTable.TableDetails);
        var alphaSlot = Assert.Single(alphaTable.Slots);
        Assert.True(alphaSlot.IsAlpha);
        Assert.Equal("Guaranteed Alpha", alphaSlot.EncounterKind);
        Assert.Equal("wild_guaranteed_alpha_Alpha", alphaSlot.EncounterDataId);
        Assert.Equal(1, alphaSlot.SpeciesId);
        Assert.Equal("Bulbasaur", alphaSlot.Species);
        Assert.Equal(100, alphaSlot.AlphaChancePercent);
        Assert.Equal(9, alphaSlot.AlphaLevelBonus);
        Assert.Equal(29, alphaSlot.LevelMax + alphaSlot.AlphaLevelBonus);
        var alphaRecordId = Assert.IsType<string>(alphaSlot.EncounterRecordId);
        Assert.All(
            alphaTables.SelectMany(table => table.Slots),
            slot =>
            {
                Assert.True(slot.IsAlpha);
                Assert.Equal("Guaranteed Alpha", slot.EncounterKind);
                Assert.Equal(alphaRecordId, slot.EncounterRecordId);
                Assert.Equal(100, slot.AlphaChancePercent);
            });
        var plainGuaranteedTable = Assert.Single(
            workflow.Tables,
            table => Assert.Single(table.Slots).EncounterDataId == "wild_guaranteed_plain");
        var plainGuaranteedSlot = Assert.Single(plainGuaranteedTable.Slots);
        Assert.False(plainGuaranteedSlot.IsAlpha);
        Assert.Equal("Guaranteed Alpha", plainGuaranteedSlot.EncounterKind);
        Assert.Equal(100, plainGuaranteedSlot.AlphaChancePercent);
        Assert.NotEqual(alphaRecordId, plainGuaranteedSlot.EncounterRecordId);

        var invalidGuaranteedChance = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                alphaTable.TableId,
                alphaSlot.Slot,
                "alphaChancePercent",
                "99"),
            "request-za-encounters-guaranteed-alpha-chance");
        AssertSuccess(invalidGuaranteedChance);
        Assert.Empty(invalidGuaranteedChance.Payload!.Session.PendingEdits);
        Assert.Contains(
            invalidGuaranteedChance.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("must keep their shared Alpha chance at 100", StringComparison.Ordinal));

        var invalidPlainGuaranteedChance = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                plainGuaranteedTable.TableId,
                plainGuaranteedSlot.Slot,
                "alphaChancePercent",
                "99"),
            "request-za-encounters-plain-guaranteed-alpha-chance");
        AssertSuccess(invalidPlainGuaranteedChance);
        Assert.Empty(invalidPlainGuaranteedChance.Payload!.Session.PendingEdits);
        Assert.Contains(
            invalidPlainGuaranteedChance.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("must keep their shared Alpha chance at 100", StringComparison.Ordinal));

        var unchangedPlainGuaranteedChance = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                plainGuaranteedTable.TableId,
                plainGuaranteedSlot.Slot,
                "alphaChancePercent",
                "100"),
            "request-za-encounters-plain-guaranteed-alpha-unchanged");
        AssertSuccess(unchangedPlainGuaranteedChance);
        Assert.DoesNotContain(
            unchangedPlainGuaranteedChance.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var unchangedPlainPendingEdit = Assert.Single(unchangedPlainGuaranteedChance.Payload.Session.PendingEdits);
        Assert.Equal("100", unchangedPlainPendingEdit.NewValue);
        var unchangedPlainSlot = Assert.Single(
            unchangedPlainGuaranteedChance.Payload.Workflow.Tables.SelectMany(table => table.Slots),
            slot => slot.EncounterRecordId == plainGuaranteedSlot.EncounterRecordId);
        Assert.False(unchangedPlainSlot.IsAlpha);
        Assert.Equal("Guaranteed Alpha", unchangedPlainSlot.EncounterKind);

        var guaranteedBonusUpdate = Dispatch<UpdateEncounterSlotFieldsResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotFields,
            new UpdateEncounterSlotFieldsRequest(
                paths,
                Session: null,
                [
                    new EncounterSlotFieldUpdateDto(alphaTable.TableId, alphaSlot.Slot, "alphaChancePercent", "100"),
                    new EncounterSlotFieldUpdateDto(alphaTable.TableId, alphaSlot.Slot, "alphaLevelBonus", "12"),
                ]),
            "request-za-encounters-guaranteed-alpha-bonus");
        AssertSuccess(guaranteedBonusUpdate);
        Assert.DoesNotContain(
            guaranteedBonusUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.All(
            guaranteedBonusUpdate.Payload.Workflow.Tables
                .SelectMany(table => table.Slots)
                .Where(slot => slot.EncounterRecordId == alphaRecordId),
            slot =>
            {
                Assert.True(slot.IsAlpha);
                Assert.Equal(100, slot.AlphaChancePercent);
                Assert.Equal(12, slot.AlphaLevelBonus);
                Assert.Equal(32, slot.LevelMax + slot.AlphaLevelBonus);
            });

        var rawTables = workflow.Tables.Where(table => table.LocationKey == "zdm406_v00").ToArray();
        Assert.Equal(2, rawTables.Length);
        Assert.All(rawTables, table => Assert.Equal("Dimension Dungeon 406 Variant 0", table.Location));
        Assert.Contains(rawTables, table => table.TableLabel == "Dimension Dungeon 406 Variant 0 Spawn Point 700");
        Assert.Contains(rawTables, table => table.TableLabel == "Dimension Dungeon 406 Variant 0 Spawn Point 701");
        Assert.All(rawTables, table => Assert.Equal("Alpha Chance", Assert.Single(table.Slots).EncounterKind));

        var outzoneTable = Assert.Single(
            workflow.Tables,
            table => table.TableLabel == "Bleu District, Sector 1 Outside Wild Zone, Spawn Point 050, Battle Zone");
        Assert.Equal("Bleu District, Sector 1 Outside Wild Zone", outzoneTable.Location);
        Assert.Equal("Bleu District, Sector 1 Outside Wild Zone, Spawn Point 050, Battle Zone", outzoneTable.TableLabel);
        Assert.Equal("spawnPoint", outzoneTable.SpawnerCategory);

        var outzoneGroupTable = Assert.Single(
            workflow.Tables,
            table => table.TableLabel == "Bleu District, Sector 1 Outside Wild Zone, Spawn Group O, Point 50, Battle Zone");
        Assert.Equal("Bleu District, Sector 1 Outside Wild Zone", outzoneGroupTable.Location);
        Assert.Equal("Bleu District, Sector 1 Outside Wild Zone, Spawn Group O, Point 50, Battle Zone", outzoneGroupTable.TableLabel);
        Assert.Equal("spawnGroup", outzoneGroupTable.SpawnerCategory);

        var outzonePointZeroTable = Assert.Single(
            workflow.Tables,
            table => table.TableLabel == "Bleu District, Sector 1 Outside Wild Zone, Spawn Group P, Point 00");
        Assert.Equal("Bleu District, Sector 1 Outside Wild Zone, Spawn Group P, Point 00", outzonePointZeroTable.TableLabel);
        Assert.Equal("spawnGroup", outzonePointZeroTable.SpawnerCategory);

        var outzoneSpecialTable = Assert.Single(
            workflow.Tables,
            table => table.TableLabel == "Bleu District, Sector 1 Outside Wild Zone, Special Encounter 1");
        Assert.Equal("Bleu District, Sector 1 Outside Wild Zone, Special Encounter 1", outzoneSpecialTable.TableLabel);
        Assert.Equal("specialEncounter", outzoneSpecialTable.SpawnerCategory);

        var outzoneGroupATable = Assert.Single(
            workflow.Tables,
            table => table.TableLabel == "Bleu District, Sector 1 Outside Wild Zone, Spawn Group A, Point 00");
        Assert.Equal("spawnGroup", outzoneGroupATable.SpawnerCategory);
        var outzoneGroupASlot = Assert.Single(outzoneGroupATable.Slots);
        Assert.False(outzoneGroupASlot.IsAlpha);
        Assert.Equal("Wild", outzoneGroupASlot.EncounterKind);
        Assert.Equal(0, outzoneGroupASlot.AlphaChancePercent);

        var outzoneAlphaTable = Assert.Single(
            workflow.Tables,
            table => table.TableLabel
                == "Bleu District, Sector 1 Outside Wild Zone, Spawn Point 405, Alpha, Battle Zone, Phase Condition");
        Assert.Equal("alpha", outzoneAlphaTable.SpawnerCategory);
        Assert.True(Assert.Single(outzoneAlphaTable.Slots).IsAlpha);

        var bossTable = Assert.Single(workflow.Tables, table => table.LocationKey == "boss_0015_re");
        Assert.Equal("Boss Battle Pokemon 15 Rematch", bossTable.Location);
        Assert.Equal("Boss Battle Pokemon 15 Rematch", bossTable.TableLabel);

        var dungeonTable = Assert.Single(workflow.Tables, table => table.LocationKey == "d02_01");
        Assert.Equal("Lumiose Sewers Main Area", dungeonTable.Location);
        Assert.Equal("Lumiose Sewers Main Area Spawn Point 001", dungeonTable.TableLabel);

        var phaseTwoTable = Assert.Single(workflow.Tables, table => table.LocationKey == "t2");
        Assert.Equal("Lysandre Labs", phaseTwoTable.Location);
        Assert.Equal("Lysandre Labs Spawn Point 001", phaseTwoTable.TableLabel);

        var sewerMainTable = Assert.Single(workflow.Tables, table => table.LocationKey == "t3");
        Assert.Equal("Lumiose Sewers Main Area", sewerMainTable.Location);
        Assert.Equal("Lumiose Sewers Main Area Spawn Point 001", sewerMainTable.TableLabel);

        var sewerSideTable = Assert.Single(workflow.Tables, table => table.LocationKey == "t3_2");
        Assert.Equal("Lumiose Sewers Side Area", sewerSideTable.Location);
        Assert.Equal("Lumiose Sewers Side Area Spawn Point 001", sewerSideTable.TableLabel);

        var chapterTable = Assert.Single(workflow.Tables, table => table.LocationKey == "id_chapter9");
        Assert.Equal("Story Chapter Event 9", chapterTable.Location);
        Assert.Equal("Story Chapter Event 9 Spawn Point 00", chapterTable.TableLabel);

        var chapterDungeonTable = Assert.Single(workflow.Tables, table => table.LocationKey == "id_chapter5");
        Assert.Equal("Story Chapter Event 5", chapterDungeonTable.Location);
        Assert.Equal("Story Chapter Event 5 Dungeon 2 Spawn Point 001", chapterDungeonTable.TableLabel);

        var sideMissionTable = Assert.Single(workflow.Tables, table => table.LocationKey == "id_sub090");
        Assert.Equal("Floette Frolicking with Flowers", sideMissionTable.Location);
        Assert.Equal("Floette Frolicking with Flowers Spawn Point 01", sideMissionTable.TableLabel);
        Assert.Equal("Side Mission 51", sideMissionTable.LocationDetails);
        Assert.Equal(51, sideMissionTable.LocationSort);

        var rest4Table = Assert.Single(workflow.Tables, table => table.LocationKey == "id_rest4");
        Assert.Equal("Full Course of Battles: High Rolling", rest4Table.Location);
        Assert.Equal(
            "Full Course of Battles: High Rolling Battle 1 Spawn Point 001",
            rest4Table.TableLabel);
        Assert.Equal("Side Mission 73", rest4Table.LocationDetails);
        Assert.Equal(73, rest4Table.LocationSort);

        var defenselessDodgerTable = Assert.Single(
            workflow.Tables,
            table => table.LocationKey == "id_spn_subq147");
        Assert.Equal("Be a Defenseless Dodger!", defenselessDodgerTable.Location);
        Assert.Equal(
            "Be a Defenseless Dodger! Spawn Point 002B",
            defenselessDodgerTable.TableLabel);
        Assert.Equal("Side Mission 173", defenselessDodgerTable.LocationDetails);
        Assert.Equal(173, defenselessDodgerTable.LocationSort);

        var dimensionWildTables = workflow.Tables.Where(table => table.LocationKey == "zdm_random_dimension_wilds").ToArray();
        Assert.Equal(2, dimensionWildTables.Length);
        Assert.All(dimensionWildTables, table => Assert.Equal("Dimension Wild Pools", table.Location));
        Assert.Contains(dimensionWildTables, table => table.TableLabel == "Flying Type Pool 2, Rank 3, Pokemon 701");
        Assert.Contains(dimensionWildTables, table => table.TableLabel == "Flying Type Pool 2, Rank 3, Pokemon 662 Set");
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersRejectAlphaChanceForMixedLinkedPlacements()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp, includeMixedAlphaReference: true);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounters-mixed-alpha-load");
        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        var ordinaryTable = Assert.Single(
            workflow.Tables,
            table => Assert.Single(table.Slots).EncounterDataId == "wild_ignore");
        var ordinarySlot = Assert.Single(ordinaryTable.Slots);
        var mixedAlphaSlot = Assert.Single(
            workflow.Tables.SelectMany(table => table.Slots),
            slot => slot.EncounterDataId == "wild_ignore_Alpha");
        Assert.Equal(ordinarySlot.EncounterRecordId, mixedAlphaSlot.EncounterRecordId);
        Assert.False(ordinarySlot.IsAlpha);
        Assert.True(mixedAlphaSlot.IsAlpha);

        var chanceUpdate = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                ordinaryTable.TableId,
                ordinarySlot.Slot,
                "alphaChancePercent",
                "25"),
            "request-za-encounters-mixed-alpha-chance");
        AssertSuccess(chanceUpdate);
        Assert.Empty(chanceUpdate.Payload!.Session.PendingEdits);
        Assert.Contains(
            chanceUpdate.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("both structural _Alpha and ordinary references", StringComparison.Ordinal));

        var bonusUpdate = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                ordinaryTable.TableId,
                ordinarySlot.Slot,
                "alphaLevelBonus",
                "12"),
            "request-za-encounters-mixed-alpha-bonus");
        AssertSuccess(bonusUpdate);
        Assert.DoesNotContain(
            bonusUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.All(
            bonusUpdate.Payload.Workflow.Tables
                .SelectMany(table => table.Slots)
                .Where(slot => slot.EncounterRecordId == ordinarySlot.EncounterRecordId),
            slot => Assert.Equal(12, slot.AlphaLevelBonus));
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersPreserveFractionalAlphaChanceAsReadOnly()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp, normalAlphaChance: 2.5F);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounters-fractional-alpha-load");
        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        var table = Assert.Single(workflow.Tables);
        var slot = Assert.Single(table.Slots);
        Assert.Null(slot.AlphaChancePercent);
        Assert.Equal("Alpha Chance", slot.EncounterKind);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("read-only and be preserved", StringComparison.Ordinal));

        var chanceUpdate = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                table.TableId,
                slot.Slot,
                "alphaChancePercent",
                "3"),
            "request-za-encounters-fractional-alpha-edit");
        AssertSuccess(chanceUpdate);
        Assert.Empty(chanceUpdate.Payload!.Session.PendingEdits);
        Assert.Contains(
            chanceUpdate.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("read-only", StringComparison.Ordinal));

        var bonusUpdate = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                table.TableId,
                slot.Slot,
                "alphaLevelBonus",
                "11"),
            "request-za-encounters-fractional-alpha-bonus");
        AssertSuccess(bonusUpdate);
        Assert.DoesNotContain(
            bonusUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, bonusUpdate.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-fractional-alpha-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                bonusUpdate.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-fractional-alpha-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var written = ReadEncountData(temp, "wild_ignore");
        Assert.Equal(2.5F, written.OyabunProbability);
        Assert.Equal(11, written.OyabunAdditionalLevel);
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersPreserveUnsafeAlphaLevelBonusAsReadOnly()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp, normalAlphaLevelBonus: -1);
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounters-unsafe-alpha-bonus-load");
        AssertSuccess(load);
        var workflow = load.Payload!.Workflow;
        var table = Assert.Single(workflow.Tables);
        var slot = Assert.Single(table.Slots);
        Assert.Equal(5, slot.AlphaChancePercent);
        Assert.Null(slot.AlphaLevelBonus);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("read-only and be preserved", StringComparison.Ordinal));

        var bonusUpdate = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                table.TableId,
                slot.Slot,
                "alphaLevelBonus",
                "12"),
            "request-za-encounters-unsafe-alpha-bonus-edit");
        AssertSuccess(bonusUpdate);
        Assert.Empty(bonusUpdate.Payload!.Session.PendingEdits);
        Assert.Contains(
            bonusUpdate.Payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error
                && diagnostic.Message.Contains("read-only", StringComparison.Ordinal));

        var disableAlphaUpdate = Dispatch<UpdateEncounterSlotFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateEncounterSlotField,
            new UpdateEncounterSlotFieldRequest(
                paths,
                Session: null,
                table.TableId,
                slot.Slot,
                "alphaChancePercent",
                "0"),
            "request-za-encounters-unsafe-alpha-bonus-disable");
        AssertSuccess(disableAlphaUpdate);
        Assert.DoesNotContain(
            disableAlphaUpdate.Payload!.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(
                paths,
                disableAlphaUpdate.Payload.Session,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-unsafe-alpha-bonus-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(
                paths,
                disableAlphaUpdate.Payload.Session,
                plan.Payload.ChangePlan,
                ChangePlanOutputModeDto.TrinityModManager),
            "request-za-encounters-unsafe-alpha-bonus-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(
            apply.Payload!.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var written = ReadEncountData(temp, "wild_ignore");
        Assert.Equal(0F, written.OyabunProbability);
        Assert.Equal(-1, written.OyabunAdditionalLevel);
    }

    [Fact]
    public void PokemonLegendsZAWildEncountersExposeWildZoneCompletionContributionPerSlot()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp);
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonSpawnerDataArray,
            CreateWildZoneCompletionSpawnerDataArray());
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var load = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-wild-zone-completion-slots");

        AssertSuccess(load);
        var tables = load.Payload!.Workflow.Tables;

        var contributingSlot = Assert.Single(tables.Single(table => table.TableId == "za-spawner:0:0").Slots);
        Assert.True(contributingSlot.ContributesToWildZoneCompletion);

        var excludedSlot = Assert.Single(tables.Single(table => table.TableId == "za-spawner:0:1").Slots);
        Assert.False(excludedSlot.ContributesToWildZoneCompletion);
        Assert.Equal(contributingSlot.EncounterRecordId, excludedSlot.EncounterRecordId);

        var mixedSlots = tables.Single(table => table.TableId == "za-spawner:0:2").Slots;
        Assert.Equal(2, mixedSlots.Count);
        Assert.Contains(mixedSlots, slot => slot.ContributesToWildZoneCompletion == true);
        Assert.Contains(mixedSlots, slot => slot.ContributesToWildZoneCompletion == false);

        var outsideWildZoneSlot = Assert.Single(tables.Single(table => table.TableId == "za-spawner:0:3").Slots);
        Assert.Null(outsideWildZoneSlot.ContributesToWildZoneCompletion);
    }

    [Fact]
    public void PokemonLegendsZASpawnerNumbersMatchAcrossWildEncountersAndPlacement()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WriteWildEncounterFixture(temp);
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonSpawnerDataArray,
            CreateNumberedWildZoneSpawnerDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonSpawnerTransformArray,
            CreateNumberedWildZoneSpawnerTransformArray());
        var dispatcher = CreateDispatcherWithZaCache(temp);
        var paths = CreatePaths(temp);

        var encountersLoad = Dispatch<LoadEncountersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadEncountersWorkflow,
            new LoadEncountersWorkflowRequest(paths),
            "request-za-encounter-spawner-order");
        var placementLoad = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-za-placement-spawner-order");

        AssertSuccess(encountersLoad);
        AssertSuccess(placementLoad);
        var encounterTables = encountersLoad.Payload!.Workflow.Tables;
        Assert.Equal("Spawner 1", encounterTables.Single(table => table.TableId == "za-spawner:0:0").TableLabel);
        Assert.Equal("Spawner 1", encounterTables.Single(table => table.TableId == "za-spawner:0:1").TableLabel);
        Assert.Equal("Spawner 2", encounterTables.Single(table => table.TableId == "za-spawner:0:2").TableLabel);
        Assert.Equal("Spawner 2", encounterTables.Single(table => table.TableId == "za-spawner:0:3").TableLabel);
        Assert.Equal("Spawner 10", encounterTables.Single(table => table.TableId == "za-spawner:0:11").TableLabel);

        var placementObjects = placementLoad.Payload!.Workflow.Objects;
        Assert.Equal(
            "Wild Zone 1 Spawner 1",
            placementObjects.Single(placedObject => placedObject.ItemHash == "wz1_spawn_001").Label);
        Assert.Equal(
            "Wild Zone 1 Spawner 2",
            placementObjects.Single(placedObject => placedObject.ItemHash == "wz1_spawn_002").Label);
        Assert.Equal(
            "Wild Zone 1 Spawner 10",
            placementObjects.Single(placedObject => placedObject.ItemHash == "wz1_spawn_010").Label);
        Assert.Equal(
            "Wild Zone 2 Spawner 1",
            placementObjects.Single(placedObject => placedObject.ItemHash == "wz2_spawn_001").Label);
        Assert.Equal(
            "Wild Zone 2 Spawner 2",
            placementObjects.Single(placedObject => placedObject.ItemHash == "wz2_spawn_002").Label);
        Assert.DoesNotContain(
            "Spawner",
            placementObjects.Single(placedObject => placedObject.ItemHash == "transform_only_row").Label,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PokemonLegendsZAPlacementEditWritesSpawnerTransformTable()
    {
        using var temp = CreatePokemonLegendsZAProject();
        WritePlacementFixture(temp);
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        Assert.Equal(12, workflow.Objects.Count);
        Assert.Contains(workflow.Categories!, category => category.Id == "pokemonSpawners" && category.ObjectCount == 3);
        Assert.Contains(workflow.Categories!, category => category.Id == "itemBallSpawners" && category.ObjectCount == 9);
        Assert.Contains(workflow.EditableFields, field => field.Field == "point.positionX" && field.Label == "Position X");
        Assert.Contains(workflow.EditableFields, field => field.Field == "point.attachTransformEnable" && field.Label == "Attach Transform");

        var pokemonSpawner = workflow.Objects.Single(placedObject => placedObject.ItemHash == "wild_spawn_001");
        Assert.Equal("pokemonSpawners", pokemonSpawner.CategoryId);
        Assert.Equal("Pokemon Spawner", pokemonSpawner.ObjectType);
        Assert.Equal("Wild Zone 1 Spawner 1", pokemonSpawner.Label);
        Assert.Equal("Wild Zone 1 - Vert District, Sector 2", pokemonSpawner.Map);
        Assert.Equal(1, pokemonSpawner.X);
        Assert.Equal(45, pokemonSpawner.RotationY);
        Assert.EndsWith(ZaDataPaths.PokemonSpawnerTransformArray, pokemonSpawner.Provenance.SourceFile, StringComparison.Ordinal);
        Assert.Contains(pokemonSpawner.Fields!, field => field.Field == "spawner.location" && field.DisplayValue == "Wild Zone 1 - Vert District, Sector 2");
        Assert.Contains(pokemonSpawner.Fields!, field => field.Field == "spawner.district" && field.DisplayValue == "Vert District");
        Assert.Contains(pokemonSpawner.Fields!, field => field.Field == "spawner.sector" && field.DisplayValue == "Sector 2");
        Assert.Contains(pokemonSpawner.Fields!, field => field.Field == "spawner.id" && field.DisplayValue == "za_wild_spawner_001");
        Assert.Contains(pokemonSpawner.Fields!, field => field.Field == "spawner.encounterRows" && field.DisplayValue == "1");

        var restaurantSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "mission_rest4_object");
        Assert.Equal(
            "Full Course of Battles: High Rolling Battle 1 Spawn Point 001",
            restaurantSpawner.Label);
        Assert.Equal("Full Course of Battles: High Rolling", restaurantSpawner.Map);
        Assert.Contains(
            restaurantSpawner.Fields!,
            field => field.Field == "spawner.mission" && field.DisplayValue == "Side Mission 73");

        var defenselessDodgerSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "mission_subq147_object");
        Assert.Equal("Be a Defenseless Dodger! Spawn Point 002B", defenselessDodgerSpawner.Label);
        Assert.Equal("Be a Defenseless Dodger!", defenselessDodgerSpawner.Map);
        Assert.Contains(
            defenselessDodgerSpawner.Fields!,
            field => field.Field == "spawner.mission" && field.DisplayValue == "Side Mission 173");

        var itemBallSpawner = workflow.Objects.Single(placedObject => placedObject.ItemHash == "itb_a0201_01");
        Assert.Equal("itemBallSpawners", itemBallSpawner.CategoryId);
        Assert.Equal("Item Ball Spawner", itemBallSpawner.ObjectType);
        Assert.Equal("Bleu District, Sector 1 Item Ball 01: Potion", itemBallSpawner.Label);
        Assert.Equal("Bleu District, Sector 1", itemBallSpawner.Map);
        Assert.Equal("Potion", itemBallSpawner.ItemName);
        Assert.Contains(itemBallSpawner.Fields!, field => field.Field == "spawner.location" && field.DisplayValue == "Bleu District, Sector 1");
        Assert.Contains(itemBallSpawner.Fields!, field => field.Field == "spawner.district" && field.DisplayValue == "Bleu District");
        Assert.Contains(itemBallSpawner.Fields!, field => field.Field == "spawner.sector" && field.DisplayValue == "Sector 1");
        Assert.Contains(itemBallSpawner.Fields!, field => field.Field == "spawner.id" && field.DisplayValue == "id_itb_a0201_01");
        Assert.Contains(itemBallSpawner.Fields!, field => field.Field == "spawner.itemTables" && field.DisplayValue == "1");
        Assert.Contains(itemBallSpawner.Fields!, field => field.Field == "spawner.primaryData" && field.DisplayValue == "Potion");

        var interiorItemBallSpawner = workflow.Objects.Single(placedObject => placedObject.ItemHash == "itb_t1_i004a_01");
        Assert.Equal("Lumiose City, Interior Area 004A Item Ball 01: Rare Candy", interiorItemBallSpawner.Label);
        Assert.Equal("Lumiose City, Interior Area 004A", interiorItemBallSpawner.Map);
        Assert.Equal("Rare Candy", interiorItemBallSpawner.ItemName);
        Assert.Contains(interiorItemBallSpawner.Fields!, field => field.Field == "spawner.location" && field.DisplayValue == "Lumiose City, Interior Area 004A");
        Assert.Contains(interiorItemBallSpawner.Fields!, field => field.Field == "spawner.district" && field.DisplayValue == "None");
        Assert.Contains(interiorItemBallSpawner.Fields!, field => field.Field == "spawner.sector" && field.DisplayValue == "None");
        Assert.Contains(interiorItemBallSpawner.Fields!, field => field.Field == "spawner.id" && field.DisplayValue == "id_itb_t1_i004a_01");
        Assert.Contains(interiorItemBallSpawner.Fields!, field => field.Field == "spawner.primaryData" && field.DisplayValue == "Rare Candy");

        var lysandreAliasSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "itb_t2_01");
        Assert.Equal("Lysandre Labs Item Ball 01: Potion", lysandreAliasSpawner.Label);
        Assert.Equal("Lysandre Labs", lysandreAliasSpawner.Map);

        var sewerMainAliasSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "itb_t3_01");
        Assert.Equal(
            "Lumiose Sewers Main Area Item Ball 01: Potion",
            sewerMainAliasSpawner.Label);
        Assert.Equal("Lumiose Sewers Main Area", sewerMainAliasSpawner.Map);

        var sewerSideAliasSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "itb_t3_2_01");
        Assert.Equal(
            "Lumiose Sewers Side Area Item Ball 01: Potion",
            sewerSideAliasSpawner.Label);
        Assert.Equal("Lumiose Sewers Side Area", sewerSideAliasSpawner.Map);

        var lysandreDungeonSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "itd_d01_01_01");
        Assert.Equal("Lysandre Labs Item 01: Potion", lysandreDungeonSpawner.Label);
        Assert.Equal("Lysandre Labs", lysandreDungeonSpawner.Map);

        var sewerMainDungeonSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "itd_d02_01_01");
        Assert.Equal(
            "Lumiose Sewers Main Area Item 01: Potion",
            sewerMainDungeonSpawner.Label);
        Assert.Equal("Lumiose Sewers Main Area", sewerMainDungeonSpawner.Map);

        var sewerSideDungeonSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "itd_d02_02_01");
        Assert.Equal(
            "Lumiose Sewers Side Area Item 01: Potion",
            sewerSideDungeonSpawner.Label);
        Assert.Equal("Lumiose Sewers Side Area", sewerSideDungeonSpawner.Map);

        var oldBuildingDungeonSpawner = workflow.Objects.Single(
            placedObject => placedObject.ItemHash == "itd_d03_01_01");
        Assert.Equal("Old Building Item 01: Potion", oldBuildingDungeonSpawner.Label);
        Assert.Equal("Old Building", oldBuildingDungeonSpawner.Map);

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
        Assert.NotNull(update.Payload.Workflow);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);
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
        Assert.Null(encounter.FlawlessIvCount);
        Assert.Contains("HP 10", encounter.IvSummary);
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
        var ivUpdate = Dispatch<UpdateStaticEncounterFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateStaticEncounterField,
            new UpdateStaticEncounterFieldRequest(
                paths,
                moveUpdate.Payload!.Session,
                encounter.EncounterIndex,
                "flawlessIvCount",
                "3"),
            "request-za-static-encounters-ivs");
        AssertSuccess(ivUpdate);
        var updatedEncounter = Assert.Single(ivUpdate.Payload!.Workflow.Encounters);
        Assert.Equal(1, updatedEncounter.SpeciesId);
        Assert.Equal("Bulbasaur", updatedEncounter.Species);
        Assert.Contains("Bulbasaur", updatedEncounter.Label);
        Assert.DoesNotContain("Ivysaur", updatedEncounter.Label);
        Assert.Equal(42, updatedEncounter.Level);
        Assert.Equal(45, updatedEncounter.Moves[0].MoveId);
        Assert.Equal(3, updatedEncounter.FlawlessIvCount);
        Assert.Equal("3 guaranteed perfect IVs", updatedEncounter.IvSummary);

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, ivUpdate.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-static-encounters-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        Assert.Contains(plan.Payload.ChangePlan.Writes, write => write.TargetRelativePath == ZaDataPaths.EncountDataArray);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, ivUpdate.Payload.Session, plan.Payload.ChangePlan, ChangePlanOutputModeDto.TrinityModManager),
            "request-za-static-encounters-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var written = ReadEncountData(temp, "static_event_ivysaur");
        Assert.Equal(1, written.DevNo);
        Assert.Equal(42, written.MinLevel);
        Assert.Equal(42, written.MaxLevel);
        Assert.Equal(45, written.WazaList!.Value.Waza1);
        Assert.Equal(17, written.HoldItem!.Value.HoldItem);
        Assert.Equal(128, written.TalentScale);
        Assert.Equal(3, written.TalentVNum);
        Assert.Equal(-1, written.TalentValue!.Value.Hp);
        Assert.Equal(-1, written.TalentValue!.Value.Atk);
        Assert.Equal(-1, written.TalentValue!.Value.Def);
        Assert.Equal(-1, written.TalentValue!.Value.SpAtk);
        Assert.Equal(-1, written.TalentValue!.Value.SpDef);
        Assert.Equal(-1, written.TalentValue!.Value.Agi);
        Assert.Equal(101, written.StrengthenValue!.Value.Hp);
        Assert.Equal("drop_table_001", written.ItemDropInfoList(0)!.Value.ItemTableId);
        var wild = ReadEncountData(temp, "wild_ignore");
        Assert.Equal(1, wild.DevNo);
        Assert.Equal(20, wild.MinLevel);
        Assert.Equal(33, wild.WazaList!.Value.Waza1);
        Assert.Equal(101, wild.StrengthenValue!.Value.Hp);
        Assert.Equal(75U, wild.ItemDropInfoList(0)!.Value.DropProbability);
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
        var dispatcher = CreateDispatcherWithZaCache(temp);

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
        temp.WriteBaseRomFsFile(ZaDataPaths.EvolutionItemConversionArray, CreateEvolutionItemConversionArray());
        temp.EnsurePokemonLegendsZASupportFolder();
        return temp;
    }

    private static byte[] CreateEvolutionItemConversionArray()
    {
        var rows = new List<EvolutionItemConversion>
        {
            new(0, 66), new(280, 19), new(0, 11), new(1857, 88), new(1116, 79), new(306, 34),
            new(0, 42), new(0, 58), new(0, 74), new(0, 94), new(0, 13), new(0, 68),
            new(0, 105), new(0, 103), new(0, 104), new(305, 33), new(236, 16), new(0, 67),
            new(0, 64), new(0, 53), new(0, 61), new(0, 69), new(0, 43), new(309, 37),
            new(1582, 83), new(1592, 84), new(0, 59), new(0, 106), new(0, 63), new(0, 56),
            new(0, 73), new(1779, 10), new(107, 7), new(298, 26), new(82, 3), new(0, 54),
            new(0, 14), new(307, 35), new(300, 28), new(765, 102), new(0, 12), new(0, 72),
            new(2344, 85), new(1254, 82), new(83, 4), new(2345, 87), new(229, 15), new(0, 95),
            new(0, 48), new(303, 31), new(849, 52), new(313, 41), new(0, 47), new(312, 40),
            new(0, 97), new(1691, 1691), new(1103, 70), new(1104, 71), new(110, 9), new(0, 44),
            new(109, 93), new(301, 29), new(0, 96), new(84, 5), new(1858, 89), new(310, 38),
            new(304, 32), new(0, 100), new(0, 18), new(1861, 86), new(0, 76), new(0, 46),
            new(293, 24), new(292, 23), new(290, 21), new(291, 22), new(289, 20), new(294, 25),
            new(0, 90), new(0, 55), new(0, 78), new(85, 6), new(0, 57), new(311, 39),
            new(0, 65), new(0, 45), new(644, 51), new(299, 27), new(1117, 80), new(0, 50),
            new(326, 49), new(0, 77), new(0, 60), new(80, 1), new(308, 36), new(0, 91),
            new(81, 2), new(302, 30), new(0, 62), new(0, 17), new(0, 99), new(0, 101),
            new(1253, 81), new(0, 98), new(108, 8), new(218, 92), new(0, 75), new(847, 121),
        };

        return EvolutionItemConversionTable.Write(rows);
    }

    private static ProjectPathsDto CreatePaths(TemporaryBridgeProject temp)
    {
        return temp.Paths with
        {
            SelectedGame = ProjectGameDto.ZA,
            PokemonLegendsZASupportFolderPath = temp.PokemonLegendsZASupportFolderPath,
        };
    }

    private static ProjectBridgeDispatcher CreateDispatcherWithZaCache(TemporaryBridgeProject temp)
    {
        return new ProjectBridgeDispatcher(
            zaWorkflowService: new ZaWorkflowService(
                cacheManager: new ZaCacheManager(Path.Combine(temp.RootPath, "za-cache"))));
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

    private static void WriteZaOutput(TemporaryBridgeProject temp, string relativePath, byte[] contents)
    {
        temp.WriteOutputFile(Path.Combine("romfs", relativePath.Replace('/', Path.DirectorySeparatorChar)), contents);
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

    private static byte[] CreatePersonalArray(
        ushort evolutionCondition = 4,
        ushort evolutionParameter = 0,
        ushort evolutionSpecies = 1,
        byte evolutionForm = 0,
        ushort evolutionLevel = 16,
        ushort bulbasaurDexOrder = 25,
        ushort ivysaurDexOrder = 26,
        uint bulbasaurSpeciesReserved3 = 0,
        uint ivysaurSpeciesReserved3 = 0,
        bool legacyByteDexOrderLayout = false,
        bool ivysaurHasType1 = true,
        bool ivysaurHasDexOrder = true,
        bool bulbasaurPresent = true)
    {
        var builder = new FlatBufferBuilder(1024);
        builder.ForceDefaults = legacyByteDexOrderLayout;
        var empty = CreatePersonal(
            builder,
            species: 0,
            present: false,
            hp: 0,
            zaDexOrder: 0,
            legacyByteDexOrderLayout: legacyByteDexOrderLayout);
        var bulbasaur = CreatePersonal(
            builder,
            species: 1,
            present: bulbasaurPresent,
            hp: 45,
            zaDexOrder: bulbasaurDexOrder,
            evolutionCondition,
            evolutionParameter,
            evolutionSpecies,
            evolutionForm,
            evolutionLevel,
            legacyByteDexOrderLayout,
            includeType1: true,
            speciesReserved3: bulbasaurSpeciesReserved3);
        var ivysaur = CreatePersonal(
            builder,
            species: 2,
            present: true,
            hp: 60,
            zaDexOrder: ivysaurDexOrder,
            evolutionCondition,
            evolutionParameter,
            evolutionSpecies,
            evolutionForm,
            evolutionLevel,
            legacyByteDexOrderLayout,
            includeType1: ivysaurHasType1,
            includeDexOrder: ivysaurHasDexOrder,
            speciesReserved3: ivysaurSpeciesReserved3);
        var charmander = CreatePersonal(
            builder,
            species: 3,
            present: false,
            hp: 39,
            zaDexOrder: 0,
            evolutionCondition,
            evolutionParameter,
            evolutionSpecies,
            evolutionForm,
            evolutionLevel,
            legacyByteDexOrderLayout);
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
        var vector = ZaMoveDataArray.CreateValuesVector(builder, [ancientPower]);
        var root = ZaMoveDataArray.CreateZaMoveDataArray(builder, vector);
        ZaMoveDataArray.FinishZaMoveDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateItemDataArray(
        bool includeAdditionalMachines = false,
        bool includeCustomEvolutionItem = false,
        bool includePokemonItemTypes = false,
        bool includeRestoredOvalStone = false,
        bool includeUseGateRegressionItems = false,
        bool customEvolutionItemsEnabled = true,
        int noMintNature = -1,
        int extraInertSentinelRows = 0,
        int legacyZeroExtraRows = 0)
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
            sortOrder: 1,
            mintNature: noMintNature);
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
            canUseInBattle: true,
            mintNature: noMintNature);
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
            machineIndex: -1,
            mintNature: noMintNature);
        var rows = new List<Offset<ZaItemData>> { pokeBall, potion, tm };
        if (includeAdditionalMachines)
        {
            var tm1 = CreateItem(
                builder,
                itemId: 328,
                itemType: 5,
                internalName: "WAZAMASIN01",
                iconName: "item_0332",
                price: 1000,
                pocket: 6,
                stackCap: 1,
                sortOrder: 1,
                machineMoveId: 45,
                machineIndex: 0,
                mintNature: noMintNature);
            var tm2 = CreateItem(
                builder,
                itemId: 329,
                itemType: 5,
                internalName: "WAZAMASIN02",
                iconName: "item_0333",
                price: 1000,
                pocket: 6,
                stackCap: 1,
                sortOrder: 2,
                machineMoveId: 33,
                machineIndex: 1,
                mintNature: noMintNature);
            rows = [pokeBall, potion, tm1, tm2];
        }

        if (includeCustomEvolutionItem)
        {
            rows.Add(CreateItem(
                builder,
                itemId: 2,
                itemType: 4,
                internalName: "HAIPAABOORU",
                iconName: "item_0002",
                price: 600,
                pocket: 1,
                stackCap: 999,
                sortOrder: 3,
                workEvolutional: customEvolutionItemsEnabled,
                mintNature: noMintNature));
            rows.Add(CreateItem(
                builder,
                itemId: 248,
                itemType: 1,
                internalName: "MAGARISPOON",
                iconName: "item_0248",
                price: 3000,
                pocket: 2,
                stackCap: 999,
                sortOrder: 248,
                workEvolutional: customEvolutionItemsEnabled,
                mintNature: noMintNature));
            rows.Add(CreateItem(
                builder,
                itemId: 86,
                itemType: 3,
                internalName: "TIINAKINOKO",
                iconName: "item_0086",
                price: 500,
                pocket: 3,
                stackCap: 999,
                sortOrder: 86,
                mintNature: noMintNature));
            rows.Add(CreateItem(
                builder,
                itemId: 1861,
                itemType: 7,
                internalName: "NOROINOYOROI",
                iconName: "item_1861",
                price: 3000,
                pocket: 2,
                stackCap: 999,
                sortOrder: 1861,
                workEvolutional: customEvolutionItemsEnabled,
                mintNature: noMintNature));
        }

        if (includePokemonItemTypes)
        {
            rows.Add(CreateItem(
                builder,
                itemId: 82,
                itemType: 7,
                internalName: "HONOONOISI",
                iconName: "item_0082",
                price: 3000,
                pocket: 2,
                stackCap: 999,
                sortOrder: 42,
                workEvolutional: true));
            rows.Add(CreateItem(
                builder,
                itemId: 2563,
                itemType: 7,
                internalName: "MEGANIUMUNAITO",
                iconName: "item_2563",
                price: 100000,
                pocket: 7,
                stackCap: 1,
                sortOrder: 1));
        }

        if (includeUseGateRegressionItems)
        {
            rows.Add(CreateItem(
                builder,
                itemId: 82,
                itemType: 7,
                internalName: "HONOONOISI",
                iconName: "item_0082",
                price: 3000,
                pocket: 2,
                stackCap: 999,
                sortOrder: 42,
                workEvolutional: true,
                mintNature: noMintNature));
            rows.Add(CreateItem(
                builder,
                itemId: 218,
                itemType: 1,
                internalName: "YASURAGINOSUZU",
                iconName: "item_0218",
                price: 3000,
                pocket: 2,
                stackCap: 999,
                sortOrder: 218,
                mintNature: noMintNature));
            rows.Add(CreateItem(
                builder,
                itemId: 1231,
                itemType: 2,
                internalName: "SABISIGARIMINTO",
                iconName: "item_1231",
                price: 20000,
                pocket: 2,
                stackCap: 999,
                sortOrder: 1231,
                mintNature: 1));
        }

        if (includeRestoredOvalStone)
        {
            rows.Add(CreateItem(
                builder,
                itemId: 110,
                itemType: 7,
                internalName: "MAARUIISI",
                iconName: "item_0110",
                price: 3000,
                pocket: 2,
                stackCap: 999,
                sortOrder: 110,
                workEvolutional: true));
        }

        for (var index = 0; index < extraInertSentinelRows; index++)
        {
            var itemId = 3000 + index;
            rows.Add(CreateItem(
                builder,
                itemId,
                itemType: 1,
                internalName: $"LEGACY_SENTINEL_{itemId}",
                iconName: $"item_{itemId:D4}",
                price: 1000 + index,
                pocket: 2,
                stackCap: 999,
                sortOrder: itemId,
                mintNature: index < legacyZeroExtraRows ? 0 : noMintNature));
        }

        var vector = ZaItemDataArray.CreateValuesVector(builder, rows.ToArray());
        var root = ZaItemDataArray.CreateZaItemDataArray(builder, vector);
        ZaItemDataArray.FinishZaItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateHighNumberTechnicalMachineItemDataArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var tm100 = CreateItem(
            builder,
            itemId: 2160,
            itemType: 5,
            internalName: "WAZAMASIN100",
            iconName: "item_2160",
            price: 0,
            pocket: 6,
            stackCap: 1,
            sortOrder: 37,
            machineMoveId: 526,
            machineIndex: 99);
        var tm102 = CreateItem(
            builder,
            itemId: 2162,
            itemType: 5,
            internalName: "WAZAMASIN102",
            iconName: "item_2162",
            price: 0,
            pocket: 6,
            stackCap: 1,
            sortOrder: 102,
            machineMoveId: 528,
            machineIndex: 101);
        var vector = ZaItemDataArray.CreateValuesVector(builder, [tm100, tm102]);
        var root = ZaItemDataArray.CreateZaItemDataArray(builder, vector);
        ZaItemDataArray.FinishZaItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateReorderedTechnicalMachineItemDataArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var rockSmash = CreateItem(
            builder,
            itemId: 619,
            itemType: 5,
            internalName: "WAZAMASIN94",
            iconName: "item_0619",
            price: 0,
            pocket: 6,
            stackCap: 1,
            sortOrder: 4,
            machineMoveId: 249,
            machineIndex: 93);
        var vector = ZaItemDataArray.CreateValuesVector(builder, [rockSmash]);
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
            ZaDataPaths.TrainerNameKeys("English"),
            CreateKeyTable((0, "tr_battle_main_001")));
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
        bool includeAlphaAndRawSpawners = false,
        bool includeDistinctEncounterSpawner = false,
        bool includeMixedAlphaReference = false,
        float normalAlphaChance = 5,
        int normalAlphaLevelBonus = 10)
    {
        temp.WriteBaseRomFsFile(ZaDataPaths.PokemonDataArray, CreatePokemonDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.EncountDataArray,
            CreateEncounterDataArray(
                wildAlphaChance: normalAlphaChance,
                wildAlphaLevelBonus: normalAlphaLevelBonus,
                includeGuaranteedWildRow: includeAlphaAndRawSpawners));
        temp.WriteBaseRomFsFile(ZaDataPaths.PersonalArray, CreatePersonalArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonSpawnerDataArray,
            CreatePokemonSpawnerDataArray(
                includeAlphaAndRawSpawners,
                includeDistinctEncounterSpawner,
                includeMixedAlphaReference));
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
        temp.WriteBaseRomFsFile(ZaDataPaths.EncountDataArray, CreateEncounterDataArray());
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
        temp.WriteBaseRomFsFile(
            ZaDataPaths.PokemonSpawnerDataArray,
            CreatePokemonSpawnerDataArray(includePlacementMissionSpawners: true));
        temp.WriteBaseRomFsFile(ZaDataPaths.ItemBallSpawnerDataArray, CreateItemBallSpawnerDataArray());
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemNames("English"),
            CreateTextTable(328, (4, "Poke Ball"), (17, "Potion"), (50, "Rare Candy"), (328, "TM001")));
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
                attach: true,
                additionalNames: ["mission_rest4_object", "mission_subq147_object"]));
        temp.WriteBaseRomFsFile(
            ZaDataPaths.ItemBallSpawnerTransformArray,
            CreateItemBallSpawnerTransformArray());
    }

    private static byte[] CreateItemBallSpawnerDataArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var districtSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itb_a0201_01",
            "itb_a0201_01",
            "field_item_ball_0017",
            "t1_item_ball_object");
        var interiorSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itb_t1_i004a_01",
            "itb_t1_i004a_01",
            "field_item_ball_0050",
            "./");
        var lysandreAliasSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itb_t2_01",
            "itb_t2_01",
            "field_item_ball_0017",
            "./");
        var sewerMainAliasSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itb_t3_01",
            "itb_t3_01",
            "field_item_ball_0017",
            "./");
        var sewerSideAliasSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itb_t3_2_01",
            "itb_t3_2_01",
            "field_item_ball_0017",
            "./");
        var lysandreDungeonSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itd_d01_01_01",
            "itd_d01_01_01",
            "field_item_ball_0017",
            "./");
        var sewerMainDungeonSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itd_d02_01_01",
            "itd_d02_01_01",
            "field_item_ball_0017",
            "./");
        var sewerSideDungeonSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itd_d02_02_01",
            "itd_d02_02_01",
            "field_item_ball_0017",
            "./");
        var oldBuildingDungeonSpawner = CreateItemBallSpawnerData(
            builder,
            "id_itd_d03_01_01",
            "itd_d03_01_01",
            "field_item_ball_0017",
            "./");
        var rootVector = ItemBallSpawnerDataDB.CreateRootVector(
            builder,
            [
                districtSpawner,
                interiorSpawner,
                lysandreAliasSpawner,
                sewerMainAliasSpawner,
                sewerSideAliasSpawner,
                lysandreDungeonSpawner,
                sewerMainDungeonSpawner,
                sewerSideDungeonSpawner,
                oldBuildingDungeonSpawner,
            ]);
        var db = ItemBallSpawnerDataDB.CreateItemBallSpawnerDataDB(builder, rootVector);
        var valuesVector = ItemBallSpawnerDataDBArray.CreateValuesVector(builder, [db]);
        var root = ItemBallSpawnerDataDBArray.CreateItemBallSpawnerDataDBArray(builder, valuesVector);
        ItemBallSpawnerDataDBArray.FinishItemBallSpawnerDataDBArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<ItemBallSpawnerData> CreateItemBallSpawnerData(
        FlatBufferBuilder builder,
        string spawnerId,
        string objectName,
        string tableId,
        string scenePath)
    {
        var tableIdOffset = builder.CreateString(tableId);
        var table = ItemBallTableInfo.CreateTableInfo(builder, tableIdOffset);
        var tables = ItemBallSpawnerData.CreateTableInfoListVector(builder, [table]);
        var objectNameOffset = builder.CreateString(objectName);
        var scenePathOffset = builder.CreateString(scenePath);
        var appearanceInfo = ItemBallAppearanceInfo.CreateAppearanceInfo(builder, minCount: 1, maxCount: 1);
        var appearance = ItemBallAppearanceSpawnerObjectInfo.CreateAppearanceSpawnerObjectInfo(
            builder,
            objectNameOffset,
            scenePathOffset,
            appearanceInfoOffset: appearanceInfo);
        var appearances = ItemBallSpawnerData.CreateAppearanceSpawnerObjectInfoListVector(builder, [appearance]);
        var spawnerIdOffset = builder.CreateString(spawnerId);
        return ItemBallSpawnerData.CreateItemBallSpawnerData(
            builder,
            spawnerIdOffset,
            tables,
            appearances);
    }

    private static byte[] CreateItemBallSpawnerTransformArray()
    {
        var document = ZaSpawnerTransformDocument.Create(
        [
            new ZaSpawnerTransformGroup(
                0,
                [
                    new ZaSpawnerTransformRow(
                        0,
                        0,
                        "itb_a0201_01",
                        new ZaSpawnerTransformVector(4, 5, 6),
                        new ZaSpawnerTransformVector(10, 90, 20),
                        true),
                    new ZaSpawnerTransformRow(
                        0,
                        1,
                        "itb_t1_i004a_01",
                        new ZaSpawnerTransformVector(12, 0, -38),
                        new ZaSpawnerTransformVector(0, 180, 0),
                        false),
                    new ZaSpawnerTransformRow(
                        0,
                        2,
                        "itb_t2_01",
                        new ZaSpawnerTransformVector(1, 2, 3),
                        new ZaSpawnerTransformVector(0, 0, 0),
                        false),
                    new ZaSpawnerTransformRow(
                        0,
                        3,
                        "itb_t3_01",
                        new ZaSpawnerTransformVector(1, 2, 3),
                        new ZaSpawnerTransformVector(0, 0, 0),
                        false),
                    new ZaSpawnerTransformRow(
                        0,
                        4,
                        "itb_t3_2_01",
                        new ZaSpawnerTransformVector(1, 2, 3),
                        new ZaSpawnerTransformVector(0, 0, 0),
                        false),
                    new ZaSpawnerTransformRow(
                        0,
                        5,
                        "itd_d01_01_01",
                        new ZaSpawnerTransformVector(1, 2, 3),
                        new ZaSpawnerTransformVector(0, 0, 0),
                        false),
                    new ZaSpawnerTransformRow(
                        0,
                        6,
                        "itd_d02_01_01",
                        new ZaSpawnerTransformVector(1, 2, 3),
                        new ZaSpawnerTransformVector(0, 0, 0),
                        false),
                    new ZaSpawnerTransformRow(
                        0,
                        7,
                        "itd_d02_02_01",
                        new ZaSpawnerTransformVector(1, 2, 3),
                        new ZaSpawnerTransformVector(0, 0, 0),
                        false),
                    new ZaSpawnerTransformRow(
                        0,
                        8,
                        "itd_d03_01_01",
                        new ZaSpawnerTransformVector(1, 2, 3),
                        new ZaSpawnerTransformVector(0, 0, 0),
                        false),
                ]),
        ]);
        return document.Write();
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
            sex: signedDefaults ? -1 : 1,
            talentScale: 128);
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

    private static byte[] CreateEncounterDataArray(
        bool signedDefaults = false,
        float wildAlphaChance = 5,
        int wildAlphaLevelBonus = 10,
        bool includeGuaranteedWildRow = false)
    {
        var builder = new FlatBufferBuilder(4096);
        var giftScene = CreateEncounterData(
            builder,
            "main_init_poke_1",
            level: 0,
            heldItem: 0,
            move1: -1,
            move2: -1,
            ivHp: -1,
            ivAttack: -1,
            sex: 0,
            speciesId: 1);
        var ignored = CreateEncounterData(
            builder,
            "wild_ignore",
            level: 20,
            heldItem: 17,
            move1: 33,
            move2: 0,
            ivHp: 1,
            ivAttack: 2,
            sex: 1,
            speciesId: 1,
            oyabunProbability: wildAlphaChance,
            oyabunAdditionalLevel: wildAlphaLevelBonus);
        var trade = CreateEncounterData(
            builder,
            "sub_tradepoke_bulbasaur",
            level: 5,
            heldItem: 4,
            move1: signedDefaults ? -1 : 33,
            move2: 45,
            ivHp: 31,
            ivAttack: 30,
            sex: signedDefaults ? -1 : 1,
            speciesId: 1);
        var staticEncounter = CreateEncounterData(
            builder,
            "static_event_ivysaur",
            level: 35,
            heldItem: 17,
            move1: signedDefaults ? -1 : 33,
            move2: 45,
            ivHp: 10,
            ivAttack: 11,
            sex: signedDefaults ? -1 : 1,
            speciesId: 2,
            talentScale: 128);
        var giftPlayable = CreateEncounterData(
            builder,
            "test_encount_init_poke_0",
            level: 5,
            heldItem: 4,
            move1: signedDefaults ? -1 : 33,
            move2: 45,
            ivHp: 31,
            ivAttack: 30,
            sex: signedDefaults ? -1 : 1,
            speciesId: 1);
        var rows = new List<Offset<ZaEncounterDataRow>>
        {
            giftScene,
            ignored,
            trade,
            staticEncounter,
            giftPlayable,
        };
        if (includeGuaranteedWildRow)
        {
            rows.Add(CreateEncounterData(
                builder,
                "wild_ordinary",
                level: 20,
                heldItem: 17,
                move1: 33,
                move2: 0,
                ivHp: 1,
                ivAttack: 2,
                sex: 1,
                speciesId: 1,
                oyabunProbability: 0,
                oyabunAdditionalLevel: 0));
            rows.Add(CreateEncounterData(
                builder,
                "wild_guaranteed_alpha",
                level: 20,
                heldItem: 17,
                move1: 33,
                move2: 0,
                ivHp: 1,
                ivAttack: 2,
                sex: 1,
                speciesId: 1,
                oyabunProbability: 100,
                oyabunAdditionalLevel: 9));
            rows.Add(CreateEncounterData(
                builder,
                "wild_guaranteed_plain",
                level: 20,
                heldItem: 17,
                move1: 33,
                move2: 0,
                ivHp: 1,
                ivAttack: 2,
                sex: 1,
                speciesId: 1,
                oyabunProbability: 100,
                oyabunAdditionalLevel: 9));
        }

        var rootVector = ZaEncounterDataDb.CreateRootVector(
            builder,
            rows.ToArray());
        var db = ZaEncounterDataDb.Create(builder, rootVector);
        var valuesVector = ZaEncounterDataDbArray.CreateValuesVector(builder, [db]);
        var root = ZaEncounterDataDbArray.Create(builder, valuesVector);
        ZaEncounterDataDbArray.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreatePokemonSpawnerDataArray(
        bool includeAlphaAndRawSpawners = false,
        bool includeDistinctEncounterSpawner = false,
        bool includeMixedAlphaReference = false,
        bool includePlacementMissionSpawners = false)
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
                    appearanceObjectName: "wild_spawn_001",
                    encounterTag: "internal_\u6761\u4ef6"),
                CreateSpawner(builder, "id_spn_outzone_a0201_A459", "wild_guaranteed_alpha_Alpha", "a0102_w01", weight: 65),
                CreateSpawner(builder, "id_spn_outzone_a0201_A460", "wild_guaranteed_alpha_Alpha", "a0102_w01", weight: 65),
                CreateSpawner(builder, "id_spn_outzone_a0201_A461", "wild_guaranteed_plain", "a0102_w01", weight: 65),
                CreateSpawner(builder, "id_spn_outzone_a0201_050_BZ", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_outzone_a0201_O50_BZ", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_outzone_a0201_P00", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_outzone_a0201_sp1", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_outzone_a0201_A00", "wild_ordinary", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_outzone_a0201_405_A_BZ_PH", "wild_guaranteed_alpha_Alpha", zoneId: null, weight: 100),
                CreateSpawner(builder, "zdm406_v00_700", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "zdm406_v00_701", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_zdm_random_t02_r03_701", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_zdm_random_t02_r03_662_set", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "btl_spn_boss_0015_re", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_d02_01_001", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_chapter5_spn_d02_001", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_chapter9_00", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_sub090_01", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_t2_001", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_t3_001", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_t3_2_001", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_rest4_01_001", "wild_ignore", zoneId: null, weight: 100),
                CreateSpawner(builder, "id_spn_subq147_002b", "wild_ignore", zoneId: null, weight: 100),
            }
            :
            [
                CreateSpawner(
                    builder,
                    "za_wild_spawner_001",
                    "wild_ignore",
                    "a0102_w01",
                    weight: 35,
                    appearanceObjectName: "wild_spawn_001",
                    encounterTag: "internal_\u6761\u4ef6"),
            ];
        if (includeDistinctEncounterSpawner)
        {
            spawners =
            [
                .. spawners,
                CreateSpawner(
                    builder,
                    "za_wild_spawner_002",
                    "static_event_ivysaur",
                    "a0102_w01",
                    weight: 20),
            ];
        }

        if (includeMixedAlphaReference)
        {
            spawners =
            [
                .. spawners,
                CreateSpawner(
                    builder,
                    "za_wild_spawner_mixed_alpha",
                    "wild_ignore_Alpha",
                    "a0102_w01",
                    weight: 20),
            ];
        }

        if (includePlacementMissionSpawners)
        {
            spawners =
            [
                .. spawners,
                CreateSpawner(
                    builder,
                    "id_rest4_01_001",
                    "wild_ignore",
                    zoneId: null,
                    weight: 100,
                    appearanceObjectName: "mission_rest4_object"),
                CreateSpawner(
                    builder,
                    "id_spn_subq147_002b",
                    "wild_ignore",
                    zoneId: null,
                    weight: 100,
                    appearanceObjectName: "mission_subq147_object"),
            ];
        }

        var rootVector = PokemonSpawnerDataDB.CreateRootVector(builder, spawners);
        var db = PokemonSpawnerDataDB.CreatePokemonSpawnerDataDB(builder, rootVector);
        var valuesVector = PokemonSpawnerDataDBArray.CreateValuesVector(builder, [db]);
        var root = PokemonSpawnerDataDBArray.CreatePokemonSpawnerDataDBArray(builder, valuesVector);
        PokemonSpawnerDataDBArray.FinishPokemonSpawnerDataDBArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateNumberedWildZoneSpawnerDataArray()
    {
        var builder = new FlatBufferBuilder(8192);
        var spawners = new List<Offset<PokemonSpawnerData>>
        {
            CreateSpawner(builder, "wz2_data_001", "wild_ignore", "a0103_w01", 100, "wz2_spawn_001"),
            CreateSpawner(builder, "wz1_data_001", "wild_ignore", "a0102_w01", 100, "wz1_spawn_001"),
            CreateSpawner(builder, "wz1_data_002", "wild_ignore", "a0102_w01", 100, "wz1_spawn_002"),
            CreateSpawner(builder, "wz2_data_002", "wild_ignore", "a0103_w01", 100, "wz2_spawn_002"),
        };
        for (var ordinal = 3; ordinal <= 10; ordinal++)
        {
            var suffix = ordinal.ToString("000", CultureInfo.InvariantCulture);
            spawners.Add(CreateSpawner(
                builder,
                $"wz1_data_{suffix}",
                "wild_ignore",
                "a0102_w01",
                100,
                $"wz1_spawn_{suffix}"));
        }

        var rootVector = PokemonSpawnerDataDB.CreateRootVector(builder, spawners.ToArray());
        var db = PokemonSpawnerDataDB.CreatePokemonSpawnerDataDB(builder, rootVector);
        var valuesVector = PokemonSpawnerDataDBArray.CreateValuesVector(builder, [db]);
        var root = PokemonSpawnerDataDBArray.CreatePokemonSpawnerDataDBArray(builder, valuesVector);
        PokemonSpawnerDataDBArray.FinishPokemonSpawnerDataDBArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateWildZoneCompletionSpawnerDataArray()
    {
        var builder = new FlatBufferBuilder(4096);
        var spawners = new[]
        {
            CreateSpawner(
                builder,
                "wz1_completion_001",
                "wild_ignore",
                "a0102_w01",
                100,
                "wz1_completion_spawn_001",
                showMapIcon: 0),
            CreateSpawner(
                builder,
                "wz1_completion_002",
                "wild_ignore",
                "a0102_w01",
                100,
                "wz1_completion_spawn_002",
                showMapIcon: 1),
            CreateSpawner(
                builder,
                "wz1_completion_003",
                "wild_ignore",
                "a0102_w01",
                60,
                "wz1_completion_spawn_003",
                showMapIcon: 0,
                additionalEncounter: ("static_event_ivysaur", 40, 1)),
            CreateSpawner(
                builder,
                "id_spn_outzone_a0201_completion",
                "wild_ignore",
                zoneId: null,
                weight: 100,
                showMapIcon: 0),
        };
        var rootVector = PokemonSpawnerDataDB.CreateRootVector(builder, spawners);
        var db = PokemonSpawnerDataDB.CreatePokemonSpawnerDataDB(builder, rootVector);
        var valuesVector = PokemonSpawnerDataDBArray.CreateValuesVector(builder, [db]);
        var root = PokemonSpawnerDataDBArray.CreatePokemonSpawnerDataDBArray(builder, valuesVector);
        PokemonSpawnerDataDBArray.FinishPokemonSpawnerDataDBArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateNumberedWildZoneSpawnerTransformArray()
    {
        var names = new[]
        {
            "wz1_spawn_010",
            "wz1_spawn_002",
            "wz2_spawn_002",
            "wz1_spawn_001",
            "wz2_spawn_001",
            "transform_only_row",
        };
        var rows = names
            .Select((name, index) => new ZaSpawnerTransformRow(
                0,
                index,
                name,
                new ZaSpawnerTransformVector(index, 0, 0),
                new ZaSpawnerTransformVector(0, 0, 0),
                true))
            .ToArray();
        return ZaSpawnerTransformDocument.Create([new ZaSpawnerTransformGroup(0, rows)]).Write();
    }

    private static Offset<PokemonSpawnerData> CreateSpawner(
        FlatBufferBuilder builder,
        string spawnerName,
        string encounterDataId,
        string? zoneId,
        int weight,
        string? appearanceObjectName = null,
        string? encounterTag = null,
        int showMapIcon = 1,
        (string EncounterDataId, int Weight, int ShowMapIcon)? additionalEncounter = null)
    {
        var encounterId = builder.CreateString(encounterDataId);
        var tagList = string.IsNullOrWhiteSpace(encounterTag)
            ? default
            : EncountDataInfo.CreateTagListVector(builder, [builder.CreateString(encounterTag)]);
        var encounter = EncountDataInfo.CreateEncountDataInfo(
            builder,
            encounterId,
            weight: weight,
            maxCount: 2,
            additionalLevel: 0,
            tagListOffset: tagList,
            showMapIcon: showMapIcon,
            appearedTimeCondition: 4,
            appearedWeatherCondition: 2);
        var encounterOffsets = new List<Offset<EncountDataInfo>> { encounter };
        if (additionalEncounter is { } additional)
        {
            var additionalEncounterId = builder.CreateString(additional.EncounterDataId);
            encounterOffsets.Add(EncountDataInfo.CreateEncountDataInfo(
                builder,
                additionalEncounterId,
                weight: additional.Weight,
                maxCount: 2,
                showMapIcon: additional.ShowMapIcon,
                appearedTimeCondition: 4,
                appearedWeatherCondition: 2));
        }

        var encounters = PokemonSpawnerData.CreateEncountDataInfoListVector(builder, encounterOffsets.ToArray());
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
        bool attach,
        params string[] additionalNames)
    {
        var rows = new List<ZaSpawnerTransformRow>
        {
            new(
                0,
                0,
                name,
                new ZaSpawnerTransformVector(positionX, positionY, positionZ),
                new ZaSpawnerTransformVector(rotationX, rotationY, rotationZ),
                attach),
        };
        rows.AddRange(additionalNames.Select((additionalName, index) => new ZaSpawnerTransformRow(
            0,
            index + 1,
            additionalName,
            new ZaSpawnerTransformVector(positionX + index + 1, positionY, positionZ),
            new ZaSpawnerTransformVector(rotationX, rotationY, rotationZ),
            attach)));

        var document = ZaSpawnerTransformDocument.Create(
        [
            new ZaSpawnerTransformGroup(
                0,
                rows),
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

    private static Offset<ZaEncounterDataRow> CreateEncounterData(
        FlatBufferBuilder builder,
        string id,
        int level,
        int heldItem,
        int move1,
        int move2,
        int ivHp,
        int ivAttack,
        int sex,
        int speciesId,
        int talentScale = 2,
        int talentVNum = 0,
        float oyabunProbability = 0.25F,
        int oyabunAdditionalLevel = 10)
    {
        var idOffset = builder.CreateString(id);
        var talentValue = ZaPokemonDataTalentValue.Create(
            builder,
            hp: ivHp,
            atk: ivAttack,
            def: 29,
            spAtk: 28,
            spDef: 27,
            agi: 26);
        var strengthenValue = ZaPokemonDataTalentValue.Create(
            builder,
            hp: 101,
            atk: 102,
            def: 103,
            spAtk: 104,
            spDef: 105,
            agi: 106);
        var wazaList = ZaPokemonDataWazaList.Create(builder, move1, move2, waza3: 0, waza4: 0);
        var holdItemOffset = ZaPokemonDataHoldItem.Create(builder, heldItem);
        var dropTableId = builder.CreateString("drop_table_001");
        var dropConditions = ZaEncounterItemDropInfo.CreateDropConditionListVector(builder, [7, 8]);
        var drop = ZaEncounterItemDropInfo.Create(
            builder,
            dropTableId,
            dropConditions,
            dropProbability: 75,
            minCount: 1,
            maxCount: 3);
        var drops = ZaEncounterDataRow.CreateItemDropInfoListVector(builder, [drop]);

        return ZaEncounterDataRow.Create(
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
            oyabunProbability: oyabunProbability,
            oyabunAdditionalLevel: oyabunAdditionalLevel,
            talentValueOffset: talentValue,
            strengthenValueOffset: strengthenValue,
            wazaListOffset: wazaList,
            holdItemOffset: holdItemOffset,
            itemDropInfoListOffset: drops);
    }

    private static byte[] CreateTrainerDataArray(
        bool signedDefaults = false,
        string trainerId = "tr_battle_main_001",
        ulong trainerType = 1,
        ulong trainerType2 = 0,
        ushort speciesId = 1,
        int level = 12)
    {
        var builder = new FlatBufferBuilder(2048);
        var trainer = CreateTrainer(builder, signedDefaults, trainerId, trainerType, trainerType2, speciesId, level);
        var vector = ZaTrainerTable.CreateValueVector(builder, [trainer]);
        var root = ZaTrainerTable.Create(builder, vector);
        ZaTrainerTable.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateTrainerLabelFallbackDataArray(ulong trainerType)
    {
        var builder = new FlatBufferBuilder(2048);
        var dimensionTrainer = CreateTrainer(
            builder,
            trainerIdValue: "dim_rank_02_mizu_05",
            trainerType: trainerType,
            trainerType2: trainerType);
        var secondDimensionTrainer = CreateTrainer(
            builder,
            trainerIdValue: "dim_rank_04_41",
            trainerType: trainerType,
            trainerType2: trainerType);
        var eventTrainer = CreateTrainer(
            builder,
            trainerIdValue: "Ev_m03_0125",
            trainerType: trainerType,
            trainerType2: trainerType);
        var restaurantTrainer = CreateTrainer(
            builder,
            trainerIdValue: "Ev_sys_rest1_01",
            trainerType: trainerType,
            trainerType2: trainerType);
        var subquestTrainer = CreateTrainer(
            builder,
            trainerIdValue: "Ev_sub_010_030",
            trainerType: trainerType,
            trainerType2: trainerType);
        var gwynnTrainer = CreateTrainer(
            builder,
            trainerIdValue: "gwynn_of",
            trainerType: trainerType,
            trainerType2: trainerType);
        var prefixFirstTrainer = CreateTrainer(
            builder,
            trainerIdValue: "prefix_first",
            trainerType: trainerType,
            trainerType2: trainerType);
        var strongestTrainer = CreateTrainer(
            builder,
            trainerIdValue: "za_inf_strongest_04",
            trainerType: trainerType,
            trainerType2: trainerType);
        var rankInfinityTrainer = CreateTrainer(
            builder,
            trainerIdValue: "za_rank_inf1_01",
            trainerType: trainerType,
            trainerType2: trainerType);
        var vector = ZaTrainerTable.CreateValueVector(
            builder,
            [dimensionTrainer, secondDimensionTrainer, eventTrainer, restaurantTrainer, subquestTrainer, gwynnTrainer, prefixFirstTrainer, strongestTrainer, rankInfinityTrainer]);
        var root = ZaTrainerTable.Create(builder, vector);
        ZaTrainerTable.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<ZaTrainerRow> CreateTrainer(
        FlatBufferBuilder builder,
        bool signedDefaults = false,
        string trainerIdValue = "tr_battle_main_001",
        ulong trainerType = 1,
        ulong trainerType2 = 0,
        ushort speciesId = 1,
        int level = 12)
    {
        var trainerId = builder.CreateString(trainerIdValue);
        var pokemon = CreateTrainerPokemon(builder, signedDefaults, speciesId, level);

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

    private static Offset<ZaTrainerPokemon> CreateTrainerPokemon(
        FlatBufferBuilder builder,
        bool signedDefaults = false,
        ushort speciesId = 1,
        int level = 12)
    {
        var move1 = ZaTrainerMove.Create(builder, 33);
        var move2 = ZaTrainerMove.Create(builder, 45);
        var ivs = signedDefaults
            ? ZaTrainerStats.Create(builder, hp: -1, atk: -1, def: -1, spAtk: -1, spDef: -1, agi: -1)
            : ZaTrainerStats.Create(builder, hp: 31, atk: 31, def: 31, spAtk: 31, spDef: 31, agi: 31);
        var evs = ZaTrainerStats.Create(builder, hp: 4, atk: 0, def: 0, spAtk: 0, spDef: 0, agi: 0);

        return ZaTrainerPokemon.Create(
            builder,
            speciesId: speciesId,
            formId: 0,
            sex: signedDefaults ? -1 : 1,
            item: 4,
            level: level,
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

    private static byte[] CreateShopLineupArrayWithItems(params uint[] itemIds)
    {
        var builder = new FlatBufferBuilder(2048);
        var inventory = itemIds
            .Select((itemId, index) => CreateShopInventory(
                builder,
                itemId,
                checked((uint)(index + 1)),
                CreateForceShopCondition(builder)))
            .ToArray();
        var lineup = CreateShopLineup(builder, "a01_friendlyshop_01_lineup1", inventory);
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
        bool canUseInBattle = false,
        bool workEvolutional = false,
        int mintNature = -1)
    {
        var internalNameOffset = builder.CreateString(internalName);
        var iconNameOffset = builder.CreateString(iconName);
        ZaItemData.StartZaItemData(builder);
        ZaItemData.AddCanUseInBattle(builder, canUseInBattle);
        ZaItemData.AddWorkEvolutional(builder, workEvolutional);
        ZaItemData.AddMintNature(builder, mintNature);
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
        byte type = 0,
        byte category = 1,
        bool protect = true,
        bool mirror = true,
        bool metronome = true,
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
                stat1Stage,
                stat1Chance,
                stat2,
                stat2Stage,
                stat2Chance,
                stat3,
                stat3Stage,
                stat3Chance));
        ZaMoveData.AddRawTarget(builder, 3);
        ZaMoveData.AddInflict(
            builder,
            ZaMoveInflict.CreateZaMoveInflict(builder, Condition: 0, Chance: 0, TurnMode: 0, TurnMin: 0, TurnMax: 0));
        ZaMoveData.AddPp(builder, pp);
        ZaMoveData.AddAccuracy(builder, 100);
        ZaMoveData.AddPower(builder, power);
        ZaMoveData.AddCategory(builder, category);
        ZaMoveData.AddType(builder, type);
        ZaMoveData.AddCanUseMove(builder, true);
        ZaMoveData.AddMoveId(builder, moveId);
        ZaMoveData.AddFlagMetronome(builder, metronome);
        ZaMoveData.AddFlagMirror(builder, mirror);
        ZaMoveData.AddFlagProtect(builder, protect);
        ZaMoveData.AddFlagMakesContact(builder, makesContact);
        return ZaMoveData.EndZaMoveData(builder);
    }

    private static Offset<ZaPersonal> CreatePersonal(
        FlatBufferBuilder builder,
        ushort species,
        bool present,
        byte hp,
        ushort zaDexOrder,
        ushort evolutionCondition = 4,
        ushort evolutionParameter = 0,
        ushort evolutionSpecies = 1,
        byte evolutionForm = 0,
        ushort evolutionLevel = 16,
        bool legacyByteDexOrderLayout = false,
        bool includeType1 = true,
        bool includeDexOrder = true,
        uint speciesReserved3 = 0)
    {
        ZaPersonal.StartEvolutionsVector(builder, species == 0 ? 0 : 1);
        if (species != 0)
        {
            ZaEvolutionData.Create(
                builder,
                level: evolutionLevel,
                condition: evolutionCondition,
                parameter: evolutionParameter,
                reserved3: 0,
                reserved4: 0,
                reserved5: 0,
                species: evolutionSpecies,
                form: evolutionForm);
        }

        var evolutions = builder.EndVector();
        var tmMoves = ZaPersonal.CreateUshortVector(builder, species == 0 ? [] : [(ushort)45, 0]);
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
        if (includeType1)
        {
            ZaPersonal.AddType1(builder, 11);
        }

        if (includeDexOrder && legacyByteDexOrderLayout)
        {
            builder.AddByte(2, unchecked((byte)zaDexOrder), 0);
        }
        else if (includeDexOrder)
        {
            ZaPersonal.AddZADexOrder(builder, zaDexOrder);
        }

        ZaPersonal.AddIsPresent(builder, present);
        ZaPersonal.AddSpecies(
            builder,
            CreateZaSpeciesInfo(
                builder,
                species,
                speciesReserved3,
                legacyByteDexOrderLayout));
        return ZaPersonal.End(builder);
    }

    private static Offset<ZaSpeciesInfo> CreateZaSpeciesInfo(
        FlatBufferBuilder builder,
        ushort species,
        uint reserved3,
        bool legacyLayout)
    {
        if (!legacyLayout)
        {
            return ZaSpeciesInfo.Create(
                builder,
                species,
                form: 0,
                model: species,
                color: 3,
                bodyType: 1,
                height: 7,
                weight: 69,
                reserved: 0,
                reserved1: 0,
                reserved2: 0,
                reserved3: reserved3);
        }

        builder.Prep(2, 16);
        builder.Pad(1);
        builder.PutByte(0);
        builder.PutByte(0);
        builder.PutByte(0);
        builder.PutUshort(69);
        builder.PutUshort(7);
        builder.PutByte(1);
        builder.PutByte(3);
        builder.PutUshort(species);
        builder.PutUshort(0);
        builder.PutUshort(species);
        return new Offset<ZaSpeciesInfo>(builder.Offset);
    }

    private static int FindZaPersonalTableOffset(byte[] data, int personalId)
    {
        var rootOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, sizeof(int)));
        var entryVectorOffset = ReadFlatBufferVectorOffset(data, rootOffset, fieldIndex: 0);
        var personalOffsetLocation = entryVectorOffset + sizeof(int) + personalId * sizeof(int);
        return personalOffsetLocation
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(personalOffsetLocation, sizeof(uint))));
    }

    private static int FindZaPersonalStructVectorElementOffset(
        byte[] data,
        int personalId,
        int personalFieldIndex,
        int slot,
        int structSize)
    {
        var rootOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, sizeof(int)));
        var entryVectorOffset = ReadFlatBufferVectorOffset(data, rootOffset, fieldIndex: 0);
        var personalOffsetLocation = entryVectorOffset + sizeof(int) + personalId * sizeof(int);
        var personalOffset = personalOffsetLocation + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(personalOffsetLocation, sizeof(uint))));
        var vectorOffset = ReadFlatBufferVectorOffset(data, personalOffset, personalFieldIndex);
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(vectorOffset, sizeof(int)));
        Assert.InRange(slot, 0, length - 1);
        return vectorOffset + sizeof(int) + slot * structSize;
    }

    private static int ReadFlatBufferVectorOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var fieldLocation = ReadFlatBufferTableFieldLocation(data, tableOffset, fieldIndex);
        return fieldLocation + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(fieldLocation, sizeof(uint))));
    }

    private static int ReadFlatBufferTableFieldLocation(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldOffsetLocation = vtableOffset + sizeof(ushort) * 2 + fieldIndex * sizeof(ushort);
        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fieldOffsetLocation, sizeof(ushort)));
        Assert.NotEqual(0, fieldOffset);
        return tableOffset + fieldOffset;
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
        return ReadItem(File.ReadAllBytes(outputPath), itemId);
    }

    private static ZaItemData ReadItem(byte[] bytes, int itemId)
    {
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(bytes));
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

    private static void AssertItemDataEqual(
        ZaItemData expected,
        ZaItemData actual,
        params string[] excludedProperties)
    {
        var excluded = excludedProperties.ToHashSet(StringComparer.Ordinal);
        foreach (var property in typeof(ZaItemData).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name == nameof(ZaItemData.ByteBuffer) || excluded.Contains(property.Name))
            {
                continue;
            }

            var expectedValue = property.GetValue(expected);
            var actualValue = property.GetValue(actual);
            Assert.True(
                Equals(expectedValue, actualValue),
                $"Z-A item field {property.Name} changed unexpectedly. "
                + $"Expected {expectedValue ?? "null"}, actual {actualValue ?? "null"}.");
        }
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

    private static ZaEncounterDataRow ReadEncountData(TemporaryBridgeProject temp, string id)
    {
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            ZaDataPaths.EncountDataArray.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(outputPath));
        var table = ZaEncounterDataDbArray.GetRootAsZaEncounterDataDbArray(
            new ByteBuffer(File.ReadAllBytes(outputPath)));
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

        throw new InvalidOperationException($"EncountData row {id} was not written.");
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
