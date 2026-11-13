// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using KM.Formats.PLA;

namespace KM.PLA;

internal sealed class PlaWeatherCatalog
{
    internal const string MemberPath = "bin/field/weather/weather_table.bin";
    private static readonly int[] Widths = [8, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4];
    private static readonly PlaFlatBuffer.ObjectLayout Layout = new(Widths, [], new Dictionary<int, PlaFlatBuffer.ObjectLayout>(), new Dictionary<int, PlaFlatBuffer.ObjectLayout>());
    private static readonly string[] Keys = ["weatherSunny", "weatherCloudy", "weatherRain", "weatherSnow", "weatherDrought", "weatherFog", "weatherRainstorm", "weatherSnowstorm"];
    private readonly PlaCatalog catalog;
    private readonly PlaFlatBuffer original;
    private readonly PlaFlatBuffer current;
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);

    internal PlaWeatherCatalog(PlaCatalog catalog)
    {
        this.catalog = catalog;
        original = new(catalog.Wild.BaseMember(MemberPath));
        current = new(catalog.Wild.ReadMember(MemberPath));
        var identities = Enumerable.Range(0, 7).ToDictionary(index => PlaFieldArchive.HashPath("ha_area0" + index), index => "ha_area0" + index);
        var hashes = new HashSet<ulong>();
        var rows = Rows(original);
        for (var i = 0; i < rows.Length; i++)
        {
            var hash = original.Hash(original.TableAtReference(rows[i]), 0);
            if (!hashes.Add(hash)) throw new InvalidDataException("PLA weather area identities are ambiguous.");
            if (identities.TryGetValue(hash, out var area)) indices.Add(area, i);
        }
        if (indices.Count != identities.Count) throw new InvalidDataException("A supported PLA weather area is missing.");
        Validate(current);
    }

    private static int[] Rows(PlaFlatBuffer b)
    {
        b.RequireKnownFields(b.Root, [4]);
        var rows = b.VectorReferences(b.Root, 0, 128);
        foreach (var reference in rows) b.RequireKnownFields(b.TableAtReference(reference), Widths);
        return rows;
    }
    internal PlaField[] Fields() => Keys.Select(key => new PlaField(key, key, Minimum: 0, Maximum: 10000)).ToArray();
    private int Index(string id) => indices.TryGetValue(id, out var index) ? index : throw new InvalidDataException("The PLA weather area is unsupported.");
    internal string PathFor(string id) { _ = Index(id); return PlaWildCatalog.ArchivePath; }
    private PlaRecord Record(string id, PlaFlatBuffer b)
    {
        var row = b.TableAtReference(Rows(b)[Index(id)]);
        return new(id, id, Keys.Select((key, i) => (key, value: b.Scalar(row, i + 3, 4, true).ToString(CultureInfo.InvariantCulture)))
            .ToDictionary(pair => pair.key, pair => pair.value, StringComparer.Ordinal)) { Metadata = new Dictionary<string, string> { ["area"] = id } };
    }
    private PlaFlatBuffer Buffer(byte[]? archive) => archive is null ? current : new(new PlaFieldArchive(archive).Read(MemberPath, catalog.Scope.Compression));
    internal PlaRecord Record(string id, byte[]? archive) => Record(id, Buffer(archive));
    internal IReadOnlyList<PlaRecord> Records(IReadOnlyDictionary<string, byte[]> files, string? selectedId = null)
    {
        var b = Buffer(files.GetValueOrDefault(PlaWildCatalog.ArchivePath));
        return indices.Keys.Where(id => selectedId is null || id == selectedId).Select(id => Record(id, b)).ToArray();
    }
    private void Validate(PlaFlatBuffer b)
    {
        string Signature(PlaFlatBuffer source) => JsonSerializer.Serialize(Rows(source).Select((reference, index) =>
        {
            var row = source.TableAtReference(reference);
            return new { Hash = source.Hash(row, 0), Values = Enumerable.Range(1, indices.ContainsValue(index) ? 2 : 10).Select(field => source.Scalar(row, field, 4)).ToArray() };
        }));
        if (Signature(original) != Signature(b)) throw new InvalidDataException("PLA weather identities, durations, and unsupported areas must be preserved.");
        foreach (var id in indices.Keys)
        {
            var values = Record(id, b).Values.Values.Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(value => value is < 0 or > 10000) || values.All(value => value == 0))
                throw new InvalidDataException("PLA weather weights must be between 0 and 10000 and retain a positive selection.");
        }
    }
    internal byte[] Edit(IReadOnlyDictionary<string, Dictionary<string, string>> edits)
    {
        var b = new PlaFlatBuffer(current.ToArray());
        foreach (var (id, values) in edits)
        {
            var record = Record(id, b);
            var changes = new Dictionary<int, long>();
            foreach (var (key, value) in values)
            {
                var field = Array.IndexOf(Keys, key);
                if (field < 0) throw new InvalidDataException("The PLA weather field is unsupported.");
                if (record.Values[key] != value) changes.Add(field + 3, long.Parse(value, CultureInfo.InvariantCulture));
            }
            if (changes.Count > 0)
            {
                var reference = Rows(b)[Index(id)];
                b.EditObjectTree(reference, Layout, new Dictionary<int, IReadOnlyDictionary<int, long>> { [reference] = changes });
            }
        }
        Validate(b);
        return b.ToArray();
    }
}
