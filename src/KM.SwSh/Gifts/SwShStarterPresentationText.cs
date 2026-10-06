// SPDX-License-Identifier: GPL-3.0-only
using System.Text.RegularExpressions;

namespace KM.SwSh.Gifts;

internal static class SwShStarterPresentationText
{
    internal static string Create(string language, string name, bool cry, bool question, string original)
    {
        if (cry) return name + "!";
        var body = (language, question) switch
        {
            ("English", false) => $"This is {name}!",
            ("English", true) => $"Would you like {name} as your partner?",
            ("Spanish", false) => $"¡Este Pokémon es {name}!",
            ("Spanish", true) => $"¿Quieres elegir a {name}?",
            ("French", false) => $"Voici {name} !",
            ("French", true) => $"Veux-tu choisir {name} ?",
            ("German", false) => $"Das ist {name}!",
            ("German", true) => $"Möchtest du {name} wählen?",
            ("Italian", false) => $"Ecco {name}!",
            ("Italian", true) => $"Vuoi scegliere {name}?",
            ("JPN" or "JPN_KANJI", false) => $"{name}だよ！",
            ("JPN" or "JPN_KANJI", true) => $"{name}を　えらぶ？",
            ("Korean", false) => $"이 포켓몬은 {name}!",
            ("Korean", true) => $"{name}, 이 포켓몬을 선택할까?",
            ("Simp_Chinese", false) => $"这是{name}！",
            ("Simp_Chinese", true) => $"要选择{name}作为伙伴吗？",
            ("Trad_Chinese", false) => $"這是{name}！",
            ("Trad_Chinese", true) => $"要選擇{name}作為夥伴嗎？",
            _ => throw new InvalidDataException("Starter dialogue language is unsupported."),
        };
        return body + string.Concat(Regex.Matches(original, @"\[VAR 0114\([^\]]*\)\]").Select(m => m.Value));
    }
}
