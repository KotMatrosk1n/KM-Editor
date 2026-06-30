// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;

namespace KM.Core.Pokemon;

public static class PokemonFormLabels
{
    private static readonly IReadOnlyDictionary<(PokemonFormLabelFamily Family, int SpeciesId, int Form), string> LabelsBySpeciesId =
        CreateLabelsBySpeciesId();

    private static readonly IReadOnlyDictionary<(PokemonFormLabelFamily Family, string SpeciesName, int Form), string> LabelsBySpeciesName =
        CreateLabelsBySpeciesName();

    private static readonly IReadOnlyDictionary<(PokemonFormLabelFamily Family, int SpeciesId), string> BaseLabelsBySpeciesId =
        CreateBaseLabelsBySpeciesId();

    private static readonly IReadOnlyDictionary<(PokemonFormLabelFamily Family, string SpeciesName), string> BaseLabelsBySpeciesName =
        CreateBaseLabelsBySpeciesName();

    public static string? ResolveFormLabel(
        int speciesId,
        string speciesName,
        int form,
        PokemonFormLabelFamily family)
    {
        if (LabelsBySpeciesId.TryGetValue((family, speciesId, form), out var byId))
        {
            return byId;
        }

        var normalizedName = NormalizeSpeciesName(speciesName);
        return LabelsBySpeciesName.TryGetValue((family, normalizedName, form), out var byName)
            ? byName
            : null;
    }

    public static string? ResolveBaseFormLabel(
        int speciesId,
        string speciesName,
        PokemonFormLabelFamily family)
    {
        if (BaseLabelsBySpeciesId.TryGetValue((family, speciesId), out var byId))
        {
            return byId;
        }

        var normalizedName = NormalizeSpeciesName(speciesName);
        return BaseLabelsBySpeciesName.TryGetValue((family, normalizedName), out var byName)
            ? byName
            : null;
    }

    private static IReadOnlyDictionary<(PokemonFormLabelFamily Family, int SpeciesId, int Form), string> CreateLabelsBySpeciesId()
    {
        var labels = new Dictionary<(PokemonFormLabelFamily, int, int), string>();
        foreach (var definition in CreateDefinitions())
        {
            foreach (var family in definition.Families)
            {
                foreach (var (form, label) in definition.Forms)
                {
                    labels[(family, definition.SpeciesId, form)] = label;
                }
            }
        }

        return labels;
    }

    private static IReadOnlyDictionary<(PokemonFormLabelFamily Family, string SpeciesName, int Form), string> CreateLabelsBySpeciesName()
    {
        var labels = new Dictionary<(PokemonFormLabelFamily, string, int), string>();
        foreach (var definition in CreateDefinitions())
        {
            foreach (var family in definition.Families)
            {
                foreach (var speciesName in definition.SpeciesNames.Select(NormalizeSpeciesName))
                {
                    foreach (var (form, label) in definition.Forms)
                    {
                        labels[(family, speciesName, form)] = label;
                    }
                }
            }
        }

        return labels;
    }

    private static IReadOnlyDictionary<(PokemonFormLabelFamily Family, int SpeciesId), string> CreateBaseLabelsBySpeciesId()
    {
        var labels = new Dictionary<(PokemonFormLabelFamily, int), string>();
        foreach (var definition in CreateDefinitions())
        {
            if (definition.BaseLabel is null)
            {
                continue;
            }

            foreach (var family in definition.Families)
            {
                labels[(family, definition.SpeciesId)] = definition.BaseLabel;
            }
        }

        return labels;
    }

    private static IReadOnlyDictionary<(PokemonFormLabelFamily Family, string SpeciesName), string> CreateBaseLabelsBySpeciesName()
    {
        var labels = new Dictionary<(PokemonFormLabelFamily, string), string>();
        foreach (var definition in CreateDefinitions())
        {
            if (definition.BaseLabel is null)
            {
                continue;
            }

            foreach (var family in definition.Families)
            {
                foreach (var speciesName in definition.SpeciesNames.Select(NormalizeSpeciesName))
                {
                    labels[(family, speciesName)] = definition.BaseLabel;
                }
            }
        }

        return labels;
    }

