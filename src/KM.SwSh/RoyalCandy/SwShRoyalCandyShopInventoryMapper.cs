// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.SwSh.RoyalCandy;

internal static class SwShRoyalCandyShopPatchMapper
{
    private const int RoyalCandyItemId = 1128;

    public static SwShRoyalCandyShopPatchMapping Analyze(
        SwShShopDataFile targetData,
        SwShShopDataFile baseData)
    {
        ArgumentNullException.ThrowIfNull(targetData);
        ArgumentNullException.ThrowIfNull(baseData);

        var removalEdits = new List<SwShShopInventoryEdit>();
        var restoreEdits = new List<SwShShopInventoryEdit>();
        var baseOccurrences = 0;
        var matchedOccurrences = 0;
        var missingOccurrences = 0;

        foreach (var (baseShop, baseIndex) in baseData.SingleShops.Select((shop, index) => (shop, index)))
        {
            if (!baseShop.Inventory.Items.Contains(RoyalCandyItemId))
            {
                continue;
            }

            var targetIndex = ResolveTargetShopIndex(
                targetData.SingleShops,
                baseIndex,
                baseShop.Hash,
                baseData.SingleShops.Count(shop => shop.Hash == baseShop.Hash),
                shop => shop.Hash,
                "single");
            AddInventoryMapping(
                removalEdits,
                restoreEdits,
                SwShShopKind.Single,
                baseShop.Hash,
                inventoryIndex: 0,
                targetIndex,
                targetData.SingleShops[targetIndex].Inventory.Items,
                baseShop.Inventory.Items,
                ref baseOccurrences,
                ref matchedOccurrences,
                ref missingOccurrences);
        }

        foreach (var (baseShop, baseIndex) in baseData.MultiShops.Select((shop, index) => (shop, index)))
        {
            var ownedInventoryIndexes = baseShop.Inventories
                .Select((inventory, index) => (inventory, index))
                .Where(entry => entry.inventory.Items.Contains(RoyalCandyItemId))
                .Select(entry => entry.index)
                .ToArray();
            if (ownedInventoryIndexes.Length == 0)
            {
                continue;
            }

            var targetIndex = ResolveTargetShopIndex(
                targetData.MultiShops,
                baseIndex,
                baseShop.Hash,
                baseData.MultiShops.Count(shop => shop.Hash == baseShop.Hash),
                shop => shop.Hash,
                "multi");
            var targetShop = targetData.MultiShops[targetIndex];
            if (targetShop.Inventories.Count != baseShop.Inventories.Count)
            {
                throw new SwShRoyalCandyShopMappingException(
                    "A Royal Candy multi shop has a different inventory count in base and layered data, so its physical inventory identities cannot be proven.");
            }

            foreach (var inventoryIndex in ownedInventoryIndexes)
            {
                if ((uint)inventoryIndex >= (uint)targetShop.Inventories.Count)
                {
                    throw new SwShRoyalCandyShopMappingException(
                        $"Base Royal Candy multi-shop inventory {inventoryIndex} is not present in the layered shop data.");
                }

                AddInventoryMapping(
                    removalEdits,
                    restoreEdits,
                    SwShShopKind.Multi,
                    baseShop.Hash,
                    inventoryIndex,
                    targetIndex,
                    targetShop.Inventories[inventoryIndex].Items,
                    baseShop.Inventories[inventoryIndex].Items,
                    ref baseOccurrences,
                    ref matchedOccurrences,
                    ref missingOccurrences);
            }
        }

        return new SwShRoyalCandyShopPatchMapping(
            removalEdits,
            restoreEdits,
            baseOccurrences,
            matchedOccurrences,
            missingOccurrences);
    }

