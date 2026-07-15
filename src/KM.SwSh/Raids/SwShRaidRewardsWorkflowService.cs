// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Raids;

public enum SwShRaidRewardWorkflowKind
{
    Drop,
    Bonus,
    All,
}

public sealed class SwShRaidRewardsWorkflowService
{
    public const string ItemIdField = "itemId";
    public const string Star1ValueField = "star1Value";
    public const string Star2ValueField = "star2Value";
    public const string Star3ValueField = "star3Value";
    public const string Star4ValueField = "star4Value";
    public const string Star5ValueField = "star5Value";
    public const int MinimumItemId = 0;
    public const int MaximumEditableItemId = ushort.MaxValue;
    public const int MinimumRewardValue = 0;
    public const int MaximumEditableDropChance = 100;
    public const int MaximumEditableBonusQuantity = 999;
    [Obsolete("Use MaximumEditableItemId. This value is an editor policy, not a reward archive storage limit.")]
    public const int MaximumItemId = MaximumEditableItemId;
    [Obsolete("Use MaximumEditableBonusQuantity. This value is an editor policy, not a reward archive storage limit.")]
    public const int MaximumRewardValue = MaximumEditableBonusQuantity;
    public const string NestDataPath = "romfs/bin/archive/field/resident/data_table.gfpak";
    public const string EnglishItemNamePath = "romfs/bin/message/English/common/itemname.dat";
    public const string EnglishSpeciesNamePath = "romfs/bin/message/English/common/monsname.dat";

    private const string MessageRootPath = "romfs/bin/message";

    private static readonly IReadOnlyList<SwShRaidRewardEditableField> EditableFields =
    [
        new SwShRaidRewardEditableField(ItemIdField, "Item", "integer", MinimumItemId, MaximumEditableItemId),
        new SwShRaidRewardEditableField(Star1ValueField, "1-star value", "integer", MinimumRewardValue, MaximumEditableBonusQuantity),
        new SwShRaidRewardEditableField(Star2ValueField, "2-star value", "integer", MinimumRewardValue, MaximumEditableBonusQuantity),
        new SwShRaidRewardEditableField(Star3ValueField, "3-star value", "integer", MinimumRewardValue, MaximumEditableBonusQuantity),
        new SwShRaidRewardEditableField(Star4ValueField, "4-star value", "integer", MinimumRewardValue, MaximumEditableBonusQuantity),
        new SwShRaidRewardEditableField(Star5ValueField, "5-star value", "integer", MinimumRewardValue, MaximumEditableBonusQuantity),
    ];

    private static readonly IReadOnlyList<RaidRewardArchiveMember> ArchiveMembers =
    [
        new RaidRewardArchiveMember("drop", "Drop", "Raid Drops", "nest_hole_drop_rewards.bin", MaximumDropValue: MaximumEditableDropChance),
        new RaidRewardArchiveMember("bonus", "Bonus", "Raid Bonus Rewards", "nest_hole_bonus_rewards.bin", MaximumDropValue: MaximumEditableBonusQuantity),
    ];

    private readonly Func<string, byte[]> readAllBytes;

    public SwShRaidRewardsWorkflowService()
        : this(File.ReadAllBytes)
    {
    }

    internal SwShRaidRewardsWorkflowService(Func<string, byte[]> readAllBytes)
    {
        this.readAllBytes = readAllBytes ?? throw new ArgumentNullException(nameof(readAllBytes));
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        return CreateSummary(project, SwShRaidRewardWorkflowKind.Drop);
    }

    public SwShWorkflowSummary CreateBonusSummary(OpenedProject project)
    {
        return CreateSummary(project, SwShRaidRewardWorkflowKind.Bonus);
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project, SwShRaidRewardWorkflowKind kind)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                kind,
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    kind,
                    DiagnosticSeverity.Error,
                    $"{GetWorkflowLabel(kind)} requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(
            kind,
            project.Health.CanOpenEditableWorkflows
                ? SwShWorkflowAvailability.Available
                : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShRaidRewardsWorkflow Load(OpenedProject project)
    {
        return Load(project, SwShRaidRewardWorkflowKind.Drop);
    }

