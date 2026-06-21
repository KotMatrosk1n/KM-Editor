// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Integration.Tests.Tools;
using KM.SV.ExeFs;
using KM.SV.TypeChart;
using Xunit;

namespace KM.Integration.Tests.SV;

public sealed class SvTypeChartWorkflowTests
{
    [Theory]
    [InlineData(ProjectGame.Scarlet)]
    [InlineData(ProjectGame.Violet)]
    public void AnalyzeFindsVanillaSelectedGameTypeChart(ProjectGame game)
    {
        var main = SvTypeChartBridgeFixtures.CreateCompatibleMain(game);

        var analysis = SvTypeChartMainPatcher.Analyze(main, game);

        Assert.Equal(SvTypeChartMainKind.Vanilla, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal("main.ro+0x0082286C", analysis.ChartOffsetHex);
        Assert.Equal(SvTypeChartMainPatcher.VanillaChartValues, analysis.EffectivenessValues);
    }

    [Fact]
    public void AnalyzeRejectsSelectedGameMismatch()
    {
        var main = SvTypeChartBridgeFixtures.CreateCompatibleMain(ProjectGame.Scarlet);

        var analysis = SvTypeChartMainPatcher.Analyze(main, ProjectGame.Violet);

        Assert.Equal(SvTypeChartMainKind.GameMismatch, analysis.Kind);
        Assert.Equal(ProjectGame.Scarlet, analysis.DetectedGame);
        Assert.Contains("separate verified build IDs", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Scarlet)]
    [InlineData(ProjectGame.Violet)]
    public void ApplyChartPatchesOnlyReservedRoChartBytes(ProjectGame game)
    {
        var main = SvTypeChartBridgeFixtures.CreateCompatibleMain(game);
        var before = SwShNsoFile.Parse(main);
        var values = SvTypeChartMainPatcher.VanillaChartValues.ToArray();
        values[0] = 0;
        values[(1 * SvTypeChartMainPatcher.TypeCount) + 4] = 2;

        var patched = SvTypeChartMainPatcher.ApplyChart(main, values, game);
        var after = SwShNsoFile.Parse(patched);
        var analysis = SvTypeChartMainPatcher.Analyze(patched, game);

        Assert.Equal(SvTypeChartMainKind.Modified, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal(values, analysis.EffectivenessValues);
        Assert.Equal(before.Text.DecompressedData, after.Text.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.All(
            ChangedOffsets(before.Ro.DecompressedData, after.Ro.DecompressedData),
            changedOffset => Assert.Contains(
                SvTypeChartMainPatcher.ReservedMainRoRegions(),
                region => SvExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    [Fact]
    public void TypeChartReservesTheVerifiedMainRoRange()
    {
        var region = Assert.Single(SvExeFsReservedRegionLedger.MainRoRegionsForOwner(SvExeFsReservedRegionLedger.OwnerTypeChart));

        Assert.Equal("Type Chart", region.Owner);
        Assert.Equal("type-chart-sv", region.FeatureId);
        Assert.Equal(SvExeFsReservedRegionLedger.ExeFsMainPath, region.RelativePath);
        Assert.Equal("main.ro", region.Area);
        Assert.Equal(0x0082286C, region.StartOffset);
        Assert.Equal(0x144, region.Length);
    }

    [Fact]
    public void ReservedMainRegionsDoNotOverlapWithinTheSameNsoArea()
    {
        var regions = SvExeFsReservedRegionLedger.Regions
            .Where(region => string.Equals(region.RelativePath, SvExeFsReservedRegionLedger.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();

        foreach (var group in regions.GroupBy(region => region.Area, StringComparer.Ordinal))
        {
            var areaRegions = group.ToArray();
            for (var leftIndex = 0; leftIndex < areaRegions.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < areaRegions.Length; rightIndex++)
                {
                    var left = areaRegions[leftIndex];
                    var right = areaRegions[rightIndex];

                    Assert.False(
                        SvExeFsReservedRegionLedger.Overlaps(left, right.StartOffset!.Value, right.Length!.Value),
                        $"{left.Owner}/{left.FeatureId} overlaps {right.Owner}/{right.FeatureId} in {group.Key}.");
                }
            }
        }
    }

    private static int[] ChangedOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(index => before[index] != after[index])
            .ToArray();
    }
}
