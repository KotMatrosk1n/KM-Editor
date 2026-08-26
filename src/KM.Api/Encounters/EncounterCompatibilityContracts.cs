// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;

namespace KM.Api.Encounters;

public enum EncounterCompatibilityPolicyDto
{
    PreserveForEveryReplacement,
    FilterByVerifiedPair,
}

public sealed record EncounterCompatibilityPairDto(
    int SpeciesId,
    int Form,
    bool ObservedInBasePlacement,
    bool VerifiedExtension);

public sealed record EncounterCompatibilityRuleDto(
    string RuleId,
    string DisplayName,
    EncounterCompatibilityPolicyDto Policy,
    IReadOnlyList<int> ActionIds,
    bool HasTagSelector,
    IReadOnlyList<EncounterCompatibilityPairDto> CompatiblePairs);

public sealed record EncounterCompatibilityWorkflowDto(
    IReadOnlyList<EncounterCompatibilityRuleDto> Rules,
    IReadOnlyList<EncounterCompatibilityPairDto> CityBehaviorPairs,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
