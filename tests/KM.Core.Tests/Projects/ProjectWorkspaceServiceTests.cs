// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Tests;
using Xunit;

namespace KM.Core.Tests.Projects;

public sealed class ProjectWorkspaceServiceTests
{
    [Fact]
    public void OpenReturnsProjectSnapshotWithHealthAndFileGraph()
    {
        using var temp = TemporaryProjectFolders.Create();
        var openedAt = new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero);
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile("romfs/data/items.bin", "layered-items");

        var project = new ProjectWorkspaceService().Open(temp.Paths, openedAt);

        Assert.False(string.IsNullOrWhiteSpace(project.Id.Value));
        Assert.Equal(openedAt, project.OpenedAt);
        Assert.Equal(ProjectHealthState.EditableReady, project.Health.State);
        Assert.Equal(project.Health.FileGraph, project.FileGraph.ToSummary());
        Assert.Contains(
            project.FileGraph.Entries,
            entry => entry.RelativePath == "romfs/data/items.bin"
                && entry.State == ProjectFileGraphEntryState.LayeredOverride);
    }

    [Fact]
    public void OpenKeepsReadOnlyProjectGraphWhenOutputRootIsMissing()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");

        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        Assert.Equal(ProjectHealthState.ReadOnlyReady, project.Health.State);
        Assert.All(project.FileGraph.Entries, entry => Assert.Null(entry.LayeredFile));
        Assert.Equal(2, project.FileGraph.ToSummary().BaseFileCount);
    }

    [Fact]
    public void RefreshFileGraphReturnsEmptyGraphWhenBasePathsCannotOpen()
    {
        using var temp = TemporaryProjectFolders.Create();

        var graph = new ProjectWorkspaceService().RefreshFileGraph(
            temp.Paths with { BaseRomFsPath = Path.Combine(temp.RootPath, "missing-romfs") });

        Assert.Empty(graph.Entries);
    }

    [Fact]
    public void RefreshFileGraphOmitsUnsafeOutputRoot()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");

        var graph = new ProjectWorkspaceService().RefreshFileGraph(
            temp.Paths with { OutputRootPath = temp.BaseRomFsPath });

        Assert.All(graph.Entries, entry => Assert.Null(entry.LayeredFile));
        Assert.Equal(2, graph.ToSummary().BaseFileCount);
    }
}

