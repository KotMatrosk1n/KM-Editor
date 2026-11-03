// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.SwSh.Pokemon;
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

public enum SwShRestorationSupport { NotSupported, Testing, Ready }
public enum SwShRestorationInstallation { Disabled, Enabled, NeedsRepair, External }
public enum SwShRestorationDonor { ScarletViolet, LegendsZA, LegendsArceus }
public sealed record SwShRestorationModelVariant(SwShPokemonIdentity Identity, int Gender, string ResourceStem)
{
    public string? SourceResourceStem { get; init; }
}
public sealed record SwShRestorationDefinition(string Id, int Version, int ConverterVersion,
    IReadOnlyList<SwShPokemonIdentity> Identities, IReadOnlyDictionary<ProjectGame, SwShRestorationSupport> Support,
    IReadOnlyList<string> RequiredContexts)
{
    // Increment only for definitions whose generated assets change. This is independent
    // of the storage versions so installed assets remain readable and removable.
    public int AssetRevision { get; init; } = 1;
    public bool HasAssetUpdate(SwShRestorationSelection? installed) =>
        installed is { RemovalId: null } && installed.DefinitionId == Id && installed.AssetRevision < AssetRevision;
    public SwShRestorationDonor Donor { get; init; } = SwShRestorationDonor.ScarletViolet;
    public IReadOnlyList<SwShRestorationModelVariant> AdditionalModelVariants { get; init; } = [];
}

