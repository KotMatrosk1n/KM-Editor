// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Workflows;

namespace KM.ZA.GameModules;

public sealed record ZaReadOnlyProjectionSource(
    string VirtualPath,
    string RelativePath,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

internal static class ZaReadOnlyProjectionSupport
{
    public static void ValidateProject(OpenedProject project, string projectionLabel)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionLabel);

        if (project.Paths.SelectedGame is not ProjectGame.ZA
            || !project.Health.CanOpenReadOnlyWorkflows)
        {
            throw new InvalidDataException(
                $"{projectionLabel} requires a readable Z-A project with an explicit game binding.");
        }
    }

    public static ZaReadOnlyProjectionSource ToSource(ZaWorkflowFile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ZaReadOnlyProjectionSource(
            source.VirtualPath,
            source.RelativePath,
            source.SourceLayer,
            source.FileState);
    }
}

/// <summary>
/// Minimal read-only FlatBuffer decoder for independently bounded projections.
/// Every caller supplies the maximum known field count for each table so schema
/// growth fails closed instead of being interpreted as an established shape.
/// </summary>
internal sealed class ZaReadOnlyFlatBufferReader
{
    private const int VtableHeaderSize = sizeof(ushort) * 2;
    private const int MaximumTableObjectByteLength = 4_096;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly byte[] data;
    private readonly string payloadLabel;
    private readonly int maximumStringByteLength;
    private readonly long maximumAggregateStringBytes;
    private long aggregateStringBytes;

