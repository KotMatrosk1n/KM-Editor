// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Removes the static cover channel absent from Miraidon's battle model.</summary>
public sealed class SvSwShMiraidonVisibility
{
    public SvSwShMiraidonVisibility(byte[] model, byte[] mesh)
    {
        if (Convert.ToHexString(SHA256.HashData(model)) != "AD5829443BD3D3293A1BCB1D0FCA62C1D508C6B090232AF26C23BCDB36ABC091"
            || Convert.ToHexString(SHA256.HashData(mesh)) != "8FBE617013F5EA912CA531EAF5DC4EDA70F23EF91705BCD63B9D727009D5D859")
            throw new InvalidDataException("Miraidon's absent cover channel requires its qualified battle model.");
        // The model declares twenty primary shapes and corresponding LOD1/2
        // shapes. None has this cover target; every supplied battle animation
        // retains the same frame-zero visible key from the source rig.
    }

    public PreviewClip Read(PreviewClip clip)
    {
        const string cover = "pm1103_11_00_cover_mesh_shape";
        foreach (var track in clip.Visibility.Where(t => t.Name == cover))
            if (track.Keys.Length != 1 || track.Keys[0] is not { Frame: 0, Step: false }
                || track.Keys[0].Value is not [1])
                throw new InvalidDataException("Miraidon's absent cover visibility changed.");
        return clip with { Visibility = clip.Visibility.Where(t => t.Name != cover).ToArray() };
    }
}
