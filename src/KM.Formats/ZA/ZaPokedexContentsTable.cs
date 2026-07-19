// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.ZA;

public enum ZaPokedexContentsGroup
{
    Regular = 0,
    Hyperspace = 1,
}

public sealed record ZaPokedexContentsRow(
    int ContentId,
    int Species,
    int Group)
{
    public bool HasKnownGroup =>
        Group is (int)ZaPokedexContentsGroup.Regular or (int)ZaPokedexContentsGroup.Hyperspace;
}

public sealed class ZaPokedexContentsTable
{
    public const string VirtualPath =
        "world/exl/pokedex_contents_data/pokedex_contents_data/pokedex_contents_data.bin";

    private const int RootValuesFieldIndex = 0;
    private const int ContentIdFieldIndex = 0;
    private const int SpeciesFieldIndex = 1;
    private const int GroupFieldIndex = 2;
    private const int KnownRowFieldCount = 3;
    private const int ScalarSize = sizeof(int);
    private const int VtableHeaderSize = sizeof(ushort) * 2;

    private readonly byte[] sourceBytes;
    private readonly EntryLayout[] layouts;
    private readonly IReadOnlyDictionary<int, int> rowIndexBySpecies;
    private readonly IReadOnlySet<int> ambiguousSpecies;

    private ZaPokedexContentsTable(
        byte[] sourceBytes,
        ZaPokedexContentsRow[] rows,
        EntryLayout[] layouts,
        IReadOnlyDictionary<int, int> rowIndexBySpecies,
        IReadOnlySet<int> ambiguousSpecies)
    {
        this.sourceBytes = sourceBytes;
        Rows = Array.AsReadOnly(rows);
        this.layouts = layouts;
        this.rowIndexBySpecies = rowIndexBySpecies;
        this.ambiguousSpecies = ambiguousSpecies;
    }

    public IReadOnlyList<ZaPokedexContentsRow> Rows { get; }

