// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.FpsPatch;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.FpsPatch;

public sealed class SwShFpsPatchServiceTests
{
    [Fact]
    public void ApplyAndRestoreSupportsShieldMainAndRomFsOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Shield };
        temp.WriteBaseExeFsFile("main", SwShFpsMainTestAnchors.CreateMain(ProjectGame.Shield));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Shield);
        SwShFpsRomFsTestFixtures.WriteCompleteManagedBaseRomFs(temp);
        var service = new SwShFpsPatchService();

        var apply = service.Apply(paths);

        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("installed", apply.Status.Status);
        Assert.Equal(ProjectGame.Shield, apply.Status.DetectedGame);
        Assert.Equal(15, apply.Status.PatchedMainSiteCount);
        Assert.Equal(15, apply.Status.MainSiteCount);
        Assert.Equal(1019, apply.Status.PatchedRomFsFileCount);
        Assert.Equal(1019, apply.Status.ManagedRomFsFileCount);
        Assert.Equal(0, apply.Status.ConflictingRomFsFileCount);
        Assert.Equal(1020, apply.ApplyResult.WrittenFiles.Count);

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        Assert.True(File.Exists(outputMainPath));
        var mainAnalysis = SwShFpsMainPatcher.Analyze(File.ReadAllBytes(outputMainPath), ProjectGame.Shield);
        Assert.Equal(SwShFpsPatchMainKind.Installed, mainAnalysis.Kind);
        Assert.Equal(ProjectGame.Shield, mainAnalysis.DetectedGame);

        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ew052.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/eg_ball01.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ee316.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ee411.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/demo/sequence/d010.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/d230.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/archive/demo/share/anime/a_pl0110.gfpak"));
        Assert.Equal(23u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee316.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee411.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "eg_ball01.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "d230.bseq")));

        var restore = service.Restore(paths);

        Assert.DoesNotContain(restore.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("notInstalled", restore.Status.Status);
        Assert.Equal(ProjectGame.Shield, restore.Status.DetectedGame);
        Assert.Equal(0, restore.Status.PatchedMainSiteCount);
        Assert.Equal(0, restore.Status.PatchedRomFsFileCount);
        Assert.Equal(1019, restore.Status.ManagedRomFsFileCount);
        Assert.False(File.Exists(outputMainPath));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ew052.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "eg_ball01.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee411.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "demo", "sequence", "d010.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "d230.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "archive", "demo", "share", "anime", "a_pl0110.gfpak")));
    }

    private static uint ReadFrameCount(string path)
    {
        var data = File.ReadAllBytes(path);
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C, sizeof(uint)));
    }
}
