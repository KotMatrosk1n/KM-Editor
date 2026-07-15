// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShTrainerDataRecord(
    int ClassId,
    int BattleMode,
    int PokemonCount,
    IReadOnlyList<int> Items,
    uint AiFlags,
    bool Heal,
    int Money,
    int Gift);

public sealed record SwShTrainerClassRecord(
    int Group,
    int BallId);

public sealed record SwShTrainerClassEdit(
    SwShTrainerClassField Field,
    int Value);

public enum SwShTrainerClassField
{
    BallId,
}

public sealed class SwShTrainerClassFile
{
    public const string TrainerClassRootRelativePath = "romfs/bin/trainer/trainer_type";
    public const int Size = 0x118;
    public const int MaximumBallId = 26;

    private const int GroupOffset = 0x01;
    private const int BallIdOffset = 0x02;

    private readonly byte[] data;

    private SwShTrainerClassFile(byte[] data)
    {
        this.data = data;
        Record = new SwShTrainerClassRecord(
            data[GroupOffset],
            data[BallIdOffset]);
    }

    public SwShTrainerClassRecord Record { get; }

    public static SwShTrainerClassFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != Size)
        {
            throw new InvalidDataException(
                $"Trainer class file must be exactly {Size} bytes for Sword/Shield.");
        }

        return new SwShTrainerClassFile(data.ToArray());
    }

    public byte[] WriteEdits(IReadOnlyList<SwShTrainerClassEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var result = data.ToArray();
        foreach (var edit in edits)
        {
            switch (edit.Field)
            {
                case SwShTrainerClassField.BallId:
                    ValidateRange(edit.Value, 0, MaximumBallId, nameof(edits));
                    result[BallIdOffset] = checked((byte)edit.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), $"Trainer class field '{edit.Field}' is not supported.");
            }
        }

        return result;
    }

    private static void ValidateRange(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Trainer class value {value} is outside the supported range {minimum}-{maximum}.");
        }
    }
}

public sealed record SwShTrainerDataEdit(
    SwShTrainerDataField Field,
    int Value);

public enum SwShTrainerDataField
{
    ClassId,
    BattleMode,
    PokemonCount,
    Item1Id,
    Item2Id,
    Item3Id,
    Item4Id,
    AiFlags,
    Heal,
    Money,
    Gift,
}

public sealed class SwShTrainerDataFile
{
    public const string TrainerDataRootRelativePath = "romfs/bin/trainer/trainer_data";
    public const int Size = 0x14;
    public const int MaximumClassId = ushort.MaxValue;
    public const int MaximumBattleMode = 2;
    public const int MaximumPokemonCount = 6;
    public const int MaximumItemId = ushort.MaxValue;
    public const int KnownAiFlagsMask = 0x1FFF;
    public const int MaximumAiFlags = KnownAiFlagsMask;
    public const int MaximumMoney = byte.MaxValue;
    public const int MaximumGiftId = ushort.MaxValue;

    private const int ClassOffset = 0x00;
    private const int BattleModeOffset = 0x02;
    private const int PokemonCountOffset = 0x03;
    private const int Item1Offset = 0x04;
    private const int Item2Offset = 0x06;
    private const int Item3Offset = 0x08;
    private const int Item4Offset = 0x0A;
    private const int AiOffset = 0x0C;
    private const int HealOffset = 0x10;
    private const int MoneyOffset = 0x11;
    private const int GiftOffset = 0x12;

    private readonly byte[] data;