    private static IReadOnlyList<FormLabelDefinition> CreateDefinitions()
    {
        var all = new[]
        {
            PokemonFormLabelFamily.SwordShield,
            PokemonFormLabelFamily.ScarletViolet,
            PokemonFormLabelFamily.LegendsZA,
        };
        var svZa = new[]
        {
            PokemonFormLabelFamily.ScarletViolet,
            PokemonFormLabelFamily.LegendsZA,
        };
        var sv = new[]
        {
            PokemonFormLabelFamily.ScarletViolet,
        };

        return
        [
            Definition(19, ["Rattata"], [(1, "Alolan")], "Kanto", all),
            Definition(20, ["Raticate"], [(1, "Alolan")], "Kanto", all),
            Definition(25, ["Pikachu"], [
                (1, "Original Cap"),
                (2, "Hoenn Cap"),
                (3, "Sinnoh Cap"),
                (4, "Unova Cap"),
                (5, "Kalos Cap"),
                (6, "Alola Cap"),
                (7, "Partner Cap"),
                (8, "World Cap"),
                (9, "World Cap"),
            ], null, all),
            Definition(25, ["Pikachu"], [(8, "Starter"), (9, "World Cap")], null, svZa),
            Definition(26, ["Raichu"], [(1, "Alolan")], "Kanto", all),
            Definition(27, ["Sandshrew"], [(1, "Alolan")], "Kanto", all),
            Definition(28, ["Sandslash"], [(1, "Alolan")], "Kanto", all),
            Definition(37, ["Vulpix"], [(1, "Alolan")], "Kanto", all),
            Definition(38, ["Ninetales"], [(1, "Alolan")], "Kanto", all),
            Definition(50, ["Diglett"], [(1, "Alolan")], "Kanto", all),
            Definition(51, ["Dugtrio"], [(1, "Alolan")], "Kanto", all),
            Definition(52, ["Meowth"], [(1, "Alolan"), (2, "Galarian")], "Kanto", all),
            Definition(53, ["Persian"], [(1, "Alolan")], "Kanto", all),
            Definition(58, ["Growlithe"], [(1, "Hisuian")], "Kantonian", svZa),
            Definition(59, ["Arcanine"], [(1, "Hisuian")], "Kantonian", svZa),
            Definition(74, ["Geodude"], [(1, "Alolan")], "Kanto", all),
            Definition(75, ["Graveler"], [(1, "Alolan")], "Kanto", all),
            Definition(76, ["Golem"], [(1, "Alolan")], "Kanto", all),
            Definition(77, ["Ponyta"], [(1, "Galarian")], "Kanto", all),
            Definition(78, ["Rapidash"], [(1, "Galarian")], "Kanto", all),
            Definition(79, ["Slowpoke"], [(1, "Galarian")], "Kanto", all),
            Definition(80, ["Slowbro"], [(2, "Galarian")], "Kanto", all),
            Definition(83, ["Farfetchd", "Farfetch'd", "Farfetch'd"], [(1, "Galarian")], "Kanto", all),
            Definition(88, ["Grimer"], [(1, "Alolan")], "Kanto", all),
            Definition(89, ["Muk"], [(1, "Alolan")], "Kanto", all),
            Definition(100, ["Voltorb"], [(1, "Hisuian")], "Kantonian", svZa),
            Definition(101, ["Electrode"], [(1, "Hisuian")], "Kantonian", svZa),
            Definition(103, ["Exeggutor"], [(1, "Alolan")], "Kanto", all),
            Definition(105, ["Marowak"], [(1, "Alolan")], "Kanto", all),
            Definition(110, ["Weezing"], [(1, "Galarian")], "Kanto", all),
            Definition(122, ["Mr Mime", "Mr. Mime"], [(1, "Galarian")], "Kanto", all),
            Definition(128, ["Tauros"], [
                (1, "Paldean Combat Breed"),
                (2, "Paldean Blaze Breed"),
                (3, "Paldean Aqua Breed"),
            ], "Kantonian", svZa),
            Definition(133, ["Eevee"], [(1, "Partner")], null, all),
            Definition(144, ["Articuno"], [(1, "Galarian")], "Kanto", all),
            Definition(145, ["Zapdos"], [(1, "Galarian")], "Kanto", all),
            Definition(146, ["Moltres"], [(1, "Galarian")], "Kanto", all),
            Definition(157, ["Typhlosion"], [(1, "Hisuian")], "Johtonian", svZa),
            Definition(194, ["Wooper"], [(1, "Paldean")], "Johtonian", svZa),
            Definition(199, ["Slowking"], [(1, "Galarian")], "Kanto", all),
            Definition(201, ["Unown"], CreateLetterFormLabels(), null, all),
            Definition(211, ["Qwilfish"], [(1, "Hisuian")], "Johtonian", svZa),
            Definition(215, ["Sneasel"], [(1, "Hisuian")], "Johtonian", svZa),
            Definition(222, ["Corsola"], [(1, "Galarian")], "Johto", all),
            Definition(263, ["Zigzagoon"], [(1, "Galarian")], "Hoenn", all),
            Definition(264, ["Linoone"], [(1, "Galarian")], "Hoenn", all),
            Definition(351, ["Castform"], [
                (0, "Normal Form"),
                (1, "Sunny Form"),
                (2, "Rainy Form"),
                (3, "Snowy Form"),
            ], null, all),
            Definition(382, ["Kyogre"], [(1, "Primal")], null, all),
            Definition(383, ["Groudon"], [(1, "Primal")], null, all),
            Definition(386, ["Deoxys"], [
                (0, "Normal Forme"),
                (1, "Attack Forme"),
                (2, "Defense Forme"),
                (3, "Speed Forme"),
            ], null, all),
            Definition(412, ["Burmy"], [
                (0, "Plant Cloak"),
                (1, "Sandy Cloak"),
                (2, "Trash Cloak"),
            ], null, all),
            Definition(413, ["Wormadam"], [
                (0, "Plant Cloak"),
                (1, "Sandy Cloak"),
                (2, "Trash Cloak"),
            ], null, all),
            Definition(414, ["Mothim"], [
                (0, "Plant Cloak"),
                (1, "Sandy Cloak"),
                (2, "Trash Cloak"),
            ], null, all),
            Definition(421, ["Cherrim"], [(0, "Overcast Form"), (1, "Sunshine Form")], null, all),
            Definition(422, ["Shellos"], [(0, "West Sea"), (1, "East Sea")], null, all),
            Definition(423, ["Gastrodon"], [(0, "West Sea"), (1, "East Sea")], null, all),
            Definition(479, ["Rotom"], [
                (0, "Normal"),
                (1, "Heat"),
                (2, "Wash"),
                (3, "Frost"),
                (4, "Fan"),
                (5, "Mow"),
            ], null, all),
            Definition(483, ["Dialga"], [(0, "Normal Forme"), (1, "Origin Forme")], null, svZa),
            Definition(484, ["Palkia"], [(0, "Normal Forme"), (1, "Origin Forme")], null, svZa),
            Definition(487, ["Giratina"], [(0, "Altered Forme"), (1, "Origin Forme")], null, all),
            Definition(492, ["Shaymin"], [(0, "Land Forme"), (1, "Sky Forme")], null, svZa),
            Definition(493, ["Arceus"], CreateArceusFormLabels(), null, svZa),
            Definition(503, ["Samurott"], [(1, "Hisuian")], "Unovan", svZa),
            Definition(521, ["Unfezant"], [(0, "Male"), (1, "Female")], null, all),
            Definition(549, ["Lilligant"], [(1, "Hisuian")], "Unovan", svZa),
            Definition(550, ["Basculin"], [
                (0, "Red-Striped"),
                (1, "Blue-Striped"),
                (2, "White-Striped"),
            ], null, svZa),
            Definition(554, ["Darumaka"], [(1, "Galarian")], "Unovan", all),
            Definition(555, ["Darmanitan"], [
                (0, "Unovan Standard Mode"),
                (1, "Unovan Zen Mode"),
                (2, "Galarian Standard Mode"),
                (3, "Galarian Zen Mode"),
            ], null, all),
            Definition(562, ["Yamask"], [(1, "Galarian")], "Unovan", all),
            Definition(570, ["Zorua"], [(1, "Hisuian")], "Unovan", svZa),
            Definition(571, ["Zoroark"], [(1, "Hisuian")], "Unovan", svZa),
            Definition(585, ["Deerling"], CreateSeasonFormLabels(), null, svZa),
            Definition(586, ["Sawsbuck"], CreateSeasonFormLabels(), null, svZa),
            Definition(592, ["Frillish"], [(0, "Male"), (1, "Female")], null, all),
            Definition(593, ["Jellicent"], [(0, "Male"), (1, "Female")], null, all),
            Definition(618, ["Stunfisk"], [(1, "Galarian")], "Unovan", all),
            Definition(628, ["Braviary"], [(1, "Hisuian")], "Unovan", svZa),
            Definition(641, ["Tornadus"], [(0, "Incarnate Forme"), (1, "Therian Forme")], null, all),
            Definition(642, ["Thundurus"], [(0, "Incarnate Forme"), (1, "Therian Forme")], null, all),
            Definition(645, ["Landorus"], [(0, "Incarnate Forme"), (1, "Therian Forme")], null, all),
            Definition(646, ["Kyurem"], [(0, "Kyurem"), (1, "White Kyurem"), (2, "Black Kyurem")], null, all),
            Definition(647, ["Keldeo"], [(0, "Ordinary Form"), (1, "Resolute Form")], null, all),
            Definition(648, ["Meloetta"], [(0, "Aria Forme"), (1, "Pirouette Forme")], null, svZa),
            Definition(649, ["Genesect"], [
                (0, "Normal"),
                (1, "Douse Drive"),
                (2, "Shock Drive"),
                (3, "Burn Drive"),
                (4, "Chill Drive"),
            ], null, all),
            Definition(658, ["Greninja"], [(1, "Battle Bond"), (2, "Ash-Greninja")], null, svZa),
            Definition(664, ["Scatterbug"], CreateVivillonPatternLabels(), null, svZa),
            Definition(665, ["Spewpa"], CreateVivillonPatternLabels(), null, svZa),
            Definition(666, ["Vivillon"], CreateVivillonPatternLabels(), null, svZa),
            Definition(669, ["Flabebe", "Flabebe"], CreateFlowerColorLabels(), null, svZa),
            Definition(670, ["Floette"], [
                ..CreateFlowerColorLabels(),
                (5, "Eternal Flower"),
            ], null, svZa),
            Definition(671, ["Florges"], CreateFlowerColorLabels(), null, svZa),
            Definition(676, ["Furfrou"], [
                (0, "Natural Trim"),
                (1, "Heart Trim"),
                (2, "Star Trim"),
                (3, "Diamond Trim"),
                (4, "Debutante Trim"),
                (5, "Matron Trim"),
                (6, "Dandy Trim"),
                (7, "La Reine Trim"),
                (8, "Kabuki Trim"),
                (9, "Pharaoh Trim"),
            ], null, svZa),
            Definition(678, ["Meowstic"], [(0, "Male"), (1, "Female")], null, all),
            Definition(681, ["Aegislash"], [(0, "Shield Forme"), (1, "Blade Forme")], null, all),
            Definition(705, ["Sliggoo"], [(1, "Hisuian")], "Kalosian", svZa),
            Definition(706, ["Goodra"], [(1, "Hisuian")], "Kalosian", svZa),
            Definition(710, ["Pumpkaboo"], CreatePumpkinSizeLabels(), null, all),
            Definition(711, ["Gourgeist"], CreatePumpkinSizeLabels(), null, all),
            Definition(713, ["Avalugg"], [(1, "Hisuian")], "Kalosian", svZa),
            Definition(716, ["Xerneas"], [(0, "Neutral Mode"), (1, "Active Mode")], null, all),
            Definition(718, ["Zygarde"], [
                (0, "50% Forme"),
                (1, "10% Forme"),
                (2, "10% Forme Power Construct"),
                (3, "50% Forme Power Construct"),
                (4, "Complete Forme"),
                (5, "Complete Forme Power Construct"),
            ], null, all),
            Definition(720, ["Hoopa"], [(0, "Confined"), (1, "Unbound")], null, svZa),
            Definition(724, ["Decidueye"], [(1, "Hisuian")], "Alolan", svZa),
            Definition(741, ["Oricorio"], [
                (0, "Baile Style"),
                (1, "Pom-Pom Style"),
                (2, "Pa'u Style"),
                (3, "Sensu Style"),
            ], null, svZa),
            Definition(744, ["Rockruff"], [(0, "Standard"), (1, "Own Tempo")], null, all),
            Definition(745, ["Lycanroc"], [(0, "Midday Form"), (1, "Midnight Form"), (2, "Dusk Form")], null, all),
            Definition(746, ["Wishiwashi"], [(0, "Solo Form"), (1, "School Form")], null, all),
            Definition(773, ["Silvally"], CreateSilvallyFormLabels(), null, all),
            Definition(774, ["Minior"], CreateMiniorFormLabels(), null, svZa),
            Definition(778, ["Mimikyu"], [(0, "Disguised Form"), (1, "Busted Form")], null, all),
            Definition(800, ["Necrozma"], [
                (0, "Necrozma"),
                (1, "Dusk Mane"),
                (2, "Dawn Wings"),
                (3, "Ultra Necrozma"),
            ], null, all),
            Definition(801, ["Magearna"], [(0, "Normal"), (1, "Original Color")], null, all),
            Definition(845, ["Cramorant"], [(0, "Normal"), (1, "Gulping Form"), (2, "Gorging Form")], null, all),
            Definition(849, ["Toxtricity"], [(0, "Amped Form"), (1, "Low Key Form")], null, all),
            Definition(854, ["Sinistea"], [(0, "Phony Form"), (1, "Antique Form")], null, all),
            Definition(855, ["Polteageist"], [(0, "Phony Form"), (1, "Antique Form")], null, all),
            Definition(869, ["Alcremie"], CreateAlcremieFormLabels(), null, all),
            Definition(875, ["Eiscue"], [(0, "Ice Face"), (1, "Noice Face")], null, all),
            Definition(876, ["Indeedee"], [(0, "Male"), (1, "Female")], null, all),
            Definition(877, ["Morpeko"], [(0, "Full Belly Mode"), (1, "Hangry Mode")], null, all),
            Definition(888, ["Zacian"], [(0, "Hero of Many Battles"), (1, "Crowned Sword")], null, all),
            Definition(889, ["Zamazenta"], [(0, "Hero of Many Battles"), (1, "Crowned Shield")], null, all),
            Definition(890, ["Eternatus"], [(0, "Eternatus"), (1, "Eternamax")], null, all),
            Definition(892, ["Urshifu"], [(0, "Single Strike Style"), (1, "Rapid Strike Style")], null, all),
            Definition(893, ["Zarude"], [(0, "Zarude"), (1, "Dada")], null, all),
            Definition(898, ["Calyrex"], [(0, "Calyrex"), (1, "Ice Rider"), (2, "Shadow Rider")], null, all),
            Definition(901, ["Ursaluna"], [(1, "Bloodmoon")], "Standard", sv),
            Definition(902, ["Basculegion"], [(0, "Male"), (1, "Female")], null, svZa),
            Definition(905, ["Enamorus"], [(0, "Incarnate Forme"), (1, "Therian Forme")], null, svZa),
            Definition(916, ["Oinkologne"], [(0, "Male"), (1, "Female")], null, svZa),
            Definition(917, ["Dudunsparce"], [(0, "Two-Segment Form"), (1, "Three-Segment Form")], null, svZa),
            Definition(934, ["Palafin"], [(0, "Zero Form"), (1, "Hero Form")], null, svZa),
            Definition(946, ["Maushold"], [(0, "Family of Four"), (1, "Family of Three")], null, svZa),
            Definition(952, ["Tatsugiri"], [(0, "Curly Form"), (1, "Droopy Form"), (2, "Stretchy Form")], null, svZa),
            Definition(960, ["Squawkabilly"], [
                (0, "Green Plumage"),
                (1, "Blue Plumage"),
                (2, "Yellow Plumage"),
                (3, "White Plumage"),
            ], null, svZa),
            Definition(976, ["Gimmighoul"], [(0, "Chest Form"), (1, "Roaming Form")], null, svZa),
            Definition(998, ["Koraidon"], [
                (0, "Apex Build"),
                (1, "Limited Build"),
                (2, "Sprinting Build"),
                (3, "Swimming Build"),
                (4, "Gliding Build"),
            ], null, svZa),
            Definition(999, ["Miraidon"], [
                (0, "Ultimate Mode"),
                (1, "Low-Power Mode"),
                (2, "Drive Mode"),
                (3, "Aquatic Mode"),
                (4, "Glide Mode"),
            ], null, svZa),
            Definition(1011, ["Ogerpon"], [
                (0, "Teal Mask"),
                (1, "Wellspring Mask"),
                (2, "Hearthflame Mask"),
                (3, "Cornerstone Mask"),
                (4, "Teal Mask Terastallized"),
                (5, "Wellspring Mask Terastallized"),
                (6, "Hearthflame Mask Terastallized"),
                (7, "Cornerstone Mask Terastallized"),
            ], null, sv),
            Definition(1021, ["Terapagos"], [(0, "Normal Form"), (1, "Terastal Form"), (2, "Stellar Form")], null, sv),
            Definition(1024, ["Poltchageist"], [(0, "Counterfeit Form"), (1, "Artisan Form")], null, sv),
            Definition(1025, ["Sinistcha"], [(0, "Unremarkable Form"), (1, "Masterpiece Form")], null, sv),
        ];
    }

