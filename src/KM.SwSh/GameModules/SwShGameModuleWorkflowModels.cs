// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.DynamaxAdventures;
using KM.SwSh.ExeFs;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Placement;
using KM.SwSh.Raids;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;

namespace KM.SwSh.GameModules;

public sealed record SwShRewardEcosystemWorkflowSources(
    SwShNpcItemGiftWorkflow NpcItemGifts,
    SwShRaidRewardsWorkflow RaidRewards,
    SwShRaidRewardsWorkflow RaidBonusRewards,
    SwShShopsWorkflow Shops,
    SwShPlacementWorkflow Placement);

public sealed record SwShBattleCafeRewardEntry(
    int RowIndex,
    int ItemId,
    string ItemName,
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record SwShBattleCafeRewardSource(
    IReadOnlyList<SwShBattleCafeRewardEntry> Rewards,
    string? UnavailableReasonCode = null);

public sealed record SwShTrainerTypeEventAssignment(
    int TrainerTypeId,
    string EventName,
    bool IsLayered);

public sealed record SwShTrainerTypeEventAssignmentSource(
    IReadOnlyList<SwShTrainerTypeEventAssignment> Assignments,
    string? UnavailableReasonCode = null);

public sealed record SwShGameModuleWorkflowBatch(
    SwShRewardEcosystemWorkflowSources RewardEcosystem,
    SwShExeFsPatchWorkflow ExeFsCompatibility,
    SwShDynamaxAdventuresWorkflow DynamaxAdventures,
    SwShRentalPokemonWorkflow RentalPokemon,
    SwShRoyalCandyWorkflow RoyalCandyProgression,
    SwShBattleCafeRewardSource BattleCafeRewards,
    SwShTrainerTypeEventAssignmentSource EventAssignments);
