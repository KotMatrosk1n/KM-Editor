// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using System.Globalization;

namespace KM.SwSh.FpsPatch;

internal enum SwShFpsPatchMainKind
{
    NotInstalled,
    Installed,
    Partial,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record SwShFpsPatchMainAnalysis(
    SwShFpsPatchMainKind Kind,
    string Message,
    string BuildId,
    ProjectGame? DetectedGame,
    int PatchedSiteCount,
    int SiteCount);

internal static class SwShFpsMainPatcher
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, CreateSwordPatches()),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, CreateShieldPatches()),
    ];

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerFpsPatch);
    }

    public static SwShFpsPatchMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
            if (layout is null)
            {
                return new SwShFpsPatchMainAnalysis(
                    SwShFpsPatchMainKind.UnsupportedBuild,
                    "60FPS Patch supports Pokemon Sword and Shield 1.3.2 exefs/main files.",
                    buildId,
                    null,
                    0,
                    0);
            }

            if (expectedGame is not null && expectedGame != layout.Game)
            {
                return new SwShFpsPatchMainAnalysis(
                    SwShFpsPatchMainKind.GameMismatch,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. 60FPS Patch will not patch a different game's executable."),
                    buildId,
                    layout.Game,
                    0,
                    layout.Patches.Count);
            }

            var text = nso.Text.DecompressedData;
            var patchedCount = 0;
            var vanillaCount = 0;
            foreach (var patch in layout.Patches)
            {
                EnsureTextRange(text, patch.Offset, patch.Expected.Length, patch.Label);
                var current = text.AsSpan(patch.Offset, patch.Expected.Length);
                if (current.SequenceEqual(patch.Replacement))
                {
                    patchedCount++;
                }
                else if (current.SequenceEqual(patch.Expected))
                {
                    vanillaCount++;
                }
            }

            if (patchedCount == layout.Patches.Count)
            {
                return new SwShFpsPatchMainAnalysis(
                    SwShFpsPatchMainKind.Installed,
                    "60FPS ExeFS patch sites are installed.",
                    buildId,
                    layout.Game,
                    patchedCount,
                    layout.Patches.Count);
            }

            if (vanillaCount == layout.Patches.Count)
            {
                return new SwShFpsPatchMainAnalysis(
                    SwShFpsPatchMainKind.NotInstalled,
                    "60FPS ExeFS patch sites are not installed.",
                    buildId,
                    layout.Game,
                    patchedCount,
                    layout.Patches.Count);
            }

            var kind = patchedCount > 0 && patchedCount + vanillaCount == layout.Patches.Count
                ? SwShFpsPatchMainKind.Partial
                : SwShFpsPatchMainKind.Conflict;
            var message = kind == SwShFpsPatchMainKind.Partial
                ? "60FPS ExeFS patch sites are partially installed."
                : "60FPS ExeFS patch sites contain bytes that are neither vanilla nor KM 60FPS output.";
            return new SwShFpsPatchMainAnalysis(
                kind,
                message,
                buildId,
                layout.Game,
                patchedCount,
                layout.Patches.Count);
        }
        catch (InvalidDataException exception)
        {
            return new SwShFpsPatchMainAnalysis(
                SwShFpsPatchMainKind.Conflict,
                exception.Message,
                "unknown",
                null,
                0,
                0);
        }
    }

    public static byte[] Apply(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShFpsPatchMainKind.UnsupportedBuild
            or SwShFpsPatchMainKind.GameMismatch
            or SwShFpsPatchMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var layout = FindLayout(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("60FPS Patch supports Pokemon Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        foreach (var patch in layout.Patches)
        {
            var current = text.AsSpan(patch.Offset, patch.Expected.Length);
            if (!current.SequenceEqual(patch.Expected) && !current.SequenceEqual(patch.Replacement))
            {
                throw new InvalidDataException(
                    string.Create(CultureInfo.InvariantCulture, $"60FPS Patch found conflicting bytes at {FormatTextOffset(patch.Offset)}."));
            }

            patch.Replacement.CopyTo(text.AsSpan(patch.Offset));
        }

        var output = nso.Write(textDecompressedData: text);
        ValidateOnlyOwnedTextBytesChanged(mainBytes, output, layout);
        var outputAnalysis = Analyze(output, expectedGame);
        if (outputAnalysis.Kind != SwShFpsPatchMainKind.Installed)
        {
            throw new InvalidDataException("60FPS Patch verification failed after writing exefs/main.");
        }

        return output;
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentNso = SwShNsoFile.Parse(currentMainBytes);
        var baseNso = SwShNsoFile.Parse(baseMainBytes);
        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        if (!string.Equals(currentBuildId, baseBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("60FPS Patch uninstall requires output exefs/main and base exefs/main to use the same build ID.");
        }

        var layout = FindLayout(baseBuildId)
            ?? throw new InvalidDataException("60FPS Patch supports Pokemon Sword and Shield 1.3.2 exefs/main files.");
        if (expectedGame is not null && expectedGame != layout.Game)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. 60FPS Patch will not restore a different game's executable."));
        }

        var text = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        foreach (var patch in layout.Patches)
        {
            EnsureTextRange(text, patch.Offset, patch.Expected.Length, patch.Label);
            EnsureTextRange(baseText, patch.Offset, patch.Expected.Length, patch.Label);
            if (!baseText.AsSpan(patch.Offset, patch.Expected.Length).SequenceEqual(patch.Expected))
            {
                throw new InvalidDataException(
                    string.Create(CultureInfo.InvariantCulture, $"Base exefs/main is not vanilla at {FormatTextOffset(patch.Offset)}."));
            }

            var current = text.AsSpan(patch.Offset, patch.Expected.Length);
            if (current.SequenceEqual(patch.Replacement))
            {
                patch.Expected.CopyTo(text.AsSpan(patch.Offset));
            }
            else if (!current.SequenceEqual(patch.Expected))
            {
                throw new InvalidDataException(
                    string.Create(CultureInfo.InvariantCulture, $"60FPS Patch found non-owned bytes at {FormatTextOffset(patch.Offset)} and will not overwrite them."));
            }
        }

        var output = currentNso.Write(textDecompressedData: text);
        ValidateOnlyOwnedTextBytesChanged(currentMainBytes, output, layout);
        return output;
    }

    public static string FormatTextOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"main.text+0x{offset:X8}");
    }

    private static void ValidateOnlyOwnedTextBytesChanged(
        byte[] input,
        byte[] output,
        PatchLayout layout)
    {
        var before = SwShNsoFile.Parse(input);
        var after = SwShNsoFile.Parse(output);
        if (!before.BuildId.SequenceEqual(after.BuildId))
        {
            throw new InvalidDataException("60FPS Patch changed the NSO build ID.");
        }

        if (!before.Ro.DecompressedData.SequenceEqual(after.Ro.DecompressedData))
        {
            throw new InvalidDataException("60FPS Patch unexpectedly changed the .ro segment.");
        }

        if (!before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("60FPS Patch unexpectedly changed the .data segment.");
        }

        var beforeText = before.Text.DecompressedData;
        var afterText = after.Text.DecompressedData;
        if (beforeText.Length != afterText.Length)
        {
            throw new InvalidDataException("60FPS Patch changed the decompressed .text segment size.");
        }

        for (var offset = 0; offset < beforeText.Length; offset++)
        {
            var changed = beforeText[offset] != afterText[offset];
            if (!changed)
            {
                continue;
            }

            if (!layout.Patches.Any(patch => offset >= patch.Offset && offset < patch.Offset + patch.Expected.Length))
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"60FPS Patch unexpectedly changed .text byte 0x{offset:X}."));
            }
        }
    }

    private static void EnsureTextRange(ReadOnlySpan<byte> text, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset + length > text.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .text segment.");
        }
    }

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
    }

    private static string FormatGame(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => "Pokemon Sword",
            ProjectGame.Shield => "Pokemon Shield",
            ProjectGame.Scarlet => "Pokemon Scarlet",
            ProjectGame.Violet => "Pokemon Violet",
            _ => game.ToString(),
        };
    }

    private static byte[] Hex(string value)
    {
        return Convert.FromHexString(value);
    }

    private static IReadOnlyList<MainPatch> CreateSwordPatches()
    {
        return CreatePatches(
            nvnPresentIntervalOffset: 0x018A2C88,
            battleEventSchedulerAdrpOffset: 0x0131677C,
            battleEventSchedulerLdrOffset: 0x01316780);
    }

    private static IReadOnlyList<MainPatch> CreateShieldPatches()
    {
        return CreatePatches(
            nvnPresentIntervalOffset: 0x018A2D18,
            battleEventSchedulerAdrpOffset: 0x013167AC,
            battleEventSchedulerLdrOffset: 0x013167B0);
    }

    private static IReadOnlyList<MainPatch> CreatePatches(
        int nvnPresentIntervalOffset,
        int battleEventSchedulerAdrpOffset,
        int battleEventSchedulerLdrOffset)
    {
        return
        [
            new(nvnPresentIntervalOffset, Hex("E103152A"), Hex("E1030032"), "Force NVN present interval 1"),
            new(0x000061F0, Hex("E2030032"), Hex("02008052"), "Duration table index 0"),
            new(0x0000620C, Hex("E2030032"), Hex("02008052"), "Paired duration table index 0"),
            new(0x005DE834, Hex("C90A9452"), Hex("69058A52"), "Inline frame duration low half"),
            new(0x005DE838, Hex("893FA072"), Hex("C91FA072"), "Inline frame duration high half"),
            new(battleEventSchedulerAdrpOffset, Hex("A94900B0"), Hex("A94900D0"), "Battle event scheduler adrp 1/60"),
            new(battleEventSchedulerLdrOffset, Hex("20C94FBD"), Hex("20CD46BD"), "Battle event scheduler ldr 1/60"),
            new(0x009D17B0, Hex("08F044B9"), Hex("01902E1E"), "Battle actor speed fmov 1.25"),
            new(0x009D17B4, Hex("1FE90D71"), Hex("0008211E"), "Battle actor speed fmul"),
            new(0x009D17B8, Hex("21010054"), Hex("00E804BD"), "Battle actor speed store"),
            new(0x009D17BC, Hex("080445B9"), Hex("91F0FF17"), "Battle actor speed branch exit"),
            new(0x009D05C8, Hex("E81B0932"), Hex("08F4A752"), "Battle actor direct +0x4E8 seed 1.0->1.25"),
            new(0x009D0834, Hex("00102C1E"), Hex("00902C1E"), "Battle actor direct +0x4E8 seed 0.5->0.625"),
            new(0x009D0838, Hex("01102E1E"), Hex("01902E1E"), "Battle actor direct +0x4E8 seed 1.0->1.25"),
            new(0x009D0848, Hex("00102C1E"), Hex("00902C1E"), "Battle actor direct +0x4E8 seed 0.5->0.625"),
        ];
    }

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        IReadOnlyList<MainPatch> Patches);

    private sealed record MainPatch(
        int Offset,
        byte[] Expected,
        byte[] Replacement,
        string Label);
}
