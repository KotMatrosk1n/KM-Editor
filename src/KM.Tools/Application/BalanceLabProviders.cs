// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Api.Diagnostics;
using KM.Api.Encounters;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Semantics;
using KM.Api.Trainers;
using KM.Api.Workflows;

namespace KM.Tools.Application;

internal sealed record BalanceLabStudyData(
    BalanceLabStudyCapabilityDto Capability,
    IReadOnlyList<BalanceLabChartPointDto> Points,
    IReadOnlyList<BalanceLabFindingDto> Findings,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    bool Cacheable);

internal interface IBalanceLabFamilyProvider
{
    SemanticGameFamilyDto GameFamily { get; }

    IReadOnlyList<BalanceLabStudyCapabilityDto> Capabilities { get; }

    BalanceLabStudyData BuildTrainers(TrainersWorkflowDto workflow);

    BalanceLabStudyData BuildEncounters(EncountersWorkflowDto workflow);

    BalanceLabStudyData BuildMoves(MovesWorkflowDto workflow);

    BalanceLabStudyData BuildEconomy(ItemsWorkflowDto workflow);

    BalanceLabStudyData BuildPokedexEvolution(PokemonWorkflowDto workflow);
}

internal abstract class BalanceLabFamilyProviderBase : IBalanceLabFamilyProvider
{
    private const int RecordSchemaVersion = 1;
    private const int MaximumPoints = 50_000;
    private const int MaximumFacts = 100_000;
    private const int MaximumEvidenceRecords = 150_000;
    private const int MaximumFindings = 100_000;
    private const int MaximumDiagnostics = 100;
    private const long MaximumProjectedBytes = 56L * 1024L * 1024L;

    protected const string TrainersDomain = "workflow.trainers";
    protected const string EncountersDomain = "workflow.encounters";
    protected const string MovesDomain = "workflow.moves";
    protected const string ItemsDomain = "workflow.items";
    protected const string PokemonDomain = "workflow.pokemon";

    public abstract SemanticGameFamilyDto GameFamily { get; }

    protected abstract string FamilyKey { get; }

    protected virtual string TrainerCoverageReason => "progression-order-and-move-legality-unavailable";

    protected abstract string EncounterCoverageReason { get; }

    public IReadOnlyList<BalanceLabStudyCapabilityDto> Capabilities =>
    [
        Capability(BalanceLabStudyDto.TrainerProgression, SemanticConfidenceDto.Derived, TrainerCoverageReason),
        Capability(BalanceLabStudyDto.EncounterDistribution, SemanticConfidenceDto.Derived, EncounterCoverageReason),
        Capability(BalanceLabStudyDto.MoveBalance, SemanticConfidenceDto.Derived, "move-consumer-coverage-unavailable"),
        Capability(BalanceLabStudyDto.Economy, SemanticConfidenceDto.Derived, "acquisition-and-reward-coverage-unavailable"),
        Capability(BalanceLabStudyDto.PokedexEvolution, SemanticConfidenceDto.Verified, "overall-obtainability-coverage-unavailable"),
    ];

