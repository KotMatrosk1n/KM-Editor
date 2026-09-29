// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.SV.TitanSwapper;

/// <summary>Guards the beta runtime against separate packages that shadow its shared script.</summary>
public static class SvTitanSwapperCompatibility
{
    public const string Message = "Titan Swapper and Gameplay Options cannot be combined in this beta. Restore Titan replacements or remove the Gameplay Options package before using the other editor.";

    internal static bool HasGameplayPackage(ProjectPaths paths)
    {
        var title = paths.SelectedGame == ProjectGame.Scarlet ? "0100A3D008C5C000" : "01008F6008C5E000";
        return ReadOutput(paths, $"config/km-editor/gameplay-settings/{title}/bundle.manifest").State.Exists
            || new[] { $"atmosphere/contents/{title}", $"mods/contents/{title}/KM-Gameplay-Settings", $"load/{title}/KM-Gameplay-Settings" }
                .Any(root => ReadOutput(paths, $"{root}/romfs/{SvTitanSwapperScript.VirtualPath}").State.Exists);
    }

    public static IReadOnlyList<OutputReadDependency> ReviewGameplayMenuSources(ProjectPaths paths)
    {
        if (paths.SelectedGame is not (ProjectGame.Scarlet or ProjectGame.Violet)) return [];
        var sources = new[] { $"romfs/{SvTitanSwapperScript.VirtualPath}", SvTitanSwapperScript.VirtualPath }
            .Select(path => (Path: new RelativeOutputPath(path), Source: ReadOutput(paths, path))).ToArray();
        foreach (var source in sources)
            if (source.Source.State.Exists && SvTitanSwapperScript.ReadLabels(source.Source.Bytes).Count != 0)
                throw new InvalidDataException(Message);
        return sources.Select(source => new OutputReadDependency(source.Path, source.Source.State)).ToArray();
    }

    private static (OutputFileState State, byte[] Bytes) ReadOutput(ProjectPaths paths, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath)) return (OutputFileState.Missing, []);
        if (!Path.IsPathFullyQualified(paths.OutputRootPath))
            throw new IOException("A valid Output Root is required.");
        var path = Path.GetFullPath(paths.OutputRootPath);
        var parts = relativePath.Split('/');
        for (var i = -1; i < parts.Length; i++)
        {
            if (i >= 0) path = Path.Combine(path, parts[i]);
            var entry = i == parts.Length - 1 ? (FileSystemInfo)new FileInfo(path) : new DirectoryInfo(path);
            entry.Refresh();
            if (entry.LinkTarget is not null || entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("A shared script source is linked or ambiguous.");
            if (i < parts.Length - 1 && File.Exists(path) || i == parts.Length - 1 && Directory.Exists(path))
                throw new IOException("A shared script source has an unexpected entry type.");
            if (!entry.Exists) return (OutputFileState.Missing, []);
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("Invalid shared script size.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return (OutputFileState.Existing(Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength), bytes);
    }
}
