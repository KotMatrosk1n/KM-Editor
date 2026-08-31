// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Semantics;

namespace KM.Api.GameModules;

public static class GameModuleContract
{
    private const int ProvisionMultiplier = 4;
    private const int HardCeilingMultiplier = 2;
    private const int ExpectedMaximumRecords = 50_000;
    private const int ExpectedMaximumFacts = 100_000;

    private const int ProvisionedMaximumRecords = checked(
        ExpectedMaximumRecords * ProvisionMultiplier);
    private const int ProvisionedMaximumFacts = checked(
        ExpectedMaximumFacts * ProvisionMultiplier);

    public const int MaximumRecords = checked(
        ProvisionedMaximumRecords * HardCeilingMultiplier);
    public const int MaximumFacts = checked(
        ProvisionedMaximumFacts * HardCeilingMultiplier);
    public const int MaximumFactsPerRecord = 32;
    public const int MaximumEvidenceRecordsPerFact = 16;
    public const long MaximumEvidenceRecords = checked(
        (long)MaximumFacts * MaximumEvidenceRecordsPerFact);
    public const int MaximumDiagnostics = 100;
}

public enum GameModuleDto
{
    SwordShieldRewardEcosystem,
    SwordShieldExeFsCompatibility,
    SwordShieldDynamaxAdventures,
    SwordShieldRoyalCandyProgression,
    SwordShieldBattleCafeRewards,
    SwordShieldEventAssignments,
    ScarletVioletTeraRaidAnalysis,
    ScarletVioletPackedLooseComparison,
    ScarletVioletEventDataComparison,
    ScarletVioletScenePlacementEditing,
    ScarletVioletTypeEffectivenessState,
    ScarletVioletStellarBehavior,
    LegendsZaScriptedBossTimeline,
    LegendsZaTrainerArchetypes,
    LegendsZaWildSpawnExplorer,
    LegendsZaEncounterCompatibility,
    LegendsZaAlphaMoveDistribution,
    LegendsZaDexLayoutPlanning,
    LegendsZaMoveVariantComparison,
    LegendsZaTrainerPoolSwitching,
    LegendsZaTypeEffectivenessState,
    LegendsZaStaticMapMarkers,
    LegendsZaNamedFlagCatalog,
    LegendsZaPokemonResourceCatalog,
}

public enum GameModuleMaturityDto
{
    Product,
    ReadOnlyFirst,
    ResearchGated,
}

public sealed record GameModuleCapabilityDto(
    GameModuleDto Module,
    SemanticGameFamilyDto Family,
    GameModuleMaturityDto Maturity,
    string ProviderId,
    SemanticCoverageStateDto State,
    SemanticConfidenceDto Confidence,
    bool CanQuery,
    string? ReasonCode,
    IReadOnlyList<SemanticSourceLayerKindDto> SupportedLayers);

public sealed record GameModuleFactDto(
    string FactId,
    string FieldKey,
    string Label,
    SemanticScalarValueDto Value,
    string? Unit,
    SemanticConfidenceDto Confidence,
    string ProviderId,
    IReadOnlyList<SemanticRecordRefDto> Evidence);

public sealed record GameModuleRecordDto(
    string RecordId,
    string RecordKind,
    string? GroupId,
    string? ParentRecordId,
    int SortOrder,
    string Title,
    string Summary,
    SemanticRecordRefDto? Target,
    SemanticCoverageStateDto Coverage,
    SemanticConfidenceDto Confidence,
    IReadOnlyList<GameModuleFactDto> Facts);

public sealed record ReadGameModuleCapabilitiesRequest(SemanticExploreScopeDto Scope);

public sealed record ReadGameModuleCapabilitiesResponse(
    SemanticProjectRevisionDto Revision,
    IReadOnlyList<SemanticSourceSnapshotDto> Snapshots,
    IReadOnlyList<GameModuleCapabilityDto> Capabilities);

public sealed record QueryGameModuleRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    GameModuleDto Module,
    SemanticSourceLayerKindDto Layer,
    int Limit,
    string? Cursor = null);

public sealed record QueryGameModuleResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    SemanticSourceSnapshotDto Snapshot,
    GameModuleCapabilityDto Capability,
    int TotalRecordCount,
    IReadOnlyList<GameModuleRecordDto> Records,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    string? NextCursor);
