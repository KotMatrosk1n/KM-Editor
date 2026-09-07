// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.ZA.Data;
using KM.ZA.GameModules;
using KM.ZA.Workflows;

namespace KM.ZA.Models;

public sealed record ZaModelCatalogEntry(string Id, int Species, int Form, int Gender, string Name, string Category = "pokemon", bool Shiny = false);

public sealed class ZaModelPreviewService
{
    private readonly ZaWorkflowFileSource source = new(bypassReusableBaseCache: true,
        maximumReadBytes: 32 * 1024 * 1024, maximumReadCount: 8192, maximumAggregateReadBytes: 128L * 1024 * 1024);

    public IReadOnlyList<ZaModelCatalogEntry> Catalog(OpenedProject project)
    {
        if (project.Paths.SelectedGame != ProjectGame.ZA) throw new InvalidDataException("Select a Legends Z-A project.");
        using var scope = ZaWorkflowFileSource.BeginIndependentFreshReadScope(project.Paths);
        var catalog = new ZaPokemonResourceCatalogService(source).Load(project);
        var labels = ZaTextLabelLookup.Load(project, source, [], project.Paths);
        var reader = new TrinityPreviewReader(path => source.Read(project, path).Bytes, topOriginMaterialUv: true);
        var pokemon = new List<ZaModelCatalogEntry>();
        foreach (var entry in catalog.Entries.Where(entry => entry.Species > 0 && !string.IsNullOrWhiteSpace(entry.ModelPath)))
        {
            var model = new ZaModelCatalogEntry(ModelPath(entry.ModelPath!), entry.Species, entry.Form, entry.Gender, labels.Pokemon(entry.Species));
            pokemon.Add(model);
            if (entry.MaterialTablePath is not { Length: > 0 } relative) continue;
            var tablePath = TrinityPreviewReader.Resolve("ik_pokemon/data/catalog", relative);
            if (source.Exists(project, tablePath) && PreviewMaterialVariant.Shiny(new(source.Read(project, tablePath).Bytes), tablePath) is not null)
                pokemon.Add(model with { Id = model.Id + "#shiny", Shiny = true });
        }
        return pokemon
            .Concat(ZaModelDiscovery.Discover(project).Select(entry => entry.Category == "trainers" && !reader.HasCharacterSurface(entry.Id)
                ? entry with { Category = "other" } : entry)).DistinctBy(entry => entry.Id)
            .OrderBy(entry => entry.Species == 0 ? 1 : 0).ThenBy(entry => entry.Species).ThenBy(entry => entry.Form).ThenBy(entry => entry.Gender).ToArray();
    }

    public ModelTextureResource[] Textures(OpenedProject project, string id) => ModelTextureResources.Capture(observe => Prepare(project, id, "rest", observe));

    public ModelTextureResource[] Assets(OpenedProject project, string id, bool vanilla = false)
    {
        var baseProject = project with { Paths = project.Paths with { OutputRootPath = null } };
        var seeds = new HashSet<string>(StringComparer.Ordinal);
        Prepare(baseProject, id, "rest", (path, _, _) => { if (path != ZaDataPaths.PokemonResourceCatalog) seeds.Add(path); });
        var graphSource = new ZaWorkflowFileSource(bypassReusableBaseCache: true, maximumReadBytes: 32 * 1024 * 1024,
            maximumReadCount: 16384, maximumAggregateReadBytes: 512L * 1024 * 1024);
        ModelTextureResource[] graph;
        using (ZaWorkflowFileSource.BeginIndependentFreshReadScope(baseProject.Paths))
            graph = ModelAssetGraph.Read(seeds.Select(path => (path, (string?)null)), (path, _) => graphSource.ReadBase(baseProject, path).Bytes);
        if (vanilla) return graph;
        using var currentScope = ZaWorkflowFileSource.BeginIndependentFreshReadScope(project.Paths);
        return graph.Select(asset => asset with { Bytes = graphSource.Read(project, asset.Id).Bytes }).ToArray();
    }

