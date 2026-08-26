// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.FlatBuffers;

namespace KM.ZA.FashionCatalog;

internal sealed class ZaFashionLineupCatalogDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly byte[] bytes;

    private ZaFashionLineupCatalogDocument(
        byte[] bytes,
        IReadOnlyList<ZaFashionLineupData> lineups,
        IReadOnlyList<ZaFashionLineupDataRow> rows)
    {
        this.bytes = bytes;
        Lineups = lineups;
        Rows = rows;
    }

    public IReadOnlyList<ZaFashionLineupData> Lineups { get; }

    public IReadOnlyList<ZaFashionLineupDataRow> Rows { get; }

    public static ZaFashionLineupCatalogDocument Parse(
        byte[] bytes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> shopsByLineup,
        string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(shopsByLineup);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        try
        {
            var root = ReadRootTable(bytes, label);
            ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(bytes, root, 1, $"{label} root");
            var lineupTables = ReadTableVector(
                bytes,
                RequireField(bytes, root, 0, $"{label} values"),
                $"{label} values");
            var lineups = new List<ZaFashionLineupData>(lineupTables.Count);
            var rows = new List<ZaFashionLineupDataRow>();
            for (var lineupIndex = 0; lineupIndex < lineupTables.Count; lineupIndex++)
            {
                var lineupTable = lineupTables[lineupIndex];
                ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                    bytes,
                    lineupTable,
                    2,
                    $"{label} lineup {lineupIndex}");
                var lineupId = ReadRequiredString(
                    bytes,
                    RequireField(bytes, lineupTable, 0, $"{label} lineup ID"),
                    $"{label} lineup ID");
                var entryTables = ReadTableVector(
                    bytes,
                    RequireField(bytes, lineupTable, 1, $"{label} lineup entries"),
                    $"{label} lineup entries");
                shopsByLineup.TryGetValue(lineupId, out var linkedShops);
                var shopIds = linkedShops?.ToArray() ?? Array.Empty<string>();
                var entries = new List<ZaFashionLineupEntryData>(entryTables.Count);
                for (var entryIndex = 0; entryIndex < entryTables.Count; entryIndex++)
                {
                    var entryTable = entryTables[entryIndex];
                    ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                        bytes,
                        entryTable,
                        2,
                        $"{label} lineup entry {lineupIndex}:{entryIndex}");
                    var itemPosition = RequireField(
                        bytes,
                        entryTable,
                        0,
                        $"{label} lineup entry item ID");
                    var itemId = ReadUInt32(bytes, itemPosition, $"{label} lineup entry item ID");
                    var conditions = ReadConditionGroups(
                        bytes,
                        RequireField(bytes, entryTable, 1, $"{label} lineup entry conditions"),
                        $"{label} lineup entry {lineupIndex}:{entryIndex} conditions");
                    entries.Add(new ZaFashionLineupEntryData(
                        entryIndex,
                        itemId,
                        itemPosition,
                        conditions));
                    rows.Add(new ZaFashionLineupDataRow(
                        rows.Count,
                        lineupIndex,
                        entryIndex,
                        lineupId,
                        shopIds,
                        itemId,
                        itemPosition));
                }

                lineups.Add(new ZaFashionLineupData(lineupIndex, lineupId, entries));
            }

            ZaFashionCatalogFlatBufferSupport.EnsureCount(rows.Count, $"{label} entries");
            return new ZaFashionLineupCatalogDocument(bytes.ToArray(), lineups, rows);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            DecoderFallbackException or
            IndexOutOfRangeException or
            OverflowException)
        {
            throw new InvalidDataException($"{label} is not a supported Fashion Catalog FlatBuffer.", exception);
        }
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var lineupOffsets = new int[Lineups.Count];
        for (var lineupIndex = 0; lineupIndex < Lineups.Count; lineupIndex++)
        {
            var lineup = Lineups[lineupIndex];
            if (lineup.PhysicalIndex != lineupIndex)
            {
                throw new InvalidDataException(
                    "A Fashion Catalog lineup physical identity does not match its vector position.");
            }

            var entryOffsets = new int[lineup.Entries.Count];
            for (var entryIndex = 0; entryIndex < lineup.Entries.Count; entryIndex++)
            {
                var entry = lineup.Entries[entryIndex];
                if (entry.PhysicalIndex != entryIndex)
                {
                    throw new InvalidDataException(
                        "A Fashion Catalog lineup-entry identity does not match its vector position.");
                }

                var groups = WriteConditionGroups(builder, entry.ConditionGroups);
                builder.StartTable(2);
                builder.AddOffset(1, groups.Value, 0);
                ZaFashionCatalogFlatBufferSupport.AddUInt(
                    builder,
                    0,
                    entry.ItemId,
                    present: true);
                entryOffsets[entryIndex] = builder.EndTable();
            }

            var entries = ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, entryOffsets);
            var lineupId = builder.CreateString(
                ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    lineup.LineupId,
                    $"Fashion Catalog lineup {lineupIndex} ID"));
            builder.StartTable(2);
            builder.AddOffset(1, entries.Value, 0);
            builder.AddOffset(0, lineupId.Value, 0);
            lineupOffsets[lineupIndex] = builder.EndTable();
        }

        var lineups = ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, lineupOffsets);
        builder.StartTable(1);
        builder.AddOffset(0, lineups.Value, 0);
        builder.Finish(builder.EndTable());
        return builder.SizedByteArray();
    }

    public string CreateStructureRevision() =>
        CreateRevision(
            "KM.ZA.FashionCatalog.LineupStructure.v1",
            includeIdentity: true,
            includeConditions: true);

    public string CreateIdentityRevision() =>
        CreateRevision(
            "KM.ZA.FashionCatalog.LineupIdentity.v1",
            includeIdentity: true,
            includeConditions: false);

    public string CreateActivationConditionRevision() =>
        CreateRevision(
            "KM.ZA.FashionCatalog.LineupConditions.v1",
            includeIdentity: false,
            includeConditions: true);

    public byte[] ReplaceItem(int physicalIndex, uint itemId)
    {
        if ((uint)physicalIndex >= (uint)Rows.Count)
        {
            throw new InvalidDataException("The Fashion Catalog lineup entry index is outside the source table.");
        }

        var row = Rows[physicalIndex];
        var updated = bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            updated.AsSpan(row.ItemValuePosition, sizeof(uint)),
            itemId);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index >= row.ItemValuePosition && index < row.ItemValuePosition + sizeof(uint))
            {
                continue;
            }

            if (bytes[index] != updated[index])
            {
                throw new InvalidDataException(
                    "Fashion Catalog lineup serialization changed bytes outside the selected item ID.");
            }
        }

        return updated;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadShopRelationships(
        byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        const string label = "Fashion shop index";
        try
        {
            var root = ReadRootTable(bytes, label);
            ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(bytes, root, 1, $"{label} root");
            var shopTables = ReadTableVector(
                bytes,
                RequireField(bytes, root, 0, "Fashion shop rows"),
                "Fashion shop rows");
            var relationships = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var index = 0; index < shopTables.Count; index++)
            {
                var table = shopTables[index];
                ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(bytes, table, 6, $"Fashion shop {index}");
                var shopId = ReadRequiredString(
                    bytes,
                    RequireField(bytes, table, 0, "Fashion shop ID"),
                    "Fashion shop ID");
                var lineupId = ReadRequiredString(
                    bytes,
                    RequireField(bytes, table, 1, "Fashion shop lineup ID"),
                    "Fashion shop lineup ID");
                ValidateOptionalStringField(bytes, table, 2, "Fashion shop resource label");
                ValidateOptionalStringField(bytes, table, 3, "Fashion shop message label");
                ValidateOptionalScalarField(bytes, table, 4, "Fashion shop kind");
                ValidateOptionalScalarField(bytes, table, 5, "Fashion shop condition");
                if (!relationships.TryGetValue(lineupId, out var shops))
                {
                    shops = [];
                    relationships.Add(lineupId, shops);
                }

                shops.Add(shopId);
            }

            return relationships.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ToArray(),
                StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            DecoderFallbackException or
            IndexOutOfRangeException or
            OverflowException)
        {
            throw new InvalidDataException(
                "The Fashion shop index is not a supported FlatBuffer.",
                exception);
        }
    }

    private string CreateRevision(
        string domain,
        bool includeIdentity,
        bool includeConditions)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ZaFashionCatalogFlatBufferSupport.Append(hash, domain);
        ZaFashionCatalogFlatBufferSupport.Append(hash, Lineups.Count);
        foreach (var lineup in Lineups)
        {
            ZaFashionCatalogFlatBufferSupport.Append(hash, lineup.PhysicalIndex);
            ZaFashionCatalogFlatBufferSupport.Append(hash, lineup.LineupId);
            ZaFashionCatalogFlatBufferSupport.Append(hash, lineup.Entries.Count);
            foreach (var entry in lineup.Entries)
            {
                ZaFashionCatalogFlatBufferSupport.Append(hash, entry.PhysicalIndex);
                if (includeIdentity)
                {
                    ZaFashionCatalogFlatBufferSupport.Append(hash, entry.ItemId);
                }

                if (includeConditions)
                {
                    AppendConditionGroups(hash, entry.ConditionGroups);
                }
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendConditionGroups(
        IncrementalHash hash,
        IReadOnlyList<ZaFashionLineupConditionGroupData> groups)
    {
        ZaFashionCatalogFlatBufferSupport.Append(hash, groups.Count);
        foreach (var group in groups)
        {
            ZaFashionCatalogFlatBufferSupport.Append(hash, group.PhysicalIndex);
            ZaFashionCatalogFlatBufferSupport.Append(hash, group.Holders.Count);
            foreach (var holder in group.Holders)
            {
                ZaFashionCatalogFlatBufferSupport.Append(hash, holder.PhysicalIndex);
                ZaFashionCatalogFlatBufferSupport.Append(hash, holder.Conditions.Count);
                foreach (var condition in holder.Conditions)
                {
                    ZaFashionCatalogFlatBufferSupport.Append(hash, condition.PhysicalIndex);
                    ZaFashionCatalogFlatBufferSupport.Append(hash, condition.HasCondition);
                    ZaFashionCatalogFlatBufferSupport.Append(hash, condition.Condition);
                    ZaFashionCatalogFlatBufferSupport.Append(hash, condition.HasComparison);
                    ZaFashionCatalogFlatBufferSupport.Append(hash, condition.Comparison);
                    ZaFashionCatalogFlatBufferSupport.Append(hash, condition.HasArguments);
                    ZaFashionCatalogFlatBufferSupport.Append(hash, condition.Arguments.Count);
                    foreach (var argument in condition.Arguments)
                    {
                        ZaFashionCatalogFlatBufferSupport.Append(hash, argument);
                    }
                }
            }
        }
    }

    private static VectorOffset WriteConditionGroups(
        FlatBufferBuilder builder,
        IReadOnlyList<ZaFashionLineupConditionGroupData> groups)
    {
        var groupOffsets = new int[groups.Count];
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            if (group.PhysicalIndex != groupIndex)
            {
                throw new InvalidDataException(
                    "A Fashion Catalog activation-condition group identity does not match its vector position.");
            }

            var holderOffsets = new int[group.Holders.Count];
            for (var holderIndex = 0; holderIndex < group.Holders.Count; holderIndex++)
            {
                var holder = group.Holders[holderIndex];
                if (holder.PhysicalIndex != holderIndex)
                {
                    throw new InvalidDataException(
                        "A Fashion Catalog activation-condition holder identity does not match its vector position.");
                }

                var conditionOffsets = new int[holder.Conditions.Count];
                for (var conditionIndex = 0; conditionIndex < holder.Conditions.Count; conditionIndex++)
                {
                    var condition = holder.Conditions[conditionIndex];
                    if (condition.PhysicalIndex != conditionIndex)
                    {
                        throw new InvalidDataException(
                            "A Fashion Catalog activation-condition identity does not match its vector position.");
                    }

                    var conditionName = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
                        builder,
                        condition.HasCondition,
                        condition.Condition,
                        "Fashion Catalog activation-condition name");
                    var argumentOffsets = Array.Empty<int>();
                    if (condition.HasArguments)
                    {
                        argumentOffsets = new int[condition.Arguments.Count];
                        for (var argumentIndex = 0; argumentIndex < condition.Arguments.Count; argumentIndex++)
                        {
                            argumentOffsets[argumentIndex] = builder.CreateString(
                                ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                                    condition.Arguments[argumentIndex],
                                    "Fashion Catalog activation-condition argument")).Value;
                        }
                    }

                    var arguments = condition.HasArguments
                        ? ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, argumentOffsets)
                        : default;
                    builder.StartTable(3);
                    if (condition.HasArguments)
                    {
                        builder.AddOffset(2, arguments.Value, 0);
                    }

                    ZaFashionCatalogFlatBufferSupport.AddUInt(
                        builder,
                        1,
                        condition.Comparison,
                        condition.HasComparison);
                    if (condition.HasCondition)
                    {
                        builder.AddOffset(0, conditionName.Value, 0);
                    }

                    conditionOffsets[conditionIndex] = builder.EndTable();
                }

                var conditions = ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(
                    builder,
                    conditionOffsets);
                builder.StartTable(1);
                builder.AddOffset(0, conditions.Value, 0);
                holderOffsets[holderIndex] = builder.EndTable();
            }

            var holders = ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, holderOffsets);
            builder.StartTable(1);
            builder.AddOffset(0, holders.Value, 0);
            groupOffsets[groupIndex] = builder.EndTable();
        }

        return ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, groupOffsets);
    }

    private static IReadOnlyList<ZaFashionLineupConditionGroupData> ReadConditionGroups(
        byte[] bytes,
        int vectorField,
        string label)
    {
        var groups = ReadTableVector(bytes, vectorField, label);
        var result = new List<ZaFashionLineupConditionGroupData>(groups.Count);
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(bytes, group, 1, $"{label} group {groupIndex}");
            var holders = ReadTableVector(
                bytes,
                RequireField(bytes, group, 0, $"{label} holders"),
                $"{label} holders");
            var holderData = new List<ZaFashionLineupConditionHolderData>(holders.Count);
            for (var holderIndex = 0; holderIndex < holders.Count; holderIndex++)
            {
                var holder = holders[holderIndex];
                ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                    bytes,
                    holder,
                    1,
                    $"{label} holder {holderIndex}");
                var conditions = ReadTableVector(
                    bytes,
                    RequireField(bytes, holder, 0, $"{label} condition values"),
                    $"{label} condition values");
                var conditionData = new List<ZaFashionLineupConditionData>(conditions.Count);
                for (var conditionIndex = 0; conditionIndex < conditions.Count; conditionIndex++)
                {
                    var condition = conditions[conditionIndex];
                    ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                        bytes,
                        condition,
                        3,
                        $"{label} condition {conditionIndex}");
                    var nameField = GetField(bytes, condition, 0, $"{label} condition name");
                    var comparisonField = GetField(bytes, condition, 1, $"{label} comparison");
                    var argumentField = GetField(bytes, condition, 2, $"{label} arguments");
                    conditionData.Add(new ZaFashionLineupConditionData(
                        conditionIndex,
                        nameField is not null,
                        nameField is null
                            ? null
                            : ReadRequiredString(bytes, nameField.Value, $"{label} condition name"),
                        comparisonField is not null,
                        comparisonField is null
                            ? 0
                            : ReadUInt32(bytes, comparisonField.Value, $"{label} comparison"),
                        argumentField is not null,
                        argumentField is null
                            ? Array.Empty<string>()
                            : ReadStringVector(bytes, argumentField.Value, $"{label} arguments")));
                }

                holderData.Add(new ZaFashionLineupConditionHolderData(holderIndex, conditionData));
            }

            result.Add(new ZaFashionLineupConditionGroupData(groupIndex, holderData));
        }

        return result;
    }

    private static int ReadRootTable(byte[] bytes, string label)
    {
        if (bytes.Length < sizeof(uint))
        {
            throw new InvalidDataException($"{label} is missing its root offset.");
        }

        return CheckedTarget(0, ReadUInt32(bytes, 0, $"{label} root offset"), bytes.Length, label);
    }

    private static IReadOnlyList<int> ReadTableVector(byte[] bytes, int fieldPosition, string label)
    {
        var vector = ReadOffsetTarget(bytes, fieldPosition, label);
        var count = ReadVectorCount(bytes, vector, label);
        var positions = new int[count];
        var data = checked(vector + sizeof(uint));
        EnsureRange(bytes, data, checked(count * sizeof(uint)), label);
        for (var index = 0; index < count; index++)
        {
            var element = checked(data + (index * sizeof(uint)));
            positions[index] = CheckedTarget(
                element,
                ReadUInt32(bytes, element, $"{label} entry offset"),
                bytes.Length,
                label);
        }

        return positions;
    }

    private static IReadOnlyList<string> ReadStringVector(
        byte[] bytes,
        int fieldPosition,
        string label)
    {
        var vector = ReadOffsetTarget(bytes, fieldPosition, label);
        var count = ReadVectorCount(bytes, vector, label);
        var values = new string[count];
        var data = checked(vector + sizeof(uint));
        EnsureRange(bytes, data, checked(count * sizeof(uint)), label);
        for (var index = 0; index < count; index++)
        {
            var element = checked(data + (index * sizeof(uint)));
            values[index] = ReadRequiredString(bytes, element, $"{label} entry");
        }

        return values;
    }

    private static int ReadVectorCount(byte[] bytes, int vectorPosition, string label)
    {
        var raw = ReadUInt32(bytes, vectorPosition, $"{label} count");
        if (raw > int.MaxValue)
        {
            throw new InvalidDataException($"{label} count is outside the supported range.");
        }

        var count = (int)raw;
        ZaFashionCatalogFlatBufferSupport.EnsureCount(count, label);
        return count;
    }

    private static string ReadRequiredString(byte[] bytes, int fieldPosition, string label)
    {
        var target = ReadOffsetTarget(bytes, fieldPosition, label);
        var length = ReadUInt32(bytes, target, $"{label} byte length");
        if (length > ZaFashionCatalogFlatBufferSupport.MaximumCatalogTextBytes)
        {
            throw new InvalidDataException($"{label} exceeds the supported UTF-8 length.");
        }

        var data = checked(target + sizeof(uint));
        EnsureRange(bytes, data, checked((int)length + 1), label);
        if (bytes[data + (int)length] != 0)
        {
            throw new InvalidDataException($"{label} is not null terminated.");
        }

        var value = StrictUtf8.GetString(bytes, data, (int)length);
        return ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(value, label);
    }

    private static int ReadOffsetTarget(byte[] bytes, int position, string label) =>
        CheckedTarget(position, ReadUInt32(bytes, position, $"{label} offset"), bytes.Length, label);

    private static int CheckedTarget(int position, uint distance, int length, string label)
    {
        var target = (long)position + distance;
        if (distance == 0 || target < 0 || target > length - sizeof(uint))
        {
            throw new InvalidDataException($"{label} contains an invalid offset.");
        }

        return (int)target;
    }

    private static int RequireField(byte[] bytes, int table, int field, string label) =>
        GetField(bytes, table, field, label)
        ?? throw new InvalidDataException($"{label} is missing.");

    private static int? GetField(byte[] bytes, int table, int field, string label)
    {
        EnsureRange(bytes, table, sizeof(int), label);
        var distance = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(table, sizeof(int)));
        var vtableLong = (long)table - distance;
        if (distance == 0 || vtableLong < 0 || vtableLong > bytes.Length - 4)
        {
            throw new InvalidDataException($"{label} has an invalid virtual table.");
        }

        var vtable = (int)vtableLong;
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable, sizeof(ushort)));
        var entry = checked(vtable + 4 + (field * sizeof(ushort)));
        if (entry > vtable + vtableLength - sizeof(ushort))
        {
            return null;
        }

        EnsureRange(bytes, entry, sizeof(ushort), label);
        var offset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry, sizeof(ushort)));
        if (offset == 0)
        {
            return null;
        }

        var position = checked(table + offset);
        EnsureRange(bytes, position, sizeof(uint), label);
        return position;
    }

    private static void ValidateOptionalStringField(
        byte[] bytes,
        int table,
        int field,
        string label)
    {
        var position = GetField(bytes, table, field, label);
        if (position is not null)
        {
            _ = ReadRequiredString(bytes, position.Value, label);
        }
    }

    private static void ValidateOptionalScalarField(
        byte[] bytes,
        int table,
        int field,
        string label)
    {
        var position = GetField(bytes, table, field, label);
        if (position is not null)
        {
            _ = ReadUInt32(bytes, position.Value, label);
        }
    }

    private static uint ReadUInt32(byte[] bytes, int position, string label)
    {
        EnsureRange(bytes, position, sizeof(uint), label);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, sizeof(uint)));
    }

    private static void EnsureRange(byte[] bytes, int position, int length, string label)
    {
        if (position < 0 || length < 0 || position > bytes.Length - length)
        {
            throw new InvalidDataException($"{label} points outside the source buffer.");
        }
    }
}

internal sealed record ZaFashionLineupData(
    int PhysicalIndex,
    string LineupId,
    IReadOnlyList<ZaFashionLineupEntryData> Entries);

internal sealed record ZaFashionLineupEntryData(
    int PhysicalIndex,
    uint ItemId,
    int ItemValuePosition,
    IReadOnlyList<ZaFashionLineupConditionGroupData> ConditionGroups);

internal sealed record ZaFashionLineupConditionGroupData(
    int PhysicalIndex,
    IReadOnlyList<ZaFashionLineupConditionHolderData> Holders);

internal sealed record ZaFashionLineupConditionHolderData(
    int PhysicalIndex,
    IReadOnlyList<ZaFashionLineupConditionData> Conditions);

internal sealed record ZaFashionLineupConditionData(
    int PhysicalIndex,
    bool HasCondition,
    string? Condition,
    bool HasComparison,
    uint Comparison,
    bool HasArguments,
    IReadOnlyList<string> Arguments);

internal sealed record ZaFashionLineupDataRow(
    int PhysicalIndex,
    int LineupPhysicalIndex,
    int EntryPhysicalIndex,
    string LineupId,
    IReadOnlyList<string> ShopIds,
    uint ItemId,
    int ItemValuePosition);
