// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Output;

/// <summary>
/// A non-feature whole-file claim that remembers KM created an output file while
/// active record or byte-range claims still need that file to remain present.
/// It is intentionally distinguishable from an editor's active ownership.
/// </summary>
public static class OutputCreatorProvenance
{
    public const string OwnerKey = "output.creator-provenance";
    public const string PreservationRuleKey = "output.creator-provenance";

    private static readonly OwnershipOwnerId ProvenanceOwnerId = new(OwnerKey);
    private static readonly PreservationRuleDescriptor ProvenanceRule = new(
        PreservationRuleKey,
        schemaVersion: 1,
        preservesUnownedData: true,
        requiresPreimage: true);

    public static OwnedTarget Create(GameFamily gameFamily, RelativeOutputPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new OwnedTarget(
            gameFamily,
            new OwnedTargetAddress(path),
            ProvenanceOwnerId,
            ProvenanceRule);
    }

    public static bool IsClaim(OwnedTarget claim)
    {
        return claim is not null
            && claim.Address.ScopeKind == OwnedTargetScopeKind.File
            && claim.OwnerId == ProvenanceOwnerId
            && claim.PreservationRule == ProvenanceRule;
    }
}
