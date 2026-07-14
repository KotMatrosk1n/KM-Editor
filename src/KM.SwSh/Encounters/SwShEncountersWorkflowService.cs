// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Encounters;

public sealed class SwShEncountersWorkflowService
{
    public const string SpeciesIdField = "speciesId";
    public const string FormField = "form";
    public const string ProbabilityField = "probability";
    public const string LevelMinField = "levelMin";
    public const string LevelMaxField = "levelMax";
    public const int MinimumSpeciesId = 0;
    public const int MaximumSpeciesId = ushort.MaxValue;
    public const int MinimumForm = 0;
    public const int MaximumForm = byte.MaxValue;
    public const int MinimumProbability = 0;
    public const int MaximumProbability = 100;
    public const int MinimumLevel = 0;
    public const int MaximumLevel = 100;
    public const string WildDataPath = "romfs/bin/archive/field/resident/data_table.gfpak";

    private const string EncountersEditDomain = "workflow.encounters";
    private const string MessageRootPath = "romfs/bin/message";
    private const string PreferredLanguage = "English";

    private static readonly IReadOnlyList<SwShEncounterEditableField> EditableFields =
    [
        new SwShEncounterEditableField(SpeciesIdField, "Species", "integer", MinimumSpeciesId, MaximumSpeciesId),
        new SwShEncounterEditableField(FormField, "Form", "integer", MinimumForm, MaximumForm),
        new SwShEncounterEditableField(ProbabilityField, "Probability", "integer", MinimumProbability, MaximumProbability),
        new SwShEncounterEditableField(LevelMinField, "Min Level", "integer", MinimumLevel, MaximumLevel),
        new SwShEncounterEditableField(LevelMaxField, "Max Level", "integer", MinimumLevel, MaximumLevel),
    ];

    private static readonly IReadOnlyList<WildArchiveMember> ArchiveMembers =
    [
        new WildArchiveMember("sword", "Sword", "symbol", "Symbol", "encount_symbol_k.bin"),
        new WildArchiveMember("sword", "Sword", "hidden", "Hidden", "encount_k.bin"),
        new WildArchiveMember("shield", "Shield", "symbol", "Symbol", "encount_symbol_t.bin"),
        new WildArchiveMember("shield", "Shield", "hidden", "Hidden", "encount_t.bin"),
    ];

