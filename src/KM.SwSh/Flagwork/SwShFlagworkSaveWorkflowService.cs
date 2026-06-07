// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Flagwork;

public sealed class SwShFlagworkSaveWorkflowService
{
    public const string FlagworkRootPath = "romfs/bin/flagwork";

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Flagwork and Save Inspectors requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShFlagworkSaveWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShFlagRecord>(), Array.Empty<SwShSaveBlockRecord>(), sourceFileCount: 0, diagnostics);
        }

        var sources = ResolveFlagworkSources(project).ToArray();
        if (sources.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Flagwork tables are not available for this project.",
                expected: $"{FlagworkRootPath}/*.tbl"));
            return CreateWorkflow(summary, Array.Empty<SwShFlagRecord>(), Array.Empty<SwShSaveBlockRecord>(), sourceFileCount: 0, diagnostics);
        }

        var flags = new List<SwShFlagRecord>();
        var saveBlocks = new List<SwShSaveBlockRecord>();

        foreach (var source in sources)
        {
            try
            {
                var table = SwShAhtbFile.Parse(File.ReadAllBytes(source.AbsolutePath));
                var tableName = Path.GetFileNameWithoutExtension(source.GraphEntry.RelativePath);
                var provenance = CreateProvenance(source.GraphEntry);

                for (var index = 0; index < table.Entries.Count; index++)
                {
                    var entry = table.Entries[index];
                    var kind = InferKind(tableName, entry.Name);
                    var valueKind = kind == "Work" ? "integer" : "boolean";
                    var fullHash = FormatHash(entry.Hash);
                    var low32 = FormatLow32(entry.Hash);
                    var flag = new SwShFlagRecord(
                        string.Create(CultureInfo.InvariantCulture, $"{tableName}:{index:D4}"),
                        entry.Name,
                        tableName,
                        kind,
                        valueKind,
                        kind == "Work" ? "0" : "false",
                        string.Create(CultureInfo.InvariantCulture, $"{kind} hash {fullHash} uses save key {low32}."),
                        tableName,
                        index,
                        fullHash,
                        low32,
                        provenance);

                    flags.Add(flag);
                    saveBlocks.Add(new SwShSaveBlockRecord(
                        string.Create(CultureInfo.InvariantCulture, $"{tableName}:{index:D4}:{low32}"),
                        entry.Name,
                        low32,
                        fullHash,
                        kind,
                        valueKind,
                        kind == "Work"
                            ? string.Create(CultureInfo.InvariantCulture, $"Save work key {low32} is derived from {entry.Name}.")
                            : string.Create(CultureInfo.InvariantCulture, $"Save flag key {low32} is derived from {entry.Name}."),
                        provenance));
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Flagwork table '{source.GraphEntry.RelativePath}' could not be decoded: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Sword/Shield AHTB flagwork table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Flagwork table '{source.GraphEntry.RelativePath}' could not be read: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Readable Sword/Shield flagwork table"));
            }
        }

        foreach (var duplicateGroup in flags.GroupBy(flag => flag.Hash).Where(group => group.Count() > 1))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Flagwork hash '{duplicateGroup.Key}' appears more than once.",
                expected: "Unique flagwork hashes when possible"));
        }

        foreach (var duplicateGroup in saveBlocks.GroupBy(saveBlock => saveBlock.Key).Where(group => group.Count() > 1))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Save key '{duplicateGroup.Key}' appears more than once across flagwork tables.",
                expected: "Review possible low32 save-key collision"));
        }

        return CreateWorkflow(
            summary,
            flags
                .OrderBy(flag => flag.Table, StringComparer.OrdinalIgnoreCase)
                .ThenBy(flag => flag.Index)
                .ToArray(),
            saveBlocks
                .OrderBy(saveBlock => saveBlock.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(saveBlock => saveBlock.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(saveBlock => saveBlock.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            sources.Length,
            diagnostics);
    }

    private static IEnumerable<WorkflowFileSource> ResolveFlagworkSources(OpenedProject project)
    {
        foreach (var entry in project.FileGraph.Entries
            .Where(entry =>
                entry.RelativePath.StartsWith(FlagworkRootPath + "/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var sourcePath = ResolveSourcePath(project.Paths, entry);
            if (sourcePath is not null && File.Exists(sourcePath))
            {
                yield return new WorkflowFileSource(entry, sourcePath);
            }
        }
    }

    private static SwShFlagworkSaveWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShFlagRecord> flags,
        IReadOnlyList<SwShSaveBlockRecord> saveBlocks,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShFlagworkSaveWorkflow(
            summary,
            flags,
            saveBlocks,
            new SwShFlagworkSaveWorkflowStats(
                flags.Count,
                saveBlocks.Count,
                sourceFileCount),
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

    private static SwShFlagworkSaveProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShFlagworkSaveProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static string InferKind(string table, string name)
    {
        return name.StartsWith("WK_", StringComparison.OrdinalIgnoreCase)
            || table.Contains("work", StringComparison.OrdinalIgnoreCase)
            ? "Work"
            : "Flag";
    }

    private static string FormatHash(ulong hash)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{hash:X16}");
    }

    private static string FormatLow32(ulong hash)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{(uint)hash:X8}");
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.FlagworkSave,
            "Flagwork and Save Inspectors",
            "Flagwork hash tables, save keys, and source provenance.",
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
            Domain: "workflow.flagworkSave",
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
