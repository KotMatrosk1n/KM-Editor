// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShStaticEncounterArchiveTests
{
    [Fact]
    public void WriteRoundTripsStaticEncounterRecords()
    {
        var archive = CreateArchive();

        var parsed = SwShStaticEncounterArchive.Parse(archive.Write());

        Assert.Collection(
            parsed.Encounters,
            encounter =>
            {
                Assert.Equal(0, encounter.Index);
                Assert.Equal(0x1122334455667788UL, encounter.BackgroundFarTypeId);
                Assert.Equal(0x8877665544332211UL, encounter.BackgroundNearTypeId);
                Assert.Equal(new SwShStaticEncounterStats(1, 2, 3, 4, 5, 6), encounter.Evs);
                Assert.Equal(1, encounter.Form);
                Assert.Equal(10, encounter.DynamaxLevel);
                Assert.Equal(123, encounter.Field0A);
                Assert.Equal(0x0102030405060708UL, encounter.EncounterId);
                Assert.Equal(7, encounter.Field0C);
                Assert.True(encounter.CanGigantamax);
                Assert.Equal(234, encounter.HeldItem);
                Assert.Equal(70, encounter.Level);
                Assert.Equal(1, encounter.EncounterScenario);
                Assert.Equal(888, encounter.Species);
                Assert.Equal(2, encounter.ShinyLock);
                Assert.Equal(25, encounter.Nature);
                Assert.Equal(1, encounter.Gender);
                Assert.Equal(new SwShStaticEncounterStats(31, 30, 29, 27, 26, 28), encounter.Ivs);
                Assert.Equal(3, encounter.Ability);
                Assert.Equal([1, 2, 3, 4], encounter.Moves);
            },
            encounter =>
            {
                Assert.Equal(1, encounter.Index);
                Assert.False(encounter.CanGigantamax);
                Assert.Equal(25, encounter.Level);
                Assert.Equal(25, encounter.Species);
                Assert.Equal(new SwShStaticEncounterStats(-1, -1, -1, -1, -1, -1), encounter.Ivs);
                Assert.Equal([0, 0, 0, 0], encounter.Moves);
            });
    }

    [Fact]
    public void WriteEditsUpdatesStaticEncounterFieldsAndFixedIvs()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Species, 25),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Form, 2),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Level, 15),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.HeldItem, 99),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Ability, 2),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Nature, 13),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Gender, 2),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.ShinyLock, 1),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.EncounterScenario, 17),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.DynamaxLevel, 4),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.CanGigantamax, 0),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Move0, 44),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Move3, 99),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.EvHp, 10),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.EvAttack, 20),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.EvDefense, 30),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.EvSpecialAttack, 40),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.EvSpecialDefense, 50),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.EvSpeed, 60),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvHp, 0),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvAttack, 1),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvDefense, 2),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvSpeed, 3),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvSpecialAttack, 4),
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvSpecialDefense, 5),
        ]);

        var encounter = SwShStaticEncounterArchive.Parse(output).Encounters[0];
        Assert.Equal(25, encounter.Species);
        Assert.Equal(2, encounter.Form);
        Assert.Equal(15, encounter.Level);
        Assert.Equal(99, encounter.HeldItem);
        Assert.Equal(2, encounter.Ability);
        Assert.Equal(13, encounter.Nature);
        Assert.Equal(2, encounter.Gender);
        Assert.Equal(1, encounter.ShinyLock);
        Assert.Equal(17, encounter.EncounterScenario);
        Assert.Equal(4, encounter.DynamaxLevel);
        Assert.False(encounter.CanGigantamax);
        Assert.Equal([44, 2, 3, 99], encounter.Moves);
        Assert.Equal(new SwShStaticEncounterStats(10, 20, 30, 40, 50, 60), encounter.Evs);
        Assert.Equal(new SwShStaticEncounterStats(0, 1, 2, 4, 5, 3), encounter.Ivs);
    }

    [Fact]
    public void FlawlessIvCountPresetsRoundTripSentinels()
    {
        var archive = CreateArchive();

        var threePerfectOutput = archive.WriteEdits(
        [
            new SwShStaticEncounterEdit(1, SwShStaticEncounterField.FlawlessIvCount, 3),
        ]);
        var threePerfectEncounter = SwShStaticEncounterArchive.Parse(threePerfectOutput).Encounters[1];
        Assert.Equal(3, SwShStaticEncounterArchive.GetFlawlessIvCount(threePerfectEncounter.Ivs));
        Assert.Equal(new SwShStaticEncounterStats(-4, -1, -1, -1, -1, -1), threePerfectEncounter.Ivs);

        var sixPerfectOutput = SwShStaticEncounterArchive.Parse(threePerfectOutput).WriteEdits(
        [
            new SwShStaticEncounterEdit(1, SwShStaticEncounterField.FlawlessIvCount, 6),
        ]);
        var sixPerfectEncounter = SwShStaticEncounterArchive.Parse(sixPerfectOutput).Encounters[1];
        Assert.Equal(6, SwShStaticEncounterArchive.GetFlawlessIvCount(sixPerfectEncounter.Ivs));
        Assert.Equal(new SwShStaticEncounterStats(31, 31, 31, 31, 31, 31), sixPerfectEncounter.Ivs);

        var randomOutput = SwShStaticEncounterArchive.Parse(sixPerfectOutput).WriteEdits(
        [
            new SwShStaticEncounterEdit(1, SwShStaticEncounterField.FlawlessIvCount, 0),
        ]);
        var randomEncounter = SwShStaticEncounterArchive.Parse(randomOutput).Encounters[1];
        Assert.Equal(0, SwShStaticEncounterArchive.GetFlawlessIvCount(randomEncounter.Ivs));
        Assert.Equal(new SwShStaticEncounterStats(-1, -1, -1, -1, -1, -1), randomEncounter.Ivs);
    }

    [Fact]
    public void WriteEditsRejectsInvalidIvValues()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvAttack, -4)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvHp, 32)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShStaticEncounterEdit(0, SwShStaticEncounterField.FlawlessIvCount, 5)]));
    }

    [Fact]
    public void WriteEditsRejectsInvalidDynamaxLevel()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShStaticEncounterEdit(0, SwShStaticEncounterField.DynamaxLevel, 11)]));
    }

    [Fact]
    public void ParsedArchiveNoOpIsByteIdenticalIncludingTrailingData()
    {
        var original = CreateArchive().Write().Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        var parsed = SwShStaticEncounterArchive.Parse(original);

        var output = parsed.WriteEdits([]);

        Assert.Equal(original, output);
    }

    [Fact]
    public void ParsedArchivePatchesOnlyTheTargetScalarByte()
    {
        var original = CreateArchive().Write().Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        var parsed = SwShStaticEncounterArchive.Parse(original);

        var output = parsed.WriteEdits(
        [
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Level, 71),
        ]);

        Assert.Equal(original.Length, output.Length);
        Assert.Equal(original[^4..], output[^4..]);
        Assert.Single(original.Zip(output), pair => pair.First != pair.Second);
        Assert.Equal(71, SwShStaticEncounterArchive.Parse(output).Encounters[0].Level);
    }

    [Fact]
    public void ParsedArchiveRejectsChangingAnOmittedDefaultField()
    {
        var source = CreateArchive().Write();
        OmitEncounterField(source, encounterIndex: 0, fieldIndex: 15);
        var parsed = SwShStaticEncounterArchive.Parse(source);

        var exception = Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Level, 71),
        ]));

        Assert.Contains("omitted from the source FlatBuffer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IndividualRandomIvEditPreservesMixedIvArray()
    {
        var original = SwShStaticEncounterArchive.Parse(CreateArchive().Write());

        var output = original.WriteEdits(
        [
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.IvAttack, -1),
        ]);

        Assert.Equal(
            new SwShStaticEncounterStats(31, -1, 29, 27, 26, 28),
            SwShStaticEncounterArchive.Parse(output).Encounters[0].Ivs);
    }

    [Theory]
    [InlineData(SwShStaticEncounterField.Level, 0)]
    [InlineData(SwShStaticEncounterField.Level, 101)]
    [InlineData(SwShStaticEncounterField.EncounterScenario, 20)]
    [InlineData(SwShStaticEncounterField.EvHp, 253)]
    [InlineData(SwShStaticEncounterField.Gender, 3)]
    public void WriteEditsRejectsSemanticRangeViolations(SwShStaticEncounterField field, int value)
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShStaticEncounterEdit(0, field, value)]));
    }

    internal static SwShStaticEncounterArchive CreateArchive()
    {
        return new SwShStaticEncounterArchive(
        [
            new SwShStaticEncounterRecord(
                0,
                0x1122334455667788,
                0x8877665544332211,
                new SwShStaticEncounterStats(1, 2, 3, 4, 5, 6),
                1,
                10,
                123,
                0x0102030405060708,
                7,
                true,
                234,
                70,
                1,
                888,
                2,
                25,
                1,
                new SwShStaticEncounterStats(31, 30, 29, 27, 26, 28),
                3,
                [1, 2, 3, 4]),
            new SwShStaticEncounterRecord(
                1,
                0,
                0,
                new SwShStaticEncounterStats(0, 0, 0, 0, 0, 0),
                0,
                0,
                0,
                0x1111111111111111,
                0,
                false,
                0,
                25,
                0,
                25,
                0,
                0,
                0,
                new SwShStaticEncounterStats(-1, -1, -1, -1, -1, -1),
                0,
                [0, 0, 0, 0]),
        ]);
    }

    private static void OmitEncounterField(byte[] data, int encounterIndex, int fieldIndex)
    {
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        var rootVtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rootTableOffset));
        var rootVtableStart = rootTableOffset - rootVtableOffset;
        var vectorFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rootVtableStart + 4));
        var vectorReferenceOffset = rootTableOffset + vectorFieldOffset;
        var vectorOffset = vectorReferenceOffset
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(vectorReferenceOffset)));
        var elementOffset = vectorOffset + sizeof(uint) + (encounterIndex * sizeof(uint));
        var encounterTableOffset = elementOffset
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(elementOffset)));
        var encounterVtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(encounterTableOffset));
        var encounterVtableStart = encounterTableOffset - encounterVtableOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(encounterVtableStart + 4 + (fieldIndex * sizeof(ushort))),
            0);
    }
}
