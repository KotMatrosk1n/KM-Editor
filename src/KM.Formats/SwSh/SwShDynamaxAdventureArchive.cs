// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShDynamaxAdventureIvs(
    int Hp,
    int Attack,
    int Defense,
    int Speed,
    int SpecialAttack,
    int SpecialDefense);

public sealed record SwShDynamaxAdventureRecord(
    int EntryIndex,
    bool IsSingleCapture,
    ulong SingleCaptureFlagBlock,
    int Field02,
    int Form,
    int GigantamaxState,
    int BallItemId,
    int AdventureIndex,
    int Level,
    int Species,
    ulong UiMessageId,
    int OtGender,
    int Version,
    int ShinyRoll,
    SwShDynamaxAdventureIvs Ivs,
    int Ability,
    bool IsStoryProgressGated,
    IReadOnlyList<int> Moves);

public enum SwShDynamaxAdventureField
{
    Species,
    Form,
    Level,
    BallItemId,
    Ability,
    GigantamaxState,
    Version,
    ShinyRoll,
    Move0,
    Move1,
    Move2,
    Move3,
    GuaranteedPerfectIvs,
    IvAttack,
    IvDefense,
    IvSpeed,
    IvSpecialAttack,
    IvSpecialDefense,
    IsSingleCapture,
    IsStoryProgressGated,
    OtGender,
}

public sealed record SwShDynamaxAdventureEdit(
    int EntryIndex,
    SwShDynamaxAdventureField Field,
    int Value);

public sealed record SwShDynamaxAdventureRowCopy(
    int TargetEntryIndex,
    int SourceEntryIndex,
    bool PreserveTargetAdventureIndex);

public sealed record SwShDynamaxAdventureArchive(IReadOnlyList<SwShDynamaxAdventureRecord> Entries)
{
    public const int RandomIvValue = -1;
    public const int MinimumFixedIvValue = 0;
    public const int MaximumFixedIvValue = 31;
    public const int MaximumGuaranteedPerfectIvs = 6;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumIdValue = int.MaxValue;
    public const int MaximumAbilityRoll = 2;
    public const int MaximumGigantamaxState = 2;
    public const int MaximumVersion = 2;
    public const int MaximumShinyRoll = 2;

    private readonly byte[]? sourceData;
    private readonly int[]? sourceEntryTableOffsets;

    private SwShDynamaxAdventureArchive(
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        byte[] sourceData,
        int[] sourceEntryTableOffsets)
        : this(entries)
    {
        this.sourceData = sourceData;
        this.sourceEntryTableOffsets = sourceEntryTableOffsets;
    }

    public static SwShDynamaxAdventureArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Dynamax Adventure archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var tableVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var entries = ReadEntryTableVector(data, tableVectorOffset, out var entryTableOffsets);

