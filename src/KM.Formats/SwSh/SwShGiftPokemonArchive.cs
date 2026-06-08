// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShGiftPokemonIvs(
    int Hp,
    int Attack,
    int Defense,
    int Speed,
    int SpecialAttack,
    int SpecialDefense);

public sealed record SwShGiftPokemonRecord(
    int Index,
    int IsEgg,
    int Form,
    int DynamaxLevel,
    int BallItemId,
    int Field04,
    ulong Hash1,
    bool CanGigantamax,
    int HeldItem,
    int Level,
    int Species,
    int Field0A,
    int MemoryCode,
    int MemoryData,
    int MemoryFeel,
    int MemoryLevel,
    ulong OtNameId,
    int OtGender,
    int ShinyLock,
    int Nature,
    int Gender,
    SwShGiftPokemonIvs Ivs,
    int Ability,
    int SpecialMove);

public enum SwShGiftPokemonField
{
    IsEgg,
    Form,
    DynamaxLevel,
    BallItemId,
    CanGigantamax,
    HeldItem,
    Level,
    Species,
    OtGender,
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
    SpecialMove,
    FlawlessIvCount,
}

public sealed record SwShGiftPokemonEdit(
    int GiftIndex,
    SwShGiftPokemonField Field,
    int Value);

public sealed record SwShGiftPokemonArchive(IReadOnlyList<SwShGiftPokemonRecord> Gifts)
{
    public const int RandomIvValue = -1;
    public const int ThreePerfectIvSentinel = -4;
    public const int MinimumFixedIvValue = 0;
    public const int MaximumFixedIvValue = 31;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumIdValue = int.MaxValue;

    public static SwShGiftPokemonArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Gift Pokemon archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var giftsVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var gifts = ReadTableVector(data, giftsVectorOffset, ReadGift);

