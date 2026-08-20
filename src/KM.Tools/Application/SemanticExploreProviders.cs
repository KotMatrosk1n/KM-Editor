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

    SemanticLayerData Build(SemanticWorkflowCorpus corpus);
}

internal sealed record SemanticWorkflowLoad<T>(
    T? Value,
    string? ReasonCode,
    bool Partial = false)
    where T : class;

internal sealed record SemanticWorkflowCorpus(
    SemanticWorkflowLoad<ItemsWorkflowDto> Items,
    SemanticWorkflowLoad<PokemonWorkflowDto> Pokemon,
    SemanticWorkflowLoad<MovesWorkflowDto> Moves);

internal sealed record SemanticLayerData(
    IReadOnlyDictionary<string, SemanticIndexedEntity> Entities,
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

    protected const string ItemsDomain = "workflow.items";
    protected const string PokemonDomain = "workflow.pokemon";
    protected const string MovesDomain = "workflow.moves";

    public abstract SemanticGameFamilyDto GameFamily { get; }

    protected abstract string FamilyKey { get; }

    public SemanticLayerData Build(SemanticWorkflowCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var entities = new Dictionary<string, SemanticIndexedEntity>(StringComparer.Ordinal);
        var references = new List<SemanticIndexedReference>();
        var statuses = new List<SemanticDomainStatus>(3);

        BuildItems(corpus.Items, entities, references, statuses);
        BuildPokemon(corpus.Pokemon, entities, references, statuses);
        BuildMoves(corpus.Moves, entities, references, statuses);

        references.RemoveAll(reference =>
            !entities.ContainsKey(reference.SourceKey) || !entities.ContainsKey(reference.TargetKey));
        references.Sort(SemanticIndexedReferenceComparer.Instance);

        var safeEntities = entities.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Title = SafePresentation(pair.Value.Title, 256),
                Summary = pair.Value.Summary is null
                    ? null
                    : SafePresentation(pair.Value.Summary, 1_024),
                Fields = pair.Value.Fields.ToDictionary(
                    field => field.Key,
                    field => field.Value with
                    {
                        Label = SafePresentation(field.Value.Label, 256),
                        Group = SafePresentation(field.Value.Group, 128),
                        Value = field.Value.Value with
                        {
                            DisplayValue = SafePresentation(field.Value.Value.DisplayValue, 1_024),
                        },
                    },
                    StringComparer.Ordinal),
            },
            StringComparer.Ordinal);

        return new SemanticLayerData(safeEntities, references, statuses);
    }

    protected abstract void BuildItems(
        SemanticWorkflowLoad<ItemsWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses);

    protected abstract void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses);

    protected abstract void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses);

    protected string ProviderId(string domainKey) => $"{FamilyKey}.{domainKey}.semantic";

    protected void AddUnavailable(
        ICollection<SemanticDomainStatus> statuses,
        string providerId,
        string domain,
        string? reasonCode)
    {
        statuses.Add(new SemanticDomainStatus(
            providerId,
            domain,
            Available: false,
            reasonCode ?? "provider-unavailable"));
    }

    protected void AddAvailable(
        ICollection<SemanticDomainStatus> statuses,
        string providerId,
        string domain,
        bool partial = false,
        string? reasonCode = null)
    {
        statuses.Add(new SemanticDomainStatus(
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

    protected static void AddEntity(
        IDictionary<string, SemanticIndexedEntity> entities,
        SemanticIndexedEntity entity)
    {
        var key = Key(entity.Record);
        if (!entities.TryAdd(key, entity))
        {
            throw new SemanticExploreValidationException(
                "A semantic provider returned duplicate record identities.",
                SemanticExploreFailureKind.InvalidData);
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
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("items");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, ItemsDomain, load.ReasonCode);
            return;
        }

        foreach (var item in workflow.Items.OrderBy(item => item.ItemId))
        {
            var record = Record(ItemsDomain, "item", item.ItemId);
            AddEntity(entities, new SemanticIndexedEntity(
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
                references.Add(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", moveId)),
                    "teaches-move",
                    "Teaches move",
                    providerId));
            }
        }

        AddAvailable(statuses, providerId, ItemsDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("pokemon");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, PokemonDomain, load.ReasonCode);
            return;
        }

        foreach (var pokemon in workflow.Pokemon.OrderBy(pokemon => pokemon.PersonalId))
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
            AddEntity(entities, new SemanticIndexedEntity(
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
                references.Add(new SemanticIndexedReference(
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
                    references.Add(new SemanticIndexedReference(
                        Key(record),
                    Key(Record(PokemonDomain, "pokemon-personal", target.PersonalId)),
                        "evolves-to",
                        "Evolves to",
                        providerId));
                }
            }
        }

        AddAvailable(statuses, providerId, PokemonDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("moves");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, MovesDomain, load.ReasonCode);
            return;
        }

        foreach (var move in workflow.Moves.OrderBy(move => move.MoveId))
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
            AddEntity(entities, new SemanticIndexedEntity(
                record,
                move.Name,
                move.Description,
                "Moves",
                providerId,
                NormalizeSourceFile(move.Provenance.SourceFile),
                move.Provenance.SourceLayer,
                fields));
        }

        AddAvailable(statuses, providerId, MovesDomain, load.Partial, load.ReasonCode);
    }
}

