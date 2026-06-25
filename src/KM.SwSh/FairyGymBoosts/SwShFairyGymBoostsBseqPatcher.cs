// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;

namespace KM.SwSh.FairyGymBoosts;

internal static class SwShFairyGymBoostsBseqPatcher
{
    private const int SlotCount = 3;
    private const int SlotSize = 8;

    public static IReadOnlyList<SwShFairyGymBoostAnswerSlot> ReadAnswerSlots(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var payloadOffset = FindPayloadOffset(data);
        var slots = new SwShFairyGymBoostAnswerSlot[SlotCount];
        for (var slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            var slotOffset = payloadOffset + (slotIndex * SlotSize);
            slots[slotIndex] = new SwShFairyGymBoostAnswerSlot(
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slotOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slotOffset + sizeof(int), sizeof(int))));
        }

        return slots;
    }

    public static byte[] ApplySelections(
        byte[] data,
        IReadOnlyList<SwShFairyGymBoostAnswerPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(patches);

        var output = data.ToArray();
        var payloadOffset = FindPayloadOffset(output);

        foreach (var patch in patches)
        {
            ValidatePatch(patch);

            var slotOffset = payloadOffset + ((patch.AnswerChoice - 1) * SlotSize);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(slotOffset, sizeof(int)), patch.EffectId);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(slotOffset + sizeof(int), sizeof(int)), patch.ResultValue);
        }

        return output;
    }

    private static int FindPayloadOffset(byte[] data)
    {
        var file = SwShBseqFile.Parse(data);
        var command = file.GetSingleCommand(
            SwShBseqKnownCommands.SpecialQuizResult,
            SwShBseqKnownCommands.SpecialQuizResultName);
        if (command.PayloadLength != SwShBseqKnownCommands.SpecialQuizResultPayloadLength)
        {
            throw new InvalidDataException("Fairy Gym boost command payload has an unexpected size.");
        }

        if (!IsSupportedPayload(data, command.PayloadOffset))
        {
            throw new InvalidDataException("Fairy Gym boost command payload has unsupported answer slot values.");
        }

        return command.PayloadOffset;
    }

    private static bool IsSupportedPayload(byte[] data, int payloadOffset)
    {
        for (var slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            var slotOffset = payloadOffset + (slotIndex * SlotSize);
            var effectId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slotOffset, sizeof(int)));
            var resultValue = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slotOffset + sizeof(int), sizeof(int)));
            if (!IsSupportedSlot(effectId, resultValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedSlot(int effectId, int resultValue)
    {
        return effectId switch
        {
            0 => resultValue == 0,
            >= 1 and <= 6 => resultValue is 1 or 2,
            _ => false,
        };
    }

    private static void ValidatePatch(SwShFairyGymBoostAnswerPatch patch)
    {
        if (patch.AnswerChoice is < 1 or > SlotCount)
        {
            throw new InvalidDataException("Fairy Gym boost answer choice must be 1, 2, or 3.");
        }

        if (!IsSupportedSlot(patch.EffectId, patch.ResultValue))
        {
            throw new InvalidDataException("Fairy Gym boost patches only support no effect, boost, or drop outcomes from the known command presets.");
        }
    }
}
