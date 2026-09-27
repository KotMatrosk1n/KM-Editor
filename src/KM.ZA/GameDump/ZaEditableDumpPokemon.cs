// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.GameDump;
using KM.ZA.Pokemon;
using static KM.Core.GameDump.DumpSchemaBuilder;

namespace KM.ZA.GameDump;

public sealed partial class ZaEditableDumpProvider
{
    private static void BuildPokemon(ZaPokemonWorkflow data, List<DumpRecord> records)
    {
        var fields = Definitions(data.EditableFields);
        var moveOptions = data.LearnsetMoveOptions.Select(o => new DumpChoice(Value(o.Value), o.Label)).ToArray();
        var methods = data.EvolutionMethodOptions.Select(o => new DumpChoice(Value(o.Value), o.Label)).ToArray();
        var pokemonPaths = fields.ToDictionary(f => f.Field, f => "personal." + f.Field);
        foreach (var stat in new[] { "hp", "attack", "defense", "specialAttack", "specialDefense", "speed" }) pokemonPaths[stat] = "baseStats." + stat;
        foreach (var ability in new[] { "ability1", "ability2", "hiddenAbility" }) pokemonPaths[ability] = "abilities." + ability;
        foreach (var field in new[] { "baseExperience", "height", "weight" }) pokemonPaths[field] = field;
        foreach (var pokemon in data.Pokemon)
        {
            var record = Record(pokemon, fields, "personalId", "name", "pokemon", pokemonPaths);
            if (pokemon.AlphaMove is { HasMapping: true, MoveId: { } alphaMove } alpha)
                record.Fields.Add(new("alpha_move", "alphaMove", Value(alphaMove), Minimum: 0, Maximum: ushort.MaxValue,
                    Choices: alpha.Options.Select(o => new DumpChoice(Value(o.Value), o.Label)).ToArray(), Editable: alpha.CanEdit));
            if (pokemon.AlphaSizes is { Count: > 0 } sizes)
                record.Children["alpha_sizes"] = sizes.Select(size =>
                {
                    // Shared model configurations use one canonical editor target across forms.
                    var child = new DumpRecord(size.ResourcePath, "Alpha size", "pokemon", Value(size.SharedPersonalIds.Min()));
                    child.Fields.Add(new("scale", size.Field, Value(size.Scale), "number", 0, float.MaxValue));
                    child.Fields.Add(new("minimum_scale", "minimumScale", Value(size.MinimumScale), "number", Editable: false));
                    child.Fields.Add(new("default_scale", "vanillaScale", Value(size.VanillaScale), "number", Editable: false));
                    return child;
                }).ToList();
            record.Children["learnset"] = pokemon.Learnset.Select(move =>
            {
                var child = new DumpRecord(Value(move.Slot), move.MoveName, "learnset", Value(pokemon.PersonalId), move.Slot);
                child.Fields.Add(new("move", "moveId", Value(move.MoveId), Minimum: 1, Maximum: ushort.MaxValue, Choices: moveOptions));
                child.Fields.Add(new("level", "level", Value(move.Level), Minimum: -1, Maximum: 100));
                return child;
            }).ToList();
            record.Children["evolutions"] = pokemon.Evolutions.Select(evolution =>
            {
                var child = new DumpRecord(Value(evolution.Slot), evolution.MethodName, "evolution", Value(pokemon.PersonalId), evolution.Slot);
                child.Fields.Add(new("method", "method", Value(evolution.Method), Minimum: 0, Maximum: ushort.MaxValue, Choices: methods));
                child.Fields.Add(new("argument", "argument", Value(evolution.Argument), Minimum: 0, Maximum: ushort.MaxValue));
                child.Fields.Add(new("species", "species", Value(evolution.Species), Minimum: 0, Maximum: ushort.MaxValue));
                child.Fields.Add(new("form", "form", Value(evolution.Form), Minimum: 0, Maximum: ushort.MaxValue));
                child.Fields.Add(new("level", "level", Value(evolution.Level), Minimum: 0, Maximum: 100));
                return child;
            }).ToList();
            record.Children["compatibility"] = pokemon.Compatibility.Select(group =>
            {
                var child = new DumpRecord(group.GroupId, group.Label, "pokemon", Value(pokemon.PersonalId));
                foreach (var move in group.Entries)
                    child.Fields.Add(new($"move_{move.Slot}_{Key(move.MoveName).Replace(' ', '_')}",
                        $"compatibility:{group.GroupId}:{move.Slot}", move.CanLearn ? "1" : "0", "boolean", 0, 1));
                return child;
            }).ToList();
            records.Add(record);
        }
    }

}
