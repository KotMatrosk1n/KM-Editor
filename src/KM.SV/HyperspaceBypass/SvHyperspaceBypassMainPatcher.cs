// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SV.ExeFs;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SV.HyperspaceBypass;

internal enum SvHyperspaceBypassInstallKind
{
    NotInstalled,
    Installed,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record SvHyperspaceBypassAnalysis(
    SvHyperspaceBypassInstallKind Kind,
    string Message,
    string BuildId,
    string PatchOffsetHex,
    string StubKind,
    ProjectGame? DetectedGame);

internal static class SvHyperspaceBypassMainPatcher
{
    public const int PatchOffset = 0x02873A50;
    public const int PatchLength = sizeof(uint);

    private const string ScarletBuildId = "421C5411B487EB4D049DD065FEC9547773E8E598";
    private const string VioletBuildId = "709BFD66115298640155FCC4979DBA151C7CC79A";

    private static readonly byte[] VanillaSpeciesCompare = [0x1F, 0x41, 0x0B, 0x71];
    private static readonly byte[] BypassBranch = [0x1A, 0x00, 0x00, 0x14];

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Scarlet, "Pokemon Scarlet 4.0.0", ScarletBuildId),
        new(ProjectGame.Violet, "Pokemon Violet 4.0.0", VioletBuildId),
    ];

    public static SvHyperspaceBypassAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
            if (layout is null)
            {
                return new SvHyperspaceBypassAnalysis(
                    SvHyperspaceBypassInstallKind.UnsupportedBuild,
                    "Hyperspace Bypass supports verified Scarlet and Violet exefs/main builds only. This build ID is not recognized.",
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

            var patchBytes = text.AsSpan(layout.PatchOffset, PatchLength).ToArray();
            var offsetLabel = FormatTextOffset(layout.PatchOffset);

            if (patchBytes.SequenceEqual(BypassBranch))
            {
                return new SvHyperspaceBypassAnalysis(
                    SvHyperspaceBypassInstallKind.Installed,
                    "Hyperspace Bypass is installed. Hyperspace Hole and Hyperspace Fury skip the Hoopa species/form gate while this ExeFS patch is active.",
                    buildId,
                    offsetLabel,
                    "branch to existing success return",
                    layout.Game);
            }

            if (patchBytes.SequenceEqual(VanillaSpeciesCompare))
            {
                return new SvHyperspaceBypassAnalysis(
                    SvHyperspaceBypassInstallKind.NotInstalled,
                    "Hyperspace Bypass is not installed. Installing lets non-Hoopa and wrong-form users pass the Hyperspace runtime gate.",
                    buildId,
                    offsetLabel,
                    "vanilla Hoopa species compare",
                    layout.Game);
            }

            return new SvHyperspaceBypassAnalysis(
                SvHyperspaceBypassInstallKind.Conflict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyperspace Bypass expected vanilla or KM-owned bytes at {offsetLabel}, but found {FormatBytes(patchBytes)}."),
                buildId,
                offsetLabel,
                "unknown bytes",
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SvHyperspaceBypassAnalysis(
                SvHyperspaceBypassInstallKind.Conflict,
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
        if (analysis.Kind is SvHyperspaceBypassInstallKind.UnsupportedBuild
            or SvHyperspaceBypassInstallKind.GameMismatch
            or SvHyperspaceBypassInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var layout = FindLayout(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("Hyperspace Bypass supports verified Scarlet and Violet exefs/main builds only.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, layout);

        BypassBranch.CopyTo(text.AsSpan(layout.PatchOffset, PatchLength));

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
        if (currentAnalysis.Kind == SvHyperspaceBypassInstallKind.GameMismatch)
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        if (currentAnalysis.Kind != SvHyperspaceBypassInstallKind.Installed)
        {
            throw new InvalidDataException("Hyperspace Bypass restore requires an installed Hyperspace Bypass branch.");
        }

        var currentNso = SwShNsoFile.Parse(currentMainBytes);
        var baseNso = SwShNsoFile.Parse(baseMainBytes);
        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        if (!string.Equals(currentBuildId, baseBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Hyperspace Bypass restore requires current and base main NSO files with the same build ID.");
        }

        var layout = FindLayout(baseBuildId)
            ?? throw new InvalidDataException("Hyperspace Bypass restore requires a supported Scarlet or Violet base main NSO.");
        var baseMismatch = CreateGameMismatchAnalysis(layout, expectedGame, baseBuildId);
        if (baseMismatch is not null)
        {
            throw new InvalidDataException(baseMismatch.Message);
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Hyperspace Bypass restore requires current and base main NSO files with matching .text sizes.");
        }

        EnsurePatchRange(currentText, layout);
        EnsurePatchRange(baseText, layout);
        EnsureVanillaBase(baseText, layout);

        baseText.AsSpan(layout.PatchOffset, PatchLength).CopyTo(currentText.AsSpan(layout.PatchOffset, PatchLength));
        return currentNso.Write(textDecompressedData: currentText);
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        return Analyze(mainBytes).Kind == SvHyperspaceBypassInstallKind.Installed;
    }

    public static IReadOnlyList<SvExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SvExeFsReservedRegionLedger.MainTextRegionsForOwner(SvExeFsReservedRegionLedger.OwnerHyperspaceBypass);
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
            throw new InvalidDataException("Hyperspace Bypass patch changed the NSO build ID.");
        }

        if (!before.Ro.DecompressedData.SequenceEqual(after.Ro.DecompressedData))
        {
            throw new InvalidDataException("Hyperspace Bypass patch unexpectedly changed the .ro segment.");
        }

        if (!before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Hyperspace Bypass patch unexpectedly changed the .data segment.");
        }

        var beforeText = before.Text.DecompressedData;
        var afterText = after.Text.DecompressedData;
        if (beforeText.Length != afterText.Length)
        {
            throw new InvalidDataException("Hyperspace Bypass patch changed the decompressed .text segment size.");
        }

        for (var offset = 0; offset < beforeText.Length; offset++)
        {
            var changed = beforeText[offset] != afterText[offset];
            var owned = offset >= layout.PatchOffset && offset < layout.PatchOffset + PatchLength;
            if (changed && !owned)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Hyperspace Bypass patch unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

        if (Analyze(output, expectedGame).Kind != SvHyperspaceBypassInstallKind.Installed)
        {
            throw new InvalidDataException("Hyperspace Bypass patch verification failed after writing exefs/main.");
        }
    }

    private static void EnsureVanillaBase(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        if (!text.Slice(layout.PatchOffset, PatchLength).SequenceEqual(VanillaSpeciesCompare))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyperspace Bypass restore expected the vanilla Hoopa species compare at {FormatTextOffset(layout.PatchOffset)}, but found {FormatBytes(text.Slice(layout.PatchOffset, PatchLength).ToArray())}."));
        }
    }

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
    }

    private static SvHyperspaceBypassAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return new SvHyperspaceBypassAnalysis(
            SvHyperspaceBypassInstallKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. Hyperspace Bypass will not patch this file because Scarlet and Violet use separate verified build IDs."),
            buildId,
            FormatTextOffset(layout.PatchOffset),
            "game mismatch",
            layout.Game);
    }

    private static void EnsurePatchRange(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        if (layout.PatchOffset < 0 || layout.PatchOffset + PatchLength > text.Length)
        {
            throw new InvalidDataException($"{layout.GameName} Hyperspace Bypass patch range is outside the decompressed .text segment.");
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

        var instruction = bytes.Count >= sizeof(uint)
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer)
            : 0;
        return string.Create(CultureInfo.InvariantCulture, $"0x{instruction:X8}");
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
        public int PatchOffset => SvHyperspaceBypassMainPatcher.PatchOffset;
    }
}
