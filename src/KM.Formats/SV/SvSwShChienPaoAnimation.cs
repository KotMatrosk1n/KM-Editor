// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Excludes unbound animation channels from the qualified Chien-Pao body.</summary>
public sealed class SvSwShChienPaoAnimation
{
    public SvSwShChienPaoAnimation(byte[] palette, PreviewScene scene)
    {
        if (Convert.ToHexString(SHA256.HashData(palette)) is not
            ("53D369341FA127435CF428AF84FA739822F3196F05280F1FB200D5965CCFDC25" or
             "CE390CA5752FD886397939A2F28F41A6BF9254CCB8EB20A2F1AFC3B5CC65B51B"))
            throw new InvalidDataException("Chien-Pao's unbound animation requires its qualified source palette.");
        var parts = scene.Primitives.Where(p => p.Material.Name == "body_a_01").ToArray();
        if (parts.Length != 1 || parts[0].Name != "pm1099_00_00_body_mesh_shape"
            || parts[0].Vertices.Length != 4932 * 16 || parts[0].Indices.Length != 624)
            throw new InvalidDataException("Chien-Pao's one-layer body geometry changed.");
        // Standard key 0880007B:00000000, dynamic 00004001: the source
        // fragment reads only CB8[4..7] for UVScaleOffset. UVScaleOffset3
        // has no material binding, texture consumer, or shader read. Its vertex
        // stage never reads CB8; DisplacementHeight has no consumer either.
    }

    public byte[] Read(byte[] bytes)
    {
        var source = new ModelBuffer(bytes);
        var replacements = new List<(int Material, int Field, int[] Parameters)>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4);
            if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            foreach (var (field, parameter) in new[] { (1, "DisplacementHeight"), (2, "UVScaleOffset3") })
            {
                var parameters = source.Tables(material, field, 256);
                if (!parameters.Any(p => source.Text(p, 0) == parameter)) continue;
                if (source.Text(material, 0) != "body_a_01")
                    throw new InvalidDataException("An additional Chien-Pao UV channel requires an adapter.");
                replacements.Add((material, field, parameters.Where(p => source.Text(p, 0) != parameter).ToArray()));
            }
        }
        if (replacements.Count == 0) return bytes;
        var writer = new ModelTableBuilder(bytes);
        var retained = new List<(int Slot, int Target)>();
        foreach (var (material, field, parameters) in replacements)
        {
            var slots = writer.Vector(material, field, parameters.Length);
            for (var i = 0; i < slots.Length; i++) retained.Add((slots[i], parameters[i]));
        }
        var copy = writer.Append(bytes);
        foreach (var (slot, target) in retained) writer.Pointer(slot, copy + target);
        return writer.Finish(source.Root);
    }
}
