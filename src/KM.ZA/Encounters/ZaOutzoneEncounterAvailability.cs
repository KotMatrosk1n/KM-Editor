// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.Encounters;

internal sealed class ZaOutzoneEncounterAvailability
{
    private readonly IReadOnlySet<(int SpeciesId, int Form)>? supportedPairs;

    private ZaOutzoneEncounterAvailability(
        IReadOnlySet<(int SpeciesId, int Form)>? supportedPairs)
    {
        this.supportedPairs = supportedPairs;
    }

    public static ZaOutzoneEncounterAvailability Unknown { get; } = new(null);

    public bool HasKnownAvailability => supportedPairs is not null;

    public static ZaOutzoneEncounterAvailability Create(
        IEnumerable<(int SpeciesId, int Form)> supportedPairs)
    {
        ArgumentNullException.ThrowIfNull(supportedPairs);
        return new ZaOutzoneEncounterAvailability(supportedPairs.ToHashSet());
    }

    public bool IsObserved(int speciesId, int form)
    {
        return supportedPairs?.Contains((speciesId, form)) == true;
    }
}