        return new SwShDynamaxAdventureArchive(entries, data.ToArray(), entryTableOffsets);
    }

    public byte[] Write()
    {
        var writer = new DynamaxAdventureFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShDynamaxAdventureEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var editArray = edits.ToArray();
        if (sourceData is not null && sourceEntryTableOffsets is not null)
        {
            return WriteEditsInPlace(editArray);
        }

        var entries = Entries
            .Select(entry => entry with
            {
                Ivs = entry.Ivs with { },
                Moves = entry.Moves.ToArray(),
            })
            .ToArray();

        foreach (var edit in editArray)
        {
            ApplyEdit(entries, edit);
        }

        return new SwShDynamaxAdventureArchive(entries).Write();
    }

    public byte[] WriteRowCopies(IEnumerable<SwShDynamaxAdventureRowCopy> copies)
    {
        ArgumentNullException.ThrowIfNull(copies);

        if (sourceData is null || sourceEntryTableOffsets is null)
        {
            throw new InvalidDataException("Dynamax Adventure row copies require a parsed source table so the existing FlatBuffer layout can be preserved.");
        }

        var output = sourceData.ToArray();
        foreach (var copy in copies)
        {
            if ((uint)copy.TargetEntryIndex >= (uint)Entries.Count)
            {
                throw new InvalidDataException($"Dynamax Adventure target entry index {copy.TargetEntryIndex} is not present.");
            }

            if ((uint)copy.SourceEntryIndex >= (uint)Entries.Count)
            {
                throw new InvalidDataException($"Dynamax Adventure source entry index {copy.SourceEntryIndex} is not present.");
            }

            var target = Entries[copy.TargetEntryIndex];
            var source = Entries[copy.SourceEntryIndex];
            if (copy.PreserveTargetAdventureIndex)
            {
                source = source with { AdventureIndex = target.AdventureIndex };
            }

            WriteRecordInPlace(
                output,
                sourceEntryTableOffsets[copy.TargetEntryIndex],
                source);
        }

        return output;
    }

    public static int GetGuaranteedPerfectIvCount(SwShDynamaxAdventureIvs ivs)
    {
        return ivs.Hp < RandomIvValue
            ? Math.Clamp(-ivs.Hp, 0, MaximumGuaranteedPerfectIvs)
            : 0;
    }

    private byte[] WriteEditsInPlace(IReadOnlyList<SwShDynamaxAdventureEdit> edits)
    {
        var output = sourceData!.ToArray();
        foreach (var edit in edits)
        {
            ApplyEditInPlace(output, edit);
        }

        return output;
    }

    private void ApplyEditInPlace(byte[] data, SwShDynamaxAdventureEdit edit)
    {
        if ((uint)edit.EntryIndex >= (uint)sourceEntryTableOffsets!.Length)
        {
            throw new InvalidDataException($"Dynamax Adventure entry index {edit.EntryIndex} is not present.");
        }

        var tableOffset = sourceEntryTableOffsets[edit.EntryIndex];
        switch (edit.Field)
        {
            case SwShDynamaxAdventureField.Species:
                WriteTableInt32(data, tableOffset, fieldIndex: 8, ValidateRange(edit.Value, 0, MaximumIdValue), nameof(SwShDynamaxAdventureField.Species));
                break;
            case SwShDynamaxAdventureField.Form:
                WriteTableByte(data, tableOffset, fieldIndex: 3, checked((byte)ValidateRange(edit.Value, 0, MaximumByteValue)), nameof(SwShDynamaxAdventureField.Form));
                break;
            case SwShDynamaxAdventureField.Level:
                WriteTableUInt32(data, tableOffset, fieldIndex: 7, checked((uint)ValidateRange(edit.Value, 0, MaximumByteValue)), nameof(SwShDynamaxAdventureField.Level));
                break;
            case SwShDynamaxAdventureField.BallItemId:
                WriteTableUInt32(data, tableOffset, fieldIndex: 5, checked((uint)ValidateRange(edit.Value, 0, MaximumIdValue)), nameof(SwShDynamaxAdventureField.BallItemId));
                break;
            case SwShDynamaxAdventureField.Ability:
                WriteTableUInt32(data, tableOffset, fieldIndex: 19, checked((uint)ValidateRange(edit.Value, 0, MaximumAbilityRoll)), nameof(SwShDynamaxAdventureField.Ability));
                break;
            case SwShDynamaxAdventureField.GigantamaxState:
                WriteTableUInt32(data, tableOffset, fieldIndex: 4, checked((uint)ValidateRange(edit.Value, 0, MaximumGigantamaxState)), nameof(SwShDynamaxAdventureField.GigantamaxState));
                break;
            case SwShDynamaxAdventureField.Version:
                WriteTableByte(data, tableOffset, fieldIndex: 11, checked((byte)ValidateRange(edit.Value, 0, MaximumVersion)), nameof(SwShDynamaxAdventureField.Version));
                break;
            case SwShDynamaxAdventureField.ShinyRoll:
                WriteTableUInt32(data, tableOffset, fieldIndex: 12, checked((uint)ValidateRange(edit.Value, 0, MaximumShinyRoll)), nameof(SwShDynamaxAdventureField.ShinyRoll));
                break;
            case SwShDynamaxAdventureField.Move0:
                WriteTableUInt32(data, tableOffset, fieldIndex: 21, checked((uint)ValidateRange(edit.Value, 0, MaximumIdValue)), nameof(SwShDynamaxAdventureField.Move0));
                break;
            case SwShDynamaxAdventureField.Move1:
                WriteTableUInt32(data, tableOffset, fieldIndex: 22, checked((uint)ValidateRange(edit.Value, 0, MaximumIdValue)), nameof(SwShDynamaxAdventureField.Move1));
                break;
            case SwShDynamaxAdventureField.Move2:
                WriteTableUInt32(data, tableOffset, fieldIndex: 23, checked((uint)ValidateRange(edit.Value, 0, MaximumIdValue)), nameof(SwShDynamaxAdventureField.Move2));
                break;
            case SwShDynamaxAdventureField.Move3:
                WriteTableUInt32(data, tableOffset, fieldIndex: 24, checked((uint)ValidateRange(edit.Value, 0, MaximumIdValue)), nameof(SwShDynamaxAdventureField.Move3));
                break;
            case SwShDynamaxAdventureField.GuaranteedPerfectIvs:
                WriteTableSByte(data, tableOffset, fieldIndex: 16, checked((sbyte)SetGuaranteedPerfectIvs(Entries[edit.EntryIndex].Ivs, edit.Value).Hp), nameof(SwShDynamaxAdventureField.GuaranteedPerfectIvs));
                break;
            case SwShDynamaxAdventureField.IvAttack:
                WriteTableSByte(data, tableOffset, fieldIndex: 14, checked((sbyte)ValidateIvOverride(edit.Value)), nameof(SwShDynamaxAdventureField.IvAttack));
                break;
            case SwShDynamaxAdventureField.IvDefense:
                WriteTableSByte(data, tableOffset, fieldIndex: 15, checked((sbyte)ValidateIvOverride(edit.Value)), nameof(SwShDynamaxAdventureField.IvDefense));
                break;
            case SwShDynamaxAdventureField.IvSpeed:
                WriteTableSByte(data, tableOffset, fieldIndex: 13, checked((sbyte)ValidateIvOverride(edit.Value)), nameof(SwShDynamaxAdventureField.IvSpeed));
                break;
            case SwShDynamaxAdventureField.IvSpecialAttack:
                WriteTableSByte(data, tableOffset, fieldIndex: 17, checked((sbyte)ValidateIvOverride(edit.Value)), nameof(SwShDynamaxAdventureField.IvSpecialAttack));
                break;
            case SwShDynamaxAdventureField.IvSpecialDefense:
                WriteTableSByte(data, tableOffset, fieldIndex: 18, checked((sbyte)ValidateIvOverride(edit.Value)), nameof(SwShDynamaxAdventureField.IvSpecialDefense));
                break;
            case SwShDynamaxAdventureField.IsSingleCapture:
                WriteTableBool(data, tableOffset, fieldIndex: 0, ValidateBoolean(edit.Value), nameof(SwShDynamaxAdventureField.IsSingleCapture));
                break;
            case SwShDynamaxAdventureField.IsStoryProgressGated:
                WriteTableBool(data, tableOffset, fieldIndex: 20, ValidateBoolean(edit.Value), nameof(SwShDynamaxAdventureField.IsStoryProgressGated));
                break;
            case SwShDynamaxAdventureField.OtGender:
                WriteTableUInt32(data, tableOffset, fieldIndex: 10, checked((uint)ValidateRange(edit.Value, 0, MaximumIdValue)), nameof(SwShDynamaxAdventureField.OtGender));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), $"Dynamax Adventure field '{edit.Field}' is not supported.");
        }
    }

    private static void WriteRecordInPlace(
        byte[] data,
        int tableOffset,
        SwShDynamaxAdventureRecord source)
    {
        WriteTableBool(data, tableOffset, fieldIndex: 0, source.IsSingleCapture, nameof(SwShDynamaxAdventureRecord.IsSingleCapture));
        WriteTableUInt64(data, tableOffset, fieldIndex: 1, source.SingleCaptureFlagBlock, nameof(SwShDynamaxAdventureRecord.SingleCaptureFlagBlock));
        WriteTableByte(data, tableOffset, fieldIndex: 2, checked((byte)source.Field02), nameof(SwShDynamaxAdventureRecord.Field02));
        WriteTableByte(data, tableOffset, fieldIndex: 3, checked((byte)source.Form), nameof(SwShDynamaxAdventureRecord.Form));
        WriteTableUInt32(data, tableOffset, fieldIndex: 4, checked((uint)source.GigantamaxState), nameof(SwShDynamaxAdventureRecord.GigantamaxState));
        WriteTableUInt32(data, tableOffset, fieldIndex: 5, checked((uint)source.BallItemId), nameof(SwShDynamaxAdventureRecord.BallItemId));
        WriteTableUInt32(data, tableOffset, fieldIndex: 6, checked((uint)source.AdventureIndex), nameof(SwShDynamaxAdventureRecord.AdventureIndex));
        WriteTableUInt32(data, tableOffset, fieldIndex: 7, checked((uint)source.Level), nameof(SwShDynamaxAdventureRecord.Level));
        WriteTableInt32(data, tableOffset, fieldIndex: 8, source.Species, nameof(SwShDynamaxAdventureRecord.Species));
        WriteTableUInt64(data, tableOffset, fieldIndex: 9, source.UiMessageId, nameof(SwShDynamaxAdventureRecord.UiMessageId));
        WriteTableUInt32(data, tableOffset, fieldIndex: 10, checked((uint)source.OtGender), nameof(SwShDynamaxAdventureRecord.OtGender));
        WriteTableByte(data, tableOffset, fieldIndex: 11, checked((byte)source.Version), nameof(SwShDynamaxAdventureRecord.Version));
        WriteTableUInt32(data, tableOffset, fieldIndex: 12, checked((uint)source.ShinyRoll), nameof(SwShDynamaxAdventureRecord.ShinyRoll));
        WriteTableSByte(data, tableOffset, fieldIndex: 13, checked((sbyte)source.Ivs.Speed), nameof(SwShDynamaxAdventureRecord.Ivs));
        WriteTableSByte(data, tableOffset, fieldIndex: 14, checked((sbyte)source.Ivs.Attack), nameof(SwShDynamaxAdventureRecord.Ivs));
        WriteTableSByte(data, tableOffset, fieldIndex: 15, checked((sbyte)source.Ivs.Defense), nameof(SwShDynamaxAdventureRecord.Ivs));
        WriteTableSByte(data, tableOffset, fieldIndex: 16, checked((sbyte)source.Ivs.Hp), nameof(SwShDynamaxAdventureRecord.Ivs));
        WriteTableSByte(data, tableOffset, fieldIndex: 17, checked((sbyte)source.Ivs.SpecialAttack), nameof(SwShDynamaxAdventureRecord.Ivs));
        WriteTableSByte(data, tableOffset, fieldIndex: 18, checked((sbyte)source.Ivs.SpecialDefense), nameof(SwShDynamaxAdventureRecord.Ivs));
        WriteTableUInt32(data, tableOffset, fieldIndex: 19, checked((uint)source.Ability), nameof(SwShDynamaxAdventureRecord.Ability));
        WriteTableBool(data, tableOffset, fieldIndex: 20, source.IsStoryProgressGated, nameof(SwShDynamaxAdventureRecord.IsStoryProgressGated));
        WriteTableUInt32(data, tableOffset, fieldIndex: 21, checked((uint)source.Moves[0]), nameof(SwShDynamaxAdventureRecord.Moves));
        WriteTableUInt32(data, tableOffset, fieldIndex: 22, checked((uint)source.Moves[1]), nameof(SwShDynamaxAdventureRecord.Moves));
        WriteTableUInt32(data, tableOffset, fieldIndex: 23, checked((uint)source.Moves[2]), nameof(SwShDynamaxAdventureRecord.Moves));
        WriteTableUInt32(data, tableOffset, fieldIndex: 24, checked((uint)source.Moves[3]), nameof(SwShDynamaxAdventureRecord.Moves));
    }

    private static void ApplyEdit(IReadOnlyList<SwShDynamaxAdventureRecord> entries, SwShDynamaxAdventureEdit edit)
    {
        if ((uint)edit.EntryIndex >= (uint)entries.Count)
        {
            throw new InvalidDataException($"Dynamax Adventure entry index {edit.EntryIndex} is not present.");
        }

        if (entries is not SwShDynamaxAdventureRecord[] mutableEntries)
        {
            throw new InvalidDataException("Dynamax Adventure entry list is not mutable.");
        }

        var entry = entries[edit.EntryIndex];
        mutableEntries[edit.EntryIndex] = edit.Field switch
        {
            SwShDynamaxAdventureField.Species => entry with { Species = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShDynamaxAdventureField.Form => entry with { Form = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShDynamaxAdventureField.Level => entry with { Level = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShDynamaxAdventureField.BallItemId => entry with { BallItemId = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShDynamaxAdventureField.Ability => entry with { Ability = ValidateRange(edit.Value, 0, MaximumAbilityRoll) },
            SwShDynamaxAdventureField.GigantamaxState => entry with { GigantamaxState = ValidateRange(edit.Value, 0, MaximumGigantamaxState) },
            SwShDynamaxAdventureField.Version => entry with { Version = ValidateRange(edit.Value, 0, MaximumVersion) },
            SwShDynamaxAdventureField.ShinyRoll => entry with { ShinyRoll = ValidateRange(edit.Value, 0, MaximumShinyRoll) },
            SwShDynamaxAdventureField.Move0 => entry with { Moves = SetMove(entry.Moves, 0, edit.Value) },
            SwShDynamaxAdventureField.Move1 => entry with { Moves = SetMove(entry.Moves, 1, edit.Value) },
            SwShDynamaxAdventureField.Move2 => entry with { Moves = SetMove(entry.Moves, 2, edit.Value) },
            SwShDynamaxAdventureField.Move3 => entry with { Moves = SetMove(entry.Moves, 3, edit.Value) },
            SwShDynamaxAdventureField.GuaranteedPerfectIvs => entry with { Ivs = SetGuaranteedPerfectIvs(entry.Ivs, edit.Value) },
            SwShDynamaxAdventureField.IvAttack => entry with { Ivs = entry.Ivs with { Attack = ValidateIvOverride(edit.Value) } },
            SwShDynamaxAdventureField.IvDefense => entry with { Ivs = entry.Ivs with { Defense = ValidateIvOverride(edit.Value) } },
            SwShDynamaxAdventureField.IvSpeed => entry with { Ivs = entry.Ivs with { Speed = ValidateIvOverride(edit.Value) } },
            SwShDynamaxAdventureField.IvSpecialAttack => entry with { Ivs = entry.Ivs with { SpecialAttack = ValidateIvOverride(edit.Value) } },
            SwShDynamaxAdventureField.IvSpecialDefense => entry with { Ivs = entry.Ivs with { SpecialDefense = ValidateIvOverride(edit.Value) } },
            SwShDynamaxAdventureField.IsSingleCapture => entry with { IsSingleCapture = ValidateBoolean(edit.Value) },
            SwShDynamaxAdventureField.IsStoryProgressGated => entry with { IsStoryProgressGated = ValidateBoolean(edit.Value) },
            SwShDynamaxAdventureField.OtGender => entry with { OtGender = ValidateRange(edit.Value, 0, MaximumIdValue) },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Dynamax Adventure field '{edit.Field}' is not supported."),
        };
    }

    private static IReadOnlyList<int> SetMove(IReadOnlyList<int> moves, int slot, int value)
    {
        var updatedMoves = moves.ToArray();
        if ((uint)slot >= (uint)updatedMoves.Length)
        {
            throw new InvalidDataException($"Dynamax Adventure move slot {slot} is not present.");
        }

        updatedMoves[slot] = ValidateRange(value, 0, MaximumIdValue);

        return updatedMoves;
    }

    private static SwShDynamaxAdventureIvs SetGuaranteedPerfectIvs(
        SwShDynamaxAdventureIvs ivs,
        int guaranteedPerfectIvs)
    {
        var value = ValidateGuaranteedPerfectIvs(guaranteedPerfectIvs);
        return ivs with { Hp = value == 0 ? RandomIvValue : -value };
    }

    private static int ValidateGuaranteedPerfectIvs(int value)
    {
        if (value == 0 || value is >= 2 and <= MaximumGuaranteedPerfectIvs)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Dynamax Adventure guaranteed perfect IV count {value} is outside the supported values 0 or 2-{MaximumGuaranteedPerfectIvs}.");
    }

    private static int ValidateIvOverride(int value)
    {
        if (value == RandomIvValue || value is >= MinimumFixedIvValue and <= MaximumFixedIvValue)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Dynamax Adventure IV value {value} is outside the supported range {RandomIvValue}, {MinimumFixedIvValue}-{MaximumFixedIvValue}.");
    }

    private static bool ValidateBoolean(int value)
    {
        return ValidateRange(value, 0, 1) != 0;
    }

    private static int ValidateRange(int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Dynamax Adventure value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
    }

    private static SwShDynamaxAdventureRecord ReadEntry(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        return new SwShDynamaxAdventureRecord(
            index,
            ReadTableBool(data, tableOffset, fieldIndex: 0, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 1, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 2, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 3, required: false),
            ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 4, required: false),
            ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 5, required: false),
            ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 6, required: false),
            ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 7, required: false),
            ReadTableInt32(data, tableOffset, fieldIndex: 8, required: false),
            ReadTableUInt64(data, tableOffset, fieldIndex: 9, required: false),
            ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 10, required: false),
            ReadTableByte(data, tableOffset, fieldIndex: 11, required: false),
            ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 12, required: false),
            new SwShDynamaxAdventureIvs(
                ReadTableSByte(data, tableOffset, fieldIndex: 16, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 14, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 15, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 13, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 17, required: false),
                ReadTableSByte(data, tableOffset, fieldIndex: 18, required: false)),
            ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 19, required: false),
            ReadTableBool(data, tableOffset, fieldIndex: 20, required: false),
            [
                ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 21, required: false),
                ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 22, required: false),
                ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 23, required: false),
                ReadTableUInt32AsInt(data, tableOffset, fieldIndex: 24, required: false),
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
        return ReadTableByte(data, tableOffset, fieldIndex, required) != 0;
    }

    private static int ReadTableByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
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

    private static int ReadTableSByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var value = ReadTableByte(data, tableOffset, fieldIndex, required);
        return unchecked((sbyte)(byte)value);
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

    private static int ReadTableUInt32AsInt(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
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
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(uint)));
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"FlatBuffer field {fieldIndex} exceeds the supported integer range.");
        }

        return (int)value;
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

    private static SwShDynamaxAdventureRecord[] ReadEntryTableVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        out int[] tableOffsets)
    {
        var count = ReadVectorLength(data, vectorOffset);
        var values = new SwShDynamaxAdventureRecord[count];
        tableOffsets = new int[count];

        for (var index = 0; index < count; index++)
        {
            var elementOffset = vectorOffset + sizeof(uint) + (index * sizeof(uint));
            var tableOffset = ReadUOffset(data, elementOffset);
            tableOffsets[index] = tableOffset;
            values[index] = ReadEntry(data, tableOffset, index);
        }

        return values;
    }

    private static void WriteTableBool(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        bool value,
        string fieldName)
    {
        WriteTableByte(data, tableOffset, fieldIndex, value ? (byte)1 : (byte)0, fieldName);
    }

    private static void WriteTableByte(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        byte value,
        string fieldName)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            EnsureMissingFieldCanStayDefault(value, fieldName);
            return;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(byte));
        data[tableOffset + fieldOffset] = value;
    }

    private static void WriteTableSByte(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        sbyte value,
        string fieldName)
    {
        WriteTableByte(data, tableOffset, fieldIndex, unchecked((byte)value), fieldName);
    }

    private static void WriteTableInt32(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        int value,
        string fieldName)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            EnsureMissingFieldCanStayDefault(value, fieldName);
            return;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(int)), value);
    }

    private static void WriteTableUInt32(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        uint value,
        string fieldName)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            EnsureMissingFieldCanStayDefault(value, fieldName);
            return;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(uint)), value);
    }

    private static void WriteTableUInt64(
        Span<byte> data,
        int tableOffset,
        int fieldIndex,
        ulong value,
        string fieldName)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            EnsureMissingFieldCanStayDefault(value, fieldName);
            return;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(ulong)), value);
    }

    private static void EnsureMissingFieldCanStayDefault<T>(T value, string fieldName)
        where T : struct, IEquatable<T>
    {
        if (value.Equals(default(T)))
        {
            return;
        }

        throw new InvalidDataException(
            $"Dynamax Adventure field '{fieldName}' is stored as an omitted FlatBuffer default in this record and cannot be changed without rebuilding the table layout.");
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new InvalidDataException("FlatBuffer archive contains an invalid offset.");
        }
    }

    private sealed class DynamaxAdventureFlatBufferWriter
    {
        private const int EntryFieldCount = 25;
        private const int EntryVtableLength = sizeof(ushort) * 2 + (EntryFieldCount * sizeof(ushort));
        private readonly List<byte> bytes = [];

        public void Write(SwShDynamaxAdventureArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var entryVector = WriteTableVector(archive.Entries.Count);
            PatchUOffset(root.Field0Offset, entryVector.VectorOffset);
            for (var index = 0; index < archive.Entries.Count; index++)
            {
                var entryOffset = WriteEntry(archive.Entries[index]);
                PatchUOffset(entryVector.ElementOffsets[index], entryOffset);
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
            var entryFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, entryFieldOffset);
        }

        private int WriteEntry(SwShDynamaxAdventureRecord entry)
        {
            AlignForTable(vtableLength: EntryVtableLength, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(EntryVtableLength);
            WriteUInt16(80);
            WriteEntryFieldOffsets();

            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            WriteByte(entry.IsSingleCapture ? (byte)1 : (byte)0);
            WriteByte(checked((byte)entry.Field02));
            WriteByte(checked((byte)entry.Form));
            WriteByte(checked((byte)entry.Version));
            WriteUInt64(entry.SingleCaptureFlagBlock);
            WriteUInt64(entry.UiMessageId);
            WriteUInt32(checked((uint)entry.GigantamaxState));
            WriteUInt32(checked((uint)entry.BallItemId));
            WriteUInt32(checked((uint)entry.AdventureIndex));
            WriteUInt32(checked((uint)entry.Level));
            WriteInt32(entry.Species);
            WriteUInt32(checked((uint)entry.OtGender));
            WriteUInt32(checked((uint)entry.ShinyRoll));
            WriteUInt32(checked((uint)entry.Ability));
            WriteUInt32(checked((uint)entry.Moves[0]));
            WriteUInt32(checked((uint)entry.Moves[1]));
            WriteUInt32(checked((uint)entry.Moves[2]));
            WriteUInt32(checked((uint)entry.Moves[3]));
            WriteByte(entry.IsStoryProgressGated ? (byte)1 : (byte)0);
            WriteSByte(checked((sbyte)entry.Ivs.Speed));
            WriteSByte(checked((sbyte)entry.Ivs.Attack));
            WriteSByte(checked((sbyte)entry.Ivs.Defense));
            WriteSByte(checked((sbyte)entry.Ivs.Hp));
            WriteSByte(checked((sbyte)entry.Ivs.SpecialAttack));
            WriteSByte(checked((sbyte)entry.Ivs.SpecialDefense));
            WriteByte(0);

            return tableOffset;
        }

        private void WriteEntryFieldOffsets()
        {
            WriteUInt16(4);  // IsSingleCapture
            WriteUInt16(8);  // SingleCaptureFlagBlock
            WriteUInt16(5);  // Field_02
            WriteUInt16(6);  // Form
            WriteUInt16(24); // GigantamaxState
            WriteUInt16(28); // Ball
            WriteUInt16(32); // IndexNum
            WriteUInt16(36); // Level
            WriteUInt16(40); // Species
            WriteUInt16(16); // UiMessageID
            WriteUInt16(44); // OT_Gender
            WriteUInt16(7);  // Version
            WriteUInt16(48); // Shiny
            WriteUInt16(73); // IV_SPE
            WriteUInt16(74); // IV_ATK
            WriteUInt16(75); // IV_DEF
            WriteUInt16(76); // IV_HP
            WriteUInt16(77); // IV_SPA
            WriteUInt16(78); // IV_SPD
            WriteUInt16(52); // Ability
            WriteUInt16(72); // IsStoryProgressGated
            WriteUInt16(56); // Move0
            WriteUInt16(60); // Move1
            WriteUInt16(64); // Move2
            WriteUInt16(68); // Move3
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
