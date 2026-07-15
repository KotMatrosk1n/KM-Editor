// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.SwSh.NpcItemGift;

internal static class SwShNpcItemGiftDefinitions
{
    private const string AmxRoot = "romfs/bin/script/amx/";

    internal static readonly IReadOnlyList<SwShNpcItemGiftDefinition> All =
    [
        Gift("mum-postwick-poke-ball", "mum", "Mum", "Mum (Postwick)", "Postwick", 10, "main_event_0180.amx", 5118, 5, Slot("item", "Poke Ball", 5119, 4)),
        Gift("lab-guide-wedgehurst-potion", "guide", "Guide", "Guide (Wedgehurst Pokemon Lab)", "Wedgehurst", 20, "main_event_0215.amx", 2845, 1, Slot("item", "Potion", 2846, 17, 2740), companionQuantityCells: [2739]),
        Gift("leon-route-2-poke-ball", "leon", "Leon", "Leon (Route 2)", "Route 2", 30, "main_event_0250.amx", 6119, 20, Slot("item", "Poke Ball", 6120, 4, 5764), companionQuantityCells: [5763]),
        Gift("leon-postwick-endorsement", "leon", "Leon", "Leon (Postwick endorsement)", "Postwick", 40, "main_event_0300.amx", 5758, 1, Slot("item", "Endorsement", 5759, 1074, 5425), companionQuantityCells: [5424]),
        Gift("hop-postwick-wishing-star", "hop", "Hop", "Hop (Postwick Wishing Star)", "Postwick", 45, "main_event_0310.amx", 6816, 1, Slot("item", "Wishing Star", 6817, 1076, 6094), companionQuantityCells: [6093]),
        Gift("professor-magnolia-route-2-dynamax-band", "professor-magnolia", "Professor Magnolia", "Professor Magnolia (Route 2)", "Route 2", 50, "main_event_0330.amx", 6232, 1, Slot("item", "Dynamax Band", 6233, 1077, 5864)),
        Gift("hop-route-2-tm40", "hop", "Hop", "Hop (Route 2)", "Route 2", 60, "main_event_0340.amx", 6059, 1, Slot("item", "TM40", 6060, 367, 5916), companionQuantityCells: [5915]),
        Gift("mum-wedgehurst-camping-gear", "mum", "Mum", "Mum (Wedgehurst Station)", "Wedgehurst Station", 70, "main_event_0350.amx", 6095, 1, Slot("item", "Camping Gear", 6096, 1100, 5683), companionQuantityCells: [5682]),
        Gift("mum-wedgehurst-cheri-berry", "mum", "Mum", "Mum (Wedgehurst Station)", "Wedgehurst Station", 71, "main_event_0350.amx", 6100, 1, Slot("item", "Cheri Berry", 6101, 149)),
        Gift("mum-wedgehurst-oran-berry", "mum", "Mum", "Mum (Wedgehurst Station)", "Wedgehurst Station", 72, "main_event_0350.amx", 6105, 1, Slot("item", "Oran Berry", 6106, 155)),
        Gift("sonia-wild-area-station-box-link", "sonia", "Sonia", "Sonia (Wild Area Station)", "Wild Area Station", 80, "main_event_0370.amx", 6854, 1, Slot("item", "Pokemon Box Link", 6855, 1075, 6444), companionQuantityCells: [6443]),
        Gift("leon-motostoke-starter-held-item", "leon", "Leon", "Leon (Motostoke)", "Motostoke", 90, "main_event_0412.amx", 4823, 1,
            [
                Slot("water-starter", "Water starter", 4824, 243, 4590),
                Slot("fire-starter", "Fire starter", 4825, 249, 4591),
                Slot("grass-starter", "Grass starter", 4826, 239, 4592),
            ], companionQuantityCells: [4589]),
        Gift("sonia-route-3-escape-rope", "sonia", "Sonia", "Sonia (Route 3)", "Route 3", 100, "main_event_0530.amx", 3621, 1, Slot("item", "Escape Rope", 3622, 78, 3424), companionQuantityCells: [3423]),
        Gift("gym-attendant-turffield-revive", "gym-attendant", "Gym Attendant", "Gym Attendant (Turffield Gym)", "Turffield Stadium", 110, "main_event_0570.amx", 4360, 2, Slot("item", "Revive", 4361, 28, 4221), companionQuantityCells: [4220]),
        Gift("milo-turffield-tm10", "milo", "Milo", "Milo (Turffield Gym)", "Turffield Stadium", 120, "main_event_0610.amx", 8556, 1, Slot("item", "TM10", 8557, 337, 8235), companionQuantityCells: [8234]),
        Gift("bike-doctor-route-5-rotom-bike", "bike-doctor", "Bike Doctor", "Bike Doctor (Route 5)", "Route 5", 130, "main_event_0620.amx", 5959, 1, Slot("item", "Rotom Bike", 5960, 1081, 5865), companionQuantityCells: [5864]),
        Gift("hop-route-5-revive", "hop", "Hop", "Hop (Route 5)", "Route 5", 140, "main_event_0630.amx", 5931, 1, Slot("item", "Revive", 5932, 28, 5744), companionQuantityCells: [5743]),
        Gift("nessa-hulbury-tm36", "nessa", "Nessa", "Nessa (Hulbury Gym)", "Hulbury Stadium", 150, "main_event_0670.amx", 8159, 1, Slot("item", "TM36", 8160, 363, 8077), companionQuantityCells: [8076]),
        Gift("sonia-hulbury-tm79", "sonia", "Sonia", "Sonia (Hulbury)", "Hulbury", 160, "main_event_0690.amx", 2774, 1, Slot("item", "TM79", 2775, 406, 2617), companionQuantityCells: [2616]),
        Gift("marnie-motostoke-burn-heal", "marnie", "Marnie", "Marnie (Motostoke)", "Motostoke", 170, "main_event_0750.amx", 4802, 2, Slot("item", "Burn Heal", 4803, 19, 4621), companionQuantityCells: [4620]),
        Gift("kabu-motostoke-tm38", "kabu", "Kabu", "Kabu (Motostoke Gym)", "Motostoke Stadium", 180, "main_event_0800.amx", 5979, 1, Slot("item", "TM38", 5980, 365, 5707), companionQuantityCells: [5706]),
        Gift("sonia-hammerlocke-revive", "sonia", "Sonia", "Sonia (Hammerlocke)", "Hammerlocke", 190, "main_event_0900.amx", 6103, 2, Slot("item", "Revive", 6104, 28, 6014), companionQuantityCells: [6013]),
        Gift("bea-stow-on-side-tm42", "bea", "Bea", "Bea (Stow-on-Side Gym)", "Stow-on-Side Stadium", 200, "main_event_0990.amx", 7558, 1, Slot("item", "TM42", 7559, 369, 7456), ProjectGame.Sword, companionQuantityCells: [7455]),
        Gift("allister-stow-on-side-tm77", "allister", "Allister", "Allister (Stow-on-Side Gym)", "Stow-on-Side Stadium", 200, "main_event_1000.amx", 7519, 1, Slot("item", "TM77", 7520, 404, 7417), ProjectGame.Shield, companionQuantityCells: [7416]),
        Gift("sonia-stow-on-side-revive", "sonia", "Sonia", "Sonia (Stow-on-Side)", "Stow-on-Side", 210, "main_event_1110.amx", 5246, 2, Slot("item", "Revive", 5247, 28, 5131), companionQuantityCells: [5130]),
        Gift("opal-ballonlea-tm87", "opal", "Opal", "Opal (Ballonlea Gym)", "Ballonlea Stadium", 220, "main_event_1140.amx", 7937, 1, Slot("item", "TM87", 7938, 414, 7740), companionQuantityCells: [7739]),
        Gift("gordie-circhester-tm48", "gordie", "Gordie", "Gordie (Circhester Gym)", "Circhester Stadium", 230, "main_event_1242.amx", 7508, 1, Slot("item", "TM48", 7509, 375, 7426), ProjectGame.Sword, companionQuantityCells: [7425]),
        Gift("melony-circhester-tm27", "melony", "Melony", "Melony (Circhester Gym)", "Circhester Stadium", 230, "main_event_1252.amx", 7586, 1, Slot("item", "TM27", 7587, 354, 7504), ProjectGame.Shield, companionQuantityCells: [7503]),
        Gift("bike-doctor-route-9-rotom-bike", "bike-doctor", "Bike Doctor", "Bike Doctor (Route 9)", "Route 9", 240, "main_event_1300.amx", 6809, 1, Slot("item", "Rotom Bike", 6810, 1266, 6522), companionQuantityCells: [6521]),
        Gift("piers-spikemuth-tm85", "piers", "Piers", "Piers (Spikemuth Gym)", "Spikemuth", 250, "main_event_1390.amx", 6062, 1, Slot("item", "TM85", 6063, 412, 5197), companionQuantityCells: [5196]),
        Gift("raihan-hammerlocke-tm99", "raihan", "Raihan", "Raihan (Hammerlocke Gym)", "Hammerlocke Stadium", 260, "main_event_1430.amx", 8599, 1, Slot("item", "TM99", 8600, 693, 8093), companionQuantityCells: [8092]),
        Gift("sonia-slumbering-weald-max-revive", "sonia", "Sonia", "Sonia (Slumbering Weald)", "Slumbering Weald", 270, "main_event_1820.amx", 6775, 3, Slot("item", "Max Revive", 6776, 29, 6336), companionQuantityCells: [6335]),
        Gift("sonia-postgame-book", "sonia", "Sonia", "Sonia (Postgame Slumbering Weald)", "Slumbering Weald", 280, "main_event_3010.amx", 7617, 1, Slot("item", "Sonia's Book", 7618, 1271, 6826), companionQuantityCells: [6825]),
        Gift("professor-magnolia-postgame-master-ball", "professor-magnolia", "Professor Magnolia", "Professor Magnolia (Postgame)", "Postwick", 290, "sub_event_009.amx", 5057, 1, Slot("item", "Master Ball", 5058, 1)),
        Gift("ballonlea-artist-tm78", "artist", "Artist", "Artist (Ballonlea)", "Ballonlea", 300, "sub_event_001.amx", 5184, 1, Slot("item", "TM78", 5185, 405)),
        Gift("nursery-worker-route-5-exp-candy", "nursery-worker", "Nursery Worker", "Nursery Worker (Route 5)", "Route 5", 310, "sub_event_005.amx", 5207, 5, Slot("item", "Exp. Candy XS", 5208, 1124)),
        Gift("fossil-researcher-route-6-drake", "fossil-researcher", "Fossil Researcher", "Fossil Researcher (Route 6)", "Route 6", 320, "sub_event_006.amx", 4966, 2, Slot("item", "Fossilized Drake", 4967, 1107)),
        Gift("fossil-researcher-route-6-bird", "fossil-researcher", "Fossil Researcher", "Fossil Researcher (Route 6)", "Route 6", 321, "sub_event_006.amx", 4986, 2, Slot("item", "Fossilized Bird", 4987, 1105)),
        Gift("league-staff-isaac-rotom-catalog", "league-staff-isaac", "League Staff Isaac", "League Staff Isaac (Wyndon)", "Wyndon", 330, "sub_event_012.amx", 5021, 1, Slot("item", "Rotom Catalog", 5022, 1278)),
        Gift("mr-focus-focus-sash", "mr-focus", "Mr. Focus", "Mr. Focus (Stow-on-Side)", "Stow-on-Side", 340, "sub_event_013.amx", 5021, 1, Slot("item", "Focus Sash", 5022, 275)),
        Gift("pokemon-breeder-elena-eviolite", "pokemon-breeder-elena", "Pokemon Breeder Elena", "Pokemon Breeder Elena (Ballonlea)", "Ballonlea", 350, "sub_event_015.amx", 5021, 1, Slot("item", "Eviolite", 5022, 538)),
        ItemOnlyGift("hammerlocke-soothe-bell", "soothe-bell-npc", "Soothe Bell NPC", "Soothe Bell NPC (Hammerlocke)", "Hammerlocke", 355, "sub_event_016.amx", Slot("item", "Soothe Bell", 4967, 218)),
        Gift("hammerlocke-tr-npc-tr13", "hammerlocke-tr-npc", "Hammerlocke TR NPC", "Hammerlocke TR NPC", "Hammerlocke", 360, "sub_event_017.amx", 4960, 1, Slot("item", "TR13", 4961, 1143)),
        Gift("weather-npc-heat-rock", "weather-npc", "Weather NPC", "Weather NPC (Hammerlocke)", "Hammerlocke", 370, "sub_event_018.amx", 5095, 1, Slot("item", "Heat Rock", 5096, 284)),
        Gift("weather-npc-damp-rock", "weather-npc", "Weather NPC", "Weather NPC (Hammerlocke)", "Hammerlocke", 371, "sub_event_018.amx", 5235, 1, Slot("item", "Damp Rock", 5236, 285)),
        Gift("weather-npc-icy-rock", "weather-npc", "Weather NPC", "Weather NPC (Hammerlocke)", "Hammerlocke", 372, "sub_event_018.amx", 5375, 1, Slot("item", "Icy Rock", 5376, 282)),
        Gift("weather-npc-smooth-rock", "weather-npc", "Weather NPC", "Weather NPC (Hammerlocke)", "Hammerlocke", 373, "sub_event_018.amx", 5515, 1, Slot("item", "Smooth Rock", 5516, 283)),
        Gift("weather-npc-utility-umbrella", "weather-npc", "Weather NPC", "Weather NPC (Hammerlocke)", "Hammerlocke", 374, "sub_event_018.amx", 5580, 1, Slot("item", "Utility Umbrella", 5581, 1123)),
        ItemOnlyGift("flying-taxi-npc-tm06", "flying-taxi-npc", "Flying Taxi NPC", "Flying Taxi NPC (Hammerlocke)", "Hammerlocke", 375, "sub_event_019.amx", Slot("item", "TM06", 4967, 333)),
        ItemOnlyGift("screech-npc-tm16", "screech-npc", "Screech NPC", "Screech NPC (Circhester)", "Circhester", 376, "sub_event_020.amx", Slot("item", "TM16", 4967, 343)),
        Gift("detective-circhester-wide-lens", "detective", "Detective", "Detective (Circhester)", "Circhester", 380, "sub_event_021.amx", 9930, 1, Slot("item", "Wide Lens", 9931, 265)),
        Gift("hi-tech-earbuds-man", "hi-tech-earbuds-man", "Hi-tech Earbuds Man", "Hi-tech Earbuds Man (Motostoke)", "Motostoke", 390, "sub_event_022.amx", 4960, 1, Slot("item", "Hi-tech Earbuds", 4961, 1255)),
        ItemOnlyGift("fake-tears-npc-tm47", "fake-tears-npc", "Fake Tears NPC", "Fake Tears NPC (Circhester)", "Circhester", 391, "sub_event_023.amx", Slot("item", "TM47", 4967, 374)),
        ItemOnlyGift("secret-beach-npc-tm45", "secret-beach-npc", "Secret Beach NPC", "Secret Beach NPC (Route 9)", "Route 9", 392, "sub_event_024.amx", Slot("item", "TM45", 4974, 372)),
        Gift("paula-old-letter", "paula", "Paula", "Paula (Hammerlocke)", "Hammerlocke", 400, "sub_event_025.amx", 5183, 1, Slot("item", "Old Letter", 5184, 1269)),
        Gift("frank-choice-scarf", "frank", "Frank", "Frank (Ballonlea)", "Ballonlea", 410, "sub_event_025.amx", 5663, 1, Slot("item", "Choice Scarf", 5664, 287)),
        Gift("frank-reaper-cloth", "frank", "Frank", "Frank (Ballonlea)", "Ballonlea", 411, "sub_event_025.amx", 5735, 1, Slot("item", "Reaper Cloth", 5736, 325)),
        Gift("ball-guy-first-talk-poke-ball", "ball-guy", "Ball Guy", "Ball Guy (First talk)", "Stadium", 420, "sub_event_026.amx", 5488, 1, Slot("item", "Poke Ball", 5489, 4)),
        Gift("ball-guy-friend-ball", "ball-guy", "Ball Guy", "Ball Guy (Friend Ball gift)", "Stadium", 430, "sub_event_014.amx", 4898, 1, Slot("item", "Friend Ball", 4899, 497)),
        Gift("ball-guy-lure-ball", "ball-guy", "Ball Guy", "Ball Guy (Lure Ball gift)", "Stadium", 431, "sub_event_014.amx", 5103, 1, Slot("item", "Lure Ball", 5104, 494)),
        Gift("ball-guy-level-ball", "ball-guy", "Ball Guy", "Ball Guy (Level Ball gift)", "Stadium", 432, "sub_event_014.amx", 5308, 1, Slot("item", "Level Ball", 5309, 493)),
        Gift("ball-guy-heavy-ball", "ball-guy", "Ball Guy", "Ball Guy (Heavy Ball gift)", "Stadium", 433, "sub_event_014.amx", 5718, 1, Slot("item", "Heavy Ball", 5719, 495)),
        Gift("ball-guy-love-ball", "ball-guy", "Ball Guy", "Ball Guy (Love Ball gift)", "Stadium", 434, "sub_event_014.amx", 5923, 1, Slot("item", "Love Ball", 5924, 496)),
        Gift("ball-guy-moon-ball-one", "ball-guy", "Ball Guy", "Ball Guy (Moon Ball gift)", "Stadium", 435, "sub_event_014.amx", 6128, 1, Slot("item", "Moon Ball", 6129, 498)),
        Gift("ball-guy-moon-ball-two", "ball-guy", "Ball Guy", "Ball Guy (Second Moon Ball gift)", "Stadium", 436, "sub_event_014.amx", 6333, 1, Slot("item", "Moon Ball", 6334, 498)),
        Gift("ball-guy-dream-ball", "ball-guy", "Ball Guy", "Ball Guy (Dream Ball gift)", "Stadium", 437, "sub_event_014.amx", 6538, 1, Slot("item", "Dream Ball", 6539, 576)),
        ItemOnlyGift("wild-area-star-piece", "star-piece-npc", "Star Piece NPC", "Star Piece NPC (Wild Area)", "Wild Area", 438, "sub_event_027.amx", Slot("item", "Star Piece", 4967, 91)),
        Gift("hulbury-delivery-nugget", "delivery-man", "Delivery Man", "Delivery Man (Hulbury)", "Hulbury", 440, "sub_event_031.amx", 4966, 5, Slot("item", "Nugget", 4967, 92)),
        Gift("hulbury-delivery-big-nugget", "delivery-man", "Delivery Man", "Delivery Man (Hulbury)", "Hulbury", 441, "sub_event_031.amx", 5306, 2, Slot("item", "Big Nugget", 5307, 581)),
        Gift("hulbury-delivery-lucky-egg", "delivery-man", "Delivery Man", "Delivery Man (Hulbury)", "Hulbury", 442, "sub_event_031.amx", 5661, 1, Slot("item", "Lucky Egg", 5662, 231)),
        Gift("hulbury-delivery-exp-candy-l", "delivery-man", "Delivery Man", "Delivery Man (Hulbury)", "Hulbury", 443, "sub_event_031.amx", 6030, 1, Slot("item", "Exp. Candy L", 6031, 1127)),
        Gift("applin-quest-tart-apple", "applin-boy", "Applin Boy", "Applin Boy (Hammerlocke)", "Hammerlocke", 450, "sub_event_034.amx", 7285, 1, Slot("item", "Tart Apple", 7286, 1117), ProjectGame.Sword),
        Gift("applin-quest-sweet-apple", "applin-boy", "Applin Boy", "Applin Boy (Hammerlocke)", "Hammerlocke", 450, "sub_event_034.amx", 7298, 1, Slot("item", "Sweet Apple", 7299, 1116), ProjectGame.Shield),
        ItemOnlyGift("stow-on-side-gym-npc-tm42", "stow-on-side-gym-npc", "Stow-on-Side Gym NPC", "Stow-on-Side Gym NPC (TM42)", "Stow-on-Side", 451, "sub_event_029.amx", Slot("item", "TM42", 4993, 369), ProjectGame.Sword),
        ItemOnlyGift("stow-on-side-gym-npc-tm77", "stow-on-side-gym-npc", "Stow-on-Side Gym NPC", "Stow-on-Side Gym NPC (TM77)", "Stow-on-Side", 451, "sub_event_029.amx", Slot("item", "TM77", 4973, 404), ProjectGame.Shield),
        ItemOnlyGift("circhester-gym-npc-tm48", "circhester-gym-npc", "Circhester Gym NPC", "Circhester Gym NPC (TM48)", "Circhester", 452, "sub_event_030.amx", Slot("item", "TM48", 4993, 375), ProjectGame.Sword),
        ItemOnlyGift("circhester-gym-npc-tm27", "circhester-gym-npc", "Circhester Gym NPC", "Circhester Gym NPC (TM27)", "Circhester", 452, "sub_event_030.amx", Slot("item", "TM27", 4973, 354), ProjectGame.Shield),
        Gift("minccino-child-throat-spray", "minccino-child", "Minccino Child", "Minccino Child (Motostoke)", "Motostoke", 460, "sub_event_042.amx", 5223, 1, Slot("item", "Throat Spray", 5224, 1118)),
        Gift("morimoto-oval-charm", "morimoto", "Morimoto", "GAME FREAK Morimoto (Circhester)", "Circhester", 470, "sub_event_120.amx", 5340, 1, Slot("item", "Oval Charm", 5341, 631)),
        Gift("game-director-catching-charm", "game-director", "Game Director", "Game Director (Circhester)", "Circhester", 480, "comp_director.amx", 4930, 1, Slot("item", "Catching Charm", 4931, 1267)),
        Gift("game-director-shiny-charm", "game-director", "Game Director", "Game Director (Circhester Pokedex completion)", "Circhester", 481, "comp_director.amx", 5229, 1, Slot("item", "Shiny Charm", 5230, 632)),
        ..CreateTypeNullMemoryGifts(),
        Gift("klara-avery-style-card", "klara-avery", "Klara/Avery", "Klara/Avery (Master Dojo entry)", "Master Dojo", 600, "rigel01_main_event_0030.amx", 6941, 1, Slot("item", "Style Card", 6942, 1583)),
        Gift("hyde-exp-charm-first", "hyde", "Hyde", "Hyde (Master Dojo)", "Master Dojo", 610, "rigel01_main_event_0050.amx", 6888, 1, Slot("item", "Exp. Charm", 6889, 1587)),
        Gift("hyde-exp-charm-second", "hyde", "Hyde", "Hyde (Master Dojo follow-up)", "Master Dojo", 611, "rigel01_main_event_0050.amx", 8147, 1, Slot("item", "Exp. Charm", 8148, 1587)),
        Gift("honey-armorite-ore", "honey", "Honey", "Honey (Master Dojo)", "Master Dojo", 620, "rigel01_main_event_0080.amx", 7761, 5, Slot("item", "Armorite Ore", 7762, 1588)),
        Gift("honey-max-honey", "honey", "Honey", "Honey (Honeycalm Island)", "Honeycalm Island", 630, "rigel01_main_event_0305.amx", 6305, 1, Slot("item", "Max Honey", 6306, 1579)),
        Gift("pokedex-scientist-mark-charm", "isle-of-armor-pokedex-scientist", "Pokedex Scientist", "Pokedex Scientist (Isle of Armor)", "Fields of Honor Station", 640, "rigel1_sub_event_001.amx", 6203, 1, Slot("item", "Mark Charm", 6204, 1589)),
        Gift("honey-fruit-bunch", "honey", "Honey", "Honey (Master Dojo ingredients)", "Master Dojo", 650, "rigel1_sub_event_006.amx", 12691, 1, Slot("item", "Fruit Bunch", 12692, 1256)),
        Gift("honey-moomoo-cheese", "honey", "Honey", "Honey (Master Dojo ingredients)", "Master Dojo", 651, "rigel1_sub_event_006.amx", 12707, 1, Slot("item", "Moomoo Cheese", 12708, 1257)),
        Gift("honey-large-leek", "honey", "Honey", "Honey (Master Dojo ingredients)", "Master Dojo", 652, "rigel1_sub_event_006.amx", 12723, 1, Slot("item", "Large Leek", 12724, 1092)),
        Gift("honey-sausages", "honey", "Honey", "Honey (Master Dojo ingredients)", "Master Dojo", 653, "rigel1_sub_event_006.amx", 12739, 1, Slot("item", "Sausages", 12740, 1084)),
        Gift("digging-pa-armorite-ore", "digging-pa", "Digging Pa", "Digging Pa (Training Lowlands)", "Training Lowlands", 660, "rigel1_sub_event_010.amx", 6342, 9, Slot("item", "Armorite Ore", 6344, 1588)),
        Gift("bike-lady-white-bike", "bike-lady", "Bike Lady", "Bike Lady (Fields of Honor)", "Fields of Honor", 670, "rigel1_sub_event_012.amx", 2441, 1, Slot("item", "Rotom Bike", 2442, 1585)),
        Gift("bike-lady-black-bike", "bike-lady", "Bike Lady", "Bike Lady (Fields of Honor)", "Fields of Honor", 671, "rigel1_sub_event_012.amx", 2453, 1, Slot("item", "Rotom Bike", 2454, 1585)),
        Gift("bike-lady-water-white-bike", "bike-lady", "Bike Lady", "Bike Lady (Fields of Honor water mode)", "Fields of Honor", 672, "rigel1_sub_event_012.amx", 2490, 1, Slot("item", "Rotom Bike", 2491, 1586)),
        Gift("bike-lady-water-black-bike", "bike-lady", "Bike Lady", "Bike Lady (Fields of Honor water mode)", "Fields of Honor", 673, "rigel1_sub_event_012.amx", 2502, 1, Slot("item", "Rotom Bike", 2503, 1586)),
        Gift("bike-lady-rotom-bike-one", "bike-lady", "Bike Lady", "Bike Lady (Fields of Honor bike reset)", "Fields of Honor", 674, "rigel1_sub_event_012.amx", 2541, 1, Slot("item", "Rotom Bike", 2542, 1266)),
        Gift("bike-lady-rotom-bike-two", "bike-lady", "Bike Lady", "Bike Lady (Fields of Honor bike reset)", "Fields of Honor", 675, "rigel1_sub_event_012.amx", 2553, 1, Slot("item", "Rotom Bike", 2554, 1266)),
        Gift("galarica-cuff-maker", "galarica-cuff-maker", "Galarica Cuff Maker", "Galarica Cuff Maker (Workout Sea)", "Workout Sea", 680, "rigel1_sub_event_021.amx", 5450, 1, Slot("item", "Galarica Cuff", 5451, 1582)),
        Gift("peony-legendary-clue-one", "peony", "Peony", "Peony (Freezington)", "Freezington", 690, "rigel02_main_event_0080.amx", 8343, 1, Slot("item", "Legendary Clue 1", 8344, 1593)),
        Gift("peony-legendary-clue-two", "peony", "Peony", "Peony (Freezington)", "Freezington", 691, "rigel02_main_event_0080.amx", 8358, 1, Slot("item", "Legendary Clue 2", 8359, 1594)),
        Gift("peony-legendary-clue-three", "peony", "Peony", "Peony (Freezington)", "Freezington", 692, "rigel02_main_event_0080.amx", 8371, 1, Slot("item", "Legendary Clue 3", 8372, 1595)),
        Gift("peony-freezington-master-ball", "peony", "Peony", "Peony (Freezington)", "Freezington", 693, "rigel02_main_event_0080.amx", 9430, 1, Slot("item", "Master Ball", 9431, 1, 9099), companionQuantityCells: [9098]),
        Gift("wooden-crown-first", "crown-tundra-story", "Crown Tundra Story", "Wooden Crown (Freezington)", "Freezington", 700, "rigel02_main_event_0090.amx", 6903, 1, Slot("item", "Wooden Crown", 6904, 1598)),
        Gift("wooden-crown-second", "crown-tundra-story", "Crown Tundra Story", "Wooden Crown (Freezington follow-up)", "Freezington", 701, "rigel02_main_event_0090.amx", 7183, 1, Slot("item", "Wooden Crown", 7184, 1598)),
        Gift("freezington-farmer-carrot-seeds-one", "freezington-farmer", "Freezington Farmer", "Freezington Farmer (Carrot Seeds)", "Freezington", 710, "rigel02_main_event_0126.amx", 5198, 1, Slot("item", "Carrot Seeds", 5199, 1605)),
        Gift("freezington-farmer-carrot-seeds-two", "freezington-farmer", "Freezington Farmer", "Freezington Farmer (Carrot Seeds follow-up)", "Freezington", 711, "rigel02_main_event_0135.amx", 2746, 1, Slot("item", "Carrot Seeds", 2747, 1605)),
        Gift("calyrex-iceroot-carrot", "calyrex-story", "Calyrex Story", "Iceroot Carrot (Crown Tundra)", "Crown Tundra", 720, "rigel02_main_event_0145.amx", 5722, 1, Slot("item", "Iceroot Carrot", 5723, 1602)),
        Gift("calyrex-shaderoot-carrot", "calyrex-story", "Calyrex Story", "Shaderoot Carrot (Crown Tundra)", "Crown Tundra", 721, "rigel02_main_event_0145.amx", 7561, 1, Slot("item", "Shaderoot Carrot", 7562, 1603)),
        Gift("calyrex-radiant-petal", "calyrex-story", "Calyrex Story", "Radiant Petal (Crown Tundra)", "Crown Tundra", 730, "rigel02_main_event_0149.amx", 6507, 1, Slot("item", "Radiant Petal", 6508, 1599)),
        Gift("calyrex-reins-of-unity-one", "calyrex-story", "Calyrex Story", "Reins of Unity (Crown Tundra)", "Crown Tundra", 740, "rigel02_main_event_0150.amx", 8028, 1, Slot("item", "Reins of Unity", 8029, 1607)),
        Gift("calyrex-reins-of-unity-two", "calyrex-story", "Calyrex Story", "Reins of Unity (Crown Tundra variant)", "Crown Tundra", 741, "rigel02_main_event_0200.amx", 5448, 1, Slot("item", "Reins of Unity", 5449, 1591)),
        Gift("legendary-clue-question", "peony", "Peony", "Peony (Legendary Clue?)", "Freezington", 750, "rigel02_main_event_0220.amx", 2403, 1, Slot("item", "Legendary Clue?", 2404, 1596)),
        Gift("crown-pokedex-rare-candy", "crown-tundra-pokedex-scientist", "Pokedex Scientist", "Pokedex Scientist (Crown Tundra)", "Freezington Station", 760, "rigel2_sub_event_001.amx", 6294, 50, Slot("item", "Rare Candy", 6295, 50)),
        Gift("crown-pokedex-gold-bottle-cap", "crown-tundra-pokedex-scientist", "Pokedex Scientist", "Pokedex Scientist (Crown Tundra completion)", "Freezington Station", 761, "rigel2_sub_event_001.amx", 6305, 3, Slot("item", "Gold Bottle Cap", 6306, 796)),
        Gift("peony-expedition-master-ball", "peony", "Peony", "Peony (Dynamax Adventure report)", "Max Lair", 770, "rigel2_sub_event_002.amx", 5195, 1, Slot("item", "Master Ball", 5196, 1, 5189), companionQuantityCells: [5188]),
        Gift("peony-ability-patch", "peony", "Peony", "Peony (Post-clear)", "Max Lair", 780, "rigel2_sub_event_002_after_clear.amx", 6834, 1, Slot("item", "Ability Patch", 6835, 1606)),
        Gift("galarica-wreath-maker", "galarica-wreath-maker", "Galarica Wreath Maker", "Galarica Wreath Maker (Roaring-Sea Caves)", "Roaring-Sea Caves", 790, "rigel2_sub_event_015.amx", 5330, 1, Slot("item", "Galarica Wreath", 5331, 1592)),
    ];

