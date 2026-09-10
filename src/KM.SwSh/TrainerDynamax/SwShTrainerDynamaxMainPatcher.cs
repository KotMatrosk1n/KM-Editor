// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.TrainerDynamax;

internal sealed record SwShTrainerDynamaxMainState(int DisabledSides, bool Partial, string BuildId,
    IReadOnlyList<SwShTrainerDynamaxOverride> Trainers, bool Installed);

internal static partial class SwShTrainerDynamaxMainPatcher
{
    internal const string Owner = SwShExeFsReservedRegionLedger.OwnerTrainerDynamax;
    private const uint TableMarker = 0x32444D4B;
    private static readonly byte[] ReturnInstruction = [0xC0, 0x03, 0x5F, 0xD6];

    public static SwShTrainerDynamaxMainState Inspect(byte[] bytes, ProjectGame? game)
    {
        var main = NsoFile.Parse(bytes);
        var legacy = FindLayout(main, game);
        var current = TrainerLayouts.Single(candidate => candidate.Game == legacy.Game);
        var text = main.Text.DecompressedData;
        var legacyMode = ReadMode(text, legacy);
        var currentMode = ReadMode(text, current);
        var oldCount = 0;
        var newCount = 0;
        foreach (var span in legacy.Spans.Concat(current.Spans).DistinctBy(span => span.Offset))
        {
            CheckBoundary(text, span);
            var value = text.AsSpan(span.Offset, span.Original.Length);
            if (value.SequenceEqual(span.Original)) continue;
            var newer = current.Spans.FirstOrDefault(candidate => candidate.Offset == span.Offset);
            if (newer is not null && value.SequenceEqual(PatchedSpan(current, newer, currentMode))) { newCount++; continue; }
            var older = legacy.Spans.FirstOrDefault(candidate => candidate.Offset == span.Offset);
            if (older is not null && value.SequenceEqual(PatchedSpan(legacy, older, legacyMode == 0 ? 3 : legacyMode))) { oldCount++; continue; }
            throw new InvalidDataException("Trainer Dynamax found another edit in its owned executable regions.");
        }
        var table = new byte[440];
        for (var index = 0; index < current.DataOffsets.Length; index++)
        {
            var offset = current.DataOffsets[index];
            CheckBoundary(text, new(offset, new byte[12], []));
            var data = text.AsSpan(offset, 12);
            if (data.SequenceEqual(new byte[12])) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != TableMarker)
                throw new InvalidDataException("Trainer Dynamax found incompatible trainer settings.");
            data[..8].CopyTo(table.AsSpan(index * 8));
            newCount++;
        }
        if (table[0] != 0 || table.AsSpan(437).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Trainer Dynamax found settings outside the supported trainer roster.");
        List<SwShTrainerDynamaxOverride> trainers = [];
        for (var id = 1; id <= 436; id++)
        {
            var value = table[id];
            if ((value & 0xF0) != 0 || (value & 3) == 3 || ((value >> 2) & 3) == 3)
                throw new InvalidDataException("Trainer Dynamax found incompatible trainer settings.");
            if (value != 0) trainers.Add(new(id, value & 3, (value >> 2) & 3));
        }
        var installed = oldCount != 0 || newCount != 0;
        var partial = newCount != 0
            ? newCount != current.Spans.Length + current.DataOffsets.Length || oldCount != 0
            : oldCount != 0 && oldCount != legacy.Spans.Length;
        return new(newCount != 0 ? currentMode : legacyMode, partial, Convert.ToHexString(main.BuildId), trainers, installed);
    }

