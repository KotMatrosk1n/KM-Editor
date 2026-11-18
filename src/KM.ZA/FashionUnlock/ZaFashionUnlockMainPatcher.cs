// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.ZA.ExeFs;

namespace KM.ZA.FashionUnlock;

internal enum ZaFashionUnlockInstallKind
{
    NotInstalled,
    Installed,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record ZaFashionUnlockAnalysis(
    ZaFashionUnlockInstallKind Kind,
    string Message,
    string BuildId,
    string StubKind,
    ProjectGame? DetectedGame);

internal static class ZaFashionUnlockMainPatcher
{
    public const int OwnedByteCount = 16;
    public const string BuildId = "B1F12FD919EAE86AB8A978317677E64BCE443D1F000000000000000000000000";

    // Keep ownership filtering local to the wardrobe. Shared ownership queries
    // must still see real ownership when granting rewards or purchasing clothing.
    private static readonly Patch[] Patches =
    [
        new(0x02BB2A44, [0x7A, 0xAD, 0xF5, 0x97], [0x20, 0x00, 0x80, 0x52]),
        new(0x0041125C, [0xFD, 0xF6, 0xFF, 0x97], [0x00, 0x00, 0x80, 0x52]),
        new(0x02BC3F0C, [0x85, 0x07, 0x57, 0x97], [0x20, 0x00, 0x80, 0x52]),
        new(0x00183D70, [0xEC, 0x07, 0x00, 0x94], [0x20, 0x00, 0x80, 0x52]),
    ];

    public static ZaFashionUnlockAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        var buildId = "unknown";
        ProjectGame? detectedGame = null;
        try
        {
            var nso = NsoFile.Inspect(mainBytes);
            ValidateIntegrity(nso);
            buildId = Convert.ToHexString(nso.BuildId.AsSpan(0, 20));
            if (!nso.BuildId.AsSpan().SequenceEqual(Convert.FromHexString(BuildId)))
            {
                return new(ZaFashionUnlockInstallKind.UnsupportedBuild,
                    "Fashion Unlock supports Pokemon Legends Z-A 2.0.2 exefs/main files. This build ID is not recognized.",
                    buildId, "unsupported", null);
            }

            detectedGame = ProjectGame.ZA;
            if (expectedGame != ProjectGame.ZA)
            {
                return new(ZaFashionUnlockInstallKind.GameMismatch,
                    "Fashion Unlock requires a Pokemon Legends Z-A project.", buildId, "game mismatch", detectedGame);
            }

            var text = nso.Text.DecompressedData;
            if (nso.Text.Header.MemoryOffset != 0 || Patches.Any(patch => patch.Offset > text.Length - patch.Original.Length))
            {
                throw new InvalidDataException("Fashion Unlock instruction ranges are outside the supported code segment.");
            }

            var vanilla = Patches.All(patch => text.AsSpan(patch.Offset, 4).SequenceEqual(patch.Original));
            var installed = Patches.All(patch => text.AsSpan(patch.Offset, 4).SequenceEqual(patch.Installed));
            if (installed)
            {
                return new(ZaFashionUnlockInstallKind.Installed,
                    "Fashion Unlock is installed. Clothing is available in the wardrobe with its opening story gates bypassed.",
                    buildId, "wardrobe access patches", detectedGame);
            }

            if (vanilla)
            {
                return new(ZaFashionUnlockInstallKind.NotInstalled,
                    "Fashion Unlock is not installed. Install to make clothing available in the wardrobe from the opening field gameplay.",
                    buildId, "vanilla wardrobe checks", detectedGame);
            }

            return new(ZaFashionUnlockInstallKind.Conflict,
                "Fashion Unlock found conflicting or partially installed wardrobe instructions. Restore the competing changes before installing or removing it.",
                buildId, "unknown bytes", detectedGame);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return new(ZaFashionUnlockInstallKind.Conflict, exception.Message, buildId, "unreadable", detectedGame);
        }
    }

