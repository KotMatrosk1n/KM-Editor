// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.CatchCap;
using KM.SwSh.FashionUnlock;
using KM.SwSh.FpsPatch;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.IvScreen;
using KM.SwSh.RoyalCandy;

namespace KM.SwSh.ExeFs;

internal static class SwShIndependentExeFsHookDetector
{
    public static bool ContainsAny(byte[] mainBytes)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var ivScreenKind = SwShIvScreenMainPatcher.Analyze(mainBytes).Kind;
        var fpsPatchKind = SwShFpsMainPatcher.Analyze(mainBytes).Kind;
        return SwShCatchCapMainPatcher.Analyze(mainBytes).Kind == SwShCatchCapInstallKind.InstalledV1
            || SwShFashionUnlockMainPatcher.HasInstalledHook(mainBytes)
            || SwShGymUniformRemovalMainPatcher.HasInstalledHook(mainBytes)
            || SwShHyperTrainingMainPatcher.HasInstalledHook(mainBytes)
            || ivScreenKind is SwShIvScreenInstallKind.InstalledV1 or SwShIvScreenInstallKind.InstalledLegacyV1
            || fpsPatchKind is SwShFpsPatchMainKind.Installed or SwShFpsPatchMainKind.Partial
            || SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(mainBytes).Kind
                != SwShRoyalCandyExeFsSignatureKind.NotInstalled;
    }
}
