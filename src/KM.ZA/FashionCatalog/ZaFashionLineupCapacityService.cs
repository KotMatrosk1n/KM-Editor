// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;

namespace KM.ZA.FashionCatalog;

public static class ZaFashionLineupCapacityReasonCodes
{
    public const string ItemIdentityAllocationUnproven = "item-identity-allocation-unproven";
    public const string ExternalItemConsumerOwnershipUnproven = "external-item-consumer-ownership-unproven";
    public const string LineupReferenceAllocationUnproven = "lineup-reference-allocation-unproven";
    public const string DisplayOrderAllocationUnproven = "display-order-allocation-unproven";
    public const string RuntimeMenuCapacityUnverified = "runtime-menu-capacity-unverified";
    public const string RuntimeCursorCapacityUnverified = "runtime-cursor-capacity-unverified";
    public const string RuntimePurchaseCapacityUnverified = "runtime-purchase-capacity-unverified";
    public const string RuntimeReopenCapacityUnverified = "runtime-reopen-capacity-unverified";
    public const string RuntimeSaveCapacityUnverified = "runtime-save-capacity-unverified";
}

public sealed record ZaFashionLineupStructuralPlan(
    string PlanRevision,
    string SourceRevision,
    string CatalogSourceRevision,
    string LineupSourceRevision,
    string CatalogStructureRevision,
    string LineupStructureRevision,
    int CatalogSourceByteLength,
    int CatalogRebuiltByteLength,
    int LineupSourceByteLength,
    int LineupRebuiltByteLength,
    int LineupCount,
    int EntryCount,
    bool IsNoOp,
    bool OutputAuthorized);

public sealed record ZaFashionLineupCapacityAssessment(
    ZaFashionCatalogFile CatalogFile,
    string SourceRevision,
    int CatalogPhysicalRowCount,
    int LineupCount,
    int LineupEntryCount,
    int MaximumEntriesInSingleLineup,
    int VectorLengthEncodingBits,
    bool TotalEntryCountExceedsByteRange,
    bool ALineupExceedsByteRange,
    bool CompleteRebuildReparseVerified,
    bool StablePhysicalIdentitiesVerified,
    bool ActivationConditionsPreserved,
    bool AllKnownLineupFieldsPreserved,
    bool AllKnownCatalogFieldsPreserved,
    bool CatalogDisplayOrderIsOneThroughCount,
    bool CanChangeCapacity,
    ZaFashionLineupStructuralPlan StructuralPlan,
    IReadOnlyList<string> BlockingReasons);

public sealed class ZaFashionLineupCapacityService
{
    private static readonly IReadOnlyList<string> CapacityBlockingReasons = Array.AsReadOnly(
    new[]
    {
        ZaFashionLineupCapacityReasonCodes.ItemIdentityAllocationUnproven,
        ZaFashionLineupCapacityReasonCodes.ExternalItemConsumerOwnershipUnproven,
        ZaFashionLineupCapacityReasonCodes.LineupReferenceAllocationUnproven,
        ZaFashionLineupCapacityReasonCodes.DisplayOrderAllocationUnproven,
        ZaFashionLineupCapacityReasonCodes.RuntimeMenuCapacityUnverified,
        ZaFashionLineupCapacityReasonCodes.RuntimeCursorCapacityUnverified,
        ZaFashionLineupCapacityReasonCodes.RuntimePurchaseCapacityUnverified,
        ZaFashionLineupCapacityReasonCodes.RuntimeReopenCapacityUnverified,
        ZaFashionLineupCapacityReasonCodes.RuntimeSaveCapacityUnverified,
    });

    private readonly ZaFashionCatalogService catalogService = new();

    public ZaFashionLineupCapacityAssessment AssessCurrentSource(
        ZaFashionCatalogSourceSet sources,
        ZaFashionCatalogFile catalogFile)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (catalogFile is not ZaFashionCatalogFile.DressUpLineups
            and not ZaFashionCatalogFile.HairAndMakeupLineups)
        {
            throw new InvalidDataException(
                "A Fashion Catalog capacity assessment requires a shop-lineup source.");
        }