    private static FormLabelDefinition Definition(
        int speciesId,
        string[] speciesNames,
        (int Form, string Label)[] forms,
        string? baseLabel,
        IReadOnlyList<PokemonFormLabelFamily> families)
    {
        return new FormLabelDefinition(speciesId, speciesNames, forms, baseLabel, families);
    }

    private static (int Form, string Label)[] CreateLetterFormLabels()
    {
        var labels = new (int, string)[28];
        for (var form = 0; form < labels.Length; form++)
        {
            labels[form] = form switch
            {
                26 => (form, "Question Mark"),
                27 => (form, "Exclamation Mark"),
                _ => (form, ((char)('A' + form)).ToString()),
            };
        }

        return labels;
    }

    private static (int Form, string Label)[] CreateArceusFormLabels()
    {
        return
        [
            (0, "Normal Type"),
            (1, "Fighting Type"),
            (2, "Flying Type"),
            (3, "Poison Type"),
            (4, "Ground Type"),
            (5, "Rock Type"),
            (6, "Bug Type"),
            (7, "Ghost Type"),
            (8, "Steel Type"),
            (9, "Fire Type"),
            (10, "Water Type"),
            (11, "Grass Type"),
            (12, "Electric Type"),
            (13, "Psychic Type"),
            (14, "Ice Type"),
            (15, "Dragon Type"),
            (16, "Dark Type"),
            (17, "Fairy Type"),
        ];
    }

