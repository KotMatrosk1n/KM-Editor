// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Semantics;
using KM.Core.Semantics;

namespace KM.Tools.Application;

internal interface ISemanticExploreFamilyProvider
{
    SemanticGameFamilyDto GameFamily { get; }

    SemanticLayerData Build(
        SemanticWorkflowCorpus corpus,
        SemanticMaterializationBudget materializationBudget);
}

internal sealed record SemanticWorkflowLoad<T>(
    T? Value,
    string? ReasonCode,
    bool Partial = false)
    where T : class;

internal sealed record SemanticWorkflowCorpus(
    Func<SemanticWorkflowLoad<ItemsWorkflowDto>> LoadItems,
    Func<SemanticWorkflowLoad<PokemonWorkflowDto>> LoadPokemon,
    Func<SemanticWorkflowLoad<MovesWorkflowDto>> LoadMoves);

internal sealed record SemanticLayerData(
    SortedDictionary<string, SemanticIndexedEntity> Entities,
    IReadOnlyList<SemanticIndexedReference> References,
    IReadOnlyList<SemanticDomainStatus> DomainStatuses);

internal sealed record SemanticDomainStatus(
    string ProviderId,
    string Domain,
    bool Available,
    string? ReasonCode,
    bool Partial = false);

internal sealed record SemanticIndexedEntity(
    SemanticRecordRefDto Record,
    string Title,
    string? Summary,
    string DomainLabel,
    string OwnerId,
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    IReadOnlyDictionary<string, SemanticIndexedField> Fields);

internal sealed record SemanticIndexedField(
    string Key,
    string Label,
    string Group,
    SemanticScalarValueDto Value,
    string OwnerId);

internal sealed record SemanticIndexedReference(
    string SourceKey,
    string TargetKey,
    string RelationshipKey,
    string RelationshipLabel,
    string ProviderId);

internal abstract class SemanticExploreFamilyProviderBase : ISemanticExploreFamilyProvider
{
    private const int RecordSchemaVersion = 1;
    private const string ProviderLimitMessage =
        "The semantic index exceeds its bounded provider limits.";

    protected const string ItemsDomain = "workflow.items";
    protected const string PokemonDomain = "workflow.pokemon";
    protected const string MovesDomain = "workflow.moves";

    public abstract SemanticGameFamilyDto GameFamily { get; }

    protected abstract string FamilyKey { get; }

    public SemanticLayerData Build(
        SemanticWorkflowCorpus corpus,
        SemanticMaterializationBudget materializationBudget)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(materializationBudget);

        var builder = new SemanticLayerBuilder(materializationBudget);

        BuildItems(corpus.LoadItems(), builder);
        BuildPokemon(corpus.LoadPokemon(), builder);
        BuildMoves(corpus.LoadMoves(), builder);

