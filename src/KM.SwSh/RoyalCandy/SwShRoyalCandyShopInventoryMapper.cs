// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.SwSh.RoyalCandy;

internal static class SwShRoyalCandyShopPatchMapper
{
    private const int RoyalCandyItemId = 1128;
    private const int RareCandyItemId = 50;

    public static SwShRoyalCandyShopPatchMapping Analyze(
        SwShShopDataFile targetData,
        SwShShopDataFile baseData)
    {
        ArgumentNullException.ThrowIfNull(targetData);
        ArgumentNullException.ThrowIfNull(baseData);

        var installEdits = new List<SwShShopInventoryEdit>();
        var uninstallEdits = new List<SwShShopInventoryEdit>();
        var baseOccurrences = 0;
        var originalOccurrences = 0;
        var ownedReplacementOccurrences = 0;
        var legacyMissingOccurrences = 0;

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
                installEdits,
                uninstallEdits,
                SwShShopKind.Single,
                baseShop.Hash,
                inventoryIndex: 0,
                targetIndex,
                targetData.SingleShops[targetIndex].Inventory.Items,
                baseShop.Inventory.Items,
                ref baseOccurrences,
                ref originalOccurrences,
                ref ownedReplacementOccurrences,
                ref legacyMissingOccurrences);
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
                    installEdits,
                    uninstallEdits,
                    SwShShopKind.Multi,
                    baseShop.Hash,
                    inventoryIndex,
                    targetIndex,
                    targetShop.Inventories[inventoryIndex].Items,
                    baseShop.Inventories[inventoryIndex].Items,
                    ref baseOccurrences,
                    ref originalOccurrences,
                    ref ownedReplacementOccurrences,
                    ref legacyMissingOccurrences);
            }
        }

        return new SwShRoyalCandyShopPatchMapping(
            installEdits,
            uninstallEdits,
            baseOccurrences,
            originalOccurrences,
            ownedReplacementOccurrences,
            legacyMissingOccurrences);
    }

    private static void AddInventoryMapping(
        ICollection<SwShShopInventoryEdit> installEdits,
        ICollection<SwShShopInventoryEdit> uninstallEdits,
        SwShShopKind kind,
        ulong hash,
        int inventoryIndex,
        int targetShopIndex,
        IReadOnlyList<int> targetItems,
        IReadOnlyList<int> baseItems,
        ref int baseOccurrences,
        ref int originalOccurrences,
        ref int ownedReplacementOccurrences,
        ref int legacyMissingOccurrences)
    {
        var mapping = SwShRoyalCandyShopInventoryMapper.Analyze(baseItems, targetItems);
        var inventoryBaseOccurrences = baseItems.Count(itemId => itemId == RoyalCandyItemId);
        if (mapping.OriginalTargetSlots.Count
                + mapping.OwnedReplacementTargetSlots.Count
                + mapping.LegacyMissingOccurrences.Count
            != inventoryBaseOccurrences)
        {
            throw new SwShRoyalCandyShopMappingException(
                "The Royal Candy shop mapping did not account for every base item 1128 occurrence.");
        }

        baseOccurrences += inventoryBaseOccurrences;
        originalOccurrences += mapping.OriginalTargetSlots.Count;
        ownedReplacementOccurrences += mapping.OwnedReplacementTargetSlots.Count;
        legacyMissingOccurrences += mapping.LegacyMissingOccurrences.Count;

        if (mapping.OriginalTargetSlots.Count > 0
            || mapping.LegacyMissingOccurrences.Count > 0)
        {
            var installedItems = targetItems.ToList();
            foreach (var slot in mapping.OriginalTargetSlots)
            {
                installedItems[slot] = RareCandyItemId;
            }

            foreach (var occurrence in mapping.LegacyMissingOccurrences.OrderByDescending(occurrence => occurrence.TargetSlot))
            {
                installedItems.Insert(occurrence.TargetSlot, RareCandyItemId);
            }

            installEdits.Add(CreateSetEdit(
                kind,
                hash,
                inventoryIndex,
                targetShopIndex,
                installedItems));
        }

        if (mapping.OwnedReplacementTargetSlots.Count > 0
            || mapping.LegacyMissingOccurrences.Count > 0)
        {
            var uninstalledItems = targetItems.ToList();
            foreach (var slot in mapping.OwnedReplacementTargetSlots)
            {
                uninstalledItems[slot] = RoyalCandyItemId;
            }

            foreach (var occurrence in mapping.LegacyMissingOccurrences.OrderByDescending(occurrence => occurrence.TargetSlot))
            {
                uninstalledItems.Insert(occurrence.TargetSlot, RoyalCandyItemId);
            }

            uninstallEdits.Add(CreateSetEdit(
                kind,
                hash,
                inventoryIndex,
                targetShopIndex,
                uninstalledItems));
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
    private const int RareCandyItemId = 50;

    public static SwShRoyalCandyShopInventoryMapping Analyze(
        IReadOnlyList<int> baseItems,
        IReadOnlyList<int> targetItems)
    {
        ArgumentNullException.ThrowIfNull(baseItems);
        ArgumentNullException.ThrowIfNull(targetItems);

        var prefix = BuildPrefixEditDistances(baseItems, targetItems);
        var suffix = BuildSuffixEditDistances(baseItems, targetItems);
        var totalDistance = prefix[baseItems.Count, targetItems.Count];
        var originalSlots = new List<int>();
        var ownedReplacementSlots = new List<int>();
        var legacyMissingOccurrences = new List<SwShRoyalCandyMissingShopOccurrence>();

        foreach (var (itemId, baseIndex) in baseItems.Select((itemId, index) => (itemId, index)))
        {
            if (itemId != RoyalCandyItemId)
            {
                continue;
            }

            var originalCandidates = Enumerable.Range(0, targetItems.Count)
                .Where(targetIndex => targetItems[targetIndex] == RoyalCandyItemId
                    && prefix[baseIndex, targetIndex]
                        + AlignmentCost.ExactMatch
                        + suffix[baseIndex + 1, targetIndex + 1]
                    == totalDistance)
                .ToArray();
            var replacementCandidates = Enumerable.Range(0, targetItems.Count)
                .Where(targetIndex => targetItems[targetIndex] == RareCandyItemId
                    && prefix[baseIndex, targetIndex]
                        + AlignmentCost.OwnedReplacement
                        + suffix[baseIndex + 1, targetIndex + 1]
                    == totalDistance)
                .ToArray();
            var deletionCandidates = Enumerable.Range(0, targetItems.Count + 1)
                .Where(targetIndex => prefix[baseIndex, targetIndex]
                    + AlignmentCost.StructuralEdit
                    + suffix[baseIndex + 1, targetIndex]
                    == totalDistance)
                .ToArray();

            if (originalCandidates.Length
                    + replacementCandidates.Length
                    + deletionCandidates.Length
                != 1)
            {
                throw new SwShRoyalCandyShopMappingException(
                    $"Base item 1128 shop occurrence at slot {baseIndex} cannot be classified uniquely as original, Royal Candy-owned item 50 replacement, or legacy missing after unrelated inventory edits.");
            }

            if (originalCandidates.Length == 1)
            {
                originalSlots.Add(originalCandidates[0]);
            }
            else if (replacementCandidates.Length == 1)
            {
                ownedReplacementSlots.Add(replacementCandidates[0]);
            }
            else
            {
                legacyMissingOccurrences.Add(new SwShRoyalCandyMissingShopOccurrence(baseIndex, deletionCandidates[0]));
            }
        }

        var mappedTargetSlots = originalSlots
            .Concat(ownedReplacementSlots)
            .ToArray();
        if (mappedTargetSlots.Length != mappedTargetSlots.Distinct().Count())
        {
            throw new SwShRoyalCandyShopMappingException(
                "Multiple base Royal Candy shop occurrences resolve to the same target slot.");
        }

        return new SwShRoyalCandyShopInventoryMapping(
            originalSlots,
            ownedReplacementSlots,
            legacyMissingOccurrences);
    }

    private static AlignmentCost[,] BuildPrefixEditDistances(
        IReadOnlyList<int> baseItems,
        IReadOnlyList<int> targetItems)
    {
        var distances = new AlignmentCost[baseItems.Count + 1, targetItems.Count + 1];
        for (var baseIndex = 0; baseIndex <= baseItems.Count; baseIndex++)
        {
            distances[baseIndex, 0] = AlignmentCost.StructuralEdit * baseIndex;
        }

        for (var targetIndex = 0; targetIndex <= targetItems.Count; targetIndex++)
        {
            distances[0, targetIndex] = AlignmentCost.StructuralEdit * targetIndex;
        }

        for (var baseIndex = 1; baseIndex <= baseItems.Count; baseIndex++)
        {
            for (var targetIndex = 1; targetIndex <= targetItems.Count; targetIndex++)
            {
                var best = AlignmentCost.Min(
                    distances[baseIndex - 1, targetIndex] + AlignmentCost.StructuralEdit,
                    distances[baseIndex, targetIndex - 1] + AlignmentCost.StructuralEdit);
                if (TryGetMatchCost(
                    baseItems[baseIndex - 1],
                    targetItems[targetIndex - 1],
                    out var matchCost))
                {
                    best = AlignmentCost.Min(
                        best,
                        distances[baseIndex - 1, targetIndex - 1] + matchCost);
                }

                distances[baseIndex, targetIndex] = best;
            }
        }

        return distances;
    }

    private static AlignmentCost[,] BuildSuffixEditDistances(
        IReadOnlyList<int> baseItems,
        IReadOnlyList<int> targetItems)
    {
        var distances = new AlignmentCost[baseItems.Count + 1, targetItems.Count + 1];
        for (var baseIndex = baseItems.Count; baseIndex >= 0; baseIndex--)
        {
            distances[baseIndex, targetItems.Count] =
                AlignmentCost.StructuralEdit * (baseItems.Count - baseIndex);
        }

        for (var targetIndex = targetItems.Count; targetIndex >= 0; targetIndex--)
        {
            distances[baseItems.Count, targetIndex] =
                AlignmentCost.StructuralEdit * (targetItems.Count - targetIndex);
        }

        for (var baseIndex = baseItems.Count - 1; baseIndex >= 0; baseIndex--)
        {
            for (var targetIndex = targetItems.Count - 1; targetIndex >= 0; targetIndex--)
            {
                var best = AlignmentCost.Min(
                    distances[baseIndex + 1, targetIndex] + AlignmentCost.StructuralEdit,
                    distances[baseIndex, targetIndex + 1] + AlignmentCost.StructuralEdit);
                if (TryGetMatchCost(
                    baseItems[baseIndex],
                    targetItems[targetIndex],
                    out var matchCost))
                {
                    best = AlignmentCost.Min(
                        best,
                        distances[baseIndex + 1, targetIndex + 1] + matchCost);
                }

                distances[baseIndex, targetIndex] = best;
            }
        }

        return distances;
    }

    private static bool TryGetMatchCost(
        int baseItemId,
        int targetItemId,
        out AlignmentCost cost)
    {
        if (baseItemId == targetItemId)
        {
            cost = AlignmentCost.ExactMatch;
            return true;
        }

        if (baseItemId == RoyalCandyItemId && targetItemId == RareCandyItemId)
        {
            cost = AlignmentCost.OwnedReplacement;
            return true;
        }

        cost = default;
        return false;
    }

    private readonly record struct AlignmentCost(
        int StructuralEdits,
        int OwnedReplacements) : IComparable<AlignmentCost>
    {
        public static AlignmentCost ExactMatch => default;

        public static AlignmentCost OwnedReplacement => new(0, 1);

        public static AlignmentCost StructuralEdit => new(1, 0);

        public static AlignmentCost operator +(AlignmentCost left, AlignmentCost right)
        {
            return new AlignmentCost(
                checked(left.StructuralEdits + right.StructuralEdits),
                checked(left.OwnedReplacements + right.OwnedReplacements));
        }

        public static AlignmentCost operator *(AlignmentCost value, int multiplier)
        {
            return new AlignmentCost(
                checked(value.StructuralEdits * multiplier),
                checked(value.OwnedReplacements * multiplier));
        }

        public static AlignmentCost Min(AlignmentCost first, AlignmentCost second)
        {
            return first.CompareTo(second) <= 0 ? first : second;
        }

        public int CompareTo(AlignmentCost other)
        {
            var structuralComparison = StructuralEdits.CompareTo(other.StructuralEdits);
            return structuralComparison != 0
                ? structuralComparison
                : OwnedReplacements.CompareTo(other.OwnedReplacements);
        }
    }
}

internal sealed record SwShRoyalCandyShopInventoryMapping(
    IReadOnlyList<int> OriginalTargetSlots,
    IReadOnlyList<int> OwnedReplacementTargetSlots,
    IReadOnlyList<SwShRoyalCandyMissingShopOccurrence> LegacyMissingOccurrences)
{
    public IReadOnlyList<int> MatchedTargetSlots => OriginalTargetSlots;

    public IReadOnlyList<SwShRoyalCandyMissingShopOccurrence> MissingOccurrences => LegacyMissingOccurrences;
}

internal sealed record SwShRoyalCandyShopPatchMapping(
    IReadOnlyList<SwShShopInventoryEdit> InstallEdits,
    IReadOnlyList<SwShShopInventoryEdit> UninstallEdits,
    int BaseOccurrences,
    int OriginalOccurrences,
    int OwnedReplacementOccurrences,
    int LegacyMissingOccurrences)
{
    public IReadOnlyList<SwShShopInventoryEdit> RemovalEdits => InstallEdits;

    public IReadOnlyList<SwShShopInventoryEdit> RestoreEdits => UninstallEdits;

    public int MatchedOccurrences => OriginalOccurrences;

    public int MissingOccurrences => LegacyMissingOccurrences;
}

internal sealed record SwShRoyalCandyMissingShopOccurrence(int BaseSlot, int TargetSlot);

internal sealed class SwShRoyalCandyShopMappingException(string message) : IOException(message);
