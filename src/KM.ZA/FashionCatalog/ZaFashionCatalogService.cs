// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.ZA.FashionCatalog;

public sealed class ZaFashionCatalogService
{
    private const string DressUpItemRowPrefix = "dress-up-item:";
    private const string DressUpGroupRowPrefix = "dress-up-group:";
    private const string HairAndMakeupRowPrefix = "hair-and-makeup:";
    private const string DressUpLineupRowPrefix = "dress-up-lineup-entry:";
    private const string HairAndMakeupLineupRowPrefix = "hair-and-makeup-lineup-entry:";

    public ZaFashionCatalogSnapshot CreateSnapshot(ZaFashionCatalogSourceSet sources)
    {
        var state = ParseSources(sources);
        return CreateSnapshot(sources, state);
    }

    public ZaFashionCatalogEditResult UpdateDressUpItem(
        ZaFashionCatalogSourceSet sources,
        ZaFashionCatalogRowBinding binding,
        ZaDressUpItemPatch patch)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(patch);
        var state = ParseSources(sources);
        var sourceRevision = CreateSourceRevision(sources);
        var row = ResolveBoundRow(
            state.DressUpItems.Rows,
            binding,
            sourceRevision,
            DressUpItemRowPrefix,
            static value => value.CreateRevision(),
            "dress-up item");
        EnsureHasDressUpItemChange(patch);
        ValidateCatalogItemIdentityChange(
            state.DressUpItems.Rows.Select(candidate => candidate.ItemId),
            state.DressUpLineups.Rows,
            row.ItemId,
            patch.ItemId,
            "dress-up item");
        ValidateDressUpItemReferences(state, patch);

