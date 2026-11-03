// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace KM.Formats.SwSh;

/// <summary>Source qualification carried from a validated normal bake to its native consumers.</summary>
public sealed class SwShNormalTileQualification
{
    private readonly string mesh;
    private readonly string material;
    internal SwShNormalTileQualification(string mesh, string material) { this.mesh = mesh; this.material = material; }
    internal bool Matches(string name, Vector2 scale) => scale == new Vector2(2, 4)
        && (name == material || name.StartsWith(material + "_mesh", StringComparison.Ordinal));
    internal bool Matches(string meshName, string name, Vector2 scale) => meshName == mesh && Matches(name, scale);
}
