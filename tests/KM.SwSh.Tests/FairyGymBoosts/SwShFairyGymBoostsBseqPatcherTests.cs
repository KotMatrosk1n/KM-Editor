// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.FairyGymBoosts;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace KM.SwSh.Tests.FairyGymBoosts;

public sealed class SwShFairyGymBoostsBseqPatcherTests
{
    private const int PayloadOffset = 0x38;

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
        var data = new byte[0x54];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0x00);
        WriteU32(data, 0x04, SwShBseqFile.ExpectedVersion);
        WriteU32(data, 0x0C, 1);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 1);
        WriteU64(data, 0x18, SwShBseqKnownCommands.SpecialQuizResult);
        WriteU32(data, 0x20, SwShBseqKnownCommands.SpecialQuizResultPayloadLength);

        WriteU64(data, 0x30, SwShBseqKnownCommands.SpecialQuizResult);
        WriteSlot(data, PayloadOffset, answerChoice: 1, effectOne, resultOne);
        WriteSlot(data, PayloadOffset, answerChoice: 2, effectTwo, resultTwo);
        WriteSlot(data, PayloadOffset, answerChoice: 3, effectId: 0, resultValue: 0);
        WriteU32(data, 0x50, 0xFFFFFFFF);
        return data;
    }

    private static (int EffectId, int ResultValue) ReadSlot(byte[] data, int answerChoice)
    {
        var offset = PayloadOffset + ((answerChoice - 1) * 8);
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

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteU64(byte[] data, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }
}
