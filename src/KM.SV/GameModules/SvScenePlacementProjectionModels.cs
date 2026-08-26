// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.SV.GameModules;

public enum SvScenePlacementDomain
{
    VisibleItem,
    HiddenItemPool,
    RummagingItemPool,
}

public sealed record SvScenePlacementOwnedField(
    string FieldKey,
    long? CanonicalValue);

public sealed record SvScenePlacementSource(
    string SourceIdentity,
    SvScenePlacementDomain Domain,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState,
    int RecordCount);

public sealed record SvScenePlacementEntry(
    string StableIdentity,
    SvScenePlacementDomain Domain,
    string SourceIdentity,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState,
    int Occurrence,
    IReadOnlyList<SvScenePlacementOwnedField> Fields);

public sealed record SvScenePlacementProjection(
    IReadOnlyList<SvScenePlacementSource> Sources,
    IReadOnlyList<SvScenePlacementEntry> Entries);

public sealed class SvScenePlacementObservationChangedException : IOException
{
    public SvScenePlacementObservationChangedException()
        : base("The Scarlet/Violet placement sources changed during fresh inspection.")
    {
    }
}
