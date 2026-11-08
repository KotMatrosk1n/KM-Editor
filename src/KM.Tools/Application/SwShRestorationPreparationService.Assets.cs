// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.SV;
using KM.Formats.ZA;
using KM.SwSh.Restoration;
using KM.SwSh.Pokemon;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static readonly IReadOnlyDictionary<string, string> NativeClips = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ba01_landA01"] = "00152", ["ba01_landB01"] = "00153", ["ba01_landC01"] = "00155",
            ["ba10_waitA01"] = "00001", ["ba10_waitB01"] = "00010", ["ba20_buturi01"] = "00400", ["ba21_tokusyu01"] = "00450",
            ["ba30_damageS01"] = "00500", ["ba41_down01"] = "00520", ["eye01"] = "08000", ["mouth01"] = "08100", ["ba02_roar01"] = "00300",
            ["fi20_walk01"] = "00030", ["fi21_run01"] = "00100", ["kw20_drowseA01"] = "00280", ["kw20_drowseB01"] = "00281", ["kw20_drowseC01"] = "00282",
            ["kw21_sleepA01"] = "00280", ["kw21_sleepB01"] = "00281", ["kw30_hate01"] = "00563", ["kw32_happyA01"] = "00550", ["kw32_happyB01"] = "00011",
            ["kw32_happyC01"] = "00550", ["kw33_moveA01"] = "00150", ["kw33_moveC01"] = "00155", ["kw34_lonely01"] = "00560", ["kw35_playA01"] = "00011",
            ["kw36_mad01"] = "00563", ["kw50_eatA01"] = "00290", ["kw50_eatB01"] = "00291", ["kw50_eatC01"] = "00292", ["kw60_touch01"] = "00320",
        };
        internal IReadOnlyList<SwShRestorationContribution> Assets(SwShPokemonIdentity pokemon)
        {
            var species = pokemon.Species; currentIdentity = pokemon; currentSpecies = species; Report("reading");
            var profile = Profile(pokemon);
            SwShRestorationModelResources.RequireUnowned(Native("bin/pokemon/table/poke_resource_table.gfbpmcatalog"), SwShRestorationDefinitions.Get(definitionId));
            var variants = new[] { (Gender: 0, Stem: SwShRestorationModelResources.PrimaryStem(pokemon), Source: profile.SourceStem ?? $"pm{species:0000}_{pokemon.Form:00}_00") }
                .Concat(SwShRestorationDefinitions.Get(definitionId).AdditionalModelVariants.Where(v => v.Identity == pokemon)
                    .Select(v => (Gender: v.Gender, Stem: v.ResourceStem, Source: v.SourceResourceStem ?? $"pm{species:0000}_{v.Gender:00}_00"))).ToArray();
            SwShRestorationGenderAudit.RequireModels(Donor("pokemon/catalog/catalog/poke_resource_table.trpmcatalog"),
                profile.SourceSpecies ?? species, pokemon.Form, variants.ToDictionary(v => v.Gender, v => $"{v.Source[..6]}/{v.Source}/{v.Source}.trmdl"));
            var scenes = variants.ToDictionary(v => v.Gender, v =>
            {
                var scene = new TrinityPreviewReader(Donor).Load($"pokemon/data/{v.Source[..6]}/{v.Source}/{v.Source}.trmdl");
                return profile.NormalSectionPartitions is { } partitions ? SwShNormalSections.Split(scene, partitions) : scene;
            });
            var declaredMeshes = scenes.Values.SelectMany(s => s.Primitives.Select(p => p.Name)).ToHashSet(StringComparer.Ordinal);
            var result = variants.SelectMany(v => Assets(pokemon, v.Gender, v.Stem, v.Source, scenes[v.Gender], declaredMeshes)).ToList();
            var icon = SwShRestorationPartyIcons.Create(species, Native, token, pokemon.Form, textureEncoding);
            foreach (var iconGender in SwShRestorationComposer.IconGenders(species))
                result.Add(File(SwShRestorationComposer.IconPath(species, pokemon.Form, iconGender)[6..], icon));
            return result;
        }

        private IReadOnlyList<SwShRestorationContribution> Assets(SwShPokemonIdentity pokemon, int gender, string stem, string sourceStem,
            PreviewScene scene, IReadOnlySet<string> declaredMeshes)
        {
            var species = pokemon.Species; var profile = Profile(pokemon);
            var sourceFolder = $"pokemon/data/{sourceStem[..6]}/{sourceStem}/";
            var graph = ModelAssetGraph.Read(new[] { (sourceFolder + sourceStem + ".trmdl", (string?)null),
                (sourceFolder + sourceStem + "_rare.trmtr", null), (sourceFolder + sourceStem + "_base.tracn", null) }, (path, _) => Donor(path));
            Report("reading");
            if (scene.Rig.Warnings.Length > 0) throw new InvalidDataException("Source model requires an additional conversion adapter.");
            var detailHierarchy = profile.RemoveDetailLevels == 0 ? null : SvSwShDetailMeshHierarchy.Remove(scene, profile.RemoveDetailLevels);
            if (detailHierarchy is not null) scene = detailHierarchy.Scene;
            if (profile.NativeFlames && profile.FlameMaterial != "fire")
                scene = scene with { Primitives = scene.Primitives.Select(p => p.Material.Name == profile.FlameMaterial
                    ? p with { Material = p.Material with { Name = "fire" } } : p).ToArray() };
            var selectedMaterials = new Dictionary<bool, byte[]>();
            byte[] Materials(bool rare)
            {
                if (selectedMaterials.TryGetValue(rare, out var selected)) return selected;
                var bytes = Donor(sourceFolder + sourceStem + (rare ? "_rare" : "") + ".trmtr");
                if (profile.ColorSelector is { } selector)
                    bytes = SvMaterialColorSelector.Apply(Donor(sourceFolder + sourceStem + ".trmmt"), bytes, rare, selector, scene);
                if (profile.NativeFlames) bytes = SvSwShFlameMaterial.NormalizeName(bytes, profile.FlameMaterial);
                selectedMaterials[rare] = bytes;
                return bytes;
            }
            var flameAtlas = profile.NativeFlames && SvSwShFlameMaterial.IsUndisplaced(Materials(false));
            var targetScene = UniqueMaterials(profile.CompactResources ? CompactGeometry(scene) : scene);
            if (profile.StaticUvScales is not null)
                targetScene = targetScene with { Primitives = targetScene.Primitives.Select((p, index) =>
                    StaticAtlas(scene.Primitives[index])?.Geometry(p) ?? p).ToArray() };
            var flames = profile.NativeFlames ? scene.Primitives.Select((p, i) => (p, i)).Where(p => p.p.Material.Name == "fire")
                .Select(p => targetScene.Primitives[p.i].Material.Name).ToArray() : [];
            if (flameAtlas)
                targetScene = targetScene with { Primitives = targetScene.Primitives.Select(p => flames.Contains(p.Material.Name, StringComparer.Ordinal)
                    ? SvSwShFlameMaterial.AtlasGeometry(p) : p).ToArray() };
            if (profile.NativeFlames)
            {
                var masks = targetScene.Primitives.Where(p => flames.Contains(p.Material.Name, StringComparer.Ordinal))
                    .Select(flame => flame with { Name = flame.Name + "_mask", Material = flame.Material with { Name = flame.Material.Name + "_mask" } });
                targetScene = targetScene with { Primitives = targetScene.Primitives.Concat(masks).ToArray() };
            }
            var nativePack = SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0007_00.gfpak"));
            var bodyModel = Member(nativePack, "pm0007_00.gfbmdl");
            var firePack = profile.NativeFlames ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0004_00.gfpak")) : null;
            var materialBytes = Materials(false);
            var sourceMaterials = new ModelBuffer(materialBytes);
            var eyeMaterials = sourceMaterials.Tables(sourceMaterials.Root, 1, 128)
                .Where(material => SwShMaterialBaker.IsEyeMaterial(sourceMaterials, material))
                .Select(material => sourceMaterials.Text(material, 0)!).ToHashSet(StringComparer.Ordinal);
            var originTransparent = species is 483 or 484 && currentIdentity.Form == 1;
            var transparent = profile.TransparentEyes || profile.TransparentBody || originTransparent;
            var transparentPack = transparent ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0577_00.gfpak")) : null;
            var fireModel = firePack is null ? null : Member(firePack, "pm0004_00.gfbmdl");
            (byte[] Model, Dictionary<string, SvSwShFlameTemplate> Parts) FlameTemplate(byte[] palette)
            {
                var model = bodyModel; var parts = new Dictionary<string, SvSwShFlameTemplate>(StringComparer.Ordinal);
                foreach (var name in flames)
                {
                    var template = SvSwShFlameMaterial.Template(model, fireModel!, palette, materialBytes, parts.Count, profile.FlameColorExposure);
                    model = template.Model; parts.Add(name, template);
                }
                return (model, parts);
            }
            var nativeModel = FlameTemplate(materialBytes).Model;
            var eyePack = profile.LayeredEyes ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0001_00.gfpak")) : null;
            var eyeContracts = profile.LayeredEyes ? ZaSwShEyelidMaterial.Materials(materialBytes, scene, layerEmission: profile.LayeredEyeEmission,
                cornealPrimaryNormalHeights: profile.CornealPrimaryNormalHeights) : null;
            var cornealPack = profile.CornealEyes ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0060_00.gfpak")) : null;
            if (cornealPack is not null) nativeModel = SvSwShCornealMaterial.Template(nativeModel, Member(cornealPack, "pm0060_00.gfbmdl"));
            if (eyePack is not null) nativeModel = ZaSwShEyelidMaterial.Template(nativeModel, Member(eyePack, "pm0001_00.gfbmdl"), profile.CornealEyes, layerEmission: profile.LayeredEyeEmission);
            if (transparentPack is not null)
            {
                SvSwShTransparentMaterial.RequireSource(materialBytes, scene, profile.TransparentBody);
                nativeModel = SvSwShTransparentMaterial.Template(nativeModel, Member(transparentPack, "pm0577_00.gfbmdl"));
            }
            var emissionContracts = profile.LayerEmission ? SvSwShLayerEmission.Contracts(materialBytes, scene) : null;
            if (emissionContracts is not null) nativeModel = SvSwShLayerEmission.Templates(nativeModel, emissionContracts);
            var lavaContracts = profile.LavaLayers ? SvSwShLavaMaterial.Contracts(materialBytes, scene) : null;
            var flowContracts = profile.SurfaceFlow ? SvSwShSurfaceFlow.Contracts(materialBytes, scene,
                name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name))) : null;
            if (flowContracts is not null) nativeModel = SvSwShLayerEmission.Template(nativeModel, SvSwShSurfaceFlow.Program(flowContracts));
            var flowNames = targetScene.Primitives.Take(scene.Primitives.Count).Select((p, i) => (p, i))
                .Where(p => flowContracts?.ContainsKey(scene.Primitives[p.i].Material.Name) == true).Select(p => p.p.Material.Name).ToHashSet(StringComparer.Ordinal);
            var emissionNames = targetScene.Primitives.Take(scene.Primitives.Count).Select((p, i) => (p, i)).Where(p => emissionContracts?.ContainsKey(scene.Primitives[p.i].Material.Name) == true)
                .Select(p => p.p.Material.Name).ToHashSet(StringComparer.Ordinal);
            // Appended flame masks have no additional source primitive.
            var originalNames = targetScene.Primitives.Take(scene.Primitives.Count).Select((p, i) => (p.Material.Name, Original: scene.Primitives[i].Material.Name))
                .ToDictionary(p => p.Name, p => p.Original, StringComparer.Ordinal);
            var baseEmissionNames = originalNames.Where(p => emissionContracts?.GetValueOrDefault(p.Value)?.BaseColors is not null)
                .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var coverageNames = originalNames.Where(p => emissionContracts?.GetValueOrDefault(p.Value)?.NormalizedCoverage == true)
                .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var separateBaseNames = originalNames.Where(p => emissionContracts?.GetValueOrDefault(p.Value)?.SeparateBaseColors == true)
                .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var baseAlphas = originalNames.Where(p => emissionContracts?.GetValueOrDefault(p.Value)?.BaseColors is not null)
                .ToDictionary(p => p.Key, p => emissionContracts![p.Value].SourceBaseAlphas, StringComparer.Ordinal);
            var lavaNames = lavaContracts is null ? null : originalNames.Where(p => lavaContracts.ContainsKey(p.Value)).ToDictionary(p => p.Key, p => lavaContracts[p.Value], StringComparer.Ordinal);
            var innerSections = transparent ? scene.Primitives.Select((p, i) => (p, i))
                .Where(pair => originTransparent ? pair.p.Name == (species == 483 ? "pm0483_12_00_gem_mesh_shape" : "pm0484_12_00_cover_mesh_shape")
                    : profile.TransparentBody ? emissionContracts?.GetValueOrDefault(pair.p.Material.Name)?.Transparent == true : pair.p.Material.Name == "l_eye_b").Select(pair => pair.i).ToArray() : [];
            if (transparent)
            {
                if (profile.TransparentEyes && profile.CornealEyes || profile.NativeFlames) throw new InvalidDataException("Combined transparent material adapters are not registered.");
                targetScene = targetScene with { Primitives = targetScene.Primitives.Concat(innerSections.Select(i =>
                    SvSwShTransparentMaterial.Inner(targetScene.Primitives[i]))).ToArray() };
                foreach (var i in innerSections)
                {
                    var name = targetScene.Primitives[i].Material.Name;
                    originalNames.Add(name + "_inner", scene.Primitives[i].Material.Name);
                    if (emissionNames.Contains(name)) emissionNames.Add(name + "_inner");
                    if (baseEmissionNames.Contains(name)) baseEmissionNames.Add(name + "_inner");
                    if (coverageNames.Contains(name)) coverageNames.Add(name + "_inner");
                    if (separateBaseNames.Contains(name)) separateBaseNames.Add(name + "_inner");
                }
            }
            byte[] EmissionShader(SwShGfPackFile pack, string extension, string program)
            {
                var pass = Array.IndexOf(SvSwShLayerEmission.TransparentPrograms, program);
                var basePass = Array.IndexOf(SvSwShLayerEmission.TransparentBasePrograms, program);
                var sourceBasePass = Array.IndexOf(SvSwShSourceBase.TransparentPrograms, program);
                var coveragePass = Array.IndexOf(SvSwShLayerEmission.CoveragePrograms, program);
                var sourceCoveragePass = Array.IndexOf(SvSwShSourceCoverage.Programs, program);
                if (basePass >= 0) pass = basePass;
                if (sourceBasePass >= 0) pass = sourceBasePass;
                if (coveragePass >= 0) pass = coveragePass;
                if (sourceCoveragePass >= 0) pass = sourceCoveragePass;
                var name = pass < 0 ? "Body00" : SvSwShTransparentMaterial.NativePrograms[pass];
                return extension != ".bnsh_fsh" ? Member(pack, name + extension) : sourceCoveragePass >= 0
                    ? SvSwShLayerEmissionShader.TransparentSourceCoverage(Member(pack, name + extension)) : coveragePass >= 0
                    ? SvSwShLayerEmissionShader.TransparentCoverage(Member(pack, name + extension)) : sourceBasePass >= 0
                    ? SvSwShLayerEmissionShader.TransparentSourceBase(Member(pack, name + extension)) : basePass >= 0
                    ? SvSwShLayerEmissionShader.TransparentLayerBase(Member(pack, name + extension)) : program == SvSwShLayerEmission.MapProgramName
                    ? SvSwShLayerEmissionShader.ColorMap(Member(pack, name + extension)) : program == SvSwShLayerEmission.LavaProgramName
                    ? SvSwShLayerEmissionShader.Lava(Member(pack, name + extension)) : program == SvSwShLayerEmission.BaseProgramName
                    ? SvSwShLayerEmissionShader.LayerBase(Member(pack, name + extension)) : program == SvSwShLayerEmission.MirroredProgramName
                    ? SvSwShLayerEmissionShader.MirroredMask(Member(pack, name + extension)) : program == SvSwShLayerEmission.UniformProgramName
                    ? SvSwShLayerEmissionShader.UniformEmission(Member(pack, name + extension)) : program == SvSwShLayerEmission.OpaqueCoverageProgramName
                    ? SvSwShLayerEmissionShader.OpaqueCoverage(Member(pack, name + extension)) : program == SvSwShSourceCoverage.OpaqueProgramName
                    ? SvSwShLayerEmissionShader.OpaqueSourceCoverage(Member(pack, name + extension)) : program == SvSwShSourceBase.ProgramName
                    ? SvSwShLayerEmissionShader.SourceLayerBase(Member(pack, name + extension)) : program == SvSwShSourceBase.MirroredProgramName
                    ? SvSwShLayerEmissionShader.MirroredSourceBase(Member(pack, name + extension)) : program == SvSwShSourceBase.UnlitProgramName
                    ? SvSwShLayerEmissionShader.SourceUnlitBase(Member(pack, name + extension)) : SvSwShLayerEmissionShader.Convert(Member(pack, name + extension));
            }
            bool TransparentEmission(string program) => SvSwShLayerEmission.TransparentPrograms.Contains(program, StringComparer.Ordinal)
                || SvSwShLayerEmission.TransparentBasePrograms.Contains(program, StringComparer.Ordinal)
                || SvSwShSourceBase.TransparentPrograms.Contains(program, StringComparer.Ordinal)
                || SvSwShLayerEmission.CoveragePrograms.Contains(program, StringComparer.Ordinal)
                || SvSwShSourceCoverage.Programs.Contains(program, StringComparer.Ordinal);
            byte[] FlowShader(SwShGfPackFile pack, string extension, bool layers) => extension != ".bnsh_fsh"
                ? layers ? SvSwShSurfaceLayerVertexShader.Convert(Member(pack, "Body00" + extension)) : Member(pack, "Body00" + extension)
                : layers ? SvSwShLayerEmissionShader.SurfaceLayers(Member(pack, "Body00" + extension))
                : SvSwShLayerEmissionShader.SurfaceFlow(Member(pack, "Body00" + extension));
            var nativeMetadata = new ModelBuffer(nativeModel);
            int MaterialProgram(string name)
            {
                var (start, _) = nativeMetadata.Vector(nativeMetadata.Root, 3, 4, 256);
                var materials = nativeMetadata.Tables(nativeMetadata.Root, 6, 128);
                return Enumerable.Range(0, materials.Length).Single(i => nativeMetadata.StringAt(start + checked((int)nativeMetadata.Value(materials[i], 8)) * 4) == name);
            }
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = "Origin", ["waist"] = "Waist", ["head"] = "Head", ["neck"] = "Neck", ["spine"] = "Spine",
                ["lower_jaw"] = "Jaw", ["jaw"] = "Jaw", ["left_hand"] = "LHand", ["right_hand"] = "RHand", ["left_foot"] = "LFoot", ["right_foot"] = "RFoot",
            };
            // Some donors retain both a jaw pivot and the deforming lower jaw.
            // Preserve both nodes while keeping the native Jaw identity unique.
            if (scene.Rig.Bones.Any(b => b.Name == "jaw") && scene.Rig.Bones.Any(b => b.Name == "lower_jaw"))
                aliases["lower_jaw"] = "LowerJaw";
            foreach (var bone in scene.Rig.Bones) aliases.TryAdd(bone.Name, bone.Name.Replace(sourceStem, stem, StringComparison.Ordinal));
            var locators = profile.AuthoredMaterials
                ? SourceLocators(scene, aliases, Donor(sourceFolder + $"locators/{sourceStem}_{profile.AnimationOffset:00000}_eff.trskl"), profile.MouthPosition)
                : Locators(scene, aliases);
            var miniorAuthoredColors = new Dictionary<string, IReadOnlySet<(string Material, string Parameter)>>(StringComparer.Ordinal);
            var members = new Dictionary<string, byte[]>(StringComparer.Ordinal); var modelFolder = $"bin/pokemon/{stem}/mdl/"; var animationFolder = $"bin/pokemon/{stem}/anm/";
            var textures = new RestorationTexturePool(members, modelFolder, profile.CompactResources);
            var normalTileQualifications = new Dictionary<string, SwShNormalTileQualification>(StringComparer.Ordinal);
            var colorUvTransforms = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            var fireTextures = profile.NativeFlames ? SvSwShFlameMaterial.Textures(materialBytes,
                name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding) : null;
            if (fireTextures is not null)
            {
                foreach (var pair in fireTextures.Files) members[modelFolder + pair.Key + ".bntx"] = pair.Value;
                members[modelFolder + "viewport_mask_col.bntx"] = Member(firePack!, "viewport_mask_col.bntx");
            }
            foreach (var name in new[] { "Default_lta", "dummy_col", "projection_effect_col", "pm0007_00_Eye_amb" })
                members[modelFolder + name + ".bntx"] = Member(nativePack, name + ".bntx");
            if (transparentPack is not null)
                members[modelFolder + SvSwShTransparentMaterial.EnvironmentName + ".bntx"] = Member(transparentPack, SvSwShTransparentMaterial.EnvironmentName + ".bntx");
            if (profile.CornealEyes)
                members[modelFolder + SvSwShCornealMaterial.EnvironmentName + ".bntx"] = Member(
                    SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0330_00.gfpak")), SvSwShCornealMaterial.EnvironmentName + ".bntx");
            var shaderNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in new[] { 3, 5 })
            {
                var (start, count) = nativeMetadata.Vector(nativeMetadata.Root, field, 4, 256);
                for (var i = 0; i < count; i++) shaderNames.Add(nativeMetadata.StringAt(start + i * 4));
            }
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                members[modelFolder + name + extension] = profile.SurfaceFlow && name is SvSwShSurfaceFlow.ProgramName or SvSwShSurfaceFlow.LayerProgramName
                    ? FlowShader(nativePack, extension, name == SvSwShSurfaceFlow.LayerProgramName) : profile.LayerEmission && (name is SvSwShLayerEmission.ProgramName or SvSwShLayerEmission.MapProgramName or SvSwShLayerEmission.LavaProgramName or SvSwShLayerEmission.BaseProgramName or SvSwShLayerEmission.MirroredProgramName or SvSwShLayerEmission.UniformProgramName or SvSwShLayerEmission.OpaqueCoverageProgramName or SvSwShSourceCoverage.OpaqueProgramName or SvSwShSourceBase.ProgramName or SvSwShSourceBase.MirroredProgramName or SvSwShSourceBase.UnlitProgramName || TransparentEmission(name))
                    ? EmissionShader(TransparentEmission(name) ? transparentPack! : nativePack, extension, name) : profile.LayeredEyes && name == (profile.LayeredEyeEmission ? ZaSwShEyelidMaterial.EmissionProgramName : ZaSwShEyelidMaterial.ProgramName)
                    ? extension == ".bnsh_fsh" ? ZaSwShEyeShader.Convert(Member(eyePack!, ZaSwShEyelidMaterial.NativeProgramName + extension), profile.CornealEyes, profile.LavaLayers, layerEmission: profile.LayeredEyeEmission)
                        : ZaSwShEyeShader.Vertex(Member(eyePack!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                    : profile.CornealEyes && name == SvSwShCornealMaterial.ProgramName
                    ? Member(cornealPack!, SvSwShCornealMaterial.NativeProgramName + extension)
                    : transparent && Array.IndexOf(SvSwShTransparentMaterial.Programs, name) is var pass && pass >= 0
                    ? Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[pass] + extension)
                    : nativePack.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(firePack ?? nativePack, name + extension);
            SwShConvertedGeometry? geometry = null;
            foreach (var rare in new[] { false, true })
            {
                var paletteBytes = Materials(rare);
                if (transparent) SvSwShTransparentMaterial.RequireSource(paletteBytes, scene, profile.TransparentBody);
                var sourceMaterial = new ModelBuffer(paletteBytes);
                var paletteEyes = profile.LayeredEyes ? ZaSwShEyelidMaterial.Materials(paletteBytes, scene, layerEmission: profile.LayeredEyeEmission,
                    cornealPrimaryNormalHeights: profile.CornealPrimaryNormalHeights) : null;
                var paletteEmission = profile.LayerEmission ? SvSwShLayerEmission.Contracts(paletteBytes, scene) : null;
                var paletteLava = profile.LavaLayers ? SvSwShLavaMaterial.Contracts(paletteBytes, scene) : null;
                var palette = BakePalette(scene, profile.LavaLayers ? SvSwShLavaMaterial.BakingMaterials(paletteBytes) : paletteBytes, rare, sourceFolder + sourceStem + ".trmtr", textures, profile.NativeFlames,
                    profile.SourceLayeredAlbedo ? paletteEmission?.Where(p => p.Value.BaseColors is not null).Select(p => p.Key).ToHashSet(StringComparer.Ordinal) : null);
                var flameTemplate = FlameTemplate(paletteBytes);
                var paletteModel = profile.NativeFlames ? flameTemplate.Model : nativeModel;
                if (profile.NativeFlames && cornealPack is not null)
                    paletteModel = SvSwShCornealMaterial.Template(paletteModel, Member(cornealPack, "pm0060_00.gfbmdl"));
                SwShMaterialBinding FireBinding(string name, bool mask) => new(mask ? name + "_mask" : name, mask ? flameTemplate.Parts[name].Mask : flameTemplate.Parts[name].Core,
                    mask ? new Dictionary<string, string> { ["Mask0Texture"] = fireTextures!.White, ["Mask1Texture"] = fireTextures.White }
                    : new Dictionary<string, string> { ["LerpTex"] = fireTextures!.Control, ["Blend0Tex"] = fireTextures.Layer,
                        ["Blend1Tex"] = fireTextures.Noise, ["MaskTex"] = "viewport_mask_col" });
                string PrimaryEyeNormal(PreviewPrimitive primitive, int index)
                {
                    var cornealSource = new ModelBuffer(paletteBytes);
                    var heights = SvSwShCornealMaterial.SourceNormalHeights(cornealSource,
                        cornealSource.Tables(cornealSource.Root, 1, 128).Single(m => cornealSource.Text(m, 0) == primitive.Material.Name));
                    var primary = SwShMaterialBaker.Bake(primitive, paletteBytes, index,
                        name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)),
                        token, sourceNormalDimensions: profile.CompactResources, encodingCache: textureEncoding,
                        staticUvAtlas: StaticAtlas(primitive),
                        cornealNormalHeight: profile.CornealNormalHeights?.GetValueOrDefault(primitive.Material.Name, heights.Corneal) ?? heights.Corneal,
                        cornealPrimaryNormalHeight: profile.CornealPrimaryNormalHeights?.GetValueOrDefault(primitive.Material.Name, heights.Primary) ?? heights.Primary,
                        cornealPrimarySurface: true);
                    var name = ModelTextureIdentity.MaterialName(primary.NormalName, index, rare ? 'q' : 'p');
                    return textures.Add(name, ModelTextureIdentity.Rename(primary.Normal, primary.NormalName, name));
                }
                var bindings = scene.Primitives.Select((primitive, index) =>
                {
                    if (profile.NativeFlames && primitive.Material.Name == "fire") return FireBinding(targetScene.Primitives[index].Material.Name, false);
                    if (paletteEyes?.TryGetValue(primitive.Material.Name, out var eyeContract) == true)
                    {
                        var images = ZaSwShEyelidMaterial.Textures(eyeContract, index, rare,
                            name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                        var allocated = images.Files.ToDictionary(pair => pair.Key, pair => textures.Add(pair.Key, pair.Value), StringComparer.Ordinal);
                        var eyeBindings = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[1], 11, 64)
                            .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                        foreach (var pair in images.Bindings) eyeBindings[pair.Key] = allocated[pair.Value];
                        eyeBindings["LightTblTex"] = "Default_lta";
                        if (!images.Bindings.ContainsKey("EffectTex")) eyeBindings["EffectTex"] = "projection_effect_col";
                        if (profile.CornealEyes)
                        {
                            if (eyeContract.Vectors.GetValueOrDefault("NormalUVScaleOffset", new(1, 1, 0, 0)) != new Vector4(1, 1, 0, 0))
                                throw new InvalidDataException("Layered corneal normal coordinates need another adapter.");
                            // Diffuse shading retains the source primary normal. The corneal
                            // normal is carried independently by the eye geometry below.
                            eyeBindings["NormalMapTex"] = PrimaryEyeNormal(primitive, index);
                            eyeBindings["SphereMapTex"] = SvSwShCornealMaterial.EnvironmentName;
                        }
                        return new SwShMaterialBinding(targetScene.Primitives[index].Material.Name, 1,
                            eyeBindings);
                    }
                    var material = palette[index] ?? throw new InvalidDataException("Converted material is missing.");
                    var original = sourceMaterial.Tables(sourceMaterial.Root, 1, 128).Single(t => sourceMaterial.Text(t, 0) == primitive.Material.Name);
                    var textureNames = sourceMaterial.Tables(original, 2, 64).ToDictionary(t => sourceMaterial.Text(t, 0)!, t => Path.GetFileNameWithoutExtension(sourceMaterial.Text(t, 1)!));
                    var eye = eyeMaterials.Contains(primitive.Material.Name); var nativeMaterial = eye ? 1 : 0;
                    if (originTransparent && innerSections.Contains(index))
                        nativeMaterial = MaterialProgram(SvSwShTransparentMaterial.Programs[0]);
                    if (profile.TransparentEyes && primitive.Material.Name is "l_eye_b" or "l_highlight")
                        nativeMaterial = nativeMetadata.Tables(nativeMetadata.Root, 6, 128).Length - 2;
                    if (paletteEmission?.TryGetValue(primitive.Material.Name, out var emissionSurface) == true)
                        nativeMaterial = MaterialProgram(emissionSurface.Program());
                    if (flowContracts?.ContainsKey(primitive.Material.Name) == true)
                        nativeMaterial = MaterialProgram(SvSwShSurfaceFlow.Program(flowContracts));
                    var textureBindings = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[nativeMaterial], 11, 64)
                        .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                    textureBindings["Col0Tex"] = material.ColorName;
                    textureBindings["NormalMapTex"] = material.NormalName; textureBindings["AmbientTex"] = material.AmbientName;
                    if (profile.CornealEyes && eye) textureBindings["NormalMapTex"] = PrimaryEyeNormal(primitive, index);
                    textureBindings["LightTblTex"] = "Default_lta"; textureBindings["EffectTex"] = "projection_effect_col";
                    if (originTransparent && innerSections.Contains(index)) textureBindings["SphereMapTex"] = SvSwShTransparentMaterial.EnvironmentName;
                    if (paletteLava?.TryGetValue(primitive.Material.Name, out var lava) == true && lava.Detail is { } detail)
                        textureBindings["LyCol0Tex"] = textures.AddAuxiliary(Path.GetFileNameWithoutExtension(detail),
                            Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", detail)));
                    if (flowContracts?.TryGetValue(primitive.Material.Name, out var flow) == true && flow.Noise is { } noise)
                    {
                        textureBindings["LyCol0Tex"] = textures.AddAuxiliary(Path.GetFileNameWithoutExtension(noise),
                            Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", noise)));
                        if (flow.MaskScroll)
                        {
                            var baseMap = sourceMaterial.Tables(original, 2, 128).Single(t => sourceMaterial.Text(t, 0) == "BaseColorMap");
                            var image = sourceMaterial.Text(baseMap, 1)!;
                            textureBindings["Col0Tex"] = textures.AddAuxiliary(Path.GetFileNameWithoutExtension(image),
                                Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", image)));
                        }
                    }
                    if (paletteEmission?.TryGetValue(primitive.Material.Name, out var emission) == true)
                    {
                        if (emission.BaseColors is not null && !profile.SourceLayeredAlbedo)
                        {
                            var baseMap = sourceMaterial.Tables(original, 2, 128).Single(t => sourceMaterial.Text(t, 0) == "BaseColorMap");
                            var image = sourceMaterial.Text(baseMap, 1)!;
                            textureBindings["Col0Tex"] = textures.AddAuxiliary(Path.GetFileNameWithoutExtension(image),
                                Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", image)));
                        }
                        var mask = SvSwShLayerEmission.Mask(emission, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                        if (StaticAtlas(primitive) is { } atlas)
                        {
                            var image = new ModelTextureDocument(mask.Bytes);
                            var cropped = atlas.Images(Enumerable.Range(0, image.MipCount).Select(image.Pixels).ToArray(), image.Width, image.Height);
                            var allocated = SwShTextureAllocation.Create(mask.Bytes, cropped.Width, cropped.Height, image.Srgb, compressed: false);
                            var name = ModelTextureIdentity.MaterialName(mask.Name, index, 'e');
                            var container = new ModelTextureDocument(ModelTextureIdentity.Rename(allocated, mask.Name, name));
                            mask = (name, container.EncodeMipImages(cropped.Images, token, textureEncoding));
                        }
                        textureBindings["EmissionMaskTex"] = emission.BaseColorMap || emission.MaskUnused ? textureBindings["Col0Tex"] : textures.Add(mask.Name, mask.Bytes);
                        if (emission.Transparent) textureBindings["SphereMapTex"] = SvSwShTransparentMaterial.EnvironmentName;
                    }
                    if (profile.CornealEyes && eye) textureBindings["SphereMapTex"] = SvSwShCornealMaterial.EnvironmentName;
                    if (profile.TransparentEyes && primitive.Material.Name is "l_eye_b" or "l_highlight")
                        textureBindings["SphereMapTex"] = SvSwShTransparentMaterial.EnvironmentName;
                    if (material.NormalTileQualification is { } qualification)
                        normalTileQualifications[targetScene.Primitives[index].Material.Name] = qualification;
                    if (material.ColorUvTransform != new Vector4(1, 1, 0, 0))
                    {
                        var name = targetScene.Primitives[index].Material.Name;
                        if (colorUvTransforms.TryGetValue(name, out var previous) && previous != material.ColorUvTransform)
                            throw new InvalidDataException("Palette color atlas coordinates disagree.");
                        colorUvTransforms[name] = material.ColorUvTransform;
                    }
                    return new SwShMaterialBinding(targetScene.Primitives[index].Material.Name, nativeMaterial, textureBindings)
                        { NormalUvScale = material.NormalUvScale,
                            ColorUvTransform = material.ColorUvTransform,
                            UseSecondaryUv = flowContracts?.GetValueOrDefault(primitive.Material.Name)?.MaskScroll == true };
                }).ToArray();
                if (transparent) bindings = bindings.Concat(innerSections.Select(i => bindings[i] with
                {
                    Name = bindings[i].Name + "_inner", NativeMaterial = MaterialProgram(profile.TransparentBody || originTransparent
                        ? paletteEmission![scene.Primitives[i].Material.Name].PreservesTextureAlpha
                            ? paletteEmission[scene.Primitives[i].Material.Name].Program(inner: true) : SvSwShLayerEmission.TransparentPrograms[1]
                        : SvSwShTransparentMaterial.Programs[1]),
                    // The solid inner wall must not inherit the dark transparent surface color.
                    Textures = profile.TransparentEyes ? new Dictionary<string, string>(bindings[i].Textures, StringComparer.Ordinal) { ["Col0Tex"] = "dummy_col" } : bindings[i].Textures,
                })).ToArray();
                if (profile.NativeFlames) bindings = bindings.Concat(flames.Select(name => FireBinding(name, true))).ToArray();
                Report("geometry", rare ? 1 : 0, 2);
                var paletteScene = profile.CornealEyes ? targetScene with { Primitives = targetScene.Primitives.Select((primitive, index) =>
                    index < scene.Primitives.Count && eyeMaterials.Contains(scene.Primitives[index].Material.Name)
                        ? SvSwShCornealMaterial.Normals(primitive, palette[index]!.Normal, paletteBytes,
                            scene.Primitives[index].Material.Name) : primitive).ToArray() } : targetScene;
                var converted = SwShModelWriter.Convert(paletteScene, paletteModel, bindings, aliases, locators,
                    cancellationToken: token, preserveMeshNames: profile.NativeFlames || profile.MeshVisibility);
                converted = converted with { Model = SwShRestorationMaterialResponse.Apply(converted.Model) };
                if (paletteEyes is not null) converted = converted with { Model = ZaSwShEyelidMaterial.Bind(converted.Model, paletteEyes, originalNames) };
                if (flowContracts is not null) converted = converted with { Model = SvSwShSurfaceFlow.Bind(converted.Model,
                    SvSwShSurfaceFlow.Contracts(Materials(rare), scene, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name))), originalNames) };
                if (paletteEmission is not null) converted = converted with { Model = SvSwShLayerEmission.Bind(converted.Model, paletteEmission, originalNames) };
                if (paletteLava is not null) converted = converted with { Model = SvSwShLavaMaterial.Bind(converted.Model, paletteLava, originalNames) };
                if (profile.CompactResources) converted = converted with { Model = CompactPrograms(converted.Model) };
                if (profile.CompactTextureReferences) converted = converted with { Model = CompactTextures(converted.Model) };
                geometry ??= converted; members[modelFolder + stem + (rare ? "_rare" : "") + ".gfbmdl"] = converted.Model;
            }
            var sizeStem = profile.ResourceStem ?? SwShRestorationNativeTemplate.Stem(profile.NativeTemplate, Native);
            var sizePack = SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{sizeStem}.gfpak"));
            var materialNames = targetScene.Primitives.Select(p => p.Material.Name).ToArray();
            var frameStem = profile.FramingTemplate == 0 ? sizeStem : SwShRestorationNativeTemplate.Stem(profile.FramingTemplate, Native);
            var framePack = profile.FramingTemplate == 0 ? sizePack : SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{frameStem}.gfpak"));
            var clips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            // Slakoth's source graph has a stale Hariyama battle-idle reference.
            // Its skeleton is incompatible; use Slakoth's authored default idle instead.
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)
                && !(species == 287 && Path.GetFileName(r.Id) == "pm0297_00_00_00001_battlewait01_loop.tranm")).ToArray();
            if (profile.StandaloneAnimations?.Any(name => !motions.Any(r => Path.GetFileNameWithoutExtension(r.Id) == name)) == true)
                throw new InvalidDataException("A declared standalone source pose is unavailable.");
            var meshNames = targetScene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            byte[] ConvertMotion(byte[] skeleton, PreviewClip decoded, bool complete, SwShExpressionCurve expression = SwShExpressionCurve.None)
            {
                var tracks = decoded.Materials.Where(t => (!profile.NativeFlames || !flames.Contains(t.Material, StringComparer.Ordinal))
                    && (!profile.LayerEmission || !(t.Parameter.StartsWith("EmissionIntensityLayer", StringComparison.Ordinal)
                        || t.Parameter.StartsWith("EmissionColorLayer", StringComparison.Ordinal) || t.Parameter == SvSwShLayerEmission.CoverageTrack))
                    && !(baseEmissionNames.Contains(t.Material) && t.Parameter.StartsWith("BaseColorLayer", StringComparison.Ordinal))
                    && (!profile.SurfaceFlow || t.Parameter != "UVScaleOffset3")).ToArray();
                var emptyExpression = profile.EmptyMouth && expression == SwShExpressionCurve.Mouth
                    || profile.EmptyEyes && expression == SwShExpressionCurve.Eyes;
                var converted = profile.ScalarEyes || profile.LayeredEyes || emptyExpression
                    ? ZaSwShAnimationWriter.Convert(skeleton, tracks, scene.Rig.Bones, aliases, complete, expression, emptyExpression: emptyExpression)
                    : SwShAnimationWriter.Convert(skeleton, tracks, scene.Rig.Bones, aliases, complete, expression);
                if (expression != SwShExpressionCurve.None) return converted;
                if (profile.LayerEmission) converted = SvSwShLayerEmission.Animation(converted, decoded.Materials, baseEmissionNames, baseAlphas, separateBaseNames);
                if (profile.SurfaceFlow) converted = SvSwShSurfaceFlow.Animation(converted, decoded.Materials);
                if (!profile.NativeFlames)
                {
                    var transparentVisibility = transparent ? decoded.Visibility.SelectMany(v =>
                        innerSections.Any(i => scene.Primitives[i].Name == v.Name) ? new[] { v, v with { Name = v.Name + "_inner" } } : [v]).ToArray() : decoded.Visibility;
                    return profile.MeshVisibility ? SwShAnimationWriter.Visibility(converted, transparentVisibility, meshNames) : converted;
                }
                foreach (var name in flames) converted = SvSwShFlameAnimation.Apply(converted, decoded.Materials, materialName: name);
                var fireMeshes = scene.Primitives.Where(p => p.Material.Name == "fire").Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var visibility = decoded.Visibility.SelectMany(v => fireMeshes.Contains(v.Name) ? new[] { v, v with { Name = v.Name + "_mask" } } : [v]).ToArray();
                return SwShAnimationWriter.Visibility(converted, visibility, meshNames);
            }
            Report("animations", 0, motions.Length + NativeClips.Count);
            foreach (var resource in motions)
            {
                token.ThrowIfCancellationRequested();
                Report("animations", clips.Count, motions.Length + NativeClips.Count, resource.Id);
                detailHierarchy?.RequireUnanimated(resource.Bytes);
                var clip = PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones);
                var warnings = new List<string>();
                var materialAnimation = Donor(Path.ChangeExtension(resource.Id, ".tracm").Replace('\\', '/'));
                if (species == 774)
                    miniorAuthoredColors[animationFolder + Path.GetFileNameWithoutExtension(resource.Id).Replace(sourceStem, stem, StringComparison.Ordinal) + ".gfbanm"] =
                        SvSwShMiniorPalette.AuthoredColors(materialAnimation, profile.StandaloneAnimations?.Contains(Path.GetFileNameWithoutExtension(resource.Id)) == true);
                SvSwShStaticSourcePalette.RequireAnimation(materialAnimation, species, pokemon.Form);
                clip = profile.LavaLayers ? SvSwShLavaMaterial.Read(materialAnimation, clip, scene, lavaContracts!, eyeContracts!, profile.MeshVisibility)
                    : (profile.ScalarEyes || profile.LayeredEyes) && !profile.LayerEmission ? ZaSwShAnimationWriter.ReadClip(materialAnimation, clip, profile.MeshVisibility, eyes: eyeContracts)
                    : profile.NativeFlames ? SvSwShFlameAnimation.Read(materialAnimation, clip, profile.FlameMaterial, mergeIdenticalTracks: flames.Length > 1) : profile.LayerEmission ? SvSwShLayerEmission.Read(materialAnimation, clip, scene, emissionContracts!,
                        decode: profile.ScalarEyes || profile.LayeredEyes
                            ? bytes => ZaSwShAnimationWriter.ReadClip(bytes, clip, profile.MeshVisibility, eyes: eyeContracts) : null)
                    : profile.SurfaceFlow ? SvSwShSurfaceFlow.Read(materialAnimation, clip, flowContracts!)
                    : PreviewMaterialAnimation.Read(new(materialAnimation), clip, warnings, true);
                if (warnings.Count > 0 || (!profile.NativeFlames && !profile.MeshVisibility && clip.Visibility.Any(v => v.Keys.Any(k => k.Value.Any(x => x == 0)))))
                    throw new InvalidDataException("Source animation needs an unsupported material or visibility adapter.");
                foreach (var track in clip.Visibility.Where(track => !meshNames.Contains(track.Name)))
                {
                    if (!declaredMeshes.Contains(track.Name) || track.Keys.Length == 0
                        || track.Keys.Any(key => key.Value.Length != 1 || key.Value[0] is not (0 or 1) || key.Value[0] != track.Keys[0].Value[0]))
                        throw new InvalidDataException("Source visibility refers to an undeclared or changing sibling model section.");
                }
                clip = clip with { Visibility = clip.Visibility.Where(track => meshNames.Contains(track.Name)).ToArray() };
                if (clip.Materials.Any(t => !scene.Primitives.Any(p => p.Material.Name == t.Material)))
                    throw new InvalidDataException("Source animation has an undeclared material target.");
                RequireStaticAtlasMaterials(scene, clip);
                if (flameAtlas && clip.Materials.Any(t => t.Material == "fire"))
                    throw new InvalidDataException("An animated flame atlas requires another UV adapter.");
                clip = clip with { Materials = clip.Materials.SelectMany(track => scene.Primitives.Select((p, i) => (p, i))
                    .Where(pair => pair.p.Material.Name == track.Material)
                    .SelectMany(pair => innerSections.Contains(pair.i)
                        ? new[] { track with { Material = targetScene.Primitives[pair.i].Material.Name }, track with { Material = targetScene.Primitives[pair.i].Material.Name + "_inner" } }
                        : [track with { Material = targetScene.Primitives[pair.i].Material.Name }])).ToArray() };
                Report("animations", clips.Count, motions.Length + NativeClips.Count);
                var name = Path.GetFileNameWithoutExtension(resource.Id);
                if (profile.StandaloneAnimations?.Contains(name) == true)
                {
                    if (clip.Frames != 3 || clip.Rate != 60)
                        throw new InvalidDataException("Standalone source pose timeline requires another adapter.");
                    members[animationFolder + name.Replace(sourceStem, stem, StringComparison.Ordinal) + ".gfbanm"] = ConvertMotion(resource.Bytes, clip, false);
                    continue;
                }
                var sourceCode = name[sourceStem.Length..].TrimStart('_').Split('_')[0];
                var number = int.Parse(sourceCode, System.Globalization.CultureInfo.InvariantCulture) - profile.AnimationOffset;
                if (number is < 0 or > 99999) throw new InvalidDataException("Source animation code is outside the declared profile.");
                var code = number.ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
                if (!clips.TryAdd(code, (resource.Bytes, clip))) throw new InvalidDataException("Source animation code is ambiguous.");
                members[animationFolder + name.Replace(sourceStem, stem, StringComparison.Ordinal) + ".gfbanm"] = ConvertMotion(resource.Bytes, clip, false);
            }
            var nativeClipsDone = 0;
            foreach (var (name, code) in NativeClips)
            {
                var sourceCode = name == "ba10_waitA01" ? profile.BattleIdle : profile.MotionOverrides?.GetValueOrDefault(name, code) ?? code;
                if (!clips.TryGetValue(sourceCode, out var clip)) throw new InvalidDataException($"Required animation is unavailable: {sourceCode}");
                Report("animations", motions.Length + nativeClipsDone++, motions.Length + NativeClips.Count, $"{stem}_{name}.gfbanm");
                var expression = name == "eye01" ? SwShExpressionCurve.Eyes : name == "mouth01" ? SwShExpressionCurve.Mouth : SwShExpressionCurve.None;
                members[animationFolder + stem + "_" + name + ".gfbanm"] = ConvertMotion(clip.Skeleton, clip.Decoded, expression == SwShExpressionCurve.None, expression);
            }
            Report("packing");
            var battleEntry = SwShRestorationFraming.BattleEntry(members, modelFolder, animationFolder, stem);
            members[animationFolder + stem + "_battleidle01.gfbanm"] = SwShAnimationWriter.BattleIdle(members[animationFolder + stem + "_ba10_waitA01.gfbanm"]);
            var globalLoop = SwShAnimationWriter.GlobalMaterialLoop(Member(nativePack, "pm0007_00_gloop01.gfbanm"),
                materialNames.Where(n => !flames.Any(flame => n == flame || n == flame + "_mask")).ToArray(),
                scene.Primitives.Select((primitive, index) => (Name: targetScene.Primitives[index].Material.Name,
                    Scale: profile.NormalUvScales?.GetValueOrDefault(primitive.Name, Vector2.One) ?? Vector2.One))
                    .Where(entry => entry.Scale != Vector2.One).ToDictionary(entry => entry.Name, entry => entry.Scale),
                colorUvTransforms.Concat(originalNames.Where(p => emissionContracts?.GetValueOrDefault(p.Value)?.PreservesTextureAlpha == true)
                    .ToDictionary(p => p.Key, p => emissionContracts![p.Value].ColorUvOrigin is { } origin
                        ? new Vector4(1, 1, -origin.X, origin.Y)
                        : throw new InvalidDataException("Original source alpha UV origin is missing.")))
                    .ToDictionary(p => p.Key, p => p.Value), normalTileQualifications);
            if (profile.LayerEmission) globalLoop = SvSwShLayerEmission.GlobalLoop(globalLoop, emissionNames, baseEmissionNames,
                coverageNames: coverageNames, separateBaseNames: separateBaseNames);
            if (profile.CornealEyes) globalLoop = SvSwShCornealMaterial.GlobalLoop(globalLoop,
                originalNames.Where(pair => eyeMaterials.Contains(pair.Value)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
            if (eyeContracts is not null) globalLoop = ZaSwShEyelidMaterial.KeepAnimationLocal(globalLoop,
                materialNames.Where(name => name.Contains("eye", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal), layerEmission: profile.LayeredEyeEmission);
            if (lavaNames is not null)
            {
                globalLoop = SvSwShLavaMaterial.GlobalLoop(globalLoop, lavaNames);
                if (clips.TryGetValue("08201", out var lavaLoop)) globalLoop = SvSwShLavaMaterial.Loop(globalLoop, lavaLoop.Decoded, lavaNames);
                globalLoop = SwShAnimationWriter.CompactMaterialLoop(globalLoop);
            }
            if (profile.SurfaceFlow)
            {
                globalLoop = SvSwShSurfaceFlow.GlobalLoop(globalLoop, flowNames);
                if (clips.TryGetValue("08201", out var flowLoop)) globalLoop = SvSwShSurfaceFlow.Animation(globalLoop,
                    flowLoop.Decoded.Materials.Where(t => flowNames.Contains(t.Material)).ToArray(), flowLoop.Decoded, includeColor: true);
            }
            if (transparent) globalLoop = SvSwShTransparentMaterial.GlobalLoop(globalLoop,
                targetScene.Primitives.Where(p => p.Material.Alpha.Contains("Blend", StringComparison.Ordinal))
                    .Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal));
            // Deoxys has many separate materials. Remove unreachable copied donor
            // tables so its material loop fits the game's model allocation pool.
            if (species == 386) globalLoop = SwShAnimationWriter.CompactMaterialLoop(globalLoop);
            foreach (var name in flames) globalLoop = SvSwShFlameAnimation.Apply(globalLoop, clips["08201"].Decoded.Materials, clips["08201"].Decoded, name);
            var flameBones = profile.NativeFlames ? SvSwShFlameAnimation.LoopBones(scene, clips["08201"].Decoded) : [];
            if (flameBones.Length > 0)
                globalLoop = SvSwShFlameAnimation.SkeletonLoop(globalLoop,
                    SwShAnimationWriter.Convert(clips["08201"].Skeleton, [], scene.Rig.Bones, aliases, false),
                    flameBones.Select(b => aliases[b]).ToArray());
            members[animationFolder + stem + "_gloop01.gfbanm"] = globalLoop;
            var facialBones = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var name in new[] { "eye01", "mouth01" })
            {
                if (name == "mouth01" && profile.EmptyMouth || name == "eye01" && profile.EmptyEyes) continue;
                var bones = clips[profile.MotionOverrides?.GetValueOrDefault(name, NativeClips[name]) ?? NativeClips[name]].Decoded.Tracks.Select(t => scene.Rig.Bones[t.Bone].Name)
                    .Where(b => b.Contains(name == "eye01" ? "eyelid" : "jaw", StringComparison.OrdinalIgnoreCase)).Select(b => aliases[b]).ToArray();
                if (bones.Length > 0) facialBones.Add(name, bones);
            }
            foreach (var group in new[] { "battle", "camp", "field" })
            {
                var controller = profile.ScalarEyes || profile.LayeredEyes
                    ? ZaSwShAnimationWriter.Controller(Member(nativePack, "pm0007_00_" + group + ".gfbanmcfg"), "pm0007_00", stem, materialNames, facialBones, profile.LayeredEyes, layerEmission: profile.LayeredEyeEmission)
                    : SwShAnimationWriter.Controller(Member(nativePack, "pm0007_00_" + group + ".gfbanmcfg"), "pm0007_00", stem, materialNames, facialBones,
                        compactMaterials: profile.CompactAnimationResources);
                if (profile.LayerEmission) controller = SvSwShLayerEmission.Controller(controller, emissionNames,
                    originalNames.Where(pair => emissionContracts?.GetValueOrDefault(pair.Value)?.AnimatedColors == true)
                        .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal), baseEmissionNames,
                    coverageNames: coverageNames, separateBaseNames: separateBaseNames);
                if (lavaNames is not null) controller = SvSwShLavaMaterial.Controller(controller, lavaNames);
                if (profile.SurfaceFlow) controller = SvSwShSurfaceFlow.Controller(controller, flowNames);
                if (group == "battle") controller = SwShAnimationWriter.BattleIdleController(controller);
                if (group == "battle" && battleEntry) controller = SwShAnimationWriter.BattleEntryController(controller);
                members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = profile.NativeFlames
                    ? SvSwShFlameAnimation.Controller(controller, flameBones.Select(b => aliases[b]).ToArray(), flames) : controller;
            }
            if (profile.CompactAnimationResources)
            {
                // Keep every controller dependency. Numeric source conversions are
                // intermediates once their native playback aliases have been written.
                var bound = members.Where(p => p.Key.EndsWith(".gfbanmcfg", StringComparison.Ordinal)).SelectMany(pair =>
                {
                    var controller = new ModelBuffer(pair.Value);
                    return controller.Tables(controller.Table(controller.Root, 6), 0, 2048)
                        .Select(binding => TrinityPreviewReader.Resolve(pair.Key, controller.Text(binding, 1)
                            ?? throw new InvalidDataException("Converted controller dependency is missing.")));
                }).ToHashSet(StringComparer.Ordinal);
                if (bound.Any(path => !members.ContainsKey(path))) throw new InvalidDataException("Converted controller dependency is unavailable.");
                foreach (var path in members.Keys.Where(path => path.EndsWith(".gfbanm", StringComparison.Ordinal) && !bound.Contains(path)).ToArray())
                    members.Remove(path);
                var sharedAnimations = SwShAnimationResourceAliases.Compact(members);
                members.Clear();
                foreach (var pair in sharedAnimations) members.Add(pair.Key, pair.Value);
            }
            var framingGeometry = profile.AuthoredMaterials ? SwShRestorationFraming.IdleBounds(members, modelFolder, animationFolder, stem, geometry!) : geometry!;
            members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"] = SwShModelWriter.Configuration(Member(framePack, frameStem + ".gfbpokecfg"), species, framingGeometry, materialNames,
                profile.BattleHeight, profile.FieldHeight);
            if (profile.SummaryFramingStem is { } summaryStem)
            {
                var summaryPack = SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{summaryStem}.gfpak"));
                var path = $"bin/pokemon/{stem}/{stem}.gfbpokecfg";
                members[path] = SwShRestorationFraming.SummaryFields(members[path], Member(summaryPack, summaryStem + ".gfbpokecfg"));
            }
            var summaryPath = $"bin/pokemon/{stem}/{stem}.gfbpokecfg";
            members[summaryPath] = SwShRestorationFraming.SummaryCorrection(members[summaryPath], definitionId);
            var result = new List<SwShRestorationContribution>();
            foreach (var rare in profile.CompactResources ? new[] { false, true } : new[] { false })
            {
                var suffix = rare ? "_rare" : "";
                var selected = profile.CompactResources ? PaletteMembers(members, modelFolder + stem + suffix + ".gfbmdl") : members;
                if (species == 774 || SvSwShStaticSourcePalette.Supports(species, pokemon.Form))
                {
                    foreach (var path in selected.Keys.Where(path => path.EndsWith(".gfbanm", StringComparison.Ordinal)).ToArray())
                        selected[path] = species == 774
                            ? SvSwShMiniorPalette.Animation(selected[path], members[modelFolder + stem + ".gfbmdl"],
                                selected[modelFolder + stem + suffix + ".gfbmdl"], originalNames, miniorAuthoredColors.GetValueOrDefault(path))
                            : SvSwShStaticSourcePalette.Animation(selected[path], members[modelFolder + stem + ".gfbmdl"],
                                selected[modelFolder + stem + suffix + ".gfbmdl"], originalNames, species, pokemon.Form);
                    SwShRestorationResourceBudget.Validate(selected);
                }
                var archivePath = $"bin/archive/pokemon/{stem}{suffix}.gfpak";
                Report("packing", resource: archivePath);
                result.Add(File(archivePath, SwShGfPackFile.CreateWithPaths(selected).Write()));
            }
            if (profile.CompactResources)
                shaderNames.IntersectWith(new[] { 3, 5 }.SelectMany(f => ModelNames(new(members[modelFolder + stem + ".gfbmdl"]), f)));
            var giantDonor = SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0007_00.gfpak"));
            var giantFire = profile.NativeFlames ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0004_00.gfpak")) : null;
            var giantCorneal = profile.CornealEyes ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0060_00.gfpak")) : null;
            var giantEye = profile.LayeredEyes ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0001_00.gfpak")) : null;
            var giantTransparent = transparent ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0577_00.gfpak")) : null;
            var giantSize = SwShGfPackFile.Parse(Native($"bin/archive/gpokemon/{sizeStem}.gfpak"));
            var giantConfiguration = Member(giantSize, sizeStem + ".gfbgpokecfg");
            if (profile.GiantScale is { } giantScale) SwShRestorationFraming.SetGiantScale(giantConfiguration, giantScale);
            if (profile.GiantVerticalOffset is { } giantVerticalOffset) SwShRestorationFraming.SetGiantVerticalOffset(giantConfiguration, giantVerticalOffset);
            var giantMembers = new Dictionary<string, byte[]> { [$"bin/pokemon/{stem}/{stem}.gfbgpokecfg"] = giantConfiguration };
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                giantMembers[$"bin/pokemon/{stem}/g_shader/{name}{extension}"] = profile.SurfaceFlow && name is SvSwShSurfaceFlow.ProgramName or SvSwShSurfaceFlow.LayerProgramName
                    ? FlowShader(giantDonor, extension, name == SvSwShSurfaceFlow.LayerProgramName) : profile.LayerEmission && (name is SvSwShLayerEmission.ProgramName or SvSwShLayerEmission.MapProgramName or SvSwShLayerEmission.LavaProgramName or SvSwShLayerEmission.BaseProgramName or SvSwShLayerEmission.UniformProgramName or SvSwShLayerEmission.OpaqueCoverageProgramName or SvSwShSourceCoverage.OpaqueProgramName or SvSwShSourceBase.ProgramName or SvSwShSourceBase.MirroredProgramName or SvSwShSourceBase.UnlitProgramName || TransparentEmission(name))
                    ? EmissionShader(TransparentEmission(name) ? giantTransparent! : giantDonor, extension, name) : profile.LayeredEyes && name == (profile.LayeredEyeEmission ? ZaSwShEyelidMaterial.EmissionProgramName : ZaSwShEyelidMaterial.ProgramName)
                    ? extension == ".bnsh_fsh" ? ZaSwShEyeShader.Convert(Member(giantEye!, ZaSwShEyelidMaterial.NativeProgramName + extension), profile.CornealEyes, profile.LavaLayers, layerEmission: profile.LayeredEyeEmission)
                        : ZaSwShEyeShader.Vertex(Member(giantEye!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                    : profile.CornealEyes && name == SvSwShCornealMaterial.ProgramName
                    ? Member(giantCorneal!, SvSwShCornealMaterial.NativeProgramName + extension)
                    : transparent && Array.IndexOf(SvSwShTransparentMaterial.Programs, name) is var pass && pass >= 0
                    ? Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[pass] + extension)
                    : giantDonor.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(giantFire ?? giantDonor, name + extension);
            var giantPath = $"bin/archive/gpokemon/{stem}.gfpak";
            Report("packing", resource: giantPath);
            result.Add(File(giantPath, SwShGfPackFile.CreateWithPaths(giantMembers).Write()));
            foreach (var shiny in new[] { false, true })
            {
                var identity = new SwShPokemonResourceIdentity(species, pokemon.Form, gender, shiny);
                var row = new SwShPokemonResourceRow(identity, modelFolder + stem + (shiny ? "_rare" : "") + ".gfbmdl",
                    $"bin/pokemon/{stem}/{stem}.gfbpokecfg", $"bin/archive/pokemon/{stem}{(shiny && profile.CompactResources ? "_rare" : "")}.gfpak",
                    new[] { "battle", "camp", "field" }.ToDictionary(g => g, g => animationFolder + stem + "_" + g + ".gfbanmcfg", StringComparer.Ordinal));
                result.Add(new(SwShRestorationComposer.CatalogPath, SwShRestorationWriter.ModelCatalog, SwShRestorationComposer.CatalogKey(identity), null, JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            return result;
        }

        private static SwShEffectLocator[] Locators(PreviewScene scene, IReadOnlyDictionary<string, string> aliases)
        {
            var bounds = scene.Primitives.Select(p => SwShModelWriter.Bounds(p, 100)).ToArray();
            var min = bounds.Select(b => b.Min).Aggregate(Vector3.Min); var max = bounds.Select(b => b.Max).Aggregate(Vector3.Max);
            var names = scene.Rig.Bones.Select(b => aliases[b.Name]).ToHashSet(StringComparer.Ordinal);
            SwShEffectLocator L(string name, string parent, Vector3 position) => new(name, names.Contains(parent) ? parent : "Origin", position);
            return [L("EffMouth01", "Head", new(0, (max.Y - min.Y) * .03f, max.Z * .8f)), L("EffOverHead01", "Origin", new(0, max.Y, 0)),
                L("EffEye01", "Head", new(-(max.X - min.X) * .1f, 0, max.Z * .8f)), L("EffEye02", "Head", new((max.X - min.X) * .1f, 0, max.Z * .8f)),
                L("EffHeadCenter01", "Head", Vector3.Zero), L("EffHand01", "RHand", Vector3.Zero), L("EffHand02", "LHand", Vector3.Zero),
                L("EffFoot01", "RFoot", Vector3.Zero), L("EffFoot02", "LFoot", Vector3.Zero), L("EffFront01", "Origin", new(0, max.Y * .5f, max.Z)),
                L("EffCenter01", "Origin", new(0, max.Y * .5f, 0)), L("EffShoot01_01", "Head", new(0, 0, max.Z)), L("EffAttack01_01", "Head", new(0, 0, max.Z)),
                L("EffGcloud01", "Origin", new(0, max.Y * 1.2f, 0))];
        }
    }
}
