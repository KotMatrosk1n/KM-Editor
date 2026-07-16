// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Numerics;

namespace KM.SwSh.DynamaxAdventures;

internal static class SwShDynamaxAdventureSeedPlanner
{
    public const int DefaultBossStartRow = 226;
    // Seed prediction performs the game's full 16-pick shuffle for each candidate. Keep the
    // synchronous bridge request bounded to a practical interactive workload.
    public const ulong MaximumSearchLimit = 10_000;
    public const int MaximumSearchResults = 1_000;
    public const int MaximumRequiredRows = SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount;
    private const int RentalTemplateCount = 6;
    private const int EncounterTemplateCount = 10;
    private const int PredictionTemplateCount = RentalTemplateCount + EncounterTemplateCount;

    public static SwShDynamaxAdventureSeedPrediction Predict(
        ulong seed,
        int npcCount,
        IReadOnlyList<SwShDynamaxAdventureRecord> entries,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        int bossStartRow = DefaultBossStartRow)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(personalRecords);

        ValidateNpcCount(npcCount);
        ValidateEntryCount(entries.Count);

        var templates = CreateTemplates(entries, personalRecords, bossStartRow);
        if (templates.Count == 0)
        {
            throw new InvalidDataException("Dynamax Adventure seed prediction requires at least one template row.");
        }

