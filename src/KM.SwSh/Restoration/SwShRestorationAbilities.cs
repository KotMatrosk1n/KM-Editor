// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public sealed record SwShRestoredAbilityText(int OriginalLineCount, SwShGameTextLine Line);

/// <summary>Only abilities with actual restoration runtime handlers are registered.</summary>
public static class SwShRestorationAbilities
{
    public const int NativeTextCount = 268;
    public const int SeedSower = 269;
    public const int ThermalExchange = 270;
    public const int PurifyingSalt = 272;
    public const int WellBakedBody = 273;
    public const int GoodAsGold = 283;
    public const int ArmorTail = 296;
    public const int EarthEater = 297;
    public const int RockyPayload = 276;
    public const int Sharpness = 292;
    public const int MindsEye = 300;
    public const int ToxicChain = 305;
    public const int LingeringAroma = 268;
    public const int ToxicDebris = 295;
    public const int Hospitality = 299;
    public const int AngerShell = 271;
    public const int OrichalcumPulse = 288;
    public const int HadronEngine = 289;
    public const int GuardDog = 275;
    public const int VesselOfRuin = 284;
    public const int SwordOfRuin = 285;
    public const int TabletsOfRuin = 286;
    public const int BeadsOfRuin = 287;
    public const int Costar = 294;
    public const int FireMane = 316;
    public const int Dragonize = 312;
    public const int SpicySpray = 318;
    public const int Eelevate = 313;
    public const int AuraGuard = 319;
    public const int WindRider = 274;
    public const int WindPower = 277;
    public const int Electromorphosis = 280;
    public const int TeraShell = 308;
    public const int SupremeOverlord = 293;
    public const int SupersweetSyrup = 306;
    public const int ZeroToHero = 278;
    public const int TeraShift = 307;
    public const int PoisonPuppeteer = 310;
    public const int Opportunist = 290;
    public const int MyceliumMight = 298;
    public const int Protosynthesis = 281;
    public const int QuarkDrive = 282;
    public const int PiercingDrill = 311;
    public const int MegaSol = 315;
    public const int CudChew = 291;
    public const int Commander = 279;
    public static bool HasHandler(int ability) => ability is Commander or PiercingDrill or MegaSol or CudChew or Protosynthesis or QuarkDrive or MyceliumMight or PoisonPuppeteer or Opportunist or ZeroToHero or TeraShift or SupremeOverlord or SupersweetSyrup or TeraShell or WindRider or WindPower or Electromorphosis or Eelevate or AuraGuard or Dragonize or SpicySpray or Costar or FireMane or BeadsOfRuin or TabletsOfRuin or SwordOfRuin or VesselOfRuin or GuardDog or AngerShell or OrichalcumPulse or HadronEngine or LingeringAroma or ToxicDebris or Hospitality or SeedSower or ThermalExchange or PurifyingSalt or WellBakedBody or GoodAsGold or ArmorTail or EarthEater or RockyPayload or Sharpness or MindsEye or ToxicChain;
    public static bool IsSourceAbilitySupported(int ability) => ability is >= 1 and <= 267 || HasHandler(ability);
    // Z-A personal rows retain base-form ability slots for these Mega forms.
    // Use their conventional abilities, backed by native or restored handlers.
    public static int MegaAbilityFor(SwShPokemonIdentity identity) => (identity.Species, identity.Form) switch
    {
        (71, 1) => 215, // Innards Out
        (121, 1) => 37, // Huge Power
        (149, 1) => 136, // Multiscale
        (227, 1) => 242, // Stalwart
        (398, 1) => 126, // Contrary
        (445, 2) => 26, // Levitate
        (478, 1) => 117, // Snow Warning
        (500, 1) => 104, // Mold Breaker
        (545, 1) => 75, // Shell Armor
        (560, 1) => 22, // Intimidate
        (609, 1) => 151, // Infiltrator
        (623, 1) => 260, // Unseen Fist
        (652, 1) => 171, // Bulletproof
        (655, 1) => 26, // Levitate
        (658, 3) => 168, // Protean
        (670, 6) => 187, // Fairy Aura
        (678, 2) => 36, // Trace
        (678, 3) => 36, // Trace
        (687, 1) => 126, // Contrary
        (689, 1) => 181, // Tough Claws
        (691, 1) => 144, // Regenerator
        (701, 1) => 99, // No Guard
        (718, 5) => 188, // Aura Break
        (740, 1) => 89, // Iron Fist
        (768, 1) => 181, // Tough Claws
        (780, 1) => 201, // Berserk
        (870, 1) => 128, // Defiant
        (530, 1) => PiercingDrill,
        (154, 1) => MegaSol,
        (668, 1) => FireMane,
        (160, 1) => Dragonize,
        (604, 1) => Eelevate,
        (448, 2) => AuraGuard,
        (952, 1) => SpicySpray,
        _ => 0,
    };
    public static bool IsAbilityFor(SwShPokemonIdentity identity, int ability) => ability switch
    {
        Commander => identity.Species == 978 && identity.Form is >= 0 and <= 5,
        PiercingDrill => (identity.Species, identity.Form) is (530, 1),
        MegaSol => (identity.Species, identity.Form) is (154, 1),
        SupremeOverlord => (identity.Species, identity.Form) is (983, 0),
        SupersweetSyrup => (identity.Species, identity.Form) is (1011, 0) or (1019, 0),
        ZeroToHero => (identity.Species, identity.Form) is (964, 0) or (964, 1),
        TeraShift => (identity.Species, identity.Form) is (1024, 0),
        PoisonPuppeteer => (identity.Species, identity.Form) is (1025, 0),
        Protosynthesis => identity.Form == 0 && identity.Species is 984 or 985 or 986 or 987 or 988 or 989 or 1005 or 1009 or 1020 or 1021,
        QuarkDrive => identity.Form == 0 && identity.Species is 990 or 991 or 992 or 993 or 994 or 995 or 1006 or 1010 or 1022 or 1023,
        CudChew => (identity.Species, identity.Form) is (128, 1) or (128, 2) or (128, 3) or (981, 0),
        MyceliumMight => (identity.Species, identity.Form) is (948, 0) or (949, 0),
        Opportunist => (identity.Species, identity.Form) is (955, 0) or (956, 0),
        TeraShell => (identity.Species, identity.Form) is (1024, 1),
        WindRider => (identity.Species, identity.Form) is (946, 0) or (947, 0) or (275, 0),
        WindPower => (identity.Species, identity.Form) is (940, 0) or (941, 0),
        Electromorphosis => (identity.Species, identity.Form) is (939, 0),
        Eelevate => (identity.Species, identity.Form) is (604, 1),
        AuraGuard => (identity.Species, identity.Form) is (448, 2),
        Dragonize => (identity.Species, identity.Form) is (160, 1),
        SpicySpray => (identity.Species, identity.Form) is (952, 1),
        Costar => (identity.Species, identity.Form) is (973, 0),
        FireMane => (identity.Species, identity.Form) is (668, 1),
        VesselOfRuin => (identity.Species, identity.Form) is (1003, 0),
        SwordOfRuin => (identity.Species, identity.Form) is (1002, 0),
        TabletsOfRuin => (identity.Species, identity.Form) is (1001, 0),
        BeadsOfRuin => (identity.Species, identity.Form) is (1004, 0),
        GuardDog => (identity.Species, identity.Form) is (943, 0) or (1014, 0),
        AngerShell => (identity.Species, identity.Form) is (950, 0),
        OrichalcumPulse => (identity.Species, identity.Form) is (1007, 0) or (1007, 1) or (1007, 2) or (1007, 3) or (1007, 4),
        HadronEngine => (identity.Species, identity.Form) is (1008, 0) or (1008, 1) or (1008, 2) or (1008, 3) or (1008, 4),
        LingeringAroma => (identity.Species, identity.Form) is (916, 0),
        ToxicDebris => (identity.Species, identity.Form) is (969, 0) or (970, 0) or (970, 1),
        Hospitality => (identity.Species, identity.Form) is (1012, 0) or (1012, 1) or (1013, 0) or (1013, 1),
        SeedSower => (identity.Species, identity.Form) is (930, 0),
        ThermalExchange => (identity.Species, identity.Form) is (996, 0) or (997, 0) or (998, 0) or (998, 1),
        PurifyingSalt => (identity.Species, identity.Form) is (932, 0) or (933, 0) or (934, 0),
        WellBakedBody => (identity.Species, identity.Form) is (927, 0),
        GoodAsGold => (identity.Species, identity.Form) is (1000, 0),
        ArmorTail => (identity.Species, identity.Form) is (981, 0),
        EarthEater => (identity.Species, identity.Form) is (968, 0),
        RockyPayload => identity.Species == 962 && identity.Form == 0,
        Sharpness => identity.Form == 0 && identity.Species is 475 or 900 or 976
            || identity.Species == 503 && identity.Form == 1,
        MindsEye => identity.Species == 901 && identity.Form == 1,
        ToxicChain => identity.Form == 0 && identity.Species is 1014 or 1015 or 1016,
        _ => false,
    };
    public static string EnglishName(int ability) => ability switch
    {
        Commander => "Commander",
        PiercingDrill => "Piercing Drill",
        MegaSol => "Mega Sol",
        SupremeOverlord => "Supreme Overlord",
        SupersweetSyrup => "Supersweet Syrup",
        ZeroToHero => "Zero to Hero",
        TeraShift => "Tera Shift",
        PoisonPuppeteer => "Poison Puppeteer",
        Protosynthesis => "Protosynthesis",
        QuarkDrive => "Quark Drive",
        CudChew => "Cud Chew",
        MyceliumMight => "Mycelium Might",
        Opportunist => "Opportunist",
        TeraShell => "Tera Shell",
        WindRider => "Wind Rider",
        WindPower => "Wind Power",
        Electromorphosis => "Electromorphosis",
        Eelevate => "Eelevate",
        AuraGuard => "Aura Guard",
        Dragonize => "Dragonize",
        SpicySpray => "Spicy Spray",
        Costar => "Costar",
        FireMane => "Fire Mane",
        VesselOfRuin => "Vessel of Ruin",
        SwordOfRuin => "Sword of Ruin",
        TabletsOfRuin => "Tablets of Ruin",
        BeadsOfRuin => "Beads of Ruin",
        GuardDog => "Guard Dog",
        AngerShell => "Anger Shell",
        OrichalcumPulse => "Orichalcum Pulse",
        HadronEngine => "Hadron Engine",
        LingeringAroma => "Lingering Aroma",
        ToxicDebris => "Toxic Debris",
        Hospitality => "Hospitality",
        SeedSower => "Seed Sower",
        ThermalExchange => "Thermal Exchange",
        PurifyingSalt => "Purifying Salt",
        WellBakedBody => "Well-Baked Body",
        GoodAsGold => "Good as Gold",
        ArmorTail => "Armor Tail",
        EarthEater => "Earth Eater",
        RockyPayload => "Rocky Payload",
        Sharpness => "Sharpness",
        MindsEye => "Mind\u2019s Eye",
        ToxicChain => "Toxic Chain",
        _ => throw new ArgumentOutOfRangeException(nameof(ability)),
    };
    public static string TextPath(string language, bool description) =>
        SwShGameTextLanguage.CommonMessagePath(language, description ? "tokuseiinfo.dat" : "tokusei.dat");
    public static bool IsTextPath(string path) => SwShRestorationSpeciesText.Languages.Any(language =>
        TextPath(language, false) == path || TextPath(language, true) == path);
}
