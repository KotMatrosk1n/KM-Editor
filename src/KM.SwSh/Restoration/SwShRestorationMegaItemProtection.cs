// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Adds the restored Mega Stone category to the native battle item protection predicate.</summary>
public static class SwShRestorationMegaItemProtection
{
    internal const string Owner = "Restoration Mega Items";
    private const int Offset = 0x840650;
    private const int Length = 0x114;
    private const string OriginalHash = "13E5F408F80CF4A84A37F84E99FB1BCECC09882F8469A9728899BD2D7F8374AF";
    private static readonly byte[] Protected = CreatePattern();

    internal static (NsoFile Main, bool Installed) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes);
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0 || main.Text.DecompressedData.Length < Offset + Length)
            throw new InvalidDataException("Mega Stone protection requires the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        var text = main.Text.DecompressedData;
        var routine = text.AsSpan(Offset, Length).ToArray();
        if (game == ProjectGame.Sword && SwShRestorationBattleHeapPatcher.IsInstalled(main, game))
            SwShRestorationBattleHeapPatcher.NormalizeSwordMegaPadding(routine, Offset);
        var installed = routine.AsSpan().SequenceEqual(Protected);
        if (!installed && Convert.ToHexString(SHA256.HashData(routine)) != OriginalHash)
            throw new InvalidDataException("The battle item protection routine contains incompatible changes.");
        var dependencies = text.AsSpan(0x789050, 0x50).ToArray()
            .Concat(text.AsSpan(0x7890F0, 0x50).ToArray()).Concat(text.AsSpan(0x7DD650, 0x60).ToArray()).ToArray();
        var expected = game == ProjectGame.Sword
            ? "7E1D285ABC1CDDCB7E89A6EFBB1CFDDC7A1FA9F3226678DAA9BF39EDB6DC92AD"
            : "4340F0CA88030818CD05AC62228A0588FF42DC775E07B02078D2F329A99C6420";
        if (Convert.ToHexString(SHA256.HashData(dependencies)) != expected)
            throw new InvalidDataException("The native item classification helpers contain incompatible changes.");
        return (main, installed);
    }

    public static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, true);
    internal static byte[] RestoreFromBase(byte[] baseline, byte[] source, ProjectGame game) => Rewrite(baseline, source, game, false);

    private static byte[] Rewrite(byte[] baseline, byte[] source, ProjectGame game, bool enabled)
    {
        var original = Read(baseline, game); var current = Read(source, game);
        if (original.Installed) throw new InvalidDataException("Base ExeFS must contain the original battle item protection routine.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main, current.Main, Owner);
        if (SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game, Owner)
            .Any(region => SwShExeFsReservedRegionLedger.Overlaps(region, Offset, Length)))
            throw new InvalidDataException("Mega Stone protection overlaps another executable feature.");
        if (current.Installed == enabled) return source;
        if (!enabled && game == ProjectGame.Sword && SwShRestorationBattleHeapPatcher.IsInstalled(current.Main, game))
            throw new InvalidDataException("Remove the battle heap repair before restoring the native battle item routine.");
        var text = current.Main.Text.DecompressedData.ToArray();
        (enabled ? Protected.AsSpan() : original.Main.Text.DecompressedData.AsSpan(Offset, Length)).CopyTo(text.AsSpan(Offset));
        var output = current.Main.Write(textDecompressedData: text);
        var after = Read(output, game);
        if (after.Installed != enabled || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(current.Main.Ro.DecompressedData)
            || !after.Main.Data.DecompressedData.SequenceEqual(current.Main.Data.DecompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Mega Stone protection output failed preservation verification.");
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
    [new(Owner, "restoration-mega-item-protection", "exefs/main", "main.text", Offset, Length,
        "Battle item removal and transfer protection", "payload-only")];

    private static byte[] CreatePattern()
    {
        // Same routine and helper addresses in Sword and Shield 1.3.2. Category 7
        // is protected for every holder; all other items retain the native species
        // exceptions and Z-Crystal check. The compact predicate fits in the original
        // function, preserves the ABI, and needs no allocated code cave or loader.
        uint[] words =
        [
            0xA9BE7BFDU, // stp x29, x30, [sp, #-0x20]!
            0xA90153F3U, // stp x19, x20, [sp, #0x10]
            0x910003FDU, // mov x29, sp
            0x53003C13U, // uxth w19, w0
            0x53003C34U, // uxth w20, w1
            0x34000514U, // cbz w20, #0x840704
            0x2A1403E0U, // mov w0, w20
            0x97FD2279U, // bl #0x789050
            0x37000460U, // tbnz w0, #0, #0x8406fc
            0x5105FA68U, // sub w8, w19, #0x17e
            0x7100091FU, // cmp w8, #2
            0x54000443U, // b.lo #0x840704
            0x71079E7FU, // cmp w19, #0x1e7
            0x540002E0U, // b.eq #0x8406e0
            0x7107B67FU, // cmp w19, #0x1ed
            0x54000180U, // b.eq #0x8406bc
            0x710A267FU, // cmp w19, #0x289
            0x540001A0U, // b.eq #0x8406c8
            0x710C167FU, // cmp w19, #0x305
            0x540001C0U, // b.eq #0x8406d4
            0x710DE27FU, // cmp w19, #0x378
            0x54000220U, // b.eq #0x8406e8
            0x710DE67FU, // cmp w19, #0x379
            0x54000220U, // b.eq #0x8406f0
            0x2A1403E0U, // mov w0, w20
            0x97FD228FU, // bl #0x7890f0
            0x14000014U, // b #0x840708
            0x2A1403E0U, // mov w0, w20
            0x97FE73E4U, // bl #0x7dd650
            0x14000011U, // b #0x840708
            0x2A1403E0U, // mov w0, w20
            0x97FE73F1U, // bl #0x7dd690
            0x1400000EU, // b #0x840708
            0x2A1403E0U, // mov w0, w20
            0x97FE73E6U, // bl #0x7dd670
            0x1400000BU, // b #0x840708
            0x7101C29FU, // cmp w20, #0x70
            0x14000004U, // b #0x8406f4
            0x71113E9FU, // cmp w20, #0x44f
            0x14000002U, // b #0x8406f4
            0x7111429FU, // cmp w20, #0x450
            0x1A9F17E0U, // cset w0, eq
            0x14000004U, // b #0x840708
            0x52800020U, // mov w0, #1
            0x14000002U, // b #0x840708
            0x52800000U, // mov w0, #0
            0xA94153F3U, // ldp x19, x20, [sp, #0x10]
            0xA8C27BFDU, // ldp x29, x30, [sp], #0x20
            0xD65F03C0U, // ret
        ];
        var bytes = new byte[Length];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        return bytes;
    }
}
