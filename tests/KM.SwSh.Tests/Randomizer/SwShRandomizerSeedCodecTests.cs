// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Items;
using KM.SwSh.Randomizer;
using Xunit;

namespace KM.SwSh.Tests.Randomizer;

public sealed class SwShRandomizerSeedCodecTests
{
    [Fact]
    public void ImportCanonicalSeedRoundTripsSelectedOptions()
    {
        var config = new SwShRandomizerConfig(
            "A1!*seed",
            SwShRandomizerOptions.Empty with
            {
                RandomizePokemonStats = true,
                RandomizePokemonLearnsets = true,
                LearnsetExpandTo25 = true,
                RandomizeWildEncounters = true,
                RandomizeRaidRewards = true,
                StatDefense = false,
            },
            RollSeed: "roll-001",
            OutputHash: "output-abc");

        var seed = SwShRandomizerSeedCodec.Export(config);
        var imported = SwShRandomizerSeedCodec.Import(seed);

        Assert.StartsWith("KM1-", seed, StringComparison.Ordinal);
        Assert.DoesNotContain('.', seed);
        Assert.True(imported.IsValid);
        Assert.Equal(seed, imported.Seed);
        Assert.NotNull(imported.Config);
        Assert.Equal("A1!*seed", imported.Config.UserSeed);
        Assert.True(imported.Config.Options.RandomizePokemonStats);
        Assert.True(imported.Config.Options.RandomizePokemonLearnsets);
        Assert.True(imported.Config.Options.LearnsetExpandTo25);
        Assert.True(imported.Config.Options.RandomizeWildEncounters);
        Assert.True(imported.Config.Options.RandomizeRaidRewards);
        Assert.False(imported.Config.Options.StatDefense);
        Assert.Equal("roll-001", imported.Config.RollSeed);
        Assert.Equal("output-abc", imported.Config.OutputHash);
    }

    [Fact]
    public void GenerationKeyIgnoresOutputHash()
    {
        var config = new SwShRandomizerConfig(
            "same",
            SwShRandomizerOptions.Empty with { RandomizePokemonLearnsets = true },
            RollSeed: "roll-001",
            OutputHash: "first-output");

        var left = SwShRandomizerSeedCodec.CreateGenerationKey(config);
        var right = SwShRandomizerSeedCodec.CreateGenerationKey(config with { OutputHash = "second-output" });

        Assert.Equal(left, right);
    }

    [Fact]
    public void WildEncounterWeightsAreStrictlyDescendingAndTotalOneHundred()
    {
        var rng = DeterministicRandom.Create("weights", "wildEncounters");

        var weights = SwShRandomizerService.CreateStrictDescendingSlotWeights(10, rng);

        Assert.Equal(10, weights.Count);
        Assert.Equal(100, weights.Sum());
        for (var index = 1; index < weights.Count; index++)
        {
            Assert.True(weights[index - 1] > weights[index]);
        }
    }

    [Fact]
    public void ExpandedLearnsetLevelsUseTwentyFiveSlotsThroughLevelSeventyFive()
    {
        var levels = SwShRandomizerService.CreateExpandedLearnsetLevels(25);

        Assert.Equal(25, levels.Count);
        Assert.Equal(1, levels[0]);
        Assert.Equal(75, levels[^1]);
        for (var index = 1; index < levels.Count; index++)
        {
            Assert.True(levels[index] > levels[index - 1]);
        }
    }

    [Fact]
    public void ImportRejectsTamperedSeed()
    {
        var seed = SwShRandomizerSeedCodec.Export(new SwShRandomizerConfig(
            "share-me",
            SwShRandomizerOptions.Empty with { RandomizeGiftEncounters = true }));
        var tampered = seed[..^1] + (seed[^1] == 'A' ? "B" : "A");

        var imported = SwShRandomizerSeedCodec.Import(tampered);

        Assert.False(imported.IsValid);
        Assert.Null(imported.Config);
        Assert.Null(imported.Seed);
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ImportRejectsRandomCharactersBeforeCreatingConfig()
    {
        var imported = SwShRandomizerSeedCodec.Import("not-a-real-randomizer-seed-12345");

        Assert.False(imported.IsValid);
        Assert.Null(imported.Config);
        Assert.Null(imported.Seed);
        Assert.Contains(imported.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == "seed");
    }

    [Fact]
    public void RaidRewardItemPoolExcludesRoyalCandyWhenInstalled()
    {
        var pool = SwShRandomizerService.CreateRaidRewardItemPool(
            [
                CreateItem(50, "Rare Candy", "Medicine", 0),
                CreateItem(1128, "Royal Candy", "Medicine", 0),
                CreateItem(450, "Secret Key", "Key Items", 8),
            ],
            royalCandyInstalled: true);

        Assert.Contains(50, pool);
        Assert.DoesNotContain(1128, pool);
        Assert.DoesNotContain(450, pool);
    }

    [Fact]
    public void RaidRewardItemPoolAllowsItem1128WhenRoyalCandyIsNotInstalled()
    {
        var pool = SwShRandomizerService.CreateRaidRewardItemPool(
            [
                CreateItem(50, "Rare Candy", "Medicine", 0),
                CreateItem(1128, "Exp. Candy XL", "Medicine", 0),
            ],
            royalCandyInstalled: false);

        Assert.Contains(50, pool);
        Assert.Contains(1128, pool);
    }

    private static SwShItemRecord CreateItem(int itemId, string name, string category, int pouch)
    {
        return new SwShItemRecord(
            itemId,
            name,
            category,
            BuyPrice: 0,
            SellPrice: 0,
            WattsPrice: 0,
            AlternatePrice: 0,
            CreateMetadata(pouch),
            SharedItemIds: Array.Empty<int>(),
            DetailGroups: Array.Empty<SwShItemDetailGroup>(),
            new SwShItemProvenance("romfs/bin/pml/item/item.dat", ProjectFileLayer.Base, ProjectFileGraphEntryState.BaseOnly));
    }

    private static SwShItemMetadata CreateMetadata(int pouch)
    {
        return new SwShItemMetadata(
            Pouch: pouch,
            PouchFlags: 0,
            FlingPower: 0,
            FieldUseType: 0,
            FieldFlags: 0,
            CanUseOnPokemon: false,
            ItemType: 0,
            SortIndex: 0,
            ItemSprite: 1,
            GroupType: 0,
            GroupIndex: 0,
            CureStatusFlags: 0,
            Boost0: 0,
            Boost1: 0,
            Boost2: 0,
            Boost3: 0,
            UseFlags1: 0,
            UseFlags2: 0,
            EvHp: 0,
            EvAttack: 0,
            EvDefense: 0,
            EvSpeed: 0,
            EvSpecialAttack: 0,
            EvSpecialDefense: 0,
            HealAmount: 0,
            PpGain: 0,
            FriendshipGain1: 0,
            FriendshipGain2: 0,
            FriendshipGain3: 0,
            MachineSlot: null,
            MachineMoveId: null,
            MachineMoveName: null);
    }
}
