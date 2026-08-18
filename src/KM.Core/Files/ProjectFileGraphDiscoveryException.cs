// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Files;

public enum ProjectFileGraphDiscoveryLimit
{
    FileSystemEntries,
    Directories,
    TraversalDepth,
    GraphEntries,
}

public sealed class ProjectFileGraphDiscoveryException : IOException
{
    public ProjectFileGraphDiscoveryException(ProjectFileGraphDiscoveryLimit limitKind, int limit)
        : base(CreateMessage(limitKind, limit))
    {
        if (!Enum.IsDefined(limitKind))
        {
            throw new ArgumentOutOfRangeException(nameof(limitKind), limitKind, null);
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "The discovery limit must be positive.");
        }

        LimitKind = limitKind;
        Limit = limit;
    }

    public ProjectFileGraphDiscoveryLimit LimitKind { get; }

    public int Limit { get; }

    private static string CreateMessage(ProjectFileGraphDiscoveryLimit limitKind, int limit)
    {
        return $"Project file discovery exceeded the {limitKind} limit of {limit}.";
    }
}