        return builder.Complete();
    }

    protected abstract void BuildItems(
        SemanticWorkflowLoad<ItemsWorkflowDto> load,
        SemanticLayerBuilder builder);

    protected abstract void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        SemanticLayerBuilder builder);

    protected abstract void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        SemanticLayerBuilder builder);

    protected string ProviderId(string domainKey) => $"{FamilyKey}.{domainKey}.semantic";

    protected void AddUnavailable(
        SemanticLayerBuilder builder,
        string providerId,
        string domain,
        string? reasonCode)
    {
        builder.AddStatus(new SemanticDomainStatus(
            providerId,
            domain,
            Available: false,
            reasonCode ?? "provider-unavailable"));
    }

    protected void AddAvailable(
        SemanticLayerBuilder builder,
        string providerId,
        string domain,
        bool partial = false,
        string? reasonCode = null)
    {
        builder.AddStatus(new SemanticDomainStatus(
            providerId,
            domain,
            Available: true,
            partial ? reasonCode ?? "provider-partial" : null,
            partial));
    }

    protected SemanticRecordRefDto Record(string domain, string kind, int id)
    {
        if (id < 0)
        {
            throw new SemanticExploreValidationException(
                "A semantic provider returned an invalid numeric record identity.",
                SemanticExploreFailureKind.InvalidData);
        }

        return new SemanticRecordRefDto(
            GameFamily,
            domain,
            new SemanticRecordKindDto(kind, RecordSchemaVersion),
            id.ToString(CultureInfo.InvariantCulture),
            SubrecordId: null);
    }

    protected static string Key(SemanticRecordRefDto record)
    {
        return string.Join(
            ':',
            record.GameFamily,
            record.Domain,
            record.RecordKind.Key,
            record.RecordKind.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            record.RecordId,
            record.SubrecordId ?? string.Empty);
    }

    protected static string NormalizeSourceFile(string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile)
            || sourceFile != sourceFile.Trim()
            || Path.IsPathRooted(sourceFile)
            || sourceFile.Any(IsUnsafeUnicode))
        {
            throw new SemanticExploreValidationException(
                "A semantic provider returned unsafe source provenance.",
                SemanticExploreFailureKind.InvalidData);
        }

        try
        {
            return new RelativeOutputPath(sourceFile).Value;
        }
        catch (ArgumentException exception)
        {
            throw new SemanticExploreValidationException(
                "A semantic provider returned unsafe source provenance.",
                SemanticExploreFailureKind.InvalidData,
                exception);
        }
    }

    private static string SafePresentation(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        var safe = new string(value
            .Select(character => IsUnsafeUnicode(character) ? ' ' : character)
            .ToArray())
            .Trim();
        if (safe.Length == 0)
        {
            return "Unnamed";
        }

        if (!GuidedDesignProviders.IsSafeGeneratedDisplayText(safe))
        {
            return "Unnamed";
        }

        if (safe.Length <= maximumLength)
        {
            return safe;
        }

        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(safe[length - 1]))
        {
            length--;
        }

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

    protected static SemanticIndexedField Signed(
        string key,
        string label,
        string group,
        long value,
        string ownerId)
    {
        var canonical = value.ToString(CultureInfo.InvariantCulture);
        return new SemanticIndexedField(
            key,
            label,
            group,
            new SemanticScalarValueDto(SemanticValueKindDto.SignedInteger, canonical, canonical),
            ownerId);
    }

    protected static SemanticIndexedField Unsigned(
        string key,
        string label,
        string group,
        ulong value,
        string ownerId)
    {
        var canonical = value.ToString(CultureInfo.InvariantCulture);
        return new SemanticIndexedField(
            key,
            label,
            group,
            new SemanticScalarValueDto(SemanticValueKindDto.UnsignedInteger, canonical, canonical),
            ownerId);
    }

    protected static SemanticIndexedField Decimal(
        string key,
        string label,
        string group,
        double value,
        string ownerId)
    {
        var canonical = value.ToString("R", CultureInfo.InvariantCulture);
        return new SemanticIndexedField(
            key,
            label,
            group,
            new SemanticScalarValueDto(SemanticValueKindDto.Decimal, canonical, canonical),
            ownerId);
    }

    protected static SemanticIndexedField Boolean(
        string key,
        string label,
        string group,
        bool value,
        string ownerId)
    {
        var canonical = value ? "true" : "false";
        return new SemanticIndexedField(
            key,
            label,
            group,
            new SemanticScalarValueDto(SemanticValueKindDto.Boolean, canonical, canonical),
            ownerId);
    }

    protected static SemanticIndexedField Enumeration(
        string key,
        string label,
        string group,
        long value,
        string display,
        string ownerId)
    {
        return new SemanticIndexedField(
            key,
            label,
            group,
            new SemanticScalarValueDto(
                SemanticValueKindDto.Enum,
                value.ToString(CultureInfo.InvariantCulture),
                display),
            ownerId);
    }

    protected static SemanticIndexedField NullableSigned(
        string key,
        string label,
        string group,
        int? value,
        string ownerId)
    {
        return value is null
            ? new SemanticIndexedField(
                key,
                label,
                group,
                new SemanticScalarValueDto(SemanticValueKindDto.Null, null, "Unavailable"),
                ownerId)
            : Signed(key, label, group, value.Value, ownerId);
    }

    protected static IReadOnlyDictionary<string, SemanticIndexedField> ItemFields(
        ItemRecordDto item,
        ItemsWorkflowDto workflow,
        string ownerId)
    {
        if (workflow.EditableFields.Count > SemanticIndexSizingLimits.MaximumFieldCountPerEntity
            || item.FieldValues.Count > SemanticIndexSizingLimits.MaximumFieldCountPerEntity)
        {
            throw new SemanticExploreValidationException(
                ProviderLimitMessage,
                SemanticExploreFailureKind.LimitExceeded);
        }

        var definitions = workflow.EditableFields.ToDictionary(field => field.Field, StringComparer.Ordinal);
        var fields = new Dictionary<string, SemanticIndexedField>(StringComparer.Ordinal);
        foreach (var (key, value) in item.FieldValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(key, out var definition))
            {
                continue;
            }

            fields.Add(
                key,
                value is null
                    ? new SemanticIndexedField(
                        key,
                        definition.Label,
                        "Item data",
                        new SemanticScalarValueDto(SemanticValueKindDto.Null, null, "Unavailable"),
                        ownerId)
                    : Signed(key, definition.Label, "Item data", value.Value, ownerId));
        }

        return fields;
    }

    protected static IReadOnlyDictionary<(int SpeciesId, int Form), int> UniqueSpeciesForms(
        IReadOnlyList<PokemonRecordDto> pokemon)
    {
        ArgumentNullException.ThrowIfNull(pokemon);
        var speciesForms = new Dictionary<(int SpeciesId, int Form), int>();
        var duplicateKeys = new HashSet<(int SpeciesId, int Form)>();
        foreach (var record in pokemon)
        {
            var key = (record.SpeciesId, record.Form);
            if (duplicateKeys.Contains(key))
            {
                continue;
            }

            if (!speciesForms.TryAdd(key, record.PersonalId))
            {
                speciesForms.Remove(key);
                duplicateKeys.Add(key);
            }
        }

        return speciesForms;
    }

    protected sealed class SemanticLayerBuilder
    {
        private readonly SemanticMaterializationBudget materializationBudget;
        private readonly SortedDictionary<string, SemanticIndexedEntity> entities =
            new(StringComparer.Ordinal);
        private readonly List<SemanticIndexedReference> references = [];
        private readonly List<SemanticDomainStatus> statuses = new(3);

        internal SemanticLayerBuilder(SemanticMaterializationBudget materializationBudget)
        {
            this.materializationBudget = materializationBudget;
            materializationBudget.Admit(
                SemanticExploreSizeEstimator.MaximumLayerEnvelopeSizeBytes,
                ProviderLimitMessage);
        }

        public void AddEntity(SemanticIndexedEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (entities.Count >= SemanticIndexSizingLimits.MaximumEntityCount
                || entity.Fields.Count > SemanticIndexSizingLimits.MaximumFieldCountPerEntity)
            {
                throw new SemanticExploreValidationException(
                    ProviderLimitMessage,
                    SemanticExploreFailureKind.LimitExceeded);
            }

            var key = Key(entity.Record);
            if (entities.ContainsKey(key))
            {
                throw new SemanticExploreValidationException(
                    "A semantic provider returned duplicate record identities.",
                    SemanticExploreFailureKind.InvalidData);
            }

            materializationBudget.Admit(
                SemanticExploreSizeEstimator.EstimateEntity(entity),
                ProviderLimitMessage);
            entities.Add(key, SanitizeEntity(entity));
        }

        public void EnsureAdditionalEntityCapacity(int additionalEntityCount)
        {
            if (additionalEntityCount < 0
                || additionalEntityCount > SemanticIndexSizingLimits.MaximumEntityCount - entities.Count)
            {
                throw new SemanticExploreValidationException(
                    ProviderLimitMessage,
                    SemanticExploreFailureKind.LimitExceeded);
            }
        }

        public IDisposable ReserveTemporaryIndex(int maximumEntryCount)
        {
            if (maximumEntryCount < 0
                || maximumEntryCount > SemanticIndexSizingLimits.MaximumEntityCount)
            {
                throw new SemanticExploreValidationException(
                    ProviderLimitMessage,
                    SemanticExploreFailureKind.LimitExceeded);
            }

            return materializationBudget.ReserveTemporary(
                checked(
                    maximumEntryCount
                    * SemanticExploreSizeEstimator.TemporaryIndexEntrySizeBytes),
                ProviderLimitMessage);
        }

        public void AddReference(SemanticIndexedReference reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            if (references.Count >= SemanticIndexSizingLimits.MaximumReferenceCount)
            {
                throw new SemanticExploreValidationException(
                    ProviderLimitMessage,
                    SemanticExploreFailureKind.LimitExceeded);
            }

            materializationBudget.Admit(
                SemanticExploreSizeEstimator.EstimateReference(reference),
                ProviderLimitMessage);
            references.Add(reference);
        }

        public void AddStatus(SemanticDomainStatus status)
        {
            ArgumentNullException.ThrowIfNull(status);
            materializationBudget.Admit(
                SemanticExploreSizeEstimator.EstimateStatus(status),
                ProviderLimitMessage);
            statuses.Add(status);
        }

        internal SemanticLayerData Complete()
        {
            references.RemoveAll(reference =>
                !entities.ContainsKey(reference.SourceKey)
                || !entities.ContainsKey(reference.TargetKey));
            references.Sort(SemanticIndexedReferenceComparer.Instance);
            return new SemanticLayerData(entities, references, statuses);
        }

        private static SemanticIndexedEntity SanitizeEntity(SemanticIndexedEntity entity)
        {
            return entity with
            {
                Title = SafePresentation(entity.Title, 256),
                Summary = entity.Summary is null
                    ? null
                    : SafePresentation(entity.Summary, 1_024),
                Fields = entity.Fields.ToDictionary(
                    field => field.Key,
                    field => field.Value with
                    {
                        Label = SafePresentation(field.Value.Label, 256),
                        Group = SafePresentation(field.Value.Group, 128),
                        Value = field.Value.Value with
                        {
                            DisplayValue = SafePresentation(
                                field.Value.Value.DisplayValue,
                                1_024),
                        },
                    },
                    StringComparer.Ordinal),
            };
        }
    }

    private sealed class SemanticIndexedReferenceComparer : IComparer<SemanticIndexedReference>
    {
        public static SemanticIndexedReferenceComparer Instance { get; } = new();

        public int Compare(SemanticIndexedReference? left, SemanticIndexedReference? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var comparison = StringComparer.Ordinal.Compare(left.SourceKey, right.SourceKey);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(left.TargetKey, right.TargetKey);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.RelationshipKey, right.RelationshipKey);
        }
    }
}

