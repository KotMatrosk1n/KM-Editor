// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.CatchCap;
using KM.SwSh.FashionUnlock;
using KM.SwSh.FpsPatch;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.IvScreen;
using KM.SwSh.RoyalCandy;
using KM.SwSh.TrainerWhiteout;

namespace KM.SwSh.ExeFs;

internal static class SwShIndependentExeFsHookDetector
{
    public static bool ContainsAny(byte[] mainBytes)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var ivScreenKind = SwShIvScreenMainPatcher.Analyze(mainBytes).Kind;
        var fpsPatchKind = SwShFpsMainPatcher.Analyze(mainBytes).Kind;
        return SwShCatchCapMainPatcher.Analyze(mainBytes).Kind == SwShCatchCapInstallKind.InstalledV1
            || KM.SwSh.Restoration.SwShRestorationSoundMemoryPatcher.HasInstalledPatch(mainBytes)
            || KM.SwSh.Restoration.SwShRestorationTableMemoryPatcher.HasInstalledPatch(mainBytes)
            || KM.SwSh.Restoration.SwShRestorationMegaItemProtection.HasInstalledPatch(mainBytes)
            || KM.SwSh.Restoration.SwShRestorationSpeciesPatcher.HasInstalledPatch(mainBytes)
            || KM.SwSh.ShakingTrees.SwShTreeQuantityPatcher.HasInstalledHook(mainBytes)
            || SwShFashionUnlockMainPatcher.HasInstalledHook(mainBytes)
            || SwShGymUniformRemovalMainPatcher.HasInstalledHook(mainBytes)
            || SwShHyperTrainingMainPatcher.HasInstalledHook(mainBytes)
            || SwShHyperTrainingItemsPatcher.HasInstalledHook(mainBytes)
            || SwShTrainerWhiteoutMainPatcher.HasInstalledHook(mainBytes)
            || KM.SwSh.Obedience.SwShObediencePatcher.HasInstalledHook(mainBytes)
            || KM.SwSh.YComm.SwShYCommMainPatcher.HasInstalledHook(mainBytes)
            || KM.SwSh.TrainerDynamax.SwShTrainerDynamaxMainPatcher.HasInstalledHook(mainBytes)
            || ivScreenKind is SwShIvScreenInstallKind.InstalledV1 or SwShIvScreenInstallKind.InstalledRawV1 or SwShIvScreenInstallKind.InstalledLegacyV1
            || fpsPatchKind is SwShFpsPatchMainKind.Installed or SwShFpsPatchMainKind.Partial
            || SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(mainBytes).Kind
                != SwShRoyalCandyExeFsSignatureKind.NotInstalled;
    }
}
