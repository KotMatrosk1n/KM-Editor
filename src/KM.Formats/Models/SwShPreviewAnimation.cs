// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

public static class SwShPreviewAnimation
{
    public static PreviewClip Read(ModelBuffer data, PreviewClip clip, ICollection<string> warnings,
        IReadOnlySet<string>? fireMaterials = null, IReadOnlySet<string>? emissionMaterials = null)
    {
        var visibility = new List<PreviewVisibility>();
        var materials = new List<PreviewMaterialTrack>();
        var visible = data.Table(data.Root, 3);
        if (visible != 0)
            foreach (var track in data.Tables(visible, 0, 512))
            {
                var name = data.Text(track, 0) ?? throw new InvalidDataException("Visibility target is missing.");
                visibility.Add(new(name, Keys(data, track, 1, 1, true, clip.Frames)[0]));
            }
        var materialRoot = data.Table(data.Root, 2);
        if (materialRoot != 0)
            foreach (var material in data.Tables(materialRoot, 0, 128))
            {
                var name = data.Text(material, 0) ?? throw new InvalidDataException("Material animation target is missing.");
                var fire = fireMaterials?.Contains(name) == true;
                var effects = new Dictionary<string, PreviewKey[][]>(StringComparer.Ordinal);
                foreach (var vector in data.Tables(material, 3, 256))
                {
                    if (fire && SwShPreviewFire.ColorSlot(data.Text(vector, 0) ?? "") is var slot && slot >= 0)
                    {
                        var color = Keys(data, vector, 1, 3, false, clip.Frames);
                        materials.Add(new(name, $"FireValue{slot}", [color[0], color[1], color[2], []]));
                        continue;
                    }
                    if (data.Text(vector, 0) != "ConstantColor0") { warnings.Add("materialAnimationUnsupported"); continue; }
                    var channels = Keys(data, vector, 1, 3, false, clip.Frames);
                    materials.Add(new(name, "BaseColor", [channels[0], channels[1], channels[2], []]));
                }
                PreviewKey[][] uv = [[], [], [], []];
                PreviewKey[][] underlayUv = [[], [], [], []];
                PreviewKey[][] origins = [[], [], [], []];
                foreach (var scalar in data.Tables(material, 2, 256))
                {
                    if (emissionMaterials?.Contains(name) == true && data.Text(scalar, 0) is "EmissionMaskVal" or "OnGameEmissionVal")
                    {
                        var target = data.Text(scalar, 0) == "EmissionMaskVal" ? "EmissionGain" : "EmissionScale";
                        materials.Add(new(name, target, [Keys(data, scalar, 1, 1, false, clip.Frames)[0], [], [], []]));
                        continue;
                    }
                    if (fire)
                    {
                        var scalarName = data.Text(scalar, 0) ?? "";
                        var value = SwShPreviewFire.ScalarSlot(scalarName);
                        var transform = SwShPreviewFire.UvChannel(scalarName);
                        var target = value.Slot >= 0 ? $"FireValue{value.Slot}" : transform.Parameter;
                        var component = value.Slot >= 0 ? value.Channel : transform.Channel;
                        if (component >= 0)
                        {
                            if (!effects.TryGetValue(target, out var channels)) effects.Add(target, channels = [[], [], [], []]);
                            channels[component] = Keys(data, scalar, 1, 1, false, clip.Frames)[0];
                            continue;
                        }
                    }
                    var channel = data.Text(scalar, 0) switch
                    {
                        "ColorUVScaleU" => 0,
                        "ColorUVScaleV" => 1,
                        "ColorUVTranslateU" => 2,
                        "ColorUVTranslateV" => 3,
                        "Layer1UVScaleU" => 4,
                        "Layer1UVScaleV" => 5,
                        "Layer1UVTranslateU" => 6,
                        "Layer1UVTranslateV" => 7,
                        "ColorBaseU" => 8,
                        "ColorBaseV" => 9,
                        "Layer1BaseU" => 10,
                        "Layer1BaseV" => 11,
                        _ => -1
                    };
                    if (channel < 0) { warnings.Add("materialAnimationUnsupported"); continue; }
                    (channel < 4 ? uv : channel < 8 ? underlayUv : origins)[channel % 4] = Keys(data, scalar, 1, 1, false, clip.Frames)[0];
                }
                if (uv.Any(channel => channel.Length > 0)) materials.Add(new(name, "UVScaleOffset", uv));
                if (underlayUv.Any(channel => channel.Length > 0)) materials.Add(new(name, "UnderlayUV", underlayUv));
                if (origins.Any(channel => channel.Length > 0)) materials.Add(new(name, "UvOrigins", origins));
                foreach (var (parameter, channels) in effects) materials.Add(new(name, parameter, channels));
                if (data.Tables(material, 1, 256).Length > 0)
                    warnings.Add("materialAnimationUnsupported");
            }
        return clip with { Visibility = visibility.ToArray(), Materials = materials.ToArray() };
    }

