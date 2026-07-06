// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Gifts;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.StaticEncounters;
using KM.Api.Trades;
using KM.Api.Trainers;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class BridgeJsonTests
{
    [Fact]
    public void SerializesRequestEnvelopeWithCamelCaseNames()
    {
        var paths = new ProjectPathsDto(
            "base-romfs",
            "base-exefs",
            OutputRootPath: null,
            SaveFilePath: null,
            SelectedGame: ProjectGameDto.Shield);
        var request = new BridgeRequest<OpenProjectRequest>(
            KmCommandNames.OpenProject,
            new OpenProjectRequest(paths),
            RequestId: "request-1");

        var json = JsonSerializer.Serialize(request, BridgeJson.SerializerOptions);

        Assert.Contains("\"command\":\"project.open\"", json);
        Assert.Contains("\"requestId\":\"request-1\"", json);
        Assert.Contains("\"baseRomFsPath\":\"base-romfs\"", json);
        Assert.Contains("\"selectedGame\":\"shield\"", json);
        Assert.DoesNotContain("BaseRomFsPath", json);
    }

    [Fact]
    public void SerializesScarletProjectGameAsCamelCaseName()
    {
        var paths = new ProjectPathsDto(
            "base-romfs",
            "base-exefs",
            OutputRootPath: null,
            SaveFilePath: null,
            SelectedGame: ProjectGameDto.Scarlet);
        var request = new BridgeRequest<ValidateProjectRequest>(
            KmCommandNames.ValidateProject,
            new ValidateProjectRequest(paths),
            RequestId: "request-scarlet");

        var json = JsonSerializer.Serialize(request, BridgeJson.SerializerOptions);

        Assert.Contains("\"selectedGame\":\"scarlet\"", json);
    }

    [Fact]
    public void SerializesProjectGameTextLanguageAsCamelCaseName()
    {
        var paths = new ProjectPathsDto(
            "base-romfs",
            "base-exefs",
            OutputRootPath: null,
            SaveFilePath: null,
            ScarletVioletSupportFolderPath: null,
            SelectedGame: ProjectGameDto.Sword,
            GameTextLanguage: "es");
        var request = new BridgeRequest<ValidateProjectRequest>(
            KmCommandNames.ValidateProject,
            new ValidateProjectRequest(paths),
            RequestId: "request-language");

        var json = JsonSerializer.Serialize(request, BridgeJson.SerializerOptions);

        Assert.Contains("\"gameTextLanguage\":\"es\"", json);
    }

    [Fact]
    public void SerializesResponseEnvelopeWithStringDiagnostics()
    {
        var diagnostic = new ApiDiagnostic(ApiDiagnosticSeverity.Warning, "Project has missing optional output.");
        var error = new ApiError("project.invalidPaths", "Project paths are not valid.", [diagnostic]);
        var response = BridgeResponse<OpenProjectResponse>.Failure(error, requestId: "request-2");

        var json = JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);

        Assert.Contains("\"error\":", json);
        Assert.Contains("\"severity\":\"warning\"", json);
        Assert.Contains("\"requestId\":\"request-2\"", json);
        Assert.DoesNotContain("\"succeeded\"", json);
    }

    [Fact]
    public void SerializesProjectHealthStateAsString()
    {
        var health = new ProjectHealthDto(
            State: ProjectHealthStateDto.EditableReady,
            CanOpenReadOnlyWorkflows: true,
            CanOpenEditableWorkflows: true,
            Paths:
            [
                new ProjectPathValidationDto(
                    Role: ProjectPathRoleDto.BaseRomFs,
                    Path: "base-romfs",
                    Status: ProjectPathStatusDto.Valid,
                    IsRequired: true,
                    Diagnostics: []),
            ],
            FileGraph: new ProjectFileGraphSummaryDto(BaseFileCount: 1, LayeredFileCount: 0, OverrideCount: 0, LayeredOnlyCount: 0),
            Diagnostics: []);
        var response = BridgeResponse<OpenProjectResponse>.Success(
            new OpenProjectResponse(
                "project-1",
                health,
                new ProjectFileGraphDto(
                    Entries:
                    [
                        new ProjectFileGraphEntryDto(
                            RelativePath: "romfs/data/items.bin",
                            BaseFile: new ProjectFileReferenceDto(ProjectFileLayerDto.Base, "romfs/data/items.bin"),
                            LayeredFile: new ProjectFileReferenceDto(ProjectFileLayerDto.Layered, "romfs/data/items.bin"),
                            State: ProjectFileGraphEntryStateDto.LayeredOverride),
                    ],
                    Summary: new ProjectFileGraphSummaryDto(BaseFileCount: 1, LayeredFileCount: 1, OverrideCount: 1, LayeredOnlyCount: 0))),
            requestId: "request-3");

        var json = JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);

        Assert.Contains("\"state\":\"editableReady\"", json);
        Assert.Contains("\"role\":\"baseRomFs\"", json);
        Assert.Contains("\"status\":\"valid\"", json);
        Assert.Contains("\"layer\":\"base\"", json);
        Assert.Contains("\"state\":\"layeredOverride\"", json);
    }

    [Fact]
    public void OmitsNullSharedMechanicFieldsFromSerializedDtos()
    {
        var pokemonPersonalJson = JsonSerializer.Serialize(
            new PokemonPersonalDetailsDto(
                Type1: 0,
                Type2: 0,
                CatchRate: 45,
                EvolutionStage: 1,
                EVYieldHP: 0,
                EVYieldAttack: 0,
                EVYieldDefense: 0,
                EVYieldSpecialAttack: 0,
                EVYieldSpecialDefense: 0,
                EVYieldSpeed: 0,
                HeldItem1: 0,
                HeldItem2: 0,
                HeldItem3: 0,
                GenderRatio: 31,
                HatchCycles: 20,
                BaseFriendship: 70,
                ExpGrowth: 4,
                EggGroup1: 1,
                EggGroup2: 1,
                FormStatsIndex: 0,
                FormCount: 1,
                Color: 1,
                IsPresentInGame: true,
                HasSpriteForm: false,
                ModelId: 1,
                HatchedSpecies: 1,
                LocalFormIndex: 0,
                IsRegionalForm: false,
                CanNotDynamax: null,
                Form: 0),
            BridgeJson.SerializerOptions);
        var trainerPokemonJson = JsonSerializer.Serialize(
            new TrainerPokemonRecordDto(
                Slot: 1,
                SpeciesId: 1,
                Species: "Bulbasaur",
                Form: 0,
                Level: 5,
                HeldItemId: 0,
                HeldItem: null,
                MoveIds: [0, 0, 0, 0],
                Moves: ["None", "None", "None", "None"],
                Gender: 0,
                GenderLabel: "Random",
                Ability: 0,
                AbilityLabel: "Default",
                Nature: 0,
                NatureLabel: "Default",
                Evs: new TrainerPokemonStatsDto(0, 0, 0, 0, 0, 0),
                DynamaxLevel: null,
                CanGigantamax: null,
                Ivs: new TrainerPokemonStatsDto(0, 0, 0, 0, 0, 0),
                Shiny: false,
                CanDynamax: null,
                TeraType: null,
                TeraTypeLabel: null),
            BridgeJson.SerializerOptions);
        var giftJson = JsonSerializer.Serialize(
            new GiftPokemonRecordDto(
                GiftIndex: 1,
                Label: "Gift 1",
                SpeciesId: 1,
                Species: "Bulbasaur",
                Form: 0,
                Level: 5,
                IsEgg: false,
                HeldItemId: 0,
                HeldItem: null,
                BallItemId: 0,
                BallItem: "None",
                Ability: 0,
                AbilityLabel: "Default",
                Nature: 0,
                NatureLabel: "Default",
                Gender: 0,
                GenderLabel: "Random",
                ShinyLock: 0,
                ShinyLockLabel: "Default",
                DynamaxLevel: null,
                CanGigantamax: null,
                SpecialMoveId: 0,
                SpecialMove: null,
                Ivs: new GiftPokemonIvsDto(0, 0, 0, 0, 0, 0),
                FlawlessIvCount: null,
                IvSummary: "Random",
                Provenance: new GiftPokemonProvenanceDto("source.bin", ProjectFileLayerDto.Base, ProjectFileGraphEntryStateDto.BaseOnly)),
            BridgeJson.SerializerOptions);
        var tradeJson = JsonSerializer.Serialize(
            new TradePokemonRecordDto(
                TradeIndex: 1,
                Label: "Trade 1",
                SpeciesId: 1,
                Species: "Bulbasaur",
                Form: 0,
                Level: 5,
                HeldItemId: 0,
                HeldItem: null,
                BallItemId: 0,
                BallItem: "None",
                Ability: 0,
                AbilityLabel: "Default",
                Nature: 0,
                NatureLabel: "Default",
                Gender: 0,
                GenderLabel: "Random",
                ShinyLock: 0,
                ShinyLockLabel: "Default",
                DynamaxLevel: null,
                CanGigantamax: null,
                RequiredSpeciesId: 0,
                RequiredSpecies: "Handled by trade event",
                RequiredForm: 0,
                RequiredNature: 0,
                RequiredNatureLabel: "Default",
                UnknownRequirement: 0,
                TrainerId: 0,
                OtGender: 0,
                OtGenderLabel: "Default",
                MemoryCode: 0,
                MemoryTextVariable: 0,
                MemoryFeel: 0,
                MemoryIntensity: 0,
                Field03: 0,
                Hash0: "0x0",
                Hash1: "0x0",
                Hash2: "0x0",
                RelearnMoves: [],
                Ivs: new TradePokemonIvsDto(0, 0, 0, 0, 0, 0),
                FlawlessIvCount: null,
                IvSummary: "Random",
                Provenance: new TradePokemonProvenanceDto("source.bin", ProjectFileLayerDto.Base, ProjectFileGraphEntryStateDto.BaseOnly)),
            BridgeJson.SerializerOptions);
        var staticJson = JsonSerializer.Serialize(
            new StaticEncounterRecordDto(
                EncounterIndex: 1,
                Label: "Static 1",
                EncounterId: "static-1",
                SpeciesId: 1,
                Species: "Bulbasaur",
                Form: 0,
                Level: 5,
                HeldItemId: 0,
                HeldItem: null,
                Ability: 0,
                AbilityLabel: "Default",
                Nature: 0,
                NatureLabel: "Default",
                Gender: 0,
                GenderLabel: "Random",
                ShinyLock: 0,
                ShinyLockLabel: "Default",
                EncounterScenario: 0,
                EncounterScenarioLabel: "Default",
                DynamaxLevel: null,
                CanGigantamax: null,
                Evs: new StaticEncounterStatsDto(0, 0, 0, 0, 0, 0),
                Ivs: new StaticEncounterStatsDto(0, 0, 0, 0, 0, 0),
                FlawlessIvCount: null,
                IvSummary: "Random",
                Moves: [],
                Provenance: new StaticEncounterProvenanceDto("source.bin", ProjectFileLayerDto.Base, ProjectFileGraphEntryStateDto.BaseOnly)),
            BridgeJson.SerializerOptions);

        Assert.DoesNotContain("canNotDynamax", pokemonPersonalJson);
        Assert.DoesNotContain("dynamaxLevel", trainerPokemonJson);
        Assert.DoesNotContain("canGigantamax", trainerPokemonJson);
        Assert.DoesNotContain("canDynamax", trainerPokemonJson);
        Assert.DoesNotContain("teraType", trainerPokemonJson);
        Assert.DoesNotContain("dynamaxLevel", giftJson);
        Assert.DoesNotContain("canGigantamax", giftJson);
        Assert.DoesNotContain("teraType", giftJson);
        Assert.DoesNotContain("dynamaxLevel", tradeJson);
        Assert.DoesNotContain("canGigantamax", tradeJson);
        Assert.DoesNotContain("teraType", tradeJson);
        Assert.DoesNotContain("dynamaxLevel", staticJson);
        Assert.DoesNotContain("canGigantamax", staticJson);
    }
}
