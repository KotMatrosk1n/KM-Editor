// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal IReadOnlyList<SwShRestorationContribution> AbilityText(SwShPokemonIdentity identity)
        {
            if (TryProfile(identity, out var profile) && profile.PlaAssets) return [];
            var megaAbility = SwShRestorationAbilities.MegaAbilityFor(identity);
            var source = new ModelBuffer(Donor("avalon/data/personal_array.bin"));
            var table = source.Tables(source.Root, 0, 4096).Single(t => source.Field(t, 0) is var at && at != 0
                && source.U16(at + 4) == identity.Species && source.U16(at + 2) == identity.Form);
            var abilities = megaAbility != 0 ? (SwShRestorationAbilities.HasHandler(megaAbility) ? [megaAbility] : Array.Empty<int>()) : Enumerable.Range(5, 3)
                .Select(field => source.Field(table, field) is var at && at != 0 ? (int)source.U16(at) : 0)
                .Where(SwShRestorationAbilities.HasHandler).Distinct().ToArray();
            var result = new List<SwShRestorationContribution>();
            foreach (var ability in abilities)
            {
                if (!SwShRestorationAbilities.IsAbilityFor(identity, ability))
                    throw new InvalidDataException("The source ability is outside the registered identity.");
                var descriptionText = ability switch
                {
                    SwShRestorationAbilities.LingeringAroma => "Contact with this Pokemon replaces the attacker's Ability with Lingering Aroma.",
                    SwShRestorationAbilities.SeedSower => "Creates Grassy Terrain when hit by a damaging move.",
                    SwShRestorationAbilities.ThermalExchange => "Fire moves raise its Attack. It cannot be burned.",
                    SwShRestorationAbilities.AngerShell => "At half HP after a hit, raises Attack, Sp. Atk and Speed but lowers Defense and Sp. Def.",
                    SwShRestorationAbilities.PurifyingSalt => "Prevents status conditions and halves damage from Ghost-type moves.",
                    SwShRestorationAbilities.WellBakedBody => "Fire moves raise its Defense sharply instead of damaging it.",
                    SwShRestorationAbilities.WindRider => "Tailwind raises its Attack. Wind moves raise its Attack instead of damaging it.",
                    SwShRestorationAbilities.GuardDog => "Intimidate raises its Attack. It cannot be forced out by moves or items.",
                    SwShRestorationAbilities.RockyPayload => "Powers up the Pokemon's Rock-type moves.",
                    SwShRestorationAbilities.WindPower => "Wind moves and Tailwind charge its next Electric-type move.",
                    SwShRestorationAbilities.ZeroToHero => "Changes into Hero Form when withdrawn from battle.",
                    SwShRestorationAbilities.Commander => "Enters an ally Dondozo's mouth and sharply raises its stats.",
                    SwShRestorationAbilities.Electromorphosis => "Taking damage charges its next Electric-type move.",
                    SwShRestorationAbilities.Protosynthesis => "Boosts its highest stat in harsh sunlight or by consuming Booster Energy.",
                    SwShRestorationAbilities.QuarkDrive => "Boosts its highest stat on Electric Terrain or by consuming Booster Energy.",
                    SwShRestorationAbilities.GoodAsGold => "Protects the Pokemon from other Pokemon's status moves.",
                    SwShRestorationAbilities.VesselOfRuin => "Lowers the Sp. Atk of all other active Pokemon.",
                    SwShRestorationAbilities.SwordOfRuin => "Lowers the Defense of all other active Pokemon.",
                    SwShRestorationAbilities.TabletsOfRuin => "Lowers the Attack of all other active Pokemon.",
                    SwShRestorationAbilities.BeadsOfRuin => "Lowers the Sp. Def of all other active Pokemon.",
                    SwShRestorationAbilities.OrichalcumPulse => "Creates harsh sunlight on entry and boosts its Attack in harsh sunlight.",
                    SwShRestorationAbilities.HadronEngine => "Creates Electric Terrain on entry and boosts its Sp. Atk on Electric Terrain.",
                    SwShRestorationAbilities.Opportunist => "Copies opposing Pokemon's stat boosts.",
                    SwShRestorationAbilities.CudChew => "Eats a consumed Berry again at the end of the following turn.",
                    SwShRestorationAbilities.Sharpness => "Powers up slicing moves.",
                    SwShRestorationAbilities.SupremeOverlord => "Powers up moves for each fainted party member, up to five.",
                    SwShRestorationAbilities.Costar => "Copies an ally's stat changes on entry.",
                    SwShRestorationAbilities.ToxicDebris => "Scatters Toxic Spikes when hit by a physical move.",
                    SwShRestorationAbilities.ArmorTail => "Protects the Pokemon and its allies from opposing priority moves.",
                    SwShRestorationAbilities.EarthEater => "Ground-type moves restore HP instead of causing damage.",
                    SwShRestorationAbilities.MyceliumMight => "Status moves act last within their priority and ignore target Abilities.",
                    SwShRestorationAbilities.PiercingDrill => "Contact moves pierce protection, dealing one quarter of their usual damage.",
                    SwShRestorationAbilities.MegaSol => "The Pokemon uses its moves as though the sunlight were harsh.",
                    SwShRestorationAbilities.Eelevate => "Floats above the ground. Knocking out a Pokemon boosts its highest stat.",
                    SwShRestorationAbilities.AuraGuard => "Halves damage taken from moves that make contact.",
                    SwShRestorationAbilities.Dragonize => "Normal-type moves become Dragon-type moves and their power is boosted by 20%.",
                    SwShRestorationAbilities.SpicySpray => "When hit by a damaging move, the Pokemon burns the attacker.",
                    SwShRestorationAbilities.FireMane => "Powers up the Pokemon's Fire-type moves.",
                    _ => throw new InvalidDataException("The source ability description is unavailable."),
                };
                // Z-A has no localized conventional ability text. Supply authored
                // English in each supported language until translations exist.
                foreach (var language in SwShRestorationSpeciesText.Languages)
                foreach (var description in new[] { false, true })
                {
                    var path = SwShRestorationAbilities.TextPath(language, description);
                    var native = SwShGameTextFile.Parse(Native(path[6..]), 512);
                    if (native.Lines.Count != SwShRestorationAbilities.NativeTextCount)
                        throw new InvalidDataException("The native ability text layout is incompatible.");
                    var line = new SwShGameTextLine(description ? descriptionText : SwShRestorationAbilities.EnglishName(ability), 0);
                    SwShGameTextFile.ValidateText(line.Text);
                    result.Add(new(path, SwShRestorationWriter.AbilityText, ability.ToString(CultureInfo.InvariantCulture), null,
                        JsonSerializer.SerializeToUtf8Bytes(new SwShRestoredAbilityText(native.Lines.Count, line))));
                }
            }
            return result;
        }
    }
}
