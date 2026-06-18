// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.Scripts;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.NpcItemGift;

public sealed class SwShAmxCellPatcherTests
{
    private const ushort PawnMagic64 = 0xF1E1;
    private const uint PackedConstantOpcode = 0x000000BC;

    [Fact]
    public void ReadCodeCellIntReadsPackedConstantOperand()
    {
        var amx = CreateExpandedAmx([PackConstant(5), 4]);

        Assert.Equal(5, SwShAmxCellPatcher.ReadCodeCellInt(amx, 0));
        Assert.Equal(4, SwShAmxCellPatcher.ReadCodeCellInt(amx, 1));
    }

    [Fact]
    public void ApplyCodeCellPatchesPreservesPackedConstantOpcode()
    {
        var amx = CreateExpandedAmx([PackConstant(5)]);

        var patched = SwShAmxCellPatcher.ApplyCodeCellPatches(
            amx,
            [new SwShAmxCellPatch(0, 30)]);

        Assert.Equal(PackConstant(30), ReadCodeCell(patched, 0));
        Assert.Equal(30, SwShAmxCellPatcher.ReadCodeCellInt(patched, 0));
    }

    [Fact]
    public void ApplyCodeCellPatchesKeepsPlainIntegerCellsPlain()
    {
        var amx = CreateExpandedAmx([PackedConstantOpcode]);

        var patched = SwShAmxCellPatcher.ApplyCodeCellPatches(
            amx,
            [new SwShAmxCellPatch(0, 30)]);

        Assert.Equal(30UL, ReadCodeCell(patched, 0));
        Assert.Equal(30, SwShAmxCellPatcher.ReadCodeCellInt(patched, 0));
    }

    private static byte[] CreateExpandedAmx(IReadOnlyList<ulong> codeCells)
    {
        const int headerSize = 0x38;
        const int cellSize = 8;
        var dat = headerSize + codeCells.Count * cellSize;
        var amx = new byte[dat];

        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x00), amx.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(amx.AsSpan(0x04), PawnMagic64);
        amx[0x06] = 12;
        amx[0x07] = 14;
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x08), 0);
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x0A), 8);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x0C), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x14), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x18), dat);

        for (var i = 0; i < codeCells.Count; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                amx.AsSpan(headerSize + i * cellSize),
                codeCells[i]);
        }

        return amx;
    }

    private static ulong ReadCodeCell(byte[] amx, int cell)
    {
        const int headerSize = 0x38;
        const int cellSize = 8;
        return BinaryPrimitives.ReadUInt64LittleEndian(amx.AsSpan(headerSize + cell * cellSize));
    }

    private static ulong PackConstant(int value)
    {
        return ((ulong)(uint)value << 32) | PackedConstantOpcode;
    }
}
