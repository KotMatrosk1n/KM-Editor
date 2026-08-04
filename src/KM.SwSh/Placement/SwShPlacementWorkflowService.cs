// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Workflows;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Placement;

public sealed class SwShPlacementWorkflowService
{
    private const string CatalogParserSchema = "swsh-placement-catalog-v3";
    private const int MaximumCatalogQueryLimit = 250;
    private const int PlacementArchiveCacheCapacity = 8;
    private const int PlacementDetailCacheCapacity = 64;
    private const ulong Wr02HoeruoObjectHash = 0x12E3C0CA0F529035;

    private static readonly string[] EditingSnapshotPrimaryTransformLabels =
        ["X", "Y", "Z", "Rotation Y"];
    private static readonly SwShCacheArtifactDescriptor CatalogCacheArtifact = new(
        "placement.catalog",
        "catalog",
        SwShCacheArtifactPolicy.Balanced);

    private readonly ProjectWorkflowMemoryCache<SwShPlacementWorkflow> memoryCache = new();
    private readonly SwShCacheManager? cacheManager;
    private readonly object catalogSyncRoot = new();
    private readonly BoundedLruCache<PlacementArchiveCacheKey, SwShPlacementZoneArchive> archiveCache =
        new(PlacementArchiveCacheCapacity);
    private readonly BoundedLruCache<PlacementDetailCacheKey, SwShPlacedObjectRecord> detailCache =
        new(PlacementDetailCacheCapacity);
    private PlacementCatalogRuntimeEntry? catalogEntry;
    private SwShPlacementCatalogCacheData? retainedCatalogData;

    public SwShPlacementWorkflowService(SwShCacheManager? cacheManager = null)
    {
        this.cacheManager = cacheManager;
    }

    public const string PlacementDataPath = "romfs/bin/archive/field/resident/placement.gfpak";
    public const string ItemHashPath = "romfs/bin/pml/item/item_hash_to_index.dat";
    public const string EnglishItemNamePath = "romfs/bin/message/English/common/itemname.dat";

    public const string LocationXField = "locationX";
    public const string LocationYField = "locationY";
    public const string LocationZField = "locationZ";
    public const string RotationYField = "rotationY";
    public const string ItemIdField = "itemId";
    public const string QuantityField = "quantity";
    public const string ChanceField = "chance";

    public const double MinimumCoordinate = -1_000_000;
    public const double MaximumCoordinate = 1_000_000;
    public const double MinimumRotation = -3600;
    public const double MaximumRotation = 3600;
    public const int MaximumItemId = ushort.MaxValue;
    public const int MaximumFieldItemQuantity = byte.MaxValue;
    public const int MaximumQuantity = 999;
    public const int MaximumChance = 100;

    private const string AreaNameHashTableMember = "AreaNameHashTable.tbl";
    private const string ZoneNameHashTableMember = "ZoneNameHashTable.tbl";
    private const string ObjectNameHashTableMember = "ObjectNameHashTable.tbl";
    private const string VanishFlagAutoTableMember = "VanishFlagAutoTable.tbl";
    private const string FlagworkRootPath = "romfs/bin/flagwork/";
    private const string TrainerIdHashTablePath = "romfs/bin/trainer/trainer_id_hash_table.tbl";

    private static readonly IReadOnlyList<SwShPlacementEditableField> EditableFields =
    [
        new SwShPlacementEditableField(LocationXField, "X", "number", MinimumCoordinate, MaximumCoordinate, Group: "Transform"),
        new SwShPlacementEditableField(LocationYField, "Y", "number", MinimumCoordinate, MaximumCoordinate, Group: "Transform"),
        new SwShPlacementEditableField(LocationZField, "Z", "number", MinimumCoordinate, MaximumCoordinate, Group: "Transform"),
        new SwShPlacementEditableField(RotationYField, "Rotation Y", "number", MinimumRotation, MaximumRotation, Group: "Transform"),
        new SwShPlacementEditableField(ItemIdField, "Item", "integer", 0, MaximumItemId, Group: "Item"),
        new SwShPlacementEditableField(QuantityField, "Quantity", "integer", 0, MaximumQuantity, Group: "Item"),
        new SwShPlacementEditableField(ChanceField, "Chance", "integer", 0, MaximumChance, Group: "Item"),
    ];

    private static readonly IReadOnlyDictionary<string, PlacementCategoryInfo> CategoryByObjectType =
        new Dictionary<string, PlacementCategoryInfo>(StringComparer.Ordinal)
        {
            ["FieldItem"] = new("items", "Items", "Visible pickups, hidden pickups, and berry/tree item entries."),
            ["HiddenItem"] = new("items", "Items", "Visible pickups, hidden pickups, and berry/tree item entries."),
            ["BerryTree"] = new("items", "Items", "Visible pickups, hidden pickups, and berry/tree item entries."),
            ["NPCType1"] = new("npcsTrainers", "NPCs & Trainers", "NPC instances, trainer anchors, models, animations, messages, paths, and event references."),
            ["NPCType2"] = new("npcsTrainers", "NPCs & Trainers", "NPC instances, trainer anchors, models, animations, messages, paths, and event references."),
            ["Trainer"] = new("npcsTrainers", "NPCs & Trainers", "NPC instances, trainer anchors, models, animations, messages, paths, and event references."),
            ["Critter"] = new("pokemonEncounters", "Pokemon & Encounters", "Static Pokemon, wild symbol anchors, raid dens, fishing points, and ambient critter placements."),
            ["FishingPoint"] = new("pokemonEncounters", "Pokemon & Encounters", "Static Pokemon, wild symbol anchors, raid dens, fishing points, and ambient critter placements."),
            ["Nest"] = new("pokemonEncounters", "Pokemon & Encounters", "Static Pokemon, wild symbol anchors, raid dens, fishing points, and ambient critter placements."),
            ["StaticObject"] = new("pokemonEncounters", "Pokemon & Encounters", "Static Pokemon, wild symbol anchors, raid dens, fishing points, and ambient critter placements."),
            ["Symbol"] = new("pokemonEncounters", "Pokemon & Encounters", "Static Pokemon, wild symbol anchors, raid dens, fishing points, and ambient critter placements."),
            ["FlyTo"] = new("travelNavigation", "Travel & Navigation", "Warps, fly anchors, spawn anchors, jumps, ladders, and traversal objects."),
            ["Ladder"] = new("travelNavigation", "Travel & Navigation", "Warps, fly anchors, spawn anchors, jumps, ladders, and traversal objects."),
            ["PokeCenterAnchor"] = new("travelNavigation", "Travel & Navigation", "Warps, fly anchors, spawn anchors, jumps, ladders, and traversal objects."),
            ["RotomRally"] = new("travelNavigation", "Travel & Navigation", "Warps, fly anchors, spawn anchors, jumps, ladders, and traversal objects."),
            ["StepJump"] = new("travelNavigation", "Travel & Navigation", "Warps, fly anchors, spawn anchors, jumps, ladders, and traversal objects."),
            ["Warp"] = new("travelNavigation", "Travel & Navigation", "Warps, fly anchors, spawn anchors, jumps, ladders, and traversal objects."),
            ["Environment"] = new("worldObjects", "World Objects", "Unit objects, particles, environment events, and scene support objects."),
            ["IKStep"] = new("worldObjects", "World Objects", "Unit objects, particles, environment events, and scene support objects."),
            ["Particle"] = new("worldObjects", "World Objects", "Unit objects, particles, environment events, and scene support objects."),
            ["UnitObject"] = new("worldObjects", "World Objects", "Unit objects, particles, environment events, and scene support objects."),
            ["AdvancedTip"] = new("messagesPrompts", "Messages & UI Prompts", "Trainer tips, signs, popups, and message/sign hash placements."),
            ["Popup"] = new("messagesPrompts", "Messages & UI Prompts", "Trainer tips, signs, popups, and message/sign hash placements."),
            ["TrainerTip"] = new("messagesPrompts", "Messages & UI Prompts", "Trainer tips, signs, popups, and message/sign hash placements."),
            ["Quadrant"] = new("triggersVolumes", "Triggers & Volumes", "Trigger and quadrant volume records."),
            ["Trigger"] = new("triggersVolumes", "Triggers & Volumes", "Trigger and quadrant volume records."),
            ["Path"] = new("pathsTechnical", "Paths & Technical", "Movement paths and technical placement metadata."),
        };

