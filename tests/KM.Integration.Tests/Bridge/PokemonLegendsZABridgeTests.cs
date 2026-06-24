// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
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
    public void PokemonLegendsZAProjectListsPokemonAndMovesWorkflows()
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
        Assert.Contains(workflows.Payload.Workflows, workflow => workflow.Id == "moves" && workflow.Label == "Moves");
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