    public BalanceLabStudyData BuildTrainers(TrainersWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var unavailable = WorkflowUnavailable(BalanceLabStudyDto.TrainerProgression, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var projectedTeamCount = workflow.Trainers.Sum(trainer => (long)trainer.Team.Count);
        ValidateProjectedBounds(
            workflow.Trainers.Count + projectedTeamCount,
            workflow.Trainers.Count * 12L + projectedTeamCount * 10L,
            workflow.Trainers.Count * 24L + projectedTeamCount * 10L,
            workflow.Trainers.Count + projectedTeamCount);
        var providerId = ProviderId(BalanceLabStudyDto.TrainerProgression);
        var points = new List<BalanceLabChartPointDto>();
        var findings = new List<BalanceLabFindingDto>();

        foreach (var trainer in workflow.Trainers.OrderBy(trainer => trainer.TrainerId))
        {
            var trainerRecord = NumericRecord(TrainersDomain, "trainer", trainer.TrainerId, subrecordId: null);
            var occupiedTeam = trainer.Team.Where(member => member.SpeciesId > 0).ToArray();
            var inRangeGroups = occupiedTeam
                .Where(member => IsTrainerPartySlotSupported(member.Slot))
                .GroupBy(member => member.Slot)
                .ToArray();
            var validTeam = inRangeGroups
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .ToArray();
            var aggregateFacts = new List<BalanceLabFactDto>
            {
                VerifiedSigned(providerId, "trainerId", "Trainer ID", trainer.TrainerId, null, trainerRecord),
                DerivedSigned(providerId, "partySize", "Party size", occupiedTeam.Length, "pokemon", [trainerRecord]),
                VerifiedSigned(providerId, "moneyFieldValue", "Stored money field value", trainer.Money, null, trainerRecord),
                VerifiedSigned(providerId, "aiFlags", "AI flags", trainer.AiFlags, null, trainerRecord),
            };
            if (HasVerifiedTrainerBattleType)
            {
                aggregateFacts.Add(VerifiedSigned(
                    providerId,
                    "battleType",
                    "Battle type",
                    trainer.BattleTypeValue,
                    null,
                    trainerRecord));
            }

            if (validTeam.Length > 0)
            {
                aggregateFacts.Add(DerivedDecimal(
                    providerId,
                    "averagePartyLevel",
                    "Average party level",
                    validTeam.Average(member => member.Level),
                    "level",
                    validTeam.Select(member => TrainerPartyRecord(trainer.TrainerId, member.Slot)).ToArray()));
                aggregateFacts.Add(DerivedSigned(
                    providerId,
                    "maximumPartyLevel",
                    "Maximum party level",
                    validTeam.Max(member => member.Level),
                    "level",
                    validTeam.Select(member => TrainerPartyRecord(trainer.TrainerId, member.Slot)).ToArray()));
            }

            AddTrainerProgressionFact(providerId, trainer, trainerRecord, aggregateFacts);
            points.Add(Point(providerId, $"trainer:{trainer.TrainerId.ToString(CultureInfo.InvariantCulture)}", TrainerAggregateSeriesKey(trainer), trainer.Name, trainerRecord, aggregateFacts));

            if (occupiedTeam.Length == 0)
            {
                findings.Add(Finding(
                    providerId,
                    $"trainer:{trainer.TrainerId.ToString(CultureInfo.InvariantCulture)}:empty-party",
                    "empty-party",
                    BalanceLabFindingSeverityDto.Warning,
                    SemanticConfidenceDto.Verified,
                    "Trainer has no party members",
                    "The loaded trainer record contains no occupied party slots.",
                    trainerRecord,
                    [],
                    [aggregateFacts[1]]));
            }

            var invalidSlots = occupiedTeam.Where(member => !IsTrainerPartySlotSupported(member.Slot)).ToArray();
            if (invalidSlots.Length > 0)
            {
                var fact = DerivedSigned(providerId, "invalidPartySlotCount", "Invalid party slot count", invalidSlots.Length, "slots", [trainerRecord]);
                findings.Add(Finding(
                    providerId,
                    $"trainer:{trainer.TrainerId.ToString(CultureInfo.InvariantCulture)}:invalid-party-slots",
                    "invalid-party-slot",
                    BalanceLabFindingSeverityDto.Warning,
                    SemanticConfidenceDto.Verified,
                    "Trainer has out-of-range party slots",
                    $"One or more loaded party slots fall outside the supported {TrainerPartySlotIdentityRange} identity range.",
                    trainerRecord,
                    [],
                    [fact]));
            }

            var duplicateSlots = inRangeGroups.Where(group => group.Count() > 1).ToArray();
            if (duplicateSlots.Length > 0)
            {
                var fact = DerivedSigned(providerId, "duplicatePartySlotCount", "Duplicate party slot count", duplicateSlots.Length, "slots", [trainerRecord]);
                findings.Add(Finding(
                    providerId,
                    $"trainer:{trainer.TrainerId.ToString(CultureInfo.InvariantCulture)}:duplicate-party-slots",
                    "duplicate-party-slot",
                    BalanceLabFindingSeverityDto.Warning,
                    SemanticConfidenceDto.Verified,
                    "Trainer has duplicate party slot identities",
                    "Two or more loaded party members use the same supported slot identity. Ambiguous party rows are omitted from slot points.",
                    trainerRecord,
                    [],
                    [fact]));
            }

            foreach (var member in validTeam.OrderBy(member => member.Slot))
            {
                var memberRecord = TrainerPartyRecord(trainer.TrainerId, member.Slot);
                var facts = new List<BalanceLabFactDto>
                {
                    VerifiedSigned(providerId, "partySlot", "Party slot", member.Slot, null, memberRecord),
                    VerifiedSigned(providerId, "speciesId", "Species ID", member.SpeciesId, null, memberRecord),
                    VerifiedSigned(providerId, "form", "Form", member.Form, null, memberRecord),
                    VerifiedSigned(providerId, "level", "Level", member.Level, "level", memberRecord),
                    VerifiedSigned(providerId, "heldItemId", "Held item ID", member.HeldItemId, null, memberRecord),
                    DerivedSigned(providerId, "moveCount", "Move count", member.MoveIds.Count(move => move > 0), "moves", [memberRecord]),
                };
                facts.Add(AreStatsNonnegative(member.Evs)
                    ? DerivedSigned(providerId, "evTotal", "EV total", SumStats(member.Evs), "points", [memberRecord])
                    : VerifiedNull(providerId, "evTotal", "EV total", "points", memberRecord));
                facts.Add(AreStatsNonnegative(member.Ivs)
                    ? DerivedSigned(providerId, "ivTotal", "IV total", SumStats(member.Ivs), "points", [memberRecord])
                    : VerifiedNull(providerId, "ivTotal", "IV total", "points", memberRecord));
                if (member.BaseStats is not null)
                {
                    facts.Add(DerivedSigned(providerId, "baseStatTotal", "Base stat total", SumStats(member.BaseStats), "points", [memberRecord]));
                }

                points.Add(Point(
                    providerId,
                    $"trainer:{trainer.TrainerId.ToString(CultureInfo.InvariantCulture)}:party-slot:{member.Slot.ToString(CultureInfo.InvariantCulture)}",
                    "trainer-party",
                    $"{trainer.Name}, slot {TrainerPartySlotDisplayNumber(member.Slot).ToString(CultureInfo.InvariantCulture)}",
                    memberRecord,
                    facts));

                var duplicateMoves = member.MoveIds.Where(move => move > 0).GroupBy(move => move).Where(group => group.Count() > 1).ToArray();
                if (duplicateMoves.Length > 0)
                {
                    findings.Add(Finding(
                        providerId,
                        $"trainer:{trainer.TrainerId.ToString(CultureInfo.InvariantCulture)}:party-slot:{member.Slot.ToString(CultureInfo.InvariantCulture)}:duplicate-moves",
                        "duplicate-move-id",
                        BalanceLabFindingSeverityDto.Warning,
                        SemanticConfidenceDto.Verified,
                        "Party member repeats a move",
                        "The loaded party slot contains the same nonzero move ID more than once.",
                        memberRecord,
                        [],
                        [DerivedSigned(providerId, "duplicateMoveIdCount", "Repeated move ID count", duplicateMoves.Length, "move IDs", [memberRecord])]));
                }
            }
        }

        return CompleteStudy(BalanceLabStudyDto.TrainerProgression, points, findings, workflow.Summary, workflow.Diagnostics);
    }

    public BalanceLabStudyData BuildEncounters(EncountersWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var unavailable = WorkflowUnavailable(BalanceLabStudyDto.EncounterDistribution, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var projectedSlotCount = workflow.Tables.Sum(table => (long)table.Slots.Count);
        ValidateProjectedBounds(
            workflow.Tables.Count + projectedSlotCount,
            workflow.Tables.Count * 5L + projectedSlotCount * 14L,
            workflow.Tables.Count * 5L + projectedSlotCount * 18L,
            workflow.Tables.Count + projectedSlotCount);
        var providerId = ProviderId(BalanceLabStudyDto.EncounterDistribution);
        var points = new List<BalanceLabChartPointDto>();
        var findings = new List<BalanceLabFindingDto>();

        foreach (var table in workflow.Tables.OrderBy(table => table.TableId, StringComparer.Ordinal))
        {
            var tableRecord = TextRecord(EncountersDomain, "encounter-table", table.TableId, subrecordId: null);
            var context = GetEncounterWeightContext(table);
            var tableFacts = new List<BalanceLabFactDto>
            {
                DerivedSigned(providerId, "slotCount", "Slot count", table.Slots.Count, "slots", [tableRecord]),
                DerivedSigned(providerId, "nativeWeightTotal", "Native weight total", table.Slots.Sum(slot => (long)slot.Weight), "weight", [tableRecord]),
                VerifiedEnum(providerId, "weightSemantics", "Weight semantics", context.Semantics, tableRecord),
                context.Denominator is null
                    ? VerifiedNull(providerId, "weightDenominator", "Weight denominator", "weight", tableRecord)
                    : DerivedSigned(providerId, "weightDenominator", "Weight denominator", context.Denominator.Value, "weight", [tableRecord]),
            };
            points.Add(Point(providerId, $"encounter:{StableComponent(table.TableId)}", "encounter-table", table.TableLabel ?? table.Location, tableRecord, tableFacts));

            if (context.Finding is not null)
            {
                findings.Add(Finding(
                    providerId,
                    $"encounter:{StableComponent(table.TableId)}:{context.Finding.RuleId}",
                    context.Finding.RuleId,
                    BalanceLabFindingSeverityDto.Warning,
                    context.Finding.Confidence,
                    context.Finding.Title,
                    context.Finding.Summary,
                    tableRecord,
                    [],
                    tableFacts));
            }

            var validSlotGroups = table.Slots
                .Where(slot => slot.Slot >= 0)
                .GroupBy(slot => slot.Slot)
                .ToArray();
            var invalidSlots = table.Slots.Where(slot => slot.Slot < 0).ToArray();
            if (invalidSlots.Length > 0)
            {
                findings.Add(Finding(
                    providerId,
                    $"encounter:{StableComponent(table.TableId)}:invalid-slots",
                    "invalid-encounter-slot",
                    BalanceLabFindingSeverityDto.Warning,
                    SemanticConfidenceDto.Verified,
                    "Encounter table has invalid slot identities",
                    "One or more loaded encounter rows have a negative slot index and cannot be represented by the stable slot identity grammar.",
                    tableRecord,
                    [],
                    [DerivedSigned(providerId, "invalidSlotCount", "Invalid slot count", invalidSlots.Length, "slots", [tableRecord])]));
            }

            var duplicateSlots = validSlotGroups.Where(group => group.Count() > 1).ToArray();
            if (duplicateSlots.Length > 0)
            {
                findings.Add(Finding(
                    providerId,
                    $"encounter:{StableComponent(table.TableId)}:duplicate-slots",
                    "duplicate-encounter-slot",
                    BalanceLabFindingSeverityDto.Warning,
                    SemanticConfidenceDto.Verified,
                    "Encounter table has duplicate slot identities",
                    "Two or more loaded encounter rows use the same nonnegative slot identity. Ambiguous rows are omitted from slot points.",
                    tableRecord,
                    [],
                    [DerivedSigned(providerId, "duplicateSlotCount", "Duplicate slot count", duplicateSlots.Length, "slots", [tableRecord])]));
            }

            foreach (var slot in validSlotGroups
                         .Where(group => group.Count() == 1)
                         .Select(group => group.Single())
                         .OrderBy(slot => slot.Slot))
            {
                var slotRecord = TextRecord(EncountersDomain, "encounter-table", table.TableId, $"slot:{slot.Slot.ToString(CultureInfo.InvariantCulture)}");
                var facts = new List<BalanceLabFactDto>
                {
                    VerifiedSigned(providerId, "slot", "Slot", slot.Slot, null, slotRecord),
                    VerifiedSigned(providerId, "speciesId", "Species ID", slot.SpeciesId, null, slotRecord),
                    VerifiedSigned(providerId, "form", "Form", slot.Form, null, slotRecord),
                    VerifiedSigned(providerId, "minimumLevel", "Minimum level", slot.LevelMin, "level", slotRecord),
                    VerifiedSigned(providerId, "maximumLevel", "Maximum level", slot.LevelMax, "level", slotRecord),
                    VerifiedSigned(providerId, "nativeWeight", "Native weight", slot.Weight, "weight", slotRecord),
                    VerifiedEnum(providerId, "weightSemantics", "Weight semantics", context.Semantics, tableRecord),
                    context.Denominator is null
                        ? VerifiedNull(providerId, "weightDenominator", "Weight denominator", "weight", tableRecord)
                        : DerivedSigned(providerId, "weightDenominator", "Weight denominator", context.Denominator.Value, "weight", [tableRecord]),
                };
                if (context.CanDeriveShare && context.Denominator is > 0)
                {
                    facts.Add(DerivedDecimal(
                        providerId,
                        "effectiveShare",
                        "Effective listed share",
                        slot.Weight * 100d / context.Denominator.Value,
                        "percent",
                        [slotRecord, tableRecord]));
                }

                if (!string.IsNullOrWhiteSpace(slot.TimeOfDay))
                {
                    facts.Add(VerifiedText(providerId, "timeOfDay", "Time of day", slot.TimeOfDay, slotRecord));
                }

                if (!string.IsNullOrWhiteSpace(slot.Weather))
                {
                    facts.Add(VerifiedText(providerId, "weather", "Weather or conditions", slot.Weather, slotRecord));
                }

                AddEncounterFamilyFacts(providerId, slot, slotRecord, facts);
                points.Add(Point(
                    providerId,
                    $"encounter:{StableComponent(table.TableId)}:slot:{slot.Slot.ToString(CultureInfo.InvariantCulture)}",
                    "encounter-slot",
                    $"{table.TableLabel ?? table.Location}, slot {EncounterSlotDisplayNumber(slot.Slot).ToString(CultureInfo.InvariantCulture)}",
                    slotRecord,
                    facts));

                if (slot.LevelMin > slot.LevelMax)
                {
                    findings.Add(Finding(
                        providerId,
                        $"encounter:{StableComponent(table.TableId)}:slot:{slot.Slot.ToString(CultureInfo.InvariantCulture)}:level-range",
                        "level-range-inverted",
                        BalanceLabFindingSeverityDto.Warning,
                        SemanticConfidenceDto.Verified,
                        "Encounter level range is inverted",
                        "The loaded minimum level is greater than the loaded maximum level.",
                        slotRecord,
                        [],
                        facts.Where(fact => fact.FactId.EndsWith("minimumLevel", StringComparison.Ordinal) || fact.FactId.EndsWith("maximumLevel", StringComparison.Ordinal)).ToArray()));
                }

                AddEncounterFamilyFindings(providerId, table, slot, slotRecord, facts, findings);
            }
        }

        return CompleteStudy(BalanceLabStudyDto.EncounterDistribution, points, findings, workflow.Summary, workflow.Diagnostics);
    }

    public BalanceLabStudyData BuildMoves(MovesWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var unavailable = WorkflowUnavailable(BalanceLabStudyDto.MoveBalance, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        ValidateProjectedBounds(workflow.Moves.Count, workflow.Moves.Count * 10L, workflow.Moves.Count * 12L, workflow.Moves.Count);
        var providerId = ProviderId(BalanceLabStudyDto.MoveBalance);
        var points = new List<BalanceLabChartPointDto>();
        var findings = new List<BalanceLabFindingDto>();
        var positivePowers = workflow.Moves.Where(move => move.CanUseMove && move.Power > 0).Select(move => move.Power).Order().ToArray();
        var powerP95 = PercentileNearestRank(positivePowers, 0.95);

        foreach (var move in workflow.Moves.OrderBy(move => move.MoveId))
        {
            var record = NumericRecord(MovesDomain, "move", move.MoveId, subrecordId: null);
            var facts = new List<BalanceLabFactDto>
            {
                VerifiedBoolean(providerId, "canUseMove", "Usable", move.CanUseMove, record),
                VerifiedSigned(providerId, "power", "Power", move.Power, "power", record),
                VerifiedSigned(providerId, "accuracyValue", "Stored accuracy value", move.Accuracy, null, record),
                VerifiedSigned(providerId, "pp", "PP", move.PP, "uses", record),
                VerifiedSigned(providerId, "priority", "Priority", move.Priority, null, record),
                DerivedSigned(providerId, "runtimeVariantCount", "Runtime variant count", move.RuntimeVariants.Count, "variants", [record]),
            };
            AddMoveFamilyFacts(providerId, move, record, facts);
            points.Add(Point(providerId, $"move:{move.MoveId.ToString(CultureInfo.InvariantCulture)}", "move", move.Name, record, facts));

            if (powerP95 is > 0 && move.CanUseMove && move.Power >= powerP95)
            {
                findings.Add(Finding(
                    providerId,
                    $"move:{move.MoveId.ToString(CultureInfo.InvariantCulture)}:power-p95",
                    "power-at-or-above-p95",
                    BalanceLabFindingSeverityDto.Info,
                    SemanticConfidenceDto.Derived,
                    "Move is in the highest observed power band",
                    "Its loaded power is at or above the nearest-rank 95th percentile among usable moves with positive power. This is a distribution observation, not a confirmed balance error.",
                    record,
                    [],
                    [
                        facts.Single(fact => fact.FactId.EndsWith("power", StringComparison.Ordinal)),
                        DerivedSigned(providerId, "powerP95", "Observed power 95th percentile", powerP95.Value, "power", [record]),
                    ]));
            }
        }

        return CompleteStudy(BalanceLabStudyDto.MoveBalance, points, findings, workflow.Summary, workflow.Diagnostics);
    }

    public BalanceLabStudyData BuildEconomy(ItemsWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var unavailable = WorkflowUnavailable(BalanceLabStudyDto.Economy, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        ValidateProjectedBounds(workflow.Items.Count, workflow.Items.Count * 6L, workflow.Items.Count * 6L, workflow.Items.Count * 2L);
        var providerId = ProviderId(BalanceLabStudyDto.Economy);
        var points = new List<BalanceLabChartPointDto>();
        var findings = new List<BalanceLabFindingDto>();
        var positiveBuyPrices = workflow.Items.Where(item => item.BuyPrice > 0).Select(item => item.BuyPrice).Order().ToArray();
        var buyP95 = PercentileNearestRank(positiveBuyPrices, 0.95);

        foreach (var item in workflow.Items.OrderBy(item => item.ItemId))
        {
            var record = NumericRecord(ItemsDomain, "item", item.ItemId, subrecordId: null);
            var facts = new List<BalanceLabFactDto>
            {
                VerifiedSigned(providerId, "buyPrice", "Buy price", item.BuyPrice, "currency", record),
                DerivedSigned(providerId, "derivedSellValue", "Derived sell value", item.SellPrice, "currency", [record]),
            };
            AddEconomyFamilyFacts(providerId, item, record, facts);
            points.Add(Point(providerId, $"item:{item.ItemId.ToString(CultureInfo.InvariantCulture)}", "item-price", item.Name, record, facts));

            if (item.BuyPrice > 0 && item.SellPrice > item.BuyPrice)
            {
                findings.Add(Finding(
                    providerId,
                    $"item:{item.ItemId.ToString(CultureInfo.InvariantCulture)}:sell-exceeds-buy",
                    "derived-sell-value-exceeds-buy-price",
                    BalanceLabFindingSeverityDto.Warning,
                    SemanticConfidenceDto.Derived,
                    "Derived sell value exceeds buy price",
                    "The workflow-derived sell value is larger than the loaded positive buy price. Shop availability is outside this study's current coverage.",
                    record,
                    [],
                    facts.Take(2).ToArray()));
            }

            if (buyP95 is > 0 && item.BuyPrice >= buyP95)
            {
                findings.Add(Finding(
                    providerId,
                    $"item:{item.ItemId.ToString(CultureInfo.InvariantCulture)}:buy-price-p95",
                    "buy-price-at-or-above-p95",
                    BalanceLabFindingSeverityDto.Info,
                    SemanticConfidenceDto.Derived,
                    "Item is in the highest observed buy-price band",
                    "Its loaded buy price is at or above the nearest-rank 95th percentile among positive item buy prices. This is a distribution observation, not a confirmed economy error.",
                    record,
                    [],
                    [
                        facts[0],
                        DerivedSigned(providerId, "buyPriceP95", "Observed buy-price 95th percentile", buyP95.Value, "currency", [record]),
                    ]));
            }
        }

        return CompleteStudy(BalanceLabStudyDto.Economy, points, findings, workflow.Summary, workflow.Diagnostics);
    }

    public BalanceLabStudyData BuildPokedexEvolution(PokemonWorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var unavailable = WorkflowUnavailable(BalanceLabStudyDto.PokedexEvolution, workflow.Summary, workflow.Diagnostics);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var projectedEvolutionCount = workflow.Pokemon.Sum(pokemon => (long)pokemon.Evolutions.Count);
        ValidateProjectedBounds(
            workflow.Pokemon.Count,
            workflow.Pokemon.Count * 8L + projectedEvolutionCount * 3L,
            workflow.Pokemon.Count * 8L + projectedEvolutionCount * 6L,
            projectedEvolutionCount);
        var providerId = ProviderId(BalanceLabStudyDto.PokedexEvolution);
        var points = new List<BalanceLabChartPointDto>();
        var findings = new List<BalanceLabFindingDto>();
        var bySpeciesForm = workflow.Pokemon
            .GroupBy(pokemon => (pokemon.SpeciesId, pokemon.Form))
            .ToDictionary(group => group.Key, group => group.OrderBy(pokemon => pokemon.PersonalId).ToArray());

        foreach (var pokemon in workflow.Pokemon.OrderBy(pokemon => pokemon.PersonalId))
        {
            var record = NumericRecord(PokemonDomain, "pokemon-personal", pokemon.PersonalId, subrecordId: null);
            var facts = new List<BalanceLabFactDto>
            {
                VerifiedSigned(providerId, "speciesId", "Species ID", pokemon.SpeciesId, null, record),
                VerifiedSigned(providerId, "form", "Form", pokemon.Form, null, record),
                VerifiedBoolean(providerId, "isPresentInGame", "Present in game", pokemon.DexPresence.IsPresentInGame, record),
                DerivedBoolean(providerId, "isInAnyDex", "In any loaded Dex", pokemon.DexPresence.IsInAnyDex, [record]),
                DerivedSigned(providerId, "evolutionCount", "Evolution count", pokemon.Evolutions.Count, "rows", [record]),
            };
            AddPokedexFamilyFacts(providerId, pokemon, record, facts);
            points.Add(Point(providerId, $"pokemon:{pokemon.PersonalId.ToString(CultureInfo.InvariantCulture)}", "pokedex", pokemon.Name, record, facts));

            foreach (var evolution in pokemon.Evolutions.OrderBy(evolution => evolution.Slot))
            {
                var evolutionRecord = evolution.Slot >= 0
                    ? NumericRecord(PokemonDomain, "pokemon-personal", pokemon.PersonalId, $"evolution-slot:{evolution.Slot.ToString(CultureInfo.InvariantCulture)}")
                    : record;
                if (!bySpeciesForm.TryGetValue((evolution.Species, evolution.Form), out var targets))
                {
                    var evidenceFacts = EvolutionEvidenceFacts(providerId, evolution, evolutionRecord);
                    findings.Add(Finding(
                        providerId,
                        $"pokemon:{pokemon.PersonalId.ToString(CultureInfo.InvariantCulture)}:evolution:{evolution.Slot.ToString(CultureInfo.InvariantCulture)}:missing-target",
                        "evolution-target-unavailable",
                        BalanceLabFindingSeverityDto.Warning,
                        SemanticConfidenceDto.Verified,
                        "Evolution target is unavailable",
                        "No loaded Pokemon record has the exact target species and form stored by this evolution row.",
                        record,
                        [evolutionRecord],
                        evidenceFacts));
                    continue;
                }

                if (targets.All(target => !target.DexPresence.IsPresentInGame))
                {
                    var evidenceFacts = EvolutionEvidenceFacts(providerId, evolution, evolutionRecord);
                    var targetRecords = targets
                        .Select(target => NumericRecord(PokemonDomain, "pokemon-personal", target.PersonalId, subrecordId: null))
                        .ToArray();
                    findings.Add(Finding(
                        providerId,
                        $"pokemon:{pokemon.PersonalId.ToString(CultureInfo.InvariantCulture)}:evolution:{evolution.Slot.ToString(CultureInfo.InvariantCulture)}:target-not-present",
                        "evolution-target-not-present-in-game",
                        BalanceLabFindingSeverityDto.Warning,
                        SemanticConfidenceDto.Verified,
                        "Evolution target is not marked present",
                        "The exact loaded target record is marked as not present in this game. This does not claim overall obtainability beyond the loaded Pokedex facts.",
                        record,
                        [evolutionRecord, .. targetRecords],
                        evidenceFacts));
                }
            }
        }

        return CompleteStudy(BalanceLabStudyDto.PokedexEvolution, points, findings, workflow.Summary, workflow.Diagnostics);
    }

    protected virtual string TrainerAggregateSeriesKey(TrainerRecordDto trainer)
    {
        return "trainer-roster";
    }

    protected virtual void AddTrainerProgressionFact(
        string providerId,
        TrainerRecordDto trainer,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
    }

    protected virtual bool IsTrainerPartySlotSupported(int slot)
    {
        return slot is >= 0 and <= 5;
    }

    protected virtual int TrainerPartySlotDisplayNumber(int slot)
    {
        return checked(slot + 1);
    }

    protected virtual string TrainerPartySlotIdentityRange => "0 through 5";

    protected virtual bool HasVerifiedTrainerBattleType => true;

    protected virtual int EncounterSlotDisplayNumber(int slot)
    {
        return checked(slot + 1);
    }

    protected virtual void AddEncounterFamilyFacts(
        string providerId,
        EncounterSlotRecordDto slot,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
    }

    protected virtual void AddEncounterFamilyFindings(
        string providerId,
        EncounterTableRecordDto table,
        EncounterSlotRecordDto slot,
        SemanticRecordRefDto record,
        IReadOnlyList<BalanceLabFactDto> facts,
        ICollection<BalanceLabFindingDto> findings)
    {
    }

    protected virtual void AddMoveFamilyFacts(
        string providerId,
        MoveRecordDto move,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
    }

    protected virtual void AddEconomyFamilyFacts(
        string providerId,
        ItemRecordDto item,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        facts.Add(VerifiedSigned(providerId, "wattsPrice", "Watts price", item.WattsPrice, "watts", record));
        facts.Add(VerifiedSigned(providerId, "alternatePriceValue", "Stored alternate price value", item.AlternatePrice, null, record));
    }

    protected virtual void AddPokedexFamilyFacts(
        string providerId,
        PokemonRecordDto pokemon,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        facts.Add(VerifiedSigned(providerId, "regionalDexIndex", "Regional Dex index", pokemon.DexPresence.RegionalDexIndex, null, record));
        facts.Add(VerifiedSigned(providerId, "armorDexIndex", "Armor Dex index", pokemon.DexPresence.ArmorDexIndex, null, record));
        facts.Add(VerifiedSigned(providerId, "crownDexIndex", "Crown Dex index", pokemon.DexPresence.CrownDexIndex, null, record));
    }

    protected abstract EncounterWeightContext GetEncounterWeightContext(EncounterTableRecordDto table);

    private IReadOnlyList<BalanceLabFactDto> EvolutionEvidenceFacts(
        string providerId,
        PokemonEvolutionRecordDto evolution,
        SemanticRecordRefDto evolutionRecord)
    {
        return
        [
            VerifiedSigned(providerId, "evolutionTargetSpeciesId", "Evolution target species ID", evolution.Species, null, evolutionRecord),
            VerifiedSigned(providerId, "evolutionTargetForm", "Evolution target form", evolution.Form, null, evolutionRecord),
            VerifiedSigned(providerId, "evolutionMethod", "Evolution method", evolution.Method, null, evolutionRecord),
        ];
    }

    protected EncounterWeightContext AbsolutePercentageContext(EncounterTableRecordDto table)
    {
        var total = table.Slots.Sum(slot => (long)slot.Weight);
        var valid = table.Slots.All(slot => slot.Weight >= 0) && total == 100;
        return new EncounterWeightContext(
            "absolutePercentage",
            100,
            valid,
            valid
                ? null
                : new EncounterContextFinding(
                    "absolute-percentage-total-invalid",
                    "Encounter percentages do not total 100",
                    "This native percentage table is not normalized because its loaded values do not form an exact nonnegative total of 100.",
                    SemanticConfidenceDto.Verified));
    }

    protected EncounterWeightContext RelativeWeightContext(
        EncounterTableRecordDto table,
        bool ownershipVerified,
        string unavailableSummary)
    {
        if (!ownershipVerified)
        {
            return new EncounterWeightContext(
                "unavailable",
                null,
                CanDeriveShare: false,
                new EncounterContextFinding(
                    "weight-semantics-unavailable",
                    "Weight semantics are unavailable",
                    unavailableSummary,
                    SemanticConfidenceDto.Unknown));
        }

        var total = table.Slots.Sum(slot => (long)slot.Weight);
        var valid = table.Slots.All(slot => slot.Weight >= 0) && total > 0;
        return new EncounterWeightContext(
            "relativeWeight",
            total,
            valid,
            valid
                ? null
                : new EncounterContextFinding(
                    "relative-weight-denominator-invalid",
                    "Relative-weight denominator is unavailable",
                    "The loaded relative weights are negative or have no positive total, so an effective listed share is not derived.",
                    SemanticConfidenceDto.Verified));
    }

    protected BalanceLabFactDto VerifiedSigned(
        string providerId,
        string factKey,
        string label,
        long value,
        string? unit,
        SemanticRecordRefDto evidence)
    {
        var canonical = value.ToString(CultureInfo.InvariantCulture);
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.SignedInteger, canonical, canonical), unit, SemanticConfidenceDto.Verified, [evidence]);
    }

    protected BalanceLabFactDto DerivedSigned(
        string providerId,
        string factKey,
        string label,
        long value,
        string? unit,
        IReadOnlyList<SemanticRecordRefDto> evidence)
    {
        var canonical = value.ToString(CultureInfo.InvariantCulture);
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.SignedInteger, canonical, canonical), unit, SemanticConfidenceDto.Derived, evidence);
    }

