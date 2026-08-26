// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KM.Formats.SV.Habitat;

public readonly record struct SvHabitatCoordinate(int X, int Y);

public sealed record SvHabitatSemanticIdentity(
    int DevNo,
    int FormNo,
    bool VersionA,
    bool VersionB);

public sealed record SvHabitatPhysicalLocator(
    string SourceFile,
    int OuterGroupOccurrence,
    int RowOccurrence,
    string RowPreimageSha256);

public sealed record SvHabitatDistributionRow(
    SvHabitatPhysicalLocator Locator,
    SvHabitatSemanticIdentity Identity,
    SvHabitatCoordinate Coordinate);

public sealed record SvHabitatDistributionGroup(
    int OuterGroupOccurrence,
    IReadOnlyList<SvHabitatDistributionRow> Rows);

public sealed record SvHabitatCoordinateMutation(
    SvHabitatPhysicalLocator Locator,
    SvHabitatSemanticIdentity Identity,
    SvHabitatCoordinate ExpectedCoordinate,
    SvHabitatCoordinate DesiredCoordinate);

/// <summary>
/// Bounded reader and exact-byte transformer for Scarlet/Violet Pokedex
/// distribution cells. The transformer changes only a row's Grid reference and
/// points it at a deterministic, KM-owned catalog of coordinates observed in
/// the exact supported source. It never rebuilds the source FlatBuffer.
/// </summary>
public sealed class SvHabitatDistributionDocument
{
    public const int MaximumSourceBytes = 2 * 1024 * 1024;
    public const int MaximumGroupCount = 4_096;
    public const int MaximumRowCount = 50_000;
    public const int MaximumCoordinateCount = 2_048;
    public const int MaximumMutationCount = 10_000;

    private static ReadOnlySpan<byte> CatalogMagic => "KMHABV01"u8;
    private const int CatalogHeaderBytes = 48;
    private const int CatalogEntryBytes = 20;
    private const int CatalogVersion = 1;

    private readonly IReadOnlyList<RowLayout> rowLayouts;

