// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SV.GameModules;

public enum SvPackedLooseSourceKind
{
    BaseArchive,
    BaseLoose,
    StandaloneLooseOutput,
    ManagerLooseOutput,
    OutputArchive,
}

public enum SvPackedLooseEffectiveSource
{
    None,
    BaseArchive,
    BaseLoose,
    StandaloneLooseOutput,
    ManagerLooseOutput,
    OutputArchive,
}

public enum SvPackedLooseDualOutputState
{
    NotComparable,
    Identical,
    Divergent,
}

public sealed record SvPackedLooseSourceCandidate(
    SvPackedLooseSourceKind Kind,
    bool IsPresent,
    long? ByteLength,
    bool IsEffective,
    bool? MatchesEffective,
    bool? MatchesBaseArchive);

public sealed record SvPackedLooseSourceEntry(
    string VirtualIdentity,
    SvPackedLooseEffectiveSource EffectiveSource,
    SvPackedLooseDualOutputState DualLooseOutputState,
    IReadOnlyList<SvPackedLooseSourceCandidate> Candidates);

public sealed record SvPackedLooseSourceComparison(
    IReadOnlyList<SvPackedLooseSourceEntry> Entries,
    int DivergentDualLooseCount);

public sealed class SvPackedLooseSourceObservationChangedException : IOException
{
    public SvPackedLooseSourceObservationChangedException()
        : base("The Scarlet/Violet source candidates changed during fresh comparison.")
    {
    }
}
