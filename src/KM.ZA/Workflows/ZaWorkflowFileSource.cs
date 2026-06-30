// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA;

namespace KM.ZA.Workflows;

internal sealed class ZaWorkflowFileSource
{
    public const string DescriptorVirtualPath = ZaTrinityDescriptorPatcher.DescriptorVirtualPath;

    private readonly ZaCacheManager cacheManager;

    public ZaWorkflowFileSource(ZaCacheManager? cacheManager = null)
    {
        this.cacheManager = cacheManager ?? new ZaCacheManager();
    }

    public ZaWorkflowFile Read(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            var trinityModManagerPath = CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath);
            if (File.Exists(trinityModManagerPath))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(trinityModManagerPath),
                    ProjectFileLayer.Layered,
                    ProjectFileGraphEntryState.LayeredOverride);
            }

            var standalonePath = CombineGraphPath(project.Paths.OutputRootPath, relativePath);
            if (File.Exists(standalonePath))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(standalonePath),
                    ProjectFileLayer.Layered,
                    entry?.State ?? ProjectFileGraphEntryState.LayeredOverride);
            }

            if (TryReadOutputArchive(project.Paths, normalizedVirtualPath, out var layeredArchiveBytes))
            {
                return new ZaWorkflowFile(
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
            if (File.Exists(looseBasePath))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(looseBasePath),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            try
            {
                var archiveBytes = cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }
            catch (FileNotFoundException)
            {
            }
        }

        throw new FileNotFoundException($"Pokemon Legends Z-A file '{relativePath}' could not be resolved.");
    }

    public ZaWorkflowFile ReadBase(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);

        if (!string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            var looseBasePath = CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath);
            if (File.Exists(looseBasePath))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(looseBasePath),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            try
            {
                var archiveBytes = cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }
            catch (FileNotFoundException)
            {
            }
        }

        throw new FileNotFoundException($"Pokemon Legends Z-A base file '{relativePath}' could not be resolved.");
    }

    public bool Exists(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            if (File.Exists(CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath))
                || File.Exists(CombineGraphPath(project.Paths.OutputRootPath, relativePath))
                || TryOutputArchiveContains(project.Paths, normalizedVirtualPath))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            return false;
        }

        if (File.Exists(CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath)))
        {
            return true;
        }

        try
        {
            return cacheManager.ContainsBaseTrinityFile(project.Paths, normalizedVirtualPath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> ListBasePackNames(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        try
        {
            return cacheManager.ListBaseTrinityPackNames(project.Paths);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    public static ProjectFileReference CreateReference(ZaWorkflowFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ProjectFileReference(file.SourceLayer, file.RelativePath);
    }

    public static string ResolveOutputPath(
        ProjectPaths paths,
        string virtualRomFsPath,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Set an output root before applying Pokemon Legends Z-A edits.");
        }

        var targetRelativePath = ToOutputRelativePath(NormalizeVirtualPath(virtualRomFsPath), outputMode);
        if (Path.IsPathRooted(targetRelativePath))
        {
            throw new InvalidOperationException($"Pokemon Legends Z-A target path '{targetRelativePath}' must be relative.");
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, targetRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);
        if (pathFromOutputRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(pathFromOutputRoot))
        {
            throw new InvalidOperationException($"Pokemon Legends Z-A target path '{targetRelativePath}' escapes the output root.");
        }

        return targetPath;
    }

    public static PlannedWriteInfo CreatePlannedWrite(
        ProjectPaths paths,
        string virtualRomFsPath,
        IReadOnlyList<ProjectFileReference> sources,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
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
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var targetPath = ResolveOutputPath(paths, virtualRomFsPath, outputMode);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(targetPath, bytes);
        if (outputMode == ZaOutputMode.Standalone)
        {
            WritePatchedDescriptor(paths);
        }
    }

    public static PlannedWriteInfo CreateDescriptorPlannedWrite(ProjectPaths paths)
    {
        var sources = new[]
        {
            new ProjectFileReference(ProjectFileLayer.Base, ToRelativePath(DescriptorVirtualPath)),
        };
        return CreatePlannedWrite(paths, DescriptorVirtualPath, sources, ZaOutputMode.Standalone);
    }

    private static void WritePatchedDescriptor(ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A descriptor patching requires a base RomFS path.");
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A descriptor patching requires an output root.");
        }

        var descriptorBytes = ZaTrinityDescriptorPatcher.CreateLayeredDescriptor(
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
            Domain: "za.editor",
            Field: field,
            Expected: expected);
    }

    public static bool IsPokemonLegendsZA(ProjectGame? game)
    {
        return game is ProjectGame.ZA;
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

    private static string ToOutputRelativePath(string normalizedVirtualPath, ZaOutputMode outputMode)
    {
        return outputMode switch
        {
            ZaOutputMode.Standalone => ToRelativePath(normalizedVirtualPath),
            ZaOutputMode.TrinityModManager => normalizedVirtualPath,
            ZaOutputMode.TrinityBypass => ToRelativePath(normalizedVirtualPath),
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

    private static bool TryReadOutputArchive(ProjectPaths paths, string virtualPath, out byte[] bytes)
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

            using var archive = ZaTrinityArchive.Open(
                outputRootPath,
                paths.PokemonLegendsZASupportFolderPath);
            return archive.TryReadFile(virtualPath, out bytes);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryOutputArchiveContains(ProjectPaths paths, string virtualPath)
    {
        try
        {
            var outputRootPath = paths.OutputRootPath;
            if (string.IsNullOrWhiteSpace(outputRootPath) || !HasTrinityArchive(outputRootPath))
            {
                return false;
            }

            using var archive = ZaTrinityArchive.Open(
                outputRootPath,
                paths.PokemonLegendsZASupportFolderPath);
            return archive.ContainsFile(virtualPath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasTrinityArchive(string rootPath)
    {
        return HasTrinityArchiveAt(rootPath)
            || HasTrinityArchiveAt(Path.Combine(rootPath, "romfs"));
    }

    private static bool HasTrinityArchiveAt(string romFsRoot)
    {
        return File.Exists(Path.Combine(romFsRoot, "arc", "data.trpfd"))
            && File.Exists(Path.Combine(romFsRoot, "arc", "data.trpfs"));
    }
}

internal sealed record ZaWorkflowFile(
    string VirtualPath,
    string RelativePath,
    byte[] Bytes,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

internal sealed record PlannedWriteInfo(
    string TargetRelativePath,
    IReadOnlyList<ProjectFileReference> Sources,
    bool ReplacesExistingOutput);
