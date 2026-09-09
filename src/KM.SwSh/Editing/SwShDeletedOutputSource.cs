// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;

namespace KM.SwSh.Editing;

internal static class SwShDeletedOutputSource
{
    public static bool Any(ProjectPaths paths, PendingEdit edit) =>
        edit.Sources.Any(source => IsMissing(paths, source));

    public static bool Contains(ProjectPaths paths, PendingEdit edit, string relativePath) =>
        edit.Sources.Any(source => string.Equals(source.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)
            && IsMissing(paths, source));

    private static bool IsMissing(ProjectPaths paths, ProjectFileReference source)
    {
        if (source.Layer != ProjectFileLayer.Layered || string.IsNullOrWhiteSpace(paths.OutputRootPath)
            || Path.IsPathRooted(source.RelativePath)) return false;
        var root = Path.GetFullPath(paths.OutputRootPath);
        var target = Path.GetFullPath(Path.Combine(root, source.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        return PathContainment.IsWithinRoot(Path.GetRelativePath(root, target))
            && HasPhysicalAncestors(target)
            && !Path.Exists(target);
    }

    private static bool HasPhysicalAncestors(string target)
    {
        try
        {
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(target)!); parent is not null; parent = parent.Parent)
            {
                if (File.Exists(parent.FullName)
                    || (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
