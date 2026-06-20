// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV;

namespace KM.SV.Workflows;

internal sealed class SvWorkflowFileSource
{
    public const string DescriptorVirtualPath = SvTrinityDescriptorPatcher.DescriptorVirtualPath;

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
            if (File.Exists(trinityModManagerPath))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(trinityModManagerPath),
                    ProjectFileLayer.Layered,
                    ProjectFileGraphEntryState.LayeredOverride);
            }

            var standalonePath = CombineGraphPath(project.Paths.OutputRootPath, relativePath);
            if (File.Exists(standalonePath))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(standalonePath),
                    ProjectFileLayer.Layered,
                    entry?.State ?? ProjectFileGraphEntryState.LayeredOverride);
            }

            if (TryReadOutputArchive(project.Paths, normalizedVirtualPath, out var layeredArchiveBytes))
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
            if (File.Exists(looseBasePath))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(looseBasePath),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            using var archive = SvTrinityArchive.Open(
                project.Paths.BaseRomFsPath,
                project.Paths.ScarletVioletSupportFolderPath);
            if (archive.TryReadFile(normalizedVirtualPath, out var archiveBytes))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }
        }

        throw new FileNotFoundException($"Scarlet/Violet file '{relativePath}' could not be resolved.");
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
            if (File.Exists(looseBasePath))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(looseBasePath),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            using var archive = SvTrinityArchive.Open(
                project.Paths.BaseRomFsPath,
                project.Paths.ScarletVioletSupportFolderPath);
            if (archive.TryReadFile(normalizedVirtualPath, out var archiveBytes))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }
        }

        throw new FileNotFoundException($"Scarlet/Violet base file '{relativePath}' could not be resolved.");
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
        if (pathFromOutputRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(pathFromOutputRoot))
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

        var targetPath = ResolveOutputPath(paths, virtualRomFsPath, outputMode);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(targetPath, bytes);
        if (outputMode == SvOutputMode.Standalone)
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

            using var archive = SvTrinityArchive.Open(
                outputRootPath,
                paths.ScarletVioletSupportFolderPath);
            return archive.TryReadFile(virtualPath, out bytes);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            bytes = [];
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

internal sealed record SvWorkflowFile(
    string VirtualPath,
    string RelativePath,
    byte[] Bytes,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

internal sealed record PlannedWriteInfo(
    string TargetRelativePath,
    IReadOnlyList<ProjectFileReference> Sources,
    bool ReplacesExistingOutput);
