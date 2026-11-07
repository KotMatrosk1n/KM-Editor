// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal IReadOnlyList<SwShRestorationContribution> AbilityText(SwShPokemonIdentity identity)
        {
            var source = new ModelBuffer(Donor("avalon/data/personal_array.bin"));
            var table = source.Tables(source.Root, 0, 4096).Single(t => source.Field(t, 0) is var at && at != 0
                && source.U16(at + 4) == identity.Species && source.U16(at + 2) == identity.Form);
            var abilities = Enumerable.Range(7, 3).Select(field => source.Field(table, field) is var at && at != 0 ? source.U16(at) : 0)
                .Where(SwShRestorationAbilities.HasHandler).Distinct().ToArray();
            var result = new List<SwShRestorationContribution>();
            foreach (var ability in abilities)
            {
                if (!SwShRestorationAbilities.IsAbilityFor(identity, ability)) throw new InvalidDataException("The source ability is outside the registered identity.");
                foreach (var language in SwShRestorationSpeciesText.Languages)
                foreach (var description in new[] { false, true })
                {
                    var path = SwShRestorationAbilities.TextPath(language, description);
                    var native = SwShGameTextFile.Parse(Native(path[6..]), 512);
                    if (native.Lines.Count != SwShRestorationAbilities.NativeTextCount)
                        throw new InvalidDataException("The native ability text layout is incompatible.");
                    var text = SwShGameTextFile.Parse(Donor($"message/dat/{language}/common/{(description ? "tokuseiinfo" : "tokusei")}.dat"), 512);
                    if (ability >= text.Lines.Count || string.IsNullOrWhiteSpace(text.Lines[ability].Text)
                        || language == "English" && !description && text.Lines[ability].Text != SwShRestorationAbilities.EnglishName(ability))
                        throw new InvalidDataException("The source ability text is missing or mismatched.");
                    SwShGameTextFile.ValidateText(text.Lines[ability].Text);
                    result.Add(new(path, SwShRestorationWriter.AbilityText, ability.ToString(CultureInfo.InvariantCulture), null,
                        JsonSerializer.SerializeToUtf8Bytes(new SwShRestoredAbilityText(native.Lines.Count, text.Lines[ability]))));
                }
            }
            return result;
        }
    }
}
