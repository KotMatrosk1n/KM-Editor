// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.RegularExpressions;

namespace KM.ZA.Data;

internal static class ZaLabels
{
    private static readonly IReadOnlyDictionary<int, string> MoveFallbacks = new Dictionary<int, string>
    {
        [622] = "Breakneck Blitz (Physical)",
        [623] = "Breakneck Blitz (Special)",
        [624] = "All-Out Pummeling (Physical)",
        [625] = "All-Out Pummeling (Special)",
        [626] = "Supersonic Skystrike (Physical)",
        [627] = "Supersonic Skystrike (Special)",
        [628] = "Acid Downpour (Physical)",
        [629] = "Acid Downpour (Special)",
        [630] = "Tectonic Rage (Physical)",
        [631] = "Tectonic Rage (Special)",
        [632] = "Continental Crush (Physical)",
        [633] = "Continental Crush (Special)",
        [634] = "Savage Spin-Out (Physical)",
        [635] = "Savage Spin-Out (Special)",
        [636] = "Never-Ending Nightmare (Physical)",
        [637] = "Never-Ending Nightmare (Special)",
        [638] = "Corkscrew Crash (Physical)",
        [639] = "Corkscrew Crash (Special)",
        [640] = "Inferno Overdrive (Physical)",
        [641] = "Inferno Overdrive (Special)",
        [642] = "Hydro Vortex (Physical)",
        [643] = "Hydro Vortex (Special)",
        [644] = "Bloom Doom (Physical)",
        [645] = "Bloom Doom (Special)",
        [646] = "Gigavolt Havoc (Physical)",
        [647] = "Gigavolt Havoc (Special)",
        [648] = "Shattered Psyche (Physical)",
        [649] = "Shattered Psyche (Special)",
        [650] = "Subzero Slammer (Physical)",
        [651] = "Subzero Slammer (Special)",
        [652] = "Devastating Drake (Physical)",
        [653] = "Devastating Drake (Special)",
        [654] = "Black Hole Eclipse (Physical)",
        [655] = "Black Hole Eclipse (Special)",
        [656] = "Twinkle Tackle (Physical)",
        [657] = "Twinkle Tackle (Special)",
        [658] = "Catastropika",
        [695] = "Sinister Arrow Raid",
        [696] = "Malicious Moonsault",
        [697] = "Oceanic Operetta",
        [698] = "Guardian of Alola",
        [699] = "Soul-Stealing 7-Star Strike",
        [700] = "Stoked Sparksurfer",
        [701] = "Pulverizing Pancake",
        [702] = "Extreme Evoboost",
        [703] = "Genesis Supernova",
        [719] = "10,000,000 Volt Thunderbolt",
        [723] = "Light That Burns the Sky",
        [724] = "Searing Sunraze Smash",
        [725] = "Menacing Moonraze Maelstrom",
        [726] = "Let's Snuggle Forever",
        [727] = "Splintered Stormshards",
        [728] = "Clangorous Soulblaze",
    };

