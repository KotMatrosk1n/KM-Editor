// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;

namespace KM.ZA.Workflows;

internal static class ZaSpeciesFormPairValidation
{
    public static bool ValidateChangedPair(
        ZaPokemonAvailability availability,
        int sourceSpeciesId,
        int sourceForm,
        int projectedSpeciesId,
        int projectedForm,
        string domain,
        string subject,
        ICollection<ValidationDiagnostic> diagnostics,
        string? file = null,
        string field = "form")
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (sourceSpeciesId == projectedSpeciesId && sourceForm == projectedForm)
        {
            return true;
        }

        if (projectedSpeciesId == 0 && projectedForm == 0)
        {
            return true;
        }

        if (projectedSpeciesId <= 0 || projectedForm < 0)
        {
            diagnostics.Add(CreateDiagnostic(
                domain,
                subject,
                projectedSpeciesId,
                projectedForm,
                file,
                field));
            return false;
        }

        if (availability.HasKnownAvailability
            && availability.IsPresentSpeciesForm(projectedSpeciesId, projectedForm))
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            domain,
            subject,
            projectedSpeciesId,
            projectedForm,
            file,
            field,
            availability.HasKnownAvailability
                ? null
                : " Pokémon availability could not be loaded, so changed species/form pairs cannot be verified."));
        return false;
    }

    private static ValidationDiagnostic CreateDiagnostic(
        string domain,
        string subject,
        int speciesId,
        int form,
        string? file,
        string field,
        string? reason = null)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"{subject} cannot use species {speciesId}, form {form}.{reason}",
            domain,
            file,
            field,
            "An unchanged source pair, the empty (0, 0) sentinel, or an exact species/form pair present in loaded Pokémon Data.");
    }
}