internal sealed class SvSemanticExploreProvider : SemanticExploreFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.ScarletViolet;

    protected override string FamilyKey => "sv";

    protected override void BuildItems(
        SemanticWorkflowLoad<ItemsWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("items");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, ItemsDomain, load.ReasonCode);
            return;
        }

        foreach (var item in workflow.Items.OrderBy(item => item.ItemId))
        {
            var record = Record(ItemsDomain, "item", item.ItemId);
            AddEntity(entities, new SemanticIndexedEntity(
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
                references.Add(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", moveId)),
                    "machine-move",
                    "Machine move",
                    providerId));
            }
        }

        AddAvailable(statuses, providerId, ItemsDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("pokemon");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, PokemonDomain, load.ReasonCode);
            return;
        }

        var speciesForms = workflow.Pokemon
            .GroupBy(pokemon => (pokemon.SpeciesId, pokemon.Form))
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().PersonalId);
        foreach (var pokemon in workflow.Pokemon.OrderBy(pokemon => pokemon.PersonalId))
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
            AddEntity(entities, new SemanticIndexedEntity(
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
                references.Add(new SemanticIndexedReference(
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
                    references.Add(new SemanticIndexedReference(
                        Key(record),
                        Key(Record(PokemonDomain, "pokemon-personal", targetPersonalId)),
                        "evolves-to",
                        "Evolves to",
                        providerId));
                }
            }
        }

        AddAvailable(statuses, providerId, PokemonDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("moves");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, MovesDomain, load.ReasonCode);
            return;
        }

        foreach (var move in workflow.Moves.OrderBy(move => move.MoveId))
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
            AddEntity(entities, new SemanticIndexedEntity(
                record,
                move.Name,
                move.Description,
                "Moves",
                providerId,
                NormalizeSourceFile(move.Provenance.SourceFile),
                move.Provenance.SourceLayer,
                fields));
        }

        AddAvailable(statuses, providerId, MovesDomain, load.Partial, load.ReasonCode);
    }
}

internal sealed class ZaSemanticExploreProvider : SemanticExploreFamilyProviderBase
{
    public override SemanticGameFamilyDto GameFamily => SemanticGameFamilyDto.LegendsZA;

    protected override string FamilyKey => "za";

    protected override void BuildItems(
        SemanticWorkflowLoad<ItemsWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("items");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, ItemsDomain, load.ReasonCode);
            return;
        }

        foreach (var item in workflow.Items.OrderBy(item => item.ItemId))
        {
            var record = Record(ItemsDomain, "item", item.ItemId);
            AddEntity(entities, new SemanticIndexedEntity(
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
                references.Add(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", moveId)),
                    "technical-machine-move",
                    "Technical Machine move",
                    providerId));
            }
        }

        AddAvailable(statuses, providerId, ItemsDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildPokemon(
        SemanticWorkflowLoad<PokemonWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("pokemon");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, PokemonDomain, load.ReasonCode);
            return;
        }

        var speciesForms = workflow.Pokemon
            .GroupBy(pokemon => (pokemon.SpeciesId, pokemon.Form))
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().PersonalId);
        foreach (var pokemon in workflow.Pokemon.OrderBy(pokemon => pokemon.PersonalId))
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
            AddEntity(entities, new SemanticIndexedEntity(
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
                references.Add(new SemanticIndexedReference(
                    Key(record),
                    Key(Record(MovesDomain, "move", move.MoveId)),
                    "learns-move",
                    "Learns move",
                    providerId));
            }

            if (pokemon.AlphaMove?.MoveId is { } alphaMoveId)
            {
                references.Add(new SemanticIndexedReference(
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
                    references.Add(new SemanticIndexedReference(
                        Key(record),
                        Key(Record(PokemonDomain, "pokemon-personal", targetPersonalId)),
                        "evolves-to",
                        "Evolves to",
                        providerId));
                }
            }
        }

        AddAvailable(statuses, providerId, PokemonDomain, load.Partial, load.ReasonCode);
    }

    protected override void BuildMoves(
        SemanticWorkflowLoad<MovesWorkflowDto> load,
        IDictionary<string, SemanticIndexedEntity> entities,
        ICollection<SemanticIndexedReference> references,
        ICollection<SemanticDomainStatus> statuses)
    {
        var providerId = ProviderId("moves");
        if (load.Value is not { } workflow)
        {
            AddUnavailable(statuses, providerId, MovesDomain, load.ReasonCode);
            return;
        }

        foreach (var move in workflow.Moves.OrderBy(move => move.MoveId))
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

            AddEntity(entities, new SemanticIndexedEntity(
                record,
                move.Name,
                move.Description,
                "Moves",
                providerId,
                NormalizeSourceFile(move.Provenance.SourceFile),
                move.Provenance.SourceLayer,
                fields));
        }

        AddAvailable(statuses, providerId, MovesDomain, load.Partial, load.ReasonCode);
    }
}
