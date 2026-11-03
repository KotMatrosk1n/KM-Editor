// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.ZA;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static bool HasCalibratedCampForm(int species, int form) =>
            (species, form) is (26, 2) or (36, 1) or (65, 1);

        // These three rigs have genuinely different face and secondary controls.
        // Transfer the shared body through the hierarchy retargeter, and calibrate
        // the remaining controls from paired, authored base/Mega action poses.
        // Each output retains the exact Camp timeline, not the calibration timeline.
        private void AddCalibratedCampMotions(PreviewScene target,
            Dictionary<string, (byte[] Skeleton, PreviewClip Decoded)> clips,
            IEnumerable<string> required, int species, int form)
        {
            if (!HasCalibratedCampForm(species, form))
                throw new InvalidDataException("Camp pose calibration is unavailable for this identity.");
            var targetStem = Profile(new(species, form)).ResourceStem!;
            var stem = $"pm{species:0000}_00_00";
            var prefix = $"ik_pokemon/data/{stem[..6]}/{stem}/{stem}";
            var source = new TrinityPreviewReader(Donor, topOriginMaterialUv: true).Load(prefix + ".trmdl");
            if (source.Rig.Warnings.Length != 0)
                throw new InvalidDataException("Camp calibration source rig is unsupported.");
            var graph = ModelAssetGraph.Read([(prefix + "_base.tracn", (string?)null)], (path, _) => Donor(path));
            var originals = graph.Where(r => r.Id.EndsWith(".tranm", StringComparison.Ordinal))
                .ToDictionary(r => int.Parse(Path.GetFileNameWithoutExtension(r.Id)[(stem.Length + 1)..].Split('_')[0]),
                    r => ReadCampSource(PreviewRigReader.Animation(new(r.Bytes), r.Id, source.Rig.Bones),
                        Donor(Path.ChangeExtension(r.Id, ".tracm").Replace('\\', '/'))));
            var bank = new CampPoseBank(source.Rig.Bones, target.Rig.Bones, originals,
                clips.ToDictionary(p => int.Parse(p.Key), p => p.Value.Decoded),
                CampMaterialDefaults(Donor(prefix + ".trmtr")), species, stem, targetStem);
            foreach (var code in required.Distinct().Where(code => !clips.ContainsKey(code)))
            {
                var numeric = int.Parse(code);
                if (!originals.TryGetValue(numeric, out var original)
                    && !originals.TryGetValue(numeric % 10000, out original))
                    throw new InvalidDataException("Camp calibration action is missing: " + code);
                var adapted = bank.Transfer(original);
                var skeleton = SwShAnimationWriter.Retarget(adapted, target.Rig.Bones, target.Rig.Bones,
                    target.Rig.Bones.ToDictionary(b => b.Name, b => b.Name, StringComparer.Ordinal));
                var decoded = PreviewRigReader.Animation(new(skeleton), original.Id, target.Rig.Bones)
                    with { Materials = adapted.Materials, Visibility = adapted.Visibility };
                clips.Add(code, (skeleton, decoded));
            }
        }

        private static PreviewClip ReadCampSource(PreviewClip clip, byte[] bytes)
        {
            var data = new ModelBuffer(bytes);
            var rate = data.Value(data.Table(data.Root, 0), 2, (uint)clip.Rate);
            if (rate == 0) throw new InvalidDataException("Camp source material rate is invalid.");
            return clip with { Materials = data.Tables(data.Root, 1, 512)
                .Select(track => data.Table(track, 4)).Where(timeline => timeline != 0)
                .SelectMany(timeline => data.Tables(timeline, 2, 128))
                .SelectMany(material => ZaSwShEyelidMaterial.ReadTracks(data, material, clip.Rate / (float)rate)).ToArray() };
        }

        private static Dictionary<(string Material, string Parameter), float[]> CampMaterialDefaults(byte[] bytes)
        {
            var data = new ModelBuffer(bytes);
            var result = new Dictionary<(string, string), float[]>();
            foreach (var material in data.Tables(data.Root, 1, 128))
            {
                var name = data.Text(material, 0)!;
                foreach (var parameter in data.Tables(material, 4, 256))
                    result.Add((name, data.Text(parameter, 0)!), [data.Field(parameter, 1) is var at && at != 0 ? data.Float(at) : 0]);
                foreach (var parameter in data.Tables(material, 7, 256))
                {
                    var at = data.Field(parameter, 1);
                    result.Add((name, data.Text(parameter, 0)!), Enumerable.Range(0, 4).Select(c => data.Float(at + c * 4)).ToArray());
                }
            }
            return result;
        }

        private sealed class CampPoseBank
        {
            private readonly PreviewBone[] source, target;
            private readonly Dictionary<string, string> body = new(StringComparer.Ordinal);
            private readonly List<PoseGroup> groups = [];
            private readonly Sample[] samples;
            private readonly IReadOnlyDictionary<(string Material, string Parameter), float[]> materialDefaults;
            private readonly (string Material, string Parameter, int Channels)[] sourceMaterials;
            private readonly IReadOnlyDictionary<(string Material, string Parameter), PreviewMaterialTrack> targetDefaults;
            private sealed record Sample(PreviewClip Source, float SourceFrame, PreviewClip Target, float TargetFrame);
            private sealed record PoseGroup(int[] SourceBones, int[] TargetBones, string Surface, float[][] Features);

            public CampPoseBank(PreviewBone[] source, PreviewBone[] target,
                IReadOnlyDictionary<int, PreviewClip> originals, IReadOnlyDictionary<int, PreviewClip> authored,
                IReadOnlyDictionary<(string Material, string Parameter), float[]> materialDefaults,
                int species, string sourceStem, string targetStem)
            {
                this.source = source; this.target = target;
                this.materialDefaults = materialDefaults;
                sourceMaterials = originals.Values.SelectMany(c => c.Materials).GroupBy(t => (t.Material, t.Parameter))
                    .Select(g => (g.Key.Material, g.Key.Parameter, g.First().Channels.Length)).OrderBy(p => p.Material).ThenBy(p => p.Parameter).ToArray();
                if (sourceMaterials.Any(p => !materialDefaults.TryGetValue((p.Material, p.Parameter), out var value) || value.Length != p.Channels))
                    throw new InvalidDataException("Camp source material reset is incomplete.");
                targetDefaults = authored.OrderBy(p => p.Key).SelectMany(p => p.Value.Materials)
                    .GroupBy(t => (t.Material, t.Parameter)).ToDictionary(g => g.Key, g => g.First());
                var byName = source.Select((b, i) => (b.Name, Index: i)).ToDictionary(p => p.Name, p => p.Index);
                foreach (var bone in target)
                {
                    if (Face(bone.Name) || Secondary(bone.Name)) continue;
                    var name = bone.Name == targetStem ? sourceStem
                        : species == 36 && bone.Name == "spine" ? "spine_01"
                        : species == 65 && bone.Name == "spine_02" ? "spine" : bone.Name;
                    if (!byName.TryGetValue(name, out var index)) continue;
                    var parent = bone.Parent;
                    while (parent >= 0 && !body.ContainsKey(target[parent].Name)) parent = target[parent].Parent;
                    if (parent >= 0)
                    {
                        var ancestor = byName[body[target[parent].Name]];
                        var at = source[index].Parent;
                        while (at >= 0 && at != ancestor) at = source[at].Parent;
                        if (at < 0) continue;
                    }
                    body.Add(bone.Name, name);
                }
                foreach (var name in new[] { "origin", "waist", "head", "hips", "left_arm_01", "right_arm_01", "left_leg_01", "right_leg_01" })
                    if (!body.ContainsKey(name)) throw new InvalidDataException("Camp body calibration is incomplete: " + name);
                var pairs = new List<Sample>();
                foreach (var (code, clip) in authored.OrderBy(p => p.Key))
                {
                    if (code >= 10000 && authored.ContainsKey(code % 10000)) continue;
                    if (!originals.TryGetValue(code, out var original)
                        && !originals.TryGetValue(code % 10000, out original)) continue;
                    // Keep every facial calibration frame; sample other authored
                    // actions at two-frame intervals with both endpoints included.
                    var step = code % 10000 == 8000 ? 1 : 2;
                    for (var f = 0; f < original.Frames; f += step)
                        pairs.Add(new(original, f, clip, f * (clip.Frames - 1f) / Math.Max(1, original.Frames - 1)));
                    if ((original.Frames - 1) % step != 0)
                        pairs.Add(new(original, original.Frames - 1, clip, clip.Frames - 1));
                }
                samples = pairs.ToArray();
                if (samples.Length < 500 || !authored.Keys.Any(code => code % 10000 == 8000))
                    throw new InvalidDataException("Camp pose calibration lacks authored coverage.");
                foreach (var side in new[] { "left", "right" })
                    AddGroup(source.Select((b, i) => (b, i)).Where(p => Eye(p.b.Name, side)).Select(p => p.i).ToArray(),
                        target.Select((b, i) => (b, i)).Where(p => Eye(p.b.Name, side)).Select(p => p.i).ToArray(), side);
                AddGroup(source.Select((b, i) => (b, i)).Where(p => Mouth(p.b.Name)).Select(p => p.i).ToArray(),
                    target.Select((b, i) => (b, i)).Where(p => Mouth(p.b.Name)).Select(p => p.i).ToArray(), "mouth");
                var assigned = groups.SelectMany(g => g.TargetBones).ToHashSet();
                var extra = target.Select((b, i) => (b, i)).Where(p => p.b.Joint >= 0 || Secondary(p.b.Name))
                    .Where(p => !body.ContainsKey(p.b.Name) && !assigned.Contains(p.i)).Select(p => p.i).ToArray();
                string[] controls = ["waist", "head", "hips", "left_arm_01", "left_arm_02", "right_arm_01", "right_arm_02", "left_leg_01", "right_leg_01"];
                AddGroup(controls.Select(name => byName[name]).ToArray(), extra, "body");
                var covered = groups.SelectMany(g => g.TargetBones).ToHashSet();
                if (target.Select((b, i) => (b, i)).Any(p => p.b.Joint >= 0 && !body.ContainsKey(p.b.Name) && !covered.Contains(p.i)))
                    throw new InvalidDataException("Camp calibration leaves a skin control unassigned.");
            }

            private void AddGroup(int[] input, int[] output, string surface)
            {
                if (output.Length == 0 && surface == "mouth") return;
                if (input.Length == 0) throw new InvalidDataException("Camp pose group has no source controls.");
                groups.Add(new(input, output, surface, samples.Select(s => Feature(s.Source, s.SourceFrame, input, surface)).ToArray()));
            }

            public PreviewClip Transfer(PreviewClip clip)
            {
                var skeleton = SwShAnimationWriter.Retarget(clip, source, target, body);
                var retargeted = PreviewRigReader.Animation(new(skeleton), clip.Id, target);
                var result = retargeted.Tracks.ToDictionary(t => t.Bone);
                var materials = new List<PreviewMaterialTrack>();
                var visibility = new List<PreviewVisibility>();
                foreach (var group in groups)
                {
                    var nearest = Enumerable.Range(0, clip.Frames).Select(frame => Neighbors(Feature(clip, frame, group.SourceBones, group.Surface), group.Features)).ToArray();
                    foreach (var bone in group.TargetBones)
                    {
                        var scales = new PreviewKey[clip.Frames]; var rotations = new PreviewKey[clip.Frames]; var translations = new PreviewKey[clip.Frames];
                        for (var f = 0; f < clip.Frames; f++)
                        {
                            var scale = Vector3.Zero; var position = Vector3.Zero; var rotation = Vector4.Zero;
                            Quaternion? first = null;
                            foreach (var (index, weight) in nearest[f])
                            {
                                var sample = samples[index]; var pose = Pose(sample.Target, sample.TargetFrame, target, bone);
                                scale += pose.Scale * weight; position += pose.Position * weight;
                                var q = pose.Rotation; first ??= q;
                                if (Quaternion.Dot(first.Value, q) < 0) q = new(-q.X, -q.Y, -q.Z, -q.W);
                                rotation += new Vector4(q.X, q.Y, q.Z, q.W) * weight;
                            }
                            var normalized = Quaternion.Normalize(new(rotation.X, rotation.Y, rotation.Z, rotation.W));
                            scales[f] = new(f, [scale.X, scale.Y, scale.Z]);
                            rotations[f] = new(f, [normalized.X, normalized.Y, normalized.Z, normalized.W]);
                            translations[f] = new(f, [position.X, position.Y, position.Z]);
                        }
                        result[bone] = new(bone, Stable(scales), Stable(rotations), Stable(translations));
                    }
                    // Sample only the Mega's already converted material contract.
                    // Base UV coordinates never address the Mega's different atlas.
                    foreach (var (identity, reset) in targetDefaults.Where(p => Surface(p.Key.Material) == group.Surface))
                    {
                        var channels = Enumerable.Range(0, reset.Channels.Length).Select(c => Enumerable.Range(0, clip.Frames)
                            .Select(f => new PreviewKey(f, [Blend(nearest[f], index => {
                                var sample = samples[index];
                                var track = sample.Target.Materials.SingleOrDefault(t => t.Material == identity.Material && t.Parameter == identity.Parameter);
                                return Value(track?.Channels[c], sample.TargetFrame, reset.Channels[c][0].Value, false)[0];
                            })])).ToArray()).Select(Stable).ToArray();
                        materials.Add(new(identity.Material, identity.Parameter, channels));
                    }
                    foreach (var name in samples.SelectMany(s => s.Target.Visibility).Select(v => v.Name).Distinct().Where(n => Surface(n) == group.Surface))
                        visibility.Add(new(name, Enumerable.Range(0, clip.Frames).Select(f => new PreviewKey(f,
                            [nearest[f].Sum(n => {
                                var sample = samples[n.Index];
                                var keys = sample.Target.Visibility.SingleOrDefault(v => v.Name == name)?.Keys;
                                return n.Weight * (keys?.LastOrDefault(k => k.Frame <= sample.TargetFrame)?.Value[0] ?? 1);
                            }) >= .5f ? 1 : 0])).ToArray()));
                }
                if (materials.Count != targetDefaults.Count)
                    throw new InvalidDataException("Camp target surface mapping is incomplete.");
                return retargeted with { Tracks = result.Values.OrderBy(t => t.Bone).ToArray(), Materials = materials.ToArray(), Visibility = visibility.ToArray() };
            }

            // Normalize in double precision so constant palette alpha and color
            // endpoints remain exact despite rounded neighbor weights.
            private static float Blend((int Index, float Weight)[] neighbors, Func<int, float> value)
            {
                var anchor = value(neighbors[0].Index);
                double delta = 0, total = 0;
                foreach (var neighbor in neighbors)
                {
                    delta += neighbor.Weight * (double)(value(neighbor.Index) - anchor);
                    total += neighbor.Weight;
                }
                return (float)(anchor + delta / total);
            }

            // Normalized blends of identical authored values can differ by a
            // few float ULPs. Preserve those constant channels exactly instead
            // of allocating a full animation curve for arithmetic noise.
            private static PreviewKey[] Stable(PreviewKey[] keys)
            {
                if (keys.Length < 2) return keys;
                var first = keys[0].Value;
                if (keys.All(k => k.Value.Select((v, c) => MathF.Abs(v - first[c]) <= 1e-6f * MathF.Max(1, MathF.Abs(first[c]))).All(v => v)))
                    return [new(0, first)];
                return keys;
            }

            private float[] Feature(PreviewClip clip, float frame, int[] bones, string surface)
            {
                var feature = new List<float>(bones.Length * 7);
                foreach (var index in bones)
                {
                    var bone = source[index]; var pose = Pose(clip, frame, source, index);
                    var bind = new Quaternion(bone.Rotation[0], bone.Rotation[1], bone.Rotation[2], bone.Rotation[3]);
                    var delta = Quaternion.Normalize(Quaternion.Inverse(bind) * pose.Rotation);
                    if (delta.W < 0) delta = new(-delta.X, -delta.Y, -delta.Z, -delta.W);
                    var scale = MathF.Max(.02f, new Vector3(bone.Translation[0], bone.Translation[1], bone.Translation[2]).Length());
                    var translation = (pose.Position - new Vector3(bone.Translation[0], bone.Translation[1], bone.Translation[2])) / scale;
                    feature.AddRange([delta.X, delta.Y, delta.Z, translation.X, translation.Y, translation.Z,
                        (pose.Scale.X + pose.Scale.Y + pose.Scale.Z) / 3 - 1]);
                }
                foreach (var (material, parameter, channels) in sourceMaterials.Where(p => Surface(p.Material) == surface))
                {
                    var track = clip.Materials.SingleOrDefault(t => t.Material == material && t.Parameter == parameter);
                    var reset = materialDefaults[(material, parameter)];
                    for (var c = 0; c < channels; c++)
                        feature.Add(Value(track?.Channels[c], frame, [reset[c]], false)[0] - reset[c]);
                }
                return feature.ToArray();
            }

            private static string Surface(string name) => name.Contains("eye", StringComparison.Ordinal)
                ? name.StartsWith("l_", StringComparison.Ordinal) || name.Contains("left", StringComparison.Ordinal) ? "left" : "right"
                : "body";

            private static (int Index, float Weight)[] Neighbors(float[] query, float[][] bank)
            {
                var nearest = new List<(int Index, float Distance)>();
                for (var i = 0; i < bank.Length; i++)
                {
                    var distance = 0f;
                    for (var c = 0; c < query.Length; c++) { var delta = query[c] - bank[i][c]; distance += delta * delta; }
                    var at = nearest.FindIndex(p => distance < p.Distance);
                    if (at < 0) at = nearest.Count;
                    if (at >= 8) continue;
                    nearest.Insert(at, (i, distance));
                    if (nearest.Count > 8) nearest.RemoveAt(8);
                }
                var weights = nearest.Select(p => 1 / MathF.Pow(.001f + p.Distance, 2)).ToArray();
                var total = weights.Sum();
                return nearest.Select((p, i) => (p.Index, weights[i] / total)).ToArray();
            }

            private static (Vector3 Scale, Quaternion Rotation, Vector3 Position) Pose(PreviewClip clip, float frame, PreviewBone[] bones, int index)
            {
                var bone = bones[index]; var track = clip.Tracks.FirstOrDefault(t => t.Bone == index);
                var s = Value(track?.Scale, frame, bone.Scale, false);
                var r = Value(track?.Rotation, frame, bone.Rotation, true);
                var p = Value(track?.Translation, frame, bone.Translation, false);
                return (new(s[0], s[1], s[2]), Quaternion.Normalize(new(r[0], r[1], r[2], r[3])), new(p[0], p[1], p[2]));
            }

            private static float[] Value(PreviewKey[]? keys, float frame, float[] fallback, bool rotation)
            {
                if (keys is null || keys.Length == 0) return fallback;
                if (frame <= keys[0].Frame) return keys[0].Value;
                var i = Array.FindIndex(keys, k => k.Frame >= frame);
                if (i < 0) return keys[^1].Value;
                var a = keys[i - 1]; var b = keys[i]; var t = (frame - a.Frame) / (b.Frame - a.Frame);
                if (rotation)
                {
                    var q = Quaternion.Slerp(new(a.Value[0], a.Value[1], a.Value[2], a.Value[3]), new(b.Value[0], b.Value[1], b.Value[2], b.Value[3]), t);
                    return [q.X, q.Y, q.Z, q.W];
                }
                return a.Value.Select((v, c) => v + (b.Value[c] - v) * t).ToArray();
            }

            private static bool Eye(string name, string side) => name.StartsWith(side + "_", StringComparison.Ordinal)
                && (name.Contains("eye", StringComparison.Ordinal));
            private static bool Mouth(string name) => name.Contains("jaw", StringComparison.Ordinal) || name.Contains("teeth", StringComparison.Ordinal);
            private static bool Face(string name) => name.Contains("eye", StringComparison.Ordinal) || Mouth(name);
            private static bool Secondary(string name) => name.Contains("tail", StringComparison.Ordinal) || name.Contains("feeler", StringComparison.Ordinal)
                || name.Contains("cloth", StringComparison.Ordinal) || name.Contains("wing", StringComparison.Ordinal) || name.Contains("horn", StringComparison.Ordinal)
                || name.EndsWith("_sub", StringComparison.Ordinal) || name.EndsWith("_roll", StringComparison.Ordinal);
        }
    }
}
