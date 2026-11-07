// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Audio;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal void Verify(SwShPreparedRestoration prepared)
        {
            archive.VerifySource();
            var composition = SwShRestorationComposer.Compose(prepared.Contributions, path =>
            {
                var source = Path.Combine(target.BaseRomFsPath!, path[6..].Replace('/', Path.DirectorySeparatorChar));
                return System.IO.File.Exists(source) ? Native(path[6..]) : null;
            }, enable: true);
            var files = composition.Files;
            var speciesCount = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Count;
            var femaleCount = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Count(i => Profile(i).SeparateFemale);
            var itemFiles = prepared.Contributions.Where(SwShRestorationMegaItems.IsItemContribution).Select(c => c.Path).Distinct().Count();
            var additionalIcons = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Sum(i => SwShRestorationComposer.IconGenders(i.Species).Count - 1);
            var abilityFiles = prepared.Contributions.Where(c => c.Writer == SwShRestorationWriter.AbilityText).Select(c => c.Path).Distinct().Count();
            if (files.Count != 6 + 6 * speciesCount + 3 * femaleCount + itemFiles + additionalIcons + abilityFiles || files.Values.Any(v => v is null)) throw new InvalidDataException("Prepared restoration output set is incomplete.");
            var catalog = new SwShPokemonResourceCatalog(files[SwShRestorationComposer.CatalogPath]!);
            var personal = SwShPersonalTable.Parse(files[SwShRestorationComposer.PersonalPath]!, 4096);
            var packs = new Dictionary<string, SwShGfPackFile>(StringComparer.Ordinal);
            foreach (var identity in SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities)
            {
                token.ThrowIfCancellationRequested();
                var species = identity.Species;
                var profile = Profile(identity); var personalId = SwShRestorationForms.PersonalRow(identity);
                foreach (var gender in profile.SeparateFemale ? new[] { 0, 1 } : new[] { 0 })
                {
                var stem = SwShRestorationModelResources.Variants(SwShRestorationDefinitions.Get(prepared.DefinitionId))
                    .Single(v => v.Identity == identity && v.Gender == gender).ResourceStem;
                if (!personal.Records[personalId].IsPresentInGame || personal.Records[personalId].HatchedSpecies != profile.HatchedSpecies || personal.Records[personalId].CanNotDynamax)
                    throw new InvalidDataException("Converted personal data does not support the declared contexts.");
                foreach (var shiny in new[] { false, true })
                {
                    var row = catalog.Read(new(species, identity.Form, gender, shiny));
                    var packPath = "romfs/" + row.Archive;
                    if (!packs.TryGetValue(packPath, out var pack))
                    { pack = SwShGfPackFile.Parse(files[packPath]!); packs.Add(packPath, pack); }
                    _ = pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(row.Configuration), 1024 * 1024);
                    var scene = new SwShPreviewReader(path =>
                    {
                        if (path == row.Model) return pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(path), 32 * 1024 * 1024);
                        return Member(pack, Path.GetFileName(path));
                    }).Load(row.Model);
                    if (!scene.Rig.Bones.Any(b => b.Name == "EffGcloud01") || !scene.Rig.Bones.Any(b => b.Name == "EffMouth01"))
                        throw new InvalidDataException("Converted model effect attachments are incomplete.");
                    foreach (var controllerPath in row.Controllers.Values)
                    {
                        var controller = new ModelBuffer(pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(controllerPath), 16 * 1024 * 1024));
                        var bindings = controller.Table(controller.Root, 6);
                        foreach (var binding in controller.Tables(bindings, 0, 2048))
                        {
                            var animation = controller.Text(binding, 1) ?? throw new InvalidDataException("Converted animation binding is missing.");
                            var path = TrinityPreviewReader.Resolve(controllerPath, animation);
                            var bytes = pack.ReadFileByHash(SwShGfPackFile.HashMemberPath(path), 16 * 1024 * 1024);
                            var data = new ModelBuffer(bytes);
                            _ = PreviewRigReader.Animation(data, path, scene.Rig.Bones);
                        }
                    }
                }
                var giant = SwShGfPackFile.Parse(files[$"romfs/bin/archive/gpokemon/{stem}.gfpak"]!);
                _ = giant.ReadFileByHash(SwShGfPackFile.HashMemberPath($"bin/pokemon/{stem}/{stem}.gfbgpokecfg"), 1024 * 1024);
                foreach (var iconGender in SwShRestorationComposer.IconGenders(species))
                    _ = new ModelTextureDocument(files[SwShRestorationComposer.IconPath(species, identity.Form, iconGender)]!);
                }
            }
            var originalBank = new SoundBankDocument(Native(SwShRestorationComposer.VoicesPath[6..]));
            var newBank = new SoundBankDocument(files[SwShRestorationComposer.VoicesPath]!);
            VerifyVoiceCompositionWithSilences(originalBank, newBank, prepared.Contributions,
                SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities, silentVoiceStates);
            foreach (var pair in native)
            {
                token.ThrowIfCancellationRequested();
                var current = ReadPhysical(Path.Combine(target.BaseRomFsPath!, pair.Key.Replace('/', Path.DirectorySeparatorChar)), 64 * 1024 * 1024);
                if (!SHA256.HashData(current).AsSpan().SequenceEqual(SHA256.HashData(pair.Value)))
                    throw new InvalidDataException("Target sources changed during restoration preparation.");
            }
        }

        private static void VerifyVoiceComposition(SoundBankDocument original, SoundBankDocument converted,
            IReadOnlyList<SwShRestorationContribution> contributions, IReadOnlyList<KM.SwSh.Pokemon.SwShPokemonIdentity> identities)
            => VerifyVoiceCompositionWithSilences(original, converted, contributions, identities, new Dictionary<KM.SwSh.Pokemon.SwShPokemonIdentity, IReadOnlySet<string>>());

        private static void VerifyVoiceCompositionWithSilences(SoundBankDocument original, SoundBankDocument converted,
            IReadOnlyList<SwShRestorationContribution> contributions, IReadOnlyList<KM.SwSh.Pokemon.SwShPokemonIdentity> identities,
            IReadOnlyDictionary<KM.SwSh.Pokemon.SwShPokemonIdentity, IReadOnlySet<string>> silences)
        {
            IReadOnlySet<string>? Silent(KM.SwSh.Pokemon.SwShPokemonIdentity i) => silences.TryGetValue(i, out var states) ? states : null;
            var voices = contributions.Where(c => c.Writer == SwShRestorationWriter.Voices).ToArray();
            var objects = voices.Where(c => !c.Key.StartsWith("media:", StringComparison.Ordinal)).ToDictionary(c => c.Key, StringComparer.Ordinal);
            var media = voices.Where(c => c.Key.StartsWith("media:", StringComparison.Ordinal)).ToDictionary(c => uint.Parse(c.Key[6..], System.Globalization.CultureInfo.InvariantCulture));
            var requiredObjects = identities.SelectMany(i => SwShVoiceWriter.ObjectIds(i.Species, i.Form, silentStates: Silent(i))).ToHashSet();
            var allowedObjects = identities.SelectMany(i => SwShVoiceWriter.ObjectIds(i.Species, i.Form, 2, Silent(i))).ToHashSet();
            var actualObjects = objects.Keys.Select(k => uint.Parse(k.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            var requiredMedia = identities.SelectMany(i => SwShVoiceWriter.States.Where(s => Silent(i)?.Contains(s) != true).Select(s => SwShVoiceWriter.MediaId(i.Species, s, i.Form))).ToHashSet();
            var allowedMedia = identities.SelectMany(i => SwShVoiceWriter.States.Where(s => Silent(i)?.Contains(s) != true).SelectMany(s => new[] { SwShVoiceWriter.MediaId(i.Species, s, i.Form), SwShVoiceWriter.MediaId(i.Species, s, i.Form, 1) })).ToHashSet();
            if (!requiredObjects.IsSubsetOf(actualObjects) || !actualObjects.IsSubsetOf(allowedObjects)
                || !requiredMedia.IsSubsetOf(media.Keys) || !media.Keys.All(allowedMedia.Contains)
                || converted.Objects.Count != original.Objects.Count + objects.Count || converted.Media.Count != original.Media.Count + media.Count)
                throw new InvalidDataException("Converted voices are incomplete or contain unowned layers.");
            var convertedObjects = converted.Objects.ToDictionary(o => $"{o.Type}:{o.Id}", StringComparer.Ordinal);
            var convertedMedia = converted.Media.ToDictionary(m => m.Id);
            foreach (var item in objects)
                if (!convertedObjects.TryGetValue(item.Key, out var node) || !node.Payload.AsSpan().SequenceEqual(item.Value.After))
                    throw new InvalidDataException("Voice composition changed a converted object.");
            foreach (var item in media)
                if (!convertedMedia.TryGetValue(item.Key, out var clip) || !clip.Bytes.AsSpan().SequenceEqual(item.Value.After))
                    throw new InvalidDataException("Voice composition changed converted media.");
            foreach (var node in original.Objects)
                if (!convertedObjects.TryGetValue($"{node.Type}:{node.Id}", out var current) || !current.Payload.AsSpan().SequenceEqual(node.Payload))
                    throw new InvalidDataException("Voice composition changed an unrelated native object.");
            foreach (var clip in original.Media)
                if (!convertedMedia.TryGetValue(clip.Id, out var current) || !current.Bytes.AsSpan().SequenceEqual(clip.Bytes))
                    throw new InvalidDataException("Voice composition changed unrelated native media.");
        }
    }
}