    protected BalanceLabFactDto DerivedDecimal(
        string providerId,
        string factKey,
        string label,
        double value,
        string? unit,
        IReadOnlyList<SemanticRecordRefDto> evidence)
    {
        var canonical = value.ToString("0.######", CultureInfo.InvariantCulture);
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.Decimal, canonical, canonical), unit, SemanticConfidenceDto.Derived, evidence);
    }

    protected BalanceLabFactDto VerifiedDecimal(
        string providerId,
        string factKey,
        string label,
        double value,
        string? unit,
        SemanticRecordRefDto evidence)
    {
        if (!double.IsFinite(value))
        {
            throw new SemanticExploreValidationException(
                "A verified Balance Lab decimal fact is not finite.",
                SemanticExploreFailureKind.InvalidData);
        }

        var canonical = value.ToString("R", CultureInfo.InvariantCulture);
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.Decimal, canonical, canonical), unit, SemanticConfidenceDto.Verified, [evidence]);
    }

    protected BalanceLabFactDto VerifiedBoolean(
        string providerId,
        string factKey,
        string label,
        bool value,
        SemanticRecordRefDto evidence)
    {
        var canonical = value ? "true" : "false";
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.Boolean, canonical, canonical), null, SemanticConfidenceDto.Verified, [evidence]);
    }

    protected BalanceLabFactDto DerivedBoolean(
        string providerId,
        string factKey,
        string label,
        bool value,
        IReadOnlyList<SemanticRecordRefDto> evidence)
    {
        var canonical = value ? "true" : "false";
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.Boolean, canonical, canonical), null, SemanticConfidenceDto.Derived, evidence);
    }

    protected BalanceLabFactDto VerifiedText(
        string providerId,
        string factKey,
        string label,
        string value,
        SemanticRecordRefDto evidence)
    {
        var safe = SafePresentation(value, 512);
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.Text, safe, safe), null, SemanticConfidenceDto.Verified, [evidence]);
    }

    protected BalanceLabFactDto VerifiedEnum(
        string providerId,
        string factKey,
        string label,
        string value,
        SemanticRecordRefDto evidence)
    {
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.Enum, value, value), null, SemanticConfidenceDto.Verified, [evidence]);
    }

    protected BalanceLabFactDto VerifiedNull(
        string providerId,
        string factKey,
        string label,
        string? unit,
        SemanticRecordRefDto evidence)
    {
        return Fact(providerId, factKey, label, new SemanticScalarValueDto(SemanticValueKindDto.Null, null, "Unavailable"), unit, SemanticConfidenceDto.Unknown, [evidence]);
    }

    private BalanceLabStudyData CompleteStudy(
        BalanceLabStudyDto study,
        IReadOnlyList<BalanceLabChartPointDto> points,
        IReadOnlyList<BalanceLabFindingDto> findings,
        WorkflowSummaryDto summary,
        IReadOnlyList<ApiDiagnostic> workflowDiagnostics)
    {
        var factCount = points.Sum(point => (long)point.Facts.Count)
            + findings.Sum(finding => (long)finding.Facts.Count);
        var evidenceCount = points.Sum(point => point.Facts.Sum(fact => (long)fact.Evidence.Count))
            + findings.Sum(finding => finding.Facts.Sum(fact => (long)fact.Evidence.Count) + finding.RelatedRecords.Count);
        ValidateProjectedBounds(points.Count, factCount, evidenceCount, findings.Count);

        var sourceDiagnostics = summary.Diagnostics.Concat(workflowDiagnostics).Distinct().ToArray();
        var diagnostics = ScrubDiagnostics(study, sourceDiagnostics);
        if (points.Count == 0)
        {
            var declared = Capabilities.Single(capability => capability.Study == study);
            return new BalanceLabStudyData(
                declared with
                {
                    State = SemanticCoverageStateDto.Unavailable,
                    Confidence = SemanticConfidenceDto.Unknown,
                    ReasonCode = "workflow-source-unavailable",
                },
                [],
                [],
                diagnostics,
                Cacheable: false);
        }

        var cacheable = summary.Availability != WorkflowAvailabilityDto.Disabled
            && sourceDiagnostics.Length == 0;
        return new BalanceLabStudyData(
            Capabilities.Single(capability => capability.Study == study),
            points,
            findings,
            diagnostics,
            cacheable);
    }

    private BalanceLabStudyData? WorkflowUnavailable(
        BalanceLabStudyDto study,
        WorkflowSummaryDto summary,
        IReadOnlyList<ApiDiagnostic> workflowDiagnostics)
    {
        var sourceDiagnostics = summary.Diagnostics.Concat(workflowDiagnostics).Distinct().ToArray();
        if (summary.Availability != WorkflowAvailabilityDto.Disabled
            && sourceDiagnostics.All(diagnostic => diagnostic.Severity != ApiDiagnosticSeverity.Error))
        {
            return null;
        }

        var declared = Capabilities.Single(capability => capability.Study == study);
        return new BalanceLabStudyData(
            declared with
            {
                State = SemanticCoverageStateDto.Unavailable,
                Confidence = SemanticConfidenceDto.Unknown,
                ReasonCode = summary.Availability == WorkflowAvailabilityDto.Disabled
                    ? "workflow-disabled"
                    : "workflow-source-invalid",
            },
            [],
            [],
            ScrubDiagnostics(study, sourceDiagnostics),
            Cacheable: false);
    }

    private static IReadOnlyList<ApiDiagnostic> ScrubDiagnostics(
        BalanceLabStudyDto study,
        IEnumerable<ApiDiagnostic> diagnostics)
    {
        return diagnostics
            .Select(diagnostic => new ApiDiagnostic(
                diagnostic.Severity,
                "The owning workflow reported a diagnostic while preparing this read-only analysis.",
                Domain: StudyDomain(study),
                Field: SafeDiagnosticToken(diagnostic.Field))
            {
                Code = SafeDiagnosticCode(diagnostic.Code),
            })
            .Distinct()
            .Take(MaximumDiagnostics)
            .ToArray();
    }

    private static string? SafeDiagnosticCode(string? code)
    {
        if (code is not { Length: > 0 and <= 128 }
            || !code.StartsWith("KM-", StringComparison.Ordinal))
        {
            return null;
        }

        var segments = code.Split('-');
        return segments.Length >= 2
            && string.Equals(segments[0], "KM", StringComparison.Ordinal)
            && segments.Skip(1).All(segment =>
                segment.Length > 0
                && segment.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                    ? code
                    : null;
    }

    private static string? SafeDiagnosticToken(string? value)
    {
        return value is { Length: > 0 and <= 128 }
            && value == value.Trim()
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
                ? value
                : null;
    }

    private static string StudyDomain(BalanceLabStudyDto study)
    {
        return study switch
        {
            BalanceLabStudyDto.TrainerProgression => TrainersDomain,
            BalanceLabStudyDto.EncounterDistribution => EncountersDomain,
            BalanceLabStudyDto.MoveBalance => MovesDomain,
            BalanceLabStudyDto.Economy => ItemsDomain,
            BalanceLabStudyDto.PokedexEvolution => PokemonDomain,
            _ => throw new ArgumentOutOfRangeException(nameof(study), study, null),
        };
    }

    private static void ValidateProjectedBounds(
        long pointCount,
        long factCount,
        long evidenceCount,
        long findingCount)
    {
        var projectedBytes = checked(
            pointCount * 512L
            + factCount * 512L
            + evidenceCount * 256L
            + findingCount * 1_024L);
        if (pointCount > MaximumPoints
            || factCount > MaximumFacts
            || evidenceCount > MaximumEvidenceRecords
            || findingCount > MaximumFindings
            || projectedBytes > MaximumProjectedBytes)
        {
            throw new SemanticExploreValidationException(
                "The Balance Lab result exceeds its bounded analysis limits.",
                SemanticExploreFailureKind.LimitExceeded);
        }
    }

    private BalanceLabStudyCapabilityDto Capability(
        BalanceLabStudyDto study,
        SemanticConfidenceDto confidence,
        string reasonCode)
    {
        return new BalanceLabStudyCapabilityDto(
            study,
            ProviderId(study),
            SemanticCoverageStateDto.Partial,
            confidence,
            reasonCode);
    }

    private string ProviderId(BalanceLabStudyDto study)
    {
        var studyKey = study switch
        {
            BalanceLabStudyDto.TrainerProgression => "trainer-progression",
            BalanceLabStudyDto.EncounterDistribution => "encounter-distribution",
            BalanceLabStudyDto.MoveBalance => "move-balance",
            BalanceLabStudyDto.Economy => "economy",
            BalanceLabStudyDto.PokedexEvolution => "pokedex-evolution",
            _ => throw new ArgumentOutOfRangeException(nameof(study), study, null),
        };
        return $"{FamilyKey}.balance-lab.{studyKey}";
    }

    private static BalanceLabFactDto Fact(
        string providerId,
        string factKey,
        string label,
        SemanticScalarValueDto value,
        string? unit,
        SemanticConfidenceDto confidence,
        IReadOnlyList<SemanticRecordRefDto> evidence)
    {
        return new BalanceLabFactDto(
            $"{providerId}.fact.{factKey}",
            SafePresentation(label, 128),
            value with { DisplayValue = SafePresentation(value.DisplayValue, 512) },
            unit,
            confidence,
            providerId,
            evidence.Distinct().Take(16).ToArray());
    }

    private static BalanceLabChartPointDto Point(
        string providerId,
        string pointKey,
        string seriesKey,
        string label,
        SemanticRecordRefDto record,
        IReadOnlyList<BalanceLabFactDto> facts)
    {
        return new BalanceLabChartPointDto(
            $"{providerId}.point.{pointKey}",
            seriesKey,
            SafePresentation(label, 256),
            record,
            facts.Take(32).ToArray());
    }

    protected static BalanceLabFindingDto Finding(
        string providerId,
        string findingKey,
        string ruleKey,
        BalanceLabFindingSeverityDto severity,
        SemanticConfidenceDto confidence,
        string title,
        string summary,
        SemanticRecordRefDto record,
        IReadOnlyList<SemanticRecordRefDto> relatedRecords,
        IReadOnlyList<BalanceLabFactDto> facts)
    {
        return new BalanceLabFindingDto(
            $"{providerId}.finding.{findingKey}",
            $"{providerId}.rule.{ruleKey}",
            severity,
            confidence,
            SafePresentation(title, 256),
            SafePresentation(summary, 1_024),
            record,
            relatedRecords.Distinct().Take(16).ToArray(),
            facts.Take(32).ToArray());
    }

    private SemanticRecordRefDto TrainerPartyRecord(int trainerId, int slot)
    {
        return NumericRecord(TrainersDomain, "trainer", trainerId, $"party-slot:{slot.ToString(CultureInfo.InvariantCulture)}");
    }

    private SemanticRecordRefDto NumericRecord(string domain, string kind, int id, string? subrecordId)
    {
        if (id < 0)
        {
            throw new SemanticExploreValidationException(
                "A Balance Lab provider returned a negative numeric record identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        return TextRecord(domain, kind, id.ToString(CultureInfo.InvariantCulture), subrecordId);
    }

    private SemanticRecordRefDto TextRecord(string domain, string kind, string id, string? subrecordId)
    {
        var safeId = StableIdentity(id);
        var safeSubrecord = subrecordId is null ? null : StableIdentity(subrecordId);
        return new SemanticRecordRefDto(
            GameFamily,
            domain,
            new SemanticRecordKindDto(kind, RecordSchemaVersion),
            safeId,
            safeSubrecord);
    }

    private static string StableIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 1_024
            || value.Any(character => char.IsControl(character) || IsUnsafeUnicode(character))
            || ContainsLocalPathSignature(value))
        {
            throw new SemanticExploreValidationException(
                "A Balance Lab provider returned an unsafe stable record identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        return value;
    }

    private static bool ContainsLocalPathSignature(string value)
    {
        if (value.Contains('\\')
            || value.Split('|').Any(component =>
                component.Contains('/')
                && !string.Equals(component, "Scarlet/Violet", StringComparison.Ordinal)))
        {
            return true;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (index + 1 < value.Length
                && char.IsAsciiLetter(value[index])
                && value[index + 1] == ':'
                && (index == 0 || value[index - 1] == '|'))
            {
                return true;
            }
        }

        return false;
    }

    protected static string StableComponent(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes.AsSpan(0, 12));
    }

    private static string SafePresentation(string value, int maximumLength)
    {
        var safe = new string(value.Select(character => IsUnsafeUnicode(character) ? ' ' : character).ToArray()).Trim();
        if (safe.Length == 0)
        {
            return "Unnamed";
        }

        if (safe.Length <= maximumLength)
        {
            return safe;
        }

        var length = char.IsHighSurrogate(safe[maximumLength - 1]) ? maximumLength - 1 : maximumLength;
        return safe[..length];
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

    private static int SumStats(TrainerPokemonStatsDto stats)
    {
        return checked(stats.HP + stats.Attack + stats.Defense + stats.SpecialAttack + stats.SpecialDefense + stats.Speed);
    }

    private static bool AreStatsNonnegative(TrainerPokemonStatsDto stats)
    {
        return stats.HP >= 0
            && stats.Attack >= 0
            && stats.Defense >= 0
            && stats.SpecialAttack >= 0
            && stats.SpecialDefense >= 0
            && stats.Speed >= 0;
    }

    private static int? PercentileNearestRank(IReadOnlyList<int> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        var rank = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Count), 1, sortedValues.Count);
        return sortedValues[rank - 1];
    }

    protected sealed record EncounterWeightContext(
        string Semantics,
        long? Denominator,
        bool CanDeriveShare,
        EncounterContextFinding? Finding);

    protected sealed record EncounterContextFinding(
        string RuleId,
        string Title,
        string Summary,
        SemanticConfidenceDto Confidence);
}

