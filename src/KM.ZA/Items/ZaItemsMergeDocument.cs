// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.ZA.Items;

public static class ZaItemsMergeDocument
{
    public static MergeDocument Read(byte[] bytes) => ZaItemsEditSessionService.ReadMergeDocument(bytes);
}

internal sealed partial class ZaItemsEditSessionService
{
    internal static MergeDocument ReadMergeDocument(byte[] bytes)
    {
        var table = KM.Formats.ZA.Generated.GameData.ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(bytes));
        KM.Formats.FlatBufferMergeGuard.Validate(table);
        var rows = ReadRows(bytes);
        if (rows.Count is <= 0 or > 50_000) throw new InvalidDataException("Invalid table row count.");
        var document = MergeRowDocument.Create(rows, WriteRows, "items");
        KM.Formats.FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }
}
