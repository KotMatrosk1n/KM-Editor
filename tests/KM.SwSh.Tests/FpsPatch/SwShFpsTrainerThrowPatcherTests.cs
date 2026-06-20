// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.FpsPatch;
using Xunit;

namespace KM.SwSh.Tests.FpsPatch;

public sealed class SwShFpsTrainerThrowPatcherTests
{
    [Fact]
    public void ConvertAnimationToHalfSpeedHalvesFrameRateWithoutChangingKeyFrames()
    {
        var source = SwShFpsRomFsTestFixtures.CreateGfAnimationClip(keyFrames: 143, frameRate: 60);

        var patched = SwShFpsTrainerThrowPatcher.ConvertAnimationToHalfSpeed(source);

        var info = SwShFpsTrainerThrowPatcher.InspectAnimation(patched);
        Assert.Equal(143u, info.KeyFrames);
        Assert.Equal(30u, info.FrameRate);
    }

    [Fact]
    public void ConvertTrainerBattleArchiveOnlySlowsBallThrowClips()
    {
        var source = SwShFpsRomFsTestFixtures.CreateTrainerThrowArchive();

        var patched = SwShFpsTrainerThrowPatcher.ConvertTrainerBattleArchive(
            source,
            SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath);

        var archive = SwShGfPackFile.Parse(patched);
        var throwInfo = SwShFpsTrainerThrowPatcher.InspectAnimation(
            archive.GetFileByName(SwShFpsRomFsTestFixtures.TrainerThrowArchiveClipName));
        var nonThrowInfo = SwShFpsTrainerThrowPatcher.InspectAnimation(
            archive.GetFileByName(SwShFpsRomFsTestFixtures.TrainerNonThrowArchiveClipName));
        Assert.Equal(30u, throwInfo.FrameRate);
        Assert.Equal(60u, nonThrowInfo.FrameRate);
    }

    [Fact]
    public void ServiceRecognizesTrainerThrowRomFsPaths()
    {
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowLooseRelativePath));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.PlayerThrowCameraRelativePath));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.PlayerThrowBattleModelRelativePath));
    }
}
