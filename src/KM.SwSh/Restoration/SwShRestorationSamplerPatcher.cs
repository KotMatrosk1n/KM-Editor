// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Owns exact NVN clamp samplers independently of battle runtime modules.</summary>
public static class SwShRestorationSamplerPatcher
{
    public const int SerializedClampMode = 4;
    internal const string Owner = "Restoration Samplers";
    private const int RoOffset = 0x1901000;
    private sealed record Region(int Offset, byte[] Original, byte[] Replacement);
    private static readonly Region[] Regions =
    [
        new(0x61841C, Convert.FromHexString("615A68B8"), Convert.FromHexString("E103082A")),
        new(0x618450, Convert.FromHexString("615A68B8"), Convert.FromHexString("E103082A")),
        new(0x618484, Convert.FromHexString("615A68B8"), Convert.FromHexString("E103082A")),
        new(0x5FC670, Convert.FromHexString("69B200D029C12D91E803003229D961B809C0053908800739C0035FD600000000"), Convert.FromHexString("09CA8A522804010B2925C81A2909001209C005392800805208800739C0035FD6")),
        new(0x5FC690, Convert.FromHexString("69B200D029C12D91E803003229D961B809C4053908800739C0035FD600000000"), Convert.FromHexString("09CA8A522804010B2925C81A2909001209C405392800805208800739C0035FD6")),
        new(0x5FC6B0, Convert.FromHexString("69B200D029C12D91E803003229D961B809C8053908800739C0035FD600000000"), Convert.FromHexString("09CA8A522804010B2925C81A2909001209C805392800805208800739C0035FD6")),
    ];
    // Stock GF0..3 -> descriptor0/2/1/3 is byte-exact. Own GF4 ->
    // descriptor5 reaches the pinned renderer's NVN_CLAMP0 argument.
    // The stock GF enum4 lookup is not used or assumed to support clamp.
    // Each leaf uses its existing32-byte function+padding slot, preserves W1,
    // finalW8/W9, every other register/NZCV and all native0..3 memory writes.
    // The immutable renderer and its literal are guarded below; no driver or
    // ability module is modified. All six text regions form one version.
    internal static (NsoFile Main, bool Installed) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0 || main.Text.DecompressedData.Length < 0x18A2B60
            || main.Ro.Header.MemoryOffset != RoOffset || main.Ro.DecompressedData.Length < 0x2088378 - RoOffset)
            throw new InvalidDataException("Source clamp samplers require the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The sampler executable segment checksum is invalid.");
        int versions = 3;
        foreach (var region in Regions)
        {
            var value = main.Text.DecompressedData.AsSpan(region.Offset, region.Original.Length);
            versions &= value.SequenceEqual(region.Original) ? 1 : value.SequenceEqual(region.Replacement) ? 2 : 0;
        }
        if (versions == 0) throw new InvalidDataException("The source sampler routines contain incompatible or mixed changes.");
        var parser = main.Text.DecompressedData.AsSpan(0x6183E4, 0x134).ToArray();
        foreach (var region in Regions.Where(r => r.Offset >= 0x6183E4 && r.Offset < 0x618518))
            region.Original.CopyTo(parser, region.Offset - 0x6183E4);
        int renderer = game == ProjectGame.Sword ? 0x18A2920 : 0x18A29B0;
        int constructor = game == ProjectGame.Sword ? 0x189BFE0 : 0x189C070;
        if (Convert.ToHexString(SHA256.HashData(parser)) != "AE2C280F7C7441E2CE3258D6E214F066FFBB61D630E7D8B80DCF06119D74F922"
            || Convert.ToHexString(SHA256.HashData(main.Text.DecompressedData.AsSpan(renderer, 0x1B0))) != "2CAE5805DA716534CC417FD413C8E880BA59FC0C93207AFB2D41266A3174B470"
            || Convert.ToHexString(SHA256.HashData(main.Text.DecompressedData.AsSpan(constructor, 0x2C))) != "52E2178AF61E9254CB954901D3E79F4E9729CFE0B02F5DDA510748234F89CEB8"
            || !main.Ro.DecompressedData.AsSpan(0x1C4B1D0 - RoOffset, 16).SequenceEqual(Convert.FromHexString("00000000010000000200000003000000"))
            || !main.Ro.DecompressedData.AsSpan(0x1C4AB70 - RoOffset, 16).SequenceEqual(Convert.FromHexString("00000000020000000100000003000000"))
            || !main.Ro.DecompressedData.AsSpan(0x2088360 - RoOffset, 24).SequenceEqual(Convert.FromHexString("010000000600000007000000050000000300000000000000")))
            throw new InvalidDataException("The source sampler parser, renderer or descriptor lookup contains incompatible changes.");
        return (main, versions == 2);
    }

    public static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, true);
    internal static byte[] RestoreFromBase(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, false);
    private static byte[] Rewrite(byte[] baseline, byte[] source, ProjectGame game, bool enabled)
    {
        var original = Read(baseline, game); var current = Read(source, game);
        if (original.Installed) throw new InvalidDataException("Base ExeFS must contain the original sampler routines.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main, current.Main, Owner);
        if (Regions.Any(region => SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game, Owner)
            .Any(other => SwShExeFsReservedRegionLedger.Overlaps(other, region.Offset, region.Original.Length))))
            throw new InvalidDataException("Source sampler support overlaps another executable feature.");
        if (current.Installed == enabled) return source;
        var text = current.Main.Text.DecompressedData.ToArray();
        foreach (var region in Regions)
            (enabled ? region.Replacement : region.Original).CopyTo(text, region.Offset);
        var output = current.Main.Write(textDecompressedData: text); var after = Read(output, game);
        if (after.Installed != enabled || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(current.Main.Ro.DecompressedData)
            || !after.Main.Data.DecompressedData.SequenceEqual(current.Main.Data.DecompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Source sampler output failed preservation verification.");
        return output;
    }

    public static bool HasInstalledPatch(byte[] bytes)
    {
        try
        {
            var game = SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId));
            return game is not null && Read(bytes, game.Value).Installed;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() =>
    [
        .. Regions.Select(r => new SwShExeFsReservedRegion(Owner, $"restoration-source-sampler-{r.Offset:x}", "exefs/main", "main.text",
            r.Offset, r.Original.Length, "Source sampler clamp ABI", "payload-only")),
        new(Owner, "restoration-source-sampler-parser-context", "exefs/main", "main.text", 0x6183E4, 0x134, "Source sampler parser dependency", "do-not-overwrite"),
        new(Owner, "restoration-source-sampler-renderer-context", "exefs/main", "main.text", 0x18A2920, 0x240, "Source sampler renderer dependency", "do-not-overwrite"),
        new(Owner, "restoration-source-sampler-constructor-context", "exefs/main", "main.text", 0x189BFE0, 0xBC, "Source sampler constructor dependency", "do-not-overwrite"),
        new(Owner, "restoration-source-sampler-gf-map", "exefs/main", "main.ro", 0x1C4B1D0 - RoOffset, 16, "Native GF address identity dependency", "do-not-overwrite"),
        new(Owner, "restoration-source-sampler-setter-map", "exefs/main", "main.ro", 0x1C4AB70 - RoOffset, 16, "Native setter address dependency", "do-not-overwrite"),
        new(Owner, "restoration-source-sampler-nvn-map", "exefs/main", "main.ro", 0x2088360 - RoOffset, 24, "Native NVN clamp argument dependency", "do-not-overwrite"),
    ];
}
