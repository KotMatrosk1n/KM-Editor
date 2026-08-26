// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;

namespace KM.ZA.TrainerPools;

public static class ZaTrainerPoolsDiagnosticCodes
{
    public const string Safety = "KM-ZA-TRAINER-POOLS-SAFETY";
    public const string UnsupportedMirrorShape = "KM-ZA-TRAINER-POOLS-MIRROR-SHAPE-UNSUPPORTED";
    public const string EditSafety = "KM-ZA-TRAINER-POOLS-EDIT-SAFETY";
    public const string ReviewedState = "KM-ZA-TRAINER-POOLS-REVIEWED-STATE";
    public const string SessionConflict = "KM-ZA-TRAINER-POOLS-SESSION-CONFLICT";
    public const string SwapAlreadyStaged = "KM-ZA-TRAINER-POOLS-SWAP-ALREADY-STAGED";
    public const string SelectionInvalid = "KM-ZA-TRAINER-POOLS-SELECTION-INVALID";
    public const string PoolsIncompatible = "KM-ZA-TRAINER-POOLS-INCOMPATIBLE";
    public const string SourceChanged = "KM-ZA-TRAINER-POOLS-SOURCE-CHANGED";
    public const string PlanStale = "KM-ZA-TRAINER-POOLS-PLAN-STALE";
    public const string VerificationFailed = "KM-ZA-TRAINER-POOLS-VERIFICATION-FAILED";
    public const string ApplyFailed = "KM-ZA-TRAINER-POOLS-APPLY-FAILED";
}

public enum ZaTrainerPoolKind
{
    Story,
    Infinity,
}

public sealed record ZaTrainerPoolMember(
    string RawTrainerId,
    string AppearanceAssetId,
    string RawRosterId,
    int RosterIndex,
    string DisplayName,
    int StoredRank,
    int TeamSize,
    int Weight);

public sealed record ZaTrainerPoolRecord(
    string LogicalPoolId,
    string DisplayLabel,
    string CompatibilityGroup,
    ZaTrainerPoolKind Kind,
    IReadOnlyList<string> PhysicalTableIds,
    int ReferencedPhysicalTableCount,
    int MemberCount,
    int TotalWeight,
    IReadOnlyList<ZaTrainerPoolMember> Members);

public sealed record ZaTrainerPoolsWorkflowStats(
    int LogicalPoolCount,
    int PhysicalMirrorCount,
    int MemberReferenceCount,
    int DormantPhysicalMirrorCount);

public sealed record ZaTrainerPoolsWorkflow(
    IReadOnlyList<ZaTrainerPoolRecord> Pools,
    ZaTrainerPoolsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    bool CanStage);

public sealed record ZaTrainerPoolFixedCountSwap(
    string SourceLogicalPoolId,
    string SourceRawTrainerId,
    string DestinationLogicalPoolId,
    string DestinationRawTrainerId);

public sealed record ZaTrainerPoolsEditResult(
    ZaTrainerPoolsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed record ZaTrainerPoolIdentityRecord(
    string RawTrainerId,
    string AppearanceAssetId,
    string RawRosterId,
    int RosterIndex,
    string DisplayName,
    int StoredRank,
    int TeamSize);

internal sealed record ZaTrainerPoolsLoadedState(
    ZaTrainerPoolsWorkflow Workflow,
    Data.ZaTrainerPoolDataDocument Document,
    IReadOnlyDictionary<string, ZaTrainerPoolIdentityRecord> Identities,
    IReadOnlySet<string> ReferencedTableIds,
    IReadOnlyList<ProjectFileReference> Sources);
