// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using System.Text.RegularExpressions;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

/// <summary>Moves a retained model variant away from its former shared archive paths.</summary>
internal sealed partial class SwShRestorationLegacyModels(string oldStem, string newStem)
{
    private const int MaximumMemberBytes = 32 * 1024 * 1024;

    internal static SwShRestorationLegacyModels For(SwShPreparedRestoration prepared)
    {
        var definition = SwShRestorationDefinitions.Get(prepared.DefinitionId);
        var legacy = SwShRestorationModelResources.LegacyVariants(definition).Single();
        var current = SwShRestorationModelResources.Variants(definition)
            .Single(variant => variant.Identity == legacy.Identity && variant.Gender == legacy.Gender);
        return new(legacy.ResourceStem, current.ResourceStem);
    }

    internal bool IsDestination(string path) =>
        path.StartsWith($"romfs/bin/pokemon/{newStem}/", StringComparison.Ordinal)
        || IsArchive(path, newStem);

    internal SwShPreparedRestoration Move(SwShPreparedRestoration package, SwShPreparedRestoration baseline,
        SwShPreparedRestoration? movedBaseline = null)
    {
        var packed = package.Contributions.Where(item => item.Writer == SwShRestorationWriter.File && IsArchive(item.Path, oldStem))
            .ToDictionary(item => item.Path, item => ReadArchive(item.After), StringComparer.Ordinal);
        var originals = baseline.Contributions.ToDictionary(item => (item.Path, item.Key));
        var moved = movedBaseline?.Contributions.ToDictionary(item => (item.Path, item.Key));
        var candidates = MemberNames(package, packed);
        if (movedBaseline is not null)
        {
            var originalPacks = baseline.Contributions.Where(item => packed.ContainsKey(item.Path))
                .ToDictionary(item => item.Path, item => ReadArchive(item.After), StringComparer.Ordinal);
            candidates.UnionWith(MemberNames(baseline, originalPacks));
        }
        var namesByHash = candidates.GroupBy(SwShGfPackFile.HashMemberPath).ToDictionary(group => group.Key, group => group.ToArray());
        return package with { Contributions = package.Contributions.Select(MoveItem).ToArray() };

        SwShRestorationContribution MoveItem(SwShRestorationContribution item)
        {
            if (item.Writer == SwShRestorationWriter.ModelCatalog)
            {
                var row = ReadRow(item);
                if (Rename(row.Archive) == row.Archive) return item;
                RequireOwned(item);
                return item with { After = JsonSerializer.SerializeToUtf8Bytes(row with
                {
                    Archive = Rename(row.Archive),
                    Model = Rename(row.Model),
                    Configuration = Rename(row.Configuration),
                    Controllers = row.Controllers.ToDictionary(pair => pair.Key, pair => Rename(pair.Value), StringComparer.Ordinal),
                }) };
            }
            if (item.Writer != SwShRestorationWriter.File) return item;
            var path = Rename(item.Path[6..]);
            if (path == item.Path[6..]) return item;
            RequireOwned(item);
            if (moved is not null && originals.TryGetValue((item.Path, item.Key), out var original)
                && item.After.AsSpan().SequenceEqual(original.After))
                return moved[("romfs/" + path, item.Key)];

            var bytes = packed.TryGetValue(item.Path, out var pack) ? MoveArchive(item.Path, pack, namesByHash)
                : RewriteController(item.Path[6..], item.After);
            return item with { Path = "romfs/" + path, After = bytes };
        }
    }

