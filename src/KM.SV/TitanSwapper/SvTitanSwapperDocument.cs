// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using Google.FlatBuffers;
using KM.Formats;
using KM.Formats.SV.Placement;

namespace KM.SV.TitanSwapper;

public sealed record SvTitanSwapperRow(string Id, int StorySpecies, int Phase, string Edition,
    IReadOnlyDictionary<string, int> Values);

internal sealed class SvTitanSwapperDocument
{
    public const string Prefix = "km_titan_";
    public static readonly (string Label, int Species, string Edition)[] Encounters =
    [
        ("nusi_962_01", 962, "both"), ("nusi_962_02", 962, "both"),
        ("nusi_959_01", 959, "both"), ("nusi_959_02", 959, "both"),
        ("nusi_944_01", 944, "both"), ("nusi_944_02", 944, "both"),
        ("nusi_978_01", 978, "scarlet"), ("nusi_978_02", 978, "scarlet"),
        ("nusi_986_01", 986, "violet"), ("nusi_986_02", 986, "violet"),
        ("nusi_931_01", 931, "both"), ("nusi_931_02", 931, "both"),
        ("nusi_952_01", 952, "both"),
    ];
    private readonly EventBattlePokemonArray table;
    private readonly Dictionary<string, EventBattlePokemon> records;
    public string Revision { get; }
    public IReadOnlyList<SvTitanSwapperRow> Rows { get; }

    public SvTitanSwapperDocument(byte[] bytes)
    {
        if (bytes.Length is < 16 or > 4 * 1024 * 1024) throw new InvalidDataException("Invalid event table size.");
        table = EventBattlePokemonArray.GetRootAsEventBattlePokemonArray(new ByteBuffer(bytes));
        FlatBufferMergeGuard.Validate(table);
        if (table.ValuesLength is < 13 or > 10000) throw new InvalidDataException("Invalid event inventory.");
        records = new(StringComparer.Ordinal);
        for (var i = 0; i < table.ValuesLength; i++)
        {
            var row = table.Values(i) ?? throw new InvalidDataException("Missing event row.");
            if (string.IsNullOrEmpty(row.Label) || !records.TryAdd(row.Label, row))
                throw new InvalidDataException("Missing or duplicate event label.");
            if (row.Label.StartsWith(Prefix, StringComparison.Ordinal)
                && !Encounters.Any(encounter => Prefix + encounter.Label == row.Label))
                throw new InvalidDataException("Unrecognized Titan Swapper record.");
        }
        Rows = Encounters.Select(encounter =>
        {
            var original = records[encounter.Label].PokeData ?? throw new InvalidDataException("Missing Titan data.");
            var enabled = records.TryGetValue(Prefix + encounter.Label, out var replacement);
            var value = enabled ? replacement.PokeData ?? throw new InvalidDataException("Missing replacement data.") : original;
            if (enabled && (value.FormId != 0 || value.Level is < 1 or > 100))
                throw new InvalidDataException("Unsupported replacement values.");
            return new SvTitanSwapperRow(encounter.Label, encounter.Species,
                encounter.Label == "nusi_952_01" ? 3 : encounter.Label.EndsWith("02", StringComparison.Ordinal) ? 2 : 1,
                encounter.Edition, new Dictionary<string, int>
                { ["enabled"] = enabled ? 1 : 0, ["species"] = (int)value.DevId, ["level"] = value.Level });
        }).ToArray();
        Revision = Convert.ToHexString(SHA256.HashData(bytes));
        FlatBufferMergeGuard.ValidateRoundTrip(table, Serialize(null));
    }

    public byte[] Write(IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> values) => Serialize(values);