    private static void AddInventoryMapping(
        ICollection<SwShShopInventoryEdit> removalEdits,
        ICollection<SwShShopInventoryEdit> restoreEdits,
        SwShShopKind kind,
        ulong hash,
        int inventoryIndex,
        int targetShopIndex,
        IReadOnlyList<int> targetItems,
        IReadOnlyList<int> baseItems,
        ref int baseOccurrences,
        ref int matchedOccurrences,
        ref int missingOccurrences)
    {
        var mapping = SwShRoyalCandyShopInventoryMapper.Analyze(baseItems, targetItems);
        var inventoryBaseOccurrences = baseItems.Count(itemId => itemId == RoyalCandyItemId);
        if (mapping.MatchedTargetSlots.Count + mapping.MissingOccurrences.Count != inventoryBaseOccurrences)
        {
            throw new SwShRoyalCandyShopMappingException(
                "The Royal Candy shop mapping did not account for every base item 1128 occurrence.");
        }

        baseOccurrences += inventoryBaseOccurrences;
        matchedOccurrences += mapping.MatchedTargetSlots.Count;
        missingOccurrences += mapping.MissingOccurrences.Count;

        if (mapping.MatchedTargetSlots.Count > 0)
        {
            var removedItems = targetItems.ToList();
            foreach (var slot in mapping.MatchedTargetSlots.OrderDescending())
            {
                removedItems.RemoveAt(slot);
            }

            removalEdits.Add(CreateSetEdit(
                kind,
                hash,
                inventoryIndex,
                targetShopIndex,
                removedItems));
        }

        if (mapping.MissingOccurrences.Count > 0)
        {
            var restoredItems = targetItems.ToList();
            foreach (var occurrence in mapping.MissingOccurrences.OrderByDescending(occurrence => occurrence.TargetSlot))
            {
                restoredItems.Insert(occurrence.TargetSlot, RoyalCandyItemId);
            }

            restoreEdits.Add(CreateSetEdit(
                kind,
                hash,
                inventoryIndex,
                targetShopIndex,
                restoredItems));
        }
    }

    private static SwShShopInventoryEdit CreateSetEdit(
        SwShShopKind kind,
        ulong hash,
        int inventoryIndex,
        int shopIndex,
        IReadOnlyList<int> items)
    {
        return new SwShShopInventoryEdit(
            kind,
            hash,
            inventoryIndex,
            Slot: 0,
            ItemId: 0,
            Action: SwShShopInventoryEditAction.Set,
            Items: items,
            ShopIndex: shopIndex);
    }

    private static int ResolveTargetShopIndex<TShop>(
        IReadOnlyList<TShop> targetShops,
        int baseIndex,
        ulong hash,
        int expectedHashCount,
        Func<TShop, ulong> getHash,
        string kind)
    {
        var targetHashCount = targetShops.Count(shop => getHash(shop) == hash);
        if (targetHashCount != expectedHashCount)
        {
            throw new SwShRoyalCandyShopMappingException(
                $"The Royal Candy {kind} shop hash 0x{hash:X16} occurs {targetHashCount} time(s) in layered data; expected {expectedHashCount} physical occurrence(s) from base.");
        }

        if ((uint)baseIndex < (uint)targetShops.Count && getHash(targetShops[baseIndex]) == hash)
        {
            return baseIndex;
        }

        if (expectedHashCount == 1)
        {
            var matches = targetShops
                .Select((shop, index) => (shop, index))
                .Where(entry => getHash(entry.shop) == hash)
                .Select(entry => entry.index)
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }
        }

        throw new SwShRoyalCandyShopMappingException(
            $"The base Royal Candy {kind} shop at physical index {baseIndex} cannot be mapped uniquely in the layered shop data.");
    }
}

internal static class SwShRoyalCandyShopInventoryMapper
{
    private const int RoyalCandyItemId = 1128;

    public static SwShRoyalCandyShopInventoryMapping Analyze(
        IReadOnlyList<int> baseItems,
        IReadOnlyList<int> targetItems)
    {
        ArgumentNullException.ThrowIfNull(baseItems);
        ArgumentNullException.ThrowIfNull(targetItems);

        var prefix = BuildPrefixEditDistances(baseItems, targetItems);
        var suffix = BuildSuffixEditDistances(baseItems, targetItems);
        var totalDistance = prefix[baseItems.Count, targetItems.Count];
        var matchedSlots = new List<int>();
        var missingOccurrences = new List<SwShRoyalCandyMissingShopOccurrence>();

        foreach (var (itemId, baseIndex) in baseItems.Select((itemId, index) => (itemId, index)))
        {
            if (itemId != RoyalCandyItemId)
            {
                continue;
            }

            var matchCandidates = Enumerable.Range(0, targetItems.Count)
                .Where(targetIndex => targetItems[targetIndex] == RoyalCandyItemId
                    && prefix[baseIndex, targetIndex] + suffix[baseIndex + 1, targetIndex + 1] == totalDistance)
                .ToArray();
            var deletionCandidates = Enumerable.Range(0, targetItems.Count + 1)
                .Where(targetIndex => prefix[baseIndex, targetIndex] + 1 + suffix[baseIndex + 1, targetIndex] == totalDistance)
                .ToArray();

            if (matchCandidates.Length > 1
                || deletionCandidates.Length > 1
                || (matchCandidates.Length == 1 && deletionCandidates.Length == 1)
                || (matchCandidates.Length == 0 && deletionCandidates.Length == 0))
            {
                throw new SwShRoyalCandyShopMappingException(
                    $"Base Royal Candy shop occurrence at slot {baseIndex} cannot be isolated uniquely after unrelated inventory edits.");
            }

            if (matchCandidates.Length == 1)
            {
                matchedSlots.Add(matchCandidates[0]);
            }
            else
            {
                missingOccurrences.Add(new SwShRoyalCandyMissingShopOccurrence(baseIndex, deletionCandidates[0]));
            }
        }

        if (matchedSlots.Count != matchedSlots.Distinct().Count())
        {
            throw new SwShRoyalCandyShopMappingException(
                "Multiple base Royal Candy shop occurrences resolve to the same target slot.");
        }

        return new SwShRoyalCandyShopInventoryMapping(matchedSlots, missingOccurrences);
    }

