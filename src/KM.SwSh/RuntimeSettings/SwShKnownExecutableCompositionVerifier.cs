// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using System.Security.Cryptography;
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
using KM.SwSh.HeldItemChance;
using KM.SwSh.GameOptions;
using KM.SwSh.Obedience;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.TrainerDynamax;
using KM.SwSh.TrainerWhiteout;

namespace KM.SwSh.RuntimeSettings;

/// <summary>
/// Recognizes complete, exact KM-authored Sword/Shield executable outputs by
/// asking every owning patcher to prove and remove its own semantics. Merely
/// changing bytes inside a registered address range is never sufficient proof.
/// </summary>
public static class SwShKnownExecutableCompositionVerifier
{
    private const int MaximumVerifiedCompositions = 32;
    private static readonly object VerificationSync = new();
    private static readonly Dictionary<CompositionIdentity, bool> VerifiedCompositions = [];
    private sealed record CompositionIdentity(ProjectGame Game, string Retail, string Candidate,
        string? BaseAdventure, string? CurrentAdventure);

    public static bool IsCompatibleRegisteredOutput(
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> candidateMain,
        ProjectGame expectedGame) => IsCompatibleRegisteredOutputWithAdventureArchive(
            retailMain, candidateMain, expectedGame, null, null);

    public static bool IsCompatibleRegisteredOutputWithAdventureArchive(
        ReadOnlySpan<byte> retailMain, ReadOnlySpan<byte> candidateMain, ProjectGame expectedGame,
        byte[]? baseAdventureArchive, byte[]? currentAdventureArchive)
    {
        if (retailMain.IsEmpty
            || candidateMain.IsEmpty
            || retailMain.SequenceEqual(candidateMain)
            || expectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            return false;
        }

        // This memoizes a pure byte-level proof, never filesystem observations or
        // ownership. Every call hashes the supplied content, including paired archives.
        var retail = retailMain.ToArray();
        var normalized = candidateMain.ToArray();
        var baseArchive = baseAdventureArchive?.ToArray();
        var currentArchive = currentAdventureArchive?.ToArray();
        var identity = new CompositionIdentity(expectedGame,
            Convert.ToHexString(SHA256.HashData(retail)), Convert.ToHexString(SHA256.HashData(normalized)),
            baseArchive is null ? null : Convert.ToHexString(SHA256.HashData(baseArchive)),
            currentArchive is null ? null : Convert.ToHexString(SHA256.HashData(currentArchive)));
        lock (VerificationSync)
        {
            if (VerifiedCompositions.TryGetValue(identity, out var verified)) return verified;
        }
        var result = Verify(retail, normalized, expectedGame, baseArchive, currentArchive);
        lock (VerificationSync)
        {
            if (VerifiedCompositions.Count >= MaximumVerifiedCompositions) VerifiedCompositions.Clear();
            VerifiedCompositions[identity] = result;
        }
        return result;
    }

