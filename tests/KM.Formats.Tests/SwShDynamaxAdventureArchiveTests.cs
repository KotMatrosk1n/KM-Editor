// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShDynamaxAdventureArchiveTests
{
    [Fact]
    public void ParseRoundTripsDynamaxAdventureArchive()
    {
        var archive = CreateArchive();

        var parsed = SwShDynamaxAdventureArchive.Parse(archive.Write());

        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal(0, parsed.Entries[0].EntryIndex);
        Assert.True(parsed.Entries[0].IsSingleCapture);
        Assert.Equal(0x1122334455667788UL, parsed.Entries[0].SingleCaptureFlagBlock);
        Assert.Equal(133, parsed.Entries[0].Species);
        Assert.Equal(1, parsed.Entries[0].Form);
        Assert.Equal(65, parsed.Entries[0].Level);
        Assert.Equal(4, parsed.Entries[0].GuaranteedPerfectIvs());
        Assert.Equal(-1, parsed.Entries[0].Ivs.Attack);
        Assert.Equal(10, parsed.Entries[0].Moves[2]);
        Assert.True(parsed.Entries[0].IsStoryProgressGated);
        Assert.Equal(0x8877665544332211UL, parsed.Entries[0].UiMessageId);
        Assert.Equal(31, parsed.Entries[1].Ivs.SpecialDefense);

        var reparsed = SwShDynamaxAdventureArchive.Parse(parsed.Write());
        Assert.Equal(parsed.Entries.Count, reparsed.Entries.Count);
        Assert.Equal(parsed.Entries[0].Species, reparsed.Entries[0].Species);
        Assert.Equal(parsed.Entries[0].Ivs, reparsed.Entries[0].Ivs);
        Assert.Equal(parsed.Entries[0].Moves, reparsed.Entries[0].Moves);
        Assert.Equal(parsed.Entries[1].UiMessageId, reparsed.Entries[1].UiMessageId);
    }

    [Fact]
    public void WriteEditsAppliesStableDynamaxAdventureFields()
    {
        var archive = CreateArchive();

        var edited = SwShDynamaxAdventureArchive.Parse(archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.Species, 25),
            new(0, SwShDynamaxAdventureField.Form, 2),
            new(0, SwShDynamaxAdventureField.Ability, 2),
            new(0, SwShDynamaxAdventureField.GigantamaxState, 2),
            new(0, SwShDynamaxAdventureField.Version, 2),
            new(0, SwShDynamaxAdventureField.ShinyRoll, 2),
            new(0, SwShDynamaxAdventureField.Move3, 85),
            new(0, SwShDynamaxAdventureField.GuaranteedPerfectIvs, 6),
            new(0, SwShDynamaxAdventureField.IvAttack, 31),
            new(0, SwShDynamaxAdventureField.IsSingleCapture, 0),
            new(0, SwShDynamaxAdventureField.IsStoryProgressGated, 0),
            new(0, SwShDynamaxAdventureField.OtGender, 0),
        ]));

        var entry = edited.Entries[0];
        Assert.Equal(25, entry.Species);
        Assert.Equal(2, entry.Form);
        Assert.Equal(2, entry.Ability);
        Assert.Equal(2, entry.GigantamaxState);
        Assert.Equal(2, entry.Version);
        Assert.Equal(2, entry.ShinyRoll);
        Assert.Equal(85, entry.Moves[3]);
        Assert.Equal(6, entry.GuaranteedPerfectIvs());
        Assert.Equal(31, entry.Ivs.Attack);
        Assert.False(entry.IsSingleCapture);
        Assert.False(entry.IsStoryProgressGated);
        Assert.Equal(0, entry.OtGender);

        Assert.Equal(0x1122334455667788UL, entry.SingleCaptureFlagBlock);
        Assert.Equal(0x8877665544332211UL, entry.UiMessageId);
    }

    [Fact]
    public void WriteEditsRejectsInvalidGuaranteedPerfectIvCount()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.GuaranteedPerfectIvs, 7),
        ]));
    }

    [Fact]
    public void WriteEditsRejectsAmbiguousGuaranteedPerfectIvCount()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.GuaranteedPerfectIvs, 1),
        ]));
    }

    [Fact]
    public void WriteEditsRejectsUnsupportedIvOverrideSentinel()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new(0, SwShDynamaxAdventureField.IvDefense, -2),
        ]));
    }

    private static SwShDynamaxAdventureArchive CreateArchive()
    {
        return new SwShDynamaxAdventureArchive(
        [
            new SwShDynamaxAdventureRecord(
                0,
                IsSingleCapture: true,
                SingleCaptureFlagBlock: 0x1122334455667788UL,
                Field02: 0,
                Form: 1,
                GigantamaxState: 1,
                BallItemId: 4,
                AdventureIndex: 100,
                Level: 65,
                Species: 133,
                UiMessageId: 0x8877665544332211UL,
                OtGender: 1,
                Version: 1,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-4, -1, -1, -1, -1, -1),
                Ability: 1,
                IsStoryProgressGated: true,
                Moves: [1, 2, 10, 20]),
            new SwShDynamaxAdventureRecord(
                1,
                IsSingleCapture: false,
                SingleCaptureFlagBlock: 0x0102030405060708UL,
                Field02: 0,
                Form: 0,
                GigantamaxState: 0,
                BallItemId: 4,
                AdventureIndex: 101,
                Level: 60,
                Species: 25,
                UiMessageId: 0x0807060504030201UL,
                OtGender: 1,
                Version: 0,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-1, 0, 1, 2, 3, 31),
                Ability: 0,
                IsStoryProgressGated: false,
                Moves: [3, 4, 5, 6]),
        ]);
    }
}

internal static class SwShDynamaxAdventureArchiveTestExtensions
{
    public static int GuaranteedPerfectIvs(this SwShDynamaxAdventureRecord entry)
    {
        return SwShDynamaxAdventureArchive.GetGuaranteedPerfectIvCount(entry.Ivs);
    }
}
