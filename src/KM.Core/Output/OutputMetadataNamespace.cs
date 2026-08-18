// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;
using KM.Core.Files;

namespace KM.Core.Output;

/// <summary>
/// Identifies the private metadata namespace that must never become mod payload content.
/// </summary>
public static class OutputMetadataNamespace
{
    public const string DirectoryName = ".km";

    public static bool ContainsReservedSegment(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(IsReservedSegment);
    }

    public static bool IsReservedExistingPath(string? path)
    {
        if (ContainsReservedSegment(path))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var existingPath = Path.GetFullPath(path);
            while (!File.Exists(existingPath) && !Directory.Exists(existingPath))
            {
                var parent = Path.GetDirectoryName(existingPath);
                if (string.IsNullOrWhiteSpace(parent)
                    || string.Equals(parent, existingPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                existingPath = parent;
            }

            var capacity = 512;
            while (capacity <= 32768)
            {
                var expanded = new StringBuilder(capacity);
                var length = GetLongPathName(existingPath, expanded, (uint)expanded.Capacity);
                if (length == 0)
                {
                    return true;
                }

                if (length < expanded.Capacity)
                {
                    return ContainsReservedSegment(expanded.ToString());
                }

                capacity = checked((int)length + 1);
            }

            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            OverflowException)
        {
            return true;
        }
    }

    public static bool IsSafeExistingPayloadPath(string? path, bool isDirectory)
    {
        return !IsReservedExistingPath(path)
            && path is not null
            && FileSystemPathBoundary.HasSafeExistingChain(path, isDirectory);
    }

    public static bool IsSafePayloadDestinationPath(string? path)
    {
        return !IsReservedExistingPath(path)
            && path is not null
            && FileSystemPathBoundary.HasSafeExistingAncestorChain(path);
    }

    private static bool IsReservedSegment(string segment)
    {
        var portableSegment = segment.Trim().TrimEnd(' ', '.');
        return string.Equals(portableSegment, DirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(
        string shortPath,
        StringBuilder longPath,
        uint bufferLength);
}
