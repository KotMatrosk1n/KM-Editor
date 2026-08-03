// SPDX-License-Identifier: GPL-3.0-only

using System.IO;

namespace KM.Setup.UI.Burn;

internal static class InstallPathPolicy
{
    public static string? TryNormalizeLocalFolder(string? candidate, bool mustExist)
    {
        candidate = TrimMatchingQuotes(candidate);
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Path.IsPathFullyQualified(candidate) ||
            candidate.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root) ||
                root.Length != 3 ||
                !char.IsAsciiLetter(root[0]) ||
                root[1] != ':' ||
                root[2] != Path.DirectorySeparatorChar ||
                string.Equals(fullPath, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase) ||
                fullPath.AsSpan(2).Contains(':'))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed || mustExist && !Directory.Exists(fullPath))
            {
                return null;
            }

            for (var directory = new DirectoryInfo(fullPath); directory is not null; directory = directory.Parent)
            {
                if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return null;
                }
            }

            return fullPath;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            IOException or
            NotSupportedException or
            PathTooLongException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TrimMatchingQuotes(string? value)
    {
        value = value?.Trim();
        return value is { Length: >= 2 } && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Trim()
            : value;
    }
}
