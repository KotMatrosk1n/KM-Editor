// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

/// <summary>Removes static source detail nodes replaced by native visibility nodes.</summary>
public sealed record SvSwShDetailMeshHierarchy(PreviewScene Scene, IReadOnlySet<string> RemovedNames)
{
    public static SvSwShDetailMeshHierarchy Remove(PreviewScene scene, int detailLevels)
    {
        if (detailLevels is < 1 or > 4 || scene.Rig.Clip is not null || scene.Primitives.Count == 0)
            throw new InvalidDataException("Source detail hierarchy contract is invalid.");
        var names = new HashSet<string>(StringComparer.Ordinal) { "lod_group" };
        for (var lod = 0; lod < detailLevels; lod++)
        {
            names.Add("lod" + lod);
            foreach (var name in scene.Primitives.Select(p => p.Name).Distinct(StringComparer.Ordinal))
            {
                if (!name.EndsWith("_mesh_shape", StringComparison.Ordinal))
                    throw new InvalidDataException("Source detail mesh identity is unsupported.");
                names.Add(name[..^6] + (lod == 0 ? "" : "_lod" + lod));
            }
        }
        var bones = scene.Rig.Bones;
        if (bones.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != bones.Length
            || names.Any(name => !bones.Any(b => b.Name == name)))
            throw new InvalidDataException("Source detail hierarchy is missing or ambiguous.");
        var mapping = new Dictionary<int, int>();
        for (var i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            if (b.Parent < -1 || b.Parent >= i)
                throw new InvalidDataException("Source detail hierarchy parent is invalid.");
            if (!names.Contains(b.Name)) { mapping.Add(i, mapping.Count); continue; }
            if (i == 0 || b.Joint != -1 || !b.Scale.SequenceEqual(new float[] { 1, 1, 1 })
                || !b.Rotation.SequenceEqual(new float[] { 0, 0, 0, 1 })
                || !b.Translation.SequenceEqual(new float[] { 0, 0, 0 }))
                throw new InvalidDataException("Source detail hierarchy has a joint or transform.");
        }
        if (mapping.Any(pair => bones[pair.Key].Parent >= 0 && !mapping.ContainsKey(bones[pair.Key].Parent)))
            throw new InvalidDataException("Source detail hierarchy contains a retained descendant.");
        var retained = mapping.Select(pair => bones[pair.Key] with
        {
            Parent = bones[pair.Key].Parent < 0 ? -1 : mapping[bones[pair.Key].Parent],
        }).ToArray();
        return new(scene with { Rig = scene.Rig with { Bones = retained } }, names);
    }

    public void RequireUnanimated(byte[] skeletonAnimation)
    {
        var source = new ModelBuffer(skeletonAnimation);
        var skeleton = source.Table(source.Root, 1);
        if (source.Tables(skeleton, 0, 4096).Any(t => RemovedNames.Contains(source.Text(t, 0) ?? "")))
            throw new InvalidDataException("Source detail hierarchy contains an animated mesh node.");
    }
}
