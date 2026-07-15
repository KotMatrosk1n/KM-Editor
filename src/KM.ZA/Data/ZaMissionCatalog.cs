// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.Data;

internal enum ZaMissionKind
{
    Main,
    Hyperspace,
    Side,
    ExtraSide,
}

internal enum ZaMissionTitleTable
{
    QuestListMain,
    QuestListDlc,
    QuestListSub,
}

internal sealed record ZaMissionDescriptor(
    ZaMissionKind Kind,
    int Number,
    ZaMissionTitleTable TitleTable,
    int TitleMessageIndex,
    string EnglishTitle,
    int? InternalSideId = null)
{
    public string DisplayReference => Kind switch
    {
        ZaMissionKind.Main => $"Main Mission {Number}",
        ZaMissionKind.Hyperspace => $"Hyperspace Mission {Number}",
        ZaMissionKind.Side => $"Side Mission {Number}",
        ZaMissionKind.ExtraSide => $"Side Mission EX{Number}",
        _ => $"Mission {Number}",
    };

    public string ResolveTitle(string? localizedTitle)
    {
        return string.IsNullOrWhiteSpace(localizedTitle)
            || localizedTitle.Contains("[VAR BDFF", StringComparison.OrdinalIgnoreCase)
            || localizedTitle.Contains("[~ ", StringComparison.Ordinal)
                ? EnglishTitle
                : localizedTitle.Trim();
    }
}

internal static class ZaMissionCatalog
{
    private static readonly ZaMissionDescriptor[] MainMissionEntries =
    [
        Main(1, 6, "Get Your Travel Bag Back!"),
        Main(2, 7, "Escape from the Battle Zone!"),
        Main(3, 8, "A New Life in Lumiose City"),
        Main(4, 9, "Battling in the Z-A Royale"),
        Main(5, 10, "The City in the Shadow of Prism Tower"),
        Main(6, 11, "Reaching Rank X"),
        Main(7, 12, "Reaching Rank W"),
        Main(8, 13, "Reaching Rank V"),
        Main(9, 14, "Chase That Mysterious Pokémon!"),
        Main(10, 15, "The Secrets of Mega Evolution"),
        Main(11, 16, "A Rogue Mega Slowbro"),
        Main(12, 17, "A Rogue Mega Camerupt"),
        Main(13, 18, "A Rogue Mega Victreebel"),
        Main(14, 19, "Reaching Rank E"),
        Main(15, 20, "A Job for Team MZ!"),
        Main(16, 22, "A Rogue Mega Beedrill"),
        Main(17, 23, "A Rogue Mega Hawlucha"),
        Main(18, 24, "A Rogue Mega Banette"),
        Main(19, 21, "Reaching Rank D"),
        Main(20, 25, "A Request from the Rust Syndicate"),
        Main(21, 26, "A Rogue Mega Mawile"),
        Main(22, 27, "A Rogue Mega Barbaracle"),
        Main(23, 28, "A Rogue Mega Ampharos"),
        Main(24, 29, "Reaching Rank C"),
        Main(25, 30, "A Showdown on the Battle Court"),
        Main(26, 31, "An Invitation from the SBC"),
        Main(27, 32, "A Rogue Mega Froslass"),
        Main(28, 33, "A Rogue Mega Altaria"),
        Main(29, 34, "A Rogue Mega Venusaur"),
        Main(30, 35, "Reaching Rank B"),
        Main(31, 36, "A Summons from Vinnie"),
        Main(32, 37, "A Rogue Mega Dragonite"),
        Main(33, 38, "A Rogue Mega Tyranitar"),
        Main(34, 39, "A Rogue Mega Starmie"),
        Main(35, 40, "Reaching Rank A"),
        Main(36, 41, "Prism Tower’s Dark Turn"),
        Main(37, 42, "Operation Protect Lumiose"),
        Main(38, 43, "The Future of Lumiose City"),
        Main(39, 44, "The Infinite Z-A Royale"),
        Main(40, 45, "The One That Gives"),
        Main(41, 46, "The One That Takes"),
        Main(42, 47, "To Keep the World in Balance"),
    ];

