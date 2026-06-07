// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Raids;

public sealed class SwShRaidRewardsWorkflowService
{
    public const string ItemIdField = "itemId";
    public const string Star1ValueField = "star1Value";
    public const string Star2ValueField = "star2Value";
    public const string Star3ValueField = "star3Value";
    public const string Star4ValueField = "star4Value";
    public const string Star5ValueField = "star5Value";
    public const int MinimumItemId = 0;
    public const int MaximumItemId = (int)SwShNestHoleRewardArchive.MaximumItemId;
    public const int MinimumRewardValue = 0;
    public const int MaximumRewardValue = (int)SwShNestHoleRewardArchive.MaximumBonusQuantity;
    public const string NestDataPath = "romfs/bin/archive/field/resident/data_table.gfpak";
    public const string EnglishItemNamePath = "romfs/bin/message/English/common/itemname.dat";

    private const string MessageRootPath = "romfs/bin/message";

    private static readonly IReadOnlyList<SwShRaidRewardEditableField> EditableFields =
    [
        new SwShRaidRewardEditableField(ItemIdField, "Item ID", "integer", MinimumItemId, MaximumItemId),
        new SwShRaidRewardEditableField(Star1ValueField, "1-star value", "integer", MinimumRewardValue, MaximumRewardValue),
        new SwShRaidRewardEditableField(Star2ValueField, "2-star value", "integer", MinimumRewardValue, MaximumRewardValue),
        new SwShRaidRewardEditableField(Star3ValueField, "3-star value", "integer", MinimumRewardValue, MaximumRewardValue),
        new SwShRaidRewardEditableField(Star4ValueField, "4-star value", "integer", MinimumRewardValue, MaximumRewardValue),
        new SwShRaidRewardEditableField(Star5ValueField, "5-star value", "integer", MinimumRewardValue, MaximumRewardValue),
    ];

    private static readonly IReadOnlyList<RaidRewardArchiveMember> ArchiveMembers =
    [
        new RaidRewardArchiveMember("drop", "Drop", "Raid Drops", "nest_hole_drop_rewards.bin", MaximumDropValue: 100),
        new RaidRewardArchiveMember("bonus", "Bonus", "Raid Bonus Rewards", "nest_hole_bonus_rewards.bin", MaximumDropValue: MaximumRewardValue),
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
                    "Raid Rewards requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShRaidRewardsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), sourceFileCount: 0, diagnostics);
        }