    private static PreviewKey[][] Keys(ModelBuffer data, int track, int field, int components, bool bytes, int frames)
    {
        var at = data.Field(track, field);
        var kind = at == 0 ? 0 : data.U8(at);
        if (kind == 0) return Enumerable.Range(0, components).Select(_ => Array.Empty<PreviewKey>()).ToArray();
        if (kind > 4) throw new InvalidDataException("Animation curve encoding is unsupported.");
        var table = data.Table(track, field + 1);
        if (bytes && kind > 1)
        {
            // Visibility samples are packed least-significant-bit first, with
            // sparse frame indices stored separately from the sample bitset.
            var (packed, packedCount) = data.Vector(table, kind == 2 ? 0 : 1, 1, 2250);
            var (indices, samples) = kind == 2 ? (0, Math.Min(frames, packedCount * 8)) : data.Vector(table, 0, kind == 3 ? 2 : 1, 18000);
            if (samples == 0 || packedCount != (samples + 7) / 8)
                throw new InvalidDataException("Visibility samples are incomplete.");
            var visibility = new PreviewKey[samples];
            for (var i = 0; i < samples; i++)
            {
                var frame = kind == 2 ? i : kind == 3 ? data.U16(indices + i * 2) : data.U8(indices + i);
                if (frame >= frames || (i > 0 && frame < visibility[i - 1].Frame))
                    throw new InvalidDataException("Visibility keys are out of order.");
                visibility[i] = new(frame, [(data.U8(packed + i / 8) >> (i % 8)) & 1]);
            }
            return [visibility];
        }
        var stride = bytes ? 1 : 4 * components;
        var (start, count) = kind == 1 ? (data.Field(table, 0), 1) : data.Vector(table, kind == 2 ? 0 : 1, stride, 18000);
        var (keys, keyCount) = kind >= 3 ? data.Vector(table, 0, kind == 3 ? 2 : 1, 18000) : (0, count);
        if (count == 0 || count != keyCount) throw new InvalidDataException("Animation keys are incomplete.");
        var result = Enumerable.Range(0, components).Select(_ => new PreviewKey[count]).ToArray();
        for (var i = 0; i < count; i++)
        {
            var frame = kind == 1 ? 0 : kind == 2 ? i : kind == 3 ? data.U16(keys + i * 2) : data.U8(keys + i);
            if (frame >= frames || (i > 0 && frame < result[0][i - 1].Frame)) throw new InvalidDataException("Animation keys are out of order.");
            for (var c = 0; c < components; c++)
            {
                var value = start == 0 ? 0 : bytes ? data.U8(start + i) : data.Float(start + i * stride + c * 4);
                if (!float.IsFinite(value) || Math.Abs(value) > 1_000_000) throw new InvalidDataException("Invalid animation value.");
                result[c][i] = new(frame, [bytes ? value == 0 ? 0 : 1 : value]);
            }
        }
        return result;
    }
}
