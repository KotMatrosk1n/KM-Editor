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
        Species("qwilfish-hisui", 211, 1),
        Species("dialga-origin", 483, 1),
        Species("palkia-origin", 484, 1),
        Species("sliggoo-hisui", 705, 1),
        Species("goodra-hisui", 706, 1),
        Species("decidueye-hisui", 724, 1),
        Species("kyogre-primal", 382, 1) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("groudon-primal", 383, 1) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("hoopa", 720) with { Donor = SwShRestorationDonor.LegendsZA },
        Species("hoopa-unbound", 720, 1),
        Species("murkrow", 198) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(198, 0), 1, "pm0198_01") }) },
        Species("misdreavus", 200),
        Species("teddiursa", 216),
        Species("ursaring", 217) with { AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(217, 0), 1, "pm0217_01") }) },
        Species("cranidos", 408),
        Species("rampardos", 409),
        Species("shieldon", 410),
        Species("bastiodon", 411),
        Species("mismagius", 429),
        Species("honchkrow", 430),
        Species("blitzle", 522),
        Species("zebstrika", 523),
        Species("sewaddle", 540),
        Species("swadloon", 541),
        Species("leavanny", 542),
        Species("bidoof", 399) with { Donor = SwShRestorationDonor.LegendsArceus, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(399, 0), 1, "pm0399_01") }) },
        Species("bibarel", 400) with { Donor = SwShRestorationDonor.LegendsArceus, AdditionalModelVariants = Array.AsReadOnly(new[] { new SwShRestorationModelVariant(new(400, 0), 1, "pm0400_01") }) },
        Species("ursaluna", 901),
        Species("basculegion", 902),
        Species("overqwil", 904),
        Species("enamorus", 905),
        Species("basculin-white-striped", 550, 2),
        Species("basculegion-female", 902, 1),
        Species("enamorus-therian", 905, 1),
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
        Species("scatterbug-polar", 664, 1) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-tundra", 664, 2) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-continental", 664, 3) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-garden", 664, 4) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-elegant", 664, 5) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-meadow", 664, 6) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-modern", 664, 7) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-marine", 664, 8) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-archipelago", 664, 9) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-high-plains", 664, 10) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-sandstorm", 664, 11) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-river", 664, 12) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-monsoon", 664, 13) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-savanna", 664, 14) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-sun", 664, 15) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-ocean", 664, 16) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-jungle", 664, 17) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-fancy", 664, 18) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug-poke-ball", 664, 19) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-polar", 665, 1) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-tundra", 665, 2) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-continental", 665, 3) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-garden", 665, 4) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-elegant", 665, 5) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-meadow", 665, 6) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-modern", 665, 7) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-marine", 665, 8) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-archipelago", 665, 9) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-high-plains", 665, 10) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-sandstorm", 665, 11) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-river", 665, 12) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-monsoon", 665, 13) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-savanna", 665, 14) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-sun", 665, 15) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-ocean", 665, 16) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-jungle", 665, 17) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-fancy", 665, 18) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa-poke-ball", 665, 19) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-polar", 666, 1) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-tundra", 666, 2) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-continental", 666, 3) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-garden", 666, 4) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-elegant", 666, 5) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-meadow", 666, 6) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-modern", 666, 7) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-marine", 666, 8) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-archipelago", 666, 9) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-high-plains", 666, 10) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-sandstorm", 666, 11) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-river", 666, 12) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-monsoon", 666, 13) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-savanna", 666, 14) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-sun", 666, 15) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-ocean", 666, 16) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-jungle", 666, 17) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-fancy", 666, 18) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon-poke-ball", 666, 19) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("bellsprout", 69) with { AssetRevision = 2, Donor = SwShRestorationDonor.LegendsZA },
        Species("weepinbell", 70) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("victreebel", 71) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("spinarak", 167) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("ariados", 168) with { AssetRevision = 2, Donor = SwShRestorationDonor.LegendsZA },
        Species("meditite", 307) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = [new(new(307, 0), 1, "pm0307_01")] },
        Species("medicham", 308) with { AssetRevision = 2, Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = [new(new(308, 0), 1, "pm0308_01")] },
        Species("spoink", 325) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("grumpig", 326) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("chimecho", 358) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("chingling", 433) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("scatterbug", 664) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("spewpa", 665) with { AssetRevision = 6, Donor = SwShRestorationDonor.LegendsZA },
        Species("vivillon", 666) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("litleo", 667) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("pyroar", 668) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA, AdditionalModelVariants = [new(new(668, 0), 1, "pm0668_01")] },
        Species("flabebe", 669) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("floette", 670) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("florges", 671) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("skiddo", 672) with { AssetRevision = 2, Donor = SwShRestorationDonor.LegendsZA },
        Species("gogoat", 673) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("crabrawler", 739) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("crabominable", 740) with { AssetRevision = 4, Donor = SwShRestorationDonor.LegendsZA },
        Species("sentret", 161) with { AssetRevision = 2 },
        Species("furret", 162) with { AssetRevision = 3 },
        Species("hoppip", 187) with { AssetRevision = 3 },
        Species("skiploom", 188) with { AssetRevision = 3 },
        Species("jumpluff", 189) with { AssetRevision = 2 },
        Species("sunkern", 191) with { AssetRevision = 2 },
        Species("sunflora", 192) with { AssetRevision = 1 },
        Species("snubbull", 209) with { AssetRevision = 2 },
        Species("granbull", 210) with { AssetRevision = 2 },
        Species("smeargle", 235) with { AssetRevision = 2 },
        Species("surskit", 283) with { AssetRevision = 2 },
        Species("masquerain", 284) with { AssetRevision = 3 },
        Species("makuhita", 296) with { AssetRevision = 1 },
        Species("hariyama", 297) with { AssetRevision = 4 },
        Species("plusle", 311) with { AssetRevision = 2 },
        Species("minun", 312) with { AssetRevision = 2 },
        Species("volbeat", 313) with { AssetRevision = 5 },
        Species("illumise", 314) with { AssetRevision = 3 },
        Species("tropius", 357) with { AssetRevision = 3 },
        Species("luvdisc", 370) with { AssetRevision = 2 },
        Species("pikipek", 731) with { AssetRevision = 5 },
        Species("trumbeak", 732) with { AssetRevision = 3 },
        Species("toucannon", 733) with { AssetRevision = 5 },
        Species("yungoos", 734) with { AssetRevision = 3 },
        Species("gumshoos", 735) with { AssetRevision = 3 },
        Species("oricorio", 741) with { AssetRevision = 2 },
        Species("minior", 774) with { AssetRevision = 7 },
        Species("komala", 775) with { AssetRevision = 1 },
        Species("bruxish", 779) with { AssetRevision = 6 },
        Species("wurmple", 265) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsArceus },
        Species("silcoon", 266) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsArceus },
        Species("beautifly", 267) with { AssetRevision = 2, Donor = SwShRestorationDonor.LegendsArceus, AdditionalModelVariants = [new(new(267, 0), 1, "pm0267_01")] },
        Species("cascoon", 268) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsArceus },
        Species("dustox", 269) with { AssetRevision = 2, Donor = SwShRestorationDonor.LegendsArceus, AdditionalModelVariants = [new(new(269, 0), 1, "pm0269_01")] },
        Species("flabebe-yellow", 669, 1) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("flabebe-orange", 669, 2) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("flabebe-blue", 669, 3) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("flabebe-white", 669, 4) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("floette-yellow", 670, 1) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("floette-orange", 670, 2) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("floette-blue", 670, 3) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("floette-white", 670, 4) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("floette-eternal", 670, 5) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("florges-yellow", 671, 1) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("florges-orange", 671, 2) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("florges-blue", 671, 3) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("florges-white", 671, 4) with { AssetRevision = 3, Donor = SwShRestorationDonor.LegendsZA },
        Species("oricorio-pom-pom", 741, 1) with { AssetRevision = 2 },
        Species("oricorio-pau", 741, 2) with { AssetRevision = 2 },
        Species("oricorio-sensu", 741, 3) with { AssetRevision = 2 },
        Species("minior-meteor-orange", 774, 1) with { AssetRevision = 7 },
        Species("minior-meteor-yellow", 774, 2) with { AssetRevision = 7 },
        Species("minior-meteor-green", 774, 3) with { AssetRevision = 7 },
        Species("minior-meteor-blue", 774, 4) with { AssetRevision = 7 },
        Species("minior-meteor-indigo", 774, 5) with { AssetRevision = 7 },
        Species("minior-meteor-violet", 774, 6) with { AssetRevision = 7 },
        Species("minior-core-red", 774, 7) with { AssetRevision = 7 },
        Species("minior-core-orange", 774, 8) with { AssetRevision = 7 },
        Species("minior-core-yellow", 774, 9) with { AssetRevision = 7 },
        Species("minior-core-green", 774, 10) with { AssetRevision = 7 },
        Species("minior-core-blue", 774, 11) with { AssetRevision = 7 },
        Species("minior-core-indigo", 774, 12) with { AssetRevision = 7 },
        Species("minior-core-violet", 774, 13) with { AssetRevision = 7 },
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
                        : SwShRestorationSpecies.DisplayName(group.Key) ?? $"Pokemon {group.Key}",
                    definition?.Id, paths.SelectedGame is { } game && definition is not null ? definition.Support.GetValueOrDefault(game) : SwShRestorationSupport.NotSupported,
                    selection?.Enabled == true ? complete ? SwShRestorationInstallation.Enabled : SwShRestorationInstallation.NeedsRepair
                        : group.Any(e => e.Personal.IsPresentInGame) ? SwShRestorationInstallation.External : SwShRestorationInstallation.Disabled,
                    definition?.Identities.Select(i => i.Species).Distinct().ToArray() ?? [group.Key])
                    { Donor = definition?.Donor, AvailableForAssignment = complete };
            });
        return new(catalog.Revision + ":forms-1", SwShRestorationFormCandidates.Include(entries, catalog, names, paths.SelectedGame, state.State), null);
    }
}