    public SwShRaidRewardsWorkflow LoadBonus(OpenedProject project)
    {
        return Load(project, SwShRaidRewardWorkflowKind.Bonus);
    }

    internal SwShRaidRewardsWorkflow LoadAll(OpenedProject project)
    {
        return Load(project, SwShRaidRewardWorkflowKind.All);
    }

    public SwShRaidRewardsWorkflow Load(OpenedProject project, SwShRaidRewardWorkflowKind kind)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project, kind);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SwShRaidRewardTableRecord>(),
                sourceFileCount: 0,
                [],
                [],
                [],
                diagnostics,
                kind);
        }

        var dataSource = ResolveNestDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"{GetWorkflowLabel(kind)} data is not available for this project.",
                expected: NestDataPath));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShRaidRewardTableRecord>(),
                sourceFileCount: 0,
                [],
                [],
                [],
                diagnostics,
                kind);
        }

        var itemNames = LoadItemNames(project, diagnostics, kind);
        var itemCatalog = SwShItemsWorkflowService.CreateItemDisplayCatalog(project, itemNames);
        var itemDisplayNames = itemCatalog.DisplayNames;
        if (itemCatalog.ItemIds.Count == 0 && itemNames.Any(name => !string.IsNullOrWhiteSpace(name)))
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                "Item data is unavailable; reward item choices use nonblank item-name entries as a compatibility fallback and cannot be verified against item.dat.",
                file: SwShItemsWorkflowService.ItemDataPath,
                expected: "Readable Sword/Shield item.dat for verified item choices"));
        }

        try
        {
            var pack = SwShGfPackFile.Parse(readAllBytes(dataSource.AbsolutePath));
            var tables = new List<SwShRaidRewardTableRecord>();
            var provenance = CreateProvenance(dataSource.GraphEntry);
            var usageLabels = CreateRewardUsageLabels(pack, project, diagnostics, kind);

            foreach (var member in GetArchiveMembers(kind))
            {
                if (!pack.TryGetFileByName(member.FileName, out var memberData))
                {
                    diagnostics.Add(CreateDiagnostic(
                        kind,
                        DiagnosticSeverity.Warning,
                        $"{GetWorkflowLabel(kind)} source does not contain {member.Label} member '{member.FileName}'.",
                        file: dataSource.GraphEntry.RelativePath,
                        expected: member.FileName));
                    continue;
                }

                var archive = SwShNestHoleRewardArchive.Parse(memberData);
                var duplicateTableIds = DiagnoseDuplicateTableIds(
                    archive,
                    member,
                    dataSource.GraphEntry.RelativePath,
                    diagnostics,
                    kind);
                tables.AddRange(FlattenArchive(
                    archive,
                    member,
                    provenance,
                    itemDisplayNames,
                    usageLabels,
                    duplicateTableIds));
            }

            if (tables.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    kind,
                    DiagnosticSeverity.Warning,
                    $"{GetWorkflowLabel(kind)} source did not contain supported Sword/Shield reward members.",
                    file: dataSource.GraphEntry.RelativePath,
                    expected: GetExpectedArchiveMembers(kind)));
            }

            return CreateWorkflow(
                summary,
                tables,
                sourceFileCount: 1,
                itemDisplayNames,
                itemCatalog.ItemIds,
                itemNames,
                diagnostics,
                kind);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(kind)} source is not supported: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield data_table.gfpak with nest-hole reward members"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShRaidRewardTableRecord>(),
                sourceFileCount: 1,
                itemDisplayNames,
                itemCatalog.ItemIds,
                itemNames,
                diagnostics,
                kind);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(kind)} source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShRaidRewardTableRecord>(),
                sourceFileCount: 1,
                itemDisplayNames,
                itemCatalog.ItemIds,
                itemNames,
                diagnostics,
                kind);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Error,
                $"{GetWorkflowLabel(kind)} source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShRaidRewardTableRecord>(),
                sourceFileCount: 1,
                itemDisplayNames,
                itemCatalog.ItemIds,
                itemNames,
                diagnostics,
                kind);
        }
    }

    internal static bool IsEditableField(string? field)
    {
        return field
            is ItemIdField
            or Star1ValueField
            or Star2ValueField
            or Star3ValueField
            or Star4ValueField
            or Star5ValueField;
    }

    internal static WorkflowFileSource? ResolveNestDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, NestDataPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

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

    internal static bool TryParseTableId(
        string? tableId,
        out RaidRewardArchiveMember member,
        out int tableIndex,
        out ulong sourceTableId)
    {
        return TryParseTableId(
            tableId,
            out member,
            out tableIndex,
            out sourceTableId,
            out _,
            out _);
    }

    internal static bool TryParseTableId(
        string? tableId,
        out RaidRewardArchiveMember member,
        out int tableIndex,
        out ulong sourceTableId,
        out string sourceIdentity,
        out bool isLegacy)
    {
        member = ArchiveMembers[0];
        tableIndex = -1;
        sourceTableId = 0;
        sourceIdentity = string.Empty;
        isLegacy = true;

        var parts = tableId?.Split(':') ?? [];
        if (parts.Length is not 3 and not 4)
        {
            return false;
        }

        var foundMember = ArchiveMembers.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, parts[0], StringComparison.Ordinal));
        if (foundMember is null)
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out tableIndex)
            || tableIndex < 0)
        {
            return false;
        }

        if (!ulong.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out sourceTableId))
        {
            return false;
        }

        if (parts.Length == 4)
        {
            if (parts[3].Length != SHA256.HashSizeInBytes * 2 || !parts[3].All(Uri.IsHexDigit))
            {
                return false;
            }

            sourceIdentity = parts[3].ToUpperInvariant();
            isLegacy = false;
        }

        member = foundMember;
        return true;
    }

    internal static string CreateRewardRecordId(string tableId, int slot)
    {
        return $"{tableId}#{slot.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static bool TryParseRewardRecordId(string? recordId, out string tableId, out int slot)
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

    internal static string CreateTableId(
        RaidRewardArchiveMember member,
        int tableIndex,
        SwShNestHoleRewardTable table)
    {
        var sourceIdentity = CreateTableSourceIdentity(member, tableIndex, table);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{member.Key}:{tableIndex}:{table.TableId:X16}:{sourceIdentity}");
    }

    internal static string CreateTableSourceIdentity(
        RaidRewardArchiveMember member,
        int tableIndex,
        SwShNestHoleRewardTable table)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfNegative(tableIndex);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendIdentityString(hash, member.Key);
        AppendIdentityString(hash, member.FileName);
        AppendIdentityInt32(hash, tableIndex);
        AppendIdentityUInt64(hash, table.TableId);
        AppendIdentityInt32(hash, table.Rewards.Count);

        foreach (var reward in table.Rewards)
        {
            AppendIdentityUInt32(hash, reward.EntryId);
            AppendIdentityUInt32(hash, reward.ItemId);
            AppendIdentityInt32(hash, reward.Values.Count);
            foreach (var value in reward.Values)
            {
                AppendIdentityUInt32(hash, value);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static IReadOnlyList<RaidRewardArchiveMember> KnownArchiveMembers => ArchiveMembers;

    internal static IReadOnlyList<RaidRewardArchiveMember> GetArchiveMembers(SwShRaidRewardWorkflowKind kind)
    {
        return kind switch
        {
            SwShRaidRewardWorkflowKind.Drop =>
                ArchiveMembers.Where(member => string.Equals(member.Key, "drop", StringComparison.Ordinal)).ToArray(),
            SwShRaidRewardWorkflowKind.Bonus =>
                ArchiveMembers.Where(member => string.Equals(member.Key, "bonus", StringComparison.Ordinal)).ToArray(),
            _ => ArchiveMembers,
        };
    }

    private static IReadOnlySet<ulong> DiagnoseDuplicateTableIds(
        SwShNestHoleRewardArchive archive,
        RaidRewardArchiveMember member,
        string sourceFile,
        ICollection<ValidationDiagnostic> diagnostics,
        SwShRaidRewardWorkflowKind kind)
    {
        var duplicateGroups = archive.Tables
            .GroupBy(table => table.TableId)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key)
            .ToArray();

        foreach (var duplicateGroup in duplicateGroups)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"{member.Label} member contains {duplicateGroup.Count().ToString(CultureInfo.InvariantCulture)} tables with duplicate table hash 0x{duplicateGroup.Key:X16}. Den usage is ambiguous.",
                file: sourceFile,
                expected: "Unique reward table hashes within each archive member"));
        }

        return duplicateGroups.Select(group => group.Key).ToHashSet();
    }

    private static IReadOnlyList<SwShRaidRewardTableRecord> FlattenArchive(
        SwShNestHoleRewardArchive archive,
        RaidRewardArchiveMember member,
        SwShRaidRewardProvenance provenance,
        IReadOnlyList<string> itemNames,
        IReadOnlyDictionary<(string RewardKind, ulong TableId), string> usageLabels,
        IReadOnlySet<ulong> duplicateTableIds)
    {
        return archive.Tables
            .Select((table, tableIndex) => new SwShRaidRewardTableRecord(
                CreateTableId(member, tableIndex, table),
                FormatRewardTableDisplayName(
                    member,
                    tableIndex,
                    table.TableId,
                    usageLabels,
                    duplicateTableIds.Contains(table.TableId)),
                $"table_{table.TableId:X16}",
                0,
                "Sword/Shield",
                member.Key,
                member.Label,
                member.FileName,
                tableIndex,
                $"0x{table.TableId:X16}",
                table.Rewards
                    .Select((reward, rewardIndex) =>
                        ToRewardRecord(reward, rewardIndex, tableIndex, member, itemNames))
                    .ToArray(),
                provenance))
            .OrderBy(table => table.RewardKind, StringComparer.Ordinal)
            .ThenBy(table => table.TableIndex)
            .ToArray();
    }

    private static string FormatRewardTableDisplayName(
        RaidRewardArchiveMember member,
        int tableIndex,
        ulong tableId,
        IReadOnlyDictionary<(string RewardKind, ulong TableId), string> usageLabels,
        bool hasDuplicateTableId)
    {
        var baseName = $"{member.Label} {tableIndex.ToString("000", CultureInfo.InvariantCulture)}";
        if (hasDuplicateTableId)
        {
            return $"{baseName} | Usage ambiguous: duplicate table hash";
        }

        return usageLabels.TryGetValue((member.Key, tableId), out var usageLabel) && !string.IsNullOrWhiteSpace(usageLabel)
            ? $"{baseName} | {usageLabel}"
            : baseName;
    }

    private IReadOnlyDictionary<(string RewardKind, ulong TableId), string> CreateRewardUsageLabels(
        SwShGfPackFile pack,
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        SwShRaidRewardWorkflowKind kind)
    {
        if (!pack.TryGetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName, out var encounterData))
        {
            return new Dictionary<(string RewardKind, ulong TableId), string>();
        }

        try
        {
            var speciesNames = LoadSpeciesNames(project, diagnostics, kind);
            var archive = SwShEncounterNestArchive.Parse(encounterData);
            var references = new Dictionary<(string RewardKind, ulong TableId), List<RaidRewardUsageReference>>();
            var denTableIndexes = CreateEncounterDenTableIndexes(archive.Tables, diagnostics, kind);

            foreach (var (table, tableIndex) in archive.Tables.Select((table, tableIndex) => (table, tableIndex)))
            {
                var version = FormatShortGameVersion(table.GameVersion);
                var denTable = denTableIndexes[tableIndex];

                foreach (var (entry, entryIndex) in table.Entries.Select((entry, entryIndex) => (entry, entryIndex)))
                {
                    AddRewardUsageReference(references, "drop", entry.DropTableId, version, tableIndex, denTable, entryIndex, entry, speciesNames);
                    AddRewardUsageReference(references, "bonus", entry.BonusTableId, version, tableIndex, denTable, entryIndex, entry, speciesNames);
                }
            }

            return references.ToDictionary(
                pair => pair.Key,
                pair => FormatRewardUsageLabel(pair.Value),
                EqualityComparer<(string RewardKind, ulong TableId)>.Default);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Raid battle usage data could not be decoded for reward labels: {exception.Message}",
                expected: "Sword/Shield nest-hole encounter member"));
            return new Dictionary<(string RewardKind, ulong TableId), string>();
        }
    }

    private static IReadOnlyList<int?> CreateEncounterDenTableIndexes(
        IReadOnlyList<SwShEncounterNestTable> tables,
        ICollection<ValidationDiagnostic> diagnostics,
        SwShRaidRewardWorkflowKind kind)
    {
        var denTableIndexes = new int?[tables.Count];
        for (var pairStart = 0; pairStart < tables.Count; pairStart += 2)
        {
            var hasCompletePair = pairStart + 1 < tables.Count;
            var hasExpectedVersions = hasCompletePair
                && tables[pairStart].GameVersion == 1
                && tables[pairStart + 1].GameVersion == 2;
            if (hasExpectedVersions)
            {
                denTableIndexes[pairStart] = pairStart / 2;
                denTableIndexes[pairStart + 1] = pairStart / 2;
                continue;
            }

            var pairEnd = hasCompletePair
                ? $" and {pairStart + 1}"
                : string.Empty;
            var versions = hasCompletePair
                ? $"{tables[pairStart].GameVersion.ToString(CultureInfo.InvariantCulture)}, {tables[pairStart + 1].GameVersion.ToString(CultureInfo.InvariantCulture)}"
                : tables[pairStart].GameVersion.ToString(CultureInfo.InvariantCulture);
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Raid battle encounter tables {pairStart.ToString(CultureInfo.InvariantCulture)}{pairEnd} do not form an expected Sword and Shield pair. Found game version values {versions}; reward usage labels will use physical encounter table indexes.",
                file: NestDataPath,
                expected: "Adjacent encounter tables with Sword game version 1 followed by Shield game version 2"));
        }

        return denTableIndexes;
    }

    private static void AddRewardUsageReference(
        IDictionary<(string RewardKind, ulong TableId), List<RaidRewardUsageReference>> references,
        string rewardKind,
        ulong tableId,
        string version,
        int encounterTableIndex,
        int? denTable,
        int entryIndex,
        SwShEncounterNest entry,
        IReadOnlyList<string> speciesNames)
    {
        if (tableId == 0)
        {
            return;
        }

        var key = (rewardKind, tableId);
        if (!references.TryGetValue(key, out var rewardReferences))
        {
            rewardReferences = [];
            references[key] = rewardReferences;
        }

        var (minimumStar, maximumStar) = GetRaidStarRange(entry.Probabilities);
        rewardReferences.Add(new RaidRewardUsageReference(
            version,
            encounterTableIndex,
            denTable,
            entryIndex,
            minimumStar,
            maximumStar,
            FormatSpeciesName(entry, speciesNames)));
    }

    private static string FormatRewardUsageLabel(IReadOnlyList<RaidRewardUsageReference> references)
    {
        var distinctReferences = references
            .Distinct()
            .OrderBy(reference => reference.Version, StringComparer.Ordinal)
            .ThenBy(reference => reference.EncounterTableIndex)
            .ThenBy(reference => reference.EntryIndex)
            .ToArray();

        if (distinctReferences.Length <= 2)
        {
            return string.Join(
                "; ",
                distinctReferences.Select(reference =>
                    $"{reference.Version} {FormatRewardUsageLocation(reference)} Slot {reference.EntryIndex.ToString("00", CultureInfo.InvariantCulture)}, {FormatRaidStarRange(reference.MinimumStar, reference.MaximumStar)} {reference.Species}"));
        }

        var versionLabel = FormatVersionSummary(distinctReferences.Select(reference => reference.Version));
        var allReferencesUseDenPairs = distinctReferences.All(reference => reference.DenTable is not null);
        var locationCount = allReferencesUseDenPairs
            ? distinctReferences.Select(reference => reference.DenTable).Distinct().Count()
            : distinctReferences.Select(reference => reference.EncounterTableIndex).Distinct().Count();
        var locationLabel = allReferencesUseDenPairs
            ? $"{locationCount.ToString(CultureInfo.InvariantCulture)} dens"
            : $"{locationCount.ToString(CultureInfo.InvariantCulture)} encounter tables";
        var speciesCount = distinctReferences
            .Select(reference => reference.Species)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var minimumStar = distinctReferences
            .Where(reference => reference.MinimumStar > 0)
            .Select(reference => reference.MinimumStar)
            .DefaultIfEmpty(0)
            .Min();
        var maximumStar = distinctReferences
            .Where(reference => reference.MaximumStar > 0)
            .Select(reference => reference.MaximumStar)
            .DefaultIfEmpty(0)
            .Max();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{versionLabel}, {distinctReferences.Length} slots, {locationLabel}, {FormatRaidStarRange(minimumStar, maximumStar)}, {speciesCount} species");
    }

    private static string FormatRewardUsageLocation(RaidRewardUsageReference reference)
    {
        return reference.DenTable is int denTable
            ? $"Den {denTable.ToString(CultureInfo.InvariantCulture)}"
            : $"Encounter Table {reference.EncounterTableIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private static (int MinimumStar, int MaximumStar) GetRaidStarRange(IReadOnlyList<uint> probabilities)
    {
        var nonZeroStars = probabilities
            .Select((probability, index) => probability > 0 ? index + 1 : 0)
            .Where(star => star > 0)
            .ToArray();

        return nonZeroStars.Length == 0
            ? (0, 0)
            : (nonZeroStars.Min(), nonZeroStars.Max());
    }

    private static string FormatRaidStarRange(int minimumStar, int maximumStar)
    {
        if (minimumStar <= 0 || maximumStar <= 0)
        {
            return "No-Star";
        }

        return minimumStar == maximumStar
            ? $"{minimumStar.ToString(CultureInfo.InvariantCulture)}-Star"
            : $"{minimumStar.ToString(CultureInfo.InvariantCulture)}-{maximumStar.ToString(CultureInfo.InvariantCulture)}-Star";
    }

    private static string FormatVersionSummary(IEnumerable<string> versions)
    {
        var distinctVersions = versions
            .Distinct(StringComparer.Ordinal)
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToArray();

        return distinctVersions.Length == 2
            && distinctVersions.Contains("SW", StringComparer.Ordinal)
            && distinctVersions.Contains("SH", StringComparer.Ordinal)
                ? "SW/SH"
                : string.Join("/", distinctVersions);
    }

    private static string FormatShortGameVersion(int gameVersion)
    {
        return gameVersion switch
        {
            1 => "SW",
            2 => "SH",
            _ => $"V{gameVersion.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatSpeciesName(SwShEncounterNest entry, IReadOnlyList<string> speciesNames)
    {
        var speciesName = entry.Species >= 0
            && entry.Species < speciesNames.Count
            && !string.IsNullOrWhiteSpace(speciesNames[entry.Species])
                ? speciesNames[entry.Species]
                : $"Species {entry.Species.ToString(CultureInfo.InvariantCulture)}";

        return entry.Form == 0
            ? speciesName
            : $"{speciesName}-{entry.Form.ToString(CultureInfo.InvariantCulture)}";
    }

    private static SwShRaidRewardItemRecord ToRewardRecord(
        SwShNestHoleReward reward,
        int rewardIndex,
        int tableIndex,
        RaidRewardArchiveMember member,
        IReadOnlyList<string> itemNames)
    {
        if (reward.Values.Count < 5)
        {
            throw new InvalidDataException(
                $"Raid reward member '{member.FileName}' table {tableIndex} slot {rewardIndex + 1} " +
                $"contains {reward.Values.Count} star values; at least 5 are required.");
        }

        var values = reward.Values.Select(value => (long)value).ToArray();
        var firstValue = values[0];
        return new SwShRaidRewardItemRecord(
            Slot: rewardIndex + 1,
            EntryId: reward.EntryId,
            ItemId: reward.ItemId,
            ItemName: GetItemName(reward.ItemId, itemNames),
            Quantity: member.Key == "bonus" ? firstValue : 0,
            Weight: member.Key == "drop" ? firstValue : 0,
            Values: values);
    }

    private static string GetItemName(uint itemId, IReadOnlyList<string> itemNames)
    {
        if (itemId == 0)
        {
            return "None";
        }

        return itemId < (uint)itemNames.Count && !string.IsNullOrWhiteSpace(itemNames[(int)itemId])
            ? itemNames[(int)itemId]
            : string.Create(CultureInfo.InvariantCulture, $"Item {itemId}");
    }

    private string[] LoadItemNames(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        SwShRaidRewardWorkflowKind kind)
    {
        var itemNamesSource = ResolveItemNamesSource(project, diagnostics, kind);
        if (itemNamesSource is null)
        {
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(readAllBytes(itemNamesSource.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Item name table could not be decoded: {exception.Message}",
                file: itemNamesSource.GraphEntry.RelativePath,
                expected: "Sword/Shield itemname.dat"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Item name table could not be read: {exception.Message}",
                file: itemNamesSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield itemname.dat"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Item name table could not be read: {exception.Message}",
                file: itemNamesSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield itemname.dat"));
            return [];
        }
    }

    private string[] LoadSpeciesNames(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        SwShRaidRewardWorkflowKind kind)
    {
        var speciesNamesSource = ResolveSpeciesNamesSource(project);
        if (speciesNamesSource is null)
        {
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(readAllBytes(speciesNamesSource.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Species name table could not be decoded for reward labels: {exception.Message}",
                file: speciesNamesSource.GraphEntry.RelativePath,
                expected: "Sword/Shield monsname.dat"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Species name table could not be read for reward labels: {exception.Message}",
                file: speciesNamesSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield monsname.dat"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                $"Species name table could not be read for reward labels: {exception.Message}",
                file: speciesNamesSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield monsname.dat"));
            return [];
        }
    }

    private static WorkflowFileSource? ResolveItemNamesSource(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        SwShRaidRewardWorkflowKind kind)
    {
        var fallback = ResolveItemNamesSource(project);

        if (fallback is null)
        {
            diagnostics.Add(CreateDiagnostic(
                kind,
                DiagnosticSeverity.Warning,
                "Item names are not available; item IDs will be shown as fallback names.",
                expected: "romfs/bin/message/{language}/common/itemname.dat"));
            return null;
        }

        return fallback;
    }

    internal static WorkflowFileSource? ResolveItemNamesSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveCommonTextSource(project, "itemname.dat");
    }

    internal static IReadOnlyList<WorkflowFileSource> ResolveItemDisplaySourcesForValidation(
        OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var itemNamesSource = ResolveItemNamesSource(project);
        var itemDataSource = ResolveWorkflowFile(project, SwShItemsWorkflowService.ItemDataPath);
        var moveNamesSource = itemDataSource is null
            ? null
            : ResolveCommonTextSource(project, "wazaname.dat");

        return new[] { itemNamesSource, itemDataSource, moveNamesSource }
            .Where(source => source is not null)
            .Select(source => source!)
            .DistinctBy(source => source.GraphEntry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorkflowFileSource? ResolveSpeciesNamesSource(OpenedProject project)
    {
        return ResolveCommonTextSource(project, "monsname.dat");
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
                entry.RelativePath.StartsWith(MessageRootPath + "/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith($"/common/{fileName}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ResolveWorkflowFile(project, entry.RelativePath))
            .FirstOrDefault(source => source is not null);
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

    private static SwShRaidRewardsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShRaidRewardTableRecord> tables,
        int sourceFileCount,
        IReadOnlyList<string> itemDisplayNames,
        IReadOnlyList<int> itemRecordIds,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        SwShRaidRewardWorkflowKind kind)
    {
        return new SwShRaidRewardsWorkflow(
            summary,
            tables,
            CreateEditableFields(itemDisplayNames, itemRecordIds, itemNames, kind),
            new SwShRaidRewardsWorkflowStats(
                tables.Count,
                tables.Sum(table => table.Rewards.Count),
                sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<SwShRaidRewardEditableField> CreateEditableFields(
        IReadOnlyList<string> itemDisplayNames,
        IReadOnlyList<int> itemRecordIds,
        IReadOnlyList<string> itemNames,
        SwShRaidRewardWorkflowKind kind)
    {
        var hasLoadedItemNames = itemNames.Count > 0;
        IEnumerable<int> candidateItemIds = itemRecordIds.Count > 0
            ? itemRecordIds
            : Enumerable.Range(0, Math.Min(itemNames.Count, MaximumEditableItemId + 1));
        var itemOptions = candidateItemIds
            .Where(itemId => itemId >= MinimumItemId && itemId <= MaximumEditableItemId)
            .Where(itemId => itemId == 0
                || (hasLoadedItemNames
                    && itemId < itemNames.Count
                    && !string.IsNullOrWhiteSpace(itemNames[itemId])))
            .Append(0)
            .Distinct()
            .OrderBy(itemId => itemId)
            .Select(itemId =>
            {
                var name = itemId < itemDisplayNames.Count ? itemDisplayNames[itemId] : string.Empty;
                var label = itemId switch
                {
                    0 => "000 None",
                    _ when string.IsNullOrWhiteSpace(name) =>
                        $"{itemId.ToString("000", CultureInfo.InvariantCulture)} Item {itemId}",
                    _ => $"{itemId.ToString("000", CultureInfo.InvariantCulture)} {name}",
                };
                return new SwShRaidRewardEditableFieldOption(itemId, label);
            })
            .ToArray();
        var maximumStarValue = kind == SwShRaidRewardWorkflowKind.Drop
            ? MaximumEditableDropChance
            : MaximumEditableBonusQuantity;

        return EditableFields
            .Select(field => field.Field switch
            {
                ItemIdField => field with { Options = itemOptions },
                Star1ValueField or Star2ValueField or Star3ValueField or Star4ValueField or Star5ValueField =>
                    field with { MaximumValue = maximumStarValue },
                _ => field,
            })
            .ToArray();
    }

    private static SwShRaidRewardProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShRaidRewardProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShRaidRewardWorkflowKind kind,
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            GetWorkflowId(kind),
            GetWorkflowLabel(kind),
            kind == SwShRaidRewardWorkflowKind.Bonus
                ? "Raid bonus reward tables, items, per-star quantities, and provenance."
                : "Raid drop reward tables, items, per-star drop chances, and provenance.",
            availability,
            diagnostics);
    }

    private static string GetWorkflowId(SwShRaidRewardWorkflowKind kind)
    {
        return kind == SwShRaidRewardWorkflowKind.Bonus
            ? SwShWorkflowIds.RaidBonusRewards
            : SwShWorkflowIds.RaidRewards;
    }

    private static string GetWorkflowLabel(SwShRaidRewardWorkflowKind kind)
    {
        return kind == SwShRaidRewardWorkflowKind.Bonus
            ? "Raid Bonus Rewards"
            : "Raid Rewards";
    }

    private static string GetExpectedArchiveMembers(SwShRaidRewardWorkflowKind kind)
    {
        return string.Join(
            " or ",
            GetArchiveMembers(kind).Select(member => $"{member.FileName} inside data_table.gfpak"));
    }

    private static void AppendIdentityString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendIdentityInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendIdentityInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendIdentityUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendIdentityUInt64(IncrementalHash hash, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        SwShRaidRewardWorkflowKind kind,
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
            Domain: $"workflow.{GetWorkflowId(kind)}",
            Expected: expected);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return CreateDiagnostic(SwShRaidRewardWorkflowKind.Drop, severity, message, file, field, expected);
    }
}

public sealed record RaidRewardArchiveMember(
    string Key,
    string Label,
    string TableLabel,
    string FileName,
    [property: Obsolete("Use MaximumEditableValue. This value is an editor policy, not a drop-only or storage limit.")]
    int MaximumDropValue)
{
    public int MaximumEditableValue => MaximumDropValue;
}

internal sealed record WorkflowFileSource(
    ProjectFileGraphEntry GraphEntry,
    string AbsolutePath);

internal sealed record RaidRewardUsageReference(
    string Version,
    int EncounterTableIndex,
    int? DenTable,
    int EntryIndex,
    int MinimumStar,
    int MaximumStar,
    string Species);