    private static readonly string[] SubTableLabels =
    [
        "Normal",
        "Overcast",
        "Raining",
        "Thunderstorm",
        "Intense Sun",
        "Snowing",
        "Snowstorm",
        "Sandstorm",
        "Heavy Fog",
        "Shaking Trees",
        "Fishing",
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
                    "Encounters and Wild Data requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SwShEncounterTableRecord>(),
                sourceFileCount: 0,
                [],
                new HashSet<int>(),
                diagnostics);
        }

        var dataSource = ResolveWildDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Encounters and Wild Data is not available for this project.",
                expected: WildDataPath));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShEncounterTableRecord>(),
                sourceFileCount: 0,
                [],
                new HashSet<int>(),
                diagnostics);
        }

        var speciesNames = LoadSpeciesNames(project, diagnostics);
        var presentSpeciesIds = SwShSpeciesAvailability.LoadPresentSpeciesIds(project);

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
            var vanillaPack = LoadVanillaWildDataPack(project, dataSource, pack, diagnostics);
            var tables = new List<SwShEncounterTableRecord>();
            var provenance = CreateProvenance(dataSource.GraphEntry);

            foreach (var member in GetArchiveMembers(project.Paths.SelectedGame))
            {
                if (!pack.TryGetFileByName(member.FileName, out var memberData))
                {
                    continue;
                }

                var archive = SwShWildEncounterArchive.Parse(memberData);
                var vanillaArchive = vanillaPack.TryGetFileByName(member.FileName, out var vanillaMemberData)
                    ? SwShWildEncounterArchive.Parse(vanillaMemberData)
                    : null;
                tables.AddRange(FlattenArchive(archive, vanillaArchive, member, provenance, speciesNames));
            }

            if (tables.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Encounters and Wild Data source did not contain supported Sword/Shield encounter members.",
                    file: dataSource.GraphEntry.RelativePath,
                    expected: FormatExpectedArchiveMembers(project.Paths.SelectedGame)));
            }

            return CreateWorkflow(
                summary,
                tables,
                sourceFileCount: 1,
                speciesNames,
                presentSpeciesIds,
                diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters and Wild Data source is not supported: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield data_table.gfpak with EncounterArchive members"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShEncounterTableRecord>(),
                sourceFileCount: 1,
                speciesNames,
                presentSpeciesIds,
                diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters and Wild Data source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShEncounterTableRecord>(),
                sourceFileCount: 1,
                speciesNames,
                presentSpeciesIds,
                diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Encounters and Wild Data source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShEncounterTableRecord>(),
                sourceFileCount: 1,
                speciesNames,
                presentSpeciesIds,
                diagnostics);
        }
    }

    internal static bool IsEditableField(string? field)
    {
        return field
            is SpeciesIdField
            or FormField
            or ProbabilityField
            or LevelMinField
            or LevelMaxField;
    }

    internal static IReadOnlyList<WildArchiveMember> GetArchiveMembers(ProjectGame? selectedGame)
    {
        return selectedGame switch
        {
            ProjectGame.Sword => ArchiveMembers.Where(member => member.GameKey == "sword").ToArray(),
            ProjectGame.Shield => ArchiveMembers.Where(member => member.GameKey == "shield").ToArray(),
            _ => ArchiveMembers,
        };
    }

    internal static WorkflowFileSource? ResolveWildDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, WildDataPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    internal static WorkflowFileSource? ResolveBaseWildDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, WildDataPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry?.BaseFile is null)
        {
            return null;
        }

        var sourcePath = ResolveBaseSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
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

    internal static string CreateTableId(
        WildArchiveMember member,
        int tableIndex,
        ulong zoneId,
        int subTableIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{member.GameKey}:{member.KindKey}:{tableIndex}:{zoneId:X16}:{subTableIndex}");
    }

    internal static string CreateSlotRecordId(string tableId, int slot)
    {
        return $"{tableId}#{slot.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static bool TryParseSlotRecordId(string? recordId, out string tableId, out int slot)
    {
        tableId = string.Empty;
        slot = 0;

        var separatorIndex = recordId?.LastIndexOf('#') ?? -1;
        if (separatorIndex <= 0 || separatorIndex >= recordId!.Length - 1)
        {
            return false;
        }

        tableId = recordId[..separatorIndex];
        return int.TryParse(recordId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 1;
    }

    internal static bool TryParseTableId(
        string? tableId,
        out WildArchiveMember member,
        out int tableIndex,
        out ulong zoneId,
        out int subTableIndex)
    {
        member = ArchiveMembers[0];
        tableIndex = -1;
        zoneId = 0;
        subTableIndex = -1;

        var parts = tableId?.Split(':') ?? [];
        if (parts.Length != 5)
        {
            return false;
        }

        var match = ArchiveMembers.FirstOrDefault(candidate =>
            string.Equals(candidate.GameKey, parts[0], StringComparison.Ordinal)
            && string.Equals(candidate.KindKey, parts[1], StringComparison.Ordinal));
        if (match is null)
        {
            return false;
        }

        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out tableIndex)
            || tableIndex < 0
            || !ulong.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out zoneId)
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out subTableIndex)
            || subTableIndex < 0)
        {
            return false;
        }

        member = match;
        return true;
    }

    internal static string FormatEncounterSpeciesLabel(
        int speciesId,
        int form,
        IReadOnlyList<string> speciesNames)
    {
        if (speciesId == 0)
        {
            return "Empty";
        }

        var speciesName = GetLookupValue(speciesNames, speciesId, $"Species {speciesId}");
        return FormatEncounterSpeciesLabel(speciesId, form, speciesName);
    }

    internal static string FormatEncounterSpeciesLabel(
        int speciesId,
        int form,
        string speciesName)
    {
        return speciesId == 0
            ? "Empty"
            : SwShSpeciesFormLabels.FormatSpeciesFormLabel(speciesName, speciesId, form);
    }

    private static SwShEncountersWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShEncounterTableRecord> tables,
        int sourceFileCount,
        IReadOnlyList<string> speciesNames,
        IReadOnlySet<int> presentSpeciesIds,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShEncountersWorkflow(
            summary,
            tables.OrderBy(table => table.Location, StringComparer.Ordinal)
                .ThenBy(table => table.GameVersion, StringComparer.Ordinal)
                .ThenBy(table => table.Area, StringComparer.Ordinal)
                .ThenBy(table => table.TableId, StringComparer.Ordinal)
                .ToArray(),
            CreateEditableFields(speciesNames, presentSpeciesIds),
            new SwShEncountersWorkflowStats(
                tables.Count,
                tables.Sum(table => table.Slots.Count),
                sourceFileCount),
            diagnostics)
        {
            PresentSpeciesIds = presentSpeciesIds,
        };
    }

    private static IReadOnlyList<SwShEncounterEditableField> CreateEditableFields(
        IReadOnlyList<string> speciesNames,
        IReadOnlySet<int> presentSpeciesIds)
    {
        var speciesOptions = new[]
            {
                new SwShEncounterEditableFieldOption(0, "000 Empty"),
            }
            .Concat(SwShSpeciesAvailability.CreateSpeciesOptions(
                speciesNames,
                presentSpeciesIds,
                (value, label) => new SwShEncounterEditableFieldOption(value, label))
                .Where(option => option.Value != 0))
            .ToArray();

        return EditableFields
            .Select(field => field.Field == SpeciesIdField
                ? field with { Options = speciesOptions }
                : field)
            .ToArray();
    }

    private static IEnumerable<SwShEncounterTableRecord> FlattenArchive(
        SwShWildEncounterArchive archive,
        SwShWildEncounterArchive? vanillaArchive,
        WildArchiveMember member,
        SwShEncounterProvenance provenance,
        IReadOnlyList<string> speciesNames)
    {
        for (var tableIndex = 0; tableIndex < archive.Tables.Count; tableIndex++)
        {
            var table = archive.Tables[tableIndex];
            for (var subTableIndex = 0; subTableIndex < table.SubTables.Count; subTableIndex++)
            {
                var subTable = table.SubTables[subTableIndex];
                if (!IsVanillaSubTableAvailable(vanillaArchive, tableIndex, table.ZoneId, subTableIndex))
                {
                    continue;
                }

                var encounterType = FormatSubTable(subTableIndex);
                yield return new SwShEncounterTableRecord(
                    CreateTableId(member, tableIndex, table.ZoneId, subTableIndex),
                    FormatZone(table.ZoneId),
                    SwShEncounterZoneNames.FormatGroupKey(table.ZoneId),
                    member.AreaLabel,
                    encounterType,
                    member.GameLabel,
                    member.FileName,
                    subTable.Slots
                        .Select((slot, slotIndex) => ToSlotRecord(slotIndex, slot, subTable, encounterType, speciesNames))
                        .ToArray(),
                    provenance);
            }
        }
    }

    private static SwShEncounterSlotRecord ToSlotRecord(
        int slotIndex,
        SwShWildEncounterSlot slot,
        SwShWildEncounterSubTable subTable,
        string encounterType,
        IReadOnlyList<string> speciesNames)
    {
        return new SwShEncounterSlotRecord(
            slotIndex + 1,
            slot.Species,
            FormatEncounterSpeciesLabel(slot.Species, slot.Form, speciesNames),
            slot.Form,
            subTable.LevelMin,
            subTable.LevelMax,
            slot.Probability,
            TimeOfDay: null,
            Weather: encounterType);
    }

    private static string FormatZone(ulong zoneId)
    {
        return SwShEncounterZoneNames.Format(zoneId);
    }

    private static string FormatSubTable(int subTableIndex)
    {
        return (uint)subTableIndex < (uint)SubTableLabels.Length
            ? SubTableLabels[subTableIndex]
            : $"Subtable {subTableIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private static bool IsVanillaSubTableAvailable(
        SwShWildEncounterArchive? vanillaArchive,
        int tableIndex,
        ulong zoneId,
        int subTableIndex)
    {
        if (vanillaArchive is null)
        {
            return true;
        }

        var vanillaTable = GetMatchingVanillaTable(vanillaArchive, tableIndex, zoneId);
        if (vanillaTable is null || (uint)subTableIndex >= (uint)vanillaTable.SubTables.Count)
        {
            return false;
        }

        return HasVanillaPokemon(vanillaTable.SubTables[subTableIndex]);
    }

    private static SwShWildEncounterTable? GetMatchingVanillaTable(
        SwShWildEncounterArchive vanillaArchive,
        int tableIndex,
        ulong zoneId)
    {
        if ((uint)tableIndex < (uint)vanillaArchive.Tables.Count
            && vanillaArchive.Tables[tableIndex].ZoneId == zoneId)
        {
            return vanillaArchive.Tables[tableIndex];
        }

        return vanillaArchive.Tables.FirstOrDefault(table => table.ZoneId == zoneId);
    }

    private static bool HasVanillaPokemon(SwShWildEncounterSubTable subTable)
    {
        return subTable.Slots.Any(slot => slot.Species != 0);
    }

    private static SwShGfPackFile LoadVanillaWildDataPack(
        OpenedProject project,
        WorkflowFileSource activeSource,
        SwShGfPackFile activePack,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (activeSource.GraphEntry.LayeredFile is null)
        {
            return activePack;
        }

        var baseSource = ResolveBaseWildDataSource(project);
        if (baseSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Base wild encounter data is not available; using the active encounter archive to determine table availability.",
                expected: WildDataPath));
            return activePack;
        }

        return SwShGfPackFile.Parse(File.ReadAllBytes(baseSource.AbsolutePath));
    }

    private static string FormatExpectedArchiveMembers(ProjectGame? selectedGame)
    {
        return selectedGame switch
        {
            ProjectGame.Sword => "encount_symbol_k.bin or encount_k.bin inside data_table.gfpak",
            ProjectGame.Shield => "encount_symbol_t.bin or encount_t.bin inside data_table.gfpak",
            _ => "encount_symbol_k/t.bin or encount_k/t.bin inside data_table.gfpak",
        };
    }

    private static string[] LoadSpeciesNames(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var messageRoot = ResolveLanguageMessageRoot(project, diagnostics);
        if (messageRoot is null)
        {
            return [];
        }

        var relativePath = $"{messageRoot}/monsname.dat";
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Species names are not available; encounter slots will use numeric fallback labels.",
                expected: relativePath));
            return [];
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(sourcePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Species name table could not be decoded: {exception.Message}",
                file: relativePath,
                expected: "Sword/Shield message table"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Species name table could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Species name table could not be read: {exception.Message}",
                file: relativePath,
                expected: "Readable message table"));
            return [];
        }
    }

    private static string? ResolveLanguageMessageRoot(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var languages = project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
            .Select(entry => GetLanguage(entry.RelativePath))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (languages.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Species lookup text is not available; numeric fallback labels will be shown.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/monsname.dat"));
            return null;
        }

        var preferredLanguage = SwShGameTextLanguage.Resolve(project.Paths);
        var language = languages.Contains(preferredLanguage, StringComparer.OrdinalIgnoreCase)
            ? preferredLanguage
            : languages.Contains(PreferredLanguage, StringComparer.OrdinalIgnoreCase)
                ? PreferredLanguage
                : languages[0];

        if (!string.Equals(language, PreferredLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(preferredLanguage, PreferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"English species lookup text was not found; using '{language}' lookup tables instead.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/common/monsname.dat"));
        }

        return $"{MessageRootPath}/{language}/common";
    }

    private static string? GetLanguage(string relativePath)
    {
        if (!relativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var languageStart = MessageRootPath.Length + 1;
        var nextSeparator = relativePath.IndexOf('/', languageStart);

        return nextSeparator < 0
            ? null
            : relativePath[languageStart..nextSeparator];
    }

    private static string GetLookupValue(IReadOnlyList<string> values, int index, string fallback)
    {
        return (uint)index < (uint)values.Count && !string.IsNullOrWhiteSpace(values[index])
            ? values[index]
            : fallback;
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

    private static string? ResolveBaseSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
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

    private static SwShEncounterProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShEncounterProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Encounters,
            "Encounters and Wild Data",
            "Encounter tables, wild slots, levels, weather, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: EncountersEditDomain,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);

    internal sealed record WildArchiveMember(
        string GameKey,
        string GameLabel,
        string KindKey,
        string AreaLabel,
        string FileName);
}
