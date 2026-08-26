// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using Google.FlatBuffers;
using KM.Formats.ZA.Generated.FashionCatalog;

namespace KM.ZA.FashionCatalog;

internal sealed class ZaHairAndMakeupCatalogDocument
{
    private const int RootFieldCount = 1;
    private const int RowFieldCount = 9;

    public ZaHairAndMakeupCatalogDocument(IReadOnlyList<ZaHairAndMakeupCatalogDataRow> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<ZaHairAndMakeupCatalogDataRow> Rows { get; }

    public static ZaHairAndMakeupCatalogDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < sizeof(int))
        {
            throw new InvalidDataException("The Z-A hair and makeup catalog is truncated.");
        }

        try
        {
            var root = ZaHairAndMakeupCatalogArray.GetRootAsZaHairAndMakeupCatalogArray(
                new ByteBuffer(bytes));
            ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                bytes,
                root.TablePosition,
                RootFieldCount,
                "Z-A hair and makeup catalog root");
            if (!root.HasEntries)
            {
                throw new InvalidDataException(
                    "The Z-A hair and makeup catalog has no physical-row vector.");
            }

            ZaFashionCatalogFlatBufferSupport.EnsureCount(
                root.EntriesLength,
                "Z-A hair and makeup catalog");
            var rows = new List<ZaHairAndMakeupCatalogDataRow>(root.EntriesLength);
            for (var index = 0; index < root.EntriesLength; index++)
            {
                var source = root.Entries(index)
                    ?? throw new InvalidDataException(
                        $"The Z-A hair and makeup catalog contains a null physical row at index {index}.");
                ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                    bytes,
                    source.TablePosition,
                    RowFieldCount,
                    $"Z-A hair and makeup row {index}");
                EnsureRequiredString(source.HasModelKey, source.ModelKey, index, "model key");
                ZaFashionCatalogFlatBufferSupport.ValidateOptionalText(
                    source.ColorValue,
                    $"Z-A hair and makeup row {index} color value");
                ZaFashionCatalogFlatBufferSupport.ValidateOptionalText(
                    source.LabelKey,
                    $"Z-A hair and makeup row {index} label key");
                rows.Add(new ZaHairAndMakeupCatalogDataRow(
                    source.HasItemId,
                    source.ItemId,
                    source.HasModelKey,
                    source.ModelKey,
                    source.HasCatalogTypeCode,
                    source.CatalogTypeCode,
                    source.HasReservedFlag,
                    source.ReservedFlag,
                    source.HasColorValue,
                    source.ColorValue,
                    source.HasLabelKey,
                    source.LabelKey,
                    source.HasDisplayOrder,
                    source.DisplayOrder,
                    source.HasGroupCode,
                    source.GroupCode,
                    source.HasVariantCode,
                    source.VariantCode));
            }

            return new ZaHairAndMakeupCatalogDocument(rows);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                "The Z-A hair and makeup catalog is not a supported complete FlatBuffer.",
                exception);
        }
    }

    public ZaHairAndMakeupCatalogDocument Replace(
        int physicalIndex,
        ZaHairAndMakeupCatalogDataRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if ((uint)physicalIndex >= (uint)Rows.Count)
        {
            throw new InvalidDataException(
                $"Hair and makeup physical index {physicalIndex} is outside the loaded catalog.");
        }

        var rows = Rows.ToArray();
        rows[physicalIndex] = row;
        return new ZaHairAndMakeupCatalogDocument(rows);
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var rowOffsets = Rows.Select(row => row.Write(builder).Value).ToArray();
        var rowVector = ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, rowOffsets);
        builder.StartTable(RootFieldCount);
        builder.AddOffset(0, rowVector.Value, 0);
        var root = new Offset<ZaHairAndMakeupCatalogArray>(builder.EndTable());
        ZaHairAndMakeupCatalogArray.FinishBuffer(builder, root);
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
                $"Z-A hair and makeup row {physicalIndex} has no required {label}.");
        }

        ZaFashionCatalogFlatBufferSupport.ValidateOptionalText(
            value,
            $"Z-A hair and makeup row {physicalIndex} {label}");
    }
}

