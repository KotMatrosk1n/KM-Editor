// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;

namespace KM.SwSh.DynamaxAdventures;

internal enum SwShDynamaxAdventuresMainKind
{
    Vanilla,
    Synchronized,
    Stale,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record SwShDynamaxAdventuresMainAnalysis(
    SwShDynamaxAdventuresMainKind Kind,
    string Message,
    string BuildId,
    ProjectGame? DetectedGame,
    bool RequiresSummaryMirror,
    bool RequiresCommandValidatorPatch,
    bool SummaryMatchesEffectiveArchive,
    bool CommandValidatorsMatchEffectiveArchive)
{
    public bool HasLegacyBossTargetPatch { get; init; }
}

internal static class SwShDynamaxAdventuresMainPatcher
{
    public const int SummaryOffset = 0x00774054;
    public const int SummaryEntrySize = 0x06;
    internal const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    internal const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    internal const int LocalSpeciesPresentMismatchBranchOffset = 0x00EA52AC;
    internal const int LocalSpeciesMissingMismatchBranchOffset = 0x00EA52C0;
    internal const int LocalFormPresentMismatchBranchOffset = 0x00EA52F4;
    internal const int LocalFormMissingMismatchBranchOffset = 0x00EA5308;
    internal const int LocalGigantamaxMismatchBranchOffset = 0x00EA5310;
    internal const int NestSpeciesPresentMismatchBranchOffset = 0x00EA76AC;
    internal const int NestSpeciesMissingMismatchBranchOffset = 0x00EA76C0;
    internal const int NestFormPresentMismatchBranchOffset = 0x00EA76F4;
    internal const int NestFormMissingMismatchBranchOffset = 0x00EA7708;
    internal const int NestGigantamaxMismatchBranchOffset = 0x00EA7710;
    internal const int DaiSpeciesPresentMismatchBranchOffset = 0x00EA78B4;
    internal const int DaiSpeciesMissingMismatchBranchOffset = 0x00EA78C8;
    internal const int DaiFormPresentMismatchBranchOffset = 0x00EA78FC;
    internal const int DaiFormMissingMismatchBranchOffset = 0x00EA7910;
    internal const int DaiGigantamaxMismatchBranchOffset = 0x00EA7918;
    internal const int ShieldCommandValidatorOffsetDelta = 0x30;

