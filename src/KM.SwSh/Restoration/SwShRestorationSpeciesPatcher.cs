// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Supports registered species and alternate forms through the guarded native identity dispatch.</summary>
public static class SwShRestorationSpeciesPatcher
{
    internal const string Owner = "Restoration Species";
    private sealed record Region(int Offset, int Length, string SwordHash, string ShieldHash, byte[] Replacement, byte[]? LegacyReplacement = null);
    private static Region[] Regions(ProjectGame game) =>
    [
        new(0x77F540, 0xC0, "EC331E954CFB77895CA68038AFE9D7DCDD20F6498E92E0D98C7E798195FA4069", "60910881CA92DF088E6C3AEB0B61FC78EC0C82F25E266C6C5AB76764CAA56353", ExtendedPersonalRecord(game), game == ProjectGame.Sword ? SWPersonalRecord() : SHPersonalRecord()),
        new(0x77F600, 0x78, "94A6BB06AF7A6A2E34749771B92A6A9F2B01A63291E58E10E7CF87AC982AEC7F", "94A6BB06AF7A6A2E34749771B92A6A9F2B01A63291E58E10E7CF87AC982AEC7F", ExtendedPersonalIndex(), SWPersonalIndex()),
        new(0x7803B0, 0x80, "393853EA5655EDB57FE17C548A7EFF179C11CE2BE930265B2D6209CD7C3D8832", "393853EA5655EDB57FE17C548A7EFF179C11CE2BE930265B2D6209CD7C3D8832", ExtendedEggMoves(), SWEggMoves()),
        new(0x764828, 0x4, "6273941B21F6F9047BF44AADD796C0A70DDE78AAFA722EF8E3BF0F4D4EC26311", "6273941B21F6F9047BF44AADD796C0A70DDE78AAFA722EF8E3BF0F4D4EC26311", SWNameRangeOne()),
        new(0x7648AC, 0x4, "6273941B21F6F9047BF44AADD796C0A70DDE78AAFA722EF8E3BF0F4D4EC26311", "6273941B21F6F9047BF44AADD796C0A70DDE78AAFA722EF8E3BF0F4D4EC26311", SWNameRangeTwo()),
        new(0x76492C, 0x4, "089DD51486867EB480937270F84538D17006F703EFB08C098D14CDBD3C296749", "089DD51486867EB480937270F84538D17006F703EFB08C098D14CDBD3C296749", SWNameRangeThree()),
        new(0x764854, 0x4, "DC953AF83E75A53FB5A55F68D9025B690BC339C8FCB3798EAF813EC1AE62A042", "DC953AF83E75A53FB5A55F68D9025B690BC339C8FCB3798EAF813EC1AE62A042", SWNameIndexOne()),
        new(0x7648D0, 0x4, "D842055D8080A94350581A591A4629E3E43B2D7646EAA54FFB5BD2BE0CC43521", "D842055D8080A94350581A591A4629E3E43B2D7646EAA54FFB5BD2BE0CC43521", SWNameIndexTwo()),
        new(0x76494C, 0x4, "C7C582903BE9FD63963FCBDDC0188C41CDE5B26C612CD3CA09A47B1FD75F9D88", "C7C582903BE9FD63963FCBDDC0188C41CDE5B26C612CD3CA09A47B1FD75F9D88", SWNameIndexThree()),
    ];