    private SvHabitatDistributionDocument(
        string sourceFile,
        byte[] bytes,
        IReadOnlyList<SvHabitatDistributionGroup> groups,
        IReadOnlyList<SvHabitatCoordinate> observedCoordinates,
        IReadOnlyList<RowLayout> rowLayouts)
    {
        SourceFile = sourceFile;
        Bytes = bytes;
        Groups = groups;
        ObservedCoordinates = observedCoordinates;
        this.rowLayouts = rowLayouts;
        SourceRevision = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public string SourceFile { get; }

    public string SourceRevision { get; }

    public IReadOnlyList<SvHabitatDistributionGroup> Groups { get; }

    public IReadOnlyList<SvHabitatCoordinate> ObservedCoordinates { get; }

    public int RowCount => rowLayouts.Count;

    private byte[] Bytes { get; }

    public static SvHabitatDistributionDocument Parse(byte[] bytes, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        if (sourceFile.Length > 512 || sourceFile.Any(char.IsControl))
        {
            throw new ArgumentException("The habitat source identity is invalid.", nameof(sourceFile));
        }

        if (bytes.Length is < 8 or > MaximumSourceBytes)
        {
            throw new InvalidDataException("The habitat distribution source is outside its bounded byte limit.");
        }

        var reader = new BoundedFlatBufferReader(bytes);
        var root = reader.ReadRootTable();
        var groupVector = reader.ReadRequiredTableVector(root, fieldId: 0, "distribution groups");
        if (groupVector.Count > MaximumGroupCount)
        {
            throw new InvalidDataException("The habitat distribution source exceeds the bounded group limit.");
        }

        var groups = new List<SvHabitatDistributionGroup>(groupVector.Count);
        var layouts = new List<RowLayout>();
        var seenGroupTables = new HashSet<int>();
        var seenRowTables = new HashSet<int>();
        for (var groupOccurrence = 0; groupOccurrence < groupVector.Count; groupOccurrence++)
        {
            var groupTable = reader.ReadTableVectorElement(groupVector, groupOccurrence);
            if (!seenGroupTables.Add(groupTable.Position))
            {
                throw new InvalidDataException("The habitat distribution source aliases an outer group table.");
            }

            var rowVector = reader.ReadRequiredTableVector(groupTable, fieldId: 0, "distribution rows");
            if (rowVector.Count > MaximumRowCount - layouts.Count)
            {
                throw new InvalidDataException("The habitat distribution source exceeds the bounded row limit.");
            }

            var rows = new List<SvHabitatDistributionRow>(rowVector.Count);
            for (var rowOccurrence = 0; rowOccurrence < rowVector.Count; rowOccurrence++)
            {
                var rowTable = reader.ReadTableVectorElement(rowVector, rowOccurrence);
                if (!seenRowTables.Add(rowTable.Position))
                {
                    throw new InvalidDataException("The habitat distribution source aliases a physical row table.");
                }

                var identity = new SvHabitatSemanticIdentity(
                    reader.ReadInt32(rowTable, fieldId: 0),
                    reader.ReadInt32(rowTable, fieldId: 1),
                    reader.ReadBoolean(rowTable, fieldId: 2),
                    reader.ReadBoolean(rowTable, fieldId: 3));
                var (gridPointerLocation, gridTable) = reader.ReadRequiredTable(rowTable, fieldId: 4, "distribution grid");
                var coordinate = new SvHabitatCoordinate(
                    reader.ReadInt32(gridTable, fieldId: 0),
                    reader.ReadInt32(gridTable, fieldId: 1));
                var preimage = CreateRowPreimage(reader, rowTable, gridTable);
                var locator = new SvHabitatPhysicalLocator(
                    sourceFile,
                    groupOccurrence,
                    rowOccurrence,
                    preimage);
                var row = new SvHabitatDistributionRow(locator, identity, coordinate);
                rows.Add(row);
                layouts.Add(new RowLayout(
                    groupOccurrence,
                    rowOccurrence,
                    rowTable,
                    gridPointerLocation,
                    gridTable,
                    row));
            }

            groups.Add(new SvHabitatDistributionGroup(groupOccurrence, rows));
        }

        var coordinates = layouts
            .Select(layout => layout.Row.Coordinate)
            .Distinct()
            .OrderBy(coordinate => coordinate.X)
            .ThenBy(coordinate => coordinate.Y)
            .ToArray();
        if (coordinates.Length is 0 or > MaximumCoordinateCount)
        {
            throw new InvalidDataException("The habitat distribution coordinate catalog is outside its bounded limit.");
        }

        return new SvHabitatDistributionDocument(
            sourceFile,
            bytes,
            groups,
            coordinates,
            layouts);
    }

    public static byte[] Apply(
        byte[] exactBaseBytes,
        byte[] currentBytes,
        string sourceFile,
        IReadOnlyList<SvHabitatCoordinateMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(exactBaseBytes);
        ArgumentNullException.ThrowIfNull(currentBytes);
        ArgumentNullException.ThrowIfNull(mutations);
        if (mutations.Count is 0 or > MaximumMutationCount)
        {
            throw new InvalidDataException("A habitat coordinate transform requires a bounded, non-empty mutation list.");
        }

        var baseDocument = Parse(exactBaseBytes, sourceFile);
        var currentDocument = Parse(currentBytes, sourceFile);
        var trailer = CreateCatalogTrailer(baseDocument);
        ValidateSupportedCurrent(baseDocument, currentDocument, trailer);

        var currentRows = currentDocument.rowLayouts.ToDictionary(
            layout => (layout.GroupOccurrence, layout.RowOccurrence));
        var baseRows = baseDocument.rowLayouts.ToDictionary(
            layout => (layout.GroupOccurrence, layout.RowOccurrence));
        var desiredByPhysicalRow = currentRows.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Row.Coordinate);
        var seenTargets = new HashSet<(int Group, int Row)>();
        var observedCoordinates = baseDocument.ObservedCoordinates.ToHashSet();

        foreach (var mutation in mutations)
        {
            if (!string.Equals(mutation.Locator.SourceFile, sourceFile, StringComparison.Ordinal)
                || mutation.Locator.OuterGroupOccurrence < 0
                || mutation.Locator.RowOccurrence < 0)
            {
                throw new InvalidDataException("A habitat coordinate mutation has an invalid physical source locator.");
            }

            var key = (mutation.Locator.OuterGroupOccurrence, mutation.Locator.RowOccurrence);
            if (!seenTargets.Add(key)
                || !currentRows.TryGetValue(key, out var currentRow)
                || !baseRows.ContainsKey(key))
            {
                throw new InvalidDataException("A habitat coordinate mutation does not resolve to one unique existing row.");
            }

            if (currentRow.Row.Identity != mutation.Identity
                || currentRow.Row.Coordinate != mutation.ExpectedCoordinate
                || !string.Equals(
                    currentRow.Row.Locator.RowPreimageSha256,
                    mutation.Locator.RowPreimageSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("A habitat coordinate mutation is stale or no longer matches its exact row preimage.");
            }

            if (!observedCoordinates.Contains(mutation.DesiredCoordinate))
            {
                throw new InvalidDataException("A habitat coordinate must be selected from the exact source region's observed catalog.");
            }

            desiredByPhysicalRow[key] = mutation.DesiredCoordinate;
        }

        if (desiredByPhysicalRow.All(pair => pair.Value == baseRows[pair.Key].Row.Coordinate))
        {
            return exactBaseBytes.ToArray();
        }

        if (mutations.All(mutation => mutation.ExpectedCoordinate == mutation.DesiredCoordinate))
        {
            return currentBytes.ToArray();
        }

        var output = new byte[checked(exactBaseBytes.Length + trailer.Length)];
        exactBaseBytes.CopyTo(output, 0);
        trailer.CopyTo(output, exactBaseBytes.Length);
        var catalogTables = GetCatalogTablePositions(baseDocument, exactBaseBytes.Length);
        foreach (var pair in desiredByPhysicalRow)
        {
            var baseRow = baseRows[pair.Key];
            if (pair.Value == baseRow.Row.Coordinate)
            {
                continue;
            }

            var tablePosition = catalogTables[pair.Value];
            var relativeOffset = checked((uint)(tablePosition - baseRow.GridPointerLocation));
            BinaryPrimitives.WriteUInt32LittleEndian(
                output.AsSpan(baseRow.GridPointerLocation, sizeof(uint)),
                relativeOffset);
        }

        var reparsed = Parse(output, sourceFile);
        ValidateSupportedCurrent(baseDocument, reparsed, trailer);
        foreach (var layout in reparsed.rowLayouts)
        {
            var key = (layout.GroupOccurrence, layout.RowOccurrence);
            if (layout.Row.Identity != baseRows[key].Row.Identity
                || layout.Row.Coordinate != desiredByPhysicalRow[key])
            {
                throw new InvalidDataException("The habitat coordinate transform failed its full structural reparse.");
            }
        }

        return output;
    }

    public static void ValidateSupportedCurrent(
        byte[] exactBaseBytes,
        byte[] currentBytes,
        string sourceFile)
    {
        var baseDocument = Parse(exactBaseBytes, sourceFile);
        var currentDocument = Parse(currentBytes, sourceFile);
        ValidateSupportedCurrent(baseDocument, currentDocument, CreateCatalogTrailer(baseDocument));
    }

    private static void ValidateSupportedCurrent(
        SvHabitatDistributionDocument baseDocument,
        SvHabitatDistributionDocument currentDocument,
        byte[] expectedTrailer)
    {
        var baseBytes = baseDocument.Bytes;
        var currentBytes = currentDocument.Bytes;
        if (currentBytes.Length == baseBytes.Length)
        {
            if (!currentBytes.AsSpan().SequenceEqual(baseBytes))
            {
                throw new InvalidDataException("The habitat source is not the exact supported input or a canonical KM output.");
            }

            return;
        }

        if (currentBytes.Length != checked(baseBytes.Length + expectedTrailer.Length)
            || !currentBytes.AsSpan(baseBytes.Length).SequenceEqual(expectedTrailer))
        {
            throw new InvalidDataException("The habitat source has an unsupported structural or trailer revision.");
        }

        if (currentDocument.rowLayouts.Count != baseDocument.rowLayouts.Count
            || currentDocument.Groups.Count != baseDocument.Groups.Count)
        {
            throw new InvalidDataException("The habitat source changed its root grouping or row membership.");
        }

        var normalizedPrefix = currentBytes.AsSpan(0, baseBytes.Length).ToArray();
        var catalogTables = GetCatalogTablePositions(baseDocument, baseBytes.Length);
        for (var index = 0; index < baseDocument.rowLayouts.Count; index++)
        {
            var baseRow = baseDocument.rowLayouts[index];
            var currentRow = currentDocument.rowLayouts[index];
            if (baseRow.GroupOccurrence != currentRow.GroupOccurrence
                || baseRow.RowOccurrence != currentRow.RowOccurrence
                || baseRow.Row.Identity != currentRow.Row.Identity
                || baseRow.GridPointerLocation != currentRow.GridPointerLocation
                || !baseDocument.ObservedCoordinates.Contains(currentRow.Row.Coordinate))
            {
                throw new InvalidDataException("The habitat source changed a physical or semantic row identity.");
            }

            var currentPointer = BinaryPrimitives.ReadUInt32LittleEndian(
                currentBytes.AsSpan(currentRow.GridPointerLocation, sizeof(uint)));
            var basePointer = BinaryPrimitives.ReadUInt32LittleEndian(
                baseBytes.AsSpan(baseRow.GridPointerLocation, sizeof(uint)));
            var expectedPointer = currentRow.Row.Coordinate == baseRow.Row.Coordinate
                ? basePointer
                : checked((uint)(catalogTables[currentRow.Row.Coordinate] - currentRow.GridPointerLocation));
            if (currentPointer != expectedPointer)
            {
                throw new InvalidDataException("The habitat source contains a noncanonical coordinate reference.");
            }

            baseBytes.AsSpan(baseRow.GridPointerLocation, sizeof(uint))
                .CopyTo(normalizedPrefix.AsSpan(baseRow.GridPointerLocation, sizeof(uint)));
        }

        if (!normalizedPrefix.AsSpan().SequenceEqual(baseBytes))
        {
            throw new InvalidDataException("The habitat source changes bytes outside existing coordinate references.");
        }
    }

    private static byte[] CreateCatalogTrailer(SvHabitatDistributionDocument baseDocument)
    {
        var coordinates = baseDocument.ObservedCoordinates;
        var trailer = new byte[checked(CatalogHeaderBytes + (coordinates.Count * CatalogEntryBytes))];
        CatalogMagic.CopyTo(trailer);
        BinaryPrimitives.WriteInt32LittleEndian(trailer.AsSpan(8, sizeof(int)), CatalogVersion);
        BinaryPrimitives.WriteInt32LittleEndian(trailer.AsSpan(12, sizeof(int)), coordinates.Count);
        SHA256.HashData(baseDocument.Bytes).CopyTo(trailer, 16);
        for (var index = 0; index < coordinates.Count; index++)
        {
            var coordinate = coordinates[index];
            var entry = trailer.AsSpan(CatalogHeaderBytes + (index * CatalogEntryBytes), CatalogEntryBytes);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, 8);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 12);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 4);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 8);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], 8);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], coordinate.X);
            BinaryPrimitives.WriteInt32LittleEndian(entry[16..], coordinate.Y);
        }

        return trailer;
    }

    private static IReadOnlyDictionary<SvHabitatCoordinate, int> GetCatalogTablePositions(
        SvHabitatDistributionDocument baseDocument,
        int baseLength)
    {
        return baseDocument.ObservedCoordinates
            .Select((coordinate, index) => new
            {
                Coordinate = coordinate,
                Position = checked(baseLength + CatalogHeaderBytes + (index * CatalogEntryBytes) + 8),
            })
            .ToDictionary(entry => entry.Coordinate, entry => entry.Position);
    }

    private static string CreateRowPreimage(
        BoundedFlatBufferReader reader,
        TableLayout row,
        TableLayout grid)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("sv-habitat-row-preimage-v1"u8);
        AppendTablePreimage(hash, reader, row);
        AppendTablePreimage(hash, reader, grid);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendTablePreimage(
        IncrementalHash hash,
        BoundedFlatBufferReader reader,
        TableLayout table)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, table.VTableLength);
        hash.AppendData(length);
        hash.AppendData(reader.Slice(table.VTablePosition, table.VTableLength));
        BinaryPrimitives.WriteInt32LittleEndian(length, table.ObjectLength);
        hash.AppendData(length);
        hash.AppendData(reader.Slice(table.Position, table.ObjectLength));
    }

    private sealed record RowLayout(
        int GroupOccurrence,
        int RowOccurrence,
        TableLayout RowTable,
        int GridPointerLocation,
        TableLayout GridTable,
        SvHabitatDistributionRow Row);

    private readonly record struct TableLayout(
        int Position,
        int VTablePosition,
        int VTableLength,
        int ObjectLength);

    private readonly record struct TableVector(int DataPosition, int Count);

    private sealed class BoundedFlatBufferReader
    {
        private const int MaximumVTableBytes = 512;
        private const int MaximumObjectBytes = 60 * 1024;
        private readonly byte[] bytes;

        public BoundedFlatBufferReader(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public ReadOnlySpan<byte> Slice(int offset, int length)
        {
            RequireRange(offset, length);
            return bytes.AsSpan(offset, length);
        }

        public TableLayout ReadRootTable()
        {
            var offset = ReadUInt32At(0);
            if (offset > int.MaxValue)
            {
                throw new InvalidDataException("The habitat FlatBuffer root offset is outside its bounded range.");
            }

            return ReadTable(checked((int)offset));
        }

        public TableVector ReadRequiredTableVector(TableLayout table, int fieldId, string label)
        {
            var field = ReadFieldLocation(table, fieldId, sizeof(uint));
            if (field is null)
            {
                throw new InvalidDataException($"The habitat FlatBuffer is missing its required {label} vector.");
            }

            var vector = FollowForwardOffset(field.Value, label);
            var count = ReadUInt32At(vector);
            if (count > MaximumRowCount)
            {
                throw new InvalidDataException($"The habitat FlatBuffer {label} vector exceeds its bounded count.");
            }

            var dataPosition = checked(vector + sizeof(uint));
            RequireRange(dataPosition, checked((int)count * sizeof(uint)));
            return new TableVector(dataPosition, checked((int)count));
        }

        public TableLayout ReadTableVectorElement(TableVector vector, int index)
        {
            if ((uint)index >= (uint)vector.Count)
            {
                throw new InvalidDataException("The habitat FlatBuffer table occurrence is outside its vector.");
            }

            var pointer = checked(vector.DataPosition + (index * sizeof(uint)));
            return ReadTable(FollowForwardOffset(pointer, "table vector element"));
        }

        public (int PointerLocation, TableLayout Table) ReadRequiredTable(
            TableLayout table,
            int fieldId,
            string label)
        {
            var field = ReadFieldLocation(table, fieldId, sizeof(uint));
            if (field is null)
            {
                throw new InvalidDataException($"The habitat FlatBuffer is missing its required {label} table.");
            }

            return (field.Value, ReadTable(FollowForwardOffset(field.Value, label)));
        }

        public int ReadInt32(TableLayout table, int fieldId)
        {
            var field = ReadFieldLocation(table, fieldId, sizeof(int));
            return field is null
                ? 0
                : BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(field.Value, sizeof(int)));
        }

        public bool ReadBoolean(TableLayout table, int fieldId)
        {
            var field = ReadFieldLocation(table, fieldId, sizeof(byte));
            if (field is null)
            {
                return false;
            }

            return bytes[field.Value] switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("The habitat FlatBuffer contains a noncanonical boolean value."),
            };
        }

        private TableLayout ReadTable(int position)
        {
            RequireRange(position, sizeof(int));
            var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, sizeof(int)));
            var vtablePosition = checked(position - vtableDistance);
            RequireRange(vtablePosition, 4);
            var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtablePosition, sizeof(ushort)));
            var objectLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtablePosition + 2, sizeof(ushort)));
            if (vtableLength < 4
                || (vtableLength & 1) != 0
                || vtableLength > MaximumVTableBytes
                || objectLength < sizeof(int)
                || objectLength > MaximumObjectBytes)
            {
                throw new InvalidDataException("The habitat FlatBuffer contains an invalid table layout.");
            }

            RequireRange(vtablePosition, vtableLength);
            RequireRange(position, objectLength);
            return new TableLayout(position, vtablePosition, vtableLength, objectLength);
        }

        private int? ReadFieldLocation(TableLayout table, int fieldId, int fieldSize)
        {
            if (fieldId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldId));
            }

            var entryOffset = checked(4 + (fieldId * sizeof(ushort)));
            if (entryOffset + sizeof(ushort) > table.VTableLength)
            {
                return null;
            }

            var objectOffset = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(table.VTablePosition + entryOffset, sizeof(ushort)));
            if (objectOffset == 0)
            {
                return null;
            }

            if (objectOffset < sizeof(int) || objectOffset > table.ObjectLength - fieldSize)
            {
                throw new InvalidDataException("The habitat FlatBuffer contains an invalid table field offset.");
            }

            return checked(table.Position + objectOffset);
        }

        private int FollowForwardOffset(int pointerPosition, string label)
        {
            var relative = ReadUInt32At(pointerPosition);
            if (relative == 0 || relative > int.MaxValue)
            {
                throw new InvalidDataException($"The habitat FlatBuffer {label} offset is invalid.");
            }

            var target = checked(pointerPosition + (int)relative);
            RequireRange(target, 1);
            return target;
        }

        private uint ReadUInt32At(int position)
        {
            RequireRange(position, sizeof(uint));
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, sizeof(uint)));
        }

        private void RequireRange(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length - length)
            {
                throw new InvalidDataException("The habitat FlatBuffer is truncated or contains an out-of-range reference.");
            }
        }
    }
}
