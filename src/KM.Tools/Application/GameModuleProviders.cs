// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Diagnostics;
using KM.Api.DynamaxAdventures;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.GameModules;
using KM.Api.Moves;
using KM.Api.NpcItemGift;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Rentals;
using KM.Api.RoyalCandy;
using KM.Api.ScriptedBosses;
using KM.Api.Semantics;
using KM.Api.Shops;
using KM.Api.Trainers;
using KM.Api.TrainerPools;
using KM.Api.Workflows;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.GameModules;

namespace KM.Tools.Application;

internal sealed record GameModuleData(
    GameModuleCapabilityDto Capability,
    IReadOnlyList<GameModuleRecordDto> Records,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    bool Cacheable);

internal static class GameModuleSizingLimits
{
    internal const int ProvisionMultiplier = 4;
    internal const int CacheCeilingMultiplier = 2;
    internal const long ExpectedModuleSizeBytes = 192L * 1024L * 1024L;
    internal const long ModuleProvisionSizeBytes = checked(
        ExpectedModuleSizeBytes * ProvisionMultiplier);
    internal const long ModuleCacheCeilingBytes = checked(
        ModuleProvisionSizeBytes * CacheCeilingMultiplier);
}

internal static class GameModuleProviders
{
    private const int RecordSchemaVersion = 1;
    private const long MaximumProjectedBytes = GameModuleSizingLimits.ModuleCacheCeilingBytes;
    private sealed record PlayerDamageAggregate(int RowCount, int? LaunchCount)
    {
        public static PlayerDamageAggregate Empty { get; } = new(0, 0);
    }

    private sealed record ScriptedMoveEvidence(
        MoveRecordDto Move,
        PlayerDamageAggregate AllPlayerDamage,
        IReadOnlyDictionary<int, PlayerDamageAggregate> ByRuntimeMoveId);

    private sealed record BattleCafeBranch(
        string Key,
        string Title,
        string TrainerTypeIds,
        int OwnerCount,
        Func<SwordShieldBattleCafeRewardEntryDto, int> Percentage);

    private static readonly IReadOnlyList<SemanticSourceLayerKindDto> EffectiveLayer =
        [SemanticSourceLayerKindDto.Layered];

    public static IReadOnlyList<GameModuleCapabilityDto> Capabilities(SemanticGameFamilyDto family)
    {
        return family switch
        {
            SemanticGameFamilyDto.SwordShield =>
            [
                Available(
                    GameModuleDto.SwordShieldRewardEcosystem,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "trainer-payout-and-runtime-acquisition-order-unavailable"),
                Available(
                    GameModuleDto.SwordShieldExeFsCompatibility,
                    family,
                    GameModuleMaturityDto.Product,
                    "patch-interaction-and-unlisted-build-coverage-unavailable"),
                Available(
                    GameModuleDto.SwordShieldDynamaxAdventures,
                    family,
                    GameModuleMaturityDto.Product,
                    "runtime-route-generation-and-unlisted-build-coverage-unavailable"),
                Available(
                    GameModuleDto.SwordShieldRoyalCandyProgression,
                    family,
                    GameModuleMaturityDto.Product,
                    "runtime-progression-evaluation-unavailable"),
                Available(
                    GameModuleDto.SwordShieldBattleCafeRewards,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "runtime-scene-availability-unavailable"),
                Available(
                    GameModuleDto.SwordShieldEventAssignments,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "scene-script-assignments-and-runtime-audio-resolution-unavailable"),
            ],
            SemanticGameFamilyDto.ScarletViolet =>
            [
                Available(
                    GameModuleDto.ScarletVioletTeraRaidAnalysis,
                    family,
                    GameModuleMaturityDto.Product,
                    "progression-unlock-and-rotation-coverage-unavailable"),
                Available(
                    GameModuleDto.ScarletVioletPackedLooseComparison,
                    family,
                    GameModuleMaturityDto.Product,
                    "source-content-decoding-outside-scope"),
                Available(
                    GameModuleDto.ScarletVioletEventDataComparison,
                    family,
                    GameModuleMaturityDto.Product,
                    "unmapped-event-fields-remain-opaque"),
                Available(
                    GameModuleDto.ScarletVioletScenePlacementEditing,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "coordinates-rotations-naming-and-unowned-scene-fields-excluded"),
                Available(
                    GameModuleDto.ScarletVioletTypeEffectivenessState,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "stellar-and-runtime-effect-resolution-unavailable"),
                Unavailable(
                    GameModuleDto.ScarletVioletStellarBehavior,
                    family,
                    GameModuleMaturityDto.ResearchGated,
                    "research-evidence-required"),
            ],
            SemanticGameFamilyDto.LegendsZA =>
            [
                Available(
                    GameModuleDto.LegendsZaScriptedBossTimeline,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "runtime-execution-order-unavailable"),
                Available(
                    GameModuleDto.LegendsZaTrainerArchetypes,
                    family,
                    GameModuleMaturityDto.Product,
                    "class-and-presentation-semantics-research-gated"),
                Available(
                    GameModuleDto.LegendsZaWildSpawnExplorer,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "placement-and-runtime-reachability-coverage-unavailable"),
                Available(
                    GameModuleDto.LegendsZaEncounterCompatibility,
                    family,
                    GameModuleMaturityDto.Product,
                    "runtime-city-behavior-and-unlisted-attachment-coverage-unavailable"),
                Available(
                    GameModuleDto.LegendsZaAlphaMoveDistribution,
                    family,
                    GameModuleMaturityDto.Product,
                    "mapping-addition-and-runtime-selection-coverage-unavailable"),
                Available(
                    GameModuleDto.LegendsZaDexLayoutPlanning,
                    family,
                    GameModuleMaturityDto.Product,
                    "movement-proposals-and-per-species-mega-membership-unavailable"),
                Available(
                    GameModuleDto.LegendsZaMoveVariantComparison,
                    family,
                    GameModuleMaturityDto.Product,
                    "variant-consumer-coverage-unavailable"),
                Available(
                    GameModuleDto.LegendsZaTrainerPoolSwitching,
                    family,
                    GameModuleMaturityDto.Product,
                    "pool-resizing-and-runtime-selection-coverage-unavailable"),
                Available(
                    GameModuleDto.LegendsZaTypeEffectivenessState,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "edit-proposals-and-runtime-effect-resolution-unavailable"),
            ],
            _ => throw new SemanticExploreValidationException(
                "The selected game-specific module family is unsupported.",
                SemanticExploreFailureKind.Unsupported),
        };
    }