        var inputSources = sources;
        var sourceRevision = CreateSourceRevision(inputSources);
        sources = CloneSources(inputSources);
        if (!string.Equals(sourceRevision, CreateSourceRevision(inputSources), StringComparison.Ordinal)
            || !string.Equals(sourceRevision, CreateSourceRevision(sources), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Fashion Catalog source changed while it was being captured for capacity assessment.");
        }

        var snapshot = catalogService.CreateSnapshot(sources);
        if (!string.Equals(sourceRevision, snapshot.SourceRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Fashion Catalog source changed while its capacity was being assessed.");
        }

        var shopsByLineup = ZaFashionLineupCatalogDocument.ReadShopRelationships(
            sources.FashionShops);
        EnsureUniqueShopIdentities(shopsByLineup);
        var isDressUp = catalogFile == ZaFashionCatalogFile.DressUpLineups;
        var lineupSourceBytes = isDressUp
            ? sources.DressUpLineups
            : sources.HairAndMakeupLineups;
        var catalogSourceBytes = isDressUp
            ? sources.DressUpItems
            : sources.HairAndMakeup;
        var label = isDressUp ? "Dress-up shop lineups" : "Hair and makeup shop lineups";
        var document = ZaFashionLineupCatalogDocument.Parse(
            lineupSourceBytes,
            shopsByLineup,
            label);
        EnsureUniquePhysicalIdentities(document, label);

        var rebuiltLineupBytes = document.Write();
        var rebuilt = ZaFashionLineupCatalogDocument.Parse(
            rebuiltLineupBytes,
            shopsByLineup,
            label);
        EnsureUniquePhysicalIdentities(rebuilt, label);
        var catalogProof = isDressUp
            ? RebuildDressUpCatalog(catalogSourceBytes)
            : RebuildHairAndMakeupCatalog(catalogSourceBytes);
        var rebuiltSources = isDressUp
            ? sources with
            {
                DressUpItems = catalogProof.RebuiltBytes,
                DressUpLineups = rebuiltLineupBytes,
            }
            : sources with
            {
                HairAndMakeup = catalogProof.RebuiltBytes,
                HairAndMakeupLineups = rebuiltLineupBytes,
            };
        _ = catalogService.CreateSnapshot(rebuiltSources);

        var stableIdentities = HasSamePhysicalIdentities(document, rebuilt)
            && string.Equals(
                document.CreateIdentityRevision(),
                rebuilt.CreateIdentityRevision(),
                StringComparison.Ordinal);
        var activationConditionsPreserved = string.Equals(
            document.CreateActivationConditionRevision(),
            rebuilt.CreateActivationConditionRevision(),
            StringComparison.Ordinal);
        var structurePreserved = string.Equals(
            document.CreateStructureRevision(),
            rebuilt.CreateStructureRevision(),
            StringComparison.Ordinal);
        if (!stableIdentities || !activationConditionsPreserved || !structurePreserved)
        {
            throw new InvalidDataException(
                "The Fashion Catalog lineup did not survive a complete rebuild and reparse; no capacity plan was returned.");
        }

        if (!string.Equals(sourceRevision, CreateSourceRevision(sources), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Fashion Catalog source changed while its capacity was being assessed.");
        }

        var maximumEntries = document.Lineups.Count == 0
            ? 0
            : document.Lineups.Max(lineup => lineup.Entries.Count);
        var catalogSourceRevision = ZaFashionCatalogFlatBufferSupport.HashBytes(catalogSourceBytes);
        var lineupSourceRevision = ZaFashionCatalogFlatBufferSupport.HashBytes(lineupSourceBytes);
        var lineupStructureRevision = document.CreateStructureRevision();
        var planRevision = CreatePlanRevision(
            sourceRevision,
            catalogSourceRevision,
            lineupSourceRevision,
            catalogProof.StructureRevision,
            lineupStructureRevision,
            catalogSourceBytes.Length,
            catalogProof.RebuiltBytes.Length,
            lineupSourceBytes.Length,
            rebuiltLineupBytes.Length,
            document.Lineups.Count,
            document.Rows.Count);
        var plan = new ZaFashionLineupStructuralPlan(
            planRevision,
            sourceRevision,
            catalogSourceRevision,
            lineupSourceRevision,
            catalogProof.StructureRevision,
            lineupStructureRevision,
            catalogSourceBytes.Length,
            catalogProof.RebuiltBytes.Length,
            lineupSourceBytes.Length,
            rebuiltLineupBytes.Length,
            document.Lineups.Count,
            document.Rows.Count,
            IsNoOp: true,
            OutputAuthorized: false);

        if (!string.Equals(
                sourceRevision,
                CreateSourceRevision(inputSources),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Fashion Catalog source changed while its capacity was being assessed.");
        }

        return new ZaFashionLineupCapacityAssessment(
            catalogFile,
            sourceRevision,
            catalogProof.RowCount,
            document.Lineups.Count,
            document.Rows.Count,
            maximumEntries,
            VectorLengthEncodingBits: sizeof(uint) * 8,
            TotalEntryCountExceedsByteRange: document.Rows.Count > byte.MaxValue,
            ALineupExceedsByteRange: maximumEntries > byte.MaxValue,
            CompleteRebuildReparseVerified: true,
            StablePhysicalIdentitiesVerified: true,
            ActivationConditionsPreserved: true,
            AllKnownLineupFieldsPreserved: true,
            AllKnownCatalogFieldsPreserved: true,
            catalogProof.DisplayOrderIsOneThroughCount,
            CanChangeCapacity: false,
            plan,
            CapacityBlockingReasons);
    }

    private static ZaFashionCatalogSourceSet CloneSources(ZaFashionCatalogSourceSet sources)
    {
        ArgumentNullException.ThrowIfNull(sources.DressUpItems);
        ArgumentNullException.ThrowIfNull(sources.DressUpGroups);
        ArgumentNullException.ThrowIfNull(sources.HairAndMakeup);
        ArgumentNullException.ThrowIfNull(sources.FashionShops);
        ArgumentNullException.ThrowIfNull(sources.DressUpLineups);
        ArgumentNullException.ThrowIfNull(sources.HairAndMakeupLineups);
        return new ZaFashionCatalogSourceSet(
            sources.DressUpItems.ToArray(),
            sources.DressUpGroups.ToArray(),
            sources.HairAndMakeup.ToArray(),
            sources.FashionShops.ToArray(),
            sources.DressUpLineups.ToArray(),
            sources.HairAndMakeupLineups.ToArray());
    }

    private static CatalogRebuildProof RebuildDressUpCatalog(byte[] sourceBytes)
    {
        var document = ZaDressUpCatalogDocument.Parse(sourceBytes);
        EnsureUniqueCatalogItemIdentities(
            document.Rows.Select(row => row.ItemId),
            "Dress-up item catalog");
        var structureRevision = CreateCatalogStructureRevision(
            "dress-up-items",
            document.Rows.Select(row => row.CreateRevision()));
        var rebuiltBytes = document.Write();
        var rebuilt = ZaDressUpCatalogDocument.Parse(rebuiltBytes);
        EnsureUniqueCatalogItemIdentities(
            rebuilt.Rows.Select(row => row.ItemId),
            "Rebuilt dress-up item catalog");
        var rebuiltRevision = CreateCatalogStructureRevision(
            "dress-up-items",
            rebuilt.Rows.Select(row => row.CreateRevision()));
        if (!document.Rows.SequenceEqual(rebuilt.Rows)
            || !string.Equals(structureRevision, rebuiltRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The dress-up item catalog did not survive a complete rebuild and reparse; no capacity plan was returned.");
        }

        var displayOrderIsOneThroughCount = IsOneThroughCount(
            document.Rows,
            static row => row.HasDisplayOrder,
            static row => row.DisplayOrder);
        if (displayOrderIsOneThroughCount != IsOneThroughCount(
                rebuilt.Rows,
                static row => row.HasDisplayOrder,
                static row => row.DisplayOrder))
        {
            throw new InvalidDataException(
                "The dress-up item display-order structure changed during rebuild; no capacity plan was returned.");
        }

        return new CatalogRebuildProof(
            rebuiltBytes,
            structureRevision,
            document.Rows.Count,
            displayOrderIsOneThroughCount);
    }

    private static CatalogRebuildProof RebuildHairAndMakeupCatalog(byte[] sourceBytes)
    {
        var document = ZaHairAndMakeupCatalogDocument.Parse(sourceBytes);
        EnsureUniqueCatalogItemIdentities(
            document.Rows.Select(row => row.ItemId),
            "Hair and makeup catalog");
        var structureRevision = CreateCatalogStructureRevision(
            "hair-and-makeup",
            document.Rows.Select(row => row.CreateRevision()));
        var rebuiltBytes = document.Write();
        var rebuilt = ZaHairAndMakeupCatalogDocument.Parse(rebuiltBytes);
        EnsureUniqueCatalogItemIdentities(
            rebuilt.Rows.Select(row => row.ItemId),
            "Rebuilt hair and makeup catalog");
        var rebuiltRevision = CreateCatalogStructureRevision(
            "hair-and-makeup",
            rebuilt.Rows.Select(row => row.CreateRevision()));
        if (!document.Rows.SequenceEqual(rebuilt.Rows)
            || !string.Equals(structureRevision, rebuiltRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The hair and makeup catalog did not survive a complete rebuild and reparse; no capacity plan was returned.");
        }

        var displayOrderIsOneThroughCount = IsOneThroughCount(
            document.Rows,
            static row => row.HasDisplayOrder,
            static row => row.DisplayOrder);
        if (displayOrderIsOneThroughCount != IsOneThroughCount(
                rebuilt.Rows,
                static row => row.HasDisplayOrder,
                static row => row.DisplayOrder))
        {
            throw new InvalidDataException(
                "The hair and makeup display-order structure changed during rebuild; no capacity plan was returned.");
        }

        return new CatalogRebuildProof(
            rebuiltBytes,
            structureRevision,
            document.Rows.Count,
            displayOrderIsOneThroughCount);
    }

    private static string CreateCatalogStructureRevision(
        string catalogKind,
        IEnumerable<string> rowRevisions)
    {
        var revisions = rowRevisions.ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ZaFashionCatalogFlatBufferSupport.Append(
            hash,
            "KM.ZA.FashionCatalog.CapacityCatalogStructure.v1");
        ZaFashionCatalogFlatBufferSupport.Append(hash, catalogKind);
        ZaFashionCatalogFlatBufferSupport.Append(hash, revisions.Length);
        for (var index = 0; index < revisions.Length; index++)
        {
            ZaFashionCatalogFlatBufferSupport.Append(hash, index);
            ZaFashionCatalogFlatBufferSupport.Append(hash, revisions[index]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void EnsureUniquePhysicalIdentities(
        ZaFashionLineupCatalogDocument document,
        string label)
    {
        var lineupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lineup in document.Lineups)
        {
            if (!lineupIds.Add(lineup.LineupId))
            {
                throw new InvalidDataException(
                    $"{label} contains duplicate lineup ID '{lineup.LineupId}', so its references are ambiguous.");
            }
        }

        var rowIdentities = new HashSet<(int Lineup, int Entry)>();
        foreach (var row in document.Rows)
        {
            if (!rowIdentities.Add((row.LineupPhysicalIndex, row.EntryPhysicalIndex)))
            {
                throw new InvalidDataException(
                    $"{label} contains a duplicate physical lineup-entry identity.");
            }
        }
    }

    private static void EnsureUniqueShopIdentities(
        IReadOnlyDictionary<string, IReadOnlyList<string>> shopsByLineup)
    {
        var shopIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in shopsByLineup)
        {
            foreach (var shopId in relationship.Value)
            {
                if (!shopIds.Add(shopId))
                {
                    throw new InvalidDataException(
                        $"Fashion shop ID '{shopId}' has more than one physical relationship, so lineup ownership is ambiguous.");
                }
            }
        }
    }

    private static void EnsureUniqueCatalogItemIdentities(
        IEnumerable<uint> itemIds,
        string label)
    {
        var identities = new HashSet<uint>();
        foreach (var itemId in itemIds)
        {
            if (!identities.Add(itemId))
            {
                throw new InvalidDataException(
                    $"{label} contains duplicate item ID {itemId}, so lineup references are ambiguous.");
            }
        }
    }

    private static bool HasSamePhysicalIdentities(
        ZaFashionLineupCatalogDocument before,
        ZaFashionLineupCatalogDocument after)
    {
        if (before.Rows.Count != after.Rows.Count || before.Lineups.Count != after.Lineups.Count)
        {
            return false;
        }

        for (var index = 0; index < before.Rows.Count; index++)
        {
            var left = before.Rows[index];
            var right = after.Rows[index];
            if (left.PhysicalIndex != right.PhysicalIndex
                || left.LineupPhysicalIndex != right.LineupPhysicalIndex
                || left.EntryPhysicalIndex != right.EntryPhysicalIndex
                || !string.Equals(left.LineupId, right.LineupId, StringComparison.Ordinal)
                || left.ItemId != right.ItemId
                || !left.ShopIds.SequenceEqual(right.ShopIds, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOneThroughCount<T>(
        IReadOnlyList<T> rows,
        Func<T, bool> hasDisplayOrder,
        Func<T, uint> getDisplayOrder)
    {
        if (rows.Count == 0 || rows.Any(row => !hasDisplayOrder(row)))
        {
            return false;
        }

        var orders = rows.Select(getDisplayOrder).Order().ToArray();
        for (var index = 0; index < orders.Length; index++)
        {
            if (orders[index] != checked((uint)index + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static string CreatePlanRevision(
        string sourceRevision,
        string catalogSourceRevision,
        string lineupSourceRevision,
        string catalogStructureRevision,
        string lineupStructureRevision,
        int catalogSourceByteLength,
        int catalogRebuiltByteLength,
        int lineupSourceByteLength,
        int lineupRebuiltByteLength,
        int lineupCount,
        int entryCount)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ZaFashionCatalogFlatBufferSupport.Append(
            hash,
            "KM.ZA.FashionCatalog.LineupCapacityPlan.v1");
        ZaFashionCatalogFlatBufferSupport.Append(hash, sourceRevision);
        ZaFashionCatalogFlatBufferSupport.Append(hash, catalogSourceRevision);
        ZaFashionCatalogFlatBufferSupport.Append(hash, lineupSourceRevision);
        ZaFashionCatalogFlatBufferSupport.Append(hash, catalogStructureRevision);
        ZaFashionCatalogFlatBufferSupport.Append(hash, lineupStructureRevision);
        ZaFashionCatalogFlatBufferSupport.Append(hash, catalogSourceByteLength);
        ZaFashionCatalogFlatBufferSupport.Append(hash, catalogRebuiltByteLength);
        ZaFashionCatalogFlatBufferSupport.Append(hash, lineupSourceByteLength);
        ZaFashionCatalogFlatBufferSupport.Append(hash, lineupRebuiltByteLength);
        ZaFashionCatalogFlatBufferSupport.Append(hash, lineupCount);
        ZaFashionCatalogFlatBufferSupport.Append(hash, entryCount);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string CreateSourceRevision(ZaFashionCatalogSourceSet sources) =>
        ZaFashionCatalogFlatBufferSupport.CreateSourceRevision(
            sources.DressUpItems,
            sources.DressUpGroups,
            sources.HairAndMakeup,
            sources.FashionShops,
            sources.DressUpLineups,
            sources.HairAndMakeupLineups);

    private sealed record CatalogRebuildProof(
        byte[] RebuiltBytes,
        string StructureRevision,
        int RowCount,
        bool DisplayOrderIsOneThroughCount);
}
