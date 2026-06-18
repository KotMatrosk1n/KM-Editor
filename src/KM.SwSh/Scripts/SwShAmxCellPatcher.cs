// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.SwSh.Scripts;

internal sealed record SwShAmxCellPatch(
    int Cell,
    int Value);

internal static class SwShAmxCellPatcher
{
    private const ushort PawnMagic16 = 0xF1E2;
    private const ushort PawnMagic32 = 0xF1E0;
    private const ushort PawnMagic64 = 0xF1E1;
    private const short PawnFlagCompact = 0x0004;

    public static int ReadCodeCellInt(byte[] data, int cell)
    {
        ArgumentNullException.ThrowIfNull(data);

        var decoded = Decode(data);
        var codeCells = ReadCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, decoded.CellSize);
        if ((uint)cell >= (uint)codeCells.Length)
        {
            throw new InvalidDataException($"AMX code cell {cell} is outside code cell count {codeCells.Length}.");
        }

        var value = SignedCellValue(codeCells[cell], decoded.CellSize);
        if (value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidDataException($"AMX code cell {cell} value {value} is outside the supported 32-bit integer range.");
        }

        return (int)value;
    }

    public static byte[] ApplyCodeCellPatches(byte[] data, IReadOnlyList<SwShAmxCellPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(patches);

        var decoded = Decode(data);
        var codeCells = ReadCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, decoded.CellSize);
        foreach (var patch in patches)
        {
            if ((uint)patch.Cell >= (uint)codeCells.Length)
            {
                throw new InvalidDataException($"AMX code cell {patch.Cell} is outside code cell count {codeCells.Length}.");
            }

            if (patch.Value < 0)
            {
                throw new InvalidDataException($"AMX code cell {patch.Cell} value {patch.Value} must not be negative.");
            }

            codeCells[patch.Cell] = unchecked((ulong)patch.Value);
        }

        WriteCells(decoded.Expanded, decoded.Header.Cod, codeCells, decoded.CellSize);
        if (!decoded.WasCompact)
        {
            return decoded.Expanded;
        }

        var compact = BuildCompactAmx(data[..decoded.Header.Cod], decoded.Header, decoded.Expanded, decoded.CellSize);
        VerifyExpandedMemory(compact, decoded.Expanded);
        return compact;
    }

    private static DecodedAmx Decode(byte[] data)
    {
        var header = SwShAmxHeader.Read(data);
        var cellSize = GetPawnCellSize(header.Magic);
        if (cellSize != 8)
        {
            throw new InvalidDataException($"Expected 64-bit AMX cells; found {cellSize * 8}-bit cells.");
        }

        var wasCompact = (header.Flags & PawnFlagCompact) != 0;
        var expanded = wasCompact ? ExpandAmxIfNeeded(data, header, cellSize) : data.ToArray();
        if (wasCompact)
        {
            VerifyCompactRoundTrip(data, header, expanded, cellSize);
        }

        return new DecodedAmx(header, cellSize, wasCompact, expanded);
    }

    private static int GetPawnCellSize(ushort magic) => magic switch
    {
        PawnMagic16 => 2,
        PawnMagic32 => 4,
        PawnMagic64 => 8,
        _ => throw new InvalidDataException($"Unknown AMX magic 0x{magic:X4}."),
    };

    private static byte[] ExpandAmxIfNeeded(byte[] data, SwShAmxHeader header, int cellSize)
    {
        if ((header.Flags & PawnFlagCompact) == 0)
        {
            return data.ToArray();
        }

        if (header.Hea < header.Cod || header.Size < header.Cod || header.Size > data.Length)
        {
            throw new InvalidDataException("AMX compact header has inconsistent code/data bounds.");
        }

        var expanded = new byte[header.Hea];
        Array.Copy(data, expanded, Math.Min(header.Cod, data.Length));

        var src = header.Size - header.Cod;
        var dst = header.Hea - header.Cod;
        if (dst % cellSize != 0)
        {
            throw new InvalidDataException($"Expanded AMX memory size 0x{dst:X} is not aligned to {cellSize}-byte cells.");
        }

        while (src > 0)
        {
            ulong cell = 0;
            var shift = 0;
            var signSource = 0;
            do
            {
                src--;
                signSource = header.Cod + src;
                var current = data[signSource];
                cell |= (ulong)(current & 0x7F) << shift;
                shift += 7;
            } while (src > 0 && (data[header.Cod + src - 1] & 0x80) != 0);

            if ((data[signSource] & 0x40) != 0)
            {
                while (shift < cellSize * 8)
                {
                    cell |= 0xFFUL << shift;
                    shift += 8;
                }
            }

            dst -= cellSize;
            if (dst < 0)
            {
                throw new InvalidDataException("AMX compact expansion produced more cells than the header allows.");
            }

            WriteCell(expanded, header.Cod + dst, cell, cellSize);
        }

        if (dst != 0)
        {
            throw new InvalidDataException($"AMX compact expansion stopped with 0x{dst:X} bytes unwritten.");
        }

        return expanded;
    }

    private static void VerifyCompactRoundTrip(byte[] original, SwShAmxHeader header, byte[] expanded, int cellSize)
    {
        var rebuilt = BuildCompactAmx(original[..header.Cod], header, expanded, cellSize);
        VerifyExpandedMemory(rebuilt, expanded);
    }

    private static byte[] BuildCompactAmx(byte[] prefix, SwShAmxHeader header, byte[] expanded, int cellSize)
    {
        if (prefix.Length != header.Cod)
        {
            throw new InvalidDataException($"AMX compact prefix length 0x{prefix.Length:X} does not match COD 0x{header.Cod:X}.");
        }

        if (expanded.Length < header.Hea)
        {
            throw new InvalidDataException("Expanded AMX memory is shorter than HEA.");
        }

        var compactBody = CompactAmxMemory(expanded, header, cellSize);
        var result = new byte[header.Cod + compactBody.Length];
        Array.Copy(prefix, result, prefix.Length);
        Array.Copy(compactBody, 0, result, header.Cod, compactBody.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0), result.Length);
        return result;
    }

    private static byte[] CompactAmxMemory(byte[] expanded, SwShAmxHeader header, int cellSize)
    {
        var compact = new List<byte>(Math.Max(0, header.Size - header.Cod));
        for (var offset = header.Cod; offset < header.Hea; offset += cellSize)
        {
            var signed = SignedCellValue(ReadCell(expanded, offset, cellSize), cellSize);
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

    private static void VerifyExpandedMemory(byte[] compactData, byte[] expectedExpanded)
    {
        var header = SwShAmxHeader.Read(compactData);
        var cellSize = GetPawnCellSize(header.Magic);
        var expanded = ExpandAmxIfNeeded(compactData, header, cellSize);
        var normalizedExpected = expectedExpanded.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(normalizedExpected.AsSpan(0x00), compactData.Length);
        if (!expanded.AsSpan(0, normalizedExpected.Length).SequenceEqual(normalizedExpected))
        {
            throw new InvalidDataException("AMX compact round trip did not preserve expanded memory.");
        }
    }

    private static ulong[] ReadCells(byte[] data, int offset, int length, int cellSize)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
        {
            throw new InvalidDataException($"AMX cell read is outside expanded data: offset 0x{offset:X}, length 0x{length:X}.");
        }

        if (length % cellSize != 0)
        {
            throw new InvalidDataException($"AMX cell span length 0x{length:X} is not aligned to {cellSize}-byte cells.");
        }

        var cells = new ulong[length / cellSize];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = ReadCell(data, offset + i * cellSize, cellSize);
        }

        return cells;
    }

    private static void WriteCells(byte[] data, int offset, IReadOnlyList<ulong> cells, int cellSize)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            WriteCell(data, offset + i * cellSize, cells[i], cellSize);
        }
    }

    private static ulong ReadCell(byte[] data, int offset, int cellSize) => cellSize switch
    {
        2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)),
        8 => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset)),
        _ => throw new ArgumentOutOfRangeException(nameof(cellSize)),
    };

    private static void WriteCell(byte[] data, int offset, ulong value, int cellSize)
    {
        switch (cellSize)
        {
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)value));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), checked((uint)value));
                break;
            case 8:
                BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cellSize));
        }
    }

    private static long SignedCellValue(ulong value, int cellSize) => cellSize switch
    {
        2 => unchecked((short)(ushort)value),
        4 => unchecked((int)(uint)value),
        8 => unchecked((long)value),
        _ => throw new ArgumentOutOfRangeException(nameof(cellSize)),
    };

    private sealed record DecodedAmx(
        SwShAmxHeader Header,
        int CellSize,
        bool WasCompact,
        byte[] Expanded);

    private sealed record SwShAmxHeader(
        int Size,
        ushort Magic,
        byte FileVersion,
        byte AmxVersion,
        short Flags,
        short DefSize,
        int Cod,
        int Dat,
        int Hea,
        int Stp,
        int Cip,
        int Publics,
        int Natives,
        int Libraries,
        int PubVars,
        int Tags,
        int NameTable)
    {
        internal static SwShAmxHeader Read(byte[] data)
        {
            if (data.Length < 0x38)
            {
                throw new InvalidDataException("AMX file is too small for a standard header.");
            }

            var header = new SwShAmxHeader(
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x00)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x04)),
                data[0x06],
                data[0x07],
                BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x08)),
                BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x0A)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x0C)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x10)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x14)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x18)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x1C)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x20)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x24)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x28)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x2C)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x30)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x34)));

            if (header.Size < 0 || header.Size > data.Length)
            {
                throw new InvalidDataException($"AMX header size 0x{header.Size:X} is outside the file length 0x{data.Length:X}.");
            }

            if (header.Cod < 0 || header.Dat < header.Cod || header.Hea < header.Dat || header.Stp < header.Hea)
            {
                throw new InvalidDataException("AMX header has invalid COD/DAT/HEA/STP ordering.");
            }

            return header;
        }
    }
}