    public ZaReadOnlyFlatBufferReader(
        byte[] data,
        string payloadLabel,
        int maximumPayloadBytes,
        int maximumStringByteLength,
        long maximumAggregateStringBytes)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadLabel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStringByteLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAggregateStringBytes);

        if (data.Length == 0 || data.Length > maximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"{payloadLabel} is empty or exceeds its bounded payload size.");
        }

        this.data = data;
        this.payloadLabel = payloadLabel;
        this.maximumStringByteLength = maximumStringByteLength;
        this.maximumAggregateStringBytes = maximumAggregateStringBytes;
    }

    public int ReadRootTable(int maximumFieldCount, string tableLabel)
    {
        EnsureRange(0, sizeof(uint), "root offset");
        var table = ResolveForwardOffset(0, ReadUInt32(0), $"{tableLabel} root table");
        ValidateTable(table, maximumFieldCount, tableLabel);
        return table;
    }

    public int ReadRequiredTableField(
        int table,
        int tableMaximumFieldCount,
        int field,
        int targetMaximumFieldCount,
        string fieldLabel)
    {
        var fieldAddress = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(uint),
            fieldLabel);
        if (fieldAddress is null)
        {
            throw new InvalidDataException($"{payloadLabel} is missing its {fieldLabel}.");
        }

        var target = ResolveForwardOffset(
            fieldAddress.Value,
            ReadUInt32(fieldAddress.Value),
            fieldLabel);
        ValidateTable(target, targetMaximumFieldCount, fieldLabel);
        return target;
    }

    public IReadOnlyList<int> ReadRequiredTableVectorField(
        int table,
        int tableMaximumFieldCount,
        int field,
        int maximumCount,
        string fieldLabel)
    {
        var fieldAddress = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(uint),
            fieldLabel);
        if (fieldAddress is null)
        {
            throw new InvalidDataException($"{payloadLabel} is missing its {fieldLabel}.");
        }

        return ReadTableVector(fieldAddress.Value, maximumCount, fieldLabel);
    }

    public IReadOnlyList<int> ReadOptionalTableVectorField(
        int table,
        int tableMaximumFieldCount,
        int field,
        int maximumCount,
        string fieldLabel)
    {
        var fieldAddress = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(uint),
            fieldLabel);
        return fieldAddress is null
            ? Array.Empty<int>()
            : ReadTableVector(fieldAddress.Value, maximumCount, fieldLabel);
    }

    public byte[] ReadRequiredByteVectorField(
        int table,
        int tableMaximumFieldCount,
        int field,
        int maximumByteCount,
        string fieldLabel)
    {
        var fieldAddress = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(uint),
            fieldLabel);
        if (fieldAddress is null)
        {
            throw new InvalidDataException($"{payloadLabel} is missing its {fieldLabel}.");
        }

        var vector = ResolveForwardOffset(
            fieldAddress.Value,
            ReadUInt32(fieldAddress.Value),
            fieldLabel);
        EnsureRange(vector, sizeof(uint), $"{fieldLabel} length");
        var byteCountValue = ReadUInt32(vector);
        if (byteCountValue == 0 || byteCountValue > maximumByteCount)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} is empty or exceeds its bounded byte count.");
        }

        var byteCount = checked((int)byteCountValue);
        var content = checked(vector + sizeof(uint));
        EnsureRange(content, byteCount, fieldLabel);
        return data.AsSpan(content, byteCount).ToArray();
    }

    public string ReadRequiredStringField(
        int table,
        int tableMaximumFieldCount,
        int field,
        string fieldLabel)
    {
        return ReadOptionalStringField(
                table,
                tableMaximumFieldCount,
                field,
                fieldLabel)
            ?? throw new InvalidDataException($"{payloadLabel} is missing its {fieldLabel}.");
    }

    public string? ReadOptionalStringField(
        int table,
        int tableMaximumFieldCount,
        int field,
        string fieldLabel)
    {
        var fieldAddress = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(uint),
            fieldLabel);
        if (fieldAddress is null)
        {
            return null;
        }

        var text = ResolveForwardOffset(
            fieldAddress.Value,
            ReadUInt32(fieldAddress.Value),
            fieldLabel);
        EnsureRange(text, sizeof(uint), $"{fieldLabel} length");
        var byteLengthValue = ReadUInt32(text);
        if (byteLengthValue > maximumStringByteLength)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} exceeds its bounded UTF-8 byte length.");
        }

        var byteLength = checked((int)byteLengthValue);
        var content = checked(text + sizeof(uint));
        EnsureRange(content, checked(byteLength + 1), fieldLabel);
        if (data[checked(content + byteLength)] != 0)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} has no FlatBuffer string terminator.");
        }

        aggregateStringBytes = checked(aggregateStringBytes + byteLength);
        if (aggregateStringBytes > maximumAggregateStringBytes)
        {
            throw new InvalidDataException(
                $"{payloadLabel} exceeds its bounded aggregate UTF-8 byte count.");
        }

        return StrictUtf8.GetString(data, content, byteLength);
    }

    public byte ReadByteField(
        int table,
        int tableMaximumFieldCount,
        int field,
        byte defaultValue,
        string fieldLabel)
    {
        var address = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(byte),
            fieldLabel);
        return address is null ? defaultValue : data[address.Value];
    }

    public bool ReadBooleanField(
        int table,
        int tableMaximumFieldCount,
        int field,
        bool defaultValue,
        string fieldLabel)
    {
        var value = ReadByteField(
            table,
            tableMaximumFieldCount,
            field,
            defaultValue ? (byte)1 : (byte)0,
            fieldLabel);
        if (value > 1)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} is not a valid FlatBuffer boolean.");
        }

        return value != 0;
    }

    public ushort ReadUInt16Field(
        int table,
        int tableMaximumFieldCount,
        int field,
        ushort defaultValue,
        string fieldLabel)
    {
        var address = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(ushort),
            fieldLabel);
        return address is null ? defaultValue : ReadUInt16(address.Value);
    }

    public short ReadInt16Field(
        int table,
        int tableMaximumFieldCount,
        int field,
        short defaultValue,
        string fieldLabel)
    {
        var address = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(short),
            fieldLabel);
        return address is null ? defaultValue : ReadInt16(address.Value);
    }

    public uint ReadUInt32Field(
        int table,
        int tableMaximumFieldCount,
        int field,
        uint defaultValue,
        string fieldLabel)
    {
        var address = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(uint),
            fieldLabel);
        return address is null ? defaultValue : ReadUInt32(address.Value);
    }

    public (float X, float Y, float Z) ReadRequiredVector3Field(
        int table,
        int tableMaximumFieldCount,
        int field,
        string fieldLabel)
    {
        var address = ReadFieldAddress(
            table,
            tableMaximumFieldCount,
            field,
            sizeof(float) * 3,
            fieldLabel);
        if (address is null)
        {
            throw new InvalidDataException($"{payloadLabel} is missing its {fieldLabel}.");
        }

        return (
            ReadSingle(address.Value),
            ReadSingle(checked(address.Value + sizeof(float))),
            ReadSingle(checked(address.Value + (sizeof(float) * 2))));
    }

    private IReadOnlyList<int> ReadTableVector(
        int fieldAddress,
        int maximumCount,
        string fieldLabel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        var vector = ResolveForwardOffset(
            fieldAddress,
            ReadUInt32(fieldAddress),
            fieldLabel);
        EnsureRange(vector, sizeof(uint), $"{fieldLabel} length");
        var countValue = ReadUInt32(vector);
        if (countValue > maximumCount)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} exceeds its bounded record count.");
        }

        var count = checked((int)countValue);
        var firstElement = checked(vector + sizeof(uint));
        EnsureRange(
            firstElement,
            checked(count * sizeof(uint)),
            $"{fieldLabel} elements");

        var tables = new int[count];
        for (var index = 0; index < count; index++)
        {
            var element = checked(firstElement + (index * sizeof(uint)));
            tables[index] = ResolveForwardOffset(
                element,
                ReadUInt32(element),
                $"{fieldLabel} record {index}");
        }

        return tables;
    }

    private int? ReadFieldAddress(
        int table,
        int maximumFieldCount,
        int field,
        int fieldWidth,
        string fieldLabel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(field);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fieldWidth);
        var info = ValidateTable(table, maximumFieldCount, fieldLabel);
        if (field >= info.FieldCount)
        {
            return null;
        }

        var vtableField = checked(info.Vtable + VtableHeaderSize + (field * sizeof(ushort)));
        var fieldOffset = ReadUInt16(vtableField);
        if (fieldOffset == 0)
        {
            return null;
        }

        if (fieldOffset < sizeof(int)
            || checked((int)fieldOffset + fieldWidth) > info.ObjectLength)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} points outside its FlatBuffer table.");
        }

        var fieldAddress = checked(table + fieldOffset);
        EnsureRange(fieldAddress, fieldWidth, fieldLabel);
        return fieldAddress;
    }

    private TableInfo ValidateTable(
        int table,
        int maximumFieldCount,
        string tableLabel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFieldCount);
        EnsureRange(table, sizeof(int), tableLabel);
        var vtableDistance = ReadInt32(table);
        if (vtableDistance == 0)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {tableLabel} has an invalid vtable offset.");
        }

        var vtablePosition = checked((long)table - vtableDistance);
        if (vtablePosition < 0 || vtablePosition > int.MaxValue)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {tableLabel} has an out-of-range vtable offset.");
        }

        var vtable = (int)vtablePosition;
        if ((vtable & 1) != 0)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {tableLabel} has a misaligned vtable.");
        }

        EnsureRange(vtable, VtableHeaderSize, $"{tableLabel} vtable header");
        var vtableLength = ReadUInt16(vtable);
        var objectLength = ReadUInt16(checked(vtable + sizeof(ushort)));
        if (vtableLength < VtableHeaderSize
            || (vtableLength & 1) != 0
            || objectLength < sizeof(int)
            || objectLength > MaximumTableObjectByteLength)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {tableLabel} has an invalid FlatBuffer table header.");
        }

        var fieldCount = (vtableLength - VtableHeaderSize) / sizeof(ushort);
        if (fieldCount > maximumFieldCount)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {tableLabel} exceeds its established vtable field ceiling.");
        }

        EnsureRange(vtable, vtableLength, $"{tableLabel} vtable");
        EnsureRange(table, objectLength, $"{tableLabel} object");
        return new TableInfo(vtable, objectLength, fieldCount);
    }

    private int ResolveForwardOffset(int origin, uint relativeOffset, string fieldLabel)
    {
        if (relativeOffset == 0 || relativeOffset > int.MaxValue)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} has an invalid forward offset.");
        }

        var target = checked(origin + (int)relativeOffset);
        if (target <= origin)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} does not point forward.");
        }

        EnsureRange(target, 1, fieldLabel);
        return target;
    }

    private short ReadInt16(int offset)
    {
        EnsureRange(offset, sizeof(short), "16-bit integer");
        return BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, sizeof(short)));
    }

    private ushort ReadUInt16(int offset)
    {
        EnsureRange(offset, sizeof(ushort), "16-bit unsigned integer");
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
    }

    private int ReadInt32(int offset)
    {
        EnsureRange(offset, sizeof(int), "32-bit integer");
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));
    }

    private uint ReadUInt32(int offset)
    {
        EnsureRange(offset, sizeof(uint), "32-bit unsigned integer");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
    }

    private float ReadSingle(int offset)
    {
        EnsureRange(offset, sizeof(float), "32-bit floating-point value");
        return BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, sizeof(float)));
    }

    private void EnsureRange(int offset, int length, string fieldLabel)
    {
        if (offset < 0
            || length < 0
            || offset > data.Length
            || length > data.Length - offset)
        {
            throw new InvalidDataException(
                $"{payloadLabel} {fieldLabel} points outside the FlatBuffer payload.");
        }
    }

    private sealed record TableInfo(int Vtable, int ObjectLength, int FieldCount);
}
