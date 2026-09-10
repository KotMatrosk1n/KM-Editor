// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.TrainerDynamax;

internal sealed record SwShTrainerDynamaxMainState(int DisabledSides, bool Partial, string BuildId);

internal static partial class SwShTrainerDynamaxMainPatcher
{
    internal const string Owner = SwShExeFsReservedRegionLedger.OwnerTrainerDynamax;
    private static readonly byte[] ReturnInstruction = [0xC0, 0x03, 0x5F, 0xD6];

    public static SwShTrainerDynamaxMainState Inspect(byte[] bytes, ProjectGame? game)
    {
        var main = NsoFile.Parse(bytes);
        var layout = FindLayout(main, game);
        var text = main.Text.DecompressedData;
        var modeWord = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(layout.ModeOffset, 4));
        var mode = modeWord == 0 ? 0 : Enumerable.Range(1, 3)
            .FirstOrDefault(value => ModeWord(value) == modeWord, -1);
        if (mode < 0) throw new InvalidDataException("Trainer Dynamax found incompatible permission settings.");
        var installed = 0;
        foreach (var span in layout.Spans)
        {
            var current = text.AsSpan(span.Offset, span.Original.Length);
            if (span.Original.Length == 12 && !text.AsSpan(span.Offset - 4, 4).SequenceEqual(ReturnInstruction))
                throw new InvalidDataException("Trainer Dynamax found incompatible helper boundaries.");
            if (current.SequenceEqual(span.Original)) continue;
            if (!current.SequenceEqual(PatchedSpan(layout, span, mode == 0 ? 3 : mode)))
                throw new InvalidDataException("Trainer Dynamax found another edit in its owned executable regions.");
            installed++;
        }
        return new(mode, installed != 0 && installed != layout.Spans.Length, Convert.ToHexString(main.BuildId));
    }

    public static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, int disabledSides)
    {
        if (disabledSides is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(disabledSides));
        var baseline = NsoFile.Parse(vanilla);
        var current = NsoFile.Parse(source);
        var layout = FindLayout(baseline, game);
        VerifyLedgerOwnership(layout);
        _ = FindLayout(current, game);
        var vanillaState = Inspect(vanilla, game);
        if (vanillaState.DisabledSides != 0 || vanillaState.Partial)
            throw new InvalidDataException("Trainer Dynamax requires vanilla Base ExeFS.");
        _ = Inspect(source, game);
        var text = current.Text.DecompressedData.ToArray();
        foreach (var span in layout.Spans)
            (disabledSides == 0 ? span.Original : PatchedSpan(layout, span, disabledSides)).CopyTo(text, span.Offset);
        if (text.SequenceEqual(current.Text.DecompressedData)) return source.ToArray();
        var result = current.Write(textDecompressedData: text);
        var parsed = NsoFile.Parse(result);
        if (!parsed.Text.DecompressedData.SequenceEqual(text)
            || !parsed.Ro.DecompressedData.SequenceEqual(current.Ro.DecompressedData)
            || !parsed.Data.DecompressedData.SequenceEqual(current.Data.DecompressedData)
            || !parsed.BuildId.SequenceEqual(current.BuildId))
            throw new InvalidDataException("Trainer Dynamax could not verify executable preservation.");
        var state = Inspect(result, game);
        if (state.DisabledSides != disabledSides || state.Partial)
            throw new InvalidDataException("Trainer Dynamax could not verify its output settings.");
        return result;
    }

    public static bool Equivalent(byte[] first, byte[] second)
    {
        return SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(first, second);
    }

    public static bool HasInstalledHook(byte[] bytes)
    {
        try
        {
            var state = Inspect(bytes, null);
            return state.DisabledSides != 0 || state.Partial;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> CreateReservations() => Layouts.SelectMany(layout => layout.Spans.Select(span =>
        new SwShExeFsReservedRegion(Owner, $"trainer-dynamax-{layout.Game.ToString().ToLowerInvariant()}-{span.Offset:X}",
            "exefs/main", "main.text", span.Original.Length == 12 ? span.Offset - 4 : span.Offset,
            span.Original.Length == 12 ? 16 : span.Original.Length, "Trainer Dynamax permission helper", "do-not-overwrite"))).ToArray();

    private static uint ModeWord(int mode) => 0x52800011u | ((uint)mode << 5);

    private static void VerifyLedgerOwnership(Layout layout)
    {
        var reservations = SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(layout.Game, Owner);
        foreach (var span in layout.Spans)
        {
            // The preceding return is a protected dependency of each helper,
            // even though installation and removal never write that instruction.
            var start = span.Original.Length == 12 ? span.Offset - 4 : span.Offset;
            var length = span.Original.Length == 12 ? 16 : span.Original.Length;
            if (reservations.Any(region => SwShExeFsReservedRegionLedger.Overlaps(region, start, length)))
                throw new InvalidDataException("Trainer Dynamax overlaps another reserved executable feature.");
        }
    }

    private static byte[] PatchedSpan(Layout layout, Span span, int mode)
    {
        var result = span.Patched.ToArray();
        if (layout.ModeOffset >= span.Offset && layout.ModeOffset < span.Offset + result.Length)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.ModeOffset - span.Offset, 4), ModeWord(mode));
        return result;
    }

    private static Layout FindLayout(NsoFile main, ProjectGame? game)
    {
        var id = Convert.ToHexString(main.BuildId);
        var layout = Layouts.SingleOrDefault(candidate => candidate.BuildId == id)
            ?? throw new InvalidDataException("Trainer Dynamax supports Sword and Shield 1.3.2 executable builds.");
        if (game is not null && layout.Game != game)
            throw new InvalidDataException("Trainer Dynamax executable does not match the selected game.");
        if (layout.Spans.Any(span => span.Offset > main.Text.DecompressedData.Length - span.Original.Length))
            throw new InvalidDataException("Trainer Dynamax found an incomplete executable.");
        return layout;
    }

    private sealed record Span(int Offset, byte[] Original, byte[] Patched);
    private sealed record Layout(ProjectGame Game, string BuildId, int ModeOffset, Span[] Spans);
}
