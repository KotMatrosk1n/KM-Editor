// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.GameModules;

public sealed record ZaStaticMapMarker(
    string SourcePath,
    int PhysicalIndex,
    string? PointType,
    string? PointName,
    float X,
    float Y,
    float Z,
    byte RawField2,
    uint RawField3,
    byte NestedRawField2);

public sealed record ZaStaticMapMarkerCatalog(
    ZaReadOnlyProjectionSource Source,
    string? Name,
    string? NodeType,
    uint RawField2,
    uint RawField3,
    uint RawField5,
    bool RawField6,
    IReadOnlyList<ZaStaticMapMarker> Markers);

public sealed class ZaStaticMapMarkerCatalogService
{
    private readonly ZaWorkflowFileSource fileSource;

    public ZaStaticMapMarkerCatalogService()
        : this(new ZaWorkflowFileSource())
    {
    }

    internal ZaStaticMapMarkerCatalogService(ZaWorkflowFileSource fileSource)
    {
        this.fileSource = fileSource ?? throw new ArgumentNullException(nameof(fileSource));
    }

    public ZaStaticMapMarkerCatalog Load(OpenedProject project)
    {
        ZaReadOnlyProjectionSupport.ValidateProject(project, "Static Map Marker Catalog");
        var source = fileSource.Read(project, ZaDataPaths.StaticMapMarkerCatalog);
        var projection = ZaStaticMapMarkerCatalogParser.Read(
            source.Bytes,
            source.VirtualPath);
        return new ZaStaticMapMarkerCatalog(
            ZaReadOnlyProjectionSupport.ToSource(source),
            projection.Name,
            projection.NodeType,
            projection.RawField2,
            projection.RawField3,
            projection.RawField5,
            projection.RawField6,
            projection.Markers);
    }
}

internal static class ZaStaticMapMarkerCatalogParser
{
    private const int RootFieldCount = 7;
    private const int PointFieldCount = 4;
    private const int NestedPointFieldCount = 3;

    private const int RootNameField = 0;
    private const int RootNodeTypeField = 1;
    private const int RootRawField2 = 2;
    private const int RootRawField3 = 3;
    private const int RootPointsField = 4;
    private const int RootRawField5 = 5;
    private const int RootRawField6 = 6;
    private const int PointTypeField = 0;
    private const int PointDataField = 1;
    private const int PointRawField2 = 2;
    private const int PointRawField3 = 3;
    private const int NestedPointNameField = 0;
    private const int NestedPointLocationField = 1;
    private const int NestedPointRawField2 = 2;

    private const int MaximumPayloadBytes = 16 * 1024 * 1024;
    private const int MaximumPointCount = 16_384;
    private const int MaximumNestedPointBytes = 1024 * 1024;
    private const int MaximumAggregateNestedPointBytes = 16 * 1024 * 1024;
    private const int MaximumStringByteLength = 65_536;
    private const long MaximumAggregateStringBytes = 16L * 1024L * 1024L;

    public static ZaStaticMapMarkerCatalogProjection Read(
        byte[] data,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        try
        {
            var reader = new ZaReadOnlyFlatBufferReader(
                data,
                "Static map marker catalog",
                MaximumPayloadBytes,
                MaximumStringByteLength,
                MaximumAggregateStringBytes);
            var root = reader.ReadRootTable(RootFieldCount, "map marker root");
            var pointTables = reader.ReadRequiredTableVectorField(
                root,
                RootFieldCount,
                RootPointsField,
                MaximumPointCount,
                "map marker entries");
            var markers = new ZaStaticMapMarker[pointTables.Count];
            var aggregateNestedBytes = 0;
            for (var index = 0; index < pointTables.Count; index++)
            {
                var pointTable = pointTables[index];
                var nestedData = reader.ReadRequiredByteVectorField(
                    pointTable,
                    PointFieldCount,
                    PointDataField,
                    MaximumNestedPointBytes,
                    $"map marker {index} nested point data");
                aggregateNestedBytes = checked(aggregateNestedBytes + nestedData.Length);
                if (aggregateNestedBytes > MaximumAggregateNestedPointBytes)
                {
                    throw new InvalidDataException(
                        "Static map marker catalog exceeds its bounded aggregate nested-data byte count.");
                }

                var nestedReader = new ZaReadOnlyFlatBufferReader(
                    nestedData,
                    $"Static map marker {index} nested point",
                    MaximumNestedPointBytes,
                    MaximumStringByteLength,
                    MaximumStringByteLength);
                var nestedPoint = nestedReader.ReadRootTable(
                    NestedPointFieldCount,
                    $"map marker {index} nested point root");
                var location = nestedReader.ReadRequiredVector3Field(
                    nestedPoint,
                    NestedPointFieldCount,
                    NestedPointLocationField,
                    $"map marker {index} location");
                if (!float.IsFinite(location.X)
                    || !float.IsFinite(location.Y)
                    || !float.IsFinite(location.Z))
                {
                    throw new InvalidDataException(
                        $"Static map marker {index} contains a non-finite location component.");
                }

                markers[index] = new ZaStaticMapMarker(
                    sourcePath,
                    index,
                    reader.ReadOptionalStringField(
                        pointTable,
                        PointFieldCount,
                        PointTypeField,
                        $"map marker {index} point type"),
                    nestedReader.ReadOptionalStringField(
                        nestedPoint,
                        NestedPointFieldCount,
                        NestedPointNameField,
                        $"map marker {index} point name"),
                    location.X,
                    location.Y,
                    location.Z,
                    reader.ReadByteField(
                        pointTable,
                        PointFieldCount,
                        PointRawField2,
                        defaultValue: 0,
                        $"map marker {index} raw field 2"),
                    reader.ReadUInt32Field(
                        pointTable,
                        PointFieldCount,
                        PointRawField3,
                        defaultValue: 0,
                        $"map marker {index} raw field 3"),
                    nestedReader.ReadByteField(
                        nestedPoint,
                        NestedPointFieldCount,
                        NestedPointRawField2,
                        defaultValue: 0,
                        $"map marker {index} nested raw field 2"));
            }

            return new ZaStaticMapMarkerCatalogProjection(
                reader.ReadOptionalStringField(
                    root,
                    RootFieldCount,
                    RootNameField,
                    "map marker catalog name"),
                reader.ReadOptionalStringField(
                    root,
                    RootFieldCount,
                    RootNodeTypeField,
                    "map marker catalog node type"),
                reader.ReadUInt32Field(
                    root,
                    RootFieldCount,
                    RootRawField2,
                    defaultValue: 0,
                    "map marker catalog raw field 2"),
                reader.ReadUInt32Field(
                    root,
                    RootFieldCount,
                    RootRawField3,
                    defaultValue: 0,
                    "map marker catalog raw field 3"),
                reader.ReadUInt32Field(
                    root,
                    RootFieldCount,
                    RootRawField5,
                    defaultValue: 0,
                    "map marker catalog raw field 5"),
                reader.ReadBooleanField(
                    root,
                    RootFieldCount,
                    RootRawField6,
                    defaultValue: false,
                    "map marker catalog raw field 6"),
                markers);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Static map marker catalog contains an overflowing FlatBuffer offset or count.",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Static map marker catalog contains invalid UTF-8 text.",
                exception);
        }
    }
}

internal sealed record ZaStaticMapMarkerCatalogProjection(
    string? Name,
    string? NodeType,
    uint RawField2,
    uint RawField3,
    uint RawField5,
    bool RawField6,
    IReadOnlyList<ZaStaticMapMarker> Markers);
