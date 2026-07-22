// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.ZA.Data;
using KM.ZA.Encounters;
using KM.ZA.Workflows;
using System.Globalization;
using ItemBallSpawnerData = KM.Formats.ZA.Generated.Field.ItemBall.ItemBallSpawnerData;
using ItemBallSpawnerDataDBArray = KM.Formats.ZA.Generated.Field.ItemBall.ItemBallSpawnerDataDBArray;

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
        var labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
        var pokemonContext = LoadPokemonSpawnerContext(project, diagnostics, sourceFiles);
        var itemBallContext = LoadItemBallSpawnerContext(project, diagnostics, sourceFiles, labels);

        TryLoadTransformCategory(
            project,
            ZaDataPaths.PokemonSpawnerTransformArray,
            PokemonSpawnersCategory,
            "Pokemon Spawner",
            labels,
            pokemonContext,
            objects,
            sourceFiles,
            diagnostics);
        TryLoadTransformCategory(
            project,
            ZaDataPaths.ItemBallSpawnerTransformArray,
            ItemBallSpawnersCategory,
            "Item Ball Spawner",
            labels,
            itemBallContext,
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
        ZaTextLabelLookup labels,
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
                    objects.Add(ToObjectRecord(source, category, objectType, row, context, labels));
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
            var displayOrder = ZaPokemonSpawnerDisplayOrder.Create(table);
            var bossBattleContextResolver = TryLoadBossBattleContextResolver(
                project,
                table,
                diagnostics,
                sourceFiles);
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
                        spawnerIndex,
                        displayOrder[(groupIndex, spawnerIndex)],
                        bossBattleContextResolver.Resolve(
                            spawner.Value.Id,
                            Enumerable
                                .Range(0, spawner.Value.EncountDataInfoListLength)
                                .Select(index => spawner.Value.EncountDataInfoList(index)?.EncountDataId)
                                .Where(id => !string.IsNullOrWhiteSpace(id))
                                .Cast<string>()));
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
        int spawnerIndex,
        ZaPokemonSpawnerDisplayPosition displayPosition,
        ZaBossBattleTableContext? bossBattleContext)
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
                    LocationKey: displayPosition.LocationKey,
                    "spawner.encounterRows",
                    "Encounter Rows",
                    spawner.EncountDataInfoListLength,
                    PrimaryData: string.Empty,
                    DisplayLabel: string.Empty,
                    DisplayMap: string.Empty,
                    FormatTags(appearance.Value),
                    displayPosition.Ordinal,
                    bossBattleContext));
        }
    }

    private ZaBossBattleContextResolver TryLoadBossBattleContextResolver(
        OpenedProject project,
        PokemonSpawnerDataDBArray table,
        ICollection<ValidationDiagnostic> diagnostics,
        ISet<string> sourceFiles)
    {
        var spawnerIds = new List<string>();
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
                if (!string.IsNullOrWhiteSpace(spawner?.Id))
                {
                    spawnerIds.Add(spawner.Value.Id!);
                }
            }
        }

        try
        {
            if (!fileSource.Exists(project, ZaDataPaths.BossBattleDataGlobal))
            {
                return new ZaBossBattleContextResolver(
                    consumerRecords: null,
                    spawnerIds);
            }

            var source = fileSource.Read(project, ZaDataPaths.BossBattleDataGlobal);
            var consumers = ZaBossBattleConsumerTable.Read(source.Bytes);
            sourceFiles.Add(source.RelativePath);
            return new ZaBossBattleContextResolver(consumers, spawnerIds);
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                "Boss battle gameplay relationships could not be loaded. "
                + $"Placement organization will use raw spawner identifiers instead: {exception.Message}",
                $"romfs/{ZaDataPaths.BossBattleDataGlobal}"));
            return new ZaBossBattleContextResolver(
                consumerRecords: null,
                spawnerIds);
        }
    }

    private Dictionary<string, ZaPlacementSpawnerContext> LoadItemBallSpawnerContext(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        ISet<string> sourceFiles,
        ZaTextLabelLookup labels)
    {
        try
        {
            var source = fileSource.Read(project, ZaDataPaths.ItemBallSpawnerDataArray);
            sourceFiles.Add(source.RelativePath);
            var table = ItemBallSpawnerDataDBArray.GetRootAsItemBallSpawnerDataDBArray(new ByteBuffer(source.Bytes));
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

                    AddItemBallSpawnerContexts(
                        contexts,
                        spawner.Value,
                        groupIndex,
                        spawnerIndex,
                        labels);
                }
            }

            return contexts;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Item ball spawner context could not be loaded for Placement: {exception.Message}",
                $"romfs/{ZaDataPaths.ItemBallSpawnerDataArray}"));
            return [];
        }
    }

    private static void AddItemBallSpawnerContexts(
        IDictionary<string, ZaPlacementSpawnerContext> contexts,
        ItemBallSpawnerData spawner,
        int groupIndex,
        int spawnerIndex,
        ZaTextLabelLookup labels)
    {
        var tableIds = Enumerable
            .Range(0, spawner.TableInfoListLength)
            .Select(index => spawner.TableInfoList(index)?.TableId ?? string.Empty)
            .Where(tableId => !string.IsNullOrWhiteSpace(tableId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var primaryData = FormatItemBallTablePreview(tableIds, labels);

        for (var objectIndex = 0; objectIndex < spawner.AppearanceSpawnerObjectInfoListLength; objectIndex++)
        {
            var appearance = spawner.AppearanceSpawnerObjectInfoList(objectIndex);
            if (appearance is null || string.IsNullOrWhiteSpace(appearance.Value.ObjectName))
            {
                continue;
            }

            var objectName = appearance.Value.ObjectName!;
            var itemBallLabel = FormatItemBallObjectLabel(objectName, primaryData);
            var itemBallMap = FormatItemBallObjectMap(objectName);
            _ = TryParseItemBallObjectName(objectName, out var locationKey, out _);
            contexts.TryAdd(
                objectName,
                new ZaPlacementSpawnerContext(
                    spawner.Id ?? string.Empty,
                    groupIndex,
                    spawnerIndex,
                    appearance.Value.CreateScenePath ?? string.Empty,
                    DungeonName: string.Empty,
                    BattleAreaId: string.Empty,
                    appearance.Value.ZoneInfo?.ZoneId ?? string.Empty,
                    appearance.Value.ZoneInfo?.VariationId ?? string.Empty,
                    appearance.Value.AppearanceInfo?.MinCount ?? 0,
                    appearance.Value.AppearanceInfo?.MaxCount ?? 0,
                    locationKey,
                    "spawner.itemTables",
                    "Item Tables",
                    tableIds.Length,
                    primaryData,
                    itemBallLabel,
                    itemBallMap,
                    string.Join(", ", tableIds),
                    spawnerIndex + 1,
                    BossBattleContext: null));
        }
    }

    private static ZaPlacedObjectRecord ToObjectRecord(
        ZaWorkflowFile source,
        string category,
        string objectType,
        ZaSpawnerTransformRow row,
        ZaPlacementSpawnerContext? context,
        ZaTextLabelLookup labels)
    {
        var categorySeed = CategorySeeds.First(seed => seed.Id == category);
        context ??= category == ItemBallSpawnersCategory
            ? CreateFallbackItemBallContext(row)
            : null;
        var label = FormatObjectLabel(objectType, row, context, labels);
        var map = FormatMap(categorySeed.Label, context, labels);
        var fields = CreateFields(row, context, map);
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
            ItemName: context?.PrimaryData ?? string.Empty,
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
        ZaPlacementSpawnerContext? context,
        string map)
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
            var locationKey = ZaLumioseLocationLabels.CreateLocationKey(
                context.ZoneId,
                context.VariationId,
                string.IsNullOrWhiteSpace(context.LocationKey) ? context.SpawnerId : context.LocationKey);
            var district = ZaLumioseLocationLabels.FormatDistrict(locationKey);
            var sector = ZaLumioseLocationLabels.FormatSector(locationKey);

            fields.AddRange(
            [
                Text("spawner.location", "Location", "Spawner Context", map, map, isReadOnly: true),
                Text("spawner.district", "District", "Spawner Context", district ?? string.Empty, district ?? "None", isReadOnly: true),
                Text("spawner.sector", "Sector", "Spawner Context", sector ?? string.Empty, sector ?? "None", isReadOnly: true),
                Text("spawner.id", "Spawner ID", "Spawner Context", context.SpawnerId, context.SpawnerId, isReadOnly: true),
                Text("spawner.scenePath", "Create Scene Path", "Spawner Context", context.CreateScenePath, EmptyAsNone(context.CreateScenePath), isReadOnly: true),
                Text("spawner.zoneId", "Zone ID", "Spawner Context", context.ZoneId, EmptyAsNone(context.ZoneId), isReadOnly: true),
                Text("spawner.variationId", "Zone Variation", "Spawner Context", context.VariationId, EmptyAsNone(context.VariationId), isReadOnly: true),
                Text("spawner.tags", "Tags", "Spawner Context", context.Tags, EmptyAsNone(context.Tags), isReadOnly: true),
                Text(context.CountField, context.CountLabel, "Spawner Context", context.CountValue.ToString(CultureInfo.InvariantCulture), context.CountValue.ToString(CultureInfo.InvariantCulture), isReadOnly: true),
                Text("spawner.minCount", "Minimum Count", "Spawner Context", context.MinCount.ToString(CultureInfo.InvariantCulture), context.MinCount.ToString(CultureInfo.InvariantCulture), isReadOnly: true),
                Text("spawner.maxCount", "Maximum Count", "Spawner Context", context.MaxCount.ToString(CultureInfo.InvariantCulture), context.MaxCount.ToString(CultureInfo.InvariantCulture), isReadOnly: true),
            ]);

            if (context.BossBattleContext is { } bossBattleContext)
            {
                var contextLabels = string.Join(", ", bossBattleContext.Contexts.Select(battleContext => battleContext.Label));
                fields.AddRange(
                [
                    Text("spawner.bossBattleContextKey", "Boss Battle Context Key", "Boss Battle Context", bossBattleContext.PrimaryContext.Key, bossBattleContext.PrimaryContext.Key, isReadOnly: true),
                    Text("spawner.bossBattleContextLabel", "Boss Battle Context", "Boss Battle Context", bossBattleContext.PrimaryContext.Label, bossBattleContext.PrimaryContext.Label, isReadOnly: true),
                    Text("spawner.bossBattleContextRank", "Boss Battle Context Rank", "Boss Battle Context", bossBattleContext.PrimaryContext.Rank.ToString(CultureInfo.InvariantCulture), bossBattleContext.PrimaryContext.Rank.ToString(CultureInfo.InvariantCulture), isReadOnly: true),
                    Text("spawner.bossBattleContexts", "Boss Battle Usage", "Boss Battle Context", contextLabels, contextLabels, isReadOnly: true),
                ]);

                if (!string.IsNullOrWhiteSpace(bossBattleContext.WaveLabel)
                    && bossBattleContext.WaveRank is { } waveRank)
                {
                    fields.Add(Text("spawner.bossBattleWaveLabel", "Boss Battle Wave", "Boss Battle Context", bossBattleContext.WaveLabel, bossBattleContext.WaveLabel, isReadOnly: true));
                    fields.Add(Text("spawner.bossBattleWaveRank", "Boss Battle Wave Rank", "Boss Battle Context", waveRank.ToString(CultureInfo.InvariantCulture), waveRank.ToString(CultureInfo.InvariantCulture), isReadOnly: true));
                }
            }

            if (ZaLumioseLocationLabels.GetMissionDetails(locationKey) is { Length: > 0 } missionDetails)
            {
                fields.Add(Text(
                    "spawner.mission",
                    "Mission",
                    "Spawner Context",
                    missionDetails,
                    missionDetails,
                    isReadOnly: true));
            }

            if (!string.IsNullOrWhiteSpace(context.PrimaryData))
            {
                fields.Add(Text("spawner.primaryData", "Primary Data", "Spawner Context", context.PrimaryData, context.PrimaryData, isReadOnly: true));
            }
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
        ZaPlacementSpawnerContext? context,
        ZaTextLabelLookup labels)
    {
        if (context is null)
        {
            return fallback;
        }

        if (!string.IsNullOrWhiteSpace(context.DisplayMap))
        {
            return context.DisplayMap;
        }

        return ZaLumioseLocationLabels.FormatPlacementMap(
            fallback,
            context.ZoneId,
            context.VariationId,
            context.DungeonName,
            context.BattleAreaId,
            string.IsNullOrWhiteSpace(context.LocationKey) ? context.SpawnerId : context.LocationKey,
            labels.PlaceName,
            labels.Pokemon,
            labels.MissionTitle);
    }

    private static string FormatObjectLabel(
        string objectType,
        ZaSpawnerTransformRow row,
        ZaPlacementSpawnerContext? context,
        ZaTextLabelLookup labels)
    {
        if (!string.IsNullOrWhiteSpace(context?.DisplayLabel))
        {
            return context.DisplayLabel;
        }

        if (context is not null)
        {
            var locationKey = string.IsNullOrWhiteSpace(context.LocationKey)
                ? ZaLumioseLocationLabels.CreateLocationKey(
                    context.ZoneId,
                    context.VariationId,
                    context.SpawnerId)
                : context.LocationKey;
            if (ZaLumioseLocationLabels.IsNumberedWildZone(locationKey))
            {
                var location = ZaLumioseLocationLabels.FormatLocation(
                    locationKey,
                    labels.PlaceName,
                    labels.Pokemon,
                    labels.MissionTitle);
                return $"{location} Spawner {context.DisplayOrdinal.ToString(CultureInfo.InvariantCulture)}";
            }

            if (!string.IsNullOrWhiteSpace(context.SpawnerId))
            {
                return ZaLumioseLocationLabels.FormatRawSpawnerId(
                    context.SpawnerId,
                    labels.Pokemon,
                    labels.MissionTitle);
            }
        }

        if (!string.IsNullOrWhiteSpace(row.Name))
        {
            return ZaLumioseLocationLabels.FormatRawObjectName(row.Name);
        }

        return string.Create(CultureInfo.InvariantCulture, $"{objectType} {row.GroupIndex}:{row.RowIndex}");
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

    private static ZaPlacementSpawnerContext? CreateFallbackItemBallContext(ZaSpawnerTransformRow row)
    {
        var label = FormatItemBallObjectLabel(row.Name, primaryData: string.Empty);
        var map = FormatItemBallObjectMap(row.Name);
        if (string.Equals(label, ZaLumioseLocationLabels.FormatRawObjectName(row.Name), StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(map))
        {
            return null;
        }

        return new ZaPlacementSpawnerContext(
            row.Name,
            row.GroupIndex,
            row.RowIndex,
            CreateScenePath: string.Empty,
            DungeonName: string.Empty,
            BattleAreaId: string.Empty,
            ZoneId: string.Empty,
            VariationId: string.Empty,
            MinCount: 0,
            MaxCount: 0,
            LocationKey: TryParseItemBallObjectName(row.Name, out var locationKey, out _) ? locationKey : string.Empty,
            "spawner.itemTables",
            "Item Tables",
            0,
            PrimaryData: string.Empty,
            DisplayLabel: label,
            DisplayMap: string.IsNullOrWhiteSpace(map) ? "Item Ball Spawners" : map,
            Tags: string.Empty,
            DisplayOrdinal: row.RowIndex + 1,
            BossBattleContext: null);
    }

    private static string FormatItemBallTablePreview(
        IReadOnlyList<string> tableIds,
        ZaTextLabelLookup labels)
    {
        var itemNames = tableIds
            .Select(ParseItemBallTableItemId)
            .Where(itemId => itemId > 0)
            .Select(labels.Item)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (itemNames.Length > 0)
        {
            return itemNames.Length == 1
                ? itemNames[0]
                : $"{itemNames[0]} + {(itemNames.Length - 1).ToString(CultureInfo.InvariantCulture)} more";
        }

        if (tableIds.Count == 0)
        {
            return string.Empty;
        }

        return tableIds.Count == 1
            ? tableIds[0]
            : $"{tableIds[0]} + {(tableIds.Count - 1).ToString(CultureInfo.InvariantCulture)} more";
    }

    private static int ParseItemBallTableItemId(string tableId)
    {
        const string prefix = "field_item_ball_";
        return tableId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(tableId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            ? itemId
            : 0;
    }

    private static string FormatItemBallObjectMap(string objectName)
    {
        if (TryParseDungeonItemObjectName(objectName, out var dungeonItem))
        {
            return dungeonItem.Map;
        }

        if (TryParseItemBallObjectName(objectName, out var locationKey, out _))
        {
            return FormatItemBallLocation(locationKey);
        }

        return string.Empty;
    }

    private static string FormatItemBallObjectLabel(string objectName, string primaryData)
    {
        string label;
        if (TryParseDungeonItemObjectName(objectName, out var dungeonItem))
        {
            label = dungeonItem.Label;
        }
        else if (TryParseItemBallObjectName(objectName, out var locationKey, out var itemBallNumber))
        {
            label = string.IsNullOrWhiteSpace(itemBallNumber)
                ? $"{FormatItemBallLocation(locationKey)} Item Ball"
                : $"{FormatItemBallLocation(locationKey)} Item Ball {itemBallNumber}";
        }
        else
        {
            label = ZaLumioseLocationLabels.FormatRawObjectName(objectName);
        }

        return string.IsNullOrWhiteSpace(primaryData)
            ? label
            : $"{label}: {primaryData}";
    }

    private static bool TryParseDungeonItemObjectName(
        string objectName,
        out DungeonItemLocation dungeonItem)
    {
        dungeonItem = default;
        var tokens = objectName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3
            || !string.Equals(tokens[0], "itd", StringComparison.OrdinalIgnoreCase)
            || !tokens[1].StartsWith("d", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[1][1..], NumberStyles.None, CultureInfo.InvariantCulture, out var dungeonNumber))
        {
            return false;
        }

        if (tokens.Length >= 4
            && int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out var floorNumber)
            && int.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            var map = ZaLumioseLocationLabels.FormatLocation(
                $"d{dungeonNumber.ToString("00", CultureInfo.InvariantCulture)}_{floorNumber.ToString("00", CultureInfo.InvariantCulture)}");
            dungeonItem = new DungeonItemLocation(
                map,
                $"{map} Item {tokens[3].ToUpperInvariant()}");
            return true;
        }

        if (int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            var map = ZaLumioseLocationLabels.FormatLocation(
                $"d{dungeonNumber.ToString("00", CultureInfo.InvariantCulture)}");
            dungeonItem = new DungeonItemLocation(
                map,
                $"{map} Item {tokens[2].ToUpperInvariant()}");
            return true;
        }

        return false;
    }

    private static string FormatItemBallLocation(string locationKey)
    {
        if (ZaLumioseLocationLabels.FormatKnownLocation(locationKey) is { Length: > 0 } knownLocation)
        {
            return knownLocation;
        }

        if (ZaLumioseLocationLabels.FormatDistrictSector(locationKey) is { Length: > 0 } districtSector)
        {
            return districtSector;
        }

        if (locationKey.StartsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            return ZaLumioseLocationLabels.FormatLocation(locationKey);
        }

        var tokens = locationKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2
            && string.Equals(tokens[0], "last", StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[1], "battle", StringComparison.OrdinalIgnoreCase))
        {
            return "Final Battle Area";
        }

        if (tokens.Length > 0 && tokens[0].StartsWith("t", StringComparison.OrdinalIgnoreCase))
        {
            var parts = new List<string>();
            var locationPrefixLength = 1;
            var phase = ZaLumioseLocationLabels.FormatKnownLocation(tokens[0])
                ?? FormatItemBallScenePhase(tokens[0]);
            if (tokens.Length > 1
                && ZaLumioseLocationLabels.FormatKnownLocation($"{tokens[0]}_{tokens[1]}") is { Length: > 0 } compositeLocation)
            {
                phase = compositeLocation;
                locationPrefixLength = 2;
            }

            for (var index = locationPrefixLength; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (token.StartsWith("i", StringComparison.OrdinalIgnoreCase) && token.Length > 1)
                {
                    parts.Add($"Interior Area {token[1..].ToUpperInvariant()}");
                    continue;
                }

                parts.Add(token.All(char.IsDigit)
                    ? $"Room {int.Parse(token, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}"
                    : token.ToUpperInvariant());
            }

            return parts.Count == 0
                ? phase
                : $"{phase}, {string.Join(", ", parts)}";
        }

        return ZaLumioseLocationLabels.FormatRawObjectName(locationKey);
    }

    private static string FormatItemBallScenePhase(string token)
    {
        if (token.Length > 1
            && token[0] is 't' or 'T'
            && int.TryParse(token[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var phase))
        {
            return phase <= 1
                ? "Lumiose City"
                : $"Lumiose City Phase {phase.ToString(CultureInfo.InvariantCulture)}";
        }

        return ZaLumioseLocationLabels.FormatRawObjectName(token);
    }

    private static bool TryParseItemBallObjectName(
        string objectName,
        out string locationKey,
        out string itemBallNumber)
    {
        locationKey = string.Empty;
        itemBallNumber = string.Empty;
        var trimmed = objectName.Trim();
        if (trimmed.StartsWith("id_", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["id_".Length..];
        }

        var tokens = trimmed.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return false;
        }

        if (string.Equals(tokens[0], "itb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseItbObjectTokens(tokens, out locationKey, out itemBallNumber);
        }

        if (string.Equals(tokens[0], "itd", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseItdObjectTokens(tokens, out locationKey, out itemBallNumber);
        }

        return false;
    }

    private static bool TryParseItbObjectTokens(
        IReadOnlyList<string> tokens,
        out string locationKey,
        out string itemBallNumber)
    {
        locationKey = string.Empty;
        itemBallNumber = string.Empty;
        if (tokens.Count < 2)
        {
            return false;
        }

        var locationTokens = tokens.Skip(1).ToArray();
        if (locationTokens.Length > 1 && locationTokens[^1].All(char.IsDigit))
        {
            itemBallNumber = locationTokens[^1];
            locationTokens = locationTokens[..^1];
        }

        if (locationTokens.Length == 0)
        {
            return false;
        }

        locationKey = string.Join('_', locationTokens);
        return true;
    }

    private static bool TryParseItdObjectTokens(
        IReadOnlyList<string> tokens,
        out string locationKey,
        out string itemBallNumber)
    {
        locationKey = string.Empty;
        itemBallNumber = string.Empty;
        if (tokens.Count < 3)
        {
            return false;
        }

        locationKey = string.Join('_', tokens.Skip(1).Take(2));
        if (tokens.Count > 3 && tokens[3].All(char.IsDigit))
        {
            itemBallNumber = tokens[3];
        }

        return true;
    }

    private static string EmptyAsNone(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "None" : value;
    }

    private readonly record struct DungeonItemLocation(
        string Map,
        string Label);

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
        string LocationKey,
        string CountField,
        string CountLabel,
        int CountValue,
        string PrimaryData,
        string DisplayLabel,
        string DisplayMap,
        string Tags,
        int DisplayOrdinal,
        ZaBossBattleTableContext? BossBattleContext);

    private sealed record CategorySeed(
        string Id,
        string Label,
        string Description);
}