        return new SwShGiftPokemonArchive(gifts);
    }

    public byte[] Write()
    {
        var writer = new GiftFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShGiftPokemonEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var gifts = Gifts
            .Select(gift => gift with { Ivs = gift.Ivs with { } })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(gifts, edit);
        }

        return new SwShGiftPokemonArchive(gifts).Write();
    }

    public static int? GetFlawlessIvCount(SwShGiftPokemonIvs ivs)
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

    private static void ApplyEdit(IReadOnlyList<SwShGiftPokemonRecord> gifts, SwShGiftPokemonEdit edit)
    {
        if ((uint)edit.GiftIndex >= (uint)gifts.Count)
        {
            throw new InvalidDataException($"Gift Pokemon index {edit.GiftIndex} is not present.");
        }

        if (gifts is not SwShGiftPokemonRecord[] mutableGifts)
        {
            throw new InvalidDataException("Gift Pokemon list is not mutable.");
        }

        var gift = gifts[edit.GiftIndex];
        mutableGifts[edit.GiftIndex] = edit.Field switch
        {
            SwShGiftPokemonField.IsEgg => gift with { IsEgg = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.Form => gift with { Form = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShGiftPokemonField.DynamaxLevel => gift with { DynamaxLevel = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShGiftPokemonField.BallItemId => gift with { BallItemId = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.CanGigantamax => gift with { CanGigantamax = ValidateBool(edit.Value) },
            SwShGiftPokemonField.HeldItem => gift with { HeldItem = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.Level => gift with { Level = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShGiftPokemonField.Species => gift with { Species = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.OtGender => gift with { OtGender = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.ShinyLock => gift with { ShinyLock = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.Nature => gift with { Nature = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.Gender => gift with { Gender = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShGiftPokemonField.IvHp => gift with { Ivs = gift.Ivs with { Hp = ValidateHpIvValue(edit.Value) } },
            SwShGiftPokemonField.IvAttack => gift with { Ivs = gift.Ivs with { Attack = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvDefense => gift with { Ivs = gift.Ivs with { Defense = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvSpeed => gift with { Ivs = gift.Ivs with { Speed = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvSpecialAttack => gift with { Ivs = gift.Ivs with { SpecialAttack = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvSpecialDefense => gift with { Ivs = gift.Ivs with { SpecialDefense = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.Ability => gift with { Ability = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.SpecialMove => gift with { SpecialMove = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.FlawlessIvCount => gift with { Ivs = CreateIvPreset(edit.Value) },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Gift Pokemon field '{edit.Field}' is not supported."),
        };
    }

    private static SwShGiftPokemonIvs CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShGiftPokemonIvs(RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue, RandomIvValue),
            3 => new SwShGiftPokemonIvs(
                ThreePerfectIvSentinel,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue,
                RandomIvValue),
            6 => new SwShGiftPokemonIvs(
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue,
                MaximumFixedIvValue),
            _ => throw new ArgumentOutOfRangeException(
                nameof(flawlessIvCount),
                "Gift Pokemon flawless IV count must be 0, 3, or 6."),
        };
    }

    private static bool ValidateBool(int value)
    {
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Gift Pokemon boolean values must be 0 or 1."),
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
            $"Gift Pokemon IV value {value} is outside the supported range {RandomIvValue}, {MinimumFixedIvValue}-{MaximumFixedIvValue}.");
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
                $"Gift Pokemon value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static SwShGiftPokemonRecord ReadGift(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        return new SwShGiftPokemonRecord(
            index,
            ReadTableInt32(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 1, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 2, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 3, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 4, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 5, required: false),
            ReadTableBool(data, tableOffset, fieldIndex: 6, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 7, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 8, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 9, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 10, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 11, required: false),
            ReadTableUInt16(data, tableOffset, fieldIndex: 12, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 13, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 14, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 15, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 16, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 17, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 18, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 19, required: false),
            new SwShGiftPokemonIvs(
                ReadTableSByte(data, tableOffset, fieldIndex: 23, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 21, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 22, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 20, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 24, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 25, required: false)),
            ReadTableInt32(data, tableOffset, fieldIndex: 26, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 27, required: false));
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

    private sealed class GiftFlatBufferWriter
    {
        private const int GiftFieldCount = 28;
        private const int GiftVtableLength = sizeof(ushort) * 2 + (GiftFieldCount * sizeof(ushort));
        private readonly List<byte> bytes = [];

        public void Write(SwShGiftPokemonArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var giftVector = WriteTableVector(archive.Gifts.Count);
            PatchUOffset(root.Field0Offset, giftVector.VectorOffset);
            for (var index = 0; index < archive.Gifts.Count; index++)
            {
                var giftOffset = WriteGift(archive.Gifts[index]);
                PatchUOffset(giftVector.ElementOffsets[index], giftOffset);
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
            var giftFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, giftFieldOffset);
        }

        private int WriteGift(SwShGiftPokemonRecord gift)
        {
            AlignForTable(vtableLength: GiftVtableLength, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(GiftVtableLength);
            WriteUInt16(74);
            WriteGiftFieldOffsets();

            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            WriteInt32(gift.IsEgg);
            WriteInt32(gift.BallItemId);
            WriteInt32(gift.HeldItem);
            WriteInt32(gift.Species);
            WriteInt32(gift.OtGender);
            WriteInt32(gift.ShinyLock);
            WriteInt32(gift.Nature);
            WriteInt32(gift.Ability);
            WriteInt32(gift.SpecialMove);
            WriteUInt64(gift.Hash1);
            WriteUInt64(gift.OtNameId);
            WriteUInt16(checked((ushort)gift.MemoryData));
            WriteByte(checked((byte)gift.Form));
            WriteByte(checked((byte)gift.DynamaxLevel));
            WriteByte(checked((byte)gift.Field04));
            WriteByte(gift.CanGigantamax ? (byte)1 : (byte)0);
            WriteByte(checked((byte)gift.Level));
            WriteByte(checked((byte)gift.Field0A));
            WriteByte(checked((byte)gift.MemoryCode));
            WriteByte(checked((byte)gift.MemoryFeel));
            WriteByte(checked((byte)gift.MemoryLevel));
            WriteByte(checked((byte)gift.Gender));
            WriteSByte(checked((sbyte)gift.Ivs.Speed));
            WriteSByte(checked((sbyte)gift.Ivs.Attack));
            WriteSByte(checked((sbyte)gift.Ivs.Defense));
            WriteSByte(checked((sbyte)gift.Ivs.Hp));
            WriteSByte(checked((sbyte)gift.Ivs.SpecialAttack));
            WriteSByte(checked((sbyte)gift.Ivs.SpecialDefense));

            return tableOffset;
        }

        private void WriteGiftFieldOffsets()
        {
            WriteUInt16(4);  // IsEgg
            WriteUInt16(58); // Form
            WriteUInt16(59); // DynamaxLevel
            WriteUInt16(8);  // BallItemID
            WriteUInt16(60); // Field_04
            WriteUInt16(40); // Hash1
            WriteUInt16(61); // CanGigantamax
            WriteUInt16(12); // HeldItem
            WriteUInt16(62); // Level
            WriteUInt16(16); // Species
            WriteUInt16(63); // Field_0A
            WriteUInt16(64); // MemoryCode
            WriteUInt16(56); // MemoryData
            WriteUInt16(65); // MemoryFeel
            WriteUInt16(66); // MemoryLevel
            WriteUInt16(48); // OtNameID
            WriteUInt16(20); // OtGender
            WriteUInt16(24); // ShinyLock
            WriteUInt16(28); // Nature
            WriteUInt16(67); // Gender
            WriteUInt16(68); // IV_SPE
            WriteUInt16(69); // IV_ATK
            WriteUInt16(70); // IV_DEF
            WriteUInt16(71); // IV_HP
            WriteUInt16(72); // IV_SPA
            WriteUInt16(73); // IV_SPD
            WriteUInt16(32); // Ability
            WriteUInt16(36); // SpecialMove
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