        var dataSource = ResolveNestDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Raid Rewards data is not available for this project.",
                expected: NestDataPath));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), sourceFileCount: 0, diagnostics);
        }

        var itemNames = LoadItemNames(project, diagnostics);

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
            var tables = new List<SwShRaidRewardTableRecord>();
            var provenance = CreateProvenance(dataSource.GraphEntry);

            foreach (var member in ArchiveMembers)
            {
                if (!pack.TryGetFileByName(member.FileName, out var memberData))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Raid Rewards source does not contain {member.Label} member '{member.FileName}'.",
                        file: dataSource.GraphEntry.RelativePath,
                        expected: member.FileName));
                    continue;
                }

                var archive = SwShNestHoleRewardArchive.Parse(memberData);
                tables.AddRange(FlattenArchive(archive, member, provenance, itemNames));
            }

            if (tables.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Raid Rewards source did not contain supported Sword/Shield reward members.",
                    file: dataSource.GraphEntry.RelativePath,
                    expected: "nest_hole_drop_rewards.bin or nest_hole_bonus_rewards.bin inside data_table.gfpak"));
            }

            return CreateWorkflow(summary, tables, sourceFileCount: 1, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards source is not supported: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield data_table.gfpak with nest-hole reward members"));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), sourceFileCount: 1, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), sourceFileCount: 1, diagnostics);
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards source could not be read: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield data_table.gfpak"));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), sourceFileCount: 1, diagnostics);
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
        member = ArchiveMembers[0];
        tableIndex = -1;
        sourceTableId = 0;

        var parts = tableId?.Split(':') ?? [];
        if (parts.Length != 3)
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

    internal static string CreateTableId(RaidRewardArchiveMember member, int tableIndex, ulong sourceTableId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{member.Key}:{tableIndex}:{sourceTableId:X16}");
    }

    internal static IReadOnlyList<RaidRewardArchiveMember> KnownArchiveMembers => ArchiveMembers;

    private static IReadOnlyList<SwShRaidRewardTableRecord> FlattenArchive(
        SwShNestHoleRewardArchive archive,
        RaidRewardArchiveMember member,
        SwShRaidRewardProvenance provenance,
        IReadOnlyList<string> itemNames)
    {
        return archive.Tables
            .Select((table, tableIndex) => new SwShRaidRewardTableRecord(
                CreateTableId(member, tableIndex, table.TableId),
                $"table_{table.TableId:X16}",
                Rank: 0,
                GameVersion: "Sword/Shield",
                member.Key,
                member.Label,
                member.FileName,
                tableIndex,
                $"0x{table.TableId:X16}",
                table.Rewards
                    .Select((reward, rewardIndex) => ToRewardRecord(reward, rewardIndex, member, itemNames))
                    .ToArray(),
                provenance))
            .OrderBy(table => table.RewardKind, StringComparer.Ordinal)
            .ThenBy(table => table.TableIndex)
            .ToArray();
    }

    private static SwShRaidRewardItemRecord ToRewardRecord(
        SwShNestHoleReward reward,
        int rewardIndex,
        RaidRewardArchiveMember member,
        IReadOnlyList<string> itemNames)
    {
        var values = PadValues(reward.Values, length: 5);
        var firstValue = values[0];
        return new SwShRaidRewardItemRecord(
            Slot: rewardIndex + 1,
            EntryId: checked((int)reward.EntryId),
            ItemId: checked((int)reward.ItemId),
            ItemName: GetItemName(reward.ItemId, itemNames),
            Quantity: member.Key == "bonus" ? firstValue : 0,
            Weight: member.Key == "drop" ? firstValue : 0,
            Values: values);
    }

    private static int[] PadValues(IReadOnlyList<uint> values, int length)
    {
        var padded = new int[length];
        for (var index = 0; index < padded.Length && index < values.Count; index++)
        {
            padded[index] = checked((int)values[index]);
        }

        return padded;
    }

    private static string GetItemName(uint itemId, IReadOnlyList<string> itemNames)
    {
        return itemId < (uint)itemNames.Count && !string.IsNullOrWhiteSpace(itemNames[(int)itemId])
            ? itemNames[(int)itemId]
            : string.Create(CultureInfo.InvariantCulture, $"Item {itemId}");
    }

    private static string[] LoadItemNames(OpenedProject project, ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemNamesSource = ResolveItemNamesSource(project, diagnostics);
        if (itemNamesSource is null)
        {
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(itemNamesSource.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item name table could not be decoded: {exception.Message}",
                file: itemNamesSource.GraphEntry.RelativePath,
                expected: "Sword/Shield itemname.dat"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item name table could not be read: {exception.Message}",
                file: itemNamesSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield itemname.dat"));
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
                entry.RelativePath.StartsWith(MessageRootPath + "/", StringComparison.OrdinalIgnoreCase)
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
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShRaidRewardsWorkflow(
            summary,
            tables,
            EditableFields,
            new SwShRaidRewardsWorkflowStats(
                tables.Count,
                tables.Sum(table => table.Rewards.Count),
                sourceFileCount),
            diagnostics);
    }

    private static SwShRaidRewardProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShRaidRewardProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.RaidRewards,
            "Raid Rewards",
            "Raid reward tables, den ranks, item quantities, and source provenance.",
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
            Domain: "workflow.raidRewards",
            Expected: expected);
    }
}

public sealed record RaidRewardArchiveMember(
    string Key,
    string Label,
    string TableLabel,
    string FileName,
    int MaximumDropValue);

internal sealed record WorkflowFileSource(
    ProjectFileGraphEntry GraphEntry,
    string AbsolutePath);