    private HashSet<string> MemberNames(SwShPreparedRestoration package, IReadOnlyDictionary<string, SwShGfPackFile> packs)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { $"bin/pokemon/{oldStem}/{oldStem}.gfbgpokecfg" };
        foreach (var item in package.Contributions.Where(SwShRestorationModelOverrides.IsLoose))
            if (Rename(item.Path[6..]) != item.Path[6..]) names.Add(item.Path[6..]);
        foreach (var item in package.Contributions.Where(item => item.Writer == SwShRestorationWriter.ModelCatalog))
        {
            var row = ReadRow(item);
            if (!packs.TryGetValue("romfs/" + row.Archive, out var pack)) continue;
            names.Add(row.Model);
            names.Add(row.Configuration);
            var model = new ModelBuffer(ReadMember(pack, row.Model));
            var modelFolder = row.Model[..(row.Model.LastIndexOf('/') + 1)];
            foreach (var texture in ModelNames(model, 2)) names.Add(modelFolder + texture + ".bntx");
            foreach (var shader in ModelNames(model, 3).Concat(ModelNames(model, 5)))
            {
                names.Add(modelFolder + shader + ".bnsh_vsh");
                names.Add(modelFolder + shader + ".bnsh_fsh");
                names.Add($"bin/pokemon/{oldStem}/g_shader/{shader}.bnsh_vsh");
                names.Add($"bin/pokemon/{oldStem}/g_shader/{shader}.bnsh_fsh");
            }
            foreach (var path in row.Controllers.Values)
            {
                names.Add(path);
                var controller = new ModelBuffer(ReadMember(pack, path));
                foreach (var binding in AnimationBindings(controller))
                    names.Add(TrinityPreviewReader.Resolve(path, controller.Text(binding, 1)!));
            }
        }
        // Legacy packs also include converted donor clips without controller bindings.
        // The source fingerprints retain their names even if the donor drive is offline.
        foreach (var source in package.Sources.Where(source => source.Role == "donor" && source.RelativePath.EndsWith(".tranm", StringComparison.Ordinal)))
        {
            var name = Path.GetFileNameWithoutExtension(source.RelativePath);
            var match = DonorAnimationName().Match(name);
            if (match.Success) names.Add($"bin/pokemon/{oldStem}/anm/{oldStem}{name[match.Length..]}.gfbanm");
        }
        return names;
    }

    private byte[] MoveArchive(string path, SwShGfPackFile pack, IReadOnlyDictionary<ulong, string[]> namesByHash)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in pack.AbsoluteHashes)
        {
            var hash = entry.HashFnv1aPathFull;
            if (!namesByHash.TryGetValue(hash, out var names) || names.Length != 1)
                throw new InvalidDataException($"A retained model archive member could not be relocated safely: {path} ({hash:x16}).");
            var before = names[0];
            paths.Add(before, Rename(before));
            if (before.EndsWith(".gfbanmcfg", StringComparison.Ordinal))
                pack.ReplaceFileByHash(hash, RewriteController(before, pack.ReadFileByHash(hash, MaximumMemberBytes)));
        }
        return pack.WithRenamedMembers(paths).Write();
    }

    private byte[] RewriteController(string path, byte[] bytes)
    {
        if (!path.EndsWith(".gfbanmcfg", StringComparison.Ordinal)) return bytes;
        var source = new ModelBuffer(bytes);
        ModelTableBuilder? builder = null;
        foreach (var binding in AnimationBindings(source))
        {
            var reference = source.Text(binding, 1)!;
            var target = Rename(TrinityPreviewReader.Resolve(path, reference));
            if (TrinityPreviewReader.Resolve(Rename(path), reference) == target) continue;
            var relative = Path.GetRelativePath(Path.GetDirectoryName(Rename(path))!, target).Replace('\\', '/');
            (builder ??= new(bytes)).Text(binding, 1, relative);
        }
        return builder?.Finish(source.Root) ?? bytes;
    }

    private string Rename(string path)
    {
        if (IsArchive("romfs/" + path, oldStem))
        {
            var start = path.LastIndexOf('/') + 1;
            return path[..start] + newStem + path[(start + oldStem.Length)..];
        }
        var prefix = $"bin/pokemon/{oldStem}/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return path;
        var suffix = path[prefix.Length..];
        var startOfName = suffix.LastIndexOf('/') + 1;
        var name = suffix[startOfName..];
        // Texture and shader names are embedded in the model and stay unchanged.
        if (Path.GetExtension(name) is ".gfbmdl" or ".gfbpokecfg" or ".gfbgpokecfg" or ".gfbanmcfg" or ".gfbanm"
            && (name.StartsWith(oldStem + ".", StringComparison.Ordinal) || name.StartsWith(oldStem + "_", StringComparison.Ordinal)))
            suffix = suffix[..startOfName] + newStem + name[oldStem.Length..];
        return $"bin/pokemon/{newStem}/{suffix}";
    }

    private static bool IsArchive(string path, string stem) => path == $"romfs/bin/archive/pokemon/{stem}.gfpak"
        || path == $"romfs/bin/archive/pokemon/{stem}_rare.gfpak" || path == $"romfs/bin/archive/gpokemon/{stem}.gfpak";

    private static void RequireOwned(SwShRestorationContribution item)
    {
        if (item.Before is not null) throw new InvalidDataException($"Cannot relocate a retained model with an existing base resource: {item.Path}.");
    }

    private static SwShPokemonResourceRow ReadRow(SwShRestorationContribution item) =>
        JsonSerializer.Deserialize<SwShPokemonResourceRow>(item.After) ?? throw new InvalidDataException("Retained model routing is missing.");

    private static SwShGfPackFile ReadArchive(byte[] bytes) => SwShGfPackFile.ParseBoundedReadOnly(bytes, 4096, 64L * 1024 * 1024);

    private static byte[] ReadMember(SwShGfPackFile pack, string path) =>
        pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(path), MaximumMemberBytes);

    private static IEnumerable<int> AnimationBindings(ModelBuffer controller) => controller.Tables(controller.Table(controller.Root, 6), 0, 2048);

    private static IEnumerable<string> ModelNames(ModelBuffer model, int field)
    {
        var (start, count) = model.Vector(model.Root, field, 4, 256);
        for (var index = 0; index < count; index++) yield return model.StringAt(start + index * 4);
    }

    [GeneratedRegex("^pm[0-9]{4}_[0-9]{2}_[0-9]{2}(?=_)")]
    private static partial Regex DonorAnimationName();
}
