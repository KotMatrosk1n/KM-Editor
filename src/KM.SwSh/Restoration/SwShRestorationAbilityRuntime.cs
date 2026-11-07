// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Owns one independent runtime module and its immutable dispatch surface.</summary>
public static class SwShRestorationAbilityRuntime
{
    public const string ModulePath = "exefs/subsdk8";
    internal const string Owner = "Restoration Abilities";
    private const string ModuleHash = "4E2B306255307485BEFFB362D554A6A1450EFAC8D5372540B2E3E2064AFF3073";
    private const string CompletedAbilitiesModuleHash = "7F64FC73C1BCC21DA75DBAC86BAC77C011EA1A1DB83205927C29E8AB4CB60B6B";
    private const string DamageAbilitiesModuleHash = "4DCFC0F1786A7FED2103BD6F3C016CD82A7F92F9CFD046FAE0E2F219955B7EFC";
    private const string MindsEyeModuleHash = "C8ED4C125DB39EDDAB142351891C47AAB54F63DDB642E8DE40E031C733EB48D5";
    private const string ToxicChainOnlyModuleHash = "B2F4ED7DF99127280088F825E08C243B1231AE7B705127CD5DCA37F35561FA2B";
    private const string PriorRestorationModuleHash = "749794919615EC537C4968331B1D2E2BCA41052B6C9C7563A1B034F1E4AFA0A4";
    private const string PriorRestorationRockyPayloadModuleHash = "DF2BB303DB03E3BB210E2FFE466EBDB499DA60AD3232B96D9EB14A7BBBA0B4E1";
    private const string PriorRestorationRuinModuleHash = "AEA84FBD5256CF78AEC8E307C61EFABE5762B183929B5E437ECB970C361C8C00";
    private const string PriorRestorationEarthEaterModuleHash = "ACAA0919AD0024C3D5B5200F067AE2C004DFDDA4AA44BBE24BFCAF44E24DB1FC";
    private const string PriorRestorationGuardDogModuleHash = "B6786A87F3D6BDF74E9D8BA812A7BE08810B8D403DE97DA9D5767C447F1BD285";
    private const string PriorRestorationGoodAsGoldModuleHash = "28B6F70B52909BC3CC3BF2246EE75166B7885AFDB3BCC2151154AE4243EEEC36";
    private const string TableHash = "6A0CCECCDA51190EF39C13594D48E0381CD949FB9D6728103AA37FDC20E83750";
    private const string WordsHash = "FA3808516F8BC76DF9AF3E0B7BA78C7650ACAF846B6ECA6BCDA150F761070043";
    private const string BreakableHash = "662A53D6C5C48180606DC7C0A5A83AED4316796E2B96B67DCFA975348A6E43C6";
    private static readonly (int Offset, int Length)[] Sites =
    [
        (0x8887d4, 8), (0x888810, 8), (0x8889d0, 8), (0x888a0c, 8), (0x888a90, 8), (0x888acc, 8),
        (0x888800, 4), (0x8889fc, 4), (0x888abc, 4),
        (0x83eb90, 16), (0x7ec900, 16), (0x843e60, 16), (0x843f20, 16), (0x843f60, 16), (0x83f310, 16),
        (0x83ebf0, 16),
        (0x7dd6a4, 8), (0x7dd6cc, 4),
        (0x88afd0, 16), (0x88afe0, 16), (0x88aff0, 16), (0x88f960, 16), (0x88f9c0, 16),
        (0x88fa30, 16), (0x88adf0, 16), (0x88aeb0, 16), (0x7dd6a0, 16),
        (0x83ebd0, 16),
        (0x83ec40, 16),
        (0x780b10, 16),
        (0x8412d0, 16),
        (0x88f140, 16),
        (0x88f270, 16),
        (0x83f640, 16),
        (0x83f010, 16),
        (0x83f090, 16),
        (0x83f400, 16),
        (0x83eda0, 16),
        (0x83f970, 16),
        (0x88b6e0, 16),
        (0x88b1c0, 16),
        (0x88b710, 16),
        (0x88b760, 16),
        (0x88b480, 16),
        (0x898350, 16),
        (0x83ec60, 16),
        (0x7e12a0, 16),
        (0x8415b0, 16),
        (0x7dd580, 16),
        (0x83ed80, 16),
        (0x83ed70, 16),
        (0x83ee50, 16),
        (0x841920, 16),
        (0x7e1890, 16),
        (0x7ec2f0, 16),
        (0x83fb80, 16),
        (0x83f7a0, 16),
        (0x83f110, 16),
        (0x83ec30, 16),
        (0x7ec2a0, 16),
        (0x83fa30, 16),
        (0x88c0b0, 16),
        (0x8999b0, 16),
        (0x888d10, 4),
        (0x7e3740, 16),
        (0x7e28f0, 16),
        (0x7e1810, 16),
        (0x7e1aa0, 16),
        (0x83f440, 16),
        (0x83f6f0, 16),
        (0x83f730, 16),
        (0x83f290, 16),
        (0x88fc20, 16),
        (0x88fc90, 16),
        (0x88fd50, 16),
        (0x8943c0, 16),
        (0x805270, 16),
        (0x7e1720, 16),
        (0x840d40, 16),
        (0x80530c, 4),
        (0x831960, 16),
        (0x835550, 16),
        (0x83ed30, 16),
        (0x7daa40, 16),
        (0x7dd0a0, 16),
        (0x7dd130, 16),
        (0x7dba00, 16),
        (0x870880, 16),
        (0x7db870, 16),
        (0x7db860, 16),
        (0x83ec30, 16),
        (0x874590, 16),
        (0x874690, 16),
        (0x874700, 16),
        (0x874770, 16),
        (0x8747e0, 16),
        (0x86f3a0, 16),
        (0x87c088, 4),
        (0x827a30, 16),
        (0x7db2b0, 16),
        (0x7db2f0, 16),
        (0x7db3f0, 16),
        (0x7db460, 16),
        (0x7dad40, 16),
        (0x7db610, 16),
        (0x7e0a00, 16),
        (0x7da800, 16),
        (0x7daa10, 16),
        (0x8416b0, 16),
        (0x7e55a0, 16),
        (0x7d9f70, 4),
        (0x7da5fc, 4),
        (0x82cd24, 4),
        (0x7e1290, 16),
        (0x7e1570, 16),
        (0x840150, 16),
        (0x83a6f0, 16),
        (0x838590, 16),
        (0x83a6a0, 16),
        (0x8140f0, 16),
        (0x7e4ab0, 16),
        (0x7e4aa0, 16),
        (0x7dd530, 16),
        (0x7dd580, 16),
        (0x7dd610, 16),
        (0x7dd6f0, 16),
        (0x851704, 4),
        (0x94850c, 4),
        (0x9485f0, 4),
        (0x7ec080, 16),
        (0x8423e0, 16),
        (0x8440e0, 16),
        (0x863f28, 4),
        (0x780b10, 16),
        (0x8894c0, 16),
        (0x890930, 16),
        (0x8909a0, 16),
        (0x890a00, 16),
        (0x7e17b8, 16),
        (0x7e17c8, 16),
        (0x7e17d8, 16),
        (0x7e17e8, 16),
        (0x7e17f8, 16),
        (0x7e172c, 16),
        (0x832ae0, 16),
        (0x83f9f0, 16),
        (0x7e2d10, 16),
        (0x83fd20, 16),
        (0x8502f8, 16),
        (0x850310, 16),
        (0x850388, 16),
        (0x82e85c, 16),
        (0x788fc0, 16),
        (0x83fce0, 16),
        (0x852a80, 16),
        (0x852b28, 16),
        (0x852b54, 16),
        (0x852b8c, 16),
        (0x8a99f0, 16),
        (0x87ef74, 4),
        (0x8a8050, 4),
        (0x876690, 4),
        (0x87675c, 4),
        (0x876770, 4),
        (0x876810, 4),
        (0x876c78, 4),
        (0x876cdc, 4),
        (0x87e2bc, 4),
        (0x87e33c, 4),
        (0x87eaec, 4),
        (0x87ebdc, 4),
        (0x881fec, 4),
        (0x826b20, 4),
        (0x826b50, 16),
        (0x826d44, 16),
        (0x826dd4, 16),
        (0x7db640, 16),
        (0x83fa30, 16),
        (0x7e2df0, 16),
        (0x780a00, 16),
        (0x780c60, 16),
        (0x89b3b0, 16),
        (0x89b410, 16),
        (0x89b470, 16),
        (0x7dad50, 16),
        (0x802050, 16),
        (0x82da80, 16),
        (0x84ec70, 16),
        (0x83a080, 16),
        (0x781630, 16),
        (0x781670, 16),
        (0x781690, 16),
        (0x7db8c0, 16),
        (0x7db9d0, 16),
        (0x7dafa0, 16),
        (0x7dafb0, 16),
        (0x7db080, 16),
        (0x87a7d8, 16),
        (0x87a7e8, 16),
        (0x87a810, 16),
        (0x87a82c, 4),
    ];

