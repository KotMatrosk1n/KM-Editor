// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using Google.FlatBuffers;

namespace KM.ZA.Behavior;

internal sealed partial class ZaCityRoamingDocument
{
    // Retail leaves action IDs 22518 through 25500 unused across the action and scenario tables.
    private const int FirstGeneratedAction = 22518;
    private const string HomePoint = "HomePoint";

    private static int GeneratedAction(int species) => FirstGeneratedAction + species * 2;

    private string GeneratedStatus(int species)
    {
        if (nativeSpecies is null) return "unavailable";
        if (!speciesActors.TryGetValue(species, out var actor)) return "unavailable";
        var id = GeneratedAction(species);
        if (nonCityIds.Contains(id) || nonCityIds.Contains(id + 1)) return "unavailable";
        if (!actions.ContainsKey(id) && !actions.ContainsKey(id + 1) && !cityIds.Contains(id) && !cityIds.Contains(id + 1))
            return nativeSpecies.Contains(species) ? NativeStatus(species, actor) : "unsupported";
        if (nativeSpecies.Contains(species) || cityIds.Count(i => i == id) != 1 || cityIds.Count(i => i == id + 1) != 1)
            return "unavailable";
        foreach (var choice in new[] { "ground", "hover" })
            if (MatchesGeneratedAction(id, actor, choice, false) && MatchesGeneratedAction(id + 1, actor, choice, true))
                return choice;
        return "unavailable";
    }

    private bool MatchesGeneratedAction(int id, string actor, string choice, bool home)
    {
        if (actions.GetValueOrDefault(id, -1) < 0 || !actionDatabases.TryGetValue(id, out var database)
            || templates.DatabaseRows(database).Length != 1)
            return false;
        var expected = BuildAction(id, actor, choice, home);
        var start = database - BinaryPrimitives.ReadInt32LittleEndian(expected);
        return start >= 0 && start <= templates.Bytes.Length - expected.Length
            && templates.Bytes.AsSpan(start, expected.Length).SequenceEqual(expected);
    }

    private static byte[] BuildAction(int id, string actor, string choice, bool home)
    {
        var b = new FlatBufferBuilder(1024);
        var hover = choice == "hover";
        var distance = hover ? 5 : 3;
        var global = BuildGroups(b, [new(6, home ? $"役者間距離#>{distance}" : $"役者間距離#<={distance}", false)]);
        var pokemon = BuildActor(b, actor, [new(6, Wild, false), new(6, City, false), new(6, Usually, false),
            new(6, actor, false), new(6, Alpha, true)]);
        var homePoint = BuildActor(b, HomePoint, [new(6, "ホームポイント", false)]);
        var actors = OffsetVector(b, [pokemon, homePoint]);
        var interruptTag = b.CreateString("State_usually");
        var interrupts = OffsetVector(b, [interruptTag.Value]);
        var name = b.CreateString(actor);
        var names = OffsetVector(b, [name.Value]);

        // Ground uses the shared city walk. Hover uses the shared floating random-move program.
        int[][] commands = (hover, home) switch
        {
            (false, false) => [[1855], [1020], [1551], [1850], [1551]],
            (false, true) => [[1855], [1352], [1551]],
            (true, false) => [[1152], [1855, 1850, 1005], [1152], [1551], [1850, 1002, 1005, 1856], [1152]],
            (true, true) => [[1855, 1850, 1005], [1352], [1551], [1850, 1002, 1005, 1856]],
        };
        var slots = new List<int>();
        foreach (var slot in commands)
        {
            var options = new List<int>();
            for (var i = 0; i < slot.Length; i++)
            {
                var command = slot[i];
                var target = command == 1352 ? b.CreateString(HomePoint).Value : 0;
                var probability = slot.Length == 1 ? 100 : slot.Length == 3 && i == 0 ? 50 : 25;
                b.StartTable(3);
                b.AddInt(0, probability, 0);
                b.AddInt(1, command, 0);
                b.AddOffset(2, target, 0);
                options.Add(b.EndTable());
            }
            slots.Add(OffsetVector(b, options));
        }
        b.StartTable(7);
        b.AddOffset(0, names, 0);
        for (var i = 0; i < slots.Count; i++) b.AddOffset(i + 1, slots[i], 0);
        var data = OffsetVector(b, [b.EndTable()]);
        b.StartTable(12);
        b.AddInt(0, 6, 0);
        b.AddOffset(2, global, 0);
        b.AddOffset(3, actors, 0);
        b.AddOffset(5, interrupts, 0);
        b.AddInt(10, id, 0);
        b.AddOffset(11, data, 0);
        var rows = OffsetVector(b, [b.EndTable()]);
        b.StartTable(1);
        b.AddOffset(0, rows, 0);
        b.Finish(b.EndTable());
        return b.SizedByteArray();
    }

    private static int BuildActor(FlatBufferBuilder b, string actor, Group[] groups)
    {
        var name = b.CreateString(actor);
        var tags = BuildGroups(b, groups);
        b.StartTable(4);
        b.AddOffset(0, name.Value, 0);
        b.AddOffset(1, tags, 0);
        return b.EndTable();
    }

    private static int OffsetVector(FlatBufferBuilder b, IReadOnlyList<int> offsets)
    {
        b.StartVector(4, offsets.Count, 4);
        for (var i = offsets.Count - 1; i >= 0; i--) b.AddOffset(offsets[i]);
        return b.EndVector().Value;
    }

    private static int Align(int length) => checked((length + 7) & ~7);

    private static byte[] ReplaceDatabases(byte[] source, IEnumerable<int> retained, List<byte[]> additions)
    {
        var databases = retained.ToArray();
        var count = checked(databases.Length + additions.Count);
        const int rootVtable = 4, rootTable = 12, rootField = 16, vector = 20, entries = 24;
        var sourceStart = Align(checked(entries + count * 4));
        var length = checked(sourceStart + Align(source.Length) + additions.Sum(db => Align(db.Length)));
        if (length > 32 * 1024 * 1024) throw Invalid();
        var result = new byte[length];
        void Int(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), value);
        Int(0, rootTable);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(rootVtable), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(rootVtable + 2), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(rootVtable + 4), 4);
        Int(rootTable, rootTable - rootVtable);
        Int(rootField, vector - rootField);
        Int(vector, count);

        // Keep unmodeled tables and internal offsets intact by relocating the original data as a unit.
        source.CopyTo(result, sourceStart);
        var entry = entries;
        foreach (var database in databases)
        {
            Int(entry, checked(sourceStart + database - entry));
            entry += 4;
        }
        var start = checked(sourceStart + Align(source.Length));
        foreach (var addition in additions)
        {
            addition.CopyTo(result, start);
            Int(entry, checked(start + BinaryPrimitives.ReadInt32LittleEndian(addition) - entry));
            entry += 4;
            start += Align(addition.Length);
        }
        return result;
    }
}
