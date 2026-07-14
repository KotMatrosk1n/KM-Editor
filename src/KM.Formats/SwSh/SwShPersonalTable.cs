// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShPersonalTable(IReadOnlyList<SwShPersonalRecord> Records)
{
    public const int RecordSize = 0xB0;
    public const int TechnicalMachineCompatibilityCount = 100;
    public const int TechnicalRecordCompatibilityCount = 100;
    public const int TypeTutorCompatibilityCount = 8;
    public const int ArmorTutorCompatibilityCount = 18;
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

    public static byte[] Write(IReadOnlyList<SwShPersonalRecord> records, ReadOnlySpan<byte> originalData)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (originalData.Length == 0 || originalData.Length % RecordSize != 0)
        {
            throw new InvalidDataException(
                $"Personal table length must be a non-empty multiple of {RecordSize} bytes.");
        }

        if (records.Count != originalData.Length / RecordSize)
        {
            throw new InvalidDataException(
                "Personal table write requires the same number of records as the source table.");
        }

        var data = originalData.ToArray();
        for (var index = 0; index < records.Count; index++)
        {
            WriteRecord(records[index], data.AsSpan(index * RecordSize, RecordSize));
        }

        return data;
    }

    public static void WriteRecord(SwShPersonalRecord record, Span<byte> data)
    {
        if (data.Length < RecordSize)
        {
            throw new InvalidDataException($"Personal record must be {RecordSize} bytes.");
        }

        data[0x00] = checked((byte)record.HP);
        data[0x01] = checked((byte)record.Attack);
        data[0x02] = checked((byte)record.Defense);
        data[0x03] = checked((byte)record.Speed);
        data[0x04] = checked((byte)record.SpecialAttack);
        data[0x05] = checked((byte)record.SpecialDefense);
        data[0x06] = checked((byte)record.Type1);
        data[0x07] = checked((byte)record.Type2);
        data[0x08] = checked((byte)record.CatchRate);
        data[0x09] = checked((byte)record.EvolutionStage);

        var evYield =
            (BinaryPrimitives.ReadUInt16LittleEndian(data[0x0A..]) & 0xF000)
            |
            (record.EVYieldHP & 0x3)
            | ((record.EVYieldAttack & 0x3) << 2)
            | ((record.EVYieldDefense & 0x3) << 4)
            | ((record.EVYieldSpeed & 0x3) << 6)
            | ((record.EVYieldSpecialAttack & 0x3) << 8)
            | ((record.EVYieldSpecialDefense & 0x3) << 10);
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x0A..], checked((ushort)evYield));

        BinaryPrimitives.WriteInt16LittleEndian(data[0x0C..], checked((short)record.HeldItem1));
        BinaryPrimitives.WriteInt16LittleEndian(data[0x0E..], checked((short)record.HeldItem2));
        BinaryPrimitives.WriteInt16LittleEndian(data[0x10..], checked((short)record.HeldItem3));
        data[0x12] = checked((byte)record.GenderRatio);
        data[0x13] = checked((byte)record.HatchCycles);
        data[0x14] = checked((byte)record.BaseFriendship);
        data[0x15] = checked((byte)record.ExpGrowth);
        data[0x16] = checked((byte)record.EggGroup1);
        data[0x17] = checked((byte)record.EggGroup2);
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x18..], checked((ushort)record.Ability1));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x1A..], checked((ushort)record.Ability2));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x1C..], checked((ushort)record.HiddenAbility));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x1E..], checked((ushort)record.FormStatsIndex));
        data[0x20] = checked((byte)record.FormCount);
        data[0x21] = checked((byte)(
            (record.Color & 0x3F)
            | (record.IsPresentInGame ? 0x40 : 0)
            | (record.HasSpriteForm ? 0x80 : 0)));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x22..], checked((ushort)record.BaseExperience));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x24..], checked((ushort)record.Height));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x26..], checked((ushort)record.Weight));
        WriteFlags(record.TechnicalMachines, data[0x28..], TechnicalMachineCompatibilityCount);
        WriteFlags(record.TypeTutors, data[0x38..], TypeTutorCompatibilityCount);
        WriteFlags(record.TechnicalRecords, data[0x3C..], TechnicalRecordCompatibilityCount);
        BinaryPrimitives.WriteUInt32LittleEndian(data[0x4C..], record.ModelId);
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x56..], checked((ushort)record.HatchedSpecies));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x58..], checked((ushort)record.LocalFormIndex));

        var regionalFlags = record.RegionalFlags;
        regionalFlags = (regionalFlags & ~0x1) | (record.IsRegionalForm ? 0x1 : 0);
        regionalFlags = (regionalFlags & ~0x4) | (record.CanNotDynamax ? 0x4 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x5A..], checked((ushort)regionalFlags));

        BinaryPrimitives.WriteUInt16LittleEndian(data[0x5C..], checked((ushort)record.RegionalDexIndex));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0x5E..], checked((ushort)record.Form));
        WriteFlags(record.ArmorTutors, data[0xA8..], ArmorTutorCompatibilityCount);
        BinaryPrimitives.WriteUInt16LittleEndian(data[0xAC..], checked((ushort)record.ArmorDexIndex));
        BinaryPrimitives.WriteUInt16LittleEndian(data[0xAE..], checked((ushort)record.CrownDexIndex));
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
            TechnicalMachines: ReadFlags(data[0x28..], TechnicalMachineCompatibilityCount),
            TechnicalRecords: ReadFlags(data[0x3C..], TechnicalRecordCompatibilityCount),
            TypeTutors: ReadFlags(data[0x38..], TypeTutorCompatibilityCount),
            ArmorTutors: ReadFlags(data[0xA8..], ArmorTutorCompatibilityCount),
            ModelId: BinaryPrimitives.ReadUInt32LittleEndian(data[0x4C..]),
            HatchedSpecies: BinaryPrimitives.ReadUInt16LittleEndian(data[0x56..]),
            LocalFormIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0x58..]),
            RegionalFlags: BinaryPrimitives.ReadUInt16LittleEndian(data[0x5A..]),
            IsRegionalForm: (BinaryPrimitives.ReadUInt16LittleEndian(data[0x5A..]) & 1) == 1,
            CanNotDynamax: ((BinaryPrimitives.ReadUInt16LittleEndian(data[0x5A..]) >> 2) & 1) == 1,
            RegionalDexIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0x5C..]),
            Form: BinaryPrimitives.ReadUInt16LittleEndian(data[0x5E..]),
            ArmorDexIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0xAC..]),
            CrownDexIndex: BinaryPrimitives.ReadUInt16LittleEndian(data[0xAE..]));
    }

    private static bool[] ReadFlags(ReadOnlySpan<byte> data, int count)
    {
        var flags = new bool[count];
        for (var index = 0; index < count; index++)
        {
            flags[index] = GetFlag(data, index);
        }

        return flags;
    }

    private static void WriteFlags(IReadOnlyList<bool> flags, Span<byte> data, int expectedCount)
    {
        if (flags.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"Personal compatibility flag write expected {expectedCount} flags, but found {flags.Count}.");
        }

        for (var index = 0; index < expectedCount; index++)
        {
            SetFlag(data, index, flags[index]);
        }
    }

    private static bool GetFlag(ReadOnlySpan<byte> data, int bitIndex)
    {
        var byteIndex = bitIndex / 8;
        var mask = 1 << (bitIndex % 8);
        return (data[byteIndex] & mask) != 0;
    }

    private static void SetFlag(Span<byte> data, int bitIndex, bool enabled)
    {
        var byteIndex = bitIndex / 8;
        var mask = (byte)(1 << (bitIndex % 8));
        data[byteIndex] = enabled
            ? (byte)(data[byteIndex] | mask)
            : (byte)(data[byteIndex] & ~mask);
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
    IReadOnlyList<bool> TechnicalMachines,
    IReadOnlyList<bool> TechnicalRecords,
    IReadOnlyList<bool> TypeTutors,
    IReadOnlyList<bool> ArmorTutors,
    uint ModelId,
    int HatchedSpecies,
    int LocalFormIndex,
    int RegionalFlags,
    bool IsRegionalForm,
    bool CanNotDynamax,
    int RegionalDexIndex,
    int Form,
    int ArmorDexIndex,
    int CrownDexIndex)
{
    public int BaseStatTotal => HP + Attack + Defense + Speed + SpecialAttack + SpecialDefense;
}
