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
