// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.ZA.Data;

internal sealed record ZaBossBattleConsumerRecord(
    int SourceIndex,
    string? SupportSpawnerGroupId,
    string? MainSpawnerId,
    string? EventId,
    string? BattleId);

internal static class ZaBossBattleConsumerTable
{
    // These field indexes are from the boss_btl_data_global FlatBuffer used by the game.
    // Only the established spawner-consumer relationships are decoded here. Unknown
    // fields remain untouched and are not needed by the encounter browser.
    private const int RootRecordsField = 0;
    private const int SupportSpawnerGroupField = 0;
    private const int MainSpawnerField = 1;
    private const int EventField = 2;
    private const int BattleIdField = 13;
    private const int MaximumRecordCount = 16_384;
    private const int MaximumStringByteLength = 65_536;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<ZaBossBattleConsumerRecord> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var reader = new FlatBufferReader(data);
            var rootTable = reader.ReadRootTable();
            var recordsVector = reader.ReadRequiredOffsetField(
                rootTable,
                RootRecordsField,
                "boss battle record vector");
            var recordTables = reader.ReadTableVector(
                recordsVector,
                MaximumRecordCount,
                "boss battle record vector");
            if (recordTables.Count == 0)
            {
                throw new InvalidDataException("Boss battle consumer data contains no records.");
            }

            var records = new ZaBossBattleConsumerRecord[recordTables.Count];
            for (var index = 0; index < recordTables.Count; index++)
            {
                var recordTable = recordTables[index];
                var supportSpawnerGroupId = NormalizeId(reader.ReadOptionalStringField(
                    recordTable,
                    SupportSpawnerGroupField,
                    MaximumStringByteLength));
                var mainSpawnerId = NormalizeId(reader.ReadOptionalStringField(
                    recordTable,
                    MainSpawnerField,
                    MaximumStringByteLength));
                if (supportSpawnerGroupId is null && mainSpawnerId is null)
                {
                    throw new InvalidDataException(
                        $"Boss battle consumer record {index} has no spawner references.");
                }

                records[index] = new ZaBossBattleConsumerRecord(
                    index,
                    supportSpawnerGroupId,
                    mainSpawnerId,
                    NormalizeId(reader.ReadOptionalStringField(
                        recordTable,
                        EventField,
                        MaximumStringByteLength)),
                    NormalizeId(reader.ReadOptionalStringField(
                        recordTable,
                        BattleIdField,
                        MaximumStringByteLength)));
            }

            if (!records.Any(record =>
                record.SupportSpawnerGroupId?.StartsWith(
                    "spn_boss_",
                    StringComparison.OrdinalIgnoreCase) == true
                && record.MainSpawnerId?.StartsWith(
                    "btl_spn_boss_",
                    StringComparison.OrdinalIgnoreCase) == true))
            {
                throw new InvalidDataException(
                    "Boss battle consumer data contains no recognizable boss spawner relationships.");
            }

