// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;
using Google.FlatBuffers;

namespace KM.ZA.Behavior;

// Changes only a selected actor's location predicates and the city's action registrations.
// Appended vectors retain all original tables, action programs, and uninterpreted data.
internal sealed class ZaCityRoamingDocument
{
    internal const string TemplatePath = "param_ai/data/ai/template/action_template_1/action_template_1_array.bin";
    internal const string ScenarioPath = "param_ai/data/ai/scenario_setting/tag_scenario/tag_scenario_array.bin";
    private const string City = "野生ポケ_ゾーン外";
    private const string Wild = "野生ポケモン";
    private const string Usually = "通常状態";
    private readonly Reader templates;
    private readonly Reader scenarios;
    private readonly Dictionary<int, int> actions;
    private readonly Dictionary<int, int> actorReferences;
    private readonly int cityActions;

    internal sealed record Profile(int Species, int Walk, int Home, string Actor, string Zone, bool HomeIdle);
    // These pairs use their own species animations, random walking, and HomePoint only.
    // Species with routes, water, partner actors, or special state requirements are excluded.
    internal static readonly Profile[] Profiles =
    [
        new(322, 7808, 7815, "ドンメル", "野生ポケ_WZ4章B", false),
        new(673, 8717, 8723, "ゴーゴート", "野生ポケ_WZ5章B", true),
        new(123, 8804, 8815, "ストライク", "野生ポケ_WZ5章C", true),
        new(315, 8823, 8824, "ロゼリア", "野生ポケ_WZ5章C", false),
        new(689, 9668, 9669, "ガメノデス", "野生ポケ_WZ7章A", false),
        new(334, 9825, 9826, "チルタリス", "野生ポケ_WZ8章A", false),
        new(229, 9935, 9936, "ヘルガー", "野生ポケ_WZ8章B", false),
        new(1, 10000, 10001, "フシギダネ", "野生ポケ_WZ20", false),
        new(152, 10002, 10003, "チコリータ", "野生ポケ_WZ20", false),
        new(4, 10004, 10005, "ヒトカゲ", "野生ポケ_WZ20", false),
        new(498, 10006, 10007, "ポカブ", "野生ポケ_WZ20", false),
        new(7, 10008, 10009, "ゼニガメ", "野生ポケ_WZ20", false),
        new(158, 10010, 10011, "ワニノコ", "野生ポケ_WZ20", false),
        new(309, 10509, 10510, "ラクライ", "野生ポケ_ラボ", false),
        new(310, 10511, 10512, "ライボルト", "野生ポケ_ラボ", false),
    ];

    internal ZaCityRoamingDocument(byte[] templateBytes, byte[] scenarioBytes)
    {
        templates = new(templateBytes); scenarios = new(scenarioBytes);
        actions = [];
        actorReferences = [];
        foreach (var action in templates.Rows())
        {
            var id = templates.Int(templates.Field(action, 10, 12));
            if (!actions.TryAdd(id, action)) actions[id] = -1;
            foreach (var actor in templates.Tables(templates.Field(action, 3, 12), 32))
                actorReferences[actor] = actorReferences.GetValueOrDefault(actor) + 1;
        }
        var cities = scenarios.Rows().SelectMany(row => scenarios.Tables(scenarios.Field(row, 1, 2), 1024))
            .Where(row => scenarios.String(scenarios.Field(row, 0, 3)) == "WP_zone_outfield").ToArray();
        if (cities.Length != 1) throw Invalid();
        cityActions = scenarios.Field(cities[0], 1, 3);
        if (cityActions == 0) throw Invalid();
        _ = scenarios.Integers(cityActions);
    }

    internal string Status(int species)
    {
        var profile = Profiles.SingleOrDefault(p => p.Species == species);
        if (profile is null) return "unsupported";
        try
        {
            var walk = Actor(profile, profile.Walk, false);
            var home = Actor(profile, profile.Home, true);
            var ids = scenarios.Integers(cityActions);
            var enabled = Groups(true, profile);
            var disabled = Groups(false, profile);
            if (ReadGroups(walk).SequenceEqual(enabled) && ReadGroups(home).SequenceEqual(enabled)
                && ids.Count(i => i == profile.Walk) == 1 && ids.Count(i => i == profile.Home) == 1) return "enabled";
            if (ReadGroups(walk).SequenceEqual(disabled) && ReadGroups(home).SequenceEqual(disabled)
                && !ids.Contains(profile.Walk) && !ids.Contains(profile.Home)) return "disabled";
        }
        catch (Exception e) when (ZaBehaviorSettings.SourceFailure(e)) { }
        return "unavailable";
    }

