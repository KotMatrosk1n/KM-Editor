// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using Google.FlatBuffers;

namespace KM.Formats.SwSh;

public sealed record SwShScriptRecord(
    ulong ScriptId,
    string AmxPath,
    string TextPath);

public sealed class SwShScriptRecordTable
{
    private const int DefaultMaximumRecordCount = 100_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly SwShScriptRecord[] records;
    private readonly ReadOnlyCollection<SwShScriptRecord> readOnlyRecords;
    private readonly byte[]? originalBytes;

    private SwShScriptRecordTable(
        IReadOnlyList<SwShScriptRecord> records,
        byte[]? originalBytes)
    {
        this.records = records.ToArray();
        readOnlyRecords = Array.AsReadOnly(this.records);
        this.originalBytes = originalBytes;
    }

    public IReadOnlyList<SwShScriptRecord> Records => readOnlyRecords;

    public static SwShScriptRecordTable Parse(
        ReadOnlySpan<byte> data,
        int maximumRecordCount = DefaultMaximumRecordCount)
    {
        if (maximumRecordCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRecordCount));
        }

        var bytes = data.ToArray();
        EnsureRange(bytes, 0, sizeof(uint), "root offset");
        var root = ResolveForwardOffset(bytes, 0, ReadUInt32(bytes, 0, "root offset"), "root table");
        ValidateTableShape(bytes, root, maximumFieldCount: 1, "root table");
        var vectorReference = ResolveTableField(bytes, root, fieldIndex: 0, required: true, "script vector");
        var vector = ResolveForwardOffset(
            bytes,
            vectorReference,
            ReadUInt32(bytes, vectorReference, "script vector offset"),
            "script vector");
        var recordCountValue = ReadUInt32(bytes, vector, "script record count");
        if (recordCountValue > maximumRecordCount || recordCountValue > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Sword/Shield script record count {recordCountValue} exceeds the bounded semantic limit {maximumRecordCount}.");
        }

        var recordCount = (int)recordCountValue;
        EnsureRange(bytes, vector + sizeof(uint), checked(recordCount * sizeof(uint)), "script record vector");
        var records = new SwShScriptRecord[recordCount];
        var ids = new HashSet<ulong>();
        for (var index = 0; index < recordCount; index++)
        {
            var elementReference = checked(vector + sizeof(uint) + (index * sizeof(uint)));
            var table = ResolveForwardOffset(
                bytes,
                elementReference,
                ReadUInt32(bytes, elementReference, $"script record {index} offset"),
                $"script record {index}");
            ValidateTableShape(bytes, table, maximumFieldCount: 3, $"script record {index}");

            var idField = ResolveTableField(bytes, table, fieldIndex: 0, required: true, $"script record {index} id");
            EnsureRange(bytes, idField, sizeof(ulong), $"script record {index} id");
            var scriptId = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(idField, sizeof(ulong)));
            if (!ids.Add(scriptId))
            {
                throw new InvalidDataException(
                    $"Sword/Shield script record table contains duplicate id 0x{scriptId:X16}.");
            }

            var amxPath = ReadRequiredString(bytes, table, 1, $"script record {index} AMX path");
            var textPath = ReadRequiredString(bytes, table, 2, $"script record {index} text path");
            ValidateRelativePath(amxPath, $"script record {index} AMX path");
            ValidateRelativePath(textPath, $"script record {index} text path");
            records[index] = new SwShScriptRecord(scriptId, amxPath, textPath);
        }

        return new SwShScriptRecordTable(records, bytes);
    }

    public SwShScriptRecordTable Append(
        SwShScriptRecord record,
        bool requireUniqueAmxPath = true)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record, "appended script record");
        if (records.Any(existing => existing.ScriptId == record.ScriptId))
        {
            throw new InvalidDataException(
                $"Sword/Shield script id 0x{record.ScriptId:X16} is already present.");
        }

        if (requireUniqueAmxPath
            && records.Any(existing => string.Equals(existing.AmxPath, record.AmxPath, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX path '{record.AmxPath}' is already assigned to another script id.");
        }

        return new SwShScriptRecordTable(records.Append(record).ToArray(), null);
    }

    public byte[] ToByteArray()
    {
        if (originalBytes is not null)
        {
            return originalBytes.ToArray();
        }

        var builder = new FlatBufferBuilder(Math.Max(1024, checked(records.Length * 64)));
        var tableOffsets = new int[records.Length];
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            ValidateRecord(record, $"script record {index}");
            var amxPath = builder.CreateString(record.AmxPath);
            var textPath = builder.CreateString(record.TextPath);
            builder.StartTable(3);
            builder.AddOffset(2, textPath.Value, 0);
            builder.AddOffset(1, amxPath.Value, 0);
            builder.AddUlong(0, record.ScriptId, 0);
            tableOffsets[index] = builder.EndTable();
        }

        builder.StartVector(sizeof(int), tableOffsets.Length, sizeof(int));
        for (var index = tableOffsets.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(tableOffsets[index]);
        }

        var vector = builder.EndVector();
        builder.StartTable(1);
        builder.AddOffset(0, vector.Value, 0);
        var root = builder.EndTable();
        builder.Finish(root);
        var bytes = builder.SizedByteArray();

        var verification = Parse(bytes, Math.Max(DefaultMaximumRecordCount, records.Length));
        if (!verification.Records.SequenceEqual(records))
        {
            throw new InvalidDataException("Sword/Shield script record serialization failed its semantic round-trip check.");
        }

        return bytes;
    }

    public static byte[] AppendRecord(
        ReadOnlySpan<byte> data,
        SwShScriptRecord record,
        bool requireUniqueAmxPath = true,
        int maximumRecordCount = DefaultMaximumRecordCount)
    {
        var table = Parse(data, maximumRecordCount);
        if (table.Records.Count >= maximumRecordCount)
        {
            throw new InvalidDataException(
                $"Sword/Shield script record table reached its bounded record limit {maximumRecordCount}.");
        }

        return table.Append(record, requireUniqueAmxPath).ToByteArray();
    }

    private static string ReadRequiredString(
        byte[] bytes,
        int table,
        int fieldIndex,
        string label)
    {
        var field = ResolveTableField(bytes, table, fieldIndex, required: true, label);
        var stringOffset = ReadUInt32(bytes, field, $"{label} offset");
        var value = ResolveForwardOffset(bytes, field, stringOffset, label);
        var lengthValue = ReadUInt32(bytes, value, $"{label} length");
        if (lengthValue > int.MaxValue)
        {
            throw new InvalidDataException($"Sword/Shield {label} length {lengthValue} is invalid.");
        }

        var length = (int)lengthValue;
        var content = checked(value + sizeof(uint));
        EnsureRange(bytes, content, checked(length + 1), label);
        if (bytes[content + length] != 0)
        {
            throw new InvalidDataException($"Sword/Shield {label} is not null-terminated.");
        }

        try
        {
            return StrictUtf8.GetString(bytes, content, length);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"Sword/Shield {label} contains invalid UTF-8.", exception);
        }
    }

    private static void ValidateRecord(SwShScriptRecord record, string label)
    {
        if (record.ScriptId == 0)
        {
            throw new InvalidDataException($"Sword/Shield {label} uses the reserved zero script id.");
        }

        ValidateRelativePath(record.AmxPath, $"{label} AMX path");
        ValidateRelativePath(record.TextPath, $"{label} text path");
    }

    private static void ValidateRelativePath(string path, string label)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0
            || path[0] == '/'
            || path[^1] == '/'
            || path.Contains('\\')
            || path.Any(char.IsControl))
        {
            throw new InvalidDataException($"Sword/Shield {label} '{path}' is not a canonical relative RomFS path.");
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException($"Sword/Shield {label} '{path}' contains an unsafe path segment.");
        }
    }

    private static void ValidateTableShape(
        byte[] bytes,
        int table,
        int maximumFieldCount,
        string label)
    {
        EnsureRange(bytes, table, sizeof(int), label);
        var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(table, sizeof(int)));
        var vtableValue = (long)table - vtableDistance;
        if (vtableDistance == 0 || vtableValue < 0 || vtableValue > int.MaxValue)
        {
            throw new InvalidDataException($"Sword/Shield {label} has invalid vtable distance {vtableDistance}.");
        }

        var vtable = (int)vtableValue;
        EnsureRange(bytes, vtable, sizeof(ushort) * 2, $"{label} vtable");
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable, sizeof(ushort)));
        var objectLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable + sizeof(ushort), sizeof(ushort)));
        var maximumVtableLength = checked((sizeof(ushort) * 2) + (maximumFieldCount * sizeof(ushort)));
        if (vtableLength < sizeof(ushort) * 2
            || (vtableLength & 1) != 0
            || vtableLength > maximumVtableLength
            || objectLength < sizeof(int))
        {
            throw new InvalidDataException($"Sword/Shield {label} has an unsupported FlatBuffer table shape.");
        }

        EnsureRange(bytes, vtable, vtableLength, $"{label} vtable");
        EnsureRange(bytes, table, objectLength, label);
    }

    private static int ResolveTableField(
        byte[] bytes,
        int table,
        int fieldIndex,
        bool required,
        string label)
    {
        var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(table, sizeof(int)));
        var vtableValue = (long)table - vtableDistance;
        if (vtableDistance == 0 || vtableValue < 0 || vtableValue > int.MaxValue)
        {
            throw new InvalidDataException("Sword/Shield table has an invalid signed vtable offset.");
        }
        var vtable = (int)vtableValue;
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable, sizeof(ushort)));
        var fieldEntry = checked(vtable + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)));
        if (fieldEntry + sizeof(ushort) > vtable + vtableLength)
        {
            if (required)
            {
                throw new InvalidDataException($"Sword/Shield table is missing required {label}.");
            }

            return 0;
        }

        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(fieldEntry, sizeof(ushort)));
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Sword/Shield table is missing required {label}.");
            }

            return 0;
        }

        var field = checked(table + fieldOffset);
        EnsureRange(bytes, field, sizeof(uint), label);
        return field;
    }

    private static int ResolveForwardOffset(
        byte[] bytes,
        int origin,
        uint offset,
        string label)
    {
        if (offset == 0)
        {
            throw new InvalidDataException($"Sword/Shield {label} has a zero forward offset.");
        }

        var resolved = (long)origin + offset;
        if (resolved > int.MaxValue)
        {
            throw new InvalidDataException($"Sword/Shield {label} offset 0x{resolved:X} is invalid.");
        }

        EnsureRange(bytes, (int)resolved, sizeof(uint), label);
        return (int)resolved;
    }

    private static uint ReadUInt32(byte[] bytes, int offset, string label)
    {
        EnsureRange(bytes, offset, sizeof(uint), label);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
    }

    private static void EnsureRange(byte[] bytes, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            var end = (long)offset + length;
            throw new InvalidDataException(
                $"Sword/Shield {label} range 0x{offset:X}..0x{end:X} exceeds file length 0x{bytes.Length:X}.");
        }
    }
}
