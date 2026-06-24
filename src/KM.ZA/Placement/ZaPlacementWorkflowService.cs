// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Placement;

internal sealed class ZaPlacementWorkflowService
{
    public const string PokemonSpawnersCategory = "pokemonSpawners";
    public const string ItemBallSpawnersCategory = "itemBallSpawners";

    public const string PositionXField = "point.positionX";
    public const string PositionYField = "point.positionY";
    public const string PositionZField = "point.positionZ";
    public const string RotationPitchField = "point.rotationPitch";
    public const string RotationYawField = "point.rotationYaw";
    public const string RotationRollField = "point.rotationRoll";
    public const string AttachTransformEnableField = "point.attachTransformEnable";

    private const string WorkflowLabel = "Placement";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A data-backed spawner placement transforms.";

    private static readonly CategorySeed[] CategorySeeds =
    [
        new(PokemonSpawnersCategory, "Pokemon Spawners", "Pokemon spawner transform rows joined to Pokemon spawner table context."),
        new(ItemBallSpawnersCategory, "Item Ball Spawners", "Item ball spawner transform rows."),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaPlacementWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Placement,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaPlacementWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        var objects = new List<ZaPlacedObjectRecord>();
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pokemonContext = LoadPokemonSpawnerContext(project, diagnostics, sourceFiles);

        TryLoadTransformCategory(
            project,
            ZaDataPaths.PokemonSpawnerTransformArray,
            PokemonSpawnersCategory,
            "Pokemon Spawner",
            pokemonContext,
            objects,
            sourceFiles,
            diagnostics);
        TryLoadTransformCategory(
            project,
            ZaDataPaths.ItemBallSpawnerTransformArray,
            ItemBallSpawnersCategory,
            "Item Ball Spawner",
            new Dictionary<string, ZaPlacementSpawnerContext>(StringComparer.Ordinal),
            objects,
            sourceFiles,
            diagnostics);

        if (sourceFiles.Count == 0)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                "Placement could not load any Pokemon Legends Z-A spawner transform tables.",
                $"romfs/{ZaDataPaths.PokemonSpawnerTransformArray}",
                expected: "At least one Z-A spawner transform table"));
        }

        var categories = CategorySeeds
            .Select(seed => new ZaPlacementCategory(
                seed.Id,
                seed.Label,
                seed.Description,
                objects.Count(placedObject => string.Equals(placedObject.CategoryId, seed.Id, StringComparison.Ordinal))))
            .ToArray();
        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Placement,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaPlacementWorkflow(
            summary,
            objects,
            CreateEditableFields(),
            categories,
            new ZaPlacementWorkflowStats(
                objects.Count,
                categories.Count(category => category.ObjectCount > 0),
                sourceFiles.Count),
            diagnostics);
    }

    public static ZaPlacementEditableField? GetEditableField(string? field)
    {
        return CreateEditableFields().FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    public static string CreateRecordId(
        string category,
        string sourcePath,
        int groupIndex,
        int rowIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{category}|{sourcePath}|{groupIndex}|{rowIndex}");
    }

    public static bool TryParseRecordId(
        string? recordId,
        out string category,
        out string sourcePath,
        out int groupIndex,
        out int rowIndex)
    {
        category = string.Empty;
        sourcePath = string.Empty;
        groupIndex = -1;
        rowIndex = -1;

        var parts = recordId?.Split('|') ?? [];
        return parts.Length == 4
            && !string.IsNullOrWhiteSpace(parts[0])
            && !string.IsNullOrWhiteSpace(parts[1])
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out groupIndex)
            && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out rowIndex)
            && groupIndex >= 0
            && rowIndex >= 0
            && (category = parts[0]).Length > 0
            && (sourcePath = parts[1]).Length > 0;
    }

    private void TryLoadTransformCategory(
        OpenedProject project,
        string path,
        string category,
        string objectType,
        IReadOnlyDictionary<string, ZaPlacementSpawnerContext> contextByObjectName,
        ICollection<ZaPlacedObjectRecord> objects,
        ISet<string> sourceFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, path);
            sourceFiles.Add(source.RelativePath);
            var document = ZaSpawnerTransformDocument.Parse(source.Bytes);
            foreach (var group in document.Groups)
            {
                foreach (var row in group.Rows)
                {
                    contextByObjectName.TryGetValue(row.Name, out var context);
                    objects.Add(ToObjectRecord(source, category, objectType, row, context));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"{objectType} transforms could not be loaded: {exception.Message}",
                $"romfs/{path}"));
        }
    }

    private Dictionary<string, ZaPlacementSpawnerContext> LoadPokemonSpawnerContext(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        ISet<string> sourceFiles)
    {
        try
        {
            var source = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
            sourceFiles.Add(source.RelativePath);
            var table = PokemonSpawnerDataDBArray.GetRootAsPokemonSpawnerDataDBArray(new ByteBuffer(source.Bytes));
            var contexts = new Dictionary<string, ZaPlacementSpawnerContext>(StringComparer.Ordinal);
            for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
            {
                var db = table.Values(groupIndex);
                if (db is null)
                {
                    continue;
                }

                for (var spawnerIndex = 0; spawnerIndex < db.Value.RootLength; spawnerIndex++)
                {
                    var spawner = db.Value.Root(spawnerIndex);
                    if (spawner is null)
                    {
                        continue;
                    }

                    AddPokemonSpawnerContexts(
                        contexts,
                        spawner.Value,
                        groupIndex,
                        spawnerIndex);
                }
            }

            return contexts;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Pokemon spawner context could not be loaded for Placement: {exception.Message}",
                $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}"));
            return [];
        }
    }

    private static void AddPokemonSpawnerContexts(
        IDictionary<string, ZaPlacementSpawnerContext> contexts,
        PokemonSpawnerData spawner,
        int groupIndex,
        int spawnerIndex)
    {
        for (var objectIndex = 0; objectIndex < spawner.AppearanceSpawnerObjectInfoListLength; objectIndex++)
        {
            var appearance = spawner.AppearanceSpawnerObjectInfoList(objectIndex);
            if (appearance is null || string.IsNullOrWhiteSpace(appearance.Value.ObjectName))
            {
                continue;
            }

            contexts.TryAdd(
                appearance.Value.ObjectName!,
                new ZaPlacementSpawnerContext(
                    spawner.Id ?? string.Empty,
                    groupIndex,
                    spawnerIndex,
                    appearance.Value.CreateScenePath ?? string.Empty,
                    appearance.Value.DungeonName ?? string.Empty,
                    appearance.Value.BattleAreaId ?? string.Empty,
                    appearance.Value.ZoneInfo?.ZoneId ?? string.Empty,
                    appearance.Value.ZoneInfo?.VariationId ?? string.Empty,
                    appearance.Value.AppearanceInfo?.MinCount ?? 0,
                    appearance.Value.AppearanceInfo?.MaxCount ?? 0,
                    spawner.EncountDataInfoListLength,
                    FormatTags(appearance.Value)));
        }
    }

    private static ZaPlacedObjectRecord ToObjectRecord(
        ZaWorkflowFile source,
        string category,
        string objectType,
        ZaSpawnerTransformRow row,
        ZaPlacementSpawnerContext? context)
    {
        var categorySeed = CategorySeeds.First(seed => seed.Id == category);
        var label = string.IsNullOrWhiteSpace(row.Name)
            ? string.Create(CultureInfo.InvariantCulture, $"{objectType} {row.GroupIndex}:{row.RowIndex}")
            : row.Name;
        var map = FormatMap(categorySeed.Label, context);
        var fields = CreateFields(row, context);
        return new ZaPlacedObjectRecord(
            CreateRecordId(category, source.VirtualPath, row.GroupIndex, row.RowIndex),
            category,
            categorySeed.Label,
            objectType,
            label,
            map,
            source.VirtualPath,
            row.GroupIndex,
            row.RowIndex,
            ChanceIndex: null,
            ItemId: null,
            ItemName: context?.SpawnerId ?? string.Empty,
            ItemHash: row.Name,
            Quantity: 0,
            Chance: null,
            row.Position.X,
            row.Position.Y,
            row.Position.Z,
            row.Rotation.Y,
            ScriptId: row.Name,
            fields,
            new ZaPlacementProvenance(source.VirtualPath, source.SourceLayer, source.FileState));
    }

    private static IReadOnlyList<ZaPlacementFieldValue> CreateFields(
        ZaSpawnerTransformRow row,
        ZaPlacementSpawnerContext? context)
    {
        var fields = new List<ZaPlacementFieldValue>
        {
            Text("point.name", "Object Name", "Identity", row.Name, row.Name, isReadOnly: true),
            Number(PositionXField, "Position X", "Transform", row.Position.X),
            Number(PositionYField, "Position Y", "Transform", row.Position.Y),
            Number(PositionZField, "Position Z", "Transform", row.Position.Z),
            Number(RotationPitchField, "Rotation Pitch", "Transform", row.Rotation.X),
            Number(RotationYawField, "Rotation Yaw", "Transform", row.Rotation.Y),
            Number(RotationRollField, "Rotation Roll", "Transform", row.Rotation.Z),
            Boolean(AttachTransformEnableField, "Attach Transform", "Transform", row.AttachTransformEnable),
        };

        if (context is not null)
        {
            fields.AddRange(
            [
                Text("spawner.id", "Spawner ID", "Spawner Context", context.SpawnerId, context.SpawnerId, isReadOnly: true),
                Text("spawner.scenePath", "Create Scene Path", "Spawner Context", context.CreateScenePath, EmptyAsNone(context.CreateScenePath), isReadOnly: true),
                Text("spawner.zoneId", "Zone ID", "Spawner Context", context.ZoneId, EmptyAsNone(context.ZoneId), isReadOnly: true),
                Text("spawner.variationId", "Zone Variation", "Spawner Context", context.VariationId, EmptyAsNone(context.VariationId), isReadOnly: true),
                Text("spawner.tags", "Tags", "Spawner Context", context.Tags, EmptyAsNone(context.Tags), isReadOnly: true),
                Text("spawner.encounterRows", "Encounter Rows", "Spawner Context", context.EncounterRowCount.ToString(CultureInfo.InvariantCulture), context.EncounterRowCount.ToString(CultureInfo.InvariantCulture), isReadOnly: true),
                Text("spawner.minCount", "Minimum Count", "Spawner Context", context.MinCount.ToString(CultureInfo.InvariantCulture), context.MinCount.ToString(CultureInfo.InvariantCulture), isReadOnly: true),
                Text("spawner.maxCount", "Maximum Count", "Spawner Context", context.MaxCount.ToString(CultureInfo.InvariantCulture), context.MaxCount.ToString(CultureInfo.InvariantCulture), isReadOnly: true),
            ]);
        }

        return fields;
    }

    private static IReadOnlyList<ZaPlacementEditableField> CreateEditableFields()
    {
        return
        [
            NumberField(PositionXField, "Position X", "Transform"),
            NumberField(PositionYField, "Position Y", "Transform"),
            NumberField(PositionZField, "Position Z", "Transform"),
            NumberField(RotationPitchField, "Rotation Pitch", "Transform"),
            NumberField(RotationYawField, "Rotation Yaw", "Transform"),
            NumberField(RotationRollField, "Rotation Roll", "Transform"),
            new(
                AttachTransformEnableField,
                "Attach Transform",
                "Transform",
                "integer",
                0,
                1,
                IsReadOnly: false,
                "Whether this row should attach to a scene transform.",
                [new(0, "0 No"), new(1, "1 Yes")]),
        ];
    }

    private static ZaPlacementEditableField NumberField(
        string field,
        string label,
        string group)
    {
        return new ZaPlacementEditableField(
            field,
            label,
            group,
            "number",
            -1_000_000,
            1_000_000,
            IsReadOnly: false,
            "Spawner transform coordinate or rotation value.",
            []);
    }

    private static ZaPlacementFieldValue Number(
        string field,
        string label,
        string group,
        float value)
    {
        return new ZaPlacementFieldValue(
            field,
            label,
            group,
            value.ToString("R", CultureInfo.InvariantCulture),
            value.ToString("R", CultureInfo.InvariantCulture),
            IsReadOnly: false,
            ValueKind: "number",
            MinimumValue: -1_000_000,
            MaximumValue: 1_000_000,
            Description: "Spawner transform coordinate or rotation value.");
    }

    private static ZaPlacementFieldValue Boolean(
        string field,
        string label,
        string group,
        bool value)
    {
        var numericValue = value ? "1" : "0";
        return new ZaPlacementFieldValue(
            field,
            label,
            group,
            numericValue,
            value ? "Yes" : "No",
            IsReadOnly: false,
            ValueKind: "integer",
            MinimumValue: 0,
            MaximumValue: 1,
            Description: "Whether this row should attach to a scene transform.",
            Options: [new(0, "0 No"), new(1, "1 Yes")]);
    }

    private static ZaPlacementFieldValue Text(
        string field,
        string label,
        string group,
        string value,
        string displayValue,
        bool isReadOnly)
    {
        return new ZaPlacementFieldValue(
            field,
            label,
            group,
            value,
            displayValue,
            isReadOnly);
    }

    private static string FormatMap(
        string fallback,
        ZaPlacementSpawnerContext? context)
    {
        if (context is null)
        {
            return fallback;
        }

        if (!string.IsNullOrWhiteSpace(context.ZoneId) && !string.IsNullOrWhiteSpace(context.VariationId))
        {
            return $"{context.ZoneId} {context.VariationId}";
        }

        if (!string.IsNullOrWhiteSpace(context.ZoneId))
        {
            return context.ZoneId;
        }

        if (!string.IsNullOrWhiteSpace(context.DungeonName))
        {
            return context.DungeonName;
        }

        return string.IsNullOrWhiteSpace(context.SpawnerId) ? fallback : context.SpawnerId;
    }

    private static string FormatTags(AppearanceSpawnerObjectInfo appearance)
    {
        var tags = Enumerable
            .Range(0, appearance.TagListLength)
            .Select(appearance.TagList)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return tags.Length == 0 ? string.Empty : string.Join(", ", tags);
    }

    private static string EmptyAsNone(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "None" : value;
    }

    private sealed record ZaPlacementSpawnerContext(
        string SpawnerId,
        int GroupIndex,
        int SpawnerIndex,
        string CreateScenePath,
        string DungeonName,
        string BattleAreaId,
        string ZoneId,
        string VariationId,
        int MinCount,
        int MaxCount,
        int EncounterRowCount,
        string Tags);

    private sealed record CategorySeed(
        string Id,
        string Label,
        string Description);
}
