// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.SwSh;
using KM.SwSh.FairyGymBoosts;

namespace KM.SwSh.MarnieBoosts;

internal static class SwShMarnieBoostsPatcher
{
    internal const int FileLength = 0x683C;
    internal const int PayloadOffset = 0x1554;
    internal const int OwnedLength = 16;
    internal static readonly string[] Paths = Enumerable.Range(135, 3)
        .Select(id => $"romfs/bin/battle/waza/sequence/bk{id}.bseq").ToArray();
    internal static readonly string[] Ids = Enumerable.Range(1, 3)
        .SelectMany(battle => Enumerable.Range(1, 2).Select(answer => $"marnie-{battle}-{answer}")).ToArray();
    internal static int DefaultEffect(int battle) => battle switch { 1 => 6, 2 => 4, 3 => 2, _ => throw new ArgumentOutOfRangeException(nameof(battle)) };

    internal static SwShFairyGymBoostSelection[] Read(byte[] bytes, int battle)
    {
        if (bytes.Length != FileLength) throw new InvalidDataException("The cheering sequence has an unsupported length.");
        var command = SwShBseqFile.Parse(bytes).GetSingleCommand(SwShBseqKnownCommands.SpecialQuizResult, SwShBseqKnownCommands.SpecialQuizResultName);
        if (command.PayloadOffset != PayloadOffset || command.PayloadLength != 24)
            throw new InvalidDataException("The cheering outcome command has an unsupported layout.");
        return Enumerable.Range(1, 2).Select(answer =>
        {
            var offset = PayloadOffset + (answer - 1) * 8;
            var effect = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            var result = SwShFairyGymBoostsWorkflowService.ToResultKind(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4)));
            if (!SwShFairyGymBoostsWorkflowService.IsSupportedSelection(effect, result))
                throw new InvalidDataException("The cheering sequence contains an unsupported outcome.");
            return new SwShFairyGymBoostSelection($"marnie-{battle}-{answer}", effect, result);
        }).ToArray();
    }

    internal static void ValidateBase(byte[] bytes, int battle)
    {
        if (Read(bytes, battle).Any(value => value.EffectId != DefaultEffect(battle) || value.ResultKind != "increase"))
            throw new InvalidDataException("The base cheering sequence does not contain its original outcomes.");
    }

    internal static byte[] Apply(byte[] vanilla, byte[] source, int battle, IReadOnlyList<SwShFairyGymBoostSelection> selections)
    {
        ValidateBase(vanilla, battle);
        _ = Read(source, battle);
        if (selections.Count != 2 || selections.Where((value, index) => value is null
                || value.BoostId != $"marnie-{battle}-{index + 1}"
                || !SwShFairyGymBoostsWorkflowService.IsSupportedSelection(value.EffectId, value.ResultKind)).Any())
            throw new InvalidDataException("Choose both supported cheering outcomes in order.");
        var output = source.ToArray();
        for (var index = 0; index < 2; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(PayloadOffset + index * 8), selections[index].EffectId);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(PayloadOffset + index * 8 + 4),
                SwShFairyGymBoostsWorkflowService.ToResultValue(selections[index].ResultKind));
        }
        if (!Read(output, battle).SequenceEqual(selections)
            || !source.AsSpan(0, PayloadOffset).SequenceEqual(output.AsSpan(0, PayloadOffset))
            || !source.AsSpan(PayloadOffset + OwnedLength).SequenceEqual(output.AsSpan(PayloadOffset + OwnedLength)))
            throw new InvalidDataException("Cheering output failed its preservation check.");
        return output;
    }

    // Timing tools compare their own bytes independently of these six answer slots.
    internal static bool TryRestoreOutcomes(string path, byte[] vanilla, byte[] source, out byte[] normalized)
    {
        normalized = source;
        var battle = Array.FindIndex(Paths, item => item.Equals(path, StringComparison.OrdinalIgnoreCase)) + 1;
        if (battle == 0) return false;
        try
        {
            ValidateBase(vanilla, battle);
            _ = Read(source, battle);
            normalized = source.ToArray();
            vanilla.AsSpan(PayloadOffset, OwnedLength).CopyTo(normalized.AsSpan(PayloadOffset));
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    internal static byte[] PreserveOutcomes(byte[] desired, byte[] current)
    {
        var output = desired.ToArray();
        current.AsSpan(PayloadOffset, OwnedLength).CopyTo(output.AsSpan(PayloadOffset));
        return output;
    }
}
