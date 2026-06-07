// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.SwSh.Raids;

public sealed class SwShRaidRewardsWorkflowService
{
    public const string RaidRewardsReadModelPath = "romfs/kmeditor/raid.rewards.readmodel.json";

    private static readonly JsonSerializerOptions ReadModelJsonOptions = new(JsonSerializerDefaults.Web);

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
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, RaidRewardsReadModelPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Raid Rewards data is not available for this project yet.",
                expected: RaidRewardsReadModelPath));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Raid Rewards data source could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Raid Rewards read model"));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var readModel = JsonSerializer.Deserialize<RaidRewardsReadModel>(stream, ReadModelJsonOptions);
            var provenance = CreateProvenance(graphEntry);
            var tables = readModel?.Tables is null
                ? Array.Empty<SwShRaidRewardTableRecord>()
                : readModel.Tables
                    .OrderBy(table => table.TableId, StringComparer.Ordinal)
                    .Select(table => ToRaidRewardTableRecord(table, provenance))
                    .ToArray();

            foreach (var duplicateGroup in tables.GroupBy(table => table.TableId).Where(group => group.Count() > 1))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Raid reward table id '{duplicateGroup.Key}' appears more than once in the Raid Rewards read model.",
                    file: graphEntry.RelativePath,
                    expected: "Unique raid reward table ids"));
            }

            return CreateWorkflow(summary, tables, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards data source is not valid JSON: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Sanitized Raid Rewards read model JSON"));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Raid Rewards data source could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Raid Rewards read model"));
            return CreateWorkflow(summary, Array.Empty<SwShRaidRewardTableRecord>(), diagnostics);
        }
    }

    private static SwShRaidRewardsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShRaidRewardTableRecord> tables,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShRaidRewardsWorkflow(
            summary,
            tables,
            new SwShRaidRewardsWorkflowStats(
                tables.Count,
                tables.Sum(table => table.Rewards.Count),
                tables.Count > 0 ? 1 : 0),
            diagnostics);
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

    private static SwShRaidRewardProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShRaidRewardProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShRaidRewardTableRecord ToRaidRewardTableRecord(
        RaidRewardTableReadModelRecord table,
        SwShRaidRewardProvenance provenance)
    {
        return new SwShRaidRewardTableRecord(
            table.TableId,
            table.DenId,
            table.Rank,
            table.GameVersion,
            (table.Rewards ?? Array.Empty<RaidRewardItemReadModelRecord>())
                .OrderBy(reward => reward.Slot)
                .Select(ToRaidRewardItemRecord)
                .ToArray(),
            provenance);
    }

    private static SwShRaidRewardItemRecord ToRaidRewardItemRecord(RaidRewardItemReadModelRecord reward)
    {
        return new SwShRaidRewardItemRecord(
            reward.Slot,
            reward.ItemId,
            reward.ItemName,
            reward.Quantity,
            reward.Weight);
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
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.raidRewards",
            Expected: expected);
    }

    private sealed record RaidRewardsReadModel(
        int SchemaVersion,
        IReadOnlyList<RaidRewardTableReadModelRecord>? Tables);

    private sealed record RaidRewardTableReadModelRecord(
        string TableId,
        string DenId,
        int Rank,
        string GameVersion,
        IReadOnlyList<RaidRewardItemReadModelRecord>? Rewards);

    private sealed record RaidRewardItemReadModelRecord(
        int Slot,
        int ItemId,
        string ItemName,
        int Quantity,
        int Weight);
}