    private static readonly ZaMissionDescriptor[] HyperspaceMissionEntries =
    [
        Hyperspace(1, "Hyperspace Lumiose Survey No. 1"),
        Hyperspace(2, "Lebanne Arrives with a Bang!"),
        Hyperspace(3, "Hyperspace Lumiose Survey No. 2"),
        Hyperspace(4, "Le Musée et le Café"),
        Hyperspace(5, "A Boom from the Strategy Room"),
        Hyperspace(6, "Hyperspace Lumiose Survey No. 3"),
        Hyperspace(7, "Naveen’s Not OK"),
        Hyperspace(8, "Hyperspace Lumiose Survey No. 4"),
        Hyperspace(9, "Hyperspace Lumiose Survey No. 5"),
        Hyperspace(10, "Mayhem at Midnight"),
        Hyperspace(11, "Hyperspace Lumiose Survey No. 6"),
        Hyperspace(12, "The Greatest Gift"),
        Hyperspace(13, "A Ruby-Red Legend"),
        Hyperspace(14, "A Sapphire-Blue Legend"),
    ];

    // Internal side-quest IDs are implementation order, not the mission numbers
    // shown to players. Most also have an sk/sub_NNN script. Internal ID 122 is
    // present in quest metadata but has no standalone script in the current assets.
    private static readonly ZaMissionDescriptor[] NumberedSideMissionEntries =
    [
        Side(1, 2, "A Big Ol’ Bunnelby"),
        Side(2, 1, "A Use for an Evolution Stone!"),
        Side(3, 3, "Sableye in the Cemetery"),
        Side(4, 53, "A Break Time Battle"),
        Side(5, 6, "I’d Like to See an Ekans!"),
        Side(6, 4, "Long-Range Moves Have Style"),
        Side(7, 111, "A Feisty Chespin"),
        Side(8, 112, "Get Well, Fennekin"),
        Side(9, 113, "A Challenge from Froakie"),
        Side(10, 12, "Skiddo’s Fragrant Leaves"),
        Side(11, 7, "The Kakuna Master"),
        Side(12, 89, "The Many Flowers of Flabébé"),
        Side(13, 98, "Stumped at the Fountain"),
        Side(14, 8, "Slurpuff’s Café Visit"),
        Side(15, 9, "A Sensitive Audino"),
        Side(16, 11, "The Budew Show"),
        Side(17, 74, "A Shiny Mareep?"),
        Side(18, 16, "A Pan-tastic Pot of Tea"),
        Side(19, 10, "Poisonous, Paralyzing Strategies"),
        Side(20, 121, "A Berry Clever Plan"),
        Side(21, 104, "Spewpa in the Museum"),
        Side(22, 118, "A Call from Mable"),
        Side(23, 55, "Underneath the Holovator"),
        Side(24, 14, "An Abra Playmate"),
        Side(25, 26, "Trubblesome Patrons"),
        Side(26, 22, "Burn, Litleo, Burn"),
        Side(27, 30, "Restored from a Fossil"),
        Side(28, 65, "Who Says Normal Is Weak?"),
        Side(29, 81, "Full Course of Battles: One Star"),
        Side(30, 85, "Show Me a Mega Camerupt!"),
        Side(31, 86, "Show Me a Mega Sableye!"),
        Side(32, 87, "Show Me a Mega Medicham!"),
        Side(33, 24, "Who Has the Bigger Magikarp?"),
        Side(34, 21, "Moves That Put Up a Wall"),
        Side(35, 96, "Guidance from a Yoga Master"),
        Side(36, 20, "Some Friendly Competition"),
        Side(37, 95, "Binacle by the Boatload"),
        Side(38, 19, "Chasing Status"),
        Side(39, 18, "Slowpoke for Slowpoke"),
        Side(40, 52, "A Holovator Without Power"),
        Side(41, 34, "Watch Out for Traps"),
        Side(42, 5, "A Fan of Fairy Types"),
        Side(43, 23, "A Big Weedle Problem"),
        Side(44, 13, "Vanillite’s Fragrant Snow"),
        Side(45, 51, "On Maintenance Duty"),
        Side(46, 35, "Pidgeot Soaring High"),
        Side(47, 31, "Becoming a Furfrou Trimmer"),
        Side(48, 75, "All Tied Up"),
        Side(49, 50, "Hit and Heal"),
        Side(50, 17, "Just a Few Questions for You..."),
        Side(51, 90, "Floette Frolicking with Flowers"),
        Side(52, 105, "Numel Frozen Solid"),
        Side(53, 36, "The Most Electrifying Eelektrik"),
        Side(54, 37, "Get ENERGIZED!"),
        Side(55, 78, "Carvanha, Menace of the Deep!"),
        Side(56, 59, "We’ll Just Muscle Our Way Through!"),
        Side(57, 60, "The Camerupt Entrepreneur"),
        Side(58, 38, "Better to Detect Than to Protect"),
        Side(59, 93, "A Rematch with Hawlucha!"),
        Side(60, 82, "Full Course of Battles: Two Stars"),
        Side(61, 54, "My Favorite Holovator"),
        Side(62, 32, "Becoming a Pro Furfrou Trimmer"),
        Side(63, 15, "An Extra-Large Gogoat"),
        Side(64, 40, "Let It Rain, Let It Pour"),
        Side(65, 28, "Apartment Block Eeriness"),
        Side(66, 76, "Investigating with Shuppet"),
        Side(67, 29, "Sylveon the Soother"),
        Side(68, 42, "The Best Use for Leftovers"),
        Side(69, 39, "A Sky Battle, for Old Times’ Sake"),
        Side(70, 25, "Who’s the Strongest, Huh?!"),
        Side(71, 56, "The Burning Gaze of Watchog"),
        Side(72, 43, "Find My Galarian Stunfisk!"),
        Side(73, 84, "Full Course of Battles: High Rolling"),
        Side(74, 27, "Delibird Gets in a Flap"),
        Side(75, 49, "Some Unusual Pokémon"),
        Side(76, 77, "Let’s Learn About Mega Evolution!"),
        Side(77, 107, "Catch Mawile If You Can"),
        Side(78, 44, "Inkay’s Fragrant Ink"),
        Side(79, 48, "A Fateful Swing of a Metronome"),
        Side(80, 57, "A Shocking Territorial Dispute"),
        Side(81, 103, "Pancham the Courier"),
        Side(82, 110, "Clauncher Launching Water Gun"),
        Side(83, 68, "Honedge’s Cutting Edge"),
        Side(84, 47, "Strike First to Make ’Em Flinch!"),
        Side(85, 109, "Follow Litwick!"),
        Side(86, 91, "Who Messed Up the Garden?"),
        Side(87, 33, "Becoming a Peerless Furfrou Trimmer"),
        Side(88, 46, "The Nervous Novice Cabbie"),
        Side(89, 115, "Up, Up, and Away After Emolga!"),
        Side(90, 99, "Froslass’s Unfinished Business"),
        Side(91, 62, "Dragon You into Battle"),
        Side(92, 58, "The Beldum Blockade"),
        Side(93, 108, "Finding a Place for Heliolisk"),
        Side(94, 83, "Full Course of Battles: Three Stars"),
        Side(95, 66, "A Haunting Experience"),
        Side(96, 41, "Let Us Battle...Artistically"),
        Side(97, 97, "Stop the Runaway Whirlipede!"),
        Side(98, 92, "Jumbo Variety Pumpkaboo"),
        Side(99, 45, "Pleasing Aron’s Palate"),
        Side(100, 100, "Starmie on High"),
        Side(101, 67, "Steadfast as Steel"),
        Side(102, 63, "A Chilling Challenge"),
        Side(103, 61, "Facing the Furfrou League"),
        Side(104, 64, "Abuzz About Bug Types"),
        Side(105, 79, "Trevenant, the Haunted Elder Tree!"),
        Side(106, 94, "Klefki’s Lost Key"),
        Side(107, 71, "The World’s Greatest Pikachu!"),
        Side(108, 72, "Alola, Raichu!"),
        Side(109, 69, "Wondrous Self-Healing Pokémon"),
        Side(110, 70, "A Tune That Beckons Doom"),
        Side(111, 73, "My Adorable, Adorable Babies"),
        Side(112, 102, "Exploring the Scents of Spritzee"),
        Side(113, 116, "Bergmite sur un Avalugg"),
        Side(114, 101, "A Feather from Skarmory"),
        Side(115, 80, "Tyrantrum’s Furious Jaws!"),
        Side(116, 106, "Show the Power of Aurorus!"),
        Side(117, 114, "Josée’s Training"),
        Side(118, 117, "Goodbye, Gengar"),
        Side(119, 88, "Le Super-Tournoi de Jacinthe O"),
        Side(120, 122, "Donuts of Unworldly Deliciousness!"),
        Side(121, 123, "A Big Ol’ Battle"),
        Side(122, 154, "Let’s Golden Goooooo!"),
        Side(123, 190, "Charging Toward Victory"),
        Side(124, 191, "Multistrike, Multistrike, Multistrike Moves!"),
        Side(125, 189, "My Hasty Jolteon"),
        Side(126, 192, "Cyclizoom"),
        Side(127, 152, "Mime Jr.’s First Big Job"),
        Side(128, 137, "A Novel Adventure"),
        Side(129, 138, "A Work of Great Love"),
        Side(130, 139, "A Tale of Mystery"),
        Side(131, 135, "Rouge District’s Utility Hole Covers?"),
        Side(132, 172, "Bitter Blue Flames vs. Blazing Crimson"),
        Side(133, 176, "Farfetch’d Ambush!"),
        Side(134, 184, "Squawking, Squabbling Squawkabilly!"),
        Side(135, 187, "Cubone’s Survey"),
        Side(136, 156, "Fidough Loves Walks!"),
        Side(137, 159, "Fungi-ble Goods"),
        Side(138, 149, "Octolock Away the Pain!"),
        Side(139, 124, "The Dauntless Raichu Duo"),
        Side(140, 125, "A Message from Across Dimensions"),
        Side(141, 181, "Rogue Mega Showdown"),
        Side(142, 153, "That’s Some Nacli Coffee"),
        Side(143, 128, "Which Meowth Do You Purrfer?"),
        Side(144, 132, "Imitation Is the Sincerest Form of Flattery"),
        Side(145, 130, "My Neighbor Tinkatuff"),
        Side(146, 157, "Terror in the Sands"),
        Side(147, 134, "Our Gluttonous Gulpin"),
        Side(148, 126, "A Sorta Scary Cemetery Story"),
        Side(149, 131, "Scovillain’s Spice-Off"),
        Side(150, 174, "Corvisquire’s Search"),
        Side(151, 161, "Hyperspatial Scuffle vs. DYN4MO!"),
        Side(152, 162, "Hyperspatial Scuffle vs. the Fist of Justice!"),
        Side(153, 163, "Hyperspatial Scuffle vs. the Rust Syndicate!"),
        Side(154, 164, "Hyperspatial Scuffle vs. the SBC!"),
        Side(155, 165, "Hyperspatial Scuffle vs. Team Flare Nouveau!"),
        Side(156, 151, "A Special Seviper"),
        Side(157, 178, "What’s Wafting from Wigglytuff?"),
        Side(158, 183, "The Trainer Tipster"),
        Side(159, 160, "Who Nicked That Snack?"),
        Side(160, 173, "Feebas’s New Friends"),
        Side(161, 175, "Frigibax’s Friend-Finding"),
        Side(162, 195, "Maybe Morpeko Guards?"),
        Side(163, 136, "Help Us Pick a Name!"),
        Side(164, 155, "Kiosk Conundrum"),
        Side(165, 180, "The Lumiose Museum Heist"),
        Side(166, 193, "The Joy of Mimicry"),
        Side(167, 133, "Porygon’s Polygon Count"),
        Side(168, 150, "Rêve de Musharna"),
        Side(169, 129, "A Gallant Indeedee"),
        Side(170, 127, "Rotom Showcase!"),
        Side(171, 177, "Awaken, Cofagrigus!"),
        Side(172, 171, "Sawk vs. Throh"),
        Side(173, 147, "Be a Defenseless Dodger!"),
        Side(174, 148, "I Still Remember the Taste..."),
        Side(175, 188, "Romance avec Purrloin"),
        Side(176, 186, "Ultimate Beans of Supreme Ultimacy"),
        Side(177, 185, "Triste Dreams of Tatsugiri"),
        Side(178, 158, "Dondozo Down in the Dumps"),
        Side(179, 179, "A Sub-30-Second Loss?!"),
        Side(180, 182, "A Wild Rosebud"),
        Side(181, 166, "Hyperspatial Slugfest vs. DYN4MO!"),
        Side(182, 167, "Hyperspatial Slugfest vs. the Fist of Justice!"),
        Side(183, 168, "Hyperspatial Slugfest vs. the Rust Syndicate!"),
        Side(184, 169, "Hyperspatial Slugfest vs. the SBC!"),
        Side(185, 170, "Hyperspatial Slugfest vs. Team Flare Nouveau!"),
        Side(186, 194, "The Ultimate Techniques"),
        Side(187, 145, "The Dauntless Dragalge Trainer"),
        Side(188, 143, "Start Special Scanning!"),
        Side(189, 198, "Siblings of the Sky"),
        Side(190, 146, "A Mimikyu for My Cutie"),
        Side(191, 196, "Collecting Four Drives"),
        Side(192, 197, "The Stealthy Shadow"),
        Side(193, 200, "Dreams of Meltan"),
        Side(194, 201, "Volcanion Unleashed"),
        Side(195, 199, "Restarting Magearna"),
        Side(196, 202, "The Djinn Unbound"),
        Side(197, 144, "Ultra-Hardcore Lucario Showdown!"),
        Side(198, 141, "Lida’s Lament"),
        Side(199, 142, "Naveen’s Newfound Determination"),
        Side(200, 140, "Here in Lumiose City"),
    ];

