// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Models;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Security.Cryptography;

namespace KM.SV.Models;

public sealed record SvModelCatalogEntry(string Id, int Species, int Form, int Gender, string Name);
public sealed class SvModelPreviewService
{
    private const string CatalogPath = "pokemon/catalog/catalog/poke_resource_table.trpmcatalog";
    private readonly SvWorkflowFileSource source = new(bypassReusableBaseCache: true,
        maximumReadBytes: 32 * 1024 * 1024, maximumReadCount: 256, maximumAggregateReadBytes: 128L * 1024 * 1024);

    public IReadOnlyList<SvModelCatalogEntry> Catalog(OpenedProject project)
    {
        var data = new ModelBuffer(source.Read(project, CatalogPath).Bytes);
        var labels = SvTextLabelLookup.LoadPokemonNames(project, source, [], project.Paths);
        var results = new List<SvModelCatalogEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in data.Tables(data.Root, 1, 8192))
        {
            var identity = data.Table(entry, 0);
            if (identity == 0) throw new InvalidDataException("Model catalog identity is missing.");
            int Small(int index) => data.Field(identity, index) is var at && at != 0 ? data.U16(at) : 0;
            var species = Small(0); var form = Small(1);
            var genderAt = data.Field(identity, 2);
            var gender = genderAt == 0 ? 0 : data.U8(genderAt);
            var relative = data.Text(entry, 1);
            if (string.IsNullOrWhiteSpace(relative)) continue;
            var path = TrinityPreviewReader.Resolve("pokemon/data/catalog", relative);
            if (!path.StartsWith("pokemon/data/", StringComparison.Ordinal) ||
                !path.EndsWith(".trmdl", StringComparison.Ordinal) ||
                TrinityPreviewReader.Resolve("catalog", path) != path)
                throw new InvalidDataException("Model catalog path is unsupported.");
            if (species != 0 && seen.Add(path)) results.Add(new(path, species, form, gender, labels.Pokemon(species)));
        }
        return results.OrderBy(x => x.Species).ThenBy(x => x.Form).ThenBy(x => x.Gender).ToArray();
    }

    public PreviewScene Prepare(OpenedProject project, string id)
    {
        if (!Catalog(project).Any(x => x.Id == id)) throw new InvalidDataException("Select a model from the current catalog.");
        var hashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[] Read(string path)
        {
            var bytes = source.Read(project, path).Bytes;
            var hash = SHA256.HashData(bytes);
            if (hashes.TryGetValue(path, out var previous) && !hash.AsSpan().SequenceEqual(previous))
                throw new InvalidDataException("Model sources changed while loading. Reload the model.");
            hashes[path] = hash;
            return bytes;
        }
        var scene = new TrinityPreviewReader(Read).Load(id);
        foreach (var (path, hash) in hashes)
            if (!SHA256.HashData(source.Read(project, path).Bytes).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Model sources changed while loading. Reload the model.");
        return scene;
    }
}