    private static bool Verify(byte[] retail, byte[] normalized, ProjectGame expectedGame,
        byte[]? baseAdventureArchive, byte[]? currentAdventureArchive)
    {
        try
        {
            var recognizedTransformation = false;

            if (!NormalizeSpecies(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeMegaItems(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeSoundMemory(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeTableMemory(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeTreeQuantity(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeAdventures(retail, ref normalized, expectedGame, baseAdventureArchive, currentAdventureArchive, ref recognizedTransformation)
                || !NormalizeTrainers(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeObedience(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeYComm(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeGameOptions(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeLegacyGameplaySettings(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeIvScreen(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeHyperTraining(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeCatchCap(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeRoyalCandy(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeFps(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeShinyRate(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeTypeChart(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeHeldItemChance(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeFashionUnlock(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeGymUniformRemoval(retail, ref normalized, expectedGame, ref recognizedTransformation)
                || !NormalizeNameFilter(retail, ref normalized, expectedGame, ref recognizedTransformation))
            {
                return false;
            }

            // Adventure changes require paired archive proof. Without it, their
            // remaining bytes cannot pass the final comparison with the base.
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

    private static bool NormalizeTrainers(byte[] retail, ref byte[] normalized,
        ProjectGame game, ref bool recognizedTransformation)
    {
        var whiteout = SwShTrainerWhiteoutMainPatcher.Inspect(normalized, game);
        if (whiteout.HasAny)
        {
            if (!whiteout.Installed) return false;
            normalized = SwShTrainerWhiteoutMainPatcher.Apply(retail, normalized, false, game);
            recognizedTransformation = true;
        }
        var dynamax = SwShTrainerDynamaxMainPatcher.Inspect(normalized, game);
        if (dynamax.Installed)
        {
            if (dynamax.Partial) return false;
            normalized = SwShTrainerDynamaxMainPatcher.ApplySettings(retail, normalized, game, new(false, false));
            recognizedTransformation = true;
        }
        return true;
    }

    private static bool NormalizeSpecies(byte[] retail, ref byte[] normalized, ProjectGame game, ref bool recognizedTransformation)
    {
        if (!KM.SwSh.Restoration.SwShRestorationSpeciesPatcher.Read(normalized, game).Installed) return true;
        normalized = KM.SwSh.Restoration.SwShRestorationSpeciesPatcher.RestoreFromBase(retail, normalized, game);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeMegaItems(byte[] retail, ref byte[] normalized, ProjectGame game, ref bool recognizedTransformation)
    {
        if (!KM.SwSh.Restoration.SwShRestorationMegaItemProtection.Read(normalized, game).Installed) return true;
        normalized = KM.SwSh.Restoration.SwShRestorationMegaItemProtection.RestoreFromBase(retail, normalized, game);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeTableMemory(byte[] retail, ref byte[] normalized, ProjectGame game, ref bool recognizedTransformation)
    {
        if (!KM.SwSh.Restoration.SwShRestorationTableMemoryPatcher.Read(normalized, game).Installed) return true;
        normalized = KM.SwSh.Restoration.SwShRestorationTableMemoryPatcher.RestoreFromBase(retail, normalized, game);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeSoundMemory(byte[] retail, ref byte[] normalized, ProjectGame game, ref bool recognizedTransformation)
    {
        var state = KM.SwSh.Restoration.SwShRestorationSoundMemoryPatcher.Read(normalized, game).State;
        if (state == KM.SwSh.Restoration.SwShRestorationSoundMemoryPatcher.Kind.Original) return true;
        if (state != KM.SwSh.Restoration.SwShRestorationSoundMemoryPatcher.Kind.Installed) return false;
        normalized = KM.SwSh.Restoration.SwShRestorationSoundMemoryPatcher.RestoreFromBase(retail, normalized, game);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeTreeQuantity(byte[] retail, ref byte[] normalized,
        ProjectGame game, ref bool recognizedTransformation)
    {
        var current = KM.SwSh.ShakingTrees.SwShTreeQuantityPatcher.Read(normalized, game);
        if (current.Min == 1 && current.Max == 3) return true;
        normalized = KM.SwSh.ShakingTrees.SwShTreeQuantityPatcher.Apply(retail, normalized, game, 1, 3);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeAdventures(byte[] retail, ref byte[] normalized,
        ProjectGame game, byte[]? baseArchiveBytes, byte[]? currentArchiveBytes, ref bool recognizedTransformation)
    {
        if (baseArchiveBytes is null || currentArchiveBytes is null) return true;
        var baseline = SwShDynamaxAdventureArchive.Parse(baseArchiveBytes);
        var current = SwShDynamaxAdventureArchive.Parse(currentArchiveBytes);
        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(normalized, retail, current, baseline, game);
        if (analysis.Kind == SwShDynamaxAdventuresMainKind.Vanilla) return true;
        if (analysis.Kind == SwShDynamaxAdventuresMainKind.Stale && analysis.HasLegacyBossTargetPatch
            && analysis.SummaryMatchesEffectiveArchive && analysis.CommandValidatorsMatchEffectiveArchive
            && SwShDynamaxAdventuresBossTargetPatcher.TryReadConditionalTargetSpeciesRemap(normalized, out _))
        {
            // Prove both complete historical stubs before removing them in the
            // verification copy. The user's original executable stays intact.
            normalized = SwShDynamaxAdventuresMainPatcher.Reconcile(normalized, retail, current, baseline, game);
        }
        else if (analysis.Kind != SwShDynamaxAdventuresMainKind.Synchronized) return false;
        normalized = SwShDynamaxAdventuresMainPatcher.RestoreFromBase(normalized, retail, baseline.Entries.Count);
        recognizedTransformation = true;
        return true;
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
            or SwShIvScreenInstallKind.InstalledRawV1
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
        if (SwShHyperTrainingItemsPatcher.ReadMain(normalized, expectedGame) != SwShHyperTrainingItemsPatcher.Defaults)
        {
            normalized = SwShHyperTrainingItemsPatcher.ApplyMain(normalized, expectedGame, SwShHyperTrainingItemsPatcher.Defaults);
            recognizedTransformation = true;
        }
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
        // Partial includes older releases and selected component combinations.
        // Every site must still match an exact vanilla, current, or legacy value;
        // the reviewed inverse and final whole-image proof reject unknown edits.
        if (analysis.Kind is SwShFpsPatchMainKind.Installed or SwShFpsPatchMainKind.Partial)
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

    private static bool NormalizeHeldItemChance(byte[] retail, ref byte[] normalized,
        ProjectGame expectedGame, ref bool recognizedTransformation)
    {
        var original = SwShHeldItemChancePatcher.Rates(NsoFile.Inspect(retail));
        var current = SwShHeldItemChancePatcher.Rates(NsoFile.Inspect(normalized));
        if (original.SequenceEqual(current) && SwShHeldItemChancePatcher.Overrides(NsoFile.Inspect(normalized), expectedGame).Count == 0) return true;
        normalized = SwShHeldItemChancePatcher.Apply(retail, normalized, expectedGame, original, []);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeGameOptions(byte[] retail, ref byte[] normalized,
        ProjectGame expectedGame, ref bool recognizedTransformation)
    {
        var current = SwShGameOptionsPatcher.Read(normalized, expectedGame);
        if (current.Selections.All(value => value == 0)) return true;
        normalized = SwShGameOptionsPatcher.Apply(retail, normalized, expectedGame, SwShGameOptionsPolicy.Defaults);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeObedience(byte[] retail, ref byte[] normalized,
        ProjectGame expectedGame, ref bool recognizedTransformation)
    {
        var current = SwShObediencePatcher.Read(normalized, expectedGame);
        if (current.Selections.SequenceEqual(SwShObediencePatcher.Defaults)) return true;
        normalized = SwShObediencePatcher.Apply(retail, normalized, expectedGame, SwShObediencePatcher.Defaults);
        recognizedTransformation = true;
        return true;
    }

    private static bool NormalizeYComm(byte[] retail, ref byte[] normalized,
        ProjectGame expectedGame, ref bool recognizedTransformation)
    {
        if (!KM.SwSh.YComm.SwShYCommMainPatcher.Read(normalized, expectedGame).Disabled) return true;
        normalized = KM.SwSh.YComm.SwShYCommMainPatcher.Apply(retail, normalized, expectedGame, false);
        recognizedTransformation = true;
        return true;
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
