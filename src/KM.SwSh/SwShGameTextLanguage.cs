// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.SwSh;

internal static class SwShGameTextLanguage
{
    public const string English = "English";

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
            "ko" or "kr" or "korean" => "Korean",
            "zh" or "zh-cn" or "zh-hans" or "cn" or "simplifiedchinese" or "simplified_chinese" or "simp_chinese" => "Simp_Chinese",
            "zh-tw" or "zh-hant" or "tw" or "traditionalchinese" or "traditional_chinese" or "trad_chinese" => "Trad_Chinese",
            _ => English,
        };
    }

    public static string CommonMessagePath(string language, string fileName)
    {
        return $"romfs/bin/message/{language}/common/{fileName}";
    }

    public static string ScriptMessagePath(string language, string fileName)
    {
        return $"romfs/bin/message/{language}/script/{fileName}";
    }

    private static string Normalize(string? language)
    {
        return string.IsNullOrWhiteSpace(language)
            ? string.Empty
            : language.Trim().ToLowerInvariant();
    }
}
