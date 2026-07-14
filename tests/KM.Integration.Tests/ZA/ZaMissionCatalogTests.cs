// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaMissionCatalogTests
{
    [Fact]
    public void CatalogContainsEveryOfficialMissionNumber()
    {
        Assert.Equal(Enumerable.Range(1, 42), ZaMissionCatalog.MainMissions.Select(mission => mission.Number));
        Assert.Equal(Enumerable.Range(1, 14), ZaMissionCatalog.HyperspaceMissions.Select(mission => mission.Number));
        Assert.Equal(Enumerable.Range(1, 200), ZaMissionCatalog.NumberedSideMissions.Select(mission => mission.Number));
        Assert.Equal(Enumerable.Range(1, 3), ZaMissionCatalog.ExtraSideMissions.Select(mission => mission.Number));
    }

    [Fact]
    public void InternalSideIdsCoverTheCompleteQuestMetadataRange()
    {
        var internalIds = ZaMissionCatalog.AllSideMissions
            .Select(mission => Assert.IsType<int>(mission.InternalSideId))
            .Order()
            .ToArray();

        Assert.Equal(Enumerable.Range(1, 203), internalIds);
    }

    [Fact]
    public void InternalSideIdsResolveToDisplayedMissionNumbers()
    {
        Assert.True(ZaMissionCatalog.TryGetSideMissionByInternalId(84, out var restaurant));
        Assert.Equal(ZaMissionKind.Side, restaurant.Kind);
        Assert.Equal(73, restaurant.Number);
        Assert.Equal("Side Mission 73", restaurant.DisplayReference);
        Assert.Equal(85, restaurant.TitleMessageIndex);
        Assert.Equal("Full Course of Battles: High Rolling", restaurant.EnglishTitle);

        Assert.True(ZaMissionCatalog.TryGetSideMissionByInternalId(147, out var dodger));
        Assert.Equal(ZaMissionKind.Side, dodger.Kind);
        Assert.Equal(173, dodger.Number);
        Assert.Equal(148, dodger.TitleMessageIndex);
        Assert.Equal("Be a Defenseless Dodger!", dodger.EnglishTitle);

        Assert.True(ZaMissionCatalog.TryGetNumberedSideMission(147, out var gulpin));
        Assert.Equal(134, gulpin.InternalSideId);
        Assert.Equal(135, gulpin.TitleMessageIndex);
        Assert.Equal("Our Gluttonous Gulpin", gulpin.EnglishTitle);
    }

    [Fact]
    public void ExtraSideMissionsShareTheInternalSideIdNamespace()
    {
        Assert.True(ZaMissionCatalog.TryGetSideMissionByInternalId(119, out var ex1));
        Assert.Equal(ZaMissionKind.ExtraSide, ex1.Kind);
        Assert.Equal(1, ex1.Number);
        Assert.Equal("Side Mission EX1", ex1.DisplayReference);
        Assert.Equal("Shine Bright like a Gemstone", ex1.EnglishTitle);

        Assert.True(ZaMissionCatalog.TryGetSideMissionByInternalId(120, out var ex2));
        Assert.Equal(ZaMissionKind.ExtraSide, ex2.Kind);
        Assert.Equal(2, ex2.Number);

        Assert.True(ZaMissionCatalog.TryGetSideMissionByInternalId(203, out var ex3));
        Assert.Equal(ZaMissionKind.ExtraSide, ex3.Kind);
        Assert.Equal(3, ex3.Number);
        Assert.Equal("Raging Lightning", ex3.EnglishTitle);
    }

    [Fact]
    public void InternalIdWithoutAStandaloneScriptStillResolvesFromQuestMetadata()
    {
        Assert.True(ZaMissionCatalog.TryGetSideMissionByInternalId(122, out var mission));
        Assert.Equal(ZaMissionKind.Side, mission.Kind);
        Assert.Equal(120, mission.Number);
        Assert.Equal(123, mission.TitleMessageIndex);
        Assert.Equal("Donuts of Unworldly Deliciousness!", mission.EnglishTitle);
    }

    [Fact]
    public void MissionMetadataPointsAtLocalizedQuestTitleTables()
    {
        Assert.True(ZaMissionCatalog.TryGetMainMission(19, out var main));
        Assert.Equal(ZaMissionTitleTable.QuestListMain, main.TitleTable);
        Assert.Equal(21, main.TitleMessageIndex);
        Assert.Equal("Reaching Rank D", main.EnglishTitle);

        Assert.True(ZaMissionCatalog.TryGetHyperspaceMission(7, out var hyperspace));
        Assert.Equal(ZaMissionTitleTable.QuestListDlc, hyperspace.TitleTable);
        Assert.Equal(7, hyperspace.TitleMessageIndex);
        Assert.Equal("Naveen’s Not OK", hyperspace.EnglishTitle);

        Assert.True(ZaMissionCatalog.TryGetNumberedSideMission(73, out var side));
        Assert.Equal(ZaMissionTitleTable.QuestListSub, side.TitleTable);
    }

    [Fact]
    public void LocalizedTitleWinsAndEnglishTitleIsOnlyTheFallback()
    {
        Assert.True(ZaMissionCatalog.TryGetNumberedSideMission(73, out var mission));

        Assert.Equal("Localized mission title", mission.ResolveTitle("  Localized mission title  "));
        Assert.Equal(mission.EnglishTitle, mission.ResolveTitle(null));
        Assert.Equal(mission.EnglishTitle, mission.ResolveTitle("[VAR BDFF(0055)]"));
        Assert.False(mission.ResolveTitle(null).StartsWith("Side Mission", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(204)]
    public void UnknownInternalSideIdsAreRejected(int internalId)
    {
        Assert.False(ZaMissionCatalog.TryGetSideMissionByInternalId(internalId, out _));
    }
}
