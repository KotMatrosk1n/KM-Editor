// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Numerics;

namespace KM.SwSh.DynamaxAdventures;

internal static class SwShDynamaxAdventureSeedPlanner
{
    public const int DefaultBossStartRow = 226;

    public static SwShDynamaxAdventureSeedPrediction Predict(
        ulong seed,
        int npcCount,
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        int bossStartRow = DefaultBossStartRow)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(personalRecords);

        var templates = CreateTemplates(entries, personalRecords, bossStartRow);
        if (templates.Count == 0)
        {
            throw new InvalidDataException("Dynamax Adventure seed prediction requires at least one template row.");
        }

        var rng = new Xoroshiro(seed);
        var results = new List<SwShDynamaxAdventureSeedTemplate>(capacity: 16);

        if (npcCount > 2)
        {
            rng.NextBound(27);
        }

        for (var index = 0; index < 4; index++)
        {
            SetTemplate(rng, templates, results);
        }

        if (npcCount > 2)
        {
            rng.NextBound(2);
        }

        SetTemplate(rng, templates, results);

        if (npcCount > 1)
        {
            rng.NextBound(2);
        }

        SetTemplate(rng, templates, results);

        if (npcCount > 0)
        {
            rng.NextBound(2);
        }

        rng.NextBound(2);
        rng.NextBound(9);

        for (var index = 0; index < 10; index++)
        {
            SetEncounterTemplate(rng, templates, results);
        }

