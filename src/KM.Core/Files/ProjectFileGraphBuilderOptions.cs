// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Files;

public sealed record ProjectFileGraphBuilderOptions
{
    public const int MaximumAllowedFileSystemEntries = 2_000_000;
    public const int MaximumAllowedDirectories = 250_000;
    public const int MaximumAllowedTraversalDepth = 256;
    public const int MaximumAllowedGraphEntries = 1_000_000;

    public int MaximumFileSystemEntries { get; init; } = MaximumAllowedFileSystemEntries;

    public int MaximumDirectories { get; init; } = MaximumAllowedDirectories;

    public int MaximumTraversalDepth { get; init; } = MaximumAllowedTraversalDepth;

    public int MaximumGraphEntries { get; init; } = MaximumAllowedGraphEntries;

    internal void Validate()
    {
        ValidateBound(
            MaximumFileSystemEntries,
            MaximumAllowedFileSystemEntries,
            nameof(MaximumFileSystemEntries));
        ValidateBound(MaximumDirectories, MaximumAllowedDirectories, nameof(MaximumDirectories));
        ValidateBound(
            MaximumTraversalDepth,
            MaximumAllowedTraversalDepth,
            nameof(MaximumTraversalDepth));
        ValidateBound(MaximumGraphEntries, MaximumAllowedGraphEntries, nameof(MaximumGraphEntries));
    }

    private static void ValidateBound(int value, int maximum, string parameterName)
    {
        if (value <= 0 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The value must be between 1 and {maximum}.");
        }
    }
}
