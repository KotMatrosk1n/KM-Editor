// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Tools.Application;

/// <summary>
/// One immutable, KM-generated gameplay bundle. Bundle bytes are accepted only
/// through application construction and are never supplied by a bridge request.
/// </summary>
public sealed class InGameSettingsBundleCatalogEntry
{
    private readonly byte[] archiveBytes;

    public InGameSettingsBundleCatalogEntry(
        GameFamily gameFamily,
        ReadOnlyMemory<byte> archiveBytes,
        bool isCurrent)
    {
        if (!Enum.IsDefined(gameFamily))
        {
            throw new ArgumentOutOfRangeException(nameof(gameFamily));
        }

        this.archiveBytes = archiveBytes.ToArray();
        var bundle = GameplayBundleArchive.Read(
            this.archiveBytes,
            GameplayBundleDeploymentPlanner.ToSettingsFamily(gameFamily));
        var game = ProjectGameMetadata.DetectByTitleId(bundle.Manifest.TitleId);
        if (game is null || !gameFamily.Contains(game.Value))
        {
            throw new ArgumentException(
                "The gameplay bundle title does not belong to its catalog game family.",
                nameof(archiveBytes));
        }

        GameFamily = gameFamily;
        IsCurrent = isCurrent;
        Manifest = bundle.Manifest;
        ArchiveSha256 = bundle.Sha256;
        AuthorityKey = GameplaySettingsBundleAuthorityKey.FromManifest(
            gameFamily,
            bundle.Manifest,
            bundle.ManifestBytes.AsSpan());
        TargetCount = bundle.Entries.Length;
    }

    public GameFamily GameFamily { get; }

    public bool IsCurrent { get; }

    public GameplayBundleManifest Manifest { get; }

    public string ArchiveSha256 { get; }

    public GameplaySettingsBundleAuthorityKey AuthorityKey { get; }

    public int TargetCount { get; }

    internal ReadOnlyMemory<byte> ArchiveBytes => archiveBytes;
}

/// <summary>
/// Immutable inventory of KM-generated bundles. The production default is empty.
/// Exact authorization is enforced independently by GameplaySettingsBundleAuthority.
/// </summary>
public sealed class InGameSettingsBundleCatalog
{
    private readonly ImmutableArray<InGameSettingsBundleCatalogEntry> entries;

    private InGameSettingsBundleCatalog(
        ImmutableArray<InGameSettingsBundleCatalogEntry> entries)
    {
        this.entries = entries;
    }

    public static InGameSettingsBundleCatalog Empty { get; } = new([]);

    public static InGameSettingsBundleCatalog Create(
        IEnumerable<InGameSettingsBundleCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToImmutableArray();
        if (materialized.Length > GameplaySettingsBundleAuthority.MaximumAuthorizedBundles
            || materialized.Any(entry => entry is null))
        {
            throw new ArgumentException(
                "The in-game settings bundle catalog is invalid or out of bounds.",
                nameof(entries));
        }

        if (materialized
                .GroupBy(entry => (entry.GameFamily, entry.Manifest.TitleId))
                .Any(group => group.Count(entry => entry.IsCurrent) > 1)
            || materialized
                .GroupBy(entry => (entry.GameFamily, entry.Manifest.TitleId, entry.Manifest.BundleId))
                .Any(group => group.Count() != 1)
            || materialized
                .GroupBy(entry => entry.ArchiveSha256, StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
        {
            throw new ArgumentException(
                "The in-game settings bundle catalog contains duplicate or ambiguous entries.",
                nameof(entries));
        }

        foreach (var group in materialized.GroupBy(entry =>
                     (entry.GameFamily, entry.Manifest.TitleId)))
        {
            var current = group.SingleOrDefault(entry => entry.IsCurrent);
            if (current is not null
                && group.Any(entry => CompareVersion(
                    entry.Manifest.PackageVersion,
                    current.Manifest.PackageVersion) > 0))
            {
                throw new ArgumentException(
                    "A current catalog bundle cannot be older than another bundle for the same title.",
                    nameof(entries));
            }
        }

        return new InGameSettingsBundleCatalog(materialized
            .OrderBy(entry => entry.GameFamily)
            .ThenBy(entry => entry.Manifest.TitleId)
            .ThenBy(entry => entry.Manifest.PackageVersion.Major)
            .ThenBy(entry => entry.Manifest.PackageVersion.Minor)
            .ThenBy(entry => entry.Manifest.PackageVersion.Patch)
            .ThenBy(entry => entry.Manifest.BundleId, StringComparer.Ordinal)
            .ToImmutableArray());
    }

    internal ImmutableArray<InGameSettingsBundleCatalogEntry> GetEntries(
        GameFamily gameFamily,
        ulong titleId)
    {
        return entries
            .Where(entry => entry.GameFamily == gameFamily
                && entry.Manifest.TitleId == titleId)
            .ToImmutableArray();
    }

    private static int CompareVersion(
        GameplayBundleVersion left,
        GameplayBundleVersion right)
    {
        var major = left.Major.CompareTo(right.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = left.Minor.CompareTo(right.Minor);
        return minor != 0 ? minor : left.Patch.CompareTo(right.Patch);
    }
}