    private static readonly IReadOnlyList<string> CategoryOrder =
    [
        "items",
        "npcsTrainers",
        "pokemonEncounters",
        "travelNavigation",
        "worldObjects",
        "messagesPrompts",
        "triggersVolumes",
        "pathsTechnical",
    ];

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Placement requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShPlacementWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var workflow = LoadUncached(project);
        memoryCache.Set(project.Paths, CreateEditingSnapshot(workflow));
        return workflow;
    }

    public SwShPlacementWorkflow LoadForEditing(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (memoryCache.TryGet(project.Paths, out var cachedWorkflow))
        {
            return cachedWorkflow!;
        }

        var workflow = CreateEditingSnapshot(LoadUncached(project));
        memoryCache.Set(project.Paths, workflow);
        return workflow;
    }

    public void ClearMemoryCache(bool clearReusableDataCache = true)
    {
        memoryCache.Clear();
        lock (catalogSyncRoot)
        {
            catalogEntry = null;
            archiveCache.Clear();
            detailCache.Clear();
            if (clearReusableDataCache)
            {
                retainedCatalogData = null;
            }
        }
    }

    public SwShPlacementCatalog OpenCatalog(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        lock (catalogSyncRoot)
        {
            return EnsureCatalog(project).Data.Catalog;
        }
    }

    public SwShPlacementCatalogQueryResult QueryCatalog(
        OpenedProject project,
        string revision,
        string? categoryId,
        string? searchText,
        int offset,
        int limit,
        EditSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(revision))
        {
            throw SwShPlacementCatalogException.Stale(
                "Placement catalog revision is required. Reopen Placement and try again.");
        }

        if (offset < 0)
        {
            throw new SwShPlacementCatalogException("Placement query offset must not be negative.");
        }

        if (limit is < 1 or > MaximumCatalogQueryLimit)
        {
            throw new SwShPlacementCatalogException(
                $"Placement query limit must be between 1 and {MaximumCatalogQueryLimit.ToString(CultureInfo.InvariantCulture)}.");
        }

        lock (catalogSyncRoot)
        {
            var entry = ResolveCatalogForRevision(project, revision);

            var categoryFilter = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim();
            var searchFilter = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
            var page = new List<SwShPlacedObjectRecord>(Math.Min(limit, entry.Data.Rows.Count));
            var editingSnapshot = entry.EditingSnapshot;
            var projectedSnapshot = session is { PendingEdits.Count: > 0 }
                ? SwShPlacementEditSessionService.OverlayPendingEdits(
                    editingSnapshot,
                    session.PendingEdits,
                    entry.DetailContext?.ItemHashes)
                : null;
            var totalCount = 0;
            for (var index = 0; index < entry.Data.Rows.Count; index++)
            {
                var row = entry.Data.Rows[index];
                var summary = row.Summary;
                var effectiveSearchText = row.SearchText;
                if (projectedSnapshot is not null
                    && !ReferenceEquals(projectedSnapshot.Objects[index], editingSnapshot.Objects[index]))
                {
                    var overlaidObject = WithRefreshedPreviewText(projectedSnapshot.Objects[index]);
                    summary = overlaidObject with { Fields = Array.Empty<SwShPlacementFieldValue>() };
                    effectiveSearchText = string.Concat(
                        row.SearchText,
                        " ",
                        CreateCatalogSearchText(overlaidObject));
                }

                if (categoryFilter is not null
                    && !string.Equals(summary.CategoryId, categoryFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                if (searchFilter is not null
                    && !effectiveSearchText.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (totalCount >= offset && page.Count < limit)
                {
                    page.Add(summary);
                }

                totalCount++;
            }

            return new SwShPlacementCatalogQueryResult(
                entry.Data.Revision,
                page,
                offset,
                limit,
                totalCount);
        }
    }

    public SwShPlacementObjectDetailResult LoadCatalogObject(
        OpenedProject project,
        string revision,
        string objectId,
        EditSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(revision))
        {
            throw SwShPlacementCatalogException.Stale(
                "Placement catalog revision is required. Reopen Placement and try again.");
        }

        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new SwShPlacementCatalogException(
                "A Placement object must be selected before its details can be loaded.");
        }

        lock (catalogSyncRoot)
        {
            var entry = ResolveCatalogForRevision(project, revision);
            if (!entry.ObjectIndexes.TryGetValue(objectId, out var objectIndex))
            {
                throw SwShPlacementCatalogException.Stale(
                    "The selected Placement object is no longer present. Reopen Placement and try again.");
            }

            var detail = LoadCatalogObjectCore(project, entry, objectIndex);
            if (session is not null && session.PendingEdits.Count > 0)
            {
                detail = OverlayCatalogObject(entry, objectIndex, detail, session);
            }

            return new SwShPlacementObjectDetailResult(
                entry.Data.Revision,
                detail,
                entry.Data.Catalog.Diagnostics);
        }
    }

    private static SwShPlacementWorkflow CreateEditingSnapshot(
        SwShPlacementWorkflow workflow)
    {
        return workflow with
        {
            Objects = workflow.Objects.Select(CreateEditingSnapshotRecord).ToArray(),
            Categories = [],
        };
    }

    private static SwShPlacedObjectRecord CreateEditingSnapshotRecord(
        SwShPlacedObjectRecord placedObject)
    {
        if (placedObject.Fields is null || placedObject.Fields.Count == 0)
        {
            return placedObject;
        }

        var retainedFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in EditingSnapshotPrimaryTransformLabels)
        {
            var primaryRawTransform = placedObject.Fields.FirstOrDefault(field =>
                field.Field.StartsWith("raw.", StringComparison.Ordinal)
                && field.Group == "Transform"
                && field.Label == label);
            if (primaryRawTransform is not null)
            {
                retainedFields.Add(primaryRawTransform.Field);
            }
        }

        if (FindPlacementPreviewField(placedObject) is { } previewField)
        {
            retainedFields.Add(previewField.Field);
        }

        var fields = placedObject.Fields
            .Where(field =>
                !field.IsReadOnly
                || retainedFields.Contains(field.Field)
                || IsCanonicalEditingField(field.Field)
                || field.Field is "fieldItem.hash" or "hiddenItem.hash")
            .ToArray();

        return placedObject with { Fields = fields };
    }

    private static bool IsCanonicalEditingField(string field)
    {
        return field is LocationXField
            or LocationYField
            or LocationZField
            or RotationYField
            or ItemIdField
            or QuantityField
            or ChanceField;
    }

    private PlacementCatalogRuntimeEntry EnsureCatalog(OpenedProject project)
    {
        var sourceSnapshot = CaptureCatalogSourceSnapshot(project);
        var revision = sourceSnapshot.Revision;
        if (catalogEntry is not null
            && Equals(catalogEntry.Paths, project.Paths)
            && string.Equals(catalogEntry.Data.Revision, revision, StringComparison.Ordinal))
        {
            return catalogEntry;
        }

        SwShPlacementCatalogCacheData? data = null;
        PlacementDetailContext? detailContext = null;
        var loadedFromCache = sourceSnapshot.CacheIdentity is not null
            && TryLoadCachedCatalog(sourceSnapshot.CacheIdentity, revision, out data);

        data ??= BuildCatalogCacheData(project, revision, out detailContext);
        var verifiedSnapshot = CaptureCatalogSourceSnapshot(project);
        if (!string.Equals(revision, verifiedSnapshot.Revision, StringComparison.Ordinal))
        {
            throw SwShPlacementCatalogException.Stale(
                "Placement source data changed while the catalog was loading. Reopen Placement and try again.");
        }

        if (!loadedFromCache && verifiedSnapshot.CacheIdentity is not null)
        {
            TryStoreCachedCatalog(verifiedSnapshot.CacheIdentity, data);
        }

        var objectIndexes = data.Rows
            .Select((row, index) => (row.Summary.ObjectId, Index: index))
            .ToDictionary(entry => entry.ObjectId, entry => entry.Index, StringComparer.Ordinal);
        var nextEntry = new PlacementCatalogRuntimeEntry(
            project.Paths,
            data,
            objectIndexes,
            detailContext,
            verifiedSnapshot.CacheIdentity,
            verifiedSnapshot.Fingerprints);

        catalogEntry = nextEntry;
        memoryCache.Set(project.Paths, data.EditingSnapshot);
        archiveCache.Clear();
        detailCache.Clear();
        return nextEntry;
    }

    private PlacementCatalogRuntimeEntry ResolveCatalogForRevision(
        OpenedProject project,
        string revision)
    {
        if (catalogEntry is not null
            && Equals(catalogEntry.Paths, project.Paths)
            && string.Equals(catalogEntry.Data.Revision, revision, StringComparison.Ordinal))
        {
            if (AreCatalogDependencyMetadataCurrent(project.Paths, catalogEntry.DependencyFingerprints))
            {
                return catalogEntry;
            }

            var currentSnapshot = CaptureCatalogSourceSnapshot(project);
            if (!string.Equals(currentSnapshot.Revision, revision, StringComparison.Ordinal))
            {
                InvalidateCatalogRuntime();
                throw SwShPlacementCatalogException.Stale(
                    "Placement source data changed after this catalog was opened. Reopen Placement and try again.");
            }
        }

        var entry = EnsureCatalog(project);
        ValidateCatalogRevision(entry, revision);
        return entry;
    }

    public SwShCacheSourceIdentity? CaptureCatalogCacheSourceIdentity(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (catalogSyncRoot)
        {
            if (catalogEntry is not null
                && Equals(catalogEntry.Paths, project.Paths)
                && AreCatalogDependencyMetadataCurrent(project.Paths, catalogEntry.DependencyFingerprints))
            {
                return catalogEntry.CacheIdentity;
            }

            return CaptureCatalogSourceSnapshot(project).CacheIdentity;
        }
    }

    private bool TryLoadCachedCatalog(
        SwShCacheSourceIdentity sourceIdentity,
        string revision,
        out SwShPlacementCatalogCacheData? data)
    {
        data = null;
        if (IsUsableCatalogCacheData(retainedCatalogData, revision))
        {
            data = retainedCatalogData;
            return true;
        }

        if (cacheManager is null)
        {
            return false;
        }

        try
        {
            if (!cacheManager.TryGetArtifact(
                    sourceIdentity,
                    CatalogCacheArtifact,
                    out SwShPlacementCatalogCacheData cachedData))
            {
                return false;
            }

            if (IsUsableCatalogCacheData(cachedData, revision))
            {
                data = cachedData;
                retainedCatalogData = cachedData;
                return true;
            }

            cacheManager.RemoveArtifact(sourceIdentity, CatalogCacheArtifact);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    private void TryStoreCachedCatalog(
        SwShCacheSourceIdentity sourceIdentity,
        SwShPlacementCatalogCacheData data)
    {
        if (cacheManager is null)
        {
            retainedCatalogData = data;
            return;
        }

        try
        {
            // Retain one decoded catalog for the current process. This preserves the
            // intended session-memory cache in Minimal mode and for LayeredFS sources
            // without serializing a very large disk-ineligible payload.
            retainedCatalogData = data;
            if (sourceIdentity.Sources.Any(source => source.SourceLayer != ProjectFileLayer.Base)
                || cacheManager.GetSettings().Mode == SwShCacheMode.Minimal)
            {
                return;
            }

            cacheManager.SetArtifact(sourceIdentity, CatalogCacheArtifact, data);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsUsableCatalogCacheData(
        SwShPlacementCatalogCacheData? data,
        string revision)
    {
        if (data is null
            || data.Catalog is null
            || data.Rows is null
            || data.EditingSnapshot is null
            || !string.Equals(data.Revision, revision, StringComparison.Ordinal)
            || !string.Equals(data.Catalog.Revision, revision, StringComparison.Ordinal)
            || data.Rows.Count != data.EditingSnapshot.Objects.Count
            || data.Catalog.Stats.TotalObjectCount != data.Rows.Count)
        {
            return false;
        }

        for (var index = 0; index < data.Rows.Count; index++)
        {
            var row = data.Rows[index];
            var editingObject = data.EditingSnapshot.Objects[index];
            if (row is null
                || row.Summary is null
                || editingObject is null
                || string.IsNullOrWhiteSpace(row.Summary.ObjectId)
                || !string.Equals(row.Summary.ObjectId, editingObject.ObjectId, StringComparison.Ordinal)
                || (row.Summary.Fields?.Count ?? 0) != 0)
            {
                return false;
            }
        }

        return true;
    }

    internal SwShPlacementCatalogCacheData BuildCatalogCacheData(
        OpenedProject project,
        string revision)
    {
        return BuildCatalogCacheData(project, revision, out _);
    }

    private SwShPlacementCatalogCacheData BuildCatalogCacheData(
        OpenedProject project,
        string revision,
        out PlacementDetailContext? detailContext)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        var workflow = LoadUncached(project, out detailContext);
        var editingSnapshot = CreateEditingSnapshot(workflow);
        var diagnostics = SanitizeCatalogDiagnostics(project.Paths, workflow.Diagnostics);
        var catalog = new SwShPlacementCatalog(
            revision,
            workflow.Summary with { Diagnostics = SanitizeCatalogDiagnostics(project.Paths, workflow.Summary.Diagnostics) },
            workflow.EditableFields,
            workflow.Stats,
            diagnostics,
            workflow.Categories);
        var rows = workflow.Objects
            .Select(placedObject => new SwShPlacementCatalogCacheRow(
                placedObject with { Fields = Array.Empty<SwShPlacementFieldValue>() },
                CreateCatalogSearchText(placedObject)))
            .ToArray();

        return new SwShPlacementCatalogCacheData(
            revision,
            catalog,
            rows,
            editingSnapshot);
    }

    private static PlacementDetailContext? LoadCatalogDetailContext(OpenedProject project)
    {
        var placementSource = ResolvePlacementDataSource(project);
        if (placementSource is null)
        {
            return null;
        }

        var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            var itemNames = LoadItemNames(project, diagnostics, out _);
            var itemDisplayNames = SwShItemsWorkflowService.CreateItemDisplayNames(project, itemNames);
            var itemHashes = LoadItemHashes(project, diagnostics, out _);
            var itemIdsByHash = CreateItemIdsByHash(itemHashes);
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(placementSource.AbsolutePath));
            var areaNames = LoadRequiredHashTable(pack, AreaNameHashTableMember);
            var zoneNames = LoadOptionalHashTable(pack, ZoneNameHashTableMember, diagnostics);
            var objectNames = LoadOptionalHashTable(pack, ObjectNameHashTableMember, diagnostics);
            var hashLabels = LoadPlacementHashLabels(
                project,
                pack,
                areaNames,
                zoneNames,
                objectNames,
                diagnostics);
            return new PlacementDetailContext(
                pack,
                zoneNames,
                objectNames,
                hashLabels,
                itemHashes,
                itemIdsByHash,
                itemDisplayNames,
                CreateProvenance(placementSource.GraphEntry));
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private SwShPlacedObjectRecord LoadCatalogObjectCore(
        OpenedProject project,
        PlacementCatalogRuntimeEntry entry,
        int objectIndex)
    {
        var summary = entry.Data.Rows[objectIndex].Summary;
        var detailKey = new PlacementDetailCacheKey(entry.Data.Revision, summary.ObjectId);
        if (detailCache.TryGet(detailKey, out var cachedDetail))
        {
            EnrichEditingSnapshot(entry, objectIndex, cachedDetail!);
            return cachedDetail!;
        }

        var context = entry.DetailContext ??= LoadCatalogDetailContext(project)
            ?? throw new SwShPlacementCatalogException(
                "Placement object details are unavailable because the catalog source could not be decoded.");
        var archiveKey = new PlacementArchiveCacheKey(entry.Data.Revision, summary.ArchiveMember);
        if (!archiveCache.TryGet(archiveKey, out var archive))
        {
            try
            {
                if (!context.Pack.ContainsFileName(summary.ArchiveMember))
                {
                    throw SwShPlacementCatalogException.Stale(
                        "The selected Placement area is no longer present. Reopen Placement and try again.");
                }

                archive = SwShPlacementZoneArchive.Parse(
                    context.Pack.GetFileByName(summary.ArchiveMember),
                    context.ItemIdsByHash);
                archiveCache.Set(archiveKey, archive);
            }
            catch (SwShPlacementCatalogException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                throw new SwShPlacementCatalogException(
                    "The selected Placement area could not be decoded. Reopen Placement and try again.");
            }
            catch (IOException)
            {
                throw new SwShPlacementCatalogException(
                    "The selected Placement area could not be read. Reopen Placement and try again.");
            }
        }

        var detail = FlattenArchive(
                summary.ArchiveMember,
                archive!,
                context.ZoneNames,
                context.ObjectNames,
                context.HashLabels,
                context.ItemHashes,
                context.ItemDisplayNames,
                context.Provenance)
            .FirstOrDefault(candidate => string.Equals(candidate.ObjectId, summary.ObjectId, StringComparison.Ordinal));
        if (detail is null)
        {
            throw SwShPlacementCatalogException.Stale(
                "The selected Placement object is no longer present. Reopen Placement and try again.");
        }

        detailCache.Set(detailKey, detail);
        EnrichEditingSnapshot(entry, objectIndex, detail);
        return detail;
    }

    internal void EnsureCatalogObjectsForEditing(
        OpenedProject project,
        IEnumerable<string?> objectIds,
        bool verifyContent = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(objectIds);

        lock (catalogSyncRoot)
        {
            if (catalogEntry is null || !Equals(catalogEntry.Paths, project.Paths))
            {
                return;
            }

            EnsureCatalogSourcesCurrentForEditing(project, catalogEntry, verifyContent);

            foreach (var objectId in objectIds
                .Where(objectId => !string.IsNullOrWhiteSpace(objectId))
                .Distinct(StringComparer.Ordinal))
            {
                if (catalogEntry.ObjectIndexes.TryGetValue(objectId!, out var objectIndex))
                {
                    LoadCatalogObjectCore(project, catalogEntry, objectIndex);
                }
            }
        }
    }

    private void EnrichEditingSnapshot(
        PlacementCatalogRuntimeEntry entry,
        int objectIndex,
        SwShPlacedObjectRecord detail)
    {
        var objects = entry.EditingSnapshot.Objects.ToArray();
        objects[objectIndex] = detail;
        if (entry.TouchEditingDetail(objectIndex, out var evictedIndex) && evictedIndex != objectIndex)
        {
            objects[evictedIndex] = entry.Data.EditingSnapshot.Objects[evictedIndex];
        }

        entry.EditingSnapshot = entry.EditingSnapshot with { Objects = objects };
        memoryCache.Set(entry.Paths, entry.EditingSnapshot);
    }

    private static SwShPlacedObjectRecord OverlayCatalogObject(
        PlacementCatalogRuntimeEntry entry,
        int objectIndex,
        SwShPlacedObjectRecord detail,
        EditSession session)
    {
        var objects = entry.EditingSnapshot.Objects.ToArray();
        objects[objectIndex] = detail;
        var workflow = entry.EditingSnapshot with { Objects = objects };
        var projected = SwShPlacementEditSessionService.OverlayPendingEdits(
            workflow,
            session.PendingEdits,
            entry.DetailContext?.ItemHashes);
        return WithRefreshedPreviewText(projected.Objects[objectIndex]);
    }

    private static void ValidateCatalogRevision(
        PlacementCatalogRuntimeEntry entry,
        string revision)
    {
        if (!string.Equals(entry.Data.Revision, revision, StringComparison.Ordinal))
        {
            throw SwShPlacementCatalogException.Stale(
                "Placement source data changed after this catalog was opened. Reopen Placement and try again.");
        }
    }

    private static string CreateCatalogSearchText(SwShPlacedObjectRecord placedObject)
    {
        var builder = new StringBuilder();
        AppendSearchValue(builder, placedObject.ArchiveMember);
        AppendSearchValue(builder, placedObject.CategoryLabel);
        foreach (var field in placedObject.Fields ?? [])
        {
            AppendSearchValue(builder, field.Label);
            AppendSearchValue(builder, field.Value);
            AppendSearchValue(builder, field.DisplayValue);
            AppendSearchValue(builder, field.Group);
        }

        AppendSearchValue(builder, placedObject.ItemHash);
        AppendSearchValue(builder, placedObject.ItemId?.ToString(CultureInfo.InvariantCulture));
        AppendSearchValue(builder, placedObject.ItemName);
        AppendSearchValue(builder, placedObject.Label);
        AppendSearchValue(builder, placedObject.Map);
        AppendSearchValue(builder, placedObject.ObjectType);
        AppendSearchValue(builder, placedObject.PreviewText);
        AppendSearchValue(builder, placedObject.ScriptId);
        return builder.ToString();
    }

    private static bool AreCatalogDependencyMetadataCurrent(
        ProjectPaths paths,
        IReadOnlyList<CatalogDependencyFingerprint> fingerprints)
    {
        foreach (var fingerprint in fingerprints)
        {
            var current = CaptureCurrentCatalogDependencyMetadata(paths, fingerprint.RelativePath);
            if (current.IsPresent != fingerprint.IsPresent
                || !string.Equals(current.SourceLayer, fingerprint.SourceLayer, StringComparison.Ordinal)
                || !string.Equals(current.SourceState, fingerprint.SourceState, StringComparison.Ordinal)
                || !string.Equals(current.AbsolutePath, fingerprint.AbsolutePath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.Length, fingerprint.Length, StringComparison.Ordinal)
                || !string.Equals(
                    current.LastWriteTimeUtcTicks,
                    fingerprint.LastWriteTimeUtcTicks,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static CatalogDependencyMetadata CaptureCurrentCatalogDependencyMetadata(
        ProjectPaths paths,
        string relativePath)
    {
        var layeredPath = CombineGraphPath(paths.OutputRootPath, relativePath);
        var basePath = relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? CombineGraphPath(paths.BaseRomFsPath, relativePath["romfs/".Length..])
            : relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
                ? CombineGraphPath(paths.BaseExeFsPath, relativePath["exefs/".Length..])
                : null;
        var layeredExists = layeredPath is not null && File.Exists(layeredPath);
        var baseExists = basePath is not null && File.Exists(basePath);
        var absolutePath = layeredExists ? layeredPath : baseExists ? basePath : null;
        if (absolutePath is null)
        {
            return new CatalogDependencyMetadata(
                IsPresent: false,
                SourceLayer: "missing",
                SourceState: "missing",
                AbsolutePath: null,
                Length: "missing",
                LastWriteTimeUtcTicks: "missing");
        }

        var sourceLayer = layeredExists ? "layered" : "base";
        var sourceState = layeredExists
            ? baseExists
                ? ProjectFileGraphEntryState.LayeredOverride.ToString()
                : ProjectFileGraphEntryState.LayeredOnly.ToString()
            : ProjectFileGraphEntryState.BaseOnly.ToString();
        try
        {
            var info = new FileInfo(absolutePath);
            return new CatalogDependencyMetadata(
                IsPresent: true,
                sourceLayer,
                sourceState,
                absolutePath,
                info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "missing",
                info.Exists
                    ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                    : "missing");
        }
        catch (IOException)
        {
            return new CatalogDependencyMetadata(
                IsPresent: true,
                sourceLayer,
                sourceState,
                absolutePath,
                Length: "unreadable",
                LastWriteTimeUtcTicks: "unreadable");
        }
        catch (UnauthorizedAccessException)
        {
            return new CatalogDependencyMetadata(
                IsPresent: true,
                sourceLayer,
                sourceState,
                absolutePath,
                Length: "unreadable",
                LastWriteTimeUtcTicks: "unreadable");
        }
    }

    private void EnsureCatalogSourcesCurrentForEditing(
        OpenedProject project,
        PlacementCatalogRuntimeEntry entry,
        bool verifyContent)
    {
        if (!verifyContent
            && AreCatalogDependencyMetadataCurrent(project.Paths, entry.DependencyFingerprints))
        {
            return;
        }

        var currentSnapshot = CaptureCatalogSourceSnapshot(project);
        if (!string.Equals(currentSnapshot.Revision, entry.Data.Revision, StringComparison.Ordinal))
        {
            InvalidateCatalogRuntime();
            throw SwShPlacementCatalogException.Stale(
                "Placement source data changed after editing began. Reopen Placement and try again.");
        }
    }

    private void InvalidateCatalogRuntime()
    {
        catalogEntry = null;
        memoryCache.Clear();
        archiveCache.Clear();
        detailCache.Clear();
    }

    private static void AppendSearchValue(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(value);
    }

    private static CatalogSourceSnapshot CaptureCatalogSourceSnapshot(OpenedProject project)
    {
        var language = SwShGameTextLanguage.Resolve(project.Paths);
        var dependencyPaths = GetCatalogDependencyPaths(project);
        var dependencies = dependencyPaths
            .Select(relativePath => new CatalogDependencySource(
                relativePath,
                ResolveWorkflowFile(project, relativePath)))
            .ToArray();
        var fingerprints = new CatalogDependencyFingerprint[dependencies.Length];
        Parallel.For(
            fromInclusive: 0,
            toExclusive: dependencies.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) },
            index => fingerprints[index] = CaptureCatalogDependencyFingerprint(dependencies[index]));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprint(hash, CatalogParserSchema);
        AppendFingerprint(hash, project.Paths.SelectedGame?.ToString() ?? "none");
        AppendFingerprint(hash, language);
        foreach (var fingerprint in fingerprints)
        {
            AppendFingerprint(hash, fingerprint.RelativePath.ToLowerInvariant());
            if (!fingerprint.IsPresent)
            {
                AppendFingerprint(hash, "missing");
                continue;
            }

            AppendFingerprint(hash, fingerprint.SourceLayer);
            AppendFingerprint(hash, fingerprint.SourceState);
            AppendFingerprint(hash, fingerprint.AbsolutePath!);
            AppendFingerprint(hash, fingerprint.Length);
            AppendFingerprint(hash, fingerprint.LastWriteTimeUtcTicks);
            AppendFingerprint(hash, fingerprint.ContentSha256);
        }

        var revision = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return new CatalogSourceSnapshot(
            revision,
            CreateCatalogCacheSourceIdentity(project, language, fingerprints),
            fingerprints);
    }

    private static string[] GetCatalogDependencyPaths(OpenedProject project)
    {
        var paths = project.FileGraph.Entries
            .Select(entry => entry.RelativePath)
            .Where(IsCatalogEnumeratedDependencyPath)
            .Concat(
            [
                PlacementDataPath,
                ItemHashPath,
                TrainerIdHashTablePath,
                SwShStaticEncountersWorkflowService.StaticEncounterDataPath,
                SwShItemTable.ItemDataRelativePath,
                SwShPersonalTable.PersonalDataRelativePath,
            ])
            .ToList();

        foreach (var fileName in new[] { "itemname.dat", "wazaname.dat" })
        {
            var source = ResolveCommonTextSource(project, fileName);
            paths.Add(source?.GraphEntry.RelativePath
                ?? SwShGameTextLanguage.CommonMessagePath(
                    SwShGameTextLanguage.Resolve(project.Paths),
                    fileName));
        }

        var abilityNamesSource = ResolvePreferredOrEnglishCommonTextSource(project, "tokusei.dat");
        paths.Add(abilityNamesSource?.GraphEntry.RelativePath
            ?? SwShGameTextLanguage.CommonMessagePath(
                SwShGameTextLanguage.Resolve(project.Paths),
                "tokusei.dat"));

        var staticMessageRoot = ResolveStaticEncounterMessageRoot(project);
        if (staticMessageRoot is not null)
        {
            paths.Add($"{staticMessageRoot}/monsname.dat");
            paths.Add($"{staticMessageRoot}/itemname.dat");
            paths.Add($"{staticMessageRoot}/wazaname.dat");
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsCatalogEnumeratedDependencyPath(string relativePath)
    {
        return (relativePath.StartsWith(FlagworkRootPath, StringComparison.OrdinalIgnoreCase)
                && relativePath.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
            || (relativePath.StartsWith(
                    SwShMoveDataFile.MoveDataRelativeDirectory + "/",
                    StringComparison.OrdinalIgnoreCase)
                && (relativePath.EndsWith(".wazabin", StringComparison.OrdinalIgnoreCase)
                    || relativePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)));
    }

    private static WorkflowFileSource? ResolvePreferredOrEnglishCommonTextSource(
        OpenedProject project,
        string fileName)
    {
        var language = SwShGameTextLanguage.Resolve(project.Paths);
        var preferred = ResolveWorkflowFile(
            project,
            SwShGameTextLanguage.CommonMessagePath(language, fileName));
        if (preferred is not null)
        {
            return preferred;
        }

        return string.Equals(language, SwShGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
            ? null
            : ResolveWorkflowFile(
                project,
                SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.English, fileName));
    }

    private static string? ResolveStaticEncounterMessageRoot(OpenedProject project)
    {
        const string messageRoot = "romfs/bin/message";
        var languages = project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith(messageRoot + "/", StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var start = messageRoot.Length + 1;
                var separator = entry.RelativePath.IndexOf('/', start);
                return separator < 0 ? null : entry.RelativePath[start..separator];
            })
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (languages.Length == 0)
        {
            return null;
        }

        var preferredLanguage = SwShGameTextLanguage.Resolve(project.Paths);
        var language = languages.Contains(preferredLanguage, StringComparer.OrdinalIgnoreCase)
            ? preferredLanguage
            : languages.Contains(SwShGameTextLanguage.English, StringComparer.OrdinalIgnoreCase)
                ? SwShGameTextLanguage.English
                : languages[0]!;
        return $"{messageRoot}/{language}/common";
    }

    private static SwShCacheSourceIdentity? CreateCatalogCacheSourceIdentity(
        OpenedProject project,
        string language,
        IReadOnlyList<CatalogDependencyFingerprint> fingerprints)
    {
        if (project.Paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || fingerprints.FirstOrDefault(fingerprint =>
                string.Equals(
                    fingerprint.RelativePath,
                    PlacementDataPath,
                    StringComparison.OrdinalIgnoreCase)) is not { IsCacheSource: true })
        {
            return null;
        }

        var present = fingerprints.Where(fingerprint => fingerprint.IsPresent).ToArray();
        if (present.Length == 0 || present.Any(fingerprint => !fingerprint.IsCacheSource))
        {
            return null;
        }

        var stamps = present.Select(fingerprint => new SwShCacheFileStamp(
            Path.GetFullPath(fingerprint.AbsolutePath!),
            fingerprint.CacheSourceLayer!.Value,
            fingerprint.CacheLength!.Value,
            fingerprint.CacheLastWriteTimeUtc!.Value,
            fingerprint.ContentSha256)).ToArray();
        return new SwShCacheSourceIdentity(
            SwShCacheManager.CacheSchemaVersion,
            $"{CatalogParserSchema};language={language}",
            project.Paths.SelectedGame.Value,
            stamps);
    }

    private static CatalogDependencyFingerprint CaptureCatalogDependencyFingerprint(
        CatalogDependencySource dependency)
    {
        if (dependency.Source is null)
        {
            return new CatalogDependencyFingerprint(
                dependency.RelativePath,
                IsPresent: false,
                SourceLayer: "missing",
                SourceState: "missing",
                AbsolutePath: null,
                Length: "missing",
                LastWriteTimeUtcTicks: "missing",
                ContentSha256: "missing",
                CacheSourceLayer: null,
                CacheLength: null,
                CacheLastWriteTimeUtc: null);
        }

        var source = dependency.Source;
        var length = "unreadable";
        var lastWriteTimeUtcTicks = "unreadable";
        long? cacheLength = null;
        DateTime? cacheLastWriteTimeUtc = null;
        try
        {
            var info = new FileInfo(source.AbsolutePath);
            length = info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "missing";
            lastWriteTimeUtcTicks = info.Exists
                ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                : "missing";
            if (info.Exists)
            {
                cacheLength = info.Length;
                cacheLastWriteTimeUtc = DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new CatalogDependencyFingerprint(
            dependency.RelativePath,
            IsPresent: true,
            source.GraphEntry.LayeredFile is null ? "base" : "layered",
            source.GraphEntry.State.ToString(),
            source.AbsolutePath,
            length,
            lastWriteTimeUtcTicks,
            CreateCatalogContentFingerprint(source.AbsolutePath),
            source.GraphEntry.LayeredFile is null
                ? ProjectFileLayer.Base
                : ProjectFileLayer.Layered,
            cacheLength,
            cacheLastWriteTimeUtc);
    }

    private static string CreateCatalogContentFingerprint(string absolutePath)
    {
        try
        {
            using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (FileNotFoundException)
        {
            return "missing";
        }
        catch (DirectoryNotFoundException)
        {
            return "missing";
        }
        catch (IOException)
        {
            return "unreadable";
        }
        catch (UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }

    private static void AppendFingerprint(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static IReadOnlyList<ValidationDiagnostic> SanitizeCatalogDiagnostics(
        ProjectPaths paths,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Select(diagnostic => diagnostic with
        {
            Message = SanitizeCatalogText(paths, diagnostic.Message),
            File = SanitizeCatalogFile(paths, diagnostic.File),
        }).ToArray();
    }

    private static string SanitizeCatalogText(ProjectPaths paths, string value)
    {
        var sanitized = value;
        foreach (var (path, replacement) in new[]
        {
            (paths.BaseRomFsPath, "base RomFS"),
            (paths.BaseExeFsPath, "base ExeFS"),
            (paths.OutputRootPath, "output root"),
        })
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                sanitized = sanitized.Replace(path, replacement, StringComparison.OrdinalIgnoreCase);
            }
        }

        return sanitized;
    }

    private static string? SanitizeCatalogFile(ProjectPaths paths, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
        {
            return value;
        }

        return SanitizeCatalogText(paths, value);
    }

    private SwShPlacementWorkflow LoadUncached(OpenedProject project)
    {
        return LoadUncached(project, out _);
    }

    private SwShPlacementWorkflow LoadUncached(
        OpenedProject project,
        out PlacementDetailContext? detailContext)
    {
        detailContext = null;
        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 0, [], new Dictionary<int, ulong>(), diagnostics);
        }

        var placementSource = ResolvePlacementDataSource(project);
        if (placementSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Placement data is not available for this project.",
                expected: PlacementDataPath));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 0, [], new Dictionary<int, ulong>(), diagnostics);
        }

        var itemNames = LoadItemNames(project, diagnostics, out var itemNameSourceCount);
        var itemDisplayNames = SwShItemsWorkflowService.CreateItemDisplayNames(project, itemNames);
        var itemHashes = LoadItemHashes(project, diagnostics, out var itemHashSourceCount);
        var itemIdsByHash = CreateItemIdsByHash(itemHashes);

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(placementSource.AbsolutePath));
            var areaNames = LoadRequiredHashTable(pack, AreaNameHashTableMember);
            var zoneNames = LoadOptionalHashTable(pack, ZoneNameHashTableMember, diagnostics);
            var objectNames = LoadOptionalHashTable(pack, ObjectNameHashTableMember, diagnostics);
            var hashLabels = LoadPlacementHashLabels(project, pack, areaNames, zoneNames, objectNames, diagnostics);
            var provenance = CreateProvenance(placementSource.GraphEntry);
            detailContext = new PlacementDetailContext(
                pack,
                zoneNames,
                objectNames,
                hashLabels,
                itemHashes,
                itemIdsByHash,
                itemDisplayNames,
                provenance);
            var records = new List<SwShPlacedObjectRecord>();
            var areaCount = 0;

            foreach (var areaName in areaNames.Values.OrderBy(value => value, StringComparer.Ordinal))
            {
                var archiveMember = areaName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                    ? areaName
                    : areaName + ".bin";
                if (!pack.ContainsFileName(archiveMember))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Placement area '{archiveMember}' is listed but is not present in the placement pack.",
                        file: PlacementDataPath,
                        expected: "Area member listed by AreaNameHashTable.tbl"));
                    continue;
                }

                try
                {
                    var archive = SwShPlacementZoneArchive.Parse(pack.GetFileByName(archiveMember), itemIdsByHash);
                    areaCount++;
                    records.AddRange(FlattenArchive(
                        archiveMember,
                        archive,
                        zoneNames,
                        objectNames,
                        hashLabels,
                        itemHashes,
                        itemDisplayNames,
                        provenance));
                }
                catch (InvalidDataException exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Placement area '{archiveMember}' could not be decoded: {exception.Message}",
                        file: PlacementDataPath,
                        expected: "Sword/Shield PlacementZoneArchive member"));
                }
            }

            return CreateWorkflow(
                summary,
                records
                    .OrderBy(record => record.ArchiveMember, StringComparer.Ordinal)
                    .ThenBy(record => record.ZoneIndex)
                    .ThenBy(record => record.ObjectType, StringComparer.Ordinal)
                    .ThenBy(record => record.ObjectIndex)
                    .ThenBy(record => record.ChanceIndex ?? -1)
                    .ToArray(),
                areaCount,
                sourceFileCount: 1 + itemNameSourceCount + itemHashSourceCount,
                itemDisplayNames,
                itemHashes,
                diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement data source is not a supported Sword/Shield placement pack: {exception.Message}",
                file: placementSource.GraphEntry.RelativePath,
                expected: "Sword/Shield placement.gfpak"));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 1, itemDisplayNames, itemHashes, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement data source could not be read: {exception.Message}",
                file: placementSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield placement.gfpak"));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 1, itemDisplayNames, itemHashes, diagnostics);
        }
    }

    internal static WorkflowFileSource? ResolvePlacementDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, PlacementDataPath);
    }

    internal static WorkflowFileSource? ResolveItemHashSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, ItemHashPath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRootWithSeparator = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;

        return targetPath.StartsWith(outputRootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? targetPath
            : null;
    }

    internal static string CreateObjectRecordId(
        string archiveMember,
        int zoneIndex,
        string objectType,
        int objectIndex,
        int? chanceIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{archiveMember}|{zoneIndex}|{objectType}|{objectIndex}|{chanceIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
    }

    internal static bool TryParseObjectRecordId(
        string objectId,
        out string archiveMember,
        out int zoneIndex,
        out string objectType,
        out int objectIndex,
        out int? chanceIndex)
    {
        archiveMember = string.Empty;
        zoneIndex = 0;
        objectType = string.Empty;
        objectIndex = 0;
        chanceIndex = null;

        var parts = objectId.Split('|');
        if (parts.Length != 5)
        {
            return false;
        }

        archiveMember = parts[0];
        objectType = parts[2];
        if (string.IsNullOrWhiteSpace(archiveMember)
            || string.IsNullOrWhiteSpace(objectType)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out zoneIndex)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out objectIndex))
        {
            return false;
        }

        if (parts[4] != "-")
        {
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedChanceIndex))
            {
                return false;
            }

            chanceIndex = parsedChanceIndex;
        }

        return true;
    }

    private static IReadOnlyList<SwShPlacedObjectRecord> FlattenArchive(
        string archiveMember,
        SwShPlacementZoneArchive archive,
        IReadOnlyDictionary<ulong, string> zoneNames,
        IReadOnlyDictionary<ulong, string> objectNames,
        IReadOnlyDictionary<ulong, string> hashLabels,
        IReadOnlyDictionary<int, ulong> itemHashes,
        IReadOnlyList<string> itemNames,
        SwShPlacementProvenance provenance)
    {
        var itemIdsByHash = CreateItemIdsByHash(itemHashes);
        var records = new List<SwShPlacedObjectRecord>();
        foreach (var zone in archive.Zones)
        {
            var map = ResolveZoneName(zone, zoneNames);
            var rawObjects = zone.RawObjects
                .GroupBy(rawObject => (rawObject.ObjectType, rawObject.ObjectIndex))
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var fieldItem in zone.FieldItems)
            {
                rawObjects.TryGetValue(("FieldItem", fieldItem.ObjectIndex), out var rawObject);
                var itemId = ResolveFieldItemId(fieldItem, itemIdsByHash);
                var itemHash = fieldItem.ItemHashes.FirstOrDefault();
                var itemName = ResolveItemName(itemId, itemNames);
                records.Add(CreatePlacedObjectRecord(
                    CreateObjectRecordId(archiveMember, zone.ZoneIndex, "fieldItem", fieldItem.ObjectIndex, null),
                    "FieldItem",
                    itemName == "None" ? "Field item" : $"Field item: {itemName}",
                    map,
                    archiveMember,
                    zone.ZoneIndex,
                    fieldItem.ObjectIndex,
                    ChanceIndex: null,
                    itemId is null ? null : (uint)itemId.Value,
                    itemName,
                    FormatHash(itemHash),
                    fieldItem.Quantity,
                    Chance: null,
                    fieldItem.Transform.X,
                    fieldItem.Transform.Y,
                    fieldItem.Transform.Z,
                    fieldItem.Transform.RotationY,
                    string.IsNullOrWhiteSpace(fieldItem.Model) ? ResolveObjectName(zone.ObjectHash, objectNames) : CleanPath(fieldItem.Model),
                    provenance,
                    CreateFieldItemFields(fieldItem, itemId, itemName, itemHash, rawObject, hashLabels, itemIdsByHash, itemNames),
                    itemUsesHashStorage: fieldItem.ItemHashOffsets.Count > 0,
                    itemUsesDirectIdStorage: fieldItem.ItemHashOffsets.Count == 0
                        && fieldItem.ItemIdOffsets.Count > 0));
            }

            foreach (var hiddenItem in zone.HiddenItems)
            {
                rawObjects.TryGetValue(("HiddenItem", hiddenItem.ObjectIndex), out var rawObject);
                foreach (var chance in hiddenItem.Chances)
                {
                    var itemName = ResolveItemName(chance.ItemId, itemNames);
                    records.Add(CreatePlacedObjectRecord(
                        CreateObjectRecordId(archiveMember, zone.ZoneIndex, "hiddenItem", hiddenItem.ObjectIndex, chance.ChanceIndex),
                        "HiddenItem",
                        itemName == "None" ? "Hidden item" : $"Hidden item: {itemName}",
                        map,
                        archiveMember,
                        zone.ZoneIndex,
                        hiddenItem.ObjectIndex,
                        chance.ChanceIndex,
                        chance.ItemId is null ? null : (uint)chance.ItemId.Value,
                        itemName,
                        FormatHash(chance.ItemHash),
                        chance.Quantity,
                        chance.Chance,
                        hiddenItem.Transform.X,
                        hiddenItem.Transform.Y,
                        hiddenItem.Transform.Z,
                        hiddenItem.Transform.RotationY,
                        ResolveObjectName(zone.ObjectHash, objectNames),
                        provenance,
                        CreateHiddenItemFields(hiddenItem, chance, itemName, rawObject, hashLabels, itemIdsByHash, itemNames),
                        itemUsesHashStorage: chance.ItemHashOffset > 0));
                }
            }

            foreach (var rawObject in zone.RawObjects
                .Where(rawObject => rawObject.ObjectType is not ("FieldItem" or "HiddenItem")))
            {
                records.Add(CreatePlacedObjectRecord(
                    CreateObjectRecordId(archiveMember, zone.ZoneIndex, rawObject.ObjectType, rawObject.ObjectIndex, null),
                    rawObject.ObjectType,
                    CreateRawObjectLabel(rawObject, hashLabels, itemIdsByHash, itemNames),
                    map,
                    archiveMember,
                    zone.ZoneIndex,
                    rawObject.ObjectIndex,
                    ChanceIndex: null,
                    ItemId: null,
                    itemName: string.Empty,
                    itemHash: string.Empty,
                    Quantity: 0,
                    Chance: null,
                    rawObject.Transform.X,
                    rawObject.Transform.Y,
                    rawObject.Transform.Z,
                    rawObject.Transform.RotationY,
                    string.IsNullOrWhiteSpace(rawObject.LinkValue)
                        ? ResolveObjectName(rawObject.ObjectHash, objectNames)
                        : rawObject.LinkValue,
                    provenance,
                    ConvertRawFields(
                        rawObject.Fields,
                        hashLabels,
                        itemIdsByHash,
                        itemNames,
                        runtimeOwnsScaleAndRotation: IsWr02Hoeruo(rawObject, hashLabels))));
            }
        }

        return records;
    }

    private static IReadOnlyDictionary<ulong, int> CreateItemIdsByHash(
        IReadOnlyDictionary<int, ulong> itemHashes)
    {
        return itemHashes
            .Where(entry => entry.Value != 0)
            .OrderBy(entry => entry.Key)
            .GroupBy(entry => entry.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);
    }

    private static IReadOnlyList<SwShPlacementFieldValue> ConvertRawFields(
        IReadOnlyList<SwShPlacementRawField> rawFields,
        IReadOnlyDictionary<ulong, string> hashLabels,
        IReadOnlyDictionary<ulong, int> itemIdsByHash,
        IReadOnlyList<string> itemNames,
        bool runtimeOwnsScaleAndRotation = false,
        Func<SwShPlacementRawField, bool>? includeField = null)
    {
        return rawFields
            .Where(field => includeField?.Invoke(field) ?? true)
            .Select(field =>
            {
                var isRuntimeOwned = runtimeOwnsScaleAndRotation
                    && field.Group == "Transform"
                    && (field.Label.StartsWith("Scale ", StringComparison.Ordinal)
                        || field.Label.StartsWith("Rotation ", StringComparison.Ordinal));
                return new SwShPlacementFieldValue(
                    field.Field,
                    field.Label,
                    field.Group,
                    field.Value,
                    ResolveRawDisplayValue(field, hashLabels, itemIdsByHash, itemNames),
                    field.IsReadOnly || isRuntimeOwned,
                    field.ValueKind,
                    field.MinimumValue,
                    field.MaximumValue,
                    isRuntimeOwned
                        ? "Runtime-owned by the wr02_hoeruo Wailord AI. The game reapplies model scale 5 and rotation Y -74 after this placement spawner loads."
                        : field.Description);
            })
            .ToArray();
    }

    private static SwShPlacementFieldValue CanonicalNumberField(
        string field,
        string label,
        string group,
        double value,
        bool isStored,
        double minimumValue,
        double maximumValue)
    {
        var formatted = FormatNumber(value);
        return new SwShPlacementFieldValue(
            field,
            label,
            group,
            formatted,
            formatted,
            IsReadOnly: !isStored,
            ValueKind: "number",
            MinimumValue: minimumValue,
            MaximumValue: maximumValue,
            Description: GetCanonicalStorageDescription(isStored));
    }

    private static SwShPlacementFieldValue CanonicalIntegerField(
        string field,
        string label,
        string group,
        int? value,
        string displayValue,
        bool isStored,
        int maximumValue,
        IReadOnlyList<SwShPlacementEditableFieldOption>? options = null)
    {
        return new SwShPlacementFieldValue(
            field,
            label,
            group,
            value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            displayValue,
            IsReadOnly: !isStored,
            ValueKind: "integer",
            MinimumValue: 0,
            MaximumValue: maximumValue,
            Description: GetCanonicalStorageDescription(isStored),
            Options: options);
    }

    private static SwShPlacementFieldValue CanonicalIntegerField(
        string field,
        string label,
        string group,
        int value,
        bool isStored,
        int maximumValue)
    {
        var formatted = value.ToString(CultureInfo.InvariantCulture);
        return CanonicalIntegerField(field, label, group, value, formatted, isStored, maximumValue);
    }

    private static string GetCanonicalStorageDescription(bool isStored)
    {
        return isStored
            ? "Stored directly in this placement object and editable without rebuilding its FlatBuffer table."
            : "Read-only because this scalar is omitted from the placement object's FlatBuffer table and cannot be patched safely in place.";
    }

    private static SwShPlacementFieldValue ReadOnlyField(
        string field,
        string label,
        string group,
        string value,
        string displayValue)
    {
        return new SwShPlacementFieldValue(
            field,
            label,
            group,
            value,
            displayValue,
            IsReadOnly: true,
            Description: "Reference value displayed for context; edit the mapped field when available.");
    }

    private static string ResolveRawDisplayValue(
        SwShPlacementRawField field,
        IReadOnlyDictionary<ulong, string> hashLabels,
        IReadOnlyDictionary<ulong, int> itemIdsByHash,
        IReadOnlyList<string> itemNames)
    {
        return ResolveRawDisplayValue(field.Value, field.DisplayValue, field.Label, field.Field, hashLabels, itemIdsByHash, itemNames);
    }

    private static string ResolveRawDisplayValue(
        string value,
        string displayValue,
        string label,
        string field,
        IReadOnlyDictionary<ulong, string> hashLabels,
        IReadOnlyDictionary<ulong, int> itemIdsByHash,
        IReadOnlyList<string> itemNames)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.IsNullOrWhiteSpace(displayValue) ? "None" : displayValue;
        }

        if (!TryParseHash(value, out var hash))
        {
            return displayValue;
        }

        if (hash == 0)
        {
            return "None";
        }

        if (hash == SwShPlacementZoneArchive.EmptyFnvHash)
        {
            return "None (empty hash)";
        }

        if (label.Contains("Item", StringComparison.OrdinalIgnoreCase)
            || field.Contains("FieldItem", StringComparison.Ordinal)
            || field.Contains("HiddenItem", StringComparison.Ordinal)
            || field.Contains("BerryTree", StringComparison.Ordinal))
        {
            var itemDisplay = ResolveItemHashDisplay(hash, itemIdsByHash, itemNames);
            if (!string.IsNullOrWhiteSpace(itemDisplay))
            {
                return itemDisplay;
            }
        }

        if (hashLabels.TryGetValue(hash, out var hashLabel) && !string.IsNullOrWhiteSpace(hashLabel))
        {
            return $"{CleanPath(hashLabel)} ({FormatHash(hash)})";
        }

        return displayValue;
    }

    private static string ResolveItemHashDisplay(
        ulong hash,
        IReadOnlyDictionary<ulong, int> itemIdsByHash,
        IReadOnlyList<string> itemNames)
    {
        if (hash == 0)
        {
            return "None";
        }

        if (hash == SwShPlacementZoneArchive.EmptyFnvHash)
        {
            return "None (empty hash)";
        }

        return itemIdsByHash.TryGetValue(hash, out var itemId)
            ? $"{ResolveItemName(itemId, itemNames)} ({itemId.ToString(CultureInfo.InvariantCulture)})"
            : FormatHash(hash);
    }

    private static string CreateRawObjectLabel(
        SwShPlacementRawObject rawObject,
        IReadOnlyDictionary<ulong, string> hashLabels,
        IReadOnlyDictionary<ulong, int> itemIdsByHash,
        IReadOnlyList<string> itemNames)
    {
        var typeLabel = rawObject.ObjectType switch
        {
            "AdvancedTip" => "Advanced tip",
            "BerryTree" => "Berry tree",
            "Critter" => "Critter",
            "Environment" => "Environment",
            "FishingPoint" => "Fishing point",
            "FlyTo" => "Fly anchor",
            "IKStep" => "IK step",
            "Ladder" => "Ladder",
            "Nest" => "Raid den",
            "NPCType1" => "NPC",
            "NPCType2" => "NPC",
            "Particle" => "Particle",
            "Path" => "Path",
            "PokeCenterAnchor" => "Pokemon Center anchor",
            "Popup" => "Popup",
            "Quadrant" => "Quadrant",
            "RotomRally" => "Rotom Rally",
            "StaticObject" => "Static Pokemon",
            "StepJump" => "Step jump",
            "Symbol" => "Symbol spawn",
            "Trainer" => "Trainer",
            "TrainerTip" => "Trainer tip",
            "Trigger" => "Trigger",
            "UnitObject" => "World object",
            "Warp" => "Warp",
            _ => rawObject.ObjectType,
        };

        if (rawObject.ObjectType == "StaticObject")
        {
            var spawnLabels = rawObject.Fields
                .Where(field => field.Field.Contains(".Spawns[", StringComparison.Ordinal)
                    && field.Field.EndsWith(".SpawnID", StringComparison.Ordinal))
                .Select(field => ResolveRawDisplayValue(field, hashLabels, itemIdsByHash, itemNames))
                .Where(label => !IsEmptyRawDisplay(label))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (spawnLabels.Length > 0)
            {
                var displayedLabels = string.Join(", ", spawnLabels.Take(3));
                var overflow = spawnLabels.Length > 3
                    ? $", +{(spawnLabels.Length - 3).ToString(CultureInfo.InvariantCulture)} more"
                    : string.Empty;
                var count = spawnLabels.Length > 1
                    ? $" ({spawnLabels.Length.ToString(CultureInfo.InvariantCulture)} spawn IDs)"
                    : string.Empty;
                return $"{typeLabel}: {displayedLabels}{overflow}{count}";
            }
        }

        var primaryLabel = ResolveRawDisplayValue(
            rawObject.PrimaryLabel,
            rawObject.PrimaryLabel,
            typeLabel,
            rawObject.ObjectType,
            hashLabels,
            itemIdsByHash,
            itemNames);

        if (IsEmptyRawDisplay(primaryLabel) && rawObject.ObjectHash != 0)
        {
            primaryLabel = ResolveRawDisplayValue(
                FormatHash(rawObject.ObjectHash),
                FormatHash(rawObject.ObjectHash),
                typeLabel,
                rawObject.ObjectType,
                hashLabels,
                itemIdsByHash,
                itemNames);
        }

        return string.IsNullOrWhiteSpace(primaryLabel)
            || primaryLabel == rawObject.ObjectType
            || IsEmptyRawDisplay(primaryLabel)
            ? $"{typeLabel} {rawObject.ObjectIndex.ToString(CultureInfo.InvariantCulture)}"
            : $"{typeLabel}: {primaryLabel}";
    }

    private static bool IsEmptyRawDisplay(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.Equals("None", StringComparison.OrdinalIgnoreCase)
            || value.Equals("None (empty hash)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWr02Hoeruo(
        SwShPlacementRawObject rawObject,
        IReadOnlyDictionary<ulong, string> hashLabels)
    {
        if (rawObject.ObjectHash == Wr02HoeruoObjectHash)
        {
            return true;
        }

        return hashLabels.TryGetValue(rawObject.ObjectHash, out var label)
            && label.Contains("wr02_hoeruo", StringComparison.OrdinalIgnoreCase);
    }

    private static PlacementCategoryInfo ResolveCategory(string objectType)
    {
        return CategoryByObjectType.TryGetValue(objectType, out var category)
            ? category
            : new PlacementCategoryInfo("pathsTechnical", "Paths & Technical", "Technical placement records.");
    }

    private static SwShPlacedObjectRecord CreatePlacedObjectRecord(
        string objectId,
        string objectType,
        string label,
        string map,
        string archiveMember,
        int zoneIndex,
        int objectIndex,
        int? ChanceIndex,
        uint? ItemId,
        string itemName,
        string itemHash,
        int Quantity,
        int? Chance,
        double x,
        double y,
        double z,
        double rotationY,
        string? scriptId,
        SwShPlacementProvenance provenance,
        IReadOnlyList<SwShPlacementFieldValue> fields,
        bool itemUsesHashStorage = false,
        bool itemUsesDirectIdStorage = false)
    {
        var category = ResolveCategory(objectType);
        var placedObject = new SwShPlacedObjectRecord(
            objectId,
            objectType,
            label,
            map,
            archiveMember,
            zoneIndex,
            objectIndex,
            ChanceIndex,
            ItemId,
            itemName,
            itemHash,
            Quantity,
            Chance,
            x,
            y,
            z,
            rotationY,
            scriptId,
            provenance,
            category.Id,
            category.Label,
            fields,
            itemUsesHashStorage,
            itemUsesDirectIdStorage);
        return WithRefreshedPreviewText(placedObject);
    }

    internal static SwShPlacedObjectRecord WithRefreshedPreviewText(
        SwShPlacedObjectRecord placedObject)
    {
        ArgumentNullException.ThrowIfNull(placedObject);
        return placedObject with { PreviewText = CreatePlacementPreviewText(placedObject) };
    }

    private static string CreatePlacementPreviewText(SwShPlacedObjectRecord placedObject)
    {
        if (FindPlacementPreviewField(placedObject) is { } previewField)
        {
            return GetPlacementDisplayValue(previewField);
        }

        if (string.Equals(placedObject.CategoryId, "pokemonSpawners", StringComparison.Ordinal))
        {
            return placedObject.Label;
        }

        if (string.Equals(placedObject.CategoryId, "itemBallSpawners", StringComparison.Ordinal))
        {
            return FirstNonempty(placedObject.ItemName, placedObject.Label);
        }

        if (!string.IsNullOrEmpty(placedObject.ScriptId))
        {
            return placedObject.ScriptId;
        }

        return placedObject.ItemId is null
            ? FirstNonempty(placedObject.ItemHash, placedObject.ItemName, placedObject.ObjectType)
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{placedObject.ItemName} ({placedObject.ItemId.Value})");
    }

    private static SwShPlacementFieldValue? FindPlacementPreviewField(
        SwShPlacedObjectRecord placedObject)
    {
        var fields = placedObject.Fields ?? [];
        var species = fields.FirstOrDefault(field =>
                field.Field.EndsWith(".speciesId", StringComparison.Ordinal))
            ?? fields.FirstOrDefault(field =>
                field.Field.EndsWith(".Species", StringComparison.Ordinal));
        if (species is not null)
        {
            return species;
        }

        if (string.Equals(placedObject.CategoryId, "pokemonSpawners", StringComparison.Ordinal)
            || string.Equals(placedObject.CategoryId, "itemBallSpawners", StringComparison.Ordinal))
        {
            return null;
        }

        var table = fields.FirstOrDefault(field =>
                field.Field.EndsWith(".tableKey", StringComparison.Ordinal))
            ?? fields.FirstOrDefault(field =>
                field.Field.EndsWith(".label", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                field.Label.Contains("Static Encounter", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                field.Label.Contains("Symbol Encounter", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                field.Label.Contains("Raid Table", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                field.Label.Contains("Trainer Battle", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                field.Label.Contains("Object Hash", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                field.Label.Contains("Model Hash", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                field.Label.Contains("Message Hash", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                string.Equals(field.Group, "References", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field));
        if (table is not null)
        {
            return table;
        }

        var model = fields.FirstOrDefault(field =>
                string.Equals(field.Label, "Model", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field))
            ?? fields.FirstOrDefault(field =>
                string.Equals(field.Label, "Model Hash", StringComparison.Ordinal)
                && HasUsefulPlacementDisplay(field));
        if (model is not null)
        {
            return model;
        }

        return null;
    }

    private static string GetPlacementDisplayValue(SwShPlacementFieldValue field)
    {
        return string.IsNullOrEmpty(field.DisplayValue) ? field.Value : field.DisplayValue;
    }

    private static bool HasUsefulPlacementDisplay(SwShPlacementFieldValue field)
    {
        var value = GetPlacementDisplayValue(field).Trim();
        return value.Length > 0
            && !string.Equals(value, "None", StringComparison.Ordinal)
            && !string.Equals(value, "None (empty hash)", StringComparison.Ordinal)
            && !string.Equals(value, "0xCBF29CE484222645", StringComparison.Ordinal);
    }

    private static string FirstNonempty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? string.Empty;
    }

    private static IReadOnlyList<SwShPlacementFieldValue> CreateFieldItemFields(
        SwShPlacementFieldItem fieldItem,
        int? itemId,
        string itemName,
        ulong itemHash,
        SwShPlacementRawObject? rawObject,
        IReadOnlyDictionary<ulong, string> hashLabels,
        IReadOnlyDictionary<ulong, int> itemIdsByHash,
        IReadOnlyList<string> itemNames)
    {
        var directItemOptions = fieldItem.ItemHashOffsets.Count == 0
            && fieldItem.ItemIdOffsets.Count > 0
                ? CreateItemOptions(itemNames, _ => true)
                : null;
        var fields = new List<SwShPlacementFieldValue>
        {
            CanonicalNumberField(LocationXField, "X", "Transform", fieldItem.Transform.X, fieldItem.TransformOffsets.X > 0, MinimumCoordinate, MaximumCoordinate),
            CanonicalNumberField(LocationYField, "Y", "Transform", fieldItem.Transform.Y, fieldItem.TransformOffsets.Y > 0, MinimumCoordinate, MaximumCoordinate),
            CanonicalNumberField(LocationZField, "Z", "Transform", fieldItem.Transform.Z, fieldItem.TransformOffsets.Z > 0, MinimumCoordinate, MaximumCoordinate),
            CanonicalNumberField(RotationYField, "Rotation Y", "Transform", fieldItem.Transform.RotationY, fieldItem.TransformOffsets.RotationY > 0, MinimumRotation, MaximumRotation),
            CanonicalIntegerField(ItemIdField, "Item", "Item", itemId, itemName, fieldItem.ItemHashOffsets.Count > 0 || fieldItem.ItemIdOffsets.Count > 0, MaximumItemId, directItemOptions),
            CanonicalIntegerField(QuantityField, "Quantity", "Item", fieldItem.Quantity, fieldItem.QuantityOffset > 0, MaximumFieldItemQuantity),
            ReadOnlyField("fieldItem.hash", "Item Hash", "Item", FormatHash(itemHash), ResolveItemHashDisplay(itemHash, itemIdsByHash, itemNames)),
        };

        if (rawObject is not null)
        {
            fields.AddRange(ConvertRawFields(
                rawObject.Fields,
                hashLabels,
                itemIdsByHash,
                itemNames,
                includeField: field => !IsFieldItemCanonicalRawAlias(
                    field,
                    usesHashStorage: fieldItem.ItemHashOffsets.Count > 0,
                    usesDirectIdStorage: fieldItem.ItemHashOffsets.Count == 0
                        && fieldItem.ItemIdOffsets.Count > 0)));
        }

        return fields;
    }

    private static IReadOnlyList<SwShPlacementFieldValue> CreateHiddenItemFields(
        SwShPlacementHiddenItem hiddenItem,
        SwShPlacementHiddenItemChance chance,
        string itemName,
        SwShPlacementRawObject? rawObject,
        IReadOnlyDictionary<ulong, string> hashLabels,
        IReadOnlyDictionary<ulong, int> itemIdsByHash,
        IReadOnlyList<string> itemNames)
    {
        var fields = new List<SwShPlacementFieldValue>
        {
            CanonicalNumberField(LocationXField, "X", "Transform", hiddenItem.Transform.X, hiddenItem.TransformOffsets.X > 0, MinimumCoordinate, MaximumCoordinate),
            CanonicalNumberField(LocationYField, "Y", "Transform", hiddenItem.Transform.Y, hiddenItem.TransformOffsets.Y > 0, MinimumCoordinate, MaximumCoordinate),
            CanonicalNumberField(LocationZField, "Z", "Transform", hiddenItem.Transform.Z, hiddenItem.TransformOffsets.Z > 0, MinimumCoordinate, MaximumCoordinate),
            CanonicalNumberField(RotationYField, "Rotation Y", "Transform", hiddenItem.Transform.RotationY, hiddenItem.TransformOffsets.RotationY > 0, MinimumRotation, MaximumRotation),
            CanonicalIntegerField(ItemIdField, "Item", "Item", chance.ItemId, itemName, chance.ItemHashOffset > 0, MaximumItemId),
            CanonicalIntegerField(QuantityField, "Quantity", "Item", chance.Quantity, chance.QuantityOffset > 0, MaximumQuantity),
            CanonicalIntegerField(ChanceField, "Chance", "Item", chance.Chance, chance.ChanceOffset > 0, MaximumChance),
            ReadOnlyField("hiddenItem.chanceIndex", "Chance Slot", "Item", chance.ChanceIndex.ToString(CultureInfo.InvariantCulture), chance.ChanceIndex.ToString(CultureInfo.InvariantCulture)),
            ReadOnlyField("hiddenItem.hash", "Item Hash", "Item", FormatHash(chance.ItemHash), ResolveItemHashDisplay(chance.ItemHash, itemIdsByHash, itemNames)),
        };

        if (rawObject is not null)
        {
            fields.AddRange(ConvertRawFields(
                rawObject.Fields,
                hashLabels,
                itemIdsByHash,
                itemNames,
                includeField: field => !IsHiddenItemCanonicalRawAlias(field)));
        }

        return fields;
    }

    private static bool IsFieldItemCanonicalRawAlias(
        SwShPlacementRawField field,
        bool usesHashStorage,
        bool usesDirectIdStorage)
    {
        return IsCanonicalTransformAlias(field)
            || (usesHashStorage && field.Field.EndsWith(".Flags[0]", StringComparison.Ordinal))
            || (usesDirectIdStorage && field.Field.EndsWith(".Items[0]", StringComparison.Ordinal))
            || field.Field.EndsWith(".Quantity", StringComparison.Ordinal);
    }

    private static bool IsHiddenItemCanonicalRawAlias(SwShPlacementRawField field)
    {
        return IsCanonicalTransformAlias(field)
            || field.Field.Contains(".Field_02[", StringComparison.Ordinal);
    }

    private static bool IsCanonicalTransformAlias(SwShPlacementRawField field)
    {
        return field.Group == "Transform"
            && field.Label is "X" or "Y" or "Z" or "Rotation Y";
    }

    private static int? ResolveFieldItemId(
        SwShPlacementFieldItem fieldItem,
        IReadOnlyDictionary<ulong, int> itemIdsByHash)
    {
        if (fieldItem.ItemHashes.Count > 0 && itemIdsByHash.TryGetValue(fieldItem.ItemHashes[0], out var itemId))
        {
            return itemId;
        }

        if (fieldItem.ItemHashes.Count == 0 && fieldItem.ItemIds.Count > 0)
        {
            var directItemId = fieldItem.ItemIds[0];
            return directItemId <= MaximumItemId ? (int)directItemId : null;
        }

        return null;
    }

    private static string ResolveZoneName(
        SwShPlacementZone zone,
        IReadOnlyDictionary<ulong, string> zoneNames)
    {
        if (zoneNames.TryGetValue(zone.ZoneId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return zone.ZoneId == 0 ? $"Zone {zone.ZoneIndex}" : FormatHash(zone.ZoneId);
    }

    private static string ResolveObjectName(
        ulong objectHash,
        IReadOnlyDictionary<ulong, string> objectNames)
    {
        if (objectNames.TryGetValue(objectHash, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return CleanPath(name);
        }

        return objectHash == 0 ? string.Empty : FormatHash(objectHash);
    }

    private static string ResolveItemName(int? itemId, IReadOnlyList<string> itemNames)
    {
        if (itemId is null)
        {
            return "None";
        }

        return (uint)itemId.Value < (uint)itemNames.Count && !string.IsNullOrWhiteSpace(itemNames[itemId.Value])
            ? itemNames[itemId.Value]
            : string.Create(CultureInfo.InvariantCulture, $"Item {itemId.Value}");
    }

    private static string CleanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash >= 0 && slash + 1 < normalized.Length)
        {
            normalized = normalized[(slash + 1)..];
        }

        var dot = normalized.LastIndexOf('.');
        if (dot > 0)
        {
            normalized = normalized[..dot];
        }

        return normalized;
    }

    private static string FormatHash(ulong hash)
    {
        return hash == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"0x{hash:X16}");
    }

    private static string FormatNumber(double value)
    {
        var floatValue = (float)value;
        return floatValue == 0
            ? "0"
            : floatValue.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static bool TryParseHash(string value, out ulong hash)
    {
        hash = 0;
        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ulong.TryParse(
            value[2..],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out hash);
    }

    private static IReadOnlyDictionary<ulong, string> LoadRequiredHashTable(
        SwShGfPackFile pack,
        string memberName)
    {
        return SwShAhtbFile.Parse(pack.GetFileByName(memberName)).ToDictionary();
    }

    private static IReadOnlyDictionary<ulong, string> LoadOptionalHashTable(
        SwShGfPackFile pack,
        string memberName,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!pack.ContainsFileName(memberName))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Placement label table '{memberName}' is not present; hash fallback labels will be shown.",
                file: PlacementDataPath,
                expected: memberName));
            return new Dictionary<ulong, string>();
        }

        try
        {
            return SwShAhtbFile.Parse(pack.GetFileByName(memberName)).ToDictionary();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Placement label table '{memberName}' could not be decoded: {exception.Message}",
                file: PlacementDataPath,
                expected: "Sword/Shield placement AHTB"));
            return new Dictionary<ulong, string>();
        }
    }

    private static IReadOnlyDictionary<ulong, string> LoadPlacementHashLabels(
        OpenedProject project,
        SwShGfPackFile pack,
        IReadOnlyDictionary<ulong, string> areaNames,
        IReadOnlyDictionary<ulong, string> zoneNames,
        IReadOnlyDictionary<ulong, string> objectNames,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var labels = new Dictionary<ulong, string>();
        AddHashLabels(labels, areaNames);
        AddHashLabels(labels, zoneNames);
        AddHashLabels(labels, objectNames);

        if (pack.ContainsFileName(VanishFlagAutoTableMember))
        {
            try
            {
                AddHashLabels(labels, SwShAhtbFile.Parse(pack.GetFileByName(VanishFlagAutoTableMember)).ToDictionary());
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Placement vanish flag table could not be decoded: {exception.Message}",
                    file: PlacementDataPath,
                    expected: "Sword/Shield placement AHTB"));
            }
        }

        foreach (var source in project.FileGraph.Entries
            .Where(entry =>
                entry.RelativePath.StartsWith(FlagworkRootPath, StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ResolveWorkflowFile(project, entry.RelativePath))
            .Where(source => source is not null)
            .Cast<WorkflowFileSource>())
        {
            AddWorkflowHashLabels(labels, source, diagnostics);
        }

        var trainerIdSource = ResolveWorkflowFile(project, TrainerIdHashTablePath);
        if (trainerIdSource is not null)
        {
            AddWorkflowHashLabels(labels, trainerIdSource, diagnostics);
        }

        AddStaticEncounterHashLabels(labels, project, diagnostics);

        return labels;
    }

    private static void AddStaticEncounterHashLabels(
        IDictionary<ulong, string> labels,
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (SwShStaticEncountersWorkflowService.ResolveStaticEncounterDataSource(project) is null)
        {
            return;
        }

        var staticWorkflow = new SwShStaticEncountersWorkflowService().Load(project);
        foreach (var diagnostic in staticWorkflow.Diagnostics)
        {
            diagnostics.Add(CreateDiagnostic(
                diagnostic.Severity == DiagnosticSeverity.Error
                    ? DiagnosticSeverity.Warning
                    : diagnostic.Severity,
                $"Static Encounter labels: {diagnostic.Message}",
                file: diagnostic.File,
                field: diagnostic.Field,
                expected: diagnostic.Expected,
                code: diagnostic.Code));
        }

        foreach (var encounter in staticWorkflow.Encounters)
        {
            if (!TryParseHash(encounter.EncounterId, out var encounterId)
                || encounterId == 0
                || string.IsNullOrWhiteSpace(encounter.Label)
                || labels.ContainsKey(encounterId))
            {
                continue;
            }

            labels.Add(encounterId, encounter.Label);
        }
    }

    private static void AddWorkflowHashLabels(
        IDictionary<ulong, string> labels,
        WorkflowFileSource source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            AddHashLabels(labels, SwShAhtbFile.Parse(File.ReadAllBytes(source.AbsolutePath)).ToDictionary());
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Placement label table '{source.GraphEntry.RelativePath}' could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield AHTB"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Placement label table '{source.GraphEntry.RelativePath}' could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield AHTB"));
        }
    }

    private static void AddHashLabels(
        IDictionary<ulong, string> labels,
        IReadOnlyDictionary<ulong, string> additions)
    {
        foreach (var (hash, label) in additions)
        {
            if (hash == 0 || string.IsNullOrWhiteSpace(label) || labels.ContainsKey(hash))
            {
                continue;
            }

            labels.Add(hash, label);
        }
    }

    private static string[] LoadItemNames(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        out int sourceFileCount)
    {
        var source = ResolveItemNamesSource(project, diagnostics);
        if (source is null)
        {
            sourceFileCount = 0;
            return [];
        }

        sourceFileCount = 1;
        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item name table could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield itemname.dat"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item name table could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield itemname.dat"));
            return [];
        }
    }

    private static Dictionary<int, ulong> LoadItemHashes(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        out int sourceFileCount)
    {
        var source = ResolveItemHashSource(project);
        if (source is null)
        {
            sourceFileCount = 0;
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Item hash table is not available; hash-coded placement item IDs cannot be edited by item ID.",
                expected: ItemHashPath));
            return [];
        }

        sourceFileCount = 1;
        try
        {
            return SwShItemHashTable.Parse(File.ReadAllBytes(source.AbsolutePath)).ToHashByItemId();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item hash table could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield item_hash_to_index.dat"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item hash table could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield item_hash_to_index.dat"));
            return [];
        }
    }

    private static WorkflowFileSource? ResolveItemNamesSource(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var fallback = ResolveCommonTextSource(project, "itemname.dat");

        if (fallback is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Item names are not available; item IDs will be shown as fallback names.",
                expected: "romfs/bin/message/{language}/common/itemname.dat"));
            return null;
        }

        return fallback;
    }

    private static WorkflowFileSource? ResolveCommonTextSource(
        OpenedProject project,
        string fileName)
    {
        var language = SwShGameTextLanguage.Resolve(project.Paths);
        var preferred = ResolveWorkflowFile(project, SwShGameTextLanguage.CommonMessagePath(language, fileName));
        if (preferred is not null)
        {
            return preferred;
        }

        if (!string.Equals(language, SwShGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
        {
            var english = ResolveWorkflowFile(
                project,
                SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.English, fileName));
            if (english is not null)
            {
                return english;
            }
        }

        return project.FileGraph.Entries
            .Where(entry =>
                entry.RelativePath.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith($"/common/{fileName}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ResolveWorkflowFile(project, entry.RelativePath))
            .FirstOrDefault(source => source is not null);
    }

    private static SwShPlacementWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShPlacedObjectRecord> objects,
        int areaCount,
        int sourceFileCount,
        IReadOnlyList<string> itemNames,
        IReadOnlyDictionary<int, ulong> itemHashes,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShPlacementWorkflow(
            summary,
            objects,
            CreateEditableFields(itemNames, itemHashes),
            new SwShPlacementWorkflowStats(objects.Count, areaCount, sourceFileCount),
            diagnostics,
            CreateCategories(objects));
    }

    private static IReadOnlyList<SwShPlacementCategory> CreateCategories(
        IReadOnlyList<SwShPlacedObjectRecord> objects)
    {
        var counts = objects
            .GroupBy(record => record.CategoryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var infoById = objects
            .Select(record => ResolveCategory(record.ObjectType))
            .GroupBy(category => category.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return CategoryOrder
            .Where(counts.ContainsKey)
            .Select(categoryId =>
            {
                var info = infoById[categoryId];
                return new SwShPlacementCategory(info.Id, info.Label, info.Description, counts[categoryId]);
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShPlacementEditableField> CreateEditableFields(
        IReadOnlyList<string> itemNames,
        IReadOnlyDictionary<int, ulong> itemHashes)
    {
        var itemOptions = CreateItemOptions(
            itemNames,
            itemId => itemHashes.TryGetValue(itemId, out var hash) && hash != 0);

        return EditableFields
            .Select(field => field.Field == ItemIdField
                ? field with { Options = itemOptions }
                : field)
            .ToArray();
    }

    private static IReadOnlyList<SwShPlacementEditableFieldOption> CreateItemOptions(
        IReadOnlyList<string> itemNames,
        Func<int, bool> includeItem)
    {
        return itemNames
            .Select((name, index) => new SwShPlacementEditableFieldOption(
                index,
                string.IsNullOrWhiteSpace(name)
                    ? $"{index.ToString("000", CultureInfo.InvariantCulture)} Item {index}"
                    : $"{index.ToString("000", CultureInfo.InvariantCulture)} {name}"))
            .Where(option => includeItem(option.Value))
            .ToArray();
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShPlacementProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShPlacementProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private sealed class PlacementCatalogRuntimeEntry
    {
        private readonly Dictionary<int, LinkedListNode<int>> editingDetailNodes = [];
        private readonly LinkedList<int> editingDetailRecency = [];

        public PlacementCatalogRuntimeEntry(
            ProjectPaths paths,
            SwShPlacementCatalogCacheData data,
            IReadOnlyDictionary<string, int> objectIndexes,
            PlacementDetailContext? detailContext,
            SwShCacheSourceIdentity? cacheIdentity,
            IReadOnlyList<CatalogDependencyFingerprint> dependencyFingerprints)
        {
            Paths = paths;
            Data = data;
            ObjectIndexes = objectIndexes;
            DetailContext = detailContext;
            CacheIdentity = cacheIdentity;
            DependencyFingerprints = dependencyFingerprints;
            EditingSnapshot = data.EditingSnapshot;
        }

        public ProjectPaths Paths { get; }

        public SwShPlacementCatalogCacheData Data { get; }

        public IReadOnlyDictionary<string, int> ObjectIndexes { get; }

        public PlacementDetailContext? DetailContext { get; set; }

        public SwShCacheSourceIdentity? CacheIdentity { get; }

        public IReadOnlyList<CatalogDependencyFingerprint> DependencyFingerprints { get; }

        public SwShPlacementWorkflow EditingSnapshot { get; set; }

        public bool TouchEditingDetail(int objectIndex, out int evictedIndex)
        {
            evictedIndex = -1;
            if (editingDetailNodes.Remove(objectIndex, out var existingNode))
            {
                editingDetailRecency.Remove(existingNode);
            }

            editingDetailNodes[objectIndex] = editingDetailRecency.AddLast(objectIndex);
            if (editingDetailNodes.Count <= PlacementDetailCacheCapacity
                || editingDetailRecency.First is not { } oldest)
            {
                return false;
            }

            editingDetailRecency.RemoveFirst();
            editingDetailNodes.Remove(oldest.Value);
            evictedIndex = oldest.Value;
            return true;
        }
    }

    private sealed record PlacementDetailContext(
        SwShGfPackFile Pack,
        IReadOnlyDictionary<ulong, string> ZoneNames,
        IReadOnlyDictionary<ulong, string> ObjectNames,
        IReadOnlyDictionary<ulong, string> HashLabels,
        IReadOnlyDictionary<int, ulong> ItemHashes,
        IReadOnlyDictionary<ulong, int> ItemIdsByHash,
        IReadOnlyList<string> ItemDisplayNames,
        SwShPlacementProvenance Provenance);

    private sealed record PlacementArchiveCacheKey(string Revision, string ArchiveMember);

    private sealed record PlacementDetailCacheKey(string Revision, string ObjectId);

    private sealed record CatalogDependencySource(
        string RelativePath,
        WorkflowFileSource? Source);

    private sealed record CatalogSourceSnapshot(
        string Revision,
        SwShCacheSourceIdentity? CacheIdentity,
        IReadOnlyList<CatalogDependencyFingerprint> Fingerprints);

    private sealed record CatalogDependencyMetadata(
        bool IsPresent,
        string SourceLayer,
        string SourceState,
        string? AbsolutePath,
        string Length,
        string LastWriteTimeUtcTicks);

    private sealed record CatalogDependencyFingerprint(
        string RelativePath,
        bool IsPresent,
        string SourceLayer,
        string SourceState,
        string? AbsolutePath,
        string Length,
        string LastWriteTimeUtcTicks,
        string ContentSha256,
        ProjectFileLayer? CacheSourceLayer,
        long? CacheLength,
        DateTime? CacheLastWriteTimeUtc)
    {
        public bool IsCacheSource =>
            IsPresent
            && !string.IsNullOrWhiteSpace(AbsolutePath)
            && CacheSourceLayer is not null
            && CacheLength is >= 0
            && CacheLastWriteTimeUtc is not null
            && ContentSha256.Length == 64
            && ContentSha256.All(Uri.IsHexDigit);
    }

    private sealed class BoundedLruCache<TKey, TValue>
        where TKey : notnull
    {
        private readonly int capacity;
        private readonly Dictionary<TKey, CacheEntry> entries = [];
        private readonly LinkedList<TKey> recency = [];

        public BoundedLruCache(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
            this.capacity = capacity;
        }

        public bool TryGet(TKey key, out TValue? value)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                value = default;
                return false;
            }

            recency.Remove(entry.RecencyNode);
            recency.AddLast(entry.RecencyNode);
            value = entry.Value;
            return true;
        }

        public void Set(TKey key, TValue value)
        {
            if (entries.Remove(key, out var replaced))
            {
                recency.Remove(replaced.RecencyNode);
            }

            var node = recency.AddLast(key);
            entries[key] = new CacheEntry(value, node);
            while (entries.Count > capacity && recency.First is { } oldest)
            {
                recency.RemoveFirst();
                entries.Remove(oldest.Value);
            }
        }

        public void Clear()
        {
            entries.Clear();
            recency.Clear();
        }

        private sealed record CacheEntry(TValue Value, LinkedListNode<TKey> RecencyNode);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Placement,
            "Placement",
            "Placed objects, map coordinates, item pickups, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null,
        string? code = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Field: field,
            Domain: "workflow.placement",
            Expected: expected)
        {
            Code = code,
        };
    }
}

internal sealed record WorkflowFileSource(
    ProjectFileGraphEntry GraphEntry,
    string AbsolutePath);

internal sealed record PlacementCategoryInfo(
    string Id,
    string Label,
    string Description);
