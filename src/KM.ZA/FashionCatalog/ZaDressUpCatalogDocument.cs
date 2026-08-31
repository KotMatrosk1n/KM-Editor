// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using Google.FlatBuffers;
using KM.Formats.ZA.Generated.FashionCatalog;

namespace KM.ZA.FashionCatalog;

internal sealed class ZaDressUpCatalogDocument
{
    private const int RootFieldCount = 1;
    private const int RowFieldCount = 13;

    public ZaDressUpCatalogDocument(IReadOnlyList<ZaDressUpCatalogDataRow> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<ZaDressUpCatalogDataRow> Rows { get; }

    public static ZaDressUpCatalogDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < sizeof(int))
        {
            throw new InvalidDataException("The Z-A dress-up item catalog is truncated.");
        }

        try
        {
            var root = ZaDressUpCatalogArray.GetRootAsZaDressUpCatalogArray(new ByteBuffer(bytes));
            ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                bytes,
                root.TablePosition,
                RootFieldCount,
                "Z-A dress-up item catalog root");
            if (!root.HasEntries)
            {
                throw new InvalidDataException(
                    "The Z-A dress-up item catalog has no physical-row vector.");
            }

            ZaFashionCatalogFlatBufferSupport.EnsureCount(
                root.EntriesLength,
                "Z-A dress-up item catalog");
            var rows = new List<ZaDressUpCatalogDataRow>(root.EntriesLength);
            for (var index = 0; index < root.EntriesLength; index++)
            {
                var source = root.Entries(index)
                    ?? throw new InvalidDataException(
                        $"The Z-A dress-up item catalog contains a null physical row at index {index}.");
                ZaFashionCatalogFlatBufferSupport.EnsureKnownFields(
                    bytes,
                    source.TablePosition,
                    RowFieldCount,
                    $"Z-A dress-up item row {index}");
                EnsureRequiredString(source.HasModelPart, source.ModelPart, index, "model part");
                EnsureRequiredString(source.HasModelVariant, source.ModelVariant, index, "model variant");
                EnsureRequiredString(
                    source.HasPrimaryColorLabel,
                    source.PrimaryColorLabel,
                    index,
                    "primary color label");
                EnsureRequiredString(
                    source.HasSecondaryColorLabel,
                    source.SecondaryColorLabel,
                    index,
                    "secondary color label");
                ZaFashionCatalogFlatBufferSupport.ValidateOptionalText(
                    source.FootwearSubtype,
                    $"Z-A dress-up item row {index} footwear subtype");

                rows.Add(new ZaDressUpCatalogDataRow(
                    source.HasItemId,
                    source.ItemId,
                    source.HasModelPart,
                    source.ModelPart,
                    source.HasCatalogGroupCode,
                    source.CatalogGroupCode,
                    source.HasModelVariant,
                    source.ModelVariant,
                    source.HasCategoryCode,
                    source.CategoryCode,
                    source.HasColorVariantCode,
                    source.ColorVariantCode,
                    source.HasPrimaryColorLabel,
                    source.PrimaryColorLabel,
                    source.HasSecondaryColorLabel,
                    source.SecondaryColorLabel,
                    source.HasReservedFlagA,
                    source.ReservedFlagA,
                    source.HasPrice,
                    source.Price,
                    source.HasUiIndex,
                    source.UiIndex,
                    source.HasFootwearSubtype,
                    source.FootwearSubtype,
                    source.HasReservedFlagB,
                    source.ReservedFlagB));
            }

            return new ZaDressUpCatalogDocument(rows);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                "The Z-A dress-up item catalog is not a supported complete FlatBuffer.",
                exception);
        }
    }

    public ZaDressUpCatalogDocument Replace(int physicalIndex, ZaDressUpCatalogDataRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if ((uint)physicalIndex >= (uint)Rows.Count)
        {
            throw new InvalidDataException(
                $"Dress-up item physical index {physicalIndex} is outside the loaded catalog.");
        }

        var rows = Rows.ToArray();
        rows[physicalIndex] = row;
        return new ZaDressUpCatalogDocument(rows);
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var rowOffsets = Rows.Select(row => row.Write(builder).Value).ToArray();
        var rowVector = ZaFashionCatalogFlatBufferSupport.CreateOffsetVector(builder, rowOffsets);
        builder.StartTable(RootFieldCount);
        builder.AddOffset(0, rowVector.Value, 0);
        var root = new Offset<ZaDressUpCatalogArray>(builder.EndTable());
        ZaDressUpCatalogArray.FinishBuffer(builder, root);
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
                $"Z-A dress-up item row {physicalIndex} has no required {label}.");
        }

        ZaFashionCatalogFlatBufferSupport.ValidateOptionalText(
            value,
            $"Z-A dress-up item row {physicalIndex} {label}");
    }
}

