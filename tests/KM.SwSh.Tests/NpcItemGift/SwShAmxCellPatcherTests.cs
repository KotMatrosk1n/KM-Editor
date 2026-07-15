// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.Scripts;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.NpcItemGift;

public sealed class SwShAmxCellPatcherTests
{
    private const ushort PawnMagic64 = 0xF1E1;
    private const short PawnFlagCompact = 0x0004;
    private const uint PackedConstantOpcode = 0x000000BC;

    [Fact]
    public void ReadCodeCellIntReadsPackedConstantOperand()
    {
        var amx = CreateExpandedAmx([PackConstant(5), 4]);

        Assert.Equal(5, SwShAmxCellPatcher.ReadCodeCellInt(amx, 0));
        Assert.Equal(4, SwShAmxCellPatcher.ReadCodeCellInt(amx, 1));
        Assert.Throws<InvalidDataException>(() => SwShAmxCellPatcher.ReadPackedCodeCellInt(amx, 1));
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

    [Fact]
    public void PackedOperandContextDistinguishesZeroFromPlainInteger188()
    {
        var amx = CreateExpandedAmx([PackConstant(0)]);

        Assert.Equal(188, SwShAmxCellPatcher.ReadCodeCellInt(amx, 0));
        Assert.Equal(0, SwShAmxCellPatcher.ReadPackedCodeCellInt(amx, 0));

        var patched = SwShAmxCellPatcher.ApplyCodeCellPatches(
            amx,
            [new SwShAmxCellPatch(0, 30, RequirePackedConstantOperand: true)]);

        Assert.Equal(PackConstant(30), ReadCodeCell(patched, 0));
        Assert.Equal(30, SwShAmxCellPatcher.ReadPackedCodeCellInt(patched, 0));
    }

    [Fact]
    public void RequiredPackedOperandPatchRejectsPlainIntegerCell()
    {
        var amx = CreateExpandedAmx([4]);

        Assert.Throws<InvalidDataException>(() => SwShAmxCellPatcher.ApplyCodeCellPatches(
            amx,
            [new SwShAmxCellPatch(0, 30, RequirePackedConstantOperand: true)]));
    }

    [Fact]
    public void ApplyCodeCellPatchesPreservesUnchangedCompactAmxBytes()
    {
        var amx = CreateCompactAmx([PackConstant(2), PackConstant(28), 135, 72, 16]);

        var patched = SwShAmxCellPatcher.ApplyCodeCellPatches(amx, []);

        Assert.Equal(amx, patched);
    }

    [Fact]
    public void ApplyCodeCellPatchesKeepsItemOnlyCompactSwapLengthStable()
    {
        var amx = CreateCompactAmx([PackConstant(2), PackConstant(28), 135, 72, 16]);

        var patched = SwShAmxCellPatcher.ApplyCodeCellPatches(
            amx,
            [new SwShAmxCellPatch(1, 5)]);

        Assert.Equal(amx.Length, patched.Length);
        Assert.Equal(2, SwShAmxCellPatcher.ReadCodeCellInt(patched, 0));
        Assert.Equal(5, SwShAmxCellPatcher.ReadCodeCellInt(patched, 1));
    }

    [Fact]
    public void ApplyCodeCellPatchesRejectsDuplicateCells()
    {
        var amx = CreateExpandedAmx([PackConstant(5)]);

        Assert.Throws<InvalidDataException>(() => SwShAmxCellPatcher.ApplyCodeCellPatches(
            amx,
            [new SwShAmxCellPatch(0, 6), new SwShAmxCellPatch(0, 6)]));
    }

    [Fact]
    public void ApplyCodeCellPatchesPreservesCompactSuffix()
    {
        var core = CreateCompactAmx([PackConstant(2), PackConstant(28)]);
        var suffix = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var amx = core.Concat(suffix).ToArray();

        var patched = SwShAmxCellPatcher.ApplyCodeCellPatches(
            amx,
            [new SwShAmxCellPatch(1, 5)]);

        Assert.Equal(suffix, patched[^suffix.Length..]);
        Assert.Equal(patched.Length - suffix.Length, BinaryPrimitives.ReadInt32LittleEndian(patched));
        Assert.Equal(5, SwShAmxCellPatcher.ReadPackedCodeCellInt(patched, 1));
    }

    [Fact]
    public void ReadCodeCellIntRejectsUnsafeCompactExpansionBeforeAllocation()
    {
        var amx = CreateCompactAmx([PackConstant(1)]);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x14), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x18), int.MaxValue);

        var error = Assert.Throws<InvalidDataException>(() =>
            SwShAmxCellPatcher.ReadCodeCellInt(amx, 0));

        Assert.Contains("unsafe expanded-memory size", error.Message, StringComparison.Ordinal);
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

    private static byte[] CreateCompactAmx(IReadOnlyList<ulong> codeCells)
    {
        const int headerSize = 0x38;
        const int cellSize = 8;
        var dat = headerSize + codeCells.Count * cellSize;
        var compactBody = CompactAmxCells(codeCells);
        var amx = new byte[headerSize + compactBody.Length];

        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x00), amx.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(amx.AsSpan(0x04), PawnMagic64);
        amx[0x06] = 12;
        amx[0x07] = 14;
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x08), PawnFlagCompact);
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x0A), 8);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x0C), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x14), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x18), dat);
        compactBody.CopyTo(amx.AsSpan(headerSize));

        return amx;
    }

    private static byte[] CompactAmxCells(IEnumerable<ulong> cells)
    {
        var compact = new List<byte>();
        foreach (var cell in cells)
        {
            var signed = unchecked((long)cell);
            var chunks = new List<byte>();
            var value = signed;
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
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