    internal byte[] RecoverLegacy(SvTitanSwapperDocument baseline, string edition, out bool changed)
    {
        var legacy = new Dictionary<string, EventBattlePokemon>(StringComparer.Ordinal);
        foreach (var encounter in Encounters.Where(entry => entry.Edition == "both" || entry.Edition == edition))
        {
            var current = records[encounter.Label];
            var original = baseline.records[encounter.Label];
            var pokemon = current.PokeData!.Value;
            var originalPokemon = original.PokeData!.Value;
            if (pokemon.DevId == originalPokemon.DevId && pokemon.FormId == originalPokemon.FormId) continue;
            // Story and combat identities use the same game species IDs as names and models.
            if ((int)originalPokemon.DevId != int.Parse(encounter.Label.AsSpan(5, 3), System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidDataException("The original Titan identity is not supported.");
            if (!records.ContainsKey(Prefix + encounter.Label) && (pokemon.FormId != 0 || pokemon.Level is < 1 or > 100))
                throw new NotSupportedException("Legacy Titan replacements require form 0 and a level from 1 to 100.");
            legacy.Add(encounter.Label, current);
        }
        changed = legacy.Count > 0;
        if (!changed) return [];

        var builder = new FlatBufferBuilder(32768);
        var offsets = new List<Offset<EventBattlePokemon>>();
        for (var i = 0; i < table.ValuesLength; i++)
        {
            var row = table.Values(i)!.Value;
            offsets.Add(WriteRow(builder, legacy.ContainsKey(row.Label!) ? baseline.records[row.Label!] : row,
                row.Label!, null));
        }
        foreach (var (label, row) in legacy)
            if (!records.ContainsKey(Prefix + label))
                offsets.Add(WriteRow(builder, row, Prefix + label, null));
        var vector = EventBattlePokemonArray.CreateValuesVector(builder, offsets.ToArray());
        EventBattlePokemonArray.FinishEventBattlePokemonArrayBuffer(builder,
            EventBattlePokemonArray.CreateEventBattlePokemonArray(builder, vector));
        return builder.SizedByteArray();
    }

    private byte[] Serialize(IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? values)
    {
        var builder = new FlatBufferBuilder(32768);
        var offsets = new List<Offset<EventBattlePokemon>>();
        for (var i = 0; i < table.ValuesLength; i++)
        {
            var row = table.Values(i)!.Value;
            if (values is not null && row.Label!.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            offsets.Add(WriteRow(builder, row, row.Label!, null));
        }
        if (values is not null)
            foreach (var encounter in Encounters)
            {
                var v = values[encounter.Label];
                if (v["enabled"] != 0)
                {
                    // Keep fields outside this editor when updating an existing combat record.
                    var row = records.GetValueOrDefault(Prefix + encounter.Label, records[encounter.Label]);
                    offsets.Add(WriteRow(builder, row, Prefix + encounter.Label, v));
                }
            }
        var vector = EventBattlePokemonArray.CreateValuesVector(builder, offsets.ToArray());
        var root = EventBattlePokemonArray.CreateEventBattlePokemonArray(builder, vector);
        EventBattlePokemonArray.FinishEventBattlePokemonArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static Offset<EventBattlePokemon> WriteRow(FlatBufferBuilder builder, EventBattlePokemon row,
        string label, IReadOnlyDictionary<string, int>? replacement)
    {
        var name = builder.CreateString(label);
        var data = row.PokeData is { } pokemon ? WritePokemon(builder, pokemon, replacement) : default;
        return EventBattlePokemon.CreateEventBattlePokemon(builder, name, data, row.DisableBattleOut, row.EventEncount);
    }

    private static Offset<global::PokeDataEventBattle> WritePokemon(FlatBufferBuilder b, global::PokeDataEventBattle p,
        IReadOnlyDictionary<string, int>? replacement)
    {
        var ivs = Stats(b, p.TalentValue);
        var evs = Stats(b, p.EffortValue);
        var m1 = Move(b, replacement is null ? p.Waza1 : null);
        var m2 = Move(b, replacement is null ? p.Waza2 : null);
        var m3 = Move(b, replacement is null ? p.Waza3 : null);
        var m4 = Move(b, replacement is null ? p.Waza4 : null);
        return global::PokeDataEventBattle.CreatePokeDataEventBattle(b,
            replacement is null ? p.DevId : (global::pml.common.DevID)checked((ushort)replacement["species"]),
            replacement is null ? p.FormId : (short)0,
            replacement is null ? p.Sex : global::SexType.DEFAULT,
            replacement is null ? p.Level : replacement["level"], p.RareType, p.TalentType, p.TalentVnum,
            ivs, evs, p.Item, p.DropItem, p.DropItemNum, p.Seikaku, p.SeikakuHosei,
            replacement is null ? p.Tokusei : global::TokuseiType.RANDOM_12,
            replacement is null ? p.WazaType : global::WazaType.DEFAULT,
            m1, m2, m3, m4, p.GemType, p.ScaleType, p.ScaleValue, p.SetRibbon);
    }

    private static Offset<global::ParamSet> Stats(FlatBufferBuilder b, global::ParamSet? value) => value is { } p
        ? global::ParamSet.CreateParamSet(b, p.Hp, p.Atk, p.Def, p.SpAtk, p.SpDef, p.Agi) : default;
    private static Offset<global::WazaSet> Move(FlatBufferBuilder b, global::WazaSet? value) => value is { } p
        ? global::WazaSet.CreateWazaSet(b, p.WazaId, p.PointUp) : default;
}
