// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Pokemon;
using KM.Core.Projects;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Combines planned forms with the status of registered form restorations.</summary>
internal static class SwShRestorationFormCandidates
{
    private sealed record FormCandidate(int Species, int Form, string Label, SwShRestorationDonor? Donor);
    private static readonly FormCandidate[] PlannedForms =
    [
        Candidate(3, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(6, 1, "MegaX", SwShRestorationDonor.LegendsZA),
        Candidate(6, 2, "MegaY", SwShRestorationDonor.LegendsZA),
        Candidate(9, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(15, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(18, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(19, 1, "Alolan"),
        Candidate(20, 1, "Alolan"),
        Candidate(26, 2, "MegaX", SwShRestorationDonor.LegendsZA),
        Candidate(26, 3, "MegaY", SwShRestorationDonor.LegendsZA),
        Candidate(36, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(58, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(59, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(65, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(71, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(74, 1, "Alolan", SwShRestorationDonor.ScarletViolet),
        Candidate(75, 1, "Alolan", SwShRestorationDonor.ScarletViolet),
        Candidate(76, 1, "Alolan", SwShRestorationDonor.ScarletViolet),
        Candidate(80, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(88, 1, "Alolan", SwShRestorationDonor.ScarletViolet),
        Candidate(89, 1, "Alolan", SwShRestorationDonor.ScarletViolet),
        Candidate(94, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(100, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(101, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(115, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(121, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(127, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(128, 1, "PaldeanCombat", SwShRestorationDonor.ScarletViolet),
        Candidate(128, 2, "PaldeanBlaze", SwShRestorationDonor.ScarletViolet),
        Candidate(128, 3, "PaldeanAqua", SwShRestorationDonor.ScarletViolet),
        Candidate(130, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(142, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(149, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(150, 1, "MegaX", SwShRestorationDonor.LegendsZA),
        Candidate(150, 2, "MegaY", SwShRestorationDonor.LegendsZA),
        Candidate(154, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(157, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(160, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(181, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(194, 1, "Paldean", SwShRestorationDonor.ScarletViolet),
        Candidate(208, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(211, 1, "Hisuian", SwShRestorationDonor.LegendsZA),
        Candidate(212, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(214, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(215, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(227, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(229, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(248, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(254, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(257, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(260, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(282, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(302, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(303, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(306, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(308, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(310, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(319, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(323, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(334, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(354, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(358, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(359, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(359, 2, "MegaZ", SwShRestorationDonor.LegendsZA),
        Candidate(362, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(373, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(376, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(380, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(381, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(382, 1, "Primal", SwShRestorationDonor.LegendsZA),
        Candidate(383, 1, "Primal", SwShRestorationDonor.LegendsZA),
        Candidate(384, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(386, 1, "Attack", SwShRestorationDonor.ScarletViolet),
        Candidate(386, 2, "Defense", SwShRestorationDonor.ScarletViolet),
        Candidate(386, 3, "Speed", SwShRestorationDonor.ScarletViolet),
        Candidate(398, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(428, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(445, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(445, 2, "MegaZ", SwShRestorationDonor.LegendsZA),
        Candidate(448, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(448, 2, "MegaZ", SwShRestorationDonor.LegendsZA),
        Candidate(460, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(475, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(478, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(483, 1, "Origin", SwShRestorationDonor.ScarletViolet),
        Candidate(484, 1, "Origin", SwShRestorationDonor.ScarletViolet),
        Candidate(485, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(491, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(500, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(503, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(530, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(531, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(545, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(549, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(560, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(570, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(571, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(604, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(609, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(623, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(628, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(652, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(655, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(658, 3, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(668, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(670, 6, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(678, 2, "MegaMale", SwShRestorationDonor.LegendsZA),
        Candidate(678, 3, "MegaFemale", SwShRestorationDonor.LegendsZA),
        Candidate(687, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(689, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(691, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(701, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(705, 1, "Hisuian", SwShRestorationDonor.LegendsZA),
        Candidate(706, 1, "Hisuian", SwShRestorationDonor.LegendsZA),
        Candidate(713, 1, "Hisuian", SwShRestorationDonor.LegendsZA),
        Candidate(718, 5, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(719, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(724, 1, "Hisuian", SwShRestorationDonor.ScarletViolet),
        Candidate(740, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(768, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(780, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(801, 2, "MegaStandard", SwShRestorationDonor.LegendsZA),
        Candidate(801, 3, "MegaOriginal", SwShRestorationDonor.LegendsZA),
        Candidate(807, 1, "Mega", SwShRestorationDonor.LegendsZA),
        Candidate(870, 1, "Mega", SwShRestorationDonor.LegendsZA),
    ];

    private static readonly IReadOnlyDictionary<SwShPokemonIdentity, SwShRestorationDefinition> RegisteredForms =
        SwShRestorationDefinitions.All.SelectMany(definition => definition.Identities
            .Where(identity => identity.Form > 0).Select(identity => (identity, definition)))
            .ToDictionary(row => row.identity, row => row.definition);

    private static readonly IReadOnlyDictionary<SwShPokemonIdentity, FormCandidate> Forms = PlannedForms
        .Concat(RegisteredForms.Select(pair => RegisteredCandidate(pair.Key, pair.Value.Donor)))
        .DistinctBy(form => new SwShPokemonIdentity(form.Species, form.Form))
        .ToDictionary(form => new SwShPokemonIdentity(form.Species, form.Form));

    private static FormCandidate Candidate(int species, int form, string label, SwShRestorationDonor? donor = null) =>
        new(species, form, label, donor);

    private static FormCandidate RegisteredCandidate(SwShPokemonIdentity identity, SwShRestorationDonor donor)
    {
        var family = donor == SwShRestorationDonor.LegendsZA
            ? PokemonFormLabelFamily.LegendsZA : PokemonFormLabelFamily.ScarletViolet;
        var label = PokemonFormLabels.ResolveFormLabel(identity.Species, string.Empty, identity.Form, family)
            ?? string.Create(CultureInfo.InvariantCulture, $"Form {identity.Form}");
        return Candidate(identity.Species, identity.Form, label, donor);
    }

    internal static string? ResolveFormLabel(int species, int form) => Forms.GetValueOrDefault(new(species, form))?.Label switch
    {
        "MegaX" => "Mega X", "MegaY" => "Mega Y", "MegaZ" => "Mega Z",
        "MegaMale" => "Mega (Male)", "MegaFemale" => "Mega (Female)",
        "MegaStandard" => "Mega (Standard Color)", "MegaOriginal" => "Mega (Original Color)",
        "PaldeanCombat" => "Paldean (Combat Breed)", "PaldeanBlaze" => "Paldean (Blaze Breed)",
        "PaldeanAqua" => "Paldean (Aqua Breed)", var label => label,
    };

    public static IReadOnlyList<SwShRestorationCatalogEntry> Include(IEnumerable<SwShRestorationCatalogEntry> speciesEntries,
        SwShEffectivePokemonCatalog catalog, IReadOnlyList<string> names, ProjectGame? game, SwShRestorationState state)
    {
        var current = catalog.Entries.ToDictionary(e => e.Identity);
        var forms = Forms.Values.Where(f => !current.TryGetValue(new(f.Species, f.Form), out var entry) || entry.Origin != SwShPokemonOrigin.Native)
            .Select(f =>
            {
                var definition = RegisteredForms.GetValueOrDefault(new(f.Species, f.Form));
                var selection = state.Selections.SingleOrDefault(s => s.DefinitionId == definition?.Id);
                current.TryGetValue(new(f.Species, f.Form), out var entry);
                var complete = entry?.Capabilities.HasFlag(SwShPokemonCapability.Assignment) == true;
                return new SwShRestorationCatalogEntry(f.Species,
                f.Species < names.Count && !string.IsNullOrWhiteSpace(names[f.Species]) ? names[f.Species]
                    : SwShRestorationSpecies.DisplayName(f.Species) ?? $"Pokemon {f.Species}",
                definition?.Id, game is { } edition && definition is not null ? definition.Support.GetValueOrDefault(edition) : SwShRestorationSupport.NotSupported,
                selection?.Enabled == true ? complete ? SwShRestorationInstallation.Enabled : SwShRestorationInstallation.NeedsRepair
                    : entry?.Personal.IsPresentInGame == true ? SwShRestorationInstallation.External : SwShRestorationInstallation.Disabled, [f.Species])
                { Form = f.Form, FormLabel = f.Label, Donor = definition?.Donor ?? f.Donor, AvailableForAssignment = complete,
                    PlannedPersonalId = SwShRestorationFormSlots.PlannedPersonalRow(f.Species, f.Form) };
            });
        return SwShRestorationFamilyOrder.Sort(speciesEntries.Concat(forms));
    }
}