    public static GameModuleData BuildTeraRaidAnalysis(TeraRaidsWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.ScarletVioletTeraRaidAnalysis;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var rewardTableCount = checked(
            (long)workflow.FixedRewardTables.Count + workflow.LotteryRewardTables.Count);
        var rewardCount = checked(
            workflow.FixedRewardTables.Sum(table => (long)table.Rewards.Count)
            + workflow.LotteryRewardTables.Sum(table => (long)table.Rewards.Count));
        EnsureProjectionCounts(
            checked(workflow.Raids.Count + rewardTableCount + rewardCount),
            checked(workflow.Raids.Count * 18L + rewardTableCount * 3L + rewardCount * 7L));
        var records = new BoundedRecordCollection();
        foreach (var raid in workflow.Raids
                     .OrderBy(raid => raid.Region, StringComparer.Ordinal)
                     .ThenBy(raid => raid.StarRank)
                     .ThenBy(raid => raid.EntryIndex))
        {
            var target = Record(
                SemanticGameFamilyDto.ScarletViolet,
                "workflow.teraRaids",
                "tera-raid",
                raid.RecordId);
            var recordId = RecordId(providerId, "raid", raid.RecordId);
            records.Add(CreateRecord(
                recordId,
                "teraRaid",
                StableGroup("region", raid.Region),
                parentRecordId: null,
                records.Count,
                $"{SafePresentation(raid.Species, 192)} - {SafePresentation(raid.StarLabel, 48)}",
                "Verified raid boss and reward-table references. No unlock or rotation order is inferred.",
                target,
                capability,
                [
                    TextFact(providerId, recordId, "region", "Region", raid.Region, evidence: target),
                    NullableSignedFact(providerId, recordId, "starRank", "Star rank", raid.StarRank, "stars", target),
                    SignedFact(providerId, recordId, "deliveryGroupId", "Delivery group", raid.DeliveryGroupId, evidence: target),
                    SignedFact(providerId, recordId, "difficulty", "Stored difficulty", raid.Difficulty, evidence: target),
                    SignedFact(providerId, recordId, "spawnRate", "Spawn rate", raid.SpawnRate, evidence: target),
                    SignedFact(providerId, recordId, "captureRate", "Capture rate", raid.CaptureRate, evidence: target),
                    SignedFact(providerId, recordId, "speciesId", "Species ID", raid.SpeciesId, evidence: target),
                    SignedFact(providerId, recordId, "form", "Form", raid.Form, evidence: target),
                    SignedFact(providerId, recordId, "level", "Level", raid.Level, "level", target),
                    EnumFact(providerId, recordId, "teraType", "Tera type", raid.TeraTypeLabel, target),
                    SignedFact(providerId, recordId, "hpMultiplier", "HP multiplier", raid.HpMultiplier, evidence: target),
                    SignedFact(providerId, recordId, "shieldTriggerHp", "Shield trigger HP", raid.ShieldTriggerHp, "percent", target),
                    SignedFact(providerId, recordId, "shieldTriggerTime", "Shield trigger time", raid.ShieldTriggerTime, evidence: target),
                    SignedFact(providerId, recordId, "doubleActionHp", "Double action HP", raid.DoubleActionHp, "percent", target),
                    SignedFact(providerId, recordId, "doubleActionTime", "Double action time", raid.DoubleActionTime, evidence: target),
                    SignedFact(providerId, recordId, "doubleActionRate", "Double action rate", raid.DoubleActionRate, evidence: target),
                    TextFact(providerId, recordId, "fixedRewardTable", "Fixed reward table", raid.FixedRewardTableHash, evidence: target),
                    TextFact(providerId, recordId, "lotteryRewardTable", "Lottery reward table", raid.LotteryRewardTableHash, evidence: target),
                ]));
        }

        AddTeraRewardRecords(workflow.FixedRewardTables, providerId, capability, records);
        AddTeraRewardRecords(workflow.LotteryRewardTables, providerId, capability, records);
        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildScriptedBossTimeline(
        EncountersWorkflowDto encounters,
        MovesWorkflowDto moves)
    {
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(moves);
        var module = GameModuleDto.LegendsZaScriptedBossTimeline;
        var unavailable = WorkflowUnavailable(
            module,
            encounters.Summary,
            encounters.Diagnostics.Concat(moves.Diagnostics).ToArray());
        if (unavailable is not null)
        {
            return unavailable;
        }

        unavailable = WorkflowUnavailable(module, moves.Summary, moves.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var phaseCount = encounters.ScriptedBosses.Sum(profile => (long)profile.PhaseModel.Phases.Count);
        var actionCount = encounters.ScriptedBosses.Sum(profile => (long)profile.Actions.Count);
        EnsureProjectionCounts(
            checked(encounters.ScriptedBosses.Count + phaseCount + actionCount),
            checked(encounters.ScriptedBosses.Count * 7L + phaseCount * 6L + actionCount * 12L));
        var moveEvidence = CreateScriptedMoveEvidence(moves.Moves);
        var records = new BoundedRecordCollection();
        foreach (var profile in encounters.ScriptedBosses.OrderBy(profile => profile.Key, StringComparer.Ordinal))
        {
            var profileId = RecordId(providerId, "profile", profile.Key);
            records.Add(CreateRecord(
                profileId,
                "scriptedBossProfile",
                StableGroup("profile", profile.Key),
                parentRecordId: null,
                records.Count,
                profile.Name,
                "Verified phase and action membership. Record order is not runtime execution order.",
                target: null,
                capability,
                [
                    SignedFact(providerId, profileId, "speciesId", "Species ID", profile.SpeciesId),
                    SignedFact(providerId, profileId, "form", "Form", profile.Form),
                    EnumFact(providerId, profileId, "scope", "Scope", profile.Scope),
                    EnumFact(providerId, profileId, "phaseModelState", "Phase model state", profile.PhaseModel.State),
                    EnumFact(providerId, profileId, "phaseModelKind", "Phase model kind", profile.PhaseModel.Kind),
                    DerivedSignedFact(providerId, profileId, "phaseCount", "Phase count", profile.PhaseModel.Phases.Count),
                    DerivedSignedFact(providerId, profileId, "actionCount", "Action count", profile.Actions.Count),
                ]));

            foreach (var phase in profile.PhaseModel.Phases
                         .OrderBy(phase => phase.Stage)
                         .ThenBy(phase => phase.HpPhase)
                         .ThenBy(phase => phase.Key, StringComparer.Ordinal))
            {
                var phaseId = RecordId(
                    providerId,
                    "phase",
                    CompositeSourceIdentity(profile.Key, phase.Key));
                records.Add(CreateRecord(
                    phaseId,
                    "scriptedBossPhase",
                    StableGroup("profile", profile.Key),
                    profileId,
                    records.Count,
                    phase.StageName,
                    "Verified Battle Stage and HP Phase boundary.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, phaseId, "battleStage", "Battle Stage", phase.Stage),
                        SignedFact(providerId, phaseId, "hpPhase", "HP Phase", phase.HpPhase),
                        SignedFact(providerId, phaseId, "minimumHpPercent", "Minimum HP", phase.MinimumHpPercent, "percent"),
                        SignedFact(providerId, phaseId, "maximumHpPercent", "Maximum HP", phase.MaximumHpPercent, "percent"),
                        SignedFact(providerId, phaseId, "speciesId", "Species ID", phase.SpeciesId),
                        SignedFact(providerId, phaseId, "form", "Form", phase.Form),
                    ]));
            }

            foreach (var action in profile.Actions.OrderBy(action => action.Key, StringComparer.Ordinal))
            {
                AddScriptedBossAction(
                    profile,
                    action,
                    moveEvidence,
                    providerId,
                    capability,
                    profileId,
                    records);
            }
        }

        return Complete(
            module,
            encounters.Summary,
            encounters.Diagnostics.Concat(moves.Diagnostics).ToArray(),
            records);
    }

    public static GameModuleData BuildTrainerArchetypes(TrainersWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.LegendsZaTrainerArchetypes;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var trainerCount = workflow.Trainers.LongCount(
            trainer => trainer.Team.Any(member => member.SpeciesId > 0));
        var partyCount = workflow.Trainers.Sum(
            trainer => trainer.Team.LongCount(member => member.SpeciesId > 0));
        EnsureProjectionCounts(
            checked(trainerCount + partyCount),
            checked(trainerCount * 4L + partyCount * 10L));
        var records = new BoundedRecordCollection();
        foreach (var trainer in workflow.Trainers
                     .Where(trainer => trainer.Team.Any(member => member.SpeciesId > 0))
                     .OrderBy(trainer => trainer.TrainerId))
        {
            var trainerTarget = Record(
                SemanticGameFamilyDto.LegendsZA,
                "workflow.trainers",
                "trainer",
                trainer.TrainerId.ToString(CultureInfo.InvariantCulture));
            var trainerRecordId = RecordId(providerId, "trainer", trainer.TrainerId.ToString(CultureInfo.InvariantCulture));
            records.Add(CreateRecord(
                trainerRecordId,
                "trainer",
                StableGroup("trainer", trainer.TrainerId.ToString(CultureInfo.InvariantCulture)),
                parentRecordId: null,
                records.Count,
                trainer.Name,
                "Verified trainer and party facts. Class and presentation semantics are not inferred.",
                trainerTarget,
                capability,
                [
                    DerivedSignedFact(
                        providerId,
                        trainerRecordId,
                        "partySize",
                        "Party size",
                        trainer.Team.Count(member => member.SpeciesId > 0),
                        evidence: trainerTarget),
                    DerivedSignedFact(providerId, trainerRecordId, "aiFlags", "AI flags", trainer.AiFlags, evidence: trainerTarget),
                    NullableSignedFact(providerId, trainerRecordId, "zaRank", "Z-A rank", trainer.ZaRank, null, trainerTarget),
                    NullableBooleanFact(providerId, trainerRecordId, "megaEvolution", "Mega Evolution", trainer.ZaMegaEvolution, trainerTarget),
                ]));

            foreach (var member in trainer.Team
                         .Where(member => member.SpeciesId > 0)
                         .OrderBy(member => member.Slot))
            {
                var memberTarget = trainerTarget with
                {
                    SubrecordId = $"party-slot:{member.Slot.ToString(CultureInfo.InvariantCulture)}",
                };
                var memberRecordId = RecordId(
                    providerId,
                    "party",
                    $"{trainer.TrainerId.ToString(CultureInfo.InvariantCulture)}:{member.Slot.ToString(CultureInfo.InvariantCulture)}");
                var archetype = ClassifyArchetype(member.Evs);
                records.Add(CreateRecord(
                    memberRecordId,
                    "trainerPartyMember",
                    StableGroup("trainer", trainer.TrainerId.ToString(CultureInfo.InvariantCulture)),
                    trainerRecordId,
                    records.Count,
                    $"{SafePresentation(trainer.Name, 192)} - party slot {checked(member.Slot + 1).ToString(CultureInfo.InvariantCulture)}",
                    "Archetype is an exact EV-vector classification; custom does not infer a role.",
                    memberTarget,
                    capability,
                    [
                        SignedFact(providerId, memberRecordId, "speciesId", "Species ID", member.SpeciesId, evidence: memberTarget),
                        SignedFact(providerId, memberRecordId, "form", "Form", member.Form, evidence: memberTarget),
                        SignedFact(providerId, memberRecordId, "level", "Level", member.Level, "level", memberTarget),
                        DerivedEnumFact(providerId, memberRecordId, "archetype", "Archetype", archetype, memberTarget),
                        SignedFact(providerId, memberRecordId, "evHp", "HP EV", member.Evs.HP, "points", memberTarget),
                        SignedFact(providerId, memberRecordId, "evAttack", "Attack EV", member.Evs.Attack, "points", memberTarget),
                        SignedFact(providerId, memberRecordId, "evDefense", "Defense EV", member.Evs.Defense, "points", memberTarget),
                        SignedFact(providerId, memberRecordId, "evSpecialAttack", "Sp. Atk EV", member.Evs.SpecialAttack, "points", memberTarget),
                        SignedFact(providerId, memberRecordId, "evSpecialDefense", "Sp. Def EV", member.Evs.SpecialDefense, "points", memberTarget),
                        SignedFact(providerId, memberRecordId, "evSpeed", "Speed EV", member.Evs.Speed, "points", memberTarget),
                    ]));
            }
        }

        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildWildSpawnExplorer(EncountersWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.LegendsZaWildSpawnExplorer;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var slotCount = workflow.Tables.Sum(table => (long)table.Slots.Count);
        EnsureProjectionCounts(
            checked(workflow.Tables.Count + slotCount),
            checked(workflow.Tables.Count * 8L + slotCount * 13L));
        var records = new BoundedRecordCollection();
        foreach (var table in workflow.Tables.OrderBy(table => table.TableId, StringComparer.Ordinal))
        {
            var tableTarget = Record(
                SemanticGameFamilyDto.LegendsZA,
                "workflow.encounters",
                "encounter-table",
                table.TableId);
            var tableRecordId = RecordId(providerId, "table", table.TableId);
            records.Add(CreateRecord(
                tableRecordId,
                "spawnTable",
                StableGroup("table", table.TableId),
                parentRecordId: null,
                records.Count,
                table.TableLabel ?? table.Location,
                table.TableDetails ?? table.LocationDetails ?? "Verified encounter population and activation facts.",
                tableTarget,
                capability,
                [
                    DerivedNullableTextFact(providerId, tableRecordId, "area", "Area", table.Area, tableTarget),
                    DerivedEnumFact(providerId, tableRecordId, "encounterType", "Encounter type", table.EncounterType, tableTarget),
                    DerivedNullableTextFact(providerId, tableRecordId, "spawnerCategory", "Spawner category", table.SpawnerCategory, tableTarget),
                    DerivedNullableBooleanFact(providerId, tableRecordId, "isPostgame", "Postgame", table.IsPostgame, tableTarget),
                    DerivedSignedFact(providerId, tableRecordId, "phaseConditionCount", "Phase condition count", table.PhaseConditions?.Count ?? 0, evidence: tableTarget),
                    DerivedSignedFact(providerId, tableRecordId, "slotCount", "Slot count", table.Slots.Count, evidence: tableTarget),
                    DerivedNullableTextFact(providerId, tableRecordId, "bossContext", "Boss context", table.BossBattleContextLabel, tableTarget),
                    DerivedNullableTextFact(providerId, tableRecordId, "bossWave", "Boss wave", table.BossBattleWaveLabel, tableTarget),
                ]));

            foreach (var slot in table.Slots.OrderBy(slot => slot.Slot))
            {
                var slotTarget = tableTarget with
                {
                    SubrecordId = $"slot:{slot.Slot.ToString(CultureInfo.InvariantCulture)}",
                };
                var slotRecordId = RecordId(
                    providerId,
                    "slot",
                    $"{table.TableId}:{slot.Slot.ToString(CultureInfo.InvariantCulture)}");
                var hasResolvedPokemon = !string.IsNullOrWhiteSpace(slot.EncounterRecordId);
                records.Add(CreateRecord(
                    slotRecordId,
                    "spawnSlot",
                    StableGroup("table", table.TableId),
                    tableRecordId,
                    records.Count,
                    slot.IsAlpha ? $"{SafePresentation(slot.Species, 224)} Alpha" : slot.Species,
                    hasResolvedPokemon
                        ? "Verified slot values. Runtime reachability and physical attachment are not inferred."
                        : "The spawner slot is verified, but its PokemonData reference is unresolved. Pokemon-dependent facts are unavailable.",
                    slotTarget,
                    capability,
                    [
                        SignedFact(providerId, slotRecordId, "slot", "Slot", checked(slot.Slot + 1), evidence: slotTarget),
                        hasResolvedPokemon
                            ? SignedFact(providerId, slotRecordId, "speciesId", "Species ID", slot.SpeciesId, evidence: slotTarget)
                            : NullFact(providerId, slotRecordId, "speciesId", "Species ID", unit: null, slotTarget),
                        hasResolvedPokemon
                            ? SignedFact(providerId, slotRecordId, "form", "Form", slot.Form, evidence: slotTarget)
                            : NullFact(providerId, slotRecordId, "form", "Form", unit: null, slotTarget),
                        hasResolvedPokemon
                            ? SignedFact(providerId, slotRecordId, "minimumLevel", "Minimum level", slot.LevelMin, "level", slotTarget)
                            : NullFact(providerId, slotRecordId, "minimumLevel", "Minimum level", "level", slotTarget),
                        hasResolvedPokemon
                            ? SignedFact(providerId, slotRecordId, "maximumLevel", "Maximum level", slot.LevelMax, "level", slotTarget)
                            : NullFact(providerId, slotRecordId, "maximumLevel", "Maximum level", "level", slotTarget),
                        SignedFact(providerId, slotRecordId, "weight", "Native weight", slot.Weight, "weight", slotTarget),
                        DerivedNullableBooleanFact(providerId, slotRecordId, "isAlpha", "Alpha", slot.IsAlpha, slotTarget),
                        NullableSignedFact(providerId, slotRecordId, "alphaChance", "Alpha chance", slot.AlphaChancePercent, "percent", slotTarget),
                        NullableSignedFact(providerId, slotRecordId, "alphaLevelBonus", "Alpha level bonus", slot.AlphaLevelBonus, "level", slotTarget),
                        NullableSignedFact(providerId, slotRecordId, "slotMaximum", "Slot maximum", slot.SlotMaxCount, "pokemon", slotTarget),
                        DerivedNullableSignedFact(providerId, slotRecordId, "appearanceMinimum", "Appearance minimum", slot.AppearanceMinCount, "pokemon", slotTarget),
                        DerivedNullableSignedFact(providerId, slotRecordId, "appearanceMaximum", "Appearance maximum", slot.AppearanceMaxCount, "pokemon", slotTarget),
                        DerivedNullableSignedFact(providerId, slotRecordId, "appearanceObjectCount", "Appearance object count", slot.AppearanceObjectCount, "objects", slotTarget),
                    ]));
            }
        }

        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildPackedLooseSourceComparison(
        SvPackedLooseSourceComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var module = GameModuleDto.ScarletVioletPackedLooseComparison;
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        if (comparison.Entries.Count > 500
            || comparison.DivergentDualLooseCount < 0
            || comparison.DivergentDualLooseCount
                != comparison.Entries.Count(entry =>
                    entry.DualLooseOutputState == SvPackedLooseDualOutputState.Divergent))
        {
            throw new SemanticExploreValidationException(
                "The packed and loose source comparison exceeds its bounded result contract.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        EnsureProjectionCounts(
            comparison.Entries.Count,
            checked(comparison.Entries.Count * 24L));
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var records = new BoundedRecordCollection();
        foreach (var entry in comparison.Entries
                     .OrderBy(entry => entry.VirtualIdentity, StringComparer.Ordinal))
        {
            var presentKinds = entry.Candidates
                .Where(candidate => candidate.IsPresent)
                .Select(candidate => candidate.Kind)
                .ToHashSet();
            var hasEffective = entry.EffectiveSource != SvPackedLooseEffectiveSource.None;
            var hasBaseArchive = presentKinds.Contains(SvPackedLooseSourceKind.BaseArchive);
            var hasBothLooseOutputs = presentKinds.Contains(
                    SvPackedLooseSourceKind.StandaloneLooseOutput)
                && presentKinds.Contains(SvPackedLooseSourceKind.ManagerLooseOutput);
            if (!identities.Add(entry.VirtualIdentity)
                || !IsPackedLooseVirtualIdentity(entry.VirtualIdentity)
                || entry.Candidates.Count != 5
                || !entry.Candidates.Select(candidate => candidate.Kind).ToHashSet().SetEquals(
                    Enum.GetValues<SvPackedLooseSourceKind>())
                || entry.Candidates.Count(candidate => candidate.IsEffective)
                    != (entry.EffectiveSource == SvPackedLooseEffectiveSource.None ? 0 : 1)
                || !IsPackedLooseEffectiveSourceConsistent(entry, presentKinds)
                || hasBothLooseOutputs
                    != (entry.DualLooseOutputState
                        is SvPackedLooseDualOutputState.Identical
                            or SvPackedLooseDualOutputState.Divergent)
                || entry.Candidates.Any(candidate =>
                    candidate.IsPresent != (candidate.ByteLength is not null)
                    || candidate.ByteLength is < 0
                    || candidate.IsEffective && !candidate.IsPresent
                    || candidate.IsEffective
                        != CandidateMatchesEffectiveSource(
                            candidate.Kind,
                            entry.EffectiveSource)
                    || !candidate.IsPresent
                        && (candidate.MatchesEffective is not null
                            || candidate.MatchesBaseArchive is not null)
                    || candidate.IsPresent
                        && hasEffective != (candidate.MatchesEffective is not null)
                    || candidate.IsPresent
                        && hasBaseArchive != (candidate.MatchesBaseArchive is not null)
                    || candidate.IsEffective && candidate.MatchesEffective != true
                    || candidate.Kind == SvPackedLooseSourceKind.BaseArchive
                        && candidate.IsPresent
                        && candidate.MatchesBaseArchive != true))
            {
                throw new SemanticExploreValidationException(
                    "The packed and loose source comparison returned an inconsistent candidate.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var recordId = RecordId(providerId, "source", entry.VirtualIdentity);
            var facts = new List<GameModuleFactDto>
            {
                TextFact(
                    providerId,
                    recordId,
                    "virtualIdentity",
                    "Virtual identity",
                    entry.VirtualIdentity),
                EnumFact(
                    providerId,
                    recordId,
                    "effectiveSource",
                    "Effective source",
                    PackedLooseEffectiveSourceValue(entry.EffectiveSource)),
                EnumFact(
                    providerId,
                    recordId,
                    "dualLooseOutputState",
                    "Dual loose output state",
                    PackedLooseDualOutputStateValue(entry.DualLooseOutputState)),
                DerivedSignedFact(
                    providerId,
                    recordId,
                    "presentSourceCount",
                    "Present source count",
                    entry.Candidates.Count(candidate => candidate.IsPresent)),
            };
            foreach (var candidate in entry.Candidates.OrderBy(candidate => candidate.Kind))
            {
                var (key, label) = PackedLooseCandidatePresentation(candidate.Kind);
                facts.Add(BooleanFact(
                    providerId,
                    recordId,
                    $"{key}Present",
                    $"{label} present",
                    candidate.IsPresent));
                facts.Add(NullableSignedFact(
                    providerId,
                    recordId,
                    $"{key}ByteLength",
                    $"{label} byte length",
                    candidate.ByteLength,
                    "bytes"));
                facts.Add(NullableBooleanFact(
                    providerId,
                    recordId,
                    $"{key}MatchesEffective",
                    $"{label} matches effective",
                    candidate.MatchesEffective));
                facts.Add(NullableBooleanFact(
                    providerId,
                    recordId,
                    $"{key}MatchesBaseArchive",
                    $"{label} matches base archive",
                    candidate.MatchesBaseArchive));
            }

            records.Add(CreateRecord(
                recordId,
                "packedLooseSource",
                groupId: null,
                parentRecordId: null,
                records.Count,
                entry.VirtualIdentity,
                "Verified source presence, byte equality, and effective read precedence.",
                target: null,
                capability,
                facts));
        }

        var diagnostics = comparison.DivergentDualLooseCount == 0
            ? Array.Empty<ApiDiagnostic>()
            :
            [
                new ApiDiagnostic(
                    ApiDiagnosticSeverity.Warning,
                    "Standalone and manager-style loose candidates differ. Both remain explicit and the effective source is shown.",
                    Domain: "gameModules")
                {
                    Code = "KM-SV-SOURCE-COMPARISON-DUAL-LOOSE-DIVERGENT",
                },
            ];
        var ordered = records
            .OrderBy(record => record.SortOrder)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .ToArray();
        var scrubbedDiagnostics = ScrubDiagnostics(module, diagnostics);
        ValidateProjectedBounds(ordered, scrubbedDiagnostics);
        return new GameModuleData(
            capability,
            ordered,
            scrubbedDiagnostics,
            Cacheable: false);
    }

    private static bool IsPackedLooseVirtualIdentity(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity)
            || Encoding.UTF8.GetByteCount(identity) > 512)
        {
            return false;
        }

        if (identity.StartsWith("romfs/", StringComparison.Ordinal))
        {
            var virtualPath = identity["romfs/".Length..];
            return virtualPath.Length > 0
                && !virtualPath.Contains('\\')
                && !virtualPath.Contains(':')
                && !virtualPath.Any(IsUnsafeUnicode)
                && virtualPath.Split('/').All(segment =>
                    !string.IsNullOrWhiteSpace(segment)
                    && segment is not "." and not "..");
        }

        const string hashPrefix = "trinity-hash:";
        return identity.StartsWith(hashPrefix, StringComparison.Ordinal)
            && identity.Length == hashPrefix.Length + 16
            && identity[hashPrefix.Length..].All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsPackedLooseEffectiveSourceConsistent(
        SvPackedLooseSourceEntry entry,
        IReadOnlySet<SvPackedLooseSourceKind> presentKinds)
    {
        if (presentKinds.Contains(SvPackedLooseSourceKind.StandaloneLooseOutput)
            || presentKinds.Contains(SvPackedLooseSourceKind.ManagerLooseOutput))
        {
            return entry.EffectiveSource
                is SvPackedLooseEffectiveSource.StandaloneLooseOutput
                    or SvPackedLooseEffectiveSource.ManagerLooseOutput;
        }

        if (presentKinds.Contains(SvPackedLooseSourceKind.OutputArchive))
        {
            return entry.EffectiveSource == SvPackedLooseEffectiveSource.OutputArchive;
        }

        if (presentKinds.Contains(SvPackedLooseSourceKind.BaseLoose))
        {
            return entry.EffectiveSource == SvPackedLooseEffectiveSource.BaseLoose;
        }

        return presentKinds.Contains(SvPackedLooseSourceKind.BaseArchive)
            ? entry.EffectiveSource == SvPackedLooseEffectiveSource.BaseArchive
            : entry.EffectiveSource == SvPackedLooseEffectiveSource.None;
    }

    private static bool CandidateMatchesEffectiveSource(
        SvPackedLooseSourceKind kind,
        SvPackedLooseEffectiveSource effectiveSource)
    {
        return (kind, effectiveSource) switch
        {
            (SvPackedLooseSourceKind.BaseArchive,
                SvPackedLooseEffectiveSource.BaseArchive) => true,
            (SvPackedLooseSourceKind.BaseLoose,
                SvPackedLooseEffectiveSource.BaseLoose) => true,
            (SvPackedLooseSourceKind.StandaloneLooseOutput,
                SvPackedLooseEffectiveSource.StandaloneLooseOutput) => true,
            (SvPackedLooseSourceKind.ManagerLooseOutput,
                SvPackedLooseEffectiveSource.ManagerLooseOutput) => true,
            (SvPackedLooseSourceKind.OutputArchive,
                SvPackedLooseEffectiveSource.OutputArchive) => true,
            _ => false,
        };
    }

    private static (string Key, string Label) PackedLooseCandidatePresentation(
        SvPackedLooseSourceKind kind)
    {
        return kind switch
        {
            SvPackedLooseSourceKind.BaseArchive => ("baseArchive", "Base archive"),
            SvPackedLooseSourceKind.BaseLoose => ("baseLoose", "Base loose"),
            SvPackedLooseSourceKind.StandaloneLooseOutput =>
                ("standaloneLooseOutput", "Standalone loose output"),
            SvPackedLooseSourceKind.ManagerLooseOutput =>
                ("managerLooseOutput", "Manager-style loose output"),
            SvPackedLooseSourceKind.OutputArchive => ("outputArchive", "Output archive"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static string PackedLooseEffectiveSourceValue(SvPackedLooseEffectiveSource source)
    {
        return source switch
        {
            SvPackedLooseEffectiveSource.None => "none",
            SvPackedLooseEffectiveSource.BaseArchive => "baseArchive",
            SvPackedLooseEffectiveSource.BaseLoose => "baseLoose",
            SvPackedLooseEffectiveSource.StandaloneLooseOutput => "standaloneLooseOutput",
            SvPackedLooseEffectiveSource.ManagerLooseOutput => "managerLooseOutput",
            SvPackedLooseEffectiveSource.OutputArchive => "outputArchive",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
    }

    private static string PackedLooseDualOutputStateValue(SvPackedLooseDualOutputState state)
    {
        return state switch
        {
            SvPackedLooseDualOutputState.NotComparable => "notComparable",
            SvPackedLooseDualOutputState.Identical => "identical",
            SvPackedLooseDualOutputState.Divergent => "divergent",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    public static GameModuleData BuildEventDataComparison(
        SvEventDataComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var module = GameModuleDto.ScarletVioletEventDataComparison;
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        if (comparison.Entries.Count > 10_000
            || comparison.ComparedEntityCount != comparison.Entries.Count
            || comparison.ChangedEntityCount < 0
            || comparison.ChangedEntityCount
                != comparison.Entries.Count(entry => entry.Presence != SvEventComparisonPresence.Unchanged)
            || comparison.ChangedFieldCount < 0
            || comparison.ChangedFieldCount
                != comparison.Entries.Sum(entry => entry.Differences.Count))
        {
            throw new SemanticExploreValidationException(
                "The event data comparison exceeds its bounded result contract.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in comparison.Entries)
        {
            var expectedIdentity = string.Create(
                CultureInfo.InvariantCulture,
                $"{EventComparisonDomainPrefix(entry.Domain)}:{entry.Occurrence}");
            var differenceKeys = new HashSet<string>(StringComparer.Ordinal);
            if (entry.Occurrence < 0
                || !identities.Add(entry.StableIdentity)
                || !string.Equals(entry.StableIdentity, expectedIdentity, StringComparison.Ordinal)
                || entry.ComparedFieldCount != EventComparisonFieldCount(entry.Domain)
                || entry.Differences.Count > entry.ComparedFieldCount
                || entry.Presence == SvEventComparisonPresence.Unchanged && entry.Differences.Count != 0
                || entry.Presence == SvEventComparisonPresence.Modified && entry.Differences.Count == 0
                || entry.Presence is SvEventComparisonPresence.Added or SvEventComparisonPresence.Removed
                    && entry.Differences.Count != 0
                || entry.Differences.Any(difference =>
                    !differenceKeys.Add(difference.FieldKey)
                    || !IsEventComparisonField(entry.Domain, difference.FieldKey)
                    || string.IsNullOrWhiteSpace(difference.FieldLabel)
                    || difference.FieldLabel.Length > 128
                    || ContainsLocalPathSignature(difference.FieldLabel)
                    || !IsValidEventComparisonScalar(difference.BaseValue)
                    || !IsValidEventComparisonScalar(difference.EffectiveValue)))
            {
                throw new SemanticExploreValidationException(
                    "The event data comparison returned an inconsistent owned-field record.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        var changedEntries = comparison.Entries
            .Where(entry => entry.Presence != SvEventComparisonPresence.Unchanged)
            .ToArray();
        var projectedRecordCount = checked(3L + changedEntries.Sum(entry =>
            entry.Differences.Count == 0
                ? 1L
                : (entry.Differences.Count + 11L) / 12L));
        var projectedFactCount = checked(
            24L
            + changedEntries.Sum(entry => entry.Differences.Count == 0
                ? 3L
                : ((entry.Differences.Count + 11L) / 12L) * 3L
                    + entry.Differences.Count * 2L));
        EnsureProjectionCounts(projectedRecordCount, projectedFactCount);

        var records = new BoundedRecordCollection();
        foreach (var domain in Enum.GetValues<SvEventComparisonDomain>())
        {
            var domainEntries = comparison.Entries
                .Where(entry => entry.Domain == domain)
                .OrderBy(entry => entry.Occurrence)
                .ToArray();
            var (domainKey, domainLabel) = EventComparisonDomainPresentation(domain);
            var groupId = StableGroup("eventDomain", domainKey);
            var summaryRecordId = RecordId(providerId, "eventSummary", domainKey);
            records.Add(CreateRecord(
                summaryRecordId,
                "eventComparisonSummary",
                groupId,
                parentRecordId: null,
                records.Count,
                $"{domainLabel} comparison",
                "Verified Base-to-effective comparison for fields owned by the existing editor.",
                target: null,
                capability,
                [
                    EnumFact(providerId, summaryRecordId, "domain", "Domain", domainKey),
                    DerivedSignedFact(providerId, summaryRecordId, "comparedEntities", "Compared entities", domainEntries.Length),
                    DerivedSignedFact(providerId, summaryRecordId, "unchangedEntities", "Unchanged entities", domainEntries.Count(entry => entry.Presence == SvEventComparisonPresence.Unchanged)),
                    DerivedSignedFact(providerId, summaryRecordId, "modifiedEntities", "Modified entities", domainEntries.Count(entry => entry.Presence == SvEventComparisonPresence.Modified)),
                    DerivedSignedFact(providerId, summaryRecordId, "addedEntities", "Added entities", domainEntries.Count(entry => entry.Presence == SvEventComparisonPresence.Added)),
                    DerivedSignedFact(providerId, summaryRecordId, "removedEntities", "Removed entities", domainEntries.Count(entry => entry.Presence == SvEventComparisonPresence.Removed)),
                    DerivedSignedFact(providerId, summaryRecordId, "changedFields", "Changed owned fields", domainEntries.Sum(entry => entry.Differences.Count)),
                    DerivedSignedFact(providerId, summaryRecordId, "ownedFieldsPerEntity", "Owned fields per entity", EventComparisonFieldCount(domain)),
                ]));

            foreach (var entry in domainEntries.Where(candidate =>
                         candidate.Presence != SvEventComparisonPresence.Unchanged))
            {
                AddEventComparisonChangeRecords(
                    records,
                    providerId,
                    capability,
                    groupId,
                    domainLabel,
                    entry);
            }
        }

        var ordered = records
            .OrderBy(record => record.SortOrder)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .ToArray();
        ValidateProjectedBounds(ordered, []);
        return new GameModuleData(
            capability,
            ordered,
            [],
            Cacheable: false);
    }

    private static void AddEventComparisonChangeRecords(
        BoundedRecordCollection records,
        string providerId,
        GameModuleCapabilityDto capability,
        string groupId,
        string domainLabel,
        SvEventComparisonEntry entry)
    {
        var target = EventComparisonTarget(entry);
        if (entry.Differences.Count == 0)
        {
            var recordId = RecordId(providerId, "eventChange", $"{entry.StableIdentity}:part:0");
            records.Add(CreateRecord(
                recordId,
                "eventComparisonChange",
                groupId,
                parentRecordId: null,
                records.Count,
                $"{domainLabel} {entry.Occurrence} - {EventComparisonPresenceValue(entry.Presence)}",
                "Verified physical occurrence presence relative to Base.",
                entry.Presence == SvEventComparisonPresence.Added ? target : null,
                capability,
                [
                    EnumFact(providerId, recordId, "domain", "Domain", EventComparisonDomainPresentation(entry.Domain).Key),
                    SignedFact(providerId, recordId, "occurrence", "Physical occurrence", entry.Occurrence),
                    EnumFact(providerId, recordId, "presence", "Presence", EventComparisonPresenceValue(entry.Presence)),
                ]));
            return;
        }

        var chunks = entry.Differences.Chunk(12).ToArray();
        for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            var recordId = RecordId(
                providerId,
                "eventChange",
                $"{entry.StableIdentity}:part:{chunkIndex}");
            var facts = new List<GameModuleFactDto>
            {
                EnumFact(
                    providerId,
                    recordId,
                    "domain",
                    "Domain",
                    EventComparisonDomainPresentation(entry.Domain).Key,
                    target),
                SignedFact(
                    providerId,
                    recordId,
                    "occurrence",
                    "Physical occurrence",
                    entry.Occurrence,
                    evidence: target),
                EnumFact(
                    providerId,
                    recordId,
                    "presence",
                    "Presence",
                    EventComparisonPresenceValue(entry.Presence),
                    target),
            };

            foreach (var difference in chunk)
            {
                facts.Add(EventComparisonScalarFact(
                    providerId,
                    recordId,
                    $"{difference.FieldKey}Base",
                    $"Base {difference.FieldLabel}",
                    difference.BaseValue,
                    target));
                facts.Add(EventComparisonScalarFact(
                    providerId,
                    recordId,
                    $"{difference.FieldKey}Effective",
                    $"Effective {difference.FieldLabel}",
                    difference.EffectiveValue,
                    target));
            }

            var firstField = chunkIndex * 12 + 1;
            var lastField = firstField + chunk.Length - 1;
            records.Add(CreateRecord(
                recordId,
                "eventComparisonChange",
                groupId,
                parentRecordId: null,
                records.Count,
                chunk.Length == 1
                    ? $"{domainLabel} {entry.Occurrence} - {chunk[0].FieldLabel}"
                    : $"{domainLabel} {entry.Occurrence} - fields {firstField}-{lastField}",
                "Verified Base and effective values for owned event fields. Unmapped fields remain opaque.",
                target,
                capability,
                facts));
        }
    }

    private static GameModuleFactDto EventComparisonScalarFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        SvEventComparisonScalar scalar,
        SemanticRecordRefDto? evidence)
    {
        return scalar.Kind switch
        {
            SvEventComparisonScalarKind.Null =>
                NullFact(providerId, recordId, fieldKey, label, unit: null, evidence),
            SvEventComparisonScalarKind.SignedInteger when long.TryParse(
                scalar.CanonicalValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var signed) => SignedFact(providerId, recordId, fieldKey, label, signed, evidence: evidence),
            SvEventComparisonScalarKind.Text when scalar.CanonicalValue is { } text =>
                TextFact(providerId, recordId, fieldKey, label, text, evidence: evidence),
            _ => throw new SemanticExploreValidationException(
                "The event data comparison returned an invalid scalar.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static bool IsValidEventComparisonScalar(SvEventComparisonScalar scalar)
    {
        if (scalar is null)
        {
            return false;
        }

        return scalar.Kind switch
        {
            SvEventComparisonScalarKind.Null => scalar.CanonicalValue is null,
            SvEventComparisonScalarKind.SignedInteger => long.TryParse(
                    scalar.CanonicalValue,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var value)
                && string.Equals(
                    scalar.CanonicalValue,
                    value.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal),
            SvEventComparisonScalarKind.Text => scalar.CanonicalValue is { Length: <= 512 } text
                && !ContainsLocalPathSignature(text),
            _ => false,
        };
    }

    private static SemanticRecordRefDto? EventComparisonTarget(SvEventComparisonEntry entry)
    {
        return entry.Domain switch
        {
            SvEventComparisonDomain.GiftPokemon => Record(
                SemanticGameFamilyDto.ScarletViolet,
                "workflow.giftPokemon",
                "gift-pokemon",
                entry.Occurrence.ToString(CultureInfo.InvariantCulture)),
            SvEventComparisonDomain.TradePokemon => Record(
                SemanticGameFamilyDto.ScarletViolet,
                "workflow.tradePokemon",
                "trade-pokemon",
                entry.Occurrence.ToString(CultureInfo.InvariantCulture)),
            SvEventComparisonDomain.EventDeliveryRaid => Record(
                SemanticGameFamilyDto.ScarletViolet,
                "workflow.teraRaids",
                "tera-raid",
                $"raid:delivery:{entry.Occurrence}"),
            _ => null,
        };
    }

    private static (string Key, string Label) EventComparisonDomainPresentation(
        SvEventComparisonDomain domain)
    {
        return domain switch
        {
            SvEventComparisonDomain.GiftPokemon => ("giftPokemon", "Gift Pokemon"),
            SvEventComparisonDomain.TradePokemon => ("tradePokemon", "Trade Pokemon"),
            SvEventComparisonDomain.EventDeliveryRaid => ("eventDeliveryRaids", "Event Delivery Raids"),
            _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, null),
        };
    }

    private static string EventComparisonDomainPrefix(SvEventComparisonDomain domain)
    {
        return domain switch
        {
            SvEventComparisonDomain.GiftPokemon => "gift",
            SvEventComparisonDomain.TradePokemon => "trade",
            SvEventComparisonDomain.EventDeliveryRaid => "event-raid",
            _ => throw new SemanticExploreValidationException(
                "The event data comparison returned an unsupported domain.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static int EventComparisonFieldCount(SvEventComparisonDomain domain)
    {
        return domain switch
        {
            SvEventComparisonDomain.GiftPokemon => 23,
            SvEventComparisonDomain.TradePokemon => 27,
            SvEventComparisonDomain.EventDeliveryRaid => 42,
            _ => throw new SemanticExploreValidationException(
                "The event data comparison returned an unsupported domain.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static bool IsEventComparisonField(
        SvEventComparisonDomain domain,
        string field)
    {
        return domain switch
        {
            SvEventComparisonDomain.GiftPokemon => field is
                "species" or "form" or "level" or "heldItemId" or "ballItemId"
                or "ability" or "nature" or "gender" or "shinyLock" or "teraType"
                or "move1Id" or "move2Id" or "move3Id" or "move4Id"
                or "flawlessIvCount" or "ivHp" or "ivAttack" or "ivDefense"
                or "ivSpecialAttack" or "ivSpecialDefense" or "ivSpeed"
                or "scaleMode" or "scaleValue",
            SvEventComparisonDomain.TradePokemon => field is
                "species" or "form" or "level" or "heldItemId" or "ballItemId"
                or "ability" or "nature" or "gender" or "shinyLock" or "teraType"
                or "move1Id" or "move2Id" or "move3Id" or "move4Id"
                or "flawlessIvCount" or "ivHp" or "ivAttack" or "ivDefense"
                or "ivSpecialAttack" or "ivSpecialDefense" or "ivSpeed"
                or "scaleMode" or "scaleValue" or "requiredSpecies" or "requiredForm"
                or "trainerId" or "otGender",
            SvEventComparisonDomain.EventDeliveryRaid => field is
                "version" or "difficulty" or "deliveryGroupId" or "spawnRate"
                or "captureRate" or "captureLevel" or "species" or "form" or "level"
                or "heldItemId" or "ballItemId" or "ability" or "nature" or "gender"
                or "shinyLock" or "teraType" or "moveMode" or "move1Id" or "move2Id"
                or "move3Id" or "move4Id" or "flawlessIvCount" or "ivHp" or "ivAttack"
                or "ivDefense" or "ivSpecialAttack" or "ivSpecialDefense" or "ivSpeed"
                or "scaleMode" or "scaleValue" or "heightMode" or "heightValue"
                or "weightMode" or "weightValue" or "hpMultiplier" or "shieldTriggerHp"
                or "shieldTriggerTime" or "doubleActionHp" or "doubleActionTime"
                or "doubleActionRate" or "fixedRewardTable" or "lotteryRewardTable",
            _ => false,
        };
    }

    private static string EventComparisonPresenceValue(SvEventComparisonPresence presence)
    {
        return presence switch
        {
            SvEventComparisonPresence.Unchanged => "unchanged",
            SvEventComparisonPresence.Modified => "modified",
            SvEventComparisonPresence.Added => "added",
            SvEventComparisonPresence.Removed => "removed",
            _ => throw new SemanticExploreValidationException(
                "The event data comparison returned an unsupported presence state.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    public static GameModuleData BuildScenePlacementProjection(
        SvScenePlacementProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var module = GameModuleDto.ScarletVioletScenePlacementEditing;
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var ownedFieldCount = projection.Entries.Sum(entry => (long)entry.Fields.Count);
        if (projection.Sources.Count > 8
            || projection.Entries.Count > 10_000
            || ownedFieldCount > 300_000)
        {
            throw new SemanticExploreValidationException(
                "The scene placement projection exceeds its bounded result contract.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (projection.Sources.Count != 8
            || !MatchesExactScenePlacementSourceSet(projection.Sources))
        {
            throw new SemanticExploreValidationException(
                "The scene placement projection does not contain the exact supported source set.",
                SemanticExploreFailureKind.InvalidData);
        }

        var sourcesByIdentity = new Dictionary<string, SvScenePlacementSource>(StringComparer.Ordinal);
        foreach (var source in projection.Sources)
        {
            if (!IsSafeScenePlacementSourceIdentity(source.SourceIdentity)
                || !sourcesByIdentity.TryAdd(source.SourceIdentity, source)
                || source.RecordCount < 0
                || !IsScenePlacementSourceMetadataConsistent(source))
            {
                throw new SemanticExploreValidationException(
                    "The scene placement projection returned invalid source metadata.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        var stableIdentities = new HashSet<string>(StringComparer.Ordinal);
        var occurrencesBySource = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var entry in projection.Entries)
        {
            if (!occurrencesBySource.TryGetValue(entry.SourceIdentity, out var sourceOccurrences))
            {
                sourceOccurrences = new HashSet<int>();
                occurrencesBySource.Add(entry.SourceIdentity, sourceOccurrences);
            }

            if (!sourcesByIdentity.TryGetValue(entry.SourceIdentity, out var source)
                || source.Domain != entry.Domain
                || source.SourceLayer != entry.SourceLayer
                || source.FileState != entry.FileState
                || entry.Occurrence < 0
                || !stableIdentities.Add(entry.StableIdentity)
                || !string.Equals(
                    entry.StableIdentity,
                    ScenePlacementStableIdentity(entry.Domain, entry.SourceIdentity, entry.Occurrence),
                    StringComparison.Ordinal)
                || !sourceOccurrences.Add(entry.Occurrence)
                || !ScenePlacementExpectedFields(entry.Domain)
                    .SequenceEqual(entry.Fields.Select(field => field.FieldKey), StringComparer.Ordinal)
                || entry.Fields.Any(field =>
                    field.CanonicalValue is < int.MinValue or > int.MaxValue
                    || entry.Domain == SvScenePlacementDomain.RummagingItemPool
                        && field.CanonicalValue is null))
            {
                throw new SemanticExploreValidationException(
                    "The scene placement projection returned an inconsistent owned-field record.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        if (projection.Sources.Any(source =>
                projection.Entries.Count(entry => string.Equals(
                    entry.SourceIdentity,
                    source.SourceIdentity,
                    StringComparison.Ordinal)) != source.RecordCount))
        {
            throw new SemanticExploreValidationException(
                "The scene placement source counts do not match their physical records.",
                SemanticExploreFailureKind.InvalidData);
        }

        var projectedRecordCount = checked(3L + projection.Sources.Count + projection.Entries.Count);
        var projectedFactCount = checked(
            12L
            + projection.Sources.Count * 5L
            + projection.Entries.Count * 2L
            + ownedFieldCount);
        EnsureProjectionCounts(projectedRecordCount, projectedFactCount);

        var records = new BoundedRecordCollection();
        foreach (var domain in Enum.GetValues<SvScenePlacementDomain>())
        {
            var domainSources = projection.Sources
                .Where(source => source.Domain == domain)
                .OrderBy(source => source.SourceIdentity, StringComparer.Ordinal)
                .ToArray();
            var domainEntries = projection.Entries
                .Where(entry => entry.Domain == domain)
                .OrderBy(entry => entry.SourceIdentity, StringComparer.Ordinal)
                .ThenBy(entry => entry.Occurrence)
                .ToArray();
            var (domainKey, domainLabel, _) = ScenePlacementDomainPresentation(domain);
            var domainGroupId = StableGroup("scenePlacementDomain", domainKey);
            var domainRecordId = RecordId(providerId, "scenePlacementSummary", domainKey);
            records.Add(CreateRecord(
                domainRecordId,
                "scenePlacementSummary",
                domainGroupId,
                parentRecordId: null,
                records.Count,
                $"{domainLabel} coverage",
                "Verified read-only coverage for fields owned by the existing Placement editor.",
                target: null,
                capability,
                [
                    EnumFact(providerId, domainRecordId, "domain", "Domain", domainKey),
                    DerivedSignedFact(providerId, domainRecordId, "sourceCount", "Source count", domainSources.Length),
                    DerivedSignedFact(providerId, domainRecordId, "recordCount", "Record count", domainEntries.Length),
                    DerivedSignedFact(providerId, domainRecordId, "ownedFieldCount", "Owned field count", domainEntries.Sum(entry => entry.Fields.Count)),
                ]));

            foreach (var source in domainSources)
            {
                var sourceRecordId = RecordId(
                    providerId,
                    "scenePlacementSource",
                    source.SourceIdentity);
                var sourceGroupId = StableGroup("scenePlacementSource", source.SourceIdentity);
                records.Add(CreateRecord(
                    sourceRecordId,
                    "scenePlacementSource",
                    sourceGroupId,
                    domainRecordId,
                    records.Count,
                    $"{domainLabel} source",
                    "Verified effective source metadata for the owned read-only projection.",
                    target: null,
                    capability,
                    [
                        EnumFact(providerId, sourceRecordId, "domain", "Domain", domainKey),
                        TextFact(providerId, sourceRecordId, "sourceIdentity", "Source identity", source.SourceIdentity),
                        EnumFact(providerId, sourceRecordId, "sourceLayer", "Source layer", ScenePlacementSourceLayerValue(source.SourceLayer)),
                        EnumFact(providerId, sourceRecordId, "fileState", "File state", ScenePlacementFileStateValue(source.FileState)),
                        DerivedSignedFact(providerId, sourceRecordId, "recordCount", "Record count", source.RecordCount),
                    ]));

                foreach (var entry in domainEntries.Where(entry => string.Equals(
                             entry.SourceIdentity,
                             source.SourceIdentity,
                             StringComparison.Ordinal)))
                {
                    var target = Record(
                        SemanticGameFamilyDto.ScarletViolet,
                        "workflow.placement",
                        "placed-object",
                        entry.StableIdentity);
                    var recordId = RecordId(
                        providerId,
                        "scenePlacementRecord",
                        entry.StableIdentity);
                    var facts = new List<GameModuleFactDto>
                    {
                        EnumFact(
                            providerId,
                            recordId,
                            "domain",
                            "Domain",
                            domainKey,
                            target),
                        SignedFact(
                            providerId,
                            recordId,
                            "occurrence",
                            "Physical occurrence",
                            entry.Occurrence,
                            evidence: target),
                    };
                    foreach (var field in entry.Fields)
                    {
                        var factKey = ScenePlacementFactKey(field.FieldKey);
                        var label = ScenePlacementFieldLabel(field.FieldKey);
                        facts.Add(field.CanonicalValue is { } value
                            ? SignedFact(
                                providerId,
                                recordId,
                                factKey,
                                label,
                                value,
                                evidence: target)
                            : NullFact(
                                providerId,
                                recordId,
                                factKey,
                                label,
                                unit: null,
                                target));
                    }

                    records.Add(CreateRecord(
                        recordId,
                        "scenePlacementRecord",
                        sourceGroupId,
                        sourceRecordId,
                        records.Count,
                        $"{domainLabel} occurrence {entry.Occurrence.ToString(CultureInfo.InvariantCulture)}",
                        "Verified visible-item or item-pool fields only. Coordinates, rotations, display naming, and other scene values are excluded.",
                        target,
                        capability,
                        facts));
                }
            }
        }

        var ordered = records
            .OrderBy(record => record.SortOrder)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .ToArray();
        ValidateProjectedBounds(ordered, []);
        return new GameModuleData(
            capability,
            ordered,
            [],
            Cacheable: false);
    }

    private static bool MatchesExactScenePlacementSourceSet(
        IReadOnlyList<SvScenePlacementSource> sources)
    {
        var identities = sources
            .Select(source => source.SourceIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var shared = new[]
        {
            SvDataPaths.HiddenItemDataTableArray,
            SvDataPaths.HiddenItemDataTableSu1Array,
            SvDataPaths.HiddenItemDataTableSu2Array,
            SvDataPaths.HiddenItemDataTableLcArray,
            SvDataPaths.RummagingItemDataTableArray,
        };
        var scarlet = shared.Concat([
            SvDataPaths.VisibleItemScenePaldeaScarlet,
            SvDataPaths.VisibleItemSceneKitakamiScarlet,
            SvDataPaths.VisibleItemSceneBlueberryScarlet,
        ]);
        var violet = shared.Concat([
            SvDataPaths.VisibleItemScenePaldeaViolet,
            SvDataPaths.VisibleItemSceneKitakamiViolet,
            SvDataPaths.VisibleItemSceneBlueberryViolet,
        ]);
        return (identities.SetEquals(scarlet) || identities.SetEquals(violet))
            && sources.All(source => source.Domain == ScenePlacementSourceDomain(
                source.SourceIdentity));
    }

    private static SvScenePlacementDomain ScenePlacementSourceDomain(string sourceIdentity)
    {
        if (sourceIdentity == SvDataPaths.RummagingItemDataTableArray)
        {
            return SvScenePlacementDomain.RummagingItemPool;
        }

        if (sourceIdentity == SvDataPaths.HiddenItemDataTableArray
            || sourceIdentity == SvDataPaths.HiddenItemDataTableSu1Array
            || sourceIdentity == SvDataPaths.HiddenItemDataTableSu2Array
            || sourceIdentity == SvDataPaths.HiddenItemDataTableLcArray)
        {
            return SvScenePlacementDomain.HiddenItemPool;
        }

        return SvScenePlacementDomain.VisibleItem;
    }

    private static bool IsSafeScenePlacementSourceIdentity(string identity)
    {
        return !string.IsNullOrWhiteSpace(identity)
            && Encoding.UTF8.GetByteCount(identity) <= 512
            && identity.StartsWith("world/", StringComparison.Ordinal)
            && !identity.Contains('\\')
            && !identity.Contains(':')
            && !identity.Any(IsUnsafeUnicode)
            && identity.Split('/').All(segment =>
                !string.IsNullOrWhiteSpace(segment)
                && segment is not "." and not "..");
    }

    private static bool IsScenePlacementSourceMetadataConsistent(
        SvScenePlacementSource source)
    {
        return source.SourceLayer switch
        {
            ProjectFileLayer.Base => source.FileState == ProjectFileGraphEntryState.BaseOnly,
            ProjectFileLayer.Layered => source.FileState == ProjectFileGraphEntryState.LayeredOverride,
            _ => false,
        };
    }

    private static string ScenePlacementStableIdentity(
        SvScenePlacementDomain domain,
        string sourceIdentity,
        int occurrence)
    {
        var (_, _, categoryId) = ScenePlacementDomainPresentation(domain);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{categoryId}:{sourceIdentity}:{occurrence}");
    }

    private static (string Key, string Label, string CategoryId) ScenePlacementDomainPresentation(
        SvScenePlacementDomain domain)
    {
        return domain switch
        {
            SvScenePlacementDomain.VisibleItem =>
                ("visibleItems", "Visible items", "visibleItems"),
            SvScenePlacementDomain.HiddenItemPool =>
                ("hiddenItemPools", "Hidden item pools", "hiddenItems"),
            SvScenePlacementDomain.RummagingItemPool =>
                ("rummagingItemPools", "Rummaging pools", "rummagingPoints"),
            _ => throw new SemanticExploreValidationException(
                "The scene placement projection returned an unsupported domain.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static IReadOnlyList<string> ScenePlacementExpectedFields(
        SvScenePlacementDomain domain)
    {
        return domain switch
        {
            SvScenePlacementDomain.VisibleItem =>
                ["visible.itemId", "visible.quantity"],
            SvScenePlacementDomain.HiddenItemPool => Enumerable.Range(1, 10)
                .SelectMany(slot => new[]
                {
                    $"hidden.item{slot.ToString(CultureInfo.InvariantCulture)}.itemId",
                    $"hidden.item{slot.ToString(CultureInfo.InvariantCulture)}.chance",
                    $"hidden.item{slot.ToString(CultureInfo.InvariantCulture)}.count",
                })
                .ToArray(),
            SvScenePlacementDomain.RummagingItemPool =>
            [
                "rummaging.category",
                "rummaging.pattern",
                "rummaging.item1",
                "rummaging.item2",
                "rummaging.item3",
                "rummaging.item4",
                "rummaging.item5",
            ],
            _ => throw new SemanticExploreValidationException(
                "The scene placement projection returned an unsupported domain.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static string ScenePlacementFieldLabel(string field)
    {
        if (field == "visible.itemId")
        {
            return "Item ID";
        }

        if (field == "visible.quantity")
        {
            return "Quantity";
        }

        if (field == "rummaging.category")
        {
            return "Category";
        }

        if (field == "rummaging.pattern")
        {
            return "Pattern";
        }

        if (field.StartsWith("rummaging.item", StringComparison.Ordinal)
            && int.TryParse(
                field["rummaging.item".Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rummagingSlot)
            && rummagingSlot is >= 1 and <= 5)
        {
            return $"Item {rummagingSlot.ToString(CultureInfo.InvariantCulture)} ID";
        }

        const string hiddenPrefix = "hidden.item";
        if (field.StartsWith(hiddenPrefix, StringComparison.Ordinal))
        {
            var suffix = field[hiddenPrefix.Length..];
            var separator = suffix.IndexOf('.');
            if (separator > 0
                && int.TryParse(
                    suffix[..separator],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var hiddenSlot)
                && hiddenSlot is >= 1 and <= 10)
            {
                var label = suffix[(separator + 1)..] switch
                {
                    "itemId" => "item ID",
                    "chance" => "emerge value",
                    "count" => "drop count",
                    _ => null,
                };
                if (label is not null)
                {
                    return $"Slot {hiddenSlot.ToString(CultureInfo.InvariantCulture)} {label}";
                }
            }
        }

        throw new SemanticExploreValidationException(
            "The scene placement projection returned an unsupported field.",
            SemanticExploreFailureKind.InvalidData);
    }

    private static string ScenePlacementFactKey(string field)
    {
        var segments = field.Split('.');
        if (segments.Length < 2 || segments.Any(string.IsNullOrEmpty))
        {
            throw new SemanticExploreValidationException(
                "The scene placement projection returned an unsupported field.",
                SemanticExploreFailureKind.InvalidData);
        }

        return segments[0]
            + string.Concat(segments.Skip(1).Select(segment =>
                char.ToUpperInvariant(segment[0]) + segment[1..]));
    }

    private static string ScenePlacementSourceLayerValue(ProjectFileLayer layer)
    {
        return layer switch
        {
            ProjectFileLayer.Base => "base",
            ProjectFileLayer.Layered => "layered",
            _ => throw new SemanticExploreValidationException(
                "The scene placement projection returned an unsupported source layer.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static string ScenePlacementFileStateValue(ProjectFileGraphEntryState state)
    {
        return state switch
        {
            ProjectFileGraphEntryState.BaseOnly => "baseOnly",
            ProjectFileGraphEntryState.LayeredOverride => "layeredOverride",
            _ => throw new SemanticExploreValidationException(
                "The scene placement projection returned an unsupported file state.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    public static GameModuleData BuildScarletVioletTypeEffectivenessState(
        SvTypeEffectivenessStateProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var module = GameModuleDto.ScarletVioletTypeEffectivenessState;
        var source = projection.Source;
        var expectedBuildId = source.Game switch
        {
            ProjectGame.Scarlet => "421C5411B487EB4D049DD065FEC9547773E8E598",
            ProjectGame.Violet => "709BFD66115298640155FCC4979DBA151C7CC79A",
            _ => null,
        };
        if (!string.Equals(source.SourceIdentity, "exefs/main", StringComparison.Ordinal)
            || expectedBuildId is null
            || !string.Equals(source.BuildId, expectedBuildId, StringComparison.Ordinal)
            || !IsScarletVioletTypeSourceMetadataConsistent(source)
            || projection.ChangedCellCount < 0
            || projection.ChartStateIsInconsistent())
        {
            throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an invalid executable identity or source boundary.",
                SemanticExploreFailureKind.InvalidData);
        }

        var cells = projection.Cells
            .OrderBy(cell => cell.AttackTypeId)
            .ThenBy(cell => cell.DefenseTypeId)
            .ToArray();
        var coordinates = new HashSet<(int Attack, int Defense)>();
        if (cells.Length != 18 * 18
            || cells.Any(cell =>
                cell.AttackTypeId is < 0 or >= 18
                || cell.DefenseTypeId is < 0 or >= 18
                || !string.Equals(
                    cell.StableIdentity,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"type-effectiveness:{cell.AttackTypeId}:{cell.DefenseTypeId}"),
                    StringComparison.Ordinal)
                || !IsVerifiedTypeEffectivenessValue(cell.Effectiveness)
                || !IsVerifiedTypeEffectivenessValue(cell.VanillaEffectiveness)
                || !coordinates.Add((cell.AttackTypeId, cell.DefenseTypeId)))
            || !cells.Select(cell => (cell.AttackTypeId, cell.DefenseTypeId))
                .SequenceEqual(
                    from attack in Enumerable.Range(0, 18)
                    from defense in Enumerable.Range(0, 18)
                    select (attack, defense))
            || projection.ChangedCellCount
                != cells.Count(cell => cell.Effectiveness != cell.VanillaEffectiveness))
        {
            throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an incomplete, duplicate, or inconsistent 18 by 18 table.",
                SemanticExploreFailureKind.InvalidData);
        }

        EnsureProjectionCounts(checked(1L + cells.LongLength), checked(8L + cells.LongLength * 9L));
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var records = new BoundedRecordCollection();
        var stateRecordId = RecordId(providerId, "state", "current-table");
        records.Add(CreateRecord(
            stateRecordId,
            "typeEffectivenessState",
            StableGroup("typeChart", "current-table"),
            parentRecordId: null,
            records.Count,
            "Current type effectiveness table",
            "Verified effective and vanilla 18 by 18 table state. Stellar and move-specific runtime resolution remain outside this projection.",
            target: null,
            capability,
            [
                TextFact(providerId, stateRecordId, "buildId", "Build ID", source.BuildId),
                EnumFact(providerId, stateRecordId, "game", "Game", ScarletVioletTypeGameValue(source.Game)),
                EnumFact(providerId, stateRecordId, "sourceLayer", "Source layer", ScenePlacementSourceLayerValue(source.SourceLayer)),
                EnumFact(providerId, stateRecordId, "fileState", "File state", ScenePlacementFileStateValue(source.FileState)),
                EnumFact(providerId, stateRecordId, "chartState", "Chart state", ScarletVioletTypeChartStateValue(source.ChartState)),
                DerivedSignedFact(providerId, stateRecordId, "cellCount", "Cell count", cells.Length),
                DerivedSignedFact(providerId, stateRecordId, "changedCellCount", "Changed from vanilla", projection.ChangedCellCount),
                DerivedSignedFact(providerId, stateRecordId, "runtimeClaimCount", "Runtime claim count", 0),
            ]));

        foreach (var cell in cells)
        {
            var identity = string.Create(
                CultureInfo.InvariantCulture,
                $"{cell.AttackTypeId}:{cell.DefenseTypeId}");
            var recordId = RecordId(providerId, "cell", identity);
            records.Add(CreateRecord(
                recordId,
                "typeEffectivenessCell",
                StableGroup(
                    "attackType",
                    cell.AttackTypeId.ToString(CultureInfo.InvariantCulture)),
                stateRecordId,
                records.Count,
                $"Type {cell.AttackTypeId} attacking type {cell.DefenseTypeId}",
                "Verified effective value and exact vanilla-table comparison.",
                target: null,
                capability,
                [
                    SignedFact(providerId, recordId, "attackTypeIndex", "Attack type index", cell.AttackTypeId),
                    EnumFact(providerId, recordId, "attackType", "Attack type", ScarletVioletTypeValue(cell.AttackTypeId)),
                    SignedFact(providerId, recordId, "defenseTypeIndex", "Defense type index", cell.DefenseTypeId),
                    EnumFact(providerId, recordId, "defenseType", "Defense type", ScarletVioletTypeValue(cell.DefenseTypeId)),
                    SignedFact(providerId, recordId, "currentValue", "Current stored value", cell.Effectiveness),
                    EnumFact(providerId, recordId, "currentEffectiveness", "Current effectiveness", TypeEffectivenessLabel(cell.Effectiveness)),
                    SignedFact(providerId, recordId, "vanillaValue", "Vanilla stored value", cell.VanillaEffectiveness),
                    EnumFact(providerId, recordId, "vanillaEffectiveness", "Vanilla effectiveness", TypeEffectivenessLabel(cell.VanillaEffectiveness)),
                    BooleanFact(providerId, recordId, "differsFromVanilla", "Differs from vanilla", cell.Effectiveness != cell.VanillaEffectiveness),
                ]));
        }

        return Complete(module, Array.Empty<ApiDiagnostic>(), records);
    }

    private static bool ChartStateIsInconsistent(
        this SvTypeEffectivenessStateProjection projection)
    {
        return projection.Source.ChartState switch
        {
            SvTypeEffectivenessChartState.Vanilla => projection.ChangedCellCount != 0,
            SvTypeEffectivenessChartState.Modified => projection.ChangedCellCount == 0,
            _ => true,
        };
    }

    private static bool IsScarletVioletTypeSourceMetadataConsistent(
        SvTypeEffectivenessSource source)
    {
        return source.SourceLayer switch
        {
            ProjectFileLayer.Base => source.FileState == ProjectFileGraphEntryState.BaseOnly,
            ProjectFileLayer.Layered => source.FileState == ProjectFileGraphEntryState.LayeredOverride,
            _ => false,
        };
    }

    private static string ScarletVioletTypeGameValue(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Scarlet => "scarlet",
            ProjectGame.Violet => "violet",
            _ => throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an unsupported game.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static string ScarletVioletTypeChartStateValue(
        SvTypeEffectivenessChartState state)
    {
        return state switch
        {
            SvTypeEffectivenessChartState.Vanilla => "vanilla",
            SvTypeEffectivenessChartState.Modified => "modified",
            _ => throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an unsupported chart state.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static string ScarletVioletTypeValue(int typeId)
    {
        return typeId switch
        {
            0 => "normal",
            1 => "fighting",
            2 => "flying",
            3 => "poison",
            4 => "ground",
            5 => "rock",
            6 => "bug",
            7 => "ghost",
            8 => "steel",
            9 => "fire",
            10 => "water",
            11 => "grass",
            12 => "electric",
            13 => "psychic",
            14 => "ice",
            15 => "dragon",
            16 => "dark",
            17 => "fairy",
            _ => throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an unsupported type ID.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    public static GameModuleData BuildRewardEcosystem(
        NpcItemGiftWorkflowDto npcItemGifts,
        RaidRewardsWorkflowDto raidRewards,
        RaidRewardsWorkflowDto raidBonusRewards,
        ShopsWorkflowDto shops,
        PlacementWorkflowDto placement)
    {
        ArgumentNullException.ThrowIfNull(npcItemGifts);
        ArgumentNullException.ThrowIfNull(raidRewards);
        ArgumentNullException.ThrowIfNull(raidBonusRewards);
        ArgumentNullException.ThrowIfNull(shops);
        ArgumentNullException.ThrowIfNull(placement);
        var module = GameModuleDto.SwordShieldRewardEcosystem;
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var records = new BoundedRecordCollection();

        if (CanProjectWorkflow(npcItemGifts.Summary, npcItemGifts.Diagnostics))
        {
            AddNpcItemGiftRecords(npcItemGifts, providerId, capability, records);
        }

        if (CanProjectWorkflow(raidRewards.Summary, raidRewards.Diagnostics))
        {
            AddRaidAcquisitionRecords(
                raidRewards,
                "workflow.raidRewards",
                "raid-reward-table",
                providerId,
                capability,
                records);
        }

        if (CanProjectWorkflow(raidBonusRewards.Summary, raidBonusRewards.Diagnostics))
        {
            AddRaidAcquisitionRecords(
                raidBonusRewards,
                "workflow.raidBonusRewards",
                "raid-bonus-reward-table",
                providerId,
                capability,
                records);
        }

        if (CanProjectWorkflow(shops.Summary, shops.Diagnostics))
        {
            AddShopAcquisitionRecords(shops, providerId, capability, records);
        }

        if (CanProjectWorkflow(placement.Summary, placement.Diagnostics))
        {
            AddPlacedItemRecords(placement, providerId, capability, records);
        }

        var summaries = new[]
        {
            npcItemGifts.Summary,
            raidRewards.Summary,
            raidBonusRewards.Summary,
            shops.Summary,
            placement.Summary,
        };
        var diagnostics = npcItemGifts.Diagnostics
            .Concat(raidRewards.Diagnostics)
            .Concat(raidBonusRewards.Diagnostics)
            .Concat(shops.Diagnostics)
            .Concat(placement.Diagnostics);
        return Complete(module, summaries, diagnostics, records);
    }

    public static GameModuleData BuildExeFsCompatibility(ExeFsPatchWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.SwordShieldExeFsCompatibility;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        EnsureProjectionCounts(
            checked(workflow.Patches.Count + workflow.Segments.Count + workflow.Checks.Count),
            checked(workflow.Patches.Count * 5L + workflow.Segments.Count * 7L + workflow.Checks.Count * 8L));
        var records = new BoundedRecordCollection();
        foreach (var patch in workflow.Patches.OrderBy(patch => patch.PatchId, StringComparer.Ordinal))
        {
            var target = Record(
                SemanticGameFamilyDto.SwordShield,
                "workflow.exefsPatches",
                "exefs-patch",
                patch.PatchId);
            var recordId = RecordId(providerId, "patch", patch.PatchId);
            records.Add(CreateRecord(
                recordId,
                "exefsPatch",
                StableGroup("patch", patch.PatchId),
                parentRecordId: null,
                records.Count,
                patch.Name,
                patch.Description,
                target,
                capability,
                [
                    TextFact(providerId, recordId, "patchId", "Patch ID", patch.PatchId, evidence: target),
                    EnumFact(providerId, recordId, "patchKind", "Patch kind", patch.PatchKind, target),
                    EnumFact(providerId, recordId, "status", "Status", patch.Status, target),
                    DerivedSignedFact(providerId, recordId, "detailCount", "Detail count", patch.Details.Count, evidence: target),
                    DerivedSignedFact(providerId, recordId, "sourceLayer", "Source layer", (int)patch.Provenance.SourceLayer, evidence: target),
                ]));
        }

        foreach (var segment in workflow.Segments.OrderBy(segment => segment.SegmentId, StringComparer.Ordinal))
        {
            var recordId = RecordId(providerId, "segment", segment.SegmentId);
            records.Add(CreateRecord(
                recordId,
                "exefsSegment",
                StableGroup("segment", segment.SegmentId),
                parentRecordId: null,
                records.Count,
                segment.Name,
                "Verified executable segment layout and hash state.",
                target: null,
                capability,
                [
                    TextFact(providerId, recordId, "segmentId", "Segment ID", segment.SegmentId),
                    TextFact(providerId, recordId, "fileOffset", "File offset", segment.FileOffset),
                    TextFact(providerId, recordId, "memoryOffset", "Memory offset", segment.MemoryOffset),
                    TextFact(providerId, recordId, "decompressedSize", "Decompressed size", segment.DecompressedSize),
                    TextFact(providerId, recordId, "compressedSize", "Compressed size", segment.CompressedSize),
                    TextFact(providerId, recordId, "sha256", "SHA-256", segment.Sha256),
                    EnumFact(providerId, recordId, "hashStatus", "Hash status", segment.HashStatus),
                ]));
        }

        foreach (var check in workflow.Checks.OrderBy(check => check.CheckId, StringComparer.Ordinal))
        {
            var target = Record(
                SemanticGameFamilyDto.SwordShield,
                "workflow.exefsPatches",
                "exefs-check",
                check.CheckId);
            var recordId = RecordId(providerId, "check", check.CheckId);
            records.Add(CreateRecord(
                recordId,
                "exefsCheck",
                StableGroup("patch", check.PatchId),
                parentRecordId: null,
                records.Count,
                check.Name,
                check.Notes,
                target,
                capability,
                [
                    TextFact(providerId, recordId, "checkId", "Check ID", check.CheckId, evidence: target),
                    TextFact(providerId, recordId, "patchId", "Patch ID", check.PatchId, evidence: target),
                    EnumFact(providerId, recordId, "status", "Status", check.Status, target),
                    EnumFact(providerId, recordId, "area", "Area", check.Area, target),
                    NullableTextFact(providerId, recordId, "offset", "Offset", check.Offset, target),
                    NullableTextFact(providerId, recordId, "expected", "Expected", check.Expected, target),
                    NullableTextFact(providerId, recordId, "actual", "Actual", check.Actual, target),
                    DerivedSignedFact(providerId, recordId, "sourceLayer", "Source layer", (int)check.Provenance.SourceLayer, evidence: target),
                ]));
        }

        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildDynamaxAdventures(
        DynamaxAdventuresWorkflowDto workflow,
        RentalPokemonWorkflowDto rentalPokemon,
        RaidRewardsWorkflowDto raidRewards)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(rentalPokemon);
        ArgumentNullException.ThrowIfNull(raidRewards);
        var module = GameModuleDto.SwordShieldDynamaxAdventures;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var moveCount = workflow.Encounters.Sum(encounter => (long)encounter.Moves.Count);
        var canProjectRentalPokemon = CanProjectWorkflow(
            rentalPokemon.Summary,
            rentalPokemon.Diagnostics);
        var canProjectRaidRewards = CanProjectWorkflow(
            raidRewards.Summary,
            raidRewards.Diagnostics);
        var rentalMoveCount = canProjectRentalPokemon
            ? rentalPokemon.Rentals.Sum(rental => (long)rental.Moves.Count)
            : 0L;
        var raidRewardCount = canProjectRaidRewards
            ? raidRewards.Tables.Sum(table => (long)table.Rewards.Count)
            : 0L;
        EnsureProjectionCounts(
            checked(
                2L
                + workflow.Encounters.Count
                + moveCount
                + workflow.ReservedRegions.Count
                + (canProjectRentalPokemon ? rentalPokemon.Rentals.Count : 0)
                + rentalMoveCount
                + (canProjectRaidRewards ? raidRewards.Tables.Count : 0)
                + raidRewardCount),
            checked(
                20L
                + workflow.Encounters.Count * 20L
                + moveCount * 3L
                + workflow.ReservedRegions.Count * 4L
                + (canProjectRentalPokemon ? rentalPokemon.Rentals.Count * 23L : 0L)
                + rentalMoveCount * 3L
                + (canProjectRaidRewards ? raidRewards.Tables.Count * 8L : 0L)
                + raidRewardCount * 22L));
        var records = new BoundedRecordCollection();
        var stateId = RecordId(providerId, "state", "executable-state");
        records.Add(CreateRecord(
            stateId,
            "dynamaxAdventureState",
            StableGroup("state", "executable-state"),
            parentRecordId: null,
            records.Count,
            "Adventure source state",
            workflow.InstallMessage,
            target: null,
            capability,
            [
                EnumFact(providerId, stateId, "installStatus", "Install status", workflow.InstallStatus),
                TextFact(providerId, stateId, "buildId", "Build ID", workflow.BuildId),
                NullableTextFact(providerId, stateId, "detectedGame", "Detected game", workflow.DetectedGame?.ToString()),
                BooleanFact(providerId, stateId, "legacyBossTargetPatch", "Legacy boss target patch", workflow.HasLegacyBossTargetPatch),
                BooleanFact(providerId, stateId, "canRestoreVanillaTable", "Can restore base table", workflow.CanRestoreVanillaTable),
                BooleanFact(providerId, stateId, "usesVanillaRecoveryProjection", "Uses base recovery projection", workflow.UsesVanillaRecoveryProjection),
                DerivedSignedFact(providerId, stateId, "reservedRegionCount", "Reserved region count", workflow.ReservedRegions.Count),
                DerivedSignedFact(providerId, stateId, "encounterCount", "Encounter count", workflow.Encounters.Count),
                DerivedSignedFact(providerId, stateId, "standaloneRentalCount", "Standalone rental count", canProjectRentalPokemon ? rentalPokemon.Rentals.Count : 0),
                DerivedSignedFact(providerId, stateId, "standardRaidRewardTableCount", "Standard raid reward table count", canProjectRaidRewards ? raidRewards.Tables.Count : 0),
            ]));

        var coverageId = RecordId(providerId, "coverage", "route-input-boundaries");
        records.Add(CreateRecord(
            coverageId,
            "dynamaxAdventureCoverage",
            StableGroup("coverage", "route-input-boundaries"),
            parentRecordId: null,
            records.Count,
            "Route input coverage",
            "Route rental choices come from Adventure encounter rows. The separate rental catalog and standard raid rewards are shown as adjacent sources only. Adventure completion reward consumers are not mapped.",
            target: null,
            capability,
            [
                DerivedNullableBooleanFact(providerId, coverageId, "routeRentalRowsMapped", "Route rental rows mapped", true),
                DerivedNullableBooleanFact(providerId, coverageId, "standaloneRentalCatalogLoaded", "Standalone rental catalog loaded", canProjectRentalPokemon),
                DerivedNullableBooleanFact(providerId, coverageId, "standardRaidRewardsLoaded", "Standard raid rewards loaded", canProjectRaidRewards),
                DerivedNullableBooleanFact(providerId, coverageId, "adventureRewardConsumerMapped", "Adventure reward consumer mapped", false),
            ]));

        var entryIds = new HashSet<int>();
        foreach (var encounter in workflow.Encounters.OrderBy(encounter => encounter.EntryIndex))
        {
            if (encounter.EntryIndex < 0
                || encounter.AdventureIndex < 0
                || encounter.SpeciesId < 0
                || encounter.Form < 0
                || encounter.Level < 0
                || !entryIds.Add(encounter.EntryIndex))
            {
                throw new SemanticExploreValidationException(
                    "Dynamax Adventures returned an invalid or duplicate physical encounter identity.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var sourceIdentity = encounter.EntryIndex.ToString(CultureInfo.InvariantCulture);
            var recordId = RecordId(providerId, "encounter", sourceIdentity);
            records.Add(CreateRecord(
                recordId,
                "dynamaxAdventureEncounter",
                StableGroup("encounter", sourceIdentity),
                parentRecordId: null,
                records.Count,
                encounter.Label,
                "Verified physical Adventure encounter row. Runtime route generation is not inferred.",
                target: null,
                capability,
                [
                    SignedFact(providerId, recordId, "entryIndex", "Entry index", encounter.EntryIndex),
                    BooleanFact(providerId, recordId, "editable", "Editable", encounter.IsEditable),
                    SignedFact(providerId, recordId, "adventureIndex", "Adventure index", encounter.AdventureIndex),
                    SignedFact(providerId, recordId, "speciesId", "Species ID", encounter.SpeciesId),
                    TextFact(providerId, recordId, "species", "Species", encounter.Species),
                    SignedFact(providerId, recordId, "form", "Form", encounter.Form),
                    SignedFact(providerId, recordId, "bossTargetSpeciesId", "Boss target species ID", encounter.BossTargetSpeciesId),
                    TextFact(providerId, recordId, "bossTargetSpecies", "Boss target species", encounter.BossTargetSpecies),
                    SignedFact(providerId, recordId, "level", "Level", encounter.Level, "level"),
                    SignedFact(providerId, recordId, "ballItemId", "Ball item ID", encounter.BallItemId),
                    TextFact(providerId, recordId, "ballItem", "Ball item", encounter.BallItem),
                    EnumFact(providerId, recordId, "ability", "Ability roll", encounter.AbilityLabel),
                    EnumFact(providerId, recordId, "gigantamaxState", "Gigantamax state", encounter.GigantamaxLabel),
                    EnumFact(providerId, recordId, "version", "Version", encounter.VersionLabel),
                    EnumFact(providerId, recordId, "shinyRoll", "Shiny roll", encounter.ShinyRollLabel),
                    BooleanFact(providerId, recordId, "singleCapture", "Single capture", encounter.IsSingleCapture),
                    BooleanFact(providerId, recordId, "storyProgressGated", "Story progress gated", encounter.IsStoryProgressGated),
                    SignedFact(providerId, recordId, "guaranteedPerfectIvs", "Guaranteed perfect IVs", encounter.GuaranteedPerfectIvs),
                    EnumFact(providerId, recordId, "otGender", "OT gender", encounter.OtGenderLabel),
                    DerivedSignedFact(providerId, recordId, "moveCount", "Move count", encounter.Moves.Count),
                ]));

            var moveSlots = new HashSet<int>();
            foreach (var move in encounter.Moves.OrderBy(move => move.Slot))
            {
                if (move.Slot < 0 || move.MoveId < 0 || !moveSlots.Add(move.Slot))
                {
                    throw new SemanticExploreValidationException(
                        "Dynamax Adventures returned an invalid or duplicate move-slot identity.",
                        SemanticExploreFailureKind.InvalidData);
                }

                var moveIdentity = CompositeSourceIdentity(
                    sourceIdentity,
                    move.Slot.ToString(CultureInfo.InvariantCulture));
                var moveRecordId = RecordId(providerId, "moveSlot", moveIdentity);
                records.Add(CreateRecord(
                    moveRecordId,
                    "dynamaxAdventureMove",
                    StableGroup("encounter", sourceIdentity),
                    recordId,
                    records.Count,
                    $"Move {checked(move.Slot + 1).ToString(CultureInfo.InvariantCulture)}: {SafePresentation(move.Move, 192)}",
                    "Verified move slot stored on the physical encounter row.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, moveRecordId, "slot", "Slot", move.Slot),
                        SignedFact(providerId, moveRecordId, "moveId", "Move ID", move.MoveId),
                        TextFact(providerId, moveRecordId, "move", "Move", move.Move),
                    ]));
            }
        }

        foreach (var region in workflow.ReservedRegions
                     .OrderBy(region => region.Area, StringComparer.Ordinal)
                     .ThenBy(region => region.Offset, StringComparer.Ordinal))
        {
            var sourceIdentity = CompositeSourceIdentity(region.Area, region.Offset, region.Label);
            var recordId = RecordId(providerId, "reservedRegion", sourceIdentity);
            records.Add(CreateRecord(
                recordId,
                "dynamaxAdventureReservedRegion",
                StableGroup("reservedRegion", region.Area),
                stateId,
                records.Count,
                region.Label,
                region.Rule,
                target: null,
                capability,
                [
                    EnumFact(providerId, recordId, "area", "Area", region.Area),
                    TextFact(providerId, recordId, "offset", "Offset", region.Offset),
                    TextFact(providerId, recordId, "label", "Label", region.Label),
                    TextFact(providerId, recordId, "rule", "Rule", region.Rule),
                ]));
        }

        if (canProjectRentalPokemon)
        {
            AddStandaloneRentalPokemonRecords(
                rentalPokemon,
                providerId,
                capability,
                records);
        }

        if (canProjectRaidRewards)
        {
            AddRaidAcquisitionRecords(
                raidRewards,
                "workflow.raidRewards",
                "raid-reward-table",
                providerId,
                capability,
                records);
        }

        return Complete(
            module,
            [workflow.Summary, rentalPokemon.Summary, raidRewards.Summary],
            workflow.Diagnostics
                .Concat(rentalPokemon.Diagnostics)
                .Concat(raidRewards.Diagnostics),
            records);
    }

    public static GameModuleData BuildRoyalCandyProgression(RoyalCandyWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.SwordShieldRoyalCandyProgression;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var levelCapCount = workflow.Workflows.Sum(item => (long)item.LevelCaps.Count);
        var stepCount = workflow.Workflows.Sum(item => (long)item.Steps.Count);
        EnsureProjectionCounts(
            checked(workflow.Workflows.Count + levelCapCount + stepCount + workflow.Checks.Count + workflow.Outputs.Count),
            checked(workflow.Workflows.Count * 7L + levelCapCount * 8L + stepCount * 3L + workflow.Checks.Count * 6L + workflow.Outputs.Count * 4L));
        var records = new BoundedRecordCollection();
        foreach (var item in workflow.Workflows.OrderBy(item => item.WorkflowId, StringComparer.Ordinal))
        {
            var target = Record(
                SemanticGameFamilyDto.SwordShield,
                "workflow.royalCandy",
                "royal-candy-workflow",
                item.WorkflowId);
            var recordId = RecordId(providerId, "workflow", item.WorkflowId);
            records.Add(CreateRecord(
                recordId,
                "royalCandyWorkflow",
                StableGroup("workflow", item.WorkflowId),
                parentRecordId: null,
                records.Count,
                item.Name,
                item.Description,
                target,
                capability,
                [
                    TextFact(providerId, recordId, "workflowId", "Workflow ID", item.WorkflowId, evidence: target),
                    EnumFact(providerId, recordId, "category", "Category", item.Category, target),
                    EnumFact(providerId, recordId, "target", "Target", item.Target, target),
                    EnumFact(providerId, recordId, "mode", "Mode", item.Mode, target),
                    SignedFact(providerId, recordId, "itemId", "Item ID", item.ItemId, evidence: target),
                    SignedFact(providerId, recordId, "templateItemId", "Template item ID", item.TemplateItemId, evidence: target),
                    EnumFact(providerId, recordId, "status", "Status", item.Status, target),
                ]));

            var levelCapSlots = new HashSet<int>();
            foreach (var levelCap in item.LevelCaps.OrderBy(levelCap => levelCap.Slot))
            {
                if (levelCap.Slot < 0
                    || levelCap.LevelCap < levelCap.MinimumLevelCap
                    || levelCap.LevelCap > levelCap.MaximumLevelCap
                    || !levelCapSlots.Add(levelCap.Slot))
                {
                    throw new SemanticExploreValidationException(
                        "Royal Candy returned an invalid or duplicate level-cap identity.",
                        SemanticExploreFailureKind.InvalidData);
                }

                var capIdentity = CompositeSourceIdentity(
                    item.WorkflowId,
                    levelCap.Slot.ToString(CultureInfo.InvariantCulture),
                    levelCap.MilestoneId);
                var capRecordId = RecordId(providerId, "levelCap", capIdentity);
                records.Add(CreateRecord(
                    capRecordId,
                    "royalCandyLevelCap",
                    StableGroup("workflow", item.WorkflowId),
                    recordId,
                    records.Count,
                    levelCap.Label,
                    "Verified stored progression milestone and level-cap selection.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, capRecordId, "slot", "Slot", levelCap.Slot),
                        TextFact(providerId, capRecordId, "milestoneId", "Milestone ID", levelCap.MilestoneId),
                        SignedFact(providerId, capRecordId, "levelCap", "Level cap", levelCap.LevelCap, "level"),
                        SignedFact(providerId, capRecordId, "minimumLevelCap", "Minimum level cap", levelCap.MinimumLevelCap, "level"),
                        SignedFact(providerId, capRecordId, "maximumLevelCap", "Maximum level cap", levelCap.MaximumLevelCap, "level"),
                        EnumFact(providerId, capRecordId, "progressKind", "Progress kind", levelCap.ProgressKind),
                        TextFact(providerId, capRecordId, "progressHash", "Progress hash", levelCap.ProgressHash),
                        NullableSignedFact(providerId, capRecordId, "workMinimum", "Work minimum", levelCap.WorkMinimum),
                    ]));
            }

            var stepIds = new HashSet<int>();
            foreach (var step in item.Steps.OrderBy(step => step.Step))
            {
                if (step.Step < 0 || !stepIds.Add(step.Step))
                {
                    throw new SemanticExploreValidationException(
                        "Royal Candy returned an invalid or duplicate workflow-step identity.",
                        SemanticExploreFailureKind.InvalidData);
                }

                var stepIdentity = CompositeSourceIdentity(
                    item.WorkflowId,
                    step.Step.ToString(CultureInfo.InvariantCulture));
                var stepRecordId = RecordId(providerId, "step", stepIdentity);
                records.Add(CreateRecord(
                    stepRecordId,
                    "royalCandyStep",
                    StableGroup("workflow", item.WorkflowId),
                    recordId,
                    records.Count,
                    step.Label,
                    step.Description,
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, stepRecordId, "step", "Step", step.Step),
                        TextFact(providerId, stepRecordId, "label", "Label", step.Label),
                        TextFact(providerId, stepRecordId, "description", "Description", step.Description),
                    ]));
            }
        }

        foreach (var check in workflow.Checks.OrderBy(check => check.CheckId, StringComparer.Ordinal))
        {
            var sourceIdentity = RoyalCandyCheckSourceIdentity(check.CheckId);
            var target = string.Equals(sourceIdentity, check.CheckId, StringComparison.Ordinal)
                ? Record(
                    SemanticGameFamilyDto.SwordShield,
                    "workflow.royalCandy",
                    "royal-candy-check",
                    check.CheckId)
                : null;
            var recordId = RecordId(providerId, "check", sourceIdentity);
            records.Add(CreateRecord(
                recordId,
                "royalCandyCheck",
                StableGroup("workflow", check.WorkflowId),
                parentRecordId: null,
                records.Count,
                check.Target,
                check.Message,
                target,
                capability,
                [
                    TextFact(providerId, recordId, "checkId", "Check ID", check.CheckId, evidence: target),
                    TextFact(providerId, recordId, "workflowId", "Workflow ID", check.WorkflowId, evidence: target),
                    EnumFact(providerId, recordId, "status", "Status", check.Status, target),
                    EnumFact(providerId, recordId, "area", "Area", check.Area, target),
                    TextFact(providerId, recordId, "target", "Target", check.Target, evidence: target),
                    TextFact(providerId, recordId, "message", "Message", check.Message, evidence: target),
                ]));
        }

        foreach (var output in workflow.Outputs.OrderBy(output => output.OutputId, StringComparer.Ordinal))
        {
            var outputSourceIdentity = RoyalCandyOutputSourceIdentity(
                output.OutputId,
                output.WorkflowId);
            var recordId = RecordId(providerId, "output", outputSourceIdentity);
            records.Add(CreateRecord(
                recordId,
                "royalCandyOutput",
                StableGroup("workflow", output.WorkflowId),
                parentRecordId: null,
                records.Count,
                output.OutputId,
                output.Description,
                target: null,
                capability,
                [
                    TextFact(providerId, recordId, "outputId", "Output ID", output.OutputId),
                    TextFact(providerId, recordId, "workflowId", "Workflow ID", output.WorkflowId),
                    EnumFact(providerId, recordId, "outputKind", "Output kind", output.OutputKind),
                    EnumFact(providerId, recordId, "status", "Status", output.Status),
                ]));
        }

        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildBattleCafeRewards(
        SwordShieldBattleCafeRewardSourceDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var module = GameModuleDto.SwordShieldBattleCafeRewards;
        if (source.UnavailableReasonCode is { } unavailableReasonCode)
        {
            return SourceUnavailable(
                module,
                ValidateBattleCafeUnavailableReason(unavailableReasonCode));
        }

        const int expectedRewardCount = 23;
        if (source.Rewards.Count != expectedRewardCount)
        {
            throw new SemanticExploreValidationException(
                "Battle Cafe rewards returned an unsupported physical row count.",
                SemanticExploreFailureKind.InvalidData);
        }

        var rows = new Dictionary<int, SwordShieldBattleCafeRewardEntryDto>();
        var itemIds = new HashSet<int>();
        foreach (var reward in source.Rewards)
        {
            if (reward.RowIndex is < 1 or > expectedRewardCount
                || reward.ItemId <= 0
                || string.IsNullOrWhiteSpace(reward.ItemName)
                || reward.ItemName.Length > 512
                || ContainsLocalPathSignature(reward.ItemName)
                || reward.DwightPercent is < 0 or > 100
                || reward.BernardPercent is < 0 or > 100
                || reward.RichardPercent is < 0 or > 100
                || !rows.TryAdd(reward.RowIndex, reward)
                || !itemIds.Add(reward.ItemId))
            {
                throw new SemanticExploreValidationException(
                    "Battle Cafe rewards returned an invalid or duplicate physical row.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        if (!rows.Keys.Order().SequenceEqual(Enumerable.Range(1, expectedRewardCount)))
        {
            throw new SemanticExploreValidationException(
                "Battle Cafe rewards returned a non-contiguous physical row set.",
                SemanticExploreFailureKind.InvalidData);
        }

        var branches = new[]
        {
            new BattleCafeBranch("dwight", "Cafe Master Dwight rewards", "241, 242", 2,
                static reward => reward.DwightPercent),
            new BattleCafeBranch("bernard", "Cafe Master Bernard rewards", "243", 1,
                static reward => reward.BernardPercent),
            new BattleCafeBranch("richard", "Cafe Master Richard rewards", "244", 1,
                static reward => reward.RichardPercent),
        };
        foreach (var branch in branches)
        {
            if (source.Rewards.Sum(branch.Percentage) != 100)
            {
                throw new SemanticExploreValidationException(
                    "Battle Cafe reward percentages do not total 100 for every verified owner branch.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        EnsureProjectionCounts(
            checked(branches.LongLength * (1L + expectedRewardCount)),
            checked(branches.LongLength * 3L + branches.LongLength * expectedRewardCount * 5L));
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var records = new BoundedRecordCollection();
        foreach (var branch in branches)
        {
            var branchRecordId = RecordId(providerId, "ownerBranch", branch.Key);
            records.Add(CreateRecord(
                branchRecordId,
                "battleCafeOwnerBranch",
                StableGroup("ownerBranch", branch.Key),
                parentRecordId: null,
                records.Count,
                branch.Title,
                "Verified item-selection percentages for this trainer-owner branch. Runtime scene availability is outside this coverage.",
                target: null,
                capability,
                [
                    TextFact(providerId, branchRecordId, "trainerTypeIds", "Trainer type IDs", branch.TrainerTypeIds),
                    DerivedSignedFact(providerId, branchRecordId, "ownerCount", "Owner count", branch.OwnerCount),
                    DerivedSignedFact(providerId, branchRecordId, "percentageTotal", "Percentage total", 100, "percent"),
                ]));

            foreach (var reward in source.Rewards.OrderBy(reward => reward.RowIndex))
            {
                var rowIdentity = CompositeSourceIdentity(
                    branch.Key,
                    reward.RowIndex.ToString(CultureInfo.InvariantCulture));
                var rewardRecordId = RecordId(providerId, "rewardChance", rowIdentity);
                records.Add(CreateRecord(
                    rewardRecordId,
                    "battleCafeRewardChance",
                    StableGroup("ownerBranch", branch.Key),
                    branchRecordId,
                    records.Count,
                    reward.ItemName,
                    "Verified percentage stored on this physical reward row for the selected owner branch.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, rewardRecordId, "rowIndex", "Row index", reward.RowIndex),
                        SignedFact(providerId, rewardRecordId, "itemId", "Item ID", reward.ItemId),
                        TextFact(providerId, rewardRecordId, "itemName", "Item", reward.ItemName),
                        SignedFact(providerId, rewardRecordId, "percentage", "Percentage", branch.Percentage(reward), "percent"),
                        TextFact(providerId, rewardRecordId, "ownerBranch", "Owner branch", branch.Key),
                    ]));
            }
        }

        return Complete(module, Array.Empty<ApiDiagnostic>(), records);
    }

    public static GameModuleData BuildEventAssignments(
        SwordShieldTrainerTypeEventAssignmentSourceDto source,
        ExeFsPatchWorkflowDto executableWorkflow)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(executableWorkflow);
        var module = GameModuleDto.SwordShieldEventAssignments;
        var executableUnavailable = WorkflowUnavailable(
            module,
            executableWorkflow.Summary,
            executableWorkflow.Diagnostics);
        if (executableUnavailable is not null)
        {
            return executableUnavailable;
        }

        var requiredExecutableChecks = new[]
        {
            "exefs-main-compatibility:supported-build",
            "exefs-main-compatibility:selected-game",
        };
        var checksById = new Dictionary<string, ExeFsPatchCheckRecordDto>(StringComparer.Ordinal);
        foreach (var check in executableWorkflow.Checks)
        {
            if (string.IsNullOrWhiteSpace(check.CheckId)
                || !checksById.TryAdd(check.CheckId, check))
            {
                throw new SemanticExploreValidationException(
                    "Event assignment executable evidence contains a duplicate or invalid check identity.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        if (requiredExecutableChecks.Any(checkId =>
                !checksById.TryGetValue(checkId, out var check)
                || !string.Equals(check.Status, "Pass", StringComparison.Ordinal)))
        {
            return SourceUnavailable(
                module,
                "trainer-type-event-executable-build-unverified");
        }

        if (source.UnavailableReasonCode is { } unavailableReasonCode)
        {
            return SourceUnavailable(
                module,
                ValidateTrainerTypeEventUnavailableReason(unavailableReasonCode));
        }

        const int expectedAssignmentCount = 254;
        if (source.Assignments.Count != expectedAssignmentCount)
        {
            throw new SemanticExploreValidationException(
                "Trainer type event assignments returned an unsupported physical row count.",
                SemanticExploreFailureKind.InvalidData);
        }

        var assignments = new Dictionary<int, SwordShieldTrainerTypeEventAssignmentDto>();
        foreach (var assignment in source.Assignments)
        {
            if (assignment.TrainerTypeId is < 0 or >= expectedAssignmentCount
                || !IsVerifiedTrainerTypeEventName(assignment.EventName)
                || !assignments.TryAdd(assignment.TrainerTypeId, assignment))
            {
                throw new SemanticExploreValidationException(
                    "Trainer type event assignments returned an invalid or duplicate physical row.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        if (!assignments.Keys.Order().SequenceEqual(Enumerable.Range(0, expectedAssignmentCount)))
        {
            throw new SemanticExploreValidationException(
                "Trainer type event assignments returned a non-contiguous physical row set.",
                SemanticExploreFailureKind.InvalidData);
        }

        var groups = source.Assignments
            .GroupBy(assignment => assignment.EventName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        EnsureProjectionCounts(
            checked(groups.LongLength + expectedAssignmentCount),
            checked(groups.LongLength * 2L + expectedAssignmentCount * 3L));
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var records = new BoundedRecordCollection();
        var groupRecordIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var groupRecordId = RecordId(providerId, "event", group.Key);
            groupRecordIds.Add(group.Key, groupRecordId);
            records.Add(CreateRecord(
                groupRecordId,
                "trainerTypeEventGroup",
                StableGroup("event", group.Key),
                parentRecordId: null,
                records.Count,
                group.Key,
                "Verified normal trainer-type actor event assignment group. Scene-specific assignments and runtime audio resolution are outside this coverage.",
                target: null,
                capability,
                [
                    TextFact(providerId, groupRecordId, "eventName", "Event", group.Key),
                    DerivedSignedFact(providerId, groupRecordId, "assignmentCount", "Assignment count", group.Count()),
                ]));
        }

        foreach (var assignment in source.Assignments.OrderBy(assignment => assignment.TrainerTypeId))
        {
            var sourceIdentity = assignment.TrainerTypeId.ToString(CultureInfo.InvariantCulture);
            var recordId = RecordId(providerId, "trainerType", sourceIdentity);
            records.Add(CreateRecord(
                recordId,
                "trainerTypeEventAssignment",
                StableGroup("event", assignment.EventName),
                groupRecordIds[assignment.EventName],
                records.Count,
                $"Trainer type {assignment.TrainerTypeId.ToString("000", CultureInfo.InvariantCulture)}",
                "Verified fixed-field event assignment used by the normal trainer-type actor route.",
                target: null,
                capability,
                [
                    SignedFact(providerId, recordId, "trainerTypeId", "Trainer type ID", assignment.TrainerTypeId),
                    TextFact(providerId, recordId, "eventName", "Event", assignment.EventName),
                    BooleanFact(providerId, recordId, "layered", "Layered source", assignment.IsLayered),
                ]));
        }

        return Complete(module, executableWorkflow.Diagnostics, records);
    }

    public static GameModuleData BuildEncounterCompatibility(
        PokemonWorkflowDto pokemonWorkflow,
        EncounterCompatibilityWorkflowDto compatibilityWorkflow)
    {
        ArgumentNullException.ThrowIfNull(pokemonWorkflow);
        ArgumentNullException.ThrowIfNull(compatibilityWorkflow);
        var module = GameModuleDto.LegendsZaEncounterCompatibility;
        var pokemonUnavailable = WorkflowUnavailable(
            module,
            pokemonWorkflow.Summary,
            pokemonWorkflow.Diagnostics);
        if (pokemonUnavailable is not null)
        {
            return pokemonUnavailable;
        }

        if (compatibilityWorkflow.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == ApiDiagnosticSeverity.Error))
        {
            return SourceUnavailable(
                module,
                "workflow-source-invalid",
                ScrubDiagnostics(module, compatibilityWorkflow.Diagnostics));
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var rulesById = new Dictionary<string, EncounterCompatibilityRuleDto>(StringComparer.Ordinal);
        foreach (var rule in compatibilityWorkflow.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId)
                || !rulesById.TryAdd(rule.RuleId, rule)
                || rule.ActionIds.Count != rule.ActionIds.Distinct().Count())
            {
                throw new SemanticExploreValidationException(
                    "The encounter compatibility workflow returned an invalid or duplicate rule identity.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        var filteredRules = rulesById.Values
            .Where(rule => rule.Policy == EncounterCompatibilityPolicyDto.FilterByVerifiedPair)
            .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
            .ToArray();
        var portableRules = rulesById.Values
            .Where(rule => rule.Policy == EncounterCompatibilityPolicyDto.PreserveForEveryReplacement)
            .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
            .ToArray();
        if (filteredRules.Length == 0 || portableRules.Length == 0)
        {
            throw new SemanticExploreValidationException(
                "The encounter compatibility workflow omitted a required compatibility policy.",
                SemanticExploreFailureKind.InvalidData);
        }

        var attachmentGroupsByPair = new Dictionary<(int SpeciesId, int Form), List<string>>();
        long filteredPairCount = 0;
        foreach (var rule in filteredRules)
        {
            var seenPairs = new HashSet<(int SpeciesId, int Form)>();
            foreach (var pair in rule.CompatiblePairs)
            {
                var key = ValidateCompatibilityPair(pair);
                if (!seenPairs.Add(key))
                {
                    throw new SemanticExploreValidationException(
                        "The encounter compatibility workflow returned a duplicate attachment pair.",
                        SemanticExploreFailureKind.InvalidData);
                }

                if (!pair.ObservedInBasePlacement && !pair.VerifiedExtension)
                {
                    throw new SemanticExploreValidationException(
                        "An attachment compatibility pair has no verified evidence classification.",
                        SemanticExploreFailureKind.InvalidData);
                }

                if (!attachmentGroupsByPair.TryGetValue(key, out var groups))
                {
                    groups = [];
                    attachmentGroupsByPair.Add(key, groups);
                }

                groups.Add(rule.RuleId);
                filteredPairCount = checked(filteredPairCount + 1);
            }
        }

        var cityPairs = new HashSet<(int SpeciesId, int Form)>();
        foreach (var pair in compatibilityWorkflow.CityBehaviorPairs)
        {
            var key = ValidateCompatibilityPair(pair);
            if (!pair.ObservedInBasePlacement || pair.VerifiedExtension || !cityPairs.Add(key))
            {
                throw new SemanticExploreValidationException(
                    "The encounter compatibility workflow returned an invalid or duplicate city pair.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        var replacementCandidates = pokemonWorkflow.Pokemon
            .Where(pokemon => pokemon.Personal.IsPresentInGame)
            .GroupBy(pokemon => (pokemon.SpeciesId, pokemon.Form))
            .OrderBy(group => group.Key.SpeciesId)
            .ThenBy(group => group.Key.Form)
            .Select(group =>
            {
                var names = group.Select(candidate => candidate.Name).Distinct(StringComparer.Ordinal).ToArray();
                var formLabels = group.Select(candidate => candidate.FormLabel).Distinct(StringComparer.Ordinal).ToArray();
                if (names.Length != 1 || formLabels.Length != 1)
                {
                    throw new SemanticExploreValidationException(
                        "The Pokemon workflow returned ambiguous replacement-candidate labels.",
                        SemanticExploreFailureKind.InvalidData);
                }

                return group.OrderBy(candidate => candidate.PersonalId).First();
            })
            .ToArray();
        if (replacementCandidates.Length == 0)
        {
            return SourceUnavailable(module, "workflow-source-unavailable");
        }

        var projectedRecordCount = checked(
            2L
            + rulesById.Count
            + filteredPairCount
            + cityPairs.Count
            + replacementCandidates.LongLength);
        var projectedFactCount = checked(
            6L
            + rulesById.Count * 4L
            + filteredPairCount * 4L
            + cityPairs.Count * 2L
            + replacementCandidates.LongLength * 6L);
        EnsureProjectionCounts(projectedRecordCount, projectedFactCount);

        var records = new BoundedRecordCollection();
        var coverageRecordId = RecordId(providerId, "coverage", "verified-rules");
        records.Add(CreateRecord(
            coverageRecordId,
            "compatibilityCoverage",
            StableGroup("coverage", "verified-rules"),
            parentRecordId: null,
            records.Count,
            "Verified replacement compatibility",
            "Coverage is limited to immutable base city observations, verified geometry-bound attachment pairs, and portable behaviors preserved by the editor. Absence is not proof of runtime incompatibility.",
            target: null,
            capability,
            [
                DerivedSignedFact(providerId, coverageRecordId, "candidateCount", "Replacement candidate count", replacementCandidates.Length),
                DerivedSignedFact(providerId, coverageRecordId, "cityPairCount", "Observed city pair count", cityPairs.Count),
                DerivedSignedFact(providerId, coverageRecordId, "portableBehaviorCount", "Portable behavior count", portableRules.Length),
                DerivedSignedFact(providerId, coverageRecordId, "attachmentGroupCount", "Filtered attachment group count", filteredRules.Length),
                DerivedSignedFact(providerId, coverageRecordId, "attachmentPairCount", "Attachment pair count", filteredPairCount),
            ]));

        foreach (var rule in portableRules.Concat(filteredRules))
        {
            var ruleRecordId = RecordId(providerId, "rule", rule.RuleId);
            var actionIds = rule.ActionIds.Count == 0
                ? null
                : string.Join(", ", rule.ActionIds.Select(value =>
                    value.ToString(CultureInfo.InvariantCulture)));
            records.Add(CreateRecord(
                ruleRecordId,
                rule.Policy == EncounterCompatibilityPolicyDto.PreserveForEveryReplacement
                    ? "portableBehavior"
                    : "attachmentGroup",
                StableGroup("rule", rule.RuleId),
                coverageRecordId,
                records.Count,
                rule.DisplayName,
                rule.Policy == EncounterCompatibilityPolicyDto.PreserveForEveryReplacement
                    ? "This verified base behavior is restored for every replacement candidate. Runtime animation quality and placement lifecycle are outside this coverage."
                    : "This geometry-bound attachment is retained only for verified species and form pairs. Unlisted pairs are filtered without declaring them runtime-incompatible.",
                target: null,
                capability,
                [
                    EnumFact(providerId, ruleRecordId, "policy", "Policy", rule.Policy.ToString()),
                    DerivedSignedFact(providerId, ruleRecordId, "compatiblePairCount", "Compatible pair count", rule.CompatiblePairs.Count),
                    BooleanFact(providerId, ruleRecordId, "hasTagSelector", "Has tag selector", rule.HasTagSelector),
                    NullableTextFact(providerId, ruleRecordId, "actionIds", "Action IDs", actionIds),
                ]));

            foreach (var pair in rule.CompatiblePairs
                         .OrderBy(pair => pair.SpeciesId)
                         .ThenBy(pair => pair.Form))
            {
                var pairIdentity = PairIdentity(pair.SpeciesId, pair.Form);
                var pairRecordId = RecordId(providerId, "attachmentPair", $"{rule.RuleId}:{pairIdentity}");
                records.Add(CreateRecord(
                    pairRecordId,
                    "attachmentPair",
                    StableGroup("rule", rule.RuleId),
                    ruleRecordId,
                    records.Count,
                    $"Species {pair.SpeciesId.ToString(CultureInfo.InvariantCulture)}, form {pair.Form.ToString(CultureInfo.InvariantCulture)}",
                    "This exact species and form pair has verified coverage for the attachment group.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, pairRecordId, "speciesId", "Species ID", pair.SpeciesId),
                        SignedFact(providerId, pairRecordId, "form", "Form", pair.Form),
                        BooleanFact(providerId, pairRecordId, "observedInBasePlacement", "Observed in base placement", pair.ObservedInBasePlacement),
                        BooleanFact(providerId, pairRecordId, "verifiedExtension", "Verified extension", pair.VerifiedExtension),
                    ]));
            }
        }

        var cityRecordId = RecordId(providerId, "cityCoverage", "base-observations");
        records.Add(CreateRecord(
            cityRecordId,
            "cityBehaviorCoverage",
            StableGroup("cityCoverage", "base-observations"),
            coverageRecordId,
            records.Count,
            "City behavior observations",
            "These pairs occur in immutable base city encounters outside Wild Zones. The observation does not prove compatibility for unlisted pairs or every runtime context.",
            target: null,
            capability,
            [
                DerivedSignedFact(providerId, cityRecordId, "observedPairCount", "Observed pair count", cityPairs.Count),
            ]));
        foreach (var pair in cityPairs.OrderBy(pair => pair.SpeciesId).ThenBy(pair => pair.Form))
        {
            var pairIdentity = PairIdentity(pair.SpeciesId, pair.Form);
            var pairRecordId = RecordId(providerId, "cityPair", pairIdentity);
            records.Add(CreateRecord(
                pairRecordId,
                "cityBehaviorPair",
                StableGroup("cityCoverage", "base-observations"),
                cityRecordId,
                records.Count,
                $"Species {pair.SpeciesId.ToString(CultureInfo.InvariantCulture)}, form {pair.Form.ToString(CultureInfo.InvariantCulture)}",
                "This exact pair is observed in immutable base city encounters outside Wild Zones.",
                target: null,
                capability,
                [
                    SignedFact(providerId, pairRecordId, "speciesId", "Species ID", pair.SpeciesId),
                    SignedFact(providerId, pairRecordId, "form", "Form", pair.Form),
                ]));
        }

        foreach (var candidate in replacementCandidates)
        {
            var pair = (candidate.SpeciesId, candidate.Form);
            var pairIdentity = PairIdentity(pair.SpeciesId, pair.Form);
            var candidateRecordId = RecordId(providerId, "candidate", pairIdentity);
            var groups = attachmentGroupsByPair.GetValueOrDefault(pair, [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var title = string.IsNullOrWhiteSpace(candidate.FormLabel)
                ? candidate.Name
                : $"{candidate.Name} ({candidate.FormLabel})";
            var cityObserved = cityPairs.Contains(pair);
            records.Add(CreateRecord(
                candidateRecordId,
                "replacementCandidate",
                StableGroup("candidate", pairIdentity),
                coverageRecordId,
                records.Count,
                title,
                cityObserved
                    ? "This exact replacement pair is observed in immutable base city encounters. Only the listed attachment groups and portable behavior rules are covered."
                    : "This exact replacement pair is not observed in immutable base city encounters. That absence is a coverage limit, not proof of runtime incompatibility.",
                target: null,
                capability,
                [
                    SignedFact(providerId, candidateRecordId, "speciesId", "Species ID", candidate.SpeciesId),
                    SignedFact(providerId, candidateRecordId, "form", "Form", candidate.Form),
                    BooleanFact(providerId, candidateRecordId, "cityBehaviorObserved", "City behavior observed", cityObserved),
                    DerivedSignedFact(providerId, candidateRecordId, "portableBehaviorCount", "Portable behavior count", portableRules.Length),
                    DerivedSignedFact(providerId, candidateRecordId, "attachmentGroupCount", "Compatible attachment group count", groups.Length),
                    NullableTextFact(providerId, candidateRecordId, "attachmentGroups", "Compatible attachment groups", groups.Length == 0 ? null : string.Join(", ", groups)),
                ]));
        }

        return Complete(
            module,
            pokemonWorkflow.Summary,
            pokemonWorkflow.Diagnostics.Concat(compatibilityWorkflow.Diagnostics).ToArray(),
            records);
    }

    public static GameModuleData BuildAlphaMoveDistribution(PokemonWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.LegendsZaAlphaMoveDistribution;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var mappings = workflow.Pokemon
            .Where(pokemon => pokemon.AlphaMove?.HasMapping == true)
            .OrderBy(pokemon => pokemon.PersonalId)
            .ToArray();
        if (mappings.Select(mapping => mapping.PersonalId).Distinct().Count() != mappings.Length)
        {
            throw new SemanticExploreValidationException(
                "The Pokemon workflow returned duplicate alpha-move row identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        var optionSets = new Dictionary<string, IReadOnlyList<PokemonEditableFieldOptionDto>>(
            StringComparer.Ordinal);
        var mappingOptionSetIds = new Dictionary<int, string>();
        foreach (var mapping in mappings)
        {
            var alphaMove = mapping.AlphaMove!;
            var optionIds = new HashSet<int>();
            foreach (var option in alphaMove.Options)
            {
                if (option.Value <= 0 || !optionIds.Add(option.Value))
                {
                    throw new SemanticExploreValidationException(
                        "An alpha-move mapping returned an invalid or duplicate option.",
                        SemanticExploreFailureKind.InvalidData);
                }
            }

            var optionSetSource = alphaMove.Options.Count == 0
                ? "empty"
                : string.Join(",", alphaMove.Options
                    .Select(option => option.Value)
                    .Order()
                    .Select(value => value.ToString(CultureInfo.InvariantCulture)));
            var optionSetId = StableComponent(optionSetSource);
            if (optionSets.TryGetValue(optionSetId, out var existing))
            {
                if (!existing.OrderBy(option => option.Value)
                    .SequenceEqual(alphaMove.Options.OrderBy(option => option.Value)))
                {
                    throw new SemanticExploreValidationException(
                        "Two alpha-move option sets collided under stable identity.",
                        SemanticExploreFailureKind.InvalidData);
                }
            }
            else
            {
                optionSets.Add(optionSetId, alphaMove.Options.ToArray());
            }

            mappingOptionSetIds.Add(mapping.PersonalId, optionSetId);
        }

        var optionCount = optionSets.Sum(set => (long)set.Value.Count);
        EnsureProjectionCounts(
            checked(mappings.LongLength + optionSets.Count + optionCount),
            checked(mappings.LongLength * 12L + optionSets.Count + optionCount * 2L));
        var records = new BoundedRecordCollection();
        foreach (var mapping in mappings)
        {
            var alphaMove = mapping.AlphaMove!;
            var target = Record(
                SemanticGameFamilyDto.LegendsZA,
                "workflow.pokemon",
                "pokemon",
                mapping.PersonalId.ToString(CultureInfo.InvariantCulture));
            var recordId = RecordId(
                providerId,
                "mapping",
                mapping.PersonalId.ToString(CultureInfo.InvariantCulture));
            var optionSetId = mappingOptionSetIds[mapping.PersonalId];
            records.Add(CreateRecord(
                recordId,
                "alphaMoveMapping",
                StableGroup("optionSet", optionSetId),
                parentRecordId: null,
                records.Count,
                string.IsNullOrWhiteSpace(mapping.FormLabel)
                    ? mapping.Name
                    : $"{mapping.Name} ({mapping.FormLabel})",
                "Exact alpha-move mapping and the verified option set shared by this species and form. Adding mappings and runtime selection behavior are outside this coverage.",
                target,
                capability,
                [
                    SignedFact(providerId, recordId, "personalId", "Personal row ID", mapping.PersonalId, evidence: target),
                    SignedFact(providerId, recordId, "speciesId", "Species ID", mapping.SpeciesId, evidence: target),
                    SignedFact(providerId, recordId, "form", "Form", mapping.Form, evidence: target),
                    NullableSignedFact(providerId, recordId, "moveId", "Alpha move ID", alphaMove.MoveId, evidence: target),
                    NullableTextFact(providerId, recordId, "moveName", "Alpha move", alphaMove.MoveName, target),
                    NullableSignedFact(providerId, recordId, "vanillaMoveId", "Base alpha move ID", alphaMove.VanillaMoveId, evidence: target),
                    BooleanFact(providerId, recordId, "canEdit", "Can edit", alphaMove.CanEdit, target),
                    NullableTextFact(providerId, recordId, "blockedReason", "Edit limitation", alphaMove.BlockedReason, target),
                    BooleanFact(providerId, recordId, "differsFromVanilla", "Differs from base", alphaMove.DiffersFromVanilla, target),
                    BooleanFact(providerId, recordId, "canRevertToVanilla", "Can restore base", alphaMove.CanRevertToVanilla, target),
                    NullableTextFact(providerId, recordId, "restoreBlockedReason", "Restore limitation", alphaMove.RestoreBlockedReason, target),
                    TextFact(providerId, recordId, "optionSetId", "Verified option set", optionSetId, evidence: target),
                ]));
        }

        foreach (var (optionSetId, options) in optionSets.OrderBy(set => set.Key, StringComparer.Ordinal))
        {
            var setRecordId = RecordId(providerId, "optionSet", optionSetId);
            records.Add(CreateRecord(
                setRecordId,
                "alphaMoveOptionSet",
                StableGroup("optionSet", optionSetId),
                parentRecordId: null,
                records.Count,
                $"Verified option set ({options.Count.ToString(CultureInfo.InvariantCulture)} moves)",
                "This exact option set is the intersection of verified move compatibility and available active and base move data.",
                target: null,
                capability,
                [
                    DerivedSignedFact(providerId, setRecordId, "optionCount", "Option count", options.Count),
                ]));
            foreach (var option in options.OrderBy(option => option.Value))
            {
                var optionRecordId = RecordId(
                    providerId,
                    "option",
                    $"{optionSetId}:{option.Value.ToString(CultureInfo.InvariantCulture)}");
                records.Add(CreateRecord(
                    optionRecordId,
                    "alphaMoveOption",
                    StableGroup("optionSet", optionSetId),
                    setRecordId,
                    records.Count,
                    option.Label,
                    "Verified alpha-move replacement option for every mapping linked to this option set.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, optionRecordId, "moveId", "Move ID", option.Value),
                        TextFact(providerId, optionRecordId, "moveName", "Move", option.Label),
                    ]));
            }
        }

        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildDexLayoutPlanning(PokemonWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.LegendsZaDexLayoutPlanning;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var editor = workflow.DexEditor;
        if (editor is null || !editor.CanEdit)
        {
            return SourceUnavailable(module, "workflow-source-invalid");
        }

        if (!editor.CanEditAdvanced
            || editor.ExecutableRegularCount is null
            || !IsVerifiedBuildIdentity(editor.ExecutableBuildId))
        {
            return SourceUnavailable(module, "workflow-source-invalid");
        }

        if (editor.ExecutableRegularCount.Value != editor.RegularCount)
        {
            throw new SemanticExploreValidationException(
                "Dex Layout returned an inconsistent executable boundary.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (editor.RegularCount <= 0
            || editor.HyperspaceCount <= 0
            || editor.RegularCount > int.MaxValue - editor.HyperspaceCount
            || editor.Placements.Count != editor.RegularCount + editor.HyperspaceCount
            || editor.IsVanillaLayout && (editor.CanReturnToVanilla || editor.CanSyncMegasToRegular)
            || editor.CanReturnToVanilla && editor.IsVanillaLayout)
        {
            throw new SemanticExploreValidationException(
                "Dex Layout returned an inconsistent verified state boundary.",
                SemanticExploreFailureKind.InvalidData);
        }

        var ordered = editor.Placements
            .OrderBy(placement => placement.InternalIndex)
            .ToArray();
        var speciesIds = new HashSet<int>();
        var internalIndices = new HashSet<int>();
        foreach (var placement in ordered)
        {
            if (placement.SpeciesId <= 0
                || placement.InternalIndex <= 0
                || placement.DisplayedNumber <= 0
                || placement.Label is not { Length: > 0 and <= 256 }
                || string.IsNullOrWhiteSpace(placement.Label)
                || ContainsLocalPathSignature(placement.Label)
                || placement.DexKind is not "regular" and not "hyperspace"
                || !speciesIds.Add(placement.SpeciesId)
                || !internalIndices.Add(placement.InternalIndex))
            {
                throw new SemanticExploreValidationException(
                    "Dex Layout returned an invalid or duplicate placement identity.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        var regular = ordered.Where(placement => placement.DexKind == "regular").ToArray();
        var hyperspace = ordered.Where(placement => placement.DexKind == "hyperspace").ToArray();
        if (regular.Length != editor.RegularCount
            || hyperspace.Length != editor.HyperspaceCount
            || !ordered.Select(placement => placement.InternalIndex)
                .SequenceEqual(Enumerable.Range(1, ordered.Length))
            || !regular.Select(placement => placement.InternalIndex)
                .SequenceEqual(Enumerable.Range(1, editor.RegularCount))
            || !regular.Select(placement => placement.DisplayedNumber)
                .SequenceEqual(Enumerable.Range(1, editor.RegularCount))
            || !hyperspace.Select(placement => placement.InternalIndex)
                .SequenceEqual(Enumerable.Range(editor.RegularCount + 1, editor.HyperspaceCount))
            || !hyperspace.Select(placement => placement.DisplayedNumber)
                .SequenceEqual(Enumerable.Range(1, editor.HyperspaceCount)))
        {
            throw new SemanticExploreValidationException(
                "Dex Layout placements do not match the verified contiguous boundaries.",
                SemanticExploreFailureKind.InvalidData);
        }

        EnsureProjectionCounts(
            checked(1L + ordered.LongLength),
            checked(10L + ordered.LongLength * 4L));
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var records = new BoundedRecordCollection();
        var stateRecordId = RecordId(providerId, "state", "current-layout");
        records.Add(CreateRecord(
            stateRecordId,
            "dexLayoutState",
            StableGroup("layout", "current-layout"),
            parentRecordId: null,
            records.Count,
            "Current Dex layout",
            "Verified current order, membership, Regular and Hyperspace boundary, whole-table Mega group state, and executable boundary. No movement is proposed and no per-species Mega membership is inferred.",
            target: null,
            capability,
            [
                SignedFact(providerId, stateRecordId, "regularCount", "Regular count", editor.RegularCount),
                SignedFact(providerId, stateRecordId, "hyperspaceCount", "Hyperspace count", editor.HyperspaceCount),
                DerivedSignedFact(providerId, stateRecordId, "totalSpeciesCount", "Total species count", ordered.Length),
                SignedFact(providerId, stateRecordId, "executableRegularCount", "Executable Regular boundary", editor.ExecutableRegularCount.Value),
                BooleanFact(providerId, stateRecordId, "isVanillaLayout", "Matches base layout", editor.IsVanillaLayout),
                BooleanFact(providerId, stateRecordId, "canReturnToVanilla", "Can restore base layout", editor.CanReturnToVanilla),
                BooleanFact(providerId, stateRecordId, "megaGroupsDifferFromRegular", "Mega groups differ from Regular", editor.CanSyncMegasToRegular),
                DerivedSignedFact(providerId, stateRecordId, "verifiedReferenceCount", "Verified reference count", 4),
                DerivedSignedFact(providerId, stateRecordId, "movementProposalCount", "Movement proposal count", 0),
                BooleanFact(providerId, stateRecordId, "advancedBoundaryVerified", "Advanced boundary verified", true),
            ]));

        foreach (var placement in ordered)
        {
            var sourceIdentity = placement.SpeciesId.ToString(CultureInfo.InvariantCulture);
            var recordId = RecordId(providerId, "placement", sourceIdentity);
            records.Add(CreateRecord(
                recordId,
                "dexPlacement",
                StableGroup("layout", placement.DexKind),
                stateRecordId,
                records.Count,
                placement.Label,
                "Verified current species membership and physical position. This record does not propose a move.",
                target: null,
                capability,
                [
                    SignedFact(providerId, recordId, "speciesId", "Species ID", placement.SpeciesId),
                    SignedFact(providerId, recordId, "internalIndex", "Internal index", placement.InternalIndex),
                    EnumFact(providerId, recordId, "dexKind", "Dex kind", placement.DexKind),
                    SignedFact(providerId, recordId, "displayedNumber", "Displayed number", placement.DisplayedNumber),
                ]));
        }

        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildTypeEffectivenessState(
        LegendsZaTypeEffectivenessStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var module = GameModuleDto.LegendsZaTypeEffectivenessState;
        if (!IsVerifiedBuildIdentity(state.BuildId)
            || !string.Equals(
                state.ChartOffsetHex,
                "main.ro+0x0019F2A4",
                StringComparison.Ordinal)
            || !IsVerifiedTypeEffectivenessSource(state.BaseSource, requireBase: true)
            || !IsVerifiedTypeEffectivenessSource(state.EffectiveSource, requireBase: false))
        {
            throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an invalid executable identity or source boundary.",
                SemanticExploreFailureKind.InvalidData);
        }

        var types = state.Types.OrderBy(type => type.TypeIndex).ToArray();
        if (types.Length != 18
            || !types.Select(type => type.TypeIndex).SequenceEqual(Enumerable.Range(0, 18))
            || types.Select(type => type.Label).Distinct(StringComparer.Ordinal).Count() != 18
            || types.Select(type => type.ShortLabel).Distinct(StringComparer.Ordinal).Count() != 18
            || types.Any(type =>
                string.IsNullOrWhiteSpace(type.Label)
                || string.IsNullOrWhiteSpace(type.ShortLabel)
                || type.Label.Length > 64
                || type.ShortLabel.Length > 16
                || ContainsLocalPathSignature(type.Label)
                || ContainsLocalPathSignature(type.ShortLabel)))
        {
            throw new SemanticExploreValidationException(
                "Type Effectiveness State returned incomplete, duplicate, or unsafe type definitions.",
                SemanticExploreFailureKind.InvalidData);
        }

        var cells = state.Cells
            .OrderBy(cell => cell.AttackTypeIndex)
            .ThenBy(cell => cell.DefenseTypeIndex)
            .ToArray();
        var coordinates = new HashSet<(int Attack, int Defense)>();
        if (cells.Length != 18 * 18
            || cells.Any(cell =>
                cell.AttackTypeIndex is < 0 or >= 18
                || cell.DefenseTypeIndex is < 0 or >= 18
                || !IsVerifiedTypeEffectivenessValue(cell.CurrentValue)
                || !IsVerifiedTypeEffectivenessValue(cell.BaseValue)
                || !coordinates.Add((cell.AttackTypeIndex, cell.DefenseTypeIndex)))
            || !cells.Select(cell => (cell.AttackTypeIndex, cell.DefenseTypeIndex))
                .SequenceEqual(
                    from attack in Enumerable.Range(0, 18)
                    from defense in Enumerable.Range(0, 18)
                    select (attack, defense))
            || state.DifferenceCount < 0
            || state.DifferenceCount != cells.Count(cell => cell.CurrentValue != cell.BaseValue))
        {
            throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an incomplete, duplicate, or inconsistent 18 by 18 table.",
                SemanticExploreFailureKind.InvalidData);
        }

        EnsureProjectionCounts(checked(1L + cells.LongLength), checked(8L + cells.LongLength * 9L));
        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var records = new BoundedRecordCollection();
        var stateRecordId = RecordId(providerId, "state", "current-table");
        records.Add(CreateRecord(
            stateRecordId,
            "typeEffectivenessState",
            StableGroup("typeChart", "current-table"),
            parentRecordId: null,
            records.Count,
            "Current type effectiveness table",
            "Verified current and base 18 by 18 table state. This record does not propose edits or make runtime claims.",
            target: null,
            capability,
            [
                TextFact(providerId, stateRecordId, "buildId", "Build ID", state.BuildId),
                TextFact(providerId, stateRecordId, "chartOffset", "Chart offset", state.ChartOffsetHex),
                EnumFact(providerId, stateRecordId, "baseSourceLayer", "Base source layer", TypeEffectivenessSourceLayer(state.BaseSource.SourceLayer)),
                EnumFact(providerId, stateRecordId, "effectiveSourceLayer", "Effective source layer", TypeEffectivenessSourceLayer(state.EffectiveSource.SourceLayer)),
                DerivedSignedFact(providerId, stateRecordId, "cellCount", "Cell count", cells.Length),
                DerivedSignedFact(providerId, stateRecordId, "differenceCount", "Base difference count", state.DifferenceCount),
                DerivedSignedFact(providerId, stateRecordId, "editProposalCount", "Edit proposal count", 0),
                DerivedSignedFact(providerId, stateRecordId, "runtimeClaimCount", "Runtime claim count", 0),
            ]));

        foreach (var cell in cells)
        {
            var attack = types[cell.AttackTypeIndex];
            var defense = types[cell.DefenseTypeIndex];
            var identity = string.Create(
                CultureInfo.InvariantCulture,
                $"{cell.AttackTypeIndex}:{cell.DefenseTypeIndex}");
            var recordId = RecordId(providerId, "cell", identity);
            records.Add(CreateRecord(
                recordId,
                "typeEffectivenessCell",
                StableGroup(
                    "attackType",
                    cell.AttackTypeIndex.ToString(CultureInfo.InvariantCulture)),
                stateRecordId,
                records.Count,
                $"{attack.Label} attacking {defense.Label}",
                "Verified current effectiveness and its exact base-table comparison.",
                target: null,
                capability,
                [
                    SignedFact(providerId, recordId, "attackTypeIndex", "Attack type index", cell.AttackTypeIndex),
                    TextFact(providerId, recordId, "attackType", "Attack type", attack.Label),
                    SignedFact(providerId, recordId, "defenseTypeIndex", "Defense type index", cell.DefenseTypeIndex),
                    TextFact(providerId, recordId, "defenseType", "Defense type", defense.Label),
                    SignedFact(providerId, recordId, "currentValue", "Current stored value", cell.CurrentValue),
                    EnumFact(providerId, recordId, "currentEffectiveness", "Current effectiveness", TypeEffectivenessLabel(cell.CurrentValue)),
                    SignedFact(providerId, recordId, "baseValue", "Base stored value", cell.BaseValue),
                    EnumFact(providerId, recordId, "baseEffectiveness", "Base effectiveness", TypeEffectivenessLabel(cell.BaseValue)),
                    BooleanFact(providerId, recordId, "differsFromBase", "Differs from base", cell.CurrentValue != cell.BaseValue),
                ]));
        }

        return Complete(module, Array.Empty<ApiDiagnostic>(), records);
    }

    public static GameModuleData BuildTrainerPoolSwitching(TrainerPoolsWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.LegendsZaTrainerPoolSwitching;
        if (workflow.Diagnostics.Any(diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error))
        {
            return SourceUnavailable(
                module,
                "workflow-source-invalid",
                ScrubDiagnostics(module, workflow.Diagnostics));
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var poolIds = new HashSet<string>(StringComparer.Ordinal);
        var physicalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pool in workflow.Pools)
        {
            if (string.IsNullOrWhiteSpace(pool.LogicalPoolId)
                || string.IsNullOrWhiteSpace(pool.CompatibilityGroup)
                || !poolIds.Add(pool.LogicalPoolId)
                || pool.MemberCount != pool.Members.Count
                || pool.PhysicalTableIds.Count == 0
                || pool.ReferencedPhysicalTableCount is < 0
                || pool.ReferencedPhysicalTableCount > pool.PhysicalTableIds.Count
                || pool.TotalWeight != pool.Members.Sum(member => member.Weight))
            {
                throw new SemanticExploreValidationException(
                    "The Trainer Pools workflow returned an inconsistent logical pool.",
                    SemanticExploreFailureKind.InvalidData);
            }

            if (pool.PhysicalTableIds.Any(id =>
                    string.IsNullOrWhiteSpace(id) || !physicalIds.Add(id)))
            {
                throw new SemanticExploreValidationException(
                    "The Trainer Pools workflow returned an invalid or duplicate physical mirror identity.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var memberIds = new HashSet<string>(StringComparer.Ordinal);
            if (pool.Members.Any(member =>
                    string.IsNullOrWhiteSpace(member.RawTrainerId)
                    || !memberIds.Add(member.RawTrainerId)
                    || member.Weight <= 0))
            {
                throw new SemanticExploreValidationException(
                    "The Trainer Pools workflow returned an invalid or duplicate member identity.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        if (workflow.Stats.LogicalPoolCount != workflow.Pools.Count
            || workflow.Stats.PhysicalMirrorCount != workflow.Pools.Sum(pool => pool.PhysicalTableIds.Count)
            || workflow.Stats.MemberReferenceCount
                != workflow.Pools.Sum(pool => pool.MemberCount * pool.PhysicalTableIds.Count)
            || workflow.Stats.DormantPhysicalMirrorCount
                != workflow.Pools.Sum(pool => pool.PhysicalTableIds.Count - pool.ReferencedPhysicalTableCount))
        {
            throw new SemanticExploreValidationException(
                "The Trainer Pools workflow summary does not match its bounded records.",
                SemanticExploreFailureKind.InvalidData);
        }

        var groups = workflow.Pools
            .GroupBy(pool => pool.CompatibilityGroup, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        foreach (var group in groups)
        {
            if (group.Select(pool => pool.Kind).Distinct().Count() != 1)
            {
                throw new SemanticExploreValidationException(
                    "A Trainer Pools compatibility group mixes incompatible pool kinds.",
                    SemanticExploreFailureKind.InvalidData);
            }
        }

        var memberCount = workflow.Pools.Sum(pool => (long)pool.Members.Count);
        var mirrorCount = workflow.Pools.Sum(pool => (long)pool.PhysicalTableIds.Count);
        EnsureProjectionCounts(
            checked(groups.LongLength + workflow.Pools.Count + memberCount + mirrorCount),
            checked(groups.LongLength * 4L + workflow.Pools.Count * 8L + memberCount * 5L + mirrorCount));
        var records = new BoundedRecordCollection();
        foreach (var group in groups)
        {
            var groupPools = group.OrderBy(pool => pool.LogicalPoolId, StringComparer.Ordinal).ToArray();
            var kind = groupPools[0].Kind;
            var groupRecordId = RecordId(providerId, "swapGroup", group.Key);
            records.Add(CreateRecord(
                groupRecordId,
                "trainerPoolSwapGroup",
                StableGroup("swapGroup", group.Key),
                parentRecordId: null,
                records.Count,
                kind == TrainerPoolKindDto.Story
                    ? "Story fixed-count swap group"
                    : "Infinity fixed-count swap group",
                "Pools in this group have the same verified mirror shape and may exchange existing member identities without resizing. Runtime selection and lifecycle behavior are outside this coverage.",
                target: null,
                capability,
                [
                    TextFact(providerId, groupRecordId, "compatibilityGroup", "Compatibility group", group.Key),
                    EnumFact(providerId, groupRecordId, "poolKind", "Pool kind", kind.ToString()),
                    DerivedSignedFact(providerId, groupRecordId, "poolCount", "Compatible pool count", groupPools.Length),
                    DerivedSignedFact(providerId, groupRecordId, "memberCount", "Member count", groupPools.Sum(pool => pool.MemberCount)),
                ]));

            foreach (var pool in groupPools)
            {
                var poolRecordId = RecordId(providerId, "pool", pool.LogicalPoolId);
                records.Add(CreateRecord(
                    poolRecordId,
                    "trainerPool",
                    StableGroup("swapGroup", group.Key),
                    groupRecordId,
                    records.Count,
                    pool.DisplayLabel,
                    "Logical pool projected across verified synchronized physical mirrors. Swaps preserve every row count and do not establish runtime activation or selection order.",
                    target: null,
                    capability,
                    [
                        EnumFact(providerId, poolRecordId, "poolKind", "Pool kind", pool.Kind.ToString()),
                        TextFact(providerId, poolRecordId, "compatibilityGroup", "Compatibility group", pool.CompatibilityGroup),
                        DerivedSignedFact(providerId, poolRecordId, "compatiblePoolCount", "Compatible pool count", groupPools.Length),
                        SignedFact(providerId, poolRecordId, "physicalMirrorCount", "Physical mirror count", pool.PhysicalTableIds.Count),
                        SignedFact(providerId, poolRecordId, "referencedMirrorCount", "Referenced mirror count", pool.ReferencedPhysicalTableCount),
                        DerivedSignedFact(providerId, poolRecordId, "dormantMirrorCount", "Dormant mirror count", pool.PhysicalTableIds.Count - pool.ReferencedPhysicalTableCount),
                        SignedFact(providerId, poolRecordId, "memberCount", "Fixed member count", pool.MemberCount),
                        SignedFact(providerId, poolRecordId, "totalWeight", "Total weight", pool.TotalWeight, "weight"),
                    ]));

                foreach (var physicalTableId in pool.PhysicalTableIds.OrderBy(id => id, StringComparer.Ordinal))
                {
                    var mirrorRecordId = RecordId(
                        providerId,
                        "mirror",
                        $"{pool.LogicalPoolId}:{physicalTableId}");
                    records.Add(CreateRecord(
                        mirrorRecordId,
                        "trainerPoolMirror",
                        StableGroup("pool", pool.LogicalPoolId),
                        poolRecordId,
                        records.Count,
                        physicalTableId,
                        "Exact synchronized physical mirror identity. Per-mirror runtime references are not inferred from the aggregate reference count.",
                        target: null,
                        capability,
                        [
                            TextFact(providerId, mirrorRecordId, "physicalTableId", "Physical table ID", physicalTableId),
                        ]));
                }

                for (var memberIndex = 0; memberIndex < pool.Members.Count; memberIndex++)
                {
                    var member = pool.Members[memberIndex];
                    var memberRecordId = RecordId(
                        providerId,
                        "member",
                        $"{pool.LogicalPoolId}:{member.RawTrainerId}");
                    records.Add(CreateRecord(
                        memberRecordId,
                        "trainerPoolMember",
                        StableGroup("pool", pool.LogicalPoolId),
                        poolRecordId,
                        records.Count,
                        member.DisplayName,
                        "Existing fixed-count pool member. Identity swaps preserve the row count, mirror synchronization, and stored weight.",
                        target: null,
                        capability,
                        [
                            DerivedSignedFact(providerId, memberRecordId, "position", "Pool position", checked(memberIndex + 1)),
                            SignedFact(providerId, memberRecordId, "storedRank", "Stored rank", member.StoredRank),
                            SignedFact(providerId, memberRecordId, "teamSize", "Team size", member.TeamSize),
                            SignedFact(providerId, memberRecordId, "weight", "Weight", member.Weight, "weight"),
                            SignedFact(providerId, memberRecordId, "rosterIndex", "Roster index", member.RosterIndex),
                        ]));
                }
            }
        }

        return Complete(module, workflow.Diagnostics, records);
    }

    public static GameModuleData BuildMoveVariantComparison(MovesWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var module = GameModuleDto.LegendsZaMoveVariantComparison;
        var unavailable = WorkflowUnavailable(module, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var capability = Capability(module);
        var providerId = capability.ProviderId;
        var variantCount = workflow.Moves.Sum(move => (long)move.RuntimeVariants.Count);
        var moveCount = workflow.Moves.LongCount(move => move.RuntimeVariants.Count > 0);
        EnsureProjectionCounts(
            checked(moveCount + variantCount),
            checked(moveCount * 4L + variantCount * 10L));
        var records = new BoundedRecordCollection();
        foreach (var move in workflow.Moves
                     .Where(move => move.RuntimeVariants.Count > 0)
                     .OrderBy(move => move.MoveId))
        {
            var timingCountByVariant = move.GameModuleTimingCounts.Count > 0
                ? new Dictionary<int, int>(move.GameModuleTimingCounts)
                : new Dictionary<int, int>();
            if (timingCountByVariant.Count == 0)
            {
                foreach (var timingRow in move.TimingRows)
                {
                    timingCountByVariant[timingRow.Variant] = checked(
                        timingCountByVariant.GetValueOrDefault(timingRow.Variant) + 1);
                }
            }
            var timingRowCount = timingCountByVariant.Aggregate(
                0L,
                (total, entry) => checked(total + entry.Value));

            var variantIds = new HashSet<int>();
            foreach (var variant in move.RuntimeVariants)
            {
                if (!variantIds.Add(variant.Variant))
                {
                    throw new SemanticExploreValidationException(
                        "The move workflow returned duplicate runtime variant identity.",
                        SemanticExploreFailureKind.InvalidData);
                }
            }

            var target = Record(
                SemanticGameFamilyDto.LegendsZA,
                "workflow.moves",
                "move",
                move.MoveId.ToString(CultureInfo.InvariantCulture));
            var moveRecordId = RecordId(providerId, "move", move.MoveId.ToString(CultureInfo.InvariantCulture));
            records.Add(CreateRecord(
                moveRecordId,
                "moveVariantSet",
                StableGroup("move", move.MoveId.ToString(CultureInfo.InvariantCulture)),
                parentRecordId: null,
                records.Count,
                move.Name,
                "Verified runtime variants shown side by side. Missing variants remain explicit and are not synchronized.",
                target,
                capability,
                [
                    SignedFact(providerId, moveRecordId, "moveId", "Move ID", move.MoveId, evidence: target),
                    DerivedSignedFact(providerId, moveRecordId, "variantCount", "Variant count", move.RuntimeVariants.Count, evidence: target),
                    DerivedSignedFact(providerId, moveRecordId, "timingRowCount", "Timing row count", timingRowCount, evidence: target),
                    DerivedSignedFact(providerId, moveRecordId, "playerDamageRowCount", "Player damage row count", move.PlayerDamageRows.Count, evidence: target),
                ]));

            foreach (var variant in move.RuntimeVariants.OrderBy(variant => variant.Variant))
            {
                var variantRecordId = RecordId(
                    providerId,
                    "variant",
                    $"{move.MoveId.ToString(CultureInfo.InvariantCulture)}:{variant.Variant.ToString(CultureInfo.InvariantCulture)}");
                records.Add(CreateRecord(
                    variantRecordId,
                    "moveVariant",
                    StableGroup("move", move.MoveId.ToString(CultureInfo.InvariantCulture)),
                    moveRecordId,
                    records.Count,
                    $"{SafePresentation(move.Name, 192)} - {VariantLabel(variant.Variant)}",
                    "Verified battle parameter variant. Exact repeated source rows are collapsed and their multiplicity is explicit. Consumer coverage is not inferred.",
                    target,
                    capability,
                    [
                        SignedFact(providerId, variantRecordId, "variant", "Variant", variant.Variant, evidence: target),
                        DerivedSignedFact(
                            providerId,
                            variantRecordId,
                            "sourceMultiplicity",
                            "Source row multiplicity",
                            move.GameModuleVariantMultiplicities.GetValueOrDefault(variant.Variant, 1),
                            "rows",
                            target),
                        SignedFact(providerId, variantRecordId, "type", "Type", variant.Type, evidence: target),
                        SignedFact(providerId, variantRecordId, "power", "Power", variant.Power, evidence: target),
                        SignedFact(providerId, variantRecordId, "criticalRank", "Critical rank", variant.CriticalRank, evidence: target),
                        SignedFact(providerId, variantRecordId, "conditionId", "Condition ID", variant.ConditionId, evidence: target),
                        SignedFact(providerId, variantRecordId, "conditionPercent", "Condition chance", variant.ConditionPercent, "percent", target),
                        BooleanFact(providerId, variantRecordId, "makesContact", "Makes contact", variant.MakesContact, target),
                        BooleanFact(providerId, variantRecordId, "blockedByProtect", "Blocked by Protect", variant.BlockedByProtect, target),
                        DerivedSignedFact(
                            providerId,
                            variantRecordId,
                            "timingRowCount",
                            "Timing row count",
                            timingCountByVariant.GetValueOrDefault(variant.Variant),
                            evidence: target),
                    ]));
            }
        }

        return Complete(module, workflow.Summary, workflow.Diagnostics, records);
    }

    private static void AddNpcItemGiftRecords(
        NpcItemGiftWorkflowDto workflow,
        string providerId,
        GameModuleCapabilityDto capability,
        ICollection<GameModuleRecordDto> records)
    {
        foreach (var npc in workflow.Npcs
                     .OrderBy(npc => npc.DisplayOrder)
                     .ThenBy(npc => npc.NpcId, StringComparer.Ordinal))
        {
            foreach (var gift in npc.Gifts
                         .OrderBy(gift => gift.DisplayOrder)
                         .ThenBy(gift => gift.GiftId, StringComparer.Ordinal))
            {
                if (gift.Quantity < 0 || gift.Items.Any(item => item.SlotId is null || item.ItemId < 0))
                {
                    throw new SemanticExploreValidationException(
                        "NPC item gifts returned an invalid quantity or item identity.",
                        SemanticExploreFailureKind.InvalidData);
                }

                var giftRecordId = RecordId(providerId, "npcGift", gift.GiftId);
                records.Add(CreateRecord(
                    giftRecordId,
                    "npcItemGift",
                    StableGroup("npc", gift.NpcId),
                    parentRecordId: null,
                    records.Count,
                    gift.Label,
                    "Verified item-grant operands from one existing NPC script. Runtime event order is not inferred.",
                    target: null,
                    capability,
                    [
                        TextFact(providerId, giftRecordId, "giftId", "Gift ID", gift.GiftId),
                        TextFact(providerId, giftRecordId, "npcId", "NPC ID", gift.NpcId),
                        TextFact(providerId, giftRecordId, "npcName", "NPC", gift.NpcName),
                        TextFact(providerId, giftRecordId, "location", "Location", gift.Location),
                        SignedFact(providerId, giftRecordId, "quantity", "Quantity", gift.Quantity),
                        EnumFact(providerId, giftRecordId, "status", "Status", gift.Status),
                        DerivedSignedFact(providerId, giftRecordId, "itemCount", "Item slot count", gift.Items.Count),
                    ]));

                foreach (var item in gift.Items.OrderBy(item => item.SlotId, StringComparer.Ordinal))
                {
                    var itemIdentity = CompositeSourceIdentity(gift.GiftId, item.SlotId);
                    var itemRecordId = RecordId(providerId, "npcGiftItem", itemIdentity);
                    records.Add(CreateRecord(
                        itemRecordId,
                        "npcItemGiftSlot",
                        StableGroup("npc", gift.NpcId),
                        giftRecordId,
                        records.Count,
                        item.Label,
                        "Verified physical item operand in the NPC gift script.",
                        target: null,
                        capability,
                        [
                            TextFact(providerId, itemRecordId, "slotId", "Slot ID", item.SlotId),
                            SignedFact(providerId, itemRecordId, "itemId", "Item ID", item.ItemId),
                            TextFact(providerId, itemRecordId, "itemName", "Item", item.ItemName),
                            SignedFact(providerId, itemRecordId, "vanillaItemId", "Base item ID", item.VanillaItemId),
                            TextFact(providerId, itemRecordId, "vanillaItemName", "Base item", item.VanillaItemName),
                            SignedFact(providerId, itemRecordId, "itemCell", "Script cell", item.ItemCell),
                        ]));
                }
            }
        }
    }

    private static void AddRaidAcquisitionRecords(
        RaidRewardsWorkflowDto workflow,
        string domain,
        string recordKind,
        string providerId,
        GameModuleCapabilityDto capability,
        ICollection<GameModuleRecordDto> records)
    {
        foreach (var table in workflow.Tables
                     .OrderBy(table => table.TableIndex)
                     .ThenBy(table => table.TableId, StringComparer.Ordinal))
        {
            if (table.TableIndex < 0
                || table.Rewards.Any(reward => reward.Slot < 0 || reward.ItemId < 0 || reward.Values.Count > 16))
            {
                throw new SemanticExploreValidationException(
                    "Raid rewards returned an invalid physical table or item identity.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var target = Record(
                SemanticGameFamilyDto.SwordShield,
                domain,
                recordKind,
                table.TableId);
            var tableRecordId = RecordId(providerId, "raidTable", CompositeSourceIdentity(domain, table.TableId));
            records.Add(CreateRecord(
                tableRecordId,
                "raidRewardTable",
                StableGroup("raidReward", table.RewardKind),
                parentRecordId: null,
                records.Count,
                table.DisplayName,
                "Verified raid reward table and physical item membership.",
                target,
                capability,
                [
                    TextFact(providerId, tableRecordId, "tableId", "Table ID", table.TableId, evidence: target),
                    TextFact(providerId, tableRecordId, "denId", "Den ID", table.DenId, evidence: target),
                    SignedFact(providerId, tableRecordId, "rank", "Rank", table.Rank, evidence: target),
                    EnumFact(providerId, tableRecordId, "gameVersion", "Game version", table.GameVersion, target),
                    EnumFact(providerId, tableRecordId, "rewardKind", "Reward kind", table.RewardKindLabel, target),
                    SignedFact(providerId, tableRecordId, "tableIndex", "Table index", table.TableIndex, evidence: target),
                    TextFact(providerId, tableRecordId, "sourceTableHash", "Source table hash", table.SourceTableHash, evidence: target),
                    DerivedSignedFact(providerId, tableRecordId, "rewardCount", "Reward count", table.Rewards.Count, evidence: target),
                ]));

            var rewardSlots = new HashSet<int>();
            foreach (var reward in table.Rewards.OrderBy(reward => reward.Slot))
            {
                if (!rewardSlots.Add(reward.Slot))
                {
                    throw new SemanticExploreValidationException(
                        "Raid rewards returned a duplicate physical reward slot.",
                        SemanticExploreFailureKind.InvalidData);
                }

                var rewardIdentity = CompositeSourceIdentity(
                    domain,
                    table.TableId,
                    reward.Slot.ToString(CultureInfo.InvariantCulture),
                    reward.EntryId.ToString(CultureInfo.InvariantCulture));
                var rewardRecordId = RecordId(providerId, "raidItem", rewardIdentity);
                var facts = new List<GameModuleFactDto>
                {
                    SignedFact(providerId, rewardRecordId, "slot", "Slot", reward.Slot),
                    SignedFact(providerId, rewardRecordId, "entryId", "Entry ID", reward.EntryId),
                    SignedFact(providerId, rewardRecordId, "itemId", "Item ID", reward.ItemId),
                    TextFact(providerId, rewardRecordId, "itemName", "Item", reward.ItemName),
                    SignedFact(providerId, rewardRecordId, "quantity", "Quantity", reward.Quantity),
                    SignedFact(providerId, rewardRecordId, "weight", "Weight", reward.Weight, "weight"),
                };
                for (var index = 0; index < reward.Values.Count; index++)
                {
                    facts.Add(SignedFact(
                        providerId,
                        rewardRecordId,
                        $"storedValue{checked(index + 1).ToString(CultureInfo.InvariantCulture)}",
                        $"Stored value {checked(index + 1).ToString(CultureInfo.InvariantCulture)}",
                        reward.Values[index]));
                }

                records.Add(CreateRecord(
                    rewardRecordId,
                    "raidRewardItem",
                    StableGroup("raidReward", table.RewardKind),
                    tableRecordId,
                    records.Count,
                    reward.ItemName,
                    "Verified physical reward row. Runtime roll order is not inferred.",
                    target: null,
                    capability,
                    facts));
            }
        }
    }

    private static void AddStandaloneRentalPokemonRecords(
        RentalPokemonWorkflowDto workflow,
        string providerId,
        GameModuleCapabilityDto capability,
        ICollection<GameModuleRecordDto> records)
    {
        var rentalIndexes = new HashSet<int>();
        foreach (var rental in workflow.Rentals.OrderBy(rental => rental.RentalIndex))
        {
            if (rental.RentalIndex < 0
                || rental.SpeciesId <= 0
                || rental.Form < 0
                || rental.Level < 1
                || rental.HeldItemId < 0
                || rental.BallItemId < 0
                || rental.Moves.Count > 4
                || !rentalIndexes.Add(rental.RentalIndex))
            {
                throw new SemanticExploreValidationException(
                    "Rental Pokemon returned an invalid or duplicate physical row identity.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var sourceIdentity = rental.RentalIndex.ToString(CultureInfo.InvariantCulture);
            var target = Record(
                SemanticGameFamilyDto.SwordShield,
                "workflow.rentalPokemon",
                "rental-pokemon",
                $"rental:{sourceIdentity}");
            var recordId = RecordId(providerId, "standaloneRental", sourceIdentity);
            records.Add(CreateRecord(
                recordId,
                "standaloneRentalPokemon",
                StableGroup("standaloneRental", "catalog"),
                parentRecordId: null,
                records.Count,
                rental.Label,
                "Verified standalone rental catalog row. This row is not presented as a Dynamax Adventure route choice.",
                target,
                capability,
                [
                    SignedFact(providerId, recordId, "rentalIndex", "Rental index", rental.RentalIndex, evidence: target),
                    SignedFact(providerId, recordId, "speciesId", "Species ID", rental.SpeciesId, evidence: target),
                    TextFact(providerId, recordId, "species", "Species", rental.Species, evidence: target),
                    SignedFact(providerId, recordId, "form", "Form", rental.Form, evidence: target),
                    SignedFact(providerId, recordId, "level", "Level", rental.Level, "level", target),
                    SignedFact(providerId, recordId, "heldItemId", "Held item ID", rental.HeldItemId, evidence: target),
                    NullableTextFact(providerId, recordId, "heldItem", "Held item", rental.HeldItem, target),
                    SignedFact(providerId, recordId, "ballItemId", "Ball item ID", rental.BallItemId, evidence: target),
                    TextFact(providerId, recordId, "ballItem", "Ball item", rental.BallItem, evidence: target),
                    SignedFact(providerId, recordId, "ability", "Ability slot", rental.Ability, evidence: target),
                    TextFact(providerId, recordId, "abilityLabel", "Ability", rental.AbilityLabel, evidence: target),
                    SignedFact(providerId, recordId, "nature", "Nature ID", rental.Nature, evidence: target),
                    TextFact(providerId, recordId, "natureLabel", "Nature", rental.NatureLabel, evidence: target),
                    SignedFact(providerId, recordId, "gender", "Gender ID", rental.Gender, evidence: target),
                    TextFact(providerId, recordId, "genderLabel", "Gender", rental.GenderLabel, evidence: target),
                    SignedFact(providerId, recordId, "trainerId", "Trainer ID", rental.TrainerId, evidence: target),
                    TextFact(providerId, recordId, "hash1", "Stored hash 1", rental.Hash1, evidence: target),
                    TextFact(providerId, recordId, "hash2", "Stored hash 2", rental.Hash2, evidence: target),
                    BooleanFact(providerId, recordId, "perfectIvs", "Perfect IVs", rental.HasPerfectIvs, target),
                    TextFact(providerId, recordId, "ivSummary", "IV summary", rental.IvSummary, evidence: target),
                    DerivedSignedFact(providerId, recordId, "evTotal", "EV total", RentalStatTotal(rental.Evs), evidence: target),
                    DerivedSignedFact(providerId, recordId, "ivTotal", "IV total", RentalStatTotal(rental.Ivs), evidence: target),
                    DerivedSignedFact(providerId, recordId, "sourceLayer", "Source layer", (int)rental.Provenance.SourceLayer, evidence: target),
                ]));

            var moveSlots = new HashSet<int>();
            foreach (var move in rental.Moves.OrderBy(move => move.Slot))
            {
                if (move.Slot is < 0 or > 3 || move.MoveId < 0 || !moveSlots.Add(move.Slot))
                {
                    throw new SemanticExploreValidationException(
                        "Rental Pokemon returned an invalid or duplicate physical move slot.",
                        SemanticExploreFailureKind.InvalidData);
                }

                var moveIdentity = CompositeSourceIdentity(
                    sourceIdentity,
                    move.Slot.ToString(CultureInfo.InvariantCulture));
                var moveRecordId = RecordId(providerId, "standaloneRentalMove", moveIdentity);
                records.Add(CreateRecord(
                    moveRecordId,
                    "standaloneRentalPokemonMove",
                    StableGroup("standaloneRental", "catalog"),
                    recordId,
                    records.Count,
                    $"Move {checked(move.Slot + 1).ToString(CultureInfo.InvariantCulture)}: {SafePresentation(move.Move ?? "None", 192)}",
                    "Verified physical move slot in the standalone rental catalog.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, moveRecordId, "slot", "Slot", move.Slot, evidence: target),
                        SignedFact(providerId, moveRecordId, "moveId", "Move ID", move.MoveId, evidence: target),
                        NullableTextFact(providerId, moveRecordId, "move", "Move", move.Move, target),
                    ]));
            }
        }
    }

    private static int RentalStatTotal(RentalPokemonStatsDto stats)
    {
        return checked(
            stats.HP
            + stats.Attack
            + stats.Defense
            + stats.SpecialAttack
            + stats.SpecialDefense
            + stats.Speed);
    }

    private static void AddShopAcquisitionRecords(
        ShopsWorkflowDto workflow,
        string providerId,
        GameModuleCapabilityDto capability,
        ICollection<GameModuleRecordDto> records)
    {
        foreach (var shop in workflow.Shops
                     .OrderBy(shop => shop.InventoryIndex)
                     .ThenBy(shop => shop.ShopId, StringComparer.Ordinal))
        {
            if (shop.InventoryIndex < 0
                || shop.Inventory.Any(item => item.Slot < 0 || item.ItemId < 0 || item.Price < 0))
            {
                throw new SemanticExploreValidationException(
                    "Shops returned an invalid physical inventory identity or value.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var target = Record(
                SemanticGameFamilyDto.SwordShield,
                "workflow.shops",
                "shop",
                shop.ShopId);
            var shopRecordId = RecordId(providerId, "shop", shop.ShopId);
            records.Add(CreateRecord(
                shopRecordId,
                "shop",
                StableGroup("shop", shop.ShopId),
                parentRecordId: null,
                records.Count,
                shop.Name,
                "Verified inventory membership and stored prices. Unlock timing and rotation order are not inferred.",
                target,
                capability,
                [
                    TextFact(providerId, shopRecordId, "shopId", "Shop ID", shop.ShopId, evidence: target),
                    EnumFact(providerId, shopRecordId, "kind", "Kind", shop.Kind, target),
                    TextFact(providerId, shopRecordId, "location", "Location", shop.Location, evidence: target),
                    EnumFact(providerId, shopRecordId, "currency", "Currency", shop.Currency, target),
                    SignedFact(providerId, shopRecordId, "inventoryIndex", "Inventory index", shop.InventoryIndex, evidence: target),
                    DerivedSignedFact(providerId, shopRecordId, "inventoryCount", "Inventory count", shop.Inventory.Count, evidence: target),
                ]));

            var slots = new HashSet<int>();
            foreach (var item in shop.Inventory.OrderBy(item => item.Slot))
            {
                if (!slots.Add(item.Slot))
                {
                    throw new SemanticExploreValidationException(
                        "Shops returned a duplicate physical inventory slot.",
                        SemanticExploreFailureKind.InvalidData);
                }

                var itemIdentity = CompositeSourceIdentity(
                    shop.ShopId,
                    item.Slot.ToString(CultureInfo.InvariantCulture));
                var itemRecordId = RecordId(providerId, "shopItem", itemIdentity);
                records.Add(CreateRecord(
                    itemRecordId,
                    "shopInventoryItem",
                    StableGroup("shop", shop.ShopId),
                    shopRecordId,
                    records.Count,
                    item.ItemName,
                    "Verified physical inventory row.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, itemRecordId, "slot", "Slot", item.Slot),
                        SignedFact(providerId, itemRecordId, "itemId", "Item ID", item.ItemId),
                        TextFact(providerId, itemRecordId, "itemName", "Item", item.ItemName),
                        SignedFact(providerId, itemRecordId, "price", "Price", item.Price, shop.Currency),
                        BooleanFact(providerId, itemRecordId, "knownItem", "Known item", item.IsKnownItem),
                        NullableSignedFact(providerId, itemRecordId, "stockLimit", "Stock limit", item.StockLimit),
                    ]));
            }
        }
    }

    private static void AddPlacedItemRecords(
        PlacementWorkflowDto workflow,
        string providerId,
        GameModuleCapabilityDto capability,
        ICollection<GameModuleRecordDto> records)
    {
        foreach (var item in workflow.Objects
                     .Where(item => item.ItemId is > 0)
                     .OrderBy(item => item.ObjectId, StringComparer.Ordinal))
        {
            if (item.ZoneIndex < 0
                || item.ObjectIndex < 0
                || item.Quantity < 0
                || item.Chance is < 0)
            {
                throw new SemanticExploreValidationException(
                    "Placement returned an invalid physical item identity or value.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var target = Record(
                SemanticGameFamilyDto.SwordShield,
                "workflow.placement",
                "placed-object",
                item.ObjectId);
            var recordId = RecordId(providerId, "placedItem", item.ObjectId);
            records.Add(CreateRecord(
                recordId,
                "placedItem",
                StableGroup("placedItem", item.Map),
                parentRecordId: null,
                records.Count,
                item.Label,
                "Verified placed item row. Pickup timing and runtime reachability are not inferred.",
                target,
                capability,
                [
                    TextFact(providerId, recordId, "objectId", "Object ID", item.ObjectId, evidence: target),
                    EnumFact(providerId, recordId, "objectType", "Object type", item.ObjectType, target),
                    TextFact(providerId, recordId, "map", "Map", item.Map, evidence: target),
                    SignedFact(providerId, recordId, "zoneIndex", "Zone index", item.ZoneIndex, evidence: target),
                    SignedFact(providerId, recordId, "objectIndex", "Object index", item.ObjectIndex, evidence: target),
                    SignedFact(providerId, recordId, "itemId", "Item ID", item.ItemId!.Value, evidence: target),
                    TextFact(providerId, recordId, "itemName", "Item", item.ItemName, evidence: target),
                    SignedFact(providerId, recordId, "quantity", "Quantity", item.Quantity, evidence: target),
                    NullableSignedFact(providerId, recordId, "chance", "Chance", item.Chance, "percent", target),
                ]));
        }
    }

    private static bool CanProjectWorkflow(
        WorkflowSummaryDto summary,
        IEnumerable<ApiDiagnostic> diagnostics)
    {
        return summary.Availability != WorkflowAvailabilityDto.Disabled
            && summary.Diagnostics.Concat(diagnostics)
                .All(diagnostic => diagnostic.Severity != ApiDiagnosticSeverity.Error);
    }

    private static void AddTeraRewardRecords(
        IEnumerable<TeraRaidRewardTableDto> tables,
        string providerId,
        GameModuleCapabilityDto capability,
        ICollection<GameModuleRecordDto> records)
    {
        foreach (var table in tables
                     .OrderBy(table => table.RewardKind, StringComparer.Ordinal)
                     .ThenBy(table => table.TableIndex))
        {
            var tableId = RecordId(providerId, "rewardTable", $"{table.RewardKind}:{table.TableIndex.ToString(CultureInfo.InvariantCulture)}");
            records.Add(CreateRecord(
                tableId,
                "raidRewardTable",
                StableGroup("reward", table.RewardKind),
                parentRecordId: null,
                records.Count,
                $"{SafePresentation(table.RewardKindLabel, 192)} table {table.TableIndex.ToString(CultureInfo.InvariantCulture)}",
                "Verified reward table membership; no event rotation is inferred.",
                target: null,
                capability,
                [
                    SignedFact(providerId, tableId, "tableIndex", "Table index", table.TableIndex),
                    TextFact(providerId, tableId, "tableHash", "Table hash", table.TableHash),
                    DerivedSignedFact(providerId, tableId, "rewardCount", "Reward count", table.RewardItemCount, "items"),
                ]));

            foreach (var reward in table.Rewards.OrderBy(reward => reward.Slot))
            {
                var evidence = Record(
                    SemanticGameFamilyDto.ScarletViolet,
                    "workflow.teraRaids",
                    "tera-raid-reward",
                    reward.RecordId);
                var recordId = RecordId(providerId, "reward", reward.RecordId);
                records.Add(CreateRecord(
                    recordId,
                    "raidReward",
                    StableGroup("reward", table.RewardKind),
                    tableId,
                    records.Count,
                    reward.ItemName,
                    "Verified reward slot values.",
                    target: null,
                    capability,
                    [
                        SignedFact(providerId, recordId, "slot", "Slot", reward.Slot, evidence: evidence),
                        SignedFact(providerId, recordId, "itemId", "Item ID", reward.ItemId, evidence: evidence),
                        SignedFact(providerId, recordId, "count", "Count", reward.Count, "items", evidence),
                        EnumFact(providerId, recordId, "category", "Category", reward.CategoryLabel, evidence),
                        NullableTextFact(providerId, recordId, "subject", "Subject", reward.SubjectTypeLabel, evidence),
                        NullableSignedFact(providerId, recordId, "rate", "Rate", reward.Rate, null, evidence),
                        NullableBooleanFact(providerId, recordId, "rareItem", "Rare item", reward.RareItemFlag, evidence),
                    ]));
            }
        }
    }

    private static void AddScriptedBossAction(
        ScriptedBossProfileDto profile,
        ScriptedBossActionDto action,
        IReadOnlyDictionary<int, ScriptedMoveEvidence> moves,
        string providerId,
        GameModuleCapabilityDto capability,
        string profileId,
        ICollection<GameModuleRecordDto> records)
    {
        moves.TryGetValue(action.MoveId ?? int.MinValue, out var moveByMoveId);
        var matchingMove = moveByMoveId;
        SemanticRecordRefDto? target = matchingMove is null
            ? null
            : Record(
                SemanticGameFamilyDto.LegendsZA,
                "workflow.moves",
                "move",
                matchingMove.Move.MoveId.ToString(CultureInfo.InvariantCulture));
        var actionId = RecordId(
            providerId,
            "action",
            CompositeSourceIdentity(profile.Key, action.Key));
        PlayerDamageAggregate? playerDamage = null;
        if (matchingMove is not null)
        {
            playerDamage = action.RuntimeMoveId is { } runtimeMoveId
                ? matchingMove.ByRuntimeMoveId.GetValueOrDefault(runtimeMoveId, PlayerDamageAggregate.Empty)
                : matchingMove.AllPlayerDamage;
        }
        var facts = new List<GameModuleFactDto>
        {
            EnumFact(providerId, actionId, "kind", "Action kind", action.Kind, target),
            NullableSignedFact(providerId, actionId, "selectorActionId", "Selector action ID", action.SelectorActionId, null, target),
            NullableSignedFact(providerId, actionId, "moveId", "Move ID", action.MoveId, null, target),
            NullableSignedFact(providerId, actionId, "runtimeMoveId", "Runtime move ID", action.RuntimeMoveId, null, target),
            NullableSignedFact(providerId, actionId, "variant", "Variant", action.Variant, null, target),
            BooleanFact(providerId, actionId, "usesBattleParameters", "Uses battle parameters", action.UsesBattleParameters, target),
            BooleanFact(providerId, actionId, "usesTimingParameters", "Uses timing parameters", action.UsesTimingParameters, target),
            DerivedEnumFact(providerId, actionId, "runtimeState", "Runtime state", action.RuntimeState, target),
            DerivedEnumFact(providerId, actionId, "compatibilityState", "Compatibility state", action.CompatibilityState, target),
            DerivedSignedFact(providerId, actionId, "phaseAvailabilityCount", "Phase availability count", action.PhaseAvailability.Count, evidence: target),
        };
        if (matchingMove is null)
        {
            facts.Add(NullFact(providerId, actionId, "playerDamageRowCount", "Player damage row count", null, evidence: null));
            facts.Add(NullFact(providerId, actionId, "verifiedTimelineLaunchCount", "Verified timeline launch count", null, evidence: null));
        }
        else
        {
            facts.Add(DerivedSignedFact(providerId, actionId, "playerDamageRowCount", "Player damage row count", playerDamage!.RowCount, evidence: target));
            facts.Add(playerDamage.LaunchCount is { } launchCount
                ? DerivedSignedFact(providerId, actionId, "verifiedTimelineLaunchCount", "Verified timeline launch count", launchCount, evidence: target)
                : NullFact(providerId, actionId, "verifiedTimelineLaunchCount", "Verified timeline launch count", null, target));
        }

        records.Add(CreateRecord(
            actionId,
            "scriptedBossAction",
            StableGroup("profile", profile.Key),
            profileId,
            records.Count,
            action.Name,
            action.PhaseContext ?? "Verified controller action membership.",
            target,
            capability,
            facts));
    }

    private static IReadOnlyDictionary<int, ScriptedMoveEvidence> CreateScriptedMoveEvidence(
        IReadOnlyList<MoveRecordDto> moves)
    {
        var result = new Dictionary<int, ScriptedMoveEvidence>();
        foreach (var move in moves)
        {
            if (result.ContainsKey(move.MoveId))
            {
                throw new SemanticExploreValidationException(
                    "The move workflow returned duplicate move identity.",
                    SemanticExploreFailureKind.InvalidData);
            }

            var byRuntimeMoveId = new Dictionary<int, PlayerDamageAggregate>();
            int? totalLaunchCount = 0;
            foreach (var row in move.PlayerDamageRows)
            {
                int? rowLaunchCount = row.VerifiedVanillaTimelineCatalogAvailable
                    && row.BulletMappingMatchesVerifiedVanilla
                        ? 0
                        : null;
                if (rowLaunchCount is not null)
                {
                    foreach (var invocation in row.Invocations)
                    {
                        rowLaunchCount = checked(
                            rowLaunchCount.Value + invocation.VerifiedVanillaTimelineLaunches.Count);
                    }
                }

                totalLaunchCount = totalLaunchCount is { } total && rowLaunchCount is { } rowTotal
                    ? checked(total + rowTotal)
                    : null;
                var previous = byRuntimeMoveId.GetValueOrDefault(
                    row.RuntimeMoveId,
                    PlayerDamageAggregate.Empty);
                byRuntimeMoveId[row.RuntimeMoveId] = new PlayerDamageAggregate(
                    checked(previous.RowCount + 1),
                    previous.LaunchCount is { } previousLaunches
                        && rowLaunchCount is { } currentLaunches
                        ? checked(previousLaunches + currentLaunches)
                        : null);
            }

            result.Add(
                move.MoveId,
                new ScriptedMoveEvidence(
                    move,
                    new PlayerDamageAggregate(move.PlayerDamageRows.Count, totalLaunchCount),
                    byRuntimeMoveId));
        }

        return result;
    }

    private static GameModuleData Complete(
        GameModuleDto module,
        WorkflowSummaryDto summary,
        IEnumerable<ApiDiagnostic> workflowDiagnostics,
        IReadOnlyList<GameModuleRecordDto> records)
    {
        var ordered = records
            .OrderBy(record => record.SortOrder)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = ScrubDiagnostics(module, summary.Diagnostics.Concat(workflowDiagnostics));
        ValidateProjectedBounds(ordered, diagnostics);
        if (ordered.Length == 0)
        {
            return SourceUnavailable(module, "workflow-source-unavailable", diagnostics);
        }

        var sourceDiagnostics = summary.Diagnostics.Concat(workflowDiagnostics).Distinct().ToArray();
        return new GameModuleData(
            Capability(module),
            ordered,
            diagnostics,
            Cacheable: summary.Availability != WorkflowAvailabilityDto.Disabled
                && sourceDiagnostics.All(diagnostic => diagnostic.Severity != ApiDiagnosticSeverity.Error));
    }

    private static GameModuleData Complete(
        GameModuleDto module,
        IEnumerable<WorkflowSummaryDto> summaries,
        IEnumerable<ApiDiagnostic> workflowDiagnostics,
        IReadOnlyList<GameModuleRecordDto> records)
    {
        var summaryList = summaries.ToArray();
        var ordered = records
            .OrderBy(record => record.SortOrder)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .ToArray();
        var sourceDiagnostics = summaryList
            .SelectMany(summary => summary.Diagnostics)
            .Concat(workflowDiagnostics)
            .Distinct()
            .ToArray();
        var diagnostics = ScrubDiagnostics(module, sourceDiagnostics);
        ValidateProjectedBounds(ordered, diagnostics);
        if (ordered.Length == 0)
        {
            return SourceUnavailable(module, "workflow-source-unavailable", diagnostics);
        }

        return new GameModuleData(
            Capability(module),
            ordered,
            diagnostics,
            Cacheable: summaryList.All(summary => summary.Availability != WorkflowAvailabilityDto.Disabled)
                && sourceDiagnostics.All(diagnostic => diagnostic.Severity != ApiDiagnosticSeverity.Error));
    }

    private static GameModuleData Complete(
        GameModuleDto module,
        IEnumerable<ApiDiagnostic> workflowDiagnostics,
        IReadOnlyList<GameModuleRecordDto> records)
    {
        var ordered = records
            .OrderBy(record => record.SortOrder)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .ToArray();
        var sourceDiagnostics = workflowDiagnostics.Distinct().ToArray();
        var diagnostics = ScrubDiagnostics(module, sourceDiagnostics);
        ValidateProjectedBounds(ordered, diagnostics);
        if (ordered.Length == 0)
        {
            return SourceUnavailable(module, "workflow-source-unavailable", diagnostics);
        }

        return new GameModuleData(
            Capability(module),
            ordered,
            diagnostics,
            Cacheable: sourceDiagnostics.All(diagnostic =>
                diagnostic.Severity != ApiDiagnosticSeverity.Error));
    }

    private static (int SpeciesId, int Form) ValidateCompatibilityPair(
        EncounterCompatibilityPairDto pair)
    {
        if (pair.SpeciesId <= 0 || pair.Form < 0)
        {
            throw new SemanticExploreValidationException(
                "The encounter compatibility workflow returned an invalid species or form pair.",
                SemanticExploreFailureKind.InvalidData);
        }

        return (pair.SpeciesId, pair.Form);
    }

    private static string PairIdentity(int speciesId, int form)
    {
        return $"{speciesId.ToString(CultureInfo.InvariantCulture)}:{form.ToString(CultureInfo.InvariantCulture)}";
    }

    private static GameModuleData? WorkflowUnavailable(
        GameModuleDto module,
        WorkflowSummaryDto summary,
        IEnumerable<ApiDiagnostic> workflowDiagnostics)
    {
        var diagnostics = summary.Diagnostics.Concat(workflowDiagnostics).Distinct().ToArray();
        if (summary.Availability != WorkflowAvailabilityDto.Disabled
            && diagnostics.All(diagnostic => diagnostic.Severity != ApiDiagnosticSeverity.Error))
        {
            return null;
        }

        return SourceUnavailable(
            module,
            summary.Availability == WorkflowAvailabilityDto.Disabled ? "workflow-disabled" : "workflow-source-invalid",
            ScrubDiagnostics(module, diagnostics));
    }

    private static GameModuleData SourceUnavailable(
        GameModuleDto module,
        string reasonCode,
        IReadOnlyList<ApiDiagnostic>? diagnostics = null)
    {
        var declared = Capability(module);
        return new GameModuleData(
            declared with
            {
                State = SemanticCoverageStateDto.Unavailable,
                Confidence = SemanticConfidenceDto.Unknown,
                CanQuery = false,
                ReasonCode = reasonCode,
                SupportedLayers = [],
            },
            [],
            diagnostics ??
            [
                new ApiDiagnostic(
                    ApiDiagnosticSeverity.Warning,
                    "The module could not load a bounded verified source.",
                    Domain: ModuleDomain(module))
                {
                    Code = "KM-GAME-MODULE-SOURCE-UNAVAILABLE",
                },
            ],
            Cacheable: false);
    }

    private static string ValidateBattleCafeUnavailableReason(string reasonCode)
    {
        return reasonCode switch
        {
            "battle-cafe-source-unavailable" => reasonCode,
            "battle-cafe-source-shape-unverified" => reasonCode,
            _ => throw new SemanticExploreValidationException(
                "Battle Cafe rewards returned an unsupported availability reason.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static string ValidateTrainerTypeEventUnavailableReason(string reasonCode)
    {
        return reasonCode switch
        {
            "trainer-type-event-source-incomplete" => reasonCode,
            "trainer-type-event-identity-ambiguous" => reasonCode,
            "trainer-type-event-source-unavailable" => reasonCode,
            "trainer-type-event-source-shape-unverified" => reasonCode,
            _ => throw new SemanticExploreValidationException(
                "Trainer type event assignments returned an unsupported availability reason.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static bool IsVerifiedTrainerTypeEventName(string value)
    {
        return value is { Length: > 9 and < 128 }
            && value.StartsWith("Play_bgm_", StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static bool IsVerifiedBuildIdentity(string? value)
    {
        return value is { Length: 40 }
            && value.All(character => char.IsAsciiHexDigit(character));
    }

    private static bool IsVerifiedTypeEffectivenessSource(
        LegendsZaTypeEffectivenessStateSourceDto source,
        bool requireBase)
    {
        if (!string.Equals(source.RelativePath, "exefs/main", StringComparison.Ordinal)
            || requireBase && source.SourceLayer != ProjectFileLayerDto.Base)
        {
            return false;
        }

        return source.SourceLayer switch
        {
            ProjectFileLayerDto.Base => requireBase
                ? source.FileState is
                    ProjectFileGraphEntryStateDto.BaseOnly
                    or ProjectFileGraphEntryStateDto.LayeredOverride
                : source.FileState == ProjectFileGraphEntryStateDto.BaseOnly,
            ProjectFileLayerDto.Layered => !requireBase
                && source.FileState == ProjectFileGraphEntryStateDto.LayeredOverride,
            _ => false,
        };
    }

    private static bool IsVerifiedTypeEffectivenessValue(int value)
    {
        return value is 0 or 2 or 4 or 8;
    }

    private static string TypeEffectivenessLabel(int value)
    {
        return value switch
        {
            0 => "0x",
            2 => "0.5x",
            4 => "1x",
            8 => "2x",
            _ => throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an unsupported effectiveness value.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static string TypeEffectivenessSourceLayer(ProjectFileLayerDto layer)
    {
        return layer switch
        {
            ProjectFileLayerDto.Base => "base",
            ProjectFileLayerDto.Layered => "layered",
            _ => throw new SemanticExploreValidationException(
                "Type Effectiveness State returned an unsupported source layer.",
                SemanticExploreFailureKind.InvalidData),
        };
    }

    private static GameModuleCapabilityDto Capability(GameModuleDto module)
    {
        var family = ModuleFamily(module);
        return Capabilities(family).Single(capability => capability.Module == module);
    }

    private static GameModuleCapabilityDto Available(
        GameModuleDto module,
        SemanticGameFamilyDto family,
        GameModuleMaturityDto maturity,
        string reasonCode)
    {
        return new GameModuleCapabilityDto(
            module,
            family,
            maturity,
            ProviderId(module),
            SemanticCoverageStateDto.Partial,
            SemanticConfidenceDto.Verified,
            CanQuery: true,
            reasonCode,
            EffectiveLayer);
    }

    private static GameModuleCapabilityDto Unavailable(
        GameModuleDto module,
        SemanticGameFamilyDto family,
        GameModuleMaturityDto maturity,
        string reasonCode)
    {
        return new GameModuleCapabilityDto(
            module,
            family,
            maturity,
            ProviderId(module),
            SemanticCoverageStateDto.Unavailable,
            SemanticConfidenceDto.Unknown,
            CanQuery: false,
            reasonCode,
            []);
    }

    private static string ProviderId(GameModuleDto module)
    {
        return module switch
        {
            GameModuleDto.SwordShieldRewardEcosystem => "swsh.game-modules.reward-ecosystem",
            GameModuleDto.SwordShieldExeFsCompatibility => "swsh.game-modules.exefs-compatibility",
            GameModuleDto.SwordShieldDynamaxAdventures => "swsh.game-modules.dynamax-adventures",
            GameModuleDto.SwordShieldRoyalCandyProgression => "swsh.game-modules.royal-candy-progression",
            GameModuleDto.SwordShieldBattleCafeRewards => "swsh.game-modules.battle-cafe-rewards",
            GameModuleDto.SwordShieldEventAssignments => "swsh.game-modules.event-assignments",
            GameModuleDto.ScarletVioletTeraRaidAnalysis => "sv.game-modules.tera-raid-analysis",
            GameModuleDto.ScarletVioletPackedLooseComparison => "sv.game-modules.packed-loose-comparison",
            GameModuleDto.ScarletVioletEventDataComparison => "sv.game-modules.event-data-comparison",
            GameModuleDto.ScarletVioletScenePlacementEditing => "sv.game-modules.scene-placement",
            GameModuleDto.ScarletVioletTypeEffectivenessState => "sv.game-modules.type-effectiveness-state",
            GameModuleDto.ScarletVioletStellarBehavior => "sv.game-modules.stellar-behavior",
            GameModuleDto.LegendsZaScriptedBossTimeline => "za.game-modules.scripted-boss-timeline",
            GameModuleDto.LegendsZaTrainerArchetypes => "za.game-modules.trainer-archetypes",
            GameModuleDto.LegendsZaWildSpawnExplorer => "za.game-modules.wild-spawn-explorer",
            GameModuleDto.LegendsZaEncounterCompatibility => "za.game-modules.encounter-compatibility",
            GameModuleDto.LegendsZaAlphaMoveDistribution => "za.game-modules.alpha-move-distribution",
            GameModuleDto.LegendsZaDexLayoutPlanning => "za.game-modules.dex-layout-planning",
            GameModuleDto.LegendsZaMoveVariantComparison => "za.game-modules.move-variant-comparison",
            GameModuleDto.LegendsZaTrainerPoolSwitching => "za.game-modules.trainer-pool-switching",
            GameModuleDto.LegendsZaTypeEffectivenessState => "za.game-modules.type-effectiveness-state",
            _ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
        };
    }

    internal static SemanticGameFamilyDto ModuleFamily(GameModuleDto module)
    {
        return module switch
        {
            >= GameModuleDto.SwordShieldRewardEcosystem and <= GameModuleDto.SwordShieldEventAssignments =>
                SemanticGameFamilyDto.SwordShield,
            >= GameModuleDto.ScarletVioletTeraRaidAnalysis and <= GameModuleDto.ScarletVioletStellarBehavior =>
                SemanticGameFamilyDto.ScarletViolet,
            >= GameModuleDto.LegendsZaScriptedBossTimeline and <= GameModuleDto.LegendsZaTypeEffectivenessState =>
                SemanticGameFamilyDto.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
        };
    }

    internal static string ModuleDomain(GameModuleDto module)
    {
        return module switch
        {
            GameModuleDto.SwordShieldExeFsCompatibility => "workflow.exefsPatches",
            GameModuleDto.SwordShieldDynamaxAdventures => "workflow.dynamaxAdventures",
            GameModuleDto.SwordShieldRoyalCandyProgression => "workflow.royalCandy",
            GameModuleDto.ScarletVioletTeraRaidAnalysis => "workflow.teraRaids",
            GameModuleDto.ScarletVioletTypeEffectivenessState => "workflow.typeChart",
            GameModuleDto.LegendsZaScriptedBossTimeline => "workflow.encounters",
            GameModuleDto.LegendsZaTrainerArchetypes => "workflow.trainers",
            GameModuleDto.LegendsZaWildSpawnExplorer or GameModuleDto.LegendsZaEncounterCompatibility =>
                "workflow.encounters",
            GameModuleDto.LegendsZaAlphaMoveDistribution or GameModuleDto.LegendsZaDexLayoutPlanning => "workflow.pokemon",
            GameModuleDto.LegendsZaMoveVariantComparison => "workflow.moves",
            GameModuleDto.LegendsZaTrainerPoolSwitching => "workflow.trainerPools",
            GameModuleDto.LegendsZaTypeEffectivenessState => "workflow.typeChart",
            _ => "gameModules",
        };
    }

    private static GameModuleRecordDto CreateRecord(
        string recordId,
        string recordKind,
        string? groupId,
        string? parentRecordId,
        int sortOrder,
        string title,
        string summary,
        SemanticRecordRefDto? target,
        GameModuleCapabilityDto capability,
        IReadOnlyList<GameModuleFactDto> facts)
    {
        if (facts.Count > GameModuleContract.MaximumFactsPerRecord)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module record exceeds its bounded fact limit.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        return new GameModuleRecordDto(
            StableIdentity(recordId),
            StableToken(recordKind, 64),
            groupId is null ? null : StableIdentity(groupId),
            parentRecordId is null ? null : StableIdentity(parentRecordId),
            sortOrder,
            SafePresentation(title, 256),
            SafePresentation(summary, 1_024),
            target,
            capability.State,
            capability.Confidence,
            facts);
    }

    private static GameModuleFactDto SignedFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        long value,
        string? unit = null,
        SemanticRecordRefDto? evidence = null) =>
        Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(
                SemanticValueKindDto.SignedInteger,
                value.ToString(CultureInfo.InvariantCulture),
                value.ToString(CultureInfo.InvariantCulture)),
            unit,
            evidence,
            SemanticConfidenceDto.Verified);

    private static GameModuleFactDto DerivedSignedFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        long value,
        string? unit = null,
        SemanticRecordRefDto? evidence = null) =>
        Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(
                SemanticValueKindDto.SignedInteger,
                value.ToString(CultureInfo.InvariantCulture),
                value.ToString(CultureInfo.InvariantCulture)),
            unit,
            evidence,
            SemanticConfidenceDto.Derived);

    private static GameModuleFactDto NullableSignedFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        long? value,
        string? unit = null,
        SemanticRecordRefDto? evidence = null) =>
        value is null
            ? NullFact(providerId, recordId, fieldKey, label, unit, evidence)
            : SignedFact(providerId, recordId, fieldKey, label, value.Value, unit, evidence);

    private static GameModuleFactDto DerivedNullableSignedFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        long? value,
        string? unit = null,
        SemanticRecordRefDto? evidence = null) =>
        value is null
            ? NullFact(providerId, recordId, fieldKey, label, unit, evidence)
            : DerivedSignedFact(providerId, recordId, fieldKey, label, value.Value, unit, evidence);

    private static GameModuleFactDto BooleanFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        bool value,
        SemanticRecordRefDto? evidence = null) =>
        Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(
                SemanticValueKindDto.Boolean,
                value ? "true" : "false",
                value ? "Yes" : "No"),
            unit: null,
            evidence,
            SemanticConfidenceDto.Verified);

    private static GameModuleFactDto NullableBooleanFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        bool? value,
        SemanticRecordRefDto? evidence = null) =>
        value is null
            ? NullFact(providerId, recordId, fieldKey, label, unit: null, evidence)
            : BooleanFact(providerId, recordId, fieldKey, label, value.Value, evidence);

    private static GameModuleFactDto DerivedNullableBooleanFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        bool? value,
        SemanticRecordRefDto? evidence = null) =>
        value is null
            ? NullFact(providerId, recordId, fieldKey, label, unit: null, evidence)
            : Fact(
                providerId,
                recordId,
                fieldKey,
                label,
                new SemanticScalarValueDto(
                    SemanticValueKindDto.Boolean,
                    value.Value ? "true" : "false",
                    value.Value ? "Yes" : "No"),
                unit: null,
                evidence,
                SemanticConfidenceDto.Derived);

    private static GameModuleFactDto TextFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        string value,
        string? unit = null,
        SemanticRecordRefDto? evidence = null)
    {
        if (value is null)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned a missing fact value.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (value.Length > 512)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an oversized fact value.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (ContainsLocalPathSignature(value))
        {
            return NullFact(providerId, recordId, fieldKey, label, unit, evidence);
        }

        var safe = ExactFactPresentation(value, 512);
        return Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(SemanticValueKindDto.Text, safe, safe),
            unit,
            evidence,
            SemanticConfidenceDto.Verified);
    }

    private static GameModuleFactDto NullableTextFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        string? value,
        SemanticRecordRefDto? evidence = null) =>
        value is null || value.Length <= 512 && string.IsNullOrWhiteSpace(value)
            ? NullFact(providerId, recordId, fieldKey, label, unit: null, evidence)
            : TextFact(providerId, recordId, fieldKey, label, value, evidence: evidence);

    private static GameModuleFactDto DerivedNullableTextFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        string? value,
        SemanticRecordRefDto? evidence = null)
    {
        if (value is null || value.Length <= 512 && string.IsNullOrWhiteSpace(value))
        {
            return NullFact(providerId, recordId, fieldKey, label, unit: null, evidence);
        }

        if (value.Length > 512)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an oversized fact value.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (ContainsLocalPathSignature(value))
        {
            return NullFact(providerId, recordId, fieldKey, label, unit: null, evidence);
        }

        var safe = ExactFactPresentation(value, 512);
        return Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(SemanticValueKindDto.Text, safe, safe),
            unit: null,
            evidence,
            SemanticConfidenceDto.Derived);
    }

    private static GameModuleFactDto EnumFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        string value,
        SemanticRecordRefDto? evidence = null)
    {
        if (value is null)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned a missing fact value.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (value.Length > 256)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an oversized fact value.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (ContainsLocalPathSignature(value))
        {
            return NullFact(providerId, recordId, fieldKey, label, unit: null, evidence);
        }

        var safe = ExactFactPresentation(value, 256);
        return Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(SemanticValueKindDto.Enum, safe, safe),
            unit: null,
            evidence,
            SemanticConfidenceDto.Verified);
    }

    private static GameModuleFactDto DerivedEnumFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        string value,
        SemanticRecordRefDto? evidence = null)
    {
        if (value is null)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned a missing fact value.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (value.Length > 256)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an oversized fact value.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (ContainsLocalPathSignature(value))
        {
            return NullFact(providerId, recordId, fieldKey, label, unit: null, evidence);
        }

        var safe = ExactFactPresentation(value, 256);
        return Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(SemanticValueKindDto.Enum, safe, safe),
            unit: null,
            evidence,
            SemanticConfidenceDto.Derived);
    }

    private static GameModuleFactDto NullFact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        string? unit,
        SemanticRecordRefDto? evidence) =>
        Fact(
            providerId,
            recordId,
            fieldKey,
            label,
            new SemanticScalarValueDto(SemanticValueKindDto.Null, null, "Unavailable"),
            unit,
            evidence,
            SemanticConfidenceDto.Unknown);

    private static GameModuleFactDto Fact(
        string providerId,
        string recordId,
        string fieldKey,
        string label,
        SemanticScalarValueDto value,
        string? unit,
        SemanticRecordRefDto? evidence,
        SemanticConfidenceDto confidence)
    {
        var safeField = StableToken(fieldKey, 128);
        return new GameModuleFactDto(
            StableIdentity($"{providerId}.fact.{StableComponent(recordId)}.{safeField}"),
            safeField,
            SafePresentation(label, 128),
            value with { DisplayValue = ExactFactPresentation(value.DisplayValue, 512) },
            unit is null ? null : SafePresentation(unit, 64),
            confidence,
            providerId,
            evidence is null ? [] : [evidence]);
    }

    private static SemanticRecordRefDto Record(
        SemanticGameFamilyDto family,
        string domain,
        string kind,
        string recordId,
        string? subrecordId = null)
    {
        return new SemanticRecordRefDto(
            family,
            StableToken(domain, 128, allowDots: true),
            new SemanticRecordKindDto(StableToken(kind, 64, allowDashes: true), RecordSchemaVersion),
            StableIdentity(recordId),
            subrecordId is null ? null : StableIdentity(subrecordId));
    }

    private static void ValidateProjectedBounds(
        IReadOnlyList<GameModuleRecordDto> records,
        IReadOnlyList<ApiDiagnostic> diagnostics)
    {
        var factCount = records.Sum(record => (long)record.Facts.Count);
        var evidenceCount = records.Sum(record => record.Facts.Sum(fact => (long)fact.Evidence.Count));
        var projectedBytes = EstimatePayloadBytes(records, diagnostics);
        if (records.Count > GameModuleContract.MaximumRecords
            || factCount > GameModuleContract.MaximumFacts
            || evidenceCount > GameModuleContract.MaximumEvidenceRecords
            || diagnostics.Count > GameModuleContract.MaximumDiagnostics
            || projectedBytes > MaximumProjectedBytes)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module result exceeds its bounded analysis limits.",
                SemanticExploreFailureKind.LimitExceeded);
        }
    }

    private static void EnsureProjectionCounts(long recordCount, long factCount)
    {
        if (recordCount < 0
            || factCount < 0
            || recordCount > GameModuleContract.MaximumRecords
            || factCount > GameModuleContract.MaximumFacts)
        {
            throw new SemanticExploreValidationException(
                "The game-specific module projection exceeds its bounded record or fact limit.",
                SemanticExploreFailureKind.LimitExceeded);
        }
    }

    internal static long EstimateSizeBytes(GameModuleData data)
    {
        var size = checked(EstimatePayloadBytes(data.Records, data.Diagnostics) + 512L);
        size = checked(size + StringBytes(data.Capability.ProviderId));
        size = checked(size + StringBytes(data.Capability.ReasonCode));
        return size;
    }

    private static long EstimatePayloadBytes(
        IReadOnlyList<GameModuleRecordDto> records,
        IReadOnlyList<ApiDiagnostic> diagnostics)
    {
        long size = 1_024;
        foreach (var record in records)
        {
            size = checked(size + RecordBytes(record));
        }

        foreach (var diagnostic in diagnostics)
        {
            size = checked(size + 256L);
            size = checked(size + StringBytes(diagnostic.Message));
            size = checked(size + StringBytes(diagnostic.File));
            size = checked(size + StringBytes(diagnostic.Domain));
            size = checked(size + StringBytes(diagnostic.Field));
            size = checked(size + StringBytes(diagnostic.Expected));
            size = checked(size + StringBytes(diagnostic.Code));
        }

        return size;
    }

    private static long RecordBytes(GameModuleRecordDto record)
    {
        var size = checked(
            256L
            + StringBytes(record.RecordId)
            + StringBytes(record.RecordKind)
            + StringBytes(record.GroupId)
            + StringBytes(record.ParentRecordId)
            + StringBytes(record.Title)
            + StringBytes(record.Summary)
            + RecordRefBytes(record.Target));
        foreach (var fact in record.Facts)
        {
            size = checked(size + 256L);
            size = checked(size + StringBytes(fact.FactId));
            size = checked(size + StringBytes(fact.FieldKey));
            size = checked(size + StringBytes(fact.Label));
            size = checked(size + StringBytes(fact.Value.CanonicalValue));
            size = checked(size + StringBytes(fact.Value.DisplayValue));
            size = checked(size + StringBytes(fact.Unit));
            size = checked(size + StringBytes(fact.ProviderId));
            foreach (var evidence in fact.Evidence)
            {
                size = checked(size + RecordRefBytes(evidence));
            }
        }

        return size;
    }

    private static long RecordRefBytes(SemanticRecordRefDto? record)
    {
        return record is null
            ? 8L
            : checked(
                192L
                + StringBytes(record.Domain)
                + StringBytes(record.RecordKind.Key)
                + StringBytes(record.RecordId)
                + StringBytes(record.SubrecordId));
    }

    private static long StringBytes(string? value) =>
        value is null ? 8L : checked(8L + value.Length * 6L);

    private static IReadOnlyList<ApiDiagnostic> ScrubDiagnostics(
        GameModuleDto module,
        IEnumerable<ApiDiagnostic> diagnostics)
    {
        return diagnostics
            .Distinct()
            .Take(GameModuleContract.MaximumDiagnostics)
            .Select(diagnostic => new ApiDiagnostic(
                diagnostic.Severity,
                "The owning workflow reported a diagnostic while preparing this read-only module.",
                Domain: ModuleDomain(module),
                Field: SafeDiagnosticToken(diagnostic.Field))
            {
                Code = SafeDiagnosticCode(diagnostic.Code),
            })
            .ToArray();
    }

    private static string? SafeDiagnosticToken(string? value)
    {
        return value is not null
            && value.Length <= 128
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value
            : null;
    }

    private static string? SafeDiagnosticCode(string? value)
    {
        return value is not null
            && value.Length is >= 3 and <= 128
            && value.StartsWith("KM-", StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? value
            : null;
    }

    private static string ClassifyArchetype(TrainerPokemonStatsDto evs)
    {
        return (evs.HP, evs.Attack, evs.Defense, evs.SpecialAttack, evs.SpecialDefense, evs.Speed) switch
        {
            (4, 252, 0, 0, 0, 252) => "Physical attacker",
            (4, 0, 0, 252, 0, 252) => "Special attacker",
            (85, 85, 85, 85, 85, 85) => "Balanced",
            _ => "Custom",
        };
    }

    private static string VariantLabel(int variant)
    {
        return variant switch
        {
            0 => "Normal",
            1 => "Plus",
            2 => "Boss",
            _ => $"Variant {variant.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string RecordId(string providerId, string kind, string sourceIdentity) =>
        $"{providerId}.record.{StableToken(kind, 64)}.{StableComponent(sourceIdentity)}";

    private static string StableGroup(string kind, string identity) =>
        $"{StableToken(kind, 64)}:{StableComponent(identity)}";

    private static string StableComponent(string value)
    {
        if (value is null)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module source identity is missing.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (value.Length > 4_096)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module source identity exceeds its bounded stable-key limit.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Any(IsUnsafeUnicode)
            || ContainsLocalPathSignature(value))
        {
            throw new SemanticExploreValidationException(
                "A game-specific module source identity is unsafe for publication.",
                SemanticExploreFailureKind.InvalidData);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes.AsSpan(0, 12));
    }

    private static string CompositeSourceIdentity(params string[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return string.Join('.', components.Select(StableComponent));
    }

    private static string RoyalCandyCheckSourceIdentity(string checkId)
    {
        const string relativeSourcePrefix = "royal-candy-preflight:item-text-shape:";
        if (!ContainsLocalPathSignature(checkId))
        {
            return checkId;
        }

        if (!checkId.StartsWith(relativeSourcePrefix, StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "Royal Candy returned an unsupported path-bearing check identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        var relativePath = checkId[relativeSourcePrefix.Length..];
        var segments = relativePath.Split('/');
        if (!relativePath.StartsWith("romfs/bin/message/", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains('\\')
            || segments.Any(segment => segment.Length == 0 || segment is "." or "..")
            || relativePath.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "Royal Candy returned an invalid relative source check identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        return "royal-candy-text-source-"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath)))
                [..24];
    }

    private static string RoyalCandyOutputSourceIdentity(string outputId, string workflowId)
    {
        if (!ContainsLocalPathSignature(outputId))
        {
            return outputId;
        }

        var prefix = workflowId + ":";
        if (string.IsNullOrWhiteSpace(workflowId)
            || !outputId.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new SemanticExploreValidationException(
                "Royal Candy returned an unsupported path-bearing output identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        var relativePath = outputId[prefix.Length..];
        var segments = relativePath.Split('/');
        if (!(relativePath.StartsWith("romfs/", StringComparison.Ordinal)
              || relativePath.StartsWith("exefs/", StringComparison.Ordinal)
              || relativePath.StartsWith(".km-editor/", StringComparison.Ordinal))
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains('\\')
            || segments.Any(segment => segment.Length == 0 || segment is "." or "..")
            || relativePath.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "Royal Candy returned an invalid relative output identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        return workflowId
            + "-source-"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath)))
                [..24];
    }

    private static string StableIdentity(string value)
    {
        if (value is null || value.Length > 1_024)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an unsafe stable identity.",
                value is null
                    ? SemanticExploreFailureKind.InvalidData
                    : SemanticExploreFailureKind.LimitExceeded);
        }

        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Any(character => IsUnsafeUnicode(character))
            || ContainsLocalPathSignature(value))
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an unsafe stable identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        return value;
    }

    private static string StableToken(
        string value,
        int maximumLength,
        bool allowDots = false,
        bool allowDashes = false)
    {
        if (value is null || value.Length > maximumLength)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an invalid stable token.",
                value is null
                    ? SemanticExploreFailureKind.InvalidData
                    : SemanticExploreFailureKind.LimitExceeded);
        }

        if (string.IsNullOrWhiteSpace(value)
            || !char.IsAsciiLetter(value[0])
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character != '_'
                && (!allowDots || character != '.')
                && (!allowDashes || character != '-')))
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an invalid stable token.",
                SemanticExploreFailureKind.InvalidData);
        }

        return value;
    }

    private static string SafePresentation(string value, int maximumLength)
    {
        if (value is null)
        {
            return "Unavailable";
        }

        var boundedLength = Math.Min(value.Length, maximumLength);
        if (boundedLength > 0
            && boundedLength < value.Length
            && char.IsHighSurrogate(value[boundedLength - 1]))
        {
            boundedLength--;
        }

        var bounded = value[..boundedLength];
        var safe = new string(bounded.Select(character => IsUnsafeUnicode(character) ? ' ' : character).ToArray())
            .Trim();
        if (safe.Length == 0 || ContainsLocalPathSignature(safe))
        {
            return "Unavailable";
        }

        return safe;
    }

    private static string ExactFactPresentation(string value, int maximumLength)
    {
        if (value is null)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned a missing fact value.",
                SemanticExploreFailureKind.InvalidData);
        }

        if (value.Length > maximumLength)
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an oversized fact value.",
                SemanticExploreFailureKind.LimitExceeded);
        }

        if (string.IsNullOrWhiteSpace(value)
            || value.Any(IsUnsafeUnicode)
            || ContainsLocalPathSignature(value))
        {
            throw new SemanticExploreValidationException(
                "A game-specific module provider returned an unsafe or oversized fact value.",
                SemanticExploreFailureKind.InvalidData);
        }

        return value;
    }

    private static bool ContainsLocalPathSignature(string value)
    {
        var candidate = value;
        for (var decodeDepth = 0; decodeDepth <= 3; decodeDepth++)
        {
            if (ContainsLiteralLocalPathSignature(candidate))
            {
                return true;
            }

            if (decodeDepth == 3 || !candidate.Contains('%'))
            {
                break;
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(candidate);
            }
            catch (UriFormatException)
            {
                return true;
            }

            if (string.Equals(decoded, candidate, StringComparison.Ordinal))
            {
                return true;
            }

            candidate = decoded;
        }

        return false;
    }

    private static bool ContainsLiteralLocalPathSignature(string value)
    {
        if (IsSafePublishedVirtualSourceIdentity(value))
        {
            return false;
        }

        if (value.Contains('\\')
            || value.Split('|').Any(component =>
                component.Contains('/')
                && !string.Equals(component, "Scarlet/Violet", StringComparison.Ordinal)))
        {
            return true;
        }

        for (var index = 0; index + 1 < value.Length; index++)
        {
            if (char.IsAsciiLetter(value[index])
                && value[index + 1] == ':'
                && (index == 0 || !char.IsAsciiLetterOrDigit(value[index - 1])))
            {
                return true;
            }
        }

        for (var index = value.IndexOf("file:", StringComparison.OrdinalIgnoreCase);
             index >= 0;
             index = value.IndexOf("file:", index + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (index == 0 || !char.IsAsciiLetterOrDigit(value[index - 1]))
            {
                return true;
            }
        }

        return value.StartsWith('~');
    }

    private static bool IsSafePublishedVirtualSourceIdentity(string value)
    {
        if (value.Length > 512
            || Encoding.UTF8.GetByteCount(value) > 512
            || value.Contains('\\')
            || value.Any(IsUnsafeUnicode))
        {
            return false;
        }

        const string romFsPrefix = "romfs/";
        const string worldPrefix = "world/";
        var virtualPath = value.StartsWith(romFsPrefix, StringComparison.Ordinal)
            ? value[romFsPrefix.Length..]
            : value.StartsWith(worldPrefix, StringComparison.Ordinal)
                ? value[worldPrefix.Length..]
                : ScenePlacementVirtualPath(value);
        return virtualPath.Length > 0
            && !virtualPath.Contains(':')
            && virtualPath.Split('/').All(segment =>
                !string.IsNullOrWhiteSpace(segment)
                && segment is not "." and not "..");
    }

    private static string ScenePlacementVirtualPath(string value)
    {
        foreach (var category in new[] { "visibleItems", "hiddenItems", "rummagingPoints" })
        {
            var prefix = category + ":world/";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var occurrenceSeparator = value.LastIndexOf(':');
            if (occurrenceSeparator <= prefix.Length
                || !int.TryParse(
                    value.AsSpan(occurrenceSeparator + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var occurrence)
                || occurrence < 0)
            {
                return string.Empty;
            }

            return value[prefix.Length..occurrenceSeparator];
        }

        return string.Empty;
    }

    private static bool IsUnsafeUnicode(char character)
    {
        return char.IsControl(character)
            || character is '\u061c'
                or '\u200b'
                or '\u200c'
                or '\u200d'
                or '\u200e'
                or '\u200f'
                or '\u202a'
                or '\u202b'
                or '\u202c'
                or '\u202d'
                or '\u202e'
                or '\u2060'
                or '\u2061'
                or '\u2062'
                or '\u2063'
                or '\u2064'
                or '\u2066'
                or '\u2067'
                or '\u2068'
                or '\u2069'
                or '\ufeff';
    }

    private sealed class BoundedRecordCollection : Collection<GameModuleRecordDto>
    {
        private readonly HashSet<string> recordIds = new(StringComparer.Ordinal);
        private long factCount;
        private long evidenceCount;
        private long projectedBytes = 1_024;

        protected override void InsertItem(int index, GameModuleRecordDto item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (index != Count || item.SortOrder != Count)
            {
                throw new SemanticExploreValidationException(
                    "A game-specific module provider returned a noncanonical record order.",
                    SemanticExploreFailureKind.InvalidData);
            }

            if (Count >= GameModuleContract.MaximumRecords
                || factCount > GameModuleContract.MaximumFacts - item.Facts.Count)
            {
                throw new SemanticExploreValidationException(
                    "The game-specific module result exceeds its bounded record or fact limit.",
                    SemanticExploreFailureKind.LimitExceeded);
            }

            if (recordIds.Contains(item.RecordId)
                || item.ParentRecordId is not null && !recordIds.Contains(item.ParentRecordId))
            {
                throw new SemanticExploreValidationException(
                    "A game-specific module provider returned duplicate or orphaned record identity.",
                    SemanticExploreFailureKind.InvalidData);
            }

            recordIds.Add(item.RecordId);

            var factIds = new HashSet<string>(StringComparer.Ordinal);
            var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
            long itemEvidenceCount = 0;
            foreach (var fact in item.Facts)
            {
                if (!factIds.Add(fact.FactId)
                    || !fieldKeys.Add(fact.FieldKey))
                {
                    throw new SemanticExploreValidationException(
                        "A game-specific module provider returned duplicate fact identity.",
                        SemanticExploreFailureKind.InvalidData);
                }

                if (fact.Evidence.Count > GameModuleContract.MaximumEvidenceRecordsPerFact)
                {
                    throw new SemanticExploreValidationException(
                        "A game-specific module fact exceeds its bounded evidence limit.",
                        SemanticExploreFailureKind.LimitExceeded);
                }

                itemEvidenceCount = checked(itemEvidenceCount + fact.Evidence.Count);
            }

            var nextEvidenceCount = checked(evidenceCount + itemEvidenceCount);
            var nextProjectedBytes = checked(projectedBytes + RecordBytes(item));
            if (nextEvidenceCount > GameModuleContract.MaximumEvidenceRecords
                || nextProjectedBytes > MaximumProjectedBytes)
            {
                throw new SemanticExploreValidationException(
                    "The game-specific module result exceeds its bounded evidence or payload limit.",
                    SemanticExploreFailureKind.LimitExceeded);
            }

            factCount = checked(factCount + item.Facts.Count);
            evidenceCount = nextEvidenceCount;
            projectedBytes = nextProjectedBytes;
            base.InsertItem(index, item);
        }

        protected override void ClearItems() => throw new NotSupportedException();

        protected override void RemoveItem(int index) => throw new NotSupportedException();

        protected override void SetItem(int index, GameModuleRecordDto item) =>
            throw new NotSupportedException();
    }
}
