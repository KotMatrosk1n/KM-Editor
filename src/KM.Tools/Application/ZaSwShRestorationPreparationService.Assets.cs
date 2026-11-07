// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Text.Json;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.SV;
using KM.Formats.ZA;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static readonly IReadOnlyDictionary<string, string> NativeClips = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ba01_landA01"] = "00152", ["ba01_landB01"] = "00153", ["ba01_landC01"] = "00155",
            ["ba10_waitA01"] = "00001", ["ba10_waitB01"] = "00010", ["ba20_buturi01"] = "00400", ["ba21_tokusyu01"] = "00450",
            ["ba30_damageS01"] = "00500", ["ba41_down01"] = "00520", ["eye01"] = "08000", ["mouth01"] = "08100", ["ba02_roar01"] = "00300",
            ["fi20_walk01"] = "00030", ["fi21_run01"] = "00100", ["kw20_drowseA01"] = "00280", ["kw20_drowseB01"] = "00281", ["kw20_drowseC01"] = "00282",
            ["kw21_sleepA01"] = "00280", ["kw21_sleepB01"] = "00281", ["kw30_hate01"] = "00563", ["kw32_happyA01"] = "00550", ["kw32_happyB01"] = "00550",
            ["kw32_happyC01"] = "00550", ["kw33_moveA01"] = "00150", ["kw33_moveC01"] = "00155", ["kw34_lonely01"] = "00560", ["kw35_playA01"] = "00310",
            ["kw36_mad01"] = "00563", ["kw50_eatA01"] = "00293", ["kw50_eatB01"] = "00294", ["kw50_eatC01"] = "00295", ["kw60_touch01"] = "00320",
        };

        internal IReadOnlyList<SwShRestorationContribution> Assets(KM.SwSh.Pokemon.SwShPokemonIdentity identity)
        {
            var species = identity.Species; var profile = Profile(identity);
            SwShRestorationModelResources.RequireUnowned(Native("bin/pokemon/table/poke_resource_table.gfbpmcatalog"), SwShRestorationDefinitions.Get(definitionId));
            currentSpecies = species; Report("reading");
            var genders = profile.SeparateFemale ? new[] { 0, 1 } : new[] { 0 };
            string ModelPath(int gender)
            {
                var resource = SourceStem(species, gender, profile);
                return profile.PlaAssets ? $"bin/pokemon/{resource[..6]}/{resource}/mdl/{resource}.trmdl"
                    : $"{resource[..6]}/{resource}/{resource}.trmdl";
            }
            SwShRestorationGenderAudit.RequireModels(Donor(profile.PlaAssets ? "bin/pokemon/table/poke_resource_table.trpmcatalog"
                    : "ik_pokemon/catalog/catalog/poke_resource_table.trpmcatalog"),
                profile.SourceSpecies ?? species, identity.Form, genders.ToDictionary(g => g, ModelPath));
            var declaredMeshes = genders.SelectMany(g => new TrinityPreviewReader(Donor, topOriginMaterialUv: true)
                .Load(profile.PlaAssets ? ModelPath(g) : "ik_pokemon/data/" + ModelPath(g)).Primitives.Select(p => p.Name)).ToHashSet(StringComparer.Ordinal);
            var result = genders.SelectMany(g => Assets(species, g, profile, identity.Form, declaredMeshes)).ToList();
            var icon = SwShRestorationPartyIcons.Create(species, Native, token, identity.Form, textureEncoding);
            foreach (var gender in SwShRestorationComposer.IconGenders(species))
                result.Add(File(SwShRestorationComposer.IconPath(species, identity.Form, gender)[6..], icon));
            return result;
        }

        private IReadOnlyList<SwShRestorationContribution> Assets(int species, int gender, SpeciesProfile? formProfile = null, int form = 0,
            IReadOnlySet<string>? declaredMeshes = null)
        {
            var profile = formProfile ?? Profiles[species];
            var groups = new[] { "battle", "camp", "field" };
            var nativeClips = (profile.PlaAssets ? PlaMotions(species) : NativeClips).ToArray();
            var flames = profile.PairedFlames ? profile.FlameMaterials ?? ["fire"] : Array.Empty<string>();
            var stem = SwShRestorationModelResources.Variants(SwShRestorationDefinitions.Get(definitionId))
                .Single(v => v.Identity == new KM.SwSh.Pokemon.SwShPokemonIdentity(species, form) && v.Gender == gender).ResourceStem;
            var sourceStem = SourceStem(species, gender, profile);
            var sourceRoot = profile.PlaAssets ? $"bin/pokemon/{sourceStem[..6]}/{sourceStem}/" : $"ik_pokemon/data/{sourceStem[..6]}/{sourceStem}/";
            var sourceFolder = sourceRoot + (profile.PlaAssets ? "mdl/" : "");
            var sourceAnimationFolder = sourceRoot + (profile.PlaAssets ? "anm/" : "");
            var plaResources = profile.PlaAssets ? SwShRestorationPlaResources.Read(Donor(SwShRestorationPlaResources.CatalogPath),
                profile.SourceSpecies ?? species, form, gender, sourceFolder + sourceStem + ".trmdl") : null;
            string MotionName(string path) => plaResources?.MotionName(path, stem)
                ?? Path.GetFileNameWithoutExtension(path).Replace(sourceStem, stem, StringComparison.Ordinal);
            var graph = ModelAssetGraph.Read(new[] { (sourceFolder + sourceStem + ".trmdl", (string?)null),
                (sourceFolder + sourceStem + "_rare.trmtr", null), (plaResources?.BaseAnimation ?? sourceAnimationFolder + sourceStem + "_base.tracn", null) }, (path, _) => Donor(path));
            var sourceScene = new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(sourceFolder + sourceStem + ".trmdl");
            var formVisibilityMeshes = FormVisibilityMeshes(species, sourceStem, sourceFolder, sourceScene);
            Report("reading");
            var materialBytes = Donor(sourceFolder + sourceStem + ".trmtr");
            var surfaceContracts = species == 530 && form == 1 ? SvSwShSurfaceFlow.Contracts(materialBytes, sourceScene,
                name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name))) : null;
            if (surfaceContracts is not null) sourceScene = SvSwShSurfaceFlow.RequireSecondaryMaskScene(materialBytes, sourceScene, surfaceContracts);
            var layerSurfaceContracts = ZaSwShLayerSurface.Contracts(materialBytes);
            var unlitContracts = ZaSwShDisplacedUnlit.Contracts(materialBytes);
            var gemSettings = FresnelSettings(materialBytes);
            var tangentBodies = ZaSwShTangentBody.Contracts(materialBytes, sourceScene);
            var tangentGeometry = new Dictionary<string, ZaSwShFresnelMaterial.Settings>(gemSettings, StringComparer.Ordinal);
            foreach (var name in tangentBodies.Keys) tangentGeometry.Add(name, new(0, 0, 1));
            sourceScene = ZaSwShLayerSurface.Geometry(sourceScene, layerSurfaceContracts);
            sourceScene = ZaSwShDisplacedUnlit.Geometry(sourceScene, unlitContracts);
            sourceScene = RequireFresnelGeometry(sourceFolder + sourceStem + ".trmdl", sourceScene, materialBytes, tangentGeometry);
            var preservedUvMaterials = tangentGeometry.Keys.Concat(unlitContracts.Keys).Concat(layerSurfaceContracts.Keys).ToHashSet(StringComparer.Ordinal);
            var scene = NormalizeUvs(sourceScene, true, preservedUvMaterials);
            var normalTileScales = NormalTileScales(scene, profile.NormalTiles);
            for (var i = 0; i < normalTileScales.Length; i++)
                if (tangentBodies.ContainsKey(scene.Primitives[i].Material.Name)) normalTileScales[i] = Vector2.One;
            var colorTileScales = ColorTileScales(scene, profile.ColorTiles);
            if (scene.Rig.Warnings.Length > 0) throw new InvalidDataException("Source model requires an additional conversion adapter.");
            var detailHierarchy = profile.RemoveDetailLevels == 0 ? null : SvSwShDetailMeshHierarchy.Remove(scene, profile.RemoveDetailLevels);
            if (detailHierarchy is not null) scene = detailHierarchy.Scene;
            string Code(string name, string value) => profile.MotionOverrides?.GetValueOrDefault(name)
                ?? (int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) + profile.AnimationOffset
                    + (species == 15 || profile.AirborneBattle && name.StartsWith("ba", StringComparison.Ordinal) ? 20000 : 0))
                    .ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
            bool EmptyExpression(string name) => name == "mouth01" || (name == "eye01"
                && (profile.EmptyEyes || species is 14 or 316 || profile.PlaAssets && species == 47));
            string NativeCode(string name, string value) => Code(name, EmptyExpression(name) ? "00000" : value);
            var campClips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            if ((species == 6 && form is 1 or 2) || HasQualifiedBaseCampForm(species, form))
            {
                foreach (var resource in graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)))
                {
                    if (profile.StandaloneMotionTimelines?.ContainsKey(Path.GetFileNameWithoutExtension(resource.Id)) == true) continue;
                    // Heatran's alternate offscreen clips share the visible clip's
                    // numeric ID. The native bank uses the visible source motion.
                    if (species == 485 && form == 1 && Path.GetFileNameWithoutExtension(resource.Id) is
                        "pm0485_51_00_00001_battlewait01_loop_offscreen" or "pm0485_51_00_00300_roar01_offscreen"
                        or "pm0485_51_00_00400_attack01_offscreen" or "pm0485_51_00_00450_rangeattack01_offscreen") continue;
                    var code = MotionName(resource.Id)[(stem.Length + 1)..].Split('_')[0];
                    campClips.Add(code, (resource.Bytes, PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones)));
                }
            }
            var nativePack = SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0007_00.gfpak"));
            var smokePack = species == 485 && form == 1 ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0324_00.gfpak")) : null;
            var smoke = smokePack is null ? null : new SwShHeatranSmoke(Member(smokePack, "pm0324_00.gfbmdl"),
                Member(smokePack, "pm0324_00_ba10_waitA01.gfbanm"), Member(smokePack, "pm0324_00_gloop01.gfbanm"));
            var bodyModel = Member(nativePack, "pm0007_00.gfbmdl");
            var members = new Dictionary<string, byte[]>(StringComparer.Ordinal); var modelFolder = $"bin/pokemon/{stem}/mdl/"; var animationFolder = $"bin/pokemon/{stem}/anm/";
            var textures = new RestorationTexturePool(members, modelFolder, true);
            var firePack = profile.PairedFlames ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0004_00.gfpak")) : null;
            var fireModel = firePack is null ? null : Member(firePack, "pm0004_00.gfbmdl");
            (byte[] Model, Dictionary<string, SvSwShFlameTemplate> Flames) FlameTemplate(byte[] materials)
            {
                var model = bodyModel;
                var templates = new Dictionary<string, SvSwShFlameTemplate>(StringComparer.Ordinal);
                foreach (var name in flames)
                {
                    var template = SvSwShFlameMaterial.Template(model, fireModel!, SvSwShFlameMaterial.NormalizeName(materials, name),
                        SvSwShFlameMaterial.NormalizeName(materialBytes, name), templates.Count);
                    model = template.Model;
                    templates.Add(name, template);
                }
                return (model, templates);
            }
            var nativeModel = FlameTemplate(materialBytes).Model;
            var primalGroudon = species == 383 && form == 1;
            var chandelureEmission = species == 609 && form == 1;
            var malamarEmission = species == 687 && form == 1;
            var sourceTransparentEmission = chandelureEmission || malamarEmission;
            var transparentPack = profile.PlaAssets && species == 46 || primalGroudon || sourceTransparentEmission
                ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0577_00.gfpak")) : null;
            if (transparentPack is not null)
            {
                var source = new ModelBuffer(materialBytes);
                var transparent = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 15) is "Blend" or "BlendPreMultiAlpha").ToArray();
                if (!sourceTransparentEmission && (!transparent.Select(t => source.Text(t, 0)).Order().SequenceEqual(primalGroudon ? new[] { "body_b_02" } : new[] { "tra_l_eye", "tra_r_eye" })
                    || transparent.Any(t => !source.Tables(t, 1, 16).Select(s => source.Text(s, 0)).SequenceEqual(new[] { "Transparent" }))))
                    throw new InvalidDataException("PLA transparent eye surfaces require another adapter.");
                nativeModel = SvSwShTransparentMaterial.Template(nativeModel, Member(transparentPack, "pm0577_00.gfbmdl"));
            }
            var eyePack = profile.LayeredEyes ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0001_00.gfpak")) : null;
            var eyeContracts = profile.LayeredEyes ? ZaSwShEyelidMaterial.Materials(materialBytes, sourceScene, profile.EyeAtlasOffsets, profile.IndependentEyeHighlights, layerEmission: profile.LayeredEyeEmission, layerTint: profile.LayeredEyeTint) : null;
            var eyeProgram = profile.LayeredEyeEmission ? ZaSwShEyelidMaterial.EmissionProgramName : profile.LayeredEyeTint ? ZaSwShEyelidMaterial.TintProgramName : profile.IndependentEyeHighlights ? ZaSwShEyelidMaterial.HighlightProgramName : ZaSwShEyelidMaterial.ProgramName;
            if (eyePack is not null) nativeModel = ZaSwShEyelidMaterial.Template(nativeModel, Member(eyePack, "pm0001_00.gfbmdl"), independentHighlights: profile.IndependentEyeHighlights, layerEmission: profile.LayeredEyeEmission, layerTint: profile.LayeredEyeTint);
            var legacyEmission = profile.LayerEmission;
            var emissionContracts = legacyEmission ? SvSwShLayerEmission.Contracts(materialBytes, primalGroudon ? sourceScene : scene) : null;
            var rareEmissionContracts = legacyEmission ? SvSwShLayerEmission.Contracts(Donor(sourceFolder + sourceStem + "_rare.trmtr"), primalGroudon ? sourceScene : scene) : null;
            var fresnelPack = emissionContracts?.Values.Any(c => c.FresnelNormal is not null) == true
                ? SwShGfPackFile.Parse(Native("bin/archive/pokemon/pm0007_00.gfpak")) : null;
            if (emissionContracts is not null && (primalGroudon || sourceTransparentEmission))
                nativeModel = SvSwShLayerEmission.Templates(nativeModel, emissionContracts);
            else if (emissionContracts is not null)
                foreach (var program in emissionContracts.Values.Concat(rareEmissionContracts!.Values).Select(c => c.Program()).Distinct(StringComparer.Ordinal))
                    nativeModel = SvSwShLayerEmission.Template(nativeModel, program);
            if (surfaceContracts is not null) nativeModel = SvSwShLayerEmission.Template(nativeModel, SvSwShSurfaceFlow.Program(surfaceContracts));
            var gem = gemSettings.Count == 0 ? null : FresnelTextures(materialBytes, sourceFolder + sourceStem + ".trmtr", false, gemSettings, textures);
            var rareGemMaterials = gem is null ? null : Donor(sourceFolder + sourceStem + "_rare.trmtr");
            var rareGem = gem is null ? null : FresnelTextures(rareGemMaterials!, sourceFolder + sourceStem + "_rare.trmtr", true, FresnelSettings(rareGemMaterials!), textures);
            var gemPrograms = gem is null ? null : FresnelPrograms(gem, rareGem!);
            if (gem is not null)
                foreach (var group in gem.Settings.GroupBy(p => p.Value.Program, StringComparer.Ordinal))
                {
                    var (name, settings) = group.First();
                    nativeModel = ZaSwShFresnelMaterial.TemplateWithTextures(nativeModel, materialBytes, name,
                        settings.LayerUv, settings.AmbientUv, settings.SourceVariant, settings.MaskUv, gem.Images[name]);
                }
            foreach (var program in tangentBodies.Values.Select(c => c.Program).Distinct(StringComparer.Ordinal))
                nativeModel = SvSwShLayerEmission.Template(nativeModel, program, SwShZaTangentBodyShader.Parent(program));
            var unlit = UnlitTextures(materialBytes, sourceFolder + sourceStem + ".trmtr", false, textures);
            var rareUnlit = UnlitTextures(Donor(sourceFolder + sourceStem + "_rare.trmtr"), sourceFolder + sourceStem + "_rare.trmtr", true, textures);
            if (!unlit.Contracts.Keys.Order(StringComparer.Ordinal).SequenceEqual(rareUnlit.Contracts.Keys.Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Source Unlit palettes disagree on material consumers.");
            foreach (var shader in unlit.Contracts.GroupBy(p => p.Value.Shader))
            {
                var first = shader.First();
                nativeModel = ZaSwShDisplacedUnlit.Template(nativeModel, first.Value, unlit.Images[first.Key]);
            }
            var layerSurface = LayerSurfaceTextures(materialBytes, sourceFolder + sourceStem + ".trmtr", false, textures);
            var rareLayerSurface = LayerSurfaceTextures(Donor(sourceFolder + sourceStem + "_rare.trmtr"), sourceFolder + sourceStem + "_rare.trmtr", true, textures);
            if (!layerSurface.Contracts.Keys.Order().SequenceEqual(rareLayerSurface.Contracts.Keys.Order()))
                throw new InvalidDataException("Source surface palettes disagree on consumers.");
            foreach (var shader in layerSurface.Contracts.GroupBy(p => p.Value.Shader))
            {
                var first = shader.First();
                nativeModel = ZaSwShLayerSurface.Template(nativeModel, first.Value, layerSurface.Images[first.Key]);
            }
            var nativeMetadata = new ModelBuffer(nativeModel);
            // Golurk's material sections share authored visibility nodes. Keeping
            // those groups avoids extra joints without removing any rig bones.
            var targetScene = UniqueMaterials(scene, shareMeshNodes: species == 623 && form == 1);
            var originalMaterialNames = targetScene.Primitives.Select((p, i) => (p.Material.Name, Original: scene.Primitives[i].Material.Name))
                .ToDictionary(p => p.Name, p => p.Original, StringComparer.Ordinal);
            var separateBaseNames = targetScene.Primitives.Where((_, index) =>
                    emissionContracts?.GetValueOrDefault(scene.Primitives[index].Material.Name)?.SeparateBaseColors == true)
                .Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            var baseEmissionNames = targetScene.Primitives.Where((_, index) =>
                    emissionContracts?.GetValueOrDefault(scene.Primitives[index].Material.Name)?.BaseColors is not null)
                .Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            var coverageNames = targetScene.Primitives.Where((_, index) =>
                    emissionContracts?.GetValueOrDefault(scene.Primitives[index].Material.Name)?.FifthMask is not null)
                .Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            var surfaceNames = targetScene.Primitives.Where((_, index) => surfaceContracts?.ContainsKey(scene.Primitives[index].Material.Name) == true)
                .Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            if (profile.PairedFlames)
            {
                var masks = flames.Select(name => targetScene.Primitives.Single(primitive => primitive.Material.Name == name))
                    .Select(flame => flame with { Name = flame.Name + "_mask", Material = flame.Material with { Name = flame.Material.Name + "_mask" } });
                targetScene = targetScene with { Primitives = targetScene.Primitives.Concat(masks).ToArray() };
            }
            var innerSections = sourceTransparentEmission ? scene.Primitives.Select((p, i) => (p, i))
                .Where(pair => emissionContracts?.GetValueOrDefault(pair.p.Material.Name) is { Transparent: true, InnerPass: true })
                .Select(pair => pair.i).ToArray() : [];
            if (innerSections.Length != 0)
            {
                targetScene = targetScene with { Primitives = targetScene.Primitives.Concat(innerSections.Select(i =>
                    SvSwShTransparentMaterial.Inner(targetScene.Primitives[i]))).ToArray() };
                foreach (var i in innerSections)
                {
                    var original = targetScene.Primitives[i].Material.Name;
                    var inner = original + "_inner";
                    originalMaterialNames.Add(inner, scene.Primitives[i].Material.Name);
                    if (baseEmissionNames.Contains(original)) baseEmissionNames.Add(inner);
                    if (separateBaseNames.Contains(original)) separateBaseNames.Add(inner);
                    if (coverageNames.Contains(original)) coverageNames.Add(inner);
                }
            }
            var textureTemplateName = sourceStem + profile.TextureSuffix;
            var textureTemplate = profile.MeshVisibility ? [] : Donor(sourceFolder + textureTemplateName + ".bntx");
            var aliases = BoneAliases(scene, sourceStem, stem);
            var airborneOffset = profile.AirborneBattle ? AirborneOffset(species, form, Donor(sourceFolder + sourceStem + ".trpokecfg")) : 0;
            var locatorPath = plaResources?.EffectLocators
                ?? sourceRoot + $"locators/{sourceStem}_{(species == 15 || profile.AirborneBattle ? 20000 : profile.AnimationOffset):00000}_eff.trskl";
            var locators = Locators(scene, aliases, Donor(locatorPath), profile.MouthPosition, airborneOffset, profile.PlaAssets,
                singleEye: species is 201 or 491 || species == 359 && form is 1 or 2 || species == 121 && form == 1);
            var fireTextures = new Dictionary<string, SvSwShFlameTextures>(StringComparer.Ordinal);
            foreach (var flame in flames)
            {
                var converted = SvSwShFlameMaterial.ZaTextures(SvSwShFlameMaterial.NormalizeName(materialBytes, flame),
                    name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                var allocated = converted.Files.ToDictionary(pair => pair.Key, pair => textures.Add(pair.Key, pair.Value), StringComparer.Ordinal);
                fireTextures.Add(flame, converted with { Layer = allocated[converted.Layer], Noise = allocated[converted.Noise],
                    Control = allocated[converted.Control], White = allocated[converted.White] });
            }
            if (flames.Length > 0) members[modelFolder + "viewport_mask_col.bntx"] = Member(firePack!, "viewport_mask_col.bntx");
            if (profile.NativeFlames)
                members[modelFolder + sourceStem + "_fire_alb.bntx"] = Donor(sourceFolder + sourceStem + "_fire_alb.bntx");
            foreach (var name in new[] { "Default_lta", "dummy_col", "projection_effect_col", "pm0007_00_Eye_amb" })
                members[modelFolder + name + ".bntx"] = Member(nativePack, name + ".bntx");
            if (transparentPack is not null)
                members[modelFolder + SvSwShTransparentMaterial.EnvironmentName + ".bntx"] = Member(transparentPack, SvSwShTransparentMaterial.EnvironmentName + ".bntx");
            var shaderNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in new[] { 3, 5 })
            {
                var (start, count) = nativeMetadata.Vector(nativeMetadata.Root, field, 4, 256);
                for (var i = 0; i < count; i++) shaderNames.Add(nativeMetadata.StringAt(start + i * 4));
            }
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                members[modelFolder + name + extension] = ZaSwShLayerSurface.Recognizes(name)
                    ? extension == ".bnsh_fsh" ? SwShZaLayerSurfaceShader.Fragment(Member(nativePack, "Body00" + extension), name)
                        : SwShZaLayerSurfaceShader.Vertex(Member(nativePack, "Body00" + extension), name)
                    : ZaSwShDisplacedUnlit.Recognizes(name)
                    ? extension == ".bnsh_fsh" ? SwShZaDisplacedUnlitShader.Fragment(Member(nativePack, "Body00" + extension), name)
                        : SwShZaDisplacedUnlitShader.Vertex(Member(nativePack, "Body00" + extension), name)
                    : SwShZaTangentBodyShader.Recognizes(name)
                    ? extension == ".bnsh_fsh" ? SwShZaTangentBodyShader.Fragment(Member(nativePack, "Body00" + extension), name)
                        : SwShZaTangentBodyShader.Vertex(Member(nativePack, "Body00" + extension))
                    : gemPrograms?.TryGetValue(name, out var gemProgram) == true
                    ? FresnelShader(Member(nativePack, "Body00" + extension), extension, gemProgram.Settings, gemProgram.Width)
                    : name == SvSwShLayerEmission.FresnelMirrorProgramName && fresnelPack is not null
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.FresnelMirrorOnce(Member(fresnelPack, "Body00" + extension)) : Member(fresnelPack, "Body00" + extension)
                    : name == SvSwShLayerEmission.FresnelProgramName && fresnelPack is not null
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Fresnel(Member(fresnelPack, "Body00" + extension)) : Member(fresnelPack, "Body00" + extension)
                    : name == SvSwShSurfaceFlow.SourceLayerProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceSurfaceLayers(Member(nativePack, "Body00" + extension))
                        : SvSwShSurfaceLayerVertexShader.Convert(Member(nativePack, "Body00" + extension))
                    : profile.LayerEmission && name == SvSwShLayerEmission.SourceOcclusionProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceOcclusion(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.ProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Convert(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.FiveLayerProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.GroudonFiveLayers(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : chandelureEmission && Array.IndexOf(SvSwShLayerEmission.TransparentPrograms, name) is var emissionPass && emissionPass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Convert(Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[emissionPass] + extension)) : Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[emissionPass] + extension)
                    : primalGroudon && Array.IndexOf(SvSwShLayerEmission.TransparentBasePrograms, name) is var layerPass && layerPass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.TransparentLayerBase(Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[layerPass] + extension)) : Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[layerPass] + extension)
                    : sourceTransparentEmission && Array.IndexOf(SvSwShSourceBase.TransparentPrograms, name) is var SourcePass && SourcePass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.TransparentSourceBase(Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[SourcePass] + extension)) : Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[SourcePass] + extension)
                    : sourceTransparentEmission && Array.IndexOf(SvSwShSourceBase.DynamicTransparentPrograms, name) is var DynamicPass && DynamicPass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.DynamicSourceBase(Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[DynamicPass] + extension), true) : Member(transparentPack!, SvSwShTransparentMaterial.NativePrograms[DynamicPass] + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.DynamicProgram
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.DynamicSourceBase(Member(nativePack, "Body00" + extension), false) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.MirroredProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.MirroredSourceBase(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.SharedUvProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SharedUvSourceBase(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.MatteMirrorProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.MatteMirroredSourceBase(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.ProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceLayerBase(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.UnlitProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceUnlitBase(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.BaseProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.LayerBase(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.UnlitBaseProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.UnlitLayerBase(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.MirroredProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.MirroredMask(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.ThreeLayerProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.ThreeLayer(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.AdditiveProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Additive(Member(nativePack, "Body00" + extension)) : Member(nativePack, "Body00" + extension)
                    : profile.LayeredEyes && name == eyeProgram
                    ? extension == ".bnsh_fsh" ? ZaSwShEyeShader.Convert(Member(eyePack!, ZaSwShEyelidMaterial.NativeProgramName + extension), independentHighlights: profile.IndependentEyeHighlights, layerEmission: profile.LayeredEyeEmission, layerTint: profile.LayeredEyeTint, mirrorEmissionColor: eyeContracts?.Values.Any(c => c.MirroredColor) == true)
                        : ZaSwShEyeShader.Vertex(Member(eyePack!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                    : transparentPack is not null && Array.IndexOf(SvSwShTransparentMaterial.Programs, name) is var pass && pass >= 0
                    ? Member(transparentPack, SvSwShTransparentMaterial.NativePrograms[pass] + extension)
                    : nativePack.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(firePack ?? nativePack, name + extension);
            SwShConvertedGeometry? geometry = null;
            Dictionary<string, Vector4>? colorUvTransforms = null;
            foreach (var rare in new[] { false, true })
            {
                var variant = rare ? PreviewMaterialVariant.Shiny(new(Donor(sourceFolder + sourceStem + ".trmmt")), sourceFolder + sourceStem + ".trmmt")
                    ?? throw new InvalidDataException("Z-A shiny materials are missing.") : null;
                var sourceMaterialBytes = rare ? Donor(variant!.Materials.Single()) : materialBytes;
                var variantScene = rare ? NormalizeUvs(RequireFresnelGeometry(sourceFolder + sourceStem + ".trmdl",
                    new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(sourceFolder + sourceStem + ".trmdl", variant), sourceMaterialBytes, tangentGeometry), true,
                    preservedUvMaterials) : scene;
                variantScene = ZaSwShLayerSurface.Geometry(variantScene, layerSurfaceContracts);
                variantScene = ZaSwShDisplacedUnlit.Geometry(variantScene, unlitContracts);
                var paletteTangent = ZaSwShTangentBody.Contracts(sourceMaterialBytes, sourceScene);
                if (paletteTangent.Count != tangentBodies.Count || tangentBodies.Any(p =>
                    !paletteTangent.TryGetValue(p.Key, out var contract) || contract.Program != p.Value.Program))
                    throw new InvalidDataException("ZA tangent body palettes disagree on material programs.");
                var paletteNormalScales = NormalTileScales(variantScene, profile.NormalTiles);
                for (var i = 0; i < paletteNormalScales.Length; i++)
                    if (paletteTangent.ContainsKey(variantScene.Primitives[i].Material.Name)) paletteNormalScales[i] = Vector2.One;
                if (!paletteNormalScales.SequenceEqual(normalTileScales))
                    throw new InvalidDataException("Z-A palettes disagree on normal tile sections.");
                if (!ColorTileScales(variantScene, profile.ColorTiles).SequenceEqual(colorTileScales))
                    throw new InvalidDataException("Z-A palettes disagree on color tile sections.");
                if (variantScene.Primitives.Count != scene.Primitives.Count || variantScene.Primitives.Where((p, i) => p.Material.Name != scene.Primitives[i].Material.Name).Any())
                    throw new InvalidDataException("Z-A shiny material identities do not match the model.");
                var normalAtlases = (species, form) switch
                {
                    (18, 1) => ZaSwShNormalAtlas.Pidgeot(variantScene, sourceMaterialBytes),
                    (26, 3) => ZaSwShNormalAtlas.RaichuY(variantScene, sourceMaterialBytes),
                    (505, 0) => ZaSwShNormalAtlas.Watchog(variantScene, sourceMaterialBytes),
                    (623, 1) => ZaSwShNormalAtlas.Golurk(variantScene, sourceMaterialBytes),
                    (652, 1) => ZaSwShNormalAtlas.Chesnaught(variantScene, sourceMaterialBytes),
                    (398, 1) => ZaSwShNormalAtlas.Staraptor(variantScene, sourceMaterialBytes),
                    (768, 1) => ZaSwShNormalAtlas.Golisopod(variantScene, sourceMaterialBytes),
                    (780, 1) => ZaSwShNormalAtlas.Drampa(variantScene, sourceMaterialBytes),
                    (448, 2) => ZaSwShNormalAtlas.LucarioZ(variantScene, sourceMaterialBytes),
                    (545, 1) => ZaSwShNormalAtlas.Scolipede(variantScene, sourceMaterialBytes),
                    (689, 1) => ZaSwShNormalAtlas.Barbaracle(variantScene, sourceMaterialBytes),
                    (740, 1) => ZaSwShNormalAtlas.Crabominable(variantScene, sourceMaterialBytes),
                    (801, 2) => ZaSwShNormalAtlas.Magearna(variantScene, sourceMaterialBytes),
                    (801, 3) => ZaSwShNormalAtlas.MagearnaOriginal(variantScene, sourceMaterialBytes),
                    (668, 1) => ZaSwShNormalAtlas.Pyroar(variantScene, sourceMaterialBytes),
                    (491, 1) => ZaSwShNormalAtlas.Darkrai(variantScene, sourceMaterialBytes),
                    (530, 1) => ZaSwShNormalAtlas.Excadrill(variantScene, sourceMaterialBytes),
                    (807, 1) => ZaSwShNormalAtlas.Zeraora(variantScene, sourceMaterialBytes),
                    (718, 5) => ZaSwShNormalAtlas.Zygarde(variantScene, sourceMaterialBytes),
                    _ => null,
                };
                var paletteEmission = legacyEmission ? SvSwShLayerEmission.Contracts(sourceMaterialBytes, primalGroudon ? sourceScene : scene) : null;
                var paletteSurface = surfaceContracts is null ? null : SvSwShSurfaceFlow.Contracts(sourceMaterialBytes, sourceScene,
                    name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)));
                var paletteLayerSurface = rare ? rareLayerSurface : layerSurface;
                var paletteUnlit = rare ? rareUnlit : unlit;
                var paletteGem = rare ? rareGem : gem;
                var paletteEyes = profile.LayeredEyes ? ZaSwShEyelidMaterial.Materials(sourceMaterialBytes, sourceScene, profile.EyeAtlasOffsets, profile.IndependentEyeHighlights, layerEmission: profile.LayeredEyeEmission, layerTint: profile.LayeredEyeTint) : null;
                var eyeTextures = new Dictionary<int, ZaSwShEyelidMaterial.EyeTextures>();
                var flameTemplate = FlameTemplate(sourceMaterialBytes);
                SwShMaterialBinding FireBinding(string name, bool mask) => new(mask ? name + "_mask" : name,
                    mask ? flameTemplate.Flames[name].Mask : flameTemplate.Flames[name].Core,
                    mask ? new Dictionary<string, string> { ["Mask0Texture"] = fireTextures[name].White, ["Mask1Texture"] = fireTextures[name].White }
                    : new Dictionary<string, string> { ["LerpTex"] = fireTextures[name].Control, ["Blend0Tex"] = fireTextures[name].Layer,
                        ["Blend1Tex"] = fireTextures[name].Noise, ["MaskTex"] = "viewport_mask_col" });
                var baked = variantScene.Primitives.Select((primitive, index) =>
                {
                    if (flames.Contains(primitive.Material.Name, StringComparer.Ordinal)) return null;
                    if (paletteUnlit.Contracts.ContainsKey(primitive.Material.Name)) return null;
                    if (paletteGem?.Settings.ContainsKey(primitive.Material.Name) == true) return null;
                    if (paletteEyes?.TryGetValue(primitive.Material.Name, out var eyeContract) == true)
                    {
                        Report("textures", (rare ? scene.Primitives.Count : 0) + index, 2 * scene.Primitives.Count);
                        var images = ZaSwShEyelidMaterial.Textures(eyeContract, index, rare,
                            name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                        var allocated = images.Files.ToDictionary(pair => pair.Key, pair => textures.Add(pair.Key, pair.Value), StringComparer.Ordinal);
                        eyeTextures.Add(index, images with { Bindings = images.Bindings.ToDictionary(pair => pair.Key, pair => allocated[pair.Value], StringComparer.Ordinal) });
                        return null;
                    }
                    var templateName = textureTemplateName; var template = textureTemplate;
                    if (profile.MeshVisibility)
                    {
                        // Use each authored material's image size, including the small eyes.
                        var sourceMaterials = new ModelBuffer(sourceMaterialBytes);
                        var material = sourceMaterials.Tables(sourceMaterials.Root, 1, 128).Single(t => sourceMaterials.Text(t, 0) == primitive.Material.Name);
                        var color = sourceMaterials.Tables(material, 2, 64).Single(t => sourceMaterials.Text(t, 0) == "BaseColorMap");
                        var path = sourceMaterials.Text(color, 1) ?? throw new InvalidDataException("Z-A material texture is unnamed.");
                        templateName = Path.GetFileNameWithoutExtension(path);
                        template = Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", path));
                    }
                    Report("textures", (rare ? scene.Primitives.Count : 0) + index, 2 * scene.Primitives.Count);
                    var materialBake = profile.NativeFlames && primitive.Material.Name == "fire"
                        ? ZaSwShFlameMaterial.Bake(sourceMaterialBytes, rare, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding)
                        : ZaSwShMaterialBaker.Bake(primitive, sourceMaterialBytes, index, rare,
                            template, templateName, name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, paletteEyes?.ContainsKey(primitive.Material.Name) == true,
                            authoredAlpha: transparentPack is not null && !primalGroudon && !sourceTransparentEmission && (primitive.Material.Alpha is "Blend" or "BlendPreMultiAlpha"), encodingCache: textureEncoding,
                            normalUvScale: profile.BakeNormalTiles ? normalTileScales[index] : null,
                            colorUvScale: colorTileScales[index],
                            unlit: paletteEmission?.GetValueOrDefault(primitive.Material.Name)?.Unlit == true,
                            normalAtlas: paletteTangent.ContainsKey(primitive.Material.Name) ? null : normalAtlases?.GetValueOrDefault(primitive.Material.Name),
                            maximumTextureDimension: HasCalibratedCampForm(species, form) || species is 655 or 687 && form == 1 || layerSurfaceContracts.Count != 0 ? 512 : 2048,
                            secondaryMask: paletteSurface?.ContainsKey(primitive.Material.Name) == true || paletteLayerSurface.Contracts.ContainsKey(primitive.Material.Name));
                    if (paletteTangent.TryGetValue(primitive.Material.Name, out var tangentBody))
                        materialBake = materialBake with { NormalName = Path.GetFileNameWithoutExtension(tangentBody.Normal),
                            Normal = Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", tangentBody.Normal)), NormalUvScale = Vector2.One,
                            AmbientName = tangentBody.Occlusion is null ? materialBake.AmbientName : Path.GetFileNameWithoutExtension(tangentBody.Occlusion),
                            Ambient = tangentBody.Occlusion is null ? materialBake.Ambient : Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", tangentBody.Occlusion)) };
                    if (paletteEmission?.GetValueOrDefault(primitive.Material.Name)?.SourceOcclusion is { } sourceAo)
                        materialBake = materialBake with { AmbientName = Path.GetFileNameWithoutExtension(sourceAo),
                            Ambient = Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", sourceAo)) };
                    if (paletteEmission?.GetValueOrDefault(primitive.Material.Name) is { Additive: true } additive)
                        materialBake = SvSwShLayerEmission.AdditiveColor(additive, primitive, materialBake, sourceMaterialBytes,
                            name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                    Report("textures", (rare ? scene.Primitives.Count : 0) + index + 1, 2 * scene.Primitives.Count);
                    if (profile.BakeNormalTiles)
                    {
                        if (materialBake.NormalUvScale != normalTileScales[index])
                            throw new InvalidDataException("Generated Z-A normal tile scale differs from its material binding.");
                    }
                    else materialBake = NormalTile(primitive, materialBake, normalTileScales[index]);
                    return materialBake with
                    {
                        ColorName = textures.Add(materialBake.ColorName, materialBake.Color),
                        NormalName = textures.Add(materialBake.NormalName, materialBake.Normal),
                        AmbientName = textures.Add(materialBake.AmbientName, materialBake.Ambient),
                    };
                }).ToArray();
                var emissionIndex = profile.EmissionMaterial is null ? -1 : Array.FindIndex(variantScene.Primitives.ToArray(), primitive => primitive.Material.Name == profile.EmissionMaterial);
                if (profile.EmissionMaterial is not null && emissionIndex < 0) throw new InvalidDataException("Z-A emission material is missing.");
                var emission = emissionIndex < 0 ? null : ZaSwShEmissionMaterial.Bake(sourceMaterialBytes, profile.EmissionMaterial!, baked[emissionIndex]!,
                    name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                var emissionName = emission is null ? null : textures.Add(emission.Name, emission.Bytes);
                var bindings = variantScene.Primitives.Select((primitive, index) =>
                {
                    if (flames.Contains(primitive.Material.Name, StringComparer.Ordinal)) return FireBinding(primitive.Material.Name, false);
                    if (paletteLayerSurface.Contracts.TryGetValue(primitive.Material.Name, out var layerContract))
                    {
                        var programs = ModelNames(nativeMetadata, 3);
                        var material = Array.FindIndex(nativeMetadata.Tables(nativeMetadata.Root, 6, 128),
                            m => programs[checked((int)nativeMetadata.Value(m, 8))] == layerContract.Shader);
                        if (material < 0) throw new InvalidDataException("Source surface template is missing.");
                        var images = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[material], 11, 32)
                            .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                        foreach (var binding in layerContract.Textures)
                            images[binding.NativeRole] = paletteLayerSurface.Images[primitive.Material.Name][binding.SourceRole];
                        images["NormalMapTex"] = baked[index]!.NormalName; images["AmbientTex"] = baked[index]!.AmbientName;
                        images["LightTblTex"] = "Default_lta"; images["EffectTex"] = "projection_effect_col";
                        return new SwShMaterialBinding(primitive.Material.Name, material, images) { UseSecondaryUv = layerContract.SecondaryUv };
                    }
                    if (paletteUnlit.Contracts.TryGetValue(primitive.Material.Name, out var unlitContract))
                    {
                        var programs = ModelNames(nativeMetadata, 3);
                        var material = Array.FindIndex(nativeMetadata.Tables(nativeMetadata.Root, 6, 128),
                            m => programs[checked((int)nativeMetadata.Value(m, 8))] == unlitContract.Shader);
                        if (material < 0) throw new InvalidDataException("Source Unlit template is missing.");
                        var images = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[material], 11, 32)
                            .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                        foreach (var binding in unlitContract.Textures)
                            images[binding.NativeRole] = paletteUnlit.Images[primitive.Material.Name][binding.SourceRole];
                        images["LightTblTex"] = "Default_lta"; images["EffectTex"] = "projection_effect_col";
                        return new SwShMaterialBinding(primitive.Material.Name, material, images)
                            { UseSourceNormalComponents = true, UseSourceColor0 = true, UseSecondaryUv = unlitContract.SecondaryUv };
                    }
                    if (paletteGem?.Settings.TryGetValue(primitive.Material.Name, out var settings) == true)
                    {
                        var programs = ModelNames(nativeMetadata, 3);
                        var material = Array.FindIndex(nativeMetadata.Tables(nativeMetadata.Root, 6, 128),
                            m => programs[checked((int)nativeMetadata.Value(m, 8))] == settings.Program);
                        if (material < 0) throw new InvalidDataException("ZA gem native template is missing.");
                        var images = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[material], 11, 32)
                            .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                        foreach (var binding in ZaSwShFresnelMaterial.TextureBindings(sourceMaterialBytes, primitive.Material.Name, settings.MaskedEmission))
                            images[binding.NativeRole] = paletteGem.Images[primitive.Material.Name][binding.SourceRole];
                        images["LightTblTex"] = "Default_lta"; images["EffectTex"] = "projection_effect_col";
                        return new SwShMaterialBinding(primitive.Material.Name, material, images)
                            { UseSourceNormalComponents = true, SourceTangentSet = 0, UseSecondaryUv = settings.SecondaryUv };
                    }
                    var nativeMaterial = primitive.Material.Name.Contains("eye", StringComparison.Ordinal) ? 1 : 0;
                    if (paletteEmission?.ContainsKey(primitive.Material.Name) == true) nativeMaterial = nativeMetadata.Tables(nativeMetadata.Root, 6, 128).Length - 1;
                    if (transparentPack is not null && (primitive.Material.Alpha is "Blend" or "BlendPreMultiAlpha"))
                        nativeMaterial = nativeMetadata.Tables(nativeMetadata.Root, 6, 128).Length - 2;
                    if (paletteEmission?.GetValueOrDefault(primitive.Material.Name) is { } selectedEmission)
                    {
                        var (programStart, programCount) = nativeMetadata.Vector(nativeMetadata.Root, 3, 4, 256);
                        var programs = Enumerable.Range(0, programCount).Select(i => nativeMetadata.StringAt(programStart + i * 4)).ToArray();
                        nativeMaterial = Array.FindIndex(nativeMetadata.Tables(nativeMetadata.Root, 6, 128),
                            m => programs[nativeMetadata.Value(m, 8)] == selectedEmission.Program());
                        if (nativeMaterial < 0) throw new InvalidDataException("Primal Groudon material program is missing.");
                    }
                    if (paletteSurface?.ContainsKey(primitive.Material.Name) == true)
                    {
                        var programs = ModelNames(nativeMetadata, 3);
                        nativeMaterial = Array.FindIndex(nativeMetadata.Tables(nativeMetadata.Root, 6, 128),
                            m => programs[checked((int)nativeMetadata.Value(m, 8))] == SvSwShSurfaceFlow.Program(paletteSurface));
                        if (nativeMaterial < 0) throw new InvalidDataException("Source secondary mask program is missing.");
                    }
                    if (paletteTangent.TryGetValue(primitive.Material.Name, out var tangentBody))
                    {
                        var programs = ModelNames(nativeMetadata, 3);
                        nativeMaterial = Array.FindIndex(nativeMetadata.Tables(nativeMetadata.Root, 6, 128),
                            m => programs[checked((int)nativeMetadata.Value(m, 8))] == tangentBody.Program);
                        if (nativeMaterial < 0) throw new InvalidDataException("ZA tangent body template is missing.");
                    }
                    var textureBindings = nativeMetadata.Tables(nativeMetadata.Tables(nativeMetadata.Root, 6, 128)[nativeMaterial], 11, 64)
                        .ToDictionary(t => nativeMetadata.Text(t, 0)!, _ => "dummy_col", StringComparer.Ordinal);
                    if (eyeTextures.TryGetValue(index, out var eyeImages))
                    {
                        foreach (var pair in eyeImages.Bindings) textureBindings[pair.Key] = pair.Value;
                    }
                    else
                    {
                        var materialBake = baked[index] ?? throw new InvalidDataException("Converted material is missing.");
                        textureBindings["Col0Tex"] = materialBake.ColorName;
                        textureBindings["NormalMapTex"] = materialBake.NormalName; textureBindings["AmbientTex"] = materialBake.AmbientName;
                    }
                    textureBindings["LightTblTex"] = "Default_lta";
                    if (!eyeTextures.TryGetValue(index, out var authoredEyes) || !authoredEyes.Bindings.ContainsKey("EffectTex"))
                        textureBindings["EffectTex"] = "projection_effect_col";
                    if (transparentPack is not null && (primitive.Material.Alpha is "Blend" or "BlendPreMultiAlpha"))
                        textureBindings["SphereMapTex"] = SvSwShTransparentMaterial.EnvironmentName;
                    if (profile.NativeFlames && primitive.Material.Name == "fire") textureBindings["EmissionMaskTex"] = sourceStem + "_fire_alb";
                    if (index == emissionIndex) textureBindings["EmissionMaskTex"] = emissionName!;
                    if (paletteEmission?.TryGetValue(primitive.Material.Name, out var layerEmission) == true)
                    {
                        if (layerEmission.BaseColors is not null)
                        {
                            var materials = new ModelBuffer(sourceMaterialBytes);
                            var material = materials.Tables(materials.Root, 1, 128).Single(t => materials.Text(t, 0) == primitive.Material.Name);
                            var baseMap = materials.Tables(material, 2, 64).Single(t => materials.Text(t, 0) == "BaseColorMap");
                            var image = materials.Text(baseMap, 1)!;
                            textureBindings["Col0Tex"] = textures.Add(Path.GetFileNameWithoutExtension(image),
                                Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", image)));
                        }
                        var mask = SvSwShLayerEmission.Mask(layerEmission,
                            name => Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", name)), token, textureEncoding);
                        textureBindings["EmissionMaskTex"] = textures.Add(mask.Name, mask.Bytes);
                        if (layerEmission.FifthMask is { } fifthMask)
                            textureBindings["LyCol0Tex"] = textures.Add(Path.GetFileNameWithoutExtension(fifthMask),
                                Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", fifthMask)));
                        if (layerEmission.FresnelNormal is not null)
                        {
                            string Image(string path) => textures.Add(Path.GetFileNameWithoutExtension(path), Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", path)));
                            textureBindings["LyCol0Tex"] = Image(layerEmission.FresnelNormal);
                            textureBindings["EffectTex"] = Image(layerEmission.FresnelNormal1!);
                        }
                    }
                    if (paletteSurface?.GetValueOrDefault(primitive.Material.Name) is { Noise: not null } surface)
                        textureBindings["LyCol0Tex"] = textures.Add(Path.GetFileNameWithoutExtension(surface.Noise),
                            Donor(TrinityPreviewReader.Resolve(sourceFolder + sourceStem + ".trmtr", surface.Noise)));
                    return new SwShMaterialBinding(primitive.Material.Name, nativeMaterial, textureBindings)
                        { ColorUvTransform = paletteEmission?.GetValueOrDefault(primitive.Material.Name) is { BaseColors: not null } basePalette
                                ? basePalette.MaskUv : baked[index]?.ColorUvTransform ?? new(1, 1, 0, 0),
                            UseSecondaryUv = paletteSurface?.ContainsKey(primitive.Material.Name) == true,
                            UseSourceNormalComponents = paletteTangent.ContainsKey(primitive.Material.Name),
                            SourceTangentSet = paletteTangent.ContainsKey(primitive.Material.Name) ? 0 : null,
                            NormalUvScale = baked[index]?.NormalUvScale ?? Vector2.One };
                }).ToArray();
                if (innerSections.Length != 0)
                {
                    var programs = ModelNames(nativeMetadata, 3);
                    bindings = bindings.Concat(innerSections.Select(i => bindings[i] with
                    {
                        Name = bindings[i].Name + "_inner",
                        NativeMaterial = Array.FindIndex(nativeMetadata.Tables(nativeMetadata.Root, 6, 128),
                            m => programs[checked((int)nativeMetadata.Value(m, 8))] == paletteEmission![scene.Primitives[i].Material.Name].Program(inner: true)),
                    })).ToArray();
                }
                if (profile.PairedFlames) bindings = bindings.Concat(flames.Select(name => FireBinding(name, true))).ToArray();
                bindings = bindings.Select((binding, index) => binding with { Name = targetScene.Primitives[index].Material.Name }).ToArray();
                var paletteColorTransforms = bindings.Where((_, index) => index < colorTileScales.Length && colorTileScales[index] != Vector2.One)
                    .ToDictionary(binding => binding.Name, binding => binding.ColorUvTransform, StringComparer.Ordinal);
                if (colorUvTransforms is null) colorUvTransforms = paletteColorTransforms;
                else if (colorUvTransforms.Count != paletteColorTransforms.Count
                    || colorUvTransforms.Any(pair => !paletteColorTransforms.TryGetValue(pair.Key, out var transform) || transform != pair.Value))
                    throw new InvalidDataException("Source palettes require different global color atlas transforms.");
                Report("geometry", rare ? 1 : 0, 2);
                var converted = SwShModelWriter.Convert(targetScene, profile.PairedFlames ? flameTemplate.Model : nativeModel, bindings, aliases, locators, cancellationToken: token, preserveMeshNames: profile.MeshVisibility);
                if (paletteEyes is not null)
                    converted = converted with { Model = ZaSwShEyelidMaterial.Bind(converted.Model, paletteEyes,
                        originalMaterialNames) };
                // Match the native species' highlight response. Aggron's source
                // separates the metallic armor and matte joints into materials.
                var specular = (species, form) switch
                {
                    (282, 1) => originalMaterialNames.Where(p => p.Value is "body_a" or "body_b")
                        .ToDictionary(p => p.Key, _ => (6f, .25f), StringComparer.Ordinal),
                    (306, 1) => originalMaterialNames.Where(p => p.Value is "body_a_00" or "body_b_00" or "body_a_01" or "body_b_01")
                        .ToDictionary(p => p.Key, p => p.Value is "body_a_00" or "body_b_00" ? (4f, .4f) : (5f, .3f), StringComparer.Ordinal),
                    _ => null,
                };
                converted = converted with { Model = SwShRestorationMaterialResponse.Apply(converted.Model, specular) };
                if (paletteLayerSurface.Contracts.Count != 0) converted = converted with { Model = ZaSwShLayerSurface.Bind(converted.Model,
                    paletteLayerSurface.Contracts, originalMaterialNames, paletteLayerSurface.Images) };
                if (paletteUnlit.Contracts.Count != 0) converted = converted with { Model = ZaSwShDisplacedUnlit.Bind(converted.Model,
                    paletteUnlit.Contracts, originalMaterialNames, paletteUnlit.Images) };
                if (paletteGem is not null) converted = converted with { Model = ZaSwShFresnelMaterial.BindTextures(converted.Model,
                    sourceMaterialBytes, originalMaterialNames, paletteGem.Images) };
                if (paletteEmission is not null) converted = converted with { Model = SvSwShLayerEmission.Bind(converted.Model, paletteEmission,
                    originalMaterialNames) };
                if (paletteEmission?.Values.Any(c => c.DynamicSource) == true) converted = converted with { Model = SvSwShDynamicSource.Bind(converted.Model, paletteEmission, originalMaterialNames) };
                if (paletteSurface is not null) converted = converted with { Model = SvSwShSurfaceFlow.Bind(converted.Model, paletteSurface, originalMaterialNames) };
                if (paletteTangent.Count != 0) converted = converted with { Model = ZaSwShTangentBody.Bind(converted.Model, paletteTangent, originalMaterialNames) };
                if (profile.NativeFlames) converted = converted with { Model = ZaSwShFlameMaterial.Emission(converted.Model) };
                if (emission is not null) converted = converted with { Model = ZaSwShFlameMaterial.Emission(converted.Model, profile.EmissionMaterial!, 0, emission.Gain) };
                converted = converted with { Model = CompactPrograms(converted.Model) };
                if (profile.CompactTextureReferences || paletteLayerSurface.Contracts.Count != 0 || paletteUnlit.Contracts.Count != 0 || paletteGem is not null || paletteEmission is not null)
                    converted = converted with { Model = CompactTextures(converted.Model) };
                geometry ??= converted;
                if (smoke is not null) converted = converted with { Model = smoke.Model(converted.Model, aliases) };
                members[modelFolder + stem + (rare ? "_rare" : "") + ".gfbmdl"] = converted.Model;
            }
            var sizeStem = SwShRestorationNativeTemplate.Stem(profile.NativeTemplate, Native);
            var sizePack = SwShGfPackFile.Parse(Native($"bin/archive/pokemon/{sizeStem}.gfpak"));
            if (smoke is not null)
            {
                foreach (var texture in smoke.TextureNames) members[modelFolder + texture + ".bntx"] = Member(smokePack!, texture + ".bntx");
                foreach (var shader in smoke.ShaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                    members[modelFolder + shader + extension] = Member(smokePack!, shader + extension);
                shaderNames.UnionWith(smoke.ShaderNames);
            }
            var materialNames = targetScene.Primitives.Select(p => p.Material.Name).Concat(smoke?.MaterialNames ?? []).ToArray();
            if ((species == 6 && form is 1 or 2) || HasQualifiedBaseCampForm(species, form))
                AddBaseFormCampMotions(scene, campClips, nativeClips.Where(p => p.Key != "mouth01")
                    .Select(p => (Code: NativeCode(p.Key, p.Value), SourceCode: BaseCampSourceMotionCode(species, form, p.Key, NativeCode(p.Key, p.Value))))
                    .Append((Code("mouth01", "00000"), Code("mouth01", "00000"))), species, form);
            var alphaNames = targetScene.Primitives.Where(p => emissionContracts?.GetValueOrDefault(originalMaterialNames[p.Material.Name])?.Additive == true)
                .Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            var meshNames = targetScene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var configuration = Member(sizePack, sizeStem + ".gfbpokecfg");
            if (profile.SourceFraming)
            {
                SourceFraming(configuration, Donor(plaResources?.Configuration ?? sourceRoot + sourceStem + ".trpokecfg"), species, form);
            }
            if (species == 6 && form is 1 or 2)
            {
                // The Mega wing spans need a preview fit between the source and native
                // Charizard scales. Retain the authored camera position and angles.
                var framing = new ModelBuffer(configuration);
                var at = framing.Field(framing.Root, 17);
                if (at == 0) throw new InvalidDataException("Native Charizard preview framing is missing.");
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(configuration.AsSpan(at, 4), 300);
            }
            var clips = new Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)>(StringComparer.Ordinal);
            byte[] ConvertMotion(byte[] skeleton, PreviewClip decoded, bool complete, SwShExpressionCurve expression = SwShExpressionCurve.None, bool empty = false)
            {
                var materialTracks = decoded.Materials.Where(track => !flames.Contains(track.Material, StringComparer.Ordinal)
                    && (expression == SwShExpressionCurve.None || track.Parameter != "SurfaceAlpha")
                    && !(baseEmissionNames.Contains(track.Material) && track.Parameter.StartsWith("BaseColorLayer", StringComparison.Ordinal))
                    && (!(legacyEmission || surfaceNames.Contains(track.Material)) || !(track.Parameter.StartsWith("EmissionIntensityLayer", StringComparison.Ordinal)
                        || track.Parameter.StartsWith("EmissionColorLayer", StringComparison.Ordinal)
                        || track.Parameter == SvSwShLayerEmission.CoverageTrack
                        || coverageNames.Contains(track.Material) && track.Parameter == "UVScaleOffset3"))).ToArray();
                if (expression == SwShExpressionCurve.None)
                    materialTracks = materialTracks.Concat(alphaNames.Where(name => !materialTracks.Any(t => t.Material == name && t.Parameter == "SurfaceAlpha"))
                        .Select(name => new PreviewMaterialTrack(name, "SurfaceAlpha", [[new(0, [0])]]))).ToArray();
                var converted = ZaSwShAnimationWriter.Convert(skeleton, materialTracks, scene.Rig.Bones, aliases, complete, expression, empty);
                if (expression != SwShExpressionCurve.None) return converted;
                // Retargeted camp clips retain model palettes. Additive effects
                // also reset their opacity so a preceding attack cannot linger.
                if ((legacyEmission || surfaceNames.Count != 0) && decoded.Materials.Any())
                    converted = SvSwShLayerEmission.Animation(converted, decoded.Materials, baseEmissionNames, separateBaseNames: separateBaseNames);
                if (coverageNames.Count != 0)
                    converted = SvSwShSurfaceFlow.Animation(converted, decoded.Materials);
                if (!profile.PairedFlames) return profile.MeshVisibility ? ZaSwShAnimationWriter.Visibility(converted, decoded.Visibility, meshNames) : converted;
                foreach (var flame in flames) converted = SvSwShFlameAnimation.Apply(converted, decoded.Materials, materialName: flame);
                var fireMeshes = targetScene.Primitives.Where(primitive => flames.Contains(primitive.Material.Name, StringComparer.Ordinal))
                    .Select(primitive => primitive.Name).ToHashSet(StringComparer.Ordinal);
                var visibility = decoded.Visibility.SelectMany(track => fireMeshes.Contains(track.Name) ? new[] { track, track with { Name = track.Name + "_mask" } } : [track]).ToArray();
                return ZaSwShAnimationWriter.Visibility(converted, visibility, meshNames);
            }
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)).ToArray();
            byte[] ReadSourceMaterial(string path)
            {
                var material = Donor(path);
                if ((species, form) is (485, 1) or (718, 5))
                    material = ZaSwShAnimationWriter.RemoveUnboundMaterial(material,
                        species == 485 ? "smog" : "eye", sourceScene);
                ZaSwShStaticSourcePalette.RequireAnimation(material, species, form);
                return material;
            }
            IEnumerable<(byte[] Bytes, PreviewClip Skeletal)> LayerSurfaceClips()
            {
                foreach (var motion in motions)
                {
                    token.ThrowIfCancellationRequested();
                    var skeletal = PreviewRigReader.Animation(new(motion.Bytes), motion.Id, scene.Rig.Bones);
                    yield return (ReadSourceMaterial(Path.ChangeExtension(motion.Id, ".tracm").Replace('\\', '/')), skeletal);
                }
            }
            // Static shader defaults already live in the material. Reset only the
            // parameters animated by any clip, so action changes cannot retain a
            // previous pose's value without allocating every static parameter.
            var layerAnimatedParameters = layerSurface.Contracts.Count == 0 ? null
                : ZaSwShLayerSurfaceAnimation.AnimatedParameters(LayerSurfaceClips(), layerSurface.Contracts);
            var convertedLayerParameters = layerAnimatedParameters is null ? null : originalMaterialNames
                .Where(p => layerSurface.Contracts.ContainsKey(p.Value))
                .ToDictionary(p => p.Key, p => layerAnimatedParameters[p.Value], StringComparer.Ordinal);
            if (profile.StandaloneMotionTimelines?.Keys.Any(name => !motions.Any(r => Path.GetFileNameWithoutExtension(r.Id) == name)) == true)
                throw new InvalidDataException("A declared standalone source motion is unavailable.");
            Report("animations", 0, motions.Length + nativeClips.Length);
            foreach (var resource in motions)
            {
                token.ThrowIfCancellationRequested();
                detailHierarchy?.RequireUnanimated(resource.Bytes);
                var clip = PreviewRigReader.Animation(new(resource.Bytes), resource.Id, scene.Rig.Bones);
                var materialPath = Path.ChangeExtension(resource.Id, ".tracm").Replace('\\', '/');
                // PLA's resting clips can declare only a skeletal stream.
                if (!profile.PlaAssets || graph.Any(r => r.Id == materialPath))
                {
                    var materialAnimation = ReadSourceMaterial(materialPath);
                    if (gem is not null) RequireFresnelPaletteAnimation(materialAnimation, gem, rareGem!);
                    PreviewClip DecodeLayers(byte[] sanitized) => legacyEmission
                        ? SvSwShLayerEmission.Read(sanitized, clip, scene, emissionContracts!,
                            remaining => ZaSwShAnimationWriter.ReadClip(remaining, clip, profile.MeshVisibility, profile.EmissionMaterial, eyeContracts))
                        : ZaSwShAnimationWriter.ReadClip(sanitized, clip, profile.MeshVisibility, profile.EmissionMaterial, eyeContracts);
                    PreviewClip DecodeOther(byte[] sanitized) => emissionContracts?.Values.Any(c => c.DynamicSource) == true
                        ? SvSwShDynamicSource.Read(sanitized, clip, emissionContracts, DecodeLayers) : DecodeLayers(sanitized);
                    PreviewClip DecodeLayerSurface(byte[] sanitized) => layerSurface.Contracts.Count != 0
                        ? ZaSwShLayerSurfaceAnimation.Read(sanitized, clip, layerSurface.Contracts, DecodeOther, layerAnimatedParameters) : DecodeOther(sanitized);
                    PreviewClip DecodeGem(byte[] sanitized) => gem is not null
                        ? ZaSwShFresnelAnimation.ReadClip(sanitized, clip, gem.Defaults, DecodeLayerSurface) : DecodeLayerSurface(sanitized);
                    clip = unlit.Contracts.Count != 0 ? ZaSwShDisplacedUnlitAnimation.Read(materialAnimation, clip, unlit.Contracts, DecodeGem)
                        : surfaceContracts is not null ? SvSwShSurfaceFlow.Read(materialAnimation, clip, surfaceContracts)
                        : gem is not null ? ZaSwShFresnelAnimation.ReadClip(materialAnimation, clip, gem.Defaults,
                            DecodeLayerSurface)
                        : layerSurface.Contracts.Count != 0 ? DecodeLayerSurface(materialAnimation)
                        : legacyEmission ? DecodeOther(materialAnimation)
                        : profile.PairedFlames ? ZaSwShAnimationWriter.ReadFlameClip(materialAnimation, clip, flames)
                        : ZaSwShAnimationWriter.ReadClip(materialAnimation, clip, profile.MeshVisibility, profile.EmissionMaterial, eyeContracts);
                }
                RequireStaticNormalTiles(clip, profile.NormalTiles);
                RequireStaticColorTiles(clip, profile.ColorTiles);
                // A material can be shared by multiple mesh sections. Each converted
                // material needs the authored channel, including hidden mouth sections.
                clip = clip with { Materials = clip.Materials.SelectMany(track => originalMaterialNames
                    .Where(pair => pair.Value == track.Material)
                    .Select(pair => track with { Material = pair.Key })).ToArray() };
                if (species == 658 && form == 3 && sourceStem == "pm0725_51_00")
                    clip = clip with { Visibility = GreninjaMegaVisibility(clip.Visibility, sourceScene) };
                else if (species == 740 && form == 1 && sourceStem == "pm0860_51_00")
                    clip = clip with { Visibility = CrabominableMegaVisibility(clip.Visibility, sourceScene) };
                else if (species == 115 && form == 1 && sourceStem == "pm0115_51_00")
                    clip = clip with { Visibility = KangaskhanMegaVisibility(clip.Visibility, sourceScene) };
                else if (species == 485 && form == 1 && sourceStem == "pm0485_51_00")
                    clip = clip with { Visibility = HeatranMegaVisibility(clip.Visibility, sourceScene) };
                else if (species == 720 && sourceStem == "pm0774_11_00")
                    clip = clip with { Visibility = HoopaUnboundVisibility(clip.Visibility, sourceScene) };
                else if (formVisibilityMeshes is not null)
                    clip = clip with { Visibility = SelectedFormVisibility(clip.Visibility, sourceScene, formVisibilityMeshes) };
                if (profile.SeparateFemale)
                {
                    if (species == 308)
                    {
                        // Medicham's shared animation names the original eye group;
                        // both gender models split it into the same two eye sections.
                        const string group = "pm0308_00_00_eye_mesh_shape";
                        var eyes = new[] { "pm0308_00_00_eye_a_mesh_shape", "pm0308_00_00_eye_b_mesh_shape" };
                        if (clip.Visibility.Any(v => v.Name == group))
                        {
                            if (eyes.Any(eye => declaredMeshes?.Contains(eye) != true || clip.Visibility.Any(v => v.Name == eye)))
                                throw new InvalidDataException("Medicham grouped eye visibility is ambiguous.");
                            clip = clip with { Visibility = clip.Visibility.SelectMany(v => v.Name == group
                                ? eyes.Select(eye => v with { Name = eye }) : [v]).ToArray() };
                        }
                    }
                    if (species == 322)
                    {
                        // Numel repeats its gender visibility on three lower LODs.
                        // Only the highest LOD is exported. Require every discarded
                        // channel to exactly match its exported source channel.
                        foreach (var visibility in clip.Visibility.Where(v => declaredMeshes?.Contains(v.Name) != true))
                        {
                            var suffix = visibility.Name.Length >= 5 ? visibility.Name[^5..] : "";
                            var original = suffix is "_lod1" or "_lod2" or "_lod3"
                                ? clip.Visibility.SingleOrDefault(v => v.Name == visibility.Name[..^5]) : null;
                            if (original is null || declaredMeshes?.Contains(original.Name) != true
                                || original.Keys.Length != visibility.Keys.Length
                                || !original.Keys.Zip(visibility.Keys).All(k => k.First.Frame == k.Second.Frame
                                    && k.First.Value.SequenceEqual(k.Second.Value)))
                                throw new InvalidDataException("Numel lower LOD visibility differs from its exported section.");
                        }
                        clip = clip with { Visibility = clip.Visibility.Where(v => declaredMeshes!.Contains(v.Name)).ToArray() };
                    }
                    // Shared gender animations may address sections that exist only in
                    // the sibling model. Reject undeclared targets before filtering them.
                    if (clip.Visibility.Any(v => declaredMeshes?.Contains(v.Name) != true))
                        throw new InvalidDataException("Source gender visibility needs another conversion adapter.");
                    clip = clip with { Visibility = clip.Visibility.Where(v => meshNames.Contains(v.Name)).ToArray() };
                }
                clip = clip with { Visibility = VisibilitySections(scene, targetScene, clip.Visibility) };
                if (innerSections.Length != 0)
                    clip = clip with { Visibility = clip.Visibility.SelectMany(v => innerSections.Select((sourceIndex, innerIndex) => (sourceIndex, innerIndex))
                        .Where(p => targetScene.Primitives[p.sourceIndex].Name == v.Name)
                        .Select(p => v with { Name = targetScene.Primitives[scene.Primitives.Count + p.innerIndex].Name }).Prepend(v)).ToArray() };
                Report("animations", clips.Count, motions.Length + nativeClips.Length);
                var name = MotionName(resource.Id); var code = name[stem.Length..].TrimStart('_').Split('_')[0];
                // Some PLA directions share a numeric code. Retain each exact
                // named timeline without making an ambiguous native action alias.
                if (profile.StandaloneMotionTimelines?.TryGetValue(Path.GetFileNameWithoutExtension(resource.Id), out var timeline) == true)
                {
                    if (clip.Frames != timeline.Frames || clip.Rate != 60 || clip.Loop != timeline.Loop)
                        throw new InvalidDataException("Standalone source motion timeline requires another adapter.");
                    members[animationFolder + name + ".gfbanm"] = ConvertMotion(resource.Bytes, clip, false);
                    continue;
                }
                if (!clips.TryAdd(code, (resource.Bytes, clip))) throw new InvalidDataException("Source animation code is ambiguous.");
                members[animationFolder + name + ".gfbanm"] = ConvertMotion(resource.Bytes, clip, false);
            }
            if (HasCalibratedCampForm(species, form))
                AddCalibratedCampMotions(scene, clips, nativeClips.Select(p => NativeCode(p.Key, p.Value)), species, form);
            foreach (var pair in campClips) clips.TryAdd(pair.Key, pair.Value);
            var nativeClipsDone = 0;
            foreach (var (name, code) in nativeClips)
            {
                Report("animations", motions.Length + nativeClipsDone++, motions.Length + nativeClips.Length);
                var expression = name == "eye01" ? SwShExpressionCurve.Eyes : name == "mouth01" ? SwShExpressionCurve.Mouth : SwShExpressionCurve.None;
                var empty = EmptyExpression(name);
                // An empty facial layer only needs timing. It writes no bones
                // or material tracks and leaves the authored body motion intact.
                var sourceCode = NativeCode(name, code);
                if (!clips.TryGetValue(sourceCode, out var clip)) throw new InvalidDataException($"Required Z-A motion is unavailable: {name}/{sourceCode}");
                var convertedMotion = ConvertMotion(clip.Skeleton, clip.Decoded, expression == SwShExpressionCurve.None, expression, empty);
                if (profile.PlaAssets && name is "ba21_tokusyu01" or "ba30_damageS01")
                {
                    var start = int.Parse(sourceCode, System.Globalization.CultureInfo.InvariantCulture);
                    var segments = Enumerable.Range(start, 3).Select(index => clips[index.ToString("00000", System.Globalization.CultureInfo.InvariantCulture)])
                        .Select(segment => ConvertMotion(segment.Skeleton, segment.Decoded, true)).ToArray();
                    var convertedBones = scene.Rig.Bones.Select(bone => bone with { Name = aliases.GetValueOrDefault(bone.Name, bone.Name) }).ToArray();
                    convertedMotion = SwShAnimationWriter.SkeletalSequence(segments, convertedBones, profile.LayeredEyes);
                }
                if (profile.AirborneBattle && name.StartsWith("ba", StringComparison.Ordinal))
                    convertedMotion = SwShAnimationWriter.VerticalOffset(convertedMotion, "Waist", airborneOffset);
                members[animationFolder + stem + "_" + name + ".gfbanm"] = convertedMotion;
            }
            var framingGeometry = profile.IdleFraming
                ? SwShRestorationFraming.IdleBounds(members, modelFolder, animationFolder, stem, geometry!, fullCycle: species == 325) : geometry!;
            members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"] = SwShModelWriter.Configuration(configuration, species, framingGeometry, materialNames,
                profile.BattleHeight, profile.FieldHeight);
            var summaryPath = $"bin/pokemon/{stem}/{stem}.gfbpokecfg";
            members[summaryPath] = SwShRestorationFraming.SummaryCorrection(members[summaryPath], definitionId);
            if (profile.BattleClearance != 0)
            {
                // The native battle camera expects these flying actors above the
                // arena origin. Keep the donor motion and translate its battle
                // placement in world units, after measuring the unshifted pose.
                var config = new ModelBuffer(members[summaryPath]);
                var modelHeight = config.Float(config.Field(config.Root, 8));
                var battleHeight = config.Float(config.Field(config.Root, 9));
                if (modelHeight <= 0 || battleHeight <= 0)
                    throw new InvalidDataException("Battle clearance requires positive model and battle heights.");
                var clearance = profile.BattleClearance * modelHeight / battleHeight;
                if (profile.SummaryClearanceReferenceHeight is float summaryHeight)
                    members[summaryPath] = SwShRestorationFraming.CompensateSummaryClearance(
                        members[summaryPath], profile.BattleClearance, summaryHeight);
                foreach (var (name, _) in nativeClips)
                {
                    if (!name.StartsWith("ba", StringComparison.Ordinal)) continue;
                    var path = animationFolder + stem + "_" + name + ".gfbanm";
                    members[path] = SwShAnimationWriter.VerticalOffset(members[path], "Waist", clearance);
                }
            }
            var battleEntry = SwShRestorationFraming.BattleEntry(members, modelFolder, animationFolder, stem);
            Report("packing");
            members[animationFolder + stem + "_battleidle01.gfbanm"] = SwShAnimationWriter.BattleIdle(members[animationFolder + stem + "_ba10_waitA01.gfbanm"]);
            members[animationFolder + stem + "_gloop01.gfbanm"] = SwShAnimationWriter.GlobalMaterialLoop(Member(nativePack, "pm0007_00_gloop01.gfbanm"),
                materialNames.Where(name => !flames.Any(flame => name == flame || name == flame + "_mask")).ToArray(),
                targetScene.Primitives.Select((primitive, index) => (primitive.Material.Name,
                    Scale: index < normalTileScales.Length ? normalTileScales[index] : Vector2.One))
                    .Where(pair => pair.Scale != Vector2.One).ToDictionary(pair => pair.Name, pair => pair.Scale, StringComparer.Ordinal), colorUvTransforms);
            var emissionNames = targetScene.Primitives.Where(p => emissionContracts?.ContainsKey(originalMaterialNames[p.Material.Name]) == true)
                .Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal);
            if (legacyEmission) members[animationFolder + stem + "_gloop01.gfbanm"] = SvSwShLayerEmission.GlobalLoop(
                members[animationFolder + stem + "_gloop01.gfbanm"], emissionNames, baseNames: baseEmissionNames, alphaNames: alphaNames, separateBaseNames: separateBaseNames, coverageNames: coverageNames);
            if (surfaceNames.Count != 0)
            {
                members[animationFolder + stem + "_gloop01.gfbanm"] = SvSwShLayerEmission.GlobalLoop(members[animationFolder + stem + "_gloop01.gfbanm"], surfaceNames);
                members[animationFolder + stem + "_gloop01.gfbanm"] = SvSwShSurfaceFlow.GlobalLoop(members[animationFolder + stem + "_gloop01.gfbanm"], surfaceNames);
            }
            if (coverageNames.Count != 0) members[animationFolder + stem + "_gloop01.gfbanm"] = SvSwShSurfaceFlow.GlobalLoop(
                members[animationFolder + stem + "_gloop01.gfbanm"], coverageNames);
            if (transparentPack is not null)
                members[animationFolder + stem + "_gloop01.gfbanm"] = SvSwShTransparentMaterial.GlobalLoop(
                    members[animationFolder + stem + "_gloop01.gfbanm"], targetScene.Primitives
                        .Where(p => p.Material.Alpha is "Blend" or "BlendPreMultiAlpha").Select(p => p.Material.Name).ToHashSet(StringComparer.Ordinal));
            if (eyeContracts is not null)
                members[animationFolder + stem + "_gloop01.gfbanm"] = ZaSwShEyelidMaterial.KeepAnimationLocal(
                    members[animationFolder + stem + "_gloop01.gfbanm"], materialNames.Where(name => name.Contains("eye", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal), profile.IndependentEyeHighlights, layerEmission: profile.LayeredEyeEmission, layerTint: profile.LayeredEyeTint);
            if (profile.EmissionMaterial is not null)
                members[animationFolder + stem + "_gloop01.gfbanm"] = ZaSwShEmissionMaterial.KeepAnimationLocal(
                    members[animationFolder + stem + "_gloop01.gfbanm"], profile.EmissionMaterial);
            var flameBones = profile.NativeFlames ? SvSwShFlameAnimation.LoopBones(scene, clips["08201"].Decoded) : [];
            if (profile.NativeFlames)
            {
                var loop = clips["08201"];
                var convertedLoop = ZaSwShAnimationWriter.Convert(loop.Skeleton, loop.Decoded.Materials, scene.Rig.Bones, aliases, completePose: false);
                members[animationFolder + stem + "_gloop01.gfbanm"] = flameBones.Length == 0 ? convertedLoop
                    : SvSwShFlameAnimation.SkeletonLoop(convertedLoop, convertedLoop, flameBones.Select(name => aliases[name]).ToArray());
            }
            if (profile.PairedFlames)
            {
                var loop = clips.GetValueOrDefault("08201");
                if (loop.Decoded is not null)
                {
                    flameBones = SvSwShFlameAnimation.LoopBones(scene, loop.Decoded, flames);
                    var convertedLoop = ZaSwShAnimationWriter.Convert(loop.Skeleton, [], scene.Rig.Bones, aliases, completePose: false);
                    var global = members[animationFolder + stem + "_gloop01.gfbanm"];
                    foreach (var flame in flames) global = SvSwShFlameAnimation.Apply(global, loop.Decoded.Materials, loop.Decoded, flame);
                    members[animationFolder + stem + "_gloop01.gfbanm"] = flameBones.Length == 0 ? global
                        : SvSwShFlameAnimation.SkeletonLoop(global, convertedLoop, flameBones.Select(name => aliases[name]).ToArray());
                }
            }
            var facialBones = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (smoke is not null) members[animationFolder + stem + "_gloop01.gfbanm"] = smoke.GlobalLoop(members[animationFolder + stem + "_gloop01.gfbanm"]);
            var eyelids = scene.Rig.Bones.Where(b => b.Name.Contains("eyelid", StringComparison.OrdinalIgnoreCase)).Select(b => aliases[b.Name]).ToArray();
            if (eyelids.Length > 0) facialBones.Add("eye01", eyelids);
            foreach (var group in groups)
            {
                var controller = ZaSwShAnimationWriter.Controller(Member(nativePack, "pm0007_00_" + group + ".gfbanmcfg"), "pm0007_00", stem, materialNames, facialBones, profile.LayeredEyes, profile.IndependentEyeHighlights, layerEmission: profile.LayeredEyeEmission, layerTint: profile.LayeredEyeTint);
                if (legacyEmission) controller = SvSwShLayerEmission.Controller(controller, emissionNames, baseNames: baseEmissionNames, alphaNames: alphaNames, separateBaseNames: separateBaseNames, coverageNames: coverageNames);
                if (surfaceNames.Count != 0)
                {
                    controller = SvSwShLayerEmission.Controller(controller, surfaceNames, colorNames: surfaceNames);
                    controller = SvSwShSurfaceFlow.Controller(controller, surfaceNames);
                }
                if (coverageNames.Count != 0) controller = SvSwShSurfaceFlow.Controller(controller, coverageNames);
                if (emissionContracts?.Values.Any(c => c.DynamicSource) == true) controller = SvSwShDynamicSource.Controller(controller,
                    originalMaterialNames.Where(p => emissionContracts.GetValueOrDefault(p.Value)?.DynamicSource == true).Select(p => p.Key).ToHashSet(StringComparer.Ordinal));
                if (layerSurface.Contracts.Count != 0) controller = ZaSwShLayerSurfaceAnimation.Controller(controller,
                    originalMaterialNames.Where(p => layerSurface.Contracts.ContainsKey(p.Value)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal),
                    animatedParameters: convertedLayerParameters);
                if (unlit.Contracts.Count != 0) controller = ZaSwShDisplacedUnlitAnimation.Controller(controller,
                    originalMaterialNames.Where(p => unlit.Contracts.ContainsKey(p.Value)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal));
                if (gem is not null) controller = ZaSwShFresnelAnimation.Controller(controller,
                    originalMaterialNames.Where(p => gem.Settings.ContainsKey(p.Value)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal));
                if (profile.NativeFlames || profile.PairedFlames)
                    controller = SvSwShFlameAnimation.Controller(controller, flameBones.Select(name => aliases[name]).ToArray(), profile.PairedFlames ? flames : null);
                if (smoke is not null) controller = smoke.Controller(controller);
                if (group == "battle") controller = SwShAnimationWriter.BattleIdleController(controller);
                if (group == "battle" && battleEntry) controller = SwShAnimationWriter.BattleEntryController(controller);
                if (group == "battle" && profile.NativeBattleAttackRouting)
                    controller = SwShAnimationWriter.BattleAttackController(controller, Member(sizePack, sizeStem + "_battle.gfbanmcfg"));
                members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = controller;
            }
            if (species == 6 && form is 1 or 2 || smoke is not null)
                foreach (var group in new[] { "camp", "field" })
                    members[animationFolder + stem + "_" + group + ".gfbanmcfg"] = SwShAnimationWriter.AddGlobalLoop(
                        members[animationFolder + stem + "_" + group + ".gfbanmcfg"], members[animationFolder + stem + "_battle.gfbanmcfg"]);
            members = SwShAnimationResourceAliases.Compact(members);
            var result = new List<SwShRestorationContribution>();
            foreach (var rare in new[] { false, true })
            {
                var suffix = rare ? "_rare" : "";
                var modelPath = modelFolder + stem + suffix + ".gfbmdl";
                var selected = PaletteMembers(members, modelPath);
                if (rare && gem is not null)
                    foreach (var path in selected.Keys.Where(p => p.EndsWith(".gfbanm", StringComparison.Ordinal)).ToArray())
                        selected[path] = ZaSwShFresnelAnimation.PaletteDefaults(selected[path], gem.Defaults, rareGem!.Defaults, originalMaterialNames);
                if (ZaSwShStaticSourcePalette.Supports(species, form))
                {
                    var originalNames = originalMaterialNames;
                    foreach (var path in selected.Keys.Where(path => path.EndsWith(".gfbanm", StringComparison.Ordinal)).ToArray())
                        selected[path] = ZaSwShStaticSourcePalette.Animation(selected[path], members[modelFolder + stem + ".gfbmdl"],
                            selected[modelPath], originalNames, species, form);
                    SwShRestorationResourceBudget.Validate(selected);
                }
                result.Add(File($"bin/archive/pokemon/{stem}{suffix}.gfpak", SwShGfPackFile.CreateWithPaths(selected).Write()));
            }
            shaderNames.IntersectWith(new[] { 3, 5 }.SelectMany(f => ModelNames(new(members[modelFolder + stem + ".gfbmdl"]), f)));
            var giantDonor = SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0007_00.gfpak"));
            var giantFire = profile.PairedFlames ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0004_00.gfpak")) : null;
            var giantSmoke = smoke is not null ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0324_00.gfpak")) : null;
            var giantEye = profile.LayeredEyes ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0001_00.gfpak")) : null;
            var giantTransparent = transparentPack is not null ? SwShGfPackFile.Parse(Native("bin/archive/gpokemon/pm0577_00.gfpak")) : null;
            var giantSize = SwShGfPackFile.Parse(Native($"bin/archive/gpokemon/{sizeStem}.gfpak"));
            var giantConfiguration = Member(giantSize, sizeStem + ".gfbgpokecfg");
            if (profile.MatchNativeGiantHeight)
                SwShRestorationFraming.MatchGiantHeight(giantConfiguration, configuration, profile.BattleHeight);
            else if (profile.PreserveGiantHeightFrom is { } previousBattleHeight)
                SwShRestorationFraming.MatchGiantHeight(giantConfiguration, previousBattleHeight, profile.BattleHeight);
            if (profile.AirborneBattle)
                SwShRestorationFraming.MatchGiantGround(giantConfiguration, configuration,
                    members[$"bin/pokemon/{stem}/{stem}.gfbpokecfg"]);
            if (species == 15)
            {
                // The source idle already hovers above its origin. Butterfree's
                // negative giant offset would bury Beedrill below the arena.
                var giant = new ModelBuffer(giantConfiguration);
                var offset = giant.Field(giant.Root, 8);
                if (offset == 0) throw new InvalidDataException("Native giant flight configuration is incomplete.");
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(offset, 4), 0);
            }
            var giantMembers = new Dictionary<string, byte[]> { [$"bin/pokemon/{stem}/{stem}.gfbgpokecfg"] = giantConfiguration };
            foreach (var name in shaderNames) foreach (var extension in new[] { ".bnsh_vsh", ".bnsh_fsh" })
                giantMembers[$"bin/pokemon/{stem}/g_shader/{name}{extension}"] = ZaSwShLayerSurface.Recognizes(name)
                    ? extension == ".bnsh_fsh" ? SwShZaLayerSurfaceShader.Fragment(Member(giantDonor, "Body00" + extension), name)
                        : SwShZaLayerSurfaceShader.Vertex(Member(giantDonor, "Body00" + extension), name)
                    : ZaSwShDisplacedUnlit.Recognizes(name)
                    ? extension == ".bnsh_fsh" ? SwShZaDisplacedUnlitShader.Fragment(Member(giantDonor, "Body00" + extension), name)
                        : SwShZaDisplacedUnlitShader.Vertex(Member(giantDonor, "Body00" + extension), name)
                    : SwShZaTangentBodyShader.Recognizes(name)
                    ? extension == ".bnsh_fsh" ? SwShZaTangentBodyShader.Fragment(Member(giantDonor, "Body00" + extension), name)
                        : SwShZaTangentBodyShader.Vertex(Member(giantDonor, "Body00" + extension))
                    : gemPrograms?.TryGetValue(name, out var gemProgram) == true
                    ? FresnelShader(Member(giantDonor, "Body00" + extension), extension, gemProgram.Settings, gemProgram.Width)
                    : name == SvSwShLayerEmission.FresnelMirrorProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.FresnelMirrorOnce(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : name == SvSwShLayerEmission.FresnelProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Fresnel(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : name == SvSwShSurfaceFlow.SourceLayerProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceSurfaceLayers(Member(nativePack, "Body00" + extension))
                        : SvSwShSurfaceLayerVertexShader.Convert(Member(nativePack, "Body00" + extension))
                    : profile.LayerEmission && name == SvSwShLayerEmission.SourceOcclusionProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceOcclusion(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.ProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Convert(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.FiveLayerProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.GroudonFiveLayers(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : chandelureEmission && Array.IndexOf(SvSwShLayerEmission.TransparentPrograms, name) is var giantEmissionPass && giantEmissionPass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Convert(Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantEmissionPass] + extension)) : Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantEmissionPass] + extension)
                    : primalGroudon && Array.IndexOf(SvSwShLayerEmission.TransparentBasePrograms, name) is var giantLayerPass && giantLayerPass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.TransparentLayerBase(Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantLayerPass] + extension)) : Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantLayerPass] + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.MirroredProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.MirroredMask(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : sourceTransparentEmission && Array.IndexOf(SvSwShSourceBase.TransparentPrograms, name) is var giantSourcePass && giantSourcePass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.TransparentSourceBase(Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantSourcePass] + extension)) : Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantSourcePass] + extension)
                    : sourceTransparentEmission && Array.IndexOf(SvSwShSourceBase.DynamicTransparentPrograms, name) is var giantDynamicPass && giantDynamicPass >= 0
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.DynamicSourceBase(Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantDynamicPass] + extension), true) : Member(giantTransparent!, SvSwShTransparentMaterial.NativePrograms[giantDynamicPass] + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.DynamicProgram
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.DynamicSourceBase(Member(giantDonor, "Body00" + extension), false) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.MirroredProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.MirroredSourceBase(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.SharedUvProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SharedUvSourceBase(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.MatteMirrorProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.MatteMirroredSourceBase(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.ProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceLayerBase(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShSourceBase.UnlitProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.SourceUnlitBase(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.BaseProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.LayerBase(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.UnlitBaseProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.UnlitLayerBase(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.MirroredProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.MirroredMask(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.ThreeLayerProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.ThreeLayer(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayerEmission && name == SvSwShLayerEmission.AdditiveProgramName
                    ? extension == ".bnsh_fsh" ? SvSwShLayerEmissionShader.Additive(Member(giantDonor, "Body00" + extension)) : Member(giantDonor, "Body00" + extension)
                    : profile.LayeredEyes && name == eyeProgram
                    ? extension == ".bnsh_fsh" ? ZaSwShEyeShader.Convert(Member(giantEye!, ZaSwShEyelidMaterial.NativeProgramName + extension), independentHighlights: profile.IndependentEyeHighlights, layerEmission: profile.LayeredEyeEmission, layerTint: profile.LayeredEyeTint, mirrorEmissionColor: eyeContracts?.Values.Any(c => c.MirroredColor) == true)
                        : ZaSwShEyeShader.Vertex(Member(giantEye!, ZaSwShEyelidMaterial.NativeProgramName + extension))
                    : giantTransparent is not null && Array.IndexOf(SvSwShTransparentMaterial.Programs, name) is var pass && pass >= 0
                    ? Member(giantTransparent, SvSwShTransparentMaterial.NativePrograms[pass] + extension)
                    : giantDonor.TryGetFileByName(name + extension, 32 * 1024 * 1024, out var shader)
                    ? shader : Member(giantSmoke is not null && smoke!.ShaderNames.Contains(name, StringComparer.Ordinal) ? giantSmoke : giantFire ?? giantDonor, name + extension);
            result.Add(File($"bin/archive/gpokemon/{stem}.gfpak", SwShGfPackFile.CreateWithPaths(giantMembers).Write()));
            foreach (var shiny in new[] { false, true })
            {
                var identity = new SwShPokemonResourceIdentity(species, form, gender, shiny);
                var row = new SwShPokemonResourceRow(identity, modelFolder + stem + (shiny ? "_rare" : "") + ".gfbmdl",
                    $"bin/pokemon/{stem}/{stem}.gfbpokecfg", $"bin/archive/pokemon/{stem}{(shiny ? "_rare" : "")}.gfpak",
                    groups.ToDictionary(g => g, g => animationFolder + stem + "_" + g + ".gfbanmcfg", StringComparer.Ordinal));
                result.Add(new(SwShRestorationComposer.CatalogPath, SwShRestorationWriter.ModelCatalog, SwShRestorationComposer.CatalogKey(identity), null, JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            return result;
        }

        private static bool HasQualifiedBaseCampRig(int species) => species is
            154 or 160 or 227 or 248 or 254 or 257 or 260 or 282 or 302 or 306 or 308 or 359 or 428 or 448 or 475 or 478 or 531 or 560 or 623 or 652 or 691 or 701;

        private static bool HasQualifiedBaseCampForm(int species, int form) =>
            ExtendedMegaBaseCampAnatomy(species, form) is not null ||
            form == 1 && HasQualifiedBaseCampRig(species)
            || (species, form) is (3, 1) or (9, 1) or (15, 1) or (18, 1) or (26, 3) or (80, 1) or (115, 1) or (127, 1) or (130, 1) or (150, 1) or (150, 2) or (181, 1) or (212, 1) or (214, 1) or (229, 1) or (303, 1) or (310, 1) or (319, 1) or (334, 1)
                or (354, 1) or (359, 2) or (362, 1) or (373, 1) or (376, 1) or (380, 1) or (381, 1) or (382, 1) or (383, 1) or (398, 1) or (445, 1) or (445, 2) or (448, 2) or (545, 1)
                or (658, 3) or (668, 1) or (670, 6) or (678, 2) or (678, 3) or (740, 1) or (768, 1) or (780, 1) or (870, 1) or (952, 1) or (998, 1);

        private static string BaseCampResourceStem(string resourceStem, int species, int form) =>
            // Follow the catalog's base-form identity, including Greninja's
            // numbered donor, Eternal Flower Floette and female Meowstic.
            (species, form) switch
            {
                (658, 3) => "pm0725_11_00",
                (670, 6) => "pm0714_16_00",
                (678, 3) => "pm0734_01_00",
                (718, 5) => "pm0770_13_00",
                (801, 2) => "pm0882_11_00",
                (801, 3) => "pm0882_12_00",
                _ => resourceStem[..6] + "_00_00"
            };

        private static string[] AdditionalBaseCampAnatomy(int species, int form) => (species, form) switch
        {
            (26, 3) => ["tail_01", "tail_02", "tail_03", "tail_04", "tail_05", "tail_06", "tail_07", "tail_08", "tail_09"],
            (150, 1) => ["spine", "left_upper_eyelid_a_01", "left_upper_eyelid_b_01", "left_upper_eyelid_c_01",
                "right_upper_eyelid_a_01", "right_upper_eyelid_b_01", "right_upper_eyelid_c_01", "left_foot_thumb_01", "right_foot_thumb_01"],
            (150, 2) => ["spine_01", "spine_02", "left_upper_eyelid_a", "left_upper_eyelid_b", "left_upper_eyelid_c",
                "left_upper_eyelid_d_01", "left_upper_eyelid_d_02", "right_upper_eyelid_a", "right_upper_eyelid_b", "right_upper_eyelid_c",
                "right_upper_eyelid_d_01", "right_upper_eyelid_d_02", "left_foot_thumb", "right_foot_thumb",
                "tail_06", "tail_07", "tail_08", "tail_09", "tail_10"],
            (359, 2) => ["neck_01", "neck_02", "tail"],
            (448, 2) => ["neck", "tail_01", "tail_02", "tail_03", "tail_04"],
            (545, 1) => ["spine_03", "neck_01", "neck_02", "neck_03", "tail"],
            (668, 1) => ["neck_01", "neck_02", "tail_a_01", "tail_a_02", "tail_a_03", "tail_a_04", "tail_a_05", "tail_a_06", "tail_a_07"],
            (678, 2) => ["left_upper_jaw_a", "left_upper_jaw_b", "right_upper_jaw_a", "right_upper_jaw_b"],
            (678, 3) => ["left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "right_upper_eyelid_c_01", "right_upper_eyelid_c_02"],
            _ => []
        };

        private static string[] BaseCampAnatomy(int species) => species switch
        {
            // Primal Groudon retains the body, digits, legs and tail. Its extra
            // shoulder, middle neck, distal thumbs and eyeball controls keep bind.
            383 => ["origin", "waist", "spine_01", "spine_02", "neck_01", "neck_03", "head", "jaw", "hips",
                "left_arm", "left_hand", "left_thumb_01", "left_index", "left_middle", "left_ring",
                "right_arm", "right_hand", "right_thumb_01", "right_index", "right_middle", "right_ring",
                "left_crotch", "left_leg_01", "left_leg_02", "left_foot", "left_foot_index", "left_foot_middle", "left_foot_ring",
                "right_crotch", "right_leg_01", "right_leg_02", "right_foot", "right_foot_index", "right_foot_middle", "right_foot_ring",
                "tail_01", "tail_02", "tail_03", "tail_04", "tail_05",
                "left_feeler_a", "right_feeler_a", "left_feeler_b", "right_feeler_b", "left_feeler_c", "right_feeler_c",
                "left_feeler_d", "right_feeler_d", "left_feeler_e", "right_feeler_e", "left_feeler_f", "right_feeler_f",
                "left_feeler_g", "right_feeler_g", "left_feeler_h", "right_feeler_h"],
            870 => FalinksBaseCampAnatomy(),
            // Mega Banette keeps the complete shared face, digits, limbs and
            // hair chains. Added suit feelers and terminal joints retain bind.
            354 => ["origin", "waist", "spine", "neck", "head", "jaw",
                "hair_a_01", "hair_a_02", "hair_a_03", "hair_a_04", "hair_a_05", "hair_a_06", "hair_b", "left_hair", "right_hair",
                "left_upper_eyelid_a", "left_upper_eyelid_b", "left_upper_eyelid_c", "left_upper_eyelid_d", "left_upper_eyelid_d_sub",
                "right_upper_eyelid_a", "right_upper_eyelid_b", "right_upper_eyelid_c", "right_upper_eyelid_d", "right_upper_eyelid_d_sub",
                "left_arm_01", "left_arm_02", "left_hand_01", "left_index", "left_middle", "left_ring",
                "right_arm_01", "right_arm_02", "right_hand_01", "right_index", "right_middle", "right_ring",
                "hips_01", "left_leg_01", "left_leg_02", "left_foot", "right_leg_01", "right_leg_02", "right_foot"],
            3 or 130 or 212 or 460 => ClassicMegaBaseCampAnatomy(species),
            18 => PidgeotBaseCampAnatomy(),
            303 => MawileBaseCampAnatomy(),
            398 => StaraptorBaseCampAnatomy(),
            768 => GolisopodBaseCampAnatomy(),
            780 => DrampaBaseCampAnatomy(),
            998 => BaxcaliburBaseCampAnatomy(),
            380 or 381 => LatiasLatiosBaseCampAnatomy(species),
            // Sharpedo's original ventral jaw fin and dorsal fin become the
            // b and c chains. Added snout, side spikes and tail retain bind.
            319 => ["origin", "waist", "jaw_01", "jaw_02", "left_eyeball", "right_eyeball",
                "left_arm", "left_hand", "right_arm", "right_hand", "feeler_b_01", "feeler_b_02", "feeler_c_01", "feeler_c_02"],
            // Metagross retains the four original limb chains as the Mega's
            // a branches. Its added b limbs, distal claws and eye root keep bind.
            376 => ["origin", "waist", "jaw", "left_eyeball", "right_eyeball",
                "left_arm_a_01", "left_arm_a_02", "left_hand_a", "left_finger_a_01", "left_finger_b_01", "left_finger_c_01",
                "right_arm_a_01", "right_arm_a_02", "right_hand_a", "right_finger_a_01", "right_finger_b_01", "right_finger_c_01",
                "left_leg_a_01", "left_leg_a_02", "left_foot_a", "left_foot_finger_a", "left_foot_finger_b", "left_foot_finger_c",
                "right_leg_a_01", "right_leg_a_02", "right_foot_a", "right_foot_finger_a", "right_foot_finger_b", "right_foot_finger_c"],
            6 => ["origin", "waist", "spine_01", "spine_02", "neck_01", "head", "jaw", "hips", "left_leg_01", "right_leg_01", "tail_01"],
            // Shared limb, digit and tail chains retain their full donor motion.
            // Added Mega cannons, wings and neck/tail extensions retain bind.
            9 => ["origin", "waist", "neck", "head", "jaw", "tail_01", "tail_02",
                "left_arm_01", "left_arm_02", "left_hand", "left_thumb", "left_index", "left_middle",
                "right_arm_01", "right_arm_02", "right_hand", "right_thumb", "right_index", "right_middle",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_thumb", "left_foot_index", "left_foot_middle", "left_foot_ring",
                "right_leg_01", "right_leg_02", "right_foot", "right_foot_thumb", "right_foot_index", "right_foot_middle", "right_foot_ring"],
            // Mother and child retain both complete shared rigs. The Mega-only
            // child digits and final tail segment have no base motion and retain bind.
            115 => ["origin", "waist", "waist_a", "spine_a", "neck_a", "head_a", "jaw_a", "hips_a",
                "left_upper_eyelid_a", "right_upper_eyelid_a", "left_upper_eyelid_b", "right_upper_eyelid_b",
                "left_lower_eyelid_a", "right_lower_eyelid_a", "left_lower_eyelid_b", "right_lower_eyelid_b", "left_lower_eyelid_c", "right_lower_eyelid_c",
                "left_ear_a", "right_ear_a", "left_shoulder_a", "left_arm_a_01", "left_arm_a_02", "left_hand_a",
                "left_index_a_01", "left_index_a_02", "left_middle_a_01", "left_middle_a_02", "left_ring_a_01", "left_ring_a_02",
                "left_arm_a_02_sub", "left_arm_a_02_roll", "left_shoulder_a_sub", "right_shoulder_a", "right_arm_a_01", "right_arm_a_02", "right_hand_a",
                "right_index_a_01", "right_index_a_02", "right_middle_a_01", "right_middle_a_02", "right_ring_a_01", "right_ring_a_02",
                "right_arm_a_02_sub", "right_arm_a_02_roll", "right_shoulder_a_sub", "left_leg_a_01", "left_leg_a_02", "left_foot_a",
                "left_foot_index_a", "left_foot_middle_a", "left_foot_ring_a", "left_leg_a_01_sub", "left_leg_a_02_sub",
                "right_leg_a_01", "right_leg_a_02", "right_foot_a", "right_foot_index_a", "right_foot_middle_a", "right_foot_ring_a", "right_leg_a_01_sub", "right_leg_a_02_sub",
                "tail_a_01", "tail_a_02", "tail_a_03", "tail_a_04", "feeler_a_01", "feeler_a_02", "waist_b", "spine_b", "head_b", "lower_jaw_b",
                "left_ear_b", "right_ear_b", "left_upper_eyelid_c", "right_upper_eyelid_c", "left_upper_eyelid_d", "right_upper_eyelid_d",
                "left_upper_eyelid_e", "right_upper_eyelid_e", "left_lower_eyelid_d", "right_lower_eyelid_d",
                "left_shoulder_b", "left_arm_b_01", "left_arm_b_02", "left_hand_b", "right_shoulder_b", "right_arm_b_01", "right_arm_b_02", "right_hand_b",
                "hips_b", "tail_b_01", "tail_b_02", "left_leg_b_01", "left_leg_b_02", "left_foot_b", "left_toe_b",
                "right_leg_b_01", "right_leg_b_02", "right_foot_b", "right_toe_b"],
            15 => ["origin", "waist", "head", "tail_01", "tail_02", "tail_03",
                "left_arm_01", "left_arm_02", "left_hand", "right_arm_01", "right_arm_02", "right_hand",
                "left_leg_01", "left_leg_02", "left_foot", "right_leg_01", "right_leg_02", "right_foot",
                "left_wing_a", "left_wing_b", "right_wing_a", "right_wing_b"],
            // Slowbro's shorter arm chains retain the source motion through
            // each hand. Its added spine and shell feelers retain bind. The
            // shell's eyelids move from the tail branch to the upper spine.
            80 => ["origin", "waist", "spine_01", "head", "jaw", "left_ear", "right_ear", "hips", "tail_01", "tail_02", "feeler_a", "feeler_b",
                "left_upper_eyelid_a", "left_upper_eyelid_a_sub", "left_upper_eyelid_b", "left_upper_eyelid_b_sub",
                "left_upper_eyelid_c", "left_upper_eyelid_c_sub", "left_upper_eyelid_d", "left_upper_eyelid_d_sub",
                "left_upper_eyelid_e", "left_upper_eyelid_e_sub", "left_upper_eyelid_f", "left_upper_eyelid_f_sub",
                "left_upper_eyelid_g", "left_upper_eyelid_g_sub", "left_upper_eyelid_h", "left_upper_eyelid_h_sub",
                "right_upper_eyelid_a", "right_upper_eyelid_a_sub", "right_upper_eyelid_b", "right_upper_eyelid_b_sub",
                "right_upper_eyelid_c", "right_upper_eyelid_c_sub", "right_upper_eyelid_d", "right_upper_eyelid_d_sub",
                "right_upper_eyelid_e", "right_upper_eyelid_e_sub", "right_upper_eyelid_f", "right_upper_eyelid_f_sub",
                "right_upper_eyelid_g", "right_upper_eyelid_g_sub", "right_upper_eyelid_h", "right_upper_eyelid_h_sub",
                "left_lower_eyelid_a", "left_lower_eyelid_b", "right_lower_eyelid_a", "right_lower_eyelid_b",
                "left_arm", "left_hand", "left_index", "left_middle", "left_ring",
                "right_arm", "right_hand", "right_index", "right_middle", "right_ring"],
            127 => ["origin", "waist", "spine", "head", "left_horn", "right_horn", "root_jaw", "hips",
                "left_teeth_a", "left_teeth_b", "left_teeth_c", "left_teeth_d", "left_teeth_e",
                "right_teeth_a", "right_teeth_b", "right_teeth_c", "right_teeth_d", "right_teeth_e",
                "left_arm_01", "left_arm_02", "left_hand", "left_thumb_01", "left_thumb_02", "left_index_01", "left_index_02", "left_middle_01", "left_middle_02",
                "right_arm_01", "right_arm_02", "right_hand", "right_thumb_01", "right_thumb_02", "right_index_01", "right_index_02", "right_middle_01", "right_middle_02",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_middle", "left_foot_index", "left_foot_thumb",
                "right_leg_01", "right_leg_02", "right_foot", "right_foot_middle", "right_foot_index", "right_foot_thumb"],
            // Both Mega Mewtwo rigs share the body and first tail segments.
            // Their distinct spine, eyelid and toe chains are listed per form;
            // new feelers and secondary branches retain bind.
            150 => ["origin", "waist", "neck", "head", "jaw", "left_lower_eyelid_a", "right_lower_eyelid_a",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "left_thumb_01", "left_thumb_02",
                "left_middle_01", "left_middle_02", "left_ring_01", "left_ring_02",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "right_thumb_01", "right_thumb_02",
                "right_middle_01", "right_middle_02", "right_ring_01", "right_ring_02",
                "hips", "left_crotch", "left_leg_01", "left_leg_02", "left_foot", "left_foot_index_01",
                "right_crotch", "right_leg_01", "right_leg_02", "right_foot", "right_foot_index_01",
                "tail_01", "tail_02", "tail_03", "tail_04", "tail_05"],
            // Mega Heracross retains these facial and limb branches. Its new
            // shoulders, terminal finger segments and third fingers retain bind;
            // arm roots compensate for the changed head attachment below.
            214 => ["origin", "waist", "head", "jaw", "left_feeler_a_01", "left_feeler_a_02", "right_feeler_a_01", "right_feeler_a_02", "hips",
                "left_upper_eyelid_a", "left_upper_eyelid_a_sub", "left_upper_eyelid_b",
                "left_upper_eyelid_c", "left_upper_eyelid_c_sub", "left_upper_eyelid_d", "left_upper_eyelid_d_sub",
                "left_upper_eyelid_e", "left_upper_eyelid_e_sub", "left_upper_eyelid_f", "left_upper_eyelid_f_sub",
                "left_upper_eyelid_g", "left_upper_eyelid_g_sub", "left_upper_eyelid_h", "left_upper_eyelid_h_sub",
                "right_upper_eyelid_a", "right_upper_eyelid_a_sub", "right_upper_eyelid_b",
                "right_upper_eyelid_c", "right_upper_eyelid_c_sub", "right_upper_eyelid_d", "right_upper_eyelid_d_sub",
                "right_upper_eyelid_e", "right_upper_eyelid_e_sub", "right_upper_eyelid_f", "right_upper_eyelid_f_sub",
                "right_upper_eyelid_g", "right_upper_eyelid_g_sub", "right_upper_eyelid_h", "right_upper_eyelid_h_sub",
                "left_lower_eyelid_a", "left_lower_eyelid_a_sub", "left_lower_eyelid_b", "left_lower_eyelid_c",
                "right_lower_eyelid_a", "right_lower_eyelid_a_sub", "right_lower_eyelid_b", "right_lower_eyelid_c",
                "left_arm_01", "left_arm_02", "left_hand", "left_index_01", "left_middle_01",
                "right_arm_01", "right_arm_02", "right_hand", "right_index_01", "right_middle_01",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_index", "right_leg_01", "right_leg_02", "right_foot", "right_foot_index"],
            // Mega Ampharos shares these complete body and facial branches.
            // Its added mane, upper eyelid and terminal tail joints retain bind.
            181 => ["origin", "waist", "spine_01", "spine_02", "neck_01", "neck_02", "head", "upper_jaw", "lower_jaw", "hips",
                "left_upper_eyelid_a", "left_upper_eyelid_a_sub", "left_upper_eyelid_b", "left_upper_eyelid_b_sub", "left_upper_eyelid_c",
                "left_upper_eyelid_d", "left_upper_eyelid_d_sub", "left_upper_eyelid_e", "left_upper_eyelid_f", "left_upper_eyelid_f_sub",
                "left_upper_eyelid_g", "left_upper_eyelid_g_sub", "left_upper_eyelid_h", "left_upper_eyelid_h_sub", "left_upper_eyelid_i", "left_upper_eyelid_i_sub", "left_upper_eyelid_j",
                "right_upper_eyelid_a", "right_upper_eyelid_a_sub", "right_upper_eyelid_b", "right_upper_eyelid_b_sub", "right_upper_eyelid_c",
                "right_upper_eyelid_d", "right_upper_eyelid_d_sub", "right_upper_eyelid_e", "right_upper_eyelid_f", "right_upper_eyelid_f_sub",
                "right_upper_eyelid_g", "right_upper_eyelid_g_sub", "right_upper_eyelid_h", "right_upper_eyelid_h_sub", "right_upper_eyelid_i", "right_upper_eyelid_i_sub", "right_upper_eyelid_j",
                "left_lower_eyelid_a", "left_lower_eyelid_a_sub", "left_lower_eyelid_b", "left_lower_eyelid_b_sub", "left_lower_eyelid_c", "left_lower_eyelid_c_sub",
                "left_lower_eyelid_d", "left_lower_eyelid_d_sub", "left_lower_eyelid_e", "left_lower_eyelid_e_sub",
                "right_lower_eyelid_a", "right_lower_eyelid_a_sub", "right_lower_eyelid_b", "right_lower_eyelid_b_sub", "right_lower_eyelid_c", "right_lower_eyelid_c_sub",
                "right_lower_eyelid_d", "right_lower_eyelid_d_sub", "right_lower_eyelid_e", "right_lower_eyelid_e_sub",
                "left_upper_jaw_a", "left_upper_jaw_b", "left_upper_jaw_c", "left_upper_jaw_d", "left_upper_jaw_e",
                "right_upper_jaw_a", "right_upper_jaw_b", "right_upper_jaw_c", "right_upper_jaw_d", "right_upper_jaw_e",
                "left_lower_jaw_a", "left_lower_jaw_b", "left_lower_jaw_c", "left_lower_jaw_d",
                "right_lower_jaw_a", "right_lower_jaw_b", "right_lower_jaw_c", "right_lower_jaw_d",
                "left_shoulder", "left_arm_01", "left_arm_02", "right_shoulder", "right_arm_01", "right_arm_02",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_index", "right_leg_01", "right_leg_02", "right_foot", "right_foot_index",
                "tail_01", "tail_02", "tail_03", "tail_04", "tail_05"],
            229 => ["origin", "waist", "spine_01", "spine_02", "neck_01", "neck_02", "head", "jaw", "tongue_01", "tongue_02", "tongue_03", "hips",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "left_index",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "right_index",
                "left_leg_01", "left_leg_02", "left_foot", "left_toe", "left_foot_index",
                "right_leg_01", "right_leg_02", "right_foot", "right_toe", "right_foot_index",
                "tail_01", "tail_02", "tail_03", "tail_04"],
            // Manectric retains its body, facial and three-joint hind toe
            // branches. Added feelers and lower eyelid branches retain bind.
            310 => ["origin", "waist", "spine", "neck", "head", "jaw", "hips", "tail",
                "left_upper_eyelid_a_01", "left_upper_eyelid_a_02", "left_upper_eyelid_b_01", "left_upper_eyelid_b_02",
                "left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "left_upper_eyelid_d_01", "left_upper_eyelid_d_02",
                "left_upper_eyelid_e_01", "left_upper_eyelid_e_02", "left_upper_eyelid_f_01", "left_upper_eyelid_f_02",
                "left_upper_eyelid_g_01", "left_upper_eyelid_g_02", "left_upper_eyelid_h_01", "left_upper_eyelid_h_02",
                "left_upper_eyelid_i_01", "left_upper_eyelid_i_02", "left_upper_eyelid_j", "left_upper_eyelid_k_01", "left_upper_eyelid_k_02",
                "right_upper_eyelid_a_01", "right_upper_eyelid_a_02", "right_upper_eyelid_b_01", "right_upper_eyelid_b_02",
                "right_upper_eyelid_c_01", "right_upper_eyelid_c_02", "right_upper_eyelid_d_01", "right_upper_eyelid_d_02",
                "right_upper_eyelid_e_01", "right_upper_eyelid_e_02", "right_upper_eyelid_f_01", "right_upper_eyelid_f_02",
                "right_upper_eyelid_g_01", "right_upper_eyelid_g_02", "right_upper_eyelid_h_01", "right_upper_eyelid_h_02",
                "right_upper_eyelid_i_01", "right_upper_eyelid_i_02", "right_upper_eyelid_j", "right_upper_eyelid_k_01", "right_upper_eyelid_k_02",
                "left_lower_eyelid_a_01", "left_lower_eyelid_a_02", "left_lower_eyelid_b_01", "left_lower_eyelid_b_02",
                "left_lower_eyelid_c_01", "left_lower_eyelid_c_02", "left_lower_eyelid_d_01", "left_lower_eyelid_d_02",
                "right_lower_eyelid_a_01", "right_lower_eyelid_a_02", "right_lower_eyelid_b_01", "right_lower_eyelid_b_02",
                "right_lower_eyelid_c_01", "right_lower_eyelid_c_02", "right_lower_eyelid_d_01", "right_lower_eyelid_d_02",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "left_index",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "right_index",
                "left_leg_01", "left_leg_02", "left_foot", "left_toe_01", "left_toe_02", "left_foot_index",
                "right_leg_01", "right_leg_02", "right_foot", "right_toe_01", "right_toe_02", "right_foot_index"],
            // Altaria keeps the four-joint wing and toe chains. Its extra
            // spine, cloud, hair and terminal tail branches retain bind.
            334 => ["origin", "waist", "spine_01", "neck_01", "neck_02", "neck_03", "head", "upper_jaw", "lower_jaw", "hips",
                "left_upper_eyelid_a", "right_upper_eyelid_a", "left_upper_eyelid_b_01", "right_upper_eyelid_b_01",
                "left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "right_upper_eyelid_c_01", "right_upper_eyelid_c_02",
                "left_upper_eyelid_d_01", "left_upper_eyelid_d_02", "right_upper_eyelid_d_01", "right_upper_eyelid_d_02",
                "left_upper_eyelid_e", "right_upper_eyelid_e", "left_lower_eyelid_a", "right_lower_eyelid_a",
                "left_lower_eyelid_b", "right_lower_eyelid_b", "left_lower_eyelid_c", "right_lower_eyelid_c",
                "left_shoulder", "right_shoulder", "left_wing_01", "left_wing_02", "left_wing_03", "left_wing_04",
                "right_wing_01", "right_wing_02", "right_wing_03", "right_wing_04", "tail_01", "tail_02", "tail_03", "tail_04",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_index_01", "left_foot_index_02",
                "left_foot_middle_01", "left_foot_middle_02", "left_foot_ring_01", "left_foot_ring_02", "left_foot_thumb",
                "right_leg_01", "right_leg_02", "right_foot", "right_foot_index_01", "right_foot_index_02",
                "right_foot_middle_01", "right_foot_middle_02", "right_foot_ring_01", "right_foot_ring_02", "right_foot_thumb"],
            // Glalie shares its floating body and these complete eyelid
            // branches. Its added Mega jaw and longer eyelids retain bind.
            362 => ["origin", "waist",
                "left_upper_eyelid_a", "left_upper_eyelid_a_sub", "left_upper_eyelid_b", "left_upper_eyelid_b_sub",
                "left_upper_eyelid_c", "left_upper_eyelid_c_sub", "left_upper_eyelid_d", "left_upper_eyelid_d_sub",
                "left_upper_eyelid_e", "left_upper_eyelid_e_sub", "left_upper_eyelid_f", "left_upper_eyelid_f_sub",
                "left_upper_eyelid_g", "left_upper_eyelid_g_sub",
                "left_lower_eyelid_a", "left_lower_eyelid_b", "left_lower_eyelid_c", "left_lower_eyelid_d",
                "left_lower_eyelid_e", "left_lower_eyelid_f", "left_lower_eyelid_g", "left_lower_eyelid_h", "left_lower_eyelid_i",
                "right_upper_eyelid_a", "right_upper_eyelid_a_sub", "right_upper_eyelid_b", "right_upper_eyelid_b_sub",
                "right_upper_eyelid_c", "right_upper_eyelid_c_sub", "right_upper_eyelid_d", "right_upper_eyelid_d_sub",
                "right_upper_eyelid_e", "right_upper_eyelid_e_sub", "right_upper_eyelid_f", "right_upper_eyelid_f_sub",
                "right_upper_eyelid_g", "right_upper_eyelid_g_sub",
                "right_lower_eyelid_a", "right_lower_eyelid_b", "right_lower_eyelid_c", "right_lower_eyelid_d",
                "right_lower_eyelid_e", "right_lower_eyelid_f", "right_lower_eyelid_g", "right_lower_eyelid_h", "right_lower_eyelid_i"],
            // Greninja retains the donor's jaw, head feelers and paired toe
            // chains. Its inserted waist and added fingers keep authored bind.
            658 => ["origin", "waist", "spine_01", "spine_02", "neck", "head", "jaw_01", "jaw_02",
                "root_feeler", "left_feeler", "right_feeler", "front_feeler", "back_feeler", "hips",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand",
                "left_leg_01", "left_leg_02", "left_foot", "left_feeler_a", "left_feeler_b",
                "left_foot_index_01", "left_foot_index_02", "left_foot_middle_01", "left_foot_middle_02",
                "right_leg_01", "right_leg_02", "right_foot", "right_feeler_a", "right_feeler_b",
                "right_foot_index_01", "right_foot_index_02", "right_foot_middle_01", "right_foot_middle_02"],
            // Both Meowstic genders retain these jaw branches and five joints
            // in each tail. Gender-specific facial chains are required above;
            // the remaining added neck, ear, facial and tail tips keep bind.
            678 => ["origin", "waist", "spine", "head", "upper_jaw", "lower_jaw", "feeler_a", "hips",
                "left_lower_jaw_a", "left_lower_jaw_b", "right_lower_jaw_a", "right_lower_jaw_b",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand",
                "left_leg_01", "left_leg_02", "left_foot", "right_leg_01", "right_leg_02", "right_foot",
                "tail_a_01", "tail_a_02", "tail_a_03", "tail_a_04", "tail_a_05",
                "tail_b_01", "tail_b_02", "tail_b_03", "tail_b_04", "tail_b_05"],
            // Both heads attach to the waist in the base rig and to the neck
            // tips in the Mega rig. Their transferred poses compensate for
            // those moving parents; added jaw, tongue and tail tips keep bind.
            952 => ["origin", "waist", "spine_01", "spine_02",
                "left_neck_01", "left_neck_02", "left_neck_03", "left_neck_04", "left_neck_05",
                "right_neck_01", "right_neck_02", "right_neck_03", "right_neck_04", "right_neck_05",
                "head_a", "head_b", "jaw_a_01", "jaw_a_02", "jaw_b_01", "jaw_b_02",
                "tongue_a_01", "tongue_a_02", "tongue_a_03", "tongue_a_04", "tongue_a_05",
                "tongue_b_01", "tongue_b_02", "tongue_b_03", "tongue_b_04", "tongue_b_05", "hips",
                "left_arm_01", "left_arm_02", "left_hand", "left_leg_01", "left_leg_02", "left_foot",
                "right_arm_01", "right_arm_02", "right_hand", "right_leg_01", "right_leg_02", "right_foot",
                "tail_01", "tail_02", "tail_03"],
            // Mega Salamence retains the base donor's four neck segments,
            // paired limbs, wing roots and complete tail. Its extra feelers,
            // fingers and back wing retain their authored bind transforms.
            373 => ["origin", "waist", "spine", "neck_01", "neck_02", "neck_03", "neck_04", "head", "jaw", "hips",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "left_index",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "right_index",
                "left_wing_01", "left_wing_02", "right_wing_01", "right_wing_02",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_index",
                "right_leg_01", "right_leg_02", "right_foot", "right_foot_index",
                "tail_01", "tail_02", "tail_03", "tail_04", "tail_05", "tail_06", "tail_07", "tail_08"],
            // Primal Kyogre retains the base donor's complete fin and tail
            // anatomy. Its additional streamer joints keep their authored bind
            // transforms while shared descendants follow the base camp motion.
            382 => ["origin", "waist", "spine", "neck", "head", "jaw", "hips",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "left_index", "left_middle", "left_ring", "left_pinky",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "right_index", "right_middle", "right_ring", "right_pinky",
                "tail_01", "tail_02", "tail_03",
                "left_feeler_b_01", "left_feeler_b_02", "left_feeler_b_03", "left_feeler_b_04", "left_feeler_c_01", "left_feeler_c_02",
                "right_feeler_b_01", "right_feeler_b_02", "right_feeler_b_03", "right_feeler_b_04", "right_feeler_c_01", "right_feeler_c_02"],
            445 => ["origin", "waist", "spine_01", "neck", "head", "jaw_a", "hips",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand",
                "left_leg_01", "left_leg_02", "left_foot", "right_leg_01", "right_leg_02", "right_foot",
                "tail_a_01", "tail_a_02", "tail_a_03", "tail_a_04", "tail_b", "tail_c"],
            227 => ["origin", "waist", "spine", "neck_01", "neck_02", "neck_03", "neck_04", "head", "jaw", "hips", "tail",
                "left_wing_a_01", "left_wing_a_02", "left_wing_a_03", "left_wing_b_01", "left_wing_c_01", "left_wing_d_01",
                "right_wing_a_01", "right_wing_a_02", "right_wing_a_03", "right_wing_b_01", "right_wing_c_01", "right_wing_d_01",
                "left_leg_01", "left_leg_02", "left_foot", "right_leg_01", "right_leg_02", "right_foot"],
            478 => ["origin", "waist", "spine_01", "neck", "head", "hips", "left_arm_01", "left_arm_02", "left_hand",
                "right_arm_01", "right_arm_02", "right_hand"],
            623 => ["origin", "waist", "head", "left_shoulder", "left_arm_01", "left_arm_02", "left_hand",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "left_leg_01", "left_leg_02", "left_foot",
                "right_leg_01", "right_leg_02", "right_foot"],
            670 => ["origin", "waist", "spine", "neck", "head", "hips",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand",
                "tail_01", "tail_02", "tail_03", "feeler_a",
                "left_feeler_a_01", "left_feeler_a_02", "left_feeler_a_03",
                "right_feeler_a_01", "right_feeler_a_02", "right_feeler_a_03"],
            // Mega Crabominable keeps both pairs of walking legs and the arm
            // chains of its base donor. Added hair and claw joints retain bind.
            740 => ["origin", "waist", "head", "jaw",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand",
                "left_leg_a_01", "left_leg_a_02", "left_foot_a", "right_leg_a_01", "right_leg_a_02", "right_foot_a",
                "left_leg_b_01", "left_leg_b_02", "left_foot_b", "right_leg_b_01", "right_leg_b_02", "right_foot_b"],
            691 => ["origin", "waist", "spine_01", "spine_02", "neck_01", "neck_02", "head", "hips",
                "left_arm_01", "left_arm_02", "left_arm_03", "left_hand", "right_arm_01", "right_arm_02", "right_arm_03", "right_hand",
                "tail_01", "tail_02", "tail_03", "tail_04", "tail_05", "tail_06"],
            _ => ["origin", "waist", "spine_01", "spine_02", "head", "hips", "left_arm_01", "left_arm_02", "left_hand",
                "right_arm_01", "right_arm_02", "right_hand", "left_leg_01", "left_leg_02", "left_foot", "right_leg_01", "right_leg_02", "right_foot"]
        };

        private static string[] ClassicMegaBaseCampAnatomy(int species)
        {
            // Require the complete shared body, limb and facial chains. Added
            // flower branches, fins, wing roots and distal jaws retain bind;
            // same-named branches with changed ancestry are not mapped.
            var result = new List<string>(species switch
            {
                3 => ["origin", "waist", "spine_01", "spine_02", "neck_01", "neck_02", "head", "jaw", "hips"],
                130 => ["origin", "waist", "spine_a_01", "spine_a_02", "spine_a_03", "spine_a_04", "head", "upper_jaw", "lower_jaw", "tongue_a_01", "tongue_a_02", "hips_a",
                    "tail_a_01", "tail_a_02", "tail_a_03", "tail_a_04", "tail_a_05", "tail_a_06", "tail_a_07", "tail_a_08"],
                212 => ["origin", "waist", "spine", "neck", "head", "upper_jaw", "lower_jaw", "hips"],
                460 => ["origin", "waist", "spine", "head", "jaw_a", "jaw_b", "hips", "tail_01", "tail_02", "tail_03"],
                _ => throw new InvalidDataException("Unqualified classic Mega anatomy")
            });
            foreach (var side in new[] { "left", "right" })
            {
                if (species != 130)
                {
                    foreach (var part in new[] { "arm_01", "arm_02", "hand", "leg_01", "leg_02", "foot" }) result.Add(side + "_" + part);
                    if (species != 460) result.Add(side + "_shoulder");
                }
                if (species == 3)
                {
                    foreach (var part in new[] { "ear", "index", "foot_index" }) result.Add(side + "_" + part);
                    foreach (var letter in new[] { "a", "b", "c", "d", "e", "f" }) result.Add(side + "_upper_eyelid_" + letter);
                    foreach (var letter in new[] { "a", "b", "c" }) result.Add(side + "_lower_eyelid_" + letter + "_01");
                }
                else if (species == 130)
                {
                    result.Add(side + "_ear_a_01"); result.Add(side + "_eyelid_a");
                    foreach (var number in new[] { "01", "02", "03", "04", "05", "06" }) result.Add(side + "_feeler_a_" + number);
                    foreach (var type in new[] { "upper", "lower" }) foreach (var letter in new[] { "a", "b", "c" }) foreach (var number in new[] { "01", "02" }) result.Add(side + "_" + type + "_eyelid_" + letter + "_" + number);
                    foreach (var letter in new[] { "b", "c" }) foreach (var number in new[] { "01", "02" }) result.Add(side + "_eyelid_" + letter + "_" + number);
                }
                else if (species == 212)
                {
                    result.Add(side + "_index");
                    foreach (var letter in new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" }) foreach (var suffix in new[] { "", "_sub" }) result.Add(side + "_upper_eyelid_" + letter + suffix);
                    foreach (var letter in new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" }) foreach (var suffix in new[] { "", "_sub" }) result.Add(side + "_lower_eyelid_" + letter + suffix);
                }
                else
                {
                    result.Add(side + "_lower_eyelid"); result.Add(side + "_upper_eyelid_a");
                    foreach (var letter in new[] { "b", "c" }) foreach (var number in new[] { "01", "02" }) result.Add(side + "_upper_eyelid_" + letter + "_" + number);
                    foreach (var letter in new[] { "a", "b", "c" }) result.Add(side + "_finger_" + letter);
                    foreach (var digit in new[] { "middle", "ring", "index" }) result.Add(side + "_foot_" + digit);
                }
            }
            return result.ToArray();
        }

        private static string[] DrampaBaseCampAnatomy() =>
            // The Mega retains five original neck segments, eyebrows and core
            // limb chains. Its second spine and added wing, hair, horn and
            // streamer branches retain their own authored bind transforms.
            ["origin", "waist", "spine_01", "neck_01", "neck_02", "neck_03", "neck_04", "neck_05", "head", "jaw", "tongue", "hips", "tail",
                "left_eyebrow_01", "left_eyebrow_02", "left_eyebrow_03", "left_eyebrow_04",
                "right_eyebrow_01", "right_eyebrow_02", "right_eyebrow_03", "right_eyebrow_04",
                "left_feeler_a_01", "left_feeler_a_02", "right_feeler_a_01", "right_feeler_a_02",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "right_shoulder", "right_arm_01", "right_arm_02", "right_hand"];

        private static string[] GolisopodBaseCampAnatomy()
        {
            var names = new List<string> { "origin", "waist", "spine_01", "spine_02", "spine_03", "head", "jaw", "hips", "feeler_a_01" };
            foreach (var side in new[] { "left", "right" })
            {
                foreach (var part in new[] { "feeler_a", "feeler_b", "upper_eyelid_01", "lower_eyelid_01", "arm_01", "arm_02", "index_01", "index_02", "middle_01", "middle_02" })
                    names.Add(side + "_" + part);
                foreach (var branch in new[] { "a", "b" })
                    foreach (var part in new[] { $"leg_{branch}_01", $"leg_{branch}_02", $"foot_{branch}", $"foot_index_{branch}" })
                        names.Add(side + "_" + part);
            }
            return names.ToArray();
        }

        private static string[] BaxcaliburBaseCampAnatomy()
        {
            var names = new List<string> { "origin", "waist", "spine_01", "spine_02", "neck_01", "neck_02", "head", "lower_jaw", "hips",
                "tail_01", "tail_02", "tail_03", "tail_04", "tail_05", "tail_06" };
            foreach (var side in new[] { "left", "right" })
                foreach (var part in new[] { "upper_eyelid_a", "upper_eyelid_b", "upper_eyelid_c", "lower_eyelid", "shoulder", "arm_01", "arm_02",
                    "hand", "index", "middle", "ring", "leg_01", "leg_02", "foot", "toe" })
                    names.Add(side + "_" + part);
            return names.ToArray();
        }

        private static string[] PidgeotBaseCampAnatomy()
        {
            var names = new List<string> { "origin", "waist", "spine_01", "spine_02", "neck", "head", "jaw", "hips", "tail_01", "tail_02" };
            foreach (var side in new[] { "left", "right" })
            {
                foreach (var part in new[] { "shoulder", "wing_a_01", "wing_a_02", "wing_a_03", "wing_i", "wing_j_01",
                    "upper_eyelid_a", "upper_eyelid_b", "upper_eyelid_c", "lower_eyelid", "leg_a", "foot_a", "leg_b_01", "leg_b_02", "foot_b" })
                    names.Add(side + "_" + part);
                foreach (var branch in new[] { "a", "b" })
                {
                    names.Add($"{side}_foot_thumb_{branch}_01");
                    foreach (var digit in new[] { "index", "middle", "ring" })
                        for (var i = 1; i <= 2; i++) names.Add($"{side}_foot_{digit}_{branch}_{i:00}");
                }
            }
            return names.ToArray();
        }

        private static string[] StaraptorBaseCampAnatomy()
        {
            var names = new List<string> { "origin", "waist", "spine_01", "neck", "head", "upper_jaw", "lower_jaw", "hips",
                "hair_a_01", "hair_a_02", "hair_a_03", "tail_01", "tail_02" };
            foreach (var side in new[] { "left", "right" })
            {
                foreach (var part in new[] { "ear", "shoulder", "arm_01", "arm_02", "hand", "wing_01", "wing_02", "crotch", "leg_01", "leg_02", "foot", "foot_thumb_01", "foot_thumb_02" })
                    names.Add(side + "_" + part);
                foreach (var part in new[] { "upper_eyelid_a", "upper_eyelid_b", "upper_eyelid_c", "lower_eyelid_a", "lower_eyelid_b" })
                    names.Add(side + "_" + part);
                foreach (var digit in new[] { "index", "middle", "ring" })
                    for (var i = 1; i <= 3; i++) names.Add($"{side}_foot_{digit}_{i:00}");
            }
            return names.ToArray();
        }

        private static string[] FalinksBaseCampAnatomy()
        {
            // Each of the six original units retains its own body and leg
            // motion. The commander's arms are now suffixed a. Its original
            // horn is absent; the new horns belong to other units and must
            // not inherit that branch. Mega-only spines, arms and feelers
            // retain their authored bind transforms.
            var names = new List<string> { "origin", "waist", "left_shoulder_a", "left_arm_a", "left_hand_a",
                "right_shoulder_a", "right_arm_a", "right_hand_a" };
            foreach (var unit in "abcdef")
            {
                names.Add("waist_" + unit);
                foreach (var side in new[] { "left", "right" })
                foreach (var segment in new[] { "crotch", "leg", "foot" })
                    names.Add($"{side}_{segment}_{unit}");
            }
            return names.ToArray();
        }

        private static string[] MawileBaseCampAnatomy()
        {
            // Both back jaws inherit the original jaw chain, with each Mega
            // branch's own bind orientation and proportions. The fourth hair
            // joint, new jaw feelers and extended side hair retain bind.
            var names = new List<string> { "origin", "waist", "spine", "neck", "head", "jaw", "hips" };
            foreach (var side in new[] { "left", "right" })
            {
                foreach (var name in new[] { "arm_01", "arm_02", "hand", "hand_sub", "leg_01", "leg_02", "foot", "foot_index", "index", "middle", "ring" }) names.Add(side + "_" + name);
                foreach (var lid in new[] { "upper_eyelid_a", "upper_eyelid_b", "lower_eyelid_a", "lower_eyelid_b", "lower_eyelid_c" })
                foreach (var segment in new[] { "01", "02" }) names.Add($"{side}_{lid}_{segment}");
                foreach (var chain in new[] { "hair_a", "hair_b", "upper_jaw", "lower_jaw", "feeler_c" })
                foreach (var segment in new[] { "01", "02", "03" }) names.Add($"{side}_{chain}_{segment}");
            }
            return names.ToArray();
        }

        private static string[] LatiasLatiosBaseCampAnatomy(int species)
        {
            // The shared face, body and original arms retain donor motion.
            // Added feelers, legs, second arms and eyelid extensions keep bind.
            var names = new List<string> { "origin", "waist", "spine", "neck_01", "neck_02", "neck_03", "head", "upper_jaw", "lower_jaw", "hips", "tail" };
            foreach (var side in new[] { "left", "right" })
            {
                foreach (var name in new[] { "shoulder", "arm_a_01", "arm_a_02", "hand", "index", "middle", "ring" }) names.Add(side + "_" + name);
                for (var letter = 'a'; letter <= 'i'; letter++) names.Add($"{side}_jaw_{letter}");
                var lastUpper = species == 380 ? 'j' : 'm';
                for (var letter = 'a'; letter <= lastUpper; letter++)
                {
                    var name = $"{side}_upper_eyelid_{letter}";
                    names.Add(name);
                    if (species == 380 ? letter < 'j' : letter > 'a') names.Add(name + "_sub");
                }
                for (var letter = 'a'; letter <= 'i'; letter++)
                {
                    var name = $"{side}_lower_eyelid_{letter}";
                    names.Add(name);
                    if (letter < 'i') names.Add(name + "_sub");
                }
            }
            return names.ToArray();
        }

        private static IReadOnlyDictionary<string, string>? BaseCampBranchAnchors(string resourceStem, int species, int form,
            PreviewBone[] source, PreviewBone[] target)
        {
            if ((species, form) == (80, 1))
            {
                if (resourceStem != "pm0080_51_00") throw new InvalidDataException("Base-form camp branch donor is not qualified.");
                var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in new[] { "left_upper_eyelid_h", "right_upper_eyelid_h", "left_lower_eyelid_b", "right_lower_eyelid_b" })
                {
                    var original = source.SingleOrDefault(b => b.Name == name);
                    var destination = target.SingleOrDefault(b => b.Name == name);
                    if (original is null || destination is null || original.Parent < 0 || destination.Parent < 0
                        || source[original.Parent].Name != "feeler_a" || target[destination.Parent].Name != "spine_02")
                        throw new InvalidDataException("Base-form camp branch anatomy differs from its qualified donor.");
                    anchors.Add(name, "waist");
                }
                return anchors;
            }
            if ((species, form) == (214, 1))
            {
                if (resourceStem != "pm0214_51_00") throw new InvalidDataException("Base-form camp branch donor is not qualified.");
                var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var side in new[] { "left", "right" })
                {
                    var name = side + "_arm_01";
                    var original = source.SingleOrDefault(b => b.Name == name);
                    var destination = target.SingleOrDefault(b => b.Name == name);
                    if (original is null || destination is null || original.Parent < 0 || destination.Parent < 0
                        || source[original.Parent].Name != "spine" || target[destination.Parent].Name != side + "_shoulder"
                        || target[destination.Parent].Parent < 0 || target[target[destination.Parent].Parent].Name != "head")
                        throw new InvalidDataException("Base-form camp branch anatomy differs from its qualified donor.");
                    anchors.Add(name, "waist");
                }
                return anchors;
            }
            if ((species, form) == (150, 2))
            {
                if (resourceStem != "pm0150_52_00") throw new InvalidDataException("Base-form camp branch donor is not qualified.");
                var original = source.SingleOrDefault(b => b.Name == "tail_01");
                var destination = target.SingleOrDefault(b => b.Name == "tail_01");
                if (original is null || destination is null || original.Parent < 0 || destination.Parent < 0
                    || source[original.Parent].Name != "hips" || target[destination.Parent].Name != "head")
                    throw new InvalidDataException("Base-form camp branch anatomy differs from its qualified donor.");
                return new Dictionary<string, string>(StringComparer.Ordinal) { ["tail_01"] = "waist" };
            }
            if (species is 380 or 381 && form == 1)
            {
                if (resourceStem != $"pm{species:0000}_51_00") throw new InvalidDataException("Base-form camp branch donor is not qualified.");
                var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var side in new[] { "left", "right" })
                foreach (var digit in new[] { "index", "middle", "ring" })
                {
                    var name = side + "_" + digit;
                    var original = source.SingleOrDefault(b => b.Name == name);
                    var destination = target.SingleOrDefault(b => b.Name == name);
                    if (original is null || destination is null || original.Parent < 0 || destination.Parent < 0
                        || source[original.Parent].Name != side + "_arm_02" || target[destination.Parent].Name != side + "_hand"
                        || target[destination.Parent].Parent < 0 || target[target[destination.Parent].Parent].Name != side + "_arm_a_02")
                        throw new InvalidDataException("Base-form camp branch anatomy differs from its qualified donor.");
                    anchors.Add(name, side + "_arm_a_02");
                }
                return anchors;
            }
            if ((species, form) != (952, 1)) return null;
            if (resourceStem != "pm1043_51_00") throw new InvalidDataException("Base-form camp branch donor is not qualified.");
            foreach (var (head, neck) in new[] { ("head_a", "left_neck_05"), ("head_b", "right_neck_05") })
            {
                var original = source.SingleOrDefault(b => b.Name == head);
                var destination = target.SingleOrDefault(b => b.Name == head);
                if (original is null || destination is null || original.Parent < 0 || destination.Parent < 0
                    || source[original.Parent].Name != "waist" || target[destination.Parent].Name != neck)
                    throw new InvalidDataException("Base-form camp branch anatomy differs from its qualified donor.");
            }
            return new Dictionary<string, string>(StringComparer.Ordinal) { ["head_a"] = "waist", ["head_b"] = "waist" };
        }

        private static string BaseCampSourceMotionCode(int species, int form, string name, string code)
        {
            // Cross banks only for the exact same authored camp action.
            // Battle and field identities never use this action-bank adapter.
            var sourceBank = (species, form) switch
            {
                (71, 1) or (142, 1) => 20000,
                (121, 1) => 10000,
                (94, 1) or (127, 1) or (149, 1) or (604, 1) or (655, 1)
                    or (658, 3) or (678, 2) or (678, 3) or (687, 1) or (718, 5) or (780, 1) => 0,
                _ => -1
            };
            if (sourceBank >= 0 && name.StartsWith("kw", StringComparison.Ordinal)
                && NativeClips.TryGetValue(name, out var value)
                && int.Parse(code, System.Globalization.CultureInfo.InvariantCulture) % 10000
                    == int.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
                return (int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) + sourceBank)
                    .ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
            return code;
        }

        private void AddBaseFormCampMotions(PreviewScene scene,
            Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)> clips, IEnumerable<(string Code, string SourceCode)> required, int species, int form)
        {
            if (!(species == 6 && form is 1 or 2) && !HasQualifiedBaseCampForm(species, form))
                throw new InvalidDataException("Base-form camp rig is not qualified.");
            var resourceStem = Profile(new(species, form)).ResourceStem
                ?? throw new InvalidDataException("Base-form camp donor identity is missing.");
            var stem = BaseCampResourceStem(resourceStem, species, form);
            var folder = $"ik_pokemon/data/{stem[..6]}/{stem}/";
            var source = new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(folder + stem + ".trmdl");
            if (source.Rig.Warnings.Length > 0) throw new InvalidDataException("Base-form camp rig requires another conversion adapter.");
            var graph = ModelAssetGraph.Read(new[] { (folder + stem + "_base.tracn", (string?)null) }, (path, _) => Donor(path));
            var sourceBones = source.Rig.Bones.ToDictionary(b => b.Name, StringComparer.Ordinal);
            var branchAnchors = BaseCampBranchAnchors(resourceStem, species, form, source.Rig.Bones, scene.Rig.Bones);
            var fixedTranslations = BaseCampFixedTranslations(resourceStem, species, form, source.Rig.Bones, scene.Rig.Bones);
            var translationReferences = BaseCampTranslationReferences(resourceStem, species, form, source.Rig.Bones, scene.Rig.Bones);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var bone in scene.Rig.Bones)
            {
                var name = BaseCampSourceBoneName(bone.Name, resourceStem, species, form);
                if (!sourceBones.TryGetValue(name, out var original)) continue;
                var parentIndex = bone.Parent;
                if (branchAnchors is not null && branchAnchors.TryGetValue(bone.Name, out var anchor))
                    parentIndex = Array.FindIndex(scene.Rig.Bones, b => b.Name == anchor);
                if (HasQualifiedBaseCampForm(species, form))
                    while (parentIndex >= 0 && !map.ContainsKey(scene.Rig.Bones[parentIndex].Name)) parentIndex = scene.Rig.Bones[parentIndex].Parent;
                if (parentIndex >= 0)
                {
                    if (!map.TryGetValue(scene.Rig.Bones[parentIndex].Name, out var mappedParent)) continue;
                    var at = original.Parent;
                    while (at >= 0 && source.Rig.Bones[at].Name != mappedParent) at = source.Rig.Bones[at].Parent;
                    if (at < 0) continue;
                }
                map.Add(bone.Name, name);
            }
            foreach (var name in BaseCampRequiredAnatomy(species, form))
                if (!map.ContainsKey(name)) throw new InvalidDataException("Base-form camp motion has an incomplete anatomical mapping.");
            var motions = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal)).ToDictionary(
                r => Path.GetFileNameWithoutExtension(r.Id)[(stem.Length + 1)..].Split('_')[0], StringComparer.Ordinal);
            foreach (var (code, sourceCode) in required.Distinct().Where(motion => !clips.ContainsKey(motion.Code)))
            {
                if (!motions.TryGetValue(code, out var motion)
                    && !motions.TryGetValue(sourceCode, out motion))
                    throw new InvalidDataException("Base-form camp source motion is missing: " + code);
                var original = PreviewRigReader.Animation(new(motion.Bytes), motion.Id, source.Rig.Bones);
                var skeleton = SwShAnimationWriter.Retarget(original, source.Rig.Bones, scene.Rig.Bones, map, branchAnchors, fixedTranslations, translationReferences);
                var decoded = PreviewRigReader.Animation(new(skeleton), motion.Id, scene.Rig.Bones);
                clips.Add(code, (skeleton, decoded));
            }
        }

        private static IEnumerable<string> BaseCampRequiredAnatomy(int species, int form)
        {
            if (ExtendedMegaBaseCampAnatomy(species, form) is { } shared) return shared;
            if (species != 445 || form != 2)
                return BaseCampAnatomy(species).Concat(AdditionalBaseCampAnatomy(species, form));
            // Mega Garchomp Z has a separate torso and face hierarchy. Its second
            // spine/neck segments, arm rolls, crotches and tail extensions keep bind.
            string[] body = ["origin", "waist", "spine_01", "neck_01", "head", "jaw", "hips",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "left_feeler_01",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "right_feeler_01",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_index",
                "right_leg_01", "right_leg_02", "right_foot", "right_foot_index",
                "tail_01", "tail_02", "tail_03", "tail_04"];
            return body.Concat(from side in new[] { "left", "right" }
                from lid in new[] { "upper", "lower" }
                from letter in (lid == "upper" ? "abcdefghij" : "abcdefgh")
                from segment in (lid == "lower" && letter == 'g' ? new[] { "01" } : new[] { "01", "02" })
                select $"{side}_{lid}_eyelid_{letter}_{segment}");
        }

        private static IReadOnlyDictionary<string, string>? BaseCampTranslationReferences(string resourceStem, int species, int form,
            PreviewBone[] source, PreviewBone[] target)
        {
            if (ExtendedMegaTranslationReferences(resourceStem, species, form) is { } megaReferences) return megaReferences;
            if ((species, form) == (383, 1))
            {
                if (resourceStem != "pm0383_51_00")
                    throw new InvalidDataException("Primal Groudon camp references require the qualified donor.");
                var references = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var side in new[] { "left", "right" })
                {
                    // Base neck and torso spikes have nearly zero pivot offsets.
                    // Scale their authored displacement by the adjacent neck or
                    // spine segment, rather than the Primal spike's long offset.
                    references.Add(side + "_feeler_a", "head");
                    references.Add(side + "_feeler_b", "neck_03");
                    references.Add(side + "_feeler_d", "spine_02");
                }
                return references;
            }
            if (species != 870 || form != 1) return null;
            // The commander's moving body pivot is nearly zero in the base
            // rig. Measure its corresponding leg segment rather than its
            // unrelated pivot offset. Retarget checks both descendant chains,
            // unit scales and the bounded ratio for every source clip.
            if (resourceStem != "pm0923_51_00")
                throw new InvalidDataException("Falinks camp reference requires the qualified donor identity.");
            foreach (var bones in new[] { source, target })
                foreach (var (name, parent) in new[] { ("waist_a", "waist"), ("left_crotch_a", "waist_a"), ("left_leg_a", "left_crotch_a") })
                    if (bones.SingleOrDefault(b => b.Name == name) is not { Parent: >= 0 } bone || bones[bone.Parent].Name != parent)
                        throw new InvalidDataException("Falinks camp reference requires the commander's body and leg chain.");
            return new Dictionary<string, string>(StringComparer.Ordinal) { ["waist_a"] = "left_leg_a" };
        }

        private static IReadOnlySet<string>? BaseCampFixedTranslations(string resourceStem, int species, int form,
            PreviewBone[] source, PreviewBone[] target)
        {
            if (species == 998 && form == 1)
            {
                // All 59 base clips retain the spine's authored 0.0001 offset.
                // Keep the Mega's 0.001 offset and transfer its rotation; every
                // translation key must still equal source bind exactly.
                if (resourceStem != "pm1055_51_00"
                    || source.SingleOrDefault(b => b.Name == "spine_01") is not { Parent: >= 0 } original
                    || target.SingleOrDefault(b => b.Name == "spine_01") is not { Parent: >= 0 } destination
                    || source[original.Parent].Name != "waist" || target[destination.Parent].Name != "waist")
                    throw new InvalidDataException("Baxcalibur camp fixed offset requires the qualified spine and waist rig.");
                return new HashSet<string>(StringComparer.Ordinal) { "spine_01" };
            }
            if (species is 768 or 780 && form == 1)
            {
                // Every Golisopod (115) and Drampa (59) base clip retains these
                // pivots' bind translations. Transfer rotation at the Mega's offsets.
                // Retarget still rejects any moving offset or non-unit scale.
                var qualifiedStem = species == 768 ? "pm0867_51_00" : "pm0856_51_00";
                var offsets = new HashSet<string>(StringComparer.Ordinal) { "spine_01", "hips" };
                foreach (var name in offsets)
                    if (resourceStem != qualifiedStem
                        || source.SingleOrDefault(b => b.Name == (species == 780 && name == "spine_01" ? "spine" : name)) is not { Parent: >= 0 } original
                        || target.SingleOrDefault(b => b.Name == name) is not { Parent: >= 0 } destination
                        || source[original.Parent].Name != "waist" || target[destination.Parent].Name != "waist")
                        throw new InvalidDataException("Camp fixed offsets require the qualified spine, hips and waist rig.");
                return offsets;
            }
            if (species != 115 || form != 1) return null;
            // The child's spine and hips rotate in both donors, but every source offset
            // is its bind translation. Retarget checks that contract per clip.
            var names = new HashSet<string>(StringComparer.Ordinal) { "spine_b", "hips_b" };
            foreach (var name in names)
                if (resourceStem != "pm0115_51_00"
                    || source.SingleOrDefault(b => b.Name == name) is not { Parent: >= 0 } original
                    || target.SingleOrDefault(b => b.Name == name) is not { Parent: >= 0 } destination
                    || source[original.Parent].Name != "waist_b" || target[destination.Parent].Name != "waist_b")
                    throw new InvalidDataException("Kangaskhan camp fixed offsets require the qualified mother and child rig.");
            return names;
        }

        private static string BaseCampSourceBoneName(string name, string resourceStem, int species, int form)
        {
            if (name == resourceStem) return BaseCampResourceStem(resourceStem, species, form);
            if (species == 801 && form == 3 && name == "pm0882_51_00") return "pm0882_12_00";
            if (species == 71 && form == 1)
                return name switch { "spine" => "spine_01", "neck" => "spine_02", _ => name };
            if (species == 530 && form == 1)
                return name switch { "spine" => "spine_01", "neck" => "neck_02", "left_arm_02" => "left_arm", "right_arm_02" => "right_arm", _ => name };
            if (species == 323 && form == 1)
                return name switch { "jaw" => "lower_jaw", "lower_jaw" => "jaw_a", "upper_jaw" => "jaw_b", _ => name };
            if (species == 655 && form == 1 && name is "left_feeler_e" or "right_feeler_e") return "mega_only_" + name;
            if (species == 718 && form == 5 && name == "feeler_b") return "mega_only_feeler_b";
            if (species == 383 && form == 1)
                return name switch
                {
                    "neck_02" => "primal_only_neck_02", "neck_03" => "neck_02",
                    "left_thumb_01" => "left_thumb", "right_thumb_01" => "right_thumb",
                    "left_feeler_e" => "left_feeler_h", "right_feeler_e" => "right_feeler_h",
                    "left_feeler_f" => "left_feeler_g", "right_feeler_f" => "right_feeler_g",
                    "left_feeler_g" => "left_feeler_f", "right_feeler_g" => "right_feeler_f",
                    "left_feeler_h" => "left_feeler_e", "right_feeler_h" => "right_feeler_e",
                    _ => name
                };
            if (species == 870 && form == 1 && name is "left_shoulder_a" or "left_arm_a" or "left_hand_a" or "right_shoulder_a" or "right_arm_a" or "right_hand_a")
                return name[..^2];
            if (species == 354 && form == 1)
                return name switch
                {
                    // The single Mega spine carries both original spine segments.
                    // Retarget evaluates the full source ancestry, including spine_01.
                    "spine" => "spine_02", "hips_01" => "hips",
                    "left_hand_01" => "left_hand", "right_hand_01" => "right_hand",
                    _ => name
                };
            if (species == 3 && form == 1)
                return name switch
                {
                    "left_index" => "left_fingerbase", "right_index" => "right_fingerbase",
                    "left_foot_index" => "left_toe", "right_foot_index" => "right_toe",
                    _ when name.Contains("_upper_eyelid_", StringComparison.Ordinal) => name[..name.IndexOf("_upper_eyelid_", StringComparison.Ordinal)] + "_upper_eyelid",
                    _ when name.Contains("_lower_eyelid_", StringComparison.Ordinal) && name.EndsWith("_01", StringComparison.Ordinal) => name[..name.IndexOf("_lower_eyelid_", StringComparison.Ordinal)] + "_lower_eyelid",
                    _ => name
                };
            if (species == 130 && form == 1)
                return name switch
                {
                    "hips_a" => "hips",
                    "left_ear_a_01" => "left_ear", "right_ear_a_01" => "right_ear",
                    "left_eyelid_a" => "left_eyelid_a_01", "right_eyelid_a" => "right_eyelid_a_01",
                    _ when name.StartsWith("spine_a_", StringComparison.Ordinal) => name.Replace("spine_a_", "spine_", StringComparison.Ordinal),
                    _ when name.StartsWith("tail_a_", StringComparison.Ordinal) => name.Replace("tail_a_", "tail_", StringComparison.Ordinal),
                    "tongue_a_01" => "tongue_01", "tongue_a_02" => "tongue_02",
                    _ => name
                };
            // The single Mega shoulder spans Scizor's two original segments.
            if (species == 212 && form == 1)
                return name switch { "left_shoulder" => "left_shoulder_02", "right_shoulder" => "right_shoulder_02", _ => name };
            if (species == 460 && form == 1)
                return name switch
                {
                    "jaw_a" => "jaw_01", "jaw_b" => "jaw_02",
                    "left_lower_eyelid" => "left_lower_eyelid_a_01", "right_lower_eyelid" => "right_lower_eyelid_a_01",
                    "left_foot_middle" => "left_foot_finger_a", "right_foot_middle" => "right_foot_finger_a",
                    "left_foot_ring" => "left_foot_finger_b", "right_foot_ring" => "right_foot_finger_b",
                    "left_foot_index" => "left_foot_finger_c", "right_foot_index" => "right_foot_finger_c",
                    _ => name
                };
            if (species == 998 && form == 1)
                return name switch
                {
                    "left_lower_eyelid" => "left_lower_eyelid_a",
                    "right_lower_eyelid" => "right_lower_eyelid_a",
                    _ => name
                };
            // Pidgeot's second wing root becomes i/j; the new distal feathers
            // retain bind. Both leg variants receive the original leg motion,
            // with the single-segment a leg spanning the original two segments.
            if (species == 18 && form == 1)
                return name switch
                {
                    "left_wing_i" => "left_wing_b_01", "right_wing_i" => "right_wing_b_01",
                    "left_wing_j_01" => "left_wing_b_02", "right_wing_j_01" => "right_wing_b_02",
                    "left_upper_eyelid_a" => "left_upper_eyelid_a_01", "right_upper_eyelid_a" => "right_upper_eyelid_a_01",
                    "left_upper_eyelid_b" => "left_upper_eyelid_b_01", "right_upper_eyelid_b" => "right_upper_eyelid_b_01",
                    "left_upper_eyelid_c" => "left_upper_eyelid_c_01", "right_upper_eyelid_c" => "right_upper_eyelid_c_01",
                    "left_lower_eyelid" => "left_lower_eyelid_a_01", "right_lower_eyelid" => "right_lower_eyelid_a_01",
                    "left_leg_a" => "left_leg_02", "right_leg_a" => "right_leg_02",
                    "left_foot_a" or "left_foot_b" => "left_foot", "right_foot_a" or "right_foot_b" => "right_foot",
                    "left_leg_b_01" => "left_leg_01", "left_leg_b_02" => "left_leg_02",
                    "right_leg_b_01" => "right_leg_01", "right_leg_b_02" => "right_leg_02",
                    "left_foot_thumb_a_01" or "left_foot_thumb_b_01" => "left_foot_thumb",
                    "right_foot_thumb_a_01" or "right_foot_thumb_b_01" => "right_foot_thumb",
                    _ when name.Contains("_foot_", StringComparison.Ordinal)
                        && (name.Contains("_index_", StringComparison.Ordinal) || name.Contains("_middle_", StringComparison.Ordinal) || name.Contains("_ring_", StringComparison.Ordinal))
                        && (name.EndsWith("_01", StringComparison.Ordinal) || name.EndsWith("_02", StringComparison.Ordinal))
                        => name.Replace("_a_", "_", StringComparison.Ordinal).Replace("_b_", "_", StringComparison.Ordinal),
                    _ => name
                };
            if (species == 6 && form == 2 && (name.StartsWith("left_wing_", StringComparison.Ordinal) || name.StartsWith("right_wing_", StringComparison.Ordinal)))
                return name.Replace("_wing_", "_wing_a_", StringComparison.Ordinal);
            // Froslass adds a second spine segment in its Mega rig; its first
            // segment retains the base rig's sole spine and anatomical ancestry.
            if (species == 478 && form == 1 && name == "spine_01") return "spine";
            if (species == 373 && form == 1 && name == "spine") return "spine_01";
            if (species == 780 && form == 1)
                return name switch
                {
                    "spine_01" => "spine", "tongue" => "tongue_01", "tail" => "tail_a_01",
                    "left_arm_01" => "left_arm_a", "left_arm_02" => "left_arm_b",
                    "right_arm_01" => "right_arm_a", "right_arm_02" => "right_arm_b",
                    _ => name
                };
            // Golisopod's original large arm and wrist become the first two
            // arm joints; the extended palm and third phalanges keep bind.
            // Both new leg branches receive the original leg's motion in
            // their own authored bind frames, as with Mawile's split jaws.
            if (species == 768 && form == 1)
                return name switch
                {
                    "feeler_a_01" => "feeler",
                    "left_upper_eyelid_01" => "left_upper_eyelid", "right_upper_eyelid_01" => "right_upper_eyelid",
                    "left_lower_eyelid_01" => "left_lower_eyelid", "right_lower_eyelid_01" => "right_lower_eyelid",
                    "left_arm_01" => "left_arm_a", "left_arm_02" => "left_hand_a",
                    "right_arm_01" => "right_arm_a", "right_arm_02" => "right_hand_a",
                    "left_index_01" => "left_finger_a_01", "left_index_02" => "left_finger_a_02",
                    "right_index_01" => "right_finger_a_01", "right_index_02" => "right_finger_a_02",
                    "left_middle_01" => "left_finger_b_01", "left_middle_02" => "left_finger_b_02",
                    "right_middle_01" => "right_finger_b_01", "right_middle_02" => "right_finger_b_02",
                    "left_leg_a_01" or "left_leg_b_01" => "left_leg_01",
                    "left_leg_a_02" or "left_leg_b_02" => "left_leg_02",
                    "right_leg_a_01" or "right_leg_b_01" => "right_leg_01",
                    "right_leg_a_02" or "right_leg_b_02" => "right_leg_02",
                    "left_foot_a" or "left_foot_b" => "left_foot", "right_foot_a" or "right_foot_b" => "right_foot",
                    "left_foot_index_a" or "left_foot_index_b" => "left_foot_index",
                    "right_foot_index_a" or "right_foot_index_b" => "right_foot_index",
                    _ => name
                };
            // The original shoulder wing becomes an arm/hand chain. The
            // secondary folded wing keeps its first two joints. The two-joint
            // central tail includes the original intermediate tail motion;
            // added cape, crest, horn and feather branches retain native bind.
            if (species == 398 && form == 1)
                return name switch
                {
                    "spine_01" => "spine",
                    "hair_a_01" => "hair_01", "hair_a_02" => "hair_02", "hair_a_03" => "hair_03",
                    "left_arm_01" => "left_wing_a_01", "left_arm_02" => "left_wing_a_02", "left_hand" => "left_wing_a_03",
                    "right_arm_01" => "right_wing_a_01", "right_arm_02" => "right_wing_a_02", "right_hand" => "right_wing_a_03",
                    "left_wing_01" => "left_wing_b_01", "left_wing_02" => "left_wing_b_02",
                    "right_wing_01" => "right_wing_b_01", "right_wing_02" => "right_wing_b_02",
                    "tail_01" => "tail", "tail_02" => "tail_a_02",
                    _ => name
                };
            if (species == 303 && form == 1)
                return name switch
                {
                    "left_hair_a_01" => "left_hair_01", "left_hair_a_02" => "left_hair_02", "left_hair_a_03" => "left_hair_03",
                    "right_hair_a_01" => "right_hair_01", "right_hair_a_02" => "right_hair_02", "right_hair_a_03" => "right_hair_03",
                    "left_hair_b_01" or "right_hair_b_01" => "hair_01",
                    "left_hair_b_02" or "right_hair_b_02" => "hair_02",
                    "left_hair_b_03" or "right_hair_b_03" => "hair_03",
                    "left_upper_jaw_01" or "right_upper_jaw_01" => "upper_jaw_01",
                    "left_upper_jaw_02" or "right_upper_jaw_02" => "upper_jaw_02",
                    "left_upper_jaw_03" or "right_upper_jaw_03" => "upper_jaw_03",
                    "left_lower_jaw_01" or "right_lower_jaw_01" => "lower_jaw_01",
                    "left_lower_jaw_02" or "right_lower_jaw_02" => "lower_jaw_02",
                    "left_lower_jaw_03" or "right_lower_jaw_03" => "lower_jaw_03",
                    "left_index" => "left_index_01", "left_middle" => "left_middle_01", "left_ring" => "left_ring_01",
                    "right_index" => "right_index_01", "right_middle" => "right_middle_01", "right_ring" => "right_ring_01",
                    "left_feeler_c_01" => "left_feeler_01", "left_feeler_c_02" => "left_feeler_02", "left_feeler_c_03" => "left_feeler_03",
                    "right_feeler_c_01" => "right_feeler_01", "right_feeler_c_02" => "right_feeler_02", "right_feeler_c_03" => "right_feeler_03",
                    _ => name
                };
            if (species == 319 && form == 1)
                return name switch
                {
                    "feeler_b_01" => "feeler_a_01", "feeler_b_02" => "feeler_a_02",
                    "feeler_c_01" => "feeler_b_01", "feeler_c_02" => "feeler_b_02",
                    _ => name
                };
            if (species is 380 or 381 && form == 1)
                return name switch
                {
                    "spine" when species == 381 => "spine_01",
                    "left_arm_a_01" => "left_arm_01", "left_arm_a_02" => "left_arm_02",
                    "right_arm_a_01" => "right_arm_01", "right_arm_a_02" => "right_arm_02",
                    _ => name
                };
            if (species == 376 && form == 1)
                return name switch
                {
                    "left_arm_a_01" => "left_arm_01", "left_arm_a_02" => "left_arm_02", "left_hand_a" => "left_hand",
                    "right_arm_a_01" => "right_arm_01", "right_arm_a_02" => "right_arm_02", "right_hand_a" => "right_hand",
                    "left_leg_a_01" => "left_leg_01", "left_leg_a_02" => "left_leg_02", "left_foot_a" => "left_foot",
                    "right_leg_a_01" => "right_leg_01", "right_leg_a_02" => "right_leg_02", "right_foot_a" => "right_foot",
                    "left_finger_a_01" => "left_finger_a", "left_finger_b_01" => "left_finger_b", "left_finger_c_01" => "left_finger_c",
                    "right_finger_a_01" => "right_finger_a", "right_finger_b_01" => "right_finger_b", "right_finger_c_01" => "right_finger_c",
                    _ => name
                };
            if (species == 80 && form == 1)
                return name switch
                {
                    "spine_01" => "spine",
                    "left_arm" => "left_arm_01", "right_arm" => "right_arm_01",
                    _ => name
                };
            if (species == 214 && form == 1)
                return name switch
                {
                    "left_index_01" => "left_index", "right_index_01" => "right_index",
                    "left_middle_01" => "left_middle", "right_middle_01" => "right_middle",
                    _ => name
                };
            if (species == 150 && form == 1)
                return name switch
                {
                    "spine" => "spine_01",
                    "left_upper_eyelid_a_01" => "left_upper_eyelid_a", "right_upper_eyelid_a_01" => "right_upper_eyelid_a",
                    "left_upper_eyelid_b_01" => "left_upper_eyelid_b", "right_upper_eyelid_b_01" => "right_upper_eyelid_b",
                    "left_upper_eyelid_c_01" => "left_upper_eyelid_c", "right_upper_eyelid_c_01" => "right_upper_eyelid_c",
                    "left_lower_eyelid_a" => "left_lower_eyelid", "right_lower_eyelid_a" => "right_lower_eyelid",
                    "left_foot_index_01" => "left_foot_index", "right_foot_index_01" => "right_foot_index",
                    "left_foot_thumb_01" => "left_foot_thumb", "right_foot_thumb_01" => "right_foot_thumb",
                    _ => name
                };
            if (species == 150 && form == 2)
                return name switch
                {
                    "left_lower_eyelid_a" => "left_lower_eyelid", "right_lower_eyelid_a" => "right_lower_eyelid",
                    "left_foot_index_01" => "left_foot_index", "right_foot_index_01" => "right_foot_index",
                    _ => name
                };
            // The single spine, eyelid and tail joints follow the base rig's
            // first segments. Each hind toe keeps the three-joint ancestry.
            if (species == 310 && form == 1)
                return name switch
                {
                    "spine" => "spine_01", "tail" => "tail_01",
                    "left_upper_eyelid_j" => "left_upper_eyelid_j_01",
                    "right_upper_eyelid_j" => "right_upper_eyelid_j_01",
                    "left_toe_01" => "left_toe", "left_toe_02" => "left_foot_index_01",
                    "left_foot_index" => "left_foot_index_02",
                    "right_toe_01" => "right_toe", "right_toe_02" => "right_foot_index_01",
                    "right_foot_index" => "right_foot_index_02",
                    _ => name
                };
            if (species == 334 && form == 1)
            {
                if (name.StartsWith("left_wing_", StringComparison.Ordinal) || name.StartsWith("right_wing_", StringComparison.Ordinal))
                    return name.Replace("_wing_", "_wing_a_", StringComparison.Ordinal);
                return name switch
                {
                    "spine_01" => "spine",
                    "left_upper_eyelid_b_01" => "left_upper_eyelid_b",
                    "right_upper_eyelid_b_01" => "right_upper_eyelid_b",
                    "left_upper_eyelid_e" => "left_upper_eyelid_e_01",
                    "right_upper_eyelid_e" => "right_upper_eyelid_e_01",
                    "left_lower_eyelid_c" => "left_lower_eyelid_c_01",
                    "right_lower_eyelid_c" => "right_lower_eyelid_c_01",
                    _ => name
                };
            }
            // Garchomp adds a spine, jaw and tail extension. The terminal tail
            // branches keep their anatomical sides despite the renamed chain.
            if (species == 445 && form == 1)
                return name switch
                {
                    "spine_01" => "spine",
                    "jaw_a" => "jaw",
                    "tail_a_01" => "tail_01", "tail_a_02" => "tail_02",
                    "tail_a_03" => "tail_03", "tail_a_04" => "tail_04",
                    "tail_b" => "tail_a", "tail_c" => "tail_b",
                    _ => name
                };
            if (species == 445 && form == 2)
            {
                if (name is "spine_01" or "neck_01") return name[..^3];
                if (name is "left_feeler_01" or "right_feeler_01") return name[..^3];
                if ((name.StartsWith("left_", StringComparison.Ordinal) || name.StartsWith("right_", StringComparison.Ordinal))
                    && name.Contains("_eyelid_", StringComparison.Ordinal))
                {
                    if (name.EndsWith("_01", StringComparison.Ordinal)) return name[..^3];
                    if (name.EndsWith("_02", StringComparison.Ordinal)) return name[..^3] + "_sub";
                }
            }
            return name;
        }

        private HashSet<string>? FormVisibilityMeshes(int species, string sourceStem, string sourceFolder, PreviewScene scene)
        {
            // These single-form graphs also repeat highest-LOD channels on three
            // lower LODs. SelectedFormVisibility verifies equality before removal.
            if (species is 668 or 673 or 720 || species == 208 && sourceStem == "pm0208_51_00"
                || species == 160 && sourceStem == "pm0160_51_00"
                || species == 691 && sourceStem == "pm0710_51_00"
                || species == 870 && sourceStem == "pm0923_51_00")
                return scene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            // Eternal Flower Floette has its own graph rather than the five-color
            // graph. Furfrou's trim graphs also address meshes from other trims.
            if (species == 670 && sourceStem is "pm0714_16_00" or "pm0714_51_00"
                || species == 26 && sourceStem == "pm0026_52_00")
                return scene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var lastForm = species switch { 412 or 413 => 13, 669 or 670 or 671 => 15, 676 => 20, 666 => 30, _ => 0 };
            if (lastForm == 0) return null;
            if (sourceStem.Length != 12 || !int.TryParse(sourceStem.AsSpan(7, 2), out var selected) || selected < 11 || selected > lastForm)
                throw new InvalidDataException("Shared form visibility has an unsupported source identity.");
            var meshes = scene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            for (var form = 11; form <= lastForm; form++)
            {
                if (form == selected) continue;
                var siblingStem = sourceStem[..7] + form.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + sourceStem[9..];
                var path = sourceFolder.Replace(sourceStem, siblingStem, StringComparison.Ordinal) + siblingStem + ".trmdl";
                if (species is 666 or 676)
                    SwShRestorationGenderAudit.RequireSharedModel(Donor("ik_pokemon/catalog/catalog/poke_resource_table.trpmcatalog"),
                        species, form - 11, path["ik_pokemon/data/".Length..]);
                var sibling = new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(path);
                if (sibling.Rig.Warnings.Length != 0) throw new InvalidDataException("Shared form visibility sibling requires another model adapter.");
                meshes.UnionWith(sibling.Primitives.Select(p => p.Name));
            }
            return meshes;
        }

        private static PreviewVisibility[] GreninjaMegaVisibility(IReadOnlyList<PreviewVisibility> tracks, PreviewScene scene)
        {
            const string prefix = "pm0725_51_00_";
            var selected = new[] { "body", "tongue", "eye", "shuriken", "anashuriken", "minishuriken", "kunai", "bubble" }
                .Select(name => prefix + name + "_mesh_shape").ToHashSet(StringComparer.Ordinal);
            if (scene.Primitives.Count != 10 || !selected.SetEquals(scene.Primitives.Select(p => p.Name))
                || scene.Primitives.GroupBy(p => p.Name).Any(g => g.Count() != (g.Key is prefix + "body_mesh_shape" or prefix + "eye_mesh_shape" ? 2 : 1))
                || tracks.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
                throw new InvalidDataException("Mega Greninja visibility requires the qualified primary mesh set.");
            // The primary model retains the authored effect visibility. Its unused
            // lower LODs leave these four effects enabled instead of repeating it.
            // Accept only those exact constant channels; all other lower LODs
            // still require equality with their retained primary channel.
            var remaining = new List<PreviewVisibility>();
            foreach (var track in tracks)
            {
                var lower = track.Name.EndsWith("_lod1", StringComparison.Ordinal)
                    || track.Name.EndsWith("_lod2", StringComparison.Ordinal) || track.Name.EndsWith("_lod3", StringComparison.Ordinal);
                var effect = lower && track.Name[..^5] is prefix + "shuriken_mesh_shape" or prefix + "anashuriken_mesh_shape"
                    or prefix + "minishuriken_mesh_shape" or prefix + "kunai_mesh_shape";
                if (!effect) { remaining.Add(track); continue; }
                if (track.Keys.Length != 1 || track.Keys[0].Frame != 0 || !track.Keys[0].Value.SequenceEqual(new[] { 1f })
                    || !tracks.Any(t => t.Name == track.Name[..^5]))
                    throw new InvalidDataException("Mega Greninja effect LOD visibility requires another adapter.");
            }
            return SelectedFormVisibility(remaining, scene, selected);
        }

        private static PreviewVisibility[] HoopaUnboundVisibility(IReadOnlyList<PreviewVisibility> tracks, PreviewScene scene)
        {
            const string prefix = "pm0774_11_00_";
            var selected = new[] { "body", "eye_a", "eye_b", "head_a", "head_b", "mouth_a", "mouth_b", "tooth", "arm", "pipe_a", "pipe_b", "pipe_c" }
                .Select(name => prefix + name + "_mesh_shape").ToHashSet(StringComparer.Ordinal);
            if (scene.Primitives.Count != 17 || !selected.SetEquals(scene.Primitives.Select(p => p.Name))
                || scene.Primitives.GroupBy(p => p.Name).Any(g => g.Count() != (g.Key is prefix + "body_mesh_shape"
                    or prefix + "eye_a_mesh_shape" or prefix + "eye_b_mesh_shape" or prefix + "head_a_mesh_shape" or prefix + "arm_mesh_shape" ? 2 : 1))
                || tracks.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
                throw new InvalidDataException("Hoopa Unbound visibility requires the qualified primary mesh set.");
            // The retained primary eye and mouth shapes animate independently.
            // Their unused source LODs instead author a single enabled key.
            var remaining = new List<PreviewVisibility>();
            foreach (var track in tracks)
            {
                var lower = track.Name.EndsWith("_lod1", StringComparison.Ordinal)
                    || track.Name.EndsWith("_lod2", StringComparison.Ordinal) || track.Name.EndsWith("_lod3", StringComparison.Ordinal);
                var constantLod = lower && track.Name[..^5] is prefix + "eye_a_mesh_shape" or prefix + "eye_b_mesh_shape"
                    or prefix + "mouth_a_mesh_shape" or prefix + "mouth_b_mesh_shape" or prefix + "tooth_mesh_shape";
                if (!constantLod) { remaining.Add(track); continue; }
                if (track.Keys.Length != 1 || track.Keys[0].Frame != 0 || !track.Keys[0].Value.SequenceEqual(new[] { 1f })
                    || !tracks.Any(t => t.Name == track.Name[..^5]))
                    throw new InvalidDataException("Hoopa Unbound facial LOD visibility requires another adapter.");
            }
            return SelectedFormVisibility(remaining, scene, selected);
        }

        private static PreviewVisibility[] CrabominableMegaVisibility(IReadOnlyList<PreviewVisibility> tracks, PreviewScene scene)
        {
            const string prefix = "pm0860_51_00_";
            var selected = new[] { "body", "hand", "hair_a", "hair_b", "hair_c", "hair_d" }
                .Select(name => prefix + name + "_mesh_shape").ToHashSet(StringComparer.Ordinal);
            if (scene.Primitives.Count != 11 || !selected.SetEquals(scene.Primitives.Select(p => p.Name))
                || scene.Primitives.GroupBy(p => p.Name).Any(g => g.Count() != (g.Key == prefix + "body_mesh_shape" ? 5 : g.Key == prefix + "hand_mesh_shape" ? 2 : 1))
                || tracks.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
                throw new InvalidDataException("Mega Crabominable visibility requires the qualified primary mesh set.");
            // Every authored lower LOD channel repeats its retained primary
            // channel. Validate that equality before exporting the primary mesh.
            return SelectedFormVisibility(tracks, scene, selected);
        }

        private static PreviewVisibility[] KangaskhanMegaVisibility(IReadOnlyList<PreviewVisibility> tracks, PreviewScene scene)
        {
            const string prefix = "pm0115_51_00_";
            var sections = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["bodya"] = 2, ["partsa"] = 2, ["eyea"] = 2, ["bodyb"] = 2, ["eyeb"] = 2,
                ["tongueb"] = 1, ["teethb"] = 1, ["eyelidb"] = 1, ["earb"] = 1
            }.ToDictionary(pair => prefix + pair.Key + "_mesh_shape", pair => pair.Value, StringComparer.Ordinal);
            var selected = sections.Keys.ToHashSet(StringComparer.Ordinal);
            if (scene.Primitives.Count != 14 || !selected.SetEquals(scene.Primitives.Select(part => part.Name))
                || scene.Primitives.GroupBy(part => part.Name).Any(group => group.Count() != sections[group.Key])
                || tracks.Select(track => track.Name).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
                throw new InvalidDataException("Mega Kangaskhan visibility requires the qualified mother and child mesh set.");
            // The model exports the primary mother and child sections. Every
            // omitted lower LOD must repeat its retained primary channel exactly.
            return SelectedFormVisibility(tracks, scene, selected);
        }

        private static PreviewVisibility[] HeatranMegaVisibility(IReadOnlyList<PreviewVisibility> tracks, PreviewScene scene)
        {
            const string prefix = "pm0485_51_00_";
            var selected = new[] { "body", "teeth", "foot", "metal", "finger", "eyelid" }
                .Select(name => prefix + name + "_mesh_shape").ToHashSet(StringComparer.Ordinal);
            if (!selected.SetEquals(scene.Primitives.Select(p => p.Name)))
                throw new InvalidDataException("Mega Heatran requires its qualified primary mesh set.");
            // The graph also addresses two particle-system smog shapes. Neither
            // is part of the source model; both channels are permanently enabled.
            var remaining = new List<PreviewVisibility>();
            foreach (var track in tracks)
            {
                if (track.Name is not (prefix + "smog_a_mesh_shape" or prefix + "smog_b_mesh_shape"))
                { remaining.Add(track); continue; }
                if (track.Keys.Length != 1 || track.Keys[0].Frame != 0 || !track.Keys[0].Value.SequenceEqual(new[] { 1f }))
                    throw new InvalidDataException("Mega Heatran particle visibility requires another adapter.");
            }
            return SelectedFormVisibility(remaining, scene, selected);
        }

        private static PreviewVisibility[] SelectedFormVisibility(IReadOnlyList<PreviewVisibility> tracks, PreviewScene scene, IReadOnlySet<string> declared)
        {
            // The source graph drives all flower/cloak variants together. Validate
            // sibling targets against their actual models before selecting this form.
            foreach (var track in tracks.Where(track => !declared.Contains(track.Name)))
            {
                var suffix = track.Name.Length >= 5 ? track.Name[^5..] : "";
                var original = suffix is "_lod1" or "_lod2" or "_lod3"
                    ? tracks.SingleOrDefault(candidate => candidate.Name == track.Name[..^5]) : null;
                if (original is null || !declared.Contains(original.Name) || original.Keys.Length != track.Keys.Length
                    || !original.Keys.Zip(track.Keys).All(pair => pair.First.Frame == pair.Second.Frame
                        && pair.First.Value.SequenceEqual(pair.Second.Value)))
                    throw new InvalidDataException("Shared form visibility has an undeclared target or differing lower LOD channel.");
            }
            var selected = scene.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            return tracks.Where(track => selected.Contains(track.Name)).ToArray();
        }

        private static PreviewVisibility[] VisibilitySections(PreviewScene source, PreviewScene target, IReadOnlyList<PreviewVisibility> tracks)
        {
            if (tracks.Any(track => !source.Primitives.Any(primitive => primitive.Name == track.Name)))
                tracks = SelectedFormVisibility(tracks, source, source.Primitives.Select(p => p.Name).ToHashSet(StringComparer.Ordinal));
            if (target.Primitives.Count < source.Primitives.Count || tracks.Any(track => !source.Primitives.Any(primitive => primitive.Name == track.Name)))
                throw new InvalidDataException("Source visibility targets an unavailable mesh.");
            var sections = tracks.SelectMany(track => source.Primitives.Select((primitive, index) => (primitive, index))
                .Where(pair => pair.primitive.Name == track.Name)
                .Select(pair => track with { Name = target.Primitives[pair.index].Name })).ToArray();
            return sections.GroupBy(track => track.Name, StringComparer.Ordinal).Select(group =>
            {
                var first = group.First();
                if (group.Any(track => track.Keys.Length != first.Keys.Length || !track.Keys.Zip(first.Keys)
                    .All(pair => pair.First.Frame == pair.Second.Frame && pair.First.Value.SequenceEqual(pair.Second.Value))))
                    throw new InvalidDataException("Shared mesh sections have conflicting visibility channels.");
                return first;
            }).ToArray();
        }

        private static PreviewScene UniqueMaterials(PreviewScene scene, bool shareMeshNodes = false)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var meshes = new HashSet<string>(StringComparer.Ordinal);
            return scene with { Primitives = scene.Primitives.Select((primitive, index) =>
            {
                var name = primitive.Material.Name;
                if (!names.Add(name)) { name += "_mesh" + index.ToString(System.Globalization.CultureInfo.InvariantCulture); if (!names.Add(name)) throw new InvalidDataException("Converted material name is ambiguous."); }
                var mesh = primitive.Name;
                if (!shareMeshNodes && !meshes.Add(mesh)) { mesh += "_section" + index.ToString(System.Globalization.CultureInfo.InvariantCulture); if (!meshes.Add(mesh)) throw new InvalidDataException("Converted mesh name is ambiguous."); }
                return primitive with { Name = mesh, Material = primitive.Material with { Name = name } };
            }).ToArray() };
        }

        private static PreviewScene NormalizeUvs(PreviewScene scene, bool compact = false, IReadOnlySet<string>? preserveMaterials = null)
        {
            return scene with { Primitives = scene.Primitives.Select(p =>
            {
                var indices = p.Indices;
                var vertices = p.Vertices.ToArray();
                var secondaryUv = p.SecondaryUv;
                var color0 = p.Color0;
                var tangent0 = p.Tangent0;
                var tangent1 = p.Tangent1;
                if (compact)
                {
                    // Source material sections share a vertex buffer. Keep every referenced
                    // vertex and triangle without duplicating other sections in the target.
                    var used = indices.Distinct().Order().ToArray();
                    var remap = used.Select((source, index) => (source, index)).ToDictionary(pair => pair.source, pair => (uint)pair.index);
                    vertices = used.SelectMany(index => p.Vertices.Skip(checked((int)index * 16)).Take(16)).ToArray();
                    indices = indices.Select(index => remap[index]).ToArray();
                    float[]? Attribute(float[]? values, int stride)
                    {
                        if (values is null) return null;
                        if (values.Length != checked(p.Vertices.Length / 16 * stride))
                            throw new InvalidDataException("Z-A source vertex attribute count is inconsistent.");
                        return used.SelectMany(index => values.Skip(checked((int)index * stride)).Take(stride)).ToArray();
                    }
                    secondaryUv = Attribute(secondaryUv, 2);
                    color0 = Attribute(color0, 4);
                    tangent0 = Attribute(tangent0, 4);
                    tangent1 = Attribute(tangent1, 4);
                }
                if (preserveMaterials?.Contains(p.Material.Name) != true)
                    for (var i = 0; i < vertices.Length; i += 16)
                        for (var c = 6; c <= 7; c++) vertices[i + c] -= MathF.Floor(vertices[i + c]);
                return p with
                {
                    Vertices = vertices, Indices = indices, SecondaryUv = secondaryUv,
                    Color0 = color0, Tangent0 = tangent0, Tangent1 = tangent1
                };
            }).ToArray() };
        }

        private static Dictionary<string, string> BoneAliases(PreviewScene scene, string sourceStem, string stem)
        {
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = "Origin", ["waist"] = "Waist", ["head"] = "Head", ["neck"] = "Neck", ["spine"] = "Spine",
                ["lower_jaw"] = "Jaw", ["jaw"] = "Jaw", ["left_hand"] = "LHand", ["right_hand"] = "RHand", ["left_foot"] = "LFoot", ["right_foot"] = "RFoot",
            };
            // Mega Camerupt retains both the jaw pivot and its deforming child.
            // Preserve both nodes, as for the equivalent Scarlet/Violet rigs.
            if (scene.Rig.Bones.Any(b => b.Name == "jaw") && scene.Rig.Bones.Any(b => b.Name == "lower_jaw"))
                aliases["lower_jaw"] = "LowerJaw";
            foreach (var bone in scene.Rig.Bones) aliases.TryAdd(bone.Name, bone.Name.Replace(sourceStem, stem, StringComparison.Ordinal));
            return aliases;
        }

        private static void SourceFraming(byte[] configuration, byte[] source, int species, int form)
        {
            // Keep authored framing and convert Z-A meters to SwSh centimeters.
            var framing = new ModelBuffer(source);
            var target = new ModelBuffer(configuration);
            for (var field = 3; field <= 8; field++)
            {
                var at = framing.Field(framing.Root, field);
                var destination = target.Field(target.Root, field + 14);
                // Mega Heatran omits the distance in its source camera. Keep the
                // native Heatran distance while retaining its authored target and angles.
                if (species == 485 && form == 1 && field == 3 && at == 0 && destination != 0)
                {
                    if (!float.IsFinite(target.Float(destination)) || target.Float(destination) <= 0)
                        throw new InvalidDataException("Native Heatran camera distance is invalid.");
                    continue;
                }
                // These donors encode their centered vertical camera offset as the FlatBuffer default.
                var defaultOffset = field == 5 && (species is 358 or 433 or 485 or 491 or 739
                    || form == 1 && species is 248 or 380 or 381 or 382 or 807 or 998);
                if ((at == 0 && field <= 6 && !defaultOffset) || destination == 0) throw new InvalidDataException("Z-A model framing is incomplete.");
                var value = (at == 0 ? 0 : framing.Float(at)) * (field <= 6 ? 100 : 1);
                if (!float.IsFinite(value) || (field == 3 && value <= 0)) throw new InvalidDataException("Z-A model framing is invalid.");
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(configuration.AsSpan(destination, 4), value);
            }
        }

        private static SwShEffectLocator[] Locators(PreviewScene scene, IReadOnlyDictionary<string, string> aliases, byte[] bytes, Vector3? mouthPosition = null, float airborneOffset = 0, bool pla = false, bool singleEye = false)
        {
            var source = new ModelBuffer(bytes); var nodes = source.Tables(source.Root, 1, 512);
            var transforms = PreviewRigReader.Skeleton(source);
            var entries = transforms.Select((bone, index) => (Bone: bone, Parent: source.Text(nodes[index], 6)))
                .ToDictionary(e => e.Bone.Name, StringComparer.Ordinal);
            SwShEffectLocator Convert(string name, string original)
            {
                if (!entries.TryGetValue(original, out var entry) || entry.Bone.Parent != -1 || entry.Bone.Scale.Any(v => !float.IsFinite(v) || v <= 0)
                    || entry.Parent is null || !scene.Rig.Bones.Any(b => b.Name == entry.Parent))
                    throw new InvalidDataException("Z-A effect attachment requires another target adapter.");
                var p = entry.Bone.Translation; var q = entry.Bone.Rotation; var s = entry.Bone.Scale;
                return new(name, aliases[entry.Parent], new(p[0] * 100, p[1] * 100, p[2] * 100))
                { Rotation = new(q[0], q[1], q[2], q[3]), Scale = new(s[0], s[1], s[2]) };
            }
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EffMouth01"] = "eff_mouth02", ["EffOverHead01"] = "eff_overhead01", ["EffEye01"] = "eff_eye01", ["EffEye02"] = "eff_eye02",
                ["EffHeadCenter01"] = "eff_headcenter01", ["EffFront01"] = "eff_front01", ["EffCenter01"] = "eff_center01",
                ["EffShoot01_01"] = "eff_rangeattack01", ["EffShoot01_02"] = "eff_rangeattack02",
                ["EffAttack01_01"] = "eff_attack01", ["EffAttack01_02"] = "eff_attack02", ["EffAttack01_03"] = "eff_attack03",
                ["EffGcloud01"] = "eff_overhead01",
            };
            if (pla) mapping = new(StringComparer.Ordinal)
            {
                ["EffMouth01"] = "EffMouth01", ["EffOverHead01"] = "EffOverHead01",
                ["EffEye01"] = "eff_eye_01", ["EffEye02"] = "eff_eye_02",
                ["EffHeadCenter01"] = "EffHeadCenter01", ["EffFront01"] = "EffFront01", ["EffCenter01"] = "EffCenter01",
                ["EffShoot01_01"] = "EffRangeAttack01", ["EffShoot01_02"] = "EffRangeAttack02",
                ["EffAttack01_01"] = "EffAttack01", ["EffAttack01_02"] = "EffAttack02", ["EffAttack01_03"] = "EffAttack02",
                ["EffGcloud01"] = "EffOverHead01",
            };
            // These donors expose one eye and one authored attachment.
            // Both native eye names must follow that same animated source point.
            if (singleEye)
            {
                var eye = pla ? "eff_eye_01" : "eff_eye01";
                var otherEye = pla ? "eff_eye_02" : "eff_eye02";
                if (!entries.ContainsKey(eye) || entries.ContainsKey(otherEye))
                    throw new InvalidDataException("Single-eye effect attachment no longer matches its source contract.");
                mapping["EffEye02"] = eye;
            }
            var result = mapping.Select(pair => Convert(pair.Key, pair.Value)).ToList();
            if (airborneOffset != 0)
            {
                var face = Convert("EffMouth01", "eff_face01");
                if (face.Parent != aliases["head"]) throw new InvalidDataException("Airborne mouth attachment must follow the head.");
                result[result.FindIndex(locator => locator.Name == "EffMouth01")] = face with { Rotation = Quaternion.Identity };
                for (var i = 0; i < result.Count; i++)
                    if (result[i].Parent == aliases["origin"])
                        result[i] = result[i] with { Translation = result[i].Translation + new Vector3(0, airborneOffset, 0) };
            }
            if (mouthPosition is { } mouth)
            {
                // A feeding target fixed to Origin cannot follow the face. The meal
                // camera needs a mouth attachment in the animated head's local space.
                if (!float.IsFinite(mouth.X) || !float.IsFinite(mouth.Y) || !float.IsFinite(mouth.Z)
                    || !aliases.TryGetValue("head", out var head) || !scene.Rig.Bones.Any(b => b.Name == "head"))
                    throw new InvalidDataException("Z-A mouth attachment requires another target adapter.");
                var reference = Convert("EffMouth01", "eff_headcenter01");
                if (reference.Parent != head) throw new InvalidDataException("Z-A mouth attachment requires another target adapter.");
                result[result.FindIndex(locator => locator.Name == "EffMouth01")] = reference with { Translation = mouth * 100 };
            }
            foreach (var (name, candidates) in new[] { ("EffHand01", new[] { "right_hand" }), ("EffHand02", new[] { "left_hand" }),
                ("EffFoot01", new[] { "right_foot", "right_foot_a" }), ("EffFoot02", new[] { "left_foot", "left_foot_a" }) })
            {
                var parent = candidates.FirstOrDefault(c => scene.Rig.Bones.Any(b => b.Name == c));
                result.Add(parent is null ? Convert(name, pla ? "EffCenter01" : "eff_center01") : new(name, aliases[parent], Vector3.Zero));
            }
            return result.ToArray();
        }
    }
}
