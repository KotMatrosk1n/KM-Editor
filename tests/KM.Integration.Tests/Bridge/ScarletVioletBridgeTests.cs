// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.Items;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.SV;
using KM.Integration.Tests.Tools;
using KM.Tools.Bridge;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class ScarletVioletBridgeTests
{
    private const ulong ScarletTitleId = 0x0100A3D008C5C000;
    private const ulong VioletTitleId = 0x01008F6008C5E000;

    public static IEnumerable<object[]> ScarletVioletGames()
    {
        yield return [ProjectGameDto.Scarlet, ScarletTitleId];
        yield return [ProjectGameDto.Violet, VioletTitleId];
    }

    [Theory]
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletProjectRoutesHiddenBridgeEditorsAndAppliesLooseOutputs(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

        var itemsSession = UpdateItem(dispatcher, paths, itemId: 1, field: "buyPrice", value: "777");
        Apply(dispatcher, paths, itemsSession);
        Assert.Equal(777, ReadItemPrice(temp, itemId: 1));

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

        var encountersSession = UpdateEncounter(dispatcher, paths, tableId: "test-location:test-area", slot: 0, field: "levelMin", value: "9");
        Apply(dispatcher, paths, encountersSession);
        Assert.Equal(9, ReadEncounterMinLevel(temp, index: 0));
        AssertDescriptorMarksLayeredOutputs(temp);
    }

    [Theory]
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletProjectExposesOnlyTheModMergerWorkflow(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };

        var response = Dispatch<ListWorkflowsResponse>(
            new ProjectBridgeDispatcher(),
            KmCommandNames.ListWorkflows,
            new ListWorkflowsRequest(paths),
            "request-sv-workflows");

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        var workflow = Assert.Single(response.Payload.Workflows);
        Assert.Equal("modMerger", workflow.Id);
        Assert.Equal("S/V Mod Merger", workflow.Label);
    }

    private static TemporaryBridgeProject CreateScarletVioletProject(ulong titleId)
    {
        var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "arc/data.trpfd",
            CreateTrinityDescriptor(
                [
                    SvDataPaths.ItemDataArray,
                    SvDataPaths.PersonalArray,
                    SvDataPaths.TrainerDataArray,
                    SvDataPaths.WildEncounterArray,
                ]));
        temp.WriteBaseRomFsFile("arc/data.trpfs", "storage");
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(titleId));
        return temp;
    }

    private static void WriteScarletFixtures(TemporaryBridgeProject temp)
    {
        WriteSvOutput(temp, SvDataPaths.ItemDataArray, CreateItemDataArray());
        WriteSvOutput(temp, SvDataPaths.PersonalArray, CreatePersonalArray());
        WriteSvOutput(temp, SvDataPaths.TrainerDataArray, CreateTrainerDataArray());
        WriteSvOutput(temp, SvDataPaths.WildEncounterArray, CreateEncounterArray());
    }

    private static EditSessionDto UpdateItem(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int itemId,
        string field,
        string value)
    {
        var response = Dispatch<UpdateItemFieldResponse>(
            dispatcher,
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(paths, Session: null, itemId, field, value),
            "request-sv-item-update");

        AssertSuccess(response);
        Assert.Equal(value, Assert.Single(response.Payload!.Session.PendingEdits).NewValue);
        return response.Payload.Session;
    }

    private static EditSessionDto UpdatePokemonField(
        ProjectBridgeDispatcher dispatcher,
        ProjectPathsDto paths,
        int personalId,
        string field,
        string value)
    {
        var response = Dispatch<UpdatePokemonFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePokemonField,
            new UpdatePokemonFieldRequest(paths, Session: null, personalId, field, value),
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
            SvDataPaths.PersonalArray,
            SvDataPaths.TrainerDataArray,
            SvDataPaths.WildEncounterArray,
        })
        {
            Assert.DoesNotContain(KM.Formats.SV.SvTrinityPathHasher.HashPath(path), activeHashes);
        }
    }

    private static int ReadItemPrice(TemporaryBridgeProject temp, int itemId)
    {
        var table = global::ItemDataArray.GetRootAsItemDataArray(new ByteBuffer(ReadSvOutput(temp, SvDataPaths.ItemDataArray)));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is not null && item.Value.Id == itemId)
            {
                return item.Value.Price;
            }
        }

        throw new InvalidDataException($"Item {itemId} was not written.");
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
        var table = global::trainer.TrdataMainArray.GetRootAsTrdataMainArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.TrainerDataArray)));
        var trainer = table.Values(trainerId);
        Assert.NotNull(trainer);
        var pokemon = slot switch
        {
            0 => trainer.Value.Poke1,
            1 => trainer.Value.Poke2,
            2 => trainer.Value.Poke3,
            3 => trainer.Value.Poke4,
            4 => trainer.Value.Poke5,
            5 => trainer.Value.Poke6,
            _ => null,
        };
        Assert.NotNull(pokemon);
        return pokemon.Value.Level;
    }

    private static int ReadEncounterMinLevel(TemporaryBridgeProject temp, int index)
    {
        var table = global::EncountPokeDataArray.GetRootAsEncountPokeDataArray(
            new ByteBuffer(ReadSvOutput(temp, SvDataPaths.WildEncounterArray)));
        var encounter = table.Values(index);
        Assert.NotNull(encounter);
        return encounter.Value.Minlevel;
    }

    private static byte[] CreateItemDataArray()
    {
        var builder = new FlatBufferBuilder(1024);
        var icon = builder.CreateString("item_0001");
        var item = global::ItemData.CreateItemData(
            builder,
            Id: 1,
            IconNameOffset: icon,
            Price: 100,
            BP: 2,
            ThrowPower: 10,
            SortNum: 1,
            GroupID: 1,
            SetToPoke: true);
        var vector = global::ItemDataArray.CreateValuesVector(builder, [item]);
        var root = global::ItemDataArray.CreateItemDataArray(builder, vector);
        global::ItemDataArray.FinishItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreatePersonalArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var empty = CreatePersonal(builder, species: 0, hp: 0, level: 0, evolutionLevel: 0);
        var bulbasaur = CreatePersonal(builder, species: 1, hp: 45, level: 1, evolutionLevel: 16);
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
        ushort evolutionLevel)
    {
        var tmMoves = global::personal.CreateTmMovesVector(builder, species == 0 ? [] : [(ushort)33]);
        var eggMoves = global::personal.CreateEggMovesVector(builder, []);
        var reminderMoves = global::personal.CreateReminderMovesVector(builder, []);

        global::personal.StartLevelupMovesVector(builder, species == 0 ? 0 : 1);
        if (species != 0)
        {
            global::levelup_move_data.Createlevelup_move_data(builder, Move: 33, Level: level);
        }

        var levelupMoves = builder.EndVector();

        global::personal.StartEvolutionsVector(builder, species == 0 ? 0 : 1);
        if (species != 0)
        {
            global::evo_data.Createevo_data(
                builder,
                Level: evolutionLevel,
                Condition: 4,
                Parameter: 0,
                Reserved3: 0,
                Reserved4: 0,
                Reserved5: 0,
                Species: 2,
                Form: 0);
        }

        var evolutions = builder.EndVector();

        global::personal.Startpersonal(builder);
        global::personal.AddLevelupMoves(builder, levelupMoves);
        global::personal.AddReminderMoves(builder, reminderMoves);
        global::personal.AddEggMoves(builder, eggMoves);
        global::personal.AddTmMoves(builder, tmMoves);
        global::personal.AddEvolutions(builder, evolutions);
        global::personal.AddBaseStats(builder, global::stat_info.Createstat_info(builder, hp, 49, 49, 65, 65, 45));
        global::personal.AddEvYield(builder, global::stat_info.Createstat_info(builder, 0, 0, 0, 1, 0, 0));
        global::personal.AddEggHatch(builder, global::egg_hatch_info.Createegg_hatch_info(builder, species, 0, 0, 0));
        global::personal.AddGender(builder, global::gender_info.Creategender_info(builder, 0, 31));
        global::personal.AddCatchRate(builder, 45);
        global::personal.AddXpGrowth(builder, 3);
        global::personal.AddAbilityHidden(builder, 34);
        global::personal.AddAbility2(builder, 65);
        global::personal.AddAbility1(builder, 65);
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
            talentValueOffset: ivs,
            effortValueOffset: evs);
        var trainerId = builder.CreateString("tr_0000");
        var trainerName = builder.CreateString("Test Trainer");
        var trainerType = builder.CreateString("Pokemon Trainer");
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

    private static byte[] CreateEncounterArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var area = builder.CreateString("test-area");
        var location = builder.CreateString("test-location");
        var time = global::TimeTable.CreateTimeTable(builder, morning: true, noon: true, evening: true, night: true);
        var version = global::VersionTable.CreateVersionTable(builder, A: true, B: true);

        global::EncountPokeData.StartEncountPokeData(builder);
        global::EncountPokeData.AddVersiontable(builder, version);
        global::EncountPokeData.AddTimetable(builder, time);
        global::EncountPokeData.AddLocationName(builder, location);
        global::EncountPokeData.AddArea(builder, area);
        global::EncountPokeData.AddLotvalue(builder, 40);
        global::EncountPokeData.AddMaxlevel(builder, 12);
        global::EncountPokeData.AddMinlevel(builder, 5);
        global::EncountPokeData.AddDevid(builder, (global::pml.common.DevID)1);
        global::EncountPokeData.AddEnabletable(builder, global::EnableTable.CreateEnableTable(builder, true, false, false, false, false));
        global::EncountPokeData.AddBringItem(builder, global::BringItem.CreateBringItem(builder, (global::ItemID)0, BringRate: 0));
        var encounter = global::EncountPokeData.EndEncountPokeData(builder);

        var vector = global::EncountPokeDataArray.CreateValuesVector(builder, [encounter]);
        var root = global::EncountPokeDataArray.CreateEncountPokeDataArray(builder, vector);
        global::EncountPokeDataArray.FinishEncountPokeDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static void WriteSvOutput(TemporaryBridgeProject temp, string relativePath, byte[] contents)
    {
        temp.WriteOutputFile(Path.Combine("romfs", relativePath.Replace('/', Path.DirectorySeparatorChar)), contents);
    }

    private static byte[] ReadSvOutput(TemporaryBridgeProject temp, string relativePath)
    {
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

    private static void AssertSuccess<TPayload>(BridgeResponse<TPayload> response)
    {
        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
    }
}
