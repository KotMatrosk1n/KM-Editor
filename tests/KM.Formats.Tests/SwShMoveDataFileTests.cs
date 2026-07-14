// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShMoveDataFileTests
{
    [Fact]
    public void ParseReadsHandBuiltFlatBufferWithoutUsingWriter()
    {
        var file = SwShMoveDataFile.Parse(CreateHandBuiltMoveBuffer());

        Assert.Equal(0x12345678u, file.Record.Version);
        Assert.Equal(33u, file.Record.MoveId);
        Assert.True(file.Record.CanUseMove);
        Assert.Equal(9, file.Record.Core.Type);
        Assert.Equal(13, file.Record.Core.Quality);
        Assert.Equal(2, file.Record.Core.Category);
        Assert.Equal(120, file.Record.Core.Power);
        Assert.Equal(101, file.Record.Core.Accuracy);
        Assert.Equal(40, file.Record.Core.PP);
        Assert.Equal(-7, file.Record.Core.Priority);
        Assert.Equal(0, file.Record.Targeting.RawTarget);
        Assert.All(file.Record.StatChanges, change => Assert.Equal(0, change.Stat));
        Assert.False(file.Record.Flags.MakesContact);
    }

    [Fact]
    public void ParseReadsMoveDataFields()
    {
        var input = CreateMoveRecord();

        var file = SwShMoveDataFile.Parse(SwShMoveDataFile.Write(input));

        Assert.Equal(1u, file.Record.Version);
        Assert.Equal(33u, file.Record.MoveId);
        Assert.True(file.Record.CanUseMove);
        Assert.Equal(1, file.Record.Core.Type);
        Assert.Equal(2, file.Record.Core.Category);
        Assert.Equal(40, file.Record.Core.Power);
        Assert.Equal(100, file.Record.Core.Accuracy);
        Assert.Equal(35, file.Record.Core.PP);
        Assert.Equal(-1, file.Record.Core.Priority);
        Assert.Equal(130, file.Record.Core.GigantamaxPower);
        Assert.Equal(4, file.Record.Targeting.RawTarget);
        Assert.Equal(1, file.Record.Targeting.HitMin);
        Assert.Equal(2, file.Record.Targeting.HitMax);
        Assert.Equal(1, file.Record.Secondary.Inflict);
        Assert.Equal(10, file.Record.Secondary.InflictPercent);
        Assert.Equal(-25, file.Record.Secondary.Recoil);
        Assert.Equal(-50, file.Record.Secondary.RawHealing);
        Assert.Collection(
            file.Record.StatChanges,
            stat =>
            {
                Assert.Equal(1, stat.Slot);
                Assert.Equal(1, stat.Stat);
                Assert.Equal(-1, stat.Stage);
                Assert.Equal(30, stat.Percent);
            },
            stat =>
            {
                Assert.Equal(2, stat.Slot);
                Assert.Equal(2, stat.Stat);
                Assert.Equal(1, stat.Stage);
                Assert.Equal(40, stat.Percent);
            },
            stat =>
            {
                Assert.Equal(3, stat.Slot);
                Assert.Equal(0, stat.Stat);
            });
        Assert.True(file.Record.Flags.MakesContact);
        Assert.True(file.Record.Flags.Protect);
        Assert.True(file.Record.Flags.Punch);
        Assert.True(file.Record.Flags.Metronome);
        Assert.False(file.Record.Flags.Sound);
    }

    [Fact]
    public void WriteRoundTripsCompleteMoveRecord()
    {
        var input = CreateMoveRecord() with
        {
            Core = CreateMoveRecord().Core with
            {
                Type = 9,
                Category = 1,
                Power = 120,
                Priority = 2,
            },
            Flags = CreateMoveRecord().Flags with
            {
                Sound = true,
                Metronome = false,
            },
        };

        var output = SwShMoveDataFile.Write(input);
        var parsed = SwShMoveDataFile.Parse(output);

        Assert.Equal(input.Version, parsed.Record.Version);
        Assert.Equal(input.MoveId, parsed.Record.MoveId);
        Assert.Equal(input.CanUseMove, parsed.Record.CanUseMove);
        Assert.Equal(input.Core, parsed.Record.Core);
        Assert.Equal(input.Targeting, parsed.Record.Targeting);
        Assert.Equal(input.Secondary, parsed.Record.Secondary);
        Assert.Equal(input.StatChanges, parsed.Record.StatChanges);
        Assert.Equal(input.Flags, parsed.Record.Flags);
    }

    [Fact]
    public void WriteEditedPreservesSourceLayoutAndTrailingBytes()
    {
        var source = CreateHandBuiltMoveBuffer();
        var file = SwShMoveDataFile.Parse(source);
        var edited = file.Record with
        {
            Core = file.Record.Core with { Power = 80 },
        };

        var output = file.WriteEdited(edited);

        var expected = (byte[])source.Clone();
        expected[44] = 80;
        Assert.Equal(expected, output);
        Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], output[^4..]);
    }

    [Fact]
    public void WriteEditedSafelyRebuildsKnownSparseSchemaWhenUnclassifiedBytesAreZero()
    {
        var source = CreateHandBuiltMoveBuffer();
        source.AsSpan(source.Length - 4).Clear();
        var file = SwShMoveDataFile.Parse(source);
        var edited = file.Record with
        {
            Targeting = file.Record.Targeting with { RawTarget = 4 },
        };

        var output = file.WriteEdited(edited);

        Assert.NotEqual(source.Length, output.Length);
        Assert.Equal(4, SwShMoveDataFile.Parse(output).Record.Targeting.RawTarget);
    }

    [Fact]
    public void WriteEditedRejectsSparseActivationThatWouldDiscardOpaqueTrailer()
    {
        var source = CreateHandBuiltMoveBuffer();
        var file = SwShMoveDataFile.Parse(source);
        var edited = file.Record with
        {
            Targeting = file.Record.Targeting with { RawTarget = 4 },
        };

        var exception = Assert.Throws<InvalidDataException>(() => file.WriteEdited(edited));

        Assert.Contains("field 22", exception.Message, StringComparison.Ordinal);
        Assert.Contains("opaque or extended source data", exception.Message, StringComparison.Ordinal);
        Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], source[^4..]);
    }

    [Fact]
    public void ParseRejectsMissingInvalidOrOutOfBoundsRoots()
    {
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse([]));
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(new byte[3]));
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(new byte[4]));

        var outside = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt32LittleEndian(outside, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(outside));

        var unaligned = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt32LittleEndian(unaligned, 29);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(unaligned));
    }

    [Fact]
    public void ParseRejectsInvalidVTableLocationAndLength()
    {
        var missing = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteInt32LittleEndian(missing.AsSpan(28), 0);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(missing));

        var outside = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteInt32LittleEndian(outside.AsSpan(28), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(outside));

        var tooShort = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(tooShort.AsSpan(4), 2);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(tooShort));

        var odd = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(odd.AsSpan(4), 23);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(odd));

        var truncated = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(truncated.AsSpan(4), 54);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(truncated));
    }

    [Fact]
    public void ParseAcceptsForwardSharedVTable()
    {
        var data = CreateForwardVTableMoveBuffer();

        var file = SwShMoveDataFile.Parse(data);
        var output = file.WriteEdited(file.Record with
        {
            Core = file.Record.Core with { Power = 80 },
        });

        Assert.Equal(33u, file.Record.MoveId);
        Assert.Equal(120, file.Record.Core.Power);
        var expected = (byte[])data.Clone();
        expected[20] = 80;
        Assert.Equal(expected, output);
    }

    [Fact]
    public void ParseRejectsInvalidObjectAndTypedFieldExtents()
    {
        var tooSmall = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(tooSmall.AsSpan(6), 3);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(tooSmall));

        var truncated = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(truncated.AsSpan(6), 25);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(truncated));

        var beforeFields = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(beforeFields.AsSpan(8), 2);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(beforeFields));

        var uintOutsideObject = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(uintOutsideObject.AsSpan(8), 18);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(uintOutsideObject));

        var misalignedUInt = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(misalignedUInt.AsSpan(8), 5);
        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(misalignedUInt));
    }

    [Fact]
    public void ParseRejectsOverlappingVTableAndTable()
    {
        var data = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 26);

        Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(data));
    }

    [Fact]
    public void ParseRejectsAliasedAndPartiallyOverlappingFields()
    {
        var aliased = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(aliased.AsSpan(10), 4);
        var aliasException = Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(aliased));
        Assert.Contains("fields 0 and 1 overlap", aliasException.Message, StringComparison.Ordinal);

        var partial = CreateHandBuiltMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(partial.AsSpan(14), 5);
        var partialException = Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(partial));
        Assert.Contains("fields 0 and 3 overlap", partialException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsNonCanonicalBooleanValues()
    {
        var data = CreateHandBuiltMoveBuffer();
        data[40] = 2;

        var exception = Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(data));
        Assert.Contains("Boolean field 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAndWriteEditedPreserveExtendedSchemaAndOpaqueBytes()
    {
        var data = CreateExtendedMoveBuffer();
        var file = SwShMoveDataFile.Parse(data);

        var output = file.WriteEdited(file.Record with
        {
            Core = file.Record.Core with { Power = 80 },
        });

        var expected = (byte[])data.Clone();
        expected[116] = 80;
        Assert.Equal(40, file.Record.Core.Power);
        Assert.Equal(expected, output);
        Assert.Equal(8, BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(110)));
        Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], output[120..124]);
        Assert.Equal([0xCA, 0xFE, 0xBA, 0xBE], output[^4..]);
    }

    [Fact]
    public void ParseRejectsUnknownExtendedFieldOffsetOutsideObject()
    {
        var data = CreateExtendedMoveBuffer();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(110), 12);

        var exception = Assert.Throws<InvalidDataException>(() => SwShMoveDataFile.Parse(data));
        Assert.Contains("unknown field 51 points outside", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEditedRejectsSparseActivationForExtendedSchema()
    {
        var file = SwShMoveDataFile.Parse(CreateExtendedMoveBuffer());
        var edited = file.Record with
        {
            Targeting = file.Record.Targeting with { RawTarget = 4 },
        };

        var exception = Assert.Throws<InvalidDataException>(() => file.WriteEdited(edited));
        Assert.Contains("field 22", exception.Message, StringComparison.Ordinal);
        Assert.Contains("extended source data", exception.Message, StringComparison.Ordinal);
    }

    private static SwShMoveDataRecord CreateMoveRecord()
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: 33,
            CanUseMove: true,
            new SwShMoveCoreStats(
                Type: 1,
                Quality: 5,
                Category: 2,
                Power: 40,
                Accuracy: 100,
                PP: 35,
                Priority: -1,
                CritStage: 1,
                GigantamaxPower: 130),
            new SwShMoveTargeting(
                RawTarget: 4,
                HitMin: 1,
                HitMax: 2,
                TurnMin: 0,
                TurnMax: 0),
            new SwShMoveSecondaryEffects(
                Inflict: 1,
                InflictPercent: 10,
                RawInflictCount: 3,
                Flinch: 20,
                EffectSequence: 77,
                Recoil: -25,
                RawHealing: -50),
            [
                new SwShMoveStatChange(1, Stat: 1, Stage: -1, Percent: 30),
                new SwShMoveStatChange(2, Stat: 2, Stage: 1, Percent: 40),
                new SwShMoveStatChange(3, Stat: 0, Stage: 0, Percent: 0),
            ],
            new SwShMoveFlags(
                MakesContact: true,
                Charge: false,
                Recharge: false,
                Protect: true,
                Reflectable: false,
                Snatch: false,
                Mirror: false,
                Punch: true,
                Sound: false,
                Gravity: false,
                Defrost: false,
                DistanceTriple: false,
                Heal: false,
                IgnoreSubstitute: false,
                FailSkyBattle: false,
                AnimateAlly: false,
                Dance: false,
                Metronome: true));
    }

    private static byte[] CreateHandBuiltMoveBuffer()
    {
        return
        [
            0x1C, 0x00, 0x00, 0x00,
            0x18, 0x00, 0x14, 0x00,
            0x04, 0x00, 0x08, 0x00,
            0x0C, 0x00, 0x0D, 0x00,
            0x0E, 0x00, 0x0F, 0x00,
            0x10, 0x00, 0x11, 0x00,
            0x12, 0x00, 0x13, 0x00,
            0x18, 0x00, 0x00, 0x00,
            0x78, 0x56, 0x34, 0x12,
            0x21, 0x00, 0x00, 0x00,
            0x01, 0x09, 0x0D, 0x02,
            0x78, 0x65, 0x28, 0xF9,
            0xDE, 0xAD, 0xBE, 0xEF,
        ];
    }

    private static byte[] CreateForwardVTableMoveBuffer()
    {
        return
        [
            0x04, 0x00, 0x00, 0x00,
            0xEC, 0xFF, 0xFF, 0xFF,
            0x78, 0x56, 0x34, 0x12,
            0x21, 0x00, 0x00, 0x00,
            0x01, 0x09, 0x0D, 0x02,
            0x78, 0x65, 0x28, 0xF9,
            0x18, 0x00, 0x14, 0x00,
            0x04, 0x00, 0x08, 0x00,
            0x0C, 0x00, 0x0D, 0x00,
            0x0E, 0x00, 0x0F, 0x00,
            0x10, 0x00, 0x11, 0x00,
            0x12, 0x00, 0x13, 0x00,
            0xDE, 0xAD, 0xBE, 0xEF,
        ];
    }

    private static byte[] CreateExtendedMoveBuffer()
    {
        const int tableOffset = 112;
        var data = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(data, tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 108);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(110), 8);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(tableOffset), tableOffset - 4);
        data[116] = 40;
        data[120] = 0xDE;
        data[121] = 0xAD;
        data[122] = 0xBE;
        data[123] = 0xEF;
        data[124] = 0xCA;
        data[125] = 0xFE;
        data[126] = 0xBA;
        data[127] = 0xBE;
        return data;
    }
}
