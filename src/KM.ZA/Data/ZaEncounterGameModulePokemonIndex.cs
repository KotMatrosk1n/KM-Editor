// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Data;

internal static class ZaEncounterGameModulePokemonIndex
{
    public static IReadOnlyDictionary<string, ZaPokemonDataEntry> Parse(
        byte[] bytes,
        int? maximumTableRecords = null,
        int? maximumNestedRecords = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var table = ZaEncounterDataDbArray.GetRootAsZaEncounterDataDbArray(new ByteBuffer(bytes));
        ValidateBounds(table, maximumTableRecords, maximumNestedRecords);

        var rowsById = new Dictionary<string, ZaPokemonDataEntry>(StringComparer.Ordinal);
        var sourceIndex = 0;
        for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
        {
            var db = table.Values(groupIndex);
            if (db is null)
            {
                continue;
            }

            for (var rowIndex = 0; rowIndex < db.Value.RootLength; rowIndex++)
            {
                var row = db.Value.Root(rowIndex);
                if (row is { } value)
                {
                    var id = value.Id;
                    if (!string.IsNullOrWhiteSpace(id)
                        && !rowsById.TryAdd(
                            id,
                            new ZaPokemonDataEntry
                            {
                                SourceIndex = sourceIndex,
                                Id = id,
                                DevNo = value.DevNo,
                                MinLevel = value.MinLevel,
                                MaxLevel = value.MaxLevel,
                                FormNo = value.FormNo,
                                OyabunProbability = value.OyabunProbability,
                                OyabunAdditionalLevel = value.OyabunAdditionalLevel,
                            }))
                    {
                        throw new InvalidDataException(
                            "The Z-A encounter PokemonData table contains a duplicate nonblank identity.");
                    }
                }

                sourceIndex++;
            }
        }

        return rowsById;
    }

    private static void ValidateBounds(
        ZaEncounterDataDbArray table,
        int? maximumTableRecords,
        int? maximumNestedRecords)
    {
        EnsureBounded(
            table.ValuesLength,
            maximumTableRecords,
            "The Z-A encounter group table");
        var nestedBudget = new ZaPokemonDataRecordBudget(maximumNestedRecords);
        for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
        {
            var db = table.Values(groupIndex);
            if (db is null)
            {
                continue;
            }

            EnsureBounded(
                db.Value.RootLength,
                maximumTableRecords,
                "A Z-A encounter group");
            nestedBudget.Add(db.Value.RootLength, "The Z-A encounter rows");
            for (var rowIndex = 0; rowIndex < db.Value.RootLength; rowIndex++)
            {
                var row = db.Value.Root(rowIndex);
                if (row is null)
                {
                    continue;
                }

                nestedBudget.Add(
                    row.Value.ActivationConditionLength,
                    "The Z-A encounter activation-condition vectors");
                nestedBudget.Add(
                    row.Value.ItemDropInfoListLength,
                    "The Z-A encounter item-drop vectors");
                nestedBudget.CountActivationConditionDescendants(
                    row.Value,
                    "The Z-A encounter activation-condition descendants");
                for (var dropIndex = 0;
                     dropIndex < row.Value.ItemDropInfoListLength;
                     dropIndex++)
                {
                    nestedBudget.Add(
                        row.Value.ItemDropInfoList(dropIndex)?.DropConditionListLength ?? 0,
                        "The Z-A encounter drop-condition vectors");
                }
            }
        }
    }

    private static void EnsureBounded(int count, int? maximum, string label)
    {
        if (maximum is not null && (count < 0 || count > maximum.Value))
        {
            throw new InvalidDataException($"{label} exceeds the bounded semantic record limit.");
        }
    }
}
