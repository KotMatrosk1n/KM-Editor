// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Integration.Tests.Tools;
using KM.SV.Workflows;
using KM.ZA.Workflows;
using Xunit;

namespace KM.Integration.Tests;

public sealed class LooseWorkflowOutputSelectionTests
{
    private const string VirtualPath = "world/data/test.bin";

    [Fact]
    public void ScarletVioletReadsTheLatestLooseOutput()
    {
        using var temp = TemporaryBridgeProject.Create();
        AssertLatestLooseOutput(
            temp,
            ProjectGame.Violet,
            project => new SvWorkflowFileSource(new SvCacheManager(Path.Combine(temp.RootPath, "sv-cache")))
                .Read(project, VirtualPath)
                .Bytes);
    }

    [Fact]
    public void PokemonLegendsZAReadsTheLatestLooseOutput()
    {
        using var temp = TemporaryBridgeProject.Create();
        AssertLatestLooseOutput(
            temp,
            ProjectGame.ZA,
            project => new ZaWorkflowFileSource(new ZaCacheManager(Path.Combine(temp.RootPath, "za-cache")))
                .Read(project, VirtualPath)
                .Bytes);
    }

    private static void AssertLatestLooseOutput(
        TemporaryBridgeProject temp,
        ProjectGame game,
        Func<OpenedProject, byte[]> read)
    {
        byte[] trinityModManagerBytes = [1, 2, 3];
        byte[] standaloneBytes = [4, 5, 6];
        temp.WriteOutputFile(VirtualPath, trinityModManagerBytes);
        temp.WriteOutputFile($"romfs/{VirtualPath}", standaloneBytes);

        var trinityModManagerPath = Path.Combine(
            temp.OutputRootPath,
            VirtualPath.Replace('/', Path.DirectorySeparatorChar));
        var standalonePath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            VirtualPath.Replace('/', Path.DirectorySeparatorChar));
        var baseline = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        File.SetLastWriteTimeUtc(trinityModManagerPath, baseline);
        File.SetLastWriteTimeUtc(standalonePath, baseline.AddMinutes(1));
        Assert.Equal(standaloneBytes, read(CreateProject(temp, game)));

        File.SetLastWriteTimeUtc(trinityModManagerPath, baseline.AddMinutes(2));
        Assert.Equal(trinityModManagerBytes, read(CreateProject(temp, game)));

        File.SetLastWriteTimeUtc(standalonePath, baseline.AddMinutes(2));
        Assert.Equal(trinityModManagerBytes, read(CreateProject(temp, game)));
    }

    private static OpenedProject CreateProject(TemporaryBridgeProject temp, ProjectGame game)
    {
        var graph = new ProjectFileGraph([]);
        var health = new ProjectHealth(
            ProjectHealthState.EditableReady,
            [],
            graph.ToSummary(),
            []);
        var paths = new ProjectPaths(
            temp.BaseRomFsPath,
            temp.BaseExeFsPath,
            temp.OutputRootPath,
            SaveFilePath: null,
            SelectedGame: game);

        return new OpenedProject(
            ProjectId.New(),
            paths,
            health,
            graph,
            DateTimeOffset.UtcNow);
    }
}
