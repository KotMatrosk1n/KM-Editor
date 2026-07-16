// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.FashionUnlock;

internal enum SwShFashionUnlockInstallKind
{
    NotInstalled,
    Installed,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record SwShFashionUnlockAnalysis(
    SwShFashionUnlockInstallKind Kind,
    string Message,
    string BuildId,
    string DirectGetterOffsetHex,
    string MappedGetterOffsetHex,
    string StubKind,
    ProjectGame? DetectedGame);

internal static class SwShFashionUnlockMainPatcher
{
    public const int SwordDirectGetterOffset = 0x0143A2B0;
    public const int SwordMappedGetterOffset = 0x0143A300;
    private const int ShieldOffsetDelta = 0x30;
    public const int ShieldDirectGetterOffset = SwordDirectGetterOffset + ShieldOffsetDelta;
    public const int ShieldMappedGetterOffset = SwordMappedGetterOffset + ShieldOffsetDelta;
    public const int PatchLength = sizeof(uint) * 2;

    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private static readonly byte[] DirectGetterVanilla =
    [
        0xE8, 0x03, 0x00, 0xAA,
        0xE0, 0x03, 0x1F, 0x2A,
    ];

    private static readonly byte[] MappedGetterVanilla =
    [
        0xFF, 0x03, 0x06, 0xD1,
        0xFC, 0x5F, 0x14, 0xA9,
    ];

