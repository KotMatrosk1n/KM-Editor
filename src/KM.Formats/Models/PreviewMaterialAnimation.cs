// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

public static class PreviewMaterialAnimation
{
    public static PreviewClip Read(ModelBuffer data, PreviewClip clip, ICollection<string> warnings, bool highlightAnimation = false)
    {
        var visibility = new List<PreviewVisibility>();
        var materials = new List<PreviewMaterialTrack>();
        var config = data.Table(data.Root, 0);
        var rate = data.Value(config, 2, (uint)clip.Rate);
        var ratio = rate == 0 ? 1f : clip.Rate / (float)rate;
        foreach (var track in data.Tables(data.Root, 1, 512))
        {
            var name = data.Text(track, 0) ?? "";
            var visible = data.Table(track, 5);
            if (visible != 0)
            {
                var info = data.Table(visible, 2);
                var kindAt = data.Field(info, 0); var kind = kindAt == 0 ? 0 : data.U8(kindAt);
                var values = data.Table(info, 1);
                if (kind == 1)
                    visibility.Add(new(name, [new(0, [data.Field(values, 0) is var v && v != 0 && data.U8(v) != 0 ? 1 : 0])]));
                else if (kind is >= 2 and <= 4)
                {
                    var (start, count) = data.Vector(values, kind == 2 ? 0 : 1, 1, 18000);
                    var (frames, frameCount) = kind == 2 ? (0, count) : data.Vector(values, 0, kind == 3 ? 2 : 1, 18000);
                    if (frameCount != count) throw new InvalidDataException("Visibility keys are incomplete.");
                    var keys = new PreviewKey[count];
                    for (var i = 0; i < count; i++)
                    {
                        var frame = (kind == 2 ? i : kind == 3 ? data.U16(frames + i * 2) : data.U8(frames + i)) * ratio;
                        keys[i] = new(frame, [data.U8(start + i) == 0 ? 0 : 1]);
                    }
                    visibility.Add(new(name, keys));
                }
                else warnings.Add("visibilityUnsupported");
            }
            var timeline = data.Table(track, 4);
            if (timeline != 0) foreach (var material in data.Tables(timeline, 2, 128))
            {
                var materialName = data.Text(material, 0) ?? "";
                foreach (var parameter in data.Tables(material, 2, 256))
                {
                    var parameterName = data.Text(parameter, 0) ?? "";
                    if (parameterName is not ("UVScaleOffset" or "BaseColor" or "BaseColorLayer1" or "BaseColorLayer2" or "BaseColorLayer3" or "BaseColorLayer4")
                        && !(highlightAnimation && parameterName == "UVScaleOffset1"))
                    {
                        warnings.Add("materialAnimationUnsupported"); continue;
                    }
                    var channels = data.Table(parameter, 1);
                    var result = new PreviewKey[4][];
                    for (var c = 0; c < 4; c++)
                    {
                        var list = data.Table(channels, c);
                        result[c] = list == 0 ? [] : data.Tables(list, 0, 18000).Select(key =>
                        {
                            float Scalar(int field) => data.Field(key, field) is var at && at != 0 ? data.Float(at) : 0;
                            return new PreviewKey(Scalar(0) * ratio, [Scalar(1)]);
                        }).ToArray();
                    }
                    materials.Add(new(materialName, parameterName, result));
                }
                if (data.Tables(material, 1, 256).Length > 0) warnings.Add("materialAnimationUnsupported");
            }
            if (data.Table(track, 6) != 0) warnings.Add("morphUnsupported");
        }
        return clip with { Visibility = visibility.ToArray(), Materials = materials.ToArray() };
    }
}
