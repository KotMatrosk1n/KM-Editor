// SPDX-License-Identifier: GPL-3.0-only
namespace KM.SwSh.CustomGifts;

public static class SwShCustomGiftCatalogOdds
{
    public const int MaximumPoolSize = 14;
    public const int TotalWeight = 10000;

    public static IReadOnlyList<decimal> EqualChances(int count)
    {
        if (count == 0) return [];
        if (count is < 0 or > 128) throw new InvalidDataException("The Royal Gift pool is invalid.");
        var weight = TotalWeight / count;
        var remainder = TotalWeight % count;
        return Enumerable.Range(0, count).Select(index => (weight + (index < remainder ? 1 : 0)) / 100m).ToArray();
    }

    internal static SwShCustomGiftCatalog WithChances(SwShCustomGiftCatalog catalog)
    {
        if (catalog?.RoyalPools is null || catalog.RoyalPools.Any(pool => pool?.Pokemon is null))
            throw new InvalidDataException("The Royal Gift pool is invalid.");
        return catalog with
        {
            RoyalPools = catalog.RoyalPools.Select(pool => pool with { Chances = pool.Chances ?? EqualChances(pool.Pokemon.Count) }).ToArray(),
        };
    }
}
