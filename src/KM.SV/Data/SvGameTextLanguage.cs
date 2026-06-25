// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.SV.Data;

internal static class SvGameTextLanguage
{
    public const string English = "English";

    public static IReadOnlyList<string> SupportedMessageLanguages { get; } =
    [
        English,
        "Spanish",
        "French",
        "German",
        "Italian",
        "JPN",
        "JPN_KANJI",
        "Korean",
        "Simp_Chinese",
        "Trad_Chinese",
    ];

    public static string Resolve(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Resolve(paths.GameTextLanguage);
    }

    public static string Resolve(string? language)
    {
        return Normalize(language) switch
        {
            "de" or "german" => "German",
            "es" or "spanish" => "Spanish",
            "fr" or "french" => "French",
            "it" or "italian" => "Italian",
            "ja" or "jp" or "jpn" or "japanese" => "JPN",
            "jpn_kanji" or "jpn-kanji" or "japanese_kanji" or "japanese-kanji" => "JPN_KANJI",
            "ko" or "kr" or "korean" => "Korean",
            "zh" or "zh-cn" or "zh-hans" or "cn" or "simplifiedchinese" or "simplified_chinese" or "simp_chinese" => "Simp_Chinese",
            "zh-tw" or "zh-hant" or "tw" or "traditionalchinese" or "traditional_chinese" or "trad_chinese" => "Trad_Chinese",
            _ => English,
        };
    }

    private static string Normalize(string? language)
    {
        return string.IsNullOrWhiteSpace(language)
            ? string.Empty
            : language.Trim().ToLowerInvariant();
    }
}
