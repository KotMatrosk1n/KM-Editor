// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
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
        Assert.Equal(1066, apply.Status.PatchedRomFsFileCount);
        Assert.Equal(1066, apply.Status.ManagedRomFsFileCount);
        Assert.Equal(0, apply.Status.ConflictingRomFsFileCount);
        Assert.Equal(1067, apply.ApplyResult.WrittenFiles.Count);

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        Assert.True(File.Exists(outputMainPath));
        var mainAnalysis = SwShFpsMainPatcher.Analyze(File.ReadAllBytes(outputMainPath), ProjectGame.Shield);
        Assert.Equal(SwShFpsPatchMainKind.Installed, mainAnalysis.Kind);
        Assert.Equal(ProjectGame.Shield, mainAnalysis.DetectedGame);

        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ew052.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/eg_ball01.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ee101.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ee316.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ee400.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ee411.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/ee630.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/demo/sequence/d010.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/demo/sequence/d030.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/demo/sequence/r2d020.bseq"));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TitleDemoRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.TitleDemoRelativePath));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/battle/waza/sequence/d230.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.BattleCameraRelativePath));
        Assert.True(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.BattleUiArchiveRelativePath));
        Assert.True(service.IsGeneratedRomFsOutput(paths, "romfs/bin/archive/demo/share/anime/a_pl0110.gfpak"));
        Assert.True(service.IsGeneratedRomFsOutput(paths, SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.ExcludedBallSystemCameraRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.TrainerThrowLooseRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.PlayerThrowCameraRelativePath));
        Assert.False(service.IsGeneratedRomFsOutput(paths, SwShFpsRomFsTestFixtures.PlayerThrowBattleModelRelativePath));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee101.bseq")));
        Assert.Equal(23u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee316.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee400.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee411.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee630.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "eg_ball01.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "d230.bseq")));
        Assert.Equal(20u, ReadFrameCount(Path.Combine(temp.OutputRootPath, "romfs", "bin", "demo", "sequence", "d030.bseq")));
        var battleCameraInfo = SwShFpsBattleCameraPatcher.InspectAnimation(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.BattleCameraRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(91u, battleCameraInfo.KeyFrames);
        Assert.Equal(30u, battleCameraInfo.FrameRate);
        var battleUiArchive = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.BattleUiArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var keySelectAnimation = Assert.Single(SwShFpsUiKeySelectPatcher.InspectArchive(battleUiArchive));
        Assert.Equal(4, keySelectAnimation.StartFrame);
        Assert.Equal(8, keySelectAnimation.EndFrame);
        Assert.Contains(0.0f, keySelectAnimation.FrameKeys);
        Assert.Contains(4.0f, keySelectAnimation.FrameKeys);
        var recoveryArchive = SwShGfPackFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(
            24u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                recoveryArchive.GetFileByName("unit_obj_pc_recovery01_main01_ballput.gfbanm")).FrameRate);
        Assert.Equal(
            24u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                recoveryArchive.GetFileByName("unit_obj_pc_recovery01_main01_recovery.gfbanm")).FrameRate);
        Assert.Equal(
            24u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                recoveryArchive.GetFileByName("unit_obj_pc_recovery01_ballflash01_recovery.gfbanm")).FrameRate);
        Assert.Equal(
            30u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                recoveryArchive.GetFileByName("unit_obj_pc_recovery01_text01_open.gfbanm")).FrameRate);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.ExcludedBallSystemCameraRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowLooseRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TitleDemoRelativePath.Replace('/', Path.DirectorySeparatorChar))));

        var restore = service.Restore(paths);

        Assert.DoesNotContain(restore.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("notInstalled", restore.Status.Status);
        Assert.Equal(ProjectGame.Shield, restore.Status.DetectedGame);
        Assert.Equal(0, restore.Status.PatchedMainSiteCount);
        Assert.Equal(0, restore.Status.PatchedRomFsFileCount);
        Assert.Equal(1066, restore.Status.ManagedRomFsFileCount);
        Assert.False(File.Exists(outputMainPath));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ew052.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "eg_ball01.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee101.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee411.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "ee630.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "demo", "sequence", "d010.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "demo", "sequence", "d030.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "demo", "sequence", "r2d020.bseq")));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.BattleCameraRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.BattleUiArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TitleDemoRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "battle", "waza", "sequence", "d230.bseq")));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "archive", "demo", "share", "anime", "a_pl0110.gfpak")));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ApplyRemovesLegacyTrainerAnimationOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        temp.WriteBaseExeFsFile("main", SwShFpsMainTestAnchors.CreateMain(ProjectGame.Sword));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        SwShFpsRomFsTestFixtures.WriteCompleteManagedBaseRomFs(temp);
        temp.WriteOutputFile(
            SwShFpsRomFsTestFixtures.TrainerThrowLooseRelativePath,
            SwShFpsLegacyTrainerThrowCleanupPatcher.ConvertLegacyOutput(
                SwShFpsRomFsTestFixtures.TrainerThrowLooseRelativePath,
                SwShFpsRomFsTestFixtures.CreateGfAnimationClip(keyFrames: 120, frameRate: 60)));
        temp.WriteOutputFile(
            SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath,
            SwShFpsLegacyTrainerThrowCleanupPatcher.ConvertLegacyOutput(
                SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath,
                SwShFpsRomFsTestFixtures.CreateTrainerThrowArchive()));
        temp.WriteOutputFile(
            SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath,
            SwShFpsTrainerThrowPatcher.ConvertAnimationToHalfSpeed(
                SwShFpsRomFsTestFixtures.CreateGfAnimationClip(keyFrames: 143, frameRate: 60)));
        temp.WriteOutputFile(
            SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath,
            SwShFpsTrainerThrowPatcher.ConvertAnimationToHalfSpeed(
                SwShFpsRomFsTestFixtures.CreateGfAnimationClip(keyFrames: 196, frameRate: 60)));
        var service = new SwShFpsPatchService();

        var apply = service.Apply(paths);

        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowLooseRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ApplyLeavesCustomLegacyBallThrowOutputInPlace()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        temp.WriteBaseExeFsFile("main", SwShFpsMainTestAnchors.CreateMain(ProjectGame.Sword));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        SwShFpsRomFsTestFixtures.WriteCompleteManagedBaseRomFs(temp);
        temp.WriteOutputFile(
            SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath,
            SwShFpsRomFsTestFixtures.CreateGfAnimationClip(keyFrames: 143, frameRate: 45));
        var service = new SwShFpsPatchService();

        var apply = service.Apply(paths);

        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(45u, ReadAnimationFrameRate(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ApplyRemovesLegacyTitleDemoOutput()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        temp.WriteBaseExeFsFile("main", SwShFpsMainTestAnchors.CreateMain(ProjectGame.Sword));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        SwShFpsRomFsTestFixtures.WriteCompleteManagedBaseRomFs(temp);
        var titleDemoSource = SwShFpsRomFsTestFixtures.CreateMoveBseq(frameCount: 10, startFrame: 2, endFrame: 4);
        var legacyTitleDemoOutput = SwShFpsBseqPatcher.Convert(
            titleDemoSource,
            SwShFpsBseqPatcher.OpeningDemoTimelineScale,
            out _);
        temp.WriteOutputFile(SwShFpsRomFsTestFixtures.TitleDemoRelativePath, legacyTitleDemoOutput);
        var service = new SwShFpsPatchService();

        var apply = service.Apply(paths);

        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TitleDemoRelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ApplyLeavesCustomTitleDemoOutputInPlace()
    {
        using var temp = TemporarySwShProject.Create();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        temp.WriteBaseExeFsFile("main", SwShFpsMainTestAnchors.CreateMain(ProjectGame.Sword));
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        SwShFpsRomFsTestFixtures.WriteCompleteManagedBaseRomFs(temp);
        temp.WriteOutputFile(
            SwShFpsRomFsTestFixtures.TitleDemoRelativePath,
            SwShFpsRomFsTestFixtures.CreateMoveBseq(frameCount: 123, startFrame: 5, endFrame: 9));
        var service = new SwShFpsPatchService();

        var apply = service.Apply(paths);

        Assert.DoesNotContain(apply.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShFpsRomFsTestFixtures.TitleDemoRelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static uint ReadFrameCount(string path)
    {
        var data = File.ReadAllBytes(path);
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C, sizeof(uint)));
    }

    private static uint ReadAnimationFrameRate(string path)
    {
        return SwShFpsTrainerThrowPatcher.InspectAnimation(File.ReadAllBytes(path)).FrameRate;
    }
}
