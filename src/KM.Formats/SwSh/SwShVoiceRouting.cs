// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Separates added form cries from the unused form-one events retained in the native bank.</summary>
public static class SwShVoiceRouting
{
    public const int ReservedForm = 129;
    public static IReadOnlyList<int> FormOneSpecies { get; } = Array.AsReadOnly(new[]
        { 3, 65, 80, 130, 194, 208, 212, 214, 215, 257, 445, 460 });

    public static int EventForm(int species, int form) =>
        form == 1 && FormOneSpecies.Contains(species) ? ReservedForm : form;
}
