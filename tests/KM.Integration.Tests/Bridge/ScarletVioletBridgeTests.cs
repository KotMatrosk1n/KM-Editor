// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.Gifts;
using KM.Api.HyperspaceBypass;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.Workflows;
using KM.SV.Data;
using KM.SV.Trainers;
using KM.Integration.Tests.Tools;
using KM.Formats.SV.Placement;
using KM.Formats.SwSh;
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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletChangePlansCanOutputForTrinityModManager(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();
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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletGiftPokemonLoadsStagesAndOutputsForTrinityModManager(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletGiftPokemonPendingSpeciesEditsRefreshPreviewLabelsAndDerivedFields(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletTradePokemonLoadsStagesAndOutputsForTrinityModManager(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
        Assert.Equal("Normal", trade.TeraTypeLabel);
        Assert.Equal("Fixed value", trade.ScaleModeLabel);
        Assert.Equal(123, trade.ScaleValue);
        Assert.Equal(33, trade.Moves[0].MoveId);
        Assert.Equal("Tackle", trade.Moves[0].Move);
        Assert.Contains(loaded.Payload.Workflow.EditableFields, field => field.Field == "move1Id");
        Assert.Contains(loaded.Payload.Workflow.EditableFields, field => field.Field == "requiredSpecies");
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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletTradePokemonPendingSpeciesEditsRefreshPreviewLabelsAndDerivedFields(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletHyperspaceBypassStagesStandaloneMainAndRejectsTrinityOutput(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseExeFsFile("main", SvHyperspaceBypassBridgeFixtures.CreateCompatibleMain(game));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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

        var trinityPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, stage.Payload.Session, ChangePlanOutputModeDto.TrinityModManager),
            "request-sv-hyperspace-trinity-plan");
        AssertSuccess(trinityPlan);
        Assert.False(trinityPlan.Payload!.ChangePlan.CanApply);
        Assert.Empty(trinityPlan.Payload.ChangePlan.Writes);
        Assert.Contains(
            trinityPlan.Payload.ChangePlan.Diagnostics,
            diagnostic => diagnostic.Message.Contains("outside Trinity Mod Manager RomFS output", StringComparison.Ordinal));

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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletNormalEditorsCanShareOnePendingEditSession(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletProjectExposesBasicEditorWorkflows(
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
        Assert.Equal(
            ["items", "moves", "pokemon", "trainers", "encounters", "giftPokemon", "tradePokemon", "placement", "typeChart", "hyperspaceBypass", "modMerger"],
            response.Payload.Workflows.Select(workflow => workflow.Id).ToArray());
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Pokemon Data");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Items");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Moves");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Trainers");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Wild Encounters");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Gift Pokemon");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Trade Pokemon");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Placement");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Type Chart");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "Hyperspace Bypass");
        Assert.Contains(response.Payload.Workflows, workflow => workflow.Label == "S/V Mod Merger");
    }

    [Theory]
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletProjectLoadsEnglishMessageLabels(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
        Assert.True(tm001.Metadata.CanUseOnPokemon);
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
            option => option.Value == (int)global::FieldPocket.FPOCKET_BALL && option.Label == "Ball");
        Assert.Contains(
            itemFields.Single(field => field.Field == "fieldUseType").Options,
            option => option.Value == (int)global::FieldFunctionType.FIELDFUNC_WAZA && option.Label == "Waza");
        Assert.Contains(
            itemFields.Single(field => field.Field == "itemType").Options,
            option => option.Value == (int)global::ItemType.ITEMTYPE_BALL && option.Label == "Ball");
        Assert.Contains(
            itemFields.Single(field => field.Field == "groupType").Options,
            option => option.Value == (int)global::ItemGroup.ITEMGROUP_BALL && option.Label == "Ball");
        Assert.Contains(
            itemFields.Single(field => field.Field == "machineMoveId").Options,
            option => option.Value == 36 && option.Label.Contains("Take Down", StringComparison.Ordinal));
        Assert.Contains(
            itemFields.Single(field => field.Field == "groupIndex").Options,
            option => option.Value == 1 && option.Label.Contains("TM001 Take Down", StringComparison.Ordinal));
        Assert.Contains(
            itemFields.Single(field => field.Field == "groupIndex").Options,
            option => option.Value == 2 && option.Label.Contains("TM002 Growl", StringComparison.Ordinal));

        var trainers = Dispatch<LoadTrainersWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTrainersWorkflow,
            new LoadTrainersWorkflowRequest(paths),
            "request-sv-trainer-labels");
        AssertSuccess(trainers);
        var trainer = Assert.Single(trainers.Payload!.Workflow.Trainers);
        Assert.Equal("Test Trainer", trainer.Name);
        Assert.Equal("Pokemon Trainer", trainer.TrainerClass);
        var trainerPokemon = Assert.Single(trainer.Team);
        Assert.Equal("Bulbasaur", trainerPokemon.Species);
        Assert.Equal(new[] { "Tackle", "None", "None", "None" }, trainerPokemon.Moves);
        Assert.Equal("Random", trainerPokemon.GenderLabel);
        Assert.Equal("Random 1/2", trainerPokemon.AbilityLabel);
        Assert.Equal("Default (game behavior)", trainerPokemon.NatureLabel);
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
        Assert.Contains(
            placement.Payload!.Workflow.EditableFields.Single(field => field.Field == "fixed.shiny").Options,
            option => option.Value == (int)global::RareType.NO_RARE && option.Label == "1 Not Shiny");
        Assert.Contains(
            placement.Payload.Workflow.EditableFields.Single(field => field.Field == "fixed.shiny").Options,
            option => option.Value == (int)global::RareType.RARE && option.Label == "2 Shiny");
        Assert.Equal(
            "Emerge value 1",
            placement.Payload.Workflow.EditableFields.Single(field => field.Field == "hidden.item1.chance").Label);
        Assert.Equal(
            int.MaxValue,
            placement.Payload.Workflow.EditableFields.Single(field => field.Field == "hidden.item1.chance").MaximumValue);

        var fixedSymbol = placement.Payload.Workflow.Objects.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.Equal("Bulbasaur", fixedSymbol.ItemName);
        Assert.Equal("1 Bulbasaur", fixedSymbol.Fields!.Single(field => field.Field == "fixed.speciesId").DisplayValue);
        Assert.Equal("Not Shiny", fixedSymbol.Fields!.Single(field => field.Field == "fixed.shiny").DisplayValue);
        Assert.Equal("33 Tackle", fixedSymbol.Fields!.Single(field => field.Field == "fixed.move1").DisplayValue);
        Assert.True(fixedSymbol.Fields!.Single(field => field.Field == "fixed.alcremieSweet").IsReadOnly);
        var fixedAbility = fixedSymbol.Fields!.Single(field => field.Field == "fixed.abilityMode");
        Assert.Equal("Overgrow (Ability 1)", fixedAbility.DisplayValue);
        Assert.Contains(
            fixedAbility.Options!,
            option => option.Value == (int)global::TokuseiType.SET_1 && option.Label == "2 Overgrow (Ability 1)");
        Assert.Contains(
            fixedAbility.Options!,
            option => option.Value == (int)global::TokuseiType.SET_3 && option.Label == "4 Chlorophyll (Hidden Ability)");

        var coinSymbol = placement.Payload.Workflow.Objects.Single(entry => entry.CategoryId == "coinSymbols");
        Assert.Equal("Bulbasaur", coinSymbol.ItemName);
        Assert.Equal("1 Bulbasaur", coinSymbol.Fields!.Single(field => field.Field == "coin.speciesId").DisplayValue);
        Assert.Equal("Shiny", coinSymbol.Fields!.Single(field => field.Field == "coin.shiny").DisplayValue);
        Assert.Equal("33 Tackle", coinSymbol.Fields!.Single(field => field.Field == "coin.move1").DisplayValue);
        var coinAbility = coinSymbol.Fields!.Single(field => field.Field == "coin.abilityMode");
        Assert.Equal("Chlorophyll (Hidden Ability)", coinAbility.DisplayValue);
        Assert.Contains(
            coinAbility.Options!,
            option => option.Value == (int)global::TokuseiType.SET_3 && option.Label == "4 Chlorophyll (Hidden Ability)");
    }

    [Theory]
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletPokemonLearnsetsDisplayEvolutionSentinelAndPreserveRawLevel(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        WriteSvOutput(temp, SvDataPaths.PersonalArray, CreatePersonalArrayWithLevelupMoves((33, 253), (45, 1)));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletPlacementAlcremieSweetOnlyEditsAlcremieFixedSymbols(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

        var placement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-alcremie-sweet-load");
        AssertSuccess(placement);
        var fixedSymbol = placement.Payload!.Workflow.Objects.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.True(fixedSymbol.Fields!.Single(field => field.Field == "fixed.alcremieSweet").IsReadOnly);

        var stagedSpecies = Dispatch<UpdatePlacementObjectFieldResponse>(
            dispatcher,
            KmCommandNames.UpdatePlacementObjectField,
            new UpdatePlacementObjectFieldRequest(
                paths,
                Session: null,
                fixedSymbol.ObjectId,
                Field: "fixed.speciesId",
                Value: ((int)global::pml.common.DevID.DEV_MAHOIPPU).ToString(CultureInfo.InvariantCulture)),
            "request-sv-placement-alcremie-sweet-unlock");
        AssertSuccess(stagedSpecies);
        var stagedFixedSymbol = stagedSpecies.Payload!.Workflow.Objects.Single(entry => entry.ObjectId == fixedSymbol.ObjectId);
        Assert.False(stagedFixedSymbol.Fields!.Single(field => field.Field == "fixed.alcremieSweet").IsReadOnly);

        WriteSvOutput(
            temp,
            SvDataPaths.FixedSymbolTableArray,
            CreateFixedSymbolTableArray(global::pml.common.DevID.DEV_MAHOIPPU));
        var alcremiePlacement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-alcremie-sweet-alcremie-load");
        AssertSuccess(alcremiePlacement);
        var alcremieFixedSymbol = alcremiePlacement.Payload!.Workflow.Objects.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.False(alcremieFixedSymbol.Fields!.Single(field => field.Field == "fixed.alcremieSweet").IsReadOnly);
    }

    [Theory]
    [MemberData(nameof(ScarletVioletGames))]
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
        var dispatcher = new ProjectBridgeDispatcher();

        var placement = Dispatch<LoadPlacementWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadPlacementWorkflow,
            new LoadPlacementWorkflowRequest(paths),
            "request-sv-placement-default-moves");
        AssertSuccess(placement);
        var fixedSymbol = placement.Payload!.Workflow.Objects.Single(entry => entry.CategoryId == "fixedSymbols");
        Assert.Equal("33 Tackle", fixedSymbol.Fields!.Single(field => field.Field == "fixed.move1").DisplayValue);
        Assert.Equal("45 Growl", fixedSymbol.Fields!.Single(field => field.Field == "fixed.move2").DisplayValue);
        Assert.Equal("36 Take Down", fixedSymbol.Fields!.Single(field => field.Field == "fixed.move3").DisplayValue);
        Assert.Equal("None", fixedSymbol.Fields!.Single(field => field.Field == "fixed.move4").DisplayValue);

        var session = UpdatePlacement(dispatcher, paths, fixedSymbol.ObjectId, field: "fixed.move1", value: "349");
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
    [MemberData(nameof(ScarletVioletGames))]
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
        var dispatcher = new ProjectBridgeDispatcher();

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
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletPokemonBaseExperienceAndYieldButtonsUsePersonalTableSemantics(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        WriteScarletFixtures(temp);
        temp.WriteBaseRomFsFile(SvDataPaths.PersonalArray, CreatePersonalArray());
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

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
            new ProjectBridgeDispatcher(),
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
        var dispatcher = new ProjectBridgeDispatcher();

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
                    SvDataPaths.MoveDataArray,
                    SvDataPaths.PersonalArray,
                    SvDataPaths.TrainerDataArray,
                    SvDataPaths.WildEncounterArray,
                    SvDataPaths.EventAddPokemonArray,
                    SvDataPaths.EventTradeListArray,
                    SvDataPaths.EventTradePokemonArray,
                ]));
        temp.WriteBaseRomFsFile("arc/data.trpfs", "storage");
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(titleId));
        return temp;
    }

    private static void WriteScarletFixtures(TemporaryBridgeProject temp)
    {
        WriteSvOutput(temp, SvDataPaths.ItemDataArray, CreateItemDataArray());
        WriteSvOutput(temp, SvDataPaths.MoveDataArray, CreateMoveDataArray());
        WriteSvOutput(temp, SvDataPaths.PersonalArray, CreatePersonalArray());
        WriteSvOutput(temp, SvDataPaths.TrainerDataArray, CreateTrainerDataArray());
        WriteSvOutput(temp, SvDataPaths.WildEncounterArray, CreateEncounterArray());
        WriteSvOutput(temp, SvDataPaths.EventAddPokemonArray, CreateEventAddPokemonArray());
        WriteSvOutput(temp, SvDataPaths.EventTradeListArray, CreateEventTradeListArray());
        WriteSvOutput(temp, SvDataPaths.EventTradePokemonArray, CreateEventTradePokemonArray());
        WriteSvOutput(temp, SvDataPaths.FixedSymbolTableArray, CreateFixedSymbolTableArray());
        WriteSvOutput(temp, SvDataPaths.EventBattlePokemonArray, CreateEventBattlePokemonArray());
        WriteSvOutput(temp, SvDataPaths.HiddenItemDataTableArray, CreateHiddenItemDataTableArray());
        WriteSvOutput(temp, SvDataPaths.RummagingItemDataTableArray, CreateRummagingItemDataTableArray());
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishPokemonNames,
            CreateTextTable(5, (1, "Bulbasaur"), (2, "Ivysaur"), (4, "Charmander")));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishItemNames,
            CreateTextTable(6, (1, "Master Ball"), (2, "TM001"), (3, "Legacy Move Record"), (4, "TM002"), (5, "TM100")));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishMoveNames,
            CreateTextTable(350, (33, "Tackle"), (36, "Take Down"), (45, "Growl"), (349, "Dragon Dance")));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishAbilityNames,
            CreateTextTable(95, (34, "Chlorophyll"), (65, "Overgrow"), (66, "Blaze"), (94, "Solar Power")));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishPlaceNames,
            CreateTextTable(2, (0, "South Province (Area Two)"), (1, "South Province (Area Four)")));
        temp.WriteBaseRomFsFile(
            SvDataPaths.EnglishPlaceNameKeys,
            CreateKeyTable("PLACENAME_a_w04_01", "PLACENAME_a_w05_01"));
        temp.WriteBaseRomFsFile(SvDataPaths.EnglishTrainerNames, CreateTextTable(1, (0, "Test Trainer")));
        temp.WriteBaseRomFsFile(SvDataPaths.EnglishTrainerNameKeys, CreateKeyTable("TRNAME_TEST"));
        temp.WriteBaseRomFsFile(SvDataPaths.EnglishTrainerTypes, CreateTextTable(1, (0, "Pokemon Trainer")));
        temp.WriteBaseRomFsFile(SvDataPaths.EnglishTrainerTypeKeys, CreateKeyTable("MSG_TRTYPE_TEST"));
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
            DynamaxLevel: 0,
            CanGigantamax: false,
            stats,
            Shiny: false,
            CanDynamax: true,
            TeraType: (int)teraType,
            TeraTypeLabel: SvTrainersWorkflowService.FormatTeraType(teraType));
    }

    private static global::PokeDataBattle ReadTrainerPokemon(TemporaryBridgeProject temp, int trainerId, int slot)
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
        return pokemon.Value;
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

    private static byte[] CreateItemDataArray()
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
            GroupID: 1,
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
        var vector = global::ItemDataArray.CreateValuesVector(builder, [masterBall, tm001, legacyMoveItem, tm002, tm100]);
        var root = global::ItemDataArray.CreateItemDataArray(builder, vector);
        global::ItemDataArray.FinishItemDataArrayBuffer(builder, root);
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

    private static Offset<global::SvMoveData> CreateMove(
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
        global::SvMoveData.StartSvMoveData(builder);
        global::SvMoveData.AddStatChanges(
            builder,
            global::SvMoveStatChanges.CreateSvMoveStatChanges(
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
        global::SvMoveData.AddRawTarget(builder, 3);
        global::SvMoveData.AddInflict(
            builder,
            global::SvMoveInflict.CreateSvMoveInflict(builder, Condition: 0, Chance: 0, TurnMode: 0, TurnMin: 0, TurnMax: 0));
        global::SvMoveData.AddPp(builder, pp);
        global::SvMoveData.AddAccuracy(builder, 100);
        global::SvMoveData.AddPower(builder, power);
        global::SvMoveData.AddCategory(builder, category);
        global::SvMoveData.AddType(builder, 0);
        global::SvMoveData.AddCanUseMove(builder, true);
        global::SvMoveData.AddMoveId(builder, moveId);
        global::SvMoveData.AddFlagMakesContact(builder, makesContact);
        return global::SvMoveData.EndSvMoveData(builder);
    }

    private static byte[] CreatePersonalArray()
    {
        var builder = new FlatBufferBuilder(2048);
        var empty = CreatePersonal(builder, species: 0, hp: 0, level: 0, evolutionLevel: 0);
        var bulbasaur = CreatePersonal(builder, species: 1, hp: 45, level: 1, evolutionLevel: 16);
        var charmander = CreatePersonal(
            builder,
            species: 4,
            hp: 39,
            level: 1,
            evolutionLevel: 16,
            learnedMoves: [(Move: (ushort)33, Level: 1)],
            ability1: 66,
            ability2: 66,
            hiddenAbility: 94);
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
        ushort hiddenAbility = 34)
    {
        var tmMoves = global::personal.CreateTmMovesVector(builder, species == 0 ? [] : [(ushort)36]);
        var eggMoves = global::personal.CreateEggMovesVector(builder, []);
        var reminderMoves = global::personal.CreateReminderMovesVector(builder, []);

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
            (global::pml.common.DevID)2);
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

    private static void AssertSuccess<TPayload>(BridgeResponse<TPayload> response)
    {
        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
    }
}
