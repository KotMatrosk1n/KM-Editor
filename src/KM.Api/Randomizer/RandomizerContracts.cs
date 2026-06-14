// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.Randomizer;

public sealed record RandomizerOptionsDto(
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
    bool RandomizeRaidBonusRewards);

public sealed record RandomizerConfigDto(
    string UserSeed,
    RandomizerOptionsDto Options,
    string? RollSeed,
    string? OutputHash);

public sealed record ImportRandomizerSeedRequest(string Seed);

public sealed record ImportRandomizerSeedResponse(
    RandomizerConfigDto? Config,
    string? Seed,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ApplyRandomizerRequest(
    ProjectPathsDto Paths,
    RandomizerConfigDto Config);

public sealed record ApplyRandomizerResponse(
    string Seed,
    ApplyResultDto ApplyResult);

public sealed record RestoreRandomizerRequest(
    ProjectPathsDto Paths);

public sealed record RestoreRandomizerResponse(
    ApplyResultDto ApplyResult);
