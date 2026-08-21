// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Diagnostics;
using KM.Api.Encounters;
using KM.Api.GameModules;
using KM.Api.Moves;
using KM.Api.Raids;
using KM.Api.ScriptedBosses;
using KM.Api.Semantics;
using KM.Api.Trainers;
using KM.Api.Workflows;

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

    private static readonly IReadOnlyList<SemanticSourceLayerKindDto> EffectiveLayer =
        [SemanticSourceLayerKindDto.Layered];

    public static IReadOnlyList<GameModuleCapabilityDto> Capabilities(SemanticGameFamilyDto family)
    {
        return family switch
        {
            SemanticGameFamilyDto.SwordShield =>
            [
                Unavailable(
                    GameModuleDto.SwordShieldRewardEcosystem,
                    family,
                    GameModuleMaturityDto.ReadOnlyFirst,
                    "unified-acquisition-provider-unavailable"),
                Unavailable(
                    GameModuleDto.SwordShieldExeFsCompatibility,
                    family,
                    GameModuleMaturityDto.Product,
                    "bounded-nso-decoder-unavailable"),
                Unavailable(
                    GameModuleDto.SwordShieldDynamaxAdventures,
                    family,
                    GameModuleMaturityDto.Product,
                    "bounded-route-analysis-provider-unavailable"),
                Unavailable(
                    GameModuleDto.SwordShieldRoyalCandyProgression,
                    family,
                    GameModuleMaturityDto.Product,
                    "bounded-progression-provider-unavailable"),
                Unavailable(
                    GameModuleDto.SwordShieldBattleCafeRewards,
                    family,
                    GameModuleMaturityDto.ResearchGated,
                    "research-evidence-required"),
                Unavailable(
                    GameModuleDto.SwordShieldEventAssignments,
                    family,
                    GameModuleMaturityDto.ResearchGated,
                    "research-evidence-required"),
            ],
            SemanticGameFamilyDto.ScarletViolet =>
            [
                Available(
                    GameModuleDto.ScarletVioletTeraRaidAnalysis,
                    family,
                    GameModuleMaturityDto.Product,
                    "progression-unlock-and-rotation-coverage-unavailable"),
                Unavailable(
                    GameModuleDto.ScarletVioletPackedLooseComparison,
                    family,
                    GameModuleMaturityDto.Product,
                    "packed-loose-comparison-contract-missing"),
                Unavailable(
                    GameModuleDto.ScarletVioletEventDataComparison,
                    family,
                    GameModuleMaturityDto.Product,
                    "verified-event-comparison-provider-unavailable"),
                Unavailable(
                    GameModuleDto.ScarletVioletScenePlacementEditing,
                    family,
                    GameModuleMaturityDto.ResearchGated,
                    "research-evidence-required"),
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
                Unavailable(
                    GameModuleDto.LegendsZaEncounterCompatibility,
                    family,
                    GameModuleMaturityDto.Product,
                    "read-only-compatibility-projection-missing"),
                Unavailable(
                    GameModuleDto.LegendsZaAlphaMoveDistribution,
                    family,
                    GameModuleMaturityDto.Product,
                    "bounded-pokemon-projection-unavailable"),
                Unavailable(
                    GameModuleDto.LegendsZaDexLayoutPlanning,
                    family,
                    GameModuleMaturityDto.Product,
                    "bounded-executable-observer-unavailable"),
                Available(
                    GameModuleDto.LegendsZaMoveVariantComparison,
                    family,
                    GameModuleMaturityDto.Product,
                    "variant-consumer-coverage-unavailable"),
                Unavailable(
                    GameModuleDto.LegendsZaTrainerPoolSwitching,
                    family,
                    GameModuleMaturityDto.ResearchGated,
                    "verified-trainer-pool-provider-unavailable"),
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
            GameModuleDto.ScarletVioletStellarBehavior => "sv.game-modules.stellar-behavior",
            GameModuleDto.LegendsZaScriptedBossTimeline => "za.game-modules.scripted-boss-timeline",
            GameModuleDto.LegendsZaTrainerArchetypes => "za.game-modules.trainer-archetypes",
            GameModuleDto.LegendsZaWildSpawnExplorer => "za.game-modules.wild-spawn-explorer",
            GameModuleDto.LegendsZaEncounterCompatibility => "za.game-modules.encounter-compatibility",
            GameModuleDto.LegendsZaAlphaMoveDistribution => "za.game-modules.alpha-move-distribution",
            GameModuleDto.LegendsZaDexLayoutPlanning => "za.game-modules.dex-layout-planning",
            GameModuleDto.LegendsZaMoveVariantComparison => "za.game-modules.move-variant-comparison",
            GameModuleDto.LegendsZaTrainerPoolSwitching => "za.game-modules.trainer-pool-switching",
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
            >= GameModuleDto.LegendsZaScriptedBossTimeline and <= GameModuleDto.LegendsZaTrainerPoolSwitching =>
                SemanticGameFamilyDto.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
        };
    }

    internal static string ModuleDomain(GameModuleDto module)
    {
        return module switch
        {
            GameModuleDto.SwordShieldExeFsCompatibility => "workflow.exefsPatches",
            GameModuleDto.ScarletVioletTeraRaidAnalysis => "workflow.teraRaids",
            GameModuleDto.LegendsZaScriptedBossTimeline => "workflow.encounters",
            GameModuleDto.LegendsZaTrainerArchetypes => "workflow.trainers",
            GameModuleDto.LegendsZaWildSpawnExplorer => "workflow.encounters",
            GameModuleDto.LegendsZaAlphaMoveDistribution or GameModuleDto.LegendsZaDexLayoutPlanning => "workflow.pokemon",
            GameModuleDto.LegendsZaMoveVariantComparison => "workflow.moves",
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