        var resultScratch = new SwShDynamaxAdventureSeedTemplate[PredictionTemplateCount];
        FillPredictionTemplates(
            seed,
            npcCount,
            templates,
            new int[templates.Count],
            new uint[624],
            resultScratch);
        return new SwShDynamaxAdventureSeedPrediction(
            seed,
            npcCount,
            resultScratch[..RentalTemplateCount],
            resultScratch[RentalTemplateCount..]);
    }

    private static void FillPredictionTemplates(
        ulong seed,
        int npcCount,
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        int[] poolScratch,
        uint[] mersenneTwisterScratch,
        SwShDynamaxAdventureSeedTemplate[] resultScratch)
    {
        ValidateNpcCount(npcCount);
        if (resultScratch.Length < PredictionTemplateCount)
        {
            throw new ArgumentException("Dynamax Adventure prediction result scratch is too small.", nameof(resultScratch));
        }

        var rng = new Xoroshiro(seed);
        var resultCount = 0;

        if (npcCount > 2)
        {
            rng.NextBound(27);
        }

        for (var index = 0; index < 4; index++)
        {
            SetTemplate(ref rng, templates, resultScratch, ref resultCount, poolScratch, mersenneTwisterScratch);
        }

        if (npcCount > 2)
        {
            rng.NextBound(2);
        }

        SetTemplate(ref rng, templates, resultScratch, ref resultCount, poolScratch, mersenneTwisterScratch);

        if (npcCount > 1)
        {
            rng.NextBound(2);
        }

        SetTemplate(ref rng, templates, resultScratch, ref resultCount, poolScratch, mersenneTwisterScratch);

        if (npcCount > 0)
        {
            rng.NextBound(2);
        }

        rng.NextBound(2);
        rng.NextBound(9);

        for (var index = 0; index < EncounterTemplateCount; index++)
        {
            SetEncounterTemplate(ref rng, templates, resultScratch, ref resultCount, poolScratch, mersenneTwisterScratch);
        }
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

        ValidateNpcCount(npcCount);
        ValidateEntryCount(entries.Count);
        if (requiredRows.Count == 0)
        {
            throw new ArgumentException(
                "Dynamax Adventure seed search requires at least one required row.",
                nameof(requiredRows));
        }

        if (requiredRows.Count > MaximumRequiredRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredRows),
                $"Dynamax Adventure seed search cannot accept more than {MaximumRequiredRows} required rows.");
        }

        if (limit > MaximumSearchLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Dynamax Adventure seed search limit cannot exceed {MaximumSearchLimit}." );
        }

        if (maxResults is < 1 or > MaximumSearchResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"Dynamax Adventure seed search results must be between 1 and {MaximumSearchResults}." );
        }

        if (limit == 0)
        {
            return [];
        }

        var canonicalRequiredRows = requiredRows.Distinct().ToArray();
        var templates = CreateTemplates(entries, personalRecords, bossStartRow);
        var poolScratch = new int[templates.Count];
        var mersenneTwisterScratch = new uint[624];
        var predictionScratch = new SwShDynamaxAdventureSeedTemplate[PredictionTemplateCount];
        var results = new List<SwShDynamaxAdventureSeedSearchResult>(maxResults);
        for (ulong offset = 0; offset < limit && results.Count < maxResults; offset++)
        {
            var seed = unchecked(startSeed + offset);
            FillPredictionTemplates(
                seed,
                npcCount,
                templates,
                poolScratch,
                mersenneTwisterScratch,
                predictionScratch);
            if (TryGetCanonicalRowPositions(predictionScratch, canonicalRequiredRows, out var positions))
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

    private static bool TryGetCanonicalRowPositions(
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> predictionTemplates,
        IReadOnlyList<int> canonicalRows,
        out IReadOnlyList<SwShDynamaxAdventureSeedRowPosition> positions)
    {
        Span<int> indexes = stackalloc int[canonicalRows.Count];
        for (var rowIndex = 0; rowIndex < canonicalRows.Count; rowIndex++)
        {
            indexes[rowIndex] = IndexOfRow(predictionTemplates, canonicalRows[rowIndex]);
            if (indexes[rowIndex] < 0)
            {
                positions = [];
                return false;
            }
        }

        var matched = new SwShDynamaxAdventureSeedRowPosition[canonicalRows.Count];
        for (var rowIndex = 0; rowIndex < canonicalRows.Count; rowIndex++)
        {
            var predictionIndex = indexes[rowIndex];
            matched[rowIndex] = predictionIndex < RentalTemplateCount
                ? new SwShDynamaxAdventureSeedRowPosition(
                    canonicalRows[rowIndex],
                    SwShDynamaxAdventureSeedSlotKind.Rental,
                    predictionIndex)
                : new SwShDynamaxAdventureSeedRowPosition(
                    canonicalRows[rowIndex],
                    SwShDynamaxAdventureSeedSlotKind.Encounter,
                    predictionIndex - RentalTemplateCount);
        }

        positions = matched;
        return true;
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
        var templates = new SwShDynamaxAdventureSeedTemplate[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.EntryIndex != index)
            {
                throw new InvalidDataException("Dynamax Adventure seed planning requires stable ordered table rows.");
            }

            var personal = SwShDynamaxAdventureSafetyRules.ResolvePersonalRecord(
                entry.Species,
                entry.Form,
                personalRecords)
                ?? throw new InvalidDataException(
                    $"Dynamax Adventure seed planning could not resolve personal data for row {entry.EntryIndex}, species {entry.Species}, form {entry.Form}.");
            templates[index] = new SwShDynamaxAdventureSeedTemplate(
                entry.EntryIndex,
                entry.Species,
                entry.Form,
                entry.EntryIndex >= bossStartRow,
                personal.Type1 != personal.Type2);
        }

        return templates;
    }

    private static void SetEncounterTemplate(
        ref Xoroshiro rng,
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        SwShDynamaxAdventureSeedTemplate[] results,
        ref int resultCount,
        int[] poolScratch,
        uint[] mersenneTwisterScratch)
    {
        SetTemplate(ref rng, templates, results, ref resultCount, poolScratch, mersenneTwisterScratch);
        if (results[resultCount - 1].HasTwoTypes)
        {
            rng.Next();
        }
    }

    private static void SetTemplate(
        ref Xoroshiro rng,
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        SwShDynamaxAdventureSeedTemplate[] results,
        ref int resultCount,
        int[] poolScratch,
        uint[] mersenneTwisterScratch)
    {
        FillPokemonPool(rng.Next(), poolScratch, mersenneTwisterScratch);
        results[resultCount] = GetTemplateFromPool(templates, poolScratch, results, resultCount);
        resultCount++;
    }

    private static void FillPokemonPool(uint seed, int[] pool, uint[] mersenneTwisterScratch)
    {
        ValidateEntryCount(pool.Length);
        for (var index = 0; index < pool.Length; index++)
        {
            pool[index] = index;
        }

        var mt = new MersenneTwister(seed, mersenneTwisterScratch);
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

    }

    private static void ValidateNpcCount(int npcCount)
    {
        if (npcCount is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(npcCount),
                npcCount,
                "Dynamax Adventure NPC count must be between 0 and 3.");
        }
    }

    private static void ValidateEntryCount(int count)
    {
        if (count is <= 0 or > SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount)
        {
            throw new InvalidDataException(
                $"Dynamax Adventure seed planning requires 1-{SwShDynamaxAdventuresWorkflowService.CanonicalBaseTableRowCount} bounded rows.");
        }
    }

    private static SwShDynamaxAdventureSeedTemplate GetTemplateFromPool(
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> templates,
        IReadOnlyList<int> pool,
        IReadOnlyList<SwShDynamaxAdventureSeedTemplate> previous,
        int previousCount)
    {
        for (var poolIndex = 0; poolIndex < pool.Count; poolIndex++)
        {
            var index = pool[poolIndex];
            var result = templates[index];
            var duplicate = false;
            for (var previousIndex = 0; previousIndex < previousCount; previousIndex++)
            {
                var candidate = previous[previousIndex];
                if (candidate.Species == result.Species && candidate.Form == result.Form)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!result.IsBoss && !duplicate)
            {
                return result;
            }
        }

        return templates[0];
    }

    private ref struct MersenneTwister
    {
        private const int StateSize = 624;
        private readonly Span<uint> state;
        private int index;

        public MersenneTwister(uint seed, Span<uint> state)
        {
            if (state.Length < StateSize)
            {
                throw new ArgumentException("Mersenne Twister scratch state is too small.", nameof(state));
            }

            this.state = state[..StateSize];
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

    private struct Xoroshiro
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
