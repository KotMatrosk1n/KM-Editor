// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Security.Cryptography;
using System.Buffers.Binary;

namespace KM.SwSh.HeldItemChance;

internal static partial class SwShHeldItemChancePatcher
{
    public const int RateOffset = 0x0075FB5C;
    public static readonly int[] Defaults = [50, 5, 0, 60, 20, 0];

    public static bool AreValid(IReadOnlyList<int>? rates) => rates is { Count: 6 }
        && rates.All(rate => rate is >= 0 and <= 100)
        && rates.Take(3).Sum() <= 100 && rates.Skip(3).Sum() <= 100;

    public static NsoFile Read(byte[] bytes, ProjectGame? game)
    {
        var nso = NsoFile.Parse(bytes);
        var (build, picker, hash) = game switch
        {
            ProjectGame.Sword => ("A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000", 0x00D317F0,
                "2104389400D458326034755E2C25866CABBC459885987565FA84C89B87930DC1"),
            ProjectGame.Shield => ("A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000", 0x00D31820,
                "F0EEA5416428022F5E5F9AD8FFC604A04186078238CBC51E3BFFF4011BCE330A"),
            _ => throw new InvalidDataException("Select Sword or Shield.")
        };
        if (Convert.ToHexString(nso.BuildId) != build || nso.Text.Header.MemoryOffset != 0
            || nso.Ro.Header.MemoryOffset != 0x01901000 || nso.Ro.DecompressedData.Length < RateOffset + 6
            || nso.Text.DecompressedData.Length < picker + 0x114
            || Convert.ToHexString(SHA256.HashData(OriginalPicker(nso.Text.DecompressedData, game, picker))) != hash)
            throw new InvalidDataException("Held Item Chance requires the selected game's 1.3.2 executable and a compatible held item picker.");
        foreach (var (segment, flag) in new[] { (nso.Text, NsoFlags.CheckHashText), (nso.Ro, NsoFlags.CheckHashRo), (nso.Data, NsoFlags.CheckHashData) })
            if (nso.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        if (!AreValid(Rates(nso))) throw new InvalidDataException("The executable held item percentages are invalid.");
        return nso;
    }

    public static int[] Rates(NsoFile nso) => nso.Ro.DecompressedData.AsSpan(RateOffset, 6).ToArray().Select(value => (int)value).ToArray();

    public static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, IReadOnlyList<int> rates, IReadOnlyList<SwShHeldItemChanceOverride>? overrides = null)
    {
        if (!AreValid(rates)) throw new InvalidDataException("Each set requires three integer percentages totaling at most 100%.");
        var baseline = Read(vanilla, game);
        var current = Read(source, game);
        if (!Rates(baseline).SequenceEqual(Defaults)) throw new InvalidDataException("The base executable must contain the original held item chances.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline, current, "Held Item Chance");
        var text = current.Text.DecompressedData.ToArray();
        WriteOverrides(text, game, overrides ?? Overrides(current, game));
        var ro = current.Ro.DecompressedData.ToArray();
        for (var i = 0; i < 6; i++) ro[RateOffset + i] = checked((byte)rates[i]);
        var output = current.Write(textDecompressedData: text, roDecompressedData: ro);
        var after = Read(output, game);
        if (!after.Ro.DecompressedData.SequenceEqual(ro)
            || !after.Text.DecompressedData.SequenceEqual(text)
            || !after.Data.DecompressedData.SequenceEqual(current.Data.DecompressedData)
            || !after.Data.CompressedData.SequenceEqual(current.Data.CompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.RawHeader, after.RawHeader))
            throw new InvalidDataException("Held Item Chance output did not preserve the other executable data.");
        return output;
    }

    public static IReadOnlyList<SwShHeldItemChanceOverride> Overrides(NsoFile main, ProjectGame? game) => ReadOverrides(main.Text.DecompressedData, game);

    private static byte[] OriginalPicker(byte[] text, ProjectGame? game, int picker)
    {
        _ = ReadOverrides(text, game);
        var original = text.AsSpan(picker, 0x114).ToArray();
        foreach (var span in Layouts.Single(layout => layout.Game == game).Spans.Where(span => span.Before.Length == 4))
            span.Before.CopyTo(original, span.Offset - picker);
        return original;
    }

    private static IReadOnlyList<SwShHeldItemChanceOverride> ReadOverrides(byte[] text, ProjectGame? game)
    {
        var layout = Layouts.Single(value => value.Game == game);
        var entry = layout.Spans.First(span => span.Before.Length == 4);
        CheckBoundary(text, entry.Offset, 4);
        var installed = text.AsSpan(entry.Offset, 4).SequenceEqual(entry.After);
        foreach (var span in layout.Spans)
        {
            CheckBoundary(text, span.Offset, span.Before.Length);
            if (!text.AsSpan(span.Offset, span.Before.Length).SequenceEqual(installed ? span.After : span.Before))
                throw new InvalidDataException("Held item chance hooks are incomplete or incompatible.");
        }
        var rows = new List<SwShHeldItemChanceOverride>();
        var ended = false;
        uint previous = 0;
        for (var index = 0; index < layout.Data.Length; index++)
        {
            var offset = layout.Data[index];
            CheckBoundary(text, offset, 12);
            var data = text.AsSpan(offset, 12);
            var key = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (key == 0)
            {
                ended = true;
                if (data.ContainsAnyExcept((byte)0)) throw new InvalidDataException("Held item chance storage is invalid.");
                continue;
            }
            if (!installed || ended || key <= previous || (key & 65535) is 0 or > 898 || key >> 16 > 255)
                throw new InvalidDataException("Held item chance identities are invalid.");
            previous = key;
            var rates = data.Slice(4, 6).ToArray().Select(value => (int)value).ToArray();
            if (!AreValid(rates)) throw new InvalidDataException("Held item chance percentages are invalid.");
            var next = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
            var hasNext = index + 1 < layout.Data.Length && BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(layout.Data[index + 1])) != 0;
            if (next != (hasNext ? (layout.Data[index + 1] - offset) / 4 : 0))
                throw new InvalidDataException("Held item chance storage links are invalid.");
            rows.Add(new((int)(key & 65535), (int)(key >> 16), rates));
        }
        if (installed && rows.Count == 0) throw new InvalidDataException("Held item chance hooks have no settings.");
        return rows;
    }