public static class SwShRestorationDefinitions
{
    public const string Domain = "workflow.restoreMissingPokemon";
    public static readonly SwShRestorationDefinition Piplup = new("piplup-family", 1, 1,
        Array.AsReadOnly(new[] { new SwShPokemonIdentity(393, 0), new SwShPokemonIdentity(394, 0), new SwShPokemonIdentity(395, 0) }),
        new System.Collections.ObjectModel.ReadOnlyDictionary<ProjectGame, SwShRestorationSupport>(new Dictionary<ProjectGame, SwShRestorationSupport>
        {
            [ProjectGame.Sword] = SwShRestorationSupport.Testing, [ProjectGame.Shield] = SwShRestorationSupport.Testing,
        }), Array.AsReadOnly(new[] { "summary", "battle", "field", "evolution", "breeding", "camp", "dynamax" }));
    public static IReadOnlyList<SwShRestorationDefinition> PiplupSpecies { get; } = Array.AsReadOnly(new[]
    {
        Species("piplup", 393), Species("prinplup", 394), Species("empoleon", 395),
    });
    public static IReadOnlyList<SwShRestorationDefinition> All { get; } = Array.AsReadOnly(PiplupSpecies.Concat(new[]
    {
        Species("venusaur-mega", 3, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 4 },
        Species("blastoise-mega", 9, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 4 },
        Species("absol-mega", 359, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 4 },
        Species("absol-mega-z", 359, 2) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 6 },
        Species("beedrill-mega", 15, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 3 },
        Species("pidgeot-mega", 18, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 5 },
        Species("raichu-mega-x", 26, 2) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 6 },
        Species("raichu-mega-y", 26, 3) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 8 },
        Species("clefable-mega", 36, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 9 },
        Species("alakazam-mega", 65, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 5 },
        Species("paras", 46) with { Donor = SwShRestorationDonor.LegendsArceus },
        Species("parasect", 47) with { Donor = SwShRestorationDonor.LegendsArceus },
        Species("venonat", 48), Species("venomoth", 49),
        Species("mankey", 56) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("primeape", 57) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("annihilape", 979) with { AssetRevision = 3 },
        Species("mega-charizard-x", 6, 1) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 3 },
        Species("mega-charizard-y", 6, 2) with { Donor = SwShRestorationDonor.LegendsZA, AssetRevision = 3 },
        Species("chikorita", 152) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("bayleef", 153) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("meganium", 154) with { Donor = SwShRestorationDonor.LegendsZA,
            AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(154, 0), 1, "pm0154_01") }) },
        Species("cyndaquil", 155), Species("quilava", 156), Species("typhlosion", 157),
        Species("totodile", 158) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("croconaw", 159) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("feraligatr", 160) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("snivy", 495), Species("servine", 496), Species("serperior", 497),
        Species("tepig", 498) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pignite", 499) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("emboar", 500) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("oshawott", 501), Species("dewott", 502), Species("samurott", 503),
        Species("chespin", 650) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("quilladin", 651) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("chesnaught", 652) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("fennekin", 653) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("braixen", 654) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("delphox", 655) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("froakie", 656) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("frogadier", 657) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("greninja", 658) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("ekans", 23), Species("arbok", 24),
        Species("geodude", 74), Species("graveler", 75), Species("golem", 76),
        Species("alolan-geodude", 74, 1), Species("alolan-graveler", 75, 1), Species("alolan-golem", 76, 1),
        Species("turtwig", 387), Species("grotle", 388), Species("torterra", 389),
        Species("chimchar", 390), Species("monferno", 391), Species("infernape", 392),
        Species("weedle", 13) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("kakuna", 14) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("beedrill", 15) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pidgey", 16) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pidgeotto", 17) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("pidgeot", 18) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("kecleon", 352) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("hisuian-growlithe", 58, 1), Species("hisuian-arcanine", 59, 1),
        Species("doduo", 84) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(84, 0), 1, "pm0084_01") }) },
        Species("dodrio", 85) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(85, 0), 1, "pm0085_01") }) },
        Species("seel", 86), Species("dewgong", 87), Species("grimer", 88), Species("muk", 89),
        Species("alolan-grimer", 88, 1), Species("alolan-muk", 89, 1),
        Species("drowzee", 96),
        Species("hypno", 97) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(97, 0), 1, "pm0097_01") }) },
        Species("voltorb", 100), Species("electrode", 101),
        Species("hisuian-voltorb", 100, 1), Species("hisuian-electrode", 101, 1),
        Species("hisuian-typhlosion", 157, 1), Species("hisuian-samurott", 503, 1), Species("hisuian-zoroark", 571, 1),
        Species("mareep", 179) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("flaaffy", 180) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("ampharos", 181) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("girafarig", 203) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(203, 0), 1, "pm0203_01") }) },
        Species("pineco", 204),
        Species("forretress", 205),
        Species("gligar", 207) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(207, 0), 1, "pm0207_01") }) },
        Species("gliscor", 472),
        Species("slugma", 218),
        Species("magcargo", 219),
        Species("houndour", 228) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("houndoom", 229) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(229, 0), 1, "pm0229_01") }) },
        Species("phanpy", 231),
        Species("donphan", 232) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(232, 0), 1, "pm0232_01") }) },
        Species("poochyena", 261),
        Species("mightyena", 262),
        Species("shroomish", 285),
        Species("breloom", 286),
        Species("slakoth", 287),
        Species("vigoroth", 288),
        Species("slaking", 289),
        Species("gulpin", 316) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(316, 0), 1, "pm0316_01") }) },
        Species("swalot", 317) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(317, 0), 1, "pm0317_01") }) },
        Species("numel", 322) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(322, 0), 1, "pm0322_01") }) },
        Species("camerupt", 323) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(323, 0), 1, "pm0323_01") }) },
        Species("cacnea", 331),
        Species("cacturne", 332) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(332, 0), 1, "pm0332_01") }) },
        Species("zangoose", 335) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("seviper", 336) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("shuppet", 353) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("banette", 354) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("deoxys", 386),
        Species("deoxys-attack", 386, 1),
        Species("deoxys-defense", 386, 2),
        Species("deoxys-speed", 386, 3),
        Species("tynamo", 602) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("eelektrik", 603) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("eelektross", 604) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("darkrai", 491) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("yanma", 193),
        Species("starly", 396) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(396, 0), 1, "pm0396_01") }) },
        Species("staravia", 397) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(397, 0), 1, "pm0397_01") }) },
        Species("staraptor", 398) with { Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(398, 0), 1, "pm0398_01") }) },
        Species("yanmega", 469),
    }).ToArray());
    private static SwShRestorationDefinition Species(string id, int species, int form = 0) => Piplup with
        { Id = id, Identities = Array.AsReadOnly(new[] { new SwShPokemonIdentity(species, form) }), AssetRevision = id switch
        {
            "piplup" or "prinplup" or "empoleon" or "paras" or "parasect" or "venonat" or "venomoth" or "mankey" or "primeape" or "annihilape"
                or "chikorita" or "bayleef" or "meganium" or "cyndaquil" or "quilava" or "typhlosion"
                or "totodile" or "croconaw" or "feraligatr" or "snivy" or "servine" or "serperior"
                or "tepig" or "pignite" or "emboar" or "oshawott" or "dewott" or "samurott"
                or "chespin" or "quilladin" or "chesnaught" or "fennekin" or "braixen" or "delphox"
                or "froakie" or "frogadier" or "greninja" or "ekans" or "arbok" or "geodude" or "graveler" or "golem"
                or "alolan-geodude" or "alolan-graveler" or "alolan-golem" or "turtwig" or "grotle" or "torterra"
                or "chimchar" or "monferno" or "infernape" or "weedle" or "kakuna" or "beedrill"
                or "pidgey" or "pidgeotto" or "pidgeot" or "kecleon"
                or "deoxys" or "deoxys-attack" or "deoxys-defense" or "deoxys-speed" or "darkrai" or "alolan-muk" => 2,
            _ => 1,
        } };
    public static SwShRestorationDefinition Get(string id) => id == Piplup.Id ? Piplup : All.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidDataException("This restoration definition is not supported.");
}

