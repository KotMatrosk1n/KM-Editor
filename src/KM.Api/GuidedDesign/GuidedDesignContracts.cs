// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.ChangeSets;
using KM.Api.Diagnostics;
using KM.Api.Semantics;

namespace KM.Api.GuidedDesign;

public static class GuidedDesignContract
{
    private const int ProvisionMultiplier = 4;
    private const int HardCeilingMultiplier = 2;

    public const int SchemaVersion = 1;
    public const int MaximumTargets = 128;
    public const int MaximumPins = 128;
    public const int ExpectedMutations = 768;
    public const int ProvisionedMutations = checked(ExpectedMutations * ProvisionMultiplier);
    public const int MaximumMutations = checked(ProvisionedMutations * HardCeilingMultiplier);
    public const int ExpectedAffectedRecords = 128;
    public const int ProvisionedAffectedRecords = checked(
        ExpectedAffectedRecords * ProvisionMultiplier);
    public const int MaximumAffectedRecords = checked(
        ProvisionedAffectedRecords * HardCeilingMultiplier);
    public const int ExpectedEligibleTargets = 50_000;
    public const int ProvisionedEligibleTargets = checked(
        ExpectedEligibleTargets * ProvisionMultiplier);
    public const int MaximumEligibleTargets = checked(
        ProvisionedEligibleTargets * HardCeilingMultiplier);
    public const int MaximumTargetSelectionWindow = 500;
    public const int MaximumTargetSearchTextLength = 256;
    public const int ExpectedFindings = 100;
    public const int ProvisionedFindings = checked(ExpectedFindings * ProvisionMultiplier);
    public const int MaximumFindings = checked(
        ProvisionedFindings * HardCeilingMultiplier);
    public const int MaximumPageSize = 100;
    public const int MaximumCursorLength = 2_048;
    public const int MaximumFieldKeys = 32;
    public const int MaximumChangeSetNameLength = 128;
    public const int MaximumCanonicalIntegerLength = 20;
    public const int ExpectedCanonicalExportBytes = 1 * 1_024 * 1_024;
    public const int ProvisionedCanonicalExportBytes = checked(
        ExpectedCanonicalExportBytes * ProvisionMultiplier);
    public const int MaximumCanonicalExportBytes = checked(
        ProvisionedCanonicalExportBytes * HardCeilingMultiplier);
}

public enum GuidedDesignFeatureDto
{
    DifficultyDesigner,
    EncounterPopulationDesigner,
    EconomyRebalance,
    EvolutionAccessibility,
    TrainerArchetypes,
    ConstraintRandomization,
    Plando,
    SeedInspector,
    SpoilerRaceExport,
}

public enum GuidedDesignProposalKindDto
{
    TrainerLevelAdjustment,
    EncounterLevelAdjustment,
    EncounterWeightScale,
    EconomyPrimaryPriceScale,
    EvolutionLevelClamp,
    TrainerEvArchetype,
    PokemonBaseStatShuffle,
}

public enum GuidedDesignRoundingDto
{
    Floor,
    Nearest,
    Ceiling,
}

public enum GuidedDesignTrainerArchetypeDto
{
    PhysicalAttackSpeed,
    SpecialAttackSpeed,
    Balanced,
}

public enum GuidedDesignFindingSeverityDto
{
    Info,
    Warning,
    Error,
}

public enum GuidedDesignCanonicalExportKindDto
{
    Spoiler,
    Race,
}

public sealed record GuidedDesignCapabilityDto(
    GuidedDesignFeatureDto Feature,
    string ProviderId,
    SemanticCoverageStateDto State,
    SemanticConfidenceDto Confidence,
    string? ReasonCode,
    IReadOnlyList<GuidedDesignProposalKindDto> ProposalKinds,
    IReadOnlyList<SemanticSourceLayerKindDto> SourceLayers);

public sealed record GuidedDesignPinDto(
    SemanticRecordRefDto Record,
    string FieldKey,
    string CanonicalValue);

