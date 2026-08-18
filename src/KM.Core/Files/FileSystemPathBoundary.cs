// SPDX-License-Identifier: GPL-3.0-only

using System.Security;

namespace KM.Core.Files;

internal static class FileSystemPathBoundary
{
    public static bool HasSafeExistingChain(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            FileSystemInfo? entry = isDirectory
                ? new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                : new FileInfo(Path.GetFullPath(path));
            while (entry is not null)
            {
                entry.Refresh();
                if (!entry.Exists)
                {
                    return false;
                }

                var attributes = entry.Attributes;
                if (attributes.HasFlag(FileAttributes.ReparsePoint)
                    && !string.IsNullOrEmpty(entry.LinkTarget))
                {
                    return false;
                }

                entry = entry switch
                {
                    FileInfo file => file.Directory,
                    DirectoryInfo directory => directory.Parent,
                    _ => null,
                };
            }

            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    public static bool HasSafeExistingAncestorChain(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fileEntry = new FileInfo(fullPath);
            var directoryEntry = new DirectoryInfo(fullPath);
            fileEntry.Refresh();
            directoryEntry.Refresh();
            if (!string.IsNullOrEmpty(fileEntry.LinkTarget)
                || !string.IsNullOrEmpty(directoryEntry.LinkTarget))
            {
                return false;
            }

            FileSystemInfo? entry = directoryEntry.Exists ? directoryEntry : fileEntry;
            while (entry is not null)
            {
                entry.Refresh();
                if (!string.IsNullOrEmpty(entry.LinkTarget))
                {
                    return false;
                }

                if (entry.Exists)
                {
                    var attributes = entry.Attributes;
                    if (attributes.HasFlag(FileAttributes.ReparsePoint)
                        && !string.IsNullOrEmpty(entry.LinkTarget))
                    {
                        return false;
                    }
                }

                entry = entry switch
                {
                    FileInfo file => file.Directory,
                    DirectoryInfo directory => directory.Parent,
                    _ => null,
                };
            }

            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }
}
