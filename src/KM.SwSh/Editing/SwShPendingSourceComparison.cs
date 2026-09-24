// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.SwSh.Editing;

/// <summary>Checks staged intent independently of whether a base backed output currently exists.</summary>
internal static class SwShPendingSourceComparison
{
    public static bool Matches(IReadOnlyList<ProjectFileReference> staged, IReadOnlyList<ProjectFileReference> current,
        string? optionalOutputPath = null)
    {
        static bool IsCanonical(IReadOnlyList<ProjectFileReference> sources) => sources.SequenceEqual(sources
            .Distinct().OrderBy(source => source.Layer).ThenBy(source => source.RelativePath, StringComparer.Ordinal));

        if (!IsCanonical(staged) || !IsCanonical(current)) return false;

        var basePaths = current.Where(source => source.Layer == ProjectFileLayer.Base)
            .Select(source => source.RelativePath).ToHashSet(StringComparer.Ordinal);
        bool IsStable(ProjectFileReference source) => source.Layer != ProjectFileLayer.Layered
            || !basePaths.Contains(source.RelativePath) && !string.Equals(source.RelativePath, optionalOutputPath, StringComparison.Ordinal);

        // A fresh review resolves and fingerprints the current files. Staging keeps the base
        // and payload identity, including when generated output is deleted or recreated.
        return staged.Where(IsStable).SequenceEqual(current.Where(IsStable));
    }
}
