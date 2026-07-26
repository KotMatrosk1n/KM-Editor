// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Workflows;

namespace KM.ZA.ExeFs;

internal sealed record ZaExeFsMainFile(
    string AbsolutePath,
    ProjectFileReference Reference,
    ProjectFileGraphEntryState FileState);

internal static class ZaExeFsMainFileResolver
{
    public static ZaExeFsMainFile? ResolveEffective(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var entry = FindEntry(project);
        var outputPath = ResolveOutputPath(project.Paths);
        if (outputPath is not null && File.Exists(outputPath))
        {
            return new ZaExeFsMainFile(
                outputPath,
                new ProjectFileReference(ProjectFileLayer.Layered, ZaExeFsReservedRegionLedger.ExeFsMainPath),
                entry?.BaseFile is null
                    ? ProjectFileGraphEntryState.LayeredOnly
                    : ProjectFileGraphEntryState.LayeredOverride);
        }

        var basePath = ResolveBasePath(project.Paths);
        return basePath is not null && File.Exists(basePath)
            ? new ZaExeFsMainFile(
                basePath,
                new ProjectFileReference(ProjectFileLayer.Base, ZaExeFsReservedRegionLedger.ExeFsMainPath),
                entry?.State ?? ProjectFileGraphEntryState.BaseOnly)
            : null;
    }

    public static ZaExeFsMainFile? ResolveBase(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var basePath = ResolveBasePath(project.Paths);
        if (basePath is null || !File.Exists(basePath))
        {
            return null;
        }

        return new ZaExeFsMainFile(
            basePath,
            new ProjectFileReference(ProjectFileLayer.Base, ZaExeFsReservedRegionLedger.ExeFsMainPath),
            FindEntry(project)?.State ?? ProjectFileGraphEntryState.BaseOnly);
    }

    public static string? ResolveBasePath(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            return null;
        }

        var baseRoot = Path.GetFullPath(paths.BaseExeFsPath);
        var basePath = Path.GetFullPath(Path.Combine(baseRoot, "main"));
        return PathContainment.IsOutsideRoot(Path.GetRelativePath(baseRoot, basePath))
            ? null
            : basePath;
    }

    public static string? ResolveOutputPath(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : ZaWorkflowFileSource.ResolveStandaloneOutputPath(
                paths,
                ZaExeFsReservedRegionLedger.ExeFsMainPath);
    }

    private static ProjectFileGraphEntry? FindEntry(OpenedProject project)
    {
        return project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(
                entry.RelativePath,
                ZaExeFsReservedRegionLedger.ExeFsMainPath,
                StringComparison.OrdinalIgnoreCase));
    }
}
