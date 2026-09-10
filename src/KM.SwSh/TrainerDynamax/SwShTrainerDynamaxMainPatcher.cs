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
        var layouts = new[] { legacy, TrainerLayouts.Single(candidate => candidate.Game == legacy.Game),
            EnabledLayouts.Single(candidate => candidate.Game == legacy.Game) };
        var text = main.Text.DecompressedData;
        var modes = layouts.Select(layout => ReadMode(text, layout)).ToArray();
        var counts = new int[layouts.Length];
        foreach (var span in layouts.SelectMany(layout => layout.Spans).DistinctBy(span => span.Offset))
        {
            CheckBoundary(text, span);
            var value = text.AsSpan(span.Offset, span.Original.Length);
            if (value.SequenceEqual(span.Original)) continue;
            var matched = false;
            for (var generation = layouts.Length - 1; generation >= 0; generation--)
            {
                var candidate = layouts[generation].Spans.FirstOrDefault(item => item.Offset == span.Offset);
                if (candidate is null || !value.SequenceEqual(PatchedSpan(layouts[generation], candidate, modes[generation]))) continue;
                counts[generation]++;
                matched = true;
                break;
            }
            if (!matched) throw new InvalidDataException("Trainer Dynamax found another edit in its owned executable regions.");
        }
        var tables = new byte[layouts.Length][];
        for (var generation = 0; generation < layouts.Length; generation++)
        {
            var layout = layouts[generation];
            var table = tables[generation] = new byte[440];
            for (var index = 0; index < layout.DataOffsets.Length; index++)
            {
                var offset = layout.DataOffsets[index];
                CheckBoundary(text, new(offset, new byte[12], []));
                var data = text.AsSpan(offset, 12);
                if (data.SequenceEqual(new byte[12])) continue;
                if (BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != Marker(layout))
                    throw new InvalidDataException("Trainer Dynamax found incompatible trainer settings.");
                data[..8].CopyTo(table.AsSpan(index * 8));
                counts[generation]++;
            }
            if (table[0] != 0 || table.AsSpan(437).ContainsAnyExcept((byte)0)
                || table.Any(value => (value & 0xF0) != 0 || !IsEnabledLayout(layout) && ((value & 3) == 3 || ((value >> 2) & 3) == 3)))
                throw new InvalidDataException("Trainer Dynamax found incompatible trainer settings.");
        }
        var active = Array.FindLastIndex(counts, count => count != 0);
        if (active < 0) return new(0, false, Convert.ToHexString(main.BuildId), [], false);
        var partial = counts[active] != layouts[active].Spans.Length + layouts[active].DataOffsets.Length
            || counts.Where((_, index) => index != active).Any(count => count != 0);
        List<SwShTrainerDynamaxOverride> trainers = [];
        for (var id = 1; id <= 436; id++)
            if (tables[active][id] is var value && value != 0) trainers.Add(new(id, value & 3, (value >> 2) & 3));
        return new(modes[active], partial, Convert.ToHexString(main.BuildId), trainers, true);
    }

    public static byte[] Apply(byte[] vanilla, byte[] source, ProjectGame? game, int disabledSides) =>
        ApplySettings(vanilla, source, game, new((disabledSides & 1) != 0, (disabledSides & 2) != 0), disabledSides);

    public static byte[] ApplySettings(byte[] vanilla, byte[] source, ProjectGame? game, SwShTrainerDynamaxSettings settings) =>
        ApplySettings(vanilla, source, game, settings, settings.Mask);

    private static byte[] ApplySettings(byte[] vanilla, byte[] source, ProjectGame? game, SwShTrainerDynamaxSettings settings, int mode)
    {
        if (mode is < 0 or > 15 || (mode & 5) == 5 || (mode & 10) == 10) throw new InvalidDataException("Trainer Dynamax found invalid global settings.");
        settings = settings.Canonical();
        var baseline = NsoFile.Parse(vanilla);
        var current = NsoFile.Parse(source);
        var legacy = FindLayout(baseline, game);
        var expanded = TrainerLayouts.Single(candidate => candidate.Game == legacy.Game);
        var enabled = EnabledLayouts.Single(candidate => candidate.Game == legacy.Game);
        VerifyLedgerOwnership(legacy, expanded, enabled);
        _ = FindLayout(current, game);
        if (Inspect(vanilla, game).Installed) throw new InvalidDataException("Trainer Dynamax requires vanilla Base ExeFS.");
        _ = Inspect(source, game);
        var text = current.Text.DecompressedData.ToArray();
        // Restore every recognized generation before composing the requested state.
        foreach (var span in legacy.Spans.Concat(expanded.Spans).Concat(enabled.Spans)) span.Original.CopyTo(text, span.Offset);
        foreach (var offset in expanded.DataOffsets.Concat(enabled.DataOffsets)) text.AsSpan(offset, 12).Clear();
        var hasRows = settings.Trainers!.Count != 0;
        var layout = (mode & 12) != 0 || settings.Trainers!.Any(row => row.Player == 3 || row.Opponent == 3)
            ? enabled : hasRows ? expanded : legacy;
        if (mode != 0 || hasRows)
            foreach (var span in layout.Spans) PatchedSpan(layout, span, mode).CopyTo(text, span.Offset);
        if ((mode != 0 || hasRows) && layout.DataOffsets.Length != 0)
        {
            var table = new byte[440];
            foreach (var row in settings.Trainers!) table[row.TrainerId] = (byte)(row.Player | row.Opponent << 2);
            for (var index = 0; index < layout.DataOffsets.Length; index++)
            {
                var data = text.AsSpan(layout.DataOffsets[index], 12);
                table.AsSpan(index * 8, 8).CopyTo(data);
                BinaryPrimitives.WriteUInt32LittleEndian(data[8..], Marker(layout));
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

    public static IReadOnlyList<SwShExeFsReservedRegion> CreateReservations() => Layouts.Concat(TrainerLayouts).Concat(EnabledLayouts)
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
    private static bool IsEnabledLayout(Layout layout) => EnabledLayouts.Contains(layout);
    private static uint Marker(Layout layout) => IsEnabledLayout(layout) ? 0x33444D4Bu : TableMarker;
    private static uint ModeWord(Layout layout, int mode) => (layout.DataOffsets.Length == 0 ? 0x52800011u : 0x52800003u) | ((uint)mode << 5);
    private static int ReadMode(byte[] text, Layout layout)
    {
        var word = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(layout.ModeOffset, 4));
        if (word == 0) return 0;
        for (var mode = 0; mode <= (IsEnabledLayout(layout) ? 15 : 3); mode++) if ((mode & 5) != 5 && (mode & 10) != 10 && word == ModeWord(layout, mode)) return mode;
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
        if (Layouts.Concat(TrainerLayouts).Concat(EnabledLayouts).Where(candidate => candidate.Game == layout.Game).SelectMany(OwnedSpans)
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
