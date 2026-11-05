// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private SwShStaticUvAtlas StaticAtlas(PreviewPrimitive primitive, Vector2 scale) => Profile(currentIdentity).CenteredStaticUv
            ? SwShStaticUvAtlas.CenteredRepeat(primitive, scale) : SwShStaticUvAtlas.For(primitive, scale);

        private Vector2? StaticAtlasScale(PreviewPrimitive primitive)
        {
            var profile = Profile(currentIdentity);
            if (profile.StaticUvScales?.TryGetValue(primitive.Name, out var meshScale) == true) return meshScale;
            return null;
        }

        private SwShStaticUvAtlas? StaticAtlas(PreviewPrimitive primitive) => StaticAtlasScale(primitive) is { } scale
            ? StaticAtlas(primitive, scale) : null;

        private void RequireStaticAtlasMaterials(PreviewScene scene, PreviewClip clip)
        {
            if (clip.Materials.Any(t => t.Parameter is not
                ("EmissionIntensityLayer1" or "EmissionIntensityLayer2" or "EmissionIntensityLayer3" or "EmissionIntensityLayer4")
                && scene.Primitives.Any(p => p.Material.Name == t.Material && StaticAtlasScale(p) is not null)))
                throw new InvalidDataException("Animated atlas coordinates require another material adapter.");
        }

        private static PreviewScene UniqueMaterials(PreviewScene scene)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return scene with { Primitives = scene.Primitives.Select((primitive, index) =>
            {
                var name = primitive.Material.Name;
                if (!names.Add(name))
                {
                    name += "_mesh" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!names.Add(name)) throw new InvalidDataException("Converted material name is ambiguous.");
                }
                return primitive with { Material = primitive.Material with { Name = name } };
            }).ToArray() };
        }

        private SwShBakedMaterial?[] BakePalette(PreviewScene scene, byte[] materialBytes, bool rare, string sourcePath,
            RestorationTexturePool textures, bool nativeFlames = false, IReadOnlySet<string>? sourceColorMaterials = null)
        {
            if (Profile(currentIdentity).SourceLayeredAlbedo && (sourceColorMaterials is null || sourceColorMaterials.Count == 0))
                throw new InvalidDataException("Source albedo has no qualified native layer consumer.");
            var sourceMaterials = new ModelBuffer(materialBytes);
            var eyeMaterials = sourceMaterials.Tables(sourceMaterials.Root, 1, 128)
                .Where(material => SwShMaterialBaker.IsEyeMaterial(sourceMaterials, material))
                .Select(material => sourceMaterials.Text(material, 0)!).ToHashSet(StringComparer.Ordinal);
            var shared = new Dictionary<string, SwShBakedMaterial>(StringComparer.Ordinal);
            return scene.Primitives.Select((primitive, index) =>
            {
                if (nativeFlames && primitive.Material.Name == "fire") return null;
                var eye = eyeMaterials.Contains(primitive.Material.Name);
                var cornealHeights = Profile(currentIdentity).CornealEyes && eye
                    ? KM.Formats.SV.SvSwShCornealMaterial.SourceNormalHeights(sourceMaterials,
                        sourceMaterials.Tables(sourceMaterials.Root, 1, 128).Single(m => sourceMaterials.Text(m, 0) == primitive.Material.Name))
                    : (Primary: 1f, Corneal: 1f);
                if (Profile(currentIdentity).LayeredEyes && !Profile(currentIdentity).CornealEyes
                    && eye) return null;
                var normalScale = Profile(currentIdentity).NormalUvScales?.GetValueOrDefault(primitive.Name, Vector2.One) ?? Vector2.One;
                var share = Profile(currentIdentity).SharedMaterialTextures && normalScale == Vector2.One;
                if (share && shared.TryGetValue(primitive.Material.Name, out var previous)) return previous;
                var bakingPrimitive = primitive;
                if (share)
                {
                    var vertices = new List<float>(); var indices = new List<uint>();
                    foreach (var section in scene.Primitives.Where(section => section.Material.Name == primitive.Material.Name
                        && (Profile(currentIdentity).NormalUvScales?.GetValueOrDefault(section.Name, Vector2.One) ?? Vector2.One) == Vector2.One))
                    {
                        var offset = checked((uint)(vertices.Count / 16));
                        indices.AddRange(section.Indices.Select(vertex => checked(vertex + offset)));
                        vertices.AddRange(section.Vertices);
                    }
                    bakingPrimitive = primitive with { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
                }
                Report("textures", (rare ? scene.Primitives.Count : 0) + index, scene.Primitives.Count * 2);
                var material = SwShMaterialBaker.Bake(bakingPrimitive, materialBytes, index,
                    name => Donor(TrinityPreviewReader.Resolve(sourcePath, name)), token, sourceNormalDimensions: Profile(currentIdentity).CompactResources,
                    opaqueEyeSurface: Profile(currentIdentity).OpaqueEyeSurface && eye,
                    cornealEye: Profile(currentIdentity).CornealEyes && eye,
                    cornealNormalHeight: Profile(currentIdentity).CornealNormalHeights?.GetValueOrDefault(primitive.Material.Name, cornealHeights.Corneal) ?? cornealHeights.Corneal,
                    normalUvScale: normalScale,
                    normalUvVerticalTile: Profile(currentIdentity).NormalUvVerticalTiles?.GetValueOrDefault(primitive.Name) ?? 0,
                    authoredAlpha: Profile(currentIdentity).TransparentBody && primitive.Material.Alpha != "Opaque"
                        && !eye
                        || Profile(currentIdentity).AuthoredAlpha && primitive.Material.Name is "l_eye_b" or "l_highlight",
                    staticUvAtlas: StaticAtlas(bakingPrimitive),
                    encodingCache: textureEncoding,
                    unlitSurface: Profile(currentIdentity).LavaLayers && primitive.Material.Name == "fire",
                    cornealPrimaryNormalHeight: Profile(currentIdentity).CornealPrimaryNormalHeights?.GetValueOrDefault(primitive.Material.Name, cornealHeights.Primary) ?? cornealHeights.Primary,
                    preserveSourceColor: sourceColorMaterials?.Contains(primitive.Material.Name) == true
                        || currentIdentity.Species == 733 && currentIdentity.Form == 0
                            && KM.Formats.SV.SvSwShLayerEmission.PreservesToucannonUnlayeredAlbedo(materialBytes, primitive));
                if (share && material.ConflictingSamples != 0)
                    throw new InvalidDataException("Shared material normal coordinates require another target adapter.");
                // Eyes can share a layer mask while having different palette colors.
                // Give each material and palette its own native texture identity.
                (string Name, byte[] Bytes) Add(string original, byte[] bytes, char prefix)
                {
                    var name = ModelTextureIdentity.MaterialName(original, index, prefix);
                    var renamed = ModelTextureIdentity.Rename(bytes, original, name);
                    return (textures.Add(name, renamed), renamed);
                }
                var color = Add(material.ColorName, material.Color, rare ? 'd' : 'c');
                var normal = Add(material.NormalName, material.Normal, rare ? 'o' : 'n');
                var ambient = Add(material.AmbientName, material.Ambient, rare ? 'b' : 'a');
                Report("textures", (rare ? scene.Primitives.Count : 0) + index + 1, scene.Primitives.Count * 2);
                var result = material with { ColorName = color.Name, Color = color.Bytes, NormalName = normal.Name, Normal = normal.Bytes,
                    AmbientName = ambient.Name, Ambient = ambient.Bytes };
                if (share) shared.Add(primitive.Material.Name, result);
                return result;
            }).ToArray();
        }

        private static SwShEffectLocator[] SourceLocators(PreviewScene scene, IReadOnlyDictionary<string, string> aliases, byte[] bytes, Vector3? mouthPosition = null)
        {
            var source = new ModelBuffer(bytes); var nodes = source.Tables(source.Root, 1, 512);
            var transforms = PreviewRigReader.Skeleton(source);
            var entries = transforms.Select((bone, index) => (Bone: bone, Parent: source.Text(nodes[index], 6)))
                .ToDictionary(e => e.Bone.Name, StringComparer.Ordinal);
            SwShEffectLocator Convert(string name, string original)
            {
                if (!entries.TryGetValue(original, out var entry) || entry.Bone.Parent != -1 || entry.Bone.Scale.Any(v => !float.IsFinite(v) || v <= 0)
                    || entry.Parent is null || !scene.Rig.Bones.Any(b => b.Name == entry.Parent))
                    throw new InvalidDataException("S/V effect attachment requires another target adapter.");
                var p = entry.Bone.Translation; var q = entry.Bone.Rotation; var s = entry.Bone.Scale;
                return new(name, aliases[entry.Parent], new(p[0] * 100, p[1] * 100, p[2] * 100))
                { Rotation = new(q[0], q[1], q[2], q[3]), Scale = new(s[0], s[1], s[2]) };
            }
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EffMouth01"] = "eff_mouth01", ["EffOverHead01"] = "eff_overhead01", ["EffEye01"] = "eff_eye01", ["EffEye02"] = "eff_eye02",
                ["EffHeadCenter01"] = "eff_headcenter01", ["EffFront01"] = "eff_front01", ["EffCenter01"] = "eff_center01",
                ["EffShoot01_01"] = "eff_rangeattack01", ["EffShoot01_02"] = "eff_rangeattack02",
                ["EffAttack01_01"] = "eff_attack01", ["EffAttack01_02"] = "eff_attack02",
                ["EffGcloud01"] = "eff_overhead01",
            };
            var result = mapping.Select(pair => Convert(pair.Key, pair.Value)).ToList();
            if (mouthPosition is { } mouth)
            {
                // A feeding target fixed to Origin cannot follow the face. The meal
                // camera needs a mouth attachment in the animated head's local space.
                if (!float.IsFinite(mouth.X) || !float.IsFinite(mouth.Y) || !float.IsFinite(mouth.Z)
                    || !aliases.TryGetValue("head", out var head) || !scene.Rig.Bones.Any(b => b.Name == "head"))
                    throw new InvalidDataException("S/V mouth attachment requires another target adapter.");
                var reference = Convert("EffMouth01", "eff_headcenter01");
                if (reference.Parent != head) throw new InvalidDataException("S/V mouth attachment requires another target adapter.");
                result[result.FindIndex(locator => locator.Name == "EffMouth01")] = reference with { Translation = mouth * 100 };
            }
            foreach (var (name, candidates) in new[] { ("EffHand01", new[] { "right_hand" }), ("EffHand02", new[] { "left_hand" }),
                ("EffFoot01", new[] { "right_foot", "right_foot_a" }), ("EffFoot02", new[] { "left_foot", "left_foot_a" }) })
            {
                var parent = candidates.FirstOrDefault(c => scene.Rig.Bones.Any(b => b.Name == c));
                result.Add(parent is null ? Convert(name, "eff_center01") : new(name, aliases[parent], Vector3.Zero));
            }
            return result.ToArray();
        }
    }
}
