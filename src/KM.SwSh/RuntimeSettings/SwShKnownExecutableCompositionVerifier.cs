// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.CatchCap;
using KM.SwSh.ExeFs;
using KM.SwSh.FashionUnlock;
using KM.SwSh.FpsPatch;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.IvScreen;
using KM.SwSh.NameFilter;
using KM.SwSh.ShinyRate;
using KM.SwSh.TypeChart;

namespace KM.SwSh.RuntimeSettings;

/// <summary>
/// Recognizes complete, exact KM-authored Sword/Shield executable outputs by
/// asking every owning patcher to prove and remove its own semantics. Merely
/// changing bytes inside a registered address range is never sufficient proof.
/// </summary>
public static class SwShKnownExecutableCompositionVerifier
{
    public static bool IsCompatibleRegisteredOutput(
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> candidateMain,
        ProjectGame expectedGame)
    {
        if (retailMain.IsEmpty
            || candidateMain.IsEmpty
            || retailMain.SequenceEqual(candidateMain)
            || expectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            return false;
        }

        try
        {
            var retail = retailMain.ToArray();
            var normalized = candidateMain.ToArray();
            var recognizedTransformation = false;

            if (!NormalizeLegacyGameplaySettings(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeIvScreen(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeHyperTraining(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeCatchCap(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeRoyalCandy(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeFps(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeShinyRate(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeTypeChart(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeFashionUnlock(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeGymUniformRemoval(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeNameFilter(retail, ref normalized, expectedGame, ref recognizedTransformation))
            {
                return false;
            }

            // Dynamax Adventures is intentionally not normalized here. Its
            // exact proof also needs paired archive snapshots, which this
            // executable-only boundary does not possess. Ledgerless candidates
            // carrying that output therefore remain fail-closed.
            return recognizedTransformation
                && NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(retail, normalized);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            return false;
        }
    }

    private static bool NormalizeLegacyGameplaySettings(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShStaticGameplaySettingsMainPatcher.Analyze(retail, normalized, expectedGame);
        if (analysis.Kind == SwShStaticGameplaySettingsMainKind.Configured)
        {
            normalized = SwShStaticGameplaySettingsMainPatcher.RestoreFromBase(retail, normalized, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShStaticGameplaySettingsMainKind.Vanilla;
    }

    private static bool NormalizeIvScreen(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShIvScreenMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind is SwShIvScreenInstallKind.InstalledV1
            or SwShIvScreenInstallKind.InstalledLegacyV1)
        {
            normalized = SwShIvScreenMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShIvScreenInstallKind.NotInstalled;
    }

    private static bool NormalizeHyperTraining(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShHyperTrainingMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind == SwShHyperTrainingMainKind.CustomMinimumLevel)
        {
            normalized = SwShHyperTrainingMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShHyperTrainingMainKind.NotInstalled;
    }

    private static bool NormalizeCatchCap(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShCatchCapMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind == SwShCatchCapInstallKind.InstalledV1)
        {
            normalized = SwShCatchCapMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShCatchCapInstallKind.NotInstalled;
    }

    private static bool NormalizeRoyalCandy(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(normalized, expectedGame);
        if (analysis.Kind is SwShRoyalCandyExeFsSignatureKind.Unlimited
            or SwShRoyalCandyExeFsSignatureKind.StoryLimits)
        {
            normalized = SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShRoyalCandyExeFsSignatureKind.NotInstalled;
    }

    private static bool NormalizeFps(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShFpsMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind == SwShFpsPatchMainKind.Installed)
        {
            normalized = SwShFpsMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShFpsPatchMainKind.NotInstalled;
    }

    private static bool NormalizeShinyRate(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShShinyRateMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind is SwShShinyRateMainKind.FixedRolls
            or SwShShinyRateMainKind.AlwaysShiny)
        {
            normalized = SwShShinyRateMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShShinyRateMainKind.Default;
    }

    private static bool NormalizeTypeChart(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShTypeChartMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind == SwShTypeChartMainKind.Modified)
        {
            normalized = SwShTypeChartMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShTypeChartMainKind.Vanilla;
    }

    private static bool NormalizeFashionUnlock(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShFashionUnlockMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind == SwShFashionUnlockInstallKind.Installed)
        {
            normalized = SwShFashionUnlockMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShFashionUnlockInstallKind.NotInstalled;
    }

    private static bool NormalizeGymUniformRemoval(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShGymUniformRemovalMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind == SwShGymUniformRemovalInstallKind.InstalledV1)
        {
            normalized = SwShGymUniformRemovalMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShGymUniformRemovalInstallKind.NotInstalled;
    }

    private static bool NormalizeNameFilter(
        byte[] retail,
        ref byte[] normalized,
        ProjectGame expectedGame,
        ref bool recognizedTransformation)
    {
        var analysis = SwShNameFilterMainPatcher.Analyze(normalized, expectedGame);
        if (analysis.Kind == SwShNameFilterMainKind.Installed)
        {
            normalized = SwShNameFilterMainPatcher.RestoreFromBase(normalized, retail, expectedGame);
            recognizedTransformation = true;
            return true;
        }

        return analysis.Kind == SwShNameFilterMainKind.NotInstalled;
    }
}