    public static byte[] Apply(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is not (ZaFashionUnlockInstallKind.NotInstalled or ZaFashionUnlockInstallKind.Installed))
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        foreach (var patch in Patches) patch.Installed.CopyTo(text.AsSpan(patch.Offset, 4));
        return WriteVerified(nso, text, expectedGame, ZaFashionUnlockInstallKind.Installed);
    }

    public static byte[] RestoreFromBase(byte[] currentMainBytes, byte[] baseMainBytes, ProjectGame? expectedGame = null)
    {
        if (Analyze(currentMainBytes, expectedGame).Kind != ZaFashionUnlockInstallKind.Installed
            || Analyze(baseMainBytes, expectedGame).Kind != ZaFashionUnlockInstallKind.NotInstalled)
        {
            throw new InvalidDataException("Fashion Unlock restore requires an exact installation and a verified Z-A 2.0.2 vanilla base.");
        }

        EnsureCompatibleExecutableIdentity(baseMainBytes, currentMainBytes);
        var current = NsoFile.Parse(currentMainBytes);
        var baseline = NsoFile.Parse(baseMainBytes);
        var text = current.Text.DecompressedData.ToArray();
        foreach (var patch in Patches)
        {
            baseline.Text.DecompressedData.AsSpan(patch.Offset, 4).CopyTo(text.AsSpan(patch.Offset, 4));
        }

        return WriteVerified(current, text, expectedGame, ZaFashionUnlockInstallKind.NotInstalled);
    }

    internal static void EnsureCompatibleExecutableIdentity(byte[] baseMainBytes, byte[] effectiveMainBytes)
    {
        var baseline = NsoFile.Parse(baseMainBytes);
        var effective = NsoFile.Parse(effectiveMainBytes);
        ValidateIntegrity(baseline);
        ValidateIntegrity(effective);
        NsoExecutableCompatibility.EnsureCompatibleBaseLayout(baseline, effective, "Fashion Unlock");
    }

    public static IReadOnlyList<ZaExeFsReservedRegion> ReservedMainTextRegions()
    {
        return ZaExeFsReservedRegionLedger.MainTextRegionsForOwner(ZaExeFsReservedRegionLedger.OwnerFashionUnlock);
    }

    private static byte[] WriteVerified(NsoFile before, byte[] text, ProjectGame? expectedGame, ZaFashionUnlockInstallKind expectedKind)
    {
        var output = before.Write(textDecompressedData: text);
        var after = NsoFile.Parse(output);
        ValidateIntegrity(after);
        NsoExecutableCompatibility.EnsureCompatibleBaseLayout(before, after, "Fashion Unlock output");
        if (before.Text.DecompressedData.Length != after.Text.DecompressedData.Length
            || !before.Ro.DecompressedData.AsSpan().SequenceEqual(after.Ro.DecompressedData)
            || !before.Data.DecompressedData.AsSpan().SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Fashion Unlock changed an unowned segment or segment size.");
        }

        var ranges = Patches.OrderBy(patch => patch.Offset).ToArray();
        var start = 0;
        foreach (var patch in ranges)
        {
            if (!before.Text.DecompressedData.AsSpan(start, patch.Offset - start)
                .SequenceEqual(after.Text.DecompressedData.AsSpan(start, patch.Offset - start)))
            {
                throw new InvalidDataException("Fashion Unlock changed code outside its owned instructions.");
            }

            start = patch.Offset + 4;
        }

        if (!before.Text.DecompressedData.AsSpan(start).SequenceEqual(after.Text.DecompressedData.AsSpan(start))
            || Analyze(output, expectedGame).Kind != expectedKind)
        {
            throw new InvalidDataException("Fashion Unlock output verification failed.");
        }

        return output;
    }

    private static void ValidateIntegrity(NsoFile nso)
    {
        if (nso.Segments.Any(segment => !NsoFile.ComputeHash(segment.DecompressedData).AsSpan().SequenceEqual(segment.Hash)))
        {
            throw new InvalidDataException("Fashion Unlock requires valid hashes for every executable segment.");
        }
    }

    private sealed record Patch(int Offset, byte[] Original, byte[] Installed);
}
