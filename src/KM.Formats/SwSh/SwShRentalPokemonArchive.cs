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
    private const int RentalFieldCount = 27;

    public const int MinimumFixedIvValue = 0;
    public const int MaximumFixedIvValue = 31;
    public const int MinimumPokemonLevel = 1;
    public const int MaximumPokemonLevel = 100;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumIdValue = int.MaxValue;

    private byte[]? SourceData { get; init; }

    private IReadOnlyList<int>? SourceRentalTableOffsets { get; init; }

    private IReadOnlyList<int>? SourceRentalVectorElementOffsets { get; init; }

    public static SwShRentalPokemonArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Rental Pokemon archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var rentalVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var rentalCount = ReadVectorLength(data, rentalVectorOffset);
        var rentalTableOffsets = new int[rentalCount];
        var rentalVectorElementOffsets = new int[rentalCount];
        var rentals = new SwShRentalPokemonRecord[rentalCount];
        for (var index = 0; index < rentalCount; index++)
        {
            var elementOffset = rentalVectorOffset + sizeof(uint) + (index * sizeof(uint));
            var tableOffset = ReadUOffset(data, elementOffset);
            rentalVectorElementOffsets[index] = elementOffset;
            rentalTableOffsets[index] = tableOffset;
            rentals[index] = ReadRental(data, tableOffset, index);
        }

        return new SwShRentalPokemonArchive(rentals)
        {
            SourceData = data.ToArray(),
            SourceRentalTableOffsets = rentalTableOffsets,
            SourceRentalVectorElementOffsets = rentalVectorElementOffsets,
        };
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

        var materializedEdits = edits.ToArray();

        var rentals = Rentals
            .Select(rental => rental with
            {
                Evs = rental.Evs with { },
                Ivs = rental.Ivs with { },
                Moves = rental.Moves.ToArray(),
            })
            .ToArray();

        foreach (var edit in materializedEdits)
        {
            ApplyEdit(rentals, edit);
        }

        if (SourceData is not null
            && SourceRentalTableOffsets is not null
            && SourceRentalVectorElementOffsets is not null)
        {
            var finalEdits = materializedEdits
                .Select(edit => edit.RentalIndex)
                .Distinct()
                .Order()
                .SelectMany(rentalIndex => GetChangedScalarEdits(
                    Rentals[rentalIndex],
                    rentals[rentalIndex],
                    rentalIndex))
                .ToArray();
            if (finalEdits.Length == 0)
            {
                return SourceData.ToArray();
            }

            var outputBytes = new List<byte>(SourceData.Length + (finalEdits.Length * 8));
            outputBytes.AddRange(SourceData);
            var effectiveTableOffsets = SourceRentalTableOffsets.ToArray();
            foreach (var rentalEdits in finalEdits.GroupBy(edit => edit.RentalIndex))
            {
                var rentalIndex = rentalEdits.Key;
                var tableOffset = effectiveTableOffsets[rentalIndex];
                var missingEdits = rentalEdits
                    .Where(edit => edit.Value != 0
                        && ReadTableFieldOffset(
                            SourceData,
                            tableOffset,
                            GetScalarFieldIndex(edit.Field)) == 0)
                    .ToArray();
                if (missingEdits.Length == 0)
                {
                    continue;
                }

                var expandedTableOffset = AppendExpandedRentalTable(
                    outputBytes,
                    SourceData,
                    tableOffset,
                    missingEdits);
                PatchUOffset(
                    outputBytes,
                    SourceRentalVectorElementOffsets[rentalIndex],
                    expandedTableOffset);
                effectiveTableOffsets[rentalIndex] = expandedTableOffset;
            }

            var output = outputBytes.ToArray();
            foreach (var edit in finalEdits)
            {
                PatchEdit(output, effectiveTableOffsets, edit);
            }

            return output;
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
            SwShRentalPokemonField.Level => rental with
            {
                Level = ValidateRange(edit.Value, MinimumPokemonLevel, MaximumPokemonLevel),
            },
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

    private static IEnumerable<SwShRentalPokemonEdit> GetChangedScalarEdits(
        SwShRentalPokemonRecord original,
        SwShRentalPokemonRecord updated,
        int rentalIndex)
    {
        if (original.Moves.Count < 4 || updated.Moves.Count < 4)
        {
            throw new InvalidDataException("Rental Pokemon records must contain four move slots.");
        }

        if (original.Evs.HP != updated.Evs.HP)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.EvHp, updated.Evs.HP);
        }

        if (original.Evs.Attack != updated.Evs.Attack)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.EvAttack, updated.Evs.Attack);
        }

        if (original.Evs.Defense != updated.Evs.Defense)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.EvDefense, updated.Evs.Defense);
        }

        if (original.Evs.Speed != updated.Evs.Speed)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.EvSpeed, updated.Evs.Speed);
        }

        if (original.Evs.SpecialAttack != updated.Evs.SpecialAttack)
        {
            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                SwShRentalPokemonField.EvSpecialAttack,
                updated.Evs.SpecialAttack);
        }

        if (original.Evs.SpecialDefense != updated.Evs.SpecialDefense)
        {
            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                SwShRentalPokemonField.EvSpecialDefense,
                updated.Evs.SpecialDefense);
        }

        if (original.Form != updated.Form)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.Form, updated.Form);
        }

        if (original.BallItemId != updated.BallItemId)
        {
            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                SwShRentalPokemonField.BallItemId,
                updated.BallItemId);
        }

        if (original.HeldItem != updated.HeldItem)
        {
            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                SwShRentalPokemonField.HeldItem,
                updated.HeldItem);
        }

        if (original.Level != updated.Level)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.Level, updated.Level);
        }

        if (original.Species != updated.Species)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.Species, updated.Species);
        }

        if (original.TrainerId != updated.TrainerId)
        {
            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                SwShRentalPokemonField.TrainerId,
                checked((int)updated.TrainerId));
        }

        if (original.Nature != updated.Nature)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.Nature, updated.Nature);
        }

        if (original.Gender != updated.Gender)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.Gender, updated.Gender);
        }

        if (original.Ivs.HP != updated.Ivs.HP)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.IvHp, updated.Ivs.HP);
        }

        if (original.Ivs.Attack != updated.Ivs.Attack)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.IvAttack, updated.Ivs.Attack);
        }

        if (original.Ivs.Defense != updated.Ivs.Defense)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.IvDefense, updated.Ivs.Defense);
        }

        if (original.Ivs.Speed != updated.Ivs.Speed)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.IvSpeed, updated.Ivs.Speed);
        }

        if (original.Ivs.SpecialAttack != updated.Ivs.SpecialAttack)
        {
            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                SwShRentalPokemonField.IvSpecialAttack,
                updated.Ivs.SpecialAttack);
        }

        if (original.Ivs.SpecialDefense != updated.Ivs.SpecialDefense)
        {
            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                SwShRentalPokemonField.IvSpecialDefense,
                updated.Ivs.SpecialDefense);
        }

        if (original.Ability != updated.Ability)
        {
            yield return new SwShRentalPokemonEdit(rentalIndex, SwShRentalPokemonField.Ability, updated.Ability);
        }

        for (var moveIndex = 0; moveIndex < 4; moveIndex++)
        {
            if (original.Moves[moveIndex] == updated.Moves[moveIndex])
            {
                continue;
            }

            yield return new SwShRentalPokemonEdit(
                rentalIndex,
                (SwShRentalPokemonField)((int)SwShRentalPokemonField.Move0 + moveIndex),
                updated.Moves[moveIndex]);
        }
    }

    private static int GetScalarFieldIndex(SwShRentalPokemonField field)
    {
        return field switch
        {
            SwShRentalPokemonField.EvSpeed => 0,
            SwShRentalPokemonField.EvAttack => 1,
            SwShRentalPokemonField.EvDefense => 2,
            SwShRentalPokemonField.EvHp => 3,
            SwShRentalPokemonField.EvSpecialAttack => 4,
            SwShRentalPokemonField.EvSpecialDefense => 5,
            SwShRentalPokemonField.Form => 6,
            SwShRentalPokemonField.BallItemId => 7,
            SwShRentalPokemonField.HeldItem => 9,
            SwShRentalPokemonField.Level => 10,
            SwShRentalPokemonField.Species => 11,
            SwShRentalPokemonField.TrainerId => 13,
            SwShRentalPokemonField.Nature => 14,
            SwShRentalPokemonField.Gender => 15,
            SwShRentalPokemonField.IvSpeed => 16,
            SwShRentalPokemonField.IvAttack => 17,
            SwShRentalPokemonField.IvDefense => 18,
            SwShRentalPokemonField.IvHp => 19,
            SwShRentalPokemonField.IvSpecialAttack => 20,
            SwShRentalPokemonField.IvSpecialDefense => 21,
            SwShRentalPokemonField.Ability => 22,
            SwShRentalPokemonField.Move0 => 23,
            SwShRentalPokemonField.Move1 => 24,
            SwShRentalPokemonField.Move2 => 25,
            SwShRentalPokemonField.Move3 => 26,
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                $"Rental Pokemon field '{field}' is not a scalar field."),
        };
    }

    private static int GetRentalFieldSize(int fieldIndex)
    {
        return fieldIndex switch
        {
            >= 0 and <= 6 => sizeof(byte),
            7 or 9 or 11 or 13 or 14 or 15 or >= 22 and <= 26 => sizeof(int),
            8 or 12 => sizeof(ulong),
            10 or >= 16 and <= 21 => sizeof(byte),
            _ => throw new InvalidDataException($"Rental Pokemon field {fieldIndex} is not recognized."),
        };
    }

    private static int AppendExpandedRentalTable(
        List<byte> output,
        ReadOnlySpan<byte> source,
        int tableOffset,
        IReadOnlyList<SwShRentalPokemonEdit> missingEdits)
    {
        var layout = ReadRentalTableLayout(source, tableOffset, rejectUnknownFields: true);
        var missingFieldIndexes = missingEdits
            .Select(edit => GetScalarFieldIndex(edit.Field))
            .Distinct()
            .OrderByDescending(GetRentalFieldSize)
            .ThenBy(fieldIndex => fieldIndex)
            .ToArray();
        if (missingFieldIndexes.Length == 0)
        {
            throw new InvalidOperationException("No omitted Rental Pokemon fields were selected for materialization.");
        }

        foreach (var fieldIndex in missingFieldIndexes)
        {
            if (ReadTableFieldOffset(source, tableOffset, fieldIndex) != 0)
            {
                throw new InvalidOperationException(
                    $"Rental Pokemon field {fieldIndex} is already materialized in the source table.");
            }
        }

        var requiredVtableLength = checked(
            (sizeof(ushort) * 2) + ((missingFieldIndexes.Max() + 1) * sizeof(ushort)));
        var expandedVtableLength = Math.Max(layout.VtableLength, requiredVtableLength);
        if (expandedVtableLength > ushort.MaxValue)
        {
            throw new InvalidDataException("Expanded Rental Pokemon vtable is too large.");
        }

        AlignBuffer(output, sizeof(ushort));
        var expandedVtableOffset = output.Count;
        output.AddRange(source.Slice(layout.VtableOffset, layout.VtableLength).ToArray());
        GrowBuffer(output, expandedVtableLength - layout.VtableLength);
        WriteUInt16At(output, expandedVtableOffset, checked((ushort)expandedVtableLength));

        AlignBuffer(output, sizeof(ulong));
        var expandedTableOffset = output.Count;
        output.AddRange(source.Slice(tableOffset, layout.ObjectSize).ToArray());
        WriteInt32At(
            output,
            expandedTableOffset,
            checked(expandedTableOffset - expandedVtableOffset));

        foreach (var fieldIndex in missingFieldIndexes)
        {
            var fieldSize = GetRentalFieldSize(fieldIndex);
            AlignBuffer(output, fieldSize);
            var fieldOffset = checked(output.Count - expandedTableOffset);
            if (fieldOffset > ushort.MaxValue)
            {
                throw new InvalidDataException("Expanded Rental Pokemon table field offset is too large.");
            }

            GrowBuffer(output, fieldSize);
            WriteUInt16At(
                output,
                expandedVtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)),
                checked((ushort)fieldOffset));
        }

        var expandedObjectSize = checked(output.Count - expandedTableOffset);
        if (expandedObjectSize > ushort.MaxValue)
        {
            throw new InvalidDataException("Expanded Rental Pokemon table is too large.");
        }

        WriteUInt16At(
            output,
            expandedVtableOffset + sizeof(ushort),
            checked((ushort)expandedObjectSize));
        return expandedTableOffset;
    }

    private static RentalTableLayout ReadRentalTableLayout(
        ReadOnlySpan<byte> data,
        int tableOffset,
        bool rejectUnknownFields)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vtableDelta = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        if (vtableDelta == 0)
        {
            throw new InvalidDataException("Rental Pokemon table has an invalid zero vtable offset.");
        }

        var vtableOffsetValue = (long)tableOffset - vtableDelta;
        if (vtableOffsetValue < 0 || vtableOffsetValue > int.MaxValue)
        {
            throw new InvalidDataException("Rental Pokemon table has an invalid vtable offset.");
        }

        var vtableOffset = (int)vtableOffsetValue;
        EnsureRange(data, vtableOffset, sizeof(ushort) * 2);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(vtableOffset, sizeof(ushort)));
        var objectSize = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(vtableOffset + sizeof(ushort), sizeof(ushort)));
        if (vtableLength < sizeof(ushort) * 2
            || (vtableLength - (sizeof(ushort) * 2)) % sizeof(ushort) != 0)
        {
            throw new InvalidDataException("Rental Pokemon table has an invalid vtable length.");
        }

        if (objectSize < sizeof(int))
        {
            throw new InvalidDataException("Rental Pokemon table has an invalid object size.");
        }

        EnsureRange(data, vtableOffset, vtableLength);
        EnsureRange(data, tableOffset, objectSize);
        if (RangesOverlap(vtableOffset, vtableLength, tableOffset, objectSize))
        {
            throw new InvalidDataException("Rental Pokemon table overlaps its vtable.");
        }

        var materializedFieldRanges = new List<(int Start, int End, int FieldIndex)>();
        var fieldCount = (vtableLength - (sizeof(ushort) * 2)) / sizeof(ushort);
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(
                vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)),
                sizeof(ushort)));
            if (fieldOffset == 0)
            {
                continue;
            }

            if (fieldIndex >= RentalFieldCount)
            {
                if (rejectUnknownFields)
                {
                    throw new InvalidDataException(
                        $"Rental Pokemon table contains unknown field {fieldIndex} and cannot be safely expanded.");
                }

                continue;
            }

            var fieldSize = GetRentalFieldSize(fieldIndex);
            if (fieldOffset < sizeof(int) || fieldOffset > objectSize - fieldSize)
            {
                throw new InvalidDataException(
                    $"Rental Pokemon field {fieldIndex} points outside its table object.");
            }

            var absoluteFieldOffset = checked(tableOffset + fieldOffset);
            if (absoluteFieldOffset % fieldSize != 0)
            {
                throw new InvalidDataException(
                    $"Rental Pokemon field {fieldIndex} is not naturally aligned.");
            }

            var fieldEnd = fieldOffset + fieldSize;
            if (materializedFieldRanges.Any(range =>
                fieldOffset < range.End && fieldEnd > range.Start))
            {
                throw new InvalidDataException(
                    $"Rental Pokemon field {fieldIndex} overlaps another scalar field.");
            }

            materializedFieldRanges.Add((fieldOffset, fieldEnd, fieldIndex));
        }

        return new RentalTableLayout(vtableOffset, vtableLength, objectSize);
    }

    private static bool RangesOverlap(int firstOffset, int firstLength, int secondOffset, int secondLength)
    {
        return firstOffset < (long)secondOffset + secondLength
            && secondOffset < (long)firstOffset + firstLength;
    }

    private static void PatchUOffset(List<byte> data, int sourceOffset, int targetOffset)
    {
        if (targetOffset <= sourceOffset)
        {
            throw new InvalidDataException("Expanded Rental Pokemon table must follow its vector element.");
        }

        WriteUInt32At(data, sourceOffset, checked((uint)(targetOffset - sourceOffset)));
    }

    private static void AlignBuffer(List<byte> data, int alignment)
    {
        while (data.Count % alignment != 0)
        {
            data.Add(0);
        }
    }

    private static void GrowBuffer(List<byte> data, int count)
    {
        for (var index = 0; index < count; index++)
        {
            data.Add(0);
        }
    }

    private static void WriteUInt16At(List<byte> data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(
            CollectionsMarshal.AsSpan(data).Slice(offset, sizeof(ushort)),
            value);
    }

    private static void WriteInt32At(List<byte> data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(data).Slice(offset, sizeof(int)),
            value);
    }

    private static void WriteUInt32At(List<byte> data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(
            CollectionsMarshal.AsSpan(data).Slice(offset, sizeof(uint)),
            value);
    }

    private static void PatchEdit(
        Span<byte> data,
        IReadOnlyList<int> rentalTableOffsets,
        SwShRentalPokemonEdit edit)
    {
        if ((uint)edit.RentalIndex >= (uint)rentalTableOffsets.Count)
        {
            throw new InvalidDataException($"Rental Pokemon index {edit.RentalIndex} is not present.");
        }

        var tableOffset = rentalTableOffsets[edit.RentalIndex];
        switch (edit.Field)
        {
            case SwShRentalPokemonField.EvHp:
                PatchByte(data, tableOffset, fieldIndex: 3, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.EvAttack:
                PatchByte(data, tableOffset, fieldIndex: 1, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.EvDefense:
                PatchByte(data, tableOffset, fieldIndex: 2, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.EvSpeed:
                PatchByte(data, tableOffset, fieldIndex: 0, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.EvSpecialAttack:
                PatchByte(data, tableOffset, fieldIndex: 4, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.EvSpecialDefense:
                PatchByte(data, tableOffset, fieldIndex: 5, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.Form:
                PatchByte(data, tableOffset, fieldIndex: 6, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.BallItemId:
                PatchInt32(data, tableOffset, fieldIndex: 7, edit.Value);
                break;
            case SwShRentalPokemonField.HeldItem:
                PatchInt32(data, tableOffset, fieldIndex: 9, edit.Value);
                break;
            case SwShRentalPokemonField.Level:
                PatchByte(data, tableOffset, fieldIndex: 10, checked((byte)edit.Value));
                break;
            case SwShRentalPokemonField.Species:
                PatchInt32(data, tableOffset, fieldIndex: 11, edit.Value);
                break;
            case SwShRentalPokemonField.TrainerId:
                PatchUInt32(data, tableOffset, fieldIndex: 13, checked((uint)edit.Value));
                break;
            case SwShRentalPokemonField.Nature:
                PatchInt32(data, tableOffset, fieldIndex: 14, edit.Value);
                break;
            case SwShRentalPokemonField.Gender:
                PatchInt32(data, tableOffset, fieldIndex: 15, edit.Value);
                break;
            case SwShRentalPokemonField.IvHp:
                PatchSByte(data, tableOffset, fieldIndex: 19, checked((sbyte)edit.Value));
                break;
            case SwShRentalPokemonField.IvAttack:
                PatchSByte(data, tableOffset, fieldIndex: 17, checked((sbyte)edit.Value));
                break;
            case SwShRentalPokemonField.IvDefense:
                PatchSByte(data, tableOffset, fieldIndex: 18, checked((sbyte)edit.Value));
                break;
            case SwShRentalPokemonField.IvSpeed:
                PatchSByte(data, tableOffset, fieldIndex: 16, checked((sbyte)edit.Value));
                break;
            case SwShRentalPokemonField.IvSpecialAttack:
                PatchSByte(data, tableOffset, fieldIndex: 20, checked((sbyte)edit.Value));
                break;
            case SwShRentalPokemonField.IvSpecialDefense:
                PatchSByte(data, tableOffset, fieldIndex: 21, checked((sbyte)edit.Value));
                break;
            case SwShRentalPokemonField.Ability:
                PatchInt32(data, tableOffset, fieldIndex: 22, edit.Value);
                break;
            case SwShRentalPokemonField.Move0:
                PatchInt32(data, tableOffset, fieldIndex: 23, edit.Value);
                break;
            case SwShRentalPokemonField.Move1:
                PatchInt32(data, tableOffset, fieldIndex: 24, edit.Value);
                break;
            case SwShRentalPokemonField.Move2:
                PatchInt32(data, tableOffset, fieldIndex: 25, edit.Value);
                break;
            case SwShRentalPokemonField.Move3:
                PatchInt32(data, tableOffset, fieldIndex: 26, edit.Value);
                break;
            case SwShRentalPokemonField.FixedIvPreset:
                var fixedValue = checked((sbyte)edit.Value);
                foreach (var fieldIndex in new[] { 16, 17, 18, 19, 20, 21 })
                {
                    PatchSByte(data, tableOffset, fieldIndex, fixedValue);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), $"Rental Pokemon field '{edit.Field}' is not supported.");
        }
    }

    private static void PatchByte(Span<byte> data, int tableOffset, int fieldIndex, byte value)
    {
        var valueOffset = ResolvePatchOffset(data, tableOffset, fieldIndex, value == 0, sizeof(byte));
        if (valueOffset >= 0)
        {
            data[valueOffset] = value;
        }
    }

    private static void PatchSByte(Span<byte> data, int tableOffset, int fieldIndex, sbyte value)
    {
        PatchByte(data, tableOffset, fieldIndex, unchecked((byte)value));
    }

    private static void PatchInt32(Span<byte> data, int tableOffset, int fieldIndex, int value)
    {
        var valueOffset = ResolvePatchOffset(data, tableOffset, fieldIndex, value == 0, sizeof(int));
        if (valueOffset >= 0)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data[valueOffset..], value);
        }
    }

    private static void PatchUInt32(Span<byte> data, int tableOffset, int fieldIndex, uint value)
    {
        var valueOffset = ResolvePatchOffset(data, tableOffset, fieldIndex, value == 0, sizeof(uint));
        if (valueOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data[valueOffset..], value);
        }
    }

    private static int ResolvePatchOffset(
        ReadOnlySpan<byte> data,
        int tableOffset,
        int fieldIndex,
        bool isDefaultValue,
        int valueSize)
    {
        _ = ReadRentalTableLayout(data, tableOffset, rejectUnknownFields: false);
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (isDefaultValue)
            {
                return -1;
            }

            throw new InvalidDataException(
                $"Rental Pokemon field {fieldIndex} is not materialized in the source table and cannot be edited without rebuilding it.");
        }

        var valueOffset = checked(tableOffset + fieldOffset);
        EnsureRange(data, valueOffset, valueSize);
        return valueOffset;
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
            WriteByte(checked((byte)ValidateRange(
                rental.Level,
                MinimumPokemonLevel,
                MaximumPokemonLevel)));
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

    private readonly record struct RentalTableLayout(
        int VtableOffset,
        int VtableLength,
        int ObjectSize);
}
