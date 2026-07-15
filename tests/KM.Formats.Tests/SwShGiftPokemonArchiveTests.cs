// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShGiftPokemonArchiveTests
{
    [Fact]
    public void WriteRoundTripsGiftPokemonRecords()
    {
        var archive = CreateArchive();

        var parsed = SwShGiftPokemonArchive.Parse(archive.Write());

        Assert.Collection(
            parsed.Gifts,
            gift =>
            {
                Assert.Equal(0, gift.Index);
                Assert.Equal(0, gift.IsEgg);
                Assert.Equal(1, gift.Form);
                Assert.Equal(10, gift.DynamaxLevel);
                Assert.Equal(851, gift.BallItemId);
                Assert.Equal(4, gift.Field04);
                Assert.Equal(0x1122334455667788UL, gift.Hash1);
                Assert.True(gift.CanGigantamax);
                Assert.Equal(234, gift.HeldItem);
                Assert.Equal(50, gift.Level);
                Assert.Equal(810, gift.Species);
                Assert.Equal(10, gift.Field0A);
                Assert.Equal(11, gift.MemoryCode);
                Assert.Equal(0x1234, gift.MemoryData);
                Assert.Equal(12, gift.MemoryFeel);
                Assert.Equal(13, gift.MemoryLevel);
                Assert.Equal(0x8877665544332211UL, gift.OtNameId);
                Assert.Equal(1, gift.OtGender);
                Assert.Equal(2, gift.ShinyLock);
                Assert.Equal(25, gift.Nature);
                Assert.Equal(3, gift.Gender);
                Assert.Equal(new SwShGiftPokemonIvs(31, 30, 29, 28, 27, 26), gift.Ivs);
                Assert.Equal(2, gift.Ability);
                Assert.Equal(344, gift.SpecialMove);
            },
            gift =>
            {
                Assert.Equal(1, gift.Index);
                Assert.Equal(1, gift.IsEgg);
                Assert.Equal(0, gift.Form);
                Assert.Equal(0, gift.DynamaxLevel);
                Assert.Equal(4, gift.BallItemId);
                Assert.False(gift.CanGigantamax);
                Assert.Equal(1, gift.Level);
                Assert.Equal(133, gift.Species);
                Assert.Equal(new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1), gift.Ivs);
            });
    }

    [Fact]
    public void WriteEditsUpdatesGiftFieldsAndFixedIvs()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Species, 25),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Form, 2),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Level, 15),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.BallItemId, 4),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.HeldItem, 99),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.CanGigantamax, 0),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvHp, 0),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvAttack, 1),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvDefense, 2),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpeed, 3),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpecialAttack, 4),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpecialDefense, 5),
        ]);

        var gift = SwShGiftPokemonArchive.Parse(output).Gifts[0];
        Assert.Equal(25, gift.Species);
        Assert.Equal(2, gift.Form);
        Assert.Equal(15, gift.Level);
        Assert.Equal(4, gift.BallItemId);
        Assert.Equal(99, gift.HeldItem);
        Assert.False(gift.CanGigantamax);
        Assert.Equal(new SwShGiftPokemonIvs(0, 1, 2, 3, 4, 5), gift.Ivs);
    }

    [Fact]
    public void FlawlessIvCountPresetsRoundTripSentinels()
    {
        var archive = CreateArchive();

        var threePerfectOutput = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(1, SwShGiftPokemonField.FlawlessIvCount, 3),
        ]);
        var threePerfectGift = SwShGiftPokemonArchive.Parse(threePerfectOutput).Gifts[1];
        Assert.Equal(3, SwShGiftPokemonArchive.GetFlawlessIvCount(threePerfectGift.Ivs));
        Assert.Equal(new SwShGiftPokemonIvs(-4, -1, -1, -1, -1, -1), threePerfectGift.Ivs);

        var sixPerfectOutput = SwShGiftPokemonArchive.Parse(threePerfectOutput).WriteEdits(
        [
            new SwShGiftPokemonEdit(1, SwShGiftPokemonField.FlawlessIvCount, 6),
        ]);
        var sixPerfectGift = SwShGiftPokemonArchive.Parse(sixPerfectOutput).Gifts[1];
        Assert.Equal(6, SwShGiftPokemonArchive.GetFlawlessIvCount(sixPerfectGift.Ivs));
        Assert.Equal(new SwShGiftPokemonIvs(31, 31, 31, 31, 31, 31), sixPerfectGift.Ivs);

        var randomOutput = SwShGiftPokemonArchive.Parse(sixPerfectOutput).WriteEdits(
        [
            new SwShGiftPokemonEdit(1, SwShGiftPokemonField.FlawlessIvCount, 0),
        ]);
        var randomGift = SwShGiftPokemonArchive.Parse(randomOutput).Gifts[1];
        Assert.Equal(0, SwShGiftPokemonArchive.GetFlawlessIvCount(randomGift.Ivs));
        Assert.Equal(new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1), randomGift.Ivs);
    }

    [Fact]
    public void WriteEditsRejectsInvalidIvValues()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvAttack, -4)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvHp, 32)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.FlawlessIvCount, 5)]));
    }

    [Fact]
    public void WriteEditsRejectsMixedThreePerfectIvSentinelLayout()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvHp, -4)]));
    }

    [Fact]
    public void WriteEditsAcceptsCanonicalThreePerfectIvSentinelLayoutAcrossBatch()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvHp, -4),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvAttack, -1),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvDefense, -1),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpeed, -1),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpecialAttack, -1),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpecialDefense, -1),
        ]);

        Assert.Equal(
            new SwShGiftPokemonIvs(-4, -1, -1, -1, -1, -1),
            SwShGiftPokemonArchive.Parse(output).Gifts[0].Ivs);
    }

    [Fact]
    public void WriteEditsRejectsInvalidDynamaxLevel()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.DynamaxLevel, 11)]));
    }

    [Fact]
    public void ParsedWriteEditsWithNoChangesIsByteIdentical()
    {
        var source = CreateArchive().Write()
            .Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })
            .ToArray();
        var archive = SwShGiftPokemonArchive.Parse(source);

        var output = archive.WriteEdits([]);

        Assert.Equal(source, output);
    }

    [Fact]
    public void ParsedWriteEditsPatchesOnlyChangedFieldAndPreservesTrailingBytes()
    {
        var source = CreateArchive().Write()
            .Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })
            .ToArray();
        var expected = source.ToArray();
        expected[GetGiftFieldAbsoluteOffset(expected, giftIndex: 0, fieldIndex: 8)] = 15;
        var archive = SwShGiftPokemonArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Level, 15),
        ]);

        Assert.Equal(expected, output);
    }

    [Fact]
    public void ParsedWriteEditsPreservesUneditedLegacyValues()
    {
        var archive = CreateArchive();
        var legacyGift = archive.Gifts[0] with
        {
            IsEgg = 7,
            DynamaxLevel = 11,
            BallItemId = 17,
            Level = 0,
            Species = 0,
            ShinyLock = 99,
            Nature = 99,
            Gender = 3,
            Ability = 99,
        };
        var source = new SwShGiftPokemonArchive([legacyGift, archive.Gifts[1]]).Write();

        var output = SwShGiftPokemonArchive.Parse(source).WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.HeldItem, 1),
        ]);
        var parsed = SwShGiftPokemonArchive.Parse(output).Gifts[0];

        Assert.Equal(7, parsed.IsEgg);
        Assert.Equal(11, parsed.DynamaxLevel);
        Assert.Equal(17, parsed.BallItemId);
        Assert.Equal(0, parsed.Level);
        Assert.Equal(0, parsed.Species);
        Assert.Equal(99, parsed.ShinyLock);
        Assert.Equal(99, parsed.Nature);
        Assert.Equal(3, parsed.Gender);
        Assert.Equal(99, parsed.Ability);
        Assert.Equal(1, parsed.HeldItem);
    }

    [Fact]
    public void ParsedWriteEditsPreservesUntouchedLegacyMixedIvLayout()
    {
        var archive = CreateArchive();
        var legacyGift = archive.Gifts[0] with
        {
            Ivs = new SwShGiftPokemonIvs(-4, 31, -1, -1, -1, -1),
        };
        var source = new SwShGiftPokemonArchive([legacyGift, archive.Gifts[1]]).Write()
            .Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })
            .ToArray();
        var expected = source.ToArray();
        expected[GetGiftFieldAbsoluteOffset(expected, giftIndex: 0, fieldIndex: 8)] = 15;

        var output = SwShGiftPokemonArchive.Parse(source).WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Level, 15),
        ]);

        Assert.Equal(expected, output);
        Assert.Equal(
            new SwShGiftPokemonIvs(-4, 31, -1, -1, -1, -1),
            SwShGiftPokemonArchive.Parse(output).Gifts[0].Ivs);
    }

    [Fact]
    public void ParsedWriteEditsMaterializesChangedOmittedFieldWithoutRebuildingSourceBytes()
    {
        var trailingBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var source = CreateArchive().Write().Concat(trailingBytes).ToArray();
        OmitGiftField(source, giftIndex: 0, fieldIndex: 7);
        var vectorElementOffset = GetGiftVectorElementOffset(source, giftIndex: 0);
        var archive = SwShGiftPokemonArchive.Parse(source);
        Assert.Equal(0, archive.Gifts[0].HeldItem);

        var output = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.HeldItem, 1),
        ]);

        Assert.True(output.Length > source.Length);
        Assert.Equal(1, SwShGiftPokemonArchive.Parse(output).Gifts[0].HeldItem);
        Assert.Equal(trailingBytes, output.AsSpan(source.Length - trailingBytes.Length, trailingBytes.Length).ToArray());
        for (var index = 0; index < source.Length; index++)
        {
            if (index < vectorElementOffset || index >= vectorElementOffset + sizeof(uint))
            {
                Assert.Equal(source[index], output[index]);
            }
        }
    }

    [Fact]
    public void ParsedWriteEditsIsolatesAliasedGiftTablesBeforePatching()
    {
        var source = CreateArchive().Write();
        var firstTableOffset = GetGiftTableOffset(source, giftIndex: 0);
        var secondVectorElementOffset = GetGiftVectorElementOffset(source, giftIndex: 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            source.AsSpan(secondVectorElementOffset, sizeof(uint)),
            checked((uint)(firstTableOffset - secondVectorElementOffset)));
        var archive = SwShGiftPokemonArchive.Parse(source);
        var originalLevel = archive.Gifts[0].Level;
        Assert.Equal(originalLevel, archive.Gifts[1].Level);

        var output = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Level, originalLevel + 1),
        ]);
        var parsed = SwShGiftPokemonArchive.Parse(output);

        Assert.Equal(originalLevel + 1, parsed.Gifts[0].Level);
        Assert.Equal(originalLevel, parsed.Gifts[1].Level);
    }

    [Fact]
    public void ParseRejectsVectorCountThatExceedsAvailableDataBeforeAllocation()
    {
        var source = CreateArchive().Write();
        var vectorOffset = GetGiftVectorOffset(source);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vectorOffset, sizeof(uint)), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShGiftPokemonArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsUnsignedOffsetOverflowAsInvalidData()
    {
        var source = CreateArchive().Write();
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0, sizeof(uint)), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShGiftPokemonArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsScalarFieldThatPointsOutsideItsTableObject()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetGiftTableOffset(source, giftIndex: 0);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(tableOffset, sizeof(int)));
        var objectSize = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(vtableOffset + sizeof(ushort), sizeof(ushort)));
        var fieldEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (7 * sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(fieldEntryOffset, sizeof(ushort)),
            checked((ushort)(objectSize - 2)));

        Assert.Throws<InvalidDataException>(() => SwShGiftPokemonArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsOverlappingScalarFields()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetGiftTableOffset(source, giftIndex: 0);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(tableOffset, sizeof(int)));
        var heldItemEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (7 * sizeof(ushort));
        var speciesEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (9 * sizeof(ushort));
        var speciesFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(speciesEntryOffset, sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(heldItemEntryOffset, sizeof(ushort)),
            speciesFieldOffset);

        Assert.Throws<InvalidDataException>(() => SwShGiftPokemonArchive.Parse(source));
    }

    [Fact]
    public void WriteEditsRejectsInvalidSemanticRanges()
    {
        var archive = CreateArchive();

        AssertInvalid(SwShGiftPokemonField.IsEgg, 2);
        AssertInvalid(SwShGiftPokemonField.Level, 0);
        AssertInvalid(SwShGiftPokemonField.Level, 101);
        AssertInvalid(SwShGiftPokemonField.Species, 0);
        AssertInvalid(SwShGiftPokemonField.OtGender, 2);
        AssertInvalid(SwShGiftPokemonField.ShinyLock, 3);
        AssertInvalid(SwShGiftPokemonField.Nature, 26);
        AssertInvalid(SwShGiftPokemonField.Gender, 3);
        AssertInvalid(SwShGiftPokemonField.Ability, 4);
        AssertInvalid(SwShGiftPokemonField.DynamaxLevel, 11);
        AssertInvalid(SwShGiftPokemonField.CanGigantamax, 2);

        void AssertInvalid(SwShGiftPokemonField field, int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => archive.WriteEdits([new SwShGiftPokemonEdit(0, field, value)]));
        }
    }

    [Fact]
    public void BallItemIdsUseConfirmedSwordShieldWhitelist()
    {
        int[] expected =
        [
            0,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            9,
            10,
            11,
            12,
            13,
            14,
            15,
            16,
            492,
            493,
            494,
            495,
            496,
            497,
            498,
            499,
            576,
            851,
        ];
        var archive = CreateArchive();

        Assert.Equal(expected, SwShGiftPokemonArchive.ValidBallItemIds);
        foreach (var itemId in expected)
        {
            Assert.True(SwShGiftPokemonArchive.IsValidBallItemId(itemId));
            _ = archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.BallItemId, itemId)]);
        }

        foreach (var itemId in new[] { -1, 17, 491, 500, 575, 577, 850, 852 })
        {
            Assert.False(SwShGiftPokemonArchive.IsValidBallItemId(itemId));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.BallItemId, itemId)]));
        }
    }

    [Fact]
    public void ThreePerfectIvDetectionRequiresCanonicalSentinelLayout()
    {
        Assert.Equal(
            3,
            SwShGiftPokemonArchive.GetFlawlessIvCount(
                new SwShGiftPokemonIvs(-4, -1, -1, -1, -1, -1)));
        Assert.Null(
            SwShGiftPokemonArchive.GetFlawlessIvCount(
                new SwShGiftPokemonIvs(-4, 31, -1, -1, -1, -1)));
        Assert.Null(
            SwShGiftPokemonArchive.GetFlawlessIvCount(
                new SwShGiftPokemonIvs(-4, -1, -1, -1, -1, 0)));
    }

    private static SwShGiftPokemonArchive CreateArchive()
    {
        return new SwShGiftPokemonArchive(
        [
            new SwShGiftPokemonRecord(
                0,
                0,
                1,
                10,
                851,
                4,
                0x1122334455667788,
                true,
                234,
                50,
                810,
                10,
                11,
                0x1234,
                12,
                13,
                0x8877665544332211,
                1,
                2,
                25,
                3,
                new SwShGiftPokemonIvs(31, 30, 29, 28, 27, 26),
                2,
                344),
            new SwShGiftPokemonRecord(
                1,
                1,
                0,
                0,
                4,
                0,
                0,
                false,
                0,
                1,
                133,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1),
                0,
                0),
        ]);
    }

    private static void OmitGiftField(byte[] data, int giftIndex, int fieldIndex)
    {
        var tableOffset = GetGiftTableOffset(data, giftIndex);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(fieldEntryOffset, sizeof(ushort)), 0);
    }

    private static int GetGiftFieldAbsoluteOffset(byte[] data, int giftIndex, int fieldIndex)
    {
        var tableOffset = GetGiftTableOffset(data, giftIndex);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort));
        var relativeFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fieldEntryOffset, sizeof(ushort)));
        Assert.NotEqual(0, relativeFieldOffset);
        return tableOffset + relativeFieldOffset;
    }

    private static int GetGiftTableOffset(byte[] data, int giftIndex)
    {
        var elementOffset = GetGiftVectorElementOffset(data, giftIndex);
        return elementOffset + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(elementOffset, sizeof(uint))));
    }

    private static int GetGiftVectorElementOffset(byte[] data, int giftIndex)
    {
        return GetGiftVectorOffset(data) + sizeof(uint) + (giftIndex * sizeof(uint));
    }

    private static int GetGiftVectorOffset(byte[] data)
    {
        var rootOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, sizeof(uint))));
        var vtableOffset = rootOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rootOffset, sizeof(int)));
        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(vtableOffset + (sizeof(ushort) * 2), sizeof(ushort)));
        var vectorReferenceOffset = rootOffset + fieldOffset;
        return vectorReferenceOffset
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(vectorReferenceOffset, sizeof(uint))));
    }
}