    public static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, int disabledSides) =>
        ApplySettings(vanilla, source, game, new((disabledSides & 1) != 0, (disabledSides & 2) != 0), disabledSides);

    public static byte[] ApplySettings(byte[] vanilla, byte[] source, ProjectGame? game, SwShTrainerDynamaxSettings settings) =>
        ApplySettings(vanilla, source, game, settings, settings.Mask);

    private static byte[] ApplySettings(byte[] vanilla, byte[] source, ProjectGame? game, SwShTrainerDynamaxSettings settings, int mode)
    {
        if (mode is < 0 or > 3) throw new InvalidDataException("Trainer Dynamax found invalid global settings.");
        settings = settings.Canonical();
        var baseline = NsoFile.Parse(vanilla);
        var current = NsoFile.Parse(source);
        var legacy = FindLayout(baseline, game);
        var expanded = TrainerLayouts.Single(candidate => candidate.Game == legacy.Game);
        VerifyLedgerOwnership(legacy, expanded);
        _ = FindLayout(current, game);
        if (Inspect(vanilla, game).Installed) throw new InvalidDataException("Trainer Dynamax requires vanilla Base ExeFS.");
        _ = Inspect(source, game);
        var text = current.Text.DecompressedData.ToArray();
        // Remove both recognized generations before composing the requested state.
        foreach (var span in legacy.Spans.Concat(expanded.Spans)) span.Original.CopyTo(text, span.Offset);
        foreach (var offset in expanded.DataOffsets) text.AsSpan(offset, 12).Clear();
        var hasRows = settings.Trainers!.Count != 0;
        var layout = hasRows ? expanded : legacy;
        if (mode != 0 || hasRows)
            foreach (var span in layout.Spans) PatchedSpan(layout, span, mode).CopyTo(text, span.Offset);
        if (hasRows)
        {
            var table = new byte[440];
            foreach (var row in settings.Trainers!) table[row.TrainerId] = (byte)(row.Player | row.Opponent << 2);
            for (var index = 0; index < expanded.DataOffsets.Length; index++)
            {
                var data = text.AsSpan(expanded.DataOffsets[index], 12);
                table.AsSpan(index * 8, 8).CopyTo(data);
                BinaryPrimitives.WriteUInt32LittleEndian(data[8..], TableMarker);
            }
        }
        if (text.SequenceEqual(current.Text.DecompressedData)) return source.ToArray();
        var result = current.Write(textDecompressedData: text);
        var parsed = NsoFile.Parse(result);
        if (!parsed.Text.DecompressedData.SequenceEqual(text)
            || !parsed.Ro.DecompressedData.SequenceEqual(current.Ro.DecompressedData)
            || !parsed.Data.DecompressedData.SequenceEqual(current.Data.DecompressedData)
            || !parsed.BuildId.SequenceEqual(current.BuildId))
            throw new InvalidDataException("Trainer Dynamax could not verify executable preservation.");
        var state = Inspect(result, game);
        if (state.DisabledSides != mode || state.Partial || !state.Trainers.SequenceEqual(settings.Trainers!))
            throw new InvalidDataException("Trainer Dynamax could not verify its output settings.");
        return result;
    }

    public static bool Equivalent(byte[] first, byte[] second) => SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(first, second);
    public static bool HasInstalledHook(byte[] bytes)
    {
        try { return Inspect(bytes, null).Installed; }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { return false; }
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> CreateReservations() => Layouts.Concat(TrainerLayouts)
        .SelectMany(layout => OwnedSpans(layout).Select(span => new SwShExeFsReservedRegion(Owner,
            $"trainer-dynamax-{layout.Game.ToString().ToLowerInvariant()}-{span.Offset:X}", "exefs/main", "main.text",
            span.Original.Length == 12 ? span.Offset - 4 : span.Offset, span.Original.Length == 12 ? 16 : span.Original.Length,
            "Trainer Dynamax permission settings and helpers", "do-not-overwrite")))
        .DistinctBy(region => region.FeatureId).ToArray();

    private static IEnumerable<Span> OwnedSpans(Layout layout) => layout.Spans.Concat(layout.DataOffsets.Select(offset => new Span(offset, new byte[12], [])));
    private static void VerifyLedgerOwnership(params Layout[] layouts)
    {
        var reservations = SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(layouts[0].Game, Owner);
        foreach (var span in layouts.SelectMany(OwnedSpans))
            if (reservations.Any(region => SwShExeFsReservedRegionLedger.Overlaps(region,
                span.Original.Length == 12 ? span.Offset - 4 : span.Offset, span.Original.Length == 12 ? 16 : span.Original.Length)))
                throw new InvalidDataException("Trainer Dynamax overlaps another reserved executable feature.");
    }

    private static void CheckBoundary(byte[] text, Span span)
    {
        if (span.Offset < 4 || span.Offset > text.Length - span.Original.Length
            || span.Original.Length == 12 && !text.AsSpan(span.Offset - 4, 4).SequenceEqual(ReturnInstruction))
            throw new InvalidDataException("Trainer Dynamax found incompatible helper boundaries.");
    }
    private static uint ModeWord(Layout layout, int mode) => (layout.DataOffsets.Length == 0 ? 0x52800011u : 0x52800003u) | ((uint)mode << 5);
    private static int ReadMode(byte[] text, Layout layout)
    {
        var word = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(layout.ModeOffset, 4));
        if (word == 0) return 0;
        for (var mode = 0; mode <= 3; mode++) if (word == ModeWord(layout, mode)) return mode;
        throw new InvalidDataException("Trainer Dynamax found incompatible permission settings.");
    }
    private static byte[] PatchedSpan(Layout layout, Span span, int mode)
    {
        var result = span.Patched.ToArray();
        if (layout.ModeOffset >= span.Offset && layout.ModeOffset < span.Offset + result.Length)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.ModeOffset - span.Offset, 4), ModeWord(layout, mode));
        return result;
    }
    private static Layout FindLayout(NsoFile main, ProjectGame? game)
    {
        var id = Convert.ToHexString(main.BuildId);
        var layout = Layouts.SingleOrDefault(candidate => candidate.BuildId == id)
            ?? throw new InvalidDataException("Trainer Dynamax supports Sword and Shield 1.3.2 executable builds.");
        if (game is not null && layout.Game != game) throw new InvalidDataException("Trainer Dynamax executable does not match the selected game.");
        if (Layouts.Concat(TrainerLayouts).Where(candidate => candidate.Game == layout.Game).SelectMany(OwnedSpans)
            .Any(span => span.Offset > main.Text.DecompressedData.Length - span.Original.Length))
            throw new InvalidDataException("Trainer Dynamax found an incomplete executable.");
        return layout;
    }
    private sealed record Span(int Offset, byte[] Original, byte[] Patched);
    private sealed record Layout(ProjectGame Game, string BuildId, int ModeOffset, Span[] Spans, int[]? Data = null)
    {
        public int[] DataOffsets => Data ?? [];
    }
}
