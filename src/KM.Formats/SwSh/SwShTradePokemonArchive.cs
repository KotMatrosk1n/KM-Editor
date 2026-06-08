// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShTradePokemonIvs(
    int Hp,
    int Attack,
    int Defense,
    int Speed,
    int SpecialAttack,
    int SpecialDefense);

public sealed record SwShTradePokemonRecord(
    int Index,
    int Form,
    int DynamaxLevel,
    int BallItemId,
    int Field03,
    ulong Hash0,
    bool CanGigantamax,
    int HeldItem,
    int Level,
    int Species,
    ulong Hash1,
    int TrainerId,
    int MemoryCode,
    int MemoryTextVariable,
    int MemoryFeel,
    int MemoryIntensity,
    ulong Hash2,
    int OtGender,
    int RequiredForm,
    int RequiredSpecies,
    int RequiredNature,
    int UnknownRequirement,
    int ShinyLock,
    int Nature,
    int Gender,
    SwShTradePokemonIvs Ivs,
    int Ability,
    IReadOnlyList<int> RelearnMoves);

public enum SwShTradePokemonField
{
    Form,
    DynamaxLevel,
    BallItemId,
    Field03,
    CanGigantamax,
    HeldItem,
    Level,
    Species,
    TrainerId,
    MemoryCode,
    MemoryTextVariable,
    MemoryFeel,
    MemoryIntensity,
    OtGender,
    RequiredForm,
    RequiredSpecies,
    RequiredNature,
    UnknownRequirement,
    ShinyLock,
    Nature,
    Gender,
    IvHp,
    IvAttack,
    IvDefense,
    IvSpeed,
    IvSpecialAttack,
    IvSpecialDefense,
    Ability,
    RelearnMove0,
    RelearnMove1,
    RelearnMove2,
    RelearnMove3,
    FlawlessIvCount,
}

public sealed record SwShTradePokemonEdit(
    int TradeIndex,
    SwShTradePokemonField Field,
    int Value);

public sealed record SwShTradePokemonArchive(IReadOnlyList<SwShTradePokemonRecord> Trades)
{
    public const int RandomIvValue = -1;
    public const int ThreePerfectIvSentinel = -4;
    public const int MinimumFixedIvValue = 0;
    public const int MaximumFixedIvValue = 31;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumIdValue = int.MaxValue;

    public static SwShTradePokemonArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Trade Pokemon archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var tradesVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var trades = ReadTableVector(data, tradesVectorOffset, ReadTrade);

