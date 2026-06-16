// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.SwSh.FairyGymBoosts;

internal static class SwShFairyGymBoostsBseqPatcher
{
    private const int SlotCount = 3;
    private const int SlotSize = 8;
    private const int PayloadLength = SlotCount * SlotSize;

    private static readonly byte[] CommandHashBytes =
    [
        0x30, 0xB0, 0x56, 0xB8, 0x7A, 0x22, 0x77, 0x69,
    ];

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
        if (data.Length < CommandHashBytes.Length + PayloadLength)
        {
            throw new InvalidDataException("File is too small to contain the Fairy Gym boost command payload.");
        }

        var payloadOffsets = new List<int>();
        for (var index = 0; index <= data.Length - CommandHashBytes.Length; index++)
        {
            if (!data.AsSpan(index, CommandHashBytes.Length).SequenceEqual(CommandHashBytes))
            {
                continue;
            }

            var payloadOffset = index + CommandHashBytes.Length;
            if (IsSupportedPayload(data, payloadOffset))
            {
                payloadOffsets.Add(payloadOffset);
            }
        }

        return payloadOffsets.Count switch
        {
            1 => payloadOffsets[0],
            0 => throw new InvalidDataException("Fairy Gym boost command payload was not found."),
            _ => throw new InvalidDataException("Fairy Gym boost command payload is ambiguous."),
        };
    }

    private static bool IsSupportedPayload(byte[] data, int payloadOffset)
    {
        if (payloadOffset < 0 || payloadOffset + PayloadLength > data.Length)
        {
            return false;
        }

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
