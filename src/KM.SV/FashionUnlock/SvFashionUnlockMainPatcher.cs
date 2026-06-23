// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SV.ExeFs;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SV.FashionUnlock;

internal enum SvFashionUnlockInstallKind
{
    NotInstalled,
    Installed,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record SvFashionUnlockAnalysis(
    SvFashionUnlockInstallKind Kind,
    string Message,
    string BuildId,
    string OwnershipCheckOffsetHex,
    string StubKind,
    ProjectGame? DetectedGame);

internal static class SvFashionUnlockMainPatcher
{
    public const int OwnershipCheckOffset = 0x00EAE95C;
    public const int PatchLength = sizeof(uint) * 2;

    private const string ScarletBuildId = "421C5411B487EB4D049DD065FEC9547773E8E598";
    private const string VioletBuildId = "709BFD66115298640155FCC4979DBA151C7CC79A";

    private static readonly byte[] VanillaOwnershipCheckEntry =
    [
        0xFD, 0x7B, 0xBB, 0xA9,
        0xF9, 0x0B, 0x00, 0xF9,
    ];

    private static readonly byte[] ReturnTrueStub =
    [
        0x20, 0x00, 0x80, 0x52,
        0xC0, 0x03, 0x5F, 0xD6,
    ];

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Scarlet, "Pokemon Scarlet 4.0.0", ScarletBuildId),
        new(ProjectGame.Violet, "Pokemon Violet 4.0.0", VioletBuildId),
    ];

    public static SvFashionUnlockAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
            if (layout is null)
            {
                return new SvFashionUnlockAnalysis(
                    SvFashionUnlockInstallKind.UnsupportedBuild,
                    "Fashion Unlock supports verified Scarlet and Violet exefs/main builds only. This build ID is not recognized.",
                    buildId,
                    "unknown",
                    "unsupported",
                    DetectedGame: null);
            }

            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRange(text, layout);

            var ownershipCheck = text.AsSpan(layout.OwnershipCheckOffset, PatchLength).ToArray();
            var offsetLabel = FormatTextOffset(layout.OwnershipCheckOffset);

            if (ownershipCheck.SequenceEqual(ReturnTrueStub))
            {
                return new SvFashionUnlockAnalysis(
                    SvFashionUnlockInstallKind.Installed,
                    "Fashion Unlock is installed. Scarlet/Violet dress-up ownership checks return unlocked while this ExeFS patch is active.",
                    buildId,
                    offsetLabel,
                    "return-true dress-up ownership stub",
                    layout.Game);
            }

            if (ownershipCheck.SequenceEqual(VanillaOwnershipCheckEntry))
            {
                return new SvFashionUnlockAnalysis(
                    SvFashionUnlockInstallKind.NotInstalled,
                    "Fashion Unlock is not installed. Installing makes Scarlet/Violet dress-up ownership checks return unlocked without editing the save file.",
                    buildId,
                    offsetLabel,
                    "vanilla dress-up ownership check",
                    layout.Game);
            }

            return new SvFashionUnlockAnalysis(
                SvFashionUnlockInstallKind.Conflict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Fashion Unlock expected vanilla or KM-owned dress-up ownership bytes at {offsetLabel}, but found {FormatBytes(ownershipCheck)}."),
                buildId,
                offsetLabel,
                "unknown bytes",
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SvFashionUnlockAnalysis(
                SvFashionUnlockInstallKind.Conflict,
                exception.Message,
                "unknown",
                "unknown",
                "unreadable",
                DetectedGame: null);
        }
    }

    public static byte[] Apply(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SvFashionUnlockInstallKind.UnsupportedBuild
            or SvFashionUnlockInstallKind.GameMismatch
            or SvFashionUnlockInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var layout = FindLayout(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("Fashion Unlock supports verified Scarlet and Violet exefs/main builds only.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, layout);

        ReturnTrueStub.CopyTo(text.AsSpan(layout.OwnershipCheckOffset, PatchLength));

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
        if (currentAnalysis.Kind == SvFashionUnlockInstallKind.GameMismatch)
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        if (currentAnalysis.Kind != SvFashionUnlockInstallKind.Installed)
        {
            throw new InvalidDataException("Fashion Unlock restore requires an installed Scarlet/Violet dress-up ownership stub.");
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
            ?? throw new InvalidDataException("Fashion Unlock restore requires a supported Scarlet or Violet base main NSO.");
        var baseMismatch = CreateGameMismatchAnalysis(layout, expectedGame, baseBuildId);
        if (baseMismatch is not null)
        {
            throw new InvalidDataException(baseMismatch.Message);
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Fashion Unlock restore requires current and base main NSO files with matching .text sizes.");
        }

        EnsurePatchRange(currentText, layout);
        EnsurePatchRange(baseText, layout);
        EnsureVanillaBase(baseText, layout);

        baseText
            .AsSpan(layout.OwnershipCheckOffset, PatchLength)
            .CopyTo(currentText.AsSpan(layout.OwnershipCheckOffset, PatchLength));
        return currentNso.Write(textDecompressedData: currentText);
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        return Analyze(mainBytes).Kind == SvFashionUnlockInstallKind.Installed;
    }

    public static IReadOnlyList<SvExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SvExeFsReservedRegionLedger.MainTextRegionsForOwner(SvExeFsReservedRegionLedger.OwnerFashionUnlock);
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
            var owned = offset >= layout.OwnershipCheckOffset && offset < layout.OwnershipCheckOffset + PatchLength;
            if (changed && !owned)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Fashion Unlock patch unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

        if (Analyze(output, expectedGame).Kind != SvFashionUnlockInstallKind.Installed)
        {
            throw new InvalidDataException("Fashion Unlock patch verification failed after writing exefs/main.");
        }
    }

    private static void EnsureVanillaBase(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        if (!text.Slice(layout.OwnershipCheckOffset, PatchLength).SequenceEqual(VanillaOwnershipCheckEntry))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Fashion Unlock restore expected vanilla dress-up ownership bytes at {FormatTextOffset(layout.OwnershipCheckOffset)}, but found {FormatBytes(text.Slice(layout.OwnershipCheckOffset, PatchLength).ToArray())}."));
        }
    }

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
    }

    private static SvFashionUnlockAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return new SvFashionUnlockAnalysis(
            SvFashionUnlockInstallKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. Fashion Unlock will not patch this file because Scarlet and Violet use separate verified build IDs."),
            buildId,
            FormatTextOffset(layout.OwnershipCheckOffset),
            "game mismatch",
            layout.Game);
    }

    private static void EnsurePatchRange(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        if (layout.OwnershipCheckOffset < 0 || layout.OwnershipCheckOffset + PatchLength > text.Length)
        {
            throw new InvalidDataException($"{layout.GameName} Fashion Unlock patch range is outside the decompressed .text segment.");
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
        return ProjectGameMetadata.Get(game).DisplayName;
    }

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId)
    {
        public int OwnershipCheckOffset => SvFashionUnlockMainPatcher.OwnershipCheckOffset;
    }
}
