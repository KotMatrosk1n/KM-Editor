// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Tools.Application;

/// <summary>Reviews current loose menu sources without claiming or modifying their ownership.</summary>
internal sealed class NativeGameplayMenuRomFsSources(ProjectPaths paths, CancellationToken cancellationToken)
{
    private const int MaximumSourceBytes = 64 * 1024 * 1024;
    private readonly List<OutputReadDependency> dependencies = [];
    public IReadOnlyList<OutputReadDependency> Dependencies => dependencies.ToArray();

    public byte[] Resolve(string relativePath, byte[] original)
    {
        if (!relativePath.StartsWith("romfs/", StringComparison.Ordinal))
            throw new InvalidDataException("A menu source must be a normalized RomFS path.");
        var standalone = Read(new RelativeOutputPath(relativePath));
        if (paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
            return standalone.Bytes ?? original;
        var manager = Read(new RelativeOutputPath(relativePath[6..]));
        // Match the ordinary editors when a project has switched output modes.
        var candidates = new List<(byte[]? Bytes, DateTime Modified, int Priority)>
        {
            (standalone.Bytes, standalone.Modified, 1),
            (manager.Bytes, manager.Modified, 2),
        };
        if (paths.SelectedGame == ProjectGame.ZA)
        {
            var isolated = Read(new RelativeOutputPath($"trinity-mod-manager-romfs/{relativePath[6..]}"));
            candidates.Add((isolated.Bytes, isolated.Modified, 3));
        }
        return candidates.Where(candidate => candidate.Bytes is not null)
            .OrderByDescending(candidate => candidate.Modified).ThenByDescending(candidate => candidate.Priority)
            .Select(candidate => candidate.Bytes!).FirstOrDefault() ?? original;
    }

    private (byte[]? Bytes, DateTime Modified) Read(RelativeOutputPath relative)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || !Path.IsPathFullyQualified(paths.OutputRootPath))
            throw new DirectoryNotFoundException("The project requires a valid Output Root.");
        var path = Path.GetFullPath(paths.OutputRootPath);
        var segments = relative.Value.Split('/');
        for (var index = -1; index < segments.Length; index++)
        {
            if (index >= 0) path = Path.Combine(path, segments[index]);
            var isFile = index == segments.Length - 1;
            FileSystemInfo info = isFile ? new FileInfo(path) : new DirectoryInfo(path);
            info.Refresh();
            if (info.LinkTarget is not null || info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || (isFile ? Directory.Exists(path) : File.Exists(path)))
                throw new IOException("A shared menu source is linked or has an unexpected entry type.");
            if (!info.Exists)
            {
                dependencies.Add(new(relative, OutputFileState.Missing));
                return (null, default);
            }
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumSourceBytes)
            throw new InvalidDataException("A shared menu source exceeds its supported size.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.ReadByte() != -1) throw new IOException("A shared menu source changed during review.");
        dependencies.Add(new(relative, OutputFileState.Existing(
            Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength)));
        return (bytes, File.GetLastWriteTimeUtc(path));
    }
}
