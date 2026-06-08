// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.SwSh.RoyalCandy;

internal static class SwShRoyalCandyBagEventScriptPatcher
{
    private const ushort PawnMagic16 = 0xF1E2;
    private const ushort PawnMagic32 = 0xF1E0;
    private const ushort PawnMagic64 = 0xF1E1;
    private const short PawnFlagCompact = 0x0004;
    private const int OpProc = 46;
    private const int OpRetn = 48;
    private const int OpZeroPri = 89;
    private const int OpSysreqN = 135;
    private const int OpPushmPc = 188;
    private const uint DuplicatedNativeHash = 0x0473BE4E;
    private const uint AddItemNativeHash = 0x8D631FFE;
    private const int FreedNativeIndex = 70;
    private const int DuplicateNativeIndex = 76;
    private const int DuplicateNativeCallCell = 3686;
    private const int OriginalNoOpGrantStubCell = 4991;
    private const int GrantStubCallerCell = 5020;
    private const int GrantStubCellCount = 8;

    public static byte[] ApplyGrantPatch(byte[] data, int itemId)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (itemId is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "Royal Candy Bag-event grant item id must fit the AMX patch range.");
        }

        var header = SwShAmxHeader.Read(data);
        var cellSize = GetPawnCellSize(header.Magic);
        if (cellSize != 8)
        {
            throw new InvalidDataException($"Expected 64-bit AMX cells in {SwShRoyalCandyWorkflowService.BagEventScriptPath}; found {cellSize * 8}-bit cells.");
        }

        if ((header.Flags & PawnFlagCompact) == 0)
        {
            throw new InvalidDataException($"{SwShRoyalCandyWorkflowService.BagEventScriptPath} is not compact AMX; the Bag-event patcher expects the vanilla compact layout.");
        }

        var nativeHashes = ReadNativeHashes(data, header);
        ExpectNative(nativeHashes, FreedNativeIndex, DuplicatedNativeHash);
        ExpectNative(nativeHashes, DuplicateNativeIndex, DuplicatedNativeHash);

        var expanded = ExpandAmxIfNeeded(data, header, cellSize);
        VerifyCompactRoundTrip(data, header, expanded, cellSize);

        if (header.Publics != header.Natives)
        {
            throw new InvalidDataException($"{SwShRoyalCandyWorkflowService.BagEventScriptPath} has public entries; refusing to append the Bag grant without public-table analysis.");
        }

        var codeCells = ReadCells(expanded, header.Cod, header.Dat - header.Cod, cellSize);
        ExpectCell(codeCells, DuplicateNativeCallCell, OpSysreqN, "duplicate native SYSREQ.N");
        ExpectCell(codeCells, DuplicateNativeCallCell + 1, FreedNativeIndex, "duplicate native index");
        ExpectCell(codeCells, DuplicateNativeCallCell + 2, 8, "duplicate native parameter byte count");
        ExpectCell(codeCells, OriginalNoOpGrantStubCell, OpProc, "Bag-event original no-op PROC");
        ExpectCell(codeCells, OriginalNoOpGrantStubCell + 1, OpZeroPri, "Bag-event original no-op ZERO.pri");
        ExpectCell(codeCells, OriginalNoOpGrantStubCell + 2, OpRetn, "Bag-event original no-op RETN");
        ExpectLocalCall(codeCells, GrantStubCallerCell, OriginalNoOpGrantStubCell, cellSize, "Bag-event no-op caller");

        var grantStubCell = codeCells.Length;
        ulong[] grantStub =
        [
            OpProc,
            PackAmxInstruction(OpPushmPc, 1, cellSize),
            PackAmxInstruction(OpPushmPc, itemId, cellSize),
            OpSysreqN,
            FreedNativeIndex,
            16,
            OpZeroPri,
            OpRetn,
        ];
        if (grantStub.Length != GrantStubCellCount)
        {
            throw new InvalidOperationException("Royal Candy AMX grant stub size changed unexpectedly.");
        }

        var patchedHeader = header with
        {
            Dat = header.Dat + GrantStubCellCount * cellSize,
            Hea = header.Hea + GrantStubCellCount * cellSize,
            Stp = header.Stp + GrantStubCellCount * cellSize,
        };
        var patchedExpanded = InsertAmxCodeCells(expanded, header, patchedHeader, grantStub, cellSize);

        codeCells = ReadCells(patchedExpanded, patchedHeader.Cod, patchedHeader.Dat - patchedHeader.Cod, cellSize);
        codeCells[DuplicateNativeCallCell + 1] = DuplicateNativeIndex;
        codeCells[GrantStubCallerCell + 1] = unchecked((ulong)((grantStubCell - GrantStubCallerCell) * cellSize));
        WriteCells(patchedExpanded, patchedHeader.Cod, codeCells, cellSize);

        var patchedPrefix = data[..header.Cod].ToArray();
        WriteAmxHeaderFields(patchedPrefix, patchedHeader);
        WriteAmxHeaderFields(patchedExpanded, patchedHeader);
        var freedNativeHashOffset = header.Natives + FreedNativeIndex * header.DefSize + 8;
        BinaryPrimitives.WriteUInt32LittleEndian(patchedPrefix.AsSpan(freedNativeHashOffset), AddItemNativeHash);
        BinaryPrimitives.WriteUInt32LittleEndian(patchedExpanded.AsSpan(freedNativeHashOffset), AddItemNativeHash);

        var patched = BuildCompactAmx(patchedPrefix, patchedHeader, patchedExpanded, cellSize);
        BinaryPrimitives.WriteInt32LittleEndian(patchedExpanded.AsSpan(0), patched.Length);
        VerifyExpandedMemory(patched, patchedExpanded);
        return patched;
    }

    private static byte[] InsertAmxCodeCells(byte[] expanded, SwShAmxHeader header, SwShAmxHeader patchedHeader, ulong[] cellsToAppend, int cellSize)
    {
        if (patchedHeader.Cod != header.Cod)
        {
            throw new InvalidDataException("AMX code insertion cannot change COD.");
        }

        var appendLength = cellsToAppend.Length * cellSize;
        if (patchedHeader.Dat != header.Dat + appendLength || patchedHeader.Hea != header.Hea + appendLength)
        {
            throw new InvalidDataException("AMX patched header does not match the requested appended code length.");
        }

        var result = new byte[patchedHeader.Hea];
        Array.Copy(expanded, 0, result, 0, header.Dat);
        WriteCells(result, header.Dat, cellsToAppend, cellSize);
        Array.Copy(expanded, header.Dat, result, patchedHeader.Dat, header.Hea - header.Dat);
        return result;
    }

    private static void WriteAmxHeaderFields(byte[] data, SwShAmxHeader header)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), header.Size);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), header.Cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), header.Dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), header.Hea);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), header.Stp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), header.Cip);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), header.Publics);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), header.Natives);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), header.Libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), header.PubVars);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), header.Tags);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), header.NameTable);
    }

    private static int GetPawnCellSize(ushort magic) => magic switch
    {
        PawnMagic16 => 2,
        PawnMagic32 => 4,
        PawnMagic64 => 8,
        _ => throw new InvalidDataException($"Unknown AMX magic 0x{magic:X4}."),
    };

    private static uint[] ReadNativeHashes(byte[] data, SwShAmxHeader header)
    {
        if (header.DefSize <= 0 || header.Libraries < header.Natives)
        {
            return [];
        }

        var count = (header.Libraries - header.Natives) / header.DefSize;
        var hashes = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var offset = header.Natives + i * header.DefSize;
            if (offset + header.DefSize > data.Length)
            {
                break;
            }

            hashes[i] = header.DefSize >= 12
                ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8))
                : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        }

        return hashes;
    }

    private static byte[] ExpandAmxIfNeeded(byte[] data, SwShAmxHeader header, int cellSize)
    {
        if ((header.Flags & PawnFlagCompact) == 0)
        {
            return data;
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
        if (!expanded.AsSpan(0, expectedExpanded.Length).SequenceEqual(expectedExpanded))
        {
            throw new InvalidDataException($"AMX compact round trip for {SwShRoyalCandyWorkflowService.BagEventScriptPath} did not preserve expanded memory.");
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

    private static void WriteCells(byte[] data, int offset, ulong[] cells, int cellSize)
    {
        for (var i = 0; i < cells.Length; i++)
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

    private static void ExpectNative(uint[] nativeHashes, int index, uint expectedHash)
    {
        if ((uint)index >= (uint)nativeHashes.Length)
        {
            throw new InvalidDataException($"{SwShRoyalCandyWorkflowService.BagEventScriptPath} native index {index} is outside import table length {nativeHashes.Length}.");
        }

        if (nativeHashes[index] != expectedHash)
        {
            throw new InvalidDataException($"{SwShRoyalCandyWorkflowService.BagEventScriptPath} native index {index} is 0x{nativeHashes[index]:X8}; expected 0x{expectedHash:X8}.");
        }
    }

    private static void ExpectCell(ulong[] cells, int index, long expected, string label)
    {
        if ((uint)index >= (uint)cells.Length)
        {
            throw new InvalidDataException($"{label} cell {index} is outside code cell count {cells.Length}.");
        }

        var actual = unchecked((long)cells[index]);
        if (actual != expected)
        {
            throw new InvalidDataException($"{label} cell {index} is {actual} (0x{cells[index]:X16}); expected {expected}.");
        }
    }

    private static void ExpectLocalCall(ulong[] cells, int callCell, int expectedTargetCell, int cellSize, string label)
    {
        ExpectCell(cells, callCell, 49, label);
        if ((uint)(callCell + 1) >= (uint)cells.Length)
        {
            throw new InvalidDataException($"{label} call cell {callCell} has no relative operand.");
        }

        var relativeBytes = SignedCellValue(cells[callCell + 1], cellSize);
        if (relativeBytes % cellSize != 0)
        {
            throw new InvalidDataException($"{label} call cell {callCell} has unaligned relative target {relativeBytes}.");
        }

        var targetCell = callCell + relativeBytes / cellSize;
        if (targetCell != expectedTargetCell)
        {
            throw new InvalidDataException($"{label} call cell {callCell} targets {targetCell}; expected {expectedTargetCell}.");
        }
    }

    private static ulong PackAmxInstruction(int opcode, long operand, int cellSize)
    {
        if (cellSize != 8)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Packed AMX instruction helper currently supports only 64-bit cells.");
        }

        return ((ulong)unchecked((uint)operand) << 32) | (uint)opcode;
    }

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

            return new SwShAmxHeader(
                ReadI32(data, 0x00),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x04)),
                data[0x06],
                data[0x07],
                BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x08)),
                BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x0A)),
                ReadI32(data, 0x0C),
                ReadI32(data, 0x10),
                ReadI32(data, 0x14),
                ReadI32(data, 0x18),
                ReadI32(data, 0x1C),
                ReadI32(data, 0x20),
                ReadI32(data, 0x24),
                ReadI32(data, 0x28),
                ReadI32(data, 0x2C),
                ReadI32(data, 0x30),
                ReadI32(data, 0x34));
        }

        private static int ReadI32(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
    }
}
