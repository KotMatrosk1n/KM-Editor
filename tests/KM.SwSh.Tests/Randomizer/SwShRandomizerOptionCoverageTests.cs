// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Formats.SwSh;
using KM.SwSh.Encounters;
using KM.SwSh.Gifts;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Randomizer;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Gifts;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using KM.SwSh.Tests.Raids;
using KM.SwSh.Tests.StaticEncounters;
using Xunit;

namespace KM.SwSh.Tests.Randomizer;

public sealed class SwShRandomizerOptionCoverageTests
{
    [Fact]
    public void PreviewCreatesEditsForEverySelectableRandomizerOption()
    {
        using var temp = CreateRandomizerProject();
        var service = new SwShRandomizerService();

        var preview = service.Preview(temp.Paths, CreateConfig(AllRandomizerOptions()));
        var edits = AllEdits(preview).ToArray();

        AssertNoErrors(preview);
        Assert.Contains(preview.Domains, domain => domain.Label == "Pokemon");
        Assert.Contains(preview.Domains, domain => domain.Label == "Wild Encounters");
        Assert.Contains(preview.Domains, domain => domain.Label == "Static Encounters");
        Assert.Contains(preview.Domains, domain => domain.Label == "Gift Encounters");
        Assert.Contains(preview.Domains, domain => domain.Label == "Raid Rewards");
        Assert.Contains(preview.Domains, domain => domain.Label == "Raid Bonus Rewards");

        AssertHasPokemonField(edits, SwShPokemonWorkflowService.HPField);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.AttackField);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.DefenseField);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.SpecialAttackField);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.SpecialDefenseField);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.SpeedField);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.Type1Field);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.Type2Field);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.Ability1Field);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.Ability2Field);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.HiddenAbilityField);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.HeldItem1Field);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.HeldItem2Field);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.HeldItem3Field);
        AssertHasPokemonField(edits, SwShPokemonWorkflowService.CatchRateField);
        Assert.Contains(edits, edit => edit.Domain == "workflow.pokemon" && edit.Field == "learnset:upsert:24");
        Assert.Contains(edits, edit => edit.Domain == "workflow.pokemon" && edit.Field.StartsWith("evolution:upsert:", StringComparison.Ordinal));
        Assert.Contains(edits, edit => IsCompatibilityField(edit, SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId));
        Assert.Contains(edits, edit => IsCompatibilityField(edit, SwShPokemonWorkflowService.TechnicalRecordCompatibilityGroupId));
        Assert.Contains(edits, edit => IsCompatibilityField(edit, SwShPokemonWorkflowService.TypeTutorCompatibilityGroupId));
        Assert.Contains(edits, edit => IsCompatibilityField(edit, SwShPokemonWorkflowService.ArmorTutorCompatibilityGroupId));

        Assert.All(
            edits.Where(edit => edit.Domain == "workflow.pokemon" && edit.Field == SwShPokemonWorkflowService.CatchRateField),
            edit => Assert.InRange(int.Parse(edit.NewValue), 1, 255));
        var shuffledStats = new[]
        {
            SwShPokemonWorkflowService.HPField,
            SwShPokemonWorkflowService.AttackField,
            SwShPokemonWorkflowService.DefenseField,
            SwShPokemonWorkflowService.SpecialAttackField,
            SwShPokemonWorkflowService.SpecialDefenseField,
            SwShPokemonWorkflowService.SpeedField,
        }.Select(field => int.Parse(edits.Single(edit =>
            edit.Domain == "workflow.pokemon"
            && edit.RecordId == "1"
            && edit.Field == field).NewValue)).ToArray();
        Assert.Equal(new[] { 45, 45, 49, 49, 65, 65 }, shuffledStats.Order().ToArray());
        Assert.NotEqual(new[] { 45, 49, 49, 65, 65, 45 }, shuffledStats);
        Assert.All(
            edits.Where(edit => edit.Domain == "workflow.pokemon" && edit.Field.StartsWith("learnset:upsert:", StringComparison.Ordinal)),
            edit => Assert.DoesNotContain(ParseLearnsetMove(edit), new[] { 49, 82 }));
        Assert.Contains(
            edits.Where(edit => edit.Domain == "workflow.pokemon" && edit.Field.StartsWith("learnset:upsert:", StringComparison.Ordinal)),
            edit => ParseLearnsetLevel(edit) == 75);
        Assert.All(
            edits.Where(edit => edit.Domain == "workflow.pokemon" && edit.Field == SwShPokemonWorkflowService.Type2Field),
            edit =>
            {
                var matchingType1 = edits.Single(candidate =>
                    candidate.Domain == edit.Domain
                    && candidate.RecordId == edit.RecordId
                    && candidate.Field == SwShPokemonWorkflowService.Type1Field);
                Assert.NotEqual(matchingType1.NewValue, edit.NewValue);
            });

        Assert.Contains(edits, edit => edit.Domain == "workflow.encounters" && edit.Field == SwShEncountersWorkflowService.SpeciesIdField);
        Assert.Contains(edits, edit => edit.Domain == "workflow.encounters" && edit.Field == SwShEncountersWorkflowService.FormField);
        Assert.Contains(edits, edit => edit.Domain == "workflow.encounters" && edit.Field == SwShEncountersWorkflowService.ProbabilityField);
        foreach (var probabilityGroup in edits
            .Where(edit => edit.Domain == "workflow.encounters" && edit.Field == SwShEncountersWorkflowService.ProbabilityField)
            .GroupBy(edit => edit.RecordId.Split('#')[0], StringComparer.Ordinal))
        {
            var weights = probabilityGroup
                .OrderBy(edit => int.Parse(edit.RecordId.Split('#')[1]))
                .Select(edit => int.Parse(edit.NewValue))
                .ToArray();
            Assert.Equal(10, weights.Length);
            Assert.Equal(100, weights.Sum());
            for (var index = 1; index < weights.Length; index++)
            {
                Assert.True(weights[index - 1] > weights[index]);
            }
        }

        Assert.Contains(edits, edit => edit.Domain == SwShStaticEncountersWorkflowService.StaticEncountersEditDomain && edit.Field == SwShStaticEncountersWorkflowService.SpeciesField);
        Assert.Contains(edits, edit => edit.Domain == SwShStaticEncountersWorkflowService.StaticEncountersEditDomain && edit.Field == SwShStaticEncountersWorkflowService.FormField);
        Assert.Contains(edits, edit => edit.Domain == SwShStaticEncountersWorkflowService.StaticEncountersEditDomain && edit.Field == SwShStaticEncountersWorkflowService.CanGigantamaxField && edit.NewValue == "0");
        Assert.Contains(edits, edit => edit.Domain == SwShGiftPokemonWorkflowService.GiftPokemonEditDomain && edit.Field == SwShGiftPokemonWorkflowService.SpeciesField);
        Assert.Contains(edits, edit => edit.Domain == SwShGiftPokemonWorkflowService.GiftPokemonEditDomain && edit.Field == SwShGiftPokemonWorkflowService.FormField);
        Assert.Contains(edits, edit => edit.Domain == SwShGiftPokemonWorkflowService.GiftPokemonEditDomain && edit.Field == SwShGiftPokemonWorkflowService.CanGigantamaxField && edit.NewValue == "0");
        Assert.Contains(edits, edit => edit.Domain == SwShRaidRewardsEditSessionService.RaidRewardsEditDomain && edit.Field == SwShRaidRewardsWorkflowService.ItemIdField);
        Assert.Contains(edits, edit => edit.Domain == SwShRaidRewardsEditSessionService.RaidBonusRewardsEditDomain && edit.Field == SwShRaidRewardsWorkflowService.ItemIdField);
    }

    [Fact]
    public void PreviewHonorsChildSelectionsForStatsTypesAbilitiesCompatibilityAndLearnsets()
    {
        using var temp = CreateRandomizerProject();
        var service = new SwShRandomizerService();

        var hpOnly = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonStats = true,
            ShufflePokemonStats = false,
            StatHp = true,
            StatAttack = false,
            StatDefense = false,
            StatSpecialAttack = false,
            StatSpecialDefense = false,
            StatSpeed = false,
        }));
        var hpOnlyEdits = AllEdits(hpOnly).ToArray();
        AssertNoErrors(hpOnly);
        AssertHasOnlyPokemonFields(hpOnlyEdits, SwShPokemonWorkflowService.HPField);

        var primaryTypeOnly = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonTypes = true,
            TypePrimary = true,
            TypeSecondary = false,
        }));
        var primaryTypeOnlyEdits = AllEdits(primaryTypeOnly).ToArray();
        AssertNoErrors(primaryTypeOnly);
        AssertHasOnlyPokemonFields(primaryTypeOnlyEdits, SwShPokemonWorkflowService.Type1Field);

        var ability1Only = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonAbilities = true,
            Ability1 = true,
            Ability2 = false,
            HiddenAbility = false,
        }));
        var ability1OnlyEdits = AllEdits(ability1Only).ToArray();
        AssertNoErrors(ability1Only);
        AssertHasOnlyPokemonFields(ability1OnlyEdits, SwShPokemonWorkflowService.Ability1Field);

        var tmOnly = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonCompatibility = true,
            CompatibilityMachines = true,
            CompatibilityRecords = false,
            CompatibilityTutors = false,
        }));
        var tmOnlyEdits = AllEdits(tmOnly).ToArray();
        AssertNoErrors(tmOnly);
        Assert.All(
            tmOnlyEdits.Where(edit => edit.Domain == "workflow.pokemon"),
            edit => Assert.True(IsCompatibilityField(edit, SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId)));

        var compactLearnsets = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonLearnsets = true,
            LearnsetExpandTo25 = false,
            LearnsetBanFixedDamageMoves = true,
            LearnsetRequireDamagingMove = true,
            LearnsetStabFirst = true,
        }));
        var compactLearnsetSlots = AllEdits(compactLearnsets)
            .Where(edit => edit.Domain == "workflow.pokemon"
                && edit.RecordId == "1"
                && edit.Field.StartsWith("learnset:upsert:", StringComparison.Ordinal))
            .ToArray();
        AssertNoErrors(compactLearnsets);
        Assert.Equal(2, compactLearnsetSlots.Length);
        Assert.Contains(compactLearnsetSlots, edit => IsDamagingMove(ParseLearnsetMove(edit)));

        var expandedLearnsets = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonLearnsets = true,
            LearnsetExpandTo25 = true,
            LearnsetBanFixedDamageMoves = true,
            LearnsetRequireDamagingMove = true,
            LearnsetStabFirst = true,
        }));
        var expandedLearnsetSlots = AllEdits(expandedLearnsets)
            .Where(edit => edit.Domain == "workflow.pokemon"
                && edit.RecordId == "1"
                && edit.Field.StartsWith("learnset:upsert:", StringComparison.Ordinal))
            .ToArray();
        AssertNoErrors(expandedLearnsets);
        Assert.Equal(25, expandedLearnsetSlots.Length);
        Assert.Contains(expandedLearnsetSlots, edit => edit.Field == "learnset:upsert:24" && ParseLearnsetLevel(edit) == 75);
        Assert.Contains(expandedLearnsetSlots, edit => IsDamagingMove(ParseLearnsetMove(edit)));
        Assert.Contains(GetMoveType(ParseLearnsetMove(expandedLearnsetSlots.Single(edit => edit.Field == "learnset:upsert:0"))), new[] { 3, 11 });
    }

    [Fact]
    public void ApplyAllSelectableRandomizerOptionsWritesExpectedOutputDomains()
    {
        using var temp = CreateRandomizerProject();
        var service = new SwShRandomizerService();

        var result = service.Apply(temp.Paths, CreateConfig(AllRandomizerOptions()));

        Assert.DoesNotContain(result.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.LearnsetDataPath);
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.CreateEvolutionDataPath(1));
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShEncountersWorkflowService.WildDataPath);
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShStaticEncountersWorkflowService.StaticEncounterDataPath);
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShGiftPokemonWorkflowService.GiftPokemonDataPath);
    }

    private static SwShRandomizerOptions AllRandomizerOptions()
    {
        return SwShRandomizerOptions.Empty with
        {
            RandomizePokemonStats = true,
            ShufflePokemonStats = true,
            StatHp = true,
            StatAttack = true,
            StatDefense = true,
            StatSpecialAttack = true,
            StatSpecialDefense = true,
            StatSpeed = true,
            RandomizePokemonTypes = true,
            TypePrimary = true,
            TypeSecondary = true,
            AllowSameType = false,
            RandomizePokemonAbilities = true,
            Ability1 = true,
            Ability2 = true,
            HiddenAbility = true,
            RandomizePokemonHeldItems = true,
            RandomizePokemonCatchRates = true,
            RandomizePokemonLearnsets = true,
            LearnsetStabFirst = true,
            LearnsetExpandTo25 = true,
            LearnsetBanFixedDamageMoves = true,
            LearnsetRequireDamagingMove = true,
            RandomizePokemonCompatibility = true,
            CompatibilityMachines = true,
            CompatibilityRecords = true,
            CompatibilityTutors = true,
            RandomizePokemonEvolutions = true,
            RandomizeWildEncounters = true,
            RandomizeStaticEncounters = true,
            RandomizeGiftEncounters = true,
            RandomizeRaidRewards = true,
            RandomizeRaidBonusRewards = true,
        };
    }

    private static SwShRandomizerConfig CreateConfig(SwShRandomizerOptions options)
    {
        return new SwShRandomizerConfig("verify-options", options, RollSeed: "roll-fixed");
    }

    private static TemporarySwShProject CreateRandomizerProject()
    {
        var temp = TemporarySwShProject.Create();
        WritePokemonData(temp);
        WriteMoveData(temp);
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        WriteWildAndRaidData(temp);
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        SwShGiftPokemonWorkflowServiceTests.WriteGiftFixture(temp);
        WriteMessageTables(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }

    private static void WritePokemonData(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 45, hatchedSpecies: 1),
                SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 60, hatchedSpecies: 2),
                SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 80, hatchedSpecies: 3)));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza_oboe/wazaoboe_total.bin",
            SwShPokemonWorkflowServiceTests.CreateLearnsetTable(
                [],
                [(33, 1), (45, 3)],
                [(33, 1), (45, 3)],
                [(33, 1)]));
        temp.WriteBaseRomFsFile(
            "bin/pml/evolution/evo_001.bin",
            SwShPokemonWorkflowServiceTests.CreateEvolutionFile((4, 0, 2, 0, 16)));
        temp.WriteBaseRomFsFile(
            "bin/pml/evolution/evo_002.bin",
            SwShPokemonWorkflowServiceTests.CreateEvolutionFile((4, 0, 3, 0, 32)));
    }

    private static void WriteMoveData(TemporarySwShProject temp)
    {
        foreach (var moveId in Enumerable.Range(1, 30).Concat([49, 82]))
        {
            temp.WriteBaseRomFsFile(
                $"bin/pml/waza/waza_{moveId:D3}.bin",
                SwShMoveDataFile.Write(CreateMoveRecord(moveId, GetMoveType(moveId), category: moveId % 4 == 0 ? 0 : 1)));
        }

        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazainfo.dat",
            CreateTextTable(900, Enumerable.Range(1, 900).Select(index => (index, $"Move {index} info")).ToArray()));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/typename.dat",
            CreateTextTable(
                "Normal",
                "Fighting",
                "Flying",
                "Poison",
                "Ground",
                "Rock",
                "Bug",
                "Ghost",
                "Steel",
                "Fire",
                "Water",
                "Grass",
                "Electric",
                "Psychic",
                "Ice",
                "Dragon",
                "Dark",
                "Fairy"));
    }

    private static void WriteWildAndRaidData(TemporarySwShProject temp)
    {
        var pack = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", CreateTenSlotEncounterArchive().Write()),
            new SwShGfPackNamedFile("encount_k.bin", CreateTenSlotEncounterArchive(speciesOffset: 2).Write()),
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", SwShRaidRewardTestFixtures.CreateDropArchive().Write()),
            new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", SwShRaidRewardTestFixtures.CreateBonusArchive().Write()),
        ]);

        temp.WriteBaseRomFsFile("bin/archive/field/resident/data_table.gfpak", pack.Write());
    }

    private static SwShWildEncounterArchive CreateTenSlotEncounterArchive(int speciesOffset = 0)
    {
        return SwShEncounterTestFixtures.CreateArchive(
            speciesOffset: speciesOffset,
            subTables: Enumerable.Range(0, 11)
                .Select(index => CreateTenSlotSubTable(index, speciesOffset))
                .ToArray());
    }

    private static SwShWildEncounterSubTable CreateTenSlotSubTable(int index, int speciesOffset)
    {
        int[] probabilities = [19, 17, 15, 13, 11, 9, 7, 5, 3, 1];
        return new SwShWildEncounterSubTable(
            (byte)(3 + index),
            (byte)(8 + index),
            probabilities
                .Select((probability, slotIndex) => new SwShWildEncounterSlot(
                    (byte)probability,
                    1 + speciesOffset + slotIndex,
                    (byte)(slotIndex % 2)))
                .ToArray());
    }

    private static void WriteMessageTables(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/pokelist.dat",
            CreateTextTable("None", "Which Pokemon do you want to swap with?"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(810, (1, "Bulbasaur"), (2, "Ivysaur"), (3, "Venusaur"), (25, "Pikachu"), (133, "Eevee"), (810, "Grookey")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateTextTable(4, (1, "Potion"), (2, "Antidote"), (3, "Rare Candy"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/tokusei.dat",
            CreateTextTable(70, (34, "Chlorophyll"), (65, "Overgrow")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateTextTable(900, Enumerable.Range(1, 900).Select(index => (index, $"Move {index}")).ToArray()));
    }

    private static SwShMoveDataRecord CreateMoveRecord(int moveId, int type, int category)
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: (ushort)moveId,
            CanUseMove: true,
            new SwShMoveCoreStats(
                Type: (byte)type,
                Quality: 2,
                Category: (byte)category,
                Power: category == 0 ? (byte)0 : (byte)40,
                Accuracy: 100,
                PP: 20,
                Priority: 0,
                CritStage: 0,
                GigantamaxPower: 90),
            new SwShMoveTargeting(
                RawTarget: 3,
                HitMin: 1,
                HitMax: 1,
                TurnMin: 0,
                TurnMax: 0),
            new SwShMoveSecondaryEffects(
                Inflict: 0,
                InflictPercent: 0,
                RawInflictCount: 0,
                Flinch: 0,
                EffectSequence: 0,
                Recoil: 0,
                RawHealing: 0),
            [
                new SwShMoveStatChange(0, Stat: 0, Stage: 0, Percent: 0),
                new SwShMoveStatChange(0, Stat: 0, Stage: 0, Percent: 0),
                new SwShMoveStatChange(0, Stat: 0, Stage: 0, Percent: 0),
            ],
            new SwShMoveFlags(
                MakesContact: false,
                Charge: false,
                Recharge: false,
                Protect: true,
                Reflectable: false,
                Snatch: false,
                Mirror: false,
                Punch: false,
                Sound: false,
                Gravity: false,
                Defrost: false,
                DistanceTriple: false,
                Heal: false,
                IgnoreSubstitute: false,
                FailSkyBattle: false,
                AnimateAlly: false,
                Dance: false,
                Metronome: false));
    }

    private static int GetMoveType(int moveId)
    {
        return moveId % 18;
    }

    private static IEnumerable<SwShRandomizerPreviewEdit> AllEdits(SwShRandomizerPreviewResult preview)
    {
        return preview.Domains.SelectMany(domain => domain.Edits);
    }

    private static void AssertNoErrors(SwShRandomizerPreviewResult preview)
    {
        Assert.DoesNotContain(preview.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void AssertHasPokemonField(IEnumerable<SwShRandomizerPreviewEdit> edits, string field)
    {
        Assert.Contains(edits, edit => edit.Domain == "workflow.pokemon" && edit.Field == field);
    }

    private static void AssertHasOnlyPokemonFields(IEnumerable<SwShRandomizerPreviewEdit> edits, params string[] fields)
    {
        var allowed = fields.ToHashSet(StringComparer.Ordinal);
        Assert.All(
            edits.Where(edit => edit.Domain == "workflow.pokemon"),
            edit => Assert.Contains(edit.Field, allowed));
        foreach (var field in allowed)
        {
            AssertHasPokemonField(edits, field);
        }
    }

    private static bool IsCompatibilityField(SwShRandomizerPreviewEdit edit, string groupId)
    {
        return edit.Domain == "workflow.pokemon"
            && edit.Field.StartsWith($"compatibility:{groupId}:", StringComparison.Ordinal);
    }

    private static int ParseLearnsetMove(SwShRandomizerPreviewEdit edit)
    {
        return int.Parse(edit.NewValue.Split(':')[0]);
    }

    private static int ParseLearnsetLevel(SwShRandomizerPreviewEdit edit)
    {
        return int.Parse(edit.NewValue.Split(':')[1]);
    }

    private static bool IsDamagingMove(int moveId)
    {
        return moveId % 4 != 0;
    }

    private static byte[] CreateTextTable(params string[] values)
    {
        return SwShGameTextFile.Write(values.Select(value => new SwShGameTextLine(value, Flags: 0)).ToArray());
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
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
}
