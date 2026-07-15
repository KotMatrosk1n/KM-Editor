// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.GameDump;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.GameDump;

public sealed class SwShGameDumpServiceTests
{
    [Fact]
    public void LoadDescribesRaidRewardCategoriesByTheirActualPerStarValues()
    {
        using var temp = TemporarySwShProject.Create();

        var workflow = new SwShGameDumpService().Load(
            temp.Paths with { SelectedGame = ProjectGame.Sword });

        var raidRewards = workflow.Categories.Single(category =>
            category.Id == SwShWorkflowIds.RaidRewards);
        var raidBonusRewards = workflow.Categories.Single(category =>
            category.Id == SwShWorkflowIds.RaidBonusRewards);

        Assert.Equal(
            "Raid drop reward tables, items, per-star drop chances, and provenance.",
            raidRewards.Description);
        Assert.Equal(
            "Raid bonus reward tables, items, per-star quantities, and provenance.",
            raidBonusRewards.Description);
    }
}
