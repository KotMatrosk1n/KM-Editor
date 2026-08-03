// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Resources;

namespace KM.Setup.UI.Localization;

internal sealed class LocalizationService
{
    private static readonly HashSet<string> SupportedLanguages =
    [
        "en",
        "de",
        "es",
        "fr",
        "ru",
        "uk",
        "zh",
    ];

    private readonly ResourceManager resourceManager =
        new("KM.Setup.UI.Resources.Strings", typeof(LocalizationService).Assembly);

    private CultureInfo culture = CultureInfo.GetCultureInfo("en");

    private LocalizationService()
    {
    }

    public static LocalizationService Current { get; } = new();

    public string this[string key] => Get(key);

    public void UseSystemCulture()
    {
        var requestedCulture = CultureInfo.CurrentUICulture;
        var language = requestedCulture.TwoLetterISOLanguageName;
        culture = SupportedLanguages.Contains(language)
            ? requestedCulture
            : CultureInfo.GetCultureInfo("en");
    }

    public string Get(string key)
    {
        return resourceManager.GetString(key, culture)
            ?? resourceManager.GetString(key, CultureInfo.GetCultureInfo("en"))
            ?? key;
    }

    public string Format(string key, params object?[] arguments)
    {
        return string.Format(culture, Get(key), arguments);
    }
}