    private static SwShNpcItemGiftDefinition Gift(
        string giftId,
        string npcId,
        string npcName,
        string label,
        string location,
        int displayOrder,
        string fileName,
        int quantityCell,
        int quantity,
        SwShNpcItemGiftItemSlotDefinition item,
        ProjectGame? game = null,
        IReadOnlyList<int>? companionQuantityCells = null)
    {
        return Gift(giftId, npcId, npcName, label, location, displayOrder, fileName, quantityCell, quantity, [item], game, companionQuantityCells);
    }

    private static SwShNpcItemGiftDefinition Gift(
        string giftId,
        string npcId,
        string npcName,
        string label,
        string location,
        int displayOrder,
        string fileName,
        int quantityCell,
        int quantity,
        IReadOnlyList<SwShNpcItemGiftItemSlotDefinition> items,
        ProjectGame? game = null,
        IReadOnlyList<int>? companionQuantityCells = null)
    {
        return new SwShNpcItemGiftDefinition(
            giftId,
            npcId,
            npcName,
            label,
            location,
            displayOrder,
            AmxRoot + fileName,
            quantityCell,
            quantity,
            CanEditQuantity: true,
            companionQuantityCells ?? Array.Empty<int>(),
            items,
            game);
    }

    private static SwShNpcItemGiftDefinition ItemOnlyGift(
        string giftId,
        string npcId,
        string npcName,
        string label,
        string location,
        int displayOrder,
        string fileName,
        SwShNpcItemGiftItemSlotDefinition item,
        ProjectGame? game = null)
    {
        return new SwShNpcItemGiftDefinition(
            giftId,
            npcId,
            npcName,
            label,
            location,
            displayOrder,
            AmxRoot + fileName,
            QuantityCell: null,
            Quantity: 1,
            CanEditQuantity: false,
            CompanionQuantityCells: Array.Empty<int>(),
            Items: [item],
            Game: game);
    }

