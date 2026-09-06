// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;

namespace KM.Formats.Models;

internal sealed record SwShPreviewRig(PreviewBone[] Bones, Matrix4x4[] World, int[] Skin)
{
    internal static SwShPreviewRig Read(ModelBuffer data)
    {
        var nodes = data.Tables(data.Root, 9, 512);
        var bones = new PreviewBone[nodes.Length];
        var world = new Matrix4x4[nodes.Length];
        var skin = new List<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            Vector3 Vector(int field, Vector3 fallback)
            {
                var at = data.Field(node, field);
                if (at == 0) return fallback;
                var value = new Vector3(data.Float(at), data.Float(at + 4), data.Float(at + 8));
                if (!float.IsFinite(value.X + value.Y + value.Z)) throw new InvalidDataException("Invalid skeleton transform.");
                return value;
            }
            var name = data.Text(node, 0) ?? throw new InvalidDataException("Skeleton node has no identifier.");
            var parentAt = data.Field(node, 2);
            var parent = parentAt == 0 ? 0 : data.I32(parentAt);
            if (parent >= index || parent < -1 || !names.Add(name)) throw new InvalidDataException("Skeleton hierarchy is unsupported.");
            if (data.Value(node, 3) != 0) throw new InvalidDataException("Skeleton billboard is unsupported.");
            var scalePivot = Vector(8, Vector3.Zero);
            var rotationPivot = Vector(9, Vector3.Zero);
            var scale = Vector(5, Vector3.One);
            var angles = Vector(6, Vector3.Zero);
            var translation = Vector(7, Vector3.Zero);
            var rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(angles.X)
                * Matrix4x4.CreateRotationY(angles.Y) * Matrix4x4.CreateRotationZ(angles.Z));
            var compensate = data.Field(node, 4) is var flag && flag != 0 && data.U8(flag) != 0;
            var compensation = Matrix4x4.Identity;
            if (compensate && parent >= 0)
            {
                var s = bones[parent].Scale;
                if (s.Any(value => Math.Abs(value) < 0.000001f)) throw new InvalidDataException("Skeleton parent scale is singular.");
                compensation = Matrix4x4.CreateScale(1 / s[0], 1 / s[1], 1 / s[2]);
            }
            var local = Matrix4x4.CreateTranslation(-scalePivot) * Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateTranslation(scalePivot - rotationPivot) * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(rotationPivot) * compensation * Matrix4x4.CreateTranslation(translation);
            translation = local.Translation;
            world[index] = local * (parent < 0 ? Matrix4x4.Identity : world[parent]);
            if (!Matrix4x4.Invert(world[index], out var inverse)) throw new InvalidDataException("Skeleton bind transform is singular.");
            bones[index] = new(name, parent, index, compensate, [scale.X, scale.Y, scale.Z],
                [rotation.X, rotation.Y, rotation.Z, rotation.W], [translation.X, translation.Y, translation.Z],
                [inverse.M11, inverse.M12, inverse.M13, inverse.M14, inverse.M21, inverse.M22, inverse.M23, inverse.M24,
                 inverse.M31, inverse.M32, inverse.M33, inverse.M34, inverse.M41, inverse.M42, inverse.M43, inverse.M44]);
            var skinAt = data.Field(node, 10);
            if (skinAt == 0 || data.U8(skinAt) != 0) skin.Add(index);
        }
        return new(bones, world, skin.ToArray());
    }
}