internal sealed class SwShBalanceLabProvider : BalanceLabFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.SwordShield;

    protected override string FamilyKey => "swsh";

    protected override string EncounterCoverageReason => "story-phase-and-placement-coverage-unavailable";

    protected override bool IsTrainerPartySlotSupported(int slot)
    {
        return slot is >= 1 and <= 6;
    }

    protected override int TrainerPartySlotDisplayNumber(int slot)
    {
        return slot;
    }

    protected override string TrainerPartySlotIdentityRange => "1 through 6";

    protected override int EncounterSlotDisplayNumber(int slot)
    {
        return slot;
    }

    protected override EncounterWeightContext GetEncounterWeightContext(EncounterTableRecordDto table)
    {
        return AbsolutePercentageContext(table);
    }

    protected override void AddEncounterFamilyFindings(
        string providerId,
        EncounterTableRecordDto table,
        EncounterSlotRecordDto slot,
        SemanticRecordRefDto record,
        IReadOnlyList<BalanceLabFactDto> facts,
        ICollection<BalanceLabFindingDto> findings)
    {
        var stableTableComponent = StableComponent(table.TableId);
        var stableSlot = slot.Slot.ToString(CultureInfo.InvariantCulture);
        if (slot.SpeciesId == 0 && slot.Weight != 0)
        {
            findings.Add(Finding(
                providerId,
                $"encounter:{stableTableComponent}:slot:{stableSlot}:empty-species-weight",
                "empty-species-nonzero-weight",
                BalanceLabFindingSeverityDto.Warning,
                SemanticConfidenceDto.Verified,
                "Empty encounter slot has nonzero weight",
                "The loaded Sword and Shield slot stores species ID 0 with a nonzero weight, violating the editor's exact empty-slot structural rule.",
                record,
                [],
                facts.Where(fact => fact.FactId.EndsWith("speciesId", StringComparison.Ordinal) || fact.FactId.EndsWith("nativeWeight", StringComparison.Ordinal)).ToArray()));
        }

        if (slot.SpeciesId == 0 && slot.Form != 0)
        {
            findings.Add(Finding(
                providerId,
                $"encounter:{stableTableComponent}:slot:{stableSlot}:empty-species-form",
                "empty-species-nonzero-form",
                BalanceLabFindingSeverityDto.Warning,
                SemanticConfidenceDto.Verified,
                "Empty encounter slot has nonzero form",
                "The loaded Sword and Shield slot stores species ID 0 with a nonzero form, violating the editor's exact empty-slot structural rule.",
                record,
                [],
                facts.Where(fact => fact.FactId.EndsWith("speciesId", StringComparison.Ordinal) || fact.FactId.EndsWith("form", StringComparison.Ordinal)).ToArray()));
        }
    }
}

