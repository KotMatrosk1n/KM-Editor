// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;

namespace KM.ZA.Encounters;

public enum ZaEncounterCompatibilityPolicy
{
    PreserveForEveryReplacement,
    FilterByVerifiedPair,
}

public sealed record ZaEncounterCompatibilityPair(
    int SpeciesId,
    int Form,
    bool ObservedInBasePlacement,
    bool VerifiedExtension);

public sealed record ZaEncounterCompatibilityRule(
    string RuleId,
    string DisplayName,
    ZaEncounterCompatibilityPolicy Policy,
    IReadOnlyList<int> ActionIds,
    bool HasTagSelector,
    IReadOnlyList<ZaEncounterCompatibilityPair> CompatiblePairs);

public sealed record ZaEncounterCompatibilityWorkflow(
    IReadOnlyList<ZaEncounterCompatibilityRule> Rules,
    IReadOnlyList<ZaEncounterCompatibilityPair> CityBehaviorPairs,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
