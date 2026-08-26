// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.DynamaxAdventures;
using KM.Api.ExeFs;
using KM.Api.NpcItemGift;
using KM.Api.Placement;
using KM.Api.Raids;
using KM.Api.Rentals;
using KM.Api.RoyalCandy;
using KM.Api.Shops;

namespace KM.Api.GameModules;

public sealed record SwordShieldRewardEcosystemSourceDto(
    NpcItemGiftWorkflowDto NpcItemGifts,
    RaidRewardsWorkflowDto RaidRewards,
    RaidRewardsWorkflowDto RaidBonusRewards,
    ShopsWorkflowDto Shops,
    PlacementWorkflowDto Placement);

public sealed record SwordShieldBattleCafeRewardEntryDto(
    int RowIndex,
    int ItemId,
    string ItemName,
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record SwordShieldBattleCafeRewardSourceDto(
    IReadOnlyList<SwordShieldBattleCafeRewardEntryDto> Rewards,
    string? UnavailableReasonCode);

public sealed record SwordShieldTrainerTypeEventAssignmentDto(
    int TrainerTypeId,
    string EventName,
    bool IsLayered);

public sealed record SwordShieldTrainerTypeEventAssignmentSourceDto(
    IReadOnlyList<SwordShieldTrainerTypeEventAssignmentDto> Assignments,
    string? UnavailableReasonCode);

public sealed record SwordShieldGameModuleSourceBatchDto(
    SwordShieldRewardEcosystemSourceDto RewardEcosystem,
    ExeFsPatchWorkflowDto ExeFsCompatibility,
    DynamaxAdventuresWorkflowDto DynamaxAdventures,
    RentalPokemonWorkflowDto RentalPokemon,
    RoyalCandyWorkflowDto RoyalCandyProgression,
    SwordShieldBattleCafeRewardSourceDto BattleCafeRewards,
    SwordShieldTrainerTypeEventAssignmentSourceDto EventAssignments);