    private SwShTrainerDataFile(byte[] data)
    {
        this.data = data;
        Record = new SwShTrainerDataRecord(
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ClassOffset)),
            data[BattleModeOffset],
            data[PokemonCountOffset],
            [
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Item1Offset)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Item2Offset)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Item3Offset)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Item4Offset)),
            ],
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(AiOffset)),
            data[HealOffset] == 1,
            data[MoneyOffset],
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(GiftOffset)));
    }

    public SwShTrainerDataRecord Record { get; }

    public static SwShTrainerDataFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != Size)
        {
            throw new InvalidDataException(
                $"Trainer data file must be exactly {Size} bytes for Sword/Shield.");
        }

        return new SwShTrainerDataFile(data.ToArray());
    }

    public byte[] WriteEdits(IReadOnlyList<SwShTrainerDataEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var result = data.ToArray();
        foreach (var edit in edits)
        {
            switch (edit.Field)
            {
                case SwShTrainerDataField.ClassId:
                    ValidateRange(edit.Value, 0, MaximumClassId, nameof(edits));
                    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(ClassOffset), checked((ushort)edit.Value));
                    break;
                case SwShTrainerDataField.BattleMode:
                    ValidateRange(edit.Value, 0, MaximumBattleMode, nameof(edits));
                    result[BattleModeOffset] = checked((byte)edit.Value);
                    break;
                case SwShTrainerDataField.PokemonCount:
                    ValidateRange(edit.Value, 0, MaximumPokemonCount, nameof(edits));
                    result[PokemonCountOffset] = checked((byte)edit.Value);
                    break;
                case SwShTrainerDataField.Item1Id:
                    WriteUInt16Field(result, Item1Offset, edit.Value, MaximumItemId, nameof(edits));
                    break;
                case SwShTrainerDataField.Item2Id:
                    WriteUInt16Field(result, Item2Offset, edit.Value, MaximumItemId, nameof(edits));
                    break;
                case SwShTrainerDataField.Item3Id:
                    WriteUInt16Field(result, Item3Offset, edit.Value, MaximumItemId, nameof(edits));
                    break;
                case SwShTrainerDataField.Item4Id:
                    WriteUInt16Field(result, Item4Offset, edit.Value, MaximumItemId, nameof(edits));
                    break;
                case SwShTrainerDataField.AiFlags:
                    ValidateRange(edit.Value, 0, MaximumAiFlags, nameof(edits));
                    var currentAiFlags = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(AiOffset));
                    var updatedAiFlags = (currentAiFlags & ~(uint)KnownAiFlagsMask) | (uint)edit.Value;
                    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(AiOffset), updatedAiFlags);
                    break;
                case SwShTrainerDataField.Heal:
                    ValidateRange(edit.Value, 0, 1, nameof(edits));
                    result[HealOffset] = checked((byte)edit.Value);
                    break;
                case SwShTrainerDataField.Money:
                    ValidateRange(edit.Value, 0, MaximumMoney, nameof(edits));
                    result[MoneyOffset] = checked((byte)edit.Value);
                    break;
                case SwShTrainerDataField.Gift:
                    WriteUInt16Field(result, GiftOffset, edit.Value, MaximumGiftId, nameof(edits));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), $"Trainer data field '{edit.Field}' is not supported.");
            }
        }

        return result;
    }

    private static void ValidateRange(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Trainer value {value} is outside the supported range {minimum}-{maximum}.");
        }
    }

    private static void WriteUInt16Field(byte[] data, int offset, int value, int maximum, string parameterName)
    {
        ValidateRange(value, 0, maximum, parameterName);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)value));
    }
}

