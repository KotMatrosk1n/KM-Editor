// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;
using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Text.Json;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationIconUpdate
{
    internal static SwShPreparedRestoration Rebase(SwShPreparedRestoration previous, SwShPreparedRestoration next,
        SwShPreparedRestoration customized, out SwShRestorationContribution[] changed)
    {
        if (previous.DefinitionId != next.DefinitionId || previous.Game != next.Game || previous.DefinitionVersion != next.DefinitionVersion
            || previous.ConverterVersion != next.ConverterVersion || customized.DefinitionId != next.DefinitionId
            || customized.Game != next.Game || customized.DefinitionVersion != next.DefinitionVersion || customized.ConverterVersion != next.ConverterVersion)
            throw new InvalidDataException("Restoration updates require matching definitions and targets.");
        var identities = SwShRestorationDefinitions.Get(next.DefinitionId).Identities;
        var icons = identities.SelectMany(i => SwShRestorationComposer.IconGenders(i.Species).Select(gender =>
            SwShRestorationComposer.IconPath(i.Species, i.Form, gender))).ToHashSet(StringComparer.Ordinal);
        var iconRows = identities.SelectMany(i => SwShRestorationComposer.IconGenders(i.Species).Select(gender =>
        {
            var row = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(row, checked((ushort)SwShRestorationSpecies.IconSpecies(i.Species)));
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(2), checked((ushort)i.Form));
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4), checked((ushort)gender));
            return (Key: SwShRestorationComposer.IconKey(i.Species, i.Form, gender), Row: row);
        })).ToDictionary(item => item.Key, item => item.Row, StringComparer.Ordinal);
        var old = previous.Contributions.ToDictionary(c => (c.Path, c.Key));
        var replacements = next.Contributions.ToDictionary(c => (c.Path, c.Key));
        if (old.Keys.Any(key => !replacements.ContainsKey(key))) throw new InvalidDataException("Restoration update removes resource ownership.");
        var models = SwShRestorationModelResources.Variants(next).Select(v =>
            $"romfs/bin/archive/pokemon/{v.ResourceStem}").ToArray();
        bool Archive(string path) => models.Any(stem => path == stem + ".gfpak" || path == stem + "_rare.gfpak");
        bool Giant(string path) => models.Any(stem => path == stem.Replace("/pokemon/", "/gpokemon/", StringComparison.Ordinal) + ".gfpak");
        bool Model(SwShRestorationContribution item) => item.Writer == SwShRestorationWriter.ModelCatalog
            || item.Writer == SwShRestorationWriter.File && (Archive(item.Path) || Giant(item.Path)
                || item.Path.StartsWith("romfs/bin/pokemon/pm", StringComparison.Ordinal));
        var custom = customized.Contributions.ToDictionary(c => (c.Path, c.Key));
        // A palette split is one model graph update. Keep the complete old graph
        // when it contains edits, including its original shiny routing.
        var keepModels = customized.Contributions.Where(Model).Any(item =>
            !old.TryGetValue((item.Path, item.Key), out var original) || !Equal(original.After, item.After))
            || previous.Contributions.Where(Model).Any(item => !custom.ContainsKey((item.Path, item.Key)));
        var updates = new List<SwShRestorationContribution>();
        foreach (var item in next.Contributions)
        {
            if (!old.TryGetValue((item.Path, item.Key), out var original))
            {
                if (item.Before is not null) throw new InvalidDataException("Restoration update changes resource ownership.");
                if (item.Writer == SwShRestorationWriter.File && item.Key.Length == 0 && icons.Contains(item.Path))
                    ValidateIcon(item.After);
                else if (item.Writer == SwShRestorationWriter.Icons && item.Path == SwShRestorationComposer.IconsPath
                    && iconRows.TryGetValue(item.Key, out var row) && row.AsSpan().SequenceEqual(item.After)) { }
                else if (item.Writer == SwShRestorationWriter.File && item.Key.Length == 0
                    && models.Any(stem => item.Path == stem + "_rare.gfpak")) ValidateModelArchive(item);
                else
                    throw new InvalidDataException("Restoration update changes resource ownership.");
                continue;
            }
            if (original.Writer != item.Writer || !Equal(original.Before, item.Before))
                throw new InvalidDataException("Restoration update changes resource ownership.");
            if (Equal(original.After, item.After)) continue;
            if (item.Writer == SwShRestorationWriter.File && icons.Contains(item.Path))
            {
                ValidateIcon(item.After);
            }
            else if (item.Writer == SwShRestorationWriter.File && Archive(item.Path)) ValidateModelArchive(item);
            else if (item.Writer == SwShRestorationWriter.File && Giant(item.Path)) ValidateResourceSubset(original.After, item.After);
            else if (item.Writer == SwShRestorationWriter.ModelCatalog) ValidatePaletteRouting(original.After, item.After, models);
            else if (item.Writer != SwShRestorationWriter.Voices || item.Path != SwShRestorationComposer.VoicesPath
                || !item.Key.StartsWith("2:", StringComparison.Ordinal) || !SameSoundExceptGain(original.After, item.After))
                throw new InvalidDataException("This restoration update changes an unsupported resource.");
            if (!keepModels || !Model(item)) updates.Add(item with { Before = original.After });
        }
        changed = updates.ToArray();
        // Keep edited models, gameplay, loose resources, and custom icons. Only
        // a value that still equals the previous default follows the new default.
        return next with { Contributions = customized.Contributions.Select(item =>
            (!keepModels || !Model(item)) && old.TryGetValue((item.Path, item.Key), out var original) && Equal(original.After, item.After)
                ? replacements[(item.Path, item.Key)] : item)
            .Concat(next.Contributions.Where(item => !old.ContainsKey((item.Path, item.Key)) && (!keepModels || !Model(item)))).ToArray() };
    }
    private static void ValidateIcon(byte[] bytes)
    {
        var image = new ModelTextureDocument(bytes);
        if (image.Width != 136 || image.Height != 112 || image.MipCount != 1)
            throw new InvalidDataException("Updated party icon layout is invalid.");
    }
    private static void ValidateResourceSubset(byte[] previous, byte[] next)
    {
        var old = SwShGfPackFile.ParseBoundedReadOnly(previous, 4096, 64L * 1024 * 1024);
        var pack = SwShGfPackFile.ParseBoundedReadOnly(next, 4096, 64L * 1024 * 1024);
        if (!pack.Write().AsSpan().SequenceEqual(next)) throw new InvalidDataException("Updated resource archive layout is invalid.");
        foreach (var member in pack.AbsoluteHashes)
            if (!old.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024).AsSpan()
                .SequenceEqual(pack.ReadFileByHash(member.HashFnv1aPathFull, 32 * 1024 * 1024)))
                throw new InvalidDataException("Updated giant resources change retained content.");
    }
    private static void ValidateModelArchive(SwShRestorationContribution item)
    {
        var pack = SwShGfPackFile.ParseBoundedReadOnly(item.After, 4096, 64L * 1024 * 1024);
        if (!pack.Write().AsSpan().SequenceEqual(item.After))
            throw new InvalidDataException("Updated model archive layout is invalid.");
        var name = Path.GetFileNameWithoutExtension(item.Path);
        var scene = new SwShPreviewReader(path => pack.GetFileByName(Path.GetFileName(path))).Load(name + ".gfbmdl");
        if (scene.Primitives.Count == 0) throw new InvalidDataException("Updated model archive has no geometry.");
    }
    private static void ValidatePaletteRouting(byte[] previous, byte[] next, string[] models)
    {
        var left = JsonSerializer.Deserialize<SwShPokemonResourceRow>(previous) ?? throw new InvalidDataException("Original model routing is missing.");
        var right = JsonSerializer.Deserialize<SwShPokemonResourceRow>(next) ?? throw new InvalidDataException("Updated model routing is missing.");
        if (left.Identity != right.Identity || !left.Identity.Shiny || left.Model != right.Model || left.Configuration != right.Configuration
            || !left.Controllers.OrderBy(p => p.Key).SequenceEqual(right.Controllers.OrderBy(p => p.Key))
            || !models.Any(stem => left.Archive == stem[6..] + ".gfpak" && right.Archive == stem[6..] + "_rare.gfpak"))
            throw new InvalidDataException("Restoration update changes unsupported model routing.");
    }
    private static bool SameSoundExceptGain(byte[] previous, byte[] next)
    {
        static byte[]? WithoutGain(byte[] bytes)
        {
            if (bytes.Length < 28 || bytes[14] != 0 || bytes[15] != 0 || bytes[16] != 0 || bytes[25] != 0) return null;
            if (bytes[26] == 0) return bytes;
            if (bytes[26] != 1 || bytes.Length < 33 || bytes[27] != 6) return null;
            var gain = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(28));
            if (!float.IsFinite(gain) || gain is < -96 or > 24) return null;
            var stripped = new byte[bytes.Length - 5]; bytes.AsSpan(0, 27).CopyTo(stripped); stripped[26] = 0;
            bytes.AsSpan(32).CopyTo(stripped.AsSpan(27)); return stripped;
        }
        var left = WithoutGain(previous); var right = WithoutGain(next);
        return left is not null && right is not null && left.AsSpan().SequenceEqual(right);
    }
    private static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