public sealed record GuidedDesignTargetOptionDto(
    SemanticRecordRefDto Record,
    string RecordLabel);

public sealed record GuidedDesignInputDto(
    GuidedDesignProposalKindDto Kind,
    IReadOnlyList<SemanticRecordRefDto> Targets,
    IReadOnlyList<GuidedDesignPinDto> Pins,
    IReadOnlyList<string> FieldKeys,
    int? Delta,
    int? MultiplierBasisPoints,
    int? MinimumValue,
    int? MaximumValue,
    GuidedDesignRoundingDto? Rounding,
    GuidedDesignTrainerArchetypeDto? Archetype,
    string? Seed);

public sealed record GuidedDesignMutationDto(
    string MutationId,
    SemanticRecordRefDto Record,
    string RecordLabel,
    string FieldKey,
    string FieldLabel,
    SemanticScalarValueDto Before,
    SemanticScalarValueDto After,
    bool Pinned,
    SemanticRecordRefDto? PinRecord,
    string? PinFieldKey,
    string ProviderId,
    string Summary);

public sealed record GuidedDesignFindingDto(
    string FindingId,
    string RuleId,
    GuidedDesignFindingSeverityDto Severity,
    SemanticConfidenceDto Confidence,
    string Title,
    string Summary,
    SemanticRecordRefDto? Record,
    IReadOnlyList<SemanticRecordRefDto> RelatedRecords);

public sealed record GuidedDesignCanonicalExportDto(
    GuidedDesignCanonicalExportKindDto Kind,
    int SchemaVersion,
    string MediaType,
    string SuggestedFileName,
    string Sha256,
    string Content);

public sealed record GuidedDesignCanonicalExportsDto(
    GuidedDesignCanonicalExportDto? Spoiler,
    GuidedDesignCanonicalExportDto? Race);

public sealed record ReadGuidedDesignCapabilitiesRequest(SemanticExploreScopeDto Scope);

public sealed record ReadGuidedDesignCapabilitiesResponse(
    SemanticProjectRevisionDto Revision,
    IReadOnlyList<SemanticSourceSnapshotDto> Snapshots,
    IReadOnlyList<GuidedDesignCapabilityDto> Capabilities);

public sealed record PreviewGuidedDesignRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string? ExpectedChangeSetETag,
    SemanticSourceLayerKindDto Layer,
    GuidedDesignInputDto Input,
    string? TargetSearchText,
    int Limit,
    string? Cursor = null,
    string? ProposalId = null,
    string? ProposalFingerprint = null);

public sealed record PreviewGuidedDesignResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    SemanticSourceSnapshotDto Snapshot,
    IReadOnlyList<GuidedDesignCapabilityDto> Capabilities,
    GuidedDesignInputDto NormalizedInput,
    string? Seed,
    string AuthoringContextFingerprint,
    string ProposalId,
    string ProposalFingerprint,
    bool CanImport,
    bool SelectionRequired,
    string? NormalizedTargetSearchText,
    bool EligibleTargetWindowCapped,
    int TotalEligibleTargetCount,
    IReadOnlyList<GuidedDesignTargetOptionDto> EligibleTargets,
    int TotalMutationCount,
    int TotalFindingCount,
    IReadOnlyList<SemanticRecordRefDto> AffectedRecords,
    IReadOnlyList<GuidedDesignMutationDto> Mutations,
    IReadOnlyList<GuidedDesignFindingDto> Findings,
    GuidedDesignCanonicalExportsDto Exports,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    string? NextCursor);

public sealed record ImportGuidedDesignProposalRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    GuidedDesignInputDto Input,
    string ProposalId,
    string ProposalFingerprint,
    string ChangeSetName,
    string? ExpectedChangeSetETag);

public sealed record ImportGuidedDesignProposalResponse(
    SemanticProjectRevisionDto Revision,
    string ProposalId,
    string ProposalFingerprint,
    string ImportedChangeSetId,
    ChangeSetWorkspaceSnapshotDto Snapshot,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