internal sealed record ZaHairAndMakeupCatalogDataRow(
    bool HasItemId,
    uint ItemId,
    bool HasModelKey,
    string? ModelKey,
    bool HasCatalogTypeCode,
    uint CatalogTypeCode,
    bool HasReservedFlag,
    bool ReservedFlag,
    bool HasColorValue,
    string? ColorValue,
    bool HasLabelKey,
    string? LabelKey,
    bool HasDisplayOrder,
    uint DisplayOrder,
    bool HasGroupCode,
    int GroupCode,
    bool HasVariantCode,
    int VariantCode)
{
    public string CreateRevision() =>
        ZaFashionCatalogFlatBufferSupport.CreateRowRevision("hair-and-makeup", AppendTo);

    public Offset<ZaHairAndMakeupCatalogEntry> Write(FlatBufferBuilder builder)
    {
        var modelKeyOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasModelKey,
            ModelKey,
            "hair and makeup model key");
        var colorOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasColorValue,
            ColorValue,
            "hair and makeup color value");
        var labelOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasLabelKey,
            LabelKey,
            "hair and makeup label key");
        builder.StartTable(9);
        ZaFashionCatalogFlatBufferSupport.AddInt(builder, 8, VariantCode, HasVariantCode);
        ZaFashionCatalogFlatBufferSupport.AddInt(builder, 7, GroupCode, HasGroupCode);
        ZaFashionCatalogFlatBufferSupport.AddUInt(builder, 6, DisplayOrder, HasDisplayOrder);
        if (HasLabelKey)
        {
            builder.AddOffset(5, labelOffset.Value, 0);
        }

        if (HasColorValue)
        {
            builder.AddOffset(4, colorOffset.Value, 0);
        }

        ZaFashionCatalogFlatBufferSupport.AddBool(builder, 3, ReservedFlag, HasReservedFlag);
        ZaFashionCatalogFlatBufferSupport.AddUInt(
            builder,
            2,
            CatalogTypeCode,
            HasCatalogTypeCode);
        if (HasModelKey)
        {
            builder.AddOffset(1, modelKeyOffset.Value, 0);
        }

        ZaFashionCatalogFlatBufferSupport.AddUInt(builder, 0, ItemId, HasItemId);
        return new Offset<ZaHairAndMakeupCatalogEntry>(builder.EndTable());
    }

    private void AppendTo(IncrementalHash hash)
    {
        Append(hash, HasItemId, ItemId);
        Append(hash, HasModelKey, ModelKey);
        Append(hash, HasCatalogTypeCode, CatalogTypeCode);
        Append(hash, HasReservedFlag, ReservedFlag);
        Append(hash, HasColorValue, ColorValue);
        Append(hash, HasLabelKey, LabelKey);
        Append(hash, HasDisplayOrder, DisplayOrder);
        Append(hash, HasGroupCode, GroupCode);
        Append(hash, HasVariantCode, VariantCode);
    }

    private static void Append(IncrementalHash hash, bool present, string? value)
    {
        ZaFashionCatalogFlatBufferSupport.Append(hash, present);
        ZaFashionCatalogFlatBufferSupport.Append(hash, value);
    }

    private static void Append(IncrementalHash hash, bool present, bool value)
    {
        ZaFashionCatalogFlatBufferSupport.Append(hash, present);
        ZaFashionCatalogFlatBufferSupport.Append(hash, value);
    }

    private static void Append(IncrementalHash hash, bool present, uint value)
    {
        ZaFashionCatalogFlatBufferSupport.Append(hash, present);
        ZaFashionCatalogFlatBufferSupport.Append(hash, value);
    }

    private static void Append(IncrementalHash hash, bool present, int value)
    {
        ZaFashionCatalogFlatBufferSupport.Append(hash, present);
        ZaFashionCatalogFlatBufferSupport.Append(hash, value);
    }
}
