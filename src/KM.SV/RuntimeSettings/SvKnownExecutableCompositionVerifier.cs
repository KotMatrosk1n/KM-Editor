// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SV.FashionUnlock;
using KM.SV.HyperspaceBypass;
using KM.SV.TypeChart;

namespace KM.SV.RuntimeSettings;

/// <summary>
/// Recognizes exact-build executable images by reversing only complete,
/// semantically valid KM editor outputs. Merely changing bytes inside a
/// reserved region is never sufficient to authorize ledgerless composition.
/// </summary>
public static class SvKnownExecutableCompositionVerifier
{
    public static bool IsCompatibleRegisteredOutput(
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> candidateMain,
        ProjectGame expectedGame)
    {
        if (retailMain.IsEmpty
            || candidateMain.IsEmpty
            || retailMain.SequenceEqual(candidateMain)
            || !TryGetEdition(expectedGame, out var expectedEdition))
        {
            return false;
        }

        try
        {
            var retail = retailMain.ToArray();
            var normalized = candidateMain.ToArray();
            var recognizedTransformation = false;

            var gameplay = SvGameplaySettingsMainPatcher.Analyze(
                normalized,
                expectedEdition);
            if (gameplay.Kind == SvGameplaySettingsMainKind.Modified)
            {
                normalized = SvGameplaySettingsMainPatcher.RestoreFromBase(
                    normalized,
                    retail,
                    expectedEdition);
                recognizedTransformation = true;
            }
            else if (gameplay.Kind != SvGameplaySettingsMainKind.Vanilla)
            {
                return false;
            }

            var typeChart = SvTypeChartMainPatcher.Analyze(normalized, expectedGame);
            if (typeChart.Kind == SvTypeChartMainKind.Modified)
            {
                normalized = SvTypeChartMainPatcher.RestoreFromBase(
                    normalized,
                    retail,
                    expectedGame);
                recognizedTransformation = true;
            }
            else if (typeChart.Kind != SvTypeChartMainKind.Vanilla)
            {
                return false;
            }

            var fashion = SvFashionUnlockMainPatcher.Analyze(normalized, expectedGame);
            if (fashion.Kind == SvFashionUnlockInstallKind.Installed)
            {
                normalized = SvFashionUnlockMainPatcher.RestoreFromBase(
                    normalized,
                    retail,
                    expectedGame);
                recognizedTransformation = true;
            }
            else if (fashion.Kind != SvFashionUnlockInstallKind.NotInstalled)
            {
                return false;
            }

            var hyperspace = SvHyperspaceBypassMainPatcher.Analyze(
                normalized,
                expectedGame);
            if (hyperspace.Kind == SvHyperspaceBypassInstallKind.Installed)
            {
                normalized = SvHyperspaceBypassMainPatcher.RestoreFromBase(
                    normalized,
                    retail,
                    expectedGame);
                recognizedTransformation = true;
            }
            else if (hyperspace.Kind != SvHyperspaceBypassInstallKind.NotInstalled)
            {
                return false;
            }

            return recognizedTransformation
                && NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(
                    retail,
                    normalized);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            return false;
        }
    }

    private static bool TryGetEdition(
        ProjectGame expectedGame,
        out SvGameplayRuntimeEdition expectedEdition)
    {
        expectedEdition = expectedGame switch
        {
            ProjectGame.Scarlet => SvGameplayRuntimeEdition.Scarlet,
            ProjectGame.Violet => SvGameplayRuntimeEdition.Violet,
            _ => default,
        };
        return expectedGame is ProjectGame.Scarlet or ProjectGame.Violet;
    }
}
