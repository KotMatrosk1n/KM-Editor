// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;
using KM.Formats;
using KM.Formats.SV.Blueberry.Generated;

namespace KM.SV.Blueberry;

public static class SvBlueberryQuestMergeDocument
{
    private sealed record Row(string Id, sbyte Category, int QuestType, int QuestId, short Goal,
        int RewardBp, int RerollBp, int ReleaseCondition, sbyte Grade, sbyte Difficulty, float Weight);
    public static MergeDocument Read(byte[] bytes)
    {
        var table = BlueberryQuestArray.GetRootAsBlueberryQuestArray(new ByteBuffer(bytes));
        FlatBufferMergeGuard.Validate(table);
        if (table.EntriesLength is < 1 or > 512) throw new InvalidDataException("Unsupported quest count.");
        var rows = Enumerable.Range(0, table.EntriesLength).Select(index =>
        {
            var values = table.Entries(index)?.Values ?? throw new InvalidDataException("Missing quest values.");
            return new Row($"{values.QuestType}:{values.QuestId}:{values.Difficulty}", values.Category, values.QuestType,
                values.QuestId, values.Goal, values.RewardBp, values.RerollBp, values.ReleaseCondition, values.Grade, values.Difficulty, values.Weight);
        }).ToArray();
        if (rows.Select(row => row.Id).Distinct().Count() != rows.Length) throw new InvalidDataException("Duplicate quest identity.");
        var document = MergeRowDocument.Create(rows, selected =>
        {
            var builder = new FlatBufferBuilder(4096);
            var offsets = selected.Select(row =>
            {
                BlueberryQuest.StartBlueberryQuest(builder);
                var values = BlueberryQuestValues.CreateBlueberryQuestValues(builder, row.Category, row.QuestType, row.QuestId, row.Goal,
                    row.RewardBp, row.RerollBp, row.ReleaseCondition, row.Grade, row.Difficulty, row.Weight);
                BlueberryQuest.AddValues(builder, values);
                return BlueberryQuest.EndBlueberryQuest(builder);
            }).ToArray();
            var vector = BlueberryQuestArray.CreateEntriesVector(builder, offsets);
            BlueberryQuestArray.FinishBlueberryQuestArrayBuffer(builder, BlueberryQuestArray.CreateBlueberryQuestArray(builder, vector));
            return builder.SizedByteArray();
        }, "blueberry-quests");
        FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }
}
