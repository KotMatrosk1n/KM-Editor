// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SV.GameModules;

public enum SvEventComparisonDomain
{
    GiftPokemon,
    TradePokemon,
    EventDeliveryRaid,
}

public enum SvEventComparisonPresence
{
    Unchanged,
    Modified,
    Added,
    Removed,
}

public enum SvEventComparisonScalarKind
{
    Null,
    SignedInteger,
    Text,
}

public sealed record SvEventComparisonScalar(
    SvEventComparisonScalarKind Kind,
    string? CanonicalValue);

public sealed record SvEventFieldDifference(
    string FieldKey,
    string FieldLabel,
    SvEventComparisonScalar BaseValue,
    SvEventComparisonScalar EffectiveValue);

public sealed record SvEventComparisonEntry(
    string StableIdentity,
    SvEventComparisonDomain Domain,
    int Occurrence,
    SvEventComparisonPresence Presence,
    int ComparedFieldCount,
    IReadOnlyList<SvEventFieldDifference> Differences);

public sealed record SvEventDataComparison(
    IReadOnlyList<SvEventComparisonEntry> Entries,
    int ComparedEntityCount,
    int ChangedEntityCount,
    int ChangedFieldCount);

public sealed class SvEventDataObservationChangedException : IOException
{
    public SvEventDataObservationChangedException()
        : base("The Scarlet/Violet event data changed during fresh comparison.")
    {
    }
}
