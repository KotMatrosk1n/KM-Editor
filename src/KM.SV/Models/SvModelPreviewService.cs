// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Models;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Security.Cryptography;

namespace KM.SV.Models;

public sealed record SvModelCatalogEntry(string Id, int Species, int Form, int Gender, string Name, string Category = "pokemon");
public sealed class SvModelPreviewService
{
    private const string CatalogPath = "pokemon/catalog/catalog/poke_resource_table.trpmcatalog";
    private readonly SvWorkflowFileSource source = new(bypassReusableBaseCache: true,
        maximumReadBytes: 32 * 1024 * 1024, maximumReadCount: 256, maximumAggregateReadBytes: 128L * 1024 * 1024);

    public IReadOnlyList<SvModelCatalogEntry> Catalog(OpenedProject project)
    {
        using var scope = SvWorkflowFileSource.BeginFreshReadScope(project.Paths);
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
        if (project.Paths.BaseRomFsPath is { } root)
            foreach (var model in SvModelDiscovery.Discover(root)) if (seen.Add(model.Id)) results.Add(model);
        return results.OrderBy(x => x.Species == 0 ? 1 : 0).ThenBy(x => x.Species).ThenBy(x => x.Form).ThenBy(x => x.Gender).ToArray();
    }

    public PreviewScene Prepare(OpenedProject project, string id, string? animation = null)
    {
        using var scope = SvWorkflowFileSource.BeginIndependentFreshReadScope(project.Paths);
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
        var warnings = scene.Rig.Warnings.ToList();
        PreviewClipReference[] clips;
        try { clips = Clips(project, id, Read); }
        catch (IOException) { clips = []; warnings.Add("animationUnsupported"); }
        var chosen = animation == "rest" ? null : animation is null
            ? clips.FirstOrDefault(c => c.Id == "00000_defaultwait01_loop") ?? clips.FirstOrDefault()
            : clips.FirstOrDefault(c => c.Id == animation) ?? throw new InvalidDataException("Select an animation associated with this model.");
        PreviewClip? clip = null;
        if (chosen?.Skeleton is { } skeletal && source.Exists(project, skeletal))
        {
            try { clip = PreviewRigReader.Animation(new(Read(skeletal)), chosen.Id, scene.Rig.Bones); }
            catch (IOException) { warnings.Add("animationUnsupported"); }
        }
        else if (chosen is not null) warnings.Add("animationUnsupported");
        if (clip is not null && chosen?.Material is { } material && source.Exists(project, material))
        {
            try { clip = PreviewMaterialAnimation.Read(new(Read(material)), clip, warnings); }
            catch (IOException) { warnings.Add("materialAnimationUnsupported"); }
        }
        else if (clip is not null && chosen?.Material is not null) warnings.Add("materialAnimationUnsupported");
        scene = scene with { Rig = new(scene.Rig.Bones, clips, clip, warnings.Distinct().ToArray()) };
        foreach (var (path, hash) in hashes)
            if (!SHA256.HashData(source.Read(project, path).Bytes).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Model sources changed while loading. Reload the model.");
        return scene;
    }

    private PreviewClipReference[] Clips(OpenedProject project, string id, Func<string, byte[]> read)
    {
        var catalog = new ModelBuffer(read(CatalogPath));
        var entry = catalog.Tables(catalog.Root, 1, 8192).FirstOrDefault(x => catalog.Text(x, 1) is { } path
            && TrinityPreviewReader.Resolve("pokemon/data/catalog", path) == id);
        var clips = new Dictionary<string, PreviewClipReference>(StringComparer.Ordinal);
        var paths = entry == 0 ? new[] { id[..^6] + ".tracn" } : catalog.Tables(entry, 4, 32)
            .Select(animation => catalog.Text(animation, 1)).OfType<string>()
            .Select(relative => TrinityPreviewReader.Resolve("pokemon/data/catalog", relative)).ToArray();
        foreach (var catalogPath in paths)
        {
            var path = catalogPath;
            if (!source.Exists(project, path) && path.EndsWith(".tracn", StringComparison.Ordinal))
                path = path[..^6] + "_base.tracn";
            if (!source.Exists(project, path)) continue;
            var names = new ModelBuffer(read(path));
            foreach (var name in names.Tables(names.Root, 0, 256))
            {
                var resource = names.Text(name, 1);
                if (resource is null || !resource.EndsWith(".tracr", StringComparison.Ordinal)) continue;
                var resourcePath = TrinityPreviewReader.Resolve(path, resource);
                if (!source.Exists(project, resourcePath)) continue;
                var resources = new ModelBuffer(read(resourcePath));
                foreach (var track in resources.Tables(resources.Table(resources.Root, 0), 0, 2048))
                {
                    var nameId = resources.Text(track, 0);
                    if (string.IsNullOrWhiteSpace(nameId)) continue;
                    var table = resources.Table(track, 3);
                    string? Dependency(int field)
                    {
                        var value = resources.Table(table, field);
                        return value != 0 && resources.Text(value, 0) is { } file ? TrinityPreviewReader.Resolve(resourcePath, file) : null;
                    }
                    var skeleton = Dependency(0); var material = Dependency(1);
                    if (skeleton is null) continue;
                    clips.TryAdd(nameId, new(nameId, skeleton, material));
                    if (clips.Count > 2048) throw new InvalidDataException("Animation catalog exceeds the preview budget.");
                }
            }
        }
        return clips.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }
}
