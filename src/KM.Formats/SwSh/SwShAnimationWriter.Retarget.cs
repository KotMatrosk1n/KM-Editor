// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    /// <summary>Transfers local motion deltas while retaining the destination rig's proportions.</summary>
    public static byte[] Retarget(PreviewClip clip, PreviewBone[] sourceBones, PreviewBone[] targetBones,
        IReadOnlyDictionary<string, string> boneMap, IReadOnlyDictionary<string, string>? branchAnchors = null,
        IReadOnlySet<string>? fixedTranslations = null,
        IReadOnlyDictionary<string, string>? translationReferences = null)
    {
        if (clip.Frames is < 1 or > 18000 || targetBones.Length is < 1 or > 255)
            throw new InvalidDataException("Retarget animation exceeds its budget.");
        RequireHierarchy(sourceBones);
        RequireHierarchy(targetBones);
        var source = sourceBones.Select((b, i) => (b.Name, Index: i)).ToDictionary(p => p.Name, p => p.Index, StringComparer.Ordinal);
        var target = targetBones.ToDictionary(b => b.Name, StringComparer.Ordinal);
        if (boneMap.Any(p => !target.ContainsKey(p.Key) || !source.ContainsKey(p.Value)))
            throw new InvalidDataException("Retarget mapping contains an unavailable joint.");
        if (branchAnchors is not null && branchAnchors.Any(p => !boneMap.ContainsKey(p.Key) || !boneMap.ContainsKey(p.Value)))
            throw new InvalidDataException("Retarget branch anchor is unavailable.");
        if (fixedTranslations is not null && fixedTranslations.Any(name => !boneMap.ContainsKey(name)))
            throw new InvalidDataException("Retarget fixed translation joint is unavailable.");
        if (translationReferences is not null && translationReferences.Any(p => !boneMap.ContainsKey(p.Key)
            || !boneMap.ContainsKey(p.Value) || fixedTranslations?.Contains(p.Key) == true))
            throw new InvalidDataException("Retarget translation reference is unavailable or conflicts with a fixed offset.");
        var targetIndices = targetBones.Select((b, i) => (b.Name, Index: i)).ToDictionary(p => p.Name, p => p.Index, StringComparer.Ordinal);
        float ReferenceRatio(string pivot, string reference)
        {
            var index = targetIndices[reference];
            var sourceIndex = source[boneMap[reference]];
            bool Descends(PreviewBone[] bones, int child, int ancestor)
            {
                for (var at = bones[child].Parent; at >= 0; at = bones[at].Parent)
                    if (at == ancestor) return true;
                return false;
            }
            if (!Descends(targetBones, index, targetIndices[pivot])
                || !Descends(sourceBones, sourceIndex, source[boneMap[pivot]])
                || branchAnchors?.ContainsKey(reference) == true)
                throw new InvalidDataException("Retarget translation reference must describe a descendant segment in both rigs.");
            var parent = targetBones[index].Parent;
            var targetLocal = Matrix4x4.Identity;
            for (var at = index; at >= 0; at = targetBones[at].Parent)
            {
                var segment = targetBones[at];
                if (segment.Scale.Any(v => !float.IsFinite(v) || MathF.Abs(v - 1) > .00001f))
                    throw new InvalidDataException("Retarget translation reference requires unit bind scale.");
                targetLocal *= Matrix4x4.CreateFromQuaternion(Rotation(segment.Rotation))
                    * Matrix4x4.CreateTranslation(Vector(segment.Translation));
                parent = segment.Parent;
                if (parent < 0 || boneMap.ContainsKey(targetBones[parent].Name)) break;
            }
            var sourceParent = parent < 0 ? -1 : source[boneMap[targetBones[parent].Name]];
            var sourceLocal = Matrix4x4.Identity;
            var cursor = sourceIndex;
            for (; cursor >= 0 && cursor != sourceParent; cursor = sourceBones[cursor].Parent)
            {
                var segment = sourceBones[cursor];
                if (segment.Scale.Any(v => !float.IsFinite(v) || MathF.Abs(v - 1) > .00001f))
                    throw new InvalidDataException("Retarget translation reference requires unit bind scale.");
                sourceLocal *= Matrix4x4.CreateFromQuaternion(Rotation(segment.Rotation))
                    * Matrix4x4.CreateTranslation(Vector(segment.Translation));
            }
            if (cursor != sourceParent || sourceLocal.Translation.Length() < .00001f)
                throw new InvalidDataException("Retarget translation reference has no compatible source segment.");
            var ratio = targetLocal.Translation.Length() / sourceLocal.Translation.Length();
            if (!float.IsFinite(ratio) || ratio <= 0 || ratio > 10)
                throw new InvalidDataException("Retarget translation reference scale is invalid.");
            return ratio;
        }
        var referenceRatios = translationReferences?.ToDictionary(p => p.Key, p => ReferenceRatio(p.Key, p.Value), StringComparer.Ordinal);
        var branchWorld = branchAnchors is { Count: > 0 } ? Enumerable.Range(0, clip.Frames).Select(_ => new Matrix4x4[targetBones.Length]).ToArray() : null;
        var unitScale = new bool[targetBones.Length];
        var tracks = clip.Tracks.ToDictionary(t => t.Bone);
        Matrix4x4[] World(int? frame)
        {
            var world = new Matrix4x4[sourceBones.Length];
            for (var i = 0; i < sourceBones.Length; i++)
            {
                var bone = sourceBones[i]; var track = frame is null ? null : tracks.GetValueOrDefault(i);
                var position = track is null ? bone.Translation : Sample(track.Translation, frame!.Value, bone.Translation, false);
                var rotation = track is null ? bone.Rotation : Sample(track.Rotation, frame!.Value, bone.Rotation, true);
                world[i] = Matrix4x4.CreateFromQuaternion(Rotation(rotation)) * Matrix4x4.CreateTranslation(Vector(position))
                    * (bone.Parent < 0 ? Matrix4x4.Identity : world[bone.Parent]);
            }
            return world;
        }
        var rest = World(null);
        var poses = Enumerable.Range(0, clip.Frames).Select(f => World(f)).ToArray();
        var writer = new ModelTableBuilder();
        var root = writer.Table(5, absent: new HashSet<int> { 3, 4 });
        var info = writer.Table(3, new Dictionary<int, byte[]>
        {
            [0] = ModelTableBuilder.Integer(clip.Loop ? 1 : 0),
            [1] = ModelTableBuilder.Integer(clip.Frames), [2] = ModelTableBuilder.Integer(clip.Rate),
        });
        writer.Reference(root, 0, info);
        var skeleton = writer.Table(2, absent: new HashSet<int> { 1 }); writer.Reference(root, 1, skeleton);
        var slots = writer.Vector(skeleton, 0, targetBones.Length);
        for (var i = 0; i < targetBones.Length; i++)
        {
            var bone = targetBones[i]; var node = writer.Table(7); writer.Pointer(slots[i], node); writer.Text(node, 0, bone.Name);
            unitScale[i] = true;
            void Record(int frame, Quaternion rotation, Vector3 position, float[] scale)
            {
                unitScale[i] &= scale.All(v => float.IsFinite(v) && MathF.Abs(v - 1) <= .00001f);
                if (branchWorld is not null)
                    branchWorld[frame][i] = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position)
                        * (bone.Parent < 0 ? Matrix4x4.Identity : branchWorld[frame][bone.Parent]);
            }
            void Curve(int field, byte[] values, int count)
            {
                var stride = values.Length / count;
                if (count > 1 && Enumerable.Range(1, count - 1).All(frame => values.AsSpan(frame * stride, stride).SequenceEqual(values.AsSpan(0, stride))))
                { values = values.AsSpan(0, stride).ToArray(); count = 1; }
                writer.Int32(writer.Field(node, field), count == 1 ? 1 : 2);
                var curve = writer.Table(1, sizes: new Dictionary<int, int> { [0] = count == 1 ? values.Length : 4 });
                writer.Reference(node, field + 1, curve);
                if (count == 1) writer.Write(writer.Field(curve, 0), values); else writer.Blob(curve, 0, values, count);
            }
            if (!boneMap.TryGetValue(bone.Name, out var name))
            {
                Curve(1, ModelTableBuilder.Floats(bone.Scale), 1);
                Curve(3, PackRotation(bone.Rotation), 1); Curve(5, ModelTableBuilder.Floats(bone.Translation), 1);
                for (var frame = 0; frame < clip.Frames; frame++) Record(frame, Rotation(bone.Rotation), Vector(bone.Translation), bone.Scale);
                continue;
            }
            var original = sourceBones[source[name]];
            var track = tracks.GetValueOrDefault(source[name]);
            var targetParent = bone.Parent;
            var insertedParent = Matrix4x4.Identity;
            var insertedAnchorOffset = Vector3.Zero;
            var hasInsertedParent = false;
            var branch = branchAnchors is not null && branchAnchors.TryGetValue(bone.Name, out var anchorName)
                ? targetIndices[anchorName] : -1;
            if (branch >= 0)
            {
                var nearest = targetParent;
                while (nearest >= 0 && !boneMap.ContainsKey(targetBones[nearest].Name)) nearest = targetBones[nearest].Parent;
                var at = original.Parent;
                while (at >= 0 && nearest >= 0 && at != source[boneMap[targetBones[nearest].Name]]) at = sourceBones[at].Parent;
                if (nearest < 0 || at >= 0 || nearest == branch)
                    throw new InvalidDataException("Retarget branch anchor does not describe a changed source branch.");
            }
            while (targetParent >= 0 && (branch >= 0 ? targetParent != branch : !boneMap.ContainsKey(targetBones[targetParent].Name)))
            {
                var intermediary = targetBones[targetParent];
                if (intermediary.Scale.Any(v => !float.IsFinite(v) || MathF.Abs(v - 1) > .00001f)
                    || branch >= 0 && !unitScale[targetParent])
                    throw new InvalidDataException("Retarget inserted parent requires unit bind scale.");
                insertedParent *= Matrix4x4.CreateFromQuaternion(Rotation(intermediary.Rotation))
                    * Matrix4x4.CreateTranslation(Vector(intermediary.Translation));
                insertedAnchorOffset = Vector(intermediary.Translation);
                hasInsertedParent = true; targetParent = intermediary.Parent;
            }
            if (branch >= 0 && targetParent != branch)
                throw new InvalidDataException("Retarget branch anchor changes target ancestry.");
            var parentIndex = targetParent < 0 ? -1 : source[boneMap[targetBones[targetParent].Name]];
            if (parentIndex >= 0)
            {
                var at = original.Parent;
                while (at >= 0 && at != parentIndex) at = sourceBones[at].Parent;
                if (at < 0) throw new InvalidDataException("Retarget mapping changes joint ancestry.");
            }
            // Rigid relative poses omit parent scale. Bind scale remains
            // unsupported when collapsing joints; animated scale can transfer
            // through a fully compensated chain with matching anchor scale.
            var compensatedCollapse = false;
            var hasCollapsedSource = original.Parent != parentIndex;
            for (var at = original.Parent; at >= 0 && at != parentIndex; at = sourceBones[at].Parent)
            {
                if (!UnitScale(sourceBones[at].Scale))
                    throw new InvalidDataException($"Retarget collapsed source parent requires unit bind scale: {sourceBones[at].Name}.");
                if (!tracks.TryGetValue(at, out var ancestorTrack)) continue;
                if (ancestorTrack.Scale.Any(k => k.Value.Any(v => !float.IsFinite(v) || v <= 0 || v > 10)))
                    throw new InvalidDataException($"Retarget collapsed source animation scale is invalid: {sourceBones[at].Name}.");
                compensatedCollapse |= ancestorTrack.Scale.Any(k => !UnitScale(k.Value));
            }
            var anchorTrack = parentIndex < 0 ? null : tracks.GetValueOrDefault(parentIndex);
            compensatedCollapse |= (hasCollapsedSource || hasInsertedParent) && anchorTrack is not null
                && anchorTrack.Scale.Any(k => !UnitScale(k.Value));
            if (compensatedCollapse)
            {
                if (parentIndex < 0 || !UnitScale(targetBones[targetParent].Scale)
                    || !UnitScale(sourceBones[parentIndex].Scale)
                    || anchorTrack is not null && anchorTrack.Scale.Any(k => k.Value.Any(v => !float.IsFinite(v) || v <= 0 || v > 10)))
                    throw new InvalidDataException($"Retarget collapsed source scale requires matching unit-bind mapped anchors for {bone.Name}.");
                if (!unitScale[targetParent])
                {
                    if (branch >= 0 || !hasInsertedParent && !bone.CompensateScale)
                        throw new InvalidDataException($"Retarget collapsed source scale requires a compensated target chain under an animated anchor: {bone.Name}.");
                    for (var at = bone.Parent; at != targetParent; at = targetBones[at].Parent)
                        if (!targetBones[at].CompensateScale)
                            throw new InvalidDataException("Retarget collapsed source scale requires compensated inserted target parents.");
                }
                for (var at = source[name]; at != parentIndex; at = sourceBones[at].Parent)
                    if (!sourceBones[at].CompensateScale)
                        throw new InvalidDataException("Retarget collapsed source scale requires every source child to compensate scale.");
            }
            Matrix4x4 Relative(Matrix4x4[] world) => world[source[name]] * (parentIndex < 0 ? Matrix4x4.Identity : Inverse(world[parentIndex]));
            Matrix4x4 CompensatedSourcePose(int frame)
            {
                var local = Matrix4x4.Identity;
                for (var at = source[name]; at != parentIndex; at = sourceBones[at].Parent)
                {
                    var segment = sourceBones[at]; var segmentTrack = tracks.GetValueOrDefault(at);
                    var position = segmentTrack is null ? segment.Translation : Sample(segmentTrack.Translation, frame, segment.Translation, false);
                    var rotation = segmentTrack is null ? segment.Rotation : Sample(segmentTrack.Rotation, frame, segment.Rotation, true);
                    var parent = sourceBones[segment.Parent]; var parentTrack = tracks.GetValueOrDefault(segment.Parent);
                    var parentScale = parentTrack is null ? parent.Scale : Sample(parentTrack.Scale, frame, parent.Scale, false);
                    // Inverse parent scale cancels its deformation, while
                    // inverse(S) * T(offset) * S = T(offset * S).
                    // Own scale stays in its existing independent output curve.
                    position = position.Select((v, c) => v * parentScale[c]).ToArray();
                    local *= Matrix4x4.CreateFromQuaternion(Rotation(rotation)) * Matrix4x4.CreateTranslation(Vector(position));
                }
                var anchorScale = anchorTrack is null ? sourceBones[parentIndex].Scale
                    : Sample(anchorTrack.Scale, frame, sourceBones[parentIndex].Scale, false);
                // The destination's native inverse-parent scale supplies this
                // final factor: R * T(p) * inverse(S) = R * inverse(S) * T(p / S).
                local.Translation /= Vector(anchorScale);
                return local;
            }
            // A direct source parent already has the required local transform.
            // Avoid multiplying through its ancestors and cancelling them again:
            // that introduces roundoff into otherwise constant translation curves.
            var directSourceParent = parentIndex == original.Parent;
            Matrix4x4 SourceLocal(int? frame)
            {
                var position = frame is null || track is null ? original.Translation : Sample(track.Translation, frame.Value, original.Translation, false);
                var rotation = frame is null || track is null ? original.Rotation : Sample(track.Rotation, frame.Value, original.Rotation, true);
                return Matrix4x4.CreateFromQuaternion(Rotation(rotation)) * Matrix4x4.CreateTranslation(Vector(position));
            }
            var originalRest = directSourceParent ? SourceLocal(null) : Relative(rest);
            var targetRest = Matrix4x4.CreateFromQuaternion(Rotation(bone.Rotation))
                * Matrix4x4.CreateTranslation(Vector(bone.Translation)) * insertedParent;
            var correction = (hasInsertedParent ? RotationMatrix(targetRest) : Matrix4x4.CreateFromQuaternion(Rotation(bone.Rotation)))
                * Inverse(RotationMatrix(originalRest));
            // A qualified fixed offset transfers rotation while retaining the target
            // translation exactly. It requires a direct mapped parent, unit scale
            // and every source translation key to equal bind, so no displacement
            // is discarded or amplified. Moving offsets still use the ratio guard.
            var fixedTranslation = fixedTranslations?.Contains(bone.Name) == true;
            if (fixedTranslation && (hasInsertedParent || branch >= 0 || parentIndex != original.Parent
                || original.Scale.Any(v => v != 1) || bone.Scale.Any(v => v != 1)
                || original.Translation.Any(v => !float.IsFinite(v)) || bone.Translation.Any(v => !float.IsFinite(v))
                || track is not null && (track.Translation.Any(k => !k.Value.SequenceEqual(original.Translation))
                    || track.Scale.Any(k => !k.Value.SequenceEqual(original.Scale)))))
                throw new InvalidDataException("Retarget fixed translation requires a constant unit-scale source offset and direct parent.");
            var length = originalRest.Translation.Length();
            // A moving pivot may have a near-zero bind offset unrelated to body
            // proportions. An explicit descendant segment supplies its displacement
            // scale without dropping translation keys or relaxing the ratio budget.
            var ratio = referenceRatios is not null && referenceRatios.TryGetValue(bone.Name, out var referenceRatio)
                ? referenceRatio : fixedTranslation || length < .00001f ? 1 : targetRest.Translation.Length() / length;
            if (!float.IsFinite(ratio) || ratio > 10)
                throw new InvalidDataException($"Retarget translation scale is invalid for {bone.Name}: {ratio:R}.");
            var rotations = new List<byte>(); var translations = new List<byte>(); var scales = new List<byte>();
            for (var frame = 0; frame < clip.Frames; frame++)
            {
                var animatedScale = track is null ? original.Scale : Sample(track.Scale, frame, original.Scale, false);
                var scale = animatedScale.Select((v, c) => bone.Scale[c] * v / original.Scale[c]).ToArray();
                if (scale.Any(v => !float.IsFinite(v) || v < 0 || v > 10))
                    throw new InvalidDataException("Retarget scale is invalid.");
                scales.AddRange(ModelTableBuilder.Floats(scale));
                var originalPose = compensatedCollapse ? CompensatedSourcePose(frame)
                    : directSourceParent ? SourceLocal(frame) : Relative(poses[frame]);
                var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(correction * RotationMatrix(originalPose)));
                var delta = originalPose.Translation - originalRest.Translation;
                var position = fixedTranslation ? targetRest.Translation
                    : targetRest.Translation + Vector3.TransformNormal(delta, correction) * ratio;
                if (hasInsertedParent)
                {
                    // Express the mapped ancestor-relative pose beneath the target's
                    // intervening bind joints. Their transforms remain authored constants.
                    // A qualified changed branch uses the already transferred
                    // parent pose, keeping its source ancestor-relative motion
                    // independent of the target's moving intermediary joints.
                    var parentPose = branch < 0 ? insertedParent : branchWorld![frame][bone.Parent] * Inverse(branchWorld[frame][branch]);
                    if (compensatedCollapse && !unitScale[targetParent])
                    {
                        var anchorScale = Vector(anchorTrack is null ? sourceBones[parentIndex].Scale
                            : Sample(anchorTrack.Scale, frame, sourceBones[parentIndex].Scale, false));
                        // Both ancestor-relative physical poses end in inverse(S).
                        // Cancel those factors before solving the child's rigid local
                        // pose. Only the top inserted joint has a scaled parent;
                        // its authored offset is therefore weighted by anchor S.
                        position *= anchorScale;
                        parentPose.Translation += insertedAnchorOffset * (anchorScale - Vector3.One);
                    }
                    var local = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position) * Inverse(parentPose);
                    rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(RotationMatrix(local)));
                    position = local.Translation;
                }
                rotations.AddRange(PackRotation([rotation.X, rotation.Y, rotation.Z, rotation.W]));
                translations.AddRange(ModelTableBuilder.Floats([position.X, position.Y, position.Z]));
                Record(frame, rotation, position, scale);
            }
            Curve(1, scales.ToArray(), clip.Frames); Curve(3, rotations.ToArray(), clip.Frames); Curve(5, translations.ToArray(), clip.Frames);
        }
        var materials = writer.Table(1); writer.Reference(root, 2, materials); writer.Vector(materials, 0, 0);
        var result = writer.Finish(root);
        _ = PreviewRigReader.Animation(new(result), "retarget", targetBones);
        return result;

        static void RequireHierarchy(PreviewBone[] bones)
        {
            // World transforms and ancestry walks require the same parent-first
            // ordering enforced by the source and native skeleton readers.
            for (var i = 0; i < bones.Length; i++)
                if (bones[i].Parent < -1 || bones[i].Parent >= i)
                    throw new InvalidDataException("Retarget skeleton hierarchy is invalid.");
        }
        static Vector3 Vector(float[] v) => new(v[0], v[1], v[2]);
        static bool UnitScale(float[] scale) => scale.All(v => float.IsFinite(v) && MathF.Abs(v - 1) <= .00001f);
        static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var inverse) ? inverse : throw new InvalidDataException("Retarget transform is singular.");
        static Matrix4x4 RotationMatrix(Matrix4x4 m) => Matrix4x4.Decompose(m, out _, out var q, out _)
            ? Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q)) : throw new InvalidDataException("Retarget transform cannot be decomposed.");
        static Quaternion Rotation(float[] q) => Quaternion.Normalize(new(q[0], q[1], q[2], q[3]));
        static float[] Sample(PreviewKey[] keys, float frame, float[] fallback, bool rotation)
        {
            if (keys.Length == 0) return fallback;
            if (frame <= keys[0].Frame) return keys[0].Value;
            for (var i = 1; i < keys.Length; i++)
            {
                if (frame > keys[i].Frame) continue;
                var a = keys[i - 1]; var b = keys[i]; var t = (frame - a.Frame) / (b.Frame - a.Frame);
                if (rotation) { var q = Quaternion.Slerp(Rotation(a.Value), Rotation(b.Value), t); return [q.X, q.Y, q.Z, q.W]; }
                return a.Value.Select((v, c) => v + (b.Value[c] - v) * t).ToArray();
            }
            return keys[^1].Value;
        }
    }
}