internal sealed record ZaDressUpCatalogDataRow(
    bool HasItemId,
    uint ItemId,
    bool HasModelPart,
    string? ModelPart,
    bool HasCatalogGroupCode,
    uint CatalogGroupCode,
    bool HasModelVariant,
    string? ModelVariant,
    bool HasCategoryCode,
    uint CategoryCode,
    bool HasColorVariantCode,
    uint ColorVariantCode,
    bool HasPrimaryColorLabel,
    string? PrimaryColorLabel,
    bool HasSecondaryColorLabel,
    string? SecondaryColorLabel,
    bool HasReservedFlagA,
    bool ReservedFlagA,
    bool HasPrice,
    uint Price,
    bool HasUiIndex,
    uint UiIndex,
    bool HasFootwearSubtype,
    string? FootwearSubtype,
    bool HasReservedFlagB,
    bool ReservedFlagB)
{
    public string CreateRevision() =>
        ZaFashionCatalogFlatBufferSupport.CreateRowRevision("dress-up-item", AppendTo);

    public Offset<ZaDressUpCatalogEntry> Write(FlatBufferBuilder builder)
    {
        var modelPartOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasModelPart,
            ModelPart,
            "dress-up model part");
        var modelVariantOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasModelVariant,
            ModelVariant,
            "dress-up model variant");
        var primaryColorOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasPrimaryColorLabel,
            PrimaryColorLabel,
            "dress-up primary color label");
        var secondaryColorOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasSecondaryColorLabel,
            SecondaryColorLabel,
            "dress-up secondary color label");
        var footwearSubtypeOffset = ZaFashionCatalogFlatBufferSupport.CreatePresentString(
            builder,
            HasFootwearSubtype,
            FootwearSubtype,
            "dress-up footwear subtype");

        builder.StartTable(13);
        ZaFashionCatalogFlatBufferSupport.AddBool(builder, 12, ReservedFlagB, HasReservedFlagB);
        if (HasFootwearSubtype)
        {
            builder.AddOffset(11, footwearSubtypeOffset.Value, 0);
        }

        ZaFashionCatalogFlatBufferSupport.AddUInt(builder, 10, UiIndex, HasUiIndex);
        ZaFashionCatalogFlatBufferSupport.AddUInt(builder, 9, Price, HasPrice);
        ZaFashionCatalogFlatBufferSupport.AddBool(builder, 8, ReservedFlagA, HasReservedFlagA);
        if (HasSecondaryColorLabel)
        {
            builder.AddOffset(7, secondaryColorOffset.Value, 0);
        }

        if (HasPrimaryColorLabel)
        {
            builder.AddOffset(6, primaryColorOffset.Value, 0);
        }

        ZaFashionCatalogFlatBufferSupport.AddUInt(
            builder,
            5,
            ColorVariantCode,
            HasColorVariantCode);
        ZaFashionCatalogFlatBufferSupport.AddUInt(builder, 4, CategoryCode, HasCategoryCode);
        if (HasModelVariant)
        {
            builder.AddOffset(3, modelVariantOffset.Value, 0);
        }

        ZaFashionCatalogFlatBufferSupport.AddUInt(
            builder,
            2,
            CatalogGroupCode,
            HasCatalogGroupCode);
        if (HasModelPart)
        {
            builder.AddOffset(1, modelPartOffset.Value, 0);
        }

        ZaFashionCatalogFlatBufferSupport.AddUInt(builder, 0, ItemId, HasItemId);
        return new Offset<ZaDressUpCatalogEntry>(builder.EndTable());
    }

    private void AppendTo(IncrementalHash hash)
    {
        Append(hash, HasItemId, ItemId);
        Append(hash, HasModelPart, ModelPart);
        Append(hash, HasCatalogGroupCode, CatalogGroupCode);
        Append(hash, HasModelVariant, ModelVariant);
        Append(hash, HasCategoryCode, CategoryCode);
        Append(hash, HasColorVariantCode, ColorVariantCode);
        Append(hash, HasPrimaryColorLabel, PrimaryColorLabel);
        Append(hash, HasSecondaryColorLabel, SecondaryColorLabel);
        Append(hash, HasReservedFlagA, ReservedFlagA);
        Append(hash, HasPrice, Price);
        Append(hash, HasUiIndex, UiIndex);
        Append(hash, HasFootwearSubtype, FootwearSubtype);
        Append(hash, HasReservedFlagB, ReservedFlagB);
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
}