    private static (int Form, string Label)[] CreateSeasonFormLabels()
    {
        return
        [
            (0, "Spring Form"),
            (1, "Summer Form"),
            (2, "Autumn Form"),
            (3, "Winter Form"),
        ];
    }

    private static (int Form, string Label)[] CreateFlowerColorLabels()
    {
        return
        [
            (0, "Red Flower"),
            (1, "Yellow Flower"),
            (2, "Orange Flower"),
            (3, "Blue Flower"),
            (4, "White Flower"),
        ];
    }

    private static (int Form, string Label)[] CreateVivillonPatternLabels()
    {
        return
        [
            (0, "Icy Snow Pattern"),
            (1, "Polar Pattern"),
            (2, "Tundra Pattern"),
            (3, "Continental Pattern"),
            (4, "Garden Pattern"),
            (5, "Elegant Pattern"),
            (6, "Meadow Pattern"),
            (7, "Modern Pattern"),
            (8, "Marine Pattern"),
            (9, "Archipelago Pattern"),
            (10, "High Plains Pattern"),
            (11, "Sandstorm Pattern"),
            (12, "River Pattern"),
            (13, "Monsoon Pattern"),
            (14, "Savanna Pattern"),
            (15, "Sun Pattern"),
            (16, "Ocean Pattern"),
            (17, "Jungle Pattern"),
            (18, "Fancy Pattern"),
            (19, "Poke Ball Pattern"),
        ];
    }

