// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.FpsPatch;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace KM.SwSh.Tests.FpsPatch;

public sealed class SwShFpsBseqPatcherTests
{
    [Fact]
    public void ConvertScalesMoveEffectTimelineFramesAwayFromZero()
    {
        var data = CreateBseq(frameCount: 125, startFrame: 48, endFrame: 66);

        var patched = SwShFpsBseqPatcher.Convert(data, SwShFpsBseqPatcher.MoveEffectTimelineScale, out var stats);

        Assert.Equal(281u, ReadU32(patched, 0x0C));
        Assert.Equal(108u, ReadU32(patched, 0x24));
        Assert.Equal(149u, ReadU32(patched, 0x28));
        Assert.Equal(1, stats.CommandCount);
        Assert.Equal(3, stats.FieldsChanged);
    }

    [Fact]
    public void ConvertLeavesZeroCommandFramesAtZero()
    {
        var data = CreateBseq(frameCount: 1, startFrame: 0, endFrame: 0);

        var patched = SwShFpsBseqPatcher.Convert(data, SwShFpsBseqPatcher.MoveEffectTimelineScale, out var stats);

        Assert.Equal(2u, ReadU32(patched, 0x0C));
        Assert.Equal(0u, ReadU32(patched, 0x24));
        Assert.Equal(0u, ReadU32(patched, 0x28));
        Assert.Equal(1, stats.FieldsChanged);
    }

    private static byte[] CreateBseq(uint frameCount, uint startFrame, uint endFrame)
    {
        const ulong commandHash = 0x1122334455667788;
        var data = new byte[0x50];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0);
        WriteU32(data, 0x0C, frameCount);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 1);
        WriteU64(data, 0x18, commandHash);
        WriteU32(data, 0x20, 0);
        WriteU32(data, 0x24, startFrame);
        WriteU32(data, 0x28, endFrame);
        WriteU64(data, 0x30, commandHash);
        WriteU32(data, 0x38, 0xFFFFFFFF);
        return data;
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
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
