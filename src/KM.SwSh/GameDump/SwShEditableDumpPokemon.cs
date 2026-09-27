// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.GameDump;
using KM.SwSh.Pokemon;
using static KM.Core.GameDump.DumpSchemaBuilder;

namespace KM.SwSh.GameDump;

public sealed partial class SwShEditableDumpProvider
{
    private static void BuildPokemon(SwShPokemonWorkflow data, List<DumpRecord> records)
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
