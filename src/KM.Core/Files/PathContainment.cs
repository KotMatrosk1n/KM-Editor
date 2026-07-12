// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Files;

public static class PathContainment
{
    public static bool IsWithinRoot(string relativePath)
    {
        return !IsOutsideRoot(relativePath);
    }

    public static bool IsOutsideRoot(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return Path.IsPathRooted(relativePath)
            || string.Equals(relativePath, "..", StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
