// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.ChangeSets;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.FashionCatalog;
using KM.Api.GameDump;
using KM.Api.GameModules;
using KM.Api.GuidedDesign;
using KM.Api.Output;
using KM.Api.Projects;
using KM.Api.Research;
using KM.Api.RuntimeSettings;
using KM.Api.Semantics;
using KM.Api.SemanticMerging;
using KM.Api.SvCache;
using KM.Api.SwShCache;
using KM.Api.TrainerPools;
using KM.Api.ZaCache;
using KM.Api.Workflows;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KM.Api.Bridge;

/// <summary>
/// Shared JSON settings for the local UI/backend bridge wire contract.
/// </summary>
public static class BridgeJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Bridge enums cross as readable strings instead of numeric enum values.
        options.Converters.Add(new JsonStringEnumConverter<ApiDiagnosticSeverity>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ChangePlanOutputModeDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ChangeSetOperationStorageKindDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ChangeSetSourceBindingKindDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ChangeSetMutationKindDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ChangeSetConflictKindDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ChangeSetOperationMaterializationStateDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<OutputApplyOutcomeDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<OutputTransactionPhaseDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<OutputRecoveryDispositionDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<OutputIntegrityClassificationDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<OutputCleanupDispositionDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<OutputCheckpointCoverageDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<GameplaySettingsStateDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GameplaySettingsApplyOutcomeDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsPackageStateDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsPackageOperationDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsInstallationTargetDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsPackageTargetOperationDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsExecutableInputSourceDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsExecutableCompatibilityDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsPackageReadDependencyRoleDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsExecutableCompositionStrategyDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<InGameSettingsPackageApplyOutcomeDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<EncounterCompatibilityPolicyDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<FashionCatalogFileDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<ProjectRelocationDocumentStatusDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<FileLayerDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<GameDumpCategoryKindDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<GameDumpFormatDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectGameDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectHealthStateDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectFileGraphEntryStateDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectFileLayerDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectPathRoleDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectPathStatusDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<SvCacheModeDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<SwShCacheModeDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ZaCacheModeDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<WorkflowAvailabilityDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<TrainerPoolKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticGameFamilyDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticSourceLayerKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticCoverageStateDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticConfidenceDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticFeatureDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticValueKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticDifferenceKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticReferenceDirectionDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticImpactSeverityDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticImpactActionabilityDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticOwnershipNodeKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticOwnershipEdgeKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticChangeFormatDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<BalanceLabStudyDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<BalanceLabFindingSeverityDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GameModuleDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GameModuleMaturityDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GuidedDesignFeatureDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GuidedDesignProposalKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GuidedDesignFieldSelectionModeDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GuidedDesignRoundingDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GuidedDesignTrainerArchetypeDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GuidedDesignFindingSeverityDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<GuidedDesignCanonicalExportKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticMergeFeatureDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticMergeConflictKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticMergeConflictChoiceDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticMergeRowStateDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticMergeFallbackKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<SemanticMergeFallbackTargetDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<KmRecipeCompatibilityStateDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<ResearchFeatureDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<ResearchExtensionKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<ResearchFileDifferenceKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<ResearchRangeCoverageDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<ResearchAnnotationTargetKindDto>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<ResearchAnnotationMutationKindDto>(JsonNamingPolicy.CamelCase, false));

        return options;
    }
}
