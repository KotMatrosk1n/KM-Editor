// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV;
using System.Security;

namespace KM.SV.Workflows;

internal sealed class SvWorkflowFileSource
{
    public const string DescriptorVirtualPath = SvTrinityDescriptorPatcher.DescriptorVirtualPath;

    internal static object OutputWriteSyncRoot { get; } = new();

    private readonly SvCacheManager cacheManager;

    public SvWorkflowFileSource(SvCacheManager? cacheManager = null)
    {
        this.cacheManager = cacheManager ?? new SvCacheManager();
    }

    public SvWorkflowFile Read(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            var trinityModManagerPath = CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath);
            var standalonePath = CombineGraphPath(project.Paths.OutputRootPath, relativePath);
            (string Path, bool IsStandalone)? looseOutput;
            try
            {
                looseOutput = SelectLatestLooseOutput(trinityModManagerPath, standalonePath);
            }
            catch (Exception exception) when (IsContextualFileFailure(exception))
            {
                throw CreateReadFailure(
                    relativePath,
                    ProjectFileLayer.Layered,
                    entry?.State,
                    exception,
                    ProjectFileOperation.Inspect);
            }

            if (looseOutput is not null)
            {
                var state = looseOutput.Value.IsStandalone
                    ? entry?.State ?? ProjectFileGraphEntryState.LayeredOverride
                    : ProjectFileGraphEntryState.LayeredOverride;
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytes(
                        looseOutput.Value.Path,
                        relativePath,
                        ProjectFileLayer.Layered,
                        state),
                    ProjectFileLayer.Layered,
                    state);
            }

            if (TryReadOutputArchive(
                project.Paths,
                normalizedVirtualPath,
                relativePath,
                out var layeredArchiveBytes))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    layeredArchiveBytes,
                    ProjectFileLayer.Layered,
                    ProjectFileGraphEntryState.LayeredOverride);
            }
        }

        if (!string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            var looseBasePath = CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath);
            if (FileExistsWithContext(
                looseBasePath,
                relativePath,
                ProjectFileLayer.Base,
                entry?.State))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytes(
                        looseBasePath,
                        relativePath,
                        ProjectFileLayer.Base,
                        entry?.State),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            try
            {
                var archiveBytes = cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (IsContextualFileFailure(exception))
            {
                throw CreateReadFailure(
                    relativePath,
                    ProjectFileLayer.Base,
                    entry?.State,
                    exception,
                    ProjectFileOperation.Inspect);
            }
        }

        throw CreateReadFailure(
            relativePath,
            layer: null,
            state: entry?.State,
            exception: new FileNotFoundException());
    }

    public SvWorkflowFile ReadBase(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);

        if (!string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            var looseBasePath = CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath);
            if (FileExistsWithContext(
                looseBasePath,
                relativePath,
                ProjectFileLayer.Base,
                entry?.State))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytes(
                        looseBasePath,
                        relativePath,
                        ProjectFileLayer.Base,
                        entry?.State),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            try
            {
                var archiveBytes = cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (IsContextualFileFailure(exception))
            {
                throw CreateReadFailure(
                    relativePath,
                    ProjectFileLayer.Base,
                    entry?.State,
                    exception,
                    ProjectFileOperation.Inspect);
            }
        }

        throw CreateReadFailure(
            relativePath,
            layer: null,
            state: entry?.State,
            exception: new FileNotFoundException());
    }

    public bool Exists(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            if (FileExistsWithContext(
                    CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath),
                    relativePath,
                    ProjectFileLayer.Layered,
                    state: null)
                || FileExistsWithContext(
                    CombineGraphPath(project.Paths.OutputRootPath, relativePath),
                    relativePath,
                    ProjectFileLayer.Layered,
                    state: null)
                || TryOutputArchiveContains(project.Paths, normalizedVirtualPath, relativePath))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            return false;
        }

        if (FileExistsWithContext(
            CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath),
            relativePath,
            ProjectFileLayer.Base,
            state: null))
        {
            return true;
        }

        try
        {
            return cacheManager.ContainsBaseTrinityFile(project.Paths, normalizedVirtualPath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(
                relativePath,
                ProjectFileLayer.Base,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
        }
    }

    public IReadOnlyList<string> ListBasePackNames(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        try
        {
            return cacheManager.ListBaseTrinityPackNames(project.Paths);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public SvWorkflowArchiveInventory? GetOutputArchiveInventory(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var outputRootPath = project.Paths.OutputRootPath;
        if (string.IsNullOrWhiteSpace(outputRootPath) || !HasTrinityArchive(outputRootPath))
        {
            return null;
        }

        try
        {
            var index = SvTrinityArchive.BuildIndex(outputRootPath);
            return new SvWorkflowArchiveInventory(
                index.Files
                    .Select(file => file.PackName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                index.Files.Select(file => file.FileHash).ToHashSet());
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(
                "romfs/arc/data.trpfd",
                ProjectFileLayer.Layered,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
        }
    }

    public static ProjectFileReference CreateReference(SvWorkflowFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ProjectFileReference(file.SourceLayer, file.RelativePath);
    }

    public static string ResolveOutputPath(
        ProjectPaths paths,
        string virtualRomFsPath,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Set an output root before applying Scarlet/Violet edits.");
        }

        var targetRelativePath = ToOutputRelativePath(NormalizeVirtualPath(virtualRomFsPath), outputMode);
        if (Path.IsPathRooted(targetRelativePath))
        {
            throw new InvalidOperationException($"Scarlet/Violet target path '{targetRelativePath}' must be relative.");
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, targetRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);
        if (PathContainment.IsOutsideRoot(pathFromOutputRoot))
        {
            throw new InvalidOperationException($"Scarlet/Violet target path '{targetRelativePath}' escapes the output root.");
        }

        return targetPath;
    }

    public static PlannedWriteInfo CreatePlannedWrite(
        ProjectPaths paths,
        string virtualRomFsPath,
        IReadOnlyList<ProjectFileReference> sources,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        var targetRelativePath = ToOutputRelativePath(NormalizeVirtualPath(virtualRomFsPath), outputMode);
        var targetPath = ResolveOutputPath(paths, virtualRomFsPath, outputMode);

        return new PlannedWriteInfo(
            targetRelativePath,
            sources,
            File.Exists(targetPath));
    }

    public static void Write(
        ProjectPaths paths,
        string virtualRomFsPath,
        byte[] bytes,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        lock (OutputWriteSyncRoot)
        {
            var targetPath = ResolveOutputPath(paths, virtualRomFsPath, outputMode);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, bytes);
            if (outputMode == SvOutputMode.Standalone)
            {
                WritePatchedDescriptor(paths);
            }
        }
    }

    public static PlannedWriteInfo CreateDescriptorPlannedWrite(ProjectPaths paths)
    {
        var sources = new[]
        {
            new ProjectFileReference(ProjectFileLayer.Base, ToRelativePath(DescriptorVirtualPath)),
        };
        return CreatePlannedWrite(paths, DescriptorVirtualPath, sources, SvOutputMode.Standalone);
    }

    private static void WritePatchedDescriptor(ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException("Scarlet/Violet descriptor patching requires a base RomFS path.");
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Scarlet/Violet descriptor patching requires an output root.");
        }

        var descriptorBytes = SvTrinityDescriptorPatcher.CreateLayeredDescriptor(
            paths.BaseRomFsPath,
            paths.OutputRootPath);
        var descriptorPath = ResolveOutputPath(paths, DescriptorVirtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);
        File.WriteAllBytes(descriptorPath, descriptorBytes);
    }

    public static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? actual = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: actual,
            Domain: "sv.editor",
            Field: field,
            Expected: expected);
    }

    public static bool IsScarletViolet(ProjectGame? game)
    {
        return game is ProjectGame.Scarlet or ProjectGame.Violet;
    }

    private static ProjectFileGraphEntry? FindEntry(OpenedProject project, string relativePath)
    {
        return project.FileGraph.Entries.FirstOrDefault(
            entry => string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRelativePath(string virtualRomFsPath)
    {
        return $"romfs/{virtualRomFsPath}";
    }

    private static string ToOutputRelativePath(string normalizedVirtualPath, SvOutputMode outputMode)
    {
        return outputMode switch
        {
            SvOutputMode.Standalone => ToRelativePath(normalizedVirtualPath),
            SvOutputMode.TrinityModManager => normalizedVirtualPath,
            SvOutputMode.TrinityBypass => ToRelativePath(normalizedVirtualPath),
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
    }

    private static string NormalizeVirtualPath(string virtualRomFsPath)
    {
        var normalized = virtualRomFsPath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["romfs/".Length..];
        }

        return normalized;
    }

    private static string CombineGraphPath(string rootPath, string relativePath)
    {
        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static (string Path, bool IsStandalone)? SelectLatestLooseOutput(
        string trinityModManagerPath,
        string standalonePath)
    {
        var trinityModManagerExists = FileExistsForInspection(trinityModManagerPath);
        var standaloneExists = FileExistsForInspection(standalonePath);
        if (!trinityModManagerExists)
        {
            return standaloneExists ? (standalonePath, true) : null;
        }

        if (!standaloneExists)
        {
            return (trinityModManagerPath, false);
        }

        return File.GetLastWriteTimeUtc(standalonePath) > File.GetLastWriteTimeUtc(trinityModManagerPath)
            ? (standalonePath, true)
            : (trinityModManagerPath, false);
    }

    private static bool TryReadOutputArchive(
        ProjectPaths paths,
        string virtualPath,
        string relativePath,
        out byte[] bytes)
    {
        bytes = [];
        try
        {
            var outputRootPath = paths.OutputRootPath;
            if (string.IsNullOrWhiteSpace(outputRootPath))
            {
                return false;
            }

            if (!HasTrinityArchive(outputRootPath))
            {
                return false;
            }

            using var archive = SvTrinityArchive.Open(
                outputRootPath,
                paths.ScarletVioletSupportFolderPath);
            return archive.TryReadFile(virtualPath, out bytes);
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            // An existing output archive participates in the game's effective data. Falling back
            // to base after an inspection failure would hide that source and could stage edits
            // against bytes the game does not use.
            throw CreateReadFailure(
                relativePath,
                ProjectFileLayer.Layered,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
        }
    }

    private static bool TryOutputArchiveContains(
        ProjectPaths paths,
        string virtualPath,
        string relativePath)
    {
        try
        {
            var outputRootPath = paths.OutputRootPath;
            if (string.IsNullOrWhiteSpace(outputRootPath) || !HasTrinityArchive(outputRootPath))
            {
                return false;
            }

            using var archive = SvTrinityArchive.Open(
                outputRootPath,
                paths.ScarletVioletSupportFolderPath);
            return archive.ContainsFile(virtualPath);
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            // Treat an unreadable output archive as an unknown candidate, not as an absent file.
            throw CreateReadFailure(
                relativePath,
                ProjectFileLayer.Layered,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
        }
    }

    private static byte[] ReadAllBytes(
        string path,
        string relativePath,
        ProjectFileLayer layer,
        ProjectFileGraphEntryState? state)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(relativePath, layer, state, exception);
        }
    }

    private static bool FileExistsWithContext(
        string path,
        string relativePath,
        ProjectFileLayer layer,
        ProjectFileGraphEntryState? state)
    {
        try
        {
            return FileExistsForInspection(path);
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(
                relativePath,
                layer,
                state,
                exception,
                ProjectFileOperation.Inspect);
        }
    }

    private static bool FileExistsForInspection(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static ProjectFileOperationException CreateReadFailure(
        string relativePath,
        ProjectFileLayer? layer,
        ProjectFileGraphEntryState? state,
        Exception exception,
        ProjectFileOperation operation = ProjectFileOperation.Read)
    {
        return exception as ProjectFileOperationException
            ?? new ProjectFileOperationException(
                operation,
                relativePath,
                layer,
                state,
                exception);
    }

    private static bool IsContextualFileFailure(Exception exception)
    {
        return exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or SecurityException
            or DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException;
    }

    private static bool HasTrinityArchive(string rootPath)
    {
        return HasTrinityArchiveAt(rootPath)
            || HasTrinityArchiveAt(Path.Combine(rootPath, "romfs"));
    }

    private static bool HasTrinityArchiveAt(string romFsRoot)
    {
        return FileExistsForInspection(Path.Combine(romFsRoot, "arc", "data.trpfd"))
            && FileExistsForInspection(Path.Combine(romFsRoot, "arc", "data.trpfs"));
    }
}

internal sealed record SvWorkflowFile(
    string VirtualPath,
    string RelativePath,
    byte[] Bytes,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

internal sealed record SvWorkflowArchiveInventory(
    IReadOnlyList<string> PackNames,
    IReadOnlySet<ulong> FileHashes);

internal sealed record PlannedWriteInfo(
    string TargetRelativePath,
    IReadOnlyList<ProjectFileReference> Sources,
    bool ReplacesExistingOutput);
