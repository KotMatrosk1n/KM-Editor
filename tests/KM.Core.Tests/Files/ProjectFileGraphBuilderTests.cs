// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Tests;
using Xunit;

namespace KM.Core.Tests.Files;

public sealed class ProjectFileGraphBuilderTests
{
    [Fact]
    public void BuildPrefixesBaseRootsAndMarksLayeredOverrides()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile("romfs/data/items.bin", "layered-items");
        temp.WriteOutputFile("romfs/data/extra.bin", "layered-extra");

        var graph = new ProjectFileGraphBuilder().Build(temp.Paths);

        var entriesByPath = graph.Entries.ToDictionary(entry => entry.RelativePath);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, entriesByPath["romfs/data/items.bin"].State);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, entriesByPath["exefs/main"].State);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOnly, entriesByPath["romfs/data/extra.bin"].State);

        var summary = graph.ToSummary();
        Assert.Equal(2, summary.BaseFileCount);
        Assert.Equal(2, summary.LayeredFileCount);
        Assert.Equal(1, summary.OverrideCount);
        Assert.Equal(1, summary.LayeredOnlyCount);
    }

    [Fact]
    public void BuildIgnoresMissingOptionalOutputRoot()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");

        var paths = temp.Paths with { OutputRootPath = Path.Combine(temp.RootPath, "missing-output") };

        var graph = new ProjectFileGraphBuilder().Build(paths);

        Assert.All(graph.Entries, entry => Assert.NotEqual(ProjectFileGraphEntryState.LayeredOnly, entry.State));
        Assert.Equal(2, graph.ToSummary().BaseFileCount);
        Assert.Equal(0, graph.ToSummary().LayeredFileCount);
    }
}