    public static bool IsModule(ReadOnlySpan<byte> bytes) => bytes.Length is > 0 and <= 64 * 1024
        && Convert.ToHexString(SHA256.HashData(bytes)) is ModuleHash or CompletedAbilitiesModuleHash or ToxicChainOnlyModuleHash or DamageAbilitiesModuleHash or MindsEyeModuleHash or PriorRestorationModuleHash or PriorRestorationRockyPayloadModuleHash or PriorRestorationRuinModuleHash or PriorRestorationEarthEaterModuleHash or PriorRestorationGuardDogModuleHash or PriorRestorationGoodAsGoldModuleHash;

    public static byte[] CreateModule(byte[] baselineMain, byte[] effectiveMain, ProjectGame game)
    {
        var baseline = ValidateMain(baselineMain, game);
        var current = ValidateMain(effectiveMain, game);
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline, current, Owner);
        foreach (var site in Sites)
            if (SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game, Owner)
                .Any(other => SwShExeFsReservedRegionLedger.Overlaps(other, site.Offset, site.Length)))
                throw new InvalidDataException("Restoration ability support overlaps another executable feature.");
        using var input = typeof(SwShRestorationAbilityRuntime).Assembly.GetManifestResourceStream(
            "KM.SwSh.Resources.swsh-restoration-abilities.nso")
            ?? throw new InvalidDataException("The restoration ability runtime is unavailable.");
        if (input.Length is < 1 or > 64 * 1024) throw new InvalidDataException("The restoration ability runtime is invalid.");
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != ModuleHash)
            throw new InvalidDataException("The restoration ability runtime failed identity verification.");
        return bytes;
    }

    private static NsoFile ValidateMain(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0)
            throw new InvalidDataException("Restoration abilities require the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var site in Sites)
        {
            if (site.Offset > main.Text.DecompressedData.Length - site.Length)
                throw new InvalidDataException("The restoration ability ABI is unavailable.");
            fingerprint.AppendData(main.Text.DecompressedData.AsSpan(site.Offset, site.Length));
        }
        var tableOffset = checked(0x2525ed0 - (int)main.Data.Header.MemoryOffset);
        var breakableOffset = checked(0x2048a38 - (int)main.Ro.Header.MemoryOffset);
        if (tableOffset < 0 || tableOffset > main.Data.DecompressedData.Length - 260 * 16
            || Convert.ToHexString(SHA256.HashData(main.Data.DecompressedData.AsSpan(tableOffset, 260 * 16))) != TableHash
            || breakableOffset < 0 || breakableOffset > main.Ro.DecompressedData.Length - 140
            || Convert.ToHexString(SHA256.HashData(main.Ro.DecompressedData.AsSpan(breakableOffset, 140))) != BreakableHash
            || Convert.ToHexString(fingerprint.GetHashAndReset()) != WordsHash)
            throw new InvalidDataException("The restoration ability dispatch or native ABI contains incompatible changes.");
        return main;
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() => Sites.Select(site =>
        new SwShExeFsReservedRegion(Owner, $"restoration-ability-{site.Offset:X}", "exefs/main", "main.text",
            site.Offset, site.Length, "Restored ability registration and native ABI", "do-not-overwrite"))
        .Append(new(Owner, "restoration-ability-module", ModulePath, "module", null, null,
            "Restored ability runtime", "do-not-overwrite"))
        .Append(new(Owner, "restoration-ability-breakable-list", "exefs/main", "main.ro", 0x747a38, 140,
            "Native ability suppression identities", "do-not-overwrite")).ToArray();
}
