// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.ZA;

public sealed record ZaPokedexMegaContentsRow(
    int ContentId,
    int Species,
    int Form,
    int Group)
{
    public bool HasKnownGroup =>
        Group is (int)ZaPokedexContentsGroup.Regular or (int)ZaPokedexContentsGroup.Hyperspace;
}

public sealed class ZaPokedexMegaContentsTable
{
    public const string VirtualPath =
        "world/exl/pokedex_contents_data/pokedex_mega_contents_data/pokedex_mega_contents_data.bin";

    private const int RootValuesFieldIndex = 0;
    private const int ContentIdFieldIndex = 0;
    private const int SpeciesFieldIndex = 1;
    private const int FormFieldIndex = 2;
    private const int GroupFieldIndex = 4;
    private const int ScalarSize = sizeof(int);
    private const int VtableHeaderSize = sizeof(ushort) * 2;

    private readonly byte[] sourceBytes;
    private readonly EntryLayout[] layouts;

    private ZaPokedexMegaContentsTable(
        byte[] sourceBytes,
        ZaPokedexMegaContentsRow[] rows,
        EntryLayout[] layouts)
    {
        this.sourceBytes = sourceBytes;
        Rows = Array.AsReadOnly(rows);
        this.layouts = layouts;
    }

    public IReadOnlyList<ZaPokedexMegaContentsRow> Rows { get; }

