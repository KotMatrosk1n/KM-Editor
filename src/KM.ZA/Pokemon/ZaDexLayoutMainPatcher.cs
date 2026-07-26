// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.ZA.ExeFs;

namespace KM.ZA.Pokemon;

internal enum ZaDexLayoutMainKind
{
    Vanilla,
    Modified,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record ZaDexLayoutMainAnalysis(
    ZaDexLayoutMainKind Kind,
    string Message,
    string BuildId,
    int? RegularCount,
    ProjectGame? DetectedGame);

internal static class ZaDexLayoutMainPatcher
{
    public const int VanillaRegularCount = 232;
    public const int TotalDexSpeciesCount = 364;

    private const string ZABuildId = "B1F12FD919EAE86AB8A978317677E64BCE443D1F";
    private const uint ImmediateMask = 0x003FFC00;

    private static readonly InstructionPatch[] InstructionPatches =
    [
        new(0x008D0EF0, 0x51000009),
        new(0x008D0EF4, 0x7100011F),
        new(0x009F0C38, 0x510002A9),
        new(0x009F0C3C, 0x7100011F),
        new(0x00A027B0, 0x51000009),
        new(0x00A027B4, 0x7100011F),
        new(0x02C8D524, 0x710002BF),
        new(0x02C8D528, 0x51000289),
    ];

    public static ZaDexLayoutMainAnalysis Analyze(
        byte[] mainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = NsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            if (!string.Equals(buildId, ZABuildId, StringComparison.OrdinalIgnoreCase))
            {
                return new ZaDexLayoutMainAnalysis(
                    ZaDexLayoutMainKind.UnsupportedBuild,
                    "Dex Layout supports the verified Pokemon Legends Z-A exefs/main build only. This build ID is not recognized.",
                    buildId,
                    RegularCount: null,
                    DetectedGame: null);
            }

            if (expectedGame is not null && expectedGame != ProjectGame.ZA)
            {
                return new ZaDexLayoutMainAnalysis(
                    ZaDexLayoutMainKind.GameMismatch,
                    "Dex Layout will not patch exefs/main because the selected project is not Pokemon Legends Z-A.",
                    buildId,
                    RegularCount: null,
                    ProjectGame.ZA);
            }

            var text = nso.Text.DecompressedData;
            var counts = new HashSet<int>();
            foreach (var patch in InstructionPatches)
            {
                if (patch.Offset < 0 || patch.Offset > text.Length - sizeof(uint))
                {
                    return CreateConflict(
                        buildId,
                        $"Dex Layout instruction offset 0x{patch.Offset:X8} is outside exefs/main .text.");
                }

                var instruction = BinaryPrimitives.ReadUInt32LittleEndian(
                    text.AsSpan(patch.Offset, sizeof(uint)));
                if ((instruction & ~ImmediateMask) != patch.OpcodeWithoutImmediate)
                {
                    return CreateConflict(
                        buildId,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Dex Layout found an unexpected instruction at main.text+0x{patch.Offset:X8}."));
                }

                counts.Add((int)((instruction & ImmediateMask) >> 10));
            }

            if (counts.Count != 1)
            {
                return CreateConflict(
                    buildId,
                    "Dex Layout found inconsistent Regular Dex boundaries in exefs/main.");
            }

            var regularCount = counts.Single();
            if (regularCount is <= 0 or >= TotalDexSpeciesCount)
            {
                return CreateConflict(
                    buildId,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Dex Layout found unsupported Regular Dex boundary {regularCount}."));
            }

            var kind = regularCount == VanillaRegularCount
                ? ZaDexLayoutMainKind.Vanilla
                : ZaDexLayoutMainKind.Modified;
            var message = kind == ZaDexLayoutMainKind.Vanilla
                ? "Pokédex number normalization uses the base Regular Dex boundary of 232."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Pokédex number normalization uses a custom Regular Dex boundary of {regularCount}.");

            return new ZaDexLayoutMainAnalysis(
                kind,
                message,
                buildId,
                regularCount,
                ProjectGame.ZA);
        }
        catch (InvalidDataException exception)
        {
            return new ZaDexLayoutMainAnalysis(
                ZaDexLayoutMainKind.Conflict,
                exception.Message,
                "unknown",
                RegularCount: null,
                DetectedGame: null);
        }
    }

    public static byte[] ApplyRegularCount(
        byte[] mainBytes,
        int regularCount,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ValidateRegularCount(regularCount);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is ZaDexLayoutMainKind.UnsupportedBuild
            or ZaDexLayoutMainKind.GameMismatch
            or ZaDexLayoutMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        foreach (var patch in InstructionPatches)
        {
            var instruction = patch.OpcodeWithoutImmediate | ((uint)regularCount << 10);
            BinaryPrimitives.WriteUInt32LittleEndian(
                text.AsSpan(patch.Offset, sizeof(uint)),
                instruction);
        }

        var output = nso.Write(textDecompressedData: text);
        ValidateOutput(mainBytes, output, regularCount, expectedGame);
        return output;
    }

    public static IReadOnlyList<ZaExeFsReservedRegion> ReservedMainTextRegions()
    {
        return ZaExeFsReservedRegionLedger.MainTextRegionsForOwner(
            ZaExeFsReservedRegionLedger.OwnerDexLayout);
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        int expectedRegularCount,
        ProjectGame? expectedGame)
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        if (!before.BuildId.SequenceEqual(after.BuildId))
        {
            throw new InvalidDataException("Dex Layout changed the NSO build ID.");
        }

        if (!before.Ro.DecompressedData.SequenceEqual(after.Ro.DecompressedData)
            || !before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException(
                "Dex Layout unexpectedly changed a non-text NSO segment.");
        }

        var beforeText = before.Text.DecompressedData;
        var afterText = after.Text.DecompressedData;
        if (beforeText.Length != afterText.Length)
        {
            throw new InvalidDataException(
                "Dex Layout changed the decompressed .text segment size.");
        }

        for (var offset = 0; offset < beforeText.Length; offset++)
        {
            if (beforeText[offset] == afterText[offset])
            {
                continue;
            }

            var owned = InstructionPatches.Any(patch =>
                offset >= patch.Offset && offset < patch.Offset + sizeof(uint));
            if (!owned)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Dex Layout unexpectedly changed main.text byte 0x{offset:X}."));
            }
        }

        var outputAnalysis = Analyze(output, expectedGame);
        if (outputAnalysis.Kind is ZaDexLayoutMainKind.UnsupportedBuild
            or ZaDexLayoutMainKind.GameMismatch
            or ZaDexLayoutMainKind.Conflict
            || outputAnalysis.RegularCount != expectedRegularCount)
        {
            throw new InvalidDataException(
                "Dex Layout verification failed after writing exefs/main.");
        }
    }

    private static void ValidateRegularCount(int regularCount)
    {
        if (regularCount is <= 0 or >= TotalDexSpeciesCount)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Regular Dex count must be between 1 and {TotalDexSpeciesCount - 1}."));
        }
    }

    private static ZaDexLayoutMainAnalysis CreateConflict(string buildId, string message)
    {
        return new ZaDexLayoutMainAnalysis(
            ZaDexLayoutMainKind.Conflict,
            message,
            buildId,
            RegularCount: null,
            ProjectGame.ZA);
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
    }

    private sealed record InstructionPatch(int Offset, uint OpcodeWithoutImmediate);
}
