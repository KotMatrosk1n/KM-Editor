// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.SwSh.Models;

public sealed record SwShModelCatalogEntry(string Id, int Species, int Form, int Gender, string Name, string Category = "pokemon", bool Shiny = false);

public sealed class SwShModelPreviewService
{
    private sealed record AnimationSource(string Path, string? Archive);
    private sealed record Resource(SwShModelCatalogEntry Entry, string? Archive, AnimationSource[] Animations);
    private const string CatalogPath = "bin/pokemon/table/poke_resource_table.gfbpmcatalog";

    public IReadOnlyList<SwShModelCatalogEntry> Catalog(OpenedProject project)
    {
        var source = new SwShModelSource(project);
        var resources = Resources(project, source);
        IReadOnlyList<SwShGameTextLine> labels = [];
        var path = SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.Resolve(project.Paths), "monsname.dat")[6..];
        try { labels = SwShGameTextFile.Parse(source.Read(path), 2048).Lines; }
        catch (FileNotFoundException) { }
        source.Verify();
        return resources.Select(resource => resource.Entry is var entry && entry.Species > 0 && entry.Species < labels.Count
            ? entry with { Name = labels[entry.Species].Text } : resource.Entry).ToArray();
    }

    public ModelTextureResource[] Textures(OpenedProject project, string id) => ModelTextureResources.Capture(observe => Prepare(project, id, "rest", observe));

    public ModelTextureResource[] Assets(OpenedProject project, string id, bool vanilla = false)
    {
        var baseProject = project with { Paths = project.Paths with { OutputRootPath = null } };
        var source = new SwShModelSource(baseProject, 512L * 1024 * 1024, 16384);
        var resource = Resources(baseProject, source).SingleOrDefault(r => r.Entry.Id == id)
            ?? throw new InvalidDataException("Select a model from the vanilla catalog.");
        var roots = resource.Animations.Select(a => (a.Path, a.Archive)).Prepend((id, resource.Archive));
        var graph = ModelAssetGraph.Read(roots, source.Read);
        source.Verify();
        if (vanilla) return graph;
        var layered = new SwShModelSource(project, 512L * 1024 * 1024, 16384);
        var result = graph.Select(asset => asset with { Bytes = layered.Read(asset.Id, asset.Archive) }).ToArray();
        layered.Verify(); return result;
    }

    public PreviewScene Prepare(OpenedProject project, string id, string? animation = null, Action<string, byte[], string?>? observe = null, Func<string, byte[], byte[]>? transform = null)
    {
        var source = new SwShModelSource(project);
        var resource = Resources(project, source).FirstOrDefault(item => item.Entry.Id == id)
            ?? throw new InvalidDataException("Select a model from the current catalog.");
        var modelFolder = id[..(id.LastIndexOf('/') + 1)];
        byte[] Read(string dependency)
        {
            var path = dependency.StartsWith("bin/", StringComparison.Ordinal) ? dependency
                : TrinityPreviewReader.Resolve(modelFolder + "resource", "../tex/" + dependency);
            var bytes = source.Read(path, resource.Archive);
            observe?.Invoke(path, bytes, resource.Archive);
            return transform?.Invoke(path, bytes) ?? bytes;
        }
        var scene = ModelPreviewCache.Load("SwSh", id, null, Read, read => new SwShPreviewReader(read).Load(id));
        var warnings = scene.Rig.Warnings.ToList();
        var clips = new Dictionary<string, PreviewClipReference>(StringComparer.Ordinal);
        var clipArchives = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var animationSource in resource.Animations)
        {
            var configPath = animationSource.Path;
            ModelBuffer config;
            try { var bytes = source.Read(configPath, animationSource.Archive); config = new(transform?.Invoke(configPath, bytes) ?? bytes); }
            catch (FileNotFoundException) { continue; }
            var group = config.Table(config.Root, 6);
            if (group == 0) continue;
            foreach (var node in config.Tables(group, 0, 2048))
            {
                var name = config.Text(node, 0); var file = config.Text(node, 1);
                if (string.IsNullOrWhiteSpace(name) || file is null || !file.EndsWith(".gfbanm", StringComparison.Ordinal)) continue;
                var path = TrinityPreviewReader.Resolve(configPath, file);
                var clipId = Path.GetFileNameWithoutExtension(path);
                if (clips.TryGetValue(clipId, out var existing) && existing.Skeleton != path)
                    throw new InvalidDataException("Animation identifiers are ambiguous.");
                clips.TryAdd(clipId, new(clipId, path, null));
                clipArchives.TryAdd(clipId, animationSource.Archive);
                if (clips.Count > 2048) throw new InvalidDataException("Animation catalog exceeds the preview budget.");
            }
        }
        var available = clips.Values.OrderBy(clip => clip.Id, StringComparer.Ordinal).ToArray();
        var chosen = animation == "rest" ? null : animation is null
            ? available.FirstOrDefault(clip => clip.Id.EndsWith("_ba10_waitA01", StringComparison.Ordinal))
                ?? available.FirstOrDefault(clip => clip.Id.Contains("wait", StringComparison.OrdinalIgnoreCase)) ?? available.FirstOrDefault()
            : clips.GetValueOrDefault(animation) ?? throw new InvalidDataException("Select an animation associated with this model.");
        PreviewClip? clip = null;
        if (chosen?.Skeleton is { } skeletal)
        {
            try
            {
                var bytes = source.Read(skeletal, clipArchives[chosen.Id]);
                var data = new ModelBuffer(transform?.Invoke(skeletal, bytes) ?? bytes);
                clip = PreviewRigReader.Animation(data, chosen.Id, scene.Rig.Bones);
                clip = SwShPreviewAnimation.Read(data, clip, warnings);
            }
            catch (IOException) { warnings.Add("animationUnsupported"); }
        }
        source.Verify();
        return scene with { Rig = new(scene.Rig.Bones, available, clip, warnings.Distinct().ToArray()) };
    }

    private static Resource[] Resources(OpenedProject project, SwShModelSource source)
    {
        if (project.Paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
            throw new InvalidDataException("Select a Sword or Shield project.");
        var data = new ModelBuffer(source.Read(CatalogPath));
        var resources = new Dictionary<string, Resource>(StringComparer.Ordinal);
        foreach (var row in data.Tables(data.Root, 1, 8192))
        {
            var identity = data.Table(row, 0);
            if (identity == 0) throw new InvalidDataException("Model catalog identity is missing.");
            int Small(int field) => data.Field(identity, field) is var at && at != 0 ? data.U16(at) : 0;
            var species = Small(0); var model = data.Text(row, 1); var archive = data.Text(row, 3);
            if (species == 0 || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(archive)) continue;
            model = SwShModelSource.Canonical(model); archive = SwShModelSource.Canonical(archive);
            if (!model.EndsWith(".gfbmdl", StringComparison.Ordinal) || !archive.EndsWith(".gfpak", StringComparison.Ordinal))
                throw new InvalidDataException("Model catalog resource type is unsupported.");
            var genderAt = data.Field(identity, 2);
            var shinyAt = data.Field(identity, 3);
            var entry = new SwShModelCatalogEntry(model, species, Small(1), genderAt == 0 ? 0 : data.U8(genderAt), $"#{species}",
                Shiny: shinyAt != 0 && data.U8(shinyAt) != 0);
            var configs = data.Tables(row, 4, 32).Select(table => data.Text(table, 1)).OfType<string>()
                .Select(path => new AnimationSource(SwShModelSource.Canonical(path), archive)).ToArray();
            resources.TryAdd(model, new(entry, archive, configs));
        }
        foreach (var entry in project.FileGraph.Entries)
        {
            const string trainerModels = "romfs/bin/archive/chara/data/tr/mdl/";
            if (entry.RelativePath.StartsWith(trainerModels, StringComparison.Ordinal) && entry.RelativePath.EndsWith(".gfpak", StringComparison.Ordinal))
            {
                var folder = Path.GetFileNameWithoutExtension(entry.RelativePath);
                if (folder.Length < 11 || !System.Text.RegularExpressions.Regex.IsMatch(folder, @"^tr\d{4}_\d{2}_[a-zA-Z0-9_]+$")) continue;
                var model = $"bin/chara/data/tr/{folder}/mdl/{folder[..9]}.gfbmdl";
                var animationPrefix = $"romfs/bin/archive/chara/data/tr/anm/{folder}_";
                var configs = project.FileGraph.Entries.Where(file => file.RelativePath.StartsWith(animationPrefix, StringComparison.Ordinal)
                    && file.RelativePath.EndsWith(".gfpak", StringComparison.Ordinal)).Take(33).Select(file =>
                    new AnimationSource($"bin/chara/data/tr/{folder}/anm/{file.RelativePath[animationPrefix.Length..^6]}.gfbanmcfg", file.RelativePath[6..])).ToArray();
                if (configs.Length > 32) throw new InvalidDataException("Trainer animation catalog exceeds the preview budget.");
                resources.TryAdd(model, new(new(model, 0, 0, 0, folder.Replace('_', ' '), "trainers"), entry.RelativePath[6..], configs));
                continue;
            }
            if (!entry.RelativePath.StartsWith("romfs/bin/", StringComparison.Ordinal) || !entry.RelativePath.EndsWith(".gfbmdl", StringComparison.Ordinal)) continue;
            var path = SwShModelSource.Canonical(entry.RelativePath[6..]);
            var category = path.StartsWith("bin/chara/", StringComparison.Ordinal) ? "trainers" : path.StartsWith("bin/field/", StringComparison.Ordinal) ? "environment" : "other";
            resources.TryAdd(path, new(new(path, 0, 0, 0, Path.GetFileNameWithoutExtension(path), category), null, []));
            if (resources.Count > 8192) throw new InvalidDataException("Model catalog exceeds the preview budget.");
        }
        return resources.Values.OrderBy(resource => resource.Entry.Species == 0 ? 1 : 0).ThenBy(resource => resource.Entry.Species)
            .ThenBy(resource => resource.Entry.Form).ThenBy(resource => resource.Entry.Gender).ThenBy(resource => resource.Entry.Id, StringComparer.Ordinal).ToArray();
    }
}
