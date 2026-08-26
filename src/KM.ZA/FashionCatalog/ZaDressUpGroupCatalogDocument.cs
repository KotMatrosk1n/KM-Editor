// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using Google.FlatBuffers;
using KM.Formats.ZA.Generated.FashionCatalog;

namespace KM.ZA.FashionCatalog;

internal sealed class ZaDressUpGroupCatalogDocument
{
    private const int RootFieldCount = 1;
    private const int RowFieldCount = 3;

    public ZaDressUpGroupCatalogDocument(IReadOnlyList<ZaDressUpGroupCatalogDataRow> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<ZaDressUpGroupCatalogDataRow> Rows { get; }

    public static ZaDressUpGroupCatalogDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < sizeof(int))
        {
            throw new InvalidDataException("The Z-A dress-up group catalog is truncated.");
        }

        try
        {
            var root = ZaDressUpGroupCatalogArray.GetRootAsZaDressUpGroupCatalogArray(
                new ByteBuffer(bytes));
            ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                bytes,
                root.TablePosition,
                RootFieldCount,
                "Z-A dress-up group catalog root");
            if (!root.HasEntries)
            {
                throw new InvalidDataException(
                    "The Z-A dress-up group catalog has no physical-row vector.");
            }

            ZaFashionCatalogFlatBufferSupport.EnsureCount(
                root.EntriesLength,
                "Z-A dress-up group catalog");
            var rows = new List<ZaDressUpGroupCatalogDataRow>(root.EntriesLength);
            for (var index = 0; index < root.EntriesLength; index++)
            {
                var source = root.Entries(index)
                    ?? throw new InvalidDataException(
                        $"The Z-A dress-up group catalog contains a null physical row at index {index}.");
                ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                    bytes,
                    source.TablePosition,
                    RowFieldCount,
                    $"Z-A dress-up group row {index}");
                EnsureRequiredString(source.HasModelPart, source.ModelPart, index, "model part");
                EnsureRequiredString(source.HasDisplayLabel, source.DisplayLabel, index, "display label");
                rows.Add(new ZaDressUpGroupCatalogDataRow(
                    source.HasModelPart,
                    source.ModelPart,
                    source.HasDisplayOrder,
                    source.DisplayOrder,
                    source.HasDisplayLabel,
                    source.DisplayLabel));
            }

            return new ZaDressUpGroupCatalogDocument(rows);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                "The Z-A dress-up group catalog is not a supported complete FlatBuffer.",
                exception);
        }
    }

    public ZaDressUpGroupCatalogDocument Replace(int physicalIndex, ZaDressUpGroupCatalogDataRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if ((uint)physicalIndex >= (uint)Rows.Count)
        {
            throw new InvalidDataException(
                $"Dress-up group physical index {physicalIndex} is outside the loaded catalog.");
        }

        var rows = Rows.ToArray();
        rows[physicalIndex] = row;
        return new ZaDressUpGroupCatalogDocument(rows);
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var rowOffsets = Rows.Select(row => row.Write(builder).Value).ToArray();
        var rowVector = ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, rowOffsets);
        builder.StartTable(RootFieldCount);
        builder.AddOffset(0, rowVector.Value, 0);
        var root = new Offset<ZaDressUpGroupCatalogArray>(builder.EndTable());
        ZaDressUpGroupCatalogArray.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static void EnsureRequiredString(
        bool present,
        string? value,
        int physicalIndex,
        string label)
    {
        if (!present || value is null)
        {
            throw new InvalidDataException(
                $"Z-A dress-up group row {physicalIndex} has no required {label}.");
        }

        ZaFashionCatalogFlatBufferSupport.ValidateOptionalText(
            value,
            $"Z-A dress-up group row {physicalIndex} {label}");
    }
}

internal sealed record ZaDressUpGroupCatalogDataRow(
    bool HasModelPart,
    string? ModelPart,
    bool HasDisplayOrder,
    uint DisplayOrder,
    bool HasDisplayLabel,
    string? DisplayLabel)
{
    public string CreateRevision() =>
        ZaFashionCatalogFlatBufferSupport.CreateRowRevision("dress-up-group", AppendTo);

    public Offset<ZaDressUpGroupCatalogEntry> Write(FlatBufferBuilder builder)
    {
        var modelPartOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasModelPart,
            ModelPart,
            "dress-up group model part");
        var displayLabelOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasDisplayLabel,
            DisplayLabel,
            "dress-up group display label");
        builder.StartTable(3);
        if (HasDisplayLabel)
        {
            builder.AddOffset(2, displayLabelOffset.Value, 0);
        }

        ZaFashionCatalogFlatBufferSupport.AddUInt(builder, 1, DisplayOrder, HasDisplayOrder);
        if (HasModelPart)
        {
            builder.AddOffset(0, modelPartOffset.Value, 0);
        }

        return new Offset<ZaDressUpGroupCatalogEntry>(builder.EndTable());
    }

    private void AppendTo(IncrementalHash hash)
    {
        ZaFashionCatalogFlatBufferSupport.Append(hash, HasModelPart);
        ZaFashionCatalogFlatBufferSupport.Append(hash, ModelPart);
        ZaFashionCatalogFlatBufferSupport.Append(hash, HasDisplayOrder);
        ZaFashionCatalogFlatBufferSupport.Append(hash, DisplayOrder);
        ZaFashionCatalogFlatBufferSupport.Append(hash, HasDisplayLabel);
        ZaFashionCatalogFlatBufferSupport.Append(hash, DisplayLabel);
    }
}
