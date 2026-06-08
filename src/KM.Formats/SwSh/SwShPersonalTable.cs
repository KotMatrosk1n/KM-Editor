// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShPersonalTable(IReadOnlyList<SwShPersonalRecord> Records)
{
    public const int RecordSize = 0xB0;
    public const string PersonalDataRelativePath = "romfs/bin/pml/personal/personal_total.bin";

    public static SwShPersonalTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length % RecordSize != 0)
        {
            throw new InvalidDataException(
                $"Personal table length must be a non-empty multiple of {RecordSize} bytes.");
        }

        var records = new SwShPersonalRecord[data.Length / RecordSize];
        for (var index = 0; index < records.Length; index++)
        {
            var record = data.Slice(index * RecordSize, RecordSize);
            records[index] = ParseRecord(index, record);
        }

        return new SwShPersonalTable(records);
    }

    private static SwShPersonalRecord ParseRecord(int personalId, ReadOnlySpan<byte> data)
    {
        var evYield = BinaryPrimitives.ReadUInt16LittleEndian(data[0x0A..]);
        var flags = data[0x21];

        return new SwShPersonalRecord(
            personalId,
            HP: data[0x00],
            Attack: data[0x01],
            Defense: data[0x02],
            Speed: data[0x03],
            SpecialAttack: data[0x04],
            SpecialDefense: data[0x05],
            Type1: data[0x06],
            Type2: data[0x07],
            CatchRate: data[0x08],
            EvolutionStage: data[0x09],
            EVYieldHP: evYield & 0x3,
            EVYieldAttack: (evYield >> 2) & 0x3,
            EVYieldDefense: (evYield >> 4) & 0x3,
            EVYieldSpeed: (evYield >> 6) & 0x3,
            EVYieldSpecialAttack: (evYield >> 8) & 0x3,
            EVYieldSpecialDefense: (evYield >> 10) & 0x3,
            HeldItem1: BinaryPrimitives.ReadInt16LittleEndian(data[0x0C..]),
            HeldItem2: BinaryPrimitives.ReadInt16LittleEndian(data[0x0E..]),
            HeldItem3: BinaryPrimitives.ReadInt16LittleEndian(data[0x10..]),
            GenderRatio: data[0x12],
            HatchCycles: data[0x13],
            BaseFriendship: data[0x14],
            ExpGrowth: data[0x15],
            EggGroup1: data[0x16],
            EggGroup2: data[0x17],
            Ability1: BinaryPrimitives.ReadUInt16LittleEndian(data[0x18..]),
            Ability2: BinaryPrimitives.ReadUInt16LittleEndian(data[0x1A..]),
            HiddenAbility: BinaryPrimitives.ReadUInt16LittleEndian(data[0x1C..]),
            FormStatsIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0x1E..]),
            FormCount: data[0x20],
            Color: flags & 0x3F,
            IsPresentInGame: ((flags >> 6) & 1) == 1,
            HasSpriteForm: ((flags >> 7) & 1) == 1,
            BaseExperience: BinaryPrimitives.ReadUInt16LittleEndian(data[0x22..]),
            Height: BinaryPrimitives.ReadUInt16LittleEndian(data[0x24..]),
            Weight: BinaryPrimitives.ReadUInt16LittleEndian(data[0x26..]),
            ModelId: BinaryPrimitives.ReadUInt32LittleEndian(data[0x4C..]),
            HatchedSpecies: BinaryPrimitives.ReadUInt16LittleEndian(data[0x56..]),
            LocalFormIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0x58..]),
            RegionalFlags: BinaryPrimitives.ReadUInt16LittleEndian(data[0x5A..]),
            RegionalDexIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0x5C..]),
            Form: BinaryPrimitives.ReadUInt16LittleEndian(data[0x5E..]),
            ArmorDexIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0xAC..]),
            CrownDexIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0xAE..]));
    }
}

public sealed record SwShPersonalRecord(
    int PersonalId,
    int HP,
    int Attack,
    int Defense,
    int Speed,
    int SpecialAttack,
    int SpecialDefense,
    int Type1,
    int Type2,
    int CatchRate,
    int EvolutionStage,
    int EVYieldHP,
    int EVYieldAttack,
    int EVYieldDefense,
    int EVYieldSpeed,
    int EVYieldSpecialAttack,
    int EVYieldSpecialDefense,
    int HeldItem1,
    int HeldItem2,
    int HeldItem3,
    int GenderRatio,
    int HatchCycles,
    int BaseFriendship,
    int ExpGrowth,
    int EggGroup1,
    int EggGroup2,
    int Ability1,
    int Ability2,
    int HiddenAbility,
    int FormStatsIndex,
    int FormCount,
    int Color,
    bool IsPresentInGame,
    bool HasSpriteForm,
    int BaseExperience,
    int Height,
    int Weight,
    uint ModelId,
    int HatchedSpecies,
    int LocalFormIndex,
    int RegionalFlags,
    int RegionalDexIndex,
    int Form,
    int ArmorDexIndex,
    int CrownDexIndex)
{
    public int BaseStatTotal => HP + Attack + Defense + Speed + SpecialAttack + SpecialDefense;
}
