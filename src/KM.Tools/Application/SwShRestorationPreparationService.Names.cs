// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal IReadOnlyList<SwShRestorationContribution> Names(SwShPokemonIdentity identity)
        {
            if (identity.Species <= SwShEffectivePokemonCatalog.MaximumSpecies) return [];
            var result = new List<SwShRestorationContribution>();
            foreach (var language in SwShRestorationSpeciesText.Languages)
            {
                var path = SwShRestorationSpeciesText.Path(language);
                var native = SwShGameTextFile.Parse(Native(path[6..]), 2048);
                if (native.Lines.Count != 899) throw new InvalidDataException("The native species name layout is incompatible.");
                // S/V text and voices use development IDs, independently of the
                // National species and the model resource number.
                var source = SwShGameTextFile.Parse(Donor($"message/dat/{language}/common/monsname.dat"), 2048);
                var sourceId = Profile(identity).SourceSpecies
                    ?? throw new InvalidDataException("The new species source name identity is missing.");
                if (sourceId >= source.Lines.Count || string.IsNullOrWhiteSpace(source.Lines[sourceId].Text))
                    throw new InvalidDataException("The new species source name is unavailable.");
                var line = source.Lines[sourceId];
                if (language == "English" && line.Text != SwShRestorationSpecies.DisplayName(identity.Species))
                    throw new InvalidDataException("The source species name does not match the registered identity.");
                SwShGameTextFile.ValidateText(line.Text);
                result.Add(new(path, SwShRestorationWriter.SpeciesText, identity.Species.ToString(System.Globalization.CultureInfo.InvariantCulture), null,
                    JsonSerializer.SerializeToUtf8Bytes(new SwShRestoredSpeciesName(native.Lines.Count, line))));
            }
            return result;
        }
    }
}
