// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public readonly record struct SwShPokemonResourceIdentity(int Species, int Form, int Gender, bool Shiny);
public sealed record SwShPokemonResourceRow(SwShPokemonResourceIdentity Identity, string Model,
    string Configuration, string Archive, IReadOnlyDictionary<string, string> Controllers);

public sealed class SwShPokemonResourceCatalog
{
    private readonly byte[] bytes;
    private readonly ModelBuffer buffer;
    private readonly Dictionary<SwShPokemonResourceIdentity, int> rows;
    public SwShPokemonResourceCatalog(byte[] bytes)
    {
        this.bytes = bytes.ToArray(); buffer = new(this.bytes);
        rows = new();
        foreach (var row in buffer.Tables(buffer.Root, 1, 20000))
        {
            var identity = buffer.Table(row, 0);
            int Small(int i, bool wide) => buffer.Field(identity, i) is var at && at != 0 ? wide ? buffer.U16(at) : buffer.U8(at) : 0;
            var key = new SwShPokemonResourceIdentity(Small(0, true), Small(1, true), Small(2, false), Small(3, false) != 0);
            if (!rows.TryAdd(key, row)) throw new InvalidDataException("Pokemon resource identity is duplicated.");
        }
    }
    public IReadOnlyCollection<SwShPokemonResourceIdentity> Identities => rows.Keys;
    public SwShPokemonResourceRow Read(SwShPokemonResourceIdentity identity)
    {
        if (!rows.TryGetValue(identity, out var row)) throw new InvalidDataException("Pokemon resource identity is missing.");
        string Text(int table, int field) => buffer.Text(table, field) ?? throw new InvalidDataException("Pokemon resource path is missing.");
        return new(identity, Text(row, 1), Text(row, 2), Text(row, 3),
            buffer.Tables(row, 4, 32).ToDictionary(t => Text(t, 0), t => Text(t, 1), StringComparer.Ordinal));
    }
    public byte[] Compose(IReadOnlyList<SwShPokemonResourceRow> additions,
        IReadOnlyCollection<SwShPokemonResourceIdentity>? removals = null)
    {
        removals ??= [];
        if (additions.Count == 0 && removals.Count == 0) return bytes.ToArray();
        if (additions.Select(row => row.Identity).Distinct().Count() != additions.Count
            || removals.Distinct().Count() != removals.Count || removals.Any(key => !rows.ContainsKey(key)))
            throw new InvalidDataException("Pokemon resource changes are ambiguous.");
        var keys = rows.Keys.Except(removals).Concat(additions.Select(row => row.Identity)).ToArray();
        if (keys.Distinct().Count() != keys.Length) throw new InvalidDataException("Pokemon resource already exists.");
        var ordered = keys.OrderBy(k => k.Species).ThenBy(k => k.Form).ThenBy(k => k.Gender).ThenBy(k => k.Shiny).ToArray();
        // Rebuild the supported schema instead of retaining dead vector copies
        // on every toggle. Reject extensions whose preservation is not defined.
        Shape(buffer.Root, 2);
        var header = buffer.Table(buffer.Root, 0); Shape(header, 2);
        foreach (var existing in rows.Values)
        {
            Shape(existing, 5); Shape(buffer.Table(existing, 0), 4);
            foreach (var binding in buffer.Tables(existing, 4, 32)) Shape(binding, 2);
        }
        var writer = new ModelTableBuilder(); var root = writer.Table(2);
        var headerFields = Enumerable.Range(0, 2).Where(i => buffer.Field(header, i) != 0).ToDictionary(i => i,
            i => buffer.Slice(buffer.Field(header, i), 4).ToArray());
        writer.Reference(root, 0, writer.Table(2, headerFields, absent: Enumerable.Range(0, 2).Except(headerFields.Keys).ToHashSet()));
        var slots = writer.Vector(root, 1, ordered.Length);
        var added = additions.ToDictionary(row => row.Identity);
        // Emit strings after their referencing tables so repeated native paths
        // and controller names share storage without backward object offsets.
        var textReferences = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        void Text(int table, int field, string value)
        {
            if (!textReferences.TryGetValue(value, out var references)) textReferences.Add(value, references = []);
            references.Add(writer.Field(table, field));
        }
        for (var i = 0; i < ordered.Length; i++)
        {
            var identity = ordered[i];
            var row = added.TryGetValue(identity, out var replacement) ? replacement : Read(identity);
            if (identity.Species is < 1 or > ushort.MaxValue || identity.Form is < 0 or > ushort.MaxValue || identity.Gender is < 0 or > 255)
                throw new InvalidDataException("Pokemon resource identity is out of range.");
            var table = writer.Table(5); writer.Pointer(slots[i], table);
            var id = writer.Table(4, new Dictionary<int, byte[]>
            {
                [0] = ModelTableBuilder.Integer(identity.Species), [1] = ModelTableBuilder.Integer(identity.Form),
                [2] = ModelTableBuilder.Integer(identity.Gender), [3] = ModelTableBuilder.Integer(identity.Shiny ? 1 : 0),
            });
            writer.Reference(table, 0, id); Text(table, 1, row.Model); Text(table, 2, row.Configuration); Text(table, 3, row.Archive);
            var controllers = writer.Vector(table, 4, row.Controllers.Count);
            var n = 0;
            foreach (var (name, path) in row.Controllers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var binding = writer.Table(2); writer.Pointer(controllers[n++], binding); Text(binding, 0, name); Text(binding, 1, path);
            }
        }
        foreach (var (value, references) in textReferences.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var text = writer.String(value);
            foreach (var reference in references) writer.Pointer(reference, text);
        }
        var result = writer.Finish(root);
        _ = new SwShPokemonResourceCatalog(result);
        return result;

        void Shape(int table, int maximumFields)
        {
            if (table == 0) throw new InvalidDataException("Pokemon resource metadata is missing.");
            var vtable = checked(table - buffer.I32(table));
            if (buffer.U16(vtable) > 4 + maximumFields * 2) throw new InvalidDataException("Pokemon resource metadata has unsupported extension fields.");
        }
    }
}
