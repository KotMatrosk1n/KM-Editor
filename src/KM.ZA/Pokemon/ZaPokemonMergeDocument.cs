// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Pokemon;

public static class ZaPokemonMergeDocument
{
    public static MergeDocument Read(byte[] bytes) => ZaPokemonEditSessionService.ReadMergeDocument(bytes);
}

internal sealed partial class ZaPokemonEditSessionService
{
    internal static MergeDocument ReadMergeDocument(byte[] bytes)
    {
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        KM.Formats.FlatBufferMergeGuard.Validate(table);
        if (table.EntryLength is <= 0 or > 50_000 || table.HasLegacyByteZADexOrderLayout)
            throw new InvalidDataException("Personal table layout requires an explicit complete file choice.");
        var document = MergeRowDocument.Create(ReadTableRows(table), WriteRows, "pokemon");
        KM.Formats.FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }
}
