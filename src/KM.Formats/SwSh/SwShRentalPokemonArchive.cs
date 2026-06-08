// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShRentalPokemonStats(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShRentalPokemonRecord(
    int Index,
    SwShRentalPokemonStats Evs,
    int Form,
    int BallItemId,
    ulong Hash1,
    int HeldItem,
    int Level,
    int Species,
    ulong Hash2,
    uint TrainerId,
    int Nature,
    int Gender,
    SwShRentalPokemonStats Ivs,
    int Ability,
    IReadOnlyList<int> Moves);

public enum SwShRentalPokemonField
{
    EvHp,
    EvAttack,
    EvDefense,
    EvSpeed,
    EvSpecialAttack,
    EvSpecialDefense,
    Form,
    BallItemId,
    HeldItem,
    Level,
    Species,
    TrainerId,
    Nature,
    Gender,
    IvHp,
    IvAttack,
    IvDefense,
    IvSpeed,
    IvSpecialAttack,
    IvSpecialDefense,
    Ability,
    Move0,
    Move1,
    Move2,
    Move3,
    FixedIvPreset,
}

public sealed record SwShRentalPokemonEdit(
    int RentalIndex,
    SwShRentalPokemonField Field,
    int Value);

public sealed record SwShRentalPokemonArchive(IReadOnlyList<SwShRentalPokemonRecord> Rentals)
{
    public const int MinimumFixedIvValue = 0;
    public const int MaximumFixedIvValue = 31;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumIdValue = int.MaxValue;

    public static SwShRentalPokemonArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Rental Pokemon archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var rentalVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var rentals = ReadTableVector(data, rentalVectorOffset, ReadRental);

