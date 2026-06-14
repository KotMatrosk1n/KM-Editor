// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.HyperTraining;

internal enum SwShHyperTrainingScriptKind
{
    NotInstalled,
    CustomMinimumLevel,
    Conflict,
}

internal sealed record SwShHyperTrainingScriptAnalysis(
    SwShHyperTrainingScriptKind Kind,
    string Message,
    int MinimumLevel,
    string ScriptCell);

internal static class SwShHyperTrainingAmxPatcher
{
    public const int VanillaMinimumLevel = 100;
    public const int MinimumAllowedLevel = 1;
    public const int MaximumAllowedLevel = 100;
    public const int LevelThresholdCell = 2294;
    public const string LevelThresholdCellLabel = "AMX code cell 2294 (RND_TO_FLOOR operand)";

    private const ushort PawnMagic16 = 0xF1E2;
    private const ushort PawnMagic32 = 0xF1E0;
    private const ushort PawnMagic64 = 0xF1E1;
    private const short PawnFlagCompact = 0x0004;
    private const int OpRndToFloor = 172;
    private const int OpJsgeq = 64;
    private const int OpFloatGt = 176;

    public static SwShHyperTrainingScriptAnalysis Analyze(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var level = ReadMinimumLevel(data);
            var kind = level == VanillaMinimumLevel
                ? SwShHyperTrainingScriptKind.NotInstalled
                : SwShHyperTrainingScriptKind.CustomMinimumLevel;
            var message = kind == SwShHyperTrainingScriptKind.NotInstalled
                ? "Hyper Training is using the vanilla Lv.100 minimum."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training currently accepts Pokemon at Lv.{level} or higher.");

            return new SwShHyperTrainingScriptAnalysis(
                kind,
                message,
                level,
                LevelThresholdCellLabel);
        }
        catch (InvalidDataException exception)
        {
            return new SwShHyperTrainingScriptAnalysis(
                SwShHyperTrainingScriptKind.Conflict,
                exception.Message,
                VanillaMinimumLevel,
                LevelThresholdCellLabel);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return new SwShHyperTrainingScriptAnalysis(
                SwShHyperTrainingScriptKind.Conflict,
                exception.Message,
                VanillaMinimumLevel,
                LevelThresholdCellLabel);
        }
    }

    public static byte[] ApplyMinimumLevel(byte[] data, int minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateLevel(minimumLevel);

        var decoded = Decode(data);
        var codeCells = ReadCells(
            decoded.Expanded,
            decoded.Header.Cod,
            decoded.Header.Dat - decoded.Header.Cod,
            decoded.CellSize);
        ValidateSupportedShape(codeCells);

        codeCells[LevelThresholdCell] = PackAmxInstruction(OpRndToFloor, minimumLevel, decoded.CellSize);
        WriteCells(decoded.Expanded, decoded.Header.Cod, codeCells, decoded.CellSize);

        if (!decoded.IsCompact)
        {
            BinaryPrimitives.WriteInt32LittleEndian(decoded.Expanded.AsSpan(0), decoded.Expanded.Length);
            return decoded.Expanded;
        }

        var patchedPrefix = data[..decoded.Header.Cod].ToArray();
        var patched = BuildCompactAmx(patchedPrefix, decoded.Header, decoded.Expanded, decoded.CellSize);
        VerifyExpandedMemory(patched, decoded.Expanded);
        return patched;
    }

    public static int ReadMinimumLevel(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var decoded = Decode(data);
        var codeCells = ReadCells(
            decoded.Expanded,
            decoded.Header.Cod,
            decoded.Header.Dat - decoded.Header.Cod,
            decoded.CellSize);
        ValidateSupportedShape(codeCells);
        if (!TryUnpackPackedInstruction(codeCells[LevelThresholdCell], OpRndToFloor, out var minimumLevel))
        {
            throw new InvalidDataException("Hyper Training level threshold cell is not a packed RND_TO_FLOOR instruction.");
        }

        ValidateLevel(minimumLevel);
        return minimumLevel;
    }

    private static void ValidateSupportedShape(IReadOnlyList<ulong> codeCells)
    {
        if (codeCells.Count <= LevelThresholdCell + 2)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training script has {codeCells.Count:N0} code cells; expected the level check at cell {LevelThresholdCell}."));
        }

        if (!TryUnpackPackedInstruction(codeCells[LevelThresholdCell], OpRndToFloor, out var minimumLevel))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training level threshold cell {LevelThresholdCell} is 0x{codeCells[LevelThresholdCell]:X16}; expected packed RND_TO_FLOOR <level>."));
        }

        if (minimumLevel is < MinimumAllowedLevel or > MaximumAllowedLevel)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training minimum level {minimumLevel} is outside the supported {MinimumAllowedLevel}-{MaximumAllowedLevel} range."));
        }

        ExpectCell(codeCells, LevelThresholdCell + 1, OpJsgeq, "Hyper Training level comparison jump");
        ExpectCell(codeCells, LevelThresholdCell + 2, OpFloatGt, "Hyper Training level failure branch");
    }

    private static void ValidateLevel(int minimumLevel)
    {
        if (minimumLevel is < MinimumAllowedLevel or > MaximumAllowedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLevel),
                minimumLevel,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training minimum level must be between {MinimumAllowedLevel} and {MaximumAllowedLevel}."));
        }
    }

    private static DecodedAmx Decode(byte[] data)
    {
        var header = SwShAmxHeader.Read(data);
        var cellSize = GetPawnCellSize(header.Magic);
        if (cellSize != 8)
        {
            throw new InvalidDataException($"Expected 64-bit AMX cells in Hyper Training script; found {cellSize * 8}-bit cells.");
        }

        var isCompact = (header.Flags & PawnFlagCompact) != 0;
        var expanded = ExpandAmxIfNeeded(data, header, cellSize);
        if (isCompact)
        {
            VerifyCompactRoundTrip(data, header, expanded, cellSize);
        }
        else if (header.Hea > expanded.Length)
        {
            throw new InvalidDataException("Expanded Hyper Training AMX memory is shorter than HEA.");
        }

        return new DecodedAmx(header, cellSize, expanded, isCompact);
    }

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
            }
            while (src > 0 && (data[header.Cod + src - 1] & 0x80) != 0);

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
            throw new InvalidDataException("AMX compact round trip for Hyper Training did not preserve expanded memory.");
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

    private static bool TryUnpackPackedInstruction(ulong cell, int opcode, out int operand)
    {
        operand = 0;
        if ((cell & 0xFFFFFFFFUL) != (uint)opcode)
        {
            return false;
        }

        operand = unchecked((int)(uint)(cell >> 32));
        return true;
    }

    private static ulong PackAmxInstruction(int opcode, long operand, int cellSize)
    {
        if (cellSize != 8)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Packed AMX instruction helper currently supports only 64-bit cells.");
        }

        return ((ulong)unchecked((uint)operand) << 32) | (uint)opcode;
    }

    private static void ExpectCell(IReadOnlyList<ulong> cells, int index, long expected, string label)
    {
        if ((uint)index >= (uint)cells.Count)
        {
            throw new InvalidDataException($"{label} cell {index} is outside code cell count {cells.Count}.");
        }

        var actual = SignedCellValue(cells[index], 8);
        if (actual != expected)
        {
            throw new InvalidDataException($"{label} cell {index} is {actual} (0x{cells[index]:X16}); expected {expected}.");
        }
    }

    private static int GetPawnCellSize(ushort magic) => magic switch
    {
        PawnMagic16 => 2,
        PawnMagic32 => 4,
        PawnMagic64 => 8,
        _ => throw new InvalidDataException($"Unknown AMX magic 0x{magic:X4}."),
    };

    private sealed record DecodedAmx(
        SwShAmxHeader Header,
        int CellSize,
        byte[] Expanded,
        bool IsCompact);

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
