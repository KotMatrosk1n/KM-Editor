// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.GameOptions;

internal static partial class SwShGameOptionsPatcher
{
    internal const int DescriptorsOffset = 0x20736A8 - 0x1901000;
    private const int DescriptorLength = 16 * 56;
    private const ulong EmptyLabel = 0xCBF29CE484222645;

    internal static (NsoFile Main, int[] Selections) Read(byte[] bytes, ProjectGame? game)
    {
        var main = NsoFile.Parse(bytes);
        var expected = game == ProjectGame.Sword ? "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000"
            : game == ProjectGame.Shield ? "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000" : "";
        if (expected.Length == 0 || Convert.ToHexString(main.BuildId) != expected || main.Text.Header.MemoryOffset != 0
            || main.Ro.Header.MemoryOffset != 0x1901000 || main.Ro.DecompressedData.Length < DescriptorsOffset + DescriptorLength)
            throw new InvalidDataException("Game Options requires the selected game's 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text, NsoFlags.CheckHashText), (main.Ro, NsoFlags.CheckHashRo), (main.Data, NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        var layout = Layouts.Single(value => value.Game == game);
        var installed = BinaryPrimitives.ReadUInt32LittleEndian(main.Ro.DecompressedData.AsSpan(DescriptorsOffset + 52)) != 0;
        foreach (var span in layout.Spans)
        {
            if (span.Offset > main.Text.DecompressedData.Length - span.Before.Length
                || !main.Text.DecompressedData.AsSpan(span.Offset, span.Before.Length).SequenceEqual(installed ? span.After : span.Before)
                || span.Before.Length == 12 && !main.Text.DecompressedData.AsSpan(span.Offset - 4, 4).SequenceEqual(new byte[] { 0xC0, 3, 0x5F, 0xD6 }))
                throw new InvalidDataException("Game Options found conflicting or incomplete executable hooks.");
        }
        foreach (var guard in Guards.Where(value => value.Game == game))
        {
            if (guard.Offset > main.Text.DecompressedData.Length - guard.Length)
                throw new InvalidDataException("Game Options code is truncated.");
            var code = main.Text.DecompressedData.AsSpan(guard.Offset, guard.Length).ToArray();
            foreach (var span in layout.Spans.Where(span => span.Offset >= guard.Offset && span.Offset + span.Before.Length <= guard.Offset + guard.Length))
                span.Before.CopyTo(code, span.Offset - guard.Offset);
            if (Convert.ToHexString(SHA256.HashData(code)) != guard.Hash)
                throw new InvalidDataException("Game Options initialization or menu code is incompatible.");
        }
        var selections = SwShGameOptionsPolicy.Defaults;
        var descriptorCopy = main.Ro.DecompressedData.AsSpan(DescriptorsOffset, DescriptorLength).ToArray();
        var offset = 0;
        for (var row = 0; row < 16; row++)
        {
            var descriptor = descriptorCopy.AsSpan(row * 56, 56);
            if (installed)
            {
                var states = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[48..]);
                var metadata = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[52..]);
                for (var choice = 0; choice < SwShGameOptionsPolicy.ChoiceCounts[row]; choice++)
                    selections[offset + choice] = (int)(states >> (choice * 2) & 3) | (int)(metadata >> choice & 1) * 4;
                if (states >> (SwShGameOptionsPolicy.ChoiceCounts[row] * 2) != 0
                    || metadata != SwShGameOptionsPolicy.Metadata(row, selections.AsSpan(offset, SwShGameOptionsPolicy.ChoiceCounts[row])))
                    throw new InvalidDataException("Game Options policy metadata is inconsistent.");
                descriptor[48..].Clear();
            }
            // Validate the complete descriptor using original label hashes. Hidden labels
            // use the stock empty key; no game message files are rewritten.
            for (var choice = 0; choice < Math.Min(3, SwShGameOptionsPolicy.ChoiceCounts[row]); choice++)
                if (row < 13 && (selections[offset + choice] & 4) != 0)
                {
                    if (BinaryPrimitives.ReadUInt64LittleEndian(descriptor[(16 + choice * 8)..]) != EmptyLabel)
                        throw new InvalidDataException("A hidden Game Options label has changed.");
                    BinaryPrimitives.WriteUInt64LittleEndian(descriptor[(16 + choice * 8)..], ChoiceLabels[row][choice]);
                }
            offset += SwShGameOptionsPolicy.ChoiceCounts[row];
        }
        if (installed && selections.All(value => value == 0)
            || !SwShGameOptionsPolicy.Normalize(selections).SequenceEqual(selections)
            || Convert.ToHexString(SHA256.HashData(descriptorCopy)) != DescriptorHash)
            throw new InvalidDataException("The stock options descriptors or policy are incompatible.");
        return (main, selections);
    }

    internal static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, IReadOnlyList<int> selections)
    {
        var normalized = SwShGameOptionsPolicy.Normalize(selections);
        var baseline = Read(vanilla, game);
        var current = Read(source, game);
        if (baseline.Selections.Any(value => value != 0)) throw new InvalidDataException("Game Options requires original Base settings.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline.Main, current.Main, "Game Options");
        var installed = normalized.Any(value => value != 0);
        var text = current.Main.Text.DecompressedData.ToArray();
        var ro = current.Main.Ro.DecompressedData.ToArray();
        foreach (var span in Layouts.Single(value => value.Game == game).Spans)
            (installed ? span.After : span.Before).CopyTo(text, span.Offset);
        baseline.Main.Ro.DecompressedData.AsSpan(DescriptorsOffset, DescriptorLength).CopyTo(ro.AsSpan(DescriptorsOffset));
        var offset = 0;
        for (var row = 0; installed && row < 16; row++)
        {
            var choices = normalized.AsSpan(offset, SwShGameOptionsPolicy.ChoiceCounts[row]);
            var descriptor = ro.AsSpan(DescriptorsOffset + row * 56, 56);
            uint states = 0;
            for (var choice = 0; choice < choices.Length; choice++)
            {
                states |= (uint)(choices[choice] & 3) << (choice * 2);
                if (row < 13 && (choices[choice] & 4) != 0)
                    BinaryPrimitives.WriteUInt64LittleEndian(descriptor[(16 + choice * 8)..], EmptyLabel);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[48..], states);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[52..], SwShGameOptionsPolicy.Metadata(row, choices));
            offset += choices.Length;
        }
        var output = current.Main.Write(textDecompressedData: text, roDecompressedData: ro);
        var after = Read(output, game);
        if (!after.Selections.SequenceEqual(normalized) || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(ro) || !after.Main.Data.DecompressedData.SequenceEqual(current.Main.Data.DecompressedData)
            || !after.Main.Data.CompressedData.SequenceEqual(current.Main.Data.CompressedData)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(current.Main.RawHeader, after.Main.RawHeader))
            throw new InvalidDataException("Game Options output failed preservation verification.");
        return output;
    }

    internal static IReadOnlyList<SwShExeFsReservedRegion> CreateReservations() => Layouts.SelectMany(layout => layout.Spans.Select(span =>
        new SwShExeFsReservedRegion("Game Options", $"game-options-{layout.Game.ToString().ToLowerInvariant()}-{span.Offset:X}",
            "exefs/main", "main.text", span.Before.Length == 12 ? span.Offset - 4 : span.Offset,
            span.Before.Length == 12 ? 16 : span.Before.Length, "Game Options initialization and choice navigation", "do-not-overwrite")))
        .Append(new("Game Options", "game-options-descriptors", "exefs/main", "main.ro", DescriptorsOffset, DescriptorLength,
            "Game Options selection policy and labels", "payload-only")).ToArray();

    private sealed record Span(int Offset, byte[] Before, byte[] After);
    private sealed record Layout(ProjectGame Game, Span[] Spans);
    private sealed record Guard(ProjectGame Game, int Offset, int Length, string Hash);
}
