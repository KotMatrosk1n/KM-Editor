// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.SV;

/// <summary>Keeps every authored mouse face state at its original texel density.</summary>
public static class SvSwShMouseFaceAtlas
{
    private static int Divisions(string name) => name == "body_c_03" || name.StartsWith("body_c_03_mesh", StringComparison.Ordinal) ? 4
        : name is "body_a_03" or "body_b_03" || name.StartsWith("body_a_03_mesh", StringComparison.Ordinal)
            || name.StartsWith("body_b_03_mesh", StringComparison.Ordinal) ? 2 : 0;

    public static SwShStaticUvAtlas? For(byte[] materials, PreviewPrimitive primitive)
    {
        var divisions = Divisions(primitive.Material.Name);
        if (divisions == 0) return null;
        SvSwShMouseNormalTile.Require(materials, primitive, new Vector2(2, 4));
        var maximumOffset = divisions == 2 ? .25f : 0;
        foreach (var index in primitive.Indices.Distinct())
        {
            var at = checked((int)index * 16);
            var u = primitive.Vertices[at + 6]; var v = 1 - primitive.Vertices[at + 7];
            if (!float.IsFinite(u + v) || u <= 0 || u + .5f >= 1 || v <= 0 || v + maximumOffset >= 1f / divisions)
                throw new InvalidDataException("Mouse face states exceed their qualified color atlas.");
        }
        return new(1, divisions, 0, 0);
    }

    public static void RequireAnimation(byte[] animation)
    {
        var source = new ModelBuffer(animation);
        foreach (var node in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(node, 4); if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var divisions = Divisions(source.Text(material, 0) ?? ""); if (divisions == 0) continue;
                if (source.Tables(material, 1, 256).Length != 0)
                    throw new InvalidDataException("Mouse face scalar animation needs another color atlas adapter.");
                foreach (var parameter in source.Tables(material, 2, 256))
                {
                    if (source.Text(parameter, 0) != "UVScaleOffset")
                        throw new InvalidDataException("Mouse face animation needs another color atlas adapter.");
                    var channels = source.Table(parameter, 1);
                    for (var component = 0; component < 4; component++)
                    {
                        var curve = source.Table(channels, component);
                        var keys = source.Tables(curve, 0, 18000);
                        if (keys.Length == 0) throw new InvalidDataException("Mouse face coordinates are incomplete.");
                        foreach (var key in keys)
                        {
                            float Value(int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
                            var value = Value(1);
                            if (!float.IsFinite(value) || Value(2) != 0 || Value(3) != 0
                                || (component < 2 ? value != 1 : component == 2 ? value is < 0 or > .5f
                                    : value < 0 || value > (divisions == 2 ? .25f : 0)))
                                throw new InvalidDataException("Mouse face coordinates exceed their qualified animation bounds.");
                        }
                    }
                }
            }
        }
    }

    public static PreviewMaterialTrack Transform(PreviewMaterialTrack track)
    {
        var divisions = Divisions(track.Material);
        if (divisions == 0) return track;
        if (track.Parameter != "UVScaleOffset" || track.Channels.Length != 4
            || track.Channels.Any(c => c.Length == 0))
            throw new InvalidDataException("Mouse face animation has an unsupported atlas consumer.");
        return track with { Channels = track.Channels.Select((keys, component) => keys.Select(key => key with
        {
            Value = component == 1 ? [key.Value[0] * divisions]
                : component == 3 ? [key.Value[0] + (divisions - 1f) / divisions] : key.Value.ToArray(),
        }).ToArray()).ToArray() };
    }
}