        return new SwShTradePokemonArchive(trades);
    }

    public byte[] Write()
    {
        var writer = new TradeFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShTradePokemonEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var trades = Trades
            .Select(trade => trade with
            {
                Ivs = trade.Ivs with { },
                RelearnMoves = trade.RelearnMoves.ToArray(),
            })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(trades, edit);
        }

        return new SwShTradePokemonArchive(trades).Write();
    }

    public static int? GetFlawlessIvCount(SwShTradePokemonIvs ivs)
    {
        if (ivs.Hp == ThreePerfectIvSentinel)
        {
            return 3;
        }

        if (ivs.Hp == MaximumFixedIvValue
            && ivs.Attack == MaximumFixedIvValue
            && ivs.Defense == MaximumFixedIvValue
            && ivs.Speed == MaximumFixedIvValue
            && ivs.SpecialAttack == MaximumFixedIvValue
            && ivs.SpecialDefense == MaximumFixedIvValue)
        {
            return 6;
        }

        if (ivs.Hp == RandomIvValue
            && ivs.Attack == RandomIvValue
            && ivs.Defense == RandomIvValue
            && ivs.Speed == RandomIvValue
            && ivs.SpecialAttack == RandomIvValue
            && ivs.SpecialDefense == RandomIvValue)
        {
            return 0;
        }

        return null;
    }

    private static void ApplyEdit(IReadOnlyList<SwShTradePokemonRecord> trades, SwShTradePokemonEdit edit)
    {
        if ((uint)edit.TradeIndex >= (uint)trades.Count)
        {
            throw new InvalidDataException($"Trade Pokemon index {edit.TradeIndex} is not present.");
        }

        if (trades is not SwShTradePokemonRecord[] mutableTrades)
        {
            throw new InvalidDataException("Trade Pokemon list is not mutable.");
        }

        var trade = trades[edit.TradeIndex];
        mutableTrades[edit.TradeIndex] = edit.Field switch
        {
            SwShTradePokemonField.Form => trade with { Form = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.DynamaxLevel => trade with { DynamaxLevel = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.BallItemId => trade with { BallItemId = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.Field03 => trade with { Field03 = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.CanGigantamax => trade with { CanGigantamax = ValidateBool(edit.Value) },
            SwShTradePokemonField.HeldItem => trade with { HeldItem = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.Level => trade with { Level = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.Species => trade with { Species = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.TrainerId => trade with { TrainerId = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.MemoryCode => trade with { MemoryCode = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.MemoryTextVariable => trade with { MemoryTextVariable = ValidateRange(edit.Value, 0, ushort.MaxValue) },
            SwShTradePokemonField.MemoryFeel => trade with { MemoryFeel = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.MemoryIntensity => trade with { MemoryIntensity = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.OtGender => trade with { OtGender = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.RequiredForm => trade with { RequiredForm = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.RequiredSpecies => trade with { RequiredSpecies = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.RequiredNature => trade with { RequiredNature = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.UnknownRequirement => trade with { UnknownRequirement = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.ShinyLock => trade with { ShinyLock = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.Nature => trade with { Nature = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.Gender => trade with { Gender = ValidateSByteRange(edit.Value) },
            SwShTradePokemonField.IvHp => trade with { Ivs = trade.Ivs with { Hp = ValidateHpIvValue(edit.Value) } },
            SwShTradePokemonField.IvAttack => trade with { Ivs = trade.Ivs with { Attack = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvDefense => trade with { Ivs = trade.Ivs with { Defense = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvSpeed => trade with { Ivs = trade.Ivs with { Speed = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvSpecialAttack => trade with { Ivs = trade.Ivs with { SpecialAttack = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvSpecialDefense => trade with { Ivs = trade.Ivs with { SpecialDefense = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.Ability => trade with { Ability = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.RelearnMove0 => trade with { RelearnMoves = SetRelearnMove(trade.RelearnMoves, 0, edit.Value) },
            SwShTradePokemonField.RelearnMove1 => trade with { RelearnMoves = SetRelearnMove(trade.RelearnMoves, 1, edit.Value) },
            SwShTradePokemonField.RelearnMove2 => trade with { RelearnMoves = SetRelearnMove(trade.RelearnMoves, 2, edit.Value) },
            SwShTradePokemonField.RelearnMove3 => trade with { RelearnMoves = SetRelearnMove(trade.RelearnMoves, 3, edit.Value) },
            SwShTradePokemonField.FlawlessIvCount => trade with { Ivs = CreateIvPreset(edit.Value) },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Trade Pokemon field '{edit.Field}' is not supported."),
        };
    }

    private static IReadOnlyList<int> SetRelearnMove(IReadOnlyList<int> moves, int slot, int value)
    {
        var updatedMoves = moves.ToArray();
        if ((uint)slot >= (uint)updatedMoves.Length)
        {
            throw new InvalidDataException($"Trade Pokemon relearn move slot {slot} is not present.");
        }

        updatedMoves[slot] = ValidateRange(value, 0, ushort.MaxValue);

        return updatedMoves;
    }

    private static SwShTradePokemonIvs CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShTradePokemonIvs(RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue),
            3 => new SwShTradePokemonIvs(
                ThreePerfectIvSentinel,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue),
            6 => new SwShTradePokemonIvs(
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue),
            _ => throw new ArgumentOutOfRangeException(
                nameof(flawlessIvCount),
                "Trade Pokemon flawless IV count must be 0, 3, or 6."),
        };
    }

    private static bool ValidateBool(int value)
    {
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Trade Pokemon boolean values must be 0 or 1."),
        };
    }

    private static int ValidateIvValue(int value)
    {
        if (value == RandomIvValue || value is >= MinimumFixedIvValue and <= MaximumFixedIvValue)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Trade Pokemon IV value {value} is outside the supported range {RandomIvValue}, {MinimumFixedIvValue}-{MaximumFixedIvValue}.");
    }

    private static int ValidateHpIvValue(int value)
    {
        if (value == ThreePerfectIvSentinel)
        {
            return value;
        }

        return ValidateIvValue(value);
    }

    private static int ValidateRange(int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Trade Pokemon value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static int ValidateSByteRange(int value)
    {
        if (value < sbyte.MinValue || value > sbyte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Trade Pokemon signed byte value {value} is outside the supported range {sbyte.MinValue}-{sbyte.MaxValue}.");
        }

        return value;
    }

    private static SwShTradePokemonRecord ReadTrade(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        return new SwShTradePokemonRecord(
            index,
            ReadTableByte(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 1, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 2, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 3, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 4, required: false),
            ReadTableBool(data, tableOffset, fieldIndex: 5, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 6, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 7, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 8, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 9, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 10, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 11, required: false),
            ReadTableUInt16(data, tableOffset, fieldIndex: 12, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 13, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 14, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 15, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 16, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 17, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 18, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 19, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 20, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 21, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 22, required: false),
            ReadTableSByte(data, tableOffset, fieldIndex: 23, required: false),
            new SwShTradePokemonIvs(
                ReadTableSByte(data, tableOffset, fieldIndex: 27, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 25, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 26, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 24, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 28, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 29, required: false)),
            ReadTableByte(data, tableOffset, fieldIndex: 30, required: false),
            [
                ReadTableUInt16(data, tableOffset, fieldIndex: 31, required: false),
                ReadTableUInt16(data, tableOffset, fieldIndex: 32, required: false),
                ReadTableUInt16(data, tableOffset, fieldIndex: 33, required: false),
                ReadTableUInt16(data, tableOffset, fieldIndex: 34, required: false),
            ]);
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(uint));
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
        var targetOffset = checked(offset + (int)relativeOffset);
        EnsureRange(data, targetOffset, sizeof(int));

        return targetOffset;
    }

    private static int ReadTableUOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        return ReadUOffset(data, tableOffset + fieldOffset);
    }

    private static bool ReadTableBool(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return false;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(byte));

        return data[tableOffset + fieldOffset] != 0;
    }

    private static byte ReadTableByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(byte));

        return data[tableOffset + fieldOffset];
    }

    private static sbyte ReadTableSByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(sbyte));

        return unchecked((sbyte)data[tableOffset + fieldOffset]);
    }

    private static ushort ReadTableUInt16(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(ushort));

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(ushort)));
    }

    private static int ReadTableInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(int));

        return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(int)));
    }

    private static ulong ReadTableUInt64(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(ulong));

        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(ulong)));
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        var vtableStart = tableOffset - vtableOffset;
        EnsureRange(data, vtableStart, sizeof(ushort) * 2);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart, sizeof(ushort)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        if (fieldEntryOffset + sizeof(ushort) > vtableLength)
        {
            return 0;
        }

        EnsureRange(data, vtableStart + fieldEntryOffset, sizeof(ushort));

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart + fieldEntryOffset, sizeof(ushort)));
    }

    private static T[] ReadTableVector<T>(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        Func<ReadOnlySpan<byte>, int, int, T> readTable)
    {
        var count = ReadVectorLength(data, vectorOffset);
        var values = new T[count];

        for (var index = 0; index < count; index++)
        {
            var elementOffset = vectorOffset + sizeof(uint) + (index * sizeof(uint));
            values[index] = readTable(data, ReadUOffset(data, elementOffset), index);
        }

        return values;
    }

    private static int ReadVectorLength(ReadOnlySpan<byte> data, int vectorOffset)
    {
        EnsureRange(data, vectorOffset, sizeof(uint));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vectorOffset, sizeof(uint)));
        if (count > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer vector is too large.");
        }

        return (int)count;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new InvalidDataException("FlatBuffer archive contains an invalid offset.");
        }
    }

    private sealed class TradeFlatBufferWriter
    {
        private const int TradeFieldCount = 35;
        private const int TradeVtableLength = sizeof(ushort) * 2 + (TradeFieldCount * sizeof(ushort));
        private readonly List<byte> bytes = [];

        public void Write(SwShTradePokemonArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var tradeVector = WriteTableVector(archive.Trades.Count);
            PatchUOffset(root.Field0Offset, tradeVector.VectorOffset);
            for (var index = 0; index < archive.Trades.Count; index++)
            {
                var tradeOffset = WriteTrade(archive.Trades[index]);
                PatchUOffset(tradeVector.ElementOffsets[index], tradeOffset);
            }
        }

        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        private TableFields WriteArchiveTable()
        {
            AlignForTable(vtableLength: 6, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(6);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var tradeFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, tradeFieldOffset);
        }

        private int WriteTrade(SwShTradePokemonRecord trade)
        {
            AlignForTable(vtableLength: TradeVtableLength, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(TradeVtableLength);
            WriteUInt16(96);
            WriteTradeFieldOffsets();

            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            WriteInt32(0); // Padding keeps 64-bit hashes 8-byte aligned.
            WriteUInt64(trade.Hash0);
            WriteUInt64(trade.Hash1);
            WriteUInt64(trade.Hash2);
            WriteInt32(trade.BallItemId);
            WriteInt32(trade.Field03);
            WriteInt32(trade.HeldItem);
            WriteInt32(trade.Species);
            WriteInt32(trade.TrainerId);
            WriteInt32(trade.RequiredSpecies);
            WriteInt32(trade.RequiredNature);
            WriteInt32(trade.ShinyLock);
            WriteInt32(trade.Nature);
            WriteUInt16(checked((ushort)trade.MemoryTextVariable));
            WriteUInt16(checked((ushort)trade.RelearnMoves[0]));
            WriteUInt16(checked((ushort)trade.RelearnMoves[1]));
            WriteUInt16(checked((ushort)trade.RelearnMoves[2]));
            WriteUInt16(checked((ushort)trade.RelearnMoves[3]));
            WriteByte(checked((byte)trade.Form));
            WriteByte(checked((byte)trade.DynamaxLevel));
            WriteByte(trade.CanGigantamax ? (byte)1 : (byte)0);
            WriteByte(checked((byte)trade.Level));
            WriteByte(checked((byte)trade.MemoryCode));
            WriteByte(checked((byte)trade.MemoryFeel));
            WriteByte(checked((byte)trade.MemoryIntensity));
            WriteByte(checked((byte)trade.OtGender));
            WriteByte(checked((byte)trade.RequiredForm));
            WriteByte(checked((byte)trade.UnknownRequirement));
            WriteSByte(checked((sbyte)trade.Gender));
            WriteSByte(checked((sbyte)trade.Ivs.Speed));
            WriteSByte(checked((sbyte)trade.Ivs.Attack));
            WriteSByte(checked((sbyte)trade.Ivs.Defense));
            WriteSByte(checked((sbyte)trade.Ivs.Hp));
            WriteSByte(checked((sbyte)trade.Ivs.SpecialAttack));
            WriteSByte(checked((sbyte)trade.Ivs.SpecialDefense));
            WriteByte(checked((byte)trade.Ability));

            return tableOffset;
        }

        private void WriteTradeFieldOffsets()
        {
            WriteUInt16(78); // Form
            WriteUInt16(79); // DynamaxLevel
            WriteUInt16(32); // BallItemID
            WriteUInt16(36); // Field_03
            WriteUInt16(8);  // Hash0
            WriteUInt16(80); // CanGigantamax
            WriteUInt16(40); // HeldItem
            WriteUInt16(81); // Level
            WriteUInt16(44); // Species
            WriteUInt16(16); // Hash1
            WriteUInt16(48); // TrainerID
            WriteUInt16(82); // Memory
            WriteUInt16(68); // TextVar
            WriteUInt16(83); // Feeling
            WriteUInt16(84); // Intensity
            WriteUInt16(24); // Hash2
            WriteUInt16(85); // OTGender
            WriteUInt16(86); // RequiredForm
            WriteUInt16(52); // RequiredSpecies
            WriteUInt16(56); // RequiredNature
            WriteUInt16(87); // UnknownRequirement
            WriteUInt16(60); // ShinyLock
            WriteUInt16(64); // Nature
            WriteUInt16(88); // Gender
            WriteUInt16(89); // IV_SPE
            WriteUInt16(90); // IV_ATK
            WriteUInt16(91); // IV_DEF
            WriteUInt16(92); // IV_HP
            WriteUInt16(93); // IV_SPA
            WriteUInt16(94); // IV_SPD
            WriteUInt16(95); // AbilityNumber
            WriteUInt16(70); // Relearn1
            WriteUInt16(72); // Relearn2
            WriteUInt16(74); // Relearn3
            WriteUInt16(76); // Relearn4
        }

        private VectorFields WriteTableVector(int count)
        {
            Align(4);
            var vectorOffset = Position;
            WriteUInt32(checked((uint)count));
            var elementOffsets = new int[count];
            for (var index = 0; index < count; index++)
            {
                elementOffsets[index] = Position;
                WriteUInt32(0);
            }

            return new VectorFields(vectorOffset, elementOffsets);
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            if (targetOffset <= sourceOffset)
            {
                throw new InvalidOperationException("FlatBuffer target offsets must point forward.");
            }

            WriteUInt32At(sourceOffset, checked((uint)(targetOffset - sourceOffset)));
        }

        private void AlignForTable(int vtableLength, int alignment)
        {
            while (((Position + vtableLength) % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private void Align(int alignment)
        {
            while ((Position % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private int Position => bytes.Count;

        private void WriteByte(byte value)
        {
            bytes.Add(value);
        }

        private void WriteSByte(sbyte value)
        {
            bytes.Add(unchecked((byte)value));
        }

        private void WriteUInt16(ushort value)
        {
            var start = Grow(sizeof(ushort));
            BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ushort)), value);
        }

        private void WriteInt32(int value)
        {
            var start = Grow(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(int)), value);
        }

        private void WriteUInt32(uint value)
        {
            var start = Grow(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(uint)), value);
        }

        private void WriteUInt64(ulong value)
        {
            var start = Grow(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ulong)), value);
        }

        private void WriteUInt32At(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(uint)), value);
        }

        private int Grow(int count)
        {
            var start = bytes.Count;
            for (var index = 0; index < count; index++)
            {
                bytes.Add(0);
            }

            return start;
        }

        private sealed record TableFields(int TableOffset, int Field0Offset);

        private sealed record VectorFields(
            int VectorOffset,
            IReadOnlyList<int> ElementOffsets);
    }
}
