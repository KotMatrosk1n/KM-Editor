// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.SwSh.Editing;

internal static class SwShStagedSourceIdentity
{
    // Staging records file identity. Review binds the current layer and contents.
    // Another editor in the same transaction can create an output lookup file.
    public static bool Matches(ProjectFileReference staged, ProjectFileReference current) =>
        string.Equals(
            staged.RelativePath.Replace('\\', '/'),
            current.RelativePath.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase)
        && (staged.Layer == current.Layer
            || (staged.Layer is ProjectFileLayer.Base or ProjectFileLayer.Layered
                && current.Layer is ProjectFileLayer.Base or ProjectFileLayer.Layered));

    public static bool Contains(IEnumerable<ProjectFileReference> staged, ProjectFileReference current) =>
        staged.Any(source => Matches(source, current));
}
