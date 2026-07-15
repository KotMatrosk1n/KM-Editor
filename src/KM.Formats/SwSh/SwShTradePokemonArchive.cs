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
    private const int TradeFieldCount = 35;

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

    private IReadOnlyList<int>? SourceTradeTableOffsets { get; init; }

    private IReadOnlyList<int>? SourceTradeVectorElementOffsets { get; init; }

    public static SwShTradePokemonArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Trade Pokemon archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var tradesVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var count = ReadVectorLength(data, tradesVectorOffset);
        var trades = new SwShTradePokemonRecord[count];
        var tableOffsets = new int[count];
        var vectorElementOffsets = new int[count];
        var uniqueTableObjectRanges = new Dictionary<int, int>();
        for (var index = 0; index < count; index++)
        {
            var elementOffset = checked(tradesVectorOffset + sizeof(uint) + (index * sizeof(uint)));
            var tableOffset = ReadUOffset(data, elementOffset);
            var tableLayout = ReadTradeTableLayout(data, tableOffset, rejectUnknownFields: false);
            if (!uniqueTableObjectRanges.ContainsKey(tableOffset))
            {
                if (uniqueTableObjectRanges.Any(range =>
                    RangesOverlap(tableOffset, tableLayout.ObjectSize, range.Key, range.Value)))
                {
                    throw new InvalidDataException("Trade Pokemon table objects partially overlap.");
                }

                uniqueTableObjectRanges.Add(tableOffset, tableLayout.ObjectSize);
            }

            vectorElementOffsets[index] = elementOffset;
            tableOffsets[index] = tableOffset;
            trades[index] = ReadTrade(data, tableOffset, index);
        }

        return new SwShTradePokemonArchive(trades)
        {
            SourceData = data.ToArray(),
            SourceTradeTableOffsets = tableOffsets,
            SourceTradeVectorElementOffsets = vectorElementOffsets,
        };
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

        var materializedEdits = edits.ToArray();

        var trades = Trades
            .Select(trade => trade with
            {
                Ivs = trade.Ivs with { },
                RelearnMoves = trade.RelearnMoves.ToArray(),
            })
            .ToArray();

        foreach (var edit in materializedEdits)
        {
            ApplyEdit(trades, edit);
        }

        ValidateTouchedIvLayouts(trades, materializedEdits);

        if (SourceData is not null
            && SourceTradeTableOffsets is not null
            && SourceTradeVectorElementOffsets is not null)
        {
            return WriteEditsInPlace(trades);
        }

        return new SwShTradePokemonArchive(trades).Write();
    }

    private static void ValidateTouchedIvLayouts(
        IReadOnlyList<SwShTradePokemonRecord> trades,
        IReadOnlyList<SwShTradePokemonEdit> edits)
    {
        var ivTouchedTradeIndexes = edits
            .Where(edit => edit.Field is
                SwShTradePokemonField.IvHp
                or SwShTradePokemonField.IvAttack
                or SwShTradePokemonField.IvDefense
                or SwShTradePokemonField.IvSpeed
                or SwShTradePokemonField.IvSpecialAttack
                or SwShTradePokemonField.IvSpecialDefense
                or SwShTradePokemonField.FlawlessIvCount)
            .Select(edit => edit.TradeIndex)
            .Distinct();
        foreach (var tradeIndex in ivTouchedTradeIndexes)
        {
            var ivs = trades[tradeIndex].Ivs;
            if (ivs.Hp == ThreePerfectIvSentinel
                && (ivs.Attack != RandomIvValue
                    || ivs.Defense != RandomIvValue
                    || ivs.Speed != RandomIvValue
                    || ivs.SpecialAttack != RandomIvValue
                    || ivs.SpecialDefense != RandomIvValue))
            {
                throw new ArgumentException(
                    "Trade Pokemon HP IV -4 requires all five other IVs to be -1.",
                    nameof(edits));
            }
        }
    }

    public static int? GetFlawlessIvCount(SwShTradePokemonIvs ivs)
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
            SwShTradePokemonField.DynamaxLevel => trade with { DynamaxLevel = ValidateRange(edit.Value, 0, MaximumDynamaxLevel) },
            SwShTradePokemonField.BallItemId => trade with { BallItemId = ValidateBallItemId(edit.Value) },
            SwShTradePokemonField.Field03 => trade with { Field03 = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.CanGigantamax => trade with { CanGigantamax = ValidateBool(edit.Value) },
            SwShTradePokemonField.HeldItem => trade with { HeldItem = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.Level => trade with { Level = ValidateRange(edit.Value, MinimumLevel, MaximumLevel) },
            SwShTradePokemonField.Species => trade with { Species = ValidateRange(edit.Value, 1, MaximumIdValue) },
            SwShTradePokemonField.TrainerId => trade with { TrainerId = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.MemoryCode => trade with { MemoryCode = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.MemoryTextVariable => trade with { MemoryTextVariable = ValidateRange(edit.Value, 0, ushort.MaxValue) },
            SwShTradePokemonField.MemoryFeel => trade with { MemoryFeel = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.MemoryIntensity => trade with { MemoryIntensity = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.OtGender => trade with { OtGender = ValidateRange(edit.Value, 0, 1) },
            SwShTradePokemonField.RequiredForm => trade with { RequiredForm = ValidateRange(edit.Value, 0, MaximumByteValue) },
            SwShTradePokemonField.RequiredSpecies => trade with { RequiredSpecies = ValidateRange(edit.Value, 0, MaximumIdValue) },
            SwShTradePokemonField.RequiredNature => trade with { RequiredNature = ValidateRange(edit.Value, 0, 25) },
            SwShTradePokemonField.UnknownRequirement => trade with { UnknownRequirement = ValidateUnknownRequirement(edit.Value) },
            SwShTradePokemonField.ShinyLock => trade with { ShinyLock = ValidateRange(edit.Value, 0, 2) },
            SwShTradePokemonField.Nature => trade with { Nature = ValidateRange(edit.Value, 0, 25) },
            SwShTradePokemonField.Gender => trade with { Gender = ValidateRange(edit.Value, 0, 2) },
            SwShTradePokemonField.IvHp => trade with { Ivs = trade.Ivs with { Hp = ValidateHpIvValue(edit.Value) } },
            SwShTradePokemonField.IvAttack => trade with { Ivs = trade.Ivs with { Attack = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvDefense => trade with { Ivs = trade.Ivs with { Defense = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvSpeed => trade with { Ivs = trade.Ivs with { Speed = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvSpecialAttack => trade with { Ivs = trade.Ivs with { SpecialAttack = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.IvSpecialDefense => trade with { Ivs = trade.Ivs with { SpecialDefense = ValidateIvValue(edit.Value) } },
            SwShTradePokemonField.Ability => trade with { Ability = ValidateRange(edit.Value, 0, 3) },
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

    private static int ValidateBallItemId(int value)
    {
        if (IsValidBallItemId(value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            $"Trade Pokemon ball item ID {value} is not a supported Sword/Shield ball item ID.");
    }

    private static int ValidateUnknownRequirement(int value)
    {
        if (value == 0)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            "Trade Pokemon unknown requirement is unconfirmed and can only be cleared to 0.");
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

    private byte[] WriteEditsInPlace(IReadOnlyList<SwShTradePokemonRecord> trades)
    {
        if (SourceData is null
            || SourceTradeTableOffsets is null
            || SourceTradeVectorElementOffsets is null
            || trades.Count != Trades.Count
            || SourceTradeTableOffsets.Count != Trades.Count
            || SourceTradeVectorElementOffsets.Count != Trades.Count)
        {
            throw new InvalidDataException("Trade Pokemon archive source layout is unavailable or inconsistent.");
        }

        var changedFieldsByTrade = Enumerable.Range(0, trades.Count)
            .Select(index => new
            {
                TradeIndex = index,
                FieldIndexes = GetChangedFieldIndexes(Trades[index], trades[index]),
            })
            .Where(change => change.FieldIndexes.Count > 0)
            .ToArray();
        if (changedFieldsByTrade.Length == 0)
        {
            return SourceData.ToArray();
        }

        var outputBytes = new List<byte>(SourceData.Length + (changedFieldsByTrade.Length * 16));
        outputBytes.AddRange(SourceData);
        var effectiveTableOffsets = SourceTradeTableOffsets.ToArray();
        var aliasedTableOffsets = SourceTradeTableOffsets
            .GroupBy(tableOffset => tableOffset)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        foreach (var change in changedFieldsByTrade)
        {
            var tableOffset = effectiveTableOffsets[change.TradeIndex];
            var tableLayout = ReadTradeTableLayout(SourceData, tableOffset, rejectUnknownFields: false);
            if (tableLayout.HasMaterializedUnknownFields)
            {
                throw new InvalidDataException(
                    $"Trade Pokemon table {change.TradeIndex} contains materialized unknown fields and cannot be safely edited.");
            }

            var missingFieldIndexes = change.FieldIndexes
                .Where(fieldIndex => ReadTableFieldOffset(
                    SourceData,
                    tableOffset,
                    fieldIndex,
                    GetTradeFieldSize(fieldIndex)) == 0)
                .ToArray();
            if (missingFieldIndexes.Length == 0 && !aliasedTableOffsets.Contains(tableOffset))
            {
                continue;
            }

            var expandedTableOffset = AppendTradeTableCopy(
                outputBytes,
                SourceData,
                tableOffset,
                missingFieldIndexes);
            PatchUOffset(
                outputBytes,
                SourceTradeVectorElementOffsets[change.TradeIndex],
                expandedTableOffset);
            effectiveTableOffsets[change.TradeIndex] = expandedTableOffset;
        }

        var output = outputBytes.ToArray();
        foreach (var change in changedFieldsByTrade)
        {
            var original = Trades[change.TradeIndex];
            var updated = trades[change.TradeIndex];
            var tableOffset = effectiveTableOffsets[change.TradeIndex];

            PatchByte(output, tableOffset, 0, original.Form, updated.Form);
            PatchByte(output, tableOffset, 1, original.DynamaxLevel, updated.DynamaxLevel);
            PatchInt32(output, tableOffset, 2, original.BallItemId, updated.BallItemId);
            PatchInt32(output, tableOffset, 3, original.Field03, updated.Field03);
            PatchBool(output, tableOffset, 5, original.CanGigantamax, updated.CanGigantamax);
            PatchInt32(output, tableOffset, 6, original.HeldItem, updated.HeldItem);
            PatchByte(output, tableOffset, 7, original.Level, updated.Level);
            PatchInt32(output, tableOffset, 8, original.Species, updated.Species);
            PatchInt32(output, tableOffset, 10, original.TrainerId, updated.TrainerId);
            PatchByte(output, tableOffset, 11, original.MemoryCode, updated.MemoryCode);
            PatchUInt16(output, tableOffset, 12, original.MemoryTextVariable, updated.MemoryTextVariable);
            PatchByte(output, tableOffset, 13, original.MemoryFeel, updated.MemoryFeel);
            PatchByte(output, tableOffset, 14, original.MemoryIntensity, updated.MemoryIntensity);
            PatchByte(output, tableOffset, 16, original.OtGender, updated.OtGender);
            PatchByte(output, tableOffset, 17, original.RequiredForm, updated.RequiredForm);
            PatchInt32(output, tableOffset, 18, original.RequiredSpecies, updated.RequiredSpecies);
            PatchInt32(output, tableOffset, 19, original.RequiredNature, updated.RequiredNature);
            PatchByte(output, tableOffset, 20, original.UnknownRequirement, updated.UnknownRequirement);
            PatchInt32(output, tableOffset, 21, original.ShinyLock, updated.ShinyLock);
            PatchInt32(output, tableOffset, 22, original.Nature, updated.Nature);
            PatchSByte(output, tableOffset, 23, original.Gender, updated.Gender);
            PatchSByte(output, tableOffset, 27, original.Ivs.Hp, updated.Ivs.Hp);
            PatchSByte(output, tableOffset, 25, original.Ivs.Attack, updated.Ivs.Attack);
            PatchSByte(output, tableOffset, 26, original.Ivs.Defense, updated.Ivs.Defense);
            PatchSByte(output, tableOffset, 24, original.Ivs.Speed, updated.Ivs.Speed);
            PatchSByte(output, tableOffset, 28, original.Ivs.SpecialAttack, updated.Ivs.SpecialAttack);
            PatchSByte(output, tableOffset, 29, original.Ivs.SpecialDefense, updated.Ivs.SpecialDefense);
            PatchByte(output, tableOffset, 30, original.Ability, updated.Ability);
            PatchUInt16(output, tableOffset, 31, original.RelearnMoves[0], updated.RelearnMoves[0]);
            PatchUInt16(output, tableOffset, 32, original.RelearnMoves[1], updated.RelearnMoves[1]);
            PatchUInt16(output, tableOffset, 33, original.RelearnMoves[2], updated.RelearnMoves[2]);
            PatchUInt16(output, tableOffset, 34, original.RelearnMoves[3], updated.RelearnMoves[3]);
        }

        return output;
    }

    private static IReadOnlyList<int> GetChangedFieldIndexes(
        SwShTradePokemonRecord original,
        SwShTradePokemonRecord updated)
    {
        var fieldIndexes = new List<int>();
        AddIfChanged(0, original.Form, updated.Form);
        AddIfChanged(1, original.DynamaxLevel, updated.DynamaxLevel);
        AddIfChanged(2, original.BallItemId, updated.BallItemId);
        AddIfChanged(3, original.Field03, updated.Field03);
        AddIfChanged(5, original.CanGigantamax, updated.CanGigantamax);
        AddIfChanged(6, original.HeldItem, updated.HeldItem);
        AddIfChanged(7, original.Level, updated.Level);
        AddIfChanged(8, original.Species, updated.Species);
        AddIfChanged(10, original.TrainerId, updated.TrainerId);
        AddIfChanged(11, original.MemoryCode, updated.MemoryCode);
        AddIfChanged(12, original.MemoryTextVariable, updated.MemoryTextVariable);
        AddIfChanged(13, original.MemoryFeel, updated.MemoryFeel);
        AddIfChanged(14, original.MemoryIntensity, updated.MemoryIntensity);
        AddIfChanged(16, original.OtGender, updated.OtGender);
        AddIfChanged(17, original.RequiredForm, updated.RequiredForm);
        AddIfChanged(18, original.RequiredSpecies, updated.RequiredSpecies);
        AddIfChanged(19, original.RequiredNature, updated.RequiredNature);
        AddIfChanged(20, original.UnknownRequirement, updated.UnknownRequirement);
        AddIfChanged(21, original.ShinyLock, updated.ShinyLock);
        AddIfChanged(22, original.Nature, updated.Nature);
        AddIfChanged(23, original.Gender, updated.Gender);
        AddIfChanged(27, original.Ivs.Hp, updated.Ivs.Hp);
        AddIfChanged(25, original.Ivs.Attack, updated.Ivs.Attack);
        AddIfChanged(26, original.Ivs.Defense, updated.Ivs.Defense);
        AddIfChanged(24, original.Ivs.Speed, updated.Ivs.Speed);
        AddIfChanged(28, original.Ivs.SpecialAttack, updated.Ivs.SpecialAttack);
        AddIfChanged(29, original.Ivs.SpecialDefense, updated.Ivs.SpecialDefense);
        AddIfChanged(30, original.Ability, updated.Ability);
        AddIfChanged(31, original.RelearnMoves[0], updated.RelearnMoves[0]);
        AddIfChanged(32, original.RelearnMoves[1], updated.RelearnMoves[1]);
        AddIfChanged(33, original.RelearnMoves[2], updated.RelearnMoves[2]);
        AddIfChanged(34, original.RelearnMoves[3], updated.RelearnMoves[3]);
        return fieldIndexes;

        void AddIfChanged<T>(int fieldIndex, T originalValue, T updatedValue)
        {
            if (!EqualityComparer<T>.Default.Equals(originalValue, updatedValue))
            {
                fieldIndexes.Add(fieldIndex);
            }
        }
    }

    private static int GetTradeFieldSize(int fieldIndex)
    {
        return fieldIndex switch
        {
            2 or 3 or 6 or 8 or 10 or 18 or 19 or 21 or 22 => sizeof(int),
            4 or 9 or 15 => sizeof(ulong),
            12 or 31 or 32 or 33 or 34 => sizeof(ushort),
            0 or 1 or 5 or 7 or 11 or 13 or 14 or 16 or 17 or 20
                or 23 or 24 or 25 or 26 or 27 or 28 or 29 or 30 => sizeof(byte),
            _ => throw new InvalidDataException($"Trade Pokemon field {fieldIndex} is not recognized."),
        };
    }

    private static int AppendTradeTableCopy(
        List<byte> output,
        ReadOnlySpan<byte> source,
        int tableOffset,
        IReadOnlyList<int> missingFieldIndexes)
    {
        var layout = ReadTradeTableLayout(source, tableOffset, rejectUnknownFields: true);
        var orderedFieldIndexes = missingFieldIndexes
            .Distinct()
            .OrderByDescending(GetTradeFieldSize)
            .ThenBy(fieldIndex => fieldIndex)
            .ToArray();

        foreach (var fieldIndex in orderedFieldIndexes)
        {
            if (ReadTableFieldOffset(source, tableOffset, fieldIndex, GetTradeFieldSize(fieldIndex)) != 0)
            {
                throw new InvalidOperationException(
                    $"Trade Pokemon field {fieldIndex} is already materialized in the source table.");
            }
        }

        var requiredVtableLength = orderedFieldIndexes.Length == 0
            ? layout.VtableLength
            : checked((sizeof(ushort) * 2) + ((orderedFieldIndexes.Max() + 1) * sizeof(ushort)));
        var expandedVtableLength = Math.Max(layout.VtableLength, requiredVtableLength);
        if (expandedVtableLength > ushort.MaxValue)
        {
            throw new InvalidDataException("Expanded Trade Pokemon vtable is too large.");
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
            var fieldSize = GetTradeFieldSize(fieldIndex);
            AlignBuffer(output, fieldSize);
            var fieldOffset = checked(output.Count - expandedTableOffset);
            if (fieldOffset > ushort.MaxValue)
            {
                throw new InvalidDataException("Expanded Trade Pokemon table field offset is too large.");
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
            throw new InvalidDataException("Expanded Trade Pokemon table is too large.");
        }

        WriteUInt16At(
            output,
            expandedVtableOffset + sizeof(ushort),
            checked((ushort)expandedObjectSize));
        return expandedTableOffset;
    }

    private static TableLayout ReadTradeTableLayout(
        ReadOnlySpan<byte> data,
        int tableOffset,
        bool rejectUnknownFields)
    {
        var layout = ReadTableLayout(data, tableOffset);
        var materializedFieldRanges = new List<(int Start, int End)>();
        var hasMaterializedUnknownFields = false;
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

            if (fieldIndex >= TradeFieldCount)
            {
                hasMaterializedUnknownFields = true;
                if (rejectUnknownFields)
                {
                    throw new InvalidDataException(
                        $"Trade Pokemon table contains unknown field {fieldIndex} and cannot be safely expanded.");
                }

                continue;
            }

            var fieldSize = GetTradeFieldSize(fieldIndex);
            _ = ReadTableFieldOffset(data, tableOffset, fieldIndex, fieldSize);
            var fieldEnd = checked(fieldOffset + fieldSize);
            if (materializedFieldRanges.Any(range =>
                fieldOffset < range.End && fieldEnd > range.Start))
            {
                throw new InvalidDataException(
                    $"Trade Pokemon field {fieldIndex} overlaps another scalar field.");
            }

            materializedFieldRanges.Add((fieldOffset, fieldEnd));
        }

        return layout with { HasMaterializedUnknownFields = hasMaterializedUnknownFields };
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
            throw new InvalidDataException($"Trade Pokemon field {fieldIndex} is missing after table expansion.");
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

    private static void PatchUInt16(byte[] output, int tableOffset, int fieldIndex, int original, int updated)
    {
        if (original == updated)
        {
            return;
        }

        var offset = GetMaterializedFieldOffset(output, tableOffset, fieldIndex, sizeof(ushort));
        EnsureRange(output, offset, sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset, sizeof(ushort)), checked((ushort)updated));
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

    private static SwShTradePokemonRecord ReadTrade(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        _ = ReadTradeTableLayout(data, tableOffset, rejectUnknownFields: false);

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
            throw new InvalidDataException("Expanded Trade Pokemon table must follow its vector element.");
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
        int ObjectSize,
        bool HasMaterializedUnknownFields = false);

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
