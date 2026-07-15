// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
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
using KM.SwSh.TypeChart;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.Randomizer;

public sealed class SwShRandomizerOptionCoverageTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private static readonly int[] ProtectedBoxLegendarySpeciesIds = [888, 889, 890];

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
        Assert.Contains(preview.Domains, domain => domain.Label == "Type Chart");

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
            edits.Where(edit => edit.Domain == "workflow.pokemon"
                && (edit.Field == SwShPokemonWorkflowService.Type1Field
                    || edit.Field == SwShPokemonWorkflowService.Type2Field)),
            edit => Assert.InRange(int.Parse(edit.NewValue), 0, 17));

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
        Assert.All(
            edits.Where(edit => edit.Domain == SwShStaticEncountersWorkflowService.StaticEncountersEditDomain),
            edit =>
            {
                Assert.True(SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(
                    edit.RecordId,
                    out _,
                    out var encounterId));
                Assert.NotNull(encounterId);
            });
        Assert.Contains(edits, edit => edit.Domain == SwShGiftPokemonWorkflowService.GiftPokemonEditDomain && edit.Field == SwShGiftPokemonWorkflowService.SpeciesField);
        Assert.Contains(edits, edit => edit.Domain == SwShGiftPokemonWorkflowService.GiftPokemonEditDomain && edit.Field == SwShGiftPokemonWorkflowService.FormField);
        Assert.Contains(edits, edit => edit.Domain == SwShGiftPokemonWorkflowService.GiftPokemonEditDomain && edit.Field == SwShGiftPokemonWorkflowService.CanGigantamaxField && edit.NewValue == "0");
        Assert.Contains(edits, edit => edit.Domain == SwShRaidRewardsEditSessionService.RaidRewardsEditDomain && edit.Field == SwShRaidRewardsWorkflowService.ItemIdField);
        Assert.Contains(edits, edit => edit.Domain == SwShRaidRewardsEditSessionService.RaidBonusRewardsEditDomain && edit.Field == SwShRaidRewardsWorkflowService.ItemIdField);
        var typeChartValues = DecodeTypeChartValues(Assert.Single(edits, edit =>
            edit.Domain == SwShTypeChartEditSessionService.TypeChartEditDomain
            && edit.Field == "effectiveness").NewValue);
        Assert.All(typeChartValues, value => Assert.Contains(value, new[] { 0, 2, 4, 8 }));
        Assert.All(
            Enumerable.Range(0, SwShTypeChartMainPatcher.TypeCount),
            attackType => Assert.InRange(
                typeChartValues
                    .Skip(attackType * SwShTypeChartMainPatcher.TypeCount)
                    .Take(SwShTypeChartMainPatcher.TypeCount)
                    .Count(value => value == 0),
                0,
                1));
    }

    [Fact]
    public void PreviewDoesNotRandomizeProtectedBoxLegendarySpecies()
    {
        using var temp = CreateRandomizerProject();
        WritePokemonDataWithProtectedBoxLegendaries(temp);
        WriteWildAndRaidData(temp, protectedFirstSlotSpeciesId: 888);
        WriteProtectedStaticEncounterData(temp);
        WriteProtectedGiftData(temp);
        var service = new SwShRandomizerService();

        var preview = service.Preview(temp.Paths, CreateConfig(AllRandomizerOptions()));
        var edits = AllEdits(preview).ToArray();

        AssertNoErrors(preview);
        Assert.DoesNotContain(edits, edit => edit.Domain == "workflow.pokemon" && edit.RecordId is "888" or "889" or "890" or "891");
        Assert.DoesNotContain(
            edits,
            edit => edit.Domain == "workflow.pokemon"
                && edit.RecordId == "1"
                && edit.Field.StartsWith("evolution:upsert:", StringComparison.Ordinal));
        Assert.DoesNotContain(
            edits,
            edit => edit.Domain == "workflow.encounters"
                && edit.RecordId.EndsWith("#0", StringComparison.Ordinal));
        Assert.DoesNotContain(
            edits,
            edit => edit.Domain == SwShStaticEncountersWorkflowService.StaticEncountersEditDomain
                && SwShStaticEncountersWorkflowService.TryParseEncounterRecordId(
                    edit.RecordId,
                    out var encounterIndex)
                && encounterIndex == 0);
        Assert.DoesNotContain(
            edits,
            edit => edit.Domain == SwShGiftPokemonWorkflowService.GiftPokemonEditDomain
                && edit.RecordId == SwShGiftPokemonWorkflowService.CreateGiftRecordId(0));
        Assert.DoesNotContain(edits, WritesProtectedBoxLegendarySpecies);
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
    public void TypeChartRandomizerHonorsImmunityConstraints()
    {
        using var temp = CreateRandomizerProject();
        var service = new SwShRandomizerService();

        var noImmunities = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
            TypeChartNoImmunities = true,
        }));
        AssertNoErrors(noImmunities);
        var noImmunityValues = DecodeTypeChartValues(Assert.Single(AllEdits(noImmunities), edit =>
            edit.Domain == SwShTypeChartEditSessionService.TypeChartEditDomain).NewValue);
        Assert.DoesNotContain(0, noImmunityValues);

        var oneImmunityPerType = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
            TypeChartOneImmunityPerType = true,
        }));
        AssertNoErrors(oneImmunityPerType);
        var limitedImmunityValues = DecodeTypeChartValues(Assert.Single(AllEdits(oneImmunityPerType), edit =>
            edit.Domain == SwShTypeChartEditSessionService.TypeChartEditDomain).NewValue);
        Assert.All(
            Enumerable.Range(0, SwShTypeChartMainPatcher.TypeCount),
            attackType => Assert.InRange(
                limitedImmunityValues
                    .Skip(attackType * SwShTypeChartMainPatcher.TypeCount)
                    .Take(SwShTypeChartMainPatcher.TypeCount)
                    .Count(value => value == 0),
                0,
                1));
    }

    [Fact]
    public void PokemonTypeRandomizerCanChangeBetweenSingleAndDualTyping()
    {
        var typeOptions = Enumerable.Range(0, 18).ToArray();
        var sawSingleToDual = false;
        var sawDualToSingle = false;

        for (var index = 0; index < 200 && (!sawSingleToDual || !sawDualToSingle); index++)
        {
            var singleRoll = SwShRandomizerService.CreateRandomizedTypePair(
                originalType1: 10,
                originalType2: 10,
                typeOptions,
                DeterministicRandom.Create("type-shape", $"single-{index}"));
            sawSingleToDual |= singleRoll.Type1 != singleRoll.Type2;

            var dualRoll = SwShRandomizerService.CreateRandomizedTypePair(
                originalType1: 11,
                originalType2: 3,
                typeOptions,
                DeterministicRandom.Create("type-shape", $"dual-{index}"));
            sawDualToSingle |= dualRoll.Type1 == dualRoll.Type2;
        }

        Assert.True(sawSingleToDual);
        Assert.True(sawDualToSingle);
    }

    [Fact]
    public void TypeChartRandomizerDoesNotRequirePokemonPersonalData()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateTypeChartCompatibleNso());
        var service = new SwShRandomizerService();

        var preview = service.Preview(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
        }));

        AssertNoErrors(preview);
        Assert.Contains(preview.Domains, domain => domain.Label == "Type Chart");
        Assert.Single(AllEdits(preview), edit => edit.Domain == SwShTypeChartEditSessionService.TypeChartEditDomain);
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
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);
        Assert.DoesNotContain(
            result.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Message.Contains("source layer changed", StringComparison.Ordinal));
        var appliedMessages = result.ApplyResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.StartsWith("Applied ", StringComparison.Ordinal))
            .Select(diagnostic => diagnostic.Message)
            .ToArray();
        var staticIndex = Array.FindIndex(
            appliedMessages,
            message => message.StartsWith("Applied Static Encounter", StringComparison.Ordinal));
        var pokemonIndex = Array.FindIndex(
            appliedMessages,
            message => message.StartsWith("Applied Pokemon Data", StringComparison.Ordinal));
        Assert.True(staticIndex >= 0 && pokemonIndex > staticIndex);
    }

    [Fact]
    public void ApplyRollsBackEarlierDomainsWhenALaterDomainFails()
    {
        using var temp = CreateRandomizerProject();
        var blockedMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        Directory.CreateDirectory(blockedMainPath);
        var service = new SwShRandomizerService();

        var result = service.Apply(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonStats = true,
            StatHp = true,
            RandomizeStaticEncounters = true,
            RandomizeTypeChart = true,
        }));

        Assert.Contains(result.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            result.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Message.Contains("source layer changed", StringComparison.Ordinal));
        Assert.Contains(
            result.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("all output changes were rolled back", StringComparison.Ordinal));
        Assert.Empty(result.ApplyResult.WrittenFiles);
        Assert.Empty(result.ApplyResult.Manifest.Writes);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShStaticEncountersWorkflowService.StaticEncounterDataPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(Directory.Exists(blockedMainPath));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, ".km-editor", "randomizer-manifest.json")));
    }

    [Fact]
    public void ApplyRejectsOutputThroughSymbolicLinkBelowOutputRoot()
    {
        using var temp = CreateRandomizerProject();
        var externalRoot = Directory.CreateDirectory(Path.Combine(temp.RootPath, "external-output")).FullName;
        var externalMainPath = Path.Combine(externalRoot, "main");
        var originalMain = File.ReadAllBytes(Path.Combine(temp.BaseExeFsPath, "main"));
        File.WriteAllBytes(externalMainPath, originalMain);
        var linkPath = Path.Combine(temp.OutputRootPath, "exefs");
        if (!TryCreateDirectorySymbolicLink(linkPath, externalRoot))
        {
            return;
        }

        try
        {
            var result = new SwShRandomizerService().Apply(
                temp.Paths,
                CreateConfig(SwShRandomizerOptions.Empty with { RandomizeTypeChart = true }));

            Assert.Contains(
                result.ApplyResult.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                    && diagnostic.Message.Contains("snapshot output before apply", StringComparison.Ordinal));
            Assert.Empty(result.ApplyResult.WrittenFiles);
            Assert.Equal(originalMain, File.ReadAllBytes(externalMainPath));
            Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, ".km-editor", "randomizer-manifest.json")));
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Fact]
    public void ApplyAllowsConfiguredOutputRootSymbolicLink()
    {
        using var temp = CreateRandomizerProject();
        var rootLink = Path.Combine(temp.RootPath, "output-link");
        if (!TryCreateDirectorySymbolicLink(rootLink, temp.OutputRootPath))
        {
            return;
        }

        try
        {
            var result = new SwShRandomizerService().Apply(
                temp.Paths with { OutputRootPath = rootLink },
                CreateConfig(SwShRandomizerOptions.Empty with { RandomizeTypeChart = true }));

            Assert.DoesNotContain(
                result.ApplyResult.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, "exefs", "main")));
        }
        finally
        {
            if (Directory.Exists(rootLink))
            {
                Directory.Delete(rootLink);
            }
        }
    }

    [Fact]
    public void RestoreReinstatesPreExistingLayeredOutputInsteadOfDeletingIt()
    {
        using var temp = CreateRandomizerProject();
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var originalLayeredMain = ModifyNsoDataByte(
            File.ReadAllBytes(Path.Combine(temp.BaseExeFsPath, "main")),
            offset: 0,
            value: 0x6A);
        temp.WriteOutputFile("exefs/main", originalLayeredMain);
        var service = new SwShRandomizerService();

        var apply = service.Apply(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
        }));
        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.NotEqual(originalLayeredMain, File.ReadAllBytes(outputMainPath));

        var restore = service.Restore(temp.Paths);

        Assert.DoesNotContain(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(originalLayeredMain, File.ReadAllBytes(outputMainPath));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, ".km-editor", "randomizer-manifest.json")));
    }

    [Fact]
    public void RestoreRollsBackEarlierTargetAndRetainsBackupsAndManifestWhenLaterTargetFails()
    {
        using var temp = CreateRandomizerProject();
        var mainRelativePath = SwShTypeChartWorkflowService.ExeFsMainPath;
        var personalRelativePath = SwShPokemonWorkflowService.PersonalDataPath;
        var originalMain = ModifyNsoDataByte(
            File.ReadAllBytes(Path.Combine(temp.BaseExeFsPath, "main")),
            offset: 0,
            value: 0x6A);
        var originalPersonal = File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            personalRelativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar)));
        temp.WriteOutputFile(mainRelativePath, originalMain);
        temp.WriteOutputFile(personalRelativePath, originalPersonal);

        var applyService = new SwShRandomizerService();
        var apply = applyService.Apply(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonStats = true,
            StatHp = true,
            RandomizeTypeChart = true,
        }));
        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var mainPath = ResolveOutputPath(temp, mainRelativePath);
        var personalPath = ResolveOutputPath(temp, personalRelativePath);
        var manifestPath = ResolveOutputPath(temp, ".km-editor/randomizer-manifest.json");
        var randomizedMain = File.ReadAllBytes(mainPath);
        var randomizedPersonal = File.ReadAllBytes(personalPath);
        var manifestBeforeRestore = File.ReadAllBytes(manifestPath);
        var backupsBeforeRestore = SnapshotRandomizerBackups(temp);
        Assert.Equal(2, backupsBeforeRestore.Count);

        var restoreService = new SwShRandomizerService
        {
            RestoreMutationHook = (stage, relativePath) =>
            {
                if (stage == SwShRandomizerRestoreMutationStage.BeforeTargetMutation
                    && string.Equals(relativePath, personalRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Injected later target failure.");
                }
            },
        };

        var restore = restoreService.Restore(temp.Paths);

        Assert.Contains(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            restore.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("changes were rolled back", StringComparison.Ordinal));
        Assert.Empty(restore.WrittenFiles);
        Assert.Equal(randomizedMain, File.ReadAllBytes(mainPath));
        Assert.Equal(randomizedPersonal, File.ReadAllBytes(personalPath));
        Assert.Equal(manifestBeforeRestore, File.ReadAllBytes(manifestPath));
        AssertRandomizerBackupsEqual(backupsBeforeRestore, SnapshotRandomizerBackups(temp));
    }

    [Fact]
    public void RestoreRollsBackTargetAndRetainsBackupWhenManifestWriteFails()
    {
        using var temp = CreateRandomizerProject();
        var mainRelativePath = SwShTypeChartWorkflowService.ExeFsMainPath;
        var personalRelativePath = SwShPokemonWorkflowService.PersonalDataPath;
        var originalMain = ModifyNsoDataByte(
            File.ReadAllBytes(Path.Combine(temp.BaseExeFsPath, "main")),
            offset: 0,
            value: 0x6A);
        temp.WriteOutputFile(mainRelativePath, originalMain);

        var applyService = new SwShRandomizerService();
        var apply = applyService.Apply(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizePokemonStats = true,
            StatHp = true,
            RandomizeTypeChart = true,
        }));
        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var mainPath = ResolveOutputPath(temp, mainRelativePath);
        var personalPath = ResolveOutputPath(temp, personalRelativePath);
        var laterPersonal = File.ReadAllBytes(personalPath);
        laterPersonal[0] ^= 0xFF;
        File.WriteAllBytes(personalPath, laterPersonal);
        var manifestPath = ResolveOutputPath(temp, ".km-editor/randomizer-manifest.json");
        var randomizedMain = File.ReadAllBytes(mainPath);
        var manifestBeforeRestore = File.ReadAllBytes(manifestPath);
        var backupsBeforeRestore = SnapshotRandomizerBackups(temp);
        Assert.Single(backupsBeforeRestore);

        var restoreService = new SwShRandomizerService
        {
            RestoreMutationHook = (stage, _) =>
            {
                if (stage == SwShRandomizerRestoreMutationStage.BeforeManifestMutation)
                {
                    throw new IOException("Injected manifest write failure.");
                }
            },
        };

        var restore = restoreService.Restore(temp.Paths);

        Assert.Contains(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            restore.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("changes were rolled back", StringComparison.Ordinal));
        Assert.Empty(restore.WrittenFiles);
        Assert.Equal(randomizedMain, File.ReadAllBytes(mainPath));
        Assert.Equal(laterPersonal, File.ReadAllBytes(personalPath));
        Assert.Equal(manifestBeforeRestore, File.ReadAllBytes(manifestPath));
        AssertRandomizerBackupsEqual(backupsBeforeRestore, SnapshotRandomizerBackups(temp));
    }

    [Fact]
    public void RestoreDeletesOutputCreatedByRandomizerWhenItIsUnchanged()
    {
        using var temp = CreateRandomizerProject();
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var service = new SwShRandomizerService();

        var apply = service.Apply(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
        }));
        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(outputMainPath));

        var restore = service.Restore(temp.Paths);

        Assert.DoesNotContain(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(outputMainPath));
    }

    [Fact]
    public void RestorePreservesOutputChangedAfterRandomizerApply()
    {
        using var temp = CreateRandomizerProject();
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var service = new SwShRandomizerService();
        var apply = service.Apply(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
        }));
        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var laterEditedMain = ModifyNsoDataByte(File.ReadAllBytes(outputMainPath), offset: 1, value: 0x7B);
        File.WriteAllBytes(outputMainPath, laterEditedMain);

        var restore = service.Restore(temp.Paths);

        Assert.DoesNotContain(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            restore.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("changed after Randomizer apply", StringComparison.Ordinal));
        Assert.Equal(laterEditedMain, File.ReadAllBytes(outputMainPath));
        Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, ".km-editor", "randomizer-manifest.json")));
    }

    [Fact]
    public void ReapplyBlocksChangedOutputAndRestoreStillPreservesTheLaterEdit()
    {
        using var temp = CreateRandomizerProject();
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var service = new SwShRandomizerService();
        var config = CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
        });
        var firstApply = service.Apply(temp.Paths, config);
        Assert.DoesNotContain(firstApply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var laterEditedMain = ModifyNsoDataByte(File.ReadAllBytes(outputMainPath), offset: 1, value: 0x7B);
        File.WriteAllBytes(outputMainPath, laterEditedMain);

        var reapply = service.Apply(temp.Paths, config);

        Assert.Contains(
            reapply.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("changed after an earlier apply", StringComparison.Ordinal));
        Assert.Empty(reapply.ApplyResult.WrittenFiles);
        Assert.Equal(laterEditedMain, File.ReadAllBytes(outputMainPath));

        var restore = service.Restore(temp.Paths);

        Assert.DoesNotContain(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            restore.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("changed after Randomizer apply", StringComparison.Ordinal));
        Assert.Equal(laterEditedMain, File.ReadAllBytes(outputMainPath));
        Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, ".km-editor", "randomizer-manifest.json")));
    }

    [Fact]
    public void RestorePreservesLegacyManifestOutputWithoutVerifiableOwnership()
    {
        using var temp = CreateRandomizerProject();
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var layeredMain = File.ReadAllBytes(Path.Combine(temp.BaseExeFsPath, "main"));
        temp.WriteOutputFile("exefs/main", layeredMain);
        temp.WriteOutputFile(
            ".km-editor/randomizer-manifest.json",
            """
            {
              "version": 1,
              "updatedAt": "2026-01-01T00:00:00+00:00",
              "writtenRelativePaths": ["exefs/main"]
            }
            """);

        var restore = new SwShRandomizerService().Restore(temp.Paths);

        Assert.DoesNotContain(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            restore.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("legacy tracked Randomizer file", StringComparison.Ordinal));
        Assert.Equal(layeredMain, File.ReadAllBytes(outputMainPath));
    }

    [Fact]
    public void RestoreRejectsOutputThroughSymbolicLinkBelowOutputRoot()
    {
        using var temp = CreateRandomizerProject();
        var service = new SwShRandomizerService();
        var apply = service.Apply(temp.Paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeTypeChart = true,
        }));
        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var randomizedMain = File.ReadAllBytes(outputMainPath);
        var externalRoot = Directory.CreateDirectory(Path.Combine(temp.RootPath, "external-output")).FullName;
        var externalMainPath = Path.Combine(externalRoot, "main");
        File.Move(outputMainPath, externalMainPath);
        Directory.Delete(Path.GetDirectoryName(outputMainPath)!);
        var linkPath = Path.Combine(temp.OutputRootPath, "exefs");
        if (!TryCreateDirectorySymbolicLink(linkPath, externalRoot))
        {
            return;
        }

        try
        {
            var restore = service.Restore(temp.Paths);

            Assert.Contains(restore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Empty(restore.WrittenFiles);
            Assert.Equal(randomizedMain, File.ReadAllBytes(externalMainPath));
            Assert.True(File.Exists(Path.Combine(temp.OutputRootPath, ".km-editor", "randomizer-manifest.json")));
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Theory]
    [InlineData(ProjectGame.Sword, "encount_symbol_k.bin")]
    [InlineData(ProjectGame.Shield, "encount_symbol_t.bin")]
    public void ApplyWildAndTypeChartRandomizerUsesSelectedGameExecutableAndEncounterMembers(
        ProjectGame game,
        string expectedSymbolMember)
    {
        using var temp = CreateRandomizerProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        var service = new SwShRandomizerService();
        var result = service.Apply(paths, CreateConfig(SwShRandomizerOptions.Empty with
        {
            RandomizeWildEncounters = true,
            RandomizeTypeChart = true,
        }));

        Assert.DoesNotContain(result.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShEncountersWorkflowService.WildDataPath);
        Assert.Contains(result.ApplyResult.WrittenFiles, file => file.RelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);

        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak")));
        Assert.True(outputPack.TryGetFileByName(expectedSymbolMember, out _));

        var typeChartAnalysis = SwShTypeChartMainPatcher.Analyze(
            File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main")),
            game);
        Assert.Equal(SwShTypeChartMainKind.Modified, typeChartAnalysis.Kind);
        Assert.Equal(game, typeChartAnalysis.DetectedGame);
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
            RandomizeTypeChart = true,
            TypeChartNoImmunities = false,
            TypeChartOneImmunityPerType = true,
        };
    }

    private static SwShRandomizerConfig CreateConfig(SwShRandomizerOptions options)
    {
        return new SwShRandomizerConfig("verify-options", options, RollSeed: "roll-fixed");
    }

    private static string ResolveOutputPath(TemporarySwShProject temp, string relativePath)
    {
        return Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyDictionary<string, byte[]> SnapshotRandomizerBackups(TemporarySwShProject temp)
    {
        var backupRoot = ResolveOutputPath(temp, ".km-editor/randomizer-backups");
        return Directory.Exists(backupRoot)
            ? Directory.GetFiles(backupRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(backupRoot, path).Replace('\\', '/'),
                    File.ReadAllBytes,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertRandomizerBackupsEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> actual)
    {
        Assert.Equal(
            expected.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            actual.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        foreach (var (relativePath, expectedBytes) in expected)
        {
            Assert.Equal(expectedBytes, actual[relativePath]);
        }
    }

    private static TemporarySwShProject CreateRandomizerProject(ProjectGame game = ProjectGame.Sword)
    {
        var temp = TemporarySwShProject.Create();
        WritePokemonData(temp);
        WriteMoveData(temp);
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        WriteWildAndRaidData(temp, game);
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        SwShGiftPokemonWorkflowServiceTests.WriteGiftFixture(temp);
        WriteMessageTables(temp);
        temp.WriteBaseExeFsFile("main", CreateTypeChartCompatibleNso(game));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);

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

    private static void WritePokemonDataWithProtectedBoxLegendaries(TemporarySwShProject temp)
    {
        var records = Enumerable.Range(0, 892)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        records[1] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 45, hatchedSpecies: 1);
        records[2] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 60, hatchedSpecies: 2);
        records[3] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 80, hatchedSpecies: 3);
        records[888] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hp: 92,
            hatchedSpecies: 888,
            formStatsIndex: 891,
            formCount: 2);
        records[889] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 92, hatchedSpecies: 889);
        records[890] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 140, hatchedSpecies: 890);
        records[891] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hp: 92,
            hatchedSpecies: 888,
            localFormIndex: 1,
            form: 1);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(records));

        var learnsets = Enumerable.Range(0, records.Length)
            .Select(_ => Array.Empty<(ushort MoveId, ushort Level)>())
            .ToArray();
        learnsets[1] = [(33, 1), (45, 3)];
        learnsets[2] = [(33, 1), (45, 3)];
        learnsets[3] = [(33, 1)];
        temp.WriteBaseRomFsFile(
            "bin/pml/waza_oboe/wazaoboe_total.bin",
            SwShPokemonWorkflowServiceTests.CreateLearnsetTable(learnsets));
        temp.WriteBaseRomFsFile(
            "bin/pml/evolution/evo_001.bin",
            SwShPokemonWorkflowServiceTests.CreateEvolutionFile((4, 0, 888, 1, 16)));
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

    private static void WriteWildAndRaidData(
        TemporarySwShProject temp,
        ProjectGame game = ProjectGame.Sword,
        int? protectedFirstSlotSpeciesId = null)
    {
        var symbolMember = game == ProjectGame.Shield ? "encount_symbol_t.bin" : "encount_symbol_k.bin";
        var hiddenMember = game == ProjectGame.Shield ? "encount_t.bin" : "encount_k.bin";
        var pack = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile(symbolMember, CreateTenSlotEncounterArchive(protectedFirstSlotSpeciesId: protectedFirstSlotSpeciesId).Write()),
            new SwShGfPackNamedFile(hiddenMember, CreateTenSlotEncounterArchive(speciesOffset: 2, protectedFirstSlotSpeciesId: protectedFirstSlotSpeciesId).Write()),
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", SwShRaidRewardTestFixtures.CreateDropArchive().Write()),
            new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", SwShRaidRewardTestFixtures.CreateBonusArchive().Write()),
        ]);

        temp.WriteBaseRomFsFile("bin/archive/field/resident/data_table.gfpak", pack.Write());
    }

    private static SwShWildEncounterArchive CreateTenSlotEncounterArchive(
        int speciesOffset = 0,
        int? protectedFirstSlotSpeciesId = null)
    {
        return SwShEncounterTestFixtures.CreateArchive(
            speciesOffset: speciesOffset,
            subTables: Enumerable.Range(0, 11)
                .Select(index => CreateTenSlotSubTable(index, speciesOffset, protectedFirstSlotSpeciesId))
                .ToArray());
    }

    private static SwShWildEncounterSubTable CreateTenSlotSubTable(
        int index,
        int speciesOffset,
        int? protectedFirstSlotSpeciesId)
    {
        int[] probabilities = [19, 17, 15, 13, 11, 9, 7, 5, 3, 1];
        return new SwShWildEncounterSubTable(
            (byte)(3 + index),
            (byte)(8 + index),
            probabilities
                .Select((probability, slotIndex) => new SwShWildEncounterSlot(
                    (byte)probability,
                    slotIndex == 0 && protectedFirstSlotSpeciesId.HasValue
                        ? protectedFirstSlotSpeciesId.Value
                        : 1 + speciesOffset + slotIndex,
                    (byte)(slotIndex % 2)))
                .ToArray());
    }

    private static void WriteProtectedStaticEncounterData(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShStaticEncountersWorkflowService.StaticEncounterDataPath["romfs/".Length..],
            new SwShStaticEncounterArchive(
            [
                new SwShStaticEncounterRecord(
                    0,
                    0,
                    0,
                    new SwShStaticEncounterStats(0, 0, 0, 0, 0, 0),
                    0,
                    0,
                    0,
                    0x0102030405060708,
                    0,
                    true,
                    0,
                    70,
                    0,
                    889,
                    1,
                    0,
                    0,
                    new SwShStaticEncounterStats(-1, -1, -1, -1, -1, -1),
                    0,
                    [0, 0, 0, 0]),
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
            ]).Write());
    }

    private static void WriteProtectedGiftData(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShGiftPokemonWorkflowService.GiftPokemonDataPath["romfs/".Length..],
            new SwShGiftPokemonArchive(
            [
                new KM.Formats.SwSh.SwShGiftPokemonRecord(
                    0,
                    0,
                    1,
                    0,
                    4,
                    0,
                    0,
                    true,
                    0,
                    70,
                    890,
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1),
                    0,
                    0),
                new KM.Formats.SwSh.SwShGiftPokemonRecord(
                    1,
                    0,
                    0,
                    0,
                    4,
                    0,
                    0,
                    false,
                    0,
                    1,
                    133,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1),
                    0,
                    0),
            ]).Write());
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

    private static bool WritesProtectedBoxLegendarySpecies(SwShRandomizerPreviewEdit edit)
    {
        if (edit.Field is SwShEncountersWorkflowService.SpeciesIdField
            or SwShStaticEncountersWorkflowService.SpeciesField
            or SwShGiftPokemonWorkflowService.SpeciesField)
        {
            return int.TryParse(edit.NewValue, out var speciesId)
                && ProtectedBoxLegendarySpeciesIds.Contains(speciesId);
        }

        if (edit.Domain == "workflow.pokemon"
            && edit.Field.StartsWith("evolution:upsert:", StringComparison.Ordinal))
        {
            var parts = edit.NewValue.Split(':');
            return parts.Length >= 3
                && int.TryParse(parts[2], out var speciesId)
                && ProtectedBoxLegendarySpeciesIds.Contains(speciesId);
        }

        return false;
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

    private static int[] DecodeTypeChartValues(string value)
    {
        return Convert.FromHexString(value)
            .Select(effectiveness => (int)effectiveness)
            .ToArray();
    }

    private static byte[] CreateTypeChartCompatibleNso(ProjectGame game = ProjectGame.Sword)
    {
        var text = Enumerable.Range(0, 0x40).Select(index => (byte)(0x80 + index)).ToArray();
        var ro = new byte[SwShTypeChartMainPatcher.SwordRoChartOffset + SwShTypeChartMainPatcher.ChartLength + 0x40];
        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x20 + index)).ToArray();
        Array.Fill(ro, (byte)0xCC);
        SwShTypeChartMainPatcher.VanillaChartValues
            .Select(value => checked((byte)value))
            .ToArray()
            .CopyTo(ro.AsSpan(SwShTypeChartMainPatcher.SwordRoChartOffset));

        return CreateNso(text, ro, data, BuildIdForGame(game));
    }

    private static byte[] ModifyNsoDataByte(byte[] mainBytes, int offset, byte value)
    {
        var main = NsoFile.Parse(mainBytes);
        var data = main.Data.DecompressedData.ToArray();
        data[offset] = value;
        return main.Write(dataDecompressedData: data);
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static byte[] BuildIdForGame(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static void WriteSegmentHeader(byte[] output, int offset, int fileOffset, int memoryOffset, int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
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