    public static ZaPokedexMegaContentsTable Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return ReadCore(bytes);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Invalid Z-A Mega Pokédex contents data: an offset or size exceeds supported bounds.",
                exception);
        }
    }

    private static ZaPokedexMegaContentsTable ReadCore(ReadOnlySpan<byte> bytes)
    {
        var sourceBytes = bytes.ToArray();
        var source = sourceBytes.AsSpan();
        if (source.Length < sizeof(uint))
        {
            throw Invalid("The root offset is missing.");
        }

        var rootPosition = ResolveForwardOffset(source, 0, "root table");
        var root = ReadTableLayout(source, rootPosition, "root table");
        var valuesFieldOffset = ReadFieldOffset(source, root, RootValuesFieldIndex);
        if (valuesFieldOffset == 0)
        {
            throw Invalid("The required row vector is missing.");
        }

        ValidateIntField(root, valuesFieldOffset, "root row vector");
        var valuesFieldPosition = checked(root.TablePosition + valuesFieldOffset);
        var vectorPosition = ResolveForwardOffset(source, valuesFieldPosition, "row vector");
        EnsureRange(source, vectorPosition, sizeof(int), "row vector length");
        var rowCount = BinaryPrimitives.ReadInt32LittleEndian(source[vectorPosition..]);
        if (rowCount <= 0)
        {
            throw Invalid("The row vector must contain at least one row.");
        }

        var vectorEntriesPosition = checked(vectorPosition + sizeof(int));
        var vectorByteLength = checked(rowCount * sizeof(uint));
        EnsureRange(source, vectorEntriesPosition, vectorByteLength, "row vector entries");

        var rows = new ZaPokedexMegaContentsRow[rowCount];
        var layouts = new EntryLayout[rowCount];
        var contentIds = new HashSet<int>();
        var speciesForms = new HashSet<(int Species, int Form)>();
        var tablePositions = new HashSet<int>();

        for (var index = 0; index < rowCount; index++)
        {
            var vectorEntryPosition = checked(vectorEntriesPosition + index * sizeof(uint));
            var tablePosition = ResolveForwardOffset(
                source,
                vectorEntryPosition,
                $"row {index} table");
            if (!tablePositions.Add(tablePosition))
            {
                throw Invalid($"Row {index} aliases another row table.");
            }

            var table = ReadTableLayout(source, tablePosition, $"row {index}");
            var contentIdOffset = ReadFieldOffset(source, table, ContentIdFieldIndex);
            var speciesOffset = ReadFieldOffset(source, table, SpeciesFieldIndex);
            var formOffset = ReadFieldOffset(source, table, FormFieldIndex);
            var groupOffset = ReadFieldOffset(source, table, GroupFieldIndex);
            if (contentIdOffset == 0 || speciesOffset == 0 || formOffset == 0)
            {
                throw Invalid($"Row {index} is missing a required identity field.");
            }

            ValidateIntField(table, contentIdOffset, $"row {index} content ID");
            ValidateIntField(table, speciesOffset, $"row {index} species");
            ValidateIntField(table, formOffset, $"row {index} form");
            ValidateDistinctIntFields(
                contentIdOffset,
                speciesOffset,
                formOffset,
                $"row {index}");
            if (groupOffset != 0)
            {
                ValidateByteField(table, groupOffset, $"row {index} group");
                ValidateGroupDoesNotOverlapFields(
                    source,
                    table,
                    groupOffset,
                    contentIdOffset,
                    speciesOffset,
                    formOffset,
                    $"row {index}");
            }

            var contentId = ReadInt(source, tablePosition + contentIdOffset);
            var species = ReadInt(source, tablePosition + speciesOffset);
            var form = ReadInt(source, tablePosition + formOffset);
            var group = groupOffset == 0 ? 0 : source[tablePosition + groupOffset];
            if (contentId <= 0 || species <= 0 || form <= 0)
            {
                throw Invalid($"Row {index} has a non-positive identity value.");
            }

            if (group is not (int)ZaPokedexContentsGroup.Regular
                and not (int)ZaPokedexContentsGroup.Hyperspace)
            {
                throw Invalid($"Row {index} has unsupported group value {group}.");
            }

            if (!contentIds.Add(contentId))
            {
                throw Invalid($"Content ID {contentId} occurs more than once.");
            }

            if (!speciesForms.Add((species, form)))
            {
                throw Invalid($"Species {species} form {form} occurs more than once.");
            }

            rows[index] = new ZaPokedexMegaContentsRow(contentId, species, form, group);
            layouts[index] = new EntryLayout(table, vectorEntryPosition, groupOffset);
        }

        return new ZaPokedexMegaContentsTable(sourceBytes, rows, layouts);
    }

    public byte[] WriteSpeciesGroups(
        IReadOnlyDictionary<int, ZaPokedexContentsGroup> groupsBySpecies)
    {
        ArgumentNullException.ThrowIfNull(groupsBySpecies);

        var output = sourceBytes.ToArray();
        for (var index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index];
            if (!groupsBySpecies.TryGetValue(row.Species, out var group))
            {
                throw new InvalidDataException(
                    $"Mega Pokédex species {row.Species} has no target Pokédex membership.");
            }

            ValidateGroup(group, nameof(groupsBySpecies));
            var groupValue = (int)group;
            if (row.Group == groupValue)
            {
                continue;
            }

            var layout = layouts[index];
            if (layout.GroupFieldOffset != 0)
            {
                output[layout.Table.TablePosition + layout.GroupFieldOffset] = checked((byte)groupValue);
                continue;
            }

            MaterializeGroupField(ref output, layout, checked((byte)groupValue));
        }

        var verification = Read(output);
        for (var index = 0; index < Rows.Count; index++)
        {
            var before = Rows[index];
            var after = verification.Rows[index];
            var expectedGroup = (int)groupsBySpecies[before.Species];
            if (after.ContentId != before.ContentId
                || after.Species != before.Species
                || after.Form != before.Form
                || after.Group != expectedGroup)
            {
                throw new InvalidDataException(
                    "Mega Pokédex contents verification failed after writing species membership.");
            }
        }

        return output;
    }

    private void MaterializeGroupField(
        ref byte[] output,
        EntryLayout layout,
        byte group)
    {
        var requiredVtableLength = VtableHeaderSize + (GroupFieldIndex + 1) * sizeof(ushort);
        var newVtableLength = Math.Max((int)layout.Table.VtableLength, requiredVtableLength);
        var newGroupFieldOffset = layout.Table.ObjectSize;
        var newObjectSize = checked(newGroupFieldOffset + sizeof(byte));
        if (newVtableLength > ushort.MaxValue || newObjectSize > ushort.MaxValue)
        {
            throw new InvalidDataException("The Mega Pokédex row is too large to extend safely.");
        }

        var vtablePosition = AlignUp(output.Length, sizeof(ushort));
        var tablePosition = AlignUp(checked(vtablePosition + newVtableLength), ScalarSize);
        var newLength = checked(tablePosition + newObjectSize);
        Array.Resize(ref output, newLength);

        sourceBytes
            .AsSpan(layout.Table.VtablePosition, layout.Table.VtableLength)
            .CopyTo(output.AsSpan(vtablePosition, layout.Table.VtableLength));
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(vtablePosition),
            checked((ushort)newVtableLength));
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(vtablePosition + sizeof(ushort)),
            checked((ushort)newObjectSize));
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(
                vtablePosition + VtableHeaderSize + GroupFieldIndex * sizeof(ushort)),
            checked((ushort)newGroupFieldOffset));

        sourceBytes
            .AsSpan(layout.Table.TablePosition, layout.Table.ObjectSize)
            .CopyTo(output.AsSpan(tablePosition, layout.Table.ObjectSize));
        WriteInt(output, tablePosition, checked(tablePosition - vtablePosition));
        output[tablePosition + newGroupFieldOffset] = group;

        var newRowOffset = checked((uint)(tablePosition - layout.VectorEntryPosition));
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(layout.VectorEntryPosition),
            newRowOffset);
    }

    private static TableLayout ReadTableLayout(
        ReadOnlySpan<byte> source,
        int tablePosition,
        string context)
    {
        EnsureRange(source, tablePosition, sizeof(int), $"{context} header");
        var vtableDistance = ReadInt(source, tablePosition);
        if (vtableDistance == 0)
        {
            throw Invalid($"{context} has a zero vtable distance.");
        }

        var vtablePosition = checked(tablePosition - vtableDistance);
        EnsureRange(source, vtablePosition, VtableHeaderSize, $"{context} vtable header");
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(source[vtablePosition..]);
        var objectSize = BinaryPrimitives.ReadUInt16LittleEndian(
            source[(vtablePosition + sizeof(ushort))..]);
        if (vtableLength < VtableHeaderSize || (vtableLength & 1) != 0)
        {
            throw Invalid($"{context} has an invalid vtable length.");
        }

        if (objectSize < sizeof(int))
        {
            throw Invalid($"{context} has an invalid object size.");
        }

        EnsureRange(source, vtablePosition, vtableLength, $"{context} vtable");
        EnsureRange(source, tablePosition, objectSize, $"{context} object");
        return new TableLayout(tablePosition, vtablePosition, vtableLength, objectSize);
    }

    private static ushort ReadFieldOffset(
        ReadOnlySpan<byte> source,
        TableLayout table,
        int fieldIndex)
    {
        var vtableFieldPosition = VtableHeaderSize + fieldIndex * sizeof(ushort);
        if (vtableFieldPosition + sizeof(ushort) > table.VtableLength)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(
            source.Slice(
                table.VtablePosition + vtableFieldPosition,
                sizeof(ushort)));
    }

    private static void ValidateDistinctIntFields(
        ushort first,
        ushort second,
        ushort third,
        string context)
    {
        var offsets = new[] { first, second, third };
        for (var left = 0; left < offsets.Length; left++)
        {
            for (var right = left + 1; right < offsets.Length; right++)
            {
                if (RangesOverlap(offsets[left], ScalarSize, offsets[right], ScalarSize))
                {
                    throw Invalid($"{context} has overlapping identity fields.");
                }
            }
        }
    }

    private static void ValidateGroupDoesNotOverlapFields(
        ReadOnlySpan<byte> source,
        TableLayout table,
        ushort groupOffset,
        ushort contentIdOffset,
        ushort speciesOffset,
        ushort formOffset,
        string context)
    {
        foreach (var intOffset in new[] { contentIdOffset, speciesOffset, formOffset })
        {
            if (RangesOverlap(groupOffset, sizeof(byte), intOffset, ScalarSize))
            {
                throw Invalid($"{context} group overlaps an identity field.");
            }
        }

        var fieldCount = (table.VtableLength - VtableHeaderSize) / sizeof(ushort);
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            if (fieldIndex is ContentIdFieldIndex
                or SpeciesFieldIndex
                or FormFieldIndex
                or GroupFieldIndex)
            {
                continue;
            }

            var fieldOffset = ReadFieldOffset(source, table, fieldIndex);
            if (fieldOffset == groupOffset)
            {
                throw Invalid($"{context} group aliases another field.");
            }
        }
    }

    private static void ValidateIntField(
        TableLayout table,
        ushort fieldOffset,
        string context)
    {
        if (fieldOffset < sizeof(int)
            || (fieldOffset & (ScalarSize - 1)) != 0
            || fieldOffset > table.ObjectSize - ScalarSize)
        {
            throw Invalid($"{context} falls outside its table object.");
        }
    }

    private static void ValidateByteField(
        TableLayout table,
        ushort fieldOffset,
        string context)
    {
        if (fieldOffset < sizeof(int) || fieldOffset >= table.ObjectSize)
        {
            throw Invalid($"{context} falls outside its table object.");
        }
    }

    private static int ResolveForwardOffset(
        ReadOnlySpan<byte> source,
        int offsetPosition,
        string context)
    {
        EnsureRange(source, offsetPosition, sizeof(uint), $"{context} offset");
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(source[offsetPosition..]);
        if (relativeOffset == 0 || relativeOffset > int.MaxValue)
        {
            throw Invalid($"{context} has an invalid relative offset.");
        }

        var targetPosition = checked(offsetPosition + (int)relativeOffset);
        EnsureRange(source, targetPosition, 1, context);
        return targetPosition;
    }

    private static int ReadInt(ReadOnlySpan<byte> source, int position)
    {
        EnsureRange(source, position, sizeof(int), "32-bit value");
        return BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
    }

    private static void WriteInt(byte[] output, int position, int value)
    {
        if (position < 0 || position > output.Length - sizeof(int))
        {
            throw Invalid("A group write falls outside the output buffer.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(position), value);
    }

    private static void ValidateGroup(ZaPokedexContentsGroup group, string parameterName)
    {
        if (group is not ZaPokedexContentsGroup.Regular
            and not ZaPokedexContentsGroup.Hyperspace)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                group,
                "Pokédex group must be Regular or Hyperspace.");
        }
    }

    private static bool RangesOverlap(int firstOffset, int firstLength, int secondOffset, int secondLength)
    {
        return firstOffset < secondOffset + secondLength
            && secondOffset < firstOffset + firstLength;
    }

    private static int AlignUp(int value, int alignment)
    {
        return checked((value + alignment - 1) & -alignment);
    }

    private static void EnsureRange(
        ReadOnlySpan<byte> source,
        int position,
        int length,
        string context)
    {
        if (position < 0 || length < 0 || position > source.Length - length)
        {
            throw Invalid($"{context} falls outside the input buffer.");
        }
    }

    private static InvalidDataException Invalid(string message)
    {
        return new InvalidDataException($"Invalid Z-A Mega Pokédex contents data: {message}");
    }

    private readonly record struct EntryLayout(
        TableLayout Table,
        int VectorEntryPosition,
        ushort GroupFieldOffset);

    private readonly record struct TableLayout(
        int TablePosition,
        int VtablePosition,
        ushort VtableLength,
        ushort ObjectSize);
}