internal sealed class SvBalanceLabProvider : BalanceLabFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.ScarletViolet;

    protected override string FamilyKey => "sv";

    protected override string EncounterCoverageReason => "story-phase-and-placement-coverage-unavailable";

    protected override EncounterWeightContext GetEncounterWeightContext(EncounterTableRecordDto table)
    {
        return RelativeWeightContext(
            table,
            ownershipVerified: true,
            "The selected Scarlet and Violet encounter row has no verified lot-weight ownership.");
    }

    protected override void AddEconomyFamilyFacts(
        string providerId,
        ItemRecordDto item,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        facts.Add(VerifiedSigned(providerId, "battlePointPrice", "Battle Point price", item.WattsPrice, "BP", record));
    }

    protected override void AddPokedexFamilyFacts(
        string providerId,
        PokemonRecordDto pokemon,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        facts.Add(VerifiedSigned(providerId, "paldeaDexIndex", "Paldea Dex index", pokemon.DexPresence.RegionalDexIndex, null, record));
        facts.Add(VerifiedSigned(providerId, "kitakamiDexIndex", "Kitakami Dex index", pokemon.DexPresence.ArmorDexIndex, null, record));
        facts.Add(VerifiedSigned(providerId, "blueberryDexIndex", "Blueberry Dex index", pokemon.DexPresence.CrownDexIndex, null, record));
    }
}

