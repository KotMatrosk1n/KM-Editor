// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventureSeedPlannerTests
{
    [Fact]
    public void PredictMatchesObservedGalarMrMimeAndMagmarGoldenRoute()
    {
        const ulong Seed = 0x815EB04D77A44507;
        var archive = CreateGoldenRouteArchive();
        var personal = CreateGoldenRoutePersonalRecords();

        var prediction = SwShDynamaxAdventureSeedPlanner.Predict(
            Seed,
            npcCount: 3,
            archive.Entries,
            personal);

        Assert.Equal(273, archive.Entries.Count);
        Assert.Equal(273, archive.Entries.Select(entry => (entry.Species, entry.Form)).Distinct().Count());
        Assert.Equal((122, 1), (archive.Entries[46].Species, archive.Entries[46].Form));
        Assert.Equal((126, 0), (archive.Entries[50].Species, archive.Entries[50].Form));
        Assert.True(prediction.Encounters[0].HasTwoTypes);
        Assert.Equal(46, prediction.Encounters[0].Row);
        Assert.Equal(122, prediction.Encounters[0].Species);
        Assert.Equal(1, prediction.Encounters[0].Form);
        Assert.Equal(50, prediction.Encounters[1].Row);
        Assert.Equal(126, prediction.Encounters[1].Species);
        Assert.Equal(0, prediction.Encounters[1].Form);
    }

    [Fact]
    public void PredictUsesEditedTableRowsForSeedSelectedPokemon()
    {
        const ulong Seed = 0x815EB04D77A44507;
        var archive = CreateArchive(rowCount: 240);
        var personal = CreatePersonalRecords(count: 800);
        var vanillaPrediction = SwShDynamaxAdventureSeedPlanner.Predict(
            Seed,
            npcCount: 3,
            archive.Entries,
            personal);
        var targetRow = vanillaPrediction.Encounters[1].Row;
        var editedEntries = archive.Entries
            .Select(entry => entry.EntryIndex == targetRow
                ? entry with { Species = 467 }
                : entry)
            .ToArray();

        var editedPrediction = SwShDynamaxAdventureSeedPlanner.Predict(
            Seed,
            npcCount: 3,
            editedEntries,
            personal);
        var positions = SwShDynamaxAdventureSeedPlanner.GetRowPositions(editedPrediction, [targetRow]);

        Assert.Equal(targetRow, editedPrediction.Encounters[1].Row);
        Assert.Equal(467, editedPrediction.Encounters[1].Species);
        var position = Assert.Single(positions);
        Assert.Equal(SwShDynamaxAdventureSeedSlotKind.Encounter, position.Kind);
        Assert.Equal(1, position.Slot);
    }

    [Fact]
    public void SearchRowsFindsSeedForRequiredEditedRows()
    {
        const ulong Seed = 0x815EB04D77A44507;
        var archive = CreateArchive(rowCount: 240);
        var personal = CreatePersonalRecords(count: 800);
        var prediction = SwShDynamaxAdventureSeedPlanner.Predict(
            Seed,
            npcCount: 3,
            archive.Entries,
            personal);
        var requiredRows = new[]
        {
            prediction.Encounters[0].Row,
            prediction.Encounters[1].Row,
        };

        var results = SwShDynamaxAdventureSeedPlanner.SearchRows(
            archive.Entries,
            personal,
            requiredRows,
            npcCount: 3,
            startSeed: Seed,
            limit: 1,
            maxResults: 1);

        var result = Assert.Single(results);
        Assert.Equal(Seed, result.Seed);
        Assert.Equal(2, result.Positions.Count);
        Assert.Contains(result.Positions, position =>
            position.Row == requiredRows[0]
            && position.Kind == SwShDynamaxAdventureSeedSlotKind.Encounter
            && position.Slot == 0);
        Assert.Contains(result.Positions, position =>
            position.Row == requiredRows[1]
            && position.Kind == SwShDynamaxAdventureSeedSlotKind.Encounter
            && position.Slot == 1);
    }

    [Fact]
    public void PredictBurnsExtraRngValueAfterDualTypeEncounter()
    {
        var archive = CreateArchive(rowCount: 240);
        var singleTypePersonal = CreatePersonalRecords(count: 800);

        for (ulong seed = 0; seed < 1000; seed++)
        {
            var singleTypePrediction = SwShDynamaxAdventureSeedPlanner.Predict(
                seed,
                npcCount: 3,
                archive.Entries,
                singleTypePersonal);
            var firstEncounter = singleTypePrediction.Encounters[0];
            var dualTypePersonal = CreatePersonalRecords(
                count: 800,
                dualTypeSpecies: firstEncounter.Species);
            var dualTypePrediction = SwShDynamaxAdventureSeedPlanner.Predict(
                seed,
                npcCount: 3,
                archive.Entries,
                dualTypePersonal);

            if (dualTypePrediction.Encounters[1].Row == singleTypePrediction.Encounters[1].Row)
            {
                continue;
            }

            Assert.Equal(firstEncounter.Row, dualTypePrediction.Encounters[0].Row);
            Assert.NotEqual(singleTypePrediction.Encounters[1].Row, dualTypePrediction.Encounters[1].Row);
            return;
        }

        Assert.Fail("Expected to find a seed whose later encounters change after a dual-type RNG burn.");
    }

    [Fact]
    public void SearchRowsRejectsUnboundedRawRequiredRowsBeforeEnumeration()
    {
        var archive = CreateArchive(rowCount: 240);
        var personal = CreatePersonalRecords(count: 800);

        Assert.Throws<ArgumentOutOfRangeException>(() => SwShDynamaxAdventureSeedPlanner.SearchRows(
            archive.Entries,
            personal,
            Enumerable.Repeat(1, SwShDynamaxAdventureSeedPlanner.MaximumRequiredRows + 1).ToArray(),
            npcCount: 3,
            startSeed: 0,
            limit: 1,
            maxResults: 1));
    }

    [Fact]
    public void SearchRowsRejectsEmptyRequiredRows()
    {
        var archive = CreateArchive(rowCount: 240);
        var personal = CreatePersonalRecords(count: 800);

        Assert.Throws<ArgumentException>(() => SwShDynamaxAdventureSeedPlanner.SearchRows(
            archive.Entries,
            personal,
            requiredRows: [],
            npcCount: 3,
            startSeed: 0,
            limit: 1,
            maxResults: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SearchRowsRejectsNonpositiveResultLimit(int maxResults)
    {
        var archive = CreateArchive(rowCount: 240);
        var personal = CreatePersonalRecords(count: 800);

        Assert.Throws<ArgumentOutOfRangeException>(() => SwShDynamaxAdventureSeedPlanner.SearchRows(
            archive.Entries,
            personal,
            [1],
            npcCount: 3,
            startSeed: 0,
            limit: 1,
            maxResults));
    }

    [Fact]
    public void SearchRowsReusesPredictionScratchAcrossSeedLoop()
    {
        var archive = CreateArchive(rowCount: 240);
        var personal = CreatePersonalRecords(count: 800);
        _ = SwShDynamaxAdventureSeedPlanner.SearchRows(
            archive.Entries,
            personal,
            requiredRows: [239],
            npcCount: 3,
            startSeed: 0,
            limit: 1,
            maxResults: 1);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var results = SwShDynamaxAdventureSeedPlanner.SearchRows(
            archive.Entries,
            personal,
            requiredRows: [239],
            npcCount: 3,
            startSeed: 0,
            limit: 1_000,
            maxResults: 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Empty(results);
        Assert.True(allocated < 200_000, $"Expected bounded scratch reuse, but search allocated {allocated:N0} bytes.");
    }

    private static SwShDynamaxAdventureArchive CreateArchive(int rowCount)
    {
        return new SwShDynamaxAdventureArchive(
            Enumerable.Range(0, rowCount)
                .Select(row => new SwShDynamaxAdventureRecord(
                    row,
                    IsSingleCapture: false,
                    SingleCaptureFlagBlock: (ulong)row,
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: row,
                    Level: 65,
                    Species: row + 1,
                    UiMessageId: (ulong)(row + 1),
                    OtGender: 0,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-2, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4]))
                .ToArray());
    }

    private static SwShDynamaxAdventureArchive CreateGoldenRouteArchive()
    {
        return new SwShDynamaxAdventureArchive(
            Enumerable.Range(0, 273)
                .Select(row => new SwShDynamaxAdventureRecord(
                    row,
                    IsSingleCapture: row >= SwShDynamaxAdventureSeedPlanner.DefaultBossStartRow,
                    SingleCaptureFlagBlock: (ulong)row,
                    Field02: 0,
                    Form: row == 46 ? 1 : 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: row,
                    Level: 65,
                    Species: row switch
                    {
                        45 or 46 => 122,
                        50 => 126,
                        _ => 300 + row,
                    },
                    UiMessageId: (ulong)(row + 1),
                    OtGender: 0,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-2, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4]))
                .ToArray());
    }

    private static IReadOnlyList<SwShPersonalRecord> CreateGoldenRoutePersonalRecords()
    {
        const int galarMrMimePersonalIndex = 900;
        var rows = Enumerable.Range(0, galarMrMimePersonalIndex + 1)
            .Select(_ => CreatePersonalRecord(type1: 0, type2: 0))
            .ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(rows[122].AsSpan(0x1E), galarMrMimePersonalIndex);
        rows[122][0x20] = 2;
        rows[galarMrMimePersonalIndex] = CreatePersonalRecord(type1: 1, type2: 2);
        return SwShPersonalTable.Parse(CreatePersonalTable(rows)).Records;
    }

    private static IReadOnlyList<SwShPersonalRecord> CreatePersonalRecords(
        int count,
        int? dualTypeSpecies = null)
    {
        var rows = Enumerable.Range(0, count)
            .Select(_ => CreatePersonalRecord(type1: 0, type2: 0))
            .ToArray();
        if (dualTypeSpecies is { } species)
        {
            rows[species] = CreatePersonalRecord(type1: 1, type2: 2);
        }

        return SwShPersonalTable.Parse(CreatePersonalTable(rows)).Records;
    }

    private static byte[] CreatePersonalRecord(int type1, int type2)
    {
        var record = new byte[SwShPersonalTable.RecordSize];
        record[0x06] = checked((byte)type1);
        record[0x07] = checked((byte)type2);
        record[0x20] = 1;
        return record;
    }

    private static byte[] CreatePersonalTable(IReadOnlyList<byte[]> records)
    {
        var data = new byte[records.Count * SwShPersonalTable.RecordSize];
        for (var index = 0; index < records.Count; index++)
        {
            records[index].CopyTo(data.AsSpan(index * SwShPersonalTable.RecordSize));
        }

        return data;
    }
}
