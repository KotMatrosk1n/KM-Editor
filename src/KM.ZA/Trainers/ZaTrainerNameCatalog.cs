// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.ZA.Trainers;

internal static class ZaTrainerNameCatalog
{
    private static readonly IReadOnlyDictionary<string, string> MandatoryTrainerNamesByTeamSignature =
        CreateMandatoryTrainerNamesByTeamSignature();

    private static readonly IReadOnlyDictionary<string, string> MandatoryTrainerNamesBySpeciesSignature =
        CreateMandatoryTrainerNamesBySpeciesSignature();

    public static string? ResolveMandatoryTrainerName(IReadOnlyList<ZaTrainerPokemonRecord> team)
    {
        var signature = CreateTeamSignature(team);
        if (!string.IsNullOrWhiteSpace(signature) && MandatoryTrainerNamesByTeamSignature.TryGetValue(signature, out var name))
        {
            return FormatTrainerNameOnly(name);
        }

        var speciesSignature = CreateSpeciesSignature(team);
        return string.IsNullOrWhiteSpace(speciesSignature)
            || !MandatoryTrainerNamesBySpeciesSignature.TryGetValue(speciesSignature, out var speciesName)
            ? null
            : FormatTrainerNameOnly(speciesName);
    }

    private static IReadOnlyDictionary<string, string> CreateMandatoryTrainerNamesByTeamSignature()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["674:4"] = "Andi the Backpacker",
            ["173:4|659:4"] = "Brigitte the Waitress",
            ["158:5|498:5"] = "Urbain/Taunie of Team MZ (Chikorita)",
            ["498:5|152:5"] = "Urbain/Taunie of Team MZ (Totodile)",
            ["152:5|158:5"] = "Urbain/Taunie of Team MZ (Tepig)",
            ["79:8|16:9|25:9"] = "Zach the Driver",
            ["66:10|307:11"] = "Josee of the Fist of Justice",
            ["179:15"] = "Detective Emma",
            ["682:15|684:15|666:16"] = "Yvon the Office Worker",
            ["167:16|302:16|559:17"] = "Naveen of Team MZ",
            ["543:20|315:20|64:21|676:21"] = "Xavi the Grade-Schooler",
            ["692:22|582:22|120:23"] = "Lida of Team MZ",
            ["512:24|516:24|514:24"] = "Rintaro",
            ["159:25|499:25|310:26"] = "Urbain/Taunie of Team MZ (Chikorita)",
            ["499:25|153:25|310:26"] = "Urbain/Taunie of Team MZ (Totodile)",
            ["153:25|159:25|310:26"] = "Urbain/Taunie of Team MZ (Tepig)",
            ["229:30|319:30|427:30|780:32"] = "Vinnie of Quasartico Inc.",
            ["168:33|302:33|559:33"] = "Naveen of Team MZ",
            ["694:34|587:34|26:35"] = "Mani of DYN4MO",
            ["450:36|660:36|530:37"] = "Tarragon of DYN4MO",
            ["695:37|181:38|618:38|604:39"] = "Canari of DYN4MO",
            ["67:43|308:43"] = "Josée of the Fist of Justice",
            ["695:42|181:43|618:43|604:44"] = "Canari of DYN4MO",
            ["354:43|711:44|609:45"] = "Gwynn of the Fist of Justice",
            ["214:45|308:46|68:46|870:47"] = "Ivor of the Fist of Justice",
            ["569:44|305:44"] = "Rust Syndicate Grunt",
            ["208:46|212:46|227:47"] = "Philippe of the Rust Syndicate",
            ["354:45|711:46|94:46|609:47"] = "Gwynn of the Fist of Justice",
            ["444:47|705:47"] = "Francois of the SBC",
            ["683:48|685:48"] = "Vivica of the SBC",
            ["127:48|95:48|362:49"] = "Representative of Lumiose Safety Group",
            ["24:50|130:51|407:51|545:52"] = "Corbeau of the Rust Syndicate",
            ["693:46|582:46|587:47|120:48"] = "Lida of Team MZ",
            ["168:48|302:48|553:49|560:50"] = "Naveen of Team MZ",
            ["671:50"] = "Asami the Furisode Girl",
            ["303:52|707:52"] = "Livia of the SBC",
            ["148:52|372:52"] = "Gérald of the SBC",
            ["715:53|697:53|445:53|691:54"] = "Lebanne of the SBC",
            ["695:53|181:54|618:54|604:55"] = "Canari of DYN4MO",
            ["214:53|308:54|68:54|870:55"] = "Ivor of the Fist of Justice",
            ["24:54|130:55|407:55|545:56"] = "Corbeau of the Rust Syndicate",
            ["718:57"] = "Zygarde",
            ["703:57|303:58|699:58|282:58|36:59"] = "Jacinthe of the SBC",
            ["208:56|212:56|306:56|227:57"] = "Philippe of the Rust Syndicate",
            ["181:57|303:57|428:57|448:57|687:58"] = "Detective Emma",
            ["663:59|323:59|142:60|376:60|668:61"] = "Griselle of Team Flare Nouveau",
            ["675:61|687:61|668:61|248:62|373:62|6:63"] = "Grisham of Team Flare Nouveau",
            ["678:62|706:62|713:62|160:63|310:63|500:64"] = "Urbain/Taunie of Team MZ (Chikorita)",
            ["678:62|706:62|713:62|500:63|310:63|154:64"] = "Urbain/Taunie of Team MZ (Totodile)",
            ["678:62|706:62|713:62|154:63|310:63|160:64"] = "Urbain/Taunie of Team MZ (Tepig)",
            ["678:70|706:70|160:71|310:71|500:71|670:72"] = "Urbain/Taunie of Team MZ (Chikorita)",
            ["678:70|706:70|500:71|310:71|154:71|670:72"] = "Urbain/Taunie of Team MZ (Totodile)",
            ["678:70|706:70|154:71|310:71|160:71|670:72"] = "Urbain/Taunie of Team MZ (Tepig)",
            ["668:78|671:78|302:78|715:79|569:79|130:80"] = "L",
        };

        return names;
    }

    private static IReadOnlyDictionary<string, string> CreateMandatoryTrainerNamesBySpeciesSignature()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in MandatoryTrainerNamesByTeamSignature)
        {
            var speciesSignature = CreateSpeciesSignature(entry.Key);
            if (!string.IsNullOrWhiteSpace(speciesSignature))
            {
                names[speciesSignature] = entry.Value;
            }
        }

        return names;
    }

    private static string CreateTeamSignature(IEnumerable<ZaTrainerPokemonRecord> team)
    {
        return string.Join(
            "|",
            team
                .Where(pokemon => pokemon.SpeciesId > 0)
                .Select(pokemon => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{pokemon.SpeciesId}:{pokemon.Level}")));
    }

    private static string CreateSpeciesSignature(IEnumerable<ZaTrainerPokemonRecord> team)
    {
        return string.Join(
            "|",
            team
                .Where(pokemon => pokemon.SpeciesId > 0)
                .Select(pokemon => pokemon.SpeciesId.ToString(CultureInfo.InvariantCulture)));
    }

    private static string CreateSpeciesSignature(string teamSignature)
    {
        return string.Join(
            "|",
            teamSignature
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Split(':', 2)[0]));
    }

    private static string FormatTrainerNameOnly(string name)
    {
        var trimmed = name.Trim();
        var parenthetical = trimmed.IndexOf(" (", StringComparison.Ordinal);
        if (parenthetical > 0)
        {
            trimmed = trimmed[..parenthetical];
        }

        if (trimmed.StartsWith("Detective ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed["Detective ".Length..];
        }

        if (trimmed.Equals("Rust Syndicate Grunt", StringComparison.OrdinalIgnoreCase))
        {
            return "Grunt";
        }

        if (trimmed.StartsWith("Representative ", StringComparison.OrdinalIgnoreCase))
        {
            return "representative";
        }

        var titleIndex = trimmed.IndexOf(" the ", StringComparison.OrdinalIgnoreCase);
        if (titleIndex > 0)
        {
            return trimmed[..titleIndex];
        }

        var groupIndex = trimmed.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
        return groupIndex > 0
            ? trimmed[..groupIndex]
            : trimmed;
    }
}
