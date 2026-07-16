// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventureSeedPlanningServiceTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    public void PredictRejectsFormsThatDoNotExistForTheSpecies(int entryIndex, int form)
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
            [
                new(entryIndex, SwShDynamaxAdventureField.Form, form),
            ]));
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();

        var plan = service.Predict(temp.Paths, seed: 0, npcCount: 0);

        Assert.Empty(plan.Rentals);
        Assert.Empty(plan.Encounters);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == SwShDynamaxAdventuresWorkflowService.FormField
            && diagnostic.Message.Contains("form does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void PredictUsesLayeredAdventureTableRows()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp, count: 800);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
            [
                new(1, SwShDynamaxAdventureField.Species, 467),
            ]));
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();

        var plan = service.Predict(temp.Paths, seed: 0, npcCount: 0, requiredRows: [1]);

        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(plan.Rentals.Concat(plan.Encounters), template =>
            template.Row == 1
            && template.Species == 467);
        Assert.Contains(plan.RequiredRowPositions, position => position.Row == 1);
    }

    [Fact]
    public void SearchRowsFindsSeedAgainstActiveAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();
        var target = service.Predict(temp.Paths, seed: 0, npcCount: 0).Encounters[0].Row;

        var search = service.SearchRows(
            temp.Paths,
            requiredRows: [target],
            npcCount: 0,
            startSeed: 0,
            limit: 1,
            maxResults: 1);

        var result = Assert.Single(search.Results);
        Assert.Equal(0UL, result.Seed);
        Assert.Contains(result.Positions, position => position.Row == target);
        Assert.DoesNotContain(search.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PredictReturnsDiagnosticWhenPersonalDataIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp, includeDependencies: false);
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();

        var plan = service.Predict(temp.Paths, seed: 0, npcCount: 0);

        Assert.Empty(plan.Rentals);
        Assert.Empty(plan.Encounters);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("personal data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchRowsRejectsRowsOutsideLoadedAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();

        var search = service.SearchRows(
            temp.Paths,
            requiredRows: [99],
            npcCount: 0,
            startSeed: 0,
            limit: 100,
            maxResults: 1);

        Assert.Empty(search.Results);
        Assert.Contains(search.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("outside the loaded table", StringComparison.Ordinal));
    }

    [Fact]
    public void PredictWarnsWhenRequiredRowsIncludeBossRows()
    {
        using var temp = TemporarySwShProject.Create();
        WriteSeedPlanningDynamaxAdventures(temp, rowCount: 230);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp, count: 400);
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();

        var plan = service.Predict(temp.Paths, seed: 0, npcCount: 0, requiredRows: [226]);

        Assert.NotEmpty(plan.Rentals);
        Assert.NotEmpty(plan.Encounters);
        Assert.Empty(plan.RequiredRowPositions);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.Message.Contains("cannot select boss row(s) 226", StringComparison.Ordinal));
    }

    [Fact]
    public void PredictWarnsWhenRequiredRowsAreOutsideLoadedAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp);
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();

        var plan = service.Predict(temp.Paths, seed: 0, npcCount: 0, requiredRows: [99]);

        Assert.NotEmpty(plan.Rentals);
        Assert.NotEmpty(plan.Encounters);
        Assert.Empty(plan.RequiredRowPositions);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.Message.Contains("outside the loaded table", StringComparison.Ordinal));
    }

    [Fact]
    public void SearchRowsRejectsBossRows()
    {
        using var temp = TemporarySwShProject.Create();
        WriteSeedPlanningDynamaxAdventures(temp, rowCount: 230);
        SwShDynamaxAdventureTestFixtures.WriteBasePersonalData(temp, count: 400);
        var service = SwShDynamaxAdventureSeedPlanningService.CreateForSyntheticTests();

        var search = service.SearchRows(
            temp.Paths,
            requiredRows: [226],
            npcCount: 0,
            startSeed: 0,
            limit: 100,
            maxResults: 1);

        Assert.Empty(search.Results);
        Assert.Contains(search.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot select boss row(s) 226", StringComparison.Ordinal));
    }

    private static void WriteSeedPlanningDynamaxAdventures(TemporarySwShProject temp, int rowCount)
    {
        temp.SelectedGame = KM.Core.Projects.ProjectGame.Sword;
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(
                Enumerable.Range(0, rowCount).Select(row => new SwShDynamaxAdventureRecord(
                    row,
                    IsSingleCapture: row >= SwShDynamaxAdventureSeedPlanner.DefaultBossStartRow,
                    SingleCaptureFlagBlock: (ulong)(row + 1),
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: row + 1,
                    Level: row >= SwShDynamaxAdventureSeedPlanner.DefaultBossStartRow ? 70 : 65,
                    Species: row + 1,
                    UiMessageId: (ulong)(row + 1),
                    OtGender: 1,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4])).ToArray()).Write());
    }
}