internal sealed class SwShSemanticExploreProvider : SemanticExploreFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.SwordShield;

    protected override string FamilyKey => "swsh";

    protected override void BuildItems(
        SemanticWorkflowLoad<ItemsWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("items");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, ItemsDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Items.Count);
        foreach (var item in workflow.Items)
        {
            var record = Record(ItemsDomain, "item", item.ItemId);
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                item.Name,
                $"Sword and Shield item {item.ItemId.ToString(CultureInfo.InvariantCulture)}",
                "Items",
                providerId,
                NormalizeSourceFile(item.Provenance.SourceFile),
                item.Provenance.SourceLayer,
                ItemFields(item, workflow, providerId)));

            if (item.Metadata.MachineMoveId is { } moveId)
            {
                builder.AddReference(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", moveId)),
                    "teaches-move",
                    "Teaches move",
                    providerId));
            }
        }

        AddAvailable(builder, providerId, ItemsDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("pokemon");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, PokemonDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Pokemon.Count);
        foreach (var pokemon in workflow.Pokemon)
        {
            var record = Record(PokemonDomain, "pokemon-personal", pokemon.PersonalId);
            var fields = new Dictionary<string, SemanticIndexedField>(StringComparer.Ordinal)
            {
                ["hp"] = Signed("hp", "HP", "Base stats", pokemon.BaseStats.HP, providerId),
                ["attack"] = Signed("attack", "Attack", "Base stats", pokemon.BaseStats.Attack, providerId),
                ["defense"] = Signed("defense", "Defense", "Base stats", pokemon.BaseStats.Defense, providerId),
                ["specialAttack"] = Signed("specialAttack", "Special Attack", "Base stats", pokemon.BaseStats.SpecialAttack, providerId),
                ["specialDefense"] = Signed("specialDefense", "Special Defense", "Base stats", pokemon.BaseStats.SpecialDefense, providerId),
                ["speed"] = Signed("speed", "Speed", "Base stats", pokemon.BaseStats.Speed, providerId),
                ["type1"] = Enumeration("type1", "Primary type", "Identity", pokemon.Personal.Type1, pokemon.Type1, providerId),
                ["type2"] = Enumeration("type2", "Secondary type", "Identity", pokemon.Personal.Type2, pokemon.Type2, providerId),
                ["catchRate"] = Signed("catchRate", "Catch rate", "Growth", pokemon.CatchRate, providerId),
                ["baseExperience"] = Signed("baseExperience", "Base experience", "Growth", pokemon.BaseExperience, providerId),
                ["ability1"] = Enumeration("ability1", "Ability 1", "Abilities", pokemon.Abilities.Ability1, pokemon.Abilities.Ability1Label, providerId),
                ["ability2"] = Enumeration("ability2", "Ability 2", "Abilities", pokemon.Abilities.Ability2, pokemon.Abilities.Ability2Label, providerId),
                ["hiddenAbility"] = Enumeration("hiddenAbility", "Hidden ability", "Abilities", pokemon.Abilities.HiddenAbility, pokemon.Abilities.HiddenAbilityLabel, providerId),
                ["heldItem1"] = Signed("heldItem1", "Held item 1", "Held items", pokemon.Personal.HeldItem1, providerId),
                ["heldItem2"] = Signed("heldItem2", "Held item 2", "Held items", pokemon.Personal.HeldItem2, providerId),
                ["heldItem3"] = Signed("heldItem3", "Held item 3", "Held items", pokemon.Personal.HeldItem3, providerId),
            };
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                pokemon.Name,
                pokemon.FormLabel,
                "Pokemon",
                providerId,
                NormalizeSourceFile(pokemon.Provenance.SourceFile),
                pokemon.Provenance.SourceLayer,
                fields));

            foreach (var move in pokemon.Learnset)
            {
                builder.AddReference(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", move.MoveId)),
                    "learns-move",
                    "Learns move",
                    providerId));
            }

            foreach (var evolution in pokemon.Evolutions)
            {
                var target = workflow.Pokemon.FirstOrDefault(candidate =>
                    candidate.SpeciesId == evolution.Species && candidate.Form == evolution.Form);
                if (target is not null)
                {
                    builder.AddReference(new SemanticIndexedReference(
                        Key(record),
                    Key(Record(PokemonDomain, "pokemon-personal", target.PersonalId)),
                        "evolves-to",
                        "Evolves to",
                        providerId));
                }
            }
        }

        AddAvailable(builder, providerId, PokemonDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("moves");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, MovesDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Moves.Count);
        foreach (var move in workflow.Moves)
        {
            var record = Record(MovesDomain, "move", move.MoveId);
            var fields = new Dictionary<string, SemanticIndexedField>(StringComparer.Ordinal)
            {
                ["canUseMove"] = Boolean("canUseMove", "Usable", "Identity", move.CanUseMove, providerId),
                ["type"] = Enumeration("type", "Type", "Damage", move.Type, move.TypeName, providerId),
                ["category"] = Enumeration("category", "Category", "Damage", move.Category, move.CategoryName, providerId),
                ["power"] = Signed("power", "Power", "Damage", move.Power, providerId),
                ["accuracy"] = Signed("accuracy", "Accuracy", "Damage", move.Accuracy, providerId),
                ["pp"] = Signed("pp", "PP", "Usage", move.PP, providerId),
                ["priority"] = Signed("priority", "Priority", "Usage", move.Priority, providerId),
                ["critStage"] = Signed("critStage", "Critical stage", "Damage", move.CritStage, providerId),
                ["maxMovePower"] = Signed("maxMovePower", "Max Move power", "Damage", move.MaxMovePower, providerId),
                ["target"] = Enumeration("target", "Target", "Usage", move.Target, move.TargetName, providerId),
                ["hitMin"] = Signed("hitMin", "Minimum hits", "Usage", move.HitMin, providerId),
                ["hitMax"] = Signed("hitMax", "Maximum hits", "Usage", move.HitMax, providerId),
                ["inflict"] = Enumeration("inflict", "Condition", "Effects", move.Inflict, move.InflictName, providerId),
                ["inflictPercent"] = Signed("inflictPercent", "Condition chance", "Effects", move.InflictPercent, providerId),
                ["flinch"] = Signed("flinch", "Flinch chance", "Effects", move.Flinch, providerId),
                ["recoil"] = Signed("recoil", "Recoil", "Effects", move.Recoil, providerId),
                ["rawHealing"] = Signed("rawHealing", "Healing", "Effects", move.RawHealing, providerId),
            };
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                move.Name,
                move.Description,
                "Moves",
                providerId,
                NormalizeSourceFile(move.Provenance.SourceFile),
                move.Provenance.SourceLayer,
                fields));
        }

        AddAvailable(builder, providerId, MovesDomain, load.Partial, load.ReasonCode);
    }
}