        return new SwShRentalPokemonArchive(rentals);
    }

    public byte[] Write()
    {
        var writer = new RentalFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShRentalPokemonEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var rentals = Rentals
            .Select(rental => rental with
            {
                Evs = rental.Evs with { },
                Ivs = rental.Ivs with { },
                Moves = rental.Moves.ToArray(),
            })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(rentals, edit);
        }

        return new SwShRentalPokemonArchive(rentals).Write();
    }

    public static bool HasPerfectIvs(SwShRentalPokemonStats ivs)
    {
        return ivs.HP == MaximumFixedIvValue
            && ivs.Attack == MaximumFixedIvValue
            && ivs.Defense == MaximumFixedIvValue
            && ivs.Speed == MaximumFixedIvValue
            && ivs.SpecialAttack == MaximumFixedIvValue
            && ivs.SpecialDefense == MaximumFixedIvValue;
    }

    private static void ApplyEdit(IReadOnlyList<SwShRentalPokemonRecord> rentals, SwShRentalPokemonEdit edit)
    {
        if ((uint)edit.RentalIndex >= (uint)rentals.Count)
        {
            throw new InvalidDataException($"Rental Pokemon index {edit.RentalIndex} is not present.");
        }

        if (rentals is not SwShRentalPokemonRecord[] mutableRentals)
        {
            throw new InvalidDataException("Rental Pokemon list is not mutable.");
        }

        var rental = rentals[edit.RentalIndex];
        mutableRentals[edit.RentalIndex] = edit.Field switch
        {
            SwShRentalPokemonField.EvHp => rental with { Evs = rental.Evs with { HP = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShRentalPokemonField.EvAttack => rental with { Evs = rental.Evs with { Attack = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShRentalPokemonField.EvDefense => rental with { Evs = rental.Evs with { Defense = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShRentalPokemonField.EvSpeed => rental with { Evs = rental.Evs with { Speed = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShRentalPokemonField.EvSpecialAttack => rental with { Evs = rental.Evs with { SpecialAttack = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShRentalPokemonField.EvSpecialDefense => rental with { Evs = rental.Evs with { SpecialDefense = ValidateRange(edit.Value, 0, MaximumByteValue) } },
            SwShRentalPokemonField.Form => rental with { Form = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShRentalPokemonField.BallItemId => rental with { BallItemId = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShRentalPokemonField.HeldItem => rental with { HeldItem = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShRentalPokemonField.Level => rental with { Level = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShRentalPokemonField.Species => rental with { Species = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShRentalPokemonField.TrainerId => rental with { TrainerId = checked((uint)ValidateRange(edit.Value, 0, MaximumIdValue)) },
            SwShRentalPokemonField.Nature => rental with { Nature = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShRentalPokemonField.Gender => rental with { Gender = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShRentalPokemonField.IvHp => rental with { Ivs = rental.Ivs with { HP = ValidateIvValue(edit.Value) } },
            SwShRentalPokemonField.IvAttack => rental with { Ivs = rental.Ivs with { Attack = ValidateIvValue(edit.Value) } },
            SwShRentalPokemonField.IvDefense => rental with { Ivs = rental.Ivs with { Defense = ValidateIvValue(edit.Value) } },
            SwShRentalPokemonField.IvSpeed => rental with { Ivs = rental.Ivs with { Speed = ValidateIvValue(edit.Value) } },
            SwShRentalPokemonField.IvSpecialAttack => rental with { Ivs = rental.Ivs with { SpecialAttack = ValidateIvValue(edit.Value) } },
            SwShRentalPokemonField.IvSpecialDefense => rental with { Ivs = rental.Ivs with { SpecialDefense = ValidateIvValue(edit.Value) } },
            SwShRentalPokemonField.Ability => rental with { Ability = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShRentalPokemonField.Move0 => rental with { Moves = SetMove(rental.Moves, 0, edit.Value) },
            SwShRentalPokemonField.Move1 => rental with { Moves = SetMove(rental.Moves, 1, edit.Value) },
            SwShRentalPokemonField.Move2 => rental with { Moves = SetMove(rental.Moves, 2, edit.Value) },
            SwShRentalPokemonField.Move3 => rental with { Moves = SetMove(rental.Moves, 3, edit.Value) },
            SwShRentalPokemonField.FixedIvPreset => rental with { Ivs = CreateFixedIvPreset(edit.Value) },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Rental Pokemon field '{edit.Field}' is not supported."),
        };
    }

    private static IReadOnlyList<int> SetMove(IReadOnlyList<int> moves, int slot, int value)
    {
        var updatedMoves = moves.ToArray();
        if ((uint)slot >= (uint)updatedMoves.Length)
        {
            throw new InvalidDataException($"Rental Pokemon move slot {slot} is not present.");
        }

        updatedMoves[slot] = ValidateRange(value, 0, MaximumIdValue);

        return updatedMoves;
    }

    private static SwShRentalPokemonStats CreateFixedIvPreset(int value)
    {
        var fixedValue = ValidateIvValue(value);
        return new SwShRentalPokemonStats(fixedValue, fixedValue, fixedValue, fixedValue, fixedValue, fixedValue);
    }

    private static int ValidateIvValue(int value)
    {
        if (value is >= MinimumFixedIvValue and <= MaximumFixedIvValue)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Rental Pokemon IV value {value} is outside the supported range {MinimumFixedIvValue}-{MaximumFixedIvValue}.");
    }

    private static int ValidateRange(int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Rental Pokemon value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static SwShRentalPokemonRecord ReadRental(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        return new SwShRentalPokemonRecord(
            index,
            new SwShRentalPokemonStats(
                ReadTableByte(data, tableOffset, fieldIndex: 3, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 1, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 2, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 4, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 5, required: false),
                ReadTableByte(data, tableOffset, fieldIndex: 0, required: false)),
            ReadTableByte(data, tableOffset, fieldIndex: 6, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 7, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 8, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 9, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 10, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 11, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 12, required: false),
            ReadTableUInt32(data, tableOffset, fieldIndex: 13, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 14, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 15, required: false),
            new SwShRentalPokemonStats(
                ReadTableSByte(data, tableOffset, fieldIndex: 19, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 17, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 18, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 20, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 21, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 16, required: false)),
            ReadTableInt32(data, tableOffset, fieldIndex: 22, required: false),
            [
                ReadTableInt32(data, tableOffset, fieldIndex: 23, required: false),
                ReadTableInt32(data, tableOffset, fieldIndex: 24, required: false),
                ReadTableInt32(data, tableOffset, fieldIndex: 25, required: false),
                ReadTableInt32(data, tableOffset, fieldIndex: 26, required: false),
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

    private static uint ReadTableUInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
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

        EnsureRange(data, tableOffset + fieldOffset, sizeof(uint));

        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(uint)));
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

    private sealed class RentalFlatBufferWriter
    {
        private const int RentalFieldCount = 27;
        private const int RentalVtableLength = sizeof(ushort) * 2 + (RentalFieldCount * sizeof(ushort));
        private readonly List<byte> bytes = [];

        public void Write(SwShRentalPokemonArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var rentalVector = WriteTableVector(archive.Rentals.Count);
            PatchUOffset(root.Field0Offset, rentalVector.VectorOffset);
            for (var index = 0; index < archive.Rentals.Count; index++)
            {
                var rentalOffset = WriteRental(archive.Rentals[index]);
                PatchUOffset(rentalVector.ElementOffsets[index], rentalOffset);
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
            var rentalFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, rentalFieldOffset);
        }

        private int WriteRental(SwShRentalPokemonRecord rental)
        {
            AlignForTable(vtableLength: RentalVtableLength, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(RentalVtableLength);
            WriteUInt16(82);
            WriteRentalFieldOffsets();

            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            WriteInt32(0); // Padding keeps 64-bit hashes 8-byte aligned.
            WriteUInt64(rental.Hash1);
            WriteUInt64(rental.Hash2);
            WriteInt32(rental.BallItemId);
            WriteInt32(rental.HeldItem);
            WriteInt32(rental.Species);
            WriteUInt32(rental.TrainerId);
            WriteInt32(rental.Nature);
            WriteInt32(rental.Gender);
            WriteInt32(rental.Ability);
            WriteInt32(rental.Moves[0]);
            WriteInt32(rental.Moves[1]);
            WriteInt32(rental.Moves[2]);
            WriteInt32(rental.Moves[3]);
            WriteByte(checked((byte)rental.Evs.Speed));
            WriteByte(checked((byte)rental.Evs.Attack));
            WriteByte(checked((byte)rental.Evs.Defense));
            WriteByte(checked((byte)rental.Evs.HP));
            WriteByte(checked((byte)rental.Evs.SpecialAttack));
            WriteByte(checked((byte)rental.Evs.SpecialDefense));
            WriteByte(checked((byte)rental.Form));
            WriteByte(checked((byte)rental.Level));
            WriteSByte(checked((sbyte)rental.Ivs.Speed));
            WriteSByte(checked((sbyte)rental.Ivs.Attack));
            WriteSByte(checked((sbyte)rental.Ivs.Defense));
            WriteSByte(checked((sbyte)rental.Ivs.HP));
            WriteSByte(checked((sbyte)rental.Ivs.SpecialAttack));
            WriteSByte(checked((sbyte)rental.Ivs.SpecialDefense));

            return tableOffset;
        }

        private void WriteRentalFieldOffsets()
        {
            WriteUInt16(68); // EV_SPE
            WriteUInt16(69); // EV_ATK
            WriteUInt16(70); // EV_DEF
            WriteUInt16(71); // EV_HP
            WriteUInt16(72); // EV_SPA
            WriteUInt16(73); // EV_SPD
            WriteUInt16(74); // Form
            WriteUInt16(24); // Ball
            WriteUInt16(8);  // Hash1
            WriteUInt16(28); // Item
            WriteUInt16(75); // Level
            WriteUInt16(32); // Species
            WriteUInt16(16); // Hash2
            WriteUInt16(36); // TrainerID
            WriteUInt16(40); // Nature
            WriteUInt16(44); // Gender
            WriteUInt16(76); // IV_SPE
            WriteUInt16(77); // IV_ATK
            WriteUInt16(78); // IV_DEF
            WriteUInt16(79); // IV_HP
            WriteUInt16(80); // IV_SPA
            WriteUInt16(81); // IV_SPD
            WriteUInt16(48); // Ability
            WriteUInt16(52); // Move1
            WriteUInt16(56); // Move2
            WriteUInt16(60); // Move3
            WriteUInt16(64); // Move4
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