    private static SwShNpcItemGiftItemSlotDefinition Slot(
        string slotId,
        string label,
        int itemCell,
        int itemId,
        params int[] companionItemCells)
    {
        return new SwShNpcItemGiftItemSlotDefinition(slotId, label, itemCell, itemId, companionItemCells);
    }

    private static IReadOnlyList<SwShNpcItemGiftDefinition> CreateTypeNullMemoryGifts()
    {
        string[] names =
        [
            "Fighting Memory",
            "Flying Memory",
            "Poison Memory",
            "Ground Memory",
            "Rock Memory",
            "Bug Memory",
            "Ghost Memory",
            "Steel Memory",
            "Fire Memory",
            "Water Memory",
            "Grass Memory",
            "Electric Memory",
            "Psychic Memory",
            "Ice Memory",
            "Dragon Memory",
            "Dark Memory",
            "Fairy Memory",
        ];

        return Enumerable.Range(0, names.Length)
            .Select(index => Gift(
                $"type-null-memory-{index + 1:D2}",
                "battle-tower-league-staff",
                "Battle Tower League Staff",
                $"Battle Tower League Staff ({names[index]})",
                "Battle Tower",
                500 + index,
                "sub_event_040.amx",
                5164 + index * 5,
                1,
                Slot("item", names[index], 5165 + index * 5, 904 + index)))
            .ToArray();
    }
}
