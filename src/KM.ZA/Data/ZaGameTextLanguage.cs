// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.ZA.Data;

internal static class ZaGameTextLanguage
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

    private static string Normalize(string? language)
    {
        return string.IsNullOrWhiteSpace(language)
            ? string.Empty
            : language.Trim().ToLowerInvariant();
    }
}