    private static readonly ZaMissionDescriptor[] ExtraSideMissionEntries =
    [
        ExtraSide(1, 119, "Shine Bright like a Gemstone"),
        ExtraSide(2, 120, "Project M"),
        ExtraSide(3, 203, "Raging Lightning"),
    ];

    private static readonly ZaMissionDescriptor[] AllSideMissionEntries =
        [.. NumberedSideMissionEntries, .. ExtraSideMissionEntries];

    private static readonly IReadOnlyDictionary<int, ZaMissionDescriptor> MainMissionsByNumber =
        MainMissionEntries.ToDictionary(mission => mission.Number);

    private static readonly IReadOnlyDictionary<int, ZaMissionDescriptor> HyperspaceMissionsByNumber =
        HyperspaceMissionEntries.ToDictionary(mission => mission.Number);

    private static readonly IReadOnlyDictionary<int, ZaMissionDescriptor> NumberedSideMissionsByNumber =
        NumberedSideMissionEntries.ToDictionary(mission => mission.Number);

    private static readonly IReadOnlyDictionary<int, ZaMissionDescriptor> ExtraSideMissionsByNumber =
        ExtraSideMissionEntries.ToDictionary(mission => mission.Number);

    private static readonly IReadOnlyDictionary<int, ZaMissionDescriptor> SideMissionsByInternalId =
        AllSideMissionEntries.ToDictionary(mission => mission.InternalSideId!.Value);

