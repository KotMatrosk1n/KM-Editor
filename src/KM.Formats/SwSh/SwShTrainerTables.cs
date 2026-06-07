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

public sealed record SwShTrainerDataEdit(
    SwShTrainerDataField Field,
    int Value);

public enum SwShTrainerDataField
{
    ClassId,
    BattleMode,
}

public sealed class SwShTrainerDataFile
{
    public const string TrainerDataRootRelativePath = "romfs/bin/trainer/trainer_data";
    public const int Size = 0x14;
    public const int MaximumClassId = ushort.MaxValue;
    public const int MaximumBattleMode = 2;

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
}

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
    int DynamaxLevel,
    bool CanGigantamax,
    uint IvFlags);

public sealed record SwShTrainerPokemonEdit(
    int Slot,
    SwShTrainerPokemonField Field,
    int Value);

public enum SwShTrainerPokemonField
{
    SpeciesId,
    Level,
    HeldItemId,
    Move1Id,
    Move2Id,
    Move3Id,
    Move4Id,
}

public sealed class SwShTrainerTeamFile
{
    public const string TrainerPokeRootRelativePath = "romfs/bin/trainer/trainer_poke";
    public const int RowSize = 0x20;
    public const int MaximumPokemonId = ushort.MaxValue;
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 100;
    public const int MaximumItemId = ushort.MaxValue;
    public const int MaximumMoveId = ushort.MaxValue;

    private const int GenderAbilityOffset = 0x00;
    private const int NatureOffset = 0x01;
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

        return new SwShTrainerTeamFile(data.ToArray());
    }

    public byte[] WriteEdits(IReadOnlyList<SwShTrainerPokemonEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var result = data.ToArray();
        foreach (var edit in edits)
        {
            if (edit.Slot < 1 || edit.Slot > Records.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"Trainer party slot {edit.Slot} is not present.");
            }

            var rowOffset = (edit.Slot - 1) * RowSize;
            switch (edit.Field)
            {
                case SwShTrainerPokemonField.SpeciesId:
                    WriteUInt16Field(result, rowOffset + SpeciesOffset, edit.Value, MaximumPokemonId, nameof(edits));
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), $"Trainer party field '{edit.Field}' is not supported.");
            }
        }

        return result;
    }

    private SwShTrainerPokemonTableRecord ReadRecord(int rowIndex)
    {
        var rowOffset = rowIndex * RowSize;
        var genderAbility = data[rowOffset + GenderAbilityOffset];

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
            data[rowOffset + DynamaxLevelOffset],
            data[rowOffset + CanGigantamaxOffset] != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rowOffset + IvFlagsOffset)));
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
