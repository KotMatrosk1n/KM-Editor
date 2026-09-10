// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.TrainerWhiteout;

internal sealed record SwShTrainerWhiteoutMainState(ProjectGame Game, int InstalledSites, int TotalSites)
{
    public bool Installed => InstalledSites == TotalSites;
    public bool HasAny => InstalledSites != 0;
}

internal static partial class SwShTrainerWhiteoutMainPatcher
{
    public static SwShTrainerWhiteoutMainState Inspect(byte[] bytes, ProjectGame? expectedGame = null)
    {
        var main = NsoFile.Parse(bytes);
        var layout = FindLayout(main, expectedGame);
        var installed = 0;
        foreach (var span in layout.Spans)
        {
            RequireRange(main.Text.DecompressedData, span.Offset, span.Original.Length);
            var current = main.Text.DecompressedData.AsSpan(span.Offset, span.Original.Length);
            if (current.SequenceEqual(span.Patched)) installed++;
            else if (!current.SequenceEqual(span.Original))
                throw new InvalidDataException($"Trainer Whiteout found incompatible executable code at text+0x{span.Offset:X}.");
            if (span.Original.Length == 12 && !main.Text.DecompressedData.AsSpan(span.Offset - 4, 4).SequenceEqual(ReturnInstruction))
                throw new InvalidDataException("Trainer Whiteout helper padding no longer follows its original return instruction.");
        }
        return new(layout.Game, installed, layout.Spans.Length);
    }

    public static byte[] Apply(byte[] vanilla, byte[] source, bool installed, ProjectGame expectedGame)
    {
        var baseline = NsoFile.Parse(vanilla);
        var input = NsoFile.Parse(source);
        var layout = FindLayout(baseline, expectedGame);
        _ = FindLayout(input, expectedGame);
        var baseState = Inspect(vanilla, expectedGame);
        var current = Inspect(source, expectedGame);
        if (baseState.HasAny) throw new InvalidDataException("Trainer Whiteout requires vanilla facility callbacks in Base ExeFS.");
        if (!baseline.BuildId.SequenceEqual(input.BuildId) || baseline.Text.DecompressedData.Length != input.Text.DecompressedData.Length)
            throw new InvalidDataException("Trainer Whiteout requires matching base and output executable layouts.");
        if (installed ? current.Installed : !current.HasAny) return source.ToArray();
        var text = input.Text.DecompressedData.ToArray();
        foreach (var span in layout.Spans)
            (installed ? span.Patched : span.Original).CopyTo(text, span.Offset);
        var output = input.Write(textDecompressedData: text);
        var actual = NsoFile.Parse(output);
        if (!actual.Text.DecompressedData.SequenceEqual(text)
            || !actual.Ro.DecompressedData.SequenceEqual(input.Ro.DecompressedData)
            || !actual.Data.DecompressedData.SequenceEqual(input.Data.DecompressedData)
            || !actual.BuildId.SequenceEqual(input.BuildId))
            throw new InvalidDataException("Trainer Whiteout executable output did not preserve its expected contents.");
        var unowned = actual.Text.DecompressedData.ToArray();
        foreach (var span in layout.Spans)
            input.Text.DecompressedData.AsSpan(span.Offset, span.Original.Length).CopyTo(unowned.AsSpan(span.Offset));
        if (!unowned.SequenceEqual(input.Text.DecompressedData))
            throw new InvalidDataException("Trainer Whiteout modified executable code outside its owned helpers.");
        var state = Inspect(output, expectedGame);
        if (installed ? !state.Installed : state.HasAny)
            throw new InvalidDataException("Trainer Whiteout executable output failed verification.");
        return output;
    }

    public static bool HasInstalledHook(byte[] bytes)
    {
        try
        {
            var main = NsoFile.Parse(bytes);
            var layout = FindLayout(main, null);
            // A partially repaired installation still owns its remaining helpers.
            return layout.Spans.Any(span => span.Offset <= main.Text.DecompressedData.Length - span.Patched.Length
                && main.Text.DecompressedData.AsSpan(span.Offset, span.Patched.Length).SequenceEqual(span.Patched));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> CreateReservations() => Layouts.SelectMany(layout => layout.Spans.Select(span =>
        new SwShExeFsReservedRegion(SwShExeFsReservedRegionLedger.OwnerTrainerWhiteout,
            $"trainer-whiteout-{layout.Game.ToString().ToLowerInvariant()}-{span.Offset:X}", "exefs/main", "main.text",
            span.Original.Length == 12 ? span.Offset - 4 : span.Offset,
            span.Original.Length == 12 ? 16 : span.Original.Length,
            $"Trainer Whiteout {layout.Game} facility helper", "do-not-overwrite"))).ToArray();

    private static Layout FindLayout(NsoFile main, ProjectGame? expectedGame)
    {
        var id = Convert.ToHexString(main.BuildId);
        var layout = Layouts.SingleOrDefault(layout => id.StartsWith(layout.BuildId, StringComparison.Ordinal))
            ?? throw new InvalidDataException("Trainer Whiteout supports Sword and Shield 1.3.2 executable builds.");
        if (expectedGame is not null && expectedGame != layout.Game)
            throw new InvalidDataException("Trainer Whiteout executable does not match the selected game.");
        return layout;
    }

    private static void RequireRange(byte[] data, int offset, int length)
    {
        if (offset < 4 || offset > data.Length - length)
            throw new InvalidDataException("Trainer Whiteout executable helper is outside the text segment.");
    }

    private static readonly byte[] ReturnInstruction = [0xC0, 0x03, 0x5F, 0xD6];
    private sealed record PatchSpan(int Offset, byte[] Original, byte[] Patched);
    private sealed record Layout(ProjectGame Game, string BuildId, PatchSpan[] Spans);
}