public sealed record SwShTrainerPokemonStats(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShTrainerPokemonTableRecord(
    int Slot,
    int SpeciesId,
    int Level,
    int HeldItemId,
    IReadOnlyList<int> MoveIds,
    int Gender,
    int Ability,
    int Nature,
    int Form,
    SwShTrainerPokemonStats Evs,
    int DynamaxLevel,
    bool CanGigantamax,
    SwShTrainerPokemonStats Ivs,
    bool Shiny,
    bool CanDynamax,
    uint IvFlags);

public sealed record SwShTrainerPokemonEdit(
    int Slot,
    SwShTrainerPokemonField Field,
    int Value);

public enum SwShTrainerPokemonField
{
    SpeciesId,
    Form,
    Level,
    HeldItemId,
    Move1Id,
    Move2Id,
    Move3Id,
    Move4Id,
    Gender,
    Ability,
    Nature,
    EvHp,
    EvAttack,
    EvDefense,
    EvSpecialAttack,
    EvSpecialDefense,
    EvSpeed,
    DynamaxLevel,
    CanGigantamax,
    IvHp,
    IvAttack,
    IvDefense,
    IvSpecialAttack,
    IvSpecialDefense,
    IvSpeed,
    Shiny,
    CanDynamax,
}

public sealed class SwShTrainerTeamFile
{
    public const string TrainerPokeRootRelativePath = "romfs/bin/trainer/trainer_poke";
    public const int RowSize = 0x20;
    public const int MaximumPartySize = 6;
    public const int MaximumPokemonId = ushort.MaxValue;
    public const int MaximumFormId = ushort.MaxValue;
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 100;
    public const int MaximumItemId = ushort.MaxValue;
    public const int MaximumMoveId = ushort.MaxValue;
    public const int MaximumGenderValue = 3;
    public const int MaximumAbilityValue = 3;
    public const int MaximumNatureId = 24;
    public const int MaximumEvValue = byte.MaxValue;
    public const int MaximumDynamaxLevel = 10;
    public const int MaximumIvValue = 31;

    private const int GenderAbilityOffset = 0x00;
    private const int NatureOffset = 0x01;
    private const int EvHpOffset = 0x02;
    private const int EvAttackOffset = 0x03;
    private const int EvDefenseOffset = 0x04;
    private const int EvSpecialAttackOffset = 0x05;
    private const int EvSpecialDefenseOffset = 0x06;
    private const int EvSpeedOffset = 0x07;
    private const int DynamaxLevelOffset = 0x08;
    private const int CanGigantamaxOffset = 0x09;
    private const int LevelOffset = 0x0A;
    private const int SpeciesOffset = 0x0C;
    private const int FormOffset = 0x0E;
    private const int HeldItemOffset = 0x10;
    private const int Move1Offset = 0x12;
    private const int Move2Offset = 0x14;
    private const int Move3Offset = 0x16;
    private const int Move4Offset = 0x18;
    private const int IvFlagsOffset = 0x1C;
    private const int IvHpShift = 0;
    private const int IvAttackShift = 5;
    private const int IvDefenseShift = 10;
    private const int IvSpeedShift = 15;
    private const int IvSpecialAttackShift = 20;
    private const int IvSpecialDefenseShift = 25;
    private const int ShinyFlagShift = 30;
    private const int CanDynamaxFlagShift = 31;
    private const uint IvValueMask = 0x1F;

    private readonly byte[] data;

    private SwShTrainerTeamFile(byte[] data)
    {
        this.data = data;
        Records = Enumerable
            .Range(0, data.Length / RowSize)
            .Select(ReadRecord)
            .ToArray();
    }

    public IReadOnlyList<SwShTrainerPokemonTableRecord> Records { get; }

    public static SwShTrainerTeamFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length % RowSize != 0)
        {
            throw new InvalidDataException(
                $"Trainer party file length must be a multiple of {RowSize} bytes for Sword/Shield.");
        }

        var pokemonCount = data.Length / RowSize;
        if (pokemonCount > MaximumPartySize)
        {
            throw new InvalidDataException(
                $"Trainer party file must contain at most {MaximumPartySize} Pokemon rows for Sword/Shield.");
        }

        return new SwShTrainerTeamFile(data.ToArray());
    }

    public byte[] WriteEdits(IReadOnlyList<SwShTrainerPokemonEdit> edits, int? outputPokemonCount = null)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var targetPokemonCount = outputPokemonCount ?? Records.Count;
        ValidateRange(targetPokemonCount, 0, MaximumPartySize, nameof(outputPokemonCount));

        var result = new byte[targetPokemonCount * RowSize];
        data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);
        for (var index = Records.Count; index < targetPokemonCount; index++)
        {
            InitializeDefaultRow(result, index * RowSize);
        }

        foreach (var edit in edits)
        {
            if (edit.Slot < 1 || edit.Slot > targetPokemonCount)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"Trainer party slot {edit.Slot} is not present.");
            }

            var rowOffset = (edit.Slot - 1) * RowSize;
            switch (edit.Field)
            {
                case SwShTrainerPokemonField.SpeciesId:
                    WriteUInt16Field(result, rowOffset + SpeciesOffset, edit.Value, MaximumPokemonId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Form:
                    WriteUInt16Field(result, rowOffset + FormOffset, edit.Value, MaximumFormId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Level:
                    ValidateRange(edit.Value, MinimumLevel, MaximumLevel, nameof(edits));
                    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(rowOffset + LevelOffset), checked((ushort)edit.Value));
                    break;
                case SwShTrainerPokemonField.HeldItemId:
                    WriteUInt16Field(result, rowOffset + HeldItemOffset, edit.Value, MaximumItemId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Move1Id:
                    WriteUInt16Field(result, rowOffset + Move1Offset, edit.Value, MaximumMoveId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Move2Id:
                    WriteUInt16Field(result, rowOffset + Move2Offset, edit.Value, MaximumMoveId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Move3Id:
                    WriteUInt16Field(result, rowOffset + Move3Offset, edit.Value, MaximumMoveId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Move4Id:
                    WriteUInt16Field(result, rowOffset + Move4Offset, edit.Value, MaximumMoveId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Gender:
                    WriteTwoBitField(result, rowOffset + GenderAbilityOffset, edit.Value, shift: 0, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Ability:
                    WriteTwoBitField(result, rowOffset + GenderAbilityOffset, edit.Value, shift: 4, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Nature:
                    WriteByteField(result, rowOffset + NatureOffset, edit.Value, MaximumNatureId, nameof(edits));
                    break;
                case SwShTrainerPokemonField.EvHp:
                    WriteByteField(result, rowOffset + EvHpOffset, edit.Value, MaximumEvValue, nameof(edits));
                    break;
                case SwShTrainerPokemonField.EvAttack:
                    WriteByteField(result, rowOffset + EvAttackOffset, edit.Value, MaximumEvValue, nameof(edits));
                    break;
                case SwShTrainerPokemonField.EvDefense:
                    WriteByteField(result, rowOffset + EvDefenseOffset, edit.Value, MaximumEvValue, nameof(edits));
                    break;
                case SwShTrainerPokemonField.EvSpecialAttack:
                    WriteByteField(result, rowOffset + EvSpecialAttackOffset, edit.Value, MaximumEvValue, nameof(edits));
                    break;
                case SwShTrainerPokemonField.EvSpecialDefense:
                    WriteByteField(result, rowOffset + EvSpecialDefenseOffset, edit.Value, MaximumEvValue, nameof(edits));
                    break;
                case SwShTrainerPokemonField.EvSpeed:
                    WriteByteField(result, rowOffset + EvSpeedOffset, edit.Value, MaximumEvValue, nameof(edits));
                    break;
                case SwShTrainerPokemonField.DynamaxLevel:
                    WriteByteField(result, rowOffset + DynamaxLevelOffset, edit.Value, MaximumDynamaxLevel, nameof(edits));
                    break;
                case SwShTrainerPokemonField.CanGigantamax:
                    WriteBooleanByteField(result, rowOffset + CanGigantamaxOffset, edit.Value, nameof(edits));
                    break;
                case SwShTrainerPokemonField.IvHp:
                    WriteIvValue(result, rowOffset + IvFlagsOffset, edit.Value, IvHpShift, nameof(edits));
                    break;
                case SwShTrainerPokemonField.IvAttack:
                    WriteIvValue(result, rowOffset + IvFlagsOffset, edit.Value, IvAttackShift, nameof(edits));
                    break;
                case SwShTrainerPokemonField.IvDefense:
                    WriteIvValue(result, rowOffset + IvFlagsOffset, edit.Value, IvDefenseShift, nameof(edits));
                    break;
                case SwShTrainerPokemonField.IvSpecialAttack:
                    WriteIvValue(result, rowOffset + IvFlagsOffset, edit.Value, IvSpecialAttackShift, nameof(edits));
                    break;
                case SwShTrainerPokemonField.IvSpecialDefense:
                    WriteIvValue(result, rowOffset + IvFlagsOffset, edit.Value, IvSpecialDefenseShift, nameof(edits));
                    break;
                case SwShTrainerPokemonField.IvSpeed:
                    WriteIvValue(result, rowOffset + IvFlagsOffset, edit.Value, IvSpeedShift, nameof(edits));
                    break;
                case SwShTrainerPokemonField.Shiny:
                    WriteIvFlag(result, rowOffset + IvFlagsOffset, edit.Value, ShinyFlagShift, nameof(edits));
                    break;
                case SwShTrainerPokemonField.CanDynamax:
                    WriteIvFlag(result, rowOffset + IvFlagsOffset, edit.Value, CanDynamaxFlagShift, nameof(edits));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), $"Trainer party field '{edit.Field}' is not supported.");
            }
        }

        return result;
    }

    private static void InitializeDefaultRow(byte[] output, int rowOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(rowOffset + LevelOffset), MinimumLevel);
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(rowOffset + IvFlagsOffset),
            1u << CanDynamaxFlagShift);
    }

    private SwShTrainerPokemonTableRecord ReadRecord(int rowIndex)
    {
        var rowOffset = rowIndex * RowSize;
        var genderAbility = data[rowOffset + GenderAbilityOffset];
        var ivFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + IvFlagsOffset));

        return new SwShTrainerPokemonTableRecord(
            rowIndex + 1,
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + SpeciesOffset)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + LevelOffset)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + HeldItemOffset)),
            [
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + Move1Offset)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + Move2Offset)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + Move3Offset)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + Move4Offset)),
            ],
            genderAbility & 0x3,
            (genderAbility >> 4) & 0x3,
            data[rowOffset + NatureOffset],
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rowOffset + FormOffset)),
            new SwShTrainerPokemonStats(
                data[rowOffset + EvHpOffset],
                data[rowOffset + EvAttackOffset],
                data[rowOffset + EvDefenseOffset],
                data[rowOffset + EvSpecialAttackOffset],
                data[rowOffset + EvSpecialDefenseOffset],
                data[rowOffset + EvSpeedOffset]),
            data[rowOffset + DynamaxLevelOffset],
            data[rowOffset + CanGigantamaxOffset] != 0,
            ReadIvs(ivFlags),
            IsIvFlagSet(ivFlags, ShinyFlagShift),
            IsIvFlagSet(ivFlags, CanDynamaxFlagShift),
            ivFlags);
    }

    private static void WriteUInt16Field(
        byte[] output,
        int offset,
        int value,
        int maximum,
        string parameterName)
    {
        ValidateRange(value, 0, maximum, parameterName);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset), checked((ushort)value));
    }

    private static void WriteByteField(
        byte[] output,
        int offset,
        int value,
        int maximum,
        string parameterName)
    {
        ValidateRange(value, 0, maximum, parameterName);
        output[offset] = checked((byte)value);
    }

    private static void WriteBooleanByteField(
        byte[] output,
        int offset,
        int value,
        string parameterName)
    {
        ValidateRange(value, 0, 1, parameterName);
        output[offset] = checked((byte)value);
    }

    private static void WriteTwoBitField(
        byte[] output,
        int offset,
        int value,
        int shift,
        string parameterName)
    {
        ValidateRange(value, 0, 3, parameterName);
        var mask = 0x3 << shift;
        output[offset] = checked((byte)((output[offset] & ~mask) | ((value & 0x3) << shift)));
    }

    private static void WriteIvValue(
        byte[] output,
        int offset,
        int value,
        int shift,
        string parameterName)
    {
        ValidateRange(value, 0, MaximumIvValue, parameterName);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset));
        flags = (flags & ~(IvValueMask << shift)) | ((uint)value << shift);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset), flags);
    }

    private static void WriteIvFlag(
        byte[] output,
        int offset,
        int value,
        int shift,
        string parameterName)
    {
        ValidateRange(value, 0, 1, parameterName);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset));
        var mask = 1u << shift;
        flags = value == 0
            ? flags & ~mask
            : flags | mask;
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset), flags);
    }

    private static SwShTrainerPokemonStats ReadIvs(uint ivFlags)
    {
        return new SwShTrainerPokemonStats(
            ReadIvValue(ivFlags, IvHpShift),
            ReadIvValue(ivFlags, IvAttackShift),
            ReadIvValue(ivFlags, IvDefenseShift),
            ReadIvValue(ivFlags, IvSpecialAttackShift),
            ReadIvValue(ivFlags, IvSpecialDefenseShift),
            ReadIvValue(ivFlags, IvSpeedShift));
    }

    private static int ReadIvValue(uint ivFlags, int shift)
    {
        return (int)((ivFlags >> shift) & IvValueMask);
    }

    private static bool IsIvFlagSet(uint ivFlags, int shift)
    {
        return ((ivFlags >> shift) & 1) != 0;
    }

    private static void ValidateRange(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Trainer party value {value} is outside the supported range {minimum}-{maximum}.");
        }
    }
}
