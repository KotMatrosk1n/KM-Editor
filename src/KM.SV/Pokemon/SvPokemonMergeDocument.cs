// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.SV.Pokemon;

public static class SvPokemonMergeDocument
{
    public static MergeDocument Read(byte[] bytes) => SvPokemonEditSessionService.ReadMergeDocument(bytes);
}

internal sealed partial class SvPokemonEditSessionService
{
    internal static MergeDocument ReadMergeDocument(byte[] bytes)
    {
        var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(bytes));
        KM.Formats.FlatBufferMergeGuard.Validate(table);
        if (table.EntryLength is <= 0 or > 50_000)
            throw new InvalidDataException("Personal table has an invalid record count.");
        var rows = ReadRows(bytes);
        if (rows.Count != table.EntryLength)
            throw new InvalidDataException("Personal table contains absent physical rows.");
        var document = MergeRowDocument.Create(rows, WriteRows, "pokemon");
        KM.Formats.FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }
}
