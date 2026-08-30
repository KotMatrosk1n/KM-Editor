// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Diagnostics;
using KM.Api.Encounters;
using KM.Api.GuidedDesign;
using KM.Api.Items;
using KM.Api.Pokemon;
using KM.Api.Semantics;
using KM.Api.Trainers;
using KM.Api.Workflows;

namespace KM.Tools.Application;

public abstract record GuidedDesignStagingEdit(SemanticRecordRefDto Record);

public sealed record GuidedDesignScalarStagingEdit(
    SemanticRecordRefDto Record,
    string Field,
    int Value) : GuidedDesignStagingEdit(Record);

public sealed record GuidedDesignEvolutionStagingEdit(
    SemanticRecordRefDto Record,
    int Slot,
    int Method,
    int Argument,
    int Species,
    int Form,
    int Level) : GuidedDesignStagingEdit(Record);

public sealed record GuidedDesignStagingResult(
    KM.Core.Editing.EditSession Session,
    bool IsValid);

internal sealed record GuidedDesignProviderBuild(
    GuidedDesignInputDto NormalizedInput,
    string? Seed,
    string ProviderId,
    bool SelectionRequired,
    IReadOnlyList<GuidedDesignTargetOptionDto> EligibleTargets,
    IReadOnlyList<GuidedDesignMutationDto> Mutations,
    IReadOnlyList<GuidedDesignFindingDto> Findings,
    IReadOnlyList<SemanticRecordRefDto> AffectedRecords,
    IReadOnlyList<GuidedDesignStagingEdit> StagingEdits,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

internal static class GuidedDesignProviders
{
    internal const string GeneratedEditOwner = "guided-design.v1";
    internal const string PendingOverlayDiagnosticCode =
        "KM-GUIDED-DESIGN-PENDING-OVERLAY-UNAVAILABLE";
    internal const string ProposalBlockedDiagnosticCode =
        "KM-GUIDED-DESIGN-PROPOSAL-BLOCKED";
    internal const string NoEffectiveChangeDiagnosticCode =
        "KM-GUIDED-DESIGN-NO-EFFECTIVE-CHANGE";
    internal const string TargetSelectionRequiredDiagnosticCode =
        "KM-GUIDED-DESIGN-TARGET-SELECTION-REQUIRED";

    private const int RecordSchemaVersion = 1;
    private const string TrainersDomain = "workflow.trainers";
    private const string EncountersDomain = "workflow.encounters";
    private const string ItemsDomain = "workflow.items";
    private const string PokemonDomain = "workflow.pokemon";
    private static readonly string[] StatFields =
    [
        "hp",
        "attack",
        "defense",
        "specialAttack",
        "specialDefense",
        "speed",
    ];
    private static readonly string[] EvFields =
    [
        "evHp",
        "evAttack",
        "evDefense",
        "evSpecialAttack",
        "evSpecialDefense",
        "evSpeed",
    ];

    public static IReadOnlyList<GuidedDesignCapabilityDto> Capabilities(
        SemanticGameFamilyDto family)
    {
        var familyKey = FamilyKey(family);
        var trainerKinds = family == SemanticGameFamilyDto.SwordShield
            ? Array.Empty<GuidedDesignProposalKindDto>()
            : [GuidedDesignProposalKindDto.TrainerLevelAdjustment];
        var encounterKinds = family switch
        {
            SemanticGameFamilyDto.SwordShield =>
                [GuidedDesignProposalKindDto.EncounterLevelAdjustment],
            SemanticGameFamilyDto.ScarletViolet or SemanticGameFamilyDto.LegendsZA =>
            [
                GuidedDesignProposalKindDto.EncounterLevelAdjustment,
                GuidedDesignProposalKindDto.EncounterWeightScale,
            ],
            _ => Array.Empty<GuidedDesignProposalKindDto>(),
        };
        var trainerArchetypeKinds = family == SemanticGameFamilyDto.SwordShield
            ? Array.Empty<GuidedDesignProposalKindDto>()
            : [GuidedDesignProposalKindDto.TrainerEvArchetype];
        var evolutionKinds = family == SemanticGameFamilyDto.LegendsZA
            ? [GuidedDesignProposalKindDto.EvolutionLevelClamp]
            : Array.Empty<GuidedDesignProposalKindDto>();
        var supported = trainerKinds
            .Concat(encounterKinds)
            .Concat([GuidedDesignProposalKindDto.EconomyPrimaryPriceScale])
            .Concat(evolutionKinds)
            .Concat(trainerArchetypeKinds)
            .Concat([GuidedDesignProposalKindDto.PokemonBaseStatShuffle])
            .Distinct()
            .Order()
            .ToArray();

        return
        [
            Capability(
                familyKey,
                GuidedDesignFeatureDto.DifficultyDesigner,
                trainerKinds.Length == 0 ? SemanticCoverageStateDto.Unavailable : SemanticCoverageStateDto.Partial,
                trainerKinds.Length == 0 ? SemanticConfidenceDto.Unknown : SemanticConfidenceDto.Verified,
                trainerKinds.Length == 0
                    ? "atomic-trainer-batch-unavailable"
                    : "progression-order-and-move-legality-unavailable",
                trainerKinds),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.EncounterPopulationDesigner,
                SemanticCoverageStateDto.Partial,
                SemanticConfidenceDto.Verified,
                family == SemanticGameFamilyDto.SwordShield
                    ? "probability-normalization-provider-unavailable"
                    : "habitat-and-species-coverage-unavailable",
                encounterKinds),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.EconomyRebalance,
                SemanticCoverageStateDto.Partial,
                SemanticConfidenceDto.Verified,
                "acquisition-and-reward-coverage-unavailable",
                [GuidedDesignProposalKindDto.EconomyPrimaryPriceScale]),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.EvolutionAccessibility,
                evolutionKinds.Length == 0 ? SemanticCoverageStateDto.Unavailable : SemanticCoverageStateDto.Partial,
                evolutionKinds.Length == 0 ? SemanticConfidenceDto.Unknown : SemanticConfidenceDto.Verified,
                evolutionKinds.Length == 0
                    ? "verified-level-method-metadata-unavailable"
                    : "overall-obtainability-coverage-unavailable",
                evolutionKinds),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.TrainerArchetypes,
                trainerArchetypeKinds.Length == 0 ? SemanticCoverageStateDto.Unavailable : SemanticCoverageStateDto.Partial,
                trainerArchetypeKinds.Length == 0 ? SemanticConfidenceDto.Unknown : SemanticConfidenceDto.Verified,
                trainerArchetypeKinds.Length == 0
                    ? "atomic-trainer-batch-unavailable"
                    : "move-legality-and-role-coverage-unavailable",
                trainerArchetypeKinds),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.ConstraintRandomization,
                SemanticCoverageStateDto.Partial,
                SemanticConfidenceDto.Verified,
                "broader-randomization-mechanics-unavailable",
                [GuidedDesignProposalKindDto.PokemonBaseStatShuffle]),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.Plando,
                SemanticCoverageStateDto.Partial,
                SemanticConfidenceDto.Verified,
                "supported-fields-only",
                supported),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.SeedInspector,
                SemanticCoverageStateDto.Complete,
                SemanticConfidenceDto.Verified,
                null,
                [GuidedDesignProposalKindDto.PokemonBaseStatShuffle]),
            Capability(
                familyKey,
                GuidedDesignFeatureDto.SpoilerRaceExport,
                SemanticCoverageStateDto.Partial,
                SemanticConfidenceDto.Derived,
                "replay-and-hiding-commitment-contract-unavailable",
                supported),
        ];
    }

    public static IReadOnlyList<GuidedDesignFieldCatalogDto> FieldCatalogs(
        SemanticGameFamilyDto family,
        IEnumerable<GuidedDesignProposalKindDto> availableKinds)
    {
        ArgumentNullException.ThrowIfNull(availableKinds);
        return availableKinds
            .Distinct()
            .Order()
            .Select(kind =>
            {
                var fields = FieldsFor(family, kind);
                var selectionMode = kind == GuidedDesignProposalKindDto.PokemonBaseStatShuffle
                    ? GuidedDesignFieldSelectionModeDto.Subset
                    : GuidedDesignFieldSelectionModeDto.Fixed;
                return new GuidedDesignFieldCatalogDto(
                    kind,
                    selectionMode,
                    selectionMode == GuidedDesignFieldSelectionModeDto.Subset
                        ? 2
                        : fields.Length,
                    fields);
            })
            .ToArray();
    }

    private static string[] FieldsFor(
        SemanticGameFamilyDto family,
        GuidedDesignProposalKindDto kind)
    {
        return kind switch
        {
            GuidedDesignProposalKindDto.TrainerLevelAdjustment =>
                family == SemanticGameFamilyDto.SwordShield
                    ? throw Unsupported(
                        "The selected family has no verified trainer-level provider.")
                    : ["level"],
            GuidedDesignProposalKindDto.EncounterLevelAdjustment => ["levelMin", "levelMax"],
            GuidedDesignProposalKindDto.EncounterWeightScale => family switch
            {
                SemanticGameFamilyDto.ScarletViolet => ["probability"],
                SemanticGameFamilyDto.LegendsZA => ["weight"],
                _ => throw Unsupported(
                    "The selected family has no verified encounter-weight field."),
            },
            GuidedDesignProposalKindDto.EconomyPrimaryPriceScale =>
                family == SemanticGameFamilyDto.LegendsZA ? ["price"] : ["buyPrice"],
            GuidedDesignProposalKindDto.EvolutionLevelClamp =>
                family == SemanticGameFamilyDto.LegendsZA
                    ? ["level"]
                    : throw Unsupported(
                        "The selected family has no verified evolution-level provider."),
            GuidedDesignProposalKindDto.TrainerEvArchetype =>
                family == SemanticGameFamilyDto.SwordShield
                    ? throw Unsupported(
                        "The selected family has no verified trainer-EV provider.")
                    : [.. EvFields],
            GuidedDesignProposalKindDto.PokemonBaseStatShuffle => [.. StatFields],
            _ => throw Unsupported("The selected Guided Design field catalog is unavailable."),
        };
    }

    public static string DomainFor(GuidedDesignProposalKindDto kind)
    {
        return kind switch
        {
            GuidedDesignProposalKindDto.TrainerLevelAdjustment or
            GuidedDesignProposalKindDto.TrainerEvArchetype => TrainersDomain,
            GuidedDesignProposalKindDto.EncounterLevelAdjustment or
            GuidedDesignProposalKindDto.EncounterWeightScale => EncountersDomain,
            GuidedDesignProposalKindDto.EconomyPrimaryPriceScale => ItemsDomain,
            GuidedDesignProposalKindDto.EvolutionLevelClamp or
            GuidedDesignProposalKindDto.PokemonBaseStatShuffle => PokemonDomain,
            _ => throw Unsupported("The selected Guided Design proposal kind is unsupported."),
        };
    }

    public static GuidedDesignProviderBuild Build(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        Func<TrainersWorkflowDto> loadTrainers,
        Func<EncountersWorkflowDto> loadEncounters,
        Func<ItemsWorkflowDto> loadItems,
        Func<PokemonWorkflowDto> loadPokemon,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw Invalid("The Guided Design input is malformed.");
        }
        ValidateCommonInput(input);
        EnsureKindSupported(family, input.Kind);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return input.Kind switch
            {
                GuidedDesignProposalKindDto.TrainerLevelAdjustment => BuildTrainerLevels(
                    family,
                    input,
                    loadTrainers(),
                    cancellationToken),
                GuidedDesignProposalKindDto.EncounterLevelAdjustment => BuildEncounterLevels(
                    family,
                    input,
                    loadEncounters(),
                    cancellationToken),
                GuidedDesignProposalKindDto.EncounterWeightScale => BuildEncounterWeights(
                    family,
                    input,
                    loadEncounters(),
                    cancellationToken),
                GuidedDesignProposalKindDto.EconomyPrimaryPriceScale => BuildEconomy(
                    family,
                    input,
                    loadItems(),
                    cancellationToken),
                GuidedDesignProposalKindDto.EvolutionLevelClamp => BuildEvolutionClamp(
                    family,
                    input,
                    loadPokemon(),
                    cancellationToken),
                GuidedDesignProposalKindDto.TrainerEvArchetype => BuildTrainerArchetype(
                    family,
                    input,
                    loadTrainers(),
                    cancellationToken),
                GuidedDesignProposalKindDto.PokemonBaseStatShuffle => BuildStatShuffle(
                    family,
                    input,
                    loadPokemon(),
                    cancellationToken),
                _ => throw Unsupported("The selected Guided Design proposal kind is unsupported."),
            };
        }
        catch (OverflowException exception)
        {
            throw new SemanticExploreValidationException(
                "A Guided Design numeric proposal exceeds its supported integer range.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }
    }

    private static GuidedDesignProviderBuild BuildTrainerLevels(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        TrainersWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        RequireEditable(workflow.Summary, workflow.Diagnostics, TrainersDomain);
        RequireOnly(input, delta: true);
        var delta = input.Delta
            ?? throw Invalid("Trainer level adjustment requires an integer delta.");
        RequireRange(delta, -100, 100, "Trainer level delta");
        var field = RequireField(workflow.EditableFields, "level");
        var eligible = workflow.Trainers
            .OrderBy(trainer => trainer.TrainerId)
            .SelectMany(trainer => trainer.Team
                .Where(member => member.SpeciesId > 0)
                .OrderBy(member => member.Slot)
                .Select(member => new TrainerTarget(
                    TrainerRecord(family, trainer.TrainerId, member.Slot),
                    trainer,
                    member)))
            .ToArray();
        var providerId = ProviderId(family, input.Kind);
        if (input.Targets.Count == 0)
        {
            return Selection(
                input,
                providerId,
                ["level"],
                eligible.Select(target => TargetOption(
                    target.Record,
                    $"{target.Trainer.Name} party slot {TrainerSlotDisplay(family, target.Member.Slot).ToString(CultureInfo.InvariantCulture)}")));
        }

        var selected = SelectTargets(input.Targets, eligible, target => target.Record);
        var normalized = NormalizeInput(input, selected.Select(target => target.Record), ["level"]);
        var pins = CreatePinMap(normalized, ["level"]);
        var mutations = new List<GuidedDesignMutationDto>();
        var edits = new List<GuidedDesignStagingEdit>();
        foreach (var target in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = target.Member.Level;
            var pinned = TryPinned(pins, target.Record, "level", out var pinnedValue);
            var after = pinned ? ToInt(pinnedValue!, "Trainer level pin") : checked(before + delta);
            ValidateFieldValue(field, after);
            AddScalarChange(
                mutations,
                edits,
                providerId,
                target.Record,
                $"{SafePresentation(target.Trainer.Name, 220)} party slot {TrainerSlotDisplay(family, target.Member.Slot).ToString(CultureInfo.InvariantCulture)}",
                "level",
                "Level",
                before,
                after,
                pinned,
                "Adjust trainer party level.");
        }

        return Complete(normalized, null, providerId, mutations, [], edits);
    }

    private static GuidedDesignProviderBuild BuildTrainerArchetype(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        TrainersWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        RequireEditable(workflow.Summary, workflow.Diagnostics, TrainersDomain);
        RequireOnly(input, archetype: true);
        var archetype = input.Archetype
            ?? throw Invalid("Trainer archetype design requires an archetype.");
        if (!Enum.IsDefined(archetype))
        {
            throw Invalid("The trainer archetype is invalid.");
        }

        var fields = EvFields.ToDictionary(
            key => key,
            key => RequireField(workflow.EditableFields, key),
            StringComparer.Ordinal);
        var eligible = workflow.Trainers
            .OrderBy(trainer => trainer.TrainerId)
            .SelectMany(trainer => trainer.Team
                .Where(member => member.SpeciesId > 0)
                .OrderBy(member => member.Slot)
                .Select(member => new TrainerTarget(
                    TrainerRecord(family, trainer.TrainerId, member.Slot),
                    trainer,
                    member)))
            .ToArray();
        var providerId = ProviderId(family, input.Kind);
        if (input.Targets.Count == 0)
        {
            return Selection(
                input,
                providerId,
                EvFields,
                eligible.Select(target => TargetOption(
                    target.Record,
                    $"{target.Trainer.Name} party slot {TrainerSlotDisplay(family, target.Member.Slot).ToString(CultureInfo.InvariantCulture)}")));
        }

        var selected = SelectTargets(input.Targets, eligible, target => target.Record);
        var normalized = NormalizeInput(input, selected.Select(target => target.Record), EvFields);
        var pins = CreatePinMap(normalized, EvFields);
        var mutations = new List<GuidedDesignMutationDto>();
        var edits = new List<GuidedDesignStagingEdit>();
        foreach (var target in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var desired = archetype switch
            {
                GuidedDesignTrainerArchetypeDto.PhysicalAttackSpeed =>
                    new[] { 4, 252, 0, 0, 0, 252 },
                GuidedDesignTrainerArchetypeDto.SpecialAttackSpeed =>
                    new[] { 4, 0, 0, 252, 0, 252 },
                GuidedDesignTrainerArchetypeDto.Balanced =>
                    new[] { 85, 85, 85, 85, 85, 85 },
                _ => throw Invalid("The trainer archetype is invalid."),
            };
            var before = TrainerEvValues(target.Member.Evs);
            for (var index = 0; index < EvFields.Length; index++)
            {
                if (TryPinned(pins, target.Record, EvFields[index], out var pinnedValue))
                {
                    desired[index] = ToInt(pinnedValue!, "Trainer EV pin");
                }

                ValidateFieldValue(fields[EvFields[index]], desired[index]);
                RequireRange(desired[index], 0, 252, "Trainer EV");
            }

            if (desired.Sum() > 510)
            {
                throw Invalid("A trainer EV archetype cannot exceed the verified 510 point total.");
            }

            for (var index = 0; index < EvFields.Length; index++)
            {
                AddScalarChange(
                    mutations,
                    edits,
                    providerId,
                    target.Record,
                    $"{SafePresentation(target.Trainer.Name, 220)} party slot {TrainerSlotDisplay(family, target.Member.Slot).ToString(CultureInfo.InvariantCulture)}",
                    EvFields[index],
                    FieldLabel(EvFields[index]),
                    before[index],
                    desired[index],
                    TryPinned(pins, target.Record, EvFields[index], out _),
                    "Apply a verified trainer EV archetype.");
            }
        }

        return Complete(normalized, null, providerId, mutations, [], edits);
    }

    private static GuidedDesignProviderBuild BuildEncounterLevels(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        EncountersWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        RequireEditable(workflow.Summary, workflow.Diagnostics, EncountersDomain);
        RequireOnly(input, delta: true);
        var delta = input.Delta
            ?? throw Invalid("Encounter level adjustment requires an integer delta.");
        RequireRange(delta, -100, 100, "Encounter level delta");
        var fields = new Dictionary<string, EncounterEditableFieldDto>(StringComparer.Ordinal)
        {
            ["levelMin"] = RequireField(workflow.EditableFields, "levelMin"),
            ["levelMax"] = RequireField(workflow.EditableFields, "levelMax"),
        };
        var eligible = EncounterTargets(family, workflow).ToArray();
        var providerId = ProviderId(family, input.Kind);
        if (input.Targets.Count == 0)
        {
            return Selection(
                input,
                providerId,
                ["levelMin", "levelMax"],
                eligible.Select(target => TargetOption(target.Record, EncounterLabel(target))));
        }

        var selected = SelectTargets(input.Targets, eligible, target => target.Record);
        var normalized = NormalizeInput(input, selected.Select(target => target.Record), ["levelMin", "levelMax"]);
        var pins = CreatePinMap(normalized, ["levelMin", "levelMax"]);
        var mutations = new List<GuidedDesignMutationDto>();
        var findings = new List<GuidedDesignFindingDto>();
        var edits = new List<GuidedDesignStagingEdit>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selectedTarget in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var physicalKey = family == SemanticGameFamilyDto.LegendsZA
                && !string.IsNullOrWhiteSpace(selectedTarget.Slot.EncounterDataId)
                    ? $"encount-data:{selectedTarget.Slot.EncounterDataId}"
                    : RecordKey(selectedTarget.Record);
            if (!handled.Add(physicalKey))
            {
                continue;
            }

            var aliases = family == SemanticGameFamilyDto.LegendsZA
                && !string.IsNullOrWhiteSpace(selectedTarget.Slot.EncounterDataId)
                    ? eligible.Where(target => string.Equals(
                        target.Slot.EncounterDataId,
                        selectedTarget.Slot.EncounterDataId,
                        StringComparison.Ordinal)).ToArray()
                    : [selectedTarget];
            if (aliases.Any(alias =>
                    alias.Slot.LevelMin != selectedTarget.Slot.LevelMin
                    || alias.Slot.LevelMax != selectedTarget.Slot.LevelMax))
            {
                throw Invalid("A shared encounter level source has inconsistent loaded aliases.");
            }

            var minimumPin = ResolveAliasPin(pins, selected, aliases, "levelMin");
            var maximumPin = ResolveAliasPin(pins, selected, aliases, "levelMax");
            var afterMin = minimumPin is null
                ? checked(selectedTarget.Slot.LevelMin + delta)
                : ToInt(minimumPin.CanonicalValue, "Encounter minimum level pin");
            var afterMax = maximumPin is null
                ? checked(selectedTarget.Slot.LevelMax + delta)
                : ToInt(maximumPin.CanonicalValue, "Encounter maximum level pin");
            ValidateFieldValue(fields["levelMin"], afterMin);
            ValidateFieldValue(fields["levelMax"], afterMax);
            if (afterMin > afterMax)
            {
                throw Invalid("An encounter minimum level cannot exceed its maximum level.");
            }

            if (afterMin != selectedTarget.Slot.LevelMin
                    && afterMin > selectedTarget.Slot.LevelMax
                || afterMax != selectedTarget.Slot.LevelMax
                    && afterMax < selectedTarget.Slot.LevelMin)
            {
                throw Invalid(
                    "Encounter level constraints must keep each generated minimum or maximum edit independently valid against the loaded row.");
            }

            if (afterMin != selectedTarget.Slot.LevelMin)
            {
                edits.Add(new GuidedDesignScalarStagingEdit(
                    selectedTarget.Record,
                    "levelMin",
                    afterMin));
            }

            if (afterMax != selectedTarget.Slot.LevelMax)
            {
                edits.Add(new GuidedDesignScalarStagingEdit(
                    selectedTarget.Record,
                    "levelMax",
                    afterMax));
            }

            foreach (var alias in aliases.OrderBy(target => RecordKey(target.Record), StringComparer.Ordinal))
            {
                AddMutation(
                    mutations,
                    providerId,
                    alias.Record,
                    EncounterLabel(alias),
                    "levelMin",
                    "Minimum level",
                    alias.Slot.LevelMin,
                    afterMin,
                    minimumPin is not null,
                    "Adjust encounter minimum level.",
                    minimumPin?.Record ?? selectedTarget.Record,
                    minimumPin?.FieldKey ?? "levelMin");
                AddMutation(
                    mutations,
                    providerId,
                    alias.Record,
                    EncounterLabel(alias),
                    "levelMax",
                    "Maximum level",
                    alias.Slot.LevelMax,
                    afterMax,
                    maximumPin is not null,
                    "Adjust encounter maximum level.",
                    maximumPin?.Record ?? selectedTarget.Record,
                    maximumPin?.FieldKey ?? "levelMax");
            }

            if (aliases.Length > 1)
            {
                findings.Add(Finding(
                    providerId,
                    "shared-encounter-level-source",
                    GuidedDesignFindingSeverityDto.Warning,
                    "Shared encounter source",
                    "This level edit affects every loaded alias backed by the same verified encounter data row.",
                    selectedTarget.Record,
                    aliases.Select(alias => alias.Record).ToArray()));
            }
        }

        return Complete(normalized, null, providerId, mutations, findings, edits);
    }

    private static GuidedDesignProviderBuild BuildEncounterWeights(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        EncountersWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        RequireEditable(workflow.Summary, workflow.Diagnostics, EncountersDomain);
        RequireOnly(input, multiplier: true, rounding: true);
        var multiplier = input.MultiplierBasisPoints
            ?? throw Invalid("Encounter weight scaling requires a basis-point multiplier.");
        RequireRange(multiplier, 0, 100_000, "Encounter weight multiplier");
        var rounding = input.Rounding
            ?? throw Invalid("Encounter weight scaling requires an explicit rounding rule.");
        var fieldKey = family switch
        {
            SemanticGameFamilyDto.ScarletViolet => "probability",
            SemanticGameFamilyDto.LegendsZA => "weight",
            _ => throw Unsupported(
                "Sword and Shield encounter probability scaling is unavailable without a verified full-table normalization provider."),
        };
        var field = RequireField(workflow.EditableFields, fieldKey);
        var eligible = EncounterTargets(family, workflow)
            .Where(target => family != SemanticGameFamilyDto.LegendsZA
                || target.Slot.CanEditWeight == true)
            .ToArray();
        var providerId = ProviderId(family, input.Kind);
        if (input.Targets.Count == 0)
        {
            return Selection(
                input,
                providerId,
                [fieldKey],
                eligible.Select(target => TargetOption(target.Record, EncounterLabel(target))));
        }

        var selected = SelectTargets(input.Targets, eligible, target => target.Record);
        var normalized = NormalizeInput(input, selected.Select(target => target.Record), [fieldKey]);
        var pins = CreatePinMap(normalized, [fieldKey]);
        var mutations = new List<GuidedDesignMutationDto>();
        var edits = new List<GuidedDesignStagingEdit>();
        var proposedBySlot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var target in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = target.Slot.Weight;
            var pinned = TryPinned(pins, target.Record, fieldKey, out var pinnedValue);
            var after = pinned
                ? ToInt(pinnedValue!, "Encounter weight pin")
                : Scale(before, multiplier, rounding);
            ValidateFieldValue(field, after);
            AddScalarChange(
                mutations,
                edits,
                providerId,
                target.Record,
                EncounterLabel(target),
                fieldKey,
                FieldLabel(fieldKey),
                before,
                after,
                pinned,
                "Scale the existing encounter weight value.");
            proposedBySlot[RecordKey(target.Record)] = after;
        }

        foreach (var table in workflow.Tables.Where(table => table.Slots.Any(slot =>
                     proposedBySlot.ContainsKey(RecordKey(EncounterRecord(family, table.TableId, slot.Slot))))))
        {
            var nativeTotal = table.Slots.Sum(slot => (long)proposedBySlot.GetValueOrDefault(
                RecordKey(EncounterRecord(family, table.TableId, slot.Slot)),
                slot.Weight));
            if (nativeTotal <= 0)
            {
                throw Invalid(
                    "Encounter weight scaling must preserve a positive native relative-weight total in every affected table.");
            }
        }

        return Complete(normalized, null, providerId, mutations, [], edits);
    }

    private static GuidedDesignProviderBuild BuildEconomy(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        ItemsWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        RequireEditable(workflow.Summary, workflow.Diagnostics, ItemsDomain);
        RequireOnly(input, multiplier: true, rounding: true);
        var multiplier = input.MultiplierBasisPoints
            ?? throw Invalid("Economy scaling requires a basis-point multiplier.");
        RequireRange(multiplier, 0, 100_000, "Economy multiplier");
        var rounding = input.Rounding
            ?? throw Invalid("Economy scaling requires an explicit rounding rule.");
        var fieldKey = family == SemanticGameFamilyDto.LegendsZA ? "price" : "buyPrice";
        var field = RequireField(workflow.EditableFields, fieldKey);
        var eligible = workflow.Items
            .Where(item => item.ItemId > 0)
            .OrderBy(item => item.ItemId)
            .Select(item => new ItemTarget(ItemRecord(family, item.ItemId), item))
            .ToArray();
        var providerId = ProviderId(family, input.Kind);
        if (input.Targets.Count == 0)
        {
            return Selection(
                input,
                providerId,
                [fieldKey],
                eligible.Select(target => TargetOption(target.Record, target.Item.Name)));
        }

        var selected = SelectTargets(input.Targets, eligible, target => target.Record);
        var normalized = NormalizeInput(input, selected.Select(target => target.Record), [fieldKey]);
        var pins = CreatePinMap(normalized, [fieldKey]);
        var mutations = new List<GuidedDesignMutationDto>();
        var findings = new List<GuidedDesignFindingDto>();
        var edits = new List<GuidedDesignStagingEdit>();
        var byId = eligible.ToDictionary(target => target.Item.ItemId);
        var handled = new HashSet<int>();
        foreach (var selectedTarget in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aliases = ExpandSharedItems(selectedTarget, byId);
            var identity = aliases.Min(alias => alias.Item.ItemId);
            if (!handled.Add(identity))
            {
                continue;
            }

            if (aliases.Any(alias => alias.Item.BuyPrice != selectedTarget.Item.BuyPrice))
            {
                throw Invalid("A shared item price source has inconsistent loaded aliases.");
            }

            var pin = ResolveAliasPin(pins, selected, aliases, fieldKey);
            var before = selectedTarget.Item.BuyPrice;
            var after = pin is null
                ? Scale(before, multiplier, rounding)
                : ToInt(pin.CanonicalValue, "Item price pin");
            ValidateFieldValue(field, after);
            if (after != before)
            {
                edits.Add(new GuidedDesignScalarStagingEdit(selectedTarget.Record, fieldKey, after));
            }

            foreach (var alias in aliases.OrderBy(target => target.Item.ItemId))
            {
                AddMutation(
                    mutations,
                    providerId,
                    alias.Record,
                    SafePresentation(alias.Item.Name, 256),
                    fieldKey,
                    "Primary item price",
                    alias.Item.BuyPrice,
                    after,
                    pin is not null,
                    "Scale the existing primary item price.",
                    pin?.Record ?? selectedTarget.Record,
                    pin?.FieldKey ?? fieldKey);
            }

            if (aliases.Length > 1)
            {
                findings.Add(Finding(
                    providerId,
                    "shared-item-price-source",
                    GuidedDesignFindingSeverityDto.Warning,
                    "Shared item price source",
                    "This price edit affects every loaded item alias backed by the same verified value.",
                    selectedTarget.Record,
                    aliases.Select(alias => alias.Record).ToArray()));
            }
        }

        return Complete(normalized, null, providerId, mutations, findings, edits);
    }

    private static GuidedDesignProviderBuild BuildEvolutionClamp(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        PokemonWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        if (family != SemanticGameFamilyDto.LegendsZA)
        {
            throw Unsupported(
                "Evolution accessibility requires verified family-owned level-method metadata.");
        }

        RequireEditable(workflow.Summary, workflow.Diagnostics, PokemonDomain);
        RequireOnly(input, minimum: true, maximum: true);
        var minimum = input.MinimumValue
            ?? throw Invalid("Evolution level clamping requires a minimum value.");
        var maximum = input.MaximumValue
            ?? throw Invalid("Evolution level clamping requires a maximum value.");
        RequireRange(minimum, 0, 100, "Evolution minimum");
        RequireRange(maximum, 0, 100, "Evolution maximum");
        if (minimum > maximum)
        {
            throw Invalid("The evolution minimum cannot exceed its maximum.");
        }

        var methodOptions = workflow.EvolutionMethodOptions
            .GroupBy(option => option.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var eligible = workflow.Pokemon
            .Where(IsEligiblePokemonPersonal)
            .Where(pokemon => pokemon.Evolutions.Any(evolution =>
                methodOptions.TryGetValue(evolution.Method, out var options)
                && options.Length == 1
                && options[0].UsesLevel))
            .OrderBy(pokemon => pokemon.PersonalId)
            .Select(pokemon => new PokemonTarget(PokemonRecord(family, pokemon.PersonalId), pokemon))
            .ToArray();
        var providerId = ProviderId(family, input.Kind);
        if (input.Targets.Count == 0)
        {
            return Selection(
                input,
                providerId,
                ["level"],
                eligible.Select(target => TargetOption(target.Record, target.Pokemon.Name)));
        }

        var selected = SelectTargets(input.Targets, eligible, target => target.Record);
        var normalized = NormalizeInput(
            input,
            selected.Select(target => target.Record),
            ["level"],
            allowEvolutionPinChildren: true);
        var pins = CreatePinMap(normalized, ["level"], requireInputFields: false);
        var mutations = new List<GuidedDesignMutationDto>();
        var edits = new List<GuidedDesignStagingEdit>();
        foreach (var target in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var evolution in target.Pokemon.Evolutions.OrderBy(row => row.Slot))
            {
                if (!methodOptions.TryGetValue(evolution.Method, out var options)
                    || options.Length != 1
                    || !options[0].UsesLevel)
                {
                    continue;
                }

                var mutationRecord = target.Record with
                {
                    SubrecordId = EvolutionSlotSubrecord(evolution.Slot),
                };
                var pinned = TryPinned(pins, mutationRecord, "level", out var pinnedValue);
                var after = pinned
                    ? ToInt(pinnedValue!, "Evolution level pin")
                    : Math.Clamp(evolution.Level, minimum, maximum);
                RequireRange(after, 0, 100, "Evolution level");
                if (after == evolution.Level)
                {
                    continue;
                }

                mutations.Add(Mutation(
                    providerId,
                    mutationRecord,
                    SafePresentation(target.Pokemon.Name, 256),
                    "level",
                    $"Evolution slot {checked(evolution.Slot + 1).ToString(CultureInfo.InvariantCulture)} level",
                    evolution.Level,
                    after,
                    pinned,
                    "Clamp an existing verified level-using evolution row."));
                edits.Add(new GuidedDesignEvolutionStagingEdit(
                    target.Record,
                    evolution.Slot,
                    evolution.Method,
                    evolution.Argument,
                    evolution.Species,
                    evolution.Form,
                    after));
            }
        }

        return Complete(normalized, null, providerId, mutations, [], edits);
    }

    private static GuidedDesignProviderBuild BuildStatShuffle(
        SemanticGameFamilyDto family,
        GuidedDesignInputDto input,
        PokemonWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        RequireEditable(workflow.Summary, workflow.Diagnostics, PokemonDomain);
        RequireOnly(input, seed: true);
        var seed = input.Seed;
        if (seed is not { Length: 32 }
            || seed.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw Invalid("Pokemon stat shuffling requires a canonical lowercase 128-bit hexadecimal seed.");
        }

        var requestedFields = input.FieldKeys.Count == 0
            ? StatFields
            : StatFields.Where(field => input.FieldKeys.Contains(field, StringComparer.Ordinal)).ToArray();
        if (requestedFields.Length < 2
            || requestedFields.Length != input.FieldKeys.Distinct(StringComparer.Ordinal).Count()
                && input.FieldKeys.Count > 0)
        {
            throw Invalid("Pokemon stat shuffling requires at least two distinct supported stat fields.");
        }

        var fields = requestedFields.ToDictionary(
            key => key,
            key => RequireField(workflow.EditableFields, key),
            StringComparer.Ordinal);
        var eligible = workflow.Pokemon
            .Where(IsEligiblePokemonPersonal)
            .OrderBy(pokemon => pokemon.PersonalId)
            .Select(pokemon => new PokemonTarget(PokemonRecord(family, pokemon.PersonalId), pokemon))
            .ToArray();
        var providerId = ProviderId(family, input.Kind);
        if (input.Targets.Count == 0)
        {
            return Selection(
                input,
                providerId,
                requestedFields,
                eligible.Select(target => TargetOption(target.Record, target.Pokemon.Name)));
        }

        var selected = SelectTargets(input.Targets, eligible, target => target.Record);
        var normalized = NormalizeInput(input, selected.Select(target => target.Record), requestedFields);
        var pins = CreatePinMap(normalized, requestedFields);
        var mutations = new List<GuidedDesignMutationDto>();
        var edits = new List<GuidedDesignStagingEdit>();
        foreach (var target in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = PokemonStatValues(target.Pokemon.BaseStats);
            var remaining = requestedFields.Select(field => before[field]).ToList();
            var after = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fieldKey in requestedFields)
            {
                if (!TryPinned(pins, target.Record, fieldKey, out var pinnedValue))
                {
                    continue;
                }

                var value = ToInt(pinnedValue!, "Pokemon stat pin");
                var index = remaining.IndexOf(value);
                if (index < 0)
                {
                    throw Invalid(
                        "A Pokemon stat pin must consume a value from that record's selected original stat multiset.");
                }

                remaining.RemoveAt(index);
                after[fieldKey] = value;
            }

            ShuffleDeterministically(remaining, seed, RecordKey(target.Record));
            var remainingIndex = 0;
            foreach (var fieldKey in requestedFields)
            {
                if (!after.ContainsKey(fieldKey))
                {
                    after[fieldKey] = remaining[remainingIndex++];
                }

                ValidateFieldValue(fields[fieldKey], after[fieldKey]);
                AddScalarChange(
                    mutations,
                    edits,
                    providerId,
                    target.Record,
                    SafePresentation(target.Pokemon.Name, 256),
                    fieldKey,
                    FieldLabel(fieldKey),
                    before[fieldKey],
                    after[fieldKey],
                    TryPinned(pins, target.Record, fieldKey, out _),
                    "Shuffle selected base stats while preserving their exact multiset.");
            }
        }

        return Complete(normalized, seed, providerId, mutations, [], edits);
    }

    private static GuidedDesignProviderBuild Complete(
        GuidedDesignInputDto normalized,
        string? seed,
        string providerId,
        IReadOnlyList<GuidedDesignMutationDto> mutations,
        IReadOnlyList<GuidedDesignFindingDto> findings,
        IReadOnlyList<GuidedDesignStagingEdit> edits)
    {
        var orderedMutations = mutations
            .OrderBy(mutation => RecordKey(mutation.Record), StringComparer.Ordinal)
            .ThenBy(mutation => mutation.FieldKey, StringComparer.Ordinal)
            .ToArray();
        var orderedFindings = findings
            .OrderBy(finding => finding.FindingId, StringComparer.Ordinal)
            .ToArray();
        var normalizedPins = normalized.Pins.ToDictionary(
            pin => PinKey(pin.Record, pin.FieldKey),
            StringComparer.Ordinal);
        foreach (var mutation in orderedMutations.Where(mutation => mutation.Pinned))
        {
            if (mutation.PinRecord is null
                || mutation.PinFieldKey is null
                || !normalizedPins.TryGetValue(
                    PinKey(mutation.PinRecord, mutation.PinFieldKey),
                    out var pin)
                || !string.Equals(
                    pin.CanonicalValue,
                    mutation.After.CanonicalValue,
                    StringComparison.Ordinal))
            {
                throw Invalid(
                    "A pinned Guided Design mutation must reference its exact normalized constraint.");
            }
        }

        var affected = orderedMutations
            .Select(mutation => mutation.Record)
            .Distinct()
            .OrderBy(RecordKey, StringComparer.Ordinal)
            .ToArray();
        if (orderedMutations.Length > GuidedDesignContract.MaximumMutations
            || edits.Count > GuidedDesignContract.MaximumMutations
            || affected.Length > GuidedDesignContract.MaximumAffectedRecords
            || orderedFindings.Length > GuidedDesignContract.MaximumFindings)
        {
            throw Limit(
                "The complete Guided Design proposal exceeds its bounded mutation, finding, or affected-record limit. Select fewer exact targets.");
        }

        var diagnostics = orderedMutations.Length == 0
            ?
            [
                new ApiDiagnostic(
                    ApiDiagnosticSeverity.Info,
                    "The selected Guided Design constraints produce no effective change.",
                    Domain: DomainFor(normalized.Kind))
                {
                    Code = NoEffectiveChangeDiagnosticCode,
                },
            ]
            : Array.Empty<ApiDiagnostic>();
        return new GuidedDesignProviderBuild(
            normalized,
            seed,
            providerId,
            SelectionRequired: false,
            EligibleTargets: Array.Empty<GuidedDesignTargetOptionDto>(),
            orderedMutations,
            orderedFindings,
            affected,
            edits,
            diagnostics);
    }

    private static GuidedDesignProviderBuild Selection(
        GuidedDesignInputDto input,
        string providerId,
        IReadOnlyList<string> defaultFields,
        IEnumerable<GuidedDesignTargetOptionDto> targetOptions)
    {
        if (input.Pins.Count > 0)
        {
            throw Invalid("Guided Design pins require exact selected targets.");
        }

        var options = targetOptions
            .GroupBy(option => RecordKey(option.Record), StringComparer.Ordinal)
            .Select(group => group.Count() == 1
                ? group.Single()
                : throw Invalid("The owning workflow returned duplicate target options."))
            .OrderBy(option => RecordKey(option.Record), StringComparer.Ordinal)
            .ToArray();
        if (options.Length > GuidedDesignContract.MaximumEligibleTargets)
        {
            throw Limit(
                $"Guided Design target discovery exceeds its bounded {GuidedDesignContract.MaximumEligibleTargets:N0}-record limit.");
        }

        var normalized = NormalizeInput(input, [], defaultFields);
        return new GuidedDesignProviderBuild(
            normalized,
            input.Seed,
            providerId,
            SelectionRequired: true,
            options,
            Mutations: Array.Empty<GuidedDesignMutationDto>(),
            Findings: Array.Empty<GuidedDesignFindingDto>(),
            AffectedRecords: Array.Empty<SemanticRecordRefDto>(),
            StagingEdits: Array.Empty<GuidedDesignStagingEdit>(),
            Diagnostics:
            [
                new ApiDiagnostic(
                    ApiDiagnosticSeverity.Info,
                    "Select one or more exact eligible targets before generating an importable Guided Design proposal.",
                    Domain: DomainFor(input.Kind))
                {
                    Code = TargetSelectionRequiredDiagnosticCode,
                },
            ]);
    }

    private static GuidedDesignTargetOptionDto TargetOption(
        SemanticRecordRefDto record,
        string label) => new(record, SafePresentation(label, 256));

    private static GuidedDesignInputDto NormalizeInput(
        GuidedDesignInputDto input,
        IEnumerable<SemanticRecordRefDto> targets,
        IReadOnlyList<string> defaultFields,
        bool allowEvolutionPinChildren = false)
    {
        var normalizedTargets = targets
            .Distinct()
            .OrderBy(RecordKey, StringComparer.Ordinal)
            .ToArray();
        if (normalizedTargets.Length > GuidedDesignContract.MaximumTargets)
        {
            throw Limit(
                "The complete Guided Design target set exceeds the 128-record bound. Select fewer exact targets.");
        }

        if (input.FieldKeys.Count > 0
            && (input.FieldKeys.Count != defaultFields.Count
                || !input.FieldKeys.ToHashSet(StringComparer.Ordinal).SetEquals(defaultFields)))
        {
            throw Invalid("The selected proposal kind received an unsupported field set.");
        }

        var fields = defaultFields.ToArray();

        var targetKeys = normalizedTargets.Select(RecordKey).ToHashSet(StringComparer.Ordinal);
        foreach (var pin in input.Pins)
        {
            if (pin is null || pin.Record is null)
            {
                throw Invalid("A Guided Design pin is malformed.");
            }

            var pinTargetKey = allowEvolutionPinChildren
                ? RecordKey(pin.Record with { SubrecordId = null })
                : RecordKey(pin.Record);
            if (!targetKeys.Contains(pinTargetKey)
                || allowEvolutionPinChildren
                    && !IsEvolutionSlotRecord(pin.Record))
            {
                throw Invalid("Every Guided Design pin must belong to an exact selected target.");
            }
        }

        return input with
        {
            Targets = normalizedTargets,
            Pins = input.Pins
                .OrderBy(pin => RecordKey(pin.Record), StringComparer.Ordinal)
                .ThenBy(pin => pin.FieldKey, StringComparer.Ordinal)
                .ToArray(),
            FieldKeys = fields,
        };
    }

    private static IReadOnlyDictionary<string, GuidedDesignPinDto> CreatePinMap(
        GuidedDesignInputDto input,
        IReadOnlyCollection<string> allowedFields,
        bool requireInputFields = true)
    {
        var allowed = allowedFields.ToHashSet(StringComparer.Ordinal);
        var pins = new Dictionary<string, GuidedDesignPinDto>(StringComparer.Ordinal);
        foreach (var pin in input.Pins)
        {
            if (pin is null || pin.Record is null)
            {
                throw Invalid("A Guided Design pin is malformed.");
            }

            if (string.IsNullOrWhiteSpace(pin.FieldKey)
                || !allowed.Contains(pin.FieldKey)
                || requireInputFields && !input.FieldKeys.Contains(pin.FieldKey, StringComparer.Ordinal))
            {
                throw Invalid("A Guided Design pin targets an unsupported field.");
            }

            _ = ParseCanonicalInteger(pin.CanonicalValue, "Guided Design pin");
            if (!pins.TryAdd(PinKey(pin.Record, pin.FieldKey), pin))
            {
                throw Invalid("Guided Design pins must target distinct semantic fields.");
            }
        }

        return pins;
    }

    private static bool TryPinned(
        IReadOnlyDictionary<string, GuidedDesignPinDto> pins,
        SemanticRecordRefDto record,
        string field,
        out string? canonicalValue)
    {
        if (pins.TryGetValue(PinKey(record, field), out var pin))
        {
            canonicalValue = pin.CanonicalValue;
            return true;
        }

        canonicalValue = null;
        return false;
    }

    private static GuidedDesignPinDto? ResolveAliasPin<T>(
        IReadOnlyDictionary<string, GuidedDesignPinDto> pins,
        IReadOnlyList<T> selected,
        IReadOnlyList<T> aliases,
        string field)
        where T : IGuidedTarget
    {
        var selectedKeys = selected.Select(target => RecordKey(target.Record)).ToHashSet(StringComparer.Ordinal);
        var matchingPins = aliases
            .Where(alias => selectedKeys.Contains(RecordKey(alias.Record)))
            .Select(alias => pins.GetValueOrDefault(PinKey(alias.Record, field)))
            .Where(pin => pin is not null)
            .Cast<GuidedDesignPinDto>()
            .OrderBy(pin => RecordKey(pin.Record), StringComparer.Ordinal)
            .ToArray();
        var values = matchingPins
            .Select(pin => pin.CanonicalValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length > 1)
        {
            throw Invalid("Pins for aliases of one physical record must agree exactly.");
        }

        return matchingPins.FirstOrDefault();
    }

    private static T[] SelectTargets<T>(
        IReadOnlyList<SemanticRecordRefDto> requested,
        IReadOnlyList<T> eligible,
        Func<T, SemanticRecordRefDto> record)
    {
        var lookup = eligible
            .GroupBy(target => RecordKey(record(target)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (lookup.Values.Any(values => values.Length != 1))
        {
            throw Invalid("The owning workflow returned duplicate semantic target identities.");
        }

        var selected = new List<T>(requested.Count);
        foreach (var target in requested)
        {
            if (!lookup.TryGetValue(RecordKey(target), out var matches))
            {
                throw Unsupported("A selected Guided Design target is unavailable in the exact layered workflow.");
            }

            selected.Add(matches[0]);
        }

        return selected
            .OrderBy(target => RecordKey(record(target)), StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateCommonInput(GuidedDesignInputDto input)
    {
        if (!Enum.IsDefined(input.Kind)
            || input.Targets is null
            || input.Pins is null
            || input.FieldKeys is null)
        {
            throw Invalid("The Guided Design input is malformed.");
        }

        if (input.Targets.Count > GuidedDesignContract.MaximumTargets
            || input.Pins.Count > GuidedDesignContract.MaximumPins
            || input.FieldKeys.Count > GuidedDesignContract.MaximumFieldKeys)
        {
            throw Limit("The Guided Design input exceeds a bounded collection limit.");
        }

        if (input.Targets.Any(target => target is null)
            || input.Pins.Any(pin => pin is null || pin.Record is null)
            || input.FieldKeys.Any(field => field is null))
        {
            throw Invalid("The Guided Design input contains a malformed collection element.");
        }

        if (input.Targets.Select(RecordKey).Distinct(StringComparer.Ordinal).Count()
                != input.Targets.Count
            || input.FieldKeys.Distinct(StringComparer.Ordinal).Count() != input.FieldKeys.Count)
        {
            throw Invalid("Guided Design targets and fields must be distinct.");
        }

        foreach (var field in input.FieldKeys)
        {
            ValidateFieldKey(field, allowEvolutionSlot: false);
        }

        foreach (var pin in input.Pins)
        {
            if (pin is null || pin.Record is null)
            {
                throw Invalid("A Guided Design pin is malformed.");
            }
            ValidateFieldKey(pin.FieldKey, allowEvolutionSlot: true);
            if (pin.CanonicalValue is not { Length: > 0 and <= GuidedDesignContract.MaximumCanonicalIntegerLength })
            {
                throw Invalid("A Guided Design pin has an invalid canonical integer.");
            }
        }
    }

    private static void RequireOnly(
        GuidedDesignInputDto input,
        bool delta = false,
        bool multiplier = false,
        bool minimum = false,
        bool maximum = false,
        bool rounding = false,
        bool archetype = false,
        bool seed = false)
    {
        if ((!delta && input.Delta is not null)
            || (!multiplier && input.MultiplierBasisPoints is not null)
            || (!minimum && input.MinimumValue is not null)
            || (!maximum && input.MaximumValue is not null)
            || (!rounding && input.Rounding is not null)
            || (!archetype && input.Archetype is not null)
            || (!seed && input.Seed is not null))
        {
            throw Invalid("The Guided Design input contains constraints that do not apply to its proposal kind.");
        }

        if (rounding && input.Rounding is not null && !Enum.IsDefined(input.Rounding.Value))
        {
            throw Invalid("The Guided Design rounding rule is invalid.");
        }
    }

    private static void EnsureKindSupported(
        SemanticGameFamilyDto family,
        GuidedDesignProposalKindDto kind)
    {
        var supported = Capabilities(family)
            .Where(capability => capability.State != SemanticCoverageStateDto.Unavailable)
            .SelectMany(capability => capability.ProposalKinds)
            .Contains(kind);
        if (!supported)
        {
            throw Unsupported("The selected family does not expose this Guided Design proposal kind.");
        }
    }

    private static void RequireEditable(
        WorkflowSummaryDto summary,
        IReadOnlyList<ApiDiagnostic> diagnostics,
        string domain)
    {
        if (summary.Availability != WorkflowAvailabilityDto.Available
            || summary.Diagnostics.Concat(diagnostics)
                .Any(diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error))
        {
            throw Unsupported(
                $"The owning {domain} workflow is not available for exact Guided Design staging.");
        }
    }

    private static TrainerEditableFieldDto RequireField(
        IReadOnlyList<TrainerEditableFieldDto> fields,
        string key)
    {
        var matches = fields.Where(field => string.Equals(field.Field, key, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Unsupported("The owning trainer workflow does not expose a required editable field.");
    }

    private static EncounterEditableFieldDto RequireField(
        IReadOnlyList<EncounterEditableFieldDto> fields,
        string key)
    {
        var matches = fields.Where(field => string.Equals(field.Field, key, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Unsupported("The owning encounter workflow does not expose a required editable field.");
    }

    private static ItemEditableFieldDto RequireField(
        IReadOnlyList<ItemEditableFieldDto> fields,
        string key)
    {
        var matches = fields.Where(field =>
            string.Equals(field.Field, key, StringComparison.Ordinal) && !field.IsReadOnly).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Unsupported("The owning item workflow does not expose a required editable field.");
    }

    private static PokemonEditableFieldDto RequireField(
        IReadOnlyList<PokemonEditableFieldDto> fields,
        string key)
    {
        var matches = fields.Where(field => string.Equals(field.Field, key, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Unsupported("The owning Pokemon workflow does not expose a required editable field.");
    }

    private static void ValidateFieldValue(TrainerEditableFieldDto field, int value) =>
        ValidateFieldValue(field.MinimumValue, field.MaximumValue, value);

    private static void ValidateFieldValue(EncounterEditableFieldDto field, int value) =>
        ValidateFieldValue(field.MinimumValue, field.MaximumValue, value);

    private static void ValidateFieldValue(ItemEditableFieldDto field, int value) =>
        ValidateFieldValue(field.MinimumValue, field.MaximumValue, value);

    private static void ValidateFieldValue(PokemonEditableFieldDto field, int value) =>
        ValidateFieldValue(field.MinimumValue, field.MaximumValue, value);

    private static void ValidateFieldValue(int? minimum, int? maximum, int value)
    {
        if (minimum is null || maximum is null || value < minimum || value > maximum)
        {
            throw Invalid("A proposed value falls outside its owning provider's exact editable bounds.");
        }
    }

    private static IEnumerable<EncounterTarget> EncounterTargets(
        SemanticGameFamilyDto family,
        EncountersWorkflowDto workflow)
    {
        foreach (var table in workflow.Tables.OrderBy(table => table.TableId, StringComparer.Ordinal))
        {
            foreach (var slot in table.Slots.Where(slot => slot.SpeciesId > 0).OrderBy(slot => slot.Slot))
            {
                yield return new EncounterTarget(
                    EncounterRecord(family, table.TableId, slot.Slot),
                    table,
                    slot);
            }
        }
    }

    private static ItemTarget[] ExpandSharedItems(
        ItemTarget origin,
        IReadOnlyDictionary<int, ItemTarget> byId)
    {
        var discovered = new HashSet<int> { origin.Item.ItemId };
        var pending = new Queue<int>();
        pending.Enqueue(origin.Item.ItemId);
        while (pending.TryDequeue(out var id))
        {
            if (!byId.TryGetValue(id, out var target))
            {
                continue;
            }

            foreach (var sharedId in target.Item.SharedItemIds)
            {
                if (byId.ContainsKey(sharedId) && discovered.Add(sharedId))
                {
                    pending.Enqueue(sharedId);
                }
            }
        }

        return discovered.Select(id => byId[id]).ToArray();
    }

    private static void AddScalarChange(
        ICollection<GuidedDesignMutationDto> mutations,
        ICollection<GuidedDesignStagingEdit> edits,
        string providerId,
        SemanticRecordRefDto record,
        string recordLabel,
        string fieldKey,
        string fieldLabel,
        int before,
        int after,
        bool pinned,
        string summary)
    {
        if (before == after)
        {
            return;
        }

        mutations.Add(Mutation(
            providerId,
            record,
            recordLabel,
            fieldKey,
            fieldLabel,
            before,
            after,
            pinned,
            summary));
        edits.Add(new GuidedDesignScalarStagingEdit(record, fieldKey, after));
    }

    private static void AddMutation(
        ICollection<GuidedDesignMutationDto> mutations,
        string providerId,
        SemanticRecordRefDto record,
        string recordLabel,
        string fieldKey,
        string fieldLabel,
        int before,
        int after,
        bool pinned,
        string summary,
        SemanticRecordRefDto? pinRecord = null,
        string? pinFieldKey = null)
    {
        if (before != after)
        {
            mutations.Add(Mutation(
                providerId,
                record,
                recordLabel,
                fieldKey,
                fieldLabel,
                before,
                after,
                pinned,
                summary,
                pinRecord,
                pinFieldKey));
        }
    }

    private static GuidedDesignMutationDto Mutation(
        string providerId,
        SemanticRecordRefDto record,
        string recordLabel,
        string fieldKey,
        string fieldLabel,
        int before,
        int after,
        bool pinned,
        string summary,
        SemanticRecordRefDto? pinRecord = null,
        string? pinFieldKey = null)
    {
        var identity = Hash(
            "guided-design-mutation-v1",
            providerId,
            RecordKey(record),
            fieldKey);
        return new GuidedDesignMutationDto(
            $"{providerId}.mutation.{identity[..24]}",
            record,
            SafePresentation(recordLabel, 256),
            fieldKey,
            SafePresentation(fieldLabel, 128),
            Scalar(before),
            Scalar(after),
            pinned,
            pinRecord ?? record,
            pinFieldKey ?? fieldKey,
            providerId,
            SafePresentation(summary, 512));
    }

    private static GuidedDesignFindingDto Finding(
        string providerId,
        string ruleKey,
        GuidedDesignFindingSeverityDto severity,
        string title,
        string summary,
        SemanticRecordRefDto record,
        IReadOnlyList<SemanticRecordRefDto> related)
    {
        var identity = Hash(
            "guided-design-finding-v1",
            providerId,
            ruleKey,
            RecordKey(record));
        return new GuidedDesignFindingDto(
            $"{providerId}.finding.{identity[..24]}",
            $"{providerId}.rule.{ruleKey}",
            severity,
            SemanticConfidenceDto.Verified,
            SafePresentation(title, 256),
            SafePresentation(summary, 1_024),
            record,
            related.Distinct().OrderBy(RecordKey, StringComparer.Ordinal).ToArray());
    }

    private static SemanticScalarValueDto Scalar(int value)
    {
        var canonical = value.ToString(CultureInfo.InvariantCulture);
        return new SemanticScalarValueDto(
            SemanticValueKindDto.SignedInteger,
            canonical,
            canonical);
    }

    private static GuidedDesignCapabilityDto Capability(
        string familyKey,
        GuidedDesignFeatureDto feature,
        SemanticCoverageStateDto state,
        SemanticConfidenceDto confidence,
        string? reason,
        IReadOnlyList<GuidedDesignProposalKindDto> kinds)
    {
        return new GuidedDesignCapabilityDto(
            feature,
            $"{familyKey}.guided-design.{FeatureKey(feature)}",
            state,
            confidence,
            reason,
            kinds,
            state == SemanticCoverageStateDto.Unavailable
                ? Array.Empty<SemanticSourceLayerKindDto>()
                : [SemanticSourceLayerKindDto.Layered]);
    }

    private static string FeatureKey(GuidedDesignFeatureDto feature)
    {
        return feature switch
        {
            GuidedDesignFeatureDto.DifficultyDesigner => "difficulty-designer",
            GuidedDesignFeatureDto.EncounterPopulationDesigner => "encounter-population-designer",
            GuidedDesignFeatureDto.EconomyRebalance => "economy-rebalance",
            GuidedDesignFeatureDto.EvolutionAccessibility => "evolution-accessibility",
            GuidedDesignFeatureDto.TrainerArchetypes => "trainer-archetypes",
            GuidedDesignFeatureDto.ConstraintRandomization => "constraint-randomization",
            GuidedDesignFeatureDto.Plando => "plando",
            GuidedDesignFeatureDto.SeedInspector => "seed-inspector",
            GuidedDesignFeatureDto.SpoilerRaceExport => "spoiler-race-export",
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
        };
    }

    private static string ProviderId(
        SemanticGameFamilyDto family,
        GuidedDesignProposalKindDto kind)
    {
        return $"{FamilyKey(family)}.guided-design.{ProposalKey(kind)}";
    }

    private static string FamilyKey(SemanticGameFamilyDto family)
    {
        return family switch
        {
            SemanticGameFamilyDto.SwordShield => "swsh",
            SemanticGameFamilyDto.ScarletViolet => "sv",
            SemanticGameFamilyDto.LegendsZA => "za",
            _ => throw Unsupported("The selected Guided Design family provider is unavailable."),
        };
    }

    private static string ProposalKey(GuidedDesignProposalKindDto kind)
    {
        return kind switch
        {
            GuidedDesignProposalKindDto.TrainerLevelAdjustment => "trainer-level-adjustment",
            GuidedDesignProposalKindDto.EncounterLevelAdjustment => "encounter-level-adjustment",
            GuidedDesignProposalKindDto.EncounterWeightScale => "encounter-weight-scale",
            GuidedDesignProposalKindDto.EconomyPrimaryPriceScale => "economy-primary-price-scale",
            GuidedDesignProposalKindDto.EvolutionLevelClamp => "evolution-level-clamp",
            GuidedDesignProposalKindDto.TrainerEvArchetype => "trainer-ev-archetype",
            GuidedDesignProposalKindDto.PokemonBaseStatShuffle => "pokemon-base-stat-shuffle",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static SemanticRecordRefDto TrainerRecord(
        SemanticGameFamilyDto family,
        int trainerId,
        int slot) => new(
            family,
            TrainersDomain,
            new SemanticRecordKindDto("trainer", RecordSchemaVersion),
            trainerId.ToString(CultureInfo.InvariantCulture),
            $"party-slot:{slot.ToString(CultureInfo.InvariantCulture)}");

    private static SemanticRecordRefDto EncounterRecord(
        SemanticGameFamilyDto family,
        string tableId,
        int slot) => new(
            family,
            EncountersDomain,
            new SemanticRecordKindDto("encounter-table", RecordSchemaVersion),
            StableIdentity(tableId),
            $"slot:{slot.ToString(CultureInfo.InvariantCulture)}");

    private static SemanticRecordRefDto ItemRecord(
        SemanticGameFamilyDto family,
        int itemId) => new(
            family,
            ItemsDomain,
            new SemanticRecordKindDto("item", RecordSchemaVersion),
            itemId.ToString(CultureInfo.InvariantCulture),
            null);

    private static SemanticRecordRefDto PokemonRecord(
        SemanticGameFamilyDto family,
        int personalId) => new(
            family,
            PokemonDomain,
            new SemanticRecordKindDto("pokemon-personal", RecordSchemaVersion),
            personalId.ToString(CultureInfo.InvariantCulture),
            null);

    internal static string RecordKey(SemanticRecordRefDto record)
    {
        if (record is null || record.RecordKind is null)
        {
            throw Invalid("A Guided Design semantic record reference is malformed.");
        }
        return string.Join(
            '\n',
            ((int)record.GameFamily).ToString("D2", CultureInfo.InvariantCulture),
            StableIdentity(record.Domain),
            StableIdentity(record.RecordKind.Key),
            record.RecordKind.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            StableIdentity(record.RecordId),
            record.SubrecordId is null ? string.Empty : StableIdentity(record.SubrecordId));
    }

    private static string PinKey(SemanticRecordRefDto record, string field) =>
        $"{RecordKey(record)}\n{field}";

    private static string StableIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 1_024
            || value.Any(IsUnsafeUnicode)
            || ContainsLocalPathSignature(value))
        {
            throw Invalid("A Guided Design semantic identity is invalid or unsafe.");
        }

        return value;
    }

    private static void ValidateFieldKey(string value, bool allowEvolutionSlot)
    {
        _ = allowEvolutionSlot;
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 128
            || value.Any(IsUnsafeUnicode))
        {
            throw Invalid("A Guided Design field key is invalid.");
        }

        var ordinary = char.IsAsciiLetterLower(value[0])
            && value.Skip(1).All(char.IsAsciiLetterOrDigit);
        if (!ordinary)
        {
            throw Invalid("A Guided Design field key is unsupported.");
        }
    }

    private static string EvolutionSlotSubrecord(int slot) =>
        $"evolution-slot:{slot.ToString(CultureInfo.InvariantCulture)}";

    private static bool IsEvolutionSlotRecord(SemanticRecordRefDto record)
    {
        const string prefix = "evolution-slot:";
        return record.SubrecordId is { } value
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                value.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var slot)
            && slot >= 0
            && value == EvolutionSlotSubrecord(slot);
    }

    private static int[] TrainerEvValues(TrainerPokemonStatsDto values) =>
        [values.HP, values.Attack, values.Defense, values.SpecialAttack, values.SpecialDefense, values.Speed];

    private static Dictionary<string, int> PokemonStatValues(PokemonBaseStatsDto values) =>
        new(StringComparer.Ordinal)
        {
            ["hp"] = values.HP,
            ["attack"] = values.Attack,
            ["defense"] = values.Defense,
            ["specialAttack"] = values.SpecialAttack,
            ["specialDefense"] = values.SpecialDefense,
            ["speed"] = values.Speed,
        };

    private static string EncounterLabel(EncounterTarget target)
    {
        var label = target.Table.TableLabel ?? target.Table.Location;
        return $"{SafePresentation(label, 220)} slot {EncounterSlotDisplay(target.Record.GameFamily, target.Slot.Slot).ToString(CultureInfo.InvariantCulture)}";
    }

    private static int TrainerSlotDisplay(SemanticGameFamilyDto family, int slot) =>
        family == SemanticGameFamilyDto.SwordShield ? slot : checked(slot + 1);

    private static int EncounterSlotDisplay(SemanticGameFamilyDto family, int slot) =>
        family == SemanticGameFamilyDto.SwordShield ? slot : checked(slot + 1);

    private static bool IsEligiblePokemonPersonal(PokemonRecordDto pokemon)
    {
        return pokemon.PersonalId > 0
            && pokemon.SpeciesId > 0
            && pokemon.DexPresence.IsPresentInGame
            && pokemon.Personal.IsPresentInGame
            && !string.Equals(pokemon.Name, "Egg", StringComparison.OrdinalIgnoreCase);
    }

    private static string FieldLabel(string field)
    {
        return field switch
        {
            "hp" => "HP",
            "attack" => "Attack",
            "defense" => "Defense",
            "specialAttack" => "Special Attack",
            "specialDefense" => "Special Defense",
            "speed" => "Speed",
            "evHp" => "HP EV",
            "evAttack" => "Attack EV",
            "evDefense" => "Defense EV",
            "evSpecialAttack" => "Special Attack EV",
            "evSpecialDefense" => "Special Defense EV",
            "evSpeed" => "Speed EV",
            "probability" => "Relative encounter weight",
            "weight" => "Weight",
            _ => field,
        };
    }

    private static int Scale(
        int value,
        int multiplierBasisPoints,
        GuidedDesignRoundingDto rounding)
    {
        if (value < 0)
        {
            throw Invalid("A provider-owned scalable value cannot be negative.");
        }

        var product = checked((long)value * multiplierBasisPoints);
        var scaled = rounding switch
        {
            GuidedDesignRoundingDto.Floor => product / 10_000L,
            GuidedDesignRoundingDto.Nearest => checked(product + 5_000L) / 10_000L,
            GuidedDesignRoundingDto.Ceiling => checked(product + 9_999L) / 10_000L,
            _ => throw Invalid("The Guided Design rounding rule is invalid."),
        };
        return checked((int)scaled);
    }

    private static void ShuffleDeterministically(
        IList<int> values,
        string seed,
        string recordKey)
    {
        var random = new DeterministicByteStream(seed, recordKey);
        for (var index = values.Count - 1; index > 0; index--)
        {
            var selected = random.Next(index + 1);
            (values[index], values[selected]) = (values[selected], values[index]);
        }
    }

    private static long ParseCanonicalInteger(string value, string label)
    {
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            || parsed.ToString(CultureInfo.InvariantCulture) != value)
        {
            throw Invalid($"{label} must be a canonical signed integer.");
        }

        return parsed;
    }

    private static int ToInt(string value, string label) =>
        checked((int)ParseCanonicalInteger(value, label));

    private static void RequireRange(int value, int minimum, int maximum, string label)
    {
        if (value < minimum || value > maximum)
        {
            throw Invalid($"{label} is outside its supported range.");
        }
    }

    private static string Hash(string prefix, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, prefix);
        foreach (var value in values)
        {
            AppendHash(hash, value);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static void AppendHash(IncrementalHash hash, string? value)
    {
        var bytes = value is null ? null : Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes?.Length ?? -1);
        hash.AppendData(length);
        if (bytes is not null)
        {
            hash.AppendData(bytes);
        }
    }

    private static string SafePresentation(string value, int maximumLength)
    {
        var safe = new string(value.Select(character => IsUnsafeUnicode(character) ? ' ' : character).ToArray())
            .Trim();
        if (safe.Length == 0 || ContainsLocalPathSignature(safe))
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
                break;
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

    internal static bool IsSafeGeneratedDisplayText(string? value) =>
        value is null || !ContainsLocalPathSignature(value);

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

    private static SemanticExploreValidationException Invalid(string message) =>
        new(message, SemanticExploreFailureKind.InvalidData);

    private static SemanticExploreValidationException Unsupported(string message) =>
        new(message, SemanticExploreFailureKind.Unsupported);

    private static SemanticExploreValidationException Limit(string message) =>
        new(message, SemanticExploreFailureKind.LimitExceeded);

    private interface IGuidedTarget
    {
        SemanticRecordRefDto Record { get; }
    }

    private sealed record TrainerTarget(
        SemanticRecordRefDto Record,
        TrainerRecordDto Trainer,
        TrainerPokemonRecordDto Member) : IGuidedTarget;

    private sealed record EncounterTarget(
        SemanticRecordRefDto Record,
        EncounterTableRecordDto Table,
        EncounterSlotRecordDto Slot) : IGuidedTarget;

    private sealed record ItemTarget(
        SemanticRecordRefDto Record,
        ItemRecordDto Item) : IGuidedTarget;

    private sealed record PokemonTarget(
        SemanticRecordRefDto Record,
        PokemonRecordDto Pokemon) : IGuidedTarget;

    private sealed class DeterministicByteStream
    {
        private readonly byte[] key;
        private uint counter;

        public DeterministicByteStream(string seed, string scope)
        {
            key = SHA256.HashData(Encoding.UTF8.GetBytes($"guided-design-rng-v1\0{seed}\0{scope}"));
        }

        public int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            }

            var maximum = (uint)exclusiveMaximum;
            var accepted = uint.MaxValue - uint.MaxValue % maximum;
            Span<byte> input = stackalloc byte[key.Length + sizeof(uint)];
            key.CopyTo(input);
            while (true)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(input[key.Length..], counter++);
                var bytes = SHA256.HashData(input);
                var candidate = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                if (candidate < accepted)
                {
                    return (int)(candidate % maximum);
                }
            }
        }
    }
}
