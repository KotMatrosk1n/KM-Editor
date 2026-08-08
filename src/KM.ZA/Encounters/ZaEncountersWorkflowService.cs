// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.ZA.Data;
using KM.ZA.ScriptedBosses;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Encounters;

internal sealed class ZaEncountersWorkflowService
{
    public const string SpeciesIdField = "speciesId";
    public const string FormField = "form";
    public const string LevelMinField = "levelMin";
    public const string LevelMaxField = "levelMax";
    public const string PlayerPartnerLevelField = "playerPartnerLevel";
    public const string AlphaChancePercentField = "alphaChancePercent";
    public const string AlphaLevelBonusField = "alphaLevelBonus";
    public const string HeldItemIdField = "heldItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyModeField = "shinyLock";
    public const string Move1IdField = "move1Id";
    public const string Move2IdField = "move2Id";
    public const string Move3IdField = "move3Id";
    public const string Move4IdField = "move4Id";
    public const string FlawlessIvCountField = "flawlessIvCount";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string StrengthenHpField = "strengthenHp";
    public const string StrengthenAttackField = "strengthenAttack";
    public const string StrengthenDefenseField = "strengthenDefense";
    public const string StrengthenSpecialAttackField = "strengthenSpecialAttack";
    public const string StrengthenSpecialDefenseField = "strengthenSpecialDefense";
    public const string StrengthenSpeedField = "strengthenSpeed";
    public const int MinimumStrengthenValue = 0;
    public const int MaximumStrengthenHpValue = ushort.MaxValue;
    public const int MaximumStrengthenOtherValue = byte.MaxValue;
    internal const string VanillaTalentScaleField = "vanillaTalentScale";
    internal const string VanillaTalentVCountField = "vanillaTalentVCount";
    public const string WeightField = "weight";
    public const string SlotMaxCountField = "slotMaxCount";
    public const string AppearanceMinCountField = "appearanceMinCount";
    public const string AppearanceMaxCountField = "appearanceMaxCount";

    private const string WorkflowLabel = "Wild Encounters";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A wild encounter Pokemon rows, slot weights, and spawn counts.";
    private const string GameVersionLabel = "Pokemon Legends ZA";
    private const string TableIdPrefix = "za-spawner";
    private const string PokemonDataRecordIdPrefix = "encount-data:";
    private const string AppearanceRecordIdSuffix = "#appearance";
    private const string PhaseCondition = "phase_condition";
    private const int CurrentPhaseAtLeastComparison = 5;
    private const int PostgamePhaseThreshold = 100000;

    // Encounter PokemonData uses the actual-gender codes, not the separate
    // payload SexType enum whose zero value means default.
    private static readonly IReadOnlyList<ZaEncounterEditableFieldOption> GenderOptions =
    [
        new(-1, "Random / species default"),
        new(0, "Male"),
        new(1, "Female"),
        new(2, "Genderless"),
    ];

    private static readonly IReadOnlyList<ZaEncounterEditableFieldOption> ShinyModeOptions =
    [
        new(ZaPokemonDataConstants.RareNotShiny, "Never Shiny"),
        new(ZaPokemonDataConstants.RareForcedShiny, "Always Shiny"),
        new(ZaPokemonDataConstants.RareDefaultShinyRoll, "Random"),
    ];

    private static readonly IReadOnlyList<ZaEncounterEditableFieldOption> AbilityModeOptions =
    [
        new(0, "Random Ability 1 or 2"),
        new(1, "Random Ability 1, 2, or Hidden"),
        new(2, "Ability 1"),
        new(3, "Ability 2"),
        new(4, "Hidden Ability"),
        new(255, "Game default"),
    ];

    private static readonly IReadOnlyList<ZaEncounterEditableFieldOption> NatureOptions =
    [
        new(-1, "Random"),
        new(0, "Default (game behavior)"),
        new(1, "Hardy"),
        new(2, "Lonely"),
        new(3, "Brave"),
        new(4, "Adamant"),
        new(5, "Naughty"),
        new(6, "Bold"),
        new(7, "Docile"),
        new(8, "Relaxed"),
        new(9, "Impish"),
        new(10, "Lax"),
        new(11, "Timid"),
        new(12, "Hasty"),
        new(13, "Serious"),
        new(14, "Jolly"),
        new(15, "Naive"),
        new(16, "Modest"),
        new(17, "Mild"),
        new(18, "Quiet"),
        new(19, "Bashful"),
        new(20, "Rash"),
        new(21, "Calm"),
        new(22, "Gentle"),
        new(23, "Sassy"),
        new(24, "Careful"),
        new(25, "Quirky"),
    ];