    private static readonly byte[] ReturnTrueStub =
    [
        0x20, 0x00, 0x80, 0x52,
        0xC0, 0x03, 0x5F, 0xD6,
    ];

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, 0),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, ShieldOffsetDelta),
    ];

    public static SwShFashionUnlockAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var buildId = "unknown";
        PatchLayout? detectedLayout = null;
        try
        {
            var nso = NsoFile.Parse(mainBytes);
            ValidateRequiredSegmentHashes(nso);
            buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(nso.BuildId);
            if (layout is null)
            {
                return CreateAnalysis(
                    SwShFashionUnlockInstallKind.UnsupportedBuild,
                    "Fashion Unlock supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    buildId,
                    "unknown",
                    "unknown",
                    "unsupported",
                    detectedGame: null);
            }

            detectedLayout = layout;
            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRanges(text, layout);

            var direct = text.AsSpan(layout.DirectGetterOffset, PatchLength).ToArray();
            var mapped = text.AsSpan(layout.MappedGetterOffset, PatchLength).ToArray();
            var directInstalled = direct.SequenceEqual(ReturnTrueStub);
            var mappedInstalled = mapped.SequenceEqual(ReturnTrueStub);
            var directVanilla = direct.SequenceEqual(DirectGetterVanilla);
            var mappedVanilla = mapped.SequenceEqual(MappedGetterVanilla);

            if (directInstalled && mappedInstalled)
            {
                return CreateAnalysis(
                    SwShFashionUnlockInstallKind.Installed,
                    "Fashion Unlock is installed. Fashion ownership checks return unlocked while the ExeFS patch is active.",
                    buildId,
                    FormatTextOffset(layout.DirectGetterOffset),
                    FormatTextOffset(layout.MappedGetterOffset),
                    "return-true ownership stubs",
                    layout.Game);
            }

            if (directVanilla && mappedVanilla)
            {
                return CreateAnalysis(
                    SwShFashionUnlockInstallKind.NotInstalled,
                    "Fashion Unlock is not installed. Installing makes clothing ownership checks return unlocked without editing the save file.",
                    buildId,
                    FormatTextOffset(layout.DirectGetterOffset),
                    FormatTextOffset(layout.MappedGetterOffset),
                    "vanilla ownership getters",
                    layout.Game);
            }

            return CreateAnalysis(
                SwShFashionUnlockInstallKind.Conflict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Fashion Unlock expected vanilla or KM-owned ownership getter bytes at {FormatTextOffset(layout.DirectGetterOffset)} and {FormatTextOffset(layout.MappedGetterOffset)}, but found {FormatBytes(direct)} and {FormatBytes(mapped)}."),
                buildId,
                FormatTextOffset(layout.DirectGetterOffset),
                FormatTextOffset(layout.MappedGetterOffset),
                "unknown bytes",
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return CreateAnalysis(
                SwShFashionUnlockInstallKind.Conflict,
                exception.Message,
                buildId,
                detectedLayout is null ? "unknown" : FormatTextOffset(detectedLayout.DirectGetterOffset),
                detectedLayout is null ? "unknown" : FormatTextOffset(detectedLayout.MappedGetterOffset),
                "unreadable",
                detectedLayout?.Game);
        }
    }

    public static byte[] Apply(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        EnsureSupportedExpectedGame(expectedGame);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShFashionUnlockInstallKind.UnsupportedBuild
            or SwShFashionUnlockInstallKind.GameMismatch
            or SwShFashionUnlockInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        ValidateRequiredSegmentHashes(nso);
        var layout = FindLayout(nso.BuildId)
            ?? throw new InvalidDataException("Fashion Unlock supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRanges(text, layout);

        ReturnTrueStub.CopyTo(text.AsSpan(layout.DirectGetterOffset, PatchLength));
        ReturnTrueStub.CopyTo(text.AsSpan(layout.MappedGetterOffset, PatchLength));

        var output = nso.Write(textDecompressedData: text);
        ValidateOutput(mainBytes, output, layout, expectedGame);
        return output;
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        EnsureSupportedExpectedGame(expectedGame);

        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind != SwShFashionUnlockInstallKind.Installed)
        {
            throw new InvalidDataException(
                currentAnalysis.Kind is SwShFashionUnlockInstallKind.UnsupportedBuild
                    or SwShFashionUnlockInstallKind.GameMismatch
                    or SwShFashionUnlockInstallKind.Conflict
                    ? currentAnalysis.Message
                    : "Fashion Unlock restore requires installed Fashion Unlock stubs.");
        }

        var baseAnalysis = Analyze(baseMainBytes, expectedGame);
        if (baseAnalysis.Kind != SwShFashionUnlockInstallKind.NotInstalled)
        {
            throw new InvalidDataException(
                "Fashion Unlock restore requires a verified selected-game vanilla base exefs/main.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        EnsureSameBuildAndLayout(baseNso, currentNso, "Fashion Unlock restore");
        var layout = FindLayout(baseNso.BuildId)
            ?? throw new InvalidDataException("Fashion Unlock restore requires a supported Sword or Shield 1.3.2 base main NSO.");

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        EnsurePatchRanges(currentText, layout);
        EnsurePatchRanges(baseText, layout);
        EnsureVanillaBase(baseText, layout);

        foreach (var offset in new[] { layout.DirectGetterOffset, layout.MappedGetterOffset })
        {
            baseText.AsSpan(offset, PatchLength).CopyTo(currentText.AsSpan(offset, PatchLength));
        }

        var output = currentNso.Write(textDecompressedData: currentText);
        ValidateOutput(
            currentMainBytes,
            output,
            layout,
            expectedGame,
            SwShFashionUnlockInstallKind.NotInstalled,
            "Fashion Unlock restore");
        return output;
    }

    internal static void EnsureCompatibleExecutableIdentity(
        byte[] baseMainBytes,
        byte[] effectiveMainBytes)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(effectiveMainBytes);

        var baseNso = NsoFile.Parse(baseMainBytes);
        var effectiveNso = NsoFile.Parse(effectiveMainBytes);
        ValidateRequiredSegmentHashes(baseNso);
        ValidateRequiredSegmentHashes(effectiveNso);
        EnsureSameBuildAndLayout(baseNso, effectiveNso, "Fashion Unlock apply");
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        return Analyze(mainBytes).Kind == SwShFashionUnlockInstallKind.Installed;
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerFashionUnlock);
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions(ProjectGame game)
    {
        if (game is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            return [];
        }

        var inactiveGameToken = game == ProjectGame.Sword ? "-shield-" : "-sword-";
        return ReservedMainTextRegions()
            .Where(region => !region.FeatureId.Contains(inactiveGameToken, StringComparison.Ordinal))
            .ToArray();
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        PatchLayout layout,
        ProjectGame? expectedGame,
        SwShFashionUnlockInstallKind expectedKind = SwShFashionUnlockInstallKind.Installed,
        string operation = "Fashion Unlock apply")
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        ValidateRequiredSegmentHashes(before);
        ValidateRequiredSegmentHashes(after);
        EnsureSameBuildAndLayout(before, after, operation);
        VerifyPreservedSegment(before.Ro, after.Ro, ".ro", operation);
        VerifyPreservedSegment(before.Data, after.Data, ".data", operation);

        var beforeText = before.Text.DecompressedData;
        var afterText = after.Text.DecompressedData;
        if (beforeText.Length != afterText.Length)
        {
            throw new InvalidDataException("Fashion Unlock patch changed the decompressed .text segment size.");
        }

        for (var offset = 0; offset < beforeText.Length; offset++)
        {
            var changed = beforeText[offset] != afterText[offset];
            var owned = IsOwnedPatchByte(layout, offset);
            if (changed && !owned)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{operation} unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

        if (Analyze(output, expectedGame).Kind != expectedKind)
        {
            throw new InvalidDataException($"{operation} verification failed after writing exefs/main.");
        }
    }

    private static void EnsureSameBuildAndLayout(NsoFile left, NsoFile right, string operation)
    {
        if (left.Version != right.Version
            || left.Flags != right.Flags
            || !left.BuildId.SequenceEqual(right.BuildId)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(left.RawHeader, right.RawHeader))
        {
            throw new InvalidDataException(
                $"{operation} requires matching NSO version, flags, stable header metadata, and full 32-byte build identity.");
        }

        for (var index = 0; index < left.Segments.Count; index++)
        {
            var leftSegment = left.Segments[index];
            var rightSegment = right.Segments[index];
            if (leftSegment.Header.MemoryOffset != rightSegment.Header.MemoryOffset
                || leftSegment.Header.DecompressedSize != rightSegment.Header.DecompressedSize
                || leftSegment.DecompressedData.Length != rightSegment.DecompressedData.Length)
            {
                throw new InvalidDataException(
                    $"{operation} requires matching {leftSegment.Name} memory offsets and decompressed sizes.");
            }
        }
    }

    private static void VerifyPreservedSegment(
        NsoSegment source,
        NsoSegment output,
        string segmentName,
        string operation)
    {
        if (source.Header.MemoryOffset != output.Header.MemoryOffset
            || source.Header.DecompressedSize != output.Header.DecompressedSize
            || source.CompressedSize != output.CompressedSize
            || !source.Hash.SequenceEqual(output.Hash)
            || !source.CompressedData.SequenceEqual(output.CompressedData)
            || !source.DecompressedData.SequenceEqual(output.DecompressedData))
        {
            throw new InvalidDataException($"{operation} verification found a changed {segmentName} segment.");
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
                $"Fashion Unlock rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static bool IsOwnedPatchByte(PatchLayout layout, int offset)
    {
        return (offset >= layout.DirectGetterOffset && offset < layout.DirectGetterOffset + PatchLength)
            || (offset >= layout.MappedGetterOffset && offset < layout.MappedGetterOffset + PatchLength);
    }

    private static void EnsureVanillaBase(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        if (!text.Slice(layout.DirectGetterOffset, PatchLength).SequenceEqual(DirectGetterVanilla)
            || !text.Slice(layout.MappedGetterOffset, PatchLength).SequenceEqual(MappedGetterVanilla))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Fashion Unlock restore expected vanilla ownership getter bytes at {FormatTextOffset(layout.DirectGetterOffset)} and {FormatTextOffset(layout.MappedGetterOffset)}."));
        }
    }

    private static SwShFashionUnlockAnalysis CreateAnalysis(
        SwShFashionUnlockInstallKind kind,
        string message,
        string buildId,
        string directGetterOffsetHex,
        string mappedGetterOffsetHex,
        string stubKind,
        ProjectGame? detectedGame)
    {
        return new SwShFashionUnlockAnalysis(
            kind,
            message,
            buildId,
            directGetterOffsetHex,
            mappedGetterOffsetHex,
            stubKind,
            detectedGame);
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

    private static void EnsureSupportedExpectedGame(ProjectGame? expectedGame)
    {
        if (expectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            throw new InvalidDataException(
                "Fashion Unlock patching requires Pokemon Sword or Pokemon Shield to be selected explicitly.");
        }
    }

    private static SwShFashionUnlockAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return CreateAnalysis(
            SwShFashionUnlockInstallKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. Fashion Unlock will not patch this file because Sword and Shield use separate verified patch layouts."),
            buildId,
            FormatTextOffset(layout.DirectGetterOffset),
            FormatTextOffset(layout.MappedGetterOffset),
            "game mismatch",
            layout.Game);
    }

    private static void EnsurePatchRanges(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureTextRange(text, layout.DirectGetterOffset, PatchLength, $"{layout.GameName} direct fashion ownership getter");
        EnsureTextRange(text, layout.MappedGetterOffset, PatchLength, $"{layout.GameName} mapped fashion ownership getter");
    }

    private static void EnsureTextRange(ReadOnlySpan<byte> text, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset + length > text.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .text segment.");
        }
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
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

    private static string FormatTextOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"main.text+0x{offset:X8}");
    }

    private static string FormatBytes(IReadOnlyList<byte> bytes)
    {
        Span<byte> buffer = stackalloc byte[PatchLength];
        for (var index = 0; index < Math.Min(bytes.Count, PatchLength); index++)
        {
            buffer[index] = bytes[index];
        }

        var first = bytes.Count >= sizeof(uint)
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer[..sizeof(uint)])
            : 0;
        var second = bytes.Count >= PatchLength
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(sizeof(uint), sizeof(uint)))
            : 0;
        return string.Create(CultureInfo.InvariantCulture, $"0x{first:X8} 0x{second:X8}");
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
        int Shift)
    {
        public int DirectGetterOffset => SwordDirectGetterOffset + Shift;
        public int MappedGetterOffset => SwordMappedGetterOffset + Shift;
    }
}
