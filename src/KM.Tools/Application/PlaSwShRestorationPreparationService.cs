// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

/// <summary>Converts the registered PLA donors with their own archive, gameplay, and voice adapters.</summary>
public sealed class PlaSwShRestorationPreparationService
{
    public SwShPreparedRestoration Prepare(ProjectPaths target, string donorRomFs, string? compressionSupport,
        string definitionId, IProgress<SwShRestorationPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default, SwShRestorationDonorSession? donorSession = null)
    {
        var definition = SwShRestorationDefinitions.Get(definitionId);
        if (definition.Donor != SwShRestorationDonor.LegendsArceus
            || definition.Identities.Any(i => i.Form != 0 || i.Species is not (46 or 47 or 265 or 266 or 267 or 268 or 269)))
            throw new InvalidDataException("The PLA restoration adapter is not registered for this identity.");
        // Only the verified Trinity geometry, material, and curve fields share writers.
        return new ZaSwShRestorationPreparationService().Prepare(target, donorRomFs, compressionSupport,
            definitionId, progress, cancellationToken, donorSession);
    }
}
