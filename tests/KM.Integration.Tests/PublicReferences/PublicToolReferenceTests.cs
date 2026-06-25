// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using KM.Core.Files;
using KM.Formats.SV;
using KM.Formats.ZA;
using KM.SV.Data;
using KM.SV.Raids;
using KM.SV.Workflows;
using KM.ZA.Data;
using KM.ZA.Workflows;
using Xunit;

namespace KM.Integration.Tests.PublicReferences;

public sealed class PublicToolReferenceTests
{
    private static readonly IReadOnlyDictionary<string, ulong> SvPokeDocsFileHashes =
        new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [SvDataPaths.PersonalArray] = 0x68AB38E2CF1281ED,
            [SvDataPaths.MoveDataArray] = 0xA90026235522C2F0,
            [SvDataPaths.ItemDataArray] = 0x7083A88255C22977,
            [SvDataPaths.FriendlyShopLineupDataArray] = 0xD04EA642FBFB7769,
            [SvDataPaths.ShopWazaMachineDataArray] = 0x7B5CE225F89570CD,
            [SvDataPaths.TrainerDataArray] = 0xC9BA95BBE9B76E05,
            [SvDataPaths.WildEncounterArray] = 0x9486A89DCC92E372,
            [SvDataPaths.FixedSymbolTableArray] = 0x234255A39BAF5244,
            [SvDataPaths.EventBattlePokemonArray] = 0xF792EB37C1E3B3B6,
            [SvDataPaths.EventAddPokemonArray] = 0xE3B58FB2ED22126F,
            [SvDataPaths.EventTradeListArray] = 0x8977252CE8EC5854,
            [SvDataPaths.EventTradePokemonArray] = 0xA938D7AAAA1A35C6,
            [SvDataPaths.TeraRaidEnemyPaldea1] = 0xBC2A2B9D82370878,
            [SvDataPaths.TeraRaidEnemyPaldea2] = 0x67BB6C28CC60C33C,
            [SvDataPaths.TeraRaidEnemyPaldea3] = 0xB59E9CBC4F19694C,
            [SvDataPaths.TeraRaidEnemyPaldea4] = 0x42E371879886A028,
            [SvDataPaths.TeraRaidEnemyPaldea5] = 0x5AB7B309EBA3DCD0,
            [SvDataPaths.TeraRaidEnemyPaldea6] = 0x5A2AF8ECD6039DEC,
            [SvDataPaths.TeraRaidEnemyKitakami1] = 0x20B8CB8E39D08268,
            [SvDataPaths.TeraRaidEnemyKitakami2] = 0x5FCA21488A80F998,
            [SvDataPaths.TeraRaidEnemyKitakami3] = 0x20E6F89505475F2C,
            [SvDataPaths.TeraRaidEnemyKitakami4] = 0xF6292BAE7E8BBA14,
            [SvDataPaths.TeraRaidEnemyKitakami5] = 0xC23C1C0EC2A745E0,
            [SvDataPaths.TeraRaidEnemyKitakami6] = 0x7F3A85F4949F86A0,
            [SvDataPaths.TeraRaidEnemyBlueberry1] = 0xE82D1B672D343818,
            [SvDataPaths.TeraRaidEnemyBlueberry2] = 0x3CC8AF402105C860,
            [SvDataPaths.TeraRaidEnemyBlueberry3] = 0x32267F0B9FAB7B74,
            [SvDataPaths.TeraRaidEnemyBlueberry4] = 0x1E670794ED910E8C,
            [SvDataPaths.TeraRaidEnemyBlueberry5] = 0x2B96E03ED3136798,
            [SvDataPaths.TeraRaidEnemyBlueberry6] = 0x9FD858B587315EF0,
            [SvDataPaths.TeraRaidEnemyDelivery] = 0xF2B8A70349CE67A8,
            [SvDataPaths.TeraRaidFixedRewardItemArray] = 0x7A2389960F2B1994,
            [SvDataPaths.TeraRaidLotteryRewardItemArray] = 0x792210AB0D8E1746,
            [SvDataPaths.HiddenItemDataTableArray] = 0xD26F07890671C14B,
            [SvDataPaths.HiddenItemDataTableSu1Array] = 0x934E37FF264F9B53,
            [SvDataPaths.HiddenItemDataTableSu2Array] = 0x001B78B7E1CE742F,
            [SvDataPaths.HiddenItemDataTableLcArray] = 0xF09C18685CF3C2AF,
            [SvDataPaths.RummagingItemDataTableArray] = 0x1B32D55B3F81CD59,
            [SvDataPaths.VisibleItemScenePaldeaScarlet] = 0x340E8C102238CD9A,
            [SvDataPaths.VisibleItemScenePaldeaViolet] = 0xCDFA7B2F41A458A5,
            [SvDataPaths.VisibleItemSceneKitakamiScarlet] = 0xF79A479D93DAB0D2,
            [SvDataPaths.VisibleItemSceneKitakamiViolet] = 0xE33DDE5E8F8CAF7D,
            [SvDataPaths.VisibleItemSceneBlueberryScarlet] = 0x1669A9F6DF329BA2,
            [SvDataPaths.VisibleItemSceneBlueberryViolet] = 0xB0F16B352A50B04D,
        };

    private static readonly IReadOnlyDictionary<string, ulong> ZaPokeDocsFileHashes =
        new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [ZaDataPaths.PersonalArray] = 0x68AB38E2CF1281ED,
            [ZaDataPaths.MoveDataArray] = 0xA90026235522C2F0,
            [ZaDataPaths.ItemDataArray] = 0x1D68DB6DDE838D23,
            [ZaDataPaths.TrainerDataArray] = 0xAF90EA15CFA38004,
            [ZaDataPaths.PokemonDataArray] = 0xFC4AFA9B03D9E490,
            [ZaDataPaths.EncountDataArray] = 0x2121C649746E6EAB,
            [ZaDataPaths.PokemonSpawnerDataArray] = 0x56C7548BF894A85A,
            [ZaDataPaths.PokemonSpawnerTransformArray] = 0x09434CAAD959037C,
            [ZaDataPaths.ItemBallSpawnerDataArray] = 0x58CF93C261929B97,
            [ZaDataPaths.ItemBallSpawnerTransformArray] = 0x7ABED9219E495DE5,
            [ZaDataPaths.RandomPopItemSpawnerDataArray] = 0x8C743A7B19DE1A94,
            [ZaDataPaths.BattleTrainerSpawnerDataArray] = 0x1EB1F625C4A2A1BF,
            [ZaDataPaths.ShopItemArray] = 0x791D09E087C7C72F,
            [ZaDataPaths.ShopItemLineupArray] = 0xFED7C9E73E3ABE19,
            [ZaDataPaths.ShopDressUpArray] = 0x3F0E131140ABD720,
            [ZaDataPaths.ShopDressUpLineupArray] = 0x55B6280F7D2F05F1,
            [ZaDataPaths.ShopHairMakeLineupArray] = 0xA67251B40BD0CCBF,
            [ZaDataPaths.DressUpDataArray] = 0x96CADE1EE819A31D,
            [ZaDataPaths.HairMakeDataArray] = 0x1BD5F4245561F129,
        };

    private static readonly string[] SvMessageLanguages =
    [
        "English",
        "Spanish",
        "French",
        "German",
        "Italian",
        "JPN",
        "JPN_KANJI",
        "Korean",
        "Simp_Chinese",
        "Trad_Chinese",
    ];

    private static readonly string[] ZaMessageLanguages =
    [
        "English",
        "Spanish",
        "French",
        "German",
        "Italian",
        "JPN",
        "JPN_KANJI",
        "Korean",
        "LATAM",
        "Simp_Chinese",
        "Trad_Chinese",
    ];

    [Fact]
    public void ScarletVioletDataPathsMatchPokeDocsFileHashes()
    {
        AssertDataPathHashes(
            EnumerateDataPathConstants(typeof(SvDataPaths), IsScarletVioletDataPath),
            SvPokeDocsFileHashes,
            SvTrinityPathHasher.HashPath);
    }

    [Fact]
    public void PokemonLegendsZADataPathsMatchPokeDocsFileHashes()
    {
        AssertDataPathHashes(
            EnumerateDataPathConstants(typeof(ZaDataPaths), IsPokemonLegendsZADataPath),
            ZaPokeDocsFileHashes,
            ZaTrinityPathHasher.HashPath);
    }

    [Fact]
    public void ScarletVioletMessageLanguagesMatchPokeDocsFolders()
    {
        Assert.Equal(SvMessageLanguages, SvGameTextLanguage.SupportedMessageLanguages);
        Assert.Equal("Italian", SvGameTextLanguage.Resolve("it"));
        Assert.Equal("JPN", SvGameTextLanguage.Resolve("ja"));
        Assert.Equal("JPN_KANJI", SvGameTextLanguage.Resolve("japanese-kanji"));
        Assert.Equal("Korean", SvGameTextLanguage.Resolve("ko"));
        Assert.Equal("Simp_Chinese", SvGameTextLanguage.Resolve("zh-Hans"));
        Assert.Equal("Trad_Chinese", SvGameTextLanguage.Resolve("zh-Hant"));

        foreach (var language in SvMessageLanguages)
        {
            Assert.Contains(SvDataPaths.ItemNames(language), SvCacheManager.WarmupVirtualPaths);
            Assert.Contains(SvDataPaths.TrainerTypeKeys(language), SvCacheManager.WarmupVirtualPaths);
            Assert.Contains($"message/dat/{language}/common/itemname.dat", ScarletVioletKnownRomFsFiles.Paths);
            Assert.Contains($"message/dat/{language}/common/trtype.tbl", ScarletVioletKnownRomFsFiles.Paths);
        }
    }

    [Fact]
    public void PokemonLegendsZAMessageLanguagesMatchPokeDocsAndTextportFolders()
    {
        Assert.Equal(ZaMessageLanguages, ZaGameTextLanguage.SupportedMessageLanguages);
        Assert.Equal("Italian", ZaGameTextLanguage.Resolve("it"));
        Assert.Equal("JPN", ZaGameTextLanguage.Resolve("ja"));
        Assert.Equal("JPN_KANJI", ZaGameTextLanguage.Resolve("japanese-kanji"));
        Assert.Equal("Korean", ZaGameTextLanguage.Resolve("ko"));
        Assert.Equal("LATAM", ZaGameTextLanguage.Resolve("es-419"));
        Assert.Equal("Simp_Chinese", ZaGameTextLanguage.Resolve("zh-Hans"));
        Assert.Equal("Trad_Chinese", ZaGameTextLanguage.Resolve("zh-Hant"));

        foreach (var language in ZaMessageLanguages)
        {
            Assert.Contains(ZaDataPaths.ItemNames(language), ZaCacheManager.WarmupVirtualPaths);
            Assert.Contains(ZaDataPaths.PlaceNameKeys(language), ZaCacheManager.WarmupVirtualPaths);
            Assert.Contains(ZaDataPaths.TrainerNameKeys(language), ZaCacheManager.WarmupVirtualPaths);
            Assert.Contains(ZaDataPaths.TrainerTypeKeys(language), ZaCacheManager.WarmupVirtualPaths);
        }
    }

    [Fact]
    public void ScarletVioletRaidSourcesMatchPublicRaidToolLayout()
    {
        var sources = SvTeraRaidsWorkflowService.EnemySourceDefinitions;

        Assert.Equal(19, sources.Count);
        Assert.Equal(
            Enumerable.Range(1, 6).Select(rank => $"paldea-{rank}")
                .Concat(Enumerable.Range(1, 6).Select(rank => $"kitakami-{rank}"))
                .Concat(Enumerable.Range(1, 6).Select(rank => $"blueberry-{rank}"))
                .Append("delivery"),
            sources.Select(source => source.SourceKey));
        Assert.All(sources.Take(6), source => Assert.Equal("Paldea", source.Region));
        Assert.All(sources.Skip(6).Take(6), source => Assert.Equal("Kitakami", source.Region));
        Assert.All(sources.Skip(12).Take(6), source => Assert.Equal("Blueberry", source.Region));
        Assert.Null(sources[^1].StarRank);

        foreach (var group in sources.Take(18).Chunk(6))
        {
            Assert.Equal(Enumerable.Range(1, 6), group.Select(source => source.StarRank!.Value));
        }

        foreach (var source in sources)
        {
            Assert.True(SvPokeDocsFileHashes.ContainsKey(source.VirtualPath), source.VirtualPath);
        }

        Assert.Contains(SvDataPaths.TeraRaidFixedRewardItemArray, SvCacheManager.WarmupVirtualPaths);
        Assert.Contains(SvDataPaths.TeraRaidLotteryRewardItemArray, SvCacheManager.WarmupVirtualPaths);
    }

    private static void AssertDataPathHashes(
        IReadOnlyList<string> dataPaths,
        IReadOnlyDictionary<string, ulong> expectedHashes,
        Func<string, ulong> hash)
    {
        Assert.Equal(
            expectedHashes.Keys.OrderBy(path => path, StringComparer.Ordinal),
            dataPaths.OrderBy(path => path, StringComparer.Ordinal));
        foreach (var path in dataPaths)
        {
            Assert.Equal(expectedHashes[path], hash(path));
        }
    }

    private static IReadOnlyList<string> EnumerateDataPathConstants(
        Type type,
        Func<string, bool> include)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(include)
            .ToArray();
    }

    private static bool IsScarletVioletDataPath(string path)
    {
        return path.StartsWith("avalon/", StringComparison.Ordinal)
            || path.StartsWith("world/", StringComparison.Ordinal);
    }

    private static bool IsPokemonLegendsZADataPath(string path)
    {
        return path.StartsWith("avalon/", StringComparison.Ordinal)
            || path.StartsWith("world/", StringComparison.Ordinal);
    }
}