        var updated = row with
        {
            HasItemId = patch.ItemId is not null || row.HasItemId,
            ItemId = patch.ItemId ?? row.ItemId,
            HasModelPart = patch.ModelPart is not null || row.HasModelPart,
            ModelPart = patch.ModelPart is null
                ? row.ModelPart
                : ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    patch.ModelPart,
                    "Dress-up item model part"),
            HasCatalogGroupCode = patch.CatalogGroupCode is not null || row.HasCatalogGroupCode,
            CatalogGroupCode = patch.CatalogGroupCode ?? row.CatalogGroupCode,
            HasModelVariant = patch.ModelVariant is not null || row.HasModelVariant,
            ModelVariant = patch.ModelVariant is null
                ? row.ModelVariant
                : ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    patch.ModelVariant,
                    "Dress-up item model variant"),
            HasCategoryCode = patch.CategoryCode is not null || row.HasCategoryCode,
            CategoryCode = patch.CategoryCode ?? row.CategoryCode,
            HasColorVariantCode = patch.ColorVariantCode is not null || row.HasColorVariantCode,
            ColorVariantCode = patch.ColorVariantCode ?? row.ColorVariantCode,
            HasPrimaryColorLabel = patch.PrimaryColorLabel is not null || row.HasPrimaryColorLabel,
            PrimaryColorLabel = patch.PrimaryColorLabel is null
                ? row.PrimaryColorLabel
                : ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    patch.PrimaryColorLabel,
                    "Dress-up item primary color label"),
            HasSecondaryColorLabel = patch.SecondaryColorLabel is not null || row.HasSecondaryColorLabel,
            SecondaryColorLabel = patch.SecondaryColorLabel is null
                ? row.SecondaryColorLabel
                : ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    patch.SecondaryColorLabel,
                    "Dress-up item secondary color label"),
            HasDisplayOrder = patch.DisplayOrder is not null || row.HasDisplayOrder,
            DisplayOrder = patch.DisplayOrder ?? row.DisplayOrder,
            HasVariantOrder = patch.VariantOrder is not null || row.HasVariantOrder,
            VariantOrder = patch.VariantOrder ?? row.VariantOrder,
        };
        EnsureChanged(row, updated, "dress-up item");

        var updatedDocument = state.DressUpItems.Replace(binding.PhysicalIndex, updated);
        var updatedBytes = updatedDocument.Write();
        var reparsed = ZaDressUpCatalogDocument.Parse(updatedBytes);
        EnsureDressUpPreserved(state.DressUpItems, reparsed, binding.PhysicalIndex);
        var updatedSources = sources with { DressUpItems = updatedBytes };
        return new ZaFashionCatalogEditResult(
            ZaFashionCatalogFile.DressUpItems,
            updatedSources,
            CreateSnapshot(updatedSources));
    }

    public ZaFashionCatalogEditResult UpdateDressUpGroup(
        ZaFashionCatalogSourceSet sources,
        ZaFashionCatalogRowBinding binding,
        ZaDressUpGroupPatch patch)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(patch);
        var state = ParseSources(sources);
        var sourceRevision = CreateSourceRevision(sources);
        var row = ResolveBoundRow(
            state.DressUpGroups.Rows,
            binding,
            sourceRevision,
            DressUpGroupRowPrefix,
            static value => value.CreateRevision(),
            "dress-up group");
        if (patch.ModelPart is null && patch.DisplayOrder is null && patch.DisplayLabel is null)
        {
            throw new InvalidDataException("The dress-up group patch contains no changes.");
        }

        ValidateDressUpGroupReferences(state, patch);

        var updated = row with
        {
            HasModelPart = patch.ModelPart is not null || row.HasModelPart,
            ModelPart = patch.ModelPart is null
                ? row.ModelPart
                : ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    patch.ModelPart,
                    "Dress-up group model part"),
            HasDisplayOrder = patch.DisplayOrder is not null || row.HasDisplayOrder,
            DisplayOrder = patch.DisplayOrder ?? row.DisplayOrder,
            HasDisplayLabel = patch.DisplayLabel is not null || row.HasDisplayLabel,
            DisplayLabel = patch.DisplayLabel is null
                ? row.DisplayLabel
                : ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    patch.DisplayLabel,
                    "Dress-up group display label"),
        };
        EnsureChanged(row, updated, "dress-up group");

        var updatedDocument = state.DressUpGroups.Replace(binding.PhysicalIndex, updated);
        var updatedBytes = updatedDocument.Write();
        var reparsed = ZaDressUpGroupCatalogDocument.Parse(updatedBytes);
        EnsureRowsPreserved(
            state.DressUpGroups.Rows,
            reparsed.Rows,
            binding.PhysicalIndex,
            "dress-up group");
        var updatedSources = sources with { DressUpGroups = updatedBytes };
        return new ZaFashionCatalogEditResult(
            ZaFashionCatalogFile.DressUpGroups,
            updatedSources,
            CreateSnapshot(updatedSources));
    }

    public ZaFashionCatalogEditResult UpdateHairAndMakeup(
        ZaFashionCatalogSourceSet sources,
        ZaFashionCatalogRowBinding binding,
        ZaHairAndMakeupPatch patch)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(patch);
        ValidateOptionalEdit(patch.ColorValue, "Hair and makeup color value");
        ValidateOptionalEdit(patch.LabelKey, "Hair and makeup label key");
        var state = ParseSources(sources);
        var sourceRevision = CreateSourceRevision(sources);
        var row = ResolveBoundRow(
            state.HairAndMakeup.Rows,
            binding,
            sourceRevision,
            HairAndMakeupRowPrefix,
            static value => value.CreateRevision(),
            "hair and makeup");
        if (patch.ItemId is null
            && patch.ModelKey is null
            && patch.CatalogTypeCode is null
            && patch.ColorValue is null
            && patch.LabelKey is null
            && patch.DisplayOrder is null
            && patch.GroupCode is null
            && patch.VariantCode is null)
        {
            throw new InvalidDataException("The hair and makeup patch contains no changes.");
        }

        ValidateCatalogItemIdentityChange(
            state.HairAndMakeup.Rows.Select(candidate => candidate.ItemId),
            state.HairAndMakeupLineups.Rows,
            row.ItemId,
            patch.ItemId,
            "hair and makeup item");
        ValidateHairAndMakeupReferences(state, patch);

        var updated = row with
        {
            HasItemId = patch.ItemId is not null || row.HasItemId,
            ItemId = patch.ItemId ?? row.ItemId,
            HasModelKey = patch.ModelKey is not null || row.HasModelKey,
            ModelKey = patch.ModelKey is null
                ? row.ModelKey
                : ZaFashionCatalogFlatBufferSupport.ValidateRequiredText(
                    patch.ModelKey,
                    "Hair and makeup model key"),
            HasCatalogTypeCode = patch.CatalogTypeCode is not null || row.HasCatalogTypeCode,
            CatalogTypeCode = patch.CatalogTypeCode ?? row.CatalogTypeCode,
            HasColorValue = patch.ColorValue?.IsSpecified == true || row.HasColorValue,
            ColorValue = patch.ColorValue?.IsSpecified == true
                ? ZaFashionCatalogFlatBufferSupport.ValidateOptionalNonEmptyText(
                    patch.ColorValue.Value,
                    "Hair and makeup color value")
                : row.ColorValue,
            HasLabelKey = patch.LabelKey?.IsSpecified == true || row.HasLabelKey,
            LabelKey = patch.LabelKey?.IsSpecified == true
                ? ZaFashionCatalogFlatBufferSupport.ValidateOptionalNonEmptyText(
                    patch.LabelKey.Value,
                    "Hair and makeup label key")
                : row.LabelKey,
            HasDisplayOrder = patch.DisplayOrder is not null || row.HasDisplayOrder,
            DisplayOrder = patch.DisplayOrder ?? row.DisplayOrder,
            HasGroupCode = patch.GroupCode is not null || row.HasGroupCode,
            GroupCode = patch.GroupCode ?? row.GroupCode,
            HasVariantCode = patch.VariantCode is not null || row.HasVariantCode,
            VariantCode = patch.VariantCode ?? row.VariantCode,
        };

        if (patch.ColorValue?.IsSpecified == true && patch.ColorValue.Value is null)
        {
            updated = updated with { HasColorValue = false, ColorValue = null };
        }

        if (patch.LabelKey?.IsSpecified == true && patch.LabelKey.Value is null)
        {
            updated = updated with { HasLabelKey = false, LabelKey = null };
        }

        EnsureChanged(row, updated, "hair and makeup");
        var updatedDocument = state.HairAndMakeup.Replace(binding.PhysicalIndex, updated);
        var updatedBytes = updatedDocument.Write();
        var reparsed = ZaHairAndMakeupCatalogDocument.Parse(updatedBytes);
        EnsureHairAndMakeupPreserved(state.HairAndMakeup, reparsed, binding.PhysicalIndex);
        var updatedSources = sources with { HairAndMakeup = updatedBytes };
        return new ZaFashionCatalogEditResult(
            ZaFashionCatalogFile.HairAndMakeup,
            updatedSources,
            CreateSnapshot(updatedSources));
    }

    public ZaFashionCatalogEditResult UpdateLineupEntry(
        ZaFashionCatalogSourceSet sources,
        ZaFashionCatalogFile catalogFile,
        ZaFashionCatalogRowBinding binding,
        ZaFashionLineupEntryPatch patch)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(patch);
        if (catalogFile is not ZaFashionCatalogFile.DressUpLineups
            and not ZaFashionCatalogFile.HairAndMakeupLineups)
        {
            throw new InvalidDataException("The Fashion Catalog source is not an editable shop lineup.");
        }

        var state = ParseSources(sources);
        var sourceRevision = CreateSourceRevision(sources);
        var isDressUp = catalogFile == ZaFashionCatalogFile.DressUpLineups;
        var document = isDressUp ? state.DressUpLineups : state.HairAndMakeupLineups;
        var sourceBytes = isDressUp ? sources.DressUpLineups : sources.HairAndMakeupLineups;
        var sourceHash = ZaFashionCatalogFlatBufferSupport.HashBytes(sourceBytes);
        var rowPrefix = isDressUp ? DressUpLineupRowPrefix : HairAndMakeupLineupRowPrefix;
        var label = isDressUp ? "dress-up shop lineup entry" : "hair and makeup shop lineup entry";
        var row = ResolveBoundRow(
            document.Rows,
            binding,
            sourceRevision,
            rowPrefix,
            value => CreateLineupRowRevision(sourceHash, rowPrefix, value),
            label);
        var knownItem = isDressUp
            ? state.DressUpItems.Rows.Any(candidate => candidate.ItemId == patch.ItemId)
            : state.HairAndMakeup.Rows.Any(candidate => candidate.ItemId == patch.ItemId);
        if (!knownItem)
        {
            throw new InvalidDataException(
                $"The {label} item ID is not present in the exact loaded Z-A catalog options.");
        }

        if (row.ItemId == patch.ItemId)
        {
            throw new InvalidDataException($"The {label} patch does not change the selected row.");
        }

        var updatedBytes = document.ReplaceItem(binding.PhysicalIndex, patch.ItemId);
        var updatedSources = isDressUp
            ? sources with { DressUpLineups = updatedBytes }
            : sources with { HairAndMakeupLineups = updatedBytes };
        var snapshot = CreateSnapshot(updatedSources);
        var updatedRows = isDressUp ? snapshot.DressUpLineups : snapshot.HairAndMakeupLineups;
        if (updatedRows.Count != document.Rows.Count
            || updatedRows[binding.PhysicalIndex].ItemId != patch.ItemId)
        {
            throw new InvalidDataException(
                $"The {label} rewrite did not preserve the fixed physical structure.");
        }

        return new ZaFashionCatalogEditResult(catalogFile, updatedSources, snapshot);
    }

    private static ParsedCatalogSources ParseSources(ZaFashionCatalogSourceSet sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sources.DressUpItems);
        ArgumentNullException.ThrowIfNull(sources.DressUpGroups);
        ArgumentNullException.ThrowIfNull(sources.HairAndMakeup);
        ArgumentNullException.ThrowIfNull(sources.FashionShops);
        ArgumentNullException.ThrowIfNull(sources.DressUpLineups);
        ArgumentNullException.ThrowIfNull(sources.HairAndMakeupLineups);
        var dressUpItems = ZaDressUpCatalogDocument.Parse(sources.DressUpItems);
        var dressUpGroups = ZaDressUpGroupCatalogDocument.Parse(sources.DressUpGroups);
        var hairAndMakeup = ZaHairAndMakeupCatalogDocument.Parse(sources.HairAndMakeup);
        var shopRelationships = ZaFashionLineupCatalogDocument.ReadShopRelationships(
            sources.FashionShops);
        var dressUpLineups = ZaFashionLineupCatalogDocument.Parse(
            sources.DressUpLineups,
            shopRelationships,
            "Dress-up shop lineups");
        var hairAndMakeupLineups = ZaFashionLineupCatalogDocument.Parse(
            sources.HairAndMakeupLineups,
            shopRelationships,
            "Hair and makeup shop lineups");
        ValidateSourceRelationships(
            dressUpItems,
            hairAndMakeup,
            shopRelationships,
            dressUpLineups,
            hairAndMakeupLineups);
        return new ParsedCatalogSources(
            dressUpItems,
            dressUpGroups,
            hairAndMakeup,
            dressUpLineups,
            hairAndMakeupLineups);
    }

    private static ZaFashionCatalogSnapshot CreateSnapshot(
        ZaFashionCatalogSourceSet sources,
        ParsedCatalogSources state)
    {
        var sourceRevision = CreateSourceRevision(sources);
        var dressUpItems = state.DressUpItems.Rows
            .Select((row, index) => new ZaDressUpItemRecord(
                index,
                CreatePhysicalRowId(DressUpItemRowPrefix, index),
                row.CreateRevision(),
                row.ItemId,
                row.ModelPart!,
                row.CatalogGroupCode,
                row.ModelVariant!,
                row.CategoryCode,
                row.ColorVariantCode,
                row.PrimaryColorLabel!,
                row.SecondaryColorLabel!,
                row.DisplayOrder,
                row.VariantOrder))
            .ToArray();
        var dressUpGroups = state.DressUpGroups.Rows
            .Select((row, index) => new ZaDressUpGroupRecord(
                index,
                CreatePhysicalRowId(DressUpGroupRowPrefix, index),
                row.CreateRevision(),
                row.ModelPart!,
                row.DisplayOrder,
                row.DisplayLabel!))
            .ToArray();
        var hairAndMakeup = state.HairAndMakeup.Rows
            .Select((row, index) => new ZaHairAndMakeupRecord(
                index,
                CreatePhysicalRowId(HairAndMakeupRowPrefix, index),
                row.CreateRevision(),
                row.ItemId,
                row.ModelKey!,
                row.CatalogTypeCode,
                row.ColorValue,
                row.LabelKey,
                row.DisplayOrder,
                row.GroupCode,
                row.VariantCode))
            .ToArray();
        var dressUpLineupsRevision = ZaFashionCatalogFlatBufferSupport.HashBytes(sources.DressUpLineups);
        var dressUpLineups = state.DressUpLineups.Rows
            .Select((row, index) => new ZaFashionLineupEntryRecord(
                index,
                CreatePhysicalRowId(DressUpLineupRowPrefix, index),
                CreateLineupRowRevision(dressUpLineupsRevision, DressUpLineupRowPrefix, row),
                row.LineupPhysicalIndex,
                row.EntryPhysicalIndex,
                row.LineupId,
                row.ShopIds,
                row.ItemId))
            .ToArray();
        var hairAndMakeupLineupsRevision = ZaFashionCatalogFlatBufferSupport.HashBytes(
            sources.HairAndMakeupLineups);
        var hairAndMakeupLineups = state.HairAndMakeupLineups.Rows
            .Select((row, index) => new ZaFashionLineupEntryRecord(
                index,
                CreatePhysicalRowId(HairAndMakeupLineupRowPrefix, index),
                CreateLineupRowRevision(
                    hairAndMakeupLineupsRevision,
                    HairAndMakeupLineupRowPrefix,
                    row),
                row.LineupPhysicalIndex,
                row.EntryPhysicalIndex,
                row.LineupId,
                row.ShopIds,
                row.ItemId))
            .ToArray();
        return new ZaFashionCatalogSnapshot(
            sourceRevision,
            ZaFashionCatalogFlatBufferSupport.HashBytes(sources.DressUpItems),
            ZaFashionCatalogFlatBufferSupport.HashBytes(sources.DressUpGroups),
            ZaFashionCatalogFlatBufferSupport.HashBytes(sources.HairAndMakeup),
            ZaFashionCatalogFlatBufferSupport.HashBytes(sources.FashionShops),
            dressUpLineupsRevision,
            hairAndMakeupLineupsRevision,
            dressUpItems,
            dressUpGroups,
            hairAndMakeup,
            dressUpLineups,
            hairAndMakeupLineups);
    }

    private static string CreateSourceRevision(ZaFashionCatalogSourceSet sources) =>
        ZaFashionCatalogFlatBufferSupport.CreateSourceRevision(
            sources.DressUpItems,
            sources.DressUpGroups,
            sources.HairAndMakeup,
            sources.FashionShops,
            sources.DressUpLineups,
            sources.HairAndMakeupLineups);

    private static string CreateLineupRowRevision(
        string fileRevision,
        string rowKind,
        ZaFashionLineupDataRow row) =>
        ZaFashionCatalogFlatBufferSupport.CreateRowRevision(
            rowKind,
            hash =>
            {
                ZaFashionCatalogFlatBufferSupport.Append(hash, fileRevision);
                ZaFashionCatalogFlatBufferSupport.Append(hash, row.LineupPhysicalIndex);
                ZaFashionCatalogFlatBufferSupport.Append(hash, row.EntryPhysicalIndex);
                ZaFashionCatalogFlatBufferSupport.Append(hash, row.LineupId);
                ZaFashionCatalogFlatBufferSupport.Append(hash, row.ItemId);
            });

    private static T ResolveBoundRow<T>(
        IReadOnlyList<T> rows,
        ZaFashionCatalogRowBinding binding,
        string sourceRevision,
        string rowPrefix,
        Func<T, string> getRevision,
        string label)
    {
        if (!string.Equals(binding.SourceRevision, sourceRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {label} edit belongs to another source revision. Reload before editing.");
        }

        if ((uint)binding.PhysicalIndex >= (uint)rows.Count)
        {
            throw new InvalidDataException(
                $"The {label} physical index is outside the loaded catalog.");
        }

        var expectedId = CreatePhysicalRowId(rowPrefix, binding.PhysicalIndex);
        if (!string.Equals(binding.PhysicalRowId, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {label} physical-row identity does not match its source position.");
        }

        var row = rows[binding.PhysicalIndex];
        if (!string.Equals(binding.RowRevision, getRevision(row), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {label} row changed after it was loaded. Reload before editing.");
        }

        return row;
    }

    private static string CreatePhysicalRowId(string prefix, int physicalIndex) =>
        string.Concat(prefix, physicalIndex.ToString(CultureInfo.InvariantCulture));

    private static void EnsureHasDressUpItemChange(ZaDressUpItemPatch patch)
    {
        if (patch.ItemId is null
            && patch.ModelPart is null
            && patch.CatalogGroupCode is null
            && patch.ModelVariant is null
            && patch.CategoryCode is null
            && patch.ColorVariantCode is null
            && patch.PrimaryColorLabel is null
            && patch.SecondaryColorLabel is null
            && patch.DisplayOrder is null
            && patch.VariantOrder is null)
        {
            throw new InvalidDataException("The dress-up item patch contains no changes.");
        }
    }

    private static void ValidateDressUpItemReferences(
        ParsedCatalogSources state,
        ZaDressUpItemPatch patch)
    {
        EnsureKnownTextOption(
            patch.ModelPart,
            state.DressUpGroups.Rows.Select(row => row.ModelPart),
            "Dress-up item model part");
        EnsureKnownValueOption(
            patch.CatalogGroupCode,
            state.DressUpItems.Rows.Select(row => row.CatalogGroupCode),
            "Dress-up item catalog group code");
        EnsureKnownTextOption(
            patch.ModelVariant,
            state.DressUpItems.Rows.Select(row => row.ModelVariant),
            "Dress-up item model variant");
        EnsureKnownValueOption(
            patch.CategoryCode,
            state.DressUpItems.Rows.Select(row => row.CategoryCode),
            "Dress-up item category code");
        EnsureKnownValueOption(
            patch.ColorVariantCode,
            state.DressUpItems.Rows.Select(row => row.ColorVariantCode),
            "Dress-up item color variant code");
        var colorLabels = state.DressUpItems.Rows
            .SelectMany(row => new[] { row.PrimaryColorLabel, row.SecondaryColorLabel });
        EnsureKnownTextOption(
            patch.PrimaryColorLabel,
            colorLabels,
            "Dress-up item primary color label");
        EnsureKnownTextOption(
            patch.SecondaryColorLabel,
            colorLabels,
            "Dress-up item secondary color label");
    }

    private static void ValidateDressUpGroupReferences(
        ParsedCatalogSources state,
        ZaDressUpGroupPatch patch)
    {
        EnsureKnownTextOption(
            patch.ModelPart,
            state.DressUpItems.Rows.Select(row => row.ModelPart),
            "Dress-up group model part");
        EnsureKnownTextOption(
            patch.DisplayLabel,
            state.DressUpGroups.Rows.Select(row => row.DisplayLabel),
            "Dress-up group display label");
    }

    private static void ValidateHairAndMakeupReferences(
        ParsedCatalogSources state,
        ZaHairAndMakeupPatch patch)
    {
        EnsureKnownTextOption(
            patch.ModelKey,
            state.HairAndMakeup.Rows.Select(row => row.ModelKey),
            "Hair and makeup model key");
        EnsureKnownValueOption(
            patch.CatalogTypeCode,
            state.HairAndMakeup.Rows.Select(row => row.CatalogTypeCode),
            "Hair and makeup catalog type code");
        if (patch.ColorValue?.IsSpecified == true && patch.ColorValue.Value is not null)
        {
            EnsureKnownTextOption(
                patch.ColorValue.Value,
                state.HairAndMakeup.Rows.Select(row => row.ColorValue),
                "Hair and makeup color value");
        }

        if (patch.LabelKey?.IsSpecified == true && patch.LabelKey.Value is not null)
        {
            EnsureKnownTextOption(
                patch.LabelKey.Value,
                state.HairAndMakeup.Rows.Select(row => row.LabelKey),
                "Hair and makeup label key");
        }

        EnsureKnownValueOption(
            patch.GroupCode,
            state.HairAndMakeup.Rows.Select(row => row.GroupCode),
            "Hair and makeup group code");
        EnsureKnownValueOption(
            patch.VariantCode,
            state.HairAndMakeup.Rows.Select(row => row.VariantCode),
            "Hair and makeup variant code");
    }

    private static void ValidateSourceRelationships(
        ZaDressUpCatalogDocument dressUpItems,
        ZaHairAndMakeupCatalogDocument hairAndMakeup,
        IReadOnlyDictionary<string, IReadOnlyList<string>> shopsByLineup,
        ZaFashionLineupCatalogDocument dressUpLineups,
        ZaFashionLineupCatalogDocument hairAndMakeupLineups)
    {
        var dressUpIds = dressUpItems.Rows.Select(row => row.ItemId).ToHashSet();
        var hairAndMakeupIds = hairAndMakeup.Rows.Select(row => row.ItemId).ToHashSet();
        var dressUpLineupIds = dressUpLineups.Rows
            .Select(row => row.LineupId)
            .ToHashSet(StringComparer.Ordinal);
        var hairAndMakeupLineupIds = hairAndMakeupLineups.Rows
            .Select(row => row.LineupId)
            .ToHashSet(StringComparer.Ordinal);
        if (dressUpLineupIds.Overlaps(hairAndMakeupLineupIds))
        {
            throw new InvalidDataException(
                "A Fashion shop lineup ID is ambiguous between dress-up and hair and makeup sources.");
        }

        foreach (var lineup in dressUpLineups.Rows)
        {
            if (!dressUpIds.Contains(lineup.ItemId))
            {
                throw new InvalidDataException(
                    $"Dress-up shop lineup '{lineup.LineupId}' references item ID {lineup.ItemId}, which is absent from the exact loaded dress-up catalog.");
            }
        }

        foreach (var lineup in hairAndMakeupLineups.Rows)
        {
            if (!hairAndMakeupIds.Contains(lineup.ItemId))
            {
                throw new InvalidDataException(
                    $"Hair and makeup shop lineup '{lineup.LineupId}' references item ID {lineup.ItemId}, which is absent from the exact loaded hair and makeup catalog.");
            }
        }

        foreach (var lineupId in shopsByLineup.Keys)
        {
            if (!dressUpLineupIds.Contains(lineupId)
                && !hairAndMakeupLineupIds.Contains(lineupId))
            {
                throw new InvalidDataException(
                    $"Fashion shop lineup '{lineupId}' is absent from both exact loaded lineup catalogs.");
            }
        }
    }

    private static void ValidateCatalogItemIdentityChange(
        IEnumerable<uint> catalogItemIds,
        IReadOnlyList<ZaFashionLineupDataRow> lineupRows,
        uint currentItemId,
        uint? requestedItemId,
        string label)
    {
        if (requestedItemId is null || requestedItemId.Value == currentItemId)
        {
            return;
        }

        if (catalogItemIds.Any(itemId => itemId == requestedItemId.Value))
        {
            throw new InvalidDataException(
                $"The {label} ID {requestedItemId.Value} is already used by another physical catalog row.");
        }

        var referenceCount = lineupRows.Count(row => row.ItemId == currentItemId);
        if (referenceCount > 0)
        {
            throw new InvalidDataException(
                $"The {label} ID {currentItemId} is still used by {referenceCount} exact shop-lineup entries. Stage every reference replacement before changing the catalog identity.");
        }
    }

    private static void EnsureKnownValueOption<T>(
        T? requested,
        IEnumerable<T> loadedOptions,
        string label)
        where T : struct
    {
        if (requested is null)
        {
            return;
        }

        var comparer = EqualityComparer<T>.Default;
        if (loadedOptions.Any(option => comparer.Equals(option, requested.Value)))
        {
            return;
        }

        throw new InvalidDataException(
            $"{label} is not present in the exact loaded Z-A catalog options.");
    }

    private static void EnsureKnownTextOption(
        string? requested,
        IEnumerable<string?> loadedOptions,
        string label)
    {
        if (requested is null)
        {
            return;
        }

        if (loadedOptions.Any(option => string.Equals(option, requested, StringComparison.Ordinal)))
        {
            return;
        }

        throw new InvalidDataException(
            $"{label} is not present in the exact loaded Z-A catalog options.");
    }

    private static void ValidateOptionalEdit(ZaOptionalCatalogText? edit, string label)
    {
        if (edit is not null && !edit.IsSpecified)
        {
            throw new InvalidDataException(
                $"{label} edit must explicitly set or clear the value.");
        }
    }

    private static void EnsureChanged<T>(T original, T updated, string label)
        where T : notnull
    {
        if (EqualityComparer<T>.Default.Equals(original, updated))
        {
            throw new InvalidDataException($"The {label} patch does not change the selected row.");
        }
    }

    private static void EnsureDressUpPreserved(
        ZaDressUpCatalogDocument original,
        ZaDressUpCatalogDocument updated,
        int changedIndex)
    {
        EnsureRowsPreserved(original.Rows, updated.Rows, changedIndex, "dress-up item");
        var before = original.Rows[changedIndex];
        var after = updated.Rows[changedIndex];
        if (before.HasReservedFlagA != after.HasReservedFlagA
            || before.ReservedFlagA != after.ReservedFlagA
            || before.HasAlternateModelVariant != after.HasAlternateModelVariant
            || !string.Equals(
                before.AlternateModelVariant,
                after.AlternateModelVariant,
                StringComparison.Ordinal)
            || before.HasReservedFlagB != after.HasReservedFlagB
            || before.ReservedFlagB != after.ReservedFlagB)
        {
            throw new InvalidDataException(
                "The dress-up item rewrite changed an unexposed source field; no output was returned.");
        }
    }

    private static void EnsureHairAndMakeupPreserved(
        ZaHairAndMakeupCatalogDocument original,
        ZaHairAndMakeupCatalogDocument updated,
        int changedIndex)
    {
        EnsureRowsPreserved(original.Rows, updated.Rows, changedIndex, "hair and makeup");
        var before = original.Rows[changedIndex];
        var after = updated.Rows[changedIndex];
        if (before.HasReservedFlag != after.HasReservedFlag
            || before.ReservedFlag != after.ReservedFlag)
        {
            throw new InvalidDataException(
                "The hair and makeup rewrite changed an unexposed source field; no output was returned.");
        }
    }

    private static void EnsureRowsPreserved<T>(
        IReadOnlyList<T> original,
        IReadOnlyList<T> updated,
        int changedIndex,
        string label)
        where T : notnull
    {
        if (original.Count != updated.Count)
        {
            throw new InvalidDataException(
                $"The {label} rewrite changed the fixed physical-row count; no output was returned.");
        }

        for (var index = 0; index < original.Count; index++)
        {
            if (index != changedIndex
                && !EqualityComparer<T>.Default.Equals(original[index], updated[index]))
            {
                throw new InvalidDataException(
                    $"The {label} rewrite changed untouched physical row {index}; no output was returned.");
            }
        }
    }

    private sealed record ParsedCatalogSources(
        ZaDressUpCatalogDocument DressUpItems,
        ZaDressUpGroupCatalogDocument DressUpGroups,
        ZaHairAndMakeupCatalogDocument HairAndMakeup,
        ZaFashionLineupCatalogDocument DressUpLineups,
        ZaFashionLineupCatalogDocument HairAndMakeupLineups);
}