    public PreviewScene Prepare(OpenedProject project, string id, string? animation = null, Action<string, byte[], string?>? observe = null, Func<string, byte[], byte[]>? transform = null)
    {
        if (project.Paths.SelectedGame != ProjectGame.ZA) throw new InvalidDataException("Select a Legends Z-A project.");
        var shiny = id.EndsWith("#shiny", StringComparison.Ordinal);
        if (shiny) id = id[..^6];
        using var scope = ZaWorkflowFileSource.BeginIndependentFreshReadScope(project.Paths);
        var hashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[] Read(string path)
        {
            var bytes = source.Read(project, path).Bytes;
            var hash = SHA256.HashData(bytes);
            if (hashes.TryGetValue(path, out var prior) && !hash.AsSpan().SequenceEqual(prior))
                throw new InvalidDataException("Model sources changed while loading. Reload the model.");
            hashes[path] = hash;
            observe?.Invoke(path, bytes, null);
            return transform?.Invoke(path, bytes) ?? bytes;
        }
        var catalog = ZaPokemonResourceCatalogParser.Read(Read(ZaDataPaths.PokemonResourceCatalog));
        var entry = catalog.Entries.FirstOrDefault(entry => entry.Species > 0 && entry.ModelPath is not null && ModelPath(entry.ModelPath) == id);
        if (entry is null && (shiny || !ZaModelDiscovery.Discover(project).Any(model => model.Id == id)))
            throw new InvalidDataException("Select a model from the current catalog.");
        PreviewMaterialVariant? variant = null;
        if (shiny)
        {
            var tablePath = TrinityPreviewReader.Resolve("ik_pokemon/data/catalog", entry!.MaterialTablePath
                ?? throw new InvalidDataException("Shiny materials are unavailable."));
            variant = PreviewMaterialVariant.Shiny(new(Read(tablePath)), tablePath)
                ?? throw new InvalidDataException("Shiny materials are unavailable.");
        }
        var scene = new TrinityPreviewReader(Read, topOriginMaterialUv: true).Load(id, variant);
        var warnings = scene.Rig.Warnings.ToList();
        var clips = new Dictionary<string, PreviewClipReference>(StringComparer.Ordinal);
        // Resource groups are resolved independently from the Z-A catalog. The base
        // group exists even when the optional animation-name catalog is absent.
        var groups = new HashSet<string>(StringComparer.Ordinal);
        if (entry?.DerivedBaseAnimationPath is { } baseGroup) groups.Add(baseGroup);
        var namePaths = entry is null ? ZaModelDiscovery.AnimationCatalogs(project.Paths.BaseRomFsPath!, id) : entry.Animations.Select(item => item.Path).OfType<string>()
            .Select(path => TrinityPreviewReader.Resolve("ik_pokemon/data/catalog", path)).ToArray();
        foreach (var namesPath in namePaths)
        {
            if (!source.Exists(project, namesPath)) continue;
            var names = new ModelBuffer(Read(namesPath));
            foreach (var name in names.Tables(names.Root, 0, 256))
                if (names.Text(name, 1) is { } relative && relative.EndsWith(".tracr", StringComparison.Ordinal))
                    groups.Add(TrinityPreviewReader.Resolve(namesPath, relative));
        }
        foreach (var group in groups)
        {
            if (!source.Exists(project, group)) continue;
            var data = new ModelBuffer(Read(group));
            foreach (var track in data.Tables(data.Table(data.Root, 0), 0, 2048))
            {
                var name = data.Text(track, 0);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var resources = data.Table(track, 3);
                if (resources == 0) continue;
                string? Dependency(int field)
                {
                    var table = data.Table(resources, field);
                    return table != 0 && data.Text(table, 0) is { } relative ? TrinityPreviewReader.Resolve(group, relative) : null;
                }
                if (Dependency(0) is not { } skeletal) continue;
                clips.TryAdd(name, new(name, skeletal, Dependency(1)));
                if (clips.Count > 2048) throw new InvalidDataException("Animation catalog exceeds the preview budget.");
            }
        }
        var available = clips.Values.OrderBy(clip => clip.Id, StringComparer.Ordinal).ToArray();
        var chosen = animation == "rest" ? null : animation is null
            ? available.FirstOrDefault(clip => clip.Id == "00000_defaultwait01_loop") ?? available.FirstOrDefault()
            : clips.GetValueOrDefault(animation) ?? throw new InvalidDataException("Select an animation associated with this model.");
        PreviewClip? clip = null;
        if (chosen?.Skeleton is { } skeleton)
        {
            try { clip = PreviewRigReader.Animation(new(Read(skeleton)), chosen.Id, scene.Rig.Bones); }
            catch (IOException) { warnings.Add("animationUnsupported"); }
        }
        if (clip is not null && chosen?.Material is { } material)
        {
            try { clip = PreviewMaterialAnimation.Read(new(Read(material)), clip, warnings, highlightAnimation: true); }
            catch (IOException) { warnings.Add("materialAnimationUnsupported"); }
        }
        foreach (var (path, hash) in hashes)
            if (!SHA256.HashData(source.Read(project, path).Bytes).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Model sources changed while loading. Reload the model.");
        return scene with { Rig = new(scene.Rig.Bones, available, clip, warnings.Distinct().ToArray()) };
    }

    private static string ModelPath(string relative)
    {
        var path = TrinityPreviewReader.Resolve("ik_pokemon/data/catalog", relative);
        if (!path.StartsWith("ik_pokemon/data/", StringComparison.Ordinal) || !path.EndsWith(".trmdl", StringComparison.Ordinal))
            throw new InvalidDataException("Model catalog path is unsupported.");
        return path;
    }
}