            return records;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Boss battle consumer data contains an overflowing FlatBuffer offset.",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Boss battle consumer data contains invalid UTF-8 text.",
                exception);
        }
    }

    private static string? NormalizeId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class FlatBufferReader
    {
        private readonly byte[] data;

        public FlatBufferReader(byte[] data)
        {
            this.data = data;
        }

        public int ReadRootTable()
        {
            EnsureRange(0, sizeof(uint), "FlatBuffer root offset");
            var relativeOffset = ReadUInt32(0);
            return ResolveForwardOffset(0, relativeOffset, "FlatBuffer root table");
        }

        public int ReadRequiredOffsetField(int table, int field, string description)
        {
            var fieldAddress = ReadFieldAddress(table, field, sizeof(uint));
            if (fieldAddress is null)
            {
                throw new InvalidDataException($"Boss battle consumer data is missing its {description}.");
            }

            return ResolveForwardOffset(
                fieldAddress.Value,
                ReadUInt32(fieldAddress.Value),
                description);
        }

        public string? ReadOptionalStringField(int table, int field, int maximumByteLength)
        {
            var fieldAddress = ReadFieldAddress(table, field, sizeof(uint));
            if (fieldAddress is null)
            {
                return null;
            }

            var stringAddress = ResolveForwardOffset(
                fieldAddress.Value,
                ReadUInt32(fieldAddress.Value),
                $"string field {field}");
            EnsureRange(stringAddress, sizeof(uint), $"string field {field} length");
            var byteLength = ReadUInt32(stringAddress);
            if (byteLength > maximumByteLength)
            {
                throw new InvalidDataException(
                    $"Boss battle consumer string field {field} exceeds the supported length.");
            }

            var contentAddress = checked(stringAddress + sizeof(uint));
            var contentLength = checked((int)byteLength);
            EnsureRange(
                contentAddress,
                checked(contentLength + 1),
                $"string field {field} contents");
            if (data[checked(contentAddress + contentLength)] != 0)
            {
                throw new InvalidDataException(
                    $"Boss battle consumer string field {field} has no FlatBuffer terminator.");
            }

            return StrictUtf8.GetString(data, contentAddress, contentLength);
        }

        public IReadOnlyList<int> ReadTableVector(
            int vectorAddress,
            int maximumCount,
            string description)
        {
            EnsureRange(vectorAddress, sizeof(uint), $"{description} length");
            var countValue = ReadUInt32(vectorAddress);
            if (countValue > maximumCount)
            {
                throw new InvalidDataException(
                    $"Boss battle consumer data declares too many records ({countValue}).");
            }

            var count = checked((int)countValue);
            var firstElement = checked(vectorAddress + sizeof(uint));
            EnsureRange(
                firstElement,
                checked(count * sizeof(uint)),
                $"{description} elements");

            var tables = new int[count];
            for (var index = 0; index < count; index++)
            {
                var elementAddress = checked(firstElement + (index * sizeof(uint)));
                tables[index] = ResolveForwardOffset(
                    elementAddress,
                    ReadUInt32(elementAddress),
                    $"{description} record {index}");
                ValidateTable(tables[index]);
            }

            return tables;
        }

        private int? ReadFieldAddress(int table, int field, int fieldWidth)
        {
            var (vtable, vtableLength, objectLength) = ValidateTable(table);
            var vtableFieldAddress = checked(vtable + 4 + (field * sizeof(ushort)));
            if (checked(vtableFieldAddress + sizeof(ushort)) > checked(vtable + vtableLength))
            {
                return null;
            }

            var fieldOffset = ReadUInt16(vtableFieldAddress);
            if (fieldOffset == 0)
            {
                return null;
            }

            if (fieldOffset < sizeof(int)
                || checked((int)fieldOffset + fieldWidth) > objectLength)
            {
                throw new InvalidDataException(
                    $"Boss battle consumer field {field} points outside its FlatBuffer table.");
            }

            var fieldAddress = checked(table + fieldOffset);
            EnsureRange(fieldAddress, fieldWidth, $"field {field}");
            return fieldAddress;
        }

        private (int Vtable, int VtableLength, int ObjectLength) ValidateTable(int table)
        {
            EnsureRange(table, sizeof(int), "FlatBuffer table");
            var vtableDistance = ReadInt32(table);
            if (vtableDistance == 0)
            {
                throw new InvalidDataException(
                    "Boss battle consumer data contains an invalid FlatBuffer vtable offset.");
            }

            var vtable = checked(table - vtableDistance);
            EnsureRange(vtable, 4, "FlatBuffer vtable header");
            var vtableLength = ReadUInt16(vtable);
            var objectLength = ReadUInt16(checked(vtable + sizeof(ushort)));
            if (vtableLength < 4
                || (vtableLength & 1) != 0
                || objectLength < sizeof(int))
            {
                throw new InvalidDataException(
                    "Boss battle consumer data contains an invalid FlatBuffer table header.");
            }

            EnsureRange(vtable, vtableLength, "FlatBuffer vtable");
            EnsureRange(table, objectLength, "FlatBuffer table object");
            return (vtable, vtableLength, objectLength);
        }

        private int ResolveForwardOffset(int origin, uint relativeOffset, string description)
        {
            if (relativeOffset == 0)
            {
                throw new InvalidDataException(
                    $"Boss battle consumer data contains a null {description} offset.");
            }

            var target = checked(origin + checked((int)relativeOffset));
            EnsureRange(target, 1, description);
            return target;
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

        private ushort ReadUInt16(int offset)
        {
            EnsureRange(offset, sizeof(ushort), "16-bit unsigned integer");
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
        }

        private void EnsureRange(int offset, int length, string description)
        {
            if (offset < 0
                || length < 0
                || offset > data.Length
                || length > data.Length - offset)
            {
                throw new InvalidDataException(
                    $"Boss battle consumer {description} points outside the FlatBuffer payload.");
            }
        }
    }
}
