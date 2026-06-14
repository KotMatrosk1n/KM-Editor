// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.Randomizer;

public sealed record SwShRandomizerOptions(
    bool RandomizePokemonStats,
    bool ShufflePokemonStats,
    bool StatHp,
    bool StatAttack,
    bool StatDefense,
    bool StatSpecialAttack,
    bool StatSpecialDefense,
    bool StatSpeed,
    bool RandomizePokemonTypes,
    bool TypePrimary,
    bool TypeSecondary,
    bool AllowSameType,
    bool RandomizePokemonAbilities,
    bool Ability1,
    bool Ability2,
    bool HiddenAbility,
    bool RandomizePokemonHeldItems,
    bool RandomizePokemonCatchRates,
    bool RandomizePokemonLearnsets,
    bool LearnsetStabFirst,
    bool LearnsetExpandTo25,
    bool LearnsetBanFixedDamageMoves,
    bool LearnsetRequireDamagingMove,
    bool RandomizePokemonCompatibility,
    bool CompatibilityMachines,
    bool CompatibilityRecords,
    bool CompatibilityTutors,
    bool RandomizePokemonEvolutions,
    bool RandomizeWildEncounters,
    bool RandomizeStaticEncounters,
    bool RandomizeGiftEncounters,
    bool RandomizeRaidRewards,
    bool RandomizeRaidBonusRewards)
{
    public static SwShRandomizerOptions Empty { get; } = new(
        RandomizePokemonStats: false,
        ShufflePokemonStats: true,
        StatHp: true,
        StatAttack: true,
        StatDefense: true,
        StatSpecialAttack: true,
        StatSpecialDefense: true,
        StatSpeed: true,
        RandomizePokemonTypes: false,
        TypePrimary: true,
        TypeSecondary: true,
        AllowSameType: false,
        RandomizePokemonAbilities: false,
        Ability1: true,
        Ability2: true,
        HiddenAbility: true,
        RandomizePokemonHeldItems: false,
        RandomizePokemonCatchRates: false,
        RandomizePokemonLearnsets: false,
        LearnsetStabFirst: true,
        LearnsetExpandTo25: false,
        LearnsetBanFixedDamageMoves: true,
        LearnsetRequireDamagingMove: true,
        RandomizePokemonCompatibility: false,
        CompatibilityMachines: true,
        CompatibilityRecords: true,
        CompatibilityTutors: true,
        RandomizePokemonEvolutions: false,
        RandomizeWildEncounters: false,
        RandomizeStaticEncounters: false,
        RandomizeGiftEncounters: false,
        RandomizeRaidRewards: false,
        RandomizeRaidBonusRewards: false);

    public bool HasAnySelection =>
        RandomizePokemonStats
        || RandomizePokemonTypes
        || RandomizePokemonAbilities
        || RandomizePokemonHeldItems
        || RandomizePokemonCatchRates
        || RandomizePokemonLearnsets
        || RandomizePokemonCompatibility
        || RandomizePokemonEvolutions
        || RandomizeWildEncounters
        || RandomizeStaticEncounters
        || RandomizeGiftEncounters
        || RandomizeRaidRewards
        || RandomizeRaidBonusRewards;
}

public sealed record SwShRandomizerConfig(
    string UserSeed,
    SwShRandomizerOptions Options,
    string? RollSeed = null,
    string? OutputHash = null);

public sealed record SwShRandomizerImportResult(
    SwShRandomizerConfig? Config,
    string? Seed,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Config is not null
        && Seed is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed record SwShRandomizerApplyResult(
    SwShRandomizerConfig Config,
    string Seed,
    ApplyResult ApplyResult);

internal sealed record SwShRandomizerPreviewResult(
    SwShRandomizerConfig Config,
    string Seed,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    IReadOnlyList<SwShRandomizerPreviewDomain> Domains);

internal sealed record SwShRandomizerPreviewDomain(
    string Label,
    IReadOnlyList<SwShRandomizerPreviewEdit> Edits);

internal sealed record SwShRandomizerPreviewEdit(
    string Domain,
    string RecordId,
    string Field,
    string NewValue,
    string Summary);
