// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShTradePokemonArchiveTests
{
    [Fact]
    public void WriteRoundTripsTradePokemonRecords()
    {
        var archive = CreateArchive();

        var parsed = SwShTradePokemonArchive.Parse(archive.Write());

        Assert.Collection(
            parsed.Trades,
            trade =>
            {
                Assert.Equal(0, trade.Index);
                Assert.Equal(1, trade.Form);
                Assert.Equal(10, trade.DynamaxLevel);
                Assert.Equal(851, trade.BallItemId);
                Assert.Equal(4, trade.Field03);
                Assert.Equal(0x1122334455667788UL, trade.Hash0);
                Assert.True(trade.CanGigantamax);
                Assert.Equal(234, trade.HeldItem);
                Assert.Equal(50, trade.Level);
                Assert.Equal(810, trade.Species);
                Assert.Equal(0x8877665544332211UL, trade.Hash1);
                Assert.Equal(123456, trade.TrainerId);
                Assert.Equal(11, trade.MemoryCode);
                Assert.Equal(0x1234, trade.MemoryTextVariable);
                Assert.Equal(12, trade.MemoryFeel);
                Assert.Equal(13, trade.MemoryIntensity);
                Assert.Equal(0x0102030405060708UL, trade.Hash2);
                Assert.Equal(1, trade.OtGender);
                Assert.Equal(2, trade.RequiredForm);
                Assert.Equal(25, trade.RequiredSpecies);
                Assert.Equal(24, trade.RequiredNature);
                Assert.Equal(3, trade.UnknownRequirement);
                Assert.Equal(2, trade.ShinyLock);
                Assert.Equal(25, trade.Nature);
                Assert.Equal(1, trade.Gender);
                Assert.Equal(new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26), trade.Ivs);
                Assert.Equal(2, trade.Ability);
                Assert.Equal([344, 345, 346, 347], trade.RelearnMoves);
            },
            trade =>
            {
                Assert.Equal(1, trade.Index);
                Assert.Equal(0, trade.Form);
                Assert.Equal(0, trade.DynamaxLevel);
                Assert.Equal(4, trade.BallItemId);
                Assert.False(trade.CanGigantamax);
                Assert.Equal(1, trade.Level);
                Assert.Equal(133, trade.Species);
                Assert.Equal(new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1), trade.Ivs);
                Assert.Equal([0, 0, 0, 0], trade.RelearnMoves);
            });
    }

    [Fact]
    public void WriteEditsUpdatesTradeFieldsAndFixedIvs()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Species, 25),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Form, 2),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Level, 15),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.BallItemId, 4),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.HeldItem, 99),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RequiredSpecies, 52),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RequiredForm, 1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RequiredNature, 25),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.UnknownRequirement, 0),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.MemoryTextVariable, 77),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RelearnMove2, 400),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.CanGigantamax, 0),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvHp, 0),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvAttack, 1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvDefense, 2),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpeed, 3),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpecialAttack, 4),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpecialDefense, 5),
        ]);

        var trade = SwShTradePokemonArchive.Parse(output).Trades[0];
        Assert.Equal(25, trade.Species);
        Assert.Equal(2, trade.Form);
        Assert.Equal(15, trade.Level);
        Assert.Equal(4, trade.BallItemId);
        Assert.Equal(99, trade.HeldItem);
        Assert.Equal(52, trade.RequiredSpecies);
        Assert.Equal(1, trade.RequiredForm);
        Assert.Equal(25, trade.RequiredNature);
        Assert.Equal(0, trade.UnknownRequirement);
        Assert.Equal(77, trade.MemoryTextVariable);
        Assert.Equal(400, trade.RelearnMoves[2]);
        Assert.False(trade.CanGigantamax);
        Assert.Equal(new SwShTradePokemonIvs(0, 1, 2, 3, 4, 5), trade.Ivs);
    }

    [Fact]
    public void FlawlessIvCountPresetsRoundTripSentinels()
    {
        var archive = CreateArchive();

        var threePerfectOutput = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(1, SwShTradePokemonField.FlawlessIvCount, 3),
        ]);
        var threePerfectTrade = SwShTradePokemonArchive.Parse(threePerfectOutput).Trades[1];
        Assert.Equal(3, SwShTradePokemonArchive.GetFlawlessIvCount(threePerfectTrade.Ivs));
        Assert.Equal(new SwShTradePokemonIvs(-4, -1, -1, -1, -1, -1), threePerfectTrade.Ivs);

        var sixPerfectOutput = SwShTradePokemonArchive.Parse(threePerfectOutput).WriteEdits(
        [
            new SwShTradePokemonEdit(1, SwShTradePokemonField.FlawlessIvCount, 6),
        ]);
        var sixPerfectTrade = SwShTradePokemonArchive.Parse(sixPerfectOutput).Trades[1];
        Assert.Equal(6, SwShTradePokemonArchive.GetFlawlessIvCount(sixPerfectTrade.Ivs));
        Assert.Equal(new SwShTradePokemonIvs(31, 31, 31, 31, 31, 31), sixPerfectTrade.Ivs);

        var randomOutput = SwShTradePokemonArchive.Parse(sixPerfectOutput).WriteEdits(
        [
            new SwShTradePokemonEdit(1, SwShTradePokemonField.FlawlessIvCount, 0),
        ]);
        var randomTrade = SwShTradePokemonArchive.Parse(randomOutput).Trades[1];
        Assert.Equal(0, SwShTradePokemonArchive.GetFlawlessIvCount(randomTrade.Ivs));
        Assert.Equal(new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1), randomTrade.Ivs);
    }

    [Fact]
    public void WriteEditsRejectsInvalidIvValues()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.IvAttack, -4)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.IvHp, 32)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.FlawlessIvCount, 5)]));
    }

    [Fact]
    public void WriteEditsRejectsMixedThreePerfectIvSentinelLayout()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.IvHp, -4)]));
    }

    [Fact]
    public void WriteEditsAcceptsCanonicalThreePerfectIvSentinelLayoutAcrossBatch()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvHp, -4),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvAttack, -1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvDefense, -1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpeed, -1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpecialAttack, -1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpecialDefense, -1),
        ]);

        Assert.Equal(
            new SwShTradePokemonIvs(-4, -1, -1, -1, -1, -1),
            SwShTradePokemonArchive.Parse(output).Trades[0].Ivs);
    }

    [Fact]
    public void WriteEditsRejectsInvalidDynamaxLevel()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.DynamaxLevel, 11)]));
    }

    [Fact]
    public void ParsedWriteEditsWithNoChangesIsByteIdentical()
    {
        var source = CreateArchive().Write()
            .Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })
            .ToArray();
        var archive = SwShTradePokemonArchive.Parse(source);

        var output = archive.WriteEdits([]);
        var sameValueOutput = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Level, archive.Trades[0].Level),
        ]);

        Assert.Equal(source, output);
        Assert.Equal(source, sameValueOutput);
    }

    [Fact]
    public void ParsedWriteEditsPatchesOnlyChangedFieldAndPreservesTrailingBytes()
    {
        var source = CreateArchive().Write()
            .Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })
            .ToArray();
        var expected = source.ToArray();
        expected[GetTradeFieldAbsoluteOffset(expected, tradeIndex: 0, fieldIndex: 7)] = 15;
        var archive = SwShTradePokemonArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Level, 15),
        ]);

        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData(SwShTradePokemonField.Form, 2, 0, 1)]
    [InlineData(SwShTradePokemonField.DynamaxLevel, 9, 1, 1)]
    [InlineData(SwShTradePokemonField.BallItemId, 4, 2, 4)]
    [InlineData(SwShTradePokemonField.Field03, 5, 3, 4)]
    [InlineData(SwShTradePokemonField.CanGigantamax, 0, 5, 1)]
    [InlineData(SwShTradePokemonField.HeldItem, 235, 6, 4)]
    [InlineData(SwShTradePokemonField.Level, 49, 7, 1)]
    [InlineData(SwShTradePokemonField.Species, 809, 8, 4)]
    [InlineData(SwShTradePokemonField.TrainerId, 123457, 10, 4)]
    [InlineData(SwShTradePokemonField.MemoryCode, 10, 11, 1)]
    [InlineData(SwShTradePokemonField.MemoryTextVariable, 0x1235, 12, 2)]
    [InlineData(SwShTradePokemonField.MemoryFeel, 11, 13, 1)]
    [InlineData(SwShTradePokemonField.MemoryIntensity, 14, 14, 1)]
    [InlineData(SwShTradePokemonField.OtGender, 0, 16, 1)]
    [InlineData(SwShTradePokemonField.RequiredForm, 1, 17, 1)]
    [InlineData(SwShTradePokemonField.RequiredSpecies, 26, 18, 4)]
    [InlineData(SwShTradePokemonField.RequiredNature, 23, 19, 4)]
    [InlineData(SwShTradePokemonField.UnknownRequirement, 0, 20, 1)]
    [InlineData(SwShTradePokemonField.ShinyLock, 1, 21, 4)]
    [InlineData(SwShTradePokemonField.Nature, 24, 22, 4)]
    [InlineData(SwShTradePokemonField.Gender, 2, 23, 1)]
    [InlineData(SwShTradePokemonField.IvSpeed, 27, 24, 1)]
    [InlineData(SwShTradePokemonField.IvAttack, 29, 25, 1)]
    [InlineData(SwShTradePokemonField.IvDefense, 28, 26, 1)]
    [InlineData(SwShTradePokemonField.IvHp, 30, 27, 1)]
    [InlineData(SwShTradePokemonField.IvSpecialAttack, 26, 28, 1)]
    [InlineData(SwShTradePokemonField.IvSpecialDefense, 25, 29, 1)]
    [InlineData(SwShTradePokemonField.Ability, 1, 30, 1)]
    [InlineData(SwShTradePokemonField.RelearnMove0, 343, 31, 2)]
    [InlineData(SwShTradePokemonField.RelearnMove1, 348, 32, 2)]
    [InlineData(SwShTradePokemonField.RelearnMove2, 349, 33, 2)]
    [InlineData(SwShTradePokemonField.RelearnMove3, 350, 34, 2)]
    public void ParsedWriteEditsPatchesExactSchemaField(
        SwShTradePokemonField field,
        int value,
        int fieldIndex,
        int fieldSize)
    {
        var source = CreateArchive().Write();
        var expected = source.ToArray();
        var fieldOffset = GetTradeFieldAbsoluteOffset(expected, tradeIndex: 0, fieldIndex);
        switch (fieldSize)
        {
            case sizeof(byte):
                expected[fieldOffset] = unchecked((byte)value);
                break;
            case sizeof(ushort):
                BinaryPrimitives.WriteUInt16LittleEndian(
                    expected.AsSpan(fieldOffset, sizeof(ushort)),
                    checked((ushort)value));
                break;
            case sizeof(int):
                BinaryPrimitives.WriteInt32LittleEndian(
                    expected.AsSpan(fieldOffset, sizeof(int)),
                    value);
                break;
            default:
                throw new InvalidOperationException($"Unexpected field size {fieldSize}.");
        }

        var output = SwShTradePokemonArchive.Parse(source).WriteEdits(
        [
            new SwShTradePokemonEdit(0, field, value),
        ]);

        Assert.Equal(expected, output);
    }

    [Fact]
    public void ParsedWriteEditsPreservesUneditedLegacyValues()
    {
        var archive = CreateArchive();
        var legacyTrade = archive.Trades[0] with
        {
            DynamaxLevel = 11,
            BallItemId = 17,
            Level = 0,
            Species = 0,
            OtGender = 2,
            ShinyLock = 99,
            Nature = 99,
            Gender = 3,
            Ability = 99,
        };
        var source = new SwShTradePokemonArchive([legacyTrade, archive.Trades[1]]).Write();

        var output = SwShTradePokemonArchive.Parse(source).WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.HeldItem, 1),
        ]);
        var parsed = SwShTradePokemonArchive.Parse(output).Trades[0];

        Assert.Equal(11, parsed.DynamaxLevel);
        Assert.Equal(17, parsed.BallItemId);
        Assert.Equal(0, parsed.Level);
        Assert.Equal(0, parsed.Species);
        Assert.Equal(2, parsed.OtGender);
        Assert.Equal(99, parsed.ShinyLock);
        Assert.Equal(99, parsed.Nature);
        Assert.Equal(3, parsed.Gender);
        Assert.Equal(99, parsed.Ability);
        Assert.Equal(3, parsed.UnknownRequirement);
        Assert.Equal(1, parsed.HeldItem);
    }

    [Fact]
    public void ParsedWriteEditsPreservesUntouchedLegacyMixedIvLayout()
    {
        var archive = CreateArchive();
        var legacyTrade = archive.Trades[0] with
        {
            Ivs = new SwShTradePokemonIvs(-4, 31, -1, -1, -1, -1),
        };
        var source = new SwShTradePokemonArchive([legacyTrade, archive.Trades[1]]).Write()
            .Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })
            .ToArray();
        var expected = source.ToArray();
        expected[GetTradeFieldAbsoluteOffset(expected, tradeIndex: 0, fieldIndex: 7)] = 15;

        var output = SwShTradePokemonArchive.Parse(source).WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Level, 15),
        ]);

        Assert.Equal(expected, output);
        Assert.Equal(
            new SwShTradePokemonIvs(-4, 31, -1, -1, -1, -1),
            SwShTradePokemonArchive.Parse(output).Trades[0].Ivs);
    }

    [Fact]
    public void ParsedWriteEditsMaterializesChangedOmittedFieldWithoutRebuildingSourceBytes()
    {
        var trailingBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var source = CreateArchive().Write().Concat(trailingBytes).ToArray();
        OmitTradeField(source, tradeIndex: 0, fieldIndex: 6);
        var vectorElementOffset = GetTradeVectorElementOffset(source, tradeIndex: 0);
        var archive = SwShTradePokemonArchive.Parse(source);
        Assert.Equal(0, archive.Trades[0].HeldItem);

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.HeldItem, 1),
        ]);

        Assert.True(output.Length > source.Length);
        Assert.Equal(1, SwShTradePokemonArchive.Parse(output).Trades[0].HeldItem);
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
    public void ParsedWriteEditsMaterializesMultipleScalarSizesAndLateFieldFromShortVtable()
    {
        var trailingBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var source = CreateArchive().Write().Concat(trailingBytes).ToArray();
        OmitTradeField(source, tradeIndex: 0, fieldIndex: 0);
        OmitTradeField(source, tradeIndex: 0, fieldIndex: 6);
        OmitTradeField(source, tradeIndex: 0, fieldIndex: 12);
        SetTradeVtableFieldCount(source, tradeIndex: 0, fieldCount: 31);
        var vectorElementOffset = GetTradeVectorElementOffset(source, tradeIndex: 0);
        var archive = SwShTradePokemonArchive.Parse(source);
        Assert.Equal(0, archive.Trades[0].Form);
        Assert.Equal(0, archive.Trades[0].HeldItem);
        Assert.Equal(0, archive.Trades[0].MemoryTextVariable);
        Assert.Equal(0, archive.Trades[0].RelearnMoves[3]);

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Form, 2),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.HeldItem, 1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.MemoryTextVariable, 77),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RelearnMove3, 350),
        ]);

        Assert.True(output.Length > source.Length);
        var trade = SwShTradePokemonArchive.Parse(output).Trades[0];
        Assert.Equal(2, trade.Form);
        Assert.Equal(1, trade.HeldItem);
        Assert.Equal(77, trade.MemoryTextVariable);
        Assert.Equal(350, trade.RelearnMoves[3]);
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
    public void ParsedWriteEditsRejectsExpansionWithMaterializedUnknownField()
    {
        var source = AddMaterializedUnknownTradeField(CreateArchive().Write(), tradeIndex: 0);
        OmitTradeField(source, tradeIndex: 0, fieldIndex: 6);
        var archive = SwShTradePokemonArchive.Parse(source);
        Assert.Equal(0, archive.Trades[0].HeldItem);

        var exception = Assert.Throws<InvalidDataException>(
            () => archive.WriteEdits(
            [
                new SwShTradePokemonEdit(0, SwShTradePokemonField.HeldItem, 1),
            ]));

        Assert.Contains("materialized unknown fields", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParsedWriteEditsRejectsPresentFieldEditWithMaterializedUnknownField()
    {
        var source = AddMaterializedUnknownTradeField(CreateArchive().Write(), tradeIndex: 0);
        var archive = SwShTradePokemonArchive.Parse(source);

        var exception = Assert.Throws<InvalidDataException>(
            () => archive.WriteEdits(
            [
                new SwShTradePokemonEdit(0, SwShTradePokemonField.Level, 49),
            ]));

        Assert.Contains("materialized unknown fields", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParsedWriteEditsPreservesUntouchedTableWithMaterializedUnknownField()
    {
        var source = AddMaterializedUnknownTradeField(CreateArchive().Write(), tradeIndex: 0);
        var expected = source.ToArray();
        expected[GetTradeFieldAbsoluteOffset(expected, tradeIndex: 1, fieldIndex: 7)] = 2;
        var archive = SwShTradePokemonArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(1, SwShTradePokemonField.Level, 2),
        ]);

        Assert.Equal(expected, output);
    }

    [Fact]
    public void ParsedWriteEditsIsolatesAliasedTradeTablesBeforePatching()
    {
        var source = CreateArchive().Write();
        var firstTableOffset = GetTradeTableOffset(source, tradeIndex: 0);
        var secondVectorElementOffset = GetTradeVectorElementOffset(source, tradeIndex: 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            source.AsSpan(secondVectorElementOffset, sizeof(uint)),
            checked((uint)(firstTableOffset - secondVectorElementOffset)));
        var archive = SwShTradePokemonArchive.Parse(source);
        var originalLevel = archive.Trades[0].Level;
        Assert.Equal(originalLevel, archive.Trades[1].Level);

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Level, originalLevel + 1),
        ]);
        var parsed = SwShTradePokemonArchive.Parse(output);

        Assert.Equal(originalLevel + 1, parsed.Trades[0].Level);
        Assert.Equal(originalLevel, parsed.Trades[1].Level);
    }

    [Fact]
    public void ParseRejectsVectorCountThatExceedsAvailableDataBeforeAllocation()
    {
        var source = CreateArchive().Write();
        var vectorOffset = GetTradeVectorOffset(source);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vectorOffset, sizeof(uint)), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShTradePokemonArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsUnsignedOffsetOverflowAsInvalidData()
    {
        var source = CreateArchive().Write();
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0, sizeof(uint)), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShTradePokemonArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsScalarFieldThatPointsOutsideItsTableObject()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetTradeTableOffset(source, tradeIndex: 0);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(tableOffset, sizeof(int)));
        var objectSize = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(vtableOffset + sizeof(ushort), sizeof(ushort)));
        var fieldEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (6 * sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(fieldEntryOffset, sizeof(ushort)),
            checked((ushort)(objectSize - 2)));

        Assert.Throws<InvalidDataException>(() => SwShTradePokemonArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsOverlappingScalarFields()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetTradeTableOffset(source, tradeIndex: 0);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(tableOffset, sizeof(int)));
        var heldItemEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (6 * sizeof(ushort));
        var speciesEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (8 * sizeof(ushort));
        var speciesFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(speciesEntryOffset, sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(heldItemEntryOffset, sizeof(ushort)),
            speciesFieldOffset);

        Assert.Throws<InvalidDataException>(() => SwShTradePokemonArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsPartiallyOverlappingTradeTableObjects()
    {
        var source = CreateArchive().Write();
        var firstTableOffset = GetTradeTableOffset(source, tradeIndex: 0);
        var secondTableOffset = GetTradeTableOffset(source, tradeIndex: 1);
        var firstVtableOffset = firstTableOffset - BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(firstTableOffset, sizeof(int)));
        var overlappingObjectSize = checked((ushort)((secondTableOffset - firstTableOffset) + sizeof(int)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(firstVtableOffset + sizeof(ushort), sizeof(ushort)),
            overlappingObjectSize);

        var exception = Assert.Throws<InvalidDataException>(() => SwShTradePokemonArchive.Parse(source));

        Assert.Contains("partially overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteEditsRejectsInvalidSemanticRanges()
    {
        var archive = CreateArchive();

        AssertInvalid(SwShTradePokemonField.Level, 0);
        AssertInvalid(SwShTradePokemonField.Level, 101);
        AssertInvalid(SwShTradePokemonField.Species, 0);
        AssertInvalid(SwShTradePokemonField.OtGender, 2);
        AssertInvalid(SwShTradePokemonField.ShinyLock, 3);
        AssertInvalid(SwShTradePokemonField.Nature, 26);
        AssertInvalid(SwShTradePokemonField.RequiredNature, 26);
        AssertInvalid(SwShTradePokemonField.Gender, 3);
        AssertInvalid(SwShTradePokemonField.Ability, 4);
        AssertInvalid(SwShTradePokemonField.DynamaxLevel, 11);
        AssertInvalid(SwShTradePokemonField.CanGigantamax, 2);
        AssertInvalid(SwShTradePokemonField.UnknownRequirement, 1);
        AssertInvalid(SwShTradePokemonField.RelearnMove0, ushort.MaxValue + 1);

        void AssertInvalid(SwShTradePokemonField field, int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => archive.WriteEdits([new SwShTradePokemonEdit(0, field, value)]));
        }
    }

    [Fact]
    public void RequiredSpeciesAllowsDefaultZeroSentinel()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RequiredSpecies, 0),
        ]);

        Assert.Equal(0, SwShTradePokemonArchive.Parse(output).Trades[0].RequiredSpecies);
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

        Assert.Equal(expected, SwShTradePokemonArchive.ValidBallItemIds);
        foreach (var itemId in expected)
        {
            Assert.True(SwShTradePokemonArchive.IsValidBallItemId(itemId));
            _ = archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.BallItemId, itemId)]);
        }

        foreach (var itemId in new[] { -1, 17, 491, 500, 575, 577, 850, 852 })
        {
            Assert.False(SwShTradePokemonArchive.IsValidBallItemId(itemId));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.BallItemId, itemId)]));
        }
    }

    [Fact]
    public void ThreePerfectIvDetectionRequiresCanonicalSentinelLayout()
    {
        Assert.Equal(
            3,
            SwShTradePokemonArchive.GetFlawlessIvCount(
                new SwShTradePokemonIvs(-4, -1, -1, -1, -1, -1)));
        Assert.Null(
            SwShTradePokemonArchive.GetFlawlessIvCount(
                new SwShTradePokemonIvs(-4, 31, -1, -1, -1, -1)));
        Assert.Null(
            SwShTradePokemonArchive.GetFlawlessIvCount(
                new SwShTradePokemonIvs(-4, -1, -1, -1, -1, 0)));
    }

    private static SwShTradePokemonArchive CreateArchive()
    {
        return new SwShTradePokemonArchive(
        [
            new SwShTradePokemonRecord(
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
                0x8877665544332211,
                123456,
                11,
                0x1234,
                12,
                13,
                0x0102030405060708,
                1,
                2,
                25,
                24,
                3,
                2,
                25,
                1,
                new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26),
                2,
                [344, 345, 346, 347]),
            new SwShTradePokemonRecord(
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
                0,
                0,
                0,
                0,
                0,
                new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1),
                0,
                [0, 0, 0, 0]),
        ]);
    }

    private static void OmitTradeField(byte[] data, int tradeIndex, int fieldIndex)
    {
        var tableOffset = GetTradeTableOffset(data, tradeIndex);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(fieldEntryOffset, sizeof(ushort)), 0);
    }

    private static void SetTradeVtableFieldCount(byte[] data, int tradeIndex, int fieldCount)
    {
        var tableOffset = GetTradeTableOffset(data, tradeIndex);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var vtableLength = checked((ushort)((sizeof(ushort) * 2) + (fieldCount * sizeof(ushort))));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(vtableOffset, sizeof(ushort)), vtableLength);
    }

    private static byte[] AddMaterializedUnknownTradeField(byte[] source, int tradeIndex)
    {
        const int unknownFieldIndex = 35;
        const byte unknownFieldValue = 0xA5;

        var sourceTableOffset = GetTradeTableOffset(source, tradeIndex);
        var sourceVtableOffset = sourceTableOffset
            - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(sourceTableOffset, sizeof(int)));
        var sourceVtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(sourceVtableOffset, sizeof(ushort)));
        var sourceObjectSize = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(sourceVtableOffset + sizeof(ushort), sizeof(ushort)));
        Assert.Equal(
            (sizeof(ushort) * 2) + (unknownFieldIndex * sizeof(ushort)),
            sourceVtableLength);

        var bytes = source.ToList();
        Align(bytes, sizeof(ushort));
        var expandedVtableOffset = bytes.Count;
        bytes.AddRange(source.AsSpan(sourceVtableOffset, sourceVtableLength).ToArray());
        bytes.AddRange(new byte[sizeof(ushort)]);

        Align(bytes, sizeof(ulong));
        var expandedTableOffset = bytes.Count;
        bytes.AddRange(source.AsSpan(sourceTableOffset, sourceObjectSize).ToArray());
        bytes.Add(unknownFieldValue);

        var output = bytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(expandedVtableOffset, sizeof(ushort)),
            checked((ushort)(sourceVtableLength + sizeof(ushort))));
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(expandedVtableOffset + sizeof(ushort), sizeof(ushort)),
            checked((ushort)(sourceObjectSize + sizeof(byte))));
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(
                expandedVtableOffset + (sizeof(ushort) * 2) + (unknownFieldIndex * sizeof(ushort)),
                sizeof(ushort)),
            sourceObjectSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            output.AsSpan(expandedTableOffset, sizeof(int)),
            checked(expandedTableOffset - expandedVtableOffset));

        var vectorElementOffset = GetTradeVectorElementOffset(output, tradeIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(vectorElementOffset, sizeof(uint)),
            checked((uint)(expandedTableOffset - vectorElementOffset)));
        return output;
    }

    private static void Align(List<byte> bytes, int alignment)
    {
        while (bytes.Count % alignment != 0)
        {
            bytes.Add(0);
        }
    }

    private static int GetTradeFieldAbsoluteOffset(byte[] data, int tradeIndex, int fieldIndex)
    {
        var tableOffset = GetTradeTableOffset(data, tradeIndex);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldEntryOffset = vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort));
        var relativeFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fieldEntryOffset, sizeof(ushort)));
        Assert.NotEqual(0, relativeFieldOffset);
        return tableOffset + relativeFieldOffset;
    }

    private static int GetTradeTableOffset(byte[] data, int tradeIndex)
    {
        var elementOffset = GetTradeVectorElementOffset(data, tradeIndex);
        return elementOffset + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(elementOffset, sizeof(uint))));
    }

    private static int GetTradeVectorElementOffset(byte[] data, int tradeIndex)
    {
        return GetTradeVectorOffset(data) + sizeof(uint) + (tradeIndex * sizeof(uint));
    }

    private static int GetTradeVectorOffset(byte[] data)
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
