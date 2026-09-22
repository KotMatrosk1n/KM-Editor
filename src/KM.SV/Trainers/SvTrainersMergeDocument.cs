// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.SV.Trainers;

public static class SvTrainersMergeDocument
{
    public static MergeDocument Read(byte[] bytes) => SvTrainersEditSessionService.ReadMergeDocument(bytes);
}

internal sealed partial class SvTrainersEditSessionService
{
    internal static MergeDocument ReadMergeDocument(byte[] bytes)
    {
        var table = global::trainer.TrdataMainArray.GetRootAsTrdataMainArray(new ByteBuffer(bytes));
        KM.Formats.FlatBufferMergeGuard.Validate(table);
        var rows = ReadRows(bytes);
        if (rows.Count is <= 0 or > 50_000) throw new InvalidDataException("Invalid table row count.");
        var document = MergeRowDocument.Create(rows, WriteRows, "trainers");
        KM.Formats.FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }
}
