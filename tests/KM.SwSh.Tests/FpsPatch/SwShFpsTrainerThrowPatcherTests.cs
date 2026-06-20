// SPDX-License-Identifier: GPL-3.0-only

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
    public void TrainerThrowGfPathsAreLegacyCleanupOnly()
    {
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath));
        Assert.True(SwShFpsTrainerThrowPatcher.IsLegacyBallThrowTimingPath(SwShFpsRomFsTestFixtures.TrainerThrowCameraRelativePath));
        Assert.True(SwShFpsTrainerThrowPatcher.IsLegacyBallThrowTimingPath(SwShFpsRomFsTestFixtures.TrainerThrowBattleModelRelativePath));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowLooseRelativePath));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.TrainerThrowArchiveRelativePath));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.PlayerThrowCameraRelativePath));
        Assert.False(SwShFpsPatchService.IsManagedRomFsPath(SwShFpsRomFsTestFixtures.PlayerThrowBattleModelRelativePath));
        Assert.False(SwShFpsTrainerThrowPatcher.IsLegacyBallThrowTimingPath(SwShFpsRomFsTestFixtures.PlayerThrowCameraRelativePath));
        Assert.False(SwShFpsTrainerThrowPatcher.IsLegacyBallThrowTimingPath(SwShFpsRomFsTestFixtures.PlayerThrowBattleModelRelativePath));
    }
}