    private static readonly IReadOnlyDictionary<string, string> TrainerTokenLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["aku"] = "Dark",
            ["denki"] = "Electric",
            ["doku"] = "Poison",
            ["dragon"] = "Dragon",
            ["esper"] = "Psychic",
            ["fairy"] = "Fairy",
            ["ghost"] = "Ghost",
            ["hagane"] = "Steel",
            ["hikou"] = "Flying",
            ["honoo"] = "Fire",
            ["iwa"] = "Rock",
            ["kakutou"] = "Fighting",
            ["koori"] = "Ice",
            ["kusa"] = "Grass",
            ["mizu"] = "Water",
            ["musi"] = "Bug",
            ["normal"] = "Normal",
            ["strong"] = "Strong",
            ["zimen"] = "Ground",
        };

    private static readonly IReadOnlyDictionary<string, string> ZaMegaFormLabels = new Dictionary<string, string>
    {
        ["6:1"] = "Mega X",
        ["6:2"] = "Mega Y",
        ["150:1"] = "Mega X",
        ["150:2"] = "Mega Y",
    };

    private static readonly IReadOnlySet<int> ZaSingleMegaSpecies = new HashSet<int>
    {
        3, 9, 15, 18, 36, 65, 71, 80, 94, 115, 121, 127, 130, 142, 149, 154, 160, 181, 208, 212,
        214, 227, 229, 248, 254, 257, 260, 282, 302, 303, 306, 308, 310, 319, 323, 334, 354, 358,
        359, 362, 373, 376, 380, 381, 384, 428, 445, 448, 460, 475, 478, 500, 530, 531, 609, 623,
        652, 655, 658, 670, 701, 719, 740, 780, 952, 970,
    };

    public static string Pokemon(int speciesId) => $"Pokemon {speciesId.ToString(CultureInfo.InvariantCulture)}";

    public static string Move(int moveId) => MoveFallbacks.TryGetValue(moveId, out var label)
        ? label
        : $"Move {moveId.ToString(CultureInfo.InvariantCulture)}";

    public static string Item(int itemId) => itemId == 0
        ? "None"
        : $"Item {itemId.ToString(CultureInfo.InvariantCulture)}";

    public static string Ability(int abilityId) => abilityId == 0
        ? "None"
        : $"Ability {abilityId.ToString(CultureInfo.InvariantCulture)}";

    public static string Bool(bool value) => value ? "Yes" : "No";

    public static string PokemonFormLabel(int speciesId, int form)
    {
        if (form == 0)
        {
            return "Base";
        }

        return ZaMegaFormLabels.TryGetValue($"{speciesId.ToString(CultureInfo.InvariantCulture)}:{form.ToString(CultureInfo.InvariantCulture)}", out var label)
            ? label
            : form == 1 && ZaSingleMegaSpecies.Contains(speciesId)
                ? "Mega"
                : $"Form {form.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string PokemonWithForm(int speciesId, int form, string speciesName)
    {
        if (speciesId == 0)
        {
            return "Empty";
        }

        if (form == 0)
        {
            return speciesName;
        }

        return $"{speciesName} ({PokemonFormLabel(speciesId, form)})";
    }

    public static string FormatRawNameForLookup(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var value = raw.Replace('_', ' ').Trim();
        return value.Length == 0
            ? raw
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }

    public static string FormatTrainerIdForLookup(string raw, string? trainerClass = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var rankMatch = Regex.Match(
            raw,
            @"^dim_rank_(?<rank>\d+)(?:_(?<type>[a-z]+))?_(?<number>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (rankMatch.Success)
        {
            var rank = int.Parse(rankMatch.Groups["rank"].Value, CultureInfo.InvariantCulture);
            var number = int.Parse(rankMatch.Groups["number"].Value, CultureInfo.InvariantCulture);
            var typeLabel = rankMatch.Groups["type"].Success
                ? $" {FormatTrainerToken(rankMatch.Groups["type"].Value)}"
                : string.Empty;
            var label = string.IsNullOrWhiteSpace(trainerClass)
                ? string.Create(CultureInfo.InvariantCulture, $"Dimension Rank {rank}{typeLabel} {number}")
                : string.Create(CultureInfo.InvariantCulture, $"Rank {rank}{typeLabel} {number}");
            return PrefixTrainerClass(label, trainerClass);
        }

        var mainMissionMatch = Regex.Match(
            raw,
            @"^Ev_m(?<mission>\d+)_(?<step>\d+)(?:_(?<variant>[a-z0-9_]+))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (mainMissionMatch.Success)
        {
            var mission = mainMissionMatch.Groups["mission"].Value.PadLeft(2, '0');
            var step = FormatNumberToken(mainMissionMatch.Groups["step"].Value);
            var variant = mainMissionMatch.Groups["variant"].Success
                ? $" {FormatTrainerTokenSequence(mainMissionMatch.Groups["variant"].Value)}"
                : string.Empty;
            return PrefixTrainerClass($"Mission {mission} Step {step}{variant}", trainerClass);
        }

        var sideMissionMatch = Regex.Match(
            raw,
            @"^Ev_sub_(?<mission>\d+)_(?<step>\d+)(?:_(?<variant>[a-z0-9_]+))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (sideMissionMatch.Success)
        {
            var mission = FormatNumberToken(sideMissionMatch.Groups["mission"].Value);
            var step = FormatNumberToken(sideMissionMatch.Groups["step"].Value);
            var variant = sideMissionMatch.Groups["variant"].Success
                ? $" {FormatTrainerTokenSequence(sideMissionMatch.Groups["variant"].Value)}"
                : string.Empty;
            return PrefixTrainerClass($"Side Mission {mission} Step {step}{variant}", trainerClass);
        }

        var infiniteMatch = Regex.Match(
            raw,
            @"^za_inf(?<strong>_strong)?_(?<rest>[a-z0-9_]+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (infiniteMatch.Success)
        {
            var battleKind = infiniteMatch.Groups["strong"].Success
                ? "Infinite Z-A Strong Battle"
                : "Infinite Z-A Battle";
            return PrefixTrainerClass(
                $"{battleKind} {FormatTrainerTokenSequence(infiniteMatch.Groups["rest"].Value)}",
                trainerClass);
        }

        return PrefixTrainerClass(FormatTrainerTokenSequence(raw), trainerClass);
    }

    private static string PrefixTrainerClass(string label, string? trainerClass)
    {
        if (string.IsNullOrWhiteSpace(trainerClass)
            || string.Equals(trainerClass, "Trainer", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith(trainerClass, StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return $"{trainerClass} {label}";
    }

    private static string FormatTrainerTokenSequence(string value)
    {
        return string.Join(
            " ",
            value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(FormatTrainerToken));
    }

    private static string FormatTrainerToken(string token)
    {
        if (TrainerTokenLabels.TryGetValue(token, out var label))
        {
            return label;
        }

        return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? FormatNumberToken(token)
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant());
    }

    private static string FormatNumberToken(string token)
    {
        return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : token;
    }
}
