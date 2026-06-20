// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.FpsPatch;
using Xunit;

namespace KM.SwSh.Tests.FpsPatch;

public sealed class SwShFpsPokemonCenterRecoveryPatcherTests
{
    [Fact]
    public void ConvertArchiveSlowsRecoveryAnimationsOnly()
    {
        var source = SwShFpsRomFsTestFixtures.CreatePokemonCenterRecoveryArchive();

        var patched = SwShFpsPokemonCenterRecoveryPatcher.ConvertArchive(source);

        var archive = SwShGfPackFile.Parse(patched);
        Assert.Equal(
            24u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                archive.GetFileByName("unit_obj_pc_recovery01_main01_ballput.gfbanm")).FrameRate);
        Assert.Equal(
            24u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                archive.GetFileByName("unit_obj_pc_recovery01_main01_recovery.gfbanm")).FrameRate);
        Assert.Equal(
            24u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                archive.GetFileByName("unit_obj_pc_recovery01_ballflash01_recovery.gfbanm")).FrameRate);
        Assert.Equal(
            30u,
            SwShFpsTrainerThrowPatcher.InspectAnimation(
                archive.GetFileByName("unit_obj_pc_recovery01_text01_open.gfbanm")).FrameRate);
    }
}