internal sealed class ZaBalanceLabProvider : BalanceLabFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.LegendsZA;

    protected override string FamilyKey => "za";

    protected override string TrainerCoverageReason => "move-legality-and-full-story-order-unavailable";

    protected override string EncounterCoverageReason => "eligibility-filters-population-caps-and-coordinates-unavailable";

    protected override bool HasVerifiedTrainerBattleType => false;

    protected override string TrainerAggregateSeriesKey(TrainerRecordDto trainer)
    {
        return trainer.ZaRank is > 0 ? "trainer-rank-band" : "trainer-roster";
    }

    protected override void AddTrainerProgressionFact(
        string providerId,
        TrainerRecordDto trainer,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        if (trainer.ZaRank is not null)
        {
            facts.Add(VerifiedSigned(providerId, "royaleRank", "Royale rank", trainer.ZaRank.Value, "rank", record));
        }

        if (trainer.ZaMegaEvolution is not null)
        {
            facts.Add(VerifiedBoolean(
                providerId,
                "megaEvolutionEnabled",
                "Mega Evolution enabled",
                trainer.ZaMegaEvolution.Value,
                record));
        }
    }

    protected override void AddEncounterFamilyFacts(
        string providerId,
        EncounterSlotRecordDto slot,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        facts.Add(VerifiedBoolean(providerId, "isAlpha", "Alpha", slot.IsAlpha, record));
        if (slot.AlphaChancePercent is not null)
        {
            facts.Add(VerifiedSigned(providerId, "alphaChancePercent", "Alpha chance", slot.AlphaChancePercent.Value, "percent", record));
        }

        if (slot.SlotMaxCount is not null)
        {
            facts.Add(VerifiedSigned(providerId, "slotMaxCount", "Slot maximum", slot.SlotMaxCount.Value, "pokemon", record));
        }
    }

    protected override void AddMoveFamilyFacts(
        string providerId,
        MoveRecordDto move,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        if (move.Timing is null)
        {
            return;
        }

        facts.Add(VerifiedDecimal(providerId, "cooldown", "Cooldown", move.Timing.Cooldown, "seconds", record));
        facts.Add(VerifiedDecimal(providerId, "effectiveRange", "Effective range", move.Timing.EffectiveRange, "distance", record));
        facts.Add(VerifiedSigned(providerId, "runtimeHitPercent", "Runtime hit percent", move.Timing.HitPercent, "percent", record));
    }

    protected override void AddEconomyFamilyFacts(
        string providerId,
        ItemRecordDto item,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        facts.Add(VerifiedSigned(providerId, "megaShardPrice", "Mega Shard price", item.WattsPrice, "mega shards", record));
        facts.Add(VerifiedSigned(providerId, "colorfulScrewPrice", "Colorful Screw price", item.AlternatePrice, "colorful screws", record));
    }

    protected override void AddPokedexFamilyFacts(
        string providerId,
        PokemonRecordDto pokemon,
        SemanticRecordRefDto record,
        ICollection<BalanceLabFactDto> facts)
    {
        facts.Add(VerifiedSigned(providerId, "zaDexOrder", "Z-A Dex order", pokemon.DexPresence.RegionalDexIndex, null, record));
    }

    protected override EncounterWeightContext GetEncounterWeightContext(EncounterTableRecordDto table)
    {
        var hasVerifiedSpawnerOwnership = table.Slots.Count > 0 && table.Slots.All(slot => slot.CanEditWeight == true);
        return RelativeWeightContext(
            table,
            hasVerifiedSpawnerOwnership,
            "This Z-A encounter table does not expose verified spawner-local weight ownership, so listed shares are not derived.");
    }
}