    private static int[,] BuildPrefixEditDistances(
        IReadOnlyList<int> baseItems,
        IReadOnlyList<int> targetItems)
    {
        var distances = new int[baseItems.Count + 1, targetItems.Count + 1];
        for (var baseIndex = 0; baseIndex <= baseItems.Count; baseIndex++)
        {
            distances[baseIndex, 0] = baseIndex;
        }

        for (var targetIndex = 0; targetIndex <= targetItems.Count; targetIndex++)
        {
            distances[0, targetIndex] = targetIndex;
        }

        for (var baseIndex = 1; baseIndex <= baseItems.Count; baseIndex++)
        {
            for (var targetIndex = 1; targetIndex <= targetItems.Count; targetIndex++)
            {
                var best = Math.Min(
                    distances[baseIndex - 1, targetIndex] + 1,
                    distances[baseIndex, targetIndex - 1] + 1);
                if (baseItems[baseIndex - 1] == targetItems[targetIndex - 1])
                {
                    best = Math.Min(best, distances[baseIndex - 1, targetIndex - 1]);
                }

                distances[baseIndex, targetIndex] = best;
            }
        }

        return distances;
    }

    private static int[,] BuildSuffixEditDistances(
        IReadOnlyList<int> baseItems,
        IReadOnlyList<int> targetItems)
    {
        var distances = new int[baseItems.Count + 1, targetItems.Count + 1];
        for (var baseIndex = baseItems.Count; baseIndex >= 0; baseIndex--)
        {
            distances[baseIndex, targetItems.Count] = baseItems.Count - baseIndex;
        }

        for (var targetIndex = targetItems.Count; targetIndex >= 0; targetIndex--)
        {
            distances[baseItems.Count, targetIndex] = targetItems.Count - targetIndex;
        }

        for (var baseIndex = baseItems.Count - 1; baseIndex >= 0; baseIndex--)
        {
            for (var targetIndex = targetItems.Count - 1; targetIndex >= 0; targetIndex--)
            {
                var best = Math.Min(
                    distances[baseIndex + 1, targetIndex] + 1,
                    distances[baseIndex, targetIndex + 1] + 1);
                if (baseItems[baseIndex] == targetItems[targetIndex])
                {
                    best = Math.Min(best, distances[baseIndex + 1, targetIndex + 1]);
                }

                distances[baseIndex, targetIndex] = best;
            }
        }

        return distances;
    }
}

internal sealed record SwShRoyalCandyShopInventoryMapping(
    IReadOnlyList<int> MatchedTargetSlots,
    IReadOnlyList<SwShRoyalCandyMissingShopOccurrence> MissingOccurrences);

internal sealed record SwShRoyalCandyShopPatchMapping(
    IReadOnlyList<SwShShopInventoryEdit> RemovalEdits,
    IReadOnlyList<SwShShopInventoryEdit> RestoreEdits,
    int BaseOccurrences,
    int MatchedOccurrences,
    int MissingOccurrences);

internal sealed record SwShRoyalCandyMissingShopOccurrence(int BaseSlot, int TargetSlot);

internal sealed class SwShRoyalCandyShopMappingException(string message) : IOException(message);
