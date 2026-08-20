// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Files;
using KM.Core.Semantics;

namespace KM.Core.Output;

/// <summary>
/// Captures a bounded output-directory membership revision without creating or
/// claiming output metadata. This observer is suitable for read-only previews.
/// </summary>
public static class ReadOnlyOutputDirectoryMembership
{
    public const int DefaultMaximumEntries = 100_000;

    public static OutputDirectoryMembershipSnapshot Capture(
        string outputRoot,
        RelativeOutputPath directory,
        int maximumEntries = DefaultMaximumEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(directory);
        if (!Path.IsPathFullyQualified(outputRoot))
        {
            throw new ArgumentException(
                "The output root must be a fully qualified path.",
                nameof(outputRoot));
        }

        if (maximumEntries <= 0 || maximumEntries > OutputLimits.MaximumIntegrityEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        var fullOutputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (!FileSystemPathBoundary.HasSafeExistingAncestorChain(fullOutputRoot))
        {
            throw new OutputPathSecurityException();
        }

        if (!Directory.Exists(fullOutputRoot))
        {
            if (File.Exists(fullOutputRoot))
            {
                throw new OutputPathSecurityException();
            }

            return CreateSnapshot(
                directory,
                exists: false,
                ImmutableArray<OutputDirectoryMembershipEntry>.Empty);
        }

        var safety = new OutputPathSafety(fullOutputRoot);
        var exists = safety.OwnedDirectoryExists(directory);
        var entries = exists
            ? safety.EnumerateDirectoryMembershipReadOnly(directory, maximumEntries)
                .OrderBy(entry => entry.Path.CanonicalKey, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<OutputDirectoryMembershipEntry>.Empty;
        return CreateSnapshot(directory, exists, entries);
    }

    private static OutputDirectoryMembershipSnapshot CreateSnapshot(
        RelativeOutputPath directory,
        bool exists,
        ImmutableArray<OutputDirectoryMembershipEntry> entries)
    {
        var tokens = new List<string?>
        {
            directory.CanonicalKey,
            exists ? "1" : "0",
        };
        foreach (var entry in entries)
        {
            tokens.Add(entry.Path.CanonicalKey);
            tokens.Add(entry.IsDirectory ? "D" : "F");
        }

        return new OutputDirectoryMembershipSnapshot(
            directory,
            exists,
            OutputRevisionCalculator.FromTokens("output-directory-membership-v1", tokens),
            entries);
    }
}
