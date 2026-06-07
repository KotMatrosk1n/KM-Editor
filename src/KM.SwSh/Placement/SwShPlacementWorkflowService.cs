// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Placement;

public sealed class SwShPlacementWorkflowService
{
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
    public const int MaximumQuantity = 999;
    public const int MaximumChance = 100;

    private const string AreaNameHashTableMember = "AreaNameHashTable.tbl";
    private const string ZoneNameHashTableMember = "ZoneNameHashTable.tbl";
    private const string ObjectNameHashTableMember = "ObjectNameHashTable.tbl";

    private static readonly IReadOnlyList<SwShPlacementEditableField> EditableFields =
    [
        new SwShPlacementEditableField(LocationXField, "X", "number", MinimumCoordinate, MaximumCoordinate),
        new SwShPlacementEditableField(LocationYField, "Y", "number", MinimumCoordinate, MaximumCoordinate),
        new SwShPlacementEditableField(LocationZField, "Z", "number", MinimumCoordinate, MaximumCoordinate),
        new SwShPlacementEditableField(RotationYField, "Rotation Y", "number", MinimumRotation, MaximumRotation),
        new SwShPlacementEditableField(ItemIdField, "Item ID", "integer", 0, MaximumItemId),
        new SwShPlacementEditableField(QuantityField, "Quantity", "integer", 0, MaximumQuantity),
        new SwShPlacementEditableField(ChanceField, "Chance", "integer", 0, MaximumChance),
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

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 0, diagnostics);
        }

        var placementSource = ResolvePlacementDataSource(project);
        if (placementSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Placement data is not available for this project.",
                expected: PlacementDataPath));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 0, diagnostics);
        }

        var itemNames = LoadItemNames(project, diagnostics, out var itemNameSourceCount);
        var itemHashes = LoadItemHashes(project, diagnostics, out var itemHashSourceCount);
        var itemIdsByHash = itemHashes.ToDictionary(entry => entry.Value, entry => entry.Key);

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(placementSource.AbsolutePath));
            var areaNames = LoadRequiredHashTable(pack, AreaNameHashTableMember);
            var zoneNames = LoadOptionalHashTable(pack, ZoneNameHashTableMember, diagnostics);
            var objectNames = LoadOptionalHashTable(pack, ObjectNameHashTableMember, diagnostics);
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
                        itemHashes,
                        itemNames,
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
                diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement data source is not a supported Sword/Shield placement pack: {exception.Message}",
                file: placementSource.GraphEntry.RelativePath,
                expected: "Sword/Shield placement.gfpak"));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 1, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement data source could not be read: {exception.Message}",
                file: placementSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield placement.gfpak"));
            return CreateWorkflow(summary, Array.Empty<SwShPlacedObjectRecord>(), areaCount: 0, sourceFileCount: 1, diagnostics);
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
        IReadOnlyDictionary<int, ulong> itemHashes,
        IReadOnlyList<string> itemNames,
        SwShPlacementProvenance provenance)
    {
        var itemIdsByHash = itemHashes.ToDictionary(entry => entry.Value, entry => entry.Key);
        var records = new List<SwShPlacedObjectRecord>();
        foreach (var zone in archive.Zones)
        {
            var map = ResolveZoneName(zone, zoneNames);
            foreach (var fieldItem in zone.FieldItems)
            {
                var itemId = ResolveFieldItemId(fieldItem, itemIdsByHash);
                var itemHash = fieldItem.ItemHashes.FirstOrDefault();
                var itemName = ResolveItemName(itemId, itemNames);
                records.Add(new SwShPlacedObjectRecord(
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
                    provenance));
            }

            foreach (var hiddenItem in zone.HiddenItems)
            {
                foreach (var chance in hiddenItem.Chances)
                {
                    var itemName = ResolveItemName(chance.ItemId, itemNames);
                    records.Add(new SwShPlacedObjectRecord(
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
                        provenance));
                }
            }
        }

        return records;
    }

    private static int? ResolveFieldItemId(
        SwShPlacementFieldItem fieldItem,
        IReadOnlyDictionary<ulong, int> itemIdsByHash)
    {
        if (fieldItem.ItemIds.Count > 0)
        {
            return checked((int)fieldItem.ItemIds[0]);
        }

        if (fieldItem.ItemHashes.Count > 0 && itemIdsByHash.TryGetValue(fieldItem.ItemHashes[0], out var itemId))
        {
            return itemId;
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
        var englishNames = ResolveWorkflowFile(project, EnglishItemNamePath);
        if (englishNames is not null)
        {
            return englishNames;
        }

        var fallback = project.FileGraph.Entries
            .Where(entry =>
                entry.RelativePath.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith("/common/itemname.dat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ResolveWorkflowFile(project, entry.RelativePath))
            .FirstOrDefault(source => source is not null);

        if (fallback is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Item names are not available; item IDs will be shown as fallback names.",
                expected: "romfs/bin/message/{language}/common/itemname.dat"));
            return null;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Warning,
            "English item names are not available; using another available item name table.",
            file: fallback.GraphEntry.RelativePath,
            expected: EnglishItemNamePath));

        return fallback;
    }

    private static SwShPlacementWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShPlacedObjectRecord> objects,
        int areaCount,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShPlacementWorkflow(
            summary,
            objects,
            EditableFields,
            new SwShPlacementWorkflowStats(objects.Count, areaCount, sourceFileCount),
            diagnostics);
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