internal sealed class SvSemanticExploreProvider : SemanticExploreFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.ScarletViolet;

    protected override string FamilyKey => "sv";

    protected override void BuildItems(
        SemanticWorkflowLoad<ItemsWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("items");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, ItemsDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Items.Count);
        foreach (var item in workflow.Items)
        {
            var record = Record(ItemsDomain, "item", item.ItemId);
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                item.Name,
                $"Scarlet and Violet item {item.ItemId.ToString(CultureInfo.InvariantCulture)}",
                "Items",
                providerId,
                NormalizeSourceFile(item.Provenance.SourceFile),
                item.Provenance.SourceLayer,
                ItemFields(item, workflow, providerId)));

            if (item.Metadata.MachineMoveId is { } moveId)
            {
                builder.AddReference(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", moveId)),
                    "machine-move",
                    "Machine move",
                    providerId));
            }
        }

        AddAvailable(builder, providerId, ItemsDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("pokemon");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, PokemonDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Pokemon.Count);
        using var speciesFormReservation = builder.ReserveTemporaryIndex(workflow.Pokemon.Count);
        var speciesForms = UniqueSpeciesForms(workflow.Pokemon);
        foreach (var pokemon in workflow.Pokemon)
        {
            var record = Record(PokemonDomain, "pokemon-personal", pokemon.PersonalId);
            var fields = new Dictionary<string, SemanticIndexedField>(StringComparer.Ordinal)
            {
                ["hp"] = Signed("hp", "HP", "Base stats", pokemon.BaseStats.HP, providerId),
                ["attack"] = Signed("attack", "Attack", "Base stats", pokemon.BaseStats.Attack, providerId),
                ["defense"] = Signed("defense", "Defense", "Base stats", pokemon.BaseStats.Defense, providerId),
                ["specialAttack"] = Signed("specialAttack", "Special Attack", "Base stats", pokemon.BaseStats.SpecialAttack, providerId),
                ["specialDefense"] = Signed("specialDefense", "Special Defense", "Base stats", pokemon.BaseStats.SpecialDefense, providerId),
                ["speed"] = Signed("speed", "Speed", "Base stats", pokemon.BaseStats.Speed, providerId),
                ["type1"] = Enumeration("type1", "Primary type", "Identity", pokemon.Personal.Type1, pokemon.Type1, providerId),
                ["type2"] = Enumeration("type2", "Secondary type", "Identity", pokemon.Personal.Type2, pokemon.Type2, providerId),
                ["catchRate"] = Signed("catchRate", "Catch rate", "Growth", pokemon.CatchRate, providerId),
                ["baseExperience"] = Signed("baseExperience", "Base experience", "Growth", pokemon.BaseExperience, providerId),
                ["height"] = Signed("height", "Height", "Form", pokemon.Height, providerId),
                ["weight"] = Signed("weight", "Weight", "Form", pokemon.Weight, providerId),
                ["ability1"] = Enumeration("ability1", "Ability 1", "Abilities", pokemon.Abilities.Ability1, pokemon.Abilities.Ability1Label, providerId),
                ["ability2"] = Enumeration("ability2", "Ability 2", "Abilities", pokemon.Abilities.Ability2, pokemon.Abilities.Ability2Label, providerId),
                ["hiddenAbility"] = Enumeration("hiddenAbility", "Hidden ability", "Abilities", pokemon.Abilities.HiddenAbility, pokemon.Abilities.HiddenAbilityLabel, providerId),
                ["isPresentInGame"] = Boolean("isPresentInGame", "Present in game", "Availability", pokemon.Personal.IsPresentInGame, providerId),
            };
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                pokemon.Name,
                pokemon.FormLabel,
                "Pokemon",
                providerId,
                NormalizeSourceFile(pokemon.Provenance.SourceFile),
                pokemon.Provenance.SourceLayer,
                fields));

            foreach (var move in pokemon.Learnset)
            {
                builder.AddReference(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", move.MoveId)),
                    "learns-move",
                    "Learns move",
                    providerId));
            }

            foreach (var evolution in pokemon.Evolutions)
            {
                if (speciesForms.TryGetValue((evolution.Species, evolution.Form), out var targetPersonalId))
                {
                    builder.AddReference(new SemanticIndexedReference(
                        Key(record),
                        Key(Record(PokemonDomain, "pokemon-personal", targetPersonalId)),
                        "evolves-to",
                        "Evolves to",
                        providerId));
                }
            }
        }

        AddAvailable(builder, providerId, PokemonDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("moves");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, MovesDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Moves.Count);
        foreach (var move in workflow.Moves)
        {
            var record = Record(MovesDomain, "move", move.MoveId);
            var fields = new Dictionary<string, SemanticIndexedField>(StringComparer.Ordinal)
            {
                ["canUseMove"] = Boolean("canUseMove", "Usable", "Identity", move.CanUseMove, providerId),
                ["type"] = Enumeration("type", "Type", "Damage", move.Type, move.TypeName, providerId),
                ["quality"] = Signed("quality", "Quality", "Damage", move.Quality, providerId),
                ["category"] = Enumeration("category", "Category", "Damage", move.Category, move.CategoryName, providerId),
                ["power"] = Signed("power", "Power", "Damage", move.Power, providerId),
                ["accuracy"] = Signed("accuracy", "Accuracy", "Damage", move.Accuracy, providerId),
                ["pp"] = Signed("pp", "PP", "Usage", move.PP, providerId),
                ["priority"] = Signed("priority", "Priority", "Usage", move.Priority, providerId),
                ["target"] = Enumeration("target", "Target", "Usage", move.Target, move.TargetName, providerId),
                ["hitMin"] = Signed("hitMin", "Minimum hits", "Usage", move.HitMin, providerId),
                ["hitMax"] = Signed("hitMax", "Maximum hits", "Usage", move.HitMax, providerId),
                ["turnMin"] = Signed("turnMin", "Minimum turns", "Usage", move.TurnMin, providerId),
                ["turnMax"] = Signed("turnMax", "Maximum turns", "Usage", move.TurnMax, providerId),
                ["inflict"] = Enumeration("inflict", "Condition", "Effects", move.Inflict, move.InflictName, providerId),
                ["inflictPercent"] = Signed("inflictPercent", "Condition chance", "Effects", move.InflictPercent, providerId),
                ["flinch"] = Signed("flinch", "Flinch chance", "Effects", move.Flinch, providerId),
                ["effectSequence"] = Signed("effectSequence", "Effect sequence", "Effects", move.EffectSequence, providerId),
                ["recoil"] = Signed("recoil", "Recoil", "Effects", move.Recoil, providerId),
                ["rawHealing"] = Signed("rawHealing", "Healing", "Effects", move.RawHealing, providerId),
            };
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                move.Name,
                move.Description,
                "Moves",
                providerId,
                NormalizeSourceFile(move.Provenance.SourceFile),
                move.Provenance.SourceLayer,
                fields));
        }

        AddAvailable(builder, providerId, MovesDomain, load.Partial, load.ReasonCode);
    }
}

