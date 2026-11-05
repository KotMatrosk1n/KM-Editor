// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Audio;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
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
            var extraPalettes = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Count(i => Profile(i).CompactResources);
            var names = prepared.Contributions.Where(c => c.Writer == SwShRestorationWriter.SpeciesText).Select(c => c.Path).Distinct().Count();
            var additionalModels = SwShRestorationDefinitions.Get(prepared.DefinitionId).AdditionalModelVariants.Count;
            var additionalIcons = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities.Sum(i => SwShRestorationComposer.IconGenders(i.Species).Count - 1);
            if (files.Count != 6 + 5 * speciesCount + extraPalettes + names + 3 * additionalModels + additionalIcons || files.Values.Any(v => v is null)) throw new InvalidDataException("Prepared restoration output set is incomplete.");
            var catalog = new SwShPokemonResourceCatalog(files[SwShRestorationComposer.CatalogPath]!);
            var personal = SwShPersonalTable.Parse(SwShRestorationForms.ApplyHeaders(files[SwShRestorationComposer.PersonalPath]!,
                SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities), 4096);
            var packs = new Dictionary<string, SwShGfPackFile>(StringComparer.Ordinal);
            foreach (var identity in SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities)
            {
                token.ThrowIfCancellationRequested();
                var species = identity.Species;
                var variants = new[] { (Gender: 0, Stem: SwShRestorationModelResources.PrimaryStem(identity)) }
                    .Concat(SwShRestorationDefinitions.Get(prepared.DefinitionId).AdditionalModelVariants.Where(v => v.Identity == identity)
                        .Select(v => (Gender: v.Gender, Stem: v.ResourceStem)));
                var personalId = SwShRestorationForms.PersonalRow(identity);
                if (!personal.Records[personalId].IsPresentInGame || personal.Records[personalId].HatchedSpecies != Profile(identity).HatchedSpecies || personal.Records[personalId].CanNotDynamax)
                    throw new InvalidDataException("Converted personal data does not support the declared contexts.");
                foreach (var variant in variants)
                {
                var stem = variant.Stem;
                foreach (var shiny in new[] { false, true })
                {
                    var row = catalog.Read(new(species, identity.Form, variant.Gender, shiny));
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
            if (newBank.Objects.Count != originalBank.Objects.Count + 72 * speciesCount || newBank.Media.Count != originalBank.Media.Count + 13 * speciesCount)
                throw new InvalidDataException("Converted voices are incomplete.");
            for (var i = 0; i < originalBank.Objects.Count; i++)
                if (originalBank.Objects[i].Id != newBank.Objects[i].Id || originalBank.Objects[i].Type != newBank.Objects[i].Type
                    || !originalBank.Objects[i].Payload.AsSpan().SequenceEqual(newBank.Objects[i].Payload))
                    throw new InvalidDataException("Voice composition changed an unrelated native object.");
            foreach (var pair in native)
            {
                token.ThrowIfCancellationRequested();
                var current = ReadPhysical(Path.Combine(target.BaseRomFsPath!, pair.Key.Replace('/', Path.DirectorySeparatorChar)), 64 * 1024 * 1024);
                if (!SHA256.HashData(current).AsSpan().SequenceEqual(SHA256.HashData(pair.Value)))
                    throw new InvalidDataException("Target sources changed during restoration preparation.");
            }
        }
    }
}