    private static (int Form, string Label)[] CreatePumpkinSizeLabels()
    {
        return
        [
            (0, "Average Size"),
            (1, "Small Size"),
            (2, "Large Size"),
            (3, "Super Size"),
        ];
    }

    private static (int Form, string Label)[] CreateMiniorFormLabels()
    {
        return
        [
            (0, "Red Meteor"),
            (1, "Orange Meteor"),
            (2, "Yellow Meteor"),
            (3, "Green Meteor"),
            (4, "Blue Meteor"),
            (5, "Indigo Meteor"),
            (6, "Violet Meteor"),
            (7, "Red Core"),
            (8, "Orange Core"),
            (9, "Yellow Core"),
            (10, "Green Core"),
            (11, "Blue Core"),
            (12, "Indigo Core"),
            (13, "Violet Core"),
        ];
    }

    private static (int Form, string Label)[] CreateSilvallyFormLabels()
    {
        return
        [
            (0, "Normal Type"),
            (1, "Fighting Type"),
            (2, "Flying Type"),
            (3, "Poison Type"),
            (4, "Ground Type"),
            (5, "Rock Type"),
            (6, "Bug Type"),
            (7, "Ghost Type"),
            (8, "Steel Type"),
            (9, "Fire Type"),
            (10, "Water Type"),
            (11, "Grass Type"),
            (12, "Electric Type"),
            (13, "Psychic Type"),
            (14, "Ice Type"),
            (15, "Dragon Type"),
            (16, "Dark Type"),
            (17, "Fairy Type"),
        ];
    }

