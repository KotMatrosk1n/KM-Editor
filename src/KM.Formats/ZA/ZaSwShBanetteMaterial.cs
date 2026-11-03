// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using KM.Formats.Models;

namespace KM.Formats.ZA;

/// <summary>Qualifies Mega Banette's authored body and eye emission consumers.</summary>
public static class ZaSwShBanetteMaterial
{
    public static void RequireSource(byte[] materials, PreviewScene scene)
    {
        if (Convert.ToHexString(SHA256.HashData(materials)) is not
            ("B09C195F1647AED847C90DBC375A129186692677F93FE446561408FFA9242829"
            or "7F1F31613C610141755E582CC0E0F8E2A0741C1415AE4E3E18287DC0E35DFD43")
            || scene.Primitives.Any(p => !p.Name.StartsWith("pm0354_51_00_", StringComparison.Ordinal)))
            throw new InvalidDataException("Mega Banette emission requires its qualified source palettes.");
        foreach (var (name, mesh, vertices, used, indices, hash) in new[]
        {
            ("body_b_01", "body", 957, 957, 4260, "D9A5A5E82CA32BD31EA672E8BDBA595E89F8A722CBC7607C04530FDFBDDE88FD"),
            ("body_b_02", "suit", 340, 340, 1296, "972BD74328379E51FA35560EAE964E51AA4C5F1A09A202CBAA983D147C03ECA7"),
            ("r_eye_a", "eye_a", 68, 34, 171, "82E64661BC4DE619BF94CEBF4F9BD25BBB310EF6700EFBC436D7F2E62AF3669A"),
            ("l_eye_a", "eye_a", 68, 34, 171, "19E414D599FD666342C873E18F0FAE8884B9927255AB7DEE288CBB16330719EA"),
            ("eye_b", "eye_b", 68, 68, 342, "FA63A120F86EE8E01EBD016ACECBF806FD15EDF9EAE499CAA8CF5828739AC813"),
        })
        {
            var sections = scene.Primitives.Where(p => p.Material.Name == name).ToArray();
            if (sections.Length != 1 || sections[0].Name != $"pm0354_51_00_{mesh}_mesh_shape"
                || sections[0].Material.Alpha != "Opaque"
                || sections[0].Vertices.Length != vertices * 16 && sections[0].Vertices.Length != used * 16
                || sections[0].Indices.Length != indices || sections[0].Indices.Any(i => i >= sections[0].Vertices.Length / 16)
                || sections[0].Indices.Distinct().Count() != used)
                throw new InvalidDataException("Mega Banette emission geometry requires another adapter.");
            // ZA compacts each section's shared vertex buffer and folds UV tiles.
            // Pin the indexed stream so both representations retain every original
            // triangle, attribute and sample, without depending on unused vertices.
            var part = sections[0];
            var stream = part.Indices.SelectMany(i => part.Vertices.Skip(checked((int)i * 16)).Take(16)
                .Select((v, c) => c is 6 or 7 ? v - MathF.Floor(v) : v)).ToArray();
            if (Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(stream.AsSpan()))) != hash)
                throw new InvalidDataException("Mega Banette emission geometry differs from its authored triangle stream.");
        }
    }
}