    private static void WriteOverrides(byte[] text, ProjectGame? game, IReadOnlyList<SwShHeldItemChanceOverride> rows)
    {
        var layout = Layouts.Single(value => value.Game == game);
        var ordered = rows.OrderBy(row => row.Species | row.Form << 16).ToArray();
        if (ordered.Length > layout.Data.Length || ordered.Any(row => row.Species is < 1 or > 898 || row.Form is < 0 or > 255 || !AreValid(row.Rates))
            || ordered.Select(row => (row.Species, row.Form)).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("Choose supported Pokemon and valid held item percentages.");
        var reservations = SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game!.Value, SwShExeFsReservedRegionLedger.OwnerHeldItemChance);
        foreach (var region in layout.Spans.Select(span => (span.Offset, Length: span.Before.Length)).Concat(layout.Data.Select(offset => (Offset: offset, Length: 12))))
            if (reservations.Any(other => SwShExeFsReservedRegionLedger.Overlaps(other, region.Length == 12 ? region.Offset - 4 : region.Offset, region.Length == 12 ? 16 : region.Length)))
                throw new InvalidDataException("Held item chances overlap another executable feature.");
        foreach (var span in layout.Spans) (ordered.Length > 0 ? span.After : span.Before).CopyTo(text, span.Offset);
        for (var index = 0; index < layout.Data.Length; index++)
        {
            var data = text.AsSpan(layout.Data[index], 12);
            data.Clear();
            if (index >= ordered.Length) continue;
            var row = ordered[index];
            BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)(row.Species | row.Form << 16));
            for (var slot = 0; slot < 6; slot++) data[4 + slot] = (byte)row.Rates[slot];
            if (index + 1 < ordered.Length) BinaryPrimitives.WriteUInt16LittleEndian(data[10..], checked((ushort)((layout.Data[index + 1] - layout.Data[index]) / 4)));
        }
    }

    private static void CheckBoundary(byte[] text, int offset, int length)
    {
        if (offset < 4 || offset > text.Length - length || length == 12 && !text.AsSpan(offset - 4, 4).SequenceEqual(new byte[] { 0xC0, 3, 0x5F, 0xD6 }))
            throw new InvalidDataException("Held item chance helper boundaries are incompatible.");
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> CreateReservations() => Layouts.SelectMany(layout =>
        layout.Spans.Select(span => (span.Offset, Length: span.Before.Length)).Concat(layout.Data.Select(offset => (Offset: offset, Length: 12)))
            .Select(span => new SwShExeFsReservedRegion(SwShExeFsReservedRegionLedger.OwnerHeldItemChance,
                $"held-item-chance-{layout.Game.ToString().ToLowerInvariant()}-{span.Offset:X}", "exefs/main", "main.text",
                span.Length == 12 ? span.Offset - 4 : span.Offset, span.Length == 12 ? 16 : span.Length,
                "Pokemon held item chances", "do-not-overwrite"))).ToArray();

    private sealed record Span(int Offset, byte[] Before, byte[] After);
    private sealed record Layout(ProjectGame Game, Span[] Spans, int[] Data);
}
