// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Files;

public enum ProjectFileGraphEntryState
{
    BaseOnly,
    LayeredOverride,
    LayeredOnly,
}

public sealed record ProjectFileGraphEntry(
    string RelativePath,
    ProjectFileReference? BaseFile,
    ProjectFileReference? LayeredFile,
    ProjectFileGraphEntryState State);

public sealed record ProjectFileGraph(IReadOnlyList<ProjectFileGraphEntry> Entries)
{
    public ProjectFileGraphSummary ToSummary()
    {
        return new ProjectFileGraphSummary(
            BaseFileCount: Entries.Count(entry => entry.BaseFile is not null),
            LayeredFileCount: Entries.Count(entry => entry.LayeredFile is not null),
            OverrideCount: Entries.Count(entry => entry.State == ProjectFileGraphEntryState.LayeredOverride),
            LayeredOnlyCount: Entries.Count(entry => entry.State == ProjectFileGraphEntryState.LayeredOnly));
    }
}

public sealed record ProjectFileGraphSummary(
    int BaseFileCount,
    int LayeredFileCount,
    int OverrideCount,
    int LayeredOnlyCount);

