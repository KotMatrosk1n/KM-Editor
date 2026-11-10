// SPDX-License-Identifier: GPL-3.0-only
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using KM.Core.Projects;

namespace KM.SwSh.Restoration;

public sealed record SwShRestorationSourceFingerprint(string Role, string RelativePath, string Sha256, long Length);
public sealed record SwShPreparedRestoration(string DefinitionId, int DefinitionVersion, int ConverterVersion,
    ProjectGame Game, IReadOnlyList<SwShRestorationSourceFingerprint> Sources,
    IReadOnlyList<SwShRestorationContribution> Contributions, IReadOnlyDictionary<int, int[]> UnavailableSourceMoves)
{
    public int AssetRevision { get; init; } = 1;
    private sealed record Manifest(int Schema, string DefinitionId, int DefinitionVersion, int ConverterVersion,
        ProjectGame Game, SwShRestorationSourceFingerprint[] Sources, Item[] Items, Dictionary<int, int[]> UnavailableSourceMoves)
    {
        public int AssetRevision { get; init; } = 1;
    }
    private sealed record Item(string Path, SwShRestorationWriter Writer, string Key, string? Before, string After);

    public byte[] Encode()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var written = new HashSet<string>(StringComparer.Ordinal); long total = 0;
            string Blob(byte[] bytes)
            {
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (written.Add(hash))
                {
                    total += bytes.Length;
                    if (total > 192L * 1024 * 1024) throw new InvalidDataException("Prepared restoration exceeds its storage budget.");
                    var entry = zip.CreateEntry("blobs/" + hash, CompressionLevel.Fastest);
                    entry.LastWriteTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var stream = entry.Open(); stream.Write(bytes);
                }
                return hash;
            }
            var items = Contributions.OrderBy(c => c.Path, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new Item(c.Path, c.Writer, c.Key, c.Before is null ? null : Blob(c.Before), Blob(c.After))).ToArray();
            var manifest = new Manifest(1, DefinitionId, DefinitionVersion, ConverterVersion, Game, Sources.OrderBy(s => s.Role).ThenBy(s => s.RelativePath).ToArray(), items,
                UnavailableSourceMoves.ToDictionary()) { AssetRevision = AssetRevision };
            var metadata = zip.CreateEntry("manifest.json", CompressionLevel.Fastest); metadata.LastWriteTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var destination = metadata.Open(); JsonSerializer.Serialize(destination, manifest);
        }
        if (output.Length > 192L * 1024 * 1024) throw new InvalidDataException("Prepared restoration exceeds its encoded storage budget.");
        return output.ToArray();
    }

    public static SwShPreparedRestoration Decode(byte[] bytes)
    {
        if (bytes.Length is < 1 or > 192 * 1024 * 1024) throw new InvalidDataException("Prepared restoration size is invalid.");
        using var stream = new MemoryStream(bytes, writable: false); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 2 or > 20001 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count
            || zip.Entries.Sum(e => e.Length) > 193L * 1024 * 1024) throw new InvalidDataException("Prepared restoration entries exceed their budget.");
        var metadata = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Prepared restoration manifest is missing.");
        if (metadata.Length > 2 * 1024 * 1024) throw new InvalidDataException("Prepared restoration manifest exceeds its budget.");
        using var input = metadata.Open(); var manifest = JsonSerializer.Deserialize<Manifest>(input) ?? throw new InvalidDataException("Prepared restoration manifest is empty.");
        if (manifest.Items is null || manifest.Sources is null || manifest.UnavailableSourceMoves is null
            || manifest.Sources.Length is < 1 or > 10000 || manifest.Items.Any(i => i is null || i.Path is null || i.Key is null || i.After is null))
            throw new InvalidDataException("Prepared restoration manifest fields are missing.");
        var definition = SwShRestorationDefinitions.Get(manifest.DefinitionId);
        if (manifest.Schema != 1 || manifest.AssetRevision < 1 || manifest.DefinitionVersion != definition.Version || manifest.ConverterVersion != definition.ConverterVersion
            || manifest.Game is not (ProjectGame.Sword or ProjectGame.Shield) || manifest.Items.Length is < 1 or > 10000)
            throw new InvalidDataException("Prepared restoration version or target is unsupported.");
        var read = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var source in manifest.Sources)
        {
            if (source is null || source.RelativePath is null || source.Role is not ("donor" or "donor-media" or "target") || source.Length is < 1 or > 512L * 1024 * 1024
                || source.Sha256 is null || source.Sha256.Length != 64 || source.Sha256.Any(c => !char.IsAsciiHexDigitLower(c)))
                throw new InvalidDataException("Prepared restoration source identity is invalid.");
            _ = new KM.Core.Semantics.RelativeOutputPath(source.RelativePath);
        }
        if (manifest.Sources.Select(s => (s.Role, s.RelativePath)).Distinct().Count() != manifest.Sources.Length
            || manifest.Items.Select(i => (i.Path, i.Key)).Distinct().Count() != manifest.Items.Length
            || manifest.Items.Any(i => !Enum.IsDefined(i.Writer))) throw new InvalidDataException("Prepared restoration identities are duplicated or unsupported.");
        byte[] Blob(string hash)
        {
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigitLower(c))) throw new InvalidDataException("Prepared asset identity is invalid.");
            if (read.TryGetValue(hash, out var cached)) return cached;
            var entry = zip.GetEntry("blobs/" + hash) ?? throw new InvalidDataException("Prepared asset is missing.");
            if (entry.Length > 64 * 1024 * 1024) throw new InvalidDataException("Prepared asset exceeds its size budget.");
            using var content = entry.Open(); var payload = new byte[checked((int)entry.Length)]; content.ReadExactly(payload);
            if (Convert.ToHexStringLower(SHA256.HashData(payload)) != hash) throw new InvalidDataException("Prepared asset content does not match its identity.");
            read.Add(hash, payload); return payload;
        }
        var contributions = manifest.Items.Select(i => new SwShRestorationContribution(i.Path, i.Writer, i.Key, i.Before is null ? null : Blob(i.Before), Blob(i.After))).ToArray();
        var prepared = new SwShPreparedRestoration(manifest.DefinitionId, manifest.DefinitionVersion, manifest.ConverterVersion, manifest.Game, manifest.Sources, contributions, manifest.UnavailableSourceMoves)
            { AssetRevision = manifest.AssetRevision };
        ValidateContributions(definition, prepared);
        return prepared;
    }

    private static void ValidateContributions(SwShRestorationDefinition definition, SwShPreparedRestoration prepared)
    {
        var rows = definition.Identities.Select(SwShRestorationForms.PersonalRow).ToHashSet();
        var stones = definition.Identities.Select(SwShRestorationMegaItems.For).OfType<SwShRestorationMegaItem>().ToArray();
        var variants = SwShRestorationModelResources.Variants(prepared).ToArray();
        foreach (var item in prepared.Contributions)
        {
            SwShRestorationComposer.ValidatePath(item.Path, item.Writer);
            var valid = item.Writer switch
            {
                SwShRestorationWriter.SpeciesText => definition.Identities.Any(i => SwShRestorationSpecies.DisplayName(i.Species) is not null
                    && item.Key == i.Species.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                SwShRestorationWriter.AbilityText => int.TryParse(item.Key, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var ability) && definition.Identities.Any(identity =>
                        SwShRestorationAbilities.IsAbilityFor(identity, ability)),
                SwShRestorationWriter.Items or SwShRestorationWriter.ItemText =>
                    stones.Any(stone => item.Key == stone.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                SwShRestorationWriter.Personal or SwShRestorationWriter.Learnset =>
                    int.TryParse(item.Key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && rows.Contains(id),
                SwShRestorationWriter.Icons => definition.Identities.Any(i => SwShRestorationComposer.IconGenders(i.Species).Any(g => item.Key == SwShRestorationComposer.IconKey(i.Species, i.Form, g))
                    || i.Species == SwShRestorationSpecies.Annihilape && i.Form == 0 && item.Key == "979"),
                SwShRestorationWriter.File => item.Key.Length == 0 && (SwShRestorationEvolutionPolicy.IsNativeParentFile(definition, item)
                    || stones.Any(stone => item.Path == SwShRestorationMegaItems.IconPath(stone.ItemId)) || definition.Identities.Any(i =>
                    SwShRestorationComposer.IconGenders(i.Species).Any(g => item.Path == SwShRestorationComposer.IconPath(i.Species, i.Form, g))
                    || i.Species == SwShRestorationSpecies.Annihilape && i.Form == 0 && item.Path == "romfs/bin/appli/icon_pokemon/poke_icon_0979_00s_n.bntx"
                    || item.Path == $"romfs/bin/pml/evolution/evo_{SwShRestorationForms.PersonalRow(i):000}.bin"
                    || item.Path == $"romfs/bin/pml/tamagowaza/tamagowaza_{SwShRestorationForms.EggRow(i):0000}.bin"
                    || variants.Where(v => v.Identity == i).Any(v => item.Path == $"romfs/bin/archive/pokemon/{v.ResourceStem}.gfpak"
                        || item.Path == $"romfs/bin/archive/pokemon/{v.ResourceStem}_rare.gfpak"
                        || item.Path == $"romfs/bin/archive/gpokemon/{v.ResourceStem}.gfpak"
                        || item.Path.StartsWith($"romfs/bin/pokemon/{v.ResourceStem}/", StringComparison.Ordinal)))),
                SwShRestorationWriter.ModelCatalog => variants.Any(v => new[] { false, true }.Any(shiny =>
                    item.Key == SwShRestorationComposer.CatalogKey(new(v.Identity.Species, v.Identity.Form, v.Gender, shiny)))),
                SwShRestorationWriter.Camp => definition.Identities.Any(i =>
                    item.Key == $"bin/pokemon_data/pokecamp/common/poke_{i.Species:0000}_XX_X.bin"
                    || variants.Where(v => v.Identity == i).Any(v => new[] { "motion", "face", "ball", "turning" }.Any(group =>
                        item.Key == $"bin/pokemon_data/pokecamp/{group}/poke_{group}_{i.Species:0000}_{i.Form:00}_{v.Gender}.bin"))),
                SwShRestorationWriter.Voices => true,
                _ => false,
            };
            if (!valid) throw new InvalidDataException("Prepared restoration addresses an identity outside its registered family.");
        }
    }
}
