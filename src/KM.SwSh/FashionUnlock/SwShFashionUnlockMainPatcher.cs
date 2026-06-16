// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
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

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
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
                "unknown",
                "unknown",
                "unknown",
                "unreadable",
                detectedGame: null);
        }
    }

    public static byte[] Apply(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShFashionUnlockInstallKind.UnsupportedBuild
            or SwShFashionUnlockInstallKind.GameMismatch
            or SwShFashionUnlockInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var layout = FindLayout(FormatBuildId(nso.BuildId))
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

        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind == SwShFashionUnlockInstallKind.GameMismatch)
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        if (currentAnalysis.Kind != SwShFashionUnlockInstallKind.Installed)
        {
            throw new InvalidDataException("Fashion Unlock restore requires installed Fashion Unlock stubs.");
        }

        var currentNso = SwShNsoFile.Parse(currentMainBytes);
        var baseNso = SwShNsoFile.Parse(baseMainBytes);
        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        if (!string.Equals(currentBuildId, baseBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fashion Unlock restore requires current and base main NSO files with the same build ID.");
        }

        var layout = FindLayout(baseBuildId)
            ?? throw new InvalidDataException("Fashion Unlock restore requires a supported Sword or Shield 1.3.2 base main NSO.");
        var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, baseBuildId);
        if (mismatch is not null)
        {
            throw new InvalidDataException(mismatch.Message);
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Fashion Unlock restore requires current and base main NSO files with matching .text sizes.");
        }

        EnsurePatchRanges(currentText, layout);
        EnsurePatchRanges(baseText, layout);
        EnsureVanillaBase(baseText, layout);

        foreach (var offset in new[] { layout.DirectGetterOffset, layout.MappedGetterOffset })
        {
            baseText.AsSpan(offset, PatchLength).CopyTo(currentText.AsSpan(offset, PatchLength));
        }

        return currentNso.Write(textDecompressedData: currentText);
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        return Analyze(mainBytes).Kind == SwShFashionUnlockInstallKind.Installed;
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerFashionUnlock);
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        PatchLayout layout,
        ProjectGame? expectedGame)
    {
        var before = SwShNsoFile.Parse(input);
        var after = SwShNsoFile.Parse(output);
        if (!before.BuildId.SequenceEqual(after.BuildId))
        {
            throw new InvalidDataException("Fashion Unlock patch changed the NSO build ID.");
        }

        if (!before.Ro.DecompressedData.SequenceEqual(after.Ro.DecompressedData))
        {
            throw new InvalidDataException("Fashion Unlock patch unexpectedly changed the .ro segment.");
        }

        if (!before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Fashion Unlock patch unexpectedly changed the .data segment.");
        }

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
                        $"Fashion Unlock patch unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

        if (Analyze(output, expectedGame).Kind != SwShFashionUnlockInstallKind.Installed)
        {
            throw new InvalidDataException("Fashion Unlock patch verification failed after writing exefs/main.");
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

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
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