    private const uint NopInstruction = 0xD503201F;

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, 0),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, ShieldCommandValidatorOffsetDelta),
    ];

    private static readonly CommandValidatorBranch[] CommandMirrorFailureBranches =
    [
        new(LocalSpeciesPresentMismatchBranchOffset, 0x1400001C, "LocalNestHolePokemon species-present mismatch"),
        new(LocalSpeciesMissingMismatchBranchOffset, 0x540002E1, "LocalNestHolePokemon species-missing mismatch"),
        new(LocalFormPresentMismatchBranchOffset, 0x1400000A, "LocalNestHolePokemon form-present mismatch"),
        new(LocalFormMissingMismatchBranchOffset, 0x540000A1, "LocalNestHolePokemon form-missing mismatch"),
        new(LocalGigantamaxMismatchBranchOffset, 0x35000068, "LocalNestHolePokemon Gigantamax mismatch"),
        new(NestSpeciesPresentMismatchBranchOffset, 0x1400001C, "NestHolePokemon species-present mismatch"),
        new(NestSpeciesMissingMismatchBranchOffset, 0x540002E1, "NestHolePokemon species-missing mismatch"),
        new(NestFormPresentMismatchBranchOffset, 0x1400000A, "NestHolePokemon form-present mismatch"),
        new(NestFormMissingMismatchBranchOffset, 0x540000A1, "NestHolePokemon form-missing mismatch"),
        new(NestGigantamaxMismatchBranchOffset, 0x35000068, "NestHolePokemon Gigantamax mismatch"),
        new(DaiSpeciesPresentMismatchBranchOffset, 0x1400001C, "DaiNestHolePokemon species-present mismatch"),
        new(DaiSpeciesMissingMismatchBranchOffset, 0x540002E1, "DaiNestHolePokemon species-missing mismatch"),
        new(DaiFormPresentMismatchBranchOffset, 0x1400000A, "DaiNestHolePokemon form-present mismatch"),
        new(DaiFormMissingMismatchBranchOffset, 0x540000A1, "DaiNestHolePokemon form-missing mismatch"),
        new(DaiGigantamaxMismatchBranchOffset, 0x35000068, "DaiNestHolePokemon Gigantamax mismatch"),
    ];

    public static SwShDynamaxAdventuresMainAnalysis Analyze(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        SwShDynamaxAdventureArchive effectiveArchive,
        SwShDynamaxAdventureArchive baseArchive,
        ProjectGame? expectedGame,
        SwShDynamaxAdventureArchive? recognizedSourceArchive = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(effectiveArchive);
        ArgumentNullException.ThrowIfNull(baseArchive);

        var buildId = "unknown";
        ProjectGame? detectedGame = null;
        try
        {
            EnsureSupportedExpectedGame(expectedGame);
            EnsureMatchingArchiveShape(effectiveArchive, baseArchive);

            var currentNso = NsoFile.Parse(currentMainBytes);
            var baseNso = NsoFile.Parse(baseMainBytes);
            ValidateRequiredSegmentHashes(currentNso);
            ValidateRequiredSegmentHashes(baseNso);
            buildId = FormatBuildId(currentNso.BuildId);

            var currentLayout = FindLayout(currentNso.BuildId);
            var baseLayout = FindLayout(baseNso.BuildId);
            if (currentLayout is null || baseLayout is null)
            {
                return CreateAnalysis(
                    SwShDynamaxAdventuresMainKind.UnsupportedBuild,
                    "Dynamax Adventures supports Sword and Shield 1.3.2 exefs/main files. This full build identity is not recognized.",
                    buildId,
                    detectedGame,
                    effectiveArchive,
                    baseArchive,
                    summaryMatches: false,
                    validatorsMatch: false);
            }

            detectedGame = currentLayout.Game;
            if (currentLayout.Game != expectedGame || baseLayout.Game != expectedGame)
            {
                return CreateAnalysis(
                    SwShDynamaxAdventuresMainKind.GameMismatch,
                    $"Selected {FormatGame(expectedGame!.Value)}, but the effective or base exefs/main belongs to a different supported game.",
                    buildId,
                    detectedGame,
                    effectiveArchive,
                    baseArchive,
                    summaryMatches: false,
                    validatorsMatch: false);
            }

            EnsureCompatibleExecutableIdentity(baseNso, currentNso, "Dynamax Adventures analysis");
            ValidateBaseOwnedState(baseNso, baseArchive, baseLayout);

            var currentText = currentNso.Text.DecompressedData.AsSpan();
            var currentRo = currentNso.Ro.DecompressedData.AsSpan();
            var baseText = baseNso.Text.DecompressedData.AsSpan();
            var summaryMatches = SummaryMatchesArchive(currentRo, effectiveArchive.Entries);
            var summaryIsOwned = summaryMatches
                || SummaryMatchesArchive(currentRo, baseArchive.Entries)
                || recognizedSourceArchive is not null
                    && SummaryMatchesArchive(currentRo, recognizedSourceArchive.Entries);
            if (!summaryIsOwned)
            {
                return CreateAnalysis(
                    SwShDynamaxAdventuresMainKind.Conflict,
                    "Dynamax Adventures found a summary projection that matches neither the verified base, reviewed source, nor final effective Adventure table.",
                    buildId,
                    detectedGame,
                    effectiveArchive,
                    baseArchive,
                    summaryMatches,
                    validatorsMatch: false);
            }
            var requiresValidators = RequiresCommandValidatorPatch(effectiveArchive, baseArchive);
            var validatorsMatch = CommandValidatorsMatch(
                currentText,
                baseText,
                currentLayout,
                requiresValidators,
                out var validatorsAreOwned);
            var bossCallSiteDelta = SwShDynamaxAdventuresBossTargetPatcher.GetCallSiteOffsetDelta(currentNso.BuildId);
            var canInspectLegacyBossTargetPatch = currentText.Length >=
                SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset
                + bossCallSiteDelta
                + sizeof(uint);
            var hasLegacyBossTargetPatch = canInspectLegacyBossTargetPatch
                && TryReadLegacyBossTargetPatch(currentMainBytes, baseText.Length);

            if (!validatorsAreOwned)
            {
                return CreateAnalysis(
                    SwShDynamaxAdventuresMainKind.Conflict,
                    "Dynamax Adventures found a non-owned instruction at a command-validator site and will not overwrite it.",
                    buildId,
                    detectedGame,
                    effectiveArchive,
                    baseArchive,
                    summaryMatches,
                    validatorsMatch);
            }

            var synchronized = summaryMatches && validatorsMatch && !hasLegacyBossTargetPatch;
            var archiveMatchesBase = ArchiveRecordsEqual(effectiveArchive, baseArchive);
            var kind = synchronized
                ? archiveMatchesBase
                    ? SwShDynamaxAdventuresMainKind.Vanilla
                    : SwShDynamaxAdventuresMainKind.Synchronized
                : SwShDynamaxAdventuresMainKind.Stale;
            var message = kind switch
            {
                SwShDynamaxAdventuresMainKind.Vanilla => "Dynamax Adventures executable mirrors match the verified base table.",
                SwShDynamaxAdventuresMainKind.Synchronized => "Dynamax Adventures executable mirrors match the effective Adventure table.",
                _ => "Dynamax Adventures executable mirrors are stale but contain only base or KM-owned validator states and can be repaired.",
            };

            return CreateAnalysis(
                kind,
                message,
                buildId,
                detectedGame,
                effectiveArchive,
                baseArchive,
                summaryMatches,
                validatorsMatch) with
            {
                HasLegacyBossTargetPatch = hasLegacyBossTargetPatch,
            };
        }
        catch (InvalidDataException exception)
        {
            return CreateAnalysis(
                SwShDynamaxAdventuresMainKind.Conflict,
                exception.Message,
                buildId,
                detectedGame,
                effectiveArchive,
                baseArchive,
                summaryMatches: false,
                validatorsMatch: false);
        }
    }

    public static byte[] Reconcile(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        SwShDynamaxAdventureArchive effectiveArchive,
        SwShDynamaxAdventureArchive baseArchive,
        ProjectGame? expectedGame,
        SwShDynamaxAdventureArchive? recognizedSourceArchive = null)
    {
        var analysis = Analyze(
            currentMainBytes,
            baseMainBytes,
            effectiveArchive,
            baseArchive,
            expectedGame,
            recognizedSourceArchive);
        if (analysis.Kind is SwShDynamaxAdventuresMainKind.UnsupportedBuild
            or SwShDynamaxAdventuresMainKind.GameMismatch
            or SwShDynamaxAdventuresMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        var layout = FindLayout(currentNso.BuildId)
            ?? throw new InvalidDataException("Dynamax Adventures could not resolve the supported executable layout.");
        var text = currentNso.Text.DecompressedData.ToArray();
        var ro = currentNso.Ro.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData.AsSpan();

        var bossCallSiteDelta = SwShDynamaxAdventuresBossTargetPatcher.GetCallSiteOffsetDelta(currentNso.BuildId);
        if (analysis.HasLegacyBossTargetPatch)
        {
            text = SwShDynamaxAdventuresBossTargetPatcher.RestoreTextFromBase(
                text,
                baseText,
                bossCallSiteDelta);
        }

        WriteSummary(ro, effectiveArchive.Entries);
        WriteCommandValidatorState(
            text,
            baseText,
            layout,
            RequiresCommandValidatorPatch(effectiveArchive, baseArchive));

        var output = currentNso.Write(textDecompressedData: text, roDecompressedData: ro);
        ValidateOutputPreservation(currentMainBytes, output, text, ro);
        var outputAnalysis = Analyze(
            output,
            baseMainBytes,
            effectiveArchive,
            baseArchive,
            expectedGame,
            recognizedSourceArchive);
        if (outputAnalysis.Kind is not (SwShDynamaxAdventuresMainKind.Vanilla or SwShDynamaxAdventuresMainKind.Synchronized))
        {
            throw new InvalidDataException("Dynamax Adventures executable reconciliation did not produce the required semantic state.");
        }

        return output;
    }

    public static byte[] RestoreFromBase(byte[] currentMainBytes, byte[] baseMainBytes, int entryCount)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        if (entryCount < 0)
        {
            throw new InvalidDataException("Dynamax Adventures restore requires a non-negative Adventure entry count.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        var currentLayout = FindLayout(currentNso.BuildId)
            ?? throw new InvalidDataException("Dynamax Adventures restore requires a recognized full Sword/Shield build identity.");
        var baseLayout = FindLayout(baseNso.BuildId)
            ?? throw new InvalidDataException("Dynamax Adventures restore requires a recognized full base Sword/Shield build identity.");
        if (currentLayout.Game != baseLayout.Game)
        {
            throw new InvalidDataException("Dynamax Adventures restore requires matching Sword/Shield games.");
        }

        EnsureCompatibleExecutableIdentity(baseNso, currentNso, "Dynamax Adventures restore");
        var text = currentNso.Text.DecompressedData.ToArray();
        var ro = currentNso.Ro.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData.AsSpan();
        var baseRo = baseNso.Ro.DecompressedData.AsSpan();

        RestoreSummaryFromBase(ro, baseRo, entryCount);
        WriteCommandValidatorState(text, baseText, currentLayout, patchValidators: false);
        return currentNso.Write(textDecompressedData: text, roDecompressedData: ro);
    }

    internal static void WriteSummary(byte[] ro, IReadOnlyList<SwShDynamaxAdventureRecord> entries)
    {
        ArgumentNullException.ThrowIfNull(ro);
        ArgumentNullException.ThrowIfNull(entries);

        var length = checked(entries.Count * SummaryEntrySize);
        EnsureRange(ro, SummaryOffset, length, "Dynamax Adventures hardcoded summary table");

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.EntryIndex != index)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Dynamax Adventure entry index {entry.EntryIndex} is not in table order at row {index}."));
            }

            var destination = ro.AsSpan(SummaryOffset + (index * SummaryEntrySize), SummaryEntrySize);
            destination[0] = entry.IsSingleCapture ? (byte)1 : (byte)0;
            // Byte +1 is opaque padding in the verified layout. Preserve the source value.
            BinaryPrimitives.WriteInt16LittleEndian(
                destination[2..4],
                checked((short)ValidateSignedSummaryValue(entry.Species, short.MinValue, short.MaxValue, "species")));
            destination[4] = unchecked((byte)(sbyte)ValidateSignedSummaryValue(entry.Form, sbyte.MinValue, sbyte.MaxValue, "form"));
            destination[5] = unchecked((byte)(sbyte)ValidateSignedSummaryValue(entry.ShinyRoll, sbyte.MinValue, sbyte.MaxValue, "shiny roll"));
        }
    }

    internal static void PatchCommandValidatorMirrors(byte[] text, int offsetDelta = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var branch in CommandMirrorFailureBranches)
        {
            WriteNopIfVanillaOrOwned(
                text,
                branch.Offset + offsetDelta,
                branch.VanillaInstruction,
                branch.Label);
        }
    }

    internal static int GetCommandValidatorOffsetDelta(byte[] buildId)
    {
        return FindLayout(buildId)?.CommandValidatorOffsetDelta
            ?? throw new InvalidDataException("Dynamax Adventures does not recognize this full 32-byte Sword/Shield build identity.");
    }

    internal static ProjectGame GetGame(byte[] buildId)
    {
        return FindLayout(buildId)?.Game
            ?? throw new InvalidDataException("Dynamax Adventures does not recognize this full 32-byte Sword/Shield build identity.");
    }

    internal static string FormatBuildId(byte[] buildId)
    {
        return buildId.Length == 0 ? "unknown" : Convert.ToHexString(buildId);
    }

    internal static IReadOnlyList<(int Offset, int Length)> CommandValidatorRegions(ProjectGame game)
    {
        var layout = Layouts.SingleOrDefault(candidate => candidate.Game == game)
            ?? throw new ArgumentOutOfRangeException(nameof(game), game, "Dynamax Adventures requires Pokemon Sword or Pokemon Shield.");
        return CommandMirrorFailureBranches
            .Select(branch => (branch.Offset + layout.CommandValidatorOffsetDelta, sizeof(uint)))
            .ToArray();
    }

    private static SwShDynamaxAdventuresMainAnalysis CreateAnalysis(
        SwShDynamaxAdventuresMainKind kind,
        string message,
        string buildId,
        ProjectGame? detectedGame,
        SwShDynamaxAdventureArchive effectiveArchive,
        SwShDynamaxAdventureArchive baseArchive,
        bool summaryMatches,
        bool validatorsMatch)
    {
        var requiresSummaryMirror = false;
        var requiresCommandValidatorPatch = false;
        try
        {
            requiresSummaryMirror = RequiresSummaryMirror(effectiveArchive, baseArchive);
            requiresCommandValidatorPatch = RequiresCommandValidatorPatch(effectiveArchive, baseArchive);
        }
        catch (InvalidDataException)
        {
            // Analysis construction must not throw when the archive shape is itself invalid.
        }

        return new SwShDynamaxAdventuresMainAnalysis(
            kind,
            message,
            buildId,
            detectedGame,
            requiresSummaryMirror,
            requiresCommandValidatorPatch,
            summaryMatches,
            validatorsMatch);
    }

    private static void ValidateBaseOwnedState(
        NsoFile baseNso,
        SwShDynamaxAdventureArchive baseArchive,
        PatchLayout layout)
    {
        if (!SummaryMatchesArchive(baseNso.Ro.DecompressedData, baseArchive.Entries))
        {
            throw new InvalidDataException("Base exefs/main does not contain the canonical Dynamax Adventures summary for the verified base table.");
        }

        foreach (var branch in CommandMirrorFailureBranches)
        {
            var offset = branch.Offset + layout.CommandValidatorOffsetDelta;
            var actual = ReadInstruction(baseNso.Text.DecompressedData, offset, $"Base {branch.Label}");
            if (actual != branch.VanillaInstruction)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Base exefs/main expected vanilla {branch.Label} at main.text+0x{offset:X}, but found 0x{actual:X8}."));
            }
        }
    }

    private static bool SummaryMatchesArchive(
        ReadOnlySpan<byte> ro,
        IReadOnlyList<SwShDynamaxAdventureRecord> entries)
    {
        EnsureRange(ro, SummaryOffset, checked(entries.Count * SummaryEntrySize), "Dynamax Adventures hardcoded summary table");
        for (var index = 0; index < entries.Count; index++)
        {
            var source = ro.Slice(SummaryOffset + (index * SummaryEntrySize), SummaryEntrySize);
            var entry = entries[index];
            if (source[0] != (entry.IsSingleCapture ? (byte)1 : (byte)0)
                || BinaryPrimitives.ReadInt16LittleEndian(source[2..4]) != entry.Species
                || unchecked((sbyte)source[4]) != entry.Form
                || unchecked((sbyte)source[5]) != entry.ShinyRoll)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CommandValidatorsMatch(
        ReadOnlySpan<byte> currentText,
        ReadOnlySpan<byte> baseText,
        PatchLayout layout,
        bool patchValidators,
        out bool validatorsAreOwned)
    {
        validatorsAreOwned = true;
        var matches = true;
        foreach (var branch in CommandMirrorFailureBranches)
        {
            var offset = branch.Offset + layout.CommandValidatorOffsetDelta;
            var baseInstruction = ReadInstruction(baseText, offset, $"Base {branch.Label}");
            var currentInstruction = ReadInstruction(currentText, offset, branch.Label);
            if (currentInstruction != baseInstruction && currentInstruction != NopInstruction)
            {
                validatorsAreOwned = false;
                return false;
            }

            var expected = patchValidators ? NopInstruction : baseInstruction;
            matches &= currentInstruction == expected;
        }

        return matches;
    }

    private static void WriteCommandValidatorState(
        byte[] text,
        ReadOnlySpan<byte> baseText,
        PatchLayout layout,
        bool patchValidators)
    {
        foreach (var branch in CommandMirrorFailureBranches)
        {
            var offset = branch.Offset + layout.CommandValidatorOffsetDelta;
            var baseInstruction = ReadInstruction(baseText, offset, $"Base {branch.Label}");
            var currentInstruction = ReadInstruction(text, offset, branch.Label);
            if (currentInstruction != baseInstruction && currentInstruction != NopInstruction)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Dynamax Adventures found a non-owned {branch.Label} instruction at main.text+0x{offset:X}."));
            }

            WriteInstruction(text, offset, patchValidators ? NopInstruction : baseInstruction, branch.Label);
        }
    }

    private static void RestoreSummaryFromBase(byte[] currentRo, ReadOnlySpan<byte> baseRo, int entryCount)
    {
        var length = checked(entryCount * SummaryEntrySize);
        EnsureRange(currentRo, SummaryOffset, length, "Dynamax Adventures hardcoded summary table");
        EnsureRange(baseRo, SummaryOffset, length, "Base Dynamax Adventures hardcoded summary table");
        baseRo.Slice(SummaryOffset, length).CopyTo(currentRo.AsSpan(SummaryOffset, length));
    }

    private static bool RequiresSummaryMirror(
        SwShDynamaxAdventureArchive effectiveArchive,
        SwShDynamaxAdventureArchive baseArchive)
    {
        EnsureMatchingArchiveShape(effectiveArchive, baseArchive);
        return effectiveArchive.Entries.Zip(baseArchive.Entries).Any(pair =>
            pair.First.IsSingleCapture != pair.Second.IsSingleCapture
            || pair.First.Species != pair.Second.Species
            || pair.First.Form != pair.Second.Form
            || pair.First.ShinyRoll != pair.Second.ShinyRoll);
    }

    private static bool RequiresCommandValidatorPatch(
        SwShDynamaxAdventureArchive effectiveArchive,
        SwShDynamaxAdventureArchive baseArchive)
    {
        EnsureMatchingArchiveShape(effectiveArchive, baseArchive);
        return effectiveArchive.Entries.Zip(baseArchive.Entries).Any(pair =>
            pair.First.Species != pair.Second.Species
            || pair.First.Form != pair.Second.Form
            || pair.First.GigantamaxState != pair.Second.GigantamaxState);
    }

    private static void EnsureMatchingArchiveShape(
        SwShDynamaxAdventureArchive effectiveArchive,
        SwShDynamaxAdventureArchive baseArchive)
    {
        if (effectiveArchive.Entries.Count != baseArchive.Entries.Count)
        {
            throw new InvalidDataException("Dynamax Adventures effective and base tables must contain the same number of ordered rows.");
        }

        for (var index = 0; index < effectiveArchive.Entries.Count; index++)
        {
            if (effectiveArchive.Entries[index].EntryIndex != index
                || baseArchive.Entries[index].EntryIndex != index)
            {
                throw new InvalidDataException("Dynamax Adventures effective and base table rows must use stable ordered entry indexes.");
            }
        }
    }

    private static bool ArchiveRecordsEqual(
        SwShDynamaxAdventureArchive left,
        SwShDynamaxAdventureArchive right)
    {
        return left.Entries.Count == right.Entries.Count
            && left.Entries.Zip(right.Entries).All(pair => RecordsEqual(pair.First, pair.Second));
    }

    private static bool RecordsEqual(
        SwShDynamaxAdventureRecord left,
        SwShDynamaxAdventureRecord right)
    {
        return left.EntryIndex == right.EntryIndex
            && left.IsSingleCapture == right.IsSingleCapture
            && left.SingleCaptureFlagBlock == right.SingleCaptureFlagBlock
            && left.Field02 == right.Field02
            && left.Form == right.Form
            && left.GigantamaxState == right.GigantamaxState
            && left.BallItemId == right.BallItemId
            && left.AdventureIndex == right.AdventureIndex
            && left.Level == right.Level
            && left.Species == right.Species
            && left.UiMessageId == right.UiMessageId
            && left.OtGender == right.OtGender
            && left.Version == right.Version
            && left.ShinyRoll == right.ShinyRoll
            && left.Ivs == right.Ivs
            && left.Ability == right.Ability
            && left.IsStoryProgressGated == right.IsStoryProgressGated
            && left.Moves.SequenceEqual(right.Moves);
    }

    private static void ValidateOutputPreservation(
        byte[] inputBytes,
        byte[] outputBytes,
        byte[] expectedText,
        byte[] expectedRo)
    {
        var before = NsoFile.Parse(inputBytes);
        var after = NsoFile.Parse(outputBytes);
        ValidateRequiredSegmentHashes(after);
        EnsureCompatibleExecutableIdentity(
            before,
            after,
            "Dynamax Adventures output verification",
            allowRightTextShrink: true);
        if (!after.Text.DecompressedData.SequenceEqual(expectedText)
            || !after.Ro.DecompressedData.SequenceEqual(expectedRo)
            || !before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Dynamax Adventures executable reconciliation output does not match the verified owned-range projection.");
        }
    }

    private static void EnsureCompatibleExecutableIdentity(
        NsoFile left,
        NsoFile right,
        string operation,
        bool allowRightTextShrink = false)
    {
        if (left.Version != right.Version
            || left.Flags != right.Flags
            || !left.BuildId.SequenceEqual(right.BuildId))
        {
            throw new InvalidDataException(
                $"{operation} requires matching NSO version, flags, and full 32-byte build identity.");
        }

        var normalizedRightHeader = right.RawHeader.ToArray();
        if (normalizedRightHeader.Length == NsoFile.HeaderSize)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                normalizedRightHeader.AsSpan(0x18, sizeof(int)),
                left.Text.Header.DecompressedSize);
        }

        if (!SwShExeFsMainComparison.StableHeaderBytesMatch(left.RawHeader, normalizedRightHeader))
        {
            throw new InvalidDataException(
                $"{operation} requires matching stable NSO module, BSS, and segment-layout header metadata.");
        }

        if (left.Segments.Count != right.Segments.Count)
        {
            throw new InvalidDataException($"{operation} requires matching NSO segment counts.");
        }

        for (var index = 0; index < left.Segments.Count; index++)
        {
            var leftSegment = left.Segments[index];
            var rightSegment = right.Segments[index];
            var allowsAppendOnlyText = index == 0;
            if (leftSegment.Header.MemoryOffset != rightSegment.Header.MemoryOffset
                || leftSegment.Header.DecompressedSize != leftSegment.DecompressedData.Length
                || rightSegment.Header.DecompressedSize != rightSegment.DecompressedData.Length
                || (!allowsAppendOnlyText
                    && leftSegment.DecompressedData.Length != rightSegment.DecompressedData.Length))
            {
                throw new InvalidDataException(
                    $"{operation} requires matching segment memory offsets, stable .ro/.data sizes, and a valid append-only .text layout.");
            }

            if (allowsAppendOnlyText
                && !allowRightTextShrink
                && rightSegment.DecompressedData.Length < leftSegment.DecompressedData.Length)
            {
                throw new InvalidDataException(
                    $"{operation} requires effective main.text to retain the complete base text prefix.");
            }
        }

        var textMemoryLimit = new[] { left.Ro, left.Data }
            .Where(segment => segment.Header.DecompressedSize > 0)
            .Select(segment => (long)segment.Header.MemoryOffset)
            .DefaultIfEmpty(long.MaxValue)
            .Min();
        var leftTextEnd = (long)left.Text.Header.MemoryOffset + left.Text.DecompressedData.Length;
        var rightTextEnd = (long)right.Text.Header.MemoryOffset + right.Text.DecompressedData.Length;
        if (leftTextEnd > textMemoryLimit || rightTextEnd > textMemoryLimit)
        {
            throw new InvalidDataException($"{operation} found a .text segment that overlaps the next mapped segment.");
        }
    }

    private static void ValidateRequiredSegmentHashes(NsoFile nso)
    {
        ValidateRequiredSegmentHash(nso.Text, nso.Flags.HasFlag(NsoFlags.CheckHashText));
        ValidateRequiredSegmentHash(nso.Ro, nso.Flags.HasFlag(NsoFlags.CheckHashRo));
        ValidateRequiredSegmentHash(nso.Data, nso.Flags.HasFlag(NsoFlags.CheckHashData));
    }

    private static void ValidateRequiredSegmentHash(NsoSegment segment, bool required)
    {
        if (required && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
        {
            throw new InvalidDataException(
                $"Dynamax Adventures rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static PatchLayout? FindLayout(ReadOnlySpan<byte> buildId)
    {
        foreach (var layout in Layouts)
        {
            if (IsCanonicalBuildId(buildId, layout.BuildId))
            {
                return layout;
            }
        }

        return null;
    }

    private static bool IsCanonicalBuildId(ReadOnlySpan<byte> buildId, string expectedPrefixHex)
    {
        const int nsoBuildIdLength = 0x20;
        const int knownBuildIdLength = 0x14;
        if (buildId.Length != nsoBuildIdLength)
        {
            return false;
        }

        var expectedPrefix = Convert.FromHexString(expectedPrefixHex);
        return expectedPrefix.Length == knownBuildIdLength
            && buildId[..knownBuildIdLength].SequenceEqual(expectedPrefix)
            && IsZero(buildId[knownBuildIdLength..]);
    }

    private static bool IsZero(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadLegacyBossTargetPatch(byte[] mainBytes, int baseTextLength)
    {
        try
        {
            return SwShDynamaxAdventuresBossTargetPatcher.TryReadConditionalTargetSpeciesRemap(
                mainBytes,
                out _);
        }
        catch (InvalidDataException exception)
        {
            if (SwShDynamaxAdventuresBossTargetPatcher.HasRecognizableLegacyPatchState(
                    mainBytes,
                    baseTextLength))
            {
                throw new InvalidDataException(
                    "Dynamax Adventures found a partial or damaged historical KM boss-target remap. Restore or remove the complete legacy patch before editing.",
                    exception);
            }

            // These sites are no longer an active DA editing surface. Arbitrary foreign call-site
            // states remain outside KM ownership and are preserved.
            return false;
        }
    }

    private static void EnsureSupportedExpectedGame(ProjectGame? expectedGame)
    {
        if (expectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            throw new InvalidDataException(
                "Dynamax Adventures executable patching requires Pokemon Sword or Pokemon Shield to be selected explicitly.");
        }
    }

    private static int ValidateSignedSummaryValue(int value, int minimum, int maximum, string field)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventure {field} value {value} cannot be mirrored into the game's hardcoded summary table."));
        }

        return value;
    }

    private static void WriteNopIfVanillaOrOwned(byte[] text, int offset, uint vanillaInstruction, string label)
    {
        var actual = ReadInstruction(text, offset, label);
        if (actual == NopInstruction)
        {
            return;
        }

        if (actual != vanillaInstruction)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures expected vanilla {label} branch at main.text+0x{offset:X}, but found 0x{actual:X8}."));
        }

        WriteInstruction(text, offset, NopInstruction, label);
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset, string label)
    {
        EnsureRange(text, offset, sizeof(uint), $"{label} instruction");
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction, string label)
    {
        EnsureRange(text, offset, sizeof(uint), $"{label} instruction");
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed NSO segment.");
        }
    }

    private static string FormatGame(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => "Pokemon Sword",
            ProjectGame.Shield => "Pokemon Shield",
            _ => game.ToString(),
        };
    }

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int CommandValidatorOffsetDelta);

    private sealed record CommandValidatorBranch(
        int Offset,
        uint VanillaInstruction,
        string Label);
}