        return new SwShDynamaxAdventureSeedPrediction(
            seed,
            npcCount,
            results.Take(6).ToArray(),
            results.Skip(6).ToArray());
    }

    public static IReadOnlyList<SwShDynamaxAdventureSeedSearchResult> SearchRows(
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        IReadOnlyList<int> requiredRows,
        int npcCount,
        ulong startSeed,
        ulong limit,
        int maxResults,
        int bossStartRow = DefaultBossStartRow)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(personalRecords);
        ArgumentNullException.ThrowIfNull(requiredRows);

        if (maxResults <= 0 || limit == 0)
        {
            return [];
        }

        var distinctRequiredRowCount = requiredRows.Distinct().Count();
        var results = new List<SwShDynamaxAdventureSeedSearchResult>(maxResults);
        for (ulong offset = 0; offset < limit && results.Count < maxResults; offset++)
        {
            var seed = unchecked(startSeed + offset);
            var prediction = Predict(seed, npcCount, entries, personalRecords, bossStartRow);
            var positions = GetRowPositions(prediction, requiredRows);
            if (positions.Count == distinctRequiredRowCount)
            {
                results.Add(new SwShDynamaxAdventureSeedSearchResult(seed, positions));
            }
        }

        return results;
    }

    public static IReadOnlyList<SwShDynamaxAdventureSeedRowPosition> GetRowPositions(
        SwShDynamaxAdventureSeedPrediction prediction,
        IReadOnlyList<int> rows)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(rows);

        var positions = new List<SwShDynamaxAdventureSeedRowPosition>(rows.Count);
        foreach (var row in rows.Distinct())
        {
            var rentalIndex = IndexOfRow(prediction.Rentals, row);
            if (rentalIndex >= 0)
            {
                positions.Add(new SwShDynamaxAdventureSeedRowPosition(
                    row,
                    SwShDynamaxAdventureSeedSlotKind.Rental,
                    rentalIndex));
                continue;
            }

            var encounterIndex = IndexOfRow(prediction.Encounters, row);
            if (encounterIndex >= 0)
            {
                positions.Add(new SwShDynamaxAdventureSeedRowPosition(
                    row,
                    SwShDynamaxAdventureSeedSlotKind.Encounter,
                    encounterIndex));
            }
        }

        return positions;
    }

    private static int IndexOfRow(
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        int row)
    {
        for (var index = 0; index < templates.Count; index++)
        {
            if (templates[index].Row == row)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<SwShDynamaxAdventureSeedTemplate> CreateTemplates(
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        int bossStartRow)
    {
        return entries
            .Select(entry => new SwShDynamaxAdventureSeedTemplate(
                entry.EntryIndex,
                entry.Species,
                entry.Form,
                entry.EntryIndex >= bossStartRow,
                HasTwoTypes(entry, personalRecords)))
            .ToArray();
    }

    private static bool HasTwoTypes(
        SwShDynamaxAdventureRecord entry,
        IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        var personal = ResolvePersonalRecord(entry.Species, entry.Form, personalRecords);
        return personal is not null && personal.Type1 != personal.Type2;
    }

    private static SwShPersonalRecord? ResolvePersonalRecord(
        int species,
        int form,
        IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        if ((uint)species >= (uint)personalRecords.Count)
        {
            return null;
        }

        var record = personalRecords[species];
        if (form <= 0 || record.FormStatsIndex <= 0)
        {
            return record;
        }

        var formPersonalId = record.FormStatsIndex + form - 1;
        return (uint)formPersonalId < (uint)personalRecords.Count
            ? personalRecords[formPersonalId]
            : record;
    }

    private static void SetEncounterTemplate(
        Xoroshiro rng,
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        List<SwShDynamaxAdventureSeedTemplate> results)
    {
        SetTemplate(rng, templates, results);
        if (results[^1].HasTwoTypes)
        {
            rng.Next();
        }
    }

    private static void SetTemplate(
        Xoroshiro rng,
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        List<SwShDynamaxAdventureSeedTemplate> results)
    {
        var pool = CreatePokemonPool(rng.Next(), templates.Count);
        results.Add(GetTemplateFromPool(templates, pool, results));
    }

    private static int[] CreatePokemonPool(uint seed, int count)
    {
        var pool = Enumerable.Range(0, count).ToArray();
        var mt = new MersenneTwister(seed);
        var maxRand = pool.Length;

        for (var index = 0; index < pool.Length; index++)
        {
            maxRand -= 1;
            var rand = mt.NextBound(maxRand, 0x1FF) + index;
            if (rand != 0)
            {
                (pool[index], pool[rand]) = (pool[rand], pool[index]);
            }
        }

        return pool;
    }

    private static SwShDynamaxAdventureSeedTemplate GetTemplateFromPool(
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        IReadOnlyList<int> pool,
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> previous)
    {
        foreach (var index in pool)
        {
            var result = templates[index];
            var duplicate = previous.Any(candidate =>
                candidate.Species == result.Species && candidate.Form == result.Form);
            if (!result.IsBoss && !duplicate)
            {
                return result;
            }
        }

        return templates[0];
    }

    private sealed class MersenneTwister
    {
        private const int StateSize = 624;
        private readonly uint[] state = new uint[StateSize];
        private int index;

        public MersenneTwister(uint seed)
        {
            state[0] = seed;
            index = 1;
            var current = seed;
            for (; index < StateSize; index++)
            {
                current = unchecked((0x6C078965u * (current ^ (current >> 30))) + (uint)index);
                state[index] = current;
            }
        }

        public int NextBound(int max, uint mask)
        {
            if (max == 0)
            {
                return 0;
            }

            var inclusiveMax = (uint)(max + 1);
            uint rand;
            do
            {
                rand = Next() & mask;
            }
            while (inclusiveMax <= rand);

            return checked((int)rand);
        }

        private uint Next()
        {
            if (index >= StateSize)
            {
                Shuffle();
            }

            var value = state[index++];
            value ^= value >> 11;
            value ^= (value << 7) & 0x9D2C5680u;
            value ^= (value << 15) & 0xEFC60000u;
            value ^= value >> 18;
            return value;
        }

        private void Shuffle()
        {
            var mt1 = state[0];
            for (var shuffleIndex = 0; shuffleIndex < 227; shuffleIndex++)
            {
                var mt2 = state[shuffleIndex + 1];
                var value = (mt1 & 0x80000000u) | (mt2 & 0x7FFFFFFFu);
                var shifted = value >> 1;
                if ((value & 1) != 0)
                {
                    shifted ^= 0x9908B0DFu;
                }

                state[shuffleIndex] = shifted ^ state[shuffleIndex + 397];
                mt1 = mt2;
            }

            for (var shuffleIndex = 227; shuffleIndex < StateSize - 1; shuffleIndex++)
            {
                var mt2 = state[shuffleIndex + 1];
                var value = (mt1 & 0x80000000u) | (mt2 & 0x7FFFFFFFu);
                var shifted = value >> 1;
                if ((value & 1) != 0)
                {
                    shifted ^= 0x9908B0DFu;
                }

                state[shuffleIndex] = shifted ^ state[shuffleIndex - 227];
                mt1 = mt2;
            }

            var last = (mt1 & 0x80000000u) | (state[0] & 0x7FFFFFFFu);
            var lastShifted = last >> 1;
            if ((last & 1) != 0)
            {
                lastShifted ^= 0x9908B0DFu;
            }

            state[StateSize - 1] = lastShifted ^ state[396];
            index -= StateSize;
        }
    }

    private sealed class Xoroshiro
    {
        private const ulong State1Seed = 0x82A2B175229D6A5B;
        private ulong state0;
        private ulong state1 = State1Seed;

        public Xoroshiro(ulong seed)
        {
            state0 = seed;
        }

        public uint Next()
        {
            return unchecked((uint)NextU64());
        }

        public int NextBound(int max)
        {
            if (max == 0)
            {
                return 0;
            }

            var mask = (1u << (32 - BitOperations.LeadingZeroCount((uint)max))) - 1u;
            return NextBound(max, mask);
        }

        private int NextBound(int max, uint mask)
        {
            var inclusiveMax = (uint)(max + 1);
            uint rand;
            do
            {
                rand = Next() & mask;
            }
            while (inclusiveMax <= rand);

            return checked((int)rand);
        }

        private ulong NextU64()
        {
            var s0 = state0;
            var s1Original = state1;
            var result = unchecked(s0 + s1Original);
            var s1 = s1Original ^ s0;
            state0 = BitOperations.RotateLeft(s0, 24) ^ s1 ^ (s1 << 16);
            state1 = BitOperations.RotateLeft(s1, 37);
            return result;
        }
    }
}

internal sealed record SwShDynamaxAdventureSeedTemplate(
    int Row,
    int Species,
    int Form,
    bool IsBoss,
    bool HasTwoTypes);

internal sealed record SwShDynamaxAdventureSeedPrediction(
    ulong Seed,
    int NpcCount,
    IReadOnlyList<SwShDynamaxAdventureSeedTemplate> Rentals,
    IReadOnlyList<SwShDynamaxAdventureSeedTemplate> Encounters);

internal enum SwShDynamaxAdventureSeedSlotKind
{
    Rental,
    Encounter,
}

internal sealed record SwShDynamaxAdventureSeedRowPosition(
    int Row,
    SwShDynamaxAdventureSeedSlotKind Kind,
    int Slot);

internal sealed record SwShDynamaxAdventureSeedSearchResult(
    ulong Seed,
    IReadOnlyList<SwShDynamaxAdventureSeedRowPosition> Positions);
