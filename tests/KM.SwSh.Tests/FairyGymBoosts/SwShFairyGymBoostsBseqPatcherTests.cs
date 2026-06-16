// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.FairyGymBoosts;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.FairyGymBoosts;

public sealed class SwShFairyGymBoostsBseqPatcherTests
{
    [Fact]
    public void ApplySelectionsWritesOnlyRequestedAnswerSlots()
    {
        var data = CreateBseq(effectOne: 6, resultOne: 2, effectTwo: 6, resultTwo: 1);

        var patched = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            data,
            [
                new SwShFairyGymBoostAnswerPatch(1, 0, 0),
                new SwShFairyGymBoostAnswerPatch(2, 5, 2),
            ]);

        Assert.Equal((0, 0), ReadSlot(patched, answerChoice: 1));
        Assert.Equal((5, 2), ReadSlot(patched, answerChoice: 2));
        Assert.Equal((0, 0), ReadSlot(patched, answerChoice: 3));
    }

    [Fact]
    public void ReadAnswerSlotsReturnsCurrentPayload()
    {
        var data = CreateBseq(effectOne: 3, resultOne: 2, effectTwo: 3, resultTwo: 1);

        var slots = SwShFairyGymBoostsBseqPatcher.ReadAnswerSlots(data);

        Assert.Equal(3, slots[0].EffectId);
        Assert.Equal(2, slots[0].ResultValue);
        Assert.Equal(3, slots[1].EffectId);
        Assert.Equal(1, slots[1].ResultValue);
        Assert.Equal(0, slots[2].EffectId);
        Assert.Equal(0, slots[2].ResultValue);
    }

    private static byte[] CreateBseq(
        int effectOne,
        int resultOne,
        int effectTwo,
        int resultTwo)
    {
        var data = new byte[0x200];
        var hashOffset = 0x80;
        var payloadOffset = hashOffset + CommandHashBytes.Length;
        CommandHashBytes.AsSpan().CopyTo(data.AsSpan(hashOffset));
        WriteSlot(data, payloadOffset, answerChoice: 1, effectOne, resultOne);
        WriteSlot(data, payloadOffset, answerChoice: 2, effectTwo, resultTwo);
        WriteSlot(data, payloadOffset, answerChoice: 3, effectId: 0, resultValue: 0);
        return data;
    }

    private static (int EffectId, int ResultValue) ReadSlot(byte[] data, int answerChoice)
    {
        var offset = 0x80 + CommandHashBytes.Length + ((answerChoice - 1) * 8);
        return (
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + sizeof(int), sizeof(int))));
    }

    private static void WriteSlot(
        byte[] data,
        int payloadOffset,
        int answerChoice,
        int effectId,
        int resultValue)
    {
        var offset = payloadOffset + ((answerChoice - 1) * 8);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), effectId);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + sizeof(int), sizeof(int)), resultValue);
    }

    private static readonly byte[] CommandHashBytes =
    [
        0x30, 0xB0, 0x56, 0xB8, 0x7A, 0x22, 0x77, 0x69,
    ];
}