    internal static (NsoFile Main, bool Installed, bool IndependentForms, bool Extended) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0 || main.Text.DecompressedData.Length < 0x785D50)
            throw new InvalidDataException("New species support requires the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        // Intersect complete known versions. Mixed legacy/current regions are
        // rejected even when every individual region happens to be recognized.
        var versions = 7; var independentForms = false;
        foreach (var region in Regions(game))
        {
            var current = main.Text.DecompressedData.AsSpan(region.Offset, region.Length);
            var normalized = current.ToArray();
            if (region.Offset == 0x77F600
                && BinaryPrimitives.ReadUInt32LittleEndian(normalized.AsSpan(0x3c)) == 0xD503201F)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(normalized.AsSpan(0x3c), 0x363001AA);
                independentForms = true;
            }
            var recognized = Convert.ToHexString(SHA256.HashData(current)) == (game == ProjectGame.Sword ? region.SwordHash : region.ShieldHash) ? 1 : 0;
            if (normalized.AsSpan().SequenceEqual(region.LegacyReplacement ?? region.Replacement)) recognized |= 2;
            if (normalized.AsSpan().SequenceEqual(region.Replacement)) recognized |= 4;
            versions &= recognized;
            if (versions == 0)
                throw new InvalidDataException("The species lookup routines contain incompatible changes.");
        }
        var installed = (versions & 6) != 0;
        var helpers = main.Text.DecompressedData.AsSpan(0x77FF80, 0x420).ToArray()
            .Concat(main.Text.DecompressedData.AsSpan(0x780430, 0x190).ToArray()).ToArray();
        if (Convert.ToHexString(SHA256.HashData(helpers)) != (game == ProjectGame.Sword ? "5A29BA8B609AA67B36310F0F2FFD35AB55EDB5E3D0F3961EFC3072B288BCED8B" : "7447D3A3DB4603B37CBF511CF749C737411827CA1C020D9121DDF0983469AD77"))
            throw new InvalidDataException("The native species data helpers contain incompatible changes.");
        var names = main.Text.DecompressedData.AsSpan(0x764810, 0x180).ToArray();
        if (installed)
        {
            Convert.FromHexString("3F100E71").CopyTo(names, 0x18);
            Convert.FromHexString("3F100E71").CopyTo(names, 0x9C);
            Convert.FromHexString("9F120E71").CopyTo(names, 0x11C);
            Convert.FromHexString("6B6B0094").CopyTo(names, 0x44);
            Convert.FromHexString("4C6B0094").CopyTo(names, 0xC0);
            Convert.FromHexString("2D6B0094").CopyTo(names, 0x13C);
        }
        if (Convert.ToHexString(SHA256.HashData(names)) != (game == ProjectGame.Sword ? "0EFF3E3746336D8A587A21EA18A1FC9F8781352EAD752E554C002D81F0D2B53A" : "0EFF3E3746336D8A587A21EA18A1FC9F8781352EAD752E554C002D81F0D2B53A"))
            throw new InvalidDataException("The native species name helpers contain incompatible changes.");
        var allocationAndTextBounds = main.Text.DecompressedData.AsSpan(0x77F160, 0x2F4).ToArray()
            .Concat(main.Text.DecompressedData.AsSpan(0x785A40, 0x310).ToArray())
            .Concat(main.Text.DecompressedData.AsSpan(0x67E740, 0x28).ToArray()).ToArray();
        if (Convert.ToHexString(SHA256.HashData(allocationAndTextBounds)) != (game == ProjectGame.Sword ? SwordLoaderHash : ShieldLoaderHash))
            throw new InvalidDataException("The native Pokemon table allocation or text bounds contain incompatible changes.");
        return (main, installed, independentForms, (versions & 4) != 0);
    }

    public static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, true);
    public static byte[] ApplyIndependentForms(byte[] baseline, byte[] source, ProjectGame game)
        => Rewrite(baseline, source, game, true, independentForms: true);
    internal static byte[] RestoreFromBase(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, false);

    private static byte[] Rewrite(byte[] baseline, byte[] source, ProjectGame game, bool enabled, bool independentForms = false)
    {
        var original = Read(baseline, game); var current = Read(source, game);
        if (original.Installed) throw new InvalidDataException("Base ExeFS must contain the original species lookup routines.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main, current.Main, Owner);
        var regions = Regions(game);
        if (regions.Any(region => SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game, Owner)
            .Any(other => SwShExeFsReservedRegionLedger.Overlaps(other, region.Offset, region.Length))))
            throw new InvalidDataException("Species support overlaps another executable feature.");
        independentForms |= current.IndependentForms;
        if (current.Installed == enabled && (!enabled || current.IndependentForms == independentForms && current.Extended)) return source;
        var text = current.Main.Text.DecompressedData.ToArray();
        foreach (var region in regions)
            (enabled ? region.Replacement.AsSpan() : original.Main.Text.DecompressedData.AsSpan(region.Offset, region.Length)).CopyTo(text.AsSpan(region.Offset));
        // The shared dispatch still checks the form range and declared personal row.
        // Only the base-species presence requirement is omitted for an installed alternate form.
        if (enabled && independentForms) BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(0x77F63C), 0xD503201F);
        var output = current.Main.Write(textDecompressedData: text); var after = Read(output, game);
        if (after.Installed != enabled || after.IndependentForms != (enabled && independentForms) || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(current.Main.Ro.DecompressedData)
            || !after.Main.Data.DecompressedData.SequenceEqual(current.Main.Data.DecompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Species support output failed preservation verification.");
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

    public static bool HasExtendedSpeciesPatch(byte[] bytes)
    {
        try
        {
            var game = SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId));
            return game is not null && Read(bytes, game.Value).Extended;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() =>
    [
        new(Owner, "restoration-species-personal-record", "exefs/main", "main.text", 0x77F540, 0xC0, "Registered species personal record", "payload-only"),
        new(Owner, "restoration-species-personal-index", "exefs/main", "main.text", 0x77F600, 0x78, "Registered species personal index", "payload-only"),
        new(Owner, "restoration-species-egg-moves", "exefs/main", "main.text", 0x7803B0, 0x80, "Registered species egg moves", "payload-only"),
        new(Owner, "restoration-species-name-range-one", "exefs/main", "main.text", 0x764828, 0x4, "Registered species name range one", "payload-only"),
        new(Owner, "restoration-species-name-range-two", "exefs/main", "main.text", 0x7648AC, 0x4, "Registered species name range two", "payload-only"),
        new(Owner, "restoration-species-name-range-three", "exefs/main", "main.text", 0x76492C, 0x4, "Registered species name range three", "payload-only"),
        new(Owner, "restoration-species-name-index-one", "exefs/main", "main.text", 0x764854, 0x4, "Registered species name index one", "payload-only"),
        new(Owner, "restoration-species-name-index-two", "exefs/main", "main.text", 0x7648D0, 0x4, "Registered species name index two", "payload-only"),
        new(Owner, "restoration-species-name-index-three", "exefs/main", "main.text", 0x76494C, 0x4, "Registered species name index three", "payload-only"),
    ];

    // The constructor retains the logical species in its object. Only the table
    // address changes. The ordinary path preserves the native form calculation.
    private static byte[] SWPersonalRecord() => Words(192, [
            0xF81D0FF5U, // str x21, [sp, #-0x30]!
            0xA9014FF4U, // stp x20, x19, [sp, #0x10]
            0xA9027BFDU, // stp x29, x30, [sp, #0x20]
            0x910083FDU, // add x29, sp, #0x20
            0x2A0203F3U, // mov w19, w2
            0x2A0103F5U, // mov w21, w1
            0xAA0003F4U, // mov x20, x0
            0x2A0103E0U, // mov w0, w1
            0x2A0203E1U, // mov w1, w2
            0x94000027U, // bl #0x77f600
            0x9000F4C9U, // adrp x9, #0x2617000
            0xF941F129U, // ldr x9, [x9, #0x3e0]
            0xF9400129U, // ldr x9, [x9]
            0x52801602U, // mov w2, #0xb0
            0x9BA22401U, // umaddl x1, w0, w2, x9
            0x91004280U, // add x0, x20, #0x10
            0x9445FA10U, // bl #0x18fddc0
            0xB9000A95U, // str w21, [x20, #8]
            0x79001A93U, // strh w19, [x20, #0xc]
            0xA9427BFDU, // ldp x29, x30, [sp, #0x20]
            0xA9414FF4U, // ldp x20, x19, [sp, #0x10]
            0xF84307F5U, // ldr x21, [sp], #0x30
            0xD65F03C0U, // ret
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0x710F4C3FU, // cmp w1, #0x3d3
            0x54000040U, // b.eq #0x77f5dc
            0x710E103FU, // cmp w1, #0x384
            0xD65F03C0U, // ret
            0x710F4C1FU, // cmp w0, #0x3d3
            0x540000E1U, // b.ne #0x77f600
            0xD65F03C0U, // ret
            0xD503201FU, // nop
            0x710F4E9FU, // cmp w20, #0x3d3
            0x54000040U, // b.eq #0x77f5fc
            0x710E129FU, // cmp w20, #0x384
            0xD65F03C0U, // ret
        ]);

    private static byte[] SWPersonalIndex() => Words(120, [
            0x710F4C1FU, // cmp w0, #0x3d3
            0x54000061U, // b.ne #0x77f610
            0x52808720U, // mov w0, #0x439
            0xD65F03C0U, // ret
            0x2A1F03E8U, // mov w8, wzr
            0x710E081FU, // cmp w0, #0x382
            0x540002ACU, // b.gt #0x77f66c
            0x72003C29U, // ands w9, w1, #0xffff
            0x54000280U, // b.eq #0x77f670
            0x9000F4C8U, // adrp x8, #0x2617000
            0xF941F108U, // ldr x8, [x8, #0x3e0]
            0xF9400108U, // ldr x8, [x8]
            0x5280160AU, // mov w10, #0xb0
            0x9BAA200AU, // umaddl x10, w0, w10, x8
            0x3940854AU, // ldrb w10, [x10, #0x21]
            0x363001AAU, // tbz w10, #6, #0x77f670
            0x2A0003EAU, // mov w10, w0
            0x5280160BU, // mov w11, #0xb0
            0x9B0B2148U, // madd x8, x10, x11, x8
            0x79403D0AU, // ldrh w10, [x8, #0x1e]
            0x3940810BU, // ldrb w11, [x8, #0x20]
            0x2A0003E8U, // mov w8, w0
            0x7100015FU, // cmp w10, #0
            0x7A491160U, // ccmp w11, w9, #0, ne
            0x54000069U, // b.ls #0x77f66c
            0x0B0A0128U, // add w8, w9, w10
            0x51000508U, // sub w8, w8, #1
            0x2A0803E0U, // mov w0, w8
            0xD65F03C0U, // ret
            0xD503201FU, // nop
        ]);

    private static byte[] SWEggMoves() => Words(128, [
            0xD101C3FFU, // sub sp, sp, #0x70
            0xA9054FF4U, // stp x20, x19, [sp, #0x50]
            0xA9067BFDU, // stp x29, x30, [sp, #0x60]
            0x910183FDU, // add x29, sp, #0x60
            0x2A0103E8U, // mov w8, w1
            0xAA0003F3U, // mov x19, x0
            0x2A1F03E1U, // mov w1, wzr
            0x710F4D1FU, // cmp w8, #0x3d3
            0x540002C0U, // b.eq #0x780428
            0x710E091FU, // cmp w8, #0x382
            0x5400018CU, // b.gt #0x780408
            0x2A0203F4U, // mov w20, w2
            0x72003C5FU, // tst w2, #0xffff
            0x54000100U, // b.eq #0x780404
            0x910023E0U, // add x0, sp, #8
            0x2A0803E1U, // mov w1, w8
            0x94000010U, // bl #0x780430
            0x794013E8U, // ldrh w8, [sp, #8]
            0x0B342108U, // add w8, w8, w20, uxth
            0x51000501U, // sub w1, w8, #1
            0x14000002U, // b #0x780408
            0x2A0803E1U, // mov w1, w8
            0x91002A60U, // add x0, x19, #0xa
            0x94000009U, // bl #0x780430
            0x52800028U, // mov w8, #1
            0x39002268U, // strb w8, [x19, #8]
            0xA9467BFDU, // ldp x29, x30, [sp, #0x60]
            0xA9454FF4U, // ldp x20, x19, [sp, #0x50]
            0x9101C3FFU, // add sp, sp, #0x70
            0xD65F03C0U, // ret
            0x52808721U, // mov w1, #0x439
            0x17FFFFF7U, // b #0x780408
        ]);

    private static byte[] SWNameRangeOne() => Words(4, [
            0x94006B6AU, // bl #0x77f5d0
        ]);

    private static byte[] SWNameRangeTwo() => Words(4, [
            0x94006B49U, // bl #0x77f5d0
        ]);

    private static byte[] SWNameRangeThree() => Words(4, [
            0x94006B31U, // bl #0x77f5f0
        ]);

    private static byte[] SWNameIndexOne() => Words(4, [
            0x94006B63U, // bl #0x77f5e0
        ]);

    private static byte[] SWNameIndexTwo() => Words(4, [
            0x94006B44U, // bl #0x77f5e0
        ]);

    private static byte[] SWNameIndexThree() => Words(4, [
            0x94006B25U, // bl #0x77f5e0
        ]);

    private static byte[] SHPersonalRecord() => Words(192, [
            0xF81D0FF5U, // str x21, [sp, #-0x30]!
            0xA9014FF4U, // stp x20, x19, [sp, #0x10]
            0xA9027BFDU, // stp x29, x30, [sp, #0x20]
            0x910083FDU, // add x29, sp, #0x20
            0x2A0203F3U, // mov w19, w2
            0x2A0103F5U, // mov w21, w1
            0xAA0003F4U, // mov x20, x0
            0x2A0103E0U, // mov w0, w1
            0x2A0203E1U, // mov w1, w2
            0x94000027U, // bl #0x77f600
            0x9000F4C9U, // adrp x9, #0x2617000
            0xF941F129U, // ldr x9, [x9, #0x3e0]
            0xF9400129U, // ldr x9, [x9]
            0x52801602U, // mov w2, #0xb0
            0x9BA22401U, // umaddl x1, w0, w2, x9
            0x91004280U, // add x0, x20, #0x10
            0x9445FA34U, // bl #0x18fde50
            0xB9000A95U, // str w21, [x20, #8]
            0x79001A93U, // strh w19, [x20, #0xc]
            0xA9427BFDU, // ldp x29, x30, [sp, #0x20]
            0xA9414FF4U, // ldp x20, x19, [sp, #0x10]
            0xF84307F5U, // ldr x21, [sp], #0x30
            0xD65F03C0U, // ret
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0xD503201FU, // nop
            0x710F4C3FU, // cmp w1, #0x3d3
            0x54000040U, // b.eq #0x77f5dc
            0x710E103FU, // cmp w1, #0x384
            0xD65F03C0U, // ret
            0x710F4C1FU, // cmp w0, #0x3d3
            0x540000E1U, // b.ne #0x77f600
            0xD65F03C0U, // ret
            0xD503201FU, // nop
            0x710F4E9FU, // cmp w20, #0x3d3
            0x54000040U, // b.eq #0x77f5fc
            0x710E129FU, // cmp w20, #0x384
            0xD65F03C0U, // ret
        ]);
    // Appended records start beyond every native personal, evolution and egg
    // record. The legacy 979 reservation remains stable. The dispatcher checks
    // the loaded personal byte length before returning an appended row.
    private static byte[] ExtendedPersonalRecord(ProjectGame game)
    {
        var bytes = game == ProjectGame.Sword ? SWPersonalRecord() : SHPersonalRecord();
        ExtendedDispatch().CopyTo(bytes, 0x5c);
        ExtendedNameHelpers().CopyTo(bytes, 0x90);
        return bytes;
    }

    private static byte[] ExtendedPersonalIndex()
    {
        var bytes = SWPersonalIndex();
        ExtendedIndexPrefix().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x18), 0x540002A8); // b.hi invalid
        return bytes;
    }
    private const string SwordLoaderHash = "6475B7FEF5708E54459E2AB96925C5596765E08D3CE2C6B24D230A1B30773365";
    private const string ShieldLoaderHash = "D9EFA94BA946879B68902E328786C22480623403367035AB15F156BC04F9E4CB";
    private static byte[] ExtendedDispatch() => Words(52, [
        0x710F4C1FU, // cmp w0, #0x3d3
        0x52808729U, // mov w9, #0x439
        0x1105800AU, // add w10, w0, #0x160
        0x1A8A0120U, // csel w0, w9, w10, eq
        0x9000F4C8U, // adrp x8, #0x2617000
        0xF941F108U, // ldr x8, [x8, #0x3e0]
        0xF9400908U, // ldr x8, [x8, #0x10]
        0x52801609U, // mov w9, #0xb0
        0x1AC90908U, // udiv w8, w8, w9
        0x6B08001FU, // cmp w0, w8
        0x1A9F3000U, // csel w0, w0, wzr, lo
        0x14000015U, // b #0x77f61c
        0xD503201FU, // nop
    ]);
    private static byte[] ExtendedNameHelpers() => Words(48, [
        0x7110043FU, // cmp w1, #0x401
        0xD65F03C0U, // ret
        0xD503201FU, // nop
        0xD503201FU, // nop
        0x710E0C1FU, // cmp w0, #0x383
        0x540000E3U, // b.lo #0x77f600
        0xD65F03C0U, // ret
        0xD503201FU, // nop
        0x7110069FU, // cmp w20, #0x401
        0xD65F03C0U, // ret
        0xD503201FU, // nop
        0xD503201FU, // nop
    ]);
    private static byte[] ExtendedIndexPrefix() => Words(16, [
        0x510E0C08U, // sub w8, w0, #0x383
        0x7101F91FU, // cmp w8, #0x7e
        0x54FFFCA9U, // b.ls #0x77f59c
        0x14000001U, // b #0x77f610
    ]);
    private static byte[] ExtendedEggMoves() => Words(128, [
        0xD101C3FFU, // sub sp, sp, #0x70
        0xA9054FF4U, // stp x20, x19, [sp, #0x50]
        0xA9067BFDU, // stp x29, x30, [sp, #0x60]
        0x910183FDU, // add x29, sp, #0x60
        0x2A0103E8U, // mov w8, w1
        0xAA0003F3U, // mov x19, x0
        0x710E091FU, // cmp w8, #0x382
        0x540000C9U, // b.ls #0x7803e4
        0x2A0803E0U, // mov w0, w8
        0x2A0203E1U, // mov w1, w2
        0x97FFFC8AU, // bl #0x77f600
        0x2A0003E1U, // mov w1, w0
        0x1400000CU, // b #0x780410
        0x2A0203F4U, // mov w20, w2
        0x72003C5FU, // tst w2, #0xffff
        0x54000100U, // b.eq #0x78040c
        0x910023E0U, // add x0, sp, #8
        0x2A0803E1U, // mov w1, w8
        0x9400000EU, // bl #0x780430
        0x794013E8U, // ldrh w8, [sp, #8]
        0x0B342108U, // add w8, w8, w20, uxth
        0x51000501U, // sub w1, w8, #1
        0x14000002U, // b #0x780410
        0x2A0803E1U, // mov w1, w8
        0x91002A60U, // add x0, x19, #0xa
        0x94000007U, // bl #0x780430
        0x52800028U, // mov w8, #1
        0x39002268U, // strb w8, [x19, #8]
        0xA9467BFDU, // ldp x29, x30, [sp, #0x60]
        0xA9454FF4U, // ldp x20, x19, [sp, #0x50]
        0x9101C3FFU, // add sp, sp, #0x70
        0xD65F03C0U, // ret
    ]);

    private static byte[] Words(int length, uint[] instructions)
    {
        var bytes = new byte[length];
        for (var at = 0; at < length; at += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), at / 4 < instructions.Length ? instructions[at / 4] : 0xD503201F);
        return bytes;
    }
}