    public static ZaPokedexContentsTable Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return ReadCore(bytes);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Invalid Z-A Pokédex contents data: an offset or size exceeds supported bounds.",
                exception);
        }
    }

    private static ZaPokedexContentsTable ReadCore(ReadOnlySpan<byte> bytes)
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

        ValidateScalarField(root, valuesFieldOffset, "root row vector");
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

        var rows = new ZaPokedexContentsRow[rowCount];
        var layouts = new EntryLayout[rowCount];
        var rowIndexBySpecies = new Dictionary<int, int>(rowCount);
        var ambiguousSpecies = new HashSet<int>();

        for (var index = 0; index < rowCount; index++)
        {
            var vectorEntryPosition = checked(vectorEntriesPosition + index * sizeof(uint));
            var tablePosition = ResolveForwardOffset(
                source,
                vectorEntryPosition,
                $"row {index} table");
            var table = ReadTableLayout(source, tablePosition, $"row {index}");
            var contentIdOffset = ReadFieldOffset(source, table, ContentIdFieldIndex);
            var speciesOffset = ReadFieldOffset(source, table, SpeciesFieldIndex);
            var groupOffset = ReadFieldOffset(source, table, GroupFieldIndex);

            if (contentIdOffset != 0)
            {
                ValidateScalarField(table, contentIdOffset, $"row {index} content ID");
            }

            if (speciesOffset != 0)
            {
                ValidateScalarField(table, speciesOffset, $"row {index} species");
            }

            if (groupOffset != 0)
            {
                ValidateScalarField(table, groupOffset, $"row {index} group");
            }

            var materializedOffsets = new[] { contentIdOffset, speciesOffset, groupOffset }
                .Where(offset => offset != 0)
                .ToArray();
            if (materializedOffsets.Distinct().Count() != materializedOffsets.Length)
            {
                throw Invalid($"Row {index} has overlapping scalar fields.");
            }

            var contentId = contentIdOffset == 0 ? 0 : ReadInt(source, tablePosition + contentIdOffset);
            var species = speciesOffset == 0 ? 0 : ReadInt(source, tablePosition + speciesOffset);
            var group = groupOffset == 0 ? 0 : ReadInt(source, tablePosition + groupOffset);
            if (!rowIndexBySpecies.TryAdd(species, index))
            {
                ambiguousSpecies.Add(species);
            }

            rows[index] = new ZaPokedexContentsRow(contentId, species, group);
            layouts[index] = new EntryLayout(
                table,
                vectorEntryPosition,
                groupOffset,
                HasMaterializedUnknownFields(source, table));
        }

        return new ZaPokedexContentsTable(
            sourceBytes,
            rows,
            layouts,
            rowIndexBySpecies,
            ambiguousSpecies);
    }

    public byte[] WriteSpeciesGroups(
        IReadOnlyDictionary<int, ZaPokedexContentsGroup> groupsBySpecies)
    {
        ArgumentNullException.ThrowIfNull(groupsBySpecies);
        if (groupsBySpecies.Count == 0)
        {
            return sourceBytes.ToArray();
        }

        var output = sourceBytes.ToArray();
        foreach (var (species, group) in groupsBySpecies.OrderBy(pair => pair.Key))
        {
            ValidateGroup(group, nameof(groupsBySpecies));
            if (!rowIndexBySpecies.TryGetValue(species, out var rowIndex))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(groupsBySpecies),
                    species,
                    $"Species {species} is not present in the Pokédex contents table.");
            }

            if (ambiguousSpecies.Contains(species))
            {
                throw new InvalidDataException(
                    $"Species {species} occurs more than once in the Pokédex contents table.");
            }

            var row = Rows[rowIndex];
            if (!row.HasKnownGroup)
            {
                throw new InvalidDataException(
                    $"Species {species} has unsupported Pokédex group value {row.Group}.");
            }

            var groupValue = (int)group;
            if (row.Group == groupValue)
            {
                continue;
            }

            var layout = layouts[rowIndex];
            if (layout.GroupFieldOffset != 0)
            {
                WriteInt(output, layout.Table.TablePosition + layout.GroupFieldOffset, groupValue);
                continue;
            }

            if (layout.HasMaterializedUnknownFields)
            {
                throw new InvalidDataException(
                    $"Species {species} uses an unsupported extended row layout, "
                    + "so its omitted group cannot be materialized safely.");
            }

            MaterializeGroupField(ref output, layout, groupValue);
        }

        return output;
    }

    public byte[] SwapSpeciesGroups(int firstSpecies, int secondSpecies)
    {
        if (firstSpecies == secondSpecies)
        {
            throw new ArgumentException("A Pokédex group swap requires two different species.");
        }

        var first = GetRequiredRow(firstSpecies, nameof(firstSpecies));
        var second = GetRequiredRow(secondSpecies, nameof(secondSpecies));
        if (!first.HasKnownGroup || !second.HasKnownGroup)
        {
            throw new InvalidDataException("A Pokédex group swap cannot use an unknown group value.");
        }

        if (first.Group == second.Group)
        {
            throw new InvalidOperationException(
                "A Pokédex group swap requires one Regular species and one Hyperspace species.");
        }

        return WriteSpeciesGroups(new Dictionary<int, ZaPokedexContentsGroup>
        {
            [firstSpecies] = (ZaPokedexContentsGroup)second.Group,
            [secondSpecies] = (ZaPokedexContentsGroup)first.Group,
        });
    }

    private ZaPokedexContentsRow GetRequiredRow(int species, string parameterName)
    {
        if (!rowIndexBySpecies.TryGetValue(species, out var rowIndex))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                species,
                $"Species {species} is not present in the Pokédex contents table.");
        }

        if (ambiguousSpecies.Contains(species))
        {
            throw new InvalidDataException(
                $"Species {species} occurs more than once in the Pokédex contents table.");
        }

        return Rows[rowIndex];
    }

    private void MaterializeGroupField(
        ref byte[] output,
        EntryLayout layout,
        int group)
    {
        // Group zero is normally omitted. Append one extended copy of the row and
        // redirect its vector entry so unrelated tables and bytes remain untouched.
        var newVtableLength = Math.Max(
            (int)layout.Table.VtableLength,
            VtableHeaderSize + KnownRowFieldCount * sizeof(ushort));
        var newGroupFieldOffset = AlignUp(layout.Table.ObjectSize, ScalarSize);
        var newObjectSize = checked(newGroupFieldOffset + ScalarSize);
        if (newVtableLength > ushort.MaxValue || newObjectSize > ushort.MaxValue)
        {
            throw new InvalidDataException("The Pokédex row is too large to extend safely.");
        }

        var vtablePosition = AlignUp(output.Length, sizeof(ushort));
        var tablePosition = AlignUp(
            checked(vtablePosition + newVtableLength),
            ScalarSize);
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
        WriteInt(output, tablePosition + newGroupFieldOffset, group);

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
        return new TableLayout(
            tablePosition,
            vtablePosition,
            vtableLength,
            objectSize);
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

    private static bool HasMaterializedUnknownFields(
        ReadOnlySpan<byte> source,
        TableLayout table)
    {
        var fieldCount = (table.VtableLength - VtableHeaderSize) / sizeof(ushort);
        for (var fieldIndex = KnownRowFieldCount; fieldIndex < fieldCount; fieldIndex++)
        {
            if (ReadFieldOffset(source, table, fieldIndex) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateScalarField(
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
        return new InvalidDataException($"Invalid Z-A Pokédex contents data: {message}");
    }

    private readonly record struct EntryLayout(
        TableLayout Table,
        int VectorEntryPosition,
        ushort GroupFieldOffset,
        bool HasMaterializedUnknownFields);

    private readonly record struct TableLayout(
        int TablePosition,
        int VtablePosition,
        ushort VtableLength,
        ushort ObjectSize);
}