public sealed record SwShRestorationCatalogEntry(int SpeciesId, string Name, string? DefinitionId,
    SwShRestorationSupport Support, SwShRestorationInstallation Installation, IReadOnlyList<int> Family)
{
    public int Form { get; init; }
    public string? FormLabel { get; init; }
    public int? PlannedPersonalId { get; init; }
    public SwShRestorationDonor? Donor { get; init; }
    public bool AvailableForAssignment { get; init; }
}
public sealed record SwShRestorationCatalog(string Revision, IReadOnlyList<SwShRestorationCatalogEntry> Entries, string? UnavailableReason)
{
    public static SwShRestorationCatalog Discover(ProjectPaths paths)
    {
        var relative = SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.Resolve(paths), "monsname.dat");
        var file = Path.Combine(paths.OutputRootPath ?? "", relative);
        if (!File.Exists(file)) file = Path.Combine(paths.BaseRomFsPath ?? "", relative[6..]);
        var names = File.Exists(file) && new FileInfo(file).Length < 4 * 1024 * 1024
            ? SwShGameTextFile.Parse(File.ReadAllBytes(file), 2048).Lines.Select(l => l.Text).ToArray() : [];
        return Discover(paths, names);
    }
    public static SwShRestorationCatalog Discover(ProjectPaths paths, IReadOnlyList<string> names)
    {
        var catalog = SwShEffectivePokemonCatalog.Load(paths);
        if (!catalog.Available) return new(catalog.Revision, [], catalog.UnavailableReason);
        var state = SwShRestorationStateStore.Read(paths);
        var entries = catalog.Entries.Where(e => e.Identity.Form == 0).GroupBy(e => e.Identity.Species).Where(g => g.All(e => e.Origin != SwShPokemonOrigin.Native))
            .Select(group =>
            {
                var definition = SwShRestorationDefinitions.All.SingleOrDefault(d => d.Identities.Contains(new(group.Key, 0)));
                var selection = state.State.Selections.SingleOrDefault(s => s.DefinitionId == definition?.Id);
                var complete = group.Any(e => e.Capabilities.HasFlag(SwShPokemonCapability.Assignment));
                return new SwShRestorationCatalogEntry(group.Key,
                    group.Key < names.Count && !string.IsNullOrWhiteSpace(names[group.Key]) ? names[group.Key]
                        : group.Key == SwShRestorationSpecies.Annihilape ? "Annihilape" : $"Pokemon {group.Key}",
                    definition?.Id, paths.SelectedGame is { } game && definition is not null ? definition.Support.GetValueOrDefault(game) : SwShRestorationSupport.NotSupported,
                    selection?.Enabled == true ? complete ? SwShRestorationInstallation.Enabled : SwShRestorationInstallation.NeedsRepair
                        : group.Any(e => e.Personal.IsPresentInGame) ? SwShRestorationInstallation.External : SwShRestorationInstallation.Disabled,
                    definition?.Identities.Select(i => i.Species).Distinct().ToArray() ?? [group.Key])
                    { Donor = definition?.Donor, AvailableForAssignment = complete };
            });
        return new(catalog.Revision + ":forms-1", SwShRestorationFormCandidates.Include(entries, catalog, names, paths.SelectedGame, state.State), null);
    }
}