    public static IReadOnlyList<ZaMissionDescriptor> MainMissions => MainMissionEntries;

    public static IReadOnlyList<ZaMissionDescriptor> HyperspaceMissions => HyperspaceMissionEntries;

    public static IReadOnlyList<ZaMissionDescriptor> NumberedSideMissions => NumberedSideMissionEntries;

    public static IReadOnlyList<ZaMissionDescriptor> ExtraSideMissions => ExtraSideMissionEntries;

    public static IReadOnlyList<ZaMissionDescriptor> AllSideMissions => AllSideMissionEntries;

    public static bool TryGetMainMission(int number, out ZaMissionDescriptor mission) =>
        MainMissionsByNumber.TryGetValue(number, out mission!);

    public static bool TryGetHyperspaceMission(int number, out ZaMissionDescriptor mission) =>
        HyperspaceMissionsByNumber.TryGetValue(number, out mission!);

    public static bool TryGetNumberedSideMission(int number, out ZaMissionDescriptor mission) =>
        NumberedSideMissionsByNumber.TryGetValue(number, out mission!);

    public static bool TryGetExtraSideMission(int number, out ZaMissionDescriptor mission) =>
        ExtraSideMissionsByNumber.TryGetValue(number, out mission!);

    public static bool TryGetSideMissionByInternalId(int internalId, out ZaMissionDescriptor mission) =>
        SideMissionsByInternalId.TryGetValue(internalId, out mission!);

    private static ZaMissionDescriptor Main(int number, int titleMessageIndex, string englishTitle) =>
        new(ZaMissionKind.Main, number, ZaMissionTitleTable.QuestListMain, titleMessageIndex, englishTitle);

    private static ZaMissionDescriptor Hyperspace(int number, string englishTitle) =>
        new(ZaMissionKind.Hyperspace, number, ZaMissionTitleTable.QuestListDlc, number, englishTitle);

    private static ZaMissionDescriptor Side(int number, int internalId, string englishTitle) =>
        new(ZaMissionKind.Side, number, ZaMissionTitleTable.QuestListSub, internalId + 1, englishTitle, internalId);

    private static ZaMissionDescriptor ExtraSide(int number, int internalId, string englishTitle) =>
        new(ZaMissionKind.ExtraSide, number, ZaMissionTitleTable.QuestListSub, internalId + 1, englishTitle, internalId);
}