    internal (byte[] Templates, byte[] Scenarios) Write(IReadOnlyDictionary<int, bool> changes)
    {
        var templateBytes = templates.Bytes.ToArray(); var scenarioBytes = scenarios.Bytes.ToArray();
        var ids = scenarios.Integers(cityActions).ToList();
        foreach (var (species, enable) in changes.OrderBy(p => p.Key))
        {
            var status = Status(species);
            if (status is not ("enabled" or "disabled")) throw Invalid();
            if (enable == (status == "enabled")) continue;
            var profile = Profiles.Single(p => p.Species == species);
            foreach (var id in new[] { profile.Walk, profile.Home })
                templateBytes = ReplaceGroups(templateBytes, templates.Field(Actor(profile, id, id == profile.Home), 1, 4), Groups(enable, profile));
            ids.RemoveAll(id => id == profile.Walk || id == profile.Home);
            if (enable) { ids.Add(profile.Walk); ids.Add(profile.Home); }
        }
        if (!ids.SequenceEqual(scenarios.Integers(cityActions)))
        {
            using var stream = new MemoryStream(); stream.Write(scenarioBytes);
            using var writer = new BinaryWriter(stream);
            while (stream.Length % 4 != 0) writer.Write((byte)0);
            var target = checked((int)stream.Position);
            writer.Write(ids.Count); foreach (var id in ids) writer.Write(id);
            scenarioBytes = stream.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(scenarioBytes.AsSpan(cityActions), checked(target - cityActions));
        }
        var check = new ZaCityRoamingDocument(templateBytes, scenarioBytes);
        if (changes.Any(p => check.Status(p.Key) != (p.Value ? "enabled" : "disabled"))) throw Invalid();
        return (templateBytes, scenarioBytes);
    }

    private int Actor(Profile p, int id, bool home)
    {
        if (!actions.TryGetValue(id, out var action) || action < 0) throw Invalid();
        var interrupts = templates.Vector(templates.Field(action, 5, 12), 1);
        if (templates.Int(templates.Field(action, 0, 12)) != 6
            || templates.IntByte(templates.Field(action, 1, 12)) != 0
            || templates.Vector(templates.Field(action, 4, 12), 1024).Length != 0
            || interrupts.Length != 1 || templates.String(interrupts[0]) != "State_usually"
            || templates.Vector(templates.Field(action, 6, 12), 1024).Length != 0
            || templates.Int(templates.Field(action, 7, 12)) != 0
            || templates.Int(templates.Field(action, 8, 12)) != 0
            || templates.Int(templates.Field(action, 9, 12)) != 0) throw Invalid();
        var actors = templates.Tables(templates.Field(action, 3, 12), 2);
        if (actors.Length != 2 || actorReferences[actors[0]] != 1
            || templates.String(templates.Field(actors[0], 0, 4)) != p.Actor
            || templates.String(templates.Field(actors[1], 0, 4)) != "HomePoint"
            || !ReadGroups(actors[1]).SequenceEqual([new Group(6, "ホームポイント", false)])
            || templates.Vector(templates.Field(actors[0], 2, 4), 1024).Length != 0
            || templates.Vector(templates.Field(actors[0], 3, 4), 1024).Length != 0
            || templates.Vector(templates.Field(actors[1], 2, 4), 1024).Length != 0
            || templates.Vector(templates.Field(actors[1], 3, 4), 1024).Length != 0) throw Invalid();
        var global = ReadGroupsAt(templates.Field(action, 2, 12));
        if (!global.SequenceEqual([new Group(6, home ? "役者間距離#>3" : "役者間距離#<=3", false)])) throw Invalid();
        var data = templates.Tables(templates.Field(action, 11, 12), 1);
        if (data.Length != 1) throw Invalid();
        var names = templates.Vector(templates.Field(data[0], 0, 7), 1);
        if (names.Length != 1 || templates.String(names[0]) != p.Actor) throw Invalid();
        int[] expected = home ? (p.HomeIdle ? [1855, 1352, 1551, 1850, 1551] : [1855, 1352, 1551]) : [1855, 1020, 1551, 1850, 1551];
        for (var slot = 1; slot <= 6; slot++)
        {
            var commands = templates.Tables(templates.Field(data[0], slot, 7), 16);
            if (slot > expected.Length) { if (commands.Length != 0) throw Invalid(); continue; }
            if (commands.Length != 1 || templates.Int(templates.Field(commands[0], 0, 3)) != 100
                || templates.Int(templates.Field(commands[0], 1, 3)) != expected[slot - 1]
                || templates.String(templates.Field(commands[0], 2, 3)) != (home && slot == 2 ? "HomePoint" : "")) throw Invalid();
        }
        return actors[0];
    }

