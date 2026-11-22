// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.ZA.Workflows;

namespace KM.ZA.ExeFs;

internal sealed record ZaExeFsMainFile(
    string AbsolutePath,
    ProjectFileReference Reference,
    ProjectFileGraphEntryState FileState,
    string? PatchBasePath = null)
{
    public byte[] ReadBytes(Func<string, byte[]>? read = null)
    {
        read ??= File.ReadAllBytes;
        var bytes = read(AbsolutePath);
        if (!ZaExeFsMainFileResolver.IsPatchPath(Reference.RelativePath))
            return bytes;

        return ZaRecompExecutablePatch.Apply(
            read(PatchBasePath ?? throw new InvalidDataException(
                "A ZA Recomp executable patch requires the matching base exefs/main.")),
            bytes);
    }
}

internal static class ZaExeFsMainFileResolver
{
    public static ZaExeFsMainFile? ResolveEffective(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var entry = FindEntry(project);
        var outputPath = ResolveOutputPath(project.Paths);
        var basePath = ResolveBasePath(project.Paths);
        var hasBase = File.Exists(basePath);
        var importedPatchPath = ResolveImportedPatchPath(project.Paths);
        var directPatchPath = ResolveOutputPath(project.Paths, ZaOutputMode.ZaRecomp);
        var newest = new[]
        {
            (Path: directPatchPath, RelativePath: OutputRelativePath(ZaOutputMode.ZaRecomp)),
            (Path: importedPatchPath, RelativePath: ZaRecompExecutablePatch.RelativePath),
            (Path: outputPath, RelativePath: ZaExeFsReservedRegionLedger.ExeFsMainPath),
        }.Where(candidate => File.Exists(candidate.Path))
            .OrderByDescending(candidate => File.GetLastWriteTimeUtc(candidate.Path!))
            .FirstOrDefault();

        if (newest.Path is not null)
        {
            return new ZaExeFsMainFile(
                newest.Path,
                new ProjectFileReference(ProjectFileLayer.Layered, newest.RelativePath),
                hasBase
                    ? ProjectFileGraphEntryState.LayeredOverride
                    : ProjectFileGraphEntryState.LayeredOnly,
                hasBase ? basePath : null);
        }

        return basePath is not null && hasBase
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

    public static string OutputRelativePath(ZaOutputMode mode) =>
        mode == ZaOutputMode.ZaRecomp
            ? $"{ZaWorkflowFileSource.ZaRecompDirectory}/{ZaRecompExecutablePatch.RelativePath}"
            : ZaExeFsReservedRegionLedger.ExeFsMainPath;

    public static bool IsPatchPath(string relativePath) =>
        string.Equals(relativePath, ZaRecompExecutablePatch.RelativePath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(relativePath, OutputRelativePath(ZaOutputMode.ZaRecomp), StringComparison.OrdinalIgnoreCase);

    public static string? ResolveImportedPatchPath(ProjectPaths paths) =>
        string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : ZaWorkflowFileSource.ResolveStandaloneOutputPath(paths, ZaRecompExecutablePatch.RelativePath);

    public static string? ResolveOutputPath(ProjectPaths paths, ZaOutputMode mode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : ZaWorkflowFileSource.ResolveStandaloneOutputPath(
                paths,
                OutputRelativePath(mode));
    }

    public static ZaStandaloneOutputMutation CreateMutation(
        ProjectPaths paths, ZaOutputMode mode, byte[]? edited, byte[]? original, ZaOutputApplyContext context)
    {
        if (mode != ZaOutputMode.ZaRecomp)
            return new(OutputRelativePath(mode), edited, edited is null ? original : null, context);

        if (original is null)
            throw new InvalidDataException("A ZA Recomp executable patch requires the matching base exefs/main.");

        var patch = edited is null ? null : ZaRecompExecutablePatch.Create(original, edited);
        // Keep the restored state visible if the project also has an older executable override.
        if (patch is null && (File.Exists(ResolveOutputPath(paths)) || File.Exists(ResolveImportedPatchPath(paths))))
            patch = ZaRecompExecutablePatch.CreateEmpty(original);

        return new(OutputRelativePath(mode), patch, ApplyContext: context with { OwnerId = new OwnershipOwnerId("workflow.za.output") });
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