    private static readonly IReadOnlyList<ZaEncounterEditableFieldOption> FlawlessIvCountOptions =
    [
        new(0, "Random IVs"),
        new(1, "1 Guaranteed Perfect IV"),
        new(2, "2 Guaranteed Perfect IVs"),
        new(3, "3 Guaranteed Perfect IVs"),
        new(4, "4 Guaranteed Perfect IVs"),
        new(5, "5 Guaranteed Perfect IVs"),
        new(6, "6 Guaranteed Perfect IVs"),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaEncountersWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Encounters,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? encounterSource = null;
        ZaWorkflowFile? spawnerSource = null;
        ZaWorkflowFile? bossBattleSource = null;
        ZaWorkflowFile? playerPartnerSource = null;
        var labels = ZaTextLabelLookup.None();
        var pokemonAvailability = ZaPokemonAvailability.Unfiltered;
        var outzoneAvailability = ZaOutzoneEncounterAvailability.Unknown;
        var tables = Array.Empty<ZaEncounterTableRecord>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            pokemonAvailability = ZaPokemonAvailability.Load(project, fileSource, diagnostics, WorkflowLabel);
            encounterSource = fileSource.Read(project, ZaDataPaths.EncountDataArray);
            spawnerSource = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
            outzoneAvailability = TryLoadOutzoneAvailability(project);
            var bossBattleConsumers = TryLoadBossBattleConsumers(
                project,
                diagnostics,
                out bossBattleSource);
            tables = LoadTables(
                spawnerSource,
                encounterSource,
                bossBattleConsumers,
                labels,
                pokemonAvailability,
                diagnostics).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Wild Encounters could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.EncountDataArray}"));
        }

        var scriptedBossCatalog = ZaScriptedBossActionCatalog.Load(
            project,
            fileSource,
            labels,
            diagnostics);
        var editableFields = CreateEditableFields(labels, pokemonAvailability);
        if (tables.Any(ZaEncounterPlayerPartnerCatalog.IsTargetTable))
        {
            tables = TryAttachPlayerPartner(
                project,
                tables,
                labels,
                pokemonAvailability,
                editableFields,
                diagnostics,
                out playerPartnerSource);
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Encounters,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        var workflow = new ZaEncountersWorkflow(
            summary,
            tables,
            editableFields,
            new ZaEncountersWorkflowStats(
                tables.Length,
                tables.Sum(table => table.Slots.Count),
                new[] { encounterSource, spawnerSource, bossBattleSource, playerPartnerSource }.Count(source => source is not null)
                    + scriptedBossCatalog.SourceFileCount),
            diagnostics)
        {
            ScriptedBosses = scriptedBossCatalog.Profiles,
            ScriptedBossMoveOptions = scriptedBossCatalog.MoveOptions,
            PokemonAvailability = pokemonAvailability,
            OutzoneAvailability = outzoneAvailability,
        };
        return AddVanillaRestoreAvailability(project, workflow);
    }

    private ZaEncountersWorkflow AddVanillaRestoreAvailability(
        OpenedProject project,
        ZaEncountersWorkflow workflow)
    {
        if (workflow.Tables.Count == 0)
        {
            return workflow;
        }

        if (!ZaEncounterVanillaRestoreCatalog.TryCreate(
                project,
                fileSource,
                out var catalog,
                out var catalogBlockedReason))
        {
            return workflow with
            {
                Tables = workflow.Tables
                    .Select(table => table with
                    {
                        Slots = table.Slots
                            .Select(slot => slot with
                            {
                                CanRevertToVanilla = false,
                                RevertToVanillaBlockedReason = catalogBlockedReason,
                            })
                            .ToArray(),
                    })
                    .ToArray(),
            };
        }

        return workflow with
        {
            Tables = workflow.Tables
                .Select(table => table with
                {
                    Slots = table.Slots
                        .Select(slot =>
                        {
                            try
                            {
                                var canRevert = catalog!.TryResolve(
                                    workflow,
                                    table,
                                    slot,
                                    out _,
                                    out var blockedReason);
                                return slot with
                                {
                                    CanRevertToVanilla = canRevert,
                                    RevertToVanillaBlockedReason = canRevert
                                        ? null
                                        : blockedReason,
                                };
                            }
                            catch (Exception exception) when (exception is not OutOfMemoryException)
                            {
                                return slot with
                                {
                                    CanRevertToVanilla = false,
                                    RevertToVanillaBlockedReason =
                                        "This encounter cannot be matched exactly to the verified vanilla files.",
                                };
                            }
                        })
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    private ZaOutzoneEncounterAvailability TryLoadOutzoneAvailability(OpenedProject project)
    {
        try
        {
            return LoadOutzoneAvailability(project);
        }
        catch (Exception)
        {
            // Advisory-only base observations must never make the editable workflow unavailable.
            return ZaOutzoneEncounterAvailability.Unknown;
        }
    }

    private ZaOutzoneEncounterAvailability LoadOutzoneAvailability(OpenedProject project)
    {
        var encounterSource = fileSource.ReadBase(project, ZaDataPaths.EncountDataArray);
        var spawnerSource = fileSource.ReadBase(project, ZaDataPaths.PokemonSpawnerDataArray);
        var pokemonRowGroups = ZaEncounterDataDocument.Parse(encounterSource.Bytes)
            .Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
            .ToArray();
        var ambiguousRow = pokemonRowGroups.FirstOrDefault(group => group
            .Select(entry => (entry.DevNo, entry.FormNo))
            .Distinct()
            .Skip(1)
            .Any());
        if (ambiguousRow is not null)
        {
            throw new InvalidDataException(
                $"Base encounter row id '{ambiguousRow.Key}' resolves to multiple Pokemon pairs.");
        }

        var pokemonRows = pokemonRowGroups
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var spawnerTable = PokemonSpawnerDataDBArray.GetRootAsPokemonSpawnerDataDBArray(
            new ByteBuffer(spawnerSource.Bytes));
        var displayOrder = ZaPokemonSpawnerDisplayOrder.Create(spawnerTable);
        var observedPairs = new HashSet<(int SpeciesId, int Form)>();

        for (var groupIndex = 0; groupIndex < spawnerTable.ValuesLength; groupIndex++)
        {
            var group = spawnerTable.Values(groupIndex);
            if (group is null)
            {
                continue;
            }

            for (var spawnerIndex = 0; spawnerIndex < group.Value.RootLength; spawnerIndex++)
            {
                var spawner = group.Value.Root(spawnerIndex);
                if (spawner is null)
                {
                    continue;
                }

                if (!displayOrder.TryGetValue(
                        (groupIndex, spawnerIndex),
                        out var displayPosition))
                {
                    throw new InvalidDataException(
                        "A base Pokemon spawner could not be mapped to its location.");
                }

                if (displayPosition.LocationKey?.StartsWith(
                        "outzone_",
                        StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                for (var slotIndex = 0;
                     slotIndex < spawner.Value.EncountDataInfoListLength;
                     slotIndex++)
                {
                    var slot = spawner.Value.EncountDataInfoList(slotIndex);
                    var encounterDataId = slot?.EncountDataId ?? string.Empty;
                    var pokemon = ResolvePokemonRow(encounterDataId, pokemonRows);
                    if (pokemon is null)
                    {
                        throw new InvalidDataException(
                            $"Base city spawner encounter row '{encounterDataId}' could not be resolved.");
                    }

                    observedPairs.Add((pokemon.DevNo, pokemon.FormNo));
                }
            }
        }

        if (observedPairs.Count == 0)
        {
            throw new InvalidDataException(
                "Base Pokemon spawner data did not expose any Lumiose City encounter pairs.");
        }

        return ZaOutzoneEncounterAvailability.Create(observedPairs);
    }

    private IReadOnlyList<ZaBossBattleConsumerRecord>? TryLoadBossBattleConsumers(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        out ZaWorkflowFile? source)
    {
        source = null;
        try
        {
            if (!fileSource.Exists(project, ZaDataPaths.BossBattleDataGlobal))
            {
                return null;
            }

            var candidateSource = fileSource.Read(project, ZaDataPaths.BossBattleDataGlobal);
            var consumers = ZaBossBattleConsumerTable.Read(candidateSource.Bytes);
            source = candidateSource;
            return consumers;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                "Boss battle gameplay relationships could not be loaded. "
                + $"Boss encounter organization will use raw spawner identifiers instead: {exception.Message}",
                $"romfs/{ZaDataPaths.BossBattleDataGlobal}"));
            return null;
        }
    }

    private ZaEncounterTableRecord[] TryAttachPlayerPartner(
        OpenedProject project,
        IReadOnlyList<ZaEncounterTableRecord> tables,
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability,
        IReadOnlyList<ZaEncounterEditableField> editableFields,
        ICollection<ValidationDiagnostic> diagnostics,
        out ZaWorkflowFile? source)
    {
        source = null;
        try
        {
            var candidateSource = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            var document = ZaPokemonDataDocument.Parse(candidateSource.Bytes);
            if (!ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
                    document,
                    out var row,
                    out var blockedReason))
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    "AZ's temporary Lucario could not be matched safely and will remain hidden. "
                        + blockedReason,
                    candidateSource.RelativePath,
                    expected: $"Unique '{ZaEncounterPlayerPartnerCatalog.PokemonDataId}' row at source index {ZaEncounterPlayerPartnerCatalog.PokemonDataSourceIndex}"));
                return tables.ToArray();
            }

            source = candidateSource;
            var canRevertToVanilla = TryValidatePlayerPartnerBaseRow(
                project,
                out var revertToVanillaBlockedReason);
            var moves = row.WazaList?.Values.Take(4).ToArray() ?? [0, 0, 0, 0];
            var partner = new ZaEncounterPlayerPartnerRecord(
                ZaEncounterPlayerPartnerCatalog.EditSlot,
                row.SourceIndex,
                row.Id!,
                "AZ's Lucario",
                "Temporary player partner",
                "Used only as the player's partner during the Absol battle. This is a separate PokemonData row from the level 50 Lucario received later.",
                row.DevNo,
                FormatEncounterSpeciesLabel(row.DevNo, row.FormNo, labels),
                row.FormNo,
                row.MinLevel,
                row.MaxLevel,
                row.MinLevel == row.MaxLevel && row.MinLevel is >= 1 and <= 100,
                row.HoldItem ?? 0,
                row.Tokusei,
                row.Seikaku,
                row.Sex,
                row.Rare,
                moves,
                row.WazaList is not null,
                ZaPokemonDataIvEncoding.ReadFlawlessIvCount(row),
                ReadIv(row.TalentValue, stats => stats.HP) ?? -1,
                ReadIv(row.TalentValue, stats => stats.Attack) ?? -1,
                ReadIv(row.TalentValue, stats => stats.Defense) ?? -1,
                ReadIv(row.TalentValue, stats => stats.SpecialAttack) ?? -1,
                ReadIv(row.TalentValue, stats => stats.SpecialDefense) ?? -1,
                ReadIv(row.TalentValue, stats => stats.Speed) ?? -1,
                new ZaEncounterProvenance(
                    candidateSource.RelativePath,
                    candidateSource.SourceLayer,
                    candidateSource.FileState),
                canRevertToVanilla,
                revertToVanillaBlockedReason)
            {
                FormOptions = CreateFormOptions(
                    row.DevNo,
                    labels.Pokemon(row.DevNo),
                    pokemonAvailability),
                EditableFields = CreatePlayerPartnerEditableFields(editableFields),
                TalentScale = row.TalentScale,
                TalentVCount = row.TalentVNum,
            };

            return tables
                .Select(table => ZaEncounterPlayerPartnerCatalog.IsTargetTable(table)
                    ? table with { PlayerPartner = partner }
                    : table)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                "AZ's temporary Lucario could not be loaded and will remain hidden. "
                    + exception.Message,
                $"romfs/{ZaDataPaths.PokemonDataArray}",
                expected: "Readable verified PokemonData source"));
            return tables.ToArray();
        }
    }

    private bool TryValidatePlayerPartnerBaseRow(
        OpenedProject project,
        out string? blockedReason)
    {
        try
        {
            var source = fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray);
            var document = ZaPokemonDataDocument.Parse(source.Bytes);
            if (!ZaEncounterPlayerPartnerCatalog.TryResolveExactRow(
                    document,
                    out var row,
                    out var identityBlockedReason))
            {
                blockedReason = identityBlockedReason;
                return false;
            }

            if (row.MinLevel != row.MaxLevel)
            {
                blockedReason = "The verified base partner row does not contain one fixed level.";
                return false;
            }

            blockedReason = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            blockedReason = $"Verified base PokemonData could not be read: {exception.Message}";
            return false;
        }
    }

    private static IReadOnlyList<ZaEncounterEditableField> CreatePlayerPartnerEditableFields(
        IReadOnlyList<ZaEncounterEditableField> editableFields)
    {
        var fieldsById = editableFields.ToDictionary(field => field.Field, StringComparer.Ordinal);
        var levelField = new ZaEncounterEditableField(
            PlayerPartnerLevelField,
            "Level",
            "integer",
            1,
            100,
            Array.Empty<ZaEncounterEditableFieldOption>());
        return
        [
            fieldsById[SpeciesIdField],
            fieldsById[FormField],
            levelField,
            fieldsById[HeldItemIdField],
            fieldsById[GenderField],
            fieldsById[AbilityField],
            fieldsById[NatureField],
            fieldsById[ShinyModeField],
            fieldsById[FlawlessIvCountField],
            fieldsById[IvHpField],
            fieldsById[IvAttackField],
            fieldsById[IvDefenseField],
            fieldsById[IvSpecialAttackField],
            fieldsById[IvSpecialDefenseField],
            fieldsById[IvSpeedField],
            fieldsById[Move1IdField],
            fieldsById[Move2IdField],
            fieldsById[Move3IdField],
            fieldsById[Move4IdField],
        ];
    }

    internal static ZaEncounterEditableField? GetEditableField(
        ZaEncountersWorkflow workflow,
        string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static string CreateSlotRecordId(string tableId, int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{tableId}#{slot}");
    }

    internal static string CreatePokemonDataRecordId(int sourceIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{PokemonDataRecordIdPrefix}{sourceIndex}");
    }

    internal static string CreateAppearanceRecordId(string tableId)
    {
        return $"{tableId}{AppearanceRecordIdSuffix}";
    }

    internal static bool TryParsePokemonDataRecordId(string? recordId, out int sourceIndex)
    {
        sourceIndex = -1;
        return recordId?.StartsWith(PokemonDataRecordIdPrefix, StringComparison.Ordinal) == true
            && int.TryParse(
                recordId[PokemonDataRecordIdPrefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sourceIndex)
            && sourceIndex >= 0;
    }

    internal static bool TryParseSlotRecordId(string? recordId, out string tableId, out int slot)
    {
        tableId = string.Empty;
        slot = -1;

        var separatorIndex = recordId?.LastIndexOf('#') ?? -1;
        if (separatorIndex <= 0 || separatorIndex >= recordId!.Length - 1)
        {
            return false;
        }

        tableId = recordId[..separatorIndex];
        return int.TryParse(recordId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 0;
    }

    internal static bool TryParseAppearanceRecordId(string? recordId, out string tableId)
    {
        tableId = string.Empty;
        if (recordId?.EndsWith(AppearanceRecordIdSuffix, StringComparison.Ordinal) != true
            || recordId.Length == AppearanceRecordIdSuffix.Length)
        {
            return false;
        }

        tableId = recordId[..^AppearanceRecordIdSuffix.Length];
        return true;
    }

    internal static bool TryParseTableId(string? tableId, out int groupIndex, out int spawnerIndex)
    {
        groupIndex = -1;
        spawnerIndex = -1;

        var prefix = $"{TableIdPrefix}:";
        if (tableId?.StartsWith(prefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        var parts = tableId[prefix.Length..].Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out groupIndex)
            && groupIndex >= 0
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out spawnerIndex)
            && spawnerIndex >= 0;
    }

    internal static string FormatEncounterSpeciesLabel(int speciesId, int form, ZaTextLabelLookup labels)
    {
        return FormatEncounterSpeciesLabel(speciesId, form, labels.Pokemon(speciesId));
    }

    internal static string FormatEncounterSpeciesLabel(int speciesId, int form, string speciesName)
    {
        return ZaLabels.PokemonWithForm(speciesId, form, speciesName);
    }

    private static IEnumerable<ZaEncounterTableRecord> LoadTables(
        ZaWorkflowFile spawnerSource,
        ZaWorkflowFile encounterSource,
        IReadOnlyList<ZaBossBattleConsumerRecord>? bossBattleConsumers,
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var pokemonRows = ZaEncounterDataDocument.Parse(encounterSource.Bytes)
            .Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var table = PokemonSpawnerDataDBArray.GetRootAsPokemonSpawnerDataDBArray(new ByteBuffer(spawnerSource.Bytes));
        var displayOrder = ZaPokemonSpawnerDisplayOrder.Create(table);
        var scalarSpawners = ZaPokemonSpawnerDataDocument.Parse(spawnerSource.Bytes)
            .Entries
            .ToDictionary(
                entry => (entry.GroupIndex, entry.SpawnerIndex),
                entry => entry);
        var availableSpawnerIds = new List<string>();
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
                if (spawner is not null
                    && spawner.Value.EncountDataInfoListLength > 0
                    && !string.IsNullOrWhiteSpace(spawner.Value.Id))
                {
                    availableSpawnerIds.Add(spawner.Value.Id!);
                }
            }
        }

        var bossBattleContextResolver = new ZaBossBattleContextResolver(
            bossBattleConsumers,
            availableSpawnerIds);
        var reportedInvalidAlphaChanceSources = new HashSet<int>();
        var reportedInvalidAlphaLevelBonusSources = new HashSet<int>();
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
                if (spawner is null || spawner.Value.EncountDataInfoListLength == 0)
                {
                    continue;
                }

                var displayPosition = displayOrder[(groupIndex, spawnerIndex)];
                var locationKey = displayPosition.LocationKey;
                scalarSpawners.TryGetValue(
                    (groupIndex, spawnerIndex),
                    out var scalarSpawner);
                if (scalarSpawner is not null
                    && !string.Equals(scalarSpawner.Id, spawner.Value.Id, StringComparison.Ordinal))
                {
                    diagnostics.Add(ZaWorkflowSupport.Warning(
                        $"Spawner '{spawner.Value.Id}' could not be matched safely to its exact-byte scalar storage. "
                        + "Weight and population fields will remain read-only and be preserved.",
                        spawnerSource.RelativePath,
                        WeightField,
                        "Matching generated and exact-byte spawner identities"));
                    scalarSpawner = null;
                }

                var appearanceCounts = ReadAppearanceCounts(spawner.Value, scalarSpawner);
                if (appearanceCounts.ObjectCount > 0
                    && !appearanceCounts.HasUniformReadableValues)
                {
                    diagnostics.Add(ZaWorkflowSupport.Warning(
                        $"Spawner '{spawner.Value.Id}' has missing or mixed appearance count values. "
                        + "Overall minimum and maximum counts will remain read-only and be preserved.",
                        spawnerSource.RelativePath,
                        AppearanceMinCountField,
                        "Matching count values on every appearance object"));
                }
                else if (appearanceCounts.ObjectCount > 0
                    && !appearanceCounts.CanEdit)
                {
                    diagnostics.Add(ZaWorkflowSupport.Warning(
                        $"Spawner '{spawner.Value.Id}' stores at least one appearance count as an omitted default. "
                        + "The current uniform count range will remain visible but read-only.",
                        spawnerSource.RelativePath,
                        AppearanceMinCountField,
                        "Materialized minimum and maximum counts on every appearance object"));
                }

                var slots = ReadSlots(
                    spawner.Value,
                    scalarSpawner,
                    pokemonRows,
                    encounterSource,
                    labels,
                    pokemonAvailability,
                    IsNumberedWildZone(locationKey),
                    appearanceCounts,
                    diagnostics,
                    reportedInvalidAlphaChanceSources,
                    reportedInvalidAlphaLevelBonusSources).ToArray();
                if (slots.Length == 0)
                {
                    continue;
                }

                var bossBattleContext = bossBattleContextResolver.Resolve(
                    spawner.Value.Id,
                    slots.Select(slot => slot.EncounterDataId));
                var spawnerCategory = GetSpawnerCategory(locationKey, spawner.Value.Id);
                var location = FormatLocation(locationKey, labels);
                yield return new ZaEncounterTableRecord(
                    CreateTableId(groupIndex, spawnerIndex),
                    location,
                    FormatArea(spawner.Value, labels),
                    FormatEncounterType(spawner.Value),
                    GameVersionLabel,
                    spawnerSource.RelativePath,
                    slots,
                    new ZaEncounterProvenance(
                        spawnerSource.RelativePath,
                        spawnerSource.SourceLayer,
                        spawnerSource.FileState),
                    locationKey,
                    GetLocationSort(locationKey),
                    FormatTableLabel(locationKey, displayPosition.Ordinal, spawner.Value.Id, labels),
                    FormatTableDetails(slots),
                    ZaLumioseLocationLabels.GetMissionDetails(locationKey))
                {
                    SpawnerCategory = spawnerCategory,
                    RawSpawnerId = spawner.Value.Id,
                    PhaseConditions = ReadPhaseConditions(spawner.Value),
                    IsPostgame = HasPostgamePhaseCondition(spawner.Value),
                    BossBattleContextKey = bossBattleContext?.PrimaryContext.Key,
                    BossBattleContextLabel = bossBattleContext?.PrimaryContext.Label,
                    BossBattleContextRank = bossBattleContext?.PrimaryContext.Rank,
                    BossBattleWaveLabel = bossBattleContext?.WaveLabel,
                    BossBattleWaveRank = bossBattleContext?.WaveRank,
                    BossBattleContexts = bossBattleContext?.Contexts,
                };
            }
        }
    }

    private static IEnumerable<ZaEncounterSlotRecord> ReadSlots(
        PokemonSpawnerData spawner,
        ZaPokemonSpawnerDataEntry? scalarSpawner,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> pokemonRows,
        ZaWorkflowFile encounterSource,
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability,
        bool isNumberedWildZone,
        AppearanceCountSummary appearanceCounts,
        ICollection<ValidationDiagnostic> diagnostics,
        ISet<int> reportedInvalidAlphaChanceSources,
        ISet<int> reportedInvalidAlphaLevelBonusSources)
    {
        for (var slot = 0; slot < spawner.EncountDataInfoListLength; slot++)
        {
            var encounter = spawner.EncountDataInfoList(slot);
            if (encounter is null)
            {
                continue;
            }

            var scalarSlot = scalarSpawner is not null
                && slot < scalarSpawner.EncountDataInfoList.Count
                ? scalarSpawner.EncountDataInfoList[slot]
                : null;
            var encounterDataId = encounter.Value.EncountDataId ?? string.Empty;
            var hasMatchingScalarSlot = scalarSlot is not null
                && string.Equals(
                    scalarSlot.EncountDataId ?? string.Empty,
                    encounterDataId,
                    StringComparison.Ordinal)
                && scalarSlot.Weight == encounter.Value.Weight
                && scalarSlot.MaxCount == encounter.Value.MaxCount;
            var hasStructuralAlphaReference = HasStructuralAlphaReference(encounterDataId);
            var pokemon = ResolvePokemonRow(encounterDataId, pokemonRows);
            var encounterPokemon = pokemon as ZaEncounterDataEntry;
            var strengthenValue = encounterPokemon?.StrengthenValue;
            var speciesId = pokemon?.DevNo ?? 0;
            var form = pokemon?.FormNo ?? 0;
            var alphaChancePercent = pokemon is not null
                && TryReadAlphaChancePercent(pokemon.OyabunProbability, out var wholeAlphaChancePercent)
                    ? wholeAlphaChancePercent
                    : (int?)null;
            if (pokemon is not null
                && alphaChancePercent is null
                && reportedInvalidAlphaChanceSources.Add(pokemon.SourceIndex))
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Shared Alpha chance for encounter row '{pokemon.Id}' is {pokemon.OyabunProbability.ToString(CultureInfo.InvariantCulture)} percent. "
                    + "Only whole-number percentages from 0 through 100 are editable; this value will remain read-only and be preserved.",
                    encounterSource.RelativePath,
                    AlphaChancePercentField,
                    "Whole-number shared Alpha chance from 0 through 100"));
            }

            var alphaLevelBonus = pokemon is not null
                && pokemon.OyabunAdditionalLevel is >= 0 and <= 100
                    ? pokemon.OyabunAdditionalLevel
                    : (int?)null;
            if (pokemon is not null
                && alphaLevelBonus is null
                && reportedInvalidAlphaLevelBonusSources.Add(pokemon.SourceIndex))
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Shared Alpha level bonus for encounter row '{pokemon.Id}' is {pokemon.OyabunAdditionalLevel.ToString(CultureInfo.InvariantCulture)}. "
                    + "Only values from 0 through 100 are editable; this value will remain read-only and be preserved.",
                    encounterSource.RelativePath,
                    AlphaLevelBonusField,
                    "Shared Alpha level bonus from 0 through 100"));
            }

            yield return new ZaEncounterSlotRecord(
                slot,
                pokemon?.SourceIndex ?? -1,
                pokemon is null ? null : CreatePokemonDataRecordId(pokemon.SourceIndex),
                encounterDataId,
                speciesId,
                pokemon is null
                    ? FormatUnresolvedEncounterData(encounterDataId)
                    : FormatEncounterSpeciesLabel(speciesId, form, labels),
                form,
                pokemon?.MinLevel ?? 0,
                pokemon?.MaxLevel ?? 0,
                encounter.Value.Weight,
                FormatTimeCondition(encounter.Value.AppearedTimeCondition),
                FormatWeatherCondition(encounter.Value.AppearedWeatherCondition),
                hasStructuralAlphaReference,
                FormatEncounterKind(pokemon?.OyabunProbability),
                new ZaEncounterProvenance(
                    encounterSource.RelativePath,
                    encounterSource.SourceLayer,
                    encounterSource.FileState),
                isNumberedWildZone ? encounter.Value.ShowMapIcon == 0 : null,
                alphaChancePercent,
                alphaLevelBonus,
                pokemon?.OyabunProbability > 0)
            {
                SlotMaxCount = encounter.Value.MaxCount,
                CanEditWeight = hasMatchingScalarSlot && scalarSlot!.CanEditWeight,
                CanEditSlotMaxCount = hasMatchingScalarSlot && scalarSlot!.CanEditMaxCount,
                AppearanceMinCount = appearanceCounts.Minimum,
                AppearanceMaxCount = appearanceCounts.Maximum,
                AppearanceObjectCount = appearanceCounts.ObjectCount,
                CanEditAppearanceCounts = appearanceCounts.CanEdit,
                CanEditAppearanceMinCount = appearanceCounts.CanEditMinimum,
                CanEditAppearanceMaxCount = appearanceCounts.CanEditMaximum,
                FormOptions = CreateFormOptions(
                    speciesId,
                    labels.Pokemon(speciesId),
                    pokemonAvailability),
                HeldItemId = pokemon is null ? null : pokemon.HoldItem ?? 0,
                Ability = pokemon?.Tokusei,
                Nature = pokemon?.Seikaku,
                Gender = pokemon?.Sex,
                ShinyMode = pokemon?.Rare,
                MoveIds = pokemon is null
                    ? null
                    : pokemon.WazaList?.Values.Take(4).ToArray() ?? [0, 0, 0, 0],
                HasExplicitMoves = pokemon?.WazaList is not null,
                FlawlessIvCount = pokemon is null
                    ? null
                    : ZaPokemonDataIvEncoding.ReadFlawlessIvCount(pokemon),
                IvHp = ReadIv(pokemon?.TalentValue, stats => stats.HP),
                IvAttack = ReadIv(pokemon?.TalentValue, stats => stats.Attack),
                IvDefense = ReadIv(pokemon?.TalentValue, stats => stats.Defense),
                IvSpecialAttack = ReadIv(pokemon?.TalentValue, stats => stats.SpecialAttack),
                IvSpecialDefense = ReadIv(pokemon?.TalentValue, stats => stats.SpecialDefense),
                IvSpeed = ReadIv(pokemon?.TalentValue, stats => stats.Speed),
                TalentScale = pokemon?.TalentScale,
                TalentVCount = pokemon?.TalentVNum,
                StrengthenHp = strengthenValue?.HP,
                StrengthenAttack = strengthenValue?.Attack,
                StrengthenDefense = strengthenValue?.Defense,
                StrengthenSpecialAttack = strengthenValue?.SpecialAttack,
                StrengthenSpecialDefense = strengthenValue?.SpecialDefense,
                StrengthenSpeed = strengthenValue?.Speed,
                CanEditStrengthenValues = CanEditStrengthenValues(strengthenValue),
                EncounterActivationConditions = FormatEncounterActivationConditions(pokemon),
                StrengthenValueSummary = FormatStats(strengthenValue),
                ItemDropSummaries = FormatItemDrops(encounterPokemon),
            };
        }
    }

    private static int? ReadIv(
        ZaPokemonDataStatsRecord? stats,
        Func<ZaPokemonDataStatsRecord, int> select)
    {
        return stats is null ? -1 : select(stats);
    }

    private static IReadOnlyList<string> FormatEncounterActivationConditions(
        ZaPokemonDataEntry? pokemon)
    {
        if (pokemon is null)
        {
            return Array.Empty<string>();
        }

        return pokemon.ActivationConditions
            .SelectMany((condition, conditionIndex) =>
                condition.Elements.SelectMany((element, elementIndex) =>
                    element.Params.Select(parameter =>
                    {
                        var name = string.IsNullOrWhiteSpace(parameter.Condition)
                            ? "<unnamed>"
                            : parameter.Condition;
                        var values = parameter.Params
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value!)
                            .ToArray();
                        var valueSummary = $"[{string.Join(", ", values)}]";
                        return string.Create(
                            CultureInfo.InvariantCulture,
                            $"{name} | op={parameter.Op} | group={conditionIndex + 1}.{elementIndex + 1} | params={valueSummary}");
                    })))
            .ToArray();
    }

    private static string? FormatStats(ZaPokemonDataStatsRecord? stats)
    {
        return stats is null ? null : FormatStrengthenValues(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    internal static string? FormatStrengthenValues(
        int? hp,
        int? attack,
        int? defense,
        int? specialAttack,
        int? specialDefense,
        int? speed)
    {
        return hp is null
            || attack is null
            || defense is null
            || specialAttack is null
            || specialDefense is null
            || speed is null
                ? null
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"HP {FormatStrengthenValue(hp.Value)} / Atk {FormatStrengthenValue(attack.Value)} / Def {FormatStrengthenValue(defense.Value)} / SpA {FormatStrengthenValue(specialAttack.Value)} / SpD {FormatStrengthenValue(specialDefense.Value)} / Spe {FormatStrengthenValue(speed.Value)}");
    }

    private static string FormatStrengthenValue(int value)
    {
        return value > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{value / 10m:0.0}x (stored {value})")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"override disabled (stored {value})");
    }

    private static bool CanEditStrengthenValues(ZaPokemonDataStatsRecord? stats)
    {
        return stats is not null
            && stats.HP is >= MinimumStrengthenValue and <= MaximumStrengthenHpValue
            && stats.Attack is >= MinimumStrengthenValue and <= MaximumStrengthenOtherValue
            && stats.Defense is >= MinimumStrengthenValue and <= MaximumStrengthenOtherValue
            && stats.SpecialAttack is >= MinimumStrengthenValue and <= MaximumStrengthenOtherValue
            && stats.SpecialDefense is >= MinimumStrengthenValue and <= MaximumStrengthenOtherValue
            && stats.Speed is >= MinimumStrengthenValue and <= MaximumStrengthenOtherValue;
    }

    private static IReadOnlyList<string> FormatItemDrops(ZaEncounterDataEntry? pokemon)
    {
        if (pokemon is null)
        {
            return Array.Empty<string>();
        }

        return pokemon.ItemDrops.Select(drop =>
        {
            var itemTable = string.IsNullOrWhiteSpace(drop.ItemTableId)
                ? "<none>"
                : drop.ItemTableId;
            var count = drop.MinCount == drop.MaxCount
                ? drop.MinCount.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{drop.MinCount}-{drop.MaxCount}");
            var conditions = $"[{string.Join(", ", drop.DropConditions)}]";
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{itemTable} | probability={drop.DropProbability} | count={count} | conditions={conditions}");
        }).ToArray();
    }

    private static bool TryReadAlphaChancePercent(float value, out int wholePercent)
    {
        if (float.IsFinite(value)
            && value >= 0
            && value <= 100
            && value == MathF.Truncate(value))
        {
            wholePercent = checked((int)value);
            return true;
        }

        wholePercent = 0;
        return false;
    }

    private static string FormatEncounterKind(float? alphaChancePercent)
    {
        return alphaChancePercent switch
        {
            100 => "Guaranteed Alpha",
            > 0 and < 100 => "Alpha Chance",
            0 => "Wild",
            null => "Unresolved",
            _ => "Invalid Alpha Chance",
        };
    }

    private static ZaPokemonDataEntry? ResolvePokemonRow(
        string encounterDataId,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> pokemonRows)
    {
        if (string.IsNullOrWhiteSpace(encounterDataId))
        {
            return null;
        }

        if (pokemonRows.TryGetValue(encounterDataId, out var exactRow))
        {
            return exactRow;
        }

        var normalizedId = ZaEncounterDataIds.NormalizeSpawnerEncounterDataId(encounterDataId);
        if (!string.Equals(normalizedId, encounterDataId, StringComparison.Ordinal)
            && pokemonRows.TryGetValue(normalizedId, out var suffixedRow))
        {
            return suffixedRow;
        }

        return null;
    }

    private static bool HasStructuralAlphaReference(string encounterDataId)
    {
        return ZaEncounterDataIds.IsAlphaSpawnerEncounterDataId(encounterDataId);
    }

    private static string FormatUnresolvedEncounterData(string encounterDataId)
    {
        return string.IsNullOrWhiteSpace(encounterDataId)
            ? "Unresolved encounter data"
            : $"Unresolved encounter data ({encounterDataId})";
    }

    private static IReadOnlyList<ZaEncounterEditableField> CreateEditableFields(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        var speciesOptions = CreateSpeciesOptions(labels, pokemonAvailability);
        var speciesMaximumValue = Math.Max(
            labels.PokemonNameCount - 1,
            speciesOptions.Count > 0 ? speciesOptions.Max(option => option.Value) : 0);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateMoveOptions(labels);
        return
        [
            new(
                SpeciesIdField,
                "Species",
                "integer",
                0,
                speciesMaximumValue,
                speciesOptions),
            new(FormField, "Form", "integer", 0, short.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(HeldItemIdField, "Held item", "integer", 0, MaximumOptionValue(itemOptions, int.MaxValue), itemOptions),
            new(GenderField, "Gender", "integer", -1, 2, GenderOptions),
            new(AbilityField, "Ability mode", "integer", 0, 255, AbilityModeOptions),
            new(NatureField, "Nature", "integer", -1, 25, NatureOptions),
            new(
                ShinyModeField,
                "Shiny lock",
                "integer",
                ZaPokemonDataConstants.RareNotShiny,
                ZaPokemonDataConstants.RareDefaultShinyRoll,
                ShinyModeOptions),
            new(LevelMinField, "Min Level", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(LevelMaxField, "Max Level", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(AlphaChancePercentField, "Alpha Chance (%)", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(AlphaLevelBonusField, "Alpha Level Bonus", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(FlawlessIvCountField, "IV preset", "integer", 0, 6, FlawlessIvCountOptions),
            new(IvHpField, "HP IV", "integer", -1, 31, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(IvAttackField, "Attack IV", "integer", -1, 31, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(IvDefenseField, "Defense IV", "integer", -1, 31, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(IvSpecialAttackField, "Sp. Atk IV", "integer", -1, 31, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(IvSpecialDefenseField, "Sp. Def IV", "integer", -1, 31, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(IvSpeedField, "Speed IV", "integer", -1, 31, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(StrengthenHpField, "HP multiplier (stored tenths)", "integer", MinimumStrengthenValue, MaximumStrengthenHpValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(StrengthenAttackField, "Attack multiplier (stored tenths)", "integer", MinimumStrengthenValue, MaximumStrengthenOtherValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(StrengthenDefenseField, "Defense multiplier (stored tenths)", "integer", MinimumStrengthenValue, MaximumStrengthenOtherValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(StrengthenSpecialAttackField, "Sp. Atk multiplier (stored tenths)", "integer", MinimumStrengthenValue, MaximumStrengthenOtherValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(StrengthenSpecialDefenseField, "Sp. Def multiplier (stored tenths)", "integer", MinimumStrengthenValue, MaximumStrengthenOtherValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(StrengthenSpeedField, "Speed multiplier (stored tenths)", "integer", MinimumStrengthenValue, MaximumStrengthenOtherValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(Move1IdField, "Move 1", "integer", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            new(Move2IdField, "Move 2", "integer", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            new(Move3IdField, "Move 3", "integer", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            new(Move4IdField, "Move 4", "integer", -1, MaximumOptionValue(moveOptions, ushort.MaxValue), moveOptions),
            new(WeightField, "Weight", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(SlotMaxCountField, "Slot Max Count", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(AppearanceMinCountField, "Overall Min Count", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(AppearanceMaxCountField, "Overall Max Count", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
        ];
    }

    private static IReadOnlyList<ZaEncounterEditableFieldOption> CreateMoveOptions(
        ZaTextLabelLookup labels)
    {
        return
        [
            new(ZaPokemonDataConstants.MoveNone, ZaPokemonDataConstants.MoveNoneLabel),
            new(ZaPokemonDataConstants.MoveAuto, "Default moves"),
            .. CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: false),
        ];
    }

    private static int MaximumOptionValue(
        IReadOnlyList<ZaEncounterEditableFieldOption> options,
        int fallback)
    {
        return options.Count == 0 ? fallback : options.Max(option => option.Value);
    }

    private static AppearanceCountSummary ReadAppearanceCounts(
        PokemonSpawnerData spawner,
        ZaPokemonSpawnerDataEntry? scalarSpawner)
    {
        var objectCount = spawner.AppearanceSpawnerObjectInfoListLength;
        if (objectCount == 0)
        {
            return new AppearanceCountSummary(0, null, null, false, false);
        }

        int? minimum = null;
        int? maximum = null;
        var hasMatchingScalarShape = scalarSpawner is not null
            && scalarSpawner.AppearanceSpawnerObjectInfoList.Count == objectCount;
        var canEditMinimum = hasMatchingScalarShape;
        var canEditMaximum = hasMatchingScalarShape;
        for (var index = 0; index < objectCount; index++)
        {
            var objectInfo = spawner.AppearanceSpawnerObjectInfoList(index);
            var appearanceInfo = objectInfo?.AppearanceInfo;
            if (appearanceInfo is null)
            {
                return new AppearanceCountSummary(objectCount, null, null, false, false);
            }

            var scalarObjectInfo = scalarSpawner is not null
                && index < scalarSpawner.AppearanceSpawnerObjectInfoList.Count
                ? scalarSpawner.AppearanceSpawnerObjectInfoList[index]
                : null;
            var scalarAppearanceInfo = scalarObjectInfo?.AppearanceInfo;
            if (scalarAppearanceInfo is null
                || !string.Equals(
                    scalarObjectInfo!.ObjectName ?? string.Empty,
                    objectInfo!.Value.ObjectName ?? string.Empty,
                    StringComparison.Ordinal)
                || scalarAppearanceInfo.MinCount != appearanceInfo.Value.MinCount
                || scalarAppearanceInfo.MaxCount != appearanceInfo.Value.MaxCount)
            {
                canEditMinimum = false;
                canEditMaximum = false;
            }
            else
            {
                canEditMinimum &= scalarAppearanceInfo.CanEditMinCount;
                canEditMaximum &= scalarAppearanceInfo.CanEditMaxCount;
            }

            if (minimum is null)
            {
                minimum = appearanceInfo.Value.MinCount;
                maximum = appearanceInfo.Value.MaxCount;
                continue;
            }

            if (minimum.Value != appearanceInfo.Value.MinCount
                || maximum!.Value != appearanceInfo.Value.MaxCount)
            {
                return new AppearanceCountSummary(objectCount, null, null, false, false);
            }
        }

        return new AppearanceCountSummary(
            objectCount,
            minimum,
            maximum,
            canEditMinimum,
            canEditMaximum);
    }

    private static IReadOnlyList<ZaEncounterEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new ZaEncounterEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaEncounterEditableFieldOption> CreateSpeciesOptions(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        return pokemonAvailability
            .CreateSpeciesOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true)
            .Select(option => new ZaEncounterEditableFieldOption(option.Value, option.Label)
            {
                FormOptions = CreateSpeciesFormOptions(
                    option.Value,
                    labels,
                    pokemonAvailability),
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaEncounterEditableFieldOption>? CreateSpeciesFormOptions(
        int speciesId,
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        if (speciesId == 0)
        {
            return [new ZaEncounterEditableFieldOption(0, ZaLabels.PokemonFormLabel(0, 0, "None"))];
        }

        if (!pokemonAvailability.HasKnownAvailability)
        {
            return null;
        }

        return CreateFormOptions(speciesId, labels.Pokemon(speciesId), pokemonAvailability);
    }

    internal static IReadOnlyList<ZaEncounterEditableFieldOption> CreateFormOptions(
        int speciesId,
        string speciesName,
        ZaPokemonAvailability pokemonAvailability)
    {
        return pokemonAvailability.CreateFormOptions(
            speciesId,
            form => new ZaEncounterEditableFieldOption(
                form,
                ZaLabels.PokemonFormLabel(speciesId, form, speciesName)));
    }

    private static string CreateTableId(int groupIndex, int spawnerIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{TableIdPrefix}:{groupIndex}:{spawnerIndex}");
    }

    private static string FormatLocation(string locationKey, ZaTextLabelLookup labels)
    {
        return ZaLumioseLocationLabels.FormatLocation(
            locationKey,
            labels.PlaceName,
            labels.Pokemon,
            labels.MissionTitle);
    }

    private static int? GetLocationSort(string locationKey)
    {
        return ZaLumioseLocationLabels.GetLocationSort(locationKey);
    }

    private static string? GetSpawnerCategory(string locationKey, string? spawnerId)
    {
        if (!locationKey.StartsWith("outzone_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(spawnerId)
            ? ZaLumioseLocationLabels.OtherSpawnerCategory
            : ZaLumioseLocationLabels.ClassifyRawSpawnerId(spawnerId)
                ?? ZaLumioseLocationLabels.OtherSpawnerCategory;
    }

    private static bool IsNumberedWildZone(string locationKey)
    {
        return ZaLumioseLocationLabels.IsNumberedWildZone(locationKey);
    }

    private static IReadOnlyList<ZaEncounterPhaseCondition> ReadPhaseConditions(
        PokemonSpawnerData spawner)
    {
        var phaseConditions = new List<ZaEncounterPhaseCondition>();
        for (var conditionIndex = 0; conditionIndex < spawner.ActivationConditionLength; conditionIndex++)
        {
            var condition = spawner.ActivationCondition(conditionIndex);
            if (condition is null)
            {
                continue;
            }

            for (var elementIndex = 0; elementIndex < condition.Value.ElementLength; elementIndex++)
            {
                var element = condition.Value.Element(elementIndex);
                if (element is null)
                {
                    continue;
                }

                for (var parameterIndex = 0; parameterIndex < element.Value.ParamLength; parameterIndex++)
                {
                    var parameter = element.Value.Param(parameterIndex);
                    if (parameter is null
                        || !string.Equals(parameter.Value.Condition, PhaseCondition, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var values = new List<string>(parameter.Value.ParamLength);
                    for (var valueIndex = 0; valueIndex < parameter.Value.ParamLength; valueIndex++)
                    {
                        var value = parameter.Value.Param(valueIndex);
                        if (value is not null)
                        {
                            values.Add(value);
                        }
                    }

                    phaseConditions.Add(new ZaEncounterPhaseCondition(parameter.Value.Op, values));
                }
            }
        }

        return phaseConditions;
    }

    private static bool HasPostgamePhaseCondition(PokemonSpawnerData spawner)
    {
        for (var conditionIndex = 0; conditionIndex < spawner.ActivationConditionLength; conditionIndex++)
        {
            var condition = spawner.ActivationCondition(conditionIndex);
            if (condition is null)
            {
                continue;
            }

            for (var elementIndex = 0; elementIndex < condition.Value.ElementLength; elementIndex++)
            {
                var element = condition.Value.Element(elementIndex);
                if (element is null)
                {
                    continue;
                }

                for (var parameterIndex = 0; parameterIndex < element.Value.ParamLength; parameterIndex++)
                {
                    var parameter = element.Value.Param(parameterIndex);
                    if (parameter is null
                        || !string.Equals(parameter.Value.Condition, PhaseCondition, StringComparison.Ordinal)
                        || parameter.Value.Op != CurrentPhaseAtLeastComparison
                        || parameter.Value.ParamLength != 1)
                    {
                        continue;
                    }

                    if (int.TryParse(
                            parameter.Value.Param(0),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var phaseThreshold)
                        && phaseThreshold >= PostgamePhaseThreshold)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string FormatTableLabel(string locationKey, int tableNumber, string? spawnerId, ZaTextLabelLookup labels)
    {
        if (IsNumberedWildZone(locationKey))
        {
            return $"Spawner {tableNumber.ToString(CultureInfo.InvariantCulture)}";
        }

        return string.IsNullOrWhiteSpace(spawnerId)
            ? $"Spawner {tableNumber.ToString(CultureInfo.InvariantCulture)}"
            : ZaLumioseLocationLabels.FormatRawSpawnerId(
                spawnerId,
                labels.Pokemon,
                labels.MissionTitle);
    }

    private static string FormatTableDetails(IReadOnlyList<ZaEncounterSlotRecord> slots)
    {
        if (slots.Count == 0)
        {
            return "No slots";
        }

        var species = slots
            .Select(FormatSlotPreviewSpecies)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        var speciesLabel = species.Length == 0 ? "No species" : string.Join(", ", species);
        var additionalCount = slots
            .Select(FormatSlotPreviewSpecies)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .Skip(3)
            .Count();
        if (additionalCount > 0)
        {
            speciesLabel = $"{speciesLabel} + {additionalCount.ToString(CultureInfo.InvariantCulture)} more";
        }

        var slotLabel = slots.Count == 1 ? "slot" : "slots";
        var weightTotal = slots.Sum(slot => (long)slot.Weight);
        var alphaCount = slots.Count(slot => slot.IsAlpha);
        var alphaLabel = alphaCount == 0
            ? string.Empty
            : $" - {alphaCount.ToString(CultureInfo.InvariantCulture)} Alpha";
        return $"{speciesLabel} - {slots.Count.ToString(CultureInfo.InvariantCulture)} {slotLabel} - total weight {weightTotal.ToString(CultureInfo.InvariantCulture)}{alphaLabel}";
    }

    private static string FormatSlotPreviewSpecies(ZaEncounterSlotRecord slot)
    {
        return slot.IsAlpha ? $"{slot.Species} Alpha" : slot.Species;
    }

    private static string FormatArea(PokemonSpawnerData spawner, ZaTextLabelLookup labels)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        if (!string.IsNullOrWhiteSpace(objectInfo?.DungeonName))
        {
            return ZaLumioseLocationLabels.FormatLocation(
                objectInfo.Value.DungeonName,
                labels.PlaceName,
                labels.Pokemon,
                labels.MissionTitle);
        }

        if (!string.IsNullOrWhiteSpace(objectInfo?.BattleAreaId))
        {
            return ZaLumioseLocationLabels.FormatLocation(
                objectInfo.Value.BattleAreaId,
                labels.PlaceName,
                labels.Pokemon,
                labels.MissionTitle);
        }

        return "Pokemon Spawner";
    }

    private static string FormatEncounterType(PokemonSpawnerData spawner)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        if (objectInfo is null || objectInfo.Value.TagListLength == 0)
        {
            return "Wild Pokemon";
        }

        var tags = Enumerable
            .Range(0, objectInfo.Value.TagListLength)
            .Select(objectInfo.Value.TagList)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return tags.Length == 0 ? "Wild Pokemon" : string.Join(", ", tags);
    }

    private static AppearanceSpawnerObjectInfo? FirstAppearanceObject(PokemonSpawnerData spawner)
    {
        for (var index = 0; index < spawner.AppearanceSpawnerObjectInfoListLength; index++)
        {
            var objectInfo = spawner.AppearanceSpawnerObjectInfoList(index);
            if (objectInfo is not null)
            {
                return objectInfo;
            }
        }

        return null;
    }

    private static string? FormatTimeCondition(int value)
    {
        return value switch
        {
            0 => null,
            1 => "Day",
            2 => "Night",
            _ => $"Time condition {value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatWeatherCondition(int value)
    {
        return value switch
        {
            0 => "Any weather",
            1 => "Clear",
            2 => "Rain",
            3 => "Snow",
            4 => "Fog",
            _ => $"Weather condition {value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private readonly record struct AppearanceCountSummary(
        int ObjectCount,
        int? Minimum,
        int? Maximum,
        bool CanEditMinimum,
        bool CanEditMaximum)
    {
        public bool HasUniformReadableValues => Minimum is not null && Maximum is not null;

        public bool CanEdit => CanEditMinimum && CanEditMaximum;
    }
}
