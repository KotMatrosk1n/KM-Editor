// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.ZA.Pokemon;
using KM.ZA.TypeChart;

namespace KM.ZA.RuntimeSettings;

/// <summary>
/// Recognizes exact-build executable images by reversing only complete,
/// semantically valid KM editor outputs. Reserved offsets alone are not proof
/// of provenance or safety.
/// </summary>
public static class ZaKnownExecutableCompositionVerifier
{
    public static bool IsCompatibleRegisteredOutput(
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> candidateMain,
        ProjectGame expectedGame = ProjectGame.ZA)
    {
        if (expectedGame != ProjectGame.ZA
            || retailMain.IsEmpty
            || candidateMain.IsEmpty
            || retailMain.SequenceEqual(candidateMain))
        {
            return false;
        }

        try
        {
            var retail = retailMain.ToArray();
            var normalized = candidateMain.ToArray();
            var recognizedTransformation = false;

            var gameplay = ZaStaticGameplaySettingsMainPatcher.Analyze(
                retail,
                normalized,
                expectedGame);
            if (gameplay.Kind == ZaStaticGameplaySettingsMainKind.Configured)
            {
                normalized = ZaStaticGameplaySettingsMainPatcher.RestoreFromBase(
                    retail,
                    normalized,
                    expectedGame);
                recognizedTransformation = true;
            }
            else if (gameplay.Kind != ZaStaticGameplaySettingsMainKind.Vanilla)
            {
                return false;
            }

            var dexLayout = ZaDexLayoutMainPatcher.Analyze(normalized, expectedGame);
            if (dexLayout.Kind == ZaDexLayoutMainKind.Modified)
            {
                normalized = ZaDexLayoutMainPatcher.ApplyRegularCount(
                    normalized,
                    ZaDexLayoutMainPatcher.VanillaRegularCount,
                    expectedGame);
                recognizedTransformation = true;
            }
            else if (dexLayout.Kind != ZaDexLayoutMainKind.Vanilla)
            {
                return false;
            }

            var typeChart = ZaTypeChartMainPatcher.Analyze(normalized, expectedGame);
            if (typeChart.Kind == ZaTypeChartMainKind.Modified)
            {
                normalized = ZaTypeChartMainPatcher.RestoreFromBase(
                    normalized,
                    retail,
                    expectedGame);
                recognizedTransformation = true;
            }
            else if (typeChart.Kind != ZaTypeChartMainKind.Vanilla)
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
}
