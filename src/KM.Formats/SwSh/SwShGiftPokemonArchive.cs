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
    private const int GiftFieldCount = 28;

    public const int RandomIvValue = -1;
    public const int ThreePerfectIvSentinel = -4;
    public const int MinimumFixedIvValue = 0;
    public const int MaximumFixedIvValue = 31;
    public const int MaximumDynamaxLevel = 10;
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 100;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumIdValue = int.MaxValue;

    public static IReadOnlyList<int> ValidBallItemIds { get; } = Array.AsReadOnly<int>(
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
    ]);

    public static bool IsValidBallItemId(int itemId)
    {
        return itemId == 0
            || itemId is >= 1 and <= 16
            || itemId is >= 492 and <= 499
            || itemId is 576 or 851;
    }

    private byte[]? SourceData { get; init; }

    private IReadOnlyList<int>? SourceGiftTableOffsets { get; init; }

    private IReadOnlyList<int>? SourceGiftVectorElementOffsets { get; init; }

    public static SwShGiftPokemonArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Gift Pokemon archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var giftsVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var count = ReadVectorLength(data, giftsVectorOffset);
        var gifts = new SwShGiftPokemonRecord[count];
        var tableOffsets = new int[count];
        var vectorElementOffsets = new int[count];
        for (var index = 0; index < count; index++)
        {
            var elementOffset = checked(giftsVectorOffset + sizeof(uint) + (index * sizeof(uint)));
            var tableOffset = ReadUOffset(data, elementOffset);
            vectorElementOffsets[index] = elementOffset;
            tableOffsets[index] = tableOffset;
            gifts[index] = ReadGift(data, tableOffset, index);
        }

        return new SwShGiftPokemonArchive(gifts)
        {
            SourceData = data.ToArray(),
            SourceGiftTableOffsets = tableOffsets,
            SourceGiftVectorElementOffsets = vectorElementOffsets,
        };
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

        var materializedEdits = edits.ToArray();

        var gifts = Gifts
            .Select(gift => gift with { Ivs = gift.Ivs with { } })
            .ToArray();

        foreach (var edit in materializedEdits)
        {
            ApplyEdit(gifts, edit);
        }

        ValidateTouchedIvLayouts(gifts, materializedEdits);

        if (SourceData is not null
            && SourceGiftTableOffsets is not null
            && SourceGiftVectorElementOffsets is not null)
        {
            return WriteEditsInPlace(gifts);
        }

        return new SwShGiftPokemonArchive(gifts).Write();
    }

    private static void ValidateTouchedIvLayouts(
        IReadOnlyList<SwShGiftPokemonRecord> gifts,
        IReadOnlyList<SwShGiftPokemonEdit> edits)
    {
        var ivTouchedGiftIndexes = edits
            .Where(edit => edit.Field is
                SwShGiftPokemonField.IvHp
                or SwShGiftPokemonField.IvAttack
                or SwShGiftPokemonField.IvDefense
                or SwShGiftPokemonField.IvSpeed
                or SwShGiftPokemonField.IvSpecialAttack
                or SwShGiftPokemonField.IvSpecialDefense
                or SwShGiftPokemonField.FlawlessIvCount)
            .Select(edit => edit.GiftIndex)
            .Distinct();
        foreach (var giftIndex in ivTouchedGiftIndexes)
        {
            var ivs = gifts[giftIndex].Ivs;
            if (ivs.Hp == ThreePerfectIvSentinel
                && (ivs.Attack != RandomIvValue
                    || ivs.Defense != RandomIvValue
                    || ivs.Speed != RandomIvValue
                    || ivs.SpecialAttack != RandomIvValue
                    || ivs.SpecialDefense != RandomIvValue))
            {
                throw new ArgumentException(
                    "Gift Pokemon HP IV -4 requires all five other IVs to be -1.",
                    nameof(edits));
            }
        }
    }

    public static int? GetFlawlessIvCount(SwShGiftPokemonIvs ivs)
    {
        if (ivs.Hp == ThreePerfectIvSentinel
            && ivs.Attack == RandomIvValue
            && ivs.Defense == RandomIvValue
            && ivs.Speed == RandomIvValue
            && ivs.SpecialAttack == RandomIvValue
            && ivs.SpecialDefense == RandomIvValue)
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
            SwShGiftPokemonField.IsEgg => gift with { IsEgg = ValidateBooleanInt(edit.Value) },
            SwShGiftPokemonField.Form => gift with { Form = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShGiftPokemonField.DynamaxLevel => gift with { DynamaxLevel = ValidateRange(edit.Value, 0, MaximumDynamaxLevel) },
            SwShGiftPokemonField.BallItemId => gift with { BallItemId = ValidateBallItemId(edit.Value) },
            SwShGiftPokemonField.CanGigantamax => gift with { CanGigantamax = ValidateBool(edit.Value) },
            SwShGiftPokemonField.HeldItem => gift with { HeldItem = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShGiftPokemonField.Level => gift with { Level = ValidateRange(edit.Value, MinimumLevel, MaximumLevel) },
            SwShGiftPokemonField.Species => gift with { Species = ValidateRange(edit.Value, 1, MaximumIdValue) },
            SwShGiftPokemonField.OtGender => gift with { OtGender = ValidateBooleanInt(edit.Value) },
            SwShGiftPokemonField.ShinyLock => gift with { ShinyLock = ValidateRange(edit.Value, 0, 2) },
            SwShGiftPokemonField.Nature => gift with { Nature = ValidateRange(edit.Value, 0, 25) },
            SwShGiftPokemonField.Gender => gift with { Gender = ValidateRange(edit.Value, 0, 2) },
            SwShGiftPokemonField.IvHp => gift with { Ivs = gift.Ivs with { Hp = ValidateHpIvValue(edit.Value) } },
            SwShGiftPokemonField.IvAttack => gift with { Ivs = gift.Ivs with { Attack = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvDefense => gift with { Ivs = gift.Ivs with { Defense = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvSpeed => gift with { Ivs = gift.Ivs with { Speed = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvSpecialAttack => gift with { Ivs = gift.Ivs with { SpecialAttack = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.IvSpecialDefense => gift with { Ivs = gift.Ivs with { SpecialDefense = ValidateIvValue(edit.Value) } },
            SwShGiftPokemonField.Ability => gift with { Ability = ValidateRange(edit.Value, 0, 3) },
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

    private static int ValidateBooleanInt(int value)
    {
        return value switch
        {
            0 => 0,
            1 => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Gift Pokemon boolean values must be 0 or 1."),
        };
    }

    private static int ValidateBallItemId(int value)
    {
        if (IsValidBallItemId(value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Gift Pokemon ball item ID {value} is not a supported Sword/Shield ball item ID.");
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

    private byte[] WriteEditsInPlace(IReadOnlyList<SwShGiftPokemonRecord> gifts)
    {
        if (SourceData is null
            || SourceGiftTableOffsets is null
            || SourceGiftVectorElementOffsets is null
            || gifts.Count != Gifts.Count
            || SourceGiftTableOffsets.Count != Gifts.Count
            || SourceGiftVectorElementOffsets.Count != Gifts.Count)
        {
            throw new InvalidDataException("Gift Pokemon archive source layout is unavailable or inconsistent.");
        }

        var changedFieldsByGift = Enumerable.Range(0, gifts.Count)
            .Select(index => new
            {
                GiftIndex = index,
                FieldIndexes = GetChangedFieldIndexes(Gifts[index], gifts[index]),
            })
            .Where(change => change.FieldIndexes.Count > 0)
            .ToArray();
        if (changedFieldsByGift.Length == 0)
        {
            return SourceData.ToArray();
        }

        var outputBytes = new List<byte>(SourceData.Length + (changedFieldsByGift.Length * 16));
        outputBytes.AddRange(SourceData);
        var effectiveTableOffsets = SourceGiftTableOffsets.ToArray();
        var aliasedTableOffsets = SourceGiftTableOffsets
            .GroupBy(tableOffset => tableOffset)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        foreach (var change in changedFieldsByGift)
        {
            var tableOffset = effectiveTableOffsets[change.GiftIndex];
            var missingFieldIndexes = change.FieldIndexes
                .Where(fieldIndex => ReadTableFieldOffset(
                    SourceData,
                    tableOffset,
                    fieldIndex,
                    GetGiftFieldSize(fieldIndex)) == 0)
                .ToArray();
            if (missingFieldIndexes.Length == 0 && !aliasedTableOffsets.Contains(tableOffset))
            {
                continue;
            }

            var expandedTableOffset = AppendGiftTableCopy(
                outputBytes,
                SourceData,
                tableOffset,
                missingFieldIndexes);
            PatchUOffset(
                outputBytes,
                SourceGiftVectorElementOffsets[change.GiftIndex],
                expandedTableOffset);
            effectiveTableOffsets[change.GiftIndex] = expandedTableOffset;
        }

        var output = outputBytes.ToArray();
        foreach (var change in changedFieldsByGift)
        {
            var original = Gifts[change.GiftIndex];
            var updated = gifts[change.GiftIndex];
            var tableOffset = effectiveTableOffsets[change.GiftIndex];

            PatchInt32(output, tableOffset, 0, original.IsEgg, updated.IsEgg);
            PatchByte(output, tableOffset, 1, original.Form, updated.Form);
            PatchByte(output, tableOffset, 2, original.DynamaxLevel, updated.DynamaxLevel);
            PatchInt32(output, tableOffset, 3, original.BallItemId, updated.BallItemId);
            PatchBool(output, tableOffset, 6, original.CanGigantamax, updated.CanGigantamax);
            PatchInt32(output, tableOffset, 7, original.HeldItem, updated.HeldItem);
            PatchByte(output, tableOffset, 8, original.Level, updated.Level);
            PatchInt32(output, tableOffset, 9, original.Species, updated.Species);
            PatchInt32(output, tableOffset, 16, original.OtGender, updated.OtGender);
            PatchInt32(output, tableOffset, 17, original.ShinyLock, updated.ShinyLock);
            PatchInt32(output, tableOffset, 18, original.Nature, updated.Nature);
            PatchByte(output, tableOffset, 19, original.Gender, updated.Gender);
            PatchSByte(output, tableOffset, 23, original.Ivs.Hp, updated.Ivs.Hp);
            PatchSByte(output, tableOffset, 21, original.Ivs.Attack, updated.Ivs.Attack);
            PatchSByte(output, tableOffset, 22, original.Ivs.Defense, updated.Ivs.Defense);
            PatchSByte(output, tableOffset, 20, original.Ivs.Speed, updated.Ivs.Speed);
            PatchSByte(output, tableOffset, 24, original.Ivs.SpecialAttack, updated.Ivs.SpecialAttack);
            PatchSByte(output, tableOffset, 25, original.Ivs.SpecialDefense, updated.Ivs.SpecialDefense);
            PatchInt32(output, tableOffset, 26, original.Ability, updated.Ability);
            PatchInt32(output, tableOffset, 27, original.SpecialMove, updated.SpecialMove);
        }

        return output;
    }

    private static IReadOnlyList<int> GetChangedFieldIndexes(
        SwShGiftPokemonRecord original,
        SwShGiftPokemonRecord updated)
    {
        var fieldIndexes = new List<int>();
        AddIfChanged(0, original.IsEgg, updated.IsEgg);
        AddIfChanged(1, original.Form, updated.Form);
        AddIfChanged(2, original.DynamaxLevel, updated.DynamaxLevel);
        AddIfChanged(3, original.BallItemId, updated.BallItemId);
        AddIfChanged(6, original.CanGigantamax, updated.CanGigantamax);
        AddIfChanged(7, original.HeldItem, updated.HeldItem);
        AddIfChanged(8, original.Level, updated.Level);
        AddIfChanged(9, original.Species, updated.Species);
        AddIfChanged(16, original.OtGender, updated.OtGender);
        AddIfChanged(17, original.ShinyLock, updated.ShinyLock);
        AddIfChanged(18, original.Nature, updated.Nature);
        AddIfChanged(19, original.Gender, updated.Gender);
        AddIfChanged(23, original.Ivs.Hp, updated.Ivs.Hp);
        AddIfChanged(21, original.Ivs.Attack, updated.Ivs.Attack);
        AddIfChanged(22, original.Ivs.Defense, updated.Ivs.Defense);
        AddIfChanged(20, original.Ivs.Speed, updated.Ivs.Speed);
        AddIfChanged(24, original.Ivs.SpecialAttack, updated.Ivs.SpecialAttack);
        AddIfChanged(25, original.Ivs.SpecialDefense, updated.Ivs.SpecialDefense);
        AddIfChanged(26, original.Ability, updated.Ability);
        AddIfChanged(27, original.SpecialMove, updated.SpecialMove);
        return fieldIndexes;

        void AddIfChanged<T>(int fieldIndex, T originalValue, T updatedValue)
        {
            if (!EqualityComparer<T>.Default.Equals(originalValue, updatedValue))
            {
                fieldIndexes.Add(fieldIndex);
            }
        }
    }

    private static int GetGiftFieldSize(int fieldIndex)
    {
        return fieldIndex switch
        {
            0 or 3 or 7 or 9 or 16 or 17 or 18 or 26 or 27 => sizeof(int),
            5 or 15 => sizeof(ulong),
            12 => sizeof(ushort),
            >= 1 and <= 2 or 4 or 6 or 8 or >= 10 and <= 11 or >= 13 and <= 14 or >= 19 and <= 25 => sizeof(byte),
            _ => throw new InvalidDataException($"Gift Pokemon field {fieldIndex} is not recognized."),
        };
    }

    private static int AppendGiftTableCopy(
        List<byte> output,
        ReadOnlySpan<byte> source,
        int tableOffset,
        IReadOnlyList<int> missingFieldIndexes)
    {
        var layout = ReadGiftTableLayout(source, tableOffset, rejectUnknownFields: true);
        var orderedFieldIndexes = missingFieldIndexes
            .Distinct()
            .OrderByDescending(GetGiftFieldSize)
            .ThenBy(fieldIndex => fieldIndex)
            .ToArray();

        foreach (var fieldIndex in orderedFieldIndexes)
        {
            if (ReadTableFieldOffset(source, tableOffset, fieldIndex, GetGiftFieldSize(fieldIndex)) != 0)
            {
                throw new InvalidOperationException(
                    $"Gift Pokemon field {fieldIndex} is already materialized in the source table.");
            }
        }

        var requiredVtableLength = orderedFieldIndexes.Length == 0
            ? layout.VtableLength
            : checked((sizeof(ushort) * 2) + ((orderedFieldIndexes.Max() + 1) * sizeof(ushort)));
        var expandedVtableLength = Math.Max(layout.VtableLength, requiredVtableLength);
        if (expandedVtableLength > ushort.MaxValue)
        {
            throw new InvalidDataException("Expanded Gift Pokemon vtable is too large.");
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

        foreach (var fieldIndex in orderedFieldIndexes)
        {
            var fieldSize = GetGiftFieldSize(fieldIndex);
            AlignBuffer(output, fieldSize);
            var fieldOffset = checked(output.Count - expandedTableOffset);
            if (fieldOffset > ushort.MaxValue)
            {
                throw new InvalidDataException("Expanded Gift Pokemon table field offset is too large.");
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
            throw new InvalidDataException("Expanded Gift Pokemon table is too large.");
        }

        WriteUInt16At(
            output,
            expandedVtableOffset + sizeof(ushort),
            checked((ushort)expandedObjectSize));
        return expandedTableOffset;
    }

    private static TableLayout ReadGiftTableLayout(
        ReadOnlySpan<byte> data,
        int tableOffset,
        bool rejectUnknownFields)
    {
        var layout = ReadTableLayout(data, tableOffset);
        var materializedFieldRanges = new List<(int Start, int End)>();
        var fieldCount = (layout.VtableLength - (sizeof(ushort) * 2)) / sizeof(ushort);
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var fieldEntryOffset = layout.VtableOffset
                + (sizeof(ushort) * 2)
                + (fieldIndex * sizeof(ushort));
            var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
                data.Slice(fieldEntryOffset, sizeof(ushort)));
            if (fieldOffset == 0)
            {
                continue;
            }

            if (fieldIndex >= GiftFieldCount)
            {
                if (rejectUnknownFields)
                {
                    throw new InvalidDataException(
                        $"Gift Pokemon table contains unknown field {fieldIndex} and cannot be safely expanded.");
                }

                continue;
            }

            var fieldSize = GetGiftFieldSize(fieldIndex);
            _ = ReadTableFieldOffset(data, tableOffset, fieldIndex, fieldSize);
            var fieldEnd = checked(fieldOffset + fieldSize);
            if (materializedFieldRanges.Any(range =>
                fieldOffset < range.End && fieldEnd > range.Start))
            {
                throw new InvalidDataException(
                    $"Gift Pokemon field {fieldIndex} overlaps another scalar field.");
            }

            materializedFieldRanges.Add((fieldOffset, fieldEnd));
        }

        return layout;
    }

    private static int GetMaterializedFieldOffset(
        byte[] output,
        int tableOffset,
        int fieldIndex,
        int fieldSize)
    {
        var fieldOffset = ReadTableFieldOffset(output, tableOffset, fieldIndex, fieldSize);
        if (fieldOffset == 0)
        {
            throw new InvalidDataException($"Gift Pokemon field {fieldIndex} is missing after table expansion.");
        }

        return checked(tableOffset + fieldOffset);
    }

    private static void PatchBool(byte[] output, int tableOffset, int fieldIndex, bool original, bool updated)
    {
        if (original == updated)
        {
            return;
        }

        var offset = GetMaterializedFieldOffset(output, tableOffset, fieldIndex, sizeof(byte));
        EnsureRange(output, offset, sizeof(byte));
        output[offset] = updated ? (byte)1 : (byte)0;
    }

    private static void PatchByte(byte[] output, int tableOffset, int fieldIndex, int original, int updated)
    {
        if (original == updated)
        {
            return;
        }

        var offset = GetMaterializedFieldOffset(output, tableOffset, fieldIndex, sizeof(byte));
        EnsureRange(output, offset, sizeof(byte));
        output[offset] = checked((byte)updated);
    }

    private static void PatchSByte(byte[] output, int tableOffset, int fieldIndex, int original, int updated)
    {
        if (original == updated)
        {
            return;
        }

        var offset = GetMaterializedFieldOffset(output, tableOffset, fieldIndex, sizeof(sbyte));
        EnsureRange(output, offset, sizeof(sbyte));
        output[offset] = unchecked((byte)checked((sbyte)updated));
    }

    private static void PatchInt32(byte[] output, int tableOffset, int fieldIndex, int original, int updated)
    {
        if (original == updated)
        {
            return;
        }

        var offset = GetMaterializedFieldOffset(output, tableOffset, fieldIndex, sizeof(int));
        EnsureRange(output, offset, sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, sizeof(int)), updated);
    }

    private static SwShGiftPokemonRecord ReadGift(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        _ = ReadGiftTableLayout(data, tableOffset, rejectUnknownFields: false);

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
        if (relativeOffset == 0 || relativeOffset > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer archive contains an invalid unsigned offset.");
        }

        var targetOffset64 = offset + (long)relativeOffset;
        if (targetOffset64 > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer archive contains an invalid unsigned offset.");
        }

        var targetOffset = (int)targetOffset64;
        EnsureRange(data, targetOffset, sizeof(int));

        return targetOffset;
    }

    private static int ReadTableUOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex, sizeof(uint));
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
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex, sizeof(byte));
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
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex, sizeof(byte));
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
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex, sizeof(sbyte));
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
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex, sizeof(ushort));
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
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex, sizeof(int));
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
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex, sizeof(ulong));
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

    private static int ReadTableFieldOffset(
        ReadOnlySpan<byte> data,
        int tableOffset,
        int fieldIndex,
        int fieldSize)
    {
        if (fieldIndex < 0 || fieldSize <= 0)
        {
            throw new InvalidDataException("FlatBuffer table field metadata is invalid.");
        }

        var layout = ReadTableLayout(data, tableOffset);
        var fieldEntryOffset = checked((sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)));
        if (fieldEntryOffset + sizeof(ushort) > layout.VtableLength)
        {
            return 0;
        }

        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(layout.VtableOffset + fieldEntryOffset, sizeof(ushort)));
        if (fieldOffset == 0)
        {
            return 0;
        }

        if (fieldOffset < sizeof(int) || fieldOffset > layout.ObjectSize - fieldSize)
        {
            throw new InvalidDataException(
                $"FlatBuffer field {fieldIndex} points outside its table object.");
        }

        var absoluteFieldOffset = checked(tableOffset + fieldOffset);
        if (absoluteFieldOffset % fieldSize != 0)
        {
            throw new InvalidDataException(
                $"FlatBuffer field {fieldIndex} is not naturally aligned.");
        }

        return fieldOffset;
    }

    private static TableLayout ReadTableLayout(ReadOnlySpan<byte> data, int tableOffset)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vtableDelta = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        if (vtableDelta == 0)
        {
            throw new InvalidDataException("FlatBuffer table has an invalid zero vtable offset.");
        }

        var vtableOffsetValue = (long)tableOffset - vtableDelta;
        if (vtableOffsetValue < 0 || vtableOffsetValue > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer table has an invalid vtable offset.");
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
            throw new InvalidDataException("FlatBuffer table has an invalid vtable length.");
        }

        if (objectSize < sizeof(int))
        {
            throw new InvalidDataException("FlatBuffer table has an invalid object size.");
        }

        EnsureRange(data, vtableOffset, vtableLength);
        EnsureRange(data, tableOffset, objectSize);
        if (RangesOverlap(vtableOffset, vtableLength, tableOffset, objectSize))
        {
            throw new InvalidDataException("FlatBuffer table overlaps its vtable.");
        }

        return new TableLayout(vtableOffset, vtableLength, objectSize);
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
            throw new InvalidDataException("Expanded Gift Pokemon table must follow its vector element.");
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

    private static int ReadVectorLength(ReadOnlySpan<byte> data, int vectorOffset)
    {
        EnsureRange(data, vectorOffset, sizeof(uint));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vectorOffset, sizeof(uint)));
        var availableElementCount = (data.Length - vectorOffset - sizeof(uint)) / sizeof(uint);
        if (count > int.MaxValue || count > (uint)availableElementCount)
        {
            throw new InvalidDataException("FlatBuffer table vector length exceeds the available archive data.");
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

    private readonly record struct TableLayout(
        int VtableOffset,
        int VtableLength,
        int ObjectSize);

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
