// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.ZA.Items;

internal static class ZaMachineWazaLayoutDetector
{
    private const int MachineWazaFieldIndex = 11;

    public static ZaMachineWazaLayoutInspection Analyze(ReadOnlySpan<byte> bytes)
    {
        var rootTable = ReadOffsetTarget(bytes, 0, "item-data root");
        var valuesField = GetTableField(bytes, rootTable, 0, "item-data root");
        if (valuesField is null)
        {
            throw new InvalidDataException("The item-data root is missing its required values vector.");
        }

        var valuesVector = ReadOffsetTarget(bytes, valuesField.Value.Position, "item-data values vector");
        var valueCount = ReadInt32(bytes, valuesVector, "item-data values length");
        if (valueCount < 0)
        {
            throw new InvalidDataException("The item-data values vector has a negative length.");
        }

        var vectorData = valuesVector + sizeof(int);
        if (valueCount > (bytes.Length - vectorData) / sizeof(int))
        {
            throw new InvalidDataException("The item-data values vector exceeds the item-data buffer.");
        }

        EnsureRange(
            bytes,
            vectorData,
            valueCount * sizeof(int),
            "item-data values");

        var machineRowCount = 0;
        var unsafeRowCount = 0;
        for (var index = 0; index < valueCount; index++)
        {
            var elementPosition = checked(vectorData + index * sizeof(int));
            var itemTable = ReadOffsetTarget(
                bytes,
                elementPosition,
                $"item-data row {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            var machineWazaField = GetTableField(
                bytes,
                itemTable,
                MachineWazaFieldIndex,
                $"item-data row {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (machineWazaField is null)
            {
                continue;
            }

            var logicalMoveId = ReadUInt16(
                bytes,
                machineWazaField.Value.Position,
                "MachineWaza");
            if (logicalMoveId == 0)
            {
                continue;
            }

            machineRowCount++;
            var fieldFitsNativeRead =
                machineWazaField.Value.RelativeOffset <= machineWazaField.Value.ObjectSize - sizeof(uint);
            if (!fieldFitsNativeRead
                || !HasRange(bytes, machineWazaField.Value.Position, sizeof(uint))
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(machineWazaField.Value.Position, sizeof(uint))) != logicalMoveId)
            {
                unsafeRowCount++;
            }
        }

        return new ZaMachineWazaLayoutInspection(machineRowCount, unsafeRowCount);
    }

    private static TableField? GetTableField(
        ReadOnlySpan<byte> bytes,
        int tablePosition,
        int fieldIndex,
        string context)
    {
        EnsureRange(bytes, tablePosition, sizeof(int), context);
        var vtableDistance = ReadInt32(bytes, tablePosition, $"{context} vtable distance");
        if (vtableDistance == 0)
        {
            throw new InvalidDataException($"The {context} has an invalid vtable distance.");
        }

        var candidateVtablePosition = (long)tablePosition - vtableDistance;
        if (candidateVtablePosition < 0
            || candidateVtablePosition > bytes.Length - 2 * sizeof(ushort))
        {
            throw new InvalidDataException($"The {context} has a vtable outside the item-data buffer.");
        }

        var vtablePosition = checked((int)candidateVtablePosition);
        var vtableSize = ReadUInt16(bytes, vtablePosition, $"{context} vtable size");
        var objectSize = ReadUInt16(
            bytes,
            checked(vtablePosition + sizeof(ushort)),
            $"{context} object size");
        if (vtableSize < 2 * sizeof(ushort)
            || !HasRange(bytes, vtablePosition, vtableSize)
            || !HasRange(bytes, tablePosition, objectSize))
        {
            throw new InvalidDataException($"The {context} has invalid table bounds.");
        }

        var fieldEntryOffset = checked(2 * sizeof(ushort) + fieldIndex * sizeof(ushort));
        if (fieldEntryOffset + sizeof(ushort) > vtableSize)
        {
            return null;
        }

        var relativeFieldOffset = ReadUInt16(
            bytes,
            checked(vtablePosition + fieldEntryOffset),
            $"{context} field {fieldIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (relativeFieldOffset == 0)
        {
            return null;
        }

        if (relativeFieldOffset >= objectSize)
        {
            throw new InvalidDataException($"The {context} has a field outside its table object.");
        }

        return new TableField(
            checked(tablePosition + relativeFieldOffset),
            relativeFieldOffset,
            objectSize);
    }

    private static int ReadOffsetTarget(ReadOnlySpan<byte> bytes, int position, string context)
    {
        EnsureRange(bytes, position, sizeof(uint), context);
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(position, sizeof(uint)));
        var target = (long)position + relativeOffset;
        if (relativeOffset == 0 || target < 0 || target > bytes.Length - sizeof(int))
        {
            throw new InvalidDataException($"The {context} has an invalid FlatBuffer offset.");
        }

        return checked((int)target);
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int position, string context)
    {
        EnsureRange(bytes, position, sizeof(int), context);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(position, sizeof(int)));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int position, string context)
    {
        EnsureRange(bytes, position, sizeof(ushort), context);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(position, sizeof(ushort)));
    }

    private static bool HasRange(ReadOnlySpan<byte> bytes, int position, int length)
    {
        return position >= 0
            && length >= 0
            && position <= bytes.Length - length;
    }

    private static void EnsureRange(
        ReadOnlySpan<byte> bytes,
        int position,
        int length,
        string context)
    {
        if (!HasRange(bytes, position, length))
        {
            throw new InvalidDataException($"The {context} is outside the item-data buffer.");
        }
    }

    private readonly record struct TableField(
        int Position,
        ushort RelativeOffset,
        ushort ObjectSize);
}

internal readonly record struct ZaMachineWazaLayoutInspection(
    int MachineRowCount,
    int UnsafeRowCount)
{
    public bool RequiresRepair => UnsafeRowCount > 0;
}