    private sealed record Group(int Operator, string Tag, bool Not, string? Alternative = null);
    private static Group[] Groups(bool enabled, Profile p) => enabled
        ? [new(6, Wild, false), new(7, p.Zone, false, City), new(6, Usually, false), new(6, p.Actor, false)]
        : [new(6, Wild, false), new(6, City, true), new(6, p.Zone, false), new(6, Usually, false), new(6, p.Actor, false)];
    private Group[] ReadGroups(int actor) => ReadGroupsAt(templates.Field(actor, 1, 4));
    private Group[] ReadGroupsAt(int field) => templates.Tables(field, 32).Select(row =>
    {
        var tags = templates.Tables(templates.Field(row, 1, 2), 2);
        if (tags.Length is < 1 or > 2) throw Invalid();
        var first = tags[0];
        if (tags.Length == 2 && templates.IntByte(templates.Field(tags[1], 1, 2)) != 0) throw Invalid();
        return new Group(templates.Int(templates.Field(row, 0, 2)), templates.String(templates.Field(first, 0, 2)),
            templates.IntByte(templates.Field(first, 1, 2)) != 0,
            tags.Length == 2 ? templates.String(templates.Field(tags[1], 0, 2)) : null);
    }).ToArray();

    private static byte[] ReplaceGroups(byte[] bytes, int field, Group[] groups)
    {
        var b = new FlatBufferBuilder(1024);
        var offsets = new List<int>();
        foreach (var group in groups)
        {
            var tags = new List<int>();
            foreach (var (text, negate) in new[] { (group.Tag, group.Not), (group.Alternative, false) })
            {
                if (text is null) continue;
                var name = b.CreateString(text);
                b.StartTable(2); b.AddOffset(0, name.Value, 0); b.AddBool(1, negate, false); tags.Add(b.EndTable());
            }
            b.StartVector(4, tags.Count, 4); foreach (var tag in tags.AsEnumerable().Reverse()) b.AddOffset(tag);
            var vector = b.EndVector();
            b.StartTable(2); b.AddInt(0, group.Operator, 0); b.AddOffset(1, vector.Value, 0); offsets.Add(b.EndTable());
        }
        b.StartVector(4, offsets.Count, 4); foreach (var offset in offsets.AsEnumerable().Reverse()) b.AddOffset(offset);
        b.Finish(b.EndVector().Value);
        var suffix = b.SizedByteArray();
        var start = checked((bytes.Length + 7) / 8 * 8);
        var output = new byte[checked(start + suffix.Length)]; bytes.CopyTo(output, 0); suffix.CopyTo(output, start);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(field), checked(start + BinaryPrimitives.ReadInt32LittleEndian(suffix) - field));
        return output;
    }

    private static InvalidDataException Invalid() => new("City roaming requires compatible walking, home, and city scenario data.");

    private sealed class Reader
    {
        internal byte[] Bytes { get; }
        internal Reader(byte[] bytes) { if (bytes.Length is < 16 or > 32 * 1024 * 1024) throw Invalid(); Bytes = bytes; }
        internal int Int(int at) { if (at == 0) return 0; Range(at, 4); return BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(at)); }
        internal byte IntByte(int at) { if (at == 0) return 0; Range(at, 1); return Bytes[at]; }
        private int Target(int at) { Range(at, 4); var n = BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(at)); if (n < 4 || n > int.MaxValue) throw Invalid(); var target = checked(at + (int)n); Range(target, 4); return target; }
        internal int Field(int table, int index, int maximum)
        {
            Range(table, 4); var vt = checked(table - Int(table)); Range(vt, 4);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(vt));
            var objectSize = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(vt + 2));
            if (size < 4 || size % 2 != 0 || size > maximum * 2 + 4 || objectSize < 4) throw Invalid();
            Range(vt, size); Range(table, objectSize);
            if (index * 2 + 4 >= size) return 0;
            var offset = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(vt + index * 2 + 4));
            if (offset == 0) return 0;
            if (offset < 4 || offset >= objectSize) throw Invalid();
            return checked(table + offset);
        }
        internal int[] Vector(int field, int maximum)
        {
            if (field == 0) return [];
            var at = Target(field); var count = Int(at);
            if (count < 0 || count > maximum) throw Invalid(); Range(at + 4, checked(count * 4));
            return Enumerable.Range(0, count).Select(i => at + 4 + i * 4).ToArray();
        }
        internal int[] Tables(int field, int maximum) => Vector(field, maximum).Select(Target).ToArray();
        internal int[] Integers(int field) => Vector(field, 50000).Select(Int).ToArray();
        internal int[] Rows() => Tables(Field(Target(0), 0, 1), 50000).SelectMany(db => Tables(Field(db, 0, 1), 50000)).ToArray();
        internal string String(int field)
        {
            if (field == 0) return "";
            var at = Target(field); var count = Int(at);
            if (count < 0 || count > 4096) throw Invalid(); Range(at + 4, count + 1);
            if (Bytes[at + 4 + count] != 0) throw Invalid();
            return new UTF8Encoding(false, true).GetString(Bytes, at + 4, count);
        }
        private void Range(int at, int count) { if (at < 0 || count < 0 || at > Bytes.Length - count) throw Invalid(); }
    }
}
