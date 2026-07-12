// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Workflows;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Placement;

public sealed class SwShPlacementWorkflowService
{
    private const ulong Wr02HoeruoObjectHash = 0x12E3C0CA0F529035;

    private static readonly string[] EditingSnapshotPrimaryTransformLabels =
        ["X", "Y", "Z", "Rotation Y"];

    private readonly ProjectWorkflowMemoryCache<SwShPlacementWorkflow> memoryCache = new();

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

    public void ClearMemoryCache()
    {
        memoryCache.Clear();
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

    private SwShPlacementWorkflow LoadUncached(OpenedProject project)
    {
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
        return new SwShPlacedObjectRecord(
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
                expected: diagnostic.Expected));
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
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Field: field,
            Domain: "workflow.placement",
            Expected: expected);
    }
}

internal sealed record WorkflowFileSource(
    ProjectFileGraphEntry GraphEntry,
    string AbsolutePath);

internal sealed record PlacementCategoryInfo(
    string Id,
    string Label,
    string Description);
