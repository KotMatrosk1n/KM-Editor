/* SPDX-License-Identifier: GPL-3.0-only */

export function getApplyMessage(targetRelativePath: string, domain: string | undefined) {
  if (targetRelativePath.includes('/message/')) {
    return 'Applied Text change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/trainer/')) {
    return 'Applied Trainers change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.giftPokemon') {
    return 'Applied Gift Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.tradePokemon') {
    return 'Applied Trade Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.staticEncounters') {
    return 'Applied Static Encounter change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.rentalPokemon') {
    return 'Applied Rental Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.dynamaxAdventures') {
    return 'Applied Dynamax Adventures change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/shop/')) {
    return 'Applied Shops change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.raidBattles') {
    return 'Applied Raid Battles change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.raidRewards') {
    return 'Applied Raid Rewards change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.moves') {
    return 'Applied Moves change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.pokemon') {
    return 'Applied Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.royalCandy') {
    return 'Applied Royal Candy change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.bagHook') {
    return 'Installed Bag Hook V2 to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.catchCap') {
    return 'Applied Catch Cap Editor changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.hyperTraining') {
    return 'Applied Hyper Training changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.fashionUnlock') {
    return 'Applied Fashion Unlock changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.gymUniformRemoval') {
    return 'Applied Gym Uniform Removal changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.hyperspaceBypass') {
    return 'Applied Hyperspace Bypass changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.ivScreen') {
    return 'Applied IV Screen changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.startingItems') {
    return 'Applied Starting Items grants to Bag Hook slots 2-20 in the configured LayeredFS output root.';
  }

  if (domain === 'workflow.exefsPatches') {
    return 'Applied ExeFS patch to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/archive/field/resident/')) {
    return 'Applied Wild Encounters change plan to the configured LayeredFS output root.';
  }

  return 'Applied Items change plan to the configured LayeredFS output root.';
}

export function getValidationMessage(domain: string | undefined) {
  switch (domain) {
    case 'workflow.text':
      return 'Pending text change is valid.';
    case 'workflow.trainers':
      return 'Pending trainer change is valid.';
    case 'workflow.giftPokemon':
      return 'Pending gift Pokemon change is valid.';
    case 'workflow.tradePokemon':
      return 'Pending trade Pokemon change is valid.';
    case 'workflow.staticEncounters':
      return 'Pending static encounter change is valid.';
    case 'workflow.rentalPokemon':
      return 'Pending rental Pokemon change is valid.';
    case 'workflow.dynamaxAdventures':
      return 'Pending Dynamax Adventure change is valid.';
    case 'workflow.shops':
      return 'Pending shop change is valid.';
    case 'workflow.encounters':
      return 'Pending encounter change is valid.';
    case 'workflow.raidBattles':
      return 'Pending raid battle change is valid.';
    case 'workflow.raidRewards':
      return 'Pending raid reward change is valid.';
    case 'workflow.moves':
      return 'Pending move change is valid.';
    case 'workflow.bagHook':
      return 'Pending Bag Hook install is valid for change-plan review.';
    case 'workflow.catchCap':
      return 'Pending Catch Cap Editor values are valid for change-plan review.';
    case 'workflow.hyperTraining':
      return 'Pending Hyper Training change is valid for change-plan review.';
    case 'workflow.fashionUnlock':
      return 'Pending Fashion Unlock change is valid for change-plan review.';
    case 'workflow.gymUniformRemoval':
      return 'Pending Gym Uniform Removal change is valid for change-plan review.';
    case 'workflow.hyperspaceBypass':
      return 'Pending Hyperspace Bypass change is valid for change-plan review.';
    case 'workflow.ivScreen':
      return 'Pending IV Screen change is valid for change-plan review.';
    case 'workflow.royalCandy':
      return 'Pending Royal Candy workflow is valid.';
    case 'workflow.startingItems':
      return 'Pending Starting Items grants are valid for change-plan review.';
    case 'workflow.exefsPatches':
      return 'Pending ExeFS patch is valid.';
    default:
      return 'Pending item change is valid.';
  }
}