    private static (int Form, string Label)[] CreateAlcremieFormLabels()
    {
        var creams = new[]
        {
            "Vanilla Cream",
            "Ruby Cream",
            "Matcha Cream",
            "Mint Cream",
            "Lemon Cream",
            "Salted Cream",
            "Ruby Swirl",
            "Caramel Swirl",
            "Rainbow Swirl",
        };
        var sweets = new[]
        {
            "Strawberry Sweet",
            "Berry Sweet",
            "Love Sweet",
            "Star Sweet",
            "Clover Sweet",
            "Flower Sweet",
            "Ribbon Sweet",
        };

        var labels = new List<(int, string)>();
        for (var creamIndex = 0; creamIndex < creams.Length; creamIndex++)
        {
            for (var sweetIndex = 0; sweetIndex < sweets.Length; sweetIndex++)
            {
                labels.Add((
                    creamIndex * sweets.Length + sweetIndex,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{creams[creamIndex]} / {sweets[sweetIndex]}")));
            }
        }

        return labels.ToArray();
    }

    private static string NormalizeSpeciesName(string speciesName)
    {
        return new string(
            speciesName
                .Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private sealed record FormLabelDefinition(
        int SpeciesId,
        IReadOnlyList<string> SpeciesNames,
        IReadOnlyList<(int Form, string Label)> Forms,
        string? BaseLabel,
        IReadOnlyList<PokemonFormLabelFamily> Families);
}
