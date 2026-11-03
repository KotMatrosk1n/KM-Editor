// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.ZA;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private sealed record FresnelPalette(byte[] Materials,
            IReadOnlyDictionary<string, ZaSwShFresnelMaterial.Settings> Settings,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Images,
            IReadOnlyDictionary<string, (float Base, float Layer)> Defaults,
            IReadOnlyDictionary<string, int> ProbeWidths);

        private static Dictionary<string, ZaSwShFresnelMaterial.Settings> FresnelSettings(byte[] bytes)
        {
            var source = new ModelBuffer(bytes);
            return source.Tables(source.Root, 1, 128)
                .Where(m => source.Tables(m, 1, 16).Any(s => source.Text(s, 0) == "FresnelEffect"))
                .ToDictionary(m => source.Text(m, 0)!, m => ZaSwShFresnelMaterial.ReadSettings(bytes, source.Text(m, 0)!), StringComparer.Ordinal);
        }

        private PreviewScene RequireFresnelGeometry(string modelPath, PreviewScene scene, byte[] sourceMaterials,
            IReadOnlyDictionary<string, ZaSwShFresnelMaterial.Settings> settings)
        {
            if (settings.Count == 0) return scene;
            var used = new HashSet<string>(StringComparer.Ordinal);
            var modelBytes = Donor(modelPath);
            var model = new ModelBuffer(modelBytes);
            var meshes = model.Tables(model.Root, 1, 16);
            var lods = model.Tables(model.Root, 4, 16);
            var selected = lods.Length == 0 ? [0] : lods.Select(l => model.Tables(l, 0, 16).FirstOrDefault())
                .Where(t => t != 0).Select(t => checked((int)model.Value(t, 0))).Distinct().ToArray();
            foreach (var index in selected)
            {
                if ((uint)index >= meshes.Length) throw new InvalidDataException("ZA gem detail mesh is unavailable.");
                var meshPath = TrinityPreviewReader.Resolve(modelPath, model.Text(meshes[index], 0)!);
                var meshBytes = Donor(meshPath);
                var mesh = new ModelBuffer(meshBytes);
                foreach (var shape in mesh.Tables(mesh.Root, 1, 128))
                {
                    var names = mesh.Tables(shape, 4, 128).Select(p => mesh.Text(p, 3)!).Where(settings.ContainsKey).ToArray();
                    if (names.Length == 0) continue;
                    var declarations = mesh.Tables(shape, 3, 128);
                    if (declarations.Length == 0) throw new InvalidDataException("ZA gem source declaration is missing.");
                    var attributes = mesh.Tables(declarations[0], 0, 32);
                    void Require(int kind, int set, int format)
                    {
                        var matches = attributes.Where(a => mesh.Value(a, 1) == kind && mesh.Value(a, 2) == set).ToArray();
                        if (matches.Length != 1 || mesh.Value(matches[0], 0) != 0 || mesh.Value(matches[0], 3) != format)
                            throw new InvalidDataException("ZA gem requires its authored source normal, tangent and UV attributes.");
                    }
                    Require(1, 0, 51); Require(2, 0, 43); Require(3, 0, 43); Require(6, 0, 48);
                    if (names.Any(n => settings[n].SecondaryUv)) Require(6, 1, 48);
                    used.UnionWith(names);
                }
            }
            if (!used.SetEquals(settings.Keys)) throw new InvalidDataException("ZA gem source material has no selected geometry consumer.");
            foreach (var primitive in scene.Primitives.Where(p => settings.ContainsKey(p.Material.Name)))
            {
                var count = primitive.Vertices.Length / 16;
                if (primitive.Tangent0?.Length != count * 4
                    || settings[primitive.Material.Name].SecondaryUv && primitive.SecondaryUv?.Length != count * 2)
                    throw new InvalidDataException("ZA gem decoded source attributes are incomplete.");
            }
            return scene;
        }

        private FresnelPalette FresnelTextures(byte[] materials, string materialPath, bool rare,
            IReadOnlyDictionary<string, ZaSwShFresnelMaterial.Settings> settings, RestorationTexturePool pool)
        {
            var images = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            var defaults = new Dictionary<string, (float Base, float Layer)>(StringComparer.Ordinal);
            var widths = new Dictionary<string, int>(StringComparer.Ordinal);
            var source = new ModelBuffer(materials);
            foreach (var (name, value) in settings)
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var binding in ZaSwShFresnelMaterial.TextureBindings(materials, name, value.MaskedEmission))
                {
                    var bytes = Donor(TrinityPreviewReader.Resolve(materialPath, binding.FileName));
                    var b = new ModelBuffer(bytes);
                    int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(at, 8)));
                    if (!b.Slice(0, 4).SequenceEqual("BNTX"u8) || b.U32(36) != 1)
                        throw new InvalidDataException("ZA gem requires its single authored texture container.");
                    var identity = Pointer(Pointer(Pointer(40)) + 96);
                    var original = System.Text.Encoding.UTF8.GetString(b.Slice(identity + 2, b.U16(identity)));
                    if (binding.Cube)
                    {
                        var cube = ModelCubeTexture.Read(bytes);
                        for (var face = 0; face < 6; face++) _ = cube.FacePixels(face);
                        widths.Add(name, cube.Width);
                    }
                    var renamed = ModelTextureIdentity.MaterialName(original, images.Count, 'g', rare ? 's' : 'n');
                    names.Add(binding.SourceRole, pool.Add(renamed, ModelTextureIdentity.Rename(bytes, original, renamed)));
                }
                images.Add(name, names);
                var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
                float Scalar(string parameter)
                {
                    var row = source.Tables(material, 4, 256).Single(p => source.Text(p, 0) == parameter);
                    var at = source.Field(row, 1); return at == 0 ? 0 : source.Float(at);
                }
                defaults.Add(name, (Scalar("EmissionIntensity"), Scalar("EmissionIntensityLayer1")));
            }
            return new(materials, settings, images, defaults, widths);
        }

        private static Dictionary<string, (ZaSwShFresnelMaterial.Settings Settings, int Width)> FresnelPrograms(
            FresnelPalette normal, FresnelPalette rare)
        {
            if (!normal.Settings.Keys.Order(StringComparer.Ordinal).SequenceEqual(rare.Settings.Keys.Order(StringComparer.Ordinal))
                || normal.Settings.Any(p => rare.Settings[p.Key] != p.Value || normal.ProbeWidths[p.Key] != rare.ProbeWidths[p.Key]))
                throw new InvalidDataException("ZA gem palettes disagree on source selectors or probe dimensions.");
            return normal.Settings.GroupBy(p => p.Value.Program, StringComparer.Ordinal).ToDictionary(g => g.Key, g =>
            {
                var widths = g.Select(p => normal.ProbeWidths[p.Key]).Distinct().ToArray();
                if (widths.Length != 1) throw new InvalidDataException("ZA gem shared program requires identical probe dimensions.");
                return (g.First().Value, widths[0]);
            }, StringComparer.Ordinal);
        }

        private static byte[] FresnelShader(byte[] parent, string extension, ZaSwShFresnelMaterial.Settings settings, int width)
            => extension == ".bnsh_vsh"
                ? SwShZaFresnelShader.Vertex(parent, settings.LayerUv, settings.AmbientUv, settings.SourceVariant, settings.MaskUv)
                : SwShZaFresnelShader.Fragment(parent, settings.LayerUv, settings.AmbientUv, settings.SourceVariant, settings.MaskUv, width);

        // A shared animation bank may reset static palette defaults. Reject a
        // changed default with authored curves until its per-palette ownership
        // has an explicit adapter, rather than changing an authored constant key.
        private static void RequireFresnelPaletteAnimation(byte[] bytes, FresnelPalette normal, FresnelPalette rare)
        {
            var source = new ModelBuffer(bytes);
            foreach (var track in source.Tables(source.Root, 1, 512))
            {
                var timeline = source.Table(track, 4);
                if (timeline == 0) continue;
                foreach (var material in source.Tables(timeline, 2, 128))
                {
                    var name = source.Text(material, 0)!;
                    if (!normal.Defaults.TryGetValue(name, out var n)) continue;
                    var r = rare.Defaults[name];
                    foreach (var scalar in source.Tables(material, 1, 256))
                        if (source.Text(scalar, 0) is "EmissionIntensity" && n.Base != r.Base
                            || source.Text(scalar, 0) is "EmissionIntensityLayer1" && n.Layer != r.Layer)
                            throw new InvalidDataException("ZA gem palette default and authored curve need separate animation ownership.");
                }
            }
        }
    }
}