internal sealed class ZaSemanticExploreProvider : SemanticExploreFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.LegendsZA;

    protected override string FamilyKey => "za";

    protected override void BuildItems(
        SemanticWorkflowLoad<ItemsWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("items");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, ItemsDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Items.Count);
        foreach (var item in workflow.Items)
        {
            var record = Record(ItemsDomain, "item", item.ItemId);
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                item.Name,
                $"Legends Z-A item {item.ItemId.ToString(CultureInfo.InvariantCulture)}",
                "Items",
                providerId,
                NormalizeSourceFile(item.Provenance.SourceFile),
                item.Provenance.SourceLayer,
                ItemFields(item, workflow, providerId)));

            if (item.Metadata.MachineMoveId is { } moveId)
            {
                builder.AddReference(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", moveId)),
                    "technical-machine-move",
                    "Technical Machine move",
                    providerId));
            }
        }

        AddAvailable(builder, providerId, ItemsDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("pokemon");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, PokemonDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Pokemon.Count);
        using var speciesFormReservation = builder.ReserveTemporaryIndex(workflow.Pokemon.Count);
        var speciesForms = UniqueSpeciesForms(workflow.Pokemon);
        foreach (var pokemon in workflow.Pokemon)
        {
            var record = Record(PokemonDomain, "pokemon-personal", pokemon.PersonalId);
            var fields = new Dictionary<string, SemanticIndexedField>(StringComparer.Ordinal)
            {
                ["hp"] = Signed("hp", "HP", "Base stats", pokemon.BaseStats.HP, providerId),
                ["attack"] = Signed("attack", "Attack", "Base stats", pokemon.BaseStats.Attack, providerId),
                ["defense"] = Signed("defense", "Defense", "Base stats", pokemon.BaseStats.Defense, providerId),
                ["specialAttack"] = Signed("specialAttack", "Special Attack", "Base stats", pokemon.BaseStats.SpecialAttack, providerId),
                ["specialDefense"] = Signed("specialDefense", "Special Defense", "Base stats", pokemon.BaseStats.SpecialDefense, providerId),
                ["speed"] = Signed("speed", "Speed", "Base stats", pokemon.BaseStats.Speed, providerId),
                ["type1"] = Enumeration("type1", "Primary type", "Identity", pokemon.Personal.Type1, pokemon.Type1, providerId),
                ["type2"] = Enumeration("type2", "Secondary type", "Identity", pokemon.Personal.Type2, pokemon.Type2, providerId),
                ["catchRate"] = Signed("catchRate", "Catch rate", "Growth", pokemon.CatchRate, providerId),
                ["evolutionStage"] = Signed("evolutionStage", "Evolution stage", "Growth", pokemon.EvolutionStage, providerId),
                ["baseExperience"] = Signed("baseExperience", "Base experience", "Growth", pokemon.BaseExperience, providerId),
                ["height"] = Signed("height", "Height", "Form", pokemon.Height, providerId),
                ["weight"] = Signed("weight", "Weight", "Form", pokemon.Weight, providerId),
                ["ability1"] = Enumeration("ability1", "Ability 1", "Abilities", pokemon.Abilities.Ability1, pokemon.Abilities.Ability1Label, providerId),
                ["ability2"] = Enumeration("ability2", "Ability 2", "Abilities", pokemon.Abilities.Ability2, pokemon.Abilities.Ability2Label, providerId),
                ["hiddenAbility"] = Enumeration("hiddenAbility", "Hidden ability", "Abilities", pokemon.Abilities.HiddenAbility, pokemon.Abilities.HiddenAbilityLabel, providerId),
                ["alphaMove"] = NullableSigned("alphaMove", "Alpha-exclusive move", "Moves", pokemon.AlphaMove?.MoveId, providerId),
            };
            builder.AddEntity(new SemanticIndexedEntity(
                record,
                pokemon.Name,
                pokemon.FormLabel,
                "Pokemon",
                providerId,
                NormalizeSourceFile(pokemon.Provenance.SourceFile),
                pokemon.Provenance.SourceLayer,
                fields));

            foreach (var move in pokemon.Learnset)
            {
                builder.AddReference(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", move.MoveId)),
                    "learns-move",
                    "Learns move",
                    providerId));
            }

            if (pokemon.AlphaMove?.MoveId is { } alphaMoveId)
            {
                builder.AddReference(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", alphaMoveId)),
                    "alpha-exclusive-move",
                    "Alpha-exclusive move",
                    providerId));
            }

            foreach (var evolution in pokemon.Evolutions)
            {
                if (speciesForms.TryGetValue((evolution.Species, evolution.Form), out var targetPersonalId))
                {
                    builder.AddReference(new SemanticIndexedReference(
                        Key(record),
                        Key(Record(PokemonDomain, "pokemon-personal", targetPersonalId)),
                        "evolves-to",
                        "Evolves to",
                        providerId));
                }
            }
        }

        AddAvailable(builder, providerId, PokemonDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        SemanticLayerBuilder builder)
    {
        var providerId = ProviderId("moves");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(builder, providerId, MovesDomain, load.ReasonCode);
            return;
        }

        builder.EnsureAdditionalEntityCapacity(workflow.Moves.Count);
        foreach (var move in workflow.Moves)
        {
            var record = Record(MovesDomain, "move", move.MoveId);
            var fields = new Dictionary<string, SemanticIndexedField>(StringComparer.Ordinal)
            {
                ["canUseMove"] = Boolean("canUseMove", "Usable", "Identity", move.CanUseMove, providerId),
                ["type"] = Enumeration("type", "Type", "Damage", move.Type, move.TypeName, providerId),
                ["category"] = Enumeration("category", "Category", "Damage", move.Category, move.CategoryName, providerId),
                ["power"] = Signed("power", "Power", "Damage", move.Power, providerId),
                ["accuracy"] = Signed("accuracy", "Accuracy", "Damage", move.Accuracy, providerId),
                ["priority"] = Signed("priority", "Priority", "Usage", move.Priority, providerId),
                ["critStage"] = Signed("critStage", "Critical stage", "Damage", move.CritStage, providerId),
                ["target"] = Enumeration("target", "Target", "Usage", move.Target, move.TargetName, providerId),
                ["inflict"] = Enumeration("inflict", "Condition", "Effects", move.Inflict, move.InflictName, providerId),
                ["inflictPercent"] = Signed("inflictPercent", "Condition chance", "Effects", move.InflictPercent, providerId),
                ["flinch"] = Signed("flinch", "Flinch chance", "Effects", move.Flinch, providerId),
                ["recoil"] = Signed("recoil", "Recoil", "Effects", move.Recoil, providerId),
                ["rawHealing"] = Signed("rawHealing", "Healing", "Effects", move.RawHealing, providerId),
            };
            if (move.Timing is { } timing)
            {
                fields.Add("cooldown", Decimal("cooldown", "Cooldown", "Real-time behavior", timing.Cooldown, providerId));
                fields.Add("effectiveRange", Decimal("effectiveRange", "Effective range", "Real-time behavior", timing.EffectiveRange, providerId));
                fields.Add("projectileCountMin", Signed("projectileCountMin", "Minimum projectiles", "Real-time behavior", timing.ProjectileCountMin, providerId));
                fields.Add("projectileCountMax", Signed("projectileCountMax", "Maximum projectiles", "Real-time behavior", timing.ProjectileCountMax, providerId));
            }

            builder.AddEntity(new SemanticIndexedEntity(
                record,
                move.Name,
                move.Description,
                "Moves",
                providerId,
                NormalizeSourceFile(move.Provenance.SourceFile),
                move.Provenance.SourceLayer,
                fields));
        }

        AddAvailable(builder, providerId, MovesDomain, load.Partial, load.ReasonCode);
    }
}
